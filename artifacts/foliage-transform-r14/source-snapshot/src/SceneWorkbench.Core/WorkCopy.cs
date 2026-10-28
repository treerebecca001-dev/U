using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SceneWorkbench.Core;

public enum CopyState { Copying, Complete, Failed }

public sealed record WorkCopyInfo(
    int SchemaVersion,
    string SourceRoot,
    string WorkingRoot,
    CopyState State,
    DateTimeOffset CreatedUtc,
    int FileCount,
    long TotalBytes,
    bool IncludesPackageCache,
    string? ManifestSha256);

public sealed record CopyFileRecord(string Path, long Length, long LastWriteUtcTicks, string Sha256);
public sealed record CopyProgress(int FilesDone, int FilesTotal, long BytesDone, long BytesTotal, string CurrentPath);
public sealed record CopyVerification(int FilesChecked, int SourceFilesChecked, IReadOnlyList<string> Differences)
{
    public bool IsValid => Differences.Count == 0;
}

public sealed class WorkCopy : IProjectWriteAccess
{
    public const string MetadataDirectory = ".scene-workbench";
    public const string InfoPath = MetadataDirectory + "/workcopy.json";
    public const string ManifestPath = MetadataDirectory + "/copy-manifest.jsonl";
    private static readonly JsonSerializerOptions CompactJson = new(JsonFile.Options) { WriteIndented = false };

    public ProjectSession Project { get; }
    public WorkCopyInfo Info { get; }

    private WorkCopy(ProjectSession project, WorkCopyInfo info)
    {
        Project = project;
        Info = info;
    }

    public static WorkCopy Open(string root)
    {
        var project = ProjectSession.Open(root);
        if (!System.IO.File.Exists(project.Resolve(InfoPath)))
            throw new InvalidOperationException("此專案不是本工具建立的工作複本；來源專案維持唯讀。");
        var info = JsonFile.Read<WorkCopyInfo>(project.Resolve(InfoPath));
        if (info.SchemaVersion != 1 || info.State != CopyState.Complete)
            throw new InvalidDataException("複本尚未完成或其版本不受支援，禁止回寫。");
        if (!SafePath.Canonical(info.WorkingRoot).Equals(project.Root, SafePath.Comparison))
            throw new InvalidDataException("複本標記與目前路徑不一致。");
        SafePath.RequireDisjoint(info.SourceRoot, project.Root);
        var manifest = project.Resolve(ManifestPath);
        if (info.ManifestSha256 is null || !Hashing.File(manifest).Equals(info.ManifestSha256, StringComparison.Ordinal))
            throw new InvalidDataException("複本清單的雜湊不符，禁止回寫。");
        return new WorkCopy(project, info);
    }

    public string ResolveEditable(string relative)
    {
        var normalized = relative.Replace('\\', '/');
        if (!ProjectSession.RequiredRoots.Any(name => normalized.StartsWith(name + "/", StringComparison.Ordinal)))
            throw new InvalidOperationException("只能修改工作複本內的 Assets、Packages 與 ProjectSettings。");
        var path = Project.Resolve(relative);
        if (SafePath.IsWithin(Info.SourceRoot, path))
            throw new InvalidOperationException("禁止修改來源專案。");
        return path;
    }

    public static async Task<WorkCopy> CreateAsync(
        string sourceRoot,
        string destinationRoot,
        bool includePackageCache = true,
        IProgress<CopyProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var source = ProjectSession.Open(sourceRoot);
        destinationRoot = SafePath.Canonical(destinationRoot);
        SafePath.RequireDisjoint(source.Root, destinationRoot);
        if (System.IO.File.Exists(destinationRoot) || Directory.Exists(destinationRoot))
            throw new IOException("目的地必須是尚未存在的新資料夾；不覆蓋或續接既有資料。");

        var roots = ProjectSession.RequiredRoots.ToList();
        if (includePackageCache && Directory.Exists(source.Resolve("Library/PackageCache")))
            roots.Add("Library/PackageCache");
        var files = source.EnumerateSourceFiles(includePackageCache, cancellationToken)
            .Select(path => new FileInfo(path)).OrderBy(file => file.FullName, SafePath.Comparer).ToArray();
        var totalBytes = files.Sum(file => file.Length);
        var directories = roots.SelectMany(name => SafePath.EnumerateDirectories(source.Resolve(name), cancellationToken)
            .Prepend(source.Resolve(name))).ToArray();
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(destinationRoot);
        var metadata = SafePath.Resolve(destinationRoot, MetadataDirectory);
        Directory.CreateDirectory(metadata);
        var info = new WorkCopyInfo(1, source.Root, destinationRoot, CopyState.Copying,
            DateTimeOffset.UtcNow, files.Length, totalBytes, roots.Contains("Library/PackageCache"), null);
        JsonFile.WriteNew(SafePath.Resolve(destinationRoot, InfoPath), info);
        var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        var completed = 0;
        long copiedBytes = 0;
        try
        {
            foreach (var directory in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(SafePath.Resolve(destinationRoot, SafePath.Relative(source.Root, directory)));
            }
            using (var manifestStream = new FileStream(SafePath.Resolve(destinationRoot, ManifestPath), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                using (var manifest = new StreamWriter(manifestStream, new UTF8Encoding(false), 65536, true))
                {
                    foreach (var file in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        SafePath.RejectLinks(file.FullName);
                        var relative = SafePath.Relative(source.Root, file.FullName);
                        var target = SafePath.Resolve(destinationRoot, relative);
                        var plannedLength = file.Length;
                        var plannedModified = file.LastWriteTimeUtc.Ticks;
                        file.Refresh();
                        var length = file.Length;
                        var modified = file.LastWriteTimeUtc.Ticks;
                        if (length != plannedLength || modified != plannedModified)
                            throw new IOException($"建立複本時來源已改動：{relative}");
                        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                        await using (var input = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length, FileOptions.SequentialScan))
                        await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, FileOptions.SequentialScan))
                        {
                            int count;
                            while ((count = await input.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
                            {
                                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                                hash.AppendData(buffer, 0, count);
                                copiedBytes += count;
                                progress?.Report(new CopyProgress(completed, files.Length, copiedBytes, totalBytes, relative));
                            }
                        }
                        file.Refresh();
                        if (file.Length != length || file.LastWriteTimeUtc.Ticks != modified)
                            throw new IOException($"複製時來源已改動，複本不列為完成：{relative}");
                        var checksum = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                        var record = new CopyFileRecord(relative, length, modified, checksum);
                        await manifest.WriteLineAsync(JsonSerializer.Serialize(record, CompactJson));
                        System.IO.File.SetLastWriteTimeUtc(target, new DateTime(modified, DateTimeKind.Utc));
                        completed++;
                        if (completed % 256 == 0)
                            await manifest.FlushAsync(cancellationToken);
                        progress?.Report(new CopyProgress(completed, files.Length, copiedBytes, totalBytes, relative));
                    }
                }
                manifestStream.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var currentFiles = source.EnumerateSourceFiles(includePackageCache, cancellationToken).ToHashSet(SafePath.Comparer);
            if (copiedBytes != totalBytes || currentFiles.Count != files.Length || files.Any(file => !currentFiles.Contains(file.FullName)))
                throw new IOException("建立複本期間來源檔案集合已改動，複本不列為完成。");
            info = info with { State = CopyState.Complete, ManifestSha256 = Hashing.File(SafePath.Resolve(destinationRoot, ManifestPath)) };
            JsonFile.WriteAtomic(SafePath.Resolve(destinationRoot, InfoPath), info);
            return Open(destinationRoot);
        }
        catch
        {
            JsonFile.WriteAtomic(SafePath.Resolve(destinationRoot, InfoPath), info with { State = CopyState.Failed });
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public IEnumerable<CopyFileRecord> ReadManifest() => ReadManifestCore(true);

    private IEnumerable<CopyFileRecord> ReadManifestCore(bool validatePhysicalPaths)
    {
        var paths = new HashSet<string>(SafePath.Comparer);
        foreach (var line in System.IO.File.ReadLines(Project.Resolve(ManifestPath)))
        {
            var record = JsonSerializer.Deserialize<CopyFileRecord>(line, CompactJson)
                ?? throw new InvalidDataException("複本清單含空白紀錄。");
            if (!paths.Add(record.Path))
                throw new InvalidDataException("複本清單有重複路徑。");
            var normalized = record.Path.Replace('\\', '/');
            if (!ProjectSession.RequiredRoots.Append("Library/PackageCache").Any(name => normalized.StartsWith(name + "/", StringComparison.Ordinal)))
                throw new InvalidDataException("複本清單含不允許的根目錄。");
            if (validatePhysicalPaths)
            {
                _ = Project.Resolve(record.Path);
                _ = SafePath.Resolve(Info.SourceRoot, record.Path);
            }
            else
            {
                // Verification checks each physical path immediately before reading it.
                // Parsing still rejects path traversal, alternate streams and invalid roots.
                _ = SafePath.ResolveLexical(Project.Root, record.Path);
                _ = SafePath.ResolveLexical(Info.SourceRoot, record.Path);
            }
            if (record.Length < 0 || record.Sha256 is null || record.Sha256.Length != 64 || !record.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("複本清單含無效的長度或雜湊。");
            yield return record;
        }
    }

    public CopyVerification Verify(bool compareSource, IProgress<CopyProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var differences = new ConcurrentBag<string>();
        var checkedFiles = 0;
        var sourceFiles = 0;
        long bytes = 0;
        var records = ReadManifestCore(false).ToArray();
        var manifestPaths = records.Select(record => record.Path).ToHashSet(SafePath.Comparer);
        var progressGate = new object();
        Parallel.ForEach(records, new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = 4 }, record =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Project.Resolve(record.Path);
            // Preserve source path protection even when source content comparison is disabled.
            var source = SafePath.Resolve(Info.SourceRoot, record.Path);
            if (!System.IO.File.Exists(target))
                differences.Add("複本缺檔：" + record.Path);
            else if (!Hashing.File(target).Equals(record.Sha256, StringComparison.Ordinal))
                differences.Add("複本內容不同：" + record.Path);
            if (compareSource)
            {
                if (!System.IO.File.Exists(source))
                    differences.Add("來源缺檔：" + record.Path);
                else if (!Hashing.File(source).Equals(record.Sha256, StringComparison.Ordinal))
                    differences.Add("來源已改動：" + record.Path);
            }
            lock (progressGate)
            {
                checkedFiles++;
                if (compareSource) sourceFiles++;
                bytes += record.Length;
                progress?.Report(new CopyProgress(checkedFiles, Info.FileCount, bytes, Info.TotalBytes, record.Path));
            }
        });
        if (checkedFiles != Info.FileCount || bytes != Info.TotalBytes)
            differences.Add("複本清單的檔案數或總大小不符。");
        foreach (var path in Project.EnumerateSourceFiles(Info.IncludesPackageCache, cancellationToken))
        {
            var relative = SafePath.Relative(Project.Root, path);
            if (!manifestPaths.Contains(relative)) differences.Add("複本新增檔案：" + relative);
        }
        if (compareSource)
        {
            foreach (var path in ProjectSession.Open(Info.SourceRoot).EnumerateSourceFiles(Info.IncludesPackageCache, cancellationToken))
            {
                var relative = SafePath.Relative(Info.SourceRoot, path);
                if (!manifestPaths.Contains(relative)) differences.Add("來源新增檔案：" + relative);
            }
        }
        return new CopyVerification(checkedFiles, sourceFiles, differences.Order(StringComparer.Ordinal).ToArray());
    }
}

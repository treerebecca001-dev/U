using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using SceneWorkbench.Core;

namespace SceneWorkbench.Unity;

public sealed class AssetIndex
{
    private static readonly Regex GuidLine = new(@"\Aguid:\s*([0-9a-fA-F]{32})\s*\z", RegexOptions.CultureInvariant);
    private readonly Dictionary<string, List<AssetEntry>> byGuid = new(StringComparer.Ordinal);
    public IReadOnlyList<AssetEntry> Entries { get; }
    public int DuplicateGuidCount => byGuid.Count(pair => pair.Value.Count > 1);

    private AssetIndex(List<AssetEntry> entries)
    {
        Entries = entries;
        foreach (var entry in entries)
        {
            if (!byGuid.TryGetValue(entry.Guid, out var list))
                byGuid.Add(entry.Guid, list = []);
            list.Add(entry);
        }
    }

    public AssetEntry? ResolveGuid(string guid) => byGuid.TryGetValue(guid.ToLowerInvariant(), out var entries) && entries.Count == 1 ? entries[0] : null;
    public bool ContainsGuid(string guid) => byGuid.ContainsKey(guid.ToLowerInvariant());
    internal static AssetIndex RestorePreviewIndex(IReadOnlyList<AssetEntry> entries, ISet<string> currentMetaPaths)
    {
        if (entries.Any(e => !currentMetaPaths.Contains(e.Path + ".meta") || !System.Text.RegularExpressions.Regex.IsMatch(e.Guid, @"\A[0-9a-f]{32}\z"))
            || entries.Select(e => e.Path).Distinct(StringComparer.Ordinal).Count() != entries.Count)
            throw new InvalidDataException("預覽索引快取與來源不符。");
        return new AssetIndex(entries.ToList());
    }

    public static AssetIndex Build(ProjectSession project, IReadOnlyList<string> files, ICollection<Diagnostic> diagnostics, CancellationToken cancellationToken = default, IProgress<string>? progress = null, CompatibilitySnapshot? compatibility = null)
    {
        var entries = new ConcurrentBag<AssetEntry>();
        var readDiagnostics = new ConcurrentBag<Diagnostic>();
        var existingFiles = files.ToHashSet(SafePath.Comparer);
        compatibility ??= Compatibility.Read(project);
        var packageRoots = compatibility.Packages.Where(package => package.MetadataPath is not null)
            .Select(package => package.MetadataPath![..^"package.json".Length]).ToHashSet(StringComparer.Ordinal);
        // Embedded packages can exist without a manifest/lock entry. Never index every stale cache version.
        foreach (var path in files)
        {
            var relative = SafePath.Relative(project.Root, path);
            if (relative.StartsWith("Packages/", StringComparison.Ordinal) && relative.EndsWith("/package.json", StringComparison.Ordinal)
                && relative.Count(character => character == '/') == 2)
                packageRoots.Add(relative[..^"package.json".Length]);
        }
        var hiddenDirectories = new Dictionary<string, bool>(SafePath.Comparer);
        bool HiddenDirectory(string path)
        {
            if (path.Equals(project.Root, SafePath.Comparison)) return false;
            if (hiddenDirectories.TryGetValue(path, out var hidden)) return hidden;
            hidden = (File.GetAttributes(path) & FileAttributes.Hidden) != 0 || HiddenDirectory(Path.GetDirectoryName(path)!);
            hiddenDirectories.Add(path, hidden);
            return hidden;
        }
        var metas = files.Where(path =>
        {
            if (!path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) return false;
            var relative = SafePath.Relative(project.Root, path[..^5]);
            string content;
            if (relative.StartsWith("Assets/", StringComparison.Ordinal)) content = relative["Assets/".Length..];
            else
            {
                var root = packageRoots.FirstOrDefault(root => relative.StartsWith(root, StringComparison.Ordinal));
                if (root is null) return false;
                content = relative[root.Length..];
            }
            // Unity 2021.3 import exclusions apply to the asset name, not the .meta suffix.
            // Keep all these files in inventories and full copies; exclude them only from GUID resolution.
            if (content.Split('/').Any(part => part.StartsWith('.') || part.EndsWith('~') || part.Equals("cvs", StringComparison.OrdinalIgnoreCase))
                ) return false;
            return !HiddenDirectory(Path.GetDirectoryName(path)!);
        }).ToArray();
        var completed = 0;
        Parallel.ForEach(metas, new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = 4 }, path =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string? guid = null;
                var folder = false;
                foreach (var line in File.ReadLines(path).Take(30))
                {
                    var match = GuidLine.Match(line);
                    if (match.Success) guid = match.Groups[1].Value.ToLowerInvariant();
                    if (line == "folderAsset: yes") folder = true;
                    if (guid is not null && line.Length > 0 && line[0] != ' ' && line.EndsWith("Importer:", StringComparison.Ordinal)) break;
                }
                var assetPath = path[..^5];
                var fileExists = existingFiles.Contains(assetPath);
                var directoryExists = !fileExists && Directory.Exists(assetPath);
                // Apply import exclusions before missing-resource/GUID diagnostics. A real Folder.tmp
                // remains importable; an explicitly declared missing folder still needs diagnosis.
                var ignored = directoryExists && (File.GetAttributes(assetPath) & FileAttributes.Hidden) != 0
                    || assetPath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) && (fileExists || !directoryExists && !folder);
                if (!ignored)
                {
                    if (guid is null)
                        readDiagnostics.Add(new Diagnostic("META_GUID_UNREADABLE", Severity.Blocking, "找不到有效的 meta GUID。", SafePath.Relative(project.Root, path)));
                    // Older Unity/package folder metas may contain only fileFormatVersion and guid.
                    // The on-disk kind remains authoritative even without folderAsset: yes.
                    else if (!fileExists && !directoryExists)
                        readDiagnostics.Add(new Diagnostic("META_ASSET_MISSING", Severity.Blocking, "meta 對應的資源不存在；保留 meta 並將引用維持未解析。", SafePath.Relative(project.Root, path)));
                    else if (folder && fileExists)
                        readDiagnostics.Add(new Diagnostic("META_ASSET_KIND_MISMATCH", Severity.Blocking, "meta 宣告為資料夾，但對應資源是檔案。", SafePath.Relative(project.Root, path)));
                    else
                        entries.Add(new AssetEntry(SafePath.Relative(project.Root, assetPath), guid, Path.GetExtension(assetPath).ToLowerInvariant(), directoryExists));
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                readDiagnostics.Add(new Diagnostic("META_READ_FAILED", Severity.Blocking, "meta 無法讀取：" + error.GetType().Name, SafePath.Relative(project.Root, path)));
            }
            var count = Interlocked.Increment(ref completed);
            if (count % 2000 == 0 || count == metas.Length) progress?.Report($"GUID 索引：{count:N0}/{metas.Length:N0}");
        });
        foreach (var diagnostic in readDiagnostics.OrderBy(item => item.Path, StringComparer.Ordinal)) diagnostics.Add(diagnostic);
        var index = new AssetIndex(entries.OrderBy(item => item.Path, StringComparer.Ordinal).ToList());
        foreach (var duplicates in index.byGuid.Where(pair => pair.Value.Count > 1))
            diagnostics.Add(new Diagnostic("DUPLICATE_GUID", Severity.Blocking,
                $"GUID {duplicates.Key} 對應多個資源：{string.Join(", ", duplicates.Value.Select(entry => entry.Path))}"));
        return index;
    }
}

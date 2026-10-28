namespace SceneWorkbench.Core;

public static class SafePath
{
    public static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    public static StringComparer Comparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static string Canonical(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static bool IsWithin(string root, string path)
    {
        root = Canonical(root);
        path = Canonical(path);
        return path.Equals(root, Comparison) || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, Comparison);
    }

    public static void RequireDisjoint(string first, string second)
    {
        if (IsWithin(first, second) || IsWithin(second, first))
            throw new InvalidOperationException("來源與目的地不可相同、互相包含或位於同一棵專案樹內。");
        RejectLinks(first);
        RejectLinks(second);
    }

    public static string Resolve(string root, string relative)
    {
        var path = ResolveLexical(root, relative);
        RejectLinks(path);
        return path;
    }

    internal static string ResolveLexical(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)
            || relative.Contains(':') || relative.Split('/', '\\').Any(segment => segment is ".." or "."))
            throw new InvalidOperationException($"不是安全的專案相對路徑：{relative}");
        var path = Canonical(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsWithin(root, path) || path.Equals(Canonical(root), Comparison))
            throw new InvalidOperationException("路徑超出專案範圍。");
        return path;
    }

    public static void RejectLinks(string path)
    {
        for (var current = Canonical(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            try
            {
                var attributes = System.IO.File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException($"不跟隨符號連結或 junction：{current}");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    public static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    public static IEnumerable<string> EnumerateFiles(string root, CancellationToken cancellationToken = default)
        => EnumerateEntries(root, false, cancellationToken);

    public static IEnumerable<string> EnumerateDirectories(string root, CancellationToken cancellationToken = default)
        => EnumerateEntries(root, true, cancellationToken);

    // Directory enumeration supplies cached timestamps/lengths. Preview caches can validate
    // their source inventory without reopening tens of thousands of individual .meta files.
    public static IEnumerable<FileSystemInfo> EnumerateMetadata(string root, CancellationToken cancellationToken = default)
    {
        RejectLinks(root); var pending = new Stack<string>(); pending.Push(Canonical(root));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var entry in new DirectoryInfo(pending.Pop()).EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("專案含有連結，禁止建立預覽索引：" + entry.FullName);
                if (entry is DirectoryInfo) pending.Push(entry.FullName);
                yield return entry;
            }
        }
    }

    private static IEnumerable<string> EnumerateEntries(string root, bool directories, CancellationToken cancellationToken)
    {
        RejectLinks(root);
        var pending = new Stack<string>();
        pending.Push(Canonical(root));
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            // Directory enumeration already returns attributes; avoid another filesystem query per entry.
            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = entry.Attributes;
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException($"專案含有連結，請先確認其實際位置：{entry.FullName}");
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry.FullName);
                    if (directories)
                        yield return entry.FullName;
                }
                else if (!directories)
                    yield return entry.FullName;
            }
        }
    }
}

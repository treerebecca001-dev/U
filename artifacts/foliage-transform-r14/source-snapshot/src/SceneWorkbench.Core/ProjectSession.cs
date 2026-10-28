namespace SceneWorkbench.Core;

public sealed class ProjectSession
{
    public static readonly string[] RequiredRoots = ["Assets", "Packages", "ProjectSettings"];
    public string Root { get; }

    private ProjectSession(string root) => Root = root;

    public static ProjectSession Open(string root)
    {
        root = SafePath.Canonical(root);
        SafePath.RejectLinks(root);
        foreach (var name in RequiredRoots)
            if (!Directory.Exists(SafePath.Resolve(root, name)))
                throw new InvalidDataException($"不是完整 Unity 專案，缺少 {name}。");
        if (!System.IO.File.Exists(SafePath.Resolve(root, "ProjectSettings/ProjectVersion.txt")))
            throw new InvalidDataException("缺少 ProjectVersion.txt。");
        return new ProjectSession(root);
    }

    public string Resolve(string relative) => SafePath.Resolve(Root, relative);

    public IEnumerable<string> EnumerateSourceFiles(bool includePackageCache, CancellationToken cancellationToken = default)
    {
        foreach (var name in RequiredRoots)
            foreach (var path in SafePath.EnumerateFiles(Resolve(name), cancellationToken))
                yield return path;
        var cache = Resolve("Library/PackageCache");
        if (includePackageCache && Directory.Exists(cache))
            foreach (var path in SafePath.EnumerateFiles(cache, cancellationToken))
                yield return path;
    }
}

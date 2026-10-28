namespace SceneWorkbench.Core;

public interface IProjectWriteAccess
{
    ProjectSession Project { get; }
    string ResolveEditable(string relative);
}

/// <summary>Explicit in-place authoring scope. Opening creates no files and does not change project contents.</summary>
public sealed class DirectProjectAccess : IProjectWriteAccess
{
    public ProjectSession Project { get; }
    private DirectProjectAccess(ProjectSession project) => Project = project;
    public static DirectProjectAccess Open(string root) => new(ProjectSession.Open(root));

    public string ResolveEditable(string relative)
    {
        var normalized = relative.Replace('\\', '/');
        if (!ProjectSession.RequiredRoots.Any(name => normalized.StartsWith(name + "/", StringComparison.Ordinal)))
            throw new InvalidOperationException("只能修改所選專案內的 Assets、Packages 與 ProjectSettings。");
        return Project.Resolve(relative);
    }
}

using Godot;
using SceneWorkbench.Core;
using SceneWorkbench.Unity;

namespace SceneWorkbench.Desktop;

public partial class ProjectPanel : VBoxContainer
{
    private readonly Tree tree = new() { HideRoot = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, Columns = 1 };
    private readonly LineEdit search = new() { PlaceholderText = "搜尋資源名稱或路徑…", SizeFlagsHorizontal = SizeFlags.ExpandFill, AccessibilityName = "搜尋 Project 資源" };
    private readonly Label count = EditorTheme.Label("", EditorTheme.Muted);
    private Button more = null!;
    private ProjectSession? project;
    private IReadOnlyList<string> files = [];
    private IReadOnlyList<PackageSnapshot> packages = [];
    private int limit = 300;
    public Action<string>? Selected;
    public Action<string>? Activated;
    public Action<string>? Failed;
    public override void _Ready()
    {
        var row = new HBoxContainer(); row.AddChild(search);
        row.AddChild(EditorTheme.Button("清除", () => { search.Text = ""; Refresh(); }, "清除資源搜尋"));
        AddChild(row); AddChild(tree);
        var footer = new HBoxContainer(); footer.AddChild(count);
        more = EditorTheme.Button("載入更多", () => { limit += 300; Refresh(); }); more.Visible = false; footer.AddChild(more); AddChild(footer);
        search.TextChanged += _ => { limit = 300; Refresh(); };
        tree.ItemSelected += () => { var path = tree.GetSelected()?.GetMetadata(0).AsString(); if (!string.IsNullOrEmpty(path) && path != "pending") Selected?.Invoke(path); };
        tree.ItemActivated += () => { var path = tree.GetSelected()?.GetMetadata(0).AsString(); if (!string.IsNullOrEmpty(path) && project is not null && File.Exists(project.Resolve(path))) Activated?.Invoke(path); };
        tree.ItemCollapsed += item =>
        {
            if (item.Collapsed || item.GetFirstChild()?.GetMetadata(0).AsString() != "pending") return;
            item.GetFirstChild().Free(); Fill(item, item.GetMetadata(0).AsString());
        };
    }
    public void SetProject(ProjectSession value, IReadOnlyList<string> sourceFiles, IReadOnlyList<PackageSnapshot> packageList)
    { project = value; files = sourceFiles; packages = packageList; search.Text = ""; Refresh(); }
    private void Refresh()
    {
        tree.Clear(); var root = tree.CreateItem(); more.Visible = false;
        if (project is null) { count.Text = "尚未開啟專案"; return; }
        var query = search.Text.Trim();
        if (query.Length > 0)
        {
            var matches = files.Where(path => path.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (var path in matches.Take(limit)) { var item = tree.CreateItem(root); item.SetText(0, path); item.SetMetadata(0, path); item.SetTooltipText(0, path); }
            count.Text = $"{Math.Min(limit, matches.Length):N0} / {matches.Length:N0} 項符合"; more.Visible = matches.Length > limit; return;
        }
        var assets = tree.CreateItem(root); assets.SetText(0, "▾  Assets"); assets.SetMetadata(0, "Assets"); Fill(assets, "Assets");
        var pack = tree.CreateItem(root); pack.SetText(0, "▾  Packages"); pack.SetMetadata(0, "Packages");
        foreach (var package in packages.Where(p => p.MetadataPath is not null))
        {
            var relative = package.MetadataPath![..^"/package.json".Length];
            var item = tree.CreateItem(pack); item.SetText(0, package.Name + "  " + package.Version); item.SetMetadata(0, relative); item.SetTooltipText(0, relative);
            item.Collapsed = true; var placeholder = tree.CreateItem(item); placeholder.SetText(0, "載入…"); placeholder.SetMetadata(0, "pending");
        }
        count.Text = $"{files.Count:N0} 個來源資源 · 資料夾按需載入";
    }
    private void Fill(TreeItem parent, string relative)
    {
        if (project is null) return;
        try
        {
            var entries = Directory.EnumerateFileSystemEntries(project.Resolve(relative)).Where(path => !Path.GetFileName(path).StartsWith('.') && !Path.GetFileName(path).EndsWith('~') && !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => !Directory.Exists(path)).ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var path in entries)
            {
                SafePath.RejectLinks(path);
                var isDirectory = Directory.Exists(path); var item = tree.CreateItem(parent); var child = SafePath.Relative(project.Root, path);
                item.SetText(0, (isDirectory ? "▸  " : "    ") + Path.GetFileName(path)); item.SetMetadata(0, child); item.SetTooltipText(0, child);
                if (isDirectory) { item.Collapsed = true; var placeholder = tree.CreateItem(item); placeholder.SetText(0, "載入…"); placeholder.SetMetadata(0, "pending"); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Failed?.Invoke(ex.Message); }
    }
}

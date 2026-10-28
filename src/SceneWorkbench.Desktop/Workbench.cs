using System.Globalization;
using Godot;
using SceneWorkbench.Core;
using SceneWorkbench.Unity;
using YamlDotNet.RepresentationModel;

namespace SceneWorkbench.Desktop;

public partial class Workbench : Control
{
    public const string Version = "0.2.0-desktop-preview";
    private readonly InspectorPanel inspector = new();
    private readonly ProjectPanel projectPanel = new();
    private readonly SceneView sceneView = new();
    private readonly Tree hierarchy = new() { HideRoot = true, SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly LineEdit hierarchySearch = new() { PlaceholderText = "搜尋物件…", SizeFlagsHorizontal = SizeFlags.ExpandFill, AccessibilityName = "搜尋 Hierarchy 物件" };
    private readonly OptionButton scenes = new() { CustomMinimumSize = new Vector2(240, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill, FitToLongestItem = false, ClipText = true };
    private readonly Label status = EditorTheme.Label("選擇專案開始。所有操作均由獨立程式完成。", EditorTheme.Muted);
    private readonly Label projectLabel = EditorTheme.Label("尚未開啟專案");
    private readonly Label compatibilityLabel = EditorTheme.Label("");
    private readonly Label viewLabel = EditorTheme.Label("Scene · 場景結構預覽", EditorTheme.Muted);
    private readonly TextEdit console = new() { Editable = false, SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, WrapMode = TextEdit.LineWrappingMode.Boundary };
    private readonly TabContainer lowerTabs = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly HBoxContainer busyRow = new();
    private readonly Label busyLabel = EditorTheme.Label("", EditorTheme.Warning);
    private VBoxContainer editor = null!;
    private HSplitContainer outerSplit = null!, workSplit = null!;
    private VSplitContainer verticalSplit = null!;
    private PanelContainer welcome = null!;
    private Button save = null!, undo = null!, redo = null!, copy = null!, reload = null!, cancel = null!;
    private readonly FileDialog projectDialog = new() { FileMode = FileDialog.FileModeEnum.OpenDir, Access = FileDialog.AccessEnum.Filesystem, Title = "開啟 Unity 專案資料夾", UseNativeDialog = false, Size = new Vector2I(920, 620) };
    private ProjectSession? project;
    private SceneEditSession? scene;
    private CompatibilitySnapshot? compatibility;
    private IReadOnlyList<string> allFiles = [];
    private IReadOnlyList<string> scenePaths = [];
    private IReadOnlyList<string> layers = Enumerable.Range(0, 32).Select(i => i == 0 ? "Default" : "").ToArray();
    private long? selection;
    private bool workingCopy, busy, rebuilding;
    private CancellationTokenSource? cancellation;
    private readonly List<string> messages = new();
    private string appRoot = "";

    public override void _Ready()
    {
        Theme = EditorTheme.Create();
        appRoot = FindAppRoot();
        GetTree().AutoAcceptQuit = false;
        GetTree().Root.Title = "SceneWorkbench 場景工作檯 · " + Version;
        BuildUi();
        hierarchy.ItemSelected += () => { if (!rebuilding && !busy) SelectObject(hierarchy.GetSelected()?.GetMetadata(0).AsInt64()); };
        hierarchySearch.TextChanged += _ => RefreshHierarchy();
        scenes.ItemSelected += index => { if (!rebuilding && !busy && index >= 0 && index < scenePaths.Count) Navigate(() => _ = OpenScene(scenePaths[(int)index])); };
        projectDialog.DirSelected += path => Navigate(() => _ = OpenProject(path));
        projectPanel.Selected += relative =>
        {
            if (busy || project is null || !inspector.TryCommit()) return;
            var absolute = project.Resolve(relative);
            if (File.Exists(absolute)) inspector.ShowAsset(relative, absolute);
        };
        projectPanel.Activated += relative =>
        {
            if (busy || project is null) return;
            if (Path.GetExtension(relative).Equals(".unity", StringComparison.OrdinalIgnoreCase)) Navigate(() => _ = OpenScene(relative));
            else if (new[] { ".fbx", ".glb", ".gltf" }.Contains(Path.GetExtension(relative).ToLowerInvariant())) _ = PreviewModel(relative);
        };
        projectPanel.Failed += message => Log(message, true);
        inspector.Edited += RefreshAfterEdit;
        inspector.Failed += message => Log(message, true);
        sceneView.Selected += id => SelectObject(id);
        sceneView.Moved += (id, position) =>
        {
            if (busy || scene is null || !workingCopy || !scene.CanEditItem(scene.GetItem(id))) { if (scene is not null) sceneView.ShowScene(scene.Items); return; }
            try { scene.SetPosition(id, position); RefreshAfterEdit(); ShowInspector(); }
            catch (Exception ex) { Log(ex.Message, true); sceneView.ShowScene(scene.Items); }
        };
        Log("獨立桌面版 " + Version + "。不啟動 Unity、不執行專案 C#／Lua。");
        var args = UserArguments();
        GD.Print("SceneWorkbench " + Version + " · " + System.Reflection.Assembly.GetExecutingAssembly().Location);
        var path = Argument(args, "--project");
        if (path is not null) _ = OpenProject(path, Argument(args, "--scene"));
        if (Argument(args, "--desktop-qa-output") is not null) CallDeferred(MethodName.StartQa);
        UpdateActions();
        UpdateCompatibility();
        CallDeferred(MethodName.InitialLayout);
    }

    private void BuildUi()
    {
        var background = new ColorRect { Color = EditorTheme.Canvas, MouseFilter = MouseFilterEnum.Ignore };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(background);
        var root = new VBoxContainer(); root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(root);
        var top = new HBoxContainer(); root.AddChild(top);
        top.AddChild(new Label { Text = "  SceneWorkbench", CustomMinimumSize = new Vector2(150, 28) });
        top.AddChild(EditorTheme.Button("開啟專案…", () => Navigate(() => projectDialog.PopupCentered()), "Ctrl+O"));
        top.AddChild(EditorTheme.Button("示範專案", () => Navigate(() => _ = OpenDemo())));
        top.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        top.AddChild(EditorTheme.Label("獨立運作 · 無 Unity 服務  ", EditorTheme.Muted));
        var toolbar = new HBoxContainer(); root.AddChild(toolbar);
        save = EditorTheme.Button("儲存場景", () => _ = SaveScene(), "Ctrl+S · 以原生 .unity 儲存，保留 GUID 與未編輯資料"); toolbar.AddChild(save);
        undo = EditorTheme.Button("復原", Undo, "Ctrl+Z"); toolbar.AddChild(undo);
        redo = EditorTheme.Button("重做", Redo, "Ctrl+Y / Ctrl+Shift+Z"); toolbar.AddChild(redo);
        reload = EditorTheme.Button("重新載入", () => { if (scene is not null) Navigate(() => _ = OpenScene(scene.RelativePath)); }); toolbar.AddChild(reload);
        copy = EditorTheme.Button("建立工作複本…", ShowCopyDialog, "來源維持唯讀；複製完整 Assets、Packages、ProjectSettings 與套件快取"); toolbar.AddChild(copy);
        toolbar.AddChild(new VSeparator()); toolbar.AddChild(projectLabel);
        toolbar.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        toolbar.AddChild(EditorTheme.Button("復原中斷存檔", () => _ = Recover(), "使用存檔紀錄與備份復原，不覆蓋外部新變更"));
        var banner = new HBoxContainer(); banner.AddChild(compatibilityLabel);
        compatibilityLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill; compatibilityLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        banner.AddChild(EditorTheme.Button("查看 Console", () => lowerTabs.CurrentTab = 1)); root.AddChild(banner);
        busyRow.AddChild(new ProgressBar { Indeterminate = true, ShowPercentage = false, CustomMinimumSize = new Vector2(130, 18) });
        busyLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill; busyRow.AddChild(busyLabel);
        cancel = EditorTheme.Button("取消工作", () => cancellation?.Cancel()); busyRow.AddChild(cancel); busyRow.Visible = false;
        var progressSlot = new MarginContainer { CustomMinimumSize = new Vector2(0, 26) }; progressSlot.AddChild(busyRow); root.AddChild(progressSlot);
        editor = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; root.AddChild(editor);
        var horizontal = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; editor.AddChild(horizontal);
        var vertical = new VSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, SplitOffsets = [530] }; horizontal.AddChild(vertical);
        verticalSplit = vertical;
        outerSplit = horizontal;
        var work = new HSplitContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, SplitOffsets = [240] }; vertical.AddChild(work);
        workSplit = work;
        horizontal.AddChild(inspector); inspector.CustomMinimumSize = new Vector2(325, 0); horizontal.SplitOffsets = [1090];
        var left = EditorTheme.PanelBody("Hierarchy  場景物件"); left.CustomMinimumSize = new Vector2(200, 0); work.AddChild(left);
        var searchRow = new HBoxContainer(); searchRow.AddChild(hierarchySearch); searchRow.AddChild(EditorTheme.Button("清除", () => hierarchySearch.Text = "")); left.AddChild(searchRow); left.AddChild(hierarchy);
        var middle = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill }; work.AddChild(middle);
        var viewBar = new HBoxContainer(); viewBar.AddChild(viewLabel); viewBar.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        viewBar.AddChild(EditorTheme.Button("聚焦 F", sceneView.FrameSelection)); viewBar.AddChild(EditorTheme.Button("全部", sceneView.FrameAll)); middle.AddChild(viewBar);
        middle.AddChild(scenes);
        var sceneArea = new Control { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(320, 240) }; middle.AddChild(sceneArea);
        sceneView.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); sceneArea.AddChild(sceneView);
        welcome = new PanelContainer(); welcome.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); sceneArea.AddChild(welcome);
        var center = new CenterContainer(); welcome.AddChild(center); var start = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0) }; center.AddChild(start);
        var title = EditorTheme.Label("場景工作檯"); title.AddThemeFontSizeOverride("font_size", 24); start.AddChild(title);
        var intro = EditorTheme.Label("直接開啟 Unity 專案，離線編輯已支援的原生場景欄位。", EditorTheme.Muted); intro.AutowrapMode = TextServer.AutowrapMode.WordSmart; start.AddChild(intro);
        start.AddChild(EditorTheme.Button("開啟 Unity 專案…", () => projectDialog.PopupCentered()));
        start.AddChild(EditorTheme.Button("使用示範場景", () => _ = OpenDemo()));
        var sample = FindSample();
        if (sample is not null) start.AddChild(EditorTheme.Button("開啟 Client231229 工作複本", () => _ = OpenProject(sample)));
        start.AddChild(EditorTheme.Label("右鍵旋轉 · 中鍵平移 · 滾輪縮放 · F 聚焦", EditorTheme.Muted));
        vertical.AddChild(lowerTabs); lowerTabs.CustomMinimumSize = new Vector2(0, 180);
        projectPanel.Name = "Project  資源"; lowerTabs.AddChild(projectPanel);
        var consoleBody = new VBoxContainer { Name = "Console  診斷" }; consoleBody.AddChild(console); lowerTabs.AddChild(consoleBody);
        consoleBody.AddChild(EditorTheme.Button("清除顯示", () => { messages.Clear(); console.Text = ""; }));
        status.AutowrapMode = TextServer.AutowrapMode.WordSmart; root.AddChild(status);
        AddChild(projectDialog);
    }

    private async Task OpenProject(string root, string? initialScene = null)
    {
        if (busy) return;
        StartBusy("讀取專案結構…");
        try
        {
            var token = cancellation!.Token;
            var loaded = await Task.Run(() =>
            {
                var p = ProjectSession.Open(root);
                var c = Compatibility.Read(p);
                var paths = p.EnumerateSourceFiles(true, token).Select(path => SafePath.Relative(p.Root, path))
                    .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) && !path.Split('/').Any(part => part.StartsWith('.') || part.EndsWith('~'))).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                return (Project: p, Compatibility: c, Files: paths);
            }, token);
            project = loaded.Project; compatibility = loaded.Compatibility; allFiles = loaded.Files;
            try { _ = WorkCopy.Open(project.Root); workingCopy = true; } catch (Exception ex) when (ex is IOException or InvalidOperationException) { workingCopy = false; Log("來源唯讀：" + ex.Message); }
            scene = null; selection = null; welcome.Visible = false;
            projectLabel.Text = Path.GetFileName(project.Root) + (workingCopy ? " · 工作複本" : " · 來源唯讀"); projectLabel.TooltipText = project.Root;
            layers = ReadLayers(project);
            projectPanel.SetProject(project, allFiles, compatibility.Packages);
            scenePaths = allFiles.Where(path => path.StartsWith("Assets/", StringComparison.Ordinal) && Path.GetExtension(path).Equals(".unity", StringComparison.OrdinalIgnoreCase)).ToArray();
            rebuilding = true; scenes.Clear();
            foreach (var path in scenePaths) scenes.AddItem(Path.GetFileNameWithoutExtension(path));
            rebuilding = false;
            Log("開啟 " + project.Root + "；" + scenePaths.Count + " 個場景。未啟動 Unity。");
            foreach (var diagnostic in compatibility.Diagnostics) Log($"[{diagnostic.Code}] {diagnostic.Message}", diagnostic.Severity == Severity.Blocking);
            UpdateCompatibility();
            EndBusy();
            var first = initialScene ?? (scenePaths.Contains("Assets/02.Arts/Scene/Room/Room_main.unity") ? "Assets/02.Arts/Scene/Room/Room_main.unity" : scenePaths.FirstOrDefault());
            if (first is not null) await OpenScene(first);
            else { hierarchy.Clear(); inspector.ShowEmpty(); sceneView.ShowScene([]); status.Text = "專案已開啟。Assets 內尚無可讀取的場景。"; }
        }
        catch (OperationCanceledException) { Log("已取消開啟專案。"); }
        catch (Exception ex) { Log("無法開啟專案：" + ex.Message, true); }
        finally { EndBusy(); }
    }

    private async Task OpenScene(string relative)
    {
        if (project is null || busy) return;
        StartBusy("讀取場景與物件…");
        try
        {
            var loaded = await Task.Run(() => { var s = new SceneEditSession(project, relative); _ = s.Items; return s; }, cancellation!.Token);
            scene = loaded; selection = null; hierarchySearch.Text = "";
            rebuilding = true; var index = scenePaths.ToList().IndexOf(relative); if (index >= 0) scenes.Select(index); scenes.TooltipText = relative; rebuilding = false;
            RefreshHierarchy(); sceneView.ShowScene(scene.Items, true); inspector.ShowEmpty();
            GetTree().Root.Title = Path.GetFileName(relative) + " — SceneWorkbench " + Version;
            status.Text = $"{relative} · {scene.Items.Count:N0} 個物件 · 原生場景欄位，未支援的內容保留原文";
            viewLabel.Text = "Scene · 場景結構預覽"; UpdateCompatibility();
            Log("已開啟場景 " + relative + "；完整物件 " + scene.Items.Count(i => !i.IsPrefab) + "、Prefab 唯讀 " + scene.Items.Count(i => i.IsPrefab));
        }
        catch (OperationCanceledException) { Log("已取消讀取場景。"); }
        catch (Exception ex) { Log("無法開啟場景：" + ex.Message, true); }
        finally { EndBusy(); }
    }

    private async Task<bool> SaveScene()
    {
        if (busy || scene is null || !workingCopy || !inspector.TryCommit()) return false;
        if (!scene.IsDirty) { status.Text = "沒有未儲存變更；檔案未重寫。"; return true; }
        StartBusy("檢查並儲存原生場景…");
        try
        {
            var result = await Task.Run(() => scene.Save(cancellation!.Token, new UiProgress(this)), cancellation!.Token);
            Log($"已儲存 {scene.RelativePath}；{result.ChangedFiles} 個檔案，備份紀錄 {result.TransactionId}。");
            status.Text = "已儲存 · " + scene.RelativePath + " · 原 .meta／GUID 保留。Unity 相容診斷仍需另外處理。";
            UpdateActions(); return true;
        }
        catch (OperationCanceledException) { Log("已取消儲存；已提交的部分由交易層復原。"); return false; }
        catch (Exception ex)
        {
            Log("儲存未完成，保留目前編輯：" + ex.Message, true);
            foreach (var diagnostic in scene.LastSaveDiagnostics.Where(d => d.Severity == Severity.Blocking)) Log(diagnostic.Code + ": " + diagnostic.Message, true);
            return false;
        }
        finally { EndBusy(); }
    }

    private void SelectObject(long? id)
    {
        if (busy || scene is null || id is null || !scene.Items.Any(i => i.Id == id)) return;
        if (!inspector.TryCommit()) return;
        selection = id;
        sceneView.ShowScene(scene.Items); sceneView.Select(id); viewLabel.Text = "Scene · 場景結構預覽";
        ShowInspector(); UpdateActions();
    }
    private void ShowInspector()
    {
        if (scene is null || selection is null) { inspector.ShowEmpty(); return; }
        var item = scene.GetItem(selection.Value);
        inspector.ShowObject(scene, item, workingCopy && scene.CanEditItem(item), layers);
    }
    private void RefreshAfterEdit()
    {
        if (scene is null) return;
        RefreshHierarchy(); sceneView.ShowScene(scene.Items); sceneView.Select(selection); UpdateActions();
        status.Text = scene.IsDirty ? "● 場景有未儲存變更 · Ctrl+S 儲存" : "場景與上次存檔一致";
    }
    private void RefreshHierarchy()
    {
        rebuilding = true; hierarchy.Clear(); var root = hierarchy.CreateItem();
        if (scene is not null)
        {
            var query = hierarchySearch.Text.Trim(); var nodes = new Dictionary<long, TreeItem>();
            var visible = scene.Items.Where(item => query.Length == 0 || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (var item in visible)
            {
                var treeItem = hierarchy.CreateItem(root); treeItem.SetText(0, (item.IsPrefab ? "◇ " : item.Active ? "▣ " : "□ ") + item.Name);
                treeItem.SetMetadata(0, item.Id); treeItem.SetTooltipText(0, item.IsPrefab ? "Prefab 覆寫唯讀；原始資料完整保留。" : "fileID " + item.Id);
                if (item.IsPrefab || !item.Active) treeItem.SetCustomColor(0, EditorTheme.Muted);
                nodes.Add(item.Id, treeItem);
            }
            if (query.Length == 0)
                foreach (var item in visible.Where(item => item.ParentId != 0 && item.ParentId != item.Id))
                    if (nodes.TryGetValue(item.ParentId, out var parent))
                    {
                        var child = nodes[item.Id];
                        var ancestor = parent;
                        var cycle = false;
                        for (var depth = 0; ancestor is not null && depth < nodes.Count; depth++, ancestor = ancestor.GetParent())
                            if (ancestor == child) { cycle = true; break; }
                        if (!cycle) { child.GetParent()?.RemoveChild(child); parent.AddChild(child); }
                    }
            if (selection.HasValue && nodes.TryGetValue(selection.Value, out var selected)) selected.Select(0);
            if (visible.Length == 0) { var empty = hierarchy.CreateItem(root); empty.SetText(0, "沒有符合的物件"); empty.SetSelectable(0, false); }
        }
        rebuilding = false;
    }
    private void Undo() { if (busy || scene is null || !inspector.TryCommit()) return; scene.Undo(); RefreshAfterEdit(); ShowInspector(); }
    private void Redo() { if (busy || scene is null || !inspector.TryCommit()) return; scene.Redo(); RefreshAfterEdit(); ShowInspector(); }
    private void UpdateActions()
    {
        if (save is null) return;
        save.Disabled = busy || !workingCopy || scene is null || !scene.SupportsEditing;
        undo.Disabled = busy || scene is null || scene.UndoCount == 0;
        redo.Disabled = busy || scene is null || scene.RedoCount == 0;
        copy.Disabled = busy || project is null || workingCopy;
        reload.Disabled = busy || scene is null;
        GetTree().Root.Title = (scene is null ? "SceneWorkbench 場景工作檯" : Path.GetFileName(scene.RelativePath) + (scene.IsDirty ? " *" : "") + " — SceneWorkbench") + " · " + Version;
    }
    private void UpdateCompatibility()
    {
        var blockers = compatibility?.Diagnostics.Count(d => d.Severity == Severity.Blocking) ?? 0;
        compatibilityLabel.Text = project is null ? "開啟專案後會標示可編輯的格式範圍。" :
            (workingCopy ? "工作複本" : "來源唯讀") + " · Unity " + compatibility!.EditorVersion +
            (blockers > 0 ? $" · {blockers} 項相容診斷未解；離線欄位存檔另行檢查" : " · 套件本機檢查無阻擋") +
            (scene is { SupportsEditing: false } ? " · 此場景版本唯讀" : "");
        compatibilityLabel.AddThemeColorOverride("font_color", blockers > 0 ? EditorTheme.Warning : EditorTheme.Muted);
    }

    private void Navigate(Action next)
    {
        if (busy || !inspector.TryCommit()) return;
        if (scene?.IsDirty != true) { next(); return; }
        var dialog = new ConfirmationDialog { Title = "場景尚未儲存", DialogText = Path.GetFileName(scene.RelativePath) + " 有未儲存的變更。", OkButtonText = "儲存後繼續", CancelButtonText = "取消", Size = new Vector2I(500, 180) };
        dialog.AddButton("捨棄變更", false, "discard"); AddChild(dialog);
        dialog.Confirmed += async () => { if (await SaveScene()) next(); dialog.QueueFree(); };
        dialog.CustomAction += action => { if (action == "discard") { dialog.Hide(); next(); dialog.QueueFree(); } };
        dialog.Canceled += () => dialog.QueueFree(); dialog.PopupCentered();
    }

    private void ShowCopyDialog()
    {
        if (project is null || busy) return;
        var source = project.Root;
        var dialog = new ConfirmationDialog { Title = "建立完整工作複本", OkButtonText = "建立複本", CancelButtonText = "取消", Size = new Vector2I(760, 240) };
        var body = new VBoxContainer(); dialog.AddChild(body);
        body.AddChild(EditorTheme.Label("來源維持唯讀。目的地必須是尚未存在的新資料夾。"));
        var destination = new LineEdit { Text = Path.Combine(appRoot, "Workspace", Path.GetFileName(source) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)), AccessibilityName = "工作複本目的地" }; body.AddChild(destination);
        body.AddChild(EditorTheme.Label("包含 Assets、Packages、ProjectSettings 與 Library/PackageCache；可隨時取消。", EditorTheme.Muted));
        AddChild(dialog);
        dialog.Confirmed += () => { var path = destination.Text; dialog.QueueFree(); _ = CreateCopy(source, path); };
        dialog.Canceled += () => dialog.QueueFree(); dialog.PopupCentered();
    }
    private async Task CreateCopy(string source, string destination)
    {
        StartBusy("建立完整工作複本…");
        var opened = false;
        try
        {
            await WorkCopy.CreateAsync(source, destination, progress: new CopyUiProgress(this), cancellationToken: cancellation!.Token);
            opened = true; Log("完整工作複本建立完成；來源未寫入。");
        }
        catch (OperationCanceledException) { Log("已取消複製。未完成的複本不具回寫資格。"); }
        catch (Exception ex) { Log("建立複本失敗：" + ex.Message, true); }
        finally { EndBusy(); }
        if (opened) await OpenProject(destination);
    }
    private async Task OpenDemo()
    {
        if (busy) return;
        var source = Path.Combine(appRoot, "DemoProject");
        var destination = Path.Combine(appRoot, "Workspace", "Demo");
        if (!Directory.Exists(source)) { Log("找不到隨程式附帶的 DemoProject，請先執行桌面打包腳本。", true); return; }
        if (Directory.Exists(destination)) await OpenProject(destination);
        else await CreateCopy(source, destination);
    }
    private async Task Recover()
    {
        if (project is null || !workingCopy || busy) return;
        StartBusy("檢查未完成的存檔紀錄…");
        try { var recovered = await Task.Run(() => new SaveTransaction(WorkCopy.Open(project.Root)).Recover()); Log("已復原 " + recovered.Count + " 筆未完成存檔。請重新載入場景以讀取磁碟內容。"); }
        catch (Exception ex) { Log("無法安全復原，保留現況與備份：" + ex.Message, true); }
        finally { EndBusy(); }
    }

    private async Task PreviewModel(string relative)
    {
        if (busy || project is null || !inspector.TryCommit()) return;
        StartBusy("讀取模型來源…");
        try
        {
            var path = project.Resolve(relative); SafePath.RejectLinks(path);
            var model = await Task.Run(() =>
            {
                GltfDocument document = Path.GetExtension(relative).Equals(".fbx", StringComparison.OrdinalIgnoreCase) ? new FbxDocument() : new GltfDocument();
                using var state = new GltfState();
                var error = document.AppendFromFile(path, state);
                if (error != Error.Ok) throw new InvalidDataException("模型解碼失敗：" + error);
                return document.GenerateScene(state) as Node3D ?? throw new InvalidDataException("模型沒有可預覽的 3D 場景。");
            }, cancellation!.Token);
            sceneView.ShowModel(model); viewLabel.Text = "模型來源預覽 · " + Path.GetFileName(relative);
            status.Text = "僅預覽模型來源；未套用 Unity FBX 匯入設定、ASE、Prefab 或場景綁定。";
            Log("已預覽模型來源 " + relative + "，原檔未變更。");
        }
        catch (Exception ex) { Log("模型來源預覽失敗：" + ex.Message, true); }
        finally { EndBusy(); }
    }

    private void StartBusy(string message)
    {
        busy = true; cancellation?.Dispose(); cancellation = new CancellationTokenSource();
        busyLabel.Text = message; busyRow.Visible = true; editor.ProcessMode = ProcessModeEnum.Disabled; UpdateActions();
    }
    private void EndBusy() { busy = false; if (editor is null) return; editor.ProcessMode = ProcessModeEnum.Inherit; busyRow.Visible = false; UpdateActions(); }
    private void Log(string message, bool error = false)
    {
        messages.Add(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + (error ? "  ⚠  " : "  ·  ") + message);
        if (messages.Count > 200) messages.RemoveAt(0);
        console.Text = string.Join('\n', messages); console.ScrollVertical = int.MaxValue;
        if (error) { status.Text = message; status.AddThemeColorOverride("font_color", EditorTheme.Warning); }
    }
    public override void _Notification(int what)
    {
        if (what != NotificationWMCloseRequest) return;
        if (busy) { Log("請先完成或取消目前工作，再關閉視窗。", true); return; }
        Navigate(() => GetTree().Quit());
    }
    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (busy || e is not InputEventKey { Pressed: true, Echo: false } key || DisplayServer.ImeGetText().Length > 0) return;
        var typing = GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit;
        if (key.CtrlPressed && key.Keycode == Key.S) { _ = SaveScene(); GetViewport().SetInputAsHandled(); }
        else if (key.CtrlPressed && key.Keycode == Key.O) { Navigate(() => projectDialog.PopupCentered()); GetViewport().SetInputAsHandled(); }
        else if (!typing && key.CtrlPressed && key.Keycode == Key.Z) { if (key.ShiftPressed) Redo(); else Undo(); GetViewport().SetInputAsHandled(); }
        else if (!typing && key.CtrlPressed && key.Keycode == Key.Y) { Redo(); GetViewport().SetInputAsHandled(); }
        else if (!typing && key.Keycode == Key.F) { sceneView.FrameSelection(); GetViewport().SetInputAsHandled(); }
    }

    private static string? Argument(string[] args, string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
    private static string FindAppRoot()
    {
        var executable = Path.GetDirectoryName(OS.GetExecutablePath())!;
        if (Directory.Exists(Path.Combine(executable, "DemoProject"))) return executable;
        var current = ProjectSettings.GlobalizePath("res://");
        for (var directory = new DirectoryInfo(current); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SceneWorkbench.slnx"))) return directory.FullName;
        return executable;
    }
    private string? FindSample()
    {
        for (var directory = new DirectoryInfo(appRoot); directory is not null; directory = directory.Parent)
        {
            var sample = Path.Combine(directory.FullName, "samples", "Client231229");
            if (File.Exists(Path.Combine(sample, ".scene-workbench", "workcopy.json"))) return sample;
        }
        return null;
    }
    private static IReadOnlyList<string> ReadLayers(ProjectSession p)
    {
        var names = Enumerable.Range(0, 32).Select(i => i == 0 ? "Default" : "").ToArray();
        var path = p.Resolve("ProjectSettings/TagManager.asset");
        if (!File.Exists(path)) return names;
        try
        {
            var doc = UnityYamlDocument.Load(path);
            var mapping = doc.ReadObject(doc.Objects[0].FileId);
            if (mapping.Children.Values.FirstOrDefault() is YamlMappingNode root && root.Children.TryGetValue(new YamlScalarNode("layers"), out var value) && value is YamlSequenceNode layers)
                for (var i = 0; i < Math.Min(32, layers.Children.Count); i++) names[i] = (layers.Children[i] as YamlScalarNode)?.Value ?? "";
        }
        catch (Exception ex) when (ex is InvalidDataException or YamlDotNet.Core.YamlException) { }
        return names;
    }
    private sealed class UiProgress(Workbench workbench) : IProgress<string>
    { public void Report(string value) => workbench.CallDeferred(MethodName.UpdateProgress, value); }
    private sealed class CopyUiProgress(Workbench workbench) : IProgress<CopyProgress>
    {
        private long last;
        public void Report(CopyProgress value)
        {
            var tick = System.Environment.TickCount64;
            if (tick - Interlocked.Read(ref last) < 250 && value.FilesDone != value.FilesTotal) return;
            Interlocked.Exchange(ref last, tick);
            workbench.CallDeferred(MethodName.UpdateProgress, $"複製 {value.FilesDone:N0}/{value.FilesTotal:N0} 檔 · {value.BytesDone / 1048576.0:N1}/{value.BytesTotal / 1048576.0:N1} MB");
        }
    }
    private void UpdateProgress(string message) { if (busy) busyLabel.Text = message; }
    private void InitialLayout()
    {
        outerSplit.SplitOffsets = [(int)(outerSplit.Size.X / 2 - 335)];
        workSplit.SplitOffsets = [(int)(240 - workSplit.Size.X / 2)];
        verticalSplit.SplitOffsets = [(int)(verticalSplit.Size.Y * .18f)];
    }
    private static string[] UserArguments() => OS.GetCmdlineUserArgs().Length > 0 ? OS.GetCmdlineUserArgs() : OS.GetCmdlineArgs();
    private void StartQa() => AddChild(new DesktopSmokeQa(this, UserArguments()));
}

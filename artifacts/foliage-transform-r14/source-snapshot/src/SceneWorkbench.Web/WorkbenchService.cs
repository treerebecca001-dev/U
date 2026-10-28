using System.Globalization;
using System.Numerics;
using System.Text.Json;
using SceneWorkbench.Core;
using SceneWorkbench.Unity;
using YamlDotNet.RepresentationModel;

namespace SceneWorkbench.Web;

public sealed class WorkbenchService(string workspace)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private ProjectSession? project;
    private SceneEditSession? scene;
    private MaterialEditSession? material;
    private PreviewCatalog? previewCatalog;
    private CompatibilitySnapshot? compatibility;
    private string[] files = [], scenes = [], layers = [];
    private bool writable;
    private IProjectWriteAccess? writeAccess;
    private bool packageCacheIndexed;
    private string readOnlyReason = "尚未開啟專案";
    private long revision;
    private string activity = "";
    private CancellationTokenSource? operation;
    private IReadOnlyList<Diagnostic> diagnostics = [];

    public object Activity => new { busy = operation is not null, message = Volatile.Read(ref activity) };
    public void Cancel() => operation?.Cancel();
    public object Bootstrap => new
    {
        mode = "local-web-development", unityRequired = false,
        demoPath = Path.Combine(workspace, "DemoProject"),
        samplePath = ResolveSampleProject(workspace),
        suggestedCopyPath = Path.Combine(workspace, "Workspace", "WebDemo"),
        suggestedProjectCopyPath = Path.Combine(workspace, "Workspace", "ProjectCopies", "Project-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
        demoWorkingExists = File.Exists(Path.Combine(workspace, "Workspace", "WebDemo", WorkCopy.InfoPath)),
        sampleScene = "Assets/02.Arts/Scene/Room/Room_main.unity"
    };

    private static string ResolveSampleProject(string workspace)
    {
        var configured = Environment.GetEnvironmentVariable("SCENE_WORKBENCH_SAMPLE_PROJECT");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var selected = SafePath.Canonical(configured);
            if (!SafePath.IsWithin(workspace, selected)) throw new InvalidOperationException("預設樣本必須位於工作區內的完整工作複本。");
            return WorkCopy.Open(selected).Project.Root;
        }
        var prepared = Path.Combine(workspace, "Workspace", "Baselines", "Client231229-" + BaselinePreparation.TargetEditor);
        if (File.Exists(Path.Combine(prepared, BaselinePreparation.MarkerPath)))
        {
            var copy = WorkCopy.Open(prepared);
            var report = JsonFile.Read<BaselinePreparationReport>(copy.Project.Resolve(BaselinePreparation.MarkerPath));
            if (SafePath.Canonical(report.PreparedRoot) != copy.Project.Root || report.TargetEditor != BaselinePreparation.TargetEditor
                || report.OfflineStatus != "VERIFIED_LOCAL_DECLARATIONS" || report.NativeStatus != "UNVERIFIED")
                throw new InvalidDataException("預設基準標記與複本不符，請重新檢查離線基準紀錄。");
            return copy.Project.Root;
        }
        return Path.Combine(workspace, "samples", "Client231229");
    }

    public async Task<object> StateAsync(CancellationToken ct) => await ReadAsync(State, ct);
    private object State() => new
    {
        revision, projectPath = project?.Root, projectName = project is null ? null : Path.GetFileName(project.Root),
        editorVersion = compatibility?.EditorVersion, writable, readOnlyReason, scenePath = scene?.RelativePath,
        writeMode = writeAccess is DirectProjectAccess ? "direct" : writeAccess is WorkCopy ? "copy" : "read-only", writePath = project?.Root,
        dirty = (scene?.IsDirty ?? false) || (material?.IsDirty ?? false), undoCount = material?.UndoCount ?? scene?.UndoCount ?? 0, redoCount = material?.RedoCount ?? scene?.RedoCount ?? 0,
        material = material?.Snapshot(writable),
        layers, scenePaths = scenes, sourceFiles = files.Length, packageCacheIndexed,
        items = scene?.Items.Select(item => WebSceneItem.From(scene, item, writable)).ToArray() ?? [],
        diagnostics = diagnostics.Select(d => new { d.Code, severity = d.Severity.ToString(), d.Message, d.Path }),
        pendingTransactions = PendingTransactions(),
        preview = "來源模型與材質預覽 · 固定 Shader 適配，Unity 畫面比對待驗收"
    };
    private string[] PendingTransactions()
    {
        if (!writable || project is null) return [];
        return new SaveTransaction(writeAccess!).PendingTransactions().ToArray();
    }

    public Task<object> OpenProjectAsync(OpenProjectRequest request, CancellationToken ct)
    {
        if (request.Mode == "copy") return OpenCopiedProjectAsync(request, ct);
        if (request.Mode is not "inspect" and not "direct") throw new ArgumentException("不支援的專案開啟模式。");
        if (request.Destination is not null) throw new ArgumentException("直接／檢視模式不接受副本目的地。");
        return ChangeAsync(request.Revision, "讀取專案結構與版本…", () =>
        {
            RequireClean(request.Discard);
            LoadProject(request.Path, request.Mode == "direct");
            return State();
        }, ct);
    }
    private async Task<object> OpenCopiedProjectAsync(OpenProjectRequest request, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            CheckRevision(request.Revision); RequireClean(request.Discard);
            if (string.IsNullOrWhiteSpace(request.Destination)) throw new ArgumentException("請指定新的工作複本目的地。");
            var destination = SafePath.Canonical(request.Destination);
            if (!SafePath.IsWithin(workspace, destination)) throw new InvalidOperationException("開發版的工作複本請建立於此工作區內。");
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct); operation = linked;
            Volatile.Write(ref activity, "建立完整工作複本…");
            var copy = await WorkCopy.CreateAsync(request.Path, destination, true,
                new ImmediateProgress<CopyProgress>(p => Volatile.Write(ref activity, $"複製 {p.FilesDone:N0}/{p.FilesTotal:N0} · {p.BytesDone / 1048576.0:N1}/{p.BytesTotal / 1048576.0:N1} MB")), linked.Token);
            LoadProject(copy.Project.Root); revision++;
            return State();
        }
        finally { operation = null; Volatile.Write(ref activity, ""); gate.Release(); }
    }
    private void LoadProject(string root, bool direct = false)
    {
        var next = ProjectSession.Open(root);
        Volatile.Write(ref activity, "讀取 Unity 與套件版本…");
        var nextCompatibility = Compatibility.Read(next);
        Volatile.Write(ref activity, "讀取專案資源與場景…");
        // Package folders are browsed directly; build their recursive search catalog only on demand.
        var nextFiles = next.EnumerateSourceFiles(false, operation?.Token ?? default).Select(path => SafePath.Relative(next.Root, path))
            .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var nextScenes = nextFiles.Where(path => path.StartsWith("Assets/", StringComparison.Ordinal) && path.EndsWith(".unity", StringComparison.Ordinal)).ToArray();
        var nextLayers = ReadLayers(next);
        var canWrite = false; var reason = "";
        IProjectWriteAccess? nextAccess = null;
        try { nextAccess = direct ? DirectProjectAccess.Open(next.Root) : WorkCopy.Open(next.Root); canWrite = true; }
        catch (Exception ex) when (ex is IOException or InvalidOperationException) { reason = ex.Message; }
        project = next; compatibility = nextCompatibility; files = nextFiles; scenes = nextScenes; layers = nextLayers;
        writeAccess = nextAccess;
        packageCacheIndexed = false;
        writable = canWrite; readOnlyReason = reason; scene = null; material = null; previewCatalog = null; diagnostics = nextCompatibility.Diagnostics;
    }
    public Task<object> OpenSceneAsync(OpenSceneRequest request, CancellationToken ct) =>
        ChangeAsync(request.Revision, "載入原生 Unity 場景…", () =>
        {
            RequireClean(request.Discard);
            var next = new SceneEditSession(RequireProject(), request.Path, writeAccess);
            scene = next; material = null; previewCatalog = null;
            return State();
        }, ct);
    public Task<object> EditAsync(FieldEditRequest request, CancellationToken ct) =>
        ChangeAsync(request.Revision, "套用屬性…", () =>
        {
            var current = RequireScene();
            if (material is not null) throw new InvalidOperationException("請先離開材質 Inspector 再編輯場景物件。");
            if (!writable) throw new InvalidOperationException(readOnlyReason);
            if (!long.TryParse(request.Id, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id))
                throw new ArgumentException("fileID 必須是有號 64 位元整數字串。");
            var item = current.GetItem(id);
            var web = WebSceneItem.From(current, item, writable);
            if (!web.EditableFields.Contains(request.Field, StringComparer.Ordinal)) throw new InvalidOperationException("此物件／欄位未驗證可安全編輯，維持唯讀。");
            switch (request.Field)
            {
                case "name": current.SetName(item.GameObjectId, request.Value.GetString() ?? ""); break;
                case "active": current.SetActive(item.GameObjectId, request.Value.GetBoolean()); break;
                case "layer": current.SetLayer(item.GameObjectId, request.Value.GetInt32()); break;
                case "staticFlags": current.SetStaticFlags(item.GameObjectId, request.Value.GetUInt32()); break;
                case "position": current.SetPosition(id, Vector(request.Value)); break;
                case "scale": current.SetScale(id, Vector(request.Value)); break;
                case "rotation":
                    var q = Values(request.Value, 4); current.SetRotation(id, new Quaternion(q[0], q[1], q[2], q[3])); break;
                default: throw new ArgumentException("不支援的欄位。");
            }
            return State();
        }, ct);
    public Task<object> HistoryAsync(RevisionRequest request, bool redo, CancellationToken ct) =>
        ChangeAsync(request.Revision, redo ? "重做屬性…" : "復原屬性…", () =>
        {
            if (material is not null) { if (redo) material.Redo(); else material.Undo(); }
            else { var current = RequireScene(); if (redo) current.Redo(); else current.Undo(); }
            return State();
        }, ct);
    public Task<object> SaveAsync(RevisionRequest request, CancellationToken ct) =>
        ChangeAsync(request.Revision, "檢查存檔條件…", () =>
        {
            if (!writable) throw new InvalidOperationException(readOnlyReason);
            if (material is not null)
            {
                try { _ = material.Save(operation!.Token, new ImmediateProgress<string>(message => Volatile.Write(ref activity, message))); }
                finally { if (material.LastSaveDiagnostics.Count != 0) diagnostics = material.LastSaveDiagnostics; }
                return State();
            }
            var current = RequireScene();
            try { _ = current.Save(operation!.Token, new ImmediateProgress<string>(message => Volatile.Write(ref activity, message))); }
            finally { if (current.LastSaveDiagnostics.Count != 0) diagnostics = current.LastSaveDiagnostics; }
            return State();
        }, ct);
    public Task<object> RecoverAsync(RevisionRequest request, CancellationToken ct) =>
        ChangeAsync(request.Revision, "復原中斷存檔…", () =>
        {
            if (!writable) throw new InvalidOperationException(readOnlyReason);
            RequireClean(false);
            _ = new SaveTransaction(writeAccess!).Recover();
            if (scene is not null) scene = new SceneEditSession(project!, scene.RelativePath, writeAccess);
            if (material is not null) material = Catalog().Material(material.RelativePath, writeAccess);
            return State();
        }, ct);

    public async Task<object> CopyAsync(CopyRequest request, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            CheckRevision(request.Revision); RequireClean(request.Discard);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct); operation = linked;
            Volatile.Write(ref activity, "建立完整工作複本…");
            // Complete copies stay in the task workspace. Direct mode is a separate explicit choice.
            var destination = SafePath.Canonical(request.Destination);
            if (!SafePath.IsWithin(workspace, destination)) throw new InvalidOperationException("開發版的工作複本請建立於此工作區內。");
            var copy = await WorkCopy.CreateAsync(RequireProject().Root, destination, true,
                new ImmediateProgress<CopyProgress>(p => Volatile.Write(ref activity, $"複製 {p.FilesDone:N0}/{p.FilesTotal:N0} · {p.BytesDone / 1048576.0:N1}/{p.BytesTotal / 1048576.0:N1} MB")), linked.Token);
            LoadProject(copy.Project.Root); revision++;
            return State();
        }
        finally { operation = null; Volatile.Write(ref activity, ""); gate.Release(); }
    }
    public async Task<object> AssetsAsync(string directory, string query, int offset, CancellationToken ct) => await ReadAsync<object>(() =>
    {
        var p = RequireProject();
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (!string.IsNullOrWhiteSpace(query))
        {
            if (!packageCacheIndexed)
            {
                var indexed = p.EnumerateSourceFiles(true, ct).Select(path => SafePath.Relative(p.Root, path))
                    .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                ct.ThrowIfCancellationRequested();
                files = indexed;
                packageCacheIndexed = true;
            }
            var matches = files.Where(path => IsAssetPath(path) && path.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            return new { directory, query, offset, total = matches.Length, rows = matches.Skip(offset).Take(200).Select(path => Asset(p, path, false)).ToArray() };
        }
        if (directory.Length == 0)
            return new { directory, query, offset = 0, total = 2, rows = new[] { new AssetRow("Assets", "Assets", "資料夾", true, 0), new AssetRow("Packages", "Packages", "套件", true, 0) } };
        AssetRow[] rows;
        if (directory == "Packages")
            rows = compatibility!.Packages.Where(package => package.MetadataPath is not null)
                .Select(package => new AssetRow(package.MetadataPath![..^"/package.json".Length], package.Name + "  " + package.Version, "套件", true, 0)).ToArray();
        else
        {
            RequireAssetPath(directory);
            rows = Directory.EnumerateFileSystemEntries(p.Resolve(directory)).Where(path => !Path.GetFileName(path).StartsWith('.') && !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .Select(path => { SafePath.RejectLinks(path); return Asset(p, SafePath.Relative(p.Root, path), Directory.Exists(path)); })
                .OrderBy(row => !row.IsDirectory).ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        return new { directory, query, offset, total = rows.Length, rows = rows.Skip(offset).Take(200).ToArray() };
    }, ct);
    public async Task<object> AssetInfoAsync(string path, CancellationToken ct) => await ReadAsync<object>(() =>
    {
        RequireAssetPath(path); var p = RequireProject(); var absolute = p.Resolve(path); var info = new FileInfo(absolute);
        var ext = info.Extension.ToLowerInvariant();
        var text = new[] { ".mat", ".shader", ".txt", ".json" }.Contains(ext) && info.Length <= 256 * 1024 ? File.ReadAllText(absolute) : null;
        string[] meshIds = [];
        if (ext == ".asset" && info.Length <= 64 * 1024 * 1024)
        {
            using var input = File.OpenRead(absolute); var prefix = new byte[12]; var length = input.Read(prefix);
            var start = length >= 3 && prefix.AsSpan(0, 3).SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
            if (length >= start + 9 && System.Text.Encoding.UTF8.GetString(prefix, start, 9) == "%YAML 1.1")
                meshIds = UnityYamlDocument.Load(absolute).Objects.Where(o => o.ClassId == 43 && !o.IsStripped).Select(o => o.FileId.ToString(CultureInfo.InvariantCulture)).ToArray();
        }
        return new { path, bytes = info.Length, extension = ext, text, meshIds, image = new[] { ".png", ".jpg", ".jpeg", ".webp" }.Contains(ext), readOnly = true };
    }, ct);
    private PreviewCatalog Catalog(CancellationToken ct = default) => previewCatalog ??= new PreviewCatalog(RequireProject(), ct,
        new ImmediateProgress<string>(message => Volatile.Write(ref activity, message)), SafePath.Resolve(workspace, "artifacts/cache/preview-guid"));
    public Task<object> OpenMaterialAsync(OpenMaterialRequest request, CancellationToken ct) =>
        ChangeAsync(request.Revision, "讀取材質與 Shader 參數…", () =>
        {
            RequireClean(request.Discard); RequireAssetPath(request.Path);
            // Scene and material are separate native documents. Never hide or partially save a dirty scene.
            if (scene?.IsDirty == true) scene = new SceneEditSession(RequireProject(), scene.RelativePath, writeAccess);
            var next = Catalog(operation!.Token).Material(request.Path, writeAccess); material = next; return State();
        }, ct);
    public Task<object> CloseMaterialAsync(CloseMaterialRequest request, CancellationToken ct) =>
        ChangeAsync(request.Revision, "切換場景 Inspector…", () => { RequireClean(request.Discard); material = null; return State(); }, ct);
    public Task<object> EditMaterialAsync(MaterialEditRequest request, CancellationToken ct) => EditMaterialsAsync(new(request.Revision, [request]), ct);
    public Task<object> EditMaterialsAsync(MaterialBatchRequest request, CancellationToken ct) =>
        ChangeAsync(request.Revision, "套用材質參數…", () =>
        {
            if (!writable) throw new InvalidOperationException(readOnlyReason);
            var current = material ?? throw new InvalidOperationException("請先選取 .mat 材質。");
            current.ApplyBatch(request.Edits.Select(edit =>
            {
                if (edit.Part == "texture") return new NativeMaterialEdit(edit.Name, edit.Part, TexturePath: edit.Value.ValueKind == JsonValueKind.Null ? null : edit.Value.GetString());
                var values = edit.Value.EnumerateArray().ToArray();
                if (current.IsIntegerProperty(edit.Name) && edit.Part == "value")
                {
                    if (values.Length != 1) throw new ArgumentException("Integer 材質參數必須只有一個 Int32 值。");
                    return new NativeMaterialEdit(edit.Name, edit.Part, IntegerValue: values[0].GetInt32());
                }
                if (current.IsLegacyIntProperty(edit.Name) && values.Any(v =>
                    { if (!v.TryGetInt32(out var integer)) return true; var rounded = (float)integer; return (double)rounded != integer; }))
                    throw new ArgumentException("Legacy Int 儲存在 Float32，必須輸入可精確表示的 Int32 整數；不會靜默捨入。");
                return new NativeMaterialEdit(edit.Name, edit.Part, values.Select(v => v.GetSingle()).ToArray());
            }).ToArray());
            return State();
        }, ct);
    public async Task<object> PreviewAsync(string path, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct); operation = linked;
            Volatile.Write(ref activity, "解析模型／貼圖／材質 GUID…");
            return await Task.Run<object>(() =>
            {
                if (scene is null || scene.RelativePath != path) throw new InvalidOperationException("預覽場景已切換，請重新載入。");
                return Catalog(linked.Token).Scene(path, scene.GetBytes(), p => material?.RelativePath == p ? material.Snapshot(writable) : null, linked.Token);
            }, linked.Token);
        }
        finally { operation = null; Volatile.Write(ref activity, ""); gate.Release(); }
    }
    public async Task<object> ModelAsync(string path, string fileId, CancellationToken ct) => await ReadAsync<object>(() =>
    {
        RequireAssetPath(path);
        if (Path.GetExtension(path).Equals(".fbx", StringComparison.OrdinalIgnoreCase)) return Catalog(ct).ModelInfo(path);
        if (!long.TryParse(fileId, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id)) throw new ArgumentException("無效的 Mesh fileID。");
        return NativeMeshReader.Read(RequireProject(), path, id);
    }, ct);
    public async Task<object> TextureInfoAsync(string path, CancellationToken ct) => await ReadAsync<object>(() => { RequireAssetPath(path); return Catalog(ct).TextureInfo(path); }, ct);
    public async Task<(byte[] Bytes, string Mime)> SourceAsync(string path, CancellationToken ct) => await ReadAsync(() =>
    {
        RequireAssetPath(path);
        if (!new[] { ".fbx", ".tga", ".bmp", ".exr" }.Contains(Path.GetExtension(path).ToLowerInvariant())) throw new InvalidOperationException("此來源格式不提供原始預覽下載。");
        var absolute = RequireProject().Resolve(path);
        if (new FileInfo(absolute).Length > 64 * 1024 * 1024) throw new InvalidOperationException("來源超過 64 MB 預覽限制。");
        return (File.ReadAllBytes(absolute), "application/octet-stream");
    }, ct);
    public async Task<(byte[] Bytes, string Mime)> ImageAsync(string path, CancellationToken ct) => await ReadAsync(() =>
    {
        RequireAssetPath(path); var absolute = RequireProject().Resolve(path);
        var mime = Path.GetExtension(path).ToLowerInvariant() switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", _ => throw new InvalidOperationException("此格式尚未提供貼圖預覽。") };
        if (new FileInfo(absolute).Length > 64 * 1024 * 1024) throw new InvalidOperationException("貼圖超過開發版 64 MB 預覽限制。");
        return (File.ReadAllBytes(absolute), mime);
    }, ct);

    private AssetRow Asset(ProjectSession p, string relative, bool directory)
        => new(relative, Path.GetFileName(relative), directory ? "資料夾" : Path.GetExtension(relative).TrimStart('.').ToUpperInvariant(), directory, directory ? 0 : new FileInfo(p.Resolve(relative)).Length);
    private bool IsAssetPath(string path) => path == "Assets" || path.StartsWith("Assets/", StringComparison.Ordinal)
        || (compatibility?.Packages.Any(package => package.MetadataPath is not null &&
            (path == package.MetadataPath[..^"/package.json".Length] || path.StartsWith(package.MetadataPath[..^"package.json".Length], StringComparison.Ordinal))) ?? false);
    private void RequireAssetPath(string path) { if (!IsAssetPath(path)) throw new InvalidOperationException("只能讀取目前專案的 Assets 或有效套件資源。"); _ = RequireProject().Resolve(path); }
    private ProjectSession RequireProject() => project ?? throw new InvalidOperationException("請先開啟 Unity 專案資料夾。");
    private SceneEditSession RequireScene() => scene ?? throw new InvalidOperationException("請先開啟 .unity 場景。");
    private void RequireClean(bool discard) { if (((scene?.IsDirty ?? false) || (material?.IsDirty ?? false)) && !discard) throw new InvalidOperationException("場景或材質有未儲存變更，請先儲存或明確選擇捨棄。"); }
    private void CheckRevision(long expected) { if (expected != revision) throw new StaleSessionException(); }
    private async Task<object> ChangeAsync(long expected, string message, Func<object> change, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            CheckRevision(expected);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct); operation = linked;
            Volatile.Write(ref activity, message);
            // Increment before constructing the response so its revision identifies the applied state.
            revision++;
            return await Task.Run(change, linked.Token);
        }
        finally { operation = null; Volatile.Write(ref activity, ""); gate.Release(); }
    }
    private async Task<T> ReadAsync<T>(Func<T> read, CancellationToken ct)
    { await gate.WaitAsync(ct); try { return read(); } finally { gate.Release(); } }
    private static Vector3 Vector(JsonElement value) { var v = Values(value, 3); return new(v[0], v[1], v[2]); }
    private static float[] Values(JsonElement value, int count)
    {
        var values = value.EnumerateArray().Select(item => item.GetSingle()).ToArray();
        if (values.Length != count || values.Any(number => !float.IsFinite(number))) throw new ArgumentException("座標必須是有限數值，且軸數正確。");
        return values;
    }
    private static string[] ReadLayers(ProjectSession p)
    {
        var result = Enumerable.Range(0, 32).Select(i => i == 0 ? "Default" : "").ToArray();
        if (!File.Exists(p.Resolve("ProjectSettings/TagManager.asset"))) return result;
        var doc = UnityYamlDocument.Load(p.Resolve("ProjectSettings/TagManager.asset"));
        if (doc.ReadObject(doc.Objects[0].FileId).Children.Values.FirstOrDefault() is YamlMappingNode root && root.Children.TryGetValue(new YamlScalarNode("layers"), out var node) && node is YamlSequenceNode sequence)
            for (var i = 0; i < Math.Min(32, sequence.Children.Count); i++) result[i] = (sequence.Children[i] as YamlScalarNode)?.Value ?? "";
        return result;
    }
    private sealed class ImmediateProgress<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
}

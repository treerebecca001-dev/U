using System.Globalization;
using SceneWorkbench.Core;
using YamlDotNet.RepresentationModel;
using static SceneWorkbench.Unity.UnityData;

namespace SceneWorkbench.Unity;

public sealed record MaterialPropertyValue(string Name, string Label, string Kind, object[] Value, float? Min, float? Max,
    bool Hidden, bool Editable, string? TexturePath, string TextureGuid, string TextureFileId, float[]? Scale, float[]? Offset, bool IntegerStorage = false);
public sealed record MaterialSnapshot(string Path, string Name, string ShaderPath, string ShaderGuid, string ShaderName,
    string ShaderHash, bool IsAse, bool Editable, bool Dirty, int UndoCount, int RedoCount, string[] Keywords,
    IReadOnlyList<MaterialPropertyValue> Properties, string PreviewKind = "shader-source", string ShaderFileId = "0", IReadOnlyDictionary<string, string>? ShaderDependencies = null);
public sealed record NativeMaterialEdit(string Name, string Part, float[]? Numbers = null, string? TexturePath = null, int? IntegerValue = null);

/// <summary>Native material property edits only; source Shader, ASE graph, keywords and unknown data stay byte-preserved.</summary>
public sealed class MaterialEditSession
{
    private readonly ProjectSession project;
    private readonly IProjectWriteAccess? writeAccess;
    private readonly UnityYamlDocument document;
    private readonly long id;
    private readonly CommandHistory history = new();
    private readonly Func<string, string?> resolveGuid;
    private readonly Dictionary<string, string> guards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string[] Path, ShaderProperty Definition)> fields = new(StringComparer.Ordinal);
    private readonly string shaderPath, shaderGuid, shaderText, editorVersion;
    private readonly Dictionary<string, string> shaderDependencies = new(StringComparer.Ordinal);
    private string savedHash;
    public string RelativePath { get; }
    public bool SupportsEditing { get; }
    public bool IsDirty => Hashing.Bytes(document.ToBytes()) != savedHash;
    public int UndoCount => history.UndoCount;
    public int RedoCount => history.RedoCount;
    public IReadOnlyList<Diagnostic> LastSaveDiagnostics { get; private set; } = [];

    public MaterialEditSession(ProjectSession project, string relativePath, Func<string, string?> resolveGuid, string? knownEditorVersion = null, byte[]? snapshotBytes = null, IProjectWriteAccess? writeAccess = null)
    {
        this.project = project; this.resolveGuid = resolveGuid;
        if (writeAccess is not null && !writeAccess.Project.Root.Equals(project.Root, SafePath.Comparison)) throw new ArgumentException("存檔範圍與材質專案不一致。");
        this.writeAccess = writeAccess;
        if (Path.GetExtension(relativePath).ToLowerInvariant() != ".mat")
            throw new ArgumentException("請選擇原生 .mat。");
        RelativePath = relativePath;
        document = snapshotBytes is null ? UnityYamlDocument.Load(project.Resolve(relativePath)) : UnityYamlDocument.Parse(snapshotBytes);
        var materials = document.Objects.Where(o => o is { ClassId: 21, IsStripped: false }).ToArray();
        if (materials.Length != 1) throw new InvalidDataException("材質檔案必須有唯一完整 Material；其餘附加物件保留原文。");
        id = materials[0].FileId;
        var body = Body(document, id);
        shaderGuid = Text(At(body, "m_Shader", "guid"));
        shaderPath = resolveGuid(shaderGuid) ?? "";
        shaderText = shaderPath.Length > 0 && new[] { ".shader", ".watershader" }.Contains(Path.GetExtension(shaderPath).ToLowerInvariant())
            ? ReadBounded(shaderPath, 4 * 1024 * 1024) : "";
        editorVersion = knownEditorVersion ?? Compatibility.Read(project).EditorVersion;
        var version = UnityVersion.Parse(editorVersion);
        SupportsEditing = relativePath.StartsWith("Assets/", StringComparison.Ordinal) && Path.GetExtension(shaderPath).Equals(".shader", StringComparison.OrdinalIgnoreCase)
            && YamlEditProfile.SupportsMaterial(version, Long(At(body, "serializedVersion")))
            && NullReference(At(body, "m_PrefabInstance")) && NullReference(At(body, "m_CorrespondingSourceObject"))
            && NullReference(At(body, "m_PrefabAsset")) && NullReference(At(body, "m_Parent")) && shaderText.Length > 0;
        savedHash = document.OriginalSha256;
        Guard(relativePath + ".meta");
        foreach (var path in new[] { "ProjectSettings/ProjectVersion.txt", "ProjectSettings/TagManager.asset", "Packages/manifest.json", "Packages/packages-lock.json" }) Guard(path);
        if (shaderPath.Length > 0) { Guard(shaderPath); Guard(shaderPath + ".meta"); }
        if (shaderPath.EndsWith(".watershader", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var name in new[] { "Common.hlsl", "Features.hlsl", "ForwardPass.hlsl", "Input.hlsl", "Lighting.hlsl", "Vertex.hlsl", "Waves.hlsl", "URP.hlsl", "Fog.hlsl", "Caustics.hlsl", "Tesselation.hlsl" })
            {
                var path = shaderPath[..(shaderPath.LastIndexOf('/') + 1)] + "Libraries/" + name;
                if (File.Exists(project.Resolve(path))) _ = ReadBounded(path, 4 * 1024 * 1024);
                Guard(path); shaderDependencies[name] = guards[path];
            }
        }
        var saved = At(body, "m_SavedProperties");
        foreach (var definition in ShaderProperties.Read(shaderText))
        {
            var category = definition.Kind is "2D" or "2DArray" or "Cube" ? "m_TexEnvs" : definition.Kind is "Color" or "Vector" ? "m_Colors" : definition.Kind == "Integer" ? "m_Ints" : "m_Floats";
            var candidates = List(At(saved, category)).Select((node, index) => (node, index)).Where(pair => At(pair.node, definition.Name) is not null).ToArray();
            if (candidates.Length == 0 && definition.Kind == "Int")
            { category = "m_Ints"; candidates = List(At(saved, category)).Select((node, index) => (node, index)).Where(pair => At(pair.node, definition.Name) is not null).ToArray(); }
            if (candidates.Length == 1)
            {
                fields.Add(definition.Name, (["Material", "m_SavedProperties", category, candidates[0].index.ToString(CultureInfo.InvariantCulture), definition.Name], definition));
                if (category == "m_TexEnvs")
                {
                    var texturePath = resolveGuid(Text(At(candidates[0].node, definition.Name, "m_Texture", "guid")));
                    if (texturePath is not null) { Guard(texturePath); Guard(texturePath + ".meta"); }
                }
            }
        }
    }
    private string ReadBounded(string path, int max)
    { var absolute = project.Resolve(path); if (new FileInfo(absolute).Length > max) throw new InvalidDataException("資源超過可安全讀取大小。"); return File.ReadAllText(absolute); }
    private static bool NullReference(YamlNode? node) => node is null || node is YamlMappingNode
        && Text(At(node, "fileID")) == "0" && (At(node, "guid") is null || Text(At(node, "guid")) == new string('0', 32));
    private void Guard(string path) { var absolute = project.Resolve(path); guards.TryAdd(path, File.Exists(absolute) ? Hashing.File(absolute) : ""); }
    public MaterialSnapshot Snapshot(bool writable)
    {
        var current = UnityYamlDocument.Parse(document.ToBytes()); var body = Body(current, id);
        var properties = new List<MaterialPropertyValue>();
        foreach (var definition in ShaderProperties.Read(shaderText))
        {
            var exists = fields.TryGetValue(definition.Name, out var field);
            var node = exists ? At(current.ReadObject(id), field.Path) : null;
            var texture = definition.Kind is "2D" or "2DArray" or "Cube";
            var guid = Text(At(node, "m_Texture", "guid"));
            var path = guid.Length == 32 ? resolveGuid(guid) : null;
            var floats = definition.Kind is "Color" or "Vector" ? Vector(node, "rgba")
                : texture ? [] : new[] { node is null ? definition.Default.FirstOrDefault() : Float(node) };
            if (node is null && definition.Kind is "Color" or "Vector") floats = definition.Default.Length == 4 ? definition.Default : [0, 0, 0, 0];
            var integerStorage = IsIntegerProperty(definition.Name);
            var validInteger = int.TryParse(Text(node), NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer);
            object[] numbers = integerStorage ? [integer] : definition.Kind == "Integer" && node is null ? [definition.IntegerDefault ?? 0] : floats.Cast<object>().ToArray();
            properties.Add(new(definition.Name, definition.Label.Length > 0 ? definition.Label : definition.Name, definition.Kind, numbers,
                definition.Min, definition.Max, definition.Hidden, writable && SupportsEditing && exists && (!integerStorage || validInteger) && (!texture || definition.Kind == "2D"), path, guid,
                Long(At(node, "m_Texture", "fileID")).ToString(CultureInfo.InvariantCulture), texture ? Vector(At(node, "m_Scale"), "xy", 1) : null,
                texture ? Vector(At(node, "m_Offset"), "xy") : null, integerStorage));
        }
        var keywords = List(At(body, "m_ValidKeywords")).Select(n => Text(n)).ToArray();
        if (keywords.Length == 0) keywords = Text(At(body, "m_ShaderKeywords")).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var shaderName = System.Text.RegularExpressions.Regex.Match(shaderText, "Shader\\s+\"([^\"]+)\"").Groups[1].Value;
        var previewKind = shaderPath.EndsWith(".watershader", StringComparison.OrdinalIgnoreCase) ? "water-template" : shaderText.Length == 0 ? "saved-properties" : "shader-source";
        if (properties.Count == 0) properties.AddRange(ReadOnlySavedProperties(body));
        if (shaderName.Length == 0) shaderName = previewKind == "water-template" ? "Stylized Water 2 / 原始範本" : "未解析 Shader：" + shaderGuid;
        return new(RelativePath, Text(At(body, "m_Name")), shaderPath, shaderGuid, shaderName,
            shaderText.Length == 0 ? "" : guards[shaderPath], shaderText.Contains("ASEBEGIN", StringComparison.Ordinal), writable && SupportsEditing,
            IsDirty, UndoCount, RedoCount, keywords, properties, previewKind, Long(At(body, "m_Shader", "fileID")).ToString(CultureInfo.InvariantCulture), shaderDependencies);
    }
    private IEnumerable<MaterialPropertyValue> ReadOnlySavedProperties(YamlNode body)
    {
        var saved = At(body, "m_SavedProperties");
        var entries = new[] { "m_TexEnvs", "m_Colors", "m_Floats", "m_Ints" }.SelectMany(category => List(At(saved, category))
            .OfType<YamlMappingNode>().SelectMany(map => map.Children.Select(pair => (category, name: Text(pair.Key), value: pair.Value))))
            .GroupBy(p => p.name, StringComparer.Ordinal).Where(g => g.Count() == 1).Select(g => g.Single());
        foreach (var (category, name, value) in entries)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"\A[A-Za-z_][A-Za-z_0-9]*\z")) continue;
            var guid = category == "m_TexEnvs" ? Text(At(value, "m_Texture", "guid")) : "";
            var texturePath = guid.Length == 32 ? resolveGuid(guid) : null; var kind = category == "m_TexEnvs" ? "2D" : category == "m_Colors" ? "Color" : category == "m_Ints" ? "Integer" : "Float";
            if (texturePath is not null && File.Exists(project.Resolve(texturePath + ".meta")))
            {
                var meta = ReadBounded(texturePath + ".meta", 1024 * 1024);
                var shape = System.Text.RegularExpressions.Regex.Match(meta, @"(?m)^\s*textureShape:\s*([234])\s*$").Groups[1].Value;
                kind = shape == "2" ? "Cube" : shape == "3" ? "2DArray" : shape == "4" ? "3D" : "2D";
            }
            object[] numbers;
            if (category == "m_Ints") { if (!int.TryParse(Text(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) continue; numbers = [integer]; }
            else { var floats = category == "m_TexEnvs" ? [] : category == "m_Colors" ? Vector(value, "rgba") : new[] { Float(value) }; if (floats.Any(f => !float.IsFinite(f))) continue; numbers = floats.Cast<object>().ToArray(); }
            yield return new(name, name, kind, numbers, null, null, false, false, texturePath, guid,
                category == "m_TexEnvs" ? Long(At(value, "m_Texture", "fileID")).ToString(CultureInfo.InvariantCulture) : "0",
                category == "m_TexEnvs" ? Vector(At(value, "m_Scale"), "xy", 1) : null, category == "m_TexEnvs" ? Vector(At(value, "m_Offset"), "xy") : null, category == "m_Ints");
        }
    }
    public void SetNumbers(string name, float[] values, string part = "value")
    {
        RequireField(name); var field = fields[name];
        if (IsIntegerProperty(name)) throw new ArgumentException("原生 m_Ints 必須使用精確 Int32 通道。");
        var texture = field.Definition.Kind == "2D";
        var count = texture ? 2 : field.Definition.Kind is "Color" or "Vector" ? 4 : 1;
        if (values.Length != count || values.Any(n => !float.IsFinite(n))) throw new ArgumentException("材質參數必須是軸數正確的有限 Float32 數值。");
        if (texture && part is not "scale" and not "offset" || !texture && part != "value") throw new ArgumentException("無效的材質參數欄位。");
        if (field.Definition.Kind is "2DArray" or "Cube") throw new InvalidOperationException("此貼圖維度目前唯讀。");
        if (field.Definition.Kind is "Int" or "Integer" && values.Any(n => n != MathF.Truncate(n) || n < int.MinValue || n >= 2147483648f)) throw new ArgumentException("Int 材質參數必須是有效 32 位元整數。");
        var axes = texture ? "xy" : count == 4 ? "rgba" : "";
        var prefix = texture ? field.Path.Concat(new[] { part == "scale" ? "m_Scale" : "m_Offset" }).ToArray() : field.Path;
        var paths = count == 1 ? new[] { prefix } : axes.Select(c => prefix.Concat(new[] { c.ToString() }).ToArray()).ToArray();
        var before = paths.Select(p => float.Parse(document.GetScalar(id, p), CultureInfo.InvariantCulture)).ToArray();
        if (before.SequenceEqual(values)) return;
        void Write(float[] numbers) { for (var i = 0; i < numbers.Length; i++) document.SetFloat(id, numbers[i], paths[i]); }
        history.Execute(new MaterialCommand("調整材質 " + name, () => Write(values), () => Write(before)));
    }
    public bool IsIntegerProperty(string name) => fields.TryGetValue(name, out var field) && field.Path[2] == "m_Ints" && field.Definition.Kind is "Integer" or "Int";
    public bool IsLegacyIntProperty(string name) => fields.TryGetValue(name, out var field) && field.Path[2] == "m_Floats" && field.Definition.Kind == "Int";
    public void SetInteger(string name, int value)
    {
        RequireField(name);
        if (!IsIntegerProperty(name)) throw new ArgumentException("此欄位不是原生 m_Ints 整數。");
        var path = fields[name].Path;
        if (!int.TryParse(document.GetScalar(id, path), NumberStyles.Integer, CultureInfo.InvariantCulture, out var before))
            throw new InvalidDataException("來源整數不是有效 Int32，保持唯讀。");
        if (before == value) return;
        history.Execute(new MaterialCommand("調整整數材質 " + name, () => document.SetInteger(id, value, path), () => document.SetInteger(id, before, path)));
    }
    public void SetTexture(string name, string? path)
    {
        RequireField(name); var field = fields[name];
        if (field.Definition.Kind != "2D") throw new InvalidOperationException("目前只支援來源 2D 貼圖指定。");
        var next = new UnityAssetId(new string('0', 32), 0);
        if (!string.IsNullOrEmpty(path))
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !new[] { ".png", ".jpg", ".jpeg", ".webp", ".tga", ".bmp" }.Contains(Path.GetExtension(path).ToLowerInvariant()))
                throw new ArgumentException("請指定 Assets 內可預覽的 2D 貼圖。");
            if (!File.Exists(project.Resolve(path)) || !File.Exists(project.Resolve(path + ".meta")) || new FileInfo(project.Resolve(path)).Length > 64 * 1024 * 1024) throw new ArgumentException("貼圖／meta 缺少或超過 64 MB。");
            _ = ReadBounded(path + ".meta", 1024 * 1024);
            var meta = File.ReadAllText(project.Resolve(path + ".meta"));
            if (System.Text.RegularExpressions.Regex.IsMatch(meta, @"(?m)^\s*textureShape:\s*[234]\s*$")) throw new InvalidOperationException("此資源不是來源 2D 貼圖；Array／Cube／3D 維度保持唯讀。");
            var guid = System.Text.RegularExpressions.Regex.Match(meta, @"(?m)^guid:\s*([a-f0-9]{32})\s*$").Groups[1].Value;
            if (resolveGuid(guid) != path || !meta.Contains("TextureImporter:", StringComparison.Ordinal)) throw new InvalidOperationException("貼圖 GUID 不唯一或不是 TextureImporter。");
            Guard(path); Guard(path + ".meta"); next = new UnityAssetId(guid, 2800000);
        }
        var prefix = field.Path.Concat(new[] { "m_Texture" }).ToArray();
        var current = At(UnityYamlDocument.Parse(document.ToBytes()).ReadObject(id), prefix);
        var before = new UnityAssetId(Text(At(current, "guid"), new string('0', 32)), Long(At(current, "fileID")));
        if (before == next) return;
        history.Execute(new MaterialCommand("指定貼圖 " + name, () => document.SetAssetReference(id, next, prefix), () => document.SetAssetReference(id, before, prefix)));
    }
    private void RequireField(string name)
    { if (!SupportsEditing || !fields.ContainsKey(name)) throw new InvalidOperationException("材質欄位結構尚未驗證或不存在，保持唯讀。"); }
    public void Undo() => history.Undo();
    public void Redo() => history.Redo();
    public void ApplyBatch(IReadOnlyList<NativeMaterialEdit> edits)
    {
        if (edits.Count > 128) throw new ArgumentException("一次材質編輯最多 128 個欄位。");
        var validation = new MaterialEditSession(project, RelativePath, resolveGuid, editorVersion, document.ToBytes());
        static void Apply(MaterialEditSession target, NativeMaterialEdit edit)
        {
            if (edit.Part == "texture") target.SetTexture(edit.Name, edit.TexturePath);
            else if (edit.IntegerValue.HasValue && edit.Part == "value") target.SetInteger(edit.Name, edit.IntegerValue.Value);
            else target.SetNumbers(edit.Name, edit.Numbers ?? [], edit.Part);
        }
        // Validate every source path, dimension and scalar span before changing the live history.
        foreach (var edit in edits) Apply(validation, edit);
        foreach (var edit in edits) Apply(this, edit);
    }
    public SaveResult Save(CancellationToken ct = default, IProgress<string>? progress = null)
    {
        var copy = writeAccess ?? (IProjectWriteAccess)WorkCopy.Open(project.Root);
        if (!SupportsEditing) throw new InvalidOperationException("此材質版本／Shader 尚未驗證可安全編輯。");
        if (!IsDirty) return new(null, 0);
        if (guards[RelativePath + ".meta"].Length == 0) throw new InvalidOperationException("材質缺少 .meta。");
        if (Hashing.File(project.Resolve(RelativePath)) != savedHash) throw new SaveConflictException("材質已被外部修改，保留輸入，請重新載入。");
        foreach (var (path, hash) in guards)
            if ((File.Exists(project.Resolve(path)) ? Hashing.File(project.Resolve(path)) : "") != hash)
                throw new SaveConflictException("材質的 meta、Shader、貼圖或專案設定已被外部修改，請重新載入：" + path);
        progress?.Report("檢查材質 GUID 與原生資源完整性…");
        var compatibility = Compatibility.Read(project);
        if (compatibility.EditorVersion != editorVersion || compatibility.Diagnostics.Any(d => d.Severity == Severity.Blocking && d.Code is not "PACKAGE_EDITOR_MISMATCH" and not "URP_EDITOR_MISMATCH"))
            throw new InvalidOperationException("版本／依賴存在超出材質欄位能力的阻擋，禁止回存。");
        var diagnostics = new List<Diagnostic>();
        _ = AssetIndex.Build(project, project.EnumerateSourceFiles(true, ct).ToArray(), diagnostics, ct, progress, compatibility);
        LastSaveDiagnostics = compatibility.Diagnostics.Concat(diagnostics).ToArray();
        if (diagnostics.Any(d => d.Severity == Severity.Blocking)) throw new InvalidOperationException("GUID／資源完整性檢查未通過，禁止材質回存。");
        var bytes = document.ToBytes();
        var updates = new List<FileUpdate> { new(RelativePath, savedHash, bytes) };
        updates.AddRange(guards.Where(pair => pair.Value.Length > 0).Select(pair => new FileUpdate(pair.Key, pair.Value, File.ReadAllBytes(project.Resolve(pair.Key)))));
        var result = new SaveTransaction(copy).Commit(updates, cancellationToken: ct);
        savedHash = Hashing.Bytes(bytes); return result;
    }
    private sealed class MaterialCommand(string description, Action execute, Action undo) : IEditCommand
    { public string Description => description; public void Execute() => execute(); public void Undo() => undo(); }
}

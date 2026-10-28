using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using SceneWorkbench.Core;
using YamlDotNet.RepresentationModel;

namespace SceneWorkbench.Unity;

public sealed record SceneItem(long Id, long GameObjectId, long? TransformId, long ParentId,
    string Name, Vector3 Position, Quaternion Rotation, Vector3 Scale, bool Active, int Layer,
    uint StaticFlags, bool IsPrefab, IReadOnlyList<string> Components, string? MeshGuid = null,
    long MeshFileId = 0, Vector3? ColliderSize = null, Vector3? ColliderCenter = null);

/// <summary>
/// An offline, field-scoped scene document. Package/runtime compatibility remains a separate
/// diagnostic; it is never changed or reported as passed by this authoring capability.
/// </summary>
public sealed class SceneEditSession
{
    private static readonly HashSet<string> RuntimeOnlyConflicts = new(StringComparer.Ordinal)
        { "PACKAGE_EDITOR_MISMATCH", "URP_EDITOR_MISMATCH" };
    private static readonly HashSet<string> EditablePaths = new(StringComparer.Ordinal)
    {
        "GameObject.m_Name", "GameObject.m_IsActive", "GameObject.m_Layer", "GameObject.m_StaticEditorFlags",
        "Transform.m_LocalPosition.x", "Transform.m_LocalPosition.y", "Transform.m_LocalPosition.z",
        "Transform.m_LocalRotation.x", "Transform.m_LocalRotation.y", "Transform.m_LocalRotation.z", "Transform.m_LocalRotation.w",
        "Transform.m_LocalScale.x", "Transform.m_LocalScale.y", "Transform.m_LocalScale.z"
    };
    private readonly UnityYamlDocument document;
    private readonly IProjectWriteAccess? writeAccess;
    private readonly CommandHistory history = new();
    private readonly string metaHash;
    private readonly Dictionary<string, string> contextHashes = new(StringComparer.Ordinal);
    private string savedHash;
    private IReadOnlyList<SceneItem>? items;
    public ProjectSession Project { get; }
    public string RelativePath { get; }
    public string EditorVersion { get; }
    public bool SupportsEditing { get; }
    public bool IsDirty { get; private set; }
    public int UndoCount => history.UndoCount;
    public int RedoCount => history.RedoCount;
    public IReadOnlyList<SceneItem> Items => items ??= ReadItems();
    public IReadOnlyList<ScalarChange> Changes => document.Changes;
    public IReadOnlyList<Diagnostic> LastSaveDiagnostics { get; private set; } = [];

    public SceneEditSession(ProjectSession project, string relativePath, IProjectWriteAccess? writeAccess = null)
    {
        if (!relativePath.StartsWith("Assets/", StringComparison.Ordinal) || Path.GetExtension(relativePath) != ".unity")
            throw new ArgumentException("請選擇 Assets 內的 .unity 場景。");
        Project = project;
        if (writeAccess is not null && !writeAccess.Project.Root.Equals(project.Root, SafePath.Comparison)) throw new ArgumentException("存檔範圍與文件專案不一致。");
        this.writeAccess = writeAccess;
        RelativePath = relativePath;
        var versionText = File.ReadAllText(project.Resolve("ProjectSettings/ProjectVersion.txt"));
        EditorVersion = Regex.Match(versionText, @"(?m)^m_EditorVersion:\s*(\S+)").Groups[1].Value;
        var version = UnityVersion.Parse(EditorVersion);
        SupportsEditing = YamlEditProfile.Supports(version);
        document = UnityYamlDocument.Load(project.Resolve(relativePath));
        savedHash = document.OriginalSha256;
        metaHash = File.Exists(project.Resolve(relativePath + ".meta")) ? Hashing.File(project.Resolve(relativePath + ".meta")) : "";
        foreach (var relative in new[] { "ProjectSettings/ProjectVersion.txt", "ProjectSettings/TagManager.asset", "Packages/manifest.json", "Packages/packages-lock.json" })
            contextHashes.Add(relative, File.Exists(project.Resolve(relative)) ? Hashing.File(project.Resolve(relative)) : "");
    }

    public SceneItem GetItem(long id) => Items.First(item => item.Id == id);
    public bool CanEditItem(SceneItem item)
    {
        if (!SupportsEditing || item.IsPrefab || !item.TransformId.HasValue || !HasField(item.GameObjectId, "GameObject", "m_Name")) return false;
        if (YamlEditProfile.SerializedVersion(document, item.GameObjectId) is { } gameObjectVersion && gameObjectVersion != "6") return false;
        var transformVersion = YamlEditProfile.SerializedVersion(document, item.TransformId.Value);
        if (!YamlEditProfile.SupportsTransform(UnityVersion.Parse(EditorVersion), transformVersion)) return false;
        if (Connected(item.GameObjectId, "GameObject") || Connected(item.TransformId.Value, "Transform")) return false;
        var mapping = document.ReadObject(item.GameObjectId);
        if (mapping.Children.Values.FirstOrDefault() is not YamlMappingNode go
            || !go.Children.TryGetValue(new YamlScalarNode("m_Component"), out var componentNode) || componentNode is not YamlSequenceNode list) return false;
        var references = list.Children.OfType<YamlMappingNode>().SelectMany(node => node.Children.Values).OfType<YamlMappingNode>()
            .SelectMany(node => node.Children).Where(pair => pair.Key is YamlScalarNode { Value: "fileID" })
            .Select(pair => (pair.Value as YamlScalarNode)?.Value).ToArray();
        if (references.Count(value => value == item.TransformId.Value.ToString(CultureInfo.InvariantCulture)) != 1) return false;
        foreach (var field in new[] { "m_LocalPosition", "m_LocalRotation", "m_LocalScale" })
            foreach (var axis in field == "m_LocalRotation" ? new[] { "x", "y", "z", "w" } : new[] { "x", "y", "z" })
                if (!float.TryParse(Scalar(item.TransformId.Value, "", "Transform", field, axis), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value)) return false;
        var rotationLength = ReadRotation(item.TransformId.Value).LengthSquared();
        if (!float.IsFinite(rotationLength) || rotationLength < .000001f) return false;
        if (HasField(item.GameObjectId, "GameObject", "m_Layer")
            && (!long.TryParse(Scalar(item.GameObjectId, "", "GameObject", "m_Layer"), CultureInfo.InvariantCulture, out var layer) || layer is < 0 or > 31)) return false;
        if (HasField(item.GameObjectId, "GameObject", "m_IsActive") && Scalar(item.GameObjectId, "", "GameObject", "m_IsActive") is not "0" and not "1") return false;
        if (HasField(item.GameObjectId, "GameObject", "m_StaticEditorFlags")
            && (!long.TryParse(Scalar(item.GameObjectId, "", "GameObject", "m_StaticEditorFlags"), CultureInfo.InvariantCulture, out var flags) || flags < int.MinValue || flags > uint.MaxValue)) return false;
        return true;
    }
    public bool HasField(long id, params string[] path)
    {
        try { _ = document.GetScalar(id, path); return true; }
        catch (InvalidDataException) { return false; }
    }

    public void SetName(long id, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1024) throw new ArgumentException("名稱須為 1 至 1024 字元。");
        RequireGameObject(id);
        var before = document.GetScalar(id, "GameObject", "m_Name");
        if (before == value) return;
        Execute("重新命名", () => document.SetString(id, value, "GameObject", "m_Name"), () => document.SetString(id, before, "GameObject", "m_Name"));
    }

    public void SetPosition(long transformId, Vector3 value)
    {
        RequireEditableTransform(transformId);
        if (document.GetPosition(transformId) == value) return;
        history.Execute(new TransformPositionCommand(document, transformId, value));
        Changed();
    }

    public void SetScale(long id, Vector3 value) => SetVector(id, "m_LocalScale", value, "縮放物件");

    public void SetRotation(long id, Quaternion value)
    {
        RequireEditableTransform(id);
        if (!float.IsFinite(value.LengthSquared()) || value.LengthSquared() < 0.000001f)
            throw new ArgumentException("旋轉必須是有限且非零的 Quaternion。");
        var before = ReadRotation(id);
        if (before == value) return;
        value = Quaternion.Normalize(value);
        foreach (var axis in new[] { "x", "y", "z", "w" }) _ = document.GetScalar(id, "Transform", "m_LocalRotation", axis);
        void Write(Quaternion q)
        {
            document.SetFloat(id, q.X, "Transform", "m_LocalRotation", "x");
            document.SetFloat(id, q.Y, "Transform", "m_LocalRotation", "y");
            document.SetFloat(id, q.Z, "Transform", "m_LocalRotation", "z");
            document.SetFloat(id, q.W, "Transform", "m_LocalRotation", "w");
        }
        Execute("旋轉物件", () => Write(value), () => Write(before));
    }

    public void SetActive(long id, bool value) => SetGameObjectInteger(id, "m_IsActive", value ? 1 : 0, "切換物件啟用");
    public void SetLayer(long id, int value)
    {
        if (value is < 0 or > 31) throw new ArgumentOutOfRangeException(nameof(value), "Layer 必須介於 0 與 31。");
        SetGameObjectInteger(id, "m_Layer", value, "設定 Layer");
    }
    public void SetStaticFlags(long id, uint value)
    {
        RequireGameObject(id);
        if (unchecked((uint)Integer(id, 0, "GameObject", "m_StaticEditorFlags")) == value) return;
        SetGameObjectInteger(id, "m_StaticEditorFlags", value, "設定 Static");
    }

    public void Undo() { history.Undo(); Changed(); }
    public void Redo() { history.Redo(); Changed(); }
    public byte[] GetBytes() => document.ToBytes();

    public SaveResult Save(CancellationToken cancellationToken = default, IProgress<string>? progress = null)
    {
        RequireEditable();
        var copy = writeAccess ?? (IProjectWriteAccess)WorkCopy.Open(Project.Root);
        if (!IsDirty) return new SaveResult(null, 0);
        if (metaHash.Length == 0) throw new InvalidOperationException("場景缺少 .meta，無法安全保留原 GUID。");
        if (Hashing.File(Project.Resolve(RelativePath)) != savedHash || Hashing.File(Project.Resolve(RelativePath + ".meta")) != metaHash)
            throw new SaveConflictException("場景或 .meta 已被外部修改；保留目前編輯，請重新載入或另行處理衝突。");
        foreach (var (relative, hash) in contextHashes)
            if ((File.Exists(Project.Resolve(relative)) ? Hashing.File(Project.Resolve(relative)) : "") != hash)
                throw new SaveConflictException("專案版本、Layer 或依賴已在外部改動，請重新開啟專案：" + relative);
        // Only this enumerated field capability is permitted. Never enable a general force-write path.
        if (document.Changes.Any(change => !EditablePaths.Contains(change.Property)
            || document.GetObject(change.FileId).IsStripped
            || document.GetObject(change.FileId).ClassId is not 1 and not 4))
            throw new InvalidOperationException("變更超出已驗證的離線場景欄位，禁止回寫。");
        progress?.Report("檢查版本與套件…");
        var compatibility = Compatibility.Read(Project);
        if (compatibility.EditorVersion != EditorVersion)
            throw new SaveConflictException("專案版本已在外部改動，請重新開啟專案。");
        if (compatibility.Diagnostics.Any(item => item.Severity == Severity.Blocking && !RuntimeOnlyConflicts.Contains(item.Code)))
            throw new InvalidOperationException("套件／格式基準不完整，禁止回寫；請查看 Console。");
        progress?.Report("檢查 GUID 與資源完整性…");
        var diagnostics = new List<Diagnostic>();
        _ = AssetIndex.Build(Project, Project.EnumerateSourceFiles(true, cancellationToken).ToArray(), diagnostics,
            cancellationToken, progress, compatibility);
        LastSaveDiagnostics = compatibility.Diagnostics.Concat(diagnostics).ToArray();
        if (diagnostics.Any(item => item.Severity == Severity.Blocking))
            throw new InvalidOperationException("GUID／資源完整性檢查未通過，禁止回寫；請查看 Console。");
        var bytes = document.ToBytes();
        progress?.Report("建立備份並儲存場景…");
        var updates = new List<FileUpdate> {
            new FileUpdate(RelativePath, savedHash, bytes),
            new FileUpdate(RelativePath + ".meta", metaHash, File.ReadAllBytes(Project.Resolve(RelativePath + ".meta")))
        };
        updates.AddRange(contextHashes.Where(pair => pair.Value.Length > 0).Select(pair => new FileUpdate(pair.Key, pair.Value, File.ReadAllBytes(Project.Resolve(pair.Key)))));
        var result = new SaveTransaction(copy).Commit(updates, cancellationToken: cancellationToken);
        savedHash = Hashing.Bytes(bytes);
        IsDirty = false;
        return result;
    }

    private void SetVector(long id, string field, Vector3 value, string description)
    {
        RequireEditableTransform(id);
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z)) throw new ArgumentException("數值必須有限。");
        var before = ReadVector(id, field, Vector3.One);
        if (before == value) return;
        void Write(Vector3 v)
        {
            document.SetFloat(id, v.X, "Transform", field, "x");
            document.SetFloat(id, v.Y, "Transform", field, "y");
            document.SetFloat(id, v.Z, "Transform", field, "z");
        }
        // Preflight every scalar before a multi-component mutation.
        foreach (var axis in new[] { "x", "y", "z" }) _ = document.GetScalar(id, "Transform", field, axis);
        Execute(description, () => Write(value), () => Write(before));
    }

    private void SetGameObjectInteger(long id, string field, long value, string description)
    {
        RequireGameObject(id);
        var before = long.Parse(document.GetScalar(id, "GameObject", field), CultureInfo.InvariantCulture);
        if (before == value) return;
        Execute(description, () => document.SetInteger(id, value, "GameObject", field), () => document.SetInteger(id, before, "GameObject", field));
    }
    private void Execute(string description, Action apply, Action undo)
    {
        RequireEditable();
        history.Execute(new ActionCommand(description, apply, undo));
        Changed();
    }
    private void Changed() { items = null; IsDirty = Hashing.Bytes(document.ToBytes()) != savedHash; }
    private void RequireEditable()
    {
        if (!SupportsEditing) throw new InvalidOperationException("目前離線欄位編輯只驗證 Unity 2021.3／2022.3 穩定版的已知場景結構，此版本保持唯讀。");
    }
    private void RequireGameObject(long id)
    {
        RequireEditable();
        var item = document.GetObject(id);
        if (item.ClassId != 1 || item.IsStripped) throw new InvalidOperationException("Prefab 覆寫與非完整 GameObject 目前保持唯讀。");
        var owner = Items.FirstOrDefault(obj => obj.GameObjectId == id);
        if (owner is null || !CanEditItem(owner)) throw new InvalidOperationException("只允許完整、非 Prefab 且結構已識別的 GameObject／Transform；此物件保持唯讀。");
    }
    private void RequireEditableTransform(long id)
    {
        RequireEditable();
        var item = document.GetObject(id);
        if (item.ClassId != 4 || item.IsStripped) throw new InvalidOperationException("Prefab 覆寫與非完整 Transform 目前保持唯讀。");
        var owner = Items.FirstOrDefault(obj => obj.TransformId == id);
        if (owner is null || !CanEditItem(owner)) throw new InvalidOperationException("Prefab 連接、未知結構或不一致的物件參照保持唯讀。");
    }
    private bool Connected(long id, string type)
    {
        foreach (var field in new[] { "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset" })
        {
            if (HasField(id, type, field, "fileID") && (!long.TryParse(Scalar(id, "", type, field, "fileID"), CultureInfo.InvariantCulture, out var value) || value != 0)) return true;
            if (HasField(id, type, field, "guid") && Scalar(id, "", type, field, "guid").Any(c => c != '0')) return true;
        }
        return false;
    }

    private string Scalar(long id, string fallback, params string[] path)
    { try { return document.GetScalar(id, path); } catch (InvalidDataException) { return fallback; } }
    private long Integer(long id, long fallback, params string[] path) => long.TryParse(Scalar(id, "", path), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;
    private float Float(long id, float fallback, params string[] path) => float.TryParse(Scalar(id, "", path), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && float.IsFinite(n) ? n : fallback;
    private Vector3 ReadVector(long id, string field, Vector3 fallback) => new(
        Float(id, fallback.X, "Transform", field, "x"), Float(id, fallback.Y, "Transform", field, "y"), Float(id, fallback.Z, "Transform", field, "z"));
    private Quaternion ReadRotation(long id) => new(Float(id, 0, "Transform", "m_LocalRotation", "x"), Float(id, 0, "Transform", "m_LocalRotation", "y"),
        Float(id, 0, "Transform", "m_LocalRotation", "z"), Float(id, 1, "Transform", "m_LocalRotation", "w"));

    private IReadOnlyList<SceneItem> ReadItems()
    {
        var result = new List<SceneItem>();
        var components = new Dictionary<long, List<UnityYamlObject>>();
        foreach (var obj in document.Objects.Where(obj => !obj.IsStripped && obj.ClassId is 4 or 20 or 23 or 33 or 64 or 65 or 108 or 212 or 218))
        {
            var go = Integer(obj.FileId, 0, ClassName(obj.ClassId), "m_GameObject", "fileID");
            if (go == 0) continue;
            if (!components.TryGetValue(go, out var list)) components.Add(go, list = []);
            list.Add(obj);
        }
        foreach (var obj in document.Objects.Where(obj => obj.ClassId == 1 && !obj.IsStripped))
        {
            var list = components.GetValueOrDefault(obj.FileId) ?? [];
            var transform = list.FirstOrDefault(item => item.ClassId == 4);
            var id = transform?.FileId ?? obj.FileId;
            var mesh = list.FirstOrDefault(item => item.ClassId == 33);
            var box = list.FirstOrDefault(item => item.ClassId == 65);
            Vector3? size = box is null ? null : new Vector3(Float(box.FileId, 1, "BoxCollider", "m_Size", "x"), Float(box.FileId, 1, "BoxCollider", "m_Size", "y"), Float(box.FileId, 1, "BoxCollider", "m_Size", "z"));
            Vector3? center = box is null ? null : new Vector3(Float(box.FileId, 0, "BoxCollider", "m_Center", "x"), Float(box.FileId, 0, "BoxCollider", "m_Center", "y"), Float(box.FileId, 0, "BoxCollider", "m_Center", "z"));
            result.Add(new SceneItem(id, obj.FileId, transform?.FileId, transform is null ? 0 : Integer(id, 0, "Transform", "m_Father", "fileID"),
                Scalar(obj.FileId, "GameObject " + obj.FileId, "GameObject", "m_Name"), transform is null ? Vector3.Zero : ReadVector(id, "m_LocalPosition", Vector3.Zero),
                transform is null ? Quaternion.Identity : ReadRotation(id), transform is null ? Vector3.One : ReadVector(id, "m_LocalScale", Vector3.One),
                Integer(obj.FileId, 1, "GameObject", "m_IsActive") != 0, (int)Integer(obj.FileId, 0, "GameObject", "m_Layer"),
                (uint)Integer(obj.FileId, 0, "GameObject", "m_StaticEditorFlags"), false, list.Select(c => ClassName(c.ClassId)).ToArray(),
                mesh is null ? null : Scalar(mesh.FileId, "", "MeshFilter", "m_Mesh", "guid"),
                mesh is null ? 0 : Integer(mesh.FileId, 0, "MeshFilter", "m_Mesh", "fileID"), size, center));
        }
        foreach (var obj in document.Objects.Where(obj => obj.ClassId == 1001))
        {
            var guid = Scalar(obj.FileId, "", "PrefabInstance", "m_SourcePrefab", "guid");
            result.Add(new SceneItem(obj.FileId, 0, null, 0, "Prefab · " + (guid.Length >= 8 ? guid[..8] : obj.FileId.ToString(CultureInfo.InvariantCulture)),
                Vector3.Zero, Quaternion.Identity, Vector3.One, true, 0, 0, true, ["PrefabInstance（保留原文，覆寫唯讀）"]));
        }
        return result;
    }
    private static string ClassName(int id) => id switch { 4 => "Transform", 20 => "Camera", 23 => "MeshRenderer", 33 => "MeshFilter", 64 => "MeshCollider", 65 => "BoxCollider", 108 => "Light", 212 => "SpriteRenderer", 218 => "Terrain", _ => "Component " + id };
    private sealed class ActionCommand(string description, Action execute, Action undo) : IEditCommand
    {
        public string Description => description;
        public void Execute() => execute();
        public void Undo() => undo();
    }
}

using System.Globalization;
using Godot;
using SceneWorkbench.Unity;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace SceneWorkbench.Desktop;

public partial class InspectorPanel : VBoxContainer
{
    private readonly VBoxContainer fields = new();
    private readonly Label error = EditorTheme.Label("", EditorTheme.Error);
    private SceneEditSession? session;
    private SceneItem? item;
    private LineEdit? nameField;
    private LineEdit[] position = [], rotation = [], scale = [];
    private CheckBox? active;
    private OptionButton? layer;
    private readonly List<CheckBox> staticBits = new();
    private string[] initialRotation = [];
    private bool editable, populated, draft;
    public Action? Edited;
    public Action<string>? Failed;

    public override void _Ready()
    {
        AddChild(EditorTheme.Label("Inspector  屬性"));
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(scroll);
        fields.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(fields);
        error.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(error);
        ShowEmpty();
    }

    private void Clear()
    {
        populated = false; draft = false;
        foreach (var child in fields.GetChildren()) { fields.RemoveChild(child); child.QueueFree(); }
        staticBits.Clear(); nameField = null; session = null; item = null;
        position = []; rotation = []; scale = []; initialRotation = [];
        error.Text = "";
    }
    public void ShowEmpty()
    {
        Clear();
        AddNote("選取 Hierarchy 物件以編輯屬性。\n雙擊 Project 的場景檔即可開啟。", EditorTheme.Muted);
    }

    public void ShowObject(SceneEditSession scene, SceneItem selected, bool canEdit, IReadOnlyList<string> layers)
    {
        Clear(); session = scene; item = selected; editable = canEdit && !selected.IsPrefab;
        AddNote(selected.IsPrefab ? "Prefab 覆寫目前唯讀，原始資料完整保留。" : canEdit ? "完整物件 · 可離線編輯" : "此物件的來源或格式保持唯讀。僅工作複本中已識別、未連接 Prefab 的完整 GameObject／Transform 可編輯。", canEdit && !selected.IsPrefab ? EditorTheme.Muted : EditorTheme.Warning);
        nameField = Field("名稱", selected.Name, editable);
        active = new CheckBox { Text = "Active  啟用物件", ButtonPressed = selected.Active,
            Disabled = !editable || !scene.HasField(selected.GameObjectId, "GameObject", "m_IsActive") };
        active.Toggled += _ => MarkDraft(); fields.AddChild(active);
        var layerRow = new HBoxContainer();
        layerRow.AddChild(new Label { Text = "Layer", CustomMinimumSize = new Vector2(60, 0) });
        layer = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, Disabled = !editable || !scene.HasField(selected.GameObjectId, "GameObject", "m_Layer"), AccessibilityName = "物件 Layer" };
        for (var i = 0; i < 32; i++) layer.AddItem(i + " · " + (i < layers.Count && layers[i].Length > 0 ? layers[i] : "未命名"), i);
        layer.Select(Math.Clamp(selected.Layer, 0, 31)); layer.ItemSelected += _ => MarkDraft();
        layer.GetPopup().MaxSize = new Vector2I(420, 380);
        layerRow.AddChild(layer); fields.AddChild(layerRow);
        if (selected.TransformId.HasValue)
        {
            Heading("Transform  區域座標");
            position = VectorFields("Position", selected.Position, editable);
            var q = selected.Rotation;
            var godot = new Quaternion(-q.X, -q.Y, q.Z, q.W);
            var euler = godot.LengthSquared() > .000001f ? godot.Normalized().GetEuler(EulerOrder.Yxz) : Vector3.Zero;
            var degrees = new NVector3(-Mathf.RadToDeg(euler.X), -Mathf.RadToDeg(euler.Y), Mathf.RadToDeg(euler.Z));
            rotation = VectorFields("Rotation °", degrees, editable);
            initialRotation = rotation.Select(f => f.Text).ToArray();
            scale = VectorFields("Scale", selected.Scale, editable);
            AddNote("可拖曳 Scene 的彩色座標軸移動；Ctrl 拖曳以 0.5 單位吸附。", EditorTheme.Muted);
        }
        Heading("Static  旗標");
        var bitNames = new[] { "Contribute GI", "Occluder Static", "Batching Static", "Navigation Static", "Occludee Static", "Off Mesh Link", "Reflection Probe" };
        for (var bit = 0; bit < bitNames.Length; bit++)
        {
            var toggle = new CheckBox { Text = bitNames[bit], ButtonPressed = (selected.StaticFlags & (1u << bit)) != 0,
                Disabled = !editable || !scene.HasField(selected.GameObjectId, "GameObject", "m_StaticEditorFlags") };
            toggle.Toggled += _ => MarkDraft(); staticBits.Add(toggle); fields.AddChild(toggle);
        }
        AddNote("既有尋路與光照結果在相關場景變動後須重新生成。此版不生成烘焙資料。", EditorTheme.Muted);
        var buttons = new HBoxContainer();
        var apply = EditorTheme.Button("套用變更", () => TryCommit(), "套用到目前場景；Ctrl+S 才寫入磁碟"); apply.Disabled = !editable;
        buttons.AddChild(apply);
        buttons.AddChild(EditorTheme.Button("還原輸入", () => ShowObject(scene, scene.GetItem(selected.Id), canEdit, layers)));
        fields.AddChild(buttons);
        Heading("元件（其餘欄位保留）");
        foreach (var component in selected.Components) AddNote(component, EditorTheme.Muted);
        AddNote("原生 ID · " + selected.Id.ToString(CultureInfo.InvariantCulture), EditorTheme.Muted);
        populated = true;
    }

    public bool TryCommit()
    {
        if (DisplayServer.ImeGetText().Length > 0) return false;
        if (!draft || !editable || session is null || item is null || nameField is null) return true;
        try
        {
            var name = nameField.Text;
            if (string.IsNullOrWhiteSpace(name) || name.Length > 1024) throw new ArgumentException("名稱須為 1 至 1024 字元。");
            var pos = item.Position; var scl = item.Scale; var rot = item.Rotation;
            if (item.TransformId.HasValue)
            {
                pos = ReadVector(position); scl = ReadVector(scale);
                if (!rotation.Select(f => f.Text).SequenceEqual(initialRotation))
                {
                    var angles = ReadVector(rotation);
                    var q = Basis.FromEuler(new Vector3(-Mathf.DegToRad(angles.X), -Mathf.DegToRad(angles.Y), Mathf.DegToRad(angles.Z)), EulerOrder.Yxz).GetRotationQuaternion();
                    rot = new NQuaternion(-q.X, -q.Y, q.Z, q.W);
                }
            }
            var flags = item.StaticFlags;
            for (var i = 0; i < staticBits.Count; i++) flags = staticBits[i].ButtonPressed ? flags | (1u << i) : flags & ~(1u << i);
            // Validate all input before dispatching commands.
            session.SetName(item.GameObjectId, name);
            if (item.TransformId.HasValue) { session.SetPosition(item.TransformId.Value, pos); session.SetRotation(item.TransformId.Value, rot); session.SetScale(item.TransformId.Value, scl); }
            if (active is { Disabled: false }) session.SetActive(item.GameObjectId, active.ButtonPressed);
            if (layer is { Disabled: false }) session.SetLayer(item.GameObjectId, layer.Selected);
            if (staticBits.Any(f => !f.Disabled)) session.SetStaticFlags(item.GameObjectId, flags);
            item = session.GetItem(item.Id);
            initialRotation = rotation.Select(f => f.Text).ToArray();
            draft = false; error.Text = ""; Edited?.Invoke(); return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException or FormatException)
        { error.Text = ex.Message; Failed?.Invoke(ex.Message); return false; }
    }

    public void ShowAsset(string relative, string absolute)
    {
        Clear();
        Heading(Path.GetFileName(relative));
        AddNote(relative, EditorTheme.Text);
        if (File.Exists(absolute))
        {
            var info = new FileInfo(absolute);
            AddNote($"{info.Length / 1024.0:N1} KB · {info.Extension}", EditorTheme.Muted);
            if (new[] { ".png", ".jpg", ".jpeg", ".tga", ".webp", ".bmp" }.Contains(info.Extension.ToLowerInvariant()))
            {
                var image = Image.LoadFromFile(absolute);
                if (image is not null)
                {
                    fields.AddChild(new TextureRect { Texture = ImageTexture.CreateFromImage(image), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(200, 220) });
                    AddNote(image.GetWidth() + " × " + image.GetHeight(), EditorTheme.Muted);
                }
            }
            if (info.Extension.Equals(".fbx", StringComparison.OrdinalIgnoreCase) || info.Extension is ".glb" or ".gltf")
                AddNote("雙擊可預覽模型來源。Unity 匯入設定與 ASE 畫面尚未適配。", EditorTheme.Warning);
            var meta = absolute + ".meta";
            if (File.Exists(meta))
            {
                SceneWorkbench.Core.SafePath.RejectLinks(meta);
                var lines = File.ReadLines(meta).Take(12).ToArray();
                var guid = lines.FirstOrDefault(line => line.StartsWith("guid:", StringComparison.Ordinal));
                if (guid is not null) AddNote(guid, EditorTheme.Muted);
                AddNote("匯入設定目前唯讀；原 .meta 保留。", EditorTheme.Muted);
            }
        }
    }
    private LineEdit Field(string label, string value, bool enabled)
    {
        fields.AddChild(EditorTheme.Label(label));
        var field = new LineEdit { Text = value, Editable = enabled, AccessibilityName = label, MaxLength = label == "名稱" ? 1024 : 64 };
        field.TextChanged += _ => MarkDraft(); field.TextSubmitted += _ => TryCommit(); fields.AddChild(field); return field;
    }
    private LineEdit[] VectorFields(string label, NVector3 value, bool enabled)
    {
        fields.AddChild(EditorTheme.Label(label));
        var row = new HBoxContainer(); var result = new List<LineEdit>(); var values = new[] { value.X, value.Y, value.Z };
        for (var i = 0; i < 3; i++)
        {
            var axis = new[] { "X", "Y", "Z" }[i];
            row.AddChild(EditorTheme.Label(axis, i == 0 ? new Color(.95f, .55f, .5f) : i == 1 ? new Color(.6f, .85f, .6f) : EditorTheme.Focus));
            var field = new LineEdit { Text = values[i].ToString("R", CultureInfo.InvariantCulture), Editable = enabled,
                SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(65, 0), AccessibilityName = label + " " + axis, MaxLength = 32 };
            field.TextChanged += _ => MarkDraft(); field.TextSubmitted += _ => TryCommit(); row.AddChild(field); result.Add(field);
        }
        fields.AddChild(row); return result.ToArray();
    }
    private static NVector3 ReadVector(LineEdit[] fields)
    {
        var values = fields.Select(field => float.TryParse(field.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && float.IsFinite(number)
            ? number : throw new ArgumentException(field.AccessibilityName + " 請輸入有限數字，使用小數點。")).ToArray();
        return new NVector3(values[0], values[1], values[2]);
    }
    private void Heading(string text) { fields.AddChild(new HSeparator()); fields.AddChild(EditorTheme.Label(text)); }
    private void AddNote(string text, Color color)
    { var label = EditorTheme.Label(text, color); label.AutowrapMode = TextServer.AutowrapMode.WordSmart; fields.AddChild(label); }
    private void MarkDraft() { if (populated) draft = true; }
}

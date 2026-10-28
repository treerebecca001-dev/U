using Godot;
using SceneWorkbench.Unity;
using NVector3 = System.Numerics.Vector3;

namespace SceneWorkbench.Desktop;

public partial class SceneView : SubViewportContainer
{
    private readonly SubViewport viewport = new() { OwnWorld3D = true, HandleInputLocally = false, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
    private readonly Node3D world = new(), content = new(), gizmo = new();
    private readonly Camera3D camera = new() { Fov = 55, Near = .05f, Far = 50000 };
    private readonly Dictionary<long, Node3D> objects = new();
    private long? selected;
    private float yaw = .65f, pitch = .55f, distance = 18;
    private Vector3 focus = Vector3.Zero;
    private bool orbiting, panning;
    private int dragAxis = -1;
    private Vector2 dragStart, axisScreen;
    private Vector3 originalWorld;
    private Node3D? assetPreview;
    private readonly MeshInstance3D outline = new();
    public Action<long>? Selected;
    public Action<long, NVector3>? Moved;
    public Action<string>? Notice;
    public string ModeDescription { get; private set; } = "場景結構 · Transform／碰撞框；Prefab、ASE 尚未預覽";

    public override void _Ready()
    {
        Stretch = true;
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        CustomMinimumSize = new Vector2(300, 220);
        AddChild(viewport);
        viewport.AddChild(world);
        world.AddChild(content);
        world.AddChild(gizmo);
        world.AddChild(camera);
        camera.Current = true;
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = EditorTheme.Canvas,
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White,
            AmbientLightEnergy = .75f, TonemapMode = Godot.Environment.ToneMapper.Filmic
        };
        world.AddChild(new WorldEnvironment { Environment = environment });
        world.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-48, -28, 0), LightEnergy = 1.3f });
        world.AddChild(Grid());
        outline.MaterialOverride = LineMaterial(EditorTheme.Focus);
        gizmo.AddChild(outline);
        UpdateCamera();
    }

    public void ShowScene(IReadOnlyList<SceneItem> sceneItems, bool frame = false)
    {
        ClearAsset();
        foreach (var node in objects.Values) { node.GetParent()?.RemoveChild(node); node.QueueFree(); }
        objects.Clear();
        foreach (var item in sceneItems.Where(item => !item.IsPrefab))
        {
            var node = new Node3D { Name = "Object_" + item.Id, Visible = item.Active };
            objects.Add(item.Id, node);
            content.AddChild(node);
            node.Position = Convert(item.Position);
            var q = item.Rotation;
            if (float.IsFinite(q.LengthSquared()) && q.LengthSquared() > .000001f)
                node.Quaternion = new Quaternion(-q.X, -q.Y, q.Z, q.W).Normalized();
            node.Scale = ConvertScale(item.Scale);
            Mesh mesh;
            Vector3 center = Vector3.Zero;
            var solid = false;
            if (item.ColliderSize.HasValue)
            {
                mesh = new BoxMesh { Size = ConvertScale(item.ColliderSize.Value).Abs().Max(new Vector3(.001f, .001f, .001f)) };
                center = Convert(item.ColliderCenter ?? NVector3.Zero);
                solid = item.MeshFileId == 10202 && item.MeshGuid?.StartsWith("0000000000000000", StringComparison.Ordinal) == true;
            }
            else if (item.MeshFileId == 10202 && item.MeshGuid?.StartsWith("0000000000000000", StringComparison.Ordinal) == true)
            { mesh = new BoxMesh(); solid = true; }
            else mesh = new SphereMesh { Radius = .12f, Height = .24f, RadialSegments = 12, Rings = 6 };
            var material = new StandardMaterial3D { AlbedoColor = solid ? new Color(.52f, .59f, .66f) : new Color(.38f, .57f, .68f), Roughness = .8f };
            if (!solid && item.ColliderSize.HasValue)
            {
                material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                material.AlbedoColor = new Color(.45f, .65f, .8f, .18f);
            }
            node.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = center });
        }
        foreach (var item in sceneItems.Where(item => !item.IsPrefab && item.ParentId != 0))
            if (objects.TryGetValue(item.Id, out var node) && objects.TryGetValue(item.ParentId, out var parent) && !node.IsAncestorOf(parent))
                node.Reparent(parent, false);
        ModeDescription = "場景結構 · Transform／碰撞框；Prefab、ASE 尚未預覽";
        UpdateGizmo();
        if (frame) FrameAll();
    }

    public void Select(long? id)
    {
        selected = id;
        UpdateGizmo();
    }

    public void FrameSelection()
    {
        if (selected.HasValue && objects.TryGetValue(selected.Value, out var node))
        { focus = node.GlobalPosition; distance = Math.Max(4, node.GlobalBasis.Scale.Length() * 3); UpdateCamera(); }
    }
    public void FrameAll()
    {
        var points = objects.Values.Select(node => node.GlobalPosition).Where(v => v.IsFinite()).ToArray();
        if (points.Length == 0) { focus = Vector3.Zero; distance = 18; }
        else
        {
            var min = points.Aggregate((a, b) => a.Min(b));
            var max = points.Aggregate((a, b) => a.Max(b));
            focus = (min + max) / 2;
            distance = Math.Clamp(min.DistanceTo(max) * 1.25f + 8, 8, 15000);
        }
        UpdateCamera();
    }

    public void ShowModel(Node3D node)
    {
        ClearAsset();
        content.Visible = false;
        gizmo.Visible = false;
        assetPreview = node;
        world.AddChild(node);
        var bounds = new Aabb();
        var hasBounds = false;
        void Visit(Node parent)
        {
            if (parent is MeshInstance3D mesh && mesh.Mesh is not null)
            {
                var aabb = mesh.GlobalTransform * mesh.GetAabb();
                bounds = hasBounds ? bounds.Merge(aabb) : aabb;
                hasBounds = true;
            }
            foreach (var child in parent.GetChildren()) Visit(child);
        }
        Visit(node);
        focus = hasBounds ? bounds.GetCenter() : Vector3.Zero;
        distance = hasBounds ? Math.Max(2, bounds.Size.Length() * 1.5f) : 10;
        ModeDescription = "模型來源預覽 · 未套用 Unity 匯入設定、ASE 與場景綁定";
        UpdateCamera();
    }

    private void ClearAsset()
    {
        if (assetPreview is not null) { world.RemoveChild(assetPreview); assetPreview.QueueFree(); assetPreview = null; }
        content.Visible = true;
        gizmo.Visible = true;
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton b)
        {
            GrabFocus();
            if (b.ButtonIndex == MouseButton.Right) orbiting = b.Pressed;
            if (b.ButtonIndex == MouseButton.Middle) panning = b.Pressed;
            if (b.Pressed && b.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            { distance = Math.Clamp(distance * (b.ButtonIndex == MouseButton.WheelUp ? .87f : 1.15f), .4f, 25000); UpdateCamera(); }
            if (b.ButtonIndex == MouseButton.Left)
            {
                if (b.Pressed && assetPreview is null)
                {
                    if (selected.HasValue && objects.TryGetValue(selected.Value, out var node))
                    {
                        var origin = camera.UnprojectPosition(node.GlobalPosition);
                        if (!camera.IsPositionBehind(node.GlobalPosition))
                        {
                            var length = distance * .1f;
                            for (var axis = 0; axis < 3; axis++)
                            {
                                var end = camera.UnprojectPosition(node.GlobalPosition + Axis(axis) * length);
                                if (DistanceToSegment(b.Position, origin, end) < 9 && b.Position.DistanceTo(origin) > 8)
                                {
                                    dragAxis = axis; dragStart = b.Position; axisScreen = (end - origin).Normalized();
                                    originalWorld = node.GlobalPosition;
                                    AcceptEvent(); return;
                                }
                            }
                        }
                    }
                    var hit = objects.Where(pair => !camera.IsPositionBehind(pair.Value.GlobalPosition))
                        .Select(pair => (pair.Key, Distance: camera.UnprojectPosition(pair.Value.GlobalPosition).DistanceTo(b.Position)))
                        .Where(pair => pair.Distance < 22).OrderBy(pair => pair.Distance).FirstOrDefault();
                    if (hit != default) Selected?.Invoke(hit.Key);
                }
                else if (!b.Pressed && dragAxis >= 0 && selected.HasValue && objects.TryGetValue(selected.Value, out var moved))
                {
                    var value = moved.Position;
                    dragAxis = -1;
                    Moved?.Invoke(selected.Value, new NVector3(value.X, value.Y, -value.Z));
                }
            }
            AcceptEvent();
        }
        if (e is InputEventMouseMotion motion)
        {
            if (orbiting) { yaw -= motion.Relative.X * .007f; pitch = Math.Clamp(pitch + motion.Relative.Y * .007f, -1.45f, 1.45f); UpdateCamera(); }
            if (panning)
            { focus += (-camera.GlobalBasis.X * motion.Relative.X + camera.GlobalBasis.Y * motion.Relative.Y) * distance / Math.Max(1, Size.Y); UpdateCamera(); }
            if (dragAxis >= 0 && selected.HasValue && objects.TryGetValue(selected.Value, out var node))
            {
                var pixels = (motion.Position - dragStart).Dot(axisScreen);
                var units = pixels * distance * 2 * Mathf.Tan(Mathf.DegToRad(camera.Fov / 2)) / Math.Max(1, Size.Y);
                if (motion.CtrlPressed) units = Mathf.Snapped(units, .5f);
                node.GlobalPosition = originalWorld + Axis(dragAxis) * units;
                UpdateGizmo();
            }
            AcceptEvent();
        }
    }

    private void UpdateCamera()
    {
        camera.Position = focus + new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch)) * distance;
        camera.LookAt(focus, Vector3.Up);
        if (dragAxis < 0) UpdateGizmo();
    }

    private void UpdateGizmo()
    {
        foreach (var child in gizmo.GetChildren().Where(n => n != outline)) { gizmo.RemoveChild(child); child.QueueFree(); }
        outline.Visible = selected.HasValue && objects.ContainsKey(selected.Value) && assetPreview is null;
        if (!outline.Visible || !selected.HasValue) return;
        var node = objects[selected.Value];
        var center = node.GlobalPosition;
        var length = distance * .1f;
        var lines = new ImmediateMesh();
        lines.SurfaceBegin(Mesh.PrimitiveType.Lines);
        for (var axis = 0; axis < 3; axis++)
        {
            var color = axis == 0 ? new Color(.95f, .35f, .32f) : axis == 1 ? new Color(.45f, .85f, .45f) : new Color(.35f, .65f, 1);
            var axisMesh = new ImmediateMesh();
            axisMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
            axisMesh.SurfaceAddVertex(center); axisMesh.SurfaceAddVertex(center + Axis(axis) * length);
            axisMesh.SurfaceEnd();
            gizmo.AddChild(new MeshInstance3D { Mesh = axisMesh, MaterialOverride = LineMaterial(color) });
        }
        var extent = node.GlobalBasis.Scale.Abs().Max(Vector3.One * .4f) * .55f;
        var corners = new Vector3[8];
        for (var i = 0; i < 8; i++) corners[i] = center + new Vector3((i & 1) == 0 ? -extent.X : extent.X, (i & 2) == 0 ? -extent.Y : extent.Y, (i & 4) == 0 ? -extent.Z : extent.Z);
        for (var i = 0; i < 8; i++) for (var axis = 0; axis < 3; axis++) if ((i & (1 << axis)) == 0) { lines.SurfaceAddVertex(corners[i]); lines.SurfaceAddVertex(corners[i | (1 << axis)]); }
        lines.SurfaceEnd(); outline.Mesh = lines;
    }

    private static MeshInstance3D Grid()
    {
        var mesh = new ImmediateMesh(); mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        for (var i = -50; i <= 50; i++)
        {
            mesh.SurfaceSetColor(i == 0 ? new Color(.55f, .34f, .34f) : new Color(.26f, .27f, .28f));
            mesh.SurfaceAddVertex(new Vector3(-50, 0, i)); mesh.SurfaceAddVertex(new Vector3(50, 0, i));
            mesh.SurfaceSetColor(i == 0 ? new Color(.34f, .4f, .55f) : new Color(.26f, .27f, .28f));
            mesh.SurfaceAddVertex(new Vector3(i, 0, -50)); mesh.SurfaceAddVertex(new Vector3(i, 0, 50));
        }
        mesh.SurfaceEnd();
        return new MeshInstance3D { Mesh = mesh, MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true } };
    }
    private static StandardMaterial3D LineMaterial(Color color) => new() { AlbedoColor = color, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true };
    private static Vector3 Axis(int axis) => axis == 0 ? Vector3.Right : axis == 1 ? Vector3.Up : Vector3.Back;
    private static Vector3 Convert(NVector3 p) => new(p.X, p.Y, -p.Z);
    private static Vector3 ConvertScale(NVector3 p) => new(p.X, p.Y, p.Z);
    private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    { var v = end - start; return point.DistanceTo(start + v * Math.Clamp((point - start).Dot(v) / Math.Max(.001f, v.LengthSquared()), 0, 1)); }
}

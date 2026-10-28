using System.Globalization;
using System.Text.Json;
using Godot;
using SceneWorkbench.Core;
using SceneWorkbench.Unity;

namespace SceneWorkbench.Desktop;

/// <summary>Opt-in native-engine UI integration driver. No OS input, network listener or Unity process.</summary>
public partial class DesktopSmokeQa : Node
{
    private readonly Workbench workbench;
    private readonly string output;
    private readonly string? initialProject, previous;
    private readonly string relativeScene;
    private string projectRoot = "";
    private long target;
    private float expected;
    private readonly List<string> checks = [];
    public DesktopSmokeQa(Workbench owner, string[] args)
    {
        workbench = owner;
        string? Arg(string key) { var i = Array.IndexOf(args, key); return i < 0 || i + 1 == args.Length ? null : args[i + 1]; }
        output = Path.GetFullPath(Arg("--desktop-qa-output") ?? throw new ArgumentException("QA output is required."));
        initialProject = Arg("--project"); previous = Arg("--desktop-qa-previous");
        relativeScene = Arg("--scene") ?? "Assets/WorkbenchDemo.unity";
    }
    public override async void _Ready()
    {
        try
        {
            Directory.CreateDirectory(output);
            Trace("Native GUI integration driver started.");
            if (File.Exists(Path.Combine(output, "report.json"))) throw new IOException("QA result must not overwrite an existing report.");
            await Frames(3);
            if (initialProject is null)
            {
                Trace("Click demo project button.");
                await Click(Find<Button>(b => b.Text == "示範專案"));
                projectRoot = Path.Combine(FindWorkspace(), "Workspace", "Demo");
            }
            else projectRoot = Path.GetFullPath(initialProject);
            SafePath.RequireDisjoint(projectRoot, output);
            var save = Find<Button>(b => b.Text == "儲存場景");
            await Wait(() => !save.Disabled && File.Exists(Path.Combine(projectRoot, relativeScene)), "project load", 180);
            Trace("Project and scene loaded.");
            var project = ProjectSession.Open(projectRoot);
            var copy = WorkCopy.Open(projectRoot);
            var loaded = new SceneEditSession(project, relativeScene);
            var selected = loaded.Items.First(i => loaded.CanEditItem(i));
            if (initialProject is null) selected = loaded.GetItem(301);
            target = selected.Id;
            QaRecord? earlier = previous is null ? null : JsonSerializer.Deserialize<QaRecord>(File.ReadAllText(previous), JsonFile.Options);
            if (earlier is not null)
            {
                if (earlier.ProjectRoot != projectRoot || earlier.Scene != relativeScene) throw new InvalidDataException("Readback identity differs.");
                target = earlier.FileId; expected = earlier.ExpectedX; selected = loaded.GetItem(target);
            }
            if (earlier is null)
            {
                var search = Find<LineEdit>(f => f.AccessibilityName == "搜尋 Hierarchy 物件");
                await Click(search); await SelectAllAndType("NoSuchObject_QA_7181"); await Frames(2);
                await Capture("no-results.png");
                await Click(Find<Button>(b => b.Text == "清除" && b.GetParent() == search.GetParent()));
                checks.Add("Hierarchy no-results state and native clear action exercised");
            }
            var hierarchy = Find<Tree>(t => t != Find<ProjectPanel>(_ => true).GetChildren().OfType<Tree>().FirstOrDefault());
            var item = FindTreeItem(hierarchy.GetRoot(), target) ?? throw new InvalidDataException("Hierarchy target is missing.");
            var rect = hierarchy.GetItemAreaRect(item, 0);
            await ClickAt(hierarchy.GetGlobalRect().Position + rect.Position + new Vector2(Math.Min(90, rect.Size.X / 2), rect.Size.Y / 2));
            await Wait(() => FindAll<LineEdit>().Any(field => field.AccessibilityName == "Position X"), "Inspector selection");
            Trace("Inspector selected.");
            var x = Find<LineEdit>(field => field.AccessibilityName == "Position X");
            if (!x.Editable) throw new InvalidDataException("Inspector target is read-only.");
            checks.Add("Hierarchy selected native signed fileID " + target.ToString(CultureInfo.InvariantCulture));
            var sourcePath = SafePath.Resolve(copy.Info.SourceRoot, relativeScene);
            var sourceHash = Hashing.File(sourcePath);
            var scenePath = project.Resolve(relativeScene);
            var metaHash = Hashing.File(project.Resolve(relativeScene + ".meta"));
            if (earlier is null)
            {
                await Click(Find<OptionButton>(b => b.AccessibilityName == "物件 Layer"));
                await Frames(2); await Capture("layer-popup.png"); await Press(Key.Escape);
                checks.Add("Layer dropdown opened, rendered, and dismissed by keyboard");
                var original = File.ReadAllBytes(scenePath);
                var originalHash = Hashing.Bytes(original);
                File.WriteAllBytes(Path.Combine(output, "before.unity"), original);
                await Click(x); await SelectAllAndType("NaN"); await Press(Key.Enter);
                if (Hashing.File(scenePath) != originalHash) throw new InvalidDataException("Invalid numeric input changed the native file.");
                await Capture("invalid-input.png");
                checks.Add("Invalid numeric input retained and native file remained unchanged");
                expected = selected.Position.X + .125f;
                await Click(x); await SelectAllAndType(expected.ToString("R", CultureInfo.InvariantCulture));
                await Press(Key.Enter);
                await Click(Find<Button>(b => b.Text == "復原"));
                await Click(Find<Button>(b => b.Text == "重做"));
                checks.Add("Native Undo and Redo buttons exercised before save");
                await Click(save);
                Trace("Save button activated.");
                await Wait(() => !save.Disabled && new SceneEditSession(project, relativeScene).GetItem(target).Position.X == expected, "native scene save", 300);
                var saved = File.ReadAllBytes(scenePath);
                var before = UnityYamlDocument.Parse(original); var after = UnityYamlDocument.Parse(saved);
                if (!before.Objects.Select(i => (i.ClassId, i.FileId, i.IsStripped)).SequenceEqual(after.Objects.Select(i => (i.ClassId, i.FileId, i.IsStripped)))) throw new InvalidDataException("Native identities changed.");
                foreach (var obj in before.Objects.Where(i => i.FileId != target))
                    if (before.GetRawBody(obj.FileId) != after.GetRawBody(obj.FileId)) throw new InvalidDataException("Neighbour document changed.");
                checks.Add("Coordinate entered through Godot LineEdit input; native Save button activated");
                checks.Add("Fresh native document reload matches coordinate; all other Unity documents unchanged");
                if (metaHash != Hashing.File(project.Resolve(relativeScene + ".meta")) || sourceHash != Hashing.File(sourcePath)) throw new InvalidDataException("Scene meta or original source changed.");
                checks.Add("Scene GUID/meta and original source unchanged");
                await Capture("saved.png");
                WriteReport(new QaRecord(projectRoot, relativeScene, target, expected, originalHash, Hashing.Bytes(saved), sourceHash, metaHash,
                    Path.Combine(output, "before.unity"), "PASSED_EDIT", false));
            }
            else
            {
                if (loaded.GetItem(target).Position.X != expected || float.Parse(x.Text, CultureInfo.InvariantCulture) != expected || Hashing.File(scenePath) != earlier.SavedSha256) throw new InvalidDataException("New process did not reload saved coordinate.");
                checks.Add("Separate application process reopened saved scene and populated Inspector from disk");
                await Capture("reopened.png");
                var original = File.ReadAllBytes(earlier.BeforeFile);
                if (Hashing.Bytes(original) != earlier.BeforeSha256) throw new InvalidDataException("Restoration backup identity mismatch.");
                var originalDocument = UnityYamlDocument.Parse(original);
                var originalX = originalDocument.GetScalar(target, "Transform", "m_LocalPosition", "x");
                await Click(x); await SelectAllAndType(originalX); await Press(Key.Enter); await Click(save);
                await Wait(() => !save.Disabled && Hashing.File(scenePath) == earlier.BeforeSha256, "restore through normal GUI save", 300);
                if (Hashing.File(scenePath) != earlier.BeforeSha256 || Hashing.File(project.Resolve(relativeScene + ".meta")) != earlier.MetaSha256 || Hashing.File(sourcePath) != earlier.SourceSha256) throw new InvalidDataException("Restoration or source protection mismatch.");
                checks.Add("Test scene restored byte-for-byte through normal GUI input and save; original source and meta preserved");
                WriteReport(earlier with { Phase = "PASSED_REOPEN_AND_RESTORE", Restored = true });
            }
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            try { await Capture("failure.png"); } catch (Exception) { }
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "failure.json"), JsonSerializer.Serialize(new { status = "FAILED", message = ex.ToString(), checks }, JsonFile.Options));
            GD.PushError(ex.ToString()); GetTree().Quit(1);
        }
    }
    private void Trace(string message) => File.AppendAllText(Path.Combine(output, "trace.log"), DateTimeOffset.UtcNow.ToString("O") + " " + message + "\n");
    private void WriteReport(QaRecord data)
    {
        var modules = System.Diagnostics.Process.GetCurrentProcess().Modules.Cast<System.Diagnostics.ProcessModule>().Select(module => module.ModuleName ?? "").ToArray();
        if (modules.Any(name => name.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase) || name.StartsWith("UnityEditor", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Unexpected Unity runtime module.");
        File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(data, JsonFile.Options));
        File.WriteAllText(Path.Combine(output, "ui-checks.json"), JsonSerializer.Serialize(new
        {
            generatedUtc = DateTimeOffset.UtcNow, appVersion = Workbench.Version, checks, loadedModules = modules,
            interaction = "Godot native GUI event dispatch in the actual rendered application, not OS mouse automation",
            renderer = RenderingServer.GetCurrentRenderingMethod(), display = DisplayServer.GetName(), cleanMachineWithoutUnityVerified = false,
            unityNativeRoundTripVerified = false
        }, JsonFile.Options));
    }
    private async Task Capture(string name)
    {
        await Frames(2); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("Native UI capture requires a rendered application.");
        var image = GetViewport().GetTexture().GetImage();
        var error = image.SavePng(Path.Combine(output, name)); if (error != Error.Ok) throw new IOException("Capture failed: " + error);
    }
    private async Task Click(Control control) => await ClickAt(control.GetGlobalRect().GetCenter());
    private async Task ClickAt(Vector2 point)
    {
        Input.ParseInputEvent(new InputEventMouseMotion { Position = point, GlobalPosition = point }); await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = point, GlobalPosition = point, Pressed = true, ButtonMask = MouseButtonMask.Left }); await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = point, GlobalPosition = point, Pressed = false }); await Frames(2);
    }
    private async Task SelectAllAndType(string text)
    {
        await Press(Key.A, true);
        foreach (var character in text)
        {
            Input.ParseInputEvent(new InputEventKey { Pressed = true, Unicode = character });
            Input.ParseInputEvent(new InputEventKey { Pressed = false, Unicode = character });
        }
        await Frames(1);
    }
    private async Task Press(Key key, bool ctrl = false)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true, CtrlPressed = ctrl }); await Frames(1);
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false, CtrlPressed = ctrl }); await Frames(1);
    }
    private async Task Wait(Func<bool> predicate, string description, int seconds = 30)
    {
        var start = Time.GetTicksMsec();
        while (!predicate())
        {
            if (Time.GetTicksMsec() - start > (ulong)seconds * 1000) throw new TimeoutException("Timed out: " + description);
            await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
        }
        await Frames(2);
    }
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private IEnumerable<T> FindAll<T>() where T : Node
    {
        var pending = new Stack<Node>(); pending.Push(workbench);
        while (pending.Count != 0) { var node = pending.Pop(); if (node is T found) yield return found; foreach (var child in node.GetChildren().Cast<Node>().Reverse()) pending.Push(child); }
    }
    private T Find<T>(Func<T, bool> test) where T : Node => FindAll<T>().First(test);
    private static TreeItem? FindTreeItem(TreeItem? root, long id)
    {
        if (root is null) return null;
        foreach (var child in root.GetChildren())
        {
            if (child.GetMetadata(0).VariantType == Variant.Type.Int && child.GetMetadata(0).AsInt64() == id) return child;
            var found = FindTreeItem(child, id); if (found is not null) return found;
        }
        return null;
    }
    private static string FindWorkspace()
    {
        var executable = Path.GetDirectoryName(OS.GetExecutablePath())!;
        if (Directory.Exists(Path.Combine(executable, "DemoProject"))) return executable;
        for (var d = new DirectoryInfo(ProjectSettings.GlobalizePath("res://")); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "SceneWorkbench.slnx"))) return d.FullName;
        throw new DirectoryNotFoundException("Demo workspace not found.");
    }
    private sealed record QaRecord(string ProjectRoot, string Scene, long FileId, float ExpectedX, string BeforeSha256, string SavedSha256,
        string SourceSha256, string MetaSha256, string BeforeFile, string Phase, bool Restored);
}

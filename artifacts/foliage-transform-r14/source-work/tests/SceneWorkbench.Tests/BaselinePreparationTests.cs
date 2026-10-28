using System.Numerics;
using System.Text.Json;
using SceneWorkbench.Core;
using SceneWorkbench.Unity;

namespace SceneWorkbench.Tests;

public sealed class BaselinePreparationTests
{
    internal static void VersionSet(TestProject fixture)
    {
        var names = new[] { "com.unity.ai.navigation", "com.unity.memoryprofiler", "com.unity.render-pipelines.core",
            "com.unity.render-pipelines.universal", "com.unity.render-pipelines.universal-config", "com.unity.shadergraph", "com.unity.terrain-tools" };
        var locked = new Dictionary<string, object>(); var manifest = new Dictionary<string, string>();
        foreach (var name in names)
        {
            var version = name switch { "com.unity.ai.navigation" => "1.1.5", "com.unity.memoryprofiler" => "1.1.0", "com.unity.terrain-tools" => "5.0.1", _ => "14.0.9" };
            var minimum = name is "com.unity.ai.navigation" or "com.unity.terrain-tools" ? "2022.2" : "2022.3";
            var direct = name is "com.unity.ai.navigation" or "com.unity.memoryprofiler" or "com.unity.render-pipelines.universal" or "com.unity.terrain-tools";
            var deps = name == "com.unity.render-pipelines.universal" ? new Dictionary<string, string> { [names[2]] = "14.0.9", [names[4]] = "14.0.9", [names[5]] = "14.0.9" }
                : name is "com.unity.render-pipelines.universal-config" or "com.unity.shadergraph" ? new Dictionary<string, string> { [names[2]] = "14.0.9" } : [];
            locked[name] = new { version, source = "registry", depth = direct ? 0 : 1, dependencies = deps };
            if (direct) manifest[name] = version;
            fixture.Write($"Library/PackageCache/{name}@{version}/package.json", JsonSerializer.Serialize(new { name, version, unity = minimum, dependencies = deps }));
        }
        fixture.Write("Packages/manifest.json", JsonSerializer.Serialize(new { dependencies = manifest }));
        fixture.Write("Packages/packages-lock.json", JsonSerializer.Serialize(new { dependencies = locked }));
    }
    [Fact]
    public async Task SevenConflictsPrepareACompleteCopyWithoutChangingSourceOrClaimingMigration()
    {
        using var fixture = new TestProject(); VersionSet(fixture); await fixture.CopyAsync();
        var source = ProjectSession.Open(fixture.Destination);
        var sourceVersion = File.ReadAllBytes(source.Resolve("ProjectSettings/ProjectVersion.txt"));
        var originalScene = File.ReadAllBytes(source.Resolve("Assets/Scene.unity"));
        var before = Compatibility.Read(source);
        Assert.Equal(7, before.Diagnostics.Count(d => d.Code == "PACKAGE_EDITOR_MISMATCH"));
        Assert.Throws<InvalidOperationException>(() => Compatibility.RequireKnownYamlWrite(source));
        var destination = Path.Combine(fixture.Root, "prepared"); var output = Path.Combine(fixture.Root, "proof");
        var report = await BaselinePreparation.PrepareAsync(source.Root, destination, output);
        Assert.Equal("VERIFIED_LOCAL_DECLARATIONS", report.OfflineStatus); Assert.Equal("UNVERIFIED", report.NativeStatus);
        Assert.Equal(Enumerable.Range(1, 7).Select(i => $"BL-{i:000}"), report.Issues.Select(i => i.Id));
        Assert.Equal(1, report.Transaction.ChangedFiles); Assert.NotEmpty(report.Dependencies);
        Assert.Equal(report.TargetEditor, BaselinePreparation.Verify(destination, true).TargetEditor);
        var prepared = ProjectSession.Open(destination); Compatibility.RequireKnownYamlWrite(prepared);
        Assert.True(Compatibility.Read(prepared).CanWriteKnownYaml);
        Assert.Equal(sourceVersion, File.ReadAllBytes(source.Resolve("ProjectSettings/ProjectVersion.txt")));
        Assert.Equal(originalScene, File.ReadAllBytes(prepared.Resolve("Assets/Scene.unity")));
        Assert.Equal(7, Compatibility.Read(source).Diagnostics.Count(d => d.Code == "PACKAGE_EDITOR_MISMATCH"));
        var native = new SceneEditSession(prepared, "Assets/Scene.unity"); var item = native.Items.Single(); Assert.True(native.CanEditItem(item));
        native.SetPosition(item.Id, new Vector3(4, 5, 6)); native.Save();
        Assert.Equal(new Vector3(4, 5, 6), new SceneEditSession(prepared, "Assets/Scene.unity").Items.Single().Position);
        native.Undo(); native.Save(); Assert.Equal(originalScene, File.ReadAllBytes(prepared.Resolve("Assets/Scene.unity")));
        var verify = WorkCopy.Open(destination).Verify(false);
        Assert.Single(verify.Differences); Assert.Contains("ProjectSettings/ProjectVersion.txt", verify.Differences[0], StringComparison.Ordinal);
        File.AppendAllText(prepared.Resolve("Packages/manifest.json"), "\n");
        Assert.Throws<SaveConflictException>(() => BaselinePreparation.Verify(destination));
    }
    [Theory]
    [InlineData("dependency")]
    [InlineData("guid")]
    [InlineData("missing-content")]
    public async Task UnsafePreparationDoesNotCreateOrModifyProject(string kind)
    {
        using var fixture = new TestProject(); VersionSet(fixture);
        if (kind == "dependency") fixture.Write("Library/PackageCache/com.unity.shadergraph@14.0.9/package.json", "{\"name\":\"com.unity.shadergraph\",\"version\":\"14.0.9\",\"unity\":\"2022.3\",\"dependencies\":{\"missing.required\":\"1.0.0\"}}");
        if (kind == "guid") fixture.Write("Assets/Second.asset.meta", "guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n");
        if (kind == "missing-content") File.Delete(Path.Combine(fixture.Source, "Library/PackageCache/com.unity.shadergraph@14.0.9/package.json"));
        await fixture.CopyAsync(); var destination = Path.Combine(fixture.Root, "prepared"); var output = Path.Combine(fixture.Root, "proof");
        var before = File.ReadAllBytes(Path.Combine(fixture.Destination, "ProjectSettings/ProjectVersion.txt"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => BaselinePreparation.PrepareAsync(fixture.Destination, destination, output));
        Assert.False(Directory.Exists(destination)); Assert.False(Directory.Exists(output));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(fixture.Destination, "ProjectSettings/ProjectVersion.txt")));
    }
    [Fact]
    public async Task ActualUrpSampleTransformV2RoundtripsAndUnknownVersionsStayReadOnly()
    {
        using var fixture = new TestProject(); fixture.ConfigureUrp("14.0.9", "2022.3"); fixture.Write("ProjectSettings/ProjectVersion.txt", "m_EditorVersion: 2022.3.13f1\n");
        var sample = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures/Unity2022.3/BlitToRTHandle.unity"));
        File.WriteAllBytes(Path.Combine(fixture.Source, "Assets/Scene.unity"), sample);
        await fixture.CopyAsync(); var project = ProjectSession.Open(fixture.Destination);
        var session = new SceneEditSession(project, "Assets/Scene.unity"); var item = session.GetItem(89543560);
        Assert.True(session.CanEditItem(item)); var before = item.Position;
        var original = UnityYamlDocument.Parse(sample);
        session.SetPosition(item.Id, before + Vector3.One); session.Save();
        var after = UnityYamlDocument.Load(project.Resolve("Assets/Scene.unity"));
        foreach (var obj in original.Objects.Where(o => o.FileId != item.Id)) Assert.Equal(original.GetRawBody(obj.FileId), after.GetRawBody(obj.FileId));
        Assert.Equal("2", after.GetScalar(item.Id, "Transform", "serializedVersion"));
        Assert.Equal(original.GetScalar(item.Id, "Transform", "m_ConstrainProportionsScale"), after.GetScalar(item.Id, "Transform", "m_ConstrainProportionsScale"));
        Assert.Equal(before + Vector3.One, new SceneEditSession(project, "Assets/Scene.unity").GetItem(item.Id).Position);
        session.Undo(); session.Save(); Assert.Equal(sample, File.ReadAllBytes(project.Resolve("Assets/Scene.unity")));
        File.WriteAllText(project.Resolve("Assets/Scene.unity"), File.ReadAllText(project.Resolve("Assets/Scene.unity")).Replace("  serializedVersion: 2\n  m_LocalRotation:", "  serializedVersion: 99\n  m_LocalRotation:", StringComparison.Ordinal));
        var unknown = new SceneEditSession(project, "Assets/Scene.unity"); Assert.False(unknown.CanEditItem(unknown.GetItem(item.Id)));
        Assert.Throws<InvalidOperationException>(() => unknown.SetPosition(item.Id, Vector3.Zero));
    }
    [Theory]
    [InlineData(16777217)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public async Task Native2022MaterialKeepsIntegerAndFloatSaveUndo(int value)
    {
        using var fixture = new TestProject(); MaterialEditSessionTests.AddInteger(fixture); fixture.ConfigureUrp("14.0.9", "2022.3");
        fixture.Write("ProjectSettings/ProjectVersion.txt", "m_EditorVersion: 2022.3.13f1\n"); await fixture.CopyAsync();
        var project = ProjectSession.Open(fixture.Destination); var original = File.ReadAllBytes(project.Resolve("Assets/Test.mat"));
        string? Resolve(string guid) => guid == MaterialEditSessionTests.ShaderGuid ? "Assets/Test.shader" : guid == MaterialEditSessionTests.TextureGuid ? "Assets/Texture.png" : null;
        var material = new MaterialEditSession(project, "Assets/Test.mat", Resolve);
        Assert.True(material.SupportsEditing); material.SetInteger("_Count", value); material.SetNumbers("_AO", [.25f]); material.Save();
        var loaded = new MaterialEditSession(project, "Assets/Test.mat", Resolve).Snapshot(true);
        Assert.Equal(value, Assert.IsType<int>(loaded.Properties.Single(p => p.Name == "_Count").Value[0]));
        material.Undo(); material.Undo(); material.Save(); Assert.Equal(original, File.ReadAllBytes(project.Resolve("Assets/Test.mat")));
    }
    [Theory]
    [InlineData("material-version")]
    [InlineData("variant")]
    [InlineData("linked")]
    [InlineData("editor")]
    public async Task Unvalidated2022MaterialVariantsAndSchemasStayReadOnly(string scenario)
    {
        using var fixture = new TestProject(); MaterialEditSessionTests.AddMaterial(fixture); fixture.ConfigureUrp("14.0.9", "2022.3");
        fixture.Write("ProjectSettings/ProjectVersion.txt", "m_EditorVersion: " + (scenario == "editor" ? "2022.3.0b1" : "2022.3.13f1") + "\n");
        var path = Path.Combine(fixture.Source, "Assets/Test.mat"); var text = File.ReadAllText(path);
        if (scenario == "material-version") text = text.Replace("serializedVersion: 8", "serializedVersion: 99", StringComparison.Ordinal);
        if (scenario == "variant") text = text.Replace("  m_SavedProperties:", "  m_Parent: {fileID: 2100000, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 2}\n  m_SavedProperties:", StringComparison.Ordinal);
        if (scenario == "linked") text = text.Replace("  m_SavedProperties:", "  m_CorrespondingSourceObject: {fileID: 2100000}\n  m_SavedProperties:", StringComparison.Ordinal);
        File.WriteAllText(path, text); await fixture.CopyAsync();
        var project = ProjectSession.Open(fixture.Destination); var original = File.ReadAllBytes(project.Resolve("Assets/Test.mat"));
        var material = new MaterialEditSession(project, "Assets/Test.mat", guid => guid == MaterialEditSessionTests.ShaderGuid ? "Assets/Test.shader" : null);
        Assert.False(material.SupportsEditing); Assert.Throws<InvalidOperationException>(() => material.SetNumbers("_AO", [.25f]));
        Assert.Equal(original, File.ReadAllBytes(project.Resolve("Assets/Test.mat")));
    }
}

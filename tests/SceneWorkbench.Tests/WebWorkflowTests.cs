using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using SceneWorkbench.Core;
using SceneWorkbench.Unity;
using SceneWorkbench.Web;

namespace SceneWorkbench.Tests;

public sealed class WebWorkflowTests
{
    private const string Id = "-2745811161023527127";
    [Fact]
    public async Task ExplicitDirectModeWritesSelectedSourceWithBackupAndFreshReopenWithoutCopy()
    {
        using var fixture = new TestProject(); MaterialEditSessionTests.AddInteger(fixture);
        var original = File.ReadAllBytes(Path.Combine(fixture.Source, "Assets/Scene.unity"));
        await using var server = await RunningServer.Start();
        var state = await server.Post("/api/project/open", new { revision = 0, path = fixture.Source, mode = "direct" });
        Assert.True(state.GetProperty("writable").GetBoolean()); Assert.Equal("direct", state.GetProperty("writeMode").GetString());
        Assert.Equal(fixture.Source, state.GetProperty("writePath").GetString()); Assert.False(Directory.Exists(Path.Combine(fixture.Source, WorkCopy.MetadataDirectory)));
        state = await server.Post("/api/scene/open", new { revision = Revision(state), path = "Assets/Scene.unity" });
        state = await server.Post("/api/scene/edit", new { revision = Revision(state), id = Id, field = "position", value = new[] { 3, 4, 5 } });
        state = await server.Post("/api/scene/save", new { revision = Revision(state) }); Assert.False(state.GetProperty("dirty").GetBoolean());
        var transactions = Path.Combine(fixture.Source, WorkCopy.MetadataDirectory, "transactions");
        var journalPath = Path.Combine(Directory.GetDirectories(transactions).Single(), "journal.json"); var journal = JsonFile.Read<SaveJournal>(journalPath);
        Assert.Equal("Assets/Scene.unity", Assert.Single(journal.Entries).RelativePath);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(journalPath)!, journal.Entries[0].BackupName)));
        await using var fresh = await RunningServer.Start();
        var reopened = await fresh.Post("/api/project/open", new { revision = 0, path = fixture.Source, mode = "direct" });
        reopened = await fresh.Post("/api/scene/open", new { revision = Revision(reopened), path = "Assets/Scene.unity" }); Assert.Equal(3, reopened.GetProperty("items")[0].GetProperty("position")[0].GetInt32());
        state = await server.Post("/api/scene/undo", new { revision = Revision(state) }); state = await server.Post("/api/scene/save", new { revision = Revision(state) });
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(fixture.Source, "Assets/Scene.unity")));
        state = await server.Post("/api/material/open", new { revision = Revision(state), path = "Assets/Test.mat" });
        state = await server.Post("/api/material/edit", new { revision = Revision(state), name = "_Count", part = "value", value = new[] { 16777217 } });
        state = await server.Post("/api/scene/save", new { revision = Revision(state) });
        reopened = await fresh.Post("/api/material/open", new { revision = Revision(reopened), path = "Assets/Test.mat" });
        Assert.Equal(16777217, reopened.GetProperty("material").GetProperty("properties").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "_Count").GetProperty("value")[0].GetInt32());
        Assert.False(File.Exists(Path.Combine(fixture.Source, WorkCopy.InfoPath))); Assert.False(Directory.Exists(fixture.Destination));
    }
    [Fact]
    public async Task ExplicitCopyModeCreatesAndOpensTheCopyWhileItsSourceRemainsUnchanged()
    {
        using var fixture = new TestProject(); var original = File.ReadAllBytes(Path.Combine(fixture.Source, "Assets/Scene.unity"));
        await using var server = await RunningServer.Start();
        var state = await server.Post("/api/project/open", new { revision = 0, path = fixture.Source, mode = "copy", destination = fixture.Destination });
        Assert.True(state.GetProperty("writable").GetBoolean()); Assert.Equal("copy", state.GetProperty("writeMode").GetString()); Assert.Equal(fixture.Destination, state.GetProperty("writePath").GetString());
        state = await server.Post("/api/scene/open", new { revision = Revision(state), path = "Assets/Scene.unity" });
        state = await server.Post("/api/scene/edit", new { revision = Revision(state), id = Id, field = "position", value = new[] { 7, 8, 9 } });
        _ = await server.Post("/api/scene/save", new { revision = Revision(state) });
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(fixture.Source, "Assets/Scene.unity")));
        Assert.NotEqual(Hashing.Bytes(original), Hashing.File(Path.Combine(fixture.Destination, "Assets/Scene.unity")));
        Assert.False(Directory.Exists(Path.Combine(fixture.Source, WorkCopy.MetadataDirectory))); _ = WorkCopy.Open(fixture.Destination);
    }
    [Fact]
    public async Task DirectModeDoesNotBypassUnknownProfilesOrExternalSaveConflicts()
    {
        using var fixture = new TestProject(); await using var server = await RunningServer.Start();
        var state = await server.Post("/api/project/open", new { revision = 0, path = fixture.Source, mode = "direct" });
        state = await server.Post("/api/scene/open", new { revision = Revision(state), path = "Assets/Scene.unity" });
        state = await server.Post("/api/scene/edit", new { revision = Revision(state), id = Id, field = "position", value = new[] { 7, 8, 9 } });
        var file = Path.Combine(fixture.Source, "Assets/Scene.unity"); File.AppendAllText(file, "\n# external\n"); var external = File.ReadAllBytes(file);
        _ = await server.Reject("/api/scene/save", new { revision = Revision(state) }, HttpStatusCode.Conflict);
        Assert.Equal(external, File.ReadAllBytes(file)); state = await server.Get("/api/state"); Assert.True(state.GetProperty("dirty").GetBoolean());
        fixture.Write("ProjectSettings/ProjectVersion.txt", "m_EditorVersion: 6000.0.12f1\n");
        state = await server.Post("/api/project/open", new { revision = Revision(state), path = fixture.Source, mode = "direct", discard = true });
        state = await server.Post("/api/scene/open", new { revision = Revision(state), path = "Assets/Scene.unity" }); Assert.False(state.GetProperty("items")[0].GetProperty("editable").GetBoolean());
        _ = await server.Reject("/api/scene/edit", new { revision = Revision(state), id = Id, field = "position", value = new[] { 1, 2, 3 } }, HttpStatusCode.BadRequest);
        Assert.Equal(external, File.ReadAllBytes(file)); Assert.False(Directory.Exists(fixture.Destination));
    }
    [Fact]
    public async Task Prepared2022SceneAndMaterialSaveReloadUndoAndIndependentHostRemainEditable()
    {
        using var fixture = new TestProject(); BaselinePreparationTests.VersionSet(fixture); MaterialEditSessionTests.AddInteger(fixture); await fixture.CopyAsync();
        var prepared = Path.Combine(fixture.Root, "prepared");
        await BaselinePreparation.PrepareAsync(fixture.Destination, prepared, Path.Combine(fixture.Root, "proof"));
        var sceneOriginal = File.ReadAllBytes(Path.Combine(prepared, "Assets/Scene.unity"));
        var materialOriginal = File.ReadAllBytes(Path.Combine(prepared, "Assets/Test.mat"));
        await using var server = await RunningServer.Start();
        var state = await server.Post("/api/project/open", new { revision = 0, path = prepared });
        Assert.Equal("2022.3.13f1", state.GetProperty("editorVersion").GetString());
        Assert.DoesNotContain(state.GetProperty("diagnostics").EnumerateArray(), d => d.GetProperty("severity").GetString() == "Blocking");
        state = await server.Post("/api/scene/open", new { revision = Revision(state), path = "Assets/Scene.unity" });
        Assert.True(state.GetProperty("items")[0].GetProperty("editable").GetBoolean());
        state = await server.Post("/api/scene/edit", new { revision = Revision(state), id = Id, field = "position", value = new[] { 8, 9, 10 } });
        state = await server.Post("/api/scene/save", new { revision = Revision(state) });
        await using var independent = await RunningServer.Start();
        var fresh = await independent.Post("/api/project/open", new { revision = 0, path = prepared });
        fresh = await independent.Post("/api/scene/open", new { revision = Revision(fresh), path = "Assets/Scene.unity" });
        Assert.Equal(8, fresh.GetProperty("items")[0].GetProperty("position")[0].GetInt32());
        state = await server.Post("/api/scene/undo", new { revision = Revision(state) }); state = await server.Post("/api/scene/save", new { revision = Revision(state) });
        Assert.Equal(sceneOriginal, File.ReadAllBytes(Path.Combine(prepared, "Assets/Scene.unity")));
        state = await server.Post("/api/material/open", new { revision = Revision(state), path = "Assets/Test.mat" });
        Assert.True(state.GetProperty("material").GetProperty("editable").GetBoolean());
        state = await server.Post("/api/material/edits", new { revision = Revision(state), edits = new object[] { new { name = "_Count", part = "value", value = new[] { int.MaxValue } }, new { name = "_AO", part = "value", value = new[] { .25 } } } });
        state = await server.Post("/api/scene/save", new { revision = Revision(state) });
        fresh = await independent.Post("/api/material/open", new { revision = Revision(fresh), path = "Assets/Test.mat" });
        Assert.Equal(int.MaxValue, fresh.GetProperty("material").GetProperty("properties").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "_Count").GetProperty("value")[0].GetInt32());
        state = await server.Post("/api/scene/undo", new { revision = Revision(state) }); state = await server.Post("/api/scene/undo", new { revision = Revision(state) });
        _ = await server.Post("/api/scene/save", new { revision = Revision(state) });
        Assert.Equal(materialOriginal, File.ReadAllBytes(Path.Combine(prepared, "Assets/Test.mat")));
    }
    [Fact]
    public async Task IntegerHttpPreservesAllInt32BoundariesAndRejectsFractionOverflowAndLegacyRounding()
    {
        using var fixture = new TestProject(); MaterialEditSessionTests.AddInteger(fixture); await fixture.CopyAsync();
        await using var server = await RunningServer.Start();
        var state = await server.Post("/api/project/open", new { revision = 0, path = fixture.Destination });
        state = await server.Post("/api/material/open", new { revision = Revision(state), path = "Assets/Test.mat" });
        foreach (var value in new[] { 16777217, -16777217, int.MinValue, int.MaxValue })
        {
            state = await server.Post("/api/material/edit", new { revision = Revision(state), name = "_Count", part = "value", value = new[] { value } });
            Assert.Equal(value, state.GetProperty("material").GetProperty("properties").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "_Count").GetProperty("value")[0].GetInt32());
            state = await server.Post("/api/scene/save", new { revision = Revision(state) });
            state = await server.Post("/api/material/open", new { revision = Revision(state), path = "Assets/Test.mat" });
            Assert.Equal(value, state.GetProperty("material").GetProperty("properties").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "_Count").GetProperty("value")[0].GetInt32());
            await using var independent = await RunningServer.Start();
            var fresh = await independent.Post("/api/project/open", new { revision = 0, path = fixture.Destination });
            fresh = await independent.Post("/api/material/open", new { revision = Revision(fresh), path = "Assets/Test.mat" });
            Assert.Equal(value, fresh.GetProperty("material").GetProperty("properties").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "_Count").GetProperty("value")[0].GetInt32());
        }
        var before = Hashing.File(Path.Combine(fixture.Destination, "Assets/Test.mat"));
        foreach (var value in new[] { 1.5, 2147483648d, -2147483649d })
        {
            _ = await server.Reject("/api/material/edit", new { revision = Revision(state), name = "_Count", part = "value", value = new[] { value } }, HttpStatusCode.BadRequest);
            state = await server.Get("/api/state"); Assert.False(state.GetProperty("dirty").GetBoolean()); Assert.Equal(0, state.GetProperty("undoCount").GetInt32()); Assert.Equal(before, Hashing.File(Path.Combine(fixture.Destination, "Assets/Test.mat")));
        }
        _ = await server.Reject("/api/material/edit", new { revision = Revision(state), name = "_Legacy", part = "value", value = new[] { 16777217 } }, HttpStatusCode.BadRequest);
        state = await server.Get("/api/state"); Assert.False(state.GetProperty("dirty").GetBoolean()); Assert.Equal(before, Hashing.File(Path.Combine(fixture.Destination, "Assets/Test.mat")));
        foreach (var invalid in new[] { "NaN", "Infinity", "-Infinity" })
        {
            _ = await server.Reject("/api/material/edit", new { revision = Revision(state), name = "_Count", part = "value", value = new[] { invalid } }, HttpStatusCode.BadRequest);
            state = await server.Get("/api/state"); Assert.False(state.GetProperty("dirty").GetBoolean()); Assert.Equal(before, Hashing.File(Path.Combine(fixture.Destination, "Assets/Test.mat")));
        }
    }
    [Fact]
    public async Task ReloadSceneRevalidatesExternalGuidMetadataBeforePreview()
    {
        using var fixture = new TestProject(); MaterialEditSessionTests.AddMaterial(fixture);
        fixture.Write("Assets/Mesh.asset", PreviewTests.MeshText()); fixture.Write("Assets/Mesh.asset.meta", "guid: 11111111111111111111111111111111\n");
        fixture.Write("Assets/Scene.unity", TestProject.SceneText() + """
            --- !u!33 &33
            MeshFilter:
              m_GameObject: {fileID: 7}
              m_Mesh: {fileID: 4300000, guid: 11111111111111111111111111111111, type: 2}
            --- !u!23 &23
            MeshRenderer:
              m_GameObject: {fileID: 7}
              m_Enabled: 1
              m_Materials:
              - {fileID: 2100000, guid: ffffffffffffffffffffffffffffffff, type: 2}
            """);
        await using var server = await RunningServer.Start();
        var state = await server.Post("/api/project/open", new { revision = 0, path = fixture.Source });
        state = await server.Post("/api/scene/open", new { revision = Revision(state), path = "Assets/Scene.unity" });
        var graph = await server.Get("/api/preview?path=Assets%2FScene.unity"); Assert.Equal("Assets/Test.shader", graph.GetProperty("materials")[0].GetProperty("shaderPath").GetString());
        fixture.Write("Assets/Test.shader.meta", "guid: 22222222222222222222222222222222\nShaderImporter: {}\n");
        _ = await server.Post("/api/scene/open", new { revision = Revision(state), path = "Assets/Scene.unity" });
        graph = await server.Get("/api/preview?path=Assets%2FScene.unity"); Assert.Equal("", graph.GetProperty("materials")[0].GetProperty("shaderPath").GetString());
    }
    [Fact]
    public async Task NativeMeshResourceInfoAndDecodeKeepSignedFileIdsAndRejectUnknownFormatsAsJson()
    {
        using var fixture = new TestProject();
        fixture.Write("Assets/Mesh.asset", PreviewTests.MeshText().Replace("&4300000", "&-9223372036854775808", StringComparison.Ordinal));
        await using var server = await RunningServer.Start();
        _ = await server.Post("/api/project/open", new { revision = 0, path = fixture.Source });
        var asset = await server.Get("/api/asset?path=Assets%2FMesh.asset");
        Assert.Equal("-9223372036854775808", asset.GetProperty("meshIds")[0].GetString());
        var mesh = await server.Get("/api/model?path=Assets%2FMesh.asset&fileId=-9223372036854775808"); Assert.Equal(3, mesh.GetProperty("vertexCount").GetInt32());
        fixture.Write("Assets/Unsupported.asset", "not Unity YAML");
        using var response = await server.Client.GetAsync("/api/model?path=Assets%2FUnsupported.asset&fileId=1");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_OPERATION", JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync()).GetProperty("code").GetString());
    }
    [Fact]
    public async Task MaterialHttpNativeSaveReopenConflictAndBatchFailureKeepDraft()
    {
        using var fixture = new TestProject(); MaterialEditSessionTests.AddMaterial(fixture); await fixture.CopyAsync();
        var original = File.ReadAllBytes(Path.Combine(fixture.Destination, "Assets/Test.mat"));
        await using var server = await RunningServer.Start();
        var state = await server.Post("/api/project/open", new { revision = 0, path = fixture.Destination });
        state = await server.Post("/api/material/open", new { revision = Revision(state), path = "Assets/Test.mat" });
        state = await server.Post("/api/material/edits", new { revision = Revision(state), edits = new object[] { new { name = "_AO", part = "value", value = new[] { .25 } }, new { name = "_Texture", part = "texture", value = "Assets/Texture.png" } } });
        Assert.True(state.GetProperty("dirty").GetBoolean());
        state = await server.Post("/api/scene/save", new { revision = Revision(state) }); Assert.False(state.GetProperty("dirty").GetBoolean());
        await using var fresh = await RunningServer.Start();
        var reopened = await fresh.Post("/api/project/open", new { revision = 0, path = fixture.Destination });
        reopened = await fresh.Post("/api/material/open", new { revision = Revision(reopened), path = "Assets/Test.mat" });
        var properties = reopened.GetProperty("material").GetProperty("properties").EnumerateArray().ToArray();
        Assert.Equal(.25, properties.Single(p => p.GetProperty("name").GetString() == "_AO").GetProperty("value")[0].GetDouble());
        var before = Hashing.File(Path.Combine(fixture.Destination, "Assets/Test.mat"));
        await server.Reject("/api/material/edits", new { revision = Revision(state), edits = new object[] { new { name = "_AO", part = "value", value = new[] { .7 } }, new { name = "_Texture", part = "texture", value = "Assets/missing.png" } } }, HttpStatusCode.BadRequest);
        state = await server.Get("/api/state"); Assert.False(state.GetProperty("dirty").GetBoolean()); Assert.Equal(before, Hashing.File(Path.Combine(fixture.Destination, "Assets/Test.mat")));
        state = await server.Post("/api/scene/undo", new { revision = Revision(state) }); state = await server.Post("/api/scene/undo", new { revision = Revision(state) });
        state = await server.Post("/api/scene/save", new { revision = Revision(state) }); Assert.Equal(original, File.ReadAllBytes(Path.Combine(fixture.Destination, "Assets/Test.mat")));
        state = await server.Post("/api/material/edit", new { revision = Revision(state), name = "_AO", part = "value", value = new[] { .3 } });
        File.AppendAllText(Path.Combine(fixture.Destination, "Assets/Test.mat.meta"), "\n# external\n");
        await server.Reject("/api/scene/save", new { revision = Revision(state) }, HttpStatusCode.Conflict);
        Assert.True((await server.Get("/api/state")).GetProperty("dirty").GetBoolean());
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(fixture.Source, "Assets/Test.mat")));
    }
    [Fact]
    public async Task OpeningDefersPackageCatalogWhileBrowseAndSearchRetainCachedResources()
    {
        using var fixture = new TestProject();
        const string folder = "Library/PackageCache/com.unity.render-pipelines.universal@12.1.15/Manual";
        const string resource = folder + "/package-only.txt";
        fixture.Write(resource, "package resource");
        await using var server = await RunningServer.Start();
        var state = await server.Post("/api/project/open", new { revision = 0, path = fixture.Source });
        Assert.False(state.GetProperty("packageCacheIndexed").GetBoolean());
        var indexedSources = state.GetProperty("sourceFiles").GetInt32();
        var rows = await server.Get("/api/assets?directory=" + Uri.EscapeDataString(folder));
        Assert.Equal(resource, rows.GetProperty("rows")[0].GetProperty("path").GetString());
        Assert.False((await server.Get("/api/state")).GetProperty("packageCacheIndexed").GetBoolean());
        rows = await server.Get("/api/assets?query=package-only");
        Assert.Equal(resource, rows.GetProperty("rows")[0].GetProperty("path").GetString());
        state = await server.Get("/api/state");
        Assert.True(state.GetProperty("packageCacheIndexed").GetBoolean());
        Assert.True(state.GetProperty("sourceFiles").GetInt32() > indexedSources);
        state = await server.Post("/api/project/open", new { revision = Revision(state), path = fixture.Source });
        Assert.False(state.GetProperty("packageCacheIndexed").GetBoolean());
    }

    [Fact]
    public async Task DeferredPackageSearchRejectsLinksWithoutReadingTheirTargets()
    {
        using var fixture = new TestProject();
        var outside = Path.Combine(fixture.Root, "outside");
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "sentinel.txt");
        File.WriteAllText(sentinel, "keep");
        TestProject.MakeDirectoryLink(SafePath.Resolve(fixture.Source, "Library/PackageCache/com.unity.render-pipelines.universal@12.1.15/Redirect"), outside);
        await using var server = await RunningServer.Start();
        var state = await server.Post("/api/project/open", new { revision = 0, path = fixture.Source });
        Assert.False(state.GetProperty("packageCacheIndexed").GetBoolean());
        using var response = await server.Client.GetAsync("/api/assets?query=sentinel");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        Assert.Contains("連結", error.GetProperty("error").GetString());
        Assert.False((await server.Get("/api/state")).GetProperty("packageCacheIndexed").GetBoolean());
        Assert.Equal("keep", File.ReadAllText(sentinel));
    }

    [Fact]
    public async Task NativeSaveReopenAndUndoPreserveSignedIdsAndUnknownData()
    {
        using var fixture = new TestProject(); fixture.ConfigureUrp("14.0.9", "2022.3"); await fixture.CopyAsync();
        var original = File.ReadAllBytes(Path.Combine(fixture.Source, "Assets/Scene.unity"));
        var meta = Hashing.File(Path.Combine(fixture.Destination, "Assets/Scene.unity.meta"));
        await using var server = await RunningServer.Start();
        var state = await server.Post("/api/project/open", new { revision = 0, path = fixture.Destination });
        state = await server.Post("/api/scene/open", new { revision = Revision(state), path = "Assets/Scene.unity" });
        Assert.Equal(Id, state.GetProperty("items")[0].GetProperty("id").GetString());
        state = await server.Post("/api/scene/edit", new { revision = Revision(state), id = Id, field = "position", value = new[] { 3.125, -4.5, 9.25 } });
        Assert.True(state.GetProperty("dirty").GetBoolean());
        state = await server.Post("/api/scene/save", new { revision = Revision(state) });
        Assert.False(state.GetProperty("dirty").GetBoolean());
        Assert.Contains(state.GetProperty("diagnostics").EnumerateArray(), d => d.GetProperty("code").GetString() == "PACKAGE_EDITOR_MISMATCH");
        state = await server.Post("/api/scene/open", new { revision = Revision(state), path = "Assets/Scene.unity" });
        Assert.Equal(3.125, state.GetProperty("items")[0].GetProperty("position")[0].GetDouble());
        var current = UnityYamlDocument.Load(Path.Combine(fixture.Destination, "Assets/Scene.unity"));
        Assert.Equal(UnityYamlDocument.Parse(original).GetRawBody(99), current.GetRawBody(99));
        Assert.Equal(meta, Hashing.File(Path.Combine(fixture.Destination, "Assets/Scene.unity.meta")));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(fixture.Source, "Assets/Scene.unity")));
        // A separate HTTP host process must read the native save, not the old in-memory document.
        await using var fresh = await RunningServer.Start();
        var reopened = await fresh.Post("/api/project/open", new { revision = 0, path = fixture.Destination });
        reopened = await fresh.Post("/api/scene/open", new { revision = Revision(reopened), path = "Assets/Scene.unity" });
        Assert.Equal(3.125, reopened.GetProperty("items")[0].GetProperty("position")[0].GetDouble());
        state = await server.Post("/api/scene/edit", new { revision = Revision(state), id = Id, field = "name", value = "網頁場景 <&> 🌲" });
        state = await server.Post("/api/scene/undo", new { revision = Revision(state) });
        Assert.False(state.GetProperty("dirty").GetBoolean());
        var time = File.GetLastWriteTimeUtc(Path.Combine(fixture.Destination, "Assets/Scene.unity"));
        _ = await server.Post("/api/scene/save", new { revision = Revision(state) });
        Assert.Equal(time, File.GetLastWriteTimeUtc(Path.Combine(fixture.Destination, "Assets/Scene.unity")));
    }
    [Fact]
    public async Task SourceIsReadOnlyAndStaleTabsCannotOverwrite()
    {
        using var fixture = new TestProject(); await fixture.CopyAsync();
        await using var server = await RunningServer.Start();
        var state = await server.Post("/api/project/open", new { revision = 0, path = fixture.Source });
        state = await server.Post("/api/scene/open", new { revision = Revision(state), path = "Assets/Scene.unity" });
        Assert.False(state.GetProperty("writable").GetBoolean());
        Assert.False(state.GetProperty("items")[0].GetProperty("editable").GetBoolean());
        await server.Reject("/api/scene/edit", new { revision = Revision(state), id = Id, field = "name", value = "禁止" }, HttpStatusCode.BadRequest);
        state = await server.Get("/api/state");
        state = await server.Post("/api/project/open", new { revision = Revision(state), path = fixture.Destination });
        state = await server.Post("/api/scene/open", new { revision = Revision(state), path = "Assets/Scene.unity" });
        var oldRevision = Revision(state);
        state = await server.Post("/api/scene/edit", new { revision = oldRevision, id = Id, field = "position", value = new[] { 6, 7, 8 } });
        var stale = await server.Reject("/api/scene/edit", new { revision = oldRevision, id = Id, field = "position", value = new[] { 9, 9, 9 } }, HttpStatusCode.Conflict);
        Assert.Equal("STALE_SESSION", stale.GetProperty("code").GetString());
        Assert.Equal(6, (await server.Get("/api/state")).GetProperty("items")[0].GetProperty("position")[0].GetDouble());
    }
    [Fact]
    public async Task ExternalSceneChangesBlockSaveAndRetainDraft()
    {
        using var fixture = new TestProject(); await fixture.CopyAsync();
        await using var server = await RunningServer.Start();
        var state = await server.Post("/api/project/open", new { revision = 0, path = fixture.Destination });
        state = await server.Post("/api/scene/open", new { revision = Revision(state), path = "Assets/Scene.unity" });
        state = await server.Post("/api/scene/edit", new { revision = Revision(state), id = Id, field = "position", value = new[] { 8, 9, 10 } });
        var path = Path.Combine(fixture.Destination, "Assets/Scene.unity"); File.AppendAllText(path, "\n# external edit\n");
        var external = Hashing.File(path);
        var error = await server.Reject("/api/scene/save", new { revision = Revision(state) }, HttpStatusCode.Conflict);
        Assert.Equal("FILE_CONFLICT", error.GetProperty("code").GetString());
        Assert.Equal(external, Hashing.File(path));
        state = await server.Get("/api/state"); Assert.True(state.GetProperty("dirty").GetBoolean());
        Assert.Equal(8, state.GetProperty("items")[0].GetProperty("position")[0].GetDouble());
        await server.Reject("/api/scene/open", new { revision = Revision(state), path = "Assets/Scene.unity" }, HttpStatusCode.BadRequest);
        state = await server.Get("/api/state");
        state = await server.Post("/api/scene/open", new { revision = Revision(state), path = "Assets/Scene.unity", discard = true });
        Assert.False(state.GetProperty("dirty").GetBoolean());
    }
    [Fact]
    public async Task BadInputsAndUnknownSchemaStayUnchanged()
    {
        using var fixture = new TestProject(); await fixture.CopyAsync();
        await using var server = await RunningServer.Start();
        var state = await server.Post("/api/project/open", new { revision = 0, path = fixture.Destination });
        state = await server.Post("/api/scene/open", new { revision = Revision(state), path = "Assets/Scene.unity" });
        foreach (var value in new object[] { new { field = "position", value = (object)new[] { 1, 2 } }, new { field = "rotation", value = (object)new[] { 0, 0, 0, 0 } }, new { field = "layer", value = (object)32 }, new { field = "name", value = (object)"   " } })
        {
            var data = JsonSerializer.SerializeToElement(value);
            await server.Reject("/api/scene/edit", new { revision = Revision(state), id = Id, field = data.GetProperty("field").GetString(), value = data.GetProperty("value") }, HttpStatusCode.BadRequest);
            state = await server.Get("/api/state"); Assert.False(state.GetProperty("dirty").GetBoolean());
        }
        await server.Reject("/api/scene/edit", new { revision = Revision(state), id = "9223372036854775808", field = "name", value = "錯誤" }, HttpStatusCode.BadRequest);
    }
    [Fact]
    public async Task HostOriginTokenAndAssetTraversalAreRejected()
    {
        using var fixture = new TestProject();
        await using var server = await RunningServer.Start();
        using var unauthenticated = new HttpClient { BaseAddress = server.Client.BaseAddress };
        Assert.Equal(HttpStatusCode.Forbidden, (await unauthenticated.GetAsync("/api/state")).StatusCode);
        using var crossOrigin = new HttpRequestMessage(HttpMethod.Get, "/api/state"); crossOrigin.Headers.Add("Origin", "https://example.invalid");
        Assert.Equal(HttpStatusCode.Forbidden, (await server.Client.SendAsync(crossOrigin)).StatusCode);
        using var rebound = new HttpRequestMessage(HttpMethod.Get, "/api/state"); rebound.Headers.Host = "example.invalid:" + server.Client.BaseAddress!.Port.ToString(CultureInfo.InvariantCulture);
        Assert.Equal(HttpStatusCode.Forbidden, (await server.Client.SendAsync(rebound)).StatusCode);
        _ = await server.Post("/api/project/open", new { revision = 0, path = fixture.Source });
        var blocked = await server.Client.GetAsync("/api/asset?path=Assets%2F..%2FProjectSettings%2FProjectVersion.txt");
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await server.Client.GetAsync("/Assets/Scene.unity")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await server.Client.GetAsync("/ui/app.js")).StatusCode);
    }

    [Theory]
    [InlineData("layer", "2147483648")]
    [InlineData("layer", "-2147483649")]
    [InlineData("staticFlags", "-1")]
    [InlineData("staticFlags", "4294967296")]
    public async Task NumericRangeErrorsReturnJsonWithoutMutationOrLostConnection(string field, string json)
    {
        using var fixture = new TestProject();
        fixture.Write("Assets/Scene.unity", TestProject.SceneText().Replace("  m_Layer: 31", "  m_StaticEditorFlags: 0\n  m_Layer: 31", StringComparison.Ordinal));
        await fixture.CopyAsync();
        var path = Path.Combine(fixture.Destination, "Assets/Scene.unity"); var original = Hashing.File(path);
        await using var server = await RunningServer.Start();
        var state = await server.Post("/api/project/open", new { revision = 0, path = fixture.Destination });
        state = await server.Post("/api/scene/open", new { revision = Revision(state), path = "Assets/Scene.unity" });
        var error = await server.Reject("/api/scene/edit", new { revision = Revision(state), id = Id, field, value = JsonSerializer.Deserialize<JsonElement>(json) }, HttpStatusCode.BadRequest);
        Assert.Equal("INVALID_VALUE", error.GetProperty("code").GetString());
        Assert.Contains("數值", error.GetProperty("error").GetString(), StringComparison.Ordinal);
        state = await server.Get("/api/state");
        Assert.False(state.GetProperty("dirty").GetBoolean());
        Assert.Equal(0, state.GetProperty("undoCount").GetInt32());
        Assert.Equal(31, state.GetProperty("items")[0].GetProperty("layer").GetInt32());
        Assert.Equal(0u, state.GetProperty("items")[0].GetProperty("staticFlags").GetUInt32());
        Assert.Equal(original, Hashing.File(path));
        Assert.Equal(original, Hashing.File(Path.Combine(fixture.Source, "Assets/Scene.unity")));
    }

    private static long Revision(JsonElement state) => state.GetProperty("revision").GetInt64();
    private sealed class RunningServer : IAsyncDisposable
    {
        private readonly Process process;
        private readonly Task<string> output;
        private readonly Task<string> error;
        public HttpClient Client { get; }
        private RunningServer(Process process, HttpClient client) { this.process = process; Client = client; output = process.StandardOutput.ReadToEndAsync(); error = process.StandardError.ReadToEndAsync(); }
        public static async Task<RunningServer> Start()
        {
            var workspace = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(workspace, "SceneWorkbench.slnx"))) workspace = Path.GetDirectoryName(workspace)!;
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var assembly = typeof(WorkbenchService).Assembly.Location;
            var dotnet = Path.Combine(workspace, ".tools", "dotnet", "dotnet.exe");
            var start = new ProcessStartInfo(dotnet) { WorkingDirectory = workspace, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(assembly); start.ArgumentList.Add("--port"); start.ArgumentList.Add(port.ToString(CultureInfo.InvariantCulture));
            start.Environment["SCENE_WORKBENCH_ROOT"] = workspace; start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            var process = Process.Start(start)!;
            var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(90) };
            var server = new RunningServer(process, client);
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (process.HasExited) { await server.DisposeAsync(); throw new InvalidOperationException("Web test host exited during startup."); }
                try
                {
                    var html = await client.GetStringAsync("/"); var token = Regex.Match(html, "name=\"workbench-token\" content=\"([a-f0-9]+)\"").Groups[1].Value;
                    if (token.Length != 64) throw new InvalidOperationException("Missing per-launch API token.");
                    client.DefaultRequestHeaders.Add("X-Workbench-Token", token); return server;
                }
                catch (HttpRequestException) { await Task.Delay(100); }
            }
            await server.DisposeAsync(); throw new TimeoutException("Local web host did not start.");
        }
        public async Task<JsonElement> Get(string path) => await Read(await Client.GetAsync(path), HttpStatusCode.OK);
        public async Task<JsonElement> Post(string path, object body) => await Read(await Client.PostAsJsonAsync(path, body), HttpStatusCode.OK);
        public async Task<JsonElement> Reject(string path, object body, HttpStatusCode expected) => await Read(await Client.PostAsJsonAsync(path, body), expected);
        private static async Task<JsonElement> Read(HttpResponseMessage response, HttpStatusCode expected)
        {
            using (response)
            {
                var text = await response.Content.ReadAsStringAsync(); Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {text}");
                return JsonSerializer.Deserialize<JsonElement>(text);
            }
        }
        public async ValueTask DisposeAsync()
        {
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
            var logs = Path.Combine(Client.BaseAddress!.Port.ToString(CultureInfo.InvariantCulture) + "-web-host.log");
            var workspace = process.StartInfo.WorkingDirectory; Directory.CreateDirectory(Path.Combine(workspace, "artifacts", "logs", "web-tests"));
            await File.WriteAllTextAsync(Path.Combine(workspace, "artifacts", "logs", "web-tests", logs), await output + await error);
            Client.Dispose(); process.Dispose();
        }
    }
}

using System.Numerics;
using SceneWorkbench.Core;
using SceneWorkbench.Unity;

namespace SceneWorkbench.Tests;

public sealed class SceneEditSessionTests
{
    private const long TransformId = -2745811161023527127;

    [Fact]
    public async Task OfflineFieldSaveKeepsRuntimeConflictAndPreservesNativeNeighbours()
    {
        using var fixture = new TestProject();
        fixture.ConfigureUrp("14.0.9", "2022.3");
        fixture.Write("Assets/Scene.unity", TestProject.SceneText().Replace("  m_Layer: 31", "  m_IsActive: 1\n  m_StaticEditorFlags: -1\n  m_Layer: 31", StringComparison.Ordinal));
        var source = File.ReadAllBytes(Path.Combine(fixture.Source, "Assets/Scene.unity"));
        await fixture.CopyAsync();
        var project = ProjectSession.Open(fixture.Destination);
        var original = UnityYamlDocument.Parse(source);
        var metaBefore = Hashing.File(project.Resolve("Assets/Scene.unity.meta"));
        var scene = new SceneEditSession(project, "Assets/Scene.unity");
        Assert.True(scene.CanEditItem(scene.GetItem(TransformId)));
        scene.SetName(7, "離線場景：<&> 🌲 \"#\"\n下一行");
        scene.SetPosition(TransformId, new Vector3(3.125f, -4.5f, 9.25f));
        scene.SetScale(TransformId, new Vector3(.5f, 2, 3));
        scene.SetRotation(TransformId, Quaternion.CreateFromYawPitchRoll(.3f, .2f, .1f));
        scene.SetActive(7, false); scene.SetLayer(7, 9); scene.SetStaticFlags(7, 65);
        var saved = scene.Save();
        Assert.Equal(1, saved.ChangedFiles);
        Assert.False(scene.IsDirty);
        var loaded = new SceneEditSession(project, "Assets/Scene.unity").GetItem(TransformId);
        Assert.Equal("離線場景：<&> 🌲 \"#\"\n下一行", loaded.Name);
        Assert.Equal(new Vector3(3.125f, -4.5f, 9.25f), loaded.Position);
        Assert.Equal(new Vector3(.5f, 2, 3), loaded.Scale);
        Assert.False(loaded.Active); Assert.Equal(9, loaded.Layer); Assert.Equal(65u, loaded.StaticFlags);
        Assert.Equal(metaBefore, Hashing.File(project.Resolve("Assets/Scene.unity.meta")));
        var current = UnityYamlDocument.Load(project.Resolve("Assets/Scene.unity"));
        Assert.Equal(original.GetRawBody(99), current.GetRawBody(99));
        Assert.Contains("m_UnknownSetting: [4, 8, 15, 16, 23, 42] # preserve this exact text", current.GetRawBody(TransformId), StringComparison.Ordinal);
        Assert.Equal(source, File.ReadAllBytes(Path.Combine(fixture.Source, "Assets/Scene.unity")));
        Assert.Contains(scene.LastSaveDiagnostics, d => d.Code == "PACKAGE_EDITOR_MISMATCH" && d.Severity == Severity.Blocking);
        Assert.False(Compatibility.Read(project).CanWriteKnownYaml);
        Assert.Throws<InvalidOperationException>(() => Compatibility.RequireKnownYamlWrite(project));
    }

    [Fact]
    public async Task UndoRedoAcrossSaveReturnsExactOriginalBytesAndNoOpKeepsTimestamp()
    {
        using var fixture = new TestProject(); await fixture.CopyAsync();
        var project = ProjectSession.Open(fixture.Destination);
        var path = project.Resolve("Assets/Scene.unity"); var original = File.ReadAllBytes(path);
        var scene = new SceneEditSession(project, "Assets/Scene.unity");
        var initialTime = File.GetLastWriteTimeUtc(path);
        Assert.Equal(0, scene.Save().ChangedFiles); Assert.Equal(initialTime, File.GetLastWriteTimeUtc(path));
        scene.SetPosition(TransformId, new Vector3(7, 8, 9)); scene.Save();
        Assert.False(scene.IsDirty);
        scene.Undo(); Assert.True(scene.IsDirty); scene.Save();
        Assert.Equal(original, File.ReadAllBytes(path));
        scene.Redo(); Assert.True(scene.IsDirty); scene.Save();
        Assert.Equal(new Vector3(7, 8, 9), new SceneEditSession(project, "Assets/Scene.unity").GetItem(TransformId).Position);
    }

    [Fact]
    public void SourceProjectCannotBeSavedEvenWithValidFormat()
    {
        using var fixture = new TestProject();
        var path = Path.Combine(fixture.Source, "Assets/Scene.unity"); var original = Hashing.File(path);
        var scene = new SceneEditSession(ProjectSession.Open(fixture.Source), "Assets/Scene.unity");
        scene.SetPosition(TransformId, Vector3.One);
        Assert.Throws<InvalidOperationException>(() => scene.Save());
        Assert.Equal(original, Hashing.File(path));
    }

    [Theory]
    [InlineData("duplicate-guid")]
    [InlineData("orphan-meta")]
    [InlineData("manifest-lock")]
    [InlineData("unknown-version")]
    public async Task IntegrityAndUnknownSchemaBlockersStillRejectWrites(string scenario)
    {
        using var fixture = new TestProject();
        switch (scenario)
        {
            case "duplicate-guid": fixture.Write("Assets/Second.asset.meta", "guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n"); break;
            case "orphan-meta": fixture.Write("Assets/Missing.mat.meta", "guid: dddddddddddddddddddddddddddddddd\n"); break;
            case "manifest-lock": fixture.Write("Packages/manifest.json", "{\"dependencies\":{\"com.unity.render-pipelines.universal\":\"14.0.9\"}}"); break;
            case "unknown-version": fixture.Write("ProjectSettings/ProjectVersion.txt", "m_EditorVersion: 2023.3.0f1\n"); break;
        }
        await fixture.CopyAsync(); var project = ProjectSession.Open(fixture.Destination);
        var path = project.Resolve("Assets/Scene.unity"); var original = Hashing.File(path);
        var scene = new SceneEditSession(project, "Assets/Scene.unity");
        if (scenario == "unknown-version") Assert.Throws<InvalidOperationException>(() => scene.SetPosition(TransformId, Vector3.One));
        else { scene.SetPosition(TransformId, Vector3.One); Assert.Throws<InvalidOperationException>(() => scene.Save()); }
        Assert.Equal(original, Hashing.File(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExternalSceneOrMetaChangeIsNeverOverwritten(bool meta)
    {
        using var fixture = new TestProject(); await fixture.CopyAsync(); var project = ProjectSession.Open(fixture.Destination);
        var scene = new SceneEditSession(project, "Assets/Scene.unity"); scene.SetPosition(TransformId, new Vector3(7, 8, 9));
        var path = project.Resolve(meta ? "Assets/Scene.unity.meta" : "Assets/Scene.unity");
        File.AppendAllText(path, "\n# external owner edit\n"); var external = File.ReadAllBytes(path);
        Assert.Throws<SaveConflictException>(() => scene.Save());
        Assert.True(scene.IsDirty); Assert.Equal(external, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task InvalidEditsDoNotMutateDocumentAndUnchangedNegativeStaticFlagsStayRaw()
    {
        using var fixture = new TestProject();
        fixture.Write("Assets/Scene.unity", TestProject.SceneText().Replace("  m_Layer: 31", "  m_StaticEditorFlags: -1\n  m_Layer: 31", StringComparison.Ordinal));
        await fixture.CopyAsync(); var project = ProjectSession.Open(fixture.Destination);
        var scene = new SceneEditSession(project, "Assets/Scene.unity"); var original = scene.GetBytes();
        Assert.Throws<ArgumentOutOfRangeException>(() => scene.SetPosition(TransformId, new Vector3(float.NaN, 0, 0)));
        Assert.Throws<ArgumentException>(() => scene.SetScale(TransformId, new Vector3(float.PositiveInfinity, 1, 1)));
        Assert.Throws<ArgumentException>(() => scene.SetRotation(TransformId, new Quaternion(0, 0, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => scene.SetLayer(7, 32));
        scene.SetStaticFlags(7, uint.MaxValue);
        Assert.Equal(original, scene.GetBytes()); Assert.False(scene.IsDirty); Assert.Equal(0, scene.UndoCount);
    }

    [Theory]
    [InlineData("connected-transform")]
    [InlineData("connected-gameobject")]
    [InlineData("wrong-component")]
    [InlineData("rect-transform")]
    [InlineData("unknown-object-schema")]
    [InlineData("unknown-transform-schema")]
    public async Task PrefabConnectionsAndUnrecognisedOwnershipRemainReadOnly(string scenario)
    {
        using var fixture = new TestProject(); var text = TestProject.SceneText();
        text = scenario switch
        {
            "connected-transform" => text.Replace("Transform:\n", "Transform:\n  m_PrefabInstance: {fileID: 1001}\n", StringComparison.Ordinal),
            "connected-gameobject" => text.Replace("GameObject:\n", "GameObject:\n  m_CorrespondingSourceObject: {fileID: 42}\n", StringComparison.Ordinal),
            "wrong-component" => text.Replace("- component: {fileID: -2745811161023527127}", "- component: {fileID: 99}", StringComparison.Ordinal),
            "unknown-object-schema" => text.Replace("GameObject:\n", "GameObject:\n  serializedVersion: 999\n", StringComparison.Ordinal),
            "unknown-transform-schema" => text.Replace("Transform:\n", "Transform:\n  serializedVersion: 999\n", StringComparison.Ordinal),
            _ => text.Replace("!u!4 &", "!u!224 &", StringComparison.Ordinal).Replace("\nTransform:\n", "\nRectTransform:\n", StringComparison.Ordinal)
        };
        fixture.Write("Assets/Scene.unity", text); await fixture.CopyAsync();
        var scene = new SceneEditSession(ProjectSession.Open(fixture.Destination), "Assets/Scene.unity");
        var original = scene.GetBytes();
        Assert.False(scene.CanEditItem(scene.Items.First(i => i.GameObjectId == 7)));
        Assert.Throws<InvalidOperationException>(() => scene.SetName(7, "不得修改"));
        Assert.Throws<InvalidOperationException>(() => scene.SetPosition(TransformId, Vector3.One));
        Assert.Equal(original, scene.GetBytes());
    }

    [Theory]
    [InlineData("ProjectSettings/ProjectVersion.txt")]
    [InlineData("Packages/manifest.json")]
    [InlineData("Packages/packages-lock.json")]
    public async Task ExternalContextChangeRejectsSaveEvenWhenVersionsStillParse(string relative)
    {
        using var fixture = new TestProject(); await fixture.CopyAsync(); var project = ProjectSession.Open(fixture.Destination);
        var scene = new SceneEditSession(project, "Assets/Scene.unity"); var path = project.Resolve("Assets/Scene.unity"); var original = Hashing.File(path);
        scene.SetPosition(TransformId, Vector3.One); File.AppendAllText(project.Resolve(relative), "\n");
        Assert.Throws<SaveConflictException>(() => scene.Save());
        Assert.Equal(original, Hashing.File(path)); Assert.True(scene.IsDirty);
    }
}

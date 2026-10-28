using SceneWorkbench.Core;
using SceneWorkbench.Unity;

namespace SceneWorkbench.Tests;

public sealed class MaterialEditSessionTests
{
    public const string ShaderGuid = "dddddddddddddddddddddddddddddddd";
    public const string TextureGuid = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
    private const string MaterialText = """
        %YAML 1.1
        %TAG !u! tag:unity3d.com,2011:
        --- !u!21 &2100000
        Material:
          serializedVersion: 8
          m_Name: "保留 ASE"
          m_Shader: {fileID: 4800000, guid: dddddddddddddddddddddddddddddddd, type: 3}
          m_ValidKeywords: [KEEP_UNKNOWN]
          m_PrefabInstance: {fileID: 0}
          m_SavedProperties:
            serializedVersion: 3
            m_TexEnvs:
            - _Texture:
                m_Texture: {fileID: 0} # keep this comment
                m_Scale: {x: 1.0000, y: 1}
                m_Offset: {x: 0, y: 0}
            m_Ints: []
            m_Floats:
            - _AO: 0.8000 # preserve unrelated bytes
            m_Colors:
            - _Tint: {r: 1.0000, g: 0.5, b: 0.25, a: 1}
          unknown: [4, 8, 15, 16, 23, 42] # untouched
        """;
    internal static void AddMaterial(TestProject fixture)
    {
        fixture.Write("Assets/Test.mat", MaterialText.ReplaceLineEndings("\r\n") + "\r\n");
        fixture.Write("Assets/Test.mat.meta", "guid: ffffffffffffffffffffffffffffffff\nNativeFormatImporter:\n  mainObjectFileID: 2100000\n");
        fixture.Write("Assets/Test.shader", """
            Shader "ASE_fixture" {
            Properties {
              _Texture("Texture", 2D) = "white" {}
              _AO("AO", Range(0,1)) = 0.8
              _Tint("Tint", Color) = (1,.5,.25,1)
              _Missing("Missing", Float) = 4
            }
            SubShader { }
            /*ASEBEGIN preserved graph */
            """);
        fixture.Write("Assets/Test.shader.meta", "guid: " + ShaderGuid + "\nShaderImporter: {}\n");
        fixture.Write("Assets/Texture.png", "fixture bytes"); fixture.Write("Assets/Texture.png.meta", "guid: " + TextureGuid + "\nTextureImporter:\n  textureType: 0\n");
    }
    internal static void AddInteger(TestProject fixture)
    {
        AddMaterial(fixture);
        var shader = Path.Combine(fixture.Source, "Assets/Test.shader");
        File.WriteAllText(shader, File.ReadAllText(shader).Replace("_Missing(\"Missing\", Float) = 4", "_Count(\"Count\", Integer) = 2147483647\n  _Legacy(\"Legacy\", Int) = 1", StringComparison.Ordinal));
        var material = Path.Combine(fixture.Source, "Assets/Test.mat");
        File.WriteAllText(material, File.ReadAllText(material).Replace("m_Ints: []", "m_Ints:\r\n    - _Count: 1 # exact signed Int32 required", StringComparison.Ordinal).Replace("- _AO: 0.8000", "- _Legacy: 1\r\n    - _AO: 0.8000", StringComparison.Ordinal));
    }
    [Theory]
    [InlineData(16777217)]
    [InlineData(-16777217)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public async Task Int32NativeSaveReopenUndoAndRedoPreserveExactIntegerAndNeighbours(int value)
    {
        using var fixture = new TestProject(); AddInteger(fixture); await fixture.CopyAsync(); var project = ProjectSession.Open(fixture.Destination);
        var original = File.ReadAllBytes(project.Resolve("Assets/Test.mat")); var session = Open(project); var before = session.Snapshot(true);
        Assert.True(session.IsIntegerProperty("_Count")); Assert.True(session.IsLegacyIntProperty("_Legacy"));
        session.SetInteger("_Count", value); session.Save();
        var loaded = Open(project).Snapshot(true);
        Assert.Equal(value, Assert.IsType<int>(loaded.Properties.Single(p => p.Name == "_Count").Value[0]));
        Assert.True(loaded.Properties.Single(p => p.Name == "_Count").IntegerStorage);
        Assert.Equal(before.Properties.Single(p => p.Name == "_AO").Value, loaded.Properties.Single(p => p.Name == "_AO").Value);
        Assert.Equal(before.Properties.Single(p => p.Name == "_Tint").Value, loaded.Properties.Single(p => p.Name == "_Tint").Value);
        Assert.Contains($"_Count: {value} # exact signed Int32 required", File.ReadAllText(project.Resolve("Assets/Test.mat")), StringComparison.Ordinal);
        session.Undo(); session.Save(); Assert.Equal(original, File.ReadAllBytes(project.Resolve("Assets/Test.mat")));
        session.Redo(); session.Save(); Assert.Equal(value, Assert.IsType<int>(Open(project).Snapshot(true).Properties.Single(p => p.Name == "_Count").Value[0]));
        session.Undo(); session.Save(); Assert.Equal(original, File.ReadAllBytes(project.Resolve("Assets/Test.mat")));
    }
    private static MaterialEditSession Open(ProjectSession p) => new(p, "Assets/Test.mat", guid => guid == ShaderGuid ? "Assets/Test.shader" : guid == TextureGuid ? "Assets/Texture.png" : null);
    [Fact]
    public async Task NativeMaterialSaveReopenUndoPreservesShaderGraphMetaAndUnknownBytes()
    {
        using var fixture = new TestProject(); AddMaterial(fixture); fixture.ConfigureUrp("14.0.9", "2022.3"); await fixture.CopyAsync();
        var project = ProjectSession.Open(fixture.Destination); var original = File.ReadAllBytes(project.Resolve("Assets/Test.mat"));
        var shader = Hashing.File(project.Resolve("Assets/Test.shader")); var meta = Hashing.File(project.Resolve("Assets/Test.mat.meta"));
        var session = Open(project);
        Assert.Equal(0, session.Save().ChangedFiles);
        Assert.False(session.Snapshot(true).Properties.Single(p => p.Name == "_Missing").Editable);
        session.SetNumbers("_AO", [.35f]); session.SetNumbers("_Tint", [.2f, .3f, .4f, .5f]);
        session.SetNumbers("_Texture", [2, 3], "scale"); session.SetTexture("_Texture", "Assets/Texture.png");
        Assert.Equal(1, session.Save().ChangedFiles); Assert.False(session.IsDirty);
        var loaded = Open(project).Snapshot(true);
        Assert.Equal(.35f, loaded.Properties.Single(p => p.Name == "_AO").Value[0]);
        Assert.Equal("Assets/Texture.png", loaded.Properties.Single(p => p.Name == "_Texture").TexturePath);
        Assert.Contains("unknown: [4, 8, 15, 16, 23, 42] # untouched", File.ReadAllText(project.Resolve("Assets/Test.mat")), StringComparison.Ordinal);
        Assert.Equal(shader, Hashing.File(project.Resolve("Assets/Test.shader"))); Assert.Equal(meta, Hashing.File(project.Resolve("Assets/Test.mat.meta")));
        Assert.Contains(session.LastSaveDiagnostics, d => d.Code == "PACKAGE_EDITOR_MISMATCH");
        for (var i = 0; i < 4; i++) session.Undo(); session.Save();
        Assert.Equal(original, File.ReadAllBytes(project.Resolve("Assets/Test.mat")));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(fixture.Source, "Assets/Test.mat")));
        session.Redo(); Assert.True(session.IsDirty);
    }
    [Theory]
    [InlineData("Assets/Test.mat")]
    [InlineData("Assets/Test.mat.meta")]
    [InlineData("Assets/Test.shader")]
    [InlineData("Packages/manifest.json")]
    public async Task ExternalChangesRejectSaveAndKeepMaterialDraft(string changed)
    {
        using var fixture = new TestProject(); AddMaterial(fixture); await fixture.CopyAsync(); var p = ProjectSession.Open(fixture.Destination); var session = Open(p);
        session.SetNumbers("_AO", [.4f]); File.AppendAllText(p.Resolve(changed), "\n# external change\n"); var expected = Hashing.File(p.Resolve(changed));
        Assert.Throws<SaveConflictException>(() => session.Save()); Assert.True(session.IsDirty); Assert.Equal(expected, Hashing.File(p.Resolve(changed)));
    }
    [Fact]
    public async Task InvalidFieldNumericAndDuplicateGuidsDoNotMutateOrWrite()
    {
        using var fixture = new TestProject(); AddMaterial(fixture); await fixture.CopyAsync(); var p = ProjectSession.Open(fixture.Destination); var session = Open(p);
        Assert.Throws<ArgumentException>(() => session.SetNumbers("_Tint", [1, 2, float.NaN, 4]));
        Assert.Throws<InvalidOperationException>(() => session.SetNumbers("_Missing", [5])); Assert.False(session.IsDirty); Assert.Equal(0, session.UndoCount);
        session.SetNumbers("_AO", [.2f]); File.WriteAllText(p.Resolve("Assets/Other.asset"), "fixture"); File.WriteAllText(p.Resolve("Assets/Other.asset.meta"), "guid: " + ShaderGuid + "\n");
        Assert.Throws<InvalidOperationException>(() => session.Save()); Assert.True(session.IsDirty);
    }
    [Fact]
    public void SourceProjectIsNeverWritable()
    { using var fixture = new TestProject(); AddMaterial(fixture); var session = Open(ProjectSession.Open(fixture.Source)); session.SetNumbers("_AO", [.5f]); Assert.Throws<InvalidOperationException>(() => session.Save()); }
}

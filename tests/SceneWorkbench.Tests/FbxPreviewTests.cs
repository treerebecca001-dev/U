using SceneWorkbench.Core;
using SceneWorkbench.Unity;
using YamlDotNet.RepresentationModel;

namespace SceneWorkbench.Tests;

public sealed class FbxPreviewTests
{
    private const string FbxGuid = "1234567890abcdef1234567890abcdef";
    private const string PrefabGuid = "1234567890abcdef1234567890abcdee";
    private const string MatGuid = "ffffffffffffffffffffffffffffffff";
    private const string OtherMatGuid = "11111111111111111111111111111111";
    private const string RendererId = "-7511558181221131132";
    [Fact]
    public void NativeFbxOverridesRetainLayerAndDisabledShadowOrProbeFlags()
    {
        var result = FbxInstanceOverrides.Read(FbxGuid, Changes(Change(RendererId, "m_Materials.Array.data[0]")
            + Change(RendererId, "m_CastShadows", "0") + Change(RendererId, "m_ReceiveShadows", "0")
            + Change(RendererId, "m_ReflectionProbeUsage", "0") + Change(RendererId, "m_RenderingLayerMask", "4")
            + Change("18", "m_Layer", "19")), (_, _) => "Assets/Test.mat", _ => { });
        var renderer = Assert.Single(result.Renderers);
        Assert.Equal(0, renderer.CastShadows); Assert.False(renderer.ReceiveShadows);
        Assert.Equal(0, renderer.ReflectionProbeUsage); Assert.Equal(4u, renderer.RenderingLayerMask); Assert.Equal(19, result.Layer);
    }
    private static string Change(string target, string property, string value = "", string guid = FbxGuid, string material = MatGuid, string materialId = "2100000") => $$"""
        - target: {fileID: {{target}}, guid: {{guid}}, type: 3}
          propertyPath: {{property}}
          value: '{{value}}'
          objectReference: {fileID: {{materialId}}, guid: {{material}}, type: 2}
        """ + "\n";
    private static IEnumerable<YamlNode> Changes(string text)
    { var yaml = new YamlStream(); yaml.Load(new StringReader(text)); return ((YamlSequenceNode)yaml.Documents[0].RootNode).Children; }

    [Fact]
    public void NativeFbxOverridesKeepExactIdsSlotHolesAndLastOverrideWithoutGuessing()
    {
        var warnings = new List<string>(); var refs = new List<string>();
        var result = FbxInstanceOverrides.Read(FbxGuid, Changes(
            Change(RendererId, "m_Materials.Array.data[0]") +
            Change(RendererId, "m_Materials.Array.data[2]", material: OtherMatGuid) +
            Change(RendererId, "m_Materials.Array.data[0]", material: OtherMatGuid) +
            Change(RendererId, "m_Enabled", "0") +
            Change("-8679921383154817045", "m_LocalPosition.y", "-0.3") +
            Change("-8679921383154817045", "m_LocalScale.x", "1.1") +
            Change("17", "m_Enabled", "0") +
            Change("8", "m_Materials.Array.data[0]", guid: OtherMatGuid)),
            (g, id) => { refs.Add(g + "/" + id); return g == MatGuid ? "Assets/Test.mat" : "Assets/Other.mat"; }, warnings.Add);
        var renderer = Assert.Single(result.Renderers);
        Assert.Equal(RendererId, renderer.FileId); Assert.False(renderer.Enabled);
        Assert.Equal(3, renderer.Slots.Length); Assert.Equal("Assets/Other.mat", renderer.Slots[0].Path);
        Assert.Null(renderer.Slots[1].Path); Assert.Equal("0", renderer.Slots[1].FileId);
        Assert.Equal("2100000", renderer.Slots[2].FileId); Assert.Equal(3, refs.Count);
        Assert.Equal("-8679921383154817045", result.TransformFileId);
        Assert.Equal(new float[] { 0, -.3f, 0 }, result.Position); Assert.Equal(new float[] { 1.1f, 1, 1 }, result.Scale);
        Assert.Empty(warnings);
    }

    [Fact]
    public void AmbiguousAndUnresolvedNativeReferencesRetainDiagnosticsAndSeparateRenderers()
    {
        var warnings = new List<string>();
        var result = FbxInstanceOverrides.Read(FbxGuid, Changes(
            Change(RendererId, "m_Materials.Array.data[0]", materialId: "9223372036854775808") +
            Change("33", "m_Materials.Array.data[64]") +
            Change("44", "m_LocalPosition.x", "1") + Change("45", "m_LocalPosition.x", "2")), (_, _) => throw new Exception("Invalid ID must not resolve"), warnings.Add);
        Assert.Equal(2, result.Renderers.Length); Assert.Null(result.Renderers[0].Slots[0].Path);
        Assert.Empty(result.Renderers[1].Slots); Assert.Null(result.TransformFileId);
        Assert.Equal(new float[] { 0, 0, 0 }, result.Position); Assert.Equal(3, warnings.Count);
    }

    [Fact]
    public void NestedPrefabOverrideOnlyBindsItsOwnStrippedFbxRendererAndPreservesNativeBytes()
    {
        using var fixture = new TestProject(); MaterialEditSessionTests.AddMaterial(fixture);
        fixture.Write("Assets/Other.mat", File.ReadAllText(Path.Combine(fixture.Source, "Assets/Test.mat")));
        fixture.Write("Assets/Other.mat.meta", "guid: " + OtherMatGuid + "\nNativeFormatImporter:\n  mainObjectFileID: 2100000\n");
        fixture.Write("Assets/Model.fbx", "source fixture; catalog must not decode or rewrite it");
        fixture.Write("Assets/Model.fbx.meta", "guid: " + FbxGuid + "\nModelImporter:\n  internalIDToNameTable: []\n");
        var prefab = Header + Instance(201, FbxGuid, Change(RendererId, "m_Materials.Array.data[0]"))
            + Instance(202, FbxGuid, Change(RendererId, "m_Materials.Array.data[0]")) + $$"""
            --- !u!23 &10001 stripped
            MeshRenderer:
              m_CorrespondingSourceObject: {fileID: {{RendererId}}, guid: {{FbxGuid}}, type: 3}
              m_PrefabInstance: {fileID: 201}
            --- !u!23 &10002 stripped
            MeshRenderer:
              m_CorrespondingSourceObject: {fileID: {{RendererId}}, guid: {{FbxGuid}}, type: 3}
              m_PrefabInstance: {fileID: 202}
            """ + "\n";
        fixture.Write("Assets/Group.prefab", prefab); fixture.Write("Assets/Group.prefab.meta", "guid: " + PrefabGuid + "\nPrefabImporter: {}\n");
        var scene = Header + Instance(100, PrefabGuid, Change("10001", "m_Materials.Array.data[0]", guid: PrefabGuid, material: OtherMatGuid));
        fixture.Write("Assets/Scene.unity", scene);
        var before = Directory.GetFiles(fixture.Source, "*", SearchOption.AllDirectories).ToDictionary(p => p, Hashing.File);
        var project = ProjectSession.Open(fixture.Source); var catalog = new PreviewCatalog(project);
        var graph = catalog.Scene("Assets/Scene.unity", File.ReadAllBytes(project.Resolve("Assets/Scene.unity")));
        Assert.Equal("Assets/Other.mat", Assert.Single(graph.Nodes.Single(n => n.Id == "100/201/fbx").Materials));
        Assert.Equal("Assets/Test.mat", Assert.Single(graph.Nodes.Single(n => n.Id == "100/202/fbx").Materials));
        Assert.All(graph.Nodes.Where(n => n.FbxInstance is not null), n => Assert.Equal(RendererId, Assert.Single(n.FbxInstance!.Renderers).FileId));
        Assert.Equal(2, graph.Materials.Length);
        foreach (var (path, hash) in before) Assert.Equal(hash, Hashing.File(path));
    }

    [Fact]
    public void FbxMaterialGuidWithWrongFileIdDoesNotBecomeAValidBinding()
    {
        using var fixture = new TestProject(); MaterialEditSessionTests.AddMaterial(fixture);
        fixture.Write("Assets/Model.fbx", "fixture"); fixture.Write("Assets/Model.fbx.meta", "guid: " + FbxGuid + "\nModelImporter: {}\n");
        var text = Header + Instance(100, FbxGuid, Change(RendererId, "m_Materials.Array.data[0]", materialId: "2100001"));
        var catalog = new PreviewCatalog(ProjectSession.Open(fixture.Source));
        var graph = catalog.Scene("Assets/Scene.unity", System.Text.Encoding.UTF8.GetBytes(text));
        Assert.Equal("", Assert.Single(graph.Nodes.Single(n => n.Id.EndsWith("/fbx", StringComparison.Ordinal)).Materials));
        Assert.Empty(graph.Materials); Assert.Contains(graph.Diagnostics, d => d.Contains(MatGuid + "/2100001", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingShaderExposesReadOnlySavedPropertiesWithExactInt32AndTextureIdentity()
    {
        using var fixture = new TestProject(); MaterialEditSessionTests.AddInteger(fixture);
        var material = Path.Combine(fixture.Source, "Assets/Test.mat");
        File.WriteAllText(material, File.ReadAllText(material).Replace("m_Texture: {fileID: 0}", "m_Texture: {fileID: 2800000, guid: " + MaterialEditSessionTests.TextureGuid + ", type: 3}", StringComparison.Ordinal).Replace("_Count: 1 #", "_Count: 2147483647 #", StringComparison.Ordinal));
        await fixture.CopyAsync(); var project = ProjectSession.Open(fixture.Destination); var before = Hashing.File(project.Resolve("Assets/Test.mat"));
        var session = new MaterialEditSession(project, "Assets/Test.mat", guid => guid == MaterialEditSessionTests.TextureGuid ? "Assets/Texture.png" : null);
        var snapshot = session.Snapshot(true); Assert.False(snapshot.Editable); Assert.Equal("saved-properties", snapshot.PreviewKind);
        Assert.Equal("4800000", snapshot.ShaderFileId); Assert.All(snapshot.Properties, p => Assert.False(p.Editable));
        Assert.Equal(int.MaxValue, Assert.IsType<int>(snapshot.Properties.Single(p => p.Name == "_Count").Value[0]));
        var texture = snapshot.Properties.Single(p => p.Name == "_Texture");
        Assert.Equal("Assets/Texture.png", texture.TexturePath); Assert.Equal("2800000", texture.TextureFileId);
        Assert.Equal(new float[] { 1, 1 }, texture.Scale); Assert.Equal(new float[] { 0, 0 }, texture.Offset);
        Assert.Throws<InvalidOperationException>(() => session.SetNumbers("_AO", [.2f]));
        Assert.Throws<InvalidOperationException>(() => session.SetTexture("_Texture", null));
        Assert.Throws<InvalidOperationException>(() => session.Save());
        Assert.False(session.IsDirty); Assert.Equal(before, Hashing.File(project.Resolve("Assets/Test.mat")));
    }

    [Fact]
    public async Task WaterTemplatePropertiesSupportPreviewButNeverNativeEditing()
    {
        using var fixture = new TestProject(); MaterialEditSessionTests.AddMaterial(fixture);
        var water = fixture.Write("Assets/Water.watershader", File.ReadAllText(Path.Combine(fixture.Source, "Assets/Test.shader")));
        await fixture.CopyAsync(); var project = ProjectSession.Open(fixture.Destination);
        var session = new MaterialEditSession(project, "Assets/Test.mat", guid => guid == MaterialEditSessionTests.ShaderGuid ? "Assets/Water.watershader" : null);
        var snapshot = session.Snapshot(true); Assert.Equal("water-template", snapshot.PreviewKind);
        Assert.Equal(Hashing.File(water), snapshot.ShaderHash); Assert.Contains(snapshot.Properties, p => p.Name == "_Texture");
        Assert.False(session.SupportsEditing); Assert.False(snapshot.Editable); Assert.All(snapshot.Properties, p => Assert.False(p.Editable));
        Assert.Throws<InvalidOperationException>(() => session.SetNumbers("_AO", [.2f]));
        Assert.Throws<InvalidOperationException>(() => session.Save());
        Assert.Equal(Hashing.File(water), Hashing.File(project.Resolve("Assets/Water.watershader")));
    }

    [Fact]
    public void WaterLibrarySourceChangesInvalidateItsPreviewIdentityWithoutGrantingEditing()
    {
        using var fixture = new TestProject(); MaterialEditSessionTests.AddMaterial(fixture);
        fixture.Write("Assets/Water.watershader", File.ReadAllText(Path.Combine(fixture.Source, "Assets/Test.shader")));
        fixture.Write("Assets/Libraries/Waves.hlsl", "original native wave library");
        var project = ProjectSession.Open(fixture.Source);
        MaterialEditSession OpenWater() => new(project, "Assets/Test.mat", guid => guid == MaterialEditSessionTests.ShaderGuid ? "Assets/Water.watershader" : null);
        var before = OpenWater().Snapshot(true);
        Assert.Equal(Hashing.File(project.Resolve("Assets/Libraries/Waves.hlsl")), before.ShaderDependencies!["Waves.hlsl"]);
        fixture.Write("Assets/Libraries/Waves.hlsl", "externally changed library");
        var after = OpenWater().Snapshot(true); Assert.NotEqual(before.ShaderDependencies["Waves.hlsl"], after.ShaderDependencies!["Waves.hlsl"]);
        Assert.Equal("", after.ShaderDependencies["Features.hlsl"]); Assert.False(after.Editable);
    }

    private const string Header = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n";
    private static string Instance(long id, string sourceGuid, string changes) => $$"""
        --- !u!1001 &{{id}}
        PrefabInstance:
          m_Modification:
            m_TransformParent: {fileID: 0}
            m_Modifications:
        {{string.Join('\n', changes.TrimEnd().Split('\n').Select(line => "    " + line))}}
          m_SourcePrefab: {fileID: 100100000, guid: {{sourceGuid}}, type: 3}
        """ + "\n";
}

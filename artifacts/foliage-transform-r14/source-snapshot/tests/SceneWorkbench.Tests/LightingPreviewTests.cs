using System.Text;
using SceneWorkbench.Core;
using SceneWorkbench.Unity;

namespace SceneWorkbench.Tests;

public sealed class LightingPreviewTests
{
    private const string LightObject = """
        --- !u!108 &900000000000000001
        Light:
          m_GameObject: {fileID: 7}
          m_Enabled: 1
          m_Type: 1
          m_Color: {r: 1, g: 1, b: 1, a: 1}
          m_Intensity: 0.8
          m_CullingMask: {m_Bits: 4066832}
          m_RenderingLayerMask: 4
          m_Shadows:
            m_Type: 1
            m_Strength: 0.6
            m_Bias: 0.05
            m_NormalBias: 0.4
            m_NearPlane: 0.2
            m_Resolution: -1
            m_CustomResolution: -1
            m_UseCullingMatrixOverride: 0
        """ + "\n";
    [Fact]
    public void NativeRendererAndLightShadingFlagsRemainExactAndReadOnly()
    {
        using var fixture = new TestProject();
        var text = TestProject.SceneText() + LightObject + """
            --- !u!23 &900000000000000002
            MeshRenderer:
              m_GameObject: {fileID: 7}
              m_Enabled: 1
              m_CastShadows: 2
              m_ReceiveShadows: 0
              m_ReflectionProbeUsage: 0
              m_RenderingLayerMask: 8
            --- !u!104 &2
            RenderSettings:
              m_SkyboxMaterial: {fileID: 10304, guid: 0000000000000000f000000000000000, type: 0}
              m_DefaultReflectionMode: 0
              m_ReflectionIntensity: 0.75
              m_CustomReflection: {fileID: 0}
            """ + "\n";
        var path = fixture.Write("Assets/Scene.unity", text); var hash = Hashing.File(path);
        var graph = new PreviewCatalog(ProjectSession.Open(fixture.Source)).Scene("Assets/Scene.unity", Encoding.UTF8.GetBytes(text));
        var node = Assert.Single(graph.Nodes); Assert.Equal(31, node.Layer);
        Assert.Equal("900000000000000002", node.Renderer!.FileId); Assert.Equal(2, node.Renderer.CastShadows);
        Assert.False(node.Renderer.ReceiveShadows); Assert.Equal(0, node.Renderer.ReflectionProbeUsage); Assert.Equal(8u, node.Renderer.RenderingLayerMask);
        Assert.Equal(4066832u, node.Light!.CullingMask); Assert.Equal(4u, node.Light.RenderingLayerMask);
        Assert.Equal(1, node.Light.Shadows!.Type); Assert.Equal(.6f, node.Light.Shadows.Strength); Assert.Equal(.4f, node.Light.Shadows.NormalBias);
        Assert.Equal("10304", graph.Environment!.SkyboxFileId); Assert.Null(graph.Environment.SkyboxPath);
        Assert.Null(graph.Environment.BakedReflection); Assert.Equal(.75f, graph.Environment.ReflectionIntensity); Assert.Equal(hash, Hashing.File(path));
    }
    [Fact]
    public void NestedPrefabShadowOverrideUsesActualSourceAndPreservesBothFiles()
    {
        using var fixture = new TestProject(); const string guid = "1234567890abcdef1234567890abcdee";
        var prefab = fixture.Write("Assets/Sun.prefab", TestProject.SceneText() + LightObject);
        fixture.Write("Assets/Sun.prefab.meta", "guid: " + guid + "\n");
        var text = $$"""
            %YAML 1.1
            %TAG !u! tag:unity3d.com,2011:
            --- !u!1001 &9
            PrefabInstance:
              m_Modification:
                m_TransformParent: {fileID: 0}
                m_Modifications:
                - target: {fileID: 900000000000000001, guid: {{guid}}, type: 3}
                  propertyPath: m_Shadows.m_Strength
                  value: 0.3
                  objectReference: {fileID: 0}
              m_SourcePrefab: {fileID: 100100000, guid: {{guid}}, type: 3}
            """ + "\n";
        var scene = fixture.Write("Assets/Scene.unity", text); var before = new[] { Hashing.File(scene), Hashing.File(prefab) };
        var graph = new PreviewCatalog(ProjectSession.Open(fixture.Source)).Scene("Assets/Scene.unity", Encoding.UTF8.GetBytes(text));
        var light = Assert.Single(graph.Nodes, n => n.Light is not null).Light!;
        Assert.Equal(.3f, light.Shadows!.Strength); Assert.Equal(1, light.Shadows.Type);
        Assert.Equal(before, new[] { Hashing.File(scene), Hashing.File(prefab) });
    }
}

using System.Text;
using SceneWorkbench.Core;
using SceneWorkbench.Unity;

namespace SceneWorkbench.Tests;

public sealed class PrefabOverridePreviewTests
{
    private const string Model = "1234567890abcdef1234567890abcdef";
    private const string OtherModel = "22222222222222222222222222222222";
    private const string Group = "33333333333333333333333333333333";
    private const string Child = "44444444444444444444444444444444";
    private const string Mat = "ffffffffffffffffffffffffffffffff";
    private const string Header = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n";

    [Fact]
    public void ImplicitNestedRendererHasExactVersionedMappingAndDoesNotChangeItsSibling()
    {
        using var f = Fixture();
        // Actual signed IDs and correspondence anchor from 60001_Wall_P's wall_02.
        const long nested = 3112415062092470202, renderer = -7511558181221131132, transform = -8679921383154817045;
        const long anchor = 3223236475665887313, implicitRenderer = 4391027932898413374;
        WritePrefab(f, "Group", Group, Instance(nested, Model, Material(renderer, Model) + Change(renderer, Model, "m_CastShadows", "0"))
            + Stripped(anchor, 4, nested, Model, transform)
            + Instance(202, Model, Material(renderer, Model) + Change(renderer, Model, "m_CastShadows", "0")));
        var graph = Read(f, Instance(100, Group, Change(implicitRenderer, Group, "m_CastShadows", "1")));
        Assert.Equal(1, graph.Nodes.Single(n => n.Id == $"100/{nested}/fbx").Renderer!.CastShadows);
        Assert.Equal(0, graph.Nodes.Single(n => n.Id == "100/202/fbx").Renderer!.CastShadows);
        var mapping = Assert.Single(graph.PrefabOverrides!);
        Assert.Equal("resolved", mapping.Status); Assert.Equal("validated-nested-xor63", mapping.Method);
        Assert.Equal("4391027932898413374", mapping.TargetFileId); Assert.Equal("-7511558181221131132", mapping.SourceFileId);
        Assert.Empty(graph.Diagnostics);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrContradictoryAnchorNeverAuthorizesImplicitRouting(bool badAnchor)
    {
        using var f = Fixture();
        WritePrefab(f, "Group", Group, Instance(100, Model, Material(8, Model) + Change(8, Model, "m_CastShadows", "0"))
            + (badAnchor ? Stripped(999, 4, 100, Model, 1) : ""));
        var graph = Read(f, Instance(500, Group, Change(108, Group, "m_CastShadows", "1")));
        Assert.Equal(0, graph.Nodes.Single(n => n.FbxInstance is not null).Renderer!.CastShadows);
        Assert.Equal("unresolved-target", Assert.Single(graph.PrefabOverrides!).Status);
        Assert.Contains(graph.Diagnostics, d => d.Contains("108", StringComparison.Ordinal));
    }

    [Fact]
    public void ImplicitCandidateCollisionCannotApplyAnOverrideToEitherInstance()
    {
        using var f = Fixture();
        f.Write("Assets/Other.fbx", "read-only FBX fixture"); f.Write("Assets/Other.fbx.meta", "guid: " + OtherModel + "\nModelImporter: {}\n");
        WritePrefab(f, "Group", Group,
            Instance(100, Model, Material(8, Model) + Change(8, Model, "m_CastShadows", "0")) + Stripped(101, 4, 100, Model, 1)
            + Instance(200, OtherModel, Material(164, OtherModel) + Change(164, OtherModel, "m_CastShadows", "0")) + Stripped(202, 4, 200, OtherModel, 2));
        var graph = Read(f, Instance(500, Group, Change(108, Group, "m_CastShadows", "1")));
        Assert.All(graph.Nodes.Where(n => n.FbxInstance is not null), n => Assert.Equal(0, n.Renderer!.CastShadows));
        Assert.Equal("ambiguous-target", Assert.Single(graph.PrefabOverrides!).Status);
        Assert.Contains(graph.Diagnostics, d => d.Contains("多個候選", StringComparison.Ordinal));
    }

    [Fact]
    public void ExplicitCorrespondenceAliasesPreserveOriginalLastModificationOrder()
    {
        using var f = Fixture();
        WritePrefab(f, "Group", Group, Instance(201, Model, Material(8, Model))
            + Stripped(10001, 23, 201, Model, 8) + Stripped(10002, 23, 201, Model, 8));
        var graph = Read(f, Instance(500, Group, Change(10001, Group, "m_CastShadows", "0")
            + Change(10002, Group, "m_CastShadows", "1") + Change(10001, Group, "m_CastShadows", "2")));
        Assert.Equal(2, graph.Nodes.Single(n => n.FbxInstance is not null).Renderer!.CastShadows);
        Assert.All(graph.PrefabOverrides!, d => Assert.Equal("stripped-correspondence", d.Method));
        Assert.Empty(graph.Diagnostics);
    }

    [Fact]
    public void ExplicitNestedPrefabCorrespondenceRoutesOnlyTheNamedChildAndRemainsReadOnly()
    {
        using var f = Fixture(); WritePrefab(f, "Child", Child, NativeObjects);
        WritePrefab(f, "Group", Group, Instance(201, Child, "") + Instance(202, Child, "") + Stripped(10001, 4, 201, Child, 11));
        var graph = Read(f, Instance(500, Group, Change(10001, Group, "m_LocalPosition.x", "7")));
        Assert.Equal(7, graph.Nodes.Single(n => n.Id == "500/201/11").Position[0]);
        Assert.Equal(0, graph.Nodes.Single(n => n.Id == "500/202/11").Position[0]);
        Assert.All(graph.Nodes, n => Assert.Equal("500", n.OwnerId));
        Assert.Empty(graph.Diagnostics);
    }

    [Fact]
    public void RemovingKnownRendererAffectsOnlyItsOwnInstanceAndPreservesAllSourceBytes()
    {
        using var f = Fixture(); WritePrefab(f, "Child", Child, NativeObjects);
        var graph = Read(f, Instance(100, Child, "", $"- {{fileID: 13, guid: {Child}, type: 3}}\n") + Instance(200, Child, ""));
        Assert.Null(graph.Nodes.Single(n => n.Id == "100/11").MeshPath);
        Assert.Equal("10202", graph.Nodes.Single(n => n.Id == "200/11").MeshFileId);
        Assert.Equal("removed-component", Assert.Single(graph.PrefabOverrides!).Status);
        Assert.Empty(graph.Diagnostics);
    }

    [Fact]
    public void AbsentComponentInCompleteNonNestedSourceIsUnusedRemovalWithoutRemovingAnotherComponent()
    {
        using var f = Fixture(); WritePrefab(f, "Child", Child, NativeObjects);
        var graph = Read(f, Instance(100, Child, "", $"- {{fileID: 5094106267881264939, guid: {Child}, type: 3}}\n"));
        Assert.Equal("10202", graph.Nodes.Single(n => n.Id == "100/11").MeshFileId);
        Assert.Equal("unused-removal", Assert.Single(graph.PrefabOverrides!).Status);
        Assert.Contains(graph.Diagnostics, d => d.Contains("5094106267881264939", StringComparison.Ordinal) && d.Contains("原文保留", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(11, Child)]
    [InlineData(13, Model)]
    public void TransformOrWrongAssetRemovalIsNotAppliedOrMisclassifiedAsUnused(long id, string guid)
    {
        using var f = Fixture(); WritePrefab(f, "Child", Child, NativeObjects);
        var graph = Read(f, Instance(100, Child, "", $"- {{fileID: {id}, guid: {guid}, type: 3}}\n"));
        Assert.Equal("10202", graph.Nodes.Single(n => n.Id == "100/11").MeshFileId);
        Assert.Equal("unresolved-removal", Assert.Single(graph.PrefabOverrides!).Status);
        Assert.NotEmpty(graph.Diagnostics);
    }

    private static TestProject Fixture()
    {
        var f = new TestProject(); MaterialEditSessionTests.AddMaterial(f);
        f.Write("Assets/Model.fbx", "read-only FBX fixture"); f.Write("Assets/Model.fbx.meta", "guid: " + Model + "\nModelImporter: {}\n");
        return f;
    }
    private static void WritePrefab(TestProject f, string name, string guid, string body)
    { f.Write($"Assets/{name}.prefab", Header + body); f.Write($"Assets/{name}.prefab.meta", "guid: " + guid + "\nPrefabImporter: {}\n"); }
    private static PreviewGraph Read(TestProject f, string body)
    {
        var before = Directory.GetFiles(f.Source, "*", SearchOption.AllDirectories).ToDictionary(p => p, Hashing.File);
        var project = ProjectSession.Open(f.Source); var graph = new PreviewCatalog(project).Scene("Assets/Scene.unity", Encoding.UTF8.GetBytes(Header + body));
        foreach (var (path, hash) in before) Assert.Equal(hash, Hashing.File(path));
        return graph;
    }
    private static string Change(long id, string guid, string property, string value = "", string reference = "fileID: 0") => $$"""
        - target: {fileID: {{id}}, guid: {{guid}}, type: 3}
          propertyPath: {{property}}
          value: '{{value}}'
          objectReference: {{{reference}}}
        """ + "\n";
    private static string Material(long id, string guid) => Change(id, guid, "m_Materials.Array.data[0]", reference: $"fileID: 2100000, guid: {Mat}, type: 2");
    private static string Instance(long id, string guid, string changes, string removed = "") => $$"""
        --- !u!1001 &{{id}}
        PrefabInstance:
          serializedVersion: 2
          m_Modification:
            serializedVersion: 3
            m_TransformParent: {fileID: 0}
            m_Modifications:{{(changes.Length == 0 ? " []" : "")}}
        {{string.Join('\n', changes.TrimEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => "    " + line))}}
            m_RemovedComponents:{{(removed.Length == 0 ? " []" : "")}}
        {{string.Join('\n', removed.TrimEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => "    " + line))}}
          m_SourcePrefab: {fileID: 100100000, guid: {{guid}}, type: 3}
        """ + "\n";
    private static string Stripped(long id, int type, long instance, string guid, long source) => $$"""
        --- !u!{{type}} &{{id}} stripped
        Object:
          m_CorrespondingSourceObject: {fileID: {{source}}, guid: {{guid}}, type: 3}
          m_PrefabInstance: {fileID: {{instance}}}
        """ + "\n";
    private const string NativeObjects = """
        --- !u!1 &10
        GameObject:
          m_Name: Source
          m_IsActive: 1
          m_Component:
          - component: {fileID: 11}
          - component: {fileID: 12}
          - component: {fileID: 13}
        --- !u!4 &11
        Transform:
          m_GameObject: {fileID: 10}
          m_Father: {fileID: 0}
          m_LocalPosition: {x: 0, y: 0, z: 0}
          m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
          m_LocalScale: {x: 1, y: 1, z: 1}
        --- !u!33 &12
        MeshFilter:
          m_GameObject: {fileID: 10}
          m_Mesh: {fileID: 10202, guid: 0000000000000000e000000000000000, type: 0}
        --- !u!23 &13
        MeshRenderer:
          m_GameObject: {fileID: 10}
          m_Enabled: 1
          m_Materials: []
        """ + "\n";
}

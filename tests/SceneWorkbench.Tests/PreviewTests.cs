using SceneWorkbench.Core;
using SceneWorkbench.Unity;

namespace SceneWorkbench.Tests;

public sealed class PreviewTests
{
    internal static string MeshText()
    {
        using var memory = new MemoryStream(); using var writer = new BinaryWriter(memory);
        foreach (var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }) writer.Write(value);
        while (memory.Length < 48) writer.Write((byte)0);
        foreach (var value in new float[] { 0, 0, 1, 0, 0, 1 }) writer.Write(value);
        return $$"""
            %YAML 1.1
            %TAG !u! tag:unity3d.com,2011:
            --- !u!43 &4300000
            Mesh:
              m_Name: native triangle
              serializedVersion: 10
              m_IndexFormat: 0
              m_IndexBuffer: 000001000200
              m_SubMeshes:
              - firstByte: 0
                indexCount: 3
                topology: 0
                baseVertex: 0
              m_VertexData:
                m_VertexCount: 3
                m_Channels:
                - {stream: 0, offset: 0, format: 0, dimension: 3}
                - {stream: 0, offset: 0, format: 0, dimension: 0}
                - {stream: 0, offset: 0, format: 0, dimension: 0}
                - {stream: 0, offset: 0, format: 0, dimension: 0}
                - {stream: 1, offset: 0, format: 0, dimension: 2}
                m_DataSize: 72
                _typelessdata: {{Convert.ToHexString(memory.ToArray()).ToLowerInvariant()}}
            """;
    }
    [Fact]
    public void NativeMeshDecodesAlignedStreamsAndRejectsInvalidIndices()
    {
        using var fixture = new TestProject(); fixture.Write("Assets/Mesh.asset", MeshText()); var p = ProjectSession.Open(fixture.Source);
        var mesh = NativeMeshReader.Read(p, "Assets/Mesh.asset", 4300000);
        Assert.Equal(3, mesh.VertexCount); Assert.Equal(new[] { 0, 1, 2 }, mesh.Indices);
        Assert.Equal(new float[] { 0, 0, 1, 0, 0, 1 }, mesh.Attributes["uv"]);
        fixture.Write("Assets/Mesh.asset", MeshText().Replace("000001000200", "000001000300", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => NativeMeshReader.Read(p, "Assets/Mesh.asset", 4300000));
    }
    [Fact]
    public void PreviewCacheInvalidatesOnMetaChangeAndCancellationDoesNotReplaceCache()
    {
        using var fixture = new TestProject(); MaterialEditSessionTests.AddMaterial(fixture); var p = ProjectSession.Open(fixture.Source); var cache = Path.Combine(fixture.Root, "cache");
        var first = new PreviewCatalog(p, cacheDirectory: cache); Assert.Equal("Assets/Test.shader", first.Resolve(MaterialEditSessionTests.ShaderGuid));
        var file = Directory.GetFiles(cache).Single(); var hash = Hashing.File(file);
        var cached = new PreviewCatalog(p, cacheDirectory: cache); Assert.Equal("Assets/Test.shader", cached.Resolve(MaterialEditSessionTests.ShaderGuid)); Assert.Equal(hash, Hashing.File(file));
        Assert.Throws<OperationCanceledException>(() => new PreviewCatalog(p, new CancellationToken(true), cacheDirectory: cache)); Assert.Equal(hash, Hashing.File(file));
        fixture.Write("Assets/Test.shader.meta", "guid: 11111111111111111111111111111111\nShaderImporter: {}\n");
        var changed = new PreviewCatalog(p, cacheDirectory: cache); Assert.Null(changed.Resolve(MaterialEditSessionTests.ShaderGuid)); Assert.NotEqual(hash, Hashing.File(file));
    }
    [Fact]
    public async Task MaterialWithAdditionalUnityObjectKeepsThatObjectByteIdentical()
    {
        using var fixture = new TestProject(); MaterialEditSessionTests.AddMaterial(fixture);
        File.AppendAllText(Path.Combine(fixture.Source, "Assets/Test.mat"), "--- !u!114 &-987654321012345678\r\nMonoBehaviour:\r\n  m_Unknown: [1, 2, 3] # keep exact\r\n");
        await fixture.CopyAsync();
        var p = ProjectSession.Open(fixture.Destination); var catalog = new PreviewCatalog(p); var original = UnityYamlDocument.Load(p.Resolve("Assets/Test.mat"));
        var material = catalog.Material("Assets/Test.mat"); material.SetNumbers("_AO", [.45f]);
        // The public snapshot reads the current patched YAML, including the extra document.
        Assert.Equal(.45f, material.Snapshot(true).Properties.Single(x => x.Name == "_AO").Value[0]);
        material.Save();
        Assert.Equal(original.GetRawBody(-987654321012345678), UnityYamlDocument.Load(p.Resolve("Assets/Test.mat")).GetRawBody(-987654321012345678));
    }
    [Fact]
    public void FailedMaterialBatchDoesNotApplyEarlierValidPropertiesOrEraseRedo()
    {
        using var fixture = new TestProject(); MaterialEditSessionTests.AddMaterial(fixture); var catalog = new PreviewCatalog(ProjectSession.Open(fixture.Source)); var material = catalog.Material("Assets/Test.mat");
        material.SetNumbers("_AO", [.5f]); material.Undo(); Assert.Equal(1, material.RedoCount);
        Assert.Throws<ArgumentException>(() => material.ApplyBatch([new("_AO", "value", [.3f]), new("_Texture", "texture", TexturePath: "Assets/missing.png")]));
        Assert.False(material.IsDirty); Assert.Equal(0, material.UndoCount); Assert.Equal(1, material.RedoCount);
    }
}

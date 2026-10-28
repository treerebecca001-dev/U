using SceneWorkbench.Core;
using SceneWorkbench.Unity;

namespace SceneWorkbench.Tests;

public sealed class TerrainPreviewTests
{
    [Fact]
    public void NativeSInt16HeightGridUses32766NormalizationAndSampleSpacing()
    {
        short[] samples = [55, 519, 233, 55];
        var grid = NativeTerrainReader.Decode(2, samples, [.46875f, 600, .25f]);
        Assert.Equal(new float[] { .46875f, 600, .25f }, grid.Size);
        Assert.Equal((float)(55d / 32766 * 600), grid.Minimum, 5);
        Assert.Equal((float)(519d / 32766 * 600), grid.Maximum, 5);
        Assert.Equal(Hashing.Bytes([0x37, 0, 7, 2, 0xe9, 0, 0x37, 0]), grid.SamplesSha256);
        Assert.Equal(samples, grid.Samples);
    }

    [Fact]
    public void InvalidSampleDimensionsRangeAndScaleNeverGenerateReplacementTerrain()
    {
        Assert.Throws<InvalidDataException>(() => NativeTerrainReader.Decode(3, [0, 0, 0, 0], [1, 1, 1]));
        Assert.Throws<InvalidDataException>(() => NativeTerrainReader.Decode(2, [-1, 0, 0, 0], [1, 1, 1]));
        Assert.Throws<InvalidDataException>(() => NativeTerrainReader.Decode(2, [32767, 0, 0, 0], [1, 1, 1]));
        Assert.Throws<InvalidDataException>(() => NativeTerrainReader.Decode(2, [0, 0, 0, 0], [1, float.NaN, 1]));
        Assert.Throws<InvalidDataException>(() => NativeTerrainReader.Decode(2, [0, 0, 0, 0], [0, 1, 1]));
        Assert.Throws<InvalidDataException>(() => NativeTerrainReader.Decode(1026, [], [1, 1, 1]));
    }

    [Fact]
    public void MissingTerrainGuidRetainsExactNativeReferenceAndVisibilityWithoutFakeMesh()
    {
        using var fixture = new TestProject();
        var scene = TestProject.SceneText() + """
            --- !u!218 &21800000
            Terrain:
              m_GameObject: {fileID: 7}
              m_TerrainData: {fileID: 15600000, guid: 11111111111111111111111111111111, type: 2}
              m_Enabled: 0
              m_DrawHeightmap: 0
            """ + "\n";
        var catalog = new PreviewCatalog(ProjectSession.Open(fixture.Source));
        var graph = catalog.Scene("Assets/Scene.unity", System.Text.Encoding.UTF8.GetBytes(scene));
        var node = Assert.Single(graph.Nodes); Assert.Null(node.MeshPath); Assert.Equal("0", node.MeshFileId);
        Assert.NotNull(node.Terrain); Assert.Equal("15600000", node.Terrain.FileId); Assert.Null(node.Terrain.Path);
        Assert.False(node.Terrain.Enabled); Assert.False(node.Terrain.DrawHeightmap);
        Assert.Empty(graph.Terrains!); Assert.Contains(graph.Diagnostics, d => d.Contains("TerrainData GUID", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidTerrainIdentityAndCancellationNeverWriteNativeData()
    {
        using var fixture = new TestProject(); var project = ProjectSession.Open(fixture.Source);
        fixture.Write("Assets/NotTerrain.asset", "unchanged native fixture");
        var original = Hashing.File(project.Resolve("Assets/NotTerrain.asset"));
        Assert.Throws<ArgumentException>(() => NativeTerrainReader.Read(project, "Assets/NotTerrain.asset", "9223372036854775808", _ => null));
        Assert.Throws<ArgumentException>(() => NativeTerrainReader.Read(project, "Assets/Scene.unity", "15600000", _ => null));
        Assert.Throws<OperationCanceledException>(() => NativeTerrainReader.Read(project, "Assets/NotTerrain.asset", "15600000", _ => null, new CancellationToken(true)));
        Assert.Equal(original, Hashing.File(project.Resolve("Assets/NotTerrain.asset")));
    }
}

using SceneWorkbench.Unity;
using YamlDotNet.RepresentationModel;

namespace SceneWorkbench.Tests;

public sealed class FbxTransformPropertiesTests
{
    private const string Guid = "44c5a147808ad044eb7a1a0c8c716fde";
    private static string Change(string property, string value, string id = "-8679921383154817045") =>
        $"- target: {{fileID: {id}, guid: {Guid}, type: 3}}\n  propertyPath: {property}\n  value: {value}\n  objectReference: {{fileID: 0}}\n";
    private static FbxInstancePreview Read(string yaml)
    {
        var stream = new YamlStream(); stream.Load(new StringReader(yaml));
        var changes = (YamlSequenceNode)stream.Documents[0].RootNode;
        return FbxInstanceOverrides.Read(Guid, changes.Children, (_, _) => null, _ => { });
    }
    [Fact]
    public void PartialOverrideDistinguishesExplicitAxisFromFallbackZero()
    {
        var value = Read(Change("m_LocalPosition.y", "-0.3"));
        Assert.Equal("-8679921383154817045", value.TransformFileId);
        Assert.Equal(new[] { "m_LocalPosition.y" }, value.TransformProperties);
        Assert.Equal(new float[] { 0, -.3f, 0 }, value.Position);
    }
    [Fact]
    public void ExplicitZeroAndLastWriteRemainAnOverride()
    {
        var value = Read(Change("m_LocalPosition.x", "4") + Change("m_LocalPosition.x", "0") + Change("m_LocalScale.y", "NaN"));
        Assert.Equal(0, value.Position[0]);
        Assert.Equal(new[] { "m_LocalPosition.x" }, value.TransformProperties);
    }
    [Fact]
    public void AmbiguousTransformTargetsDoNotInventARootMapping()
    {
        var value = Read(Change("m_LocalPosition.x", "4", "1") + Change("m_LocalPosition.y", "3", "2"));
        Assert.Null(value.TransformFileId); Assert.Empty(value.TransformProperties!);
    }
    [Fact]
    public void InvalidQuaternionFallbackReplacesAllQuaternionComponents()
    {
        var value = Read(Change("m_LocalRotation.w", "0"));
        Assert.Equal(new float[] { 0, 0, 0, 1 }, value.Rotation);
        Assert.Equal(new[] { "m_LocalRotation.w", "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z" }, value.TransformProperties);
    }
}

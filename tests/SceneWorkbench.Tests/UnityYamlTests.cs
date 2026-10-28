using System.Numerics;
using System.Text;
using SceneWorkbench.Core;
using SceneWorkbench.Unity;

namespace SceneWorkbench.Tests;

public sealed class UnityYamlTests
{
    private const long TransformId = -2745811161023527127;

    [Theory]
    [InlineData("\n", false)]
    [InlineData("\r\n", true)]
    public void NoOpPreservesEveryByteIncludingBomAndUnknownData(string newline, bool bom)
    {
        var bytes = Encode(TestProject.SceneText(newline), bom);
        var document = UnityYamlDocument.Parse(bytes);
        Assert.Equal(bytes, document.ToBytes());
        Assert.False(document.IsModified);
        Assert.Equal(TransformId, document.Objects[1].FileId);
    }

    [Theory]
    [InlineData("\n", false)]
    [InlineData("\r\n", true)]
    public void PositionPatchChangesOnlyTheIntendedScalarAndReloads(string newline, bool bom)
    {
        var text = TestProject.SceneText(newline);
        var document = UnityYamlDocument.Parse(Encode(text, bom));
        document.SetPosition(TransformId, new Vector3(4.25f, -2.5f, 0));
        Assert.Equal(Encode(text.Replace("x: 1.0000", "x: 4.25", StringComparison.Ordinal), bom), document.ToBytes());
        Assert.Single(document.Changes);
        var reloaded = UnityYamlDocument.Parse(document.ToBytes());
        Assert.Equal(new Vector3(4.25f, -2.5f, 0), reloaded.GetPosition(TransformId));
        Assert.Equal(document.GetRawBody(99), reloaded.GetRawBody(99));
    }

    [Fact]
    public void UndoRestoresOriginalNumericSpellingAndRedoReapplies()
    {
        var original = Encode(TestProject.SceneText("\r\n"), true);
        var document = UnityYamlDocument.Parse(original);
        var history = new CommandHistory();
        history.Execute(new TransformPositionCommand(document, TransformId, new Vector3(20, 30, 40)));
        var changed = document.ToBytes();
        Assert.NotEqual(original, changed);
        history.Undo();
        Assert.Equal(original, document.ToBytes());
        Assert.False(document.IsModified);
        history.Redo();
        Assert.Equal(changed, document.ToBytes());
        Assert.Equal(1, history.UndoCount);
        Assert.Equal(0, history.RedoCount);
    }

    [Fact]
    public void StringPatchEscapesYamlSyntaxAndReloads()
    {
        var document = UnityYamlDocument.Parse(Encode(TestProject.SceneText(), false));
        const string name = "場景: # literal\n\"quoted\" 🌲";
        document.SetString(7, name, "GameObject", "m_Name");
        var loaded = UnityYamlDocument.Parse(document.ToBytes());
        Assert.Equal(name, loaded.GetScalar(7, "GameObject", "m_Name"));
        Assert.Equal(document.GetRawBody(99), loaded.GetRawBody(99));
    }

    [Fact]
    public void SameNumericValueIsANoOp()
    {
        var bytes = Encode(TestProject.SceneText(), false);
        var document = UnityYamlDocument.Parse(bytes);
        document.SetFloat(TransformId, 1, "Transform", "m_LocalPosition", "x");
        Assert.Equal(bytes, document.ToBytes());
        Assert.Empty(document.Changes);
    }

    [Fact]
    public void NonFinitePositionIsRejectedBeforeAnyFieldChanges()
    {
        var bytes = Encode(TestProject.SceneText(), false);
        var document = UnityYamlDocument.Parse(bytes);
        Assert.Throws<ArgumentOutOfRangeException>(() => document.SetPosition(TransformId, new Vector3(5, float.NaN, 9)));
        Assert.Equal(bytes, document.ToBytes());
    }

    [Fact]
    public void MalformedPositionCannotPartiallyModifyDocument()
    {
        var bytes = Encode(TestProject.SceneText().Replace("y: -2.5, ", "", StringComparison.Ordinal), false);
        var document = UnityYamlDocument.Parse(bytes);
        Assert.Throws<InvalidDataException>(() => document.SetPosition(TransformId, new Vector3(5, 6, 7)));
        Assert.Equal(bytes, document.ToBytes());
    }

    [Fact]
    public void StrippedPrefabTransformIsNotEditedAsAStandaloneTransform()
    {
        var text = TestProject.SceneText().Replace("--- !u!4 &-2745811161023527127", "--- !u!4 &-2745811161023527127 stripped", StringComparison.Ordinal);
        var document = UnityYamlDocument.Parse(Encode(text, false));
        Assert.Throws<InvalidOperationException>(() => document.GetPosition(TransformId));
    }

    [Fact]
    public void DuplicateFileIdAndInvalidUtf8AreRejected()
    {
        var text = TestProject.SceneText().Replace("--- !u!114 &99", "--- !u!114 &7", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => UnityYamlDocument.Parse(Encode(text, false)));
        Assert.Throws<DecoderFallbackException>(() => UnityYamlDocument.Parse([0xff, 0xfe, 0xfd]));
    }

    [Fact]
    public void UnityAssetIdPreservesSigned64BitAndNormalizesGuid()
    {
        var id = new UnityAssetId("ABCDEFABCDEFABCDEFABCDEFABCDEFAB", long.MinValue);
        Assert.Equal(long.MinValue, id.FileId);
        Assert.Equal("abcdefabcdefabcdefabcdefabcdefab:-9223372036854775808", id.ToString());
        Assert.Throws<ArgumentException>(() => new UnityAssetId("not-a-guid", 0));
    }

    private static byte[] Encode(string text, bool bom)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return bom ? new byte[] { 0xef, 0xbb, 0xbf }.Concat(bytes).ToArray() : bytes;
    }
}

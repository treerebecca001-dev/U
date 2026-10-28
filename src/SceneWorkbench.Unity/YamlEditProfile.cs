namespace SceneWorkbench.Unity;

/// <summary>Only byte-preserving scalar/reference edits; this does not certify Unity import or migration.</summary>
public static class YamlEditProfile
{
    public static bool Supports(UnityVersion editor) => editor.Channel is 'f' or 'p'
        && (editor is { Major: 2021, Minor: 3 } or { Major: 2022, Minor: 3 });

    public static bool SupportsTransform(UnityVersion editor, string? serializedVersion) => Supports(editor)
        && (serializedVersion is null || editor is { Major: 2022, Minor: 3 } && serializedVersion == "2");

    public static bool SupportsMaterial(UnityVersion editor, long serializedVersion) => Supports(editor)
        && serializedVersion is 6 or 7 or 8;

    internal static string? SerializedVersion(UnityYamlDocument document, long fileId)
    {
        var node = UnityData.At(UnityData.Body(document, fileId), "serializedVersion");
        return node is null ? null : UnityData.Text(node);
    }

    public static void RequireTransform(UnityVersion editor, UnityYamlDocument document, long fileId)
    {
        var version = SerializedVersion(document, fileId);
        if (!SupportsTransform(editor, version)) throw new InvalidOperationException("Transform 的版本結構尚未驗證，禁止回寫。");
    }
}

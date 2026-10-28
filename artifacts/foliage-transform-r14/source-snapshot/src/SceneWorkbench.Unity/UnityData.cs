using System.Globalization;
using YamlDotNet.RepresentationModel;

namespace SceneWorkbench.Unity;

public static class UnityData
{
    public static YamlNode? At(YamlNode? node, params string[] path)
    {
        foreach (var key in path)
        {
            if (node is YamlMappingNode map) node = map.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;
            else if (node is YamlSequenceNode list && int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                && index >= 0 && index < list.Children.Count) node = list.Children[index];
            else return null;
        }
        return node;
    }
    public static string Text(YamlNode? node, string fallback = "") => (node as YamlScalarNode)?.Value ?? fallback;
    public static float Float(YamlNode? node, float fallback = 0) => float.TryParse(Text(node), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value) ? value : fallback;
    public static long Long(YamlNode? node, long fallback = 0) => long.TryParse(Text(node), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    public static IEnumerable<YamlNode> List(YamlNode? node) => node is YamlSequenceNode sequence ? sequence.Children : [];
    public static YamlMappingNode Body(UnityYamlDocument doc, long id) => doc.ReadObject(id).Children.Values.OfType<YamlMappingNode>().Single();
    public static float[] Vector(YamlNode? node, string axes, float fallback = 0) => axes.Select(axis => Float(At(node, axis.ToString()), fallback)).ToArray();
}

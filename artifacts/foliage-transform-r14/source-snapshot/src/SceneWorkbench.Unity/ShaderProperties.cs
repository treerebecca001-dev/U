using System.Globalization;
using System.Text.RegularExpressions;

namespace SceneWorkbench.Unity;

public sealed record ShaderProperty(string Name, string Label, string Kind, float[] Default, float? Min, float? Max, bool Hidden, int? IntegerDefault = null);

public static class ShaderProperties
{
    private static readonly Regex Property = new(@"^\s*((?:\[[^\]\r\n]*\]\s*)*)([A-Za-z_][A-Za-z_0-9]*)\s*\(\s*""([^""]*)""\s*,\s*(Range\s*\([^)]*\)|2DArray|2D|Cube|Color|Vector|Float|Int|Integer)\s*\)\s*=\s*(.*)$", RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(3));
    public static IReadOnlyList<ShaderProperty> Read(string text)
    {
        var start = text.IndexOf("Properties", StringComparison.Ordinal);
        var end = text.IndexOf("SubShader", StringComparison.Ordinal);
        if (start < 0 || end < start) return [];
        var result = new List<ShaderProperty>();
        foreach (Match match in Property.Matches(text[start..end]))
        {
            var type = match.Groups[4].Value;
            var range = Regex.Matches(type, @"-?\d+(?:\.\d+)?").Select(m => float.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
            var value = match.Groups[5].Value.Trim();
            var numbers = Regex.Matches(value.Split('{')[0], @"[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?").Select(m => float.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
            var kind = type.StartsWith("Range", StringComparison.Ordinal) ? "Range" : type;
            result.Add(new(match.Groups[2].Value, match.Groups[3].Value, kind, numbers,
                range.Length == 2 ? range[0] : null, range.Length == 2 ? range[1] : null, match.Groups[1].Value.Contains("HideInInspector", StringComparison.Ordinal),
                kind == "Integer" && int.TryParse(value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) ? integer : null));
        }
        return result.GroupBy(p => p.Name, StringComparer.Ordinal).Where(g => g.Count() == 1).Select(g => g.Single()).ToArray();
    }
}

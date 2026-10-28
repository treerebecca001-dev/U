using System.Globalization;
using System.Text.RegularExpressions;
using SceneWorkbench.Core;

namespace SceneWorkbench.Unity;

public static class SceneInspector
{
    private static readonly Regex Pointer = new(@"\{fileID:\s*(-?\d+),\s*guid:\s*([0-9a-fA-F]{32}),\s*type:\s*\d+\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(30));

    public static SceneSnapshot Inspect(ProjectSession project, string relativePath, AssetIndex index)
    {
        var path = project.Resolve(relativePath);
        var bytes = File.ReadAllBytes(path);
        var document = UnityYamlDocument.Parse(bytes);
        var classes = document.Objects.GroupBy(item => item.ClassId).OrderBy(group => group.Key).ToDictionary(group => group.Key, group => group.Count());
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        var unresolved = new HashSet<string>(StringComparer.Ordinal);
        var fbx = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
        var count = 0;
        foreach (Match pointer in Pointer.Matches(text))
        {
            var guid = pointer.Groups[2].Value.ToLowerInvariant();
            if (guid.StartsWith("0000000000000000", StringComparison.Ordinal)) continue;
            count++;
            if (!index.ContainsGuid(guid)) unresolved.Add(guid);
            var asset = index.ResolveGuid(guid);
            if (asset is null || asset.Extension != ".fbx") continue;
            if (!fbx.TryGetValue(asset.Path, out var ids)) fbx.Add(asset.Path, ids = []);
            ids.Add(long.Parse(pointer.Groups[1].Value, CultureInfo.InvariantCulture));
        }
        var fbxReferences = new List<FbxReferenceSnapshot>();
        foreach (var (assetPath, ids) in fbx.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var meta = File.ReadAllText(project.Resolve(assetPath + ".meta"));
            var generation = Regex.Match(meta, @"^\s*fileIdsGeneration:\s*(\d+)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
            var empty = Regex.IsMatch(meta, @"^\s*internalIDToNameTable:\s*\[\]\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
            var table = Regex.IsMatch(meta, @"^\s*internalIDToNameTable:\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
            fbxReferences.Add(new FbxReferenceSnapshot(assetPath,
                generation.Success ? int.Parse(generation.Groups[1].Value, CultureInfo.InvariantCulture) : null,
                table && !empty, ids.Order().Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray()));
        }
        return new SceneSnapshot(relativePath, bytes.Length, Hashing.Bytes(bytes), classes,
            classes.GetValueOrDefault(1001), classes.GetValueOrDefault(4), document.Objects.Count(item => item.ClassId == 4 && item.IsStripped),
            count, unresolved.Count, unresolved.Order(StringComparer.Ordinal).Take(100).ToArray(), fbxReferences);
    }
}

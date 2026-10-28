using YamlDotNet.RepresentationModel;
using static SceneWorkbench.Unity.UnityData;

namespace SceneWorkbench.Unity;

internal sealed record PrefabOverrideTarget(long InstanceId, string Guid, long FileId, string Method);

/// <summary>Read-only routing inside one native Prefab scope. Never identifies an object by its name or order.</summary>
internal sealed class PrefabOverrideTargets
{
    private readonly Dictionary<long, List<PrefabOverrideTarget>> candidates = new();

    public PrefabOverrideTargets(UnityYamlDocument doc, Dictionary<long, YamlMappingNode> bodies,
        Func<string, string?> resolve, string editorVersion)
    {
        var instances = doc.Objects.Where(o => o.ClassId == 1001 && !o.IsStripped).ToDictionary(o => o.FileId);
        var stripped = doc.Objects.Where(o => o.IsStripped).ToArray();
        foreach (var item in stripped)
        {
            var body = bodies[item.FileId]; var instanceId = Long(At(body, "m_PrefabInstance", "fileID"));
            var source = At(body, "m_CorrespondingSourceObject"); var guid = Text(At(source, "guid")); var id = Long(At(source, "fileID"));
            if (id != 0 && instances.ContainsKey(instanceId) && guid.Length > 0 &&
                Text(At(bodies[instanceId], "m_SourcePrefab", "guid")) == guid)
                Add(item.FileId, new(instanceId, guid, id, "stripped-correspondence"));
        }

        // Imported nested objects can be referenced without a serialized stripped stub.
        // Enable this versioned rule only when *every* explicit anchor of that exact
        // instance proves it, and only for source IDs already declared by its FBX overrides.
        if (!editorVersion.StartsWith("2021.3.", StringComparison.Ordinal) && !editorVersion.StartsWith("2022.3.", StringComparison.Ordinal)) return;
        foreach (var instance in instances.Values)
        {
            var body = bodies[instance.FileId]; var guid = Text(At(body, "m_SourcePrefab", "guid"));
            if (Long(At(body, "serializedVersion")) != 2 ||
                !(resolve(guid)?.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) ?? false)) continue;
            var anchors = stripped.Where(o => Long(At(bodies[o.FileId], "m_PrefabInstance", "fileID")) == instance.FileId).ToArray();
            if (anchors.Length == 0 || anchors.Any(o =>
                Text(At(bodies[o.FileId], "m_CorrespondingSourceObject", "guid")) != guid ||
                Long(At(bodies[o.FileId], "m_CorrespondingSourceObject", "fileID")) == 0 ||
                NestedId(instance.FileId, Long(At(bodies[o.FileId], "m_CorrespondingSourceObject", "fileID"))) != o.FileId)) continue;
            var changes = List(At(body, "m_Modification", "m_Modifications"))
                .Where(c => Text(At(c, "target", "guid")) == guid).GroupBy(c => Long(At(c, "target", "fileID")));
            foreach (var group in changes.Where(g => g.Key != 0))
            {
                var properties = group.Select(c => Text(At(c, "propertyPath"))).ToArray();
                var transform = properties.Any(p => p.StartsWith("m_LocalPosition.", StringComparison.Ordinal) ||
                    p.StartsWith("m_LocalRotation.", StringComparison.Ordinal) || p.StartsWith("m_LocalScale.", StringComparison.Ordinal));
                var renderer = properties.Any(p => p.StartsWith("m_Materials.Array.", StringComparison.Ordinal));
                var gameObject = properties.Contains("m_Name", StringComparer.Ordinal);
                if ((transform ? 1 : 0) + (renderer ? 1 : 0) + (gameObject ? 1 : 0) != 1) continue;
                Add(NestedId(instance.FileId, group.Key), new(instance.FileId, guid, group.Key, "validated-nested-xor63"));
            }
        }
    }

    private static long NestedId(long instance, long source) => unchecked((long)(((ulong)instance ^ (ulong)source) & long.MaxValue));

    private void Add(long id, PrefabOverrideTarget target)
    {
        if (!candidates.TryGetValue(id, out var entries)) candidates[id] = entries = [];
        if (!entries.Any(e => e.InstanceId == target.InstanceId && e.Guid == target.Guid && e.FileId == target.FileId)) entries.Add(target);
    }

    public PrefabOverrideTarget? Resolve(long id, out bool ambiguous)
    {
        var entries = candidates.GetValueOrDefault(id); ambiguous = entries?.Count > 1;
        return entries?.Count == 1 ? entries[0] : null;
    }

    public static YamlNode Retarget(YamlNode change, PrefabOverrideTarget target) => new YamlMappingNode
    {
        { "target", new YamlMappingNode { { "fileID", new YamlScalarNode(target.FileId.ToString(System.Globalization.CultureInfo.InvariantCulture)) },
            { "guid", new YamlScalarNode(target.Guid) }, { "type", new YamlScalarNode("3") } } },
        { "propertyPath", At(change, "propertyPath") ?? new YamlScalarNode("") },
        { "value", At(change, "value") ?? new YamlScalarNode("") },
        { "objectReference", At(change, "objectReference") ?? new YamlMappingNode() }
    };
}

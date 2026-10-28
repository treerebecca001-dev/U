using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;
using static SceneWorkbench.Unity.UnityData;

namespace SceneWorkbench.Unity;

public sealed record PreviewMaterialReference(string Guid, string FileId, string? Path);
public sealed record FbxRendererOverride(string FileId, PreviewMaterialReference[] Slots, bool Enabled, int CastShadows = 1, bool ReceiveShadows = true, int ReflectionProbeUsage = 1, uint RenderingLayerMask = 1);
public sealed record FbxInstancePreview(string SourceGuid, FbxRendererOverride[] Renderers, string? TransformFileId,
    float[] Position, float[] Rotation, float[] Scale, bool Active, string Status, int Layer = 0);

/// <summary>Reads explicit native bindings only. The browser must additionally verify a unique source renderer.</summary>
public static class FbxInstanceOverrides
{
    public static FbxInstancePreview Read(string sourceGuid, IEnumerable<YamlNode> changes,
        Func<string, string, string?> resolveMaterial, Action<string> warn)
    {
        var relevant = changes.Where(c => Text(At(c, "target", "guid")) == sourceGuid).ToArray();
        var rendererIds = relevant.Where(c => Text(At(c, "propertyPath")).StartsWith("m_Materials.", StringComparison.Ordinal))
            .Select(c => Text(At(c, "target", "fileID"))).ToHashSet(StringComparer.Ordinal);
        var rendererGroups = relevant.Where(c => rendererIds.Contains(Text(At(c, "target", "fileID"))))
            .GroupBy(c => Text(At(c, "target", "fileID")), StringComparer.Ordinal);
        var renderers = new List<FbxRendererOverride>();
        foreach (var renderer in rendererGroups)
        {
            if (!long.TryParse(renderer.Key, CultureInfo.InvariantCulture, out var rendererId) || rendererId == 0)
            { warn("FBX Renderer fileID 無效，未套用覆寫。"); continue; }
            var slots = new SortedDictionary<int, PreviewMaterialReference>(); var enabled = true; var castShadows = 1; var receiveShadows = true; var reflectionProbeUsage = 1; uint renderingLayerMask = 1;
            foreach (var change in renderer)
            {
                var property = Text(At(change, "propertyPath"));
                if (property == "m_Enabled") { enabled = Text(At(change, "value")) != "0"; continue; }
                if (property == "m_ReceiveShadows") { receiveShadows = Text(At(change, "value")) != "0"; continue; }
                if (property == "m_CastShadows" && int.TryParse(Text(At(change, "value")), out var casting) && casting is >= 0 and <= 3) { castShadows = casting; continue; }
                if (property == "m_ReflectionProbeUsage" && int.TryParse(Text(At(change, "value")), out var probeUsage) && probeUsage is >= 0 and <= 3) { reflectionProbeUsage = probeUsage; continue; }
                if (property == "m_RenderingLayerMask" && uint.TryParse(Text(At(change, "value")), out var mask)) { renderingLayerMask = mask; continue; }
                var match = Regex.Match(property, @"\Am_Materials\.Array\.data\[(\d+)\]\z");
                if (!match.Success) continue;
                if (!int.TryParse(match.Groups[1].Value, out var index) || index > 63)
                { warn("FBX 材質槽超過預覽範圍，未猜測綁定。"); continue; }
                var reference = At(change, "objectReference"); var guid = Text(At(reference, "guid")); var fileId = Text(At(reference, "fileID"));
                var path = long.TryParse(fileId, CultureInfo.InvariantCulture, out var parsed) && parsed != 0 ? resolveMaterial(guid, fileId) : null;
                if (path is null) warn($"FBX 原生材質參照未解析：{guid}/{fileId}。");
                slots[index] = new(guid, fileId, path);
            }
            var values = Enumerable.Range(0, slots.Count == 0 ? 0 : slots.Keys.Max() + 1)
                .Select(i => slots.GetValueOrDefault(i) ?? new PreviewMaterialReference("", "0", null)).ToArray();
            renderers.Add(new(rendererId.ToString(CultureInfo.InvariantCulture), values, enabled, castShadows, receiveShadows, reflectionProbeUsage, renderingLayerMask));
        }
        var transforms = relevant.Where(c => Text(At(c, "propertyPath")).StartsWith("m_Local", StringComparison.Ordinal))
            .GroupBy(c => Text(At(c, "target", "fileID")), StringComparer.Ordinal).ToArray();
        float[] position = [0, 0, 0], rotation = [0, 0, 0, 1], scale = [1, 1, 1]; string? transformId = null;
        if (transforms.Length == 1 && long.TryParse(transforms[0].Key, CultureInfo.InvariantCulture, out var id) && id != 0)
        {
            transformId = id.ToString(CultureInfo.InvariantCulture);
            foreach (var change in transforms[0])
            {
                var p = Text(At(change, "propertyPath")); var parts = p.Split('.');
                if (parts.Length != 2 || parts[1].Length != 1 || !float.TryParse(Text(At(change, "value")), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value)) continue;
                var axis = "xyzw".IndexOf(parts[1][0]); var vector = parts[0] == "m_LocalPosition" ? position : parts[0] == "m_LocalRotation" ? rotation : parts[0] == "m_LocalScale" ? scale : null;
                if (vector is not null && axis >= 0 && axis < vector.Length) vector[axis] = value;
            }
            if (rotation.All(v => v == 0)) { rotation = [0, 0, 0, 1]; warn("FBX Quaternion 無效，預覽保留單位旋轉。"); }
        }
        else if (transforms.Length > 1) warn("FBX 多個 Transform 覆寫無法唯一映射，保留來源階層。");
        var activeChanges = relevant.Where(c => Text(At(c, "propertyPath")) == "m_IsActive").ToArray();
        var active = activeChanges.Select(c => Text(At(c, "target", "fileID"))).Distinct().Count() <= 1
            && (activeChanges.LastOrDefault() is not { } activeChange || Text(At(activeChange, "value")) != "0");
        if (activeChanges.Select(c => Text(At(c, "target", "fileID"))).Distinct().Count() > 1)
        { active = true; warn("FBX 多個 GameObject 啟用覆寫尚未映射，保留來源狀態。"); }
        var layerChanges = relevant.Where(c => Text(At(c, "propertyPath")) == "m_Layer").ToArray(); var layer = 0;
        if (layerChanges.Select(c => Text(At(c, "target", "fileID"))).Distinct().Count() <= 1)
        { if (int.TryParse(Text(At(layerChanges.LastOrDefault(), "value")), out var sourceLayer) && sourceLayer is >= 0 and <= 31) layer = sourceLayer; }
        else warn("FBX 多個 GameObject Layer 覆寫無法唯一映射，保留匯入預設 Layer。");
        return new(sourceGuid, renderers.ToArray(), transformId, position, rotation, scale, active,
            "原生 GUID/fileID/材質槽覆寫；只在來源 FBX 有唯一 Mesh/Renderer 時套用，其他情況保留診斷。Renderer 未覆寫旗標採此版本匯入預設。", layer);
    }
}

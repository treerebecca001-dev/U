using System.Globalization;
using System.Numerics;
using System.Text.Json;
using SceneWorkbench.Unity;

namespace SceneWorkbench.Web;

public sealed record RevisionRequest(long Revision);
public sealed record OpenProjectRequest(long Revision, string Path, bool Discard = false, string Mode = "inspect", string? Destination = null);
public sealed record OpenSceneRequest(long Revision, string Path, bool Discard = false);
public sealed record CopyRequest(long Revision, string Destination, bool Discard = false);
public sealed record FieldEditRequest(long Revision, string Id, string Field, JsonElement Value);
public sealed record OpenMaterialRequest(long Revision, string Path, bool Discard = false);
public sealed record MaterialEditRequest(long Revision, string Name, string Part, JsonElement Value);
public sealed record MaterialBatchRequest(long Revision, MaterialEditRequest[] Edits);
public sealed record CloseMaterialRequest(long Revision, bool Discard = false);
public sealed record AssetRow(string Path, string Name, string Kind, bool IsDirectory, long Bytes);
public sealed record WebSceneItem(string Id, string GameObjectId, string? TransformId, string ParentId,
    string Name, float[] Position, float[] Rotation, float[] Scale, bool Active, int Layer, uint StaticFlags,
    bool IsPrefab, bool Editable, string[] EditableFields, IReadOnlyList<string> Components,
    string? MeshGuid, string MeshFileId, float[]? ColliderSize, float[]? ColliderCenter)
{
    public static WebSceneItem From(SceneEditSession session, SceneItem item, bool writable)
    {
        var editable = writable && session.CanEditItem(item);
        var fields = new List<string>();
        if (editable)
        {
            fields.AddRange(["name", "position", "rotation", "scale"]);
            foreach (var pair in new[] { ("active", "m_IsActive"), ("layer", "m_Layer"), ("staticFlags", "m_StaticEditorFlags") })
                if (session.HasField(item.GameObjectId, "GameObject", pair.Item2)) fields.Add(pair.Item1);
        }
        return new(item.Id.ToString(CultureInfo.InvariantCulture), item.GameObjectId.ToString(CultureInfo.InvariantCulture),
            item.TransformId?.ToString(CultureInfo.InvariantCulture), item.ParentId.ToString(CultureInfo.InvariantCulture), item.Name,
            V(item.Position), [Finite(item.Rotation.X), Finite(item.Rotation.Y), Finite(item.Rotation.Z), Finite(item.Rotation.W)], V(item.Scale), item.Active, item.Layer,
            item.StaticFlags, item.IsPrefab, editable, fields.ToArray(), item.Components, item.MeshGuid,
            item.MeshFileId.ToString(CultureInfo.InvariantCulture), item.ColliderSize is { } size ? V(size) : null, item.ColliderCenter is { } center ? V(center) : null);
    }
    private static float Finite(float value) => float.IsFinite(value) ? value : 0;
    private static float[] V(Vector3 v) => [Finite(v.X), Finite(v.Y), Finite(v.Z)];
}
public sealed class StaleSessionException() : InvalidOperationException("另一個分頁或操作已變更文件，請先同步目前狀態；你的輸入仍保留。" );

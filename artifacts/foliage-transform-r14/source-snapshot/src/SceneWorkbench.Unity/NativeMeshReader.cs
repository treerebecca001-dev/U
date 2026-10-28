using System.Buffers.Binary;
using SceneWorkbench.Core;
using static SceneWorkbench.Unity.UnityData;

namespace SceneWorkbench.Unity;

public sealed record MeshGroup(int Start, int Count, int MaterialIndex);
public sealed record NativeMeshData(string Name, int VertexCount, Dictionary<string, float[]> Attributes, int[] Indices, MeshGroup[] Groups);

public static class NativeMeshReader
{
    public static NativeMeshData Read(ProjectSession project, string relativePath, long fileId)
    {
        var path = project.Resolve(relativePath);
        if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new InvalidDataException("Mesh 超過 64 MB 預覽限制。");
        var doc = UnityYamlDocument.Load(path);
        var obj = doc.GetObject(fileId);
        if (obj.ClassId != 43 || obj.IsStripped) throw new InvalidDataException("fileID 不是完整原生 Mesh。");
        var body = Body(doc, fileId);
        if (Long(At(body, "serializedVersion")) is not 9 and not 10) throw new InvalidDataException("Mesh 版本未驗證，保持唯讀。");
        var vertex = At(body, "m_VertexData");
        var count = checked((int)Long(At(vertex, "m_VertexCount")));
        if (count < 1 || count > 2_000_000) throw new InvalidDataException("Mesh 頂點數超出預覽範圍。");
        var data = Convert.FromHexString(Text(At(vertex, "_typelessdata")));
        if (data.Length != Long(At(vertex, "m_DataSize"))) throw new InvalidDataException("Mesh 頂點資料長度不符。");
        var channels = List(At(vertex, "m_Channels")).Select(node => new Channel(checked((int)Long(At(node, "stream"))), checked((int)Long(At(node, "offset"))), checked((int)Long(At(node, "format"))), checked((int)Long(At(node, "dimension"))) & 15)).ToArray();
        var streams = new Dictionary<int, (int Start, int Stride)>(); var streamStart = 0;
        foreach (var group in channels.Where(c => c.Dimension > 0).GroupBy(c => c.Stream).OrderBy(g => g.Key))
        {
            if (group.Key < 0 || group.Key > 15 || group.Any(c => c.Offset < 0 || c.Dimension > 4)) throw new InvalidDataException("Mesh 通道配置無效。");
            var stride = group.Max(c => checked(c.Offset + Size(c.Format) * c.Dimension));
            streams.Add(group.Key, (streamStart, stride));
            streamStart = checked((streamStart + count * stride + 15) & ~15);
        }
        var names = new[] { "position", "normal", "tangent", "color", "uv", "uv1", "uv2", "uv3", "uv4", "uv5", "uv6", "uv7" };
        var attributes = new Dictionary<string, float[]>();
        for (var i = 0; i < Math.Min(names.Length, channels.Length); i++)
        {
            var channel = channels[i]; if (channel.Dimension == 0) continue;
            var stream = streams[channel.Stream]; var values = new float[checked(count * channel.Dimension)];
            var size = Size(channel.Format);
            for (var v = 0; v < count; v++) for (var axis = 0; axis < channel.Dimension; axis++)
            {
                var offset = checked(stream.Start + v * stream.Stride + channel.Offset + axis * size);
                if (offset < 0 || offset + size > data.Length) throw new InvalidDataException("Mesh 頂點通道超出資料範圍。");
                var value = Number(data.AsSpan(offset, size), channel.Format);
                if (!float.IsFinite(value)) throw new InvalidDataException("Mesh 含無效頂點數值。");
                values[v * channel.Dimension + axis] = value;
            }
            attributes.Add(names[i], values);
        }
        if (!attributes.TryGetValue("position", out var positions) || positions.Length != count * 3) throw new InvalidDataException("Mesh 缺少三維頂點。");
        var buffer = Convert.FromHexString(Text(At(body, "m_IndexBuffer")));
        var indexSize = Long(At(body, "m_IndexFormat")) switch { 0 => 2, 1 => 4, _ => throw new InvalidDataException("未知 Mesh 索引格式。") };
        var indices = new List<int>(); var groups = new List<MeshGroup>(); var slot = 0;
        foreach (var mesh in List(At(body, "m_SubMeshes")))
        {
            if (Long(At(mesh, "topology")) != 0) throw new InvalidDataException("此 Mesh 子網格不是三角形，保持標示未支援。");
            var first = Long(At(mesh, "firstByte")); var length = Long(At(mesh, "indexCount")); var baseVertex = Long(At(mesh, "baseVertex"));
            if (first < 0 || length < 0 || length > 6_000_000 || length % 3 != 0 || first + length * indexSize > buffer.Length) throw new InvalidDataException("Mesh 子網格索引範圍無效。");
            groups.Add(new(indices.Count, (int)length, slot++));
            for (var j = 0; j < length; j++)
            {
                var span = buffer.AsSpan(checked((int)(first + j * indexSize)), indexSize);
                var index = (indexSize == 2 ? BinaryPrimitives.ReadUInt16LittleEndian(span) : BinaryPrimitives.ReadUInt32LittleEndian(span)) + baseVertex;
                if (index < 0 || index >= count) throw new InvalidDataException("Mesh 索引超出頂點範圍。");
                indices.Add((int)index);
            }
        }
        return new(Text(At(body, "m_Name")), count, attributes, indices.ToArray(), groups.ToArray());
    }
    private sealed record Channel(int Stream, int Offset, int Format, int Dimension);
    private static int Size(int format) => format switch { 0 or 10 or 11 => 4, 1 or 4 or 5 or 8 or 9 => 2, 2 or 3 or 6 or 7 => 1, _ => throw new InvalidDataException("未知 Mesh 頂點格式。") };
    private static float Number(ReadOnlySpan<byte> data, int format) => format switch
    {
        0 => BinaryPrimitives.ReadSingleLittleEndian(data), 1 => (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(data)),
        2 => data[0] / 255f, 3 => Math.Max(-1, (sbyte)data[0] / 127f), 4 => BinaryPrimitives.ReadUInt16LittleEndian(data) / 65535f,
        5 => Math.Max(-1, BinaryPrimitives.ReadInt16LittleEndian(data) / 32767f), 6 => data[0], 7 => (sbyte)data[0],
        8 => BinaryPrimitives.ReadUInt16LittleEndian(data), 9 => BinaryPrimitives.ReadInt16LittleEndian(data),
        10 => BinaryPrimitives.ReadUInt32LittleEndian(data), 11 => BinaryPrimitives.ReadInt32LittleEndian(data), _ => throw new InvalidDataException("未知 Mesh 頂點格式。")
    };
}

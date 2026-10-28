using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SceneWorkbench.Core;
using YamlDotNet.RepresentationModel;

namespace SceneWorkbench.Unity;

public sealed record UnityYamlObject(int ClassId, long FileId, bool IsStripped, int BodyStart, int BodyEnd);
public sealed record ScalarChange(long FileId, string Property, string Before, string After);

public sealed class UnityYamlDocument
{
    private static readonly Regex Header = new(@"^--- !u!(\d+) &(-?\d+)( stripped)?\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(30));
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly byte[] original;
    private readonly string text;
    private readonly bool bom;
    private readonly Dictionary<long, UnityYamlObject> byId;
    private readonly Dictionary<long, YamlMappingNode> parsed = new();
    private readonly Dictionary<int, ScalarPatch> changes = new();

    public IReadOnlyList<UnityYamlObject> Objects { get; }
    public string OriginalSha256 { get; }
    public bool IsModified => changes.Count != 0;
    public IReadOnlyList<ScalarChange> Changes => changes.Values.OrderBy(change => change.Start)
        .Select(change => new ScalarChange(change.FileId, change.Property, change.Before, change.Value)).ToArray();

    private UnityYamlDocument(byte[] bytes)
    {
        original = bytes.ToArray();
        OriginalSha256 = Hashing.Bytes(bytes);
        bom = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf });
        text = Utf8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        if (!text.StartsWith("%YAML 1.1", StringComparison.Ordinal))
            throw new InvalidDataException("不是支援的 Unity UTF-8 YAML 文件；保持唯讀。");
        var headers = Header.Matches(text).Cast<Match>().ToArray();
        if (headers.Length == 0)
            throw new InvalidDataException("Unity YAML 文件沒有物件標頭。");
        var objects = new List<UnityYamlObject>(headers.Length);
        byId = new Dictionary<long, UnityYamlObject>();
        for (var index = 0; index < headers.Length; index++)
        {
            var header = headers[index];
            var bodyStart = header.Index + header.Length;
            if (bodyStart < text.Length && text[bodyStart] == '\n')
                bodyStart++;
            var item = new UnityYamlObject(
                int.Parse(header.Groups[1].Value, CultureInfo.InvariantCulture),
                long.Parse(header.Groups[2].Value, CultureInfo.InvariantCulture),
                header.Groups[3].Success, bodyStart,
                index + 1 == headers.Length ? text.Length : headers[index + 1].Index);
            if (!byId.TryAdd(item.FileId, item))
                throw new InvalidDataException($"Unity YAML 有重複 fileID：{item.FileId}");
            objects.Add(item);
        }
        Objects = objects;
    }

    public static UnityYamlDocument Load(string path) => Parse(File.ReadAllBytes(path));
    public static UnityYamlDocument Parse(byte[] bytes) => new(bytes);

    public UnityYamlObject GetObject(long fileId) => byId.TryGetValue(fileId, out var item)
        ? item : throw new KeyNotFoundException($"找不到 fileID {fileId}。");

    public string GetRawBody(long fileId)
    {
        var item = GetObject(fileId);
        return text[item.BodyStart..item.BodyEnd];
    }

    public YamlMappingNode ReadObject(long fileId)
    {
        if (parsed.TryGetValue(fileId, out var existing))
            return existing;
        var stream = new YamlStream();
        stream.Load(new StringReader(GetRawBody(fileId)));
        if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode mapping)
            throw new InvalidDataException("只支援單一 Unity 物件映射。");
        parsed.Add(fileId, mapping);
        return mapping;
    }

    public string GetScalar(long fileId, params string[] path)
    {
        var (scalar, start, _) = FindScalar(fileId, path);
        return changes.TryGetValue(start, out var change) ? change.Value : scalar.Value ?? "";
    }

    public bool SetFloat(long fileId, float value, params string[] path)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "數值必須有限。");
        var (scalar, start, end) = FindScalar(fileId, path);
        var originalValue = ParseFloat(scalar.Value);
        if (value == ParseFloat(GetScalar(fileId, path)))
            return false;
        if (value == originalValue)
            changes.Remove(start);
        else
        {
            var formatted = value.ToString("R", CultureInfo.InvariantCulture);
            changes[start] = new ScalarPatch(start, end, fileId, string.Join('.', path), scalar.Value ?? "", formatted, formatted);
        }
        return true;
    }

    public bool SetInteger(long fileId, long value, params string[] path)
    {
        var (scalar, start, end) = FindScalar(fileId, path);
        var formatted = value.ToString(CultureInfo.InvariantCulture);
        if (GetScalar(fileId, path) == formatted)
            return false;
        if (long.Parse(scalar.Value ?? "", CultureInfo.InvariantCulture) == value)
            changes.Remove(start);
        else
            changes[start] = new ScalarPatch(start, end, fileId, string.Join('.', path), scalar.Value ?? "", formatted, formatted);
        return true;
    }

    public bool SetString(long fileId, string value, params string[] path)
    {
        var (scalar, start, end) = FindScalar(fileId, path);
        if (GetScalar(fileId, path) == value)
            return false;
        if (scalar.Value == value)
            changes.Remove(start);
        else
            changes[start] = new ScalarPatch(start, end, fileId, string.Join('.', path), scalar.Value ?? "", value, JsonSerializer.Serialize(value));
        return true;
    }

    public Vector3 GetPosition(long fileId)
    {
        RequireTransform(fileId);
        return new Vector3(ParseFloat(GetScalar(fileId, "Transform", "m_LocalPosition", "x")),
            ParseFloat(GetScalar(fileId, "Transform", "m_LocalPosition", "y")),
            ParseFloat(GetScalar(fileId, "Transform", "m_LocalPosition", "z")));
    }

    public void SetPosition(long fileId, Vector3 position)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
            throw new ArgumentOutOfRangeException(nameof(position), "座標必須有限。");
        _ = GetPosition(fileId); // Validate all fields before modifying any component.
        SetFloat(fileId, position.X, "Transform", "m_LocalPosition", "x");
        SetFloat(fileId, position.Y, "Transform", "m_LocalPosition", "y");
        SetFloat(fileId, position.Z, "Transform", "m_LocalPosition", "z");
    }

    public byte[] ToBytes()
    {
        if (!IsModified)
            return original.ToArray();
        var builder = new StringBuilder(text);
        foreach (var patch in changes.Values.OrderByDescending(change => change.Start))
        {
            builder.Remove(patch.Start, patch.End - patch.Start);
            builder.Insert(patch.Start, patch.Raw);
        }
        var body = Utf8.GetBytes(builder.ToString());
        return bom ? new byte[] { 0xef, 0xbb, 0xbf }.Concat(body).ToArray() : body;
    }

    private (YamlScalarNode Scalar, int Start, int End) FindScalar(long fileId, IReadOnlyList<string> path)
    {
        var (current, start, end) = FindNode(fileId, path);
        if (current is not YamlScalarNode scalar)
            throw new InvalidDataException("只允許局部修改 scalar 欄位。");
        return (scalar, start, end);
    }

    // Sequence indices are explicit path segments, used by Unity's saved material properties.
    // Patch the source span rather than serializing neighbouring YAML or unknown fields.
    private (YamlNode Node, int Start, int End) FindNode(long fileId, IReadOnlyList<string> path)
    {
        if (path.Count == 0)
            throw new ArgumentException("欄位路徑不可為空。", nameof(path));
        YamlNode current = ReadObject(fileId);
        foreach (var segment in path)
        {
            if (current is YamlSequenceNode sequence && int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                && index >= 0 && index < sequence.Children.Count)
            { current = sequence.Children[index]; continue; }
            if (current is not YamlMappingNode mapping)
                throw new InvalidDataException("欄位不是映射或有效的明確陣列索引。");
            var matches = mapping.Children.Where(pair => pair.Key is YamlScalarNode key && key.Value == segment).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException($"欄位不存在或不唯一：{string.Join('.', path)}");
            current = matches[0].Value;
        }
        var item = GetObject(fileId);
        var start = checked(item.BodyStart + (int)current.Start.Index);
        var end = checked(item.BodyStart + (int)current.End.Index);
        if (current is YamlScalarNode && end <= start)
            throw new InvalidDataException("空值欄位無法安全局部修改。");
        return (current, start, end);
    }

    public bool SetAssetReference(long fileId, UnityAssetId reference, params string[] path)
    {
        var (node, start, end) = FindNode(fileId, path);
        if (node is not YamlMappingNode mapping || !mapping.Children.ContainsKey(new YamlScalarNode("fileID")))
            throw new InvalidDataException("不是原生 Unity 資源參照。");
        // RepresentationModel does not retain mapping end marks. Locate this specific flow
        // reference using the retained fileID scalar mark, then validate its entire grammar.
        var scalar = mapping.Children[new YamlScalarNode("fileID")] as YamlScalarNode ?? throw new InvalidDataException("資源 fileID 無效。");
        var valueStart = checked(GetObject(fileId).BodyStart + (int)scalar.Start.Index);
        start = text.LastIndexOf('{', valueStart); end = text.IndexOf('}', valueStart) + 1;
        if (start < GetObject(fileId).BodyStart || end <= start || end > GetObject(fileId).BodyEnd
            || !Regex.IsMatch(text[start..end], @"\A\{\s*fileID:\s*-?\d+(?:,\s*guid:\s*[0-9a-fA-F]{32},\s*type:\s*\d+)?\s*\}\z", RegexOptions.CultureInvariant))
            throw new InvalidDataException("只允許已驗證的 flow 資源參照；未知欄位保持唯讀。");
        var raw = reference.FileId == 0 ? "{fileID: 0}" : $"{{fileID: {reference.FileId.ToString(CultureInfo.InvariantCulture)}, guid: {reference.Guid}, type: 3}}";
        var originalRaw = text[start..end];
        var currentRaw = changes.TryGetValue(start, out var change) ? change.Raw : originalRaw;
        if (currentRaw == raw) return false;
        // Semantic equality also restores exact original whitespace / reference type on Undo.
        var originalId = long.Parse(((YamlScalarNode)mapping.Children[new YamlScalarNode("fileID")]).Value!, CultureInfo.InvariantCulture);
        var originalGuid = mapping.Children.TryGetValue(new YamlScalarNode("guid"), out var guid) ? ((YamlScalarNode)guid).Value : "";
        if (reference.FileId == originalId && (reference.FileId == 0 || reference.Guid == originalGuid)) changes.Remove(start);
        else changes[start] = new ScalarPatch(start, end, fileId, string.Join('.', path), originalRaw, raw, raw);
        return true;
    }

    private void RequireTransform(long fileId)
    {
        var item = GetObject(fileId);
        if (item.ClassId != 4 || item.IsStripped)
            throw new InvalidOperationException("此操作只支援完整 Transform；Prefab 覆寫與 stripped 物件需要專用處理。");
    }

    private static float ParseFloat(string? value)
    {
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) || !float.IsFinite(result))
            throw new InvalidDataException("不是可安全編輯的有限浮點數。");
        return result;
    }

    private sealed record ScalarPatch(int Start, int End, long FileId, string Property, string Before, string Value, string Raw);
}

public sealed class TransformPositionCommand(UnityYamlDocument document, long fileId, Vector3 position) : IEditCommand
{
    private readonly Vector3 before = document.GetPosition(fileId);
    public string Description => "移動物件";
    public void Execute() => document.SetPosition(fileId, position);
    public void Undo() => document.SetPosition(fileId, before);
}

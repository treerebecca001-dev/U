using System.Buffers.Binary;
using System.Globalization;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using SceneWorkbench.Core;
using static SceneWorkbench.Unity.UnityData;

namespace SceneWorkbench.Unity;

public sealed record PreviewTerrainReference(string? Path, string Guid, string FileId, bool Enabled, bool DrawHeightmap);
public sealed record TerrainLayerPreview(string Guid, string FileId, string? Path, string? DiffusePath,
    string DiffuseGuid, string DiffuseFileId, float[] TileSize, float[] TileOffset);
public sealed record TerrainControlPreview(string FileId, int Width, int Height, byte[] Rgba);
public sealed record TerrainHeightGrid(int Resolution, short[] Samples, float[] Size, float Minimum, float Maximum, string SamplesSha256);
public sealed record NativeTerrainPreview(string Path, string FileId, string Sha256, string UnityVersion, TerrainHeightGrid Heightmap,
    int HolesSamples, int HolesLodSamples, TerrainLayerPreview[] Layers, TerrainControlPreview[] Controls, string[] Diagnostics);

/// <summary>Read-only, type-tree-based terrain preview. Never writes TerrainData or synthesizes missing heights.</summary>
public static class NativeTerrainReader
{
    // Unity 2021.3 PaintContext.kNormalizedHeightScale = 32766 / 65535.
    // The serialized SInt16 samples contain the corresponding integer texel value.
    public const float HeightDivisor = 32766f;
    public static TerrainHeightGrid Decode(int resolution, short[] samples, float[] scale)
    {
        if (resolution < 2 || resolution > 1025 || samples.Length != checked(resolution * resolution))
            throw new InvalidDataException("Terrain 高度解析度／樣本數無效，或超過本輪 1025 預覽上限。");
        if (scale.Length != 3 || scale.Any(v => !float.IsFinite(v) || v <= 0) || samples.Any(v => v < 0 || v > HeightDivisor))
            throw new InvalidDataException("Terrain 高度或尺寸超出已驗證的 SInt16 格式，未產生替代地形。");
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2, 2), samples[i]);
        var size = new[] { scale[0] * (resolution - 1), scale[1], scale[2] * (resolution - 1) };
        if (size.Any(v => !float.IsFinite(v))) throw new InvalidDataException("Terrain 尺寸超出有限值。");
        return new(resolution, samples, size, samples.Min() / HeightDivisor * scale[1], samples.Max() / HeightDivisor * scale[1], Hashing.Bytes(bytes));
    }

    public static NativeTerrainPreview Read(ProjectSession project, string path, string fileId, Func<string, string?> resolveGuid, CancellationToken ct = default)
    {
        if (!long.TryParse(fileId, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id) || id == 0 || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("請指定原生 TerrainData .asset 與精確 fileID。");
        var absolute = project.Resolve(path); SafePath.RejectLinks(absolute);
        if (new FileInfo(absolute).Length > 32 * 1024 * 1024) throw new InvalidDataException("TerrainData 超過本輪 32 MB 唯讀預覽上限。");
        ct.ThrowIfCancellationRequested(); var hash = Hashing.File(absolute); var manager = new AssetsManager();
        try
        {
            var instance = manager.LoadAssetsFile(absolute, false); var asset = instance.file;
            var version = UnityVersion.Parse(asset.Metadata.UnityVersion);
            if (!asset.Metadata.TypeTreeEnabled || version.Major is not (2021 or 2022) || version.Minor != 3)
                throw new InvalidDataException("TerrainData 缺少 type tree，或不是已驗證的 2021.3／2022.3 資料；保持唯讀診斷。");
            var info = asset.AssetInfos.SingleOrDefault(o => o.PathId == id && o.TypeId == 156)
                ?? throw new InvalidDataException("TerrainData fileID／型別不符，未猜測其他物件。");
            var field = manager.GetBaseField(instance, info); ct.ThrowIfCancellationRequested();
            var heights = Required(field, "m_Heightmap");
            var resolution = Required(heights, "m_Resolution").AsInt;
            var sourceSamples = Array(Required(heights, "m_Heights"));
            if (sourceSamples.Count > 1025 * 1025 || sourceSamples.Any(s => s.TypeName != "SInt16")) throw new InvalidDataException("未知 Terrain 高度樣本格式。");
            var samples = new short[sourceSamples.Count];
            for (var i = 0; i < samples.Length; i++) { if ((i & 8191) == 0) ct.ThrowIfCancellationRequested(); samples[i] = sourceSamples[i].AsShort; }
            var scale = Required(heights, "m_Scale");
            var grid = Decode(resolution, samples, new[] { Required(scale, "x").AsFloat, Required(scale, "y").AsFloat, Required(scale, "z").AsFloat });
            var holes = Array(Required(heights, "m_Holes")).Count; var holesLod = Array(Required(heights, "m_HolesLOD")).Count;
            var diagnostics = new List<string>();
            if (holes != 0 || holesLod != 0) diagnostics.Add("Terrain 含有尚未解碼的 holes 遮罩；保留高度資訊，暫不繪製完整表面。");
            var splat = Required(field, "m_SplatDatabase"); var layers = new List<TerrainLayerPreview>();
            foreach (var pointer in Array(Required(splat, "m_TerrainLayers")))
            {
                ct.ThrowIfCancellationRequested(); var external = Required(pointer, "m_FileID").AsInt;
                var layerId = Required(pointer, "m_PathID").AsLong.ToString(CultureInfo.InvariantCulture);
                if (external <= 0 || external > asset.Metadata.Externals.Count) throw new InvalidDataException("TerrainLayer 外部參照索引無效。");
                var guid = asset.Metadata.Externals[external - 1].Guid.ToString(); var layerPath = resolveGuid(guid);
                var layer = new TerrainLayerPreview(guid, layerId, layerPath, null, "", "0", [1, 1], [0, 0]);
                if (layerPath is null) diagnostics.Add("TerrainLayer GUID 缺少或不唯一：" + guid + "；該層保留診斷棋盤，不猜測替代貼圖。");
                else
                {
                    try
                    {
                        var layerAbsolute = project.Resolve(layerPath);
                        if (!layerPath.EndsWith(".terrainlayer", StringComparison.OrdinalIgnoreCase) || new FileInfo(layerAbsolute).Length > 1024 * 1024)
                            throw new InvalidDataException("TerrainLayer 不是可讀取的原生文字資源。");
                        var document = UnityYamlDocument.Load(layerAbsolute);
                        if (!long.TryParse(layerId, CultureInfo.InvariantCulture, out var nativeId) || !document.Objects.Any(o => o.FileId == nativeId && o.ClassId == 1953259897 && !o.IsStripped))
                            throw new InvalidDataException("TerrainLayer fileID／型別不符。");
                        var body = Body(document, nativeId); var textureGuid = Text(At(body, "m_DiffuseTexture", "guid"));
                        var textureId = Long(At(body, "m_DiffuseTexture", "fileID")).ToString(CultureInfo.InvariantCulture);
                        var tile = Vector(At(body, "m_TileSize"), "xy", 1); var offset = Vector(At(body, "m_TileOffset"), "xy");
                        var texturePath = resolveGuid(textureGuid);
                        if (tile.Any(v => v <= 0) || textureId != "2800000") texturePath = null;
                        layer = layer with { DiffusePath = texturePath, DiffuseGuid = textureGuid, DiffuseFileId = textureId, TileSize = tile, TileOffset = offset };
                        if (texturePath is null) diagnostics.Add(layerPath + "：來源 Diffuse 參照或 Tiling 尚未解析，保留診斷棋盤。");
                    }
                    catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or YamlDotNet.Core.YamlException)
                    { diagnostics.Add(layerPath + "：" + ex.Message); }
                }
                layers.Add(layer);
            }
            var controls = new List<TerrainControlPreview>();
            foreach (var pointer in Array(Required(splat, "m_AlphaTextures")))
            {
                var textureId = Required(pointer, "m_PathID").AsLong; var external = Required(pointer, "m_FileID").AsInt;
                var textureInfo = asset.AssetInfos.SingleOrDefault(o => o.PathId == textureId && o.TypeId == 28);
                if (external != 0 || textureInfo is null) { diagnostics.Add("Terrain alphamap 不是可解析的檔內 Texture2D，混色保持未解析。"); continue; }
                var texture = manager.GetBaseField(instance, textureInfo); var width = Required(texture, "m_Width").AsInt; var height = Required(texture, "m_Height").AsInt;
                if (Required(texture, "m_TextureFormat").AsInt != 4 || width < 1 || height < 1 || width > 2048 || height > 2048)
                { diagnostics.Add("Terrain alphamap 不是支援的 RGBA32／尺寸，混色保持未解析。"); continue; }
                var data = Required(texture, "image data").AsByteArray; var size = checked(width * height * 4);
                if (data.Length < size) { diagnostics.Add("Terrain alphamap 本體不完整，不讀取外部串流或產生替代權重。"); continue; }
                controls.Add(new(textureId.ToString(CultureInfo.InvariantCulture), width, height, data[..size]));
            }
            if (layers.Count > 4 || controls.Count != 1) diagnostics.Add("本輪只預覽單一 RGBA alphamap／最多 4 層 Diffuse；其餘 Terrain 表面效果未適配。");
            ct.ThrowIfCancellationRequested();
            if (Hashing.File(absolute) != hash) throw new SaveConflictException("TerrainData 讀取期間已變動，請重新載入。");
            return new(path, id.ToString(CultureInfo.InvariantCulture), hash, asset.Metadata.UnityVersion, grid, holes, holesLod, layers.ToArray(), controls.ToArray(), diagnostics.ToArray());
        }
        finally { manager.UnloadAllAssetsFiles(true); }
    }
    private static AssetTypeValueField Required(AssetTypeValueField parent, string name) => parent.Children.SingleOrDefault(c => c.FieldName == name)
        ?? throw new InvalidDataException("TerrainData 結構尚未支援：缺少 " + name);
    private static List<AssetTypeValueField> Array(AssetTypeValueField field) => Required(field, "Array").Children;
}

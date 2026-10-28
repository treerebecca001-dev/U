using System.Globalization;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using SceneWorkbench.Core;

namespace SceneWorkbench.Unity;

public sealed record PreviewCubeReference(string Guid, string FileId, string? Path, string? Sha256);
public sealed record PreviewLightingReflection(string Path, string Sha256, string UnityVersion, string SceneGuid,
    int ProbeCount, PreviewCubeReference[] Cubemaps, float[] AmbientInLinear);

/// <summary>Exact read-only source identities; never selects a reflection texture by filename.</summary>
public static class NativeLightingReflectionReader
{
    public static PreviewLightingReflection Read(ProjectSession project, string path, string fileId, string sceneGuid,
        Func<string, string?> resolve, CancellationToken ct = default)
    {
        var absolute = project.Resolve(path); SafePath.RejectLinks(absolute);
        if (new FileInfo(absolute).Length > 8 * 1024 * 1024) throw new InvalidDataException("LightingData 超過 8 MB 反射參照預覽上限。");
        if (!long.TryParse(fileId, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id)) throw new InvalidDataException("LightingData fileID 無效。");
        var manager = new AssetsManager();
        try
        {
            ct.ThrowIfCancellationRequested(); var instance = manager.LoadAssetsFile(absolute, false); var file = instance.file;
            var version = UnityVersion.Parse(file.Metadata.UnityVersion);
            if (!file.Metadata.TypeTreeEnabled || version.Major is not (2021 or 2022) || version.Minor != 3)
                throw new InvalidDataException("LightingData type tree／版本未驗證。");
            var info = file.AssetInfos.SingleOrDefault(o => o.PathId == id && o.TypeId == 1120)
                ?? throw new InvalidDataException("LightingData 精確 fileID／類型不符。");
            var field = manager.GetBaseField(instance, info);
            string Guid(AssetTypeValueField pointer)
            {
                var external = Required(pointer, "m_FileID").AsInt;
                if (external < 1 || external > file.Metadata.Externals.Count) throw new InvalidDataException("LightingData 外部參照無效。");
                return file.Metadata.Externals[external - 1].Guid.ToString();
            }
            var actualScene = Guid(Required(field, "m_Scene"));
            if (sceneGuid.Length != 32 || actualScene != sceneGuid) throw new InvalidDataException("LightingData 綁定另一個場景，未借用其反射。");
            var probes = Required(Required(field, "m_BakedReflectionProbes"), "Array").Children.Count;
            var pointers = Required(Required(field, "m_BakedReflectionProbeCubemaps"), "Array").Children;
            if (pointers.Count > 256) throw new InvalidDataException("LightingData 反射參照超出本輪上限。");
            var cubes = pointers.Select(pointer =>
            {
                ct.ThrowIfCancellationRequested(); var guid = Guid(pointer); var source = resolve(guid);
                return new PreviewCubeReference(guid, Required(pointer, "m_PathID").AsLong.ToString(CultureInfo.InvariantCulture), source,
                    source is null ? null : Hashing.File(project.Resolve(source)));
            }).ToArray();
            var ambient = Required(field, "m_BakedAmbientProbeInLinear").Children.Select(c => c.AsFloat).ToArray();
            if (ambient.Length != 27 || ambient.Any(v => !float.IsFinite(v))) throw new InvalidDataException("LightingData ambient SH 格式未驗證。");
            return new(path, Hashing.File(absolute), file.Metadata.UnityVersion, actualScene, probes, cubes, ambient);
        }
        finally { manager.UnloadAllAssetsFiles(true); }
    }
    private static AssetTypeValueField Required(AssetTypeValueField parent, string name) => parent.Children.SingleOrDefault(c => c.FieldName == name)
        ?? throw new InvalidDataException("LightingData 缺少欄位：" + name);
}

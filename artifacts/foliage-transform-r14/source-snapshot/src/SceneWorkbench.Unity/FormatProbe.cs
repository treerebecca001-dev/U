using System.Globalization;
using System.Numerics;
using SceneWorkbench.Core;

namespace SceneWorkbench.Unity;

public sealed record YamlProbeResult(string Path, string SourceSha256, string CandidateSha256, string FileId, bool NoOpByteIdentical,
    bool ChangedPositionReloaded, bool OtherObjectsUnchanged, bool UndoByteIdentical, bool SourceUnchanged, string CandidatePath)
{
    public bool UnityAccepted => false;
}

public sealed record FormatProbeReport(DateTimeOffset GeneratedUtc, IReadOnlyList<YamlProbeResult> Yaml, IReadOnlyList<BinaryRoundTripResult> Binary,
    IReadOnlyList<string> MissingEvidence)
{
    public bool StageOnePassed => false;
}

public static class FormatProbe
{
    public static FormatProbeReport Run(string projectRoot, string outputRoot, string? binaryRelative = null, string? classDataPath = null, IProgress<string>? progress = null)
    {
        var project = ProjectSession.Open(projectRoot);
        AuditReportWriter.PrepareOutput(project.Root, outputRoot);
        var yamlResults = new List<YamlProbeResult>();
        foreach (var relative in ProjectAuditor.ReferenceScenes.Where(relative => File.Exists(project.Resolve(relative))))
        {
            progress?.Report("驗證 YAML 局部編輯與復原：" + Path.GetFileName(relative));
            var path = project.Resolve(relative);
            var original = File.ReadAllBytes(path);
            var document = UnityYamlDocument.Parse(original);
            var noOp = original.AsSpan().SequenceEqual(document.ToBytes());
            var transform = document.Objects.FirstOrDefault(item => item.ClassId == 4 && !item.IsStripped);
            if (transform is null) continue;
            var position = document.GetPosition(transform.FileId);
            var target = position + new Vector3(0.125f, 0.25f, -0.5f);
            var history = new CommandHistory();
            history.Execute(new TransformPositionCommand(document, transform.FileId, target));
            var changed = document.ToBytes();
            var reloaded = UnityYamlDocument.Parse(changed);
            var positionReloaded = reloaded.GetPosition(transform.FileId) == target;
            var othersUnchanged = document.Objects.Where(item => item.FileId != transform.FileId)
                .All(item => document.GetRawBody(item.FileId) == reloaded.GetRawBody(item.FileId));
            history.Undo();
            var undo = original.AsSpan().SequenceEqual(document.ToBytes());
            var candidate = Path.Combine(outputRoot, Path.GetFileNameWithoutExtension(relative) + ".candidate.unity");
            using (var stream = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write(changed);
            yamlResults.Add(new YamlProbeResult(relative, Hashing.Bytes(original), Hashing.Bytes(changed), transform.FileId.ToString(CultureInfo.InvariantCulture),
                noOp, positionReloaded, othersUnchanged, undo, Hashing.File(path) == Hashing.Bytes(original), candidate));
        }
        var binaryResults = new List<BinaryRoundTripResult>();
        string[] candidates = binaryRelative is not null ? [binaryRelative] :
        [
            "Assets/02.Arts/Scene/Map/Map_60003/60003_Terrain.asset",
            "Assets/02.Arts/Scene/Map/Map_12006/12006_Terrain.asset"
        ];
        foreach (var relative in candidates)
        {
            var path = project.Resolve(relative);
            if (!File.Exists(path))
            {
                if (binaryRelative is not null) throw new FileNotFoundException("找不到指定原生資源。", path);
                continue;
            }
            progress?.Report("驗證二進位容器重寫：" + Path.GetFileName(relative));
            binaryResults.Add(NativeAssetInspector.RoundTripContainer(path, Path.Combine(outputRoot, Path.GetFileNameWithoutExtension(relative) + ".container-roundtrip.asset"), classDataPath));
        }
        var report = new FormatProbeReport(DateTimeOffset.UtcNow, yamlResults, binaryResults,
        [
            "Unity／URP 可重現環境及 Unity 端載入、繼續編輯證據",
            "FBX Mesh fileID／頂點順序／UV2 的完整對應",
            "新 Terrain／Mesh 生成與實際地形編輯",
            "ProBuilder／Polybrush 原生編輯資料生成",
            "Unity NavMeshData 與 A* 4.3.60 新圖形生成",
            "新 Lightmap／Shadowmask／探針及 Lighting Data 原生綁定",
            "Godot ASE Shader 適配及同條件畫面對照"
        ]);
        JsonFile.WriteNew(Path.Combine(outputRoot, "format-probes.json"), report);
        return report;
    }
}

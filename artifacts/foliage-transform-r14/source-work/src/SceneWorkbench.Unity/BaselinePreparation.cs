using System.Text;
using System.Text.RegularExpressions;
using SceneWorkbench.Core;

namespace SceneWorkbench.Unity;

public sealed record BaselinePackageChange(string Id, string Package, string Version, string? MinimumUnity,
    string? MinimumUnityRelease, string Before, string After, string NativeValidation);
public sealed record BaselineDependency(string Package, string Dependency, string Requirement, string ResolvedVersion);
public sealed record BaselinePreparationReport(int SchemaVersion, string SourceRoot, string PreparedRoot,
    string SourceEditor, string TargetEditor, string TargetRevision, string Method, string OfflineStatus, string NativeStatus,
    string CopyManifestSha256, SaveResult Transaction, IReadOnlyList<BaselinePackageChange> Issues,
    IReadOnlyList<PackageSnapshot> Packages, IReadOnlyList<BaselineDependency> Dependencies,
    IReadOnlyList<FileFingerprint> SourceInputs, IReadOnlyList<FileFingerprint> PreparedInputs);

/// <summary>Creates an explicitly targeted, complete work copy. Never invokes Unity or certifies asset migration.</summary>
public static class BaselinePreparation
{
    public const string MarkerPath = WorkCopy.MetadataDirectory + "/baseline-preparation.json";
    public const string TargetEditor = "2022.3.13f1";
    public const string TargetRevision = "5f90a5ebde0f";
    private static readonly string[] VersionPackages = ["com.unity.ai.navigation", "com.unity.memoryprofiler",
        "com.unity.render-pipelines.core", "com.unity.render-pipelines.universal", "com.unity.render-pipelines.universal-config",
        "com.unity.shadergraph", "com.unity.terrain-tools"];

    public static async Task<BaselinePreparationReport> PrepareAsync(string sourceRoot, string destinationRoot, string outputRoot,
        IProgress<CopyProgress>? progress = null, CancellationToken cancellationToken = default, IProgress<string>? status = null)
    {
        // Preparation is a distinct operation. Strict audit/patch never substitutes a candidate Editor for the recorded version.
        var source = WorkCopy.Open(sourceRoot).Project;
        destinationRoot = SafePath.Canonical(destinationRoot); outputRoot = SafePath.Canonical(outputRoot);
        SafePath.RequireDisjoint(source.Root, destinationRoot);
        SafePath.RequireDisjoint(source.Root, outputRoot); SafePath.RequireDisjoint(destinationRoot, outputRoot);
        if (Directory.Exists(destinationRoot) || File.Exists(destinationRoot) || Directory.Exists(outputRoot) || File.Exists(outputRoot))
            throw new IOException("基準複本與報告目的地必須是新的資料夾，不覆蓋既有資料。");
        SafePath.RejectLinks(destinationRoot); SafePath.RejectLinks(outputRoot);
        status?.Report("檢查來源版本、實際套件與完整依賴條件…");
        var before = Compatibility.Read(source);
        if (!YamlEditProfile.Supports(UnityVersion.Parse(before.EditorVersion)) || before.EditorVersion == TargetEditor)
            throw new InvalidOperationException("只接受已識別、尚未對齊目標的來源基準。");
        RequireOnlyVersionConflicts(before);
        var target = Compatibility.Read(source, UnityVersion.Parse(TargetEditor));
        if (!target.CanWriteKnownYaml) throw new InvalidOperationException("目標基準仍有版本／依賴阻擋，未建立或修改複本。");
        var integrity = new List<Diagnostic>();
        status?.Report("檢查來源資源與 GUID；此階段不修改來源…");
        _ = AssetIndex.Build(source, source.EnumerateSourceFiles(true, cancellationToken).ToArray(), integrity,
            cancellationToken, progress: status, compatibility: before);
        if (integrity.Any(d => d.Severity == Severity.Blocking)) throw new InvalidOperationException("來源 GUID／資源基準不完整，未建立或修改複本。");
        var sourceInputs = Fingerprints(source, before);
        status?.Report("建立附帶套件快取的完整基準複本…");
        var copy = await WorkCopy.CreateAsync(source.Root, destinationRoot, true, progress, cancellationToken);
        RequireUnchanged(source, sourceInputs);
        RequireUnchanged(copy.Project, sourceInputs);
        var copyCandidate = Compatibility.Read(copy.Project, UnityVersion.Parse(TargetEditor));
        if (!copyCandidate.CanWriteKnownYaml) throw new InvalidOperationException("複本套件基準與預檢不同，保留原版本並停止。");
        var versionRelative = "ProjectSettings/ProjectVersion.txt";
        var oldBytes = File.ReadAllBytes(copy.Project.Resolve(versionRelative));
        var bytes = TargetVersionBytes(oldBytes);
        RequireUnchanged(source, sourceInputs);
        cancellationToken.ThrowIfCancellationRequested();
        var saved = new SaveTransaction(copy).Commit([new FileUpdate(versionRelative, Hashing.Bytes(oldBytes), bytes)], cancellationToken: cancellationToken);
        var after = Compatibility.Read(copy.Project);
        if (!after.CanWriteKnownYaml)
        {
            _ = new SaveTransaction(copy).Commit([new FileUpdate(versionRelative, Hashing.Bytes(bytes), oldBytes)]);
            throw new InvalidOperationException("對齊後檢查失敗，版本宣告已復原，未放行。");
        }
        RequireUnchanged(source, sourceInputs);
        var versions = after.Packages.ToDictionary(p => p.Name, StringComparer.Ordinal);
        var issues = before.Packages.Where(p => before.Diagnostics.Any(d => d.Code == "PACKAGE_EDITOR_MISMATCH" && d.Path == p.MetadataPath))
            .Select(p => new BaselinePackageChange(Array.IndexOf(VersionPackages, p.Name) is var i && i >= 0 ? $"BL-{i + 1:000}" : p.Name,
                p.Name, p.Version, p.MinimumUnity, p.MinimumUnityRelease, "BLOCKED", "OFFLINE_DECLARATION_ALIGNED", "UNVERIFIED")).ToArray();
        var edges = after.Packages.SelectMany(p => (p.Dependencies ?? new Dictionary<string, string>())
            .Select(d => new BaselineDependency(p.Name, d.Key, d.Value, versions[d.Key].Version))).ToArray();
        var report = new BaselinePreparationReport(1, source.Root, copy.Project.Root, before.EditorVersion, TargetEditor, TargetRevision,
            "OfflineTargetDeclarationWithKnownYamlProfile;NoUnityImportOrMigration", "VERIFIED_LOCAL_DECLARATIONS", "UNVERIFIED",
            copy.Info.ManifestSha256!, saved, issues, after.Packages, edges, sourceInputs, Fingerprints(copy.Project, after));
        JsonFile.WriteNew(copy.Project.Resolve(MarkerPath), report);
        Directory.CreateDirectory(outputRoot);
        JsonFile.WriteNew(SafePath.Resolve(outputRoot, "preparation.json"), report);
        return report;
    }

    private static void RequireOnlyVersionConflicts(CompatibilitySnapshot snapshot)
    {
        if (snapshot.Diagnostics.Any(d => d.Severity == Severity.Blocking && d.Code is not "PACKAGE_EDITOR_MISMATCH" and not "URP_EDITOR_MISMATCH"))
            throw new InvalidOperationException("來源有版本之外的完整性／依賴阻擋，先修正，未改動來源或建立目標。");
    }
    public static BaselinePreparationReport Verify(string preparedRoot, bool compareSource = false)
    {
        var copy = WorkCopy.Open(preparedRoot);
        var report = JsonFile.Read<BaselinePreparationReport>(copy.Project.Resolve(MarkerPath));
        if (report.SchemaVersion != 1 || !SafePath.Canonical(report.PreparedRoot).Equals(copy.Project.Root, SafePath.Comparison)
            || !SafePath.Canonical(report.SourceRoot).Equals(SafePath.Canonical(copy.Info.SourceRoot), SafePath.Comparison)
            || report.CopyManifestSha256 != copy.Info.ManifestSha256 || report.TargetEditor != TargetEditor || report.TargetRevision != TargetRevision
            || report.NativeStatus != "UNVERIFIED" || report.OfflineStatus != "VERIFIED_LOCAL_DECLARATIONS")
            throw new InvalidDataException("離線基準紀錄的身份或狀態不一致。");
        RequireUnchanged(copy.Project, report.PreparedInputs);
        var current = Compatibility.Read(copy.Project);
        if (!current.CanWriteKnownYaml || current.EditorVersion != TargetEditor)
            throw new InvalidOperationException("目前的版本／依賴基準不符合準備紀錄。");
        if (compareSource) RequireUnchanged(ProjectSession.Open(report.SourceRoot), report.SourceInputs);
        return report;
    }
    private static FileFingerprint[] Fingerprints(ProjectSession project, CompatibilitySnapshot snapshot) =>
        new[] { "ProjectSettings/ProjectVersion.txt", "ProjectSettings/TagManager.asset", "Packages/manifest.json", "Packages/packages-lock.json" }
        .Concat(snapshot.Packages.Select(p => p.MetadataPath).OfType<string>()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
        .Where(p => File.Exists(project.Resolve(p))).Select(p => new FileFingerprint(p, new FileInfo(project.Resolve(p)).Length, Hashing.File(project.Resolve(p)))).ToArray();
    private static void RequireUnchanged(ProjectSession project, IReadOnlyList<FileFingerprint> inputs)
    {
        foreach (var input in inputs)
            if (!File.Exists(project.Resolve(input.Path)) || Hashing.File(project.Resolve(input.Path)) != input.Sha256)
                throw new SaveConflictException($"基準輸入已被外部修改：{input.Path}");
    }
    private static byte[] TargetVersionBytes(byte[] original)
    {
        var bom = original.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf });
        var text = new UTF8Encoding(false, true).GetString(original, bom ? 3 : 0, original.Length - (bom ? 3 : 0));
        var version = new Regex(@"(?m)^m_EditorVersion:[^\r\n]*");
        var revision = new Regex(@"(?m)^m_EditorVersionWithRevision:[^\r\n]*");
        if (version.Matches(text).Count != 1 || revision.Matches(text).Count > 1) throw new InvalidDataException("版本宣告不唯一，保持原檔。");
        text = version.Replace(text, "m_EditorVersion: " + TargetEditor);
        var revisionLine = $"m_EditorVersionWithRevision: {TargetEditor} ({TargetRevision})";
        if (revision.IsMatch(text)) text = revision.Replace(text, revisionLine);
        else { var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n"; text = text.TrimEnd('\r', '\n') + newline + revisionLine + newline; }
        var encoded = Encoding.UTF8.GetBytes(text);
        return bom ? new byte[] { 0xef, 0xbb, 0xbf }.Concat(encoded).ToArray() : encoded;
    }
}

namespace SceneWorkbench.Unity;

public enum Severity { Info, Warning, Blocking }
public enum GateStatus { PassedLocal, Blocked, Pending, NotImplemented }
public sealed record Diagnostic(string Code, Severity Severity, string Message, string? Path = null);
public sealed record GateResult(string Id, GateStatus Status, string Description);
public sealed record PackageSnapshot(string Name, string Version, string Source, string? MetadataPath, string? MinimumUnity,
    string? MinimumUnityRelease = null, IReadOnlyDictionary<string, string>? Dependencies = null, bool IsFeature = false);
public sealed record FileFingerprint(string Path, long Bytes, string Sha256);
public sealed record LayerSnapshot(int Index, string Name);
public sealed record AssetEntry(string Path, string Guid, string Extension, bool IsFolder);
public sealed record InventorySnapshot(int SourceFiles, long SourceBytes, int CacheFiles, long CacheBytes, IReadOnlyDictionary<string, int> Extensions, int IndexedGuids, int DuplicateGuids);
public sealed record MachineSnapshot(string OperatingSystem, string Architecture, string Runtime, int LogicalProcessors, IReadOnlyList<string> InstalledUnityEditors);
public sealed record ShaderSnapshot(string Path, string Name, string Sha256, int GraphNodes, IReadOnlyList<string> NodeTypes, string PreviewStatus);
public sealed record FbxReferenceSnapshot(string Path, int? FileIdsGeneration, bool HasInternalIdTable, IReadOnlyList<string> ReferencedFileIds);
public sealed record SceneSnapshot(string Path, long Bytes, string Sha256, IReadOnlyDictionary<int, int> Classes, int PrefabInstances, int Transforms, int StrippedTransforms, int ExternalReferences, int UnresolvedGuidCount, IReadOnlyList<string> UnresolvedGuidSamples, IReadOnlyList<FbxReferenceSnapshot> FbxReferences);
public sealed record NativeAssetSnapshot(string Path, string Format, uint? SerializedVersion, string? UnityVersion, long Bytes, bool? HasTypeTree, string InspectionStatus, IReadOnlyList<NativeObjectSnapshot> Objects, string? Failure);
public sealed record NativeObjectSnapshot(string Type, string FileId, string? FieldSha256, IReadOnlyList<string> Fields);

public sealed class ProjectAudit
{
    public int SchemaVersion { get; init; } = 1;
    public required string ProjectRoot { get; init; }
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public required MachineSnapshot Machine { get; init; }
    public required string UnityVersion { get; init; }
    public required string ColorSpace { get; init; }
    public required IReadOnlyDictionary<string, string> ToolVersions { get; init; }
    public required IReadOnlyList<PackageSnapshot> Packages { get; init; }
    public required IReadOnlyList<LayerSnapshot> Layers { get; init; }
    public required InventorySnapshot Inventory { get; init; }
    public required IReadOnlyList<SceneSnapshot> Scenes { get; init; }
    public required IReadOnlyList<ShaderSnapshot> Shaders { get; init; }
    public required IReadOnlyList<NativeAssetSnapshot> NativeAssets { get; init; }
    public required IReadOnlyList<FileFingerprint> Fingerprints { get; init; }
    public required IReadOnlyList<GateResult> Gates { get; init; }
    public required IReadOnlyList<Diagnostic> Diagnostics { get; init; }
    public bool BaselineBlocked => Diagnostics.Any(diagnostic => diagnostic.Severity == Severity.Blocking);
    public bool FullEditorAccepted => false;
}

public sealed record AuditBundle(ProjectAudit Report, IReadOnlyList<AssetEntry> Assets);

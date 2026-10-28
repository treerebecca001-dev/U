using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using SceneWorkbench.Core;
using YamlDotNet.RepresentationModel;

namespace SceneWorkbench.Unity;

public static class ProjectAuditor
{
    public static readonly string[] ReferenceScenes =
    [
        "Assets/02.Arts/Scene/Room/Room_main.unity",
        "Assets/02.Arts/Scene/Map/Map_12006/Map_12006.unity",
        "Assets/02.Arts/Scene/Map/Map_60003/Map_60003.unity"
    ];

    public static AuditBundle Audit(string root, string? classDataPath = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var project = ProjectSession.Open(root);
        progress?.Report("讀取版本與套件基準");
        var compatibility = Compatibility.Read(project);
        var diagnostics = compatibility.Diagnostics.ToList();
        progress?.Report("盤點 Unity 來源與套件快取");
        var files = project.EnumerateSourceFiles(true, cancellationToken).ToArray();
        var sourceFiles = 0;
        var cacheFiles = 0;
        long sourceBytes = 0, cacheBytes = 0;
        var extensions = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = SafePath.Relative(project.Root, path);
            var length = new FileInfo(path).Length;
            if (relative.StartsWith("Library/", StringComparison.Ordinal)) { cacheFiles++; cacheBytes += length; }
            else
            {
                sourceFiles++; sourceBytes += length;
                var extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension.Length == 0) extension = "(none)";
                extensions[extension] = extensions.GetValueOrDefault(extension) + 1;
            }
        }
        progress?.Report($"建立 GUID 索引：{sourceFiles:N0} 個來源檔，{cacheFiles:N0} 個快取檔");
        var index = AssetIndex.Build(project, files, diagnostics, cancellationToken, progress, compatibility);
        var scenes = new List<SceneSnapshot>();
        foreach (var relative in ReferenceScenes.Where(relative => File.Exists(project.Resolve(relative))))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("解析場景：" + Path.GetFileName(relative));
            var scene = SceneInspector.Inspect(project, relative, index);
            scenes.Add(scene);
            if (scene.UnresolvedGuidCount != 0)
                diagnostics.Add(new Diagnostic("UNRESOLVED_SCENE_GUIDS", Severity.Warning,
                    $"場景有 {scene.UnresolvedGuidCount} 個尚未解析的外部 GUID。", relative));
        }
        var unresolvedFbx = scenes.SelectMany(scene => scene.FbxReferences).Where(item => !item.HasInternalIdTable).Select(item => item.Path).Distinct().Count();
        if (unresolvedFbx != 0)
            diagnostics.Add(new Diagnostic("FBX_SUBASSET_MAPPING_UNPROVEN", Severity.Warning,
                $"樣本場景引用 {unresolvedFbx} 個沒有可用 internalIDToNameTable 的 FBX；不得猜測 Mesh fileID。"));

        progress?.Report("盤點 ASE Shader 與原生二進位樣本");
        var shaders = ReadShaders(project, files, cancellationToken);
        var native = new List<NativeAssetSnapshot>();
        var nativePaths = files.Where(path => path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                && SafePath.Relative(project.Root, path).StartsWith("Assets/02.Arts/Scene/Map/", StringComparison.Ordinal)
                && (path.Contains("Map_60003", StringComparison.Ordinal) || path.Contains("Map_12006", StringComparison.Ordinal))
                && (Path.GetFileName(path).Contains("Terrain", StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(path).Contains("LightingData", StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(path).Contains("NavMesh", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(path => path, StringComparer.Ordinal).Take(12).ToArray();
        foreach (var path in nativePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            native.Add(NativeAssetInspector.Inspect(path, SafePath.Relative(project.Root, path), classDataPath));
        }
        var toolVersions = ReadToolVersions(project);
        var fingerprints = new List<FileFingerprint>();
        var criticalFiles = new[] { "ProjectSettings/ProjectVersion.txt", "ProjectSettings/ProjectSettings.asset", "ProjectSettings/TagManager.asset", "ProjectSettings/DynamicsManager.asset", "Packages/manifest.json", "Packages/packages-lock.json" }
            .Concat(compatibility.Packages.Where(item => item.MetadataPath is not null && item.Name.StartsWith("com.unity.render-pipelines", StringComparison.Ordinal)).Select(item => item.MetadataPath!))
            .Concat(shaders.Select(item => item.Path)).Concat(ReferenceScenes).Concat(nativePaths.Select(path => SafePath.Relative(project.Root, path)))
            .Where(relative => File.Exists(project.Resolve(relative))).Distinct(StringComparer.Ordinal);
        foreach (var relative in criticalFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = project.Resolve(relative);
            fingerprints.Add(new FileFingerprint(relative, new FileInfo(path).Length, Hashing.File(path)));
        }
        var gates = new List<GateResult>
        {
            new("baseline", diagnostics.Any(item => item.Severity == Severity.Blocking) ? GateStatus.Blocked : GateStatus.PassedLocal, "版本、套件內容與 GUID 唯一性"),
            new("fbx-subassets", GateStatus.Pending, "FBX Mesh 子資源 fileID 與頂點／UV2 對應尚未通過 Unity 往返"),
            new("native-terrain", GateStatus.Pending, "二進位容器讀取不等於 Terrain 編輯與 Unity 驗收"),
            new("probuilder-polybrush", GateStatus.NotImplemented, "原套件編輯資料生成與雙向編輯尚未實作"),
            new("unity-navmesh", GateStatus.NotImplemented, "Unity 原生 NavMeshData 生成尚未實作"),
            new("astar-recast", GateStatus.NotImplemented, "A* 4.3.60 原生圖形寫入尚未實作"),
            new("native-lighting", GateStatus.NotImplemented, "獨立生成 Lightmap／Shadowmask／探針及原生綁定尚未實作"),
            new("ase-preview", GateStatus.NotImplemented, "目前只盤點 ASE 來源；Godot Shader 適配與畫面比對尚未實作"),
            new("full-editor", GateStatus.Blocked, "高風險關卡未通過，完整桌面編輯器尚未開發")
        };
        var machine = new MachineSnapshot(RuntimeInformation.OSDescription, RuntimeInformation.OSArchitecture.ToString(), RuntimeInformation.FrameworkDescription,
            Environment.ProcessorCount, InstalledEditors());
        var settings = project.Resolve("ProjectSettings/ProjectSettings.asset");
        var colorMatch = File.Exists(settings) ? Regex.Match(File.ReadAllText(settings), @"^\s*m_ActiveColorSpace:\s*(\d+)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant) : Match.Empty;
        var colorSpace = colorMatch.Success ? colorMatch.Groups[1].Value == "0" ? "Gamma" : "Linear" : "Unknown";
        var report = new ProjectAudit
        {
            ProjectRoot = project.Root, Machine = machine, UnityVersion = compatibility.EditorVersion, ColorSpace = colorSpace,
            ToolVersions = toolVersions, Packages = compatibility.Packages, Layers = ReadLayers(project),
            Inventory = new InventorySnapshot(sourceFiles, sourceBytes, cacheFiles, cacheBytes, extensions, index.Entries.Count, index.DuplicateGuidCount),
            Scenes = scenes, Shaders = shaders, NativeAssets = native, Fingerprints = fingerprints, Gates = gates, Diagnostics = diagnostics
        };
        progress?.Report("診斷完成");
        return new AuditBundle(report, index.Entries);
    }

    private static IReadOnlyList<ShaderSnapshot> ReadShaders(ProjectSession project, IEnumerable<string> files, CancellationToken cancellationToken)
    {
        var result = new List<ShaderSnapshot>();
        foreach (var path in files.Where(path => path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = SafePath.Relative(project.Root, path);
            if (!relative.StartsWith("Assets/", StringComparison.Ordinal) || relative.StartsWith("Assets/AmplifyShaderEditor/", StringComparison.Ordinal)) continue;
            var text = File.ReadAllText(path);
            if (!text.Contains("ASEBEGIN", StringComparison.Ordinal)) continue;
            var name = Regex.Match(text, "\\bShader\\s+\"([^\"]+)\"", RegexOptions.CultureInvariant);
            var nodes = Regex.Matches(text, @"^Node;AmplifyShaderEditor\.([^;]+);", RegexOptions.Multiline | RegexOptions.CultureInvariant);
            result.Add(new ShaderSnapshot(relative, name.Success ? name.Groups[1].Value : Path.GetFileNameWithoutExtension(path), Hashing.File(path), nodes.Count,
                nodes.Cast<Match>().Select(match => match.Groups[1].Value).Distinct().Order(StringComparer.Ordinal).ToArray(), "not-adapted"));
        }
        return result.OrderBy(item => item.Path, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyDictionary<string, string> ReadToolVersions(ProjectSession project)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var ase = project.Resolve("Assets/AmplifyShaderEditor/Plugins/Editor/Version/VersionInfo.cs");
        if (File.Exists(ase))
        {
            var text = File.ReadAllText(ase);
            string Part(string name) => Regex.Match(text, @"\b" + name + @"\s*=\s*(\d+)\s*;", RegexOptions.CultureInvariant).Groups[1].Value;
            if (Part("Major") != "" && Part("Minor") != "" && Part("Release") != "")
                result["ASE"] = $"{Part("Major")}.{Part("Minor")}.{Part("Release")}r{Part("Revision")}";
        }
        var astar = project.Resolve("Assets/04.Packages/AstarPathfindingProject/Core/AstarPath.cs");
        if (File.Exists(astar))
        {
            var match = Regex.Match(File.ReadAllText(astar), @"System\.Version\((\d+),\s*(\d+),\s*(\d+)\)", RegexOptions.CultureInvariant);
            if (match.Success) result["AstarPathfinding"] = $"{match.Groups[1].Value}.{match.Groups[2].Value}.{match.Groups[3].Value}";
        }
        return result;
    }

    private static IReadOnlyList<LayerSnapshot> ReadLayers(ProjectSession project)
    {
        var path = project.Resolve("ProjectSettings/TagManager.asset");
        if (!File.Exists(path)) return [];
        var document = UnityYamlDocument.Load(path);
        var item = document.Objects.FirstOrDefault(item => item.ClassId == 78);
        if (item is null) return [];
        var root = document.ReadObject(item.FileId);
        if (!root.Children.TryGetValue(new YamlScalarNode("TagManager"), out var manager) || manager is not YamlMappingNode mapping
            || !mapping.Children.TryGetValue(new YamlScalarNode("layers"), out var layers) || layers is not YamlSequenceNode sequence) return [];
        return sequence.Children.Select((node, index) => new LayerSnapshot(index, (node as YamlScalarNode)?.Value ?? "")).ToArray();
    }

    private static IReadOnlyList<string> InstalledEditors()
    {
        if (!OperatingSystem.IsWindows()) return [];
        var roots = new[] { @"D:\Program Files\Unity\Hub\Editor", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity", "Hub", "Editor") };
        return roots.Where(Directory.Exists).SelectMany(Directory.EnumerateDirectories)
            .Select(Path.GetFileName).OfType<string>().Distinct().Order(StringComparer.Ordinal).ToArray();
    }
}

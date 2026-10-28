using System.Globalization;
using System.Text;
using System.Text.Json;
using SceneWorkbench.Core;
using YamlDotNet.RepresentationModel;
using static SceneWorkbench.Unity.UnityData;

namespace SceneWorkbench.Unity;

public sealed record PreviewNode(string Id, string OwnerId, string Name, string ParentId, float[] Position, float[] Rotation, float[] Scale,
    bool Active, string? MeshPath, string MeshGuid, string MeshFileId, string[] Materials, PreviewLight? Light = null, FbxInstancePreview? FbxInstance = null, PreviewTerrainReference? Terrain = null, PreviewRenderer? Renderer = null, int Layer = 0);
public sealed record PreviewRenderer(string FileId, int CastShadows, bool ReceiveShadows, int ReflectionProbeUsage, uint RenderingLayerMask);
public sealed record PreviewShadow(int Type, float Strength, float Bias, float NormalBias, float NearPlane, int Resolution, int CustomResolution, bool CullingMatrixOverride);
public sealed record PreviewLight(int Type, float[] Color, float Intensity, float Range, float SpotAngle, PreviewShadow? Shadows = null, uint CullingMask = uint.MaxValue, uint RenderingLayerMask = 1);
public sealed record PreviewEnvironment(string SkyboxGuid, string SkyboxFileId, string? SkyboxPath, int ReflectionMode, float ReflectionIntensity, string CustomReflectionGuid, string CustomReflectionFileId, string? CustomReflectionPath, PreviewLightingReflection? BakedReflection);
public sealed record PrefabOverrideDetail(string OwnerId, string AssetPath, string InstanceId, string TargetFileId,
    string[] Properties, string Status, string? NestedInstanceId = null, string? SourceGuid = null, string? SourceFileId = null, string? Method = null);
public sealed record PreviewGraph(string ScenePath, PreviewNode[] Nodes, MaterialSnapshot[] Materials, string[] Diagnostics, NativeTerrainPreview[]? Terrains = null, PreviewEnvironment? Environment = null, PrefabOverrideDetail[]? PrefabOverrides = null);
public sealed record ModelImportPreview(string Path, string Sha256, float GlobalScale, bool UseFileScale, bool UseFileUnits, bool SwapUvChannels,
    bool BakeAxisConversion, Dictionary<string, string> MeshNames);

/// <summary>Read-only native dependency graph. Expanded Prefabs never become editable scene documents.</summary>
public sealed class PreviewCatalog
{
    private readonly ProjectSession project;
    private readonly AssetIndex index;
    private readonly string editorVersion;
    private readonly Dictionary<string, string> guidByPath;
    public PreviewCatalog(ProjectSession project, CancellationToken ct = default, IProgress<string>? progress = null, string? cacheDirectory = null)
    {
        this.project = project;
        var compatibility = Compatibility.Read(project);
        editorVersion = compatibility.EditorVersion;
        var metadata = ProjectSession.RequiredRoots.SelectMany(root => SafePath.EnumerateMetadata(project.Resolve(root), ct)).ToList();
        // Runtime shader GUIDs are needed for the existing URP materials. Index just their
        // source folders, not every cached Editor/test/sample file on initial scene load.
        foreach (var package in compatibility.Packages.Where(p => p.Name == "com.unity.render-pipelines.universal" && p.MetadataPath is not null))
        {
            var root = package.MetadataPath![..^"package.json".Length];
            foreach (var folder in new[] { "Shaders", "Runtime/Materials" })
            { var path = project.Resolve(root + folder); if (Directory.Exists(path)) metadata.AddRange(SafePath.EnumerateMetadata(path, ct)); }
        }
        var files = metadata.OfType<FileInfo>().Select(info => info.FullName).Distinct(SafePath.Comparer).ToArray();
        var signature = Hashing.Bytes(Encoding.UTF8.GetBytes(string.Join('\n', metadata.OrderBy(info => info.FullName, SafePath.Comparer).Select(info =>
            SafePath.Relative(project.Root, info.FullName) + "|" + (int)info.Attributes + (info is FileInfo f && f.Extension == ".meta" ? "|" + f.Length + "|" + f.LastWriteTimeUtc.Ticks : "")))));
        var cachePath = cacheDirectory is null ? null : SafePath.Resolve(cacheDirectory, Hashing.Bytes(Encoding.UTF8.GetBytes(project.Root)) + ".json");
        AssetIndex? cached = null;
        if (cachePath is not null && File.Exists(cachePath))
        {
            try
            {
                var cache = JsonSerializer.Deserialize<PreviewIndexCache>(File.ReadAllText(cachePath));
                if (cache is { Version: 1 } && cache.ProjectRoot == project.Root && cache.Signature == signature)
                    cached = AssetIndex.RestorePreviewIndex(cache.Entries, files.Where(f => f.EndsWith(".meta", StringComparison.Ordinal)).Select(f => SafePath.Relative(project.Root, f)).ToHashSet(StringComparer.Ordinal));
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or ArgumentException) { /* Rebuild derived cache; never weaken native-save validation. */ }
        }
        var diagnostics = new List<Diagnostic>();
        index = cached ?? AssetIndex.Build(project, files, diagnostics, ct, progress, compatibility);
        ct.ThrowIfCancellationRequested();
        if (cached is null && cachePath is not null)
        {
            Directory.CreateDirectory(cacheDirectory!);
            var temporary = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new PreviewIndexCache(1, project.Root, signature, index.Entries)));
            File.Move(temporary, cachePath, true);
        }
        guidByPath = index.Entries.Where(e => index.ResolveGuid(e.Guid) is not null).ToDictionary(e => e.Path, e => e.Guid, StringComparer.Ordinal);
    }
    private sealed record PreviewIndexCache(int Version, string ProjectRoot, string Signature, IReadOnlyList<AssetEntry> Entries);
    public string? Resolve(string guid) => index.ResolveGuid(guid)?.Path;
    public MaterialEditSession Material(string path, IProjectWriteAccess? writeAccess = null) => new(project, path, Resolve, editorVersion, writeAccess: writeAccess);
    public ModelImportPreview ModelInfo(string path)
    {
        var absolute = project.Resolve(path);
        if (!Path.GetExtension(path).Equals(".fbx", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("請選擇 FBX。");
        var stream = new YamlStream(); stream.Load(new StringReader(File.ReadAllText(project.Resolve(path + ".meta"))));
        var importer = At(stream.Documents[0].RootNode, "ModelImporter");
        var names = new Dictionary<string, string>();
        foreach (var row in List(At(importer, "internalIDToNameTable")))
            if (At(row, "first") is YamlMappingNode first)
                foreach (var pair in first.Children.Where(p => Text(p.Key) == "43")) names[Text(pair.Value)] = Text(At(row, "second"));
        return new(path, Hashing.File(absolute), Float(At(importer, "meshes", "globalScale"), 1), Long(At(importer, "meshes", "useFileScale"), 1) != 0,
            Long(At(importer, "meshes", "useFileUnits"), 1) != 0, Long(At(importer, "meshes", "swapUVChannels")) != 0, Long(At(importer, "meshes", "bakeAxisConversion")) != 0, names);
    }
    public object TextureInfo(string path)
    {
        var stream = new YamlStream(); stream.Load(new StringReader(File.ReadAllText(project.Resolve(path + ".meta"))));
        var importer = At(stream.Documents[0].RootNode, "TextureImporter");
        return new { path, srgb = Long(At(importer, "mipmaps", "sRGBTexture"), 1) != 0, textureType = Long(At(importer, "textureType")),
            textureShape = Long(At(importer, "textureShape"), 1), rows = Long(At(importer, "flipbookRows"), 1), columns = Long(At(importer, "flipbookColumns"), 1),
            mipmaps = Long(At(importer, "mipmaps", "enableMipMap"), 1) != 0, filterMode = Long(At(importer, "textureSettings", "filterMode"), 1),
            wrapU = Long(At(importer, "textureSettings", "wrapU")), wrapV = Long(At(importer, "textureSettings", "wrapV")), sha256 = Hashing.File(project.Resolve(path)) };
    }
    public PreviewGraph Scene(string relativePath, byte[] bytes, Func<string, MaterialSnapshot?>? currentMaterial = null, CancellationToken ct = default)
    {
        var nodes = new List<PreviewNode>(); var diagnostics = new HashSet<string>(StringComparer.Ordinal);
        var materials = new Dictionary<string, MaterialSnapshot>(StringComparer.Ordinal); var visiting = new HashSet<string>(StringComparer.Ordinal);
        var terrains = new Dictionary<string, NativeTerrainPreview>(StringComparer.Ordinal);
        var prefabOverrides = new List<PrefabOverrideDetail>();
        void LoadMaterial(string path)
        {
            if (materials.ContainsKey(path)) return;
            try { materials[path] = currentMaterial?.Invoke(path) ?? Material(path).Snapshot(false); }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException or ArgumentException or YamlDotNet.Core.YamlException) { diagnostics.Add(path + "：" + ex.Message); }
        }
        var materialIdentities = new Dictionary<string, long>(StringComparer.Ordinal);
        string? ResolveMaterial(string guid, string fileId)
        {
            var path = Resolve(guid);
            if (path is null || !path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)) return null;
            if (!materialIdentities.TryGetValue(path, out var actual))
            {
                try
                {
                    var matches = UnityYamlDocument.Load(project.Resolve(path)).Objects.Where(o => o.ClassId == 21 && !o.IsStripped).ToArray();
                    actual = matches.Length == 1 ? matches[0].FileId : 0;
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or YamlDotNet.Core.YamlException)
                { actual = 0; diagnostics.Add(path + "：" + ex.Message); }
                materialIdentities[path] = actual;
            }
            if (actual == 0 || actual.ToString(CultureInfo.InvariantCulture) != fileId) return null;
            LoadMaterial(path); return materials.ContainsKey(path) ? path : null;
        }
        void AddDoc(string path, UnityYamlDocument doc, string prefix, string owner, string parent, List<YamlNode> overrides, int depth,
            List<YamlNode>? removedComponents = null)
        {
            ct.ThrowIfCancellationRequested();
            if (depth > 12 || nodes.Count > 10000) { diagnostics.Add("Prefab 深度或物件數超出本輪預覽限制。"); return; }
            var bodies = doc.Objects.ToDictionary(o => o.FileId, o => Body(doc, o.FileId));
            var objects = doc.Objects.ToDictionary(o => o.FileId);
            var targets = new PrefabOverrideTargets(doc, bodies, Resolve, editorVersion);
            var routed = new Dictionary<long, List<YamlNode>>();
            var mappings = new Dictionary<long, PrefabOverrideTarget>();
            foreach (var changes in overrides.Where(c => Text(At(c, "target", "guid")) == guidByPath.GetValueOrDefault(path))
                .GroupBy(c => Long(At(c, "target", "fileID"))))
            {
                if (bodies.TryGetValue(changes.Key, out var body) && !objects[changes.Key].IsStripped)
                {
                    foreach (var change in changes) ApplyOverride(body, Text(At(change, "propertyPath")), Text(At(change, "value")), At(change, "objectReference"), diagnostics, path);
                    continue;
                }
                var target = targets.Resolve(changes.Key, out var ambiguous);
                var properties = changes.Select(c => Text(At(c, "propertyPath"))).Distinct(StringComparer.Ordinal).ToArray();
                if (target is null)
                {
                    diagnostics.Add(path + $"：覆寫目標 fileID {changes.Key} {(ambiguous ? "有多個候選，未套用" : "在目前來源無法解析，可能為未使用覆寫")}；原始 {properties.Length} 個欄位保留，不套用至其他物件。");
                    prefabOverrides.Add(new(owner, path, parent, changes.Key.ToString(CultureInfo.InvariantCulture), properties,
                        ambiguous ? "ambiguous-target" : "unresolved-target"));
                    continue;
                }
                mappings[changes.Key] = target;
                prefabOverrides.Add(new(owner, path, parent, changes.Key.ToString(CultureInfo.InvariantCulture), properties, "resolved",
                    target.InstanceId.ToString(CultureInfo.InvariantCulture), target.Guid, target.FileId.ToString(CultureInfo.InvariantCulture), target.Method));
            }
            // Preserve original order even when two explicit correspondence aliases name
            // the same imported object. The last native property modification still wins.
            foreach (var change in overrides.Where(c => Text(At(c, "target", "guid")) == guidByPath.GetValueOrDefault(path)))
            {
                if (!mappings.TryGetValue(Long(At(change, "target", "fileID")), out var target)) continue;
                if (!routed.TryGetValue(target.InstanceId, out var outgoing)) routed[target.InstanceId] = outgoing = [];
                outgoing.Add(PrefabOverrideTargets.Retarget(change, target));
            }
            var removed = new HashSet<long>();
            foreach (var reference in removedComponents ?? [])
            {
                var id = Long(At(reference, "fileID")); var sameAsset = Text(At(reference, "guid")) == guidByPath.GetValueOrDefault(path);
                var item = objects.GetValueOrDefault(id);
                var valid = sameAsset && item is { IsStripped: false } && item.ClassId is not (1 or 4 or 224 or 1001) &&
                    objects.GetValueOrDefault(Long(At(bodies[id], "m_GameObject", "fileID"))) is { ClassId: 1, IsStripped: false };
                if (valid) removed.Add(id);
                var unused = sameAsset && id != 0 && item is null && !doc.Objects.Any(o => o.ClassId == 1001);
                var status = valid ? "removed-component" : unused ? "unused-removal" : "unresolved-removal";
                prefabOverrides.Add(new(owner, path, parent, id.ToString(CultureInfo.InvariantCulture), ["m_RemovedComponents"], status));
                if (unused) diagnostics.Add(path + $"：移除覆寫指向目前來源不存在的元件 fileID {id}，屬未使用的移除資料；原文保留，現有元件不變。");
                else if (!valid) diagnostics.Add(path + $"：移除元件覆寫 fileID {id} 尚未安全對應，預覽保留來源資料。");
            }
            string NodeId(long id) => prefix + id.ToString(CultureInfo.InvariantCulture);
            var transforms = doc.Objects.Where(o => !o.IsStripped && o.ClassId == 4).ToArray();
            var goToTransform = transforms.ToDictionary(t => Long(At(bodies[t.FileId], "m_GameObject", "fileID")), t => t.FileId);
            var componentGroups = doc.Objects.Where(o => !o.IsStripped && !removed.Contains(o.FileId) && o.ClassId is 33 or 23 or 137 or 108 or 218)
                .GroupBy(o => Long(At(bodies[o.FileId], "m_GameObject", "fileID"))).ToDictionary(g => g.Key, g => g.ToArray());
            foreach (var transform in transforms)
            {
                ct.ThrowIfCancellationRequested();
                var body = bodies[transform.FileId]; var goId = Long(At(body, "m_GameObject", "fileID"));
                if (!bodies.TryGetValue(goId, out var go)) continue;
                var list = componentGroups.GetValueOrDefault(goId) ?? [];
                var filter = list.FirstOrDefault(o => o.ClassId == 33); var renderer = list.FirstOrDefault(o => o.ClassId is 23 or 137);
                var render = renderer is null ? null : bodies[renderer.FileId];
                var mesh = filter is not null ? At(bodies[filter.FileId], "m_Mesh") : renderer?.ClassId == 137 ? At(render, "m_Mesh") : null;
                if (renderer is null || Long(At(render, "m_Enabled"), 1) == 0) mesh = null;
                var meshGuid = Text(At(mesh, "guid")); var meshId = Long(At(mesh, "fileID"));
                var meshPath = meshGuid.Length > 0 ? Resolve(meshGuid) : meshId != 0 ? path : null;
                var matPaths = List(At(render, "m_Materials")).Select(m => Resolve(Text(At(m, "guid"))) ?? "").ToArray();
                foreach (var matPath in matPaths.Where(p => p.Length > 0)) LoadMaterial(matPath);
                if (meshId != 0 && meshPath is null && !meshGuid.StartsWith("0000000000000000", StringComparison.Ordinal)) diagnostics.Add("找不到唯一 Mesh GUID：" + meshGuid);
                var lightObject = list.FirstOrDefault(o => o.ClassId == 108); PreviewLight? light = null;
                if (lightObject is not null && Long(At(bodies[lightObject.FileId], "m_Enabled"), 1) != 0)
                {
                    var l = bodies[lightObject.FileId]; var s = At(l, "m_Shadows");
                    light = new((int)Long(At(l, "m_Type")), Vector(At(l, "m_Color"), "rgb", 1), Float(At(l, "m_Intensity"), 1), Float(At(l, "m_Range"), 10), Float(At(l, "m_SpotAngle"), 30),
                        new((int)Long(At(s, "m_Type")), Float(At(s, "m_Strength"), 1), Float(At(s, "m_Bias"), .05f), Float(At(s, "m_NormalBias"), .4f), Float(At(s, "m_NearPlane"), .2f),
                            (int)Long(At(s, "m_Resolution"), -1), (int)Long(At(s, "m_CustomResolution"), -1), Long(At(s, "m_UseCullingMatrixOverride")) != 0),
                        unchecked((uint)Long(At(l, "m_CullingMask", "m_Bits"), -1)), unchecked((uint)Long(At(l, "m_RenderingLayerMask"), 1)));
                }
                PreviewTerrainReference? terrain = null;
                var terrainObject = list.FirstOrDefault(o => o.ClassId == 218);
                if (terrainObject is not null)
                {
                    var t = bodies[terrainObject.FileId]; var reference = At(t, "m_TerrainData");
                    var terrainGuid = Text(At(reference, "guid")); var terrainId = Long(At(reference, "fileID")).ToString(CultureInfo.InvariantCulture);
                    var terrainPath = Resolve(terrainGuid);
                    terrain = new(terrainPath, terrainGuid, terrainId, Long(At(t, "m_Enabled"), 1) != 0, Long(At(t, "m_DrawHeightmap"), 1) != 0);
                    if (terrainPath is null) diagnostics.Add("TerrainData GUID 缺少或不唯一：" + terrainGuid);
                    else if (!terrains.ContainsKey(terrainPath + ":" + terrainId))
                    {
                        try
                        {
                            var data = NativeTerrainReader.Read(project, terrainPath, terrainId, Resolve, ct);
                            terrains[terrainPath + ":" + terrainId] = data;
                            foreach (var message in data.Diagnostics) diagnostics.Add(terrainPath + "：" + message);
                        }
                        catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException or ArgumentException or YamlDotNet.Core.YamlException)
                        { diagnostics.Add(terrainPath + "：無法預覽原生 Terrain 高度：" + ex.Message); }
                    }
                }
                var father = Long(At(body, "m_Father", "fileID"));
                PreviewRenderer? rendererPreview = renderer is null ? null : new(renderer.FileId.ToString(CultureInfo.InvariantCulture), (int)Long(At(render, "m_CastShadows"), 1), Long(At(render, "m_ReceiveShadows"), 1) != 0,
                    (int)Long(At(render, "m_ReflectionProbeUsage"), 1), unchecked((uint)Long(At(render, "m_RenderingLayerMask"), 1)));
                if (terrainObject is not null)
                {
                    var t = bodies[terrainObject.FileId];
                    rendererPreview = new(terrainObject.FileId.ToString(CultureInfo.InvariantCulture), (int)Long(At(t, "m_ShadowCastingMode"), 1), true,
                        (int)Long(At(t, "m_ReflectionProbeUsage"), 1), unchecked((uint)Long(At(t, "m_RenderingLayerMask"), 1)));
                }
                nodes.Add(new(NodeId(transform.FileId), owner.Length == 0 ? transform.FileId.ToString(CultureInfo.InvariantCulture) : owner,
                    Text(At(go, "m_Name")), father == 0 ? parent : NodeId(father), Vector(At(body, "m_LocalPosition"), "xyz"),
                    At(body, "m_LocalRotation") is null ? [0, 0, 0, 1] : Vector(At(body, "m_LocalRotation"), "xyzw"), Vector(At(body, "m_LocalScale"), "xyz", 1),
                    Long(At(go, "m_IsActive"), 1) != 0, meshPath, meshGuid, meshId.ToString(CultureInfo.InvariantCulture), matPaths, light, Terrain: terrain,
                    Renderer: rendererPreview, Layer: (int)Long(At(go, "m_Layer"))));
            }
            foreach (var prefab in doc.Objects.Where(o => o.ClassId == 1001 && !o.IsStripped))
            {
                var body = bodies[prefab.FileId]; var source = Resolve(Text(At(body, "m_SourcePrefab", "guid")));
                var key = NodeId(prefab.FileId); var prefabOwner = owner.Length == 0 ? prefab.FileId.ToString(CultureInfo.InvariantCulture) : owner;
                var transformParent = Long(At(body, "m_Modification", "m_TransformParent", "fileID"));
                nodes.Add(new(key, prefabOwner, source is null ? "未解析 Prefab" : System.IO.Path.GetFileNameWithoutExtension(source), transformParent == 0 ? parent : NodeId(transformParent),
                    [0, 0, 0], [0, 0, 0, 1], [1, 1, 1], true, null, "", "0", []));
                if (source is null) { diagnostics.Add("Prefab GUID 缺少或不唯一：" + Text(At(body, "m_SourcePrefab", "guid"))); continue; }
                if (Path.GetExtension(source).Equals(".fbx", StringComparison.OrdinalIgnoreCase))
                {
                    var sourceGuid = Text(At(body, "m_SourcePrefab", "guid"));
                    var modelChanges = List(At(body, "m_Modification", "m_Modifications")).ToList();
                    modelChanges.AddRange(routed.GetValueOrDefault(prefab.FileId) ?? []);
                    var binding = FbxInstanceOverrides.Read(sourceGuid, modelChanges, ResolveMaterial, message => diagnostics.Add(source + "：" + message));
                    var paths = binding.Renderers.Length == 1 ? binding.Renderers[0].Slots.Select(s => s.Path ?? "").ToArray() : [];
                    if (binding.Renderers.Length != 1) diagnostics.Add(source + "：FBX Renderer 材質覆寫不唯一／缺少；保留未解析診斷，不猜測綁定。");
                    var fbxRenderer = binding.Renderers.Length == 1 ? binding.Renderers[0] : null;
                    nodes.Add(new(key + "/fbx", prefabOwner, Path.GetFileNameWithoutExtension(source), key, [0, 0, 0], [0, 0, 0, 1], [1, 1, 1], true, source, sourceGuid, "0", paths, FbxInstance: binding,
                        Renderer: fbxRenderer is null ? null : new(fbxRenderer.FileId, fbxRenderer.CastShadows, fbxRenderer.ReceiveShadows, fbxRenderer.ReflectionProbeUsage, fbxRenderer.RenderingLayerMask), Layer: binding.Layer));
                    continue;
                }
                if (!source.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) || !visiting.Add(source)) { diagnostics.Add("無法展開循環／未支援 Prefab：" + source); continue; }
                try
                {
                    if (new FileInfo(project.Resolve(source)).Length > 64 * 1024 * 1024) throw new InvalidDataException("Prefab 超過預覽大小。");
                    var modifications = List(At(body, "m_Modification", "m_Modifications"))
                        .Concat(overrides.Where(c => Text(At(c, "target", "guid")) != guidByPath.GetValueOrDefault(path)))
                        .Concat(routed.GetValueOrDefault(prefab.FileId) ?? []).ToList();
                    AddDoc(source, UnityYamlDocument.Load(project.Resolve(source)), key + "/", prefabOwner, key, modifications, depth + 1,
                        List(At(body, "m_Modification", "m_RemovedComponents")).ToList());
                    foreach (var field in new[] { "m_RemovedGameObjects", "m_AddedGameObjects", "m_AddedComponents" })
                        if (List(At(body, "m_Modification", field)).Any()) diagnostics.Add(source + "：Prefab 增刪覆寫尚未完整適配，畫面待核對。");
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException or ArgumentException or YamlDotNet.Core.YamlException) { diagnostics.Add(source + "：" + ex.Message); }
                finally { visiting.Remove(source); }
            }
        }
        var sceneDocument = UnityYamlDocument.Parse(bytes);
        AddDoc(relativePath, sceneDocument, "", "", "", [], 0);
        PreviewEnvironment? environment = null;
        var settings = sceneDocument.Objects.FirstOrDefault(o => o.ClassId == 104);
        if (settings is not null)
        {
            var body = Body(sceneDocument, settings.FileId); var sky = At(body, "m_SkyboxMaterial"); var custom = At(body, "m_CustomReflection");
            PreviewLightingReflection? baked = null;
            var lightmaps = sceneDocument.Objects.FirstOrDefault(o => o.ClassId == 157);
            if (lightmaps is not null)
            {
                var pointer = At(Body(sceneDocument, lightmaps.FileId), "m_LightingDataAsset"); var lightingPath = Resolve(Text(At(pointer, "guid")));
                if (lightingPath is not null)
                {
                    try { baked = NativeLightingReflectionReader.Read(project, lightingPath, Text(At(pointer, "fileID")), guidByPath.GetValueOrDefault(relativePath) ?? "", Resolve, ct); }
                    catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException or ArgumentException) { diagnostics.Add("環境反射資料：" + ex.Message); }
                }
            }
            environment = new(Text(At(sky, "guid")), Text(At(sky, "fileID")), Resolve(Text(At(sky, "guid"))), (int)Long(At(body, "m_DefaultReflectionMode")),
                Float(At(body, "m_ReflectionIntensity"), 1), Text(At(custom, "guid")), Text(At(custom, "fileID")), Resolve(Text(At(custom, "guid"))), baked);
        }
        return new(relativePath, nodes.ToArray(), materials.Values.ToArray(), diagnostics.ToArray(), terrains.Values.ToArray(), environment, prefabOverrides.ToArray());
    }
    private static void ApplyOverride(YamlMappingNode body, string property, string value, YamlNode? reference, HashSet<string> diagnostics, string path)
    {
        var segments = property.Replace(".Array.data[", ".[", StringComparison.Ordinal).Split('.'); YamlNode node = body;
        for (var i = 0; i < segments.Length; i++)
        {
            var last = i == segments.Length - 1; var part = segments[i];
            if (node is YamlSequenceNode list && part.StartsWith('[') && int.TryParse(part.Trim('[', ']'), out var n) && n >= 0 && n < list.Children.Count)
            { if (last && reference is YamlMappingNode) list.Children[n] = reference; else node = list.Children[n]; continue; }
            if (node is not YamlMappingNode map || !map.Children.TryGetValue(new YamlScalarNode(part), out var next)) return;
            if (last)
            {
                if (next is YamlScalarNode) map.Children[new YamlScalarNode(part)] = new YamlScalarNode(value);
                else if (next is YamlMappingNode && reference is YamlMappingNode) map.Children[new YamlScalarNode(part)] = reference;
                return;
            }
            node = next;
        }
    }
}

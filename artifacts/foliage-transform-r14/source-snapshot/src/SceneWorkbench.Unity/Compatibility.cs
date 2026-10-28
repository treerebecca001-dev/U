using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SceneWorkbench.Core;

namespace SceneWorkbench.Unity;

public sealed record UnityVersion(int Major, int Minor, int Patch, string Text, char Channel = 'f', int Release = 0) : IComparable<UnityVersion>
{
    private static readonly Regex Pattern = new(@"\A(\d+)\.(\d+)(?:\.(\d+)(?:([abfp])(\d+))?)?\z", RegexOptions.CultureInvariant);

    public static UnityVersion Parse(string text)
    {
        var match = Pattern.Match(text.Trim());
        if (!match.Success)
            throw new InvalidDataException($"無法辨識 Unity 版本：{text}");
        return new UnityVersion(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
            match.Groups[3].Success ? int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) : 0, text.Trim(),
            match.Groups[4].Success ? match.Groups[4].Value[0] : 'f',
            match.Groups[5].Success ? int.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture) : 0);
    }

    public int CompareTo(UnityVersion? other)
    {
        if (other is null) return 1;
        var major = Major.CompareTo(other.Major);
        if (major != 0) return major;
        var minor = Minor.CompareTo(other.Minor);
        if (minor != 0) return minor;
        var patch = Patch.CompareTo(other.Patch);
        if (patch != 0) return patch;
        var channel = "abfp".IndexOf(Channel).CompareTo("abfp".IndexOf(other.Channel));
        return channel != 0 ? channel : Release.CompareTo(other.Release);
    }
}

public sealed record CompatibilitySnapshot(string EditorVersion, IReadOnlyList<PackageSnapshot> Packages, IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool CanWriteKnownYaml => !Diagnostics.Any(item => item.Severity == Severity.Blocking);
}

public static class Compatibility
{
    private static readonly Regex EditorPattern = new(@"^m_EditorVersion:\s*(\S+)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Sources = new(StringComparer.Ordinal) { "registry", "builtin", "git", "local", "embedded" };

    public static CompatibilitySnapshot Read(ProjectSession project) => Read(project, null);

    // Candidate evaluation is internal to explicit baseline preparation, never a write-gate override.
    internal static CompatibilitySnapshot Read(ProjectSession project, UnityVersion? candidateEditor)
    {
        var diagnostics = new List<Diagnostic>();
        var versionText = File.ReadAllText(project.Resolve("ProjectSettings/ProjectVersion.txt"));
        var editorMatch = EditorPattern.Match(versionText);
        if (!editorMatch.Success)
            throw new InvalidDataException("ProjectVersion.txt 沒有有效的 m_EditorVersion。");
        var editor = candidateEditor ?? UnityVersion.Parse(editorMatch.Groups[1].Value);
        if (!YamlEditProfile.Supports(editor))
            diagnostics.Add(new Diagnostic("UNVALIDATED_EDITOR_PROFILE", Severity.Blocking,
                $"目前只啟用 Unity 2021.3／2022.3 穩定版的已知局部 YAML 欄位；{editor.Text} 尚未建立回寫相容設定。", "ProjectSettings/ProjectVersion.txt"));

        var manifestPath = project.Resolve("Packages/manifest.json");
        if (!File.Exists(manifestPath))
            throw new InvalidDataException("缺少 Packages/manifest.json。");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        if (!manifest.RootElement.TryGetProperty("dependencies", out var declared))
            throw new InvalidDataException("manifest.json 缺少 dependencies。");
        var declarations = declared.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.GetString() ?? "", StringComparer.Ordinal);
        foreach (var item in declared.EnumerateObject())
        {
            var declaration = item.Value.GetString() ?? "";
            if (!declaration.StartsWith("file:", StringComparison.Ordinal)) continue;
            var candidate = SafePath.Canonical(Path.Combine(project.Root, "Packages", declaration[5..]));
            if (!SafePath.IsWithin(project.Root, candidate))
            {
                diagnostics.Add(new Diagnostic("EXTERNAL_LOCAL_PACKAGE", Severity.Blocking,
                    $"{item.Name} 依賴專案外的本機路徑，複本不能獨立重現。", "Packages/manifest.json"));
                continue;
            }
            SafePath.RejectLinks(candidate);
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                diagnostics.Add(new Diagnostic("MISSING_LOCAL_PACKAGE", Severity.Blocking,
                    $"{item.Name} 的本機套件不存在。", SafePath.Relative(project.Root, candidate)));
        }

        var packages = new List<PackageSnapshot>();
        var lockPath = project.Resolve("Packages/packages-lock.json");
        if (!File.Exists(lockPath))
        {
            diagnostics.Add(new Diagnostic("MISSING_PACKAGE_LOCK", Severity.Blocking, "缺少 packages-lock.json，不能固定套件版本。", "Packages/packages-lock.json"));
            return new CompatibilitySnapshot(editor.Text, packages, diagnostics);
        }
        using var locked = JsonDocument.Parse(File.ReadAllBytes(lockPath));
        var cacheRoot = project.Resolve("Library/PackageCache");
        var cacheDirectories = Directory.Exists(cacheRoot) ? Directory.GetDirectories(cacheRoot) : [];
        var dependencies = locked.RootElement.GetProperty("dependencies").EnumerateObject().ToDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);
        var embeddedNames = Directory.GetDirectories(project.Resolve("Packages")).Where(path => File.Exists(SafePath.Resolve(path, "package.json")))
            .Select(Path.GetFileName).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var (name, declaration) in declarations)
        {
            // Unity gives embedded package contents priority over registry declarations.
            if (embeddedNames.Contains(name)) continue;
            if (!dependencies.TryGetValue(name, out var entry))
                diagnostics.Add(new Diagnostic("DECLARED_PACKAGE_NOT_LOCKED", Severity.Blocking,
                    $"{name} 已宣告但未列入鎖定檔，無法確認套件基準。", "Packages/manifest.json"));
            else if (!entry.TryGetProperty("version", out var lockedVersion) || lockedVersion.GetString() != declaration)
                diagnostics.Add(new Diagnostic("MANIFEST_LOCK_MISMATCH", Severity.Blocking,
                    $"{name} 的宣告與鎖定版本不一致；請先在驗證環境解析套件。", "Packages/manifest.json"));
        }
        foreach (var (name, entry) in dependencies)
            if (!declarations.ContainsKey(name) && !embeddedNames.Contains(name)
                && entry.TryGetProperty("depth", out var depth) && depth.GetInt32() == 0)
                diagnostics.Add(new Diagnostic("UNDECLARED_DIRECT_PACKAGE", Severity.Blocking,
                    $"鎖定檔中的直接套件 {name} 已不在專案宣告中。", "Packages/packages-lock.json"));
        foreach (var name in dependencies.Keys.Concat(declarations.Keys).Concat(embeddedNames).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var hasLock = dependencies.TryGetValue(name, out var metadata);
            var source = hasLock && metadata.TryGetProperty("source", out var sourceElement) ? sourceElement.GetString() ?? "unknown" : "unknown";
            var version = hasLock && metadata.TryGetProperty("version", out var versionElement) ? versionElement.GetString() ?? "unknown" : declarations.GetValueOrDefault(name, "unknown");
            if (!Sources.Contains(source)) source = "unknown";
            // Git/local declarations may contain private URLs. Reports retain only source kind and cached package version.
            var safeVersion = Regex.IsMatch(version, @"\A[0-9a-zA-Z.+_-]+\z", RegexOptions.CultureInvariant) ? version : "resolved-from-cache";
            var embedded = project.Resolve("Packages/" + name + "/package.json");
            string? resolved = File.Exists(embedded) ? embedded : null;
            if (resolved is not null) source = "embedded";
            if (resolved is null)
            {
                var matches = cacheDirectories.Where(path => Path.GetFileName(path).StartsWith(name + "@", StringComparison.Ordinal)).ToArray();
                if (matches.Length == 1)
                    resolved = SafePath.Resolve(matches[0], "package.json");
                else if (matches.Length > 1)
                {
                    var exact = matches.Where(path => Path.GetFileName(path) == name + "@" + version).ToArray();
                    if (exact.Length == 1) resolved = SafePath.Resolve(exact[0], "package.json");
                    else diagnostics.Add(new Diagnostic("AMBIGUOUS_PACKAGE_CACHE", Severity.Blocking, $"{name} 有多個無法確定的快取版本。"));
                }
            }
            string? minimum = null, minimumRelease = null;
            var packageDependencies = new Dictionary<string, string>(StringComparer.Ordinal);
            var isFeature = false;
            if (resolved is not null && File.Exists(resolved))
            {
                SafePath.RejectLinks(resolved);
                using var packageJson = JsonDocument.Parse(File.ReadAllBytes(resolved));
                isFeature = packageJson.RootElement.TryGetProperty("type", out var packageType) && packageType.GetString() == "feature";
                if (packageJson.RootElement.TryGetProperty("name", out var actualName) && actualName.GetString() != name)
                    diagnostics.Add(new Diagnostic("PACKAGE_NAME_MISMATCH", Severity.Blocking, $"套件內容與索引名稱不符：{name}", SafePath.Relative(project.Root, resolved)));
                if (packageJson.RootElement.TryGetProperty("version", out var actualVersion))
                {
                    var actual = actualVersion.GetString() ?? "unknown";
                    if (Regex.IsMatch(actual, @"\A[0-9a-zA-Z.+_-]+\z", RegexOptions.CultureInvariant))
                    {
                        if (source is "registry" or "builtin" && actual != version)
                            diagnostics.Add(new Diagnostic("PACKAGE_VERSION_MISMATCH", Severity.Blocking, $"{name} 鎖定 {safeVersion}，快取卻是 {actual}。", SafePath.Relative(project.Root, resolved)));
                        safeVersion = actual;
                    }
                }
                if (packageJson.RootElement.TryGetProperty("unity", out var requirement))
                {
                    minimum = requirement.GetString();
                    minimumRelease = packageJson.RootElement.TryGetProperty("unityRelease", out var release) ? release.GetString() : null;
                    var fullMinimum = minimum + (string.IsNullOrEmpty(minimumRelease) ? "" : "." + minimumRelease);
                    if (minimum is not null && UnityVersion.Parse(fullMinimum).CompareTo(editor) > 0)
                        diagnostics.Add(new Diagnostic("PACKAGE_EDITOR_MISMATCH", Severity.Blocking,
                            $"{name} {safeVersion} 要求 Unity {fullMinimum}，專案記錄為 {editor.Text}。", SafePath.Relative(project.Root, resolved)));
                }
                if (packageJson.RootElement.TryGetProperty("dependencies", out var requiredPackages))
                    foreach (var dependency in requiredPackages.EnumerateObject())
                        packageDependencies.Add(dependency.Name, dependency.Value.GetString() ?? "");
            }
            else if (source != "builtin")
                diagnostics.Add(new Diagnostic("PACKAGE_CONTENT_UNAVAILABLE", Severity.Blocking,
                    $"{name} 尚無可讀取的套件內容；不自動下載或改版。", "Packages/packages-lock.json"));
            packages.Add(new PackageSnapshot(name, safeVersion, source,
                resolved is not null && File.Exists(resolved) ? SafePath.Relative(project.Root, resolved) : null, minimum, minimumRelease, packageDependencies, isFeature));
        }
        PackageGraph.Validate(packages, dependencies, diagnostics);
        var urp = packages.FirstOrDefault(package => package.Name == "com.unity.render-pipelines.universal");
        if (urp is not null
            && (urp.Version.StartsWith("14.", StringComparison.Ordinal) && (editor.Major != 2022 || editor.Minor is not 2 and not 3)
                || urp.Version.StartsWith("12.", StringComparison.Ordinal) && (editor.Major != 2021 || editor.Minor is not 2 and not 3))
            && !diagnostics.Any(item => item.Code == "PACKAGE_EDITOR_MISMATCH" && item.Path?.Contains("com.unity.render-pipelines.universal@", StringComparison.Ordinal) == true))
            diagnostics.Add(new Diagnostic("URP_EDITOR_MISMATCH", Severity.Blocking,
                $"URP {urp.Version} 與 {editor.Text} 的 Editor 系列不一致；URP 12 對應 2021.2／2021.3，URP 14 對應 2022.2／2022.3。", "Packages/packages-lock.json"));
        return new CompatibilitySnapshot(editor.Text, packages, diagnostics);
    }

    public static void RequireKnownYamlWrite(ProjectSession project)
    {
        var compatibility = Read(project);
        if (!compatibility.CanWriteKnownYaml)
            throw new InvalidOperationException("版本／資源基準尚未通過，禁止回寫。請先執行 audit 檢視阻擋原因。");
        var diagnostics = new List<Diagnostic>();
        _ = AssetIndex.Build(project, project.EnumerateSourceFiles(true).ToArray(), diagnostics, compatibility: compatibility);
        if (diagnostics.Any(item => item.Severity == Severity.Blocking))
            throw new InvalidOperationException("GUID／資源完整性基準尚未通過，禁止回寫。請先執行 audit 檢視阻擋原因。");
    }
}

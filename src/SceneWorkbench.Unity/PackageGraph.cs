using System.Text.Json;
using System.Text.RegularExpressions;

namespace SceneWorkbench.Unity;

internal static class PackageGraph
{
    public static void Validate(IReadOnlyList<PackageSnapshot> packages, IReadOnlyDictionary<string, JsonElement> locked,
        List<Diagnostic> diagnostics)
    {
        var resolved = packages.ToDictionary(p => p.Name, StringComparer.Ordinal);
        foreach (var package in packages)
        {
            var required = package.Dependencies ?? new Dictionary<string, string>();
            var lockDependencies = new Dictionary<string, string>(StringComparer.Ordinal);
            if (locked.TryGetValue(package.Name, out var entry) && entry.TryGetProperty("dependencies", out var deps))
                foreach (var dependency in deps.EnumerateObject()) lockDependencies.Add(dependency.Name, dependency.Value.GetString() ?? "");
            // Embedded contents intentionally override stale lock metadata. Other resolved contents must match the lock.
            if (package.MetadataPath is not null && package.Source != "embedded"
                && (required.Count != lockDependencies.Count || required.Any(d => !lockDependencies.TryGetValue(d.Key, out var value)
                    || d.Value != value && !(package.IsFeature && d.Value == "default" && PackageVersion.IsVersion(value)))))
                diagnostics.Add(new Diagnostic("LOCK_DEPENDENCIES_MISMATCH", Severity.Blocking,
                    $"{package.Name} 的實際相依宣告與鎖定檔不一致。", package.MetadataPath));
            foreach (var (name, version) in required)
            {
                if (!resolved.TryGetValue(name, out var dependency))
                {
                    diagnostics.Add(new Diagnostic("MISSING_PACKAGE_DEPENDENCY", Severity.Blocking,
                        $"{package.Name} 所需的 {name} 未解析。", package.MetadataPath));
                    continue;
                }
                var effective = package.IsFeature && version == "default" ? lockDependencies.GetValueOrDefault(name, "") : version;
                if (!PackageVersion.TryCompare(dependency.Version, effective, out var comparison))
                    diagnostics.Add(new Diagnostic("UNVALIDATED_PACKAGE_DEPENDENCY", Severity.Blocking,
                        $"{package.Name} → {name} 的版本條件尚未驗證。", package.MetadataPath));
                else if (comparison < 0)
                    diagnostics.Add(new Diagnostic("PACKAGE_DEPENDENCY_TOO_OLD", Severity.Blocking,
                        $"{package.Name} 要求 {name} {effective}，實際解析為 {dependency.Version}。", package.MetadataPath));
            }
        }
    }
}

internal static class PackageVersion
{
    private static readonly Regex Pattern = new(@"\A(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?\z", RegexOptions.CultureInvariant);
    public static bool IsVersion(string text) => Pattern.IsMatch(text);
    public static bool TryCompare(string left, string right, out int result)
    {
        result = 0;
        var a = Pattern.Match(left); var b = Pattern.Match(right);
        if (!a.Success || !b.Success) return false;
        for (var i = 1; i <= 3; i++)
        {
            result = CompareNumber(a.Groups[i].Value, b.Groups[i].Value);
            if (result != 0) return true;
        }
        var ap = a.Groups[4].Value; var bp = b.Groups[4].Value;
        if (ap.Length == 0 || bp.Length == 0) { result = (ap.Length == 0).CompareTo(bp.Length == 0); return true; }
        var aa = ap.Split('.'); var bb = bp.Split('.');
        for (var i = 0; i < Math.Min(aa.Length, bb.Length); i++)
        {
            var an = aa[i].All(char.IsAsciiDigit); var bn = bb[i].All(char.IsAsciiDigit);
            result = an && bn ? CompareNumber(aa[i], bb[i]) : an != bn ? (an ? -1 : 1) : string.CompareOrdinal(aa[i], bb[i]);
            if (result != 0) return true;
        }
        result = aa.Length.CompareTo(bb.Length); return true;
    }
    private static int CompareNumber(string a, string b) => a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
}

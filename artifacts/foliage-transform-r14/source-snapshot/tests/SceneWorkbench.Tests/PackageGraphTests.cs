using SceneWorkbench.Core;
using SceneWorkbench.Unity;

namespace SceneWorkbench.Tests;

public sealed class PackageGraphTests
{
    [Fact]
    public void AnOlderUrpFamilyIsNotTreatedAsCompatibleJustBecauseItsMinimumIsLower()
    {
        using var fixture = new TestProject(); fixture.Write("ProjectSettings/ProjectVersion.txt", "m_EditorVersion: 2022.3.13f1\n");
        var result = Compatibility.Read(ProjectSession.Open(fixture.Source)); Assert.False(result.CanWriteKnownYaml);
        Assert.Contains(result.Diagnostics, d => d.Code == "URP_EDITOR_MISMATCH");
    }
    [Theory]
    [InlineData("2022.2.0a15", true)]
    [InlineData("2022.2.0a16", false)]
    [InlineData("2022.2.0b1", false)]
    [InlineData("2022.3.13f1", false)]
    public void UnityReleaseMinimumIsIncluded(string editor, bool mismatch)
    {
        using var fixture = new TestProject(); fixture.Write("ProjectSettings/ProjectVersion.txt", "m_EditorVersion: " + editor + "\n");
        fixture.Write("Library/PackageCache/com.unity.render-pipelines.universal@12.1.15/package.json", "{\"name\":\"com.unity.render-pipelines.universal\",\"version\":\"12.1.15\",\"unity\":\"2022.2\",\"unityRelease\":\"0a16\"}");
        Assert.Equal(mismatch, Compatibility.Read(ProjectSession.Open(fixture.Source)).Diagnostics.Any(d => d.Code == "PACKAGE_EDITOR_MISMATCH"));
    }
    [Theory]
    [InlineData("missing", "MISSING_PACKAGE_DEPENDENCY")]
    [InlineData("too-old", "PACKAGE_DEPENDENCY_TOO_OLD")]
    [InlineData("prerelease", "PACKAGE_DEPENDENCY_TOO_OLD")]
    [InlineData("lock-diverged", "LOCK_DEPENDENCIES_MISMATCH")]
    [InlineData("unsupported", "UNVALIDATED_PACKAGE_DEPENDENCY")]
    public void TransitiveGraphFailureBlocksWrites(string scenario, string code)
    {
        using var fixture = new TestProject();
        var actual = scenario == "too-old" ? "1.0.0" : scenario == "prerelease" ? "1.0.1-preview.1" : "1.0.1";
        var requirement = scenario == "unsupported" ? "^1.0.0" : "1.0.1";
        fixture.Write("Library/PackageCache/com.unity.render-pipelines.universal@12.1.15/package.json", "{\"name\":\"com.unity.render-pipelines.universal\",\"version\":\"12.1.15\",\"unity\":\"2021.3\",\"dependencies\":{\"required.dep\":\"" + requirement + "\"}}");
        var locked = scenario == "lock-diverged" ? "1.0.0" : requirement;
        var additional = scenario == "missing" ? "" : ",\"required.dep\":{\"version\":\"" + actual + "\",\"source\":\"builtin\",\"depth\":1,\"dependencies\":{}}";
        fixture.Write("Packages/packages-lock.json", "{\"dependencies\":{\"com.unity.render-pipelines.universal\":{\"version\":\"12.1.15\",\"source\":\"builtin\",\"depth\":0,\"dependencies\":{\"required.dep\":\"" + locked + "\"}}" + additional + "}}");
        var result = Compatibility.Read(ProjectSession.Open(fixture.Source)); Assert.False(result.CanWriteKnownYaml);
        Assert.Contains(result.Diagnostics, d => d.Code == code);
    }
    [Fact]
    public void FeatureDefaultUsesTheLockedResolutionAndNewerTransitiveVersionIsAllowed()
    {
        using var fixture = new TestProject();
        fixture.Write("Packages/manifest.json", "{\"dependencies\":{\"feature\":\"1.0.0\"}}");
        fixture.Write("Packages/packages-lock.json", "{\"dependencies\":{\"feature\":{\"version\":\"1.0.0\",\"source\":\"builtin\",\"depth\":0,\"dependencies\":{\"dep\":\"1.2.0\"}},\"dep\":{\"version\":\"1.2.3\",\"source\":\"builtin\",\"depth\":1,\"dependencies\":{}}}}");
        fixture.Write("Library/PackageCache/feature@1.0.0/package.json", "{\"name\":\"feature\",\"version\":\"1.0.0\",\"type\":\"feature\",\"dependencies\":{\"dep\":\"default\"}}");
        Assert.True(Compatibility.Read(ProjectSession.Open(fixture.Source)).CanWriteKnownYaml);
    }
}

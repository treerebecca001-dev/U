using SceneWorkbench.Core;
using SceneWorkbench.Unity;

namespace SceneWorkbench.Tests;

public sealed class CompatibilityTests
{
    [Fact]
    public void ValidReferenceFamilyAllowsOnlyKnownYamlWrites()
    {
        using var fixture = new TestProject();
        var result = Compatibility.Read(ProjectSession.Open(fixture.Source));
        Assert.True(result.CanWriteKnownYaml);
        Assert.DoesNotContain(result.Diagnostics, item => item.Severity == Severity.Blocking);
    }

    [Fact]
    public void RealWorldUrpMismatchBlocksWritesAndExplainsTheMinimumVersion()
    {
        using var fixture = new TestProject();
        fixture.ConfigureUrp("14.0.9", "2022.3");
        var project = ProjectSession.Open(fixture.Source);
        var result = Compatibility.Read(project);
        Assert.False(result.CanWriteKnownYaml);
        Assert.Contains(result.Diagnostics, item => item.Code == "PACKAGE_EDITOR_MISMATCH" && item.Message.Contains("2022.3", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => Compatibility.RequireKnownYamlWrite(project));
        Assert.Equal("m_EditorVersion: 2021.3.15f1\n", File.ReadAllText(project.Resolve("ProjectSettings/ProjectVersion.txt")));
    }

    [Fact]
    public void MissingLockAndUnvalidatedVersionStayReadOnly()
    {
        using var fixture = new TestProject();
        fixture.Write("ProjectSettings/ProjectVersion.txt", "m_EditorVersion: 6000.0.12f1\n");
        File.Delete(Path.Combine(fixture.Source, "Packages", "packages-lock.json"));
        var result = Compatibility.Read(ProjectSession.Open(fixture.Source));
        Assert.False(result.CanWriteKnownYaml);
        Assert.Contains(result.Diagnostics, item => item.Code == "UNVALIDATED_EDITOR_PROFILE");
        Assert.Contains(result.Diagnostics, item => item.Code == "MISSING_PACKAGE_LOCK");
    }

    [Fact]
    public void MissingLocalArchiveIsReportedWithoutReadingOutsideTheProject()
    {
        using var fixture = new TestProject();
        fixture.Write("Packages/manifest.json", "{\"dependencies\":{\"local.package\":\"file:../../external/private.tgz\"}}");
        var result = Compatibility.Read(ProjectSession.Open(fixture.Source));
        Assert.Contains(result.Diagnostics, item => item.Code == "EXTERNAL_LOCAL_PACKAGE");
    }

    [Fact]
    public void DuplicateGuidIsDetectedInsteadOfChoosingAnAssetArbitrarily()
    {
        using var fixture = new TestProject();
        fixture.Write("Assets/Second.asset.meta", "fileFormatVersion: 2\nguid: AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\n");
        var project = ProjectSession.Open(fixture.Source);
        var diagnostics = new List<Diagnostic>();
        var index = AssetIndex.Build(project, project.EnumerateSourceFiles(false).ToArray(), diagnostics);
        Assert.Null(index.ResolveGuid("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));
        Assert.Equal(1, index.DuplicateGuidCount);
        Assert.Contains(diagnostics, item => item.Code == "DUPLICATE_GUID" && item.Severity == Severity.Blocking);
    }

    [Theory]
    [InlineData("Assets/Examples~/Example.asset")]
    [InlineData("Assets/.hidden/Example.asset")]
    [InlineData("Assets/cvs/Example.asset")]
    [InlineData("Assets/Example.tmp")]
    [InlineData("Assets/Example.asset~")]
    [InlineData("Library/PackageCache/com.unity.render-pipelines.universal@12.1.15/Samples~/Example.asset")]
    public void IgnoredImportNamesStayInTheInventoryWithoutCreatingGuidConflicts(string relative)
    {
        using var fixture = new TestProject();
        fixture.Write(relative, "ignored sample");
        fixture.Write(relative + ".meta", "guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n");
        var project = ProjectSession.Open(fixture.Source);
        var files = project.EnumerateSourceFiles(true).ToArray();
        var diagnostics = new List<Diagnostic>();
        var index = AssetIndex.Build(project, files, diagnostics);
        Assert.Contains(files, path => SafePath.Relative(project.Root, path) == relative);
        Assert.Equal("Assets/Scene.unity", index.ResolveGuid("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")?.Path);
        Assert.DoesNotContain(diagnostics, item => item.Code == "DUPLICATE_GUID");
    }

    [Fact]
    public void OnlyTheResolvedPackageCacheVersionContributesGuids()
    {
        using var fixture = new TestProject();
        fixture.Write("Library/PackageCache/com.unity.render-pipelines.universal@11.0.0/package.json", "{\"name\":\"com.unity.render-pipelines.universal\",\"version\":\"11.0.0\"}");
        fixture.Write("Library/PackageCache/com.unity.render-pipelines.universal@11.0.0/Unused.asset", "stale cache resource");
        fixture.Write("Library/PackageCache/com.unity.render-pipelines.universal@11.0.0/Unused.asset.meta", "guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n");
        fixture.Write("Library/PackageCache/com.unity.render-pipelines.universal@12.1.15/Active.asset", "active resource");
        fixture.Write("Library/PackageCache/com.unity.render-pipelines.universal@12.1.15/Active.asset.meta", "guid: dddddddddddddddddddddddddddddddd\n");
        var project = ProjectSession.Open(fixture.Source);
        var index = AssetIndex.Build(project, project.EnumerateSourceFiles(true).ToArray(), new List<Diagnostic>());
        Assert.Equal(0, index.DuplicateGuidCount);
        Assert.Contains(index.Entries, item => item.Path.EndsWith("/Active.asset", StringComparison.Ordinal));
        Assert.DoesNotContain(index.Entries, item => item.Path.EndsWith("/Unused.asset", StringComparison.Ordinal));
    }

    [Fact]
    public void EmbeddedPackageShadowsItsOldCacheAndUnlistedEmbeddedAssetsAreIndexed()
    {
        using var fixture = new TestProject();
        fixture.Write("Packages/com.unity.render-pipelines.universal/package.json", "{\"name\":\"com.unity.render-pipelines.universal\",\"version\":\"12.1.15\",\"unity\":\"2021.3\"}");
        fixture.Write("Packages/com.unity.render-pipelines.universal/Active.asset", "active embedded resource");
        fixture.Write("Packages/com.unity.render-pipelines.universal/Active.asset.meta", "guid: dddddddddddddddddddddddddddddddd\n");
        fixture.Write("Library/PackageCache/com.unity.render-pipelines.universal@12.1.15/Unused.asset", "unused cache resource");
        fixture.Write("Library/PackageCache/com.unity.render-pipelines.universal@12.1.15/Unused.asset.meta", "guid: dddddddddddddddddddddddddddddddd\n");
        fixture.Write("Packages/embedded.custom/package.json", "{\"name\":\"embedded.custom\",\"version\":\"1.0.0\"}");
        fixture.Write("Packages/embedded.custom/Custom.asset", "custom resource");
        fixture.Write("Packages/embedded.custom/Custom.asset.meta", "guid: eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee\n");
        var project = ProjectSession.Open(fixture.Source);
        var index = AssetIndex.Build(project, project.EnumerateSourceFiles(true).ToArray(), new List<Diagnostic>());
        Assert.Equal(0, index.DuplicateGuidCount);
        Assert.Equal("Packages/com.unity.render-pipelines.universal/Active.asset", index.ResolveGuid("dddddddddddddddddddddddddddddddd")?.Path);
        Assert.Equal("Packages/embedded.custom/Custom.asset", index.ResolveGuid("eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee")?.Path);
    }

    [Fact]
    public void UnlistedEmbeddedPackageMinimumVersionStillBlocksWrites()
    {
        using var fixture = new TestProject();
        fixture.Write("Packages/embedded.custom/package.json", "{\"name\":\"embedded.custom\",\"version\":\"1.0.0\",\"unity\":\"2022.3\"}");
        var result = Compatibility.Read(ProjectSession.Open(fixture.Source));
        Assert.False(result.CanWriteKnownYaml);
        Assert.Contains(result.Diagnostics, item => item.Code == "PACKAGE_EDITOR_MISMATCH" && item.Path == "Packages/embedded.custom/package.json");
        Assert.Contains(result.Packages, item => item.Name == "embedded.custom" && item.Source == "embedded");
    }

    [Fact]
    public void HiddenDirectoriesAreExcludedButTmpFolderNamesRemainImportable()
    {
        using var fixture = new TestProject();
        var hidden = Path.Combine(fixture.Source, "Assets", "HiddenDirectory");
        fixture.Write("Assets/HiddenDirectory/Example.asset", "hidden resource");
        fixture.Write("Assets/HiddenDirectory/Example.asset.meta", "guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n");
        fixture.Write("Assets/HiddenDirectory.meta", "guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\nfolderAsset: yes\n");
        File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
        var temporaryFolder = Path.Combine(fixture.Source, "Assets", "Folder.tmp");
        Directory.CreateDirectory(temporaryFolder);
        fixture.Write("Assets/Folder.tmp.meta", "guid: dddddddddddddddddddddddddddddddd\nfolderAsset: yes\n");
        var project = ProjectSession.Open(fixture.Source);
        var index = AssetIndex.Build(project, project.EnumerateSourceFiles(true).ToArray(), new List<Diagnostic>());
        Assert.Equal(0, index.DuplicateGuidCount);
        Assert.Equal("Assets/Folder.tmp", index.ResolveGuid("dddddddddddddddddddddddddddddddd")?.Path);
    }

    [Theory]
    [InlineData("{\"dependencies\":{\"com.unity.render-pipelines.universal\":\"12.1.15\",\"com.example.required\":\"1.0.0\"}}", "DECLARED_PACKAGE_NOT_LOCKED")]
    [InlineData("{\"dependencies\":{\"com.unity.render-pipelines.universal\":\"14.0.9\"}}", "MANIFEST_LOCK_MISMATCH")]
    [InlineData("{\"dependencies\":{}}", "UNDECLARED_DIRECT_PACKAGE")]
    public void ManifestAndLockDisagreementsBlockTheSameWriteGuard(string manifest, string diagnosticCode)
    {
        using var fixture = new TestProject();
        fixture.Write("Packages/manifest.json", manifest);
        var project = ProjectSession.Open(fixture.Source);
        var result = Compatibility.Read(project);
        Assert.False(result.CanWriteKnownYaml);
        Assert.Contains(result.Diagnostics, item => item.Code == diagnosticCode && item.Severity == Severity.Blocking);
        Assert.Throws<InvalidOperationException>(() => Compatibility.RequireKnownYamlWrite(project));
    }

    [Fact]
    public void OrphanMetaCannotResolveAMissingAssetAndIsPreserved()
    {
        using var fixture = new TestProject();
        var meta = fixture.Write("Assets/Missing.mat.meta", "guid: cccccccccccccccccccccccccccccccc\n");
        var originalHash = Hashing.File(meta);
        var project = ProjectSession.Open(fixture.Source);
        var diagnostics = new List<Diagnostic>();
        var index = AssetIndex.Build(project, project.EnumerateSourceFiles(true).ToArray(), diagnostics);
        Assert.Null(index.ResolveGuid("cccccccccccccccccccccccccccccccc"));
        Assert.Contains(diagnostics, item => item.Code == "META_ASSET_MISSING" && item.Path == "Assets/Missing.mat.meta");
        Assert.True(SceneInspector.Inspect(project, "Assets/Scene.unity", index).UnresolvedGuidCount >= 1);
        Assert.Throws<InvalidOperationException>(() => Compatibility.RequireKnownYamlWrite(project));
        Assert.Equal(originalHash, Hashing.File(meta));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyFolderMetaWithoutFolderAssetFlagResolvesExistingDirectories(bool empty)
    {
        using var fixture = new TestProject();
        Directory.CreateDirectory(Path.Combine(fixture.Source, "Assets", "LegacyFolder"));
        if (!empty) fixture.Write("Assets/LegacyFolder/Preserved.cs", "// original legacy package source");
        var meta = fixture.Write("Assets/LegacyFolder.meta", "fileFormatVersion: 2\nguid: dddddddddddddddddddddddddddddddd\n");
        var before = Hashing.File(meta);
        var project = ProjectSession.Open(fixture.Source);
        var diagnostics = new List<Diagnostic>();
        var index = AssetIndex.Build(project, project.EnumerateSourceFiles(true).ToArray(), diagnostics);
        var entry = index.ResolveGuid("dddddddddddddddddddddddddddddddd");
        Assert.NotNull(entry);
        Assert.True(entry.IsFolder);
        Assert.DoesNotContain(diagnostics, item => item.Severity == Severity.Blocking);
        Compatibility.RequireKnownYamlWrite(project);
        Assert.Equal(before, Hashing.File(meta));
    }

    [Theory]
    [InlineData(false, "guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n")]
    [InlineData(true, "fileFormatVersion: 2\nDefaultImporter: {}\n")]
    public void IgnoredTmpFilesDoNotCreateGuidOrMissingResourceBlockers(bool createFile, string metaText)
    {
        using var fixture = new TestProject();
        if (createFile) fixture.Write("Assets/Temporary.tmp", "temporary original contents");
        var meta = fixture.Write("Assets/Temporary.tmp.meta", metaText);
        var before = Hashing.File(meta);
        var project = ProjectSession.Open(fixture.Source);
        var diagnostics = new List<Diagnostic>();
        var index = AssetIndex.Build(project, project.EnumerateSourceFiles(true).ToArray(), diagnostics);
        Assert.DoesNotContain(index.Entries, item => item.Path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(diagnostics, item => item.Severity == Severity.Blocking);
        Compatibility.RequireKnownYamlWrite(project);
        Assert.Equal(before, Hashing.File(meta));
    }

    [Fact]
    public void ADeclaredMissingFolderEndingInTmpIsStillReported()
    {
        using var fixture = new TestProject();
        fixture.Write("Assets/MissingFolder.tmp.meta", "guid: dddddddddddddddddddddddddddddddd\nfolderAsset: yes\n");
        var project = ProjectSession.Open(fixture.Source);
        var diagnostics = new List<Diagnostic>();
        _ = AssetIndex.Build(project, project.EnumerateSourceFiles(true).ToArray(), diagnostics);
        Assert.Contains(diagnostics, item => item.Code == "META_ASSET_MISSING");
        Assert.Throws<InvalidOperationException>(() => Compatibility.RequireKnownYamlWrite(project));
    }

    [Fact]
    public void ValidEmbeddedOverrideTakesPriorityOverTheDeclaredRegistryVersion()
    {
        using var fixture = new TestProject();
        fixture.Write("Packages/manifest.json", "{\"dependencies\":{\"com.unity.render-pipelines.universal\":\"14.0.9\"}}");
        fixture.Write("Packages/com.unity.render-pipelines.universal/package.json", "{\"name\":\"com.unity.render-pipelines.universal\",\"version\":\"12.1.15\",\"unity\":\"2021.3\"}");
        var result = Compatibility.Read(ProjectSession.Open(fixture.Source));
        Assert.True(result.CanWriteKnownYaml);
        Assert.Contains(result.Packages, package => package.Source == "embedded" && package.Version == "12.1.15");
    }

    [Fact]
    public void FbxReferencesKeepSignedIdsAndFlagMissingSubassetMapping()
    {
        using var fixture = new TestProject();
        fixture.Write("Assets/Model.fbx", "fixture binary placeholder");
        fixture.Write("Assets/Model.fbx.meta", "fileFormatVersion: 2\nguid: cccccccccccccccccccccccccccccccc\nModelImporter:\n  internalIDToNameTable: []\n  fileIdsGeneration: 2\n");
        var project = ProjectSession.Open(fixture.Source);
        var index = AssetIndex.Build(project, project.EnumerateSourceFiles(false).ToArray(), new List<Diagnostic>());
        var scene = SceneInspector.Inspect(project, "Assets/Scene.unity", index);
        var reference = Assert.Single(scene.FbxReferences);
        Assert.Equal("-9223372036854775808", Assert.Single(reference.ReferencedFileIds));
        Assert.False(reference.HasInternalIdTable);
        Assert.Equal(2, reference.FileIdsGeneration);
    }

    [Fact]
    public void AuditOutputsOutsideSourceAndNeverClaimsFullEditorAcceptance()
    {
        using var fixture = new TestProject();
        var bundle = ProjectAuditor.Audit(fixture.Source);
        Assert.False(bundle.Report.FullEditorAccepted);
        Assert.Contains(bundle.Report.Gates, item => item.Id == "native-lighting" && item.Status == GateStatus.NotImplemented);
        Assert.Throws<InvalidOperationException>(() => AuditReportWriter.Write(bundle, Path.Combine(fixture.Source, "reports")));
        var reportRoot = Path.Combine(fixture.Root, "reports");
        AuditReportWriter.Write(bundle, reportRoot);
        Assert.True(File.Exists(Path.Combine(reportRoot, "baseline.md")));
        Assert.Throws<IOException>(() => AuditReportWriter.Write(bundle, reportRoot));
    }

    [Theory]
    [InlineData("2021.3.15f1", "2022.3", -1)]
    [InlineData("2022.3.0f1", "2022.3", 1)] // A minimum with no release suffix sorts before f1.
    [InlineData("6000.0.12f1", "2022.3", 1)]
    [InlineData("2022.2.0a15", "2022.2.0a16", -1)]
    [InlineData("2022.2.0b1", "2022.2.0a16", 1)]
    [InlineData("2022.3.13f1", "2022.3.13f2", -1)]
    [InlineData("2022.3.13p1", "2022.3.13f1", 1)]
    public void UnityVersionComparisonUsesNumericComponents(string first, string second, int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(UnityVersion.Parse(first).CompareTo(UnityVersion.Parse(second))));
    }
}

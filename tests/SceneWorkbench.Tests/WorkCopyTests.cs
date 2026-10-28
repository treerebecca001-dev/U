using SceneWorkbench.Core;

namespace SceneWorkbench.Tests;

public sealed class WorkCopyTests
{
    [Fact]
    public async Task CopiesAllSourceFilesAndCacheWithHashesAndEmptyFolders()
    {
        using var fixture = new TestProject();
        var sourceHash = Hashing.File(Path.Combine(fixture.Source, "Assets", "Scene.unity"));
        var copy = await fixture.CopyAsync();
        Assert.Equal(CopyState.Complete, copy.Info.State);
        Assert.True(copy.Info.IncludesPackageCache);
        Assert.True(Directory.Exists(copy.Project.Resolve("Assets/EmptyFolder")));
        Assert.True(File.Exists(copy.Project.Resolve("Assets/Scene.unity.meta")));
        var verification = copy.Verify(true);
        Assert.True(verification.IsValid, string.Join("\n", verification.Differences));
        Assert.Equal(copy.Info.FileCount, verification.FilesChecked);
        Assert.Equal(sourceHash, Hashing.File(Path.Combine(fixture.Source, "Assets", "Scene.unity")));
    }

    [Fact]
    public async Task HiddenUnicodeResourcesRemainIncludedInTheWorkingCopy()
    {
        using var fixture = new TestProject();
        const string relative = "Assets/隱藏資源/.keep.asset";
        fixture.Write(relative, "hidden resource");
        var original = Path.Combine(fixture.Source, relative);
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(original, File.GetAttributes(original) | FileAttributes.Hidden);
            var folder = Path.GetDirectoryName(original)!;
            File.SetAttributes(folder, File.GetAttributes(folder) | FileAttributes.Hidden);
        }
        var copy = await fixture.CopyAsync();
        Assert.Equal("hidden resource", File.ReadAllText(copy.Project.Resolve(relative)));
        Assert.True(copy.Verify(true).IsValid);
    }

    [Fact]
    public async Task CacheCanBeOmittedExplicitlyWithoutOmittingProjectSources()
    {
        using var fixture = new TestProject();
        var copy = await fixture.CopyAsync(false);
        Assert.False(copy.Info.IncludesPackageCache);
        Assert.False(Directory.Exists(Path.Combine(copy.Project.Root, "Library")));
        Assert.True(copy.Verify(true).IsValid);
    }

    [Fact]
    public async Task SameNestedAndExistingDestinationsAreRejectedWithoutOverwriting()
    {
        using var fixture = new TestProject();
        await Assert.ThrowsAsync<InvalidOperationException>(() => WorkCopy.CreateAsync(fixture.Source, fixture.Source));
        await Assert.ThrowsAsync<InvalidOperationException>(() => WorkCopy.CreateAsync(fixture.Source, Path.Combine(fixture.Source, "copy")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => WorkCopy.CreateAsync(fixture.Source, fixture.Root));
        Directory.CreateDirectory(fixture.Destination);
        var sentinel = Path.Combine(fixture.Destination, "keep.txt");
        File.WriteAllText(sentinel, "existing user data");
        await Assert.ThrowsAsync<IOException>(() => fixture.CopyAsync());
        Assert.Equal("existing user data", File.ReadAllText(sentinel));
    }

    [Fact]
    public void SourceProjectAndEscapingPathsCannotAcquireWriteAccess()
    {
        using var fixture = new TestProject();
        Assert.Throws<InvalidOperationException>(() => WorkCopy.Open(fixture.Source));
        Assert.Throws<InvalidOperationException>(() => SafePath.Resolve(fixture.Source, "../outside.txt"));
        Assert.Throws<InvalidOperationException>(() => SafePath.Resolve(fixture.Source, "Assets/../../outside.txt"));
        Assert.Throws<InvalidOperationException>(() => SafePath.Resolve(fixture.Source, "Assets/file.txt:stream"));
    }

    [Fact]
    public async Task JunctionInSourceIsRejectedAndTargetIsUntouched()
    {
        using var fixture = new TestProject();
        var other = Path.Combine(fixture.Root, "other");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "sentinel.txt"), "keep");
        TestProject.MakeDirectoryLink(Path.Combine(fixture.Source, "Assets", "Linked"), other);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CopyAsync());
        Assert.False(Directory.Exists(fixture.Destination));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(other, "sentinel.txt")));
    }

    [Fact]
    public async Task CancellationLeavesAnUnwritablePartialCopy()
    {
        using var fixture = new TestProject();
        using var cancellation = new CancellationTokenSource();
        var progress = new ImmediateProgress<CopyProgress>(_ => cancellation.Cancel());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WorkCopy.CreateAsync(fixture.Source, fixture.Destination, true, progress, cancellation.Token));
        var info = JsonFile.Read<WorkCopyInfo>(Path.Combine(fixture.Destination, WorkCopy.InfoPath));
        Assert.Equal(CopyState.Failed, info.State);
        Assert.Throws<InvalidDataException>(() => WorkCopy.Open(fixture.Destination));
    }

    [Fact]
    public async Task ChangedSourceAndTamperedManifestAreDetected()
    {
        using var fixture = new TestProject();
        var copy = await fixture.CopyAsync();
        fixture.Write("Assets/Scene.unity", "external change");
        Assert.Contains(copy.Verify(true).Differences, item => item.StartsWith("來源已改動", StringComparison.Ordinal));
        File.AppendAllText(copy.Project.Resolve(WorkCopy.ManifestPath), "tamper\n");
        Assert.Throws<InvalidDataException>(() => WorkCopy.Open(copy.Project.Root));
    }

    [Fact]
    public async Task JunctionInsertedAfterCopyCannotRedirectAnEdit()
    {
        using var fixture = new TestProject();
        var copy = await fixture.CopyAsync();
        TestProject.MakeDirectoryLink(Path.Combine(copy.Project.Root, "Assets", "SourceRedirect"), Path.Combine(fixture.Source, "Assets"));
        Assert.Throws<InvalidOperationException>(() => copy.ResolveEditable("Assets/SourceRedirect/Scene.unity"));
    }

    [Fact]
    public async Task VerificationDetectsNewFilesOutsideTheOriginalManifest()
    {
        using var fixture = new TestProject();
        var copy = await fixture.CopyAsync();
        fixture.Write("Assets/NewSource.asset", "new source");
        File.WriteAllText(copy.Project.Resolve("Assets/NewCopy.asset"), "new copy");
        var result = copy.Verify(true);
        Assert.False(result.IsValid);
        Assert.Contains(result.Differences, value => value == "來源新增檔案：Assets/NewSource.asset");
        Assert.Contains(result.Differences, value => value == "複本新增檔案：Assets/NewCopy.asset");
    }

    [Fact]
    public async Task ParallelVerificationReportsEveryDifferenceAndKeepsAccurateCounts()
    {
        using var fixture = new TestProject();
        for (var index = 0; index < 64; index++)
            fixture.Write($"Assets/Verification/{index:D3}.txt", $"original {index}");
        var copy = await fixture.CopyAsync();
        fixture.Write("Assets/Verification/001.txt", "changed source");
        File.WriteAllText(copy.Project.Resolve("Assets/Verification/002.txt"), "changed copy");
        File.Delete(Path.Combine(fixture.Source, "Assets", "Verification", "003.txt"));
        File.Delete(copy.Project.Resolve("Assets/Verification/004.txt"));
        fixture.Write("Assets/Verification/ExtraSource.txt", "new source");
        File.WriteAllText(copy.Project.Resolve("Assets/Verification/ExtraCopy.txt"), "new copy");
        var counts = new List<int>();
        var result = copy.Verify(true, new ImmediateProgress<CopyProgress>(item => counts.Add(item.FilesDone)));
        Assert.False(result.IsValid);
        Assert.Equal(copy.Info.FileCount, result.FilesChecked);
        Assert.Equal(copy.Info.FileCount, result.SourceFilesChecked);
        Assert.Equal(Enumerable.Range(1, copy.Info.FileCount), counts);
        var expected = new[]
        {
            "來源已改動：Assets/Verification/001.txt", "複本內容不同：Assets/Verification/002.txt",
            "來源缺檔：Assets/Verification/003.txt", "複本缺檔：Assets/Verification/004.txt",
            "來源新增檔案：Assets/Verification/ExtraSource.txt", "複本新增檔案：Assets/Verification/ExtraCopy.txt"
        };
        Assert.Equal(expected.Order(StringComparer.Ordinal), result.Differences);
    }

    [Fact]
    public async Task CancellingVerificationDoesNotInvalidateOrChangeTheCompleteCopy()
    {
        using var fixture = new TestProject();
        var copy = await fixture.CopyAsync();
        var sceneHash = Hashing.File(copy.Project.Resolve("Assets/Scene.unity"));
        using var cancellation = new CancellationTokenSource();
        Assert.ThrowsAny<OperationCanceledException>(() => copy.Verify(true,
            new ImmediateProgress<CopyProgress>(_ => cancellation.Cancel()), cancellation.Token));
        Assert.Equal(CopyState.Complete, WorkCopy.Open(copy.Project.Root).Info.State);
        Assert.Equal(sceneHash, Hashing.File(copy.Project.Resolve("Assets/Scene.unity")));
        Assert.True(copy.Verify(true).IsValid);
    }

    [Fact]
    public async Task ParallelVerificationRejectsJunctionsBeforeReadingTheirTargets()
    {
        using var fixture = new TestProject();
        fixture.Write("Assets/Sub/Sentinel.asset", "original content");
        var copy = await fixture.CopyAsync();
        var copiedFile = copy.Project.Resolve("Assets/Sub/Sentinel.asset");
        var copiedFolder = Path.GetDirectoryName(copiedFile)!;
        Assert.True(SafePath.IsWithin(fixture.Root, copiedFolder));
        File.Delete(copiedFile);
        Directory.Delete(copiedFolder, false);
        var other = Path.Combine(fixture.Root, "outside-project");
        Directory.CreateDirectory(other);
        var sentinel = Path.Combine(other, "Sentinel.asset");
        File.WriteAllText(sentinel, "do not read through a junction");
        TestProject.MakeDirectoryLink(copiedFolder, other);
        using (var locked = new FileStream(sentinel, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var error = Assert.Throws<AggregateException>(() => copy.Verify(true));
            Assert.All(error.Flatten().InnerExceptions, item => Assert.IsType<InvalidOperationException>(item));
            Assert.Contains(error.Flatten().InnerExceptions, item => item is InvalidOperationException && item.Message.Contains("junction", StringComparison.Ordinal));
            Assert.Throws<InvalidOperationException>(() => copy.ReadManifest().ToArray());
        }
        Assert.Equal("do not read through a junction", File.ReadAllText(sentinel));
    }
}

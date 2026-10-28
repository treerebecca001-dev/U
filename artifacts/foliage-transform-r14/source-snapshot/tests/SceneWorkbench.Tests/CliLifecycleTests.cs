using System.Diagnostics;
using System.Numerics;
using System.Text;
using System.Text.Json;
using SceneWorkbench.Core;
using SceneWorkbench.Unity;

namespace SceneWorkbench.Tests;

public sealed class CliLifecycleTests
{
    [Theory]
    [InlineData("99")]
    [InlineData("{unrecognized: 2}")]
    public async Task Strict2022CliRejectsUnknownTransformSchemaWithoutChangingBytes(string version)
    {
        using var fixture = new TestProject(); fixture.ConfigureUrp("14.0.9", "2022.3");
        fixture.Write("ProjectSettings/ProjectVersion.txt", "m_EditorVersion: 2022.3.13f1\n");
        fixture.Write("Assets/Scene.unity", TestProject.SceneText().Replace("Transform:\n", "Transform:\n  serializedVersion: " + version + "\n", StringComparison.Ordinal));
        await fixture.CopyAsync(); var original = File.ReadAllBytes(Path.Combine(fixture.Destination, "Assets/Scene.unity"));
        var result = await Run("patch-transform", "--project", fixture.Destination, "--scene", "Assets/Scene.unity", "--file-id", "-2745811161023527127", "--position", "5,6,7");
        Assert.Equal(1, result.ExitCode); Assert.Contains("Transform", result.StdErr, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(fixture.Destination, "Assets/Scene.unity")));
        var session = new SceneEditSession(ProjectSession.Open(fixture.Destination), "Assets/Scene.unity"); Assert.False(session.CanEditItem(session.Items.Single()));
    }

    [Fact]
    public async Task Prepared2022BaselinePassesLocalAuditAndRealCliSaveWithoutClaimingNativeImport()
    {
        using var fixture = new TestProject(); BaselinePreparationTests.VersionSet(fixture); await fixture.CopyAsync();
        var prepared = Path.Combine(fixture.Root, "prepared"); var proof = Path.Combine(fixture.Root, "preparation");
        var result = await Run("prepare-baseline", "--project", fixture.Destination, "--destination", prepared, "--output", proof);
        Assert.True(result.ExitCode == 0, result.StdErr);
        Assert.Equal("UNVERIFIED", JsonSerializer.Deserialize<JsonElement>(result.StdOut).GetProperty("nativeStatus").GetString());
        var output = Path.Combine(fixture.Root, "audit");
        Assert.Equal(0, (await Run("audit", "--project", prepared, "--output", output)).ExitCode);
        var report = JsonFile.Read<ProjectAudit>(Path.Combine(output, "baseline.json"));
        Assert.False(report.BaselineBlocked); Assert.False(report.FullEditorAccepted);
        var saved = await Run("patch-transform", "--project", prepared, "--scene", "Assets/Scene.unity", "--file-id", "-2745811161023527127", "--position", "5,6,7");
        Assert.True(saved.ExitCode == 0, saved.StdErr);
        Assert.Equal(new Vector3(5, 6, 7), UnityYamlDocument.Load(Path.Combine(prepared, "Assets/Scene.unity")).GetPosition(-2745811161023527127));
        var version = File.ReadAllText(Path.Combine(fixture.Destination, "ProjectSettings/ProjectVersion.txt"));
        Assert.Contains("2021.3.15f1", version, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RealCliCopiesEditsReloadsAndProtectsOriginal()
    {
        using var fixture = new TestProject();
        var sourcePath = Path.Combine(fixture.Source, "Assets", "Scene.unity");
        var original = Hashing.File(sourcePath);
        var copy = await Run("copy", "--project", fixture.Source, "--destination", fixture.Destination);
        Assert.Equal(0, copy.ExitCode);
        var verification = await Run("verify-copy", "--project", fixture.Destination, "--compare-source");
        Assert.Equal(0, verification.ExitCode);
        var preview = await Run("patch-transform", "--project", fixture.Source, "--scene", "Assets/Scene.unity", "--file-id", "-2745811161023527127", "--position", "3.25,4.5,-5.75", "--dry-run");
        Assert.Equal(0, preview.ExitCode);
        Assert.Equal(original, Hashing.File(sourcePath));
        var saved = await Run("patch-transform", "--project", fixture.Destination, "--scene", "Assets/Scene.unity", "--file-id", "-2745811161023527127", "--position", "3.25,4.5,-5.75");
        Assert.True(saved.ExitCode == 0, saved.StdErr);
        var loaded = UnityYamlDocument.Load(Path.Combine(fixture.Destination, "Assets", "Scene.unity"));
        Assert.Equal(new Vector3(3.25f, 4.5f, -5.75f), loaded.GetPosition(-2745811161023527127));
        Assert.Equal(original, Hashing.File(sourcePath));
        var sourceSave = await Run("patch-transform", "--project", fixture.Source, "--scene", "Assets/Scene.unity", "--file-id", "-2745811161023527127", "--position", "0,0,0");
        Assert.Equal(1, sourceSave.ExitCode);
        Assert.Equal(original, Hashing.File(sourcePath));
    }

    [Fact]
    public async Task RealCliBlocksMismatchedProjectAndStillProducesDiagnosticEvidence()
    {
        using var fixture = new TestProject();
        fixture.ConfigureUrp("14.0.9", "2022.3");
        var copy = await Run("copy", "--project", fixture.Source, "--destination", fixture.Destination);
        Assert.Equal(0, copy.ExitCode);
        var copiedScene = Path.Combine(fixture.Destination, "Assets", "Scene.unity");
        var before = Hashing.File(copiedScene);
        var rejected = await Run("patch-transform", "--project", fixture.Destination, "--scene", "Assets/Scene.unity", "--file-id", "-2745811161023527127", "--position", "5,6,7");
        Assert.Equal(1, rejected.ExitCode);
        Assert.Contains("禁止回寫", rejected.StdErr, StringComparison.Ordinal);
        Assert.Equal(before, Hashing.File(copiedScene));
        var output = Path.Combine(fixture.Root, "reports");
        var audit = await Run("audit", "--project", fixture.Destination, "--output", output);
        Assert.Equal(2, audit.ExitCode);
        using var report = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, "baseline.json")));
        Assert.True(report.RootElement.GetProperty("baselineBlocked").GetBoolean());
        Assert.False(report.RootElement.GetProperty("fullEditorAccepted").GetBoolean());
    }

    [Fact]
    public async Task UnknownOptionsAreRejectedBeforeWritingAReport()
    {
        using var fixture = new TestProject();
        var output = Path.Combine(fixture.Root, "reports");
        var result = await Run("audit", "--project", fixture.Source, "--output", output, "--force-upgrade");
        Assert.Equal(1, result.ExitCode);
        Assert.False(Directory.Exists(output));
    }

    [Theory]
    [InlineData("missing-lock", "DECLARED_PACKAGE_NOT_LOCKED")]
    [InlineData("stale-manifest", "MANIFEST_LOCK_MISMATCH")]
    [InlineData("duplicate-guid", "DUPLICATE_GUID")]
    [InlineData("orphan-meta", "META_ASSET_MISSING")]
    public async Task BaselineBlockersRejectRealPersistenceWithoutChangingTheScene(string scenario, string diagnosticCode)
    {
        using var fixture = new TestProject();
        switch (scenario)
        {
            case "missing-lock":
                fixture.Write("Packages/manifest.json", "{\"dependencies\":{\"com.unity.render-pipelines.universal\":\"12.1.15\",\"com.example.required\":\"1.0.0\"}}");
                break;
            case "stale-manifest":
                fixture.Write("Packages/manifest.json", "{\"dependencies\":{\"com.unity.render-pipelines.universal\":\"14.0.9\"}}");
                break;
            case "duplicate-guid":
                fixture.Write("Assets/Second.asset.meta", "guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n");
                break;
            case "orphan-meta":
                fixture.Write("Assets/Missing.mat.meta", "guid: cccccccccccccccccccccccccccccccc\n");
                break;
        }
        await fixture.CopyAsync();
        var scenePath = Path.Combine(fixture.Destination, "Assets", "Scene.unity");
        var before = Hashing.File(scenePath);
        var time = File.GetLastWriteTimeUtc(scenePath);
        var output = Path.Combine(fixture.Root, "baseline");
        var audit = await Run("audit", "--project", fixture.Destination, "--output", output);
        Assert.Equal(2, audit.ExitCode);
        using var report = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, "baseline.json")));
        Assert.Contains(report.RootElement.GetProperty("diagnostics").EnumerateArray(), item => item.GetProperty("code").GetString() == diagnosticCode);
        var preview = await Run("patch-transform", "--project", fixture.Destination, "--scene", "Assets/Scene.unity", "--file-id", "-2745811161023527127", "--position", "7,8,9", "--dry-run");
        Assert.Equal(0, preview.ExitCode);
        var patch = await Run("patch-transform", "--project", fixture.Destination, "--scene", "Assets/Scene.unity", "--file-id", "-2745811161023527127", "--position", "7,8,9");
        Assert.Equal(1, patch.ExitCode);
        Assert.Contains("禁止回寫", patch.StdErr, StringComparison.Ordinal);
        Assert.Equal(before, Hashing.File(scenePath));
        Assert.Equal(time, File.GetLastWriteTimeUtc(scenePath));
        Assert.Equal(before, Hashing.File(Path.Combine(fixture.Source, "Assets", "Scene.unity")));
    }

    private static async Task<ProcessResult> Run(params string[] arguments)
    {
        var workspace = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(workspace, "SceneWorkbench.slnx")))
            workspace = Path.GetDirectoryName(workspace) ?? throw new InvalidOperationException("找不到 CLI 工作區。");
        var executable = Environment.GetEnvironmentVariable("SCENE_WORKBENCH_CLI");
        var start = new ProcessStartInfo
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        if (!string.IsNullOrWhiteSpace(executable))
            start.FileName = executable;
        else
        {
            start.FileName = Path.Combine(workspace, ".tools", "dotnet", "dotnet.exe");
            if (!File.Exists(start.FileName)) start.FileName = "dotnet";
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            start.ArgumentList.Add(Path.Combine(workspace, "src", "SceneWorkbench.Cli", "bin", configuration, "net10.0", "SceneWorkbench.dll"));
        }
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("無法啟動 CLI。");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw; }
        return new ProcessResult(process.ExitCode, await stdout, await stderr);
    }

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
}

using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using SceneWorkbench.Core;
using SceneWorkbench.Unity;

Console.OutputEncoding = new UTF8Encoding(false);
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
try
{
    return await Run(args, cancellation.Token);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("已取消。未完成的複本不會取得回寫資格。");
    return 130;
}
catch (Exception error)
{
    Console.Error.WriteLine($"{error.GetType().Name}: {error.Message}");
    return 1;
}

static async Task<int> Run(string[] args, CancellationToken cancellationToken)
{
    if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
    {
        Console.WriteLine("""
            SceneWorkbench 0.1 — 階段 0／1 相容性與檔案保護工具

            audit           --project <Unity 專案> --output <新報告資料夾> [--classdata <classdata.tpk>]
            copy            --project <來源專案> --destination <新複本資料夾> [--without-cache]
            verify-copy     --project <工作複本> [--compare-source]
            probe           --project <Unity 專案> --output <新輸出資料夾> [--asset <相對路徑>] [--classdata <classdata.tpk>]
            patch-transform --project <工作複本> --scene <相對路徑> --file-id <有號 64 位元 ID> --position <x,y,z> [--dry-run]
            recover         --project <工作複本>
            prepare-baseline --project <工作複本> --destination <新完整複本> --output <新報告資料夾>
            verify-baseline  --project <準備好的複本> [--compare-source]

            audit：0 = 本機基準檢查無阻擋；2 = 基準被阻擋。兩者都不代表完整編輯器驗收通過。
            probe：0 = 本機格式檢查通過；2 = 證據不完整；完整 Unity 原生互通仍需另外驗證。
            其他命令：0 = 成功；1 = 失敗或衝突；130 = 使用者取消。
            """);
        return 0;
    }
    if (args[0] == "--version") { Console.WriteLine("SceneWorkbench 0.1.0-stage0"); return 0; }
    var allowed = args[0] switch
    {
        "audit" => new[] { "project", "output", "classdata" },
        "copy" => ["project", "destination", "without-cache"],
        "verify-copy" => ["project", "compare-source"],
        "probe" => ["project", "output", "asset", "classdata"],
        "patch-transform" => ["project", "scene", "file-id", "position", "dry-run"],
        "recover" => ["project"],
        "prepare-baseline" => ["project", "destination", "output"],
        "verify-baseline" => ["project", "compare-source"],
        _ => throw new ArgumentException("未知命令。執行 help 查看用法。")
    };
    var options = ParseOptions(args[1..], allowed);
    string Required(string key) => options.TryGetValue(key, out var value) && value is not null ? value : throw new ArgumentException($"缺少 --{key}。");
    var projectRoot = Required("project");
    var log = new InlineProgress<string>(message => Console.Error.WriteLine(message));
    switch (args[0])
    {
        case "verify-baseline":
        {
            var report = BaselinePreparation.Verify(projectRoot, options.ContainsKey("compare-source"));
            Console.WriteLine(JsonSerializer.Serialize(new { report.TargetEditor, report.OfflineStatus, report.NativeStatus, packages = report.Packages.Count, sourceCompared = options.ContainsKey("compare-source") }, JsonFile.Options));
            return 0;
        }
        case "prepare-baseline":
        {
            var report = await BaselinePreparation.PrepareAsync(projectRoot, Required("destination"), Required("output"), CopyProgress(), cancellationToken, log);
            Console.WriteLine(JsonSerializer.Serialize(new { report.PreparedRoot, report.TargetEditor, report.OfflineStatus, report.NativeStatus, versionIssues = report.Issues.Count }, JsonFile.Options));
            return 0;
        }
        case "audit":
        {
            var output = Required("output");
            // Check output boundaries before doing an expensive read of the project.
            SafePath.RequireDisjoint(projectRoot, output);
            if (Directory.Exists(output) || File.Exists(output)) throw new IOException("報告目的地已存在。");
            var bundle = ProjectAuditor.Audit(projectRoot, options.GetValueOrDefault("classdata"), log, cancellationToken);
            AuditReportWriter.Write(bundle, output);
            Console.WriteLine($"基準：{(bundle.Report.BaselineBlocked ? "BLOCKED" : "PASSED LOCAL CHECKS")}");
            Console.WriteLine($"報告：{Path.GetFullPath(Path.Combine(output, "baseline.md"))}");
            Console.WriteLine($"ASE：{bundle.Report.Shaders.Count}；GUID：{bundle.Report.Inventory.IndexedGuids:N0}；阻擋：{bundle.Report.Diagnostics.Count(item => item.Severity == Severity.Blocking)}");
            return bundle.Report.BaselineBlocked ? 2 : 0;
        }
        case "copy":
        {
            var copy = await WorkCopy.CreateAsync(projectRoot, Required("destination"), !options.ContainsKey("without-cache"), CopyProgress(), cancellationToken);
            Console.WriteLine($"工作複本：{copy.Project.Root}");
            Console.WriteLine($"已複製並記錄 SHA-256：{copy.Info.FileCount:N0} 檔，{copy.Info.TotalBytes:N0} bytes。");
            return 0;
        }
        case "verify-copy":
        {
            var result = WorkCopy.Open(projectRoot).Verify(options.ContainsKey("compare-source"), CopyProgress(), cancellationToken);
            Console.WriteLine(JsonSerializer.Serialize(result, JsonFile.Options));
            return result.IsValid ? 0 : 1;
        }
        case "probe":
        {
            var report = FormatProbe.Run(projectRoot, Required("output"), options.GetValueOrDefault("asset"), options.GetValueOrDefault("classdata"), log);
            var localPass = report.Yaml.Count > 0 && report.Yaml.All(item => item.NoOpByteIdentical && item.ChangedPositionReloaded && item.OtherObjectsUnchanged && item.UndoByteIdentical && item.SourceUnchanged)
                && report.Binary.Count > 0 && report.Binary.All(item => item.SourceUnchanged && item.SameObjectFields);
            Console.WriteLine($"本機格式檢查：{(localPass ? "PASSED" : "INCOMPLETE")}");
            Console.WriteLine("完整原生互通關卡：NOT PASSED；未啟動 Unity 驗證。");
            return localPass ? 0 : 2;
        }
        case "patch-transform":
        {
            var project = ProjectSession.Open(projectRoot);
            var relative = Required("scene");
            if (!relative.StartsWith("Assets/", StringComparison.Ordinal) || Path.GetExtension(relative) is not ".unity" and not ".prefab")
                throw new ArgumentException("場景必須是 Assets 內的 .unity 或 .prefab 相對路徑。");
            var fileId = long.Parse(Required("file-id"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            var coordinates = Required("position").Split(',');
            if (coordinates.Length != 3) throw new ArgumentException("position 格式是 x,y,z，使用小數點。");
            var position = new Vector3(float.Parse(coordinates[0], CultureInfo.InvariantCulture), float.Parse(coordinates[1], CultureInfo.InvariantCulture), float.Parse(coordinates[2], CultureInfo.InvariantCulture));
            var document = UnityYamlDocument.Load(project.Resolve(relative));
            var history = new CommandHistory();
            history.Execute(new TransformPositionCommand(document, fileId, position));
            if (options.ContainsKey("dry-run"))
            {
                Console.WriteLine(JsonSerializer.Serialize(new { dryRun = true, writesPerformed = false, fileId = fileId.ToString(CultureInfo.InvariantCulture), document.OriginalSha256, document.Changes }, JsonFile.Options));
                return 0;
            }
            var workCopy = WorkCopy.Open(projectRoot);
            Compatibility.RequireKnownYamlWrite(project);
            YamlEditProfile.RequireTransform(UnityVersion.Parse(Compatibility.Read(project).EditorVersion), document, fileId);
            var result = new SaveTransaction(workCopy).Commit([new FileUpdate(relative, document.OriginalSha256, document.ToBytes())], cancellationToken: cancellationToken);
            Console.WriteLine(JsonSerializer.Serialize(result, JsonFile.Options));
            return 0;
        }
        case "recover":
        {
            var recovered = new SaveTransaction(WorkCopy.Open(projectRoot)).Recover();
            Console.WriteLine(JsonSerializer.Serialize(new { recovered }, JsonFile.Options));
            return 0;
        }
    }
    throw new InvalidOperationException("無法辨識命令。");
}

static Dictionary<string, string?> ParseOptions(string[] args, IReadOnlyCollection<string> allowed)
{
    var flags = new HashSet<string>(StringComparer.Ordinal) { "without-cache", "compare-source", "dry-run" };
    var result = new Dictionary<string, string?>(StringComparer.Ordinal);
    for (var index = 0; index < args.Length; index++)
    {
        if (!args[index].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("選項必須以 -- 開始。");
        var key = args[index][2..];
        if (!allowed.Contains(key)) throw new ArgumentException($"未知選項：--{key}。");
        string? value = null;
        if (!flags.Contains(key))
        {
            if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"--{key} 缺少值。");
            value = args[index];
        }
        if (!result.TryAdd(key, value)) throw new ArgumentException($"重複選項：--{key}。");
    }
    return result;
}

static IProgress<CopyProgress> CopyProgress()
{
    var timer = Stopwatch.StartNew();
    return new InlineProgress<CopyProgress>(progress =>
    {
        if (timer.Elapsed < TimeSpan.FromSeconds(2) && progress.FilesDone != progress.FilesTotal) return;
        timer.Restart();
        Console.Error.WriteLine($"{progress.FilesDone:N0}/{progress.FilesTotal:N0} 檔；{progress.BytesDone / 1048576.0:N1}/{progress.BytesTotal / 1048576.0:N1} MiB");
    });
}

sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}

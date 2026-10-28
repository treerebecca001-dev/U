using System.Globalization;
using System.Text;
using SceneWorkbench.Core;

namespace SceneWorkbench.Unity;

public static class AuditReportWriter
{
    public static void Write(AuditBundle bundle, string outputRoot)
    {
        PrepareOutput(bundle.Report.ProjectRoot, outputRoot);
        JsonFile.WriteNew(Path.Combine(outputRoot, "baseline.json"), bundle.Report);
        JsonFile.WriteNew(Path.Combine(outputRoot, "asset-index.json"), bundle.Assets);
        JsonFile.WriteTextNew(Path.Combine(outputRoot, "baseline.md"), Markdown(bundle.Report));
    }

    public static void PrepareOutput(string projectRoot, string outputRoot)
    {
        outputRoot = SafePath.Canonical(outputRoot);
        SafePath.RequireDisjoint(projectRoot, outputRoot);
        var workCopyMarker = SafePath.Resolve(projectRoot, WorkCopy.InfoPath);
        if (File.Exists(workCopyMarker))
            SafePath.RequireDisjoint(WorkCopy.Open(projectRoot).Info.SourceRoot, outputRoot);
        if (Directory.Exists(outputRoot) || File.Exists(outputRoot))
            throw new IOException("報告目的地必須是尚未存在的新資料夾，以免覆蓋既有證據。");
        Directory.CreateDirectory(outputRoot);
    }

    private static string Cell(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static string Markdown(ProjectAudit report)
    {
        var text = new StringBuilder();
        text.AppendLine("# Unity 相容性基準報告");
        text.AppendLine();
        text.AppendLine($"- 產生時間：{report.GeneratedUtc:O}");
        text.AppendLine($"- 專案：`{report.ProjectRoot}`");
        text.AppendLine($"- Unity：{report.UnityVersion}；色彩空間：{report.ColorSpace}");
        text.AppendLine($"- 基準關卡：**{(report.BaselineBlocked ? "BLOCKED" : "PASSED LOCAL CHECKS；仍需 Unity 驗證") }**");
        text.AppendLine("- 目前產物是階段 0／1 的診斷與格式驗證工具，完整桌面編輯器尚未交付。");
        text.AppendLine();
        text.AppendLine("## 資源盤點");
        text.AppendLine();
        text.AppendLine($"來源 {report.Inventory.SourceFiles:N0} 檔、{report.Inventory.SourceBytes:N0} bytes；套件快取 {report.Inventory.CacheFiles:N0} 檔、{report.Inventory.CacheBytes:N0} bytes。");
        text.AppendLine($"索引 {report.Inventory.IndexedGuids:N0} 個 GUID；重複 GUID 群組 {report.Inventory.DuplicateGuids:N0}；專案 ASE Shader {report.Shaders.Count} 支。");
        text.AppendLine();
        foreach (var (tool, version) in report.ToolVersions)
            text.AppendLine($"- {tool}：{version}");
        text.AppendLine();
        text.AppendLine("## 關卡");
        text.AppendLine();
        text.AppendLine("| 項目 | 狀態 | 說明 |");
        text.AppendLine("|---|---|---|");
        foreach (var gate in report.Gates)
            text.AppendLine($"| {gate.Id} | {gate.Status} | {Cell(gate.Description)} |");
        text.AppendLine();
        text.AppendLine("## 診斷");
        text.AppendLine();
        text.AppendLine("| 程式碼 | 嚴重度 | 說明 | 位置 |");
        text.AppendLine("|---|---|---|---|");
        foreach (var diagnostic in report.Diagnostics.OrderByDescending(item => item.Severity).ThenBy(item => item.Code, StringComparer.Ordinal))
            text.AppendLine($"| {diagnostic.Code} | {diagnostic.Severity} | {Cell(diagnostic.Message)} | {Cell(diagnostic.Path ?? "")} |");
        text.AppendLine();
        text.AppendLine("## 場景樣本");
        text.AppendLine();
        text.AppendLine("| 場景 | Bytes | PrefabInstance | Transform | 未解析 GUID |");
        text.AppendLine("|---|---:|---:|---:|---:|");
        foreach (var scene in report.Scenes)
            text.AppendLine($"| {Cell(scene.Path)} | {scene.Bytes.ToString(CultureInfo.InvariantCulture)} | {scene.PrefabInstances} | {scene.Transforms} | {scene.UnresolvedGuidCount} |");
        text.AppendLine();
        text.AppendLine("## ASE Shader");
        text.AppendLine();
        foreach (var shader in report.Shaders)
            text.AppendLine($"- `{shader.Path}`：{shader.GraphNodes} 個節點；預覽狀態 `{shader.PreviewStatus}`。");
        text.AppendLine();
        text.AppendLine("## 下一步需要的證據");
        text.AppendLine();
        text.AppendLine("1. 在保留既有版本與原始檔案的條件下，確認可重現的 Unity／URP 環境。");
        text.AppendLine("2. FBX 子資源識別、原生 Terrain／Mesh 與套件編輯資料的實際往返。");
        text.AppendLine("3. 獨立生成兩套尋路與完整光照資料，交由 Unity 原系統載入。");
        text.AppendLine("4. 同攝影機、時間、Gamma、光照與後製的 ASE 預覽對照。");
        text.AppendLine();
        text.AppendLine("二進位容器重寫或 YAML 局部修改的本機測試，不代表上述關卡已通過。");
        return text.ToString();
    }
}

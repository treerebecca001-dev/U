# 階段 0／1 前置工具重交驗：0.1.0-r3

完整產品狀態仍為 **UNVERIFIED**，範圍遵照 [完整核准方案](APPROVED_PLAN.zh-TW.md)。r2 交驗與獨立失敗證據保留，本次不覆蓋舊產物。

## 固定產物與證據

- EXE：`E:\2026場景編輯器\artifacts\app\stage0-0.1.0-r3\SceneWorkbench.exe`。
- `build-manifest.json` SHA-256：`ead892742ef96d0accfec52d277db6a37bd72e460c1b45fe93821e4cdc3cd62f`。
- `artifacts/reports/source-manifest-r3.json` SHA-256：`b11218318f32e399925203f54018e75ff92566793d12234c7c6b80d530f32e16`。
- 發佈紀錄：`artifacts/logs/publish-r3.log`；測試建置：`artifacts/logs/test-build-r3.log`。
- 使用本次已發佈 exe 的本機測試：**60 通過、0 失敗、0 略過**；`artifacts/test-results/published-r3.trx`、`artifacts/logs/tests-published-r3.log`。
- 原始失敗證據：`artifacts/acceptance/r2-20261021/initial-r2/results.json`（驗收官 13 項斷言，9 通過／4 失敗，歸為 3 個缺陷）。
- r3 範本盤點：`artifacts/reports/template-baseline-r3`；執行中／完成紀錄 `artifacts/logs/template-audit-r3.log` 及 `template-audit-r3-exit.txt`。
- 測試機：`artifacts/reports/test-machine.json`，Threadripper 1900X／16 logical CPU、約 32 GiB RAM、RTX 2080 Ti、1920×1080。GPU 的 `AdapterRAM` 是 CIM 原始欄位，不作為實際 VRAM 容量宣稱。未量測編輯視角 FPS。

## 三項修正與回歸檢查

| 問題 | 修正後行為 | 本機執行檢查 |
|---|---|---|
| AC-001：manifest 宣告未鎖定／版本不一致 | manifest、lock 與嵌入套件一起檢查；缺少鎖定、宣告版本不同、未宣告的直接 lock 依賴均為 Blocking。已嵌入套件依 Unity 規則優先，未下載或改寫套件 | 實際 CLI audit 退出 2、patch 退出 1；來源與複本場景位元組及時間未變；有效 embedded override 仍可通過版本檢查 |
| AC-002：孤立 meta 假裝有效資源 | 檔案使用已枚舉的實體檔案集合查核、資料夾另查存在性；缺失資源產生 META_ASSET_MISSING，排除有效索引，引用維持未解析 | meta 雜湊保持、無虛構索引、場景缺失引用維持；實際寫入拒絕 |
| AC-003：GUID Blocking 未擋存檔 | `RequireKnownYamlWrite` 在套件通過後檢查有效 GUID 索引的所有 Blocking；CLI 提交前經同一檢查 | 真實重複資源 GUID 導致 audit 2、patch 1、場景未變；dry-run 保持唯讀可用 |

本次新增 9 項行為測試，與原有正常存檔、衝突、復原、ignored Samples、套件快取選擇等測試一起通過。r3 發佈與測試建置仍採 r2 文件說明的隔離方式，避免替換正在建立範本複本的舊 CLI 載入檔案。

## 驗收官重測

```powershell
& 'C:\Users\tree\.cache\codex-runtimes\codex-primary-runtime\dependencies\native\powershell\pwsh.exe' -NoProfile -File 'E:\2026場景編輯器\artifacts\acceptance\r2-20261021\run-acceptance.ps1' -Cli 'E:\2026場景編輯器\artifacts\app\stage0-0.1.0-r3\SceneWorkbench.exe' -RunName '<新的隔離結果目錄名稱>'
```

此腳本由驗收官建立，實作方沒有修改。正式重測及結果由驗收官執行回報。新產物可再使用原先的命令列入口；完整命令說明見 [README](../README.md)。

## 持續保留的門檻

Unity／URP 可重現基準、缺少的套件內容、FBX 子資源、真正新 Terrain／Mesh／ProBuilder／Polybrush／兩套尋路／光照原生生成、Unity 端往返、Godot 工作檯及 ASE 預覽等仍未通過。r2 的三場景／兩容器格式結果只證明其列出的本機格式處理，不證明地形製作或新烘焙能力。

原始範本保持唯讀；完整複本及逐檔核對將在完成後補交。階段 1 尚未通過，不擴展完整工具 UI。

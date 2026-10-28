# 階段 0／1 前置工具交驗：0.1.0-r2

完整產品狀態：**UNVERIFIED**。完整核准範圍以 [APPROVED_PLAN.zh-TW.md](APPROVED_PLAN.zh-TW.md) 為準，沒有縮減為 CLI。這次交付可獨立驗證的資料保護、診斷與格式處理工具；版本基準及階段 1 原生互通關卡尚未通過，尚不能進入完整工作檯開發。

## 固定受測產物

- 可執行檔：`E:\2026場景編輯器\artifacts\app\stage0-0.1.0-r2\SceneWorkbench.exe`。Windows x64、自帶 .NET 執行環境，目前是 CLI。
- 產物清單：同資料夾 `build-manifest.json`；SHA-256：`ca170c02285dbf03f766de75bbf84036931046e45f788b36fb16d9dea28eed3b`。
- 核准方案 SHA-256：`c1446c59bb4c61af3b2c215e36f8d7eb1816af02fcbd3aa23012249c2a2b9b96`。
- 原始碼與設定的受控範圍雜湊：`artifacts/reports/source-manifest-r2.json`。
- SDK：工作區 `.tools/dotnet` 的 10.0.402；套件由各 `packages.lock.json` 固定，發佈帶有第三方授權。

## 已取得的本機執行證據

| 項目 | 觀察結果 | 證據 |
|---|---|---|
| 發佈建置 | 自帶執行環境的 win-x64 產物已產生 | `artifacts/logs/publish-r2.log` |
| 51 項測試 | 51 通過、0 失敗、0 略過；CLI 生命週期使用上述已發佈 exe | `artifacts/test-results/published-r2.trx`、`artifacts/logs/tests-published-r2.log` |
| 原檔保護／存檔 | 隔離專案驗證來源唯讀、外部修改阻擋、中斷／重啟復原、未知資料保留 | 同一測試紀錄及 `tests/SceneWorkbench.Tests` |
| 實際範本的 3 個場景 | No-op 原樣、局部座標變更可重讀、其他物件原樣、Undo 位元組一致、來源 SHA-256 未變 | `artifacts/reports/template-format-probes-r2/format-probes.json` |
| 實際範本的 2 個 Terrain 容器 | 可讀型別樹，隔離重寫後欄位與檔案位元組一致，來源未變 | 同上；僅容器檢查，未雕塑／新建地形 |
| Unity 匯入規則 | 排除 `Samples~`、隱藏／忽略項目與未選用快取；保留實體清單與複本；有效重複 GUID 仍阻擋 | CompatibilityTests 的實際檔案樣本測試 |

完整範本工作複本正在建立於 `E:\2026場景編輯器\samples\Client231229`；完成狀態及逐檔來源／複本驗證將另行補交。標記仍為 Copying 時不可修改、驗收為完成或自行接續複製；執行中的複製程序由實作方管理。

參考專案新版盤點已完成，位於 `artifacts/reports/template-baseline-r2`；紀錄為 `artifacts/logs/template-audit-r2.log`。共 44,054 個來源檔、26,508 個快取檔、33,877 個有效 GUID、0 個重複 GUID、14 支專案 ASE Shader。基準仍為 BLOCKED：7 個套件最低 Unity 版本衝突、1 個套件內容缺少。兩個場景分別有 1／12 個外部 GUID 尚未解析。

## 可重測的命令

```powershell
$cli = 'E:\2026場景編輯器\artifacts\app\stage0-0.1.0-r2\SceneWorkbench.exe'
& $cli help
& $cli --version
& $cli audit --project '<隔離 Unity 專案>' --output '<尚未存在的隔離報告資料夾>'
& $cli copy --project '<隔離來源>' --destination '<尚未存在的複本資料夾>'
& $cli verify-copy --project '<複本>' --compare-source
& $cli patch-transform --project '<複本>' --scene 'Assets/Scene.unity' --file-id '-2745811161023527127' --position '3.25,4.5,-5.75'
& $cli recover --project '<複本>'
```

上述 Transform ID 屬於自動化隔離樣本，實際專案必須使用各自存在的完整 Transform ID。任何範本修改均只能在工作複本或另建隔離樣本執行；不修改原始範本、不改版本字串來繞過檢查。

```powershell
$env:SCENE_WORKBENCH_CLI = 'E:\2026場景編輯器\artifacts\app\stage0-0.1.0-r2\SceneWorkbench.exe'
.\scripts\dotnet.ps1 test tests/SceneWorkbench.Tests/SceneWorkbench.Tests.csproj --configuration Release --no-build --no-restore
```

一般完整建置與測試可用 `scripts/verify.ps1 -CliExecutable $cli`。首次 r2 驗證時，舊版本 CLI 正在複製大專案並鎖住其 build 輸出；完整解決方案建置遇到 MSB3021。因此 r2 先透過 RID 專用輸出發佈所有產品相依專案，再建置測試專案（`BuildProjectReferences=false`），最後對已發佈 exe 執行測試。沒有停止複製或替換執行中程式；相關失敗記錄保留在 `artifacts/logs/build-r2-locked.log`。此失敗不可算作解決方案建置通過。

## 仍然阻擋的基準與完整需求

1. 範本記錄 Unity 2021.3.15f1，URP／Core／ShaderGraph 14.0.9 快取要求 2022.3；AI Navigation、Memory Profiler、Terrain Tools 也有較高版本要求。機器未安裝 2022.3，未取得可重現的 Unity 載入環境。
2. `com.tuyoogame.yooasset` 在範本中缺少可用套件內容。不自動存取私人來源。
3. FBX Mesh fileID／頂點／UV2 對應尚未證實；場景內存在需追查的外部 GUID。場景盤點未遞迴展開所有 Prefab 相依關係。
4. 三個候選場景、Terrain 容器與既有 Lighting Data 的讀取，均沒有 Unity 實際載入證據；`unityAccepted` 固定為 false。
5. 新 Terrain／Mesh、ProBuilder／Polybrush 可繼續編輯資料、兩套新尋路與新光照原生資料仍未生成。`stageOnePassed` 為 false。
6. Godot 桌面介面、ASE 預覽、Scene／Game、Play、地形製作、粒子、完整 Prefab 與光照工具尚未實作；沒有對應 UI／效能／無 Unity 乾淨機驗收。

版本未確立前，範本複本回寫會被拒絕。請將可重現問題回報至實作對話「規劃 Unity 相容場景編輯器」，附固定產物、命令、退出碼、預期／實際結果及隔離樣本；修正後以新產物重新驗證。前置工具通過不可代替完整階段 0–7 驗收。

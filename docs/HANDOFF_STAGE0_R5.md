# 階段 0／1 前置工具重交驗：0.1.0-r5

後續唯讀全量核對的效能修正與最新產物見 [r6 交驗紀錄](HANDOFF_STAGE0_R6.md)。r5 的慢速全量核對已中止，未宣稱完成；此文件保留 r5 當時的測試與交驗內容。

完整產品狀態：**UNVERIFIED**。完整範圍及階段順序以 [核准方案](APPROVED_PLAN.zh-TW.md) 為準。此交付是診斷與資料保護工具，完整 Godot 桌面編輯器及原生生成能力仍未交付。

## 固定產物

- 可執行檔：`E:\2026場景編輯器\artifacts\app\stage0-0.1.0-r5\SceneWorkbench.exe`，Windows x64、自帶 .NET 執行環境、CLI。
- `build-manifest.json` SHA-256：`2ee7039932163ebc66729d06de37bee3b49b623e12c0148fc09081b115ceda15`。
- `artifacts/reports/source-manifest-r5.json` SHA-256：`b668a18f06151b34dccdb85730ca55d2b35f938297748a6c74924a73dea7dc85`。
- 核准方案 SHA-256：`c1446c59bb4c61af3b2c215e36f8d7eb1816af02fcbd3aa23012249c2a2b9b96`。

舊 r2／r3／r4 產物、失敗紀錄及來源雜湊清單均保留，不覆寫為新版。r4 未正式交驗；r5 同時納入 AC-004 與 AC-005。

## 本機證據

- `scripts/verify.ps1 -CliExecutable '<r5 exe>'`：完整解決方案建置 **0 警告、0 錯誤**；測試 **65 通過、0 失敗、0 略過**。CLI 生命週期直接啟動本次發佈 exe。
- 原始紀錄：`artifacts/logs/verification-r5.log`、`artifacts/logs/publish-r5.log`；TRX：`artifacts/test-results/stage0-20261021-111423.trx`。
- r2 建置曾因執行中複製程序鎖定輸出而失敗；該程序已結束，完整建置恢復。失敗證據保留於 `artifacts/logs/build-r2-locked.log`。
- 先前三場景的局部 YAML／Undo 與兩 Terrain 容器檢查保留在 `artifacts/reports/template-format-probes-r2/format-probes.json`。本次沒有修改這些格式處理模組；該證據仍只支持其列出的本機行為，沒有 Unity 驗收或新資料生成證據。
- 實際測試機規格：`artifacts/reports/test-machine.json`。尚無 Godot 畫面或 FPS 測試。

## 獨立驗收與修正

驗收官原始腳本與案例未由實作方修改：

- `artifacts/acceptance/r2-20261021/run-acceptance.ps1`；r2 原始結果 9／13 通過，r3 重測 13／13 通過，AC-001／002／003 原重現情境已關閉。
- `artifacts/acceptance/r2-20261021/run-neighbors.ps1`；r3 鄰近回歸 3／6 通過，發現 AC-004／005，須以本次 r5 重新驗證。

**r5 獨立重測已完成：13／13 加 6／6，共 19／19 通過。** 實作方已讀取兩份原始 results.json，確認所有斷言為 true，受測 build manifest 與本次固定產物一致。AC-001 至 AC-005 在所列重現及鄰近回歸範圍關閉，沒有新增工具缺陷。

證據：`artifacts/acceptance/r2-20261021/retest-r5/results.json`、`neighbors-r5/results.json`，以及 `retest-r5/identity.json`。驗收官另核對 204 個發佈檔與 41 個受控來源檔，全部與清單一致。此結論只限前置工具已測行為，階段 0／1 及完整產品仍未通過。

| 問題 | r5 行為 |
|---|---|
| AC-001：套件宣告／lock 不一致 | 缺少宣告套件的鎖定、宣告與 lock 版本不同、lock 的直接依賴已不在 manifest 均為 Blocking；有效嵌入套件仍依 Unity 規則優先 |
| AC-002：孤立 meta | 不將不存在的資源計入有效索引；回報缺失並保留原 meta 與場景引用 |
| AC-003：GUID Blocking 未擋存檔 | 實際提交前，同時通過套件及 GUID／資源完整性的 Blocking 檢查；乾跑保持唯讀可用 |
| AC-004：舊資料夾 meta | 實體資料夾優先判定種類，即使沒有 folderAsset 標記也正常索引；涵蓋空資料夾 |
| AC-005：暫存項目誤阻擋 | 在發出缺檔／GUID 錯誤前排除 .tmp 檔案；不存在的暫存檔及無效暫存 meta 不阻擋。真實 Folder.tmp 可匯入，明確宣告但不存在的資料夾仍回報缺失 |

另外會診斷 meta 宣告資料夾但實際為檔案的種類不一致。所有排除均只影響索引與診斷，實體檔案仍完整保留與複製。

## 實際範本結果

`artifacts/reports/template-baseline-r5/baseline.json`：

- 44,054 個來源檔、26,508 個快取檔，14 支專案 ASE Shader。
- 33,875 個有效索引項、0 個重複 GUID、0 個孤立 meta 缺檔診斷。
- **10 個 Blocking**：7 個套件最低 Unity 版本衝突、1 個套件內容缺少、2 個 meta／實體種類不一致。
- 兩個種類不一致位於 RiderFlow 的 `hash.txt.meta` 與 Burst 的 `Unity.Burst.Unsafe.xml.meta`。保留現況，沒有自動重建 meta。
- Map_12006／Map_60003 仍分別有 1／12 個外部 GUID 未解析；FBX 子資源映射未證實。
- `artifacts/logs/template-audit-r5-exit.txt` 記錄原生退出碼；audit 退出 2，不把 BLOCKED 當作通過。

r3 曾將 73 個舊資料夾 meta 誤報缺檔；`orphan-meta-check-r3.json` 逐項實體核對確認全部資料夾存在。這是已修正的診斷回歸，不能用 r3 報告宣稱範本遺失 73 個資源。

## 完整工作複本

`E:\2026場景編輯器\samples\Client231229` 已完成建立，共 **70,562 檔、5,793,404,498 bytes**，包含 Unity 三個來源根目錄、空資料夾與 PackageCache。marker 狀態為 Complete。

`copy-manifest.jsonl` SHA-256：`fb537b74185ac947e3039c23be74258e9c61635105a4b94fb3d320a717416e5d`。

全量來源／複本核對另以 r5 `verify-copy --compare-source` 執行，完成證據為 `artifacts/reports/template-copy-verification-r5.json` 及 `artifacts/logs/template-verify-copy-r5-exit.txt`。未取得完整結果前，不能宣稱全量核對通過。先前 r4 沙箱執行因逐檔檢查過慢而中止，保留部分進度；新版執行使用相同唯讀驗證流程，沒有降低程式內路徑或雜湊檢查。追蹤狀態已更正：驗收官的既有排程實際為 PAUSED，沒有自行恢復；目前由實作對話直接追蹤此執行中的程序，完成後補交原始結果、退出碼及呼叫證據，再由驗收官驗收。不另啟重複全量掃描。

原始範本始終不開放回寫；範本複本的版本／套件基準仍不合格，程式會拒絕實際場景提交。

## 完整門檻與後續處理

需要先建立可重現的 Unity／URP 驗證環境並釐清缺少套件與 meta 種類衝突。保持原始版本及資源，不能只改版本字串、忽略 Blocking 或自行改為工具專用格式。

FBX 對應、新 Terrain／Mesh、ProBuilder／Polybrush 可繼續編輯、兩套新尋路、完整光照原生生成、ASE 預覽及 Unity 端往返仍未完成；Godot UI、Play、工具與效能驗收仍待高風險關卡通過。依核准方案暫停依賴未通過結果的後續實作，交付診斷及樣本差異。

驗收官可直接執行原有兩支獨立腳本，指定 r5 exe 與新結果目錄，回報問題後由實作方修正再交驗。

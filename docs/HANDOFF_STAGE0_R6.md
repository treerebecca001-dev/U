# 階段 0／1 前置工具重交驗：0.1.0-r6

完整產品狀態仍為 **UNVERIFIED**；[核准方案](APPROVED_PLAN.zh-TW.md) 的階段與全部需求維持不變。此次只改善完整工作複本的唯讀驗證，不擴展 UI 或原生生成工具。

## 固定產物及測試

- EXE：`E:\2026場景編輯器\artifacts\app\stage0-0.1.0-r6\SceneWorkbench.exe`。
- `build-manifest.json` SHA-256：`9a9cc83d6543779e1689d7947912e0fcb5650fb6ec0ef09403a591f335dc123c`。
- `artifacts/reports/source-manifest-r6.json` SHA-256：`3b196445f3078d480fbf61d088b993c0b7eb015f7a6e06c3e16c690ec6d761ef`。
- 完整解決方案建置 **0 警告、0 錯誤**；使用本次已發佈 EXE 的本機測試 **68 通過、0 失敗、0 略過**。
- 原始紀錄：`artifacts/logs/verification-r6.log`、`artifacts/logs/publish-r6.log`、`artifacts/test-results/stage0-20261021-112656.trx`。
- 發佈 Core DLL 與測試所用建置 DLL 的 SHA-256 相同：`68761fafce1a0b17a3911efdddaf3761cd536240edd50e1bd722e3cfab8991e8`。

## 唯讀核對的變更

r5 的 70,562 檔全量驗證以串行方式重複檢查路徑，實際執行很慢。r6 先解析清單並拒絕非法根目錄、重複路徑、路徑逃逸與不合法雜湊，再以最多 4 項並行核對檔案。

每個實體檔案讀取前仍使用原有 `Resolve`／`RejectLinks` 檢查。來源路徑保護在未啟用來源內容比較時也保留。公開 `ReadManifest` 仍檢查實體路徑；純語法路徑解析只供內部預先讀取清單使用，沒有新增跳過安全檢查的 CLI 選項。

來源／複本雜湊、清單數量與大小、額外檔案、取消及最後的完整檔案集掃描皆保留。進度回呼依序報告完成數；差異清單按 ordinal 排序。`Copy` 與 `SaveTransaction` 行為未變。

新增測試實際檢查：64 個檔案中同時存在來源變更、複本變更、兩邊缺檔及新增檔案，回報六種差異與完整計數；取消後複本標記及內容不變；junction 目標檔被獨佔鎖定時，驗證仍在路徑保護處拒絕、沒有嘗試讀取目標。

## 獨立重測結果

驗收官已完成 r6 的 **29／29 項獨立檢查**：原有兩支腳本共 19 項，以及另外建立的 10 項 Verify 檢查。本輪沒有新增工具缺陷，AC-001～005 在既有重現範圍仍保持關閉。實作方已讀取原始結果並核對受測產物識別。

原始證據：`artifacts/acceptance/r2-20261021/retest-r6/results.json`、`neighbors-r6/results.json`、`artifacts/acceptance/r6-verify-20261021/native-run/results.json`。彙整識別為 `retest-r6/identity.json`；驗收官核對 204 個發佈檔與 41 個受控來源檔，皆與固定清單一致。

獨立 Verify 補驗涵蓋 128 個資料檔的六類差異（連續 3 次計數及排序一致）、僅比較複本、取消後由新 CLI 程序成功重讀、來源／複本 junction 的鎖定目標見證，以及重新計算 manifest 完整性後仍拒絕路徑穿越、ADS 和重複清單。這些有界測試不替代真實範本的全量核對或完整階段驗收。

## 保留的 r5 證據

r5 固定產物與原始 19／19 獨立通過證據未變，其 41 個受控來源檔另依原 SHA-256 核對後保存於 `artifacts/source-snapshots/r5`。AC-001～005 的修正仍保留，驗收官須針對本次 r6 重測受影響行為，不能把舊產物的通過直接當作新產物通過。

r5 慢速全量核對已中止，不宣稱完成。原呼叫與中止狀態保留於 `artifacts/reports/template-copy-verification-run-r5.json`，進度保留在 `artifacts/logs/template-verify-copy-r5-progress.log`。

## 真實複本全量核對

已完成 r6 `verify-copy --project E:\2026場景編輯器\samples\Client231229 --compare-source`。原始 stdout：`artifacts/reports/template-copy-verification-r6.json`；stderr 進度：`artifacts/logs/template-verify-copy-r6-progress.log`；原生退出碼：`artifacts/logs/template-verify-copy-r6-exit.txt`。

實際結果：`filesChecked = 70562`、`sourceFilesChecked = 70562`、`differences = []`、`isValid = true`，原生退出碼 **0**。來源與複本皆符合建立複本時記錄的逐檔 SHA-256；最後檔案集掃描沒有發現新增檔案。此命令不修改原始範本或複本資源。

結果 JSON SHA-256：`eeff628b8bb3f190542d32b13e5cd77c0e30821fef94ca7638daba994a46970b`。複本 marker 仍為 Complete，共 70,562 檔、5,793,404,498 bytes；copy-manifest 實際 SHA-256 仍與 marker 的 `fb537b74185ac947e3039c23be74258e9c61635105a4b94fb3d320a717416e5d` 相符。

實際呼叫、受測產物、程序開始／結束及結果識別：`artifacts/reports/template-copy-verification-run-r6.json`。UTC 11:28:34.7750877 至 11:42:04.8936298，單次實測 **810.119 秒（約 13 分 30 秒）**。日期計算已保留 UTC DateTime 種類，修正初次紀錄時 PowerShell 自動解析日期造成的 8 小時偏移；原始結果及退出碼沒有變動。r5 未完成，不能據此宣稱相對效能倍數。

驗收官的既有排程維持 PAUSED；由實作對話直接完成追蹤，原始結果與呼叫證據已補交驗收官確認，不另啟重複全量掃描。檔案內容一致性通過不等於 Unity 端載入、可繼續編輯或新原生資料生成通過。

**全量補交證據已由驗收官確認通過。** 獨立彙整：`artifacts/acceptance/r6-verify-20261021/full-copy-review.json`。其核對原始 JSON、原生退出碼、固定產物、實際參數、來源／複本根路徑、marker 與 manifest，以及獨立重算的 810.1185421 秒，沒有另起全量掃描。實作方亦已讀取該原始彙整。此結論只限本次清單所涵蓋 Unity 來源根目錄與 PackageCache 的檔案完整性。

## 持續保留的完整門檻

[r5 實際範本盤點](../artifacts/reports/template-baseline-r5/baseline.md) 的 10 項基準 Blocking 尚未處理；套件／GUID 索引與 Unity 內容沒有變更。Unity／URP 可重現環境及階段 1 的完整原生生成、Unity 往返、ASE 畫面等仍未通過。依核准方案停止依賴該結果的後續開發。

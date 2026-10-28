# 範本修復與獨立運作交驗

2026-10-22 交付 BL-008 至 BL-010 的工作複本修復，請驗收官獨立重測。完整產品狀態為 **UNVERIFIED**；七項版本矛盾與完整階段 1 門檻仍未通過。

## 獨立運作要求

使用者確認目前沒有可用的 Unity，本工具的正常開啟、編輯、預覽及原生存檔必須不依賴 Unity 的安裝、啟動、授權或背景服務。缺少 Unity 授權只留下 Unity 端載入、繼續編輯與回存的外部驗收缺口，不阻止獨立功能的開發與離線驗證。核准計畫的完整功能、資料保護與階段 1 放行條件繼續保留。

目前仍是階段 0／1 的 CLI 及核心程式，尚無完整桌面介面、ASE 畫面、Play 或新地形／尋路／光照生成。現有 `patch-transform` 在有效的 2021.3 格式測試專案可以獨立儲存，但 Client231229 的七項版本矛盾仍會阻擋回寫；此次沒有跳過檢查或啟用未驗證的 2022.3 回寫設定。

## 修復範圍

| 編號 | 修復或目前狀態 | 可核對資料 |
|---|---|---|
| BL-001 | ai.navigation 1.1.5 要求 2022.2，與記錄的 2021.3.15f1 矛盾，仍未解決 | package.json 與 audit |
| BL-002 | memoryprofiler 1.1.0 要求 2022.3，矛盾仍在 | package.json 與 audit |
| BL-003 | render-pipelines.core 14.0.9 要求 2022.3，矛盾仍在 | package.json 與 audit |
| BL-004 | render-pipelines.universal 14.0.9 要求 2022.3，矛盾仍在 | package.json 與 audit |
| BL-005 | render-pipelines.universal-config 14.0.9 要求 2022.3，矛盾仍在 | package.json 與 audit |
| BL-006 | shadergraph 14.0.9 要求 2022.3，矛盾仍在 | package.json 與 audit |
| BL-007 | terrain-tools 5.0.1 要求 2022.2，矛盾仍在 | package.json 與 audit |
| BL-008 | 從既有合法來源找回鎖定 commit 的完整 YooAsset，加入工作複本 Packages 的 embedded 套件 | commit、Git tree、archive、1257 筆逐檔 SHA-256 及依賴 |
| BL-009 | 刪除 RiderFlow `hash.txt.meta` 錯誤的 `folderAsset: yes` 行，保留 GUID 與其他內容 | 前後雜湊、原始 bytes 備份及對應 33 bytes 檔案 |
| BL-010 | 刪除 Burst `Unity.Burst.Unsafe.xml.meta` 同一錯誤行，保留 GUID 與其他內容 | 前後雜湊、原始 bytes 備份及對應 18657 bytes 檔案 |

YooAsset 的名稱為 `com.tuyoogame.yooasset`，版本 `1.4.5-preview`，commit 為 `160717b6411986cba4c223fa6f7604b32e1e669e`，Git tree 為 `a282330eda5031f7ab119c7171b0dac78ee295a3`。封存檔 SHA-256 為 `c5fdeb3190a5cab13e740e917a240f6f647a951d057dec35db37aec9e26367b3`。沒有改寫 manifest 或 lock，沒有刪除依賴或使用空殼套件；四項 package.json 依賴與原鎖定內容相符。私人來源 URL 不列入報告。

## 產物與原始證據

證據根目錄為 [BL 修復紀錄](E:/2026場景編輯器/artifacts/remediation/BL-001-010)。

- [修復紀錄](E:/2026場景編輯器/artifacts/remediation/BL-001-010/repairs-applied.json) 列出兩項 meta 的 GUID、前後 SHA-256、備份路徑，以及套件來源身份。
- [YooAsset 檔案清單](E:/2026場景編輯器/artifacts/remediation/BL-001-010/yooasset-added-files.json) 記錄完整 1257 個新增檔案；原始 Git 封存檔位於 `.tools/package-recovery/yooasset-160717b641.tar`。
- [最新離線 audit](E:/2026場景編輯器/artifacts/remediation/BL-001-010/baseline-offline/baseline.json)、`audit-offline.stdout.log`、`audit-offline.stderr.log`、`audit-offline.exit.txt` 及 `audit-offline-run.json` 保留原始結果與呼叫身份。預期為七項 `PACKAGE_EDITOR_MISMATCH`、退出碼 2；三項已修復問題不再出現。
- [差異摘要](E:/2026場景編輯器/artifacts/remediation/BL-001-010/offline-baseline/delta-summary.json) 為 71819 個檔案、修改 2、新增 1257、未預期差異 0。它沿用先前已完整驗證的 70560 筆大小／時間未變記錄，只重新雜湊新增及改動檔案，**不是重新完成一次 5.8 GB 的全檔雜湊**。
- [來源保護核對](E:/2026場景編輯器/artifacts/remediation/BL-001-010/offline-baseline/source-protection-after.json) 重查兩個受修 meta 與七項來源監測記錄；均符合修復前雜湊。所有修復操作只寫入 E 槽工作複本，不寫入 D 槽原始範本。
- 原 copy-manifest 仍為 `fb537b74185ac947e3039c23be74258e9c61635105a4b94fb3d320a717416e5d`。新的衍生清單另存於 `offline-baseline/derived-post-repair-baseline.jsonl`，SHA-256 為 `ca072867845e509737ac5c9374016f465992f0e6cb71e8fd87d94bb569ea1161`；沒有覆蓋原清單。

仍使用固定 r6 `SceneWorkbench.exe`，其 build-manifest SHA-256 為 `9a9cc83d6543779e1689d7947912e0fcb5650fb6ec0ef09403a591f335dc123c`。產品核心與 CLI 沒有改版。新修復及紀錄腳本另以 `baseline-remediation-0.1.0` 原始碼快照和 manifest 固定身份，不宣稱是新增桌面功能的產品版本。

## 獨立儲存的本機驗證

以固定 r6 執行檔重跑既有 `RealCliCopiesEditsReloadsAndProtectsOriginal`，PATH 限制為 Windows 系統目錄並移除 Unity 路徑變數。測試包含建立工作複本、dry-run、修改原生 YAML、獨立儲存、重新讀取座標、確認來源不變，以及拒絕回寫來源，結果 1 項通過、退出碼 0。原始 TRX、輸出與執行身份為 `offline-cli-lifecycle.trx`、`offline-cli-lifecycle.log` 和 `offline-cli-lifecycle-run.json`。

這是測試專案的 CLI 證據；本機已有開發用 Unity，PATH 限制也不是檔案存取隔離，因此仍未宣稱「未安裝 Unity 的乾淨環境」或完整編輯器獨立驗收通過。沒有將此測試擴大為 Client231229 已可回寫。

## Unity 端驗收狀態

開發環境曾在工作區安裝 Unity 2022.3.13f1，官方安裝檔的來源、完整性與簽章另有紀錄。只對空白隔離專案執行過授權探測；實際主機日誌回報 `No valid Unity Editor license found`，原生退出碼 1。未載入、升級或由 Unity 回存 Client231229，ProjectVersion 仍是 2021.3.15f1。

使用者表示目前無法啟用，因此不再重試啟用或要求使用者提供授權。`scripts/unity` 僅保留未編譯、未執行的可選外部 QA 原始碼；兩個暫放在工作複本 Assets 的 QA 檔案已按雜湊核對後撤出，詳見 `unity-helper-withdrawal.json`。產品及獨立測試不依賴這些檔案。

## 還原與下一次交驗

兩個 meta 的原始 bytes 在 `backups/Library/PackageCache/...`；還原前先確認目前 SHA-256 符合 `repairs-applied.json` 的 afterSha256，備份符合 beforeSha256，再逐檔還原。若有外部新改動則停止，不覆蓋。YooAsset 封存檔與新增清單可追溯新增內容；如需撤回套件，逐檔核對這 1257 筆身份，保留任何額外或已被修改的檔案，不對資料夾直接遞迴刪除。此次沒有執行還原。

驗收官可直接重測 BL-008 至 BL-010、原始來源保護與受影響的離線診斷。BL-001 至 BL-007 保留版本矛盾及外部相容驗證缺口；離線檔案與功能開發繼續，真正 Unity 往返和完整階段 1 未放行。既有排程維持 PAUSED。

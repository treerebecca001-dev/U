# 版本基準與直接專案存取交驗

本輪依人類要求處理 BL-001～007 中可離線完成的部分，並加入在人類驗收對話明確提出的「使用專案副本」勾選。這是本機網頁來源開發版；不啟動 Unity、不重新嘗試授權、不 publish、不打包。原始 D:/Work_ART/漂流NFT/@WLN_work/Client231229 在開發與驗收中維持唯讀。

## 版本選擇與資料邊界

來源記錄 2021.3.15f1，但 7 支實際套件的最低 Editor 是 2022.2／2022.3。採用新的隔離完整複本，明確宣告 2022.3.13f1／5f90a5ebde0f 為驗證目標；保留所有套件、manifest、lock、Shader／ASE、地形／尋路及其他原生資源。該版本的官方安裝來源及 revision 既有證據在 `.tools/unity-2022.3.13f1/provenance.json`；軟體正常運作不載入或依賴該 Editor。

| 原編號 | 套件與原版本 | 最低 Editor | 深度 |
|---|---|---|---|
| BL-001 | ai.navigation 1.1.5 | 2022.2.0a16 | 直接 |
| BL-002 | memoryprofiler 1.1.0 | 2022.3 | 直接 |
| BL-003 | render-pipelines.core 14.0.9 | 2022.3 | 間接 |
| BL-004 | render-pipelines.universal 14.0.9 | 2022.3 | 直接 |
| BL-005 | render-pipelines.universal-config 14.0.9 | 2022.3 | 間接 |
| BL-006 | shadergraph 14.0.9 | 2022.3 | 間接 |
| BL-007 | terrain-tools 5.0.1 | 2022.2.0a15 | 直接 |

這不是資源遷移或引擎通過證明。新複本 ProjectVersion 的修改表示明確選定的目標 Editor；既有 `.unity`／`.mat` 和二進位資源保留原本資料版本。來源 2021.3 複本仍如實顯示原 7 項矛盾；Unity 的匯入、程式／Shader 編譯、原生系統載入與真正往返各自仍為 UNVERIFIED。

離線修正同時包含 2022.3 的局部欄位支援，避免只改版本後整個工作流退為唯讀。辨識 GameObject 6、Transform 原有結構及 2022.3 的 serializedVersion 2、Material 6／7／8；依原來的 ownership、欄位存在與型別檢查限制修改。Material Variant、Prefab 連結／stripped、未知結構、其他未驗證 Editor 家族保持唯讀。URP 12 與 14 有 Editor 家族限制；`unityRelease` 的 patch／alpha／beta／final 順序，以及實際依賴與 lock 的閉合、最低版本、feature 的已鎖定 default 解析都會檢查，沒有通用版本覆寫參數。

原始 Transform v2 樣本來自現有 URP 14.0.9 `BlitToRTHandle.unity`，30363 bytes，SHA256 `9149fba25a366ff4ded282324530cacf5ff133a510780d6fa1c8b1b19db49805`。完整原樣樣本、provenance 與 Unity Companion License 在 `tests/SceneWorkbench.Tests/Fixtures/Unity2022.3`。它沒有在本輪經 Unity 產生，僅用於已觀察結構的局部修改、其他物件原文不變及 Undo 精確復原驗證。

## 基準命令

```powershell
scripts/dotnet.ps1 src/SceneWorkbench.Cli/bin/Release/net10.0/SceneWorkbench.dll prepare-baseline --project samples/Client231229 --destination Workspace/Baselines/Client231229-2022.3.13f1 --output artifacts/version-r8/preparation
scripts/dotnet.ps1 src/SceneWorkbench.Cli/bin/Release/net10.0/SceneWorkbench.dll verify-baseline --project Workspace/Baselines/Client231229-2022.3.13f1 --compare-source
```

準備目的地與報告必須不存在；不覆蓋、不刪舊資料。準備程序檢查來源、套件依賴及 GUID，建立包含完整 package cache 與逐檔 SHA 的新複本，以 SaveTransaction 備份並修改新複本的版本宣告。`baseline-preparation.json` 分別記錄來源與目標、逐項狀態、依賴、輸入 hash、copy manifest、交易及 NativeStatus。`verify-copy` 仍會如實回報有意修改的 ProjectVersion，不改寫原始 copy manifest 來掩飾差異。

## 可選的直接存取

人類實際訊息已從驗收對話的 userMessage 核對，識別見 `artifacts/version-r8/IN_PROGRESS.json`。開啟對話預設勾選「使用專案副本」，必須指定工作區內新的目的地，完整建立並開啟複本；取消勾選則明示「直接儲存至所選專案」，不建立隱藏的完整複本，也不新增確認對話框。工作檯顯示模式與實際路徑。

DirectProjectAccess 只由明確選擇建立，開啟本身不寫入專案。它與 WorkCopy 共享 IProjectWriteAccess／SaveTransaction，使用同一寫入鎖、分檔安全替換、備份、失敗回復、外部修改與復原衝突檢查。存檔只備份實際改變的檔案；不偽造 workcopy marker，不改版本／套件。未指定 mode 的既有 API 仍沿用原本 inspect 行為；嚴格 CLI 來源保護不變。

直接模式的開發、測試只使用新建隔離 QA 專案。原始 D: 專案沒有被指定為直接寫入測試目標。

## 驗收範圍與證據

最新正式交驗身份為 `baseline-r8-fix2`，凍結的普通來源目錄為 `artifacts/version-r8/source-snapshot-fix2`（不是安裝包或壓縮檔）。128 個來源檔案的清單在 `artifacts/version-r8/handoff-fix2/source-manifest.json`，SHA256 `bf708316e884edb12ebb2863758220ee0ecc63a4b5827b45c1e35d26517aae08`；Web／CLI／測試實際 DLL 的身份在同目錄 `runtime.json`。此前 `handoff`／`handoff-fix1` 與各來源快照保留不變。fix1 增加明確載入按鈕；fix2 修正獨立驗收的 WEB-SCENE-001，並加入使用者後續要求的讀取進度條，C# DLL 未改。

| 驗證 | 實際結果與原始證據 |
|---|---|
| 完整基準準備 | 71,819 檔、5,821,816,843 bytes，完整 cache；`preparation/preparation.json` 與 `artifacts/logs/baseline-r8-prepare.log`。準備當時 DLL 身份為 `prepare-invocation.json`，不是事後的 DLL |
| 最新 CLI 基準檢查 | `artifacts/logs/baseline-r8-verify-current.log`：78 個實際套件，來源輸入比對成立，離線宣告通過，NativeStatus 仍 UNVERIFIED |
| 套件／原始資源保護 | `packages-matrix.json`：78 套件／96 依賴／164 輸入 pin；`native-preservation.json`：96 個來源預覽資源／meta 及 10 個版本／套件輸入 watch 一致。最新核對在 `content-completion/current-inputs-and-protection.json` |
| 完整來源／新複本內容比對 | `native-content-comparison.json` 與 `artifacts/logs/baseline-r8-content-check.log`：各71,819檔、各5,821,816,843 bytes，清單無增缺、SHA256除目標ProjectVersion外皆一致，讀取期間大小／mtime清單穩定，exit0。完成時間2026-10-22 15:10:40（Asia/Taipei），用時1115.16秒。兩趟中止的CLI紀錄仍不算PASS |
| 整合自動測試 | `artifacts/test-results/baseline-r8-integrated-final.trx`：163/163；`artifacts/logs/baseline-r8-integrated-final.log`。其後僅前端／說明／驗收腳本變更，這份後端證據按相同 DLL 沿用 |
| 副本／直接模式 | `access-browser/result.json`：8 組實際 Chrome 操作通過。包含真正建立副本、兩種原生儲存、重新開啟、外部衝突與逐檔備份、390px。此流程相關程式未改，新增載入控制另測 |
| 實際新 Client 複本 | `browser-isolated-fix1/result.json`：7 組通過；Room_main 的 7 網格／2681 三角形、原生 Scene／既有 ASE 數值及貼圖儲存／重讀／Undo／Redo、精確還原及 390px。pageErrors／console／failedRequests 皆空 |
| Scene 載入修正 | `scene-load-browser/result.json`：7 組通過；自動載入、同一選項明確重載、進度與禁止重複、原生／預覽失敗與重試、未存檔取消／捨棄、真正 token 拒絕提示、390px。預期注入 2 次 HTTP503 及真實拒絕 1 次 HTTP403；無其他 Console 或頁面錯誤 |
| fix2 首次失敗／進度條 | `scene-load-progress-fix2-retry/result.json`：9 組通過；首次原生失敗仍保留錯誤、既有 Scene 失敗、重試、切換專案清除舊狀態、未存檔保護、真實 token 拒絕及 390px。進度條實測 indeterminate，另以受控 GUID 當前／總數驗證 stage progress，不是實際全流程百分比。4 個刻意注入 HTTP503 與 1 個真實 token 拒絕 HTTP403，無其他錯誤 |
| 靜態介面檢查 | `scripts/check-web.mjs` 通過；`premium-audit-scene-load.json` strict 0 findings。靜態檢查不取代上列瀏覽器操作 |

表內未加根路徑的證據位於 `artifacts/version-r8`。測試原始紀錄全部保留；不能將先前失敗的 assertion 或建置紀錄改寫為通過。最初 `browser-final/result.json` 的 Save 檢查為主動取消：當時 QA 佔用了 5286 共用工作階段，使用者反映 Scene 沒反應，因此取消 QA GUID 檢查、確認原生檔案未變，僅復原該筆 QA 位移命令。該原始紀錄保持 FAILED，不當作通過；後續全部在獨立 5288 重新實測，記錄於 `browser-isolated-fix1`。驗收官請自行啟動其他埠及自有 QA 資料，不對使用者 5286 注入測試修改。

Scene 選單原本在改選時自動讀取，同一項目再選不會再次觸發 change。fix1 保留自動載入，並增加相鄰「載入 Scene」按鈕；場景狀態列顯示原生解析／來源預覽／完成／部分失敗及重試提示。預覽失敗不再被完整成功訊息覆蓋，舊服務 token 拒絕會明確提示重新整理並保留輸入。未存檔 Save／Discard／Cancel 與同來源安全限制均保留。

驗收官在 fix1 首次載入失敗時發現狀態列被 renderState 的無場景分支改回 idle（WEB-SCENE-001／P2）。底部錯誤、所選路徑與按鈕原本仍存在，沒有資料遺失。fix2 保留這個 error 狀態，並依 project root 清除不同專案的舊狀態。原始缺陷與獨立證據在 `artifacts/acceptance/baseline-r8-20261022/WEB-SCENE-001.json`。新增的視窗進度條在 loading 期間顯示；GUID 索引提供合法 current／total 才顯示該階段百分比，其他時間保持不定進度。完成、失敗或切換專案後會隱藏，不顯示猜測的全程百分比。

fix2 第一趟新增的驗收腳本曾發生 Playwright「Route is already handled」基礎設施錯誤；其部分輸出不當作完整通過。改為單一預覽攔截器後，以新的原始結果目錄 `scene-load-progress-fix2-retry` 完整重跑 9 組通過。另有 QA 服務尚未就緒的連線拒絕紀錄及獨立內容檢查腳本初期路徑／enum 校正紀錄，全部保留，不宣稱產品通過證據。

## 獨立複驗結果與完整內容比對

驗收官已對 `baseline-r8-fix2` 作出 VERIFIED（僅本輪範圍），WEB-SCENE-001 關閉。正式報告為 `artifacts/acceptance/baseline-r8-20261022/VERDICT-fix2.zh-TW.md`；摘要 SHA256 `74f62cf2bf38887e99e23bf8db4034edc7f90122da84202d5929e715db88f584`，104 項原始證據清單 SHA256 `0579390153f352f7634faeef136e47caf1458c2dff45790d97cdee66479362dd`。獨立核對128來源、6項增量、9個 DLL，另建置並核對 served21 項。25 組獨立功能檢查包含沿用的副本／直接模式10組、2022.3已知欄位1組、CLI5組，以及新增Scene／進度9組；沒有把163個原整合測試再加總成獨立案例。受控進度數量只證明該階段顯示；無假全程進度。獨立驗收的測試腳本錯誤及補測保留在原始證據與 harness-corrections.json。

上述 fix2 獨立驗收當時尚未包含完整新複本逐檔比對。現在唯讀驗收程式已完成，實際原始結果 `native-content-comparison.json` 為 VERIFIED_EXPECTED_VERSION_ONLY_DIFFERENCE，exit0。來源是修復後的 `E:/2026場景編輯器/samples/Client231229`，目標是 `Workspace/Baselines/Client231229-2022.3.13f1`；比較涵蓋 Assets／Packages／ProjectSettings／Library/PackageCache，不含工具交易資料與Unity暫存資料。各71,819檔、5,821,816,843 bytes，完整清單無增缺、SHA256只有目標ProjectVersion有意差異；其SHA正好符合準備紀錄，來源全數仍符合原複製清單。檢查前後檔案大小／mtime清單一致，複製清單的hash也一致。這是來源E複本與新目標的完整比對，不宣稱原始D全目錄逐檔重跑；D保護的最新限定範圍仍為96項原生資源／meta及10個來源watch。

`content-completion` 補交驗收程式的固定來源、實際執行檔hash、最新164個版本／套件輸入pin及原始D限定範圍的再次核對。最新 CLI `artifacts/logs/baseline-r8-version-completion.log` exit0，78套件宣告及依賴檢查成立，NativeStatus仍UNVERIFIED。原128來源與9個應用DLL仍符合 fix2 身份；不修改應用程式、不重算已通過的163測試與25項獨立功能檢查。

兩趟 CLI verify-copy 與第一個 Python 清單掃描因 I/O 耗時被停止，全部原始紀錄保留，不當作PASS。最終比對使用快取目錄資訊的唯讀驗收程式 `artifacts/version-r8/content-check`，不是替代應用程式的安全回存檢查。完成結果與最終證據清單交由驗收官獨立複核；完整資料內容通過仍不能當作Unity匯入、編譯或真正雙向往返通過。

人類另外在驗收對話要求後續以 `Assets/02.Arts/Scene/Map/Map_60001/Map_60001.unity` 為主要預覽驗收場景，並回報粉紅線框的實際畫面問題（WEB-PREVIEW-60001-001）。該問題另案追蹤；本輪版本／完整內容比對的通過不代表60001預覽已修好，也不以歷史Room_main或小fixture取代60001的預覽驗收。

必要驗收包括：7 項原矛盾在來源仍可重現、新目標離線宣告與完整實際套件相容；新目標的場景與既有 ASE 材質可編輯／保存／重新讀取；原始資源保護；checkbox 兩種實際操作與寫入路徑、直接模式逐檔備份與失敗／衝突保護。未變更的 r7-fix1 數值及 Shader 證據可按差異沿用，不宣稱重跑整批 14 Shader 或真正 Unity 相似度。

本輪未完成且不能宣稱通過：Unity 原生匯入、程式及 Shader 編譯、Unity 修改後的真正雙向往返、畫面相似度，以及完整需求中尚未交付的地形／建模／尋路生成／光照烘焙等功能。原方案仍以 `docs/APPROVED_PLAN.zh-TW.md` 為準。

選擇依據：[Unity URP 相容表](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@14.0/manual/requirements.html)、[Package manifest 的 unity／unityRelease](https://docs.unity3d.com/2022.3/Documentation/Manual/upm-manifestPkg.html)、[2022 LTS 升級差異](https://docs.unity3d.com/2022.3/Documentation/Manual/UpgradeGuide2022LTS.html)。具體 patch 的最低條件以本專案實際 package.json 為準，不用目前網站系列頁面的新 patch 取代 14.0.9 等原輸入。

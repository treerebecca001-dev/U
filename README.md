# SceneWorkbench

獨立 Unity 相容場景編輯器，目前提供**從原始碼啟動的本機網頁開發版**。瀏覽器介面搭配本機 C# 服務，讀取 Unity 專案、預覽來源模型與貼圖、編輯場景屬性及既有 ASE 材質，回存原生 `.unity`／`.mat`。不啟動 Unity、不製作安裝包或壓縮交付。

最新後端驗證為 191/191，原始 TRX 與 SDK 重試紀錄見 `artifacts/foliage-transform-r14/test-results` 及 `docs/HANDOFF_FOLIAGE_R14.zh-TW.md`。Client231229 的 Room_main 已顯示來源 FBX／原生 Mesh／Prefab 幾何、實際牆地板貼圖及 ASE_Role 角色。Material Inspector 支援數值、色彩／向量、Tiling／Offset、2D 貼圖指定、Undo／Redo 與原生材質安全存檔。14 支固定 ASE Shader 已做瀏覽器編譯與操作測試；其中 9 支使用既有 Client 材質，另 5 支沒有既有實例，使用原生預設材質測試。完整 Unity 畫面比對、地形編輯、Prefab 編輯、Play、建模、尋路與烘焙仍有後續門檻。

R14 最新入口為 [http://127.0.0.1:5408/](http://127.0.0.1:5408/?source=foliage-r14)。指定兩株 TreeA01 的根 Transform 重複套用與歷史植物材質雙面／固定裁切已完成獨立複驗；61 份來源、58 種直接 Mesh、三份 Gress 保留及拖曳／Esc／Undo／重讀回歸分項通過。405 檔獨立證據已逐檔核對，見 `artifacts/acceptance/foliage-r14-20261027/VERDICT.zh-TW.md`。R13 另有289檔獨立證據收件：HTML原生接管的trusted pointercancel、還原及零提交限定通過。一般OS失焦／hidden與pointercancel後的晚到pointerup仍未驗證，沒有把未記錄的事件推論為UA沒發出。總狀態維持UNVERIFIED／局部通過，合併結果見 `docs/VALIDATION_R13_R14.zh-TW.md`。

5406 的舊工作階段含一筆 Redo，維持運作並保留原本來源。R14 由 `artifacts/foliage-transform-r14/source-snapshot` 與已測 runtime 提供普通本機開發服務，沒有打包。重新啟動 R14 使用 `./scripts/start-reviewed-r14.ps1 -Port 5408`。後續程式修改位於 `artifacts/foliage-transform-r14/source-work`；`source-snapshot` 是交驗身分固定的參照，請勿直接修改。根目錄 `src` 尚未取代舊5406來源，以免舊服務從同一路徑讀到一半更新的 JS 與不相符後端。

R15（2026-10-28）增加隔離的獨立pointerup／pointerdown觀察器；正式程式沒有修改，入口仍是5408。Producer實測changed→trusted pointercancel→還原、完整state／graph／鏡頭零提交；取消後到新down前沒有pointerup，新點擊重用pointerId不算晚到放開。驗收官已核對原始證據及37份served資產，但其瀏覽器控制初始化失敗，沒有獨立UI操作；一般OS失焦／hidden和同一cancelled手勢晚到pointerup維持UNVERIFIED。設施阻擋、保留工作階段及收件狀態見 [R15驗證紀錄](docs/VALIDATION_R15.zh-TW.md)。

正常使用的目標是不需安裝、啟動或授權 Unity。Unity 僅用於另外的雙向相容性驗收；缺少可用的 Unity 環境不會阻止獨立模組開發及離線資料驗證，尚缺的 Unity 端驗收會明確保留。

Map_60001 是目前的主要畫面驗收場景，Room_main 保留作迴歸。r9 修正直接 FBX 的原生材質槽與巢狀 Prefab 實例覆寫；只在來源有唯一 Mesh／Renderer 時套用，不以檔名或幾何順序猜測。缺少 Shader 的材質可用已存主貼圖／色彩做明示的唯讀近似。r9-fix1 的水深／交界／波動已獨立複驗；fix2 再讀同場景 LightingData 精確綁定的原始反射 EXR，並按真正啟用的方向光、Renderer、Layer 與接影旗標投影。固定範本與 11 個 HLSL 函式庫保持雜湊檢查。湖水原始波速 0、海水 0.8，兩者法線強度 0，表面泡沫／折射／焦散關閉，預覽尊重這些差異。r11 已找回原草地資源，缺層問題及唯讀回歸通過獨立複驗。完整 URP GI、CSM／Shadowmask、局部反射探針混合及 Unity 像素比對仍待完成，三筆既有 Prefab 覆寫診斷與 257 項近似繼續保留。最新結果見 `artifacts/acceptance/terrain-recovery-r11-20261027/VERDICT.zh-TW.md`。

Map_60001 的原生 TerrainData 現可唯讀載入 513×513 高度、三個圖層參照及 RGBA alphamap，按來源高度繪製並保留父層變換。Inspector 顯示尺寸、實際高度範圍、世界原點與限制。r11 從同層 `Client` 找回原 GUID／fileID 對應的 Grass Layer、原 PNG 與 metadata，只在 E 工作複本新增五個原本缺失的原生檔；完整高度與權重保持原樣，三層及重新載入已實測。其他來源缺層時仍顯示診斷棋盤。Terrain 雕刻、圖層繪製、非空 holes、碰撞模擬和原生高度回寫尚未實作。

## Git 原始碼範圍

Git 保留根目錄的基礎版本，以及 `artifacts/foliage-transform-r14/source-work` 最新開發來源和 `source-snapshot` 固定交驗來源。後續 R14 開發請從 `source-work` 進行；根目錄 `src` 仍保留舊服務使用的版本。

本機 SDK、NuGet 快取、編譯產物、測試證據與 `samples`／`Workspace` 專案資源不納入 Git。文件中的 `artifacts` 證據路徑是本機紀錄；`start-reviewed-r14.ps1` 另需本機已測 runtime。新複製的儲存庫請先在 `artifacts/foliage-transform-r14/source-work` 執行 `scripts/bootstrap.ps1`、`scripts/bootstrap-web.ps1`，再依下節的 `scripts/start-web.ps1 -Port 5410` 從原始碼啟動。

## 啟動網頁開發版

目前 R14 入口為 <http://127.0.0.1:5408/?source=foliage-r14>；舊5406及其Redo維持原樣。R14 可直接重開：

```powershell
cd 'E:\2026場景編輯器'
.\scripts\start-reviewed-r14.ps1 -Port 5408
```

下列既有 `start-web.ps1`／bootstrap 指令用於各自來源目錄的建置開發；根 `src` 目前仍是保留舊5406的基礎版本。要修改 R14 請使用上列 `source-work`，不要把根來源版誤當成已複驗的新版本。

```powershell
cd 'E:\2026場景編輯器\artifacts\foliage-transform-r14\source-work'
.\scripts\start-web.ps1 -Port 5410
```

原基礎版本的啟動方式：

```powershell
cd 'E:\2026場景編輯器'
.\scripts\start-web.ps1 -Port 5406
```

開啟 <http://127.0.0.1:5406/>。終端按 Ctrl+C 停止該服務。`-Port` 可指定其他空閒埠；目前保留舊 5286 工作階段。已建置且原始碼未變時可用 `-NoBuild`。網頁檔案由來源直接供應，修改 HTML／CSS／JS 後重新整理即可；C# 修改後重啟服務。

新開發環境先執行以下命令，僅安裝工作區 SDK 與固定依賴，不需要 Godot 或 Unity：

```powershell
.\scripts\bootstrap.ps1
.\scripts\bootstrap-web.ps1
.\scripts\start-web.ps1 -Port 5406
```

1. 按「開啟專案」輸入 Unity 專案根資料夾。「使用專案副本」預設勾選，建立並開啟指定的新複本；取消勾選則直接讀寫所選專案，儲存時只備份變更檔案。介面會顯示模式與實際路徑。開啟本身不改版本、套件或原生資源；未支援的欄位維持唯讀。
2. 已建立的範本工作複本為 `E:\2026場景編輯器\samples\Client231229`；主要驗收選擇 `Assets/02.Arts/Scene/Map/Map_60001/Map_60001.unity`。若已準備新的 `Workspace/Baselines/Client231229-2022.3.13f1` 並取得完整基準標記，預設範本入口會改開這份隔離複本；也可用 `start-web.ps1 -SampleProject <工作區內完整複本>` 明確指定。

選擇 Scene 後會自動載入，也可按選單旁的「載入 Scene」重新讀取目前選項。Scene 下方顯示載入階段，視窗內顯示讀取進度條；能取得資源總數時顯示目前階段的實際數量，其他階段使用持續讀取狀態。失敗會保留原因並可重試；新專案會清除舊場景狀態。這項流程已通過 9 組實際 Chrome 操作（含首次載入失敗、未存檔保護、進度條、錯誤重試及 390px 畫面）；原始紀錄在 `artifacts/version-r8/scene-load-progress-fix2-retry/result.json`。驗收長任務使用另一個本機服務，不佔用操作中的 5286 工作階段。
3. 選取 Hierarchy 的可編輯物件，修改名稱、Position、Quaternion Rotation、Scale、Active、Layer 或 Static，按「套用屬性」及「儲存」。未支援物件有唯讀標示。
   Scene 工具列新增 Q 檢視、W 移動、E 旋轉、R 縮放，提供世界／區域座標與吸附間距。縮放使用區域軸；拖曳結束後沿用同一筆原生屬性命令，按「儲存」才寫入 `.unity`。數值欄仍可精確編輯；Prefab 與未知欄位不因此開放寫入。r10-fix1 的點選、拖曳、父層與原生存檔等限定範圍已獨立複驗。r12 修正取消後鏡頭跳動；60001 移動／旋轉／縮放與輸入框取得焦點後的四次中途 Esc 已獨立複驗，放開後沒有新增命令，正常後續拖曳維持單步。blur／visibility／pointercancel 的實際事件仍待驗證，三筆 Prefab／所有取消路徑的整體判定仍為 UNVERIFIED／局部通過。最新結果見 `artifacts/acceptance/prefab-cancel-r12-20261027/VERDICT.zh-TW.md`，交驗範圍見 `docs/HANDOFF_PREFAB_CANCEL_R12.zh-TW.md`。
4. Undo／Redo 以屬性為單位；重新載入可讀回磁碟資料。外部檔案變動會阻擋覆蓋並保留輸入。
5. 選取物件後，在 Inspector 的 Renderer 材質按鈕開啟 `.mat`，或直接選取 Project 材質。數值可即時預覽；貼圖可輸入 `Assets/` 路徑或由 Project 拖入。按「套用材質」後，再按「儲存」寫入原生 `.mat`。
6. Project 的 FBX／支援的原生 Mesh 有幾何預覽；TGA／BMP 也能檢視。碰撞輔助顯示、材質預覽時間只改動畫面，不改原資源。

Scene 使用固定 Shader 表面適配，尚未完成 Unity 燈光／烘焙／後製、SceneDepth 軟邊及 UI Stencil／裁切的畫面校準。Prefab 展開供預覽，編輯覆寫、Rig／動畫及無法唯一對應的 FBX 子資源另待驗證。Shader 來源變更會標示適配失效。來源檔案留在本機；本機服務只綁定 127.0.0.1。

開啟專案先載入來源資源與場景清單。套件資料夾可直接逐層瀏覽；首次全域搜尋才建立完整套件搜尋索引，大型專案可能需要較久，清除或更改搜尋會取消舊請求。存檔仍執行原有的完整相依檢查。5.3 秒是本機單次觀察，沒有冷快取或所有機器的效能保證。

啟動 SDK、依賴及初版介面限制見 `docs/HANDOFF_WEB_DEV_20261022.md`，r6 凍結證據見 `docs/HANDOFF_WEB_DEV_20261022_R6.md`；最新交驗見 `docs/HANDOFF_PREVIEW_R7.zh-TW.md`，Integer 修正複驗見 `docs/HANDOFF_PREVIEW_R7_FIX1.zh-TW.md`；版本基準與可選直接存取交驗見 `docs/HANDOFF_BASELINE_R8.zh-TW.md`。原始完整需求保留在 `docs/APPROVED_PLAN.zh-TW.md`。

## 共用核心與 CLI 功能

### 離線版本基準

`prepare-baseline` 保留原來的 2021.3 工作複本，建立包含完整套件快取的新複本，固定 2022.3.13f1／5f90a5ebde0f 的目標宣告。原有套件版本、直接／間接依賴與資源保持原樣；先核對實際 package.json、manifest、lock、最低 unity／unityRelease、相依版本及 GUID，通過才以具備備份的 transaction 調整新複本的 ProjectVersion。準備紀錄明示沒有進行 Unity 匯入或資源遷移，不代表引擎驗收。

```powershell
.\scripts\dotnet.ps1 src/SceneWorkbench.Cli/bin/Release/net10.0/SceneWorkbench.dll prepare-baseline --project samples/Client231229 --destination Workspace/Baselines/Client231229-2022.3.13f1 --output artifacts/new-baseline-report
.\scripts\dotnet.ps1 src/SceneWorkbench.Cli/bin/Release/net10.0/SceneWorkbench.dll verify-baseline --project Workspace/Baselines/Client231229-2022.3.13f1 --compare-source
```

兩個目的地必須不存在，準備程序不覆蓋舊複本。`verify-baseline` 重查被固定的版本與套件輸入；一般場景／材質編輯不改動這些輸入。這項檢查不取代完整資源驗證，`verify-copy` 仍如實回報有意調整的 ProjectVersion 與原始複本清單不同。

2021.3／2022.3 穩定版只開放已知 GameObject／Transform 與 Material scalar／reference 路徑；2022.3 額外識別 Transform serializedVersion 2。未知結構、連結／stripped Prefab、Material Variant、未支援 Editor 家族維持唯讀。嚴格 CLI 不接受使用者提供任意版本覆寫來略過阻擋。

- 直接讀取 Unity 專案，檢查 Editor／套件版本、manifest／lock 一致性、套件內容、GUID、缺失資源、場景引用、Layer、ASE Shader 與原生二進位樣本。
- 建立新工作複本，包含 `Assets`、`Packages`、`ProjectSettings`，預設附帶 `Library/PackageCache`。逐檔記錄 SHA-256，可再次核對來源與複本。
- 保留 UTF-8 BOM、換行、未知元件與未變更原文，局部修改完整 Transform 的座標；支援 Undo／Redo。
- 回寫前檢查工作複本資格、路徑、套件與 GUID／資源完整性阻擋及外部修改；以存檔紀錄和備份處理中斷、衝突及重啟復原。
- 用 AssetsTools.NET 讀取 Unity serialized 資源並在隔離輸出位置驗證容器重寫。這項檢查不等於 Terrain 編輯、Unity 端驗收或新烘焙生成。

## 建置與驗證

需要 Windows x64、PowerShell，以及 `global.json` 固定的 .NET 10 SDK。共用核心為 .NET 8；本機 Web、CLI 與測試使用 .NET 10。網頁版使用固定 three.js 0.180.0，本機啟動後不使用 CDN。

```powershell
.\scripts\dotnet.ps1 restore tests/SceneWorkbench.Tests/SceneWorkbench.Tests.csproj --configfile src/SceneWorkbench.Web/NuGet.Config --locked-mode -m:1 -nr:false
.\scripts\dotnet.ps1 test tests/SceneWorkbench.Tests/SceneWorkbench.Tests.csproj --configuration Release --no-restore --disable-build-servers -m:1 -nr:false -p:UseSharedCompilation=false
node scripts/check-web.mjs
```

SDK 只下載到工作區 `.tools`，先依 Microsoft 官方 metadata 核對 SHA-512，不修改系統 SDK。NuGet 套件位於工作區 `.nuget`，版本由各專案 `packages.lock.json` 固定。

舊 CLI 固定產物與 Godot 桌面匯出僅保留為歷史驗證紀錄；本次交付使用來源開發服務，不需要重新發佈。

## 參考專案診斷

```powershell
.\scripts\dotnet.ps1 .\src\SceneWorkbench.Cli\bin\Release\net10.0\SceneWorkbench.dll audit `
  --project 'D:\Work_ART\漂流NFT\@WLN_work\Client231229' `
  --output 'E:\2026場景編輯器\artifacts\reports\new-baseline'
```

輸出資料夾必須是新資料夾，且位於受檢專案之外。產生 `baseline.md`、`baseline.json` 和 `asset-index.json`。報告不輸出私人套件 URL 或套件原始內容。

來源範本記錄 Unity 2021.3.15f1，快取 URP 14.0.9 要求 Unity 2022.3。來源保留七項版本診斷，嚴格 CLI 寫入仍阻擋。新建的 2022.3.13f1 完整驗證複本已對齊離線套件宣告；這不等於 Unity 已匯入、遷移或驗收。網頁版的 GameObject／Transform 及既有 Material saved-property 編輯屬於分別驗證的有限欄位能力；其他完整性或未知格式阻擋仍拒絕回寫，不提供強制忽略選項。

## 工作複本與原檔保護

```powershell
SceneWorkbench.exe copy --project '<來源 Unity 專案>' --destination '<尚未存在的工作複本資料夾>'
SceneWorkbench.exe verify-copy --project '<工作複本>' --compare-source
```

來源與目的地不能相同或互相包含。程式拒絕 junction／symlink，不跟隨連結；不覆蓋既有目的地。取消或失敗的複本保留診斷資料，但無回寫資格。

複本標記與清單位於專案根目錄 `.scene-workbench`。不要手動改動標記、清單、備份或 journal。完整來源複本不包含可再生的其他 Library 快取、Logs、IDE 設定及專案根目錄的非 Unity 工作檔。

## 格式驗證及局部編輯

```powershell
SceneWorkbench.exe probe --project '<專案>' --output '<新隔離輸出資料夾>'
SceneWorkbench.exe patch-transform --project '<專案>' --scene 'Assets/Scene.unity' `
  --file-id '-2745811161023527127' --position '3.25,4.5,-5.75' --dry-run
SceneWorkbench.exe recover --project '<工作複本>'
```

`--dry-run` 只顯示變更。實際回寫限通過目前版本檢查的工作複本；不提供忽略版本、強制覆蓋或自動改版選項。stripped Transform／Prefab 覆寫尚未支援，會拒絕修改。

`probe` 的候選場景與容器重寫檔只放在隔離資料夾；報告固定標示缺少的 Unity 驗收及生成能力。可選擇 `--classdata <classdata.tpk>` 補充與 AssetsTools.NET 相容的型別資料。

## 退出碼

| 退出碼 | 意義 |
|---|---|
| 0 | 該命令的本機行為通過，不代表完整編輯器驗收 |
| 1 | 輸入錯誤、衝突、檔案保護拒絕或執行失敗 |
| 2 | audit 基準被阻擋，或 probe 本機語意檢查不完整 |
| 130 | 使用者取消 |

完整目標與後續關卡見 [實作狀態](docs/IMPLEMENTATION.md)。正式交驗規則見 [驗收說明](docs/ACCEPTANCE.md)。

目前固定產物、原始驗收結果與修正交驗見 [r6 交驗紀錄](docs/HANDOFF_STAGE0_R6.md)。

範本套件與 meta 修復、獨立運作要求和目前驗證範圍見 [2026-10-22 交驗紀錄](docs/HANDOFF_BASELINE_REPAIR_20261022.md)。

# Client 模型與 ASE 材質網頁開發版交驗

本輪交驗的是無需 Unity 的本機來源開發版。主入口 `http://127.0.0.1:5286/?source=preview-r7`；啟動命令仍為 `scripts/start-web.ps1`。沒有執行 publish、桌面匯出、安裝包、壓縮打包或 Unity。原始 `D:/Work_ART/漂流NFT/@WLN_work/Client231229` 全程唯讀。

## 固定來源

正式來源記錄在 `artifacts/preview-r7/handoff-v2/source-manifest.json`，普通檔案複本在 `artifacts/preview-r7/source-snapshot-v2`，不是發佈包。首份私有 r7 快照的材質數量說明誤寫為 8／6；本版改正為逐案結果的 9／5，並將原快照保留。產品程式與測試結果沒有因此改變，舊 r6 清單也保持凍結。

正式 source-manifest SHA-256：`ac9f015f91dfb30295292fe3788fbfe1d00e128a40da074ee15afd5e3977a3a3`，共 112 檔。證據清單在 `artifacts/preview-r7/handoff-v2/evidence-manifest.json`。

工作區編譯識別在 `artifacts/preview-r7/handoff-v2/runtime.json`：Web DLL `a06e1be1c15703cd41c7ea39ae9d50a500dd4abd32aad948d02b4cff6c66b7ae`、Unity 格式層 `743b7ae30f342993fb8647efea80de83f02b8c47642691501bd351b7e2990471`、Core `38b9e9f376c5f7182f87c7ab54d1040479a147c4408daf372fb6967c47a4be92`。

隔離重建時使用已存在的工作區 SDK／NuGet 快取，從所選來源重新建置 Web／Tests；不要把舊 DLL 配上新的 ContentRoot。Development 的 `staticwebassets.runtime.json` 會保留編譯時的絕對路徑，必須逐份核對實際供應的 HTML、JS、CSS、Shader pin JSON 與 vendor 檔。HTML 先把每次啟動的 token 正規化成 `__WORKBENCH_TOKEN__`。

可用工作區 `.tools/dotnet/dotnet.exe` 的絕對路徑，對 snapshot 的 Web csproj 執行 `restore --packages E:/2026場景編輯器/.nuget/packages --locked-mode --disable-parallel`，再 `build --configuration Release --no-restore --disable-build-servers -m:1 -nr:false -p:UseSharedCompilation=false`。啟動所建 DLL 時將 `SCENE_WORKBENCH_ROOT` 設為 snapshot 根目錄，使用獨立埠；該 host 的 QA 複本目的地也必須位於其根目錄內。工作區整合測試命令為 `scripts/dotnet.ps1 test tests/SceneWorkbench.Tests/SceneWorkbench.Tests.csproj --configuration Release --no-restore --disable-build-servers -m:1 -nr:false -p:UseSharedCompilation=false`。

## 本輪能力與實作邊界

- Scene 展開原生 Prefab 供唯讀預覽，解析 Transform、Renderer 材質槽、基本覆寫及 Mesh 參照。讀取來源 FBX 和支援的 Unity Mesh YAML v9/v10；原生索引、對齊頂點串流、UV、Normals、Tangents 與子網格維持原檔。
- 網頁使用固定 [three.js FBXLoader](https://threejs.org/docs/pages/FBXLoader.html)／[TGALoader](https://threejs.org/docs/pages/TGALoader.html)。不依賴 Unity 背景匯入。FBX 依明確名稱表或唯一幾何選取；多個子資源無法對應、非已驗證軸向或未知格式會標示診斷，不猜測 fileID。
- 14 支 Client ASE Shader 以 GUID＋原始 SHA-256 綁定固定 GLSL 表面適配；沒有通用 ASE 編譯器或節點編輯器。原 Shader、ASE 節點與 checksum 不修改。基本 URP 材質另有來源貼圖／色彩表面預覽。
- Material Inspector 提供既有 saved properties 的 Float／Int／Color／Vector、2D 貼圖、Tiling／Offset。數值草稿預覽材質球，貼圖套用後更新。整批驗證通過後才加入各屬性的 Undo；Save 寫原生 `.mat`，保留其他 Unity 物件與未知欄位。Keywords、Cube／Array 指定及缺少的 saved entries 維持唯讀。
- Save／Undo／Redo 操作目前文件。切換場景與材質沿用 Save／Discard／Cancel；有外部修改、stale revision 或非法資料時保留輸入。材質存檔檢查內容、meta、Shader、貼圖、設定及完整 GUID／套件基準，沿用備份與 transaction。
- Project 可直接預覽 FBX／原生 Mesh 幾何及 PNG／JPEG／WebP／TGA／BMP 圖片。材質與模型 Inspector 共用可釋放 WebGL context 的預覽元件。碰撞輔助顯示與預覽時間不改動原生資料。
- 衍生 GUID 預覽索引存於工作區 `artifacts/cache/preview-guid`，以檔案／metadata 清單檢查是否失效；取消不發布部分結果，場景重載會重建 resolver。這個快取不取代原生存檔完整性檢查。

## 原始測試證據

`artifacts/test-results/preview-r7-consolidated.trx` 為 **113/113**，原始輸出 `artifacts/logs/preview-r7-consolidated.log`。包含原有 99 案例與新增材質、Mesh、快取、原生寫入及 HTTP 流程。先前失敗的測試和 log 保留；最後的實作與測試更正均納入整合結果。

| 瀏覽器紀錄 | 實際範圍 |
|---|---|
| `browser-final-source/result.json` | 真實 Client Room_main，7 網格／2,681 三角形，實際貼圖與 ASE_Role，無 pageerror／Console 錯誤 |
| `client-native-browser/result.json` ＋ `recovery-result.json` | 真實 Client 工作複本 GUI 改 `_Shininess` 和 `_ColorTextures`、原生 Save、重讀、Undo／Redo、恢復原始 bytes，390px 表單；首筆在成功存檔後因測試用 Float32 精確比較失敗，續跑更正比較後完成全部復原，沒有把失敗的 assertion 算通過 |
| `material-matrix-final/result.json` | 14 Shader 實際編譯／Inspector 操作、原生材質 Save／Undo、時間控制、連續切換 context、來源 FBX／Mesh、TGA／BMP；無 Console 錯誤或 context 洩漏 |
| `material-interactions/result.json` | 真拖放、錯誤整批保留、兩真分頁 stale 409、外部 meta 衝突、來源模型切回材質的 GPU 釋放、目前 TGA 解碼與 390px 介面；3 筆 HTTP 400／409 為刻意負向案例，無未預期錯誤 |

矩陣中 **9 支使用既有 Client 材質**；Afterimage、RoleMask、MonsterMask、WeaponMask、StarWindow 共 **5 支沒有既有材質實例**，使用原生 `.mat` 預設值測試，不能據此宣稱實際場景或 Unity 相似度通過。原始結果的 scope 文字有舊的數量誤植；實際 `materials[].actualClientMaterial` 為 9 true／5 false，詳見 `count-correction.json`。完整列表與來源在 `ase-materials.json`、`fixture-info.json`。

Client 原生材質恢復 SHA-256 為 `655efab93c0718c51f867fdd6a06040de4eb045e86c50e7248c472641082e4b7`。`handoff-v2/native-preservation.json` 另將本輪使用的 **96 份資源／meta** 與 D: 原檔、工作複本、fixture 比對，全部一致；這不是重跑整個 5.8GB 專案稽核。

`premium-audit-source2.json` 為 strict audit 0 findings；JS syntax 通過。主要有效截圖和每次實際供應的來源雜湊都在上述資料夾。早期 `browser-probe` 的附加 Material 物件讀取失敗，以及 `material-matrix` 的 context 洩漏紀錄保留，已由新增測試及後續實際瀏覽器結果取代。

## 驗收官重現入口

1. 開啟主入口，按「開啟 Client231229 工作複本」，選 Room_main。選 TouristWoman、牆、地板，按 F；檢查材質槽、貼圖與碰撞輔助視圖切換。
2. 從 Renderer 按鈕或 Project 開啟 `.mat`，測試數值／向量、Tiling／Offset、貼圖拖入及套用；檢查材質球、Scene 更新、Undo／Redo、native Save／重讀。先複製自己的 QA 專案，不與主機目前文件共寫。
3. 小型原生來源 `artifacts/preview-r7/fixture-source`；GUI 建立自己的完整工作複本。它含 47 份未改動的 Client 來源資源、相應 meta、14 個代表材質與小場景，是測試專案，**不是 Client 的完整複本**。用 `fixture-info.json` 和 `handoff-v2/native-preservation.json` 核對輸入。
4. 可檢視或重跑 `scripts/test-material-matrix-r7.cjs`、`scripts/test-material-interactions-r7.cjs`。測試使用已安裝的受支援 Playwright Library／Chrome；目前 CUA kernel 不可用，沒有關閉 Chrome sandbox。重跑前使用新的結果目錄或隔離副本，保留舊證據。
5. 依照本輪修改做原生 `.mat` 完整性、未支援資源、失效快取、取消、衝突與相鄰場景操作回歸。未失效的 r6 固定證據可重用，請回報具體資料、步驟、預期／實際、來源識別與 raw log；收到後直接修正和交複驗。

## 尚未放行

Unity 實際載入、雙向往返與畫面相似度仍為 **UNVERIFIED**。SceneDepth 軟邊、Unity 光照／Shadow／烘焙／後製、Canvas Stencil／ClipRect、完整 FBX 匯入差異、Prefab 增刪與巢狀覆寫、Rig／動畫、地形、Play、建模、粒子、尋路與烘焙保留原計畫門檻。介面的相應預覽會標示限制。

BL-001～007 的七項版本矛盾仍是 **Blocking**，沒有改 ProjectVersion、刪套件或隱藏診斷來關閉。本輪只交驗獨立預覽與已驗證的原生欄位能力，不以離線 UI／資料測試冒充 Unity 引擎驗收。不要重試 Unity 授權，也不要打包。

# 獨立場景編輯器：技術架構與開發計畫

文件狀態：使用者於實作對話「規劃 Unity 相容場景編輯器」透過「PLEASE IMPLEMENT THIS PLAN」核准的完整方案。此文件固定驗收範圍；目前實作進度、缺陷與未完成項目另見 IMPLEMENTATION.md 及交驗證據。

## 1. 目標與專案基準

製作 Windows x64 獨立桌面軟體，讓美術人員直接開啟 Unity 專案，編輯場景、模型、ASE 材質、地形、特效、尋路及光照，並回存 Unity 可使用、繼續編輯的原生資源。介面與操作習慣接近 Unity。

- 開發位置：E:\2026場景編輯器。
- 參考專案：D:\Work_ART\漂流NFT\@WLN_work\Client231229。
- 預設建立完整工作複本，保留原始範本。
- 正常使用時不啟動 Unity Editor、不依賴 Unity 背景轉檔服務；開發驗收時可手動使用 Unity。
- 先鎖定範本使用的 ASE 1.8.9r35、ProBuilder 5.2.2、Polybrush 1.1.5、A* Pathfinding 4.3.60，依版本建立相容模組。

**版本基準是第一個關卡。** 範本記錄 Unity 2021.3.15f1，但使用 URP 14.0.9；官方相容表將 URP 14 對應到 Unity 2022 系列。先保留現有版本與檔案，在驗證複本上確立可重現的 Unity 環境，再進行原生回存驗收。[URP 相容表](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@14.0/manual/requirements.html)

完整目標包含尚未證實可行的原生資料寫入工作。前期驗證失敗時，交付診斷與樣本差異，停止依賴該結果的開發；維持完整需求，不自行改成工具專用格式。

## 2. 技術架構

採用 **Godot 4.7.2 .NET＋C#** 製作桌面介面、3D 預覽及 Play 模擬，使用 Forward+ 渲染。Unity 原始資源是編輯資料來源，Godot 場景只負責顯示與模擬。[Godot 官方版本](https://godotengine.org/download/windows/)

| 模組 | 責任與技術 |
|---|---|
| 編輯器介面 | Godot Control、獨立 Scene／Game 視窗、可停駐面板、Inspector、資源樹、選取與操作工具 |
| 編輯核心 | 場景文件、資源參照、選取、命令、Undo／Redo、變更追蹤；與預覽引擎分離 |
| Unity 格式層 | YAML 解析配合保留原文的局部修改；二進位資源使用 AssetsTools.NET，另外實作各資源的語意與版本處理 |
| 匯入與索引 | Assimp 讀取 FBX；處理來源貼圖、.meta 設定、GUID、子資源與相依關係 |
| 預覽相容層 | 固定 ASE Shader、URP Terrain Lit、燈光、後製與專案 Renderer Feature 的對應實作 |
| 場景製作工具 | Terrain、種植、合併、ProBuilder、Polybrush、Prefab、粒子及尋路編輯 |
| 運算工作程序 | 獨立進程執行 UV2、尋路生成與光照烘焙，提供進度、取消與錯誤資訊 |
| 儲存層 | 驗證相依資源、分檔替換、復原紀錄、外部修改衝突偵測與崩潰復原 |

[AssetsTools.NET](https://github.com/nesrak1/AssetsTools.NET) 提供 Unity 資源容器讀寫；Terrain、NavMesh、Lighting Data 等資料的正確性，仍由專用相容模組負責。[Assimp](https://github.com/assimp/assimp) 負責模型解碼，Unity 匯入結果的識別與對應另外驗證。

核心介面固定為：

- ProjectSession：管理專案路徑、工作複本、套件版本與相容能力。
- UnityAssetId：以 GUID 與有號 64 位元 fileID 識別資源。
- EditCommand：所有可復原編輯統一經過命令系統。
- SimulationSession：Play 使用獨立場景狀態，停止後還原。
- BakeJob：接收幾何、材質與設定快照，輸出結果及診斷。
- SaveTransaction：集中提交場景、設定與生成資源。

資料流程為：

**Unity 專案 → 索引／解析 → 編輯文件 → 預覽與工具 → 變更驗證 → 原生檔案回存。**

## 3. 功能與互通規則

| 子系統 | 完成範圍 |
|---|---|
| 專案與資源樹 | 直接指定 Unity 專案；呈現 Assets／Packages、搜尋、縮圖、拖放與相依資源；保留 .meta 和 GUID |
| 場景與編輯視角 | Hierarchy、建立／複製／刪除／改父層、多選、移動旋轉縮放、吸附、世界／區域座標、自由飛行與正交視圖 |
| 模型與貼圖匯入 | FBX 縮放、軸向、法線、切線、材質與 UV2；貼圖色彩空間、Normal Map、尺寸、MipMap、Wrap、Filter 與平台設定 |
| 材質與畫面 | 編輯既有 ASE 貼圖、參數、Keywords；逐支適配範本的 14 支 ASE Shader；保留原 Shader 與 ASE 節點資料 |
| 燈光、攝影機與後製 | 基本光源、陰影、攝影機投影／FOV／遮罩、Scene／Game 預覽；URP Volume、Profile 與範本使用的後製效果 |
| 碰撞與 Play | Collider、Trigger、物理材質、碰撞矩陣；預覽玩家可第一／第三人稱走路、跑步、跳躍，測試坡度及階梯 |
| Prefab 與分類設定 | 建立與實例化、覆寫、Apply／Revert、巢狀 Prefab、Variant；保留 Layer／Tag 索引及各項 Static 旗標 |
| 地形與種植 | 雕塑、平滑、高度與 TerrainLayer 繪製；樹木、草及 Prefab 散布，支援密度、坡度、高度、隨機縮放與旋轉 |
| 合併與 ProBuilder | 可復原的網格合併；基本形狀、點線面選取、擠出、切割、細分、焊接、UV 與材質分面；保留原套件編輯資料 |
| Polybrush | 雕塑、平滑、頂點色、貼圖混合與散布；同步原網格、PolyMesh 與 Additional Vertex Streams |
| Particle System | 編輯範本使用的粒子模組、曲線、漸層、材質、貼圖、拖尾及子發射器；支援播放、暫停、重播與固定種子預覽 |
| 尋路 | 分別編輯及生成 Unity NavMesh 與 A*／Recast；包含 Agent 尺寸、坡度、障礙、區域成本、連接與路徑測試 |
| 光照烘焙 | 光照設定、UV2、Lightmap、Mixed、Shadowmask、Light Probes、反射探針，以及 Unity 原生綁定資料 |

實作時遵守以下規則：

- **預覽基準**：尊重範本目前的 Gamma 設定，逐項校準取樣、燈光、透明、陰影及後製；雲影、X-Ray、Decal、Tilt-Shift 納入範本畫面相容工作。
- **Shader 邊界**：提供固定 Shader 適配，不製作通用 ASE 編譯器或節點編輯器。Shader 來源改變時，標示適配需要重新驗證。
- **Polybrush 材質**：繪色與混合提供固定 URP 工具材質，保留既有 ASE 材質，由使用者選擇套用。
- **Play 邊界**：使用工具內建預覽控制器；原專案 C#／Lua 遊戲程式保留，不在獨立軟體執行。
- **FBX 邊界**：第一版以場景靜態模型匯入為主；Rig、Humanoid、動畫及進階攝影機腳本設定保留，另列後續相容能力。
- **未知資料**：未支援的元件、欄位與版本保留原文並顯示相容狀態；無法安全修改的部分維持唯讀。
- **存檔保護**：無變更不重寫；外部檔案已改動時，阻擋覆蓋並提供重新載入或另存。
- **運算失效**：幾何、UV2、位置、碰撞、Static 或 Layer 改動後，標示受影響的尋路／光照結果需要重新生成。

尋路採用 [Recast／Detour](https://github.com/recastnavigation/recastnavigation) 作為幾何生成與查詢基礎，另建 Unity 與 A* 的版本化讀寫器。

光照採用獨立 C++ 工作程序：以 [xatlas](https://github.com/jpcy/xatlas) 處理 UV2，自行實作基於 [Embree](https://github.com/RenderKit/embree) 的 CPU 光照運算，並使用 [Open Image Denoise](https://github.com/RenderKit/oidn) 降噪。材質適配器提供烘焙所需的表面資料。

此架構另設烘焙後端，因為 Godot LightmapGI 無法在一般匯出的應用程式內烘焙；Unity 的 Lighting Data Asset 也包含貼圖之外的 Renderer 與探針關係。[Godot 烘焙限制](https://docs.godotengine.org/en/stable/classes/class_lightmapgi.html)、[Unity 光照資料](https://docs.unity3d.com/cn/2021.3/Manual/LightmapSnapshot.html)

## 4. 開發順序與交付門檻

| 階段 | 工作與交付結果 |
|---|---|
| **0：建立基準** | 確認 Unity／URP 組合、資源完整性、套件與測試機規格；建立原檔雜湊、工作複本及最小驗證場景 |
| **1：高風險驗證** | 驗證 FBX 子資源識別、Terrain／Mesh 往返、ProBuilder／Polybrush 可繼續編輯、兩套尋路原生載入、完整光照資料載入，以及代表 ASE 材質的畫面 |
| **2：編輯核心與基本工作檯** | 完成索引、專案樹、Hierarchy、Inspector、基本場景操作、匯入設定、Prefab 基礎、Layer／Static、Undo／Redo 與安全存檔 |
| **3：預覽與試玩閉環** | 完成固定 ASE 適配、燈光、攝影機、後製、碰撞及 Play；形成「開啟 → 編輯 → 試走 → 儲存 → Unity 開啟」的第一個可用版本 |
| **4：場景製作工具** | 完成地形、種植、合併、ProBuilder、Polybrush，以及完整 Prefab 覆寫、巢狀與 Variant 工作流 |
| **5：特效與尋路** | 完成 Particle System 編輯器、相關資源管理、兩套尋路的參數介面、生成、除錯顯示及 Play 測試 |
| **6：完整光照流程** | 完成品質設定、UV2 檢查、圖集、混合光照、Shadowmask、探針、生成進度、取消及原生回存 |
| **7：整合與發佈** | 大場景效能、長時間穩定性、復原、安裝包、相容清單、操作說明及完整往返驗收 |

階段 1 使用小型樣本驗證資料完整流程，全部通過後才擴展完整工具介面。驗證必須包含**獨立產生新資料**；保留既有烘焙結果不能作為生成能力的驗收。

工期與人力估算放在階段 1 結束後，以實測結果制定。

## 5. 測試與驗收

- **獨立運作**：在未安裝 Unity 的測試環境完成匯入、編輯、Play、尋路生成、光照烘焙及儲存。
- **雙向往返**：獨立編輯器修改 → Unity 開啟與修改 → 獨立編輯器重新讀取 → 再回存 Unity，資源參照及編輯資料持續有效。
- **資料完整性**：未編輯資源保持原樣；GUID、fileID、Prefab 關係、未知元件與 ASE 節點資料不遺失。
- **畫面比對**：使用相同攝影機、時間、材質、燈光與後製逐項比對；以固定測試圖及人工確認驗收，不預設相似百分比。
- **建模與地形**：Unity 可繼續用原 ProBuilder／Polybrush 工具編輯；Terrain 高度、圖層、樹草及碰撞正確。
- **尋路**：兩套系統各自測試窄路、坡道、障礙、區域成本及連接；Unity 端使用原有系統載入生成資料。
- **光照**：Unity 重新載入後直接使用獨立生成的 Lightmap、Shadowmask 與探針，確認綁定、動態物件受光及混合陰影。
- **穩定性**：測試外部修改、缺少資源、取消運算、存檔中斷與崩潰復原。
- **效能**：依序使用 Room_main、Map_12006、約 50 MB 的 Map_60003 驗證；在記錄規格的測試機上，以 1080p 編輯視角達到至少 30 FPS 為初始目標，長任務保持介面可操作。

介面預設繁體中文，保留 Unity 常用英文術語；以 Hierarchy、Scene／Game、Inspector、Project、Console 為主要布局，沿用常用快捷鍵、拖放及選取習慣。

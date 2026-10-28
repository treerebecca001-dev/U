# Map_60001 原生材質與地形高度交驗（r9）

本輪依人類指示直接修正、交驗及複修，不啟動 Unity、不重試授權、不打包。主要場景固定為 `Assets/02.Arts/Scene/Map/Map_60001/Map_60001.unity`。核准完整方案仍在 `docs/APPROVED_PLAN.zh-TW.md`，其內容與雜湊未變。

## 已實作與根因

- `WEB-PREVIEW-60001-001`：直接 FBX 分支原先忽略原生材質與 Transform 覆寫。現在以精確 GUID、字串 fileID、材質槽及 stripped Prefab 實例 correspondence 解析，並確認材質 fileID 確實存在。瀏覽器還要求來源唯一 Mesh／原生 Renderer，否則保留幾何與診斷，不用檔名或順序猜測。原生 Enabled／Active 仍控制顯示。
- 缺少 Shader 來源的 TreeA01／TreeA05 等材質提供唯讀 saved properties，保留貼圖 GUID/fileID、Tiling／Offset、顏色、精確 Int32。用可識別主貼圖與原生 `_AlphaCutoff` 做明示的表面／裁切近似，消除不透明白底；風動、完整原 Shader 與 Unity 光照仍未適配。原生材質寫入能力沒有因此擴大。
- Stylized Water 2 `.watershader` 以 GUID `d7b0192b9bf19c949900035fa781fdc4` 與 SHA-256 `ca1398d72f423e9a74c2c5e6b548b0ee715071de1f09273a07f7b6ff1c89a24a` 固定範本，只近似原生深淺色、Alpha、移動法線。不編譯模板、不開放材質寫入；折射、反射、SceneDepth、波浪位移、陰影與完整泡沫未重現。
- 1178px Inspector 溢出來自元件負邊距套到未含 padding 的 Renderer 材質區；已將邊距限制在表單內，向量欄位可收縮，沒有用隱藏 overflow 遮住輸入。
- `WEB-TERRAIN-60001-001`：Terrain 218 沒有加入預覽元件集，原檔高度沒有遺失。新增 AssetsTools.NET/type-tree 唯讀 reader，讀取精確 TerrainData、完整高度格、原生尺寸、holes 儲存資訊、TerrainLayer 參照與 RGBA32 alphamap。畫面尊重 Terrain enabled、DrawHeightmap、祖先變換與顯示狀態，Inspector 顯示高度與資源限制。

## 實際場景結果與邊界

| 項目 | 原 r8 | r9 實測 |
|---|---:|---:|
| 原生場景物件／預覽節點 | 3078／6613 | 3078／6613 |
| 缺少材質槽的模型節點 | 265 | 0 |
| FBX 原生覆寫綁定 | 未處理 | 265 |
| 材質快照 | 22 | 28，保留六種 TreeA01 變體 |
| 原有模型幾何 | 3104 網格／108877 三角形 | 保持；另加原生 Terrain |
| Terrain 幾何 | 未顯示 | 263169 頂點／524288 三角形 |
| 合計幾何 | 3104／108877 | 3105／633165 |

TerrainData 為 `Assets/02.Arts/Scene/Map/Map_60001/Map_60001.asset`，GUID `95b234315861c5b4fa9a36b90f7c9e8e`、fileID `15600000`，SHA-256 `5599b8d0b42efd1b2ddf2b81e67a9f0d26bac7c57cd81b602f1001d7b674dcd3`，內部版本仍為 2021.3.15f1。

完整高度圖 513×513，原生 SInt16 最小 55、最大 519、465 個不同值；中心為 233、四角為 55。原生 sample spacing `(0.46875,600,0.46875)` 對應尺寸 `(240,600,240)`。依 [Unity 2021.3 PaintContext 的高度比例](https://github.com/Unity-Technologies/UnityCsReference/blob/2021.3/Modules/Terrain/Public/PaintContext.cs) 與檔內 patch min/max 交叉核對，採 raw/32766×size.y，得到本地高度約 1.0071～9.5038 m。網格依 [GetHeights 的 z/x 排列](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/TerrainData.GetHeights.html) 轉為預覽座標，原生 +Z 映到預覽 -Z。世界原點實測 `(-62.7000,0,-8.2000)`，保留父 Map_60001 變換。

三個 TerrainLayer 的原生 fileID 均保持字串 `8574412962073106934`，對應一張 512×512 RGBA32 控制圖。Sand／Soil 來源貼圖和 9×9 Tiling 已讀入。第一個 GUID `3a2a9d837fbf13e4b9197bb9b1ba3484` 在 Assets／Packages 索引與套件 `.terrainlayer.meta` 搜尋未解析，保留棋盤、原始 GUID 與「1 地形圖層未解析」，沒有替換來源參照。這是現存相依缺口，不能宣稱完整地形材質或整體 Unity 畫面已一致。

holes／holesLOD 陣列在這份資源均為 0 項。非空遮罩尚未解碼時保留資訊並拒絕繪製假定無洞的表面。高度預覽限可解析 type tree 的 2021.3／2022.3、1025 解析度與 32 MB 來源；超限或未知資料明示診斷。Terrain 雕刻、圖層繪製、非空洞、碰撞模擬、完整 Terrain Lit 與 TerrainData 回寫仍未實作。原計畫其他功能及 Unity 端驗收門檻保持。

## 固定來源及驗證

執行 `scripts/capture-preview-r9.py handoff` 後，交驗來源為 `artifacts/preview-r9/source-snapshot`，manifest／相對 r8 差異／runtime 雜湊／173 項測試摘要在 `artifacts/preview-r9/handoff`。這是普通來源檔案複本，沒有匯出或封裝。

- 建置：`build-terrain.log`，0 警告／0 錯誤；候選 runtime 為 `artifacts/preview-r9/terrain-runtime`。
- 後端：`test-results/preview-r9-terrain.trx` 與 `test-terrain.log`，完整 173/173、0 略過。含既有 native save／undo／conflict／直接存取保護，新增 FBX 實例隔離、錯誤 fileID、唯讀 Shader／模板、Int32、Terrain 高度換算、無效資料及取消測試。隔離輸出以 `SCENE_WORKBENCH_CLI` 指向同批 CLI，沒有修改測試期望規避問題。
- JS 語法：`node scripts/check-web.mjs` 通過。UI strict audit：`ui-audit-terrain.json` 0 findings。
- Chrome：`terrain-browser/result.json`、`terrain-focused/result.json`，實際原生場景操作、來源／served 雜湊、1178／1920／390px、具名樹木／水面／地形、Shader hash 改動及多 Renderer 拒絕猜測、高度缺失／非空 holes 拒絕假地形。`browser-third`／`focused-cutout` 保留前一個有用比對；`browser-first`／`browser-second` 是失敗與修正紀錄，不列通過。
- 原檔：`protection.json` 的 512 項 bounded refresh 通過；Chrome watched native files 前後相同，TerrainData source hash 與驗收官獨立讀取一致。原始 D: 只讀，沒有 native 寫入測試。

## 離線版本與完整專案比對的既有複驗

延用已通過的 r8 固定證據，不重跑未改動的整批讀取：`artifacts/version-r8/native-content-comparison.json` 每側 71819 檔／約 5.82 GB，唯一內容差異是新複本預期調整的 ProjectVersion。78 套件／96 依賴離線宣告檢查通過。30 項完整比對證據 manifest SHA-256 `db24fe56cd6946ccb9def764edca35187c8a49c1fb550cd53592e90b9272d4a2`；獨立複驗為 `artifacts/acceptance/baseline-r8-20261022/content-review/VERDICT.zh-TW.md`。

這只證明離線檢查與位元內容比對，不代表 Unity 匯入、Shader 畫面一致或原生往返通過。原七項 2021.3／URP14 衝突在來源診斷仍保留，新的 2022.3 複本只完成離線對齊。

## 複驗方式

請由固定 source-snapshot 另起自己的 QA 埠與產物目錄，保持使用者 5286 及原始 D: 不動。開啟已備妥複本的 Map_60001，放大 TreeA01／TreeA05、水面、Terrain，核對高度、貼圖、裁切、原始變體、缺失圖層提示及 Console。保留三個議題各自的通過／未通過範圍，完整 Unity 畫面、Terrain 編輯往返及其他計畫功能不能併稱通過。結果直接回報實作對話，發現可修問題即續修。

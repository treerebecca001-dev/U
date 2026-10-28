# Map_60001 水面修正與缺失地表來源交驗（r9-fix1）

依人類指示繼續獨立網頁開發，直接交驗及複修；不啟動 Unity、不重試授權、不打包。使用者原始 D: 專案及 5286 互動服務保持不動。驗收以固定來源 `artifacts/preview-r9/source-snapshot-fix1` 為準，另起自己的 QA 埠；目前實作方隔離候選在 5296。完整核准方案 `docs/APPROVED_PLAN.zh-TW.md` 沒有縮減或改成工具專用格式。

前次驗收已確認 `WEB-PREVIEW-60001-001` 的原生樹木綁定／裁切與 Inspector 溢出、`WEB-TERRAIN-60001-001` 的完整高度／世界位置。整體畫面仍 FAILED；本次修正不能單獨代表全案或完整 StylizedWater2 通過。

## 本次水面變更

以 Map_60001 的兩個原生 `.mat`、`.watershader` 及 11 個 Libraries HLSL 雜湊固定適配；保留原 Shader fileID `-6465566751694194690`、GUID、keywords、材質與 ASE 原文。後端快照增加函式庫來源識別，瀏覽器遇到缺失／變更的範本或任何函式庫會拒絕固定適配並顯示診斷。水面材質仍唯讀。

| 原生設定 | 本次預覽及限制 |
|---|---|
| Depth 啟用 | 另外渲染原場景幾何的深度，以反投影矩陣取得 world／eye depth，依原數值顯示深淺色、Edge Fade 和水邊交界。水面與編輯輔助不寫入此深度。 |
| `_WAVES` | 依原波浪常數、方向、數量、衰減距離、波速、高度與頂點遮罩位移。海水 `_WaveSpeed=0.8`／Height 0.15；湖水 Speed 0／Height 0.1／Distance 1，原設定產生靜止表面，不強迫動畫。 |
| `_SHARP_INERSECTION` | 按原 Noise、長度、Falloff、Clipping、Ripple、頂點遮罩與色彩近似銳利水邊交界。這是交界效果，沒有強開關閉的整片表面泡沫。 |
| `_NORMALMAP`／`_DISTANCE_NORMALS` | 保留原取樣、距離及強度。兩材質 `_NormalStrength=0`，不擅自增加可見波紋。 |
| `_TRANSLUCENCY`／Specular | 近似原參數、地平線色、透光與方向光反光；讀實際啟用的原生方向光，完整 URP 光照／陰影尚未重現。 |
| `_FoamOn=0`／`_RefractionOn=0`／`_CausticsOn=0` | 維持關閉，無效舊 keywords 或殘留非零細項不會強開效果。River／Planar Reflection 亦未啟用。 |
| Environment Reflection／Receive Shadows 啟用 | **尚未適配反射探針、完整 URP GI／受光與接收陰影**。完整水面與 Unity 像素一致性不能宣稱通過。 |

材質球沒有場景深度，Inspector 會明示；預覽時間只改畫面。`water-contract-fix1.json` 含兩材質原數值／貼圖參照／來源 SHA／已啟用與尚未適配效果，請按實際設定檢查。

## 原生 TerrainLayer 缺失

`WEB-TERRAIN-60001-002` 仍 FAILED。缺的是 Terrain 第 0 層的原始地表設定，GUID `3a2a9d837fbf13e4b9197bb9b1ba3484`／fileID `8574412962073106934`。原始高度與權重存在，該層參照沒有包含檔名、Diffuse 貼圖或 Tiling，不能由 GUID 推知是哪張草地貼圖。

原始 D: Assets／Packages／Library PackageCache 的逐一 `.meta` 精確 GUID 搜尋、原匯入 metadata 路徑與兩個資料庫的指定 byte-pattern 查核記錄在 `source-dependency-search.json`；準備複本的全部 Assets／PackageCache `.meta` 搜尋結果也保留。資料庫只做精確 bytes 查核，沒有宣稱解碼未知 cache 格式。三份 TerrainData 原文 SHA 一致。既有匯出 OBJ 的預設材質沒有貼圖，不能還原缺層。人類已回覆不知道缺哪項，沒有提供其他備份路徑。

最低缺少的是原始 TerrainLayer 和相同 GUID 的 `.meta`，以及它指到而專案中沒有的貼圖；目前不要求人類辨認 GUID。保留原權重、參照和棋盤診斷，不猜換貼圖、不改 GUID 或刪權重。`terrain-layer-diagnostic.json` 的平均權重 0.27097 是 alphamap 統計，**不是螢幕面積或相似度**。地形雕刻／原生回寫仍未實作。

## 驗證資料與複驗方式

- `build-water.log`：候選 C# 建置 0 警告／0 錯誤；`water-runtime` 為同批未封裝開發輸出。
- `test-water.log`／`test-results/preview-r9-water.trx`：完整後端 174/174、0 略過；新增 Libraries 變更識別且唯讀不變的測試。未改的 R8 原生存檔、Undo、衝突等涵蓋在同批測試。
- `water-browser/result.json`：真實完整 Map_60001，6 組操作，原 3078 物件／6613 節點、265 FBX 綁定、3105 網格／633165 三角形、原生高度、1178／1920／390px、9 項 native watch 不變，無瀏覽器／請求錯誤。此檔是完整場景 UI 驗證，不單獨證明波浪實作。
- `water-focused-verified/result.json`／`contract-probe.json`：實際場景近景及預覽時間操作；兩個原材質在真正 WebGL 編譯，查核 active uniforms、GPU 高度值、波浪／深度／交界的像素改變。已知合成岸線只驗資料流，不能作為 Unity 圖像比對。原生零波速湖面應靜止。初始重開 Inspector 後取消舊縮圖的預期 Abort 有階段與路徑記錄；互動期間任何請求錯誤仍失敗。
- `water-focused`／`water-focused-fix1` 是測試程式錯認共用 GL program 名稱的失敗紀錄；`water-focused-fix2`／`water-focused-final` 保留未分類啟動縮圖取消的失敗紀錄，不列整體通過。沒有因此改動原生材質或放寬實際效果條件。
- `node scripts/check-web.mjs` 通過；`ui-audit-water.json` strict audit 0 findings。來源與 served 檔案雜湊見瀏覽器紀錄，包含三個新增水面模組。

凍結 source manifest、相對 r9 差異、runtime 與測試摘要在 `handoff-fix1`，證據 manifest 記錄各原始檔案。來源快照為普通檔案複本，沒有壓縮、匯出或發佈。先前 r9 及 R8 凍結證據未覆寫。

R8 的完整 71819 檔／每側約 5.82 GB 比對與 78 套件／96 依賴離線對齊，沿用已獨立 VERIFIED 的原證據；本次沒有重跑整批。Unity 匯入、原生往返、Shader 像素對照及完整方案各階段仍保留驗收門檻。

請驗收官從固定來源獨立啟動，重測 Map_60001 海水／湖水、原始開關、SceneDepth 交界及來源失效，檢查樹木／地形與 Inspector 鄰近迴歸。保留兩個未通過議題的實際範圍：缺失 TerrainLayer 以及尚未適配的啟用水面效果，不以警示存在或局部測試通過放行整體畫面。發現可修問題直接回報實作對話續修。

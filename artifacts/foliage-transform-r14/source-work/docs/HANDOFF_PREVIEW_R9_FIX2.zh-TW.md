# Map_60001 原始環境反射與水面接影交驗（r9-fix2）

本輪處理驗收官的 `WEB-WATER-60001-002`，沿用人類授權直接修正、交驗及複修；不啟動 Unity、不重試授權、不打包。原始 D: 與使用者 5286 保持不動，候選服務另用 5406。前次 r9-fix1 的 147 檔來源與 31 項交驗、62 項獨立複驗證據保持原樣。

前次已獨立 VERIFIED 的範圍為樹木綁定／裁切／Inspector、完整原生地形高度，以及水面波動／深度／交界。整體 60001 仍 FAILED，尤其原地表缺層不能靠這次水面修正放行。完整方案與原生互通門檻保留。

## 原生反射來源

讀取原 Scene 的 RenderSettings 與 LightingData PPtr，不按檔名搜尋反射圖。原本內建 Skybox 材質只有 GUID／fileID 參照，沒有偽造它的來源檔；本場景另有可精確綁定的既有反射資料：

- LightingData GUID `5bcb011e2e6af534abd434181540ef9b`／fileID `112000000`，classID `1120`，SHA `9d041ed84f189c9ff5eda3ed455cb4027e8ca66652e1177605d73e08cebe068f`。
- 原始 binary type tree 為 2021.3.15f1，內部 m_Scene GUID 必須與正載入場景相同，否則拒絕借用。
- `m_BakedReflectionProbes` 為 0，`m_BakedReflectionProbeCubemaps` 只有 1 項。精確外部表索引 11／fileID `8900000`，解析為 GUID `4c2e18007d862fd409ac3a9f67f24508` 的原 `ReflectionProbe-0.exr`，並非因檔名而選它。
- 原 EXR 為 768×128、RGB HALF、PIZ；TextureImporter shape 2。使用固定 r180 EXRLoader，保留原著作權資訊；解碼前檢查檔頭、維度與配置。只接受已支援的六面橫排，保留 HDR Half 資料，以來源雜湊拒絕讀取期間變動。

Scene ReflectionIntensity 為 1。材質原 `_ReflectionStrength=0.248`、Fresnel 5、Blur 0、ReflectionLighting 0 保留。依原 HLSL 的 AIR_RI、兩次 Strength、泡沫／交界遮罩及 lighting/emission 合成順序處理；Blur 0 使用明確 LOD 0。貼圖座標按原生 +Z／預覽 -Z 轉換，完整引擎的方向、色彩空間與平台匯入壓縮仍須 Unity 圖像比對，不能只憑綁定正確宣稱相同。

模式不符、場景不符、多探針／多圖無法唯一對應、缺失／變動來源、不支援的 EXR 都顯示診斷；不猜用其他 cubemap。這只是既有 LightingData 的唯讀反射參照，沒有產生或回寫烘焙資料。

## 原生方向光與接影

真正的 active 光源為 `Scene_Directional Light`，node `372289713/1727464136580948766`，Light fileID `1727464136580948767`。原 Prefab SHA `ddd278e68a59046df0a7eb7e8458611d047ef4d3561d1361c62d197105a3c402`，Hard Shadow type 1、Strength 0.6、Bias 0.05、NormalBias 0.4、NearPlane 0.2，CullingMask `4066832`、RenderingLayerMask 1。另一個 inactive／無陰影方向光沒有取代它。

兩個水 Renderer `595751174`／`743902876` 的 receiveShadows／reflectionProbeUsage 均為 1，GameObject Layer 4。現在讀取精確 Renderer flags、Layer、Light mask 和 Prefab 覆寫；FBX 可唯一映射時保留 Layer／shadow／probe 覆寫，未覆寫的 import flags 採版本預設，完整匯入器驗收仍保留。

獨立方向光深度圖尊重 CastShadows、雙面投影、接影、Layer/culling/rendering masks、active 祖先及編輯輔助。葉片以原貼圖 Alpha Cutoff 和 Tiling／Offset 投影，水面模板沒有 ShadowCaster pass，不製造整片水面遮蔽。60001 Terrain 的原 `m_ShadowCastingMode=0` 保持不投影。預覽時間改變仍只改畫面；幾何／材質／光源方向變更會使深度快取失效。

目前為獨立單張方向光硬陰影；來源解析度為 FromQuality 時採 2048 預覽設定，**不是宣稱已還原 URP 的品質／CSM**。完整 GI、Shadowmask、CSM、局部反射探針混合、其他材質的完整 URP 受光和 Unity 像素一致性未完成。材質球會明示沒有場景深度／陰影。所有原生 Shader、Material、Renderer flags、LightingData 與 EXR 保留原樣。

湖水仍是原 WaveSpeed 0／Distance 1，海水 WaveSpeed 0.8；兩者 NormalStrength 0，Foam／Refraction／Caustics 關閉，不為做出效果而強開。

## 測試與原始紀錄

後端有效結果為 177 項，`test-summary-lighting.json` 依原 TRX 合併同批未改二進位的有效結果，沒有把重試加總成額外測試：原完整回合 167 通過、10 項因錯將 DLL 當 executable 失敗；改用同批 `.exe`／工作區 DOTNET_ROOT 後 9 通過，另一項在 sandbox 原子存檔遇 Access denied，僅該項在原生執行環境重試通過。測試預期及產品存檔保護均未放寬。原失敗 log／TRX 與最初 xUnit analyzer／SDK 啟動設定失敗保留。新增 Renderer／mask／原生 Light／巢狀 Prefab shadow 覆寫的唯讀測試。

實際候選 DLL 在 `lighting-test-runtime`；較早 `lighting-runtime` 是 Terrain render flags 加入前的建置，不用它交驗。`build-lighting.log` 和 test 編譯輸出保留。

`lighting-browser` 為完整場景／13 項 native watch／1178、1920、390px 六組操作；部分後續水面模組再調整，由最新 lighting-focused 系列記錄實際 served SHA 與原場景產品 SceneView GPU 測試。受影響效果須按最後固定來源的紀錄為準，早期結果不冒稱最終 bundle 全通。

lighting-focused 使用原 60001 graph 和產品 SceneView／AssetPreview 放入獨立 QA document，暫時切換記憶體中的環境反射／接影旗標，比較實際像素；不是用另建假岸線替代真實場景。核對 active GPU uniforms、原 EXR SHA／PPtr、真正方向光、mask、還原及 GL error。原始精確還原斷言的微小讀回色差保留，暖機／固定畫面重複取樣與還原量化界線分列，**不是 Unity 相似度百分比**。拒絕無效 EXR／不支援矩陣並測 off flags。最後的 `lighting-focused-source-formula` 依原 HLSL 校正反射合成後通過，GL error 0；`lighting-browser-ready` 記錄最終完整 UI／來源。`lighting-browser-final` 的五組 UI 已通過，最後誤將啟動時舊唯讀 Inspector 縮圖取消列為錯誤；保留失敗原文，先關閉自有 QA 唯讀材質、沒有捨棄編輯，再從場景狀態重測。

JS 語法、strict UI audit、最終畫面與固定來源／runtime／證據雜湊在 `handoff-fix2`。快照為普通來源檔案複本，沒有封裝、壓縮、匯出或發佈。請驗收官用固定 `source-snapshot-fix2` 另起自己的服務，對水面反射／接影及必要鄰近回歸獨立重測，問題直接回報續修。

## 原地表相依缺口

`WEB-TERRAIN-60001-002` 仍保留：原 TerrainLayer GUID `3a2a9d837fbf13e4b9197bb9b1ba3484`／fileID `8574412962073106934`。現有 35,560 個原 D: `.meta` 與所列 cache 查核已由驗收官確認範圍，不重跑整批或假稱能解未知 DB。人類不知道缺哪層，沒有其他來源路徑；不再要求辨認 GUID、不猜換貼圖、不改原權重／參照。高度完整、缺層棋盤仍明示。R8 每側 71,819 檔完整比對與離線版本證據沿用，完整 Unity 原生往返及其他計畫能力保持未驗收／未實作門檻。

# Terrain source recovery r11 交驗

2026-10-27，修復 `WEB-TERRAIN-60001-002` 的來源缺口。這是 **原生資源資料版本**，產品前端／後端仍為 `transform-r10-fix1`，沒有改地形渲染公式、GUID、TerrainData、高度或權重。完整原計畫及 Unity 原生驗收、完整 URP／Shader 校準與中途取消補證仍保留。

## 真實來源

先前指定的 `Client231229` 原檔、工作複本及套件來源均找不到第 0 圖層。這次唯讀檢查其同層相關專案 `D:/Work_ART/漂流NFT/@WLN_work/Client`，找到原參照的 `Grass_01_Layer.terrainlayer` 與 metadata：

- GUID `3a2a9d837fbf13e4b9197bb9b1ba3484`，原生 classID **1953259897**，fileID **8574412962073106934**，與 TerrainData 原 PPtr 精確相同。
- Layer SHA `14048bf0b8a4c5612037365983fe33da904eec7c3329b427721deebbe65a8f98`；原始 Tiling 9×9、Offset 0×0、Normal／Mask 均未指定。
- 原 Diffuse GUID `ed7a9b9b5f64c5747bcf402b754ec89e`、fileID `2800000`，是該來源專案的 512×512 `Grass_01a_t.png`，SHA `da6785b2307628680f006664138eb552eff2c4d2800469cf2e58540c50bbadbd`。
- 來源 `Client` 與原 `Client231229`／目標的 `Map_60001.asset` 完全同 SHA：`5599b8d0b42efd1b2ddf2b81e67a9f0d26bac7c57cd81b602f1001d7b674dcd3`。來源 Editor 宣告 2021.3.15f1；沒有用近似圖片、檔名或顏色猜配。

來源 `Client` 的 Assets／Packages 全部 `.meta` GUID 搜尋只找到上述匹配；同層 `Client230411` 沒有匹配。這是新增搜尋範圍，不是推翻先前針對指定 `Client231229` 的缺失結論。

## 唯一資料變更

只在既有 E 槽完整工作複本 `Workspace/Baselines/Client231229-2022.3.13f1` 新增五個原本不存在的原生檔，全部與來源逐 byte 相同：

1. `Assets/ResourcesBuild/Scenes/Common/TerrainTextures/Grass_01_Layer.meta`。
2. 同名資料夾內 `Grass_01_Layer.terrainlayer` 與其 `.meta`。
3. 同一資料夾的 `Grass_01a_t.png` 與其 `.meta`。

Folder GUID 亦保留來源 `42a3da345d5217f4a8420c587e8f2ec2`。目標 Assets／Packages／Library/PackageCache 全部 `.meta` 搜尋確認三個 GUID 在新增前皆無匹配。未覆蓋既有檔、不複製無相依的 Grass 材質／Prefab、不改任何 D 槽專案。既有完整複本是編輯目的地；原本 r8 完整內容比對屬於修復前資料版本，新差異正是本次五檔來源復原，不冒稱又重跑 71,819 檔全量比對。

`scripts/recover-terrain-layer-r11.py` 先驗證 canonical path、source hash、目標不存在與來源無 reparse point，完整 staging 後依相依順序發佈，Layer meta 最後公開。Windows rename 不覆蓋現有目標，每步寫復原紀錄。`artifacts/terrain-recovery-r11/application.json` 為 COMMITTED；13 個既有受觀察原生輸入前後一致，五個 D 來源亦一致。不要重跑 `--apply`；重複執行會由 guard 拒絕。只可在五個新增檔雜湊仍符合紀錄且沒有使用者修改時處理回復。

## 主工程驗證

隔離服務 5420，用正常 project/scene API 準備唯讀測試，再用 fresh in-app browser 載入完整 60001，執行 Q 檢視、選取 TreeA01、focus 取得同一近景。無原生場景編輯或儲存命令，dirty false、Console 空。原棋盤格區域已顯示原始草地，viewport unresolvedTerrainLayers 由 1 變 0。實圖為 `artifacts/terrain-recovery-r11/recovered-scene.png`，瀏覽器觀測 `browser.json`。

`api-verification.json` 核對完整新／舊 graph：6,613 節點、28 材質、環境資料完全相同；其餘兩層、全部高度及 alphamap 控制資料完全相同。第 0 層精確解析到原 Layer／PNG 與 9×9 Tiling。完整 263,169 高度樣本 SHA `366bccfd0810593d68b04917ffe00b37c51824f104a256959e226581dca149f5`。3,105 網格、633,165 三角形、265 FBX 綁定、524,288 地形三角形、3,026 投影、原反射與既有原生光源識別均保留。257 項近似仍明示，沒有把正常地表當成完整 Unity 畫面驗收。

## 獨立複驗

請核對 `handoff/` 的來源、五個資料檔與證據清單；資料鏡本放在 `source-assets/`，保留相同 Unity 相對路徑。來源程式快照是 fix1 相同產品碼及本次復原腳本／文件，無新 DLL。可以唯讀開啟已復原的 E 工作複本，或用自有隔離複本保留全部原資源後套入五檔；不要在 D 槽執行復原，也不要修改先前凍結來源或舊判定。

請實際確認 60001 不再出現缺層棋盤格、原高度與權重、三層混色／Tiling、樹木與水面、唯讀及原檔保護，完成 fresh Scene reload。以原始輸入變更身分判定 `WEB-TERRAIN-60001-002`；先前 r10／fix1 缺失資料的歷史判定仍保留。其餘有效程式／幾何／變換證據按未變更來源與環境適用性沿用；不要重跑同批 177 個後端結果。不啟動 Unity、不打包，5406 留給使用者，QA 用自有埠。

# TreeA01 與原生中斷事件：本輪結果

2026-10-27。總狀態 **UNVERIFIED（局部通過）**。不用 Unity 即可完成的指定預覽修正及有界原生事件實測已交驗收官獨立複驗，原始證據已逐檔核對；沒有啟動 Unity 或打包。

## 可使用的開發版

- 入口：[R14 本機開發版](http://127.0.0.1:5408/?source=foliage-r14-accepted)。60001 已載入，新增分頁保留。
- 重開：工作區執行 `scripts/start-reviewed-r14.ps1 -Port 5408`，使用普通凍結來源及已測 runtime，沒有建置、下載或封裝。
- 已在新入口實際選取 TreeA01_3／聚焦／縮放確認3105網格、633165三角形、265 FBX bindings、1地形、0未解析材質，瀏覽器warning/error為0。完整6613節點graph、原生state與8個受改動served來源hash相符。
- 舊5406整份工作階段與本輪開始時完全相同：revision6、dirty=false、Undo0／Redo1，沒有重啟或reload。保留舊根src，避免舊後端讀到新版JS。後續修改在 `artifacts/foliage-transform-r14/source-work`；不要修改作為交驗身分的 `source-snapshot`。

## 分項結果

| 項目 | 結果及範圍 |
|---|---|
| TreeA01當前樹冠 | 指定owner222870／630463173正面與海側側面、重讀均通過。修正來源Model根Transform與原生覆寫重複套用；自己的枝幹接入自己的樹冠。 |
| 歷史植物材質 | 同GUID／subasset／來源SHA符合才採歷史URP的Both與固定0.5裁切，負向guards及唯讀UI通過。原材質與Unity Assets不變；現在的ShaderGraph／UV風動／Unity光照等價未驗證。 |
| 幾何與樹叢保留 | 61來源整體／58種直接Mesh頂點回歸通過；三份指定Gress的資料與鏡頭保留。不能因此宣布所有樹叢都與原Unity位置相同。 |
| Escape與後續操作 | 真實changed拖曳取消、晚到trusted pointerup、零提交、鏡頭不動；正常拖曳一筆命令、Undo及重讀完整還原。 |
| pointercancel | 明示HTML原生拖放接管的一次真實樣本通過：changed→trusted pointercancel→還原、約2242.9ms後dragend，零提交，完整state／graph／鏡頭不變。不是一般OS／裝置中斷或晚到pointerup的通過證據。 |
| 失焦／hidden | 新的受支援visibility能力已在changed拖曳中限一次嘗試，DOM仍visible、hidden=false，沒產生真正window blur／hidden事件，維持未驗證。沒有以合成事件或重複失敗路徑冒充實測。 |
| 舊Prefab覆寫 | Tree舊target的x=-0.2／z=0.87及body原元件／Unity unused分類仍未驗證，原文與診斷保留，沒有猜測重綁。 |

R13曾把未記錄pointerup推論為UA抑制；被動觀察器沒有訂閱pointerup，控制器取消後也清除追蹤ID，因此這個推論已更正。晚到pointerup保留為未驗證，原始raw／frozen manifests未改。

## 證據與限制

- R14：[獨立驗收結果](../artifacts/acceptance/foliage-r14-20261027/VERDICT.zh-TW.md)、[收件紀錄](../artifacts/foliage-transform-r14/review-receipt.json)，405檔；317個原生／來源pins不變。
- R13：[獨立事件結果](../artifacts/acceptance/tree-input-r13-20261027/VERDICT.zh-TW.md)、[收件紀錄](../artifacts/tree-input-r13/review-receipt.json)，289檔；185個原生／樹木pins不變。兩輪監看檔案有重疊，不能把數量相加當作不同原檔數。
- C#191/191為已核對原始TRX與受測組件身分的有效證據；獨立驗收沒有重跑完整C#來冒充新原生事件測試。兩輪QA程序／分頁已清理，viewport reset，CUA交回。
- 驗收官以direct讀E隔離基準，producer及新開發入口為copy。沒有做原生Save，不核准兩種儲存模式等價。完整Unity往返／版本／畫面、原計畫階段0–7及新尋路／烘焙等門檻仍未通過。

完整核准方案仍見 [APPROVED_PLAN.zh-TW.md](APPROVED_PLAN.zh-TW.md)，沒有改成工具專用格式或自行縮減最終需求。

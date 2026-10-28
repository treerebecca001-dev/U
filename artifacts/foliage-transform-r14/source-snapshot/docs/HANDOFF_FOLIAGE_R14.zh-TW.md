# R14：TreeA01 預覽位置與歷史植物材質

2026-10-27。交驗狀態 **UNVERIFIED，待獨立複驗**。本輪針對驗收官轉達的樹葉／樹叢位置問題 `WEB-PLACEMENT-60001-001`，保留 R13 原生中斷事件證據。完整方案見 [APPROVED_PLAN.zh-TW.md](APPROVED_PLAN.zh-TW.md)；目前並未完成方案所有階段。遵守最新要求：不啟動 Unity、不製作安裝包或發佈封裝。

## 改動與驗收條件

FBX 整體預覽先前把來源 Model 根節點的位置與旋轉寫入頂點，再乘上 Prefab 原生根覆寫，導致 TreeA01 葉片偏移。現在只在「單一 Model、單一 Mesh、無骨架、已知原生根 fileID、具有逐軸覆寫來源」時分離根 Transform。幾何 Transform 仍由 FBXLoader 處理；未覆寫的軸使用來源預設。一般來源檢視及直接 Mesh 子資源路徑保持原樣；未知根 ID、缺少覆寫來源及多 Model 情況不猜測對應。

驗收官的條件是同一株樹的樹冠位於自己的樹幹上方，枝幹接入樹冠，沒有整團漂到旁邊或海面。bounds 交疊不足以單獨通過。指定 owner `222870`、`630463173` 均需正面與側面；另看相同鏡頭全景。Gress_17（3106871）、Gress_19（1962971）、Gress_33_b（851942063）代表直接 Mesh 路徑，分開核對。

另從唯讀舊專案 Client230411 找到材質相同 GUID `828648e0d2efd30478f914acd51fe15c` 的 `Temp_ScenePlantBase.shadergraph`，SHA256 `c4781ba261421c1bacd0ed55f07ce80fed85d0d6333ad41cb45663cdfcad0e29`。其 URP target 固定 Both／CullOff、AlphaClip；未連線的 AlphaClipThreshold 為常數 0.5、禁止材質覆寫。Vertex Position／Normal／Tangent 沒有輸入，沒有 billboard 或頂點變形。

新增有 hash 驗證的歷史顯示參考，只有 GUID、字串 subasset ID `-6465566751694194690`、材質特徵符合且目前 Shader 來源缺少時才使用。忽略殘留的單面與裁切參數，採雙面、opaque／depthWrite 與固定 0.5 裁切。參考檔缺少或雜湊不同時退回一般唯讀近似。這份來源只放在工具內 `/ui/references/*.txt`，**沒有回填 Unity Assets**。Inspector 顯示歷史參考、目前來源缺少、UV 風動／版本等價／Unity 光照未驗證，材質維持唯讀。

## 根端實測

- `test-results/foliage-r14-runtime-corrected.trx`：191/191，包含 4 個新逐軸覆寫測試。最初錯誤的 SDK／執行環境造成 29 失敗，原 TRX 保留；使用工作區既有 SDK、序列建置參數及僅測試程序的 DOTNET_ROOT/PATH 後通過。C# 後續沒有改動，不重跑相同套件。
- `geometry-tests-final-root-id.json`：61 份來源整體幾何、58 種直接 Mesh 子資源與修正前相同；分離根且無覆寫時，與原本完整幾何最大誤差約 0.00000403 公尺。缺 mask／歧義／未知根 ID 拒絕套用。
- `historical-surface-tests-final.json`：來源身分、雜湊、缺檔、錯誤身分與失配回退；實際 createMaterial 雙面／深度／0.5 裁切、殘留 0.9 不覆蓋來源常數、原材質不變、vertex shader 不變。
- 真實 IAB 1178×1000，fresh reload 最終材質後，`both-tree-*-front/side.jpg` 各兩張。使用 Q 真實旋轉視角，沒有設定鏡頭內部狀態。兩株樹冠回到樹幹上方；側面仍呈現原來源葉片平面形狀，沒有補造體積。請獨立看實際接合。
- `both-fixed-camera.*` 與 `before-fixed-camera.*` 鏡頭完全相同；三份 `both-gress*` 的鏡頭與修正前相同，實際 geometry/matrix 保持一致。這不等於有原 Unity 截圖可比。
- 860×900 窄視窗的 geometry／材質 side／歷史來源 pin 與桌面一致，唯讀套用按鈕不可用。`both-material-status.json` 保留 Inspector 原文；預覽時間 slider 可操作，其餘原生材質設定唯讀。
- `native-escape-regression.json`：真實 Terrain changed 拖曳、trusted Escape 取消、約 2 秒後 trusted pointerup，零 commit，鏡頭及完整 state／graph 還原。
- `api-final-both.json`：6613 預覽節點，完整 scene graph 不變，dirty=false、Undo0／Redo0；僅開關唯讀材質 Inspector 讓 session revision 2→4。`verification-final.json` 明確核對此差異，沒有忽略其他欄位。
- 最終3105網格／633165三角形／265 FBX bindings／1 Terrain／257近似／0未解析材質；瀏覽器 warning/error 0，Console 的 body／TreeA01 兩筆原生診斷保留。41個原生輸入、144份樹木比對檔、61組植被來源與 meta 雜湊不變。

最初 `verification.json` 在寫出報告後，輸出摘要時因腳本區域變數遮蔽 console 而失敗；腳本已修正，`verification-final.json` 是成功完成的合併證據。初次 root-only 側面與不一致尺寸截圖均保留，不冒充最終雙面證據。自有 QA 頁面已關閉、viewport 已 reset，自有5444已停；主5406尚未切換。

## 複驗方法與剩餘限制

使用 `handoff/source-manifest.json` 的 `source-snapshot`，及 `handoff/runtime.json` 的既有測試 runtime；這些是普通檔案複本，沒有封裝。以環境變數 `SCENE_WORKBENCH_ROOT` 指向 frozen source、`DOTNET_ROOT` 指向工作區 `.tools/dotnet`，清除 `SCENE_WORKBENCH_SAMPLE_PROJECT`，執行 runtime 的 Web DLL `--port 5446` 或其他自有隔離埠。不要用 `--urls`。正常 API inspect 開啟 E: 準備好的 Client231229-2022.3.13f1 與 60001。可把既有 GUID 衍生 cache 複製到自己的 QA 根目錄加速；cache 不屬於驗收來源。

請核對實際 served bundle、原生輸入 pins、兩株正面／海側側面與三份 Gress。使用 Hierarchy 搜尋並按聚焦 F；Q 旋轉，側面範例拖曳 [650,320]→[548,301]（以最新截圖與實際 viewport 為準）。`#viewport` 的唯讀 DOM selectionGeometry 只提供有界的實際 matrix／bounds／材質 side；畫面結果仍需肉眼檢查。必要時重測正常拖曳／Esc 取消，結束後不儲存並還原乾淨狀態。

R13 見 [HANDOFF_TREE_INPUT_R13.zh-TW.md](HANDOFF_TREE_INPUT_R13.zh-TW.md) 與其 frozen manifests。獨立 pointercancel 複驗可使用明示原生 HTML 拖放 QA fixture，正式 JS/C# 與 R12 相同；不能把此事件推廣成一般 OS／裝置中斷。blur 模態嘗試含工具中斷，visibility 尚未取得 changed 拖曳中的 hidden=true；維持 UNVERIFIED。不要重複同一失敗的 native Sky 初始化或降低瀏覽器安全限制。

TreeA01 舊 target `5565035281593725506` 的 x=-0.2/z=0.87 沒有安全替換證據，保留原文與診斷。body 原元件／Unity unused 分類、FBX 全面 Unity 匯入 parity、當前 ShaderGraph／編譯 subasset 等價與完整畫面仍未驗證。本輪不能作為完整計畫或所有版本阻擋解除的證明。獨立複驗通過的限定修正才會同步到主開發版。

# R13：TreeA01 與原生指標中斷補證

2026-10-27。總狀態 **UNVERIFIED**；本輪新增限定條件的原生事件證據，沒有變更正式 C#／JS／CSS、Unity 資源或主工作頁。未啟動 Unity，未製作安裝包或發佈產物。

| 項目 | 本輪可確認 | 仍未驗證 |
|---|---|---|
| TreeA01 | 舊 leaves2 FBX 仍在目前專案，Prefab 改參照另外兩個 GUID；新舊來源幾何與 UV buffer 不同；指定 owner 的 Inspector 唯讀，兩個原始覆寫保留 | 新舊目標的可靠替換關係、美術意圖、Unity 原生 unused 分類 |
| pointercancel | 實際 60001 Terrain changed 拖曳 → QA HTML 原生拖放接管 → trusted pointercancel → 還原；完整 API 狀態／圖形、鏡頭、Undo、原檔均無變動 | 一般 OS／裝置／觸控中斷；本次 UA 抑制 pointerup，不能宣稱收到晚到 pointerup |
| blur | 原生模態中斷及工具復原期間，正式控制器收到 trusted window blur，changed 值還原，無 commit | 乾淨、可重現的一般 OS 切換視窗與晚到 release；此嘗試含工具中斷，不能當一般流程通過 |
| visibility | 已嘗試 Chrome 新分頁與 IAB 原生 target=_blank | changed 拖曳時 hidden=true 的事件；後者的 window blur 晚於正常 commit，不能列入取消驗收 |
| 鄰近回歸 | 中斷後正常拖曳一筆命令、一次 Undo 還原完整 graph、重新載入清空歷史；Tree 唯讀、Console 兩筆保留診斷；瀏覽器無 warning/error | 完整 Unity 往返與原計畫其他階段 |
| body | 沿用 R12 已驗收的完整125-object來源缺目標、安全保留 | 原元件身分、Unity unused 分類，沒有新增可證明的資料 |

## TreeA01 的具體證據

場景 owner `630463173`，舊 target `5565035281593725506`，原 x=`-0.2`／z=`0.87`。歷史 Client230411 將它連至 instance `5381875668383798697`、來源 Transform `-8679921383154817045`、GUID `78b4ac0deff380946a311d7c61f3bc07`。

該 GUID 對應的 `TreeA01_leaves2.fbx` 仍在 Client231229。現在 TreeA01 Prefab 使用 GUID `44c5a147808ad044eb7a1a0c8c716fde` 的 `TreeA01_leaves_ce_f.fbx` 與 GUID `235d7584ec64aea49bea59ecc8cc1ec7` 的 `TreeA01_trunk_ce_f.fbx`，沒有舊巢狀 instance。這是目前 Prefab 的相依關係改變，不能稱為 FBX 遺失，也不能僅憑名稱轉套覆寫。

`tree-target-audit.json` 逐檔固定四份專案內144個指定樹木資料檔及 meta。`tree-geometry-analysis.json` 使用固定 Three FBX 解碼器做離線幾何比較：舊新葉模型各61三角形，樹幹各199，但 position／UV buffers 都不同，葉模型矩陣也不同。貼圖使用未解碼 placeholder，這份比較不證明材質或 Unity 匯入結果。沒有將 fileID 轉為浮點數。

## QA fixture 與真實瀏覽器步驟

fixture 位於 `artifacts/tree-input-r13/native-event-fixture`，181份既有來源依 manifest 複製，只有 fixture index 加入明示 QA 控制列與額外 grid row，另加被動事件觀察器及一頁焦點目標。所有正式 JS／C# 模組與 R12 相同。這不是主工作頁 UX，也不是通用實測結果。

復現可用自己的隔離埠，例如5440，執行既有 `artifacts/prefab-cancel-r12/runtime/SceneWorkbench.Web.dll --port 5440`；`SCENE_WORKBENCH_ROOT` 指向 fixture（或驗收官自己的 byte-identical fixture 複本），清除 `SCENE_WORKBENCH_SAMPLE_PROJECT`，`DOTNET_ROOT` 指向工作區 `.tools/dotnet`。不要使用 `--urls`，此服務只接受 `--port`。正常 API 以 inspect 模式開啟 E: 已準備的 Client231229-2022.3.13f1，再開 60001；不得修改 D: 或主5406。

1. IAB viewport1178×1000，等待3105模型／1地形／633165三角形。搜尋並選取60001_Terrain、聚焦、移動工具。
2. **pointercancel**：勾「讓瀏覽器接管拖曳」，由截圖取得 Y 軸位置（本輪373,479→373,387）。真實 drag 自然觸發原生 HTML dragstart、pointercancel、lostpointercapture、dragend。沒有 dispatchEvent、合成輸入事件或程式 cancel 呼叫。
3. 檢查 `fixture-native-drag-attempt.json`：preview Y11.367310474617593 → cancel-request:pointercancel → cancelled:pointercancel Y0。事件 trusted、pointerId1。dragend 在還原約2491.6ms後，無 commit。UA 沒發 pointerup，這項限制必須保留。
4. **模態嘗試需審慎判讀**：取消勾選；在 changed 拖曳中以鍵盤 Space 操作明示 QA 原生模態按鈕。原生 alert 中斷 drag promise，未即時處理的拒絕使 CUA session 重設；接回 tab 的 focus-emulation 命令逾時，再經 core tabs.get／getJsDialog 恢復，對話已不存在。正式 trace 在 preview 約65秒後收到 trusted window blur 並還原；焦點事件 target=null 可區別欄位 blur。這不是乾淨的一般 OS 失焦驗收，請勿將 handler 執行推廣為一般流程完成。若重測，立刻替 pending drag 加 rejection handler，先保存 mid snapshot，使用已記錄的 getJsDialog 介面處理提示，不重跑相同失敗復原循環。
5. **原生分頁連結**：取消勾選，在 changed 拖曳中以 Enter 開啟 QA target=_blank。新分頁確有開啟，但目前控制介面仍回報 visible，拖曳正常結束並提交。後續 window blur 在 commit 後才發生，所以 visibility／拖曳中 blur 仍未證。此正常拖曳剛好驗證取消後可產生一筆命令，完整 graph 僅 nodes[71].position[1] 改變；一次 Undo 全圖還原。
6. 檢查實際 TreeA01_3 owner630463173、唯讀 Inspector、Console 兩筆診斷；查看 `tree-current-qa.png`。最後 reload，API revision5、dirty=false、Undo0／Redo0，關閉 QA tabs，還原 viewport。

## 來源、執行及資料保護

`verification.json` 以完整 state／graph 的 canonical JSON hash 比較取消前後，僅 JSON 數值負零按 JSON 規則處理，fileID 字串與原檔 bytes 不正規化。正常提交及 Undo 用明確期望值比較完整資料；不是只看 Terrain 或 Undo 數字。

正式181來源（包含 R12 已記錄的三份結案文字更新）、20個固定 runtime 檔、41原生輸入檔、144樹木比對檔均重新核對。因正式執行程式不變，沿用 R12 的187/187測試與正式 TransformTools 8/8 原始證據，不重跑相同套件冒充新 OS 實測。新增測試腳本另做語法檢查。

最初 fixture 冷 GUID 索引超過120秒擷取時限；準備狀態完成後另取完整 before-ready。QA 佈局曾因多一列位移，已只在 fixture 修正。另一次啟動誤傳 `--urls` 而使用預設5286，該次自有程序立即停止，未開啟或準備其專案；主5406保持原工作階段。這些是實測設施紀錄，不歸因為產品故障。Chrome 需先從普通 loopback 連結導航，以符合既有 Sec-Fetch-Site 同來源保護，沒有減弱安全檢查。

原始模態／分頁／Chrome失敗嘗試一併交付。`handoff` 固定來源、runtime引用、fixture、輸入、原始證據雜湊。驗收官請獨立重測受影響範圍，已驗收且程式未變的 R12 Esc／Wall 等檢查沿用；若沒有收到真實 hidden／相應原生事件，維持 UNVERIFIED。

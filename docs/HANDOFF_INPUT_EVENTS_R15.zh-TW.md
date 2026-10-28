# R15：獨立 pointerup 觀察與失焦事件複驗

2026-10-28。總狀態 **UNVERIFIED（局部通過）**。本輪不啟動 Unity、不打包，也不執行原生 Save。完整核准方案仍是 [APPROVED_PLAN.zh-TW.md](APPROVED_PLAN.zh-TW.md)；沒有縮減原需求。

## 本輪新增範圍

從 R14 凍結來源的204檔建立 `artifacts/input-events-r15/fixture`。只改隔離頁 index，增加兩份 QA 檔；82個 `src/` 正式來源檔（排除 index）與 R14 相同。後端重用 R14 原來20份 runtime；沒有重新建置或改動正式控制器。

QA observer 在 window capture 獨立訂閱 pointerup／pointerdown／pointermove／pointercancel／capture／HTML drag／焦點／visibility事件，不依賴控制器的 gesturePointerId。紀錄 seq、最近一次 pointerdown序號、pointerId、isTrusted、document可見性、真正window或欄位target，最多512筆並公開dropped計數。這次所有引用 raw 的 dropped=0。沒有 dispatchEvent、控制器呼叫、輸入合成、preventDefault 或 stopPropagation。

勾選「瀏覽器原生拖放接管」只設定隔離viewport的HTML draggable。另一個明示按鈕／F8由真實使用者輸入開啟同來源QA popup；這是有界瀏覽器刺激，不能宣稱已測一般OS Alt+Tab。

## 已取得的原始結果

- `native-pointercancel-arrow.json`：一次原生 drag，Terrain `1114381152` 的Y預覽為10.0636728211，再收到 trusted pointercancel，恢復0；完整 API state／6613節點graph／鏡頭不變，revision2、dirty=false、Undo0／Redo0。
- 取消觀察序號37，約108.2ms後dragend（精確值以raw／verification為準）。獨立observer已在同頁記錄過真正pointerup；取消後到下一個pointerdown前沒有pointerup。這只是該次完整capture的結果，**實際晚到pointerup仍未驗證**，不推論所有UA都不會發出。
- 取消後下一次按下是勾選框的序號40，其pointerup是43。雖然也使用pointerId1，lastDown不同，屬於新手勢，不能當作被取消拖曳的晚到放開。
- `popup-mid-gesture.json`：已確認changed=true，再用真實F8開啟新的QA popup；原文件仍visible、focused，沒有window blur或hidden事件。該拖曳正常完成一筆position命令，完整graph只有該物件position改變；一次Undo及重讀完整還原，最後revision5、dirty=false、Undo0／Redo0。這不是產品取消失敗，也不是一般失焦／hidden通過。
- `native-pointercancel.json`為第一次**未命中gizmo**的嘗試：原生HTML取消發生，控制器沒有開始拖曳，Orbit鏡頭有變。該檔完整保留但不列入取消驗收；之後以聚焦按鈕重設鏡頭並重新定位箭頭才取得有效樣本。
- Chrome紀錄兩筆 `Could not establish connection. Receiving end does not exist.`，原始log保留，不在沒有stack證據時歸因於產品或擴充套件，不宣稱零錯誤。請獨立複驗時另記該次主控台。

一般原生視窗工具本輪限一次初始化，在任何UI操作前即失敗：`failed to write kernel assets ... (os error 3)`。未重試舊失敗路徑、未改用Win32／PowerShell／CDP繞過UI控制工具。先前visibility capability、alert、普通新分頁的失敗證據沿用，不重做相同測試來充數。

## 給驗收官的操作與判定

Root完成凍結並明確移交後才取得CUA lease。請使用自己的5449埠／分頁，不碰原5406、5408或既有使用者Chrome分頁。執行凍結 `scripts/start-input-events-r15.ps1 -Port 5449`，使用 R14 固定runtime。可用凍結capture腳本在全新instance執行 `--prepare --port 5449 --label ...`；或UI直接開既有E隔離基準並記錄writeMode。GUID初次索引可能超過120秒，讀取操作上限已調為600秒，agent工具可yield並持續回報。

1. 逐檔驗證fixture、R14 parent/runtime、實際served資源與41個原生pins。原始方案包含在source-snapshot。
2. 從實際截圖選擇Terrain移動軸；一次native HTML drag重新檢查changed→trusted pointercancel→還原，並讀**獨立observer**的取消後pointerup、下一個pointerdown及dropped。不要把field blur當window blur，不要只比pointerId而忽略新down。
3. 只有真正同一cancelled手勢的trusted pointerup、且之前沒有新down，才能核准晚到pointerup。沒有觸發就維持未驗證。HTML dragend不是pointerup；模擬單元事件也不是原生證據。
4. popup新路徑已在root執行且不造成window blur／hidden；不必重跑同一路徑。一般OS刺激若現有支援工具仍不可用，保留明確缺口；不要重試Sky初始化、alert、相同visibility或新分頁路徑。
5. 取消後完整state／graph／鏡頭與命令數不變；後續正常拖曳一筆命令，Undo／reload完整還原。不要Save。保存原始DOM、API、log、截圖與各項hash，出具獨立判定。

程序結束時關閉自有分頁、停止自有服務、還原暫時viewport／visibility，再交回CUA。若發現真正產品bug，提供可重現raw與來源身分，由root修好後再複驗。原R14 191/191後端與R12控制器8/8證據在來源相同範圍沿用，不重跑它們冒充原生OS測試。

## 保留的狀態與缺口

本輪開始原5406 revision6、clean、Undo0／Redo1；5408 revision2、clean、Undo0／Redo0，完整快照已存。前後比對必須保持一致。41個原生檔案目前SHA不變。

尚待真實輸入證據：一般視窗失焦、拖曳中document.hidden=true、pointercancel後該次手勢晚到pointerup。R13／R14其他尚未通過門檻及完整Unity往返仍見 [VALIDATION_R13_R14.zh-TW.md](VALIDATION_R13_R14.zh-TW.md)，不因新observer而自動通過。

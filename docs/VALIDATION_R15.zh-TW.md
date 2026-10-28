# R15 原生中斷事件驗證

2026-10-28。總狀態 **UNVERIFIED（局部通過）**；驗收官已校核提交的原始證據，獨立 UI 實操受測試工具初始化阻擋。

這一輪新增隔離被動觀察器，獨立記錄 pointerup、pointerdown 與真正 window／document 事件，解決先前觀察器未訂閱 pointerup 的證據缺口。正式編輯器程式與 R14 相同。原開發入口仍是 [5408](http://127.0.0.1:5408/?source=foliage-r14-accepted)，5406 舊工作階段保留。

Producer 已實際取得一次 changed 拖曳 → trusted pointercancel → 還原：完整場景、鏡頭、命令數不變。完整觀察紀錄沒有遺失；取消後到下一次 pointerdown 前沒有 pointerup，之後同 pointerId 的放開屬於勾選框的新手勢。這樣的樣本仍不能核准「同一取消手勢的晚到 pointerup」。

新 popup 路徑在 changed 拖曳時由真實 F8 開啟，但文件仍 visible／focused，沒有真正 window blur 或 hidden。該次拖曳正常產生一筆命令，Undo／重讀後完整還原。一般 Windows 視窗工具本輪在輸入前初始化失敗；沒有用合成事件替代，也沒有重試已失敗的舊路徑。

交驗資料與完整判定條件見 [HANDOFF_INPUT_EVENTS_R15.zh-TW.md](HANDOFF_INPUT_EVENTS_R15.zh-TW.md)；固定來源／24份原始證據與雜湊見 `artifacts/input-events-r15/handoff`。Chrome 兩筆連線錯誤原始紀錄保留，歸因尚未確認。原5406／5408完整 state／graph前後相同，41個監看原生檔SHA不變。

驗收官核對206份fixture、203份未變parent來源、82份未變正式src、8份交驗腳本／方案、20份runtime、24份producer證據及37份實際served資產。對提交的完整raw重算事件序號／counter／dropped、取消還原、完整state／graph／鏡頭、正常一筆命令及Undo／重讀，結果與原始證據相符。這是**獨立審閱producer證據**，不等於他已重新操作。

新阻擋 `QA-R15-INFRA-001`（P1，驗收設施，未歸因產品）：驗收官CUA在輸入前回報 `failed to write kernel assets ... (os error 3)`，一次公開重設後的標準入口仍失敗，獨立瀏覽器動作為0。他的預設cwd及TEMP／TMP均存在；root既有CUA仍可讀inventory，卻不能代替驗收官的新kernel。本機唯讀log診斷沒有提供可安全修復的具體缺失path，未改設定、重啟app或改用其他UI驅動。原始錯誤及判定保存在 `artifacts/acceptance/input-events-r15-20261028`。

既有R12控制器8/8及R14後端191/191已按實際controller雜湊、原TRX和三份受測DLL身分重新核對；沒有重跑，也不作為原生失焦證據。補充紀錄見 `artifacts/input-events-r15/evidence-reuse-after-freeze.json`。

獨立結果已封存並逐檔收件：288份資料／185個原生與樹木pins均核對，37份served資產相符。見 [驗收官判定](../artifacts/acceptance/input-events-r15-20261028/VERDICT.zh-TW.md) 與 [收件紀錄](../artifacts/input-events-r15/review-receipt.json)。自有5449／5450／5451服務均已停止；沒有改動使用者5406／5408。

驗收官另確認producer截圖實際為JPEG，原凍結檔副檔名為png。已補上相同bytes／像素的 [正確副檔名截圖](../artifacts/input-events-r15/restored-browser.jpg)，原檔及凍結manifest不變。這張圖屬producer實測，並非新的驗收官截圖。

未啟動 Unity、未打包、未執行 Save。一般視窗失焦、document hidden、同一cancelled手勢的晚到trusted pointerup仍待真實事件；原完整計畫及 Unity 往返門檻繼續保留。

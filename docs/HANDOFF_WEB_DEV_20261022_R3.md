# 本機網頁來源開發版 · web-dev-source-r3 補充

本版保留 r2 多行名稱修正及 r1 的啟動方法、完整規劃與功能界線；**仍不打包**。執行 `scripts/start-web.ps1`，目前來源服務在 `http://127.0.0.1:5286/`。最新來源清單在 `artifacts/web-qa/web-dev-source-r3`；舊版清單／證據不覆寫。

## WEB-AC-001 的修正

主工作及驗收官均在隔離 Demo 工作複本實際重現：`POST /api/scene/edit`，`field=layer`、`value=2147483648`，會因超出 Int32 範圍產生未捕捉的 FormatException。預設 Windows EventLog logger 又因權限不足拋出 AggregateException，客戶端最終得到 ResponseEnded／連線提前結束。沒有變更文件、沒有請求存檔。驗收官缺陷紀錄為 `artifacts/acceptance/web-dev-source-r2-20261022/WEB-AC-001.json`。

Program.cs 現在將 FormatException／OverflowException 回傳 HTTP 400、`INVALID_VALUE` 及繁體中文修正提示；不輸出系統堆疊。開發日誌改成 console-only，不要求 Windows EventLog 寫入權限。未改變欄位允許範圍、存檔核心、版本診斷或任何原生資源格式。

## 本版證據與重用範圍

- `artifacts/test-results/web-r3-http-regression.trx`：最新全部 9 項 Web HTTP 測試通過、0 failed／skipped，含原有 5 項流程及新增 4 項 Layer／Static signed／unsigned 邊界。每個失敗輸入都驗證正確 JSON 400、之後的 GET 仍可用、dirty=false、Undo=0、原生文件／來源未變。
- `artifacts/logs/web-r3-http-regression.log`：原始建置及測試。正式啟動日誌在 `artifacts/logs/web-dev-source-r3-startup.log`，C# 建置 0 warnings／errors。
- `artifacts/web-qa/numeric-boundary-before.json` 與 r2 原始開發 host 日誌保留重現；`numeric-boundary-after-r3.json` 用新的正常來源服務重試相同 Layer=2147483648，確認 HTTP 400、INVALID_VALUE、中文提示、文件未變，沒有連線中斷。
- Core／Unity writer 和其測試來源未變；原 87 項核心／CLI 證據重用。最早 92 項中的 5 項 Web 流程在本版 9 項中重新執行，不把不同範圍相加冒稱一份最新全套 TRX。
- 真實 Room_main 的資料寫入器、原生格式與 SaveTransaction 未變，保留先前 API 存檔／新程序讀取／Undo 位元還原證據。新的 host DLL 身分在本版 evidence manifest 中；不重跑無關的 5.8 GB 全檔檢查。
- 驗收官 r2 已獨立核對來源／證據並通過 8 項 API 檢查，含精確 CRLF／LF／Unicode 名稱、新 HTTP 程序讀取及 no-op／Undo；屬 API 證據，不是 textarea 操作證據。遇到 WEB-AC-001 後未執行的案例不計通過。

請驗收官先核對 r3 清單，重測 WEB-AC-001 及必要鄰近數值／meta 衝突等剩餘案例。既有有效的原檔、工作複本及原生保存證據不需整套重跑。

## 仍未通過

雙方受支持的瀏覽器工具皆因 kernel assets／os error 3 初始化失敗；瀏覽器畫面、console、WebGL、popup、拖曳、鍵盤、窄版、textarea 的實際操作仍為 **UNVERIFIED_BROWSER**。後端與靜態檢查不能替代它。完整 ASE／URP、Prefab、Terrain、Play、建模、粒子、尋路、烘焙、Unity 雙向載入及乾淨無 Unity 測試機仍未完成；七項版本矛盾不因本版修正而解除。

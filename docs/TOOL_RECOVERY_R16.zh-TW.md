# 驗收工具復原交接（2026-10-28）

狀態 **UNVERIFIED**，`QA-R15-INFRA-001` 尚未解除。已依新授權收斂診斷，沒有修改受驗功能、工具資產、安全設定或共享程序，也未啟動 Unity 或打包。

本輪核對了驗收官新增的程序／暫存目錄／路徑型環境資料。指定新暫存目錄可由受限 exec 寫入、讀回並移除唯一測試檔，不能據此推論 MCP 程序自身可以建立核心資產。仍未得到可精準修復的缺失資產路徑。

額外核對的支援介面：

- 安裝中的 node_repl.exe 公開 help 只有停用 sandbox 的額外旗標；沒有使用它。
- 本機 Codex 0.163.0-alpha.11 產生的協定 schema 明列 `config/mcpServer/reload` 的 params 為 null，沒有指定 thread/server 的選項；[官方說明](https://learn.chatgpt.com/docs/app-server) 指它會排程刷新已載入對話，未執行全域重載。
- 公開 `codex app-server proxy` 的預設管理 socket 不存在；受限呼叫另回報10050。沒有另建daemon、socket、猜測私有RPC或停共享host。Node子程序探測於spawn階段回EPERM，沒有送出管理請求。
- [官方MCP說明](https://learn.chatgpt.com/docs/extend/mcp) 提供設定中的Restart操作，但使用者已向驗收官回覆找不到node_repl選項，因此沒有將它描述為可立即執行的修復。

驗收官已作為單一窗口向使用者說明保留工作、退出再重開宿主的外部步驟；root不重複要求，也不自行執行。重開能否修復仍未驗證；只有驗收官的新核心成功取得inventory並實際操作，才能解除阻擋。

只讀狀態快照：5406 revision6、dirty=false、Undo0／Redo1；5408 revision2、dirty=false、Undo0／Redo0。5286本輪無法連線，沒有啟停。這些紀錄不等於可以保證宿主退出後記憶體歷程仍存在；命令歷程以記憶體Stack保存，磁碟檔保留和Redo／工作階段延續必須分別確認。沒有為保存快照而操作Redo、Undo或Save。

本輪資料位於 `artifacts/tool-runtime-recovery-r16`，驗收官原始診斷位於 `artifacts/acceptance/tool-recovery-20261028`。R15原凍結來源／證據與收件結果均未改動。工具恢復後由驗收官接續60001的一般失焦、hidden及同取消手勢晚到pointerup實測，先前未驗證狀態保持原樣。

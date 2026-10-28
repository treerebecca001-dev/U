# 本機網頁來源開發版 · web-dev-source-r2 補充

延續 `HANDOFF_WEB_DEV_20261022.md` 的範圍、啟動方法、完整規劃及已知缺口；**不打包**，目前網址仍是 `http://127.0.0.1:5286/`。r1 的來源／證據清單不覆寫，r2 清單另存於 `artifacts/web-qa/web-dev-source-r2`。

交驗後自查發現，HTML 單行 text input 會移除原生 GameObject 名稱中的換行，可能在只選取物件後被當成尚未套用的改名。這是來源推導，尚未取得瀏覽器重現。r2 改成可多行、不可手動拖曳縮放的名稱 textarea，隨輸入增加行數，超過六行可以捲動。textarea 也會將 CRLF 正規化成 LF，因此在實際 input 事件前，reader 回傳原始字串；沒有主動改名就不送出 name 變更。

名稱沿用其他 Inspector 欄位的 disabled／busy、錯誤文字、aria-invalid、錯誤焦點及 IME submit 保護。新增 textarea 也納入存檔期間的輸入鎖定。

本版只修改前端與說明，C# 與後端測試內容未變。重用 r1 的 92 項後端測試、真實 Room_main API 存檔／新程序讀取／Undo 位元還原證據；不重跑仍有效的測試或全專案雜湊。對修改後前端重新跑 JS 語法與 strict static audit。瀏覽器驗收仍為 UNVERIFIED，兩者不能替代實際畫面／操作證據。

請驗收官除 r1 的案例外，再用隔離 Demo 複本建立含 LF、CRLF 與 Unicode 的名稱樣本，確認只選取／套用其他欄位／儲存不會自行改名，編輯多行名稱可正常存取，無效名稱時焦點和草稿保護正確。不要修改 D: 原始範本。

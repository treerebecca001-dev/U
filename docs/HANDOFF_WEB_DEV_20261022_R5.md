# 本機網頁來源開發版：r5 與真瀏覽器交驗

延續 r4 的有限原生場景編輯範圍，仍由 `scripts/start-web.ps1` 啟動 `http://127.0.0.1:5286/`，不啟動 Unity、不打包、不啟用排程。核准的完整需求和七項範本版本矛盾全部保留。

## 已修正的實際操作問題

r4 在 390×844 窄版選取物件時，Inspector 的 sticky 按鈕列遮住 Position X。欄位中心命中「還原輸入」按鈕，捲動到欄位後仍被遮住。重現畫面、實際位置與 hit-test 記錄保留於 `artifacts/web-qa/browser-root-r4-20261022-run2/layout-followup.json` 與同目錄 PNG。

r5 僅修改 `editor.css` 的既有窄版規則：小於 900px 時，Inspector 按鈕列使用一般文件排列。桌面維持原本的面板內 sticky 行為。C#、DLL、儲存核心、JS 和相依套件均未改變。

真 Chrome 重測 390、899、1440px，Position X 中心均命中欄位本身，實際 click → fill → Tab → 還原成功，頁面無橫向溢出；console error、pageerror、failed request 均為空。結果及逐張畫面在 `artifacts/web-qa/browser-root-r5-20261022/narrow-result.json` 與 `width-*.png`。此結果僅支持這次修正及相鄰桌面行為，完整網頁工作流由驗收官獨立操作。

## 瀏覽器驗收能力

原 CUA 控制工具仍出現 kernel assets/os error 3，未繼續反覆重試。`load_workspace_dependencies` 明確提供的已安裝 Playwright 1.62.1 能透過官方 Library API 啟動獨立 Chrome 155.0.8059.82，保留 `chromiumSandbox:true`，不使用個人瀏覽器資料。第一次執行沙箱回報 spawn EPERM；依正常 `exec_command require_escalated` 自動審核後成功。沒有下載新工具、修改系統安全設定或使用 `--no-sandbox`。

初始唯讀探針見 `artifacts/web-qa/browser-probe-20261022`。根對話的 UI 操作見 `artifacts/web-qa/browser-root-r4-20261022-run2`：開啟來源、經 UI 建立完整工作複本、非法輸入保留與焦點、Layer 原生鍵盤選單、搜尋無結果／清除皆通過。第一輪腳本用錯示範物件名稱而超時的原始紀錄仍保留於 `browser-root-r4-20261022`，只屬測試定位錯誤；後續改用場景實際名稱，使用新複本重跑。

## 證據重用與未完成範圍

r3 的 9 項 Web HTTP 測試、87 項既有核心／CLI 證據、Client Room API 存檔／新程序讀取／位元還原，以及驗收官的 17 項 API 結果仍適用；本次沒有修改其相關來源或 DLL。WEB-AC-001 維持限 API 範圍結案，不以這些證據取代真瀏覽器驗收。

最新固定來源及證據清單分別為 `artifacts/web-qa/web-dev-source-r5/source-manifest.json`、`evidence-manifest.json`。不覆寫 r1～r4 清單。驗收官另行保留獨立操作的畫面、console、失敗請求、原生檔案及問題紀錄，後續直接回報實作對話並修正。

目前預覽仍是基本網格、Transform、碰撞框與未知物件標記；完整 ASE／URP 外觀、模型與 Prefab 幾何、Terrain、Play、建模、粒子、尋路、光照烘焙、Unity 端往返及乾淨無 Unity 機驗收尚未完成。這次交驗不宣稱完整計畫通過。

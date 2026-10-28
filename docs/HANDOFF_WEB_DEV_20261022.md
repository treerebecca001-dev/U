# 本機網頁開發版交驗 · web-dev-source-r1

本次依人類最新指示，提供原始碼啟動的本機網頁開發版，**不打包、不執行 publish、不製作安裝包或壓縮檔**。先前已完成的桌面匯出僅保留歷史紀錄，未宣稱通過。驗收官為「擔任驗收編輯器驗收官」；追蹤 automation 維持 PAUSED。

完整核准規劃在 `docs/APPROVED_PLAN.zh-TW.md`。使用者後續授權優先完成不需 Unity 的開啟／有限編輯／原生儲存工作檯，不等待整套原生生成能力完成；完整要求仍保留。

## 固定來源及啟動

- 工作區：`E:\2026場景編輯器`。
- 固定來源清單：`artifacts/web-qa/web-dev-source-r1/source-manifest.json`。逐檔相對路徑、長度與 SHA-256；不是安裝包。正式交驗訊息另外記錄此清單雜湊。
- SDK：`global.json` 固定 .NET 10.0.402；Web runtime .NET 10，Core／Unity library 為 .NET 8。Godot、Unity、Node 都不是執行網頁服務的必要條件；Node 只用於前端語法檢查。
- 本機 three.js 0.180.0／r180，官方 commit `0af9729d0c143a86a1d725d6e2c3ad83301f3f34`；來源與適配前後雜湊在 `src/SceneWorkbench.Web/vendor-manifest.json`。授權在 vendor／LICENSE。正常執行不讀 CDN、不啟動 Unity、不執行專案 C#／Lua。

```powershell
cd 'E:\2026場景編輯器'
.\scripts\start-web.ps1
```

網址：`http://127.0.0.1:5286/`。可用 `-Port 5287` 隔離獨立驗收程序；Ctrl+C 停止。`-NoBuild` 只供已建置且來源未變時使用。正常啟動不進行 NuGet 還原；新環境先執行 `scripts/bootstrap.ps1`（不加 Restore）及 `scripts/bootstrap-web.ps1`。一次性的固定依賴還原需要網路；後續本機編輯不需要網路。

服務只接受 127.0.0.1 的同來源與正確 Host，API 需每次啟動新產生的 token；沒有跨來源允許設定。HTML／CSS／JS 由來源檔供應，沒有前端編譯打包步驟。C# 修改後重啟。

## 已實作範圍

瀏覽器提供 Hierarchy、Scene 結構預覽、Inspector、Project、Console。可以直接讀取 Unity 專案，建立完整工作複本，瀏覽 Assets／有效 Packages、搜尋、選取場景及物件、編輯已驗證的 standalone GameObject／Transform 欄位，透過命令歷程 Undo／Redo，存成原生 `.unity`，重新讀取磁碟。Project 提供部分圖片與文字的唯讀檢視。

欄位限名稱、Active、Layer、Static flags、Position、Quaternion Rotation 與 Scale；每個屬性是一個 Undo 步驟。fileID 跨 JSON 一律為十進位字串，不使用 JavaScript Number。完整且已驗證的 WorkCopy 才有存檔資格。

Scene 預覽僅包含 Transform、Unity 基本方塊、碰撞框和其他幾何標記；未知模型／Prefab 不表示實際幾何。超過 5,000 物件的場景只預覽前 5,000 個並顯示限制。這不是 ASE／URP 畫面相似度、遊戲執行或完整美術場景驗收。

安全邊界沿用 SceneEditSession：未知 serializedVersion、RectTransform、connected／stripped Prefab、錯誤元件歸屬維持唯讀；未知資料與非編輯文件保留原文。每次存檔重新檢查工作複本、套件、GUID、場景／meta／專案設定的預期雜湊，使用既有備份與 transaction journal，拒絕外部變動覆蓋。多分頁用 revision 防止舊文件直接覆寫。

七項 Unity 2021.3／URP 14 套件版本矛盾仍顯示 Blocking。有限欄位能力只接受先前明列的 runtime-only PACKAGE_EDITOR_MISMATCH／URP_EDITOR_MISMATCH，其餘完整性阻擋仍拒絕寫入。原嚴格 CLI gate 不變，沒有通用強制儲存。

## 已有證據及缺口

- `artifacts/test-results/web-source-regression.trx`：92 passed、0 failed、0 skipped。包含 5 項新 Web HTTP 工作流測試；真實本機 host 程序經 HTTP 測試新程序讀回、signed 64-bit ID、未知原文／meta／來源保護、Undo／no-op timestamp、唯讀、stale revision、外部衝突、無效輸入及 Host／Origin／token／路徑逃逸保護。
- `artifacts/logs/web-source-regression.log` 與 `artifacts/logs/web-tests/`：原始建置／測試及各測試 host 日誌。C# 建置為 0 warnings、0 errors。
- `artifacts/web-qa/client-room-source-r1/report.json`：真實工作複本 Room_main 經 HTTP 編輯 fileID `228251656` 的 X 座標後存檔，由另一個新程序讀取，再由原 session Undo／Save 還原。
- Room_main 前後／還原 SHA-256：`52232055c6de51431aabb61308e4c634f8e56ffabd0df37b6f011d2dfbe5614f`；暫存修改 SHA-256：`ad19154ef9a8a1a4373049e1aee84ffb46c6d3af001fd6ca079bcfccdc039502`。其餘 40 個 Unity 文件、場景 `.meta` 及 D: 原始場景未變；測試另明確還原工作複本場景時間戳。備份與新程序日誌在同一目錄。
- `artifacts/web-qa/premium-audit-r2.json`：strict static audit 0 findings。控制項在 JS 完成事件接線前保持 disabled；不是用假 action 避過檢查。
- 前端 `node scripts/check-web.mjs` 通過。`artifacts/logs/design-lint-web-final.log`：DESIGN.md 0 errors、2 warnings；兩個 warning 是 linter 未追蹤的 border／focus tokens，實際由共用 CSS 的 border／outline 使用。
- `artifacts/logs/bootstrap-web-network-verified.log`：固定 vendor 雜湊核對及 locked dependency restore 通過。受限網路下曾因 NU1900 無法讀官方弱點 metadata；已使用允許的網路環境完成還原，未停用 audit。正常啟動已改為不自動還原。
- `artifacts/web-qa/web-dev-source-r1/browser-blocker.json`：瀏覽器工具實際初始化失敗。`cua.getState()`、一次 js_reset 後的 Chrome 備援均回報 `failed to write kernel assets: 系統找不到指定的路徑。 (os error 3)`。未取得瀏覽器畫面、console、鍵盤、popup、拖曳或窄版實測；不得拿 HTTP／語法檢查冒充 UI 已通過。

本次整體狀態為 **UNVERIFIED：後端與實檔往返有證據，瀏覽器操作仍待驗收**。不是完整產品通過。BL-008～010 的既有局部結案不重開；BL-001～007 矛盾、Unity 原生載入／雙向往返與未安裝 Unity 的乾淨機驗收仍未通過。

## 請驗收官獨立操作

1. 核對 source-manifest，從來源啟動隔離埠。確認初始介面、瀏覽器 console 與 failed requests；後端測試通過不能替代這一步。
2. 按「開啟示範場景」。初次會把 DemoProject 複製到 Workspace／WebDemo，來源保持唯讀；後續開啟已完成的相同複本。選取「可移動方塊」，在 Inspector 改 X，套用、Undo／Redo、儲存、重新載入；用原生檔案／新程序核對。結束後透過 Undo／Save 或正常屬性編輯還原測試修改。
3. 用「開啟專案」讀取 samples／Client231229，開啟 Room_main，檢查可編輯物件與唯讀 Prefab 的標示。原始 D: 範本只可讀取；任何額外有副作用測試優先用隔離 Demo 工作複本。
4. 驗證名稱／NaN／空數值／零 Quaternion 等錯誤輸入、選取其他物件時的草稿保護、存檔期間欄位鎖定、錯誤焦點、搜尋無結果／清除、資源讀取失敗、Layer select 打開與鍵盤、Tab／Escape、快捷鍵及窄螢幕。保留具體操作和截圖／console。
5. 在隔離 Demo 複本測試外部改檔衝突、未存檔導覽的 Save／Discard／Cancel、兩分頁 stale revision。確認錯誤保留輸入且不覆寫外部修改；可以重新載入並明確捨棄草稿恢復。
6. 發現問題直接回報主工作，不需再向人類確認。若你的瀏覽器工具也遇到相同環境問題，明列未驗證；不要繞過限制或將 API 通過升格為 UI 通過。

## 仍保留的完整需求

14 支 ASE Shader 適配及材質編輯、完整 FBX 匯入／子資源識別、Terrain／種植、Prefab nested／variant、燈光／後製／攝影機／Play、ProBuilder／Polybrush、Particle System、兩套尋路原生生成、UV2／光照／探針／Shadowmask 烘焙與 Unity 雙向回存，仍依原始規劃後續開發與逐項驗收。本次未以工具專用格式替代它們。

# 本機網頁來源開發版 r6：大型專案開啟與獨立複驗

使用 `scripts/start-web.ps1` 在 `http://127.0.0.1:5286/` 啟動。不啟動 Unity、不打包、不啟用排程。來源與證據清單放在 `artifacts/web-qa/web-dev-source-r6`，舊版清單不覆寫。

## 最終修改

1. 共用 `SafePath` 使用 `DirectoryInfo.EnumerateFileSystemInfos` 已取得的 Attributes，避免逐項額外查詢；仍逐項拒絕 ReparsePoint、檢查取消，實際讀寫的 Resolve／RejectLinks 未放寬。新增隱藏檔與中文路徑複製案例。
2. Web 開啟只建立來源資源與場景清單；完整 PackageCache 搜尋索引延至首次全域搜尋。既有即時資料夾瀏覽仍能讀取有效套件。搜尋建完且未取消才交換 catalog，失敗可重試，開啟另一專案會重設。`packageCacheIndexed` 表示完整性，`sourceFiles` 為當前已索引數量。
3. 開啟作業提供版本及來源資源兩個進度階段。沒有變更 HTML、CSS、JS、套件、原生欄位寫入規則或存檔完整性檢查。

新增兩項 HTTP 案例驗證套件檔仍可瀏覽／搜尋、索引重設，以及延後搜尋仍拒絕 junction 且不改目標。

## 實際參考專案結果

r5 及尚未延後 catalog 的初版 r6，在 Client 開啟碰到 180 秒測試預算上限，原始紀錄保留於 `browser-client-r5-20261022`、`browser-client-r6-20261022`。分段讀取診斷顯示完整 71,819 檔列舉約 156,646ms，WorkCopy 標記讀取 874ms；未把一個較快的小型 metadata 探針當作產品整體速度。

最終 r6 在獨立 Chrome／1920×1080 真實開啟 `samples/Client231229` 為 **5299ms**，載入 `Room_main.unity`、選取 fileID `228251656` 的 collider、修改本地 Position 草稿並還原成功。WebGL 2，10 個結構項目，七項原版本診斷照常顯示。console、pageerror、失敗請求均空；原 D: 場景、工作場景與 meta 前後雜湊相同。結果與兩張實際畫面在 `artifacts/web-qa/browser-client-r6-lazy-20261022`。先核對全部實際供應的 10 份 JS／CSS／vendor JS 符合 r5 UI 清單，r6 UI 位元未變。

這是單機單次、已有檔案快取的開啟量測，不是冷啟動、完整模型／ASE 預覽或 FPS 驗收。本次 Client 流程沒有套用或存檔；舊版 Client API 存檔紀錄不冒稱為本次瀏覽器寫入。首次大型套件全域搜尋及完整原生存檔仍可能較久。

## 自動測試與證據重用

- `web-r6-regression.trx`：97/97，含 88 項核心／CLI 與當時 9 項 HTTP。其後只改 Web catalog 流程與對應測試，88 項核心／CLI 證據仍有效。
- `web-r6-lazy-http.trx`：新 11 項 HTTP 中 10 項通過；唯一失敗發生在新 junction fixture 的 mklink 路徑含斜線，尚未進入產品端點。
- fixture 改用原有 SafePath 正規化，並解析 JSON 後比較訊息；`web-r6-lazy-link-retest.trx` 該案 1/1 通過。其餘十案與產品碼未再變，合計現行 **88＋11＝99** 項有效案例，並非宣稱單筆 TRX 有 99 項。

對應 log 在 `artifacts/logs`。沒有重掃 5.8GB 原範本雜湊或重新製作完整工作複本。

## 獨立瀏覽器交驗

驗收官在凍結 r4/r5 快照、先核對 11 份 HTML／JS／CSS／vendor JS 後，已完成 basic-2 八組、interaction-2 前五組、narrow-r5-2 四組、name-edges-1 七組、recovery-2 六組、viewport-1 兩組，共 **32 組有效通過**。包含原生保存重載、來源與 meta 保護、未保存導覽、非法草稿與焦點、Layer 真選單、CRLF／空／超長原名、Float32、外部衝突、兩真分頁、beforeunload、Gizmo、畫布選取及相機操作。負向測試預期的 400／409 在瀏覽器 console 有對應記錄，沒有未預期 pageerror／failed request。WEB-UI-002 在 r5 CSS 範圍獨立複驗結案。

第一輪 QA host 的 Development staticwebassets 路徑錯指 r3，basic-1／interaction-1／narrow-r5-1 等混版紀錄保留但不計通過；正式證據均使用重建的快照並核對實際 bytes，不能只靠 ContentRoot 名稱判版本。

r6 新依賴回歸須由驗收官在此固定來源上進行：索引延後與完整套件結果、取消／失敗再試、切專案、連結拒絕、來源保護及基本原生存檔。JS／CSS 未變的 32 組可依適用性重用，不要求全套盲目重跑。

## 仍未交付

完整 ASE／URP 外觀、模型與 Prefab 幾何、Terrain、Play、建模、粒子、兩套尋路、光照烘焙及 Unity 雙向往返，維持完整計畫的後續門檻。原生 IME、完整無障礙、觸控、乾淨無 Unity 機及大場景效能也不以目前證據宣稱通過。七項原版本矛盾保留。

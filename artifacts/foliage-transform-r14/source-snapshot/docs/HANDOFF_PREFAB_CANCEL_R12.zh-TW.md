# Prefab 目標與拖曳取消 r12 複驗

2026-10-27。沿用核准計畫 `docs/APPROVED_PLAN.zh-TW.md`；只交付本機來源開發版，不啟動 Unity、不打包。原始 D 專案唯讀；E 工作複本的 41 個監看原生檔與這輪開始時相同。

## 本輪實作與界線

| 項目 | 本輪結果 | 尚未證實 |
|---|---|---|
| 60001_Wall_P | 四個外層 Renderer 目標唯一對應，其中兩個 CastShadows 值修正；原生 ID／順序保留 | Unity FBX 匯入及引擎畫面 |
| body | 完整來源 125 objects，不含移除目標 5094106267881264939；unused-removal 不移除其他元件、不改原檔 | 原來的元件類型、Unity 端分類 |
| TreeA01 | 在 Client230411 找到舊目標 5565035281593725506 及原巢狀 FBX 身分；現在來源已不同，仍 unresolved-target | 目前應有的美術意圖、Unity 端 unused 分類 |
| 實際拖曳途中 Esc | Demo 移動／旋轉／縮放與真實 60001 地形的 trusted 事件、還原、後續放開、鏡頭／history 均留證 | 獨立複驗結果 |
| focus／visibility／pointercancel | 生產控制器單元路徑通過 | 真實事件時序仍 UNVERIFIED |

這些是預覽解析與操作修正，Prefab／TerrainData 原生寫入能力維持關閉。無法解析的欄位沒有移到另一棵樹、猜測元件或刪除。257 項明示近似仍在；沒有宣稱完整 Unity／URP 相容。

## 目標路由的限制

新增 `PrefabOverrideTargets.cs`：優先使用原生 stripped correspondence。缺少虛擬 stub 的 FBX 僅限 2021.3／2022.3、PrefabInstance serializedVersion 2，以同一實例所有既有 stripped anchors 驗證 `(instance XOR signedSourceID) & 0x7fffffffffffffff`；來源 ID 必須已存在於該 FBX 的原生覆寫且角色唯一。沒有 anchor、任何 anchor 不合、碰撞或角色不明一律不推測。這是樣本驗證的相容規則，沒有宣稱它是 Unity 官方通用格式契約。

路由保留 native last-write-wins，限制在正確的巢狀實例；完整非巢狀來源的不存在 removal 才能標 unused。`PreviewGraph.prefabOverrides` 提供六筆細節：四筆 resolved、body 一筆 unused-removal、Tree 一筆 unresolved-target。Console 目前兩筆診斷固定放在短期載入紀錄之前，計數為 2，fresh reload 後仍可見。

| 外層 target fileID | nested instance | 來源 Renderer fileID | CastShadows r11→r12 |
|---|---|---|---|
| 4391027932898413374 | 3112415062092470202 | -7511558181221131132 | 0→1 |
| 5132720836648353441 | 5835121169829274149 | -7511558181221131132 | 1→1 |
| 6685076069268142894 | 5406497572763445162 | -7511558181221131132 | 1→0 |
| 7917458375259260575 | 8800565630691940891 | -7511558181221131132 | 1→1 |

`prefab-verification.json` 將完整 graph 與 r11 比對，只允許上表四個來源旗標在 node／FBX renderer 各自位置的變化，其餘 nodes、materials、terrain、environment 相同。只正規化 JSON 的數值負零；fileID 字串、原始檔案及雜湊不轉換。陰影 caster 總數仍 3026。

body 在 D 的 Client230411／Client／Client231229 三份內容一致（SHA-256 `636bd4e0e975c5a25321209134f1840fed88b9601ae2060efa53f623a53966ad`）。沒有依名稱猜它的原元件類型。

Tree 舊 Prefab SHA-256 `2def726686224cd2a7db1e19d6585728e1a3d7ee622170c67dbc6c4665dbe702`；舊 stripped Transform 對應 source `-8679921383154817045`、GUID `78b4ac0deff380946a311d7c61f3bc07`、instance `5381875668383798697`，這些 instance／模型在目前來源已不存在。唯讀副本在 `legacy-source/`，沒有匯入 E 專案。

## 真實取消缺陷與修正

1. 先前 Escape 被 input shortcut guard 擋住；目前活躍拖曳先處理 Escape，IME／dialog 仍保留控制權。舊控制器同一單元案例 7/8，目前 8/8。
2. 真實拖曳中 Esc 後，物件還原但剩餘 mouse moves 讓鏡頭轉到地下。原失敗 JSON／JPEG 和兩支原程式保留於 `native-escape-camera-before-fix.*`、`pre-camera-fix/`。
3. 目前取消時透過 OrbitControls 公開 dispose／重建生命週期清掉舊指標互動，保留 camera／target，沒有改 vendor 私有狀態。

`native-cancel-verification.json` 驗證四組生產 DOM 時序：trusted pointerdown → buttons1／changed preview → trusted Escape（仍 active／changed）→ 原值還原 → 較晚的 trusted pointerup／buttons0，沒有 commit；鏡頭 before／mid／after 相同。

最強的真實專案配對：`client-native-escape-paired.json`，Terrain1114381152 原位置 `[-62.700005,0,-8.2]`，拖曳中 Y=10.207171521163275；Esc 還原，約 2.2 秒後放開仍未提交。`client-paired-before.json`／`client-paired-after.json`：revision 13 完全相同、dirty false、Undo=0、Redo=0、所有 items 與 graph 不變、41 原檔不變。場景 SHA-256 `6a93d9dbcdd39e186c09dcb5c1130d9cecf6baf3caef602328ba796ce6903f3d`。

三次 blur／visibility 的實際 UI 嘗試（另一個 IAB tab、browser visibility、Ctrl+L）都沒有產生可觀察的對應事件，因此正常完成拖曳、各增加一筆未儲存命令；原始紀錄完整保留，隨後用 UI Undo／Reload 還原，沒有存檔。這些記為 **UNVERIFIED_EVENT_GENERATION**，不是產品取消已通過或失敗。pointercancel 亦未生成原生事件。請勿用合成 DOM event 或單元案例替代真實時序。

## 可重現的真實 Esc 操作

使用專屬 QA 服務 5432，不能操作 5286／5406 主工作階段。讀取 E 基準、Map_60001，搜尋 `60001_Terrain`、選 id1114381152、按聚焦；1178×1000 viewport 下 Y gizmo 約 `[358,434]`→`[358,342]`，實際坐標仍須按當次截圖確認。

CUA 支援在尚未 await 的原生 drag 進行中，等待生產 DOM 的 `#viewport[data-transform-dragging="true"][data-transform-drag-changed="true"]`，讀取當時 DOM 作證，再 `tab.pressKey(null,'Escape')`，最後 await drag。不是在拖曳完成後補按 Escape。此工具方式只證明真實 key 事件；不注入事件或改動 app 狀態。

Viewport `data-transform-gesture` 是最多 12 筆的被動觀察，附 `isTrusted`、buttons、key、pointerID、時間與原值；`data-preview-camera` 由實際 controls change 更新。它們沒有寫入 API 或控制能力。

## 驗證與固定交付

- 完整後端 187/187、0 skip、0 failure：`tests/backend.trx`。最初一次 PowerShell 未引用 MSBuild colon 參數，MSB1008 在測試啟動前退出；修正引號後才是這份成功 TRX，沒有把初次錯誤算成通過。
- 新增 10 個後端情境，涵蓋真實 signed IDs、scope 隔離、缺 anchor／錯 anchor／collision、覆寫順序、非 FBX nested 路由、component removal；檢查 native 原文不變。
- `cancel-unit-baseline.json` 是舊控制器 7/8；`cancel-unit-camera-fix.json` 是生產控制器 8/8，明示 simulated unit scope。新 native chronology 檢查另列。
- `check-web.mjs` 通過。Console 最後修正只改 JS；後端未變，不重跑完整 187 組。
- fresh reload 完整 6613 nodes／3105 meshes／633165 triangles；265 FBX bindings；Terrain524288 triangles；missingLayer0；257近似；環境反射仍在。Console2，瀏覽器 error log空。`client-console-persistent.json/.jpg`、`browser-errors.json`。
- 三個 Prefab Owner 的實際 Inspector 仍唯讀、Apply disabled，證據 `client-wall-browser.json`、`client-tree-browser.json`、`client-body-browser.json`。

`artifacts/prefab-cancel-r12/handoff/` 保存來源、當次已測執行檔及證據 SHA-256 manifests；`source-snapshot/` 和 `runtime/` 是一般資料夾，不是壓縮包或發佈。這輪來源與資源停止修改後交獨立驗收，規劃全文也在來源清單內。

驗收官請在 60001 重測四個 Wall 對應、body／Tree 診斷仍可見且沒有猜綁、Prefab 唯讀、地形／水／樹回歸、實際 changed-drag Escape 與放開後鏡頭／history／原檔，再嘗試可用的真實 focus／pointercancel；不能產生時繼續保留 UNVERIFIED。只有取得獨立結果後更新收件紀錄，原始主工程證據不可覆寫。

## 獨立複驗收件續錄

2026-10-27 已收件，整體 **UNVERIFIED／局部通過**。原始受驗來源仍固定在 `source-snapshot/`；本段與 README／IMPLEMENTATION 的驗收結果、CastShadows 用詞更新為驗收後的文件更正，沒有改動已測程式、runtime 或原生資源。文件前後雜湊另列於 `artifacts/prefab-cancel-r12/post-review-docs.json`。

驗收官用自行核對的 181 檔來源、20 檔執行產物及 49 檔原始證據，在獨立服務完成 60001 的四次原生中途 Esc：移動、旋轉、縮放，以及 Position Y 輸入框取得焦點後取消。原值、鏡頭、API/history 均還原或保持，後續正常拖曳只產生一筆命令；fresh reload、三個 Prefab 唯讀、Console2 與草地／水面／樹木回歸分項通過。41 個 E 原檔與 20 筆來源／TerrainData 保護記錄未變。最短約 2.6 秒是「已處理取消」到「已處理放開」的觀察間隔，與原生事件建立時間戳分開記錄。

Wall 的四個受限路由與 18 個原生 anchors 已獨立核對。body 只確認目前來源不存在與安全保留；其 Unity unused 分類仍未驗證。TreeA01 目前有效目標與美術意圖、原生 blur／visibility／pointercancel、完整 Unity／URP 及其他原生互通仍 UNVERIFIED，不能把父項全數標記完成。四例之一之後曾有來源未明的 `0搞` 草稿，驗證防護阻擋提交，已用 UI 還原；因果不明，不歸產品缺陷，原始紀錄留在 `execution-notes.json`。

341 檔獨立證據已逐檔核對收件，見 `artifacts/prefab-cancel-r12/review-receipt.json`。正式報告：`artifacts/acceptance/prefab-cancel-r12-20261027/VERDICT.zh-TW.md`，SHA-256 `ef035a996fe1a4d6f68a9f522de2d7aed6ab314f25e600dd07d09cabd958a2d9`。驗收官專用分頁／服務及主工程 5432 QA 服務皆已關閉；主開發入口 5406 保留，沒有啟動 Unity、沒有打包。

# 實作狀態與關卡

本文件對應使用者核准的 Windows 獨立場景編輯器計畫。原始範本保持唯讀；完整工作複本是唯一預設回寫目的地。

2026-10-22 使用者再次確認：目前沒有可用的 Unity，本工具必須在沒有安裝、啟動或授權 Unity 的狀態下完成日常開啟、編輯、預覽及原生儲存。Unity Editor 只能是另外進行的相容性驗收環境。使用者隨後授權先完成離線開啟／編輯／存檔版本，直接交驗、修正，不等確認；最新指示改以本機網頁開發版進行且不要打包。這項有限工作檯不等待完整階段 1；其他高風險原生生成和 Unity 往返驗收仍須通過原門檻。

## 已建立的程式模組

| 模組 | 實作內容 |
|---|---|
| SceneWorkbench.Core | ProjectSession、UnityAssetId、命令歷程、雜湊、實體路徑保護、工作複本、SaveTransaction |
| SceneWorkbench.Unity | 版本診斷、GUID 索引、場景／ASE 盤點、局部 YAML 修改、來源 FBX／Mesh／Prefab 預覽、原生 Terrain 高度及同場景反射讀取 |
| SceneEditSession | 完整 standalone GameObject／Transform 的已驗證欄位、跨存檔 Undo／Redo、完整性及外部修改保護 |
| SceneWorkbench.Web | 本機 ASP.NET Core 服務、Hierarchy／Inspector／Project／Console、來源模型／貼圖、固定 ASE／水面適配、原生材質編輯及 Scene 工具 |
| SceneWorkbench.Desktop | 已開發的 Godot 原型；最新工作改為網頁來源開發，不再匯出或製作安裝包 |
| SceneWorkbench.Cli | audit、copy、verify-copy、probe、patch-transform、recover |
| SceneWorkbench.Tests | 原檔保護、未知資料保留、64-bit ID、YAML 修改／復原、衝突與 journal 重啟復原、真實 CLI 工作流 |

這些模組尚未構成完整桌面編輯器。執行中的工具不載入 Unity Editor API，不執行範本 C#／Lua，不自動下載私有套件，也不修改範本版本。開發修復已在工作複本另行找回原鎖定 commit 的完整 YooAsset；這項一次性的套件修復不構成正常使用時的 Unity 依賴。

## 關卡順序

0. 固定可重現的 Unity／URP、套件與資源基準。保留七項版本矛盾，缺漏修復已有獨立驗收；只有已驗證的有限欄位能力可在 runtime-only 版本矛盾下編輯工作複本。
1. 分別取得 FBX 子資源對應、可編輯 Terrain／Mesh、ProBuilder／Polybrush、兩套尋路、完整光照及 ASE 畫面的 Unity 往返證據。
2. 依後續授權先建立來源啟動的本機網頁工作檯及有限原生存檔。高風險工具按關卡逐項開放。
3. 完成固定 ASE、燈光、攝影機、後製、碰撞與預覽玩家，形成第一個可用閉環。
4. 完成地形、種植、合併、ProBuilder／Polybrush、巢狀 Prefab 與 Variant。
5. 完成 Particle System、Unity NavMesh 及 A*／Recast。
6. 完成獨立光照後端、UV2、Lightmap、Mixed、Shadowmask、Light Probes 與反射探針。
7. 大場景效能、穩定性、發佈與完整雙向驗收。

網頁基本工作檯已有 32 組獨立真瀏覽器有效案例；r6 的開啟／套件搜尋索引變更另作依賴回歸，詳見 `HANDOFF_WEB_DEV_20261022_R6.md`。這些結果限目前可操作的欄位與結構預覽。

## 2026-10-27 接續狀態

- r8 離線套件／版本基準與兩份完整 71,819 檔工作複本內容比對已有獨立驗收。這不代表 Unity 已匯入、遷移或完成真正雙向往返。
- r7 的 14 支固定 ASE Shader 已做瀏覽器適配與操作測試：9 支使用原專案材質，5 支以原生預設材質測試。Material Inspector 可修改已支援的參數及貼圖，安全寫回 `.mat`；完整 Unity 畫面仍待比對。
- r9-fix2 的主場景是 Map_60001。樹木原綁定／裁切、513×513 原生 Terrain 高度、StylizedWater2 原波動／深度／交界、既有原反射及單張方向光接影均取得明確範圍的獨立驗證。當時第 0 TerrainLayer 原始來源缺失，地表棋盤格使該資料版本的整體 60001 驗收 FAILED；歷史結果保留於 `artifacts/acceptance/scene-60001-20261022/VERDICT-r9-fix2.zh-TW.md`。
- r10 新增 Q／W／E／R、旋轉／縮放、世界／區域與吸附，沿用既有原生 Transform 命令。主工程實際拖曳、原生儲存重讀及 60001 唯讀回歸已留證；獨立驗收另確認父層／負縮放、三軸、吸附、窄版、原生存檔與外部衝突的限定範圍。`WEB-TRANSFORM-R10-002` 的拖曳中途 Esc／pointercancel／blur 仍缺實際時序證據，保留 UNVERIFIED，不視為已證實的取消缺陷。原始交驗見 `docs/HANDOFF_TRANSFORM_R10.zh-TW.md`。
- r10-fix1 修正獨立發現的 `WEB-TRANSFORM-R10-001`：內建方塊的預覽面分組缺少材質造成點選例外。驗收官以固定 169 檔來源實際重現點選切換、原生單步拖曳與 Undo／Redo、唯讀 60001，已將此缺陷限定範圍標記 VERIFIED；135 檔獨立證據雜湊已核對收件。此修正不變更 Transform 命令或原生資料。見 `artifacts/acceptance/transform-r10-20261027/VERDICT-fix1.zh-TW.md` 與 `artifacts/transform-r10/review-receipt-fix1.json`。
- terrain-recovery-r11 從同層 `Client` 找回第 0 層的原 GUID／fileID、原 Grass PNG 與 metadata，來源 TerrainData 與指定範本完全同 SHA。只在 E 工作複本新增五個原本不存在的原生檔，D 槽保持唯讀，既有受觀察輸入、高度與權重均不變。驗收官以固定來源 172 檔、資料 5 檔與證據 9 檔完成獨立瀏覽器複驗；missingLayers 1→0、草地恢復、完整高度與權重不變、fresh reload 正常，35 個 E 原生檔與 8 筆來源／TerrainData 保護記錄均未變。`WEB-TERRAIN-60001-002` 在這批新輸入下 VERIFIED，246 檔獨立證據已逐檔核對收件。使用者 5406 已載入這批資料。見 `artifacts/acceptance/terrain-recovery-r11-20261027/VERDICT.zh-TW.md` 與 `artifacts/terrain-recovery-r11/review-receipt.json`；不要重跑復原腳本。
- r12 修復 60001_Wall_P 的四個外層虛擬 Renderer 目標路由，以該實例的原生 stripped anchors 驗證版本化對應，其中兩個投射陰影（CastShadows）值真正改變，其餘圖形資料與 r11 相同。body 的移除目標已不在目前完整來源，標記 unused-removal 並保留原文；TreeA01 的缺失目標在舊 Client230411 找到，目前 Prefab 已更換巢狀實例與模型參照，保留 unresolved-target，沒有換綁新樹或回復舊檔。舊 leaves2 FBX 仍在目前專案，不能稱為 FBX 檔案遺失。Console 固定顯示兩筆剩餘診斷，避免載入訊息把它們擠出。
- r12 實際原生 pointerdown → changed pointermove → Escape → restore → pointerup 已在移動、旋轉、縮放及 60001 Terrain1114381152 留證；實測發現並修正取消後的 OrbitControls 鏡頭跳動。60001 配對 API revision 13 不變、dirty false、Undo／Redo 0、41 個監看原生檔不變。完整後端 187/187 與取消單元 8/8 通過；focus／visibility／pointercancel 仍限單元證據，三次真實 UI 嘗試未產生對應事件，不冒稱已測。最新獨立複驗入口見 `HANDOFF_PREFAB_CANCEL_R12.zh-TW.md`。
- r12 已完成獨立複驗與 341 檔原始證據收件。整體 **UNVERIFIED／局部通過**：Wall 受限路由、body 來源不存在與安全保留，以及在 60001 的移動／旋轉／縮放／輸入框焦點四次原生中途 Esc、後續正常單步拖曳、fresh reload 與 Console 均分項通過；41 個 E 原生檔與 20 筆來源／TerrainData 保護記錄不變。TreeA01 目前目標／意圖、body 的 Unity unused 分類、原生 blur／visibility／pointercancel 及完整互通仍未驗證。沒有新增已歸因的產品失敗；來源未明的 `0搞` 草稿另留證，驗證防護阻擋了提交並已由 UI 還原。見 `artifacts/acceptance/prefab-cancel-r12-20261027/VERDICT.zh-TW.md` 與 `artifacts/prefab-cancel-r12/review-receipt.json`。
- r13 逐檔比對四份專案共144份指定樹木資料，確認舊 leaves2 FBX 仍在，現有 Prefab 已切換其他 GUID；保留舊 target 的 x=-0.2/z=0.87，沒有猜測對應。原生 HTML 拖放 QA fixture 已取得 changed→trusted pointercancel→restore，原生模態 blur 嘗試含工具中斷，不能算一般 OS 失焦完成。完整 state／graph、原檔與後續正常拖曳／Undo 已核對，獨立事件複驗另案接續。見 `HANDOFF_TREE_INPUT_R13.zh-TW.md`。
- r13 獨立事件複驗完成289檔收件：一次真實HTML原生dragstart接管，changed Y11.3673104746→trusted pointercancel→Y0、lostpointercapture、約2242.9ms後dragend，零commit、完整state／graph及鏡頭不變。visibility能力在changed拖曳中成功隱藏表面，但DOM仍visible、hidden=false且沒有window blur；此案例為正常一筆命令，Undo／reload完整還原。一般OS失焦／hidden與pointercancel後晚到pointerup維持UNVERIFIED；觀察器未登錄pointerup，不能從trace缺事件推論UA沒發出。185個原生／樹木pins不變，正式控制器未改。見 `artifacts/acceptance/tree-input-r13-20261027/VERDICT.zh-TW.md` 及 `artifacts/tree-input-r13/review-receipt.json`。
- r14 修正來源 FBX Model 根位置先寫入頂點、再乘原生覆寫的重複放置；只有單 Model／Mesh、已知根 fileID 與逐軸來源才分離。以同 GUID 歷史 ShaderGraph 的 URP 固定 Both／AlphaClipThreshold0.5 校準六份 TreeA01 材質變體，hash失配回退，原生材質仍唯讀，沒有加入 billboard 或補造幾何。當前 ShaderGraph／版本等價、UV風動與Unity光照未驗證。
- r14 已收件405檔獨立證據，317個原生／來源pins不變。兩株 owner222870／630463173 的正面與海側側面接合、重讀、歷史材質負向guards／唯讀UI、61來源／58直接Mesh、三份Gress保留、原生Esc與晚到pointerup、正常單筆拖曳／Undo／完整重讀分項 VERIFIED；191/191受測組件身分已核對，未重跑相同C#套件。Gress全面原生位置、舊Tree覆寫與完整Unity/畫面仍UNVERIFIED。驗收模式direct、producer與新開發服務copy的差異明列，未驗收存檔模式等價。見 `artifacts/acceptance/foliage-r14-20261027/VERDICT.zh-TW.md` 與 `artifacts/foliage-transform-r14/review-receipt.json`。
- 最新 60001 仍有 257 項明示近似。完整 Scene／Unity／URP／StylizedWater2 相容性仍為 UNVERIFIED；分項唯讀回歸通過不開放 Prefab 寫入、Terrain 編輯或其他原生生成關卡。

以上是分項能力，沒有以瀏覽器預覽或沿用既有烘焙資料取代原計畫的新資料生成門檻。最新R14開發入口為5408，以 `scripts/start-reviewed-r14.ps1` 啟動固定普通來源／既有測試runtime；後續修改位於 `artifacts/foliage-transform-r14/source-work`。主5406與根src保留原階段及Redo1，沒有重啟、reload或混用新舊後端。QA另用隔離服務，不啟動Unity，也不打包。

## 2026-10-28 原生中斷事件

R15新增隔離被動observer，獨立記錄pointerup／pointerdown、事件序號及遺失計數；正式src／R14 runtime不變。Producer一次changed HTML原生pointercancel後完整state／graph／鏡頭還原且零提交，取消到新down之間沒有pointerup。後續同pointerId的up屬新勾選框手勢；不冒稱已驗證取消後的晚到up。新popup刺激在changed拖曳中沒有真正window blur／hidden，正常一筆命令、Undo／重讀完整還原。

驗收官核對206份fixture、8交驗來源、20runtime、24份producer證據、37份served資產及原生／樹木pins，確認提交raw的時序與結果；自身CUA兩次在輸入前初始化失敗（中間一次公開重設），`QA-R15-INFRA-001`為設施阻擋，獨立UI執行0次。cwd／TEMP存在，沒有已建立的安全修復，未改設定／重啟app或替換驅動。總狀態UNVERIFIED，三項真實事件與獨立UI實測門檻保留。原5406 revision6／Redo1及5408 revision2的完整工作階段不變，不啟動Unity、不打包、不Save。詳見 [VALIDATION_R15.zh-TW.md](VALIDATION_R15.zh-TW.md)。

## 尚未實作或驗收的能力

- 完整 Scene／Game、原生模型子資源全面識別及玩家 Play，以及原生 IME、完整無障礙與觸控驗收。
- ASE 與 Unity 畫面校準，以及完整 Gamma、雲影、X-Ray、Decal、Tilt-Shift 與後製比對。
- 新 Terrain／Mesh 編輯、ProBuilder 5.2.2 與 Polybrush 1.1.5 編輯資料寫入。
- Prefab 結構與覆寫寫入；現有 YAML 修改器刻意拒絕 stripped Transform。
- 原生 Unity NavMeshData 與 A* 4.3.60 新圖形生成。
- xatlas／Embree／OIDN 光照後端及 Lighting Data 的原生綁定。
- Unity 端載入、繼續編輯和回存；未安裝 Unity 的乾淨測試機與效能驗收。

不將未完成的能力放成可操作的 UI，不用一般網格、工具自訂格式或既有烘焙檔案替代使用者要求的原生生成能力。

## 儲存行為

每次提交先驗證所有目標檔案的預期 SHA-256，完成備份與 staging 後才寫入 journal；分檔替換後標記提交完成。任一失敗會嘗試復原；遇到其他人的新修改時保留現況與備份，要求處理衝突。

SceneEditSession 的離線能力只允許 Unity 2021.3／2022.3 穩定版已知 standalone GameObject／Transform scalar 欄位；2022.3 辨識 Transform serializedVersion 2，未知版本維持唯讀。runtime-only PACKAGE_EDITOR_MISMATCH／URP_EDITOR_MISMATCH 診斷保留，未知 schema、Prefab 關聯、GUID／清單損壞及其他 blocker 仍拒絕寫入。嚴格 CLI gate 維持不變。Web fileID 一律使用十進位字串，避免 JavaScript 精度遺失。

啟動後可掃描未完成的 journal。重新存檔前必須先處理待復原紀錄。復原前核對所有目標與備份的雜湊，不覆蓋無法確認來源的修改。

目前提交器只修改既有檔案。新增／刪除／搬移 Unity 資源及其 `.meta` 的跨檔案交易，須在後續資源編輯階段擴充與驗證。


最新明確授權的直接專案存取以 DirectProjectAccess 代表，和 WorkCopy 共享 IProjectWriteAccess／SaveTransaction。只有開啟專案時明確選擇直接模式才提供此範圍；未指定 mode 的既有 API 與嚴格 CLI 仍沿用既有來源保護。開啟本身不寫入，存檔僅備份變更檔案；原始 D: 範本不作寫入測試。版本準備及兩模式交驗見 HANDOFF_BASELINE_R8.zh-TW.md。

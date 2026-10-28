# Scene Transform r10-fix1 交驗

修正獨立驗收發現的 `WEB-TRANSFORM-R10-001 / P2`：示範場景聚焦 fileID 301 後，點選另一個可見方塊會出現 Raycaster `undefined.side` 例外，無法切換選取。原始案例及 Console 在 `artifacts/acceptance/transform-r10-20261027/WEB-TRANSFORM-R10-001.json`、`picking-reproduction.json`，沒有判定首次引入版本。

原因是預覽建立的 Three BoxGeometry 含六個面材質 group，但原材質陣列只有一個診斷材質，部分面缺少 material。這也使部分面未繪製。現在將內建 primitive 的預覽面分組正常化；真正解碼的子網格仍保留原 group 邊界與 source slot。每個被引用但缺失的槽位補上明示診斷材質，不把已知的來源材質猜配給其他子網格，不吞掉點選例外。group index 需為 0–1023 的整數。無分組模型使用第一槽，其他原生參照保留並提示額外 pass 未預覽；更新未使用的額外材質不會覆蓋第一槽。

這次只改 `preview-bindings.js`、`scene-view.js` 及相關測試／文件。原 TransformTools 與後端 C#／8 個 binary 不變，沿用仍有效的已驗收移動／旋轉／原生儲存、父層與窄版證據；修正部分另做瀏覽器複驗。完整 r10 原功能範圍及尚缺的 Esc 時序證據見前輪交驗與獨立報告。

主工程檢查：

- 用實際 pinned Three Raycaster 重現原 `side` 例外；修正後六方向點選、保留兩個明確 source material／缺槽診斷、無來源材質、非法分組拒用與單材質真實 group 範圍共四組通過。結果 `artifacts/transform-r10/picking-integration-final.json`。
- 補上 group 範圍案例時，第一次簡化材質選擇使局部 submesh 成為完整表面，測試確實失敗（6 != 2），日誌 `picking-groups-pre-fix.log` 留存；已保留 grouped material array 修正，原斷言未放寬。較早三組結果保留，不冒稱它涵蓋四組。
- Fresh in-app browser、隔離 5418、既有 owned QA fixture：Hierarchy 301 → focus → 實際點擊左側方塊切到 401 → 點另一方塊切到 201。Console 空、dirty false、未存檔，`.unity` 保持原 SHA `f590d9fb9999a021002bc7683e46b1ce588c7023a83a6702a7c84cefa26b8900`。原始畫面／記錄在 `picking-fixed.png`、`picking-browser-fix1.json` 與 `browser/picking-fixed-native-unchanged.json`。
- 此次 QA 初始化期間觀察到共用 IAB 焦點的額外輸入與表單未送出，未歸為產品缺陷或成功證據。與驗收官暫時序列化原生輸入後，使用受保護的 owned fixture project/scene API 初始化，再 fresh reload 完成真正點選測試。原生輸入權已交還驗收官，root 自有 5418 與暫存分頁已停止。
- JS syntax 通過，strict UI audit 0 findings（`ui-audit-fix1.json`）。沒有重跑未變更的整套後端測試。

請驗收官從新的固定來源獨立啟動，複測原 1178×1000 點選案例及缺材質幾何的各面；檢查來源多子網格材質不被合併，必要的材質更新、拖曳／history 與 60001 唯讀畫面回歸。其餘有效 r10 案例按相同來源及環境適用性沿用。

Scene 60001 的缺失 TerrainLayer 保留 FAILED；原始水面 scoped VERIFIED 不擴大為完整 StylizedWater2／URP／Unity 一致性。完整原計畫仍在。使用者 5406 保留，QA 使用自有埠及 fixture；不操作 5286、不啟動 Unity、不打包、不寫原始 D 專案。

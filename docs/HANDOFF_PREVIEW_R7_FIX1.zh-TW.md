# 材質 Integer 精度修正交驗

本版直接回應驗收官 `WEB-MAT-001／P1`：Integer／m_Ints 的 `16777217` 曾經 Float32 靜默成為 `16777216`。原始缺陷及 source2 的 FAILED 結果完整保留於 `artifacts/acceptance/preview-r7-20261022`，不改寫成通過。本輪只修正這個數值通道及必要相鄰行為，不打包、不啟動 Unity。

## 修正

- 真正的 `m_Ints`：JSON `GetInt32` → `int` 命令與 Undo → `UnityYamlDocument.SetInteger` → boxed `int` Snapshot。輸入、歷史、原生 `.mat` 和重新讀取全程不經 Float32。
- Float／Range／Color／Vector 保持原 Float32 路徑。混合 Snapshot 的 JSON 外形仍是 `value: [number]`，實際整數使用 Int32 序列化。新增 `integerStorage` 說明原生存放型別。
- ShaderLab `Integer` 優先對應 `m_Ints`。舊 ShaderLab `Int` 存於 `m_Floats` 時，UI／API 會明確拒絕不能精確表示的整數，避免另一條靜默捨入路徑；Inspector 標示 Legacy Int。
- 原生 Integer shader 預設值、GLSL uniform 型別與 Inspector 檢查一併區分。整批驗證、非法值保留、GUID／Shader／meta 保護及儲存 transaction 維持原規則。

## 證據

- `preview-r7-fix1-consolidated.trx`：最終整合 **118/118**，原始 log 同名保存在 `artifacts/logs`。
- `preview-r7-fix1-affected.trx`：30/30，含四個 Int32 原生存檔／重讀／Undo／Redo 精確 bytes 案例及 Float／Color／Vector 鄰近保護。
- `preview-r7-fix1-fresh-int.trx`：1/1，實際獨立 HTTP host 逐個讀回 `±16777217`、`Int32.MinValue/MaxValue`；API 拒絕分數、溢位、NaN／Infinity 字串，以及不能精確表示的 Legacy Int。
- 真 Chrome `artifacts/preview-r7/integer-fix1/result.json`：10 組通過。四個有效邊界各自 Apply／Save／reload／Undo／Redo／原始 bytes 復原，五個非法輸入保留草稿，混合 Float／Color／Vector 保存與附加原生物件復原。無 pageerror／Console 錯誤。測試只寫入已建立的隔離 QA 工作複本。
- `premium-audit-fix1.json` strict 0 findings；JS syntax 通過。

固定來源、DLL 識別與 96 份未變動原檔比對在 `artifacts/preview-r7/handoff-fix1`，普通來源副本 `artifacts/preview-r7/source-snapshot-fix1`。source2 的 112 份來源和 182 份證據不覆寫；變更清單應與 source2 比較，舊版未失效的 37 組局部行為可按適用性重用。新影響限於材質數值的解析、原生讀寫、Snapshot、Inspector 與 shader uniform；請獨立複驗 WEB-MAT-001 及必要鄰近行為。

修正版 source-manifest 共 113 檔，SHA-256 `55df0c78fe8d038f5d430c9a8a1766c79b32c501780a741f809c77ab5448ad43`。`source-delta.json` 列出相對 source2 的 11 份變更；Core DLL 未變，Web DLL `624d8ca18bf30df3a6b4c349b320dae49a1458fbb26d42caa1e6b4b85bffb75b`、Unity 格式層 `ab9ba76bb95ea5820f122f3d8c91c08ae2048bcdbb664499a2eab531387732bd`。

模型／貼圖／ASE 預覽的原交驗規劃、資料及限制仍見 `docs/HANDOFF_PREVIEW_R7.zh-TW.md` 和原核准計畫。9 種既有材質／5 種預設材質的統計不變。完整 Unity 載入、雙向往返與畫面相似度仍 UNVERIFIED，BL-001～007 仍 Blocking，不以本修正版關閉。

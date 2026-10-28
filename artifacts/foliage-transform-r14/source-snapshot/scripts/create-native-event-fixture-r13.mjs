// Isolated source fixture with UA-native interruption controls. Never edits product sources or Unity assets.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const root = path.resolve(import.meta.dirname, '..'), base = path.join(root, 'artifacts/tree-input-r13');
const destination = path.join(base, 'native-event-fixture');
assert.equal(await fs.stat(destination).then(() => true, () => false), false);
const hash = data => crypto.createHash('sha256').update(data).digest('hex');
const source = JSON.parse(await fs.readFile(path.join(root, 'artifacts/prefab-cancel-r12/handoff/source-manifest.json')));
const docs = JSON.parse(await fs.readFile(path.join(root, 'artifacts/prefab-cancel-r12/post-review-docs.json')));
const docsHashes = new Map(docs.files.map(f => [f.path, f.afterSha256]));
await fs.mkdir(destination);
const copied = [];
for (const f of source.files) {
  const content = await fs.readFile(path.join(root, f.path));
  assert.equal(hash(content), docsHashes.get(f.path) ?? f.sha256, f.path);
  const target = path.join(destination, f.path); await fs.mkdir(path.dirname(target), { recursive: true }); await fs.writeFile(target, content, { flag: 'wx' });
  copied.push({ path: f.path, originalSha256: hash(content), fixtureSha256: hash(content) });
}
const htmlPath = path.join(destination, 'src/SceneWorkbench.Web/wwwroot/index.html');
let html = await fs.readFile(htmlPath, 'utf8');
assert.equal(html.split('<main class="workspace"').length, 2);
html = html.replace('<span class="badge">本機開發版</span>', '<span class="badge">QA 原生中斷 fixture</span>')
  .replace('<div class="editor-shell">', '<div class="editor-shell" style="grid-template-rows:35px auto auto auto minmax(300px,1fr) 26px 27px 23px">')
  .replace('<script type="module" src="/ui/app.js"></script>', '<script type="module" src="/ui/app.js"></script>\n  <script type="module" src="/qa-native-events.js"></script>')
  .replace('    <div class="compatibility"', '    <nav class="toolbar" aria-label="僅供驗證的瀏覽器原生中斷"><strong>僅供 QA</strong><label><input id="qa-native-drag" type="checkbox">讓瀏覽器接管拖曳（pointercancel）</label><button type="button" id="qa-native-modal">開啟瀏覽器原生模態（僅 QA）</button><a id="qa-native-tab" href="/qa-focus-target.html" target="_blank" rel="noopener">原生新分頁（僅 QA）</a><span id="qa-mode-note" role="status">正式程式模組未修改；僅驗證瀏覽器原生事件。</span></nav>\n    <div class="compatibility"');
await fs.writeFile(htmlPath, html);
copied.find(f => f.path === 'src/SceneWorkbench.Web/wwwroot/index.html').fixtureSha256 = hash(Buffer.from(html));
const trigger = `// QA fixture only. Native browser behavior, never synthetic input or a controller cancel call.
const viewport = document.getElementById('viewport'), observations = [];
function observe(event) {
  observations.push({ type: event.type, trusted: event.isTrusted, at: performance.now(), eventAt: event.timeStamp,
    pointerId: event.pointerId ?? null, pointerType: event.pointerType ?? null, buttons: event.buttons ?? null,
    target: event.target?.id ?? event.target?.tagName ?? null, hidden: document.hidden, visibility: document.visibilityState,
    dragging: viewport.dataset.transformDragging, changed: viewport.dataset.transformDragChanged,
    gesture: viewport.dataset.transformGesture ?? null });
  if (observations.length > 32) observations.shift();
  viewport.dataset.qaNativeEvents = JSON.stringify(observations);
}
for (const type of ['pointercancel','lostpointercapture','dragstart','dragend','blur','focus']) window.addEventListener(type,observe,true);
document.addEventListener('visibilitychange',observe,true);
document.getElementById('qa-native-drag').addEventListener('change', event => {
  viewport.draggable = event.target.checked;
  document.getElementById('qa-mode-note').textContent = event.target.checked ? 'QA：原生 HTML 拖放模式，觀察真正 pointercancel。' : 'QA：正常拖曳模式。';
});
document.getElementById('qa-native-modal').addEventListener('click', () => {
  // The only native dialog is an explicit test stimulus, never shipped in product UI.
  alert('僅供 QA：瀏覽器原生模態中斷；關閉後檢查控制器還原、鏡頭與歷史。');
});
`;
const script = path.join(destination, 'src/SceneWorkbench.Web/wwwroot/qa-native-events.js'); await fs.writeFile(script, trigger, { flag: 'wx' });
const focusPath = 'src/SceneWorkbench.Web/wwwroot/qa-focus-target.html';
const focusHtml = '<!doctype html><html lang="zh-Hant"><meta charset="utf-8"><title>R13 QA 原生焦點目標</title><h1>R13 QA 原生焦點目標</h1><p>僅供隔離驗證。無編輯器 API 或程式呼叫。</p></html>';
await fs.writeFile(path.join(destination, focusPath), focusHtml, { flag: 'wx' });
const report = { time: new Date().toISOString(), fixtureRoot: destination,
  scope: 'Isolated explicit QA environment. The original181 source files are copied byte-for-byte except fixture index HTML; all production JS/C# modules remain identical. Extra controls provoke UA-native interruptions rather than dispatching synthetic events or calling cancellation.',
  productSourceEdited: false, nativeAssetEdited: false, copied, added: { path: path.relative(destination, script), sha256: hash(Buffer.from(trigger)) },
  additionalFiles: [{ path: focusPath, sha256: hash(Buffer.from(focusHtml)) }],
  excludes: 'This fixture is not product UI or evidence of normal window-switch workflows; classify actual observed native events separately.',
  unityStarted: false, packaging: false };
await fs.writeFile(path.join(base, 'native-event-fixture-manifest.json'), JSON.stringify(report, null, 2) + '\n', { flag: 'wx' });
console.log(JSON.stringify({ fixtureRoot: destination, copied: copied.length, productionModulesChanged: false }));

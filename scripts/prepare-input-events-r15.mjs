// Isolated QA fixture. Production modules and Unity files are never edited.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const root = path.resolve(import.meta.dirname, '..');
const out = path.join(root, 'artifacts/input-events-r15');
const parent = path.join(root, 'artifacts/foliage-transform-r14');
const fixture = path.join(out, 'fixture');
const hash = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
assert.equal(await fs.stat(fixture).then(() => true, () => false), false, 'new fixture only');
await fs.mkdir(out, { recursive: true });
const manifestBytes = await fs.readFile(path.join(parent, 'handoff/source-manifest.json'));
const manifest = JSON.parse(manifestBytes), files = [];
for (const f of manifest.files) {
  const bytes = await fs.readFile(path.join(parent, 'source-snapshot', f.path));
  assert.equal(hash(bytes), f.sha256, f.path);
  const dest = path.join(fixture, f.path);
  await fs.mkdir(path.dirname(dest), { recursive: true });
  await fs.writeFile(dest, bytes, { flag: 'wx' });
  files.push({ path: f.path, bytes: bytes.length, parentSha256: f.sha256, sha256: f.sha256 });
}
const index = 'src/SceneWorkbench.Web/wwwroot/index.html';
let html = await fs.readFile(path.join(fixture, index), 'utf8');
assert.equal(html.split('<div class="editor-shell">').length, 2);
html = html.replace('<span class="badge">本機開發版</span>', '<span class="badge">R15 隔離事件驗證</span>')
  .replace('<div class="editor-shell">', '<div class="editor-shell" style="grid-template-rows:35px auto auto auto minmax(300px,1fr) 26px 27px 23px">')
  .replace('<script type="module" src="/ui/app.js"></script>', '<script type="module" src="/qa-input-events.js"></script>\n  <script type="module" src="/ui/app.js"></script>')
  .replace('    <div class="compatibility"', '    <nav class="toolbar" aria-label="僅供 QA 的原生事件"><strong>僅供 QA</strong><label><input id="qa-native-drag" type="checkbox">瀏覽器原生拖放接管</label><button id="qa-focus-window" type="button">開啟焦點視窗（F8）</button><span id="qa-observer-status" role="status">被動觀察 pointerup；未呼叫控制器取消。</span></nav>\n    <div class="compatibility"');
await fs.writeFile(path.join(fixture, index), html);
Object.assign(files.find(f => f.path === index), { sha256: hash(html), bytes: Buffer.byteLength(html) });
const script = `// Explicit QA fixture only; no synthetic input, controller calls, event cancellation or app-state changes.
const viewport = document.getElementById('viewport');
const events = [], counts = {}, capacity = 512;
let sequence = 0, dropped = 0, lastDown = 0;
function observe(event) {
  if (event.type === 'pointermove' && !event.buttons) return;
  const seq = ++sequence;
  if (event.type === 'pointerdown') lastDown = seq;
  counts[event.type] = (counts[event.type] ?? 0) + 1;
  events.push({ seq, lastDown, type: event.type, trusted: event.isTrusted,
    at: performance.now(), eventAt: event.timeStamp, pointerId: event.pointerId ?? null,
    pointerType: event.pointerType ?? null, button: event.button ?? null, buttons: event.buttons ?? null,
    target: event.target === window ? 'window' : event.target === document ? 'document' : event.target?.id || event.target?.tagName || null,
    hidden: document.hidden, visibility: document.visibilityState, focused: document.hasFocus(),
    dragging: viewport.dataset.transformDragging ?? null, changed: viewport.dataset.transformDragChanged ?? null,
    gesture: viewport.dataset.transformGesture ?? null });
  if (events.length > capacity) { events.shift(); dropped++; }
  viewport.dataset.qaInputEvents = JSON.stringify({ version: 2, sequence, dropped, counts, events });
}
// Window capture observes pointerup regardless of the controller's gesturePointerId.
for (const type of ['pointerdown', 'pointermove', 'pointerup', 'pointercancel', 'gotpointercapture',
  'lostpointercapture', 'dragstart', 'dragend', 'drop', 'blur', 'focus', 'keydown', 'keyup']) {
  window.addEventListener(type, observe, { capture: true, passive: true });
}
document.addEventListener('visibilitychange', observe, { capture: true, passive: true });
viewport.dataset.qaInputEvents = JSON.stringify({ version: 2, sequence, dropped, counts, events });
document.getElementById('qa-native-drag').addEventListener('change', event => {
  viewport.draggable = event.target.checked;
  document.getElementById('qa-observer-status').textContent = event.target.checked
    ? 'QA 原生 HTML 拖放：完整記錄取消、放開與新按下事件。' : 'QA 一般操作：完整記錄原生事件。';
});
function openFocusWindow(event) {
  if (!event.isTrusted || event.repeat || event.isComposing) return;
  // User-initiated browser window, an explicit scoped stimulus; never an OS Alt+Tab claim.
  window.open('/qa-focus-target.html', 'r15-qa-focus-window', 'popup,width=460,height=300');
}
document.getElementById('qa-focus-window').addEventListener('click', openFocusWindow);
window.addEventListener('keydown', event => { if (event.code === 'F8') openFocusWindow(event); });
`;
const focus = '<!doctype html><html lang="zh-Hant"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>R15 QA 焦點視窗</title><h1>R15 QA 焦點視窗</h1><p>隔離測試目標。沒有編輯器 API、輸入合成或取消呼叫。</p><button type="button">焦點測試</button></html>';
for (const [relative, content] of [['qa-input-events.js', script], ['qa-focus-target.html', focus]]) {
  const p = 'src/SceneWorkbench.Web/wwwroot/' + relative;
  await fs.writeFile(path.join(fixture, p), content, { flag: 'wx' });
  files.push({ path: p, bytes: Buffer.byteLength(content), parentSha256: null, sha256: hash(content) });
}
const record = { time: new Date().toISOString(), parentManifestSha256: hash(manifestBytes),
  parentSourceFiles: manifest.files.length, productionModulesChanged: 0, fixtureRoot: fixture,
  observer: { independentPointerup: true, capacity: 512, lostEventsExposed: true, syntheticInput: false, controllerCalls: false },
  scope: 'Only fixture index differs; 2 explicit QA files added. Popup is scoped browser stimulus, not proof of ordinary OS window switching. Native HTML drag does not imply a later pointerup exists.', files };
await fs.writeFile(path.join(out, 'fixture-manifest.json'), JSON.stringify(record, null, 2) + '\n', { flag: 'wx' });
await fs.writeFile(path.join(out, 'infrastructure.json'), JSON.stringify({ time: new Date().toISOString(),
  nativeWindowRuntime: { tool: 'mcp__node_repl__js / @oai/sky', outcome: 'initialization failed before executing input',
    error: 'failed to write kernel assets: 系統找不到指定的路徑。 (os error 3)', attemptsThisTurn: 1, fallbackOsInputUsed: false },
  userSessions: [5406, 5408], unityStarted: false, packaging: false }, null, 2) + '\n', { flag: 'wx' });
console.log(JSON.stringify({ fixture, parentFiles: manifest.files.length, fixtureFiles: files.length, productionModulesChanged: 0 }));

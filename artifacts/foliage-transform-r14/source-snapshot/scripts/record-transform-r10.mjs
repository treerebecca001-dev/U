// Read-only API/native-file evidence for real browser interactions on an owned QA service.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const root = path.resolve(import.meta.dirname, '..'), label = process.argv[2];
assert.match(label ?? '', /^[a-z0-9-]+$/);
const base = process.env.WORKBENCH_QA_URL ?? 'http://127.0.0.1:5416';
assert.equal(new URL(base).hostname, '127.0.0.1'); assert.notEqual(new URL(base).port, '5286');
const out = path.join(root, 'artifacts/transform-r10/browser'); await fs.mkdir(out, { recursive: true });
const target = path.join(out, label + '.json'); assert.equal(await fs.stat(target).then(() => true, () => false), false);
const html = await (await fetch(base)).text(), token = html.match(/name="workbench-token" content="([a-f0-9]+)"/)[1];
const response = await fetch(base + '/api/state', { headers: { 'X-Workbench-Token': token } }); assert.equal(response.status, 200);
const state = await response.json(), project = path.resolve(state.projectPath), qa = path.join(root, 'Workspace/QA');
assert.ok(project.startsWith(qa + path.sep)); assert.equal(state.scenePath, 'Assets/WorkbenchDemo.unity');
const scene = await fs.readFile(path.join(project, state.scenePath)), original = await fs.readFile(path.join(root, 'DemoProject', state.scenePath));
const hash = value => crypto.createHash('sha256').update(value).digest('hex');
const files = ['ui/app.js', 'ui/scene-view.js', 'ui/transform-tools.js', 'ui/preview-bindings.js', 'ui/editor.css', 'index.html'];
const served = [];
for (const file of files) {
  const source = await fs.readFile(path.join(root, 'src/SceneWorkbench.Web/wwwroot', file));
  if (file === 'index.html') { assert.equal(html.replace(token, '__WORKBENCH_TOKEN__'), source.toString('utf8')); }
  else { const r = await fetch(base + '/' + file); assert.equal(r.status, 200); assert.equal(hash(Buffer.from(await r.arrayBuffer())), hash(source)); }
  served.push({ path: file, sha256: hash(source) });
}
const record = { label, time: new Date().toISOString(), base, project, scenePath: state.scenePath, revision: state.revision,
  dirty: state.dirty, undoCount: state.undoCount, redoCount: state.redoCount, item: state.items.find(item => item.id === '301'),
  nativeSha256: hash(scene), originalDemoSha256: hash(original), served };
await fs.writeFile(target, JSON.stringify(record, null, 2) + '\n', { flag: 'wx' });
console.log(JSON.stringify({ label, dirty: record.dirty, undo: record.undoCount, redo: record.redoCount,
  position: record.item.position, rotation: record.item.rotation, scale: record.item.scale, nativeSha256: record.nativeSha256 }));

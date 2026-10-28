// Owned QA service only. Normal application APIs prepare a fresh demo working copy
// or read the real Client scene. No scene edit/save or Unity invocation is issued.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const root = path.resolve(import.meta.dirname, '..');
const mode = process.argv[2]; assert.ok(['demo', 'client', 'capture-demo', 'capture-client'].includes(mode));
const base = 'http://127.0.0.1:5432', out = path.join(root, 'artifacts/prefab-cancel-r12');
const html = await (await fetch(base)).text(), token = html.match(/name="workbench-token" content="([a-f0-9]+)"/)[1];
const hash = data => crypto.createHash('sha256').update(data).digest('hex');
async function api(route, body) {
  const response = await fetch(base + route, { method: body ? 'POST' : 'GET', headers: { 'X-Workbench-Token': token, Origin: base,
    ...(body ? { 'Content-Type': 'application/json' } : {}) }, ...(body ? { body: JSON.stringify(body) } : {}) });
  const result = await response.json(); assert.equal(response.status, 200, JSON.stringify(result)); return result;
}
let state = await api('/api/state');
if (mode === 'demo') {
  const old = JSON.parse(await fs.readFile(path.join(root, 'artifacts/acceptance/terrain-recovery-r11-20261027/inputs-after.json')));
  const pins = new Map(Object.entries(old.targetPins).map(([relative, sha]) => [path.join(old.target, relative), sha]));
  for (const relative of ['Assets/Tourist/TouristAssets/woman/body.prefab', 'Assets/Tourist/TouristWoman.prefab', 'Assets/02.Arts/Scene/Map/Map_60001/Model/Prefab/60001_Wall_P.prefab'])
    for (const suffix of ['', '.meta']) { const absolute = path.join(old.target, relative + suffix); pins.set(absolute, hash(await fs.readFile(absolute))); }
  for (const [absolute, expected] of pins) assert.equal(hash(await fs.readFile(absolute)), expected, absolute);
  const before = [...pins].map(([absolute, sha256]) => ({ absolute, sha256 }));
  await fs.writeFile(path.join(out, 'native-before.json'), JSON.stringify({ time: new Date().toISOString(), files: before }, null, 2) + '\n', { flag: 'wx' });
  const destination = path.join(root, 'Workspace/QA/PrefabCancel-r12-20261027');
  assert.equal(await fs.stat(destination).then(() => true, () => false), false);
  state = await api('/api/project/open', { revision: state.revision, path: path.join(root, 'DemoProject'), mode: 'copy', destination });
  state = await api('/api/scene/open', { revision: state.revision, path: 'Assets/WorkbenchDemo.unity' });
  await fs.writeFile(path.join(out, 'demo-prepared.json'), JSON.stringify(state, null, 2) + '\n', { flag: 'wx' });
  console.log(JSON.stringify({ mode, projectPath: state.projectPath, dirty: state.dirty, items: state.items.length, pins: before.length }));
} else if (mode === 'client') {
  assert.equal(state.dirty, false);
  state = await api('/api/project/open', { revision: state.revision, path: path.join(root, 'Workspace/Baselines/Client231229-2022.3.13f1'), mode: 'inspect' });
  state = await api('/api/scene/open', { revision: state.revision, path: 'Assets/02.Arts/Scene/Map/Map_60001/Map_60001.unity' });
  const graph = await api('/api/preview?path=' + encodeURIComponent(state.scenePath));
  await fs.writeFile(path.join(out, 'client-state.json'), JSON.stringify(state) + '\n', { flag: 'wx' });
  await fs.writeFile(path.join(out, 'client-graph.json'), JSON.stringify(graph) + '\n', { flag: 'wx' });
  console.log(JSON.stringify({ mode, projectPath: state.projectPath, dirty: state.dirty, nodes: graph.nodes.length,
    diagnostics: graph.diagnostics, overrideDetails: graph.prefabOverrides.length,
    implicitMappings: graph.prefabOverrides.filter(item => item.method === 'validated-nested-xor63').length,
    unresolvedTargets: graph.prefabOverrides.filter(item => item.status === 'unresolved-target'),
    unusedRemovals: graph.prefabOverrides.filter(item => item.status === 'unused-removal') }));
} else if (mode === 'capture-client') {
  assert.equal(path.resolve(state.projectPath), path.join(root, 'Workspace/Baselines/Client231229-2022.3.13f1'));
  const label = process.argv[3]; assert.match(label ?? '', /^[a-z0-9-]+$/);
  const before = JSON.parse(await fs.readFile(path.join(out, 'native-before.json')));
  for (const file of before.files) assert.equal(hash(await fs.readFile(file.absolute)), file.sha256, file.absolute);
  const record = { label, time: new Date().toISOString(), state, nativeFilesUnchanged: before.files.length,
    nativeSceneSha256: hash(await fs.readFile(path.join(state.projectPath, state.scenePath))) };
  if (process.argv.includes('--graph')) record.graph = await api('/api/preview?path=' + encodeURIComponent(state.scenePath));
  await fs.writeFile(path.join(out, 'client-' + label + '.json'), JSON.stringify(record) + '\n', { flag: 'wx' });
  console.log(JSON.stringify({ label, revision: state.revision, dirty: state.dirty, undo: state.undoCount, redo: state.redoCount,
    item: state.items.find(item => item.id === '1114381152'), nativeFilesUnchanged: before.files.length, nativeSceneSha256: record.nativeSceneSha256 }));
} else {
  assert.equal(path.resolve(state.projectPath), path.join(root, 'Workspace/QA/PrefabCancel-r12-20261027'));
  const label = process.argv[3]; assert.match(label ?? '', /^[a-z0-9-]+$/);
  const native = await fs.readFile(path.join(state.projectPath, state.scenePath));
  const original = await fs.readFile(path.join(root, 'DemoProject', state.scenePath));
  const served = await fs.readFile(path.join(root, 'src/SceneWorkbench.Web/wwwroot/ui/transform-tools.js'));
  assert.equal(hash(Buffer.from(await (await fetch(base + '/ui/transform-tools.js')).arrayBuffer())), hash(served));
  const record = { label, time: new Date().toISOString(), state, nativeSha256: hash(native), originalDemoSha256: hash(original), controllerSha256: hash(served) };
  await fs.writeFile(path.join(out, 'demo-' + label + '.json'), JSON.stringify(record, null, 2) + '\n', { flag: 'wx' });
  console.log(JSON.stringify({ label, revision: state.revision, dirty: state.dirty, undo: state.undoCount, redo: state.redoCount, item: state.items.find(i => i.id === '301'), nativeSha256: record.nativeSha256 }));
}

// Test-only API preparation and immutable state snapshots; never save native data.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const root = path.resolve(import.meta.dirname, '..'), out = path.join(root, 'artifacts/tree-input-r13');
const base = 'http://127.0.0.1:5438', label = process.argv[2];
assert.match(label ?? '', /^[a-z0-9-]+$/);
const hash = b => crypto.createHash('sha256').update(b).digest('hex');
const native = JSON.parse(await fs.readFile(path.join(out, 'native-before.json')));
for (const f of native.files) assert.equal(hash(await fs.readFile(f.absolute)), f.sha256, f.absolute);
const htmlResponse = await fetch(base); assert.equal(htmlResponse.status, 200);
const html = await htmlResponse.text(), token = html.match(/name="workbench-token" content="([a-f0-9]+)"/)[1];
assert.ok(html.includes('qa-native-events.js'));
const fixture = JSON.parse(await fs.readFile(path.join(out, 'native-event-fixture-manifest.json')));
const served = [];
for (const relative of ['qa-native-events.js', 'ui/transform-tools.js', 'ui/asset-preview.js']) {
  const response = await fetch(base + '/' + relative); assert.equal(response.status, 200, relative);
  const bytes = Buffer.from(await response.arrayBuffer());
  const expected = await fs.readFile(path.join(fixture.fixtureRoot, 'src/SceneWorkbench.Web/wwwroot', relative));
  assert.equal(hash(bytes), hash(expected), relative);
  served.push({ path: relative, sha256: hash(bytes) });
}
async function api(route, body) {
  const r = await fetch(base + route, { method: body ? 'POST' : 'GET',
    headers: { 'X-Workbench-Token': token, Origin: base, ...(body ? { 'Content-Type': 'application/json' } : {}) },
    ...(body ? { body: JSON.stringify(body) } : {}), signal: AbortSignal.timeout(120000) });
  assert.equal(r.status, 200, route); return r.json();
}
let state = await api('/api/state');
if (process.argv.includes('--prepare')) {
  assert.equal(state.projectPath, null);
  state = await api('/api/project/open', { revision: state.revision,
    path: path.join(root, 'Workspace/Baselines/Client231229-2022.3.13f1'), mode: 'inspect' });
  state = await api('/api/scene/open', { revision: state.revision, path: 'Assets/02.Arts/Scene/Map/Map_60001/Map_60001.unity' });
}
const record = { time: new Date().toISOString(), base, state, nativeFilesUnchanged: native.files.length, served };
if (process.argv.includes('--graph')) record.graph = await api('/api/preview?path=' + encodeURIComponent(state.scenePath));
await fs.writeFile(path.join(out, 'fixture-api-' + label + '.json'), JSON.stringify(record) + '\n', { flag: 'wx' });
console.log(JSON.stringify({ label, revision: state.revision, dirty: state.dirty, undo: state.undoCount,
  redo: state.redoCount, terrain: state.items.find(i => i.id === '1114381152')?.position, nativeFilesUnchanged: native.files.length, served }));

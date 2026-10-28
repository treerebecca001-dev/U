// Owned QA service only. Normal APIs prepare/read60001; no save or native edits.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const root = path.resolve(import.meta.dirname, '..'), out = path.join(root, 'artifacts/tree-input-r13');
const base = 'http://127.0.0.1:5434', mode = process.argv[2];
assert.ok(['prepare', 'capture'].includes(mode));
const hash = data => crypto.createHash('sha256').update(data).digest('hex');
const original = JSON.parse(await fs.readFile(path.join(root, 'artifacts/prefab-cancel-r12/native-before.json')));
for (const f of original.files) assert.equal(hash(await fs.readFile(f.absolute)), f.sha256, f.absolute);
const html = await (await fetch(base)).text(), token = html.match(/name="workbench-token" content="([a-f0-9]+)"/)[1];
async function api(route, body) {
  const r = await fetch(base + route, { method: body ? 'POST' : 'GET',
    headers: { 'X-Workbench-Token': token, Origin: base, ...(body ? { 'Content-Type': 'application/json' } : {}) },
    ...(body ? { body: JSON.stringify(body) } : {}), signal: AbortSignal.timeout(120000) });
  assert.equal(r.status, 200, route); return r.json();
}
let state = await api('/api/state');
if (mode === 'prepare') {
  assert.equal(state.projectPath, null);
  await fs.writeFile(path.join(out, 'native-before.json'), JSON.stringify(original, null, 2) + '\n', { flag: 'wx' });
  state = await api('/api/project/open', { revision: state.revision, path: path.join(root, 'Workspace/Baselines/Client231229-2022.3.13f1'), mode: 'inspect' });
  console.log('Owned QA project opened; opening60001.');
  state = await api('/api/scene/open', { revision: state.revision, path: 'Assets/02.Arts/Scene/Map/Map_60001/Map_60001.unity' });
}
const label = mode === 'prepare' ? 'before' : process.argv[3]; assert.match(label ?? '', /^[a-z0-9-]+$/);
const record = { time: new Date().toISOString(), base, state, nativeFilesUnchanged: original.files.length };
if (mode === 'prepare' || process.argv.includes('--graph')) record.graph = await api('/api/preview?path=' + encodeURIComponent(state.scenePath));
await fs.writeFile(path.join(out, 'api-' + label + '.json'), JSON.stringify(record) + '\n', { flag: 'wx' });
console.log(JSON.stringify({ label, revision: state.revision, dirty: state.dirty, undo: state.undoCount, redo: state.redoCount,
  terrain: state.items.find(i => i.id === '1114381152')?.position, nativeFilesUnchanged: original.files.length }));

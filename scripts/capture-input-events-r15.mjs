// Read-only API/evidence capture except --prepare on a new, isolated QA service.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const root = path.resolve(import.meta.dirname, '..'), out = path.join(root, 'artifacts/input-events-r15');
const args = process.argv.slice(2), option = (key, fallback) => args.includes(key) ? args[args.indexOf(key) + 1] : fallback;
const label = option('--label', 'before'), port = Number(option('--port', 5450));
assert.match(label, /^[a-z0-9-]+$/); assert.ok(port >= 1024 && port <= 65535);
await fs.mkdir(out, { recursive: true });
const hash = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
const base = 'http://127.0.0.1:' + port, html = await (await fetch(base)).text();
const token = html.match(/name="workbench-token" content="([a-f0-9]+)"/)[1];
async function api(route, body) {
  const r = await fetch(base + route, { method: body ? 'POST' : 'GET', headers: { 'X-Workbench-Token': token, Origin: base,
    ...body ? { 'Content-Type': 'application/json' } : {} }, ...body ? { body: JSON.stringify(body) } : {}, signal: AbortSignal.timeout(body ? 600000 : 120000) });
  assert.equal(r.status, 200, route); return r.json();
}
let state = await api('/api/state');
if (args.includes('--prepare')) {
  assert.equal(state.projectPath, null, 'prepare only fresh QA instance');
  assert.ok([5449, 5450].includes(port), 'never prepare user sessions');
  state = await api('/api/project/open', { revision: state.revision, path: path.join(root, 'Workspace/Baselines/Client231229-2022.3.13f1'), mode: 'inspect' });
  state = await api('/api/scene/open', { revision: state.revision, path: 'Assets/02.Arts/Scene/Map/Map_60001/Map_60001.unity' });
}
const graph = state.scenePath ? await api('/api/preview?path=' + encodeURIComponent(state.scenePath)) : null;
const pins = JSON.parse(await fs.readFile(path.join(root, 'artifacts/foliage-transform-r14/handoff/native-input-manifest.json'))).files;
for (const f of pins) assert.equal(hash(await fs.readFile(f.absolute)), f.sha256, f.absolute);
const result = { time: new Date().toISOString(), base, state, graph, graphSha256: hash(JSON.stringify(graph)), nativeUnchanged: pins.length };
await fs.writeFile(path.join(out, label + '.json'), JSON.stringify(result), { flag: 'wx' });
console.log(JSON.stringify({ label, port, revision: state.revision, dirty: state.dirty, undo: state.undoCount, redo: state.redoCount,
  graphNodes: graph?.nodes?.length ?? 0, graphSha256: result.graphSha256, nativeUnchanged: pins.length }));

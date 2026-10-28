import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const root = path.resolve(import.meta.dirname, '..'), out = path.join(root, 'artifacts/transform-r10');
const read = async name => JSON.parse(await fs.readFile(path.join(out, name), 'utf8'));
const hash = async file => crypto.createHash('sha256').update(await fs.readFile(file)).digest('hex');
const records = {};
for (const name of ['rotation-drag', 'rotation-saved-reloaded', 'rotation-snap-30', 'scale-snap-025',
  'move-snap-world', 'all-transforms-saved-reloaded', 'move-local', 'local-undo-redo-view', 'native-restored']) records[name] = await read('browser/' + name + '.json');
const checks = [], check = (name, fn) => { fn(); checks.push({ name, status: 'PASS' }); };
const a = records['rotation-drag'], b = records['rotation-saved-reloaded'], c = records['rotation-snap-30'],
  d = records['scale-snap-025'], e = records['move-snap-world'], f = records['all-transforms-saved-reloaded'],
  g = records['move-local'], h = records['local-undo-redo-view'], restored = records['native-restored'];
check('Actual rotation drag is one native quaternion command with no disk write', () => {
  assert.equal(a.undoCount, 1); assert.equal(a.dirty, true); assert.deepEqual(a.item.position, [2, .5, 1]);
  assert.deepEqual(a.item.scale, [1, 1, 1]); assert.notDeepEqual(a.item.rotation, [0, 0, 0, 1]); assert.equal(a.nativeSha256, a.originalDemoSha256);
});
check('Rotation native save/reload preserves the actual drag result', () => {
  assert.deepEqual(b.item.rotation, a.item.rotation); assert.equal(b.dirty, false); assert.equal(b.undoCount, 0); assert.notEqual(a.nativeSha256, b.nativeSha256);
});
check('30-degree rotation snap changes the orientation by a measured multiple of 30 degrees', () => {
  const q = c.item.rotation, p = b.item.rotation, dot = Math.abs(q.reduce((n, v, i) => n + v * p[i], 0) / (Math.hypot(...q) * Math.hypot(...p)));
  const angle = 2 * Math.acos(Math.min(1, dot)) * 180 / Math.PI;
  assert.ok(angle > 1); assert.ok(Math.abs(angle / 30 - Math.round(angle / 30)) < 1e-5); assert.equal(c.undoCount, 1);
});
check('0.25 scale and 0.5 world translation drags produce separate commands and preserve other fields', () => {
  assert.deepEqual(d.item.scale, [2.25, 1, 1]); assert.deepEqual(d.item.rotation, c.item.rotation); assert.deepEqual(d.item.position, c.item.position); assert.equal(d.undoCount, 2);
  assert.deepEqual(e.item.position, [2.5, .5, 1]); assert.deepEqual(e.item.scale, d.item.scale); assert.deepEqual(e.item.rotation, d.item.rotation); assert.equal(e.undoCount, 3);
  assert.equal(e.nativeSha256, b.nativeSha256);
});
check('All three transforms survive native save and scene reload', () => {
  for (const key of ['position', 'rotation', 'scale']) assert.deepEqual(f.item[key], e.item[key]); assert.equal(f.dirty, false); assert.equal(f.undoCount, 0);
});
check('Local movement follows the rotated X basis; Undo/Redo and view-tool switch preserve the result', () => {
  const [,, z, w] = f.item.rotation, basis = [1 - 2 * z * z, 2 * z * w];
  const delta = g.item.position.map((v, i) => v - f.item.position[i]);
  assert.ok(Math.hypot(...delta) > .1); assert.ok(Math.abs(delta[0] * basis[1] - delta[1] * basis[0]) < 1e-6); assert.equal(delta[2], 0);
  assert.deepEqual(h.item.position, g.item.position); assert.equal(h.undoCount, 1); assert.equal(h.redoCount, 0); assert.equal(h.nativeSha256, f.nativeSha256);
});
check('Normal Inspector restore/save recovers the exact original scene bytes', () => {
  assert.equal(restored.dirty, false); assert.equal(restored.nativeSha256, restored.originalDemoSha256);
  assert.deepEqual(restored.item.position, [2, .5, 1]); assert.deepEqual(restored.item.rotation, [0, 0, 0, 1]); assert.deepEqual(restored.item.scale, [1, 1, 1]);
});
const ui = await read('browser/cua-observation.json');
check('Fresh browser page has no captured console errors and contains the viewport in 1280px layout', () => {
  assert.deepEqual(ui.console, []); assert.equal(ui.ui.documentWidth, ui.ui.viewportWidth); assert.equal(ui.ui.viewport.transformEditable, 'true'); assert.equal(ui.ui.dirty, '✓ 已儲存');
});
const baseline = JSON.parse(await fs.readFile(path.join(root, 'artifacts/preview-r9/handoff-fix2/source-manifest.json'), 'utf8'));
let backendSourceFiles = 0;
for (const file of baseline.files.filter(f => /\.(cs|csproj|props|slnx)$/.test(f.path))) {
  assert.equal(await hash(path.join(root, file.path)), file.sha256, file.path); backendSourceFiles++;
}
const runtime = JSON.parse(await fs.readFile(path.join(root, 'artifacts/preview-r9/handoff-fix2/runtime.json'), 'utf8'));
for (const file of runtime.binaries) assert.equal(await hash(path.join(root, file.path)), file.sha256, file.path);
const watches = await read('client-watch-before.json'), after = {};
for (const key of Object.keys(watches)) after[key] = await hash(path.join(root, 'Workspace/Baselines/Client231229-2022.3.13f1', key));
assert.deepEqual(after, watches);
await fs.writeFile(path.join(out, 'client-watch-after.json'), JSON.stringify(after, null, 2) + '\n', { flag: 'wx' });
const report = { status: 'VERIFIED_SCOPED', time: new Date().toISOString(), checks,
  backend: { reused: true, unchangedSourceFiles: backendSourceFiles, unchangedBinaries: runtime.binaries.length, uniqueApplicableTests: 177,
    evidence: 'artifacts/preview-r9/handoff-fix2/test-summary.json', sha256: await hash(path.join(root, 'artifacts/preview-r9/handoff-fix2/test-summary.json')) },
  nativeWatches: Object.keys(watches).length, limitations: ['Independent Esc/cancellation, three-axis/parent edge cases and 390px retest requested separately', 'TerrainLayer source missing; whole60001 FAILED', 'Unity-native engine interoperability and full original plan UNVERIFIED'] };
await fs.writeFile(path.join(out, 'verification.json'), JSON.stringify(report, null, 2) + '\n', { flag: 'wx' });
console.log(JSON.stringify({ status: report.status, checks: checks.length, backendSourceFiles, reusedBackendTests: 177, nativeWatches: report.nativeWatches }));

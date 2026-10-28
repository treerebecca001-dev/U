// Verifies already captured, real CUA browser observations. Does not generate input.
import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
const root = path.resolve(import.meta.dirname, '..');
const base = path.join(root, 'artifacts/prefab-cancel-r12');
const read = async name => JSON.parse(await fs.readFile(path.join(base, name)));
const trace = record => JSON.parse(record.data.transformGesture);
const camera = record => JSON.parse(record.data.previewCamera);
const verified = [];
for (const name of ['native-move-escape-fixed.json', 'native-rotate-escape-fixed.json',
  'native-scale-escape-fixed.json', 'client-native-escape-paired.json']) {
  const record = await read(name), mid = trace(record.mid), entries = trace(record.after);
  assert.equal(record.mid.data.transformDragging, 'true');
  assert.equal(record.mid.data.transformDragChanged, 'true');
  assert.ok(mid.some(e => e.phase === 'preview' && e.active && e.changed && e.event.trusted && e.event.buttons === 1));
  assert.deepEqual(entries.map(e => e.phase), ['begin', 'preview', 'cancel-request:escape', 'cancelled:escape', 'pointerup']);
  const [begin, preview, request, cancelled, up] = entries;
  assert.equal(begin.event.type, 'pointerdown'); assert.equal(begin.event.buttons, 1);
  assert.equal(preview.event.type, 'pointermove'); assert.equal(preview.event.buttons, 1);
  assert.equal(request.event.type, 'keydown'); assert.equal(request.event.key, 'Escape');
  assert.ok(request.active && request.changed); assert.ok(!cancelled.active && !cancelled.changed);
  assert.equal(up.event.type, 'pointerup'); assert.equal(up.event.buttons, 0);
  assert.deepEqual(cancelled.value, begin.before); assert.deepEqual(up.value, begin.before);
  assert.ok(entries.every(e => e.event.trusted === true));
  assert.ok(entries.every((e, i) => !i || e.at >= entries[i - 1].at));
  assert.ok(request.event.at > preview.event.at && up.event.at > request.event.at);
  assert.equal(record.after.data.transformDragging, 'false');
  assert.equal(record.after.data.transformDragChanged, 'false');
  assert.deepEqual(camera(record.before), camera(record.mid));
  assert.deepEqual(camera(record.before), camera(record.after));
  verified.push({ name, id: begin.id, field: begin.field, changedPreview: preview.value,
    restoredValue: up.value, cancelToReleaseMilliseconds: up.event.at - request.event.at,
    trustedEvents: true, cameraUnchanged: true, commits: 0 });
}
const regression = await read('native-escape-camera-before-fix.json');
assert.notDeepEqual(regression.beforeCamera, camera(regression.after));
assert.ok(trace(regression.after).some(e => e.phase === 'cancelled:escape'));
const before = await read('client-paired-before.json'), after = await read('client-paired-after.json');
assert.deepEqual(before.state, after.state);
assert.equal(before.nativeSceneSha256, after.nativeSceneSha256);
assert.equal(after.nativeFilesUnchanged, 41);
assert.equal(after.state.dirty, false); assert.equal(after.state.undoCount, 0); assert.equal(after.state.redoCount, 0);
assert.deepEqual(after.graph, await read('client-graph.json'));
const demo = await read('demo-three-escapes-fixed.json');
assert.equal(demo.state.dirty, false); assert.equal(demo.state.undoCount, 0); assert.equal(demo.state.redoCount, 0);
assert.equal(demo.nativeSha256, demo.originalDemoSha256);
const unverified = [];
for (const [file, apiFile] of [['native-focus-loss.json', 'demo-focus-attempt-completed.json'],
  ['native-browser-hide.json', 'demo-visibility-attempt-completed.json'],
  ['client-address-focus-attempt.json', 'client-focus-attempt-completed.json']]) {
  const record = await read(file), entries = trace(record.after), api = await read(apiFile);
  assert.ok(entries.some(e => e.phase === 'commit'));
  assert.ok(!entries.some(e => e.phase.startsWith('cancel-request:')));
  assert.equal(api.state.undoCount, 1); assert.equal(api.state.dirty, true);
  unverified.push({ file, apiFile, status: 'UNVERIFIED_EVENT_GENERATION',
    reason: 'Documented UI action generated no observable blur/visibility/pointercancel; normal drag committed and was undone/reloaded without saving.' });
}
const output = { status: 'VERIFIED_SCOPED_NATIVE_ESCAPE', time: new Date().toISOString(), verified,
  client: { revisionBefore: before.state.revision, revisionAfter: after.state.revision, dirty: after.state.dirty,
    undo: after.state.undoCount, redo: after.state.redoCount, nativeFilesUnchanged: after.nativeFilesUnchanged,
    nativeSceneSha256: after.nativeSceneSha256, graphUnchanged: true },
  actualCameraRegression: { evidence: 'native-escape-camera-before-fix.json', source: 'pre-camera-fix/manifest.json', reproducedBeforeFix: true },
  unverified, pointercancel: 'UNIT_ONLY_NO_NATIVE_EVENT',
  note: 'Native Escape is proven after a changed live drag and before release; blur/visibility/pointercancel are not promoted from unit tests.',
  independentReview: 'PENDING', unityStarted: false, packaging: false };
await fs.writeFile(path.join(base, 'native-cancel-verification.json'), JSON.stringify(output, null, 2) + '\n', { flag: 'wx' });
console.log(JSON.stringify({ status: output.status, nativeEscapes: verified.length,
  clientRevision: after.state.revision, nativeFilesUnchanged: after.nativeFilesUnchanged, unverified: unverified.length }));

// Verify raw observations, not claims. Does not control a browser or synthesize events.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const root = path.resolve(import.meta.dirname, '..'), out = path.join(root, 'artifacts/input-events-r15');
const read = async file => JSON.parse(await fs.readFile(path.join(out, file)));
const hash = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
const same = (a, b, label) => assert.equal(hash(JSON.stringify(a)), hash(JSON.stringify(b)), label);
const fixture = await read('fixture-manifest.json');
let productFiles = 0;
for (const f of fixture.files) {
  const bytes = await fs.readFile(path.join(fixture.fixtureRoot, f.path));
  assert.equal(bytes.length, f.bytes, f.path + ' size');
  assert.equal(hash(bytes), f.sha256, f.path + ' hash');
  if (f.path.startsWith('src/') && f.path !== 'src/SceneWorkbench.Web/wwwroot/index.html' && f.parentSha256) {
    assert.equal(f.sha256, f.parentSha256, f.path + ' unchanged product'); productFiles++;
  }
}
const runtime = JSON.parse(await fs.readFile(path.join(root, 'artifacts/foliage-transform-r14/handoff/runtime.json')));
for (const f of runtime.binaries) assert.equal(hash(await fs.readFile(path.join(root, 'artifacts/foliage-transform-r14', f.path))), f.sha256, f.path);
const native = JSON.parse(await fs.readFile(path.join(root, 'artifacts/foliage-transform-r14/handoff/native-input-manifest.json')));
for (const f of native.files) assert.equal(hash(await fs.readFile(f.absolute)), f.sha256, f.absolute);
const before = await read('qa-ready.json'), cancelled = await read('qa-after-pointercancel.json');
same(before.state, cancelled.state, 'complete state after pointercancel');
same(before.graph, cancelled.graph, 'complete graph after pointercancel');
const raw = await read('native-pointercancel-arrow.json'), trace = JSON.parse(raw.after.data.transformGesture);
assert.deepEqual(trace.map(e => e.phase), ['begin', 'preview', 'cancel-request:pointercancel', 'cancelled:pointercancel']);
assert.ok(trace.every(e => e.event.trusted)); assert.ok(trace[1].active && trace[1].changed);
assert.ok(trace[2].active && trace[2].changed); assert.ok(!trace[3].active && !trace[3].changed);
same(trace[3].value, trace[0].before, 'native transform restored');
assert.equal(raw.before.data.previewCamera, raw.after.data.previewCamera, 'camera after cancellation');
const observed = JSON.parse(raw.after.data.qaInputEvents);
assert.equal(observed.version, 2); assert.equal(observed.dropped, 0);
const cancelEvent = observed.events.find(e => e.type === 'pointercancel' && e.dragging === 'true' && e.changed === 'true');
assert.ok(cancelEvent?.trusted); assert.ok(observed.events.some(e => e.type === 'pointerup' && e.trusted && e.seq < cancelEvent.seq));
const end = observed.events.find(e => e.type === 'dragend' && e.seq > cancelEvent.seq);
assert.ok(end?.trusted); assert.equal(end.dragging, 'false'); assert.equal(end.changed, 'false');
const popup = await read('popup-mid-gesture.json'), popupTrace = JSON.parse(popup.after.data.transformGesture);
assert.equal(popup.mid.data.transformDragging, 'true'); assert.equal(popup.mid.data.transformDragChanged, 'true');
assert.ok(popup.drag.ok);
const commits = popupTrace.filter(e => e.phase === 'commit'); assert.equal(commits.length, 1);
assert.ok(commits[0].event.trusted); assert.equal(commits[0].event.type, 'pointerup');
const all = JSON.parse(popup.after.data.qaInputEvents); assert.equal(all.dropped, 0);
const nextDown = all.events.find(e => e.type === 'pointerdown' && e.seq > cancelEvent.seq);
assert.ok(nextDown && nextDown.seq > end.seq);
const afterCancelBeforeNextDown = all.events.filter(e => e.seq > cancelEvent.seq && e.seq < nextDown.seq);
assert.equal(afterCancelBeforeNextDown.filter(e => e.type === 'pointerup' && e.pointerId === cancelEvent.pointerId).length, 0);
const laterUp = all.events.find(e => e.type === 'pointerup' && e.seq > nextDown.seq && e.pointerId === cancelEvent.pointerId);
assert.ok(laterUp?.trusted); assert.equal(laterUp.lastDown, nextDown.seq); assert.notEqual(laterUp.lastDown, cancelEvent.lastDown);
const from = popupTrace[0].at, to = commits[0].at;
const during = all.events.filter(e => e.at >= from && e.at <= to);
assert.ok(during.some(e => e.type === 'keydown' && e.trusted && e.dragging === 'true' && e.changed === 'true'));
assert.equal(during.filter(e => e.type === 'blur' && e.target === 'window').length, 0);
assert.equal(during.filter(e => e.type === 'visibilitychange' && e.hidden).length, 0);
const afterFocus = await read('qa-after-focus-attempt.json'), undo = await read('qa-after-undo.json'), reload = await read('qa-after-reload.json');
assert.equal(afterFocus.state.revision, before.state.revision + 1);
assert.equal(afterFocus.state.dirty, true); assert.equal(afterFocus.state.undoCount, 1);
const expected = structuredClone(before.graph), target = expected.nodes.find(n => n.id === commits[0].id);
const changed = afterFocus.graph.nodes.find(n => n.id === commits[0].id);
for (let i = 0; i < 3; i++) assert.ok(Math.abs(changed.position[i] - commits[0].value[i]) < 1e-5);
target.position = changed.position;
same(expected, afterFocus.graph, 'only one position command');
same(undo.graph, before.graph, 'Undo complete graph'); same(reload.graph, before.graph, 'reload complete graph');
same({ ...undo.state, revision: 0, redoCount: 0 }, { ...before.state, revision: 0 }, 'Undo complete native state');
same({ ...reload.state, revision: 0 }, { ...before.state, revision: 0 }, 'reload complete native state');
assert.equal(reload.state.undoCount, 0); assert.equal(reload.state.redoCount, 0); assert.equal(reload.state.dirty, false);
const logs = await read('browser-warnings-errors.json');
const report = { time: new Date().toISOString(), status: 'UNVERIFIED',
  scopedPass: 'Trusted changed HTML pointercancel restores complete state/graph/camera; independent pointerup observer active and untruncated.',
  productFilesUnchanged: productFiles, fixtureFiles: fixture.files.length, runtimeFiles: runtime.binaries.length, nativePinsUnchanged: native.files.length,
  pointercancel: { pointerId: cancelEvent.pointerId, downSeq: cancelEvent.lastDown, cancelSeq: cancelEvent.seq,
    preview: trace[1].value, restored: trace[3].value, commits: 0, dragendAfterCancelMs: end.at - cancelEvent.at,
    observedPointerupsUntilNextDown: 0, nextDownSeq: nextDown.seq,
    laterUpSeq: laterUp.seq, laterUpBelongsToNewGesture: true, latePointerupAcceptance: 'UNVERIFIED_NO_SUCH_NATIVE_EVENT_IN_THIS_CAPTURE' },
  focusAttempt: { scopedPopup: true, changedDrag: true, trustedKeyDuringChangedDrag: true, windowBlur: 0, hiddenVisibilitychange: 0,
    outcome: 'UNVERIFIED_NO_NATIVE_BLUR_OR_HIDDEN', normalCommitCount: 1, completeUndoReloadRestored: true },
  browserWarningErrors: logs.length, browserLogClassification: 'Two Chrome connection errors preserved; no unsupported attribution to application or extension.',
  reusedChecks: ['R14 191/191 backend; unchanged production/runtime identity', 'R12 8/8 controller unit cases include simulated blur/hidden/cancel followed by release; not native acceptance'],
  remaining: ['Ordinary OS window blur while changed dragging', 'Native hidden visibilitychange while changed dragging', 'Actual late trusted pointerup from the cancelled gesture'],
  unityStarted: false, packaging: false, nativeSavePerformed: false };
await fs.writeFile(path.join(out, 'verification.json'), JSON.stringify(report, null, 2) + '\n', { flag: 'wx' });
console.log(JSON.stringify({ status: report.status, productFiles, fixtureFiles: fixture.files.length, nativePins: native.files.length,
  pointercancelSeq: cancelEvent.seq, sameGestureLateUp: false, newerGestureUp: laterUp.seq, ordinaryBlurHidden: 'UNVERIFIED' }));

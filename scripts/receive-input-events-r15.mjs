// Verify independent artifact/raw review; never promotes it to independent browser execution.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const root = path.resolve(import.meta.dirname, '..'), out = path.join(root, 'artifacts/input-events-r15');
const incoming = path.join(root, 'artifacts/acceptance/input-events-r15-20261028');
const hash = b => crypto.createHash('sha256').update(b).digest('hex');
const read = async file => JSON.parse(await fs.readFile(path.join(incoming, file)));
const same = (a, b, message) => assert.equal(hash(JSON.stringify(a)), hash(JSON.stringify(b)), message);
const expected = {
  'evidence-manifest.json': '8ea3dac1b7619ca86390035841590b2ba206ef2c9e323539e06fbbddbc2f7273',
  'review-summary.json': 'be75322b3bb6845c4ef73ab667a0db434ab1c961beb0cb0c6fb9c4f6caa0bd80',
  'VERDICT.zh-TW.md': '424919badc59d6595fa90cd4886cb53363c66d48942efa5ecc2bb665e03fb7f8'
};
for (const [file, pin] of Object.entries(expected)) assert.equal(hash(await fs.readFile(path.join(incoming, file))), pin, file);
const manifest = await read('evidence-manifest.json'); assert.equal(manifest.files.length, 288);
for (const f of manifest.files) {
  const absolute = path.resolve(incoming, f.path); assert.ok(absolute.startsWith(incoming + path.sep));
  const bytes = await fs.readFile(absolute); assert.equal(bytes.length, f.bytes, f.path + ' size'); assert.equal(hash(bytes), f.sha256, f.path + ' hash');
}
const summary = await read('review-summary.json'); assert.equal(summary.status, 'UNVERIFIED');
for (const [file, pin] of Object.entries(summary.artifact)) assert.equal(hash(await fs.readFile(path.join(out, 'handoff', file))), pin, file + ' submission');
assert.equal(summary.independentNativeUi.executions, 0); assert.equal(summary.independentNativeUi.screenshotsCaptured, 0);
assert.equal(summary.independentNativeUi.status, 'UNVERIFIED'); assert.equal(summary.independentNativeUi.blocker, 'QA-R15-INFRA-001');
assert.equal(summary.independentNativeUi.browserConsoleLogs.count, null);
assert.equal(summary.submittedRawReview.independentExecution, false);
const raw = JSON.parse(await fs.readFile(path.join(out, 'native-pointercancel-arrow.json')));
const trace = JSON.parse(raw.after.data.transformGesture), observation = JSON.parse(raw.after.data.qaInputEvents);
const cancelled = trace.find(e => e.phase === 'cancelled:pointercancel'); assert.ok(cancelled?.event.trusted);
assert.equal(trace.filter(e => e.phase === 'commit').length, 0);
same(cancelled.value, trace[0].before, 'raw restored transform');
assert.equal(raw.before.data.previewCamera, raw.after.data.previewCamera);
assert.equal(observation.dropped, 0);
const pointer = summary.submittedRawReview.pointercancel;
assert.equal(pointer.downSeq, 31); assert.equal(pointer.cancelSeq, 37); assert.equal(pointer.commits, 0);
assert.equal(pointer.observedUpsBeforeNextDown, 0);
assert.equal(pointer.nextDown.seq, 40); assert.equal(pointer.nextDown.target, 'qa-native-drag');
assert.equal(pointer.laterNewGestureUp.seq, 43); assert.equal(pointer.laterNewGestureUp.lastDown, 40);
const end = observation.events.find(e => e.type === 'dragend' && e.seq > pointer.cancelSeq);
assert.ok(Math.abs(end.at - cancelled.at - pointer.lateDragendProcessedMs) < .02);
const baseline = JSON.parse(await fs.readFile(path.join(out, 'qa-ready.json')));
for (const file of ['qa-after-pointercancel.json', 'qa-after-undo.json', 'qa-after-reload.json']) {
  const current = JSON.parse(await fs.readFile(path.join(out, file)));
  same(baseline.graph, current.graph, file + ' complete graph');
  same({ ...baseline.state, revision: 0 }, { ...current.state, revision: 0, redoCount: 0 }, file + ' native state');
}
const focus = summary.submittedRawReview.focus;
assert.ok(focus.changedBeforeStimulus && focus.trustedKeyDuringChangedGesture);
assert.equal(focus.windowBlur, 0); assert.equal(focus.hiddenVisibilitychange, 0); assert.equal(focus.normalCommitCount, 1);
assert.equal(summary.submittedRawReview.browserLogs.count, 2);
assert.equal(summary.submittedRawReview.missedGizmo.acceptedAsCancellation, false);
const before = await read('inputs-before.json'), after = await read('inputs-after.json'); let protectedPins = 0;
for (const key of ['targetPins', 'sourcePins']) {
  same(before[key], after[key], key + ' before/after');
  for (const [absolute, pin] of Object.entries(before[key])) { assert.equal(hash(await fs.readFile(absolute)), pin, absolute); protectedPins++; }
}
assert.equal(protectedPins, 185);
const service = await read('service.json'); assert.equal(service.servedAssets.length, 37);
assert.ok(service.servedAssets.every(f => f.matches));
assert.equal(summary.independentStaticChecks.ownedServiceProjectOpened, false);
assert.equal(summary.independentStaticChecks.ownedServiceInitialRevision, 0);
assert.equal(summary.cleanup.ownedHost.stopped, true); assert.equal(summary.cleanup.ownedHost.listenerRemaining, false);
assert.equal(summary.cleanup.cuaReturnedToImplementation, true);
const image = await read('image-metadata.json'); assert.equal(image.actualFormat, 'JPEG'); assert.equal(image.independentScreenshots, 0);
const jpg = await fs.readFile(path.join(out, 'restored-browser.jpg'));
assert.equal(hash(jpg), hash(await fs.readFile(path.join(out, 'restored-browser.png'))), 'correct suffix copy byte identity');
assert.equal(jpg[0], 255); assert.equal(jpg[1], 216);
const receipt = { time: new Date().toISOString(), status: 'UNVERIFIED',
  scope: 'Independent source/served/raw-evidence review verified; independent native UI execution remains blocked.',
  incoming: path.relative(root, incoming).replaceAll('\\', '/'), reviewPins: expected, evidenceFiles: manifest.files.length,
  independentNativeUiExecutions: 0, producerActualNativePointercancel: true, lateSameGesturePointerup: 'UNVERIFIED',
  nativeWindowBlur: 'UNVERIFIED', nativeHidden: 'UNVERIFIED', blocker: summary.independentNativeUi,
  protectedPinsUnchanged: protectedPins, servedAssetsVerified: service.servedAssets.length,
  normalOneCommandUndoReloadRawReviewed: true, image: { correctedFile: 'restored-browser.jpg', originalFrozenFilePreserved: true, byteIdentical: true },
  cleanup: summary.cleanup, previousAcceptedScopesRetained: true, remaining: summary.remaining,
  unityStarted: false, packaged: false, nativeSavePerformed: false };
await fs.writeFile(path.join(out, 'review-receipt.json'), JSON.stringify(receipt, null, 2) + '\n', { flag: 'wx' });
console.log(JSON.stringify({ status: receipt.status, evidenceFiles: manifest.files.length, protectedPins,
  served: service.servedAssets.length, independentUiExecutions: 0, infrastructureBlocker: 'QA-R15-INFRA-001' }));

// Consolidated constrained result; the remaining native and independent UI checks stay open.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const root = path.resolve(import.meta.dirname, '..'), out = path.join(root, 'artifacts/input-events-r15');
const hash = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
const read = async file => JSON.parse(await fs.readFile(path.join(out, file)));
const sessions = [];
for (const port of [5406, 5408]) {
  const before = await read('user-' + port + '-before.json'), after = await read('user-' + port + '-receipt-final.json');
  assert.equal(hash(JSON.stringify(before.state)), hash(JSON.stringify(after.state)), port + ' complete state');
  assert.equal(before.graphSha256, after.graphSha256, port + ' complete graph');
  sessions.push({ port, revision: after.state.revision, dirty: after.state.dirty,
    undo: after.state.undoCount, redo: after.state.redoCount, fullStateAndGraphPreserved: true });
}
const receipt = await read('review-receipt.json');
assert.equal(receipt.status, 'UNVERIFIED'); assert.equal(receipt.independentNativeUiExecutions, 0);
for (const port of [5449, 5450, 5451]) {
  const alive = await fetch('http://127.0.0.1:' + port, { signal: AbortSignal.timeout(2000) }).then(() => true, () => false);
  assert.equal(alive, false, 'owned QA service closed: ' + port);
}
const supplemental = ['README.md', 'docs/IMPLEMENTATION.md', 'docs/VALIDATION_R15.zh-TW.md',
  'scripts/diagnose-input-infra-r15.py', 'scripts/receive-input-events-r15.mjs', 'scripts/finalize-input-events-r15.mjs',
  'artifacts/input-events-r15/evidence-reuse-after-freeze.json', 'artifacts/input-events-r15/runtime-diagnosis.json',
  'artifacts/input-events-r15/runtime-diagnosis-final.json', 'artifacts/input-events-r15/cleanup-after-freeze.json',
  'artifacts/input-events-r15/screenshot-extension-correction.json', 'artifacts/input-events-r15/restored-browser.jpg',
  'artifacts/input-events-r15/review-receipt.json', 'artifacts/input-events-r15/user-5406-receipt-final.json',
  'artifacts/input-events-r15/user-5408-receipt-final.json'];
const pins = [];
for (const file of supplemental) {
  const bytes = await fs.readFile(path.join(root, file)); pins.push({ path: file, bytes: bytes.length, sha256: hash(bytes) });
}
const report = { time: new Date().toISOString(), status: 'UNVERIFIED',
  delivery: 'Existing R14 development service retained; this round adds an isolated QA observer and verified raw-evidence review.',
  applicationProductionModulesChanged: 0, originalHandoffUnchanged: true,
  userSessions: sessions, ownedQaPortsClosed: [5449, 5450, 5451],
  independentReview: { evidenceFiles: receipt.evidenceFiles, protectedPins: receipt.protectedPinsUnchanged,
    servedAssets: receipt.servedAssetsVerified, nativeUiExecutions: 0, blocker: 'QA-R15-INFRA-001', sourceAndRawReviewCompleted: true },
  remaining: receipt.remaining, supplementalPins: pins, nativeSavePerformed: false, unityStarted: false, packaged: false };
await fs.writeFile(path.join(out, 'closing.json'), JSON.stringify(report, null, 2) + '\n', { flag: 'wx' });
console.log(JSON.stringify({ status: report.status, independentUi: 0, evidenceFiles: receipt.evidenceFiles,
  userSessionsPreserved: sessions, closedPorts: report.ownedQaPortsClosed, supplementalPins: pins.length }));

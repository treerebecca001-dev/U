// Consolidate raw evidence. Bounded comparisons avoid dumping large scene graphs on failure.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const root = path.resolve(import.meta.dirname, '..'), out = path.join(root, 'artifacts/tree-input-r13');
const read = async name => JSON.parse(await fs.readFile(path.join(out, name)));
const hash = b => crypto.createHash('sha256').update(b).digest('hex');
const canonical = value => JSON.stringify(value); // JSON numeric -0 only becomes 0; fileID strings remain exact.
const equal = (a, b, label) => assert.equal(hash(canonical(a)), hash(canonical(b)), label);
const before = await read('fixture-api-before-ready.json');
const cancelled = await read('fixture-api-after-native-pointercancel.json');
const blurred = await read('fixture-api-after-native-modal.json');
for (const [label, record] of [['pointercancel',cancelled],['fixture recovery blur',blurred]]) {
  equal(record.state, before.state, label + ' complete state');
  equal(record.graph, before.graph, label + ' complete graph');
}
const pointer = await read('fixture-native-drag-attempt.json');
const pointerTrace = JSON.parse(pointer.after.data.transformGesture);
const request = pointerTrace.find(e => e.phase === 'cancel-request:pointercancel');
const restored = pointerTrace.find(e => e.phase === 'cancelled:pointercancel');
assert.ok(request?.active && request.changed && request.event.trusted && request.event.pointerId === 1);
assert.equal(request.event.type, 'pointercancel');
assert.ok(restored && !restored.active && !restored.changed); equal(restored.value, request.before, 'native cancellation restored value');
assert.equal(pointerTrace.some(e => e.phase === 'commit'), false);
assert.equal(pointer.before.data.previewCamera, pointer.after.data.previewCamera);
const ua = JSON.parse(pointer.after.data.qaNativeEvents);
const dragEnd = ua.find(e => e.type === 'dragend' && e.at > restored.at);
assert.ok(dragEnd?.trusted && dragEnd.buttons === 0 && dragEnd.dragging === 'false');
const modal = await read('fixture-native-modal-attempt.json');
const blurTrace = JSON.parse(modal.after.data.transformGesture);
const blurRequest = blurTrace.find(e => e.phase === 'cancel-request:blur');
const blurRestored = blurTrace.find(e => e.phase === 'cancelled:blur');
assert.ok(blurRequest?.active && blurRequest.changed && blurRequest.event.trusted);
assert.equal(blurTrace.some(e => e.phase === 'commit'), false);
equal(blurRestored.value, blurRequest.before, 'fixture recovery blur restored value');
const modalBefore = await read('fixture-dom-before-modal.json');
assert.equal(modalBefore.data.previewCamera, modal.after.data.previewCamera);
const windowBlur = JSON.parse(modal.after.data.qaNativeEvents).find(e => e.type === 'blur' && e.target === null && e.eventAt === blurRequest.event.at);
assert.ok(windowBlur?.trusted, 'actual window blur, not element blur');
const anchor = await read('fixture-native-anchor-attempt.json');
assert.equal(anchor.mid.data.transformDragging, 'true'); assert.equal(anchor.mid.data.transformDragChanged, 'true');
assert.equal(anchor.after.hidden, false); assert.equal(anchor.after.visibility, 'visible');
const finalBrowser = await read('fixture-final-browser.json');
assert.equal(finalBrowser.browserLogs.length, 0); assert.equal(finalBrowser.dom.data.modelCount, '3105');
assert.ok(finalBrowser.dom.console.includes('5565035281593725506'));
assert.ok(finalBrowser.dom.console.includes('5094106267881264939'));
assert.ok(finalBrowser.dom.fields.find(f => f.id === 'apply-inspector')?.disabled);
const drag = await read('fixture-api-normal-drag-after-blur.json'), undo = await read('fixture-api-after-one-undo.json');
const final = await read('fixture-api-restored-final.json');
assert.equal(drag.state.undoCount, 1); assert.equal(drag.state.redoCount, 0); assert.equal(drag.state.dirty, true);
const expectedDrag = structuredClone(before.state), terrain = expectedDrag.items.find(i=>i.id === '1114381152');
terrain.position[1] = 88.38609; expectedDrag.revision = 3; expectedDrag.dirty = true; expectedDrag.undoCount = 1;
equal(drag.state, expectedDrag, 'one normal command only');
const terrainIndex = before.graph.nodes.findIndex(n=>n.id === '1114381152');
assert.equal(terrainIndex, 71); const expectedGraph = structuredClone(before.graph);
expectedGraph.nodes[terrainIndex].position[1] = 88.38609; equal(drag.graph, expectedGraph, 'only Terrain node Y changed');
const expectedUndo = structuredClone(before.state); expectedUndo.revision = 4; expectedUndo.redoCount = 1;
equal(undo.state, expectedUndo, 'one undo restores full state'); equal(undo.graph, before.graph, 'one undo restores full graph');
const expectedFinal = structuredClone(before.state); expectedFinal.revision = 5;
equal(final.state, expectedFinal, 'final clean state'); equal(final.graph, before.graph, 'final complete graph');
const native = await read('native-before.json');
for (const f of native.files) assert.equal(hash(await fs.readFile(f.absolute)), f.sha256, f.absolute);
const tree = await read('tree-target-audit.json'), geometry = await read('tree-geometry-analysis.json');
for (const f of tree.files) assert.equal(hash(await fs.readFile(f.absolute)), f.sha256, f.absolute);
assert.equal(tree.files.length, 144); assert.equal(tree.scene.owner, '630463173');
assert.ok(geometry.pairs.every(p => !p.sourceFileEqual && !p.sourcePositionBufferEqual && !p.sourceUvBufferEqual));
const source = JSON.parse(await fs.readFile(path.join(root, 'artifacts/prefab-cancel-r12/handoff/source-manifest.json')));
const docs = JSON.parse(await fs.readFile(path.join(root, 'artifacts/prefab-cancel-r12/post-review-docs.json')));
const docHashes = new Map(docs.files.map(f=>[f.path,f.afterSha256]));
for (const f of source.files) assert.equal(hash(await fs.readFile(path.join(root,f.path))), docHashes.get(f.path) ?? f.sha256, f.path);
const runtime = JSON.parse(await fs.readFile(path.join(root, 'artifacts/prefab-cancel-r12/handoff/runtime.json')));
for (const f of runtime.binaries) assert.equal(hash(await fs.readFile(path.join(root,f.path))), f.sha256, f.path);
const fixture = await read('native-event-fixture-manifest.json');
for (const f of fixture.copied) assert.equal(hash(await fs.readFile(path.join(fixture.fixtureRoot,f.path))), f.fixtureSha256, f.path);
for (const f of [fixture.added,...fixture.additionalFiles]) assert.equal(hash(await fs.readFile(path.join(fixture.fixtureRoot,f.path))), f.sha256, f.path);
const mainResponse = await fetch('http://127.0.0.1:5406/'); assert.equal(mainResponse.status, 200);
const report = { time: new Date().toISOString(), status: 'UNVERIFIED', independentReview: 'PENDING',
  scope: 'No product source change. New offline Tree evidence and native event fixture observations; full workflow acceptance remains open.',
  pointercancel: { status: 'VERIFIED_FIXTURE_CONDITION', trusted: true, pointerId: 1,
    phases: pointerTrace.map(e=>e.phase), changedBeforeCancel: true, restored: true, cameraUnchanged: true,
    completeStateAndGraphUnchanged: true, nativeDragEndAfterRestoreMs: dragEnd.at-restored.at,
    commits: 0, latePointerupObserved: false, caveat: 'Native HTML drag suppresses mouse pointerup; actual dragend/release produced no commit. General OS/device/touch cancellation not claimed.' },
  blur: { status: 'OBSERVED_FIXTURE_RECOVERY_ONLY', trustedWindowEvent: true, changedBeforeCancel: true,
    restored: true, cameraUnchanged: true, completeStateAndGraphUnchanged: true, commits: 0,
    previewToHandledCancelMs: blurRequest.at-blurTrace.find(e=>e.phase==='preview').at,
    caveat: 'Native alert interrupted drag tool; CUA kernel reset and focus-emulation timeout preceded recovered window blur. Ordinary OS window switching and late pointerup remain UNVERIFIED.' },
  visibility: { status: 'UNVERIFIED', reason: 'Chrome tabs.new and IAB target=_blank did not make the editor hidden during a changed drag. Later IAB window blur occurred after commit, so is excluded from cancellation acceptance.' },
  normalDragAfterInterruption: { status: 'VERIFIED', commands: 1, onlyGraphChange: 'nodes[71].position[1]', oneUndoRestoresCompleteGraph: true, finalRevision: 5, finalDirty: false, finalUndo: 0, finalRedo: 0 },
  tree: { status: 'UNVERIFIED_CURRENT_INTENT', verifiedFacts: 'Old leaves2 FBX remains present; current Prefab references different GUIDs/models with different decoded source position/UV buffers. Original x=-0.2/z=0.87 preserved, not remapped.', boundedFilesUnchanged: tree.files.length },
  body: { status: 'UNVERIFIED_UNITY_CLASSIFICATION', evidenceReuse: 'R12 complete125-object source absence and safe preservation already accepted; no new native classification evidence.' },
  productSourceFilesUnchanged: source.files.length, runtimeBinariesUnchanged: runtime.binaries.length,
  nativeFilesUnchanged: native.files.length, browserWarningsOrErrors: 0, main5406Http: 200,
  infrastructure: ['Windows native input kernel asset initialization failed twice; recovery exhausted.',
    'Chrome same-site link navigation retained origin/fetch guards; no security protections changed.',
    'First fixture load cold GUID scan exceeded120s client capture; prepared state recovered and pinned separately.',
    'QA extra toolbar initially displaced grid; fixture-only inline grid row corrected, no product module edit.',
    'One initial QA startup used unsupported --urls, fell back to5286; owned transient process immediately stopped before opening/preparing it. Existing5406 session unaffected.',
    'Native modal interrupted outstanding drag promise and reset CUA; subsequent normal API/browser state remained unchanged. This attempt is not a clean OS-focus acceptance pass.'],
  unityStarted: false, packaged: false, userMainRestarted: false };
await fs.writeFile(path.join(out,'verification.json'), JSON.stringify(report,null,2)+'\n',{flag:'wx'});
console.log(JSON.stringify({status:report.status,pointercancel:report.pointercancel.status,blur:report.blur.status,tree:report.tree.status,source:source.files.length,native:native.files.length,treeFiles:tree.files.length,runtime:runtime.binaries.length}));

// Inspect sealed native-event evidence without simulating any input or changing the app.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const root=path.resolve(import.meta.dirname,'..'),out=path.join(root,'artifacts/tree-input-r13'),incoming=path.join(root,'artifacts/acceptance/tree-input-r13-20261027');
const hash=b=>crypto.createHash('sha256').update(b).digest('hex'),read=async n=>JSON.parse(await fs.readFile(path.join(incoming,n)));
const same=(a,b,label)=>assert.equal(hash(JSON.stringify(a)),hash(JSON.stringify(b)),label);
const expectedPins={'evidence-manifest.json':'b469d3abefc6e7f15ea05c504bfc9e2f8ff516fff4b67d767172a550e5c33377','review-summary.json':'958e5fdefe6fdd2c2d19ca5ef0dcce413543e23a98f0a95da123fa05aaebf961','VERDICT.zh-TW.md':'f0386d08c4c36763af6bfc669e6c344233edb2f86038f8a38e7f0d3d8d340afb'};
for(const [name,pin]of Object.entries(expectedPins))assert.equal(hash(await fs.readFile(path.join(incoming,name))),pin,name);
const manifest=await read('evidence-manifest.json');assert.equal(manifest.files.length,289);
for(const f of manifest.files){const absolute=path.resolve(incoming,f.path);assert.ok(absolute.startsWith(incoming+path.sep));const bytes=await fs.readFile(absolute);assert.equal(bytes.length,f.bytes,f.path+' size');assert.equal(hash(bytes),f.sha256,f.path+' hash');}
const summary=await read('review-summary.json');for(const [name,pin]of Object.entries(summary.artifact))assert.equal(hash(await fs.readFile(path.join(out,'handoff',name))),pin,'submitted '+name);
assert.equal(summary.status,'UNVERIFIED');assert.equal(summary.pointercancel.status,'VERIFIED');assert.equal(summary.blur.status,'UNVERIFIED');assert.equal(summary.visibility.status,'UNVERIFIED');
const before=await read('before-pointercancel-graph.raw.json'),beforeState=await read('before-pointercancel-state.raw.json');
same(before,await read('after-pointercancel-graph.raw.json'),'complete pointercancel graph');same(beforeState,await read('after-pointercancel-state.raw.json'),'complete pointercancel state');
const finalR14=JSON.parse(await fs.readFile(path.join(root,'artifacts/foliage-transform-r14/api-final-both.json'))),stripped=structuredClone(finalR14.graph);
for(const n of stripped.nodes)if(n.fbxInstance)delete n.fbxInstance.transformProperties;same(before,stripped,'R13 graph equals R14 original fields');
const native=await read('native-pointercancel.json'),trace=JSON.parse(native.after.data.transformGesture),events=JSON.parse(native.after.data.qaNativeEvents);
const cancel=trace.find(e=>e.phase==='cancel-request:pointercancel'),restored=trace.find(e=>e.phase==='cancelled:pointercancel');
assert.ok(cancel?.event.trusted&&cancel.changed);assert.ok(restored?.event.trusted);same(restored.value,trace[0].before,'restored native value');assert.equal(trace.some(e=>e.phase==='commit'),false);
const dragstart=events.find(e=>e.type==='dragstart'),lost=events.find(e=>e.type==='lostpointercapture'),dragend=events.find(e=>e.type==='dragend');
assert.ok(dragstart?.trusted&&lost?.trusted&&dragend?.trusted);assert.ok(dragstart.at<cancel.at&&lost.at>restored.at&&dragend.at>lost.at);
assert.equal(native.before.data.previewCamera,native.after.data.previewCamera);const lateDragendMs=dragend.at-restored.at;assert.ok(Math.abs(lateDragendMs-summary.pointercancel.lateDragendProcessedMs)<.02);
const visibility=await read('visibility-capability-attempt.json');assert.equal(visibility.attempts,1);assert.equal(visibility.mid.data.transformDragChanged,'true');assert.equal(visibility.hideResult.ok,true);assert.equal(visibility.hiddenRead.hidden,false);assert.equal(visibility.hiddenRead.visibility,'visible');
const vt=JSON.parse(visibility.after.data.transformGesture),begin=vt[0].at,commit=vt.find(e=>e.phase==='commit');assert.ok(commit?.event.trusted);assert.equal(vt.filter(e=>e.phase==='commit').length,1);
const during=JSON.parse(visibility.after.data.qaNativeEvents).filter(e=>e.at>=begin&&e.at<=commit.at);
assert.equal(during.filter(e=>e.type==='blur'&&e.target===null).length,0);assert.equal(during.filter(e=>e.type==='visibilitychange'&&e.hidden).length,0);
const changed=await read('after-visibility-attempt-graph.raw.json'),expected=structuredClone(before),position=changed.nodes.find(n=>n.id===commit.id).position;
for(let i=0;i<3;i++)assert.ok(Math.abs(position[i]-commit.value[i])<.00001);expected.nodes.find(n=>n.id===commit.id).position=position;same(expected,changed,'only normal position command');
same(await read('after-normal-undo-graph.raw.json'),before,'Undo complete graph');same(await read('after-reload-graph.raw.json'),before,'reload complete graph');
const undo=await read('after-normal-undo-state.raw.json'),last=await read('after-reload-state.raw.json');assert.equal(undo.redoCount,1);same({...undo,revision:0,redoCount:0},{...beforeState,revision:0,redoCount:0},'Undo complete native state');same({...last,revision:0},{...beforeState,revision:0},'reload complete native state');assert.equal(last.revision,5);
const pins=await read('inputs-before.json'),afterPins=await read('inputs-after.json');let nativePins=0;
for(const key of ['targetPins','sourcePins']){same(pins[key],afterPins[key],key+' before/after');for(const [absolute,pin]of Object.entries(pins[key])){assert.equal(hash(await fs.readFile(absolute)),pin,absolute);nativePins++;}}
assert.equal(nativePins,185);assert.equal((await read('browser-warnings-errors.json')).length,0);assert.equal(summary.cleanup.cuaReleasedToImplementation,true);
const report={time:new Date().toISOString(),status:'UNVERIFIED',scopedAcceptance:'VERIFIED_NATIVE_HTML_POINTERCANCEL_ONLY',reviewDirectory:path.relative(root,incoming).replaceAll('\\','/'),reviewPins:expectedPins,independentEvidenceFiles:manifest.files.length,
  trustedChangedPointercancel:true,completeStateGraphAndCameraUnchanged:true,commits:0,lateDragendMs,latePointerup:'UNVERIFIED_NOT_CAPTURED',
  evidenceInterpretationCorrection:'evidence-interpretation-correction.json; observer and pointer tracking do not prove browser-wide absence of pointerup',
  normalOneCommandAndUndoReloadVerified:true,finalRevision:last.revision,finalDirty:last.dirty,finalUndo:last.undoCount,finalRedo:last.redoCount,nativePinsUnchanged:nativePins,
  actualHideAttempt:{attempts:1,apiSucceeded:true,documentHidden:false,documentVisibility:'visible',windowBlurDuringGesture:0,hiddenVisibilitychangeDuringGesture:0},
  remaining:summary.remaining,productModuleChanges:0,writeMode:beforeState.writeMode,nativeSavePerformed:false,cuaReleased:true,unityStarted:false,packaged:false};
await fs.writeFile(path.join(out,'review-receipt.json'),JSON.stringify(report,null,2)+'\n',{flag:'wx'});console.log(JSON.stringify({status:report.status,scopedAcceptance:report.scopedAcceptance,evidenceFiles:manifest.files.length,nativePins,lateDragendMs}));

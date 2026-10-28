// Independently inspect raw reviewer results; no browser or service mutation.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const root=path.resolve(import.meta.dirname,'..'),out=path.join(root,'artifacts/foliage-transform-r14'),incoming=path.join(root,'artifacts/acceptance/foliage-r14-20261027');
const read=async name=>JSON.parse(await fs.readFile(path.join(incoming,name))),hash=b=>crypto.createHash('sha256').update(b).digest('hex');
const same=(a,b,label)=>assert.equal(hash(JSON.stringify(a)),hash(JSON.stringify(b)),label);
const submitted=JSON.parse(await fs.readFile(path.join(out,'api-final-both.json')));
const graph=await read('before-graph.raw.json'),state=await read('before-state.raw.json');
same(graph,submitted.graph,'complete initial graph');
const differences=Object.keys(state).filter(k=>JSON.stringify(state[k])!==JSON.stringify(submitted.state[k]));
same(differences,['revision','writeMode'],'only explicit reviewer configuration and navigation revision differ');
assert.equal(state.writeMode,'direct');assert.equal(submitted.state.writeMode,'copy');
same({...state,revision:0,writeMode:'review-configuration'},{...submitted.state,revision:0,writeMode:'review-configuration'},'all other initial native state');
const beforeGraph=await read('before-escape-graph.raw.json'),beforeState=await read('before-escape-state.raw.json');
same(beforeGraph,await read('after-escape-graph.raw.json'),'complete cancel graph');same(beforeState,await read('after-escape-state.raw.json'),'complete cancel state');
const escape=await read('native-escape.json'),trace=JSON.parse(escape.after.data.transformGesture);
assert.ok(trace.find(e=>e.phase==='cancel-request:escape')?.event.trusted);assert.ok(trace.find(e=>e.phase==='cancel-request:escape')?.changed);
assert.ok(trace.find(e=>e.phase==='pointerup')?.event.trusted);assert.equal(trace.some(e=>e.phase==='commit'),false);
assert.equal(escape.before.data.previewCamera,escape.after.data.previewCamera,'cancel camera');
const drag=await read('after-normal-drag-graph.raw.json'),dragState=await read('after-normal-drag-state.raw.json');
const commits=JSON.parse((await read('native-normal-drag.json')).data.transformGesture).filter(e=>e.phase==='commit');assert.equal(commits.length,1);
const commit=commits[0],expected=structuredClone(beforeGraph),changed=drag.nodes.find(n=>n.id===commit.id);assert.ok(commit.event.trusted);
for(let i=0;i<3;i++)assert.ok(Math.abs(changed.position[i]-commit.value[i])<.00001,'native float serialization');
expected.nodes.find(n=>n.id===commit.id).position=changed.position;same(expected,drag,'normal command changes only the selected native position');
assert.equal(dragState.undoCount,beforeState.undoCount+1);assert.equal(dragState.redoCount,0);
same(await read('after-normal-undo-graph.raw.json'),beforeGraph,'Undo restores complete graph');
const undo=await read('after-normal-undo-state.raw.json');assert.equal(undo.redoCount,1);assert.equal(undo.undoCount,0);assert.equal(undo.dirty,false);
same({...undo,revision:0,redoCount:0},{...beforeState,revision:0,redoCount:0},'Undo native state excluding session history revision');
same(await read('after-reload-graph.raw.json'),graph,'reload graph');const reloaded=await read('after-reload-state.raw.json');
same({...reloaded,revision:0},{...state,revision:0},'reload initial native state');
const pins=await read('inputs-before.json');let nativeFiles=0;for(const group of [pins.targetPins,pins.sourcePins])for(const [absolute,sha]of Object.entries(group)){assert.equal(hash(await fs.readFile(absolute)),sha,absolute);nativeFiles++;}
assert.equal((await read('browser-warnings-errors.json')).length,0);
const report={time:new Date().toISOString(),completeInitialGraphMatches:true,initialStateDifferences:differences,reviewerWriteMode:'direct',submittedAndDevelopmentWriteMode:'copy',nativeModeDifferenceExplicit:true,
  completeCancelGraphAndStateUnchanged:true,trustedEscapeAndLatePointerup:true,cameraUnchanged:true,normalDragSinglePositionCommand:{id:commit.id,before:commit.before,value:changed.position},
  undoRestoresCompleteGraphAndNativeState:true,reloadRestoresInitialGraphAndState:true,nativePinsUnchanged:nativeFiles,browserWarningsAndErrors:0,finalReviewerVerdict:'PENDING'};
await fs.writeFile(path.join(out,'incoming-review-precheck.json'),JSON.stringify(report,null,2)+'\n',{flag:'wx'});console.log(JSON.stringify(report));

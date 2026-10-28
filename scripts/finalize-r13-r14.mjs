// Consolidate accepted scopes and the running development entry; no rebuild, packaging or app mutation.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const root=path.resolve(import.meta.dirname,'..'),out=path.join(root,'artifacts/foliage-transform-r14');
const read=async relative=>JSON.parse(await fs.readFile(path.join(root,relative))),hash=b=>crypto.createHash('sha256').update(b).digest('hex');
const tree=await read('artifacts/tree-input-r13/review-receipt.json'),foliage=await read('artifacts/foliage-transform-r14/review-receipt.json');
assert.equal(tree.independentEvidenceFiles,289);assert.equal(foliage.independentEvidenceFiles,405);
const browser=await read('artifacts/foliage-transform-r14/development-browser.json'),api=await read('artifacts/foliage-transform-r14/development-final-api.json'),preserved=await read('artifacts/foliage-transform-r14/main-preserved-final.json');
assert.equal(browser.url,'http://127.0.0.1:5408/?source=foliage-r14-accepted');assert.equal(browser.data.modelCount,'3105');assert.equal(browser.data.unresolvedMaterials,'0');assert.equal(browser.logs.length,0);assert.equal(browser.applyDisabled,true);
const expected=await read('artifacts/foliage-transform-r14/both-tree-630463173-front.json');assert.equal(hash(browser.data.selectionGeometry),hash(expected.data.selectionGeometry),'actual new-entry rendered geometry/materials');
assert.equal(api.state.dirty,false);assert.equal(api.state.undoCount,0);assert.equal(api.state.redoCount,0);assert.equal(api.state.writeMode,'copy');
assert.equal(preserved.entireSessionUnchanged,true);assert.equal(preserved.redo,1);assert.equal(preserved.restarted,false);
const old=await read('artifacts/tree-input-r13/handoff/source-manifest.json');
const allowedExisting=new Set(['README.md','docs/IMPLEMENTATION.md','docs/HANDOFF_TREE_INPUT_R13.zh-TW.md','scripts/verify-tree-input-r13.mjs']);
const existingChanges=[];
for(const f of old.files){const sha=hash(await fs.readFile(path.join(root,f.path)));if(sha!==f.sha256){assert.ok(allowedExisting.has(f.path),'unexpected root change '+f.path);existingChanges.push(f.path);}}
assert.equal(existingChanges.length,allowedExisting.size);
const paths=[...existingChanges,'docs/HANDOFF_FOLIAGE_R14.zh-TW.md','docs/VALIDATION_R13_R14.zh-TW.md',
  'scripts/start-reviewed-r14.ps1','scripts/capture-development-r14.mjs','scripts/verify-incoming-r14.mjs','scripts/receive-foliage-r14.mjs','scripts/receive-tree-input-r13.mjs','scripts/finalize-r13-r14.mjs',
  'artifacts/tree-input-r13/evidence-interpretation-correction.json','artifacts/tree-input-r13/review-receipt.json','artifacts/foliage-transform-r14/review-receipt.json',
  'artifacts/foliage-transform-r14/development-browser.json','artifacts/foliage-transform-r14/development-browser.jpg','artifacts/foliage-transform-r14/development-final-api.json','artifacts/foliage-transform-r14/main-preserved-final.json'];
const supplemental=[];for(const relative of [...new Set(paths)].sort()){const bytes=await fs.readFile(path.join(root,relative));supplemental.push({path:relative,bytes:bytes.length,sha256:hash(bytes)});}
const jpg=await fs.readFile(path.join(out,'development-browser.jpg'));assert.equal(jpg.subarray(0,2).toString('hex'),'ffd8');
const report={time:new Date().toISOString(),status:'UNVERIFIED',acceptedScopes:['Specified TreeA01 front/side/reload alignment','Bounded historical Both/constant0.5 cutout and readonly inspector','61 source/58 direct-Mesh and three specified Gress preservation','Native Escape, late release, normal command, Undo and reload','One native HTML drag takeover pointercancel with restoration, zero command and late dragend'],
  remaining:['Clean ordinary OS window blur and true hidden visibility during a changed gesture','General OS/device/touch pointercancel and a late pointerup after pointercancel','Obsolete TreeA01 override intent, body native classification, blanket bush/native visual parity','Full Unity version/import/shader/native interoperability and all remaining approved-plan phases'],
  evidence:{independentR14:405,independentR13:289,backendTests:191,nativePinCohorts:{r14:317,r13:185,overlap:true},fullRawStateGraphReviewed:true,interpretationCorrection:'No pointerup trace does not establish UA suppression; original raw/manifests preserved'},
  development:{url:browser.url,launcher:'scripts/start-reviewed-r14.ps1 -Port 5408',sourceRoot:'artifacts/foliage-transform-r14/source-snapshot',workingSource:'artifacts/foliage-transform-r14/source-work',actualBrowserVerified:true,apiVerified:true,nodes:6613,models:3105,triangles:633165,servedChangedAssetPins:8,browserWarningsErrors:0,dirty:false,undo:0,redo:0},
  preservation:{old5406EntireSessionUnchanged:true,oldRevision:6,oldUndo:0,oldRedo:1,baseProductSourceFilesUnchanged:true,rootExistingChanges:existingChanges},
  cleanup:{ownQaStopped:true,reviewerQaStopped:true,viewportReset:true,newDeliverableTab:'15',runningDevelopmentPorts:[5406,5408]},unityStarted:false,packaged:false,supplemental};
await fs.writeFile(path.join(out,'completion.json'),JSON.stringify(report,null,2)+'\n',{flag:'wx'});console.log(JSON.stringify({status:report.status,independentEvidence:report.evidence,developmentUrl:report.development.url,oldHistoryPreserved:true,supplementalFiles:supplemental.length}));

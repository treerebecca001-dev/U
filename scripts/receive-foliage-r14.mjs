// Receive the independent review without changing its sealed evidence.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const root=path.resolve(import.meta.dirname,'..'),out=path.join(root,'artifacts/foliage-transform-r14'),incoming=path.join(root,'artifacts/acceptance/foliage-r14-20261027');
const hash=b=>crypto.createHash('sha256').update(b).digest('hex');
const expected={'evidence-manifest.json':'a272e4c02731e892fe4139df3f9b3c8a1435b7d29a3c309391f7cb476fb00e8f','review-summary.json':'d35b49b43ff3731e5f02094f243307f5785b97124033ead0fa6355484f80491a','VERDICT.zh-TW.md':'4880084c2f6b5b8854181db5045f438ad34e5a7ad35b7ec5df5dc35bd549eb07'};
const documents={};for(const [name,pin]of Object.entries(expected)){const bytes=await fs.readFile(path.join(incoming,name));assert.equal(hash(bytes),pin,name);documents[name]=bytes;}
const manifest=JSON.parse(documents['evidence-manifest.json']);assert.equal(manifest.files.length,405);
for(const file of manifest.files){const absolute=path.resolve(incoming,file.path);assert.ok(absolute.startsWith(incoming+path.sep));const bytes=await fs.readFile(absolute);assert.equal(bytes.length,file.bytes,file.path+' size');assert.equal(hash(bytes),file.sha256,file.path+' hash');}
const summary=JSON.parse(documents['review-summary.json']);
for(const [name,pin]of Object.entries(summary.artifact))assert.equal(hash(await fs.readFile(path.join(out,'handoff',name))),pin,'submitted identity '+name);
assert.equal(summary.status,'UNVERIFIED');assert.equal(summary.placement.treeAlignment.status,'VERIFIED');assert.equal(summary.historicalMaterial.status,'VERIFIED');assert.equal(summary.geometry.status,'VERIFIED');assert.equal(summary.escape.status,'VERIFIED');
assert.equal(summary.placement.bushes.status,'UNVERIFIED');assert.equal(summary.protection.nativeFilesUnchanged,true);assert.equal(summary.cleanup.stopped,true);
const precheck=JSON.parse(await fs.readFile(path.join(out,'incoming-review-precheck.json')));assert.equal(precheck.completeCancelGraphAndStateUnchanged,true);assert.equal(precheck.nativePinsUnchanged,317);
const report={time:new Date().toISOString(),status:'UNVERIFIED',scopedAcceptance:'VERIFIED',issue:'WEB-PLACEMENT-60001-001',reviewDirectory:path.relative(root,incoming).replaceAll('\\','/'),reviewPins:expected,independentEvidenceFiles:manifest.files.length,
  accepted:['Two specified TreeA01 crowns/trunks in front and sea-side views and reload','Bounded exact-identity historical Both/constant0.5 cutout and readonly UI','61 full-source and58 direct-Mesh geometry preservation','Three specified Gress direct-Mesh preservation only','Native Escape plus late release, normal one-command drag, Undo and complete graph/state reload'],
  nativePinsUnchanged:317,servedAssets:summary.service.servedPinnedAssets,backendEvidence:summary.backendTests.counters,rawFullStateAndGraphReviewed:true,
  testModeDifference:summary.testEnvironmentDifference,remaining:summary.remaining,
  delivery:{newDevelopmentUrl:'http://127.0.0.1:5408/',sourceRoot:'artifacts/foliage-transform-r14/source-snapshot',workingSource:'artifacts/foliage-transform-r14/source-work',launcher:'scripts/start-reviewed-r14.ps1',main5406Restarted:false,mainSourceProductFilesPromoted:false,reason:'Preserve the existing5406 session and its Redo1; source-served UI is isolated by workspace root.'},
  cuaLease:'Reviewer retains for R13 fixture',unityStarted:false,packaged:false};
await fs.writeFile(path.join(out,'review-receipt.json'),JSON.stringify(report,null,2)+'\n',{flag:'wx'});console.log(JSON.stringify({status:report.status,scopedAcceptance:report.scopedAcceptance,evidenceFiles:manifest.files.length,nativePins:report.nativePinsUnchanged}));

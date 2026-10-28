// Record ordinary source/evidence files for review. No archive, publish or package creation.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const label=process.argv[2]??'handoff-fix1';assert.ok(['handoff-fix1','handoff-fix2'].includes(label));const lighting=label==='handoff-fix2';
const directory='artifacts/preview-r9/'+label,target=directory+'/evidence-manifest.json';
const sha=async p=>crypto.createHash('sha256').update(await fs.readFile(path.join(root,p))).digest('hex');
const read=async p=>JSON.parse(await fs.readFile(path.join(root,p),'utf8'));
assert.equal(await fs.stat(path.join(root,target)).then(()=>true,()=>false),false,'Do not overwrite frozen evidence');
const source=await read(directory+'/source-manifest.json');
for(const row of source.files){assert.equal(await sha(row.path),row.sha256,row.path);assert.equal(await sha('artifacts/preview-r9/source-snapshot-'+(lighting?'fix2':'fix1')+'/'+row.path),row.sha256,row.path);}
for(const row of (await read(directory+'/runtime.json')).binaries)assert.equal(await sha(row.path),row.sha256,row.path);
assert.equal((await read(directory+'/test-summary.json')).uniqueTests,lighting?177:174);
assert.equal((await read('artifacts/preview-r9/'+(lighting?'lighting-browser-ready':'water-browser')+'/result.json')).status,'PASS');
assert.equal((await read('artifacts/preview-r9/'+(lighting?'lighting-focused-source-formula':'water-focused-verified')+'/result.json')).status,'PASS');
assert.equal((await read('artifacts/preview-r9/source-dependency-search.json')).status,'SOURCE_DEPENDENCY_MISSING');
const paths=new Set(['docs/HANDOFF_PREVIEW_R9_FIX1.zh-TW.md','artifacts/preview-r9/build-water.log','artifacts/preview-r9/test-water.log',
 'artifacts/preview-r9/test-results/preview-r9-water.trx','artifacts/preview-r9/ui-audit-water.json','artifacts/preview-r9/water-contract-fix1.json',
 'artifacts/preview-r9/source-dependency-search.json','artifacts/preview-r9/terrain-layer-diagnostic.json',
 'artifacts/preview-r9/terrain-layer-meta-search.txt','artifacts/preview-r9/terrain-layer-package-meta-search.txt',
 'artifacts/preview-r9/handoff/source-manifest.json','artifacts/preview-r9/handoff/evidence-manifest.json',
 'artifacts/version-r8/native-content-comparison.json','artifacts/version-r8/content-completion/evidence-manifest.json']);
if(lighting){
 paths.clear();
 for(const p of ['docs/HANDOFF_PREVIEW_R9_FIX2.zh-TW.md','artifacts/preview-r9/test-summary-lighting.json','artifacts/preview-r9/build-lighting.log',
  'artifacts/preview-r9/build-lighting-first.log','artifacts/preview-r9/test-lighting-analyzer-first.log','artifacts/preview-r9/test-lighting-cli-harness-failed.log','artifacts/preview-r9/test-lighting-cli.log','artifacts/preview-r9/test-lighting-cli-permission-retry.log',
  'artifacts/preview-r9/test-results/preview-r9-lighting-cli-harness-failed.trx','artifacts/preview-r9/test-results/preview-r9-lighting-cli.trx','artifacts/preview-r9/test-results/preview-r9-lighting-cli-permission-retry.trx',
  'artifacts/preview-r9/ui-audit-lighting.json','artifacts/preview-r9/water-lighting-contract-fix2.json','artifacts/preview-r9/lighting-fields-fix2.json','artifacts/preview-r9/inspect-lighting-fix2.ps1','artifacts/preview-r9/probe-exr-fix2.py','artifacts/preview-r9/upstream-EXRLoader-r180.js',
  'artifacts/preview-r9/lighting-focused/result.json','artifacts/preview-r9/lighting-focused-replay/result.json','artifacts/preview-r9/lighting-browser/result.json','artifacts/preview-r9/lighting-browser-final/result.json',
  'artifacts/preview-r9/review-receipt-fix1.json','artifacts/preview-r9/handoff-fix1/source-manifest.json','artifacts/preview-r9/handoff-fix1/evidence-manifest.json',
  'artifacts/acceptance/scene-60001-20261022/review-summary-r9-fix1.json','artifacts/acceptance/scene-60001-20261022/acceptance-evidence-manifest-r9-fix1.json',
  'artifacts/preview-r9/source-dependency-search.json','artifacts/version-r8/native-content-comparison.json','artifacts/version-r8/content-completion/evidence-manifest.json'])paths.add(p);
}
for(const folder of [lighting?'artifacts/preview-r9/lighting-browser-ready':'artifacts/preview-r9/water-browser',lighting?'artifacts/preview-r9/lighting-focused-source-formula':'artifacts/preview-r9/water-focused-verified',directory]){
 for(const file of await fs.readdir(path.join(root,folder),{withFileTypes:true}))if(file.isFile())paths.add(folder+'/'+file.name);
}
const files=[];for(const p of [...paths].sort()){const stat=await fs.stat(path.join(root,p));files.push({path:p,bytes:stat.size,sha256:await sha(p)});}
const value={identity:source.identity,status:lighting?'SCOPED_NATIVE_REFLECTION_SHADOW_VERIFIED_FULL_SCENE_FAILED_REVIEW_PENDING':'SCOPED_WATER_DATAFLOW_VERIFIED_FULL_SCENE_FAILED_REVIEW_PENDING',createdUtc:new Date().toISOString(),
 sourceManifestSha256:await sha(directory+'/source-manifest.json'),files,
 acceptance:{backend:lighting?'177 unique applicable PASS (167 + 9 + 1 unchanged-binary retries)':'174/174 PASS',fullSceneBrowser:'6 groups PASS',waterDataflowBrowser:lighting?'Actual Map_60001 reflection/shadow pixels and mask/EXR negatives: 2 groups PASS':'4 groups PASS',
  remaining:lighting?['Missing original TerrainLayer source','Complete URP GI/CSM/Shadowmask/local probes unimplemented; Unity pixel comparison UNVERIFIED']:['Missing original TerrainLayer source','Enabled environment reflection and complete URP lighting/shadows remain unimplemented','Unity/native pixel comparison UNVERIFIED']},
 originalDReadOnly:true,unityStarted:false,packaging:false,mainPort5286Untouched:true};
await fs.writeFile(path.join(root,target),JSON.stringify(value,null,2)+'\n');
console.log(JSON.stringify({sourceFiles:source.files.length,evidenceFiles:files.length,sourceManifestSha256:value.sourceManifestSha256,evidenceManifestSha256:await sha(target)}));

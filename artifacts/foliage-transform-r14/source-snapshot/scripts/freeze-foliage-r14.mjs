// Freeze explicit source/runtime/evidence files. Never traverse the staging SDK junction.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const root=path.resolve(import.meta.dirname,'..'),out=path.join(root,'artifacts/foliage-transform-r14'),handoff=path.join(out,'handoff');
const read=async p=>JSON.parse(await fs.readFile(p)),hash=b=>crypto.createHash('sha256').update(b).digest('hex');
assert.equal(await fs.access(path.join(handoff,'handoff.json')).then(()=>true,()=>false),false,'completed handoff is immutable');
const copyOrVerify=async(target,bytes)=>{
  try{await fs.writeFile(target,bytes,{flag:'wx'});}catch(error){if(error.code!=='EEXIST')throw error;assert.equal(hash(await fs.readFile(target)),hash(bytes),'partial freeze must match '+target);}
};
const old=await read(path.join(root,'artifacts/tree-input-r13/handoff/source-manifest.json'));
const report=await read(path.join(out,'verification-final.json'));
const extras=(await fs.readdir(path.join(root,'scripts'))).filter(n=>n.endsWith('-r14.mjs')).map(n=>'scripts/'+n);
extras.push('docs/HANDOFF_FOLIAGE_R14.zh-TW.md');
const staged=new Set([...old.files.map(f=>f.path),...report.newProductFiles]);
const source=[];
await fs.mkdir(handoff,{recursive:true});
for(const relative of [...new Set([...staged,...extras])].sort()){
  const bytes=await fs.readFile(path.join(staged.has(relative)?path.join(out,'source-work'):root,relative));
  const target=path.join(out,'source-snapshot',relative);await fs.mkdir(path.dirname(target),{recursive:true});
  if(relative==='scripts/freeze-foliage-r14.mjs')await fs.writeFile(target,bytes);else await copyOrVerify(target,bytes);
  source.push({path:relative,bytes:bytes.length,sha256:hash(bytes)});
}
const oldRuntime=await read(path.join(root,'artifacts/prefab-cancel-r12/handoff/runtime.json')),binaries=[];
await fs.mkdir(path.join(out,'runtime'),{recursive:true});
for(const f of oldRuntime.binaries){
  const name=path.basename(f.path),built=path.join(out,'source-work',f.builtSource);
  const bytes=await fs.readFile(built);await copyOrVerify(path.join(out,'runtime',name),bytes);
  binaries.push({path:'runtime/'+name,bytes:bytes.length,sha256:hash(bytes),builtSource:path.relative(root,built).replaceAll('\\','/')});
}
const tested=path.join(out,'source-work/tests/SceneWorkbench.Tests/bin/Release/net10.0');
for(const name of ['SceneWorkbench.Core.dll','SceneWorkbench.Unity.dll','SceneWorkbench.Web.dll']){
  const runtime=binaries.find(f=>f.path==='runtime/'+name);assert.equal(hash(await fs.readFile(path.join(tested,name))),runtime.sha256,'runtime matches tested assemblies '+name);
}
const native=await read(path.join(root,'artifacts/tree-input-r13/native-before.json')),trees=await read(path.join(root,'artifacts/tree-input-r13/tree-target-audit.json'));
for(const f of [...native.files,...trees.files])assert.equal(hash(await fs.readFile(f.absolute)),f.sha256,f.absolute);
const evidence=[];
for(const name of (await fs.readdir(out,{withFileTypes:true})).filter(f=>f.isFile()).map(f=>f.name).sort()){
  const bytes=await fs.readFile(path.join(out,name));evidence.push({path:name,bytes:bytes.length,sha256:hash(bytes)});
}
for(const name of (await fs.readdir(path.join(out,'test-results'))).sort()){
  const relative='test-results/'+name,bytes=await fs.readFile(path.join(out,relative));evidence.push({path:relative,bytes:bytes.length,sha256:hash(bytes)});
}
const refs=['docs/APPROVED_PLAN.zh-TW.md','artifacts/tree-input-r13/handoff/handoff.json','artifacts/acceptance/placement-60001-20261027/model-placement-audit.json','artifacts/acceptance/placement-60001-20261027/shader-source-audit.json'];
const references=[];for(const relative of refs)references.push({path:relative,sha256:hash(await fs.readFile(path.join(root,relative)))});
const manifests={
  'source-manifest.json':{time:new Date().toISOString(),root:'source-snapshot',files:source},
  'runtime.json':{root:'runtime',backendTests:report.backend,buildMatchesTestedAssemblies:true,binaries},
  'native-input-manifest.json':{files:native.files,treeFiles:trees.files,vegetationAudit:'source-transform-audit.json'},
  'evidence-manifest.json':{files:evidence,references}
};
const pins=[];for(const [name,body]of Object.entries(manifests)){const bytes=Buffer.from(JSON.stringify(body,null,2)+'\n');await fs.writeFile(path.join(handoff,name),bytes,{flag:'wx'});pins.push({path:name,sha256:hash(bytes)});}
const summary={time:new Date().toISOString(),issue:'WEB-PLACEMENT-60001-001',status:'UNVERIFIED',independentReview:'PENDING',sourceFiles:source.length,runtimeFiles:binaries.length,evidenceFiles:evidence.length,manifests:pins,main5406Restarted:false,unityStarted:false,packaged:false};
await fs.writeFile(path.join(handoff,'handoff.json'),JSON.stringify(summary,null,2)+'\n',{flag:'wx'});console.log(JSON.stringify(summary));

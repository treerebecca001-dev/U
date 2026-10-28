import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const root=path.resolve(import.meta.dirname,'..'),out=path.join(root,'artifacts/foliage-transform-r14');
const port=Number(process.argv[2]),label=process.argv[3];assert.ok([5442,5444].includes(port));assert.match(label??'',/^[a-z0-9-]+$/);
const base='http://127.0.0.1:'+port,hash=b=>crypto.createHash('sha256').update(b).digest('hex');
const before=JSON.parse(await fs.readFile(path.join(root,'artifacts/tree-input-r13/native-before.json')));
for(const f of before.files)assert.equal(hash(await fs.readFile(f.absolute)),f.sha256,f.absolute);
const response=await fetch(base);assert.equal(response.status,200);const html=await response.text(),token=html.match(/name="workbench-token" content="([a-f0-9]+)"/)[1];
async function api(route,body){const r=await fetch(base+route,{method:body?'POST':'GET',headers:{'X-Workbench-Token':token,Origin:base,...body?{'Content-Type':'application/json'}:{}},...body?{body:JSON.stringify(body)}:{},signal:AbortSignal.timeout(120000)});assert.equal(r.status,200,route);return r.json();}
let state=await api('/api/state');
if(process.argv.includes('--prepare')){assert.equal(state.projectPath,null);state=await api('/api/project/open',{revision:state.revision,path:path.join(root,'Workspace/Baselines/Client231229-2022.3.13f1'),mode:'inspect'});state=await api('/api/scene/open',{revision:state.revision,path:'Assets/02.Arts/Scene/Map/Map_60001/Map_60001.unity'});}
const record={time:new Date().toISOString(),base,state,nativeFilesUnchanged:before.files.length};
if(process.argv.includes('--graph'))record.graph=await api('/api/preview?path='+encodeURIComponent(state.scenePath));
await fs.writeFile(path.join(out,'api-'+label+'.json'),JSON.stringify(record)+'\n',{flag:'wx'});
console.log(JSON.stringify({label,revision:state.revision,dirty:state.dirty,undo:state.undoCount,redo:state.redoCount,nodes:record.graph?.nodes.length,nativeFilesUnchanged:before.files.length}));

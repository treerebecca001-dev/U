// Prepare the new isolated development session; preserve the older 5406 history.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const root=path.resolve(import.meta.dirname,'..'),out=path.join(root,'artifacts/foliage-transform-r14'),base='http://127.0.0.1:5408';
const hash=b=>crypto.createHash('sha256').update(b).digest('hex');
const html=await (await fetch(base)).text(),token=html.match(/name="workbench-token" content="([a-f0-9]+)"/)[1];
async function api(route,body){const r=await fetch(base+route,{method:body?'POST':'GET',headers:{'X-Workbench-Token':token,Origin:base,...body?{'Content-Type':'application/json'}:{}},...body?{body:JSON.stringify(body)}:{},signal:AbortSignal.timeout(120000)});assert.equal(r.status,200,route);return r.json();}
let state=await api('/api/state');
if(process.argv.includes('--prepare')){
  assert.equal(state.projectPath,null,'new session only');
  state=await api('/api/project/open',{revision:state.revision,path:path.join(root,'Workspace/Baselines/Client231229-2022.3.13f1'),mode:'inspect'});
  state=await api('/api/scene/open',{revision:state.revision,path:'Assets/02.Arts/Scene/Map/Map_60001/Map_60001.unity'});
}
assert.equal(state.dirty,false);assert.equal(state.undoCount,0);assert.equal(state.redoCount,0);
const graph=await api('/api/preview?path='+encodeURIComponent(state.scenePath)),expected=JSON.parse(await fs.readFile(path.join(out,'api-final-both.json')));
assert.equal(hash(JSON.stringify(graph)),hash(JSON.stringify(expected.graph)),'same complete preview graph');
assert.equal(hash(JSON.stringify({...state,revision:0})),hash(JSON.stringify({...expected.state,revision:0})),'same native state');
const manifest=JSON.parse(await fs.readFile(path.join(out,'handoff/source-manifest.json'))),served=[];
for(const relative of ['asset-preview.js','fbx-transforms.js','preview-bindings.js','scene-view.js','material-adapters.js','historical-surface.js','surface-adapters.json','references/Temp_ScenePlantBase.shadergraph.txt']){
  const source=manifest.files.find(f=>f.path==='src/SceneWorkbench.Web/wwwroot/ui/'+relative);assert.ok(source);
  const response=await fetch(base+'/ui/'+relative);assert.equal(response.status,200);const bytes=Buffer.from(await response.arrayBuffer());assert.equal(hash(bytes),source.sha256,relative);served.push({path:'/ui/'+relative,sha256:hash(bytes)});
}
const pins=JSON.parse(await fs.readFile(path.join(out,'handoff/native-input-manifest.json'))).files;
for(const f of pins)assert.equal(hash(await fs.readFile(f.absolute)),f.sha256,f.absolute);
const labelIndex=process.argv.indexOf('--label'),label=labelIndex>=0?process.argv[labelIndex+1]:process.argv.includes('--prepare')?'development-ready':'development-final-api';
assert.match(label,/^[a-z0-9-]+$/);
await fs.writeFile(path.join(out,label+'.json'),JSON.stringify({time:new Date().toISOString(),base,state,graphSha256:hash(JSON.stringify(graph)),served,nativeFilesUnchanged:pins.length,old5406Restarted:false},null,2)+'\n',{flag:'wx'});
console.log(JSON.stringify({url:base,revision:state.revision,dirty:state.dirty,nodes:graph.nodes.length,servedSources:served.length,nativeFilesUnchanged:pins.length,old5406Restarted:false}));

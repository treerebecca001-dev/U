// Execute the actual new geometry loader and root-binding code against source FBX data.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
import { pathToFileURL } from 'node:url';
const root=path.resolve(import.meta.dirname,'..'),out=path.join(root,'artifacts/foliage-transform-r14');
const working=path.join(out,'source-work'),before=path.join(out,'before-source');
// The material module reads its pinned registry at import time; serve local static
// data in this ordinary Node harness. This never intercepts browser requests.
globalThis.fetch = async route => {
  assert.ok(typeof route === 'string' && route.startsWith('/ui/') && !route.includes('..'));
  return new Response(await fs.readFile(path.join(working,'src/SceneWorkbench.Web/wwwroot',route)));
};
const load=async(base,file)=>import(pathToFileURL(path.join(base,'src/SceneWorkbench.Web/wwwroot',file)).href);
const { AssetPreview: Current }=await load(working,'ui/asset-preview.js'),{AssetPreview:Previous}=await load(before,'ui/asset-preview.js');
const {fbxInstanceBinding}=await load(working,'ui/preview-bindings.js'),{overrideFbxRoot}=await load(working,'ui/fbx-transforms.js');
const THREE=await load(working,'vendor/three.module.js'),hash=b=>crypto.createHash('sha256').update(b).digest('hex');
const defaults={position:[5,6,7],rotation:[.1,.2,.3,.9],scale:[2,3,4]};
const instance={transformFileId:'-8679921383154817045',transformProperties:['m_LocalPosition.y','m_LocalScale.x'],position:[0,0,0],rotation:[0,0,0,1],scale:[1,1,1],renderers:[{enabled:true}],active:true};
assert.deepEqual(overrideFbxRoot(instance,defaults),{position:[5,0,7],rotation:[.1,.2,.3,.9],scale:[1,3,4]});
assert.deepEqual(defaults,{position:[5,6,7],rotation:[.1,.2,.3,.9],scale:[2,3,4]});
assert.equal(overrideFbxRoot({...instance,transformProperties:undefined},defaults),null);
assert.equal(fbxInstanceBinding(instance,[{},{}]).supported,false);
assert.equal(fbxInstanceBinding(instance,[{}]).supported,false);
assert.equal(fbxInstanceBinding({...instance,transformFileId:'1234'},[{sourceRoot:defaults,rootGeometry:{}}]).supported,false);
assert.equal(fbxInstanceBinding({...instance,transformFileId:null},[{}]).transform,null);
const project=path.join(root,'Workspace/Baselines/Client231229-2022.3.13f1');
const audit=JSON.parse(await fs.readFile(path.join(out,'source-transform-audit.json')));
const mock=Class=>{const assets=new Class('offline',()=>{});assets.fetch=async route=>{
  const url=new URL(route,'http://offline.invalid'),relative=url.searchParams.get('path');
  if(url.pathname==='/api/source')return new Response(await fs.readFile(path.join(project,relative)));
  assert.equal(url.pathname,'/api/model');const meta=await fs.readFile(path.join(project,relative+'.meta'),'utf8');
  const get=(name,fallback)=>Number(meta.match(new RegExp('^    '+name+': (.+)$','m'))?.[1]??fallback);
  return new Response(JSON.stringify({globalScale:get('globalScale',1),useFileScale:Boolean(get('useFileScale',1)),useFileUnits:Boolean(get('useFileUnits',1)),swapUvChannels:Boolean(get('swapUVChannels',0)),meshNames:{}}));};return assets;};
const old=mock(Previous),current=mock(Current),cases=[];
const data=attribute=>Buffer.from(attribute.array.buffer,attribute.array.byteOffset,attribute.array.byteLength);
function assertGeometry(a,b,label){for(const name of ['position','normal','uv','uv1','uv2','uv3']){
  assert.equal(hash(data(a.attributes[name])),hash(data(b.attributes[name])),label+':'+name);}}
let maxSplitError=0;
for(const source of audit.sources){
  const oldWhole=await old.geometry(source.path,'0'),nowWhole=await current.geometry(source.path,'0');
  assertGeometry(oldWhole[0].geometry,nowWhole[0].geometry,source.path+' full source view');
  const local=nowWhole[0].rootGeometry,base=nowWhole[0].sourceRoot;
  assert.ok(local&&base,source.path);
  const p=base.position,q=base.rotation,s=base.scale;
  const matrix=new THREE.Matrix4().compose(new THREE.Vector3(p[0],p[1],-p[2]),new THREE.Quaternion(-q[0],-q[1],q[2],q[3]),new THREE.Vector3(...s));
  const a=local.attributes.position,b=nowWhole[0].geometry.attributes.position,v=new THREE.Vector3();let error=0;
  for(let i=0;i<a.count;i++){v.fromBufferAttribute(a,i).applyMatrix4(matrix);error=Math.max(error,v.distanceTo(new THREE.Vector3().fromBufferAttribute(b,i)));}
  assert.ok(error<.00003,source.path+': default split must reproduce full source placement');maxSplitError=Math.max(maxSplitError,error);
  const direct=source.instances.find(i=>i.fileId!=='0');
  if(direct){const oldDirect=await old.geometry(source.path,direct.fileId),newDirect=await current.geometry(source.path,direct.fileId);assertGeometry(oldDirect[0].geometry,newDirect[0].geometry,source.path+' explicit mesh');}
  cases.push({path:source.path,wholeSourceBytesUnchanged:true,directMeshUnchanged:Boolean(direct),maxDefaultSplitErrorMetres:error});
}
old.dispose();current.dispose();
const report={time:new Date().toISOString(),status:'VERIFIED_SCOPED_GEOMETRY_TESTS',partialAxesPreserved:true,explicitZeroReplacesFallback:true,ambiguousRootRejected:true,
  models:cases.length,directMeshCases:cases.filter(c=>c.directMeshUnchanged).length,maxDefaultSplitErrorMetres:maxSplitError,cases,
  scope:'Actual production geometry methods under ordinary offline file-backed fetch. Verifies source view/direct Mesh preservation and root-vs-geometry separation; browser and Unity acceptance are separate.'};
const label=process.argv[2]??'geometry-tests';assert.match(label,/^[a-z0-9-]+$/);
await fs.writeFile(path.join(out,label+'.json'),JSON.stringify(report,null,2)+'\n',{flag:'wx'});
console.log(JSON.stringify({status:report.status,models:cases.length,directMeshCases:report.directMeshCases,maxDefaultSplitErrorMetres:maxSplitError}));

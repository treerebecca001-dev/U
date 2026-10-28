// Compare full native documents plus actual DOM-backed rendered provenance.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
import * as T from '../src/SceneWorkbench.Web/wwwroot/vendor/three.module.js';
const root=path.resolve(import.meta.dirname,'..'),out=path.join(root,'artifacts/foliage-transform-r14');
const read=async n=>JSON.parse(await fs.readFile(path.join(out,n))),hash=b=>crypto.createHash('sha256').update(b).digest('hex');
const same=(a,b,label)=>assert.equal(hash(JSON.stringify(a)),hash(JSON.stringify(b)),label);
const before=await read('api-before.json'),after=await read('api-after.json'),last=await read('api-after-escape.json');
same(before.state,after.state,'complete native scene state');same(after.state,last.state,'cancel state unchanged');same(after.graph,last.graph,'cancel graph unchanged');
const final=await read('api-final-both.json');
// Opening and closing the readonly material inspector advances only the session revision.
same({...after.state,revision:0},{...final.state,revision:0},'final native state except material navigation revision');
same(after.graph,final.graph,'final historical render state does not alter native graph');
assert.equal(final.state.revision,after.state.revision+2);assert.equal(final.state.material,null);
const stripped=structuredClone(after.graph);let masks=0;
for(const n of stripped.nodes)if(n.fbxInstance){assert.ok(Array.isArray(n.fbxInstance.transformProperties));delete n.fbxInstance.transformProperties;masks++;}
same(before.graph,stripped,'only new per-axis provenance may differ in graph');assert.equal(masks,265);
const audit=await read('source-transform-audit.json'),project=path.join(root,'Workspace/Baselines/Client231229-2022.3.13f1');
const watched=JSON.parse(await fs.readFile(path.join(root,'artifacts/tree-input-r13/native-before.json'))).files;
const trees=JSON.parse(await fs.readFile(path.join(root,'artifacts/tree-input-r13/tree-target-audit.json'))).files;
for(const f of [...watched,...trees])assert.equal(hash(await fs.readFile(f.absolute)),f.sha256,f.absolute);
for(const f of audit.sources){assert.equal(hash(await fs.readFile(path.join(project,f.path))),f.sha256);assert.equal(hash(await fs.readFile(path.join(project,f.path+'.meta'))),f.metaSha256);}
const byId=new Map(after.graph.nodes.map(n=>[n.id,n])),worlds=new Map();
function matrix(id){if(worlds.has(id))return worlds.get(id);const n=byId.get(id);if(!n)return new T.Matrix4();const b=n.fbxInstance??n;
  const v=new T.Matrix4().compose(new T.Vector3(b.position[0],b.position[1],-b.position[2]),new T.Quaternion(-b.rotation[0],-b.rotation[1],b.rotation[2],b.rotation[3]),new T.Vector3(...b.scale));
  const m=matrix(n.parentId).clone().multiply(v);worlds.set(id,m);return m;}
const close=(a,b,label)=>{assert.equal(a.length,b.length,label);const error=Math.max(...a.map((v,i)=>Math.abs(v-b[i])));assert.ok(error<.00002,label+':'+error);return error;};
const cases=[];
for(const [name,id]of [['tree-222870-desktop','222870'],['tree-630463173-desktop','630463173'],['gress17','3106871'],['gress19','1962971'],['gress33b','851942063']]){
  const dom=await read('after-'+name+'.json'),observation=JSON.parse(dom.data.selectionGeometry);assert.equal(observation.id,id);
  for(const mesh of observation.meshes){const n=byId.get(mesh.nodeId),source=audit.sources.find(s=>s.path===mesh.path);assert.ok(n&&source);
    const expected=matrix(n.id),raw=source.meshes[0].bounds;
    const local=new T.Box3(new T.Vector3(...raw.localMin),new T.Vector3(...raw.localMax)).applyMatrix4(new T.Matrix4().makeScale(-source.factor,source.factor,-source.factor));
    const world=local.clone().applyMatrix4(expected);
    const matrixError=close(mesh.matrixWorld,expected.toArray(),n.id+' native parent chain');
    const localError=close(mesh.localBounds.flat(),[...local.min.toArray(),...local.max.toArray()],n.id+' geometry local');
    const worldError=close(mesh.worldBounds.flat(),[...world.min.toArray(),...world.max.toArray()],n.id+' rendered world');
    assert.equal(mesh.rootTransformSeparated,Boolean(n.fbxInstance));cases.push({id:n.id,path:mesh.path,rootSeparated:mesh.rootTransformSeparated,matrixError,localError,worldError});
  }
  if(name.startsWith('gress')){const previous=await read('before-'+name+'.json');assert.equal(previous.data.previewCamera,dom.data.previewCamera,name+' unchanged camera');}
}
const fixedBefore=await read('before-fixed-camera.json'),fixedAfter=await read('after-fixed-camera.json');assert.equal(fixedBefore.data.previewCamera,fixedAfter.data.previewCamera);
for(const field of ['modelCount','triangles','fbxBindings','terrainCount','terrainTriangles','unresolvedMaterials','unresolvedTerrainLayers','shadowCasters'])assert.equal(fixedBefore.data[field],fixedAfter.data[field],field);
const escape=await read('native-escape-regression.json'),trace=JSON.parse(escape.after.data.transformGesture);
assert.ok(trace.find(e=>e.phase==='cancel-request:escape')?.changed);assert.ok(trace.find(e=>e.phase==='cancel-request:escape')?.event.trusted);
assert.ok(trace.find(e=>e.phase==='pointerup')?.event.trusted);assert.equal(trace.some(e=>e.phase==='commit'),false);
assert.equal(escape.before.data.previewCamera,escape.after.data.previewCamera);
const narrow=await read('narrow-browser.json'),desktop=await read('after-tree-222870-desktop.json');
same(JSON.parse(narrow.data.selectionGeometry),JSON.parse(desktop.data.selectionGeometry),'narrow rendered placement');assert.equal(narrow.applyDisabled,true);
const bothCases=[];
for(const name of ['tree-222870-front','tree-222870-side','tree-630463173-front','tree-630463173-side','gress17','gress19','gress33b']){
  const dom=await read('both-'+name+'.json'),observation=JSON.parse(dom.data.selectionGeometry);
  const previous=await read('after-'+(name.startsWith('tree-')?name.replace(/-(front|side)$/,'-desktop'):name)+'.json');
  const geometry=structuredClone(observation);for(const mesh of geometry.meshes)delete mesh.materials;
  same(geometry,JSON.parse(previous.data.selectionGeometry),name+' geometry unchanged by historical material');
  for(const mesh of observation.meshes)for(const material of mesh.materials){
    if(name.startsWith('tree-')){assert.equal(material.side,T.DoubleSide);assert.equal(material.historical.sha256,'c4781ba261421c1bacd0ed55f07ce80fed85d0d6333ad41cb45663cdfcad0e29');assert.equal(material.historical.alphaCutoff,.5);}
    else {assert.equal(material.historical,null);assert.equal(material.side,T.FrontSide);}
  }
  if(name.startsWith('gress'))assert.equal(dom.data.previewCamera,previous.data.previewCamera,name+' final unchanged camera');
  bothCases.push({name,id:observation.id,meshes:observation.meshes.length,materials:observation.meshes.flatMap(m=>m.materials)});
}
const bothNarrow=await read('both-narrow-browser.json'),bothDesktop=await read('both-tree-222870-front.json');
same(JSON.parse(bothNarrow.data.selectionGeometry),JSON.parse(bothDesktop.data.selectionGeometry),'final narrow placement and render state');assert.equal(bothNarrow.applyDisabled,true);
assert.equal((await read('both-fixed-camera.json')).data.previewCamera,fixedBefore.data.previewCamera);
assert.equal((await read('both-browser-logs.json')).length,0);
const materialStatus=await read('both-material-status.json');assert.equal(materialStatus.materialApplyDisabled,true);
assert.ok(materialStatus.text.includes('歷史同 GUID ShaderGraph 參考'));assert.ok(materialStatus.text.includes('UV 風動與 Unity 光照未驗證'));
const historicalTests=await read('historical-surface-tests-final.json');assert.equal(historicalTests.nativeSnapshotUnchanged,true);assert.equal(historicalTests.vertexShaderUnchanged,true);assert.equal(historicalTests.constantCutoffIgnoresStaleMaterial,true);
const historicalPath='src/SceneWorkbench.Web/wwwroot/ui/references/Temp_ScenePlantBase.shadergraph.txt';
assert.equal(hash(await fs.readFile(path.join(out,'source-work',historicalPath))),historicalTests.referenceHash);
assert.equal(hash(await fs.readFile(path.join(root,'artifacts/acceptance/placement-60001-20261027/shader-source-audit.json'))),'1d46575aa55c444577d0e49030a3606af34b35c24a98e4c67324ce3eb0671081');
const logs=await read('final-browser-logs.json');assert.equal(logs.length,0);const consoleEvidence=await read('browser-console.json');assert.equal(consoleEvidence.dom.diagnostics,'2');
const trx=await fs.readFile(path.join(out,'test-results/foliage-r14-runtime-corrected.trx'),'utf8');
const counters=Object.fromEntries([...trx.match(/<Counters\s+([^>]+)\/>/)[1].matchAll(/(\w+)="(\d+)"/g)].map(m=>[m[1],Number(m[2])]));assert.equal(counters.total,191);assert.equal(counters.passed,191);assert.equal(counters.failed,0);
const geometryTests=await read('geometry-tests-final-root-id.json');assert.equal(geometryTests.models,61);assert.equal(geometryTests.directMeshCases,58);
const oldManifest=JSON.parse(await fs.readFile(path.join(root,'artifacts/tree-input-r13/handoff/source-manifest.json')));
for(const f of oldManifest.files)assert.equal(hash(await fs.readFile(path.join(root,f.path))),f.sha256,'main source was not staged prematurely:'+f.path);
const expectedChanges=['src/SceneWorkbench.Unity/FbxInstancePreview.cs','src/SceneWorkbench.Web/wwwroot/ui/asset-preview.js','src/SceneWorkbench.Web/wwwroot/ui/preview-bindings.js','src/SceneWorkbench.Web/wwwroot/ui/scene-view.js','src/SceneWorkbench.Web/wwwroot/ui/material-adapters.js','src/SceneWorkbench.Web/wwwroot/ui/surface-adapters.json'];
const changes=[];
for(const f of oldManifest.files){const bytes=await fs.readFile(path.join(out,'source-work',f.path));if(hash(bytes)!==f.sha256)changes.push(f.path);}
same(changes.sort(),expectedChanges.sort(),'bounded staged existing source changes');
const independentPath=path.join(root,'artifacts/acceptance/placement-60001-20261027/model-placement-audit.json');
assert.equal(hash(await fs.readFile(independentPath)),'28dabfd13c44b98ffc5683bd27bd786a1feb75f90cda89720d3783707b940f27');
const report={time:new Date().toISOString(),status:'UNVERIFIED',scopedFix:'VERIFIED_LOCAL_PREVIEW_PENDING_INDEPENDENT_REVIEW',
  issue:'WEB-PLACEMENT-60001-001',cause:'Source FBX Model root placement was baked into fileId0 mesh vertices and multiplied by native root overrides a second time.',
  change:'Keep geometric transform and full source view; split root only for exactly one source Model/Mesh, pinned native root fileID and explicit per-axis override provenance. Preserve defaults for omitted axes; leave direct Mesh branch unchanged.',
  renderedCases:cases,pairedFixedCamera:true,bushCamerasUnchanged:3,fullNativeStateUnchanged:true,graphChanges:'265 per-axis provenance arrays only; original graph fields/diagnostics/materials/Terrain unchanged',
  backend:counters,sourceGeometryModelsUnchanged:61,directMeshVariantsUnchanged:58,defaultSplitErrorMetres:geometryTests.maxDefaultSplitErrorMetres,
  trustedEscapeWithLatePointerup:true,escapeCommits:0,narrowPlacementIdentical:true,browserErrors:0,diagnostics:2,
  originalNativePinsUnchanged:watched.length,treeFilesUnchanged:trees.length,vegetationSourceAndMetaPinsUnchanged:audit.sources.length*2,
  existingStagedChanges:changes,newProductFiles:['src/SceneWorkbench.Web/wwwroot/ui/fbx-transforms.js','src/SceneWorkbench.Web/wwwroot/ui/historical-surface.js',historicalPath,'tests/SceneWorkbench.Tests/FbxTransformPropertiesTests.cs'],
  historicalRenderState:{cases:bothCases,sourceSha256:historicalTests.referenceHash,renderFace:'Both',alphaClipThreshold:.5,materialOverride:false,guardsAndFallbackTested:true,vertexShaderUnchanged:true,nativeSnapshotUnchanged:true,inspectorReadonly:true,narrowRenderStateIdentical:true,scope:'Exact GUID/subasset historical URP source reference. Current ShaderGraph remains missing; historical version equivalence, UV wind and Unity lighting are unverified. No billboard or geometry invention.'},
  finalSessionNavigation:{beforeRevision:after.state.revision,afterRevision:final.state.revision,reason:'Readonly material open/close',remainingNativeStateAndGraphUnchanged:true},
  mainSourceStillUnchanged:true,main5406Restarted:false,unityStarted:false,packaged:false,
  remaining:['Independent fresh browser recheck of final root-identity guard and transformed foliage.',
    'Full Unity import/axis/appearance parity remains unverified; source bytes and graph equality are not proof of original Unity placement.',
    'No guessed remap for TreeA01 obsolete-.2/.87 override; keep existing diagnostic and readonly data.',
    'Representative Gress17/19/33b positions were preserved, not declared visually identical to an unavailable original Unity image.',
    'R13 blur/visibility and general native pointer cancellation remain separately open.'],
  infrastructure:['Initial staged suite162/191 with29 process/runtime-path failures; preserved initial TRX. Pointed owned staging .tools/dotnet junction at existing SDK and set process-only DOTNET_ROOT/PATH; rerun191/191.',
    'First unbounded MSBuild restore hung; stopped owned attempt, restored with-m:1/-nr:false and existing locked packages.',
    'Initial after screenshot1280x720 preserved; desktop pairs explicitly1178x1000. Raw JPEG captures renamed without changing pixels.']};
const label=process.argv[2]??'verification';assert.match(label,/^[a-z0-9-]+$/);
await fs.writeFile(path.join(out,label+'.json'),JSON.stringify(report,null,2)+'\n',{flag:'wx'});
console.log(JSON.stringify({status:report.status,scopedFix:report.scopedFix,renderedCases:cases.length,tests:counters.passed,originalNativePins:watched.length,treeFiles:trees.length}));

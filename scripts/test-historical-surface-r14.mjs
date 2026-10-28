// Verify exact identities/hash, cache, fallback and actual material render state.
import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import { pathToFileURL } from 'node:url';
const root=path.resolve(import.meta.dirname,'..'),out=path.join(root,'artifacts/foliage-transform-r14');
const www=path.join(out,'source-work/src/SceneWorkbench.Web/wwwroot');
globalThis.fetch=async route=>{assert.ok(route.startsWith('/ui/')&&!route.includes('..'));return new Response(await fs.readFile(path.join(www,route)));};
const {historicalPlantState}=await import(pathToFileURL(path.join(www,'ui/historical-surface.js')));
const {createMaterial}=await import(pathToFileURL(path.join(www,'ui/material-adapters.js')));
const T=await import(pathToFileURL(path.join(www,'vendor/three.module.js')));
const pin=JSON.parse(await fs.readFile(path.join(www,'ui/surface-adapters.json'))).historicalPlant;
const graph=JSON.parse(await fs.readFile(path.join(out,'api-after.json'))).graph;
const snapshot=graph.materials.find(m=>m.shaderGuid===pin.guid),saved=JSON.stringify(snapshot);
const bytes=await fs.readFile(path.join(www,pin.source));let requests=0;const warnings=[];
const assets={white:new T.DataTexture(new Uint8Array([255,255,255,255]),1,1),
  fetch:async route=>{assert.equal(route,pin.source);requests++;return new Response(bytes);},
  texture:async()=>({texture:assets.white,meta:{columns:1,rows:1}}),warn:m=>warnings.push(m)};
assert.equal(await historicalPlantState({...snapshot,shaderGuid:'other'},assets,pin),null);
assert.equal(await historicalPlantState({...snapshot,shaderFileId:0},assets,pin),null);
assert.equal(await historicalPlantState({...snapshot,shaderPath:'Assets/new.shadergraph',shaderHash:'new'},assets,pin),null);
assert.equal(await historicalPlantState({...snapshot,properties:[]},assets,pin),null);
assert.equal(await historicalPlantState(snapshot,assets,{...pin,renderFace:'Front'}),null);assert.equal(requests,0);
assert.equal(await historicalPlantState(snapshot,assets,pin),pin);assert.equal(await historicalPlantState(snapshot,assets,pin),pin);assert.equal(requests,1);
const badAssets={...assets,historicalSources:new Map()};assert.equal(await historicalPlantState(snapshot,badAssets,{...pin,sha256:'0'.repeat(64)}),null);
const material=await createMaterial(snapshot,assets);assert.equal(material.side,T.DoubleSide);assert.equal(material.transparent,false);assert.equal(material.depthWrite,true);
assert.equal(material.userData.previewMode,'approximation');assert.equal(material.userData.historicalRenderState.sha256,pin.sha256);
assert.ok(material.fragmentShader.includes('if(alpha<0.500000)discard;'));assert.ok(material.userData.adapterStatus.includes('歷史同 GUID'));
assert.equal(JSON.stringify(snapshot),saved,'native snapshot is not mutated');
const staleCutoff=structuredClone(snapshot);staleCutoff.properties.find(p=>p.name==='_AlphaCutoff').value=[.9];
const staleMaterial=await createMaterial(staleCutoff,assets);assert.ok(staleMaterial.fragmentShader.includes('if(alpha<0.500000)discard;'));
assert.equal(staleMaterial.userData.shadowCutout.cutoff,.5);assert.equal(staleCutoff.properties.find(p=>p.name==='_AlphaCutoff').value[0],.9);
const generic=await createMaterial({...snapshot,shaderPath:'Assets/new.shadergraph',shaderHash:'new'},assets);
assert.equal(material.vertexShader,generic.vertexShader,'historical state never adds billboard or vertex deformation');
const report={time:new Date().toISOString(),status:'VERIFIED_SCOPED_HISTORICAL_RENDER_STATE',
  matchedGuid:pin.guid,matchedSubassetId:pin.fileId,referenceHash:pin.sha256,knownReferenceValidated:true,
  identitySourcePresenceAndSignatureGuards:true,hashMismatchFallsBack:true,oneFetchForSharedReference:true,
  material:{side:material.side,transparent:material.transparent,depthWrite:material.depthWrite,alphaCutoff:true,mode:material.userData.previewMode,status:material.userData.adapterStatus},
  nativeSnapshotUnchanged:true,vertexShaderUnchanged:true,constantCutoffIgnoresStaleMaterial:true,scope:'Historical exact-GUID source reference only; no current-version equivalence, graph compilation, vertex deformation or billboard claim.'};
const label=process.argv[2]??'historical-surface-tests';assert.match(label,/^[a-z0-9-]+$/);
await fs.writeFile(path.join(out,label+'.json'),JSON.stringify(report,null,2)+'\n',{flag:'wx'});
material.dispose();staleMaterial.dispose();generic.dispose();assets.white.dispose();console.log(JSON.stringify({status:report.status,material:report.material}));

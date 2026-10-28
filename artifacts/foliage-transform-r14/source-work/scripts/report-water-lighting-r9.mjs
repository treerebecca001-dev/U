import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
import {fileURLToPath} from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..'),project=path.join(root,'Workspace/Baselines/Client231229-2022.3.13f1');
const out='artifacts/preview-r9/water-lighting-contract-fix2.json',read=async p=>JSON.parse(await fs.readFile(path.join(root,p),'utf8'));
const hash=async p=>crypto.createHash('sha256').update(await fs.readFile(p)).digest('hex');
assert.equal(await fs.stat(path.join(root,out)).then(()=>true,()=>false),false);
const graph=await read('artifacts/preview-r9/lighting-browser-ready/graph.json'),result=await read('artifacts/preview-r9/lighting-focused-source-formula/result.json');assert.equal(result.status,'PASS');
const env=graph.environment,cube=env.bakedReflection.cubemaps[0];assert.equal(env.bakedReflection.probeCount,0);assert.equal(env.bakedReflection.cubemaps.length,1);
assert.equal(await hash(path.join(project,cube.path)),cube.sha256);
const waters=graph.nodes.filter(n=>n.materials.some(p=>p.includes('/Water/Materials/'))),lights=graph.nodes.filter(n=>n.light?.type===1);
const inputs=new Set([graph.scenePath,graph.scenePath+'.meta',env.bakedReflection.path,env.bakedReflection.path+'.meta',cube.path,cube.path+'.meta',
 'Assets/ResourcesBuild/Scenes/Common/Light/Scene_Directional Light/Scene_Directional Light.prefab']);
for(const node of waters)for(const p of node.materials){inputs.add(p);inputs.add(p+'.meta');}
const native=[];for(const p of inputs)native.push({path:p,sha256:await hash(path.join(project,p))});
const contract={identity:'preview-r9-fix2',capturedUtc:new Date().toISOString(),nativeInputs:native,
 environment:env,waterRenderers:waters.map(n=>({id:n.id,layer:n.layer,renderer:n.renderer,materials:n.materials})),lights:lights.map(n=>({id:n.id,name:n.name,active:n.active,light:n.light})),
 gpuEvidence:'artifacts/preview-r9/lighting-focused-source-formula/result.json',gpu:result.probe,
 scope:'Exact native source identity and actual Map_60001 product SceneView dataflow. Baked default environment and single directional hard-shadow approximation; no native file writes or full Unity image parity claim.',
 reflectionFormula:'Original Lighting.hlsl AIR_RI=1.000293 and ForwardPass reflection strength/mask and lighting/composition order; actual Blur0 uses explicit LOD0. No foreign sky image.',
 shadowScope:'Native active source, masks, receives/casts flags and alpha-cutout caster depth. FromQuality uses tool fallback2048, not URP CSM quality reproduction. Native Terrain shadow mode0 preserved.',
 remaining:['Missing original TerrainLayer source','Full URP GI/CSM/Shadowmask and local probe blending','Unity import/pixel comparison/native baked data generation and writing remain separate gates']};
await fs.writeFile(path.join(root,out),JSON.stringify(contract,null,2)+'\n');console.log(JSON.stringify({path:out,nativeInputs:native.length,sha256:await hash(path.join(root,out))}));

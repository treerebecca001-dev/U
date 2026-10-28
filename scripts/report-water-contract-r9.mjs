import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const output=path.join(root,'artifacts/preview-r9/water-contract-fix1.json');
assert.equal(await fs.stat(output).then(()=>true,()=>false),false,'Evidence is immutable');
const graph=JSON.parse(await fs.readFile(path.join(root,'artifacts/preview-r9/water-browser/graph.json')));
const probe=JSON.parse(await fs.readFile(path.join(root,'artifacts/preview-r9/water-focused-verified/contract-probe.json')));
const project=path.join(root,'Workspace/Baselines/Client231229-2022.3.13f1');
const digest=async p=>crypto.createHash('sha256').update(await fs.readFile(p)).digest('hex');
const libraries='Assets/04.Packages/StylizedWater2/Shaders/Libraries/';
const references=[['Waves.hlsl','WaveInfo GetWaveInfo'],['Vertex.hlsl','GetWaveInfo('],['Features.hlsl','float SampleIntersection'],['ForwardPass.hlsl','float depth ='],['ForwardPass.hlsl','SampleIntersection(']];
const sourceReferences=[];
for(const [file,needle] of references){const text=await fs.readFile(path.join(project,libraries,file),'utf8');const line=text.split(/\r?\n/).findIndex(l=>l.includes(needle))+1;sourceReferences.push({path:libraries+file,sha256:await digest(path.join(project,libraries,file)),needle,line:line||null});}
const materials=[];
for(const m of graph.materials.filter(m=>m.previewKind==='water-template')){
 const effect=probe.effects.find(e=>e.path===m.path),numbers=Object.fromEntries(m.properties.filter(p=>p.value?.length).map(p=>[p.name,p.value]));
 assert.ok(effect);for(const [name,hash] of Object.entries(m.shaderDependencies))assert.equal(hash,await digest(path.join(project,libraries,name)));
 materials.push({path:m.path,materialSha256:await digest(path.join(project,m.path)),metaSha256:await digest(path.join(project,m.path+'.meta')),
  shaderPath:m.shaderPath,shaderGuid:m.shaderGuid,shaderFileId:m.shaderFileId,shaderHash:m.shaderHash,libraryHashes:m.shaderDependencies,
  validKeywords:m.keywords,nativeSavedNumbers:numbers,nativeTextures:m.properties.filter(p=>p.texturePath).map(p=>({name:p.name,path:p.texturePath,guid:p.textureGuid,fileId:p.textureFileId,scale:p.scale,offset:p.offset})),
  features:effect.features,observed:effect,readonly:m.editable===false});
}
await fs.writeFile(output,JSON.stringify({identity:'preview-r9-fix1',capturedUtc:new Date().toISOString(),
 scope:'Local source-pinned fixed approximation. No Unity/template compilation, original material edits or Unity pixel parity claim.',
 implementation:{depth:'Actual opaque scene depth; inverse camera matrices reconstruct world/eye depth.',waves:'Native Gerstner constants/count/fade/speed/height/direction and flatten mask; zero-speed lake remains still.',
  intersection:'Native enabled sharp intersection, noise, depth gradient, clipping, ripple and color.',normals:'Enabled native/distance sampling; both actual native strengths are zero.',
  translucency:'Simplified source color/mask/curve using native parameters; complete URP light/shadow pipeline unimplemented.',
  specular:'Approximation using native directional light, size, strength and distortion.',
  disabledInBoth:['surface foam','refraction','caustics','river mode','planar reflections'],
  remainingEnabledGaps:['environment/probe reflection (_EnvironmentReflectionsOn=1, strength=0.248)','complete URP lighting/GI','received shadows (_ReceiveShadows=1)']},
 sourceReferences,materials,verification:'water-focused-verified/result.json; water-browser/result.json; test-results/preview-r9-water.trx',
 unityStatus:'UNVERIFIED',nativeWriting:'Water template remains read-only.'},null,2)+'\n');
console.log(JSON.stringify({path:output,materials:materials.length,sha256:await digest(output)}));

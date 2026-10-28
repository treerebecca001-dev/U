// Offline source and native graph matrix audit; no Unity, asset writes or browser automation.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import { FBXLoader } from '../src/SceneWorkbench.Web/wwwroot/vendor/loaders/FBXLoader.js';
import { Loader, LoadingManager, Texture, Matrix4, Vector3, Quaternion } from '../src/SceneWorkbench.Web/wwwroot/vendor/three.module.js';
const root = path.resolve(import.meta.dirname, '..'), out = path.join(root,'artifacts/foliage-transform-r14');
await fs.mkdir(out,{recursive:true});
const project = path.join(root,'Workspace/Baselines/Client231229-2022.3.13f1');
const graph = JSON.parse(await fs.readFile(path.join(root,'artifacts/tree-input-r13/fixture-api-before-ready.json'))).graph;
const hash = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
class Placeholder extends Loader {load(){return new Texture();}}
const manager = new LoadingManager().addHandler(/.*/,new Placeholder());
const nodes = graph.nodes.filter(n=>n.meshPath && /\/Common\/(Trees|Grass)\//.test(n.meshPath));
const paths = [...new Set(nodes.map(n=>n.meshPath))]; const sources=[];
const coord = new Matrix4().makeScale(-1,1,-1);
for (const relative of paths) {
  const bytes=await fs.readFile(path.join(project,relative)),meta=await fs.readFile(path.join(project,relative+'.meta'));
  const text=meta.toString(), field=(name,fallback)=>Number(text.match(new RegExp('^    '+name+': (.+)$','m'))?.[1]??fallback);
  const loaded=new FBXLoader(manager).parse(bytes.buffer.slice(bytes.byteOffset,bytes.byteOffset+bytes.byteLength),'');loaded.updateMatrixWorld(true);
  const factor=field('globalScale',1)*(field('useFileScale',1)&&field('useFileUnits',1)?(loaded.userData.unitScaleFactor??1)*.01:.01);
  const models=[],meshes=[];
  loaded.traverse(n=>{if(n.userData.transformData)models.push(n);if(n.isMesh)meshes.push(n);});
  sources.push({path:relative,sha256:hash(bytes),metaSha256:hash(meta),factor,modelNodes:models.length,meshCount:meshes.length,
    sourceAxes:loaded.userData.sourceAxes,
    models:models.map(n=>({name:n.name,originalName:n.userData.originalName,sourceId:String(n.ID),parent:n.parent?.name,
      matrix:n.matrix.toArray(),matrixWorld:n.matrixWorld.toArray(),position:n.position.toArray(),rotation:n.quaternion.toArray(),scale:n.scale.toArray()})),
    instances:nodes.filter(n=>n.meshPath===relative).map(n=>({id:n.id,ownerId:n.ownerId,parentId:n.parentId,fileId:n.meshFileId,
      sourceGuid:n.meshGuid,native:n.fbxInstance?{transformId:n.fbxInstance.transformFileId,position:n.fbxInstance.position,rotation:n.fbxInstance.rotation,scale:n.fbxInstance.scale}:null})),
    meshes:meshes.map(n=>{n.geometry.computeBoundingBox();const old=coord.clone().multiply(new Matrix4().makeScale(factor,factor,factor)).multiply(n.matrixWorld);
      return{name:n.name,bounds:{localMin:n.geometry.boundingBox.min.toArray(),localMax:n.geometry.boundingBox.max.toArray()},
        previousBakedMatrix:old.toArray(),previousSourceOrigin:new Vector3().applyMatrix4(old).toArray()};})});
  loaded.traverse(n=>{n.geometry?.dispose();for(const m of Array.isArray(n.material)?n.material:[n.material])m?.dispose();});
}
const graphNodes=new Map(graph.nodes.map(n=>[n.id,n]));
const matrixCache=new Map();
function world(id){if(matrixCache.has(id))return matrixCache.get(id);const n=graphNodes.get(id);if(!n)return new Matrix4();
  const b=n.fbxInstance??n,local=new Matrix4().compose(new Vector3(b.position[0],b.position[1],-b.position[2]),new Quaternion(-b.rotation[0],-b.rotation[1],b.rotation[2],b.rotation[3]),new Vector3(...b.scale));
  const value=world(n.parentId).clone().multiply(local);matrixCache.set(id,value);return value;}
const representative = nodes.filter(n=>['222870','630463173'].includes(n.ownerId)||n.fbxInstance&&/\/Grass\//.test(n.meshPath)).slice(0,14).map(n=>({id:n.id,ownerId:n.ownerId,path:n.meshPath,parentId:n.parentId,meshFileId:n.meshFileId,native:n.fbxInstance,groupWorld:world(n.id).toArray(),chain:(()=>{let id=n.id,a=[];while(graphNodes.has(id)){const p=graphNodes.get(id);a.push({id:p.id,name:p.name,position:p.position,rotation:p.rotation,scale:p.scale});id=p.parentId;}return a;})()}));
const report={time:new Date().toISOString(),graphSource:'artifacts/tree-input-r13/fixture-api-before-ready.json',sources,representative,
  summary:{vegetationPaths:paths.length,instances:nodes.length,boundInstances:nodes.filter(n=>n.fbxInstance).length,singleModelPaths:sources.filter(s=>s.modelNodes===1&&s.meshCount===1).length,
    nonIdentitySingleModels:sources.filter(s=>s.modelNodes===1&&s.meshCount===1&&!new Matrix4().fromArray(s.models[0].matrixWorld).equals(new Matrix4())).map(s=>({path:s.path,instances:s.instances.length,nested:s.instances.filter(i=>i.native).length,sourceOrigin:s.meshes[0].previousSourceOrigin}))},
  limitation:'Raw geometric positions already contain source geometric transform per pinned FBXLoader. Model hierarchy TRS is separate. No Unity parity or guessed replacement identity asserted.',unityStarted:false,packaged:false};
await fs.writeFile(path.join(out,'source-transform-audit.json'),JSON.stringify(report,null,2)+'\n',{flag:'wx'});
console.log(JSON.stringify(report.summary));

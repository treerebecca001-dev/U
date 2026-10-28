// Offline source-FBX analysis using the pinned preview decoder, not a browser or Unity import.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
import { FBXLoader } from '../src/SceneWorkbench.Web/wwwroot/vendor/loaders/FBXLoader.js';
import { Loader, LoadingManager, Texture } from '../src/SceneWorkbench.Web/wwwroot/vendor/three.module.js';
const root = path.resolve(import.meta.dirname, '..'), out = path.join(root, 'artifacts/tree-input-r13');
const project = path.join(root, 'Workspace/Baselines/Client231229-2022.3.13f1');
const folder = 'Assets/ResourcesBuild/Scenes/Common/Trees/TreeA01';
const hash = data => crypto.createHash('sha256').update(data).digest('hex');
const attribute = a => a ? { count: a.count, itemSize: a.itemSize, componentType: a.array.constructor.name,
  sha256: hash(Buffer.from(a.array.buffer, a.array.byteOffset, a.array.byteLength)) } : null;
const models = [];
// Geometry-only offline mode: external texture decoding is outside this comparison.
class PlaceholderTextureLoader extends Loader { load() { return new Texture(); } }
const manager = new LoadingManager().addHandler(/.*/, new PlaceholderTextureLoader());
for (const name of ['TreeA01_leaves2.fbx', 'TreeA01_leaves_ce_f.fbx', 'TreeA01_trunk.fbx', 'TreeA01_trunk_ce_f.fbx']) {
  const file = path.join(project, folder, name), data = await fs.readFile(file);
  const result = new FBXLoader(manager).parse(data.buffer.slice(data.byteOffset, data.byteOffset + data.byteLength), '');
  result.updateMatrixWorld(true); const meshes = [];
  result.traverse(object => {
    if (!object.isMesh) return;
    const g = object.geometry; g.computeBoundingBox();
    meshes.push({ name: object.name, positions: attribute(g.getAttribute('position')), normals: attribute(g.getAttribute('normal')),
      uv: attribute(g.getAttribute('uv')), index: attribute(g.index), triangles: (g.index?.count ?? g.getAttribute('position').count) / 3,
      bounds: { minimum: g.boundingBox.min.toArray(), maximum: g.boundingBox.max.toArray() }, matrixWorld: object.matrixWorld.toArray() });
  });
  assert.equal(meshes.length, 1, name); models.push({ path: folder + '/' + name, sha256: hash(data), meshes });
}
const pairs = [[0, 1], [2, 3]].map(([a, b]) => ({ old: models[a].path, current: models[b].path,
  sourceFileEqual: models[a].sha256 === models[b].sha256,
  sourcePositionBufferEqual: models[a].meshes[0].positions.sha256 === models[b].meshes[0].positions.sha256,
  sourceUvBufferEqual: models[a].meshes[0].uv?.sha256 === models[b].meshes[0].uv?.sha256,
  sourceMeshMatrixEqual: JSON.stringify(models[a].meshes[0].matrixWorld) === JSON.stringify(models[b].meshes[0].matrixWorld) }));
const report = { time: new Date().toISOString(), decoder: 'Pinned ThreeFBXLoader0.180.0',
  decoderSha256: hash(await fs.readFile(path.join(root, 'src/SceneWorkbench.Web/wwwroot/vendor/loaders/FBXLoader.js'))), models, pairs,
  meaning: 'Geometry-only source comparison with unloaded placeholder textures; it does not compare material appearance, Unity imported identities or an intended migration/replacement of a historical Prefab target.',
  nativeWrite: false, unityStarted: false, packaging: false };
await fs.writeFile(path.join(out, 'tree-geometry-analysis.json'), JSON.stringify(report, null, 2) + '\n', { flag: 'wx' });
console.log(JSON.stringify({ models: models.map(m => ({ path: m.path, vertices: m.meshes[0].positions.count, triangles: m.meshes[0].triangles })), pairs }));

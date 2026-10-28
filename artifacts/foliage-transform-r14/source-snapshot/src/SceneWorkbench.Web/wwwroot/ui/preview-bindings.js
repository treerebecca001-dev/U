import { overrideFbxRoot } from './fbx-transforms.js';

// Root identity from pinned2021.3/2022.3 native single-Model correspondences.
// Structural uniqueness alone is not an imported fileID mapping.
const singleModelRootTransformId = '-8679921383154817045';

// A native FBX renderer ID stays a decimal string. No filename or geometry-order matching.
export function fbxInstanceBinding(instance, decoded) {
  if (!instance) return null;
  if (instance.renderers.length !== 1 || decoded.length !== 1) return { supported: false, reason: '來源 FBX Mesh／原生 Renderer 覆寫不唯一；保留來源幾何，未猜測材質或 Transform。' };
  let transform = null, geometry;
  if (instance.transformFileId) {
    if (instance.transformFileId !== singleModelRootTransformId) return { supported: false, reason: 'FBX Transform fileID 尚未在此版本的單一 Model 根節點對應中驗證；保留來源幾何，未猜測套用。' };
    const source = decoded[0];
    if (!source.sourceRoot || !source.rootGeometry) return { supported: false, reason: 'FBX 原生 Transform 覆寫無法唯一對應來源 Model 根節點；保留來源幾何，未疊加或猜測放置。' };
    transform = overrideFbxRoot(instance, source.sourceRoot);
    if (!transform) return { supported: false, reason: 'FBX 根節點覆寫缺少可驗證的逐軸資料，保留來源幾何；請同步服務與頁面版本。' };
    geometry = source.rootGeometry;
  }
  return { supported: true, renderer: instance.renderers[0], transform, geometry, visible: instance.active && instance.renderers[0].enabled };
}

// Every referenced preview group must have a Material, including diagnostic groups.
// Never reuse a known source slot to invent a binding for an unresolved submesh.
export function geometryMaterials(geometry, materials, missing) {
  const assigned = materials.slice(), missingSlots = [];
  const slots = geometry.groups.map(group => group.materialIndex);
  if (slots.some(slot => !Number.isInteger(slot) || slot < 0 || slot >= 1024)) throw new Error('模型材質分組超出支援範圍。');
  const last = slots.reduce((max, slot) => Math.max(max, slot), 0);
  for (let slot = 0; slot <= last; slot++) {
    if (!assigned[slot]) { assigned[slot] = missing(slot); missingSlots.push(slot); }
  }
  // No groups means one complete surface. Additional native slots stay referenced,
  // but their extra passes cannot be inferred from this decoded geometry.
  return { material: !slots.length ? assigned[0] : assigned,
    missingSlots, ungroupedExtraSlots: !slots.length ? Math.max(0, materials.length - 1) : 0 };
}


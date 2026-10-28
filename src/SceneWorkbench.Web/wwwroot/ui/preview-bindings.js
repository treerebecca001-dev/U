// A native FBX renderer ID stays a decimal string. No filename or geometry-order matching.
export function fbxInstanceBinding(instance, decoded) {
  if (!instance) return null;
  if (instance.renderers.length !== 1 || decoded.length !== 1) return { supported: false, reason: '來源 FBX Mesh／原生 Renderer 覆寫不唯一；保留來源幾何，未猜測材質或 Transform。' };
  return { supported: true, renderer: instance.renderers[0], transform: instance, visible: instance.active && instance.renderers[0].enabled };
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

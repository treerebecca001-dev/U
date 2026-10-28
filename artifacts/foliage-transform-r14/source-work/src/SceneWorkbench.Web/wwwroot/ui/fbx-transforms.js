// The FBX Model's placement and the Mesh's geometric transform have separate owners.
// FBXLoader has already applied the geometric transform to the vertices.
export function nativeFbxRoot(source, factor) {
  return { position: [-source.position.x * factor, source.position.y * factor, source.position.z * factor],
    rotation: [source.quaternion.x, -source.quaternion.y, -source.quaternion.z, source.quaternion.w],
    scale: source.scale.toArray() };
}

export function overrideFbxRoot(instance, source) {
  if (!Array.isArray(instance.transformProperties)) return null;
  const result = { position: source.position.slice(), rotation: source.rotation.slice(), scale: source.scale.slice() };
  for (const property of instance.transformProperties) {
    const match = /^m_Local(Position|Rotation|Scale)\.([xyzw])$/.exec(property);
    if (!match) return null;
    const field = match[1].toLowerCase(), index = 'xyzw'.indexOf(match[2]);
    if (index >= result[field].length || !Number.isFinite(instance[field]?.[index])) return null;
    result[field][index] = instance[field][index];
  }
  return result;
}

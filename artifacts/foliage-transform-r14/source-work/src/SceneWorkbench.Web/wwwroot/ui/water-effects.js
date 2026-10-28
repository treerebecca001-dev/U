export function waterFeatures(snapshot) {
  const keywords = new Set(snapshot.keywords), values = new Map(snapshot.properties.map(p => [p.name, p]));
  const number = (name, fallback = 0) => values.get(name)?.value[0] ?? fallback;
  return { normals: keywords.has('_NORMALMAP'), normalStrength: number('_NormalStrength'),
    distanceNormals: keywords.has('_DISTANCE_NORMALS'), waves: keywords.has('_WAVES'),
    sharpIntersection: keywords.has('_SHARP_INERSECTION'), smoothIntersection: keywords.has('_SMOOTH_INTERSECTION'),
    translucency: keywords.has('_TRANSLUCENCY'), foam: keywords.has('_FOAM'), caustics: keywords.has('_CAUSTICS'),
    refraction: keywords.has('_REFRACTION'), depth: !keywords.has('_DISABLE_DEPTH_TEX') && !number('_DisableDepthTexture'),
    environmentReflection: !keywords.has('_ENVIRONMENTREFLECTIONS_OFF') && number('_EnvironmentReflectionsOn', 1) > 0,
    receiveShadows: !keywords.has('_RECEIVE_SHADOWS_OFF') && !keywords.has('_UNLIT') && number('_ReceiveShadows',1)>0 && number('_LightingOn',1)>0,
    specular: !keywords.has('_SPECULARHIGHLIGHTS_OFF'), planarReflection: number('_PlanarReflectionsEnabled') > 0 };
}

export function waterPinMatches(snapshot, pin) {
  return snapshot.shaderGuid === pin.guid && snapshot.shaderHash === pin.sha256
    && Object.entries(pin.dependencies ?? {}).every(([name, hash]) => snapshot.shaderDependencies?.[name] === hash);
}

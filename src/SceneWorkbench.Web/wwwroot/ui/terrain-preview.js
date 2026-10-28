import * as THREE from '../vendor/three.module.js';

// The source grid is indexed [z, x]. Terrain's Unity +Z becomes viewport -Z.
export function terrainGeometry(heightmap) {
  const { resolution: n, samples, size } = heightmap;
  if (n < 2 || n > 1025 || samples.length !== n * n) throw new Error('原生 Terrain 高度樣本不完整，未產生替代平面。');
  const positions = new Float32Array(n * n * 3), uv = new Float32Array(n * n * 2), indices = new Uint32Array((n - 1) ** 2 * 6);
  for (let z = 0; z < n; z++) for (let x = 0; x < n; x++) {
    const i = z * n + x;
    if (!Number.isInteger(samples[i]) || samples[i] < 0 || samples[i] > 32766) throw new Error('未知 Terrain 高度格式。');
    positions.set([x / (n - 1) * size[0], samples[i] / 32766 * size[1], -z / (n - 1) * size[2]], i * 3);
    uv.set([x / (n - 1), z / (n - 1)], i * 2);
  }
  let index = 0;
  for (let z = 0; z < n - 1; z++) for (let x = 0; x < n - 1; x++) {
    const a = z * n + x, b = a + 1, c = a + n, d = c + 1;
    indices.set([a, b, c, b, d, c], index); index += 6;
  }
  const geometry = new THREE.BufferGeometry(); geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
  geometry.setAttribute('uv', new THREE.BufferAttribute(uv, 2)); geometry.setIndex(new THREE.BufferAttribute(indices, 1));
  geometry.computeVertexNormals(); geometry.computeBoundingBox(); return geometry;
}

export async function terrainSurface(data, assets) {
  if (data.holesSamples || data.holesLodSamples) throw new Error('Terrain holes 遮罩尚未解碼；保留高度資訊，不繪製假定無洞的表面。');
  const geometry = assets.ensureUvs(terrainGeometry(data.heightmap));
  const checker = new THREE.DataTexture(new Uint8Array([110, 104, 91, 255, 160, 153, 132, 255, 160, 153, 132, 255, 110, 104, 91, 255]), 2, 2);
  checker.wrapS = checker.wrapT = THREE.RepeatWrapping; checker.needsUpdate = true;
  assets.derivedTextures ??= new Set(); assets.derivedTextures.add(checker);
  if (data.controls.length !== 1 || data.layers.length > 4 || !data.layers.length) {
    assets.warn('Terrain 原生高度已讀取；alphamap／圖層混色尚未解析，使用明示的診斷棋盤。');
    const material = new THREE.MeshStandardMaterial({ map: checker, roughness: 1 }); assets.materials.add(material);
    return { geometry, material };
  }
  const control = data.controls[0], raw = Uint8Array.from(atob(control.rgba), c => c.charCodeAt(0));
  if (raw.length !== control.width * control.height * 4) throw new Error('Terrain alphamap 長度不符。');
  const weightTexture = new THREE.DataTexture(raw, control.width, control.height); weightTexture.needsUpdate = true;
  weightTexture.minFilter = weightTexture.magFilter = THREE.LinearFilter; assets.derivedTextures.add(weightTexture);
  const uniforms = { controlMap: { value: weightTexture } }; let declaration = 'uniform sampler2D controlMap;'; let mix = 'vec3 color=vec3(0.0);';
  for (let i = 0; i < data.layers.length; i++) {
    const layer = data.layers[i]; let texture = checker;
    if (layer.diffusePath) { try { texture = (await assets.texture(layer.diffusePath)).texture; } catch (error) { assets.warn(`TerrainLayer：${error.message}`); } }
    uniforms['layer' + i] = { value: texture };
    uniforms['tile' + i] = { value: new THREE.Vector4(data.heightmap.size[0] / layer.tileSize[0], data.heightmap.size[2] / layer.tileSize[1], layer.tileOffset[0] / layer.tileSize[0], layer.tileOffset[1] / layer.tileSize[1]) };
    declaration += `uniform sampler2D layer${i};uniform vec4 tile${i};`;
    mix += `color+=texture2D(layer${i},vUv*tile${i}.xy+tile${i}.zw).rgb*weight.${'rgba'[i]};`;
  }
  const material = new THREE.ShaderMaterial({ uniforms, toneMapped: false,
    vertexShader: 'varying vec2 vUv;varying vec3 vNormal;void main(){vUv=uv;vNormal=normalize(normalMatrix*normal);gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.0);}',
    fragmentShader: declaration + `varying vec2 vUv;varying vec3 vNormal;void main(){vec4 weight=texture2D(controlMap,vUv);${mix}color*=.55+.45*max(dot(normalize(vNormal),normalize(vec3(.4,.8,.5))),0.0);gl_FragColor=vec4(color,1.0);}` });
  material.userData = { previewMode: 'approximation', unresolvedLayers: data.layers.filter(layer => !layer.diffusePath).length,
    adapterStatus: 'Terrain 原生高度／RGBA alphamap／Diffuse Tiling 近似；未解析圖層顯示棋盤，URP Terrain Lit 光照、法線與材質特效尚未適配。' };
  assets.materials.add(material); return { geometry, material };
}

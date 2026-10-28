import * as THREE from '../vendor/three.module.js';
import { FBXLoader } from '../vendor/loaders/FBXLoader.js';
import { TGALoader } from '../vendor/loaders/TGALoader.js';
import { createMaterial } from './material-adapters.js';

export class AssetPreview {
  constructor(token, onWarning) {
    this.token = token; this.onWarning = onWarning; this.controller = new AbortController(); this.models = new Map(); this.textures = new Map(); this.materials = new Set(); this.geometries = new Set(); this.warnings = new Set();
    this.white = new THREE.DataTexture(new Uint8Array([255, 255, 255, 255]), 1, 1); this.white.needsUpdate = true;
  }
  warn(message) { if (!this.warnings.has(message)) { this.warnings.add(message); this.onWarning?.(message); } }
  async fetch(path) {
    const response = await fetch(path, { headers: { 'X-Workbench-Token': this.token }, signal: this.controller.signal });
    if (!response.ok) throw new Error((await response.json()).error ?? `預覽讀取失敗（${response.status}）`);
    return response;
  }
  async texture(path) {
    if (!this.textures.has(path)) this.textures.set(path, (async () => {
      const meta = await (await this.fetch(`/api/texture?path=${encodeURIComponent(path)}`)).json();
      const tga = path.toLowerCase().endsWith('.tga'), bmp = path.toLowerCase().endsWith('.bmp');
      const response = await this.fetch(`/api/${tga || bmp ? 'source' : 'image'}?path=${encodeURIComponent(path)}`);
      let texture;
      if (tga) {
        const buffer = await response.arrayBuffer();
        if (buffer.byteLength < 18) throw new Error('TGA 檔頭不完整。');
        const header = new DataView(buffer), width = header.getUint16(12, true), height = header.getUint16(14, true);
        if (!width || !height || width > 8192 || height > 8192 || width * height > 16777216) throw new Error('TGA 超過開發版 16 MP 預覽限制。');
        const data = new TGALoader().parse(buffer); texture = new THREE.DataTexture(data.data, data.width, data.height); texture.flipY = data.flipY;
      }
      else {
        const url = URL.createObjectURL(await response.blob());
        try { texture = await new THREE.TextureLoader().loadAsync(url); } finally { URL.revokeObjectURL(url); }
      }
      const wrap = (value) => value === 1 ? THREE.ClampToEdgeWrapping : value === 2 ? THREE.MirroredRepeatWrapping : THREE.RepeatWrapping;
      texture.wrapS = wrap(meta.wrapU); texture.wrapT = wrap(meta.wrapV);
      texture.magFilter = meta.filterMode === 0 ? THREE.NearestFilter : THREE.LinearFilter;
      texture.generateMipmaps = meta.mipmaps; texture.minFilter = !meta.mipmaps ? texture.magFilter : meta.filterMode === 0 ? THREE.NearestMipmapNearestFilter : meta.filterMode === 2 ? THREE.LinearMipmapLinearFilter : THREE.LinearMipmapNearestFilter;
      // This project's Gamma workflow samples source bytes in the fixed GLSL surface adapters.
      texture.colorSpace = THREE.NoColorSpace; texture.needsUpdate = true; texture.name = path;
      return { texture, meta };
    })().catch((error) => { this.textures.delete(path); throw error; }));
    return this.textures.get(path);
  }
  async material(snapshot) { const material = await createMaterial(snapshot, this); if (this.controller.signal.aborted) { material.dispose(); throw new DOMException('預覽已取消', 'AbortError'); } this.materials.add(material); return material; }
  releaseMaterial(material) { this.materials.delete(material); material?.dispose(); }
  ensureUvs(geometry) {
    const count = geometry.getAttribute('position').count;
    for (const key of ['uv', 'uv1', 'uv2', 'uv3']) if (!geometry.getAttribute(key)) geometry.setAttribute(key, new THREE.Float32BufferAttribute(new Float32Array(count * 2), 2));
    if (!geometry.getAttribute('normal')) geometry.computeVertexNormals();
    this.geometries.add(geometry); return geometry;
  }
  async geometry(path, fileId) {
    const key = `${path}:${fileId}`;
    if (!this.models.has(key)) this.models.set(key, (async () => {
      const info = await (await this.fetch(`/api/model?path=${encodeURIComponent(path)}&fileId=${encodeURIComponent(fileId)}`)).json();
      if (path.toLowerCase().endsWith('.fbx')) {
        const response = await this.fetch(`/api/source?path=${encodeURIComponent(path)}`);
        const manager = new THREE.LoadingManager();
        // The Unity .mat is authoritative. FBX embedded/absolute texture names never cause requests.
        manager.addHandler(/.*/, { path: '', setPath(value) { this.path = value; return this; }, load: () => this.white.clone() });
        const loaded = new FBXLoader(manager).parse(await response.arrayBuffer(), '');
        const axes = loaded.userData.sourceAxes ?? {};
        if (axes.UpAxis !== undefined && (axes.UpAxis !== 1 || axes.UpAxisSign !== 1)) throw new Error(`${path}：FBX 非 Y-up 軸向尚未校準，保留警示標記。`);
        const factor = info.globalScale * (info.useFileScale && info.useFileUnits ? (loaded.userData.unitScaleFactor ?? 1) * .01 : .01);
        if (!Number.isFinite(factor) || factor <= 0 || factor > 100000) throw new Error('FBX 匯入縮放無效。');
        const meshes = []; loaded.traverse((node) => { if (node.isMesh) meshes.push(node); });
        const unique = [...new Map(meshes.map((mesh) => [mesh.geometry.uuid, mesh])).values()];
        const mappedName = info.meshNames[fileId];
        const candidates = mappedName ? unique.filter((mesh) => [mesh.name, mesh.geometry.name, mesh.userData.originalName].includes(mappedName)) : unique;
        if (fileId !== '0' && candidates.length !== 1) throw new Error(`${path}：${unique.length} 個 Mesh 無法唯一對應 fileID ${fileId}，不猜測子資源。`);
        const convert = (mesh) => {
          const geometry = mesh.geometry.clone();
          // FBX right-handed coordinates -> Unity local left-handed X reflection -> viewport Z reflection.
          if (fileId === '0') { loaded.updateMatrixWorld(true); geometry.applyMatrix4(mesh.matrixWorld); }
          geometry.applyMatrix4(new THREE.Matrix4().makeScale(-factor, factor, -factor));
          if (info.swapUvChannels && geometry.getAttribute('uv1')) { const uv = geometry.getAttribute('uv'); geometry.setAttribute('uv', geometry.getAttribute('uv1')); geometry.setAttribute('uv1', uv); }
          this.ensureUvs(geometry); return { geometry, name: mesh.name, materialSlots: geometry.groups.length };
        };
        if (fileId === '0') this.warn(`${path}：讀取來源 FBX 幾何；場景只套用可唯一對應的原生覆寫，匯入軸向仍待 Unity 交叉驗證。`);
        else if (!mappedName) this.warn(`${path}：來源只有一個 Mesh，使用唯一幾何；Unity fileID 與匯入軸向仍待引擎交叉驗證。`);
        const result = fileId === '0' ? meshes.map(convert) : [convert(candidates[0])];
        loaded.traverse((node) => { node.geometry?.dispose(); const materials = Array.isArray(node.material) ? node.material : [node.material]; materials.forEach((m) => m?.dispose()); });
        return result;
      }
      const geometry = new THREE.BufferGeometry();
      for (const [name, values] of Object.entries(info.attributes)) {
        const size = values.length / info.vertexCount; const array = Float32Array.from(values);
        if (['position', 'normal', 'tangent'].includes(name)) for (let i = 2; i < array.length; i += size) array[i] *= -1;
        if (name === 'tangent' && size === 4) for (let i = 3; i < array.length; i += 4) array[i] *= -1;
        geometry.setAttribute(name, new THREE.BufferAttribute(array, size));
      }
      const indices = info.indices.slice(); for (let i = 0; i < indices.length; i += 3) [indices[i + 1], indices[i + 2]] = [indices[i + 2], indices[i + 1]];
      geometry.setIndex(indices); info.groups.forEach((group) => geometry.addGroup(group.start, group.count, group.materialIndex));
      geometry.computeBoundingBox(); this.ensureUvs(geometry);
      return [{ geometry, name: info.name, materialSlots: info.groups.length }];
    })().catch((error) => { this.models.delete(key); throw error; }));
    return this.models.get(key);
  }
  dispose() {
    this.controller.abort(); this.white.dispose(); this.materials.forEach((m) => m.dispose()); this.geometries.forEach((g) => g.dispose());
    this.textures.forEach((promise) => promise.then((data) => data.texture.dispose()).catch(() => {}));
    this.derivedTextures?.forEach((texture) => texture.dispose()); this.derivedTextures?.clear();
    this.environmentResources?.forEach(texture => texture.dispose()); this.environmentResources?.clear();
    this.models.clear(); this.textures.clear(); this.materials.clear(); this.geometries.clear();
  }
}

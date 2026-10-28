import * as THREE from '../vendor/three.module.js';
import { OrbitControls } from '../vendor/controls/OrbitControls.js';
import { TransformControls } from '../vendor/controls/TransformControls.js';
import { $ } from './controls.js';
import { fbxInstanceBinding, geometryMaterials } from './preview-bindings.js';
import { unresolvedMaterial } from './material-adapters.js';
import { terrainSurface } from './terrain-preview.js';
import { WaterDepthPass } from './water-depth.js';
import { WaterShadowPass, nativeShadowApplies, nativeLightApplies } from './water-shadow.js';
import { loadEnvironmentPreview } from './environment-preview.js';
import { TransformTools } from './transform-tools.js';

export class SceneView {
  constructor(onPick, onTransform, onError = () => {}) {
    this.onPick = onPick; this.nodes = new Map(); this.hitTargets = []; this.selected = null; this.editingAllowed = true;
    const host = $('viewport');
    try { this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false }); }
    catch {
      $('renderer-state').textContent = 'WebGL 不可用 · 仍可編輯屬性';
      $('transform-status').textContent = 'WebGL 不可用，請使用 Inspector 的 Transform 數值編輯。';
      for (const id of ['view-tool', 'translate', 'rotate', 'scale', 'transform-space', 'snap-toggle', 'snap-step']) {
        $(id).disabled = true; $(id).removeAttribute('data-init-disabled');
      }
      return;
    }
    const css = getComputedStyle(document.documentElement);
    this.scene = new THREE.Scene(); this.scene.background = new THREE.Color(css.getPropertyValue('--color-canvas').trim());
    this.camera = new THREE.PerspectiveCamera(50, 1, .05, 100000); this.camera.position.set(12, 10, 14);
    this.renderer.setPixelRatio(Math.min(devicePixelRatio, 2)); this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    host.prepend(this.renderer.domElement);
    this.resetOrbitInteraction(new THREE.Vector3(0, 1, 0));
    this.scene.add(new THREE.HemisphereLight(0xffffff, 0x606b7a, 2));
    const sun = new THREE.DirectionalLight(0xffffff, 2); sun.position.set(4, 10, 5); this.scene.add(sun);
    this.grid = new THREE.GridHelper(100, 100, 0x666666, 0x393939); this.scene.add(this.grid);
    this.root = new THREE.Group(); this.scene.add(this.root);
    this.gizmo = new TransformControls(this.camera, this.renderer.domElement); this.gizmo.setMode('translate'); this.gizmo.setSize(.85);
    this.scene.add(this.gizmo.getHelper());
    this.gizmo.addEventListener('dragging-changed', (event) => { this.controls.enabled = !event.value; });
    const changed = () => { if (this.waterDepth) this.waterDepth.dirty = true; if (this.waterShadow) this.waterShadow.dirty = true; };
    this.gizmo.addEventListener('objectChange', changed);
    this.transformTools = new TransformTools(this.gizmo, { commit: onTransform, changed, failed: onError, cancelled: () => this.resetOrbitInteraction() });
    const ray = new THREE.Raycaster(), point = new THREE.Vector2(); let down;
    this.renderer.domElement.addEventListener('pointerdown', (event) => { down = { x: event.clientX, y: event.clientY }; });
    this.renderer.domElement.addEventListener('pointerup', (event) => {
      if (event.button !== 0 || !down || Math.hypot(event.clientX - down.x, event.clientY - down.y) > 4 || this.gizmo.axis) return;
      const bounds = this.renderer.domElement.getBoundingClientRect();
      point.set((event.clientX - bounds.left) / bounds.width * 2 - 1, -(event.clientY - bounds.top) / bounds.height * 2 + 1);
      ray.setFromCamera(point, this.camera);
      const visibleTargets = this.hitTargets.filter((target) => { for (let object = target; object; object = object.parent) if (!object.visible) return false; return true; });
      const hit = ray.intersectObjects(visibleTargets, false)[0];
      if (hit) this.onPick(hit.object.userData.id);
    });
    host.addEventListener('keydown', (event) => { if (!event.isComposing && !event.ctrlKey && !event.metaKey && !event.altKey && event.key.toLowerCase() === 'f') { event.preventDefault(); this.focus(); } });
    this.selectionBox = new THREE.BoxHelper(new THREE.Object3D(), 0x83bdf7); this.selectionBox.visible = false; this.scene.add(this.selectionBox);
    this.resize = new ResizeObserver(() => {
      const width = host.clientWidth, height = host.clientHeight;
      if (!width || !height) return;
      this.renderer.setSize(width, height, false); this.camera.aspect = width / height; this.camera.updateProjectionMatrix();
    }); this.resize.observe(host);
    this.waterDepth = new WaterDepthPass(this.renderer);
    this.waterShadow = new WaterShadowPass(this.renderer);
    this.renderer.setAnimationLoop(() => {
      if (this.selectionBox.visible) this.selectionBox.update();
      this.currentWaterLight=this.nativeWaterLight();
      this.waterShadow.render(this.scene,this.root,this.currentWaterLight,[this.grid,this.selectionBox,this.gizmo.getHelper(),...(this.helperGroups??[])]);
      this.waterDepth.render(this.scene, this.camera, this.root, [this.grid, this.selectionBox, this.gizmo.getHelper(), ...(this.helperGroups ?? [])]);
      this.renderer.render(this.scene, this.camera);
      Object.assign(host.dataset,{shadowCasters:String(this.waterShadow.casterCount??0),shadowResolution:String(this.waterShadow.resolution??0),shadowLight:this.currentWaterLight?.id??''});
    });
    $('renderer-state').textContent = 'WebGL 2';
    $('grid-toggle').addEventListener('change', (event) => { this.grid.visible = event.target.checked; });
    $('helpers-toggle').addEventListener('change', () => { for (const group of this.helperGroups ?? []) group.visible = group.userData.nativeActive && $('helpers-toggle').checked; });
    $('focus-object').addEventListener('click', () => this.focus());
    $('frame-scene').addEventListener('click', () => this.frameAll());
  }
  observeCamera() {
    $('viewport').dataset.previewCamera = JSON.stringify({ position: this.camera.position.toArray(), rotation: this.camera.quaternion.toArray(), target: this.controls.target.toArray() });
  }
  resetOrbitInteraction(target = this.controls?.target.clone()) {
    // OrbitControls also saw the pointerdown that began the gizmo drag. Simply
    // reenabling it after Escape would consume the remaining move as an orbit.
    // Discard that unfinished input state using the public lifecycle, retaining
    // the current camera and target rather than resetting the user's view.
    const position = this.camera.position.clone(), rotation = this.camera.quaternion.clone(), zoom = this.camera.zoom;
    this.controls?.dispose();
    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.enableDamping = false;
    if (target) this.controls.target.copy(target);
    this.camera.position.copy(position); this.camera.quaternion.copy(rotation); this.camera.zoom = zoom;
    this.controls.addEventListener('change', () => this.observeCamera()); this.controls.update(); this.observeCamera();
  }
  setItems(items, reframe = false) {
    if (!this.renderer) return;
    if (this.waterDepth) this.waterDepth.dirty = true;
    if (this.waterShadow) this.waterShadow.dirty = true;
    if (this.graph) {
      for (const item of items) {
        const group = this.nodes.get(item.id); if (!group || item.isPrefab) continue;
        group.position.set(item.position[0], item.position[1], -item.position[2]);
        group.quaternion.set(-item.rotation[0], -item.rotation[1], item.rotation[2], item.rotation[3]);
        group.scale.fromArray(item.scale); group.userData.nativeLayer=item.layer;group.userData.nativeActive = item.active; group.visible = item.active && (!group.userData.editorHelper || $('helpers-toggle').checked); group.userData.editable = item.editable; group.userData.editableFields = item.editableFields;
      }
      this.scene.updateMatrixWorld(true); this.select(this.selected); return;
    }
    this.gizmo.detach(); this.selectionBox.visible = false;
    this.root.traverse((object) => { object.geometry?.dispose(); if (object.material) object.material.dispose(); });
    this.root.clear(); this.nodes.clear(); this.hitTargets = [];
    const visibleItems = items.slice(0, 5000); const itemMap = new Map(visibleItems.map((item) => [item.id, item]));
    for (const item of visibleItems) {
      const group = new THREE.Group(); group.userData = { id: item.id, editable: item.editable, editableFields: item.editableFields };
      group.position.set(item.position[0], item.position[1], -item.position[2]);
      group.quaternion.set(-item.rotation[0], -item.rotation[1], item.rotation[2], item.rotation[3]);
      group.scale.fromArray(item.scale); group.visible = item.active;
      const cube = item.meshFileId === '10202' && item.meshGuid?.startsWith('0000000000000000');
      const size = cube ? [1, 1, 1] : item.colliderSize;
      const color = cube ? (item.name.includes('地板') ? 0x686f75 : 0x9baebc) : 0x968367;
      const geometry = size ? new THREE.BoxGeometry(...size.map((v) => Math.max(.001, Math.abs(v)))) : new THREE.OctahedronGeometry(.13);
      const mesh = new THREE.Mesh(geometry, new THREE.MeshStandardMaterial({ color, roughness: .85, wireframe: !cube }));
      mesh.userData.id = item.id;
      if (!cube && size && item.colliderCenter) mesh.position.set(item.colliderCenter[0], item.colliderCenter[1], -item.colliderCenter[2]);
      group.add(mesh); this.hitTargets.push(mesh); this.nodes.set(item.id, group);
    }
    for (const item of visibleItems) {
      const group = this.nodes.get(item.id); const visited = new Set([item.id]); let parentId = item.parentId, safe = true;
      while (itemMap.has(parentId)) { if (visited.has(parentId)) { safe = false; break; } visited.add(parentId); parentId = itemMap.get(parentId).parentId; }
      const parent = safe ? this.nodes.get(item.parentId) : null;
      (parent ?? this.root).add(group);
    }
    this.scene.updateMatrixWorld(true); this.select(this.selected);
    if (reframe) this.frameAll();
    if (items.length > 5000) $('renderer-state').textContent = '預覽前 5,000 物件';
  }
  clearGraph() {
    this.graph = null; this.generation = (this.generation ?? 0) + 1;
    if (!this.renderer) return;
    this.waterDepth.dispose(); this.waterDepth.dirty = true;
    this.waterShadow.dispose();this.lightCandidates=[];
    this.transformTools.select(null);
    this.gizmo.detach(); this.selectionBox.visible = false;
    this.root.clear(); this.nodes.clear(); this.hitTargets = [];
  }
  async setGraph(graph, assets, items, reframe = true) {
    if (!this.renderer) return;
    this.clearGraph(); const generation = this.generation; this.graph = graph;
    const itemMap = new Map(items.map((item) => [item.id, item]));
    const groups = new Map(); this.materialMeshes = new Map(); this.helperGroups = []; this.terrainNodes = new Map();
    for (const node of graph.nodes) {
      const group = new THREE.Group(); group.name = node.name; group.userData = { id: node.ownerId, editable: Boolean(itemMap.get(node.id)?.editable),nativeLayer:node.layer??0 };
      group.position.set(node.position[0], node.position[1], -node.position[2]); group.quaternion.set(-node.rotation[0], -node.rotation[1], node.rotation[2], node.rotation[3]); group.scale.fromArray(node.scale); group.visible = node.active;
      // Project-specific authoring overlay; explicit view toggle, never a native scene edit.
      if (node.materials.some((path) => /^Assets\/02\.Arts\/Scene\/Collider\/Materials\//.test(path))) {
        group.userData.editorHelper = true; group.userData.nativeActive = node.active; group.visible = node.active && $('helpers-toggle').checked; this.helperGroups.push(group);
      }
      groups.set(node.id, group); if (node.id === node.ownerId) this.nodes.set(node.ownerId, group);
    }
    for (const node of graph.nodes) {
      const group = groups.get(node.id); const seen = new Set([node.id]); let parent = node.parentId, safe = true;
      while (groups.has(parent)) { if (seen.has(parent)) { safe = false; break; } seen.add(parent); parent = graph.nodes.find((n) => n.id === parent)?.parentId; }
      (safe && groups.get(node.parentId) || this.root).add(group);
    }
    this.root.updateMatrixWorld(true);this.lightCandidates=graph.nodes.filter(node=>node.light?.type===1).map(node=>({node,group:groups.get(node.id)}));
    assets.waterLight=this.nativeWaterLight();assets.environmentPreview=await loadEnvironmentPreview(graph.environment,assets);
    if(generation!==this.generation)return;
    $('viewport').dataset.environmentReflection=String(assets.environmentPreview.available);
    if(assets.environmentPreview.available)assets.warn(assets.environmentPreview.status+' '+assets.environmentPreview.reference.path);
    const materials = new Map(await Promise.all(graph.materials.map(async (snapshot) => [snapshot.path, await assets.material(snapshot)])));
    if (generation !== this.generation) return;
    let count = 0, triangles = 0, fbxBindings = 0, unresolved = 0, approximations = 0, terrainCount = 0, terrainTriangles = 0, unresolvedTerrainLayers = 0;
    await Promise.all(graph.nodes.map(async (node) => {
      if (node.terrain) {
        const data = graph.terrains?.find(t => t.path === node.terrain.path && t.fileId === node.terrain.fileId);
        if (data) this.terrainNodes.set(node.ownerId, { data, reference: node.terrain, group: groups.get(node.id) });
        if (!node.terrain.enabled || !node.terrain.drawHeightmap) return;
        if (!data) { assets.warn(`${node.name}：原生 TerrainData 尚未讀取，未產生替代平面。`); return; }
        try {
          const { geometry, material } = await terrainSurface(data, assets); if (generation !== this.generation) return;
          const mesh = new THREE.Mesh(geometry, material); mesh.userData = { id: node.ownerId, sourcePath: data.path, fileId: data.fileId, terrain: true,nativeRenderer:node.renderer };
          groups.get(node.id).add(mesh); this.hitTargets.push(mesh); terrainCount++; count++; approximations++;
          unresolvedTerrainLayers += material.userData.unresolvedLayers ?? data.layers.length;
          const faces = geometry.index.count / 3; triangles += faces; terrainTriangles += faces;
          assets.warn(material.userData.adapterStatus ?? 'Terrain 高度與混色限制見 Inspector。');
        } catch (error) { if (error.name !== 'AbortError') assets.warn(`${node.name}：${error.message}`); }
        return;
      }
      if (node.meshFileId === '0' && !node.meshPath) return;
      try {
        let decoded;
        if (node.meshGuid.startsWith('0000000000000000')) {
          const geometry = node.meshFileId === '10202' ? new THREE.BoxGeometry(1, 1, 1) : node.meshFileId === '10207' ? new THREE.SphereGeometry(.5, 24, 16) : node.meshFileId === '10210' ? new THREE.PlaneGeometry(1, 1) : null;
          if (!geometry) throw new Error(`內建 Mesh ${node.meshFileId} 尚未對應。`);
          // BoxGeometry's six per-face material groups are generated by this preview;
          // they are not native Unity material slots for the built-in primitive.
          geometry.clearGroups();
          decoded = [{ geometry: assets.ensureUvs(geometry) }];
        } else if (node.meshPath) decoded = await assets.geometry(node.meshPath, node.meshFileId);
        else throw new Error('找不到模型來源。');
        if (generation !== this.generation) return;
        let paths = node.materials; const binding = fbxInstanceBinding(node.fbxInstance, decoded);
        if (binding?.supported) {
          if (!binding.visible) { groups.get(node.id).visible = false; return; }
          const group = groups.get(node.id), transform = binding.transform;
          if (transform) {
            group.position.set(transform.position[0], transform.position[1], -transform.position[2]);
            group.quaternion.set(-transform.rotation[0], -transform.rotation[1], transform.rotation[2], transform.rotation[3]); group.scale.fromArray(transform.scale);
          }
          fbxBindings++; group.userData.nativeFbxBinding = binding;
        } else if (binding) { paths = []; assets.warn(`${node.name}：${binding.reason}`); }
        for (const model of decoded) {
          const assigned = paths.map((path) => materials.get(path) ?? unresolvedMaterial(path || node.name));
          if (!assigned.length) { assigned.push(unresolvedMaterial(node.name)); assets.warn(`${node.name}：未解析原生材質槽，保留實心診斷幾何。`); }
          const geometry = binding?.supported && binding.geometry || model.geometry;
          const bound = geometryMaterials(geometry, assigned, slot => unresolvedMaterial(`${node.name}／材質槽 ${slot}`));
          if (bound.missingSlots.length) assets.warn(`${node.name}：材質槽 ${bound.missingSlots.join('、')} 未解析，使用診斷表面保留子網格。`);
          if (bound.ungroupedExtraSlots) assets.warn(`${node.name}：模型沒有子網格分組，額外 ${bound.ungroupedExtraSlots} 個原生材質槽保留參照；額外繪製尚未預覽。`);
          const displayed = Array.isArray(bound.material) ? bound.material : [bound.material];
          if (displayed.some(m => m.userData.previewMode === 'unresolved')) unresolved++;
          if (displayed.some(m => m.userData.previewMode === 'approximation')) approximations++;
          const mesh = new THREE.Mesh(geometry, bound.material);
          mesh.userData = { id: node.ownerId, materialPaths: paths, sourcePath: node.meshPath, fileId: node.meshFileId, nativeFbxBinding: binding?.supported ? binding.renderer : null };
          mesh.userData.previewNodeId = node.id; mesh.userData.rootTransformSeparated = Boolean(binding?.supported && binding.geometry);
          mesh.userData.nativeRenderer=binding&&!binding.supported?null:node.renderer;
          mesh.onBeforeRender=(_renderer,_scene,_camera,_geometry,material)=>{
            if(!material.userData.water)return;const native=mesh.userData.nativeRenderer;
            if(nativeLightApplies(native,mesh.parent.userData.nativeLayer??0,this.currentWaterLight)){material.uniforms.uLightDirection.value.copy(this.currentWaterLight.direction);material.uniforms.uLightColor.value.copy(this.currentWaterLight.color);}
            else{material.uniforms.uLightDirection.value.set(0,0,0);material.uniforms.uLightColor.value.set(0,0,0);}
            material.uniforms.uRendererReceiveShadows.value=native?.receiveShadows&&nativeShadowApplies(native,mesh.parent.userData.nativeLayer??0,this.currentWaterLight)?1:0;
            material.uniforms.uRendererProbeUsage.value=native?.reflectionProbeUsage>0?1:0;material.uniformsNeedUpdate=true;
          };
          groups.get(node.id).add(mesh); this.hitTargets.push(mesh); count++; triangles += (geometry.index?.count ?? geometry.attributes.position.count) / 3;
          paths.forEach((path) => { if (!this.materialMeshes.has(path)) this.materialMeshes.set(path, []); this.materialMeshes.get(path).push(mesh); });
        }
      } catch (error) {
        if (error.name === 'AbortError' || generation !== this.generation) return;
        assets.warn(`${node.name}：${error.message}`);
        const mesh = new THREE.Mesh(new THREE.OctahedronGeometry(.13), new THREE.MeshBasicMaterial({ color: 0xd8a3cf, wireframe: true }));
        mesh.userData.id = node.ownerId; groups.get(node.id).add(mesh); this.hitTargets.push(mesh);
      }
    }));
    if (generation !== this.generation) return;
    this.setItems(items); if (reframe) this.frameAll();
    $('viewport').dataset.modelCount = String(count); $('viewport').dataset.triangles = String(Math.round(triangles));
    Object.assign($('viewport').dataset, { fbxBindings: String(fbxBindings), unresolvedMaterials: String(unresolved), approximations: String(approximations), terrainCount: String(terrainCount), terrainTriangles: String(terrainTriangles), unresolvedTerrainLayers: String(unresolvedTerrainLayers) });
    $('renderer-state').textContent = `WebGL 2 · ${count} 網格 · ${Math.round(triangles).toLocaleString('zh-TW')} 三角形` + (terrainCount ? ` · ${terrainCount} 地形` : '') + (approximations || unresolved ? ` · ${approximations} 近似／${unresolved} 未解析材質` : '') + (unresolvedTerrainLayers ? ` · ${unresolvedTerrainLayers} 地形圖層未解析` : '');
  }
  terrainDetails(ownerId) {
    const entry = this.terrainNodes?.get(ownerId); if (!entry) return null;
    const origin = entry.group.getWorldPosition(new THREE.Vector3());
    return { ...entry, worldOrigin: [origin.x, origin.y, -origin.z] };
  }
  nativeWaterLight(){
    for(const {node,group} of this.lightCandidates??[]){
      let visible=true;for(let ancestor=group;ancestor;ancestor=ancestor.parent)if(!ancestor.visible)visible=false;
      if(visible)return {...node.light,id:node.id,direction:new THREE.Vector3(0,0,1).applyQuaternion(group.getWorldQuaternion(new THREE.Quaternion())).normalize(),color:new THREE.Vector3(...node.light.color).multiplyScalar(node.light.intensity)};
    }
    return null;
  }
  async updateMaterial(snapshot, assets) {
    this.waterShadow.dirty=true;this.waterDepth.dirty=true;
    const generation = this.generation; const material = await assets.material(snapshot);
    if (generation !== this.generation) return;
    for (const mesh of this.materialMeshes?.get(snapshot.path) ?? []) {
      const paths = mesh.userData.materialPaths;
      if (Array.isArray(mesh.material)) mesh.material = mesh.material.map((old, i) => paths[i] === snapshot.path ? material : old);
      else if (paths[0] === snapshot.path) mesh.material = material;
    }
  }
  select(id) {
    this.selected = id;
    if (!this.renderer) return;
    const group = this.nodes.get(id); this.selectionBox.visible = Boolean(group);
    if (group) this.selectionBox.setFromObject(group);
    this.transformTools.select(group);
    // Bounded, read-only provenance for a selected preview. This observes the
    // rendered matrices/bounds and never changes source assets or placement.
    const geometry = [];
    group?.updateWorldMatrix(true, true);
    group?.traverse(object => {
      if (!object.isMesh || !object.userData.sourcePath || geometry.length >= 8) return;
      object.geometry.computeBoundingBox();
      const local = object.geometry.boundingBox, world = local.clone().applyMatrix4(object.matrixWorld);
      geometry.push({ nodeId: object.userData.previewNodeId, path: object.userData.sourcePath, fileId: object.userData.fileId,
        rootTransformSeparated: object.userData.rootTransformSeparated, matrixWorld: object.matrixWorld.toArray(),
        localBounds: [local.min.toArray(), local.max.toArray()], worldBounds: [world.min.toArray(), world.max.toArray()],
        materials: (Array.isArray(object.material) ? object.material : [object.material]).map(material => ({ path: material.name,
          side: material.side, historical: material.userData.historicalRenderState ?? null })) });
    });
    $('viewport').dataset.selectionGeometry = JSON.stringify({ id, meshes: geometry });
  }
  setGizmoEnabled(enabled) { this.transformTools?.setTool(enabled ? 'translate' : null); }
  setEditingAllowed(allowed) { this.editingAllowed = allowed; this.transformTools?.setAllowed(allowed); }
  focus() {
    if (!this.renderer) return;
    const group = this.nodes.get(this.selected); if (!group) return;
    const box = new THREE.Box3().setFromObject(group); this.frame(box);
  }
  frameAll() {
    if (!this.renderer || !this.nodes.size) return;
    const box = new THREE.Box3(); this.root.updateMatrixWorld(true);
    this.root.traverseVisible((object) => { if (object.geometry) { object.geometry.computeBoundingBox(); box.union(object.geometry.boundingBox.clone().applyMatrix4(object.matrixWorld)); } });
    this.frame(box);
  }
  frame(box) {
    if (box.isEmpty()) return;
    const center = box.getCenter(new THREE.Vector3()); const size = Math.max(box.getSize(new THREE.Vector3()).length(), 3);
    this.controls.target.copy(center); this.camera.position.copy(center).add(new THREE.Vector3(.8, .6, 1).normalize().multiplyScalar(size * 1.25)); this.controls.update();
  }
}

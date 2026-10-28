import * as THREE from '../vendor/three.module.js';

// Scene depth is derived locally from existing geometry, never from guessed shoreline data.
export class WaterDepthPass {
  constructor(renderer) { this.renderer = renderer; this.dirty = true; }
  render(scene, camera, root, helpers) {
    const materials = new Set(), water = [];
    root.traverse(object => {
      if (!object.isMesh) return;
      const list = Array.isArray(object.material) ? object.material : [object.material];
      if (list.some(m => m.userData.water)) { water.push(object); list.filter(m => m.userData.water).forEach(m => materials.add(m)); }
    });
    if (![...materials].some(m => m.userData.waterNeedsDepth)) return;
    const size = this.renderer.getDrawingBufferSize(new THREE.Vector2());
    if (!this.target || this.target.width !== size.x || this.target.height !== size.y) {
      this.target?.dispose(); this.target = new THREE.WebGLRenderTarget(size.x, size.y);
      this.target.depthTexture = new THREE.DepthTexture(size.x, size.y, THREE.UnsignedIntType); this.dirty = true;
    }
    camera.updateMatrixWorld(); const signature = [...camera.matrixWorld.elements, ...camera.projectionMatrix.elements].join(',');
    if (signature !== this.signature) { this.signature = signature; this.dirty = true; }
    if (this.dirty) {
      const hidden = [...water, ...helpers.filter(Boolean)], visible = hidden.map(o => o.visible), previous = this.renderer.getRenderTarget();
      try { hidden.forEach(o => { o.visible = false; }); this.renderer.setRenderTarget(this.target); this.renderer.render(scene, camera); }
      finally { this.renderer.setRenderTarget(previous); hidden.forEach((o, i) => { o.visible = visible[i]; }); }
      this.dirty = false;
    }
    for (const material of materials) {
      const u = material.uniforms; u.uSceneDepth.value = this.target.depthTexture; u.uDepthAvailable.value = 1;
      u.uViewportSize.value.copy(size); u.uProjectionInverse.value.copy(camera.projectionMatrixInverse); u.uViewInverse.value.copy(camera.matrixWorld);
    }
  }
  dispose() { this.target?.dispose(); this.target = null; }
}

import * as THREE from '../vendor/three.module.js';
import { OrbitControls } from '../vendor/controls/OrbitControls.js';

// Shared, disposable Inspector viewport for material balls and source model geometry.
export class PreviewSurface {
  constructor(host) {
    this.renderer = new THREE.WebGLRenderer({ antialias: true }); this.renderer.setPixelRatio(Math.min(devicePixelRatio, 2)); host.append(this.renderer.domElement);
    this.scene = new THREE.Scene(); this.scene.background = new THREE.Color(getComputedStyle(document.documentElement).getPropertyValue('--color-canvas').trim());
    this.root = new THREE.Group(); this.scene.add(this.root); this.camera = new THREE.PerspectiveCamera(40, 1, .01, 100000); this.camera.position.set(0, 0, 3.5);
    this.controls = new OrbitControls(this.camera, this.renderer.domElement); this.controls.enablePan = false; this.controls.addEventListener('change', () => this.render());
    this.resize = new ResizeObserver(() => { if (!host.clientWidth || !this.renderer) return; this.renderer.setSize(host.clientWidth, host.clientHeight, false); this.camera.aspect = host.clientWidth / host.clientHeight; this.camera.updateProjectionMatrix(); this.render(); }); this.resize.observe(host);
  }
  add(object) { this.root.add(object); this.render(); }
  frame() {
    const box = new THREE.Box3().setFromObject(this.root); if (box.isEmpty()) return;
    const center = box.getCenter(new THREE.Vector3()), size = Math.max(.1, box.getSize(new THREE.Vector3()).length());
    this.controls.target.copy(center); this.camera.position.copy(center).add(new THREE.Vector3(.4, .25, 1).normalize().multiplyScalar(size * 1.5)); this.controls.update(); this.render();
  }
  render() { if (this.renderer) this.renderer.render(this.scene, this.camera); }
  dispose() {
    this.resize.disconnect(); this.controls.dispose();
    // dispose releases GPU objects; loseContext also releases the browser's context slot.
    this.renderer.dispose(); this.renderer.forceContextLoss(); this.renderer = null;
    this.root.clear();
  }
}

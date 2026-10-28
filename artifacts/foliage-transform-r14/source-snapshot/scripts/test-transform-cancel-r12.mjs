// Production TransformTools + actual pinned Three objects; simulated unit events.
// These checks explicitly do not stand in for trusted mid-gesture browser input.
import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import { pathToFileURL } from 'node:url';
import * as THREE from '../src/SceneWorkbench.Web/wwwroot/vendor/three.module.js';
import { TransformControls } from '../src/SceneWorkbench.Web/wwwroot/vendor/controls/TransformControls.js';
const root = path.resolve(import.meta.dirname, '..');
const baseline = process.argv.includes('--baseline');
const labelIndex = process.argv.indexOf('--label'); const label = labelIndex < 0 ? '' : process.argv[labelIndex + 1];
assert.match(label ?? '', /^[a-z0-9-]*$/);
const source = path.join(root, baseline ? 'artifacts/transform-r10/source-snapshot-fix1/src/SceneWorkbench.Web/wwwroot/ui/transform-tools.js' : 'src/SceneWorkbench.Web/wwwroot/ui/transform-tools.js');
const { TransformTools } = await import(pathToFileURL(source));
class Element {
  constructor() { this.listeners = new Map(); this.dataset = {}; this.style = {}; this.children = []; this.value = ''; this.checked = false; }
  addEventListener(type, listener) { const list = this.listeners.get(type) ?? []; list.push(listener); this.listeners.set(type, list); }
  removeEventListener(type, listener) { this.listeners.set(type, (this.listeners.get(type) ?? []).filter(value => value !== listener)); }
  fire(type, values = {}) { const event = { type, isTrusted: false, timeStamp: performance.now(), key: '', target: this, preventDefault() {}, ...values }; for (const listener of this.listeners.get(type) ?? []) listener(event); }
  setAttribute(name, value) { this[name] = value; }
  replaceChildren(...values) { this.children = values; }
  append(...values) { this.children.push(...values); }
  get selectedOptions() { return this.children.filter(child => child.value === this.value); }
  closest() { return null; }
  getBoundingClientRect() { return { x: 0, y: 0, left: 0, top: 0, width: 800, height: 600 }; }
  setPointerCapture() {}
  releasePointerCapture() {}
}
function fixture() {
  const elements = new Map(); const document = new Element(); const window = new Element();
  Object.assign(document, { getElementById(id) { if (!elements.has(id)) elements.set(id, new Element()); return elements.get(id); },
    createElement() { return new Element(); }, querySelector() { return null; } });
  globalThis.document = document; globalThis.window = window;
  const canvas = new Element(); canvas.ownerDocument = document;
  const camera = new THREE.PerspectiveCamera(); const gizmo = new TransformControls(camera, canvas);
  const scene = new THREE.Scene(); const object = new THREE.Group(); scene.add(object);
  object.position.set(1, 2, -3); object.quaternion.setFromAxisAngle(new THREE.Vector3(0, 1, 0), .4); object.scale.set(-1, 2, 3);
  object.userData = { id: '301', editable: true, editableFields: ['position', 'rotation', 'scale'] };
  const commits = []; const errors = [];
  const tools = new TransformTools(gizmo, { commit(...args) { commits.push(args); }, changed() {}, failed(error) { errors.push(error); } });
  tools.select(object);
  function begin(tool = 'translate') {
    tools.setTool(tool); canvas.fire('pointerdown', { pointerId: 1, buttons: 1 });
    gizmo.axis = 'X'; gizmo.dragging = true; gizmo.dispatchEvent({ type: 'mouseDown' });
  }
  function move() { canvas.fire('pointermove', { pointerId: 1, buttons: 1 }); object.position.set(5, 6, -7); object.quaternion.set(0, 0, 1, 0); object.scale.set(-4, 5, 6); gizmo.dispatchEvent({ type: 'objectChange' }); }
  function release() { canvas.fire('pointerup', { pointerId: 1, buttons: 0 }); gizmo.pointerUp({ button: 0 }); }
  const before = { position: object.position.toArray(), rotation: object.quaternion.toArray(), scale: object.scale.toArray() };
  function restored() {
    assert.deepEqual(object.position.toArray(), before.position); assert.deepEqual(object.quaternion.toArray(), before.rotation); assert.deepEqual(object.scale.toArray(), before.scale);
    assert.equal(gizmo.dragging, false); assert.equal(gizmo.axis, null); assert.equal(commits.length, 0); assert.equal(errors.length, 0);
    release(); assert.equal(commits.length, 0);
  }
  return { document, window, canvas, object, gizmo, tools, commits, begin, move, release, restored, elements };
}
const checks = [
  ['Escape during changed drag while text input remains focused', () => {
    const f = fixture(); f.begin(); f.move();
    f.document.fire('keydown', { key: 'Escape', target: { closest() { return {}; } } }); f.restored();
  }],
  ['Escape restores position, quaternion and negative scale with no release commit', () => {
    const f = fixture(); f.begin('rotate'); f.move(); f.document.fire('keydown', { key: 'Escape' }); f.restored();
  }],
  ['Pointer cancellation restores changed preview', () => {
    const f = fixture(); f.begin('scale'); f.move(); f.canvas.fire('pointercancel', { pointerId: 1 }); f.restored();
  }],
  ['Window focus loss restores changed preview', () => {
    const f = fixture(); f.begin(); f.move(); f.window.fire('blur'); f.restored();
  }],
  ['Document visibility loss restores changed preview', () => {
    const f = fixture(); f.begin(); f.move(); f.document.hidden = true; f.document.fire('visibilitychange'); f.restored();
  }],
  ['Composition owns Escape until composition finishes', () => {
    const f = fixture(); f.begin(); f.move(); f.document.fire('keydown', { key: 'Escape', isComposing: true });
    assert.equal(f.gizmo.dragging, true); assert.equal(f.commits.length, 0);
    f.document.fire('keydown', { key: 'Escape', isComposing: false }); f.restored();
  }],
  ['A new completed gesture still commits exactly once after cancellation', () => {
    const f = fixture(); f.begin(); f.move(); f.document.fire('keydown', { key: 'Escape' }); f.restored();
    f.begin(); f.object.position.x = 8; f.gizmo.dispatchEvent({ type: 'objectChange' }); f.release();
    assert.deepEqual(f.commits, [['301', 'position', [8, 2, 3]]]);
  }],
  ['Keyboard shortcuts inside inputs do not change the selected tool', () => {
    const f = fixture(); f.document.fire('keydown', { key: 'e', target: { closest() { return {}; } } }); assert.equal(f.tools.tool, 'translate'); assert.equal(f.commits.length, 0);
  }]
];
const results = [];
for (const [name, check] of checks) {
  try { check(); results.push({ name, status: 'PASS' }); }
  catch (error) { results.push({ name, status: 'FAIL', error: error.stack }); }
}
const report = { baseline, time: new Date().toISOString(), source: path.relative(root, source), sourceSha256: crypto.createHash('sha256').update(await fs.readFile(source)).digest('hex'),
  scope: 'Simulated unit events with production controller and real Three transforms; not trusted browser timing evidence', results };
const out = path.join(root, 'artifacts/prefab-cancel-r12', (baseline ? 'cancel-unit-baseline' : 'cancel-unit') + (label ? '-' + label : '') + '.json');
await fs.writeFile(out, JSON.stringify(report, null, 2) + '\n', { flag: 'wx' });
console.log(JSON.stringify({ baseline, checks: results.length, passed: results.filter(r => r.status === 'PASS').length, failed: results.filter(r => r.status === 'FAIL').length, out }));
if (results.some(r => r.status === 'FAIL')) process.exitCode = 1;

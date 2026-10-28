// Integration checks against the actual pinned Three Raycaster, not a replacement picker.
import * as THREE from '../src/SceneWorkbench.Web/wwwroot/vendor/three.module.js';
import { geometryMaterials } from '../src/SceneWorkbench.Web/wwwroot/ui/preview-bindings.js';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
const directions = [[1,0,0],[-1,0,0],[0,1,0],[0,-1,0],[0,0,1],[0,0,-1]];
const material = label => { const m = new THREE.MeshBasicMaterial({ side: THREE.DoubleSide }); m.name = label; return m; };
function hits(mesh) {
  mesh.updateMatrixWorld(true);
  return directions.map(values => {
    const origin = new THREE.Vector3(...values).multiplyScalar(3);
    return new THREE.Raycaster(origin, origin.clone().negate().normalize()).intersectObject(mesh);
  });
}
const first = material('native-slot-0'), second = material('native-slot-1');
const broken = new THREE.Mesh(new THREE.BoxGeometry(), [first]);
assert.throws(() => hits(broken), /side/);
const checks = [], run = (name, fn) => { fn(); checks.push({ name, status: 'PASS' }); };
run('Built-in primitive complete surface is pickable from all six directions', () => {
  const geometry = new THREE.BoxGeometry(); geometry.clearGroups();
  const result = geometryMaterials(geometry, [first], slot => material('missing-' + slot));
  assert.ok(hits(new THREE.Mesh(geometry, result.material)).every(h => h.length > 0));
  assert.equal(result.material, first); assert.deepEqual(result.missingSlots, []);
});
run('Missing submesh slots retain all faces and keep both explicit source bindings', () => {
  const geometry = new THREE.BoxGeometry(), native = [first, second];
  const result = geometryMaterials(geometry, native, slot => material('missing-' + slot));
  assert.deepEqual(result.missingSlots, [2,3,4,5]); assert.equal(native.length, 2);
  assert.equal(result.material[0], first); assert.equal(result.material[1], second);
  const intersections = hits(new THREE.Mesh(geometry, result.material)); assert.ok(intersections.every(h => h.length > 0));
  const hitSlots = new Set(intersections.map(h => h[0].face.materialIndex)); assert.equal(hitSlots.size, 6);
  for (const slot of [2,3,4,5]) { assert.notEqual(result.material[slot], first); assert.notEqual(result.material[slot], second); }
});
run('No native material still permits picking; extra ungrouped slots stay explicit and bounded indices reject', () => {
  const geometry = new THREE.BoxGeometry(); geometry.clearGroups();
  const missing = geometryMaterials(geometry, [], slot => material('missing-' + slot));
  assert.ok(hits(new THREE.Mesh(geometry, missing.material)).every(h => h.length > 0));
  const extra = geometryMaterials(geometry, [first, second], slot => material('missing-' + slot));
  assert.equal(extra.material, first); assert.equal(extra.ungroupedExtraSlots, 1);
  for (const index of [-1, 1024, 1.5, NaN]) {
    const invalid = geometry.clone(); invalid.addGroup(0, 3, index);
    assert.throws(() => geometryMaterials(invalid, [first], () => second), /支援範圍/);
  }
});
run('A grouped single material keeps the decoded submesh triangle bounds', () => {
  const geometry = new THREE.BoxGeometry(); geometry.clearGroups(); geometry.addGroup(0, 6, 0);
  const result = geometryMaterials(geometry, [first], () => second);
  assert.equal(hits(new THREE.Mesh(geometry, result.material)).filter(h => h.length > 0).length, 2);
});
const label = process.argv[2] ?? 'picking-integration'; assert.match(label, /^[a-z0-9-]+$/);
const out = new URL('../artifacts/transform-r10/' + label + '.json', import.meta.url);
await fs.writeFile(out, JSON.stringify({ status: 'PASS', baselineRaycastFailureReproduced: true, checks,
  scope: 'Actual Three geometry/material binding and six-sided raycasts; browser selection recorded separately.' }, null, 2) + '\n', { flag: 'wx' });
console.log(JSON.stringify({ status: 'PASS', checks: checks.length, baselineFailureReproduced: true }));

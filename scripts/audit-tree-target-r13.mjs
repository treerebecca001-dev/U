// Bounded historical/source inspection. No project mutation, name-based retargeting or Unity.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const root = path.resolve(import.meta.dirname, '..'), out = path.join(root, 'artifacts/tree-input-r13');
const folder = 'Assets/ResourcesBuild/Scenes/Common/Trees/TreeA01';
const hash = data => crypto.createHash('sha256').update(data).digest('hex');
const projects = ['Client230411', 'Client', 'Client231229'].map(name => ({ name, root: 'D:/Work_ART/漂流NFT/@WLN_work/' + name }));
projects.push({ name: 'prepared-E', root: path.join(root, 'Workspace/Baselines/Client231229-2022.3.13f1') });
const files = [], texts = new Map();
for (const project of projects) {
  const dir = path.join(project.root, folder);
  for (const name of (await fs.readdir(dir)).sort()) {
    if (!/\.(?:fbx|prefab|mat)(?:\.meta)?$/i.test(name)) continue;
    const absolute = path.join(dir, name), data = await fs.readFile(absolute);
    const row = { project: project.name, path: folder + '/' + name, absolute, bytes: data.length, sha256: hash(data) };
    if (name.endsWith('.meta')) row.guid = data.toString('utf8').match(/^guid: ([a-f0-9]{32})$/m)?.[1] ?? null;
    if (!/\.fbx$/i.test(name)) texts.set(project.name + '/' + name, data.toString('utf8'));
    files.push(row);
  }
}
function documents(text) {
  const starts = [...text.matchAll(/^--- !u!(\d+) &(-?\d+)( stripped)?\r?$/gm)];
  return new Map(starts.map((m, i) => [m[2], { classId: Number(m[1]), stripped: Boolean(m[3]),
    body: text.slice(m.index + m[0].length, starts[i + 1]?.index ?? text.length) }]));
}
const old = documents(texts.get('Client230411/TreeA01.prefab')), current = documents(texts.get('prepared-E/TreeA01.prefab'));
const target = '5565035281593725506', oldTarget = old.get(target);
assert.equal(oldTarget.classId, 4); assert.equal(oldTarget.stripped, true); assert.equal(current.has(target), false);
const oldSource = oldTarget.body.match(/m_CorrespondingSourceObject: \{fileID: (-?\d+), guid: ([a-f0-9]{32})/);
const oldInstance = oldTarget.body.match(/m_PrefabInstance: \{fileID: (-?\d+)\}/)[1];
assert.equal(oldInstance, '5381875668383798697'); assert.equal(oldSource[2], '78b4ac0deff380946a311d7c61f3bc07');
const oldAsset = files.filter(f => f.project === 'prepared-E' && f.guid === oldSource[2]);
assert.equal(oldAsset.length, 1); assert.ok(oldAsset[0].path.endsWith('TreeA01_leaves2.fbx.meta'));
const currentDependencies = [...current.entries()].filter(([, d]) => d.classId === 1001).map(([id, d]) => {
  const guid = d.body.match(/m_SourcePrefab: \{fileID: 100100000, guid: ([a-f0-9]{32})/)[1];
  const meta = files.find(f => f.project === 'prepared-E' && f.guid === guid); assert.ok(meta);
  return { instance: id, guid, model: meta.path.replace(/\.meta$/, ''),
    transformTargets: [...new Set([...d.body.matchAll(/target: \{fileID: (-?\d+), guid: [a-f0-9]{32}[^\n]*\n\s+propertyPath: m_Local/g)].map(m => m[1]))] };
});
assert.equal(currentDependencies.length, 2); assert.ok(!currentDependencies.some(d => d.instance === oldInstance || d.guid === oldSource[2]));
const modelNames = ['TreeA01_leaves2.fbx', 'TreeA01_leaves_ce_f.fbx', 'TreeA01_trunk.fbx', 'TreeA01_trunk_ce_f.fbx'];
const modelComparison = modelNames.map(name => ({ name, variants: files.filter(f => f.path.endsWith('/' + name)),
  metadata: files.filter(f => f.path.endsWith('/' + name + '.meta')) }));
const scenePath = 'Assets/02.Arts/Scene/Map/Map_60001/Map_60001.unity';
const native = await fs.readFile(path.join(projects.at(-1).root, scenePath)); const scene = documents(native.toString('utf8'));
const owner = scene.get('630463173'); assert.equal(owner.classId, 1001);
const changes = [...owner.body.matchAll(new RegExp('target: \\{fileID: ' + target + ', guid: 96aec1dc668e08d4f80af4cf499db35c[^\\n]*\\n\\s+propertyPath: ([^\\n]+)\\n\\s+value: ([^\\n]+)', 'g'))]
  .map(m => ({ property: m[1].trim(), value: m[2].trim() }));
assert.deepEqual(changes, [{ property: 'm_LocalPosition.x', value: '-0.2' }, { property: 'm_LocalPosition.z', value: '0.87' }]);
const result = { time: new Date().toISOString(), status: 'VERIFIED_HISTORICAL_TARGET_AND_CURRENT_DEPENDENCY_CHANGE',
  files, oldTarget: { fileId: target, classId: oldTarget.classId, instance: oldInstance,
    sourceFileId: oldSource[1], sourceGuid: oldSource[2], model: oldAsset[0].path.replace(/\.meta$/, ''),
    assetStillExistsInCurrentProject: true, referencedByCurrentTreePrefab: false },
  currentDependencies, modelComparison, scene: { path: scenePath, sha256: hash(native), owner: '630463173', unchangedOverrides: changes },
  disposition: 'Keep current unresolved-target and exact native overrides; do not identify a replacement by name/child order or restore a historical Prefab.',
  remaining: 'Current imported virtual target completeness and intended replacement/appearance require authoritative mapping or engine/author evidence.',
  nativeWrite: false, unityStarted: false, packaging: false };
await fs.writeFile(path.join(out, 'tree-target-audit.json'), JSON.stringify(result, null, 2) + '\n', { flag: 'wx' });
console.log(JSON.stringify({ status: result.status, inspectedFiles: files.length, oldModel: result.oldTarget.model,
  oldAssetStillPresent: true, currentDependencies, originalOverrides: changes }));

import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const root = path.resolve(import.meta.dirname, '..'), out = path.join(root, 'artifacts/prefab-cancel-r12');
const project = path.join(root, 'Workspace/Baselines/Client231229-2022.3.13f1');
const hash = data => crypto.createHash('sha256').update(data).digest('hex');
const json = async relative => JSON.parse(await fs.readFile(path.join(root, relative), 'utf8'));
const old = await json('artifacts/terrain-recovery-r11/graph.json'), current = await json('artifacts/prefab-cancel-r12/client-graph.json');
const native = await fs.readFile(path.join(project, 'Assets/02.Arts/Scene/Map/Map_60001/Map_60001.unity'), 'utf8');
function docs(text) {
  const headers = [...text.matchAll(/^--- !u!(\d+) &(-?\d+)( stripped)?\r?$/gm)];
  return new Map(headers.map((m, index) => [m[2], { type: Number(m[1]), stripped: Boolean(m[3]), body: text.slice(m.index + m[0].length, headers[index + 1]?.index ?? text.length) }]));
}
const scene = docs(native), instance = scene.get('1764236792').body;
assert.match(instance, /m_SourcePrefab: \{fileID: 100100000, guid: e6b2760a6975a194c90ccb3a6ceffa61/);
const rows = [
  ['4391027932898413374', '3112415062092470202', 'ff83b63e9391b0d4eb2fea27e5753137', 1],
  ['5132720836648353441', '5835121169829274149', 'e4d0a9db46345244ea74c94e1fd4eae0', 1],
  ['6685076069268142894', '5406497572763445162', 'dca205a23611ba64a837c800530fa464', 0],
  ['7917458375259260575', '8800565630691940891', 'ecd6b0ea3d96a8043a8645adfb810189', 1]
];
const expected = structuredClone(old), changes = [];
for (const [target, nested, guid, value] of rows) {
  const expression = new RegExp('target: \\{fileID: '+target+', guid: e6b2760a6975a194c90ccb3a6ceffa61[^}]*\\}\\s+propertyPath: m_CastShadows\\s+value: '+value+'\\s');
  assert.match(instance, expression);
  const id = '1764236792/' + nested + '/fbx', actual = current.nodes.find(n => n.id === id), prior = expected.nodes.find(n => n.id === id);
  assert.equal(actual.fbxInstance.sourceGuid, guid); assert.equal(actual.renderer.fileId, '-7511558181221131132');
  assert.equal(actual.renderer.castShadows, value); assert.equal(actual.fbxInstance.renderers[0].castShadows, value);
  const detail = current.prefabOverrides.find(d => d.targetFileId === target);
  assert.equal(detail.status, 'resolved'); assert.equal(detail.method, 'validated-nested-xor63');
  assert.equal(detail.nestedInstanceId, nested); assert.equal(detail.sourceGuid, guid); assert.equal(detail.sourceFileId, '-7511558181221131132');
  changes.push({ id, nativeTarget: target, before: prior.renderer.castShadows, after: value, sourceGuid: guid });
  prior.renderer.castShadows = value; prior.fbxInstance.renderers[0].castShadows = value;
}
// Treat JSON numeric -0 as 0 only; never normalize decimal fileID strings, bytes or hashes.
const numericZero = value => JSON.parse(JSON.stringify(value, (_, item) => typeof item === 'number' && Object.is(item, -0) ? 0 : item));
for (const field of ['scenePath', 'nodes', 'materials', 'terrains', 'environment']) assert.deepEqual(numericZero(current[field]), numericZero(expected[field]), field);
assert.equal(current.diagnostics.length, 2);
const bodyPath = 'Assets/Tourist/TouristAssets/woman/body.prefab', body = docs(await fs.readFile(path.join(project, bodyPath), 'utf8'));
assert.equal(body.size, 125); assert.equal(body.has('5094106267881264939'), false);
assert.equal([...body.values()].some(o => o.type === 1001), false);
assert.equal(current.prefabOverrides.find(d => d.targetFileId === '5094106267881264939').status, 'unused-removal');
const parent = await fs.readFile(path.join(project, 'Assets/Tourist/TouristWoman.prefab'), 'utf8');
assert.match(parent, /m_RemovedComponents:\s+- \{fileID: 5094106267881264939, guid: c8ddeb1250a8d084cb0ca06da0ed4076/);
const treePath = 'Assets/ResourcesBuild/Scenes/Common/Trees/TreeA01/TreeA01.prefab';
const tree = docs(await fs.readFile(path.join(project, treePath), 'utf8'));
assert.equal(tree.has('5565035281593725506'), false);
const legacyRoot = 'D:/Work_ART/漂流NFT/@WLN_work/Client230411';
const legacy = await fs.readFile(path.join(legacyRoot, treePath)), legacyMeta = await fs.readFile(path.join(legacyRoot, treePath+'.meta'));
assert.equal(hash(legacy), '2def726686224cd2a7db1e19d6585728e1a3d7ee622170c67dbc6c4665dbe702');
assert.match(legacyMeta.toString('utf8'), /guid: 96aec1dc668e08d4f80af4cf499db35c/);
const legacyTarget = docs(legacy.toString('utf8')).get('5565035281593725506');
assert.equal(legacyTarget.type, 4); assert.equal(legacyTarget.stripped, true);
assert.match(legacyTarget.body, /m_CorrespondingSourceObject: \{fileID: -8679921383154817045, guid: 78b4ac0deff380946a311d7c61f3bc07/);
assert.match(legacyTarget.body, /m_PrefabInstance: \{fileID: 5381875668383798697\}/);
assert.equal(tree.has('5381875668383798697'), false);
assert.equal([...tree.values()].some(o => o.type === 1001 && o.body.includes('guid: 78b4ac0deff380946a311d7c61f3bc07')), false);
const treeDetail = current.prefabOverrides.find(d => d.targetFileId === '5565035281593725506');
assert.equal(treeDetail.status, 'unresolved-target'); assert.deepEqual(treeDetail.properties, ['m_LocalPosition.x', 'm_LocalPosition.z']);
const nativeBefore = await json('artifacts/prefab-cancel-r12/native-before.json');
for (const file of nativeBefore.files) assert.equal(hash(await fs.readFile(file.absolute)), file.sha256, file.absolute);
const sourceOut = path.join(out, 'legacy-source'); await fs.mkdir(sourceOut, { recursive: false });
await fs.writeFile(path.join(sourceOut, 'TreeA01.prefab'), legacy, { flag: 'wx' });
await fs.writeFile(path.join(sourceOut, 'TreeA01.prefab.meta'), legacyMeta, { flag: 'wx' });
const report = { status: 'VERIFIED_SCOPED_API', time: new Date().toISOString(), changes,
  exactGraphComparison: 'Only four native shadow override values (two graph locations each) allowed; nodes/materials/environment/terrains otherwise equal; JSON numeric negative zero only normalized',
  body: { status: 'UNUSED_REMOVAL_IDENTIFIED', sourceObjectCount: 125, target: '5094106267881264939', nativeChange: false },
  tree: { status: 'UNRESOLVED_CURRENT_TARGET_HISTORICAL_PROVENANCE_FOUND', target: '5565035281593725506', legacyPrefabSha256: hash(legacy),
    oldNestedInstance: '5381875668383798697', oldModelGuid: '78b4ac0deff380946a311d7c61f3bc07', overrideApplied: false, nativeChange: false,
    note: 'Historical removed source identity is proven; no Unity-native unused-override classification or remapping to current trees is claimed.' },
  nativeFilesUnchanged: nativeBefore.files.length, independentReview: 'PENDING', unityStarted: false, packaging: false };
await fs.writeFile(path.join(out, 'prefab-verification.json'), JSON.stringify(report, null, 2) + '\n', { flag: 'wx' });
console.log(JSON.stringify(report));

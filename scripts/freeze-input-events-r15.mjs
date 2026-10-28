// Freeze ordinary QA files for independent review. No archive, build, packaging or Unity.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const root = path.resolve(import.meta.dirname, '..'), out = path.join(root, 'artifacts/input-events-r15');
const handoff = path.join(out, 'handoff'); await fs.mkdir(handoff);
const hash = b => crypto.createHash('sha256').update(b).digest('hex');
const fixture = JSON.parse(await fs.readFile(path.join(out, 'fixture-manifest.json')));
for (const f of fixture.files) assert.equal(hash(await fs.readFile(path.join(fixture.fixtureRoot, f.path))), f.sha256, f.path);
const sourceFiles = ['scripts/prepare-input-events-r15.mjs', 'scripts/capture-input-events-r15.mjs',
  'scripts/start-input-events-r15.ps1', 'scripts/input-events-r15-landing.cjs', 'scripts/verify-input-events-r15.mjs',
  'scripts/freeze-input-events-r15.mjs', 'docs/HANDOFF_INPUT_EVENTS_R15.zh-TW.md', 'docs/APPROVED_PLAN.zh-TW.md'];
const source = [];
for (const p of sourceFiles) {
  const bytes = await fs.readFile(path.join(root, p)), target = path.join(out, 'source-snapshot', p);
  await fs.mkdir(path.dirname(target), { recursive: true }); await fs.writeFile(target, bytes, { flag: 'wx' });
  source.push({ path: p, bytes: bytes.length, sha256: hash(bytes) });
}
const evidence = [];
for (const file of (await fs.readdir(out, { withFileTypes: true })).filter(f => f.isFile()).map(f => f.name).sort()) {
  const bytes = await fs.readFile(path.join(out, file)); evidence.push({ path: file, bytes: bytes.length, sha256: hash(bytes) });
}
const runtimePath = 'artifacts/foliage-transform-r14/handoff/runtime.json';
const runtime = JSON.parse(await fs.readFile(path.join(root, runtimePath)));
const nativePath = 'artifacts/foliage-transform-r14/handoff/native-input-manifest.json';
const bodies = {
  'fixture-manifest.json': fixture,
  'source-manifest.json': { root: 'source-snapshot', files: source },
  'runtime-reference.json': { path: runtimePath, sha256: hash(await fs.readFile(path.join(root, runtimePath))), runtimeRoot: 'artifacts/foliage-transform-r14', binaries: runtime.binaries },
  'native-input-reference.json': { path: nativePath, sha256: hash(await fs.readFile(path.join(root, nativePath))) },
  'evidence-manifest.json': { files: evidence }
};
const pins = [];
for (const [p, body] of Object.entries(bodies)) {
  const bytes = Buffer.from(JSON.stringify(body, null, 2) + '\n');
  await fs.writeFile(path.join(handoff, p), bytes, { flag: 'wx' }); pins.push({ path: p, sha256: hash(bytes) });
}
const summary = { time: new Date().toISOString(), status: 'UNVERIFIED', review: 'PENDING', fixtureFiles: fixture.files.length,
  unchangedParentSources: fixture.parentSourceFiles - 1, productionModulesChanged: 0, handoffSourceFiles: source.length,
  runtimeFiles: runtime.binaries.length, evidenceFiles: evidence.length, manifests: pins, unityStarted: false, packaged: false };
await fs.writeFile(path.join(handoff, 'handoff.json'), JSON.stringify(summary, null, 2) + '\n', { flag: 'wx' });
console.log(JSON.stringify(summary));

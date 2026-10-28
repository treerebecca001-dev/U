// Repair only the explicit QA fixture grid after adding an extra toolbar row.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const root = path.resolve(import.meta.dirname, '..'), out = path.join(root, 'artifacts/tree-input-r13');
const manifestPath = path.join(out, 'native-event-fixture-manifest.json');
const manifest = JSON.parse(await fs.readFile(manifestPath));
const relative = 'src/SceneWorkbench.Web/wwwroot/index.html', target = path.join(manifest.fixtureRoot, relative);
const before = await fs.readFile(target, 'utf8'), hash = b => crypto.createHash('sha256').update(b).digest('hex');
const after = before.replace('<div class="editor-shell">', '<div class="editor-shell" style="grid-template-rows:35px auto auto auto minmax(300px,1fr) 26px 27px 23px">');
assert.notEqual(before, after);
const entry = manifest.copied.find(f => f.path === relative); assert.equal(hash(before), entry.fixtureSha256);
await fs.writeFile(path.join(out, 'fixture-layout-repair.json'), JSON.stringify({ time: new Date().toISOString(),
  reason: 'Extra QA toolbar requires an extra auto grid row. Fixture-only layout issue; no product modules changed.',
  path: relative, beforeSha256: hash(before), afterSha256: hash(after) }, null, 2) + '\n', { flag: 'wx' });
await fs.writeFile(target, after); entry.fixtureSha256 = hash(after);
await fs.writeFile(manifestPath, JSON.stringify(manifest, null, 2) + '\n');
console.log('QA fixture layout repaired; all product modules unchanged.');

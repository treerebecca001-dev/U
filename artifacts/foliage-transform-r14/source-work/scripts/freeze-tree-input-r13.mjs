// Freeze ordinary source/evidence files for review. No archive, build, installer or Unity process.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const root = path.resolve(import.meta.dirname, '..'), out = path.join(root, 'artifacts/tree-input-r13');
const handoff = path.join(out,'handoff'); await fs.mkdir(handoff);
const hash = b => crypto.createHash('sha256').update(b).digest('hex');
const read = async file => JSON.parse(await fs.readFile(file));
const old = await read(path.join(root,'artifacts/prefab-cancel-r12/handoff/source-manifest.json'));
const extra = (await fs.readdir(path.join(root,'scripts'))).filter(n=>n.endsWith('-r13.mjs')).map(n=>'scripts/'+n);
extra.push('docs/HANDOFF_TREE_INPUT_R13.zh-TW.md');
const sourceFiles = [...new Set([...old.files.map(f=>f.path),...extra])].sort();
const source = [];
for (const relative of sourceFiles) {
  const bytes = await fs.readFile(path.join(root,relative)), target = path.join(out,'source-snapshot',relative);
  await fs.mkdir(path.dirname(target),{recursive:true}); await fs.writeFile(target,bytes,{flag:'wx'});
  source.push({path:relative,bytes:bytes.length,sha256:hash(bytes)});
}
const fixture = await read(path.join(out,'native-event-fixture-manifest.json'));
const fixtureFiles = [...fixture.copied.map(f=>({path:f.path,sha256:f.fixtureSha256})),fixture.added,...fixture.additionalFiles];
for (const f of fixtureFiles) assert.equal(hash(await fs.readFile(path.join(fixture.fixtureRoot,f.path))),f.sha256,f.path);
const runtime = await read(path.join(root,'artifacts/prefab-cancel-r12/handoff/runtime.json'));
for (const f of runtime.binaries) assert.equal(hash(await fs.readFile(path.join(root,f.path))),f.sha256,f.path);
const evidence = [];
for (const name of (await fs.readdir(out,{withFileTypes:true})).filter(f=>f.isFile()).map(f=>f.name).sort()) {
  const bytes = await fs.readFile(path.join(out,name)); evidence.push({path:name,bytes:bytes.length,sha256:hash(bytes)});
}
const manifests = {
  'source-manifest.json':{time:new Date().toISOString(),root:'source-snapshot',files:source},
  'fixture-manifest.json':{fixtureRoot:fixture.fixtureRoot,files:fixtureFiles,productModulesModified:false,scope:'QA index plus two added files only; cache excluded'},
  'runtime-reference.json':{manifest:'artifacts/prefab-cancel-r12/handoff/runtime.json',sha256:hash(await fs.readFile(path.join(root,'artifacts/prefab-cancel-r12/handoff/runtime.json'))),binaries:runtime.binaries},
  'native-input-manifest.json':{files:(await read(path.join(out,'native-before.json'))).files,treeFiles:(await read(path.join(out,'tree-target-audit.json'))).files},
  'evidence-manifest.json':{files:evidence}
};
const pins = [];
for (const [name,body] of Object.entries(manifests)) {
  const bytes=Buffer.from(JSON.stringify(body,null,2)+'\n'); await fs.writeFile(path.join(handoff,name),bytes,{flag:'wx'}); pins.push({path:name,sha256:hash(bytes)});
}
const summary = {time:new Date().toISOString(),sourceFiles:source.length,fixtureFiles:fixtureFiles.length,runtimeFiles:runtime.binaries.length,evidenceFiles:evidence.length,manifests:pins,status:'UNVERIFIED',independentReview:'PENDING',unityStarted:false,packaged:false};
await fs.writeFile(path.join(handoff,'handoff.json'),JSON.stringify(summary,null,2)+'\n',{flag:'wx'});
console.log(JSON.stringify(summary));

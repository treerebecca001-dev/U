// Isolate before/working sources so the running user service never serves half-written changes.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
const root=path.resolve(import.meta.dirname,'..'),out=path.join(root,'artifacts/foliage-transform-r14');
const manifest=JSON.parse(await fs.readFile(path.join(root,'artifacts/tree-input-r13/handoff/source-manifest.json')));
const hash=b=>crypto.createHash('sha256').update(b).digest('hex');
for(const name of ['before-source','source-work']) {
  const destination=path.join(out,name);await fs.mkdir(destination);
  for(const f of manifest.files){const bytes=await fs.readFile(path.join(root,'artifacts/tree-input-r13/source-snapshot',f.path));assert.equal(hash(bytes),f.sha256,f.path);
    const target=path.join(destination,f.path);await fs.mkdir(path.dirname(target),{recursive:true});await fs.writeFile(target,bytes,{flag:'wx'});}
  const cacheSource=path.join(root,'artifacts/tree-input-r13/native-event-fixture/artifacts/cache/preview-guid');
  const cacheTarget=path.join(destination,'artifacts/cache/preview-guid');await fs.mkdir(cacheTarget,{recursive:true});
  for(const file of await fs.readdir(cacheSource))if(file.endsWith('.json'))await fs.copyFile(path.join(cacheSource,file),path.join(cacheTarget,file));
}
await fs.writeFile(path.join(out,'staging.json'),JSON.stringify({time:new Date().toISOString(),sourceFiles:manifest.files.length,
  sourceManifestSha256:hash(await fs.readFile(path.join(root,'artifacts/tree-input-r13/handoff/source-manifest.json'))),
  beforeSource:path.join(out,'before-source'),workingSource:path.join(out,'source-work'),mainSourceEdited:false,unityStarted:false,packaged:false},null,2)+'\n',{flag:'wx'});
console.log('Before and working sources staged; main source untouched.');

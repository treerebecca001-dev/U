// Preserve screenshot bytes; use their actual JPEG format for artifact filenames.
import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
const root=path.resolve(import.meta.dirname,'..'),out=path.join(root,'artifacts/foliage-transform-r14'),renamed=[];
for(const name of (await fs.readdir(out)).filter(n=>n.endsWith('.png'))){
  const from=path.resolve(out,name),to=path.resolve(out,name.slice(0,-4)+'.jpg');
  assert.ok(from.startsWith(out+path.sep)&&to.startsWith(out+path.sep));
  const bytes=await fs.readFile(from);assert.equal(bytes[0],255);assert.equal(bytes[1],216);
  assert.equal(await fs.stat(to).then(()=>true,()=>false),false);
  await fs.rename(from,to);renamed.push({from:name,to:path.basename(to),sha256:crypto.createHash('sha256').update(bytes).digest('hex')});
}
await fs.writeFile(path.join(out,'capture-filenames.json'),JSON.stringify({time:new Date().toISOString(),pixelBytesChanged:false,renamed},null,2)+'\n',{flag:'wx'});
console.log(JSON.stringify({renamed:renamed.length,pixelsChanged:false}));

const {chromium}=require('C:/Users/tree/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const fs=require('node:fs/promises'),path=require('node:path'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'..'), project=path.join(root,'Workspace/Baselines/Client231229-2022.3.13f1');
const out=path.join(root,'artifacts/version-r8',process.argv[2]??'browser-final');
const scene='Assets/02.Arts/Scene/Room/Room_main.unity',mat='Assets/Tourist/TouristAssets/woman/material.mat',url=process.env.WORKBENCH_QA_URL??'http://127.0.0.1:5288';
const sha=b=>crypto.createHash('sha256').update(b).digest('hex');
(async()=>{
  await fs.mkdir(out,{recursive:true});
  assert.equal(await fs.stat(path.join(out,'result.json')).then(()=>true,()=>false),false,'Keep prior raw results');
  const watched=[scene,scene+'.meta',mat,mat+'.meta','Assets/02.Arts/Shader/Role/ASE_Role.shader','Assets/02.Arts/Shader/Role/ASE_Role.shader.meta','ProjectSettings/ProjectVersion.txt','Packages/manifest.json','Packages/packages-lock.json'];
  const hashes=async folder=>Object.fromEntries(await Promise.all(watched.map(async relative=>[relative,sha(await fs.readFile(path.join(folder,relative)))])));
  const original='D:/Work_ART/漂流NFT/@WLN_work/Client231229';
  const report={time:new Date().toISOString(),steps:[],served:[],pageErrors:[],console:[],failedRequests:[],before:await hashes(project),originalBefore:await hashes(original)};
  const step=(name,data={})=>{report.steps.push({name,status:'PASS',...data});console.log(JSON.stringify(report.steps.at(-1)));};
  let browser;
  try{
    browser=await chromium.launch({channel:'chrome',headless:true,chromiumSandbox:true});report.browserVersion=browser.version();
    const page=await browser.newPage({viewport:{width:1920,height:1080},locale:'zh-TW'});page.setDefaultTimeout(900000);
    page.on('pageerror',e=>report.pageErrors.push(e.stack));page.on('console',m=>report.console.push({type:m.type(),text:m.text()}));page.on('requestfailed',r=>report.failedRequests.push({url:r.url(),error:r.failure()?.errorText}));
    const idle=()=>page.waitForFunction(()=>!document.querySelector('#progress').classList.contains('busy'));
    await page.goto(url+'/?source=baseline-r8',{waitUntil:'networkidle'});await idle();
    const token=await page.locator('meta[name=workbench-token]').getAttribute('content');
    const get=async endpoint=>{const res=await page.request.get(url+endpoint,{headers:{'X-Workbench-Token':token}});assert.equal(res.status(),200);return res.json();};
    for(const file of ['ui/app.js','ui/scene-view.js','ui/material-inspector.js','ui/material-adapters.js','ui/asset-preview.js','ui/editor.css','ui/shader-adapters.json','vendor/loaders/FBXLoader.js']){
      const res=await page.request.get(url+'/'+file);assert.equal(res.status(),200);const digest=sha(await res.body());assert.equal(digest,sha(await fs.readFile(path.join(root,'src/SceneWorkbench.Web/wwwroot',file))));report.served.push({file,sha256:digest});
    }
    const bootstrap=await get('/api/bootstrap');assert.equal(path.resolve(bootstrap.samplePath),project);
    let state=await get('/api/state');assert.equal(state.dirty,false);
    await page.locator('#open-project').click();await page.locator('#project-path').fill(project);await page.locator('#project-use-copy').uncheck();await page.locator('#project-form button[type=submit]').click();
    await page.locator('#project-dialog').waitFor({state:'hidden'});await idle();
    await page.locator('#scene-select').selectOption(scene);await idle();
    state=await get('/api/state');assert.equal(state.editorVersion,'2022.3.13f1');assert.equal(state.writable,true);
    assert.equal(state.diagnostics.filter(d=>d.severity==='Blocking').length,0);assert.equal(await page.locator('#viewport').getAttribute('data-model-count'),'7');
    step('Prepared target opens with no package blockers and actual seven meshes',{renderer:await page.locator('#renderer-state').innerText()});
    await page.screenshot({path:path.join(out,'client-target.png'),fullPage:true});
    const selected=state.items.find(i=>i.editable&&i.editableFields.includes('position'));assert.ok(selected,'Legacy native objects remain editable on explicit 2022.3 profile');
    await page.locator('#hierarchy button[data-file-id="'+selected.id+'"]').click();await idle();
    const before=Number(await page.locator('#position-x').inputValue()),value=before+.125;
    await page.locator('#position-x').fill(String(value));await page.locator('#apply-inspector').click();await idle();assert.equal((await get('/api/state')).dirty,true);
    let start=Date.now();await page.locator('#save').click();await idle();state=await get('/api/state');assert.equal(state.dirty,false,await page.locator('#operation-error').innerText());
    assert.notEqual((await hashes(project))[scene],report.before[scene]);step('Actual target native scene Save',{ms:Date.now()-start,id:selected.id});
    await page.reload({waitUntil:'networkidle'});await idle();state=await get('/api/state');assert.ok(Math.abs(state.items.find(i=>i.id===selected.id).position[0]-value)<1e-7);
    await page.locator('#undo').click();await idle();await page.locator('#redo').click();await idle();await page.locator('#undo').click();await idle();
    await page.locator('#save').click();await idle();assert.equal((await hashes(project))[scene],report.before[scene]);step('Scene page reload, Undo/Redo and exact-byte restoration');
    const graph=await get('/api/preview?path='+encodeURIComponent(scene)),role=graph.nodes.find(n=>n.materials.includes(mat));assert.ok(role);
    await page.locator('#hierarchy button[data-file-id="'+role.ownerId+'"]').click();await idle();await page.locator('#focus-object').click();
    await page.screenshot({path:path.join(out,'role-target.png'),fullPage:true});
    await page.locator('#renderer-materials button').filter({hasText:'material.mat'}).click();await idle();assert.equal((await get('/api/state')).material.editable,true);
    const shine=await page.locator('#mat-_Shininess-value-value').inputValue(),texture=await page.locator('#mat-_ColorTextures-texture').inputValue();
    await page.locator('#mat-_Shininess-value-value').fill('.13');await page.locator('#mat-_ColorTextures-texture').fill('Assets/Tourist/TouristAssets/woman/texture_1.png');
    await page.locator('#apply-material').click();await idle();start=Date.now();await page.locator('#save').click();await idle();assert.equal((await get('/api/state')).dirty,false,await page.locator('#operation-error').innerText());
    assert.notEqual((await hashes(project))[mat],report.before[mat]);step('Actual ASE scalar and texture native Save on 2022.3 target',{ms:Date.now()-start});
    await page.locator('#reload').click();await idle();assert.ok(Math.abs(Number(await page.locator('#mat-_Shininess-value-value').inputValue())-.13)<1e-7);
    assert.equal(await page.locator('#mat-_ColorTextures-texture').inputValue(),'Assets/Tourist/TouristAssets/woman/texture_1.png');
    await page.locator('#mat-_Shininess-value-value').fill(shine);await page.locator('#mat-_ColorTextures-texture').fill(texture);await page.locator('#apply-material').click();await idle();
    await page.locator('#undo').click();await idle();await page.locator('#redo').click();await idle();await page.locator('#save').click();await idle();
    assert.equal((await hashes(project))[mat],report.before[mat]);step('ASE reload, Undo/Redo and exact-byte restoration');
    await page.setViewportSize({width:390,height:844});await page.locator('#mat-_Shininess-value-value').scrollIntoViewIfNeeded();await page.locator('#mat-_Shininess-value-value').click();
    assert.equal(await page.evaluate(()=>document.activeElement.id),'mat-_Shininess-value-value');assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
    await page.screenshot({path:path.join(out,'narrow-target.png'),fullPage:true});step('390px target material controls remain usable');
    assert.deepEqual(report.pageErrors,[]);assert.deepEqual(report.console.filter(m=>m.type==='error'),[]);
    // A page reload may cancel a prior preview image fetch; keep the raw list and adjudicate each request.
    assert.ok(report.failedRequests.every(r=>r.error==='net::ERR_ABORTED'),JSON.stringify(report.failedRequests));
    report.after=await hashes(project);assert.deepEqual(report.after,report.before);report.originalAfter=await hashes(original);assert.deepEqual(report.originalAfter,report.originalBefore);
    step('All watched native/source files restored; original D template untouched');report.status='PASS';
  }catch(e){report.status='FAILED';report.error=e.stack;process.exitCode=1;}
  finally{report.after=await hashes(project);report.originalAfter=await hashes(original);await fs.writeFile(path.join(out,'result.json'),JSON.stringify(report,null,2));if(browser)await browser.close();console.log(JSON.stringify({status:report.status,error:report.error,steps:report.steps.length}));}
})();

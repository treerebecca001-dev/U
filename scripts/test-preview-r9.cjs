const {chromium}=require('C:/Users/tree/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const fs=require('node:fs/promises'),path=require('node:path'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'..'),base=process.env.WORKBENCH_QA_URL??'http://127.0.0.1:5292';
const out=path.join(root,'artifacts/preview-r9',process.argv[2]??'browser'),project=path.join(root,'Workspace/Baselines/Client231229-2022.3.13f1');
const scene='Assets/02.Arts/Scene/Map/Map_60001/Map_60001.unity',sha=b=>crypto.createHash('sha256').update(b).digest('hex');
(async()=>{
 await fs.mkdir(out,{recursive:true});assert.equal(await fs.stat(path.join(out,'result.json')).then(()=>true,()=>false),false);
 const report={started:new Date().toISOString(),base,project,scene,steps:[],pageErrors:[],console:[],failedRequests:[]};let browser;
 const step=(name,value)=>{report.steps.push({name,value,status:'PASS'});console.log(JSON.stringify(report.steps.at(-1)));};
 try{
  browser=await chromium.launch({channel:'chrome',headless:true,chromiumSandbox:true});report.browser=browser.version();
  const page=await browser.newPage({viewport:{width:1178,height:841},locale:'zh-TW'});page.setDefaultTimeout(240000);
  page.on('pageerror',e=>report.pageErrors.push(e.stack));page.on('console',m=>report.console.push({type:m.type(),text:m.text()}));
  page.on('requestfailed',r=>report.failedRequests.push({url:r.url(),error:r.failure()?.errorText}));
  const idle=()=>page.waitForFunction(()=>!document.querySelector('#progress').classList.contains('busy'));
  await page.goto(base+'/?source=preview-r9-root',{waitUntil:'networkidle'});await idle();
  const token=await page.locator('meta[name=workbench-token]').getAttribute('content');
  const get=async endpoint=>{const r=await page.request.get(base+endpoint,{headers:{'X-Workbench-Token':token},timeout:240000});assert.equal(r.status(),200);return r.json();};
  report.served=[];
  for(const file of ['ui/app.js','ui/scene-view.js','ui/preview-bindings.js','ui/material-adapters.js','ui/material-inspector.js','ui/asset-preview.js','ui/surface-adapters.json','ui/terrain-preview.js','ui/editor.css','ui/water-material.js','ui/water-effects.js','ui/water-depth.js','ui/water-shadow.js','ui/environment-preview.js','vendor/loaders/EXRLoader.js']){
   const r=await page.request.get(base+'/'+file),digest=sha(await r.body());assert.equal(digest,sha(await fs.readFile(path.join(root,'src/SceneWorkbench.Web/wwwroot',file))));report.served.push({file,sha256:digest});
  }
  const watch=[scene,scene+'.meta','ProjectSettings/ProjectVersion.txt','Packages/manifest.json','Packages/packages-lock.json',
   'Assets/ResourcesBuild/Scenes/Common/Trees/TreeA01/TreeA01.prefab',
   'Assets/04.Packages/StylizedWater2/Shaders/StylizedWater2_Standard.watershader',
   'Assets/02.Arts/Scene/Map/Map_60001/Map_60001.asset','Assets/02.Arts/Scene/Map/Map_60001/Map_60001.asset.meta',
   'Assets/ResourcesBuild/Scenes/Common/Light/Scene_Directional Light/Scene_Directional Light.prefab',
   'Assets/02.Arts/Scene/Map/Map_60001/Map_60001/LightingData.asset',
   'Assets/02.Arts/Scene/Map/Map_60001/Map_60001/ReflectionProbe-0.exr','Assets/02.Arts/Scene/Map/Map_60001/Map_60001/ReflectionProbe-0.exr.meta'];
  report.nativeBefore={};for(const p of watch)report.nativeBefore[p]=sha(await fs.readFile(path.join(project,p)));
  await page.locator('#open-project').click();await page.locator('#project-path').fill(project);await page.locator('#project-use-copy').uncheck();await page.locator('#project-form button[type=submit]').click();await page.locator('#project-dialog').waitFor({state:'hidden'});await idle();
  const graphResponse=page.waitForResponse(r=>new URL(r.url()).pathname==='/api/preview'&&r.status()===200);
  await page.locator('#scene-select').selectOption(scene);await idle();
  const graph=await (await graphResponse).json(),state=await get('/api/state');
  assert.equal(state.scenePath,scene);assert.equal(state.dirty,false);assert.equal(state.items.length,3078);
  await fs.writeFile(path.join(out,'graph.json'),JSON.stringify(graph));
  const before=JSON.parse(await fs.readFile(path.join(root,'artifacts/preview-r9/graph-before.json')));
  assert.equal(graph.nodes.length,before.nodes.length);
  const missing=g=>g.nodes.filter(n=>(n.meshPath||n.meshFileId!=='0')&&!n.materials.length);
  const tree=graph.nodes.filter(n=>/TreeA01_(leaves|trunk)/i.test(n.name)&&n.fbxInstance);
  assert.equal(tree.length,246);assert.ok(tree.every(n=>n.materials.length===1&&n.materials[0]===n.fbxInstance.renderers[0].slots[0].path));
  assert.equal(tree.find(n=>n.id==='222870/729491787451398514/fbx').materials[0],'Assets/ResourcesBuild/Scenes/Common/Trees/TreeA01/TreeA01_ce_m.mat');
  assert.equal(new Set(tree.map(n=>n.materials[0])).size,6,'Native tree variants must not be collapsed to one material');
  assert.ok(tree.every(n=>n.fbxInstance.renderers[0].fileId==='-7511558181221131132'));
  const treeMat=graph.materials.find(m=>m.path.endsWith('/TreeA01_ce_m.mat'));assert.ok(treeMat);assert.equal(treeMat.previewKind,'saved-properties');assert.equal(treeMat.editable,false);
  assert.ok(treeMat.properties.find(p=>p.name==='_Texture').texturePath);
  const waters=graph.materials.filter(m=>m.previewKind==='water-template');assert.equal(waters.length,2);assert.ok(waters.every(m=>m.editable===false&&m.shaderHash==='ca1398d72f423e9a74c2c5e6b548b0ee715071de1f09273a07f7b6ff1c89a24a'));
  step('Exact Map_60001 native graph, 246 TreeA01 FBX material bindings, original texture and two read-only water templates',{nodes:graph.nodes.length,materials:graph.materials.length,missingBefore:missing(before).length,missingAfter:missing(graph).length});
  const viewport=await page.locator('#viewport').evaluate(e=>({...e.dataset}));report.viewport=viewport;
  assert.equal(viewport.modelCount,'3105');assert.equal(viewport.triangles,'633165');assert.equal(viewport.terrainCount,'1');assert.equal(viewport.terrainTriangles,'524288');
  assert.equal(viewport.unresolvedTerrainLayers,'1');assert.ok(Number(viewport.fbxBindings)>=246);assert.ok(Number(viewport.approximations)>=255);
  const terrain=graph.terrains[0];assert.equal(graph.terrains.length,1);assert.equal(terrain.sha256,'5599b8d0b42efd1b2ddf2b81e67a9f0d26bac7c57cd81b602f1001d7b674dcd3');
  assert.equal(terrain.heightmap.resolution,513);assert.equal(terrain.heightmap.samples.length,263169);assert.equal(new Set(terrain.heightmap.samples).size,465);
  assert.equal(terrain.heightmap.samples[0],55);assert.equal(terrain.heightmap.samples[131584],233);assert.equal(terrain.heightmap.samples.at(-1),55);
  assert.deepEqual(terrain.heightmap.size,[240,600,240]);assert.equal(terrain.layers.length,3);assert.equal(terrain.controls[0].rgba.length,1398104);
  assert.ok(terrain.layers.every(l=>l.fileId==='8574412962073106934'));assert.equal(terrain.layers.filter(l=>!l.path).length,1);
  assert.ok(terrain.diagnostics.some(d=>d.includes('3a2a9d837fbf13e4b9197bb9b1ba3484')));
  await page.locator('#grid-toggle').uncheck();await page.locator('#helpers-toggle').uncheck();await page.locator('#frame-scene').click();
  await page.screenshot({path:path.join(out,'map60001-1178.png')});step('Full scene geometry retained, native FBX overrides and explicitly counted approximations',viewport);
  await page.locator('#hierarchy-search').fill('60001_Terrain');await page.locator('#hierarchy button[data-file-id="1114381152"]').click();await idle();
  await page.locator('#focus-object').click();await page.locator('#terrain-details').scrollIntoViewIfNeeded();
  report.terrainInspector=await page.locator('#terrain-details').innerText();assert.match(report.terrainInspector,/513 × 513/);assert.match(report.terrainInspector,/263,169/);
  assert.match(report.terrainInspector,/-62\.7000 \/ 0\.0000 \/ -8\.2000/);assert.match(report.terrainInspector,/雕刻.*尚未實作/);
  await page.screenshot({path:path.join(out,'terrain-height-inspector-1178.png')});
  report.terrainGeometry=await page.evaluate(async data=>{const {terrainGeometry}=await import('/ui/terrain-preview.js'),g=terrainGeometry(data.heightmap),p=g.getAttribute('position');const r={vertices:p.count,indices:g.index.count,first:[p.getX(0),p.getY(0),p.getZ(0)],center:[p.getX(131584),p.getY(131584),p.getZ(131584)],last:[p.getX(p.count-1),p.getY(p.count-1),p.getZ(p.count-1)],normalY:g.getAttribute('normal').getY(0)};g.dispose();return r;},terrain);
  assert.equal(report.terrainGeometry.vertices,263169);assert.equal(report.terrainGeometry.indices,1572864);assert.ok(report.terrainGeometry.normalY>.99);assert.ok(Math.abs(report.terrainGeometry.center[1]-233/32766*600)<.00001);
  step('Native terrain raw grid, height normalization, axes and parent world origin; missing layer stays explicit',report.terrainGeometry);
  await page.locator('#hierarchy-search').fill('WaterTile_x0z0');
  await page.locator('#hierarchy button[data-file-id="743902879"]').click();await idle();
  const overflow=()=>page.locator('#inspector').evaluate(e=>({width:e.clientWidth,scroll:e.scrollWidth,offenders:[...e.querySelectorAll('*')].filter(n=>n.getBoundingClientRect().right>e.getBoundingClientRect().right+1).map(n=>({tag:n.tagName,class:n.className,right:n.getBoundingClientRect().right})).slice(0,12)}));
  report.inspector1178=await overflow();assert.ok(report.inspector1178.scroll<=report.inspector1178.width,JSON.stringify(report.inspector1178));
  await page.locator('#focus-object').click();await page.screenshot({path:path.join(out,'water-selected-1178.png')});
  await page.locator('.material-link').filter({hasText:'StylizedWater2_Mobile 3.mat'}).click();await idle();
  assert.equal(await page.locator('#apply-material').isEnabled(),false);
  await page.screenshot({path:path.join(out,'water-readonly-1178.png')});
  report.material1178=await overflow();assert.ok(report.material1178.scroll<=report.material1178.width,JSON.stringify(report.material1178));
  step('1178px object/material Inspector fits without a horizontal scrollbar; water stays read-only',report.inspector1178);
  await page.setViewportSize({width:1920,height:1080});await page.screenshot({path:path.join(out,'water-desktop.png')});
  await page.setViewportSize({width:390,height:844});assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
  report.inspector390=await overflow();assert.ok(report.inspector390.scroll<=report.inspector390.width,JSON.stringify(report.inspector390));
  await page.locator('#inspector-heading').scrollIntoViewIfNeeded();await page.screenshot({path:path.join(out,'water-narrow.png')});step('Desktop and 390px stacked Inspector retain scroll containment');
  report.nativeAfter={};for(const p of watch)report.nativeAfter[p]=sha(await fs.readFile(path.join(project,p)));assert.deepEqual(report.nativeAfter,report.nativeBefore);
  assert.equal((await get('/api/state')).dirty,false);assert.deepEqual(report.pageErrors,[]);
  assert.deepEqual(report.console.filter(m=>m.type==='error'),[]);assert.deepEqual(report.failedRequests,[]);step('No native writes or browser/network errors');
  report.status='PASS';
 }catch(e){report.status='FAILED';report.error=e.stack;process.exitCode=1;if(browser?.contexts()[0]?.pages()[0])await browser.contexts()[0].pages()[0].screenshot({path:path.join(out,'failure.png')}).catch(()=>{});}
 finally{report.finished=new Date().toISOString();await fs.writeFile(path.join(out,'result.json'),JSON.stringify(report,null,2));if(browser)await browser.close();console.log(JSON.stringify({status:report.status,error:report.error,steps:report.steps.length}));}
})();

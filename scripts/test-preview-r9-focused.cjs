const {chromium}=require('C:/Users/tree/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'..'),base=process.env.WORKBENCH_QA_URL??'http://127.0.0.1:5292',out=path.join(root,'artifacts/preview-r9',process.argv[2]??'focused');
(async()=>{await fs.mkdir(out,{recursive:true});assert.equal(await fs.stat(path.join(out,'result.json')).then(()=>true,()=>false),false);
 const report={base,steps:[],pageErrors:[],console:[]};let browser;
 try{
  browser=await chromium.launch({channel:'chrome',headless:true,chromiumSandbox:true});const page=await browser.newPage({viewport:{width:1178,height:841},locale:'zh-TW'});page.setDefaultTimeout(240000);
  page.on('pageerror',e=>report.pageErrors.push(e.stack));page.on('console',m=>report.console.push({type:m.type(),text:m.text()}));
  await page.goto(base+'/?source=preview-r9-focused',{waitUntil:'networkidle'});const idle=()=>page.waitForFunction(()=>!document.querySelector('#progress').classList.contains('busy'));await idle();
  if(await page.locator('#back-to-scene').count()){await page.locator('#back-to-scene').click();await idle();}
  await page.locator('#grid-toggle').uncheck();await page.locator('#helpers-toggle').uncheck();
  await page.locator('#hierarchy-search').fill('TreeA01');await page.locator('#hierarchy button[data-file-id="222870"]').click();await idle();await page.locator('#focus-object').click();
  await page.screenshot({path:path.join(out,'tree-native-binding-1178.png')});
  await page.locator('.material-link').filter({hasText:'TreeA01_ce_m.mat'}).click();await idle();
  await page.waitForFunction(()=>[...document.querySelectorAll('.inspector-help')].some(e=>e.textContent.includes('唯讀原生 _Texture')));
  assert.equal(await page.locator('#mat-_Texture-texture').inputValue(),'Assets/ResourcesBuild/Scenes/Common/Trees/TreeA01/TreeA01_ce_t.png');
  assert.equal(await page.locator('#apply-material').isDisabled(),true);assert.equal(await page.locator('#save').isDisabled(),true);
  assert.equal(await page.locator('#inspector').evaluate(e=>e.scrollWidth<=e.clientWidth),true);
  await page.screenshot({path:path.join(out,'tree-readonly-texture-1178.png')});report.steps.push('TreeA01 focused geometry, actual native texture and read-only material Inspector');
  const graph=JSON.parse(await fs.readFile(path.join(root,'artifacts/preview-r9/terrain-browser/graph.json')));
  report.negative=await page.evaluate(async graph=>{
   const {createMaterial}=await import('/ui/material-adapters.js'),{fbxInstanceBinding}=await import('/ui/preview-bindings.js'),warnings=[];
   const texture=(await import('/vendor/three.module.js')).DataTexture,white=new texture(new Uint8Array([255,255,255,255]),1,1);
   const assets={white,warn:m=>warnings.push(m),texture:async()=>({texture:white,meta:{rows:1,columns:1}})};
   const water=graph.materials.find(m=>m.previewKind==='water-template'),changedWater=await createMaterial({...water,shaderHash:'0'.repeat(64)},assets);
   const ase=graph.materials.find(m=>m.isAse),changedAse=await createMaterial({...ase,shaderHash:'0'.repeat(64)},assets);
   const tree=graph.materials.find(m=>m.path.endsWith('/TreeA01_ce_m.mat')),treePreview=await createMaterial(tree,assets);
   const native=graph.nodes.find(n=>n.fbxInstance).fbxInstance;
   const {terrainGeometry,terrainSurface}=await import('/ui/terrain-preview.js');let invalidGridRejected=false,holesRejected=false;
   try{terrainGeometry({resolution:3,samples:[1,2],size:[1,1,1]});}catch{invalidGridRejected=true;}
   try{await terrainSurface({...graph.terrains[0],holesSamples:1},assets);}catch{holesRejected=true;}
   const results={waterHashChanged:changedWater.userData.previewMode,aseHashChanged:changedAse.userData.previewMode,invalidGridRejected,holesRejected,
    noWireframe:[changedWater,changedAse,treePreview].every(m=>!m.wireframe),treeMode:treePreview.userData.previewMode,
    actualTextureSampler:Boolean(treePreview.uniforms.t_Texture),nativeAlphaCutoff:treePreview.fragmentShader.includes('if(alpha<f_AlphaCutoff)discard;'),ambiguousMeshes:fbxInstanceBinding(native,[{},{}]).supported,
    ambiguousRenderers:fbxInstanceBinding({...native,renderers:[...native.renderers,...native.renderers]},[{}]).supported,warnings};
   [changedWater,changedAse,treePreview].forEach(m=>m.dispose());white.dispose();return results;
  },graph);
  assert.equal(report.negative.waterHashChanged,'unresolved');assert.equal(report.negative.aseHashChanged,'unresolved');assert.equal(report.negative.noWireframe,true);
  assert.equal(report.negative.treeMode,'approximation');assert.equal(report.negative.actualTextureSampler,true);
  assert.equal(report.negative.nativeAlphaCutoff,true);
  assert.equal(report.negative.invalidGridRejected,true);assert.equal(report.negative.holesRejected,true);
  assert.equal(report.negative.ambiguousMeshes,false);assert.equal(report.negative.ambiguousRenderers,false);report.steps.push('Real browser rejects changed Shader hashes and ambiguous FBX mappings; source texture has a real GLSL sampler');
  assert.deepEqual(report.pageErrors,[]);assert.deepEqual(report.console.filter(m=>m.type==='error'),[]);report.status='PASS';
 }catch(e){report.status='FAILED';report.error=e.stack;process.exitCode=1;}
 finally{await fs.writeFile(path.join(out,'result.json'),JSON.stringify(report,null,2));if(browser)await browser.close();console.log(JSON.stringify({status:report.status,error:report.error,steps:report.steps}));}
})();

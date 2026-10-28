// Real Chrome/WebGL checks of the actual two native water snapshots and their source-pinned adapter.
const {chromium}=require('C:/Users/tree/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const fs=require('node:fs/promises'),path=require('node:path'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'..'),base=process.env.WORKBENCH_QA_URL??'http://127.0.0.1:5296';
const out=path.join(root,'artifacts/preview-r9',process.argv[2]??'water-focused');
const project=path.join(root,'Workspace/Baselines/Client231229-2022.3.13f1'),sha=b=>crypto.createHash('sha256').update(b).digest('hex');
(async()=>{
 await fs.mkdir(out,{recursive:true});assert.equal(await fs.stat(path.join(out,'result.json')).then(()=>true,()=>false),false,'Do not overwrite evidence');
 const report={started:new Date().toISOString(),base,steps:[],pageErrors:[],console:[],failedRequests:[]};let browser,phase='bootstrap';
 const step=(name,value)=>{report.steps.push({name,value,status:'PASS'});console.log(JSON.stringify(report.steps.at(-1)));};
 try{
  const graph=JSON.parse(await fs.readFile(path.join(root,'artifacts/preview-r9/water-browser/graph.json')));
  const waters=graph.materials.filter(m=>m.previewKind==='water-template');assert.equal(waters.length,2);
  const paths=[...waters.flatMap(m=>[m.path,m.path+'.meta']),
   ...Object.keys(waters[0].shaderDependencies).map(n=>'Assets/04.Packages/StylizedWater2/Shaders/Libraries/'+n),
   'Assets/02.Arts/Scene/Map/Map_60001/Map_60001.asset','Assets/02.Arts/Scene/Map/Map_60001/Map_60001.unity'];
  report.nativeBefore={};for(const file of paths)report.nativeBefore[file]=sha(await fs.readFile(path.join(project,file)));
  browser=await chromium.launch({channel:'chrome',headless:true,chromiumSandbox:true});report.browser=browser.version();
  const page=await browser.newPage({viewport:{width:1600,height:1000},locale:'zh-TW'});page.setDefaultTimeout(240000);
  page.on('pageerror',e=>report.pageErrors.push(e.stack));page.on('console',m=>report.console.push({type:m.type(),text:m.text()}));
  page.on('requestfailed',r=>report.failedRequests.push({url:r.url(),error:r.failure()?.errorText,phase}));
  const idle=()=>page.waitForFunction(()=>!document.querySelector('#progress').classList.contains('busy'));
  await page.goto(base+'/?source=preview-r9-fix1-water',{waitUntil:'networkidle'});await idle();await page.waitForLoadState('networkidle');phase='interaction';
  report.served=[];
  for(const file of ['ui/app.js','ui/scene-view.js','ui/water-material.js','ui/water-depth.js','ui/water-effects.js','ui/material-adapters.js','ui/material-inspector.js','ui/asset-preview.js','ui/surface-adapters.json','ui/terrain-preview.js','vendor/three.module.js']){
   const response=await page.request.get(base+'/'+file);assert.equal(response.status(),200);const digest=sha(await response.body());
   assert.equal(digest,sha(await fs.readFile(path.join(root,'src/SceneWorkbench.Web/wwwroot',file))));report.served.push({file,sha256:digest});
  }
  if(await page.locator('#back-to-scene').count()){await page.locator('#back-to-scene').click();await idle();await page.waitForLoadState('networkidle');}
  await page.locator('#grid-toggle').uncheck();await page.locator('#helpers-toggle').uncheck();
  await page.locator('#hierarchy-search').fill('TreeA01');await page.locator('#hierarchy button[data-file-id="222870"]').click();await idle();await page.locator('#focus-object').click();
  // Selecting water keeps the near-object camera; do not reframe its enormous plane.
  await page.locator('#hierarchy-search').fill('WaterTile_x0z0');await page.locator('#hierarchy button[data-file-id="743902879"]').click();await idle();
  await page.locator('.material-link').filter({hasText:'StylizedWater2_Mobile 3.mat'}).click();await idle();await page.waitForLoadState('networkidle');
  assert.equal(await page.locator('#apply-material').isDisabled(),true);assert.equal(await page.locator('#save').isDisabled(),true);
  const time=page.locator('#material-preview-time');
  await time.fill('0');await time.dispatchEvent('input');await page.screenshot({path:path.join(out,'map60001-near-water-time0.png')});
  await time.fill('8');await time.dispatchEvent('input');await page.screenshot({path:path.join(out,'map60001-near-water-time8.png')});
  await time.fill('0');await time.dispatchEvent('input');
  step('Actual Map_60001 near-object camera, original water material and preview-time interaction; native material remains read-only');
  report.probe=await page.evaluate(async waters=>{
   const THREE=await import('/vendor/three.module.js'),{AssetPreview}=await import('/ui/asset-preview.js');
   const {WaterDepthPass}=await import('/ui/water-depth.js'),{createMaterial}=await import('/ui/material-adapters.js');
   const {waterFeatures}=await import('/ui/water-effects.js'),warnings=[];
   const token=document.querySelector('meta[name=workbench-token]').content,assets=new AssetPreview(token,m=>warnings.push(m));
   const renderer=new THREE.WebGLRenderer({antialias:false,preserveDrawingBuffer:true});renderer.setSize(640,480);renderer.setPixelRatio(1);renderer.outputColorSpace=THREE.SRGBColorSpace;
   renderer.domElement.id='qa-water-fixture';renderer.domElement.style.cssText='position:fixed;left:0;top:0;width:640px;height:480px;z-index:10000';document.body.append(renderer.domElement);
   const scene=new THREE.Scene();scene.background=new THREE.Color(.15,.17,.19);const root=new THREE.Group();scene.add(root);
   const camera=new THREE.PerspectiveCamera(50,640/480,.1,200);camera.position.set(13,12,15);camera.lookAt(0,0,0);camera.updateMatrixWorld();
   const waterGeometry=new THREE.PlaneGeometry(24,24,40,40);waterGeometry.rotateX(-Math.PI/2);
   // Known synthetic shoreline, solely to observe depth/wave dataflow with unchanged native parameters.
   const groundGeometry=new THREE.PlaneGeometry(30,30,10,10);groundGeometry.rotateX(-Math.PI/2);
   const positions=groundGeometry.getAttribute('position');for(let i=0;i<positions.count;i++)positions.setY(i,positions.getX(i)*.16-.35);groundGeometry.computeVertexNormals();
   const groundMaterial=new THREE.MeshBasicMaterial({color:0x7c7249}),ground=new THREE.Mesh(groundGeometry,groundMaterial);root.add(ground);
   const helper=new THREE.Mesh(new THREE.BoxGeometry(1,1,1),new THREE.MeshBasicMaterial());scene.add(helper);helper.visible=false;
   const pass=new WaterDepthPass(renderer),gl=renderer.getContext(),programs=[],effects=[],materials=[];
   const pixels=()=>{const value=new Uint8Array(640*480*4);gl.readPixels(0,0,640,480,gl.RGBA,gl.UNSIGNED_BYTE,value);return value;};
   const diff=(a,b)=>{let count=0,total=0;for(let i=0;i<a.length;i+=4){const d=Math.abs(a[i]-b[i])+Math.abs(a[i+1]-b[i+1])+Math.abs(a[i+2]-b[i+2]);if(d>3)count++;total+=d;}return {changedPixels:count,absoluteRgbDifference:total};};
   for(const snapshot of waters){
    const material=await assets.material(snapshot);materials.push(material);const mesh=new THREE.Mesh(waterGeometry,material);root.add(mesh);scene.updateMatrixWorld(true);
    pass.dirty=true;pass.render(scene,camera,root,[helper]);
    const render=t=>{material.uniforms.uPreviewTime.value=t;renderer.render(scene,camera);return pixels();};
    const zero=render(0),eight=render(8);const nativeHeight=material.uniforms._WaveHeight.value;
    material.uniforms._WaveHeight.value=0;const noWavesAtEight=render(8);material.uniforms._WaveHeight.value=nativeHeight;
    material.uniforms.uDepthAvailable.value=0;const noDepth=render(8);material.uniforms.uDepthAvailable.value=1;
    const nativeIntersectionAlpha=material.uniforms._IntersectionColor.value.w;material.uniforms._IntersectionColor.value.w=0;
    const noIntersection=render(8);material.uniforms._IntersectionColor.value.w=nativeIntersectionAlpha;
    // three.js reuses the same compiled program across native materials with identical GLSL.
    const compiled=renderer.properties.get(material).currentProgram,p=compiled.program,active=[];
    for(let i=0;i<gl.getProgramParameter(p,gl.ACTIVE_UNIFORMS);i++)active.push(gl.getActiveUniform(p,i).name);
    const source=material.fragmentShader;
    effects.push({path:snapshot.path,shaderGuid:snapshot.shaderGuid,shaderHash:snapshot.shaderHash,shaderDependencies:snapshot.shaderDependencies,
     features:waterFeatures(snapshot),activeUniforms:[...new Set(active)],timeDifference:diff(zero,eight),waveDisplacementDifference:diff(eight,noWavesAtEight),depthDifference:diff(eight,noDepth),intersectionDifference:diff(eight,noIntersection),
     nativeWaveHeight:nativeHeight,gpuWaveHeight:gl.getUniform(p,gl.getUniformLocation(p,'_WaveHeight')),nativeWaveSpeed:material.uniforms._WaveSpeed.value,nativeWaveDistance:material.uniforms._WaveDistance.value,nativeNormalStrength:material.uniforms._NormalStrength.value,depthTextureAvailable:material.uniforms.uDepthAvailable.value,
     foamCodePresent:source.includes('float foamNoise='),refractionCodePresent:source.includes('texture2D(uSceneColor'),warning:material.userData.adapterStatus,
     uniformValues:Object.fromEntries(Object.entries(material.uniforms).filter(([key])=>key.startsWith('_')).map(([key,{value}])=>[key,typeof value==='number'?value:value.isVector4?value.toArray():value.name||'(fallback)']))});
    if(material===materials[0]){render(8);window.__waterFixtureShot=renderer.domElement.toDataURL('image/png');}
    root.remove(mesh);
   }
   const original=waters[0],changedLibrary=structuredClone(original);changedLibrary.shaderDependencies['Waves.hlsl']='0'.repeat(64);
   const invalid=await createMaterial(changedLibrary,assets);const missingLibrary=structuredClone(original);delete missingLibrary.shaderDependencies['ForwardPass.hlsl'];
   const missing=await createMaterial(missingLibrary,assets);
   const stale=waterFeatures({...original,keywords:[...original.keywords.filter(k=>!['_FOAM','_REFRACTION','_CAUSTICS'].includes(k)),'SURFACE_FOAM','REFRACTION']});
   const invariants={changedLibraryMode:invalid.userData.previewMode,missingLibraryMode:missing.userData.previewMode,staleLegacyFoamEnabled:stale.foam,staleLegacyRefractionEnabled:stale.refraction,
    helperStillHidden:helper.visible===false,groundStillVisible:ground.visible,renderTargetRestored:renderer.getRenderTarget()===null,glError:gl.getError()};
   invalid.dispose();missing.dispose();pass.dispose();assets.dispose();waterGeometry.dispose();groundGeometry.dispose();groundMaterial.dispose();helper.geometry.dispose();helper.material.dispose();renderer.dispose();renderer.forceContextLoss();renderer.domElement.remove();
   return {scope:'Synthetic shoreline with the two unchanged native snapshots; tests dataflow, not Unity pixel parity. Actual Map_60001 screenshots are separate.',effects,invariants,warnings};
  },waters);
  const data=await page.evaluate(()=>{const data=window.__waterFixtureShot;delete window.__waterFixtureShot;return data;});await fs.writeFile(path.join(out,'native-parameters-synthetic-shore.png'),Buffer.from(data.split(',')[1],'base64'));
  await fs.writeFile(path.join(out,'contract-probe.json'),JSON.stringify(report.probe,null,2));
  for(const effect of report.probe.effects){
   assert.equal(effect.nativeNormalStrength,0);assert.ok(Math.abs(effect.gpuWaveHeight-effect.nativeWaveHeight)<.000001);assert.equal(effect.features.waves,true);assert.equal(effect.features.sharpIntersection,true);assert.equal(effect.features.depth,true);
   assert.equal(effect.features.foam,false);assert.equal(effect.features.refraction,false);assert.equal(effect.features.caustics,false);assert.equal(effect.foamCodePresent,false);
   for(const name of ['uPreviewTime','_WaveHeight','_WaveFadeDistance','uSceneDepth','uDepthAvailable','_IntersectionLength','_NormalStrength','_DistanceNormalsTiling'])assert.ok(effect.activeUniforms.includes(name),effect.path+' missing active '+name);
   if(effect.nativeWaveSpeed===0&&effect.nativeWaveDistance===1){assert.equal(effect.timeDifference.changedPixels,0);assert.equal(effect.waveDisplacementDifference.changedPixels,0);}
   else {assert.ok(effect.timeDifference.changedPixels>0);assert.ok(effect.waveDisplacementDifference.changedPixels>0);}
   assert.ok(effect.depthDifference.changedPixels>0);assert.ok(effect.intersectionDifference.changedPixels>0);assert.equal(Object.keys(effect.shaderDependencies).length,11);
  }
  assert.deepEqual(report.probe.invariants,{changedLibraryMode:'unresolved',missingLibraryMode:'unresolved',staleLegacyFoamEnabled:false,staleLegacyRefractionEnabled:false,helperStillHidden:true,groundStillVisible:true,renderTargetRestored:true,glError:0});
  step('Both native materials compile with active wave/depth/intersection/normal uniforms; native zero-speed lake stays still',report.probe.effects.map(e=>({path:e.path,waveSpeed:e.nativeWaveSpeed,time:e.timeDifference,wave:e.waveDisplacementDifference,depth:e.depthDifference,intersection:e.intersectionDifference})));
  step('Disabled native foam/refraction/caustics stay disabled; changed or missing HLSL library pins refuse fixed adaptation',report.probe.invariants);
  report.nativeAfter={};for(const file of paths)report.nativeAfter[file]=sha(await fs.readFile(path.join(project,file)));assert.deepEqual(report.nativeBefore,report.nativeAfter);
  // Bootstrap initially restores the active Inspector, then loadPreview disposes its old
  // AssetPreview controller. Its in-flight thumbnails are deliberately cancelled and retried.
  const nativeTexturePaths=new Set(waters.flatMap(m=>m.properties.map(p=>p.texturePath).filter(Boolean)));
  const expected=r=>r.phase==='bootstrap'&&r.error==='net::ERR_ABORTED'&&new URL(r.url).pathname==='/api/image'&&nativeTexturePaths.has(new URL(r.url).searchParams.get('path'));
  report.expectedStartupCancellations=report.failedRequests.filter(expected);
  report.unexpectedRequests=report.failedRequests.filter(r=>!expected(r));
  assert.deepEqual(report.pageErrors,[]);assert.deepEqual(report.console.filter(m=>m.type==='error'),[]);assert.deepEqual(report.unexpectedRequests,[]);
  step('Native inputs unchanged; no browser/shader or unexpected request errors; bootstrap thumbnail cancellation recorded separately',{expectedStartupCancellations:report.expectedStartupCancellations.length});report.status='PASS';
 }catch(error){report.status='FAILED';report.error=error.stack;process.exitCode=1;}
 finally{report.finished=new Date().toISOString();await fs.writeFile(path.join(out,'result.json'),JSON.stringify(report,null,2));if(browser)await browser.close();console.log(JSON.stringify({status:report.status,error:report.error,groups:report.steps.length}));}
})();

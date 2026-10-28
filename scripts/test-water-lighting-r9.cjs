const {chromium}=require('C:/Users/tree/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const fs=require('node:fs/promises'),path=require('node:path'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'..'),base=process.env.WORKBENCH_QA_URL??'http://127.0.0.1:5406',out=path.join(root,'artifacts/preview-r9',process.argv[2]??'lighting-focused');
const sha=bytes=>crypto.createHash('sha256').update(bytes).digest('hex');
(async()=>{
 await fs.mkdir(out,{recursive:true});assert.equal(await fs.stat(path.join(out,'result.json')).then(()=>true,()=>false),false);
 const report={base,started:new Date().toISOString(),pageErrors:[],console:[],steps:[]};let browser;
 try{
  const graph=JSON.parse(await fs.readFile(path.join(root,'artifacts/preview-r9/lighting-browser/graph.json'))),e=graph.environment;
  assert.equal(e.reflectionMode,0);assert.equal(e.reflectionIntensity,1);assert.equal(e.bakedReflection.probeCount,0);assert.equal(e.bakedReflection.cubemaps.length,1);
  const cube=e.bakedReflection.cubemaps[0];assert.equal(cube.guid,'4c2e18007d862fd409ac3a9f67f24508');assert.equal(cube.fileId,'8900000');
  for(const id of ['595751172','743902879']){const n=graph.nodes.find(n=>n.id===id);assert.equal(n.renderer.receiveShadows,true);assert.equal(n.renderer.reflectionProbeUsage,1);assert.equal(n.layer,4);}
  const sun=graph.nodes.find(n=>n.id==='372289713/1727464136580948766');assert.equal(sun.active,true);assert.equal(sun.light.shadows.type,1);assert.equal(sun.light.shadows.strength,.6);assert.equal(sun.light.cullingMask,4066832);
  browser=await chromium.launch({channel:'chrome',headless:true,chromiumSandbox:true});report.browser=browser.version();
  const page=await browser.newPage({viewport:{width:1600,height:1000},locale:'zh-TW'});page.setDefaultTimeout(240000);
  page.on('pageerror',error=>report.pageErrors.push(error.stack));page.on('console',m=>report.console.push({type:m.type(),text:m.text()}));
  await page.goto(base+'/?source=preview-r9-fix2-lighting',{waitUntil:'networkidle'});await page.waitForFunction(()=>!document.querySelector('#progress').classList.contains('busy'));await page.waitForLoadState('networkidle');
  const token=await page.locator('meta[name=workbench-token]').getAttribute('content'),stateResponse=await page.request.get(base+'/api/state',{headers:{'X-Workbench-Token':token}}),state=await stateResponse.json();
  assert.equal(state.dirty,false);report.served=[];
  for(const file of ['ui/scene-view.js','ui/water-shadow.js','ui/environment-preview.js','ui/water-material.js','ui/water-effects.js','ui/material-adapters.js','vendor/loaders/EXRLoader.js']){const r=await page.request.get(base+'/'+file);const digest=sha(await r.body());assert.equal(digest,sha(await fs.readFile(path.join(root,'src/SceneWorkbench.Web/wwwroot',file))));report.served.push({file,sha256:digest});}
  await page.evaluate(()=>{const f=document.createElement('iframe');f.id='qa-real-scene';f.style.cssText='position:fixed;inset:0;width:1200px;height:800px;z-index:10000;border:0';f.srcdoc='<html style="--color-canvas:#202020"><body style="margin:0;background:#202020;color:white"><div style="height:30px">QA：原始 Map_60001 資料與產品 SceneView 模組；僅暫時切換記憶體預覽旗標</div><div id="viewport" style="width:1200px;height:700px"></div><span id="renderer-state"></span><input id="grid-toggle" type="checkbox"><input id="helpers-toggle" type="checkbox"><button id="translate"></button><button id="focus-object"></button><button id="frame-scene"></button></body></html>';document.body.append(f);});
  const frame=page.frameLocator('#qa-real-scene');await frame.locator('#viewport').waitFor();const child=page.frames().find(f=>f.parentFrame()===page.mainFrame());
  report.nativeRenderer=await child.evaluate(async({graph,items,token})=>{
   const THREE=await import('/vendor/three.module.js'),{SceneView}=await import('/ui/scene-view.js'),{AssetPreview}=await import('/ui/asset-preview.js');
   const warnings=[],assets=new AssetPreview(token,message=>warnings.push(message)),view=new SceneView(()=>{},()=>{});view.renderer.setAnimationLoop(null);
   await view.setGraph(graph,assets,items,false);view.select('222870');view.focus();view.select(null);
   const water=[];view.root.traverse(object=>{const materials=Array.isArray(object.material)?object.material:[object.material];if(object.isMesh&&materials.some(m=>m.userData.water))water.push(object);});
   const context={view,assets,water,graph};window.__qaLighting=context;
   const source=view.nativeWaterLight();return {waterMeshes:water.length,environment:assets.environmentPreview.reference,lightingPath:assets.environmentPreview.lightingPath,sun:{id:source.id,strength:source.shadows.strength,type:source.shadows.type,cullingMask:source.cullingMask},warnings};
  },{graph,items:state.items,token});
  assert.equal(report.nativeRenderer.waterMeshes,2);assert.equal(report.nativeRenderer.environment.sha256,cube.sha256);
  const probe=await child.evaluate(async()=>{
   const {view,water,graph}=window.__qaLighting,gl=view.renderer.getContext(),size=view.renderer.getDrawingBufferSize(new (await import('/vendor/three.module.js')).Vector2());
   const helpers=[view.grid,view.selectionBox,view.gizmo.getHelper(),...view.helperGroups];
   const capture=()=>{view.currentWaterLight=view.nativeWaterLight();view.waterShadow.render(view.scene,view.root,view.currentWaterLight,helpers);view.waterDepth.render(view.scene,view.camera,view.root,helpers);view.renderer.render(view.scene,view.camera);gl.finish();const pixels=new Uint8Array(size.x*size.y*4);gl.readPixels(0,0,size.x,size.y,gl.RGBA,gl.UNSIGNED_BYTE,pixels);return pixels;};
   const difference=(a,b)=>{let changedPixels=0,total=0,maxChannel=0;for(let i=0;i<a.length;i+=4){let sum=0;for(let c=0;c<3;c++){const d=Math.abs(a[i+c]-b[i+c]);sum+=d;maxChannel=Math.max(maxChannel,d);}if(sum>3)changedPixels++;total+=sum;}return {changedPixels,absoluteRgbDifference:total,maxChannel};};
   const materials=[...new Set(water.flatMap(mesh=>Array.isArray(mesh.material)?mesh.material:[mesh.material]))];
   capture();capture();const baseline=capture(),repeat=capture();
   materials.forEach(m=>{m.uniforms.uEnvironmentAvailable.value=0;});const reflectionOff=capture();materials.forEach(m=>{m.uniforms.uEnvironmentAvailable.value=1;});
   const flags=water.map(mesh=>mesh.userData.nativeRenderer.receiveShadows);water.forEach(mesh=>{mesh.userData.nativeRenderer.receiveShadows=false;});const shadowsOff=capture();water.forEach((mesh,i)=>{mesh.userData.nativeRenderer.receiveShadows=flags[i];});
   const restored=capture(),source=view.nativeWaterLight();
   const activeUniforms=[];for(const material of materials){const entry=view.renderer.properties.get(material).currentProgram;if(entry)for(let i=0;i<gl.getProgramParameter(entry.program,gl.ACTIVE_UNIFORMS);i++)activeUniforms.push(gl.getActiveUniform(entry.program,i).name);}
   return {size:[size.x,size.y],shadowCasters:view.waterShadow.casterCount,shadowResolution:view.waterShadow.resolution,sourceLight:source.id,sourceStrength:source.shadows.strength,
    baselineRepeatDifference:difference(baseline,repeat),reflectionDifference:difference(baseline,reflectionOff),shadowDifference:difference(baseline,shadowsOff),restoreDifference:difference(baseline,restored),activeUniforms:[...new Set(activeUniforms)],glError:gl.getError()};
  });
  report.probe=probe;assert.ok(probe.reflectionDifference.changedPixels>0,'Actual source reflection must affect scene pixels');assert.ok(probe.shadowDifference.changedPixels>0,'Native water receive-shadows flag must affect scene pixels');
  // Pixel-readback quantization is checked separately from semantic on/off effects; never compare PNG bytes or claim Unity parity.
  assert.ok(probe.restoreDifference.maxChannel<=3&&probe.restoreDifference.absoluteRgbDifference<=Math.max(1024,probe.baselineRepeatDifference.absoluteRgbDifference*4),JSON.stringify(probe.restoreDifference));
  assert.equal(probe.glError,0);assert.ok(probe.shadowCasters>0);assert.equal(probe.sourceLight,sun.id);
  for(const uniform of ['uEnvironment','uEnvironmentAvailable','_ReflectionStrength','_ReflectionFresnel','uShadowMap','uShadowMatrix','_ShadowStrength'])assert.ok(probe.activeUniforms.includes(uniform),uniform);
  await frame.locator('#viewport').screenshot({path:path.join(out,'real-map-source-reflection-and-shadow.png')});
  report.steps.push({name:'Actual Map_60001 SceneView: exact original EXR/GUID and active light; temporary reflection/receiver switches change pixels and restore',status:'PASS',probe});
  report.negative=await child.evaluate(async()=>{
   const {nativeLightApplies,nativeShadowApplies}=await import('/ui/water-shadow.js'),{validateReflectionExr,reflectionCubeFromStrip}=await import('/ui/environment-preview.js');
   const {view}=window.__qaLighting,light=view.nativeWaterLight(),renderer={renderingLayerMask:1};let malformedRejected=false,oversizedRejected=false;
   try{validateReflectionExr(new ArrayBuffer(8));}catch{malformedRejected=true;}
   try{reflectionCubeFromStrip({width:12,height:3,data:new Float32Array(12*3*4)});}catch{oversizedRejected=true;}
   return {includedWater:nativeLightApplies(renderer,4,light),excludedLayer:nativeLightApplies(renderer,0,light),excludedRenderingMask:nativeLightApplies({renderingLayerMask:2},4,light),inactiveShadows:nativeShadowApplies(renderer,4,{...light,shadows:{...light.shadows,type:0}}),unsupportedMatrix:nativeShadowApplies(renderer,4,{...light,shadows:{...light.shadows,cullingMatrixOverride:true}}),malformedRejected,oversizedRejected};
  });
  assert.deepEqual(report.negative,{includedWater:true,excludedLayer:false,excludedRenderingMask:false,inactiveShadows:false,unsupportedMatrix:false,malformedRejected:true,oversizedRejected:true});
  report.steps.push({name:'Native culling/rendering masks and shadow-off/unsupported matrix respected; invalid EXR data rejected before use',status:'PASS'});
  assert.deepEqual(report.pageErrors,[]);assert.deepEqual(report.console.filter(m=>m.type==='error'),[]);report.status='PASS';
 }catch(error){report.status='FAILED';report.error=error.stack;process.exitCode=1;}
 finally{await fs.writeFile(path.join(out,'result.json'),JSON.stringify(report,null,2));if(browser)await browser.close();console.log(JSON.stringify({status:report.status,error:report.error,steps:report.steps.length,probe:report.probe}));}
})();

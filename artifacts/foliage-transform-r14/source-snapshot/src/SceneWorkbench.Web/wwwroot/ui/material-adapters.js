import * as THREE from '../vendor/three.module.js';
import { createWaterMaterial } from './water-material.js';
import { waterPinMatches } from './water-effects.js';
import { historicalPlantState } from './historical-surface.js';

const pins = fetch('/ui/shader-adapters.json').then((r) => r.json());
const surfacePins = fetch('/ui/surface-adapters.json').then((r) => r.json());
export function unresolvedMaterial(label) {
  const material = new THREE.MeshStandardMaterial({ color: 0xb8a88b, roughness: .9, side: THREE.DoubleSide });
  material.userData = { previewMode: 'unresolved', adapterStatus: `未解析材質：${label}；以實心診斷表面保留幾何。` };
  return material;
}
const maskNames = ['_1_0_c_Color', '_1_1_HairColor', '_1_2_SkinColor', '_2_0_e_Color0', '_2_1_e_Color1', '_2_2_e_Color2', '_3_0_e_Color3', '_3_1_e_Color4', '_4_2_a_Color', '_3_2_s_Color', '_4_0_w_Color0', '_4_1_w_Color1', '_5_0_p_Color'];
const vertex = `
attribute vec2 uv1; attribute vec2 uv2; attribute vec2 uv3;
varying vec2 vUv; varying vec2 vUv1; varying vec2 vUv2; varying vec2 vUv3;
varying vec3 vNormal; varying vec3 vView; varying vec3 vWorldView; varying float vNormalY;
void main(){vUv=uv;vUv1=uv1;vUv2=uv2;vUv3=uv3;vNormal=normalize(normalMatrix*normal);vNormalY=normal.y;
vec4 view=modelViewMatrix*vec4(position,1.0);vView=-view.xyz;vWorldView=cameraPosition-(modelMatrix*vec4(position,1.0)).xyz;gl_Position=projectionMatrix*view;}`;
const helpers = `
varying vec2 vUv; varying vec2 vUv1; varying vec2 vUv2; varying vec2 vUv3;
varying vec3 vNormal; varying vec3 vView; varying vec3 vWorldView; varying float vNormalY;
vec3 sat(vec3 v){return clamp(v,0.0,1.0);} vec4 sat(vec4 v){return clamp(v,0.0,1.0);}
vec3 hard(vec3 source,vec3 dest){return mix(2.0*source*dest,1.0-2.0*(1.0-source)*(1.0-dest),step(vec3(.5),source));}
vec3 overlay(vec3 source,vec3 dest){return hard(dest,source);}
float maskWeight(float value,float key){return clamp(1.0-(sqrt(3.0)*abs(value-key)-.15)/.00001,0.0,1.0);}
vec3 surfaceShade(vec3 base,float shine,vec3 mainColor,float role){
vec3 n=normalize(vNormal),v=normalize(vView),l=normalize(vec3(.4,.8,.5));
float fres=pow(1.0-dot(n,v),2.0),inv=1.0-fres;
float rim=clamp(mix(smoothstep(.3,1.5,inv),smoothstep(0.0,1.5,fres),smoothstep(-1.0,1.5,vNormalY)*smoothstep(0.0,1.5,fres)),0.0,1.0);
vec3 shaded=mix(base*vec3(.65,.5,.6),base,rim);
float light=.25*pow(max(dot(normalize(v+l),n),0.0),max(.01,shine)*128.0)+(max(dot(n,l),0.0)+.2)*mainColor.r;
float spec=smoothstep(.5,1.0,light),roundRim=step(.5,smoothstep(role>.5?.1:.3,.5,inv));
float highlight=mix(min(spec,role>.5?.2:base.r*.2),min(spec,role>.5?.15:.18),roundRim);
vec3 result=shaded+highlight;return sat(result+(2.0*result-1.0)*(role>.5?.25:.15));}
`;

// Fixed ports of the named, hash-pinned project Shaders. No HLSL/node graph is executed or compiled.
export async function createMaterial(snapshot, assets) {
  const definitions = new Map(snapshot.properties.map((p) => [p.name, p]));
  const f = (name, fallback = 0) => definitions.has(name) ? definitions.get(name).integerStorage ? `float(f${name})` : `f${name}` : String(Number(fallback).toFixed(6));
  const c = (name, fallback = '.5,.5,.5,1.0') => definitions.has(name) ? `p${name}` : `vec4(${fallback})`;
  const supportedPin = (await pins).find((p) => p.guid === snapshot.shaderGuid && p.sha256 === snapshot.shaderHash);
  if (snapshot.previewKind === 'water-template') {
    const pin = (await surfacePins).water;
    if (waterPinMatches(snapshot, pin)) return createWaterMaterial(snapshot, assets);
    const changed = Object.keys(pin.dependencies ?? {}).filter(name => snapshot.shaderDependencies?.[name] !== pin.dependencies[name]);
    assets.warn(`StylizedWater2：範本或 Libraries 來源已變更／缺少${changed.length ? '（' + changed.join('、') + '）' : ''}，固定適配需要重新驗證。`);
    return unresolvedMaterial(snapshot.path);
  }
  let declaration = 'uniform float uPreviewTime;\n', samplers = '', uniforms = { uPreviewTime: { value: assets.previewTime ?? 0 } };
  const textureData = new Map();
  for (const property of snapshot.properties) {
    if (['2D', '2DArray'].includes(property.kind)) {
      let data;
      try { data = property.texturePath ? await assets.texture(property.texturePath) : null; }
      catch (error) { assets.warn(`${property.name}：${error.message}`); }
      const texture = data?.texture ?? assets.white;
      textureData.set(property.name, data);
      const key = `t${property.name}`, transform = `s${property.name}`, grid = `g${property.name}`;
      uniforms[key] = { value: texture }; uniforms[transform] = { value: new THREE.Vector4(...(property.scale ?? [1, 1]), ...(property.offset ?? [0, 0])) };
      uniforms[grid] = { value: new THREE.Vector2(Math.max(1, data?.meta.columns ?? 1), Math.max(1, data?.meta.rows ?? 1)) };
      declaration += `uniform sampler2D ${key};uniform vec4 ${transform};uniform vec2 ${grid};\n`;
      samplers += `vec4 sample${property.name}(vec2 uv){return texture2D(${key},uv*${transform}.xy+${transform}.zw);}\n`;
      if (property.kind === '2DArray') samplers += `vec4 array${property.name}(vec2 uv,float layer){vec2 grid=${grid};layer=clamp(floor(layer),0.0,grid.x*grid.y-1.0);vec2 cell=vec2(mod(layer,grid.x),grid.y-1.0-floor(layer/grid.x));return texture2D(${key},(cell+clamp(uv,vec2(.001),vec2(.999)))/grid);}\n`;
    } else if (['Color', 'Vector'].includes(property.kind)) {
      uniforms[`p${property.name}`] = { value: new THREE.Vector4(...property.value) }; declaration += `uniform vec4 p${property.name};\n`;
    } else if (property.value.length) { uniforms[`f${property.name}`] = { value: property.value[0] }; declaration += `uniform ${property.integerStorage ? 'int' : 'float'} f${property.name};\n`; }
  }
  const sample = (name, uv = 'vUv', fallback = 'vec4(1.0)') => definitions.has(name) ? `sample${name}(${uv})` : fallback;
  const raw = (name, uv, fallback = 'vec4(1.0)') => definitions.has(name) ? `texture2D(t${name},${uv})` : fallback;
  const uv = (name) => definitions.has(name) ? `(vUv*s${name}.xy+s${name}.zw)` : 'vUv';
  const face = definitions.has('_Face_Textures') && textureData.get('_Face_Textures') ? `array_Face_Textures(vUv1,${f('_Face_ID')})` : 'vec4(0.0)';
  const mask = (name, coordinate = 'vUv') => {
    let body = `vec4 mask=${sample(name, coordinate)};vec3 tint=vec3(.4);`;
    maskNames.forEach((property, i) => { if (definitions.has(property)) body += `tint=mix(tint,${c(property)}.rgb*.1,maskWeight(mask.${'rgb'[Math.floor(i / 5)]},${(((i % 5) + 1) * .196).toFixed(3)}));`; });
    return body;
  };
  let body = '', transparent = false, side = THREE.FrontSide, status = '', historicalReference = null;
  if (snapshot.isAse && !supportedPin) {
    assets.warn(`${snapshot.shaderName}：Shader 來源已改變／尚無固定適配；保留幾何與未適配診斷，請重新驗證。`);
    return unresolvedMaterial(snapshot.path);
  }
  const name = snapshot.shaderName;
  if (['ASE_RoleMask', 'ASE_MonsterMask', 'ASE_WeaponMask'].includes(name)) {
    body = `vec4 mask=${sample('_ColorMask')};`;
    if (name === 'ASE_WeaponMask') body += 'color=mask.rgb;';
    else if (name === 'ASE_MonsterMask') body += `vec4 face=${face};float faceWeight=clamp(1.0-(distance(vec3(0,0,.9803922),face.rgb)-.5)/.3,0.0,1.0);color=mix(mask.rgb,face.rgb,faceWeight);`;
    else body += `vec4 face=${face};float faceWeight=smoothstep(.1,.2,clamp(1.0-(distance(vec3(0,0,.9803922),face.rgb)-.5)/.3,0.0,1.0));vec3 faceColor=mix(vec3(.5882353,0,0),vec3(0,0,.9803922),faceWeight);
vec3 maskColor=mix(mask.rgb,vec3(.3921569,0,0),clamp(step(.02,vUv2.x)+step(.02,vUv2.y),0.0,1.0));color=mix(faceColor,maskColor,mask.a*(1.0-clamp(step(.02,vUv3.x)+step(.02,vUv3.y),0.0,1.0)));`;
    body += 'alpha=1.0;'; status = '固定 ASE 色碼遮罩／多 UV／Face array 適配 · 角色調色用遮罩，Unity 取樣待比對';
  } else if (name === 'ASE_Spine2D') {
    body = mask('_ColorMask1') + `vec4 source=${sample('_ColorTextures')};vec3 base=sat(mix(source.rgb,hard(tint*1.2,source.rgb),.55))*smoothstep(0.0,1.0,source.a);
vec3 eye=sat(mix(source.rgb,hard(${c('_13_EyeColor_')}.rgb*.11,source.rgb),.55));base=mix(base,eye,clamp(1.0-(distance(vec3(0,0,1),mask.rgb)-.5)/.3,0.0,1.0));color=sat(mix(base,2.0*base*base+base*base*(1.0-2.0*base),.2));alpha=source.a;`;
    transparent = true; side = THREE.DoubleSide; status = '固定 ASE Spine 遮罩／調色／柔光／Alpha 適配 · Stencil 與骨架動畫待驗證';
  } else if (['ASE_Role', 'ASE_Monster', 'ASE_Weapon'].includes(name)) {
    const role = name.startsWith('ASE_Role'), weapon = name.startsWith('ASE_Weapon'), spine = name === 'ASE_Spine2D';
    body = mask(weapon || spine ? '_ColorMask1' : '_ColorMask');
    if (weapon) body = `vec4 mask=${sample('_ColorMask1')};vec3 tint=vec3(.4);tint=mix(tint,${c('_4_0_w_Color0')}.rgb*.1,maskWeight(mask.b,.196));tint=mix(tint,${c('_4_1_w_Color1')}.rgb*.1,maskWeight(mask.b,.392));`;
    body += `vec4 source=${sample('_ColorTextures')};vec3 base=sat(mix(source.rgb,hard(tint*1.35,source.rgb),.55));`;
    if (role) body += `vec4 skin=${sample('_Skin', 'vUv3')};vec3 skinColor=sat(mix(skin.rgb,hard(${c('_1_2_SkinColor')}.rgb*.1,skin.rgb),.5));
vec3 bra=sat(mix(${c('_BraColor')}.rgb,hard(${c('_2_0_e_Color0')}.rgb*.15,${c('_BraColor')}.rgb),.55));
skinColor=mix(skinColor,bra,clamp(1.0-(distance(vec3(1,0,1),skin.rgb)-.5)/.3,0.0,1.0));
float skinFlag=clamp(step(.02,vUv3.x)+step(.02,vUv3.y),0.0,1.0);base=mix(skinColor,base,source.a*(1.0-skinFlag));
vec3 hair=hard(${c('_HairColor1')}.rgb,${c('_1_1_HairColor')}.rgb*.1);hair=mix(hair,hair*vec3(2,2.1,2),${sample('_0000_hair_t', 'vec2(vUv2.x,mix(vUv2.y+.2,normalize(vView).y,.25))')}.r);
base=mix(base,hair,clamp(step(.02,vUv2.x)+step(.02,vUv2.y),0.0,1.0));`;
    if (role || name.startsWith('ASE_Monster')) body += `vec4 face=${face};vec3 eye=sat(mix(${c('_EyeColorBase')}.rgb,hard(${c('_13_EyeColor_')}.rgb*.15,${c('_EyeColorBase')}.rgb),.5));
face.rgb=mix(face.rgb,eye,clamp(1.0-(distance(vec3(0,0,1),face.rgb)-.5)/.3,0.0,1.0));base=mix(base,face.rgb,face.a);`;
    body += `base=surfaceShade(base,${f('_Shininess', .1)},${role ? c('_MainColor') + '.rgb' : 'base'},${role ? '1.0' : '0.0'});color=${role || name.startsWith('ASE_Monster') ? `overlay(base,${c('_FettleColor')}.rgb)` : `hard(base,${c('_FettleColor')}.rgb)`};alpha=${spine ? 'source.a' : '1.0'};`;
    transparent = spine; side = spine ? THREE.DoubleSide : THREE.FrontSide;
    status = '固定 ASE 表面適配 · 遮罩調色／多組 UV／Face array／Fresnel；陰影、烘焙與特殊遮擋效果待比對';
  } else if (name === 'ASE_SceneWall') {
    body = `color=${sample('_Texture')}.rgb*mix(.3,1.0,${f('_AO', .8)});alpha=1.0;`;
    transparent = true; status = '固定 ASE 牆面貼圖／AO · SceneDepth 軟邊相交尚未重現';
  } else if (name === 'ASE_LightEffectAfterimage') {
    body = `float rim=pow(1.0-dot(normalize(vNormal),normalize(vView)),2.0);color=mix(${c('_Color2')}.rgb,${c('_Color1')}.rgb,${f('_Alpha', 1)})+vec3(.6,.8,.4)*smoothstep(0.0,.5,rim);alpha=${f('_Alpha', 1)};`;
    transparent = true; side = THREE.DoubleSide; status = '固定 ASE 殘影色彩／Alpha 預覽 · 粒子時間與深度效果待比對';
  } else if (name === 'ASE/ScreenText') {
    body = `color=(${raw('_TextureSample0', `${uv('_TextureSample0')}+uPreviewTime*${c('_Vector0')}.xy`)}.r*${c('_Color0')}+${sample('_TextureSample1')}*${c('_Color1')}).rgb;alpha=1.0;`;
    status = '固定 ASE 雙貼圖／色彩／UV 流動適配 · 可調整預覽時間';
  } else if (name === 'ASE/StarWindow') {
    body = `vec2 viewDir=normalize(vWorldView).xy;color=(${raw('_TextureSample1', `vUv+(${f('_Float0', 1)}-1.0)*viewDir+vec2(uPreviewTime*${f('_Float3')})`)}+${raw('_TextureSample0', `vUv+(${f('_Float1', 1)}-1.0)*viewDir+vec2(uPreviewTime*${f('_Float2')})`)}).rgb;alpha=1.0;`;
    status = '固定 ASE 雙層星空／視差／時間偏移適配';
  } else if (name === 'Custom/UIFlowingLight') {
    body = `color=(${raw('_MainTex', `${uv('_MainTex')}+uPreviewTime*${c('_MainSpeed')}.xy`)}*${sample('_MainTex2')}*${c('_MainCoor')}*${sample('_MaskTex1')}.a).rgb;alpha=1.0;`;
    transparent = true; status = '固定 ASE 雙貼圖／遮罩／主色／流速適配 · UI 混色與裁切待比對';
  } else if (name === 'UI_Animation') {
    body = `vec2 grid=max(vec2(1.0),${c('_RowsXY')}.xy);float total=grid.x*grid.y;float frame=fract((${f('_PlayBar')}*2.0*${f('_Speed', 12)}+.0001)/total);vec2 frameUv=vUv/grid+floor(vec2(total,grid.y)*vec2(frame,1.0-frame))/grid;` + mask('_ColorMask', 'frameUv');
    body += `tint=mix(tint,${c('_13_EyeColor_')}.rgb*.1,maskWeight(mask.b,.784));vec4 source=${sample('_TextureSample0', 'frameUv')};color=sat(mix(source.rgb,hard(tint*1.35,source.rgb),.55));alpha=source.a;`;
    transparent = true; side = THREE.DoubleSide; status = '固定 ASE Flipbook／PlayBar／遮罩調色適配 · UI 裁切待比對';
  } else if (name === 'UI_ProfilePhotoWithMask') {
    body = mask('_ColorMask1') + `vec4 source=${sample('_ColorTextures')};vec3 base=mix(source.rgb,hard(tint*1.35,source.rgb),.55)*source.a;
vec3 eye=sat(mix(source.rgb,hard(${c('_13_EyeColor_')}.rgb*.11,source.rgb),.55));base=mix(base,eye,clamp(1.0-(distance(vec3(0,0,1),mask.rgb)-.5)/.3,0.0,1.0));
color=sat(mix(base,2.0*base*base+base*base*(1.0-2.0*base),.2));alpha=source.a;
if(${f('_UseShapeMask')}>.5){alpha*=${sample('_ShapeMask')}.a;}else{float radius=length((vUv-.5)*2.0);float edge=max(.00001,${f('_EdgeLength')});float fade=smoothstep(${f('_MaskClip', .01)}-edge,${f('_MaskClip', .01)},1.0-radius);float hardEdge=smoothstep(${f('_MaskClip', .01)}-edge*.5,${f('_MaskClip', .01)},1.0-radius);alpha*=fade*${c('_EdgeColor', '1,1,1,1')}.a;color=mix(color,${c('_EdgeColor', '1,1,1,1')}.rgb,clamp((fade-hardEdge)*2.0,0.0,1.0));}
if(${f('_UseUIAlphaClip')}>.5&&alpha<.001)discard;`;
    transparent = true; side = THREE.DoubleSide; status = '固定 ASE 頭像／遮罩調色／形狀遮罩／邊框適配 · Canvas Stencil／ClipRect 待驗證';
  } else if (supportedPin) {
    const main = ['_TextureSample0', '_MainTex', '_Texture', '_ColorTextures'].find((p) => definitions.has(p));
    body = `vec4 source=${main ? sample(main) : 'vec4(1.0)'};color=source.rgb;alpha=source.a;`;
    transparent = true; side = THREE.DoubleSide; status = 'ASE UI 來源貼圖預覽 · 此 Shader 的 UI 遮罩／動畫／特殊混色尚未完整適配';
  } else if (snapshot.previewKind === 'saved-properties') {
    const historical = await historicalPlantState(snapshot, assets, (await surfacePins).historicalPlant);
    const names = ['_BaseMap', '_MainTex', '_Texture', '_ColorTextures', '_Albedo'];
    const main = names.find(p => definitions.get(p)?.kind === '2D' && definitions.get(p)?.texturePath) ?? names.find(p => definitions.get(p)?.kind === '2D');
    const tint = definitions.has('_BaseColor') ? '_BaseColor' : '_Color';
    body = `vec4 source=${main ? sample(main) : 'vec4(1.0)'}*${c(tint, '1,1,1,1')};color=source.rgb*(.55+.45*max(dot(normalize(vNormal),normalize(vec3(.4,.8,.5))),0.0));alpha=source.a;`;
    transparent = (definitions.get('_Surface')?.value[0] ?? 0) > 0 || (definitions.get('_Mode')?.value[0] ?? 0) >= 2;
    side = definitions.get('_Cull')?.value[0] === 0 ? THREE.DoubleSide : THREE.FrontSide;
    status = `Shader 未解析 · 唯讀原生 ${main ?? '無可識別主貼圖'}／色彩／Alpha${definitions.has('_AlphaCutoff') ? '／_AlphaCutoff 裁切' : ''} 近似；原 Shader、特殊效果與 Unity 光照未適配。`;
    if (historical) {
      // The graph disallows material render-state overrides. Stale Standard/Lit
      // _Cull/_Surface values are not the graph's actual render state.
      side = THREE.DoubleSide; transparent = false;
      status = '歷史同 GUID ShaderGraph 參考 · 雙面／Alpha 裁切；目前 Shader 來源缺少，版本等價、UV 風動與 Unity 光照未驗證。';
      historicalReference = { source: historical.source, sha256: historical.sha256, alphaCutoff: historical.alphaCutoff };
    }
    assets.warn(`${snapshot.path}：${status}`);
  } else if (name.startsWith('Universal Render Pipeline/')) {
    const main = definitions.has('_BaseMap') ? '_BaseMap' : '_MainTex';
    body = `vec4 source=${sample(main)}*${c('_BaseColor', '1,1,1,1')};color=source.rgb;alpha=source.a;`;
    if (!name.endsWith('/Unlit')) body += `float diffuse=max(dot(normalize(vNormal),normalize(vec3(.4,.8,.5))),0.0);color*=.55+.45*diffuse;`;
    transparent = (definitions.get('_Surface')?.value[0] ?? 0) > 0; status = 'URP 基本表面預覽 · 貼圖／色彩／Alpha；Unity 光照與後製待校準';
  } else {
    assets.warn(`${name || snapshot.path}：Shader 尚未適配；保留實心幾何與未適配診斷。`);
    return unresolvedMaterial(snapshot.path);
  }
  const savedCutoff = snapshot.previewKind === 'saved-properties' && definitions.has('_AlphaCutoff');
  const cutoff = historicalReference || savedCutoff || definitions.get('_AlphaClip')?.value[0] > 0 || snapshot.keywords.includes('_ALPHATEST_ON');
  if (cutoff) body += `if(alpha<${historicalReference ? historicalReference.alphaCutoff.toFixed(6) : f(savedCutoff ? '_AlphaCutoff' : '_Cutoff', .5)})discard;`;
  const material = new THREE.ShaderMaterial({ uniforms, vertexShader: vertex,
    fragmentShader: declaration + helpers + samplers + `void main(){vec3 color=vec3(1.0);float alpha=1.0;${body}gl_FragColor=vec4(sat(color),clamp(alpha,0.0,1.0));}`,
    transparent, depthWrite: !transparent, side, toneMapped: false });
  material.name = snapshot.path; material.userData = { materialPath: snapshot.path, adapterStatus: status, shaderHash: snapshot.shaderHash, gammaWorkflow: true,
    previewMode: ['saved-properties', 'water-template'].includes(snapshot.previewKind) ? 'approximation' : 'source-surface' };
  if (historicalReference) material.userData.historicalRenderState = historicalReference;
  if(cutoff){
    const main=['_BaseMap','_MainTex','_Texture','_TextureSample0','_ColorTextures'].find(name=>definitions.get(name)?.texturePath&&textureData.get(name)?.texture);
    if(main)material.userData.shadowCutout={texture:textureData.get(main).texture,scale:definitions.get(main).scale??[1,1],offset:definitions.get(main).offset??[0,0],cutoff:historicalReference?.alphaCutoff??definitions.get(savedCutoff?'_AlphaCutoff':'_Cutoff')?.value[0]??.5};
  }
  return material;
}

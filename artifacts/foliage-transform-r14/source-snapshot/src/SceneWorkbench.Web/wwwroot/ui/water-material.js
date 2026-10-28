import * as THREE from '../vendor/three.module.js';
import { waterFeatures } from './water-effects.js';

export async function createWaterMaterial(snapshot, assets) {
  const feature = waterFeatures(snapshot), properties = new Map(snapshot.properties.map(p => [p.name, p]));
  const number = (name, fallback = 0) => properties.get(name)?.value[0] ?? fallback;
  const vector = (name, fallback) => properties.get(name)?.value ?? fallback;
  const uniforms = { uPreviewTime: { value: assets.previewTime ?? 0 }, uSceneDepth: { value: assets.white }, uDepthAvailable: { value: 0 },
    uViewportSize: { value: new THREE.Vector2(1, 1) }, uProjectionInverse: { value: new THREE.Matrix4() }, uViewInverse: { value: new THREE.Matrix4() },
    uLightDirection: { value: assets.waterLight?.direction ?? new THREE.Vector3(.4, .8, .5).normalize() }, uLightColor: { value: assets.waterLight?.color ?? new THREE.Vector3(1, 1, 1) },
    uEnvironment: {value:assets.environmentPreview?.cube??null},uEnvironmentAvailable:{value:assets.environmentPreview?.available?1:0},uEnvironmentIntensity:{value:assets.environmentPreview?.intensity??0},uRendererProbeUsage:{value:1},
    uShadowMap:{value:assets.white},uShadowAvailable:{value:0},uShadowMatrix:{value:new THREE.Matrix4()},uShadowBias:{value:0},uShadowNormalBias:{value:0},uShadowStrength:{value:0},uRendererReceiveShadows:{value:1} };
  let declaration = '';
  const scalarNames = ['_Speed','_WaveSpeed','_WaveHeight','_WaveDistance','_WaveCount','_WaveSteepness','_WaveNormalStr','_WorldSpaceUV','_VertexColorDepth','_VertexColorWaveFlattening','_VertexColorFoam','_DepthVertical','_DepthHorizontal','_DepthExp','_EdgeFade','_WaveTint','_NormalTiling','_NormalSpeed','_NormalStrength','_DistanceNormalsTiling','_HorizonDistance','_IntersectionTiling','_IntersectionSpeed','_IntersectionLength','_IntersectionFalloff','_IntersectionClipping','_IntersectionRippleDist','_IntersectionRippleStrength','_IntersectionSource','_SunReflectionSize','_SunReflectionStrength','_SunReflectionDistortion','_TranslucencyStrength','_TranslucencyExp','_TranslucencyCurvatureMask','_FoamTiling','_FoamSpeed','_FoamSize','_FoamWaveMask','_FoamWaveMaskExp','_ShadowStrength','_ReflectionStrength','_ReflectionDistortion','_ReflectionBlur','_ReflectionFresnel','_ReflectionLighting'];
  for (const name of scalarNames) { uniforms[name] = { value: number(name, name === '_WaveCount' ? 1 : 0) }; declaration += `uniform float ${name};`; }
  for (const [name, fallback] of Object.entries({ _BaseColor:[0,.36,.75,.96],_ShallowColor:[0,.84,1,.4],_HorizonColor:[.24,.9,1,.47],_IntersectionColor:[1,1,1,1],_FoamColor:[1,1,1,1],_Direction:[1,1,0,0],_WaveDirection:[1,1,1,1],_WaveFadeDistance:[0,150,0,0],_DistanceNormalsFadeDist:[100,300,0,0] })) {
    uniforms[name] = { value: new THREE.Vector4(...vector(name, fallback)) }; declaration += `uniform vec4 ${name};`;
  }
  for (const name of ['_BumpMap','_BumpMapLarge','_IntersectionNoise','_FoamTex']) {
    let data; const path = properties.get(name)?.texturePath;
    if (path) { try { data = await assets.texture(path); } catch (error) { assets.warn(`StylizedWater2 ${name}：${error.message}`); } }
    uniforms[name] = { value: data?.texture ?? assets.white }; declaration += `uniform sampler2D ${name};`;
  }
  const wave = feature.waves ? `
vec3 wave(vec2 source,vec3 world,vec4 vertexColor){
 vec2 time=uPreviewTime*_Speed*_Direction.xy*_WaveSpeed;
 float count=clamp(floor(_WaveCount),1.0,5.0),fade=clamp((_WaveFadeDistance.y-distance(cameraPosition.xz,world.xz))/max(.0001,_WaveFadeDistance.y-_WaveFadeDistance.x),0.0,1.0);
 vec4 amp=vec4(.3,.35,.25,.25),freq=vec4(1.3,1.35,1.25,1.25)*(1.0-_WaveDistance)*3.0;
 vec4 d1=vec4(.3,.85,.85,.25)*_WaveDirection,d2=vec4(.1,.9,-.5,-.5)*_WaveDirection;
 vec4 phase=vec4(time.x*1.2,time.y*1.375,time.x*1.1,time.y),steep=vec4(12.0)*_WaveSteepness*mix(1.0,5.0,1.0/count);
 vec3 offset=vec3(0.0);
 for(int i=0;i<6;i++){if(float(i)>count)break;freq*=1.0+float(i)/count;amp*=fade;
  vec4 a=freq*vec4(dot(d1.xy,source),dot(d1.zw,source),dot(d2.xy,source),dot(d2.zw,source))+phase;
  offset.y+=dot(sin(a),amp);offset.x+=dot(cos(a),steep*amp*vec4(d1.x,d1.z,d2.x,d2.z));offset.z+=dot(cos(a),steep*amp*vec4(d1.y,d1.w,d2.y,d2.w));}
 float mask=1.0-clamp(vertexColor.b*_VertexColorWaveFlattening,0.0,1.0);offset*= _WaveHeight*mask;offset.y/=count;offset.xz*=.01;return offset;}` : 'vec3 wave(vec2 source,vec3 world,vec4 vertexColor){return vec3(0.0);}';
  const vertex = `uniform float uPreviewTime;${declaration}attribute vec4 color;varying vec2 vUv;varying vec3 vWorld;varying vec3 vWorldNormal;varying vec4 vColor;varying float vEyeDepth;varying float vWave;
${wave}
void main(){vec4 world=modelMatrix*vec4(position,1.0);vec2 source=mix(uv,vec2(world.x,-world.z),_WorldSpaceUV);vec3 offset=wave(source,world.xyz,color);
 world.xyz+=vec3(offset.x,offset.y,-offset.z);vWorld=world.xyz;vUv=uv;vWorldNormal=normalize(mat3(modelMatrix)*normal);vColor=color;vWave=offset.y;
 vec4 view=viewMatrix*world;vEyeDepth=-view.z;gl_Position=projectionMatrix*view;}`;
  const fragment = `uniform float uPreviewTime;${declaration}uniform sampler2D uSceneDepth;uniform float uDepthAvailable;uniform vec2 uViewportSize;uniform mat4 uProjectionInverse;uniform mat4 uViewInverse;uniform vec3 uLightDirection;uniform vec3 uLightColor;
uniform samplerCube uEnvironment;uniform float uEnvironmentAvailable;uniform float uEnvironmentIntensity;uniform float uRendererProbeUsage;
uniform sampler2D uShadowMap;uniform float uShadowAvailable;uniform mat4 uShadowMatrix;uniform float uShadowBias;uniform float uShadowNormalBias;uniform float uShadowStrength;uniform float uRendererReceiveShadows;
varying vec2 vUv;varying vec3 vWorld;varying vec3 vWorldNormal;varying vec4 vColor;varying float vEyeDepth;varying float vWave;
vec3 normalSample(sampler2D image,vec2 uv,vec2 time){vec3 a=texture2D(image,uv+time*_NormalSpeed).xyz*2.0-1.0;vec3 b=texture2D(image,uv*.5-time*_NormalSpeed*.5).xyz*2.0-1.0;return normalize(vec3(a.xy+b.xy,max(.001,a.z*b.z)));}
void main(){vec2 uv=mix(vUv,vec2(vWorld.x,-vWorld.z),_WorldSpaceUV),time=uPreviewTime*_Speed*_Direction.xy;vec3 view=normalize(cameraPosition-vWorld),normal=normalize(vWorldNormal);
 float vertical=1000.0,eye=1000.0,available=uDepthAvailable*${feature.depth ? '1.0' : '0.0'};
 if(available>.5){vec2 screen=gl_FragCoord.xy/uViewportSize;float raw=texture2D(uSceneDepth,screen).r;vec4 p=uProjectionInverse*vec4(screen*2.0-1.0,raw*2.0-1.0,1.0);p/=p.w;vec3 world=(uViewInverse*p).xyz;vertical=max(0.0,vWorld.y-world.y);eye=max(0.0,-p.z-vEyeDepth);}
 float density=1.0;if(available>.5){float depth=1.0-exp(-eye*_DepthVertical*.1);float height=clamp(mix(vertical*_DepthHorizontal,1.0-exp(-vertical*_DepthHorizontal),_DepthExp),0.0,1.0);density=max(depth,height);}
 density*=clamp(density-vColor.g*_VertexColorDepth,0.0,1.0);vec4 base=mix(_ShallowColor,_BaseColor,density);base.rgb+=clamp(_WaveTint*vWave,0.0,1.0);float alpha=base.a;
 ${feature.normals ? `vec3 tangent=normalSample(_BumpMap,uv*_NormalTiling,time);${feature.distanceNormals ? 'float fade=clamp((_DistanceNormalsFadeDist.y-distance(cameraPosition,vWorld))/max(.0001,_DistanceNormalsFadeDist.y-_DistanceNormalsFadeDist.x),0.0,1.0);tangent=mix(normalSample(_BumpMapLarge,uv*_NormalTiling*_DistanceNormalsTiling,time*.5),tangent,fade);' : ''}normal=normalize(normal+vec3(tangent.x,0.0,-tangent.y)*_NormalStrength);` : ''}
 float intersection=0.0;
 ${feature.sharpIntersection || feature.smoothIntersection ? `float gradient=available>.5?1.0-clamp(exp(min(vertical,30.0))/max(.0001,_IntersectionLength),0.0,1.0):0.0;if(_IntersectionSource==1.0)gradient=vColor.r;else if(_IntersectionSource==2.0)gradient=clamp(gradient+vColor.r,0.0,1.0);
 float dist=clamp(gradient/max(.0001,_IntersectionFalloff),0.0,1.0);vec2 it=time*_IntersectionSpeed;float noise=texture2D(_IntersectionNoise,uv*_IntersectionTiling+it).r;
 ${feature.sharpIntersection ? 'float ripple=sin(it.y*10.0-gradient*_IntersectionRippleDist)*_IntersectionRippleStrength;intersection=step(_IntersectionClipping,clamp((noise+ripple)*dist+dist,0.0,1.0));' : 'float second=texture2D(_IntersectionNoise,uv*_IntersectionTiling*1.5-it).r;intersection=clamp(noise+second+dist,0.0,1.0)*dist;'}intersection*=_IntersectionColor.a;` : ''}
 float foam=0.0;
 ${feature.foam ? `vec2 fu=uv*_FoamTiling;float foamNoise=clamp(texture2D(_FoamTex,fu+time*_FoamSpeed).r+texture2D(_FoamTex,fu*.5-time*_FoamSpeed*.15).r,0.0,1.0);float mask=pow(mix(1.0,clamp(vWave,0.0,1.0),_FoamWaveMask),_FoamWaveMaskExp)*_FoamColor.a+vColor.a*_VertexColorFoam;foam=smoothstep(_FoamSize,1.0,foamNoise*clamp(mask,0.0,1.0));base.rgb=mix(base.rgb,_FoamColor.rgb,foam);` : ''}
 base.rgb=mix(base.rgb,_IntersectionColor.rgb,intersection);alpha=clamp(alpha+foam+intersection,0.0,1.0);
 float fresnel=pow(1.0-clamp(dot(view,normalize(vWorldNormal)),0.0,1.0),max(.001,_HorizonDistance));base.rgb=mix(base.rgb,_HorizonColor.rgb,fresnel*_HorizonColor.a);
 float edge=available>.5?clamp(vertical/max(.0001,_EdgeFade*.01),0.0,1.0):1.0;alpha*=edge;
 float shadow=1.0;
 ${feature.receiveShadows ? 'if(uShadowAvailable>.5&&uRendererReceiveShadows>.5){vec4 clip=uShadowMatrix*vec4(vWorld+normal*uShadowNormalBias,1.0);vec3 projected=clip.xyz/clip.w;if(all(greaterThanEqual(projected,vec3(0.0)))&&all(lessThanEqual(projected,vec3(1.0)))){float blocked=step(texture2D(uShadowMap,projected.xy).r,projected.z-uShadowBias);shadow=1.0-blocked*clamp(uShadowStrength*_ShadowStrength,0.0,1.0);}}' : ''}
 float reflectionMask=0.0;vec3 reflectionRadiance=vec3(0.0);
 ${feature.environmentReflection ? 'if(uEnvironmentAvailable>.5&&uRendererProbeUsage>.5){vec3 reflectionNormal=normalize(mix(normalize(vWorldNormal),normal,_ReflectionDistortion));vec3 direction=reflect(-view,reflectionNormal);reflectionRadiance=clamp(textureLod(uEnvironment,vec3(direction.x,direction.y,-direction.z),_ReflectionBlur*6.0).rgb*uEnvironmentIntensity,0.0,1.0);float fresnel=pow(max(0.0,1.000293-clamp(dot(reflectionNormal,view),0.0,1.0)),max(.001,_ReflectionFresnel));reflectionMask=clamp(_ReflectionStrength*fresnel-foam-intersection,0.0,1.0)*_ReflectionStrength;base.rgb=mix(base.rgb,reflectionRadiance,reflectionMask*_ReflectionLighting);}' : ''}
 vec3 color=base.rgb*(vec3(.55)+.45*max(dot(normal,uLightDirection),0.0)*uLightColor*shadow);
 ${feature.specular ? 'vec3 halfVector=normalize(uLightDirection+view+normal*_SunReflectionDistortion);float spec=pow(max(0.0,dot(normal,halfVector)),mix(8196.0,64.0,_SunReflectionSize))*_SunReflectionStrength*max(dot(normal,uLightDirection),0.0)*(1.0-intersection)*(1.0-foam);color+=uLightColor*spec*shadow;' : ''}
 ${feature.translucency ? 'float mask=clamp(shadow*density*base.a-(1.0-foam)+edge,0.0,1.0);float scatter=pow(max(0.0,dot(-view,normalize(uLightDirection+normal*_TranslucencyCurvatureMask))),max(.001,_TranslucencyExp));color+=_ShallowColor.rgb*length(uLightColor)*_TranslucencyStrength*scatter*mask;' : ''}
 ${feature.environmentReflection ? 'color=mix(color,reflectionRadiance,reflectionMask*(1.0-_ReflectionLighting));' : ''}
 gl_FragColor=vec4(clamp(color,0.0,1.0),clamp(alpha,0.0,1.0));}`;
  const material = new THREE.ShaderMaterial({ uniforms, vertexShader: vertex, fragmentShader: fragment, transparent: true,
    depthWrite: number('_ZWrite', 1) !== 0, side: number('_Cull', 2) === 0 ? THREE.DoubleSide : THREE.FrontSide, toneMapped: false });
  material.defaultAttributeValues.color = [1, 1, 1, 1];
  const names = { normals:'法線',distanceNormals:'距離法線',waves:'波動',sharpIntersection:'銳利交界',smoothIntersection:'柔和交界',translucency:'透光',foam:'表面泡沫',caustics:'焦散',refraction:'折射',environmentReflection:'環境反射',receiveShadows:'接收陰影' };
  const enabled = Object.entries(names).filter(([key]) => feature[key]).map(([, label]) => label).join('、');
  const disabled = Object.entries(names).filter(([key]) => !feature[key]).map(([, label]) => label).join('、');
  material.name = snapshot.path; material.userData = { materialPath: snapshot.path, previewMode: 'approximation', water: true, waterNeedsDepth: feature.depth, waterFeatures: feature,
    adapterStatus: `StylizedWater2 原生設定適配 · 啟用：${enabled}；關閉：${disabled}。法線強度 ${feature.normalStrength}。場景深度色／交界／波動／地平線／透光近似。${assets.environmentPreview?.available?'原場景 LightingData／EXR 環境反射近似。':'環境反射來源尚未解析。'}原方向光投影依 Renderer／Layer 旗標，材質球沒有場景深度／陰影；URP GI、CSM 與 Unity 畫面待比對。` };
  assets.warn(snapshot.path + '：' + material.userData.adapterStatus); return material;
}

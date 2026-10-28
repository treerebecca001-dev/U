import * as THREE from '../vendor/three.module.js';
import { EXRLoader } from '../vendor/loaders/EXRLoader.js';

export function validateReflectionExr(buffer) {
  if(buffer.byteLength<24)throw new Error('EXR 檔頭不完整。');
  const view=new DataView(buffer),bytes=new Uint8Array(buffer);
  if(view.getUint32(0,true)!==20000630||view.getUint32(4,true)!==2)throw new Error('僅支援本輪已驗證的單一平面 EXR v2。');
  let offset=8,size;
  const text=()=>{const start=offset;while(offset<bytes.length&&bytes[offset]!==0&&offset-start<128)offset++;if(offset>=bytes.length||bytes[offset]!==0)throw new Error('EXR 標頭欄位無效。');return new TextDecoder().decode(bytes.subarray(start,offset++));};
  while(offset<bytes.length&&bytes[offset]!==0){
    if(offset>65536)throw new Error('EXR 標頭超出限制。');
    const name=text(),kind=text();if(offset+4>bytes.length)throw new Error('EXR 標頭截斷。');
    const length=view.getUint32(offset,true);offset+=4;if(length>65536||offset+length>bytes.length)throw new Error('EXR 欄位長度無效。');
    if(name==='dataWindow'&&kind==='box2i'&&length===16)size=[view.getInt32(offset+8,true)-view.getInt32(offset,true)+1,view.getInt32(offset+12,true)-view.getInt32(offset+4,true)+1];
    offset+=length;
  }
  if(!size||size[1]<1||size[1]>1024||size[0]!==size[1]*6)throw new Error('EXR 解析度或六面橫排佈局超出預覽限制。');
  return size;
}

// Unity's documented six-frame row order is +X,-X,+Y,-Y,+Z,-Z.
export function reflectionCubeFromStrip(decoded) {
  const {width,height,data}=decoded;
  if (width !== height * 6 || height < 1 || height > 1024 || !(data instanceof Float32Array) || data.length !== width * height * 4)
    throw new Error('僅支援已驗證的六面橫排 RGBA Float EXR，未猜測其他 Cubemap 佈局。');
  if (data.some(value => !Number.isFinite(value))) throw new Error('反射 EXR 含無效數值。');
  const faces=[];
  for(let face=0;face<6;face++){
    const pixels=new Float32Array(height*height*4);
    for(let row=0;row<height;row++)pixels.set(data.subarray((row*width+face*height)*4,(row*width+(face+1)*height)*4),row*height*4);
    const packed=new Uint16Array(pixels.length);for(let i=0;i<pixels.length;i++)packed[i]=THREE.DataUtils.toHalfFloat(pixels[i]);
    const image=new THREE.DataTexture(packed,height,height,THREE.RGBAFormat,THREE.HalfFloatType);image.flipY=false;faces.push(image);
  }
  const cube=new THREE.CubeTexture(faces);cube.type=THREE.HalfFloatType;cube.format=THREE.RGBAFormat;cube.colorSpace=THREE.NoColorSpace;
  cube.minFilter=THREE.LinearMipmapLinearFilter;cube.magFilter=THREE.LinearFilter;cube.generateMipmaps=true;cube.needsUpdate=true;
  return {cube,faces};
}

export async function loadEnvironmentPreview(environment,assets) {
  const fail=reason=>{assets.warn('環境反射：'+reason);return {available:false,reason};};
  if(!environment)return fail('場景缺少原生 RenderSettings，未指定其他天空貼圖。');
  const baked=environment.bakedReflection;
  if(environment.reflectionMode!==0)return fail('自訂／其他反射模式尚未驗證。');
  if(!baked||baked.probeCount!==0||baked.cubemaps.length!==1)return fail('沒有可唯一對應的場景預設烘焙 Cubemap；內建 Skybox 僅有參照，未猜用外部貼圖。');
  const reference=baked.cubemaps[0];
  if(!reference.path?.toLowerCase().endsWith('.exr')||reference.fileId!=='8900000'||!reference.sha256)return fail('LightingData Cubemap 精確來源／fileID 未解析。');
  try{
    const meta=await (await assets.fetch('/api/texture?path='+encodeURIComponent(reference.path))).json();
    if(meta.textureShape!==2||meta.sha256!==reference.sha256)return fail('反射來源不是同一份原生 Cubemap 匯入資料。');
    const response=await assets.fetch('/api/source?path='+encodeURIComponent(reference.path)),buffer=await response.arrayBuffer();
    const hash=[...new Uint8Array(await crypto.subtle.digest('SHA-256',buffer))].map(v=>v.toString(16).padStart(2,'0')).join('');
    if(hash!==reference.sha256)return fail('讀取期間反射來源已改動；請重新載入。');
    validateReflectionExr(buffer);
    const decoded=new EXRLoader().setDataType(THREE.FloatType).parse(buffer);
    if(assets.controller.signal.aborted)throw new DOMException('預覽已取消','AbortError');
    const {cube,faces}=reflectionCubeFromStrip(decoded);assets.environmentResources??=new Set();
    [cube,...faces].forEach(texture=>assets.environmentResources.add(texture));cube.name=reference.path;
    return {available:true,cube,reference,intensity:environment.reflectionIntensity,lightingPath:baked.path,
      status:'原場景 LightingData 唯一預設反射／原始 EXR；六面佈局、Gamma 與 URP 濾波仍待 Unity 畫面比對。'};
  }catch(error){if(error.name==='AbortError')throw error;return fail(error.message);}
}

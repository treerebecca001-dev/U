import * as THREE from '../vendor/three.module.js';

export function nativeLightApplies(renderer,layer,light){
  return Boolean(renderer&&light&&(light.cullingMask&(1<<layer))!==0&&(light.renderingLayerMask&renderer.renderingLayerMask)!==0);
}
export function nativeShadowApplies(renderer,layer,light) {
  return nativeLightApplies(renderer,layer,light)&&light.shadows?.type>0&&!light.shadows.cullingMatrixOverride;
}

// A fixed directional shadow map for the native water receivers. No Unity bake or CSM claim.
export class WaterShadowPass {
  constructor(renderer){this.renderer=renderer;this.dirty=true;this.depthMaterials=new Map();}
  depthMaterial(source,twoSided){
    const key=source.uuid+':'+twoSided;
    if(!this.depthMaterials.has(key)){
      const cutout=source.userData.shadowCutout;let map;
      if(cutout?.texture){map=cutout.texture.clone();map.repeat.fromArray(cutout.scale);map.offset.fromArray(cutout.offset);map.needsUpdate=true;}
      const material=new THREE.MeshDepthMaterial({depthPacking:THREE.BasicDepthPacking,map:map??null,alphaTest:cutout?.cutoff??0,side:twoSided?THREE.DoubleSide:source.side});
      this.depthMaterials.set(key,material);
    }
    return this.depthMaterials.get(key);
  }
  render(scene,root,light,helpers=[]){
    const meshes=[],waterMaterials=new Set();
    root.traverse(object=>{if(!object.isMesh)return;meshes.push(object);(Array.isArray(object.material)?object.material:[object.material]).filter(m=>m.userData.water).forEach(m=>waterMaterials.add(m));});
    for(const material of waterMaterials){material.uniforms.uShadowAvailable.value=0;if(light){material.uniforms.uLightDirection.value.copy(light.direction);material.uniforms.uLightColor.value.copy(light.color);}else{material.uniforms.uLightDirection.value.set(0,0,0);material.uniforms.uLightColor.value.set(0,0,0);}}
    if(!waterMaterials.size||!light?.shadows?.type||light.shadows.cullingMatrixOverride){this.casterCount=0;return;}
    const signature=JSON.stringify([light.direction.toArray(),light.shadows,light.cullingMask,light.renderingLayerMask]);
    if(signature!==this.signature){this.signature=signature;this.dirty=true;}
    if(this.dirty){
      const eligible=mesh=>{
        const native=mesh.userData.nativeRenderer,materials=Array.isArray(mesh.material)?mesh.material:[mesh.material];
        if(!native||native.castShadows===0||materials.some(m=>m.userData.water||m.userData.previewMode==='unresolved'||m.transparent&&!m.userData.shadowCutout))return false;
        if(!nativeShadowApplies(native,mesh.parent?.userData.nativeLayer??0,light))return false;
        for(let parent=mesh;parent;parent=parent.parent)if(!parent.visible)return false;
        return !helpers.some(helper=>{for(let parent=mesh;parent;parent=parent.parent)if(parent===helper)return true;return false;});
      };
      const casters=meshes.filter(eligible),box=new THREE.Box3();root.updateMatrixWorld(true);
      for(const mesh of casters){mesh.geometry.computeBoundingBox();box.union(mesh.geometry.boundingBox.clone().applyMatrix4(mesh.matrixWorld));}
      if(box.isEmpty()){this.casterCount=0;return;}
      const center=box.getCenter(new THREE.Vector3()),span=Math.max(box.getSize(new THREE.Vector3()).length(),4),distance=span*1.5;
      const camera=new THREE.OrthographicCamera(-span/2,span/2,span/2,-span/2,Math.max(.01,light.shadows.nearPlane),distance+span);
      camera.position.copy(center).addScaledVector(light.direction,distance);if(Math.abs(light.direction.y)>.99)camera.up.set(0,0,1);camera.lookAt(center);camera.updateMatrixWorld();
      const requested=light.shadows.customResolution>0?light.shadows.customResolution:light.shadows.resolution>=0?512*2**light.shadows.resolution:2048;
      const resolution=Math.max(256,Math.min(4096,requested));
      if(!this.target||this.target.width!==resolution){this.target?.dispose();this.target=new THREE.WebGLRenderTarget(resolution,resolution);this.target.depthTexture=new THREE.DepthTexture(resolution,resolution,THREE.UnsignedIntType);}
      this.target.depthTexture.minFilter=THREE.NearestFilter;this.target.depthTexture.magFilter=THREE.NearestFilter;
      const original=meshes.map(mesh=>({mesh,visible:mesh.visible,material:mesh.material})),helperVisibility=helpers.filter(Boolean).map(object=>({object,visible:object.visible})),previous=this.renderer.getRenderTarget();
      try{
        const selected=new Set(casters);
        for(const mesh of meshes){mesh.visible=selected.has(mesh);if(mesh.visible){const native=mesh.userData.nativeRenderer;mesh.material=Array.isArray(mesh.material)?mesh.material.map(m=>this.depthMaterial(m,native.castShadows===2)):this.depthMaterial(mesh.material,native.castShadows===2);}}
        helperVisibility.forEach(({object})=>{object.visible=false;});this.renderer.setRenderTarget(this.target);this.renderer.render(scene,camera);
      }finally{this.renderer.setRenderTarget(previous);original.forEach(({mesh,visible,material})=>{mesh.visible=visible;mesh.material=material;});helperVisibility.forEach(({object,visible})=>{object.visible=visible;});}
      const bias=new THREE.Matrix4().set(.5,0,0,.5,0,.5,0,.5,0,0,.5,.5,0,0,0,1);
      this.matrix=bias.multiply(camera.projectionMatrix).multiply(camera.matrixWorldInverse);this.bias=light.shadows.bias/(camera.far-camera.near);this.normalBias=light.shadows.normalBias*span/resolution;
      this.strength=light.shadows.strength;this.resolution=resolution;this.casterCount=casters.length;this.dirty=false;
    }
    if(this.dirty||!this.target)return;
    for(const material of waterMaterials){const u=material.uniforms;u.uShadowAvailable.value=1;u.uShadowMap.value=this.target.depthTexture;u.uShadowMatrix.value.copy(this.matrix);u.uShadowBias.value=this.bias;u.uShadowNormalBias.value=this.normalBias;u.uShadowStrength.value=this.strength;}
  }
  dispose(){this.target?.dispose();this.target=null;for(const material of this.depthMaterials.values()){material.map?.dispose();material.dispose();}this.depthMaterials.clear();this.dirty=true;}
}

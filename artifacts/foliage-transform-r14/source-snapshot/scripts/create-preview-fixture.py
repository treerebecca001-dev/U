"""Build native Unity QA inputs from the read-only Client working sample, without Unity."""
import hashlib,json,pathlib,re,shutil
root=pathlib.Path(__file__).resolve().parents[1]; sample=root/'samples/Client231229'
source=root/'artifacts/preview-r7/fixture-source'
index=json.loads((root/'artifacts/reports/template-baseline-r5/asset-index.json').read_text(encoding='utf-8-sig'))
by_guid={e['guid']:e['path'] for e in index}; by_path={e['path']:e['guid'] for e in index}
shaders=json.loads((root/'artifacts/preview-r7/inventory.json').read_text(encoding='utf-8'))['shaders']
groups=json.loads((root/'artifacts/preview-r7/ase-materials.json').read_text(encoding='utf-8'))
copied={}; materials=[]
def copy(path):
    if path in copied:return
    assert path.startswith('Assets/')
    target=source/path; assert target.resolve().is_relative_to(source.resolve())
    target.parent.mkdir(parents=True,exist_ok=True)
    shutil.copyfile(sample/path,target);copied[path]=hashlib.sha256(target.read_bytes()).hexdigest()
    if (sample/(path+'.meta')).exists():shutil.copyfile(sample/(path+'.meta'),source/(path+'.meta'))
def material_dependencies(path):
    copy(path)
    text=(sample/path).read_text(encoding='utf-8-sig')
    for guid in set(re.findall(r'guid: ([0-9a-f]{32})',text)):
        target=by_guid.get(guid)
        if target and target.startswith('Assets/') and pathlib.Path(target).suffix.lower() in ('.shader','.png','.tga','.bmp','.jpg','.jpeg','.webp','.asset'):
            copy(target)
for shader,group in zip(shaders,groups):
    copy(shader['path'])
    if group['materials']:
        path='Assets/Tourist/TouristAssets/woman/material.mat' if shader['name']=='ASE_Role' else group['materials'][0]
        material_dependencies(path); materials.append({'shader':shader['name'],'path':path,'actualClientMaterial':True})
    else:
        path='Assets/QA/'+shader['name'].replace('/','_')+'.mat';target=source/path;target.parent.mkdir(parents=True,exist_ok=True)
        properties={'m_TexEnvs':[],'m_Floats':[],'m_Colors':[]}
        for line in shader['properties'].splitlines():
            match=re.match(r'^\s*(?:\[[^\]]*\]\s*)*([A-Za-z_][A-Za-z_0-9]*)\s*\(\s*"[^"]*"\s*,\s*(Range\s*\([^)]*\)|2DArray|2D|Cube|Color|Vector|Float|Int)\s*\)\s*=\s*(.*)$',line)
            if not match:continue
            name,kind,value=match.groups()
            if kind in ('2D','2DArray','Cube'):properties['m_TexEnvs'].append(f'    - {name}:\n        m_Texture: {{fileID: 0}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}')
            elif kind in ('Color','Vector'):
                numbers=value.strip().strip('()').split(',');numbers=(numbers+['0']*4)[:4]
                properties['m_Colors'].append('    - '+name+': {'+', '.join(f'{axis}: {number.strip()}' for axis,number in zip('rgba',numbers))+'}')
            else:properties['m_Floats'].append(f'    - {name}: {value.split()[0]}')
        text='%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!21 &2100000\nMaterial:\n  serializedVersion: 8\n  m_Name: QA_'+shader['name'].replace('/','_')+'\n  m_Shader: {fileID: 4800000, guid: '+shader['guid']+', type: 3}\n  m_PrefabInstance: {fileID: 0}\n  m_ValidKeywords: []\n  m_SavedProperties:\n    serializedVersion: 3\n'
        text+='\n'.join('    '+kind+(':\n'+'\n'.join(rows) if rows else ': []') for kind,rows in properties.items())+'\n    m_Ints: []\n'
        target.write_text(text,encoding='utf-8');(source/(path+'.meta')).write_text('guid: '+hashlib.md5(path.encode()).hexdigest()+'\nNativeFormatImporter:\n  mainObjectFileID: 2100000\n',encoding='utf-8')
        materials.append({'shader':shader['name'],'path':path,'actualClientMaterial':False})
model='Assets/Tourist/TouristAssets/woman/model.asset';copy(model)
fbx='Assets/02.Arts/Scene/Room/Map_10005/Model/10005_Wall_f.fbx';copy(fbx)
tga=next(e['path'] for e in index if e['extension']=='.tga' and e['path'].startswith('Assets/02.Arts/Scene/'));copy(tga)
bmp=next(e['path'] for e in index if e['extension']=='.bmp' and e['path'].startswith('Assets/02.Arts/Scene/'));copy(bmp)
for folder in ('ProjectSettings','Packages'): (source/folder).mkdir(parents=True,exist_ok=True)
(source/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.15f1\n',encoding='utf-8')
for name in ('manifest.json','packages-lock.json'):(source/'Packages'/name).write_text('{"dependencies":{}}\n',encoding='utf-8')
scene='''%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &1
GameObject:
  serializedVersion: 6
  m_Name: Client model material QA
  m_Component:
  - component: {fileID: 4}
  - component: {fileID: 33}
  - component: {fileID: 23}
  m_IsActive: 1
  m_Layer: 0
  m_StaticEditorFlags: 0
--- !u!4 &4
Transform:
  m_GameObject: {fileID: 1}
  m_LocalPosition: {x: 0, y: 0, z: 0}
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_Father: {fileID: 0}
--- !u!33 &33
MeshFilter:
  m_GameObject: {fileID: 1}
  m_Mesh: {fileID: 4300000, guid: MODEL, type: 2}
--- !u!23 &23
MeshRenderer:
  m_GameObject: {fileID: 1}
  m_Enabled: 1
  m_Materials:
  - {fileID: 2100000, guid: MATERIAL, type: 2}
'''.replace('MODEL',by_path[model]).replace('MATERIAL',by_path['Assets/Tourist/TouristAssets/woman/material.mat'])
(source/'Assets/QA.unity').write_text(scene,encoding='utf-8');(source/'Assets/QA.unity.meta').write_text('guid: 20000000000000000000000000000002\n',encoding='utf-8')
synthetic=sum(not m['actualClientMaterial'] for m in materials)
(root/'artifacts/preview-r7/fixture-info.json').write_text(json.dumps({'source':str(source),'scope':f'Native test project, not a full Client copy; original Client assets are copied unchanged. {synthetic} unused Shaders use synthetic native .mat defaults. Unity loading remains unverified.','actualMaterialCount':len(materials)-synthetic,'syntheticMaterialCount':synthetic,'copiedSourceHashes':copied,'materials':materials,'models':[model,fbx],'textures':[tga,bmp]},ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'copiedAssets':len(copied),'materials':len(materials),'source':str(source)},ensure_ascii=False))

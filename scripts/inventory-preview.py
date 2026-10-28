"""Read-only inventory for the Client material/geometry preview milestone."""
import collections, hashlib, json, pathlib, re

root = pathlib.Path(__file__).resolve().parents[1]
project = root / 'samples/Client231229'
entries = json.loads((root/'artifacts/reports/template-baseline-r5/asset-index.json').read_text(encoding='utf-8-sig'))
index = {x['guid']:x['path'] for x in entries}
def read(path): return (project/path).read_text(encoding='utf-8-sig', errors='replace')
def resolve(guid): return index.get(guid, 'UNRESOLVED:'+guid)
shaders=[]
for entry in entries:
    if entry['extension'].lower() != '.shader' or not entry['path'].startswith('Assets/02.Arts/'): continue
    text=read(entry['path'])
    if 'ASEBEGIN' not in text: continue
    props=text.split('SubShader',1)[0]
    shaders.append({'path':entry['path'],'guid':entry['guid'],'name':re.search(r'Shader\s+"([^"]+)"',text).group(1),'properties':props.strip()})
visited=set(); refs=[]
def walk(path,depth=0):
    if path in visited or depth>7: return
    visited.add(path)
    text=read(path)
    direct=[]
    for guid in dict.fromkeys(re.findall(r'guid: ([0-9a-f]{32})',text)):
        target=resolve(guid)
        if target.startswith('UNRESOLVED:'): continue
        ext=pathlib.Path(target).suffix.lower()
        if ext in ('.prefab','.fbx','.mat','.shader','.png','.tga','.jpg','.psd','.asset'):
            direct.append(target)
        if ext in ('.prefab','.mat'): walk(target,depth+1)
    refs.append({'path':path,'references':direct})
walk('Assets/02.Arts/Scene/Room/Room_main.unity')
result={'shaders':shaders,'roomDependencies':refs,'formats':dict(collections.Counter(x['extension'].lower() for x in entries if x['path'].startswith('Assets/02.Arts/Scene/')))}
materials_by_shader={s['guid']:[] for s in shaders}
for entry in entries:
    if entry['extension'].lower()!='.mat' or not entry['path'].startswith('Assets/'): continue
    match=re.search(r'm_Shader:\s*\{fileID:\s*-?\d+,\s*guid:\s*([a-f0-9]{32})',read(entry['path'])[:16000])
    if match and match.group(1) in materials_by_shader: materials_by_shader[match.group(1)].append(entry['path'])
out=root/'artifacts/preview-r7';out.mkdir(parents=True,exist_ok=True)
(out/'inventory.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
(out/'ase-materials.json').write_text(json.dumps([{'shader':s['name'],'guid':s['guid'],'materials':materials_by_shader[s['guid']]} for s in shaders],ensure_ascii=False,indent=2),encoding='utf-8')
pins=[{'guid':s['guid'],'name':s['name'],'path':s['path'],'sha256':hashlib.sha256((project/s['path']).read_bytes()).hexdigest()} for s in shaders]
(root/'src/SceneWorkbench.Web/wwwroot/ui/shader-adapters.json').write_text(json.dumps(pins,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'shaders':[{k:s[k] for k in ('path','name','guid')} for s in shaders],'roomDependencies':refs,'formats':result['formats']},ensure_ascii=False,indent=2))

"""Read-only comparison of the prepared Editor/package baseline and exact preview inputs."""
import hashlib,json,pathlib

root=pathlib.Path(__file__).resolve().parents[1]
prepared=root/'Workspace/Baselines/Client231229-2022.3.13f1'
out=root/'artifacts/version-r8'
report=json.loads((prepared/'.scene-workbench/baseline-preparation.json').read_text(encoding='utf-8-sig'))
source=pathlib.Path(report['sourceRoot'])
original=pathlib.Path('D:/Work_ART/漂流NFT/@WLN_work/Client231229')
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def save(name,value):(out/name).write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
manifest=json.loads((prepared/'Packages/manifest.json').read_text(encoding='utf-8-sig'))['dependencies']
locked=json.loads((prepared/'Packages/packages-lock.json').read_text(encoding='utf-8-sig'))['dependencies']
rows=[]
for package in report['packages']:
    name=package['name']; entry=locked.get(name,{})
    declaration=manifest.get(name)
    if declaration and not declaration[0].isdigit():declaration='non-registry declaration retained; see pinned input hash'
    rows.append({**package,'direct':name in manifest,'declared':declaration,'lockedVersion':entry.get('version') if entry.get('source') in ('registry','builtin') else 'resolved-from-content',
                 'depth':entry.get('depth'),'lockedDependencies':entry.get('dependencies',{}),
                 'metadataSha256':digest(prepared/package['metadataPath']) if package.get('metadataPath') else None})
inputs=[]
for kind,folder,pins in [('source',source,report['sourceInputs']),('prepared',prepared,report['preparedInputs'])]:
    for pin in pins:inputs.append({'kind':kind,'path':pin['path'],'expected':pin['sha256'],'actual':digest(folder/pin['path']),'matches':pin['sha256']==digest(folder/pin['path'])})
save('packages-matrix.json',{'sourceEditor':report['sourceEditor'],'targetEditor':report['targetEditor'],'issues':report['issues'],'packages':rows,'edges':report['dependencies'],'inputPins':inputs,'allInputPinsMatch':all(p['matches'] for p in inputs),'nativeStatus':'UNVERIFIED'})
old=json.loads((root/'artifacts/preview-r7/handoff-fix1/native-preservation.json').read_text(encoding='utf-8'))
native=[]
for entry in old['files']:
    relative=entry['path']; values={'original':digest(original/relative),'source':digest(source/relative),'prepared':digest(prepared/relative)}
    native.append({'path':relative,**values,'matches':len(set(values.values()))==1})
watched=['ProjectSettings/ProjectVersion.txt','Packages/manifest.json','Packages/packages-lock.json']+[p['metadataPath'] for p in report['packages'] if any(i['package']==p['name'] for i in report['issues'])]
protection=[{'path':p,'original':digest(original/p),'source':digest(source/p),'sourceStillMatchesOriginal':digest(original/p)==digest(source/p),'prepared':digest(prepared/p),'expectedPreparedChange':p=='ProjectSettings/ProjectVersion.txt'} for p in watched]
save('native-preservation.json',{'scope':'Exact 96 preview resources/meta plus version/dependency input watches; full-copy content verification is a separate CLI record.', 'files':native,'sourceWatches':protection,'allNativeMatch':all(p['matches'] for p in native),'allOriginalWatchesMatch':all(p['sourceStillMatchesOriginal'] for p in protection)})
assert all(p['matches'] for p in inputs)
assert all(p['matches'] for p in native)
assert all(p['sourceStillMatchesOriginal'] for p in protection)
print(json.dumps({'packages':len(rows),'edges':len(report['dependencies']),'inputPins':len(inputs),'nativeInputs':len(native),'watches':len(protection),'allMatch':True,'nativeStatus':'UNVERIFIED'}))

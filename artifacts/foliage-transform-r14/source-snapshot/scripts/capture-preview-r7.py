"""Freeze source identity and verify native inputs without creating a release package."""
import hashlib,json,pathlib,datetime,sys
root=pathlib.Path(__file__).resolve().parents[1]; label=sys.argv[1] if len(sys.argv)>1 else 'handoff'
assert label in ('handoff','handoff-v2','handoff-fix1')
out=root/'artifacts/preview-r7'/label;out.mkdir(parents=True,exist_ok=True)
assert not (out/'source-manifest.json').exists(), 'Do not overwrite frozen identity'
def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def save(path,data):path.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
files=[]
for folder in ('src/SceneWorkbench.Core','src/SceneWorkbench.Unity','src/SceneWorkbench.Web','src/SceneWorkbench.Cli','tests/SceneWorkbench.Tests','scripts'):
    for path in (root/folder).rglob('*'):
        if path.is_file() and not any(part in ('bin','obj','__pycache__') for part in path.relative_to(root/folder).parts):files.append(path)
files += [root/name for name in ('README.md','DESIGN.md','UX-CONTRACT.md','premium-ui.json','THIRD-PARTY-NOTICES.md','Directory.Build.props','global.json','NuGet.Config','SceneWorkbench.slnx','src/SceneWorkbench.Desktop/EditorTheme.cs','docs/APPROVED_PLAN.zh-TW.md')]
records=[{'path':p.relative_to(root).as_posix(),'bytes':p.stat().st_size,'sha256':digest(p)} for p in sorted(set(files))]
save(out/'source-manifest.json',{'identity':'preview-r7-fix1' if label=='handoff-fix1' else 'preview-r7-source2' if label=='handoff-v2' else 'preview-r7','capturedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'files':records})
runtime=[root/'src/SceneWorkbench.Web/bin/Release/net10.0'/name for name in ('SceneWorkbench.Web.dll','SceneWorkbench.Unity.dll','SceneWorkbench.Core.dll')]
save(out/'runtime.json',{'binaries':[{'path':p.relative_to(root).as_posix(),'sha256':digest(p)} for p in runtime]})
fixture=json.loads((root/'artifacts/preview-r7/fixture-info.json').read_text(encoding='utf-8')); original=pathlib.Path('D:/Work_ART/漂流NFT/@WLN_work/Client231229'); sample=root/'samples/Client231229'; fixture_source=pathlib.Path(fixture['source'])
native=[]
for relative in fixture['copiedSourceHashes']:
    for path in (relative,relative+'.meta'):
        if not (fixture_source/path).exists():continue
        record={'path':path,'original':digest(original/path),'working':digest(sample/path),'fixture':digest(fixture_source/path)}
        record['matches']=len(set((record['original'],record['working'],record['fixture'])))==1;native.append(record)
for path in ('Assets/02.Arts/Scene/Room/Room_main.unity','Assets/02.Arts/Scene/Room/Room_main.unity.meta'):
    native.append({'path':path,'original':digest(original/path),'working':digest(sample/path),'matches':digest(original/path)==digest(sample/path)})
save(out/'native-preservation.json',{'scope':'Read-only hash comparison of the exact assets/metadata used by preview and material tests, not a new whole-project audit.','files':native,'allMatch':all(r['matches'] for r in native)})
assert all(r['matches'] for r in native)
print(json.dumps({'sourceFiles':len(records),'sourceManifestSha256':digest(out/'source-manifest.json'),'nativeCompared':len(native),'nativeAllMatch':True,'runtime':json.loads((out/'runtime.json').read_text(encoding='utf-8'))},ensure_ascii=False))

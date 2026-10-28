"""Pin the reviewed r180 loader source, applying only documented local adapters."""
import hashlib,json,pathlib
root=pathlib.Path(__file__).resolve().parents[1]/'src/SceneWorkbench.Web'
manifest=json.loads((root/'vendor-manifest.json').read_text(encoding='utf-8-sig'))
originals={'loaders/FBXLoader.js':'d97724a6bea228003e02c64651d969a9a62003a00be314ef6995c390116f547d','loaders/TGALoader.js':'7560c445146ed4432a340e312672155d86cf54ad3fc7f5571da12cba9512b5c8','curves/NURBSCurve.js':'bef2607618a7778455e71a1f0bd206951c382d313e2e245808cc7d3533d60fb6','curves/NURBSUtils.js':'c6bd7c4137d585098923f189687898ea3e8762ea3ecbe255d749a56353894379','libs/fflate.module.js':'209a4412eb48ce609edb4391992a792ffcc3983d30ee7e2b0b89a8c470f3cd8a'}
for name,digest in originals.items():
    path=root/'wwwroot/vendor'/name; data=path.read_bytes()
    if any(x['path']==name for x in manifest['files']): continue
    assert hashlib.sha256(data).hexdigest()==digest
    text=data.decode('utf-8').replace("from 'three'","from '../three.module.js'")
    if name=='loaders/FBXLoader.js':
        needle="if ( 'GlobalSettings' in fbxTree ) {"
        assert text.count(needle)==1
        text=text.replace(needle,needle+"\n            sceneGraph.userData.sourceAxes = Object.fromEntries( ['UpAxis','UpAxisSign','FrontAxis','FrontAxisSign','CoordAxis','CoordAxisSign'].map( key => [key, fbxTree.GlobalSettings[key]?.value] ) );")
    path.write_bytes(text.encode('utf-8'))
    manifest['files'].append({'path':name,'originalSha256':digest,'servedSha256':hashlib.sha256(path.read_bytes()).hexdigest()})
manifest['adaptation']="Controls/loaders/curves import 'three' replaced by local relative module. FBXLoader additionally copies six source axis metadata values to sceneGraph.userData.sourceAxes; geometry/parser algorithms unchanged. Unity material textures are resolved separately; embedded FBX textures are never fetched from external URLs."
(root/'vendor-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')

"""Freeze existing R12 source, built runtime and original evidence; no build/package or native edits."""
import datetime
import hashlib
import json
import pathlib
import shutil
import xml.etree.ElementTree as ET

root = pathlib.Path(__file__).resolve().parents[1]
base = root / 'artifacts/prefab-cancel-r12'
out, snapshot, runtime = base / 'handoff', base / 'source-snapshot', base / 'runtime'
assert not any(p.exists() for p in (out, snapshot, runtime)), 'Never overwrite a handoff'

def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()

def read(p):
    return json.loads(p.read_text(encoding='utf-8'))

def save(p, value):
    with p.open('x', encoding='utf-8', newline='\n') as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2)
        stream.write('\n')

def entry(p):
    return {'path': p.relative_to(root).as_posix(), 'bytes': p.stat().st_size, 'sha256': sha(p)}

prior_path = root / 'artifacts/terrain-recovery-r11/handoff/source-manifest.json'
assert sha(prior_path) == '16ffc2bd00915baf5f101327599d1f22c243aaa327c9457f52b392f36465021e'
prior = read(prior_path)
allowed = {'README.md', 'UX-CONTRACT.md', 'src/SceneWorkbench.Unity/PreviewCatalog.cs',
           'src/SceneWorkbench.Web/wwwroot/ui/transform-tools.js',
           'src/SceneWorkbench.Web/wwwroot/ui/scene-view.js', 'src/SceneWorkbench.Web/wwwroot/ui/app.js'}
changed = []
for f in prior['files']:
    p = root / f['path']
    if sha(p) != f['sha256']:
        assert f['path'] in allowed, f['path']
        changed.append({'path': f['path'], 'before': f['sha256'], 'after': sha(p)})
assert len(changed) == len(allowed)
plan = root / 'docs/APPROVED_PLAN.zh-TW.md'
assert sha(plan) == 'c1446c59bb4c61af3b2c215e36f8d7eb1816af02fcbd3aa23012249c2a2b9b96'
for f in read(base / 'native-before.json')['files']:
    assert sha(pathlib.Path(f['absolute'])) == f['sha256'], f['absolute']
assert read(base / 'prefab-verification.json')['nativeFilesUnchanged'] == 41
assert read(base / 'native-cancel-verification.json')['status'] == 'VERIFIED_SCOPED_NATIVE_ESCAPE'
unit = read(base / 'cancel-unit-camera-fix.json')
assert len(unit['results']) == 8 and all(r['status'] == 'PASS' for r in unit['results'])
assert unit['sourceSha256'] == sha(root / 'src/SceneWorkbench.Web/wwwroot/ui/transform-tools.js')
assert read(base / 'browser-errors.json') == []
console = read(base / 'client-console-persistent.json')
assert console['tab'] == 'Console 2' and console['dirty'] == '✓ 已儲存'
assert '5094106267881264939' in console['rows'][0] and '5565035281593725506' in console['rows'][1]
trx = ET.parse(base / 'tests/backend.trx').getroot()
counters = next(e for e in trx.iter() if e.tag.endswith('Counters')).attrib
assert counters['total'] == counters['executed'] == counters['passed'] == '187' and counters['failed'] == '0'
post = read(base / 'client-post-console.json')
assert not post['state']['dirty'] and post['state']['undoCount'] == post['state']['redoCount'] == 0
assert post['nativeFilesUnchanged'] == 41

# Bounded historical source reads. No recursive project copy or D: mutation.
body = 'Assets/Tourist/TouristAssets/woman/body.prefab'
tree = 'Assets/ResourcesBuild/Scenes/Common/Trees/TreeA01/TreeA01.prefab'
provenance = []
for project in ('Client230411', 'Client', 'Client231229'):
    source = pathlib.Path('D:/Work_ART/漂流NFT/@WLN_work') / project
    for relative in (body, body + '.meta', tree, tree + '.meta'):
        p = source / relative
        assert p.is_file(), str(p)
        provenance.append({'project': project, 'path': relative, 'absolute': str(p), 'bytes': p.stat().st_size, 'sha256': sha(p)})
body_hashes = {f['sha256'] for f in provenance if f['path'] == body}
assert body_hashes == {'636bd4e0e975c5a25321209134f1840fed88b9601ae2060efa53f623a53966ad'}
save(base / 'source-provenance.json', {'files': provenance, 'readOnly': True, 'restoredHistoricalTree': False})

out.mkdir(); snapshot.mkdir(); runtime.mkdir()
files = {root / f['path'] for f in prior['files']}
files.update(root / p for p in ('src/SceneWorkbench.Unity/PrefabOverrideTargets.cs',
    'tests/SceneWorkbench.Tests/PrefabOverridePreviewTests.cs', 'scripts/prepare-prefab-cancel-r12.mjs',
    'scripts/verify-prefab-cancel-r12.mjs', 'scripts/test-transform-cancel-r12.mjs',
    'scripts/verify-native-cancel-r12.mjs', 'scripts/seal-prefab-cancel-r12.py',
    'docs/IMPLEMENTATION.md', 'docs/HANDOFF_PREFAB_CANCEL_R12.zh-TW.md'))
source = []
for p in sorted(files):
    relative = p.relative_to(root).as_posix(); q = snapshot / relative
    q.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(p, q)
    assert sha(p) == sha(q)
    source.append({'path': relative, 'bytes': p.stat().st_size, 'sha256': sha(p)})
save(out / 'source-manifest.json', {'identity': 'prefab-cancel-r12', 'priorSourceManifestSha256': sha(prior_path),
    'planSha256': sha(plan), 'changesFromR11': changed, 'files': source})

binaries = []
web_bin = root / 'src/SceneWorkbench.Web/bin/Release/net10.0'
for p in sorted(web_bin.rglob('*')):
    if not p.is_file(): continue
    q = runtime / p.relative_to(web_bin); q.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(p, q)
    assert sha(p) == sha(q); binaries.append({**entry(q), 'builtSource': p.relative_to(root).as_posix()})
for name in ('SceneWorkbench.Core.dll', 'SceneWorkbench.Unity.dll', 'SceneWorkbench.Web.dll'):
    assert sha(root / 'tests/SceneWorkbench.Tests/bin/Release/net10.0' / name) == sha(web_bin / name)
for subdir, name in (('src/SceneWorkbench.Cli', 'SceneWorkbench.dll'), ('src/SceneWorkbench.Cli', 'SceneWorkbench.exe'),
                    ('src/SceneWorkbench.Cli', 'SceneWorkbench.runtimeconfig.json'),
                    ('tests/SceneWorkbench.Tests', 'SceneWorkbench.Tests.dll')):
    p = root / subdir / 'bin/Release/net10.0' / name; q = runtime / name
    assert not q.exists(); shutil.copyfile(p, q); assert sha(p) == sha(q)
    binaries.append({**entry(q), 'builtSource': p.relative_to(root).as_posix()})
save(out / 'runtime.json', {'identity': 'prefab-cancel-r12', 'backendTests': dict(counters),
    'buildMatchesTestedAssemblies': True, 'binaries': binaries, 'unityStarted': False, 'packaging': False})
save(out / 'native-input-manifest.json', {'identity': 'prefab-cancel-r12',
    'watchedUnchanged': read(base / 'native-before.json')['files'],
    'historicalSourceManifest': entry(base / 'source-provenance.json'),
    'r11RestoredInputsManifest': entry(root / 'artifacts/terrain-recovery-r11/handoff/native-input-manifest.json'),
    'nativeEditsThisRound': 0, 'legacyTreeImported': False})

evidence = [p for p in sorted(base.rglob('*')) if p.is_file()
            and not any(parent in (out, snapshot, runtime) for parent in p.parents)]
save(out / 'evidence-manifest.json', {'identity': 'prefab-cancel-r12',
    'status': 'VERIFIED_SCOPED_PENDING_INDEPENDENT_REVIEW', 'time': datetime.datetime.now(datetime.timezone.utc).isoformat(),
    'sourceManifestSha256': sha(out / 'source-manifest.json'), 'runtimeManifestSha256': sha(out / 'runtime.json'),
    'nativeInputManifestSha256': sha(out / 'native-input-manifest.json'),
    'files': [entry(p) for p in evidence]})
print(json.dumps({'sourceFiles': len(source), 'runtimeFiles': len(binaries), 'evidenceFiles': len(evidence),
    'sourceManifestSha256': sha(out / 'source-manifest.json'), 'runtimeManifestSha256': sha(out / 'runtime.json'),
    'nativeInputManifestSha256': sha(out / 'native-input-manifest.json'),
    'evidenceManifestSha256': sha(out / 'evidence-manifest.json')}, ensure_ascii=False))

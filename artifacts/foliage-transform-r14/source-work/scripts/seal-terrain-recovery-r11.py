"""Freeze source identity, exact recovered native inputs and existing execution evidence."""
import datetime
import hashlib
import json
import pathlib
import shutil

root = pathlib.Path(__file__).resolve().parents[1]
base = root / 'artifacts/terrain-recovery-r11'
out, snapshot, assets = base / 'handoff', base / 'source-snapshot', base / 'source-assets'
assert not out.exists() and not snapshot.exists() and not assets.exists()

def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()

def save(p, value):
    p.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

prior_path = root / 'artifacts/transform-r10/handoff-fix1/source-manifest.json'
prior = json.loads(prior_path.read_text(encoding='utf-8'))
for f in prior['files']:
    assert sha(root / f['path']) == f['sha256'], f['path']
plan = json.loads((base / 'preflight.json').read_text(encoding='utf-8'))
application = json.loads((base / 'application.json').read_text(encoding='utf-8'))
browser = json.loads((base / 'browser.json').read_text(encoding='utf-8'))
assert application['status'] == 'COMMITTED' and application['protectedUnchanged']
assert browser['status'] == 'VERIFIED_SCOPED' and not browser['console']
assert browser['ui']['viewport']['unresolvedTerrainLayers'] == '0' and browser['ui']['dirty'] == '✓ 已儲存'
out.mkdir(); snapshot.mkdir(); assets.mkdir()
files = {root / f['path'] for f in prior['files']}
files.update(root / p for p in ('scripts/recover-terrain-layer-r11.py', 'scripts/seal-terrain-recovery-r11.py', 'docs/HANDOFF_TERRAIN_RECOVERY_R11.zh-TW.md'))
source = []
for p in sorted(files):
    relative = p.relative_to(root).as_posix(); q = snapshot / relative
    q.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(p, q); assert sha(p) == sha(q)
    source.append({'path': relative, 'bytes': p.stat().st_size, 'sha256': sha(p)})
save(out / 'source-manifest.json', {'identity': 'terrain-recovery-r11', 'codeBase': 'transform-r10-fix1', 'codeBaseSourceManifestSha256': sha(prior_path), 'files': source})
native = []
for f in plan['files']:
    p = pathlib.Path(plan['targetWorkCopy']) / f['path']; s = pathlib.Path(plan['sourceProject']) / f['path']
    assert sha(p) == sha(s) == f['sha256']
    q = assets / f['path']; q.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(p, q); assert sha(q) == f['sha256']
    native.append(f)
save(out / 'native-input-manifest.json', {'identity': 'terrain-recovery-r11', 'targetWorkCopy': plan['targetWorkCopy'], 'sourceProject': plan['sourceProject'],
    'terrainSha256': plan['sourceAndTargetTerrainSha256'], 'addedNativeFiles': native, 'sourceFilesExact': True, 'nativeTerrainHeightWeightsUnchanged': True})
runtime_path = root / 'artifacts/transform-r10/handoff-fix1/runtime.json'
runtime = json.loads(runtime_path.read_text(encoding='utf-8'))
for f in runtime['binaries']:
    assert sha(root / f['path']) == f['sha256']
save(out / 'runtime.json', runtime)
evidence = [base / name for name in ('discovery.json', 'preflight.json', 'target-guid-precheck.json', 'application.json', 'api-verification.json', 'browser.json', 'recovered-scene.png', 'graph.json', 'state.json')]
save(out / 'evidence-manifest.json', {'identity': 'terrain-recovery-r11', 'status': 'VERIFIED_SCOPED_PENDING_INDEPENDENT_REVIEW',
    'sourceManifestSha256': sha(out / 'source-manifest.json'), 'nativeInputManifestSha256': sha(out / 'native-input-manifest.json'),
    'files': [{'path': p.relative_to(root).as_posix(), 'bytes': p.stat().st_size, 'sha256': sha(p)} for p in evidence]})
print(json.dumps({'sourceFiles': len(source), 'sourceManifestSha256': sha(out / 'source-manifest.json'), 'nativeFiles': len(native),
    'nativeManifestSha256': sha(out / 'native-input-manifest.json'), 'evidenceFiles': len(evidence), 'evidenceManifestSha256': sha(out / 'evidence-manifest.json')}, ensure_ascii=False))

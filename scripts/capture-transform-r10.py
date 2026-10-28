"""Freeze ordinary source/evidence folders for independent review; no export or package."""
import datetime
import hashlib
import json
import pathlib
import shutil
import sys

root = pathlib.Path(__file__).resolve().parents[1]
base = root / 'artifacts/transform-r10'
label = sys.argv[1] if len(sys.argv) > 1 else ''
assert label in ('', 'fix1')
identity = 'transform-r10' + ('-' + label if label else '')
out, snapshot = base / ('handoff' + ('-' + label if label else '')), base / ('source-snapshot' + ('-' + label if label else ''))
assert not out.exists() and not snapshot.exists(), 'Frozen source/evidence must not be overwritten'
prior = json.loads((root / 'artifacts/preview-r9/handoff-fix2/source-manifest.json').read_text(encoding='utf-8'))
verify = json.loads((base / 'verification.json').read_text(encoding='utf-8'))
assert verify['status'] == 'VERIFIED_SCOPED' and len(verify['checks']) == 8
if label:
    picking = json.loads((base / 'picking-integration-final.json').read_text(encoding='utf-8'))
    browser = json.loads((base / 'picking-browser-fix1.json').read_text(encoding='utf-8'))
    assert picking['status'] == 'PASS' and len(picking['checks']) == 4
    assert browser['status'] == 'VERIFIED_SCOPED' and not browser['console']
    assert browser['first']['selection'] == 'fileID 401' and browser['second']['selection'] == 'fileID 201'

def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()

def write(p, value):
    p.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

files = {root / f['path'] for f in prior['files']}
for folder in ('scripts', 'src/SceneWorkbench.Web', 'DemoProject'):
    for p in (root / folder).rglob('*'):
        if p.is_file() and not any(s in ('bin', 'obj', '__pycache__', '.scene-workbench') for s in p.relative_to(root / folder).parts):
            files.add(p)
files.add(root / 'docs/HANDOFF_TRANSFORM_R10.zh-TW.md')
if label:
    files.add(root / 'docs/HANDOFF_TRANSFORM_R10_FIX1.zh-TW.md')
out.mkdir(); snapshot.mkdir()
records = []
for p in sorted(files):
    relative = p.relative_to(root).as_posix(); target = snapshot / relative
    target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(p, target)
    assert sha(p) == sha(target)
    records.append({'path': relative, 'bytes': p.stat().st_size, 'sha256': sha(p)})
write(out / 'source-manifest.json', {'identity': identity, 'capturedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(), 'files': records})
if label:
    prior = json.loads((base / 'handoff/source-manifest.json').read_text(encoding='utf-8'))
old = {f['path']: f['sha256'] for f in prior['files']}; new = {f['path']: f['sha256'] for f in records}
write(out / 'source-delta.json', {'baseline': 'transform-r10' if label else 'preview-r9-fix2', 'files': [{'path': p, 'before': old.get(p), 'after': new.get(p)} for p in sorted(old.keys() | new.keys()) if old.get(p) != new.get(p)]})
runtime = json.loads((root / 'artifacts/preview-r9/handoff-fix2/runtime.json').read_text(encoding='utf-8'))
for f in runtime['binaries']:
    assert sha(root / f['path']) == f['sha256']
write(out / 'runtime.json', runtime)
evidence = list((base / 'browser').glob('*')) + [base / p for p in ('client-watch-before.json', 'client-watch-after.json', 'verification.json', 'ui-audit-final.json', 'syntax.log')]
evidence += [root / 'artifacts/preview-r9/review-receipt-fix2.json', root / 'artifacts/preview-r9/handoff-fix2/test-summary.json']
if label:
    evidence += [base / name for name in ('picking-integration.json', 'picking-integration-final.json', 'picking-groups-pre-fix.log', 'ui-audit-fix1.json', 'picking-browser-fix1.json', 'picking-fixed.png')]
write(out / 'evidence-manifest.json', {'identity': identity, 'status': 'VERIFIED_SCOPED_PENDING_INDEPENDENT_REVIEW', 'sourceManifestSha256': sha(out / 'source-manifest.json'),
    'files': [{'path': p.relative_to(root).as_posix(), 'bytes': p.stat().st_size, 'sha256': sha(p)} for p in sorted(evidence) if p.is_file()]})
print(json.dumps({'sourceFiles': len(records), 'sourceManifestSha256': sha(out / 'source-manifest.json'), 'evidenceFiles': len(evidence), 'evidenceManifestSha256': sha(out / 'evidence-manifest.json'), 'snapshot': str(snapshot)}, ensure_ascii=False))

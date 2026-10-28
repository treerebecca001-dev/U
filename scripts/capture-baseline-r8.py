"""Freeze ordinary source files and verification identities; no release/package is produced."""
import datetime, hashlib, json, pathlib, shutil, sys

root = pathlib.Path(__file__).resolve().parents[1]
label = sys.argv[1] if len(sys.argv) > 1 else 'handoff'
assert label in ('handoff', 'handoff-fix1', 'handoff-fix2')
out = root / 'artifacts/version-r8' / label
snapshot = root / 'artifacts/version-r8' / ('source-snapshot' + ('' if label == 'handoff' else '-' + label[8:]))
assert not out.exists() and not snapshot.exists(), 'Do not overwrite frozen source/evidence'
out.mkdir(parents=True)
snapshot.mkdir()

def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def save(path, value): path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

files = set()
for folder in ('src/SceneWorkbench.Core', 'src/SceneWorkbench.Unity', 'src/SceneWorkbench.Web', 'src/SceneWorkbench.Cli', 'tests/SceneWorkbench.Tests', 'scripts'):
    for file in (root / folder).rglob('*'):
        if file.is_file() and not any(p in ('bin', 'obj', '__pycache__') for p in file.relative_to(root / folder).parts): files.add(file)
for name in ('README.md', 'DESIGN.md', 'UX-CONTRACT.md', 'premium-ui.json', 'THIRD-PARTY-NOTICES.md', 'Directory.Build.props', 'global.json', 'NuGet.Config', 'SceneWorkbench.slnx', 'src/SceneWorkbench.Desktop/EditorTheme.cs', 'docs/APPROVED_PLAN.zh-TW.md'):
    files.add(root / name)
records = []
for file in sorted(files):
    relative = file.relative_to(root).as_posix()
    target = snapshot / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(file, target)
    assert digest(target) == digest(file)
    records.append({'path': relative, 'bytes': file.stat().st_size, 'sha256': digest(file)})
save(out / 'source-manifest.json', {'identity': 'baseline-r8' if label == 'handoff' else 'baseline-r8-' + label[8:], 'capturedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(), 'files': records})
previous = json.loads((root / 'artifacts/preview-r7/handoff-fix1/source-manifest.json').read_text(encoding='utf-8'))
old = {r['path']: r for r in previous['files']}
new = {r['path']: r for r in records}
delta = [{'path': p, 'before': old.get(p, {}).get('sha256'), 'after': new.get(p, {}).get('sha256')} for p in sorted(old.keys() | new.keys()) if old.get(p, {}).get('sha256') != new.get(p, {}).get('sha256')]
save(out / 'source-delta.json', {'baseline': 'preview-r7-fix1', 'files': delta})
runtime = []
for folder, names in (('src/SceneWorkbench.Web/bin/Release/net10.0', ('SceneWorkbench.Web.dll', 'SceneWorkbench.Unity.dll', 'SceneWorkbench.Core.dll')), ('src/SceneWorkbench.Cli/bin/Release/net10.0', ('SceneWorkbench.dll', 'SceneWorkbench.Unity.dll', 'SceneWorkbench.Core.dll')), ('tests/SceneWorkbench.Tests/bin/Release/net10.0', ('SceneWorkbench.Tests.dll', 'SceneWorkbench.Web.dll', 'SceneWorkbench.Unity.dll'))):
    for name in names:
        file = root / folder / name
        runtime.append({'path': file.relative_to(root).as_posix(), 'sha256': digest(file)})
save(out / 'runtime.json', {'binaries': runtime})
print(json.dumps({'sourceFiles': len(records), 'sourceManifestSha256': digest(out / 'source-manifest.json'), 'changes': len(delta), 'snapshot': str(snapshot)}, ensure_ascii=False))

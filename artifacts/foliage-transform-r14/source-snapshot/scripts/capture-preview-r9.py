"""Freeze reviewable source, existing test results and runtime identities; no package/export."""
import datetime, hashlib, json, pathlib, shutil, sys, xml.etree.ElementTree as ET

root = pathlib.Path(__file__).resolve().parents[1]
label = sys.argv[1] if len(sys.argv) > 1 else 'handoff'
assert label in ('handoff', 'handoff-fix1', 'handoff-fix2')
out = root / 'artifacts/preview-r9' / label
snapshot = root / 'artifacts/preview-r9' / ('source-snapshot' + ('' if label == 'handoff' else '-' + label[8:]))
assert not out.exists() and not snapshot.exists(), 'Frozen review evidence is immutable'
out.mkdir(); snapshot.mkdir()
def digest(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def save(p, value): p.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

files = set()
for folder in ('src/SceneWorkbench.Core', 'src/SceneWorkbench.Unity', 'src/SceneWorkbench.Web', 'src/SceneWorkbench.Cli', 'tests/SceneWorkbench.Tests', 'scripts'):
    for file in (root / folder).rglob('*'):
        if file.is_file() and not any(p in ('bin', 'obj', '__pycache__') for p in file.relative_to(root / folder).parts): files.add(file)
for name in ('README.md', 'DESIGN.md', 'UX-CONTRACT.md', 'premium-ui.json', 'THIRD-PARTY-NOTICES.md', 'Directory.Build.props', 'global.json', 'NuGet.Config', 'SceneWorkbench.slnx', 'src/SceneWorkbench.Desktop/EditorTheme.cs', 'docs/APPROVED_PLAN.zh-TW.md', 'docs/HANDOFF_PREVIEW_R9.zh-TW.md'):
    files.add(root / name)
if label != 'handoff': files.add(root / 'docs/HANDOFF_PREVIEW_R9_FIX1.zh-TW.md')
if label == 'handoff-fix2': files.add(root / 'docs/HANDOFF_PREVIEW_R9_FIX2.zh-TW.md')
records = []
for file in sorted(files):
    relative = file.relative_to(root).as_posix(); target = snapshot / relative
    target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(file, target)
    assert digest(file) == digest(target)
    records.append({'path': relative, 'bytes': file.stat().st_size, 'sha256': digest(file)})
save(out / 'source-manifest.json', {'identity': 'preview-r9' if label == 'handoff' else 'preview-r9-' + label[8:], 'capturedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(), 'files': records})
baseline = 'artifacts/version-r8/handoff-fix2/source-manifest.json' if label == 'handoff' else 'artifacts/preview-r9/handoff' + ('-fix1' if label == 'handoff-fix2' else '') + '/source-manifest.json'
old = {r['path']: r['sha256'] for r in json.loads((root / baseline).read_text(encoding='utf-8'))['files']}
new = {r['path']: r['sha256'] for r in records}
save(out / 'source-delta.json', {'baseline': 'baseline-r8-fix2' if label == 'handoff' else 'preview-r9-fix1' if label == 'handoff-fix2' else 'preview-r9', 'files': [{'path': p, 'before': old.get(p), 'after': new.get(p)} for p in sorted(old.keys() | new.keys()) if old.get(p) != new.get(p)]})
runtime = []
runtime_kind = 'terrain' if label == 'handoff' else 'water'
runtime_inputs = ((f'artifacts/preview-r9/{runtime_kind}-runtime', ('SceneWorkbench.Web.dll', 'SceneWorkbench.Unity.dll', 'SceneWorkbench.Core.dll')), (f'artifacts/preview-r9/{runtime_kind}-test-runtime', ('SceneWorkbench.Tests.dll', 'SceneWorkbench.Web.dll', 'SceneWorkbench.Unity.dll', 'SceneWorkbench.dll')))
if label == 'handoff-fix2': runtime_inputs = (('artifacts/preview-r9/lighting-test-runtime', ('SceneWorkbench.Web.dll', 'SceneWorkbench.Unity.dll', 'SceneWorkbench.Core.dll', 'SceneWorkbench.Tests.dll', 'SceneWorkbench.dll', 'SceneWorkbench.exe', 'SceneWorkbench.runtimeconfig.json', 'SceneWorkbench.Web.runtimeconfig.json')),)
for folder, names in runtime_inputs:
    for name in names:
        p = root / folder / name; runtime.append({'path': p.relative_to(root).as_posix(), 'sha256': digest(p)})
save(out / 'runtime.json', {'binaries': runtime})
ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
results = {}
if label == 'handoff-fix2':
    merged = json.loads((root / 'artifacts/preview-r9/test-summary-lighting.json').read_text(encoding='utf-8'))
    assert merged['uniqueTests'] == 177 and all(r['outcome'] == 'Passed' for r in merged['tests'].values())
    save(out / 'test-summary.json', merged)
else:
    for name in (f'preview-r9-{runtime_kind}.trx',):
        trx = ET.parse(root / 'artifacts/preview-r9/test-results' / name)
        for result in trx.findall('.//t:UnitTestResult', ns): results[result.attrib['testName']] = {'outcome': result.attrib['outcome'], 'evidence': name}
    assert len(results) == (173 if label == 'handoff' else 174) and all(r['outcome'] == 'Passed' for r in results.values())
    save(out / 'test-summary.json', {'status': 'PASS', 'uniqueTests': len(results), 'note': f'Complete post-{runtime_kind} run, alternate output with SCENE_WORKBENCH_CLI pointing to the same candidate executable; zero failures/skips.', 'tests': results})
print(json.dumps({'sourceFiles': len(records), 'manifestSha256': digest(out / 'source-manifest.json'), 'snapshot': str(snapshot)}, ensure_ascii=False))

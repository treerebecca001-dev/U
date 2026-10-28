"""Restore five exact native files into the existing work copy, never the D: sources."""
import datetime
import hashlib
import json
import os
import pathlib
import sys

root = pathlib.Path(__file__).resolve().parents[1]
report_dir = root / 'artifacts/terrain-recovery-r11'
plan = json.loads((report_dir / 'preflight.json').read_text(encoding='utf-8'))
source = pathlib.Path(plan['sourceProject']).resolve()
destination = pathlib.Path(plan['targetWorkCopy']).resolve()
assert destination.is_relative_to(root.resolve()) and source.drive.upper() == 'D:'
assert destination.name == 'Client231229-2022.3.13f1'
assert os.name == 'nt', 'Publication relies on Windows non-overwriting rename semantics'
assert '--apply' in sys.argv, 'Use --apply only after recording complete target GUID absence'
precheck = json.loads((report_dir / 'target-guid-precheck.json').read_text(encoding='utf-8'))
assert precheck['exitCode'] == 1 and precheck['matches'] == []
assert (report_dir / 'application.json').exists() is False

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def persist(value):
    temporary = report_dir / 'application.tmp'
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    os.replace(temporary, report_dir / 'application.json')

files = {entry['path']: entry for entry in plan['files']}
for relative, entry in files.items():
    src, dst = source / relative, destination / relative
    assert src.resolve().is_relative_to(source)
    assert dst.resolve().is_relative_to(destination)
    assert sha(src) == entry['sha256'] and not dst.exists(), relative
    for ancestor in (src, *src.parents):
        if ancestor == source.parent:
            break
        assert not (ancestor.stat().st_file_attributes & 0x400), 'Source link/reparse point rejected'

protected = json.loads((root / 'artifacts/transform-r10/client-watch-before.json').read_text(encoding='utf-8'))
before = {relative: sha(destination / relative) for relative in protected}
assert before['Assets/02.Arts/Scene/Map/Map_60001/Map_60001.asset'] == plan['sourceAndTargetTerrainSha256']
staging = report_dir / 'staging'; staging.mkdir()
for relative, entry in files.items():
    staged = staging / relative; staged.parent.mkdir(parents=True, exist_ok=True)
    with staged.open('xb') as output:
        output.write((source / relative).read_bytes()); output.flush(); os.fsync(output.fileno())
    assert sha(staged) == entry['sha256']

folder = destination / 'Assets/ResourcesBuild/Scenes/Common/TerrainTextures/Grass_01_Layer'
assert not folder.exists() and folder.parent.resolve().is_relative_to(destination)
transaction = {'status': 'PREPARED', 'time': datetime.datetime.now(datetime.timezone.utc).isoformat(),
    'sourceProject': str(source), 'targetWorkCopy': str(destination), 'files': plan['files'], 'created': [],
    'protectedBefore': before, 'sourceVersion': '2021.3.15f1', 'sourceTerrainMatchesTarget': True,
    'originalDWrite': False, 'unityStarted': False, 'packaging': False}
persist(transaction)
folder.mkdir()
# Make the diffuse dependency available before publishing the layer's GUID.
prefix = 'Assets/ResourcesBuild/Scenes/Common/TerrainTextures/Grass_01_Layer'
order = [prefix + '/Grass_01a_t.png', prefix + '/Grass_01a_t.png.meta',
    prefix + '/Grass_01_Layer.terrainlayer', prefix + '.meta', prefix + '/Grass_01_Layer.terrainlayer.meta']
try:
    for relative in order:
        dst = destination / relative
        assert not dst.exists() and dst.resolve().is_relative_to(destination)
        os.rename(staging / relative, dst)  # On Windows this fails if a destination already exists.
        assert sha(dst) == files[relative]['sha256']
        transaction['created'].append(relative); persist(transaction)
    after = {relative: sha(destination / relative) for relative in protected}
    assert after == before
    assert all(sha(source / relative) == entry['sha256'] for relative, entry in files.items())
    transaction.update(status='COMMITTED', completedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
        protectedAfter=after, protectedUnchanged=True, sourceFilesUnchanged=True,
        rollback='Only remove the five recorded new files if their hashes still match; never delete modified user files.')
    persist(transaction)
except Exception as error:
    transaction.update(status='RECOVERY_REQUIRED', error=str(error))
    persist(transaction)
    raise
print(json.dumps({'status': transaction['status'], 'createdFiles': len(transaction['created']),
    'protectedNativeInputsUnchanged': len(before), 'sourceFilesUnchanged': True}))

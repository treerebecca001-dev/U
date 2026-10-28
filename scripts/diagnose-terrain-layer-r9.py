"""Read-only, exact-GUID source search. No inferred material assignment or cache mutation."""
import datetime, hashlib, json, os, pathlib, re, sys, time

root = pathlib.Path(__file__).resolve().parents[1]
original = pathlib.Path(r'D:\Work_ART\漂流NFT\@WLN_work\Client231229')
out = root / 'artifacts/preview-r9/source-dependency-search.json'
assert not out.exists(), 'Do not overwrite evidence'
guid = '3a2a9d837fbf13e4b9197bb9b1ba3484'
start = time.monotonic()
report = {'status': 'IN_PROGRESS', 'startedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
          'guid': guid, 'fileId': '8574412962073106934', 'originalReadOnly': str(original), 'scopes': []}
pattern = re.compile(rb'^guid:\s*' + guid.encode() + rb'\s*$', re.IGNORECASE | re.MULTILINE)
for relative in ('Assets', 'Packages', 'Library/PackageCache'):
    base = original / relative
    entry = {'path': str(base), 'exists': base.exists(), 'metaFilesChecked': 0, 'metaBytesRead': 0, 'matches': [], 'errors': [], 'skippedLinks': []}
    pending = [base] if base.exists() else []
    while pending:
        folder = pending.pop()
        try:
            for item in os.scandir(folder):
                if item.is_symlink() or (hasattr(item, 'is_junction') and item.is_junction()):
                    entry['skippedLinks'].append(item.path)
                elif item.is_dir(follow_symlinks=False):
                    pending.append(pathlib.Path(item.path))
                elif item.name.lower().endswith('.meta') and item.is_file(follow_symlinks=False):
                    try:
                        data = pathlib.Path(item.path).read_bytes()
                        entry['metaFilesChecked'] += 1
                        entry['metaBytesRead'] += len(data)
                        if pattern.search(data): entry['matches'].append(item.path)
                    except OSError as error: entry['errors'].append({'path': item.path, 'error': str(error)})
        except OSError as error: entry['errors'].append({'path': str(folder), 'error': str(error)})
    report['scopes'].append(entry)
    print(json.dumps({'scope': relative, 'metaFiles': entry['metaFilesChecked'], 'matches': len(entry['matches']), 'errors': len(entry['errors'])}), flush=True)

variants = {'asciiGuid': guid.encode(), 'raw16Bytes': bytes.fromhex(guid),
            'uint32LittleEndian': b''.join(bytes.fromhex(guid)[i:i+4][::-1] for i in range(0, 16, 4))}
report['cacheSearch'] = []
for name in ('SourceAssetDB', 'ArtifactDB'):
    source = original / 'Library' / name
    data = source.read_bytes()
    report['cacheSearch'].append({'path': str(source), 'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest(),
                                 'bytePatternMatches': {kind: data.count(value) for kind, value in variants.items()},
                                 'scope': 'Exact byte-pattern absence; no LMDB semantic reconstruction or cache format claim.'})
report['originalMetadataCache'] = [{'path': str(original / 'Library/metadata/3a' / name), 'exists': (original / 'Library/metadata/3a' / name).exists()}
                                   for name in (guid, guid + '.info')]
report['nativeTerrainCopies'] = []
relative = pathlib.Path('Assets/02.Arts/Scene/Map/Map_60001/Map_60001.asset')
for base in (original, root / 'samples/Client231229', root / 'Workspace/Baselines/Client231229-2022.3.13f1'):
    data = (base / relative).read_bytes()
    report['nativeTerrainCopies'].append({'path': str(base / relative), 'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest()})
report['backupScope'] = {'originalRootArchives': [p.name for p in original.iterdir() if p.is_file() and p.suffix.lower() in ('.zip', '.7z', '.rar', '.unitypackage')],
                        'localHistoryFolders': [{'path': str(original / name), 'exists': (original / name).exists()} for name in ('.git', '.svn', 'Assets/.git', 'Assets/.svn')],
                        'humanResponse': '2026-10-22: user does not know which source layer is missing; no alternate local source path supplied.'}
report['minimumMissingDependency'] = {'required': 'Original TerrainLayer and its matching .meta for this exact GUID and fileID; then any texture references it contains.',
                                       'notRecoverableFromReferenceAlone': ['original filename', 'diffuse texture GUID', 'tiling/offset', 'normal/mask settings'],
                                       'weightsPreserved': True, 'replacementGuessed': False}
all_scanned = all(not r['errors'] and not r['skippedLinks'] for r in report['scopes'])
found = any(r['matches'] for r in report['scopes'])
report['status'] = 'SOURCE_FOUND_REQUIRES_INSPECTION' if found else 'SOURCE_DEPENDENCY_MISSING' if all_scanned else 'SEARCH_INCOMPLETE'
report['completedUtc'] = datetime.datetime.now(datetime.timezone.utc).isoformat()
report['elapsedSeconds'] = time.monotonic() - start
out.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps({'status': report['status'], 'metaFiles': sum(s['metaFilesChecked'] for s in report['scopes']), 'elapsedSeconds': report['elapsedSeconds']}), flush=True)
sys.exit(0 if all_scanned else 1)

#!/usr/bin/env python3
"""Retain current build/player measurements; large movies/binaries remain exact external artifacts."""
import gzip
import hashlib
import json
from pathlib import Path
import shutil
import verify as v

state = v.ROOT / 'artifacts/studio/workflows/P4.2j'
run = json.loads((state / 'run.json').read_text())
activation = json.loads((state / 'worker-activation.json').read_text())
for source, target in [(v.ROOT / '.evidence/P42jPlayer', v.OUT / 'W-GAME-01/p42j-player'),
                       (v.ROOT / '.evidence/P42jLifecycle', v.OUT / 'W-GAME-05/p42j-player-lifecycle'),
                       (v.ROOT / '.evidence/P42jLifecycleOwned', v.OUT / 'W-GAME-05/p42j-owned-window-lifecycle')]:
    if not source.exists():
        continue
    target.mkdir(parents=True, exist_ok=True)
    external = []
    for path in sorted(source.rglob('*')):
        if not path.is_file():
            continue
        relative = path.relative_to(source)
        if path.suffix in ('.mp4', '.raw', '.snap') or relative.parts[0] == 'player':
            with path.open('rb') as stream:
                digest = hashlib.file_digest(stream, 'sha256').hexdigest()
            external.append({'path': str(path.relative_to(v.ROOT)), 'bytes': path.stat().st_size, 'sha256': digest})
            if path.name not in ('build-report.json', 'revision.txt'):
                continue
        destination = target / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        if path.suffix in ('.csv', '.log') and path.stat().st_size > 1024 * 1024:
            data = v.scrub(path.read_text(errors='replace')).encode()
            destination.with_suffix(destination.suffix + '.gz').write_bytes(gzip.compress(data, mtime=0))
        elif path.suffix in ('.json', '.jsonl', '.txt', '.log', '.csv', '.md', '.xml', '.trx'):
            destination.write_text(v.scrub(path.read_text(errors='replace')))
        elif path.stat().st_size <= 20 * 1024 * 1024:
            shutil.copyfile(path, destination)
        else:
            with path.open('rb') as stream:
                digest = hashlib.file_digest(stream, 'sha256').hexdigest()
            external.append({'path': str(path.relative_to(v.ROOT)), 'bytes': path.stat().st_size, 'sha256': digest})
    (target / 'external-artifacts.json').write_text(json.dumps({'revision': run['revision'], 'releaseId': activation['release'],
        'reportedAt': v.utc(), 'files': external}, indent=2) + '\n')
    files = sorted(path for path in target.rglob('*') if path.is_file() and path.name != 'SHA256SUMS')
    (target / 'SHA256SUMS').write_text(''.join(hashlib.sha256(path.read_bytes()).hexdigest() + '  ' + str(path.relative_to(target)) + '\n' for path in files))
    print('RETAINED', target.relative_to(v.ROOT), 'external files', len(external))

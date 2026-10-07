#!/usr/bin/env python3
"""Retain current-run full snapshots losslessly outside Git, with raw and compressed hashes."""
import hashlib
import json
from pathlib import Path
import subprocess
import verify as v

state = v.ROOT / 'artifacts/studio/workflows/P4.2j'
run = json.loads((state / 'run.json').read_text())
retained = v.ROOT / '.evidence/P42jLarge'
retained.mkdir(parents=True, exist_ok=True)
manifest_path = state / 'large-evidence.json'
manifest = json.loads(manifest_path.read_text()) if manifest_path.exists() else []
paths = [p for folder in v.OUT.glob('W-PLUG-01/p42j-native-*') if (folder / 'result.json').exists() for p in folder.glob('native/**/*.snap')]
for report in v.OUT.glob('W-GAME-08/native-memory-and-pumps-20261007*/memory-and-pumps.json'):
    if not (report.parent / 'result.json').exists():
        continue
    receipt = json.loads((report.parent / 'result.json').read_text())
    if receipt.get('revision') != run['revision'] or receipt.get('started', '') < run['startedAt']:
        continue
    directory = Path(json.loads(report.read_text())['snapshotDirectory'].replace('~', str(Path.home()), 1))
    if not directory.resolve().is_relative_to(v.ROOT.resolve()):
        raise RuntimeError('Snapshot directory is outside the owned checkout')
    paths.extend(directory.glob('*.snap'))
for source in paths:
    relative = str(source.relative_to(v.ROOT))
    with source.open('rb') as stream:
        raw_sha = hashlib.file_digest(stream, 'sha256').hexdigest()
    target = retained / (raw_sha + '.snap.zst')
    subprocess.run(['zstd', '-q', '-1', '-f', str(source), '-o', str(target)], check=True)
    process = subprocess.Popen(['zstd', '-q', '-d', '-c', str(target)], stdout=subprocess.PIPE)
    restored = hashlib.file_digest(process.stdout, 'sha256').hexdigest()
    if process.wait() or restored != raw_sha:
        raise RuntimeError('Lossless snapshot verification failed; original retained: ' + relative)
    with target.open('rb') as stream:
        compressed_sha = hashlib.file_digest(stream, 'sha256').hexdigest()
    manifest.append({'revision': run['revision'], 'originalPath': relative, 'rawBytes': source.stat().st_size,
                     'rawSha256': raw_sha, 'retainedPath': str(target.relative_to(v.ROOT)),
                     'retainedBytes': target.stat().st_size, 'retainedSha256': compressed_sha,
                     'restoreCommand': 'zstd -d ' + str(target.relative_to(v.ROOT)), 'losslessVerified': True})
    manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
    source.unlink()
    print('RETAINED', relative, '->', target.name, flush=True)
print('LARGE_EVIDENCE_COMPLETE', len(manifest), flush=True)

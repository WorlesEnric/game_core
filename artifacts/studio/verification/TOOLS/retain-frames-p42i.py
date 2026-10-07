#!/usr/bin/env python3
"""Archive redundant workflow frame sequences outside Git; retain keyframes and exact raw hashes."""
import hashlib
import json
from pathlib import Path
import tarfile
import verify as v

retained = v.ROOT / '.evidence/P42iLarge'
retained.mkdir(parents=True, exist_ok=True)
for frames in sorted(v.OUT.glob('W-*/p42i-*/workflow/frames')):
    files = sorted(path for path in frames.iterdir() if path.is_file())
    if not files:
        continue
    identity = hashlib.sha256(str(frames.relative_to(v.ROOT)).encode()).hexdigest()[:16]
    archive = retained / ('workflow-frames-' + identity + '.tar.gz')
    manifest = [{'name': path.name, 'bytes': path.stat().st_size, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()} for path in files]
    with tarfile.open(archive, 'w:gz') as output:
        for path in files:
            output.add(path, arcname=path.name, recursive=False)
    with tarfile.open(archive, 'r:gz') as source:
        if set(source.getnames()) != {item['name'] for item in manifest}:
            raise RuntimeError('Frame archive contents differ; originals retained')
        for item in manifest:
            stream = source.extractfile(item['name'])
            if hashlib.file_digest(stream, 'sha256').hexdigest() != item['sha256']:
                raise RuntimeError('Frame archive digest mismatch; originals retained')
    with archive.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    receipt = {'retainedPath': str(archive.relative_to(v.ROOT)), 'retainedBytes': archive.stat().st_size,
               'retainedSha256': digest, 'losslessVerified': True, 'frames': manifest,
               'keyframes': 'Separate keyframes/ remains directly reviewable in Git.'}
    (frames.parent / 'frame-sequence-manifest.json').write_text(json.dumps(receipt, indent=2) + '\n')
    for path in files:
        path.unlink()
    frames.rmdir()
    print('RETAINED', len(files), 'frames in', archive.name)

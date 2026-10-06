"""Export only the registered source's baked world catalog, never candidate inputs/code."""
import hashlib
import json
from pathlib import Path
import subprocess
from path_policy import contained


def export(source: Path, destination: Path, change_set: str, revision: str) -> dict:
    # Enumerate tracked descriptions: ignored/candidate-created files are not source worlds.
    result = subprocess.run(['git', '-C', str(source), 'ls-files', '-z', '--',
                             'Assets/**/*.catalog.json'], check=True, capture_output=True)
    paths = [p for p in result.stdout.decode().split('\0') if p]
    if len(paths) != 1:
        return {'error': 'source_world_missing', 'count': len(paths)}
    path = contained(source, paths[0])
    data = path.read_bytes()
    document = json.loads(data)
    if document.get('descriptionFormat') != 'gamecore.catalog-description/1':
        raise ValueError('source_world_invalid: expected a baked catalog description')
    contained(destination.parent, destination.name)
    destination.mkdir(parents=True, exist_ok=True)
    target = contained(destination, 'catalog.json')
    if target.exists():
        target.chmod(0o644)
    target.write_bytes(data)
    target.chmod(0o444)
    return {'path': str(target), 'sourcePath': paths[0], 'sha256': hashlib.sha256(data).hexdigest(),
            'changeSetId': change_set, 'sourceRevision': revision}

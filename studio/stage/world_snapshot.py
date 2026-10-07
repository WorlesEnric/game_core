"""Export a current Editor-computed world description, never candidate inputs/code."""
import hashlib
import json
from pathlib import Path
import subprocess
from path_policy import contained

SNAPSHOT_PATH = 'Library/GameCoreStudio/StageWorldSnapshot.json'
SNAPSHOT_SCHEMA = 'gamecore.studio.stage-world-snapshot/1'
# Mirror Unity import boundaries and StageWorldSnapshot: a catalog contributor may
# consume any imported data format. Never inspect credential bytes under source roots.
SKIPPED_DIRECTORIES = {'Library', 'Temp', 'Logs', 'obj', 'bin', 'node_modules', 'UserSettings'}


def private_source(name: str) -> bool:
    return name.lower() in {'auth.json', 'providers.env', 'gamecorestudio.json'} or name.lower().endswith('.key')


def source_inputs(source: Path, packages: Path) -> list[dict]:
    result = []

    def visit(root: Path, directory: Path, scope: str):
        if not directory.exists():
            return
        for child in sorted(directory.iterdir()):
            if child.name.startswith('.') or child.name.endswith('~') or private_source(child.name):
                continue
            relative = child.relative_to(root).as_posix()
            path = contained(root, relative)
            if path.is_dir():
                if path.name not in SKIPPED_DIRECTORIES:
                    visit(root, path, scope)
            else:
                with path.open('rb') as stream:
                    digest = hashlib.sha256()
                    for block in iter(lambda: stream.read(1024 * 1024), b''):
                        digest.update(block)
                result.append({'scope': scope, 'path': relative, 'sha256': digest.hexdigest()})

    for name in ('Assets', 'ProjectSettings', 'Packages'):
        visit(source, contained(source, name), 'project')
    visit(packages, packages, 'packages')
    return sorted(result, key=lambda item: (item['scope'], item['path']))


def export(source: Path, destination: Path, change_set: str, revision: str,
           packages: Path | None = None) -> dict:
    # A registered project's tracked description identifies its world; its cached bytes
    # are NOT authority for freshness. The Editor publishes Entry.Compute's no-write result.
    result = subprocess.run(['git', '-C', str(source), 'ls-files', '-z', '--',
                             'Assets/**/*.catalog.json'], check=True, capture_output=True)
    paths = [p for p in result.stdout.decode().split('\0') if p]
    if len(paths) != 1:
        return {'error': 'source_world_missing', 'count': len(paths)}
    contained(source, paths[0])
    snapshot_path = contained(source, SNAPSHOT_PATH)

    def stale(detail):
        raise ValueError(f'bake_stale: {paths[0]}: {detail}; use Studio Stage in Edit mode '
                         'to save authored changes and compute the current world snapshot (no manual rebake)')

    if not snapshot_path.is_file():
        stale('current source-world snapshot is missing')
    try:
        snapshot_bytes = snapshot_path.read_bytes()
        snapshot = json.loads(snapshot_bytes)
    except (ValueError, OSError) as error:
        stale(f'unreadable source-world snapshot: {error}')
    if not isinstance(snapshot, dict) or snapshot.get('schema') != SNAPSHOT_SCHEMA:
        stale('unsupported source-world snapshot')
    if snapshot.get('sourceRevision') != revision or snapshot.get('sourcePath') != paths[0]:
        stale('snapshot is not bound to this source revision and tracked world')
    description = snapshot.get('description')
    if not isinstance(description, str):
        stale('snapshot has no computed catalog description')
    data = description.encode('utf-8')
    digest = hashlib.sha256(data).hexdigest()
    if snapshot.get('sha256') != digest:
        stale('computed catalog description changed after export')
    inputs = source_inputs(source, packages or source / 'Packages')
    declared_inputs = snapshot.get('inputs')
    if not isinstance(declared_inputs, list) or any(not isinstance(item, dict) or
            not all(isinstance(item.get(key), str) for key in ('scope', 'path', 'sha256'))
            for item in declared_inputs):
        stale('snapshot has no valid source inventory')
    if snapshot.get('inputs') != inputs:
        expected = {(i['scope'], i['path']): i['sha256'] for i in declared_inputs}
        actual = {(i['scope'], i['path']): i['sha256'] for i in inputs}
        changed = sorted(f'{scope}/{path}' for scope, path in actual.keys() | expected.keys()
                         if actual.get((scope, path)) != expected.get((scope, path)))
        stale('authored or compiler inputs changed: ' + ', '.join(changed[:8]))
    document = json.loads(description)
    if document.get('descriptionFormat') != 'gamecore.catalog-description/1':
        stale('expected a computed catalog description')
    contained(destination.parent, destination.name)
    destination.mkdir(parents=True, exist_ok=True)
    target = contained(destination, 'catalog.json')
    if target.exists():
        target.chmod(0o644)
    target.write_bytes(data)
    target.chmod(0o444)
    return {'path': str(target), 'sourcePath': paths[0], 'sha256': digest,
            'changeSetId': change_set, 'sourceRevision': revision,
            'sourceSnapshotSha256': hashlib.sha256(snapshot_bytes).hexdigest()}

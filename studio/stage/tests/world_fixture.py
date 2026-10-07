"""Data-only source project for slot tests; never publishes into a real Unity project."""
import hashlib
import json
from pathlib import Path
import subprocess
import sys
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import world_snapshot


def create(repository: Path) -> Path:
    project = repository / 'games/hollowmere'
    packages = repository / 'Packages'
    packages.mkdir(parents=True)
    (project / 'ProjectSettings').mkdir(parents=True)
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 6000.0.75f1\n')
    (project / 'Packages').mkdir()
    (project / 'Packages/manifest.json').write_text('{"dependencies":{}}\n')
    description_path = 'Assets/World/world.catalog.json'
    catalog = project / description_path
    catalog.parent.mkdir(parents=True)
    description = json.dumps({'descriptionFormat': 'gamecore.catalog-description/1'})
    catalog.write_text(description)
    subprocess.run(['git', 'init', '-q', str(repository)], check=True)
    subprocess.run(['git', '-C', str(repository), 'add', '.'], check=True)
    subprocess.run(['git', '-C', str(repository), '-c', 'user.name=Stage fixture', '-c',
                    'user.email=stage@example.invalid', 'commit', '-qm', 'source fixture'], check=True)
    revision = subprocess.check_output(['git', '-C', str(repository), 'rev-parse', 'HEAD'], text=True).strip()
    snapshot = project / world_snapshot.SNAPSHOT_PATH
    snapshot.parent.mkdir(parents=True)
    snapshot.write_text(json.dumps({
        'schema': world_snapshot.SNAPSHOT_SCHEMA,
        'sourceRevision': revision, 'sourcePath': description_path,
        'description': description, 'sha256': hashlib.sha256(description.encode()).hexdigest(),
        'inputs': world_snapshot.source_inputs(project, packages),
    }))
    return project


if __name__ == '__main__':
    print(create(Path(sys.argv[1])))

"""R6-A request 1: the source-world export has no candidate authority."""
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import world_snapshot


class WorldSnapshotTests(unittest.TestCase):
    def test_R6_A_01_source_export_is_bound_read_only_and_not_candidate_input(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            subprocess.run(['git', 'init', '-q', str(root)], check=True)
            source = root / 'Assets/World/world.catalog.json'
            source.parent.mkdir(parents=True)
            source.write_text(json.dumps({'descriptionFormat': 'gamecore.catalog-description/1'}))
            subprocess.run(['git', '-C', str(root), 'add', 'Assets'], check=True)
            # An untracked description cannot replace the trusted source.
            (source.parent / 'candidate.catalog.json').write_text('{}')
            record = world_snapshot.export(root, root / 'snapshot', 'cs_request', 'source-revision')
            self.assertEqual(record['changeSetId'], 'cs_request')
            self.assertEqual(record['sourceRevision'], 'source-revision')
            self.assertEqual(record['sha256'], hashlib.sha256(source.read_bytes()).hexdigest())
            self.assertEqual(Path(record['path']).stat().st_mode & 0o222, 0)
            self.assertEqual(Path(record['path']).read_bytes(), source.read_bytes())

    def test_R6_A_01_missing_and_ambiguous_world_fail_closed(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            subprocess.run(['git', 'init', '-q', str(root)], check=True)
            self.assertEqual(world_snapshot.export(root, root / 'snapshot', 'cs', 'rev')['error'], 'source_world_missing')
            for name in ['one', 'two']:
                path = root / 'Assets' / name / 'world.catalog.json'
                path.parent.mkdir(parents=True)
                path.write_text('{}')
            subprocess.run(['git', '-C', str(root), 'add', 'Assets'], check=True)
            self.assertEqual(world_snapshot.export(root, root / 'snapshot', 'cs', 'rev')['count'], 2)

    def test_R6_A_01_source_link_refused(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            subprocess.run(['git', 'init', '-q', str(root)], check=True)
            (root / 'Assets/World').mkdir(parents=True)
            (root / 'Assets/World/world.catalog.json').symlink_to(root / 'outside')
            subprocess.run(['git', '-C', str(root), 'add', 'Assets'], check=True)
            with self.assertRaisesRegex(ValueError, 'stage_path_link'):
                world_snapshot.export(root, root / 'snapshot', 'cs', 'rev')

    def test_R6_A_01_reused_destination_link_refused(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            subprocess.run(['git', 'init', '-q', str(root)], check=True)
            source = root / 'Assets/World/world.catalog.json'
            source.parent.mkdir(parents=True)
            source.write_text(json.dumps({'descriptionFormat': 'gamecore.catalog-description/1'}))
            subprocess.run(['git', '-C', str(root), 'add', 'Assets'], check=True)
            (root / 'outside').mkdir()
            (root / 'snapshot').symlink_to(root / 'outside')
            with self.assertRaisesRegex(ValueError, 'stage_path_link'):
                world_snapshot.export(root, root / 'snapshot', 'cs', 'rev')
            self.assertFalse((root / 'outside/catalog.json').exists())

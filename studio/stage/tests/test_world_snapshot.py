"""R6-A/R10-A: source-bound freshness, with no candidate catalog authority."""
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
    @staticmethod
    def publish(root, description, revision='source-revision', packages=None):
        source = root / 'Assets/World/world.catalog.json'
        snapshot = root / world_snapshot.SNAPSHOT_PATH
        snapshot.parent.mkdir(parents=True, exist_ok=True)
        snapshot.write_text(json.dumps({
            'schema': world_snapshot.SNAPSHOT_SCHEMA,
            'sourceRevision': revision, 'sourcePath': source.relative_to(root).as_posix(),
            'description': description, 'sha256': hashlib.sha256(description.encode()).hexdigest(),
            'inputs': world_snapshot.source_inputs(root, packages or root / 'Packages'),
        }))

    @staticmethod
    def source(root):
        subprocess.run(['git', 'init', '-q', str(root)], check=True)
        path = root / 'Assets/World/world.catalog.json'
        path.parent.mkdir(parents=True)
        path.write_text(json.dumps({'descriptionFormat': 'gamecore.catalog-description/1',
                                    'recipes': ['committed-stale']}))
        subprocess.run(['git', '-C', str(root), 'add', 'Assets'], check=True)
        return path

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
            self.publish(root, source.read_text())
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
            self.publish(root, source.read_text(), 'rev')
            (root / 'outside').mkdir()
            (root / 'snapshot').symlink_to(root / 'outside')
            with self.assertRaisesRegex(ValueError, 'stage_path_link'):
                world_snapshot.export(root, root / 'snapshot', 'cs', 'rev')
            self.assertFalse((root / 'outside/catalog.json').exists())

    def test_R10_A_W_MECH_01_stale_committed_bake_without_current_export_refuses_early(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.source(root)
            with self.assertRaisesRegex(ValueError, 'bake_stale: Assets/World/world.catalog.json'):
                world_snapshot.export(root, root / 'snapshot', 'cs', 'source-revision')
            self.assertFalse((root / 'snapshot/catalog.json').exists())

    def test_R10_A_W_MECH_01_worker_edit_and_normal_undo_export_current_not_committed_bake(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = self.source(root)
            committed = source.read_bytes()
            authored = root / 'Assets/World/definition.asset'
            for state in ('worker-edit', 'normal-undo'):
                authored.write_text(state)
                computed = json.dumps({'descriptionFormat': 'gamecore.catalog-description/1',
                                       'recipes': [state]})
                self.publish(root, computed)
                record = world_snapshot.export(root, root / 'snapshot', 'cs', 'source-revision')
                self.assertEqual(Path(record['path']).read_text(), computed)
                self.assertEqual(record['sha256'], hashlib.sha256(computed.encode()).hexdigest())
                self.assertEqual(source.read_bytes(), committed, 'staging must not rebake the live project')

    def test_R10_A_W_MECH_01_source_edit_after_export_cannot_be_signed(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = self.source(root)
            authored = root / 'Assets/World/definition.asset'
            authored.write_text('before')
            self.publish(root, source.read_text())
            authored.write_text('worker-edit')
            with self.assertRaisesRegex(ValueError, 'bake_stale:.*definition.asset'):
                world_snapshot.export(root, root / 'snapshot', 'cs', 'source-revision')

    def test_R10_A_W_MECH_01_new_or_deleted_definition_invalidates_snapshot(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = self.source(root)
            self.publish(root, source.read_text())
            authored = root / 'Assets/World/new-definition.asset'
            authored.write_text('new entity definition')
            with self.assertRaisesRegex(ValueError, 'bake_stale:.*new-definition.asset'):
                world_snapshot.export(root, root / 'snapshot', 'cs', 'source-revision')
            self.publish(root, source.read_text())
            authored.unlink()
            with self.assertRaisesRegex(ValueError, 'bake_stale:.*new-definition.asset'):
                world_snapshot.export(root, root / 'snapshot', 'cs', 'source-revision')

    def test_R10_A_W_MECH_01_package_contributor_changes_invalidate_snapshot(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = self.source(root)
            packages = root / 'trusted-packages'
            packages.mkdir()
            contributor = packages / 'Contributor.cs'
            contributor.write_text('catalog before')
            self.publish(root, source.read_text(), packages=packages)
            contributor.write_text('catalog after')
            with self.assertRaisesRegex(ValueError, 'bake_stale:.*Contributor.cs'):
                world_snapshot.export(root, root / 'snapshot', 'cs', 'source-revision', packages)

    def test_R10_A_W_MECH_01_candidate_snapshot_cannot_replace_missing_source_receipt(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.source(root)
            candidate = root / 'candidate'
            candidate.mkdir()
            (candidate / 'StageWorldSnapshot.json').write_text(json.dumps({'description': 'forged'}))
            with self.assertRaisesRegex(ValueError, 'bake_stale'):
                world_snapshot.export(root, root / 'snapshot', 'cs', 'source-revision')

    def test_R10_A_W_MECH_01_snapshot_revision_and_description_tampering_refuse(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = self.source(root)
            self.publish(root, source.read_text(), 'other-revision')
            with self.assertRaisesRegex(ValueError, 'bake_stale:.*source revision'):
                world_snapshot.export(root, root / 'snapshot', 'cs', 'source-revision')
            self.publish(root, source.read_text())
            receipt = root / world_snapshot.SNAPSHOT_PATH
            document = json.loads(receipt.read_text())
            document['description'] = '{}'
            receipt.write_text(json.dumps(document))
            with self.assertRaisesRegex(ValueError, 'bake_stale:.*changed after export'):
                world_snapshot.export(root, root / 'snapshot', 'cs', 'source-revision')

    def test_R10_A_W_MECH_01_imported_binary_data_changes_invalidate_snapshot(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = self.source(root)
            authored = root / 'Assets/World/config.bytes'
            authored.write_bytes(b'first authored data')
            self.publish(root, source.read_text())
            authored.write_bytes(b'changed authored data')
            with self.assertRaisesRegex(ValueError, 'bake_stale:.*config.bytes'):
                world_snapshot.export(root, root / 'snapshot', 'cs', 'source-revision')

    def test_source_inventory_never_follows_private_or_unimported_paths(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = self.source(root)
            before = world_snapshot.source_inputs(root, root / 'Packages')
            # Links would fail the containment guard if the inventory inspected them;
            # use dangling links so no real credential bytes exist in this fixture.
            for name in ('auth.json', 'providers.env', 'app.key', 'GameCoreStudio.json', '.private', 'Private~'):
                (source.parent / name).symlink_to(root / 'must-not-open')
            self.assertEqual(world_snapshot.source_inputs(root, root / 'Packages'), before)

    def test_R10_A_W_MECH_01_linked_source_receipt_cannot_authorize_export(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.source(root)
            receipt = root / world_snapshot.SNAPSHOT_PATH
            receipt.parent.mkdir(parents=True)
            receipt.symlink_to(root / 'candidate.json')
            with self.assertRaisesRegex(ValueError, 'stage_path_link'):
                world_snapshot.export(root, root / 'snapshot', 'cs', 'source-revision')

"""STAGE-TMP2: immutable provenance survives incomplete and reused Libraries."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import sys
sys.path.insert(0, str(Path(__file__).parents[1]))
import analysis_context as context
import world_fixture


class AnalysisContextTests(unittest.TestCase):
    def test_seed_reuse_and_tamper(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / 'cache/analysis-context'
            source.mkdir(parents=True)
            (source / 'Unity.Core.Editor.dll').write_bytes(b'pinned')
            manifest = root / 'manifest.json'
            manifest.write_text(json.dumps({'Unity.Core.Editor.dll': hashlib.sha256(b'pinned').hexdigest()}))
            target = root / 'slot/analysis-context'
            with patch.object(context, 'MANIFEST', manifest):
                context.seed(source, target)
                (target / 'Unity.Core.Editor.dll').unlink()
                # No Library exists: reuse must still recover the trusted context.
                context.seed(source, target)
                self.assertEqual((target / 'Unity.Core.Editor.dll').read_bytes(), b'pinned')
                (source / 'Unity.Core.Editor.dll').write_bytes(b'tampered')
                with self.assertRaisesRegex(ValueError, 'digest mismatch'):
                    context.seed(source, target)
                manifest.unlink()
                with self.assertRaisesRegex(ValueError, 'trusted Unity metadata missing'):
                    context.seed(source, target)

    def test_source_parent_link_refused(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'actual').mkdir()
            (root / 'actual/x').write_bytes(b'x')
            (root / 'link').symlink_to(root / 'actual', target_is_directory=True)
            with self.assertRaisesRegex(ValueError, 'link'):
                context.checked(root / 'link', 'x', hashlib.sha256(b'x').hexdigest())

    def test_make_slot_reseeds_context_on_reuse(self):
        import argparse
        import importlib.util
        stage = Path(__file__).parents[1]
        repo = stage.parents[1]
        spec = importlib.util.spec_from_file_location('make_slot', stage / 'make-slot.py')
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            project = world_fixture.create(root / 'source')
            warm = root / 'cache/Library'
            warm.mkdir(parents=True)
            (warm / 'Unity.Core.Editor.dll').write_bytes(b'pinned')
            manifest = root / 'manifest.json'
            manifest.write_text(json.dumps({'Unity.Core.Editor.dll': hashlib.sha256(b'pinned').hexdigest()}))
            args = argparse.Namespace(slot='pressure', source_project=project,
                                      slot_root=root / 'slots', candidate=repo / 'samples/mechanisms/pressure-plate/candidate',
                                      force=False, warm_library=warm)
            with patch.object(context, 'MANIFEST', manifest):
                self.assertFalse(module.make_slot(args)['reused'])
                target = root / 'slots/pressure/analysis-context/Unity.Core.Editor.dll'
                target.unlink()
                (root / 'slots/pressure/project/Library/Unity.Core.Editor.dll').unlink()
                self.assertTrue(module.make_slot(args)['reused'])
                self.assertEqual(target.read_bytes(), b'pinned')
                self.assertFalse((root / 'slots/pressure/project/Library/Unity.Core.Editor.dll').exists())

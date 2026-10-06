import importlib.util
import os
from pathlib import Path
import subprocess
import unittest

ROOT = Path(__file__).resolve().parents[5]
spec = importlib.util.spec_from_file_location('installer', ROOT / 'studio/etos/install-state.py')
installer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(installer)


class Acceptance(unittest.TestCase):
    def test_P42B_TARIFF_owner_declared_image_estimate(self):
        template = (subprocess.check_output(['git', 'show', 'origin/main:studio/etos/ops.toml.tmpl'], cwd=ROOT, text=True)
                    if os.environ.get('P42B_BEFORE') else (ROOT / 'studio/etos/ops.toml.tmpl').read_text())
        prices = installer.tariffs(template, "image") + installer.tariffs(template, "tts")
        image = next(p for p in prices if p['op'] == 'image')
        self.assertEqual(image['per_unit'], .20)
        self.assertEqual(image['source'], 'operator')
        self.assertEqual(image['note'], "Owner-declared total per-image estimate (2026-10-06), basis: OpenAI gpt-image published list prices; Echo publishes no tariff")
        self.assertEqual(next(p for p in prices if p['op'] == 'tts')['source'], 'published')

    def test_P42B_TARIFF_placeholder_still_refuses(self):
        template = (ROOT / 'studio/etos/ops.toml.tmpl').read_text().replace('per_unit = 0.20', 'per_unit = "SET_BY_OPERATOR"')
        with self.assertRaisesRegex(ValueError, 'tariff_placeholder'):
            installer.tariffs(template, "image")

    def test_R5_B_describe_placeholder_refuses_independently(self):
        template = (ROOT / "studio/etos/ops.toml.tmpl").read_text().replace("per_unit = 0.01", "per_unit = 0.0 # SET_BY_OPERATOR")
        with self.assertRaisesRegex(ValueError, "tariff_placeholder"):
            installer.tariffs(template, "describe")

class DatasetReceipts(unittest.TestCase):
    def setUp(self):
        import sys
        import tempfile
        sys.path.insert(0, str(ROOT / 'artifacts/studio/verification/TOOLS'))
        import report_p42b
        self.report = report_p42b
        self.previous = report_p42b.v.OUT
        self.temp = tempfile.TemporaryDirectory()
        report_p42b.v.OUT = Path(self.temp.name)
        self.addCleanup(self.temp.cleanup)
        self.addCleanup(setattr, report_p42b.v, 'OUT', self.previous)

    def test_R2_38_dataset_without_xml_cannot_pass(self):
        folder = self.report.v.OUT / 'run-1'
        folder.mkdir()
        (folder / 'apply.json').write_text('{}')
        with self.assertRaisesRegex(ValueError, 'no result XML'):
            self.report.aggregate('B-EDIT', 'run-*', {'apply.json': ['single']})

    def test_R2_38_empty_dataset_cannot_pass(self):
        with self.assertRaisesRegex(ValueError, 'exactly two complete datasets'):
            self.report.aggregate('B-EDIT', 'run-*', {'apply.json': ['single']})


if __name__ == '__main__':
    unittest.main()

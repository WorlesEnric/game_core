import importlib.util
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[5]
spec = importlib.util.spec_from_file_location('installer42e', ROOT / 'studio/etos/install-state.py')
installer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(installer)

class Tariff(unittest.TestCase):
    def test_R2_38_P42e_owner_describe_estimate(self):
        price, = installer.tariffs((ROOT / 'studio/etos/ops.toml.tmpl').read_text(), 'describe')
        self.assertEqual(price['source'], 'operator')
        self.assertEqual(price['per_unit'], 0.01)
        self.assertEqual(price['unit'], 'call')
        self.assertIn('2026-10-06', price['note'])
        self.assertIn('one small image plus a short answer', price['note'])
        self.assertIn('Echo resells OpenAI models', price['note'])

if __name__ == '__main__':
    unittest.main()

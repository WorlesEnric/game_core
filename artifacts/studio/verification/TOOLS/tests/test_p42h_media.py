import importlib.util
import json
from pathlib import Path
import sqlite3
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('p42h_ledger', Path(__file__).resolve().parents[1] / 'P42hMedia/ledger.py')
ledger = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ledger)


class MediaIdentity(unittest.TestCase):
    def test_R2_38_P42h_ImageWithoutOptionalJobIdRetainsAuthorityAcrossHistory(self):
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary)
            db = output / 'ledger.db'
            with sqlite3.connect(db) as connection:
                connection.execute('CREATE TABLE media_charges(key TEXT, charge TEXT, created_at INT)')
                connection.execute('CREATE TABLE artifacts(sha256 TEXT, bytes INT, media_type TEXT, producer TEXT, change_set_id TEXT)')
            before = ledger.snapshot(db, output, {'row': 'W-EDIT-03', 'label': 'before', 'generationCalls': 0})
            (output / 'ledger-before.json').write_text(json.dumps(before))
            charge = {'costUsd': 0.2, 'quantity': 1.0, 'tariff': {'unit': 'image'}}
            producer = {'op': 'generate.image', 'provider': 'echo-images', 'key': 'effect1', 'jobId': None,
                        'etosRef': 'ref_original', 'changeSetId': None, 'charge': charge}
            with sqlite3.connect(db) as connection:
                connection.execute('INSERT INTO media_charges VALUES (?, ?, ?)', ('effect1', json.dumps(charge), 1))
                connection.execute('INSERT INTO artifacts VALUES (?, ?, ?, ?, ?)', ('a' * 64, 100, 'image/png', json.dumps(producer), None))
            (output / 'generate.json').write_text(json.dumps({'providerSha256': 'a' * 64, 'maxCostUsd': 0.2, 'opState': {'usage': {'quantity': 1}}}))
            generated = ledger.snapshot(db, output, {'row': 'W-EDIT-03', 'label': 'generated', 'generationCalls': 1})
            self.assertEqual('PASS', generated['status'], generated.get('reason'))
            self.assertIsNone(generated['identity']['jobId'])
            self.assertEqual('ref_original', generated['identity']['etosRef'])
            (output / 'ledger-generated.json').write_text(json.dumps(generated))
            redone = ledger.snapshot(db, output, {'row': 'W-EDIT-03', 'label': 'redone', 'generationCalls': 1})
            self.assertEqual('PASS', redone['status'], redone.get('reason'))
            self.assertTrue(redone['usageUnchangedSinceGeneration'])
            with sqlite3.connect(db) as connection:
                connection.execute('INSERT INTO media_charges VALUES (?, ?, ?)', ('effect2', json.dumps(charge), 2))
            repeated = ledger.snapshot(db, output, {'row': 'W-EDIT-03', 'label': 'redone', 'generationCalls': 1})
            self.assertEqual('BLOCKED', repeated['status'])


if __name__ == '__main__':
    unittest.main()

import importlib.util
import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('verify_p42i', Path(__file__).resolve().parents[1] / 'verify.py')
verify = importlib.util.module_from_spec(spec)
spec.loader.exec_module(verify)


class ReportProvenance(unittest.TestCase):
    def test_R2_38_missing_historical_revision_does_not_abort_or_inherit_current_revision(self):
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary)
            attempt = output / 'W-REC-03' / 'historical'
            attempt.mkdir(parents=True)
            (attempt / 'result.json').write_text(json.dumps({'status': 'FAIL'}))
            with patch.object(verify, 'OUT', output):
                verify.summary()
            report = (output / 'SUMMARY.md').read_text()
            self.assertIn('| FAIL | unknown (receipt has no revision) |', report)


class PaidCaps(unittest.TestCase):
    def test_R2_38_narrative_reserves_all_three_worker_calls_before_launch(self):
        tools = Path(__file__).resolve().parents[1]
        with patch.object(sys, 'path', [str(tools), *sys.path]):
            spec = importlib.util.spec_from_file_location('live_p42i', tools / 'live-p42i.py')
            live = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(live)
        with tempfile.TemporaryDirectory() as temporary:
            state = Path(temporary)
            (state / 'ledger-before.json').write_text('{}')
            (state / 'paid-ledger.json').write_text('{"accountedUsd": 0.51}')
            with patch.object(live, 'STATE', state), patch.object(live.subprocess, 'run'):
                with self.assertRaisesRegex(RuntimeError, 'USD 2.00'):
                    live.reserve('narrative', {}, text_calls=3)
                self.assertFalse((state / 'reservations.json').exists())
                (state / 'paid-ledger.json').write_text('{"accountedUsd": 0.50}')
                live.reserve('narrative', {}, text_calls=3)
                with self.assertRaisesRegex(RuntimeError, 'already reserved'):
                    live.reserve('narrative', {}, text_calls=3)


class QueryIdentity(unittest.TestCase):
    def test_R2_38_real_R8_graph_identity_accepts_owned_rows_and_refuses_foreign_or_missing_nodes(self):
        tools = Path(__file__).resolve().parents[1]
        with patch.object(sys, 'path', [str(tools), *sys.path]):
            spec = importlib.util.spec_from_file_location('query_p42i', tools / 'query-p42i.py')
            query = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(query)
        workflow = tools.parent / 'W-ETOS-04/p42i-query-20261007T054157.439022Z/workflow'
        request = json.loads((workflow / 'request.json').read_text())
        receipt = json.loads((workflow / 'worker-query-receipt.json').read_text())
        trace = json.loads((workflow / 'worker-tool-trace.json').read_text())
        old = json.loads((workflow / 'result.json').read_text())
        project = '001e00fff8ea18ecbd08f8124d45629bd33b60dfe3a8051aecebd2deb3435627'
        self.assertNotEqual(old['graphId'], receipt['graphId'])
        self.assertEqual('PASS', query.verify_query(request, receipt, trace, project)['status'])
        for mutation in ('foreign', 'missing', 'text'):
            changed = copy.deepcopy(receipt)
            rows = json.loads(changed['stdout'])
            if mutation == 'foreign':
                rows['rows'][0]['owner'] = 'f' * 64
            elif mutation == 'missing':
                rows['rows'].pop()
            else:
                rows['rows'][0]['text'] = 'Fabricated dialogue'
            changed['stdout'] = json.dumps(rows)
            with self.assertRaises(ValueError):
                query.verify_query(request, changed, trace, project)


if __name__ == '__main__':
    unittest.main()

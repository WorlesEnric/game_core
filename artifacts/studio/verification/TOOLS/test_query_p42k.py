"""R2-38: actual worker execution, independent of local variable names/output punctuation."""
import copy
import importlib.util
import json
from pathlib import Path
import unittest

TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parents[3]
spec = importlib.util.spec_from_file_location('query_p42k', TOOLS / 'query-p42k.py')
query = importlib.util.module_from_spec(spec)
spec.loader.exec_module(query)


class QueryWitnessTests(unittest.TestCase):
    def test_R2_38_actual_query_witness_accepts_variable_and_punctuation_variation(self):
        # The retained current fixture is selected by its sole p42k request, never historical output.
        matches = list((ROOT / 'artifacts/studio/verification/W-ETOS-04').glob('p42k-query-*/workflow/worker-tool-trace.json'))
        self.assertEqual(len(matches), 1)
        folder = matches[0].parent
        request = json.loads((folder / 'request.json').read_text())
        receipt = json.loads((folder / 'worker-query-receipt.json').read_text())
        trace = json.loads(matches[0].read_text())
        activation = json.loads((ROOT / 'artifacts/studio/workflows/P4.2k/worker-activation.json').read_text())
        project = next(identity for identity, path in activation['projects'].items() if path.endswith('/games/hollowmere'))
        result = query.verify_query(request, receipt, trace, project)
        self.assertEqual(result['nodeCount'], 8)
        self.assertTrue(result['realWorkerQueryTrace'])
        execution = next(call for call in trace if 'subprocess.run(' in call['args'].get('command', ''))
        for mutation in ('failed', 'wrong-count', 'no-execution'):
            altered = copy.deepcopy(trace)
            call = altered[trace.index(execution)]
            if mutation == 'failed':
                call['outcome']['ok'] = False
            elif mutation == 'wrong-count':
                call['outcome']['text'] = call['outcome']['text'].replace('rows: 8', 'rows: 7')
            else:
                call['args']['command'] = 'print("receipt only")'
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                query.verify_query(request, receipt, altered, project)
        foreign = copy.deepcopy(receipt)
        foreign['graphId'] = 'foreign:graph'
        with self.assertRaises(ValueError):
            query.verify_query(request, foreign, trace, project)


if __name__ == '__main__':
    unittest.main()

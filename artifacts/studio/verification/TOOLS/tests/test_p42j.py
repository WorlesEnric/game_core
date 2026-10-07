import importlib.util
import json
import copy
import sys
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('verify_p42j', Path(__file__).resolve().parents[1] / 'verify.py')
verify = importlib.util.module_from_spec(spec)
spec.loader.exec_module(verify)


class ReportDisposition(unittest.TestCase):
    def test_R2_38_missing_historical_status_remains_unjudged_without_aborting_summary(self):
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary)
            attempt = output / 'W-MECH-01' / 'historical'
            attempt.mkdir(parents=True)
            receipt = json.dumps({'productRevision': 'a' * 40, 'signedStage': {'pass': True}})
            (attempt / 'result.json').write_text(receipt)
            with patch.object(verify, 'OUT', output):
                verify.summary()
            report = (output / 'SUMMARY.md').read_text()
            self.assertIn('| unknown (receipt has no status) | aaaaaaaaaaaa |', report)
            self.assertEqual(receipt, (attempt / 'result.json').read_text())


class QueryWitness(unittest.TestCase):
    def test_R2_38_composed_graph_identity_preserves_real_query_witness(self):
        tools = Path(__file__).resolve().parents[1]
        with patch.object(sys, 'path', [str(tools), *sys.path]):
            spec = importlib.util.spec_from_file_location('query_p42j', tools / 'query-p42j.py')
            query = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(query)
        folder = tools.parent / 'W-ETOS-04/p42j-query-20261007T130430.466361Z/workflow'
        request = json.loads((folder / 'request.json').read_text())
        receipt = json.loads((folder / 'worker-query-receipt.json').read_text())
        trace = json.loads((folder / 'worker-tool-trace.json').read_text())
        project = '844de33de4b73d1590557c7d75cc39976f4f4cf74537ad897c9c868169740936'
        self.assertEqual('PASS', query.verify_query(request, receipt, trace, project)['status'])
        foreign = copy.deepcopy(trace)
        owner = receipt['graphId'].split(':')[0]
        for call in foreign:
            if call['name'] == 'shell':
                call['args']['command'] = call['args']['command'].replace(owner, 'f' * 64)
        with self.assertRaisesRegex(ValueError, 'No independent actual worker query'):
            query.verify_query(request, receipt, foreign, project)
        with self.assertRaisesRegex(ValueError, 'No independent actual worker query'):
            query.verify_query(request, receipt, [], project)


class AudioAlignment(unittest.TestCase):
    def test_R2_38_audio_windows_stay_inside_current_region_markers(self):
        spec = importlib.util.spec_from_file_location('audio_p42j', Path(__file__).resolve().parents[1] / 'audio-p42j.py')
        audio = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(audio)
        frames = [{'time_s': '2.68', 'marker': 'boot'}, {'time_s': '17', 'marker': 'voice-maren'},
                  {'time_s': '96.75', 'marker': 'audio-village-before-marsh'},
                  {'time_s': '110', 'marker': 'audio-marsh-after-travel|region:Village->Marsh'}]
        phase = {'startedUtc': '2026-10-07T14:36:04+00:00', 'wallSeconds': 200, 'captureWallSeconds': 190.5}
        header = '2026-10-07T14:36:13+00:00'
        result = audio.aligned_windows(frames, phase, header)
        transition = 110 - result['estimatedFrameTimeAtCaptureStart']
        self.assertLess(result['beforeStart'] + result['windowDuration'], transition - 1)
        self.assertGreater(result['afterStart'], transition + 1)
        self.assertEqual(10, result['windowDuration'])
        frames[2]['time_s'] = '105'
        with self.assertRaisesRegex(ValueError, 'cannot contain'):
            audio.aligned_windows(frames, phase, header)

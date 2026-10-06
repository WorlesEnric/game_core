"""Paid caps and one-shot dispatch are acceptance safety boundaries."""
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
TOOLS=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(TOOLS))
spec=importlib.util.spec_from_file_location('p42e',TOOLS/'live-p42e.py')
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
class Caps(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.state=Path(self.temp.name)
        (self.state/'ledger-before.json').write_text('{"costUsd":0}')
        self.s=patch.object(m.live,'STATE',self.state);self.s.start()
        self.l=patch.object(m.live,'ledger',return_value={'costUsd':0});self.l.start()
    def tearDown(self):
        self.l.stop();self.s.stop();self.temp.cleanup()
    def test_R2_38_P42e_two_image_three_tts_two_describe_caps(self):
        m.reserve('one',{'image':2,'tts':3,'describe':2})
        for op in m.CAPS:
            with self.assertRaisesRegex(RuntimeError,'cap exceeded'):m.reserve(op,{op:1})
        self.assertEqual(len(json.loads((self.state/'reservations.json').read_text())),1)
    def test_R2_38_P42e_cost_reserved_before_dispatch(self):
        with patch.object(m.live,'ledger',return_value={'costUsd':2.9}):
            with self.assertRaisesRegex(RuntimeError,'USD 3'):m.reserve('image',{'image':1})
        self.assertFalse((self.state/'reservations.json').exists())
    def test_R2_38_P42e_no_paid_replay(self):
        m.reserve('voice2',{'tts':2})
        with self.assertRaisesRegex(RuntimeError,'already reserved'):m.reserve('voice2',{'tts':2})
    def test_R2_38_P42e_baseline_and_missing_ledger_fail_closed(self):
        with patch.object(sys,'argv',['x','baseline']):
            with self.assertRaisesRegex(RuntimeError,'cannot reset'):m.main()
        with patch.object(m.live,'ledger',side_effect=RuntimeError('unavailable')):
            with self.assertRaisesRegex(RuntimeError,'unavailable'):m.reserve('unknown',{})
        self.assertFalse((self.state/'reservations.json').exists())
if __name__=='__main__':unittest.main()

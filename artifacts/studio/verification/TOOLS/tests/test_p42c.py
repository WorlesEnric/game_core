"""Caps stop before a paid lane can execute; existing receipts cannot be silently replayed."""
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
TOOLS=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(TOOLS))
spec=importlib.util.spec_from_file_location('live_p42c',TOOLS/'live-p42c.py')
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)

class Caps(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory()
        self.state=Path(self.temp.name)
        self.state.joinpath('ledger-before.json').write_text(json.dumps({'costUsd':0}))
        self.p=patch.object(m,'STATE',self.state);self.p.start()
        self.l=patch.object(m,'ledger',return_value={'costUsd':0});self.l.start()
    def tearDown(self):
        self.l.stop();self.p.stop();self.temp.cleanup()
    def test_R2_38_P42c_reserves_all_paid_caps_before_launch(self):
        m.reserve('first',{'image':6,'tts':6,'describe':2})
        for op in ('image','tts','describe'):
            with self.assertRaisesRegex(RuntimeError,'cap reached'):m.reserve('more-'+op,{op:1})
        self.assertEqual(len(json.loads((self.state/'reservations.json').read_text())),1)
    def test_R2_38_P42c_ledger_ten_dollars_stops_without_reservation(self):
        with patch.object(m,'ledger',return_value={'costUsd':10}):
            with self.assertRaisesRegex(RuntimeError,'USD 10'):m.reserve('stop',{})
        self.assertFalse((self.state/'reservations.json').exists())
    def test_R2_38_P42c_no_automatic_paid_replay(self):
        m.reserve('robe2',{'image':3,'tts':2})
        with self.assertRaisesRegex(RuntimeError,'already reserved'):m.reserve('robe2',{})
    def test_R2_38_P42c_missing_ledger_refuses(self):
        with patch.object(m,'ledger',side_effect=RuntimeError('unavailable')):
            with self.assertRaisesRegex(RuntimeError,'unavailable'):m.reserve('unknown',{})
        self.assertFalse((self.state/'reservations.json').exists())

if __name__=='__main__':unittest.main()

#!/usr/bin/env python3
"""Packet owner disk floor must stop execution before creating an evidence directory."""
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import verify


class DiskReserveTests(unittest.TestCase):
    def test_R2_38_P42l_explicit_floor_allows_work_above_28_GiB(self):
        with tempfile.TemporaryDirectory() as directory:
            with patch.dict(os.environ, {'GC_STUDIO_DISK_RESERVE_GIB': '28'}), \
                    patch.object(verify, 'OUT', Path(directory)), \
                    patch.object(verify.shutil, 'disk_usage') as usage:
                usage.return_value.free = 30 * 1024**3
                record = verify.run('TEST', 'allowed-disk', [sys.executable, '-c', 'print("child-executed")'])
                self.assertEqual(record['status'], 'PASS')
                self.assertIn('child-executed', (verify.ROOT / record['evidencePath'] / 'command.log').read_text())

    def test_R2_38_P42l_owner_floor_refuses_before_child_launch(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / 'uncreated'
            with patch.dict(os.environ, {'GC_STUDIO_DISK_RESERVE_GIB': '28'}), \
                    patch.object(verify, 'OUT', output), \
                    patch.object(verify.shutil, 'disk_usage') as usage, \
                    patch.object(verify.subprocess, 'Popen') as launch:
                usage.return_value.free = 28 * 1024**3 - 1
                with self.assertRaises(RuntimeError):
                    verify.run('TEST', 'disk', ['must-not-launch'])
                launch.assert_not_called()
                self.assertFalse(output.exists())

    def test_R2_38_P42l_owner_floor_cannot_be_lowered(self):
        with patch.dict(os.environ, {'GC_STUDIO_DISK_RESERVE_GIB': '27'}), \
                patch.object(verify.subprocess, 'Popen') as launch:
            with self.assertRaises(RuntimeError):
                verify.run('TEST', 'disk', ['must-not-launch'])
            launch.assert_not_called()


if __name__ == '__main__':
    unittest.main()

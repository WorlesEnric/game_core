import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
from collections import namedtuple

ROOT = Path(__file__).resolve().parents[4]
spec = importlib.util.spec_from_file_location('verify', ROOT / 'artifacts/studio/verification/TOOLS/verify.py')
verify = importlib.util.module_from_spec(spec)
spec.loader.exec_module(verify)


class EvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.old = verify.OUT
        verify.OUT = Path(self.temp.name)

    def tearDown(self):
        verify.OUT = self.old
        self.temp.cleanup()

    def invoke(self, text):
        return verify.run('TEST', 'xml', [sys.executable, '-c',
            'from pathlib import Path; import sys; Path(sys.argv[1]).write_text(sys.argv[2])',
            '{out}/results.xml', text], results='results.xml')

    def test_R2_37_skipped_xml_never_passes(self):
        r = self.invoke('<test-run><test-case result="Passed"/><test-case result="Skipped"/></test-run>')
        self.assertEqual('BLOCKED', r['status'])

    def test_R2_37_failure_wins_over_skip(self):
        r = self.invoke('<test-run><test-case result="Failed"/><test-case result="Skipped"/></test-run>')
        self.assertEqual('FAIL', r['status'])

    def test_R2_37_suite_setup_failure_never_passes(self):
        r = self.invoke('<test-run result="Failed"><test-case result="Passed"/></test-run>')
        self.assertEqual('FAIL', r['status'])

    def test_R2_38_malformed_xml_is_retained_as_failure(self):
        r = self.invoke('<test-run')
        self.assertEqual('FAIL', r['status'])
        self.assertIn('Malformed', r['note'])

    def test_R2_38_empty_xml_never_passes(self):
        self.assertEqual('BLOCKED', self.invoke('<test-run/>')['status'])

    def test_R2_38_repeat_keeps_both_attempts(self):
        self.invoke('<test-run><test-case result="Failed"/></test-run>')
        self.invoke('<test-run><test-case result="Passed"/></test-run>')
        rows = [json.loads(p.read_text()) for p in verify.OUT.glob('TEST/*/result.json')]
        self.assertEqual(['FAIL', 'PASS'], sorted(r['status'] for r in rows))

    def test_R2_38_enospc_empty_record_does_not_abort_summary(self):
        folder = verify.OUT / 'ROW' / 'interrupted'
        folder.mkdir(parents=True)
        (folder / 'result.json').write_text('')
        verify.summary()
        self.assertIn('BLOCKED (incomplete ENOSPC record retained)', (verify.OUT / 'SUMMARY.md').read_text())
        self.assertEqual('', (folder / 'result.json').read_text())

    def test_R2_38_disk_reserve_stops_before_child_launch(self):
        usage = namedtuple('usage', 'total used free')(100, 99, 1)
        with patch.object(verify.shutil, 'disk_usage', return_value=usage), patch.object(verify.subprocess, 'Popen') as spawn:
            with self.assertRaisesRegex(RuntimeError, '40 GiB'):
                verify.run('ROW', 'low-disk', ['true'])
            spawn.assert_not_called()

    def test_P42_secret_and_home_redaction(self):
        for prefix in ('et' + 'k_', 'et' + 't_', 'et' + 'p_', 'et' + 'a_', 'sk' + '-', 'Bearer' + ' '):
            self.assertNotIn('sentinel', verify.scrub(prefix + 'sentinel'))
        self.assertNotIn('/home/', verify.scrub('/home/fixture/project'))

    def test_P42_HOST_01_local_linux_never_uses_ssh(self):
        # Fake executable fails before Unity launch: this is a transport regression, not UI evidence.
        bindir = verify.OUT / 'bin'
        bindir.mkdir()
        ssh = bindir / 'ssh'
        ssh.write_text('#!/bin/sh\necho UNEXPECTED_SSH >&2\nexit 99\n')
        ssh.chmod(0o755)
        env = dict(os.environ, PATH=str(bindir) + ':' + os.environ['PATH'],
                   UNITY='/nonexistent/p42-editor', EVIDENCE_DEST=str(verify.OUT / 'captures'))
        for name in ('evidence-p2.1.sh', 'evidence-p2.3.sh'):
            p = subprocess.run(['bash', str(ROOT / 'studio/tools' / name), 'p4.2', 'games/hollowmere'],
                               env=env, capture_output=True, text=True)
            self.assertNotIn('UNEXPECTED_SSH', p.stdout + p.stderr)
            self.assertIn('no Unity', p.stdout + p.stderr)
            self.assertNotEqual(0, p.returncode)


if __name__ == '__main__':
    unittest.main()

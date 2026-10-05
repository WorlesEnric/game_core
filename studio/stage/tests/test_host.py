import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

STAGE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(STAGE))
from redact import redact


class HostTests(unittest.TestCase):
    def test_R2_19_RedactorAllForms(self):
        for prefix in ('etk_', 'ett_', 'etp_', 'eta_', 'Bearer ', 'sk-'):
            self.assertNotIn('sentinel', redact(prefix + 'sentinel'))
        value = json.loads(redact('{"apiKEY":["sentinel"],"nested":{"secret":{"x":"sentinel"}},"token":42}'))
        self.assertNotIn('sentinel', str(value))
        self.assertEqual('[REDACTED]', value['token'])

    def test_R2_37_O48_XmlDispositions(self):
        with tempfile.TemporaryDirectory() as directory:
            xml = Path(directory) / 'tests.xml'
            for results, code in [(['Passed'], 0), (['Passed', 'Skipped'], 2),
                                  (['Passed', 'Inconclusive'], 2), (['Failed'], 1), ([], 2)]:
                xml.write_text('<test-run>' + ''.join(f'<test-case result="{v}" />' for v in results) + '</test-run>')
                proc = subprocess.run([sys.executable, str(STAGE / 'test-results.py'), str(xml)], capture_output=True)
                self.assertEqual(code, proc.returncode)

    def test_R2_37_RequiredCaseAndSuiteDisposition(self):
        with tempfile.TemporaryDirectory() as directory:
            xml = Path(directory) / 'tests.xml'
            xml.write_text('<test-run result="Passed"><test-case fullname="A" result="Passed" /></test-run>')
            command = [sys.executable, str(STAGE / 'test-results.py'), str(xml)]
            self.assertEqual(0, subprocess.run(command + ['A'], capture_output=True).returncode)
            self.assertEqual(2, subprocess.run(command + ['B'], capture_output=True).returncode)
            xml.write_text('<test-run result="Inconclusive"><test-case fullname="A" result="Passed" /></test-run>')
            self.assertEqual(2, subprocess.run(command, capture_output=True).returncode)

    def test_R2_18_EnvironmentIsEnumerated(self):
        import os
        with tempfile.TemporaryDirectory() as directory:
            log = Path(directory) / 'log'
            env = dict(os.environ, DOTNET_STARTUP_HOOKS='sentinel', UNITY_ATTACK='sentinel', GAMECORE_TEST='sentinel')
            child = 'import os,json; print(json.dumps({k:os.environ.get(k) for k in ["DOTNET_STARTUP_HOOKS","UNITY_ATTACK","GAMECORE_TEST"]}))'
            proc = subprocess.run([sys.executable, str(STAGE / 'run-redacted.py'), '--log', str(log), '--timeout', '10', '--silence', '0', '--', sys.executable, '-c', child], env=env)
            self.assertEqual(0, proc.returncode)
            self.assertNotIn('sentinel', log.read_text())

    def test_O52_CacheKeyTracksVersions(self):
        with tempfile.TemporaryDirectory() as directory:
            project = Path(directory)
            (project / 'ProjectSettings').mkdir()
            (project / 'Packages/kernel').mkdir(parents=True)
            (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 6000.0.75f1')
            (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.gamecore.contracts":"file:kernel"}}')
            meta = project / 'Packages/kernel/package.json'
            def key():
                return subprocess.check_output(['bash', str(STAGE / 'cache-key.sh'), str(project)])
            meta.write_text('{"name":"com.gamecore.contracts","version":"1.0.0"}')
            first = key()
            self.assertEqual(first, key())
            meta.write_text('{"name":"com.gamecore.contracts","version":"1.1.0"}')
            second = key()
            self.assertNotEqual(first, second)
            (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 6000.0.76f1')
            self.assertNotEqual(second, key())

    def test_R2_13_LegacyRequestRefused(self):
        proc = subprocess.run(['bash', str(STAGE / 'stage.sh'), 'old', '/unused'], capture_output=True)
        self.assertEqual(2, proc.returncode)
        self.assertEqual('legacy_stage_request', json.loads(proc.stdout)['reason'])

    def test_R2_19_PartialAndMultilineLogs(self):
        with tempfile.TemporaryDirectory() as directory:
            log = Path(directory) / 'log'
            code = 'import sys,time; sys.stdout.write(\'etk_\'); sys.stdout.flush(); time.sleep(.1); print(\'sentinel\'); print(\'{\\n"secret":\\n{"x":"sentinel"}\\n}\')'
            proc = subprocess.run([sys.executable, str(STAGE / 'run-redacted.py'), '--log', str(log), '--timeout', '10', '--silence', '0', '--', sys.executable, '-c', code])
            self.assertEqual(0, proc.returncode)
            self.assertNotIn('sentinel', log.read_text())
            self.assertIn('[REDACTED]', log.read_text())


if __name__ == '__main__': unittest.main()

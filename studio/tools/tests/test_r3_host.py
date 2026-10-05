"""Offline R3 regressions; fake Editors still use the shared host allocator."""
import json
import os
from pathlib import Path
import subprocess
import sys
import shutil

import pytest

ROOT = Path(__file__).resolve().parents[3]
TOOLS = ROOT / 'studio/tools'
RUNS = ROOT / 'artifacts/studio/workflows/P3.2/runs'


def test_D25_environment_prefix_excludes_credentials(tmp_path):
    names = ['GAMECORE_ETOS_LIVE', 'GAMECORE_CUSTOM_GATE', 'GAMECORE_ETOS_KEY_FILE',
             'GAMECORE_API_TOKEN', 'GAMECORE_PASSWORD', 'GAMECORE_HTTP_PROXY',
             'HTTP_PROXY', 'HTTPS_PROXY', 'ALL_PROXY', 'DOTNET_STARTUP_HOOKS']
    env = dict(os.environ, **dict.fromkeys(names, 'fixture'))
    env['GAMECORE_ETOS_LIVE'] = '1'
    log = tmp_path / 'log'
    child = 'import os,json; print(json.dumps([os.environ.get(n) for n in ' + repr(names) + ']))'
    result = subprocess.run([sys.executable, str(ROOT / 'studio/stage/run-redacted.py'),
                             '--log', str(log), '--timeout', '10', '--silence', '0',
                             '--', sys.executable, '-c', child], env=env)
    assert result.returncode == 0
    assert json.loads(log.read_text()) == ['1', 'fixture'] + [None] * (len(names) - 2)


@pytest.fixture
def runner(tmp_path):
    # Policy unit tests never start Unity or compete with real host reservations.
    # unity-batch-lock.sh separately exercises the production allocator.
    isolated = tmp_path / 'studio/tools'
    isolated.mkdir(parents=True)
    stage = tmp_path / 'studio/stage'
    stage.mkdir()
    for name in ('unity-batch.sh', 'unity-diagnostics.py'):
        if (TOOLS / name).exists():
            shutil.copy(TOOLS / name, isolated / name)
    for name in ('run-redacted.py', 'redact.py', 'test-results.py'):
        shutil.copy(ROOT / 'studio/stage' / name, stage / name)
    (isolated / 'unity-slot.sh').write_text('unity_slot_acquire() { :; }\nunity_slot_release() { :; }\n')
    project = tmp_path / 'project'
    (project / 'ProjectSettings').mkdir(parents=True)
    (project / 'Packages').mkdir()
    (project / 'Packages/manifest.json').write_text('{}')
    unity = tmp_path / 'Unity'
    unity.write_text('''#!/usr/bin/env python3
import json, pathlib, sys
root = pathlib.Path(__file__).parent
counter = root / 'attempts'
n = int(counter.read_text()) + 1 if counter.exists() else 1
counter.write_text(str(n))
mode = sys.argv[-1]
if mode in ('ilpp', 'ilpp-always') and (n == 1 or mode == 'ilpp-always'):
    print((root / 'ilpp.txt').read_text())
    sys.exit(0)  # The retained fault can leave the Editor alive/success-shaped.
if mode == 'timeout': sys.exit(124)
if mode == 'failure': sys.exit(1)
if mode == 'bee':
    bee = root / 'project/Library/Bee/tundra.log.json'
    bee.parent.mkdir(parents=True, exist_ok=True)
    bee.write_text(json.dumps({'msg': 'Assets/Broken.cs(4,2): error CS0246: Missing type etk_fixture'}))
    sys.exit(1)
if mode == 'stale-bee':
    import os
    bee = root / 'project/Library/Bee/tundra.log.json'
    bee.parent.mkdir(parents=True, exist_ok=True)
    bee.write_text(json.dumps({'msg': 'error CS0246: previous invocation'}))
    os.utime(bee, (1, 1))
if '-testResults' in sys.argv:
    path = pathlib.Path(sys.argv[sys.argv.index('-testResults')+1])
    path.write_text((root / 'results-input.xml').read_text())
''')
    unity.chmod(0o755)
    (tmp_path / 'ilpp.txt').write_text((RUNS / 'mech-b-20261005T120918Z/editor-excerpt.log').read_text())
    env = dict(os.environ, UNITY=str(unity))

    def run(mode, xml=None, options=()):
        args = ['bash', str(isolated / 'unity-batch.sh'), '--project', str(project),
                '--log-dir', str(tmp_path / 'logs'), '--label', mode, *options]
        if xml:
            (tmp_path / 'results-input.xml').write_text(xml)
            args += ['--results', str(tmp_path / 'results.xml')]
        return subprocess.run(args + ['--', mode], env=env, text=True, capture_output=True, timeout=120)
    return run


def test_D25_retained_skips_name_gate(runner):
    xml = (RUNS / 'harness-20261005T124954Z/results.xml').read_text()
    result = runner('skips', xml)
    assert result.returncode == 1
    assert 'RESULT skips: SKIPPED (env-gated)' in result.stdout
    assert 'GAMECORE_ETOS_LIVE' in result.stdout


def test_D25_failures_take_precedence_over_skips(runner):
    result = runner('mixed', '<test-run result="Failed"><test-case result="Failed" />'
                    '<test-case result="Skipped"><reason><message>GAMECORE_ETOS_LIVE=1</message>'
                    '</reason></test-case></test-run>')
    assert result.returncode == 1
    assert 'RESULT mixed: FAIL' in result.stdout


def test_D17_bee_compile_errors_are_redacted_and_visible(runner):
    result = runner('bee')
    assert result.returncode == 1
    assert 'error CS0246' in result.stdout
    assert 'etk_fixture' not in result.stdout
    assert 'RESULT bee: FAIL' in result.stdout


def test_D17_stale_bee_errors_do_not_fail_current_compile(runner):
    result = runner('stale-bee')
    assert result.returncode == 0
    assert 'error CS0246' not in result.stdout


def test_D18_retained_ilpp_retries_once(runner):
    result = runner('ilpp')
    assert result.returncode == 0
    assert 'retrying once' in result.stdout and 'ilpp.sock-' in result.stdout
    assert 'attempts 2' in result.stdout


def test_D18_persistent_ilpp_never_passes(runner):
    result = runner('ilpp-always')
    assert result.returncode == 1
    assert 'RESULT ilpp-always: FAIL' in result.stdout
    assert 'attempts 2' in result.stdout


@pytest.mark.parametrize('mode,code', [('timeout', 124), ('failure', 1)])
def test_D18_other_failures_never_retry(runner, mode, code):
    result = runner(mode)
    assert result.returncode == code
    assert 'attempts 1' in result.stdout


def test_D18_retry_can_be_disabled(runner):
    result = runner('ilpp', options=['--attempts', '1'])
    assert result.returncode == 1
    assert 'attempts 1' in result.stdout


def test_D17_empty_arrays_are_bash3_safe():
    source = (TOOLS / 'unity-compile.sh').read_text()
    assert '${required_tests[@]+"${required_tests[@]}"}' in source
    assert '${result_args[@]+"${result_args[@]}"}' in source


@pytest.mark.parametrize('required', [[], ['--require-test', 'A case with spaces']])
def test_D17_ssh_forwarding_preserves_optional_arrays(tmp_path, required):
    ssh = tmp_path / 'ssh'
    ssh.write_text('#!/bin/sh\ncat >/dev/null\nprintf "%s\\n" "$@"\n')
    ssh.chmod(0o755)
    env = dict(os.environ, PATH=str(tmp_path) + ':' + os.environ['PATH'],
               UNITY='/nonexistent/editor', GC_STUDIO_ON_HOST='0')
    shell = os.environ.get('R3_BASH32', 'bash')
    result = subprocess.run([shell, str(TOOLS / 'unity-compile.sh'), 'r3-e', 'games/hollowmere',
                             '--tests', 'EditMode', *required], env=env, text=True, capture_output=True)
    assert result.returncode == 0, result.stderr
    assert '--tests EditMode' in result.stdout
    if required:
        assert r'--require-test A\ case\ with\ spaces' in result.stdout
    else:
        assert '--require-test' not in result.stdout

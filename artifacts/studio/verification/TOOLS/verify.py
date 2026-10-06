#!/usr/bin/env python3
"""Retain every attempt; XML/TRX dispositions, never stdout, determine suite acceptance."""
import argparse
import collections
import contextlib
import datetime
import fcntl
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shlex
import shutil
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[4]
OUT = ROOT / 'artifacts/studio/verification'
HOME = str(Path.home())
RESULTS = []
MEMORY_OBSERVED = False


def scrub(value):
    value = value.replace(HOME, '~')
    value = re.sub(r'/(?:home|Users)/[^/\s<>"\']+', '~', value)
    value = re.sub(r'\b(?:et[kpta]_[A-Za-z0-9_.~+/=-]+|sk-[A-Za-z0-9_-]+)', '[REDACTED]', value)
    return re.sub(r'\bBearer\s+[^\s"\',;<>]+', '[AUTH REDACTED]', value, flags=re.I)


def utc():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT, text=True).strip()


@contextlib.contextmanager
def editor_lease():
    directory = Path.home() / '.cache/gamecore-studio/verification'
    directory.mkdir(parents=True, exist_ok=True)
    identity = hashlib.sha256(str(ROOT).encode()).hexdigest()[:16]
    with (directory / (identity + '.editor.lock')).open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        try:
            yield
        finally:
            fcntl.flock(lock, fcntl.LOCK_UN)


def xml_counts(path):
    root = ET.parse(path).getroot()
    cases = [x for x in root.iter() if x.tag.split('}')[-1] in ('test-case', 'UnitTestResult')]
    counts = collections.Counter(x.get('result', x.get('outcome', 'Unknown')) for x in cases)
    bad_suites = [x.get('fullname', x.get('name', x.tag)) for x in root.iter()
                  if x.tag in ('test-run', 'test-suite') and x.get('result') in ('Failed', 'Inconclusive')]
    return {'total': len(cases), 'counts': dict(counts), 'failedSuites': bad_suites,
            'nonpassing': [{'name': x.get('fullname', x.get('testName', 'unnamed')),
                            'result': x.get('result', x.get('outcome', 'Unknown')),
                            'detail': scrub(' '.join(x.itertext()).strip())}
                           for x in cases if x.get('result', x.get('outcome')) != 'Passed']}


def finish(folder, record):
    # Unity writes XML/logs itself. Scrub text paths after exit; preserve original XML outcomes.
    for path in folder.rglob('*'):
        if path.is_file() and (path.suffix in ('.xml', '.trx', '.log', '.txt', '.json', '.jsonl', '.csv', '.md') or re.search(r'\.(?:log|json|xml|trx)\.run[0-9]+$', path.name)):
            data = path.read_text(errors='replace')
            path.write_text(('\n'.join(line.rstrip() for line in scrub(data).splitlines()).rstrip() + '\n') if data else '')
    (folder / 'result.json').write_text(scrub(json.dumps(record, indent=2)) + '\n')
    (folder / 'README.md').write_text(scrub(
        f"# {record['label']}\n\nVerdict: **{record['status']}**." + (' ' + record['note'] if record.get('note') else '') + '\n\n' +
        f"Source revision: `{record['revision']}`; host: `{record['host']}`.\n"
        f"Started: {record['started']}; ended: {record['ended']}; duration: {record['seconds']:.3f} s.\n\n"
        f"Command (from repository root unless cwd specified):\n\n```sh\n{record['command']}\n```\n\n"
        "Text evidence redacts credentials and substitutes `~` for absolute home paths. "
        "XML dispositions are unchanged; trailing log whitespace is normalized. Hashes describe these retained sanitized bytes.\n"))
    paths = sorted(p for p in folder.rglob('*') if p.is_file() and p.name != 'SHA256SUMS')
    (folder / 'SHA256SUMS').write_text(''.join(
        f'{hashlib.sha256(p.read_bytes()).hexdigest()}  {p.relative_to(folder)}\n' for p in paths))


def run(row, label, command, cwd=ROOT, results=None, env=None, timeout=None, expected_http=None, editor=False):
    free = shutil.disk_usage(ROOT).free
    if free < 40 * 1024**3:
        raise RuntimeError('Disk reserve below 40 GiB; stop before starting another workload.')
    stamp = datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S.%fZ')
    folder = OUT / row / f'{label}-{stamp}'
    folder.mkdir(parents=True)
    command = [str(x).replace('{out}', str(folder)) for x in command]
    start = time.monotonic()
    record = dict(label=label, revision=git('rev-parse', 'HEAD'), host=platform.node(),
                  started=utc(), command=shlex.join(command), cwd=str(cwd), evidencePath=os.path.relpath(folder, ROOT))
    child_env = dict(os.environ, PYTHONDONTWRITEBYTECODE='1', DOTNET_CLI_TELEMETRY_OPTOUT='1',
                     DOTNET_NOLOGO='1', GC_STUDIO_UNITY_SLOTS=os.environ.get('GC_STUDIO_UNITY_SLOTS', '3'), PROBE_RUNS='2', GAMECORE_ETOS_LIVE='0')
    child_env.update({k: str(v).replace('{out}', str(folder)) for k, v in (env or {}).items()})
    print(f"START {row}/{label}", flush=True)
    with (folder / 'command.log').open('w') as log, (editor_lease() if editor else contextlib.nullcontext()):
        record['queuedAtRevision'] = record['revision']
        record['revision'] = git('rev-parse', 'HEAD')
        record['executionStarted'] = utc()
        record['sourceDiffSha256'] = hashlib.sha256(subprocess.check_output(['git', 'diff', '--', 'Packages', 'games', 'studio/agent/src', 'studio/stage'], cwd=ROOT)).hexdigest()
        try:
            proc = subprocess.Popen(command, cwd=cwd, env=child_env, stdout=subprocess.PIPE,
                                    stderr=subprocess.STDOUT, text=True, errors='replace')
            for line in proc.stdout:
                log.write(scrub(line)); log.flush()
            rc = proc.wait(timeout=timeout)
        except OSError as exc:
            log.write(scrub(str(exc)) + '\n'); rc = 127
    record.update(exitCode=rc, status='PASS' if rc == 0 else 'FAIL', ended=utc(), seconds=time.monotonic()-start)
    if expected_http is not None:
        observed = re.findall(r'^HTTP (\d+)$', (folder / 'command.log').read_text(), re.M)
        record['httpStatus'] = int(observed[-1]) if observed else None
        record['status'] = 'PASS' if rc == 0 and record['httpStatus'] == expected_http else 'FAIL'
        record['note'] = 'Unauthenticated proxy probe: expected HTTP ' + str(expected_http) + '. This exercises the node boundary, not an independently authenticated companion app-header check.'
    if results:
        paths = list(folder.glob(results))
        record['suites'] = {}
        malformed = []
        for p in paths:
            try:
                record['suites'][str(p.relative_to(folder))] = xml_counts(p)
            except (ET.ParseError, OSError) as exc:
                malformed.append(f'{p.name}: {exc}')
        if not paths:
            record.update(status='FAIL', note='No result XML/TRX produced; no test pass claimed.')
        elif malformed:
            record.update(status='FAIL', note='Malformed XML/TRX: ' + '; '.join(malformed))
        elif any(s['nonpassing'] or s['failedSuites'] or not s['total'] for s in record['suites'].values()):
            has_failure = any(s['counts'].get('Failed') or s['failedSuites'] for s in record['suites'].values())
            record.update(status='FAIL' if has_failure else 'BLOCKED', note='See XML dispositions and nonpassing case details in result.json.')
    finish(folder, record)
    RESULTS.append(record)
    print(f"END {row}/{label}: {record['status']} ({record['seconds']:.1f}s)", flush=True)
    return record


def host_test_python():
    configured = os.environ.get('P42_TEST_PYTHON')
    if configured:
        return configured
    env = Path.home() / '.cache/gamecore-studio/p42-python'
    executable = env / 'bin/python'
    if not executable.exists():
        run('STATIC', 'python-test-environment', ['python3', '-m', 'venv', str(env)])
    ready = subprocess.run([str(executable), '-c', 'import pytest, jsonschema'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    if ready.returncode:
        run('STATIC', 'python-test-dependencies', [str(executable), '-m', 'pip', '--disable-pip-version-check', '-q', 'install', 'pytest==9.1.1', 'jsonschema==4.26.0'])
    return str(executable)


def static():
    run('W-TOOL-02', 'metadata', ['python3', 'tools/check_package_metadata.py'])
    run('STATIC', 'csharp', ['python3', 'tools/check_game_core_csharp.py'])
    run('STATIC', 'schemas', ['python3', 'tools/studio/emit_studio_schemas.py', '--check'])
    run('STATIC', 'schema-mirror', ['diff', '-rq', 'docs/studio/schemas', 'studio/agent/schemas'])
    for label, args in [('cargo-fmt', ['fmt', '--check']), ('cargo-clippy', ['clippy', '--all-targets', '--', '-D', 'warnings']),
                        ('cargo-test', ['test'])]:
        run('STATIC', label, [str(Path.home() / '.cargo/bin/cargo'), *args], cwd=ROOT / 'studio/agent')
    run('STATIC', 'dotnet', ['dotnet', 'test', 'dotnet/GameCore.sln', '--logger', 'trx',
                           '--results-directory', '{out}/trx'], results='trx/*.trx')
    for label, path in [('stage-python', 'studio/stage/tests'), ('installer-python', 'studio/etos/tests')]:
        run('STATIC', label, ['python3', '-m', 'unittest', 'discover', '-s', path, '-v'])
    run('STATIC', 'host-python', [host_test_python(), '-m', 'pytest', '-q', 'studio/tools/tests/test_r3_host.py', 'studio/etos/workers/tests'])
    run('STATIC', 'stage-slot', ['python3', 'tools/check_stage_slot.py', '--self-test'])
    run('STATIC', 'p42-runner-regressions', ['python3', '-m', 'unittest', 'discover', '-s', 'studio/tools/Tests/P4_2', '-v'])


def unity_run(game, mode, label=None, test_filter='.*'):
    global MEMORY_OBSERVED
    memory_report = ROOT / 'artifacts/studio/evidence/P3.1/memory-cycles.json'
    old_memory = memory_report.read_bytes() if memory_report.exists() else None
    result = run('UNITY-' + game.upper(), label or mode.lower(), ['bash', 'studio/tools/unity-batch.sh',
        '--project', ROOT / 'games' / game, '--log-dir', '{out}/logs', '--label', label or mode.lower(),
        '--results', '{out}/results.xml', '--', '-runTests', '-testPlatform', mode, '-testFilter', test_filter],
        results='results.xml', env={'GAMECORE_ETOS_AUTOSTART': '0'}, editor=True)
    if game == 'hollowmere' and mode == 'EditMode' and memory_report.exists() and memory_report.read_bytes() != old_memory:
        data = json.loads(memory_report.read_text())
        folder = OUT / 'W-GAME-08' / ('memory-' + datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S.%fZ'))
        folder.mkdir(parents=True)
        (folder / 'memory-cycles.json').write_text(json.dumps(data, indent=2) + '\n')
        growth = data['allocatedGrowthPctCycle10']
        finish(folder, dict(result, label='B-MEMORY-15-percent', status='FAIL' if growth > 15 else 'BLOCKED',
                            note=f'Allocated growth {growth}%; budget 15%. Full Memory Profiler snapshots are absent.'))
        MEMORY_OBSERVED = True
        if old_memory is None: memory_report.unlink()
        else: memory_report.write_bytes(old_memory)
    return result


def unity():
    for game in ('hollowmere', 'cleanproof'):
        for mode in ('EditMode', 'PlayMode'):
            unity_run(game, mode)


def bake():
    for game, method, extra in (
        ('hollowmere', 'Hollowmere.Authoring.HollowmereAuthoring.AuthorAllBatch', ['-p31MediaMaxCalls', '0', '-p31Report', '{out}/author-report.json']),
        ('cleanproof', 'Saltmarsh.Authoring.SaltmarshAuthoring.AuthorAll', ['-quit'])):
        run('W-PLUG-12', 'bake-' + game, ['bash', 'studio/tools/unity-batch.sh', '--project', ROOT / 'games' / game,
            '--log-dir', '{out}/logs', '--label', 'bake-' + game, '--', '-executeMethod', method, *extra],
            env={'GAMECORE_ETOS_AUTOSTART': '0'}, editor=True)


def clean():
    for mode in ('EditMode', 'PlayMode'):
        unity_run('cleanproof', mode, 'clean-recheck-' + mode.lower())
    run('W-CLEAN-02', 'package-diff', ['git', 'diff', '--exit-code', 'origin/main', '--', 'Packages/'])


def perf():
    # Bounded probes already assert gameplay invariants and print their observed timings.
    # They do not qualify graphical B-FRAME, native-memory release, or a full p95 campaign.
    for n in (1, 2):
        unity_run('hollowmere', 'PlayMode', 'perf-probe-' + str(n),
                  'Hollowmere\\.P1_1\\..*|Hollowmere\\.P1_3\\..*|Hollowmere\\.P1_7a\\..*')
    if MEMORY_OBSERVED:
        print('Memory series already retained; not repeating ten cycles.', flush=True)
        return
    report = ROOT / 'artifacts/studio/evidence/P3.1/memory-cycles.json'
    previous = report.read_bytes() if report.exists() else None
    try:
        result = unity_run('hollowmere', 'EditMode', 'memory-ten-cycles',
                           'Hollowmere.P3_1.EditMode.Tests.P31PlayModeHooksTests.TenPlayEditCycles')
        if report.exists() and report.read_bytes() != previous:
            data = json.loads(report.read_text())
            folder = OUT / 'W-GAME-08' / ('memory-' + datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%SZ'))
            folder.mkdir(parents=True)
            (folder / 'memory-cycles.json').write_text(json.dumps(data, indent=2) + '\n')
            # Existing test asserts 25%; acceptance never relaxes 07's 15% budget.
            growth = data['allocatedGrowthPctCycle10']
            record = dict(result, label='B-MEMORY-15-percent', status='FAIL' if growth > 15 else 'BLOCKED',
                          note=f'Allocated growth {growth}%; budget 15%. Existing test uses 25%; its pass alone is insufficient. Full Memory Profiler snapshots are absent.')
            finish(folder, record)
    finally:
        if previous is not None: report.write_bytes(previous)
        elif report.exists(): report.unlink()


def recovery():
    for mode in ('rollback', 'resume'):
        stamp = datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S.%fZ')
        state = OUT / 'W-REC-01' / (mode + '-state-' + stamp)
        state.mkdir(parents=True)
        common = ['bash', 'studio/tools/unity-batch.sh', '--project', ROOT / 'games/hollowmere',
                  '--log-dir', '{out}/logs']
        crash = run('W-REC-01', mode + '-killed-editor', [*common, '--label', 'recovery-crash', '--',
            '-executeMethod', 'Hollowmere.P4_2.ProcessRecovery.Crash', '-p42State', state,
            '-p42Recovery', mode], env={'GAMECORE_ETOS_AUTOSTART': '0'}, editor=True)
        if not (state / 'crash.json').exists():
            print('No crash marker: recovery not claimed.', flush=True)
            continue
        crash['expectedTerminationObserved'] = True
        finish(ROOT / crash['evidencePath'], crash)
        recovered = run('W-REC-01', mode + '-reopened-editor', [*common, '--label', 'recovery-reopen', '--',
            '-executeMethod', 'Hollowmere.P4_2.ProcessRecovery.Recover', '-p42State', state],
            env={'GAMECORE_ETOS_AUTOSTART': '0'}, editor=True)
        passed = False
        if (state / 'recovery-result.json').exists():
            result = json.loads((state / 'recovery-result.json').read_text())
            passed = recovered['status'] == 'PASS' and result['ok'] and result['originalPid'] != result['recoveryPid']
        record = dict(recovered, label='R2-03-R2-38-' + mode,
                      status='PASS' if passed else 'FAIL',
                      note='A real SIGKILL at the engine fault hook; the killed-editor attempt is an expected nonzero exit, retained separately. Recovery is checked in a different Editor process.')
        finish(state, record)
        RESULTS.append(record)


def graphical_tests(test_filter=None, row='GRAPHICAL', label='graphics-required-tests'):
    # Interactive Editor is necessary for the tests skipped by -nographics. Same allocator as unity-batch.
    display = os.environ.get('EVIDENCE_DISPLAY', ':1')
    wrapper = '''set -euo pipefail
pgrep -af 'Unity|ffmpeg|xvfb' || true
while pgrep -x Unity >/dev/null || pgrep -x ffmpeg >/dev/null; do
  echo 'Waiting for other Editors/recorders before graphical qualification'; sleep 5
done
unity_tools_dir="$PWD/studio/tools"
source "$unity_tools_dir/unity-slot.sh"
unity_slot_acquire
trap unity_slot_release EXIT
python3 studio/stage/run-redacted.py --log "$1/editor.log" --timeout 1500 --silence 600 -- env GAMECORE_ETOS_KEY_FILE=/nonexistent/p42-offline \
  "$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity" -projectPath "$PWD/games/hollowmere" \
  -logFile - -runTests -testPlatform EditMode -testFilter "$2" -testResults "$1/results.xml"
'''
    test_filter = test_filter or ('R2_29_KeyDownUpOnPromptAndControlsNeverEnterViewportHandlers|'
                   'D12_DeferredRelayoutSurvivesWindowManagerPlacementAndRunsOnce|'
                   'Render_TargetMatchesTheViewportArea|PreviewScreenCapturesARenderTextureOrSkipsHeadless')
    return run(row, label, ['bash', '-c', wrapper, 'p42-graphical', '{out}', test_filter],
        results='results.xml', env={'DISPLAY': display, 'GAMECORE_ETOS_AUTOSTART': '0', 'GAMECORE_P42_EVIDENCE': '{out}'}, editor=True)


def graphical_views():
    run('W-VIEW-01', 'views-capture', ['bash', 'studio/tools/evidence-p2.3.sh', ROOT.name, 'games/hollowmere'],
        env={'EVIDENCE_DEST': str(OUT / 'W-VIEW-01' / ('captures-' + datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%SZ'))),
             'GAMECORE_ETOS_AUTOSTART': '0'}, editor=True)


def security_scan():
    prefixes = [b'et' + s for s in (b'k_', b't_', b'p_', b'a_')] + [b'sk' + b'-', b'Bearer' + b' ']
    token = re.compile(b'|'.join(re.escape(p) for p in prefixes))
    home = re.compile(rb'/(?:home|Users)/[^/\s<>"\']+')
    tracked = subprocess.check_output(['git', 'ls-files', '-z'], cwd=ROOT).decode().split('\0')
    paths = set(ROOT / p for p in tracked if p)
    paths.update(p for p in (ROOT / 'artifacts').rglob('*') if p.is_file())
    for game in ('hollowmere', 'cleanproof'):
        library = ROOT / 'games' / game / 'Library'
        paths.update(p for p in library.rglob('*') if p.is_file() and p.suffix.lower() in ('.json', '.log', '.txt', '.xml', '.yaml', '.asset', '.cs'))
    matches = []
    excluded = []
    for path in sorted(paths):
        relative = str(path.relative_to(ROOT))
        if path.suffix == '.key' or path.name in ('providers.env', 'auth.json', 'GameCoreStudio.json') or path.is_symlink():
            excluded.append(relative); continue
        if not path.is_file(): continue
        counts = collections.Counter()
        previous = b''
        with path.open('rb') as stream:
            while chunk := stream.read(1024 * 1024):
                data = previous + chunk
                for m in token.finditer(data):
                    if m.end() > len(previous): counts['prefix'] += 1
                for m in home.finditer(data):
                    if m.end() > len(previous): counts['absoluteHome'] += 1
                previous = data[-256:]
        if counts: matches.append({'file': relative, **counts})
    # Settings are inspected only by this boolean detector; no values or content are emitted.
    settings = []
    for game in ('hollowmere', 'cleanproof'):
        path = ROOT / 'games' / game / 'UserSettings/GameCoreStudio.json'
        record = {'project': game, 'exists': path.exists()}
        if path.exists():
            data = path.read_bytes()
            record['credentialPrefixPresent'] = bool(token.search(data))
            try:
                obj = json.loads(data)
                record['unexpectedCredentialFields'] = [k for k in obj if re.search(r'password|secret|token|^key$|api.?key', k, re.I) and obj[k]]
            except ValueError:
                record['invalidJson'] = True
        settings.append(record)
    print(json.dumps({'scope': 'tracked repository, all artifacts and both Library text caches; raw matches are NEVER printed; prefix matches require review (test fixtures and prose are not credentials)',
                      'filesScanned': len(paths), 'matches': matches, 'excludedSensitivePaths': excluded,
                      'settings': settings}, indent=2))
    return int(any(s.get('credentialPrefixPresent') or s.get('unexpectedCredentialFields') or s.get('invalidJson') for s in settings))


def security():
    run('W-ETOS-01', 'security-scan', ['python3', __file__, 'security-scan'])
    for label, extra in [('proxy-missing-app', []), ('proxy-wrong-token', ['-H', 'X-Etos-App: gamecore-unity', '-H', 'X-Etos-Proxy-Token: invalid-p42-probe'])]:
        run('W-ETOS-01', label, ['curl', '--silent', '--show-error', '--noproxy', '*', '--max-time', '10',
             '--write-out', '\nHTTP %{http_code}\n', *extra, 'http://127.0.0.1:7410/api/v1/agents/gamecore-studio/http/v1/hello'], expected_http=401)


def summary():
    lines = ['# P4.2 + P4.2b + P4.2c verification summary', '', 'Matrix rows are accepted only by their row README; suite passes alone do not close workflows.', '',
             '| Evidence | Verdict | Revision |', '|---|---|---|']
    row_file = OUT / 'ROWS.json'
    if row_file.exists():
        rows = json.loads(row_file.read_text())
        header = ['# P4.2 + P4.2b + P4.2c verification summary', '', 'Matrix row totals: ' + ', '.join(f'{k} {n}' for k, n in rows['counts'].items()) + '.', '', '| Row | Verdict | Evidence / exact limitation |', '|---|---|---|']
        for r in rows['rows']:
            header.append(f"| {r['row']} | {r['status']} | [Evidence]({r['row']}/README.md): {r['note']} |")
        lines = header + ['', '## Retained attempts (including superseded and expected failures)', ''] + lines[4:]
    for p in sorted(OUT.glob('*/*/result.json')):
        try:
            r = json.loads(p.read_text())
        except (ValueError, OSError):
            lines.append(f'| {p.parent.relative_to(OUT)} | BLOCKED (incomplete ENOSPC record retained) | unknown |')
            continue
        lines.append(f"| [{p.parent.relative_to(OUT)}]({p.parent.relative_to(OUT)}/README.md) | {r['status']} | {r['revision'][:12]} |")
    (OUT / 'SUMMARY.md').write_text('\n'.join(lines) + '\n')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', nargs='?', choices=('all', 'static', 'bake', 'unity', 'recovery', 'graphical-tests', 'graphical-views', 'perf', 'clean', 'security', 'security-scan', 'summary'), default='all')
    args = parser.parse_args()
    if platform.system() != 'Linux':
        parser.error('Qualification must run on the Linux build host.')
    if os.environ.get('PROBE_RUNS', '2') != '2':
        parser.error('Only PROBE_RUNS=2 is supported; no full performance campaign.')
    if args.mode == 'security-scan': return security_scan()
    if args.mode in ('all', 'static'): static()
    if args.mode in ('all', 'bake'): bake()
    if args.mode in ('all', 'unity'): unity()
    if args.mode == 'recovery': recovery()
    if args.mode == 'graphical-tests': graphical_tests()
    if args.mode == 'graphical-views': graphical_views()
    if args.mode in ('all', 'perf'): perf()
    if args.mode == 'all': graphical_tests('R2_38_W_UI_04_W_GAME_08_TenCyclesWithSnapshotsAndOnePump', 'W-GAME-08', 'native-memory-and-pumps')
    if args.mode in ('all', 'clean'): clean()
    if args.mode in ('all', 'security'): security()
    summary()
    return int(any(r['status'] != 'PASS' and not r.get('expectedTerminationObserved') for r in RESULTS))


if __name__ == '__main__':
    sys.exit(main())

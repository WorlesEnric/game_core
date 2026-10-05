#!/usr/bin/env python3
"""Route legacy V1 Unity invocations through the mandatory shared batch runner."""
from pathlib import Path
import os
import shutil
import subprocess
import sys
from verify import editor_lease

ROOT = Path(__file__).resolve().parents[4]


def main():
    args = sys.argv[1:]
    named = {}
    extra = []
    i = 0
    while i < len(args):
        name = args[i].lower()
        if name in ('-projectpath', '-logfile', '-testresults'):
            if i + 1 == len(args): raise SystemExit('Missing value for ' + args[i])
            named[name] = args[i + 1]
            i += 2
        elif name in ('-batchmode', '-nographics'):
            i += 1
        else:
            extra.append(args[i]); i += 1
    if '-projectpath' not in named or '-logfile' not in named:
        raise SystemExit('Gate adapter requires explicit projectPath and logFile.')
    log = Path(named['-logfile']).resolve()
    logs = log.parent / (log.stem + '-attempts')
    command = ['bash', str(ROOT / 'studio/tools/unity-batch.sh'), '--project', named['-projectpath'],
               '--log-dir', str(logs), '--label', 'v1-gate', '--timeout', os.environ.get('UNITY_TIMEOUT', '1500')]
    if '-testresults' in named: command += ['--results', named['-testresults']]
    command += ['--', *extra]
    env = dict(os.environ, GC_STUDIO_UNITY_SLOTS=os.environ.get('GC_STUDIO_UNITY_SLOTS', '3'))
    # The outer gate's UNITY names this adapter; the shared wrapper must receive the real binary.
    env['UNITY'] = str(Path.home() / 'Unity/Hub/Editor/6000.0.75f1/Editor/Unity')
    with editor_lease():
        result = subprocess.run(command, env=env)
    attempts = sorted(logs.glob('*.log'), key=lambda p: p.stat().st_mtime)
    if attempts: shutil.copyfile(attempts[-1], log)
    # Suppress the legacy gate's generic timeout retry; unity-batch owns its bounded ILPP-only retry.
    return 1 if result.returncode in (124, 137) else result.returncode


if __name__ == '__main__':
    sys.exit(main())

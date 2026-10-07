#!/usr/bin/env python3
"""Main-owned serial launches; no installation/service/provider operations are implicit."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[4]
PROJECT = ROOT / 'games/hollowmere'
METHODS = {
    'move': 'JoinedMove',
    'query': 'SelectedNpcQuery',
    'cancel': 'TrayCancel',
    'reload': 'SourceReload',
    'stage-probe': 'StageCancelProbe',
}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('lane', choices=['install', *METHODS])
    parser.add_argument('--out', type=Path)
    parser.add_argument('--stage-job', help='Optional owned job identity for the bodyless GET unsupported-route probe')
    args = parser.parse_args()
    destination = PROJECT / 'Assets/Hollowmere/Tests/P42hTasks'
    if args.lane == 'install':
        if (PROJECT / 'Temp/UnityLockfile').exists():
            raise SystemExit('Stop the owned Editor/build before installing task harness sources.')
        destination.mkdir(parents=True, exist_ok=True)
        for name in ('P42h.Tasks.asmdef', 'TaskDriver.cs', 'CompilePulse.cs'):
            shutil.copy2(HERE / name, destination / name)
        print(destination)
        return 0
    if args.out is None:
        parser.error('--out is required for a lane')
    if not (destination / 'TaskDriver.cs').is_file():
        raise SystemExit('Install the harness after the current build exits: launch.py install')
    output = args.out.resolve()
    output.mkdir(parents=True, exist_ok=True)
    if (output / 'started.json').exists() or (output / 'launch.json').exists():
        raise SystemExit('Fresh --out required; no automatic worker replay')
    settings = (PROJECT / 'ProjectSettings/ProjectSettings.asset').read_text()
    guid = re.search(r'productGUID: ([a-fA-F0-9]{32})', settings)
    if guid is None:
        raise SystemExit('No project productGUID; cannot establish paired project identity')
    project_id = hashlib.sha256((guid[1].lower() + '\n' + str(PROJECT)).encode()).hexdigest()
    environment = dict(os.environ, DISPLAY=':1', GAMECORE_ETOS_LIVE='1', GAMECORE_ETOS_AUTOSTART='1',
                       GAMECORE_ETOS_PROJECT_ID=project_id, GAMECORE_P42H_OUT=str(output),
                       UNITY=str(HERE / 'unity-graphical.py'))
    if args.stage_job:
        environment['GAMECORE_P42H_STAGE_JOB'] = args.stage_job
    command = ['bash', str(ROOT / 'studio/tools/unity-batch.sh'), '--project', str(PROJECT),
               '--log-dir', str(output / 'logs'), '--label', 'p42h-tasks-' + args.lane,
               '--timeout', '900', '--attempts', '1', '--', '-executeMethod',
               'P42h.Tasks.TaskDriver.' + METHODS[args.lane], '-saveDir', str(output / 'saves')]
    (output / 'launch.json').write_text(json.dumps({'command': command, 'project': str(PROJECT),
        'projectId': project_id, 'display': ':1', 'lane': args.lane,
        'note': 'Main must reserve text budget and enforce global ledger/host gates before invoking. No media generation is requested.'}, indent=2) + '\n')
    result = subprocess.run(command, cwd=ROOT, env=environment, check=False)
    (output / 'process-result.json').write_text(json.dumps({'exitCode': result.returncode,
        'resultPresent': (output / 'result.json').is_file()}, indent=2) + '\n')
    if args.lane == 'reload' and not (PROJECT / 'Temp/UnityLockfile').exists():
        # Restore only our installed compilation witness after the actual process exited.
        shutil.copy2(HERE / 'CompilePulse.cs', destination / 'CompilePulse.cs')
    if not (output / 'result.json').is_file():
        raise SystemExit('No result.json: classify FAIL/NotRun from retained process/log evidence, never PASS')
    print((output / 'result.json').read_text())
    return result.returncode


if __name__ == '__main__':
    raise SystemExit(main())

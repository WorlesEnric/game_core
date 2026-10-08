#!/usr/bin/env python3
"""P4.2l serial live execution and immutable spend reservations."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import sqlite3
import subprocess
import sys
import verify as v

ROOT = v.ROOT
TOOLS = Path(__file__).resolve().parent
STATE = ROOT / 'artifacts/studio/workflows/P4.2l'
STATE.mkdir(parents=True, exist_ok=True)
CAPS = {'image': 2, 'tts': 2, 'describe': 1}

def project_id():
    project = ROOT / 'games/hollowmere'
    guid = re.search(r'productGUID: ([a-fA-F0-9]{32})', (project / 'ProjectSettings/ProjectSettings.asset').read_text())[1]
    return hashlib.sha256((guid.lower() + '\n' + str(project)).encode()).hexdigest()

def ledger():
    db = Path.home() / '.local/share/etos-studio/agents/gamecore-studio/state/ledger.db'
    with sqlite3.connect('file:' + str(db) + '?mode=ro', uri=True) as connection:
        charges = [{'id': key, **json.loads(charge)} for key, charge in connection.execute('SELECT key,charge FROM media_charges ORDER BY key')]
    return {'utc': v.utc(), 'charges': charges, 'costUsd': sum(c['costUsd'] for c in charges)}

def reserve(name, counts, text_calls=1):
    path = STATE / 'reservations.json'
    rows = json.loads(path.read_text()) if path.exists() else []
    if any(row['name'] == name for row in rows):
        raise RuntimeError('Lane already reserved; no automatic paid replay')
    json.loads((STATE / 'ledger-before.json').read_text())
    subprocess.run([sys.executable, str(TOOLS / 'retain-ledger-p42l.py')], check=True, stdout=subprocess.DEVNULL)
    spent = json.loads((STATE / 'paid-ledger.json').read_text())['accountedUsd']
    ceiling = counts.get('image', 0) * 0.20 + counts.get('tts', 0) * 0.05 + counts.get('describe', 0) * 0.01
    ceiling += text_calls * 0.50
    if spent + ceiling > 2.00:
        raise RuntimeError('Next operation ceiling would exceed packet USD 2.00 cap')
    for op, cap in CAPS.items():
        if sum(row['counts'].get(op, 0) for row in rows) + counts.get(op, 0) > cap:
            raise RuntimeError(op + ' cap exceeded')
    rows.append({'name': name, 'counts': counts, 'textCalls': text_calls, 'reservedUsd': ceiling, 'utc': v.utc()})
    path.write_text(json.dumps(rows, indent=2) + '\n')

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('lane')
    parser.add_argument('--method')
    parser.add_argument('--row')
    parser.add_argument('--filter')
    parser.add_argument('--workflow-out', type=Path)
    parser.add_argument('--workflow')
    parser.add_argument('--candidate', type=Path)
    parser.add_argument('--input', type=Path)
    parser.add_argument('--negative', action='store_true')
    parser.add_argument('--offline', action='store_true')
    args = parser.parse_args()
    if args.lane == 'baseline':
        with (STATE / 'ledger-before.json').open('x') as stream:
            stream.write(json.dumps(ledger(), indent=2) + '\n')
        return
    environment = {'GAMECORE_ETOS_LIVE': '1', 'GAMECORE_ETOS_PROJECT_ID': project_id(),
        'GAMECORE_ETOS_KEY_FILE': str(Path.home() / '.config/gamecore-studio/app-key.json'),
        'GAMECORE_P42_EVIDENCE': '{out}', 'GAMECORE_P42H_OUT': '{out}/workflow'}
    if args.workflow_out:
        environment['GAMECORE_P42H_OUT'] = str(args.workflow_out.resolve())
    if args.lane == 'hello':
        result = v.run('P4.2l', 'p42l-hello', ['dotnet', 'test', str(TOOLS / 'P42eReceipt/P42eReceipt.csproj'),
            '--filter', 'FullyQualifiedName~R2_38_P42e_HelloOwnerDescribeAnd3dRefusal',
            '--logger', 'trx', '--results-directory', '{out}/trx'], env=environment, results='trx/*.trx')
    else:
        if not args.row or not (args.method or args.filter):
            parser.error('Unity lanes need --row and --method or --filter')
        counts = {'image': 1} if args.lane in ('portrait', 'robe') else {'tts': 2} if args.workflow == 'voice2' else {}
        if not args.offline and not args.lane.startswith('stage-'):
            text_calls = 0 if counts.get('image') else 3 if args.workflow == 'narrative' else 2 if args.workflow == 'p42f-npc' else 1
            reserve(args.lane, counts, text_calls=text_calls)
        environment.update(DISPLAY=':1', GAMECORE_ETOS_AUTOSTART='0' if args.offline else '1',
            GC_STUDIO_UNITY_SLOTS='1', UNITY=str(TOOLS / 'unity-interactive-p42f.py'))
        if args.workflow:
            environment.update(GAMECORE_P42C_WORKFLOW=args.workflow, GAMECORE_P42C_OUT='{out}/workflow')
        if args.candidate:
            environment['GAMECORE_P42C_CANDIDATE'] = str(args.candidate.resolve())
        if args.input:
            environment['GAMECORE_P42C_INPUT'] = str(args.input.resolve())
        if args.negative:
            environment['GAMECORE_P42C_NEGATIVE'] = '1'
        command = ['bash', str(ROOT / 'studio/tools/unity-batch.sh'), '--project', str(ROOT / 'games/hollowmere'),
            '--log-dir', '{out}/logs', '--label', 'p42l-' + args.lane, '--timeout', '1800', '--attempts', '1']
        if args.filter:
            environment['UNITY'] = str(Path.home() / 'Unity/Hub/Editor/6000.0.75f1/Editor/Unity')
            command += ['--results', '{out}/results.xml', '--', '-runTests', '-testPlatform', 'EditMode', '-testFilter', args.filter]
        else:
            command += ['--', '-executeMethod', args.method, '-saveDir', '{out}/saves']
        if args.workflow == 'voice2':
            command = ['bash', str(TOOLS / 'voice-p42e.sh'), '{out}/workflow', *command]
        result = v.run(args.row, 'p42l-' + args.lane, command, env=environment,
            results='results.xml' if args.filter else None, editor=True)
    (STATE / ('ledger-after-' + args.lane + '.json')).write_text(json.dumps(ledger(), indent=2) + '\n')
    raise SystemExit(result['status'] != 'PASS')

if __name__ == '__main__':
    main()

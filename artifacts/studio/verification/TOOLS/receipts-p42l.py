#!/usr/bin/env python3
"""Current installed-service receipts; only the named permitted operation is reachable."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import verify as v

TOOLS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('live_i', TOOLS / 'live-p42l.py')
live = importlib.util.module_from_spec(spec)
spec.loader.exec_module(live)
parser = argparse.ArgumentParser()
parser.add_argument('lane', choices=('tamper', 'describe', 'companion-restart', 'cancelled-stage', 'discard-terminal-slots'))
parser.add_argument('--artifact')
parser.add_argument('--job')
args = parser.parse_args()
env = {'GAMECORE_ETOS_LIVE': '1', 'GAMECORE_ETOS_PROJECT_ID': live.project_id(),
       'GAMECORE_ETOS_KEY_FILE': str(Path.home() / '.config/gamecore-studio/app-key.json'),
       'GAMECORE_P42_EVIDENCE': '{out}', 'GC_ETOS_EVIDENCE_DIR': '{out}/live',
       'ETOS_ROOT': str(Path.home() / '.local/share/etos-studio')}
if args.lane == 'companion-restart':
    live.reserve(args.lane, {}, text_calls=1)
    project = TOOLS / 'P42cReconnect/P42cReconnect.csproj'
    name = 'R2_38_W_ETOS_06_CompanionRestartResumesCursorAndKeepsTask'
    row = 'W-ETOS-06'
elif args.lane == 'cancelled-stage':
    if not args.job:
        parser.error('owned current-run cancelled stage job is required')
    env['GAMECORE_P42E_STAGE_JOB'] = args.job
    project = TOOLS / 'P42lReceipt/P42lReceipt.csproj'
    name = 'R2_38_P42i_CancelledStageRemainsUnauthorizableAfterCompanionRestart'
    row = 'W-REC-03'
elif args.lane == 'discard-terminal-slots':
    requests = [json.loads(path.read_text()) for row_id in ('W-MECH-01', 'W-REC-03', 'W-DOC-02')
                for path in (v.OUT / row_id).glob('p42l-stage-submit-*/stage-request.json')]
    literal = v.OUT / 'W-DOC-02/p42l-lever-literal/config.json'
    if literal.exists():
        requests.append(json.loads(literal.read_text()))
    if any(request['request']['projectId'] != live.project_id() for request in requests):
        raise RuntimeError('Refusing foreign project staging cleanup')
    retained = live.STATE / 'terminal-stage-requests.json'
    retained.write_text(json.dumps(requests, indent=2) + '\n')
    env['GAMECORE_P42I_DISCARD_REQUESTS'] = str(retained)
    project = TOOLS / 'P42lReceipt/P42lReceipt.csproj'
    name = 'R2_38_P42i_DiscardTerminalOwnedSlotsPreservesDurableJobs'
    row = 'P4.2l'
else:
    if not args.artifact:
        parser.error('owned current-run artifact SHA256 is required')
    env['GAMECORE_P42I_ARTIFACT'] = args.artifact
    project = TOOLS / 'P42lReceipt/P42lReceipt.csproj'
    row = 'W-ETOS-07'
    name = 'R2_38_CurrentGeneratedTextureTamperRefusedWithoutRegeneration'
    if args.lane == 'describe':
        live.reserve(args.lane, {'describe': 1}, text_calls=0)
        env['GAMECORE_P42I_DESCRIBE_RESERVED'] = '1'
        name = 'R2_38_DescribeCurrentOwnedTextureUnderBoundTariff'
result = v.run(row, 'p42l-' + args.lane, ['dotnet', 'test', project, '--filter', 'FullyQualifiedName~' + name,
    '--logger', 'trx', '--results-directory', '{out}/trx'], results='trx/*.trx', env=env)
subprocess.run([sys.executable, str(TOOLS / 'retain-ledger-p42l.py')], check=True)
raise SystemExit(result['status'] != 'PASS')

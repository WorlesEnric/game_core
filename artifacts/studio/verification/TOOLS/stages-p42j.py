#!/usr/bin/env python3
"""Serial installed-service mechanism, cancellation and lever qualification."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import verify as v

TOOLS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('rows', TOOLS / 'rows-p42j.py')
rows = importlib.util.module_from_spec(spec)
spec.loader.exec_module(rows)
STATE = v.ROOT / 'artifacts/studio/workflows/P4.2j'


def invoke(args, env=None):
    return subprocess.run([sys.executable, *map(str, args)], cwd=v.ROOT, env=dict(os.environ, **(env or {}))).returncode


def submit(name, candidate, row):
    result = rows.unity(row, 'p42j-stage-submit-' + name, 'P42g.Live.StageUi.Submit', graphical=True,
        env={'GAMECORE_ETOS_AUTOSTART': '1', 'GAMECORE_ETOS_LIVE': '1', 'GAMECORE_P42C_CANDIDATE': str(candidate), 'GAMECORE_P42C_STAGE_SUBMIT': '1'})
    request = v.ROOT / result['evidencePath'] / 'stage-request.json'
    if not request.exists():
        raise RuntimeError('Stage submit produced no request: ' + result['evidencePath'])
    return request


def receipt(request, row):
    binding = json.loads(request.read_text())
    return v.run(row, 'p42j-signed-record', ['dotnet', 'test', TOOLS / 'P42jReceipt/P42jReceipt.csproj',
        '--filter', 'FullyQualifiedName~R2_09_13_P42i', '--logger', 'trx', '--results-directory', '{out}/trx'],
        results='trx/*.trx', env={'GAMECORE_ETOS_PROJECT_ID': binding['request']['projectId'],
        'GAMECORE_ETOS_KEY_FILE': str(Path.home() / '.config/gamecore-studio/app-key.json'),
        'GAMECORE_P42E_STAGE_JOB': binding['jobId'], 'GAMECORE_P42_EVIDENCE': '{out}'})


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('lane', choices=('mechanism', 'cancel', 'negative'))
    parser.add_argument('--attempt', default='initial')
    parser.add_argument('--mechanism-run', choices=('cold', 'warm'))
    args = parser.parse_args()
    lane = args.lane
    if not args.attempt.replace('-', '').isalnum():
        parser.error('attempt must be an alphanumeric label')
    root = v.ROOT / '.evidence/p42j-stage' / args.attempt
    root.mkdir(parents=True, exist_ok=True)
    if lane == 'mechanism':
        for name in ((args.mechanism_run,) if args.mechanism_run else ('cold', 'warm')):
            candidate = root / name
            if invoke([TOOLS / 'instantiate-p42d.py', v.ROOT / 'samples/mechanisms/pressure-plate/candidate', candidate]):
                raise RuntimeError('Candidate instantiation failed')
            request = submit(name, candidate, 'W-MECH-01')
            if invoke([TOOLS / 'watch-stage-p42c.py', request]):
                raise RuntimeError('Stage terminal state unproven; no creator Editor launch')
            receipt(request, 'W-MECH-01')
            reviewed = rows.unity('W-MECH-01', 'p42j-stage-review-' + name, 'P42g.Live.StageUi.Review', graphical=True,
                env={'GAMECORE_ETOS_AUTOSTART': '1', 'GAMECORE_ETOS_LIVE': '1',
                     'GAMECORE_P42C_CANDIDATE': str(candidate), 'GAMECORE_P42C_INPUT': str(request)})
            if reviewed['status'] != 'PASS':
                raise RuntimeError('Admission/undo failed; preserve recovery before another stage')
    elif lane == 'negative':
        candidate = root / 'negative'
        source = root / 'negative-source'
        if invoke([TOOLS / 'make-negative-candidate.py', source]) or invoke([TOOLS / 'instantiate-p42d.py', source, candidate]):
            raise RuntimeError('Negative candidate construction failed')
        request = submit('negative', candidate, 'W-MECH-01')
        if invoke([TOOLS / 'watch-stage-p42c.py', request]):
            raise RuntimeError('Stage terminal state unproven; no creator Editor launch')
        result = rows.unity('W-MECH-01', 'p42j-stage-negative-review', 'P42g.Live.StageUi.Review', graphical=True,
            env={'GAMECORE_ETOS_AUTOSTART': '1', 'GAMECORE_ETOS_LIVE': '1', 'GAMECORE_P42C_NEGATIVE': '1',
                 'GAMECORE_P42C_REVIEW_ONLY': '1', 'GAMECORE_P42C_CANDIDATE': str(candidate), 'GAMECORE_P42C_INPUT': str(request)})
        panel = json.loads((v.ROOT / result['evidencePath'] / 'panel-verdict.json').read_text())
        if panel['canAdmit'] or panel['verified']:
            result.update(status='FAIL', note='Negative package unexpectedly received admission authority; review-only prevented admission.')
            v.finish(v.ROOT / result['evidencePath'], result)
            raise RuntimeError('Negative candidate must never become admissible')
    else:
        candidate = root / 'cancel'
        if invoke([TOOLS / 'instantiate-p42d.py', v.ROOT / 'samples/mechanisms/pressure-plate/candidate', candidate]):
            raise RuntimeError('Cancellation candidate instantiation failed')
        output = v.OUT / 'W-REC-03/p42j-installed-cancellation'
        output.mkdir(parents=True, exist_ok=False)
        activation = json.loads((STATE / 'worker-activation.json').read_text())
        project_id = next(identity for identity, path in activation['projects'].items() if path.endswith('/games/hollowmere'))
        config = {'projectId': project_id, 'candidate': str(candidate), 'evidence': str(output), 'phase': 'region'}
        path = root / 'cancel-config.json'
        path.write_text(json.dumps(config, indent=2) + '\n')
        rows.unity('W-REC-03', 'p42j-region-cancel', 'P42i.Cancellation.Acceptance.Run', graphical=True,
            env={'GAMECORE_P42I_CANCEL_CONFIG': str(path)})
        request = submit('cancel', candidate, 'W-REC-03')
        if invoke([TOOLS / 'cancel-stage-p42j.py', 'pause', '--request', request, '--out', output]):
            raise RuntimeError('Stage pause prerequisite failed; preserve actual stage and request')
        config['phase'] = 'cancel'
        path.write_text(json.dumps(config, indent=2) + '\n')
        rows.unity('W-REC-03', 'p42j-stage-cancel', 'P42i.Cancellation.Acceptance.Run', graphical=True,
            env={'GAMECORE_ETOS_AUTOSTART': '1', 'GAMECORE_ETOS_LIVE': '1', 'GAMECORE_P42I_CANCEL_CONFIG': str(path)})
        if invoke([TOOLS / 'cancel-stage-p42j.py', 'verify', '--request', request, '--out', output]):
            raise RuntimeError('Stage cancellation resource assertions failed')
    v.summary()


if __name__ == '__main__':
    main()

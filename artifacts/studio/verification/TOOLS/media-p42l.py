#!/usr/bin/env python3
"""Use an existing image reservation only after proving the first attempt never submitted."""
import argparse
import importlib.util
import json
from pathlib import Path
import verify as v

TOOLS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('rows', TOOLS / 'rows-p42l.py')
rows = importlib.util.module_from_spec(spec)
spec.loader.exec_module(rows)
parser = argparse.ArgumentParser()
parser.add_argument('mode', choices=('portrait', 'robe'))
args = parser.parse_args()
state = v.ROOT / 'artifacts/studio/workflows/P4.2l'
row = 'W-EDIT-03' if args.mode == 'portrait' else 'W-AI-01'
originals = [v.OUT / row / ('p42l-' + args.mode) / 'workflow/result.json']
if len(originals) != 1:
    raise SystemExit('Exactly one original unstarted attempt is required')
original = json.loads(originals[0].read_text())
if original['generationCalls'] != 0 or original['phase'] != 1 or original['importChangeSetId'] or original['mediaIdentity']:
    raise SystemExit('Original attempt may have generated or imported; no replay permitted')
reservations = json.loads((state / 'reservations.json').read_text())
reservation = [item for item in reservations if item['name'] == args.mode]
if len(reservation) != 1 or reservation[0]['counts'] != {'image': 1}:
    raise SystemExit('Original unused image reservation is required')
output = v.OUT / row / ('p42l-' + args.mode + '-baseline-restored') / 'workflow'
if (output / 'checkpoint.json').exists() or (output / 'result.json').exists():
    raise SystemExit('Qualified media attempt already started; cannot replay')
output.mkdir(parents=True, exist_ok=True)
(output.parent / 'reservation-handoff.json').write_text(json.dumps({
    'reportedAt': v.utc(), 'original': str(originals[0].relative_to(v.ROOT)),
    'originalGenerationCalls': 0, 'reservation': reservation[0],
    'reason': 'Original scenario stopped before generation because an earlier offline test left a second placed Maren. Exact initial authored preimages restored between scenarios; consume the same unused reservation with the read-only observer.'}, indent=2) + '\n')
result = rows.unity(row, 'p42l-' + args.mode + '-observed',
    'P42h.Media.Driver.RunPortrait' if args.mode == 'portrait' else 'P42h.Media.Driver.RunRobe', graphical=True,
    env={'GAMECORE_ETOS_AUTOSTART': '1', 'GAMECORE_ETOS_LIVE': '1', 'GAMECORE_P42H_OUT': str(output)})
raise SystemExit(result['status'] != 'PASS')

#!/usr/bin/env python3
"""Run the unchanged R8 lever creator walkthrough against this installed stage receipt."""
import argparse
import json
from pathlib import Path
import verify as v
import importlib.util

spec = importlib.util.spec_from_file_location('rows', Path(__file__).with_name('rows-p42i.py'))
rows = importlib.util.module_from_spec(spec)
spec.loader.exec_module(rows)
parser = argparse.ArgumentParser()
parser.add_argument('--request', type=Path, required=True)
parser.add_argument('--candidate', type=Path, required=True)
parser.add_argument('--out', type=Path, required=True)
args = parser.parse_args()
request = json.loads(args.request.read_text())
args.out.mkdir(parents=True, exist_ok=False)
config = {'candidate': str(args.candidate.resolve()), 'evidence': str(args.out.resolve()),
          'nodeUrl': 'http://127.0.0.1:7410', 'request': request['request'], 'jobId': request['jobId'],
          'installedNodeUsed': True, 'productRevision': v.git('rev-parse', 'HEAD')}
path = args.out / 'config.json'
path.write_text(json.dumps(config, indent=2) + '\n')
result = rows.unity('W-DOC-02', 'p42i-lever-review', 'Hollowmere.R8_B.LeverWalkthrough.Run',
    args=['-gcR8CConfig', path.resolve(), '-force-glcore'], graphical=True,
    env={'GAMECORE_ETOS_AUTOSTART': '1', 'GAMECORE_ETOS_LIVE': '1'})
raise SystemExit(result['status'] != 'PASS')

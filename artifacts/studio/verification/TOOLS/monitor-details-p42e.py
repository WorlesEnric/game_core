#!/usr/bin/env python3
"""Retain safe process classification prospectively; never rewrite earlier unknown PIDs."""
import json
from pathlib import Path
import time
repo=Path(__file__).resolve().parents[4]
root=repo/'artifacts/studio/workflows/P4.2e'
samples=[]
while not (root/'monitor-stop').exists():
    rows=[]
    for p in Path('/proc').iterdir():
        if not p.name.isdigit(): continue
        try:
            if (p/'comm').read_text().strip()!='Unity': continue
            args=(p/'cmdline').read_bytes()
            parent=next(line.split()[1] for line in (p/'status').read_text().splitlines() if line.startswith('PPid:'))
            rows.append({'pid':int(p.name),'parentPid':int(parent),'assetImportWorker':b'AssetImportWorker' in args,
                         'thisProject':str(repo/'.evidence/live/games/hollowmere').encode() in args,
                         'batchMode':b'-batchmode' in args.lower(),'emptyArguments':not args})
        except (OSError,StopIteration): pass
    samples.append({'utcEpoch':time.time(),'processes':rows})
    (root/'editor-monitor-details.json').write_text(json.dumps({'samples':samples},indent=2)+'\n')
    time.sleep(5)

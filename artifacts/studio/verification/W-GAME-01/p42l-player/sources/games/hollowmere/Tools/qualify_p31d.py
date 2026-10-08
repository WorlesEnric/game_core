#!/usr/bin/env python3
"""Report the unchanged B-FRAME gate, real menu readiness, route and save windows for both VSync states."""
import csv
import gzip
import json
import re
import sys
from pathlib import Path

root = Path(sys.argv[1])
results = {}
for vsync in (0, 1):
    state = []
    for run in (1, 2):
        folder = root / f'vsync{vsync}' / f'run{run}'
        csv_path = folder / 'frame-log.csv'
        text = csv_path.read_text() if csv_path.exists() else gzip.open(str(csv_path) + '.gz', 'rt').read()
        rows = list(csv.DictReader(line for line in text.splitlines() if not line.startswith('#')))
        log = (folder / 'player.log').read_text()
        stats = json.loads((folder / 'frame-stats.json').read_text())
        start = next(i for i, row in enumerate(rows) if 'ready' in row['marker'].split('|'))
        presented = re.search(r'menu presented frame=(\d+) input=True focus=menu-new', log)
        command = re.search(r'ui save\.1 frame=(\d+)', log)
        saved = next((int(row['frame']) for row in rows if 'saved' in row['marker'].split('|')), None)
        save_rows = [r for r in rows if command and saved and int(command[1]) - 2 <= int(r['frame']) <= saved + 3]
        captures = re.findall(r'save capture slot-1 mainThreadMs=([\d.]+)', log)
        save = {'saveCommandFrame': int(command[1]) if command else None, 'savedMarkerFrame': saved,
                'windowRule': 'two frames before save.1 through three frames after saved, inclusive',
                'maxDtMs': max((float(r['dt_ms']) for r in save_rows), default=None),
                'over100': sum(float(r['dt_ms']) > 100 for r in save_rows),
                'captureMainThreadMs': float(captures[0]) if captures else None, 'rows': save_rows}
        (folder / 'manual-save.json').write_text(json.dumps(save, indent=2) + '\n')
        required = ['menu', 'new-game', 'saved', 'ending:3', 'restarted', 'restore', 'done']
        markers = {tag for row in rows for tag in row['marker'].split('|')}
        checks = {
            'NVIDIA_1080p': '# screen 1920x1080 NVIDIA GeForce RTX 4060 Ti' in text,
            'VSync_state': f'frame pacing vsync={vsync} target=-1' in log,
            'NoOtherEditorOrPlayer': all(not json.loads((folder / f'host-{when}.json').read_text())['editorsAndPlayers'] for when in ('before', 'after')),
            'FullRoute': int((folder / 'exit-code.txt').read_text()) == 0 and all(tag in markers for tag in required),
            'P31d_BOOT_VisibleResponsiveMenu': bool(presented) and int(rows[start]['frame']) == int(presented[1]) and 'boot-warmup' in rows[0]['marker'],
            'P31d_BOOT_FirstReadyFrameUnder100ms': float(rows[start]['dt_ms']) <= 100,
            'P31d_BOOT_NoPostReadyStalls': stats['framesOver100msOutsideTransitionsCount'] == 0,
            'Save': bool(save_rows) and save['over100'] == 0,
        }
        state.append({'run': run, 'p95': stats['dtMs']['p95'], 'over100': stats['framesOver100msOutsideTransitionsCount'],
                      'seconds': stats['steadySeconds'], 'transitions': stats['transitions'], 'manualSave': {k:v for k,v in save.items() if k != 'rows'},
                      'firstReady': rows[start], 'loadingRows': rows[:start], 'checks': checks,
                      'B_FRAME': stats['verdict'].upper(), 'budgetChecks': stats['checks']})
    results[f'vsync{vsync}'] = state
out = root / 'qualification.json'
out.write_text(json.dumps(results, indent=2) + '\n')
print(json.dumps({key: [{'run': r['run'], 'p95': r['p95'], 'over100': r['over100'], 'B_FRAME': r['B_FRAME'], 'failedChecks': [k for k,v in r['checks'].items() if not v]} for r in runs] for key,runs in results.items()}, indent=2))

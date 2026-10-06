#!/usr/bin/env python3
import json
from pathlib import Path
import time
root = Path(__file__).resolve().parents[4] / 'artifacts/studio/workflows/P4.2e'
samples = []
while not (root / 'monitor-stop').exists():
    editors = []
    for p in Path('/proc').iterdir():
        if not p.name.isdigit(): continue
        try:
            if (p/'comm').read_text().strip() == 'Unity' and b'AssetImportWorker' not in (p/'cmdline').read_bytes(): editors.append(int(p.name))
        except (OSError, ProcessLookupError): pass
    samples.append({'utcEpoch':time.time(), 'editorPids':editors})
    (root/'editor-monitor.json').write_text(json.dumps({'maximum':max(len(s['editorPids']) for s in samples),'samples':samples},indent=2)+'\n')
    time.sleep(5)

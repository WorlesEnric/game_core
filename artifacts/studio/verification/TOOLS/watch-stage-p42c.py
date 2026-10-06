#!/usr/bin/env python3
"""Retain service job status and XML; this read-only watcher is never verdict authority."""
import json
from pathlib import Path
import sqlite3
import sys
import time
import verify as v
request=Path(sys.argv[1]);data=json.loads(request.read_text());job=data['jobId']
out=request.parent/'service';out.mkdir(exist_ok=True)
db=Path.home()/'.local/share/etos-studio/agents/gamecore-studio/state/ledger.db'
start=time.monotonic();last=None; samples=[]
while time.monotonic()-start<1900:
    editors=[]
    for proc in Path('/proc').iterdir():
        if not proc.name.isdigit():continue
        try:
            if (proc/'comm').read_text().strip()=='Unity' and b'AssetImportWorker' not in (proc/'cmdline').read_bytes():editors.append(int(proc.name))
        except (FileNotFoundError,PermissionError,ProcessLookupError):pass
    samples.append({'elapsedSeconds':round(time.monotonic()-start,3),'editorPids':editors})
    (out/'editor-counts.json').write_text(json.dumps({'maximum':max(len(x['editorPids']) for x in samples),'samples':samples},indent=2)+'\n')
    if len(editors)>1:raise RuntimeError('more than one Editor observed; stop qualification')
    with sqlite3.connect('file:'+str(db)+'?mode=ro',uri=True) as conn:
        row=conn.execute('SELECT state,slot,verdict,created_at,updated_at FROM stage_jobs WHERE job_id=?',(job,)).fetchone()
    if row and row!=last:
        result={'jobId':job,'state':row[0],'slot':row[1],'verdict':json.loads(row[2]) if row[2] else {},'createdAt':row[3],'updatedAt':row[4]}
        (out/'job.json').write_text(v.scrub(json.dumps(result,indent=2))+'\n');print(row[0],flush=True);last=row
    if row and row[0] not in ('queued','running'):break
    time.sleep(5)
else:raise SystemExit('Stage watcher timed out; no success inferred')
# Copy only this exact owner's slot's output evidence, never state secrets.
slot=row[1]
root=Path.home()/'.cache/gamecore-studio/stage'
for parent in root.glob('*/'+str(slot)):
    if not (parent/'stage.json').exists():continue
    document=json.loads((parent/'stage.json').read_text())
    if document.get('changeSetId')!=data['request']['changeSetId']:continue
    import shutil
    if (parent/'out').is_dir():shutil.copytree(parent/'out',out/'slot-out',dirs_exist_ok=True)
print('terminal',row[0],flush=True)

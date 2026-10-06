#!/usr/bin/env python3
"""Collect task ledger and encode the P3.2 frame sequence without opening credentials."""
import json
import os
from pathlib import Path
import subprocess
import sys
import verify as v
folder=Path(sys.argv[1]).resolve()
workflow=folder/'workflow'
etos=Path.home()/'.local/opt/etos/bin/etos'
ids=sorted({line.strip() for file in workflow.glob('*/task-ids.txt') for line in file.read_text().splitlines() if line.strip()})
usage=[]
for identity in ids:
    row={'taskId':identity}
    for name,args in [('task',['task','show',identity,'--json']),('budget',['budget','--task',identity,'--json'])]:
        proc=subprocess.run([str(etos),*args],capture_output=True,text=True,env={**os.environ,'ETOS_ROOT':str(Path.home()/'.local/share/etos-studio')})
        try:row[name]=json.loads(v.scrub(proc.stdout))
        except ValueError:row[name]={'exitCode':proc.returncode,'error':v.scrub(proc.stderr)}
    usage.append(row)
(workflow/'usage.json').write_text(json.dumps(usage,indent=2)+'\n')
frames=sorted((workflow/'frames').glob('*.png'))
if frames:
    proc=subprocess.run(['ffmpeg','-nostdin','-loglevel','error','-y','-framerate','0.5','-pattern_type','glob','-i',str(workflow/'frames/*.png'),'-vf','scale=1600:-2,format=yuv420p','-c:v','libx264','-crf','30','-preset','veryfast',str(workflow/'recording.mp4')],capture_output=True,text=True)
    (workflow/'encoding.json').write_text(json.dumps({'sourceFrames':len(frames),'secondsPerFrame':2,'exitCode':proc.returncode,'stderr':proc.stderr},indent=2)+'\n')
    if proc.returncode==0:
        for frame in frames:frame.unlink()
        (workflow/'frames').rmdir()
record=json.loads((folder/'result.json').read_text())
record['productRevision']=subprocess.check_output(['git','-C',str(v.ROOT/'.evidence/live'),'rev-parse','HEAD'],text=True).strip()
v.finish(folder,record)
print(json.dumps({'tasks':len(ids),'frames':len(frames),'bytes':sum(p.stat().st_size for p in folder.rglob('*') if p.is_file())}))

#!/usr/bin/env python3
"""Read only this packet's task status/budget, excluding task env/text and worker totals."""
import json
import os
from pathlib import Path
import re
import subprocess
import verify as v
cli=str(Path.home()/'.local/opt/etos/bin/etos')
env=dict(os.environ,ETOS_ROOT=str(Path.home()/'.local/share/etos-studio'))
for run in [p for row in v.OUT.iterdir() if row.is_dir() for p in row.glob('p42e-*') if (p/'workflow').exists()]:
    ids=set()
    for f in (run/'workflow').rglob('task-ids.txt'):
        ids.update(re.findall(r'\bt[0-9a-z]{16,}\b',f.read_text()))
    rows=[]
    for identity in sorted(ids):
        status=json.loads(subprocess.check_output([cli,'--json','task','--task',identity],env=env,text=True))
        budget=json.loads(subprocess.check_output([cli,'--json','budget','--task',identity],env=env,text=True))
        assert budget['task']['scope']=='task:'+identity
        assert isinstance(budget['task']['used']['micro_usd'],int)
        rows.append({'taskId':identity,'status':status['status'],'budget':{'task':budget['task']}})
    (run/'workflow/usage.json').write_text(json.dumps(rows,indent=2)+'\n')

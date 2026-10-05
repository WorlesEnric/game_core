#!/usr/bin/env bash
# The Unity 6000.0.75f1 Linux player needs an explicit backend even with -nographics.
# A tty shell with DISPLAY unset selects a null backend and crashes before GameBoot.
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
python3 - "$repo" <<'PY'
from pathlib import Path
import json, os, subprocess, sys, time
root=Path(sys.argv[1]); out=root/'artifacts/studio/cleanproof'; out.mkdir(parents=True,exist_ok=True)
exe=root/'games/cleanproof/Builds/Linux/Saltmarsh.x86_64'
environment=os.environ.copy(); environment['DISPLAY']=os.environ.get('SALTMARSH_DISPLAY',':1'); environment['XDG_SESSION_TYPE']='x11'
start=time.monotonic()
with (out/'player-console.log').open('w') as console:
    try:
        run=subprocess.run([str(exe),'-batchmode','-nographics','-saltmarshAutoplay','-logFile',str(out/'player.log')],cwd=root,env=environment,stdout=console,stderr=subprocess.STDOUT,timeout=180)
        code=run.returncode
    except subprocess.TimeoutExpired:
        code=124
source=out/'build-source-revision.txt'
record={'exitCode':code,'elapsedSeconds':round(time.monotonic()-start,3),
        'command':'DISPLAY='+environment['DISPLAY']+' XDG_SESSION_TYPE=x11 Saltmarsh.x86_64 -batchmode -nographics -saltmarshAutoplay -logFile artifacts/studio/cleanproof/player.log',
        'buildSourceRevision':source.read_text().strip() if source.exists() else 'unrecorded'}
(out/'player-result.json').write_text(json.dumps(record,indent=2)+'\n')
print(json.dumps(record));sys.exit(code if code>=0 else 128-code)
PY

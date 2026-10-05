#!/usr/bin/env python3
"""Trusted Unity-only offline package-resolution probe; no candidate or game sources."""
import json
import os
from pathlib import Path
import shlex
import shutil
import socket
import subprocess

repo = Path(__file__).resolve().parents[4]
base = Path.home() / '.cache/gamecore-studio/stage-int'
work = base / 'upm-qualification-final'
work.mkdir(exist_ok=True)
cache = Path((base / 'cache-path.txt').read_text().strip())
project = work / 'project'
for name in ('Assets', 'Packages', 'ProjectSettings', 'Library'):
    (project / name).mkdir(parents=True, exist_ok=True)
source = repo / 'games/hollowmere'
shutil.copyfile(source / 'ProjectSettings/ProjectVersion.txt', project / 'ProjectSettings/ProjectVersion.txt')
deps = json.loads((source / 'Packages/manifest.json').read_text())['dependencies']
(project / 'Packages/manifest.json').write_text(json.dumps({'dependencies':{k:v for k,v in deps.items() if k.startswith('com.unity.')}}))
# Both trees are disposable copies owned by this packet; this trusted-only probe
# never loads candidate/game code. Avoid copying 1.4 GB again on the loaded host.
if not (project / 'Library/PackageCache').exists():
    subprocess.run(['cp', '-al', str(cache / 'Library/PackageCache'), str(project / 'Library')], check=True)
launch = base / 'upm-launch-final'
launch.mkdir(exist_ok=True)
executable = launch / 'gamecore-studio'
shutil.copyfile(repo / 'studio/agent/target/debug/gamecore-studio', executable)
executable.chmod(0o700)
config = launch / 'sandbox.json'
config.write_text(json.dumps({'mode':'docker', 'image':'gamecore-stage:6000.0.75f1-v1',
    'editor':str(Path.home()/'Unity/Hub/Editor/6000.0.75f1/Editor'), 'home':str(Path.home()),
    'hostname':socket.gethostname(), 'licences':[str(Path.home()/'.config/unity3d/Unity'), str(Path.home()/'.local/share/unity3d/Unity'), '/var/lib/unity'],
    'slot':str(work), 'cache':str(cache), 'packages':str(repo/'Packages')}))
wrapper = launch / 'engine'
wrapper.write_text('#!/bin/sh\nexec ' + shlex.quote(str(executable)) + ' stage sandbox-unity ' + shlex.quote(str(config)) + ' "$@"\n')
wrapper.chmod(0o700)
result = subprocess.run(['bash',str(repo/'studio/tools/unity-batch.sh'),'--project',str(project),
    '--log-dir',str(work/'out'),'--label','upm-offline','--timeout','600','--','-quit'],
    env=dict(os.environ, UNITY=str(wrapper)))
raise SystemExit(result.returncode)

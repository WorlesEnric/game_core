#!/usr/bin/env python3
"""Current-source graphical frame, lifecycle, clean player and V1 lanes."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import verify as v

TOOLS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('rows', TOOLS / 'rows-p42l.py')
rows = importlib.util.module_from_spec(spec)
spec.loader.exec_module(rows)
output = Path('/tmp/gamecore-p42l-6e8e73c4/P42lPlayer')
lifecycle = Path('/tmp/gamecore-p42l-6e8e73c4/P42lLifecycleOwned')
build_root = Path('/tmp/gamecore-p42l-6e8e73c4/build-source')
os.environ['GC_STUDIO_BUILD_ROOT'] = str(build_root)
resume = sys.argv[1:] == ['--resume-completed-builds']
if sys.argv[1:] and not resume:
    raise SystemExit('Only --resume-completed-builds is supported')
def retained_result(pattern):
    paths = sorted(v.OUT.glob(pattern))
    if not paths:
        raise RuntimeError('Current completed build receipt missing')
    record = json.loads(paths[-1].read_text())
    if record['revision'] != v.git('rev-parse', 'HEAD') or record['status'] != 'PASS':
        raise RuntimeError('Cannot resume a failed or different-revision build')
    return record

build = retained_result('W-GAME-06/p42l-linux-il2cpp-*/result.json') if resume else v.run('W-GAME-06', 'p42l-linux-il2cpp', [sys.executable, TOOLS / 'player-p42l.py', 'build', '--output', output])
if build['status'] == 'PASS' and not resume:
    v.run('W-GAME-01', 'p42l-frame-profiles', [sys.executable, TOOLS / 'player-p42l.py', 'run', '--output', output], env={'DISPLAY': ':1'})
    v.run('W-GAME-05', 'p42l-player-lifecycle', [sys.executable,
        TOOLS / 'lifecycle-p42l.py',
        '--player', output / 'player/Hollowmere.x86_64', '--output', lifecycle])
if build['status'] == 'PASS':
    v.run('W-PLUG-11', 'p42l-audio-identity', [sys.executable, TOOLS / 'audio-p42l.py',
        lifecycle, '{out}'])
clean = retained_result('W-CLEAN-01/p42l-clean-build-*/result.json') if resume else rows.unity('W-CLEAN-01', 'p42l-clean-build', 'Saltmarsh.Build.BuildLinuxPlayer',
    args=['-quit'], project=build_root / 'games/cleanproof')
if clean['status'] == 'PASS':
    player = build_root / 'games/cleanproof/Builds/Linux/Saltmarsh.x86_64'
    result = v.run('W-CLEAN-01', 'p42l-clean-player', ['timeout', '--signal=TERM', '--kill-after=10', '180',
        player, '-batchmode', '-nographics', '-saltmarshAutoplay', '-logFile', '{out}/player.log'],
        env={'DISPLAY': ':1', 'XDG_SESSION_TYPE': 'x11', 'XDG_CONFIG_HOME': str(v.ROOT / '.evidence/P42lCleanPlayerState')})
    folder = v.ROOT / result['evidencePath']
    text = (folder / 'player.log').read_text() if (folder / 'player.log').exists() else ''
    matched = re.search(r'AUTOPLAY PASS frames=(\d+) pumps=(\d+) violations=(\d+)', text)
    result['playerProof'] = {'marker': bool(matched), 'frames': int(matched[1]) if matched else 0,
        'violations': int(matched[3]) if matched else None}
    if not matched or int(matched[1]) < 600 or int(matched[3]) != 0:
        result['status'] = 'FAIL'
    with player.open('rb') as stream:
        result['playerSha256'] = hashlib.file_digest(stream, 'sha256').hexdigest()
    v.finish(folder, result)
subprocess.run(['bash', 'studio/tools/verify-all.sh', 'v1'], cwd=build_root, check=False,
    env=dict(os.environ, GC_STUDIO_UNITY_SLOTS='1'))
v.summary()

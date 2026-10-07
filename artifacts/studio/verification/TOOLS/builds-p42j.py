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
spec = importlib.util.spec_from_file_location('rows', TOOLS / 'rows-p42j.py')
rows = importlib.util.module_from_spec(spec)
spec.loader.exec_module(rows)
output = v.ROOT / '.evidence/P42jPlayer'
lifecycle = v.ROOT / '.evidence/P42jLifecycleOwned'
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

def reclaim_completed_compiler_cache(project):
    if subprocess.run(['pgrep', '-x', 'Unity'], stdout=subprocess.DEVNULL).returncode != 1:
        raise RuntimeError('Cannot reclaim compiler cache while an Editor is active')
    cache = v.ROOT / 'games' / project / 'Library/Bee'
    if cache.is_symlink() or not cache.resolve().is_relative_to(v.ROOT.resolve()):
        raise RuntimeError('Compiler cache is outside the owned checkout')
    before = shutil.disk_usage(v.ROOT).free
    if cache.exists():
        shutil.rmtree(cache)
    path = v.ROOT / 'artifacts/studio/workflows/P4.2j/build-cache-reclamation.json'
    records = json.loads(path.read_text()) if path.exists() else []
    records.append({'reportedAt': v.utc(), 'path': str(cache.relative_to(v.ROOT)),
                    'freeBefore': before, 'freeAfter': shutil.disk_usage(v.ROOT).free,
                    'reason': 'This owned compiler cache completed its successful player build; binary, build logs, source, journals and measurements are retained.'})
    path.write_text(json.dumps(records, indent=2) + '\n')

build = retained_result('W-GAME-06/p42j-linux-il2cpp-*/result.json') if resume else v.run('W-GAME-06', 'p42j-linux-il2cpp', [sys.executable, TOOLS / 'player-p42j.py', 'build', '--output', output])
if build['status'] == 'PASS' and not resume:
    reclaim_completed_compiler_cache('hollowmere')
    v.run('W-GAME-01', 'p42j-frame-profiles', [sys.executable, TOOLS / 'player-p42j.py', 'run', '--output', output], env={'DISPLAY': ':1'})
    lifecycle = v.ROOT / '.evidence/P42jLifecycleOwned'
    v.run('W-GAME-05', 'p42j-player-lifecycle', [sys.executable,
        TOOLS / 'lifecycle-p42j.py',
        '--player', output / 'player/Hollowmere.x86_64', '--output', lifecycle])
if build['status'] == 'PASS':
    v.run('W-PLUG-11', 'p42j-audio-identity', [sys.executable, TOOLS / 'audio-p42j.py',
        lifecycle, '{out}'])
clean = retained_result('W-CLEAN-01/p42j-clean-build-*/result.json') if resume else rows.unity('W-CLEAN-01', 'p42j-clean-build', 'Saltmarsh.Build.BuildLinuxPlayer',
    args=['-quit'], project=v.ROOT / 'games/cleanproof')
if clean['status'] == 'PASS':
    reclaim_completed_compiler_cache('cleanproof')
    player = v.ROOT / 'games/cleanproof/Builds/Linux/Saltmarsh.x86_64'
    result = v.run('W-CLEAN-01', 'p42j-clean-player', ['timeout', '--signal=TERM', '--kill-after=10', '180',
        player, '-batchmode', '-nographics', '-saltmarshAutoplay', '-logFile', '{out}/player.log'],
        env={'DISPLAY': ':1', 'XDG_SESSION_TYPE': 'x11', 'XDG_CONFIG_HOME': str(v.ROOT / '.evidence/P42jCleanPlayerState')})
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
subprocess.run(['bash', 'studio/tools/verify-all.sh', 'v1'], cwd=v.ROOT, check=False,
    env=dict(os.environ, GC_STUDIO_UNITY_SLOTS='1'))
v.summary()

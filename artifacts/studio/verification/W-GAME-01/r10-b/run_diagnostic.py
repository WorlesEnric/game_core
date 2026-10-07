#!/usr/bin/env python3
"""Diagnostic only: fresh player, unchanged full route, private Xvfb/llvmpipe. Never :1."""
import datetime
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time

repo = Path(__file__).resolve().parents[5]
exe = Path(sys.argv[1]).resolve()
output = Path(sys.argv[2]).resolve()
output.mkdir(parents=True, exist_ok=False)
state = repo / '.evidence' / ('R10bPlayerState-' + output.name)
state.mkdir(parents=True, exist_ok=True)
route = repo / 'games/hollowmere/Autoplay/playthrough.txt'
command = [
    'timeout', '--signal=TERM', '--kill-after=30', '1000',
    'xvfb-run', '-a', '-s', '-screen 0 1920x1080x24', str(exe),
    '-batchmode', '-force-glcore', '-screen-width', '1920', '-screen-height', '1080',
    '-screen-fullscreen', '0', '-frameVsync', '0',
    '-frameLog', str(output / 'frame-log.csv'), '-autoplay', str(route),
    '-saveDir', str(output / 'saves'), '-logFile', str(output / 'player.log'),
]
env = os.environ.copy()
env.pop('DISPLAY', None)
env.update(LIBGL_ALWAYS_SOFTWARE='1', GALLIUM_DRIVER='llvmpipe', LP_NUM_THREADS='4',
           XDG_CONFIG_HOME=str(state / 'config'), XDG_CACHE_HOME=str(state / 'cache'))
receipt = {
    'qualification': False,
    'command': command,
    'routeSha256': hashlib.sha256(route.read_bytes()).hexdigest(),
    'binarySha256': hashlib.sha256(exe.read_bytes()).hexdigest(),
    'startedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
    'environmentOverrides': {k: env[k] for k in ['LIBGL_ALWAYS_SOFTWARE', 'GALLIUM_DRIVER', 'LP_NUM_THREADS', 'XDG_CONFIG_HOME', 'XDG_CACHE_HOME']},
}
(output / 'launch.json').write_text(json.dumps(receipt, indent=2) + '\n')
started = time.monotonic()
with (output / 'stdout.txt').open('w') as stream:
    result = subprocess.run(command, cwd=repo, env=env, stdin=subprocess.DEVNULL,
                            stdout=stream, stderr=subprocess.STDOUT)
receipt.update(exitCode=result.returncode, wallSeconds=time.monotonic() - started,
               finishedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat())
(output / 'launch.json').write_text(json.dumps(receipt, indent=2) + '\n')
with (output / 'analysis.txt').open('w') as stream:
    subprocess.run([sys.executable, str(repo / 'games/hollowmere/Tools/frame_stats.py'), str(output)],
                   check=True, stdout=stream, stderr=subprocess.STDOUT)
print(json.dumps(receipt, indent=2))
print((output / 'frame-stats.json').read_text())
sys.exit(result.returncode)

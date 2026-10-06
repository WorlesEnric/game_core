#!/usr/bin/env python3
"""Execute the developer guide's literal sample commands in a fresh final-main clone."""
import json
from pathlib import Path
import subprocess
import sys
import verify as v

clone = v.ROOT / '.evidence/p42b-guides'
out = Path(sys.argv[1])
if not clone.exists():
    subprocess.run(['git', 'clone', '--shared', '--branch', 'main', v.git('remote', 'get-url', 'origin'), str(clone)], check=True)
revision = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=clone, text=True).strip()
assert revision.startswith('1752ca8a'), 'guide source must be the frozen final-main baseline'
commands = [
    ['python3', 'samples/mechanisms/pressure-plate/make-catalog.py'],
    ['python3', 'samples/mechanisms/pressure-plate/make-catalog.py', '--check'],
    ['python3', 'samples/mechanisms/pressure-plate/make-candidate.py'],
    ['python3', 'samples/mechanisms/pressure-plate/make-candidate.py', '--check'],
    ['studio/agent/target/release/gamecore-studio', 'stage', 'run', 'plate-example', '--candidate',
     'samples/mechanisms/pressure-plate/candidate', '--source-project', 'games/hollowmere',
     '--verdict-out', str(out / 'plate-verdict.json')],
]
steps = []
for index, command in enumerate(commands, 1):
    try:
        result = subprocess.run(command, cwd=clone, capture_output=True, text=True)
        code, log = result.returncode, result.stdout + result.stderr
    except OSError as exc:
        code, log = 127, str(exc)
    (out / f'step-{index}.log').write_text(v.scrub(log))
    steps.append({'step': index, 'command': command, 'exitCode': code, 'status': 'PASS' if code == 0 else 'FAIL'})
    print(f'step {index}: exit {code}')
(out / 'walkthrough.json').write_text(json.dumps({'sourceRevision': revision, 'clone': str(clone),
    'guide': 'docs/studio/09-plugin-developer-guide.md:143-165', 'steps': steps,
    'literalLimitation': 'The guide names a build-output binary absent in a fresh clone. It does not give the installed immutable release path or a companion app-origin submission step. No host-confinement or verdict-file workaround was used.'}, indent=2) + '\n')
sys.exit(int(any(s['exitCode'] for s in steps)))

#!/usr/bin/env python3
"""Literal R8 lever export/client-submit/review sequence using the existing installed node.

No scratch daemon, app pairing, repaired candidate, direct admission or relaxed assertion.
The earlier panel-submit adaptation and its context refusal remain retained separately.
"""
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import verify as v

TOOLS = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('rows_i', TOOLS / 'rows-p42j.py')
rows = importlib.util.module_from_spec(spec)
spec.loader.exec_module(rows)
work = v.OUT / 'W-DOC-02/p42j-lever-literal'
work.mkdir(parents=True, exist_ok=False)
candidate = v.ROOT / '.evidence/p42j-stage/literal-lever'
subprocess.run([sys.executable, str(TOOLS / 'instantiate-p42d.py'),
    str(v.ROOT / '.evidence/p42j-lever/candidate'), str(candidate)], check=True)
config = {'candidate': str(candidate), 'evidence': str(work), 'nodeUrl': 'http://127.0.0.1:7410',
          'pairing': str(Path.home() / '.config/gamecore-studio/app-key.json'),
          'installedNodeUsed': True, 'productRevision': v.git('rev-parse', 'HEAD')}
path = work / 'config.json'
path.write_text(json.dumps(config, indent=2) + '\n')
env = {'GAMECORE_ETOS_AUTOSTART': '1', 'GAMECORE_ETOS_LIVE': '1'}
exported = rows.unity('W-DOC-02', 'p42j-lever-literal-export', 'Hollowmere.R8_B.LeverWalkthrough.Run',
    args=['-gcR8CStage', path, '-force-glcore'], graphical=True, env=env)
if exported['status'] != 'PASS':
    raise SystemExit('Literal lever context export failed; no stage submitted')
submitted = v.run('W-DOC-02', 'p42j-lever-literal-submit', ['dotnet', 'run', '--project',
    v.ROOT / 'games/hollowmere/Assets/Hollowmere/Tests/R8_B/Lever/Submit~/LeverSubmit.csproj', '--', path])
if submitted['status'] != 'PASS':
    raise SystemExit('Literal client stage submission failed')
subprocess.run([sys.executable, str(TOOLS / 'watch-stage-p42c.py'), str(path)], check=True)
reviewed = rows.unity('W-DOC-02', 'p42j-lever-literal-review', 'Hollowmere.R8_B.LeverWalkthrough.Run',
    args=['-gcR8CConfig', path, '-force-glcore'], graphical=True, env=env)
raise SystemExit(reviewed['status'] != 'PASS')

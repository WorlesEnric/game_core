#!/usr/bin/env bash
# Provision only this packet's registered project's versioned stage cache.
set -euo pipefail
repo="$(cd "$(dirname "$0")/../../../.." && pwd)"
live="$repo/.evidence/live"
owner_root="$(PYTHONDONTWRITEBYTECODE=1 python3 - "$repo" <<'PY'
import hashlib,importlib.util,json,sys
from pathlib import Path
root=Path(sys.argv[1]);sys.path.insert(0,str(root/'artifacts/studio/verification/TOOLS'))
spec=importlib.util.spec_from_file_location('live',root/'artifacts/studio/verification/TOOLS/live-p42c.py');m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
owner=json.dumps(['gamecore-unity',m.project_id()],separators=(',',':'))
print(Path.home()/'.cache/gamecore-studio/stage'/hashlib.sha256(owner.encode()).hexdigest())
PY
)"
cache="$($repo/studio/agent/target/release/gamecore-studio stage cache-path --repo "$live" --source-project "$live/games/hollowmere" --root "$owner_root")"
bash "$live/studio/stage/provision-cache.sh" "$cache" --offline-from "$HOME/.nuget/packages" --unity-library "$live/games/hollowmere/Library" --upm-from "$HOME/.cache/Unity/upm"
# Never remove/reset .cold-attempt or alter the cache manifest/budgets to make a run pass.

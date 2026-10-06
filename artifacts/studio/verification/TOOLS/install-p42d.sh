#!/usr/bin/env bash
set -euo pipefail
repo="$(cd "$(dirname "$0")/../../../.." && pwd)"
live="$repo/.evidence/live"
if pgrep -x Unity >/dev/null || pgrep -x ffmpeg >/dev/null; then echo 'BLOCKED: Editor or recorder active'; exit 2; fi
test "$(git -C "$live" rev-parse HEAD)" = "$(git -C "$repo" rev-parse origin/main)"
git -C "$repo" diff --exit-code origin/main -- studio/agent studio/etos/agent
export ETOS_ROOT="$HOME/.local/share/etos-studio" PATH="$HOME/.local/opt/etos/bin:$PATH"
for game in hollowmere cleanproof; do
 bash "$live/studio/etos/install.sh" --register-project auto "$live/games/$game"
done
python3 "$repo/studio/etos/install-state.py" --root "$ETOS_ROOT" release "$repo/studio/agent/target/release/gamecore-studio" --etos "$HOME/.local/opt/etos/bin/etos"
python3 "$repo/studio/etos/install-state.py" --root "$ETOS_ROOT" release "$repo/studio/agent/target/release/gamecore-studio" --etos "$HOME/.local/opt/etos/bin/etos"
(cd "$ETOS_ROOT/agents/gamecore-studio/current" && sha256sum --check SHA256SUMS)
etos --json agent list
python3 - <<'PY'
import pathlib,tomllib,json,hashlib
base=pathlib.Path.home()/'.local/share/etos-studio/agents/gamecore-studio'
d=tomllib.loads((base/'state/config.toml').read_text())
print(json.dumps({'release':(base/'current').resolve().name,'stage':d.get('stage'),'ops_prices':d.get('ops_prices'),'binarySha256':hashlib.sha256((base/'current/bin/gamecore-studio').read_bytes()).hexdigest()},indent=2))
PY

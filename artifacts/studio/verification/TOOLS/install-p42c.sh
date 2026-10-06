#!/usr/bin/env bash
set -euo pipefail
repo="$(cd "$(dirname "$0")/../../../.." && pwd)"
clone="$HOME/wkspace/gc-studio/companion"
if pgrep -x Unity >/dev/null || pgrep -x ffmpeg >/dev/null; then echo 'BLOCKED: Editor/recorder active'; exit 2; fi
test "$(git -C "$clone" rev-parse HEAD)" = "$(git -C "$repo" rev-parse origin/main)"
test -z "$(git -C "$clone" status --porcelain --untracked-files=no)"
export ETOS_ROOT="$HOME/.local/share/etos-studio" PATH="$HOME/.local/opt/etos/bin:$HOME/.cargo/bin:$HOME/.dotnet:$PATH"
helper="$clone/studio/etos/install-state.py"
bash "$clone/studio/etos/install.sh" --apply-prices
for game in hollowmere cleanproof; do
  bash "$clone/studio/etos/install.sh" --register-project auto "$repo/.evidence/live/games/$game"
done
python3 "$helper" --root "$ETOS_ROOT" release "$clone/studio/agent/target/release/gamecore-studio" --etos "$HOME/.local/opt/etos/bin/etos"
python3 "$helper" --root "$ETOS_ROOT" release "$clone/studio/agent/target/release/gamecore-studio" --etos "$HOME/.local/opt/etos/bin/etos"
(cd "$ETOS_ROOT/agents/gamecore-studio/current" && sha256sum --check SHA256SUMS)
etos --json agent list
python3 - <<'PY'
import pathlib,tomllib,json,subprocess
home=pathlib.Path.home();base=home/'.local/share/etos-studio/agents/gamecore-studio'
d=tomllib.loads((base/'state/config.toml').read_text())
print(json.dumps({'release':(base/'current').resolve().name,'sourceRevision':subprocess.check_output(['git','-C',str(home/'wkspace/gc-studio/companion'),'rev-parse','HEAD'],text=True).strip(),'stage':d.get('stage'),'ops_prices':d.get('ops_prices')},indent=2))
PY

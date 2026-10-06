#!/usr/bin/env bash
# The operator stage command and source project's local package pins must share a root.
set -euo pipefail
repo="$(cd "$(dirname "$0")/../../../.." && pwd)"
live="$repo/.evidence/live"
if pgrep -x Unity >/dev/null; then echo 'BLOCKED: Editor active'; exit 2; fi
# Only authored fixtures and trusted acceptance files may differ; package/stage source must be exact final main.
git -C "$live" diff --exit-code HEAD -- Packages studio/stage studio/agent studio/tools
export ETOS_ROOT="$HOME/.local/share/etos-studio"
for game in hollowmere cleanproof; do
  bash "$live/studio/etos/install.sh" --register-project auto "$live/games/$game"
done
# Reuse the verified immutable release; configuration digest changes cause only the documented agent restart.
python3 "$HOME/wkspace/gc-studio/companion/studio/etos/install-state.py" --root "$ETOS_ROOT" release "$HOME/wkspace/gc-studio/companion/studio/agent/target/release/gamecore-studio" --etos "$HOME/.local/opt/etos/bin/etos"

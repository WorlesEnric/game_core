#!/usr/bin/env bash
# P4.2 step 0: the full installer also touches providers.env and restarts etosd.
# Use its exact secret-free state helper for the expressly authorized companion reinstall.
set -euo pipefail
if pgrep -af '[g]c-studio/p3' >/dev/null; then
  echo 'BLOCKED: a P3 live run is active'; exit 2
fi
clone="$HOME/wkspace/gc-studio/companion"
node="$HOME/.local/share/etos-studio"
export ETOS_ROOT="$node" PATH="$HOME/.local/opt/etos/bin:$PATH"
repo="$(cd "$(dirname "$0")/../../../.." && pwd)"
helper="$clone/studio/etos/install-state.py"
echo "Dedicated main revision: $(git -C "$clone" rev-parse HEAD)"
echo "Before current: $(readlink "$node/agents/gamecore-studio/current")"
# Templates reviewed before invocation; retain redacted diffs in this command's transcript.
bash "$clone/studio/etos/install.sh" --apply-prices
for game in hollowmere cleanproof; do
  bash "$clone/studio/etos/install.sh" --register-project auto "$repo/games/$game"
done
python3 "$helper" --root "$node" release "$clone/studio/agent/target/release/gamecore-studio" --etos "$HOME/.local/opt/etos/bin/etos"
# Second identical release must verify checksums and be a no-op.
python3 "$helper" --root "$node" release "$clone/studio/agent/target/release/gamecore-studio" --etos "$HOME/.local/opt/etos/bin/etos"
echo "After current: $(readlink "$node/agents/gamecore-studio/current")"
(cd "$node/agents/gamecore-studio/current" && sha256sum --check SHA256SUMS)
etos --json agent list
date -u +%FT%TZ

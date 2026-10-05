#!/usr/bin/env bash
# Scratch companion authentication only; no installed service, live node or paid ops.
set -euo pipefail
repo=$(git rev-parse --show-toplevel)
cd "$repo"
evidence="$repo/studio/agent/evidence/stage-tmp"
base="$HOME/.cache/gamecore-studio/stage-tmp"
cargo build --manifest-path studio/agent/Cargo.toml
cache=$(studio/agent/target/debug/gamecore-studio stage cache-path \
  --repo "$repo" --source-project "$repo/games/hollowmere" --root "$base/stage")
bash studio/stage/provision-cache.sh "$cache" \
  --offline-from "$HOME/.cache/gamecore-studio/stage-int/bootstrap-nuget" \
  --unity-library "$HOME/.cache/gamecore-studio/stage/_warm/Library" \
  --upm-from "$HOME/.cache/Unity/upm"
bash studio/stage/provision-cache.sh "$cache" --verify
# Sequential: this packet holds at most one allocator reservation.
stage_status=0
cargo test --manifest-path studio/agent/Cargo.toml --test stage_tmp -- --ignored --nocapture || stage_status=$?
python3 "$evidence/capture.py"
cargo test --manifest-path studio/agent/Cargo.toml --test stage_licensing -- --ignored --nocapture || stage_status=$?
# negative-semantic is intentionally scan-only: never install it in Unity.
work=$(mktemp -d "$base/negative.XXXXXXXX")
rmdir "$work"
bash studio/stage/analyzer/offline-check.sh "$work" "$cache"

exit "$stage_status"

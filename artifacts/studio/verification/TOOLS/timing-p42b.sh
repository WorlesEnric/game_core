#!/usr/bin/env bash
# Only scratch-clone Assets receive the packet-owned measurement assembly.
set -euo pipefail
repo="$(cd "$(dirname "$0")/../../../.." && pwd)"
clone="$repo/.evidence/p42b-timing"
if [[ ! -d "$clone/.git" ]]; then
  git clone --shared --branch main "$(git -C "$repo" remote get-url origin)" "$clone"
fi
# Source is pinned to the final-main baseline in the packet, never a moving worktree.
[[ "$(git -C "$clone" rev-parse HEAD)" == 1752ca8aa* ]] || [[ "$(git -C "$clone" rev-parse --short=8 HEAD)" == 1752ca8a ]]
if [[ ! -d "$clone/games/hollowmere/Library" && -d "$repo/games/hollowmere/Library/ScriptAssemblies" ]]; then
  cp -a --reflink=auto "$repo/games/hollowmere/Library" "$clone/games/hollowmere/Library"
fi
mkdir -p "$clone/games/hollowmere/Assets/P42bHarness"
cp "$repo/artifacts/studio/verification/TOOLS/P42bHarness/"* "$clone/games/hollowmere/Assets/P42bHarness/"
exec bash "$repo/studio/tools/unity-batch.sh" --project "$clone/games/hollowmere" --log-dir "$1/logs" --label "p42b-timing-$2" --results "$1/results.xml" -- -runTests -testPlatform EditMode -testFilter "${3:-P42b.Acceptance.TimingTests}"

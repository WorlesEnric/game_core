#!/usr/bin/env bash
# Graphical :1 only; one Editor at a time, using the shared host allocator and redaction.
set -euo pipefail
repo="$(cd "$(dirname "$0")/../../../../.." && pwd)"
export DISPLAY=:1
export UNITY="$repo/artifacts/studio/verification/W-UI-02/r7-d/unity-graphical.py"
export GAMECORE_ETOS_AUTOSTART=0 GAMECORE_ETOS_LIVE=0
bash "$repo/studio/tools/unity-batch.sh" --project "$repo/games/hollowmere" \
  --log-dir "$repo/artifacts/studio/verification/W-UI-02/r7-d/logs" --label r7-d-suite \
  --results "$repo/artifacts/studio/verification/W-UI-02/r7-d/results.xml" -- \
  -runTests -testPlatform EditMode -testFilter 'GameCore\.Studio\.[Uu][Ii].*|Hollowmere\.R7_[BD].*|Hollowmere\.R2_38.*'
bash "$repo/studio/tools/unity-batch.sh" --project "$repo/games/hollowmere" \
  --log-dir "$repo/artifacts/studio/verification/W-UI-02/r7-d/logs" --label r7-d-fence -- \
  -executeMethod Hollowmere.P3_2.Workflows.ScenariosR7Picking.RunFence
bash "$repo/studio/tools/unity-batch.sh" --project "$repo/games/hollowmere" \
  --log-dir "$repo/artifacts/studio/verification/W-UI-03/r7-d/logs" --label r7-d-lantern -- \
  -executeMethod Hollowmere.P3_2.Workflows.ScenariosR7Picking.RunLantern

#!/usr/bin/env bash
# No pairing command, worker submission or paid media operation.
set -euo pipefail
repo="$(cd "$(dirname "$0")/../../../../.." && pwd)"
export DISPLAY=:1
export UNITY="$repo/artifacts/studio/verification/W-UI-01/r7-a/unity-graphical.py"
export GAMECORE_ETOS_LIVE=0
bash "$repo/studio/tools/unity-batch.sh" --project "$repo/games/hollowmere" \
  --log-dir "$repo/.unity-logs" --label r7-a-ui \
  --results "$repo/artifacts/studio/verification/W-UI-01/r7-a/ui.xml" -- \
  -runTests -testPlatform EditMode \
  -testFilter 'GameCore\.Studio\.UI\..*|Hollowmere\.R4_A\.ClientRegressionTests.*|Hollowmere\.R7_A\.ContentStampTests.*'
bash "$repo/studio/tools/unity-batch.sh" --project "$repo/games/hollowmere" \
  --log-dir "$repo/.unity-logs" --label r7-a-open-play -- \
  -executeMethod Hollowmere.R7_A.OpenPlay.Run

#!/usr/bin/env bash
# Replay unchanged retained candidates; no new worker or provider calls.
set -euo pipefail
repo="$(cd "$(dirname "$0")/../../../../.." && pwd)"
out="${1:?Pass a fresh absolute evidence directory}"
export GAMECORE_ETOS_AUTOSTART=0
export GAMECORE_R7_EVIDENCE="$out/prepare"
bash "$repo/studio/tools/unity-batch.sh" --project "$repo/games/hollowmere" \
  --log-dir "$out/prepare-logs" --label r7-a-prepare -- \
  -executeMethod Hollowmere.R7_A.ReopenPreparation.Prepare -quit
export DISPLAY=:1
export UNITY="$repo/artifacts/studio/verification/W-UI-01/r7-a/unity-graphical.py"
export GAMECORE_R7_WORKFLOW=reopen
export GAMECORE_R7_OUT="$out/workflow"
bash "$repo/studio/tools/unity-batch.sh" --project "$repo/games/hollowmere" \
  --log-dir "$out/reopen-logs" --label r7-a-reopen -- \
  -executeMethod Hollowmere.P3_2.Workflows.WorkflowRunner.Run

#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../../../.." && pwd)"
label="${1:-r7-e-final}"
out="$root/artifacts/studio/verification/W-PLUG-10/$label"
mkdir -p "$out"
# Include the requested spelling and the actual package namespaces (Edit and uppercase UI).
filter='GameCore\.Studio\.(Core|Ui|Edit|UI).*|Hollowmere\.R7_[CE].*'
export UNITY="$root/games/hollowmere/Assets/Hollowmere/Tests/R7_C/batch-graphics.py"
export GAMECORE_ETOS_AUTOSTART=0
export DISPLAY="${DISPLAY:-:1}"
bash "$root/studio/tools/unity-batch.sh" \
  --project "$root/games/hollowmere" --log-dir "$out" --label "$label" \
  --results "$out/results.xml" \
  --require-test Hollowmere.R7_C.Validation.DefinitionDiagnosticParityTests.WPlug10_SameInvalidDefinition_HasCodeMessageAndLocationParityAcrossAllFronts \
  -- -runTests -testPlatform EditMode -testFilter "$filter" -force-glcore

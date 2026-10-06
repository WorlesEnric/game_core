#!/usr/bin/env bash
# The private Xvfb display never occupies :1 and launches one allocator-owned Editor.
set -euo pipefail
repo="$(cd "$(dirname "$0")/../../../.." && pwd)"
clone="$repo/.evidence/p42b-guides"
[[ -d "$clone/.git" ]]
if [[ ! -d "$clone/games/hollowmere/Library" ]]; then
  cp -a --reflink=auto "$repo/games/hollowmere/Library" "$clone/games/hollowmere/Library"
fi
mkdir -p "$clone/games/hollowmere/Assets/P42bGuide"
cp "$repo/artifacts/studio/verification/TOOLS/P42bGuide/"* "$clone/games/hollowmere/Assets/P42bGuide/"
unity_tools_dir="$repo/studio/tools"
source "$unity_tools_dir/unity-slot.sh"
unity_slot_acquire
trap unity_slot_release EXIT
xvfb-run -a -s '-screen 0 1600x1000x24' python3 "$repo/studio/stage/run-redacted.py" --log "$1/editor.log" --timeout 1500 --silence 600 -- env \
 GAMECORE_ETOS_AUTOSTART=0 GAMECORE_ETOS_KEY_FILE=/nonexistent/p42b-guide-offline GAMECORE_P42_EVIDENCE="$1" \
 "$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity" -projectPath "$clone/games/hollowmere" -logFile - \
 -runTests -testPlatform EditMode -testFilter P42b.Guide.GuideOpenTests -testResults "$1/results.xml"

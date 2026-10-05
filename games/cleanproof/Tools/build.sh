#!/usr/bin/env bash
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
mkdir -p "$repo/artifacts/studio/cleanproof"
git -C "$repo" rev-parse HEAD > "$repo/artifacts/studio/cleanproof/build-source-revision.txt"
exec bash "$repo/studio/tools/unity-batch.sh" --project "$repo/games/cleanproof" --log-dir "$repo/artifacts/studio/cleanproof/build" --label linux-player -- -quit -buildTarget Linux64 -executeMethod Saltmarsh.Build.BuildLinuxPlayer

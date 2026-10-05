#!/usr/bin/env bash
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
exec bash "$repo/studio/tools/unity-batch.sh" --project "$repo/games/cleanproof" --log-dir "$repo/artifacts/studio/cleanproof/build" --label linux-player -- -quit -buildTarget Linux64 -executeMethod Saltmarsh.Build.BuildLinuxPlayer.Run

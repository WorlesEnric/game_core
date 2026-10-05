#!/usr/bin/env bash
# workflow-p3.2-robe.sh - P3.2 workflow W-AI-01 + asset generation, live (interactive Editor on the host display :1):
#   a) W-AI-01: Maren (the healer) selected, "Give her a green robe." -> candidate previewed -> applied -> behaviour
#      assets hashed before/after (unchanged) -> undone (hashes restored);
#   b) asset generation: asset.generate (generate.image, max_cost_usd 0.25) for the Lantern item's icon -> verified
#      import (asset.import, journal with prompt + digest) -> `assign` Lantern.icon -> both undone;
#   c) voice line: dialogue.generateVoice (the catalog tool) probed, then the media gateway's tts op (max_cost_usd 0.10)
#      -> verified import of the WAV -> undone.
# Usage: studio/tools/workflow-p3.2-robe.sh [--packet <name>]
# Spends: one gc-designer task, one image, one tts line.
set -euo pipefail
exec bash "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/workflow-p3.2-lib.sh" robe "$@"

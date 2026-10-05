#!/usr/bin/env bash
# workflow-p3.2-robe.sh - P3.2 workflow W-AI-01 + asset generation, live (interactive Editor on the host display :1):
#   a) W-AI-01: Maren (the healer) and her NPC/entity definitions selected, "Give her a green robe." -> candidate
#      previewed -> applied -> behaviour assets hashed before/after (unchanged) -> undone (hashes restored);
#   b) attempt 2 only: the robe texture generated (generate.image, max_cost_usd 0.25) and imported (journaled), then
#      undone - no catalog tool puts a texture on a material, so "material updated" is recorded as a gap;
#   c) asset generation: asset.generate (generate.image, max_cost_usd 0.25) for the Lantern item's icon -> verified
#      import (journal with prompt + digest) -> `bind` (re-import as Sprite + assign Lantern.icon) -> both undone;
#   d) voice line: dialogue.generateVoice (the catalog tool) probed, then the media gateway's tts op (max_cost_usd 0.10)
#      -> verified import of the WAV -> undone.
# Attempts: --attempt 1 (workflow `robe`, the mandate's prompt verbatim) or --attempt 2 (default, workflow `robe2`: the
# prompt also states the #rrggbb colour format, because attempt 1's 8-digit tint was refused at apply time).
# Usage: studio/tools/workflow-p3.2-robe.sh [--attempt 1|2] [--packet <name>]
# Spends: one or two gc-designer tasks, two or three images, one tts line.
set -euo pipefail
workflow="robe2"
args=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --attempt) [[ "$2" == "1" ]] && workflow="robe" || workflow="robe2"; shift 2 ;;
    *) args+=("$1"); shift ;;
  esac
done
exec bash "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/workflow-p3.2-lib.sh" "${workflow}" "${args[@]+"${args[@]}"}"

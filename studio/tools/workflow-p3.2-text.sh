#!/usr/bin/env bash
# workflow-p3.2-text.sh - P3.2 workflow 1/W-AI-02: text prompt -> typed edit on a viewport selection, live, through the
# real Studio windows and the real ETOS node (interactive Editor on the host display :1).
#   a) clarification round trip: Maren, Pip and Odd selected, "Make one of them patrol around the well." -> the worker's
#      question is answered from the task tray -> candidate previewed and rejected;
#   b) Maren + Village Well selected, "Move Maren two metres north and make her patrol around the well." -> candidate
#      (move / npc.setPatrol) previewed with ghosts and compared -> applied (journal entry) -> undone from History;
#   c) W-AI-02: right-click point-at near the well, "Add a ferryman NPC here who talks about the bell." -> candidate ->
#      applied -> roster/describe of what was created -> undone.
# Attempts (both recorded in artifacts/studio/workflows/P3.2):
#   --attempt 1   the scene objects only (workflow `text`): the worker cannot target npc.setPatrol because the context
#                 slice of a placed NPC does not contain its npc.definition, and asks for it;
#   --attempt 2   (default) the same prompts with the NPC definitions also selected in the Project window, and Odd's
#                 definition next to the point-at location for the ferryman (workflow `text2`).
# Usage: studio/tools/workflow-p3.2-text.sh [--attempt 1|2] [--packet <name>]
# Output: a run folder (keyframes, recording.mp4, run-log.jsonl, timeline.jsonl, per-request JSON, etos usage); see
# studio/tools/workflow-p3.2-lib.sh. Spends model time on gc-designer (3-6 tasks).
set -euo pipefail
workflow="text2"
args=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --attempt) [[ "$2" == "1" ]] && workflow="text" || workflow="text2"; shift 2 ;;
    *) args+=("$1"); shift ;;
  esac
done
exec bash "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/workflow-p3.2-lib.sh" "${workflow}" "${args[@]+"${args[@]}"}"

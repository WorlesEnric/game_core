#!/usr/bin/env bash
# workflow-p3.2-text.sh - P3.2 workflow 1/W-AI-02: text prompt -> typed edit on a viewport selection, live, through the
# real Studio windows and the real ETOS node (interactive Editor on the host display :1).
#   a) clarification round trip: Maren, Pip and Odd selected, "Make one of them patrol around the well." -> the worker's
#      question is answered from the task tray -> candidate previewed and rejected;
#   b) Maren + Village Well selected, "Move Maren two metres north and make her patrol around the well." -> candidate
#      (move / npc.setPatrol) previewed with ghosts and compared -> applied (journal entry) -> undone from History;
#   c) W-AI-02: right-click point-at near the well, "Add a ferryman NPC here who talks about the bell." -> candidate ->
#      applied -> roster/describe of what was created -> undone.
# Usage: studio/tools/workflow-p3.2-text.sh [--packet <name>]   (run from the Mac after sync-to-host.sh, or on the host)
# Output: a run folder (keyframes, recording.mp4, run-log.jsonl, timeline.jsonl, per-request JSON, etos usage); see
# studio/tools/workflow-p3.2-lib.sh. Spends model time on gc-designer (2-4 tasks).
set -euo pipefail
exec bash "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/workflow-p3.2-lib.sh" text "$@"

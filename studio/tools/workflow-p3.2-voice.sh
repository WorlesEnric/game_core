#!/usr/bin/env bash
# workflow-p3.2-voice.sh - P3.2 voice workflow and W-VOICE-01, live (interactive Editor on the host display :1, PipeWire
# virtual microphone made the default source for the run):
#   a) W-VOICE-01: two spoken prompts are made with the tts op; "Delete every NPC in the village." is played into the
#      virtual microphone while the prompt bar's mic is held (push-to-talk): partial revisions are shown, the final
#      transcript lands in the field, nothing is sent (tray rows, gateway requests and journal entries unchanged);
#   b) "Move the well one metre to the east." spoken the same way, the final transcript confirmed in the field and sent
#      with Send -> candidate previewed -> applied -> undone.
# Attempts: --attempt 1 (workflow `voice`: destructive take first, then the move) or --attempt 2 (default, `voice2`: the
# move take first, then the destructive one; attempt 1's second push-to-talk never connected, see PACKET.md D19).
# Usage: studio/tools/workflow-p3.2-voice.sh [--attempt 1|2] [--packet <name>]
# Spends: two tts lines, two realtime sessions, one gc-designer task.
set -euo pipefail
workflow="voice2"
args=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --attempt) [[ "$2" == "1" ]] && workflow="voice" || workflow="voice2"; shift 2 ;;
    *) args+=("$1"); shift ;;
  esac
done
exec bash "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/workflow-p3.2-lib.sh" "${workflow}" --voice "${args[@]+"${args[@]}"}"

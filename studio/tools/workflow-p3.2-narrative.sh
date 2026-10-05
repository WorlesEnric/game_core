#!/usr/bin/env bash
# workflow-p3.2-narrative.sh - P3.2 workflows W-AI-03, W-AI-04, W-AI-05 and W-AI-06, live, in Editor sessions:
#   narrative: Odd (+ his dialogue graph) selected, "Add a line Odd only says after the shrine is lit." (dialogue.preview
#     before / after, with and without the fact); Doc_Hud selected, "Change the HUD objective label and bind it to the
#     current quest stage name." (inspect.describe before/after); DrownedBell + Lantern selected, "Change the lantern
#     quest to require two oil flasks." (quest.simulate before/after); each candidate previewed and applied when valid;
#     the project saved and the applied entries written to Library/P3_2/narrative.json;
#   persist (with --persist, used when the narrative session applied nothing): journaled change sets that do apply
#     (generate.image import, `bind` of the icon as a Sprite, tts import) are made and saved the same way;
#   reopen (W-AI-06): a new Editor session (keeps the index cache: a real close and reopen); the journal and the file
#     hashes are checked against the saved state; every entry is undone, redone and undone again with a hash check after
#     each step, ending at the pre-edit content.
# Usage: studio/tools/workflow-p3.2-narrative.sh [--packet <name>] [--persist] [--only narrative|persist|reopen]
# Spends: three gc-designer tasks (plus at most one clarification each); --persist: one image, one tts line.
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
only=""
persist=0
args=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --only) only="$2"; shift 2 ;;
    --persist) persist=1; shift ;;
    *) args+=("$1"); shift ;;
  esac
done
rc=0
if [[ -z "${only}" || "${only}" == "narrative" ]]; then
  bash "${here}/workflow-p3.2-lib.sh" narrative "${args[@]+"${args[@]}"}" || rc=$?
fi
if [[ "${only}" == "persist" || ( -z "${only}" && "${persist}" == "1" ) ]]; then
  bash "${here}/workflow-p3.2-lib.sh" persist "${args[@]+"${args[@]}"}" || rc=$?
fi
if [[ -z "${only}" || "${only}" == "reopen" ]]; then
  # The reopen session keeps the index cache the previous session saved (a real close and reopen).
  WORKFLOW_KEEP_INDEX_CACHE=1 bash "${here}/workflow-p3.2-lib.sh" reopen "${args[@]+"${args[@]}"}" || rc=$?
fi
exit "${rc}"

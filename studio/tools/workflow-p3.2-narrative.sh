#!/usr/bin/env bash
# workflow-p3.2-narrative.sh - P3.2 workflows W-AI-03, W-AI-04, W-AI-05 and W-AI-06, live, in two Editor sessions:
#   session 1 (narrative): Odd selected, "Add a line Odd only says after the shrine is lit." (dialogue.preview before /
#     after, with and without the fact); Doc_Hud selected, "Change the HUD objective label and bind it to the current
#     quest stage name." (inspect.describe before/after); DrownedBell + Lantern selected, "Change the lantern quest to
#     require two oil flasks." (quest.simulate before/after); each candidate previewed and applied; the project saved;
#   session 2 (reopen, W-AI-06): the Editor is closed and reopened; the journal and the asset hashes are checked against
#     the saved state; every edit is undone, redone and undone again with a hash check after each step, ending at the
#     pre-edit content.
# Usage: studio/tools/workflow-p3.2-narrative.sh [--packet <name>] [--only narrative|reopen]
# Spends: three gc-designer tasks (plus at most one clarification each).
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
only=""
args=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --only) only="$2"; shift 2 ;;
    *) args+=("$1"); shift ;;
  esac
done
rc=0
if [[ -z "${only}" || "${only}" == "narrative" ]]; then
  bash "${here}/workflow-p3.2-lib.sh" narrative "${args[@]+"${args[@]}"}" || rc=$?
fi
if [[ -z "${only}" || "${only}" == "reopen" ]]; then
  # The reopen session keeps the index cache the narrative session saved (a real close and reopen).
  WORKFLOW_KEEP_INDEX_CACHE=1 bash "${here}/workflow-p3.2-lib.sh" reopen "${args[@]+"${args[@]}"}" || rc=$?
fi
exit "${rc}"

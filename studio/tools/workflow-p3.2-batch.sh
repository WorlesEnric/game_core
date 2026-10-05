#!/usr/bin/env bash
# workflow-p3.2-batch.sh - P3.2 batch / multi-target edit with conflict handling, live (interactive Editor on :1):
# marquee (full containment) over Crate 2, Crate 3 and Market Crate in Thornwick Village; "Arrange these crates evenly
# in a ring of radius 3 metres around the Village Well." -> candidate previewed -> one target moved by hand (a manual
# move change set) so its stamp is stale -> Apply under AllOrNothing (expected: refused, Conflict{expected, actual}) ->
# Rebase of the stale op + Apply under BestEffort -> journal -> both the agent entry and the manual move undone.
# Attempts: --attempt 1 (workflow `batch`: crates only) or --attempt 2 (default, workflow `batch2`: the Village Well is
# Ctrl-added to the selection and the tray answer gives its position; attempt 1's worker asked for it twice).
# Usage: studio/tools/workflow-p3.2-batch.sh [--attempt 1|2] [--packet <name>]
# Spends: one gc-designer task (plus at most one clarification).
set -euo pipefail
workflow="batch2"
args=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --attempt) [[ "$2" == "1" ]] && workflow="batch" || workflow="batch2"; shift 2 ;;
    *) args+=("$1"); shift ;;
  esac
done
exec bash "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/workflow-p3.2-lib.sh" "${workflow}" "${args[@]+"${args[@]}"}"

#!/usr/bin/env bash
# workflow-p3.2-batch.sh - P3.2 batch / multi-target edit with conflict handling, live (interactive Editor on :1):
# marquee (full containment) over Crate 2, Crate 3 and Market Crate in Thornwick Village; "Arrange these crates evenly
# in a ring of radius 3 metres around the Village Well." -> candidate previewed -> one target moved by hand (a manual
# move change set) so its stamp is stale -> Apply under AllOrNothing (expected: refused, Conflict{expected, actual}) ->
# Rebase of the stale op + Apply under BestEffort -> journal -> both the agent entry and the manual move undone.
# Usage: studio/tools/workflow-p3.2-batch.sh [--packet <name>]
# Spends: one gc-designer task (plus at most one clarification).
set -euo pipefail
exec bash "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/workflow-p3.2-lib.sh" batch "$@"

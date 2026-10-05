#!/usr/bin/env bash
# workflow-p3.2-honesty.sh - P3.2 inspect/explain and failure honesty, live (interactive Editor on :1, Blackmere Marsh):
#   a) local, model-free answers (SADR-003): inspect.describe / inspect.explain on the Causeway Gate, query.impact /
#      query.references on the Lantern item, logic.explain on the GateKeyOpensGate rule;
#   b) the worker asked "Why is the Causeway Gate locked, and what would break if the lantern item were deleted?" -
#      whatever it returns (empty change set, clarification, refusal) is recorded verbatim;
#   c) a request cancelled from the task tray while its etos task runs (cancel acknowledgement time, no candidate);
#   d) an image op with max_cost_usd 0.001 (expected budget refusal; recorded as it happens) and a describe op on the
#      result when one exists.
# Usage: studio/tools/workflow-p3.2-honesty.sh [--packet <name>]
# Spends: two gc-designer tasks (one cancelled), at most one image and one describe.
set -euo pipefail
exec bash "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/workflow-p3.2-lib.sh" honesty "$@"

#!/usr/bin/env bash
# Retired legacy packageRef bridge. Use the project-scoped candidate-stage companion route.
# A bare package loses the proposal, inputs and trusted job identity and cannot produce an admissible verdict.
set -euo pipefail
printf '%s\n' '{"ok":false,"code":"stage_failed","reason":"legacy_stage_request","hint":"Submit {changeSetId} through the authenticated project-scoped /v1/stage route."}'
exit 2

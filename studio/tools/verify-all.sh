#!/usr/bin/env bash
# Linux qualification; run from any directory. No provider calls or service restarts.
# Usage: verify-all.sh [all|static|bake|unity|perf|clean|security|summary]
# Display/node jobs: verify-all.sh ui|views|graphics|voice|media|stage|reconnect
# Additional bounded build qualification: verify-all.sh build|v1
# Interactive/node qualification is documented in the P4.2 packet and row READMEs.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
export PYTHONDONTWRITEBYTECODE=1
export PATH="$HOME/.dotnet:$HOME/.cargo/bin:$PATH"
export GC_STUDIO_UNITY_SLOTS="${GC_STUDIO_UNITY_SLOTS:-3}" PROBE_RUNS=2
case "${1:-all}" in
  final-report) exec python3 "$root/artifacts/studio/verification/TOOLS/report_p42b.py" ;;
  final-live) exec python3 "$root/artifacts/studio/verification/TOOLS/final_live.py" ;;
  live-install|final-views-tests|final-checks|final-timing|tariff-tests|final-guides|live-preflight|final-selection|release-build|release-binary|node-equivalence|guide-open|harness-checks) exec python3 "$root/artifacts/studio/verification/TOOLS/live_acceptance.py" "$@" ;;
  ui|views|graphics|voice|media|stage|reconnect|build|v1|memory)
    exec python3 "$root/artifacts/studio/verification/TOOLS/acceptance.py" "$@" ;;
  *) exec python3 "$root/artifacts/studio/verification/TOOLS/verify.py" "${@:-all}" ;;
esac

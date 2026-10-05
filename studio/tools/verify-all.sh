#!/usr/bin/env bash
# Linux qualification; run from any directory. No provider calls or service restarts.
# Usage: verify-all.sh [all|static|bake|unity|perf|clean|security|summary]
# Interactive/node qualification is documented in the P4.2 packet and row READMEs.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
export PYTHONDONTWRITEBYTECODE=1
export PATH="$HOME/.dotnet:$HOME/.cargo/bin:$PATH"
export GC_STUDIO_UNITY_SLOTS="${GC_STUDIO_UNITY_SLOTS:-3}" PROBE_RUNS=2
exec python3 "$root/artifacts/studio/verification/TOOLS/verify.py" "${@:-all}"

#!/usr/bin/env bash
#
# W0 gate (GC-002): documentation validator plus its negative self-test, then the pure asset checks
# (compile the Shared-seam API snapshot and run every protocol fixture).
#
# Tool paths come from the environment so the script works on any host:
#   DOTNET=/usr/bin/dotnet PYTHON=/usr/bin/python3 tools/run_w0_checks.sh
# Environment overrides:
#   REPO_ROOT  repository root (default: the parent directory of this script)
#   ARTIFACTS  raw log directory (default: $REPO_ROOT/artifacts/raw/w0)
# Usage:
#   tools/run_w0_checks.sh [--docs-only]
# --docs-only skips the dotnet build and test steps; it is for hosts without an SDK and never replaces the
# full gate. Unity player probing is GC-001's script and is not invoked here.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="${REPO_ROOT:-$(cd "${SCRIPT_DIR}/.." && pwd)}"
DOTNET="${DOTNET:-dotnet}"
PYTHON="${PYTHON:-python3}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/raw/w0}"

DOCS_ONLY=0
for argument in "$@"; do
  case "${argument}" in
    --docs-only)
      DOCS_ONLY=1
      ;;
    -h|--help)
      sed -n '2,20p' "${BASH_SOURCE[0]}"
      exit 0
      ;;
    *)
      echo "unknown argument: ${argument}" >&2
      exit 2
      ;;
  esac
done

if [ ! -f "${REPO_ROOT}/docs/game-core/traceability.json" ]; then
  echo "repository root not found at ${REPO_ROOT} (missing docs/game-core/traceability.json)" >&2
  exit 2
fi

mkdir -p "${ARTIFACTS}"
cd "${REPO_ROOT}"

run_step() {
  local name="$1"
  shift
  local log="${ARTIFACTS}/${name}.log"
  echo "== ${name}: $*"
  set +e
  "$@" 2>&1 | tee "${log}"
  local status="${PIPESTATUS[0]}"
  set -e
  if [ "${status}" -ne 0 ]; then
    echo "== ${name}: FAILED (exit ${status}); raw log: ${log}" >&2
    exit "${status}"
  fi
  echo "== ${name}: ok"
}

run_step validator-self-test "${PYTHON}" tools/validate_game_core_docs.py --self-test
run_step validator "${PYTHON}" tools/validate_game_core_docs.py

if [ "${DOCS_ONLY}" -eq 1 ]; then
  echo "== dotnet steps skipped (--docs-only)"
  exit 0
fi

if ! command -v "${DOTNET}" >/dev/null 2>&1; then
  echo "dotnet not found: set DOTNET to the SDK path or pass --docs-only" >&2
  exit 2
fi

run_step dotnet-build "${DOTNET}" build dotnet/GameCore.sln -c Release
run_step dotnet-test "${DOTNET}" test dotnet/GameCore.sln -c Release --logger trx

echo "== W0 checks complete; raw logs in ${ARTIFACTS}"

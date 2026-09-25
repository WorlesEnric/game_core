#!/usr/bin/env bash
# GC-014 live lifecycle and required-service closure.
#
# Runs the parts of the qualification that GC-014 owns: the plain-dotnet solution (which compiles the Unity-free
# lifecycle sources and runs the pure lifecycle suites), Unity package resolution, and every EditMode testable -
# the lifecycle suites are EditMode world tests, so they are covered by that step rather than by a new probe. The
# two family lifecycle scenarios build real owned worlds, so no player probe is added by this gate; the family
# probes and the wave gates remain the owners of the player surface.
#
# Required environment:
#   UNITY   absolute path to the Unity Editor executable of the pinned 6000.0.75f1 baseline, e.g.
#           ~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
#
# Optional environment:
#   DOTNET        .NET 8 SDK executable (default: dotnet on PATH)
#   PYTHON        python3 executable (default: python3 on PATH)
#   UNITY_PROJECT Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS     artifact directory (default: <repo>/artifacts/gc-014)
#
# Exit codes: 0 every step passed; nonzero on the first failing step (2 for a missing prerequisite).
set -euo pipefail

if [[ -z "${UNITY:-}" ]]; then
  echo "UNITY must point to the Unity Editor executable for the pinned 6000.0.75f1 baseline." >&2
  exit 2
fi
if [[ ! -x "${UNITY}" ]]; then
  echo "UNITY=${UNITY} is not an executable file." >&2
  exit 2
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
PYTHON="${PYTHON:-python3}"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/gc-014}"

mkdir -p "${ARTIFACTS}/trx" "${ARTIFACTS}/unity"
cd "${REPO_ROOT}"

echo "== GC-014 lifecycle closure (GC-014) =="
echo "repo      : ${REPO_ROOT}"
echo "unity     : ${UNITY}"
echo "dotnet    : ${DOTNET}"
echo "project   : ${UNITY_PROJECT}"
echo "artifacts : ${ARTIFACTS}"

run_step() {
  local label="$1"
  shift
  echo "-- ${label}: $*"
  "$@"
}

# 1. The Unity-free half: the lifecycle ledgers, the teardown sequencer, the closure delta and their suites.
run_step dotnet-build "${DOTNET}" build dotnet/GameCore.sln -c Release
run_step dotnet-test "${DOTNET}" test dotnet/GameCore.sln -c Release \
  --logger trx --results-directory "${ARTIFACTS}/trx"

# 2. Package resolution before any test runs.
run_step unity-resolve "${UNITY}" \
  -batchmode -nographics -quit \
  -projectPath "${UNITY_PROJECT}" \
  -logFile "${ARTIFACTS}/unity/resolve.log"

# 3. Every EditMode testable, which includes the GC-014 lifecycle suites in both families.
#    Do not add -quit to a test-run command that relies on the runner to finish asynchronously (04 s10).
run_step unity-editmode "${UNITY}" \
  -batchmode -nographics \
  -projectPath "${UNITY_PROJECT}" \
  -runTests -testPlatform EditMode \
  -testResults "${ARTIFACTS}/unity/editmode-results.xml" \
  -logFile "${ARTIFACTS}/unity/editmode.log"

# 4. The lifecycle suites alone, isolated, so a lifecycle failure is readable without the whole suite.
run_step unity-lifecycle-only "${UNITY}" \
  -batchmode -nographics \
  -projectPath "${UNITY_PROJECT}" \
  -runTests -testPlatform EditMode -testFilter GameCore.Lifecycle.Tests \
  -testResults "${ARTIFACTS}/unity/lifecycle-results.xml" \
  -logFile "${ARTIFACTS}/unity/lifecycle-editmode.log"

# 5. Documentation gate of the same revision.
"${PYTHON}" tools/validate_game_core_docs.py --self-test | tee "${ARTIFACTS}/validator-self-test.log"
"${PYTHON}" tools/validate_game_core_docs.py | tee "${ARTIFACTS}/validator.log"

echo "== GC-014 lifecycle closure PASSED =="
echo "test results: ${ARTIFACTS}/unity/editmode-results.xml, ${ARTIFACTS}/unity/lifecycle-results.xml"
echo "note: every 'Pass' above is a reported test-runner result; the XML files are the evidence."

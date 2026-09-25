#!/usr/bin/env bash
# GC-008 gate: publish a derived assembly into a real Entities world.
#
# Runs the two halves of this task's evidence on the build host:
#
#   1. the plain-dotnet solution (the pure planner, the plan state machine, migration scratch, acquisitions and the
#      target slot ledger), then
#   2. the Unity EditMode suites of the testable packages, filtering this task's own assembly first so a failure
#      names GC-008 rather than a sibling task.
#
# It deliberately does not rebuild the IL2CPP player: GC-008 adds no new player probe, and the assembly path is
# exercised in an actual Entities world by `GameCore.Unity.Assembly.Tests` (EditMode) with real systems, real jobs
# and real storage. Add `FULL=1` to also run the whole EditMode suite and the documentation validator.
#
# Required environment:
#   UNITY   absolute path to the Unity Editor executable of the pinned 6000.0.75f1 baseline, e.g.
#           ~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
#
# Optional environment:
#   DOTNET         .NET 8 SDK executable (default: dotnet on PATH)
#   PYTHON         python3 executable (default: python3 on PATH)
#   UNITY_PROJECT  Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS      artifact directory (default: <repo>/artifacts/gc-008)
#   FULL           non-empty to also run the whole EditMode suite and the documentation validator
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
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/gc-008}"

mkdir -p "${ARTIFACTS}/trx" "${ARTIFACTS}/unity"
cd "${REPO_ROOT}"

echo "== GC-008 gate: derived assembly publication =="
echo "repo      : ${REPO_ROOT}"
echo "unity     : ${UNITY}"
echo "dotnet    : ${DOTNET}"
echo "project   : ${UNITY_PROJECT}"
echo "artifacts : ${ARTIFACTS}"

run_step() {
  local name="$1"
  shift
  local log="${ARTIFACTS}/${name}.log"
  echo "-- ${name}: $*"
  local status=0
  "$@" >"${log}" 2>&1 || status=$?
  if [[ "${status}" -ne 0 ]]; then
    echo "   FAILED (exit ${status}); log: ${log}" >&2
    exit "${status}"
  fi
  echo "   ok (log: ${log})"
}

# 1. The whole plain-dotnet solution: the pure planner and its tests are in it, and every other project must keep
#    compiling against the unchanged contracts.
run_step dotnet-build "${DOTNET}" build dotnet/GameCore.sln -c Release
run_step dotnet-test "${DOTNET}" test dotnet/GameCore.sln -c Release \
  --logger trx --results-directory "${ARTIFACTS}/trx"

# 2. Package resolution, then this task's EditMode assembly. Do not add -quit to a test-run command (04 s10).
run_step unity-resolve "${UNITY}" \
  -batchmode -nographics -quit \
  -projectPath "${UNITY_PROJECT}" \
  -logFile "${ARTIFACTS}/unity/resolve.log"

run_step unity-assembly-editmode "${UNITY}" \
  -batchmode -nographics \
  -projectPath "${UNITY_PROJECT}" \
  -runTests -testPlatform EditMode -testFilter GameCore.Unity.Assembly.Tests \
  -testResults "${ARTIFACTS}/unity/assembly-editmode.xml" \
  -logFile "${ARTIFACTS}/unity/assembly-editmode.log"

run_step unity-planning-editmode "${UNITY}" \
  -batchmode -nographics \
  -projectPath "${UNITY_PROJECT}" \
  -runTests -testPlatform EditMode -testFilter GameCore.Planning.Tests \
  -testResults "${ARTIFACTS}/unity/planning-editmode.xml" \
  -logFile "${ARTIFACTS}/unity/planning-editmode.log"

if [[ -n "${FULL:-}" ]]; then
  run_step unity-editmode-all "${UNITY}" \
    -batchmode -nographics \
    -projectPath "${UNITY_PROJECT}" \
    -runTests -testPlatform EditMode \
    -testResults "${ARTIFACTS}/unity/editmode-results.xml" \
    -logFile "${ARTIFACTS}/unity/editmode.log"

  run_step unity-playmode "${UNITY}" \
    -batchmode -nographics \
    -projectPath "${UNITY_PROJECT}" \
    -runTests -testPlatform PlayMode \
    -testResults "${ARTIFACTS}/unity/playmode-results.xml" \
    -logFile "${ARTIFACTS}/unity/playmode.log"

  run_step docs-self-test "${PYTHON}" tools/validate_game_core_docs.py --self-test
  run_step docs-validation "${PYTHON}" tools/validate_game_core_docs.py
fi

echo "== GC-008 gate PASSED =="
echo "test results: ${ARTIFACTS}/unity/assembly-editmode.xml, ${ARTIFACTS}/unity/planning-editmode.xml"
echo "note: every 'Pass' above is a reported process result; the XML logs and TRX files are the evidence."

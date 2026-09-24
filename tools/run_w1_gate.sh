#!/usr/bin/env bash
# Wave 1 integration gate (W1-GATE).
#
# Runs, in order:
#   1. the whole plain-dotnet solution: build and test (every project, including the GC-003, GC-004 and GC-005
#      projects, all of which now compile against the production GameCore.Contracts assembly),
#   2. Unity package resolution on the qualification project (refreshes packages-lock.json),
#   3. the Unity EditMode suite of every testable package plus the W1 gate's own EditMode assembly,
#   4. the Unity PlayMode suite of the testable packages,
#   5. the StandaloneLinux64 IL2CPP player build (with catalog code generation) and
#   6. every player probe: GC-001 positive and negative, GC-005 world dispatch, and the W1 gate.
#
# Required environment:
#   UNITY   absolute path to the Unity Editor executable of the pinned 6000.0.75f1 baseline, e.g.
#           ~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
#
# Optional environment:
#   DOTNET        .NET 8 SDK executable (default: dotnet on PATH)
#   PYTHON        python3 executable (default: python3 on PATH)
#   UNITY_PROJECT Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS     artifact directory (default: <repo>/artifacts/w1-gate)
#
# Exit codes: 0 every step passed; nonzero on the first failing step (2 for a missing prerequisite).
set -euo pipefail

if [[ -z "${UNITY:-}" ]]; then
  echo "UNITY must point to the Unity Editor executable for the pinned 6000.0.75f1 baseline." >&2
  echo "The W1 gate is an integrated gate: it is never claimed from the dotnet half alone." >&2
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
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/w1-gate}"
PROBE_PLAYER="${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64"

mkdir -p "${ARTIFACTS}/trx" "${ARTIFACTS}/unity" "${ARTIFACTS}/toolchain"
cd "${REPO_ROOT}"

echo "== W1 integration gate (W1-GATE) =="
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

# 1. The whole plain-dotnet solution, both halves of the gate's Unity-free code.
run_step dotnet-build "${DOTNET}" build dotnet/GameCore.sln -c Release
run_step dotnet-test "${DOTNET}" test dotnet/GameCore.sln -c Release \
  --logger trx --results-directory "${ARTIFACTS}/trx"

# 2. Package resolution: the three Wave 1 packages must resolve before any test runs.
run_step unity-resolve "${UNITY}" \
  -batchmode -nographics -quit \
  -projectPath "${UNITY_PROJECT}" \
  -logFile "${ARTIFACTS}/unity/resolve.log"

# 3. EditMode tests: every testable package (the manifest's "testables": com.gamecore.unity.adapters,
#    com.gamecore.unity.runtime, com.gamecore.composition) plus GameCore.W1Gate.Tests.
#    Do not add -quit to a test-run command that relies on the runner to finish asynchronously (04 s10).
run_step unity-editmode "${UNITY}" \
  -batchmode -nographics \
  -projectPath "${UNITY_PROJECT}" \
  -runTests -testPlatform EditMode \
  -testResults "${ARTIFACTS}/unity/editmode-results.xml" \
  -logFile "${ARTIFACTS}/unity/editmode.log"

# 4. PlayMode tests: PlayerLoop installation, the application pump, reset/disposal and idle routing.
run_step unity-playmode "${UNITY}" \
  -batchmode -nographics \
  -projectPath "${UNITY_PROJECT}" \
  -runTests -testPlatform PlayMode \
  -testResults "${ARTIFACTS}/unity/playmode-results.xml" \
  -logFile "${ARTIFACTS}/unity/playmode.log"

# 5. Build-time code generation and the StandaloneLinux64 IL2CPP player with High managed stripping.
UNITY="${UNITY}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" \
  "tools/unity/build_probe.sh"

# The committed generated probe catalog must be byte-identical to what the compiler just wrote (GC-003 owns it).
if ! git diff --exit-code -- unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs; then
  echo "the committed generated catalog changed during codegen; commit the regenerated file." >&2
  exit 1
fi

# 6. Player probes, each in its own process invocation of the same built player.
PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" \
  "tools/unity/run_probe.sh" both
PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" \
  "tools/unity/run_world_probe.sh"
PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" \
  "tools/unity/run_w1_gate_probe.sh"

# 7. Documentation gate of the same revision.
"${PYTHON}" tools/validate_game_core_docs.py --self-test | tee "${ARTIFACTS}/validator-self-test.log"
"${PYTHON}" tools/validate_game_core_docs.py | tee "${ARTIFACTS}/validator.log"

echo "== W1 integration gate PASSED =="
echo "player      : ${PROBE_PLAYER}"
echo "probe JSON  : ${ARTIFACTS}/toolchain/probe-result.json (GC-001 positive)"
echo "              ${ARTIFACTS}/toolchain/probe-negative.json (GC-001 negative)"
echo "              ${ARTIFACTS}/toolchain/probe-world-dispatch.json (GC-005)"
echo "              ${ARTIFACTS}/toolchain/probe-w1-gate.json (W1-GATE)"
echo "test results: ${ARTIFACTS}/unity/editmode-results.xml, ${ARTIFACTS}/unity/playmode-results.xml"
echo "note: every 'Pass' above is a reported process result; the XML logs and JSON results are the evidence."

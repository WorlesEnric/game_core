#!/usr/bin/env bash
# Wave 4 integration gate (W4-GATE).
#
# Runs, in order:
#   1. the whole plain-dotnet solution: build and test (every Unity-free assembly of both families and the kernel),
#   2. Unity package resolution on the qualification project (refreshes packages-lock.json),
#   3. the Unity EditMode suite of every testable package plus every gate assembly, including
#      `GameCore.W4Gate.Tests` — the EditMode half of this gate,
#   4. the Unity PlayMode suite of the testable packages,
#   5. the StandaloneLinux64 IL2CPP player build (with catalog code generation for both committed catalogs) and the
#      byte-identity check of both committed catalogs,
#   6. every player probe, each executed PROBE_RUNS times (default 5): GC-001 positive and negative, GC-005 world
#      dispatch, the W1 gate, the W2 gate, the GC-010 narrative slice, the GC-011 card slice, the W3 gate, the GC-012
#      Wave 4 profile gate, the GC-013 transition probe and this gate's own `-probeW4Gate`,
#   7. the documentation validator.
#
# Wave 4's exit condition is the guide's own sentence: "Run both families in IL2CPP, then integrate indexed move/mode
# changes, service-closure lifecycle and slot policies. Demonstrate both mode directions, a subtree move preserving
# state, suspend/resume and provider loss/unload." The `-probeW4Gate` mode is the one that runs all four Wave 4 tasks
# together in a single stripped player process over two catalogs per family, so the gate is never claimed from the
# dotnet half alone and never substitutes a seam fixture for a real module.
#
# Timeouts. The Unity Editor has a known, unresolved intermittent hang before it dispatches a batchmode command, so
# EVERY Unity and player invocation below is wrapped in `timeout`. A timed-out invocation fails loudly (exit 124 is
# never a pass), and for the Unity Editor steps the script retries a timed-out invocation exactly ONCE, logging the
# retry, because that failure mode is not deterministic and a second attempt is the honest way to distinguish it from
# a real failure. A step that times out twice fails the gate. Player probe runs are not retried here: probe_runs.sh
# already runs each probe PROBE_RUNS times and fails the gate if any run is not clean.
#
# Required environment:
#   UNITY   absolute path to the Unity Editor executable of the pinned 6000.0.75f1 baseline, e.g.
#           ~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
#
# Optional environment:
#   DOTNET          .NET 8 SDK executable (default: dotnet on PATH)
#   PYTHON          python3 executable (default: python3 on PATH)
#   UNITY_PROJECT   Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS       artifact directory (default: <repo>/artifacts/w4-gate)
#   PROBE_RUNS      repetitions of every player probe (default 5; any crashing run fails the gate)
#   UNITY_TIMEOUT   seconds a full Unity Editor invocation may take (default 1800)
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
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/w4-gate}"
PROBE_PLAYER="${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64"
PROBE_RUNS="${PROBE_RUNS:-5}"
UNITY_TIMEOUT="${UNITY_TIMEOUT:-1800}"

mkdir -p "${ARTIFACTS}/trx" "${ARTIFACTS}/unity" "${ARTIFACTS}/toolchain"
cd "${REPO_ROOT}"

echo "== Wave 4 integration gate (W4-GATE) =="
echo "repo         : ${REPO_ROOT}"
echo "unity        : ${UNITY}"
echo "dotnet       : ${DOTNET}"
echo "project      : ${UNITY_PROJECT}"
echo "artifacts    : ${ARTIFACTS}"
echo "probe runs   : ${PROBE_RUNS}"
echo "unity timeout: ${UNITY_TIMEOUT}s (one retry on a timeout)"

run_step() {
  local label="$1"
  shift
  echo "-- ${label}: $*"
  "$@"
}

# One Unity Editor invocation, wrapped in `timeout` and retried exactly once when it times out. A timeout that
# happens twice is a failure; every other non-zero exit is a failure immediately (a real compile or test error is
# never retried).
unity_step() {
  local label="$1"
  shift

  local attempt rc=0
  for attempt in 1 2; do
    if (( attempt == 2 )); then
      echo "-- ${label}: retrying once after a timeout" >&2
    fi

    rc=0
    timeout --signal=TERM --kill-after=60 "${UNITY_TIMEOUT}" "$@" || rc=$?
    if (( rc == 0 )); then
      return 0
    fi

    if (( rc == 124 || rc == 137 )); then
      echo "   FAIL ${label}: Unity Editor timed out after ${UNITY_TIMEOUT}s (exit ${rc}, attempt ${attempt}/2)" >&2
      continue
    fi

    echo "   FAIL ${label}: Unity Editor exited ${rc}" >&2
    return "${rc}"
  done

  echo "   FAIL ${label}: Unity Editor timed out twice; this is not the known intermittent pre-dispatch hang" >&2
  return 1
}

# 1. The whole plain-dotnet solution: the Unity-free half of the kernel plus both families' rules packages.
run_step dotnet-build "${DOTNET}" build dotnet/GameCore.sln -c Release
run_step dotnet-test "${DOTNET}" test dotnet/GameCore.sln -c Release \
  --logger trx --results-directory "${ARTIFACTS}/trx"

# 2. Package resolution: every Wave 4 package must resolve before any test runs.
unity_step unity-resolve "${UNITY}" \
  -batchmode -nographics -quit \
  -projectPath "${UNITY_PROJECT}" \
  -logFile "${ARTIFACTS}/unity/resolve.log"

# 3. EditMode tests: every testable package plus the W1, W2, W3, GC-012, GC-013 and W4 gate assemblies.
#    Do not add -quit to a test-run command that relies on the runner to finish asynchronously (04 s10).
unity_step unity-editmode "${UNITY}" \
  -batchmode -nographics \
  -projectPath "${UNITY_PROJECT}" \
  -runTests -testPlatform EditMode \
  -testResults "${ARTIFACTS}/unity/editmode-results.xml" \
  -logFile "${ARTIFACTS}/unity/editmode.log"

# 4. PlayMode tests: PlayerLoop installation, the application pump, reset/disposal and idle routing.
unity_step unity-playmode "${UNITY}" \
  -batchmode -nographics \
  -projectPath "${UNITY_PROJECT}" \
  -runTests -testPlatform PlayMode \
  -testResults "${ARTIFACTS}/unity/playmode-results.xml" \
  -logFile "${ARTIFACTS}/unity/playmode.log"

# 5. Build-time code generation for BOTH families' catalogs, then the StandaloneLinux64 IL2CPP player with High
#    managed stripping. build_probe.sh regenerates the probe catalog (GC-003) before it builds; the card catalog has
#    its own batchmode entry point (GC-011) and is regenerated here so the build compiles the fresh file.
unity_step card-catalog-codegen "${UNITY}" \
  -batchmode -nographics -quit \
  -projectPath "${UNITY_PROJECT}" \
  -executeMethod GameCore.Validation.Editor.CardCatalogGenerator.GenerateCatalog \
  -logFile "${ARTIFACTS}/unity/card-codegen.log"

UNITY="${UNITY}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" \
  "tools/unity/build_probe.sh"

# Both committed catalogs must be byte-identical to what the production compiler just wrote.
if ! git diff --exit-code -- unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs; then
  echo "the committed probe catalog differs from a fresh generation; commit the regenerated file" >&2
  exit 1
fi
if ! git diff --exit-code -- unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCards/CardCatalog.g.cs; then
  echo "the committed card catalog differs from a fresh generation; commit the regenerated file" >&2
  exit 1
fi

# 6. Player probes, each in its own process invocation of the same built player, PROBE_RUNS times each.
export PROBE_PLAYER UNITY_PROJECT PROBE_RUNS
PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" PROBE_RUNS="${PROBE_RUNS}" \
  "tools/unity/run_probe.sh" both
PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" PROBE_RUNS="${PROBE_RUNS}" \
  "tools/unity/run_world_probe.sh"
PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" PROBE_RUNS="${PROBE_RUNS}" \
  "tools/unity/run_w1_gate_probe.sh"
PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" PROBE_RUNS="${PROBE_RUNS}" \
  "tools/unity/run_w2_gate_probe.sh"
PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" PROBE_RUNS="${PROBE_RUNS}" \
  "tools/unity/run_narrative_probe.sh"
PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" PROBE_RUNS="${PROBE_RUNS}" \
  "tools/unity/run_cards_probe.sh"
PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" PROBE_RUNS="${PROBE_RUNS}" \
  "tools/unity/run_w3_gate_probe.sh"
PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" PROBE_RUNS="${PROBE_RUNS}" \
  "tools/unity/run_w4_profile_probe.sh"
PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" PROBE_RUNS="${PROBE_RUNS}" \
  "tools/unity/run_gc013_probe.sh"
PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" PROBE_RUNS="${PROBE_RUNS}" \
  "tools/unity/run_w4_gate_probe.sh"

# 7. Documentation gate of the same revision.
"${PYTHON}" tools/validate_game_core_docs.py --self-test | tee "${ARTIFACTS}/validator-self-test.log"
"${PYTHON}" tools/validate_game_core_docs.py | tee "${ARTIFACTS}/validator.log"

echo "== Wave 4 integration gate PASSED =="
echo "player      : ${PROBE_PLAYER}"
echo "probe JSON  : ${ARTIFACTS}/toolchain/probe-result.json (GC-001 positive)"
echo "              ${ARTIFACTS}/toolchain/probe-w3-gate.json (W3-GATE)"
echo "              ${ARTIFACTS}/toolchain/probe-w4-profile.json (GC-012)"
echo "              ${ARTIFACTS}/toolchain/probe-gc013.json (GC-013)"
echo "              ${ARTIFACTS}/toolchain/probe-w4-gate.json (W4-GATE, this gate)"
echo "test results: ${ARTIFACTS}/unity/editmode-results.xml, ${ARTIFACTS}/unity/playmode-results.xml"
echo "note: every 'Pass' above is a reported process result; the XML logs and JSON results are the evidence."

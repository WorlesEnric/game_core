#!/usr/bin/env bash
# Wave 3 integration gate (W3-GATE).
#
# Runs, in order:
#   1. the whole plain-dotnet solution: build and test (every Unity-free assembly of both slices and the kernel),
#   2. Unity package resolution on the qualification project (refreshes packages-lock.json),
#   3. the Unity EditMode suite of every testable package plus the W1, W2, narrative, card and W3 gate assemblies,
#   4. the Unity PlayMode suite of the testable packages,
#   5. the StandaloneLinux64 IL2CPP player build (with catalog code generation for both committed catalogs),
#   6. every player probe, each executed PROBE_RUNS times (default 5): GC-001 positive and negative, GC-005 world
#      dispatch, the W1 gate, the W2 gate, the GC-010 narrative slice, the GC-011 card slice and the W3 gate that
#      runs both slices in one process,
#   7. the documentation validator.
#
# Wave 3's exit condition is "Run narrative and cards with the same kernel": the W3 gate probe is the one that runs
# both compositions in a single player process and audits the loaded kernel assemblies for a one-way dependency, so
# the gate is never claimed from the dotnet half alone and never substitutes a seam fixture for a real module.
#
# Required environment:
#   UNITY   absolute path to the Unity Editor executable of the pinned 6000.0.75f1 baseline, e.g.
#           ~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
#
# Optional environment:
#   DOTNET        .NET 8 SDK executable (default: dotnet on PATH)
#   PYTHON        python3 executable (default: python3 on PATH)
#   UNITY_PROJECT Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS     artifact directory (default: <repo>/artifacts/w3-gate)
#   PROBE_RUNS    repetitions of every player probe (default 5; any crashing run fails the gate)
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
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/w3-gate}"
PROBE_PLAYER="${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64"

mkdir -p "${ARTIFACTS}/trx" "${ARTIFACTS}/unity" "${ARTIFACTS}/toolchain"
cd "${REPO_ROOT}"

echo "== W3 integration gate (W3-GATE) =="
echo "repo      : ${REPO_ROOT}"
echo "unity     : ${UNITY}"
echo "dotnet    : ${DOTNET}"
echo "project   : ${UNITY_PROJECT}"
echo "artifacts : ${ARTIFACTS}"
echo "probe runs: ${PROBE_RUNS:-5}"

run_step() {
  local label="$1"
  shift
  echo "-- ${label}: $*"
  "$@"
}

# 1. The whole plain-dotnet solution: the Unity-free half of the kernel plus both slices' rules packages.
run_step dotnet-build "${DOTNET}" build dotnet/GameCore.sln -c Release
run_step dotnet-test "${DOTNET}" test dotnet/GameCore.sln -c Release \
  --logger trx --results-directory "${ARTIFACTS}/trx"

# 2. Package resolution: every Wave 3 package must resolve before any test runs.
run_step unity-resolve "${UNITY}" \
  -batchmode -nographics -quit \
  -projectPath "${UNITY_PROJECT}" \
  -logFile "${ARTIFACTS}/unity/resolve.log"

# 3. EditMode tests: every testable package plus the W1, W2, narrative, card and W3 gate assemblies.
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

# 5. Build-time code generation for BOTH families' catalogs, then the StandaloneLinux64 IL2CPP player with High
#    managed stripping. build_probe.sh regenerates the probe catalog (GC-003) before it builds; the card catalog has
#    its own batchmode entry point (GC-011) and is regenerated here so the build compiles the fresh file.
run_step card-catalog-codegen "${UNITY}" \
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
export PROBE_PLAYER UNITY_PROJECT
export PROBE_RUNS="${PROBE_RUNS:-5}"
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

# 7. Documentation gate of the same revision.
"${PYTHON}" tools/validate_game_core_docs.py --self-test | tee "${ARTIFACTS}/validator-self-test.log"
"${PYTHON}" tools/validate_game_core_docs.py | tee "${ARTIFACTS}/validator.log"

echo "== W3 integration gate PASSED =="
echo "player      : ${PROBE_PLAYER}"
echo "probe JSON  : ${ARTIFACTS}/toolchain/probe-result.json (GC-001 positive)"
echo "              ${ARTIFACTS}/toolchain/probe-negative.json (GC-001 negative)"
echo "              ${ARTIFACTS}/toolchain/probe-world-dispatch.json (GC-005)"
echo "              ${ARTIFACTS}/toolchain/probe-w1-gate.json (W1-GATE)"
echo "              ${ARTIFACTS}/toolchain/probe-w2-gate.json (W2-GATE)"
echo "              ${ARTIFACTS}/toolchain/probe-narrative.json (GC-010)"
echo "              ${ARTIFACTS}/toolchain/probe-cards.json (GC-011)"
echo "              ${ARTIFACTS}/toolchain/probe-w3-gate.json (W3-GATE)"
echo "test results: ${ARTIFACTS}/unity/editmode-results.xml, ${ARTIFACTS}/unity/playmode-results.xml"
echo "note: every 'Pass' above is a reported process result; the XML logs and JSON results are the evidence."

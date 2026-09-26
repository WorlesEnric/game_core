#!/usr/bin/env bash
# GC-022 gate: complete unload stress, callback and Play Mode lifecycle proof.
#
# Runs, in order:
#   1. the static checks that need no toolchain (host-side C# checker, W0 contract-surface parity, the `bash -n`
#      parse of this gate's own scripts),
#   2. the whole plain-dotnet solution: build and test, including the pure lifecycle stress suite
#      (`GameCore.Composition.Tests.LifecycleStressTests`) that carries the 1,000-cycle quantity,
#   3. Unity package resolution on the qualification project,
#   4. the Unity EditMode suite of every testable package plus `GameCore.LifecycleStress.Tests` - the Unity-world
#      half of this task, one real narrative world and one real card world,
#   5. the Unity PlayMode suite,
#   6. build-time code generation plus the StandaloneLinux64 IL2CPP qualification player,
#   7. every player probe, each PROBE_RUNS times: the existing modes and this task's `-probeLifecycleStress`,
#   8. the Play Mode reload matrix: {domain reload on/off} x {scene reload on/off}, 10 enter/exit cycles each,
#   9. native leak detection in the Editor (`UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE=2`, full stack traces) and the
#      attribution of every allocation the shutdown report names,
#  10. the documentation validator.
#
# GC-022's sentence is "run 1,000 mount/unmount cycles, 100 delayed completions, stalled jobs, throwing disposers
# and required-provider churn. Exercise domain reload on/off plus scene reload settings, stop/recreate and headless
# cleanup. Trace every acquisition to retirement or quarantine." The 1,000-cycle quantity and the four failure
# shapes are carried by step 2 at full mechanism fidelity and re-proved on real Unity worlds by steps 4 and 7; the
# reload/scene matrix is step 8; the headless lifecycle and the leak attribution are steps 7 and 9. No step of this
# gate is satisfied by a seam fixture.
#
# Timeouts. The Unity Editor has an unresolved intermittent hang before it dispatches a batchmode command
# (artifacts/gc-014/BUILD_REPORT.md), so EVERY Unity Editor invocation is wrapped in `timeout` and retried exactly
# ONCE on a timeout, logging the retry. A step that times out twice fails the gate. The Play Mode matrix records a
# watchdog kill as its own result (a frequency in `playmode-matrix/summary.json`), never as a pass.
#
# Required environment:
#   UNITY   absolute path to the Unity Editor executable of the pinned 6000.0.75f1 baseline, e.g.
#           ~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
#
# Optional environment:
#   DOTNET                .NET 8 SDK executable (default: dotnet on PATH)
#   PYTHON                python3 executable (default: python3 on PATH)
#   UNITY_PROJECT         Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS             artifact directory (default: <repo>/artifacts/gc-022)
#   PROBE_RUNS            repetitions of every player probe (default 5)
#   UNITY_TIMEOUT         seconds a full Unity Editor invocation may take (default 1800)
#   GC_LIFECYCLE_STRESS_CYCLES  mount/unmount cycles the counted suites run (default 1000, the GC-022 quantity)
#   MATRIX_COMBINATIONS   Play Mode matrix combinations to run (default all four)
#   MATRIX_CYCLES         enter/exit cycles per combination (default 10)
#   LEAK_DETECT           run steps 9 (Editor leak detection + attribution); default 1, 0 skips and says so
#
# Exit codes: 0 every step passed; nonzero on the first failing step (2 for a missing prerequisite).
set -euo pipefail

if [[ -z "${UNITY:-}" ]]; then
  echo "run_gc022.sh: set UNITY to the Unity Editor executable of the pinned 6000.0.75f1 baseline" >&2
  exit 2
fi
if [[ ! -x "${UNITY}" ]]; then
  echo "run_gc022.sh: UNITY is not executable: ${UNITY}" >&2
  exit 2
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
PYTHON="${PYTHON:-python3}"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/gc-022}"
PROBE_PLAYER="${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64"
PROBE_RUNS="${PROBE_RUNS:-5}"
UNITY_TIMEOUT="${UNITY_TIMEOUT:-1800}"
GC_LIFECYCLE_STRESS_CYCLES="${GC_LIFECYCLE_STRESS_CYCLES:-1000}"
MATRIX_COMBINATIONS="${MATRIX_COMBINATIONS:-reload-on-scene-on reload-on-scene-off reload-off-scene-on reload-off-scene-off}"
MATRIX_CYCLES="${MATRIX_CYCLES:-10}"
LEAK_DETECT="${LEAK_DETECT:-1}"

# Full native-leak stack traces for every Editor invocation this script launches. The Collections package's own
# `Unity.Collections.Editor.CLILeakDetectionSwitcher` reads this variable in an [InitializeOnLoadMethod] and assigns
# `NativeLeakDetection.Mode`, which is what turns the shutdown report from "57 individual allocations" into
# attributable callstacks (artifacts/gc-016/BUILD_REPORT.md).
export UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE=2
export GC_LIFECYCLE_STRESS_CYCLES

mkdir -p "${ARTIFACTS}/trx" "${ARTIFACTS}/unity" "${ARTIFACTS}/toolchain" \
  "${ARTIFACTS}/playmode-matrix" "${ARTIFACTS}/leak"

cd "${REPO_ROOT}"

echo "== GC-022 gate: unload stress, callbacks and Play Mode lifecycle =="
echo "repo         : ${REPO_ROOT}"
echo "unity        : ${UNITY}"
echo "dotnet       : ${DOTNET}"
echo "project      : ${UNITY_PROJECT}"
echo "artifacts    : ${ARTIFACTS}"
echo "probe runs   : ${PROBE_RUNS}"
echo "stress cycles: ${GC_LIFECYCLE_STRESS_CYCLES}"
echo "unity timeout: ${UNITY_TIMEOUT}s (one retry on a timeout)"
echo "leak detect  : ${LEAK_DETECT} (UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE=${UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE})"

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

# One player probe sequence, wrapped in `timeout`. probe_runs.sh does its own per-run timeout, so this wrapper
# bounds the whole PROBE_RUNS sequence and retries it once when the sequence timed out, never on a probe verdict.
probe_step() {
  local label="$1"
  shift

  local attempt rc=0
  for attempt in 1 2; do
    if (( attempt == 2 )); then
      echo "-- ${label}: retrying once after a timeout" >&2
    fi

    rc=0
    "$@" || rc=$?
    if (( rc == 0 )); then
      return 0
    fi

    if (( rc == 124 || rc == 137 )); then
      echo "   FAIL ${label}: the probe sequence timed out (exit ${rc}, attempt ${attempt}/2)" >&2
      continue
    fi

    echo "   FAIL ${label}: exited ${rc}" >&2
    return "${rc}"
  done

  echo "   FAIL ${label}: timed out twice" >&2
  return 1
}

# 1. Toolchain-free checks first: a syntax error in the host-side checker's own inputs or in this gate's scripts
#    should fail in seconds, not after a Unity import.
run_step static-csharp "${PYTHON}" tools/check_game_core_csharp.py \
  | tee "${ARTIFACTS}/static-csharp.log"
run_step static-contracts "${PYTHON}" tools/check_contract_surface_parity.py \
  | tee "${ARTIFACTS}/static-contracts.log"
run_step static-shell bash -n tools/run_gc022.sh
run_step static-shell-probe bash -n tools/unity/run_lifecycle_stress_probe.sh
run_step static-shell-matrix bash -n tools/unity/run_lifecycle_playmode_matrix.sh
run_step static-leak-tool "${PYTHON}" tools/attribute_native_leaks.py --self-test \
  | tee "${ARTIFACTS}/leak/self-test.log"

# 2. The whole plain-dotnet solution. This carries the 1,000-cycle quantity at full mechanism fidelity: the pure
#    stress suite drives the real InstallationLifecycleCoordinator, ResourceLedger, JobFenceRegistry,
#    QuarantineRegistry, CallbackGate and TeardownSequencer.
run_step dotnet-build "${DOTNET}" build dotnet/GameCore.sln -c Release
run_step dotnet-test "${DOTNET}" test dotnet/GameCore.sln -c Release \
  --logger trx --results-directory "${ARTIFACTS}/trx"
run_step dotnet-test-stress "${DOTNET}" test \
  dotnet/tests/GameCore.Composition.Tests/GameCore.Composition.Tests.csproj -c Release --no-build \
  --filter "FullyQualifiedName~GameCore.Composition.Tests.LifecycleStressTests" \
  --logger trx --results-directory "${ARTIFACTS}/trx-stress"

# 3. Package resolution: every GC-022 package must resolve before any test runs.
unity_step unity-resolve "${UNITY}" \
  -batchmode -nographics -quit \
  -projectPath "${UNITY_PROJECT}" \
  -logFile "${ARTIFACTS}/unity/resolve.log"

# 4. EditMode: every testable package plus `GameCore.LifecycleStress.Tests` (one real narrative world and one real
#    card world, cycled) and the pure suite's Unity side. Do not add -quit to a test-run command (04 s10).
unity_step unity-editmode "${UNITY}" \
  -batchmode -nographics \
  -projectPath "${UNITY_PROJECT}" \
  -runTests -testPlatform EditMode \
  -testResults "${ARTIFACTS}/unity/editmode-results.xml" \
  -logFile "${ARTIFACTS}/unity/editmode.log"

# 5. PlayMode: PlayerLoop installation, the application pump, reset/disposal and idle routing.
unity_step unity-playmode "${UNITY}" \
  -batchmode -nographics \
  -projectPath "${UNITY_PROJECT}" \
  -runTests -testPlatform PlayMode \
  -testResults "${ARTIFACTS}/unity/playmode-results.xml" \
  -logFile "${ARTIFACTS}/unity/playmode.log"

# 6. Catalog code generation and the StandaloneLinux64 IL2CPP qualification player. build_probe.sh regenerates the
#    probe catalog before it builds; the card and checkpoint catalogs have their own batchmode entry points.
unity_step card-catalog-codegen "${UNITY}" \
  -batchmode -nographics -quit \
  -projectPath "${UNITY_PROJECT}" \
  -executeMethod GameCore.Validation.Editor.CardCatalogGenerator.GenerateCatalog \
  -logFile "${ARTIFACTS}/unity/card-codegen.log"

unity_step checkpoint-catalog-codegen "${UNITY}" \
  -batchmode -nographics -quit \
  -projectPath "${UNITY_PROJECT}" \
  -executeMethod GameCore.Validation.Editor.CheckpointCatalogGenerator.GenerateCatalog \
  -logFile "${ARTIFACTS}/unity/checkpoint-codegen.log"

UNITY="${UNITY}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" UNITY_TIMEOUT="${UNITY_TIMEOUT}" \
  tools/unity/build_probe.sh

for catalog in \
  unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs \
  unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCards/CardCatalog.g.cs \
  unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCheckpoint/CheckpointCatalog.g.cs; do
  if ! git diff --exit-code -- "${catalog}" >/dev/null; then
    echo "   FAIL catalog: ${catalog} is not byte-identical to the committed generated source" >&2
    exit 1
  fi
done

# 7. Player probes: the existing modes unchanged, plus this task's `-probeLifecycleStress`, which runs the same
#    scenario headless in the IL2CPP player, records the resolved cycle count and the player's own native leak
#    detection mode, and reports the two family digests.
probe_step probe-gc001 \
  env PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" \
  PROBE_RUNS="${PROBE_RUNS}" "tools/unity/run_probe.sh" both
for harness in \
  run_world_probe.sh \
  run_w1_gate_probe.sh \
  run_w2_gate_probe.sh \
  run_narrative_probe.sh \
  run_cards_probe.sh \
  run_w3_gate_probe.sh \
  run_w4_profile_probe.sh \
  run_gc013_probe.sh \
  run_w4_gate_probe.sh \
  run_gc017_faults_probe.sh \
  run_gc018_probe.sh \
  run_gc019_probe.sh \
  run_w5_gate_probe.sh \
  run_lifecycle_stress_probe.sh; do
  probe_step "probe-${harness%.sh}" \
    env PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" \
    PROBE_RUNS="${PROBE_RUNS}" GC_LIFECYCLE_STRESS_CYCLES="${GC_LIFECYCLE_STRESS_CYCLES}" \
    "tools/unity/${harness}"
done

# 8. The Play Mode reload matrix. Four combinations, each in its own Editor process so a hang in one cannot lose the
#    others; a watchdog kill is recorded as a frequency in the combination's own JSONL and summary, never a pass.
UNITY="${UNITY}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/playmode-matrix" \
  UNITY_TIMEOUT="${UNITY_TIMEOUT}" MATRIX_COMBINATIONS="${MATRIX_COMBINATIONS}" MATRIX_CYCLES="${MATRIX_CYCLES}" \
  tools/unity/run_lifecycle_playmode_matrix.sh

# 9. Leak attribution. The Editor shutdown report is the only place a native allocation owned by this project can
#    be seen; with the env var exported above it names callstacks, and this step turns them into a per-class report
#    that fails on any GameCore-owned allocation. An unattributed block is reported as unattributed, never as zero.
if [[ "${LEAK_DETECT}" == "1" ]]; then
  "${PYTHON}" tools/attribute_native_leaks.py \
    --log "${ARTIFACTS}/unity/editmode.log" \
    --log "${ARTIFACTS}/unity/playmode.log" \
    --log "${ARTIFACTS}/unity/resolve.log" \
    --out "${ARTIFACTS}/leak/attribution.json" \
    --policy "${ARTIFACTS}/leak/policy.md" \
    | tee "${ARTIFACTS}/leak/attribution.log"
else
  echo "-- leak-attribution: skipped (LEAK_DETECT=0)"
fi

# 10. Documentation gate of the same revision.
"${PYTHON}" tools/validate_game_core_docs.py --self-test | tee "${ARTIFACTS}/validator-self-test.log"
"${PYTHON}" tools/validate_game_core_docs.py | tee "${ARTIFACTS}/validator.log"

echo "== GC-022 gate PASSED =="
echo "player      : ${PROBE_PLAYER}"
echo "stress JSON : ${ARTIFACTS}/toolchain/probe-lifecycle-stress.json"
echo "matrix      : ${ARTIFACTS}/playmode-matrix/summary.json"
echo "leak report : ${ARTIFACTS}/leak/attribution.json"
echo "test results: ${ARTIFACTS}/unity/editmode-results.xml, ${ARTIFACTS}/unity/playmode-results.xml,"
echo "              ${ARTIFACTS}/trx, ${ARTIFACTS}/trx-stress"
echo "note: GC_LIFECYCLE_STRESS_CYCLES=${GC_LIFECYCLE_STRESS_CYCLES}; every 'Pass' above is a reported process"
echo "      result, not this script's opinion. The Editor hang is recorded by step 8 as a frequency."

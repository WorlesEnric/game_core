#!/usr/bin/env bash
# Wave 5 integration gate (W5-GATE).
#
# Runs, in order:
#   1. the whole plain-dotnet solution: build and test (every Unity-free assembly of both families and the kernel,
#      including GC-016's observation storage and GC-018's checkpoint capture/restore),
#   2. Unity package resolution on the qualification project (refreshes packages-lock.json),
#   3. the Unity EditMode suite of every testable package plus every gate assembly, including
#      `GameCore.W5Gate.Tests` - the EditMode half of this task,
#   4. the Unity PlayMode suite of the testable packages,
#   5. build-time code generation for all three committed catalogs (probe, cards, checkpoint), the byte-identity
#      check of all three, and the StandaloneLinux64 IL2CPP qualification player through tools/unity/build_probe.sh,
#   6. every player probe, each executed PROBE_RUNS times (default 5): GC-001 positive and negative, GC-005 world
#      dispatch, the W1/W2/W3/W4 gates, GC-010 narrative, GC-011 cards, GC-012 profile, GC-013 transitions, GC-017
#      fault boundaries, GC-018 checkpoint round trip, GC-019 adapters and this gate's own `-probeW5Gate`,
#   7. the release surface: the latch sources compiled in BOTH configurations, the source-level release
#      configuration of every runtime file, the qualification switch, then a real marker-free release player built
#      from the cloned validation project (which also proves the gate's own scenario is absent from it), inspected
#      for latch metadata and driven through the family probes,
#   8. the documentation validator.
#
# The gate sentence is "join retained observation, deterministic faults, checkpoint restore and common adapters in
# one actual world; show prewrite rejection, postwrite fail-stop, new-session restore, read-only snapshots and stale
# asset callback rejection". The `-probeW5Gate` mode is the one that runs that join in a single stripped player
# process over two catalogs per family, so the gate is never claimed from the dotnet half alone and never substitutes
# a seam fixture for a real module.
#
# Timeouts. The Unity Editor has a known, unresolved intermittent hang before it dispatches a batchmode command, so
# EVERY Unity invocation is wrapped in `timeout`, and every Unity Editor invocation this script launches directly is
# retried exactly ONCE on a timeout, logging the retry. A step that times out twice fails the gate. Player probe runs
# are not retried: probe_runs.sh already runs each probe PROBE_RUNS times and fails the gate if any run is not clean.
# The steps that delegate to another script call `unity_step` (or the delegate's own wrapper) rather than being
# wrapped twice, because one wrapper around two Unity invocations would share a single budget.
#
# Required environment:
#   UNITY   absolute path to the Unity Editor executable of the pinned 6000.0.75f1 baseline, e.g.
#           ~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
#
# Optional environment:
#   DOTNET          .NET 8 SDK executable (default: dotnet on PATH)
#   PYTHON          python3 executable (default: python3 on PATH)
#   UNITY_PROJECT   Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS       artifact directory (default: <repo>/artifacts/w5-gate)
#   PROBE_RUNS      repetitions of every player probe (default 5; any crashing run fails the gate)
#   UNITY_TIMEOUT   seconds a full Unity Editor invocation may take (default 1800)
#   RELEASE_BUILD   build and inspect a marker-free release player (default 1; 0 skips it and says so)
#   RELEASE_PROJECT the disposable cloned project path (default: <repo>/unity/GameCore.ReleaseCheck)
#   RELEASE_PLAYER  an existing marker-free release player to inspect instead of building one
#   RELEASE_IL2CPP  the IL2CPP generated C++ directory of that release player, if it is not under RELEASE_PLAYER
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
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/w5-gate}"
PROBE_PLAYER="${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64"
PROBE_RUNS="${PROBE_RUNS:-5}"
UNITY_TIMEOUT="${UNITY_TIMEOUT:-1800}"
RELEASE_BUILD="${RELEASE_BUILD:-1}"
RELEASE_PROJECT="${RELEASE_PROJECT:-${REPO_ROOT}/unity/GameCore.ReleaseCheck}"
RELEASE_PLAYER="${RELEASE_PLAYER:-}"
RELEASE_IL2CPP="${RELEASE_IL2CPP:-}"

mkdir -p "${ARTIFACTS}/trx" "${ARTIFACTS}/unity" "${ARTIFACTS}/toolchain" "${ARTIFACTS}/release"
cd "${REPO_ROOT}"

echo "== Wave 5 integration gate (W5-GATE) =="
echo "repo         : ${REPO_ROOT}"
echo "unity        : ${UNITY}"
echo "dotnet       : ${DOTNET}"
echo "project      : ${UNITY_PROJECT}"
echo "artifacts    : ${ARTIFACTS}"
echo "probe runs   : ${PROBE_RUNS}"
echo "unity timeout: ${UNITY_TIMEOUT}s (one retry on a timeout)"
echo "release build: ${RELEASE_BUILD}"

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

# One player probe, wrapped in `timeout`. probe_runs.sh does its own per-run timeout, so this wrapper exists to bound
# the whole PROBE_RUNS sequence and to retry it once when the sequence timed out (never on a probe verdict).
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

# 1. The whole plain-dotnet solution: the Unity-free half of the kernel plus both families' rules packages.
run_step dotnet-build "${DOTNET}" build dotnet/GameCore.sln -c Release
run_step dotnet-test "${DOTNET}" test dotnet/GameCore.sln -c Release \
  --logger trx --results-directory "${ARTIFACTS}/trx"

# 2. Package resolution: every Wave 5 package must resolve before any test runs.
unity_step unity-resolve "${UNITY}" \
  -batchmode -nographics -quit \
  -projectPath "${UNITY_PROJECT}" \
  -logFile "${ARTIFACTS}/unity/resolve.log"

# 3. EditMode tests: every testable package plus every gate assembly, including `GameCore.W5Gate.Tests`, which
#    recomputes both digest literals from `W5GateScenario.QualifiedNames(label)` so a renamed or reordered observation
#    fails the suite instead of shrinking it. Do not add -quit to a test-run command (04 s10).
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

# 5. Build-time code generation for all three committed catalogs, then the StandaloneLinux64 IL2CPP player with High
#    managed stripping. build_probe.sh regenerates the probe catalog (GC-003) before it builds; the card (GC-011) and
#    checkpoint (GC-018) catalogs have their own batchmode entry points and are regenerated here so the build compiles
#    the fresh files and the byte-identity check below is meaningful. Each of build_probe.sh's own two Unity
#    invocations carries its own `timeout`.
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
  "tools/unity/build_probe.sh"

# All three committed catalogs must be byte-identical to what the production compiler just wrote.
for catalog in \
  unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs \
  unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCards/CardCatalog.g.cs \
  unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCheckpoint/CheckpointCatalog.g.cs; do
  if ! git diff --exit-code -- "${catalog}"; then
    echo "the committed catalog ${catalog} differs from a fresh generation; commit the regenerated file" >&2
    exit 1
  fi
  echo "-- catalog byte-identical: ${catalog}"
done

# 6. Player probes, each in its own process invocation of the same built player, PROBE_RUNS times each.
export PROBE_PLAYER UNITY_PROJECT PROBE_RUNS
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
  run_w5_gate_probe.sh; do
  probe_step "probe-${harness}" \
    env PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" \
    PROBE_RUNS="${PROBE_RUNS}" "tools/unity/${harness}"
done

# 7. The release surface. Three always-on halves plus a real marker-free player:
#    the latch sources compiled Release (must be empty) and Qualification (must carry everything), the source-level
#    release configuration of every runtime file, the qualification switch, and - if a release player was built or
#    supplied - its managed assemblies, its generated C++ and the family probes it can still run.
run_step release-surface "${PYTHON}" tools/check_release_fault_free.py \
  --dotnet "${DOTNET}" --json "${ARTIFACTS}/release-surface.json"

if [[ -n "${RELEASE_PLAYER}" ]]; then
  echo "-- release-player: using the supplied player ${RELEASE_PLAYER}"
elif [[ "${RELEASE_BUILD}" == "1" ]]; then
  if [[ -e "${RELEASE_PROJECT}" ]]; then
    echo "   FAIL release-project: ${RELEASE_PROJECT} already exists; remove the disposable clone first" >&2
    exit 1
  fi

  run_step release-project-prepare "${PYTHON}" tools/unity/prepare_gc017_release_project.py
  UNITY="${UNITY}" UNITY_PROJECT="${RELEASE_PROJECT}" ARTIFACTS="${ARTIFACTS}/release" \
    UNITY_TIMEOUT="${UNITY_TIMEOUT}" "tools/unity/build_probe.sh"
  RELEASE_PLAYER="${RELEASE_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64"
else
  echo "-- release-player: NOT RUN (RELEASE_BUILD=0 and no RELEASE_PLAYER)"
  echo "   the compiled-assembly and source halves of the release claim did run; this note is deliberate so the"
  echo "   gate never implies a built release player was inspected when none was supplied."
fi

if [[ -n "${RELEASE_PLAYER}" ]]; then
  if [[ ! -x "${RELEASE_PLAYER}" ]]; then
    echo "   FAIL release-player: ${RELEASE_PLAYER} is not an executable file" >&2
    exit 1
  fi

  if [[ -n "${RELEASE_IL2CPP}" ]]; then
    run_step release-player "${PYTHON}" tools/check_player_fault_free.py \
      --player "$(dirname "${RELEASE_PLAYER}")" --il2cpp "${RELEASE_IL2CPP}" \
      --json "${ARTIFACTS}/release-player-surface.json"
  else
    run_step release-player "${PYTHON}" tools/check_player_fault_free.py \
      --player "$(dirname "${RELEASE_PLAYER}")" --json "${ARTIFACTS}/release-player-surface.json"
  fi

  # The family probes the release player can still run. It cannot run `-probeFaults` or `-probeW5Gate` (both name
  # the latch types and are removed from the clone), which is exactly the point: the release surface is qualified by
  # the modes a shipping build really carries, and the qualification player above runs the rest.
  for harness in \
    run_world_probe.sh \
    run_narrative_probe.sh \
    run_cards_probe.sh \
    run_gc018_probe.sh \
    run_gc019_probe.sh; do
    probe_step "release-probe-${harness}" \
      env PROBE_PLAYER="${RELEASE_PLAYER}" UNITY_PROJECT="${RELEASE_PROJECT}" ARTIFACTS="${ARTIFACTS}/release" \
      PROBE_RUNS="${PROBE_RUNS}" "tools/unity/${harness}"
  done
fi

# 8. Documentation gate of the same revision.
"${PYTHON}" tools/validate_game_core_docs.py --self-test | tee "${ARTIFACTS}/validator-self-test.log"
"${PYTHON}" tools/validate_game_core_docs.py | tee "${ARTIFACTS}/validator.log"

echo "== Wave 5 integration gate PASSED =="
echo "player      : ${PROBE_PLAYER}"
echo "probe JSON  : ${ARTIFACTS}/toolchain/probe-w5-gate.json (W5-GATE, this gate)"
echo "              ${ARTIFACTS}/toolchain/probe-gc017-faults.json (GC-017)"
echo "              ${ARTIFACTS}/toolchain/probe-gc018.json (GC-018)"
echo "              ${ARTIFACTS}/toolchain/probe-gc019.json (GC-019)"
echo "              ${ARTIFACTS}/toolchain/probe-w4-gate.json (W4-GATE)"
echo "test results: ${ARTIFACTS}/unity/editmode-results.xml, ${ARTIFACTS}/unity/playmode-results.xml, ${ARTIFACTS}/trx"
echo "note: PROBE_RUNS=${PROBE_RUNS}; every 'Pass' above is a reported process result, not this script's opinion."
echo "release     : ${ARTIFACTS}/release-surface.json${RELEASE_PLAYER:+ and ${ARTIFACTS}/release-player-surface.json}"
echo "note: the probe JSON is the archived evidence - a Pass here means the player process reported it in"
echo "      ${ARTIFACTS}/toolchain/probe-w5-gate.json, with runs 2..${PROBE_RUNS} retained alongside it."

#!/usr/bin/env bash
# GC-020 real-time action reference gate (GC-020).
#
# Runs, in order:
#   1. the whole plain-dotnet solution: build and test (every Unity-free assembly, including the traversal rules
#      package and the adapter core's physics/animation/audio stages),
#   2. Unity package resolution on the qualification project (the two new traversal packages must resolve),
#   3. the Unity EditMode suite: every testable package, the traversal rules contract suite and the gate assembly
#      `GameCore.Gc020.Tests`, which recomputes the course digest literal from the observation table so a renamed or
#      reordered observation fails the suite instead of shrinking it,
#   4. the Unity PlayMode suite of the testable packages,
#   5. the StandaloneLinux64 IL2CPP qualification player with High managed stripping,
#   6. every player probe, each executed PROBE_RUNS times (default 5): the existing family probes plus this task's
#      own `-probeTraversal`. The card and narrative modes are re-run here on purpose: GC-020's acceptance includes
#      "cards/narrative still contain no action/physics phase", so the gate must show those two genres still run on
#      the same revision that adds the action one (P-001, P-059),
#   7. the release-surface halves (the fault latches stay compiled out of release; this task adds none),
#   8. the documentation validator.
#
# Timeouts. The Unity Editor has a known, unresolved intermittent hang before it dispatches a batchmode command, so
# EVERY Unity invocation is wrapped in `timeout` and every one this script launches directly is retried exactly ONCE
# on a timeout. Player probes are not retried: probe_runs.sh already runs each probe PROBE_RUNS times.
#
# AUDIO. The qualification player is built and launched with `-nographics` and with Unity audio DISABLED (an
# FMOD/PulseAudio crash at exit was the reason, crash-139). Nothing in this gate re-enables audio: the traversal
# audio observation drives `CommittedAudioStage` with the engine-free recording sink, and the Unity audio sink in
# `Runtime/Presentation/UnityOutputSinks.cs` is never constructed here. Documented, not assumed.
#
# Required environment:
#   UNITY   absolute path to the Unity Editor executable of the pinned 6000.0.75f1 baseline, e.g.
#           ~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
#
# Optional environment:
#   DOTNET          .NET 8 SDK executable (default: dotnet on PATH)
#   PYTHON          python3 executable (default: python3 on PATH)
#   UNITY_PROJECT   Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS       artifact directory (default: <repo>/artifacts/gc-020)
#   PROBE_RUNS      repetitions of every player probe (default 5; any crashing run fails the gate)
#   UNITY_TIMEOUT   seconds a full Unity Editor invocation may take (default 1800)
#   DIGEST          the course digest literal the traversal probe must observe (default: read from the gate's own
#                   committed literal file, artifacts/gc-020/toolchain/gc020-digest.txt)
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
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/gc-020}"
PROBE_PLAYER="${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64"
PROBE_RUNS="${PROBE_RUNS:-5}"
UNITY_TIMEOUT="${UNITY_TIMEOUT:-1800}"

mkdir -p "${ARTIFACTS}/trx" "${ARTIFACTS}/unity" "${ARTIFACTS}/toolchain"
cd "${REPO_ROOT}"

echo "== GC-020 real-time action reference gate =="
echo "repo         : ${REPO_ROOT}"
echo "unity        : ${UNITY}"
echo "dotnet       : ${DOTNET}"
echo "project      : ${UNITY_PROJECT}"
echo "artifacts    : ${ARTIFACTS}"
echo "probe runs   : ${PROBE_RUNS}"
echo "unity timeout: ${UNITY_TIMEOUT}s (one retry on a timeout)"
echo "audio        : DISABLED in the qualification player (crash-139); the audio stage is qualified engine-free"

run_step() {
  local label="$1"
  shift
  echo "-- ${label}: $*"
  "$@"
}

# One Unity Editor invocation, wrapped in `timeout` and retried exactly once when it times out.
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

# One player probe sequence, wrapped in `timeout` and retried once on a timeout (never on a probe verdict).
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

# 0. The host-side static checks this authoring host can run, so a syntax or forbidden-construct regression in the
#    new package fails before the build host is involved.
run_step host-csharp-check "${PYTHON}" tools/check_game_core_csharp.py

# 1. The whole plain-dotnet solution.
run_step dotnet-build "${DOTNET}" build dotnet/GameCore.sln -c Release
run_step dotnet-test "${DOTNET}" test dotnet/GameCore.sln -c Release \
  --logger trx --results-directory "${ARTIFACTS}/trx"

# 2. Package resolution: the two traversal packages must resolve before any test runs.
unity_step unity-resolve "${UNITY}" \
  -batchmode -nographics -quit \
  -projectPath "${UNITY_PROJECT}" \
  -logFile "${ARTIFACTS}/unity/resolve.log"

# 3. EditMode: the package suites, the traversal rules contract suite and this task's gate assembly. Do not add
#    -quit to a test-run command (04 s10).
unity_step unity-editmode "${UNITY}" \
  -batchmode -nographics \
  -projectPath "${UNITY_PROJECT}" \
  -runTests -testPlatform EditMode \
  -testResults "${ARTIFACTS}/unity/editmode-results.xml" \
  -logFile "${ARTIFACTS}/unity/editmode.log"

# 4. PlayMode: PlayerLoop installation, the application pump, reset/disposal and idle routing.
unity_step unity-playmode "${UNITY}" \
  -batchmode -nographics \
  -projectPath "${UNITY_PROJECT}" \
  -runTests -testPlatform PlayMode \
  -testResults "${ARTIFACTS}/unity/playmode-results.xml" \
  -logFile "${ARTIFACTS}/unity/playmode.log"

# 5. The StandaloneLinux64 IL2CPP player with High managed stripping. build_probe.sh regenerates the probe catalog
#    before it builds; the traversal course has no committed generated catalog yet (GC-025 owns catalog coverage),
#    so there is no traversal codegen step here and the gate says so rather than implying one ran.
UNITY="${UNITY}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" UNITY_TIMEOUT="${UNITY_TIMEOUT}" \
  "tools/unity/build_probe.sh"

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

# 6. Player probes. The traversal probe comes first (this task's own mode), then the card and narrative probes, whose
#    worlds must still run unchanged on this revision, then the remaining modes so the whole player still passes.
export PROBE_PLAYER UNITY_PROJECT PROBE_RUNS
probe_step probe-traversal \
  env PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" \
  PROBE_RUNS="${PROBE_RUNS}" "tools/unity/run_traversal_probe.sh"

for harness in \
  run_narrative_probe.sh \
  run_cards_probe.sh \
  run_world_probe.sh \
  run_w1_gate_probe.sh \
  run_w2_gate_probe.sh \
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

# 7. The release-surface halves: the fault latches must stay compiled out of release, and this task adds none.
run_step release-surface "${PYTHON}" tools/check_release_fault_free.py \
  --dotnet "${DOTNET}" --json "${ARTIFACTS}/release-surface.json"

# 8. Documentation gate of the same revision.
"${PYTHON}" tools/validate_game_core_docs.py --self-test | tee "${ARTIFACTS}/validator-self-test.log"
"${PYTHON}" tools/validate_game_core_docs.py | tee "${ARTIFACTS}/validator.log"

echo "== GC-020 gate PASSED =="
echo "player      : ${PROBE_PLAYER}"
echo "probe JSON  : ${ARTIFACTS}/toolchain/probe-traversal.json (GC-020, this task)"
echo "test results: ${ARTIFACTS}/unity/editmode-results.xml, ${ARTIFACTS}/unity/playmode-results.xml, ${ARTIFACTS}/trx"
echo "note: PROBE_RUNS=${PROBE_RUNS}; every 'Pass' above is a reported process result, not this script's opinion."
echo "note: the audio observation in this gate is engine-free by design; a live audio device is an"
echo "      audio-enabled application's concern, never the headless player's (crash-139)."

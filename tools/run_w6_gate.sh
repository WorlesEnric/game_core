#!/usr/bin/env bash
# Wave 6 integration gate (W6-GATE).
#
# Runs, in order:
#   1. the whole plain-dotnet solution: build and test (every Unity-free assembly of the kernel, the three families'
#      rules packages, GC-023's replay fixture package and the delivery core),
#   2. the host-side static checks of this revision,
#   3. Unity package resolution on the qualification project (refreshes packages-lock.json),
#   4. the Unity EditMode suite of every testable package plus every gate assembly, including `GameCore.W6Gate.Tests`
#      — the EditMode half of this gate,
#   5. the Unity PlayMode suite of the testable packages,
#   6. build-time code generation for all three committed catalogs (probe, cards, checkpoint), the byte-identity check
#      of all three, and the StandaloneLinux64 IL2CPP qualification player through tools/unity/build_probe.sh,
#   7. EVERY player probe, each executed PROBE_RUNS times (default 5): GC-001 positive and expected-negative, GC-005
#      world dispatch, the W1/W2/W3/W4 gates, GC-010 narrative, GC-011 cards, GC-012 profile, GC-013 transitions,
#      GC-017 faults, GC-018 checkpoint, GC-019 adapters, GC-020 traversal, W5-GATE, GC-021 delivery, GC-022 lifecycle
#      stress, GC-023 replay and this gate's own `-probeW6Gate`,
#   8. the release surface: the latch and telemetry source/assembly halves, then a real marker-free release player
#      built from the cloned validation project — inspected for the union of qualification-only markers and driven
#      through the family probes that survive in a clone without them,
#   9. the documentation validator.
#
# The exit gate this script serves, verbatim from `docs/game-core/09-implementation-guide.md` (Wave 6):
#
#   "Integrate the fixed-step action reference, durable reward delivery, unload stress and replay/cost counters.
#    Demonstrate that optional physics/animation are absent from cards/narrative. Complete 1,000-cycle teardown and
#    repeatability fixtures before broader qualification."
#
# The `-probeW6Gate` mode is the one that runs all of that in a single stripped player process over three genres, so
# the gate is never claimed from the dotnet half alone and never substitutes a seam fixture for a real module.
#
# AUDIO. The headless player runs with Unity audio DISABLED (an FMOD/PulseAudio crash at exit, crash-139). Nothing in
# this gate re-enables it: the traversal course's committed-audio observation drives `CommittedAudioStage` with the
# engine-free recording sink, and the live-device sink is an audio-enabled application's concern. Every Unity Editor
# invocation and every player launch is wrapped in `timeout`, and every Unity invocation this script launches itself is
# retried exactly ONCE when it timed out, logging the retry. A step that times out twice fails the gate.
#
# Required environment:
#   UNITY   absolute path to the Unity Editor executable of the pinned 6000.0.75f1 baseline
#
# Optional environment:
#   DOTNET          .NET 8 SDK executable (default: dotnet on PATH)
#   PYTHON          python3 executable (default: python3 on PATH)
#   UNITY_PROJECT   Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS       artifact directory (default: <repo>/artifacts/w6-gate)
#   PROBE_RUNS      repetitions of every player probe (default 5; any crashing run fails the gate)
#   GC_W6_GATE_CYCLES  counted cycles per family and catalog (default 1000)
#   UNITY_TIMEOUT   seconds a full Unity Editor invocation may take (default 1800)
#   RELEASE_BUILD   build and inspect a marker-free release player (default 1; 0 skips it and says so)
#   RELEASE_PROJECT the disposable cloned project path (default: <repo>/unity/GameCore.ReleaseCheck)
#   RELEASE_PLAYER  an existing marker-free release player to inspect instead of building one
#
# Exit codes: 0 every step passed; nonzero on the first failing step (2 for a missing prerequisite).
set -euo pipefail

if [[ -z "${UNITY:-}" ]]; then
  echo "run_w6_gate.sh: UNITY must name the pinned Unity Editor executable" >&2
  echo "  e.g. UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity $0" >&2
  exit 2
fi
if [[ ! -x "${UNITY}" ]]; then
  echo "run_w6_gate.sh: UNITY is not executable: ${UNITY}" >&2
  exit 2
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
PYTHON="${PYTHON:-python3}"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/w6-gate}"
PROBE_PLAYER="${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64"
PROBE_RUNS="${PROBE_RUNS:-5}"
GC_W6_GATE_CYCLES="${GC_W6_GATE_CYCLES:-1000}"
UNITY_TIMEOUT="${UNITY_TIMEOUT:-1800}"
RELEASE_BUILD="${RELEASE_BUILD:-1}"
RELEASE_PROJECT="${RELEASE_PROJECT:-${REPO_ROOT}/unity/GameCore.ReleaseCheck}"
RELEASE_PLAYER="${RELEASE_PLAYER:-}"

mkdir -p "${ARTIFACTS}/trx" "${ARTIFACTS}/unity" "${ARTIFACTS}/toolchain" "${ARTIFACTS}/release" "${ARTIFACTS}/host"
cd "${REPO_ROOT}"

echo "== Wave 6 integration gate (W6-GATE) =="
echo "repo         : ${REPO_ROOT}"
echo "unity        : ${UNITY}"
echo "dotnet       : ${DOTNET}"
echo "project      : ${UNITY_PROJECT}"
echo "artifacts    : ${ARTIFACTS}"
echo "probe runs   : ${PROBE_RUNS}"
echo "cycles       : ${GC_W6_GATE_CYCLES} per family and catalog"
echo "unity timeout: ${UNITY_TIMEOUT}s (one retry on a timeout)"
echo "release build: ${RELEASE_BUILD}"
echo "audio        : DISABLED in the headless player (crash-139); the audio stage is qualified engine-free"

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

# 1. The whole plain-dotnet solution: the Unity-free half of the kernel, the three families' rules packages, the
#    delivery core and GC-023's replay fixture package.
run_step dotnet-build "${DOTNET}" build dotnet/GameCore.sln -c Release
run_step dotnet-test "${DOTNET}" test dotnet/GameCore.sln -c Release \
  --logger trx --results-directory "${ARTIFACTS}/trx"

# 2. Host-side static checks of this revision: the C# shape checker, the frozen contract surface, the committed
#    generated catalogs and every shell script this gate owns.
run_step csharp-check "${PYTHON}" tools/check_game_core_csharp.py
run_step contract-surface-parity "${PYTHON}" tools/check_contract_surface_parity.py
run_step catalog-verify "${PYTHON}" tools/verify_generated_catalog.py
run_step shell-syntax bash -n \
  tools/run_w6_gate.sh tools/unity/run_w6_gate_probe.sh tools/unity/run_w5_gate_probe.sh

# 3. Package resolution: every package the qualification project names must resolve before any test runs.
unity_step unity-resolve "${UNITY}" \
  -batchmode -nographics -quit \
  -projectPath "${UNITY_PROJECT}" \
  -logFile "${ARTIFACTS}/unity/resolve.log"

# 4. EditMode tests: every testable package plus every gate assembly, including `GameCore.W6Gate.Tests`, which
#    recomputes all five digest literals from the frozen name tables. Do not add -quit to a test-run command (04 s10).
unity_step unity-editmode "${UNITY}" \
  -batchmode -nographics \
  -projectPath "${UNITY_PROJECT}" \
  -runTests -testPlatform EditMode \
  -testResults "${ARTIFACTS}/unity/editmode-results.xml" \
  -logFile "${ARTIFACTS}/unity/editmode.log"

# 5. PlayMode tests: PlayerLoop installation, the application pump, reset/disposal and idle routing.
unity_step unity-playmode "${UNITY}" \
  -batchmode -nographics \
  -projectPath "${UNITY_PROJECT}" \
  -runTests -testPlatform PlayMode \
  -testResults "${ARTIFACTS}/unity/playmode-results.xml" \
  -logFile "${ARTIFACTS}/unity/playmode.log"

# 6. Build-time code generation for all three committed catalogs, then the StandaloneLinux64 IL2CPP player with High
#    managed stripping. build_probe.sh regenerates the probe catalog before it builds; the card and checkpoint
#    catalogs have their own batchmode entry points and are regenerated here so the build compiles the fresh files and
#    the byte-identity check below is meaningful.
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

# 7. Every player probe, each in its own process invocation of the same built player, PROBE_RUNS times each.
export PROBE_PLAYER UNITY_PROJECT PROBE_RUNS GC_W6_GATE_CYCLES
probe_step probe-gc001 \
  env PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" \
  PROBE_RUNS="${PROBE_RUNS}" "tools/unity/run_probe.sh" both
for harness in \
  run_world_probe.sh \
  run_w1_gate_probe.sh \
  run_w2_gate_probe.sh \
  run_w3_gate_probe.sh \
  run_w4_profile_probe.sh \
  run_narrative_probe.sh \
  run_cards_probe.sh \
  run_gc013_probe.sh \
  run_w4_gate_probe.sh \
  run_gc017_faults_probe.sh \
  run_gc018_probe.sh \
  run_gc019_probe.sh \
  run_traversal_probe.sh \
  run_w5_gate_probe.sh \
  run_gc021_probe.sh \
  run_lifecycle_stress_probe.sh \
  run_replay_probe.sh \
  run_w6_gate_probe.sh; do
  probe_step "probe-${harness}" \
    env PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" \
    PROBE_RUNS="${PROBE_RUNS}" GC_W6_GATE_CYCLES="${GC_W6_GATE_CYCLES}" "tools/unity/${harness}"
done

# 8. The release surface. The always-on halves first: the latch sources compiled Release (must be empty) and
#    Qualification (must carry everything), and every telemetry counting call site compiled away without the marker.
run_step release-surface "${PYTHON}" tools/check_release_fault_free.py \
  --dotnet "${DOTNET}" --json "${ARTIFACTS}/release-surface.json"
run_step release-telemetry-surface "${PYTHON}" tools/check_release_telemetry_free.py \
  --dotnet "${DOTNET}" --artifacts "${ARTIFACTS}" --json "${ARTIFACTS}/release-telemetry-surface.json"

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
  echo "   the source and compiled-assembly halves of the release claim did run; this note is deliberate so the"
  echo "   gate never implies a built release player was inspected when none was supplied."
fi

if [[ -n "${RELEASE_PLAYER}" ]]; then
  if [[ ! -x "${RELEASE_PLAYER}" ]]; then
    echo "   FAIL release-player: ${RELEASE_PLAYER} is not an executable file" >&2
    exit 1
  fi

  run_step release-player-latches "${PYTHON}" tools/check_player_fault_free.py \
    --player "$(dirname "${RELEASE_PLAYER}")" --json "${ARTIFACTS}/release-player-surface.json"

  # The union of qualification-only markers: the latches, GC-021's seat, GC-022's stress, the Wave 5 gate and this
  # gate must be ABSENT from the release player, and the qualification player must show each group's marker so the
  # scan is falsifiable rather than merely quiet.
  run_step release-gate-surface "${PYTHON}" tools/check_release_gate_free.py \
    --player "$(dirname "${RELEASE_PLAYER}")" \
    --qualification "$(dirname "${PROBE_PLAYER}")" \
    --json "${ARTIFACTS}/release-gate-surface.json"

  run_step release-telemetry-player "${PYTHON}" tools/check_release_telemetry_free.py \
    --dotnet "${DOTNET}" --artifacts "${ARTIFACTS}" \
    --release-player-project "${RELEASE_PROJECT}" \
    --json "${ARTIFACTS}/release/telemetry-release-surface.json"

  # The probes the release player can still run: the ones whose mode the clone keeps. It cannot run `-probeFaults`,
  # `-probeW5Gate`, `-probeGc021`, `-probeLifecycleStress` or `-probeW6Gate` (each names a qualification fixture that
  # is removed from the clone), which is exactly the point: the release surface is qualified by the modes a shipping
  # build really carries, and the qualification player above runs the rest.
  for harness in \
    run_world_probe.sh \
    run_narrative_probe.sh \
    run_cards_probe.sh \
    run_gc018_probe.sh \
    run_gc019_probe.sh \
    run_replay_probe.sh; do
    probe_step "release-probe-${harness}" \
      env PROBE_PLAYER="${RELEASE_PLAYER}" UNITY_PROJECT="${RELEASE_PROJECT}" ARTIFACTS="${ARTIFACTS}/release" \
      PROBE_RUNS="${PROBE_RUNS}" "tools/unity/${harness}"
  done
fi

# 9. Documentation gate of the same revision.
"${PYTHON}" tools/validate_game_core_docs.py --self-test | tee "${ARTIFACTS}/validator-self-test.log"
"${PYTHON}" tools/validate_game_core_docs.py | tee "${ARTIFACTS}/validator.log"

echo "== Wave 6 integration gate PASSED =="
echo "player      : ${PROBE_PLAYER}"
echo "probe JSON  : ${ARTIFACTS}/toolchain/probe-w6-gate.json (W6-GATE, this gate)"
echo "              ${ARTIFACTS}/toolchain/probe-replay.json (GC-023)"
echo "              ${ARTIFACTS}/toolchain/probe-lifecycle-stress.json (GC-022)"
echo "              ${ARTIFACTS}/toolchain/probe-gc021.json (GC-021)"
echo "              ${ARTIFACTS}/toolchain/probe-traversal.json (GC-020)"
echo "test results: ${ARTIFACTS}/unity/editmode-results.xml, ${ARTIFACTS}/unity/playmode-results.xml, ${ARTIFACTS}/trx"
echo "note: PROBE_RUNS=${PROBE_RUNS}, GC_W6_GATE_CYCLES=${GC_W6_GATE_CYCLES}; every 'Pass' above is a reported"
echo "      process result, not this script's opinion. A reduced cycle count is reported by the probe itself."
echo "release     : ${ARTIFACTS}/release-surface.json and ${ARTIFACTS}/release-gate-surface.json"
echo "note: the audio observation in this gate is engine-free by design; a live audio device is an audio-enabled"
echo "      application's concern, never the headless player's (crash-139)."

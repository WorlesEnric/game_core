#!/usr/bin/env bash
# GC-017 fault injection at every apply and cancellation boundary.
#
# Runs, in order:
#   1. the whole plain-dotnet solution: build and test (every Unity-free assembly of both families and the kernel),
#   2. Unity package resolution on the qualification project (refreshes packages-lock.json),
#   3. the Unity EditMode suite of every testable package plus every fault assembly, including
#      `GameCore.Faults.Tests` — the EditMode half of this task,
#   4. the Unity PlayMode suite of the testable packages,
#   5. the StandaloneLinux64 IL2CPP player build through tools/unity/build_probe.sh (with catalog code generation),
#   6. the GC-017 player probe, `-probeFaults`, executed PROBE_RUNS times (default 5),
#   7. the release-surface check: the latch sources compiled in BOTH configurations, the source-level release
#      configuration of every runtime file, the qualification switch itself, and — when RELEASE_PLAYER is set —
#      the managed assemblies and generated C++ of a release-configuration player,
#   8. the documentation validator.
#
# GC-017's own sentence is "inject failure at every apply and cancellation boundary": deterministic latches around
# validation, acquisition, fencing, migration, first live write, structural playback, gate installation and
# cleanup; cancellation raced against the serialized cutoff; and recovery from initial definitions into a new world
# (checkpoint-based recovery is GC-018/GC-027). The `-probeFaults` mode is the one that runs all 15 named
# observations of TEST-016's matrix in a single stripped player process over two catalogs per family, so the gate is
# never claimed from the dotnet half alone and never substitutes a seam fixture for a real module.
#
# Timeouts. The Unity Editor has a known, unresolved intermittent hang before it dispatches a batchmode command, so
# EVERY Unity invocation is wrapped in `timeout`. A timed-out invocation fails loudly (exit 124 is never a pass), and
# each Unity Editor invocation this script launches directly is retried exactly ONCE, logging the retry, because that
# failure mode is not deterministic and a second attempt is the honest way to distinguish it from a real failure. A
# step that times out twice fails the gate. The build step calls tools/unity/build_probe.sh, which wraps each of its
# own two Unity invocations in the same `timeout --signal=TERM --kill-after=60 ${UNITY_TIMEOUT}`; no outer wrapper is
# added there, because one wrapper around both invocations would share a single budget and could kill a legitimate
# build. Player probe runs are not retried: probe_runs.sh already runs each probe PROBE_RUNS times and fails the
# gate if any run is not clean.
#
# Required environment:
#   UNITY   absolute path to the Unity Editor executable of the pinned 6000.0.75f1 baseline, e.g.
#           ~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
#
# Optional environment:
#   DOTNET          .NET 8 SDK executable (default: dotnet on PATH)
#   PYTHON          python3 executable (default: python3 on PATH)
#   UNITY_PROJECT   Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS       artifact directory (default: <repo>/artifacts/faults)
#   PROBE_RUNS      repetitions of the player probe (default 5; any crashing run fails the gate)
#   UNITY_TIMEOUT   seconds a full Unity Editor invocation may take (default 1800)
#   RELEASE_PLAYER  directory of a RELEASE-configuration player (one whose project does not reference the
#                   qualification marker package). When set, step 7 inspects its managed assemblies and generated
#                   C++ and fails if any latch type, boundary name or trace prefix is present. When unset, step 7
#                   states that this half did not run; the compiled-assembly and source halves always run and are
#                   what the gate is claimed on.
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
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/faults}"
PROBE_PLAYER="${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64"
PROBE_RUNS="${PROBE_RUNS:-5}"
UNITY_TIMEOUT="${UNITY_TIMEOUT:-1800}"
RELEASE_PLAYER="${RELEASE_PLAYER:-}"
RELEASE_IL2CPP="${RELEASE_IL2CPP:-}"

mkdir -p "${ARTIFACTS}/trx" "${ARTIFACTS}/unity" "${ARTIFACTS}/toolchain"
cd "${REPO_ROOT}"

echo "== GC-017 fault injection at every apply and cancellation boundary =="
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

# 2. Package resolution: every Wave 5 package must resolve before any test runs.
unity_step unity-resolve "${UNITY}" \
  -batchmode -nographics -quit \
  -projectPath "${UNITY_PROJECT}" \
  -logFile "${ARTIFACTS}/unity/resolve.log"

# 3. EditMode tests: every testable package plus the gate assemblies and `GameCore.Faults.Tests`, which recomputes
#    both digest literals from `FaultScenario.QualifiedNames(label)` so a renamed or reordered observation fails the
#    suite instead of shrinking it. Do not add -quit to a test-run command that relies on the runner to finish
#    asynchronously (04 s10).
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

# 5. Build-time code generation for the probe catalog, then the StandaloneLinux64 IL2CPP player with High managed
#    stripping. build_probe.sh regenerates the probe catalog (GC-003) before it builds, and each of its two Unity
#    invocations carries its own `timeout --signal=TERM --kill-after=60`, so the gate does not wrap it again.
UNITY="${UNITY}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" UNITY_TIMEOUT="${UNITY_TIMEOUT}" \
  "tools/unity/build_probe.sh"

# 6. The GC-017 player probe, PROBE_RUNS times, in its own process invocation of the same built player.
export PROBE_PLAYER UNITY_PROJECT PROBE_RUNS
PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" PROBE_RUNS="${PROBE_RUNS}" \
  "tools/unity/run_gc017_faults_probe.sh"

# 7. The release surface. Three always-on halves plus the built-player half when RELEASE_PLAYER is supplied:
#    the latch sources compiled Release (must be empty) and Qualification (must carry everything), the source-level
#    release configuration of every runtime file, the qualification switch, and — if a release player was built —
#    its managed assemblies and generated C++. Nothing here is a claim about the qualification player, which is
#    supposed to carry the latches.
run_step release-surface "${PYTHON}" tools/check_release_fault_free.py \
  --dotnet "${DOTNET}" --json "${ARTIFACTS}/release-surface.json"
if [[ -n "${RELEASE_PLAYER}" ]]; then
  if [[ -n "${RELEASE_IL2CPP}" ]]; then
    run_step release-player "${PYTHON}" tools/check_player_fault_free.py \
      --player "${RELEASE_PLAYER}" --il2cpp "${RELEASE_IL2CPP}" \
      --json "${ARTIFACTS}/release-player-surface.json"
  else
    run_step release-player "${PYTHON}" tools/check_player_fault_free.py \
      --player "${RELEASE_PLAYER}" --json "${ARTIFACTS}/release-player-surface.json"
  fi
else
  echo "-- release-player: NOT RUN (RELEASE_PLAYER unset)"
  echo "   the compiled-assembly and source halves of the release claim did run; this note is deliberate so the"
  echo "   gate never implies a built release player was inspected when none was supplied."
  echo "   to run it: build a player from a project whose manifest omits com.gamecore.fault-qualification, then"
  echo "   RELEASE_PLAYER=<dir> RELEASE_IL2CPP=<generated-cpp-dir> tools/run_gc017_gate.sh"
fi

# 8. Documentation gate of the same revision.
"${PYTHON}" tools/validate_game_core_docs.py --self-test | tee "${ARTIFACTS}/validator-self-test.log"
"${PYTHON}" tools/validate_game_core_docs.py | tee "${ARTIFACTS}/validator.log"

echo "== GC-017 gate PASSED =="
echo "player      : ${PROBE_PLAYER}"
echo "probe JSON  : ${ARTIFACTS}/toolchain/probe-gc017-faults.json (this task's -probeFaults mode)"
echo "test results: ${ARTIFACTS}/unity/editmode-results.xml, ${ARTIFACTS}/unity/playmode-results.xml, ${ARTIFACTS}/trx"
echo "note: PROBE_RUNS=${PROBE_RUNS}; every 'Pass' above is a reported process result, not this script's opinion."
echo "release     : ${ARTIFACTS}/release-surface.json (and release-player-surface.json when RELEASE_PLAYER was set)"
echo "note: the probe JSON is the archived evidence — a Pass here means the player process reported it in"
echo "      ${ARTIFACTS}/toolchain/probe-gc017-faults.json, with runs 2..${PROBE_RUNS} retained alongside it."

#!/usr/bin/env bash
# GC-023 gate: replay, differential propagation and complete cost instrumentation.
#
# Runs, in order:
#   1. the whole plain-dotnet solution: build and test (the Unity-free kernel plus both families),
#   2. the replay suites on their own, with a trx artifact (they include the 10,000-step worker sweep),
#   3. the disabled-shape instrumentation proof: the `[Conditional]` call-site behaviour in both build
#      configurations, the real instrumented derivation sources compiled in both, and the qualification switch,
#   4. the host-side static checks (C# balance/forbidden constructs, the contract-surface parity check, the
#      generated-catalog verifier and the documentation validator),
#   5. Unity package resolution on the qualification project (refreshes packages-lock.json),
#   6. the Unity EditMode suite of every testable package - which now includes `GameCore.Replay.Tests` - plus
#      `GameCore.Replay.IntegrationTests`, the Editor half of this task's scenario,
#   7. the Unity PlayMode suite,
#   8. the StandaloneLinux64 IL2CPP qualification player, and `-probeReplay` executed PROBE_RUNS times,
#   9. a marker-free release IL2CPP player build, replay run and generated-code inspection: the
#      cloned project has no telemetry marker, so counting call sites must be compiled away.
#
# The task sentence is "expose ordering drift and accidental control-plane work in the simulation hot path": the
# 10,000-step replay across worker counts and shuffled producer orders is what finds ordering drift, and the real
# world's zero-work idle assertions are what find accidental control-plane work. Neither is claimed from the dotnet
# half alone, and the player probe is where both run in the shape that ships.
#
# Timeouts. Every Unity Editor invocation uses a watchdog; a timeout is a defect, not a retry.
# Player probes use the per-run watchdog in tools/unity/probe_runs.sh.
#
# Required environment:
#   UNITY   absolute path to the Unity Editor executable of the pinned 6000.0.75f1 baseline.
#
# Optional environment:
#   DOTNET          .NET 8 SDK executable (default: dotnet on PATH)
#   PYTHON          python3 executable (default: python3 on PATH)
#   UNITY_PROJECT   Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS       artifact directory (default: <repo>/artifacts/gc-023)
#   PROBE_RUNS      repetitions of each player probe (default 5; any crashing run fails the gate)
#   UNITY_TIMEOUT   seconds a full Unity Editor invocation may take (default 1800)
#   RELEASE_BUILD   build and inspect a marker-free release project (default 1; 0 skips it and says so)
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
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/gc-023}"
PROBE_RUNS="${PROBE_RUNS:-5}"
UNITY_TIMEOUT="${UNITY_TIMEOUT:-1800}"
RELEASE_BUILD="${RELEASE_BUILD:-1}"
RELEASE_PROJECT="${RELEASE_PROJECT:-${REPO_ROOT}/unity/GameCore.ReleaseCheck}"

mkdir -p "${ARTIFACTS}/trx" "${ARTIFACTS}/unity" "${ARTIFACTS}/toolchain" "${ARTIFACTS}/host"
cd "${REPO_ROOT}"

echo "== GC-023 gate (replay, differential propagation, cost instrumentation) =="
echo "repo         : ${REPO_ROOT}"
echo "unity        : ${UNITY}"
echo "dotnet       : ${DOTNET}"
echo "project      : ${UNITY_PROJECT}"
echo "artifacts    : ${ARTIFACTS}"
echo "probe runs   : ${PROBE_RUNS}"
echo "unity timeout: ${UNITY_TIMEOUT}s (no retry on a hang)"
echo "release build: ${RELEASE_BUILD}"

run_step() {
  local label="$1"
  shift
  echo "-- ${label}: $*"
  "$@"
}

unity_step() {
  local label="$1"
  shift

  local rc=0
  timeout --signal=TERM --kill-after=60 "${UNITY_TIMEOUT}" "$@" || rc=$?
  if (( rc != 0 )); then
    echo "   FAIL ${label}: Unity Editor exited ${rc}; inspect its log and capture a hung process backtrace" >&2
  fi
  return "${rc}"
}

# 1. The whole plain-dotnet solution.
run_step dotnet-build "${DOTNET}" build dotnet/GameCore.sln -c Release
run_step dotnet-test "${DOTNET}" test dotnet/GameCore.sln -c Release \
  --logger trx --results-directory "${ARTIFACTS}/trx"

# 2. The replay suites alone, so their trx artifact is identifiable even when the whole-solution run is green.
run_step replay-tests "${DOTNET}" test "${REPO_ROOT}/dotnet/tests/GameCore.Replay.Tests/GameCore.Replay.Tests.csproj" \
  -c Release --logger trx --results-directory "${ARTIFACTS}/trx" \
  --filter "FullyQualifiedName~GameCore.Replay.Tests"

# 3. The instrumentation switch: both configurations of the probe tool and of the real derivation sources.
run_step telemetry-release-surface \
  "${PYTHON}" tools/check_release_telemetry_free.py \
  --json "${ARTIFACTS}/toolchain/telemetry-release-surface.json" \
  --dotnet "${DOTNET}"

# 4. Host-side static checks.
run_step csharp-check "${PYTHON}" tools/check_game_core_csharp.py
run_step contract-surface-parity "${PYTHON}" tools/check_contract_surface_parity.py
run_step catalog-verify "${PYTHON}" tools/verify_generated_catalog.py
run_step docs-validator-self-test "${PYTHON}" tools/validate_game_core_docs.py --self-test
run_step docs-validator "${PYTHON}" tools/validate_game_core_docs.py

# 5. Package resolution: the replay package and the telemetry marker must resolve before any test runs.
unity_step unity-resolve "${UNITY}" \
  -batchmode -nographics -quit \
  -projectPath "${UNITY_PROJECT}" \
  -logFile "${ARTIFACTS}/unity/resolve.log"

# 6. EditMode: every testable package (including `com.gamecore.replay`) plus `GameCore.Replay.IntegrationTests`.
#    Do not add -quit to a test-run command (04 s10); `-testResults` resolves against the project path.
unity_step unity-editmode "${UNITY}" \
  -batchmode -nographics \
  -projectPath "${UNITY_PROJECT}" \
  -runTests -testPlatform EditMode \
  -testResults "${ARTIFACTS}/unity/editmode-results.xml" \
  -logFile "${ARTIFACTS}/unity/editmode.log"

# 7. PlayMode: the application pump, loop installation and idle routing.
unity_step unity-playmode "${UNITY}" \
  -batchmode -nographics \
  -projectPath "${UNITY_PROJECT}" \
  -runTests -testPlatform PlayMode \
  -testResults "${ARTIFACTS}/unity/playmode-results.xml" \
  -logFile "${ARTIFACTS}/unity/playmode.log"

# 8. The player: build with High stripping, then run the replay probe PROBE_RUNS times.
unity_step probe-player-build "${SCRIPT_DIR}/unity/build_probe.sh"
run_step replay-probe env \
  "PROBE_RUNS=${PROBE_RUNS}" \
  "UNITY_PROJECT=${UNITY_PROJECT}" \
  "ARTIFACTS=${ARTIFACTS}/toolchain" \
  "${SCRIPT_DIR}/unity/run_replay_probe.sh"

# 9. Build and run a marker-free release player, then inspect its generated IL2CPP counting sites
#    against the qualification player. A source-only switch check does not establish the shipping shape.
if [[ "${RELEASE_BUILD}" == "1" ]]; then
  if [[ -e "${RELEASE_PROJECT}" ]]; then
    echo "   FAIL release-project: ${RELEASE_PROJECT} already exists; remove it first" >&2
    exit 1
  fi

  run_step release-clone "${PYTHON}" tools/unity/prepare_gc017_release_project.py
  run_step release-fault-free "${PYTHON}" tools/check_release_fault_free.py --no-build \
    --json "${ARTIFACTS}/release-surface.json"
  run_step release-player-build env "UNITY_PROJECT=${RELEASE_PROJECT}" \
    "ARTIFACTS=${ARTIFACTS}/release" "UNITY_TIMEOUT=${UNITY_TIMEOUT}" \
    "UNITY=${UNITY}" "DOTNET=${DOTNET}" "${SCRIPT_DIR}/unity/build_probe.sh"
  run_step release-replay env "PROBE_RUNS=1" "UNITY_PROJECT=${RELEASE_PROJECT}" \
    "ARTIFACTS=${ARTIFACTS}/release" "${SCRIPT_DIR}/unity/run_replay_probe.sh"
  run_step release-player-fault-surface "${PYTHON}" tools/check_player_fault_free.py \
    --player "${RELEASE_PROJECT}/Builds/Linux64" \
    --json "${ARTIFACTS}/release/player-fault-surface.json"
  run_step release-player-telemetry-surface "${PYTHON}" tools/check_release_telemetry_free.py \
    --dotnet "${DOTNET}" --release-player-project "${RELEASE_PROJECT}" --artifacts "${ARTIFACTS}" \
    --json "${ARTIFACTS}/release/telemetry-release-surface.json"
else
  echo "-- release-project: skipped (RELEASE_BUILD=0)"
fi

echo "== GC-023 gate: every step passed =="

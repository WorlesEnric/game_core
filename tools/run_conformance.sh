#!/usr/bin/env bash
# GC-024 reference-conformance gate (GC-024).
#
# Runs, in order:
#   1. the whole plain-dotnet solution: build and test (every Unity-free assembly, including the three rules packages,
#      the conformance fixture package and its suite, which is where the fixture's self-consistency, the trace
#      round-trip, the oracle's falsifiability and the projection agreement are asserted before any world runs),
#   2. the host-side genre audit (`tools/gc024_genre_audit.py`): no kernel assembly, project or source references a
#      gameplay/rules/qualification assembly or names a genre type, and every gameplay/rules assembly references the
#      kernel. Its document is written to `artifacts/gc-024/genre-audit.json`,
#   3. Unity package resolution on the qualification project (the conformance fixture package must resolve),
#   4. the Unity EditMode suite: every testable package plus `GameCore.Conformance.Tests`, which executes every 07
#      table in real worlds and recomputes each verdict from the trace it recorded,
#   5. the Unity PlayMode suite of the testable packages,
#   6. the StandaloneLinux64 IL2CPP qualification player with High managed stripping via tools/unity/build_probe.sh,
#   7. every player probe, each executed PROBE_RUNS times (default 5): the modes the earlier gates own, plus this
#      task's own `-probeConformance`. The other modes are re-run here on purpose: GC-024's acceptance is that all
#      three families still pass "with the same built kernel" (P-059), and a conformance run on a revision that broke
#      a family probe would be evidence about a different revision,
#   8. the release-surface halves: the fault latches and the telemetry counters stay compiled out of a release build,
#      and the disposable release clone no longer carries this task's fixture,
#   9. the documentation validator.
#
# Timeouts. The Unity Editor has a known, unresolved intermittent hang before it dispatches a batchmode command, so
# EVERY Unity invocation is wrapped in `timeout` and every one this script launches directly is retried exactly ONCE
# on a timeout. Player probes are not retried: probe_runs.sh already runs each probe PROBE_RUNS times.
#
# AUDIO. The qualification player is built and launched with `-nographics` and with Unity audio DISABLED (an
# FMOD/PulseAudio crash at exit was the reason, crash-139). Nothing in this gate re-enables audio.
#
# Required environment: DOTNET (the .NET 8 SDK) and UNITY (the 6000.0.75f1 Editor).
# Optional: PROBE_RUNS (default 5), ARTIFACTS (default <repo>/artifacts/gc-024), GC024_TRACE_DIGEST_* pins.
#
# Exit codes: 0 every step passed; 1 at least one step failed; 2 a missing prerequisite.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
UNITY="${UNITY:-${HOME}/Unity/Hub/Editor/6000.0.75f1/Editor/Unity}"
DOTNET="${DOTNET:-$(command -v dotnet || true)}"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/gc-024}"
PROBE_RUNS="${PROBE_RUNS:-5}"
UNITY_TIMEOUT="${UNITY_TIMEOUT:-1800}"
export PROBE_RUNS

if [[ ! -x "${DOTNET}" ]]; then
  echo "run_conformance.sh: DOTNET is not an executable .NET SDK: '${DOTNET}'" >&2
  exit 2
fi
if [[ ! -x "${UNITY}" ]]; then
  echo "run_conformance.sh: UNITY is not an executable Editor: '${UNITY}'" >&2
  exit 2
fi
if ! [[ "${PROBE_RUNS}" =~ ^[1-9][0-9]*$ ]]; then
  echo "run_conformance.sh: PROBE_RUNS must be a positive integer: ${PROBE_RUNS}" >&2
  exit 2
fi

mkdir -p "${ARTIFACTS}"/{trx,unity,toolchain,traces}
failures=0
cd "${REPO_ROOT}"

step() { echo; echo "== $* =="; }

# ---------------------------------------------------------------- 1. plain dotnet
step "1. plain dotnet: build and test the whole solution"
if ! "${DOTNET}" build dotnet/GameCore.sln -c Release --nologo; then
  echo "run_conformance.sh: the solution did not build" >&2
  failures=$((failures + 1))
fi
if ! "${DOTNET}" test dotnet/GameCore.sln -c Release --nologo --no-build \
    --logger "trx;LogFileName=gc024-trx.trx" --results-directory "${ARTIFACTS}/trx"; then
  echo "run_conformance.sh: at least one dotnet test failed" >&2
  failures=$((failures + 1))
fi

# ---------------------------------------------------------------- 2. host-side genre audit
step "2. genre audit (kernel assemblies, projects and sources against the families)"
if ! python3 "${REPO_ROOT}/tools/gc024_genre_audit.py" --root "${REPO_ROOT}" \
    --out "artifacts/gc-024/genre-audit.json" | tee "${ARTIFACTS}/genre-audit.log"; then
  echo "run_conformance.sh: the genre audit is not clean (see ${ARTIFACTS}/genre-audit.log)" >&2
  failures=$((failures + 1))
fi

# ---------------------------------------------------------------- 3. Unity resolve
step "3. Unity: resolve the qualification project's packages"
unity_run() {
  local log="$1"
  shift
  local rc=0
  timeout --signal=TERM --kill-after=30 "${UNITY_TIMEOUT}" "$@" >"${log}" 2>&1 || rc=$?
  if (( rc == 124 )); then
    echo "run_conformance.sh: Unity timed out; retrying once (known intermittent pre-dispatch hang)" >&2
    timeout --signal=TERM --kill-after=30 "${UNITY_TIMEOUT}" "$@" >"${log}.retry" 2>&1 || rc=$?
  fi
  return "${rc}"
}

if ! unity_run "${ARTIFACTS}/unity/resolve.log" \
    "${UNITY}" -batchmode -nographics -quit \
    -projectPath "${UNITY_PROJECT}" -accept-apiupdate -logFile -; then
  echo "run_conformance.sh: package resolution failed (see ${ARTIFACTS}/unity/resolve.log)" >&2
  failures=$((failures + 1))
fi

# ---------------------------------------------------------------- 4. EditMode
step "4. Unity EditMode: every testable package plus GameCore.Conformance.Tests"
if ! unity_run "${ARTIFACTS}/unity/editmode.log" \
    "${UNITY}" -batchmode -nographics -projectPath "${UNITY_PROJECT}" \
    -runTests -testPlatform EditMode -testResults "${ARTIFACTS}/unity/editmode-results.xml" \
    -logFile -; then
  echo "run_conformance.sh: the EditMode suite failed (see ${ARTIFACTS}/unity/editmode-results.xml)" >&2
  failures=$((failures + 1))
fi

# ---------------------------------------------------------------- 5. PlayMode
step "5. Unity PlayMode: the testable packages' play-mode suites"
if ! unity_run "${ARTIFACTS}/unity/playmode.log" \
    "${UNITY}" -batchmode -nographics -projectPath "${UNITY_PROJECT}" \
    -runTests -testPlatform PlayMode -testResults "${ARTIFACTS}/unity/playmode-results.xml" \
    -logFile -; then
  echo "run_conformance.sh: the PlayMode suite failed (see ${ARTIFACTS}/unity/playmode-results.xml)" >&2
  failures=$((failures + 1))
fi

# ---------------------------------------------------------------- 6. the player
step "6. IL2CPP player: StandaloneLinux64, High stripping"
if ! UNITY="${UNITY}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" \
    "${REPO_ROOT}/tools/unity/build_probe.sh" >"${ARTIFACTS}/toolchain/build.log" 2>&1; then
  echo "run_conformance.sh: the player build failed (see ${ARTIFACTS}/toolchain/build.log)" >&2
  failures=$((failures + 1))
fi

# The committed catalogs must still regenerate byte-identically: a conformance run over a changed catalog is a run
# over a different composition revision (P-028).
step "6b. the committed catalogs regenerate byte-identically"
if ! python3 "${REPO_ROOT}/tools/verify_generated_catalog.py" >>"${ARTIFACTS}/toolchain/build.log" 2>&1; then
  echo "run_conformance.sh: a committed catalog no longer regenerates byte-identically" >&2
  failures=$((failures + 1))
fi

# ---------------------------------------------------------------- 7. every probe
step "7. player probes (${PROBE_RUNS} run(s) each)"
probe_scripts=(
  run_probe.sh
  run_world_probe.sh
  run_narrative_probe.sh
  run_cards_probe.sh
  run_gc013_probe.sh
  run_traversal_probe.sh
  run_gc021_probe.sh
  run_replay_probe.sh
  run_conformance_probe.sh
)
for probe in "${probe_scripts[@]}"; do
  if [[ ! -f "${REPO_ROOT}/tools/unity/${probe}" ]]; then
    echo "   FAIL: probe harness not found: tools/unity/${probe}" >&2
    failures=$((failures + 1))
    continue
  fi
  if ! ARTIFACTS="${ARTIFACTS}/toolchain" UNITY_PROJECT="${UNITY_PROJECT}" \
      "${REPO_ROOT}/tools/unity/${probe}"; then
    echo "   FAIL: tools/unity/${probe} reported a mismatch" >&2
    failures=$((failures + 1))
  fi
done

# ---------------------------------------------------------------- 8. release surface
step "8. release surface: the qualification switch and this fixture stay out of a release build"
if ! python3 "${REPO_ROOT}/tools/check_release_telemetry_free.py" --no-build \
    >>"${ARTIFACTS}/toolchain/release-checks.log" 2>&1; then
  echo "run_conformance.sh: the telemetry release check failed" >&2
  failures=$((failures + 1))
fi
if ! python3 "${REPO_ROOT}/tools/check_release_fault_free.py" --no-build \
    >>"${ARTIFACTS}/toolchain/release-checks.log" 2>&1; then
  echo "run_conformance.sh: the fault release check failed" >&2
  failures=$((failures + 1))
fi

# ---------------------------------------------------------------- 9. documentation
step "9. documentation validator (self-test + full)"
if ! python3 "${REPO_ROOT}/tools/validate_game_core_docs.py" --self-test \
    >"${ARTIFACTS}/validator-self-test.log" 2>&1; then
  echo "run_conformance.sh: the validator self-test failed" >&2
  failures=$((failures + 1))
fi
if ! python3 "${REPO_ROOT}/tools/validate_game_core_docs.py" >"${ARTIFACTS}/validator.log" 2>&1; then
  echo "run_conformance.sh: documentation validation failed" >&2
  failures=$((failures + 1))
fi

echo
if (( failures != 0 )); then
  echo "== GC-024 gate FAILED (${failures} step(s)) ==" >&2
  exit 1
fi
echo "== GC-024 gate PASSED =="
echo "artifacts  : ${ARTIFACTS}"
echo "traces     : ${ARTIFACTS}/traces"
echo "genre audit: ${REPO_ROOT}/artifacts/gc-024/genre-audit.json"

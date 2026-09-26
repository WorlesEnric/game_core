#!/usr/bin/env bash
# GC-026 performance benchmark harness (Wave 7).
#
# Runs the already-built qualification player in its `-probeBenchmark` mode BENCH_RUNS times, keeps every run's raw
# per-workload sample documents in its own directory, validates each run's structured result, then hands the raw
# directories to tools/summarize_benchmarks.py. The player itself is built by tools/unity/build_probe.sh, which
# compiles the benchmark fixture package, the instrumented kernel and the validation project into the same IL2CPP
# binary; this script only launches it and validates what it wrote.
#
# Why this does not source tools/unity/probe_runs.sh: that helper imposes a 600-second watchdog and deletes the
# result before each run, whereas one steady workload is declared as 30 s warmup + 120 s measurement and the whole
# catalogue is 11 workloads, so a run needs a much longer timeout and its own per-run directory. The validation
# discipline is the same one, deliberately: strict JSON, the expected result and exit code, no failing step, at least
# one passing step, and a signal death (exit >= 128) is a crash that fails the harness rather than a verdict.
#
# -quit is deliberately NOT passed: the probe exits itself through Application.Quit with the code that encodes its
# result (0 all-pass, 1 any Fail, 3 the negative mode). A run whose exit code is >= 128 is a crash (SIGSEGV 139,
# SIGABRT 134, ...) and always fails the harness.
#
# Required environment:
#   python3                 strict JSON validation and tools/summarize_benchmarks.py
#
# Optional environment (every knob, its default, and what it costs):
#   PROBE_PLAYER   path to the built probe executable
#                  (default: <repo>/unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64)
#   UNITY_PROJECT  Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS      artifact directory (default: <repo>/artifacts/performance)
#   BENCH_RUNS     independent runs of the whole catalogue (default 5; 08: "five independent 120-second runs")
#   BENCH_WARMUP   warmup seconds per workload (default 30)
#   BENCH_DURATION steady measurement window in seconds (default 120)
#   BENCH_REPETITIONS repetitions of a change workload; 0 means "each workload's declared default" (default 0)
#   BENCH_WORKLOADS  `all` or a comma-separated list of workload ids (default all)
#   BENCH_SCOPES   fixture scopes (default 1000)
#   BENCH_TARGETS  fixture targets (default 10000)
#   BENCH_LIVE_SCOPES  live-world scopes (default: BENCH_SCOPES)
#   BENCH_LIVE_TARGETS live-world targets (default: BENCH_TARGETS)
#   BENCH_SEED     recorded fixture seed (default 20260926)
#   BENCH_TIMEOUT  per-run watchdog in seconds (default 3600; a timeout is a defect, not a retry)
#   MACHINE        recorded baseline machine name (default: $(hostname))
#
# Wall-clock cost of the declared defaults. The catalogue is 11 workloads: three steady and eight change.
#   * Steady window: 3 workloads x 5 runs x (30 s warmup + 120 s duration) = 2250 s = 37.5 min.
#   * Change warmup: 8 workloads x 5 runs x 30 s = 1200 s = 20 min, if the runner warms each workload.
#   * Change repetitions: 5 runs x 5600 declared repetitions = 28,000 repetitions (1000 each for update-size-1,
#     update-size-100, reparent-100, inactive-target-comparison and lifecycle-cycles-1000; 200 each for
#     update-size-10000, whole-world-mode-switch and spawn-1000). Their wall clock is the repetition cost, which this
#     script does not estimate because no run has measured it yet.
#   So the declared defaults cost at least ~57.5 min of warmup/window time plus the 28,000 change repetitions.
#
# Exit codes: 0 every run was clean and the summarizer judged every gate passed with no missed target; 1 a run was not
# clean or the summarizer reported a failure; 2 a missing prerequisite (no player, bad knob).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

PROBE_PLAYER="${PROBE_PLAYER:-}"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/performance}"
PROBE_PLAYER="${PROBE_PLAYER:-${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64}"

BENCH_RUNS="${BENCH_RUNS:-5}"
BENCH_WARMUP="${BENCH_WARMUP:-30}"
BENCH_DURATION="${BENCH_DURATION:-120}"
BENCH_REPETITIONS="${BENCH_REPETITIONS:-0}"
BENCH_WORKLOADS="${BENCH_WORKLOADS:-all}"
BENCH_SCOPES="${BENCH_SCOPES:-1000}"
BENCH_TARGETS="${BENCH_TARGETS:-10000}"
BENCH_LIVE_SCOPES="${BENCH_LIVE_SCOPES:-${BENCH_SCOPES}}"
BENCH_LIVE_TARGETS="${BENCH_LIVE_TARGETS:-${BENCH_TARGETS}}"
BENCH_SEED="${BENCH_SEED:-20260926}"
BENCH_TIMEOUT="${BENCH_TIMEOUT:-3600}"
MACHINE="${MACHINE:-$(hostname)}"

# The catalogue in report order, mirroring BenchmarkWorkloads.All. Every consumer iterates it, so a workload cannot be
# dropped from the harness silently: the required step list and the raw-artifact list are both derived from it.
ALL_WORKLOADS=(
  idle-command-world
  steady-unchanged-10000-steps
  steady-execution-10000-targets
  update-size-1
  update-size-100
  update-size-10000
  whole-world-mode-switch
  spawn-1000
  reparent-100
  inactive-target-comparison
  lifecycle-cycles-1000
)

die() {
  echo "run_benchmarks.sh: $*" >&2
  exit 2
}

require_uint() {
  local name="$1" value="$2"
  if [[ ! "${value}" =~ ^[[:digit:]]+$ ]]; then
    die "${name} must be a non-negative integer, got '${value}'"
  fi
}

require_positive() {
  local name="$1" value="$2"
  if [[ ! "${value}" =~ ^[[:digit:]]+$ ]] || (( value < 1 )); then
    die "${name} must be a positive integer, got '${value}'"
  fi
}

require_uint BENCH_RUNS "${BENCH_RUNS}"
require_positive BENCH_WARMUP "${BENCH_WARMUP}"
require_positive BENCH_DURATION "${BENCH_DURATION}"
require_uint BENCH_REPETITIONS "${BENCH_REPETITIONS}"
require_positive BENCH_SCOPES "${BENCH_SCOPES}"
require_positive BENCH_TARGETS "${BENCH_TARGETS}"
require_positive BENCH_LIVE_SCOPES "${BENCH_LIVE_SCOPES}"
require_positive BENCH_LIVE_TARGETS "${BENCH_LIVE_TARGETS}"
require_uint BENCH_SEED "${BENCH_SEED}"
require_positive BENCH_TIMEOUT "${BENCH_TIMEOUT}"
if (( BENCH_RUNS < 1 )); then
  die "BENCH_RUNS must be a positive integer, got '${BENCH_RUNS}'"
fi

# Resolve the workload selector against the catalogue. An unknown id is refused with the list of known ids; a silently
# skipped workload is a silently shortened gate. A duplicate is dropped, and the result is re-ordered into catalogue
# order so the request order cannot change what ran.
SELECTED_WORKLOADS=()
if [[ "${BENCH_WORKLOADS}" == "all" ]]; then
  SELECTED_WORKLOADS=("${ALL_WORKLOADS[@]}")
else
  IFS=',' read -r -a requested <<<"${BENCH_WORKLOADS}"
  for candidate in ${requested[@]+"${requested[@]}"}; do
    candidate="${candidate//[[:space:]]/}"
    [[ -z "${candidate}" ]] && continue
    known=0
    for workload in "${ALL_WORKLOADS[@]}"; do
      if [[ "${candidate}" == "${workload}" ]]; then known=1; break; fi
    done
    if (( known == 0 )); then
      die "unknown benchmark workload '${candidate}'; known ids are ${ALL_WORKLOADS[*]}"
    fi
    duplicate=0
    for selected in ${SELECTED_WORKLOADS[@]+"${SELECTED_WORKLOADS[@]}"}; do
      if [[ "${selected}" == "${candidate}" ]]; then duplicate=1; break; fi
    done
    if (( duplicate == 0 )); then
      SELECTED_WORKLOADS+=("${candidate}")
    fi
  done
  if (( ${#SELECTED_WORKLOADS[@]} == 0 )); then
    die "BENCH_WORKLOADS named no workload; use 'all' or a comma-separated list of ${ALL_WORKLOADS[*]}"
  fi
  ordered=()
  for workload in "${ALL_WORKLOADS[@]}"; do
    for selected in "${SELECTED_WORKLOADS[@]}"; do
      if [[ "${selected}" == "${workload}" ]]; then ordered+=("${workload}"); fi
    done
  done
  SELECTED_WORKLOADS=("${ordered[@]}")
fi

if [[ ! -x "${PROBE_PLAYER}" ]]; then
  die "PROBE_PLAYER=${PROBE_PLAYER} is not an executable file; build it first with tools/unity/build_probe.sh"
fi

# The per-run watchdog is a defect detector, not a retry: a hang must fail the harness. `timeout` is coreutils and is
# present on the Linux build host; when it is absent (a macOS checkout, where nothing can run anyway) the harness says
# so once and runs without a watchdog rather than silently shortening the protection.
WATCHDOG=0
if command -v timeout >/dev/null 2>&1; then
  WATCHDOG=1
else
  echo "warning: no 'timeout' on PATH; runs execute WITHOUT the ${BENCH_TIMEOUT}s watchdog" >&2
fi

mkdir -p "${ARTIFACTS}/raw"

# environment.txt is written before any run so the summary can name the hardware even when Unity's own environment.txt
# is absent. It mirrors the fields tools/unity/build_probe.sh records, with macOS fallbacks added because a developer
# may run this from a Mac (nothing runs there, but the file must not be silently empty).
{
  echo "machine: ${MACHINE}"
  echo "host: $(uname -a)"
  echo "date_utc: $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "uname_m: $(uname -m)"
  if command -v nproc >/dev/null 2>&1; then
    echo "nproc: $(nproc)"
  elif command -v sysctl >/dev/null 2>&1; then
    echo "nproc: $(sysctl -n hw.ncpu 2>/dev/null || echo unknown)"
  else
    echo "nproc: unknown"
  fi
  if [[ -r /proc/cpuinfo ]]; then
    echo "cpu_model: $(awk -F': ' '/^model name/{print $2; exit}' /proc/cpuinfo)"
  elif command -v sysctl >/dev/null 2>&1; then
    echo "cpu_model: $(sysctl -n machdep.cpu.brand_string 2>/dev/null || sysctl -n hw.model 2>/dev/null || echo unknown)"
  else
    echo "cpu_model: unknown"
  fi
  if command -v lsb_release >/dev/null 2>&1; then
    echo "lsb_release: $(lsb_release -ds 2>/dev/null | tr -d '"')"
  fi
  echo "player: ${PROBE_PLAYER}"
  if [[ -f "${PROBE_PLAYER}" ]]; then
    echo "player_bytes: $(stat -c '%s' "${PROBE_PLAYER}" 2>/dev/null || stat -f '%z' "${PROBE_PLAYER}")"
    if command -v sha256sum >/dev/null 2>&1; then
      echo "player_sha256: $(sha256sum "${PROBE_PLAYER}" | cut -d' ' -f1)"
    elif command -v shasum >/dev/null 2>&1; then
      echo "player_sha256: $(shasum -a 256 "${PROBE_PLAYER}" | cut -d' ' -f1)"
    fi
  fi
} >"${ARTIFACTS}/environment.txt"

echo "== GC-026 benchmark (${BENCH_RUNS} run(s)) =="
echo "repo        : ${REPO_ROOT}"
echo "player      : ${PROBE_PLAYER}"
echo "artifacts   : ${ARTIFACTS}"
echo "workloads   : ${SELECTED_WORKLOADS[*]}"
echo "scopes      : ${BENCH_SCOPES} live ${BENCH_LIVE_SCOPES} ; targets: ${BENCH_TARGETS} live ${BENCH_LIVE_TARGETS}"
echo "warmup      : ${BENCH_WARMUP}s ; duration: ${BENCH_DURATION}s ; repetitions: ${BENCH_REPETITIONS}"
echo "seed        : ${BENCH_SEED} ; machine: ${MACHINE}"
echo "watchdog    : ${BENCH_TIMEOUT}s per run (a timeout is a defect, not a retry)"

failures=0

bench_args=(
  -batchmode
  -nographics
  -probeBenchmark
  "-probeBenchmarkWorkload=${BENCH_WORKLOADS}"
  "-probeBenchmarkScopes=${BENCH_SCOPES}"
  "-probeBenchmarkTargets=${BENCH_TARGETS}"
  "-probeBenchmarkLiveScopes=${BENCH_LIVE_SCOPES}"
  "-probeBenchmarkLiveTargets=${BENCH_LIVE_TARGETS}"
  "-probeBenchmarkWarmup=${BENCH_WARMUP}"
  "-probeBenchmarkDuration=${BENCH_DURATION}"
  "-probeBenchmarkRepetitions=${BENCH_REPETITIONS}"
  "-probeBenchmarkSeed=${BENCH_SEED}"
)

for (( run = 1; run <= BENCH_RUNS; run++ )); do
  run_dir="${ARTIFACTS}/raw/run${run}"
  mkdir -p "${run_dir}"
  result_file="${run_dir}/probe-benchmark.json"
  log_file="${run_dir}/player-benchmark.log"
  rm -f "${result_file}"

  run_args=("${bench_args[@]}" "-probeBenchmarkRunOrdinal=${run}")

  # Record the exact invocation and the resolved environment before launching, so a run's evidence names its inputs.
  {
    echo "# exact invocation (plain)"
    printf '%s ' "${PROBE_PLAYER}" "${run_args[@]}" -logFile "${log_file}" -probeResult "${result_file}"
    echo
    echo
    echo "# exact invocation (re-runnable, shell-quoted)"
    printf '%q ' "${PROBE_PLAYER}" "${run_args[@]}" -logFile "${log_file}" -probeResult "${result_file}"
    echo
    echo
    echo "# resolved environment"
    echo "MACHINE=${MACHINE}"
    echo "UNITY_PROJECT=${UNITY_PROJECT}"
    echo "ARTIFACTS=${ARTIFACTS}"
    echo "BENCH_RUNS=${BENCH_RUNS}"
    echo "BENCH_RUN_ORDINAL=${run}"
    echo "BENCH_WORKLOADS=${BENCH_WORKLOADS} (selected: ${SELECTED_WORKLOADS[*]})"
    echo "BENCH_SCOPES=${BENCH_SCOPES}"
    echo "BENCH_TARGETS=${BENCH_TARGETS}"
    echo "BENCH_LIVE_SCOPES=${BENCH_LIVE_SCOPES}"
    echo "BENCH_LIVE_TARGETS=${BENCH_LIVE_TARGETS}"
    echo "BENCH_WARMUP=${BENCH_WARMUP}"
    echo "BENCH_DURATION=${BENCH_DURATION}"
    echo "BENCH_REPETITIONS=${BENCH_REPETITIONS}"
    echo "BENCH_SEED=${BENCH_SEED}"
    echo "BENCH_TIMEOUT=${BENCH_TIMEOUT}"
    echo "PROBE_PLAYER=${PROBE_PLAYER}"
  } >"${run_dir}/invocation.txt"

  echo "-- running benchmark probe (run ${run}/${BENCH_RUNS}) -> ${run_dir}"
  rc=0
  if (( WATCHDOG )); then
    timeout --signal=TERM --kill-after=30 "${BENCH_TIMEOUT}" "${PROBE_PLAYER}" \
      "${run_args[@]}" \
      -logFile "${log_file}" \
      -probeResult "${result_file}" || rc=$?
  else
    "${PROBE_PLAYER}" \
      "${run_args[@]}" \
      -logFile "${log_file}" \
      -probeResult "${result_file}" || rc=$?
  fi

  run_failed=0

  # A signal death is a crash, not a verdict: 139 = SIGSEGV, 134 = SIGABRT, 135 = SIGBUS, 136 = SIGFPE.
  if (( rc >= 128 )); then
    echo "   FAIL benchmark: run ${run} exited ${rc} (signal: crash)" >&2
    run_failed=1
  elif (( rc != 0 )); then
    echo "   FAIL benchmark: run ${run} exited ${rc}, expected 0 (see ${log_file})" >&2
    run_failed=1
  fi

  if [[ ! -f "${result_file}" ]]; then
    echo "   FAIL benchmark: run ${run} wrote no result to ${result_file}" >&2
    run_failed=1
  elif ! python3 -m json.tool "${result_file}" >/dev/null; then
    echo "   FAIL benchmark: ${result_file} is not valid JSON" >&2
    run_failed=1
  else
    for fragment in '"task": "GC-026"' '"mode": "Benchmark"' '"result": "Pass"'; do
      if ! grep -q "${fragment}" "${result_file}"; then
        echo "   FAIL benchmark: ${result_file} does not report ${fragment}" >&2
        run_failed=1
      fi
    done
    if grep -q '"status": "Fail"' "${result_file}"; then
      echo "   FAIL benchmark: run ${run} has at least one probe step reporting status Fail" >&2
      run_failed=1
    fi
    if ! grep -q '"status": "Pass"' "${result_file}"; then
      echo "   FAIL benchmark: run ${run} has no probe step reporting status Pass" >&2
      run_failed=1
    fi

    # Every step the mode promises, including one per selected workload. The names are frozen; a workload that ran
    # but reported no step is a missing gate, and a step for a workload that did not run is not required.
    required_steps=(
      benchmark-config
      benchmark-hardware
      benchmark-fixture
      benchmark-correctness-gates
      benchmark-digest
      benchmark-artifacts-json
      benchmark-artifacts-csv
    )
    for workload in "${SELECTED_WORKLOADS[@]}"; do
      required_steps+=("benchmark-${workload}")
    done
    for step in "${required_steps[@]}"; do
      if ! grep -q "\"name\": \"${step}\"" "${result_file}"; then
        echo "   FAIL benchmark: run ${run} is missing required step '${step}'" >&2
        run_failed=1
      fi
    done

    # The raw artifacts the summarizer reads must exist and be non-empty for every selected workload: a result JSON
    # that claims benchmark-artifacts-json/csv passed while the files are absent is exactly the failure this catches.
    for workload in "${SELECTED_WORKLOADS[@]}"; do
      if [[ ! -s "${run_dir}/${workload}.samples.json" ]]; then
        echo "   FAIL benchmark: run ${run} has no non-empty ${workload}.samples.json" >&2
        run_failed=1
      fi
      if [[ ! -s "${run_dir}/${workload}.samples.csv" ]]; then
        echo "   FAIL benchmark: run ${run} has no non-empty ${workload}.samples.csv" >&2
        run_failed=1
      fi
    done
  fi

  if (( run_failed == 0 )); then
    echo "   ok benchmark: run ${run}/${BENCH_RUNS} clean, exit 0, result Pass"
  else
    failures=$((failures + 1))
    echo "   FAIL benchmark: run ${run}/${BENCH_RUNS} was not clean (evidence ${run_dir})" >&2
  fi
done

if (( failures != 0 )); then
  echo "run_benchmarks.sh: ${failures} of ${BENCH_RUNS} run(s) were not clean; ${ARTIFACTS}/raw holds the evidence" >&2
  exit 1
fi

echo "-- summarising ${ARTIFACTS}/raw"
summary_rc=0
python3 "${SCRIPT_DIR}/summarize_benchmarks.py" \
  --raw "${ARTIFACTS}/raw" \
  --out "${ARTIFACTS}/summary.md" \
  --decisions "${ARTIFACTS}/BUDGET_DECISIONS.md" \
  --machine "${MACHINE}" \
  --json "${ARTIFACTS}/summarize.json" || summary_rc=$?

if (( summary_rc != 0 )); then
  echo "run_benchmarks.sh: the summarizer reported a failure (exit ${summary_rc}); see ${ARTIFACTS}/summary.md" >&2
fi
exit "${summary_rc}"

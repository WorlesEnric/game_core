#!/usr/bin/env bash
# probe_runs.sh — sourced helper that makes every player probe run PROBE_RUNS times (default 5) and fails the
# calling script if ANY run crashes.
#
# Why: a probe that prints its Pass JSON and then dies during native engine teardown (SIGSEGV/SIGABRT while the
# PlayerLoop, worlds and native containers are released) is a flaky failure that a single run can hide. A run is
# clean only when it exits with the expected code, writes the expected result JSON and reports no failing step; a
# later clean run never repairs an earlier dirty one.
#
# This file is a library: source it, never execute it. The sourcing script keeps an integer `failures` counter and
# sets PROBE_PLAYER before calling:
#
#   probe_run_n <mode_label> <result_file> <log_file> <expected_rc> <expected_result> [extra player args...]
#   probe_require_steps <result_file> '"name": "<step>"' ['"name": "<step>"' ...]
#
# probe_run_n returns 0 only when all PROBE_RUNS runs were clean; probe_require_steps increments the caller's
# `failures` counter once per missing step.
#
# Run 1 writes the canonical <result_file>/<log_file> (the retained evidence); runs 2..N write <file>.run<N>, so
# later runs add crash evidence without overwriting run 1.
#
# Environment:
#   PROBE_RUNS   how many times each probe is executed (default 5; must be a positive integer)
#   PROBE_LABEL  label prefixed to probe_require_steps failure messages (default: probe)
set -euo pipefail

# Sourced-library guard: `BASH_SOURCE[0]` names the caller while sourcing, and this file while executing.
if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
  echo "usage: probe_runs.sh is a sourced helper library; source it from a probe script, do not execute it." >&2
  exit 2
fi

PROBE_RUNS="${PROBE_RUNS:-5}"
if [[ ! "${PROBE_RUNS}" =~ ^[[:digit:]]+$ ]] || (( PROBE_RUNS < 1 )); then
  echo "probe_runs.sh: PROBE_RUNS must be a positive integer, got '${PROBE_RUNS}'" >&2
  exit 2
fi

# One invocation of the player. Returns 0 only when this single run is clean; every failure prints its own line.
probe_run_once() {
  local mode_label="$1" result_file="$2" log_file="$3" expected_rc="$4" expected_result="$5"
  shift 5

  local -a extra_args
  if (( $# > 0 )); then
    extra_args=("$@")
  fi

  local rc=0
  rm -f "${result_file}"

  # -batchmode -nographics keep the player headless. -quit is deliberately NOT passed: the probe exits itself
  # through Application.Quit with a code that encodes its result.
  while pgrep -f 'gc-wt/gc-026/.*[G]ameCoreProbe|gc-wt/gc-026/.*[U]nity ' >/dev/null; do sleep 60; done
  timeout --signal=TERM --kill-after=10 600 "${PROBE_PLAYER}" \
    -batchmode \
    -nographics \
    -logFile "${log_file}" \
    ${extra_args[@]+"${extra_args[@]}"} \
    -probeResult "${result_file}" || rc=$?

  local failed=0

  # A signal death is a crash, not a verdict: 139 = SIGSEGV, 134 = SIGABRT, 135 = SIGBUS, 136 = SIGFPE.
  if (( rc >= 128 )); then
    echo "   FAIL ${mode_label}: exit ${rc} (signal: crash)" >&2
    failed=1
  fi

  if [[ ! -f "${result_file}" ]]; then
    echo "   FAIL ${mode_label}: no result written to ${result_file}" >&2
    return 1
  fi

  if ! python3 -m json.tool "${result_file}" >/dev/null; then
    echo "   FAIL ${mode_label}: ${result_file} is not valid JSON" >&2
    return 1
  fi

  if ! grep -q "\"result\": \"${expected_result}\"" "${result_file}"; then
    echo "   FAIL ${mode_label}: ${result_file} does not report result ${expected_result}" >&2
    failed=1
  fi

  if (( rc != expected_rc )); then
    echo "   FAIL ${mode_label}: exit code ${rc}, expected ${expected_rc}" >&2
    failed=1
  fi

  if grep -q '"status": "Fail"' "${result_file}"; then
    echo "   FAIL ${mode_label}: at least one probe step reported status Fail" >&2
    failed=1
  fi

  if ! grep -q '"status": "Pass"' "${result_file}"; then
    echo "   FAIL ${mode_label}: no probe step reported status Pass" >&2
    failed=1
  fi

  return "${failed}"
}

# Runs the probe PROBE_RUNS times. A crash on any run fails the whole probe run, and the caller's `failures`
# counter is what turns that into a non-zero script exit.
probe_run_n() {
  local mode_label="$1" result_file="$2" log_file="$3" expected_rc="$4" expected_result="$5"
  shift 5

  local -a extra_args
  if (( $# > 0 )); then
    extra_args=("$@")
  fi

  local run failed_runs=0
  for (( run = 1; run <= PROBE_RUNS; run++ )); do
    local run_result="${result_file}" run_log="${log_file}"
    if (( run > 1 )); then
      run_result="${result_file}.run${run}"
      run_log="${log_file}.run${run}"
    fi

    echo "-- running ${mode_label} probe (run ${run}/${PROBE_RUNS})"
    if probe_run_once "${mode_label}" "${run_result}" "${run_log}" "${expected_rc}" "${expected_result}" ${extra_args[@]+"${extra_args[@]}"}; then
      echo "   ok ${mode_label}: run ${run}/${PROBE_RUNS} clean, exit ${expected_rc}, result ${expected_result}"
    else
      failed_runs=$((failed_runs + 1))
      echo "   FAIL ${mode_label}: run ${run}/${PROBE_RUNS} was not clean (log ${run_log})" >&2
    fi
  done

  if (( failed_runs != 0 )); then
    echo "   FAIL ${mode_label}: ${failed_runs} of ${PROBE_RUNS} run(s) were not clean; run 1 evidence is ${result_file} / ${log_file}" >&2
    return 1
  fi

  return 0
}

# Asserts that every `"name": "<step>"` fragment appears in the result JSON, one named message per missing step.
probe_require_steps() {
  local result_file="$1"
  shift
  if (( $# == 0 )); then
    return 0
  fi

  local fragment
  for fragment in "$@"; do
    if ! grep -q "${fragment}" "${result_file}"; then
      echo "   FAIL ${PROBE_LABEL:-probe}: required probe step ${fragment} is absent" >&2
      failures=$((failures + 1))
    fi
  done
}

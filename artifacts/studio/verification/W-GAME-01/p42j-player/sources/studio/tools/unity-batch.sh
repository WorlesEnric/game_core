#!/usr/bin/env bash
# unity-batch.sh - run ONE batchmode Unity Editor on an arbitrary project directory under the host-wide Unity lock.
#
# Usage (on the Linux host only):
#   studio/tools/unity-batch.sh --project <abs-project-dir> --log-dir <dir> --label <name> \
#       [--results <xml>] [--timeout <seconds>] [--attempts 1|2] -- <extra Unity args...>
# Examples:
#   studio/tools/unity-batch.sh --project ~/.cache/gamecore-studio/stage/cs-01ja/project \
#       --log-dir ~/.cache/gamecore-studio/stage/cs-01ja/out/logs --label editmode \
#       --results ~/.cache/gamecore-studio/stage/cs-01ja/out/editmode.xml -- -runTests -testPlatform EditMode
#   studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" --log-dir /tmp/l --label admit -- \
#       -executeMethod GameCore.Studio.Edit.StageCommandLine.Admit
#
# All four launchers source unity-slot.sh. Its allocator mutex covers reservation plus the count of
# interactive/batch Editors and outstanding reservations (AssetImportWorker children are excluded).
# At most three host-wide Editors; this wrapper holds one reservation until its child is reaped.
# Each attempt has a deadline and a silence watchdog. Only the documented missing
# /tmp/ilpp.sock-* startup fault retries once (unless --attempts 1). See tools/PACKET.md.
# Logs use -logFile - and run-redacted.py: stdout/stderr are redacted before any durable write.
# TERM/INT/HUP reach the entire child process group, with KILL after eight seconds if necessary.
# Add -quit for a plain compile; tests and StageCommandLine entries exit themselves.
# --require-test <fullname> is repeatable with --results; every selected and required XML case must pass.
# Skipped, inconclusive, absent and malformed results never count as acceptance.
#
# Operator environment: UNITY (Editor command), GC_STUDIO_REMOTE_BASE (default wkspace/gc-studio),
# GC_STUDIO_UNITY_SLOTS (1..3, default 3), UNITY_TIMEOUT (default 1500 seconds),
# UNITY_SILENCE_TIMEOUT (default 600 seconds; 0 disables silence detection).
# Non-secret GAMECORE_* config/gates pass through; credential/proxy names are excluded
# by run-redacted.py. Env-gated XML skips report SKIPPED (env-gated), with gate names.
# Service launches must use trusted tool configuration and reserve outside the Docker sandbox.
# Exit: 0 successful compile/all-passed XML; 1 failure or partial/missing XML; 124 timeout; 2 usage.
set -euo pipefail

usage() {
  sed -n '2,/^set -/p' "${BASH_SOURCE[0]:-$0}" | sed '/^set -/d' 2>/dev/null | sed 's/^# \{0,1\}//' >&2 || true
  exit 2
}

project=""
log_dir=""
label=""
results=""
required_tests=()
attempt_timeout="${UNITY_TIMEOUT:-1500}"
max_attempts=2
while [[ $# -gt 0 ]]; do
  case "$1" in
    --project) [[ $# -ge 2 ]] || usage; project="$2"; shift 2 ;;
    --log-dir) [[ $# -ge 2 ]] || usage; log_dir="$2"; shift 2 ;;
    --label) [[ $# -ge 2 ]] || usage; label="$2"; shift 2 ;;
    --results) [[ $# -ge 2 ]] || usage; results="$2"; shift 2 ;;
    --require-test) [[ $# -ge 2 ]] || usage; required_tests+=("$2"); shift 2 ;;
    --timeout) [[ $# -ge 2 ]] || usage; attempt_timeout="$2"; shift 2 ;;
    --attempts) [[ $# -ge 2 ]] || usage; max_attempts="$2"; shift 2 ;;
    --) shift; break ;;
    *) echo "unity-batch.sh: unknown argument '$1'" >&2; usage ;;
  esac
done
extra=("$@")
for arg in "${extra[@]}"; do
  case "${arg,,}" in -logfile|-projectpath|-testresults) echo 'reserved Unity argument' >&2; exit 2;; esac
done

if (( ${#required_tests[@]} > 0 )) && [[ -z "$results" ]]; then echo "--require-test needs --results" >&2; exit 2; fi
if [[ -z "${project}" || -z "${log_dir}" || -z "${label}" ]]; then
  usage
fi
if ! [[ "${label}" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ ]]; then
  echo "unity-batch.sh: label must match [A-Za-z0-9][A-Za-z0-9._-]*: '${label}'" >&2
  exit 2
fi
if ! [[ "${attempt_timeout}" =~ ^[0-9]+$ ]] || (( attempt_timeout < 10 )); then
  echo "unity-batch.sh: --timeout takes whole seconds >= 10" >&2
  exit 2
fi
if [[ "${max_attempts}" != "1" && "${max_attempts}" != "2" ]]; then
  echo "unity-batch.sh: --attempts takes 1 or 2" >&2
  exit 2
fi
if [[ "$(uname -s)" != "Linux" ]]; then
  echo "unity-batch.sh: runs on the Linux build host only (owner rule: no Unity on the Mac)" >&2
  exit 2
fi

unity="${UNITY:-${HOME}/Unity/Hub/Editor/6000.0.75f1/Editor/Unity}"
remote_base="${GC_STUDIO_REMOTE_BASE:-wkspace/gc-studio}"
slots="${GC_STUDIO_UNITY_SLOTS:-3}"
silence_limit="${UNITY_SILENCE_TIMEOUT:-600}"
slot_dir="${HOME}/${remote_base}/.unity-slots"

if [[ ! -x "${unity}" ]]; then
  echo "unity-batch.sh: Unity Editor not executable: ${unity}" >&2
  exit 2
fi
if [[ ! -d "${project}/ProjectSettings" || ! -f "${project}/Packages/manifest.json" ]]; then
  echo "unity-batch.sh: no Unity project at ${project}" >&2
  exit 2
fi
for tool in timeout flock pgrep; do
  command -v "${tool}" >/dev/null 2>&1 || { echo "unity-batch.sh: '${tool}' is required" >&2; exit 2; }
done
mkdir -p "${log_dir}" "${slot_dir}"

unity_tools_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "${unity_tools_dir}/unity-slot.sh"
unity_slot_acquire
trap unity_slot_release EXIT
stamp="$(date +%Y%m%dT%H%M%S)-$$"

rc=0
log=""
attempts_run=0
start="$(date +%s)"
watched=""
# A caller that gives up (the stage runner's B-STAGE deadline, Ctrl-C) signals this script's process group, but
# `timeout` runs the Editor in its own process group: forward the signal so no Editor outlives its runner (and keeps
# holding a host-wide Unity slot through the inherited lock descriptor).
stop_editor() {
  if [[ -n "${watched}" ]] && kill -0 "${watched}" 2>/dev/null; then
    echo "-- interrupted: stopping the Editor" >&2
    kill -TERM "${watched}" 2>/dev/null || true
    # The redacting child kills its entire process group after eight seconds; allow it to reap.
    for _ in 1 2 3 4 5 6 7 8 9 10 11 12; do
      kill -0 "${watched}" 2>/dev/null || break
      sleep 1
    done
    pkill -KILL -P "${watched}" 2>/dev/null || true
    kill -KILL "${watched}" 2>/dev/null || true
  fi
  exit 143
}
trap stop_editor TERM INT HUP
for ((attempt = 1; attempt <= max_attempts; attempt++)); do
  attempts_run="${attempt}"
  log="${log_dir}/${label}-${stamp}-a${attempt}.log"
  args=(-batchmode -nographics -projectPath "${project}" -logFile -)
  if [[ -n "${results}" ]]; then
    rm -f "${results}"
    args+=(-testResults "${results}")
  fi
  args+=("${extra[@]}")
  echo "-- attempt ${attempt}/${max_attempts}: Unity (arguments withheld from logs)"
  rc=0
  attempt_start="$(date +%s.%N)"
  python3 "${unity_tools_dir}/../stage/run-redacted.py" --log "$log" --timeout "$attempt_timeout" --silence "$silence_limit" -- "${unity}" "${args[@]}" &
  watched=$!
  wait "$watched" || rc=$?
  watched=""
  if (( rc == 124 || rc == 137 )); then
    echo "   Unity Editor timed out (limit ${attempt_timeout}s, silence limit ${silence_limit}s; exit ${rc}, attempt ${attempt}/${max_attempts})" >&2
    break
  fi
  if grep -qE "Can't find file /tmp/ilpp[.]sock-[A-Za-z0-9]+" "$log"; then
    rc=1
    startup_errors=$(python3 "${unity_tools_dir}/unity-diagnostics.py" errors "$log" "$project" "$attempt_start")
    if (( attempt < max_attempts )) && [[ -z "$startup_errors" ]] && [[ -z "$results" || ! -s "$results" ]]; then
      echo "-- retrying once after documented ILPP startup fault: Can't find file /tmp/ilpp.sock-* (log ${log})"
      continue
    fi
    echo '-- ILPP fault: retry exhausted/disabled or compile/test evidence present; failing this invocation'
  fi
  break
done
unity_rc="$rc"
elapsed=$(( $(date +%s) - start ))
env_gates=""
if [[ -n "$results" && -s "$results" && "$rc" != 124 && "$rc" != 137 ]]; then
  summary_rc=0
  python3 "${unity_tools_dir}/../stage/test-results.py" "$results" "${required_tests[@]}" || summary_rc=$?
  if (( summary_rc == 0 && (rc == 0 || rc == 2) )); then rc=0; else rc=1; fi
  if (( summary_rc == 2 && (unity_rc == 0 || unity_rc == 2) )); then
    env_gates=$(python3 "${unity_tools_dir}/unity-diagnostics.py" env-gates "$results")
  fi
fi

compiler_errors=$(python3 "${unity_tools_dir}/unity-diagnostics.py" errors "$log" "$project" "$attempt_start")
if [[ -n "$compiler_errors" ]]; then
  echo '-- compiler diagnostics (Editor / current Bee log):'
  printf '%s\n' "$compiler_errors"
  env_gates=""
  if (( rc != 124 && rc != 137 )); then rc=1; fi
fi
errors="$(grep -E 'error CS[0-9]+|Scripts have compiler errors|Aborting batchmode|An error occurred while resolving packages|\[Package Manager\].*[Ee]rror|Compilation failed|Fatal Error' "${log}" 2>/dev/null | sort -u | head -n 200 || true)"
if [[ -n "${errors}" ]]; then
  echo "-- error lines from ${log}:"
  printf '%s\n' "${errors}" | sed 's/^/   /'
fi

if grep -qE 'error CS[0-9]+|Scripts have compiler errors|Compilation failed' "$log" 2>/dev/null; then
  if (( rc != 124 && rc != 137 )); then rc=1; fi
fi
verdict="PASS"
exit_code=0
if (( rc == 124 || rc == 137 )); then
  verdict="TIMEOUT"
  exit_code=124
elif (( rc != 0 )); then
  verdict="FAIL"
  if [[ -n "$env_gates" ]]; then verdict="SKIPPED (env-gated): ${env_gates}"; fi
  exit_code=1
elif [[ -n "${results}" && ! -s "${results}" ]]; then
  echo "   no test results at ${results}: NotRun, not Pass" >&2
  verdict="FAIL"
  exit_code=1
fi
echo "RESULT ${label}: ${verdict} (unity exit ${unity_rc}, ${elapsed}s, attempts ${attempts_run}, log ${log})"
exit "${exit_code}"

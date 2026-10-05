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
#       -executeMethod GameCore.Studio.Stage.StageCommandLine.Admit
#
# This is the lock protocol of studio/tools/unity-compile.sh, factored for callers that are not a packet clone
# (the P2.4 stage runner launches batchmode Editors on staging slots under ~/.cache, and on the live project for
# admission). It uses THE SAME lock directory and rules, so all callers share the host budget:
#   * at most GC_STUDIO_UNITY_SLOTS (default 3) batchmode Editors host-wide: a slot is a flock on
#     ~/${GC_STUDIO_REMOTE_BASE:-wkspace/gc-studio}/.unity-slots/slot<N>.lock (released when this script exits,
#     even on a crash), and a slot is only taken while fewer than that many batchmode Editors run host-wide
#     (Editors started by any tool count; asset import workers do not);
#   * this script holds exactly one slot and runs exactly one Editor at a time;
#   * every attempt is bounded by `timeout --kill-after=60 <timeout>` and by a log-silence watchdog
#     (UNITY_SILENCE_TIMEOUT, default 600 s: the repository's hang threshold, docs/operator/editor-hang.md);
#   * a timeout or silence kill (exit 124/137) is retried exactly once (the known package-resolve /
#     pre-dispatch hang); any other exit is never retried and a killed run is never a pass.
# Extra Unity args are appended after `-batchmode -nographics -projectPath <dir> -logFile <log>`; add `-quit`
# yourself for a plain compile (the test runner and an -executeMethod that calls EditorApplication.Exit quit by
# themselves). With --results, `-testResults <xml>` is added and a missing/empty XML is reported.
#
# Environment:
#   UNITY                  Editor binary (default: ~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity)
#   GC_STUDIO_REMOTE_BASE  directory under $HOME holding .unity-slots (default: wkspace/gc-studio)
#   GC_STUDIO_UNITY_SLOTS  host-wide concurrent batchmode Editors allowed (default: 3)
#   UNITY_SILENCE_TIMEOUT  seconds without log growth before an attempt is killed as hung (default 600; 0 = off)
#
# Output: the Editor's error lines, then one line `RESULT <label>: PASS|FAIL|TIMEOUT (unity exit <n>, <s>s,
# attempts <a>, log <path>)`. Exit codes: 0 Editor exit 0; 1 Editor exit non-zero (or no results XML when
# --results was given); 124 timed out on every attempt; 2 bad usage or missing Editor/project.
set -euo pipefail

usage() {
  sed -n '2,40p' "${BASH_SOURCE[0]:-$0}" 2>/dev/null | sed 's/^# \{0,1\}//' >&2 || true
  exit 2
}

project=""
log_dir=""
label=""
results=""
attempt_timeout="${UNITY_TIMEOUT:-1500}"
max_attempts=2
while [[ $# -gt 0 ]]; do
  case "$1" in
    --project) [[ $# -ge 2 ]] || usage; project="$2"; shift 2 ;;
    --log-dir) [[ $# -ge 2 ]] || usage; log_dir="$2"; shift 2 ;;
    --label) [[ $# -ge 2 ]] || usage; label="$2"; shift 2 ;;
    --results) [[ $# -ge 2 ]] || usage; results="$2"; shift 2 ;;
    --timeout) [[ $# -ge 2 ]] || usage; attempt_timeout="$2"; shift 2 ;;
    --attempts) [[ $# -ge 2 ]] || usage; max_attempts="$2"; shift 2 ;;
    --) shift; break ;;
    *) echo "unity-batch.sh: unknown argument '$1'" >&2; usage ;;
  esac
done
extra=("$@")

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

# Batchmode Editors running host-wide, whoever started them (same rule as unity-compile.sh).
count_batchmode_editors() {
  local pid n=0
  for pid in $(pgrep -x Unity 2>/dev/null || true); do
    if tr '\0' ' ' < "/proc/${pid}/cmdline" 2>/dev/null | grep -- '-batchmode' | grep -vq 'AssetImportWorker'; then
      n=$((n + 1))
    fi
  done
  echo "${n}"
}

acquire_slot() {
  local waited=0 i fd running
  while true; do
    for ((i = 1; i <= slots; i++)); do
      exec {fd}>"${slot_dir}/slot${i}.lock"
      if flock -n "${fd}"; then
        running="$(count_batchmode_editors)"
        if (( running < slots )); then
          printf '%s pid=%s caller=unity-batch label=%s project=%s since=%s\n' "$(hostname)" "$$" "${label}" \
            "${project}" "$(date -Is)" > "${slot_dir}/slot${i}.owner"
          echo "-- Unity slot ${i}/${slots} acquired (${running} batchmode Editor(s) already running host-wide)"
          return 0
        fi
        flock -u "${fd}"
      fi
      exec {fd}>&-
    done
    if (( waited % 60 == 0 )); then
      echo "-- waiting for a Unity slot (${slots} max host-wide; waited ${waited}s)"
    fi
    sleep 10
    waited=$((waited + 10))
  done
}

stamp="$(date +%Y%m%dT%H%M%S)"
acquire_slot

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
    # At most 5 s (watch-loop sleep) + 8 s, inside the stage runner's 15 s grace before it kills this group.
    for _ in 1 2 3 4 5 6 7 8; do
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
  args=(-batchmode -nographics -projectPath "${project}" -logFile "${log}")
  if [[ -n "${results}" ]]; then
    rm -f "${results}"
    args+=(-testResults "${results}")
  fi
  args+=("${extra[@]}")
  echo "-- attempt ${attempt}/${max_attempts}: ${unity} ${args[*]}"
  rc=0
  attempt_start="$(date +%s)"
  timeout --signal=TERM --kill-after=60 "${attempt_timeout}" "${unity}" "${args[@]}" &
  watched=$!
  silenced=0
  while kill -0 "${watched}" 2>/dev/null; do
    sleep 5
    if (( silence_limit > 0 )); then
      now="$(date +%s)"
      last="${attempt_start}"
      if [[ -f "${log}" ]]; then
        last="$(stat -c %Y "${log}")"
      fi
      if (( now - last > silence_limit )); then
        echo "   Unity log silent for $((now - last))s (> ${silence_limit}s): killing the Editor as hung" >&2
        silenced=1
        pkill -TERM -P "${watched}" 2>/dev/null || true
        for _ in 1 2 3 4 5 6 7 8 9 10 11 12; do
          kill -0 "${watched}" 2>/dev/null || break
          sleep 5
        done
        pkill -KILL -P "${watched}" 2>/dev/null || true
        break
      fi
    fi
  done
  wait "${watched}" || rc=$?
  if (( silenced == 1 )); then
    rc=124
  fi
  if (( rc == 124 || rc == 137 )); then
    echo "   Unity Editor timed out (limit ${attempt_timeout}s, silence limit ${silence_limit}s; exit ${rc}, attempt ${attempt}/${max_attempts})" >&2
    if (( attempt < max_attempts )); then
      echo "-- retrying once after a timeout (known intermittent hang; docs/operator/editor-hang.md)"
      continue
    fi
  fi
  break
done
elapsed=$(( $(date +%s) - start ))

errors="$(grep -E 'error CS[0-9]+|Scripts have compiler errors|Aborting batchmode|An error occurred while resolving packages|\[Package Manager\].*[Ee]rror|Compilation failed|Fatal Error' "${log}" 2>/dev/null | sort -u | head -n 200 || true)"
if [[ -n "${errors}" ]]; then
  echo "-- error lines from ${log}:"
  printf '%s\n' "${errors}" | sed 's/^/   /'
fi

verdict="PASS"
exit_code=0
if (( rc == 124 || rc == 137 )); then
  verdict="TIMEOUT"
  exit_code=124
elif (( rc != 0 )); then
  verdict="FAIL"
  exit_code=1
elif [[ -n "${results}" && ! -s "${results}" ]]; then
  echo "   no test results at ${results}: NotRun, not Pass" >&2
  verdict="FAIL"
  exit_code=1
fi
echo "RESULT ${label}: ${verdict} (unity exit ${rc}, ${elapsed}s, attempts ${attempts_run}, log ${log})"
exit "${exit_code}"

#!/usr/bin/env bash
# GC-022 Play Mode reload matrix runner.
#
# Runs the 2x2 of {domain reload, scene reload} settings, every combination in its OWN Unity Editor process
# invocation, ten real Play Mode enter/exit cycles per combination (GC-022 Wave 6, TEST-018/023). Each combination
# is driven by GameCore.Validation.Editor.LifecyclePlayModeMatrix.Run through -executeMethod, which appends one
# JSONL line per observable cycle step and one JSON summary per invocation; this script owns the process
# supervision, the evidence layout and the verdict.
#
# Why one process per combination: the Unity Editor has an unresolved intermittent pre-dispatch hang (GC-014
# BUILD_REPORT section "Fixes" item 5). A hang in one combination must not lose the other three, and the hang must
# be recorded as a frequency rather than reported as a pass. So every Unity invocation is wrapped in
# `timeout --signal=TERM --kill-after=60`, a combination killed by the watchdog gets a `*.timeout` marker and
# exactly ONE sanctioned retry, and a combination that still does not complete is reported as Timeout (a nonzero
# script exit), never as Pass.
#
# -quit is deliberately NOT passed (the Editor exits itself through EditorApplication.Exit with a code that
# encodes its result, and -runTests must never be combined with -quit anyway). No audio flag is passed either:
# the qualification project already disables the audio device in ProjectSettings/AudioManager.asset
# (m_DisableAudio: 1), which is the configuration the crash-139 investigation fixed.
#
# Required environment:
#   UNITY          Unity Editor executable of the pinned revision (e.g. ~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity)
#
# Optional environment:
#   UNITY_PROJECT        Unity project (default <repo>/unity/GameCore.Validation)
#   ARTIFACTS            evidence directory (default <repo>/artifacts/gc-022/playmode-matrix)
#   UNITY_TIMEOUT        watchdog seconds per Unity invocation (default 1800)
#   MATRIX_COMBINATIONS  space-separated subset to run (default all four, in the order below)
#   MATRIX_CYCLES        enter/exit cycles per combination (default 10)
#
# Exit codes:
#   0   every requested combination reported Pass with complete evidence
#   1   a combination failed an assertion, or its evidence is missing/invalid
#   2   a combination was killed by the watchdog after its single retry (and nothing failed an assertion)
#   64  usage or configuration error (missing UNITY, unknown combination, bad cycle count, missing tool)
set -euo pipefail
export LC_ALL=C

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"

DEFAULT_COMBINATIONS="reload-on-scene-on reload-on-scene-off reload-off-scene-on reload-off-scene-off"
EXECUTE_METHOD="GameCore.Validation.Editor.LifecyclePlayModeMatrix.Run"

UNITY="${UNITY:-}"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/gc-022/playmode-matrix}"
UNITY_TIMEOUT="${UNITY_TIMEOUT:-1800}"
MATRIX_CYCLES="${MATRIX_CYCLES:-10}"
MATRIX_COMBINATIONS="${MATRIX_COMBINATIONS:-${DEFAULT_COMBINATIONS}}"

die() {
  echo "run_lifecycle_playmode_matrix.sh: $*" >&2
  exit 64
}

if [[ -z "${UNITY}" ]]; then
  die "UNITY is required: set it to the Unity Editor executable of the pinned revision (6000.0.75f1)."
fi
if [[ ! -x "${UNITY}" ]]; then
  die "UNITY is not an executable file: ${UNITY}"
fi
UNITY="$(cd "$(dirname "${UNITY}")" && pwd)/$(basename "${UNITY}")"

if [[ ! -d "${UNITY_PROJECT}" ]]; then
  die "UNITY_PROJECT is not a directory: ${UNITY_PROJECT}"
fi
UNITY_PROJECT="$(cd "${UNITY_PROJECT}" && pwd)"

mkdir -p "${ARTIFACTS}"
ARTIFACTS="$(cd "${ARTIFACTS}" && pwd)"

if [[ ! "${MATRIX_CYCLES}" =~ ^[0-9]+$ ]] || (( MATRIX_CYCLES < 1 )); then
  die "MATRIX_CYCLES must be a positive integer, got '${MATRIX_CYCLES}'"
fi
if [[ ! "${UNITY_TIMEOUT}" =~ ^[0-9]+$ ]] || (( UNITY_TIMEOUT < 1 )); then
  die "UNITY_TIMEOUT must be a positive number of seconds, got '${UNITY_TIMEOUT}'"
fi
command -v timeout >/dev/null 2>&1 || die "the 'timeout' utility (GNU coreutils) is required for the watchdog."
command -v python3 >/dev/null 2>&1 || die "python3 is required to validate the JSON evidence."

read -r -a combinations <<< "${MATRIX_COMBINATIONS}" || true
if (( ${#combinations[@]} == 0 )); then
  die "MATRIX_COMBINATIONS is empty; expected a space-separated subset of: ${DEFAULT_COMBINATIONS}"
fi
for combination in "${combinations[@]}"; do
  case "${combination}" in
    reload-on-scene-on|reload-on-scene-off|reload-off-scene-on|reload-off-scene-off) ;;
    *) die "unknown combination '${combination}'; expected one of: ${DEFAULT_COMBINATIONS}" ;;
  esac
done

# One invocation of the Editor for one combination. Returns the process exit code; 124/137 mean the watchdog fired.
run_attempt() {
  local combination="$1" attempt="$2"
  local log_file="${ARTIFACTS}/${combination}.log"
  local -a unity_command=(
    timeout --signal=TERM --kill-after=60 "${UNITY_TIMEOUT}"
    "${UNITY}"
    -batchmode
    -nographics
    -projectPath "${UNITY_PROJECT}"
    -executeMethod "${EXECUTE_METHOD}"
    -gc022Combination "${combination}"
    -gc022Cycles "${MATRIX_CYCLES}"
    -gc022OutputDirectory "${ARTIFACTS}"
    -logFile "${log_file}"
  )

  local -a quoted=()
  local argument
  for argument in "${unity_command[@]}"; do
    quoted+=("$(printf '%q' "${argument}")")
  done

  echo "-- Unity command for ${combination} (attempt ${attempt}, watchdog ${UNITY_TIMEOUT}s):"
  echo "   UNITY=${UNITY} GC022_COMBINATION=${combination} GC022_CYCLES=${MATRIX_CYCLES} GC022_OUTPUT_DIRECTORY=${ARTIFACTS}"
  echo "   ${quoted[*]}"
  {
    echo "# $(date -u '+%Y-%m-%dT%H:%M:%SZ') combination=${combination} attempt=${attempt} watchdog=${UNITY_TIMEOUT}s"
    echo "UNITY=$(printf '%q' "${UNITY}") GC022_COMBINATION=$(printf '%q' "${combination}") GC022_CYCLES=$(printf '%q' "${MATRIX_CYCLES}") GC022_OUTPUT_DIRECTORY=$(printf '%q' "${ARTIFACTS}") ${quoted[*]}"
    echo
  } >> "${ARTIFACTS}/commands.txt"

  local rc=0
  # The GC022_* environment variables mirror the command-line arguments: the Editor reads the arguments first and
  # falls back to these, so a stripped or mis-forwarded argument cannot silently run the wrong combination.
  GC022_COMBINATION="${combination}" GC022_CYCLES="${MATRIX_CYCLES}" GC022_OUTPUT_DIRECTORY="${ARTIFACTS}" \
    "${unity_command[@]}" || rc=$?

  return "${rc}"
}

# Counts the JSONL's per-cycle Pass lines, which is the on-disk evidence that a cycle really finished.
pass_cycle_count() {
  local jsonl_file="$1"
  if [[ ! -f "${jsonl_file}" ]]; then
    echo "0"
    return 0
  fi

  local count
  count="$(grep -c '"status": "Pass"' "${jsonl_file}" 2>/dev/null || true)"
  if [[ -z "${count}" ]]; then
    count=0
  fi
  echo "${count}"
}

# Validates the Editor's own summary JSON: the right combination, result Pass, all requested cycles completed.
summary_matches() {
  local summary_file="$1" expected_result="$2" expected_cycles="$3" expected_combination="$4"
  if [[ ! -s "${summary_file}" ]]; then
    echo "   no summary file at ${summary_file}"
    return 1
  fi

  python3 - "${summary_file}" "${expected_result}" "${expected_cycles}" "${expected_combination}" <<'PYSCRIPT'
import json
import sys

try:
    with open(sys.argv[1], "r", encoding="utf-8") as handle:
        summary = json.load(handle)
except Exception as error:
    print("   the summary is not readable JSON: %s" % (error,))
    sys.exit(1)

problems = []
if summary.get("combination") != sys.argv[4]:
    problems.append("combination is %r, expected %r" % (summary.get("combination"), sys.argv[4]))
if summary.get("result") != sys.argv[2]:
    problems.append("result is %r, expected %r" % (summary.get("result"), sys.argv[2]))
if int(summary.get("cyclesCompleted", -1)) != int(sys.argv[3]):
    problems.append("cyclesCompleted is %r, expected %s" % (summary.get("cyclesCompleted"), sys.argv[3]))
if int(summary.get("cyclesRequested", -1)) != int(sys.argv[3]):
    problems.append("cyclesRequested is %r, expected %s" % (summary.get("cyclesRequested"), sys.argv[3]))
if int(summary.get("failureCount", -1)) != 0:
    problems.append("failureCount is %r, expected 0" % (summary.get("failureCount"),))

for problem in problems:
    print("   the summary does not match: " + problem)
sys.exit(1 if problems else 0)
PYSCRIPT
}

# Writes a truthful runner-owned summary. It is marked source=runner and never reports Pass, so it can only ever
# make a run less confident. The default mode keeps an existing Editor summary untouched (the assertion-failure and
# watchdog cases have nothing to correct); mode "replace" moves a contradicting Editor summary to
# <combination>-summary.editor.json instead of discarding it, and writes the runner's verdict in its place.
write_runner_summary() {
  local combination="$1" result="$2" detail="$3" elapsed="$4" mode="${5:-keep}"
  local jsonl_file="${ARTIFACTS}/${combination}.jsonl"
  local summary_file="${ARTIFACTS}/${combination}-summary.json"
  if [[ -s "${summary_file}" ]]; then
    if [[ "${mode}" != "replace" ]]; then
      return 0
    fi
    mv -f "${summary_file}" "${ARTIFACTS}/${combination}-summary.editor.json" 2>/dev/null || true
  fi
  if [[ ! -f "${jsonl_file}" ]]; then
    : > "${jsonl_file}"
  fi

  python3 - "${jsonl_file}" "${summary_file}" "${combination}" "${result}" "${detail}" "${MATRIX_CYCLES}" "${elapsed}" <<'PYSCRIPT'
import json
import sys

jsonl_path = sys.argv[1]
summary_path = sys.argv[2]
combination = sys.argv[3]
result = sys.argv[4]
detail = sys.argv[5]
cycles = int(sys.argv[6])
elapsed = float(sys.argv[7])

attempted = 0
completed = 0
last_cycle = 0
with open(jsonl_path, "r", encoding="utf-8") as handle:
    for line in handle:
        line = line.strip()
        if not line:
            continue
        try:
            record = json.loads(line)
        except ValueError:
            continue
        if record.get("kind") == "cycle-begin":
            attempted += 1
            last_cycle = max(last_cycle, int(record.get("cycle", 0)))
        elif record.get("kind") == "cycle-result" and record.get("status") == "Pass":
            completed += 1

summary = {
    "kind": "summary",
    "source": "runner",
    "combination": combination,
    "result": result,
    "cyclesAttempted": attempted,
    "cyclesCompleted": completed,
    "cyclesRequested": cycles,
    "combinationsCompleted": 0,
    "wallClockSecondsPerCombination": elapsed,
    "lastCycleBegin": last_cycle,
    "detail": detail,
}
with open(summary_path, "w", encoding="utf-8") as handle:
    json.dump(summary, handle, indent=2)
    handle.write("\n")
print("   runner summary written to " + summary_path)
PYSCRIPT
}

# Aggregates every combination's verdict into matrix-summary.json and returns the five headline fields.
write_matrix_summary() {
  local overall="$1" exit_code="$2" fail_count="$3" timeout_count="$4" hang_observations="$5"
  local -a records=()
  local index
  for (( index = 0; index < ${#comb_names[@]}; index++ )); do
    records+=("${comb_names[$index]}|${comb_results[$index]}|${comb_cycles[$index]}|${comb_attempts[$index]}|${comb_kills[$index]}|${comb_seconds[$index]}|${comb_notes[$index]}")
  done

  python3 - "${ARTIFACTS}/matrix-summary.json" "${overall}" "${exit_code}" "${fail_count}" "${timeout_count}" "${hang_observations}" "${#comb_names[@]}" ${records[@]+"${records[@]}"} <<'PYSCRIPT'
import json
import sys

out_path = sys.argv[1]
overall = sys.argv[2]
exit_code = int(sys.argv[3])
fail_count = int(sys.argv[4])
timeout_count = int(sys.argv[5])
hang_observations = int(sys.argv[6])
combination_count = int(sys.argv[7])
records = sys.argv[8:]

combinations = []
wall_clock = {}
completed = 0
attempted = 0
passed = 0
for record in records:
    name, result, cycles, attempts, kills, seconds, note = record.split("|", 6)
    finished, requested = cycles.split("/", 1)
    finished = int(finished)
    requested = int(requested)
    completed += finished
    attempted += requested
    if result == "Pass":
        passed += 1
    wall_clock[name] = float(seconds)
    combinations.append({
        "combination": name,
        "result": result,
        "cyclesCompleted": finished,
        "cyclesRequested": requested,
        "attempts": int(attempts),
        "watchdogKills": int(kills),
        "wallClockSeconds": float(seconds),
        "note": note,
    })

summary = {
    "kind": "matrix-summary",
    "source": "runner",
    "result": overall,
    "exitCode": exit_code,
    "cyclesCompleted": completed,
    "cyclesAttempted": attempted,
    "cyclesRequested": sum(entry["cyclesRequested"] for entry in combinations),
    "combinationsCompleted": passed,
    "combinationsRequested": combination_count,
    "combinationsFailed": fail_count,
    "combinationsTimedOut": timeout_count,
    "watchdogKills": hang_observations,
    "wallClockSecondsPerCombination": wall_clock,
    "combinations": combinations,
}
with open(out_path, "w", encoding="utf-8") as handle:
    json.dump(summary, handle, indent=2)
    handle.write("\n")
print(out_path)
PYSCRIPT
}

echo "== GC-022 Play Mode reload matrix =="
echo "repo         : ${REPO_ROOT}"
echo "unity        : ${UNITY}"
echo "project      : ${UNITY_PROJECT}"
echo "artifacts    : ${ARTIFACTS}"
echo "combinations : ${combinations[*]}"
echo "cycles       : ${MATRIX_CYCLES} per combination"
echo "watchdog     : timeout --signal=TERM --kill-after=60 ${UNITY_TIMEOUT} (one retry per combination on 124/137)"
: > "${ARTIFACTS}/commands.txt"

comb_names=()
comb_results=()
comb_cycles=()
comb_attempts=()
comb_kills=()
comb_seconds=()
comb_notes=()

fail_count=0
timeout_count=0
hang_observations=0

for combination in "${combinations[@]}"; do
  echo
  echo "== combination ${combination} =="
  jsonl_file="${ARTIFACTS}/${combination}.jsonl"
  summary_file="${ARTIFACTS}/${combination}-summary.json"
  log_file="${ARTIFACTS}/${combination}.log"

  # A rerun of this script owns its evidence: clear every artifact of the previous attempt for this combination.
  rm -f "${log_file}" "${jsonl_file}" "${summary_file}" \
    "${ARTIFACTS}/${combination}.timeout" \
    "${ARTIFACTS}/${combination}.attempt1.timeout" \
    "${ARTIFACTS}/${combination}.attempt1.log" \
    "${ARTIFACTS}/${combination}.attempt1.jsonl" \
    "${ARTIFACTS}/${combination}-summary.attempt1.json" \
    "${ARTIFACTS}/${combination}-summary.editor.json"

  started="${SECONDS}"
  attempt=1
  kills=0
  rc=0
  run_attempt "${combination}" "${attempt}" || rc=$?

  if (( rc == 124 || rc == 137 )); then
    kills=$((kills + 1))
    echo "   the watchdog killed the invocation after ${UNITY_TIMEOUT}s (exit ${rc})"
    # Preserve the killed attempt's evidence under explicit names, then use the one sanctioned retry.
    mv -f "${log_file}" "${ARTIFACTS}/${combination}.attempt1.log" 2>/dev/null || true
    mv -f "${jsonl_file}" "${ARTIFACTS}/${combination}.attempt1.jsonl" 2>/dev/null || true
    mv -f "${summary_file}" "${ARTIFACTS}/${combination}-summary.attempt1.json" 2>/dev/null || true
    : > "${ARTIFACTS}/${combination}.attempt1.timeout"

    attempt=2
    rc=0
    run_attempt "${combination}" "${attempt}" || rc=$?
    if (( rc == 124 || rc == 137 )); then
      kills=$((kills + 1))
      : > "${ARTIFACTS}/${combination}.timeout"
      echo "   the watchdog killed the retry as well (exit ${rc}); the hang is recorded, not passed"
    fi
  fi

  elapsed=$((SECONDS - started))
  if [[ ! -f "${jsonl_file}" ]]; then
    : > "${jsonl_file}"
  fi
  pass_lines="$(pass_cycle_count "${jsonl_file}")"

  if (( rc == 124 || rc == 137 )); then
    result="Timeout"
    note="watchdog kill (exit ${rc}); hang recorded as data"
    write_runner_summary "${combination}" "Timeout" \
      "the Unity invocation was killed by the watchdog after ${UNITY_TIMEOUT}s (exit ${rc}); the combination never reported a result" \
      "${elapsed}"
    timeout_count=$((timeout_count + 1))
  elif (( rc == 0 )); then
    if ! summary_matches "${summary_file}" "Pass" "${MATRIX_CYCLES}" "${combination}"; then
      result="Fail"
      note="exit 0 without a passing summary"
      write_runner_summary "${combination}" "Fail" \
        "the Editor exited 0 but its summary is missing or does not report Pass over ${MATRIX_CYCLES} cycles" \
        "${elapsed}" "replace"
      fail_count=$((fail_count + 1))
    elif (( pass_lines != MATRIX_CYCLES )); then
      result="Fail"
      note="summary Pass but the JSONL holds ${pass_lines}/${MATRIX_CYCLES} cycle results"
      write_runner_summary "${combination}" "Fail" \
        "the Editor's summary reports Pass but the JSONL holds ${pass_lines} of ${MATRIX_CYCLES} per-cycle Pass records" \
        "${elapsed}" "replace"
      fail_count=$((fail_count + 1))
    else
      result="Pass"
      if (( kills > 0 )); then
        note="Pass on attempt ${attempt}; ${kills} watchdog kill(s) recorded"
      else
        note="-"
      fi
    fi
  elif (( rc == 1 )); then
    result="Fail"
    note="assertion failure (exit 1)"
    write_runner_summary "${combination}" "Fail" \
      "the Editor reported an assertion failure (exit 1); its own summary is retained when it wrote one" \
      "${elapsed}"
    fail_count=$((fail_count + 1))
  else
    result="Error"
    note="exit ${rc}"
    write_runner_summary "${combination}" "Fail" \
      "the Unity invocation exited ${rc}, which is neither success (0), an assertion failure (1) nor a watchdog kill (124/137)" \
      "${elapsed}"
    fail_count=$((fail_count + 1))
  fi

  hang_observations=$((hang_observations + kills))
  comb_names+=("${combination}")
  comb_results+=("${result}")
  comb_cycles+=("${pass_lines}/${MATRIX_CYCLES}")
  comb_attempts+=("${attempt}")
  comb_kills+=("${kills}")
  comb_seconds+=("${elapsed}")
  comb_notes+=("${note}")

  echo "   result=${result}; cycles=${pass_lines}/${MATRIX_CYCLES}; attempts=${attempt}; watchdogKills=${kills}; seconds=${elapsed}"
done

if (( fail_count > 0 )); then
  overall="Fail"
  exit_code=1
elif (( timeout_count > 0 )); then
  overall="Timeout"
  exit_code=2
else
  overall="Pass"
  exit_code=0
fi

write_matrix_summary "${overall}" "${exit_code}" "${fail_count}" "${timeout_count}" "${hang_observations}" >/dev/null

echo
echo "== combination results =="
printf '%-24s %-9s %-9s %-9s %s\n' "combination" "result" "cycles" "seconds" "note"
for (( index = 0; index < ${#comb_names[@]}; index++ )); do
  printf '%-24s %-9s %-9s %-9s %s\n' \
    "${comb_names[$index]}" "${comb_results[$index]}" "${comb_cycles[$index]}" \
    "${comb_seconds[$index]}" "${comb_notes[$index]}"
done

echo
echo "matrix result        : ${overall}"
echo "combinations passed  : $(( ${#comb_names[@]} - fail_count - timeout_count ))/${#comb_names[@]}"
echo "watchdog kills       : ${hang_observations}"
echo "matrix summary       : ${ARTIFACTS}/matrix-summary.json"
echo "per-combination logs : ${ARTIFACTS}/<combination>.log"
echo "per-combination json : ${ARTIFACTS}/<combination>.jsonl"
echo "command lines        : ${ARTIFACTS}/commands.txt"
echo "note: a Pass means every requested cycle of that combination re-entered and re-exited Play Mode with every"
echo "      assertion holding. A watchdog kill is recorded as a timeout frequency, never as a pass."

if (( exit_code != 0 )); then
  echo "== GC-022 Play Mode reload matrix FAILED (result ${overall}) ==" >&2
  exit "${exit_code}"
fi

echo "== GC-022 Play Mode reload matrix PASSED =="

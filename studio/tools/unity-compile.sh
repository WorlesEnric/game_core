#!/usr/bin/env bash
# unity-compile.sh - batchmode compile (and optional test run) of one project copy on the Linux host.
#
# Usage:
#   studio/tools/unity-compile.sh <packet-name> <project-rel-path> [--tests EditMode|PlayMode] [--filter <regex>]
# Examples:
#   studio/tools/unity-compile.sh p0.2-projects-tooling games/hollowmere
#   studio/tools/unity-compile.sh p0.2-projects-tooling unity/GameCore.Validation --tests EditMode \
#       --filter 'GameCore\.Composition\..*'
#
# The project must already be on the host at ~/wkspace/gc-studio/<packet-name>/<project-rel-path>, inside the
# packet's clone of the host hub (studio/tools/sync-to-host.sh <packet-name> puts it there). Run from the Mac,
# the script re-runs itself on the host over non-interactive ssh; run on the host, it works directly. Owner
# rule: Unity never runs on the Mac.
#
# What it does on the host:
#   * waits for one of at most GC_STUDIO_UNITY_SLOTS (default 3) host-wide Unity batchmode slots: a slot is a
#     flock on ~/wkspace/gc-studio/.unity-slots/slot<N>.lock (released automatically when this script exits,
#     even on a crash), and a slot is only taken while fewer than that many batchmode Editors run host-wide
#     (Editors started by other tools count too);
#   * without --tests: Unity -batchmode -nographics -quit -projectPath <copy> -logFile <log>, i.e. package
#     resolution, import and script compilation, no -executeMethod;
#   * with --tests: Unity -batchmode -nographics -runTests -testPlatform <mode> -testResults <xml>
#     [-testFilter <regex>] (no -quit: the test runner quits by itself);
#   * bounds every Editor run with `timeout --kill-after=60 ${UNITY_TIMEOUT:-1500}` and with a log-silence
#     watchdog (no log growth for ${UNITY_SILENCE_TIMEOUT:-600}s, the repository's hang-detection threshold; the
#     known hang also strikes after "Batchmode quit successfully invoked"), and retries exactly once on a
#     timeout or silence kill (exit 124/137), per docs/operator/editor-hang.md; any other non-zero exit is never
#     retried and a killed run is never a pass;
#   * prints the error lines of the log (compiler errors, package resolution errors, test failures);
#   * keeps logs and result XML in ~/wkspace/gc-studio/<packet-name>/.unity-logs/.
#
# Environment:
#   GC_STUDIO_HOST         ssh host when run off-host (default: myubuntu)
#   GC_STUDIO_REMOTE_BASE  directory under the host home (default: wkspace/gc-studio)
#   GC_STUDIO_UNITY_SLOTS  host-wide concurrent batchmode Editors allowed (default: 3)
#   UNITY                  Editor binary on the host (default: ~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity)
#   UNITY_TIMEOUT          seconds per Editor attempt (default: 1500)
#   UNITY_SILENCE_TIMEOUT  seconds without log growth before an attempt is killed as hung (default: 600; 0 = off)
#
# Exit codes: 0 compiled with no error (and the result XML says Passed with 0 failed; Inconclusive and skipped tests
# are listed but do not fail the run, even though Unity then exits 2); 1 compile error, failed test, missing
# test results, zero selected tests (a filter that matches nothing is not a pass) or a timeout on both
# attempts; 2 bad usage or missing project copy.
set -euo pipefail

usage() {
  sed -n '2,44p' "${BASH_SOURCE[0]:-$0}" 2>/dev/null | sed 's/^# \{0,1\}//' >&2 || true
  exit 2
}

if [[ $# -lt 2 ]]; then
  usage
fi
packet="$1"
project="$2"
shift 2
tests=""
filter=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --tests)
      [[ $# -ge 2 ]] || usage
      tests="$2"
      shift 2
      ;;
    --filter)
      [[ $# -ge 2 ]] || usage
      filter="$2"
      shift 2
      ;;
    *)
      echo "unity-compile.sh: unknown argument '$1'" >&2
      usage
      ;;
  esac
done
if ! [[ "${packet}" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ ]]; then
  echo "unity-compile.sh: packet name must match [A-Za-z0-9][A-Za-z0-9._-]*: '${packet}'" >&2
  exit 2
fi
if [[ "${project}" == /* || "${project}" == *..* ]]; then
  echo "unity-compile.sh: project path must be relative to the packet root, without '..': '${project}'" >&2
  exit 2
fi
if [[ -n "${tests}" && "${tests}" != "EditMode" && "${tests}" != "PlayMode" ]]; then
  echo "unity-compile.sh: --tests takes EditMode or PlayMode, not '${tests}'" >&2
  exit 2
fi
if [[ -n "${filter}" && -z "${tests}" ]]; then
  echo "unity-compile.sh: --filter needs --tests" >&2
  exit 2
fi

host="${GC_STUDIO_HOST:-myubuntu}"
remote_base="${GC_STUDIO_REMOTE_BASE:-wkspace/gc-studio}"
default_unity="${HOME}/Unity/Hub/Editor/6000.0.75f1/Editor/Unity"

# Off the host: re-run this very script on the host, forwarding the arguments and the tunables.
if [[ "${GC_STUDIO_ON_HOST:-0}" != "1" ]] && { [[ "$(uname -s)" != "Linux" ]] || [[ ! -x "${UNITY:-${default_unity}}" ]]; }; then
  self="${BASH_SOURCE[0]}"
  remote_env="GC_STUDIO_ON_HOST=1 GC_STUDIO_REMOTE_BASE=$(printf '%q' "${remote_base}")"
  remote_env+=" GC_STUDIO_UNITY_SLOTS=$(printf '%q' "${GC_STUDIO_UNITY_SLOTS:-3}")"
  remote_env+=" UNITY_TIMEOUT=$(printf '%q' "${UNITY_TIMEOUT:-1500}")"
  remote_env+=" UNITY_SILENCE_TIMEOUT=$(printf '%q' "${UNITY_SILENCE_TIMEOUT:-600}")"
  if [[ -n "${UNITY:-}" ]]; then
    remote_env+=" UNITY=$(printf '%q' "${UNITY}")"
  fi
  forwarded=("${packet}" "${project}")
  [[ -n "${tests}" ]] && forwarded+=(--tests "${tests}")
  [[ -n "${filter}" ]] && forwarded+=(--filter "${filter}")
  rc=0
  # shellcheck disable=SC2029  # the quoting is done here on purpose (printf %q)
  ssh -o BatchMode=yes "${host}" "${remote_env} bash -s -- $(printf '%q ' "${forwarded[@]}")" < "${self}" || rc=$?
  exit "${rc}"
fi

unity="${UNITY:-${default_unity}}"
unity_timeout="${UNITY_TIMEOUT:-1500}"
silence_limit="${UNITY_SILENCE_TIMEOUT:-600}"
slots="${GC_STUDIO_UNITY_SLOTS:-3}"
base="${HOME}/${remote_base}/${packet}"
project_dir="${base}/${project%/}"
logs="${base}/.unity-logs"
slot_dir="${HOME}/${remote_base}/.unity-slots"

if [[ ! -x "${unity}" ]]; then
  echo "unity-compile.sh: Unity Editor not executable: ${unity}" >&2
  exit 2
fi
if [[ ! -d "${project_dir}/ProjectSettings" || ! -f "${project_dir}/Packages/manifest.json" ]]; then
  echo "unity-compile.sh: no Unity project at ${project_dir} (run studio/tools/sync-to-host.sh ${packet} first)" >&2
  exit 2
fi
for tool in timeout flock python3; do
  command -v "${tool}" >/dev/null 2>&1 || { echo "unity-compile.sh: '${tool}' is required" >&2; exit 2; }
done
mkdir -p "${logs}" "${slot_dir}"

# Batchmode Editors running host-wide, whoever started them (the process name of the Editor is "Unity").
count_batchmode_editors() {
  local pid n=0
  for pid in $(pgrep -x Unity 2>/dev/null || true); do
    # Asset import workers are Editor children (`-batchMode -name AssetImportWorkerN`), not instances.
    if tr '\0' ' ' < "/proc/${pid}/cmdline" 2>/dev/null | grep -- '-batchmode' | grep -vq 'AssetImportWorker'; then
      n=$((n + 1))
    fi
  done
  echo "${n}"
}

slot_fd=""
acquire_slot() {
  local waited=0 i fd running
  while true; do
    for ((i = 1; i <= slots; i++)); do
      exec {fd}>"${slot_dir}/slot${i}.lock"
      if flock -n "${fd}"; then
        running="$(count_batchmode_editors)"
        if (( running < slots )); then
          slot_fd="${fd}"
          printf '%s pid=%s packet=%s project=%s since=%s\n' "$(hostname)" "$$" "${packet}" "${project}" \
            "$(date -Is)" > "${slot_dir}/slot${i}.owner"
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
slug="$(printf '%s' "${project%/}" | tr '/ ' '__')"
mode="compile"
[[ -n "${tests}" ]] && mode="$(printf '%s' "${tests}" | tr '[:upper:]' '[:lower:]')"

acquire_slot

rc=0
log=""
results=""
start="$(date +%s)"
for attempt in 1 2; do
  log="${logs}/${slug}-${mode}-${stamp}-a${attempt}.log"
  results="${logs}/${slug}-${mode}-${stamp}-a${attempt}.xml"
  args=(-batchmode -nographics -projectPath "${project_dir}" -logFile "${log}")
  if [[ -n "${tests}" ]]; then
    args+=(-runTests -testPlatform "${tests}" -testResults "${results}")
    [[ -n "${filter}" ]] && args+=(-testFilter "${filter}")
  else
    args+=(-quit)
  fi
  echo "-- attempt ${attempt}/2: ${unity} ${args[*]}"
  rc=0
  attempt_start="$(date +%s)"
  timeout --signal=TERM --kill-after=60 "${unity_timeout}" "${unity}" "${args[@]}" &
  watched=$!
  silenced=0
  # Log-silence watchdog: the known hang (docs/operator/editor-hang.md) shows as a log that stops moving, at
  # startup or after "Batchmode quit successfully invoked". Kill such a run early instead of waiting for the full
  # timeout; it is then handled exactly like a timeout (retry once, never a pass).
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
        echo "   (diagnose a live one with: sudo -n gdb -p <pid> -batch -ex 'thread apply all bt')" >&2
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
    echo "   Unity Editor timed out (limit ${unity_timeout}s, log-silence limit ${silence_limit}s; exit ${rc}, attempt ${attempt}/2)" >&2
    if (( attempt == 1 )); then
      echo "-- retrying once after a timeout (the Editor has an intermittent pre-dispatch hang; docs/operator/editor-hang.md)"
      continue
    fi
    echo "   FAIL: Unity Editor timed out twice; this is not the known intermittent pre-dispatch hang" >&2
  fi
  break
done
end="$(date +%s)"
elapsed=$((end - start))

# Error lines: compiler errors, package-manager errors, batchmode aborts. Deduplicated, capped.
errors="$(grep -E 'error CS[0-9]+|Scripts have compiler errors|Aborting batchmode|An error occurred while resolving packages|\[Package Manager\].*[Ee]rror|Compilation failed|Fatal Error' "${log}" 2>/dev/null | sort -u | head -n 200 || true)"
compile_errors=0
if grep -qE 'error CS[0-9]+|Scripts have compiler errors' "${log}" 2>/dev/null; then
  compile_errors=1
fi
if [[ -n "${errors}" ]]; then
  echo "-- error lines from ${log}:"
  printf '%s\n' "${errors}" | sed 's/^/   /'
fi

verdict="PASS"
if (( rc == 124 || rc == 137 )); then
  verdict="FAIL (timeout twice)"
elif [[ -z "${tests}" ]]; then
  if (( rc != 0 || compile_errors == 1 )); then
    verdict="FAIL"
  fi
else
  if [[ ! -s "${results}" ]]; then
    echo "   no test results at ${results}: the run produced no verdict, which is NotRun, not Pass" >&2
    verdict="FAIL (NotRun)"
  else
    summary_rc=0
    python3 - "${results}" <<'PY' || summary_rc=$?
import sys
import xml.etree.ElementTree as ET

root = ET.parse(sys.argv[1]).getroot()
run = root if root.tag == "test-run" else root.find(".//test-run")
attrs = run.attrib if run is not None else root.attrib
print("-- tests: result={0} total={1} passed={2} failed={3} skipped={4} inconclusive={5}".format(
    attrs.get("result"), attrs.get("total"), attrs.get("passed"), attrs.get("failed"),
    attrs.get("skipped"), attrs.get("inconclusive")))
failed = [case for case in root.iter("test-case") if case.get("result") == "Failed"]
for case in failed[:100]:
    message = case.find("./failure/message")
    text = (message.text or "").strip().splitlines()[0] if message is not None and message.text else ""
    print("   FAILED {0}: {1}".format(case.get("fullname"), text[:300]))
inconclusive = [case for case in root.iter("test-case") if case.get("result") == "Inconclusive"]
for case in inconclusive[:100]:
    message = case.find("./reason/message")
    text = (message.text or "").strip().splitlines()[0] if message is not None and message.text else ""
    print("   INCONCLUSIVE {0}: {1}".format(case.get("fullname"), text[:300]))
total = int(attrs.get("total") or 0)
sys.exit(1 if failed or total == 0 or not str(attrs.get("result", "")).startswith("Passed") else 0)
PY
    # Unity exits 2 whenever a run is not all-green, including runs whose only non-passes are Inconclusive (or
    # skipped). The XML is the verdict: result Passed, 0 failed and >0 selected is a PASS even with exit 2.
    tests_rc_ok=0
    if (( rc == 0 || (rc == 2 && summary_rc == 0) )); then
      tests_rc_ok=1
    fi
    if (( summary_rc != 0 || tests_rc_ok == 0 || compile_errors == 1 )); then
      verdict="FAIL"
    fi
  fi
fi

echo "RESULT ${mode} ${project}: ${verdict} (unity exit ${rc}, ${elapsed}s, log ${log})"
if [[ "${verdict}" != "PASS" ]]; then
  tail -n 30 "${log}" 2>/dev/null | sed 's/^/   | /' >&2 || true
  exit 1
fi
exit 0

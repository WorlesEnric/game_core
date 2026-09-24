#!/usr/bin/env bash
# GC-001 toolchain qualification: run the built probe player headless in both modes and record the evidence.
#
# Required environment:
#   python3 (strict JSON validation); the player defaults to <repo>/unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64
#
# Optional environment:
#   PROBE_PLAYER   path to the built probe executable
#   UNITY_PROJECT  Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS      artifact directory (default: <repo>/artifacts/toolchain)
#
# Optional first argument: positive | negative | both (default: both)
#
# Exit codes: 0 both requested modes matched the required result; nonzero on any mismatch.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/toolchain}"
PROBE_PLAYER="${PROBE_PLAYER:-${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64}"
MODE="${1:-both}"

if [[ "${MODE}" != "positive" && "${MODE}" != "negative" && "${MODE}" != "both" ]]; then
  echo "usage: $0 [positive|negative|both]" >&2
  exit 2
fi
if [[ ! -x "${PROBE_PLAYER}" ]]; then
  echo "probe player ${PROBE_PLAYER} is missing or not executable; run tools/unity/build_probe.sh first." >&2
  exit 2
fi

mkdir -p "${ARTIFACTS}"
failures=0

# -batchmode -nographics keep the player headless. -quit is deliberately NOT passed: the probe exits itself
# through Application.Quit with a code that encodes its result.
run_mode() {
  local mode="$1" result_file="$2" log_file="$3" expected_rc="$4" expected_result="$5"
  shift 5
  echo "-- running ${mode} probe"
  local rc=0
  "${PROBE_PLAYER}" \
    -batchmode \
    -nographics \
    -logFile "${log_file}" \
    "$@" \
    -probeResult "${result_file}" || rc=$?

  if [[ "${rc}" -ne "${expected_rc}" ]]; then
    echo "   FAIL ${mode}: exit code ${rc}, expected ${expected_rc}" >&2
    failures=$((failures + 1))
  fi
  if [[ ! -f "${result_file}" ]]; then
    echo "   FAIL ${mode}: no result written to ${result_file}" >&2
    failures=$((failures + 1))
    return
  fi
  if ! python3 -m json.tool "${result_file}" >/dev/null; then
    echo "   FAIL ${mode}: ${result_file} is not valid JSON" >&2
    failures=$((failures + 1))
    return
  fi
  if ! grep -q "\"result\": \"${expected_result}\"" "${result_file}"; then
    echo "   FAIL ${mode}: ${result_file} does not report result ${expected_result}" >&2
    failures=$((failures + 1))
    return
  fi
  if grep -q '"status": "Fail"' "${result_file}"; then
    echo "   FAIL ${mode}: at least one probe step reported status Fail" >&2
    failures=$((failures + 1))
    return
  fi
  if ! grep -q '"status": "Pass"' "${result_file}"; then
    echo "   FAIL ${mode}: no probe step reported status Pass" >&2
    failures=$((failures + 1))
    return
  fi
  echo "   ok ${mode}: exit ${rc}, result ${expected_result}"
}

if [[ "${MODE}" == "positive" || "${MODE}" == "both" ]]; then
  run_mode "positive" \
    "${ARTIFACTS}/probe-result.json" \
    "${ARTIFACTS}/player-positive.log" \
    0 "Pass"
fi

if [[ "${MODE}" == "negative" || "${MODE}" == "both" ]]; then
  run_mode "negative (missing registration)" \
    "${ARTIFACTS}/probe-negative.json" \
    "${ARTIFACTS}/player-negative.log" \
    3 "ExpectedNegative" "-probeMissingRegistration"
fi

if [[ "${failures}" -ne 0 ]]; then
  echo "== probe run FAILED (${failures} mismatch(es)) ==" >&2
  exit 1
fi

echo "== probe run PASSED =="
echo "positive result : ${ARTIFACTS}/probe-result.json"
echo "negative result : ${ARTIFACTS}/probe-negative.json"
echo "player logs     : ${ARTIFACTS}/player-positive.log, ${ARTIFACTS}/player-negative.log"
echo "note: 'Pass' here means the player process reported it; the result JSON is the evidence to archive."

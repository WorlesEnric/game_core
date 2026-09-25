#!/usr/bin/env bash
# GC-005 owned-world / guarded-dispatch player probe.
#
# Runs the already-built qualification player in its world-dispatch mode. The player itself is built by
# tools/unity/build_probe.sh (GC-001), which compiles the GC-005 assemblies into the same IL2CPP binary; this
# script only launches it and validates the structured result, exactly like tools/unity/run_probe.sh does for the
# GC-001 probes.
#
# Each probe is executed PROBE_RUNS times (default 5) through tools/unity/probe_runs.sh, and any run that crashes
# (exit >= 128, no result file, invalid JSON, wrong exit code or a Fail step) fails this script: a flaky crash
# during engine teardown can never hide behind a clean retry.
#
# Required environment:
#   python3 (strict JSON validation)
#
# Optional environment:
#   PROBE_PLAYER   path to the built probe executable
#                  (default: <repo>/unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64)
#   PROBE_RUNS     times the probe is executed (default 5)
#   UNITY_PROJECT  Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS      artifact directory (default: <repo>/artifacts/toolchain)
#
# Exit codes: 0 the world-dispatch probe reported Pass with exit code 0 on every run; nonzero on any mismatch.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
# shellcheck source=probe_runs.sh
source "${SCRIPT_DIR}/probe_runs.sh"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/toolchain}"
PROBE_PLAYER="${PROBE_PLAYER:-${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64}"

if [[ ! -x "${PROBE_PLAYER}" ]]; then
  echo "probe player ${PROBE_PLAYER} is missing or not executable; run tools/unity/build_probe.sh first." >&2
  exit 2
fi

mkdir -p "${ARTIFACTS}"
failures=0
result_file="${ARTIFACTS}/probe-world-dispatch.json"
log_file="${ARTIFACTS}/player-world-dispatch.log"

# -batchmode -nographics keep the player headless. -quit is deliberately NOT passed: the probe exits itself
# through Application.Quit with a code that encodes its result. probe_runs.sh owns the repetition and the verdict.
probe_run_n "world-dispatch" "${result_file}" "${log_file}" 0 "Pass" "-probeWorldDispatch" || failures=$((failures + 1))

if [[ ! -f "${result_file}" ]]; then
  echo "   FAIL world-dispatch: no result written to ${result_file}" >&2
  exit 1
fi

if ! python3 -m json.tool "${result_file}" >/dev/null; then
  echo "   FAIL world-dispatch: ${result_file} is not valid JSON" >&2
  exit 1
fi

if ! grep -q '"task": "GC-005"' "${result_file}"; then
  echo "   FAIL world-dispatch: the result is not labelled GC-005" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"mode": "WorldDispatch"' "${result_file}"; then
  echo "   FAIL world-dispatch: the result is not labelled WorldDispatch" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"result": "Pass"' "${result_file}"; then
  echo "   FAIL world-dispatch: the probe did not report Pass" >&2
  failures=$((failures + 1))
fi

if grep -q '"status": "Fail"' "${result_file}"; then
  echo "   FAIL world-dispatch: at least one probe step reported status Fail" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"status": "Pass"' "${result_file}"; then
  echo "   FAIL world-dispatch: no probe step reported status Pass" >&2
  failures=$((failures + 1))
fi

PROBE_LABEL="world-dispatch"
probe_require_steps "${result_file}" \
  '"name": "world-bootstrap-and-loop-route"' \
  '"name": "world-two-worlds-independent"' \
  '"name": "world-command-driven-idle-zero-steps"' \
  '"name": "world-no-system-double-update"' \
  '"name": "world-fixed-step-debt-and-clock"' \
  '"name": "world-guarded-fail-stop"' \
  '"name": "world-guarded-fail-stop-teardown"'

if [[ "${failures}" -ne 0 ]]; then
  echo "== world-dispatch probe run FAILED (${failures} mismatch(es)) ==" >&2
  exit 1
fi

echo "== world-dispatch probe run PASSED =="
echo "result : ${result_file}"
echo "player log : ${log_file}"
echo "note: 'Pass' here means the player process reported it; the result JSON is the evidence to archive."

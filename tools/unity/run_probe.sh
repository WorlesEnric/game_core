#!/usr/bin/env bash
# GC-001 toolchain qualification: run the built probe player headless in both modes and record the evidence.
#
# Each probe is executed PROBE_RUNS times (default 5) through tools/unity/probe_runs.sh, and any run that crashes
# (exit >= 128, no result file, invalid JSON, wrong exit code or a Fail step) fails this script: a flaky crash
# during engine teardown can never hide behind a clean retry. Run 1 keeps the canonical artifact names; later runs
# write <name>.run<N>, so the retained evidence is always run 1's.
#
# Required environment:
#   python3 (strict JSON validation); the player defaults to <repo>/unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64
#
# Optional environment:
#   PROBE_PLAYER   path to the built probe executable
#   PROBE_RUNS     times each probe is executed (default 5)
#   UNITY_PROJECT  Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS      artifact directory (default: <repo>/artifacts/toolchain)
#
# Optional first argument: positive | negative | both (default: both)
#
# Exit codes: 0 both requested modes matched the required result on every run; nonzero on any mismatch.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
# shellcheck source=probe_runs.sh
source "${SCRIPT_DIR}/probe_runs.sh"
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
# through Application.Quit with a code that encodes its result. probe_runs.sh owns the repetition and the verdict.
if [[ "${MODE}" == "positive" || "${MODE}" == "both" ]]; then
  probe_run_n "positive" \
    "${ARTIFACTS}/probe-result.json" \
    "${ARTIFACTS}/player-positive.log" \
    0 "Pass" || failures=$((failures + 1))
fi

if [[ "${MODE}" == "negative" || "${MODE}" == "both" ]]; then
  probe_run_n "negative (missing registration)" \
    "${ARTIFACTS}/probe-negative.json" \
    "${ARTIFACTS}/player-negative.log" \
    3 "ExpectedNegative" "-probeMissingRegistration" || failures=$((failures + 1))
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

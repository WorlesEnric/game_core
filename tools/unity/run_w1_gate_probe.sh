#!/usr/bin/env bash
# W1 integration gate player probe (W1-GATE).
#
# Runs the already-built qualification player in its W1-gate mode. The player itself is built by
# tools/unity/build_probe.sh (GC-001), which compiles the W1 packages into the same IL2CPP binary; this script
# only launches it and validates the structured result, exactly like tools/unity/run_probe.sh (GC-001) and
# tools/unity/run_world_probe.sh (GC-005) do for their modes.
#
# Each probe is executed PROBE_RUNS times (default 5) through tools/unity/probe_runs.sh, and any run that crashes
# (exit >= 128, no result file, invalid JSON, wrong exit code or a Fail step) fails this script: a flaky crash
# during engine teardown can never hide behind a clean retry.
#
# The mode runs the same scenario as the EditMode assembly `GameCore.W1Gate.Tests` twice: once over the committed
# generated catalog and once over the fixture's hand-written generated-style catalog. The second run's steps carry
# the "fixture:" name prefix.
#
# Required environment:
#   python3 (strict JSON validation)
#
# Optional environment:
#   PROBE_PLAYER   path to the built probe executable
#                  (default: <repo>/unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64)
#   PROBE_RUNS     times the probe is executed (default 5)
#   UNITY_PROJECT  Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS      artifact directory (default: <repo>/artifacts/w1-gate/toolchain)
#
# Exit codes: 0 the W1 gate probe reported Pass with exit code 0 on every run; nonzero on any mismatch.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
# shellcheck source=probe_runs.sh
source "${SCRIPT_DIR}/probe_runs.sh"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/w1-gate/toolchain}"
PROBE_PLAYER="${PROBE_PLAYER:-${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64}"

if [[ ! -x "${PROBE_PLAYER}" ]]; then
  echo "probe player ${PROBE_PLAYER} is missing or not executable; run tools/unity/build_probe.sh first." >&2
  exit 2
fi

mkdir -p "${ARTIFACTS}"
failures=0
result_file="${ARTIFACTS}/probe-w1-gate.json"
log_file="${ARTIFACTS}/player-w1-gate.log"

# -batchmode -nographics keep the player headless. -quit is deliberately NOT passed: the probe exits itself
# through Application.Quit with a code that encodes its result. probe_runs.sh owns the repetition and the verdict.
probe_run_n "w1-gate" "${result_file}" "${log_file}" 0 "Pass" "-probeW1Gate" || failures=$((failures + 1))

if [[ ! -f "${result_file}" ]]; then
  echo "   FAIL w1-gate: no result written to ${result_file}" >&2
  exit 1
fi

if ! python3 -m json.tool "${result_file}" >/dev/null; then
  echo "   FAIL w1-gate: ${result_file} is not valid JSON" >&2
  exit 1
fi

if ! grep -q '"task": "W1-GATE"' "${result_file}"; then
  echo "   FAIL w1-gate: the result is not labelled W1-GATE" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"mode": "W1Gate"' "${result_file}"; then
  echo "   FAIL w1-gate: the result is not labelled W1Gate" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"result": "Pass"' "${result_file}"; then
  echo "   FAIL w1-gate: the probe did not report Pass" >&2
  failures=$((failures + 1))
fi

if grep -q '"status": "Fail"' "${result_file}"; then
  echo "   FAIL w1-gate: at least one probe step reported status Fail" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"status": "Pass"' "${result_file}"; then
  echo "   FAIL w1-gate: no probe step reported status Pass" >&2
  failures=$((failures + 1))
fi

PROBE_LABEL="w1-gate"
# Every scenario observation must appear twice: once for the generated catalog and once for the fixture catalog.
required_steps=()
for base in \
  gate-catalog-and-manifest-source \
  gate-two-owned-worlds \
  gate-control-lanes-linked \
  gate-one-operation-admitted \
  gate-guarded-stage-commits \
  gate-second-world-stays-idle \
  gate-thrown-postwrite-exception-fails-stop \
  gate-operation-status-reports-fault-honestly \
  gate-faulted-world-refuses-admission \
  gate-teardown-settles-and-disposes; do
  required_steps+=("\"name\": \"${base}\"")
  required_steps+=("\"name\": \"fixture:${base}\"")
done
probe_require_steps "${result_file}" "${required_steps[@]}"

probe_require_steps "${result_file}" \
  '"name": "fixture:gate-key-derivation"' \
  '"name": "gate-generated-catalog-facts"' \
  '"name": "gate-fixture-catalog-facts"'

# The facts digest of the generated run must name the committed generated catalog's fingerprint.
generated_fingerprint="$(python3 - "${REPO_ROOT}" <<'PY'
import re, sys, pathlib
root = pathlib.Path(sys.argv[1])
source = (root / "unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs").read_text()
match = re.search(r'CatalogFingerprint = "([0-9a-f]{64})"', source)
print(match.group(1) if match else "")
PY
)"
if [[ -n "${generated_fingerprint}" ]]; then
  if ! grep -q "catalogFingerprint=${generated_fingerprint}" "${result_file}"; then
    echo "   FAIL w1-gate: the generated-catalog facts do not report the committed catalog fingerprint" >&2
    failures=$((failures + 1))
  fi
fi

if [[ "${failures}" -ne 0 ]]; then
  echo "== W1 gate probe run FAILED (${failures} mismatch(es)) ==" >&2
  exit 1
fi

echo "== W1 gate probe run PASSED =="
echo "result     : ${result_file}"
echo "player log : ${log_file}"
echo "note: 'Pass' here means the player process reported it; the result JSON is the evidence to archive."

#!/usr/bin/env bash
# GC-010 narrative vertical slice player probe (GC-010).
#
# Runs the already-built qualification player in its narrative mode. The player itself is built by
# tools/unity/build_probe.sh (GC-001), which compiles the narrative packages into the same IL2CPP binary; this
# script only launches it and validates the structured result, exactly like tools/unity/run_probe.sh (GC-001),
# tools/unity/run_world_probe.sh (GC-005), tools/unity/run_w1_gate_probe.sh (W1-GATE) and
# tools/unity/run_w2_gate_probe.sh (W2-GATE) do for their modes.
#
# The mode runs the same scenario as the EditMode assembly `GameCore.Narrative.Tests` twice: once over the committed
# generated catalog and once over the fixture's hand-written generated-style catalog. The second run's steps carry
# the "fixture:" name prefix.
#
# Every probe is executed PROBE_RUNS times (default 5) through tools/unity/probe_runs.sh, and the script fails if any
# single run crashes (SIGSEGV/SIGABRT while the engine tears down its PlayerLoop, worlds and native containers is a
# real defect, and a later clean run never repairs an earlier dirty one).
#
# Required environment:
#   python3 (strict JSON validation)
#
# Optional environment:
#   PROBE_PLAYER   path to the built probe executable
#                  (default: <repo>/unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64)
#   PROBE_RUNS     how many times each probe runs (default 5; must be a positive integer)
#   UNITY_PROJECT  Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS      artifact directory (default: <repo>/artifacts/gc-010/toolchain)
#
# Exit codes: 0 the narrative probe reported Pass with exit code 0 on every run; nonzero on any mismatch or crash.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
# shellcheck source=probe_runs.sh
source "${SCRIPT_DIR}/probe_runs.sh"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/gc-010/toolchain}"
PROBE_PLAYER="${PROBE_PLAYER:-${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64}"

if [[ ! -x "${PROBE_PLAYER}" ]]; then
  echo "probe player ${PROBE_PLAYER} is missing or not executable; run tools/unity/build_probe.sh first." >&2
  exit 2
fi

mkdir -p "${ARTIFACTS}"
failures=0
result_file="${ARTIFACTS}/probe-narrative.json"
log_file="${ARTIFACTS}/player-narrative.log"

# -batchmode -nographics keep the player headless. -quit is deliberately NOT passed: the probe exits itself
# through Application.Quit with a code that encodes its result.
echo "-- running GC-010 narrative probe (${PROBE_RUNS} run(s))"
probe_run_n "narrative" "${result_file}" "${log_file}" 0 "Pass" "-probeNarrative" || failures=$((failures + 1))

if [[ ! -f "${result_file}" ]]; then
  echo "   FAIL narrative: no result written to ${result_file}" >&2
  exit 1
fi

if ! python3 -m json.tool "${result_file}" >/dev/null; then
  echo "   FAIL narrative: ${result_file} is not valid JSON" >&2
  exit 1
fi

if ! grep -q '"task": "GC-010"' "${result_file}"; then
  echo "   FAIL narrative: the result is not labelled GC-010" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"mode": "Narrative"' "${result_file}"; then
  echo "   FAIL narrative: the result is not labelled Narrative" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"result": "Pass"' "${result_file}"; then
  echo "   FAIL narrative: the probe did not report Pass" >&2
  failures=$((failures + 1))
fi

# Every scenario observation must appear twice: once for the generated catalog and once for the fixture catalog.
PROBE_LABEL="narrative"
narrative_steps=()
for base in \
  narrative-catalog-and-declarations \
  narrative-world-and-live-targets \
  narrative-chapter-one-mounted-and-published \
  narrative-derived-layout-in-entities \
  narrative-chapter-two-mounted-and-published \
  narrative-one-choice-command-committed \
  narrative-duplicate-request-commits-nothing-new \
  narrative-forward-provider-and-spawned-target \
  narrative-idle-world-performs-zero-steps \
  narrative-genre-neutrality \
  narrative-teardown-settles-and-disposes; do
  narrative_steps+=("\"name\": \"${base}\"")
  narrative_steps+=("\"name\": \"fixture:${base}\"")
done
narrative_steps+=("\"name\": \"narrative-generated-catalog-facts\"")
narrative_steps+=("\"name\": \"narrative-fixture-catalog-facts\"")
probe_require_steps "${result_file}" "${narrative_steps[@]}"

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
    echo "   FAIL narrative: the generated-catalog facts do not report the committed catalog fingerprint" >&2
    failures=$((failures + 1))
  fi
fi

if [[ "${failures}" -ne 0 ]]; then
  echo "== GC-010 narrative probe run FAILED (${failures} mismatch(es)) ==" >&2
  exit 1
fi

echo "== GC-010 narrative probe run PASSED =="
echo "result     : ${result_file}"
echo "player log : ${log_file}"
echo "note: 'Pass' here means the player process reported it; the result JSON is the evidence to archive."

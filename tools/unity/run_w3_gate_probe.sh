#!/usr/bin/env bash
# W3 integration gate player probe (W3-GATE).
#
# Runs the already-built qualification player in its W3-gate mode. The player itself is built by
# tools/unity/build_probe.sh (GC-001), which compiles the Wave 3 packages into the same IL2CPP binary; this script
# only launches it and validates the structured result, exactly like tools/unity/run_probe.sh (GC-001),
# tools/unity/run_world_probe.sh (GC-005), tools/unity/run_w1_gate_probe.sh (W1-GATE),
# tools/unity/run_w2_gate_probe.sh (W2-GATE), tools/unity/run_narrative_probe.sh (GC-010) and
# tools/unity/run_cards_probe.sh (GC-011) do for their modes.
#
# The mode runs the Wave 3 exit-gate scenario inside ONE player process: the real narrative composition and the real
# card composition, each in its own world, then the kernel-separation audit over the loaded assembly graph. It is
# therefore the only probe that proves the two families run on one kernel image inside the shipped player; the
# per-slice probes prove each family's own observations (and the fixture catalog) on the same revision.
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
#   ARTIFACTS      artifact directory (default: <repo>/artifacts/w3-gate/toolchain)
#
# Exit codes: 0 the W3 gate probe reported Pass with exit code 0 on every run; nonzero on any mismatch or crash.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
# shellcheck source=probe_runs.sh
source "${SCRIPT_DIR}/probe_runs.sh"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/w3-gate/toolchain}"
PROBE_PLAYER="${PROBE_PLAYER:-${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64}"

if [[ ! -x "${PROBE_PLAYER}" ]]; then
  echo "probe player not found or not executable: ${PROBE_PLAYER}" >&2
  echo "build it first: UNITY=<editor> tools/unity/build_probe.sh" >&2
  exit 2
fi

mkdir -p "${ARTIFACTS}"
failures=0
result_file="${ARTIFACTS}/probe-w3-gate.json"
log_file="${ARTIFACTS}/player-w3-gate.log"

# -batchmode -nographics keep the player headless. -quit is deliberately NOT passed: the probe exits itself
# through Application.Quit with a code that encodes its result.
echo "-- running W3 integration gate probe (${PROBE_RUNS} run(s))"
probe_run_n "w3-gate" "${result_file}" "${log_file}" 0 "Pass" "-probeW3Gate" || failures=$((failures + 1))

if [[ ! -f "${result_file}" ]]; then
  echo "   FAIL w3-gate: no result written to ${result_file}" >&2
  exit 1
fi

if ! python3 -m json.tool "${result_file}" >/dev/null; then
  echo "   FAIL w3-gate: ${result_file} is not valid JSON" >&2
  exit 1
fi

if ! grep -q '"task": "W3-GATE"' "${result_file}"; then
  echo "   FAIL w3-gate: the result does not name task W3-GATE" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"mode": "W3Gate"' "${result_file}"; then
  echo "   FAIL w3-gate: the result does not name mode W3Gate" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"result": "Pass"' "${result_file}"; then
  echo "   FAIL w3-gate: the result does not report Pass" >&2
  failures=$((failures + 1))
fi

# The gate's own observations, plus the two slices' facts digests archived beside them.
PROBE_LABEL="w3-gate"
w3_gate_steps=()
w3_gate_steps+=("\"name\": \"w3-kernel-assemblies-reference-no-gameplay\"")
w3_gate_steps+=("\"name\": \"w3-narrative-composition-passes-on-this-kernel\"")
w3_gate_steps+=("\"name\": \"w3-card-composition-passes-on-this-kernel\"")
w3_gate_steps+=("\"name\": \"w3-two-worlds-on-one-kernel-image\"")
w3_gate_steps+=("\"name\": \"w3-zero-idle-command-steps-in-both\"")
w3_gate_steps+=("\"name\": \"w3-automatic-existing-and-future-targets-in-both\"")
w3_gate_steps+=("\"name\": \"w3-narrative-state-change-through-committed-snapshot\"")
w3_gate_steps+=("\"name\": \"w3-card-domain-transfer-committed-atomically\"")
w3_gate_steps+=("\"name\": \"w3-narrative-facts\"")
w3_gate_steps+=("\"name\": \"w3-card-facts\"")
w3_gate_steps+=("\"name\": \"w3-gate-facts\"")
probe_require_steps "${result_file}" "${w3_gate_steps[@]}"

# The gate's own facts digest must carry the decisive values, not only the verdicts: a clean kernel audit, both
# compositions' observations, and the two worlds' distinct sessions.
w3_gate_facts=()
w3_gate_facts+=("kernelForbiddenReferences=0")
w3_gate_facts+=("duplicateKernelAssemblies=0")
w3_gate_facts+=("kernelInspectionFailures=0")
w3_gate_facts+=("distinctSessions=True")
w3_gate_facts+=("narrativeSteps=11/0 failed")
w3_gate_facts+=("cardSteps=13/0 failed")
w3_gate_facts+=("narrativeIdle=600 frames/0 steps")
w3_gate_facts+=("narrativeExistingTargets=3")
w3_gate_facts+=("narrativeFutureRows=2")
w3_gate_facts+=("narrativeCommittedEvents=2")
w3_gate_facts+=("narrativeCommittedFactVersion=2")
w3_gate_facts+=("cardFutureBonus=2")
w3_gate_facts+=("cardTransferGiver=1")
w3_gate_facts+=("cardTransferReceiver=1")
probe_require_steps "${result_file}" "${w3_gate_facts[@]}"

# The two facts digests of the slices must name their own committed generated catalogs' fingerprints, which proves
# this process ran the same committed catalogs the per-slice gates did.
narrative_fingerprint="$(python3 - "${REPO_ROOT}" <<'PY'
import re, sys, pathlib
root = pathlib.Path(sys.argv[1])
source = (root / "unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs").read_text()
match = re.search(r'CatalogFingerprint = "([0-9a-f]{64})"', source)
print(match.group(1) if match else "")
PY
)"
cards_fingerprint="$(python3 - "${REPO_ROOT}" <<'PY'
import re, sys, pathlib
root = pathlib.Path(sys.argv[1])
source = (root / "unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCards/CardCatalog.g.cs").read_text()
match = re.search(r'CatalogFingerprint = "([0-9a-f]{64})"', source)
print(match.group(1) if match else "")
PY
)"
if [[ -z "${narrative_fingerprint}" || -z "${cards_fingerprint}" ]]; then
  echo "   FAIL w3-gate: could not read the committed catalogs' fingerprint literals" >&2
  failures=$((failures + 1))
fi

python3 - "${result_file}" "${narrative_fingerprint}" "${cards_fingerprint}" <<'PY' || failures=$((failures + 1))
import json, sys
result, narrative, cards = sys.argv[1], sys.argv[2], sys.argv[3]
with open(result, encoding="utf-8") as handle:
    document = json.load(handle)
digests = {probe["name"]: probe["detail"] for probe in document["probes"]}
missing = []
for name, expected in (("w3-narrative-facts", narrative), ("w3-card-facts", cards)):
    digest = digests.get(name, "")
    if ("catalogFingerprint=" + expected) not in digest:
        missing.append(name + " does not report catalogFingerprint=" + expected)
if missing:
    for problem in missing:
        print("   FAIL w3-gate: " + problem, file=sys.stderr)
    sys.exit(1)
PY

if [[ "${failures}" -ne 0 ]]; then
  echo "== W3 integration gate probe run FAILED (${failures} mismatch(es)) ==" >&2
  exit 1
fi

echo "== W3 integration gate probe run PASSED =="
echo "result     : ${result_file}"
echo "player log : ${log_file}"
echo "note: 'Pass' here means the player process reported it; the result JSON is the evidence to archive."

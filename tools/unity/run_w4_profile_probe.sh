#!/usr/bin/env bash
# GC-012 Wave 4 provisional generic-execution profile probe.
#
# Runs the already-built qualification player in its W4-profile mode. The player itself is built by
# tools/unity/build_probe.sh (GC-001), which compiles both families into the same IL2CPP binary with High managed
# stripping; this script only launches it and validates the structured result, exactly like
# tools/unity/run_probe.sh (GC-001), tools/unity/run_world_probe.sh (GC-005), tools/unity/run_w1_gate_probe.sh
# (W1-GATE), tools/unity/run_w2_gate_probe.sh (W2-GATE), tools/unity/run_narrative_probe.sh (GC-010),
# tools/unity/run_cards_probe.sh (GC-011) and tools/unity/run_w3_gate_probe.sh (W3-GATE) do for their modes.
#
# What this mode proves that the others do not:
#
#   * each family has its own generated inactive plugin entry (`FamilyEntryRegistrations` in both committed
#     catalogs), and nothing is mounted before the probe starts;
#   * both families run over their committed generated catalog AND their hand-written generated-style fixture
#     catalog, with the two runs compared canonically (the narrative run against the committed canonical trace's
#     own declaration via `TryCompare`, the card run by its facts digest);
#   * the P-017/P-019 multi-supporter `Additive` slot is observed in a live world: Seat A's single `cards.set-bonus`
#     row carries the composed 5 (festival +2 and nested festival +3) and two support rows, one per provider, and
#     retracting the nested provider leaves the surviving supporter and the recomposed value intact;
#   * no loaded kernel assembly references a gameplay/rules/validation/generated assembly, and no kernel assembly is
#     loaded twice. `Assets/link.xml` no longer preserves the narrative gameplay/rules assemblies, so this mode is
#     also the proof that generated roots, not linker preservation, keep them in the stripped player.
#
# Every probe is executed PROBE_RUNS times (default 5) through tools/unity/probe_runs.sh, and the script fails if any
# single run crashes (a SIGSEGV/SIGABRT while the engine tears down its PlayerLoop, worlds and native containers is a
# real defect — GC-012 records one such exit-139 segfault from the GC-007 build — and a later clean run never repairs
# an earlier dirty one).
#
# Required environment:
#   python3 (strict JSON validation)
#
# Optional environment:
#   PROBE_PLAYER   path to the built probe executable
#                  (default: <repo>/unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64)
#   PROBE_RUNS     how many times each probe runs (default 5; must be a positive integer)
#   UNITY_PROJECT  Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS      artifact directory (default: <repo>/artifacts/w4-generic-profile/toolchain)
#
# Exit codes: 0 the profile probe reported Pass with exit code 0 on every run; nonzero on any mismatch or crash.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
# shellcheck source=probe_runs.sh
source "${SCRIPT_DIR}/probe_runs.sh"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/w4-generic-profile/toolchain}"
PROBE_PLAYER="${PROBE_PLAYER:-${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64}"

if [[ ! -x "${PROBE_PLAYER}" ]]; then
  echo "no probe player at ${PROBE_PLAYER}; build it with tools/unity/build_probe.sh first." >&2
  exit 2
fi

mkdir -p "${ARTIFACTS}"
failures=0
result_file="${ARTIFACTS}/probe-w4-profile.json"
log_file="${ARTIFACTS}/player-w4-profile.log"

# -batchmode -nographics keep the player headless. -quit is deliberately NOT passed: the probe exits itself
# through Application.Quit with a code that encodes its result.
echo "-- running GC-012 W4 profile probe (${PROBE_RUNS} run(s))"
probe_run_n "w4-profile" "${result_file}" "${log_file}" 0 "Pass" "-probeW4Profile" || failures=$((failures + 1))

if [[ ! -f "${result_file}" ]]; then
  echo "   FAIL w4-profile: the player wrote no result file" >&2
  failures=$((failures + 1))
fi

if ! python3 -m json.tool "${result_file}" >/dev/null; then
  echo "   FAIL w4-profile: the result file is not valid JSON" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"task": "GC-012"' "${result_file}"; then
  echo "   FAIL w4-profile: the result does not carry task GC-012" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"mode": "W4Profile"' "${result_file}"; then
  echo "   FAIL w4-profile: the result does not carry mode W4Profile" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"result": "Pass"' "${result_file}"; then
  echo "   FAIL w4-profile: the result is not Pass" >&2
  failures=$((failures + 1))
fi

if grep -q '"status": "Fail"' "${result_file}"; then
  echo "   FAIL w4-profile: at least one probe step reported Fail" >&2
  failures=$((failures + 1))
fi

# Every gate clause must appear, plus the gate's own facts digest.
PROBE_LABEL="w4-profile"
w4_steps=()
w4_steps+=("\"name\": \"w4-generated-inactive-family-entries\"")
w4_steps+=("\"name\": \"w4-narrative-runs-agree-with-the-declared-canonical-trace\"")
w4_steps+=("\"name\": \"w4-card-runs-agree-canonically-generated-vs-fixture\"")
w4_steps+=("\"name\": \"w4-additive-multi-supporter-slot-in-a-live-world\"")
w4_steps+=("\"name\": \"w4-generic-profile-no-genre-type-in-the-kernel\"")
w4_steps+=("\"name\": \"w4-both-families-mount-late-and-leave-no-world\"")
w4_steps+=("\"name\": \"w4-profile-facts\"")
probe_require_steps "${result_file}" "${w4_steps[@]}"

# The gate's own facts digest must carry the decisive values, not only the verdicts.
w4_facts=()
w4_facts+=("bothEntriesResolved=True")
w4_facts+=("narrativeEntrySystems=6")
w4_facts+=("cardEntrySystems=4")
w4_facts+=("registryAtStart=0")
w4_facts+=("registryAfterAll=0")
w4_facts+=("narrativeGeneratedMatchesDeclaredTrace=True")
w4_facts+=("narrativeFixtureMatchesDeclaredTrace=True")
w4_facts+=("narrativeRunsAgree=True")
w4_facts+=("cardRunsAgree=True")
w4_facts+=("cardGeneratedFailures=0")
w4_facts+=("cardFixtureFailures=0")
w4_facts+=("additiveGeneratedPassed=True")
w4_facts+=("additiveFixturePassed=True")
w4_facts+=("additiveComposedValue=5")
w4_facts+=("additiveSupporterCount=2")
w4_facts+=("kernelForbiddenReferences=0")
w4_facts+=("duplicateKernelAssemblies=0")
w4_facts+=("kernelInspectionFailures=0")
probe_require_steps "${result_file}" "${w4_facts[@]}"

# The stripped player must have run exactly the committed catalogs: both fingerprints are read out of the generated
# sources and required in the gate's own facts digest, so a stale committed catalog cannot pass unnoticed.
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
  echo "   FAIL w4-profile: could not read the committed catalogs' fingerprint literals" >&2
  failures=$((failures + 1))
fi

python3 - "${result_file}" "${narrative_fingerprint}" "${cards_fingerprint}" <<'PY' || failures=$((failures + 1))
import json, sys
result, narrative, cards = sys.argv[1], sys.argv[2], sys.argv[3]
with open(result, encoding="utf-8") as handle:
    document = json.load(handle)
details = {probe["name"]: probe["detail"] for probe in document["probes"]}

problems = []
facts = details.get("w4-profile-facts", "")
for label, expected in (("narrative", narrative), ("cards", cards)):
    if expected not in facts:
        problems.append("the facts digest does not name the committed " + label + " fingerprint " + expected)

# The two families' digests must be distinct worlds' readings, so a family that silently reused the other's world
# would show up here rather than passing as "both ran".
additive = details.get("w4-additive-multi-supporter-slot-in-a-live-world", "")
for fragment in ("seatARows=1", "seatAComposedValue=5", "seatASupporters=2", "seatASupportRows=2",
                 "seatASupportValueSum=5", "festivalSupport=True", "nestedSupport=True",
                 "seatBValue=2", "seatCValue=-1",
                 "seatAValueAfterNestedUnmount=2", "seatASupportersAfterNestedUnmount=1",
                 "seatASupportRowsAfterNestedUnmount=1", "festivalSupportSurvives=True"):
    if fragment not in additive:
        problems.append("the additive observation does not report " + fragment)

if problems:
    for problem in problems:
        print("   FAIL w4-profile: " + problem, file=sys.stderr)
    sys.exit(1)
PY

if [[ "${failures}" -ne 0 ]]; then
  echo "== GC-012 W4 profile probe run FAILED (${failures} mismatch(es)) ==" >&2
  exit 1
fi

echo "== GC-012 W4 profile probe run PASSED =="
echo "result     : ${result_file}"
echo "player log : ${log_file}"
echo "note: 'Pass' here means the player process reported it; the result JSON is the evidence to archive."

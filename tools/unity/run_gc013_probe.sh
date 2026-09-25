#!/usr/bin/env bash
# GC-013 incremental invalidation, reparenting and live mode switching player probe (GC-013).
#
# Runs the already-built qualification player in its GC-013 mode. The player itself is built by
# tools/unity/build_probe.sh (GC-001), which compiles the narrative, card and derivation packages into the same IL2CPP
# binary; this script only launches it and validates the structured result, exactly like tools/unity/run_probe.sh
# (GC-001), tools/unity/run_world_probe.sh (GC-005), tools/unity/run_w1_gate_probe.sh (W1-GATE),
# tools/unity/run_w2_gate_probe.sh (W2-GATE), tools/unity/run_narrative_probe.sh (GC-010) and
# tools/unity/run_cards_probe.sh (GC-011) do for their modes.
#
# The mode runs both families' scenarios twice: once over the committed generated catalog and once over the family's
# hand-written generated-style catalog. The second run's steps carry the "fixture:" name prefix.
#
# Required environment:
#   python3 (strict JSON validation)
#
# Optional environment:
#   PROBE_PLAYER   path to the built probe executable
#                  (default: <repo>/unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64)
#   PROBE_RUNS     how many times each probe runs (default 5; must be a positive integer)
#   UNITY_PROJECT  Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS      artifact directory (default: <repo>/artifacts/gc-013/toolchain)
#
# Exit codes: 0 the GC-013 probe reported Pass with exit code 0 on every run; nonzero on any mismatch or crash.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
# shellcheck source=probe_runs.sh
source "${SCRIPT_DIR}/probe_runs.sh"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/gc-013/toolchain}"
PROBE_PLAYER="${PROBE_PLAYER:-${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64}"

if [[ ! -x "${PROBE_PLAYER}" ]]; then
  echo "run_gc013_probe.sh: probe player not found or not executable: ${PROBE_PLAYER}" >&2
  echo "  build it first with tools/unity/build_probe.sh (GC-001)." >&2
  exit 2
fi

mkdir -p "${ARTIFACTS}"
failures=0
result_file="${ARTIFACTS}/probe-gc013.json"
log_file="${ARTIFACTS}/player-gc013.log"

# -batchmode -nographics keep the player headless. -quit is deliberately NOT passed: the probe exits itself
# through Application.Quit with a code that encodes its result.
echo "-- running GC-013 transition probe (${PROBE_RUNS} run(s))"
probe_run_n "gc013" "${result_file}" "${log_file}" 0 "Pass" "-probeGc013" || failures=$((failures + 1))

if [[ ! -f "${result_file}" ]]; then
  echo "run_gc013_probe.sh: the probe wrote no result file: ${result_file}" >&2
  failures=$((failures + 1))
fi

if ! python3 -m json.tool "${result_file}" >/dev/null; then
  echo "run_gc013_probe.sh: the probe result is not valid JSON: ${result_file}" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"task": "GC-013"' "${result_file}"; then
  echo "run_gc013_probe.sh: the probe result does not declare task GC-013" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"mode": "Gc013"' "${result_file}"; then
  echo "run_gc013_probe.sh: the probe result does not declare mode Gc013" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"result": "Pass"' "${result_file}"; then
  echo "run_gc013_probe.sh: the probe did not report Pass" >&2
  failures=$((failures + 1))
fi

if grep -q '"status": "Fail"' "${result_file}"; then
  echo "run_gc013_probe.sh: the probe reported at least one failing step" >&2
  failures=$((failures + 1))
fi

# Every scenario observation must appear twice for each family: once for the generated catalog and once for the
# fixture catalog.
PROBE_LABEL="gc013"
gc013_steps=()
for family in narrative cards; do
  for base in \
    gc013-world-and-live-targets \
    gc013-mount-inherits-to-every-eligible-target \
    gc013-reparent-preserves-target-state-and-inheritance \
    gc013-isolated-branch-unchanged-across-the-reparent \
    gc013-opt-in-target-declared-and-derived \
    gc013-isolated-branch-unchanged-across-the-opt-in-target \
    gc013-mode-switch-automatic-to-conservative \
    gc013-isolated-branch-unchanged-across-the-conservative-switch \
    gc013-future-target-in-conservative-derives-nothing \
    gc013-isolated-branch-unchanged-across-the-future-spawn \
    gc013-mode-switch-conservative-to-automatic \
    gc013-isolated-branch-unchanged-across-the-automatic-switch \
    gc013-exclusive-conflict-preserves-mode-and-membership \
    gc013-isolated-branch-unchanged-across-the-conflict \
    gc013-teardown-settles-and-disposes; do
    gc013_steps+=("\"name\": \"${family}/${base}\"")
    gc013_steps+=("\"name\": \"fixture:${family}/${base}\"")
  done
done
gc013_steps+=("\"name\": \"gc013-narrative-digest\"")
gc013_steps+=("\"name\": \"gc013-cards-digest\"")
probe_require_steps "${result_file}" "${gc013_steps[@]}"

# Both digest literals must be exactly the expected ones: the digest is over the observation names and their pass
# flags, so this is the whole claim that both catalogs ran the named sequence and every step of it passed.
narrative_digest="8d0ca4d2e31cdf6e1ead4a57fa6427acb1dfb4d7c11ab9cab1590857ee81befd"
cards_digest="ac6dc0b11d32a60328cfcc2724ce23a36919afd88aa01e6afa0a135ac470c5c9"
if ! grep -q "generatedDigest=${narrative_digest}; fixtureDigest=${narrative_digest}" "${result_file}"; then
  echo "run_gc013_probe.sh: the narrative digest is not the expected value" >&2
  echo "  expected generatedDigest=${narrative_digest} and fixtureDigest=${narrative_digest}" >&2
  failures=$((failures + 1))
fi

if ! grep -q "generatedDigest=${cards_digest}; fixtureDigest=${cards_digest}" "${result_file}"; then
  echo "run_gc013_probe.sh: the card digest is not the expected value" >&2
  echo "  expected generatedDigest=${cards_digest} and fixtureDigest=${cards_digest}" >&2
  failures=$((failures + 1))
fi

if [[ "${failures}" -ne 0 ]]; then
  echo "run_gc013_probe.sh: ${failures} GC-013 probe check(s) failed" >&2
  exit 1
fi

echo "== GC-013 transition probe run PASSED =="
echo "result     : ${result_file}"
echo "player log : ${log_file}"
echo "note: 'Pass' here means the player process reported it; the result JSON is the evidence to archive."

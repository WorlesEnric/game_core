#!/usr/bin/env bash
# GC-019 adapter gate player probe (GC-019).
#
# Runs the already-built qualification player in its GC-019 mode. The player itself is built by
# tools/unity/build_probe.sh (GC-001), which compiles both gameplay families and the adapter package into the same
# IL2CPP binary; this script only launches it and validates the structured result, exactly like
# tools/unity/run_probe.sh (GC-001), tools/unity/run_world_probe.sh (GC-005), tools/unity/run_w1_gate_probe.sh
# (W1-GATE), tools/unity/run_w2_gate_probe.sh (W2-GATE), tools/unity/run_narrative_probe.sh (GC-010),
# tools/unity/run_cards_probe.sh (GC-011), tools/unity/run_w3_gate_probe.sh (W3-GATE), tools/unity/run_w4_profile_probe.sh
# (GC-012), tools/unity/run_gc013_probe.sh (GC-013) and tools/unity/run_w4_gate_probe.sh (W4-GATE) do for their modes.
#
# The mode runs both families' adapter sequences twice: once over the committed generated catalog and once over the
# family's hand-written generated-style catalog. The second run's steps carry the "fixture:" name prefix. It is the
# headless half of the GC-019 definition of done: the same runner the EditMode assembly `GameCore.Gc019.Tests` calls
# presents from committed snapshots through a recording binder, so it runs with no renderer, no camera and no
# presentation service installed - exactly the headless composition 04 s7 describes.
#
# Required environment:
#   python3 (strict JSON validation)
#
# Optional environment:
#   PROBE_PLAYER   path to the built probe executable
#                  (default: <repo>/unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64)
#   PROBE_RUNS     how many times each probe runs (default 5; must be a positive integer)
#   UNITY_PROJECT  Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS      artifact directory (default: <repo>/artifacts/gc-019/toolchain)
#
# Exit codes: 0 the GC-019 probe reported Pass with exit code 0 on every run; nonzero on any mismatch or crash.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
# shellcheck source=probe_runs.sh
source "${SCRIPT_DIR}/probe_runs.sh"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/gc-019/toolchain}"
PROBE_PLAYER="${PROBE_PLAYER:-${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64}"

if [[ ! -x "${PROBE_PLAYER}" ]]; then
  echo "run_gc019_probe.sh: probe player not found or not executable: ${PROBE_PLAYER}" >&2
  echo "  build it first with tools/unity/build_probe.sh (GC-001)." >&2
  exit 2
fi

mkdir -p "${ARTIFACTS}"
failures=0
result_file="${ARTIFACTS}/probe-gc019.json"
log_file="${ARTIFACTS}/player-gc019.log"

# -batchmode -nographics keep the player headless. -quit is deliberately NOT passed: the probe exits itself
# through Application.Quit with a code that encodes its result.
echo "-- running GC-019 adapter probe (${PROBE_RUNS} run(s))"
probe_run_n "gc019" "${result_file}" "${log_file}" 0 "Pass" "-probeGc019" || failures=$((failures + 1))

if [[ ! -f "${result_file}" ]]; then
  echo "run_gc019_probe.sh: the probe wrote no result file: ${result_file}" >&2
  failures=$((failures + 1))
fi

if ! python3 -m json.tool "${result_file}" >/dev/null; then
  echo "run_gc019_probe.sh: the probe result is not valid JSON: ${result_file}" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"task": "GC-019"' "${result_file}"; then
  echo "run_gc019_probe.sh: the probe result does not declare task GC-019" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"mode": "Gc019"' "${result_file}"; then
  echo "run_gc019_probe.sh: the probe result does not declare mode Gc019" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"result": "Pass"' "${result_file}"; then
  echo "run_gc019_probe.sh: the probe did not report Pass" >&2
  failures=$((failures + 1))
fi

if grep -q '"status": "Fail"' "${result_file}"; then
  echo "run_gc019_probe.sh: the probe reported at least one failing step" >&2
  failures=$((failures + 1))
fi

# Every adapter observation must appear twice for each family: once for the generated catalog and once for the
# fixture catalog. The names and their order are `Gc019Scenario.ObservationNames`.
gc019_steps=()
for family in narrative cards; do
  for base in \
    gc019-world-and-committed-targets \
    gc019-input-sample-becomes-a-committed-command \
    gc019-input-completion-from-a-retired-activation-is-discarded \
    gc019-asset-lease-completes-under-a-live-token \
    gc019-late-asset-completion-cannot-write-a-retired-world \
    gc019-presentation-reads-the-committed-snapshot \
    gc019-idle-world-presents-without-stepping \
    gc019-view-destruction-leaves-gameplay-intact \
    gc019-visual-reparent-does-not-move-composition \
    gc019-does-not-declare-external-authority \
    gc019-adapter-teardown-participates-in-lifecycle; do
    gc019_steps+=("\"name\": \"${family}/${base}\"")
    gc019_steps+=("\"name\": \"fixture:${family}/${base}\"")
  done
done
gc019_steps+=("\"name\": \"gc019-narrative-digest\"")
gc019_steps+=("\"name\": \"gc019-cards-digest\"")
probe_require_steps "${result_file}" "${gc019_steps[@]}"

# Both digest literals must be exactly the expected ones: the digest is over the observation names and their pass
# flags, so this is the whole claim that both catalogs ran the named sequence and every step of it passed.
narrative_digest="c812ccdce22cee6098d3f8dba5c23cfb744c7644aa83a99ae24bb790fbdde837"
cards_digest="deaff62c2a643221511524063dc0a23cb80b0fe071a3b00c8245fbd1fc6097ce"
if ! grep -q "generatedDigest=${narrative_digest}; fixtureDigest=${narrative_digest}" "${result_file}"; then
  echo "run_gc019_probe.sh: the narrative digest is not the expected value" >&2
  echo "  expected generatedDigest=${narrative_digest} and fixtureDigest=${narrative_digest}" >&2
  failures=$((failures + 1))
fi

if ! grep -q "generatedDigest=${cards_digest}; fixtureDigest=${cards_digest}" "${result_file}"; then
  echo "run_gc019_probe.sh: the card digest is not the expected value" >&2
  echo "  expected generatedDigest=${cards_digest} and fixtureDigest=${cards_digest}" >&2
  failures=$((failures + 1))
fi

# The task's own acceptance clauses, asserted as fragments of the step details rather than as bare pass flags: a run
# that recorded the right step names but did not really demonstrate the clause cannot satisfy these.
for clause in \
  "retransmission=Retransmission" \
  "retransmittedSteps=0" \
  "completion=Discarded" \
  "lateState=DiscardedStale" \
  "postRetire=DiscardedStale" \
  "rowsUnchanged=True" \
  "scopesUnchanged=True" \
  "nextCommand=Admitted" \
  "reparented=True" \
  "parentChanges=1" \
  "reparentsWithoutCompositionChange=1" \
  "externalDomains=0" \
  "physicsStages=; physicsSystems=" \
  "unregistered=True" \
  "idleSteps=0"; do
  if ! grep -q "${clause}" "${result_file}"; then
    echo "run_gc019_probe.sh: the acceptance clause fragment '${clause}' is absent from the result" >&2
    failures=$((failures + 1))
  fi
done

if [[ "${failures}" -ne 0 ]]; then
  echo "run_gc019_probe.sh: ${failures} GC-019 probe check(s) failed" >&2
  exit 1
fi

echo "== GC-019 adapter probe run PASSED =="
echo "result     : ${result_file}"
echo "player log : ${log_file}"
echo "note: 'Pass' here means the player process reported it; the result JSON is the evidence to archive."

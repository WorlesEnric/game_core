#!/usr/bin/env bash
# GC-017 fault-boundary player probe (GC-017).
#
# Runs the already-built qualification player in its fault-boundary mode. The player itself is built by
# tools/unity/build_probe.sh (GC-001), which compiles both gameplay families and every Wave 5 kernel package
# (including the GC-017 fault latches) into the same IL2CPP binary; this script only launches it and validates the
# structured result, exactly like tools/unity/run_probe.sh (GC-001), tools/unity/run_world_probe.sh (GC-005),
# tools/unity/run_w1_gate_probe.sh (W1-GATE), tools/unity/run_w2_gate_probe.sh (W2-GATE),
# tools/unity/run_narrative_probe.sh (GC-010), tools/unity/run_cards_probe.sh (GC-011), tools/unity/run_w3_gate_probe.sh
# (W3-GATE), tools/unity/run_w4_profile_probe.sh (GC-012), tools/unity/run_gc013_probe.sh (GC-013) and
# tools/unity/run_w4_gate_probe.sh (W4-GATE) do for their modes.
#
# The mode runs both families' fault scenarios twice: once over the committed generated catalog and once over the
# family's hand-written generated-style catalog. The second run's steps carry the "fixture:" name prefix. It is the
# IL2CPP half of GC-017: the same runner the EditMode assembly `GameCore.Faults.Tests` calls is executed here in a
# stripped player, so a fault boundary that only fires in the Editor cannot pass. The latches are compiled in by
# `GameCore.Unity.Runtime.asmdef`'s `GAMECORE_FAULT_INJECTION` version define, so a player that lost the symbol
# would report the same named steps as unpassed rather than silently skipping them.
#
# Required environment:
#   python3 (strict JSON validation)
#
# Optional environment:
#   PROBE_PLAYER   path to the built probe executable
#                  (default: <repo>/unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64)
#   PROBE_RUNS     how many times each probe runs (default 5; must be a positive integer)
#   UNITY_PROJECT  Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS      artifact directory (default: <repo>/artifacts/faults/toolchain)
#
# Exit codes: 0 the fault probe reported Pass with exit code 0 on every run; nonzero on any mismatch or crash.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
# shellcheck source=probe_runs.sh
source "${SCRIPT_DIR}/probe_runs.sh"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/faults/toolchain}"
PROBE_PLAYER="${PROBE_PLAYER:-${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64}"

if [[ ! -x "${PROBE_PLAYER}" ]]; then
  echo "run_gc017_faults_probe.sh: probe player not found or not executable: ${PROBE_PLAYER}" >&2
  echo "  build it first with tools/unity/build_probe.sh (GC-001)." >&2
  exit 2
fi

mkdir -p "${ARTIFACTS}"
failures=0
result_file="${ARTIFACTS}/probe-gc017-faults.json"
log_file="${ARTIFACTS}/player-gc017-faults.log"

# -batchmode -nographics keep the player headless. -quit is deliberately NOT passed: the probe exits itself
# through Application.Quit with a code that encodes its result.
echo "-- running GC-017 fault-boundary probe (${PROBE_RUNS} run(s))"
probe_run_n "gc017-faults" "${result_file}" "${log_file}" 0 "Pass" "-probeFaults" || failures=$((failures + 1))

if [[ ! -f "${result_file}" ]]; then
  echo "run_gc017_faults_probe.sh: the probe wrote no result file: ${result_file}" >&2
  failures=$((failures + 1))
fi

if ! python3 -m json.tool "${result_file}" >/dev/null; then
  echo "run_gc017_faults_probe.sh: the probe result is not valid JSON: ${result_file}" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"task": "GC-017"' "${result_file}"; then
  echo "run_gc017_faults_probe.sh: the probe result does not declare task GC-017" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"mode": "Faults"' "${result_file}"; then
  echo "run_gc017_faults_probe.sh: the probe result does not declare mode Faults" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"result": "Pass"' "${result_file}"; then
  echo "run_gc017_faults_probe.sh: the probe did not report Pass" >&2
  failures=$((failures + 1))
fi

if grep -q '"status": "Fail"' "${result_file}"; then
  echo "run_gc017_faults_probe.sh: the probe reported at least one failing step" >&2
  failures=$((failures + 1))
fi

# Every observation of TEST-016's matrix must appear twice for each family: once for the generated catalog and once
# for the fixture catalog. The names and their order are `FaultScenario.ObservationNames` (frozen), and the clauses
# are the matrix's own required observations: a rejection keeps the old assembly, staged leases are reclaimed,
# cancellation before the cutoff differs from cancellation after it, prewrite migration preserves live state, a
# postwrite fault faults the world and keeps the last image, structural playback stops the step commit, gate
# installation and the fence settle where they say, cleanup retains what a refusal staged, recovery produces a new
# world that rejects the old callback, and teardown settles.
PROBE_LABEL="gc017-faults"
gc017_steps=()
for family in narrative cards; do
  for base in \
    gc017-initial-world-lane-and-observer \
    gc017-validation-fault-rejects-and-keeps-the-old-assembly \
    gc017-acquisition-fault-releases-staged-leases \
    gc017-cancellation-before-the-cutoff-releases-staged-work \
    gc017-cancellation-after-the-cutoff-is-too-late-and-keeps-the-publication \
    gc017-prewrite-migration-fault-preserves-live-state \
    gc017-postwrite-fault-faults-the-world-and-keeps-the-last-image \
    gc017-structural-playback-fault-stops-the-step-commit \
    gc017-gate-installation-fault-stops-after-live-writes \
    gc017-fence-fault-settles-handles-and-keeps-the-old-assembly \
    gc017-cleanup-fault-retains-staged-ownership \
    gc017-cleanup-boundary-releases-what-a-refusal-staged \
    gc017-recovery-from-initial-definitions-into-a-new-world \
    gc017-old-callback-after-recovery-is-rejected \
    gc017-teardown-settles-and-disposes; do
    gc017_steps+=("\"name\": \"${family}/${base}\"")
    gc017_steps+=("\"name\": \"fixture:${family}/${base}\"")
  done
done
gc017_steps+=("\"name\": \"gc017-narrative-digest\"")
gc017_steps+=("\"name\": \"gc017-cards-digest\"")
probe_require_steps "${result_file}" "${gc017_steps[@]}"

# Both digest literals must be exactly the expected ones: the digest is over the observation names and their pass
# flags, so this is the whole claim that both catalogs ran the named sequence and every step of it passed.
narrative_digest="701a3c286098501456390975bbdc7e4bdb7218d3094f23e39e61b3744fa52b61"
cards_digest="5cd97d38a1023fe0c8b5239d611061e5696454be9ec7cc68d440506810201732"
if ! grep -q "generatedDigest=${narrative_digest}; fixtureDigest=${narrative_digest}" "${result_file}"; then
  echo "run_gc017_faults_probe.sh: the narrative digest is not the expected value" >&2
  echo "  expected generatedDigest=${narrative_digest} and fixtureDigest=${narrative_digest}" >&2
  failures=$((failures + 1))
fi

if ! grep -q "generatedDigest=${cards_digest}; fixtureDigest=${cards_digest}" "${result_file}"; then
  echo "run_gc017_faults_probe.sh: the card digest is not the expected value" >&2
  echo "  expected generatedDigest=${cards_digest} and fixtureDigest=${cards_digest}" >&2
  failures=$((failures + 1))
fi

# The matrix's own clauses, asserted as fragments of the step details rather than as bare pass flags: a run that
# recorded the right step names but did not really reach the boundary cannot satisfy these. `traceRecord=` carries
# one verbatim `FaultRecord.ToLine()` of the injected record (TEST-016 row 2's "failure includes operation ID and
# provenance"), and `boundaryReaches=`/`injected=` carry the latch's own counts.
for clause in \
  "structuralWrites=0" \
  "crossedLiveWriteBoundary=False" \
  "crossedLiveWriteBoundary=True" \
  "worldState=Faulted" \
  "stagedLeasesReleased=true" \
  "cancelOutcome=Cancelled" \
  "cancelOutcome=TooLate" \
  "armed=True" \
  "boundaryReaches=" \
  "injected=" \
  "traceRecord=boundary=validation" \
  "fired=1" \
  "retainedStaged=1" \
  "recovered=True" \
  "disposition=DiscardForeignWorld"; do
  if ! grep -q "${clause}" "${result_file}"; then
    echo "run_gc017_faults_probe.sh: the fault clause fragment '${clause}' is absent from the result" >&2
    failures=$((failures + 1))
  fi
done

if [[ "${failures}" -ne 0 ]]; then
  echo "run_gc017_faults_probe.sh: ${failures} GC-017 fault probe check(s) failed" >&2
  exit 1
fi

echo "== GC-017 fault-boundary probe run PASSED =="
echo "result     : ${result_file}"
echo "player log : ${log_file}"
echo "note: 'Pass' here means the player process reported it; the result JSON is the evidence to archive."

#!/usr/bin/env bash
# Wave 5 integration-gate player probe (W5-GATE).
#
# Runs the already-built qualification player in its Wave 5 gate mode. The player itself is built by
# tools/unity/build_probe.sh, which compiles both gameplay families and every Wave 5 kernel package (including the
# GC-017 fault latches) into the same IL2CPP binary; this script only launches it and validates the structured
# result, exactly like tools/unity/run_gc018_probe.sh and tools/unity/run_gc019_probe.sh do for their modes.
#
# The mode runs both families' gate scenarios twice: once over the committed generated catalog and once over the
# family's hand-written generated-style catalog. The second run's steps carry the "fixture:" name prefix. It is the
# IL2CPP half of the Wave 5 gate: the same runner the EditMode assembly `GameCore.W5Gate.Tests` calls is executed
# here in a stripped player, so a join that only works in the Editor cannot pass. The latches are compiled in by
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
#   ARTIFACTS      artifact directory (default: <repo>/artifacts/w5-gate/toolchain)
#
# Exit codes: 0 the gate probe reported Pass with exit code 0 on every run; nonzero on any mismatch or crash.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
# shellcheck source=probe_runs.sh
source "${SCRIPT_DIR}/probe_runs.sh"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/w5-gate/toolchain}"
PROBE_PLAYER="${PROBE_PLAYER:-${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64}"

if [[ ! -x "${PROBE_PLAYER}" ]]; then
  echo "run_w5_gate_probe.sh: probe player not found or not executable: ${PROBE_PLAYER}" >&2
  echo "  build it first with tools/unity/build_probe.sh." >&2
  exit 2
fi

mkdir -p "${ARTIFACTS}"
failures=0
result_file="${ARTIFACTS}/probe-w5-gate.json"
log_file="${ARTIFACTS}/player-w5-gate.log"

# -batchmode -nographics keep the player headless. -quit is deliberately NOT passed: the probe exits itself through
# Application.Quit with a code that encodes its result.
echo "-- running Wave 5 gate probe (${PROBE_RUNS} run(s))"
probe_run_n "w5-gate" "${result_file}" "${log_file}" 0 "Pass" "-probeW5Gate" || failures=$((failures + 1))

if [[ ! -f "${result_file}" ]]; then
  echo "run_w5_gate_probe.sh: the probe wrote no result file: ${result_file}" >&2
  failures=$((failures + 1))
fi

if ! python3 -m json.tool "${result_file}" >/dev/null; then
  echo "run_w5_gate_probe.sh: the probe result is not valid JSON: ${result_file}" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"task": "W5-GATE"' "${result_file}"; then
  echo "run_w5_gate_probe.sh: the probe result does not declare task W5-GATE" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"mode": "W5Gate"' "${result_file}"; then
  echo "run_w5_gate_probe.sh: the probe result does not declare mode W5Gate" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"result": "Pass"' "${result_file}"; then
  echo "run_w5_gate_probe.sh: the probe did not report Pass" >&2
  failures=$((failures + 1))
fi

if grep -q '"status": "Fail"' "${result_file}"; then
  echo "run_w5_gate_probe.sh: the probe reported at least one failing step" >&2
  failures=$((failures + 1))
fi

# Every observation of the gate sentence must appear twice for each family: once for the generated catalog and once
# for the fixture catalog. The names are `W5GateScenario.ObservationNames` (frozen), in the gate sentence's own
# order: one world carrying retained observation, read-only pinned snapshots, a checkpoint taken at the committed
# boundary, a prewrite rejection that keeps the old assembly, a postwrite fail-stop, a restore into a new session,
# the adapters bound to that new session, and the retired world's callbacks rejected.
PROBE_LABEL="w5-gate"
w5_steps=()
for family in narrative cards; do
  for base in \
    w5gate-one-world-with-retained-observation \
    w5gate-pinned-snapshots-are-read-only \
    w5gate-checkpoint-from-the-committed-boundary \
    w5gate-prewrite-fault-keeps-the-old-assembly \
    w5gate-postwrite-fault-fail-stops-the-world \
    w5gate-restore-into-a-new-session \
    w5gate-adapters-bind-to-the-restored-world \
    w5gate-retired-world-callbacks-are-rejected; do
    w5_steps+=("\"name\": \"${family}/${base}\"")
    w5_steps+=("\"name\": \"fixture:${family}/${base}\"")
  done
done
w5_steps+=("\"name\": \"w5gate-narrative-digest\"")
w5_steps+=("\"name\": \"w5gate-cards-digest\"")
probe_require_steps "${result_file}" "${w5_steps[@]}"

# Both digest literals must be exactly the expected ones: the digest is over the observation names and their pass
# flags, so this is the whole claim that both catalogs ran the named sequence and every step of it passed.
narrative_digest="768dc415bd79e76adacb2472f7af5383bff25b4da44635a94eb3fa64f4dc250c"
cards_digest="5740b580c5b807796af5456396195baf761947ff6fc819e173074aeeeef297ff"
if ! grep -q "generatedDigest=${narrative_digest}; fixtureDigest=${narrative_digest}" "${result_file}"; then
  echo "run_w5_gate_probe.sh: the narrative digest is not the expected value" >&2
  echo "  expected generatedDigest=${narrative_digest} and fixtureDigest=${narrative_digest}" >&2
  failures=$((failures + 1))
fi

if ! grep -q "generatedDigest=${cards_digest}; fixtureDigest=${cards_digest}" "${result_file}"; then
  echo "run_w5_gate_probe.sh: the card digest is not the expected value" >&2
  echo "  expected generatedDigest=${cards_digest} and fixtureDigest=${cards_digest}" >&2
  failures=$((failures + 1))
fi

# The gate sentence's own clauses, asserted as fragments of the step details rather than as bare pass flags: a run
# that recorded the right step names but did not really reach the boundary cannot satisfy these. `traceRecord=` is a
# verbatim `FaultRecord.ToLine()` of an injected record, `lateCompletion=` names what the retired table did with the
# old world's callback, and `oldHandlesRefused=`/`staleCallbackRejected=` are the cross-session refusals.
for clause in \
  "writableReferenceEscapes=0" \
  "pinnedImageSurvived=True" \
  "readThroughTheLease=True" \
  "declaredQueued=1" \
  "structuralWrites=0" \
  "crossedLiveWriteBoundary=False" \
  "worldState=Running" \
  "traceRecord=boundary=validation" \
  "structuralWrites=" \
  "crossedLiveWriteBoundary=True" \
  "worldState=Faulted" \
  "pumpedAfterFault=False" \
  "admittedAfterFault=False" \
  "lastGoodImageHeld=True" \
  "oldHandlesRefused=True" \
  "stateMatches=True" \
  "foreignTokenRefused=True" \
  "foreignInputRefused=True" \
  "foreignImageRefused=True" \
  "gameplayIntact=True" \
  "lateCompletion=" \
  "tableRetired=True" \
  "fired=1"; do
  if ! grep -q "${clause}" "${result_file}"; then
    echo "run_w5_gate_probe.sh: the gate clause fragment '${clause}' is absent from the result" >&2
    failures=$((failures + 1))
  fi
done

if [[ "${failures}" -ne 0 ]]; then
  echo "run_w5_gate_probe.sh: ${failures} Wave 5 gate probe check(s) failed" >&2
  exit 1
fi

echo "== Wave 5 gate probe run PASSED =="
echo "result     : ${result_file}"
echo "player log : ${log_file}"
echo "note: 'Pass' here means the player process reported it; the result JSON is the evidence to archive."

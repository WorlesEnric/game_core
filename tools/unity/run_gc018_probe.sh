#!/usr/bin/env bash
# GC-018 checkpoint capture/restore round-trip player probe (GC-018).
#
# Runs the already-built qualification player in its GC-018 mode. The player itself is built by
# tools/unity/build_probe.sh (GC-001), which compiles both gameplay families, the kernel packages and the generated
# checkpoint catalog into the same IL2CPP binary; this script only launches it and validates the structured result,
# exactly like tools/unity/run_probe.sh (GC-001), tools/unity/run_world_probe.sh (GC-005),
# tools/unity/run_w1_gate_probe.sh (W1-GATE), tools/unity/run_w2_gate_probe.sh (W2-GATE),
# tools/unity/run_narrative_probe.sh (GC-010), tools/unity/run_cards_probe.sh (GC-011),
# tools/unity/run_w3_gate_probe.sh (W3-GATE), tools/unity/run_w4_profile_probe.sh (GC-012),
# tools/unity/run_gc013_probe.sh (GC-013) and tools/unity/run_w4_gate_probe.sh (W4-GATE) do for their modes.
#
# The mode runs both families' scenarios twice: once over the committed generated catalog and once over the family's
# hand-written generated-style catalog. The second run's steps carry the "fixture:" name prefix. It is the IL2CPP
# half of the GC-018 qualification: the same runner the EditMode assembly `GameCore.Gc018.Tests` calls is executed
# here in a stripped player, so a capture/restore path that only works in the Editor cannot pass.
#
# Required environment:
#   python3 (strict JSON validation)
#
# Optional environment:
#   PROBE_PLAYER   path to the built probe executable
#                  (default: <repo>/unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64)
#   PROBE_RUNS     how many times each probe runs (default 5; must be a positive integer)
#   UNITY_PROJECT  Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS      artifact directory (default: <repo>/artifacts/gc-018/toolchain)
#
# Exit codes: 0 the GC-018 probe reported Pass with exit code 0 on every run; nonzero on any mismatch or crash.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
# shellcheck source=probe_runs.sh
source "${SCRIPT_DIR}/probe_runs.sh"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/gc-018/toolchain}"
PROBE_PLAYER="${PROBE_PLAYER:-${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64}"

if [[ ! -x "${PROBE_PLAYER}" ]]; then
  echo "run_gc018_probe.sh: probe player not found or not executable: ${PROBE_PLAYER}" >&2
  echo "  build it first with tools/unity/build_probe.sh (GC-001), or set PROBE_PLAYER." >&2
  exit 2
fi

mkdir -p "${ARTIFACTS}"
failures=0
result_file="${ARTIFACTS}/probe-gc018.json"
log_file="${ARTIFACTS}/player-gc018.log"

# -batchmode -nographics keep the player headless. -quit is deliberately NOT passed: the probe exits itself
# through Application.Quit with a code that encodes its result.
echo "-- running GC-018 checkpoint round-trip probe (${PROBE_RUNS} run(s))"
probe_run_n "gc018" "${result_file}" "${log_file}" 0 "Pass" "-probeGc018" || failures=$((failures + 1))

if [[ ! -f "${result_file}" ]]; then
  echo "run_gc018_probe.sh: no result file at ${result_file}" >&2
  exit 1
fi

if ! python3 -m json.tool "${result_file}" >/dev/null; then
  echo "run_gc018_probe.sh: ${result_file} is not valid JSON" >&2
  exit 1
fi

if ! grep -q '"task": "GC-018"' "${result_file}"; then
  echo "run_gc018_probe.sh: the result does not carry task GC-018" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"mode": "Gc018"' "${result_file}"; then
  echo "run_gc018_probe.sh: the result does not carry mode Gc018" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"result": "Pass"' "${result_file}"; then
  echo "run_gc018_probe.sh: the result is not Pass" >&2
  failures=$((failures + 1))
fi

if grep -q '"status": "Fail"' "${result_file}"; then
  echo "run_gc018_probe.sh: the result contains a failing step" >&2
  failures=$((failures + 1))
fi

# Every scenario observation must appear twice for each family: once for the generated catalog and once for the
# fixture catalog. The names and their order are `Gc018Scenario.ObservationNames`, and the clauses are the task's
# own: a committed-boundary capture, an explicit queued-command disposition, the five refusal classes, a restore
# into a fresh unexposed world at different native indices with active and dormant state, mode, boundaries, clocks,
# RNG and cursors preserved, old handles refused, the restored world routable, and a full teardown.
PROBE_LABEL="gc018"
gc018_steps=()
for family in narrative cards; do
  for base in \
    gc018-world-lane-and-live-state \
    gc018-committed-boundary-capture \
    gc018-queued-commands-are-dispositioned-not-omitted \
    gc018-capture-refuses-outside-a-boundary \
    gc018-corrupt-and-truncated-documents-reject \
    gc018-unknown-required-schema-rejects-restore \
    gc018-ambiguous-migration-rejects-restore \
    gc018-corrupt-reference-rejects-restore \
    gc018-restore-happens-into-a-new-unexposed-world \
    gc018-restore-recreates-state-at-different-native-indices \
    gc018-restore-preserves-dormant-slots \
    gc018-restore-preserves-mode-imports-and-exclusions \
    gc018-restore-continues-clocks-rng-and-cursors \
    gc018-old-callbacks-cannot-target-the-new-session \
    gc018-restored-world-publishes-and-advances \
    gc018-teardown-disposes-both-worlds; do
    gc018_steps+=("\"name\": \"${family}/${base}\"")
    gc018_steps+=("\"name\": \"fixture:${family}/${base}\"")
  done
done
gc018_steps+=("\"name\": \"gc018-narrative-digest\"")
gc018_steps+=("\"name\": \"gc018-cards-digest\"")
probe_require_steps "${result_file}" "${gc018_steps[@]}"

# Both digest literals must be exactly the expected ones: the digest is over the observation names and their pass
# flags, so this is the whole claim that both catalogs ran the named sequence and every step of it passed.
narrative_digest="f881469b2a2ba43e4c1bf7972913dd2c93f945a623fef770e00319409ece7ac0"
cards_digest="fe1aaae38982be120fe3c668742990504f984b54b053de3b2d7886ff899b5256"
if ! grep -q "generatedDigest=${narrative_digest}; fixtureDigest=${narrative_digest}" "${result_file}"; then
  echo "run_gc018_probe.sh: the narrative digest is not the expected value" >&2
  echo "  expected generatedDigest=${narrative_digest} and fixtureDigest=${narrative_digest}" >&2
  failures=$((failures + 1))
fi

if ! grep -q "generatedDigest=${cards_digest}; fixtureDigest=${cards_digest}" "${result_file}"; then
  echo "run_gc018_probe.sh: the card digest is not the expected value" >&2
  echo "  expected generatedDigest=${cards_digest} and fixtureDigest=${cards_digest}" >&2
  failures=$((failures + 1))
fi

# The task's own clauses, asserted as fragments of the step details rather than as bare pass flags: a run that
# recorded the right step names but did not really demonstrate the clause cannot satisfy these. Every fragment below
# is produced only by a passing run's detail string.
for clause in \
  "; dormantSlot=" \
  "active=False" \
  "atCommittedBoundary=False" \
  "; readRefusal=NotAtBoundary/" \
  "; captureRefusal=" \
  "includeQueued(" \
  "rejectQueued(" \
  "registryAfterStaging=" \
  "identitySetEqual=True" \
  "indexBlocksDisjoint=True" \
  "liveStorage=True" \
  "treeEqual=True" \
  "rowsEqual=True" \
  "positionsEqual=True" \
  "stampsRestamped=True" \
  "oldHandleRefused=True" \
  "resubmitAdmitted=False" \
  "branch=admitted-step" \
  "stepsCommitted=" \
  "restoredSession=" \
  "sourceSession="; do
  if ! grep -q "${clause}" "${result_file}"; then
    echo "run_gc018_probe.sh: the checkpoint clause fragment '${clause}' is absent from the result" >&2
    failures=$((failures + 1))
  fi
done

if [[ "${failures}" -ne 0 ]]; then
  echo "run_gc018_probe.sh: ${failures} GC-018 probe check(s) failed" >&2
  exit 1
fi

echo "== GC-018 checkpoint round-trip probe run PASSED =="
echo "result     : ${result_file}"
echo "player log : ${log_file}"
echo "note: 'Pass' here means the player process reported it; the result JSON is the evidence to archive."

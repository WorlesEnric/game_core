#!/usr/bin/env bash
# Wave 4 integration gate player probe (W4-GATE).
#
# Runs the already-built qualification player in its W4-gate mode. The player itself is built by
# tools/unity/build_probe.sh (GC-001), which compiles both gameplay families and every Wave 4 kernel package into the
# same IL2CPP binary; this script only launches it and validates the structured result, exactly like
# tools/unity/run_probe.sh (GC-001), tools/unity/run_world_probe.sh (GC-005), tools/unity/run_w1_gate_probe.sh
# (W1-GATE), tools/unity/run_w2_gate_probe.sh (W2-GATE), tools/unity/run_narrative_probe.sh (GC-010),
# tools/unity/run_cards_probe.sh (GC-011), tools/unity/run_w3_gate_probe.sh (W3-GATE), tools/unity/run_w4_profile_probe.sh
# (GC-012) and tools/unity/run_gc013_probe.sh (GC-013) do for their modes.
#
# The mode runs both families' scenarios twice: once over the committed generated catalog and once over the family's
# hand-written generated-style catalog. The second run's steps carry the "fixture:" name prefix. It is the IL2CPP half
# of the Wave 4 exit gate: the same runner the EditMode assembly `GameCore.W4Gate.Tests` calls is executed here in a
# stripped player, so a mode/step that only works in the Editor cannot pass the gate.
#
# Required environment:
#   python3 (strict JSON validation)
#
# Optional environment:
#   PROBE_PLAYER   path to the built probe executable
#                  (default: <repo>/unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64)
#   PROBE_RUNS     how many times each probe runs (default 5; must be a positive integer)
#   UNITY_PROJECT  Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS      artifact directory (default: <repo>/artifacts/w4-gate/toolchain)
#
# Exit codes: 0 the W4-gate probe reported Pass with exit code 0 on every run; nonzero on any mismatch or crash.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
# shellcheck source=probe_runs.sh
source "${SCRIPT_DIR}/probe_runs.sh"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/w4-gate/toolchain}"
PROBE_PLAYER="${PROBE_PLAYER:-${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64}"

if [[ ! -x "${PROBE_PLAYER}" ]]; then
  echo "run_w4_gate_probe.sh: probe player not found or not executable: ${PROBE_PLAYER}" >&2
  echo "  build it first with tools/unity/build_probe.sh (GC-001)." >&2
  exit 2
fi

mkdir -p "${ARTIFACTS}"
failures=0
result_file="${ARTIFACTS}/probe-w4-gate.json"
log_file="${ARTIFACTS}/player-w4-gate.log"

# -batchmode -nographics keep the player headless. -quit is deliberately NOT passed: the probe exits itself
# through Application.Quit with a code that encodes its result.
echo "-- running Wave 4 integration gate probe (${PROBE_RUNS} run(s))"
probe_run_n "w4-gate" "${result_file}" "${log_file}" 0 "Pass" "-probeW4Gate" || failures=$((failures + 1))

if [[ ! -f "${result_file}" ]]; then
  echo "run_w4_gate_probe.sh: the probe wrote no result file: ${result_file}" >&2
  failures=$((failures + 1))
fi

if ! python3 -m json.tool "${result_file}" >/dev/null; then
  echo "run_w4_gate_probe.sh: the probe result is not valid JSON: ${result_file}" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"task": "W4-GATE"' "${result_file}"; then
  echo "run_w4_gate_probe.sh: the probe result does not declare task W4-GATE" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"mode": "W4Gate"' "${result_file}"; then
  echo "run_w4_gate_probe.sh: the probe result does not declare mode W4Gate" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"result": "Pass"' "${result_file}"; then
  echo "run_w4_gate_probe.sh: the probe did not report Pass" >&2
  failures=$((failures + 1))
fi

if grep -q '"status": "Fail"' "${result_file}"; then
  echo "run_w4_gate_probe.sh: the probe reported at least one failing step" >&2
  failures=$((failures + 1))
fi

# Every scenario observation must appear twice for each family: once for the generated catalog and once for the
# fixture catalog. The names and their order are `W4GateScenario.ObservationNames`, and the gate's clauses are the
# wave's own exit sentence: both mode directions, a subtree move preserving state, suspend/resume, provider
# loss/unload, the four slot policies plus the manifest-supported reset, the one publication series and teardown.
PROBE_LABEL="w4-gate"
w4_steps=()
for family in narrative cards; do
  for base in \
    w4-world-lane-and-extra-manifests \
    w4-extra-providers-mount-onto-the-same-revision \
    w4-automatic-inheritance-and-the-future-target \
    w4-subtree-move-preserves-state-and-switches-binding \
    w4-mode-automatic-to-conservative-retracts-existing-and-future \
    w4-mode-conservative-to-automatic-restores-existing-and-future \
    w4-suspend-retracts-and-resume-restores \
    w4-required-provider-loss-makes-consumers-wait \
    w4-required-provider-return-resumes-consumers \
    w4-unload-disposes-in-reverse-acquisition-order \
    w4-slot-preserve-keeps-the-non-default-value \
    w4-slot-preserve-dormant-retains-without-an-active-writer \
    w4-slot-remove-derived-drops-the-row \
    w4-slot-transfer-to-moves-the-value-to-the-named-owner \
    w4-slot-reset-uses-the-manifest-permission \
    w4-lane-epoch-equals-world-epoch-throughout \
    w4-teardown-settles-and-disposes; do
    w4_steps+=("\"name\": \"${family}/${base}\"")
    w4_steps+=("\"name\": \"fixture:${family}/${base}\"")
  done
done
w4_steps+=("\"name\": \"w4gate-narrative-digest\"")
w4_steps+=("\"name\": \"w4gate-cards-digest\"")
probe_require_steps "${result_file}" "${w4_steps[@]}"

# Both digest literals must be exactly the expected ones: the digest is over the observation names and their pass
# flags, so this is the whole claim that both catalogs ran the named sequence and every step of it passed.
narrative_digest="d73e1a15e3d5f997b47087d02ea73ed809b73692b35900c3f2feeeff65cebaab"
cards_digest="4a1bdb460ab366c5a0ffed77f09aaca881b4fdfea142195290ee5ad73283b018"
if ! grep -q "generatedDigest=${narrative_digest}; fixtureDigest=${narrative_digest}" "${result_file}"; then
  echo "run_w4_gate_probe.sh: the narrative digest is not the expected value" >&2
  echo "  expected generatedDigest=${narrative_digest} and fixtureDigest=${narrative_digest}" >&2
  failures=$((failures + 1))
fi

if ! grep -q "generatedDigest=${cards_digest}; fixtureDigest=${cards_digest}" "${result_file}"; then
  echo "run_w4_gate_probe.sh: the card digest is not the expected value" >&2
  echo "  expected generatedDigest=${cards_digest} and fixtureDigest=${cards_digest}" >&2
  failures=$((failures + 1))
fi

# The gate's own clauses, asserted as fragments of the step details rather than as bare pass flags: a run that
# recorded the right step names but did not really demonstrate the clause cannot satisfy these.
for clause in \
  "mode=Automatic->Conservative" \
  "mode=Conservative->Automatic" \
  "mismatches=0" \
  "reverseOrder=True" \
  "lateCompletion=discarded" \
  "dormant=True" \
  "namedExactly=True" \
  "shapeHeld=True" \
  "supportProviderIsSecond=True" \
  "policyHostScoped=True"; do
  if ! grep -q "${clause}" "${result_file}"; then
    echo "run_w4_gate_probe.sh: the gate clause fragment '${clause}' is absent from the result" >&2
    failures=$((failures + 1))
  fi
done

if [[ "${failures}" -ne 0 ]]; then
  echo "run_w4_gate_probe.sh: ${failures} W4-gate probe check(s) failed" >&2
  exit 1
fi

echo "== Wave 4 integration gate probe run PASSED =="
echo "result     : ${result_file}"
echo "player log : ${log_file}"
echo "note: 'Pass' here means the player process reported it; the result JSON is the evidence to archive."

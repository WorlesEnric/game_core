#!/usr/bin/env bash
# GC-021 durable-outbox and destination-idempotency player probe (GC-021).
#
# Runs the already-built qualification player in its GC-021 mode. The player itself is built by
# tools/unity/build_probe.sh (GC-001), which compiles both gameplay families, the reward integration package, the
# kernel packages and the generated checkpoint catalog into the same IL2CPP binary; this script only launches it and
# validates the structured result, exactly like tools/unity/run_gc018_probe.sh (GC-018),
# tools/unity/run_gc019_probe.sh (GC-019) and tools/unity/run_w5_gate_probe.sh (W5-GATE) do for their modes.
#
# The mode runs both families' scenarios twice: once over the committed generated catalog and once over the family's
# hand-written generated-style catalog. The second run's steps carry the "fixture:" name prefix. It is the IL2CPP
# half of the GC-021 qualification: the same runner the EditMode assembly `GameCore.Gc021.Tests` calls is executed
# here in a stripped player, so a durable-delivery path that only works in the Editor cannot pass.
#
# THE TWO DIGEST LITERALS ARE NOT PINNED HERE ON PURPOSE. `ProbeGc021`'s two digest constants are written as
# "PENDING" by the author of this task, because the digest is computed over the observation names and their pass
# flags and cannot be known before the sequence first runs. The build host fills them in after the first passing run
# (the same instruction `ProbeW5Gate`'s handoff carried), and this script then asserts the pinned values. Until that
# happens this script asserts the two weaker, still-falsifiable facts it can: the digest step exists, and the
# generated digest equals the fixture digest (i.e. the committed catalog and the hand-written catalog produced the
# identical named sequence). Pinning a literal invented without running the code would be a fabricated result.
#
# Required environment:
#   python3 (strict JSON validation)
#
# Optional environment:
#   PROBE_PLAYER       path to the built probe executable
#                      (default: <repo>/unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64)
#   PROBE_RUNS         how many times each probe runs (default 5; must be a positive integer)
#   UNITY_PROJECT      Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS          artifact directory (default: <repo>/artifacts/gc-021/toolchain)
#   GC021_DIGEST_NARRATIVE / GC021_DIGEST_CARDS
#                      the two pinned digest literals; when set (non-empty) they are asserted exactly instead of the
#                      weaker equality check above. The build host sets them after the first passing run.
#
# Exit codes: 0 the GC-021 probe reported Pass with exit code 0 on every run; nonzero on any mismatch or crash.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
# shellcheck source=probe_runs.sh
source "${SCRIPT_DIR}/probe_runs.sh"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/gc-021/toolchain}"
PROBE_PLAYER="${PROBE_PLAYER:-${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64}"
GC021_DIGEST_NARRATIVE="${GC021_DIGEST_NARRATIVE:-}"
GC021_DIGEST_CARDS="${GC021_DIGEST_CARDS:-}"

if [[ ! -x "${PROBE_PLAYER}" ]]; then
  echo "run_gc021_probe.sh: probe player not found or not executable: ${PROBE_PLAYER}" >&2
  echo "  build it first with tools/unity/build_probe.sh (GC-001), or set PROBE_PLAYER." >&2
  exit 2
fi

mkdir -p "${ARTIFACTS}"
failures=0
result_file="${ARTIFACTS}/probe-gc021.json"
log_file="${ARTIFACTS}/player-gc021.log"

# -batchmode -nographics keep the player headless. -quit is deliberately NOT passed: the probe exits itself
# through Application.Quit with a code that encodes its result. Audio stays disabled in this player (crash-139).
echo "-- running GC-021 durable-delivery probe (${PROBE_RUNS} run(s))"
probe_run_n "gc021" "${result_file}" "${log_file}" 0 "Pass" "-probeGc021" || failures=$((failures + 1))

if [[ ! -f "${result_file}" ]]; then
  echo "run_gc021_probe.sh: no result file at ${result_file}" >&2
  exit 1
fi

if ! python3 -m json.tool "${result_file}" >/dev/null; then
  echo "run_gc021_probe.sh: ${result_file} is not valid JSON" >&2
  exit 1
fi

if ! grep -q '"task": "GC-021"' "${result_file}"; then
  echo "run_gc021_probe.sh: the result does not carry task GC-021" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"mode": "Gc021"' "${result_file}"; then
  echo "run_gc021_probe.sh: the result does not carry mode Gc021" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"result": "Pass"' "${result_file}"; then
  echo "run_gc021_probe.sh: the result is not Pass" >&2
  failures=$((failures + 1))
fi

if grep -q '"status": "Fail"' "${result_file}"; then
  echo "run_gc021_probe.sh: the result contains a failing step" >&2
  failures=$((failures + 1))
fi

# Every scenario observation must appear twice for each family: once for the generated catalog and once for the
# fixture catalog. The names are `Gc021Scenario.ObservationNames`; the clauses are the task's own.
PROBE_LABEL="gc021"
gc021_steps=()
for family in narrative cards; do
  for base in \
    gc021-key-is-derived-from-committed-data \
    gc021-commit-persists-before-apply \
    gc021-crash-after-delivery-loses-the-acknowledgement \
    gc021-redelivery-applies-the-mutation-once \
    gc021-capacity-exhaustion-is-never-a-silent-drop \
    gc021-volatile-delivery-is-distinguishable-from-durable \
    gc021-obligation-survives-the-source-world-unload \
    gc021-checkpoint-carries-the-outbox-and-the-cursor \
    gc021-no-universal-effect-api \
    gc021-narrative-choice-is-observed \
    gc021-reward-content-covers-the-observed-node \
    gc021-reward-obligation-is-durable-and-idempotent; do
    gc021_steps+=("\"name\": \"${family}/${base}\"")
    gc021_steps+=("\"name\": \"fixture:${family}/${base}\"")
  done
done
gc021_steps+=("\"name\": \"gc021-narrative-digest\"")
gc021_steps+=("\"name\": \"gc021-cards-digest\"")
probe_require_steps "${result_file}" "${gc021_steps[@]}"

# The two digest steps must agree between the committed catalog and the hand-written one: the digest is over the
# observation names and their pass flags, so equal digests are the claim that both catalogs ran the same named
# sequence to the same outcome. When the build host has pinned the literals, assert them exactly.
if ! grep -q 'gc021-narrative-digest' "${result_file}"; then
  echo "run_gc021_probe.sh: the narrative digest step is absent" >&2
  failures=$((failures + 1))
fi

if ! grep -q 'gc021-cards-digest' "${result_file}"; then
  echo "run_gc021_probe.sh: the card digest step is absent" >&2
  failures=$((failures + 1))
fi

if ! python3 - "${result_file}" "${GC021_DIGEST_NARRATIVE}" "${GC021_DIGEST_CARDS}" <<'PY'
import json, sys
path, pinned_narrative, pinned_cards = sys.argv[1], sys.argv[2], sys.argv[3]
with open(path, "r", encoding="utf-8") as handle:
    report = json.load(handle)
steps = {step.get("name"): step for step in report.get("probes", [])}
problems = []
for label, pinned in (("narrative", pinned_narrative), ("cards", pinned_cards)):
    step = steps.get("gc021-%s-digest" % label)
    if step is None:
        problems.append("the %s digest step is absent" % label)
        continue
    detail = step.get("detail", "")
    generated = None
    fixture = None
    expected = None
    for part in detail.split(";"):
        part = part.strip()
        if part.startswith("generatedDigest="):
            generated = part.split("=", 1)[1]
        elif part.startswith("fixtureDigest="):
            fixture = part.split("=", 1)[1]
        elif part.startswith("expectedDigest="):
            expected = part.split("=", 1)[1]
    if generated is None or fixture is None:
        problems.append("the %s digest detail does not carry both digests: %r" % (label, detail))
        continue
    if generated != fixture:
        problems.append("the %s digest differs between the two catalogs: %s vs %s" % (label, generated, fixture))
    if pinned:
        if expected != pinned:
            problems.append(
                "the %s digest literal is %s and GC021_DIGEST_%s pins %s"
                % (label, expected, label.upper(), pinned))
        if generated != pinned:
            problems.append(
                "the %s run produced %s and GC021_DIGEST_%s pins %s"
                % (label, generated, label.upper(), pinned))
if problems:
    for problem in problems:
        print("run_gc021_probe.sh: " + problem, file=sys.stderr)
    sys.exit(1)
sys.exit(0)
PY
then
  failures=$((failures + 1))
fi

# The task's own clauses, asserted as fragments of the passing steps' details rather than as bare pass flags: a run
# that recorded the right step names but did not really demonstrate the clause cannot satisfy these.
for clause in \
  "idempotencyKeyIsDistinct=True" \
  "appendedBeforeApply=True" \
  "journalFrameCount=1" \
  "crashPoint=AfterDelivery" \
  "destinationMutations=1" \
  "redeliveredMutations=1" \
  "recoveredOpen=1" \
  "atCapacity=True" \
  "capacityCode=BudgetExceeded" \
  "volatileRefused=True" \
  "durableAccepted=True" \
  "rowsReinstated=" \
  "sessionsDiffer=True" \
  "headerOutboxCount=" \
  "reverseMembers=0" \
  "recognisedChoices=" \
  "oneRewardPerEvent=True" \
  "mutationCount=1"; do
  if ! grep -q "${clause}" "${result_file}"; then
    echo "run_gc021_probe.sh: the delivery clause fragment '${clause}' is absent from the result" >&2
    failures=$((failures + 1))
  fi
done

if [[ "${failures}" -ne 0 ]]; then
  echo "run_gc021_probe.sh: ${failures} GC-021 probe check(s) failed" >&2
  exit 1
fi

echo "== GC-021 durable-delivery probe run PASSED =="
echo "result     : ${result_file}"
echo "player log : ${log_file}"
echo "note: 'Pass' here means the player process reported it; the result JSON is the evidence to archive."

#!/usr/bin/env bash
# GC-027 failure-atomic checkpoint restore and durable-recovery player probe (GC-027).
#
# Runs the already-built qualification player in its GC-027 -probeRecovery mode. The player itself is built by
# tools/unity/build_probe.sh (GC-001), which compiles both gameplay families, the reward integration package, the
# kernel packages and the generated checkpoint catalog into the same IL2CPP binary; this script only launches it and
# validates the structured result, exactly like tools/unity/run_gc018_probe.sh (GC-018) and
# tools/unity/run_gc021_probe.sh (GC-021) do for their modes.
#
# The mode runs both families' scenarios once each: the narrative family's observations carry the "narrative/" name
# prefix and the card family's carry "cards/". It is the IL2CPP half of the GC-027 qualification: the same runner the
# GC-027 EditMode suite calls is executed here in a stripped player, so a restore or recovery path that only works in
# the Editor cannot pass.
#
# THE TWO DIGEST LITERALS ARE PINNED HERE. Each family's digest is SHA-256 over its 17 qualified observation names
# with "=pass" appended, LF separated with no trailing newline (NarrativeDigest.OfLines), so a renamed, dropped,
# reordered or failing observation cannot produce the pinned value. Both were recomputed independently from the
# frozen name table:
#   narrative 2644b55aee8bedbbae60e04627e4f6b16d114ac4f418bed4a1e5960e2bdf80f9
#   cards     3c5923b2559b869767c49906e181c4e5efd0f351816926c4095e0aa36ff9074c
#
# Required environment:
#   python3 (strict JSON validation)
#
# Optional environment:
#   PROBE_PLAYER       path to the built probe executable
#                      (default: <repo>/unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64)
#   PROBE_RUNS         how many times each probe runs (default 5; must be a positive integer)
#   UNITY_PROJECT      Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS          artifact directory (default: <repo>/artifacts/gc-027/toolchain)
#
# Exit codes: 0 the GC-027 probe reported Pass with exit code 0 on every run; nonzero on any mismatch or crash.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
# shellcheck source=probe_runs.sh
source "${SCRIPT_DIR}/probe_runs.sh"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/gc-027/toolchain}"
PROBE_PLAYER="${PROBE_PLAYER:-${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64}"

if [[ ! -x "${PROBE_PLAYER}" ]]; then
  echo "run_recovery_probe.sh: probe player not found or not executable: ${PROBE_PLAYER}" >&2
  echo "  build it first with tools/unity/build_probe.sh (GC-001), or set PROBE_PLAYER." >&2
  exit 2
fi

mkdir -p "${ARTIFACTS}"
failures=0
result_file="${ARTIFACTS}/probe-gc027.json"
log_file="${ARTIFACTS}/player-gc027.log"

# -batchmode -nographics keep the player headless. -quit is deliberately NOT passed: the probe exits itself
# through Application.Quit with a code that encodes its result. Audio stays disabled in this player (crash-139).
echo "-- running GC-027 recovery probe (${PROBE_RUNS} run(s))"
probe_run_n "gc027" "${result_file}" "${log_file}" 0 "Pass" "-probeRecovery" || failures=$((failures + 1))

if [[ ! -f "${result_file}" ]]; then
  echo "run_recovery_probe.sh: no result file at ${result_file}" >&2
  exit 1
fi

# Every scenario observation must appear once for each family. The names and their order are
# `Gc027Scenario.ObservationNames`, and the clauses are the task's own: a captured checkpoint, the five fault
# refusals, a recovery into a new session at different native indices with active and dormant state, the outbox and
# its delivery cursor carried across, the acknowledgement fault, restart from the store alone, the bounded retry and
# a full teardown.
PROBE_LABEL="gc027"
gc027_steps=()
for family in narrative cards; do
  for base in \
    gc027-source-world-captures-and-publishes-a-verified-checkpoint \
    gc027-capture-copy-fault-produces-no-checkpoint \
    gc027-publication-fault-keeps-the-previous-document \
    gc027-reference-repair-fault-never-builds-a-destination \
    gc027-postwrite-apply-fault-never-exposes-a-destination \
    gc027-recovery-publication-fault-keeps-the-registry-unchanged \
    gc027-recovery-publishes-a-new-session-with-the-captured-state \
    gc027-restored-world-uses-different-native-handles \
    gc027-active-and-dormant-state-survive-the-recovery \
    gc027-outbox-rows-and-delivery-cursor-survive-the-recovery \
    gc027-outbox-append-fault-refuses-before-delivery \
    gc027-outbox-delivery-fault-redelivers-with-one-destination-effect \
    gc027-outbox-acknowledgement-fault-records-or-redelivers-once \
    gc027-restart-from-the-store-recovers-without-in-process-state \
    gc027-restart-without-a-document-or-incompatible-content-exposes-nothing \
    gc027-transient-failure-is-retried-under-the-host-bound \
    gc027-teardown-disposes-every-world; do
    gc027_steps+=("\"name\": \"${family}/${base}\"")
  done
done
gc027_steps+=("\"name\": \"gc027-narrative-digest\"")
gc027_steps+=("\"name\": \"gc027-cards-digest\"")

# The two pinned digest literals: SHA-256 over the 17 qualified names with "=pass" appended, LF separated without a
# trailing newline. A run that recorded a different set of observations, or a failing one, cannot report them.
narrative_digest="2644b55aee8bedbbae60e04627e4f6b16d114ac4f418bed4a1e5960e2bdf80f9"
cards_digest="3c5923b2559b869767c49906e181c4e5efd0f351816926c4095e0aa36ff9074c"

# A run is only evidence when its own JSON carries the whole claim, so every run's result file is asserted and not
# just run 1's: the task, the mode, the verdict, the absence of a failing step, all 34 named observations, both
# pinned digests and the clauses.
gc027_assert_result() {
  local run_result="$1" run_label="$2"

  if ! python3 -m json.tool "${run_result}" >/dev/null; then
    echo "run_recovery_probe.sh: ${run_label}: ${run_result} is not valid JSON" >&2
    failures=$((failures + 1))
    return
  fi

  if ! grep -q '"task": "GC-027"' "${run_result}"; then
    echo "run_recovery_probe.sh: ${run_label}: the result does not carry task GC-027" >&2
    failures=$((failures + 1))
  fi

  if ! grep -q '"mode": "Recovery"' "${run_result}"; then
    echo "run_recovery_probe.sh: ${run_label}: the result does not carry mode Recovery" >&2
    failures=$((failures + 1))
  fi

  if ! grep -q '"result": "Pass"' "${run_result}"; then
    echo "run_recovery_probe.sh: ${run_label}: the result is not Pass" >&2
    failures=$((failures + 1))
  fi

  if grep -q '"status": "Fail"' "${run_result}"; then
    echo "run_recovery_probe.sh: ${run_label}: the result contains a failing step" >&2
    failures=$((failures + 1))
  fi

  probe_require_steps "${run_result}" "${gc027_steps[@]}"

  if ! grep -q 'gc027-narrative-digest' "${run_result}"; then
    echo "run_recovery_probe.sh: ${run_label}: the narrative digest step is absent" >&2
    failures=$((failures + 1))
  fi

  if ! grep -q 'gc027-cards-digest' "${run_result}"; then
    echo "run_recovery_probe.sh: ${run_label}: the card digest step is absent" >&2
    failures=$((failures + 1))
  fi

  # Each family's digest step detail must carry its pinned literal: the digest is over the qualified observation
  # names and their pass flags, so the literal is the whole claim that the family ran the named sequence to a pass.
  if ! python3 - "${run_result}" "${narrative_digest}" "${cards_digest}" <<'PY'
import json, sys
path, narrative_literal, cards_literal = sys.argv[1], sys.argv[2], sys.argv[3]
with open(path, "r", encoding="utf-8") as handle:
    report = json.load(handle)
steps = {step.get("name"): step for step in report.get("probes", [])}
problems = []
for label, literal in (("narrative", narrative_literal), ("cards", cards_literal)):
    step = steps.get("gc027-%s-digest" % label)
    if step is None:
        problems.append("the %s digest step is absent" % label)
        continue
    detail = step.get("detail", "")
    if literal not in detail:
        problems.append("the %s digest detail does not carry %s: %r" % (label, literal, detail))
if problems:
    for problem in problems:
        print("run_recovery_probe.sh: " + problem, file=sys.stderr)
    sys.exit(1)
sys.exit(0)
PY
  then
    failures=$((failures + 1))
  fi

  # The task's own clauses, asserted as fragments of the passing steps' details rather than as bare pass flags: a run
  # that recorded the right step names but did not really demonstrate the clause cannot satisfy these.
  local clause
  for clause in \
    "no checkpoint" \
    "previous document" \
    "never built" \
    "never exposed" \
    "registry unchanged" \
    "different native" \
    "dormant" \
    "delivery cursor" \
    "idempotency key" \
    "retried" \
    "not contacted" \
    "disposed"; do
    if ! grep -q "${clause}" "${run_result}"; then
      echo "run_recovery_probe.sh: ${run_label}: the recovery clause fragment '${clause}' is absent from the result" >&2
      failures=$((failures + 1))
    fi
  done
  gc027_assert_clause_details "${run_result}"
}

gc027_assert_clause_details() {
  local run_result="$1"

  # The three task clauses a step name alone does not carry, read from the passing steps' own details: the failed
  # old world never resumes (its recorded lifecycle ends Disposed and never Running or Paused), the destination
  # effect is applied exactly once (a redelivery is recognised as already applied and adds no second effect, and a
  # restart delivers none), and no world is left published (the final registry count is zero).
  if ! python3 - "${run_result}" <<'PY'
import json, sys

path = sys.argv[1]
with open(path, "r", encoding="utf-8") as handle:
    report = json.load(handle)
steps = {step.get("name"): step for step in report.get("probes", [])}
problems = []


def detail_of(name):
    step = steps.get(name)
    return None if step is None else step.get("detail", "")


def source_lifecycle_after(text):
    # The clean recovery records its source as "<session>-><before>/<after>", so only the segment after the
    # transition arrow and the last "/" says whether the old world was left running.
    transition = text
    for field in text.split(";"):
        field = field.strip()
        if field.startswith("source="):
            transition = field[len("source="):]
            break
    tail = transition.rsplit("->", 1)[-1] if "->" in transition else transition
    return tail.split("/")[-1]


for family in ("narrative", "cards"):
    # (1) The failed old world never resumes: the recorded lifecycle transition must end Disposed -- the detail's
    # flat "->Disposed" form, or the after-lifecycle of its "<before>/<after>" form -- and must not end Running or
    # Paused.
    name = family + "/gc027-recovery-publishes-a-new-session-with-the-captured-state"
    text = detail_of(name)
    if text is None:
        problems.append("the %s recovery observation '%s' is absent" % (family, name))
    else:
        after = source_lifecycle_after(text)
        if after != "Disposed":
            problems.append("the %s recovery detail does not end the source lifecycle in Disposed (ends %r): %r"
                            % (family, after, text))
        if "->Running" in text or "->Paused" in text:
            problems.append("the %s recovery detail reports a resumed source: %r" % (family, text))

    # (2) The destination effect is not duplicated: a redelivery reuses the obligation's idempotency key, so it is
    # recognised as already applied and applies no second effect; a restart reinstates obligations and delivers
    # none.
    name = family + "/gc027-outbox-delivery-fault-redelivers-with-one-destination-effect"
    text = detail_of(name)
    if text is None:
        problems.append("the %s redelivery observation '%s' is absent" % (family, name))
    else:
        for fragment in ("effectsAfterRedelivery=1", "alreadyApplied=1"):
            if fragment not in text:
                problems.append("the %s redelivery detail does not carry %s: %r" % (family, fragment, text))

    name = family + "/gc027-restart-from-the-store-recovers-without-in-process-state"
    text = detail_of(name)
    if text is None:
        problems.append("the %s restart observation '%s' is absent" % (family, name))
    else:
        if "destinationAttempts=0" not in text:
            problems.append("the %s restart detail does not carry destinationAttempts=0: %r" % (family, text))
        if "->Running" in text or "->Paused" in text:
            problems.append("the %s restart detail reports a resumed source: %r" % (family, text))

    # (3) No world is left published: the teardown's registry field must end at zero.
    name = family + "/gc027-teardown-disposes-every-world"
    text = detail_of(name)
    if text is None:
        problems.append("the %s teardown observation '%s' is absent" % (family, name))
    elif "registry=" not in text:
        problems.append("the %s teardown detail carries no registry field: %r" % (family, text))
    else:
        value = text.split("registry=", 1)[1].split(";", 1)[0]
        if "->" not in value or value.rsplit("->", 1)[1] != "0":
            problems.append("the %s teardown detail leaves the registry at %r rather than 0: %r"
                            % (family, value, text))

if problems:
    for problem in problems:
        print("run_recovery_probe.sh: " + problem, file=sys.stderr)
    sys.exit(1)
sys.exit(0)
PY
  then
    failures=$((failures + 1))
  fi
}

# probe_run_n already fails a run that crashed, wrote no JSON, exited non-zero or reported a failing step; these
# assertions add the named observations, the pinned digests and the clauses for that same set of runs. run 1 writes
# the canonical file; runs 2..N write <file>.run<N>, exactly as probe_runs.sh does.
for (( run = 1; run <= PROBE_RUNS; run++ )); do
  run_result="${result_file}"
  if (( run > 1 )); then
    run_result="${result_file}.run${run}"
  fi
  gc027_assert_result "${run_result}" "run ${run}/${PROBE_RUNS}"
done

if [[ "${failures}" -ne 0 ]]; then
  echo "run_recovery_probe.sh: ${failures} GC-027 probe check(s) failed" >&2
  exit 1
fi

echo "== GC-027 recovery probe run PASSED =="
echo "result     : ${result_file}"
echo "player log : ${log_file}"
echo "note: 'Pass' here means the player process reported it; the result JSON is the evidence to archive."

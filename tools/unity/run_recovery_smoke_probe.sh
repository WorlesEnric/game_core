#!/usr/bin/env bash
# GC-027 release recovery smoke player probe (`-probeRecoverySmoke`).
#
# Runs the already-built player in its release recovery-smoke mode. Unlike every other GC-027 harness, this mode is
# designed to run in the MARKER-FREE RELEASE clone: it drives the PRODUCTION `WorldRecovery.Recover`/`Restart`
# composition with a real `FileCheckpointStore` and no fault latches at all, so it is a mode a shipping build keeps
# and the one release-player run that exercises the recovery path end to end (W7-GATE's backlog item).
#
# What it does per family (narrative and cards, derived from `ProbeRecoverySmoke.QualifiedNames()`):
#   1. builds a real world, commits the family's own command, captures at the committed boundary and PUBLISHES the
#      document to a real file beside the structured result (temp write + flush + replace);
#   2. reads the envelope back with a SECOND store over the same path and requires the identical stored identity and
#      no leftover `.partial` artifact;
#   3. requires the two refusals a recovery must make — a live source is `TooLate`, and a source the registry no
#      longer holds is `StaleHandle` — each with no destination and the registry unchanged;
#   4. recovers a faulted source into a NEW session and restarts from the stored document alone, each publishing a
#      world whose restored target/slot/dormant counts and document identity are reported;
#   5. reads the recovered world's own live state back through the same seeder and requires it to equal the source's
#      recorded rows, with the disposed source session no longer resolvable;
#   6. tears every world down and requires the registry to return to the count the section found.
#
# The digest literal is frozen here, in `ProbeRecoverySmoke` and in the gates that assert it: a renamed, reordered,
# added or dropped observation — or a run that recorded a failing step — cannot report it (P-008).
#
# Required environment:
#   python3 (strict JSON validation)
#
# Optional environment:
#   PROBE_PLAYER   path to the built probe executable
#                  (default: <UNITY_PROJECT>/Builds/Linux64/GameCoreProbe.x86_64)
#   PROBE_RUNS     how many times the probe runs (default 5; must be a positive integer)
#   UNITY_PROJECT  Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS      artifact directory (default: <repo>/artifacts/gc-027/toolchain)
#
# Exit codes: 0 the smoke reported Pass with exit code 0 on every run; nonzero on any mismatch or crash.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
# shellcheck source=probe_runs.sh
source "${SCRIPT_DIR}/probe_runs.sh"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/gc-027/toolchain}"
PROBE_PLAYER="${PROBE_PLAYER:-${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64}"

if [[ ! -x "${PROBE_PLAYER}" ]]; then
  echo "run_recovery_smoke_probe.sh: the probe player is not executable: ${PROBE_PLAYER}" >&2
  echo "  build it first: UNITY=<Unity> UNITY_PROJECT=${UNITY_PROJECT} tools/unity/build_probe.sh" >&2
  exit 2
fi

mkdir -p "${ARTIFACTS}"
# The checkpoint files this mode writes land beside the structured result, so the artifact root is absolutized for the
# same reason every other harness here does it: a relative path would resolve against the player's directory.
ARTIFACTS="$(realpath -m "${ARTIFACTS}")"
failures=0
result_file="${ARTIFACTS}/probe-recovery-smoke.json"
log_file="${ARTIFACTS}/player-recovery-smoke.log"

# -batchmode -nographics keep the player headless. `-probeResult` is REQUIRED by this mode: it is the directory the
# real checkpoint files are published into, and without it the mode records one failing step instead of a verdict.
echo "-- running GC-027 recovery smoke (${PROBE_RUNS} run(s), release-kept mode)"
probe_run_n "recovery-smoke" "${result_file}" "${log_file}" 0 "Pass" "-probeRecoverySmoke" \
  || failures=$((failures + 1))

# The frozen table: eight observations per family, then the digest. The names are `ProbeRecoverySmoke.QualifiedNames()`
# plus `recovery-smoke-digest`; asserting them by name means a dropped or renamed observation fails here rather than
# passing on a shorter run.
PROBE_LABEL="recovery-smoke"
smoke_steps=(
  '"name": "narrative/recovery-smoke-source-world-captures-to-a-real-file"'
  '"name": "narrative/recovery-smoke-published-envelope-reloads-from-disk"'
  '"name": "narrative/recovery-smoke-refuses-a-live-source"'
  '"name": "narrative/recovery-smoke-refuses-a-stopped-source"'
  '"name": "narrative/recovery-smoke-recover-publishes-a-new-session"'
  '"name": "narrative/recovery-smoke-restart-publishes-a-new-session"'
  '"name": "narrative/recovery-smoke-recovered-world-is-authoritative"'
  '"name": "narrative/recovery-smoke-teardown-is-clean"'
  '"name": "cards/recovery-smoke-source-world-captures-to-a-real-file"'
  '"name": "cards/recovery-smoke-published-envelope-reloads-from-disk"'
  '"name": "cards/recovery-smoke-refuses-a-live-source"'
  '"name": "cards/recovery-smoke-refuses-a-stopped-source"'
  '"name": "cards/recovery-smoke-recover-publishes-a-new-session"'
  '"name": "cards/recovery-smoke-restart-publishes-a-new-session"'
  '"name": "cards/recovery-smoke-recovered-world-is-authoritative"'
  '"name": "cards/recovery-smoke-teardown-is-clean"'
  '"name": "recovery-smoke-digest"'
)

# The literal over those sixteen names, all passing, LF separated with no trailing newline. It is recomputed from the
# observation table by the EditMode half of this mode's suite and asserted here, so a table edit is a loud mismatch.
smoke_digest="48d66bd6909091d190f6a04882b93cf326562a25137602f274647f2a365a87bb"

# A run is only evidence when its own JSON carries the whole claim, so every retained run is asserted and not just
# run 1's: probe_run_n already failed a run that crashed, wrote no JSON, exited non-zero or reported a failing step.
smoke_assert_result() {
  local run_result="$1" run_label="$2"

  if ! python3 -m json.tool "${run_result}" >/dev/null 2>&1; then
    echo "run_recovery_smoke_probe.sh: ${run_label}: ${run_result} is not valid JSON" >&2
    failures=$((failures + 1))
    return
  fi

  for fragment in '"task": "GC-027"' '"mode": "RecoverySmoke"' '"result": "Pass"'; do
    if ! grep -q "${fragment}" "${run_result}"; then
      echo "run_recovery_smoke_probe.sh: ${run_label}: the result does not carry ${fragment}" >&2
      failures=$((failures + 1))
    fi
  done

  probe_require_steps "${run_result}" "${smoke_steps[@]}"

  if ! grep -q "digest=${smoke_digest}" "${run_result}"; then
    echo "run_recovery_smoke_probe.sh: ${run_label}: the digest step does not hold its frozen literal" >&2
    failures=$((failures + 1))
  fi

  # The claims this mode exists for, as fragments of its own step details: the production seam really ran, the
  # refusals really refused, the recovered world is a different incarnation with the captured state, and teardown
  # returned the registry to where it started. Each fragment below is a substring the mode writes only when the
  # corresponding claim held, so a renamed observation or a fabricated pass cannot satisfy them.
  local clause
  for clause in \
    "atBoundary=true" \
    "admitted=true" \
    "publishCount=1" \
    "publication(captured=1,published=1" \
    "reloaded=true" \
    "temporaryArtifactRemoved=true" \
    "outcome=Rejected" \
    "code=TooLate" \
    "code=StaleHandle" \
    "registryUnchanged=true" \
    "sourceLifecycle=Disposed" \
    "registered=false" \
    "outcome=Published" \
    "documentPresent=true" \
    "sessionsDiffer=true" \
    "owed=0" \
    "rowsEqual=true" \
    "oldSessionRefused=true" \
    "outstanding=0" \
    "retained=0" \
    "disposed=true" \
    "observations=16"; do
    if ! grep -q -- "${clause}" "${run_result}"; then
      echo "run_recovery_smoke_probe.sh: ${run_label}: the result does not report ${clause}" >&2
      failures=$((failures + 1))
    fi
  done

  # The checkpoint files are REAL files on disk: their absence would mean the mode reported a publication it did not
  # make. They are written beside the result, and the mode removes neither (they are the evidence).
  local label
  for label in narrative cards; do
    if [[ ! -s "${ARTIFACTS}/recovery-smoke-${label}.checkpoint" ]]; then
      echo "run_recovery_smoke_probe.sh: ${run_label}: no checkpoint file for ${label} at" >&2
      echo "   ${ARTIFACTS}/recovery-smoke-${label}.checkpoint" >&2
      failures=$((failures + 1))
    fi
  done
}

for (( run = 1; run <= PROBE_RUNS; run++ )); do
  run_result="${result_file}"
  if (( run > 1 )); then
    run_result="${result_file}.run${run}"
  fi
  smoke_assert_result "${run_result}" "run ${run}/${PROBE_RUNS}"
done

if [[ "${failures}" -ne 0 ]]; then
  echo "run_recovery_smoke_probe.sh: ${failures} recovery-smoke check(s) failed" >&2
  exit 1
fi

echo "== GC-027 recovery smoke run PASSED =="
echo "result     : ${result_file}"
echo "player log : ${log_file}"
echo "checkpoints: ${ARTIFACTS}/recovery-smoke-{narrative,cards}.checkpoint"
echo "note: 'Pass' here means the player process reported it; the result JSON is the evidence to archive."

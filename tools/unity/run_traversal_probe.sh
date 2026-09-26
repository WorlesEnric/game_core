#!/usr/bin/env bash
# GC-020 real-time action reference player probe (GC-020).
#
# Runs the already-built qualification player in its `-probeTraversal` mode. The player itself is built by
# tools/unity/build_probe.sh (GC-001), which compiles every gameplay family and the adapter package into one IL2CPP
# binary; this script only launches it and validates the structured result, exactly like the probes of GC-001,
# GC-005, W1..W5-GATE, GC-010, GC-011, GC-012, GC-013 and GC-019.
#
# The mode runs the traversal course over the hand-written generated-style catalog: this revision has no committed
# generated traversal catalog (their emission is GC-025's catalog-coverage work), and `Gc020Scenario`'s header records
# that decision rather than pretending a second catalog ran. It is the headless half of the GC-020 definition of done:
# the same runner the EditMode assembly `GameCore.Gc020.Tests` calls commits one local `PhysicsScene` simulation per
# admitted step, presents committed animation with a recording sink and plays committed audio with a sink that reports
# no device, so it runs with no renderer and with Unity audio disabled (crash-139).
#
# Required environment:
#   python3 (strict JSON validation)
#
# Optional environment:
#   PROBE_PLAYER   path to the built probe executable
#                  (default: <repo>/unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64)
#   PROBE_RUNS     how many times the probe runs (default 5; must be a positive integer)
#   UNITY_PROJECT  Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS      artifact directory (default: <repo>/artifacts/gc-020/toolchain)
#
# Exit codes: 0 the traversal probe reported Pass with exit code 0 on every run; nonzero on any mismatch or crash.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
# shellcheck source=probe_runs.sh
source "${SCRIPT_DIR}/probe_runs.sh"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/gc-020/toolchain}"
PROBE_PLAYER="${PROBE_PLAYER:-${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64}"

if [[ ! -x "${PROBE_PLAYER}" ]]; then
  echo "run_traversal_probe.sh: probe player not found or not executable: ${PROBE_PLAYER}" >&2
  echo "  build it first with tools/unity/build_probe.sh (GC-001)." >&2
  exit 2
fi

mkdir -p "${ARTIFACTS}"
failures=0
result_file="${ARTIFACTS}/probe-traversal.json"
log_file="${ARTIFACTS}/player-traversal.log"

# -batchmode -nographics keep the player headless. -quit is deliberately NOT passed: the probe exits itself through
# Application.Quit with a code that encodes its result. Unity audio stays disabled in this build (crash-139).
echo "-- running GC-020 traversal probe (${PROBE_RUNS} run(s))"
probe_run_n "traversal" "${result_file}" "${log_file}" 0 "Pass" "-probeTraversal" || failures=$((failures + 1))

if [[ ! -f "${result_file}" ]]; then
  echo "run_traversal_probe.sh: the probe wrote no result file: ${result_file}" >&2
  failures=$((failures + 1))
fi

if ! python3 -m json.tool "${result_file}" >/dev/null; then
  echo "run_traversal_probe.sh: the probe result is not valid JSON: ${result_file}" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"task": "GC-020"' "${result_file}"; then
  echo "run_traversal_probe.sh: the probe result does not declare task GC-020" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"mode": "Traversal"' "${result_file}"; then
  echo "run_traversal_probe.sh: the probe result does not declare mode Traversal" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"result": "Pass"' "${result_file}"; then
  echo "run_traversal_probe.sh: the probe did not report Pass" >&2
  failures=$((failures + 1))
fi

if grep -q '"status": "Fail"' "${result_file}"; then
  echo "run_traversal_probe.sh: the probe reported at least one failing step" >&2
  failures=$((failures + 1))
fi

# Every traversal observation must appear once, qualified with the course's label. The names and their order are
# `Gc020Scenario.ObservationNames`.
traversal_steps=(
  "\"name\": \"traversal/gc020-fixed-step-world-and-runners\""
  "\"name\": \"traversal/gc020-existing-runners-derive-the-modifier\""
  "\"name\": \"traversal/gc020-one-admitted-step-integrates-once\""
  "\"name\": \"traversal/gc020-replay-separates-pure-motion-from-engine-observation\""
  "\"name\": \"traversal/gc020-presentation-rate-does-not-double-advance\""
  "\"name\": \"traversal/gc020-reparent-changes-the-contribution-and-keeps-state\""
  "\"name\": \"traversal/gc020-unmount-keeps-pose-progress-and-receipts\""
  "\"name\": \"traversal/gc020-mode-switch-both-directions\""
  "\"name\": \"traversal/gc020-future-descendant-derives-before-execution\""
  "\"name\": \"traversal/gc020-one-simulation-per-admitted-step\""
  "\"name\": \"traversal/gc020-externally-owned-pose-is-not-integrated\""
  "\"name\": \"traversal/gc020-committed-animation-and-audio-output\""
  "\"name\": \"traversal/gc020-cards-and-narrative-declare-no-action-phase\""
  "\"name\": \"traversal/gc020-teardown-settles-and-disposes\""
  "\"name\": \"gc020-traversal-digest\""
)
probe_require_steps "${result_file}" "${traversal_steps[@]}"

# The digest literal is over the observation names and their pass flags, so this is the whole claim that the course
# ran the named sequence and every step of it passed. It is frozen here and in `GameCore.Gc020.Tests`, which
# recomputes it from `Gc020Scenario.ObservationNames`.
traversal_digest="6263602b82b25315ae33f8ebcc3fbd314586743b9080b0bbe0df34ecc3172ad8"
if ! grep -q "expectedDigest=${traversal_digest}" "${result_file}"; then
  echo "run_traversal_probe.sh: the traversal digest is not the expected value" >&2
  echo "  expected digest=${traversal_digest}" >&2
  failures=$((failures + 1))
fi

# The task's own acceptance clauses, asserted as fragments of the step details rather than as bare pass flags: a run
# that recorded the right step names but did not really demonstrate the clause cannot satisfy these. Every fragment is
# a substring of a detail this scenario really writes (see `Gc020Scenario`), so a rename of one fails loudly instead of
# silently checking nothing.
for clause in \
  "stepMillis=20" \
  "maxStepsPerPump=4" \
  "idleSteps=0" \
  "identitiesAgree=True" \
  "targetsWithoutStorage=<none>" \
  "targetsUnbound=<none>" \
  "generatedCatalog=absent" \
  "providerValue=2000" \
  "checkpointRows=<none>" \
  "publicationMovedNothing=True" \
  "admission=Admitted" \
  "expectedVelocity=1040" \
  "poseAdvanceX=20" \
  "perturbedBy=1" \
  "atTolerance0=False" \
  "atDeclaredTolerance=True" \
  "sameSteps=True" \
  "sameVelocity=True" \
  "extra144HzFrames=144" \
  "reparented=True" \
  "expectedVelocity=1020" \
  "unmountedHeadwind=True" \
  "unmountedTailwind=True" \
  "activeRowAfterUnmount=False" \
  "optedInKept=True" \
  "plainLost=True" \
  "plainRegained=True" \
  "isolatedNeverHad=True" \
  "registered=True" \
  "bound=True" \
  "onePerAdmittedStep=True" \
  "duplicateRefused=True" \
  "poseReflectsIntent=True" \
  "dedicatedLocalScene=True" \
  "automaticSuppressed=True" \
  "integratedInStep=0" \
  "reselectRefused=True(OwnershipConflict" \
  "presentedOnce=True" \
  "presentedAgain=False" \
  "staleRefusals=1" \
  "secondPassPlayed=0" \
  "disabledPlays=0" \
  "offenders=<none>" \
  "narrativePipeline=Built" \
  "cardPipeline=Built" \
  "traceDetached=True" \
  "physicsDisposed=True" \
  "globalModeRestored=True" \
  "outstandingJobs=0" \
  "retainedResources=0"; do
  if ! grep -q "${clause}" "${result_file}"; then
    echo "run_traversal_probe.sh: the acceptance clause fragment '${clause}' is absent from the result" >&2
    failures=$((failures + 1))
  fi
done

if [[ "${failures}" -ne 0 ]]; then
  echo "run_traversal_probe.sh: ${failures} GC-020 probe check(s) failed" >&2
  exit 1
fi

echo "== GC-020 traversal probe run PASSED =="
echo "result     : ${result_file}"
echo "player log : ${log_file}"
echo "note: 'Pass' here means the player process reported it; the result JSON is the evidence to archive."

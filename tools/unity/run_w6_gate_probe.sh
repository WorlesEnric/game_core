#!/usr/bin/env bash
# Wave 6 integration-gate player probe (W6-GATE).
#
# Runs the already-built qualification player in its Wave 6 gate mode. The player is built by
# tools/unity/build_probe.sh, which compiles all three gameplay families, the kernel and GC-023's instrumented
# telemetry build into the same IL2CPP binary; this script only launches it and validates the structured result,
# exactly like the Wave 5 gate's harness does for its mode.
#
# The mode runs the wave-6 gate scenario over the three genres: the narrative slice and the card market over both the
# committed generated catalog and their hand-written generated-style catalogs, and the traversal course over the one
# fixture catalog this revision owns (a generated traversal catalog is GC-025's catalog-coverage work, so the gate
# says "one catalog" rather than implying a second run). The fixture runs' steps carry the `fixture:` name prefix. It
# is the IL2CPP half of the Wave 6 gate: the same runner the EditMode assembly `GameCore.W6Gate.Tests` calls is
# executed here in a stripped, High-stripping player, so a join that only works in the Editor cannot pass.
#
# Three process-level steps come first, because everything below them is a claim about an environment:
#   * the resolved cycle count (so a reduced run cannot be mistaken for the 1,000-cycle gate),
#   * the native leak detection mode, which the attribution below needs to be meaningful,
#   * the telemetry build shape, because the cost counters are compiled OUT of a release build by design.
#
# Native leak attribution. The 1,000-cycle teardown claim is only evidence about native allocations when the player's
# own shutdown report is attributed: this script runs `tools/attribute_native_leaks.py` over every retained run log
# against GC-022's resource policy, so a GameCore-owned allocation or an allocation with no declared bound is a FAILED
# gate rather than a footnote. Audio stays disabled in this player (crash-139) and is never re-enabled here.
#
# Required environment:
#   python3 (strict JSON validation, and tools/attribute_native_leaks.py)
#
# Optional environment:
#   PROBE_PLAYER              path to the built probe executable
#                             (default: <UNITY_PROJECT>/Builds/Linux64/GameCoreProbe.x86_64)
#   PROBE_RUNS                how many times the probe runs (default 5; must be a positive integer)
#   UNITY_PROJECT             Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS                 artifact directory (default: <repo>/artifacts/w6-gate/toolchain)
#   GC_W6_GATE_CYCLES         counted cycles per family and catalog (default 1000)
#   LEAK_POLICY               resource policy the attributor checks (default: GC-022's)
#   LEAK_BASELINE             optional earlier attribution JSON to compare against
#   UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE  leak detection mode; 2 prints callstacks (default 2)
#
# Exit codes: 0 the gate probe reported Pass with exit code 0 on every run and the attribution was clean; 1 on any
# mismatch, crash or attribution finding; 2 on a missing prerequisite or invalid configuration.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
# shellcheck source=probe_runs.sh
source "${SCRIPT_DIR}/probe_runs.sh"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/w6-gate/toolchain}"
PROBE_PLAYER="${PROBE_PLAYER:-${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64}"
GC_W6_GATE_CYCLES="${GC_W6_GATE_CYCLES:-1000}"
LEAK_POLICY="${LEAK_POLICY:-${REPO_ROOT}/artifacts/gc-022/leak/policy.md}"
LEAK_BASELINE="${LEAK_BASELINE:-}"
LEAK_ATTRIBUTOR="${REPO_ROOT}/tools/attribute_native_leaks.py"
UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE="${UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE:-2}"
export UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE

if [[ ! -x "${PROBE_PLAYER}" ]]; then
  echo "run_w6_gate_probe.sh: probe player not found or not executable: ${PROBE_PLAYER}" >&2
  echo "  build it first with tools/unity/build_probe.sh." >&2
  exit 2
fi
if ! [[ "${GC_W6_GATE_CYCLES}" =~ ^[1-9][0-9]*$ ]]; then
  echo "run_w6_gate_probe.sh: GC_W6_GATE_CYCLES must be a positive integer: ${GC_W6_GATE_CYCLES}" >&2
  exit 2
fi
if ! [[ "${UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE}" =~ ^[0-2]$ ]]; then
  echo "run_w6_gate_probe.sh: UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE must be 0, 1 or 2" >&2
  exit 2
fi

mkdir -p "${ARTIFACTS}"
failures=0
result_file="${ARTIFACTS}/probe-w6-gate.json"
log_file="${ARTIFACTS}/player-w6-gate.log"

echo "-- running Wave 6 gate probe (${PROBE_RUNS} run(s), ${GC_W6_GATE_CYCLES} cycles per family and catalog)"
probe_run_n "w6-gate" "${result_file}" "${log_file}" 0 "Pass" "-probeW6Gate" || failures=$((failures + 1))

if [[ ! -f "${result_file}" ]]; then
  echo "run_w6_gate_probe.sh: the probe wrote no result file: ${result_file}" >&2
  failures=$((failures + 1))
fi

if ! python3 -m json.tool "${result_file}" >/dev/null 2>&1; then
  echo "run_w6_gate_probe.sh: the probe result is not valid JSON: ${result_file}" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"task": "W6-GATE"' "${result_file}"; then
  echo "   FAIL w6-gate: the result does not name task W6-GATE" >&2
  failures=$((failures + 1))
fi
if ! grep -q '"mode": "W6Gate"' "${result_file}"; then
  echo "   FAIL w6-gate: the result does not name mode W6Gate" >&2
  failures=$((failures + 1))
fi
if ! grep -q '"result": "Pass"' "${result_file}"; then
  echo "   FAIL w6-gate: the result is not Pass" >&2
  failures=$((failures + 1))
fi
if grep -q '"status": "Fail"' "${result_file}"; then
  echo "   FAIL w6-gate: at least one recorded step failed" >&2
  failures=$((failures + 1))
fi

# Every observation the gate records, by name: three process steps, the narrative slice over both catalogs, the card
# market over both catalogs, the traversal course over its one catalog, and one digest step per family.
PROBE_LABEL="w6-gate"
w6_steps=(
  '"name": "w6-gate-cycle-count"'
  '"name": "w6-gate-native-leak-detection"'
  '"name": "w6-gate-telemetry-build-shape"'
  '"name": "narrative/w6-declares-no-action-physics-or-audio-surface"'
  '"name": "narrative/w6-adapter-assembly-is-absent"'
  '"name": "narrative/w6-thousand-cycle-teardown-is-bounded"'
  '"name": "narrative/w6-ledger-and-fence-high-water-marks-are-bounded"'
  '"name": "narrative/w6-counters-return-to-baseline"'
  '"name": "narrative/w6-repeatable-digest"'
  '"name": "fixture:narrative/w6-declares-no-action-physics-or-audio-surface"'
  '"name": "fixture:narrative/w6-adapter-assembly-is-absent"'
  '"name": "fixture:narrative/w6-thousand-cycle-teardown-is-bounded"'
  '"name": "fixture:narrative/w6-ledger-and-fence-high-water-marks-are-bounded"'
  '"name": "fixture:narrative/w6-counters-return-to-baseline"'
  '"name": "fixture:narrative/w6-repeatable-digest"'
  '"name": "cards/w6-declares-no-action-physics-or-audio-surface"'
  '"name": "cards/w6-adapter-assembly-is-absent"'
  '"name": "cards/w6-reward-delivery-is-exactly-once-across-a-reload"'
  '"name": "cards/w6-thousand-cycle-teardown-is-bounded"'
  '"name": "cards/w6-ledger-and-fence-high-water-marks-are-bounded"'
  '"name": "cards/w6-counters-return-to-baseline"'
  '"name": "cards/w6-repeatable-digest"'
  '"name": "fixture:cards/w6-declares-no-action-physics-or-audio-surface"'
  '"name": "fixture:cards/w6-adapter-assembly-is-absent"'
  '"name": "fixture:cards/w6-reward-delivery-is-exactly-once-across-a-reload"'
  '"name": "fixture:cards/w6-thousand-cycle-teardown-is-bounded"'
  '"name": "fixture:cards/w6-ledger-and-fence-high-water-marks-are-bounded"'
  '"name": "fixture:cards/w6-counters-return-to-baseline"'
  '"name": "fixture:cards/w6-repeatable-digest"'
  '"name": "traversal/w6-fixed-step-course-runs"'
  '"name": "traversal/w6-telemetry-counters-record-admitted-steps"'
  '"name": "traversal/w6-one-physics-simulation-per-admitted-step"'
  '"name": "traversal/w6-replay-reproduces-the-rule-digest"'
  '"name": "traversal/w6-carries-the-optional-engine-surface"'
  '"name": "traversal/w6-thousand-cycle-teardown-is-bounded"'
  '"name": "traversal/w6-ledger-and-fence-high-water-marks-are-bounded"'
  '"name": "traversal/w6-counters-return-to-baseline"'
  '"name": "traversal/w6-repeatable-digest"'
  '"name": "w6-narrative-digest"'
  '"name": "w6-cards-digest"'
  '"name": "w6-traversal-digest"'
)
probe_require_steps "${result_file}" "${w6_steps[@]}"

# The claims this gate exists for, as clauses of the recorded details.
w6_clauses=(
  "telemetryCompiledIn=True"
  "stepsAdvanced=5"
  "gateSimulatedSteps=5"
  "engineSimulations=5"
  "digestsAgree=True"
  "atTolerance0=False"
  "duplicateRefused=True"
  "animationStage=True"
  "animationSink=True"
  "offenders=<none>"
  "narrativeAdapters=False"
  "cardsAdapters=False"
  "traversalAdapters=True"
  "sessionsDiffer=True"
  "destinationMutations=1"
  "destinationAttempts=2"
  "destinationAlreadyApplied=1"
  "reinstatedAgain=True"
  "durable=True"
  "completed=${GC_W6_GATE_CYCLES}"
  "cycles=${GC_W6_GATE_CYCLES}"
  "liveLeases=0"
  "fenceOutstandingHighWater=0"
  "quarantineEntries=0"
  "evictedRetired=0"
  "instancesMissing=0"
  "instancesRetained=0"
)
for clause in "${w6_clauses[@]}"; do
  if ! grep -q -- "${clause}" "${result_file}"; then
    echo "   FAIL w6-gate: the result does not report ${clause}" >&2
    failures=$((failures + 1))
  fi
done

# The frozen digest literals: recomputed from the observation tables by the EditMode suite, and compared here against
# the committed constants. A renamed observation or a failing step cannot report these values.
narrative_generated_digest="4235cea3c22cf93e38307000fcc863edd4ada3e2fc4a3b475ca719765dc17279"
narrative_fixture_digest="ab198371e8d276dcc2833e88e64e278abde57274ea3c68e2497fa2b75f97fa73"
cards_generated_digest="894a975e4a5eb98d733ec213778ec275e4abe02cb4028271857bd4fa68d6fe80"
cards_fixture_digest="9c3d3b5d2f8e959920aa9678e65f56658cbe4a513fcd2895cd458fbda4f9ea14"
traversal_digest="ca29b5f7099f9fbc4abeb7031c7b1e35ba39ec1ad39db70dd0f037fa8d45675e"

if ! grep -q "generatedDigest=${narrative_generated_digest}" "${result_file}" \
  || ! grep -q "fixtureDigest=${narrative_fixture_digest}" "${result_file}"; then
  echo "   FAIL w6-gate: the narrative digest step does not hold its frozen literal" >&2
  failures=$((failures + 1))
fi
if ! grep -q "generatedDigest=${cards_generated_digest}" "${result_file}" \
  || ! grep -q "fixtureDigest=${cards_fixture_digest}" "${result_file}"; then
  echo "   FAIL w6-gate: the card digest step does not hold its frozen literal" >&2
  failures=$((failures + 1))
fi
if ! grep -q "expectedDigest=${traversal_digest}" "${result_file}" \
  || ! grep -q "catalogs=1" "${result_file}"; then
  echo "   FAIL w6-gate: the traversal digest step does not hold its frozen literal over one catalog" >&2
  failures=$((failures + 1))
fi

# The cycle count this run really measured, so a reduced run cannot be reported as the 1,000-cycle gate.
if ! grep -q "resolvedCycleCount=${GC_W6_GATE_CYCLES}" "${result_file}"; then
  echo "   FAIL w6-gate: the probe did not resolve ${GC_W6_GATE_CYCLES} cycles" >&2
  failures=$((failures + 1))
fi

# Native leak attribution over every retained run log.
if [[ "${UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE}" != "2" ]]; then
  echo "-- native-leak-attribution: NOT RUN"
  echo "   leak detection mode ${UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE} prints no callstacks; need 2 (EnabledWithStackTrace)."
  echo "   Re-run with UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE=2 to attribute this gate's shutdown allocations."
elif [[ ! -f "${LEAK_ATTRIBUTOR}" ]]; then
  echo "   FAIL w6-gate: attributor not found: ${LEAK_ATTRIBUTOR}" >&2
  failures=$((failures + 1))
elif [[ ! -f "${LEAK_POLICY}" ]]; then
  echo "   FAIL w6-gate: resource policy not found: ${LEAK_POLICY}" >&2
  failures=$((failures + 1))
else
  attribution_logs=("--log" "${log_file}")
  for (( run = 2; run <= PROBE_RUNS; run++ )); do
    if [[ -f "${log_file}.run${run}" ]]; then
      attribution_logs+=("--log" "${log_file}.run${run}")
    fi
  done
  attribution_json="${ARTIFACTS}/native-leak-attribution.json"
  attribution_args=("${attribution_logs[@]}" "--out" "${attribution_json}" "--policy" "${LEAK_POLICY}")
  if [[ -n "${LEAK_BASELINE}" ]]; then
    attribution_args+=("--baseline" "${LEAK_BASELINE}")
  fi

  attribution_file_count=$(( ${#attribution_logs[@]} / 2 ))
  echo "-- attributing native leaks in ${attribution_file_count} log(s)"
  attribution_rc=0
  python3 "${LEAK_ATTRIBUTOR}" ${attribution_args[@]+"${attribution_args[@]}"} || attribution_rc=$?
  if (( attribution_rc != 0 )); then
    echo "   FAIL w6-gate: native leak attribution exit ${attribution_rc}" >&2
    echo "   attribution: ${attribution_json}; a GameCore-owned allocation is a defect and an allocation with no" >&2
    echo "   declared bound is one too: add its frame signature and bound to ${LEAK_POLICY}." >&2
    failures=$((failures + 1))
  fi
fi

if [[ "${failures}" -ne 0 ]]; then
  echo "== Wave 6 gate probe run FAILED (${failures} check(s)) ==" >&2
  exit 1
fi

echo "== Wave 6 gate probe run PASSED =="
echo "result     : ${result_file}"
echo "player log : ${log_file}"
echo "cycles     : ${GC_W6_GATE_CYCLES} per family and catalog"
echo "note: 'Pass' here means the player process reported it in the result JSON, which is the evidence to archive."

#!/usr/bin/env bash
# Wave 7 integration-gate player probe (W7-GATE).
#
# Runs the already-built qualification player in its Wave 7 gate mode. The player is built by
# tools/unity/build_probe.sh; this script only launches it and validates the structured result, exactly like every
# earlier gate's harness does for its mode.
#
# The mode runs `W7GateScenario` on the merged revision: GC-025's own catalog coverage sequence, the GC-026-affected
# incremental-versus-clean derivation equivalence at the declared 10,000-target scale, the two merge invariants (every
# probe mode still parses; the ten recorded budget rows are still the declared ten), and GC-027's own recovery
# sequence for all three genres with its postwrite-apply and restart fault points named. It is the in-player half of
# the Wave 7 gate, so a join that only works in the Editor cannot pass.
#
# Audio stays disabled in this player (crash-139) and is never re-enabled here. Every path this script retains is
# absolutized first: Unity resolves a relative `-logFile` against the player's directory while this script re-opens it
# against its working directory, and the native-leak attribution below re-opens the very files the player wrote.
#
# Required environment:
#   python3 (strict JSON validation, and tools/attribute_native_leaks.py)
#
# Optional environment:
#   PROBE_PLAYER              path to the built probe executable
#                             (default: <UNITY_PROJECT>/Builds/Linux64/GameCoreProbe.x86_64)
#   PROBE_RUNS                how many times the probe runs (default 5; must be a positive integer)
#   UNITY_PROJECT             Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS                 artifact directory (default: <repo>/artifacts/w7-gate/toolchain)
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
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/w7-gate/toolchain}"
PROBE_PLAYER="${PROBE_PLAYER:-${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64}"
LEAK_POLICY="${LEAK_POLICY:-${REPO_ROOT}/artifacts/gc-022/leak/policy.md}"
LEAK_BASELINE="${LEAK_BASELINE:-}"
LEAK_ATTRIBUTOR="${REPO_ROOT}/tools/attribute_native_leaks.py"
UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE="${UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE:-2}"
export UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE

if [[ ! -x "${PROBE_PLAYER}" ]]; then
  echo "run_w7_gate_probe.sh: the probe player is not executable: ${PROBE_PLAYER}" >&2
  echo "  build it first: UNITY=<Unity> UNITY_PROJECT=${UNITY_PROJECT} tools/unity/build_probe.sh" >&2
  exit 2
fi
if ! [[ "${UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE}" =~ ^[0-2]$ ]]; then
  echo "run_w7_gate_probe.sh: UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE must be 0, 1 or 2" >&2
  exit 2
fi

mkdir -p "${ARTIFACTS}"
# One absolute artifact root: it keeps the log the player wrote and the log the attributor reads the same file.
ARTIFACTS="$(realpath -m "${ARTIFACTS}")"
failures=0
result_file="${ARTIFACTS}/probe-w7-gate.json"
log_file="${ARTIFACTS}/player-w7-gate.log"

echo "-- running Wave 7 gate probe (${PROBE_RUNS} run(s))"
probe_run_n "w7-gate" "${result_file}" "${log_file}" 0 "Pass" "-probeW7Gate" || failures=$((failures + 1))

# The frozen observation table: the four process observations, the recovery group for each of the three genres, the
# digest step, and the probe's own literal-agreement step. The names are `W7GateScenario.ObservationNames()` plus
# `W7GateScenario.DigestStepName` plus `w7-frozen-digest-agrees-with-the-table`; this harness asserts them by name so
# a dropped or renamed observation fails here instead of passing on a shorter run.
PROBE_LABEL="w7-gate"
w7_steps=(
  '"name": "w7/w7-catalog-coverage-repasses-on-the-merged-kernel"'
  '"name": "w7/w7-incremental-derivation-matches-a-clean-derivation"'
  '"name": "w7/w7-probe-mode-dispatch-keeps-every-mode"'
  '"name": "w7/w7-declared-budget-rows-are-the-recorded-ten"'
  '"name": "narrative/w7-recovery-sequence-repasses"'
  '"name": "narrative/w7-recovery-postwrite-apply-fault"'
  '"name": "narrative/w7-recovery-restart-without-in-process-state"'
  '"name": "cards/w7-recovery-sequence-repasses"'
  '"name": "cards/w7-recovery-postwrite-apply-fault"'
  '"name": "cards/w7-recovery-restart-without-in-process-state"'
  '"name": "traversal/w7-recovery-sequence-repasses"'
  '"name": "traversal/w7-recovery-postwrite-apply-fault"'
  '"name": "traversal/w7-recovery-restart-without-in-process-state"'
  '"name": "w7/w7-gate-digest"'
  '"name": "w7-frozen-digest-agrees-with-the-table"'
)

# Each run's own JSON must carry the whole claim, not just run 1's: probe_run_n already failed a run that crashed,
# wrote no JSON, exited non-zero or reported a failing step, so these assertions cover every retained run.
w7_assert_result() {
  local run_result="$1" run_label="$2"

  if ! python3 -m json.tool "${run_result}" >/dev/null 2>&1; then
    echo "run_w7_gate_probe.sh: ${run_label}: ${run_result} is not valid JSON" >&2
    failures=$((failures + 1))
    return
  fi

  for fragment in '"task": "W7-GATE"' '"mode": "W7Gate"' '"result": "Pass"'; do
    if ! grep -q "${fragment}" "${run_result}"; then
      echo "run_w7_gate_probe.sh: ${run_label}: the result does not carry ${fragment}" >&2
      failures=$((failures + 1))
    fi
  done

  probe_require_steps "${run_result}" "${w7_steps[@]}"

  # The clauses each group's own details must carry: a run that recorded the right step names but did not really run
  # the sequence cannot satisfy a fragment its scenario writes only when the sequence ran.
  local clause
  for clause in \
    "coverage=digest=" \
    "observations=13" \
    "failed=0" \
    "seeds=3" \
    "edits=install-mount,reparent,spawn-1000,retire-1000" \
    "scale=1000scopes/10000targets" \
    "modes=24" \
    "missing=<none>" \
    "ambiguous=<none>" \
    "resultPathParsed=True" \
    "rows=10" \
    "declared=10" \
    "reportOnly=2" \
    "problems=<none>" \
    "faultPoints=postwrite-apply,restart" \
    "destinationAttempts=0" \
    "expectedObservations=13"; do
    if ! grep -q -- "${clause}" "${run_result}"; then
      echo "run_w7_gate_probe.sh: ${run_label}: the result does not report ${clause}" >&2
      failures=$((failures + 1))
    fi
  done
}

# The frozen digest literal: recomputed from the observation table by the EditMode suite `GameCore.W7Gate.Tests` and
# compared here against the committed constant. A renamed, reordered, added or dropped observation — or a gate whose
# run recorded a failing step — cannot report this value.
w7_digest="44742f36096f9005b18b8729f7b945583315a676902af32d7a2f8db14d197b62"

for (( run = 1; run <= PROBE_RUNS; run++ )); do
  run_result="${result_file}"
  if (( run > 1 )); then
    run_result="${result_file}.run${run}"
  fi
  w7_assert_result "${run_result}" "run ${run}/${PROBE_RUNS}"

  if ! grep -q "digest=${w7_digest}" "${run_result}"; then
    echo "run_w7_gate_probe.sh: run ${run}/${PROBE_RUNS}: the digest step does not hold its frozen literal" >&2
    failures=$((failures + 1))
  fi

  if ! grep -q "expectedDigest=${w7_digest}" "${run_result}"; then
    echo "run_w7_gate_probe.sh: run ${run}/${PROBE_RUNS}: the frozen literal is not the table's own digest" >&2
    failures=$((failures + 1))
  fi
done

# Native leak attribution over every retained run log: the gate creates and destroys many worlds, so an unattributed
# allocation at shutdown is a finding rather than a footnote (P-048).
if [[ "${UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE}" != "2" ]]; then
  echo "-- native-leak-attribution: NOT RUN"
  echo "   leak detection mode ${UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE} prints no callstacks; need 2."
  echo "   Re-run with UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE=2 to attribute this gate's shutdown allocations."
elif [[ ! -f "${LEAK_ATTRIBUTOR}" ]]; then
  echo "   FAIL w7-gate: attributor not found: ${LEAK_ATTRIBUTOR}" >&2
  failures=$((failures + 1))
elif [[ ! -f "${LEAK_POLICY}" ]]; then
  echo "   FAIL w7-gate: resource policy not found: ${LEAK_POLICY}" >&2
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
    echo "   FAIL w7-gate: native leak attribution exit ${attribution_rc}" >&2
    echo "   attribution: ${attribution_json}; a GameCore-owned allocation is a defect and an allocation with no" >&2
    echo "   declared bound is one too: add its frame signature and bound to ${LEAK_POLICY}." >&2
    failures=$((failures + 1))
  fi
fi

if [[ "${failures}" -ne 0 ]]; then
  echo "run_w7_gate_probe.sh: ${failures} Wave 7 gate check(s) failed" >&2
  exit 1
fi

echo "== Wave 7 gate probe run PASSED =="
echo "result     : ${result_file}"
echo "player log : ${log_file}"
echo "note: 'Pass' here means the player process reported it in the result JSON, which is the evidence to archive."

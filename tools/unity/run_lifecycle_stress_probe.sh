#!/usr/bin/env bash
# GC-022 lifecycle stress player probe, under native leak detection with full stack traces.
#
# Runs the already-built qualification player in its `-probeLifecycleStress` mode: for each family one real family
# world over the committed generated catalog (the counted mount/unmount cycles) and one over the fixture declaration
# identity set, with delayed completions, stalled jobs, a throwing disposer and required-provider churn. The player is
# built by tools/unity/build_probe.sh; this script only launches it and validates the structured result, exactly like
# tools/unity/run_w5_gate_probe.sh does for its mode.
#
# Leak detection. The mode is armed in both halves of the toolchain from one variable, and it is exported here rather
# than passed inline so every Unity invocation (this player, and the Editor runs a build host wraps around it) sees it:
#
#   UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE=2
#
# The Editor half is the Collections package's own hook (UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE -> NativeLeakDetection
# .Mode); the player half is the probe itself, which applies the same value and records the resulting mode as a step.
# 2 = EnabledWithStackTrace, and only 2 makes Unity print `Found N leak(s) from callstack:` groups, which is what
# tools/attribute_native_leaks.py needs to attribute an allocation to a frame. With 1 or 0 this script says so and
# skips the attribution step instead of pretending the run attributed anything.
#
# Required environment:
#   python3 (strict JSON validation, and tools/attribute_native_leaks.py)
#
# Optional environment:
#   PROBE_PLAYER   path to the built probe executable
#                  (default: <UNITY_PROJECT>/Builds/Linux64/GameCoreProbe.x86_64)
#   PROBE_RUNS     how many times the probe runs (default 5; must be a positive integer)
#   UNITY_PROJECT  Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS      artifact directory (default: <repo>/artifacts/gc-022/leak/player)
#   GC_LIFECYCLE_STRESS_CYCLES    resolved cycle count of the generated-catalog run (default 1000)
#   LEAK_POLICY    resource policy the attribution gate checks (default: <repo>/artifacts/gc-022/leak/policy.md)
#   LEAK_BASELINE  optional earlier attribution JSON (or signature list) to report new signatures against
#
# Exit codes: 0 the probe reported Pass with exit code 0 on every run and the leak attribution gate was satisfied;
# nonzero on any mismatch, crash, unattributed/ unbounded allocation, or any GameCore-owned leak.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
# shellcheck source=probe_runs.sh
source "${SCRIPT_DIR}/probe_runs.sh"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/gc-022/leak/player}"
PROBE_PLAYER="${PROBE_PLAYER:-${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64}"
LEAK_POLICY="${LEAK_POLICY:-${REPO_ROOT}/artifacts/gc-022/leak/policy.md}"
LEAK_BASELINE="${LEAK_BASELINE:-}"
LEAK_ATTRIBUTOR="${REPO_ROOT}/tools/attribute_native_leaks.py"

# The player probe reads this variable itself and records the resulting NativeLeakDetection.Mode as a step; the
# Collections package reads the same variable in the Editor, so one exported value covers both halves.
UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE="${UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE:-2}"
if [[ ! "${UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE}" =~ ^[0-2]$ ]]; then
  echo "UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE must be 0, 1 or 2, got '${UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE}'" >&2
  exit 2
fi
export UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE

GC_LIFECYCLE_STRESS_CYCLES="${GC_LIFECYCLE_STRESS_CYCLES:-1000}"
if [[ ! "${GC_LIFECYCLE_STRESS_CYCLES}" =~ ^[[:digit:]]+$ ]] || (( GC_LIFECYCLE_STRESS_CYCLES < 1 )); then
  echo "GC_LIFECYCLE_STRESS_CYCLES must be a positive integer, got '${GC_LIFECYCLE_STRESS_CYCLES}'" >&2
  exit 2
fi
export GC_LIFECYCLE_STRESS_CYCLES

if [[ ! -x "${PROBE_PLAYER}" ]]; then
  echo "probe player not found or not executable: ${PROBE_PLAYER}" >&2
  echo "build it first: UNITY=<editor> tools/unity/build_probe.sh" >&2
  exit 2
fi

if [[ ! -f "${LEAK_POLICY}" ]]; then
  echo "resource policy not found: ${LEAK_POLICY}" >&2
  echo "an allocation with no declared bound is a defect, so the attribution gate needs the policy file." >&2
  exit 2
fi

mkdir -p "${ARTIFACTS}"
failures=0
result_file="${ARTIFACTS}/probe-lifecycle-stress.json"
log_file="${ARTIFACTS}/player-lifecycle-stress.log"

# -batchmode -nographics keep the player headless. -quit is deliberately NOT passed: the probe exits itself through
# Application.Quit with a code that encodes its result. Audio is never re-enabled (crash-139).
echo "-- running GC-022 lifecycle stress probe (${PROBE_RUNS} run(s), ${GC_LIFECYCLE_STRESS_CYCLES} cycle(s))"
echo "   leak detection: UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE=${UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE}"
probe_run_n "lifecycle-stress" "${result_file}" "${log_file}" 0 "Pass" "-probeLifecycleStress" || failures=$((failures + 1))

if [[ ! -f "${result_file}" ]]; then
  echo "   FAIL lifecycle-stress: no result written to ${result_file}" >&2
  exit 1
fi

if ! python3 -m json.tool "${result_file}" >/dev/null; then
  echo "   FAIL lifecycle-stress: ${result_file} is not valid JSON" >&2
  exit 1
fi

if ! grep -q '"task": "GC-022"' "${result_file}"; then
  echo "   FAIL lifecycle-stress: ${result_file} does not name task GC-022" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"mode": "LifecycleStress"' "${result_file}"; then
  echo "   FAIL lifecycle-stress: ${result_file} does not name mode LifecycleStress" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"result": "Pass"' "${result_file}"; then
  echo "   FAIL lifecycle-stress: ${result_file} does not report result Pass" >&2
  failures=$((failures + 1))
fi

if grep -q '"status": "Fail"' "${result_file}"; then
  echo "   FAIL lifecycle-stress: at least one probe step reported status Fail" >&2
  failures=$((failures + 1))
fi

# The 12 frozen observation names (Contract B) of the lifecycle-stress sentence, each qualified by its family. The
# generated-catalog run emits them unprefixed; the fixture-catalog run emits the same names behind `fixture:`, and the
# digest literal below is the preimage proof for that second catalog, so it is not re-grepped by name here.
PROBE_LABEL="lifecycle-stress"
lifecycle_stress_observations=(
  "lifecycle-stress-world-baseline"
  "lifecycle-stress-cycles-complete"
  "lifecycle-stress-counters-return-to-baseline"
  "lifecycle-stress-acquisitions-traced-to-retirement"
  "lifecycle-stress-delayed-completions-are-discarded"
  "lifecycle-stress-stalled-job-retains-buffers"
  "lifecycle-stress-throwing-disposer-keeps-cleanup-going"
  "lifecycle-stress-required-provider-churn"
  "lifecycle-stress-loop-nodes-do-not-accumulate"
  "lifecycle-stress-registry-returns-to-baseline"
  "lifecycle-stress-incarnations-are-fresh"
  "lifecycle-stress-repeatable-digest"
)
lifecycle_stress_steps=()
for family in narrative cards; do
  for observation in "${lifecycle_stress_observations[@]}"; do
    lifecycle_stress_steps+=("\"name\": \"${family}/${observation}\"")
  done
done
probe_require_steps "${result_file}" ${lifecycle_stress_steps[@]+"${lifecycle_stress_steps[@]}"}

# Both digest lines must name the exact literals ProbeLifecycleStress declares: the digest is over the observation
# names and their pass flags in emission order, so these two fragments are the whole claim that both families ran the
# named sequence over the committed generated catalog *and* over the fixture identity set, and every step passed.
narrative_generated_digest="c743b4503dff2cf719bda1055964ec225307100bfb828a94aaa26af39639c4af"
narrative_fixture_digest="dfe2e14f66e912febed6c2e32d0697f4f981c1c09467f500242f06a838ee3652"
cards_generated_digest="bc9321062734d84582a7ff50ac3f0e18c24f77bfdd45103c2958a1991e849f23"
cards_fixture_digest="f601e7378d5a800bf740441b626d323cd0dc6b3d4b3c53b43a0db3a20c7544b3"
if ! grep -q "generatedDigest=${narrative_generated_digest}; fixtureDigest=${narrative_fixture_digest}" "${result_file}"; then
  echo "   FAIL lifecycle-stress: the narrative digest line is not the expected pair" >&2
  failures=$((failures + 1))
fi
if ! grep -q "generatedDigest=${cards_generated_digest}; fixtureDigest=${cards_fixture_digest}" "${result_file}"; then
  echo "   FAIL lifecycle-stress: the card digest line is not the expected pair" >&2
  failures=$((failures + 1))
fi

# The two process observations: the run archives how much churn it measured, and that the leak detection mode it asked
# for is the mode that was applied. A run that did not apply the requested mode cannot attribute the allocations its
# shutdown report lists, so it is not a passing run of this mode.
if ! grep -q "\"name\": \"lifecycle-stress-cycle-count\"" "${result_file}"; then
  echo "   FAIL lifecycle-stress: the resolved cycle count step is absent" >&2
  failures=$((failures + 1))
fi
if ! grep -q "resolvedCycleCount=${GC_LIFECYCLE_STRESS_CYCLES}" "${result_file}"; then
  echo "   FAIL lifecycle-stress: the resolved cycle count is not ${GC_LIFECYCLE_STRESS_CYCLES}" >&2
  failures=$((failures + 1))
fi
if ! grep -q "\"name\": \"lifecycle-stress-native-leak-detection\"" "${result_file}"; then
  echo "   FAIL lifecycle-stress: the native leak detection step is absent" >&2
  failures=$((failures + 1))
fi
if ! grep -q "applied=True" "${result_file}"; then
  echo "   FAIL lifecycle-stress: the requested native leak detection mode was not applied" >&2
  failures=$((failures + 1))
fi

# Native leak attribution. The player's shutdown leak report is in the run logs; attribute every allocation to a frame,
# and fail when a GameCore frame owns one (that is the defect this task drives to zero) or when an allocation has no
# declared bound in the resource policy.
if [[ "${UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE}" != "2" ]]; then
  echo "-- native-leak-attribution: NOT RUN"
  echo "   leak detection mode ${UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE} prints no callstacks; need 2 (EnabledWithStackTrace)."
  echo "   Re-run with UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE=2 to attribute the shutdown allocations."
elif [[ ! -f "${LEAK_ATTRIBUTOR}" ]]; then
  echo "   FAIL lifecycle-stress: attributor not found: ${LEAK_ATTRIBUTOR}" >&2
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
    echo "   FAIL lifecycle-stress: native leak attribution exit ${attribution_rc}" >&2
    echo "   attribution: ${attribution_json}; a GameCore-owned allocation is a defect and an allocation with no" >&2
    echo "   declared bound is one too: add its frame signature and bound to ${LEAK_POLICY}." >&2
    failures=$((failures + 1))
  fi
fi

if [[ "${failures}" -ne 0 ]]; then
  echo "== GC-022 lifecycle stress probe run FAILED (${failures} check(s)) ==" >&2
  exit 1
fi

echo "== GC-022 lifecycle stress probe run PASSED =="
echo "result     : ${result_file}"
echo "player log : ${log_file}"
echo "leak policy: ${LEAK_POLICY}"
echo "note: 'Pass' here means the player process reported it; the result JSON is the evidence to archive."

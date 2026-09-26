#!/usr/bin/env bash
# GC-023 replay player probe.
#
# Runs the already-built qualification player in its `-probeReplay` mode. The player itself is built by
# tools/unity/build_probe.sh, which compiles the replay package, the instrumented kernel and the validation project
# into the same IL2CPP binary; this script only launches it and validates the structured result, exactly like the
# other per-mode harnesses do.
#
# What the mode proves in the player (and what this script requires of the result):
#
#   * the recorded 10,000-step integer fixture hashes identically across the supported worker counts (1, 2, 4 and the
#     fixture maximum) and under a shuffled producer/completion order;
#   * a changed admitted order is reported as a different input trace rather than as a replay failure;
#   * engine-observation replay is identical over a recording while a native-physics comparison of two recordings
#     reports divergence — the two halves TEST-022 keeps apart;
#   * the differential propagation sweep is clean and its reducer produces a re-runnable counterexample for an
#     injected divergence;
#   * one real owned world reports its own counters through the fixed compact schema: an idle command-driven world
#     advances zero steps and does zero control work, a registered wake commits exactly one step with duration
#     samples, and the memory split is four separate numbers with nothing quarantined or retained;
#   * the raw benchmark trace is written beside the probe result and reads back.
#
# Required environment:
#   python3 (strict JSON validation)
#
# Optional environment:
#   PROBE_PLAYER   path to the built probe executable
#                  (default: <repo>/unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64)
#   PROBE_RUNS     how many times each probe runs (default 5; must be a positive integer)
#   UNITY_PROJECT  Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS      artifact directory (default: <repo>/artifacts/gc-023/toolchain)
#   RECORD         recorded digest file (default: <repo>/tests/GameCore.Replay/Data/replay-record.json)
#
# Exit codes: 0 the probe reported Pass with exit code 0 on every run; nonzero on any mismatch or crash.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
# shellcheck source=probe_runs.sh
source "${SCRIPT_DIR}/probe_runs.sh"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/gc-023/toolchain}"
PROBE_PLAYER="${PROBE_PLAYER:-${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64}"
RECORD="${RECORD:-${REPO_ROOT}/tests/GameCore.Replay/Data/replay-record.json}"

if [[ ! -x "${PROBE_PLAYER}" ]]; then
  echo "run_replay_probe.sh: probe player not found or not executable: ${PROBE_PLAYER}" >&2
  echo "  build it first with tools/unity/build_probe.sh." >&2
  exit 2
fi

mkdir -p "${ARTIFACTS}"
failures=0
result_file="${ARTIFACTS}/probe-replay.json"
log_file="${ARTIFACTS}/player-replay.log"
PROBE_LABEL="replay"

echo "-- running GC-023 replay probe (${PROBE_RUNS} run(s))"
probe_run_n "replay" "${result_file}" "${log_file}" 0 "Pass" "-probeReplay" || failures=$((failures + 1))

if [[ ! -f "${result_file}" ]]; then
  echo "run_replay_probe.sh: the probe wrote no result file: ${result_file}" >&2
  failures=$((failures + 1))
fi

if ! python3 -m json.tool "${result_file}" >/dev/null; then
  echo "run_replay_probe.sh: the probe result is not valid JSON: ${result_file}" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"task": "GC-023"' "${result_file}"; then
  echo "run_replay_probe.sh: the probe result does not declare task GC-023" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"mode": "Replay"' "${result_file}"; then
  echo "run_replay_probe.sh: the probe result does not declare mode Replay" >&2
  failures=$((failures + 1))
fi

# Every named observation of the scenario, in the scenario's own order, plus the two report-shape steps.
replay_steps=(
  '"name": "replay-world-created-and-owners-sampled"'
  '"name": "replay-world-idle-pump-does-zero-control-work"'
  '"name": "replay-world-memory-split-is-reported-separately"'
  '"name": "replay-world-wake-commits-one-step-and-samples-durations"'
  '"name": "replay-world-counters-move-on-a-committed-step"'
  '"name": "replay-trace-records-ten-thousand-steps"'
  '"name": "replay-hashes-identically-across-worker-counts"'
  '"name": "replay-hashes-identically-under-shuffled-producers"'
  '"name": "replay-different-admission-is-a-different-input"'
  '"name": "replay-observation-replay-is-separate-from-physics"'
  '"name": "replay-differential-sweep-is-clean-and-reducible"'
  '"name": "replay-raw-benchmark-trace-round-trips"'
  '"name": "replay-jobs-world-runs-real-parallel-producers"'
  '"name": "replay-jobs-hashes-identical-across-worker-counts"'
  '"name": "replay-jobs-producers-execute-on-multiple-threads"'
  '"name": "replay-digest"'
  '"name": "replay-observation-count"'
  '"name": "replay-benchmark-trace"'
)
probe_require_steps "${result_file}" "${replay_steps[@]}"

# The clauses that make the pass flags mean something: the modeled worker sweep really covered every supported count,
# the real-Unity-jobs half really ran Burst parallel producers at every count with the merge reordering rows, the
# idle world really did zero control work, and the memory split really reported four separate numbers.
for clause in \
  "workers1=equal" \
  "workers2=equal" \
  "workers4=equal" \
  "workers8=equal" \
  "controlNodes=0" \
  "serviceStringLookups=0" \
  "leaseBytes=" \
  "retainedEventBytes=" \
  "cacheBytes=" \
  "quarantineBytes=" \
  "jobWaitSamples=" \
  "steps=10000" \
  "observations=15" \
  "realBurstProducerJob=" \
  "innerLoopBatchCount=1" \
  "canonicalMergeReorderedRows=true" \
  "effectiveWorkerCountsReadBack=true" \
  "reordered=" \
  "recordedThreadHistograms=" \
  "multiThreaded=true" ; do
  if ! grep -q "${clause}" "${result_file}"; then
    echo "   FAIL replay: required clause '${clause}' is absent from ${result_file}" >&2
    failures=$((failures + 1))
  fi
done

# The raw benchmark trace artifacts must exist and read back, and the digest must be the recorded literal once the
# build host has recorded one. Until then the record is empty and the comparison is skipped with a printed note: an
# unrecorded digest must never pass as a recorded one.
if [[ ! -s "${result_file}.trace" ]]; then
  echo "run_replay_probe.sh: the raw benchmark trace was not written: ${result_file}.trace" >&2
  failures=$((failures + 1))
fi

if [[ ! -s "${result_file}.trace.json" ]]; then
  echo "run_replay_probe.sh: the JSON benchmark trace was not written: ${result_file}.trace.json" >&2
  failures=$((failures + 1))
fi

recorded_digest="$(python3 - "${RECORD}" <<'PY'
import json, sys
try:
    with open(sys.argv[1], encoding="utf-8") as handle:
        print(json.load(handle).get("observationDigest", ""))
except (OSError, ValueError):
    print("")
PY
)"
observed_digest="$(python3 - "${result_file}" <<'PY'
import json, re, sys
try:
    with open(sys.argv[1], encoding="utf-8") as handle:
        document = json.load(handle)
except (OSError, ValueError):
    print("")
    sys.exit(0)
for probe in document.get("probes", []):
    if probe.get("name") == "replay-digest":
        match = re.search(r"digest=([0-9a-f]{64})", probe.get("detail", ""))
        if match:
            print(match.group(1))
            break
PY
)"

if [[ -z "${observed_digest}" ]]; then
  echo "run_replay_probe.sh: the probe reported no observation digest" >&2
  failures=$((failures + 1))
elif [[ -z "${recorded_digest}" ]]; then
  echo "-- replay digest ${observed_digest} is not recorded yet in ${RECORD}; record it from this run (see the file's _comment)"
elif [[ "${recorded_digest}" != "${observed_digest}" ]]; then
  echo "run_replay_probe.sh: the observed digest ${observed_digest} does not match the recorded ${recorded_digest}" >&2
  failures=$((failures + 1))
else
  echo "-- replay digest matches the recorded value ${recorded_digest}"
fi

if (( failures != 0 )); then
  echo "run_replay_probe.sh: ${failures} check(s) failed" >&2
  exit 1
fi

echo "run_replay_probe.sh: all checks passed"

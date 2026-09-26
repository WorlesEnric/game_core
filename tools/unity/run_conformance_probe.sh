#!/usr/bin/env bash
# GC-024 reference-conformance player probe.
#
# Runs the already-built qualification player in its conformance mode. The player is built by
# tools/unity/build_probe.sh, which compiles the kernel, all three gameplay families and the reference-conformance
# fixture assembly into the same IL2CPP binary; this script only launches it and validates the structured result,
# exactly as the Wave 6 gate's harness does for its mode.
#
# The mode executes every before/after table of `docs/game-core/07-reference-compositions.md` in real Unity worlds —
# the card market (s2.4), the chapter quest (s3.3), the traversal challenge (s4.3), and the combined
# narrative+cards world of s5 with the durable reward path — and writes one normalized trace per table beside the
# probe result. It is the IL2CPP half of GC-024: the same runner the EditMode assembly
# `GameCore.Conformance.Tests` calls is executed here in a stripped, High-stripping player, so a conformance that only
# holds in the Editor cannot pass.
#
# WHAT IS COMPARED, AND WHAT DELIBERATELY IS NOT
#
#   * the per-table trace digests are asserted across runs (run 1's digest must equal every later run's), which is the
#     falsifiable claim this fixture can make before its first execution: a digest is computed over the observation
#     names and their pass flags and cannot be known in advance. When GC024_TRACE_DIGEST_<TABLE> is supplied, an exact
#     literal pin is asserted too, which is how a promoted baseline gets frozen;
#   * the committed trace files are diffed against the run's own trace files, so a committed artifact that no longer
#     matches the player is a failure rather than stale evidence;
#   * the genre-audit step must name the half it computed. A player has no project tree, so it reports
#     `tree=no project tree`; claiming a clean build-time audit from a player would be evidence it does not have.
#
# Required environment:
#   python3 (strict JSON validation)
#
# Optional environment:
#   PROBE_PLAYER              path to the built probe executable
#                             (default: <UNITY_PROJECT>/Builds/Linux64/GameCoreProbe.x86_64)
#   PROBE_RUNS                how many times the probe runs (default 5; must be a positive integer)
#   UNITY_PROJECT             Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS                 artifact directory (default: <repo>/artifacts/gc-024/toolchain)
#   TRACE_ARTIFACTS           directory holding the committed traces to diff against
#                             (default: <repo>/artifacts/gc-024/traces)
#   GC024_TRACE_DIGEST_CARDS       optional exact digest literal pins, one per table
#   GC024_TRACE_DIGEST_NARRATIVE
#   GC024_TRACE_DIGEST_TRAVERSAL
#   GC024_TRACE_DIGEST_CROSS
#
# Exit codes: 0 the conformance probe reported Pass with exit code 0 on every run and every trace matched; 1 on any
# mismatch, crash or digest disagreement; 2 on a missing prerequisite or invalid configuration.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
# shellcheck source=probe_runs.sh
source "${SCRIPT_DIR}/probe_runs.sh"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/gc-024/toolchain}"
TRACE_ARTIFACTS="${TRACE_ARTIFACTS:-${REPO_ROOT}/artifacts/gc-024/traces}"
PROBE_PLAYER="${PROBE_PLAYER:-${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64}"
GC024_TRACE_DIGEST_CARDS="${GC024_TRACE_DIGEST_CARDS:-}"
GC024_TRACE_DIGEST_NARRATIVE="${GC024_TRACE_DIGEST_NARRATIVE:-}"
GC024_TRACE_DIGEST_TRAVERSAL="${GC024_TRACE_DIGEST_TRAVERSAL:-}"
GC024_TRACE_DIGEST_CROSS="${GC024_TRACE_DIGEST_CROSS:-}"

if [[ ! -x "${PROBE_PLAYER}" ]]; then
  echo "run_conformance_probe.sh: probe player not found or not executable: ${PROBE_PLAYER}" >&2
  echo "  build it first with tools/unity/build_probe.sh." >&2
  exit 2
fi

mkdir -p "${ARTIFACTS}" "${ARTIFACTS}/traces"
failures=0
result_file="${ARTIFACTS}/probe-conformance.json"
log_file="${ARTIFACTS}/player-conformance.log"

# The probe writes its traces under the directory named by GC024_ARTIFACT_DIR, so they land beside the result file
# rather than wherever the player happens to run from.
export GC024_ARTIFACT_DIR="${ARTIFACTS}"

echo "-- running GC-024 conformance probe (${PROBE_RUNS} run(s))"
probe_run_n "conformance" "${result_file}" "${log_file}" 0 "Pass" "-probeConformance" || failures=$((failures + 1))

if [[ ! -f "${result_file}" ]]; then
  echo "run_conformance_probe.sh: the probe wrote no result file: ${result_file}" >&2
  exit 1
fi

if ! python3 -m json.tool "${result_file}" >/dev/null 2>&1; then
  echo "run_conformance_probe.sh: the probe result is not valid JSON: ${result_file}" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"task": "GC-024"' "${result_file}"; then
  echo "   FAIL conformance: the result does not name task GC-024" >&2
  failures=$((failures + 1))
fi
if ! grep -q '"mode": "Conformance"' "${result_file}"; then
  echo "   FAIL conformance: the result does not name mode Conformance" >&2
  failures=$((failures + 1))
fi
if ! grep -q '"result": "Pass"' "${result_file}"; then
  echo "   FAIL conformance: the result is not Pass" >&2
  failures=$((failures + 1))
fi
if grep -q '"status": "Fail"' "${result_file}"; then
  echo "   FAIL conformance: at least one recorded step failed" >&2
  failures=$((failures + 1))
fi

# The structural steps: the revision, the coverage count, the genre audit and one verdict per table.
PROBE_LABEL="conformance"
conformance_steps=(
  '"name": "conformance/revision"'
  '"name": "conformance/coverage"'
  '"name": "conformance/genre-audit"'
  '"name": "conformance/cards/verdict"'
  '"name": "conformance/narrative/verdict"'
  '"name": "conformance/traversal/verdict"'
  '"name": "conformance/cross/verdict"'
  '"name": "conformance/cards/trace-digest"'
  '"name": "conformance/narrative/trace-digest"'
  '"name": "conformance/traversal/trace-digest"'
  '"name": "conformance/cross/trace-digest"'
  '"name": "conformance/cards/trace-write"'
  '"name": "conformance/narrative/trace-write"'
  '"name": "conformance/traversal/trace-write"'
  '"name": "conformance/cross/trace-write"'
  '"name": "conformance/cross/no-action-surface-in-card-or-narrative"'
)
probe_require_steps "${result_file}" "${conformance_steps[@]}"

# Every 07 row of every table, by the name the runner records for it: a row transcribed but never executed cannot be
# claimed as observed, so its absence is a failure here as well as in the oracle (P-026).
row_steps=(
  # card market (07 s2.4 plus the section's prose rows at 07:51, 07:108, 07:110)
  '"name": "conformance/cards/mount-festival"'
  '"name": "conformance/cards/spawn-seat-d"'
  '"name": "conformance/cards/reconfigure-festival"'
  '"name": "conformance/cards/nested-retraction"'
  '"name": "conformance/cards/unmount-festival"'
  '"name": "conformance/cards/reparent-seat-a"'
  '"name": "conformance/cards/mode-conservative"'
  '"name": "conformance/cards/mode-automatic"'
  '"name": "conformance/cards/exclusive-conflict-rejected"'
  '"name": "conformance/cards/exclude-seat-b"'
  '"name": "conformance/cards/suspend-festival"'
  '"name": "conformance/cards/resume-festival"'
  # chapter quest (07 s3.3)
  '"name": "conformance/narrative/mount-chapter"'
  '"name": "conformance/narrative/spawn-villager"'
  '"name": "conformance/narrative/unmount-chapter"'
  '"name": "conformance/narrative/reparent-village"'
  '"name": "conformance/narrative/mode-conservative"'
  '"name": "conformance/narrative/mode-automatic"'
  '"name": "conformance/narrative/exclude-mara"'
  '"name": "conformance/narrative/suspend-chapter"'
  '"name": "conformance/narrative/resume-chapter"'
  # traversal challenge (07 s4.3 plus 07:247)
  '"name": "conformance/traversal/mount-tailwind"'
  '"name": "conformance/traversal/spawn-runner-c"'
  '"name": "conformance/traversal/unmount-tailwind"'
  '"name": "conformance/traversal/reparent-runner-subtree"'
  '"name": "conformance/traversal/mode-conservative"'
  '"name": "conformance/traversal/mode-automatic"'
  '"name": "conformance/traversal/exclude-runner-a"'
  '"name": "conformance/traversal/suspend-tailwind"'
  '"name": "conformance/traversal/resume-tailwind"'
  # the cross-family combination (07 s5)
  '"name": "conformance/cross/reward-enqueue"'
  '"name": "conformance/cross/reward-settle"'
  '"name": "conformance/cross/reward-redelivery"'
  '"name": "conformance/cross/reward-unmount-pending"'
  '"name": "conformance/cross/reward-drain-then-unmount"'
  '"name": "conformance/cross/reward-unmount-transfer"'
  '"name": "conformance/cross/reward-scoring-unmount-keeps-card"'
)
probe_require_steps "${result_file}" "${row_steps[@]}"

# The claims this task exists for, as clauses of the recorded details.
conformance_clauses=(
  "task=GC-024"
  "tables=4"
  "allPassed=True"
  # the cross-family combination's whole point: one durable obligation, one mutation, one no-op redelivery
  "outbox.recognised=0->1"
  "outbox.mutations=0->1"
  "outbox.mutations=1->1"
  # the genre audit names the half it computed, and a player has no project tree
  "loaded=kernel="
)
for clause in "${conformance_clauses[@]}"; do
  if ! grep -q -- "${clause}" "${result_file}"; then
    echo "   FAIL conformance: the result does not report ${clause}" >&2
    failures=$((failures + 1))
  fi
done

# 07:276's claims are carried by four real rows now, so a recorded gap would be a false statement about this
# revision: the run must report none, and the four rows above must all have passed.
if grep -q '"name": "conformance/cross/recorded-gaps"' "${result_file}"; then
  echo "   FAIL conformance: the run reports a recorded gap although 07:276's claims are carried by real rows" >&2
  failures=$((failures + 1))
fi
if grep -q "recordedGaps=[1-9]" "${result_file}"; then
  echo "   FAIL conformance: a table result reports one or more recorded gaps" >&2
  failures=$((failures + 1))
fi

# A player cannot read a project tree, so it must SAY so rather than report a clean build-time audit it never ran.
if ! grep -q "tree=no project tree on this host" "${result_file}" \
  && ! grep -q "tree=kernel=" "${result_file}"; then
  echo "   FAIL conformance: the genre-audit step names neither a computed tree audit nor its absence" >&2
  failures=$((failures + 1))
fi

# The per-table trace digests, and their agreement across runs. The digest cannot be pinned before the first run, so
# run 1's digest is the reference: every later run must reproduce it, and an exact literal is additionally asserted
# when the environment supplies one (the path a promoted baseline takes).
for table in cards narrative traversal cross; do
  variable="GC024_TRACE_DIGEST_$(echo "${table}" | tr '[:lower:]' '[:upper:]')"
  expected="${!variable}"
  digest_line="${ARTIFACTS}/trace-digest-${table}.txt"
  grep -o "conformance/${table}/trace-digest[^\"]*digest=[0-9a-f]*" "${result_file}" \
    | grep -o "digest=[0-9a-f]*" | head -n 1 | sed 's/^digest=//' > "${digest_line}" || true
  if [[ ! -s "${digest_line}" ]]; then
    echo "   FAIL conformance: no trace digest was recorded for ${table}" >&2
    failures=$((failures + 1))
    continue
  fi

  if [[ -n "${expected}" ]] && [[ "$(cat "${digest_line}")" != "${expected}" ]]; then
    echo "   FAIL conformance: ${table} digest $(cat "${digest_line}") != pinned ${expected}" >&2
    failures=$((failures + 1))
  fi

  for (( run = 2; run <= PROBE_RUNS; run++ )); do
    later="${result_file}.run${run}.${table}.digest"
    if [[ -f "${result_file}.run${run}" ]]; then
      grep -o "conformance/${table}/trace-digest[^\"]*digest=[0-9a-f]*" "${result_file}.run${run}" \
        | grep -o "digest=[0-9a-f]*" | head -n 1 | sed 's/^digest=//' > "${later}" || true
      if [[ -s "${later}" ]] && [[ "$(cat "${later}")" != "$(cat "${digest_line}")" ]]; then
        echo "   FAIL conformance: ${table} digest differs between run 1 and run ${run}" >&2
        echo "     run1=$(cat "${digest_line}") run${run}=$(cat "${later}")" >&2
        failures=$((failures + 1))
      fi
    fi
  done
done

# The normalized traces: the run's own bytes, and a diff against the committed trace when one exists. A committed
# trace that no longer matches the player is stale evidence, so it fails rather than being silently replaced.
for table in cards narrative traversal cross; do
  produced="${ARTIFACTS}/traces/${table}.txt"
  if [[ ! -s "${produced}" ]]; then
    echo "   FAIL conformance: the probe wrote no normalized trace for ${table} (${produced})" >&2
    failures=$((failures + 1))
    continue
  fi
  if ! grep -q "^format=gamecore.reference-conformance-trace/1$" "${produced}"; then
    echo "   FAIL conformance: ${produced} does not declare the trace format" >&2
    failures=$((failures + 1))
  fi
  if ! grep -q "^digest=[0-9a-f]\{64\}$" "${produced}"; then
    echo "   FAIL conformance: ${produced} carries no digest line" >&2
    failures=$((failures + 1))
  fi

  committed="${TRACE_ARTIFACTS}/${table}.txt"
  if [[ -f "${committed}" ]]; then
    if ! diff -u "${committed}" "${produced}" > "${ARTIFACTS}/trace-diff-${table}.txt"; then
      echo "   FAIL conformance: ${produced} differs from the committed ${committed} (see trace-diff-${table}.txt)" >&2
      failures=$((failures + 1))
    fi
  else
    echo "-- note: no committed trace at ${committed}; the produced trace is the record for this run"
  fi
done

if [[ "${failures}" -ne 0 ]]; then
  echo "== GC-024 conformance probe run FAILED (${failures} check(s)) ==" >&2
  exit 1
fi

echo "== GC-024 conformance probe run PASSED =="
echo "result     : ${result_file}"
echo "player log : ${log_file}"
echo "traces     : ${ARTIFACTS}/traces"
echo "note: 'Pass' here means the player process reported it in the result JSON, which is the evidence to archive."

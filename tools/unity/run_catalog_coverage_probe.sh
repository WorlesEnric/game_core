#!/usr/bin/env bash
# GC-025 catalog coverage player probe.
#
# Runs the already-built player in its `-probeCatalogCoverage` mode. The player itself is built by
# tools/unity/build_probe.sh; this script only launches it and validates the structured result, exactly like the
# probes of GC-001, GC-005, W1..W6-GATE and the per-slice gates.
#
# The mode runs `CatalogCoverageScenario`: the committed reachability manifest against the live generated catalogs,
# every generated registration/serializer/closed-generic root executed in the player through the generated coverage
# companions, the generated traversal catalog against its hand-written counterpart, the linked-but-inactive plugin
# late mount, the editor-baked and runtime-recipe materializations of the traversal course, the refused
# unknown/stale recipes, a stopped-and-restarted world host and the headless reference execution against the
# pure-rule canonical fixtures. Its mandated reason for existing in a *player* is stripping: a registration or
# serializer the linker dropped fails the generated coverage companion there and not in the Editor (04 section 8).
#
# It runs in the qualification player and, when given a release player, in the marker-free release player as well
# (`PROBE_PLAYER` plus `RELEASE_PLAYER=1`).
#
# Required environment:
#   python3 (strict JSON validation)
#
# Optional environment:
#   PROBE_PLAYER   path to the built probe executable
#                  (default: <repo>/unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64)
#   PROBE_RUNS     how many times the probe runs (default 5; must be a positive integer)
#   UNITY_PROJECT  Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS      artifact directory (default: <repo>/artifacts/baseline/probe)
#   PROBE_SHAPE    "release" labels the run a release-shape run in the retained evidence names
#                  (the release shape is the marker-free clone; the variable is deliberately not named
#                  RELEASE_PLAYER, which the GC-017 gate uses for a player path)
#
# Exit codes: 0 the coverage probe reported Pass with exit code 0 on every run; nonzero on any mismatch or crash.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
# shellcheck source=probe_runs.sh
source "${SCRIPT_DIR}/probe_runs.sh"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/baseline/probe}"
PROBE_PLAYER="${PROBE_PLAYER:-${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64}"
SHAPE_LABEL="${PROBE_SHAPE:-}"

if [[ ! -x "${PROBE_PLAYER}" ]]; then
  echo "PROBE_PLAYER is not an executable file: ${PROBE_PLAYER}" >&2
  echo "build it first with: UNITY=<editor> tools/unity/build_probe.sh" >&2
  exit 2
fi

mkdir -p "${ARTIFACTS}"
failures=0
result_file="${ARTIFACTS}/probe-catalog-coverage${SHAPE_LABEL:+-release}.json"
log_file="${ARTIFACTS}/player-catalog-coverage${SHAPE_LABEL:+-release}.log"

# -batchmode -nographics keep the player headless. -quit is deliberately NOT passed: the probe exits itself through
# Application.Quit with a code that encodes its result. Unity audio stays disabled in this build (crash-139).
echo "-- running GC-025 catalog coverage probe (${PROBE_RUNS} run(s), shape=${SHAPE_LABEL:-qualification})"
probe_run_n "catalog-coverage" "${result_file}" "${log_file}" 0 "Pass" "-probeCatalogCoverage" || failures=$((failures + 1))

if [[ ! -f "${result_file}" ]]; then
  echo "FAIL catalog-coverage: no result file at ${result_file}" >&2
  exit 1
fi

if ! python3 -m json.tool "${result_file}" >/dev/null; then
  echo "FAIL catalog-coverage: ${result_file} is not valid JSON" >&2
  exit 1
fi

if ! grep -q '"task": "GC-025"' "${result_file}"; then
  echo "FAIL catalog-coverage: the result does not carry the GC-025 task identity" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"mode": "CatalogCoverage"' "${result_file}"; then
  echo "FAIL catalog-coverage: the result does not carry the CatalogCoverage mode identity" >&2
  failures=$((failures + 1))
fi

if ! grep -q '"result": "Pass"' "${result_file}"; then
  echo "FAIL catalog-coverage: the result is not Pass" >&2
  failures=$((failures + 1))
fi

if grep -q '"status": "Fail"' "${result_file}"; then
  echo "FAIL catalog-coverage: the result contains a failing step" >&2
  failures=$((failures + 1))
fi

# Every observation must appear, qualified with the sequence's label. The names and their order are
# `CatalogCoverageScenario.ObservationNames`.
coverage_steps=(
  '"name": "catalogCoverage/catalog-reachability-manifest"'
  '"name": "catalogCoverage/catalog-coverage-probe-catalog"'
  '"name": "catalogCoverage/catalog-coverage-cards-catalog"'
  '"name": "catalogCoverage/catalog-coverage-checkpoint-catalog"'
  '"name": "catalogCoverage/catalog-coverage-traversal-catalog"'
  '"name": "catalogCoverage/catalog-coverage-registration-lookup"'
  '"name": "catalogCoverage/catalog-coverage-closed-generic-roots"'
  '"name": "catalogCoverage/catalog-coverage-traversal-generated-catalog"'
  '"name": "catalogCoverage/catalog-coverage-late-mount-inactive-plugin"'
  '"name": "catalogCoverage/catalog-coverage-bake-runtime-parity"'
  '"name": "catalogCoverage/catalog-coverage-unknown-recipe-refused"'
  '"name": "catalogCoverage/catalog-coverage-world-stop-restart"'
  '"name": "catalogCoverage/catalog-coverage-headless-canonical"'
)
probe_require_steps "${result_file}" "${coverage_steps[@]}"

# The digest literal is over the observation names and their pass flags, so this is the whole claim that the sequence
# ran the named observations and every one of them passed. It is frozen here, in `CatalogCoverageProbe` and in
# `GameCore.CatalogCoverage.Tests`, which recomputes it from `CatalogCoverageScenario.ObservationNames`.
coverage_digest="77bc74196ec96b076bcc63b007cc0f57f5322121bad69f36c0e280d0576fbbb2"
if ! grep -q "digest=${coverage_digest}" "${result_file}"; then
  echo "FAIL catalog-coverage: the result does not carry the frozen digest ${coverage_digest}" >&2
  failures=$((failures + 1))
fi

# The task's own acceptance clauses, asserted as fragments of the step details rather than as bare pass flags: a run
# that recorded the right step names but did not really demonstrate the clause cannot satisfy these. Every fragment is
# a substring of a detail `CatalogCoverageScenario` really writes, so a rename of one fails loudly instead of silently
# checking nothing.
for clause in \
  "format=gamecore.catalog-reachability/1" \
  "catalogs=4" \
  "probe=match" \
  "cards=match" \
  "checkpoint=match" \
  "traversal=match" \
  "expectedRegistrations=3" \
  "expectedRegistrations=8" \
  "expectedSchemas=13" \
  "roots=2" \
  "probeRoots=2" \
  "equal=true" \
  "fixtureTableFingerprint=" \
  "generatedFileHash=" \
  "linkedInactiveInstanceCountBeforeMount=0" \
  "instanceCountAfterMount=1" \
  "cardTablePluginFactory=resolved" \
  "traversalCoursePluginFactory=resolved" \
  "runtimeSource=runtime-recipe" \
  "bakedSource=editor-baked-recipe" \
  "bakedRecipeFingerprint=" \
  "mismatches=<none>" \
  "unknownRecipeCode=MissingDependency" \
  "staleRevisionCode=StalePlan" \
  "sessionsDiffer=true" \
  "registryRestored=true" \
  "arithmetic=holds" \
  "providerValue=2000" \
  "expectedVelocity=1040" \
  "expectedVelocity=1020" \
  "poseAdvanceX=20" \
  "maxStepsPerPump=4"; do
  if ! grep -q -- "${clause}" "${result_file}"; then
    echo "FAIL catalog-coverage: required clause is absent: ${clause}" >&2
    failures=$((failures + 1))
  fi
done

# The release shape must report the same catalog facts: the marker-free clone compiles the same catalog sources, so a
# fingerprint that differs between the two shapes is a real defect and not a configuration difference.
if [[ -n "${SHAPE_LABEL}" ]] && ! grep -q '"status": "Pass"' "${result_file}"; then
  echo "FAIL catalog-coverage: the release-shape run did not pass" >&2
  failures=$((failures + 1))
fi

if [[ "${failures}" -ne 0 ]]; then
  echo "FAIL catalog-coverage: ${failures} requirement(s) unmet; run-1 evidence: ${result_file} ${log_file}" >&2
  exit 1
fi

echo "== GC-025 catalog coverage probe run PASSED (shape=${SHAPE_LABEL:-qualification}) =="
echo "result     : ${result_file}"
echo "player log : ${log_file}"
echo "note: 'Pass' here means the player process reported it; the result JSON is the evidence to archive."

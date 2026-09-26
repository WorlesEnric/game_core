#!/usr/bin/env bash
# GC-025 gate: the complete standalone IL2CPP and headless profile.
#
# GC-025's sentence is: "Build the complete selected target with locked dependencies, Burst and High managed
# stripping. Exercise every generated factory/serializer/closed generic root, bake/runtime recipe parity, inactive
# plugin late mount, stop/restart and headless reference execution. Compare generated registration fingerprints
# across builds." Its acceptance adds: "Every mandatory catalog entry executes in the player, including code absent
# from startup scenes. Headless actual Entities behavior matches the applicable pure-rule/canonical fixtures.
# Unsupported target platforms remain explicitly unqualified."
#
# Runs, in order:
#   1. the host-side checks that need no Editor or SDK: the generated-catalog verifier for all four catalogs, the
#      emitter mirror's byte-exact self-check, the reachability manifest and baked-artifact freshness checks, the
#      fingerprint tool's own falsifiability self-test, the link.xml preservation rule for both projects, the
#      gate-source invariants of this change set, and the `bash -n` parse of this gate's scripts;
#   2. the plain-dotnet solution: build and test (the pure half of the repository, unchanged by GC-025 but the
#      same revision must still pass it);
#   3. Unity package resolution on the qualification project;
#   4. the Unity EditMode suite of every testable package plus `GameCore.CatalogCoverage.Tests` — the Unity-world
#      half of this task;
#   5. the Unity PlayMode suite;
#   6. build-time code generation for all four catalogs and the coverage bake, then `git diff --exit-code`: the
#      committed artifacts must be exactly what the pinned Editor produces from their descriptions;
#   7. the StandaloneLinux64 IL2CPP qualification player;
#   8. `-probeCatalogCoverage` in the qualification player, PROBE_RUNS times;
#   9. the marker-free release shape: prepare the clone, check the clone surface, build it, then run
#      `-probeCatalogCoverage` there as well and compare the two shapes' registration fingerprints;
#  10. the documentation validator.
#
# Timeouts. The Unity Editor has an unresolved intermittent hang before it dispatches a batchmode command
# (artifacts/gc-014/BUILD_REPORT.md), so EVERY Unity Editor invocation is wrapped in `timeout` and retried exactly
# ONCE on a timeout, logging the retry. Player runs are never retried here: `probe_runs.sh` already repeats them
# PROBE_RUNS times and any dirty run fails.
#
# Required environment:
#   UNITY   absolute path to the Unity Editor executable of the pinned 6000.0.75f1 baseline, e.g.
#           ~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
#
# Optional environment:
#   DOTNET            .NET 8 SDK executable (default: dotnet on PATH)
#   PYTHON            python3 executable (default: python3 on PATH)
#   UNITY_PROJECT     qualification project path (default: <repo>/unity/GameCore.Validation)
#   RELEASE_PROJECT   release clone project path (default: <repo>/unity/GameCore.ReleaseCheck)
#   ARTIFACTS         artifact directory (default: <repo>/artifacts/gc-025)
#   PROBE_RUNS        repetitions of the coverage probe (default 5)
#   UNITY_TIMEOUT     seconds a full Unity Editor invocation may take (default 1800)
#   RELEASE_BUILD     set to 0 to reuse an existing release clone instead of recreating it
#   DOCS              run step 10 (default 1, 0 skips and says so)
#
# Exit codes: 0 every step passed; nonzero on the first failing step (2 for a missing prerequisite).
set -euo pipefail

if [[ -z "${UNITY:-}" ]]; then
  echo "run_gc025_gate.sh: set UNITY to the Unity Editor executable of the pinned 6000.0.75f1 baseline" >&2
  exit 2
fi
if [[ ! -x "${UNITY}" ]]; then
  echo "run_gc025_gate.sh: UNITY is not executable: ${UNITY}" >&2
  exit 2
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
PYTHON="${PYTHON:-python3}"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
RELEASE_PROJECT="${RELEASE_PROJECT:-${REPO_ROOT}/unity/GameCore.ReleaseCheck}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/gc-025}"
PROBE_PLAYER="${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64"
PROBE_RUNS="${PROBE_RUNS:-5}"
UNITY_TIMEOUT="${UNITY_TIMEOUT:-1800}"
RELEASE_BUILD="${RELEASE_BUILD:-1}"
DOCS="${DOCS:-1}"

mkdir -p "${ARTIFACTS}/trx" "${ARTIFACTS}/unity" "${ARTIFACTS}/host" "${ARTIFACTS}/probe" "${ARTIFACTS}/release"

cd "${REPO_ROOT}"

echo "== GC-025 gate: standalone IL2CPP and headless profile =="
echo "repo         : ${REPO_ROOT}"
echo "unity        : ${UNITY}"
echo "dotnet       : ${DOTNET}"
echo "project      : ${UNITY_PROJECT}"
echo "release      : ${RELEASE_PROJECT}"
echo "artifacts    : ${ARTIFACTS}"
echo "probe runs   : ${PROBE_RUNS}"
echo "unity timeout: ${UNITY_TIMEOUT}s (one retry on a timeout)"

run_step() {
  local label="$1"
  shift
  echo "-- ${label}: $*"
  "$@"
}

# One Unity Editor invocation, wrapped in `timeout` and retried exactly once when it times out.
unity_step() {
  local label="$1"
  shift

  local attempt rc=0
  for attempt in 1 2; do
    if (( attempt == 2 )); then
      echo "-- ${label}: retrying once after a timeout" >&2
    fi

    rc=0
    timeout --signal=TERM --kill-after=60 "${UNITY_TIMEOUT}" "$@" || rc=$?
    if (( rc == 0 )); then
      return 0
    fi

    if (( rc == 124 || rc == 137 )); then
      echo "   FAIL ${label}: Unity Editor timed out after ${UNITY_TIMEOUT}s (exit ${rc}, attempt ${attempt}/2)" >&2
      continue
    fi

    echo "   FAIL ${label}: Unity Editor exited ${rc}" >&2
    return "${rc}"
  done

  echo "   FAIL ${label}: Unity Editor timed out twice; this is not the known intermittent pre-dispatch hang" >&2
  return 1
}

# --------------------------------------------------------------------------------------------------------------
# 1. Host-side checks (no Editor, no SDK).
# --------------------------------------------------------------------------------------------------------------

echo
echo "== 1. host-side checks =="

run_step generated-catalog-probe "${PYTHON}" tools/verify_generated_catalog.py \
  unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs
run_step generated-catalog-cards "${PYTHON}" tools/verify_generated_catalog.py \
  unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCards/CardCatalog.g.cs
run_step generated-catalog-checkpoint "${PYTHON}" tools/verify_generated_catalog.py \
  unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCheckpoint/CheckpointCatalog.g.cs
run_step generated-catalog-traversal "${PYTHON}" tools/verify_generated_catalog.py \
  unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedTraversal/TraversalCatalog.g.cs
run_step catalog-emitter-mirror "${PYTHON}" tools/emit_generated_catalog.py --self-check
run_step reachability-manifest "${PYTHON}" tools/emit_catalog_reachability.py --check
run_step baked-coverage-artifact "${PYTHON}" tools/emit_baked_catalog_coverage.py --check
run_step fingerprint-tool-self-test "${PYTHON}" tools/compare_registration_fingerprints.py --self-test
run_step link-xml-qualification "${PYTHON}" tools/check_link_xml.py \
  --project "${UNITY_PROJECT}" --json "${ARTIFACTS}/host/link-xml-qualification.json"
run_step csharp-checker "${PYTHON}" tools/check_game_core_csharp.py
run_step contract-surface-parity "${PYTHON}" tools/check_contract_surface_parity.py
run_step gate-sources "${PYTHON}" tools/check_gate_sources.py \
  --file unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/CatalogCoverageScenario.cs \
  --file unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/CatalogCoverageRecipeSource.cs \
  --file unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/CatalogCoverageProbe.cs \
  --file unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/CatalogReachability.g.cs \
  --file unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/Gc020TraversalHost.cs \
  --file unity/GameCore.Validation/Assets/GameCore.Validation/Editor/BakeCatalogCoverageAuthoring.cs \
  --file unity/GameCore.Validation/Assets/GameCore.Validation/Editor/TraversalCatalogGenerator.cs \
  --file unity/GameCore.Validation/Assets/GameCore.Validation/Tests/CatalogCoverage/CatalogCoverageIntegrationTests.cs \
  --json "${ARTIFACTS}/host/gate-sources.json"
run_step shell-parse bash -n tools/run_gc025_gate.sh tools/build_baseline_player.sh \
  tools/unity/run_catalog_coverage_probe.sh

# --------------------------------------------------------------------------------------------------------------
# 2. The pure half of the repository.
# --------------------------------------------------------------------------------------------------------------

echo
echo "== 2. plain dotnet =="

run_step dotnet-build "${DOTNET}" build dotnet/GameCore.sln -c Release
run_step dotnet-test "${DOTNET}" test dotnet/GameCore.sln -c Release --no-build \
  --logger "trx;LogFileName=gc025.trx" --results-directory "${ARTIFACTS}/trx"

# --------------------------------------------------------------------------------------------------------------
# 3-5. Unity resolve, EditMode and PlayMode.
# --------------------------------------------------------------------------------------------------------------

echo
echo "== 3. Unity resolve =="
unity_step unity-resolve "${UNITY}" -batchmode -nographics -quit -projectPath "${UNITY_PROJECT}" \
  -logFile "${ARTIFACTS}/unity/resolve.log"

echo
echo "== 4. Unity EditMode (this task's suite included) =="
# Do not add -quit to a -runTests command (04 section 10): the runner finishes asynchronously.
unity_step unity-editmode "${UNITY}" -batchmode -nographics -projectPath "${UNITY_PROJECT}" \
  -runTests -testPlatform EditMode \
  -testResults "${ARTIFACTS}/unity/editmode-results.xml" \
  -logFile "${ARTIFACTS}/unity/editmode.log"

echo
echo "== 5. Unity PlayMode =="
unity_step unity-playmode "${UNITY}" -batchmode -nographics -projectPath "${UNITY_PROJECT}" \
  -runTests -testPlatform PlayMode \
  -testResults "${ARTIFACTS}/unity/playmode-results.xml" \
  -logFile "${ARTIFACTS}/unity/playmode.log"

# --------------------------------------------------------------------------------------------------------------
# 6. Codegen reproducibility: the committed artifacts are the Editor's own output.
# --------------------------------------------------------------------------------------------------------------

echo
echo "== 6. code generation and reproducibility =="

unity_step codegen-probe "${UNITY}" -batchmode -nographics -quit -projectPath "${UNITY_PROJECT}" \
  -executeMethod GameCore.Validation.Editor.ProbeCatalogGenerator.GenerateCatalog \
  -logFile "${ARTIFACTS}/unity/codegen-probe.log"
unity_step codegen-cards "${UNITY}" -batchmode -nographics -quit -projectPath "${UNITY_PROJECT}" \
  -executeMethod GameCore.Validation.Editor.CardCatalogGenerator.GenerateCatalog \
  -logFile "${ARTIFACTS}/unity/codegen-cards.log"
unity_step codegen-checkpoint "${UNITY}" -batchmode -nographics -quit -projectPath "${UNITY_PROJECT}" \
  -executeMethod GameCore.Validation.Editor.CheckpointCatalogGenerator.GenerateCatalog \
  -logFile "${ARTIFACTS}/unity/codegen-checkpoint.log"
unity_step codegen-traversal "${UNITY}" -batchmode -nographics -quit -projectPath "${UNITY_PROJECT}" \
  -executeMethod GameCore.Validation.Editor.TraversalCatalogGenerator.GenerateCatalog \
  -logFile "${ARTIFACTS}/unity/codegen-traversal.log"
unity_step codegen-bake "${UNITY}" -batchmode -nographics -quit -projectPath "${UNITY_PROJECT}" \
  -executeMethod GameCore.Validation.Editor.BakeCatalogCoverageAuthoring.Bake \
  -logFile "${ARTIFACTS}/unity/codegen-bake.log"

run_step generated-catalog-byte-identity git diff --exit-code -- \
  unity/GameCore.Validation/Assets/GameCore.Validation/Generated \
  unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCards \
  unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCheckpoint \
  unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedTraversal

# --------------------------------------------------------------------------------------------------------------
# 7-8. Qualification player and the coverage probe.
# --------------------------------------------------------------------------------------------------------------

echo
echo "== 7. qualification player =="
UNITY="${UNITY}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" \
  UNITY_TIMEOUT="${UNITY_TIMEOUT}" DOTNET="${DOTNET}" tools/unity/build_probe.sh

echo
echo "== 8. -probeCatalogCoverage (qualification) =="
PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/probe" \
  PROBE_RUNS="${PROBE_RUNS}" tools/unity/run_catalog_coverage_probe.sh

run_step fingerprint-ledger-qualification "${PYTHON}" tools/compare_registration_fingerprints.py \
  --ledger "${UNITY_PROJECT}" --ledger-out "${ARTIFACTS}/qualification"

# --------------------------------------------------------------------------------------------------------------
# 9. The marker-free release shape.
# --------------------------------------------------------------------------------------------------------------

echo
echo "== 9. release shape =="
if [[ "${RELEASE_BUILD}" != "0" ]]; then
  if [[ -d "${RELEASE_PROJECT}" ]]; then
    echo "-- removing the disposable clone at ${RELEASE_PROJECT}"
    rm -rf "${RELEASE_PROJECT}"
  fi
  run_step release-project-prepare "${PYTHON}" tools/unity/prepare_gc017_release_project.py
fi

run_step release-clone-check "${PYTHON}" tools/check_release_clone.py \
  --clone "${RELEASE_PROJECT}" --json "${ARTIFACTS}/release/clone-surface.json"
run_step link-xml-release "${PYTHON}" tools/check_link_xml.py \
  --project "${RELEASE_PROJECT}" --json "${ARTIFACTS}/release/link-xml.json"

UNITY="${UNITY}" UNITY_PROJECT="${RELEASE_PROJECT}" ARTIFACTS="${ARTIFACTS}/release" \
  UNITY_TIMEOUT="${UNITY_TIMEOUT}" DOTNET="${DOTNET}" tools/unity/build_probe.sh

run_step fingerprint-ledger-release "${PYTHON}" tools/compare_registration_fingerprints.py \
  --ledger "${RELEASE_PROJECT}" --ledger-out "${ARTIFACTS}/release"

run_step fingerprint-comparison "${PYTHON}" tools/compare_registration_fingerprints.py \
  --a "${ARTIFACTS}/qualification" --label-a qualification \
  --b "${ARTIFACTS}/release" --label-b release \
  --json "${ARTIFACTS}/fingerprint-comparison.json"

RELEASE_PLAYER="${RELEASE_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64" \
UNITY_PROJECT="${RELEASE_PROJECT}" \
ARTIFACTS="${ARTIFACTS}/probe" \
PROBE_RUNS="${PROBE_RUNS}" \
PROBE_SHAPE=release \
  tools/unity/run_catalog_coverage_probe.sh

# --------------------------------------------------------------------------------------------------------------
# 10. Documentation.
# --------------------------------------------------------------------------------------------------------------

if [[ "${DOCS}" == "1" ]]; then
  echo
  echo "== 10. documentation validator =="
  run_step docs-validator-self-test "${PYTHON}" tools/validate_game_core_docs.py --self-test
  run_step docs-validator "${PYTHON}" tools/validate_game_core_docs.py
else
  echo
  echo "== 10. documentation validator: SKIPPED (DOCS=${DOCS}) =="
fi

echo
echo "== GC-025 gate finished =="
echo "qualification player : ${PROBE_PLAYER}"
echo "release player       : ${RELEASE_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64"
echo "coverage results     : ${ARTIFACTS}/probe/probe-catalog-coverage{,-release}.json"
echo "fingerprint verdict  : ${ARTIFACTS}/fingerprint-comparison.json"
echo "baseline record      : fill artifacts/baseline/ENVIRONMENT.md from ${ARTIFACTS}/toolchain/environment.txt"

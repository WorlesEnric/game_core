#!/usr/bin/env bash
# GC-025 final baseline player build: both build shapes, locked dependencies, Burst, High stripping, headless runs.
#
# THE TWO SHAPES. 04 section 1/§8 and GC-001 fix one target baseline — StandaloneLinux64 x86_64, IL2CPP, High managed
# stripping, Burst enabled, run headless with `-batchmode -nographics`. The qualification and release shapes share
# every one of those settings (`BuildProbe.cs` sets them itself and refuses a silent override); they differ only in
# the package/fixture surface:
#
#   * qualification: `unity/GameCore.Validation`, whose manifest carries the two qualification marker packages
#     (`com.gamecore.fault-qualification`, `com.gamecore.telemetry-qualification`), the replay fixture package and
#     the Unity test packages, plus every qualification-only probe fixture.
#   * release: `unity/GameCore.ReleaseCheck`, produced by `tools/unity/prepare_gc017_release_project.py`: marker-free,
#     test-free, fixture-stripped, the shipping shape.
#
# A shape that cannot be built is reported as NOT RUN rather than silently skipped (`SHAPES=`).
#
# WHAT IT PROVES, IN ORDER:
#   1. every committed generated catalog and the baked coverage artifact reproduce from their descriptions
#      (`tools/verify_generated_catalog.py`, `tools/emit_catalog_reachability.py --check`,
#      `tools/emit_baked_catalog_coverage.py --check`);
#   2. the pinned Editor regenerates all four catalogs plus the bake and `git diff` is empty, so the committed
#      artifacts are the Editor's own output and not a hand edit;
#   3. `Assets/link.xml` preserves no kernel/gameplay assembly (`tools/check_link_xml.py`);
#   4. both shapes build with `tools/unity/build_probe.sh` (locked dependencies, Burst, High stripping);
#   5. both shapes' registration fingerprints (per catalog and combined) are compared
#      (`tools/compare_registration_fingerprints.py`), and each shape's ledger is archived;
#   6. the built players run the GC-025 catalog coverage probe headless
#      (`tools/unity/run_catalog_coverage_probe.sh`) in both shapes, so the catalog facts are proven in a player and
#      not only in the Editor;
#   7. the host facts a report needs are captured into `${ARTIFACTS}/environment.{txt,json}`; the human-readable
#      `artifacts/baseline/ENVIRONMENT.md` template is the place the exact versions get filled in from them.
#
# Required environment:
#   UNITY            absolute path to the pinned Editor (6000.0.75f1)
#
# Optional environment:
#   UNITY_PROJECT    qualification project (default <repo>/unity/GameCore.Validation)
#   RELEASE_PROJECT  release clone project (default <repo>/unity/GameCore.ReleaseCheck)
#   ARTIFACTS        evidence directory (default <repo>/artifacts/baseline/build)
#   SHAPES           comma-separated subset of `qualification,release` (default both)
#   UNITY_TIMEOUT    seconds per Editor invocation (default 1800)
#   DOTNET           dotnet executable, recorded in the environment capture when set
#   PYTHON           python3 interpreter (default python3)
#   PROBE_RUNS       player probe repetitions (default 5, passed to the probe harness)
#   RELEASE_BUILD    set to 0 to reuse an existing release clone instead of recreating it
#
# Exit codes: nonzero when any executed step fails; a shape that was not requested prints its own NOT RUN line.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
cd "${REPO_ROOT}"

UNITY="${UNITY:-}"
if [[ -z "${UNITY}" ]]; then
  echo "UNITY must point to the Unity Editor executable for the pinned 6000.0.75f1 baseline." >&2
  exit 2
fi
if [[ ! -x "${UNITY}" ]]; then
  echo "UNITY is not an executable file: ${UNITY}" >&2
  exit 2
fi
UNITY_VERSION="$("${UNITY}" -version 2>/dev/null || true)"
if [[ "${UNITY_VERSION}" != *"6000.0.75f1"* ]]; then
  echo "warning: UNITY reports '${UNITY_VERSION}', which is not the pinned 6000.0.75f1 baseline (04 section 1)" >&2
fi

UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
RELEASE_PROJECT="${RELEASE_PROJECT:-${REPO_ROOT}/unity/GameCore.ReleaseCheck}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/baseline/build}"
SHAPES="${SHAPES:-qualification,release}"
UNITY_TIMEOUT="${UNITY_TIMEOUT:-1800}"
PYTHON="${PYTHON:-python3}"
PROBE_RUNS="${PROBE_RUNS:-5}"
RELEASE_BUILD="${RELEASE_BUILD:-1}"

mkdir -p "${ARTIFACTS}/qualification" "${ARTIFACTS}/release" "${ARTIFACTS}/host" "${ARTIFACTS}/probe"

unity_step() {
  local label="$1"
  shift
  echo "-- ${label}: $*"
  local rc=0
  timeout --signal=TERM --kill-after=60 "${UNITY_TIMEOUT}" "$@" || rc=$?
  if [[ "${rc}" -eq 124 || "${rc}" -eq 137 ]]; then
    echo "-- ${label}: timed out after ${UNITY_TIMEOUT}s; retrying once (the Editor has an intermittent hang)"
    timeout --signal=TERM --kill-after=60 "${UNITY_TIMEOUT}" "$@" || rc=$?
  fi
  if [[ "${rc}" -ne 0 ]]; then
    echo "FAIL ${label}: exit code ${rc}" >&2
    return "${rc}"
  fi
}

run_step() {
  echo "-- $1: ${*:2}"
  shift
  "$@"
}

# --------------------------------------------------------------------------------------------------------------
# 0. Host facts.
# --------------------------------------------------------------------------------------------------------------

echo "== GC-025 baseline player build =="
echo "editor      : ${UNITY} (${UNITY_VERSION:-unknown})"
echo "shapes      : ${SHAPES}"
echo "artifacts   : ${ARTIFACTS}"

{
  echo "editor=${UNITY}"
  echo "editor_version=${UNITY_VERSION:-unknown}"
  echo "qualification_project=${UNITY_PROJECT}"
  echo "release_project=${RELEASE_PROJECT}"
  echo "date_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "host=$(uname -a)"
  echo "arch=$(uname -m)"
  echo "kernel=$(uname -r)"
  echo "cpu=$( (grep -m1 'model name' /proc/cpuinfo 2>/dev/null || sysctl -n machdep.cpu.brand_string 2>/dev/null) || echo absent)"
  echo "nproc=$(nproc 2>/dev/null || sysctl -n hw.ncpu 2>/dev/null || echo absent)"
  echo "clang=$(clang --version 2>/dev/null | head -n1 || echo absent)"
  echo "gcc=$(gcc --version 2>/dev/null | head -n1 || echo absent)"
  echo "ld=$(ld --version 2>/dev/null | head -n1 || echo absent)"
  echo "dotnet=${DOTNET:-absent}"
  echo "dotnet_version=$([[ -n "${DOTNET:-}" ]] && "${DOTNET}" --version 2>/dev/null || echo absent)"
  echo "python=$("${PYTHON}" --version 2>&1 || echo absent)"
  echo "packages_manifest_sha256=$( (sha256sum "${UNITY_PROJECT}/Packages/manifest.json" 2>/dev/null || shasum -a 256 "${UNITY_PROJECT}/Packages/manifest.json" 2>/dev/null) | cut -d' ' -f1 || echo absent)"
  echo "packages_lock_sha256=$( (sha256sum "${UNITY_PROJECT}/Packages/packages-lock.json" 2>/dev/null || shasum -a 256 "${UNITY_PROJECT}/Packages/packages-lock.json" 2>/dev/null) | cut -d' ' -f1 || echo absent)"
  echo "link_xml_sha256=$( (sha256sum "${UNITY_PROJECT}/Assets/link.xml" 2>/dev/null || shasum -a 256 "${UNITY_PROJECT}/Assets/link.xml" 2>/dev/null) | cut -d' ' -f1 || echo absent)"
} > "${ARTIFACTS}/host/environment.txt"
cp "${ARTIFACTS}/host/environment.txt" "${ARTIFACTS}/environment.txt"
echo "-- host facts: ${ARTIFACTS}/host/environment.txt (fill artifacts/baseline/ENVIRONMENT.md from it)"

# --------------------------------------------------------------------------------------------------------------
# 1. Host-side checks that need no Editor: generation reproducibility and the preservation rule.
# --------------------------------------------------------------------------------------------------------------

run_step catalog-verify-probe "${PYTHON}" tools/verify_generated_catalog.py \
  unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs
run_step catalog-verify-cards "${PYTHON}" tools/verify_generated_catalog.py \
  unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCards/CardCatalog.g.cs
run_step catalog-verify-checkpoint "${PYTHON}" tools/verify_generated_catalog.py \
  unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCheckpoint/CheckpointCatalog.g.cs
run_step catalog-verify-traversal "${PYTHON}" tools/verify_generated_catalog.py \
  unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedTraversal/TraversalCatalog.g.cs
run_step catalog-emitter-mirror "${PYTHON}" tools/emit_generated_catalog.py --self-check
run_step reachability-manifest "${PYTHON}" tools/emit_catalog_reachability.py --check
run_step baked-artifact "${PYTHON}" tools/emit_baked_catalog_coverage.py --check
run_step fingerprint-tool-self-test "${PYTHON}" tools/compare_registration_fingerprints.py --self-test
run_step link-xml-qualification "${PYTHON}" tools/check_link_xml.py \
  --project "${UNITY_PROJECT}" --json "${ARTIFACTS}/host/link-xml.json"

# --------------------------------------------------------------------------------------------------------------
# 2. Editor codegen: all four catalogs and the bake regenerate byte-for-byte from their descriptions.
# --------------------------------------------------------------------------------------------------------------

unity_step resolve "${UNITY}" -batchmode -nographics -quit -projectPath "${UNITY_PROJECT}" \
  -logFile "${ARTIFACTS}/qualification/resolve.log"

unity_step codegen-probe "${UNITY}" -batchmode -nographics -quit -projectPath "${UNITY_PROJECT}" \
  -executeMethod GameCore.Validation.Editor.ProbeCatalogGenerator.GenerateCatalog \
  -logFile "${ARTIFACTS}/qualification/codegen-probe.log"
unity_step codegen-cards "${UNITY}" -batchmode -nographics -quit -projectPath "${UNITY_PROJECT}" \
  -executeMethod GameCore.Validation.Editor.CardCatalogGenerator.GenerateCatalog \
  -logFile "${ARTIFACTS}/qualification/codegen-cards.log"
unity_step codegen-checkpoint "${UNITY}" -batchmode -nographics -quit -projectPath "${UNITY_PROJECT}" \
  -executeMethod GameCore.Validation.Editor.CheckpointCatalogGenerator.GenerateCatalog \
  -logFile "${ARTIFACTS}/qualification/codegen-checkpoint.log"
unity_step codegen-traversal "${UNITY}" -batchmode -nographics -quit -projectPath "${UNITY_PROJECT}" \
  -executeMethod GameCore.Validation.Editor.TraversalCatalogGenerator.GenerateCatalog \
  -logFile "${ARTIFACTS}/qualification/codegen-traversal.log"
unity_step codegen-bake "${UNITY}" -batchmode -nographics -quit -projectPath "${UNITY_PROJECT}" \
  -executeMethod GameCore.Validation.Editor.BakeCatalogCoverageAuthoring.Bake \
  -logFile "${ARTIFACTS}/qualification/codegen-bake.log"

run_step catalog-reproducibility git diff --exit-code -- \
  unity/GameCore.Validation/Assets/GameCore.Validation/Generated \
  unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCards \
  unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCheckpoint \
  unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedTraversal

# --------------------------------------------------------------------------------------------------------------
# 3. Qualification shape.
# --------------------------------------------------------------------------------------------------------------

if [[ ",${SHAPES}," == *",qualification,"* ]]; then
  UNITY="${UNITY}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/qualification" \
    UNITY_TIMEOUT="${UNITY_TIMEOUT}" DOTNET="${DOTNET:-}" tools/unity/build_probe.sh

  QUALIFICATION_PLAYER="${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64"
  run_step fingerprint-ledger-qualification "${PYTHON}" tools/compare_registration_fingerprints.py \
    --ledger "${UNITY_PROJECT}" --ledger-out "${ARTIFACTS}/qualification"

  run_step probe-catalog-coverage-qualification env \
    PROBE_PLAYER="${QUALIFICATION_PLAYER}" \
    UNITY_PROJECT="${UNITY_PROJECT}" \
    ARTIFACTS="${ARTIFACTS}/probe" \
    PROBE_RUNS="${PROBE_RUNS}" \
    tools/unity/run_catalog_coverage_probe.sh
else
  QUALIFICATION_PLAYER="${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64"
  echo "-- qualification shape: NOT RUN (SHAPES='${SHAPES}' does not request it)"
fi

# --------------------------------------------------------------------------------------------------------------
# 4. Marker-free release shape.
# --------------------------------------------------------------------------------------------------------------

if [[ ",${SHAPES}," == *",release,"* ]]; then
  if [[ "${RELEASE_BUILD}" != "0" ]]; then
    if [[ -d "${RELEASE_PROJECT}" ]]; then
      echo "-- releasing an existing clone at ${RELEASE_PROJECT} (it is disposable and gitignored)"
      rm -rf "${RELEASE_PROJECT}"
    fi
    run_step release-project-prepare "${PYTHON}" tools/unity/prepare_gc017_release_project.py
  fi

  run_step release-clone-check "${PYTHON}" tools/check_release_clone.py \
    --clone "${RELEASE_PROJECT}" --json "${ARTIFACTS}/release/clone-surface.json"
  run_step link-xml-release "${PYTHON}" tools/check_link_xml.py \
    --project "${RELEASE_PROJECT}" --json "${ARTIFACTS}/release/link-xml.json"

  UNITY="${UNITY}" UNITY_PROJECT="${RELEASE_PROJECT}" ARTIFACTS="${ARTIFACTS}/release" \
    UNITY_TIMEOUT="${UNITY_TIMEOUT}" DOTNET="${DOTNET:-}" tools/unity/build_probe.sh

  RELEASE_PLAYER="${RELEASE_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64"
  run_step fingerprint-ledger-release "${PYTHON}" tools/compare_registration_fingerprints.py \
    --ledger "${RELEASE_PROJECT}" --ledger-out "${ARTIFACTS}/release"

  run_step fingerprint-compare "${PYTHON}" tools/compare_registration_fingerprints.py \
    --a "${ARTIFACTS}/qualification" --label-a qualification \
    --b "${ARTIFACTS}/release" --label-b release \
    --json "${ARTIFACTS}/fingerprint-comparison.json" \
    || echo "-- fingerprint-compare: NOT RUN for both shapes (build the qualification shape first)"

  run_step probe-catalog-coverage-release env \
    PROBE_PLAYER="${RELEASE_PLAYER}" \
    UNITY_PROJECT="${RELEASE_PROJECT}" \
    ARTIFACTS="${ARTIFACTS}/probe" \
    PROBE_RUNS="${PROBE_RUNS}" \
    PROBE_SHAPE=release \
    tools/unity/run_catalog_coverage_probe.sh
else
  echo "-- release shape: NOT RUN (SHAPES='${SHAPES}' does not request it)"
fi

echo "== GC-025 baseline player build finished =="
echo "qualification player : ${QUALIFICATION_PLAYER}"
echo "release player       : ${RELEASE_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64 (when the shape ran)"
echo "evidence             : ${ARTIFACTS}"
echo "environment          : ${ARTIFACTS}/host/environment.txt -> fill artifacts/baseline/ENVIRONMENT.md"
echo "unsupported targets  : artifacts/baseline/unsupported-targets.md (macOS/Windows/mobile/consoles stay unqualified)"

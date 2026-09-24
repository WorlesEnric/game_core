#!/usr/bin/env bash
# GC-003 wave gate: static checks, documentation validation, the plain-dotnet build/test of every project in
# dotnet/GameCore.sln, and (when UNITY is set) Unity codegen plus the IL2CPP probe in both modes.
#
# Required environment for the dotnet steps:
#   DOTNET  path to the .NET 8 SDK executable (default: dotnet on PATH)
# Optional:
#   PYTHON  python3 executable (default: python3 on PATH)
#   UNITY   Unity Editor executable of the pinned 6000.0.75f1 baseline; when set, the Unity steps run too
#   ARTIFACTS  artifact directory (default: <repo>/artifacts/gc-003)
#
# Exit codes: 0 every requested step passed; nonzero on the first failing step.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
PYTHON="${PYTHON:-python3}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/gc-003}"

mkdir -p "${ARTIFACTS}"
cd "${REPO_ROOT}"

echo "== GC-003 checks =="
echo "repo      : ${REPO_ROOT}"
echo "dotnet    : ${DOTNET}"
echo "unity     : ${UNITY:-<not set: Unity steps skipped>}"
echo "artifacts : ${ARTIFACTS}"

echo "-- step 1/5: static host-side checks of the C# sources"
"${PYTHON}" tools/check_game_core_csharp.py | tee "${ARTIFACTS}/static-checks.log"

echo "-- step 1b/5: verify the committed generated catalog without a C# compiler"
"${PYTHON}" tools/verify_generated_catalog.py | tee "${ARTIFACTS}/generated-catalog-verification.log"

echo "-- step 2/5: documentation validator"
"${PYTHON}" tools/validate_game_core_docs.py --self-test | tee "${ARTIFACTS}/validator-self-test.log"
"${PYTHON}" tools/validate_game_core_docs.py | tee "${ARTIFACTS}/validator.log"

echo "-- step 3/5: dotnet build"
"${DOTNET}" build dotnet/GameCore.sln -c Release | tee "${ARTIFACTS}/dotnet-build.log"

echo "-- step 4/5: dotnet test (writes TRX results per test project)"
"${DOTNET}" test dotnet/GameCore.sln -c Release --logger trx --results-directory "${ARTIFACTS}/trx" \
  | tee "${ARTIFACTS}/dotnet-test.log"

if [[ -z "${UNITY:-}" ]]; then
  echo "-- step 5/5: Unity steps skipped (set UNITY to run codegen and the IL2CPP probe)"
  echo "== GC-003 dotnet checks passed =="
  exit 0
fi

echo "-- step 5/5: Unity codegen + IL2CPP probe"
UNITY="${UNITY}" ARTIFACTS="${ARTIFACTS}/toolchain" tools/unity/build_probe.sh
PROBE_PLAYER="${REPO_ROOT}/unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64" \
  UNITY_PROJECT="${REPO_ROOT}/unity/GameCore.Validation" \
  ARTIFACTS="${ARTIFACTS}/toolchain" \
  tools/unity/run_probe.sh both

# The generated probe catalog must be byte-identical to what the compiler just wrote.
if ! git diff --exit-code -- unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs; then
  echo "the committed generated catalog changed during codegen; commit the regenerated file." >&2
  exit 1
fi

echo "== GC-003 checks passed =="

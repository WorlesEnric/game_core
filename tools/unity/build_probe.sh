#!/usr/bin/env bash
# GC-001 toolchain qualification: generate the closed registration catalog, then build the
# StandaloneLinux64 IL2CPP probe player with High managed stripping.
#
# Required environment:
#   UNITY  absolute path to the Unity Editor executable, e.g.
#          ~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
#
# Optional environment:
#   UNITY_PROJECT  Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS      artifact directory (default: <repo>/artifacts/toolchain)
#
# Exit codes: 0 both steps succeeded; nonzero on the first failing Unity invocation.
set -euo pipefail

if [[ -z "${UNITY:-}" ]]; then
  echo "UNITY must point to the Unity Editor executable for the pinned 6000.0.75f1 baseline." >&2
  exit 2
fi
if [[ ! -x "${UNITY}" ]]; then
  echo "UNITY=${UNITY} is not an executable file." >&2
  exit 2
fi
if [[ "${UNITY}" != *"6000.0.75f1"* ]]; then
  echo "warning: UNITY=${UNITY} does not contain the pinned revision 6000.0.75f1; the build script will refuse" >&2
  echo "         to build unless the Editor reports that exact version." >&2
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/toolchain}"

if [[ ! -f "${UNITY_PROJECT}/ProjectSettings/ProjectVersion.txt" ]]; then
  echo "no Unity project at UNITY_PROJECT=${UNITY_PROJECT}" >&2
  exit 2
fi

mkdir -p "${ARTIFACTS}"

echo "== GC-001 toolchain build =="
echo "editor      : ${UNITY}"
echo "project     : ${UNITY_PROJECT}"
echo "artifacts   : ${ARTIFACTS}"

{
  echo "editor: ${UNITY}"
  echo "project: ${UNITY_PROJECT}"
  echo "host: $(uname -a)"
  echo "date_utc: $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "uname_m: $(uname -m)"
  if command -v gcc >/dev/null 2>&1; then echo "gcc: $(gcc --version | head -n 1)"; else echo "gcc: absent"; fi
  if command -v clang >/dev/null 2>&1; then echo "clang: $(clang --version | head -n 1)"; else echo "clang: absent"; fi
  if command -v ld >/dev/null 2>&1; then echo "ld: $(ld --version | head -n 1)"; else echo "ld: absent"; fi
  if [[ -n "${DOTNET:-}" ]]; then echo "dotnet: ${DOTNET}"; fi
  echo "sysroot_glibc: $(ls -d "${HOME}"/Unity/Hub/Editor/*/Editor/Data/PlaybackEngines/LinuxStandaloneSupport/Variations/*/Sysroot 2>/dev/null | head -n 1 || true)"
  echo "packages_manifest_sha256: $(sha256sum "${UNITY_PROJECT}/Packages/manifest.json" | cut -d' ' -f1)"
} >"${ARTIFACTS}/environment.txt"

# Step 1: build-time code generation. Runs before the build so a stale or missing generated catalog cannot be
# mistaken for a build failure, and so the catalog hash is logged in its own artifact.
echo "-- step 1/2: generate closed registration catalog"
while pgrep -f 'gc-wt/gc-026/.*[G]ameCoreProbe|gc-wt/gc-026/.*[U]nity ' >/dev/null; do sleep 60; done
timeout --signal=TERM --kill-after=60 "${UNITY_TIMEOUT:-1800}" "${UNITY}" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "${UNITY_PROJECT}" \
  -executeMethod GameCore.Validation.Editor.ProbeCatalogGenerator.GenerateCatalog \
  -logFile "${ARTIFACTS}/codegen.log"

# Step 2: standalone IL2CPP player build for the selected baseline target.
echo "-- step 2/2: build StandaloneLinux64 IL2CPP player (High stripping)"
while pgrep -f 'gc-wt/gc-026/.*[G]ameCoreProbe|gc-wt/gc-026/.*[U]nity ' >/dev/null; do sleep 60; done
timeout --signal=TERM --kill-after=60 "${UNITY_TIMEOUT:-1800}" "${UNITY}" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "${UNITY_PROJECT}" \
  -executeMethod GameCore.Validation.Editor.BuildProbe.BuildLinuxIL2CPP \
  -logFile "${ARTIFACTS}/build.log"

PLAYER="${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64"
if [[ ! -f "${PLAYER}" ]]; then
  echo "build reported success but ${PLAYER} is missing." >&2
  exit 1
fi

{
  echo "player: ${PLAYER}"
  echo "player_bytes: $(stat -c '%s' "${PLAYER}" 2>/dev/null || stat -f '%z' "${PLAYER}")"
  echo "player_sha256: $(sha256sum "${PLAYER}" | cut -d' ' -f1)"
  echo "catalog_sha256: $(sha256sum "${UNITY_PROJECT}/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs" | cut -d' ' -f1)"
} >>"${ARTIFACTS}/environment.txt"

echo "== build complete =="
echo "player   : ${PLAYER}"
echo "catalog  : ${UNITY_PROJECT}/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs"
echo "lock file: ${UNITY_PROJECT}/Packages/packages-lock.json (produced by this resolve; commit it)"
echo "next     : tools/unity/run_probe.sh"

#!/usr/bin/env bash
# GC-012 Wave 4 provisional generic-execution profile gate.
#
# Runs, in order:
#   1. the host-side static checks over every GameCore C# source (balance, forbidden C# 10+ constructs),
#   2. the whole plain-dotnet solution: build and test (every Unity-free assembly of both families and the kernel),
#   3. the host-side generic-profile audit (`tools/w4_generic_profile_audit.py`): the assembly reference graph and
#      the kernel type inventory, which is what proves no kernel assembly needs a genre-specific type,
#   4. Unity package resolution on the qualification project,
#   5. the Unity EditMode suite of every testable package plus the W1, W2, narrative, card, W3 gate and W4 profile
#      assemblies,
#   6. the Unity PlayMode suite of the testable packages,
#   7. the StandaloneLinux64 IL2CPP player build with High managed stripping, regenerating BOTH committed catalogs
#      first,
#   8. every player probe, each executed PROBE_RUNS times (default 5): GC-001 positive and negative, GC-005 world
#      dispatch, the W1 gate, the W2 gate, the GC-010 narrative slice, the GC-011 card slice, the W3 gate and the
#      GC-012 W4 profile gate,
#   9. the documentation validator.
#
# What makes this the Wave 4 gate rather than a repeat of Wave 3:
#
#   * `Assets/link.xml` no longer carries `preserve="all"` for the narrative gameplay/rules assemblies, so a type
#     that High stripping removes is a missing generated root and not a linker-preservation accident. The narrative
#     family's generated entry (`ProbeCatalog.NarrativeFamilyEntryKey` -> `NarrativeFamilyPluginEntry`) and the card
#     family's (`CardCatalog.CardFamilyEntryKey` -> `CardFamilyPluginEntry`) are the reachability roots.
#   * Both families are compared canonically against their Editor/world fixture outputs: the narrative run against
#     the committed canonical trace's own declaration, the card run by its own facts digest.
#   * The P-017/P-019 multi-supporter `Additive` slot is observed end-to-end (composed 5 from the +2 and +3
#     providers, two support rows, exact retraction of one supporter).
#
# Required environment:
#   UNITY   absolute path to the Unity Editor executable of the pinned 6000.0.75f1 baseline, e.g.
#           ~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
#
# Optional environment:
#   DOTNET        .NET 8 SDK executable (default: dotnet on PATH)
#   PYTHON        python3 executable (default: python3 on PATH)
#   UNITY_PROJECT Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS     artifact directory (default: <repo>/artifacts/gates/w4-generic-profile)
#   PROBE_RUNS    repetitions of every player probe (default 5; any crashing run fails the gate)
#
# Exit codes: 0 every step passed; nonzero on the first failing step (2 for a missing prerequisite).
set -euo pipefail

if [[ -z "${UNITY:-}" ]]; then
  echo "UNITY must point to the Unity Editor executable for the pinned 6000.0.75f1 baseline." >&2
  exit 2
fi
if [[ ! -x "${UNITY}" ]]; then
  echo "UNITY=${UNITY} is not an executable file." >&2
  exit 2
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
PYTHON="${PYTHON:-python3}"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/gates/w4-generic-profile}"
PROBE_PLAYER="${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64"

mkdir -p "${ARTIFACTS}/trx" "${ARTIFACTS}/unity" "${ARTIFACTS}/toolchain"
cd "${REPO_ROOT}"

echo "== GC-012 Wave 4 provisional generic-execution profile gate =="
echo "repo      : ${REPO_ROOT}"
echo "unity     : ${UNITY}"
echo "dotnet    : ${DOTNET}"
echo "project   : ${UNITY_PROJECT}"
echo "artifacts : ${ARTIFACTS}"
echo "probe runs: ${PROBE_RUNS:-5}"

run_step() {
  local name="$1"
  shift
  echo "-- ${name}"
  "$@"
}

# 1. Host-side static checks: the checks a compiler would fail on immediately.
run_step static-checks "${PYTHON}" tools/check_game_core_csharp.py 2>&1 \
  | tee "${ARTIFACTS}/static-checks.log"

# 2. The whole plain-dotnet solution: the Unity-free half of the kernel plus both families' rules packages.
run_step dotnet-build "${DOTNET}" build dotnet/GameCore.sln -c Release
run_step dotnet-test "${DOTNET}" test dotnet/GameCore.sln -c Release \
  --logger trx --results-directory "${ARTIFACTS}/trx"

# 3. The generic-profile audit: assembly graph, forbidden edges and the kernel type inventory. Its JSON is the
#    machine-readable half of `audit.md`; the report cites the same run.
run_step generic-profile-audit "${PYTHON}" tools/w4_generic_profile_audit.py \
  --out "${ARTIFACTS}/generic-profile-audit.json"

# 4. Package resolution: both families' packages must resolve before any test runs.
run_step unity-resolve "${UNITY}" \
  -batchmode -nographics -quit \
  -projectPath "${UNITY_PROJECT}" \
  -logFile "${ARTIFACTS}/unity/resolve.log"

# 5. EditMode tests: every testable package plus the W1, W2, narrative, card, W3 gate and W4 profile assemblies.
#    Do not add -quit to a test-run command that relies on the runner to finish asynchronously (04 s10).
run_step unity-editmode "${UNITY}" \
  -batchmode -nographics \
  -projectPath "${UNITY_PROJECT}" \
  -runTests -testPlatform EditMode \
  -testResults "${ARTIFACTS}/unity/editmode-results.xml" \
  -logFile "${ARTIFACTS}/unity/editmode.log"

# 6. PlayMode tests: PlayerLoop installation, the application pump, reset/disposal and idle routing.
run_step unity-playmode "${UNITY}" \
  -batchmode -nographics \
  -projectPath "${UNITY_PROJECT}" \
  -runTests -testPlatform PlayMode \
  -testResults "${ARTIFACTS}/unity/playmode-results.xml" \
  -logFile "${ARTIFACTS}/unity/playmode.log"

# 7. Build-time code generation for BOTH catalogs, then the StandaloneLinux64 IL2CPP player with High managed
#    stripping. build_probe.sh regenerates the probe catalog (GC-003) before it builds; the card catalog has its
#    own batchmode entry point (GC-011) and is regenerated here so the build compiles the fresh file.
run_step card-catalog-codegen "${UNITY}" \
  -batchmode -nographics -quit \
  -projectPath "${UNITY_PROJECT}" \
  -executeMethod GameCore.Validation.Editor.CardCatalogGenerator.GenerateCatalog \
  -logFile "${ARTIFACTS}/unity/card-codegen.log"

UNITY="${UNITY}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" \
  "tools/unity/build_probe.sh"

# Both committed catalogs must be byte-identical to what the production compiler just wrote. A difference here
# means `tools/regen_catalog_group.py` (the no-SDK stand-in GC-012 used to add the family entries on the authoring
# host) and the production emitter disagree; the emitter is authoritative and this diff is the finding.
if ! git diff --exit-code -- unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs; then
  echo "the regenerated probe catalog differs from the committed one." >&2
  exit 1
fi
if ! git diff --exit-code -- unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCards/CardCatalog.g.cs; then
  echo "the regenerated card catalog differs from the committed one." >&2
  exit 1
fi

# 8. Player probes, each in its own process invocation of the same built player, PROBE_RUNS times each.
export PROBE_PLAYER UNITY_PROJECT
export PROBE_RUNS="${PROBE_RUNS:-5}"
PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" PROBE_RUNS="${PROBE_RUNS}" \
  "tools/unity/run_probe.sh" both
PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" PROBE_RUNS="${PROBE_RUNS}" \
  "tools/unity/run_world_probe.sh"
PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" PROBE_RUNS="${PROBE_RUNS}" \
  "tools/unity/run_w1_gate_probe.sh"
PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" PROBE_RUNS="${PROBE_RUNS}" \
  "tools/unity/run_w2_gate_probe.sh"
PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" PROBE_RUNS="${PROBE_RUNS}" \
  "tools/unity/run_narrative_probe.sh"
PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" PROBE_RUNS="${PROBE_RUNS}" \
  "tools/unity/run_cards_probe.sh"
PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" PROBE_RUNS="${PROBE_RUNS}" \
  "tools/unity/run_w3_gate_probe.sh"
PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" PROBE_RUNS="${PROBE_RUNS}" \
  "tools/unity/run_w4_profile_probe.sh"

# 9. Documentation gate of the same revision.
"${PYTHON}" tools/validate_game_core_docs.py --self-test | tee "${ARTIFACTS}/validator-self-test.log"
"${PYTHON}" tools/validate_game_core_docs.py | tee "${ARTIFACTS}/validator.log"

echo "== GC-012 Wave 4 profile gate PASSED =="
echo "player      : ${PROBE_PLAYER}"
echo "probe JSON  : ${ARTIFACTS}/toolchain/probe-result.json (GC-001 positive)"
echo "              ${ARTIFACTS}/toolchain/probe-negative.json (GC-001 negative)"
echo "              ${ARTIFACTS}/toolchain/probe-world-dispatch.json (GC-005)"
echo "              ${ARTIFACTS}/toolchain/probe-w1-gate.json (W1-GATE)"
echo "              ${ARTIFACTS}/toolchain/probe-w2-gate.json (W2-GATE)"
echo "              ${ARTIFACTS}/toolchain/probe-narrative.json (GC-010)"
echo "              ${ARTIFACTS}/toolchain/probe-cards.json (GC-011)"
echo "              ${ARTIFACTS}/toolchain/probe-w3-gate.json (W3-GATE)"
echo "              ${ARTIFACTS}/toolchain/probe-w4-profile.json (GC-012)"
echo "audit JSON  : ${ARTIFACTS}/generic-profile-audit.json"
echo "test results: ${ARTIFACTS}/unity/editmode-results.xml, ${ARTIFACTS}/unity/playmode-results.xml"
echo "note: every 'Pass' above is a reported process result; the XML logs and JSON results are the evidence."

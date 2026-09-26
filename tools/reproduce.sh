#!/usr/bin/env bash
# GameCore V1 clean-checkout reproduction (GC-029).
#
# One entry point that takes a genuinely fresh clone to a reproduced, declared profile. Run it from the
# repository root:
#
#   UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity DOTNET=$HOME/.dotnet/dotnet tools/reproduce.sh
#
# It runs, in order, the phases below. Every step has a timeout and prints a named failure with a non-zero
# exit; the whole run is transcribed to $ARTIFACTS/transcript.log and summarised in $ARTIFACTS/steps.tsv.
# Steps are numbered sequentially in the transcript, including one entry per individual host-side check.
#
#   1. toolchain versions  - the .NET SDK, the pinned Unity Editor and python3; written to environment.txt
#   2. packages            - the local package metadata/asmdef audit and its own self-test
#   3. generated documents - the failure-code table must equal what the sources declare, and the operator
#                            guide must match ProbeArguments.cs (no stale docs, no undocumented mode)
#   4. dotnet restore      - the solution's NuGet graph is restored
#   5. dotnet build+test   - every Unity-free assembly and every test project
#   6. host-side checks    - C# shape, contract surface, catalogs, budget record, clone self-test, link.xml
#   7. Unity resolve       - the qualification project's package graph resolves (writes packages-lock.json)
#                            and the regenerated lock is re-audited against the manifests
#   8. Unity EditMode      - every testable package plus every gate assembly, unfiltered
#   9. Unity PlayMode      - the same set
#  10. catalog byte-identity - all five codegen/bake steps, then `git diff --exit-code` over the four trees
#  11. players + probes   - the qualification player, the marker-free release player, and the three family
#                           probes in BOTH players, each PROBE_RUNS (=2, the project-owner cap) times;
#                           plus the documentation validator
#
# WHAT THIS SCRIPT DELIBERATELY DOES NOT DO.
#   * It does not run the exhaustive probe matrix. That is the wave gates' and GC-028's job
#     (`tools/run_w7_gate.sh`, `tools/run_conformance.sh`); this script is the clean-checkout entry point.
#   * It does not run the full-duration TEST-023 performance catalogue. That qualification is Deferred by
#     project-owner decision (2026-09-26); see docs/operator/deferred-scope.md. It does not claim timing.
#   * It does not qualify any platform but StandaloneLinux64 x86_64 / IL2CPP / High stripping / headless.
#
# AUDIO. The headless player runs with Unity audio DISABLED (an FMOD/PulseAudio crash at exit, crash-139).
# Nothing here re-enables it.
#
# UNITY EDITOR HANG. The pinned Editor has an intermittent pre-dispatch hang. Every Editor invocation is
# wrapped in `timeout` and retried exactly ONCE on a timeout; a second timeout is a failure, and any other
# non-zero exit is a failure immediately (a real compile/test error is never retried). See
# docs/operator/editor-hang.md.
#
# Required environment:
#   UNITY            absolute path to the Unity Editor executable of the pinned 6000.0.75f1 baseline
#
# Optional environment:
#   DOTNET           .NET 8 SDK executable (default: dotnet on PATH)
#   PYTHON           python3 executable (default: python3 on PATH)
#   UNITY_PROJECT    Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS        transcript/artifact directory (default: <repo>/artifacts/reproducibility)
#   PROBE_RUNS       repetitions of each family probe (default 2; this script rejects anything else, because
#                    two is the project-owner's cap for repeated runs)
#   UNITY_TIMEOUT    seconds one Unity Editor invocation may take (default 1800)
#   STEP_TIMEOUT     seconds one host-side step may take (default 1800)
#   PLAYER_TIMEOUT   seconds one player probe sequence may take (default 600)
#   RELEASE          build and probe the marker-free release player (default 1; 0 skips it and says so)
#   RELEASE_PROJECT  the disposable cloned project path (default: <repo>/unity/GameCore.ReleaseCheck)
#   DOCS             run the documentation validator (default 1; 0 skips it and says so)
#
# Exit codes: 0 every step passed; 2 a missing prerequisite; otherwise the first failing step's exit code.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

DOTNET="${DOTNET:-dotnet}"
PYTHON="${PYTHON:-python3}"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/reproducibility}"
PROBE_RUNS="${PROBE_RUNS:-2}"
UNITY_TIMEOUT="${UNITY_TIMEOUT:-1800}"
STEP_TIMEOUT="${STEP_TIMEOUT:-1800}"
PLAYER_TIMEOUT="${PLAYER_TIMEOUT:-600}"
RELEASE="${RELEASE:-1}"
RELEASE_PROJECT="${RELEASE_PROJECT:-${REPO_ROOT}/unity/GameCore.ReleaseCheck}"
DOCS="${DOCS:-1}"

fail() {
  echo "reproduce.sh: $*" >&2
  exit 2
}

# -----------------------------------------------------------------------------------------------------------
# Prerequisites. Checked before anything runs, so a missing tool is one clear message rather than a
# confusing failure three steps in.
# -----------------------------------------------------------------------------------------------------------

if [[ -z "${UNITY:-}" ]]; then
  fail "UNITY is not set. Point it at the pinned Editor:
  UNITY=\$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity tools/reproduce.sh"
fi
if [[ ! -x "${UNITY}" ]]; then
  fail "UNITY is not an executable file: '${UNITY}'"
fi
if [[ "${UNITY}" != *"6000.0.75f1"* ]]; then
  fail "UNITY does not name the pinned Editor 6000.0.75f1: '${UNITY}'. This repository qualifies exactly one
  Editor revision; a different one is a different, unqualified profile (docs/operator/profile.md)."
fi
if ! command -v "${DOTNET}" >/dev/null 2>&1 && [[ ! -x "${DOTNET}" ]]; then
  fail "DOTNET is not executable and not on PATH: '${DOTNET}'"
fi
if ! command -v "${PYTHON}" >/dev/null 2>&1 && [[ ! -x "${PYTHON}" ]]; then
  fail "PYTHON is not executable and not on PATH: '${PYTHON}'"
fi
if ! [[ "${PROBE_RUNS}" =~ ^[12]$ ]]; then
  fail "PROBE_RUNS must be 1 or 2 (the project-owner caps repeated runs at two): '${PROBE_RUNS}'"
fi
if ! command -v timeout >/dev/null 2>&1; then
  fail "the 'timeout' utility is not available. Every step in this script is bounded by a watchdog, and the
  repository's Unity hang policy depends on it (docs/operator/editor-hang.md). Install GNU coreutils."
fi
if [[ ! -f "${REPO_ROOT}/dotnet/GameCore.sln" ]]; then
  fail "no dotnet/GameCore.sln under ${REPO_ROOT}; run this script from the repository root"
fi

# One absolute artifact root: Unity resolves a relative -logFile against its own working directory while this
# script re-opens the same path against the repository root, so a relative ARTIFACTS would silently split them.
# `realpath -m` is GNU coreutils, which the Linux qualification host has; the fallback keeps the host-side steps
# usable on a machine whose realpath lacks -m (macOS), where this script is a diagnostic rather than the gate.
if realpath -m / >/dev/null 2>&1; then
  ARTIFACTS="$(realpath -m "${ARTIFACTS}")"
elif [[ "${ARTIFACTS}" != /* ]]; then
  ARTIFACTS="${REPO_ROOT}/${ARTIFACTS}"
fi
mkdir -p "${ARTIFACTS}"/{host,unity,toolchain,release,probe}
TRANSCRIPT="${ARTIFACTS}/transcript.log"
STEPS="${ARTIFACTS}/steps.tsv"
: >"${STEPS}"
printf 'step\tname\trc\tseconds\tcommand\n' >>"${STEPS}"

cd "${REPO_ROOT}"

# Everything below is transcribed: stdout and stderr of the whole run land in the transcript, which is the
# artifact the build host archives. `exec` is used so a failure's own output is captured too.
exec > >(tee "${TRANSCRIPT}") 2>&1

STEP_INDEX=0
BEGIN="$(date +%s)"

record_step() {
  local name="$1" rc="$2" seconds="$3" command="$4"
  printf '%s\t%s\t%s\t%s\t%s\n' "${STEP_INDEX}" "${name}" "${rc}" "${seconds}" "${command}" >>"${STEPS}"
}

# One host-side or player step: wrapped in `timeout`, recorded, and turned into a named failure.
run_step() {
  local label="$1"
  shift
  STEP_INDEX=$((STEP_INDEX + 1))
  local start end rc=0
  start="$(date +%s)"
  echo
  echo "== step ${STEP_INDEX}: ${label} =="
  echo "-- command: $*"
  timeout --signal=TERM --kill-after=60 "${STEP_TIMEOUT}" "$@" || rc=$?
  end="$(date +%s)"
  record_step "${label}" "${rc}" "$((end - start))" "$*"
  if (( rc != 0 )); then
    echo "   FAIL ${label}: exited ${rc} after $((end - start))s" >&2
    if (( rc == 124 || rc == 137 )); then
      echo "   cause: the ${STEP_TIMEOUT}s step timeout elapsed (exit ${rc}). Raise STEP_TIMEOUT only if you" >&2
      echo "          have established that this step legitimately needs longer." >&2
    elif (( rc == 127 )); then
      echo "   cause: a command in this step was not found (exit 127). Check DOTNET / PYTHON / PATH." >&2
    fi
    echo "   transcript: ${TRANSCRIPT}" >&2
    echo "   step ledger: ${STEPS}" >&2
    exit "${rc}"
  fi
  echo "   ok ${label} ($((end - start))s)"
}

# One Unity Editor invocation: `timeout` plus exactly one retry on a timeout. Reproduces the policy every
# gate in this repository uses (docs/operator/editor-hang.md).
unity_step() {
  local label="$1"
  shift
  STEP_INDEX=$((STEP_INDEX + 1))
  local attempt rc=0 start end
  start="$(date +%s)"
  echo
  echo "== step ${STEP_INDEX}: ${label} =="
  echo "-- command: $*"
  for attempt in 1 2; do
    if (( attempt == 2 )); then
      echo "-- ${label}: retrying once after a timeout (the Editor has an intermittent pre-dispatch hang)"
    fi
    rc=0
    timeout --signal=TERM --kill-after=60 "${UNITY_TIMEOUT}" "$@" || rc=$?
    if (( rc == 0 )); then
      break
    fi
    if (( rc == 124 || rc == 137 )); then
      echo "   ${label}: Unity Editor timed out after ${UNITY_TIMEOUT}s (exit ${rc}, attempt ${attempt}/2)" >&2
      continue
    fi
    echo "   FAIL ${label}: Unity Editor exited ${rc}; a compile or test error is never retried" >&2
    echo "   log: see the -logFile path in the command above" >&2
    record_step "${label}" "${rc}" "$(( $(date +%s) - start ))" "$*"
    exit "${rc}"
  done
  end="$(date +%s)"
  record_step "${label}" "${rc}" "$((end - start))" "$*"
  if (( rc != 0 )); then
    echo "   FAIL ${label}: Unity Editor timed out twice; this is not the known intermittent pre-dispatch hang" >&2
    echo "   diagnosis: docs/operator/editor-hang.md (sudo -n gdb -p <pid> -batch -ex 'thread apply all bt')" >&2
    echo "   transcript: ${TRANSCRIPT}" >&2
    exit "${rc}"
  fi
  echo "   ok ${label} ($((end - start))s)"
}

# One family probe sequence in one player. probe_runs.sh repeats each probe PROBE_RUNS times with its own
# per-run watchdog and fails on any dirty run, so its exit code is the verdict.
probe_step() {
  local label="$1"
  shift
  STEP_INDEX=$((STEP_INDEX + 1))
  local start end rc=0
  start="$(date +%s)"
  echo
  echo "== step ${STEP_INDEX}: ${label} =="
  echo "-- command: $*"
  timeout --signal=TERM --kill-after=60 "${PLAYER_TIMEOUT}" "$@" || rc=$?
  end="$(date +%s)"
  record_step "${label}" "${rc}" "$((end - start))" "$*"
  if (( rc != 0 )); then
    echo "   FAIL ${label}: the family probe sequence failed (exit ${rc}) after $((end - start))s" >&2
    if (( rc == 124 || rc == 137 )); then
      echo "   cause: the ${PLAYER_TIMEOUT}s player timeout elapsed (exit ${rc})" >&2
    fi
    echo "   a probe writes its structured result beside ARTIFACTS; read the JSON, not just the exit code" >&2
    echo "   transcript: ${TRANSCRIPT}" >&2
    exit "${rc}"
  fi
  echo "   ok ${label} ($((end - start))s)"
}

echo "== GameCore V1 clean-checkout reproduction (GC-029) =="
echo "repo          : ${REPO_ROOT}"
echo "unity         : ${UNITY}"
echo "dotnet        : ${DOTNET}"
echo "python        : ${PYTHON}"
echo "project       : ${UNITY_PROJECT}"
echo "artifacts     : ${ARTIFACTS}"
echo "probe runs    : ${PROBE_RUNS} (project-owner cap: two)"
echo "release player: ${RELEASE}"
echo "docs validator: ${DOCS}"
echo "audio         : DISABLED in the headless player (crash-139)"
echo "performance   : full-duration TEST-023 timing is Deferred by project-owner decision; not run here"
echo "transcript    : ${TRANSCRIPT}"

# -----------------------------------------------------------------------------------------------------------
# 1. Toolchain versions.
# -----------------------------------------------------------------------------------------------------------

STEP_INDEX=$((STEP_INDEX + 1))
START="$(date +%s)"
echo
echo "== step ${STEP_INDEX}: toolchain versions =="
{
  echo "date_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "repo=${REPO_ROOT}"
  echo "revision=$(git -C "${REPO_ROOT}" rev-parse HEAD 2>/dev/null || echo unknown)"
  echo "revision_dirty=$(git -C "${REPO_ROOT}" status --porcelain 2>/dev/null | wc -l | tr -d ' ')"
  echo "unity=${UNITY}"
  # Asserted from the UNITY path above; recorded here so the transcript states what was checked, not what was
  # hoped for. The Editor reports its own revision in every player result document.
  echo "editor_version=6000.0.75f1 (asserted from the UNITY path)"
  echo "dotnet_path=$(command -v "${DOTNET}" 2>/dev/null || echo "${DOTNET}")"
  echo "dotnet=$("${DOTNET}" --version 2>/dev/null | tail -1 || echo unknown)"
  echo "python=$("${PYTHON}" --version 2>/dev/null | tail -1 || echo unknown)"
  echo "host=$(uname -srm)"
  echo "nproc=$(nproc 2>/dev/null || sysctl -n hw.ncpu 2>/dev/null || echo unknown)"
  echo "timeout_utility=$(command -v timeout 2>/dev/null || echo missing)"
  echo "probe_runs=${PROBE_RUNS}"
} >"${ARTIFACTS}/environment.txt"
cat "${ARTIFACTS}/environment.txt"
record_step toolchain-versions 0 "$(( $(date +%s) - START ))" "record toolchain versions"
echo "   note: this records what the host reported. It is not a build or test result."

# -----------------------------------------------------------------------------------------------------------
# 2. Packages: metadata/asmdef audit, its self-test, and the resolved lock.
# -----------------------------------------------------------------------------------------------------------

run_step package-metadata-self-test "${PYTHON}" tools/check_package_metadata.py --self-test
run_step package-metadata-audit "${PYTHON}" tools/check_package_metadata.py \
  --json "${ARTIFACTS}/host/package-metadata.json"

# 3. Generated documents must match their sources.
run_step failure-codes-check "${PYTHON}" tools/emit_failure_codes.py --check \
  --json "${ARTIFACTS}/host/failure-codes.json"

# The operator guide lives outside docs/game-core, so the game-core validator does not read it. This check
# keeps it honest: links/anchors, and the documented player command-line surface against ProbeArguments.cs.
run_step operator-docs-self-test "${PYTHON}" tools/check_operator_docs.py --self-test
run_step operator-docs "${PYTHON}" tools/check_operator_docs.py \
  --json "${ARTIFACTS}/host/operator-docs.json"

# 4. Restore the dotnet solution's package graph.
run_step dotnet-restore "${DOTNET}" restore dotnet/GameCore.sln --nologo -v:q

# 5. Build and test the whole Unity-free solution.
run_step dotnet-build "${DOTNET}" build dotnet/GameCore.sln -c Release --no-restore --nologo -v:q
run_step dotnet-test "${DOTNET}" test dotnet/GameCore.sln -c Release --no-build --nologo -v:q \
  --logger "trx;LogFilePrefix=reproduce" --results-directory "${ARTIFACTS}/host/trx"

echo
echo "== step 6: host-side checks (no Editor, no player) =="
run_step csharp-check "${PYTHON}" tools/check_game_core_csharp.py
run_step contract-surface-parity "${PYTHON}" tools/check_contract_surface_parity.py
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
run_step budget-record "${PYTHON}" tools/check_budget_record.py \
  --json "${ARTIFACTS}/host/budget-record.json"
run_step release-clone-self-test "${PYTHON}" tools/check_release_clone.py --self-test
run_step link-xml-qualification "${PYTHON}" tools/check_link_xml.py \
  --project "${UNITY_PROJECT}" --json "${ARTIFACTS}/host/link-xml-qualification.json"
for script in tools/reproduce.sh tools/unity/build_probe.sh tools/unity/probe_runs.sh \
  tools/unity/run_narrative_probe.sh tools/unity/run_cards_probe.sh tools/unity/run_traversal_probe.sh; do
  run_step "shell-parse $(basename "${script}")" bash -n "${script}"
done

# -----------------------------------------------------------------------------------------------------------
# 7. Unity package resolution.
# -----------------------------------------------------------------------------------------------------------

unity_step unity-resolve "${UNITY}" -batchmode -nographics -quit \
  -projectPath "${UNITY_PROJECT}" \
  -logFile "${ARTIFACTS}/unity/resolve.log"

# The resolve is what (re)writes packages-lock.json. Re-audit the lock afterwards: this is the moment a
# version or dependency edit shows up as a resolution problem rather than as a silent metadata drift.
run_step package-lock-audit "${PYTHON}" tools/check_package_metadata.py \
  --json "${ARTIFACTS}/host/package-metadata-after-resolve.json"

# -----------------------------------------------------------------------------------------------------------
# 8-9. Unity test suites. No -assemblyNames and no -quit: the runner discovers every test assembly the
# manifest's `testables` names, which is what makes every gate assembly participate (04 s10 forbids -quit
# on a test run).
# -----------------------------------------------------------------------------------------------------------

unity_step unity-editmode "${UNITY}" \
  -batchmode -nographics \
  -projectPath "${UNITY_PROJECT}" \
  -runTests -testPlatform EditMode \
  -testResults "${ARTIFACTS}/unity/editmode-results.xml" \
  -logFile "${ARTIFACTS}/unity/editmode.log"

unity_step unity-playmode "${UNITY}" \
  -batchmode -nographics \
  -projectPath "${UNITY_PROJECT}" \
  -runTests -testPlatform PlayMode \
  -testResults "${ARTIFACTS}/unity/playmode-results.xml" \
  -logFile "${ARTIFACTS}/unity/playmode.log"

for results in "${ARTIFACTS}/unity/editmode-results.xml" "${ARTIFACTS}/unity/playmode-results.xml"; do
  if [[ ! -s "${results}" ]]; then
    echo "   FAIL unity-tests: no results document at ${results}; the Editor run produced no verdict, which is" >&2
    echo "        NotRun rather than Pass (see docs/operator/editor-hang.md)" >&2
    exit 1
  fi
done
echo "   ok unity-tests: both result documents exist ($(grep -c '<test-case' "${ARTIFACTS}/unity/editmode-results.xml" || true) EditMode cases, $(grep -c '<test-case' "${ARTIFACTS}/unity/playmode-results.xml" || true) PlayMode cases)"

# -----------------------------------------------------------------------------------------------------------
# 10. Catalog generation and byte identity.
# -----------------------------------------------------------------------------------------------------------

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

echo
echo "== step ${STEP_INDEX}: catalog byte-identity =="
STEP_INDEX=$((STEP_INDEX + 1))
echo "-- command: git diff --exit-code -- <the four generated trees>"
if ! git diff --exit-code -- \
  unity/GameCore.Validation/Assets/GameCore.Validation/Generated \
  unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCards \
  unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCheckpoint \
  unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedTraversal; then
  record_step catalog-byte-identity 1 0 "git diff --exit-code -- <four generated trees>"
  echo "   FAIL catalog-byte-identity: regenerating the catalogs changed the committed bytes." >&2
  echo "   The committed catalogs are STALE, or generation is NON-DETERMINISTIC. Decide which:" >&2
  echo "     * different content on every run  -> non-determinism: a generation defect. Do NOT commit it." >&2
  echo "     * stable content across runs       -> stale: commit the regenerated output with the change that" >&2
  echo "                                           altered the catalog description." >&2
  echo "   See docs/operator/catalog-generation.md." >&2
  exit 1
fi
record_step catalog-byte-identity 0 0 "git diff --exit-code -- <four generated trees>"
echo "   ok catalog-byte-identity: the committed catalogs are exactly what the pinned Editor produces"

# -----------------------------------------------------------------------------------------------------------
# 11. Players and the family probes.
# -----------------------------------------------------------------------------------------------------------

QUALIFICATION_PLAYER="${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64"

echo
echo "== step ${STEP_INDEX}: qualification player build =="
STEP_INDEX=$((STEP_INDEX + 1))
START="$(date +%s)"
rc=0
UNITY="${UNITY}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" \
  UNITY_TIMEOUT="${UNITY_TIMEOUT}" DOTNET="${DOTNET}" tools/unity/build_probe.sh || rc=$?
record_step qualification-player "${rc}" "$(( $(date +%s) - START ))" "tools/unity/build_probe.sh"
if (( rc != 0 )); then
  echo "   FAIL qualification-player: tools/unity/build_probe.sh exited ${rc}; see ${ARTIFACTS}/toolchain/build.log" >&2
  exit "${rc}"
fi
if [[ ! -x "${QUALIFICATION_PLAYER}" ]]; then
  echo "   FAIL qualification-player: no executable player at ${QUALIFICATION_PLAYER}" >&2
  exit 1
fi
echo "   ok qualification-player: ${QUALIFICATION_PLAYER}"

echo
echo "== family probes in the qualification player, ${PROBE_RUNS} run(s) each =="
for harness in run_narrative_probe.sh run_cards_probe.sh run_traversal_probe.sh; do
  probe_step "probe-${harness}" \
    env PROBE_PLAYER="${QUALIFICATION_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" \
    ARTIFACTS="${ARTIFACTS}/probe" PROBE_RUNS="${PROBE_RUNS}" "tools/unity/${harness}"
done

RELEASE_PLAYER=""
if [[ "${RELEASE}" == "1" ]]; then
  echo
  echo "== step ${STEP_INDEX}: marker-free release player =="
  STEP_INDEX=$((STEP_INDEX + 1))
  if [[ -e "${RELEASE_PROJECT}" ]]; then
    echo "   FAIL release-project: ${RELEASE_PROJECT} already exists." >&2
    echo "   It is a disposable clone and a clean checkout never has one. Remove it and re-run:" >&2
    echo "     rm -rf ${RELEASE_PROJECT}" >&2
    exit 1
  fi
  run_step release-project-prepare "${PYTHON}" tools/unity/prepare_gc017_release_project.py
  run_step link-xml-release "${PYTHON}" tools/check_link_xml.py \
    --project "${RELEASE_PROJECT}" --json "${ARTIFACTS}/release/link-xml.json"
  run_step release-clone-check "${PYTHON}" tools/check_release_clone.py \
    --clone "${RELEASE_PROJECT}" --json "${ARTIFACTS}/release/clone-surface.json"
  run_step release-fault-surface "${PYTHON}" tools/check_release_fault_free.py \
    --dotnet "${DOTNET}" --json "${ARTIFACTS}/release/fault-surface.json"
  run_step release-telemetry-surface "${PYTHON}" tools/check_release_telemetry_free.py \
    --dotnet "${DOTNET}" --artifacts "${ARTIFACTS}" --json "${ARTIFACTS}/release/telemetry-source.json"

  START="$(date +%s)"
  rc=0
  UNITY="${UNITY}" UNITY_PROJECT="${RELEASE_PROJECT}" ARTIFACTS="${ARTIFACTS}/release" \
    UNITY_TIMEOUT="${UNITY_TIMEOUT}" DOTNET="${DOTNET}" tools/unity/build_probe.sh || rc=$?
  record_step release-player "${rc}" "$(( $(date +%s) - START ))" "tools/unity/build_probe.sh (release clone)"
  if (( rc != 0 )); then
    echo "   FAIL release-player: tools/unity/build_probe.sh exited ${rc}; see ${ARTIFACTS}/release/build.log" >&2
    exit "${rc}"
  fi

  RELEASE_PLAYER="${RELEASE_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64"
  if [[ ! -x "${RELEASE_PLAYER}" ]]; then
    echo "   FAIL release-player: no executable player at ${RELEASE_PLAYER}" >&2
    exit 1
  fi

  # The release player must actually be marker-free. Assert it rather than assuming the preparer worked.
  run_step release-player-latches "${PYTHON}" tools/check_player_fault_free.py \
    --player "$(dirname "${RELEASE_PLAYER}")" --json "${ARTIFACTS}/release/player-surface.json"
  run_step release-gate-surface "${PYTHON}" tools/check_release_gate_free.py \
    --player "$(dirname "${RELEASE_PLAYER}")" \
    --qualification "$(dirname "${QUALIFICATION_PLAYER}")" \
    --json "${ARTIFACTS}/release/gate-surface.json"

  echo
  echo "== family probes in the marker-free release player, ${PROBE_RUNS} run(s) each =="
  for harness in run_narrative_probe.sh run_cards_probe.sh run_traversal_probe.sh; do
    probe_step "release-probe-${harness}" \
      env PROBE_PLAYER="${RELEASE_PLAYER}" UNITY_PROJECT="${RELEASE_PROJECT}" \
      ARTIFACTS="${ARTIFACTS}/release" PROBE_RUNS="${PROBE_RUNS}" "tools/unity/${harness}"
  done
else
  echo
  echo "-- release player: NOT RUN (RELEASE=0). No marker-free player was built or inspected by this run, so"
  echo "   this transcript does not support a marker-free claim."
fi

if [[ "${DOCS}" == "1" ]]; then
  echo
  echo "== step ${STEP_INDEX}: documentation validator =="
  STEP_INDEX=$((STEP_INDEX + 1))
  "${PYTHON}" tools/validate_game_core_docs.py --self-test | tee "${ARTIFACTS}/validator-self-test.log"
  "${PYTHON}" tools/validate_game_core_docs.py | tee "${ARTIFACTS}/validator.log"
  record_step documentation-validator 0 0 "tools/validate_game_core_docs.py"
else
  echo
  echo "-- documentation validator: NOT RUN (DOCS=0)"
fi

# -----------------------------------------------------------------------------------------------------------
# Summary.
# -----------------------------------------------------------------------------------------------------------

TOTAL="$(( $(date +%s) - BEGIN ))"
echo
echo "== GameCore V1 clean-checkout reproduction PASSED =="
echo "revision             : $(git rev-parse HEAD 2>/dev/null || echo unknown)"
echo "qualification player : ${QUALIFICATION_PLAYER}"
echo "release player       : ${RELEASE_PLAYER:-<not built (RELEASE=0)>}"
echo "transcript           : ${TRANSCRIPT}"
echo "step ledger          : ${STEPS}"
echo "environment          : ${ARTIFACTS}/environment.txt"
echo "test results         : ${ARTIFACTS}/unity/editmode-results.xml, ${ARTIFACTS}/unity/playmode-results.xml, ${ARTIFACTS}/host/trx"
echo "family probes        : ${ARTIFACTS}/probe/, ${ARTIFACTS}/release/"
echo "elapsed              : ${TOTAL}s"
echo "note: PROBE_RUNS=${PROBE_RUNS}; every 'Pass' above is a reported process result, not this script's opinion."
echo "note: this run reproduces the declared profile (StandaloneLinux64 x86_64, IL2CPP, High stripping,"
echo "      headless). It qualifies no other platform."
echo "note: full-duration TEST-023 timing was NOT run: Deferred by project-owner decision."
echo "      See artifacts/performance/BUDGET_DECISIONS.md and docs/operator/deferred-scope.md."

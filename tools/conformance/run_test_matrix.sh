#!/usr/bin/env bash
# GC-028 conformance matrix runner: TEST-001..TEST-024 -> concrete commands + per-suite status JSON.
#
# What this script is for. The Wave 8 exit gate says "Every P requirement and O operation has passing
# executable evidence" and "No NotRun or Blocked required case is waived". The evidence index
# (`tools/conformance/build_evidence_index.py`) answers the first half clause by clause from result files; this
# runner is what PRODUCES those result files in one reproducible pass and, separately, reports the 24 validation
# suites of `08-validation-and-performance.md` as a per-suite status matrix.
#
# Layout it writes (the evidence index reads exactly this shape):
#   <ARTIFACTS>/trx/                       vstest TRX, whole solution plus the per-suite filtered runs
#   <ARTIFACTS>/unity/                     Unity NUnit3 result XML (EditMode, PlayMode, per-assembly filters)
#   <ARTIFACTS>/toolchain/                 qualification player logs, probe result JSON, environment.txt
#   <ARTIFACTS>/release/                   marker-free release clone, player logs and probe result JSON
#   <ARTIFACTS>/host/                      host-side check JSON (budget record, link.xml, gate sources)
#   <ARTIFACTS>/static-checks.log          the host-side checks' own log, including the parity and catalog
#                                          byte-identity markers the index resolves as `check:` evidence
#   <ARTIFACTS>/suite-status.json          the per-suite matrix this script is named for
#   <ARTIFACTS>/evidence-index.{json,md}   written by build_evidence_index.py at the end
#   <ARTIFACTS>/compatibility.{json,md}    written by build_compatibility.py at the end
#   <ARTIFACTS>/family-audit.{json,md}     the three-family audit summary
#
# Conventions it inherits from every other gate in this repository:
#   * bash, `set -euo pipefail`, tool paths from the environment (UNITY, DOTNET, PYTHON).
#   * PROBE_RUNS is capped at TWO (project-owner decision, 2026-09-26).
#   * Every Unity Editor invocation and every player launch is wrapped in `timeout`, and a Unity invocation is
#     retried exactly ONCE when it timed out (the Editor's known intermittent pre-dispatch hang). A step that
#     times out twice fails.
#   * The headless player runs with audio disabled and `-nographics` (crash-139). Nothing here re-enables audio.
#   * Nothing is reported as passing because a script said so: the per-suite status is read out of the TRX /
#     NUnit3 XML / probe JSON the commands actually produced, and a suite whose result file is missing is
#     `NotRun`, never `Pass`.
#
# Required environment:
#   UNITY   absolute path to the pinned Unity 6000.0.75f1 Editor executable
#
# Optional environment:
#   DOTNET         .NET 8 SDK executable (default: dotnet on PATH)
#   PYTHON         python3 executable (default: python3 on PATH)
#   UNITY_PROJECT  Unity project (default <repo>/unity/GameCore.Validation)
#   ARTIFACTS      output directory (default <repo>/artifacts/conformance/results)
#   PROBE_RUNS     repetitions of every player probe (default 2; values above 2 are refused)
#   UNITY_TIMEOUT  seconds a Unity invocation may take (default 1800)
#   SUITE          run one suite only, e.g. SUITE=TEST-013 (default: every suite)
#   STAGES         comma-separated subset of: dotnet,unity,codegen,player,release,host,docs (default all)
#   RELEASE_BUILD  1 to prepare and build the marker-free release clone (default 1)
#
# Flags:
#   --dry-run   print every command the selected stages would run and write no results
#   --list      print the suite table and exit
#
# Exit codes: 0 everything requested passed; 1 a suite or a check failed; 2 a missing prerequisite or bad usage.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"

UNITY="${UNITY:-}"
DOTNET="${DOTNET:-$(command -v dotnet || true)}"
PYTHON="${PYTHON:-$(command -v python3 || true)}"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/conformance/results}"
PROBE_RUNS="${PROBE_RUNS:-2}"
UNITY_TIMEOUT="${UNITY_TIMEOUT:-1800}"
SUITE="${SUITE:-}"
STAGES="${STAGES:-dotnet,unity,codegen,player,release,host,docs}"
RELEASE_BUILD="${RELEASE_BUILD:-1}"

DRY_RUN=0
for argument in "$@"; do
  case "${argument}" in
    --dry-run) DRY_RUN=1 ;;
    --list) LIST=1 ;;
    --rows) ROWS=1 ;;
    *) echo "run_test_matrix.sh: unknown argument '${argument}'" >&2; exit 2 ;;
  esac
done
LIST="${LIST:-0}"
ROWS="${ROWS:-0}"

export PROBE_RUNS

# -----------------------------------------------------------------------------------------------------------
# The suite table. One row per TEST-001..TEST-024:
#   <id>|<dotnet project>:<filter>|<unity assembly filter>|<probe harnesses>
# A field may be empty; `:` inside a dotnet field separates project from filter; harnesses are separated by `,`.
# The mapping is the one `08-validation-and-performance.md` names in each suite's "Protocol coverage" line plus
# that suite's own acceptance text, and every harness named here is one this repository already ships.
# -----------------------------------------------------------------------------------------------------------
SUITES=(
  "TEST-001|dotnet/tests/GameCore.Content.Compiler.Tests:*||run_probe.sh:Positive:both,run_probe.sh:MissingRegistration:both,run_catalog_coverage_probe.sh:CatalogCoverage"
  "TEST-002|dotnet/tests/GameCore.Contracts.Tests:FullyQualifiedName~Checkpoint|GameCore.Unity.Runtime.Tests|run_gc018_probe.sh,run_w5_gate_probe.sh"
  "TEST-003|dotnet/tests/GameCore.Composition.Tests:FullyQualifiedName~ServiceResolution|GameCore.Composition.Tests|run_w1_gate_probe.sh"
  "TEST-004|dotnet/tests/GameCore.Derivation.Tests:FullyQualifiedName~ReferenceComposition|GameCore.Derivation.Tests|run_narrative_probe.sh,run_w4_gate_probe.sh"
  "TEST-005|dotnet/tests/GameCore.Planning.Tests:FullyQualifiedName~Precedence|GameCore.Planning.Tests|run_w4_gate_probe.sh"
  "TEST-006|dotnet/tests/GameCore.Derivation.Tests:FullyQualifiedName~Isolation|GameCore.Derivation.Tests|run_gc013_probe.sh,run_conformance_probe.sh"
  "TEST-007|dotnet/tests/GameCore.Derivation.Tests:FullyQualifiedName~TerminationAndBudget|GameCore.Derivation.Tests|"
  "TEST-008|dotnet/tests/GameCore.Derivation.Tests:FullyQualifiedName~Agreement|GameCore.Derivation.Tests|run_w7_gate_probe.sh"
  "TEST-009|dotnet/tests/GameCore.Planning.Tests:FullyQualifiedName~PlanStateMachine|GameCore.Unity.Assembly.Tests|run_w2_gate_probe.sh,run_gc017_faults_probe.sh"
  "TEST-010|dotnet/tests/GameCore.Planning.Tests:FullyQualifiedName~SlotPolicyValidator|GameCore.Planning.Tests|run_w4_gate_probe.sh"
  "TEST-011|dotnet/tests/GameCore.Execution.Tests:FullyQualifiedName~TemporalAccumulator|GameCore.Unity.Runtime.Tests|run_world_probe.sh,run_w3_gate_probe.sh"
  "TEST-012|dotnet/tests/GameCore.Planning.Tests:FullyQualifiedName~ScheduleCompiler|GameCore.Planning.Tests|run_w2_gate_probe.sh"
  "TEST-013|dotnet/tests/GameCore.Execution.Tests:FullyQualifiedName~RequestLedger|GameCore.Unity.Messages.Tests|run_cards_probe.sh,run_w3_gate_probe.sh"
  "TEST-014|dotnet/tests/GameCore.Execution.Tests:FullyQualifiedName~Observation|GameCore.Unity.Observation.Tests|run_w5_gate_probe.sh"
  "TEST-015|dotnet/tests/GameCore.Composition.Tests:FullyQualifiedName~Lifecycle|GameCore.Composition.Tests|run_lifecycle_stress_probe.sh,run_w4_gate_probe.sh"
  "TEST-016|dotnet/tests/GameCore.Recovery.Fixtures.Tests:FullyQualifiedName~RecoveryPolicy|GameCore.Unity.Faults.Tests|run_gc017_faults_probe.sh,run_recovery_probe.sh"
  "TEST-017|dotnet/tests/GameCore.Contracts.Tests:FullyQualifiedName~Checkpoint|GameCore.Unity.Recovery.Tests|run_gc018_probe.sh,run_recovery_probe.sh"
  "TEST-018|dotnet/tests/GameCore.Execution.Tests:FullyQualifiedName~WorldResourceLedger|GameCore.Unity.Runtime.Tests|run_world_probe.sh,run_w1_gate_probe.sh"
  "TEST-019|dotnet/tests/GameCore.Adapters.Tests:*|GameCore.Unity.Adapters.Tests|run_gc019_probe.sh,run_traversal_probe.sh"
  "TEST-020|dotnet/tests/GameCore.Content.Compiler.Tests:FullyQualifiedName~Catalog|GameCore.CatalogCoverage.Tests|run_catalog_coverage_probe.sh:CatalogCoverage"
  "TEST-021|dotnet/tests/GameCore.ReferenceConformance.Tests:FullyQualifiedName~Reference|GameCore.Conformance.Tests|run_conformance_probe.sh,run_narrative_probe.sh,run_cards_probe.sh,run_traversal_probe.sh"
  "TEST-022|dotnet/tests/GameCore.Replay.Tests:FullyQualifiedName~Replay|GameCore.Replay.IntegrationTests|run_replay_probe.sh"
  "TEST-023|dotnet/tests/GameCore.Benchmarks.Tests:FullyQualifiedName~PerformanceBudget|GameCore.Benchmarks.Tests|"
  "TEST-024|dotnet/tests/GameCore.ReferenceConformance.Tests:FullyQualifiedName~ConformanceDocGap|GameCore.Conformance.Tests|"
)

stage_enabled() {
  case ",${STAGES}," in
    *",$1,"*) return 0 ;;
    *) return 1 ;;
  esac
}

if (( ROWS == 1 )); then
  printf '%s\n' "${SUITES[@]}"
  exit 0
fi

if (( LIST == 1 )); then
  printf '%-10s %-58s %-40s %s\n' suite dotnet unity probes
  for row in "${SUITES[@]}"; do
    IFS='|' read -r id dotnet_field unity_field probe_field <<< "${row}"
    printf '%-10s %-58s %-40s %s\n' "${id}" "${dotnet_field}" "${unity_field}" "${probe_field}"
  done
  exit 0
fi

if (( DRY_RUN == 0 )); then
  if [[ -z "${UNITY}" ]]; then
    echo "run_test_matrix.sh: UNITY must name the pinned Unity 6000.0.75f1 Editor executable" >&2
    exit 2
  fi
  if [[ ! -x "${UNITY}" ]]; then
    echo "run_test_matrix.sh: UNITY is not executable: ${UNITY}" >&2
    exit 2
  fi
  if [[ ! -x "${DOTNET}" ]]; then
    echo "run_test_matrix.sh: DOTNET is not an executable .NET SDK: '${DOTNET}'" >&2
    exit 2
  fi
  if [[ ! -x "${PYTHON}" ]]; then
    echo "run_test_matrix.sh: PYTHON is not an executable python3: '${PYTHON}'" >&2
    exit 2
  fi
  if ! [[ "${PROBE_RUNS}" =~ ^[12]$ ]]; then
    echo "run_test_matrix.sh: PROBE_RUNS must be 1 or 2 (project-owner cap): ${PROBE_RUNS}" >&2
    exit 2
  fi
fi

ARTIFACTS="$(cd "${REPO_ROOT}" && mkdir -p "${ARTIFACTS}" && cd "${ARTIFACTS}" && pwd)"
mkdir -p "${ARTIFACTS}"/{trx,unity,toolchain,release,host,matrix}
cd "${REPO_ROOT}"

echo "== GC-028 conformance test matrix =="
echo "repo        : ${REPO_ROOT}"
echo "artifacts   : ${ARTIFACTS}"
echo "unity       : ${UNITY:-<none>}"
echo "dotnet      : ${DOTNET}"
echo "probe runs  : ${PROBE_RUNS} (project-owner cap: two)"
echo "stages      : ${STAGES}"
echo "suite       : ${SUITE:-<all>}"
echo "dry run     : ${DRY_RUN}"

run() {
  if (( DRY_RUN == 1 )); then
    printf '   [dry-run] %s\n' "$*"
    return 0
  fi
  printf '   %s\n' "$*"
  "$@"
}

# One Unity invocation with a watchdog and exactly one retry on a timeout (the Editor's known intermittent hang).
unity_step() {
  local label="$1"
  shift

  if (( DRY_RUN == 1 )); then
    printf '   [dry-run] timeout %s %s %s\n' "${UNITY_TIMEOUT}" "${UNITY:-<UNITY>}" "$*"
    return 0
  fi

  local attempt rc=0
  for attempt in 1 2; do
    rc=0
    timeout --signal=TERM --kill-after=60 "${UNITY_TIMEOUT}" "$@" || rc=$?
    if (( rc == 0 )); then
      return 0
    fi
    if (( rc == 124 || rc == 137 )); then
      echo "   ${label}: the Editor timed out (exit ${rc}, attempt ${attempt}/2)" >&2
      continue
    fi
    echo "   ${label}: the Editor exited ${rc}" >&2
    return "${rc}"
  done
  echo "   ${label}: timed out twice; this is not the known intermittent hang" >&2
  return 1
}

failures=0
note() { echo "$*" | tee -a "${ARTIFACTS}/static-checks.log" >/dev/null; }

# -----------------------------------------------------------------------------------------------------------
# 1. The whole solution once: this is what the evidence index resolves `dotnet:` references against, so it must be
#    an unfiltered sweep. The per-suite filtered runs below are for the matrix, not for the index.
# -----------------------------------------------------------------------------------------------------------
if stage_enabled dotnet; then
  echo
  echo "== stage: dotnet (whole solution, unfiltered) =="
  run "${DOTNET}" build dotnet/GameCore.sln -c Release --nologo -v:q || failures=$((failures + 1))
  run "${DOTNET}" test dotnet/GameCore.sln -c Release --no-build --nologo -v:q \
    --logger "trx;LogFilePrefix=gc028" --results-directory "${ARTIFACTS}/trx" || failures=$((failures + 1))
fi

# -----------------------------------------------------------------------------------------------------------
# 2. Unity: one unfiltered EditMode and one unfiltered PlayMode run. The matrix derives each suite's Unity half
#    from these two files, so the suite table's Unity column is a *view* over one authoritative run rather than 24
#    extra Editor launches.
# -----------------------------------------------------------------------------------------------------------
if stage_enabled unity; then
  echo
  echo "== stage: unity (resolve, EditMode, PlayMode) =="
  unity_step unity-resolve "${UNITY:-UNITY}" -batchmode -nographics -quit \
    -projectPath "${UNITY_PROJECT}" -logFile "${ARTIFACTS}/unity/resolve.log" || failures=$((failures + 1))
  unity_step unity-editmode "${UNITY:-UNITY}" -batchmode -nographics \
    -projectPath "${UNITY_PROJECT}" -runTests -testPlatform EditMode \
    -testResults "${ARTIFACTS}/unity/editmode-results.xml" \
    -logFile "${ARTIFACTS}/unity/editmode.log" || failures=$((failures + 1))
  unity_step unity-playmode "${UNITY:-UNITY}" -batchmode -nographics \
    -projectPath "${UNITY_PROJECT}" -runTests -testPlatform PlayMode \
    -testResults "${ARTIFACTS}/unity/playmode-results.xml" \
    -logFile "${ARTIFACTS}/unity/playmode.log" || failures=$((failures + 1))
fi

# -----------------------------------------------------------------------------------------------------------
# 3. Code generation, then the committed bytes must be unchanged: a conformance run over a moved catalog is a run
#    over a different composition revision (P-028).
# -----------------------------------------------------------------------------------------------------------
if stage_enabled codegen; then
  echo
  echo "== stage: codegen (four catalogs plus the coverage bake, then byte identity) =="
  unity_step codegen-probe "${UNITY:-UNITY}" -batchmode -nographics -quit -projectPath "${UNITY_PROJECT}" \
    -executeMethod GameCore.Validation.Editor.ProbeCatalogGenerator.GenerateCatalog \
    -logFile "${ARTIFACTS}/unity/codegen-probe.log" || failures=$((failures + 1))
  unity_step codegen-cards "${UNITY:-UNITY}" -batchmode -nographics -quit -projectPath "${UNITY_PROJECT}" \
    -executeMethod GameCore.Validation.Editor.CardCatalogGenerator.GenerateCatalog \
    -logFile "${ARTIFACTS}/unity/codegen-cards.log" || failures=$((failures + 1))
  unity_step codegen-checkpoint "${UNITY:-UNITY}" -batchmode -nographics -quit -projectPath "${UNITY_PROJECT}" \
    -executeMethod GameCore.Validation.Editor.CheckpointCatalogGenerator.GenerateCatalog \
    -logFile "${ARTIFACTS}/unity/codegen-checkpoint.log" || failures=$((failures + 1))
  unity_step codegen-traversal "${UNITY:-UNITY}" -batchmode -nographics -quit -projectPath "${UNITY_PROJECT}" \
    -executeMethod GameCore.Validation.Editor.TraversalCatalogGenerator.GenerateCatalog \
    -logFile "${ARTIFACTS}/unity/codegen-traversal.log" || failures=$((failures + 1))
  unity_step codegen-bake "${UNITY:-UNITY}" -batchmode -nographics -quit -projectPath "${UNITY_PROJECT}" \
    -executeMethod GameCore.Validation.Editor.BakeCatalogCoverageAuthoring.Bake \
    -logFile "${ARTIFACTS}/unity/codegen-bake.log" || failures=$((failures + 1))
  run git diff --exit-code -- \
    unity/GameCore.Validation/Assets/GameCore.Validation/Generated \
    unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCards \
    unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCheckpoint \
    unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedTraversal || failures=$((failures + 1))
fi

# -----------------------------------------------------------------------------------------------------------
# 4. The headless IL2CPP qualification player and every probe the suite table names, each PROBE_RUNS times.
# -----------------------------------------------------------------------------------------------------------
if stage_enabled player; then
  echo
  echo "== stage: player (StandaloneLinux64 IL2CPP, High stripping, audio disabled) =="
  run env UNITY="${UNITY:-UNITY}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" \
    "${REPO_ROOT}/tools/unity/build_probe.sh" || failures=$((failures + 1))

  # bash 3.2 (macOS) has no associative arrays; a newline-delimited string is enough here.
  seen_harness=""
  for row in "${SUITES[@]}"; do
    IFS='|' read -r id _ _ probe_field <<< "${row}"
    if [[ -n "${SUITE}" && "${id}" != "${SUITE}" ]]; then
      continue
    fi
    IFS=',' read -r -a harnesses <<< "${probe_field}" || true
    for entry in ${harnesses[@]+"${harnesses[@]}"}; do
      [[ -z "${entry}" ]] && continue
      # `<harness>[:<Mode>[:<args>]]`. Two entries sharing harness+args are one invocation contributing two
      # mode lookups (run_probe.sh runs its positive and its missing-registration mode in one process).
      harness="${entry%%:*}"
      rest="${entry#*:}"
      args="${rest#*:}"
      if [[ "${rest}" == "${entry}" ]]; then args=""; fi
      key="${harness}|${args}"
      case "${seen_harness}" in
        *"|${key}|"*) continue ;;
      esac
      seen_harness="${seen_harness}|${key}|"
      if [[ ! -f "${REPO_ROOT}/tools/unity/${harness}" ]]; then
        echo "   FAIL: the suite table names a harness this repository does not have: tools/unity/${harness}" >&2
        failures=$((failures + 1))
        continue
      fi
      if [[ -n "${args}" ]]; then
        run env PROBE_PLAYER="${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64" \
          UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" PROBE_RUNS="${PROBE_RUNS}" \
          "${REPO_ROOT}/tools/unity/${harness}" ${args} || failures=$((failures + 1))
      else
        run env PROBE_PLAYER="${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64" \
          UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" PROBE_RUNS="${PROBE_RUNS}" \
          "${REPO_ROOT}/tools/unity/${harness}" || failures=$((failures + 1))
      fi
    done
  done
fi

# -----------------------------------------------------------------------------------------------------------
# 5. The marker-free release shape, when asked for.
# -----------------------------------------------------------------------------------------------------------
if stage_enabled release && [[ "${RELEASE_BUILD}" == "1" ]]; then
  echo
  echo "== stage: release (marker-free clone, player, kept modes) =="
  run "${PYTHON}" tools/check_release_fault_free.py --dotnet "${DOTNET}" \
    --json "${ARTIFACTS}/release-fault-surface.json" || failures=$((failures + 1))
  run "${PYTHON}" tools/check_release_telemetry_free.py --dotnet "${DOTNET}" --artifacts "${ARTIFACTS}" \
    --json "${ARTIFACTS}/release-telemetry-surface.json" || failures=$((failures + 1))
  run "${PYTHON}" tools/unity/prepare_gc017_release_project.py || failures=$((failures + 1))
  run "${PYTHON}" tools/check_link_xml.py --project "${REPO_ROOT}/unity/GameCore.ReleaseCheck" \
    --json "${ARTIFACTS}/release/link-xml.json" || failures=$((failures + 1))
  run "${PYTHON}" tools/check_release_clone.py --clone "${REPO_ROOT}/unity/GameCore.ReleaseCheck" \
    --json "${ARTIFACTS}/release/clone-surface.json" || failures=$((failures + 1))
  run env UNITY="${UNITY:-UNITY}" UNITY_PROJECT="${REPO_ROOT}/unity/GameCore.ReleaseCheck" \
    ARTIFACTS="${ARTIFACTS}/release" "${REPO_ROOT}/tools/unity/build_probe.sh" || failures=$((failures + 1))
  run "${PYTHON}" tools/check_player_fault_free.py --player "${REPO_ROOT}/unity/GameCore.ReleaseCheck/Builds/Linux64" \
    --json "${ARTIFACTS}/release-player-surface.json" || failures=$((failures + 1))
  run "${PYTHON}" tools/check_release_gate_free.py \
    --player "${REPO_ROOT}/unity/GameCore.ReleaseCheck/Builds/Linux64" \
    --qualification "${UNITY_PROJECT}/Builds/Linux64" \
    --json "${ARTIFACTS}/release-gate-surface.json" || failures=$((failures + 1))
  run "${PYTHON}" tools/check_release_telemetry_free.py --dotnet "${DOTNET}" --artifacts "${ARTIFACTS}" \
    --release-player-project "${REPO_ROOT}/unity/GameCore.ReleaseCheck" \
    --json "${ARTIFACTS}/release/telemetry-release-surface.json" || failures=$((failures + 1))
  run env PROBE_PLAYER="${REPO_ROOT}/unity/GameCore.ReleaseCheck/Builds/Linux64/GameCoreProbe.x86_64" \
    UNITY_PROJECT="${REPO_ROOT}/unity/GameCore.ReleaseCheck" ARTIFACTS="${ARTIFACTS}/release" \
    PROBE_RUNS="${PROBE_RUNS}" PROBE_SHAPE=release "${REPO_ROOT}/tools/unity/run_catalog_coverage_probe.sh" \
    || failures=$((failures + 1))
  run env PROBE_PLAYER="${REPO_ROOT}/unity/GameCore.ReleaseCheck/Builds/Linux64/GameCoreProbe.x86_64" \
    UNITY_PROJECT="${REPO_ROOT}/unity/GameCore.ReleaseCheck" ARTIFACTS="${ARTIFACTS}/release" \
    PROBE_RUNS="${PROBE_RUNS}" "${REPO_ROOT}/tools/unity/run_recovery_smoke_probe.sh" \
    || failures=$((failures + 1))
fi

# -----------------------------------------------------------------------------------------------------------
# 6. Host-side checks. Their combined stdout is the log the evidence index reads for the parity and catalog
#    byte-identity markers, so it is written to a file *and* echoed.
# -----------------------------------------------------------------------------------------------------------
if stage_enabled host; then
  echo
  echo "== stage: host-side checks (no Editor, no player) =="
  : > "${ARTIFACTS}/static-checks.log"

  host_check() {
    local label="$1"
    shift
    {
      echo
      echo "== ${label} =="
    } >> "${ARTIFACTS}/static-checks.log"
    if (( DRY_RUN == 1 )); then
      printf '   [dry-run] %s\n' "$*"
      return 0
    fi
    if "$@" >> "${ARTIFACTS}/static-checks.log" 2>&1; then
      echo "   ${label}: pass"
    else
      echo "   ${label}: FAIL" >&2
      failures=$((failures + 1))
    fi
  }

  host_check csharp-shape "${PYTHON}" tools/check_game_core_csharp.py
  host_check contract-surface-parity "${PYTHON}" tools/check_contract_surface_parity.py
  host_check catalog-emitter-mirror "${PYTHON}" tools/emit_generated_catalog.py --self-check
  host_check catalog-byte-identity "${PYTHON}" tools/verify_generated_catalog.py \
    unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs
  host_check reachability-manifest "${PYTHON}" tools/emit_catalog_reachability.py --check
  host_check baked-coverage-artifact "${PYTHON}" tools/emit_baked_catalog_coverage.py --check
  host_check link-xml "${PYTHON}" tools/check_link_xml.py --project "${UNITY_PROJECT}" \
    --json "${ARTIFACTS}/host/link-xml-qualification.json"
  host_check budget-record "${PYTHON}" tools/check_budget_record.py --json "${ARTIFACTS}/host/budget-record.json"
  host_check gate-sources "${PYTHON}" tools/check_gate_sources.py --json "${ARTIFACTS}/host/gate-sources.json"
  host_check release-clone-self-test "${PYTHON}" tools/check_release_clone.py --self-test
  host_check genre-audit "${PYTHON}" tools/gc024_genre_audit.py --root "${REPO_ROOT}" \
    --out "artifacts/gc-024/genre-audit.json"
  host_check native-leak-attribution "${PYTHON}" tools/attribute_native_leaks.py \
    --log "${ARTIFACTS}/unity/editmode.log" --log "${ARTIFACTS}/unity/playmode.log" \
    --out "${ARTIFACTS}/toolchain/native-leak-attribution.json"
  for script in tools/conformance/run_test_matrix.sh tools/conformance/build_evidence_index.py \
                tools/conformance/build_compatibility.py; do
    if [[ "${script}" == *.py ]]; then
      host_check "py-compile $(basename "${script}")" "${PYTHON}" -m py_compile "${script}"
    else
      host_check "shell-parse $(basename "${script}")" bash -n "${script}"
    fi
  done
fi

if stage_enabled docs; then
  echo
  echo "== stage: documentation validator =="
  run "${PYTHON}" tools/validate_game_core_docs.py --self-test || failures=$((failures + 1))
  run "${PYTHON}" tools/validate_game_core_docs.py || failures=$((failures + 1))
  if (( DRY_RUN == 0 )); then
    "${PYTHON}" tools/validate_game_core_docs.py --self-test >"${ARTIFACTS}/validator-self-test.log" 2>&1 || true
    "${PYTHON}" tools/validate_game_core_docs.py >"${ARTIFACTS}/validator.log" 2>&1 || true
  fi
fi

# -----------------------------------------------------------------------------------------------------------
# 7. The per-suite matrix: read each suite's verdict out of the files the stages above produced.
# -----------------------------------------------------------------------------------------------------------
echo
echo "== per-suite matrix =="
if (( DRY_RUN == 1 )); then
  echo "   [dry-run] would derive suite-status.json from the result files under ${ARTIFACTS}"
else
  SUITE_JSON="${ARTIFACTS}/suite-status.json" \
  ARTIFACTS="${ARTIFACTS}" REPO_ROOT="${REPO_ROOT}" SUITE="${SUITE}" PROBE_RUNS="${PROBE_RUNS}" \
  "${PYTHON}" "${SCRIPT_DIR}/summarize_suite_matrix.py" "${SUITES[@]}"
fi

# -----------------------------------------------------------------------------------------------------------
# 8. The evidence index and the compatibility report, built FROM the results this run produced.
# -----------------------------------------------------------------------------------------------------------
echo
echo "== evidence index and compatibility report =="
if (( DRY_RUN == 1 )); then
  echo "   [dry-run] ${PYTHON} tools/conformance/build_evidence_index.py --evidence-root ${ARTIFACTS} --check"
  echo "   [dry-run] ${PYTHON} tools/conformance/build_compatibility.py --evidence-root ${ARTIFACTS} --out-dir ${ARTIFACTS}"
else
  "${PYTHON}" "${SCRIPT_DIR}/build_evidence_index.py" --evidence-root "${ARTIFACTS}" \
    --out-dir "${ARTIFACTS}" --check || failures=$((failures + 1))
  "${PYTHON}" "${SCRIPT_DIR}/build_compatibility.py" --evidence-root "${ARTIFACTS}" \
    --out-dir "${ARTIFACTS}" || failures=$((failures + 1))
fi

echo
if (( failures != 0 )); then
  echo "== GC-028 conformance matrix FAILED (${failures} problem(s)) ==" >&2
  exit 1
fi
echo "== GC-028 conformance matrix completed =="
echo "suite matrix   : ${ARTIFACTS}/suite-status.json"
echo "evidence index : ${ARTIFACTS}/evidence-index.json"
echo "compatibility  : ${ARTIFACTS}/compatibility.json"
echo "note: every status above was read from a result file; a missing result is NotRun, never Pass."

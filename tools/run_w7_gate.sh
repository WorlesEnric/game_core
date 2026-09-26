#!/usr/bin/env bash
# Wave 7 integration gate (W7-GATE).
#
# The exit gate this script serves, verbatim from `docs/game-core/09-implementation-guide.md` (Wave 7):
#
#   "All reference transition tables and cross-template flow pass; complete IL2CPP/headless catalog coverage runs;
#    faulted checkpoint/outbox recovery passes; benchmark data and budget decisions are recorded. Production fixes
#    require affected gates rerun on the new revision."
#
# It runs, in order:
#   1. the whole plain-dotnet solution: build, then test EVERY test project, which is how the merged revision's
#      incremental-versus-full derivation equivalence suites (GC-012/TEST-008, including the 10,000-target local-edit
#      property GC-026's kernel change is answerable to) are re-run on the merged kernel;
#   2. the host-side static checks: the C# shape checker, this gate's own source invariants, the frozen contract
#      surface, the committed generated catalogs, the reachability manifest and bake mirrors, the budget decision
#      record (the document half of "benchmark data and budget decisions are recorded"), and the shell parse;
#   3. Unity package resolution on the qualification project;
#   4. the Unity EditMode suite of EVERY testable package — W1..W7 gate assemblies and every task suite, unfiltered,
#      so a Wave 7 task's own tests participate without being named here;
#   5. the Unity PlayMode suite of the same set;
#   6. build-time code generation for all four committed catalogs plus the coverage bake, then `git diff --exit-code`
#      over the four generated trees: the committed bytes must be exactly what the pinned Editor produces;
#   7. the StandaloneLinux64 IL2CPP qualification player;
#   8. EVERY player probe the repository has, each PROBE_RUNS times (default 2, the project-owner's cap): GC-001
#      positive and expected-negative, world dispatch, the W1..W6 gates, the three genre reference probes, GC-013,
#      GC-017 faults, GC-018/GC-019/GC-021, GC-022 lifecycle stress, GC-023 replay, GC-025 catalog coverage, this
#      gate's own `-probeW7Gate`, and GC-027's `-probeRecovery`;
#   9. the marker-free release shape: prepare the clone, check the clone's own invariants, build the release player,
#      inspect both players for the union of qualification-only markers, and run the modes a shipping build keeps —
#      the family probes, `-probeCatalogCoverage` (the release half of the coverage clause) and the NEW
#      `-probeRecoverySmoke` (production recovery from a real file checkpoint, no fault latches) — each PROBE_RUNS
#      times;
#  10. ONE short benchmark correctness diagnostic: the correctness gates of the benchmark fixture are asserted here
#      and its timings are recorded, while the full-duration TEST-023 p95/p99 catalogue is NOT run — that
#      qualification is Deferred by project-owner decision, and `artifacts/performance/BUDGET_DECISIONS.md` says so;
#  11. the documentation validator.
#
# AUDIO. The headless player runs with Unity audio DISABLED (an FMOD/PulseAudio crash at exit, crash-139). Nothing in
# this gate re-enables it. Every Unity Editor invocation and every player launch is wrapped in `timeout`, and every
# Unity invocation this script launches itself is retried exactly ONCE when it timed out, logging the retry. A step
# that times out twice fails the gate.
#
# PROJECT-OWNER DECISIONS THIS SCRIPT ENCODES.
#   * PROBE_RUNS is capped at two: repeated runs are capped at TWO, so the default is 2 rather than 5.
#   * Full-duration performance timing (TEST-023 p95/p99 catalogues) is NOT required. "Benchmark data and budget
#     decisions are recorded" is satisfied by GC-026's diagnostic data plus the budget decision record with the
#     timing qualification marked deferred; this gate runs only the short benchmark correctness diagnostic once.
#
# Required environment:
#   UNITY   absolute path to the Unity Editor executable of the pinned 6000.0.75f1 baseline
#
# Optional environment:
#   DOTNET             .NET 8 SDK executable (default: dotnet on PATH)
#   PYTHON             python3 executable (default: python3 on PATH)
#   UNITY_PROJECT      Unity project path (default: <repo>/unity/GameCore.Validation)
#   ARTIFACTS          artifact directory (default: <repo>/artifacts/w7-gate)
#   PROBE_RUNS         repetitions of every player probe (default 2; any crashing run fails the gate)
#   UNITY_TIMEOUT      seconds a full Unity Editor invocation may take (default 1800)
#   RELEASE_BUILD      build and inspect a marker-free release player (default 1; 0 skips it and says so)
#   RELEASE_PROJECT    the disposable cloned project path (default: <repo>/unity/GameCore.ReleaseCheck)
#   RELEASE_PLAYER     an existing marker-free release player to inspect instead of building one
#   BENCH_*            the short diagnostic's knobs; defaults are the recorded short-run shape
#   DOCS               run step 11 (default 1; 0 skips it and says so)
#
# Exit codes: 0 every step passed; nonzero on the first failing step (2 for a missing prerequisite).
set -euo pipefail

if [[ -z "${UNITY:-}" ]]; then
  echo "run_w7_gate.sh: UNITY must name the pinned Unity Editor executable (6000.0.75f1)" >&2
  exit 2
fi
if [[ ! -x "${UNITY}" ]]; then
  echo "run_w7_gate.sh: UNITY is not executable: ${UNITY}" >&2
  exit 2
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
PYTHON="${PYTHON:-python3}"
UNITY_PROJECT="${UNITY_PROJECT:-${REPO_ROOT}/unity/GameCore.Validation}"
ARTIFACTS="${ARTIFACTS:-${REPO_ROOT}/artifacts/w7-gate}"
PROBE_PLAYER="${UNITY_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64"
PROBE_RUNS="${PROBE_RUNS:-2}"
UNITY_TIMEOUT="${UNITY_TIMEOUT:-1800}"
RELEASE_BUILD="${RELEASE_BUILD:-1}"
RELEASE_PROJECT="${RELEASE_PROJECT:-${REPO_ROOT}/unity/GameCore.ReleaseCheck}"
RELEASE_PLAYER="${RELEASE_PLAYER:-}"
DOCS="${DOCS:-1}"

# The short benchmark correctness diagnostic's shape: one run, one second of warmup, two-second windows, five
# repetitions of each change workload. It is the shape `artifacts/gc-026/correctness-final-round2` was recorded with,
# and it exists to assert the correctness gates and record a number, NOT to qualify a p95 target.
BENCH_RUNS="${BENCH_RUNS:-1}"
BENCH_WARMUP="${BENCH_WARMUP:-1}"
BENCH_DURATION="${BENCH_DURATION:-2}"
BENCH_REPETITIONS="${BENCH_REPETITIONS:-5}"
BENCH_TIMEOUT="${BENCH_TIMEOUT:-1800}"

# One absolute artifact root: Unity resolves a relative `-logFile` against its own working directory while this script
# re-opens the same path against the repository root, so a relative ARTIFACTS silently separates the two.
ARTIFACTS="$(realpath -m "${ARTIFACTS}")"
mkdir -p "${ARTIFACTS}/trx" "${ARTIFACTS}/unity" "${ARTIFACTS}/toolchain" "${ARTIFACTS}/release" "${ARTIFACTS}/host" \
  "${ARTIFACTS}/probe" "${ARTIFACTS}/benchmark"
cd "${REPO_ROOT}"

echo "== Wave 7 integration gate (W7-GATE) =="
echo "repo         : ${REPO_ROOT}"
echo "unity        : ${UNITY}"
echo "dotnet       : ${DOTNET}"
echo "project      : ${UNITY_PROJECT}"
echo "artifacts    : ${ARTIFACTS}"
echo "probe runs   : ${PROBE_RUNS} (project-owner cap: two)"
echo "unity timeout: ${UNITY_TIMEOUT}s (one retry on a timeout)"
echo "release build: ${RELEASE_BUILD}"
echo "benchmark    : ONE short correctness diagnostic (${BENCH_RUNS} run, ${BENCH_WARMUP}s warmup, ${BENCH_DURATION}s windows)"
echo "               TEST-023 full-duration timing is Deferred by project-owner decision; see BUDGET_DECISIONS.md"
echo "audio        : DISABLED in the headless player (crash-139)"

run_step() {
  local label="$1"
  shift
  echo "-- ${label}: $*"
  "$@"
}

# One Unity Editor invocation, wrapped in `timeout` and retried exactly once when it times out. A timeout that
# happens twice is a failure; every other non-zero exit is a failure immediately (a real compile or test error is
# never retried).
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

# One player probe sequence, retried exactly once when the SEQUENCE timed out (never on a probe verdict): probe_runs.sh
# already repeats each probe PROBE_RUNS times with its own per-run timeout and fails on any dirty run.
probe_step() {
  local label="$1"
  shift

  local attempt rc=0
  for attempt in 1 2; do
    if (( attempt == 2 )); then
      echo "-- ${label}: retrying once after a timeout" >&2
    fi

    rc=0
    "$@" || rc=$?
    if (( rc == 0 )); then
      return 0
    fi

    if (( rc == 124 || rc == 137 )); then
      echo "   FAIL ${label}: the probe sequence timed out (exit ${rc}, attempt ${attempt}/2)" >&2
      continue
    fi

    echo "   FAIL ${label}: exited ${rc}" >&2
    return "${rc}"
  done

  echo "   FAIL ${label}: timed out twice" >&2
  return 1
}

# --------------------------------------------------------------------------------------------------------------
# 1. The pure half of the repository: every Unity-free assembly, every test project.
# --------------------------------------------------------------------------------------------------------------

echo
echo "== 1. plain dotnet (every test project, so every earlier gate's equivalence suites re-run) =="

run_step dotnet-build "${DOTNET}" build dotnet/GameCore.sln -c Release --nologo -v:q
run_step dotnet-test "${DOTNET}" test dotnet/GameCore.sln -c Release --no-build --nologo -v:q \
  --logger "trx;LogFilePrefix=w7" --results-directory "${ARTIFACTS}/trx"

# The two suites the Wave 7 exit gate names explicitly, run a second time BY NAME so this gate's own log carries
# their verdict rather than leaving it inside the whole-solution run: GC-012/TEST-008's incremental-versus-full
# agreement suites (`Packages/com.gamecore.derivation/Tests/Differential`) and the 10,000-target local-edit property
# GC-026's kernel change must still satisfy. A filter that matched no test would leave these steps vacuous, so the
# loop below requires each run's own trx to exist and to carry results.
run_step dotnet-derivation-equivalence "${DOTNET}" test dotnet/tests/GameCore.Derivation.Tests/GameCore.Derivation.Tests.csproj \
  -c Release --no-build --nologo -v:q \
  --filter "FullyQualifiedName~GameCore.Derivation.Tests.IncrementalAgreementTests|FullyQualifiedName~GameCore.Derivation.Tests.OracleAgreementTests" \
  --logger "trx;LogFilePrefix=w7-equivalence" --results-directory "${ARTIFACTS}/trx"
run_step dotnet-10k-property "${DOTNET}" test dotnet/tests/GameCore.Benchmarks.Tests/GameCore.Benchmarks.Tests.csproj \
  -c Release --no-build --nologo -v:q \
  --filter "FullyQualifiedName~BenchmarkFixtureDerivationTests" \
  --logger "trx;LogFilePrefix=w7-ten-thousand" --results-directory "${ARTIFACTS}/trx"
for prefix in w7-equivalence w7-ten-thousand; do
  matched=0
  for trx in "${ARTIFACTS}/trx/${prefix}"*.trx; do
    if [[ -f "${trx}" ]]; then
      matched=1
      if ! grep -q "UnitTestResult" "${trx}"; then
        echo "   FAIL ${prefix}: ${trx} contains no test results" >&2
        exit 1
      fi
      echo "-- named suite re-run: ${trx}"
    fi
  done
  if (( matched == 0 )); then
    echo "   FAIL ${prefix}: no result file was written; the filter matched no test" >&2
    exit 1
  fi
done

# --------------------------------------------------------------------------------------------------------------
# 2. Host-side checks: no Editor, no player.
# --------------------------------------------------------------------------------------------------------------

echo
echo "== 2. host-side checks =="

run_step csharp-check "${PYTHON}" tools/check_game_core_csharp.py
run_step gate-sources "${PYTHON}" tools/check_gate_sources.py \
  --file unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/W7GateScenario.cs \
  --file unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/ProbeW7Gate.cs \
  --file unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/ProbeRecoverySmoke.cs \
  --file unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/Gc027Scenario.cs \
  --file unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/ProbeArguments.cs \
  --file unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/ProbeRunner.cs \
  --file unity/GameCore.Validation/Assets/GameCore.Validation/Tests/W7Gate/W7GateIntegrationTests.cs \
  --json "${ARTIFACTS}/host/gate-sources.json"
run_step contract-surface-parity "${PYTHON}" tools/check_contract_surface_parity.py
run_step generated-catalog-probe "${PYTHON}" tools/verify_generated_catalog.py \
  unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs
run_step generated-catalog-cards "${PYTHON}" tools/verify_generated_catalog.py \
  unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCards/CardCatalog.g.cs
run_step generated-catalog-checkpoint "${PYTHON}" tools/verify_generated_catalog.py \
  unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCheckpoint/CheckpointCatalog.g.cs
run_step generated-catalog-traversal "${PYTHON}" tools/verify_generated_catalog.py \
  unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedTraversal/TraversalCatalog.g.cs
run_step link-xml-qualification "${PYTHON}" tools/check_link_xml.py \
  --project "${UNITY_PROJECT}" --json "${ARTIFACTS}/host/link-xml-qualification.json"
run_step catalog-emitter-mirror "${PYTHON}" tools/emit_generated_catalog.py --self-check
run_step reachability-manifest "${PYTHON}" tools/emit_catalog_reachability.py --check
run_step baked-coverage-artifact "${PYTHON}" tools/emit_baked_catalog_coverage.py --check
run_step fingerprint-tool-self-test "${PYTHON}" tools/compare_registration_fingerprints.py --self-test
# The document half of "benchmark data and budget decisions are recorded": the ten rows of the record, their
# decisions, their evidence paths, and the project-owner deferral of the full-duration timing qualification.
run_step budget-record "${PYTHON}" tools/check_budget_record.py --json "${ARTIFACTS}/host/budget-record.json"
# The clone checker's own two directions, on a synthetic post-import clone: a generated-tree-only defect set must
# pass and a real stale reference in the clone's own sources must fail. Proved here rather than asserted, because
# this host has no Unity to produce the generated trees the check has to ignore.
run_step release-clone-self-test "${PYTHON}" tools/check_release_clone.py --self-test
for script in \
  tools/run_w7_gate.sh \
  tools/unity/run_w7_gate_probe.sh \
  tools/unity/run_recovery_smoke_probe.sh \
  tools/unity/run_conformance_probe.sh \
  tools/unity/run_w6_gate_probe.sh \
  tools/unity/build_probe.sh; do
  run_step "shell-parse $(basename "${script}")" bash -n "${script}"
done

# --------------------------------------------------------------------------------------------------------------
# 3-5. Unity resolve, EditMode and PlayMode over every testable package.
# --------------------------------------------------------------------------------------------------------------

echo
echo "== 3. Unity resolve =="
unity_step unity-resolve "${UNITY}" -batchmode -nographics -quit \
  -projectPath "${UNITY_PROJECT}" \
  -logFile "${ARTIFACTS}/unity/resolve.log"

echo
echo "== 4. Unity EditMode (every testable package plus every gate assembly, W1..W7) =="
# No -assemblyNames and no -quit: the runner discovers every test assembly the manifest's `testables` names, which is
# what makes a Wave 7 task's suite participate without being listed here (04 s10 forbids -quit on a test run).
unity_step unity-editmode "${UNITY}" \
  -batchmode -nographics \
  -projectPath "${UNITY_PROJECT}" \
  -runTests -testPlatform EditMode \
  -testResults "${ARTIFACTS}/unity/editmode-results.xml" \
  -logFile "${ARTIFACTS}/unity/editmode.log"

echo
echo "== 5. Unity PlayMode =="
unity_step unity-playmode "${UNITY}" \
  -batchmode -nographics \
  -projectPath "${UNITY_PROJECT}" \
  -runTests -testPlatform PlayMode \
  -testResults "${ARTIFACTS}/unity/playmode-results.xml" \
  -logFile "${ARTIFACTS}/unity/playmode.log"

# --------------------------------------------------------------------------------------------------------------
# 6. Code generation reproducibility.
# --------------------------------------------------------------------------------------------------------------

echo
echo "== 6. code generation and byte identity =="

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
# 7-8. Qualification player, every probe.
# --------------------------------------------------------------------------------------------------------------

echo
echo "== 7. qualification player =="
UNITY="${UNITY}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" \
  UNITY_TIMEOUT="${UNITY_TIMEOUT}" DOTNET="${DOTNET}" "tools/unity/build_probe.sh"

echo
echo "== 8. every player probe, ${PROBE_RUNS} run(s) each =="
probe_step probe-gc001 \
  env PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" \
  PROBE_RUNS="${PROBE_RUNS}" "tools/unity/run_probe.sh" both
for harness in \
  run_world_probe.sh \
  run_w1_gate_probe.sh \
  run_w2_gate_probe.sh \
  run_w3_gate_probe.sh \
  run_w4_profile_probe.sh \
  run_narrative_probe.sh \
  run_cards_probe.sh \
  run_gc013_probe.sh \
  run_w4_gate_probe.sh \
  run_gc017_faults_probe.sh \
  run_gc018_probe.sh \
  run_gc019_probe.sh \
  run_traversal_probe.sh \
  run_w5_gate_probe.sh \
  run_gc021_probe.sh \
  run_lifecycle_stress_probe.sh \
  run_replay_probe.sh \
  run_w6_gate_probe.sh \
  run_catalog_coverage_probe.sh \
  run_recovery_probe.sh \
  run_conformance_probe.sh \
  run_w7_gate_probe.sh; do
  probe_step "probe-${harness}" \
    env PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/toolchain" \
    PROBE_RUNS="${PROBE_RUNS}" "tools/unity/${harness}"
done

# --------------------------------------------------------------------------------------------------------------
# 9. The marker-free release shape.
# --------------------------------------------------------------------------------------------------------------

echo
echo "== 9. release shape =="
run_step release-fault-surface "${PYTHON}" tools/check_release_fault_free.py \
  --dotnet "${DOTNET}" --json "${ARTIFACTS}/release-fault-surface.json"
run_step release-telemetry-surface "${PYTHON}" tools/check_release_telemetry_free.py \
  --dotnet "${DOTNET}" --artifacts "${ARTIFACTS}" --json "${ARTIFACTS}/release-telemetry-surface.json"

if [[ -n "${RELEASE_PLAYER}" ]]; then
  echo "-- release-player: using the supplied player ${RELEASE_PLAYER}"
elif [[ "${RELEASE_BUILD}" == "1" ]]; then
  if [[ -e "${RELEASE_PROJECT}" ]]; then
    echo "   FAIL release-project: ${RELEASE_PROJECT} already exists; remove the disposable clone first" >&2
    exit 1
  fi

  run_step release-project-prepare "${PYTHON}" tools/unity/prepare_gc017_release_project.py
  run_step link-xml-release "${PYTHON}" tools/check_link_xml.py \
    --project "${RELEASE_PROJECT}" --json "${ARTIFACTS}/release/link-xml.json"
  run_step release-clone-check "${PYTHON}" tools/check_release_clone.py \
    --clone "${RELEASE_PROJECT}" --json "${ARTIFACTS}/release/clone-surface.json"
  UNITY="${UNITY}" UNITY_PROJECT="${RELEASE_PROJECT}" ARTIFACTS="${ARTIFACTS}/release" \
    UNITY_TIMEOUT="${UNITY_TIMEOUT}" DOTNET="${DOTNET}" "tools/unity/build_probe.sh"
  RELEASE_PLAYER="${RELEASE_PROJECT}/Builds/Linux64/GameCoreProbe.x86_64"
else
  echo "-- release-player: NOT RUN (RELEASE_BUILD=0 and no RELEASE_PLAYER)"
  echo "   the source and compiled-assembly halves of the release claim did run; this note is deliberate so the"
  echo "   gate never implies a built release player was inspected when none was supplied."
fi

if [[ -n "${RELEASE_PLAYER}" ]]; then
  if [[ ! -x "${RELEASE_PLAYER}" ]]; then
    echo "   FAIL release-player: ${RELEASE_PLAYER} is not an executable file" >&2
    exit 1
  fi

  run_step release-player-latches "${PYTHON}" tools/check_player_fault_free.py \
    --player "$(dirname "${RELEASE_PLAYER}")" --json "${ARTIFACTS}/release-player-surface.json"

  # The union of qualification-only markers: the latches, GC-021's seat, GC-022's stress, GC-023's replay fixture,
  # the Wave 5 gate, the Wave 6 gate and this gate must be ABSENT from the release player, and the qualification
  # player must show each group's marker so the scan is falsifiable rather than merely quiet. The modes the clone
  # deliberately KEEPS (the traversal course, catalog coverage and this gate's new recovery smoke) must be present in
  # both players, which is the other half of that argument.
  run_step release-gate-surface "${PYTHON}" tools/check_release_gate_free.py \
    --player "$(dirname "${RELEASE_PLAYER}")" \
    --qualification "$(dirname "${PROBE_PLAYER}")" \
    --json "${ARTIFACTS}/release-gate-surface.json"

  run_step release-telemetry-player "${PYTHON}" tools/check_release_telemetry_free.py \
    --dotnet "${DOTNET}" --artifacts "${ARTIFACTS}" \
    --release-player-project "${RELEASE_PROJECT}" \
    --json "${ARTIFACTS}/release/telemetry-release-surface.json"

  # The modes a shipping build keeps. The family probes are the ones the pre-Wave-6 gates already drove in a release
  # player; `-probeCatalogCoverage` is GC-025's release-shape coverage run (the release half of the coverage clause);
  # `-probeRecoverySmoke` is this gate's NEW release-kept recovery smoke, which drives the production
  # `WorldRecovery.Recover`/`Restart` with a real file checkpoint and no fault latches.
  probe_step release-probe-catalog-coverage \
    env PROBE_PLAYER="${RELEASE_PLAYER}" UNITY_PROJECT="${RELEASE_PROJECT}" ARTIFACTS="${ARTIFACTS}/release" \
    PROBE_RUNS="${PROBE_RUNS}" PROBE_SHAPE=release "tools/unity/run_catalog_coverage_probe.sh"
  for harness in \
    run_world_probe.sh \
    run_narrative_probe.sh \
    run_cards_probe.sh \
    run_gc018_probe.sh \
    run_gc019_probe.sh \
    run_traversal_probe.sh; do
    probe_step "release-probe-${harness}" \
      env PROBE_PLAYER="${RELEASE_PLAYER}" UNITY_PROJECT="${RELEASE_PROJECT}" ARTIFACTS="${ARTIFACTS}/release" \
      PROBE_RUNS="${PROBE_RUNS}" "tools/unity/${harness}"
  done
  probe_step release-probe-recovery-smoke \
    env PROBE_PLAYER="${RELEASE_PLAYER}" UNITY_PROJECT="${RELEASE_PROJECT}" ARTIFACTS="${ARTIFACTS}/release" \
    PROBE_RUNS="${PROBE_RUNS}" "tools/unity/run_recovery_smoke_probe.sh"
fi

# --------------------------------------------------------------------------------------------------------------
# 10. ONE short benchmark correctness diagnostic (the timing qualification is deferred by project-owner decision).
# --------------------------------------------------------------------------------------------------------------

echo
echo "== 10. short benchmark correctness diagnostic =="
echo "   NOT the full-duration TEST-023 catalogue: the p95/p99 qualification is Deferred by project-owner decision,"
echo "   recorded in artifacts/performance/BUDGET_DECISIONS.md. This single short run asserts the correctness gates"
echo "   and records its numbers; the summarizer reports every row it cannot fill as NotMeasured rather than as a pass."
# The harness's OWN exit is two things at once: it fails when a player run was not clean (a real gate failure), and
# it exits with the summarizer's status, which is nonzero for a provisional timing miss on a short run — the recorded
# short diagnostic behaves exactly that way and is NOT a gate failure. So the verdict is taken from the retained
# artifacts rather than from the exit code: the player must have reported Pass with no failing step on every run, and
# the summarizer must have written its two outputs. Both exit codes are printed as recorded facts.
bench_rc=0
BENCH_RUNS="${BENCH_RUNS}" BENCH_WARMUP="${BENCH_WARMUP}" BENCH_DURATION="${BENCH_DURATION}" \
  BENCH_REPETITIONS="${BENCH_REPETITIONS}" BENCH_TIMEOUT="${BENCH_TIMEOUT}" \
  PROBE_PLAYER="${PROBE_PLAYER}" UNITY_PROJECT="${UNITY_PROJECT}" ARTIFACTS="${ARTIFACTS}/benchmark" \
  "tools/run_benchmarks.sh" || bench_rc=$?
echo "-- benchmark harness exit: ${bench_rc} (advisory; the verdict below is read from the retained artifacts)"

bench_problems=0
for (( run = 1; run <= BENCH_RUNS; run++ )); do
  bench_result="${ARTIFACTS}/benchmark/raw/run${run}/probe-benchmark.json"
  if [[ ! -f "${bench_result}" ]]; then
    echo "   FAIL benchmark: run ${run} wrote no result: ${bench_result}" >&2
    bench_problems=$((bench_problems + 1))
    continue
  fi
  if ! grep -q '"result": "Pass"' "${bench_result}"; then
    echo "   FAIL benchmark: run ${run} did not report Pass" >&2
    bench_problems=$((bench_problems + 1))
  fi
  if grep -q '"status": "Fail"' "${bench_result}"; then
    echo "   FAIL benchmark: run ${run} reported a failing step" >&2
    bench_problems=$((bench_problems + 1))
  fi
  echo "-- benchmark run ${run}: Pass (correctness gates asserted in the player)"
done
for artifact in "${ARTIFACTS}/benchmark/summary.md" "${ARTIFACTS}/benchmark/summarize.json"; do
  if [[ ! -s "${artifact}" ]]; then
    echo "   FAIL benchmark: the summarizer wrote no ${artifact}" >&2
    bench_problems=$((bench_problems + 1))
  fi
done
if (( bench_problems != 0 )); then
  echo "   FAIL benchmark: ${bench_problems} problem(s) in the short diagnostic" >&2
  exit 1
fi
echo "   note: the summarizer's provisional timing miss (if any) is recorded, not waived: the full-duration TEST-023"
echo "   qualification is Deferred by project-owner decision in artifacts/performance/BUDGET_DECISIONS.md, and"
echo "   tools/check_budget_record.py is what asserts that record rather than this step inventing a target."

# --------------------------------------------------------------------------------------------------------------
# 11. Documentation.
# --------------------------------------------------------------------------------------------------------------

if [[ "${DOCS}" == "1" ]]; then
  echo
  echo "== 11. documentation validator =="
  "${PYTHON}" tools/validate_game_core_docs.py --self-test | tee "${ARTIFACTS}/validator-self-test.log"
  "${PYTHON}" tools/validate_game_core_docs.py | tee "${ARTIFACTS}/validator.log"
else
  echo
  echo "-- documentation validator: NOT RUN (DOCS=0)"
fi

echo
echo "== Wave 7 integration gate PASSED =="
echo "qualification player : ${PROBE_PLAYER}"
echo "release player       : ${RELEASE_PLAYER:-<not built>}"
echo "gate result          : ${ARTIFACTS}/toolchain/probe-w7-gate.json (this gate)"
echo "                       ${ARTIFACTS}/toolchain/probe-conformance.json (GC-024) + toolchain/traces/"
echo "                       ${ARTIFACTS}/toolchain/probe-catalog-coverage.json, probe-catalog-coverage-release.json (GC-025)"
echo "                       ${ARTIFACTS}/release/probe-recovery-smoke.json (GC-027, release shape)"
echo "test results         : ${ARTIFACTS}/unity/editmode-results.xml, ${ARTIFACTS}/unity/playmode-results.xml, ${ARTIFACTS}/trx"
echo "budget record        : ${ARTIFACTS}/host/budget-record.json, ${ARTIFACTS}/benchmark/summary.md"
echo "note: PROBE_RUNS=${PROBE_RUNS}; every 'Pass' above is a reported process result, not this script's opinion."
echo "note: the audio observation in this gate is engine-free by design; a live audio device is an audio-enabled"
echo "      application's concern, never the headless player's (crash-139)."

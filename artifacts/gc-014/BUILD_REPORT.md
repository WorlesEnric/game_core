# GC-014 BUILD REPORT — Wave 4 live lifecycle and required-service closure

**Every executable result below is `NotRun (pending orchestrator build host)`.** This worktree was produced on
macOS with no Unity, no .NET SDK, no Mono and no C# compiler; nothing here has been compiled, imported or executed.
This file records what the Linux build host must run and what each step is expected to establish. Filling it in is
the build host's job, not this task's.

| # | Step | Command | Status | Establishes |
|---|---|---|---|---|
| 1 | Build the Unity-free half | `dotnet build dotnet/GameCore.sln -c Release` | NotRun (pending orchestrator build host) | the composition package's lifecycle sources compile under C# 9 / netstandard2.1 with `TreatWarningsAsErrors` |
| 2 | Pure lifecycle suites | `dotnet test dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gc-014/trx` | NotRun (pending orchestrator build host) | 28 lifecycle tests over the activation ledger, the teardown sequencer/quarantine registry and the closure delta |
| 3 | Unity package resolution | `"$UNITY" -batchmode -nographics -quit -projectPath unity/GameCore.Validation -logFile artifacts/gc-014/unity/resolve.log` | NotRun (pending orchestrator build host) | the new `GameCore.Lifecycle.Tests` assembly and the `GameCore.Unity.Runtime/Lifecycle` folder import; no package added, so the committed lock is unchanged |
| 4 | All EditMode testables | `"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode -testResults artifacts/gc-014/unity/editmode-results.xml -logFile artifacts/gc-014/unity/editmode.log` | NotRun (pending orchestrator build host) | every existing suite plus both lifecycle world suites |
| 5 | Lifecycle suites alone | `"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode -testFilter GameCore.Lifecycle.Tests -testResults artifacts/gc-014/unity/lifecycle-results.xml -logFile artifacts/gc-014/unity/lifecycle-editmode.log` | NotRun (pending orchestrator build host) | all twelve P-046 observations and every invalid transition, in both families, in real owned worlds |
| 6 | PlayMode | `"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform PlayMode -testResults artifacts/gc-014/unity/playmode-results.xml -logFile artifacts/gc-014/unity/playmode.log` | NotRun (pending orchestrator build host) | PlayerLoop/reset behaviour is unchanged by this task |
| 7 | Wave 3 gate on the same revision | `UNITY=<editor> DOTNET=<dotnet> PROBE_RUNS=5 tools/run_w3_gate.sh` | NotRun (pending orchestrator build host) | the lifecycle additions did not disturb either running family, the IL2CPP player, or the catalogs |
| 8 | Documentation gate | `python3 tools/validate_game_core_docs.py --self-test && python3 tools/validate_game_core_docs.py` | **Run here (interpreter-level, not a build result)** — see `artifacts/gc-014/static-checks.log` | link/anchor/traceability/DAG consistency |

## What this task expects the build host to confirm

1. `Packages/com.gamecore.composition` compiles with the new `Lifecycle/` sources under
   `TreatWarningsAsErrors=true` on netstandard2.1 (C# 9). The most likely strictness trap is an unused local; the
   scenario steps were written so every local is read in the verdict and in the detail string.
2. `GameCore.Unity.Runtime` compiles with the new `Lifecycle/` folder (it references `Unity.Jobs.JobHandle`,
   `Unity.Entities`, `GameCore.Execution` and `GameCore.Execution.Messages`).
3. The new `GameCore.Lifecycle.Tests` EditMode assembly resolves its 17 references, including
   `GameCore.Validation.ProbeHost` (used by the two committed-catalog cases).
4. Both family scenario runs report all twelve steps `Passed`, and the two committed-catalog cases report the
   families' slice observations passing.
5. `UnityWorldRegistry.Count` returns to its pre-create baseline after every run (asserted by the teardown step and
   by the suite's `[TearDown]`).
6. `git diff --exit-code` stays clean for
   `unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs` and
   `.../GeneratedCards/CardCatalog.g.cs` (GC-014 changes no catalog input).

## Failure triage pointers

- The per-step `Detail` strings carry every value each verdict was computed from, and the EditMode assertion message
  includes the whole fact bag, so a failure XML is self-explanatory. `artifacts/gc-014/HANDOFF.md` §8 lists the
  literal expectations most likely to need adjusting, with the family suite each value came from.
- A failure in *both* families' `…-provider-loss-makes-consumers-wait` is a kernel issue (the wait must appear in the
  same `PublishedOperation` as the removal), not a fixture issue.
- A failure in *both* families' `…-replacement-stages-while-old-runs` on `oldRanAtStaging` is the P-046 staging
  clause, i.e. `InstallationLifecycleCoordinator.Stage` / `ActivationLedger.StageCandidate`.
- A failure in *both* families' `…-blocked-job-prevents-buffer-release` on `fenceRetainedWhileOutstanding` means a
  buffer was released while a job still owned it — the single most severe outcome this task exists to prevent.

## Evidence files this task committed

- `artifacts/gc-014/HANDOFF.md` — summary, file lists, contract changes, coverage mapping, gaps.
- `artifacts/gc-014/static-checks.log` — verbatim output of the interpreter-level checks that did run here.
- `artifacts/gc-014/{trx,unity}/` — produced by `tools/run_gc014_checks.sh`; nothing is pre-populated.

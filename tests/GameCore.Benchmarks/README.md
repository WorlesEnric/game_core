# GameCore.Benchmarks — the reproducible performance method of 08 (GC-026)

08 section 3's performance method, as executable data: the deterministic 1,000-scope/10,000-target composition
fixture generator (TEST-023), the workload catalogue with its declared update sizes, mode switch, spawn, idle and
unload windows, nearest-rank percentile statistics, the raw per-sample record with its JSON and CSV writers, and the
provisional budget rows of `docs/game-core/08-validation-and-performance.md` as comparable rows.

**Unity-free by design.** Nothing under `Runtime/` references `UnityEngine`/`Unity.*` (the assembly's asmdef declares
`noEngineReferences: true`), so the same sources compile and run in three places:

| Consumer | How |
| --- | --- |
| Plain dotnet | `dotnet/src/GameCore.Benchmarks` (netstandard2.1, `GAMECORE_TELEMETRY` defined) compiles `Runtime/**`; `dotnet/tests/GameCore.Benchmarks.Tests` compiles `Tests/**` and runs them with NUnit 3. |
| Unity EditMode | This folder is a local Unity package (`package.json` + `Runtime/GameCore.Benchmarks.asmdef` + `Tests/GameCore.Benchmarks.Tests.asmdef`), referenced by `unity/GameCore.Validation` and listed in its `testables`, so `Tests/` runs as EditMode tests. |
| IL2CPP player | The qualification player drives the same fixture and writers, so a budget is compared against numbers measured in the shipping-shaped build rather than against an editor-only measurement. |

The plain-dotnet shadow build is the *instrumented diagnostic* build, exactly like `dotnet/src/GameCore.Replay` for
GC-023: `GAMECORE_TELEMETRY` is defined there, which is what keeps the counting call sites in the derivation kernel.
08 records the instrumented diagnostic build and the release-like measurement build separately, so a number is never
attributed to the wrong shape.

## Layout

| Path | Contents |
| --- | --- |
| `Runtime/BenchmarkWorkload.cs` | The workload catalogue (`BenchmarkWorkload`, `BenchmarkWorkloadKind`, the eleven ids), the runner parameters (`DefaultWarmupSeconds`, `DefaultDurationSeconds`, `DefaultRuns`, the repetition counts, the seed) and the `-probeBenchmarkWorkload` selector. |
| `Runtime/BenchmarkFixture.cs` | `BenchmarkScale`, `BenchmarkIds`, `BenchmarkNames`, `BenchmarkFixture` and the deterministic `BenchmarkFixtureGenerator`: 1,000 scopes, 10,000 targets, three schema families, a declared excluded subtree, a declared excluded target and a declared isolated leaf. |
| `Runtime/BenchmarkSnapshotBuilder.cs` | Composes one immutable derivation snapshot per workload variant (mount, unmount, spawn, retire, reparent, mode switch, explicit revision/epoch) and the variant values (`UpdateInstall`, `SpawnTargets`, `SpawnedTargetIds`, `LiveStateTarget`). |
| `Runtime/BenchmarkStatistics.cs` | The single nearest-rank percentile definition (`PercentileOfSorted`, `Percentile`, `Pool`) and the per-phase `BenchmarkDistribution`. |
| `Runtime/BenchmarkSamples.cs` | The sample taxonomy (`BenchmarkPhase`, `BenchmarkThreadCoverage`), the raw `BenchmarkSample`, the allocation tracker with its honest coverage reporting, the memory categories, the gate result and the `BenchmarkRunDocument`. |
| `Runtime/BenchmarkDocumentWriter.cs` | The raw per-sample JSON document, the wide CSV matrix (`CounterColumns`, `WriteJson`, `WriteCsv`) and `MetricsOf`'s `phase/metric` keys. |
| `Runtime/PerformanceBudgets.cs` | The ten budget rows of 08 section 3, the metric names, and the three-valued verdict that keeps a missing measurement out of the pass column. |
| `Tests/` | The suites: the fixture at the declared scale and its seed rules, the fixture driven through the real derivation engine (base, the 1/100/10,000-target updates, the reparent, the spawn, the mode switch, parity with a full recomputation), the percentile definition, the sample/allocation/memory record, the two writers and the budget table. |

## Running the plain-dotnet suites

```bash
dotnet build dotnet/src/GameCore.Benchmarks/GameCore.Benchmarks.csproj
dotnet test dotnet/tests/GameCore.Benchmarks.Tests/GameCore.Benchmarks.Tests.csproj \
  --filter "FullyQualifiedName~GameCore.Benchmarks.Tests"
```

`NotRun (pending orchestrator build host)` — this worktree has no .NET SDK and no Unity, so nothing here could be
compiled or executed here. The Unity EditMode suites run through the validation project's testables; the IL2CPP
player runs the same fixture through the qualification player.

## The recorded fixture

The fixture's **seed and shape are recorded in every raw sample document**: `BenchmarkScale.Seed`,
`BenchmarkScale.Scopes`, `BenchmarkScale.Targets` and `BenchmarkFixture.Describe()` travel in the JSON document
(`seed`, `scopes`, `targets`, `dimension`) and in the CSV comment line (`seed=`, `scopes=`, `targets=`), so a
measurement can always be attributed to the load that produced it. The seed may change *which* targets carry the
seeded marker tag; it never changes the declared shape, because a seed that changed the shape would make two
recorded runs incomparable rather than differently loaded.

## What is deliberately *not* asserted

No measured duration, allocation number or percentile from a real run is committed as a literal. A remembered number
would assert only that the machine and the code did not change, and 08's own preface is explicit that its targets are
"initial engineering targets, not measured results or universal shipping requirements". The suites therefore assert
the *definitions and the shapes* — the fixture at its declared scale, the affected target count of each update size,
the local invalidation of a move, the parity of the incremental and full derivations, the nearest-rank percentile on
vectors whose answers are checkable by hand, the document's own identity and the budget table's verdicts — and leave
the numbers to the recorded artifacts.

# GC-023 HANDOFF — replay, differential propagation and complete cost instrumentation (Wave 6)

Branch `gc-023` (worktree `/Users/yangcao/wkspace/gc-wt/gc-023`), based on `main` (`1cafced`, the Wave 5 gate).

**Status of every build/test command in this document: `NotRun (pending orchestrator build host)`.** This host has no
.NET SDK, no C# compiler and no Unity, so nothing in this change set has been compiled, imported, executed or built
here. What did run is interpreter-level only: the host-side C# checker
(`tools/check_game_core_csharp.py`, 481 files, clean), the contract-surface parity check, the generated-catalog
verifier, the documentation validator (self-test and full), the generic-profile audit, the telemetry release-shape
switch check in `--no-build` mode, `bash -n` over the two new shell scripts, a JSON validation of the inventory and of
the replay record, a whole-worktree `.cs`↔`.meta` coverage and GUID-uniqueness scan, and three independent read-only
audits of the new code whose findings are all addressed (section 8). None of those is a build or a test result.

## 1. Summary

GC-023's objective is "expose ordering drift and accidental control-plane work in the simulation hot path". The change
set has three parts that meet at one seam.

1. **A fixed compact telemetry schema in `GameCore.Contracts`.** `TelemetryCounter` is the schema: one stable integer
   per counter, the twenty names 08 lists first (with the two duration families expanded to a total and a sample
   count, and the high-water/overflow pair to two ids) and TEST-023's reporting split appended (retained event
   bytes/count, quarantine bytes/entries, lease bytes, cache bytes/entries, discarded callbacks, apply sample count).
   `TelemetryCounterSet` is one flat `long[]` per owner; `TelemetrySection`, `TelemetryFrame` and `TelemetryTrace`
   carry samples and a chain hash; `TelemetrySeries` is a keyed duration series; `ITelemetryOwner` is the seam;
   `TelemetryCounting` holds the three `[Conditional("GAMECORE_TELEMETRY")]` counting helpers.
2. **Twenty-two runtime owners that export their counters through it.** Derivation (result, closure, incremental
   outcome, recipe cache), composition (service resolution, operation ledger, resource ledger, job fence, callback
   gate, quarantine registry), planning (the planned publication), execution (the driver, the guarded dispatch group,
   the publication store, the committed event store, the world resource ledger, the request ledger, the step message
   schedule, the input cutoff, the clock registry), the assembly publisher, the derived-assembly pipeline and the
   observation surface. Each implements `ITelemetryOwner`; each reports only the counters it owns.
3. **A Unity-free replay fixture package** (`tests/GameCore.Replay`, assembly `GameCore.Replay`) carrying canonical
   state/decision/provenance/event hashing with a documented exclusion list, the recorded 10,000-step integer fixture
   and its generator, the worker-scheduling model (1/2/4/max), engine-observation replay separated from a
   native-physics comparison, differential propagation with a deterministic reducer, the telemetry collector and the
   raw benchmark trace format. The same sources run in plain dotnet, in Unity EditMode and in the IL2CPP player.

The switch is `GAMECORE_TELEMETRY`. It reaches a Unity compilation through an asmdef `versionDefines` entry on the new
qualification marker package `com.gamecore.telemetry-qualification` (present only in the validation project's
manifest), and the plain-dotnet shadow projects define it unconditionally, so they are the *instrumented diagnostic*
build that 08 §3 asks to be recorded separately from a release-like measurement build. Every counting site is a
`[Conditional]` helper call, so a build without the symbol emits no call, no branch and no argument evaluation.

## 2. Commits on this branch

| Commit | Contents |
|---|---|
| `GC-023: the fixed compact telemetry schema and per-owner counters` | The `GameCore.Contracts` telemetry surface, the twenty-two owner implementations, the new marker package, the asmdef `versionDefines` entries and the plain-dotnet `DefineConstants`. |
| `GC-023: the replay fixture package, its tests, the probe and the release-shape proof` | `tests/GameCore.Replay/**`, the dotnet projects and solution entries, the Unity scenario/probe/EditMode suite, the harness and gate scripts, the disabled-shape proof, the contracts allowlist entries and the counter corrections the audits found. |

Nothing was pushed; no branch was switched; no history was rewritten.

## 3. Files created

### The telemetry schema (production, `GameCore.Contracts`)

| File | Contents |
|---|---|
| `Packages/com.gamecore.contracts/Runtime/Telemetry/TelemetrySchema.cs` | `TelemetryCounter` (30 ids: 29 counters plus `Count`), `TelemetryAggregation`, `TelemetrySchema` (names, aggregation policy, formats, `IsCompiledIn`, `Symbol`), `ITelemetryOwner`, `TelemetryCounting` (the three `[Conditional]` helpers), `TelemetryBytes` (the documented byte accounting), `TelemetryRetention`. |
| `Packages/com.gamecore.contracts/Runtime/Telemetry/TelemetryContainers.cs` | `TelemetryCounterSet`, `TelemetrySection`, `TelemetryFrame`, `TelemetryTrace`, `TelemetrySeries`, `TelemetrySeriesEntry`, `TelemetryDurations`, the internal canonical big-endian writer. |
| `Packages/com.gamecore.telemetry-qualification/package.json` | The qualification marker package (version 1.0.0) that switches `GAMECORE_TELEMETRY` on. |

### The replay fixtures (assembly `GameCore.Replay`, package `com.gamecore.replay`)

| File | Contents |
|---|---|
| `tests/GameCore.Replay/package.json`, `Runtime/GameCore.Replay.asmdef`, `Tests/GameCore.Replay.Tests.asmdef` | The local Unity package, its runtime assembly (engine-free) and its EditMode test assembly. |
| `tests/GameCore.Replay/Runtime/ReplayHashing.cs` | `ExcludedFields`, `WorldOrdinalMap`, `ReplayStateHash` (state/decision/provenance/event/state-table text, hashes, `ProjectionText`), `ReplayTargetState`. |
| `tests/GameCore.Replay/Runtime/IntegerFixtureGenerator.cs` | `ReplayOperationKind`, `ReplayStepRecord`, `ReplayTrace`, `ReplayTraceHashes`, `ReplayFixtureShape`, `IntegerFixtureGenerator`, `IntegerRuleStage`, `WorkerSchedule`, `Lcg`. |
| `tests/GameCore.Replay/Runtime/ReplayRunner.cs` | `ReplayStepResult`, `ReplayRun`, `ReplayOptions`, `ReplayRunner`, `ReplayComparison`. |
| `tests/GameCore.Replay/Runtime/TelemetryCollector.cs` | `TelemetryCollector`, the internal delegate owner, `BenchmarkTrace` (raw line format, JSON document and `TryRead`). |
| `tests/GameCore.Replay/Runtime/ObservationReplay.cs` | `RecordedObservation`, `ObservationReplay`, `ObservationReplayResult`, `NativePhysicsComparison`, `ObservationRecorder`. |
| `tests/GameCore.Replay/Runtime/DifferentialPropagation.cs` | `DifferentialOperation`, `FailingSeedRecord`, `DifferentialSweepResult`, `DifferentialPropagation` (sweep + reducer). |
| `tests/GameCore.Replay/Tests/*.cs` | Four suites: schema facts, fixture determinism and the 10,000-step worker sweep, the collector/build switch/benchmark trace, observation replay and the differential sweep with an injected divergence. |
| `tests/GameCore.Replay/Data/replay-record.json`, `README.md` | The recorded input trace (shape, seed, schedules) both the probe and the suites drive, and the package's own documentation. |

### The Unity half

| File | Contents |
|---|---|
| `unity/.../Runtime/ReplayScenario.cs` | Five real-world observations (one command-driven fixture world, its owners sampled through the schema, 64 idle frames asserted to do zero control-plane work, the memory split, a registered wake that commits one step and produces duration samples, and the counters moving with the committed step) and seven pure-replay observations. |
| `unity/.../Runtime/ProbeReplay.cs` | The `-probeReplay` player mode: every observation plus the digest, the observation count and the benchmark-trace artifacts written beside the probe result. |
| `unity/.../Tests/Replay/GameCore.Replay.Tests.asmdef`, `ReplayIntegrationTests.cs` | The Editor half: one `[Test]` per named observation, the digest recomputed from the scenario's own names, and the artifact round trip. |

### Tooling

| File | Contents |
|---|---|
| `dotnet/src/GameCore.Derivation.Fixtures/`, `dotnet/src/GameCore.Replay/`, `dotnet/tests/GameCore.Replay.Tests/` | The dotnet shadow projects (netstandard2.1 / net8.0) that compile the fixture and test sources, with `GAMECORE_TELEMETRY` defined (the instrumented diagnostic build). |
| `dotnet/tools/GameCore.TelemetryProbe/` (`Probe.cs`) | The behavioural half of the disabled-shape proof: `[Conditional]` call sites with side-effecting arguments, run in both configurations. |
| `dotnet/src/GameCore.Telemetry.ReleaseCheck/` | The binary half: the real instrumented derivation sources compiled with and without the symbol. |
| `tools/check_release_telemetry_free.py` | Drives both halves, scans both assemblies, checks the switch wiring, writes `--json` evidence. |
| `tools/unity/run_replay_probe.sh` | The player-probe harness: `PROBE_RUNS` runs, strict JSON, every observation name, the required clauses, the trace artifacts and the recorded-digest comparison. |
| `tools/run_gc023_gate.sh` | The whole gate in one command (section 6). |

### Modified (shared surfaces — recorded here as the brief requires)

| File | Change | Why it is safe |
|---|---|---|
| `dotnet/GameCore.sln` | Three projects registered; **a pre-existing defect repaired**: `GameCore.Rules.Narrative.Tests` was missing its `EndProject`. | The repair makes the file well-formed (28 `Project(` lines, 28 `EndProject`, unique GUIDs, four configuration rows per project); the additions follow the existing GUID and configuration pattern. |
| `dotnet/tests/GameCore.Contracts.Tests/ContractTests.cs` | Fourteen telemetry type headers added to the GC-003 additions allowlist. | Additions only, the same mechanism GC-012 and GC-018 used; no frozen line was touched. |
| `dotnet/src/{GameCore.Contracts,GameCore.Derivation,GameCore.Composition,GameCore.Planning,GameCore.Execution}/*.csproj` | `GAMECORE_TELEMETRY` added to `DefineConstants`. | One line per project plus a comment; without it the dotnet shadow build would not be the instrumented diagnostic build. |
| `Packages/{contracts,derivation,composition,planning,unity.runtime}/Runtime/*.asmdef` | A `versionDefines` entry for `com.gamecore.telemetry-qualification`. | Additive; a shipping project without the marker is unaffected, exactly as GC-017's entry behaves. |
| `tools/check_game_core_csharp.py` | Five new `TARGETS` and one new `engine_free` root. | Additive path lists; the checker now covers the new C#. |
| `tools/unity/prepare_gc017_release_project.py` | The telemetry marker joins the clone's removal list. | One name in an existing tuple; the marker-free clone is what the release-shape claim is about. |
| `unity/GameCore.Validation/Packages/manifest.json`, `packages-lock.json` | The replay package and the telemetry marker resolve; `com.gamecore.replay` is testable. | Additive entries in the established local-package form; Unity refreshes the lock on resolve. |
| `unity/.../Runtime/GameCore.Validation.ProbeHost.asmdef` | `GameCore.Replay` added to `references`. | One entry; the scenario needs the fixture assembly. |
| `unity/.../Runtime/ProbeArguments.cs`, `ProbeRunner.cs` | The `-probeReplay` flag, its property, parse branch, invocation term, dispatch arm and report identity. | One addition per list, following the documented pattern the W5 gate established. |
| `artifacts/gates/w4-generic-profile/inventory.{json,md}` | A `gc023Revisions` section: **proposals only**, no row promoted. | A row may only be promoted from an archived passing probe on the build host. |

## 4. Requirement → implementation → observation mapping

| Requirement / test | Where implemented | Where it would be observed |
|---|---|---|
| P-007 references and leases | `ITelemetryOwner` exports carry live leases and outstanding callbacks from the owners that hold them; the world observation reports them separately from events, cache and quarantine. | `replay-world-memory-split-is-reported-separately` |
| P-008 stable ordering and the determinism boundary | `ExcludedFields` (the list a canonical hash may not read), canonical state/decision/provenance/event text, `WorldOrdinalMap` (comparison only), the recorded admitted order, the worker schedule. | `replay-hashes-identically-across-worker-counts`, `replay-hashes-identically-under-shuffled-producers`, `replay-digest` |
| P-018 precedence | Rank and identity decide composition; shuffled producer completion cannot reach a decision. | `replay-hashes-identically-under-shuffled-producers` |
| P-022 budgets | `PlanPreparedBytes` from the plan's own estimate; `StrataEvaluated`, `CandidatesMatched` and the derivation's byte counters through the schema. | `replay-world-counters-move-on-a-committed-step` |
| P-023 incrementality | `ControlNodesVisited` at every index/closure site and *before* a cached lookup; a carried step reports zero control nodes, zero strata and zero examined candidates. | `replay-world-idle-pump-does-zero-control-work` |
| P-026 explanation and provenance | The canonical projection includes decisions, explanations and loser/shadowed support identities; the differential sweep compares the whole projection. | `replay-differential-sweep-is-clean-and-reducible` |
| P-037 step admission | The recorded trace's admission sequences and sealed batches; a changed admitted order is a different input trace. | `replay-different-admission-is-a-different-input` |
| P-039/P-040/P-041 execution | Per-stage duration and job-wait duration series, structural-operation count, refusals as stale results. | `replay-world-wake-commits-one-step-and-samples-durations` |
| P-043 bounded work | Request high-water marks tracked from observed depths and capacity refusals as overflow. | `replay-world-counters-move-on-a-committed-step` |
| P-044/P-045 commit and observation | Committed event identities in the trace; retained event bytes/count from the event store; `StepsAdvanced` from the driver alone. | `replay-world-wake-commits-one-step-and-samples-durations` |
| P-048 teardown and quarantine | Quarantine bytes/entries separate from lease bytes; nothing quarantined by an untouched world; the world stops cleanly at the end of the scenario. | `replay-world-memory-split-is-reported-separately` |
| P-052 diagnostics | Every observation is a named step with a detail string; the trace and JSON artifacts are the evidence shape. | `replay-digest`, `replay-benchmark-trace` |
| P-053 checkpoint (counter completeness only) | The observation store's lease count and the event store's retained bytes are reported through the schema. | `replay-world-memory-split-is-reported-separately` |
| P-060 evidence and release status | The fixture record as data; the raw and JSON traces as artifacts; the disabled-shape proof as a machine-readable document. | `replay-benchmark-trace`, `telemetry-release-surface.json` |
| TEST-007 derivation termination and bounded expansion | The differential sweep runs the real engine and the real oracle over seeded scripts and reduces a disagreement. | `replay-differential-sweep-is-clean-and-reducible` |
| TEST-008 differential propagation and failing seeds | `DifferentialPropagation.Sweep` plus the reducer, with an injected divergence proving the reducer bites. | `replay-differential-sweep-is-clean-and-reducible` |
| TEST-012 execution graphs and job synchronization | Per-stage and job-wait duration samples come from the real dispatch and the real step fence. | `replay-world-wake-commits-one-step-and-samples-durations` |
| TEST-014 committed events and consistent observation | Committed event identities are part of the canonical per-step comparison. | `replay-hashes-identically-across-worker-counts` |
| TEST-022 ordering, replay and determinism limits | The 10,000-step fixture, four worker counts, shuffled producers, the exclusion list, the ordinal map, observation replay separated from physics. | `replay-trace-records-ten-thousand-steps`, `replay-hashes-identically-across-worker-counts`, `replay-observation-replay-is-separate-from-physics` |
| TEST-023 performance, bounded memory and architecture regressions | Zero control-tree visits and zero string service resolutions per idle frame; the memory split; the disabled-shape proof. | `replay-world-idle-pump-does-zero-control-work`, `replay-world-memory-split-is-reported-separately` |
| TEST-018 Unity worlds and Play Mode | The whole scenario runs in a real owned world and in the player. | `-probeReplay`, `GameCore.Replay.IntegrationTests` |

## 5. Contract changes (`GameCore.Contracts`)

Fourteen public types were added; **nothing was removed or changed**, and the fourteen type headers are registered in
the GC-003 additions allowlist exactly as GC-012 and GC-018 registered theirs:

```
type class GameCore.Contracts.TelemetryBytes [static]
type class GameCore.Contracts.TelemetryCounting [static]
type class GameCore.Contracts.TelemetryCounterSet
type class GameCore.Contracts.TelemetryDurations [static]
type class GameCore.Contracts.TelemetryFrame
type class GameCore.Contracts.TelemetrySchema [static]
type class GameCore.Contracts.TelemetrySection
type class GameCore.Contracts.TelemetrySeries
type class GameCore.Contracts.TelemetryTrace
type enum GameCore.Contracts.TelemetryAggregation : System.IComparable, System.IConvertible, System.IFormattable, System.ISpanFormattable
type enum GameCore.Contracts.TelemetryCounter : System.IComparable, System.IConvertible, System.IFormattable, System.ISpanFormattable
type interface GameCore.Contracts.ITelemetryOwner
type struct GameCore.Contracts.TelemetryRetention
type struct GameCore.Contracts.TelemetrySeriesEntry
```

Additionally, non-contract public members were added to existing kernel types (all additive): `CostCounters.Telemetry`,
`StrataEvaluated`, `ControlNodesVisited` and `WriteTelemetry`; `DerivationIndexSet.Build(snapshot, counters)`;
`DerivationResult.WriteTelemetry`; `InvalidationClosureResult.WriteTelemetry`; `IncrementalDerivationOutcome.WriteTelemetry`;
`DerivedRecipeCache.RetainedBytes`, `CacheEntryBytes`, `CacheRuleBytes` and `WriteTelemetry`; `ServiceResolution.Telemetry`,
`ServiceStringLookups` and `WriteTelemetry`; `OperationLedger.RowHighWaterMark` and `WriteTelemetry`;
`ResourceLedger.RetainedBytes`, `QuarantinedBytes` and `WriteTelemetry`; `JobFenceRegistry.WriteTelemetry`;
`CallbackGate.WriteTelemetry`; `QuarantineRegistry.WriteTelemetry`; `PlannedPublication.WriteTelemetry`;
`UnityExecutionDriver.TelemetryClock` and `WriteTelemetry`; `GuardedSystemGroup.TelemetryClock`, `WriteTelemetry`,
`NoteStageDuration` and `NoteJobWaitDuration`; `StepPublicationStore.WriteTelemetry`; `CommittedEventStore.RetainedBytes`
and `WriteTelemetry`; `WorldResourceLedger.RetainedResourceBytes` and `WriteTelemetry`; `RequestLedger.PendingHighWaterMark`
and `WriteTelemetry`; `StepMessageSchedule.RecordedStructuralOperationCount`, `StructuralHighWaterMark` and
`WriteTelemetry`; `StepInputCutoff.WriteTelemetry`; `PluginClockRegistry.WakeHighWaterMark` and `WriteTelemetry`;
`AssemblyPublisher.StructuralWriteCount`, `TelemetryClock` and `WriteTelemetry`; `DerivedAssemblyPipeline.WriteTelemetry`;
`WorldObservation.WriteTelemetry`. `ITelemetryOwner` is implemented by twenty-two types.

## 6. Exact commands for the Linux build host

Everything runs from the repository root. Nothing below has been run.

### 6.1 The whole gate (one command)

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity DOTNET=$HOME/.dotnet/dotnet \
  PROBE_RUNS=5 tools/run_gc023_gate.sh
```

In order: `dotnet build` + `dotnet test dotnet/GameCore.sln -c Release`; the replay suites alone with a trx;
`tools/check_release_telemetry_free.py` (both configurations of the probe tool and of the real derivation sources, plus
the switch wiring) writing `artifacts/gc-023/toolchain/telemetry-release-surface.json`; the host-side static checks;
the Unity resolve; EditMode (every testable package, which now includes `GameCore.Replay.Tests`, plus
`GameCore.Replay.IntegrationTests`); PlayMode; the IL2CPP player through `tools/unity/build_probe.sh`; `-probeReplay`
`PROBE_RUNS` times through `tools/unity/run_replay_probe.sh`; and a marker-free release project inspection. Every Unity
Editor invocation is wrapped in `timeout` with one logged retry on a timeout.

### 6.2 The pure half alone

```sh
dotnet build dotnet/GameCore.sln -c Release
dotnet test  dotnet/tests/GameCore.Replay.Tests/GameCore.Replay.Tests.csproj -c Release \
  --logger trx --results-directory artifacts/gc-023/trx
python3 tools/check_release_telemetry_free.py --json artifacts/gc-023/toolchain/telemetry-release-surface.json
```

The suite includes the 10,000-step worker sweep (4 full replays of 10,000 steps, each with a per-step comparison), so
expect seconds, not milliseconds. To run only the fast suites first, filter on the fixtures:
`--filter "FullyQualifiedName~TelemetrySchemaTests|FullyQualifiedName~TelemetryCollectorTests"`.

### 6.3 The replay probe alone

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
UNITY=$UNITY tools/unity/build_probe.sh
PROBE_RUNS=5 UNITY_PROJECT=unity/GameCore.Validation ARTIFACTS=artifacts/gc-023/toolchain \
  tools/unity/run_replay_probe.sh
```

The probe writes `probe-replay.json` plus `probe-replay.json.trace` and `probe-replay.json.trace.json`. The harness
compares the `replay-digest` value against `tests/GameCore.Replay/Data/replay-record.json` **only when the record is
non-empty**: the first build-host run must copy the observed digest into that file (the file's `_comment` says how),
and from then on the harness refuses a changed digest. Until then it prints the observed value and continues.

### 6.4 The EditMode half alone

```sh
timeout --signal=TERM --kill-after=60 1800 "$UNITY" -batchmode -nographics \
  -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode \
  -testFilter GameCore.Replay.IntegrationTests \
  -testResults "$PWD/artifacts/gc-023/unity/replay-editmode.xml" \
  -logFile artifacts/gc-023/unity/replay-editmode.log
```

Do not add `-quit` to a `-runTests` command (04 §10). `-testResults` with a relative path resolves against the Unity
**project** path, not the shell cwd, so an absolute path (as above) or a copy afterwards is required.

## 7. Inventory: proposals only

`artifacts/gates/w4-generic-profile/inventory.{json,md}` gains a `gc023Revisions` / "GC-023 revision notes" section that
**promotes nothing**. Seven rows (P-008, P-018, P-022, P-023, P-026, P-052, P-060) are proposed unchanged with a delta
that records what this task adds and which clause is not this task's to close; ten rows and every operation row are
explicitly not proposed. Every proposal names the observation that would carry it and the artifacts the build host must
first produce.

## 8. Read-only audits and the defects they found

Three independent read-only passes checked every external call against its declaration (exact name, namespace
reachability through each file's `using` directives, arity, parameter order, property existence, return type and
nullability), C# 9/netstandard2.1 legality, the `#if GAMECORE_TELEMETRY` shapes, warning-as-error risks, and whether
the asserted behaviours could hold at all. Findings, all fixed before the final commit:

**Could not compile (10).** `ServiceResolution` lost three properties (`Nodes`, `Diagnostics`, `ActivationOrder`) to a
misplaced range replacement — a hard break for the whole composition assembly. A duplicated
`TelemetryCollector.Sample` overload (CS0111). `decision.Rule.RuleId` and `ContributionKey.Slot` (CS1061; the members
are `RuleId.ToString()` and `OutputSlot`). Two explanation lines passed `IReadOnlyList<CapabilityContribution>` to an
overload taking `IReadOnlyList<ContributionKey>` (CS1503), and the now-unused overload was deleted.
`decision.Reason.ToString(IFormatProvider)` (CS0618 under `TreatWarningsAsErrors`). `DerivationSnapshot.Describe()`
(does not exist; `CanonicalText()`). `WorldPumpResult.Advance.HasValue`/`.Value` on a nullable *reference* (three
sites; the property has no `HasValue`). `OperationResult.Detail` (does not exist; `CodeText`). A missing
`using GameCore.Validation.ProbeHost;` in the new EditMode suite (CS0246/CS0103). `TelemetrySeries.TryGet` assigned a
`long` to an `out int`.

**Would have failed at runtime or as a test (6).** The replay hashed the per-step state table and event list in
producer *visit* order, so the two worker-count comparisons could never pass; both are now sorted canonically before
hashing. `RequestLedger.WriteTelemetry` reported the configured queue capacity as the high-water mark (and the clock
registry and the schedule reported current counts), which cannot witness backpressure; all three now track an observed
maximum. The lease-byte totals included quarantined bytes, contradicting their own claim that the four-way split is a
partition. `IncrementalDerivationOutcome.WriteTelemetry` wrote the shared counter object twice, and the runner
registered two owners of one shared counter object, so cumulative counters were double reported. The 240-step and
64-step test shapes recorded no composition operation at all at the default 997-step stride, so three assertions were
unsatisfiable; the tests pass an explicit stride. The schema-name table omitted ids 11, 13 and 21, shifting every
later name. The frame hash depended on section order for callers other than the collector. A live world legitimately
retains its own host resources, so the memory observation compares against the count captured at creation rather than
against zero.

**Gate wiring (3).** The fourteen new public contract types were not in the GC-003 additions allowlist, which refuses
an undocumented addition — the whole-solution test run would have failed. `TelemetrySchema.IsCompiledIn` reported the
compilation of the *declaring* assembly, which never received the symbol, so the enabled branch of the build-switch
test failed in both Unity and dotnet; the contracts asmdef and csproj now carry the marker like the others. The
Contracts `DefineConstants` interacts with the release-shape check in the one artificial way described in section 10.2.

A fourth pass re-verified every fix in the list above against the current tree; its findings are incorporated.

## 9. Decisions where the documents were ambiguous

1. **Where the replay fixtures live.** 09 GC-023 names `tests/GameCore.Replay/` and the repository conventions name
   `Packages/com.gamecore.<name>/` for a Unity package. 01's assembly table lists no `GameCore.Replay`. Resolution:
   `tests/GameCore.Replay/` **is** the local Unity package (`package.json` + `Runtime/GameCore.Replay.asmdef`), the
   form GC-005 used for the temporary reference-seam package, because the brief names that path and because a replay
   fixture is qualification surface rather than a kernel assembly. It is registered in the validation project's
   manifest and `testables`, and compiled by `dotnet/src/GameCore.Replay` for the plain-dotnet run.
2. **Which parts of a replay are hashed.** TEST-022 says to hash canonical stable identities and schema fields while
   excluding counters, and separately to map fresh `WorldId`s to a fixture ordinal "only in comparison output".
   Resolution: `ReplayStateHash` reads stable ids, schema refs, versions, effective values, support/shadowed
   identities, decision lines and event identities; `ExcludedFields` names the six exclusions; `WorldOrdinalMap` is a
   comparison structure the kernel never sees, and `WorldOrdinalTests.TheRuntimeIdentityIsStillTheRealOne` asserts the
   real identity is untouched.
3. **What "the 10,000-step integer fixture" contains.** TEST-022 fixes the step count and asks for a *pure integer*
   fixture, so the fixture is small in targets (ten) and large in steps (10,000): the acceptance criterion is
   agreement between replays of one record, not the size of one step's world. The shape is recorded in
   `Data/replay-record.json` so the claim is about a named record.
4. **Where `IsCompiledIn` lives.** The compile-time switch is a property of a compilation, and a property body is
   compiled in its declaring assembly. Resolution: the schema reports the build shape of the assembly that declares
   it, and **every** instrumented asmdef/csproj takes the symbol from the same marker package, so the answer is the
   same everywhere. `tools/check_release_telemetry_free.py` asserts that all of them carry the entry.
5. **Counting helpers versus `#if` at call sites.** A gated-*declaration* approach would have made counter members
   disappear in a release build, so any owner export would need its own guard and the two shapes would diverge
   structurally. Resolution: declarations unconditional, call sites `[Conditional]`, so the release shape has no call,
   no branch and no argument evaluation while the export surface stays compilable and reviewable. The behavioural
   proof is `dotnet/tools/GameCore.TelemetryProbe` and the binary proof is the derivation release-check.
6. **`ApplySampleCount`, `StageSampleCount` and `JobWaitSampleCount`.** 08 names durations but not their sample
   counts; a zero duration is otherwise indistinguishable from "nothing was measured". Resolution: three extra ids
   exist so a build can report "no sample" honestly. They are additions to the schema, not to the 08 list.
7. **The digest literal.** The other gates compare a digest against a committed literal. This one cannot:
   nothing here has been run, and inventing a literal would be fabrication. Resolution: the harness compares against
   `Data/replay-record.json`'s `observationDigest` only when it is non-empty, and the file documents how the build
   host records it from the first passing run.

## 10. Known gaps, assumptions and risks

1. **Nothing has been compiled or executed here.** The highest-risk items, in the order a compiler would find them:
   (a) the fifteen type/namespace resolution sites the audits checked are the ones a compiler would find first;
   (b) `tests/GameCore.Replay` is a local Unity package that has never been resolved — the manifest entry, the
   `testables` entry and the lock entries follow the existing local-package form, and Unity refreshes the lock on
   resolve; (c) the Unity-world observations depend on the fixture world's real behaviour (creation at epoch 1/step 0,
   an idle pump performing no step, one wake committing exactly one step, `Stop` returning `Published`), which the
   audits cross-checked against `Packages/com.gamecore.unity.runtime/Tests/Runtime/WorldHostTests.cs` but only a run
   can confirm; (d) the 10,000-step sweep's runtime is estimated at seconds, not measured.
2. **The symbol is build-wide, and the release-shape check is deliberately uneven about it.** `dotnet/src/GameCore.Telemetry.ReleaseCheck`
   compiles the derivation package *without* the symbol while its `GameCore.Contracts` reference keeps the
   `DefineConstants` entry, so in that one artificial configuration `TelemetrySchema.IsCompiledIn` reports `true`
   while the derivation call sites are gone. The check is about call sites (which is what the claim is about); every
   real build takes the symbol for all instrumented projects or for none, because it arrives from one marker package.
3. **The generic-profile audit does not discover `tests/`.** `tools/w4_generic_profile_audit.py` scans `Packages/` and
   `unity/GameCore.Validation/Assets`, so the new `GameCore.Replay` assembly is outside its kernel set and its
   references are not audited there. They are all kernel assemblies (`Contracts`, `Derivation`, `Derivation.Fixtures`,
   `Composition`, `Planning`), and `tools/check_game_core_csharp.py`'s `engine_free` check confirms the package
   references no Unity type, but the audit itself reports nothing about it. If the orchestrator wants the audit to
   cover local packages under `tests/`, that is a one-line change to its `ASMDEF_ROOTS`.
4. **`dotnet/GameCore.sln`'s pre-existing defect was repaired here.** `GameCore.Rules.Narrative.Tests` had lost its
   `EndProject`. MSBuild apparently tolerated it (the W5 gate reports a green solution build), so the repair is a
   correctness fix rather than a behaviour change — recorded because it edits a shared file.
5. **Three findings were latent rather than fatal**, and are recorded so a later reviewer does not re-derive them:
   the disabled-shape branch of the build-switch test (no current project compiles these suites without the symbol);
   the frame-section-order dependence (the collector sorts, so only a direct `TelemetryFrame` caller could see it —
   now sorted in the frame itself); and the fact that `ContractClaims`-style verification of `IsCompiledIn` needs the
   contracts assembly to carry the symbol (now it does).
6. **`P-008`'s cross-platform clause is explicitly not claimed.** The fixture is integer-only on purpose, and the
   observation replay's physics half is a *comparison* result with a reported delta, never bit identity — TEST-022
   excludes physics lockstep from the claim and the code says so in its own documentation.
7. **The player probe's world half is one command-driven world, not one world per family.** The brief asks for
   "Unity-world + dotnet tests" and a `-probeReplay` mode; the world half builds the fixture world (which is
   genre-neutral: no cards and no narrative vocabulary), because the counter contract it asserts — zero control work on
   an idle step, the memory split, a committed step's duration samples — is a kernel property rather than a family
   one. The card and narrative families' own counters are exercised by their gates (`-probeW4Gate`, `-probeW5Gate`),
   which run in the same player.
8. **`RetainedEventBytes` uses a documented accounting constant**, not a native struct size (`TelemetryBytes.RetainedEventOverhead`),
   because a real byte count would require reading the store's native layout from a pure assembly.
9. **The `ApplyDuration` series is recorded by `AssemblyPublisher`, which is Unity-only.** In the pure replay fixture
   no publish happens, so `ApplyDurationMicroseconds` and `ApplySampleCount` are zero there — the pure half asserts the
   *shape* of the report (the ids exist and stay zero), and the Unity half asserts the samples. Recorded because a
   reader comparing the two halves could otherwise read the zeros as a defect.

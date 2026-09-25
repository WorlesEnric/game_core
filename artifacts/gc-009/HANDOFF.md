# GC-009 HANDOFF — compile execution DAGs and both temporal drivers (Wave 2)

Branch: `gc-009` (worktree `/Users/yangcao/wkspace/gc-wt/gc-009`).

**Status of every executable check in this handoff: `NotRun (pending orchestrator build host)`.** This host has no
.NET SDK, no C# compiler, no Mono and no Unity, so nothing here has been compiled, imported or executed. Only
interpreter-level host checks ran; they are recorded in `artifacts/gc-009/static-checks.log` and are explicitly
**not** build evidence: `tools/check_game_core_csharp.py` (68 files, brace/forbidden-construct scan) and
`tools/validate_game_core_docs.py` (+ `--self-test`).

## 1. Summary

Two deliverables plus their tests, on top of GC-005 (which already owns the temporal accumulators, the guarded
ordered dispatcher and the resource ledger — extended, never duplicated):

1. **Pure schedule compiler** — `Packages/com.gamecore.planning/Runtime/Scheduling/` (assembly `GameCore.Planning`).
   Compiles the active stage/system/buffer declarations into one stable topological order with edge witnesses:
   required/optional stage edges (optional edges disappear when their endpoint is absent; required ones reject),
   each stage's inner system DAG, buffer producer→consumer edges, deferred structural playback points, and
   system-level access validation (a directed path in the expanded DAG or proven disjoint partitions, or
   `AmbiguousOrder`; every cycle is `Cycle` with its path). Output is a `CompiledSchedule` that GC-005's ordered
   dispatch consumes.
2. **Unity/engine-free temporal module** — `Packages/com.gamecore.unity.runtime/Runtime/Pure/Time/` (engine-free,
   namespace `GameCore.Execution.Time`) and `Runtime/Time/` (Unity): command admission cutoffs with host-assigned
   monotonic sequences, retained unrun input, plugin clock registry with typed wake records and per-clock pause
   policy, a native resource fence table that carries **non-component** `JobHandle` dependencies into Unity
   dispatch, the adapter that installs a compiled schedule into the guarded dispatch table, and the world time
   driver that wraps the host pump with both drivers.

## 2. Files created

Pure compiler (assembly `GameCore.Planning`, references only `GameCore.Contracts`):

- `Packages/com.gamecore.planning/Runtime/Scheduling/ScheduleDeclarations.cs` — `ScheduleDeclarations`
- `.../Scheduling/ScheduleOrdering.cs` — `FactoryKeyComparer`, `AccessDeclarationComparer`, `BufferBindingComparer`
- `.../Scheduling/ScheduleWitness.cs` — `ScheduleWitnessKind`, `ScheduleWitness`, `ScheduleWitnessComparer`, `ScheduleCompilation`
- `.../Scheduling/CompiledSchedule.cs` — `ScheduleEdgeKind`, `ScheduleStageEdge`, `ScheduleEntry`, `SchedulePlaybackPoint`, `ScheduleStage`, `CompiledSchedule`
- `.../Scheduling/ScheduleHash.cs` — internal canonical hash of one schedule
- `.../Scheduling/ScheduleCompiler.cs` — `ScheduleCompiler.Compile`

Engine-free temporal core (`Runtime/Pure/**`, also compiled by `dotnet/src/GameCore.Execution`; namespace
`GameCore.Execution.Time`):

- `Packages/com.gamecore.unity.runtime/Runtime/Pure/Time/StepInputCutoff.cs` — `DemandKind`, `DemandRecord`, `InputSeal`, `StepInputCutoff`
- `Packages/com.gamecore.unity.runtime/Runtime/Pure/Time/PluginClocks.cs` — `PluginClockKind`, `WakePausePolicy`, `PluginClockSpec`, `WakeRecord`, `PluginClockRegistry`

Unity temporal module (assembly `GameCore.Unity.Runtime`):

- `Packages/com.gamecore.unity.runtime/Runtime/Time/NativeDependencyTable.cs` — `NativeDependencyTable`
- `Packages/com.gamecore.unity.runtime/Runtime/Time/ScheduleDispatchAdapter.cs` — `IScheduleDispatchKindResolver`, `ScheduleDispatchKindTable`, `ScheduleAdaptationWitness`, `ScheduleBufferBinding`, `ScheduleAdaptation`, `CompiledScheduleAdapter`
- `Packages/com.gamecore.unity.runtime/Runtime/Time/WorldTimeDriver.cs` — `TimeFrameReport`, `WorldTimeDriver`

Tests:

- `Packages/com.gamecore.planning/Tests/Scheduling/ScheduleFixtures.cs` — frozen stage/system/buffer declaration fixtures
- `Packages/com.gamecore.planning/Tests/Scheduling/ScheduleCompilerTests.cs` — 40 pure NUnit cases
- `Packages/com.gamecore.unity.runtime/Tests/Time/TimeFixture.cs` — fixture keys, components, native container, real Burst jobs
- `Packages/com.gamecore.unity.runtime/Tests/Time/TimeFixtureWorld.cs` — fixture module, systems, registration
- `Packages/com.gamecore.unity.runtime/Tests/Time/TemporalDriverTests.cs` — 13 EditMode cases
- `Packages/com.gamecore.unity.runtime/Tests/Time/NativeDependencyTests.cs` — 7 EditMode cases
- `Packages/com.gamecore.unity.runtime/Tests/Time/ScheduleAdapterTests.cs` — 7 EditMode cases

Packaging/evidence:

- `artifacts/gc-009/HANDOFF.md` (this file), `artifacts/gc-009/static-checks.log`
- `.meta` for every new runtime-package file and folder (deterministic SHA-256-derived GUIDs, checked by hand against
  the 222 GUIDs already present in the repository; not verified by an import)

Nothing under `Packages/com.gamecore.contracts`, `Packages/com.gamecore.composition`, `Packages/com.gamecore.unity.adapters`,
`dotnet/**`, `unity/**`, `tests/**` or any other task's folder was touched. **No contract addition was needed** —
the whole feature is expressible with the existing 05 shapes (`StageSpec`, `SystemSpec`, `BufferSpec`, `AccessSet`,
`ExecutionPlan`, `BufferBinding`, `AdmissionSequence`, `TimeDebt`, `DiagnosticCode`).

## 4. Exact commands for the Linux build host

Everything below runs from the repository root. Nothing here was run on the authoring host.

### 4.1 Plain dotnet (fast; covers the compiler and the engine-free temporal core)

```sh
dotnet build dotnet/GameCore.sln -c Release
dotnet test  dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gc-009/trx
```

Expected: `GameCore.Planning` now also compiles `Runtime/Scheduling/*.cs`, `GameCore.Execution` now also compiles
`Runtime/Pure/Time/*.cs`, and the 24 new cases in `GameCore.Planning.Tests` run beside the existing suites. The
temporal cutoffs/clocks have no dotnet test project of their own (GC-005 owns `GameCore.Execution.Tests`); their
executable evidence is the Unity EditMode suite in §4.2, and they are compiled by this build so they cannot rot.

### 4.2 Unity EditMode suite (GC-009's Unity half)

```sh
UNITY=/home/worlesenric/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
"$UNITY" -batchmode -nographics \
  -projectPath "$PWD/unity/GameCore.Validation" \
  -runTests -testPlatform EditMode -testFilter GameCore.Unity.Runtime.Tests \
  -testResults "$PWD/artifacts/gc-009/editmode-runtime.xml" \
  -logFile "$PWD/artifacts/gc-009/editmode-runtime.log"

"$UNITY" -batchmode -nographics \
  -projectPath "$PWD/unity/GameCore.Validation" \
  -runTests -testPlatform EditMode -testFilter GameCore.Planning.Tests \
  -testResults "$PWD/artifacts/gc-009/editmode-planning.xml" \
  -logFile "$PWD/artifacts/gc-009/editmode-planning.log"
```

Do not add `-quit` to a test-run command (04 §10). The first command runs the existing GC-005 suites plus the new
`Time` fixtures; the second runs the new pure scheduling suite inside Unity as well as under dotnet.

### 4.3 Documentation gate

```sh
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
python3 tools/check_game_core_csharp.py
```

## 5. Requirement and test coverage mapping

| Requirement (00) | Where implemented | Executable evidence |
|---|---|---|
| P-008 stable ordering and determinism boundary | every ready-set tie-break uses canonical stage-id / system-key bytes; witnesses and rejections are canonically sorted (inner-cycle DFS roots included); the plan hash covers semantic inputs only; the schedule adapter never reorders | `ScheduleCompilerTests.ShuffledDeclarationOrderCompilesToTheSameOrderAndHash`, `IndependentStagesAreOrderedByCanonicalStageIdBytes`, `IndependentSystemsInsideOneStageAreOrderedByCanonicalKeyBytes`, `RejectionsAreDeterministicUnderShuffledDeclarationOrder`, `InnerSystemCycleWitnessesAreStableUnderAShuffledSystemDeclarationOrder`, `AccessConflictWitnessesAreStableUnderAShuffledAccessDeclarationOrder`, `ARepeatedAccessDeclarationDoesNotDuplicateItsWitness`, `TheAdapterUsesTheCompiledOrderAndNotTheDeclarationOrder` |
| P-028 validation before preparation | the compiler rejects inconsistent coalescing, missing required edges, buffer/port mismatches, cycles and unordered access before any schedule exists; the adapter refuses a table it cannot dispatch | the compiler's rejection cases; `ScheduleAdapterTests.AGeneratedRegistrationGapIsRefusedInsteadOfSkippingTheEntry`, `APlaybackPointOutsideTheConsumersPredecessorsIsRefused` |
| P-035 world lifecycle | a paused world seals nothing, fires no clock and does not move epoch or step; a faulted/created/stopping world is not driven at all | `TemporalDriverTests.PauseAppliesEachClocksDeclaredWakePolicyAndAddsNoDebt`, `AnIdleCommandDrivenWorldSealsNoInputAndAdvancesZeroSteps` |
| P-036 temporal models | fixed-step debt/catch-up is GC-005's and is never altered here; the cutoff seals one step's batch and retains the remainder; a command-driven world advances only from admitted commands or registered wakes | `TemporalDriverTests.FixedStepRetainsDebtAcrossFramesAndBoundsCatchUp`, `AFixedStepWorldDoesNotInflateCommandDemand`, `AFixedFrameThatCommitsNoStepReturnsItsSealedInputToTheQueue`, `ARegisteredWakeAdvancesACommandDrivenWorldWithoutAPlayerCommand` |
| P-037 step admission and cutoffs | monotonic host-assigned admission sequences, capacity-bounded sealing, retained remainder, duplicate refusal with the original sequence, bounded-queue backpressure, unrun input returned in its original order | `TemporalDriverTests.OneAdmittedCommandAdvancesExactlyOneStepAndConsumesItsSeal`, `ACommandArrivingAfterTheCutoffWaitsForTheNextStep`, `ADuplicateRequestKeyIsRefusedWithItsOriginalSequence`, `AFullPendingQueueBackpressuresInsteadOfDroppingInput` |
| P-038 clocks | `LogicalStepId`, committed simulation ticks and domain-explicit clocks are distinct; a wake is typed data feeding demand; pause applies each clock's declared policy | `TemporalDriverTests.ADomainClockWakeFiresOnlyWhenTheDomainAdvancesItsClock`, `AFixedDurationClockAdvancesOnlyByCommittedStepTime`, `PauseAppliesEachClocksDeclaredWakePolicyAndAddsNoDebt` |
| P-039 stage registration | coalescing only for matching contract version/owner/affinity; inner system keys are unique per stage; required/optional edges resolved by exact identity; buffer ports validated against their contract | `ScheduleCompilerTests.TwoCompatibleStageDeclarationsCoalesceIntoOneStage`, `CoalescedDeclarationsMustAgreeOnVersionOwnerAndAffinity`, `TwoConflictingDeclarationsOfOneSystemKeyReject*`, `OptionalStageEdgesDisappearWhileRequiredOnesRejectWhenAbsent` |
| P-040 execution plan | required/optional stage edges, inner DAGs, buffer edges, playback order and access sets are compiled into one expanded DAG with a canonical order and a semantic plan hash; unordered overlap is `AmbiguousOrder`, cycles are `Cycle` with a path | `DeclaredEdgesAndBufferEdgesProduceOneStableTopologicalOrder`, `UnorderedWriterConflictRejectsWithBothDeclarationsAsWitness`, `StageCycleIsRejectedWithItsPath`, `InnerSystemCycleIsRejectedWithItsPath`, `ValidDisjointPartitionsMayOverlapWithoutAnEdge`, `ChangingOnlyAnAccessSetChangesThePlanHash`, `ChangingOnlyABufferLifetimeOrCapacityChangesThePlanHash` |
| P-041 concurrency and structural work | a stage fence carries non-component producer handles; a dependent read combines and completes them; the table resets at step admission; playback binds to its producer slots | `NativeDependencyTests.ADependentReadWaitsForAnUnregisteredNonComponentProducer`, `TheStepFenceCarriesTheNonComponentHandle`, `StepAdmissionResetsTheTableAndKeepsThePublicationFenceHonest`, `APlaybackBindingCombinesExactlyItsProducerSlots`, `ADisabledProducerLeavesTheConsumerWithoutAWaitAndTheStepStillCommits` |
| P-043 buffers and bounded work | buffers are validated against their producer/consumer/owner stages, bound for the commit-time drain check, and rejected when active producers have no consumer or a port contradicts its contract | `ABufferWhoseActiveProducersHaveNoConsumerRejects`, `AStagePortWithoutABufferContractRejects`, `AProducerPortForABufferWhoseContractNamesNoSuchProducerRejects`, `TwoBufferContractsForOneBufferRejectAsDuplicate`, `ABufferBindsItsProducersAndConsumerCanonically`, `ABufferWhoseOwnerStageIsAbsentWhileItsProducerIsActiveRejects` |
| P-044 commit | the compiled schedule drives the real guarded step group; drain validation uses the compiled bindings; a committed step completes its non-component fences | `ScheduleAdapterTests.TheCompiledScheduleDrivesTheRealStepGroupInItsCompiledOrder`, `TheCompiledScheduleBecomesAnOrderedDispatchTableWithNativeResourceSlots` |
| P-001/P-059 genre neutrality | the compiler owns no stage list: an empty declaration set compiles to an empty valid schedule, and no schedule needs a combat, physics or animation stage | `ScheduleCompilerTests.AnEmptyDeclarationSetCompilesToAnEmptyValidSchedule`, `NoScheduleRequiresACombatPhysicsOrAnimationStage`, `ScheduleAdapterTests.AnEmptyScheduleInstallsAnEmptyValidTable`, `NoCompiledScheduleRequiresACombatPhysicsOrAnimationStage` |

Suites (08): **TEST-011** (both temporal models, idle worlds, pause, registered wake), **TEST-012** (stage/system
composition, cycle and missing-edge witnesses, coalescing, access conflicts, partitioned writers, buffer edges, job
synchronization, structural playback), **TEST-013** (ordered dispatch, buffer binding and drain semantics through the
compiled table), **TEST-022** (canonical ordering and hash stability under shuffled declaration order). TEST-021's
"no compulsory phase" clause is asserted as the genre-neutrality cases above; the full suite belongs to GC-028.

## 6. Decisions, assumptions and doc ambiguities

Recorded because 00 wins over 05, which wins over 09.

1. **The Planning package owns the compiler; the runtime owns the adapter.** `CompiledSchedule` lives in
   `GameCore.Planning` (task instruction), while the mapping to `GuardedDispatchPlan` must live on the Unity side
   because that type belongs to `GameCore.Unity.Runtime`. The adapter therefore sits in
   `Runtime/Time/ScheduleDispatchAdapter.cs`; it uses no Unity type at all, but its assembly reference direction
   forces it there. 04 §2 allows `GameCore.Unity.Runtime` → Planning, which is the reference this added.
2. **The compiler owns no stage list.** `ScheduleDeclarations.Empty` compiles to an empty *valid* schedule, and the
   adapter installs an empty dispatch table from it. Nothing in the kernel requires a phase table (P-001, P-059);
   the fixture's "cross-cutting" stage is just another declared stage with declared edges.
3. **Witnesses are per-phase and canonically ordered.** The compiler rejects at the first phase that finds a
   problem, so all witnesses in one rejection share one `DiagnosticCode` and are sorted by
   (code, kind, stage, related stage, system, related system, buffer, detail, cycle text). `ScheduleCompilation.Code`
   is the canonically first witness's code, and `CyclePath` is the first stage cycle found. `MaxWitnesses = 64` with
   an explicit `WitnessesTruncated` flag: a large declaration set is never silently over-reported.
4. **Coalescing is identity-plus-contract.** Multiple `StageSpec` declarations of one `StageId` merge only when
   their version, owning package and affinity agree; identical duplicate `SystemSpec` declarations for one key merge,
   and any difference (multiplicity, access set, inner edges) is `DuplicateSystemDeclaration`, because one
   precompiled system type cannot occupy two incompatible positions in one execution plan (04 §4).
5. **Disjointness requires both sides to be partitioned.** Two systems may overlap without a semantic edge only when
   both declare a non-default `PartitionId` and the ids differ; an unpartitioned writer against a partitioned one
   still rejects, which is the conservative reading of P-034/P-040 ("validated, mutually exclusive PartitionId
   assignments, not arbitrary query-predicate claims").
6. **Playback is a fence concern, not a system.** A declared buffer with at least one *active* producer creates a
   playback point on its owning stage; the compiler adds the producer→consumer and owner→consumer stage edges, so
   the consumer's predecessor list already contains the producer and the adapter can simply verify it. A buffer whose
   declared producers are all inactive orders nothing and creates no playback point (03 §3: a consumer may read a
   declared empty stream).
7. **The plan hash covers exactly the semantic inputs** — stage identity/version/owner/affinity, stage and system
   access sets, inner predecessor sets, ordered stage edges, buffer ports, and per-buffer schema/order key/lifetime/
   overflow/cancellation/capacity/producers/consumer — in canonical order, big-endian. Declaration order,
   registration timing and object addresses are excluded, so a shuffled declaration order yields a byte-identical
   hash (TEST-022).
8. **The temporal driver never samples the accumulator.** GC-005's `UnityWorldHost.PumpFrame` owns sampling and
   `ConsumeDemand`; `WorldTimeDriver.PumpFrame` only *wraps* it: it seals the next step's input, feeds admitted
   commands and due wakes as demand, pumps, then advances plugin clocks by the steps that actually committed. Two
   samplers would double-spend debt, which P-036 forbids, so the driver reads `RetainedDebt`/`MaxStepsPerPump` for
   reporting only.
9. **One sealed batch per frame.** The number of steps a fixed-step pump will run is decided inside the host, so the
   driver seals exactly one step's worth (`PerStepCommandCapacity`, default 1 = P-037's command-driven rule). If the
   pump commits no step, the batch returns to the queue with its original admission sequences; if a catch-up frame
   commits several steps, only its first step consumed a sealed batch and the queue retains the input of the rest.
   A step therefore never runs "its" input without having sealed it, which is the honest reading of P-037's
   "consumes the sealed prefix up to its configured capacity and retains the remainder".
10. **A fixed-step world's demand counter is never inflated.** Only a command-driven world is fed
    `NotifyCommandAdmitted`/`RequestWake`; a fixed-step world advances on elapsed time, so its due wakes are collected
    and reported for the caller's adapter stages but do not become demand. Otherwise `PendingDemand` would grow for
    every admitted command in a world that ignores it.
11. **A delayed plugin wake needs a clock the domain advances.** A `LogicalStep` clock ticks only on committed
    steps, so a purely wake-driven idle world cannot advance a delay of one or more steps — which is correct (an idle
    world has no time). A delayed wake is expressed on a `DomainExplicit` clock that a command advances
    (03 §2's `AdvanceDay` shape) or on a `FixedDuration` clock that advances with committed simulation time. The
    tests cover all three kinds.
12. **Pause policy applies to due-but-unconsumed wakes too.** `PluginClockRegistry.ApplyPause` cancels every wake of a
    `Cancel`-policy clock that is neither consumed nor already cancelled, including one that is already due;
    `Defer`-policy clocks keep theirs for the resume. Simulation debt is untouched: it belongs to the accumulator,
    and `WorldTimeDriver.OnWorldResumed` deliberately does not touch it (P-036, O-26).
13. **The non-component dependency path is explicit.** The fixture's producer schedules its Burst job *without*
    writing the handle back into `SystemState.Dependency` — the case 04 §4 names ("external native queues, blob
    replacement, service-owned buffers, and manually scheduled jobs require explicit dependency registration"). The
    handle reaches the consumer only through `NativeDependencyTable`, whose `Store` also forwards it into the
    host-owned `NativeFenceTable` of the producing stage so the step's publication fence really completes it and a
    failed step retains it. The load-bearing assertion is `LastConsumerIncoming == LastProducerHandle`: without the
    table the consumer would combine the default handle and race its producer.
14. **An unproduced resource is reported, not invented.** `CombineIncoming` of a slot nobody produced returns the
    default handle and increments `UnproducedReadCount`; the consumer then observes whatever the container holds and
    the test asserts exactly that (`ADisabledProducerLeavesTheConsumerWithoutAWaitAndTheStepStillCommits`).
15. **Nothing reuses another W2 task's types.** The pure scheduling tests use local frozen declaration fixtures and
    the Unity tests use a private fixture keys/components/systems set; no ownership or stage descriptor from GC-007
    or GC-008 is referenced, as the W2 brief requires. The W2 gate integrates the real ones.

## 7. Known gaps

- **Nothing has been compiled or executed on this host.** The most likely first failures, in order: (a) an asmdef
  reference error for the two new `GameCore.Planning` references, (b) a nullable or member-name slip in the largest
  new files (`ScheduleCompiler.cs`, `PluginClocks.cs`, `WorldTimeDriver.cs`), (c) a Unity-specific API mismatch in
  `NativeDependencyTable` (`JobHandle.CombineDependencies` overload choice) or in the fixture's systems, and (d) a
  `.meta` GUID collision — reviewed by hand against the 222 existing GUIDs but not verified by an import.
- **`dotnet/tools/GameCore.ApiSnapshot` is unaffected**: no contract member was added or changed, so no snapshot
  regeneration is required.
- **No dotnet test project was added for `Runtime/Pure/Time/`.** The sources compile under `dotnet/src/GameCore.Execution`
  (so they build with warnings-as-errors there), but their executable evidence is Unity EditMode only;
  `dotnet/tests/GameCore.Execution.Tests` belongs to GC-005 and adding a file to it was avoided on purpose. If the
  integrator wants a pure-dotnet case, a new `dotnet/tests/GameCore.Time.Tests` project is the clean shape.
- **The new fixture systems live in the Editor-only test assembly.** GC-005 proved managed
  `CreateSystemManaged<T>` systems inside a *runtime* assembly (`GameCore.Unity.Fixtures`). If Entities' source
  generator or that entry point refuses a test-assembly system on the first import, move
  `Packages/com.gamecore.unity.runtime/Tests/Time/TimeFixture{,.World}.cs` into
  `Packages/com.gamecore.unity.runtime/Fixtures/Runtime/Time/` unchanged (the test assembly already references
  `GameCore.Unity.Fixtures`); no test logic changes. The same note is in the file header.
- **The W2 integration gate is not proven here.** This task proves the compiler, both temporal drivers, the native
  dependency carry and the schedule→dispatch install against its own fixtures. Integration with GC-007's real
  ownership validator and GC-008's publisher is the W2 exit condition, not a hidden prerequisite of this task.
- **`PerStepCommandCapacity` is a caller declaration.** The default of one unit per step is P-037's rule; a domain
  that submits an atomic batch envelope should construct the driver with its own bound. No protocol change is implied.
- **No fixed kernel tick rate, no physics determinism and no ordering from install timing** — explicit non-goals, and
  the driver contains no rate, phase or wall-clock ordering.
- **`dotnet/README.md` was not updated.** The W2-prep commit added `dotnet/src/GameCore.Planning` and
  `dotnet/tests/GameCore.Planning.Tests` without adding their rows to that table. Editing the same rows from three
  parallel W2 branches would collide, so the row was left to the W2 integrator.
- **`.meta` files for the new Planning sources were not authored.** `Packages/com.gamecore.planning` currently has no
  `.meta` files at all (W2-prep state), so Unity will generate them on first import; the runtime package's new files
  and folders do have authored metas.

## 8. Build-host actions

1. Run §4.1 and §4.2 in that order and archive the raw logs under `artifacts/gc-009/`.
2. Commit the regenerated `unity/GameCore.Validation/Packages/packages-lock.json` and any `.meta` files Unity creates
   for `Packages/com.gamecore.planning/**` and for any rewritten `.meta` of this change set.
3. If the Editor reports a reference error, the names to check first are `GameCore.Planning` (new in two asmdefs),
   `GameCore.Contracts` and `GameCore.Unity.Runtime`.
4. The W2 gate should be run by the integrator (`tools/run_w1_gate.sh` covers W1; the W2 equivalent is the gate's
   extension point) once all four W2 outputs are merged.

## 9. Review pass (independent readers, before handoff)

**Compile breakers they found (the reason this pass was worth running):**

| Finding | Fix |
|---|---|
| `TimeFixture.cs` marked its jobs `[BurstCompile]` and imported `Unity.Burst`, which the Editor-only test asmdef does not reference | attribute and `using` removed: the jobs are plain `IJob`s, which is all the dependency semantics under test need |
| `TimeFixtureRegistration.Stages()` built the consume `StageRegistration` with three arguments (the diagnostic name had been lost), so `ConsumeStageIndex` sat where a string was required | name restored |
| `TimeFixtureWorld.cs` used `GuardedDispatchPlan`/`GuardedDispatchEntry` without `using GameCore.Execution` | directive added |
| `StepInputCutoff.SealCount` was referenced (increment, decrement, reset, read by a test) but never declared | declared beside the other counters |

**Logic and test defects they found:**

| Finding | Fix |
|---|---|
| `PluginClockRegistry.TryRegister` threw `ArgumentException` when a clock id was re-registered after `TryUnregister` (stale `wakesByClock` key) | `TryUnregister` drops the per-clock list after cancelling, so re-registration is legal |
| `TryUnregister` cancelled only *pending* wakes, so an already-due wake of a removed clock still became demand | the guard now skips only consumed or cancelled wakes, matching `ApplyPause` |
| A `LogicalStep` clock advanced once per *frame* instead of once per *committed step*, so a catch-up frame under-counted it | `PluginClockRegistry.AdvanceStep(committedSteps, simulationTicks)`; the driver passes the steps the frame committed |
| `NativeDependencyTable.Store` overwrote a slot, so a buffer with duplicate producers (legal under P-043) kept only the last handle | the slot and the step fence now combine every producer handle |
| `CompiledScheduleAdapter` rejected a playback point whose owning stage is a third stage, although the compiler legally produces that shape | the owner check now accepts "is the consumer, or is it ordered before the consumer" (new `OrderedBefore` helper) |
| The adapter never verified that an entry's `StageId` designates its fence index, although the index selects the fence slot while the id decides ledger identity and the drain check | added that check with its own witness |
| System-cycle witnesses depended on the systems' *declaration* order (the inner DFS was seeded in list order) | the DFS visits its roots in canonical system-key order |
| Access witnesses tied in the canonical comparer, so their order fell back to the declaration order inside an access set | the comparer now compares schema, both modes and both partitions before the detail text |
| A repeated access declaration produced two identical witnesses and consumed witness budget | a system's access list is stored canonical (deduplicated, sorted), so one conflict is one witness |
| `StageCycleIsRejectedWithItsPath` looked the declared edge up in the wrong direction (`X.RequiredAfter` lists the stages that must run *before* `X`) | the assertion now requires `from ∈ to.RequiredAfter`, which is what a declaration states |
| `TwoCompatibleStageDeclarationsCoalesceIntoOneStage` asserted a two-entry coalesced access summary while both declarations left their access sets empty | both rows now declare their access at stage level |
| `StepAdmissionResetsTheTableAndKeepsThePublicationFenceHonest` pumped the host directly, so no step admission (and therefore no table reset) happened and it asserted 1 where the code produced 2 | the test now drives `WorldTimeDriver.PumpFrame`, the admission point that resets an adopted table |
| `APlaybackPointOutsideTheConsumersPredecessorsIsRefused` was rejected by the index-range check, so the predecessor rule it names was never exercised | the hand-built schedule now has two in-range stages with no edge between them |

Three pure cases were added for the witness-determinism defects found this way (inner-cycle witnesses under a shuffled
system order, access witnesses under a shuffled access-set order, and a repeated declaration not duplicating its
witness), and the deepest-queue counter is now asserted.

Judge calls the reviewers raised are recorded above rather than "fixed" (the sealed-batch-per-frame rule in decision
9, the `dotnet/tests/GameCore.Execution.Tests` boundary, and the Planning `.meta` situation). Nothing else the review
found was left unfixed.

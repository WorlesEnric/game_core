# GC-008 HANDOFF — Publish a derived assembly into an Entities world

Branch: `gc-008` (worktree `/Users/yangcao/wkspace/gc-wt/gc-008`), based on `main` at the Wave 2 prep commit.

**Status of every executable check in this handoff: `NotRun (pending orchestrator build host)`.** This host has no
.NET SDK, no C# compiler, no Mono and no Unity, so nothing here has been compiled, imported or executed. The only
checks that ran are interpreter-level and are recorded in `artifacts/gc-008/static-checks.log`; they are explicitly
**not** a build result.

The gate sentence this task implements, verbatim from `docs/game-core/09-implementation-guide.md` (GC-008):

> A prepared mount changes multiple actual Entities targets in one visible epoch; an observer sees old or new.
> Stale plans and prewrite migration failure preserve old state; injected failure after the first write faults
> without publishing or resuming. Future spawned targets appear fully assembled.

## 1. Summary

One real prepare/apply/publication path now joins composition to ECS, in two halves:

| Half | Assembly | Where | What it owns |
|---|---|---|---|
| Pure | `GameCore.Planning` | `Packages/com.gamecore.planning/Runtime/Plans/` | `ChangePlan` states, expected-revision recheck, inert acquisitions, bounded migration scratch and registration, the frozen ownership/stage descriptor, the canonical effective binding table and the rule set, target slots/generations, and `AssemblyPlanner` |
| Unity | `GameCore.Unity.Runtime` | `Packages/com.gamecore.unity.runtime/Runtime/Assembly/` | `AssemblyPublisher` (fence → migrate → apply → one-reference commit), generated-style `SpawnRecipe` catalog, the stable target registry, the ECS components a publication writes and `PublishedWorldView` |

The publication order is the protocol's, and every step that can fail happens before the first live write except the
apply itself:

1. refuse a world that cannot publish (faulted/stopping/disposed/created) or a step already in progress;
2. refuse an unprepared or already-rejected plan, and recheck its expected revision/base epoch → `StalePlan`;
3. take the new revision/epoch from the adopted composition publication (P-006 increments both at one publication,
   and the two modules share that one series), while nothing is written yet;
4. fence: complete every tracked fence and handle of the old assembly;
5. migrate on scratch: copy the planned live slot values, run the registered pure migrations on the copies; failure
   releases the staged acquisitions and leaves the old assembly and its state untouched;
6. apply: binding rows, state dispositions and target stamps are written to live storage — the postwrite cutoff;
7. commit: build the complete image (bindings + rules + schedule + gates + token), rebind the step group's
   dispatch table at the new epoch, then **one** `Volatile.Write` of the published view through the host.

A postwrite failure (including the injected one) latches the world `Faulted`, publishes no epoch and no image, and
refuses every later publication and step. Spawn and despawn are separate publications with the same fence and the
same single switch; a spawned target's entity is created, stamped and given *all* currently derived rows inside the
fence, so its first visible image is already its complete effective assembly.

## 2. Files created

Pure planning package (`Packages/com.gamecore.planning/`):

| File | Contents |
|---|---|
| `Runtime/Plans/PlanStateMachine.cs` | `PlanPhase`, `PlanTransition`, `PlanStateMachine`: P-027's transition table, the `Rejected`/`Cancelled`/`Faulted`/`PublishedWithCleanupErrors`/`NoChange` distinctions, `RecheckBase`, transition history and refusal counting |
| `Runtime/Plans/PlanHashing.cs` | `PlanHashing`: canonical text hashing for descriptors, tables and plans (SHA-256 over sorted canonical text) |
| `Runtime/Plans/PlanProposals.cs` | Planner inputs: `ProposedCapability`, `ProposedMount`, `ProposedUnmount`, `CompositionProposal`, `TargetDefinition`, `LiveSlotState`, `PlanBudget`, `CompiledSchedule`, `PlannedMigration` |
| `Runtime/Plans/OwnershipStageDescriptor.cs` | The frozen Wave 2 fixture contract: `DescriptorSystem`, `DescriptorStage`, `OwnedSlotSpec`, `OwnershipStageDescriptor` with validation and a canonical `Fingerprint()` |
| `Runtime/Plans/TargetBindingTable.cs` | `TargetBindingRow`, `DerivedBindingRule`, `TargetBindingTable` (canonical order, per-identity uniqueness, `Merge`/`WithoutTarget`, `Fingerprint`) |
| `Runtime/Plans/TargetSlotLedger.cs` | `TargetSlotEntry`, `TargetSlotLedger`: slot allocation, generation advance, handle validation, exhaustion predicate |
| `Runtime/Plans/MigrationScratch.cs` | `ISlotMigration`, `MigrationRegistry`, `MigrationOutcome`, `MigrationScratch`: bounded temporary bytes and pure fallible migrations |
| `Runtime/Plans/InertAcquisitions.cs` | `IPlanResourceGate`, `InertLease`, `AcquisitionCleanup`, `InertAcquisitionSet`: staged leases that cannot emit gameplay before publication, reverse-order release with aggregated failures |
| `Runtime/Plans/AssemblyPlanner.cs` | `PlannedPublication`, `PublicationRecord`, `AssemblyPlanner`: descriptor validation, stale-base rejection, eligibility, P-018 ranking (declared *and* already-effective candidates), P-019 conflict rejection, exact retraction, migration validation, scratch reservation, hard-budget rejection, schedule compilation, plan hashing |

Pure tests (`Packages/com.gamecore.planning/Tests/Plans/`, run by `dotnet test` and Unity EditMode):

- `PlansFixture.cs` — the frozen fixture: keys, descriptor, migrations, a recording resource gate, proposal/plan builders
- `PlanStateMachineTests.cs`, `AssemblyPlannerTests.cs`, `MigrationAndAcquisitionTests.cs`, `TargetSlotLedgerTests.cs`

Unity runtime package (`Packages/com.gamecore.unity.runtime/`):

| File | Contents |
|---|---|
| `Runtime/Assembly/AssemblyComponents.cs` | `TargetIdentity`, `AssemblyStamp`, `CapabilityBinding`, `TargetSlotState`, `AssemblyStorage` read helpers |
| `Runtime/Assembly/TargetRegistry.cs` | `TargetRegistry`: `TargetId` ↔ `Entity` with generations over `TargetSlotLedger`; stale handles refused, `MappingMissCount` |
| `Runtime/Assembly/SpawnRecipeCatalog.cs` | `ISpawnApplier`, `SpawnRecipe`, `DerivedVariantKey`, `SpawnRecipeCatalog` with `TryResolve`/`TryValidateForPublication` |
| `Runtime/Assembly/PublishedWorldView.cs` | `PublishedGate`, `PublishedWorldView`, `PublishedAssemblySlot` (the one switched reference) |
| `Runtime/Assembly/AssemblyPublisher.cs` | `AssemblySpawnRequest`, `AssemblyPublicationReport`, `AssemblyFaultInjection`, `AssemblyPublisher` |
| `Tests/Assembly/GameCore.Unity.Assembly.Tests.asmdef` | Editor-only test assembly for the suite |
| `Tests/Assembly/AssemblyTestFixture.cs` | Fixture keys, descriptor over the fixture world's real system keys, recipes/appliers, migrations, plan and seed helpers |
| `Tests/Assembly/AssemblyPublisherTests.cs` | The eight EditMode cases below, plus the concurrent observer |

GameCore.Composition (the authorized cross-module change that joins the two counters into one series):

| File | Contents |
|---|---|
| `Runtime/Operations/CompositionLaneSeed.cs` | `CompositionLaneSeed`: the published revision/epoch pair a lane starts from, `Unpublished` (0/0) and `InitialAssembly` (1/1), `IsJoined`/`IsConsistent` |
| `Tests/LaneSeedTests.cs` | six cases: the join, the standalone default, revision/epoch moving together for N publications, an inconsistent seed refusal, and the seed being visible on the host and its snapshot |

Tooling and evidence: `tools/run_gc008_gate.sh`, `artifacts/gc-008/static-checks.log`, this file.

`.meta` files were authored for all 22 new files and the 2 new folders, with GUIDs derived deterministically from
the repository-relative path and checked for uniqueness against the 215 GUIDs already committed.

## 3. Files modified (each minimal and explained)

| Path | Change | Why |
|---|---|---|
| `Packages/com.gamecore.unity.runtime/Runtime/GameCore.Unity.Runtime.asmdef` | `+ "GameCore.Planning"` | 04 §2 lists Planning among the runtime assembly's allowed references; the publisher consumes the plan/table/schedule types |
| `Packages/com.gamecore.unity.runtime/Runtime/WorldHost.cs` | `+ assemblySlot` field; `CurrentEpoch` now reads the published view when one is attached; `+ IsPumping`; `+ internal AttachAssemblySlot`; `+ internal TryPublishAssembly`; `+ internal EnterFaulted` | This is the one shared file GC-008 must touch: the world is the authority for the assembly epoch (P-002) and 04 §5 requires the switch to be one serialized nonthrowing reference swap plus one image publication. Nothing else in the host changed; with no publisher attached the host behaves exactly as before |
| `Packages/com.gamecore.composition/Runtime/Operations/CompositionHost.cs` | `+ CompositionLaneSeed seed = default` constructor parameter (and on `CreateDefault`); `Seed` property; the committed state starts at the seed; a seed whose two counters disagree is refused | **the authorized minimal change in GC-004's module**: P-006 has one publication series, so a lane joined to a world must start where that world is. The default is `default(CompositionLaneSeed)` = unpublished 0/0, so every standalone-lane GC-004 test keeps its behaviour |
| `Packages/com.gamecore.composition/Runtime/Operations/CompositionState.cs` | `+ CreateEmpty(world, root, mode, revision, epoch)`; the existing overload delegates at 0/0 | the seeded start value (05 s2) |
| `Packages/com.gamecore.planning/Runtime/Plans/.gitkeep`, `Packages/com.gamecore.planning/Tests/Plans/.gitkeep` | deleted | the folders they held open now contain real sources |
| `Packages/com.gamecore.unity.runtime/Runtime/Integration/WorldCompositionBridge.cs` | `+ optional AssemblyPublisher`; the construction-time seed equality check; `JoinPublishedComposition` after each publication; `PublicationJoinRefusalCount`/`LastPublicationJoinCode`/`LastPublicationJoinDetail` | the W1 glue now joins the composition publication to the world instead of leaving the two counters apart |
| `Packages/com.gamecore.unity.runtime/Fixtures/Runtime/W1GateScenario.cs` | lanes seeded with `CompositionLaneSeed.InitialAssembly`; new fact `CompositionMatchesWorldEpoch`; epoch expectations 1 → 2 and 2 → 3 | the W1 gate's recorded lane-2/world-1 split is gone: the gate now asserts they are equal at every point |
| `unity/.../Tests/W1Gate/W1GateIntegrationTests.cs` | the same expectation updates for the EditMode half | the W1 gate's EditMode suite now asserts the equality |
| `tools/run_gc008_gate.sh` | new | additive: runs the dotnet solution, then this task's two EditMode assemblies (`FULL=1` widens it) |

Not touched: `GameCore.Contracts` (no additions were needed), the reference seams, the API snapshot, `GameCore.sln`
(no new projects — `dotnet/src/GameCore.Planning` and `dotnet/tests/GameCore.Planning.Tests` already exist and pick
up `Runtime/**` and `Tests/**` by glob), and the Unity validation manifest (its `testables` already lists
`com.gamecore.planning` and `com.gamecore.unity.runtime`, so the new assemblies are covered).

## 4. Exact commands for the Linux build host

Everything runs from the repository root.

### 4.1 One command (this task's gate)

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity DOTNET=/usr/bin/dotnet \
  tools/run_gc008_gate.sh
```

It runs `dotnet build dotnet/GameCore.sln -c Release`, `dotnet test dotnet/GameCore.sln -c Release --logger trx
--results-directory artifacts/gc-008/trx`, a Unity package resolve, the `GameCore.Unity.Assembly.Tests` EditMode
suite and the `GameCore.Planning.Tests` EditMode suite. `FULL=1` additionally runs the whole EditMode suite, the
PlayMode suite and the documentation validator. Do not add `-quit` to a test-run command (04 §10).

### 4.2 The pieces

```sh
# pure half
dotnet build dotnet/GameCore.sln -c Release
dotnet test  dotnet/GameCore.Planning.Tests/GameCore.Planning.Tests.csproj -c Release \
  --logger trx --results-directory artifacts/gc-008/trx

# Unity half
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode -testFilter GameCore.Unity.Assembly.Tests \
  -testResults artifacts/gc-008/unity/assembly-editmode.xml -logFile artifacts/gc-008/unity/assembly-editmode.log
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode -testFilter GameCore.Planning.Tests \
  -testResults artifacts/gc-008/unity/planning-editmode.xml -logFile artifacts/gc-008/unity/planning-editmode.log

# what the W1 gate must still pass unchanged on the same revision
UNITY="$UNITY" tools/run_w1_gate.sh
```

### 4.3 Host-side check that did run here

```sh
python3 artifacts/gc-008/check_gc008_sources.py   # printed into static-checks.log
python3 tools/validate_game_core_docs.py --self-test && python3 tools/validate_game_core_docs.py
```

`check_gc008_sources.py` re-uses the repository's own `tools/check_game_core_csharp.py` rules over exactly the 21
files this task added: parenthesis/brace balance, forbidden C# 10+ constructs, `#nullable enable`, TODO/FIXME
markers, missing-return heuristics and the engine-free boundary (no `UnityEngine`/Jobs type in the planning
package). It reports `checked 21 GC-008 C# file(s) / ok`. Both documentation commands pass.

## 5. Requirement and test coverage mapping

| Requirement | Mechanism | Test |
|---|---|---|
| P-002 participants/authority | `AssemblyPlanner` is a pure consumer of immutable snapshots; `AssemblyPublisher` is the only live writer and never creates a second host | `OnePublicationChangesEveryTargetInOneVisibleEpoch` (one publisher per world is asserted by construction: the registry is bound to `world.World.Session`) |
| P-004/P-005 identity and handles | `TargetSlotLedger` + `TargetRegistry`; generations advance on retire; foreign/out-of-range/wrong-generation handles refuse | `TargetSlotLedgerTests.*`, `ADespawnedHandleIsRejectedForeverAndOnlyItsOwnStorageIsDestroyed` |
| P-006 version domains | one publication series: revision and epoch move together on both modules and are equal after the join and after every publication; the logical step never moves; `NoChange` increments nothing | `TheCompositionAndPublishedSeriesAreOneAfterEveryPublication`, `AStaleOrRepeatedCompositionPublicationIsRefusedBeforeAnyWrite`, `OnePublicationChangesEveryTargetInOneVisibleEpoch` (`view.Token.LogicalStepId == 0`), `APlanThatChangesNothingPublishesNoEpoch`, `LaneSeedTests.EveryPublicationMovesRevisionAndEpochTogether`, `PlanStateMachineTests.NoChangeIsAnOutcomeOfATerminalNonErrorState` |
| P-013/P-015/P-018/P-019 eligibility, precedence, policies | recipe-matched declarations, canonical rank over declared *and* effective candidates, `Exclusive`/`Incompatible` rejection with the witnesses | `AssemblyPlannerTests.HigherPriorityWinsAndTheLoserStaysProvenance`, `.ALowerPriorityDeclarationCannotDisplaceAHigherPriorityEffectiveRow`, `.ExclusiveWithTwoCandidatesRejectsAndNamesThem`, `.AnIneligibleRecipeReceivesNothing` |
| P-017/P-033 contribution identity and support | `ContributionKey` per target/slot; one rule slot per recipe/scope/capability; retraction removes exactly the unmounting provider's rows | `AssemblyPlannerTests.UnmountRetractsExactlyTheUnmountingProvidersSupport`, `.OneMountDerivesTheSameBindingForEveryMatchingTarget` |
| P-022 budgets | bounded scratch, hard prepare/apply byte limits, `BudgetExceeded` with the counts | `MigrationAndAcquisitionTests.ScratchReservesWithinItsBudgetAndRefusesBeyondIt`, `AssemblyPlannerTests.ScratchThatCannotReserveTheMigrationsRejectsThePlan`, `.APlanBeyondTheConfiguredHardBudgetRejectsWithItsCounts` |
| P-024 spawn/despawn | pending target → complete rows → one epoch; stale recipe revision refused; despawn retracts and destroys only its own entity | `AFutureSpawnAppearsFullyAssembledInItsFirstVisibleEpoch`, `ADespawnedHandleIsRejectedForeverAndOnlyItsOwnStorageIsDestroyed` |
| P-027 plan states | `Draft→Validated→Prepared→Applying→Published` with the alternatives; only one terminal transition; cancellation cutoff | `PlanStateMachineTests.*` (8 cases) |
| P-028 validation and recheck | descriptor validation, expected revision *and* base epoch recheck, no mutation on rejection | `AssemblyPlannerTests.AStaleExpectedRevisionRejectsWithoutInstallingAnything`, `.AnInvalidDescriptorRejectsThePlan`, `AssemblyPublisherTests.AStalePlanIsRejectedBeforeAnyWrite` |
| P-029 preparation | migration on copied values only; staged leases inert until publication; failure releases them | `MigrationAndAcquisitionTests.StagedLeasesStayInertUntilPublication`, `.ReleasingStagedLeasesRunsInReverseOrderAndAggregatesFailures`, `AssemblyPublisherTests.APrewriteMigrationFailurePreservesTheOldAssemblyAndKeepsRunning` |
| P-030 publication | one visible epoch; observer sees old or new; epoch-bound table and gates installed at the fence | `OnePublicationChangesEveryTargetInOneVisibleEpoch`, `AConcurrentObserverNeverSeesAMixedAssembly` |
| P-031 apply failure | postwrite injection faults the world, publishes no epoch/image and refuses later work | `APostwriteFailureFaultsTheWorldWithoutPublishingOrResuming` |
| P-032 state dispositions | `Retain` on a matching version, `Migrate` on a change, a descriptor with no policy or a missing handler is an error, never zero-initialisation | `AssemblyPlannerTests.AMatchingSchemaVersionOnlyRetainsAndASchemaChangeMigrates`, `.AMissingMigrationHandlerRejectsBeforeAnyWrite`, `.ADescriptorWithoutAVersionChangePolicyRejectsTheMigration`, `.ARunableMigrationIsStagedOnScratchAndRunOnTheCopy`, `MigrationAndAcquisitionTests.AMissingOrMismatchedHandlerIsRefusedWithItsOwnCode` |
| P-040 execution plan | descriptor validated for a backward-only order; schedule/dag/edges in the plan; inner-DAG cycle rejection | `AssemblyPlannerTests.TheCompiledScheduleFollowsTheDescriptorOrderAndCarriesItsBuffers`, `AssemblyPublisherTests.OnePublicationChangesEveryTargetInOneVisibleEpoch` (step group rebound at the new epoch) |
| P-048 cleanup | staged releases aggregate failures and report quarantine instead of a false `Disposed` | `MigrationAndAcquisitionTests.ReleasingStagedLeasesRunsInReverseOrderAndAggregatesFailures`, `.QuarantinedLeasesAreReportedAsRetainedRatherThanReleased` |
| P-050/P-051 discipline | every attempt reports through an `OperationId`; a refusal changes nothing; a faulted world refuses before admission | `AssemblyPublisherTests.AStalePlanIsRejectedBeforeAnyWrite`, `.APostwriteFailureFaultsTheWorldWithoutPublishingOrResuming` |
| TEST-002 | generations, overflow predicate, foreign handles | `TargetSlotLedgerTests.*` |
| TEST-009 | prepared plans, atomic publication visibility, concurrent observer | `AConcurrentObserverNeverSeesAMixedAssembly`, `OnePublicationChangesEveryTargetInOneVisibleEpoch` |
| TEST-010 | state preservation and migration | `AssemblyPublisherTests.OnePublicationChangesEveryTargetInOneVisibleEpoch` (live slot value was replaced by the migrated value; other state untouched), `APrewriteMigrationFailurePreservesTheOldAssemblyAndKeepsRunning` |
| TEST-016 | named fault injection at the prewrite and postwrite boundaries | `APrewriteMigrationFailurePreservesTheOldAssemblyAndKeepsRunning`, `APostwriteFailureFaultsTheWorldWithoutPublishingOrResuming` |
| TEST-020 | generated-style runtime recipes, precompiled appliers, stale recipe revision | `AFutureSpawnAppearsFullyAssembledInItsFirstVisibleEpoch` (the applier is a direct typed call; a recipe prepared against another revision is refused) |

The W2 exit gate additionally integrates the real derivation and stage compiler (`AssemblyPlanner` consumes
`OwnershipStageDescriptor` unchanged) and the player smoke path; that is the orchestrator's gate, not this task's.

## 6. Decisions, assumptions and doc ambiguities

Recorded because 00 wins over 05, which wins over 09.

1. **The planner still needs a descriptor, and it is frozen here.** GC-007 (ownership/partitions) and GC-009
   (schedule compilation) own the real modules; 09 requires each Wave 2 task to work through a frozen
   ownership/stage descriptor fixture until the W2 gate substitutes them. `OwnershipStageDescriptor` is that
   contract shape (slots+owners, stages+systems+backward edges, buffer bindings) with real validation, and both the
   pure and the Unity test fixtures build it from their own literal keys. Substituting the real validator/compiler
   changes the *producer* of this type, not its consumers.
2. **One publication series, and the lane is seeded from the world (rewritten after orchestrator review).** P-006
   defines ONE series: `CompositionRevision` and `AssemblyEpoch` both increment at the same publication, so the
   counters a composition operation reports ARE the counters the world publishes. The first delivery of this task
   published the world assembly at `lane + 1` (the world's initial assembly is epoch 1 per 05 §2 while a joined lane
   reported its pre-publication 0); that offset is **removed**, because two counters related by an offset would leak
   into observers, snapshots, checkpoints (GC-018) and diagnostics.

   The join is now explicit and one-directional: `GameCore.Composition.CompositionLaneSeed` is an additive
   constructor parameter of `CompositionHost` (`CreateDefault(..., seed)`), with `Unpublished` (0/0, the default, so
   every standalone-lane test of GC-004 is unchanged) and `InitialAssembly` (1/1, 05 §2). A lane joined to a world
   is constructed with the world's published pair, so:

   * `AssemblyPublisher.Publish` takes the adopted composition publication's own numbers as the assembly it
     publishes (`nextEpoch = AdoptedLaneEpoch`, `nextRevision = AdoptedLaneRevision`) — no arithmetic;
   * `TryAdoptLanePublication` accepts only exactly the next value of the series on both counters, refuses a
     repeated/consumed publication and refuses a pair whose two counters disagree (`UnsupportedVersion`);
   * `Publish`, `Spawn` and `Despawn` assert equality across the two modules with
     `AssemblyPublisher.MatchesPublishedAssembly(laneRevision, laneEpoch, worldRevision, worldEpoch)`; a mismatch is
     `StalePlan` before any live write;
   * the W1 path has no publisher, so `WorldCompositionBridge` joins the composition publication to the world
     through `UnityWorldHost.TryAdoptPublishedComposition`, which likewise requires exactly the next publication and
     moves the epoch mirror and the published composition revision together. The bridge also refuses at construction
     a lane whose seed does not match the world it is joined to.

   `WorldHost.PublishedCompositionRevision` is the published revision of the same publication as `CurrentEpoch`, so
   the equality is checkable as one pair on the world side too. **Doc ambiguity:** 05 §2 does not say whether a
   joined lane's counters include the world's initial assembly; the reading implemented here is that they do, which
   is what makes the two modules share one series rather than needing a reconciliation step.
3. **Rules are per `(recipe, scope, capability, output slot)`, not per target.** A `DerivedBindingRule` is the
   recipe-level derivation template a future spawn reads; each target's own effective rows live in
   `TargetBindingTable`. A winning candidate therefore *replaces* the rule of its identity instead of appending,
   and the table rejects two rows with one contribution identity, because composing those is a policy decision the
   planner must make first (P-019). Per-target differentiation beyond that (per-target overrides) is out of scope.
4. **Already-effective rows are candidates.** Otherwise a new declaration would silently displace a
   higher-priority provider, which is exactly what P-018 forbids. A repeated declaration that re-derives an
   identical row is not a change, so `NoChange` publishes nothing (P-006).
5. **A publication belongs to an adopted composition publication.** `Publish` uses the pair adopted through
   `TryAdoptLanePublication`, which must be exactly the next publication of the one series; `Spawn`/`Despawn` name
   the pair in their request and are checked by the same rule. `MarkPublicationUsed` records the numbers a
   publication consumed, so "one number per publication, never shared" is enforced rather than asserted.
6. **Stamps are written inside the fence.** A target's `AssemblyStamp` names the epoch being published and is
   written with the rest of the apply stage, so nothing is written after the switch; the switch itself is the only
   visibility point.
7. **A refusal before the write is `Rejected`, a failure after it is `Faulted`.** The injected postwrite fault
   latches the world through the same `EnterFaulted` path the guarded dispatcher uses, so admission closes and no
   step or image follows. A `NoChange` plan is terminal with `Outcome.NoChange` and publishes no epoch.
8. **`AssemblyPublisher` opens the staged gates but does not release them.** A published plan's leases stay live:
   retiring them is the unmount/teardown path (P-048) that GC-016/GC-020 own. Only migration scratch is released at
   commit.

## 7. Known gaps

- **Nothing in this change set has been compiled or executed here.** The likely first failures on the build host
  are (a) a Unity asmdef reference error in the new test assembly, (b) an Entities 1.4.6 API shape I mis-typed in
  the new ECS code, and (c) an off-by-one in an expected count in the new tests (the fixture counts are written out
  as literals deliberately).
- **`dotnet/GameCore.Planning` now compiles the whole `Runtime/Plans/**` set**, which includes types that are only
  used by the Unity half (`CompiledSchedule`, `PlannedPublication`). They are engine-free, so this is legal, but a
  nullable/unused warning there would fail the dotnet build under `TreatWarningsAsErrors`.
- **The Unity assembly is not compiled by `dotnet test`.** `AssemblyPublisher`, `TargetRegistry`,
  `SpawnRecipeCatalog` and the ECS components are proven by the Unity EditMode suite only.
- **No player probe was added.** GC-008 adds no new IL2CPP path; the assembly publication is exercised in a real
  Entities world in the Editor. If the W2 gate wants the publish path in the player, the probe host would need a
  new `-probeAssembly` mode, which I did not add.
- **`unity/GameCore.Validation/Packages/packages-lock.json` is still stale** (the W1 gate recorded this too); the
  gate's resolve step regenerates it. My new `.meta` files were authored by hand with deterministic GUIDs.
- Only the Linux x86_64 IL2CPP profile is targeted; macOS remains unqualified (GC-001, 04 §1).

## 8. Defects found and fixed before the build host

Nothing ran here, so this section records the defects found by reading, not by a failing build. Two independent
reviews (a pure-assembly review and a Unity-assembly review) were run over the new files, and every finding was
fixed in the sources below:

| Defect | Where | Fix |
|---|---|---|
| `OwnedSlotSpec` had no version-change migration key, so the planner's migration disposition read a non-existent member | `Runtime/Plans/OwnershipStageDescriptor.cs`, `AssemblyPlanner.cs` | added `FactoryKey VersionChangePolicy` (7th optional constructor parameter) + `HasVersionChangePolicy`, and the planner now reports `MigrationRequired` explicitly when a descriptor declares no policy for a slot that must migrate (P-032) |
| `TargetRegistry`'s entity index was keyed by `Id128` while every use supplied a `Unity.Entities.Entity` | `Runtime/Assembly/TargetRegistry.cs` | key type corrected to `Entity` |
| `SpawnRecipeCatalog` used `PlanHashing` without importing `GameCore.Planning`, and `Unity.Entities` had been dropped from its using block while `ISpawnApplier` still names `EntityManager`/`Entity` | `Runtime/Assembly/SpawnRecipeCatalog.cs` | both usings present; every new file's using block was then checked mechanically against the namespaces its body actually names |
| `PublishedWorldView` did not expose the targets of its assembly, which the publication test reads | `Runtime/Assembly/PublishedWorldView.cs` | added `IReadOnlyList<TargetId> Targets => Bindings.Targets` |
| The Unity test fixture's `Plan` helper lost its `acquisitions` parameter while its body and two call sites used it | `Tests/Assembly/AssemblyTestFixture.cs` | parameter restored |

Two design gaps were also found while writing the tests and fixed in the planner rather than worked around in the
tests: rule identity composition (decision 3) and ranking against already-effective rows (decision 4). Both are
recorded above with the requirement they implement, and the second is why the `NoChange` case exists.

### Round 1 (orchestrator review): the epoch offset removed

| Defect | Where | Fix |
|---|---|---|
| The publisher published the world assembly at `lane + 1`, i.e. two counters for one publication series | `Runtime/Assembly/AssemblyPublisher.cs` | `TryLaneEpochToWorldEpoch`/`LaneRevisionToWorldRevision`/`InitialAssemblyEpoch`/`LastLaneEpoch`/`LastLaneRevision` deleted; the published pair *is* the adopted composition pair, and `MatchesPublishedAssembly` asserts the cross-module equality (mismatch → `StalePlan`, no write). The drift test `TheCompositionAndPublishedSeriesAreOneAfterEveryPublication` and the refusal test `AStaleOrRepeatedCompositionPublicationIsRefusedBeforeAnyWrite` cover it |
| A lane joined to a world started at the pre-publication 0/0, so the two counters could never agree without arithmetic | `Packages/com.gamecore.composition` (GC-004) | additive `CompositionLaneSeed` on `CompositionHost`/`CreateDefault` + `CompositionState.CreateEmpty(world, root, mode, revision, epoch)`; standalone lanes keep 0/0, so GC-004's own tests are unchanged |
| The W1 path had no publisher, so nothing joined the lane's publication to the world | `Runtime/Integration/WorldCompositionBridge.cs`, `Runtime/WorldHost.cs` | the bridge adopts the composition publication (through the publisher when present, otherwise `UnityWorldHost.TryAdoptPublishedComposition`, which moves the epoch mirror and `PublishedCompositionRevision` together) and refuses a lane whose seed disagrees with its world at construction |
| The W1 gate asserted lane epoch 2 against world epoch 1 | `Fixtures/Runtime/W1GateScenario.cs`, `Tests/W1Gate/W1GateIntegrationTests.cs` | lanes seeded at 1/1; expectations now 2/2 after the first publication and 3/3 after the second, with `CompositionMatchesWorldEpoch` asserted in both halves |
| The spawn request conflated "the revision the variant was prepared against" (P-024, must equal the published one) with "the publication this spawn lands on" (must be the next one), so its recipe validation could never pass | `Runtime/Assembly/AssemblyPublisher.cs` | the request carries both explicitly (`PreparedRevision` and the `LaneRevision`/`LaneEpoch` pair); the variant is validated against `PublishedRevision`, the epoch comes from the pair |
| The availability check refused the pair a spawn or despawn had just adopted, so both paths were dead after an adoption | `Runtime/Assembly/AssemblyPublisher.cs` | `IsUnusedPublication(revision, epoch, adopting)`: the pending-adoption clause applies only to the adoption path |
| `PublishedAssemblySlot.SwitchCount` did not count the initial view although its own doc and `PublicationCount` claimed it did | `Runtime/Assembly/PublishedWorldView.cs` | the counter starts at 1 for the constructed view |
| The series-drift test re-proposed an identical mount, which is a `NoChange` and publishes nothing | `Tests/Assembly/AssemblyPublisherTests.cs` | each iteration raises the priority, so every publication is a real change (P-018) |

Also fixed during the first pass: `PlanStateMachine.TryNoChange` (a `NoChange` plan must be terminal without being an
error), the hard prepare/apply byte-limit rejection (P-022), and the observer test now waits for its thread to take
its first view instead of relying on scheduling.

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
3. decide the new revision/epoch (P-006 increments both at one publication) while nothing is written yet;
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

Tooling and evidence: `tools/run_gc008_gate.sh`, `artifacts/gc-008/static-checks.log`, this file.

`.meta` files were authored for all 22 new files and the 2 new folders, with GUIDs derived deterministically from
the repository-relative path and checked for uniqueness against the 215 GUIDs already committed.

## 3. Files modified (each minimal and explained)

| Path | Change | Why |
|---|---|---|
| `Packages/com.gamecore.unity.runtime/Runtime/GameCore.Unity.Runtime.asmdef` | `+ "GameCore.Planning"` | 04 §2 lists Planning among the runtime assembly's allowed references; the publisher consumes the plan/table/schedule types |
| `Packages/com.gamecore.unity.runtime/Runtime/WorldHost.cs` | `+ assemblySlot` field; `CurrentEpoch` now reads the published view when one is attached; `+ IsPumping`; `+ internal AttachAssemblySlot`; `+ internal TryPublishAssembly`; `+ internal EnterFaulted` | This is the one shared file GC-008 must touch: the world is the authority for the assembly epoch (P-002) and 04 §5 requires the switch to be one serialized nonthrowing reference swap plus one image publication. Nothing else in the host changed; with no publisher attached the host behaves exactly as before |
| `Packages/com.gamecore.planning/Runtime/Plans/.gitkeep`, `Packages/com.gamecore.planning/Tests/Plans/.gitkeep` | deleted | the folders they held open now contain real sources |
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
| P-006 version domains | revision and epoch move at one publication; the logical step never moves; `NoChange` increments nothing | `OnePublicationChangesEveryTargetInOneVisibleEpoch` (`view.Token.LogicalStepId == 0`), `APlanThatChangesNothingPublishesNoEpoch`, `PlanStateMachineTests.NoChangeIsAnOutcomeOfATerminalNonErrorState` |
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
2. **Joining the two epoch counters (the W1 gate's recorded gap).** 05 §2 gives the world's initial assembly
   epoch **1** while a composition lane joined right after creation still reports its pre-publication counters as
   **0**, so a lane publication at composition epoch `E` publishes world assembly epoch `E + 1`
   (`AssemblyPublisher.TryLaneEpochToWorldEpoch`, `LaneRevisionToWorldRevision`, `InitialAssemblyEpoch = 1`). The
   invariant the publisher enforces is one number per publication and no two publications sharing a number: a
   mapped epoch that does not advance the published one is refused as `StalePlan`, a repeated lane publication is
   refused, and `LastLaneEpoch`/`LastLaneRevision` record which composition publication produced which world
   assembly. Dropping the offset requires GC-004 to seed a joined lane's committed revision/epoch at the world's
   initial assembly value; I did not change `CompositionHost` because this task does not own it. **Ambiguity:**
   05 §2 does not say whether a lane's counters include the world's initial assembly; I chose the reading that
   keeps one publication series and states it in code and tests.
3. **Rules are per `(recipe, scope, capability, output slot)`, not per target.** A `DerivedBindingRule` is the
   recipe-level derivation template a future spawn reads; each target's own effective rows live in
   `TargetBindingTable`. A winning candidate therefore *replaces* the rule of its identity instead of appending,
   and the table rejects two rows with one contribution identity, because composing those is a policy decision the
   planner must make first (P-019). Per-target differentiation beyond that (per-target overrides) is out of scope.
4. **Already-effective rows are candidates.** Otherwise a new declaration would silently displace a
   higher-priority provider, which is exactly what P-018 forbids. A repeated declaration that re-derives an
   identical row is not a change, so `NoChange` publishes nothing (P-006).
5. **A publication belongs to an adopted lane publication.** `Publish` uses the counters adopted through
   `TryAdoptLanePublication`; a publication without one maps to a non-advancing epoch and is refused rather than
   guessed. `Spawn`/`Despawn` take the lane counters in their request.
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

Also fixed during that pass: `PlanStateMachine.TryNoChange` (a `NoChange` plan must be terminal without being an
error), the hard prepare/apply byte-limit rejection (P-022), and the observer test now waits for its thread to take
its first view instead of relying on scheduling.

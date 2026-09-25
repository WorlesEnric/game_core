# GC-007 HANDOFF — state authority and bounded request contracts (Wave 2)

Branch: `gc-007` (worktree `/Users/yangcao/wkspace/gc-wt/gc-007`).

**Status of every executable check below: `NotRun (pending orchestrator build host)`.** This machine has no Unity,
no .NET SDK, no Mono and no C# compiler, so nothing in this change set has been compiled, imported or executed here.
The only checks that ran are interpreter-level static checks, recorded in `artifacts/gc-007/static-checks.log`; they
are explicitly **not** build or test results.

## 1. Summary

Delivered the enforceable authority model and the bounded request/message path of one owned world.

1. **Pure ownership module** — `Packages/com.gamecore.planning/Runtime/Ownership/` (assembly `GameCore.Planning`,
   engine-free, compiled by `dotnet/src/GameCore.Planning`):
   - one logical owner per authoritative domain, with a canonical `OwnershipConflict` rejection when two writers of
     different owners claim one domain;
   - generated, mutually exclusive partitions (`PartitionIdGenerator`, deterministic from stable identities), and the
     order-or-partition proof (`AmbiguousOrder` when neither a directed required edge / compiled stage order nor a
     validated disjoint partition exists);
   - field-to-component ownership (`ComponentOwnershipMap`): one physical owner per component layout, one slot per
     field, competing initializers rejected;
   - slot policies validated **before any migration executor exists** (`SlotPolicyValidator`: `TransferTo` needs a
     transfer policy, a reset needs an explicit reason, a version change needs a declared *and registered* migration,
     `RemoveDerived` only on disposable derived data);
   - support sets and derived lifetime (`SupportSetRegistry`): removing one provider removes exactly its support, the
     final removal follows the declared last-support policy, a base recipe requirement wins, and a refused retraction
     leaves the set unchanged.
2. **Pure message contracts** — `Packages/com.gamecore.unity.runtime/Runtime/Pure/Messages/` (namespace
   `GameCore.Execution.Messages`, compiled by `dotnet/src/GameCore.Execution` and by the Unity assembly):
   `StepMessage`/`MessageOrderKey` (canonical merge independent of producer scheduling), one consumer-owner rule,
   `BoundedMessageBuffer` + `BoundedBufferRules` (capacity, overflow, drain, carry-over), `StepMessageSchedule`
   (sealed input prefix, deferred structural playback, commit-time drain validation), `CommandRouteTable`,
   `RequestLedger` (dedup, monotonic issuer sequences, capacity backpressure, terminal result kinds, bounded
   retention), `CommandPayloadReaders` (generated typed readers, no reflection) and `CommittedEventStore` +
   `StepOutputCollector` (committed events and cursors).
3. **Unity message runtime** — `Packages/com.gamecore.unity.runtime/Runtime/Messages/`:
   `NativeMessageLanes`/`NativeMessageLane` (bounded `NativeArray<StepMessage>` rows plus a bounded payload arena a
   Burst job writes into) and `WorldMessagePlane` (routes, ledger, schedule, lanes, output; host admission, owner
   ports, commit/reject, canonical drain, liveness revalidation).
4. **GC-005 wiring** (four minimal, additive changes; see §5): the host creates the plane from its registration and
   implements `ICommandIngress`; the driver seals step input and validates declared message buffers at commit;
   `GuardedSystemGroup` reports what actually ran; the world registry can resolve the host of a Unity world.
5. **Tests**: pure NUnit under `Packages/com.gamecore.planning/Tests/Ownership/` (authority, partitions, component
   ownership, slot policies, support sets) and `dotnet/tests/GameCore.Execution.Tests/MessagePlaneTests.cs`
   (buffers, schedule, ledger, events, typed readers); Unity EditMode tests under
   `Packages/com.gamecore.unity.runtime/Tests/Messages/` (assembly `GameCore.Unity.Messages.Tests`) that drive real
   worlds.

## 2. Files created

Pure ownership (`Packages/com.gamecore.planning`):

- `Runtime/Ownership/OwnershipDeclarations.cs` — `WriterDeclaration`, `SlotAuthorityOptions`, `SlotAuthorityDeclaration`,
  `ComponentLayoutDeclaration`, `OwnerAuthorityDeclaration`
- `Runtime/Ownership/PartitionGeneration.cs` — `PartitionAssignment`, `PartitionIdGenerator`
- `Runtime/Ownership/ComponentOwnershipMap.cs` — `ComponentOwnershipEntry`, `ComponentOwnershipMap`
- `Runtime/Ownership/OwnerAuthorityValidator.cs` — `DomainOwnerRecord`, `OwnerAuthorityMap`, `OwnershipReport`,
  `OwnerAuthorityValidator`
- `Runtime/Ownership/SupportSetRegistry.cs` — `SupportSetOutcome`, `DerivedLifetimeDecision`, `SupportSetDelta`,
  `SupportSetRegistry`
- `Runtime/Ownership/SlotPolicyValidator.cs` — `SlotAuthoritySet`, `SlotChangeKind`, `SlotPolicyRequest`,
  `ISlotMigrationRegistry`, `SlotMigrationRegistry`, `SlotPolicyOutcome`, `SlotPolicyResult`, `SlotPolicyValidator`
- `Tests/Ownership/OwnershipFixtures.cs`, `OwnerAuthorityValidatorTests.cs`, `PartitionAndComponentOwnershipTests.cs`,
  `SlotPolicyValidatorTests.cs`, `SupportSetRegistryTests.cs`

Pure message layer (`Packages/com.gamecore.unity.runtime/Runtime/Pure/Messages/`):

- `MessageContracts.cs` — `MessageKind`, `MessageOrderKey`, `MessageOrderComparer`, `StepMessage`,
  `BufferAppendOutcome`, `MessageBufferDescriptor`, `BufferReadPort`, `DeferredStructuralOperation`,
  `DeferredOperationComparer`
- `BoundedMessageBuffer.cs` — `IMessageTargetLiveness`, `AlwaysLiveTargets`, `MessageDrainReport`,
  `NextStepCarryReport`, `RequestOutcome`, `BoundedBufferRules`, `BoundedMessageBuffer`
- `StepMessageSchedule.cs` — `IStepDispatchFacts`, `InputSeal`, `StepBufferCommitReport`, `StepMessageSchedule`
- `CommandRoutes.cs` — `CommandRoute`, `CommandRouteTable`
- `RequestLedger.cs` — `RequestOrigin`, `RequestAdmissionKind`, `RequestAdmission`, `RequestRow`, `RequestLedger`
- `TypedPorts.cs` — `ICommandPayloadReader<T>`, `PayloadDecodeOutcome`, `CommandPayloadReaders`, `PayloadWriter`,
  `PayloadReader`
- `CommittedEventStore.cs` — `StagedCommittedEvent`, `StepOutputCollector`, `CommittedEventStore` (moved here from
  `Runtime/Messages/` so the dotnet suite can execute it; it uses no Unity type)

Unity message runtime (`Packages/com.gamecore.unity.runtime/Runtime/Messages/`):

- `NativeMessageLanes.cs` — `NativeMessageLane`, `NativeMessageLanes`
- `WorldMessagePlane.cs` — `MessagePlaneRegistration`, `WorldMessagePlane`

Unity tests (`Packages/com.gamecore.unity.runtime/Tests/Messages/`):

- `GameCore.Unity.Messages.Tests.asmdef` (Editor-only, `UNITY_INCLUDE_TESTS`; references `GameCore.Contracts`,
  `GameCore.Planning`, `GameCore.Unity.Runtime`, `Unity.Collections`, `Unity.Entities`, both test runners)
- `MessagePlaneFixture.cs` — `MessageFixtureKeys`, `MessageProbeState`, `ProbePayload`/`ProbePayloadReader`,
  `ProbePayloads`, `MessagePayloadJob`, `ILaneProducerProbe`, `LaneProducer`, `MessageProducerASystem`,
  `MessageProducerBSystem`, `MessageOwnerSystem`, `MessageConsumerSystem`, `MessageFixtureRegistration`
- `WorldMessagePlaneTests.cs` — 10 EditMode cases

Pure dotnet tests:

- `dotnet/tests/GameCore.Execution.Tests/MessagePlaneTests.cs` — `BoundedMessageBufferTests`, `RequestLedgerTests`,
  `CommittedEventStoreTests`, `CommandPayloadReaderTests`

Evidence:

- `artifacts/gc-007/HANDOFF.md` (this file), `artifacts/gc-007/static-checks.log`

`.meta` files with deterministic GUIDs were added for every new file and folder (28 new GUIDs, all unique against the
243 in the repository; `artifacts/gc-007/static-checks.log` §4).

## 3. Files modified (each minimal and additive)

| Path | Change | Why |
|---|---|---|
| `Packages/com.gamecore.unity.runtime/Runtime/WorldHost.cs` | `IWorldExecutionContext.Messages`; host field + plane creation from the registration; `Messages` property; `ICommandIngress.Submit` (host admission + demand); committed-event staging inside `TryCommitStep`; plane disposal in `Stop`; native-plane resource record; `UnityWorldRegistry.TryGetByEntityWorld` | the host is the only admission and publication authority (P-002, P-042); the plane must publish with the step image and be torn down with the world |
| `Packages/com.gamecore.unity.runtime/Runtime/Execution/UnityExecutionDriver.cs` | seals step input before dispatch; validates declared message buffers at commit; ends the step's buffer state after commit | P-037 cutoff and P-043/O-16 commit validation belong to step admission/commit |
| `Packages/com.gamecore.unity.runtime/Runtime/Execution/GuardedDispatch.cs` | `GuardedSystemGroup` implements `IStepDispatchFacts` (`Ran`, `RanStage`) | commit-time drain validation must use what actually ran, not what was installed |
| `Packages/com.gamecore.unity.runtime/Runtime/UnityWorldRegistration.cs` | two trailing optional constructor parameters (`MessagePlaneRegistration? messages`, `CommandPayloadReaders? messageReaders`) and the matching properties | a world without a plane keeps working unchanged (all existing callers compile); a world with one gets generated routes and typed readers |
| `Packages/com.gamecore.planning/{Runtime,Tests}/Ownership/.gitkeep` | deleted | the folders now hold real sources |

No file under `Packages/com.gamecore.contracts`, `dotnet/GameCore.sln`, `dotnet/src/*.csproj`,
`dotnet/tests/*.csproj` or `unity/GameCore.Validation/Packages/manifest.json` was touched: no new project, no new
`testables` entry and **no contract addition** was needed (the required shapes — `CommandEnvelope`,
`CommandAdmissionReceipt`, `RequestResult`, `CommittedEvent`, `CommittedEventPage`, `ICommandIngress`,
`ICommittedEventReader`, `StateSlotSpec`, `BufferSpec`, `AccessDeclaration.PartitionId` — already exist). The only
new contract *implementations* are `UnityWorldHost : ICommandIngress` and `GuardedSystemGroup`.

## 4. Exact build/test commands for the Linux build host

From the repository root.

```sh
# ---- 1. everything that is Unity-free (contracts, composition, execution, planning) ----
dotnet build dotnet/GameCore.sln -c Release
dotnet test  dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gc-007/trx

# the two suites this task adds to:
dotnet test dotnet/tests/GameCore.Execution.Tests/GameCore.Execution.Tests.csproj -c Release \
  --filter "FullyQualifiedName~GameCore.Execution.Tests"    # includes MessagePlaneTests.cs
dotnet test dotnet/tests/GameCore.Planning.Tests/GameCore.Planning.Tests.csproj -c Release \
  --filter "FullyQualifiedName~GameCore.Planning.Tests.Ownership"

# ---- 2. Unity package resolve (first import also generates/completes .meta files) ----
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation -quit -logFile artifacts/gc-007/unity-resolve.log

# ---- 3. the GC-007 EditMode assembly (real worlds) ----
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode -testFilter GameCore.Unity.Messages.Tests \
  -testResults artifacts/gc-007/unity/messages-editmode.xml -logFile artifacts/gc-007/unity/messages-editmode.log

# ---- 4. the pure planning tests as Unity EditMode tests (same sources as the dotnet suite) ----
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode -testFilter GameCore.Planning.Tests \
  -testResults artifacts/gc-007/unity/planning-editmode.xml -logFile artifacts/gc-007/unity/planning-editmode.log

# ---- 5. no GC-005 regression: the neighbouring suites this task wired into ----
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode -testFilter GameCore.Unity.Runtime.Tests \
  -testResults artifacts/gc-007/unity/runtime-editmode.xml -logFile artifacts/gc-007/unity/runtime-editmode.log
```

Do not add `-quit` to a test-run command (04 §10).

## 5. Requirement and test coverage mapping

| Requirement | Where | Case |
|---|---|---|
| P-028 validated before preparation | pure | `OwnerAuthorityValidator.Validate`, `SlotPolicyValidator.ValidateDeclaration`, `StepMessageSchedule.TryDeclare` |
| P-032 state dispositions | pure | `SlotPolicyValidatorTests` (migration required / registered, permitted reset + reason, transfer, last-support), `SupportSetRegistryTests` |
| P-033 shared support and component lifetime | pure | `SupportSetRegistryTests` (exactly-one retraction, final policy, recipe requirement, rejected retraction unchanged), `PartitionAndComponentOwnershipTests` (two physical owners, two slots per field, unknown field, valid mapping) |
| P-034 state authority | pure + Unity | `OwnerAuthorityValidatorTests.TwoUndeclaredWritersOfOneDomainReject`, `...TwoValidatedDisjointPartitionsOfOneOwnerAreLegal`, `...ADirectOwnerUpdateOfAnExistingComponentNeedsNoQueueOrPartition`; `WorldMessagePlaneTests.ACommandRoutesToItsOwnerAndCommitsAtTheStepBoundary` |
| P-037 step admission, dedup, observable rejection | pure + Unity | `TheInputSealIsMonotonicAndCarriesTheAdmittedPrefix`, `ADuplicateRequestKeyReturnsItsRecordedResult`, `TerminalResultsDistinguishCommitmentFromRejectionAndCancellation`; `ADuplicateCommandReturnsItsRecordedResultAndExecutesOnce` |
| P-041 bounded lanes, jobs, canonical merge, deferred playback | pure + Unity | `DeferredStructuralPlaybackIsCanonicalAndWaitsForItsProducer`, `AFreshRowIsAcceptedAndMergedInCanonicalOrder`; `ShuffledProducerOrderCommitsTheSameResult`, `ValidatedPartitionsExecuteWithRealJobsSafely` |
| P-042 commands/requests, `RequestResult` | pure + Unity | `AFreshAdmissionIsAcceptedAndNotYetCommitted`, `...TheCommittedCursorIsAttachedAfterPublication`; `AdmissionAcceptanceIsDistinguishableFromGameplayCommitment` |
| P-043 buffers, capacity, backpressure, drain | pure + Unity | `AFullReliableBufferRefusesWithoutMutatingItself`, `ALossyBufferDropsAndCountsInsteadOfRefusing`, `UndrainedReliableRowsFailTheCommitValidation`, `ADeclaredLifetimePairAndDuplicateIdentityReject`, `AnUnconsumedNextStepQueueCarriesForwardWithTerminalCancellations`; `OverflowRejectsBeforeMutationAndLeavesStateUnchanged`, `ACommandThatOverflowsItsIngressLaneIsRefusedBeforeTheStep`, `AReliableBufferThatCannotBeDrainedFaultsInsteadOfPublishing` |
| P-044 commit and domain transactions | Unity | `TryCommitStep` stages events and publishes image + events together; `ACommandRoutesToItsOwnerAndCommitsAtTheStepBoundary`, `AReliableBufferThatCannotBeDrainedFaultsInsteadOfPublishing` |
| P-045 observation, committed events, retention | pure + Unity | `OneBuildProducesCanonicallyOrderedEventsForTheCommittedStep`, `TheStoreReportsGapsInsteadOfFalseContinuity`, `ARepeatedPageReadIsCountedAsARedelivery`, `APublicationFromAnotherWorldIncarnationAndANonIncreasingSequenceAreRefused`; `CommittedEventsExposeOnlyAtPublicationAndRetentionReportsAGap`, `ABoundedEventStreamKeepsItsRetentionBound` |
| P-050 identity/retention of operations | pure | `AReusedKeyWithDifferentInputIsAnIdempotencyConflict`, `CapacityBackpressureRecordsNothing`, `ASequenceAtOrBelowTheIssuerHighWaterIsRefusedAsExpired`, `BoundedResultRetentionTurnsDroppedIdentitiesIntoResultExpired` |
| P-008/P-022 determinism and bounds | pure + Unity | `PartitionAndComponentOwnershipTests.AGeneratedPartitionIdIsStableAndWriterSpecific`, `TheReportIsCanonicalAndStableAcrossInputPermutations`, `PendingRowsAreReportedInCanonicalAdmissionOrder`; `ShuffledProducerOrderCommitsTheSameResult` |
| P-047 retired route / deferral | pure | `UnknownAndRetiredRoutesAndSchemaMismatchesAreRefused`, `CancellingPendingWorkLeavesSettledRowsUntouched`, `AnUnconsumedNextStepQueueCarriesForwardWithTerminalCancellations` |
| P-058/04 s8 IL2CPP (no reflection) | Unity | `CommandPayloadReaderTests.ARegisteredReaderDecodesItsSchemaAndAMissIsReported`; `MessageOwnerSystem` decodes through `plane.Readers` only |
| TEST-009 | Unity | publication/visibility assertions in `ACommandRoutesToItsOwnerAndCommitsAtTheStepBoundary` and the fault case |
| TEST-010 | pure | `SlotPolicyValidatorTests`, `SupportSetRegistryTests` |
| TEST-013 | pure + Unity | the authority, request, buffer and direct-write cases listed above |
| TEST-014 | pure + Unity | the committed-event and cursor cases listed above |

## 6. Decisions, assumptions and doc ambiguities

Recorded because 00 wins over 05, which wins over 09.

1. **Where the plane's publication hooks live.** GC-005 already owned step admission, commit validation and the step
   image. The plane therefore does not publish anything itself: the driver seals input and validates declared
   buffers, and the host stages the step's committed events into the same `StepCommitEvent` that
   `StepPublicationStore` publishes (`ConfirmPublished` only after `Publish` succeeded). Events and image are
   created together and exposed together, and a step that faults publishes neither.
2. **Event `step` vs the message's `step`.** A message carries the *executing* step id, because that is the world's
   current step while it runs (and it is the step the ledger records as the terminal-result origin). A committed
   event carries the *committed* (post-increment) step id, because that is the token of the published image, exactly
   as GC-005 already stamped the image. Both are asserted explicitly
   (`row.TerminalStep == plane.ExecutingStep`, `page.Events[0].Step == host.CurrentStep`).
3. **Admission acceptance is not commitment (P-042).** `SubmitCommand` returns `Accepted`; only an owner commit
   returns `Committed`, and the committed event cursor is attached *after* publication
   (`RequestLedger.TryUpdateCommittedCursor`), never guessed at commit time.
4. **An internal `Request` has no ledger row.** P-042 calls a typed inter-system request a transient message, not a
   request with a recorded result, so `WorldMessagePlane.Commit` settles the ledger only for `MessageKind.Command`
   and stages the event for both. A `Request` still cannot silently vanish: it occupies a bounded lane row and a
   refused append is a counted value.
5. **Backpressure rejects before mutation, twice.** (a) The bounded lane refuses an admitted command during
   admission: the lane is left untouched and the command's row is settled as `Rejected(BudgetExceeded)`, which is an
   observable rejected result in the step (P-037). (b) A producer's lane refusal never mutates world state
   (`OverflowRejectsBeforeMutationAndLeavesStateUnchanged`).
6. **An unconsumed reliable buffer faults the world.** The driver validates declared message buffers after dispatch
   and before the image is built, using the same fail-stop rule as GC-005's plan drain check. Authoritative writes
   the owner already performed are *not* rolled back (P-031); the test asserts exactly that.
7. **Partition disjointness is generated, never guessed.** `PartitionIdGenerator` derives the id from
   `(salt, owner, domain, domain version, writer key, key version)` with SHA-256 and takes the first 128 bits, so
   the value is stable across runs and writer-specific (P-008). A per-partition writer that declares no explicit
   partition receives the derived id; a whole-schema writer against a partitioned one is never disjoint
   (P-040).
8. **Order proof.** Two writers of one domain are ordered when the declaration supplies a directed required edge or
   when the supplied compiled stage order places them at different positions. Without a stage order (GC-009's
   compiler output is optional here) only validated partitions prove disjointness; otherwise the pair is
   `AmbiguousOrder`. This is why every ordering case in the tests passes an explicit stage order when it means to
   rely on one.
9. **`GC-007` stays inside its own folders.** All new logic lives in `Runtime/Ownership/`, `Runtime/Pure/Messages/`
   and `Runtime/Messages/`; the Unity test world lives in the package's own `Tests/Messages/` assembly instead of
   `Fixtures/Runtime` (GC-005's folder). No GC-006/GC-008/GC-009 type is referenced: the pure tests use their own
   known ownership/slot descriptor fixture, and the Unity fixture uses its own generated-style table.
10. **`CommittedEventStore` is engine-free and was moved to `Runtime/Pure/Messages/`.** It had been written under
    `Runtime/Messages/`; nothing in it touches Unity, so it belongs with the rest of the dotnet-testable layer and is
    now executed by `GameCore.Execution.Tests`.

## 7. Known gaps

- **Nothing in this change set has been compiled, imported or executed.** The first build-host failures to expect
  are (a) a Unity asmdef reference problem in `GameCore.Unity.Messages.Tests` (it references `GameCore.Planning`,
  which the runtime assembly does not), (b) nullable warnings in the new Unity test assembly (Unity does not treat
  warnings as errors; the dotnet projects do), (c) `.meta` GUIDs — authored here, verified unique, but Unity may
  rewrite them on first import (commit any rewrite), and (d) `unity/GameCore.Validation/Packages/packages-lock.json`
  is stale in the same way GC-005's handoff recorded; the resolve step regenerates it.
- **No test executable covers the plane's retention *configurability* end to end**; the bounded-event-stream case
  builds a plane directly instead of through a world registration, because the fixture uses the fixture's own bounds.
- **Slot migration executors do not exist yet.** `SlotPolicyValidator` validates the declaration and the requested
  disposition, and requires a *registered* executor before reporting `Migrate`; the generated `Migrate_*` bodies are
  a later task's (09 names migration scratch under GC-008).
- **The plane has no composition-liveness source yet.** `IMessageTargetLiveness` is a substitution point;
  `AlwaysLiveTargets` is the default, and `WorldMessagePlane.SetTargetLiveness` is how a composition consumer
  (GC-008/GC-014) will feed removed/live targets into a carried next-step queue. Until then a carried queue cannot
  observe a removed target, which is why the cancellation path is covered by the pure suite instead of a world test.
- **Retained committed events are not part of a checkpoint**; nothing in this task persists them.
- **`GameCore.Execution` is compiled twice** (Unity assembly `GameCore.Unity.Runtime` for `Runtime/**`, plain
  `dotnet/src/GameCore.Execution` for `Runtime/Pure/**`); the dotnet suite therefore proves the pure message layer,
  while the Unity-only plane/lanes are proven by `GameCore.Unity.Messages.Tests` on the build host.
- **Contract additions: none.** The API-compat check reports the same three pre-existing additions
  (`EnvelopeError.DuplicateField`, `EnvelopeError.MissingRequiredField`, `FactoryKind.Handler`) listed by earlier
  tasks; this task added no member to `GameCore.Contracts`.

# GC-016 HANDOFF — complete immutable observation, provenance and diagnostics (Wave 5)

Branch `gc-016` (worktree `/Users/yangcao/wkspace/gc-wt/gc-016`), based on `main` (`f28a4af`, the Wave 4 integration
gate).

**Status of every build/test command in this document: `NotRun (pending orchestrator build host)`.** This host has no
.NET SDK, no C# compiler, no Mono and no Unity, so nothing in this change set has been compiled, imported or
executed here. What did run is interpreter-level only and is recorded verbatim in
`artifacts/gc-016/static-checks.log`: `python3 tools/check_game_core_csharp.py` (380 files, 3 of them the new
observation folder in its new `engine_free` entry), `python3 tools/check_contract_surface_parity.py` (the frozen
W0 snapshot still has exactly the 5 pre-existing additions list — GC-016 added **no** `GameCore.Contracts` type,
member or enum value), `python3 tools/validate_game_core_docs.py --self-test` (9 fixtures) and
`python3 tools/validate_game_core_docs.py` (14 documents), plus a repository-wide `.meta` GUID uniqueness scan
(582 metas, zero duplicate GUIDs) and a "every `.cs` under `Packages/` and `unity/Assets` has a `.meta`" scan.
None of those is a build or a test result.

## 1. The frozen lease interface checkpoint (GC-018) must read the committed boundary through — published first

The brief asks for this early, so it is the first section. **GC-018 (and GC-021/GC-023) may rely on these names and
signatures; after this commit they are additive-only.** All of them live in
`Packages/com.gamecore.unity.runtime/Runtime/Observation/CommittedBoundary.cs`, namespace
`GameCore.Execution.Observation`, assembly `GameCore.Unity.Runtime` (and `GameCore.Execution` under plain dotnet):

```csharp
public interface ICommittedBoundaryReader
{
    bool TryGetLatestBoundary(out SnapshotToken token);
    CommittedBoundaryLeaseResult LeaseCommittedBoundary(CommittedBoundaryRequest request);
}

public enum CommittedBoundaryOutcome { Leased = 0, Expired = 1, Backpressure = 2, ForeignWorld = 3, NoPublication = 4 }
public enum BoundaryQueueDisposition { Unspecified = 0, Included = 1, Rejected = 2 }

public readonly struct CommittedBoundaryRequest
{
    public static CommittedBoundaryRequest Latest(int maxEvents);
    public static CommittedBoundaryRequest At(SnapshotToken token, int maxEvents, uint eventOffset);
    public SnapshotToken Token { get; } public int MaxEvents { get; } public uint EventOffset { get; } public bool IsLatest { get; }
}

public interface ICommittedBoundaryFactsSource   // implemented by whoever owns the queue/lane, never guessed here
{
    int QueuedCommandCount { get; } int StagedOperationCount { get; } BoundaryQueueDisposition QueueDisposition { get; }
}

public interface ICommittedBoundaryLease : IDisposable
{
    WorldId World { get; } SnapshotToken Token { get; } AssemblyEpoch Epoch { get; } LogicalStepId Step { get; }
    ContentHash StateHash { get; } ContentHash PayloadHash { get; } FrozenPayload State { get; }
    int EventCount { get; } CommittedEvent EventAt(int index);
    EventCursor FirstEventCursor { get; } EventCursor LastEventCursor { get; }
    int EventGapCount { get; } bool HasMoreEvents { get; }
    BoundaryQueueDisposition QueueDisposition { get; } int QueuedCommandCount { get; } int StagedOperationCount { get; }
    bool IsDisposed { get; }
}
```

How GC-018 consumes it, and what the protocol guarantees:

1. `UnityWorldHost.Observation` (the new host member) is the `ICommittedBoundaryReader` of the world.
2. Lease at a **boundary**: call `LeaseCommittedBoundary` when the host is not inside a step (the same boundary the
   Wave 5 gate already uses for checkpoint capture). `CommittedBoundaryRequest.Latest(maxEvents)` leases exactly the
   image the published pointer names; `At(token, …)` leases one exact retained image and reports
   `CommittedBoundaryOutcome.Expired` (`DiagnosticCode.CursorExpired`) when it has left retention.
3. The lease **holds a real snapshot lease**, so the image it reads cannot be evicted or overwritten while the
   checkpoint copies it (P-007), and `State`/`EventAt` expose only frozen copies — no `Entity`, `EntityManager`,
   native container or any writable world reference is reachable through the interface (P-045).
4. `EventCount`/`EventAt`/`FirstEventCursor`/`LastEventCursor`/`EventGapCount`/`HasMoreEvents` are the committed events
   at or below the boundary's step, in canonical order, paged by `MaxEvents`/`EventOffset`; a retention gap is an
   explicit count, never a silent skip. `EventOffset > 0` with `HasMoreEvents == true` is the paging protocol.
5. `QueuedCommandCount` / `StagedOperationCount` / `QueueDisposition` are the P-053 "explicit command disposition":
   GC-018 attaches its own `ICommittedBoundaryFactsSource` with `WorldObservation.AttachBoundaryFacts(...)` and reads
   the facts captured at lease time. **A world that attaches no source reports `Unspecified`, not an empty queue** —
   so a checkpoint can never record "no queued commands" by accident.
6. Disposal releases exactly the one snapshot lease the boundary held; disposing twice releases once.

Assumption to check on the build host: the lease must be taken at a non-pumping boundary (the fact values are
captured at lease time, as the boundary's own state). GC-018 should assert `!host.IsPumping` before leasing, or lease
from the end-step boundary the W5 gate already has.

## 2. Commits on this branch

| Commit | Contents |
|---|---|
| `shared:` compile the observation module under plain dotnet and check it engine-free | `dotnet/src/GameCore.Execution/GameCore.Execution.csproj` (globs `Runtime/Observation`), `tools/check_game_core_csharp.py` (`engine_free` entry). |
| `GC-016:` extend step publication with pinned retention, concurrent leases and the committed boundary | `Runtime/Pure/Execution/PublicationBoundary.cs`, `Runtime/Pure/Messages/CommittedEventStore.cs`, `Runtime/WorldHost.cs`, the whole new `Runtime/Observation/` folder, and `Runtime/Integration/DerivationProvenancePublisher.cs`. |
| `GC-016:` add the bounded diagnostics module (payloads, provenance, staged status) | The whole new `Runtime/Diagnostics/` folder. |
| `GC-016:` prove retention, leasing, resynchronization, dedup and provenance without Unity | `Packages/com.gamecore.unity.runtime/Tests/Observation/`, `Packages/com.gamecore.composition/Tests/Diagnostics/`, and the glob in `dotnet/tests/GameCore.Execution.Tests/GameCore.Execution.Tests.csproj`. |
| `GC-016:` restore the delivery window's resync and remembered-identity counters | `DelayedConsumerDelivery.cs` (two property declarations an earlier edit in this branch had accidentally dropped; found by the Unity-test author). **This commit also carried the then-uncommitted `unity/GameCore.Validation/Assets/GameCore.Validation/Tests/Observation/` folder** because it was still untracked; the folder's content is described in §4 and was corrected by the next commit. |
| `GC-016:` correct four real-API mistakes in the Unity-world observation tests | The four new test files under `Tests/Observation/`, after four independent inspections against the real definitions. |

## 3. Summary

GC-016 makes published state, operation outcomes and composition decisions inspectable under bounded retention,
without forking any store and without exposing writable world memory:

* **Retention and leases (P-007, P-045).** `StepPublicationStore` now evicts only images that are neither pinned by a
  live lease nor the newest published image, so a leased image is never dropped or overwritten; the retained window
  may exceed the nominal retention while readers hold leases, bounded by `Retention + MaxConcurrentLeases`, and a
  trim that cannot reach its window reports `PinnedRetentionStallCount` instead of dropping leased memory. Expiry,
  backpressure, foreign worlds, evictions and lease releases are all explicit counters, and `Retained` is a bounded
  copy so a reader may enumerate it while the publisher appends.
* **Concurrent readers.** Reads (`Acquire`, `TryGetImage`, `HasPublished`, `Retained`, `IsPinned`, lease disposal)
  are synchronized against the publisher; publication stays serialized because it *is* the commit boundary.
  `PublishedStepImage.PayloadHash` is the hash of exactly the bytes a reader holds, and
  `StepPublicationStore.Verify(lease, out hash)` proves a lease is its own token's complete image.
* **Committed boundary (P-053, frozen seam).** `WorldObservation` is the one read surface over the image store and
  the committed-event store, and it leases the boundary a checkpoint captures (§1).
* **Cursor expiry and resynchronization (P-045, O-17).** `SnapshotResynchronization` names the newest retained image,
  the event cursor at that boundary and the count of events retention dropped; `WorldObservation.Resynchronize()` is
  the one call a lagging reader makes, and a world that has published nothing says so instead of returning a stale
  token.
* **Delayed-consumer dedup (P-045, TEST-014).** `DelayedConsumerDelivery` owns a **bounded per-consumer** identity
  window keyed by `(WorldId, event sequence)`: a retry of an unacknowledged page is `Duplicate` and hands out
  nothing; an expired cursor is `ResyncRequired`; the store's own redelivery ledger is now bounded by retention as
  well (it releases an identity when the event is dropped), so neither structure grows without limit (TEST-023).
* **Structured diagnostics (P-052).** `DiagnosticEnvelope` + `DiagnosticRegistry` + `CompositionDiagnosticFeed`
  intern payloads under retrieval keys; `DiagnosticPrecedence` is the only ordering and reads typed fields only —
  the canonical hash *excludes the summary*, so rewording, reformatting or re-culturing a diagnostic cannot change
  its identity or its position.
* **Reconstructable compact provenance (P-026, O-25).** `ProvenanceStore` retains one compact record per candidate
  decision plus a per-epoch interned evidence table, bounded by epoch/record/entry/staged retention, refusing
  (`BudgetExceeded`, `MissingDependency`) rather than truncating or dangling; `ProvenanceReconstruction` gives winners,
  losers, exclusions, boundaries, mode-gate denials, missing inputs, predicate rejections, selector mismatches and
  state dispositions, with a canonical digest that is independent of the producer's enumeration order.
  `ProvenanceExplanationReader` implements the frozen `IExplanationReader` **and** `IStagedPlanDiagnostics` (until now
  only the reference-seam stub did), labelling published versus staged distinctly.
* **Staged-operation status (O-25).** `StagedStatusReader` reads a real lane and reports
  `StagedStatusSource.PublishedComposition | StagedPlan | None` as a field, so a staged plan can never be mistaken
  for world observation.
* **Real derivations become provenance.** `DerivationProvenancePublisher` is the one projection from a real
  `DerivationResult` (plus the real plan's `StateDisposition`s) into the store, including a support record for every
  winner the engine did not report as a decision, so an effective capability always explains at least one support.

## 4. Files created

### `Packages/com.gamecore.unity.runtime/Runtime/Observation/` (new folder, all with `.meta`)

| File | Contents |
|---|---|
| `ObservationRetention.cs` | The three bounds (images, events, leases), validation, `Default` (32/256/16), `MaxRetainedImages`. |
| `CommittedBoundary.cs` | The frozen checkpoint seam of §1: request, facts source, outcome/refusal values, lease interface and the granted `CommittedBoundaryLease` with `Verify()`. |
| `SnapshotResynchronization.cs` | The one value a lagging reader restarts from: token, cursor, dropped-event count, retained-image count, or an explicit unavailable result. |
| `WorldObservation.cs` | The single read surface (`IObservationReader`, `ICommittedEventReader`, `ICommittedBoundaryReader`): acquisition, commit-event paging, boundary leasing with bounded event windows, resynchronization, counters. |
| `DelayedConsumerDelivery.cs` | Per-consumer at-least-once delivery with a bounded `(world, sequence)` identity window, `Poll`/`PollFrom`/`Resynchronize`. |

### `Packages/com.gamecore.unity.runtime/Runtime/Integration/`

| File | Contents |
|---|---|
| `DerivationProvenancePublisher.cs` | Real `DerivationResult` → `ProvenanceStore`: decision records, interned evidence (descriptor, exclusion, boundary, missing input, scope path, mode gate, support), synthetic support records, `ProvenancePublicationReport`. |

### `Packages/com.gamecore.composition/Runtime/Diagnostics/` (new folder, all with `.meta`)

| File | Contents |
|---|---|
| `DiagnosticEnvelope.cs` | `DiagnosticKey`, `DiagnosticEnvelope` (typed payload, `CanonicalHash` excluding the summary, `Format`), `DiagnosticPrecedence` (typed total order), `DiagnosticDocument` (canonical encoding). |
| `DiagnosticRegistry.cs` | Interned bounded payload registry, `PublicationDiagnostic`, `RejectionDiagnostic` and `CompositionDiagnosticFeed : ICompositionObserver`. |
| `ProvenanceRecords.cs` | `ProvenanceKind`, `ProvenanceEvidenceKind`, `CapabilityProvenance`, `ProvenanceEntry`, `ProvenanceReconstruction`, `ProvenanceDigest`. |
| `ProvenanceStore.cs` | Epoch/staged retention, canonical record ordering, interning, fail-closed bounds, reconstruction and digests. |
| `ProvenanceExplanationReader.cs` | The production `IExplanationReader` + `IStagedPlanDiagnostics` over retained provenance. |
| `StagedOperationStatus.cs` | `StagedStatusSource`, `StagedOperationStatus`, `StagedStatusReader` over a real `CompositionHost`. |

### Tests (all with `.meta`)

| File | Contents |
|---|---|
| `Packages/com.gamecore.unity.runtime/Tests/Observation/ObservationFixture.cs` | Literal ids, commit/event builders, boundary-facts double. |
| `.../SnapshotRetentionTests.cs` | One image per step, pinned-image survival, trim stalls, backpressure, foreign-world refusals, payload verification, `Retained` copy, settings validation. |
| `.../WorldObservationTests.cs` | Resynchronization and gaps, the boundary lease (image + events + facts + paging), every refusal value, a world with no plane, event retention/redelivery, request validation. |
| `.../DelayedConsumerDeliveryTests.cs` | Once-only delivery, retry suppression, resync-then-continue, bounded identity window, incarnation check, no-plane idling. |
| `.../ConcurrentObservationTests.cs` | Three reader threads against a real publisher verifying their own token's image; a pinned image staying byte-identical while 39 more images publish. |
| `.../GameCore.Unity.Observation.Tests.asmdef` | Editor-only test assembly referencing `GameCore.Contracts` and `GameCore.Unity.Runtime`. |
| `Packages/com.gamecore.composition/Tests/Diagnostics/DiagnosticPrecedenceTests.cs` | Wording/culture/permutation independence, typed total order, registry interning/bounds/order, feed records, feed retention. |
| `.../ProvenanceStoreTests.cs` | Reconstruction of winners/losers/exclusions, digest stability across enumeration order and page windows, retention expiry, in-place replacement, foreign world, fail-closed bounds, staged labelling/drop. |
| `.../ProvenanceExplanationReaderTests.cs` | The frozen page shape and totals, paging, expired/unknown/invalid counting, staged labelling, canonical record order. |
| `.../StagedOperationStatusTests.cs` | Real lane: staged → published, refusal, unknown versus expired, handle-versus-identity reads. |

### Unity-world tests (real worlds, both families — `unity/GameCore.Validation/Assets/GameCore.Validation/Tests/Observation/`)

| File | Contents |
|---|---|
| `GameCore.Observation.Tests.asmdef` | Editor-only test assembly referencing the two family fixture assemblies, the rules assemblies, `GameCore.Composition`, `GameCore.Derivation(.Fixtures)`, `GameCore.Planning`, `GameCore.Unity.Fixtures`, `GameCore.Unity.Runtime`, `Unity.Entities`/`Unity.Collections` and the Unity test runners. |
| `ObservationFamilyHarness.cs` | Builds one **real** narrative world and one **real** card world exactly as the family fixtures do (catalog → schedule/ownership pipeline → `NarrativeRegistration`/`CardTableRegistration` → `UnityWorldRegistry.TryCreate` → family module → `TargetRegistry`/`AssemblyPublisher`/`LiveTargetIndex`/`LiveTargetSeeder` → `CompositionHost` + `WorldCompositionBridge` → `DerivedAssemblyPipeline` → `WorldTimeDriver`), seeds the real targets/market and mounts the real providers; every refusal throws with the module's own code and detail. Exposes `Host`, `Lane`, `Publisher`, `Pipeline`, `Time`, `RootScope`, `LatestBoundary()`, `LeaseLatestBoundary(int)`, `CommitOneStep()`, `SubmitFamilyCommand()`, `PublishSetupEdit(...)`, `PublishStagedAndDerive(...)`, `CommittedEventOf(...)`, `StepCommitOf(...)`. |
| `ObservationImmutabilityTests.cs` | 4 tests × 2 families: an immutable verifiable boundary lease (token/step/epoch versus the world's own counters, payload hash recomputed from the leased copy, mutated copy never reaching the store's image); no writable state escapes and the lease survives a world advance; a pinned image is never overwritten or evicted while leased; a saturated lease pool reports backpressure and keeps the leased image. |
| `ObservationCursorAndProvenanceTests.cs` | 3 tests × 2 families: cursor expiry plus resynchronization from the newest boundary (including a foreign-world cursor); delayed-consumer delivery of each committed event once, recovery after expiry and duplicate suppression; `DerivationProvenancePublisher` over the **real** derivation of the mounted provider, reconstructing every effective capability of every target with winner/loser/exclusion evidence, paging and the page-independent digest, and the `ExplanationPage` agreeing with the reconstruction. |
| `ObservationStagedStatusAndDiagnosticsTests.cs` | 2 tests × 2 families: `StagedStatusReader` distinguishing a staged plan from published state and from an expired result; the structured diagnostics of a **really refused** edit (the lane's own retained plan hash and diagnostics) interned, retrieved and ordered independently of the formatted text. |

Every test carries `[Timeout(600000)]` and `[TearDown] UnityWorldRegistry.ResetAll()`; both families are supplied
through `[TestCase(ObservationFamilyHarness.NarrativeFamily)]` / `[TestCase(ObservationFamilyHarness.CardsFamily)]`.

### `artifacts/gc-016/`

| File | Contents |
|---|---|
| `HANDOFF.md` | This document. |
| `static-checks.log` | The verbatim output of every check that ran on this host. |

## 5. Files modified (shared surface, additive)

| File | Change | Why it is safe |
|---|---|---|
| `Packages/com.gamecore.unity.runtime/Runtime/Pure/Execution/PublicationBoundary.cs` | `PublishedStepImage` gains `PayloadHash` + `IsImageOf`; `StepPublicationStore` gains a lock, pinned-aware eviction that never drops the newest image, `RetainedCount`/`EvictedCount`/`PinnedRetentionStallCount`/`BackpressureCount`/`ExpiryCount`/`ForeignWorldCount`/`ReleasedLeaseCount`/`PinnedImageCount`, `IsPinned`, `TrimRetention`, `TryResync`, `Verify`, and `Retained` now returns a bounded copy. | Every existing member keeps its name, signature and observable behaviour (the W1/W2 assertions on `PublishedCount`, `RefusedPublicationCount`, `Last`, `HasPublished`, `Acquire` semantics and `Retained` ordering still hold; the retained window only ever *grows* beyond `Retention` while a lease pins an image). |
| `Packages/com.gamecore.unity.runtime/Runtime/Pure/Messages/CommittedEventStore.cs` | The redelivery identity ledger is bounded by retention (an identity is released with its dropped event) and `Publish`/`Read` keep their exact counters. | `RedeliveryCount`, `DroppedCount`, `FirstRetainedSequence` and page outcomes are unchanged for every existing case; only the memory of an evicted identity disappears, and an evicted event can never be re-read anyway. |
| `Packages/com.gamecore.unity.runtime/Runtime/WorldHost.cs` | One new field and one new public property: `public WorldObservation Observation { get; }`, constructed from the world's own `publications` and `messages.EventStore`. | Additive; `IWorldHost` is untouched, so no other implementer or caller changes. |
| `dotnet/src/GameCore.Execution/GameCore.Execution.csproj` | `Runtime/Observation/**` joins the `Runtime/Pure/**` glob. | The folder is engine-free by construction and is now in the checker's `engine_free` set. |
| `dotnet/tests/GameCore.Execution.Tests/GameCore.Execution.Tests.csproj` | Globs `Packages/com.gamecore.unity.runtime/Tests/Observation/**` so the same test sources run under dotnet and as Unity EditMode tests. | Default compile items stay enabled; the glob adds files only. |
| `tools/check_game_core_csharp.py` | `Packages/com.gamecore.unity.runtime/Runtime/Observation` joins `engine_free`. | Additive entry; it makes a Unity type in GC-016's observation storage a host-side failure. |

## 6. Contract changes

**No `GameCore.Contracts` change.** GC-016 added no type, member, enum value or optional constructor parameter to the
frozen shared contracts: `tools/check_contract_surface_parity.py` still reports exactly the five pre-existing
additions (`EnvelopeError` 16/17, `FactoryKind.Handler`, `StateDispositionKind.RetainDormant`/`Reset`), and
`dotnet/tests/GameCore.Contracts.Tests`' API-compatibility allow-list therefore needs no edit. Concretely,
`ICommittedBoundaryReader`, `ICommittedBoundaryLease`, `CommittedBoundaryRequest`, `DiagnosticEnvelope` and the
provenance types live in the Unity-runtime and composition assemblies, and the checkpoint seam is reachable by GC-018
because its persistence module lives in the same assembly.

Additions outside `GameCore.Contracts` (all additive, no existing signature changed):

* `GameCore.Execution.Observation.*` — the observation module of §1 and §4.
* `UnityWorldHost.Observation` — one new property.
* `StepPublicationStore` / `PublishedStepImage` / `CommittedEventStore` members listed in §5.
* `GameCore.Composition.Diagnostics.*` — the diagnostics module and the production implementations of the frozen
  `IExplanationReader` / `IStagedPlanDiagnostics` interfaces.
* `GameCore.Unity.Runtime.Integration.DerivationProvenancePublisher` / `ProvenancePublicationReport`.

## 7. Requirement → implementation → test mapping

| Requirement | Implementation | Tests |
|---|---|---|
| P-007 references and leases (bounded retention rejects rather than overwriting leased memory) | `StepPublicationStore` pinned eviction + lease pool; `CommittedBoundaryLease`; `WorldObservation` counters | `SnapshotRetentionTests` (all six), `ConcurrentObservationTests` (both), `WorldObservationTests.BoundaryRefusalsAreValuesWithStableCodes` |
| P-026 explanation (complete retained provenance, interned but reconstructable) | `ProvenanceStore`, `ProvenanceReconstruction`, `ProvenanceDigest`, `ProvenanceExplanationReader`, `DerivationProvenancePublisher` | `ProvenanceStoreTests` (all nine), `ProvenanceExplanationReaderTests` (all four) |
| P-044 commit/publication exposes events and image together | (existing) `TryCommitStep`/`Publish`; GC-016 adds image integrity and the boundary lease | `SnapshotRetentionTests.PublishingExposesExactlyOneCompleteImagePerStep`, `VerifiedPayloadsAreCompleteAndBelongToTheirOwnToken`, `WorldObservationTests.ABoundaryLeaseCarriesTheImageItsEventsAndTheQueueFactsTogether` |
| P-045 observation, cursors, dedup, resynchronization | `WorldObservation` (resync, boundary lease), `DelayedConsumerDelivery`, bounded redelivery ledger | `WorldObservationTests` (all seven), `DelayedConsumerDeliveryTests` (all five) |
| P-050 cancellation and idempotency (retention/expiry of results) | `StagedStatusReader` reports `ResultExpired` distinctly from `Unknown` | `StagedOperationStatusTests.AnUnknownOperationIsDistinctFromAnExpiredOne` |
| P-051 operation discipline (pure queries, staged status visible) | `StagedStatusReader`, `StagedOperationStatus`, `ProvenanceExplanationReader` | `StagedOperationStatusTests` (all four) |
| P-052 diagnostics (payload identity, no formatting-driven precedence) | `DiagnosticEnvelope`, `DiagnosticPrecedence`, `DiagnosticRegistry`, `CompositionDiagnosticFeed` | `DiagnosticPrecedenceTests` (all six) |
| TEST-002 (identity/epoch/stale references) | `SnapshotToken`-bound leases, `ForeignWorld` refusals, `Verify` against the token's own fingerprint | `SnapshotRetentionTests.ForeignWorldTokensAreRefusedAsValuesAndAtPublication`, `VerifiedPayloadsAreCompleteAndBelongToTheirOwnToken`, `ConcurrentObservationTests.ConcurrentReadersAlwaysLeaseACompleteImageOfTheirOwnToken` |
| TEST-008 (incremental indexes and provenance parity) | Provenance digest/canonical ordering, enumeration-order independence | `ProvenanceStoreTests.EnumerationOrderCannotChangeTheRetainedFormOrItsDigest`, `TwoWinnersChangeTheDigestAndTheRecordSet` |
| TEST-009 (prepared plans and visibility on both sides of a boundary) | Staged-versus-published status; staged provenance labelled | `StagedOperationStatusTests`, `ProvenanceExplanationReaderTests.StagedPagesAreLabeledAndMatchTheirReconstruction` |
| TEST-014 (committed events, consistent observation, delayed consumer, read-only inspection) | `WorldObservation`, `DelayedConsumerDelivery`, frozen `FrozenPayload` surfaces only | `WorldObservationTests`, `DelayedConsumerDeliveryTests`, `SnapshotRetentionTests.ALeasedImageIsPinnedAndNeverEvictedOrOverwritten` |
| TEST-016 (fault boundaries keep the last good image) | A pinned/committed image is never overwritten and keeps verifying after later publications | `SnapshotRetentionTests.ALeasedImageIsPinnedAndNeverEvictedOrOverwritten`, `ConcurrentObservationTests.APinnedImageStaysReadableWhileTheWindowMovesUnderneathIt` |
| TEST-023 (bounded memory, counters reported separately) | Bounded identity windows, retention bounds, per-structure counters | `DelayedConsumerDeliveryTests.TheRememberedIdentityWindowIsBounded`, `SnapshotRetentionTests.LeasePoolBackpressureIsAValueThatNeverOverwritesLeasedMemory`, `ProvenanceStoreTests.DeclaredBoundsRefuseInsteadOfTruncating` |

Unity-world proof of published images, cursor expiry/resynchronization, delayed-consumer dedup, reconstructable
provenance and staged-versus-published status in both families is
`GameCore.Observation.Tests` (`unity/GameCore.Validation/Assets/GameCore.Validation/Tests/Observation/`, §4): every
one of its 9 family-parameterized tests builds a real narrative world or a real card world and asserts on real
committed images.

## 8. Build and test commands for the Linux build host

All of these are **`NotRun (pending orchestrator build host)`**.

### 8.1 Plain dotnet

```sh
dotnet build dotnet/GameCore.sln -c Release
dotnet test dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gc-016/trx
# focused:
dotnet test dotnet/tests/GameCore.Execution.Tests/GameCore.Execution.Tests.csproj -c Release \
  --filter "FullyQualifiedName~GameCore.Execution.Tests.Observation"
dotnet test dotnet/tests/GameCore.Composition.Tests/GameCore.Composition.Tests.csproj -c Release \
  --filter "FullyQualifiedName~GameCore.Composition.Tests.Diagnostics"
```

The pure suites in this change set are `GameCore.Execution.Tests.Observation.*` (5 fixtures:
`SnapshotRetentionTests`, `WorldObservationTests`, `DelayedConsumerDeliveryTests`, `ConcurrentObservationTests`) and
`GameCore.Composition.Tests.Diagnostics.*` (4 fixtures: `DiagnosticPrecedenceTests`, `ProvenanceStoreTests`,
`ProvenanceExplanationReaderTests`, `StagedOperationStatusTests`).

### 8.2 Unity EditMode (test runs must NOT pass `-quit`; every invocation wrapped in `timeout`)

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
timeout --signal=TERM --kill-after=60 1800 "$UNITY" -batchmode -nographics \
  -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode \
  -testFilter "GameCore.Unity.Observation.Tests;GameCore.Composition.Tests;GameCore.Observation.Tests" \
  -testResults artifacts/gc-016/unity/observation-editmode.xml \
  -logFile artifacts/gc-016/unity/observation-editmode.log
```

The three assemblies are: `GameCore.Unity.Observation.Tests` (pure observation storage, package Tests folder),
`GameCore.Composition.Tests` (its new `Diagnostics` folder runs inside the existing assembly), and
`GameCore.Observation.Tests` (the Unity-world proof for both families).

### 8.3 Full-solution Unity EditMode and PlayMode, then the probes

```sh
timeout --signal=TERM --kill-after=60 1800 "$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode -testResults artifacts/gc-016/unity/editmode-results.xml \
  -logFile artifacts/gc-016/unity/editmode.log
timeout --signal=TERM --kill-after=60 1800 "$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform PlayMode -testResults artifacts/gc-016/unity/playmode-results.xml \
  -logFile artifacts/gc-016/unity/playmode.log
UNITY="$UNITY" ARTIFACTS=artifacts/gc-016/toolchain tools/unity/build_probe.sh
PROBE_RUNS=5 ARTIFACTS=artifacts/gc-016/toolchain tools/unity/probe_runs.sh
```

`tools/unity/probe_runs.sh` is the probe driver named in the brief: run 1 writes the canonical result, runs 2–5 write
`.run2`…`.run5`, and any signal death (exit ≥ 128) or dirty step fails the run. The Wave 5 exit gate (the W5
integration gate, not this task) is what must show prewrite rejection, postwrite fail-stop, new-session restore,
read-only snapshots and stale asset callback rejection in one actual world; GC-016 contributes the read-only
snapshot and boundary halves.

### 8.4 Documentation and host checks the orchestrator should repeat

```sh
python3 tools/check_game_core_csharp.py
python3 tools/check_contract_surface_parity.py
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
```

## 9. Known gaps, assumptions and doc ambiguities

1. **Nothing here has been compiled or run.** Every command in §8 is `NotRun`. The authoring host has no toolchain;
   the risk this carries is compile-level (names, usings, nullability), which the source has been written to minimise
   and the host-side checker partially covers. The Unity-world test assembly is the part of the change set with the
   least compiler-adjacent review for exactly this reason.
2. **How the Unity-world test assembly was reviewed instead of compiled.** Four independent inspections were run,
   one per file, each resolving every member, constructor and enum reference against the real definition (a regex
   sweep of every `.Member` access plus every capitalized identifier, checked against the declaring files). They
   found and fixed seven real defects, all committed in "correct four real-API mistakes in the Unity-world
   observation tests": `CommandAdmissionReceipt.Result.Reason` (not `Code`); an immutability check that compared a
   byte with itself instead of proving a write to the caller's copy never reaches the store's image; a wrong counter
   for a foreign-world cursor on a world that *has* an event plane; an explanation page total compared against the
   page window instead of the whole retained pair; a nullable `EditAdmission.Plan` dereference; an assertion that a
   staged plan advances `StagedState.Revision` (staging increments no revision at all, so the staged tail is now
   asserted apart by definition fingerprint); and a refused admission's plan hash read from the admission (which is
   deliberately null for a refusal) instead of from the lane's own retained ledger row. The same review round also
   caught two declarations an earlier edit of mine had dropped from `DelayedConsumerDelivery`
   (`ResyncCount`, `RememberedIdentities`), fixed in "restore the delivery window's resync and remembered-identity
   counters". Residual risk: construction order and argument arity of the family fixtures are still unverified by a
   compiler, so a first-build failure there (if any) is mechanical.
3. **Doc ambiguity — retention versus a pinned image.** P-007 says retention "rejects new leases with
   `SnapshotBackpressure` rather than overwriting leased memory" and says nothing about what the *publication* side
   does when the window is full of pins. Reading chosen: publication always succeeds (a refused publication would
   break the nonthrowing commit of P-044) and eviction skips pinned images **and the newest image**, so the window
   temporarily exceeds its nominal size, bounded by `Retention + MaxConcurrentLeases`, and the stall is counted. A
   stricter reading (refuse the publication) was rejected because it would let an observer's lease block a commit.
4. **Doc ambiguity — the committed boundary's facts.** P-053 requires an "explicit command disposition" at capture
   but does not say whose count it is. Reading chosen: the observation module never invents it; the owner attaches an
   `ICommittedBoundaryFactsSource` and an unattached world reports `Unspecified` with zero counts (never "empty").
5. **Doc ambiguity — provenance retention scope.** P-026 says records "remain reconstructable for the retained epoch"
   without bounding entries. Reading chosen: the evidence table belongs to its epoch, so evicting an epoch releases
   exactly the memory it introduced; a publish that would exceed a declared bound is refused
   (`BudgetExceeded`/`MissingDependency`) rather than truncated, and interning is per epoch (repeat evidence inside
   one epoch collapses; cross-epoch sharing is deliberately not attempted).
6. **`Explain` on an expired epoch returns an empty page.** The frozen `IExplanationReader.Explain` cannot return an
   error, so expiry is reported through `ProvenanceExplanationReader.ExpiredPageCount` / `LastCode` and through the
   explicit `TryReconstruct(...)`, which returns `DiagnosticCode.CursorExpired`. A caller that must distinguish
   "expired" from "no records" uses `TryReconstruct`.
7. **State dispositions are attached by the caller.** The store can carry them (and the page reports them), but only
   the caller knows the plan that produced them; `DerivationProvenancePublisher.Publish(..., dispositions)` takes
   them from the real `DerivedAssemblyReport.Plan.Dispositions`. A publish without them reports none — it never
   invents one.
8. **`CommittedEventStore`'s eviction now also releases the event's redelivery identity.** This bounds memory
   (TEST-023) without changing any observable counter for retained events; the semantics of "an identity outside
   retention" were already "unanswerable".
9. **No IL2CPP-specific work.** GC-016 adds no new generic instantiation, reflection or AOT root; the module is pure
   C# in assemblies the W4 profile already builds, so the W4 generic profile inventory should not change. Not
   claimed as proven until the orchestrator builds it.
10. **`ObservationHub` (lifecycle/step notification) is untouched.** GC-016's storage is the read side; the
    notification fan-out remains GC-005's, and no W5 task owns a second one.
11. **The `shared:` commits.** Two commits touch files shared with other tasks: the build/tooling one listed in §2
    (`dotnet/src/GameCore.Execution/GameCore.Execution.csproj`, `tools/check_game_core_csharp.py`) and the test-source
    glob inside the pure-tests commit (`dotnet/tests/GameCore.Execution.Tests/GameCore.Execution.Tests.csproj`). Both
    are additive and are listed in §5.
12. **Where the Unity-world tests appear in the suites.** `GameCore.Observation.Tests` is an `Assets` test assembly,
    so Unity discovers it through its `.asmdef` and it is not listed in `Packages/manifest.json` `testables` (that
    list is for package-hosted tests only). The other two new folds are package-hosted:
    `Packages/com.gamecore.unity.runtime/Tests/Observation/` (assembly `GameCore.Unity.Observation.Tests`) and
    `Packages/com.gamecore.composition/Tests/Diagnostics/` (inside the existing `GameCore.Composition.Tests`).
13. **Warning-as-error exposure.** `dotnet/Directory.Build.props` sets `TreatWarningsAsErrors`, and the composition
    and execution test projects glob package `Tests` folders, so the new pure test sources are compiled under that
    setting. The host-side checker cannot see warnings; a warning-level issue in those files would surface only on the
    Linux host.

## 10. Proposals for `artifacts/gates/w4-generic-profile/inventory.json` (proposals only)

Per the brief these are **proposals**, not edits: the profile inventory was not modified. The build host promotes
them after running.

| Row | Proposed status | Reason (evidence this change set adds) |
|---|---|---|
| P-007 | Partial → **Partial** (unchanged), new evidence | Retention backpressure, pinned-image survival (a leased image is never overwritten) and lease accounting are now executed by named tests; "bindings resolve once per assembly rather than per target per frame under load" (TEST-023) remains unproven, so the row does not reach `Implemented+Evidenced`. |
| P-026 | Partial → **Partial** (unchanged), gap reduced | The complete retained record (winners, losers, exclusions, boundaries, mode gate, stratum, state dispositions, recipe hash) is now reconstructable through `ProvenanceReconstruction` with a production `IExplanationReader`/`IStagedPlanDiagnostics`, and `CompositionPublished`/`CompositionRejected` payloads are recorded with retrieval keys by `CompositionDiagnosticFeed` — the exact gap the row names. Cross-family `Explain` in a real world waits on the Unity test assembly running. |
| P-045 | Partial → **Partial** (unchanged), new evidence | Immutable images at publication, explicit cursor expiry, resynchronization, at-least-once delivery with `(world, sequence)` dedup and bounded subscriber memory are now named tests; the crash-durable outbox for irreversible output adapters (GC-021) is still unrun. |
| P-052 | Partial → **Partial** (unchanged), gap reduced | Structured payloads (code, world/operation/plan identity, phase, involved ids/keys, counts/budgets, retry class) plus retrieval keys and a formatting-independent total order are implemented and tested; "fallback" remains the unified cross-module diagnostic assembly that GC-023/GC-026 own. |
| P-050 | Partial (unchanged) | Staged-versus-expired result reporting is now tested; the restore session reservation and bounded retries the row names remain GC-018/GC-014 work. |
| TEST-002/008/009/014/016/023 (suite rows, if the inventory tracks them) | new authored evidence | Ten pure fixtures (five observation, four diagnostics, five test methods inside them where they share a fixture) plus `GameCore.Observation.Tests`' 9 family-parameterized Unity-world tests are the applicable subsets this task owns; none of them has been executed on this host, so §8.2 is where they run. |

No `Implemented+Evidenced` promotion is proposed for any row: every claim above depends on the Linux build host
actually compiling and running the suites, and this host ran none of them.

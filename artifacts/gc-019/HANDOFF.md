# GC-019 handoff — common input, asset and presentation adapters (Wave 5)

**Every executable check this change set adds or refers to is `NotRun (pending orchestrator build host)`.** This
authoring host has no Unity, no .NET SDK and no C# compiler, so nothing here has been compiled, imported or executed.
What did run on this host is listed in §9 and is not a build, an import or a test.

## 1. Summary

GC-019 connects host resources and views to the frozen Wave 0–4 kernel **without creating a second authority and
without adding an update path**:

* **One update path, two adapter points.** `04` s3's pump algorithm already names "collect adapter input and
  completed host callbacks" before the logical-step loop and "update presentation from the last published snapshot"
  after it. `AdapterFrameRegistry` is that pair of calls, and `GameCoreApplicationPump` now makes exactly those two
  calls per host frame. No PlayerLoop node, no `MonoBehaviour.Update`, no `World.Update()` was added.
* **Stamped typed input into the existing command port.** `TypedInputIngress` stamps a sample with world, source and
  a strictly increasing source sequence, validates it, and submits a real `CommandEnvelope` to the world's own
  `ICommandIngress` (O-13). The adapter performs none of the world's checks itself: route, capacity, lifecycle and
  domain validation stay with the kernel, and admission is not gameplay success (P-042).
* **Bounded asynchronous asset leases.** `AssetLeaseTable` registers every lease in the world's own
  `WorldResourceLedger`, so the existing P-048 teardown path fences and retires adapter leases like any other managed
  lease. A completion is validated **at completion** against the real `CallbackGate`; after retirement nothing is
  installed and only the completion's own acquisition is released. Count and byte budgets both refuse as values.
* **Stable target-to-view maps and committed-output presentation.** A view is addressed by `ViewKey = (TargetId,
  Slot)`; presentation reads an immutable `CommittedAssemblyImage` DTO projected from the published assembly
  (`LiveAssemblyImageBuilder`). Stale applies are refused, orphaned views are destroyed when their target leaves the
  committed assembly, and **composition parent (a `ScopeId` read from the snapshot) is reported next to Transform
  parent rather than derived from it** — so reparenting a Transform cannot move composition (P-010).
* **An external physical-authority descriptor and seam with no physics requirement.** `ExternalAuthorityDescriptor`
  declares the single owner of one physical domain; a recipe nobody declares is explicitly ECS-owned kinematic, so a
  card or narrative world needs no adapter, no rigidbody and no simulation stage. Gameplay writes to an externally
  owned domain are refused as an `OwnershipConflict`, and a foreign or stale observation is refused, so "one declared
  authority per quantity" is a value rather than a convention (P-034, 04 s7, TEST-019).
* **View destruction cannot touch gameplay**, because `ViewRegistry` performs no ECS write of any kind: its only side
  effects are on the `IViewBinder`. That is why the acceptance clause is a property of the type, not a promise about
  its callers.

## 2. Files created

### Pure adapter core (Unity-free; compiled by the Unity assembly `GameCore.Unity.Adapters` **and** by `dotnet/src/GameCore.Adapters`)

| Path | Contents |
| --- | --- |
| `Packages/com.gamecore.unity.adapters/Runtime/Pure/AdapterFrames.cs` | `AdapterFrameOutcome`, `AdapterFrameReport`, `IAdapterFrame` and `AdapterFrameRegistry` (register/unregister by world, main-thread input and presentation entry points, skip/fault counters, per-session `Reset`). |
| `.../Runtime/Pure/Input/InputIngress.cs` | `InputSourceStamp`, `SampledInputCommand` (+ `ToEnvelope`, `InputHash`), `InputAdmissionOutcome`/`InputAdmissionResult`, `TypedInputIngress` (stamp/validate/retain/idempotent retry), `PendingInputCompletionTable` with `InputCompletionOutcome`/`InputCompletionResult`. |
| `.../Runtime/Pure/Input/InputBinding.cs` | `InputDeviceKind`, `DeviceInputSample`, `InputCommandBinding`, `InputBindingTable` and `CommandPayloadCodec` (canonical big-endian `int32`, 05 s6). |
| `.../Runtime/Pure/Assets/AssetLeases.cs` | `AssetLeaseState`, `AssetLoadStatus`/`AssetLoadPoll`, `IAssetBackend`, `AssetLoadRequest`, `AssetCompletionOutcome`/`AssetCompletionResult`, `AssetReleaseOutcome`, `AssetLease`, `AssetReleaseReport` and `AssetLeaseTable`. |
| `.../Runtime/Pure/Views/ViewRegistry.cs` | `ViewKey`, `ViewCreateOutcome`/`ViewDestroyOutcome`, `IViewBinder`, `PresentationApplyData`, `PresentationField`, `ViewRecord` and `ViewRegistry`. |
| `.../Runtime/Pure/Views/Presentation.cs` | `PresentationTarget`, `IPresentationSource`, `PresentationOutcome`/`PresentationReport` and `CommittedOutputPresenter`. |
| `.../Runtime/Pure/Views/CommittedImage.cs` | The engine-observation DTOs: `ITargetScopeIndex`, `TargetScopeTable`, `CommittedTargetEntry`, `CommittedAssemblyImage`, `ICommittedValueReader` and `CommittedImageSource`. |
| `.../Runtime/Pure/Authority/ExternalAuthority.cs` | `MotionAuthority`, `ExternalAuthorityDescriptor`, `EngineObservation`, `AuthorityIntentKind`/`AuthorityIntent`, `AuthorityIntentOutcome`, `IExternalAuthorityAdapter`, `ObservationOutcome` and `ExternalAuthorityLedger`. |

### Unity halves (reference `UnityEngine` / `Unity.Entities`; excluded from the plain-dotnet build on purpose)

| Path | Contents |
| --- | --- |
| `.../Runtime/Input/WorldAdapterFrame.cs` | `WorldAdapterFrame : IAdapterFrame` (device sampling → stamped ingress, asset completion pump, committed-output presentation, `Retire()`), `AdapterTeardownReport` and `IDeviceInputSource`. |
| `.../Runtime/Input/UnityDeviceInputSource.cs` | Classic `Input`-API sampler over a declared, canonically ordered key list (no Input System package pin, per 04 s7). |
| `.../Runtime/Assets/UnityResourcesAssetBackend.cs` | `Resources.LoadAsync`-based `IAssetBackend` plus `ResourcePaths` (a key-to-path table declared by content; a miss is reported, never guessed). No Addressables dependency. |
| `.../Runtime/Views/GameObjectViewBinder.cs` | Real `GameObject`/`Transform` binder: create, destroy, explicit visual parent, presentation-only apply. |
| `.../Runtime/Views/LiveAssemblyImageBuilder.cs` | `LiveTargetScopeIndex` over `LiveTargetIndex` and the builder that projects the published `PublishedWorldView` into a `CommittedAssemblyImage`. |

### Shared doubles and plain-dotnet projects

| Path | Contents |
| --- | --- |
| `Packages/com.gamecore.unity.adapters/Fixtures/Runtime/AdapterFixtures.cs` | `DeterministicAssetBackend`, `RecordingViewBinder`, `HeadlessViewBinder`, `QueuedDeviceInputSource`, `DeviceInputKind`, `RecordingExternalAuthorityAdapter`. |
| `Packages/com.gamecore.unity.adapters/Fixtures/Runtime/GameCore.Unity.Adapters.Fixtures.asmdef` | `GameCore.Unity.Adapters.Fixtures`, `noEngineReferences: true`. |
| `dotnet/src/GameCore.Adapters/GameCore.Adapters.csproj` | netstandard2.1 library over `Runtime/Pure/**`. |
| `dotnet/tests/GameCore.Adapters.Tests/GameCore.Adapters.Tests.csproj` | net8.0 NUnit 3 suite that also compiles `Packages/com.gamecore.unity.adapters/Fixtures/**` so both suites share one set of doubles. |
| `dotnet/tests/GameCore.Adapters.Tests/AdapterContractTests.cs` | The pure adapter suite (see §5). |
| `tools/make_unity_metas.py` | Meta generator: creates only missing `.meta` files with fresh unique GUIDs in this repository's exact byte shape; idempotent, never touches an existing meta and never reuses a GUID. |
| `Packages/com.gamecore.unity.adapters/Tests/Adapters/GameCore.Unity.Adapters.Contract.Tests.asmdef` | EditMode test assembly `GameCore.Unity.Adapters.Contract.Tests` (Editor-only) for the Unity-side adapter entry points. |
| `Packages/com.gamecore.unity.adapters/Tests/Adapters/AdapterContractUnityTests.cs` | EditMode contract tests: the registry's two calls per world, a foreign world's frame never being driven, a faulting frame counted instead of aborting the frame call, the unbound-sample counter and a plane-less world refusal, the device source's canonical key order and sampling suppression, the `Resources` backend treating a missing asset as a value, the `GameObject` binder's live-view tracking and refusals, and the committed-image projection/presentation through `LiveAssemblyImageBuilder`. |
| `Packages/com.gamecore.unity.adapters/Tests/PlayMode/GameCore.Unity.Adapters.PlayMode.Tests.asmdef` | PlayMode test assembly `GameCore.Unity.Adapters.PlayMode.Tests`, which drives **real frames**. |
| `Packages/com.gamecore.unity.adapters/Tests/PlayMode/AdapterPlayModeTests.cs` | PlayMode tests: an idle command-driven world presents on every routed host frame while committing zero steps; destroying every view leaves step, epoch, publication count and demand exactly where they were; a real `GameObject` view is created, applied once from the committed image and destroyed by the frame's own teardown while the world storage survives; and the per-session reset leaves no adapter frame behind. |

### The GC-019 qualification gate (the qualification project)

| Path | Contents |
| --- | --- |
| `unity/.../Runtime/Gc019Family.cs` | `Gc019Step`, `Gc019ScenarioResult` (digest over `name=pass\|fail` lines via `NarrativeDigest.OfLines`) and `IGc019Family : IGc013Family`, adding only the family's command route/target/schema, its own payload and the committed view targets. |
| `unity/.../Runtime/Gc019NarrativeHost.cs` | A third `partial` part of `Gc013NarrativeHost.NarrativeFamily`: the narrative choice identity and payload, its view targets, and `RunGc019GeneratedCatalog`/`RunGc019FixtureCatalog`/`RunBothGc019`. |
| `unity/.../Runtime/Gc019CardsHost.cs` | The card equivalent, a `partial` part of `Gc013CardsHost.CardFamily`, submitting the ordinary card command the card market scenario submits. |
| `unity/.../Runtime/Gc019Scenario.cs` | The eleven-step adapter sequence over one real world per family and catalog, modelled on `W4GateScenario.Executor` and checking the one-publication-series invariant after every publication. |
| `unity/.../Runtime/ProbeGc019.cs` | The `-probeGc019` player mode: the same runner in a stripped headless IL2CPP player, one digest literal per family. |
| `unity/.../Tests/Gc019/GameCore.Gc019.Tests.asmdef` + `Gc019IntegrationTests.cs` | The EditMode suite: one run per family per catalog, one `[Test]` per named observation, and the observation-table/digest check that keeps a renamed or dropped observation from shrinking the gate. |
| `tools/unity/run_gc019_probe.sh` | The player-probe harness: sources `probe_runs.sh`, `PROBE_RUNS` runs, strict JSON validation, every observation name (11 × 2 catalogs × 2 families) plus both digest steps, both digest literals and sixteen clause fragments read out of the step details. |

Every new `.cs` file and every new folder carries a Unity `.meta` with a unique GUID.

## 3. Files modified

| Path | Change | Why |
| --- | --- | --- |
| `unity/.../Runtime/GameCore.Validation.ProbeHost.asmdef` | **Additive only**: one reference line, `GameCore.Unity.Adapters.Fixtures`, after the existing `GameCore.Unity.Adapters` line. | The GC-019 scenario uses the adapter package's deterministic doubles (`DeterministicAssetBackend`, `RecordingViewBinder`). `git diff` shows exactly +1 line. |
| `unity/.../Runtime/ProbeArguments.cs` | `-probeGc019` constant, constructor parameter, `Gc019` property, `Parse` branch and one `IsProbeInvocation` term. | Every earlier task added its own probe mode the same way; this is the additive set. |
| `unity/.../Runtime/ProbeRunner.cs` | One identity branch (`Gc019`, task `GC-019`) and one dispatch branch. | Same. |
| `Packages/com.gamecore.unity.adapters/Runtime/PlayerLoop/GameCoreApplicationPump.cs` | `PumpFrame` calls `AdapterFrameRegistry.CollectInput(host.World)` before `host.PumpFrame(...)` and `AdapterFrameRegistry.Present(host.World)` after it; two new static report properties; `Reset()` clears them. | The two adapter points the pump algorithm in 04 s3 already names. **No new update path**: the pump remains the only driver, and a world with no registered frame reports `Skipped`, so an application that installs no adapters behaves exactly as before. |
| `Packages/com.gamecore.unity.adapters/Runtime/PlayerLoop/GameCoreApplicationReset.cs` | `RunReset` and `ExitPlayModeCleanup` call `AdapterFrameRegistry.Reset()`. | An adapter frame belongs to one world incarnation; a surviving registration would present into a world that no longer exists (04 s9, P-004). |
| `tools/check_game_core_csharp.py` | Additive `TARGETS` entries for the adapter package, its `Fixtures` folder and the two new dotnet projects; additive `engine_free` entries for `Runtime/Pure` and `Fixtures/Runtime`. | The host-side brace/forbidden-construct checker had no coverage of the new package. Committed separately as `shared:`. |
| `dotnet/GameCore.sln` | Two additive project entries and their configuration rows. | Build the new projects in the solution. |
| `dotnet/README.md` | Two project rows plus a paragraph describing the adapter core. | The README enumerates every plain-dotnet project; the omission of the W4 gate's row is recorded as a gap in that handoff, so this one is not repeated. |

The adapter gate's own entry points (`Gc019Family.cs`, `Gc019NarrativeHost.cs`, `Gc019CardsHost.cs`,
`Gc019Scenario.cs`, `ProbeGc019.cs`, `Tests/Gc019/**` and the new package test assemblies) are additive: they call
the existing GC-013 families through `partial` parts and modify no existing scenario, gameplay package or catalogue.

## 4. Contract changes

**None.** No file under `Packages/com.gamecore.contracts/`, no plan DTO and no existing public type was modified.
The adapter surface is additive and lives entirely in `GameCore.Unity.Adapters` (plus its two new plain-dotnet
projects). Two existing *behavioural* entry points changed only by being called from the pump and the reset path, and
both keep their previous behaviour when no adapter frame is registered.

## 5. Requirement and test coverage

| Requirement / test | Where implemented | Where observed |
| --- | --- | --- |
| P-002 participants and authority (`EngineAdapter` admits observations and presents committed output) | `IAdapterFrame`, `AdapterFrameRegistry`, `WorldAdapterFrame` | `AdapterContractTests.TheAdapterFrameRegistryReportsSkippedForAWorldWithNoFrame`, `AThrowingAdapterFrameIsCountedAndDoesNotPropagate`; the Unity EditMode/PlayMode adapter suites; GC-019 scenario steps 6/7 |
| P-007 references and leases (stamped async work, stale completion releases its own acquisition only) | `InputSourceStamp`, `AsyncWorkToken` validation at completion, `AssetLeaseTable.Complete`/`TryAdmitCompletion` | `ADelayedInputCompletionIsDiscardedAfterTheActivationIsRetired`, `ACompletionFromAStaleActivationIsDiscardedAndReleased`, `ALateAssetCompletionAfterRetirementInstallsNothing`, `AFailedCompletionIsReportedAndAPostRetireCompletionIsCounted` |
| P-024 spawn/despawn (a view presents committed state and destroys nothing in gameplay) | `ViewRegistry.Create` (refuses an uncommitted target), `DestroyViewsOf`, `CommittedOutputPresenter` | `DestroyingAViewDestroysOnlyTheView`, `AViewForAnUncommittedTargetIsRefused`, `ThePresenterReadsCommittedOutputAndRemovesOrphanedViews`; scenario step 8 |
| P-034 one owner per authoritative domain; engine-owned domains are stamped observation | `ExternalAuthorityLedger`, `EngineObservation`, `ExternalAuthority.Classify` | `AWorldWithNoExternalDomainNeedsNoPhysicsAdapter`, `AnExternallyOwnedDomainHasOneOwnerAndRefusesGameplayWrites`, `ASampleFromTheWrongAuthorityOrAnOlderStepIsRefused`; scenario step 10 |
| P-038 clocks (presentation never advances the authoritative clock) | `CommittedOutputPresenter` (reads, never produces, an image), `CommittedImageSource.Refresh` | `ThePresenterReadsCommittedOutputAndRemovesOrphanedViews` (`source.RefreshCount == 1`), scenario step 7 |
| P-041/P-043 bounded work and buffers (a full table refuses explicitly) | `AssetLeaseTable` count + byte budgets, `PendingInputCompletionTable` capacity | `TheAssetTableIsBoundedByCountAndByBytes`, `AFullPendingTableRefusesInsteadOfDroppingInFlightWork` |
| P-045 committed output only (immutable images, stale images never overwrite) | `CommittedAssemblyImage`, `ViewRegistry.TryApply` (first apply at the creation token, strict afterwards), `PresentationReport` | `AStalePresentationApplyIsRefused`, `TheFirstPresentationAtTheCreationTokenIsAcceptedAndLaterOnesMustBeNewer`, `AForeignWorldsImageIsNeverAcceptedAsAFirstPresentation`, `ThePresenterReadsCommittedOutputAndRemovesOrphanedViews`, `AHeadlessCompositionPresentsNothingAndKeepsRunning` |
| P-047 in-flight lifetime (gates checked on dispatch and completion) | `PendingInputCompletionTable.Complete`, `AssetLeaseTable.Complete`, `AssetLeaseTable.Discard` | `ADelayedInputCompletionWithALiveActivationReachesTheCommandPort`, `ACompletionFromAStaleActivationIsDiscardedAndReleased` |
| P-048 teardown (dispose at most once, retain quarantine, aggregate failures) | `AssetLeaseTable.Release`/`Retire`/`ReleaseQuarantine`, `AssetReleaseReport` | `ALeaseWithAConsumerIsRetainedAndAFailingReleaseIsQuarantined`, `AFailedCompletionIsReportedAndAPostRetireCompletionIsCounted`; scenario step 11 |
| P-010 scope membership ≠ Transform parent | `ViewRecord.CompositionParent`, `ViewRegistry.TrySetVisualParent`, `CommittedTargetEntry.CompositionParent` | `AVisualReparentDoesNotMoveComposition`; scenario step 9 |
| P-037/P-042/P-050 stamped input into the existing command port | `TypedInputIngress.Submit` | `ARetransmittedSampleReturnsTheRecordedAdmissionAndDoesNotExecuteTwice`, `TheSameSampleIdentityWithDifferentContentIsRefused`, `ARegressingSourceSequenceIsRefusedAndAGapIsLegal`, `ARetryOutsideTheBoundedRetentionReportsResultExpired`, `ASampleStampedByAnotherWorldIsRefused`, `ADegenerateStampIsRefusedRatherThanReadAsTheFirstEventOfASource` |
| P-008 canonical order, no timing dependence | `IdSequence`-derived lease ids, `InputBindingTable.Ordered`, canonical target order in `CommittedAssemblyImage` | `TheBindingTableResolvesSamplesInCanonicalOrder`, `TheAdapterFrameRegistryReportsSkippedForAWorldWithNoFrame` |
| TEST-002 identities, epochs and stale references | `InputSourceStamp.IsAllocated`, `AssetLeaseTable` world check, `ViewRegistry` world check | the foreign-world, degenerate-stamp and stale-token cases above |
| TEST-015 lifecycle and managed resource teardown | `AssetLeaseTable` registered in `WorldResourceLedger`, `AdapterTeardownReport` | `ALeaseWithAConsumerIsRetainedAndAFailingReleaseIsQuarantined`; scenario step 11 |
| TEST-018 Unity worlds, bootstrap and Play Mode | the pump's two adapter points, `AdapterFrameRegistry.Reset` | the PlayMode adapter suite; scenario steps 7/11 |
| TEST-019 engine adapters and single state authority | the whole adapter package; `ExternalAuthorityLedger` | every test above whose name names a view, a reparent or an authority |
| TEST-020 baking, runtime recipes and precompiled plugins | Unchanged by GC-019 — no baker, recipe or runtime compilation is introduced | not claimed here; GC-019's asset path loads data for a known schema only, and an unknown path is refused as a value |

### The eleven GC-019 observations (one per family per catalog, in this order)

The runner is `Gc019Scenario.Run(IGc019Family)`; the qualified name is `<label>/<name>` and the fixture-catalog run
prefixes `fixture:`. Both families' digests over the all-passing table were recomputed independently on this host
from the name table (narrative `c812ccdce22cee6098d3f8dba5c23cfb744c7644aa83a99ae24bb790fbdde837`, cards
`deaff62c2a643221511524063dc0a23cb80b0fe071a3b00c8245fbd1fc6097ce`) and match the suite's and the probe's literals.

| # | Observation | What makes it pass |
| --- | --- | --- |
| 1 | `gc019-world-and-committed-targets` | A real owned world, the lane joined to it, the family's own provider mounted and publishing rows, every declared view target live with at least one committed row, and zero idle steps. |
| 2 | `gc019-input-sample-becomes-a-committed-command` | The sample is admitted through the family's own route/target/schema/payload, exactly one step commits, the world's request ledger records a committed result and one admission, and the retransmission of the identical sample returns `Retransmission`, adds no admission, commits no step and leaves step, revision and rows byte-identical. |
| 3 | `gc019-input-completion-from-a-retired-activation-is-discarded` | A pending completion whose installation was suspended is discarded with its own acquisition released, reaches no command port, and leaves step, admitted count and the plane's admission count unchanged. |
| 4 | `gc019-asset-lease-completes-under-a-live-token` | A lease requested under a live token completes, becomes `Ready` with a readable payload, and is recorded `Ready` in the world's own resource ledger. |
| 5 | `gc019-late-asset-completion-cannot-write-a-retired-world` | A completion under a retired activation is `DiscardedStale` with nothing installed and nothing retained; a throwing release quarantines and teardown retains it; a completion after `Retire()` is counted and installs nothing; a repeat is `AlreadyTerminal`; no load and no lease remains outstanding. |
| 6 | `gc019-presentation-reads-the-committed-snapshot` | Every presented value equals the published binding row of that target/capability/slot and every presented composition parent equals the scope the live target index reports. |
| 7 | `gc019-idle-world-presents-without-stepping` | Four host frames: each `PumpFrame` commits zero steps, the step does not move, the presentation point ran once per frame, and every pass after the first is refused because the committed token is not newer. |
| 8 | `gc019-view-destruction-leaves-gameplay-intact` | After destroying every view the published rows, the scope fingerprint, the revision, the epoch and the step are unchanged, no ledger record was added, and a further real command still commits one step. |
| 9 | `gc019-visual-reparent-does-not-move-composition` | One binder parent change is recorded, the view's `CompositionParent` stays the pre-reparent committed value, the scope tree fingerprint is unchanged and no publication happened. |
| 10 | `gc019-does-not-declare-external-authority` | The ledger declares no external domain, every view target's recipe is ECS-owned kinematic, an intent for it is refused `RefusedEcsOwned`, and the compiled registration contains no physics stage or system. |
| 11 | `gc019-adapter-teardown-participates-in-lifecycle` | The frame retires its leases and views, the registry forgets it, the time driver is cleared, and the world stops with no outstanding job, no retained resource and the owned-world registry back at its pre-create baseline. |

## 6. Known gaps, assumptions and doc ambiguities

1. **Nothing has been compiled or executed on this host.** The build host is the first real gate.
2. **`AssetLeaseTable` reports lease identity, not asset identity.** A lease is keyed by a deterministic `Id128`
   (`IdSequence` salt `0x6763617373657431`), so two runs of the same fixture produce identical lease ids; the engine
   handle is explicitly not an identity (P-054).
3. **The `ResourcePaths` table is content, not convention.** A resource key is a stable 128-bit identity, so the
   mapping from key to engine path must be declared by the application; the backend refuses an undeclared key rather
   than guessing a path (P-015's "not a guessed adapter"). A production asset pipeline is out of scope (GC-019's
   non-goals name Addressables explicitly).
4. **Docs ambiguity: where the adapter frame is called from.** `04` s3 describes the pump algorithm in prose and
   names the two adapter points; it does not name a type. GC-019 therefore defines the pair as `IAdapterFrame` and
   has the *existing* pump call it, which keeps PlayerLoop ownership with GC-005/GC-019 and adds no second driver.
   Recorded as a decision.
5. **Docs ambiguity: "presentation" for a world with no views.** `04` s7 says headless compositions "omit
   presentation services", which this change reads as "the presenter runs and reports `NoViews`" rather than "the
   pump skips presentation". The observable difference is a report, not a step; the reading is recorded so a
   reviewer can overrule it cheaply.
6. **The external-authority seam is a descriptor and a ledger, not a physics integration.** GC-019's non-goals put
   full physics integration in GC-020. What exists here is the declaration, the stamped observation envelope, the
   intent submission path and the ownership refusals; the real `PhysicsScene` stage is GC-020's.
7. **`PostRetireCompletionCount` counts completions that arrive after `Retire()`, including ones for leases teardown
   already discarded.** That is deliberate: the interesting fact is that a world stopped before the completion
   arrived, not which lease it referred to.
8. **The adapter frame's device path binds a sample to one canonical `int32` payload.** A family with a richer
   payload (the card family's 40-byte command, the narrative family's choice pair) builds its own
   `SampledInputCommand` and submits it through the same ingress. The scenario uses the family codecs on purpose; the
   binding table exists for the generic device path so gameplay packages never depend on it.
9. **The IL2CPP player probe is authored, not run.** `-probeGc019` and `tools/unity/run_gc019_probe.sh` exist and the
   player mode runs the same runner the EditMode suite calls, so the headless requirement has a player path; the
   player itself has not been built or launched here, and the W5 integration gate owns joining the W5 modules in one
   actual world.
10. **The completion-after-retirement reading is a decision.** `PostRetireCompletionCount` counts attempts that
    reach a retired table (repeats included) rather than only distinct installations, because P-007 makes "the world
    stopped before this arrival" the observable fact. The scenario's repeat-completion expectation follows that
    reading; a reviewer who prefers the other reading changes one counter and one assertion.
11. **The first presentation of a view is a decision.** `ViewRegistry.TryApply` accepts a view's first apply even at
    the token it was created from, and is strict afterwards. Without that rule a view created from the currently
    committed image could never present that image, which is not what "present the last published snapshot" can mean
    (04 s3); the alternative — a view permanently one publication behind — is worse than the exception. The exception
    is guarded by a world-identity check that runs first, so a foreign image is refused at every point including the
    first. Two tests defend it: `TheFirstPresentationAtTheCreationTokenIsAcceptedAndLaterOnesMustBeNewer` and
    `AForeignWorldsImageIsNeverAcceptedAsAFirstPresentation`.
12. **One housekeeping wart.** Six `.meta` files belonging to the package test assemblies were swept into an earlier
    commit (`487113f`) by an over-broad `git add` while a subagent was still writing them; their content is exactly
    what that subagent wrote and their source files are committed in the next commit, so the revision is complete,
    but the meta/source split is not the tidy single commit it should have been.

## 7. Proposed generic-profile inventory rows (proposals only; the build host promotes them)

The frozen profile at `artifacts/gates/w4-generic-profile/inventory.json` records `P-002` and `P-034` with
`nextOwner: GC-019` and these gaps:

* **P-002** — "the `EngineAdapter` observation boundary (TEST-019) has no …". GC-019 supplies that boundary: an
  adapter port the single update path calls, with input admitted through the world's existing command port and
  presentation read from committed images only. Proposed: keep the status `Partial` (the role table is still not
  fully evidenced in a player), add `gc019.delta` naming `AdapterFrames.cs`, `WorldAdapterFrame` and the two pump
  call sites, and narrow the gap to the missing IL2CPP player evidence for the adapter boundary.
* **P-034** — "the declared external-authority domain (engine-owned state stamped as observation, intent …) is
  unr…". GC-019 supplies the declaration, the stamped `EngineObservation`, the intent path and the refusal of
  gameplay writes to an externally owned quantity, and the GC-019 scenario observes that a card/narrative world
  declares **no** external domain at all. Proposed: keep `Partial` (the real physics stage is GC-020's), add a
  `gc019.delta` naming `ExternalAuthority.cs` and the scenario step, and narrow the gap to "no rigidbody/physics
  stage is integrated; the descriptor, the stamped observation and the ownership refusals are implemented and
  observed without physics".

Neither row is changed here; this section is the proposal the build host acts on.

## 8. Exact commands for the Linux build host

Run from the repository root. Nothing below has been run.

### 8.1 The adapter halves alone

```sh
dotnet build dotnet/src/GameCore.Adapters/GameCore.Adapters.csproj -c Release
dotnet test  dotnet/tests/GameCore.Adapters.Tests/GameCore.Adapters.Tests.csproj -c Release --logger trx \
  --results-directory artifacts/gc-019/trx
```

### 8.2 The whole pure solution (what the wave gate runs)

```sh
dotnet build dotnet/GameCore.sln -c Release
dotnet test  dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gc-019/trx
```

### 8.3 The Unity suites

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode -testFilter GameCore.Unity.Adapters.Contract.Tests \
  -testResults artifacts/gc-019/unity/adapters-editmode.xml \
  -logFile artifacts/gc-019/unity/adapters-editmode.log

"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform PlayMode -testFilter GameCore.Unity.Adapters.PlayMode.Tests \
  -testResults artifacts/gc-019/unity/adapters-playmode.xml \
  -logFile artifacts/gc-019/unity/adapters-playmode.log

"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode -testFilter GameCore.Gc019.Tests \
  -testResults artifacts/gc-019/unity/gc019-editmode.xml \
  -logFile artifacts/gc-019/unity/gc019-editmode.log
```

Do not add `-quit` to a test-run command (04 §10).

### 8.4 The whole EditMode and PlayMode suites, as the wave gate runs them

```sh
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode -testResults artifacts/gc-019/unity/editmode-results.xml \
  -logFile artifacts/gc-019/unity/editmode.log

"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform PlayMode -testResults artifacts/gc-019/unity/playmode-results.xml \
  -logFile artifacts/gc-019/unity/playmode.log
```

### 8.5 Headless proof and the host-side static checks

```sh
python3 tools/check_game_core_csharp.py          # covers the adapter package on this revision
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
python3 tools/make_unity_metas.py                 # idempotent; reports 0 creations on a complete tree
```

### 8.6 The GC-019 player probe (the headless half)

The player is built once by the existing probe build (`tools/unity/build_probe.sh`, which compiles both families and
the adapter package into one IL2CPP binary), then this mode runs inside it:

```sh
UNITY="$UNITY" ARTIFACTS=artifacts/gc-019/toolchain tools/unity/build_probe.sh
PROBE_RUNS=5 ARTIFACTS=artifacts/gc-019/toolchain tools/unity/run_gc019_probe.sh
```

The harness asserts both task and mode identity, `Pass` with no failing step, all 11 × 2 observation names per
family (48 steps plus the two digest steps), both digest literals and sixteen clause fragments read out of the step
details. Nothing in it has been run here.

### 8.7 Why §8.4's whole-suite runs matter for this task

The adapter package's new test assemblies and the `GameCore.Gc019.Tests` assembly are part of the project-wide
EditMode and PlayMode runs the wave gate already performs, so the wave gate exercises them without a new step; the
filtered commands above exist so a failure can be localised quickly.

## 9. What actually ran on this host

```sh
python3 tools/check_game_core_csharp.py                  # checked 389 C# file(s); ok (brace balance,
                                                        # forbidden constructs, missing-return scan,
                                                        # #nullable enable, no TODO/FIXME)
python3 tools/validate_game_core_docs.py --self-test     # 9 isolated fixtures passed
python3 tools/validate_game_core_docs.py                 # 14 documents; links, anchors, IDs, traceability,
                                                        # DAG, wave ordering
python3 tools/make_unity_metas.py                        # idempotent; a second run reports 0 creations
bash -n tools/unity/run_gc019_probe.sh                   # the probe harness parses
# .meta GUID uniqueness over the whole worktree: 607 metas, 607 unique, 0 duplicates
# digest recomputation: both GC-019 literals reproduced independently from the observation-name table
# member audit: every adapter member the suite and the scenario call was read at its definition
```

None of that is a build, an import, a test or a player run. In particular the brace/forbidden-construct checker is a
smoke check, not a compiler: it does not resolve types, check overloads or validate nullability, and the first real
signal about this change set is the build host's build.

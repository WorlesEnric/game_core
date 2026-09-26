# GC-021 handoff — durable outbox and destination idempotency seams

Branch `gc-021` (worktree `/Users/yangcao/wkspace/gc-wt/gc-021`), forked from `main` at `1cafced` (the Wave 5
integration gate).

**Status of every build/test command in this document: `NotRun (pending orchestrator build host)`.** This host has no
.NET SDK, no C# compiler, no Mono and no Unity, so nothing in this change set has been compiled, imported, executed or
built here. §9 lists exactly what did run on this host. None of it is a build, an import, a test or a player run.

## 1. Summary

`GC-021` — "Support cross-family/external committed delivery without pretending in-memory events are durable exactly
once." Normative surface: **P-003, P-042, P-043, P-044, P-045, P-049, P-050, P-053, P-054**, 06 §7, 07 §5 (the
narrative→card reward combination) and tests **TEST-013..TEST-017**.

Before this change no delivery code existed anywhere in `Packages/`: no outbox, no external idempotency key, no
acknowledgement cursor, and `P-053`'s "external outbox/dedup cursors when used" had no representation in the
checkpoint format at all. This change adds the format section, the engine-free delivery core, the world-side owner,
the reward integration package, the file-backed journal with deterministic crash points, and the tests.

The design's central refusal: **it never claims exactly-once.** P-045's boundary is at-least-once delivery within
retention with destination deduplication, and that is what is implemented. An obligation that was handed over and
never acknowledged *will* be handed over again; the explicit external idempotency key — derived from the committed
event, so it is identical on every attempt and after any recovery — is what makes the redelivery a no-op at a
destination that honours it. Nothing in this change set says otherwise.

## 2. Commits on this branch

| Commit | Contents |
|---|---|
| `shared: version the checkpoint format for external delivery obligations (GC-021)` | `CheckpointRecordKind.Outbox = 12` + `RecordKindCount 13`, `OutboxRecordValue`, the three new enums, `HeaderRecordValue.OutboxCount` (field 40), `CountsMatch(..., int outbox)`, `CheckpointCounts.Outbox`, the catalog description's new schema + header field, regenerated `CheckpointCatalog.g.cs`, `CheckpointCatalogTests.DeclaredSchemaCount 13`. |
| `GC-021: the engine-free durable outbox, delivery keys and crash-point seam` | `Runtime/Pure/Delivery/DeliveryKey.cs`, `DurableOutbox.cs`, `DurableDeliveryAdapter.cs`. |
| `GC-021: carry the outbox and its cursors through capture, restore and the world owner` | `CommittedBoundarySnapshot.Outbox`, `CheckpointCapture`, `CaptureContext.OutboxRows`, `RestorePlan.Outbox`, `RestoreRefusal.InvalidOutbox`, `CheckpointRestorePlanner.ValidateOutbox`, `CheckpointRestoreExecutor` + `IRestoreOutboxBuilder`, `RestoreOutcome.RestoredOutboxRows`, `CheckpointSerializerBindings`'s 13th binding, new `Runtime/Delivery/WorldDeliveryOwner.cs`. |
| `GC-021: the deterministic crash-point and outbox round-trip suite` | `Tests/Delivery/DeliveryOutboxTests.cs`, `Tests/Delivery/DeliveryCrashFixture.cs`, the asmdef, the `Tests/Delivery/**` glob in `GameCore.Execution.Tests.csproj`, outbox fixtures and three planner cases in the pure suites. |
| `GC-021: the narrative-to-card reward composition as an ordinary package` | `Packages/com.gamecore.gameplay.integration/` (four Runtime files, `package.json`, asmdef), the validation project's `manifest.json`/`testables`, `check_game_core_csharp.py`'s `TARGETS`. |
| `shared: propagate the outbox kind through the checkpoint codecs, fixtures and the hand-made document` | `Gc018CheckpointCodecs`, `Gc018Scenario`, the contract test codecs and fixtures. See §5.2 for why `Gc018Scenario.cs` is in this commit. |
| `GC-021: the probe harness and the release-clone preparation for the new mode` | `tools/unity/run_gc021_probe.sh`, `tools/unity/prepare_gc017_release_project.py`. |
| `GC-021: a rebuilt outbox reports what it reinstated` | `DurableOutbox.TryRestore` counters. See §5.1. |
| `GC-021: fix the defects an adversarial review found (two of them release-blocking)` | Nine defects across `CheckpointRestorePlan`, `DeliveryKey`, `CheckpointRecords`, `DurableOutbox` and the reward bridge. See §5.4. |
| `GC-021: order a reinstated outbox's open obligations canonically` | `DurableOutbox.AdoptOpenOrder` (P-008). |
| `GC-021: the actual-world scenario, its EditMode suite and the probe mode` | `Gc021Scenario.cs`, `Gc021Family.cs`, `ProbeGc021.cs`, `Tests/Gc021/**`, `ProbeArguments.cs`, `ProbeRunner.cs`, the probe-host asmdef. See §5.5. |

## 3. Files created

### 3.1 Engine-free delivery core — `GameCore.Execution.Delivery`

Compiled into **both** `dotnet/src/GameCore.Execution` and `GameCore.Unity.Runtime` by the existing `Runtime/Pure/**`
glob, so the same assertions run as pure dotnet tests and as Unity EditMode tests over identical sources.

| File | Contents |
|---|---|
| `Packages/com.gamecore.unity.runtime/Runtime/Pure/Delivery/DeliveryKey.cs` | `OutboxAdmission`, `DeliveryOutcome`, `DeliveryKey` (`Derive`), `DeliveryObligation`. The three stable identities of one obligation and the destination command it carries. |
| `Packages/com.gamecore.unity.runtime/Runtime/Pure/Delivery/DurableOutbox.cs` | `DeliveryCursor`, `DurableOutbox`: commit, begin-delivery, acknowledge, reject, compensate, capacity and terminal retention, per-destination cursors, `ToRecords`, `TryRestore`, `CanAccept`. |
| `Packages/com.gamecore.unity.runtime/Runtime/Pure/Delivery/DurableDeliveryAdapter.cs` | `DeliveryBoundaries` (the named boundaries), `IDeliveryStepHook` (the reporting seam), `IDeliveryJournal`/`FileDeliveryJournal`/`MemoryDeliveryJournal`, `IOutboxRowCodec`/`CanonicalOutboxRowCodec`, `DestinationOutcome`, `DeliveryAttempt`, `IDestinationPort`, `DurableDeliveryAdapter`. |

### 3.2 World-side owner — `GameCore.Unity.Runtime.Delivery`

| File | Contents |
|---|---|
| `Packages/com.gamecore.unity.runtime/Runtime/Delivery/WorldDeliveryOwner.cs` | `DeliveryObligationRequest`, `IDeliveryObligationSource`, `WorldDeliveryOwner`: the committed-event cursor, the poll pass, the bounded dispatch pass, destination-port registration, `ToRecords`, `TryReinstate`. |

### 3.3 Crash fixture and tests

| File | Contents |
|---|---|
| `Packages/com.gamecore.unity.runtime/Tests/Delivery/DeliveryCrashFixture.cs` | `DeliveryCrashPoint`, `DeliveryCrashException`, `DeliveryCrashMarker : IDeliveryStepHook`. **Test-only by design** — see §6 item 2. |
| `Packages/com.gamecore.unity.runtime/Tests/Delivery/DeliveryOutboxTests.cs` | 24 NUnit cases across four fixtures: key derivation, outbox bookkeeping, the journal/row codec, and the six scripted crash points. |
| `Packages/com.gamecore.unity.runtime/Tests/Delivery/GameCore.Unity.Delivery.Tests.asmdef` | Editor-only EditMode assembly. |

### 3.4 Reward integration package — `Packages/com.gamecore.gameplay.integration/`

| File | Contents |
|---|---|
| `package.json`, `Runtime/GameCore.Gameplay.Integration.asmdef` | The package's declared dependencies (both gameplay and both rules packages, contracts, unity.runtime). |
| `Runtime/RewardOutbox/RewardRule.cs` | `RewardDefinition`, `RewardCatalog`, `CardTableConstants`. Reward content, keyed by the node the committed choice records. |
| `Runtime/RewardOutbox/RewardPayloadCodec.cs` | The canonical reward payload (version, node, recipient, holder, revision, card). |
| `Runtime/RewardOutbox/CardRewardDestination.cs` | `RewardAttemptStatus`, `RewardAttemptReport`, `CardRewardDestination : IDestinationPort`. The only place a card mutates. |
| `Runtime/RewardOutbox/NarrativeCardRewardBridge.cs` | `RewardBridgePassReport`, `NarrativeCardRewardBridge : IDeliveryObligationSource, IDisposable`. The bridge. |

### 3.5 Harness

| File | Contents |
|---|---|
| `tools/unity/run_gc021_probe.sh` | The player harness. Not a bare copy of `run_gc018_probe.sh`: it does not pin the digest literals it cannot compute, and asserts their cross-catalog equality instead (§6 item 4). |

### 3.6 Files modified

| Path | Change | Why it is safe |
|---|---|---|
| `check_game_core_csharp.py` | One new `TARGETS` entry for the integration package. | Additive; the package is a gameplay assembly, so it needs the balance/forbidden-construct checks but not `engine_free`. |
| `prepare_gc017_release_project.py` | The GC-021 scenario/family/probe joins the removal list; the mode's flag, property, parse branch and dispatch arm are stripped. | The same whole-block surgery the GC-017 and Wave 5 modes use; every `replace_once` is asserted to match exactly once. |
| `unity/GameCore.Validation/Packages/manifest.json` | The integration package is a dependency and a testable. | Two added lines; the dependency order is otherwise untouched (diff is 2 lines). |
| `dotnet/tests/GameCore.Execution.Tests/GameCore.Execution.Tests.csproj` | A `Tests/Delivery/**` glob beside the `Tests/Observation/**` one. | Additive; no project reference changed. |
| the contract/persistence/codec/test files listed in §2 | The outbox kind and header count, propagated. | §9 records the scripted parity and call-site audits. |

## 4. Requirement → implementation → observation mapping

| Requirement / test | Where implemented | Where observed (this change set) |
|---|---|---|
| **P-003** three kinds of change; no reversible `Effect` API | Nothing here reverts: `TryAcknowledge` is idempotent, `TryReject`/`TryCompensate` are terminal, and the destination port's vocabulary is six outcomes. | `ARejectionAndCompensationAreTerminalAndKeepTheirReason`; the `reverseMembers=0` clause in the probe runner. |
| **P-042** a typed request, and `Accepted` is not `Committed` | `CardRewardDestination` submits the card family's own command and reads its own result back; `Applied` requires the hand to have grown. | `AnUnavailableDestinationLeavesTheObligationOpenAndAnUnsupportedStateIsExplicit`. |
| **P-043** bounded work refuses rather than dropping | `DurableOutbox.CanAccept`/`TryCommit` capacity, `TerminalRetention` pruning with `PrunedTerminalCount`, `WorldDeliveryOwner`'s bounded poll/dispatch passes. | `CapacityExhaustionIsExplicitAndTheRefusedObligationIsNotTracked`, `TerminalRetentionPrunesAndCountsRatherThanGrowingWithoutBound`. |
| **P-044** a post-publication delivery error never rewinds a committed step | The outbox is downstream of publication: it reads committed events only and never mutates the step. | `WorldDeliveryOwner` reads through `WorldMessagePlane.ReadEvents`; asserted by every delivery test (the world's step never moves). |
| **P-045** external idempotency keys, persisted outbox, at-least-once | `DeliveryKey.Derive` (the key), `DurableDeliveryAdapter` (persist-then-apply), `FileDeliveryJournal` (durable), `OutboxDurability` (volatile vs durable). | `RedeliveryAfterAcknowledgmentLossAppliesTheDestinationMutationOnce` is the headline case; `ADurableAdapterDeclaresDurabilityAndAVolatileOneRefusesToPromiseIt`; the six crash-point cases. |
| **P-049** no hidden replay of external effects | Recovery is explicit (`TryRecover`, `TryReinstate`) and never automatic; an old session's obligation is reinstated into a **new** session. | `ACrashAfterTheAppendLeavesACommittedObligationThatOutlivesTheSourceWorld`; the `gc021-obligation-survives-the-source-world-unload` probe step. |
| **P-050** one attempt, one operation ID | `DurableDeliveryAdapter` carries the obligation's causal `OperationId`; `CardRewardDestination.OperationFor` reuses one operation identity per obligation. | `AJournalRefusalPreventsTheCommitFromBeingRecordedInMemory`; key derivation is asserted stable across recovery. |
| **P-053** the checkpoint carries outbox/dedup cursors | `CheckpointRecordKind.Outbox`, `CommittedBoundarySnapshot.Outbox`, `CaptureContext.OutboxRows`, `RestorePlan.Outbox`. | `APlanCarriesTheOutboxRowsTheDocumentDeclares`, `ACursorThatDisagreesWithItsTerminalRowsRefusesThePlan`, `TwoObligationRowsForOneIdentityRefuseThePlan`. |
| **P-054** versions, field ids, canonical bytes, explicit nulls | `OutboxRecordValue.RecordVersion`, `CanonicalOutboxRowCodec`'s format header/field-count/width checks, `RewardPayloadCodec`'s version byte. | `ACanonicalRowFrameRoundTripsEveryField`, `ARowFrameWithATamperedByteOrAnotherVersionIsRefusedRatherThanDecoded`, `ARestoreRefusesARowFromAnUnknownRecordVersionOrOfAnUnknownKind`. |
| **TEST-013** authority, requests, buffers | Delivery crosses the ordinary command port (`host.Submit`), never a second authority. | `WorldDeliveryOwner`'s header note and its use of `UnityWorldHost.Submit`; `CardRewardDestination.ApplyReward`. |
| **TEST-014** committed events only, read-only inspection | `PollCommittedEvents` reads `WorldMessagePlane.ReadEvents`, so only committed events are consumed. | `AnUnavailableDestinationLeavesTheObligationOpenAndAnUnsupportedStateIsExplicit` (the destination's own committed result is what settles the obligation). |
| **TEST-015** lifecycle and managed teardown | `WorldDeliveryOwner.Dispose` clears ports, the port index and the correlation map; the bridge disposes its owner. | `gc021-obligation-survives-the-source-world-unload` disposes the source world and still delivers from the rebuilt outbox; lifecycle stress proper is GC-022's. |
| **TEST-016** fault boundaries | The persistence boundaries are exactly the fault boundaries this task adds, and each is scripted. | all six `DeliveryCrashPoint` cases. |
| **TEST-017** checkpoints and schema evolution | The outbox section is a versioned record kind with its own row version, captured, planned and reinstated. | `APlanCarriesTheOutboxRowsTheDocumentDeclares`; `ARestoredOutboxProjectsRowsThatSurviveAJournalRoundTrip`. |
| **O-20** CaptureCheckpoint | The capture carries the world's outbox rows. | the plan/capture cases above. |
| **O-21** RestoreCheckpoint | `IRestoreOutboxBuilder` reinstates and proves the section before exposure. | `gc021-obligation-survives-the-source-world-unload` exercises the reinstate path end to end in a fresh session; the executor's own sequence is GC-018's and the W5 gate's. |

## 5. Defects found and fixed during this task

All three were found by review, not by running anything, and each has its own commit or is recorded here.

### 5.1 `TryRestore` reported nothing about what it reinstated

Two independent read-only reviews (the propagation worker and the scenario worker) found the same defect: `TryRestore`
created obligations without touching the counters, so an outbox rebuilt from checkpoint rows reported
`CommitCount == 0` while the landed crash tests assert `1`. Fixed in `DurableOutbox.TryRestore`, and the same gap
closed in the three sibling counters the rows genuinely carry rather than left at zero (`DeliveryCount`,
`RedeliveryCount`, and `AcknowledgeCount`/`RejectCount`/`CompensateCount` from each row's own recorded state). Every
restored number is read off a row's recorded state and attempt count; nothing is invented, and an empty rebuild still
reports zero.

### 5.2 My own contract change deleted a header constructor parameter

The propagating worker reported that the GC-021 diff had deleted `HeaderRecordValue`'s `sourceHostTicksPerSecond`
parameter and its assignment (CS0171 plus every caller wrong), and that `CheckpointCapture` passed the outbox count in
the wrong position. Both were mine and both are fixed. Verified by script rather than by eye: the manual
`HeaderRecordValue` and `OutboxRecordValue` constructor parameter lists are now *identical*, name for name and in the
same order, to the generated catalog's (40/40 and 27/27), every field is assigned, and an arity audit of every
`HeaderRecordValue` / `CheckpointCounts` / `CountsMatch` / `CommittedBoundarySnapshot` / `CheckpointSerializerBindings`
call site in the repository reports zero mismatches.

`Gc018Scenario.cs` was also fixed here. It is not a Wave 6 file and the propagation worker's brief excluded it; the
only reason it broke was this contract change, so it belongs to this branch's `shared:` commit rather than to a task
that owns something else.

### 5.3 The crash marker would have shipped in the runtime

The first design put `DeliveryCrashPoint`, `DeliveryCrashException` and `DeliveryCrashMarker` in
`Runtime/Pure/Delivery/`, which is production code. That is a new crash-on-demand API in a shipping build whose
release-surface story is precisely "the injected fault is compiled out"
(`tools/check_release_fault_free.py`, `04` §6). It was caught *before* the release check was re-run and refactored:

* the runtime keeps only `DeliveryBoundaries` (a list of string constants) and `IDeliveryStepHook` — a *reporting*
  seam that cannot change an outcome, and which no shipping caller ever installs;
* the marker, its enum and its exception moved to `Tests/Delivery/DeliveryCrashFixture.cs`, so they are compiled into
  the pure test project and the EditMode suite and never into a player.

`tools/check_release_fault_free.py --no-build` passes on this revision (§9). The `--no-build` mode does not inspect a
compiled assembly, so the build host must run the full check.

### 5.4 Nine defects found by an adversarial review of the whole change set

An independent read-only review of the complete diff found nine defects. All nine were real and all nine are fixed
(`GC-021: fix the defects an adversarial review found`). The two at the top would have stopped the branch from
compiling or from satisfying its own headline acceptance, which is worth recording plainly:

1. **`CheckpointRestorePlan.Plan` passed `rngStreams` twice** to `RestorePlan`'s 17-parameter constructor — a leftover
   from this branch's own edit. That is CS1501 under `TreatWarningsAsErrors`, so the assembly could not compile and no
   test, EditMode suite or probe could run at all. My own eye missed it; a compiler would not have.
2. **`DeliveryObligation.IsOpen` was `State == Pending`**, which made a `Delivered` obligation — handed to the
   destination, outcome unknown — *terminal*. That is precisely the state the at-least-once contract exists for. As
   written, `TryBeginDelivery` answered `AlreadyTerminal` before its redelivery branch could run, the adapter's
   `TryDeliver`/`TryAcknowledge` refused to settle it, `OpenObligations()` dropped it and a recovered outbox reported
   `OpenCount == 0`, so `DeliveryOutcome.Acknowledged` was unreachable through the adapter. The scenario's headline
   observation would have read red. Both `DeliveryObligation.IsOpen` and `OutboxRecordValue.IsOpen` now treat
   `Pending` and `Delivered` as open and only `Acknowledged`/`Rejected`/`Compensated` as terminal.
3. The restore counters (added in the immediately preceding commit) double counted on the *checkpoint* path, because
   `ToRecords` emits an Obligation row *and* a Terminal row for one terminal obligation. Only Obligation rows count.
4. `DeliveryCursor.TerminalTotal` counted acknowledgements while `RetainedTerminals` counted every retained terminal
   record, so a destination that had both refused and acknowledged something underflowed `PrunedTerminals` (unsigned
   subtraction), and the underflow was written into a cursor row and copied into `PrunedTerminalCount` on restore.
5. `TryRestore` threw `ArgumentException` instead of returning false on a row with a default identity, because the
   only `try`/`catch` wrapped the outbox construction — contradicting its own refusal contract.
6. `TryRestore` accepted a Terminal row whose Obligation row the section did not carry, a shape
   `CheckpointRestorePlanner.ValidateOutbox` refuses, so two implementations of one rule disagreed.
7. `PrunedTerminalCount` was restored from only the first cursor row with a nonzero count, though it is the whole
   outbox's total while the counts are per destination.
8. `RewardBridgePassReport.EventsRead`/`RewardsRecognised` were documented as per-pass values but passed cumulative
   totals, unlike their six siblings.
9. (Recorded for completeness; fixed in the review commit) `CardId.None` does not exist — see §6 item 8.

The reviewer also confirmed, and I re-checked independently: the key derivation, the persist-then-apply ordering at
every transition, durability honesty, P-008 enumeration discipline, the row codec and file journal, the planner's
outbox rules, every card and narrative API the reward package names, and that no test is a tautology. Two of the
nine findings arrived independently from other workers before the review (the `CommitCount` gap, §5.1) — the same
defect being found twice from different directions is the reason §9's delegated audits are listed as evidence.

### 5.5 And `AdoptOpenOrder` rebuilt the order from the document

Found while fixing §5.4 item 3: `AdoptOpenOrder` rebuilt the open-obligation list by *removing* terminal entries from
the order the rows happened to arrive in. A document is a set of rows, so the order a dispatcher walks must be this
outbox's own canonical order rather than whatever order a writer laid the section out in (P-008). It now rebuilds
from the reinstated obligations sorted by canonical order.

## 6. Design decisions and doc ambiguities

Recorded because `09` invites the simplest reading consistent with `00`, and `00` wins over `05`, `05` over `09`.

1. **The outbox section is one record kind with a row kind, appended at ordinal 12.** `P-053` requires a checkpoint to
   carry outbox/dedup cursors; it does not prescribe how. Appending after `Cursor` keeps every existing field id
   (`FieldIdOf = 100 + ordinal`), and `TryKindOfField` already refuses a field id outside the declared range with
   `UnsupportedVersion` — so an older reader refuses the document instead of skipping bytes it cannot interpret, which
   is the behaviour `P-055` requires and which a new feature id could not have improved on without new machinery.
2. **The crash seam is a callback, not an injection point.** `DeliveryCrashPoint`'s enum became
   `DeliveryBoundaries`' string constants so the *test* assembly owns the scripting, per §5.3. The runtime's seam is
   deliberately powerless: it reports, it cannot fail. This is not a fault-injection mechanism subject to `04` §6's
   compile-out rule; it is a no-op observation hook that a shipping build leaves null.
3. **`OutboxDurability` is a declared value, not a property of the journal's existence.** A row records the durability
   its world actually had, `Unspecified` is reported as itself (never upgraded to durable), and an adapter with no
   journal refuses an obligation that requires durability with `OutboxAdmission.DurabilityUnavailable` rather than
   accepting one it cannot keep. That is the operational content of "volatile delivery is clearly distinguishable from
   configured durable delivery".
4. **The probe runner does not pin the digest literals.** A digest is computed over the observation names and their
   pass flags and cannot be known before the sequence first runs. `ProbeGc021` therefore ships
   `NarrativeDigest = "PENDING"` / `CardsDigest = "PENDING"`, and `run_gc021_probe.sh` asserts the falsifiable thing it
   can (generated digest == fixture digest, i.e. both catalogs ran the same named sequence) and an exact pin only when
   `GC021_DIGEST_NARRATIVE` / `GC021_DIGEST_CARDS` are supplied. Inventing a literal would be a fabricated result;
   this is the same completion path `ProbeW5Gate` used.
5. **Restore reinstates the outbox through an optional builder seam.** `IRestoreOutboxBuilder` is separate from
   `IRestoreTargetBuilder` because an outbox is a managed owner, not ECS storage, and a world with no outbox simply
   does not implement it — so `Gc018FamilyRestoreBuilder` needed no change and the executor's step is a no-op for
   worlds without one. `RestoreOutcome.RestoredOutboxRows` reports what was reinstated.
6. **The reward's source receipt is the committed choice event.** `07` §5 names a `QuestTransitionReceipt`; the shipped
   narrative package emits exactly one committed event for an accepted choice (`narrative.schema.choice-committed`,
   carrying the resulting node and status) and no quest-transition event at all. `00` settles it: the committed choice
   event *is* the receipt, and the reward keys on the node that event records. No narrative package was edited.
7. **The reward's destination mutation is the card family's own `Transfer`.** `07` §5 shows
   `GrantCards(RewardId, seat-a, [reward-card])` and a rule that "admits this system-generated reward independently of
   the player's turn". The shipped card package declares exactly three command kinds — `SubmitSet`, `Transfer`,
   `Contest` — and `CardCommitSystem.TryApply` refuses any decision with zero removals and always advances the table
   version and the turn number. A reward therefore reaches a hand the only way the family can express it, a transfer
   from a holding seat. Recorded as a gap in §7, not silently reinterpreted as a grant.
8. **`CardId.None` does not exist.** The rules' `CardId` exposes an `IsNone` *predicate* over `Value == 0UL` and no
   `None` *member*. The unused candidate slots are written with `new CardId(0UL)`, which is that same encoding, rather
   than adding a member to a package this task does not own.

## 7. Known gaps, assumptions and doc ambiguities

1. **Nothing has been compiled or executed on this host.** The highest-risk items, in the order a compiler would find
   them: (a) `DurableDeliveryAdapter.cs` is ~1 600 lines and `DurableOutbox.cs` ~960, both hand-written and reviewed
   but never compiled — the crash-point cases in particular depend on the exact ordering of `TryPersist` calls, which
   only a run proves; (b) the integration package's member references were checked line by line against the card and
   narrative APIs (§9) but a compiler settles it; (c) `WorldDeliveryOwner`'s poll depends on the world's committed-event
   reader reporting `CursorOutcome.Ok` at the boundary it is called at.
2. **No Unity world ran.** `GC-021`'s acceptance says "deterministic crash-point tests (dotnet + Unity world)". Both
   halves now exist in source — the dotnet suite in `Tests/Delivery` and the world sequence in `Gc021Scenario` with
   its EditMode suite and probe mode — but neither has been executed, so **no Unity-world evidence exists yet** and
   no row is proposed as promoted on the strength of it. What was verified without a toolchain is structural: the
   probe harness's observation-name list is byte-equal to `Gc021Scenario.ObservationNames`, every clause fragment the
   harness greps for exists in a step detail, every referenced assembly resolves, and the release clone strips the
   mode cleanly with no dangling reference.
3. **`O-22 RecoverWorld` is still not composed** — GC-027 owns it over the restore path GC-018 and the W5 gate proved.
   `P-049`'s host-configured bounded retry attempts remain unproven too; this task adds no retry policy (a rejection
   is terminal and a compensation is explicit, precisely so nothing here becomes an implicit retry loop).
4. **The reward bridge's narrative half is content-dependent.** `RewardCatalog.Default()` rewards node 1, which is the
   node the narrative fixture's first accepted choice is expected to reach; if the fixture lands elsewhere the probe
   step must report that as an actionable failure rather than pass. Whether the reward transfer commits against the
   card fixture's seat stock is likewise a real observable (a refusal is legitimate and is reported). Neither is
   asserted away.
5. **`PRESERVE` for the completed outbox is not declared.** `07` §5 asks `NarrativeCardRewards` to declare
   `PreserveDormant` for its completed outbox with a scratch-migration precondition. That is a slot/lifecycle
   declaration in a plugin manifest; this task's package has no mounted plugin manifest (the bridge is an ordinary
   caller-owned object, not an installation), so the declaration has nowhere to live until a task mounts the bridge as
   a plugin. Recorded rather than faked.
6. **`GC-022`/`GC-023` share `P-045`, `P-043` and `P-050`.** Retention under memory pressure (TEST-023) and
   cursor/dedup scale are theirs; this change set exercises the boundaries at one-world scale.
7. **`check_release_fault_free.py` was run in `--no-build` mode only.** The compiled-assembly half needs the build host.

## 8. Parallel work and shared files

`GC-021` ran in parallel with `GC-020`, `GC-022`, `GC-023` from the same `main`. Two shared-file categories needed
handling:

* **The checkpoint format** (`GameCore.Contracts`, plan/record DTOs). The two additions are additive — one appended
  record kind and one appended header field — and §5.1/§5.2 record the two defects that propagating them exposed.
  Every change to another task's file is in a commit prefixed `shared:` except `Gc018Scenario.cs`, which is explained
  in §5.2.
* **The Unity probe host** (`ProbeArguments.cs`, `ProbeRunner.cs`, `GameCore.Validation.ProbeHost.asmdef`). The new
  mode is one flag, one property, one parse branch, one `IsProbeInvocation` term, one constructor argument, one
  dispatch arm and one report-identity arm per mode — the shape the W5 gate introduced so that adding a mode is a
  block rather than a reshuffle. `prepare_gc017_release_project.py` was taught to strip exactly that block.

Delegation used while authoring this change set, all read-only or mechanically scoped: three reconnaissance passes
(checkpoint subsystem, both gameplay families, harness conventions), one API-surface extraction per family, one
mechanical propagation of the new record kind, one `TARGETS`/glob wiring audit, one adversarial review of the
delivery core, and the Unity-world scenario authoring (§7 item 2). Their findings are in §5; every claim they made
about a file was re-verified here before this handoff was written.

## 9. What actually ran on this host

```sh
python3 tools/check_game_core_csharp.py
# checked 475 C# file(s)  ->  ok   (after every edit round; also enforces #nullable, C# 9 rules, brace balance)
python3 tools/check_release_fault_free.py --no-build
# GC-017 release surface: PASS (source + switch + asmdef halves; the compiled-assembly half is [not run])
python3 tools/validate_game_core_docs.py --self-test
# Documentation validator self-test passed: 9 isolated positive/negative fixtures.
python3 tools/validate_game_core_docs.py
# passed: 14 Markdown documents; local links, anchors, IDs, traceability, task DAG, wave ordering checked.
python3 tools/emit_checkpoint_catalog.py <description> <tmp>
# byte-identical to the committed CheckpointCatalog.g.cs
python3 tools/verify_generated_catalog.py <regenerated>
# 0 groups, 13 factories, 13 schemas, 1 features; file hash and catalog fingerprint recompute correctly
python3 tools/make_unity_metas.py     # created 12, then idempotent (second run: 0)
bash -n tools/unity/run_gc021_probe.sh
```

Also run, as scripts rather than by eye:

* a `.meta` GUID uniqueness scan over `Packages/**` + `unity/**` (680 GUIDs, zero duplicates) and a `.cs`↔`.cs.meta`
  coverage scan (no missing pair);
* a parity check that the manual `HeaderRecordValue` and `OutboxRecordValue` constructor parameter lists equal the
  generated catalog's, name for name and in order (40/40 and 27/27), with every field assigned;
* an arity audit of every `HeaderRecordValue` / `CheckpointCounts` / `CountsMatch` / `CommittedBoundarySnapshot` /
  `CheckpointSerializerBindings` / `OutboxRecordValue` call site in the repository (zero mismatches);
* a genre-token scan over the new engine-free delivery files against `tools/w4_generic_profile_audit.py`'s token list
  (zero hits after the four comment-level hits it found were reworded);
* an independent read-only reference-closure audit of all seven new delivery/integration files against the exact
  members of the card, narrative and kernel APIs they name, which found the one blocking defect in §5 (fixed) and no
  warning-level finding;
* an adversarial read-only review of the whole change set, which found the nine defects in §5.4;
* a brace-aware arity audit of every `new HeaderRecordValue` (10 sites, all 40), `new CheckpointCounts` (8, all 12),
  `CountsMatch` (8, all 12), `new CheckpointSerializerBindings` (1, 26), `new OutboxRecordValue` (17, all 27),
  `new CommittedBoundarySnapshot` (2, all 34) and `new DeliveryKey` (5, all 3) call site in the repository — no
  mismatch. A first brace-blind version of that audit reported two false 29/31-argument sites; they were commas
  inside `new byte[] { … }` initialisers, and the brace-aware recount shows 27/27, which is why the audit script
  tracks `{` as well as `(`;
* running `tools/unity/prepare_gc017_release_project.py` for real (it is pure local file work), then asserting the
  clone contains zero `Gc021` references, that both `replace_once`-edited files still have balanced braces, that the
  qualification marker and the `Tests/` tree are gone, and that the integration package is still present because it
  is production; the disposable clone was then deleted.

None of that is a build, an import, a test or a player run.

## 10. Exact commands for the Linux build host

Everything runs from the repository root. Nothing below has been run.

### 10.1 The whole pure half

```sh
dotnet build dotnet/GameCore.sln -c Release
dotnet test  dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gc-021/trx
```

The new cases live in `GameCore.Execution.Tests` (`GameCore.Execution.Tests.Delivery.DeliveryKeyTests`,
`DurableOutboxTests`, `DeliveryJournalTests`, `DurableDeliveryAdapterTests`; 24 cases) and, for the checkpoint half,
in `GameCore.Execution.Tests.CheckpointCaptureTests` / `CheckpointRestorePlanTests` and
`GameCore.Contracts.Tests` (`CheckpointDocumentTests`, `CheckpointRecordTests`, `CheckpointTestCodecs`). The delivery
core has no Unity reference, so the whole task-defining crash-point suite runs here in under a second.

```sh
dotnet test dotnet/tests/GameCore.Execution.Tests/GameCore.Execution.Tests.csproj -c Release \
  --filter "FullyQualifiedName~Delivery" --logger trx --results-directory artifacts/gc-021/trx
```

### 10.2 Unity: catalog, EditMode, and the delivery suite

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
"$UNITY" -batchmode -nographics -quit -projectPath unity/GameCore.Validation \
  -executeMethod GameCore.Validation.Editor.CheckpointCatalogGenerator.GenerateCatalog \
  -logFile artifacts/gc-021/unity/checkpoint-codegen.log
git diff --exit-code -- unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCheckpoint/CheckpointCatalog.g.cs

"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode \
  -testFilter GameCore.Unity.Delivery.Tests \
  -testResults "$PWD/artifacts/gc-021/unity/delivery-editmode.xml" \
  -logFile artifacts/gc-021/unity/delivery-editmode.log
```

Do not add `-quit` to a `-runTests` command (`04` §10). `-testResults` with a relative path resolves against the
Unity **project** path, not the shell cwd, so an absolute path is passed above.

The catalog regeneration is authoritative here: the committed `CheckpointCatalog.g.cs` must be byte-identical after a
fresh generation, which is what proves this host's emitter mirror agreed with the real `CatalogEmitter`
(`dotnet/tests/GameCore.Content.Compiler.Tests/CheckpointCatalogTests` re-runs the real compiler and compares).

### 10.3 The GC-021 player probe

```sh
UNITY="$UNITY" ARTIFACTS=artifacts/gc-021/toolchain tools/unity/build_probe.sh
PROBE_RUNS=5 ARTIFACTS=artifacts/gc-021/toolchain tools/unity/run_gc021_probe.sh
```

The harness asserts `"task": "GC-021"`, `"mode": "Gc021"`, `"result": "Pass"`, the full per-family observation-name
table for both catalogs plus the `fixture:`-prefixed runs, the two digest steps and the task's clause fragments. Once
the sequence is green, pin the two literals by re-running with
`GC021_DIGEST_NARRATIVE=<value> GC021_DIGEST_CARDS=<value>` and then putting those values into `ProbeGc021`'s two
constants (the harness compares automatically once they are non-empty, so a wrong pin fails rather than passing).

### 10.4 The release surface

```sh
python3 tools/check_release_fault_free.py            # the compiled-assembly half needs DOTNET
python3 tools/unity/prepare_gc017_release_project.py # then build the clone with build_probe.sh
```

### 10.5 Documentation validation

```sh
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
```

## 11. Contract changes

Per the brief, shared public contract changes need this section. All are **additions**; no existing member changed
signature or meaning except the one recorded in §5.2, which was a defect in this branch's own change and is fixed
rather than released.

1. `CheckpointRecordKind.Outbox = 12`; `CheckpointFormat.RecordKindCount` `12 → 13`. Appended, never inserted, so every
   existing field id is unchanged. A reader that does not declare the kind refuses the document with
   `UnsupportedVersion` (existing behaviour, no new code).
2. `OutboxRecordValue` (27 fields), `OutboxRowKind`, `OutboxDeliveryState`, `OutboxDurability` — new types in
   `GameCore.Contracts`.
3. `HeaderRecordValue.OutboxCount` (appended field, field id 40) and `HeaderRecordValue.CountsMatch(..., int outbox)`;
   `CheckpointCounts.Outbox` and its appended constructor parameter. The count is checked in both directions, exactly
   like every other kind.
4. `CommittedBoundarySnapshot.Outbox`, `CaptureContext.OutboxRows`, `RestorePlan.Outbox`, `RestoreRefusal.InvalidOutbox`, `RestoreOutcome.RestoredOutboxRows`,
   `RestoreRefusal.InvalidOutbox`, `RestoreOutcome.RestoredOutboxRows`,
   `IRestoreOutboxBuilder`, and `CheckpointSerializerBindings`'s 13th binding — all additive.
5. `CheckpointCatalog.catalog.json` gains one schema and one header field, and the committed generated catalog moves
   accordingly. `CheckpointCatalogTests.DeclaredSchemaCount` `12 → 13`.

No change was needed to `GameCore.Contracts`' operation catalogue, to any plan DTO's existing fields, or to any
`O-xx` procedure. **`O-22 RecoverWorld` is untouched.**

## 12. Inventory: proposals only

`artifacts/gates/w4-generic-profile/inventory.json` gains a `gc021Revisions` section that **promotes nothing**. Every
proposal below names the observation that would carry it and the artifacts that must first exist; the build host
promotes after running, as the brief requires.

| Id | Current | Proposed | What this change set would add | Evidence that must first exist |
|---|---|---|---|---|
| `P-045` | Partial | **Partial** (promotion is the build host's call, and it is the row GC-021 owns) | A durable outbox with explicit external idempotency keys, persist-then-apply ordering, acknowledgement cursors and a checkpoint section; the at-least-once boundary is implemented and stated rather than papered over. The row's remaining gap would narrow to retention under memory pressure (GC-023) and recovery composition (GC-027). | `artifacts/gc-021/trx/` (the 24 delivery cases), `artifacts/gc-021/toolchain/probe-gc021.json` ×5, `artifacts/gc-021/unity/delivery-editmode.xml`, `artifacts/gc-021/unity/*-editmode.xml` |
| `P-053` | Implemented+Evidenced | unchanged | The outbox/dedup cursor clause now has a record kind, a capture path, a planner refusal and a reinstate seam, where before it had no representation. | the same probe/EditMode artifacts, plus `APlanCarriesTheOutboxRowsTheDocumentDeclares` in the TRX |
| `P-003` | Partial | unchanged | Acknowledging a delivery is idempotent and terminal rejections/compensations are irreversible, with no universal `Effect` API: the probe asserts `reverseMembers=0` over the delivery surface. | `probe-gc021.json` (`reverseMembers=0`) |
| `P-043` | Implemented+Evidenced | unchanged | Capacity and terminal retention are bounded and their refusals and prunes are counted, never silent. | `probe-gc021.json` (`atCapacity=True`, `capacityCode=BudgetExceeded`) |
| `P-049` | Partial | unchanged | A committed obligation is reinstated into a new session and recovered explicitly, with no hidden replay. `O-22` and bounded retries remain GC-027's. | `probe-gc021.json` (`sessionsDiffer=True`) |
| `P-050` | Partial | unchanged | The obligation identity is derived from the committed event and one attempt reuses one operation identity, so a redelivery after acknowledgement loss applies the destination mutation once. | `probe-gc021.json` (`redeliveredMutations=1`) |
| `P-054` | Implemented+Evidenced | unchanged | The outbox row is versioned and frame-checked; unknown versions/field sets/kinds are refused. | the TRX (`ARowFrameWithATamperedByteOrAnotherVersionIsRefusedRatherThanDecoded`, `ARestoreRefusesARowFromAnUnknownRecordVersionOrOfAnUnknownKind`) |
| `P-042`, `P-044` | Implemented+Evidenced | unchanged | The destination is reached through the ordinary typed command port and settled from the destination's own committed result. | `probe-gc021.json` |
| `TEST-015` | — | unchanged | Lifecycle stress remains GC-022's; this change set's teardown is the owner's own disposal. | — |

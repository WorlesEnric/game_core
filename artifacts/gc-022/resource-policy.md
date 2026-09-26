# GC-022 resource policy

**Status of every result in this document: `NotRun (pending orchestrator build host)`.** Nothing here was
measured on this host; the numbers named as bounds are read from the production declarations, and the growth
findings are read from the production code paths (each is cited by file and line) with the fixture that measures
them named. `artifacts/gc-022/HANDOFF.md` records which of them the build host must confirm or refute.

GC-022's definition of done is: *"Unload/failure suite passes in Editor worlds and the standalone target; resource
policy explains any bounded caches/quarantine explicitly."* This document is that explanation. It exists because a
lifecycle stress run that only asserts "the live counters are zero" would silently accept a structure that grows
without limit; the acceptance criterion is therefore split into two explicit statements, and every retained
structure this task observed is named in one of them.

## 1. The three terminal states of one resource

| State | Meaning | Counted as | Released by |
|---|---|---|---|
| **Retired** | The lease was disposed exactly once and left every retained state (`ResourceRetirementState.Retired`). | `ResourceLedger.RetiredCount`, `ResourceLedger.DisposeCount` on the factory | nothing further; it is terminal |
| **Quarantined** | Unfinished work (a tracked job/reader), a failed release, or an exhausted registry kept the reference. It stays on the books and is reported. | `QuarantineRegistry.Count` / `.Bytes` / `.AdmittedCount`, `ResourceLedger.QuarantinedCount`, `WorldResourceLedger.QuarantinedBytes` | an explicit release after the user ended: `TeardownSequencer.ReleaseQuarantineFor` / `LifecycleController.ReleaseQuarantine`; never elapsed time (P-048) |
| **Bounded cache** | A structure that deliberately retains derived data across operations. | its own capacity counter | its declared bound, and its declared eviction rule |

Elapsed time appears in none of the three. `TeardownSequencer` has no clock dependency at all, which is how
"elapsed timeout only reports `TeardownBlocked`, never authorizes free" (P-048) is true by construction rather
than by discipline.

## 2. Retained structures, each with its bound

### 2.1 Declared and bounded — no action required

| Structure | Bound | Release/eviction rule | Evidence |
|---|---|---|---|
| `QuarantineRegistry` (composition) | `LifecycleSettings.QuarantineCapacity` entries, default **4096**; byte ceiling `QuarantineByteCapacity`, default 64 MiB | explicit `Release(resourceId)` after every user ended; at the ceiling admission is **refused** with `TeardownBlocked` and the existing references stay (06 §6) | `InstallationLifecycleCoordinator.cs:233`; asserted by `QuarantineExhaustionRefusesAdmissionInsteadOfDroppingReferences` |
| Composition quarantine **byte** bound | reachable only through `QuarantineRegistry.Admit(..., bytes, ...)` | — | **not exercisable through the composition ledger today**: `ResourceLedger.Acquire` has no byte parameter and records `Bytes = 0` (`ManagedResources.cs:283-297`), so every composition-side entry contributes 0 bytes. The *Unity* ledger does record bytes (`WorldResourceLedger.Acquire(..., ulong bytes)`), which is where the byte ceiling is meaningful. Recorded as a doc/reporting gap, not a leak |
| Pinned step images (observation) | `ObservationRetention.Default` = **32** images / 256 events / 16 concurrent leases | lease release; a pinned image is never evicted, and a trim that cannot reach its window reports `PinnedRetentionStallCount` instead of dropping leased memory | GC-016, `Packages/com.gamecore.unity.runtime/Runtime/Observation/ObservationRetention.cs` |
| Delayed-consumer identity window | bounded per consumer by retention | an identity is released together with its dropped event | GC-016, `Runtime/Observation/DelayedConsumerDelivery.cs` |
| Adapter asset leases | outstanding-load ceiling of the adapter frame | lease release at completion; a completion for a retired table installs and releases nothing of another caller's | GC-019 |
| PlayerLoop routes | exactly **one** pump node plus one `Application.quitting` hook, identified by the `GameCorePumpLoop` marker type | `GameCorePlayerLoopInstaller.Remove()`; `EnsureInstalled()` removes a node a previous session left | `GameCorePlayerLoopInstaller.cs:57-140`. Not a cache; a route count. GC-022's Play Mode matrix and probe assert it does not accumulate |
| Event/redelivery ledger (messages) | bounded by `CommittedEventStore` retention | an evicted identity is released with its event | GC-016 `PublicationBoundary.cs`, `CommittedEventStore.cs` |

### 2.2 Retained by design but **not** bounded — reported growth

These are the two structures GC-022's 1,000-cycle run makes visible. Both are *live-count clean*: nothing is still
reachable and nothing is still pending, so P-048 and P-047 are satisfied. What grows is the retained **history**,
and neither has an eviction path in the current revision.

| Structure | Growth observed | Why it is not a bound | Fixture that measures it |
|---|---|---|---|
| `ResourceLedger` record tables (`records`, `acquisitionOrder`, `leases`) | one row per acquisition ever made, forever: `Records().Count == 5 × cycles` and the `leases` dictionary also holds the `ManagedResourceLease` and its disposer delegate | `ResourceLedger` has no method that removes a record; `Retire` only changes `State`. A long-lived world with mount/unmount churn grows linearly | `RetiredLedgerRecordsAreRetainedAndTheRetainedBoundIsReported`, and the 1,000-cycle test's `Records().Count` assertion |
| `JobFenceRegistry` job table (`jobs`, `canonicalOrder`) | one row per job ever tracked, retained after completion until `Release(jobId, out resourceIds)` is called — and **`Release` has no production call site** (verified by searching `Packages/` and `unity/` for `.Release(`; the only matches are `QuarantineRegistry.Release`, `InertAcquisitions`/gate release, `scratch.Release`, the asset backend and the image store) | nothing removes the record automatically; `Complete` only flips `Completed`, and `IsResourceFenced`/`OutstandingResourceIds` then scan every retained row | `CompletedJobRecordsAccumulateUntilExplicitlyReleased` |

Consequences worth stating plainly, because they are the reason this is a finding rather than a footnote:

* the per-pass cost of `JobFenceRegistry.OutstandingResourcesFor`, `IsResourceFenced` and
  `JobFenceRegistry.OutstandingCount` is a scan over every job the ledger ever saw, not over the outstanding set,
  so a world that schedules jobs for hours pays an increasingly long teardown scan;
* `ResourceLedger.LiveLeaseCount` and `RetainedResourceIds()` likewise scan the whole acquisition history on every
  call, so those counters stop being cheap observables;
* a job's record also holds its `IReadOnlyList<Id128>` resource ids, so the retained memory is per job times its
  resource list.

**Proposed minimal fixes** (not applied here — see HANDOFF §Contract changes: no production file was changed by
GC-022 because nothing could be reproduced on this host):

1. `JobFenceRegistry`: give `Complete` the same treatment `Release` already has — remove the record when the job
   completed *and* none of its resources is still retained — or add `ReleaseCompleted()` and call it from
   `TeardownSequencer.Unload` immediately after step 5, where the ledger already knows the job's resources are
   settled. `Release` already returns exactly the resource ids a caller needs, so no new contract is required.
2. `ResourceLedger`: add `int ReleaseRetired(PluginInstanceId instance)` that drops the `records`,
   `acquisitionOrder` and `leases` entries of one installation whose every record is `Retired`, and call it from
   the same place; or bound the history with a declared capacity and drop oldest-retired-first, reporting the
   eviction count. The second shape is preferable because it also bounds a world that mounts one identity forever,
   but both need a decision from the owner of GC-004/GC-014 before they are applied.

Until one of those lands, the honest statement is: **the live counts return to baseline; the history tables grow
with the number of acquisitions, and this is an unbounded retained structure**, which is exactly what TEST-023's
"bounded memory" acceptance criterion is meant to catch.

### 2.3 Quarantine that is declared and bounded, but permanent

A lease whose disposer threw cannot be released by any later attempt:
`ManagedResourceLease.Dispose` records the attempt *before* running the disposer and refuses a repeat
(`ManagedResources.cs:141-164`: `disposeAttempted = true;` then `onDispose(LeaseId)`), and
`TeardownSequencer.ReleaseQuarantineFor` releases a reference only through `ResourceLedger.Retire`
(`TeardownSequencer.cs:352-381`), which calls that same `Dispose`. So:

* the reference is **never dropped** and never reported as disposed (P-048 satisfied);
* the *bounded* `QuarantineRegistry` is what keeps this from being unbounded: at the ceiling, further admission is
  refused with `TeardownBlocked` and the host must refuse acquisition or stop (06 §6);
* clearing the failing script does **not** make the lease releasable — the recorded attempt is the barrier.

This is a deliberate reading of "dispose each lease at most once", and GC-022 asserts it as such rather than
assuming a retry the protocol does not promise (`AFailedReleaseIsNeverRetriedAndItsQuarantineStaysBounded`). If the
intent is that a *transient* disposer failure may be retried under P-049's bounded attempts, then
`ManagedResourceLease` needs an explicit `RetryRelease()` and the quarantine registry needs
`abandoned-reference` accounting; that is a protocol-level question for the owner of P-048, recorded in HANDOFF as
an open item rather than decided here.

## 3. What a bounded cache must state to be accepted

For every retained structure, the record must name: the **capacity** (entries and, where meaningful, bytes), the
**release or eviction rule**, and the **counter** an observer reads to see it. A structure that retains data
without a capacity is not a cache; it is a leak. `artifacts/gc-022/leak/policy.md` applies the same rule to native
allocations observed at shutdown, where each allocation must either be driven to zero (a GameCore-owned allocation
is always a defect) or listed with its frame signature and its bound.

## 4. Non-goals restated

GC-022 does not add, and must not be read as authorizing:

* forced cancellation of a running job (cancellation is cooperative intent; a timeout never proves a job stopped);
* resource reclamation justified only by elapsed time;
* a new global "unload hook" that frees everything at once. Teardown order stays a property of the ledger's data
  (reverse dependency order, reverse acquisition order within an installation), which is what makes it inspectable.

## 5. Where each claim is exercised

| Claim | Fixture |
|---|---|
| Every acquisition traced to retirement or quarantine, by role and kind | `EveryAcquisitionOfACycleIsTracedToRetirementOrQuarantineByKind` |
| All live counters back to baseline across 1,000 cycles | `AThousandMountUnmountCyclesReturnEveryLiveCounterToBaseline` |
| 100 delayed completions discarded; no resource released by them | `AHundredDelayedCompletionsCannotWriteAuthorityAfterRetirement` |
| A foreign incarnation's completion is discarded | `ACompletionStampedByAnotherWorldIncarnationIsDiscarded` |
| A stalled job retains exactly its reachable buffer; the rest are released; the release is explicit | `AStalledJobRetainsItsReachableBuffersUntilItCompletes` |
| A throwing disposer: reported, retained, independent cleanup continues | `AThrowingDisposerIsQuarantinedWhileIndependentCleanupContinues` |
| A failed release is never retried; the registry bound is the containment | `AFailedReleaseIsNeverRetriedAndItsQuarantineStaysBounded` |
| Required-provider churn: wait and resume on every cycle, consumer never removed | `RequiredProviderChurnWaitsAndResumesTheConsumerOnEveryCycle` |
| Exhaustion refuses admission instead of dropping references | `QuarantineExhaustionRefusesAdmissionInsteadOfDroppingReferences` |
| Job history grows until an explicit release | `CompletedJobRecordsAccumulateUntilExplicitlyReleased` |
| Ledger history grows; retained bound reported | `RetiredLedgerRecordsAreRetainedAndTheRetainedBoundIsReported` |
| A zero-acquisition cycle is a settled empty pass; a repeat releases nothing | `AnUnloadOfACycleThatNeverRanRetainsNothing` |

The Unity-world and player halves of the same claims are `GameCore.LifecycleStress.Tests` (both families) and the
`-probeLifecycleStress` player mode; see HANDOFF for their exact commands and for the cycle count the build host
should use (`GC_LIFECYCLE_STRESS_CYCLES`, default 1000).

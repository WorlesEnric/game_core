# GC-022 resource policy

**Status: measured on the Linux build host.** The full gate passed at 1,000 cycles; the pure 1,000-cycle test observed 5,000 disposals, zero live leases, 1,024 retained retired records and 3,976 evictions. The completed-fence regression observed zero tracked jobs after each of 50 unloads. See `BUILD_REPORT.md` and the committed TRX/XML evidence.

GC-022's definition of done is: *"Unload/failure suite passes in Editor worlds and the standalone target; resource
policy explains any bounded caches/quarantine explicitly."* This document is that explanation. It exists because a
lifecycle stress run that only asserts "the live counters are zero" would silently accept a structure that grows
without limit; the acceptance criterion is therefore split into two explicit statements, and every retained
structure this task observed is named in one of them.

## 1. The three terminal states of one resource

| State | Meaning | Counted as | Released by |
|---|---|---|---|
| **Retired** | The lease was disposed exactly once and left every retained state (`ResourceRetirementState.Retired`). | `ResourceLedger.RetiredCount`, factory `DisposeCount`, `EvictedRetiredCount` | no further disposal; diagnostic record evicts at its declared history bound |
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

### 2.2 Bounded retired history and completed job fences

| Structure | Bound | Release/eviction rule | Evidence |
|---|---|---|---|
| `ResourceLedger` retired history | 1,024 retired records, plus all currently retained/quarantined resources | On successful retirement, drop the lease and disposer delegate immediately; retain a diagnostic record until the next oldest-retired-first eviction. Only retired records evict. `RetiredCount` remains cumulative; `EvictedRetiredCount` measures dropped history. | The 1,000-cycle test observed 5,000 retirements, 1,024 retained records, 3,976 evictions and zero live leases; `RetiredLedgerRecordsAreRetainedAndTheRetainedBoundIsReported` crosses the bound with 1,500 acquisitions. |
| `JobFenceRegistry` completed jobs | No completed job records remain after its installation's safe retirement or explicit release | `TeardownSequencer.Unload` and `ReleaseQuarantineFor` call `ReleaseFencesFor` after resources settle; unfinished jobs and records still fenced by another unfinished job remain. `ReleasedCount` and `AutoReleasedCount` measure releases. | `CompletedJobRecordsReturnToBaselineAfterUnload` asserts zero records after each unload, and cumulative 50 automatic releases; the stalled-job test proves reachable buffers remain held until completion and explicit release. |

These repairs replace the prior unbounded histories. A still-live/quarantined reference cannot be evicted to satisfy a numerical bound: quarantine admission remains subject to §2.1's 4,096-entry ceiling and refusal policy. The pure 1,000-cycle run's factory retains references for its own per-lease assertions; production `ResourceLedger` no longer retains disposed delegates.

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
| Completed job records return to baseline after safe unload | `CompletedJobRecordsReturnToBaselineAfterUnload` |
| Retired ledger history plateaus at 1,024 records | `RetiredLedgerRecordsAreRetainedAndTheRetainedBoundIsReported` |
| A zero-acquisition cycle is a settled empty pass; a repeat releases nothing | `AnUnloadOfACycleThatNeverRanRetainsNothing` |

The Unity-world and player halves of the same claims are `GameCore.LifecycleStress.Tests` (both families) and the
`-probeLifecycleStress` player mode; see HANDOFF for their exact commands and for the cycle count the build host
should use (`GC_LIFECYCLE_STRESS_CYCLES`, default 1000).

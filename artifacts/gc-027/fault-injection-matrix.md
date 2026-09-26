# GC-027 fault-injection matrix — where each boundary is reached, and where it is asserted

**Executed:** qualification EditMode 1,144/1,144, PlayMode 53/53; IL2CPP `-probeRecovery` five clean runs with all 3 family digests and all 17 applicable observations per family Pass. Run 1: `artifacts/gc-027/toolchain/probe-gc027.json`; runs 2–5 are retained. Traversal has no delivery endpoint, so points 6–8 are not applicable there.

Every boundary below is reached through **the mechanism that already exists** — GC-017's latch for production-owned
boundaries, GC-021's delivery hook for the delivery boundaries — so this task adds no second injection mechanism
(00 §9's rule for operations applies to faults too). The latch is compiled out of a release build by
`GAMECORE_FAULT_INJECTION`, which reaches a compilation only through the asmdef `versionDefines` entry on
`com.gamecore.fault-qualification`; `tools/check_release_fault_free.py` asserts that both at source level and (with a
`dotnet`) at compiled level, and `tools/unity/prepare_gc017_release_project.py` strips the whole qualification set
from the release clone.

| # | Point | Mechanism | Reached at | Armed by | Observation asserting it |
|---|---|---|---|---|---|
| 1 | capture copy | Latch `FaultBoundary.CheckpointCaptureCopy` | `CheckpointPublication.CaptureAndPublish`, **before** `CheckpointCapture.Capture` reads anything | `Gc027Scenario.CaptureCopyFault` | `gc027-capture-copy-fault-produces-no-checkpoint` |
| 2 | file publication | Latch `FaultBoundary.CheckpointPublication` | `CheckpointPublication.CaptureAndPublish`, after the document exists and **before** `ICheckpointStore.TryPublish` | `Gc027Scenario.PublicationFault` | `gc027-publication-fault-keeps-the-previous-document` |
| 3 | restore reference repair | Latch `FaultBoundary.RestoreReferenceRepair` | `WorldRecovery.Recover`, after the session/operation reservation and **before** the executor runs | `Gc027Scenario.ReferenceRepairFault` (armed on the **source** world's latch) | `gc027-reference-repair-fault-never-builds-a-destination` |
| 4 | postwrite apply | Latch `FaultBoundary.RestoreApply` | `CheckpointRestoreExecutor.Restore`, after `IRestoreTargetBuilder.TryBuild` returned the staged world and **before** the outbox/validate/expose steps | `Gc027RestoreBuilder` (`armBoundaryName: "restore-apply"`) on the **staging** world's latch | `gc027-postwrite-apply-fault-never-exposes-a-destination` |
| 5 | publication of the recovered world | Latch `FaultBoundary.RecoveryPublication` | `CheckpointRestoreExecutor.Restore`, after validation and **immediately before** `UnityWorldRegistry.TryExpose` | `Gc027RestoreBuilder` (`armBoundaryName: "recovery-publication"`) | `gc027-recovery-publication-fault-keeps-the-registry-unchanged` |
| 6 | outbox append | `IDeliveryStepHook` at `DeliveryBoundaries.BeforeAppend` | inside `DurableDeliveryAdapter.TryCommit` → `TryPersist`, before the journal append | `Gc027DeliveryCrashHook(DeliveryBoundaries.BeforeAppend)` | `gc027-outbox-append-fault-refuses-before-delivery` |
| 7 | delivery | `IDeliveryStepHook` at `DeliveryBoundaries.AfterDelivery` | inside `DurableDeliveryAdapter.TryDeliver`, **after** the destination port was asked and before the attempt is recorded | `Gc027DeliveryCrashHook(DeliveryBoundaries.AfterDelivery)` | `gc027-outbox-delivery-fault-redelivers-with-one-destination-effect` |
| 8 | acknowledgement | `IDeliveryStepHook` at `DeliveryBoundaries.BeforeAcknowledge` and `AfterAcknowledge` | inside the adapter's `Acknowledge`, on each side of the persisted acknowledgement | two hooks, one per boundary | `gc027-outbox-acknowledgement-fault-records-or-redelivers-once` |
| 9 | restart | `StoreRead` (a real refusal value, not a latch) | the store's own `TryRead`, both for an absent document and for an incompatible one | `Gc027FlakyStore` / a mismatched catalog fingerprint | `gc027-restart-from-the-store-recovers-without-in-process-state`, `gc027-restart-without-a-document-or-incompatible-content-exposes-nothing` |

Point 4 and point 5 are the two reaches of the table's single "postwrite apply" row; `RecoveryFaultPoints` declares
them as two boundary names on one point because they share one permitted observable result (a destination that never
became the running world).

## What every row must show, regardless of which point it is

For each injection point the observation asserts **the permitted observable result**, **that the failed old world
never resumes**, and **that no external effect was replayed**:

| Requirement | Where it is asserted for every point |
|---|---|
| the permitted result | the point's `statement` is in the observation detail as `permitted result=…`; the specific fields (document absent, previous document intact, destination `<none>`, registry count unchanged, journal frames 0, effects 1, …) are numeric |
| the old world never resumes | `WorldRecoveryReport.SourceNeverResumed` (source began faulted, is not running/paused after, fault count unchanged) on points 3–5; for points 1–2 the captured world's `Lifecycle == Running` and `FaultCount == 0`, because a capture fault is a pre-mutation failure |
| no hidden external replay | the destination port's `AttemptCount` is 0 after every recovery, and the single-effect check on the delivery/acknowledgement points (`AppliedCount == 1`, `EffectIsSingle`) |

## Falsifiability

Each row can fail in a way that is visible rather than silent:

* a latch that never fires leaves `FaultPointId` empty, so the observation's `faultPoint=` field is `<none>`;
* a latch that fires at the wrong place leaves the destination exposed (`DestinationHost != null`) or the registry
  count moved, both of which are asserted directly;
* a delivery fault that fires at the wrong side of a boundary changes the journal's frame count or the destination's
  attempt count, both asserted;
* a store that refuses for the wrong reason changes the diagnostic code, asserted per row.

## Three families

The matrix runs on all three families. The narrative slice and the card market declare a delivery obligation and no
engine domain; the **traversal course** declares an engine domain and no delivery obligation, so it covers points 1–5
and 9 with its own observations plus four engine-physics ones, and records no delivery observation because it has no
delivery obligation to inject against (P-003, P-045). The two points the review named for the course —
postwrite-apply (points 4 and 5) and restart (point 9) — are the shared observations, asserted by name for the
`traversal/` label in `Gc027IntegrationTests.TheTraversalCourseCoversThePostwriteAndRestartFaultPoints`.

| Observation | narrative | cards | traversal |
|---|---|---|---|
| points 1–3, 9 (capture copy, publication, reference repair, restart) | yes | yes | yes |
| points 4–5 (postwrite apply, publication of the recovered world) | yes | yes | yes |
| points 6–8 (outbox append, delivery, acknowledgement) | yes | yes | **not applicable** — no delivery obligation |
| engine-physics observation (`…is-reseeded-not-continued`, `…steps-its-engine-once-per-admitted-step`) | no | no | yes |
| authoritative-state observation (`…authoritative-state-survives-the-recovery`) | no | no | yes |
| old-session observation (`…refuses-an-old-session-observation`) | no | no | yes |

## Build-host fill-in

The observation `detail` strings in `artifacts/gc-027/toolchain/probe-gc027.json` are the evidence. Copy, per family
(`narrative/…` and `cards/…`):

| Column to fill | Field in the detail |
|---|---|
| fault point that fired | `faultPoint=` / `boundary=` |
| code | `code=` |
| destination exposure | `destination=` (`never built` / `never exposed` / `exposed`) |
| registry movement | `registry=<before>-><after>` |
| source state | `source=<before>-><after>` |
| permitted result | the trailing `permitted result=…` clause |
| data-loss class | `loss=` from the transcript clause |

## Executed boundary census (qualification run 1; identical verdicts in runs 2–5)

| Point | narrative | cards | traversal | Result/data-loss boundary |
|---|---|---|---|---|
| Capture copy | Pass, `ApplyFault`, no document, source `Running` | Pass | Pass | `None` |
| File publication | Pass, previous document hash/read intact | Pass | Pass | `None` |
| Reference repair | Pass, `ApplyFault`, builder attempts 0, source `Faulted->Faulted` | Pass | Pass | `None` |
| Restore apply | Pass, staged world `Disposed`, registry unchanged | Pass | Pass | `UncommittedAttemptWork` |
| Recovery publication | Pass, validated staging world `Disposed`, registry unchanged | Pass | Pass | same postwrite point/class |
| Outbox append | Pass, journal frames 0, effects 0 | Pass | N/A | `UnpersistedObligation` |
| Outbox delivery | Pass, effects 1 before/after redelivery, `alreadyApplied=1` | Pass | N/A | `UncommittedAttemptWork` |
| Acknowledgement | Pass, before crash redeliverable; after crash settled | Pass | N/A | `None` |
| Restart | Pass, new session from verified bytes; absent/incompatible exposes nothing | Pass | Pass | `UncommittedSinceCheckpoint` |

The concrete per-family operation/session and `registry=before->after` values are in the probe JSON. A refused recovery never turns the faulted source into a running world; a capture fault is pre-mutation and correctly leaves its live source running. No recovery dispatches an outbox obligation automatically: recovered destination attempts are zero, then the explicit redelivery observation measures one applied effect.

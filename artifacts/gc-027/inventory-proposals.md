# GC-027 inventory proposals (for the build host to promote only after running)

**Nothing here moves a row.** `artifacts/gates/w4-generic-profile/inventory.{md,json}` is not edited by this change
set at all: a status may only be promoted from an archived passing run, and on this host nothing has run. What
follows is the proposal list the orchestrator promotes after the commands in `artifacts/gc-027/README.md` pass.

Normative surface this change set implements: **P-031, P-045, P-049, P-050, P-053, P-054**; operations **O-20, O-21,
O-22**; tests **TEST-002, TEST-010, TEST-014, TEST-016, TEST-017**.

## Proposed promotions

| Id | Current | Proposed | The observation that would carry it | Artifact that must exist first |
|---|---|---|---|---|
| `O-22 RecoverWorld` | `Not yet` | `Implemented+Evidenced` | `gc027-recovery-publishes-a-new-session-with-the-captured-state`, `gc027-postwrite-apply-fault-never-exposes-a-destination`, `gc027-restore-…`/`gc027-restart-…` in both families | `artifacts/gc-027/toolchain/probe-gc027.json` (5 runs, byte-identical) + `artifacts/gc-027/unity/editmode.xml` |
| `P-049` | `Partial` | `Implemented+Evidenced` | the eight fault points with their permitted results, `gc027-transient-failure-is-retried-under-the-host-bound` (the host-configured bound, exercised and exhausted), `gc027-teardown-disposes-every-world` | the same two artifacts |
| `P-031` | unchanged (`Partial`) | unchanged | the postwrite fault reaches `Faulted`/`ApplyFault` with no epoch and no image, and a recovery from that world is what `gc027-recovery-publication-fault-keeps-the-registry-unchanged` proves | unchanged; GC-017's own row already carries the fault half |
| `P-045` | unchanged (`Partial`) | unchanged | the three delivery boundaries plus `gc027-outbox-rows-and-delivery-cursor-survive-the-recovery` and the single-effect check | unchanged; retention/dedup **scale** remains GC-021's and GC-026's row (one-world scale here) |
| `P-050` | unchanged (`Partial`) | unchanged | `RecoveryAttemptLog.AttemptsAreDistinct()` is asserted on real attempt records: a retry uses a new session and a new operation id | unchanged; the small-width counter-exhaustion fixture TEST-002 asks for is still absent |
| `P-053` | `Implemented+Evidenced` | unchanged | the checkpoint this recovery reads is captured by GC-018's own path; this change set adds the file/publication adapter 06 §7 requires, asserted by `gc027-publication-fault-keeps-the-previous-document` | unchanged |
| `P-054` | `Implemented+Evidenced` | unchanged | the store envelope's version gate (`UnsupportedVersion` for another major or minor) and the incompatible-catalog refusal | unchanged |

## Explicit non-proposals, with the reason

| Id | Why not |
|---|---|
| `P-004`, `P-005` | the recovery proves fresh incarnation and distinct native handle blocks, which improves both rows, but TEST-002's **small-width counter-exhaustion** boundary fixture is still absent, so neither row's stated acceptance is met |
| `P-032` | dormant serialization and dormancy preservation are exercised again here; GC-015's row also covers transfer/reset boundaries this task does not touch |
| `P-007` | the recovery reads under a retained observation lease (through GC-016, in the reader), but the long-run retention budget under memory pressure (TEST-023) remains open and is GC-026's |
| `P-002`, `P-034` | unchanged; the composition adds no new authority — `WorldRecovery` is a caller of the two existing procedures and never a second writer |
| `P-048` | the retained/quarantined outcomes are reported (`TeardownBlocked` on a blocked stop) but this task does not add a resource that can block |

## New capability rows this change set would add, if the inventory tracks them

| Proposed id | What it is | Where implemented | Observation |
|---|---|---|---|
| `O-20` publication half | the local checkpoint adapter 06 §7 asks for (temporary file, checksum, atomic replacement) that GC-018 deliberately left to the caller | `Packages/com.gamecore.unity.runtime/Runtime/Pure/Recovery/CheckpointStore.cs` (`FileCheckpointStore`, `MemoryCheckpointStore`, `CheckpointStoreFormat`) | `gc027-source-world-captures-and-publishes-a-verified-checkpoint`, `gc027-publication-fault-keeps-the-previous-document` |
| recovery fault table | the eight task boundaries as production data with their permitted results and data-loss classes | `RecoveryPolicy.cs` (`RecoveryFaultPoints`) | `Gc027IntegrationTests.TheDeclaredFaultPointsCoverTheEightTaskBoundaries` |
| host-configured bounded retry | P-049's `BoundedTransientAttempts`, consumed for the first time | `RecoveryPolicy.cs` (`RecoveryRetryPolicy`, `RecoveryFailureClassification`) | `gc027-transient-failure-is-retried-under-the-host-bound` |
| outbox consistency census | the report a crash/restart recovery is judged by, and the executor's pre-exposure proof | `OutboxConsistencyReport.cs`, `Gc027RestoreBuilder.TryProveOutbox` | `gc027-outbox-rows-and-delivery-cursor-survive-the-recovery` |
| recovery transcript | the ordered, digestible record of one recovery and the class of data it lost | `RecoveryTranscript.cs` | every recovery observation's `transcript(...)` clause |

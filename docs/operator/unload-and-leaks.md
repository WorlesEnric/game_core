# Unload, teardown and leak runbook

How to unload a plugin or stop a world safely, and how to tell a real leak from an engine cache that has not
reached its documented bound. Follows [06 §4–§6](../game-core/06-lifecycle-and-recovery.md) and
P-047/P-048/P-049.

**Status: `NotRun (pending orchestrator build host)`** except where an archived artifact is cited.

## 1. Order matters: consumers before providers, ingress first

Teardown prepares dependencies in topological order, records the lease acquisition sequence within each
instance, and **retires consumers before providers**. All affected ingress closes first.

`WorldResourceRecord` carries the two fields that encode this:

- `DependsOn` — the resource this one depends on, so retirement runs in reverse dependency order (P-048);
- `AcquisitionOrdinal` — leases dispose in reverse acquisition order (P-048).

If a teardown appears to be freeing a provider before its consumer, that is an ordering defect, not a race to
retry.

## 2. In-flight work, and the safe release point for each kind

| Work kind | Quiesce/unmount behaviour | Safe release point |
| --- | --- | --- |
| Already scheduled job | Let it complete; record all dependent handles. | After its final handle completes. |
| Current logical step | Commit normally or fault. | At the resulting boundary. |
| Queued old-route command | Cancel with `RouteRetired`, unless the port explicitly supports compatible stable-ID rebind. | After the result/ledger update; then the payload arena is released. |
| Prepared async asset load | Cancel cooperatively, then reject the stale completion. | Release the received lease when the completion arrives. |
| Managed subscription/callback | Close the activation gate **before** disconnecting. | Once callbacks settle; a failed disconnect stays inert/quarantined. |
| Read-only snapshot lease | Retain the immutable snapshot under the bounded pool. | The reader releases the lease; new reads backpressure if necessary. |
| Native container used by an adapter | Track the external reader fence alongside ECS jobs. | After all declared readers complete. |

**A timeout cannot prove that a job has stopped.** Cancellation changes the *desired* work, not native
lifetime. `TeardownBlocked` means "not yet", and the correct operator action is to wait — never to force a
free because N seconds elapsed. Forcing a buffer free under a live job is the one mistake that turns a
recoverable stall into memory corruption.

## 3. Unloading a plugin installation

1. `Unmount` (O-07): dependents wait or rebind, ownership dispositions apply, and **no other supports are
   deleted**. Removing one of two supports must leave the committed result of the other intact.
2. If a required state disposition is missing, the unmount is **rejected** while the old installation stays
   active. Fix the declaration; do not force the unmount.
3. Cleanup errors after a successful publication produce `PublishedWithCleanupErrors`: the new epoch is
   already authoritative and **cannot be cancelled back**.
4. A removal with pending durable work is refused until it drains — for example the reward installation's
   outbox lease rejects an unmount with pending work until the outbox is empty, then the completed outbox is
   preserved dormant or handed to a named owner.

### State policies and what the operator sees

| Change | Policy | Observable result |
| --- | --- | --- |
| Inherited limit changes | `Preserve` + replace config | Current hand/score remains; the next command validates the new limit. |
| Chapter bindings retract | `RemoveDerived` | Binding removed; previously committed facts retained. |
| Only the quest executor unmounts | `PreserveDormant` | Data persists in ECS/checkpoints, excluded from active query routes. |
| Owner package upgrade changes schema | `Migrate(old,new)` | Scratch conversion validates before replacing live storage. |
| Explicitly restart a match | `Reset` **with a proposal reason** | Fresh state initialized; prior external events remain historical facts. |
| Provider replacement transfers authority | Named `TransferTo` + migration | Old writer loses the grant and the new writer gains it at the same publication. |

Dormancy is visible in snapshots and diagnostics. A command targeting a dormant route is terminally rejected.
The framework does not silently preserve runnable data without an executor, and does not silently delete
durable progress to make an unmount convenient.

## 4. Reading the resource ledger

`IWorldHost.ReadResourceLedger()` returns a `WorldResourceLedgerSnapshot`:

| Member | Meaning |
| --- | --- |
| `World` | The world this snapshot describes. |
| `Epoch` | The assembly epoch the snapshot is taken against. |
| `Resources` | Every `WorldResourceRecord` this world owns. |
| `Jobs` | Every `JobLedgerRecord` (tracked job fences). |
| `QuarantinedBytes` | Bytes held by unfinished work that still reaches them. |
| `QuarantinedCount` | Number of quarantined entries. |

A `WorldResourceRecord` carries `ResourceId`, `Kind`, `Key`, `Owner`, `Instance`, `State`, `DependsOn`,
`AcquisitionOrdinal`, `Bytes`, plus computed `IsRetained` and `HasDependency`.

`WorldResourceKind` is one of: `ManagedLease`, `Subscription`, `SystemRegistration`, `NativeContainer`,
`ScheduledJob`, `ScratchAllocation`, `WorldStorage`, `IdentityIndex`.

`ResourceRetirementState` is one of:

| State | Meaning |
| --- | --- |
| `Acquired` | Taken, not yet ready. |
| `Ready` | In use. |
| `Retiring` | Release requested, not yet settled. |
| `Retired` | Released. |
| `Quarantined` | **Still reachable by unfinished work; retained, never freed on a timeout.** |
| `Failed` | Acquisition or release failed. |

A `JobLedgerRecord` carries `JobId`, `Stage`, `SystemKey`, `Epoch`, `Step`, `Completed`, and
`RetainedByQuarantine`.

## 5. Diagnosing a suspected leak

**An aggregate process memory number is never evidence.** 08 requires the categories to be reported
separately, because an engine cache that has not yet reached its documented bound is indistinguishable from a
leak in a single total.

Work through the categories in this order:

| Category | Where it is visible | If it grows without bound |
| --- | --- | --- |
| Live leases | Ledger `Resources` with state `Ready`/`Acquired` | A lease was taken and never released. Compare the acquisition ordinal against the teardown order. |
| Native containers | Ledger `Resources`, `Kind = NativeContainer` | An owner did not release, or a job fence never completed. |
| Quarantine | `QuarantinedBytes` / `QuarantinedCount` | Unfinished work still reaches these. **Expected during a stall; a defect if it never drains after the job completes.** |
| Outstanding callbacks | Live activations plus tracked jobs | A completion never arrived, or a token check failed to discard a late one. |
| Retained events | Committed events still retained | A reader has not advanced its cursor; snapshot retention is bounded, so this must plateau. |
| Retained caches | Derived caches | Must reach a documented bound and plateau. |
| Asset leases | `lease-bytes` and the assets adapter | Removing one user must release exactly one lease. |
| Managed heap | Process total | Observation only. A main-thread-only reading **cannot** prove worker allocation is zero. |

The correctness expectation for churn is: **active counts return to baseline and bounded
cache/native/managed growth plateaus**. A 1,000-cycle teardown must return the assembly and contribution
counts to their baseline — GC-022's stress mode asserts this, and GC-026's lifecycle workload recorded
`baselineAssemblies=10000; finalAssemblies=10000` in the short diagnostic.

## 6. Quarantine is bounded and observable

A published removal can leave a `Retiring` instance in a quarantine registry **without** leaving it in the
active installation graph. The registry is bounded: exhaustion **rejects further resource acquisition** or
faults/stops the world according to host policy. It never drops references to make room.

An instance becomes `Disposed` only when all its required resources are settled. If you see a world stuck in
`Stopping` with a non-zero quarantine, a fence is outstanding — find its `JobLedgerRecord`, not a bigger
timeout.

## 7. Shutdown does not race the engine

`Application.Quit` is deferred to the end of the frame. The application installs an idempotent quit hook
beside the single pump node so the loop route closes **before** the engine tears down worlds and native
storage. Without it, a pump node could step a world whose storage is being released. This is why the quit
hook exists and why it must not be removed.

The headless player also runs with Unity audio disabled (`m_DisableAudio: 1`), because an FMOD/PulseAudio
crash at exit was recorded as crash-139. The traversal genre's committed audio output goes to the engine-free
recording sink in that configuration; the Unity audio sink is never constructed there.

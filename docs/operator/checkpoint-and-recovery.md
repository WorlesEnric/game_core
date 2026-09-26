# Checkpoint, restore and recovery runbook

This is the operator procedure for durable state: capturing a checkpoint, restoring one into a new world, and
recovering a **faulted** world. It follows [06 §7](../game-core/06-lifecycle-and-recovery.md) and the
P-031/P-049/P-053/P-055 requirements.

**Status: `NotRun (pending orchestrator build host)`** unless an archived artifact is cited.

## 1. The one rule to internalize

> A faulted world is **never** resumed. Recovery creates a **new world**.

P-031 makes a post-write failure a fail-stop: live storage may already be inconsistent, so continuing to
execute it is unsafe. `WorldRecovery.Recover` therefore requires an explicit source and reuses the same
restore/create procedures as a normal restore. There is no partial rollback and no compensating gameplay
transaction.

## 2. Capture — O-20 `CaptureCheckpoint`

Captured **at a committed boundary**, after the required jobs complete. The API is
`CheckpointPublication.CaptureAndPublish` over a committed-boundary reader.

What a checkpoint contains (P-053):

- catalog and protocol fingerprints;
- world definition and temporal settings;
- logical step / time / retained debt;
- stable scope, installation and target ids, and descriptors;
- explicit imports, overrides and exclusions; the propagation mode; definitions;
- **active and dormant** authoritative state;
- owner versions, RNG streams, and bounded pending next-step messages;
- external outbox and deduplication cursors when used.

Two operator-visible behaviours worth knowing:

- **Command inclusion is explicit.** New host commands wait during capture; already-queued external commands
  are either included with ledger/cutoff or explicitly rejected before capture, according to the checkpoint
  option — never ambiguously omitted. The default sample adapter rejects unexecuted external commands with
  `CheckpointBoundary` while preserving declared authoritative next-step queues.
- **Raw host timestamps are normalized** to the declared clock policy, so a checkpoint is comparable across
  machines.

Serialization follows P-054: schema ids, integer versions, explicit field ids, canonical byte order, length
bounds, null semantics and reference tables. **No CLR assembly name or engine handle is ever an identity.**

The local file store writes a temporary file, checksums it, then atomically replaces the target. So a
`.checkpoint` file is either the previous good state or the new good state — never a torn write. A `.partial`
artifact left behind is a failed publication, and its presence is itself a defect to report.

## 3. Restore — O-21 `RestoreCheckpoint`

`CheckpointRestoreExecutor` builds a **new, unexposed** world and publishes only when everything validates:

1. validate catalog and schema versions, and the directed migration paths;
2. build the unexposed world with a **fresh `WorldId`**;
3. reconstruct scope/target stable ids and rederive capabilities;
4. allocate recipes and restore owner slots;
5. repair stable references (two-pass: identity mappings first, then patch and validate referential integrity);
6. restore clocks, RNG and outbox cursors;
7. publish, then expose.

Failure modes and what they mean:

| Condition | Result |
| --- | --- |
| Unsupported schema version, or an ambiguous migration path | **Rejected.** The existing world is unaffected. Migration paths must be unique for a requested source/target pair. |
| Missing required target reference | **Rejected.** Optional references become their declared `None` state with diagnostics. |
| Authority or effective-capability mismatch | **Rejected.** Restoration never rewrites saved state behind the caller's back. |
| Cancelled before publication | The staged world is destroyed. |
| Duplicate request | Returns the same restored session. |

An old callback or handle cannot target a restored entity (`StaleHandle`): the new world has a different
session id and different native handles. A restore that reported success while leaving old handles resolvable
would be a P-004/P-005 defect.

**Physics is not part of the portable contract.** Engine-internal solver state is not checkpointed; an adapter
restores declared authoritative pose/velocity or observation policy and records any restabilization limits.

## 4. Recover — O-22 `RecoverWorld`

`WorldRecovery.Recover` takes a **Faulted** world plus a checkpoint or an initial definition, and produces:

- the **old** world stopped (never resumed), with blocked resources retained until it is safe to release them;
- a **new** session at a fresh identity.

Source rules, which are the ones operators trip over:

| Source state | Outcome |
| --- | --- |
| `Running` or `Paused` | **Refused** with `TooLate`. Use capture-then-recover, or stop the world first. |
| Already retired/disposed | **Refused** with `StaleHandle`. |
| `Faulted` | Accepted — this is the intended source. |

There is no API that returns a registered host to `Created`, which is why a recovery source is a world that
was retired into a terminal non-live state rather than a `TryCreate`-fresh world. `-probeRecoverySmoke`
documents this constraint in its own step detail (`sourceLifecycle=Faulted->Disposed`).

Failure handling:

- If recovery fails, the **old world stays Faulted** and the failure is reported as a new attempt failure.
  Recovery does not partially apply.
- No implicit effects are replayed. External effects rely on durable outbox/receiver deduplication to avoid
  double delivery.
- Cancellation is possible only before the new publication.

`WorldRecovery.Restart` is the store-only variant: it restarts from the checkpoint store alone, owing nothing
to in-process state. `-probeRecoverySmoke` exercises both against a real `FileCheckpointStore` with **no fault
latches**, in the marker-free release player — which is why that mode is kept in a shipping build.

## 5. Operator procedure

**Normal checkpoint:**
1. Confirm the world is at a committed boundary and no apply is in flight (`IWorldHost.Lifecycle`, and no
   `Applying` plan).
2. Capture with your chosen queued-command disposition.
3. Verify the published file checksum and that no `.partial` remains.
4. Archive the checkpoint with its catalog/protocol fingerprints — a checkpoint is only restorable against a
   compatible catalog.

**Recovery after a fault:**
1. Confirm the fault is post-write: the result carries `Faulted(ApplyFault)` (see
   [failure-codes.md](failure-codes.md)).
2. Do **not** attempt to resume, retry the mutated step, or patch storage in place.
3. Pick the source: the most recent checkpoint you trust, or the initial definition.
4. Run recovery and capture the new session id.
5. Re-establish external integrations against the **new** session; discard every handle, callback or cursor
   from the old one.
6. Inspect the resource ledger of the old world (see [unload-and-leaks.md](unload-and-leaks.md)) — resources
   blocked by unfinished work are retained, and quarantine is reported separately rather than silently freed.

## 6. What an operator can observe

- `IWorldHost.ReadResourceLedger()` → `WorldResourceLedgerSnapshot` with per-resource records and job records.
- The diagnostic on the operation result: a stable code, phase, operation id, plan hash, involved ids/keys,
  counts, the configured budget limit, and a retry classification.
- `IOperationReader.Read(OperationId)` for the terminal status of a specific attempt; `ResultExpired` once the
  bounded ledger reclaims it.

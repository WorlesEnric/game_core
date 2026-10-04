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
   [failure-codes.md](failure-codes.md)). A fault whose route in was `ProviderFailed` — an already-Active
   provider failed unexpectedly and its safe dependency-closure deactivation could not publish — is the same
   fail-stop; the two outcomes of that path and how to tell them apart are in
   [build-and-run.md §9](build-and-run.md#9-a-live-provider-failed-unexpectedly-p-012-safe-deactivation-vs-fault-on-refusal).
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

## 7. Production restore and saves

SADR-012 (studio) adds a production restore path for games and a user-facing save service. Sections 2–4
still apply; this section covers only what is new.

**Builder.** `ProductionRestoreTargetBuilder` (`com.gamecore.unity.runtime`, `Runtime/Persistence`) is the
O-21 `IRestoreTargetBuilder` for real games. Its composer (`SaveRestoreComposer` for an application root)
composes the staging world with the game's own catalog, schedule, recipes and lane. The builder then restores
targets, slots (dormant rows included), scopes and grants, installations, root boundaries, the propagation
mode, plugin clocks and wakes, RNG streams, next-step buffer rows, re-admitted commands and the outbox. A game
can also declare `SaveRestoreHook` as its `IGameApplicationRestoreHook`, so
`GameApplicationRoot.TryCreateRestoreTargetBuilder` returns this builder.

**Batched publication.** The captured scope tree becomes the lane seed, and targets and slots are written
directly, so neither costs a publication. Each installation, each scope with imports, each differing root
boundary and a differing mode costs one publication. The count does not depend on how many targets the
checkpoint holds. `ProductionRestoreReport.Describe()` prints:
- every count;
- the publications and no-change edits;
- the time spent in each phase.

**Restore refusals added by SADR-012:**

| Condition | Refusal | Code |
| --- | --- | --- |
| A target names a recipe this build does not register | `RecipeMissing` | `MissingDependency` |
| A recipe is registered at another revision; the detail names the recipe, both revisions and the target | `RecipeRevisionMismatch` | `StalePlan` |
| The checkpoint's temporal model differs from the game's | `TemporalModelMismatch` | `UnsupportedVersion` |
| A slot row needs a migration that is not registered | slot migration `MigrationPathMissing`; the hint names the schema and both versions | `MigrationRequired` |
| A slot row is newer than this build | slot migration `Downgrade` | `UnsupportedVersion` |

Each of these refusals leaves the running world untouched and disposes the staging world.

**Slot migrations.** A slot migration is a pure, id-keyed forward step (`SlotMigrationStep`) in a
`SlotMigrationRegistry`, bound to slots through a `SlotSchemaCatalog`. `SlotMigrationExecutor` runs before
planning:
- it rewrites only the slot records of the document;
- every other record is carried byte for byte;
- the O-21 plan therefore stays direct.

Ambiguous chains and steps that throw are refusals, never partial results.

**Temporal continuity.** A document whose container declares the feature
`gamecore.checkpoint.feature.temporal-continuity.v1` restores with its logical step, retained debt, domain
seconds and issuer high-water marks. Details:
- The restored world still gets a new `WorldId`.
- Restored debt is owed at the first running pump.
- `WorldAdapterFrame` continues a device source's sequence above its saved mark.
- `UnityWorldHost.RestoredOrigin` reports what was applied.

A document without the feature (every V1 writer) restores at step 0 with zero debt and zero domain time.
The origin reports `LegacyStepZero`, with a detail naming the feature. A reader that predates the feature
refuses such a document as an unknown required feature; it does not misread it.

**Save files.** `SaveService` (`com.gamecore.unity.app`) writes each slot as two files under
`Application.persistentDataPath/saves`:
- `<slot>.gcc`: the checkpoint document, published atomically by `FileCheckpointStore`;
- `<slot>.json`: a header with the game id, catalog fingerprint, schema versions, region, play time, UTC
  timestamp, thumbnail path, logical step, and the document's hash and length.

Slot names are lowercase file stems: `[a-z0-9][a-z0-9_-]{0,63}`.

The two files are not jointly atomic. If a crash lands between them, the header's hash no longer matches the
document, and the slot reads as `save.corrupt-file`. It is never restored half-old.

A capture pauses a running world for the boundary read, then resumes it.

A restore does the following:
1. checks the header against the document;
2. decides catalog compatibility: identical, declared compatible through `CompatibleCatalogs`, or refused;
3. runs the slot migrations and the O-21 restore;
4. only after success, stops the previous root and hands over the new one through `SaveService.ActiveRoot` and
   the `RootChanged` event.

| Save refusal | Meaning |
| --- | --- |
| `save.missing-slot` | No document or no header for the slot. |
| `save.corrupt-file` | The header or document does not parse, the checksum fails, or the two files are from different saves. |
| `save.catalog-mismatch` | Another game, an undeclared catalog fingerprint, a recipe revision change or a temporal model change. |
| `save.migration-path-missing` | A slot needs a migration this build does not register; the hint names it. |
| `save.newer-build` | The header format, document version or a slot row is newer than this build. |
| `save.unsafe-state` | The world is stopped, faulted, mid-step or not running/paused. |
| `save.invalid-slot`, `save.capture-failed`, `save.storage-failed`, `save.restore-refused` | As named; the kernel code and detail travel with the refusal. |

`SaveService.Inspect(slot)` reads and verifies a slot and previews its migration without restoring anything.
`SaveService.TestRoundTrip()` captures, restores into a scratch world, captures again and compares the
canonical slot hashes and steps, then stops the scratch world. Studio exposes both as `save.inspect` and
`save.testRoundTrip`.

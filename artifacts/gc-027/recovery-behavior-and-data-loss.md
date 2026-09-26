# GC-027 — recovery behavior and the data-loss boundary

Normative basis: `docs/game-core/00-core-protocols.md` O-20, O-21, O-22, P-030, P-031, P-045, P-048, P-049, P-053,
P-054, and `docs/game-core/06-lifecycle-and-recovery.md` §7.

**Status of every behavioral row below: `NotRun (pending orchestrator build host)`.** This host has no Unity, no .NET
SDK and no C# compiler, so nothing in this change set has been compiled, imported, executed or built here. §1 and §2
are the *design* the code implements and the observations that assert it; the "observed evidence" column names the
artifact the build host will produce, and it is filled in by running the commands in §6. Nothing in this document
claims a run has happened.

## 1. The recovery procedure this change set implements

`GameCore.Unity.Runtime.Recovery.WorldRecovery` composes O-22 over the two procedures that already existed:

```
Recover(context)                       # O-22, source = a faulted world
  1. observe the source       read its lifecycle, fault code and fault count; refuse a live source (TooLate)
  2. reserve                  a NEW session (P-050) and a NEW operation id, per attempt
  3. validate/rebuild         via CheckpointRestoreExecutor.Restore (O-21) or InitialDefinitionRecovery (GC-017)
                              into an UNEXPOSED world; nothing is reachable until the executor exposes it
  4. publish                  the recovered session becomes the registry's world, or is destroyed
  5. stop the source          the old world is stopped and NEVER resumed; a blocked stop is reported, not retried

Restart(context, previous)             # the same sequence with no source world at all (P-053's restart)
```

Two rules are the whole point of the contract, and both are asserted rather than described:

* **the failed old world never resumes** — a source that is `Running`/`Paused` is refused outright, the source is
  re-read after the attempt, and `WorldRecoveryReport.SourceNeverResumed` is true only when the source began
  faulted/stopping/disposed, is not running/paused afterwards, and its fault count did not move (P-031, P-049);
* **no hidden external replay** — reinstating an outbox row re-owes the obligation and never delivers it, so a
  recovery ends with `ObligationsOwed > 0` and the destination's attempt count at zero (P-045, P-049).

## 2. The eight injection points, their permitted result, and their data-loss class

The table is production data (`RecoveryFaultPoints.All`), asserted by
`Gc027IntegrationTests.TheDeclaredFaultPointsCoverTheEightTaskBoundaries` and by the player probe. "Mechanism" is how
the fault is injected: `Latch` = GC-017's `AssemblyFaultInjection` (compiled out of release by
`GAMECORE_FAULT_INJECTION`); `DeliveryHook` = GC-021's existing `IDeliveryStepHook` (a reporting seam, test-only
caller); `StoreRead` = the store returning a real refusal value.

The sequence runs over **three** families: the narrative slice, the card market and the traversal course. The
traversal course is the one that exercises a *fixed-step* world with an engine physical domain, so it is also where
the physical-observation limitation is executed rather than asserted (§3.1).

| # | Injection point | Mechanism | Boundary names | Permitted observable result | Data-loss class | Observed evidence (`NotRun`) |
|---|---|---|---|---|---|---|
| 1 | capture copy | Latch | `checkpoint-capture-copy` | no checkpoint produced; the world being captured keeps running | none | `gc027-capture-copy-fault-produces-no-checkpoint` |
| 2 | file publication | Latch | `checkpoint-publication` | the previously verified document is still the stored one; no partial artifact | none | `gc027-publication-fault-keeps-the-previous-document` |
| 3 | restore reference repair | Latch | `restore-reference-repair` | the destination was never built, so no incomplete world can become the running one; source unchanged | none | `gc027-reference-repair-fault-never-builds-a-destination` |
| 4 | postwrite apply | Latch | `restore-apply`, `recovery-publication` | the staged world is destroyed, never exposed; no epoch/revision/session becomes reachable | uncommitted attempt work | `gc027-postwrite-apply-fault-never-exposes-a-destination`, `gc027-recovery-publication-fault-keeps-the-registry-unchanged` |
| 5 | outbox append | DeliveryHook | `before-append`, `after-append` | an obligation is durable before it is handed over; a fault before the append refuses the commit | unpersisted obligation | `gc027-outbox-append-fault-refuses-before-delivery` |
| 6 | delivery | DeliveryHook | `before-delivery`, `after-delivery` | the obligation stays open and a redelivery reuses the **same** idempotency key, so the destination applies exactly one mutation | uncommitted attempt work | `gc027-outbox-delivery-fault-redelivers-with-one-destination-effect` |
| 7 | acknowledgement | DeliveryHook | `before-acknowledge`, `after-acknowledge` | a fault before it leaves the obligation redeliverable; after it, settled — with one destination effect either way | none | `gc027-outbox-acknowledgement-fault-records-or-redelivers-once` |
| 8 | restart | StoreRead | `store-read` | a new session from verified bytes, or **no world at all**; the previous session is never contacted | state committed after the last verified checkpoint | `gc027-restart-from-the-store-recovers-without-in-process-state`, `gc027-restart-without-a-document-or-incompatible-content-exposes-nothing` |

Point 4 names two latch boundaries because the restore sequence reaches the postwrite-apply condition twice: after the
staging world was written to (`restore-apply`, in `CheckpointRestoreExecutor` immediately after `IRestoreTargetBuilder`
returns) and as the validated world is about to be published (`recovery-publication`, immediately before
`UnityWorldRegistry.TryExpose`). Both reaches are `Latch` reaches on the **staging** world's own latch, and both must
produce the same observable result — a destination that never became the running world.

The other three latch points are reached where their owner is: `checkpoint-capture-copy` and
`checkpoint-publication` in `CheckpointPublication.CaptureAndPublish` — the publication half of O-20 that GC-018 left
to the caller, and the reason `FileCheckpointStore` removes its temporary artifact — and `restore-reference-repair` in
`WorldRecovery.Recover` *before any destination exists*, which is why its permitted result is the strongest one
available: there is no destination to expose, so TEST-016's last row holds by construction rather than by cleanup.

## 3. The data-loss boundary, stated as a class

The classes are ordered; a report exposes the worst class its transcript recorded
(`WorldRecoveryReport.DataLossBoundary`), so the boundary is read off executed lines instead of asserted in prose.

| Class | Meaning | Where it applies |
|---|---|---|
| `None` | the boundary is crossed before any committed state is at risk | points 1–3, 7 |
| `UncommittedAttemptWork` | the failed attempt's own work only; no committed state and no durable obligation lost | points 4, 6 |
| `UnpersistedObligation` | an obligation that was never made durable; a caller that required durability was refused | point 5 |
| `UncommittedSinceCheckpoint` | state committed after the last verified checkpoint; replayable only from a later checkpoint | point 8, and the initial-definition source |

Concretely, the boundary a caller must design around is:

* **checkpoint source**: everything the source world committed **after the captured checkpoint** is gone. The
  checkpoint carries active and dormant authoritative state, the composition (scopes, installs, selections, imports,
  exclusions, mode), clocks with their pending wakes, the RNG stream positions, the bounded next-step messages, the
  admission cutoff and the delivery outbox rows with their cursors (P-053) — and nothing else. There is no arbitrary
  historical rollback (a named non-goal of GC-027) and no bit-identical Unity physics continuation (P-054 excludes it).
* **initial-definition source**: everything the source session committed is gone; only the compiled catalog is
  carried. This is exactly GC-017's existing source, reused unchanged, and the transcript records the wider class so
  no report implies the narrower one.
* **delivery**: an obligation is either durable before hand-over (so a recovery re-owes it) or it was never accepted
  (`OutboxAdmission.JournalRefused` / `DurabilityUnavailable`). A recovery reinstates obligations and **never**
  delivers them, so the destination's observable effect across a recovery is exactly one mutation per obligation,
  made by the recovered world when it dispatches (P-045).
* **not lost**: the source's committed gameplay effects as observed at the checkpoint, the source's identity, and the
  obligation set. The source is never written to, and its storage is only released by `Stop`/`Dispose`.

## 3.1 Physical observation: what is and is not continued

`P-054` excludes engine-internal solver state from a portable checkpoint, and 06 §7 says an adapter "restores
declared authoritative pose/velocity or observation policy and records any restabilization limits". The traversal
course is the genre where that becomes observable, through four observations that run only for a family declaring an
engine physical domain:

| Observation | What it proves | Requirement |
|---|---|---|
| `gc027-recovered-engine-physics-is-reseeded-not-continued` | the recovered scene is its own **dedicated local** `PhysicsScene`; every runner body's **engine** pose equals the world's **authoritative ECS** pose the checkpoint carried; the recovered scene's own `SimulateCount` is **zero** before the recovered world steps it, while the source's counter is reported for contrast | P-054, 04 s7 |
| `gc027-source-authoritative-state-survives-the-recovery` | the course's authoritative text — every runner's ECS pose and velocity plus its accepted-checkpoint progress — is **identical** in both worlds | P-053, 07 §4.3 |
| `gc027-recovered-world-refuses-an-old-session-observation` | an image stamped with the **old** session is refused `ForeignWorld`/`StaleHandle` with no lease, and the recovered world's own image is stamped with the **new** session, epoch and step | P-004, P-005, P-049 |
| `gc027-recovered-world-steps-its-engine-once-per-admitted-step` | the recovered world commits its own steps, its engine simulates exactly once per committed step, and a repeated admission for the same step is refused | REF-A06, P-036 |

**The limitation, stated plainly:** the engine's solver state is *not* carried across a recovery and is *not* claimed
to be. What is carried is the declared authoritative pose and velocity; what the recovered world does is re-seed its
bodies from that state and then simulate its own steps. A recovery therefore does not continue an in-flight
simulation, does not preserve sub-step solver accumulators, and makes no bit-identical continuation claim. Two
consequences a caller must design around: (a) a body's engine pose immediately after a recovery equals its
authoritative pose, so any engine-only state (penetration depth, contact manifolds, sleep timers) is reset; (b) the
engine's simulation counter is a per-session count, so a monitoring system must not compare a counter across a
recovery.

The same limitation applies to a card or narrative world trivially — those genres declare no engine physical domain
at all, so nothing engine-owned exists to continue.

## 4. Content incompatibility

An incompatible document leaves the new world **unexposed**, and this is enforced before anything is built:

| Incompatibility | Where it is refused | Code |
|---|---|---|
| unknown required feature id / protocol major | `CheckpointDocument.TryRead` (before a plan exists) | `UnsupportedVersion` |
| catalog fingerprint mismatch | `CheckpointRestorePlanner.Plan` (`RestoreRefusal.CatalogMismatch`) | `UnsupportedVersion` |
| captured schema without a directed migration path | `CheckpointRestorePlanner.Plan` | `MigrationRequired` |
| two migration chains to one destination | `CheckpointRestorePlanner.Plan` | `OwnershipConflict` |
| corrupt/truncated envelope, bad checksum, wrong length | the store (`TryRead`) or `CheckpointDocument.TryRead` | `ResourceUnavailable` |
| unresolved required reference / duplicate identity | `CheckpointIdentityTable` through the planner | `MissingDependency` |
| document from another session | `CheckpointRestoreExecutor.Validate` before exposure | `IdempotencyConflict` |

In every one of those rows the destination world does not exist (the planner refuses before a builder is called) or
was destroyed by the executor's own `Discard(staging)`; the probe asserts `DestinationHost == null` and that the
registry count is unchanged.

## 5. P-049's host-configured bounded retries

`RecoveryRetryPolicy.FromHostSettings(OperationExpirySettings)` is the composition's only retry input; before this
change set nothing in the repository consumed `BoundedTransientAttempts` (the W5 and W6 gates both recorded that
clause as unproven).

* only `ResourceUnavailable` is retryable with the same input (`RetryClassification.RetrySameInput`); every version,
  migration, ownership, identity and fault code needs changed input, a changed catalog or a different world;
* `BudgetExceeded` is deliberately **not** retryable — P-022 says raising a budget is an explicit configuration
  change, never a retry loop;
* a retry always reserves a NEW session and a NEW operation id (P-049, P-050), which
  `RecoveryAttemptLog.AttemptsAreDistinct()` exposes and the probe asserts on the real attempt records;
* a configured bound above `RecoveryRetryPolicy.MaxConfiguredAttempts` is **clamped** and reported
  (`WasClamped`), because a host setting is still configuration and an unbounded loop is not an option;
* the observation runs against a real transient refusal (`Gc027FlakyStore` refuses the first read with
  `ResourceUnavailable`), and its exhaustion half re-runs the same failure with the bound set to one attempt and
  asserts the failure is then final.

## 6. Exact commands for the Linux build host

```sh
# 1. the pure suites (the new fixtures project and every existing one)
dotnet build dotnet/GameCore.sln -c Release
dotnet test  dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gc-027/trx

# 2. the Unity EditMode suite, including GameCore.Gc027.Tests
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode \
  -testResults "$PWD/artifacts/gc-027/unity/editmode.xml" \
  -logFile artifacts/gc-027/unity/editmode.log

# 3. the qualification player (defines GAMECORE_FAULT_INJECTION, so the latches are live) and the probe
UNITY="$UNITY" ARTIFACTS=artifacts/gc-027/toolchain tools/unity/build_probe.sh
PROBE_RUNS=5 ARTIFACTS=artifacts/gc-027/toolchain tools/unity/run_recovery_probe.sh

# 4. the release-surface halves this change set extends
python3 tools/check_release_fault_free.py --dotnet "$DOTNET" --json artifacts/gc-027/release-surface.json
python3 tools/unity/prepare_gc017_release_project.py
python3 tools/check_release_clone.py --json artifacts/gc-027/release-clone.json
rm -rf unity/GameCore.ReleaseCheck
```

The build host fills in the evidence columns of §2 by copying, from `artifacts/gc-027/toolchain/probe-gc027.json`:

* one line per observation name with its `status` and `detail` (the transcript and outbox-consistency evidence the
  task asks for is *inside* those details: every recovery step's detail carries the transcript summary and the
  outbox census);
* the three digest literals reported by the digest steps, which must equal the frozen literals in
  `ProbeRecovery.NarrativeDigest` / `CardsDigest` / `TraversalDigest`. The narrative and card tables are unchanged
  by the traversal work; the traversal table is the shared one minus the four delivery observations (the course
  declares no delivery obligation) plus its four engine-physics ones, and its literal is `30ff0f92…`.

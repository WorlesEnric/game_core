# GC-027 HANDOFF — checkpoint and durable-delivery recovery under faults

Branch `gc-027` (worktree `/Users/yangcao/wkspace/gc-wt/gc-027`), forked from `main` at `700c3a9` (the Wave 6
integration gate).

**Status of every executable check in this change set: `NotRun (pending orchestrator build host)`.** This host has
no Unity, no .NET SDK, no Mono and no C# compiler, so nothing here has been compiled, imported, executed or built.
What *did* run is interpreter-level only and is recorded verbatim in `artifacts/gc-027/static-checks.log`: the
host-side C# checker (555 files), the GC-017 release-surface source check, the release-clone preparation **run for
real** followed by `check_release_clone.py` (verdict `clone is clean`) and then deletion, `bash -n` over the new probe
harness, `ast.parse` over the three edited Python tools, a check that all 42 release-strip needles match exactly
once, and an independent recomputation of both frozen digest literals (cross-checked by reproducing GC-018's two
published literals with the same algorithm). None of that is a build, an import, a test or a player run.

## 1. Summary

GC-027's task sentence — *"Compose `RecoverWorld` (O-22) over the existing capture/restore path, with P-049
host-configured bounded retries. Inject faults at capture copy, file publication, restore reference repair, postwrite
apply, outbox append, delivery, ack and restart. For each: the permitted observable result, the failed old world
never resumes, no hidden external replay. Restore cards, narrative and traversal-compatible checkpoint data into a
NEW world with different native handles; verify active/dormant state, pending-command disposition and delivery cursor
intact; replayed external delivery does not duplicate the test destination effect; incompatible content leaves the
new world unexposed."* — is delivered as five pieces:

1. **`O-22 RecoverWorld` is composed** (`Runtime/Recovery/WorldRecovery.cs`), over `CheckpointRestoreExecutor`
   (O-21) and `InitialDefinitionRecovery` (GC-017) — neither re-implemented. This closes the open item both the W5
   and W6 gates recorded before the W7 wave.
2. **P-049's host-configured bounded retries are enforced** (`RecoveryRetryPolicy`), which closes the other W5/W6
   open item: `OperationExpirySettings.BoundedTransientAttempts` had no consumer anywhere in the repository.
3. **The eight injection points are covered deterministically** — five through GC-017's existing latch (four new
   named boundaries appended to its mechanism, none of them a second mechanism), three through GC-021's existing
   `IDeliveryStepHook`, and the restart point through the store's own refusal values.
4. **The checkpoint file adapter 06 §7 requires now exists** (`CheckpointStore`), because GC-018 deliberately left
   publication to the caller and an O-22 recovery had no verified blob to recover from.
5. **The proof runs in Unity and in the player** — seventeen observations per family, a `-probeRecovery` mode and a
   harness, plus the EditMode suite that recomputes the two digest literals from the frozen name table.

## 2. Commits on this branch

| Commit | Contents |
|---|---|
| `kernel: GC-027 recovery latch boundaries at the restore and publication seams` | `FaultBoundaries.cs` (5 enum members + 5 name-table entries), `CheckpointRestoreExecutor.cs` (2 reaches), `tools/check_release_fault_free.py` (the mirrored name table and owner list it checks) |
| `GC-027: the engine-free recovery core and the O-22 RecoverWorld composition` | `Runtime/Pure/Recovery/{CheckpointStore,RecoveryPolicy,OutboxConsistencyReport,RecoveryTranscript}.cs`, `Runtime/Recovery/WorldRecovery.cs` |
| `GC-027: versioned recovery fixtures and their plain-dotnet projects` | `tests/GameCore.Recovery/**`, the two `dotnet/` projects, `dotnet/GameCore.sln`, `dotnet/README.md` |
| `GC-027: the Unity recovery proof, its probe mode and its release strip` | `Gc027{Family,SourceWorld,RestoreBuilder,Scenario,NarrativeHost,CardsHost}.cs`, `ProbeRecovery.cs`, `Tests/Gc027/**`, `ProbeArguments.cs`, `ProbeRunner.cs`, `tools/unity/run_recovery_probe.sh`, the two release-strip tools |
| `GC-027: the evidence set and this handoff` | `artifacts/gc-027/**` |

## 3. Files created

### 3.1 Engine-free production core (`Packages/com.gamecore.unity.runtime/Runtime/Pure/Recovery/`, compiled as `GameCore.Execution`)

| File | Contents |
|---|---|
| `CheckpointStore.cs` | `CheckpointStoreFormat` (format id, major/minor, magic, header/trailer layout, the 64 MiB ceiling mirroring `CheckpointFormat.MaxDocumentBytes`), `StoredCheckpoint`, `ICheckpointStore`, `FileCheckpointStore` (temporary file + `Flush(true)` + replacement, temporary artifact removed on a refused publication), `MemoryCheckpointStore`, `CheckpointStoreEnvelope` (the offsets a corruption case names). |
| `RecoveryPolicy.cs` | `RecoverySourceKind`, `RecoveryInjectionMechanism`, `RecoveryPermittedOutcome`, `RecoveryDataLossClass`, `RecoveryFaultPoint`/`RecoveryFaultPoints` (the eight points as production data), `RecoveryFailureClassification`, `RecoveryRetryPolicy`, `RecoveryAttemptKind`, `RecoveryAttemptRecord`, `RecoveryAttemptLog`. |
| `OutboxConsistencyReport.cs` | `OutboxCursorRow`, `OutboxConsistencyReport`, `OutboxConsistency.Verify` — the census, with every disagreement named. |
| `RecoveryTranscript.cs` | `RecoveryPhase` (14 phases), `RecoveryTranscriptLine`, `RecoveryTranscript` (bounded, ordinal, digest, data-loss boundary), `RecoveryRestartPoint`. |

### 3.2 Unity production composition (`Packages/com.gamecore.unity.runtime/Runtime/Recovery/`)

| File | Contents |
|---|---|
| `WorldRecovery.cs` | `RecoveryHealthPhase`, `WorldRecoveryRequest`, `WorldRecoveryContext`, `WorldRecoveryReport`, `CheckpointPublicationResult`, `CheckpointPublicationRequest`, `CheckpointPublication.CaptureAndPublish`, `WorldRecovery.Recover`, `WorldRecovery.Restart`. |

### 3.3 Pure fixtures (`tests/GameCore.Recovery/`)

| Path | Contents |
|---|---|
| `Data/recovery-matrix.json` | The permitted-outcome matrix, one case per injection point in the task's order. |
| `Data/checkpoint-store-versions.json` | The store-envelope version cases, one per refusal branch. |
| `Runtime/RecoveryJson.cs` | The small, strict, dependency-free JSON reader. |
| `Runtime/RecoveryMatrixFixture.cs` | The vocabulary tables, the model, the strict reader and the repository-root lookup. |
| `Tests/{CheckpointStoreTests,RecoveryMatrixFixtureTests,RecoveryPolicyTests,RecoveryTranscriptTests,OutboxConsistencyTests,RecoveryFixtureDataTests}.cs` | Six suites driving the production types. |
| `README.md`, `package.json`, the two asmdefs, `.meta` files | Package metadata; every added file has a Unity `.meta` with a unique GUID. |

### 3.4 Unity qualification (`unity/GameCore.Validation/Assets/GameCore.Validation/`)

| Path | Contents |
|---|---|
| `Runtime/Gc027Family.cs` | `Gc027RuntimeWorld`, `IGc027Family`. |
| `Runtime/Gc027SourceWorld.cs` | `Gc027SourceWorld` (bring-up, capture, postwrite fault), `Gc027RecordingDestination`, `Gc027DeliveryCrashHook`, `Gc027DeliveryCrashException`, `Gc027FlakyStore`. |
| `Runtime/Gc027RestoreBuilder.cs` | The O-21 rebuilder adapted to a recovery; also an `IRestoreOutboxBuilder` (the first implementer in the repository, which makes the executor's outbox step live rather than dead code). |
| `Runtime/Gc027Scenario.cs` | The seventeen observations, their detail strings and the per-family result digest. |
| `Runtime/Gc027NarrativeHost.cs`, `Runtime/Gc027CardsHost.cs` | The two family adapters and their `RecoveryFamily()` entry points. |
| `Runtime/ProbeRecovery.cs` | The `-probeRecovery` mode and the two pinned digest literals. |
| `Tests/Gc027/{GameCore.Gc027.Tests.asmdef,Gc027IntegrationTests.cs}` | The EditMode suite. |
| `tools/unity/run_recovery_probe.sh` | The player harness. |

### 3.5 dotnet projects and evidence

| Path | Contents |
|---|---|
| `dotnet/src/GameCore.Recovery.Fixtures/GameCore.Recovery.Fixtures.csproj` | netstandard2.1 library over `tests/GameCore.Recovery/Runtime/**`. |
| `dotnet/tests/GameCore.Recovery.Fixtures.Tests/GameCore.Recovery.Fixtures.Tests.csproj` | net8.0 NUnit 3 suite over `tests/GameCore.Recovery/Tests/**`. |
| `artifacts/gc-027/{README.md,recovery-behavior-and-data-loss.md,fault-injection-matrix.md,crash-restart-transcripts.md,outbox-consistency.md,inventory-proposals.md,static-checks.log}` | The evidence set: what ran here, the behavior/data-loss documentation with the fill-in columns, and the two transcript/report templates. |

## 4. Files modified

| Path | Change | Why |
|---|---|---|
| `Packages/.../Runtime/Faults/FaultBoundaries.cs` | **`kernel:`** five `FaultBoundary` members (`CheckpointCaptureCopy=8` … `RecoveryPublication=12`) and their five `FaultBoundaryText.Names` entries, appended in the same order. Additive: no existing member or value changed. | The task's injection points must be reachable deterministically through the mechanism that already exists (GC-017's latch), not a second one. §5.1. |
| `Packages/.../Runtime/Persistence/CheckpointRestoreExecutor.cs` | **`shared:`** two `FaultReach.Reach` call sites inside `#if GAMECORE_FAULT_INJECTION` blocks — the postwrite-apply reach after `builder.TryBuild` returns and the publication reach immediately before `UnityWorldRegistry.TryExpose` — plus one guarded `using`. Additive: the unconditioned code path is byte-for-byte unchanged. | Points 4 and 5 of the matrix have no other seam inside the O-21 sequence, and a fault there must destroy the staging world rather than expose it. §5.2. |
| `tools/check_release_fault_free.py` | **`tool:`** the mirrored boundary-name table gains the five names; `BOUNDARY_OWNERS` gains the two new owner files. | The release-surface claim is about the *declared* table and every file that reaches it; a table the checker does not know would silently stop being checked. |
| `unity/.../Runtime/ProbeArguments.cs` | `-probeRecovery` mode: const, ctor parameter, assignment, property with its doc comment, parse local, parse branch, `IsProbeInvocation` term. Additive; every earlier mode's branch untouched. | The new probe mode. |
| `unity/.../Runtime/ProbeRunner.cs` | One dispatch arm and one report-identity arm for `-probeRecovery`. Additive; each arm calls `CompletePositive()` itself (the shape a merge once broke). | The new probe mode. |
| `tools/unity/prepare_gc017_release_project.py` | GC-027's seven runtime files join the removal tuple, `("Recovery","GC-027")` joins the mode tuple, six `ARG_NEEDLES` added, the `IsProbeInvocation` expression and the constructor call re-derived, the module docstring updated. | A qualification-only mode must leave the marker-free clone as one consistent set. §5.3. |
| `tools/check_release_clone.py` | Seven GC-027 type names in `REMOVED_TYPES`, `('Recovery','recovery')` in `REMOVED_MODES`. | The clone's invariants are asserted on the clone, not trusted to the script that made it. |
| `dotnet/GameCore.sln`, `dotnet/README.md` | The two new projects and their rows/paragraph. | New dotnet projects must be in the solution the gate builds. |

No gameplay package was edited, no `Gc013*`/`W4Gate*`/`Gc018*`/`Gc021*`/`W5Gate*`/`W6Gate*` sequence body was
touched, and no frozen W0 seam changed.

## 5. Shared/kernel changes, and what they cost the earlier gates

### 5.1 `FaultBoundaries.cs` — additive, one table, no semantic change

Five enum members and five name-table entries, appended. `FaultBoundaryText.Count` was already derived from
`Names.Length`, so every array, bound and loop in the latch sizes itself correctly; `FaultBoundary.Validation`…`Cleanup`
keep values 0–7. **A semantic kernel change? No** — no existing boundary, reach, trace record or name changed, and
nothing outside the guard changed. But the following earlier gates must be **rerun** anyway, because they assert
things derived from this table:

* **GC-017's fault gate** (`tools/run_gc017_*`, `artifacts/faults/boundaries.json`, `trace-format.md`): the boundary
  list and the release-surface inspection both read this table. `artifacts/faults/boundaries.json` records 8
  boundaries and `trace-format.md` enumerates the 8 names — **both are now stale and must be regenerated** by the
  GC-017 gate owner (or accepted as historical, with a note); this change set deliberately does not edit another
  task's archived evidence.
* **`tools/check_release_fault_free.py`**: updated here, and both its `--no-build` halves pass on this host.
* **Any gate asserting `FaultBoundaryText.Count == 8`**: I found none by grep over `Packages`, `unity` and the gate
  scripts, and `check_game_core_csharp.py` plus the source-level fault check pass; if one exists it fails loudly
  rather than silently.
* **The Unity EditMode fault suites** (`GameCore.Unity.Faults.Tests`, `GameCore.Faults.Tests`) arm boundaries by
  member, so they are unaffected; they must still be rerun as part of the W7 matrix.

### 5.2 `CheckpointRestoreExecutor.cs` — additive, guarded

Both reaches are inside `#if GAMECORE_FAULT_INJECTION`; with the symbol undefined the file compiles to exactly what
it did before (verified by the source-level half of `check_release_fault_free.py`, which evaluates the guards rather
than trusting them). With the symbol defined, an unarmed boundary still records a trace reach and returns, so the
**GC-018 round trip, the W5 gate's restore step and GC-021's capture step take the same path as before** — but the
trace now contains two additional reaches per restore. Consequences:

* **Rerun required**: GC-018's Unity/player probes and the W5 gate's restore observations
  (`gc018-restore-*`, `w5gate-restore-into-a-new-session`), because both assert on a *restore* that now records
  more reaches than before. Their own digests are over observation names and pass flags, not over the trace, so no
  literal should move; the rerun is what confirms it.
* **Not rerun-affected**: the pure-dotnet suites (the guarded block is excluded there) and the checkpoint document
  bytes (no serialized field changed).

**A semantic kernel change? No, but it is a change to a file three earlier tasks own**, so §9 lists the exact
rerun set. `docs/game-core/` is unchanged: no normative document needed an edit, because O-22, P-049 and the
TEST-016 rows this composition implements were already written.

### 5.3 The release strip — one consistency set

The mode wiring is stripped from the clone by six exact needles per member; each was verified to match exactly once
against the current `ProbeArguments.cs` (42 needles, 0 mismatches), and `check_release_clone.py` runs on the real
clone and reports `clone is clean` with `Recovery` absent from every wiring point and all thirteen kept modes still
wired.

## 6. Contract changes

Per the brief: additions only, and none of them changes an existing member's signature or meaning.

| # | Change | Where | Justification |
|---|---|---|---|
| 1 | Five `FaultBoundary` members and their names | `GameCore.Unity.Runtime.Faults` (qualification-only compilation) | The task's injection points; §5.1. **Not a `GameCore.Contracts` change** — the latch is not part of the shared contract surface, and `check_contract_surface_parity.py` is unaffected (verified: it reports only the five pre-existing legitimate additions from earlier waves). |
| 2 | `ICheckpointStore`, `StoredCheckpoint`, `CheckpointStoreFormat`, `FileCheckpointStore`, `MemoryCheckpointStore`, `CheckpointStoreEnvelope` | `GameCore.Execution.Recovery` (engine-free, inside `GameCore.Unity.Runtime` for Unity) | 06 §7's local checkpoint adapter, which had no implementation. **Not a `GameCore.Contracts` change**: the contract package's envelope codec, record values and document reader are untouched, and the store frames the *already-serialized* document without decoding it (P-054 is preserved: the store adds an envelope, it does not add a second serialization). |
| 3 | `IGc027Family : IGc018Family` and the two family adapters | qualification project only | Not a production contract. |
| 4 | `Gc027RestoreBuilder` implements `IRestoreOutboxBuilder` | qualification project only | The first implementer of a seam `CheckpointRestoreExecutor` already declares; no production type changed. |

`GameCore.Contracts` is **not modified at all** by this change set, so no catalog fingerprint, no generated catalog
byte and no frozen API snapshot moves. `docs/game-core/traceability.json` is unchanged (no requirement status is
claimed by this change set; §9 proposes them).

## 7. Requirement / test coverage mapping

| Requirement / test | Where implemented | Where observed (this change set) |
|---|---|---|
| **O-22** RecoverWorld | `WorldRecovery.Recover`, `WorldRecovery.Restart` | `gc027-recovery-publishes-a-new-session-with-the-captured-state`, `…restart-from-the-store…`, `…restart-without-a-document…` |
| **P-049** recovery limits, no resume, no replay, bounded retries | `WorldRecovery`, `RecoveryRetryPolicy`, `RecoveryFailureClassification`, `RecoveryFaultPoints` | all twelve fault/restart/retry observations; `SourceNeverResumed`, `ObligationsOwed`, the run's own `registry=…->0` |
| **P-031** postwrite failure halts and never resumes | the existing publisher/`EnterFaulted` path, driven by `Gc027SourceWorld.TryFaultAfterFirstLiveWrite` | the source world's `Faulted`/`ApplyFault` state, then every recovery observation's `source=` field |
| **P-030** one serialized publication | `CheckpointRestoreExecutor`'s expose step + the new publication reach | `gc027-recovery-publication-fault-keeps-the-registry-unchanged` |
| **P-048** retained/quarantined on a blocked stop | `WorldRecovery.StopSource` | the transcript's `SourceStopped` line (a blocked stop is reported, not retried) |
| **P-045** durable delivery, idempotency, no exactly-once claim | GC-021's core, driven at its three boundaries; `OutboxConsistency` | `gc027-outbox-append…`, `…delivery-fault…`, `…acknowledgement-fault…`, `gc027-outbox-rows-and-delivery-cursor-survive-the-recovery` |
| **P-053** checkpoint contents, boundary, outbox/cursor | GC-018's capture + the new store | `gc027-source-world-captures-and-publishes-a-verified-checkpoint`, `…outbox-rows-and-delivery-cursor…` |
| **P-054** serialization discipline, versioned refusals | `CheckpointStore`, the store-version fixture | `gc027-publication-fault-keeps-the-previous-document`, `…restart-without-a-document…`, `CheckpointStoreTests`, `RecoveryMatrixFixtureTests` |
| **P-050** idempotency, new operation per retry | `WorldRecovery`'s reservation per attempt | `gc027-transient-failure-is-retried-under-the-host-bound` (`AttemptIdentitiesAreDistinct`) |
| **P-004/P-005** identities and handles | `UnityWorldHost.TryCreateUnexposed`, the rebuilder | `gc027-restored-world-uses-different-native-handles` |
| **P-032** state dispositions, dormancy | GC-018's seeder + the rebuilder | `gc027-active-and-dormant-state-survive-the-recovery` |
| **TEST-016** fault injection and recovery boundaries | the eight points | all eight observations, both families, plus `Gc027IntegrationTests.TheDeclaredFaultPointsCoverTheEightTaskBoundaries` |
| **TEST-017** checkpoints and schema evolution | GC-018's round trip + the versioned fixtures | `gc027-…`, `tests/GameCore.Recovery/Data/*.json`, `RecoveryFixtureDataTests` |
| **TEST-002** identities, epochs, stale references | the fresh-incarnation and native-handle observations | `gc027-restored-world-uses-different-native-handles`, `gc027-recovery-publishes-a-new-session-…` |
| **TEST-010** state preservation and migration | the restore's state disposition | `gc027-active-and-dormant-state-survive-the-recovery`, `CheckpointRestorePlanTests` (existing) |
| **TEST-014** committed events and consistent observation | the delivery core's own semantics, exercised at three boundaries | the three delivery observations |

## 8. Design decisions and doc ambiguities

`09` invites the simplest reading consistent with `00` (`00` wins over `05`, `05` over `09`).

1. **The postwrite-apply point covers two reaches, not one.** The task names one injection point ("postwrite
   apply"), but the O-21 sequence has two distinct moments at which a fault leaves a written-to, unpublished
   world: after `IRestoreTargetBuilder.TryBuild` returns, and as the validated world is about to be exposed. Reading
   the task together with TEST-016 row 5 (one permitted result: an incomplete destination never becomes the running
   world) and O-21's probe, both reaches belong to one point with one permitted result and one data-loss class. So
   `RecoveryFaultPoints.RestorePostwriteApply` carries two boundary names, and the scenario has two observations
   (one per reach) so each is proven separately. Recorded because a reviewer counting boundaries might expect eight
   names for eight points and find nine.
2. **The reference-repair fault fires before any destination exists.** P-049's "validates/rebuilds composition and
   recipes" happens *inside* the rebuilder, whose handle the caller does not hold until the attempt is over. Rather
   than reach into a half-built world, the `restore-reference-repair` boundary is reached in `WorldRecovery` before
   the executor runs, which gives the strictly stronger permitted result: there is no destination to leave
   incomplete. TEST-016's last row ("checkpoint restore fails during reference repair → incomplete destination never
   becomes the running published world") holds by construction rather than by cleanup. The alternative — reaching
   inside the rebuilder — was rejected because it would need a second latch on a world the caller cannot see.
3. **The checkpoint store frames, it does not serialize.** 06 §7 asks for "temporary file, checksum, and atomic file
   replacement"; it does not ask for a second container format. The store writes a thin envelope (magic, format
   major/minor, document length, document checksum, trailing envelope checksum) around the bytes
   `CheckpointCapture` already produced through the generated serializers, so P-054's serialization discipline is
   untouched and the documented envelope is the store's own version, versioned *separately* from the checkpoint
   document (P-055).
4. **The store refuses a newer minor.** `IsSupported(1, 1)` is false, which is stricter than P-055's "a minor
   extension may add optional fields". The reason is that this envelope has no optional-field escape hatch — a
   reader that accepted a newer minor would be reading a layout it cannot bound — and P-055's own rule is to refuse
   an unknown version rather than guess. The fixture records the case so a reviewer sees the choice.
5. **Only `ResourceUnavailable` is retryable.** P-049 says a retryable *transient resource* failure may be retried
   and a correctness failure needs changed input; P-052's code list has no "transient" flag, so the classification
   is explicit in `RecoveryFailureClassification.RetryOf`. `BudgetExceeded` is deliberately not retryable (P-022:
   raising a budget is a configuration change, not a retry loop), and `ApplyFault`/`TooLate`/`StaleHandle`/
   `Cancelled`/`ResultExpired`/`TeardownBlocked` are terminal.
6. **The retry bound is clamped.** A host setting of 1,000 attempts is clamped to
   `RecoveryRetryPolicy.MaxConfiguredAttempts` (16) and `WasClamped` reports it. P-049 permits *bounded* attempts;
   a configuration value that could mean "unbounded" in practice is not a bound. The clamp is reported rather than
   silent, so a caller whose setting was reduced can see it.
7. **`RecoveryTranscript` is timestamp-free.** A transcript carrying host time could not be compared between runs,
   which is the whole point of its digest. P-053's "raw host timestamps are normalized to declared clock policy"
   applies; the simplest normalization for a record whose only consumer is a diff is to have none.
8. **A restart is a separate entry point, not a recovery with a missing source.** `WorldRecovery.Restart` takes no
   source world at all, because a restart genuinely has none: `Recover` refuses a source that is not in the registry
   (`StaleHandle`), which is the right answer for "recover from a world that is gone" and the wrong shape for "the
   process died". Both share `AttemptRestore` and the same bounded-retry rule, so there is one restore path.
9. **The delivery hook, not a new latch, covers the delivery boundaries.** GC-021 already shipped
   `IDeliveryStepHook` as a *reporting* seam with a test-only caller (04 §6's split: this assembly can report where
   it is, only a test can decide the process dies there). The three delivery points use it as designed. Adding
   latches beside it would be the second mechanism 00 §9 forbids for operations.
10. **The outbox census lives in the engine-free core.** `OutboxConsistency.Verify` is pure and mutates nothing, so
    it can be the builder's pre-exposure proof (`TryProveOutbox`) *and* the scenario's report *and* a plain-dotnet
    test's subject, from one implementation. A second implementation for the probe would be a second opinion about
    whether obligations survived.

## 9. Rerun set for earlier gates (because of §5)

| Gate / task | What must be rerun | Why |
|---|---|---|
| **GC-017** (`artifacts/faults/**`) | the fault player probe and the release-surface tools; **regenerate** `artifacts/faults/boundaries.json` and the boundary list in `artifacts/faults/trace-format.md` (8 → 13 names) | the boundary table this change set extends is GC-017's own evidence subject |
| **GC-018** | the Unity EditMode suite, the `-probeGc018` player probe and its two digest literals | the restore sequence it drives now records two additional latch reaches per restore when the symbol is defined |
| **W5 gate** | `tools/run_w5_gate.sh` (its restore and postwrite observations) | same reason; its digests are over observation names, so no literal should move, and the rerun is what confirms it |
| **GC-021** | its capture/restore observation (`executorRestore`) | GC-021's checkpoint capture now has a store adapter available; its own scenario does not use one, so the rerun is a regression check, not a new claim |
| **W6 gate** | unchanged in substance; its probe modes now number fourteen | the probe host gained a mode; the W6 gate's own release-surface and probe assertions are unaffected |
| **GC-024/025/026** | no dependency | this change set adds no genre type to the kernel and touches no shared execution path |

## 10. Known gaps and assumptions

1. **Nothing has been compiled or executed.** The most likely first-build failures, in order: (a) a member or
   signature mismatch between the new Unity scenario/builder and the frozen runtime types — the compile-risk audit
   of §11 is the mitigation and its findings are folded in; (b) the `Gc027RestoreBuilder` adaptation of GC-018's
   private helpers (a name that moved, an overload that differs); (c) a partial-class member collision between
   `Gc027NarrativeHost.cs`/`Gc027CardsHost.cs` and the halves they extend; (d) `IGc027Family`'s inherited surface
   being incompletely implemented by one family.
2. **Traversal is not covered by this change set.** The task says "cards, narrative and traversal-compatible
   checkpoint data". Cards and narrative each run the full sequence. Traversal is *compatible* — nothing in the
   composition is genre-specific (P-001): the recovery reads a definition, a store and a registration, all of which
   a traversal world has — but this change set does not add a third `IGc027Family` adapter for it, because
   GC-020/GC-024 own the traversal course and its own scenarios, and a half-built third adapter would be worse
   evidence than none. **Declared limitation**: the traversal-compatible claim is structural (no genre type appears
   in `WorldRecovery`, `CheckpointStore`, `RecoveryPolicy` or the transcript), not executed. If the orchestrator
   wants an executed traversal recovery, the shape is one more `IGc027Family` implementation over
   `Gc020Family.IGc020Family`'s declarations.
3. **Physical-observation limitations for traversal.** Per the task's "declare physical-observation limitations":
   `00` P-054 and 06 §7 both exclude engine-internal state, so even for a traversal world this recovery carries the
   declared authoritative pose/velocity and the RNG streams, never a physics solver state, and makes no
   bit-identical continuation claim. That limitation is stated in
   `artifacts/gc-027/recovery-behavior-and-data-loss.md` §3 rather than implied.
4. **The checkpoint store is a local-file adapter, deliberately.** It is the "local checkpoint adapter" of 06 §7.
   The same paragraph's object-storage adapter ("its own durable publication protocol") is out of scope and not
   claimed.
5. **`Pending-command disposition` is asserted as the restore path's own values**, not re-derived: the scenario
   reads `RestoreOutcome.ReadmittedCommandCount` and the capture's queue disposition (which GC-018's own suite
   already asserts end to end). This change set adds the delivery-obligation half of the same claim, which GC-018
   left to the caller.
6. **The `Gc027RestoreBuilder` has one public member the task did not name**: `Runtime`, the direct counterpart of
   `DetachRuntime`, copied from `Gc018FamilyRestoreBuilder` where it exists for the same reason. Kept so the two
   builders stay comparable; noted here because the surface was specified exactly.
7. **Outbox retention and dedup at scale remain open.** The consistency report is exercised at one-world scale
   (one obligation, one destination, one cursor). GC-021 and GC-026 own retention-under-pressure and cursor/dedup
   scale; §11's inventory proposals do not claim them.
8. **`GameCore.Contracts` is untouched**, so the interface gate (GC-002) is not reopened and no snapshot or catalog
   fingerprint needs regenerating.
9. **The `artifacts/faults/boundaries.json` staleness from §5.1 is a real, known gap** — it is another task's
   archived evidence and this change set deliberately does not rewrite it; §9 names the fix.

## 11. What actually ran on this host

Verbatim in `artifacts/gc-027/static-checks.log`. Summary:

```sh
python3 tools/check_game_core_csharp.py                       # checked 555 C# file(s) -> ok
python3 tools/check_release_fault_free.py --no-build           # PASS (both source halves)
python3 tools/unity/prepare_gc017_release_project.py           # ran for real; clone prepared
python3 tools/check_release_clone.py                           # VERDICT: clone is clean (149 files)
rm -rf unity/GameCore.ReleaseCheck                             # clone deleted
bash -n tools/unity/run_recovery_probe.sh                      # exit 0
python3 -c "import ast; ast.parse(...)"  # the three edited Python tools -> ok
# every ARG_NEEDLES entry matches exactly once: needles 42, mismatches 0
# both digest literals recomputed from the frozen name table; the same algorithm reproduces
# GC-018's two published literals, so the method is validated rather than assumed
```

Also run: the canonical-form check over both committed fixture documents (UTF-8, LF, one trailing newline, 2-space
indentation, no trailing whitespace); a grep over `Packages`, `unity` and `tools` confirming no gate asserts
`FaultBoundaryText.Count == 8`; and the two `dotnet` project files checked by hand against their sibling templates.

None of that is a build, an import, a test or a player run.

## 12. Inventory proposals (for the build host to promote after running)

Proposed only, in `artifacts/gc-027/inventory-proposals.md`; **no row in
`artifacts/gates/w4-generic-profile/inventory.{md,json}` was edited by this change set**. Headline proposals:
`O-22 RecoverWorld` `Not yet` → `Implemented+Evidenced`; `P-049` `Partial` → `Implemented+Evidenced`; four new
capability rows (the store adapter, the fault table, the consumed retry bound, the outbox census). `P-004`, `P-005`,
`P-007`, `P-032`, `P-045`, `P-050` are explicitly **not** promoted, each with its reason.

## 13. Exact commands for the Linux build host

Everything runs from the repository root. Nothing below has been run.

### 13.1 Pure suites

```sh
dotnet build dotnet/GameCore.sln -c Release
dotnet test  dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gc-027/trx
```

The new cases live in `dotnet/tests/GameCore.Recovery.Fixtures.Tests` (`CheckpointStoreTests`,
`RecoveryMatrixFixtureTests`, `RecoveryPolicyTests`, `RecoveryTranscriptTests`, `OutboxConsistencyTests`,
`RecoveryFixtureDataTests`). While iterating on that project alone:

```sh
dotnet test dotnet/tests/GameCore.Recovery.Fixtures.Tests/GameCore.Recovery.Fixtures.Tests.csproj -c Release
```

The fixture JSON is located through `RecoveryFixturePaths.FindRoot()` (an upward search for
`docs/game-core/traceability.json`, overridable with `GAMECORE_REPO_ROOT`), so the suite must be started from a
checkout of this repository.

### 13.2 Unity EditMode (the new suite included)

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode \
  -testResults "$PWD/artifacts/gc-027/unity/editmode.xml" -logFile artifacts/gc-027/unity/editmode.log
```

Do not add `-quit` to a test-run command (04 §10). `-testResults` with a relative path resolves against the Unity
**project** path, not the shell cwd (unlike `-logFile`), which is why the command above passes an absolute path.
The new suite is `GameCore.Gc027.Tests`; the new package suite is `GameCore.Recovery.Fixtures.Tests`, which runs
because `com.gamecore.recovery` appears in the validation project's manifest and its `testables` list (this change
set adds both).

### 13.3 The qualification player and the recovery probe

```sh
UNITY="$UNITY" ARTIFACTS=artifacts/gc-027/toolchain tools/unity/build_probe.sh
PROBE_RUNS=5 ARTIFACTS=artifacts/gc-027/toolchain tools/unity/run_recovery_probe.sh
```

The harness asserts `"task": "GC-027"`, `"mode": "Recovery"`, `"result": "Pass"`, the absence of
`"status": "Fail"`, all 34 qualified observation names, both digest steps and their pinned literals, and twelve
clause fragments — on **every** one of the `PROBE_RUNS` runs, not only the first.

### 13.4 Release-surface and clone checks this change set extends

```sh
python3 tools/check_release_fault_free.py --dotnet "$DOTNET" --json artifacts/gc-027/release-surface.json
python3 tools/unity/prepare_gc017_release_project.py
python3 tools/check_release_clone.py --json artifacts/gc-027/release-clone.json
rm -rf unity/GameCore.ReleaseCheck
```

### 13.5 Documentation validation

```sh
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
```

`docs/game-core/` is unchanged by this change set, so this is a regression check rather than a new claim.

## 14. Evidence the build host must produce

| Artifact | What fills it |
|---|---|
| `artifacts/gc-027/trx/**` | every pure suite, including the six new ones |
| `artifacts/gc-027/unity/editmode.xml` | the Unity half, including `GameCore.Gc027.Tests` |
| `artifacts/gc-027/toolchain/probe-gc027.json` (+ `.run2..5`) | the player half; the observation details inside it are the source for `crash-restart-transcripts.md` and `outbox-consistency.md` |
| `artifacts/gc-027/release-surface.json`, `release-clone.json` | the two extended release checks, with the full `dotnet` present |
| the filled-in columns of `recovery-behavior-and-data-loss.md` §2 and `fault-injection-matrix.md` | copied from the probe artifact, per those files' instructions |
| the filled-in tables of `crash-restart-transcripts.md` and `outbox-consistency.md` | same |

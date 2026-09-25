# GC-018 handoff — checkpoint capture, restore and directed schema migration

Branch `gc-018` (worktree `/Users/yangcao/wkspace/gc-wt/gc-018`), forked from `main` at `f28a4af` (the Wave 4
integration gate).

**Status of every executable check this change set adds: `NotRun (pending orchestrator build host)`.** This host has
no Unity, no .NET SDK, no C# compiler and no Mono, so nothing here has been compiled, imported or executed. §8
records exactly what did run on this host: `python3 tools/check_game_core_csharp.py` (ok), the independent
catalog-hash recomputation for the new generated catalog (`MATCH`), `bash -n` over the new shell script, and a `.meta`
GUID uniqueness scan. None of those is a build, an import, a test or a player run.

## 1. What this task delivers

`GC-018` — "recreate a complete compatible world using stable data and an unexposed restore target" (09 §GC-018).
Normative surface: **P-004, P-005, P-032, P-049, P-053, P-054, P-055**, 05 §6 (the envelope), 06 §7, and tests
**TEST-002, TEST-010, TEST-017, TEST-022**.

Before this change no checkpoint code existed anywhere in `Packages/`: `P-053` was the only requirement marked
`Not yet` in the W4 inventory and `O-20`/`O-21`/`O-22` were all `Not yet`, because there was no format, no
serializer binding, no capture path and no restore path. This change adds the format, the capture path and the
restore path; `O-22 RecoverWorld` (whose procedure is "reuse Restore/Create", `00` O-22) is now *reachable* but is
still `GC-027`'s to compose, and this handoff says so rather than claiming it.

## 2. Files created

### 2.1 `GameCore.Contracts` — the serialization contract (`Packages/com.gamecore.contracts/Runtime/Serialization/`)

| Path | Contents |
| --- | --- |
| `CheckpointFormat.cs` | The versioned container format: `CheckpointRecordKind` (twelve kinds), `CheckpointQueuePolicy`, the one required feature id, `DocumentSchema`, the field-id↔kind mapping, and the document/field/record bounds. |
| `CheckpointRecords.cs` | Twelve immutable record value types — header, scope, install, selection, target, slot, grant, clock/wake, queued command, next-step message, RNG stream, cursor — each with its accessors back to stable wrapper types, plus `CanonicalId32` for the four-word ↔ 32-byte hash collation. |
| `CheckpointCodecSet.cs` | `ICheckpointRecordCodec`/`ICheckpointRecordCodec<TValue>`, `CheckpointCodecSet` (one codec per kind, completeness reporting), and `CheckpointErrors.CodeFor` (envelope error → `DiagnosticCode`). |
| `CheckpointDocument.cs` | `CheckpointCounts`, `CheckpointSerializer` (typed record collection → one canonical document) and `CheckpointDocument` (verified read: header first, ascending field ids, per-record schema check, trailing checksum, declared-count check, on-demand typed decode). |
| `CheckpointIdentityTable.cs` | The first pass of the two-pass restore: per-category identity indexing with duplicate-identity rejection, and structural reference validation reporting every unresolved required reference. |
| `CheckpointMigrationPlan.cs` | `ISchemaMigrationStep`, `MigrationPlanOutcome`, `MigrationPlan`, `CheckpointMigrationRegistry` with unique-path-or-reject planning. |

### 2.2 `GameCore.Execution` (from `Packages/com.gamecore.unity.runtime/Runtime/Pure/Persistence/`)

| Path | Contents |
| --- | --- |
| `CommittedBoundary.cs` | `ICommittedBoundaryReader`, `CommittedBoundarySnapshot`, `BoundaryRefusal`: the frozen seam capture reads a world through. |
| `RngStreams.cs` | `RngStream` (xorshift* with explicit state and draw count) and `RngStreamTable`; V1 had no RNG type at all, so there was nothing to save. |
| `CheckpointCapture.cs` | `QueueDisposition`, `CheckpointCaptureResult`, `CheckpointCapture.Capture`, `CheckpointCaptureRequest` — O-20's orchestration. |
| `RestoreReservationLedger.cs` | `RestoreAttempt`, `RestoreReservationKind`, `RestoreReservationLedger` — the bounded, process-lifetime session reservation of P-050. |
| `CheckpointRestorePlan.cs` | `RestoreRefusal`, `RestorePlan`, `RestorePlanResult`, `CheckpointRestoreRequest`, `CheckpointRestorePlanner` — O-21's validation and planning half. |

### 2.3 `GameCore.Unity.Runtime` (`Packages/com.gamecore.unity.runtime/Runtime/Persistence/`)

| Path | Contents |
| --- | --- |
| `CheckpointCodecAdapter.cs` | Binds the twelve generated record serializers to the engine-free codec seam as direct `Func`/delegate references; `CheckpointSerializerBindings` carries one per kind. |
| `CaptureContext.cs` | The declared capture surface: definition, catalog fingerprint, live target view, target registry, clock specs, RNG table, mode, temporal settings, lane, publisher, next-step buffers, admitted command payloads. |
| `UnityCommittedBoundaryReader.cs` | `ICommittedBoundaryReader` over real ECS storage: boundary refusal, and copying active **and** dormant slot rows, composition, clocks, queued commands, next-step messages, RNG and cursors into engine-free records. |
| `CheckpointRestoreExecutor.cs` | The O-21 sequence: reserve → read → plan → build unexposed → validate → expose. `IRestoreTargetBuilder` is the ECS-rebuild seam. |

### 2.4 Generated catalog (GC-003 compiler surface)

| Path | Contents |
| --- | --- |
| `unity/GameCore.Validation/Catalogs/CheckpointCatalog.catalog.json` | The catalog description: 12 record schemas (195 fields), 0 registration groups, one required feature id. |
| `unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCheckpoint/CheckpointCatalog.g.cs` (+ `.meta`, + asmdef + `.meta`, folder `.meta`) | 273157 bytes of generated output: `CheckpointCatalog` with the 12 value structs, 12 `GeneratedSerializerBase` subclasses, `SchemaRegistrations`, `Serializers`, `BuildCatalog`, `BuildVerifiedCatalog`, `FingerprintMatchesGeneratedCatalog`, and both hash literals. |
| `unity/GameCore.Validation/Assets/GameCore.Validation/Editor/CheckpointCatalogGenerator.cs` (+ `.meta`) | The Editor bridge (`GenerateCatalog`/`VerifyCatalog`) mirroring `ProbeCatalogGenerator`. |
| `tools/emit_checkpoint_catalog.py` | The no-SDK host emitter mirror. Subordinate to the Editor bridge; `CheckpointCatalogTests` regeneration is the authoritative proof. |
| `dotnet/tests/GameCore.Content.Compiler.Tests/CheckpointCatalogTests.cs` | Five cases: committed bytes equal a fresh generation, self-verification, fingerprint literal, key literals vs `StableNameKeyDerivation`, and schema/field shape. |

### 2.5 Fixtures, scenarios, probes and tests

| Path | Contents |
| --- | --- |
| `tests/GameCore.CheckpointFixtures/` | Versioned fixture data: the schema-version transition table and the queue-policy matrix, with a README documenting the format. |
| `dotnet/tests/GameCore.Contracts.Tests/Checkpoint*.cs` | Pure contract tests: framing, identity/reference repair, directed migration planning, record values, and the versioned fixtures. |
| `dotnet/tests/GameCore.Execution.Tests/Checkpoint*.cs`, `RngStreamTests.cs` | Pure persistence tests: RNG determinism/round-trip, capture incl. both queue policies, reservation ledger, restore planning. |
| `unity/.../Runtime/Gc018Scenario.cs`, `Gc018Family.cs`, `Gc018NarrativeHost.cs`, `Gc018CardsHost.cs`, `ProbeGc018.cs` | The Unity scenario: 16 observations per family per catalog, the two digest literals, and a real `IRestoreTargetBuilder` per family. |
| `unity/.../Tests/Gc018/` | The EditMode assembly and its integration tests. |
| `tools/unity/run_gc018_probe.sh` | The player-probe harness (modeled on `run_w4_gate_probe.sh`). |

## 3. Files modified

| Path | Change | Why |
| --- | --- | --- |
| `Packages/com.gamecore.unity.runtime/Runtime/WorldHost.cs` | **`shared:`** added `UnityWorldRegistry.TryExpose` and `UnityWorldHost.TryCreateUnexposed` (both additive; no existing member changed). | O-21's "C/unexposed new world" and "publish only after validation" are impossible otherwise: `TryCreate` registers a world the moment it is constructed. §5. |
| `Packages/com.gamecore.unity.runtime/Runtime/Integration/LiveTargetSeeder.cs` | **`shared:`** added a `TrySeedSlot(..., bool active, ...)` overload. | P-032 requires dormant authoritative state to survive a restore *as dormant*; the existing overload only seeds active rows. §5. |
| `tools/verify_generated_catalog.py` | `group_registration_tables()` now accepts a catalog that copies no registration table. | The emitter legitimately emits `Array.Empty<FactoryRegistration>()` for 0 groups (the checkpoint catalog), and the verifier raised `SystemExit` on the valid output. Reported by the codegen task, authorized as a scoped correctness fix, and proved non-regressive by re-verifying both existing catalogs. |
| `unity/.../Runtime/ProbeArguments.cs`, `ProbeRunner.cs`, `GameCore.Validation.ProbeHost.asmdef` | `-probeGc018` mode, `Gc018` property, dispatch and identity arms; the generated-checkpoint assembly reference. | The new probe mode. |
| `artifacts/gates/w4-generic-profile/inventory.json`, `inventory.md` | A `gc018` revision-notes section with **proposals only**; no status row moved. | The brief: rows newly evidenced are proposed in HANDOFF, and the build host promotes them after running. |

No gameplay package, no `Gc013*`/`W4Gate*` sequence body and no frozen W0 seam was changed.

## 4. Requirement / test coverage mapping

| Requirement / test | Where implemented | Where observed (this change set) |
| --- | --- | --- |
| **P-004** stable identities, keys stored in checkpoints | `CheckpointRecords` (every record is stable-id only), `CheckpointIdentityTable` (duplicate rejection) | `gc018-committed-boundary-capture`, `gc018-restore-recreates-state-at-different-native-indices`; contract tests `CheckpointIdentityTests` |
| **P-005** runtime handles are never persisted | No record field is an `Entity`, index, `SystemHandle` or lease id; `TargetRecordValue.SourceSlot`/`SourceGeneration` are declared diagnostic-only and are never used to rebuild a mapping | `gc018-restore-recreates-state-at-different-native-indices` (indices differ, canonical state identical); `CheckpointRecordTests` |
| **P-032** state dispositions, dormant state | `SlotRecordValue.Active`; `LiveTargetSeeder.TrySeedSlot(..., active, ...)`; `RestorePlan.DormantSlotCount` | `gc018-restore-preserves-dormant-slots`; `ExecutionCheckpointTests` dormant round-trip |
| **P-049** recovery limits, new WorldId, old callbacks invalid | `CheckpointRestorePlanner` (refuses the captured session as the target), `CheckpointRestoreExecutor` | `gc018-old-callbacks-cannot-target-the-new-session` |
| **P-053** checkpoint contents, boundary, queue disposition | `CommittedBoundarySnapshot` (all named categories), `CheckpointCapture` (`QueueDisposition`, `IsAccountedFor`), `HeaderRecordValue.CountsMatch` | `gc018-committed-boundary-capture`, `gc018-queued-commands-are-dispositioned-not-omitted`, `gc018-capture-refuses-outside-a-boundary` |
| **P-054** serialization, generated serializers, unique directed migrations | `Envelope`-based framing, `CheckpointFormat.Limits`, `CheckpointMigrationRegistry.Plan` (unique-or-refuse) | `gc018-ambiguous-migration-rejects-restore`, `gc018-corrupt-and-truncated-documents-reject`; `CheckpointMigrationTests`, `migration-paths.json` |
| **P-055** protocol evolution, unknown features reject | `EnvelopeHeader` required-feature gate; `CheckpointFormat.RequiredFeatureId`/`KnownFeatureIds`; `IsSupportedProtocol` | `gc018-unknown-required-schema-rejects-restore` |
| **TEST-002** identities, epochs, stale references | Source session recorded but never reused; native indices recorded as evidence | `gc018-restore-happens-into-a-new-unexposed-world`, `...-old-callbacks-cannot-target-the-new-session` |
| **TEST-010** state preservation and migration | `RestorePlan` carries slots and migrations; mode/imports/exclusions carried | `gc018-restore-preserves-mode-imports-and-exclusions` |
| **TEST-017** checkpoints and schema evolution | The whole change set | All 16 observations, both families, both catalogs |
| **TEST-022** ordering and determinism limits | Canonical field order, canonical map order, canonical record grouping; `ContentHash` over the document | `CheckpointDocumentTests` byte-determinism case; `checkpoint-*` fixture data |
| **O-20** CaptureCheckpoint | `CheckpointCapture` + `UnityCommittedBoundaryReader` | `gc018-committed-boundary-capture` |
| **O-21** RestoreCheckpoint | `CheckpointRestorePlanner` + `CheckpointRestoreExecutor` | `gc018-restore-*` (five observations) |

## 5. Contract changes

Per the brief, shared public contract changes need this section. All three are **additions**; no existing member
changed signature or meaning, and each is in its own `shared:` commit.

1. `UnityWorldRegistry.TryExpose(UnityWorldHost) → bool` (internal). Publishes a world built outside the registry and
   refuses a session already registered. Needed because O-21 requires the staging world to be invisible until it is
   validated; without it the only way to build a world is `TryCreate`, which exposes immediately.
2. `UnityWorldHost.TryCreateUnexposed(WorldCreateRequest, UnityWorldRegistration, out UnityWorldHost?) →
   WorldCreateResult` (public static). O-21's literal "C/unexposed new world". Refuses a session the registry already
   holds, so one session id still names one world.
3. `LiveTargetSeeder.TrySeedSlot(TargetId, OwnerId, SlotId, uint, int, bool active, out DiagnosticCode, out string)`
   (new overload). P-032: the two-flag-free overload can only seed an active row, so a restore could not reinstate
   dormant state as dormant.

`CheckpointFormat.RequiredFeatureId` is a new *value* in a new type rather than a change to an existing declared
feature set, so no existing catalog's `SupportedFeatureIds` or fingerprint moved. The two committed generated
catalogs (`ProbeCatalog.g.cs`, `CardCatalog.g.cs`) are byte-identical to before this change — proved by
`git diff --exit-code` in §8.

## 6. Design decisions and doc ambiguities

Recorded because 09 invites the simplest reading consistent with `00`, and `00` wins over `05`, `05` over `09`.

1. **`O-22 RecoverWorld` is not implemented here.** `00` O-22's procedure is "requires explicit source, reuse
   Restore/Create procedures". The restore path it needs now exists and its refusals are proven; composing
   "Faulted world → new session" and the "old storage never resumes" observation belongs to `GC-027` (which owns
   `O-22`'s suite) and to the W5 gate's fault module (`GC-017`). Deliberately not claimed (§7 item 1).
2. **A container field id names the record kind.** `00` requires explicit field ids and canonical order; it does not
   prescribe how a heterogeneous record stream is framed. Reading `P-054`'s "explicit field IDs" and `P-008`'s
   "stable ordering" together, the simplest total rule is: field id = `100 + kind ordinal`, header at 1, strictly
   ascending. This makes the stream self-describing, makes a reordered or repeated field detectable without trusting
   the writer, and needs no separate index record.
3. **Corruption maps to `ResourceUnavailable`.** `P-052` enumerates the required codes and none means "corrupt save".
   A present-but-unusable blob is the same class of outcome as a missing asset, so `CheckpointErrors.CodeFor` maps
   shape/checksum failures to `ResourceUnavailable`, a *version* mismatch to `UnsupportedVersion`, a missing required
   element to `MissingDependency`, and an over-bound length to `BudgetExceeded`. A caller must not treat
   `ResourceUnavailable` as retryable with unchanged input.
4. **Ambiguous migration maps to `OwnershipConflict`.** `P-054` says ambiguity is rejected but names no code;
   `OwnershipConflict` is the "two things claim one answer" code, which is exactly what two paths to one destination
   are. Unreachable maps to `MigrationRequired`, the code `05`/06 associate with a missing migration.
5. **The capture's queue default is `RejectQueued`.** `06` §7 says the default sample adapter rejects unexecuted
   external commands with `CheckpointBoundary` before capture, preserving declared authoritative next-step queues.
   `CheckpointBoundary` is not in `P-052`'s code list, so the refusal is reported with `TooLate` at the boundary and
   the disposition is carried in `QueueDisposition` (offered/included/rejected/cutoff) and in the header
   (`QueuePolicy`, `AdmissionCutoff`, `RejectedQueuedCount`). `Included + Rejected == Offered` is asserted, which is
   the operational content of "never ambiguously omitted".
6. **RNG.** No RNG type existed anywhere in V1, so `RngStream` had to be introduced to have anything to persist.
   It is one 64-bit xorshift* with explicit state and draw count per declared stream. `RngStream.Restore` installs
   the recorded state verbatim (never re-mixing it) so the round trip is exact; the seed path mixes only to avoid a
   zero state. This is a determinism *representation*, not a cross-platform bit-exactness claim (`P-008`, `P-060`).
7. **`CaptureContext` is explicit rather than discovered.** A boundary cannot be read from storage alone: the scope
   tree, a target's recipe, the clock declarations and the next-step buffer set are *declared* facts. Supplying them
   once is also what keeps the reader free of reflection and marker scanning (04 §8).
8. **The command payload map is caller-supplied.** `RequestLedger` records a command's request identity, route,
   schema and input hash but not its bytes, so `CaptureContext.CommandPayloads` lets the admitting caller supply
   them; a command without a supplied payload is still recorded, with no payload. Recorded as a limitation in §7.
9. **`CheckpointCodecAdapter` binds delegates, not an interface.** The generated serializer classes do not implement
   any checkpoint interface and must not be edited to (they are GC-003 compiler output), so the adapter holds each
   generated instance's own `Serialize`/`TryDeserialize` method groups. Direct references, no reflection, no
   generated-file change.
10. **`TryExpose` lives on `UnityWorldRegistry`, not on the host.** Registry membership is the registry's authority;
   a host that added itself would be a second writer of that index.

## 7. Known gaps and assumptions

1. **Nothing has been compiled or executed.** The most likely first failures, in order: (a) a name or signature
   mismatch between the new Unity scenario and the frozen runtime types (the runtime side is fully written, so this
   would localise to the scenario); (b) the generated catalog's `Serialize`/`TryDeserialize` method groups binding to
   `Func<TValue, byte[]>`/`CheckpointDeserialize<TValue>` (a compiler confirms this, nothing else can);
   (c) an `Entity.Index` collision in the "different native indices" assertion if the two world creations happen to
   reuse indices — the scenario must assert the *slot/entity index* sets differ, and if a run legitimately produces
   equal indices the assertion must be reported rather than weakened.
2. **`O-22 RecoverWorld` and `GC-027`'s faulted-world composition are not delivered.** See §6 item 1. The restore
   path is the prerequisite and now exists.
3. **The observation lease (GC-016) is not consumed.** `ICommittedBoundaryReader` is the frozen seam and its shape
   does not depend on GC-016, so this task is not blocked on that sibling. When GC-016's bounded lease interface
   lands, `UnityCommittedBoundaryReader` is the single place that would take one for the duration of a read; this is
   stated in the file's header so the integration is one edit, not a redesign.
4. **`ContentRevisionCount` is carried but not resolved.** The header records how many immutable-definition
   revisions the document depends on; the restore's definition revision resolution is `GC-025`'s content-loading
   surface. A restore with `AllocatedSchemas` supplied validates schema versions; definition revisions are recorded
   evidence only in this change set.
5. **The queue-inclusion path is proven at the disposition and record level, not by executing the re-admitted
   command.** `P-053` requires the commands to be *included with ledger/cutoff*, which this change proves
   (records + cutoff + high-water). Actually re-admitting them into the restored world's plane is command ingress,
   which the W5 gate's adapter task owns; the scenario therefore asserts the recorded set and the restored
   high-water, and says so in the step detail.
6. **Physics/engine-internal state is out of scope by design.** `P-054` and 06 §7 both exclude it; no observation
   claims bit-identical continuation.
7. **The catalog verifier edit is a change to a tool owned by GC-003.** It was reported by the codegen task,
   authorized as a scoped correctness fix, and proved non-regressive by re-verifying both existing catalogs (§8). If
   the orchestrator prefers the tool untouched, the alternative is to widen the emitter's own verification path
   instead — recorded so the decision is visible.
8. **No status row in `artifacts/gates/w4-generic-profile/inventory.json` was promoted.** §9 lists the proposals;
   the build host promotes them after running.

## 8. What actually ran on this host

```sh
python3 tools/check_game_core_csharp.py
# checked 377 C# file(s)  ->  ok
python3 tools/verify_generated_catalog.py unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCheckpoint/CheckpointCatalog.g.cs
# 0 groups, 12 factories, 12 schemas, 1 features, 273157 bytes
# file hash and catalog fingerprint both recompute correctly from the generated tables
python3 tools/verify_generated_catalog.py                       # the committed probe catalog, unchanged
python3 tools/verify_generated_catalog.py unity/.../GeneratedCards/CardCatalog.g.cs
git diff --exit-code -- unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs
git diff --exit-code -- unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCards/CardCatalog.g.cs
bash -n tools/unity/run_gc018_probe.sh
```

Also run: a `.meta` GUID uniqueness scan over the whole worktree (zero duplicates); an independent recomputation of
both gate digest literals from the exported observation-name table; and a field-by-field parity check that each
record value type's constructor parameter order equals the generated value struct's field order for all twelve
schemas (both sides derived from the description, so a transposition would be caught).

None of that is a build, an import, a test or a player run.

## 9. Inventory proposals (for the build host to promote after running)

Proposed only; no row in `artifacts/gates/w4-generic-profile/inventory.json` was changed by this task beyond the
`gc018` revision-notes section.

| Id | Proposed status | Evidence this change set would need to have passed |
| --- | --- | --- |
| `P-053` | `Implemented+Evidenced` (from `Not yet`) | the checkpoint scenario player probe (`probe-gc018.json`) with `gc018-committed-boundary-capture` and `gc018-queued-commands-are-dispositioned-not-omitted` passing in both families and both catalogs |
| `P-054` | `Implemented+Evidenced` (from `Partial`) | `gc018-ambiguous-migration-rejects-restore` plus the migration-path fixture suite and `CheckpointCatalogTests` |
| `P-055` | unchanged (`Partial`) | this task adds the checkpoint feature id and the protocol gate, but protocol evolution across a **second** minor revision is not exercised |
| `P-004`, `P-005` | unchanged (`Partial`) | the checkpoint half improves, but the small-width counter-exhaustion boundary fixture (TEST-002) is still absent |
| `P-032` | unchanged (`Partial`) | dormant serialization is now exercised, but `GC-015`'s row also covers transfer/reset boundaries beyond this task |
| `O-20` | `Implemented+Evidenced` (from `Not yet`) | `gc018-committed-boundary-capture` + `gc018-capture-refuses-outside-a-boundary`, plus `CheckpointCaptureTests` |
| `O-21` | `Implemented+Evidenced` (from `Not yet`) | the four `gc018-restore-*` observations, including the old-callback probe and the unexposed-then-exposed registry counts |
| `O-22` | **not proposed** | still `Not yet`; `GC-027` composes it from this task's restore path (§6 item 1) |

## 10. Exact commands for the Linux build host

Everything runs from the repository root. Nothing below has been run.

### 10.1 Pure .NET

```sh
dotnet build dotnet/GameCore.sln -c Release
dotnet test  dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gc-018/trx
```

The new cases live in `GameCore.Contracts.Tests` (`CheckpointDocumentTests`, `CheckpointIdentityTests`,
`CheckpointMigrationTests`, `CheckpointRecordTests`, `CheckpointVersionedFixtureTests`),
`GameCore.Content.Compiler.Tests` (`CheckpointCatalogTests`) and `GameCore.Execution.Tests`
(`RngStreamTests`, `CheckpointCaptureTests`, `CheckpointRestorePlanTests`).

### 10.2 Unity Editor: generate and verify the catalog, then test

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
"$UNITY" -batchmode -nographics -quit -projectPath unity/GameCore.Validation \
  -executeMethod GameCore.Validation.Editor.ProbeCatalogGenerator.GenerateCatalog \
  -logFile artifacts/gc-018/unity/catalog-codegen.log
"$UNITY" -batchmode -nographics -quit -projectPath unity/GameCore.Validation \
  -executeMethod GameCore.Validation.Editor.CheckpointCatalogGenerator.GenerateCatalog \
  -logFile artifacts/gc-018/unity/checkpoint-codegen.log
git diff --exit-code -- unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCheckpoint/CheckpointCatalog.g.cs

"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode \
  -testFilter GameCore.Gc018.Tests \
  -testResults artifacts/gc-018/unity/gc018-editmode.xml \
  -logFile artifacts/gc-018/unity/gc018-editmode.log
```

Do not add `-quit` to a test-run command (04 §10).

`-testResults` with a relative path is resolved against the Unity **project** path, not the shell cwd (unlike
`-logFile`): the run above writes `unity/GameCore.Validation/artifacts/gc-018/unity/gc018-editmode.xml`. Pass an
absolute path or copy the file out afterwards. The card catalog regenerates the same way with
`GameCore.Validation.Editor.CardCatalogGenerator.GenerateCatalog`.

### 10.3 The GC-018 player probe (Linux IL2CPP, five runs)

```sh
UNITY="$UNITY" ARTIFACTS=artifacts/gc-018/toolchain tools/unity/build_probe.sh
PROBE_RUNS=5 ARTIFACTS=artifacts/gc-018/toolchain tools/unity/run_gc018_probe.sh
```

The probe asserts `"task": "GC-018"`, `"mode": "Gc018"`, `"result": "Pass"`, all 32 observation names per catalog
family plus the two digest steps, both digest literals, and the clause fragments named in the script.

### 10.4 Documentation validation

```sh
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
```

## 11. Requirement-to-observation cross-check for the gate owner

The 16 observation names, in order, are one contract between four artifacts (the scenario's `ObservationNames`, the
EditMode tests, the probe script's `probe_require_steps` list and the digest literals in `ProbeGc018.cs`). The two
digests are over the LF-joined `<label>/<name>=pass` lines with no trailing newline:

* narrative `f881469b2a2ba43e4c1bf7972913dd2c93f945a623fef770e00319409ece7ac0`
* cards `fe1aaae38982be120fe3c668742990504f984b54b053de3b2d7886ff899b5256`

They were recomputed independently on this host from the name table, not read out of the implementation.

## 12. Review findings fixed during authoring (recorded because they were found by reading, not compiling)

Five defects in this change set were found by cross-checking symbols against their declarations after the files were
written. All are fixed (commit `256b0f6` plus the scenario's import fix in `f251c0e`); they are recorded because each
is the kind of error a compiler would have caught first, and they name what to look at if the build still fails:

1. `CanonicalId32` was `internal` while `GameCore.Execution` and `GameCore.Unity.Runtime` both use it to convert a
   `ContentHash` to and from the four `UInt64` words the records carry. Now public, with its inverse documented.
2. `RestoreReservationLedger` named `WorldId`/`OperationId`/`ContentHash`/`DiagnosticCode` with no
   `using GameCore.Contracts;`.
3. `CaptureContext` was missing `GameCore.Execution.Persistence` and `GameCore.Composition`, and exposed no clock
   registry at all — so the wake rows `P-053` requires could not be read. It now carries an optional
   `PluginClockRegistry`.
4. `UnityCommittedBoundaryReader` was missing `GameCore.Composition` (for `CompositionState`, `ScopeRecord`,
   `InstallEntry`, `ConfigDocumentCodec`) and `GameCore.Unity.Runtime.Integration` (for `LiveTarget`).
5. `Gc018Scenario` was missing `using GameCore.Execution;` for `IdSequence` — the one the sibling `W4GateScenario`
   has and the copy dropped.

Two behavioural corrections:

6. `CheckpointDocument.TryRead` accepted bytes appended after the trailing checksum. It now compares the reader's
   position with the document length and refuses with `ResourceUnavailable`, matching the envelope rule that the
   checksum is a document's last record. The contract test that had asserted the old behaviour was corrected with it.
7. Imports that named nothing in their file were removed, so no file carries a dependency its content does not use.

Host-side detection method, for reuse: index every `public`/`internal` type declaration by namespace across
`Packages/`, then for each new file check that every referenced type name is reachable from its `using` set or its own
enclosing namespaces, and that no referenced `GameCore.*` type is `internal`-only. That check reports zero findings
across every file in this change set now; `python3 tools/check_game_core_csharp.py` does not perform it.

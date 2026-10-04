# P1.2 save-restore — packet report

Branch `worktree-agent-af7f4821f2e82a7c3` (based on `main` at `86a9482`). Owner: Opus 5.5.
Scope: SADR-012 (studio) in the kernel and plugin catalog row 12 (`com.gamecore.gameplay.save` plus the
`com.gamecore.unity.app` SaveService). Everything below was compiled and run on `myubuntu` through
`studio/tools/*`. Nothing was compiled, built or tested on the Mac.

## 1. What was built

### 1.1 Production restore builder (`com.gamecore.unity.runtime`, `Runtime/Persistence/Production*.cs`)

- `ProductionRestoreTargetBuilder : IRestoreTargetBuilder, IRestoreOutboxBuilder`. It is the O-21 builder for
  real games and is driven by the manifest and the recipe catalog. A `IProductionWorldComposer` composes the
  staging world. `SaveRestoreComposer` is the composer for an application root: it calls the root's own
  `GameApplicationRoot.TryCompose` with no boot script, the captured scope tree as the lane seed, and a target
  capacity of at least the captured count.
- `TryBuild` works in this order:
  1. It refuses a plan that still carries migrations (`MigrationNotApplied`). Slot migrations run before
     planning (1.2).
  2. It refuses a temporal model mismatch.
  3. It checks recipes at the exact captured revision. An unknown recipe is `RecipeMissing`/`MissingDependency`.
     A revision change is `RecipeRevisionMismatch`/`StalePlan`, with a detail naming the recipe, both
     revisions and the target.
  4. It creates the unexposed world with the restored temporal origin (`UnityWorldHost.TryCreateUnexposed(..., origin, ...)`).
  5. It composes, then seeds targets and their base layout.
  6. It replays the composition: root boundary, installs, per-scope imports, and mode. Then it writes slot rows (dormant rows included) and prunes rows the capture did not carry. Rows are written after the replay because an install preflight checks live slots against the declared descriptors (P-032).
  7. It rebuilds plugin clocks and wakes, then restores RNG streams.
  8. It restores next-step buffer rows, rebased onto the restored step.
  9. It re-admits queued commands, recording their payloads.
  10. It reinstates the outbox and proves it (`OutboxConsistency.Verify`).

  A refusal after compose abandons the composed world and disposes the staging world.
- `ProductionWorldModules`: clocks, RNG table, next-step buffers, command payloads, and the delivery owner of
  one world. The capture context reads the same modules (`SaveService.CreateCaptureContext`), so every row the
  builder restores is a row the capture writes.
- `ProductionRestoreReport`: refusal, code and detail, the origin, every restored count, publications,
  no-change edits, and per-phase milliseconds. `Describe()` prints all of these.
- `SaveRestoreHook : IGameApplicationRestoreHook`. It fills the P0.4 restore seam, so
  `GameApplicationRoot.TryCreateRestoreTargetBuilder` returns the production builder.

### 1.2 Executable forward slot migrations (`Runtime/Pure/Persistence/SlotMigrationRegistry.cs`, engine-free)

- `SlotMigrationStep` is a pure, id-keyed step: a stable-name id, then `From` and `To` of one schema with
  `To > From`, then `Func<int,int>` or a whole-row `SlotRecordTransform`.
- `SlotMigrationRegistry` validates steps through the contract migration graph. It records a rejection for a
  duplicate id, a backward step or a cross-schema step.
- `SlotSchemaCatalog` maps (owner, slot) to its current schema. Conflicting bindings are recorded.
- `SlotMigrationExecutor.Migrate(slots, catalog, registry)` and `MigrateDocument(...)` refuse with a code and a
  hint:

  | Case | Refusal | Code |
  | --- | --- | --- |
  | No path | `MigrationPathMissing` | `MigrationRequired`; the hint is "save needs a migration for &lt;schema&gt; vN -> vM; …" |
  | A newer row | `Downgrade` | `UnsupportedVersion` |
  | Two chains | `AmbiguousPath` | |
  | A throwing or refusing step | `TransformRejected` | |
  | A malformed registry or catalog | `InvalidDeclarations` | |

  A refusal returns no rows. `MigrateDocument` rewrites only the slot records through
  `CheckpointDocument.TryRewrite`; every other record is carried byte for byte. An unchanged document is
  returned as the same byte array.
- `CheckpointCatalogCompatibility.Decide` (the compatibility rule in `CheckpointRestorePlan.cs`) returns
  Identical, DeclaredCompatible or Incompatible. A save from a declared compatible catalog restores with
  `requireCatalogMatch: false`.

### 1.3 Temporal continuity

- `Packages/com.gamecore.contracts/Runtime/Serialization` (additive):
  - container feature `CheckpointFormat.TemporalContinuityFeatureId`, derived from the stable name
    `gamecore.checkpoint.feature.temporal-continuity.v1`;
  - `ReadableFeatureIds`, `TemporalContinuityFeatureIds` and `IsWritableContainerFeatureSet`.
  - `KnownFeatureIds` is unchanged: V1 writers produce byte-identical documents.
- `CheckpointSerializer(codecs, limits, features)` declares features. `TryAddEncoded` carries an
  already-encoded record after codec validation.
- `CheckpointDocument` gains `DeclaredFeatureIds`, `DeclaresTemporalContinuity` and `TryRewrite(slots?, features?)`.
- `RestoredTemporalOrigin` holds the step, debt ticks, domain seconds, issuer high-water marks, and `Source`
  (Fresh, Continued or LegacyStepZero) with `Detail`. `FromCheckpoint` and `TryFromDocument` build it.
- `UnityWorldHost.TryCreateUnexposed(request, registration, origin, out host)` applies the origin before the
  initial publication:
  - It sets the current step and domain seconds.
  - Debt is FixedStep only. Restored debt is owed at the first running sample: `RetainedDebt` includes it
    until then.
  - `RestoredOrigin` and `PendingRestoredDebtTicks` are exposed.
  - The world always gets a new `WorldId`.
- `WorldAdapterFrame` resumes its device source's sequence above the restored high-water mark
  (`ResumedFromSequence`, `DeviceSequence`, `ResumeIssuerSequence`, never backwards).
- A document without the feature (every V1 writer) restores at step 0 with zero debt and domain time. The
  origin reports `LegacyStepZero`, with a detail naming the feature and the captured step.

### 1.4 SaveService (`com.gamecore.unity.app`, `Runtime/Save*.cs`)

- `SaveService(root, SaveServiceOptions)` writes to `Directory`, which defaults to
  `Application.persistentDataPath/saves`.
- `Capture(slot, thumbnail?)`:
  1. checks that the world is safe;
  2. pauses a running world;
  3. reads the committed boundary with the service's modules (clocks, RNG, next-step buffers, command
     payloads, outbox), using `CaptureAndPublish` into memory;
  4. declares the continuity feature, then resumes the world;
  5. writes `<slot>.gcc` atomically (`FileCheckpointStore`), then `<slot>.json` (`.partial` +
     replace/move).

  The JSON header has: format, slot, game id, catalog fingerprint, schema versions, region, play time, UTC
  timestamp, thumbnail path, logical step, document hash and length, and the continuity flag.
- `Restore(slot)`:
  1. reads the header and document, then checks game id, hash and length;
  2. checks that the world is safe;
  3. decides catalog compatibility;
  4. runs the slot migrations;
  5. runs the O-21 restore through `ProductionRestoreTargetBuilder`;
  6. only on success, stops the old root and sets `ActiveRoot` (`RootChanged`).
- Also: `Delete`, `ListSlots`, `Exists`, `Inspect` (verify and preview the migration; no restore),
  `TestRoundTrip` (non-destructive), and `SlotHashOf` (canonical slot hash).
- Refusals are `SaveRefusal{Code, CodeId, Hint, KernelCode, Detail}`:
  - `save.missing-slot`, `save.corrupt-file`, `save.catalog-mismatch`, `save.migration-path-missing`;
  - `save.unsafe-state`, `save.invalid-slot`, `save.capture-failed`, `save.storage-failed`;
  - `save.restore-refused`, `save.newer-build`.
- `SaveSlotHeader` and `SaveSlotNames` are System-only and are also compiled by the dotnet Execution tests.

### 1.5 `com.gamecore.gameplay.save` (new) and `com.gamecore.rules.gameplay/Runtime/Save`

- `SaveSchemaDefinition` is a ScriptableObject (`[Authorable("save.schema")]`) with:
  - slot schemas (owner/slot/schema stable names, current version);
  - migration ids (schema, from, to);
  - compatible catalogs.

  Its methods are `Validate`, `ToSlotSchemaCatalog`, `ToSchemaVersions`, `ToCompatibleCatalogs`,
  `BuildMigrations(bodies, out problems)` (it names declared ids that have no body) and `ApplyTo(options, bodies)`.
- Commands are `SaveCommand` `save.capture{slot}`, `save.restore{slot}` and `save.delete{slot}`. They are
  handled at host level by `SaveCommandHost`, which raises `SaveWritten`, `SaveRestored` and
  `SaveRefused{Code, CodeId, Hint, Detail}`.
- `ISaveSlotCatalog` is implemented by `SaveServiceSlotCatalog`: slots, unreadable slots, `TryGet` and `Refresh`.
- Studio operations are `[AuthorOperation("save.inspect")] Inspect(schema, slot, service)` and
  `[AuthorOperation("save.testRoundTrip")] TestRoundTrip(schema, service)`.
- `GameCore.Rules.Gameplay.Save` (engine-free) has:
  - `SaveGateRules` (`MayCapture`/`MayRestore` over `SaveGateState`, code `save.unsafe-state` plus a hint);
  - `SaveSlotPolicy` (`quick`, `slot-N`, `auto-N`, `NextAutosave` overwrites the oldest).

### 1.6 Docs

- `docs/game-core/10-decisions-and-open-questions.md`: a "SADR-012 (studio)" row.
- `docs/operator/checkpoint-and-recovery.md`: §7 "Production restore and saves" (additive).

## 2. Verification

The tests were run on the host clone `~/wkspace/gc-studio/p1.2`. The host results reflect this branch at
`5efec0f`; the later commits change only `PACKET.md` and the regenerated `packages-lock.json`. Logs and XML are
in `~/wkspace/gc-studio/p1.2/.unity-logs/`.

| Suite | Command | Result | Duration |
| --- | --- | --- | --- |
| dotnet Contracts | `studio/tools/dotnet-test.sh p1.2 dotnet/tests/GameCore.Contracts.Tests` | **173/173** (new: `CheckpointTemporalContinuityTests`) | 160 s wall |
| dotnet Execution | `studio/tools/dotnet-test.sh p1.2 dotnet/tests/GameCore.Execution.Tests` | **178/178** (new: `SlotMigrationTests`, `SaveSlotHeaderTests`, `SaveRulesTests`) | 74 s wall |
| dotnet solution | `studio/tools/dotnet-test.sh p1.2` (`dotnet/GameCore.sln`, 18 test projects) | **1419/1419** | 70 s wall |
| Unity EditMode, new tests | `studio/tools/unity-compile.sh p1.2 unity/GameCore.Validation --tests EditMode --filter 'GameCore\.Persistence\.Tests\..*'` | **17/17** | test run 0.77 s; 35 s wall (`…editmode-20261005T060109-a1.xml`) |
| Unity EditMode, full | same, no filter | **1312/1312** (P0.4 baseline 1295 + 17 new); GC-018 3/3, GC-027 22/22 | test run 320.8 s; 526 s wall (`…editmode-20261005T060237-a1.xml`) |
| Unity PlayMode, full | `… --tests PlayMode` | **84/84** (baseline 84) | 215 s wall (`…playmode-20261005T061229-a1.xml`) |

Earlier attempts:
- EditMode `…052611`: one compile error in the new test file (a missing `using GameCore.Execution`). It was a
  fresh-Library import, 1132 s wall. The production packages compiled.
- EditMode `…054814`: 8/17 passed. Every restore was refused by the install replay's preflight. Slot rows had
  been written before the composition replay, and the preflight checks live slots against the declared
  descriptors (P-032). With more than one installation, a production save would have hit the same refusal: a
  row owned by a later install is seen by an earlier one. Slot rows are now restored after the replay (step 5b),
  and the prune runs after them.

The new EditMode tests (`unity/GameCore.Validation/Assets/GameCore.Validation/Tests/Persistence/`) run on the
P0.4 application probe:
- **Round trip.** Capture, then restore, then capture again. The canonical slot hash is equal. Step 3 and
  domain time 2.5 s continue, and the next committed step is 4. The `WorldId` is new and the previous root is
  stopped. There is 1 publication.
- **FixedStep.** A retained debt of 50 ticks is restored. It is owed at the first running sample: 50 + 60 ticks
  admit exactly one 100-tick step, leaving 10.
- **Issuer continuation.** A saved high-water mark of 41 is restored. A `WorldAdapterFrame` resumes at 41 and
  never moves backwards. An unrelated source starts at 0.
- **Migrations.** A v1 save restored by a v2 build is migrated (×10). A v3 build without a path refuses with
  `save.migration-path-missing`, `MigrationRequired`, and a hint naming `… v1 -> v3`; the running world is
  untouched.
- **Other refusals:**
  - corrupt document → `save.corrupt-file` (also from `Inspect`);
  - mismatched header and document → `save.corrupt-file`;
  - missing slot, invalid name, and delete;
  - foreign catalog → `save.catalog-mismatch`, but restores when the fingerprint is declared compatible;
  - recipe revision 77 → `RecipeRevisionMismatch`, naming the recipe and both revisions;
  - stopped root → `save.unsafe-state`.
- **Legacy.** A V1-feature document loads at step 0 with domain 0. `LegacyStepZero` is reported, its detail
  names the feature, and the slots are restored exactly.
- **gameplay.save.**
  - Commands raise one event each, and `SaveRefused` carries `save.missing-slot` with a hint.
  - The slot catalog lists the slots and reports an unreadable header.
  - `save.inspect` and `save.testRoundTrip` leave the running world untouched.
  - `SaveSchemaDefinition` builds the catalog and names a declared migration that has no body.
- **Restore hook.** The seam returns the production builder.
- **200 targets.** See §4.

The dotnet tests cover:
- the feature id derivation;
- writable feature sets;
- legacy versus continued origins;
- refusal of an unknown feature;
- byte-preserving `TryRewrite`, and refusal of a slot-count change;
- slot migrations: v1→v2, chains, current rows, missing path, downgrade, ambiguity, a throwing step, registry
  rejections, catalog conflicts, and the `MigrateDocument` rewrite and no-op;
- header JSON: round trip, null thumbnail and `ulong.MaxValue`, unknown keys, 13 malformed cases, newer
  format, slot names;
- gate rules and the slot policy.

Static checks (interpreter-only), all passing:
- `tools/check_package_metadata.py`, after copying back the host-regenerated validation `packages-lock.json`;
- `check_game_core_csharp.py`;
- `check_contract_surface_parity.py`;
- `check_gate_sources.py`;
- `check_link_xml.py`;
- `check_operator_docs.py`;
- `validate_game_core_docs.py`.

`hollowmere` was not compiled separately. It references `unity.app`, whose sources compiled in the validation
project, and it does not reference `gameplay.save`.

The round trip is covered in EditMode only, on the headless application probe. No PlayMode persistence test
was added; PlayMode ran as a regression suite.

## 3. API for downstream packets

**P1.1 (gameplay entities/world)**
- To restore a game's state, give the definition `WithRestoreHook(new SaveRestoreHook(clocks, maxWakes, nextStepBuffers))`,
  or construct `SaveService(root, options)`.
- Bind every owner slot that a schema version governs: `SaveSchemaDefinition.slotSchemas`, or
  `SaveServiceOptions.SlotSchemas`.
- A version bump needs a `SlotMigrationStep` body passed to `BuildMigrations`/`ApplyTo`. Otherwise old saves
  refuse with `save.migration-path-missing` and a hint.
- Recipes are restored only at their exact revision. Bumping a recipe revision makes old saves
  `save.catalog-mismatch` (`RecipeRevisionMismatch`).
- Region residency: put the current region in `SaveServiceOptions.RegionId`.
- Gate saves with `SaveGateRules` before issuing `save.capture`.

**P1.5 (input/adapters)**
- After a restore, create adapter frames over `SaveService.ActiveRoot.Host`. A `WorldAdapterFrame` built over
  a restored host resumes its device sequence automatically.
- `ResumeIssuerSequence(mark)` is public for custom sources.
- Subscribe to `SaveService.RootChanged` to rebind. `GameApplication.Current` still names the booted root
  (its setter is private).

**P1.6 (Studio Unity side)**
- `save.inspect` and `save.testRoundTrip` are `[AuthorOperation]`s on `SaveStudioOperations`.
  `SaveSlotInspection` and `SaveRoundTripReport` are plain result objects.
- For the domain-reload checkpoint (02-architecture), use `SaveService.Capture` before the compile and
  `Restore` after it. The restored world continues the step, debt and domain time and gets a new `WorldId`.

**P2.4**
- `ProductionRestoreReport` per restore (counts, publications, per-phase milliseconds).
- `SaveResult.Milliseconds` per capture and restore.
- `SaveService.SlotHashOf` for equality checks.

## 4. Batched vs per-target restore

**Batched (what is built).**
- The captured scope tree is the lane seed. Isolation and exclusions are included, and this costs 0
  publications.
- Targets and slot rows are written directly through `LiveTargetSeeder`, and base layouts through the recipe
  applier. This also costs 0 publications.
- Each of the following costs exactly 1 publication: each installation, each scope with imports, a differing
  root boundary, and a differing mode.

Targets are seeded before the install replay, so each install publication derives over all of them once.
Slot rows are written after the replay.

**Per-target (rejected).** Spawning each target through its own composition edit would cost one publication
per target. Each of those publications derives over every target already present.

**Measurement.** One run of `TwoHundredTargetsRestoreWithPublicationsIndependentOfTheTargetCount` was made, in
EditMode on `myubuntu` (full-suite XML `…editmode-20261005T060237-a1.xml`). PROBE_RUNS=1; this is not a
benchmark.

| Restore | Targets / slot rows | Publications | Builder total | Install replay (the one publication) | Seed | End-to-end `Restore` |
| --- | --- | --- | --- | --- | --- | --- |
| round trip | 1 / 2 | 1 | 1.31 ms | 0.67 ms | 0.03 ms | — |
| 200 targets | 200 / 400 (200 dormant) | **1** | **47.7 ms** | 45.7 ms | 1.18 ms | **61.3 ms** |

- The 200-target capture took 17.2 ms and wrote a 139,117-byte document.
- In the filtered run (`…060109`), the same test measured 84.4 ms end to end, with 71.5 ms builder total and
  69.6 ms replay.
- Seeding 200 targets and 400 rows costs about 1.2 ms. Almost all the time is the single install publication,
  which derives over 200 targets.

The per-target path was not built, so its cost is an estimate, not a measurement:
- Lower bound: 200 publications at the 1-target cost of 0.67 ms, which is about 134 ms.
- Expected: publication cost grows with the number of live targets, from 0.67 ms at 1 target to 45.7 ms at
  200. The sum is about 200 × 23 ms, which is about 4.6 s.

Batching keeps the publication count at 1 for any target count.

## 5. Open items

- `GameApplication.Current` is not moved to the restored root, because its setter is private and the root is
  not restructured. `SaveService.ActiveRoot` and `RootChanged` stand in for it.
- The `RequestLedger` issuer high-water dictionary is not seeded on restore (not this packet's file). Queued
  commands are re-admitted and adapter frames resume device sequences above the mark. A non-adapter issuer
  must read `RestoredOrigin.TryGetIssuerHighWater` itself.
- Recipe and clock schema changes have no executable migration. Only slot rows migrate. A recipe revision
  change refuses.
- Games have no shared generated checkpoint codec catalog yet. `SaveServiceOptions.Codecs` is supplied by the
  game (the tests use the GC-018 generated set).
- `com.gamecore.rules.gameplay`: this branch adds only `Runtime/Save/SaveRules.cs` (+ meta, `Save.meta`) and
  an asmdef byte-identical to P1.1's. `package.json`, `Runtime.meta` and the asmdef meta come from P1.1. The
  integrator reconciles them, and P1.1's dotnet project will then also compile `Save/SaveRules.cs` (System only).
- `unity/GameCore.Validation/Packages/manifest.json` gained `com.gamecore.gameplay.save`,
  `com.gamecore.studio.core` and `com.unity.nuget.newtonsoft-json` 3.2.1, which the tests need. That is a path
  outside the exclusive list, and the packages-lock is regenerated by Unity on the host. hollowmere does not
  reference `gameplay.save` yet.
- Plugin clock driving (`WorldTimeDriver`) for application worlds is still left to the game. The builder
  restores clocks into `ProductionWorldModules.Clocks`.
- The `.gcc` and `.json` writes are each atomic but not jointly atomic. A torn pair is detected by the header
  hash and reported as `save.corrupt-file`.
- Slot rows are written after the composition replay, so a restored row that no current descriptor declares is
  restored as captured rather than refused. Its schema binding (`SlotSchemaCatalog`) is the only check. The
  next edit's preflight in the restored world reports it (P-032). A restore-time descriptor check over the
  final assembly would be a small follow-up.
- No component-state capture and no save UI (non-goals).

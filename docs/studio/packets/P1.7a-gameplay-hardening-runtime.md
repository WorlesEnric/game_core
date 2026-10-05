# P1.7a gameplay-hardening (runtime correctness)

Branch `worktree-agent-a4c0e77e640321d4a`, based on main `168c93f`. Owner: Opus 5.5. Nothing was compiled, built or tested on the Mac. Every run went through `studio/tools/sync-to-host.sh --wip p1.7a` and `studio/tools/{unity-compile,dotnet-test}.sh p1.7a …` on `myubuntu`, holding at most one Unity instance at a time. No perf benchmarks were run.

## 1. What was built

The independent review found three blockers: the narrative layer did not survive `SaveService.Restore`, the region streamer did not reconcile restored residency, and gameplay truth lived outside int32 slots. Below is how each architect decision (A1–A11) and each coordinator addition was applied.

### Delivery and restore (A1, A2)
- **One delivery owner per world.**
  - `NarrativeDelivery` no longer creates its own `WorldDeliveryOwner` when one is handed to it. `NarrativeDelivery.FactoryFor(world)` and `NarrativeDelivery.Configure(SaveServiceOptions, world)` set `SaveServiceOptions.DeliveryFactory`. The factory returns the narrative delivery's own owner for the booted root and a new narrative owner (`CreateOwner`) for a restored root.
  - The save service therefore captures and restores the narrative outbox. A test asserts `SaveService.Modules.Delivery` is the same instance as `NarrativeWorld.Delivery.Owner`.
- **Obligations are created inside the committing step.**
  - The world now has a step tap: `GameplayWorld.StepTap`, `IGameplayStepTap`, `GameplayStepTaps.Of`.
  - Every kernel that commits an event hands it to the tap in the same step, through `StepEventBatch.CommitAll(plane, message, tap)` and Interaction's `tap.OnCommitted`. The narrative delivery (`OnCommitted` / `OnActionDemand`) turns these into outbox obligations in that step: `RewardGranted`, `ActionDue`, rule triggers (logic evaluation), signals (Talk / Interact / quest-objective), pickups and action sets.
  - A save taken right after the committing pump therefore holds the open obligations. W-PERSIST-01 captures in exactly that frame.
- **Deduplication uses the outbox obligation id**, not an 8-entry ring.
  - Obligation ids are deterministic: `narrative.obligation.<worldId>.<step>.<ordinal>`. They are unique across restores because a restored world resumes at the captured step (SADR-012).
  - Destinations claim an obligation through the tap (`Claim`, which scans `OpenObligations`) and settle it in the applying step (`Settle` → `Adapter.TryAcknowledge`). A redelivery of a claimed or terminal id answers `IdempotencyConflict`, which the port maps to `AlreadyApplied`.
  - Request ids are negative for obligations (`GameplayRequestIds.OfObligation`) and positive for commands (`GameplayRequestIds.OfCommand(issuer, step, serial)`).
- **Ports covered**, each with its id count:
  - grant 3, consume 3, pickup 2, buy 4
  - set-fact 3, dialogue-start 4
  - quest-start / quest-complete / quest-fail 1, quest-advance 2, quest-objective 3
  - logic-evaluate 6, run-actions 4
  - entity-spawn / entity-despawn / entity-set-variant 2
  - world-travel 3, player-restore-stamina 2
  - play-audio and show-message (presentation)
- **`NarrativeCommandPort` outcomes:**
  - A pending submission answers `Unavailable`.
  - `Committed` is resubmitted after `RetryPasses`.
  - `IdempotencyConflict` → `AlreadyApplied`.
  - `BudgetExceeded` → deferred retry.
  - Any other rejection → `Refused`.
  - `Reinstate` clears the claims and each port's pending submissions, because the replaced outbox rows are new work to the ports.
- **Narrative restore path:**
  - `NarrativeComposer.Attach(GameApplicationRoot, WorldBuildPlan, content, modules, seedSlots, owner)` and `Attach(GameplayWorld, …)` re-attach the logic, inventory, quest and dialogue systems, the host, the delivery and explain on a new root.
  - `NarrativeComposer.AttachRestored(SaveService, GameplayWorld, content, modules)` attaches on the save service's restored owner.
  - `NarrativeComposer.TryBoot(…, out NarrativeWorld?, out string failure)` was added.
  - `GameBoot.UseSaves(service)` goes through `HollowmereUiAudio.UseSaves(service, PrepareRestored(service))` when the UI rig exists, otherwise through `SaveService.RootChanged` directly. It shuts down the old base world, attaches the restored one, re-attaches the narrative layer, and reinstalls the P1.3 sessions and P1.5 presentation (`Install`).
- **Runtime-spawned targets:**
  - They are re-registered from `root.Targets`, mapping recipe → definition (`GameplayWorld.RestoredRuntimeTargets`).
  - The spawn ordinal is the world-scope slot `world.spawnOrdinal` on the anchor region target, so a spawn after a restore gets a fresh identity.

### Streaming (A3)
- `RegionStreamer` reconciles on its first tick after an attach (`StreamingRules.Reconcile`):
  - committed Resident with no scene loaded → load the scene;
  - committed Unloaded with a scene loaded → unload it.
- A failed load backs off (`StreamingRules.BackoffFrames`). After `MaxLoadFailures` (3) consecutive failures it latches with GP-WLD-032. `ResetLatch` re-arms it.
- In the Editor, a region scene that is missing from `EditorBuildSettings` is reported once with GP-WLD-031.
- The streamer exposes `Reconciliations`, `LatchedRegions` and `Diagnostics`.

### Pose authority (A4) and coordinator addition 1
- `world.posX/Y/Z/yaw` is the authoritative pose of every entity.
  - The player and NPC kernels read it for every decision and write it in the same step.
  - `player.pos*` and `npc.pos*` remain, but only as derived mirrors that nothing reads for decisions. The NPC kernel uses the mirror only as a fallback when there is no world pose.
- `world.place` on the player or an NPC is adopted on the next update. A standing NPC's target becomes the new spot (`NpcKernel.Adopt`, `Adoptions` counter).
- `world.place` is restricted to the host issuer and the Studio issuer (`WorldModule.StudioIssuer`). Any other issuer is refused with GP-WLD-022.
- Every world refusal records an explain entry: `WorldModule.RecentRefusals`, `LastRefusalCode`, and the stable codes in `WorldRefusalCodes` (GP-WLD-010..017, 022/023, 030..032, 040).
- **Portal condition:**
  - `Travel` evaluates the portal's `conditionRef` through `IConditionEvaluator` (`world.Worlds.Conditions = narrative.Conditions`).
  - False → GP-WLD-013, Unknown → GP-WLD-014, each with an explain record.
  - The seam is `ManifestPortal.conditionRef` (default empty = always allowed) until P1.7b's `PortalDefinition` field is baked into it.
- **Quest prerequisites and fail-closes-dependents:**
  - `QuestModel` gains `Prerequisites` and `Dependents`.
  - `QuestRules.PrerequisitesMet` refuses a start with `QuestRefusal.PrerequisitesUnmet = 5`.
  - `QuestRules.DependentsOf` + `CloseDependent`: a quest fail closes its dependents in the same event batch.
  - `QuestKernel` counts `PrerequisiteRefusals` and `DependentsClosed`.
  - The model is populated by the content converter once P1.7b declares the fields (see section 5).

### Time base (A5)
- `GameplayClock` (contracts): `StepTimeMs(step, stepMs)` and `NowMs32(temporalModel, step, stepMs, domainSeconds)`.
- Command-driven worlds use `step × stepMs`. Fixed-step worlds read the domain clock. The file header documents that the root advances domain time only for fixed-step worlds.
- Logic cooldowns and time conditions (`NarrativeState.NowMs`) and NPC schedules use it.

### Slots, request ids, occupancy, retries (A6) and coordinator addition 2
- **Vertical motion:**
  - New slots `player.verticalSpeed` (mm/s) and `player.grounded`, integrated in `PlayerRules` (gravity, take-off at `PlayerRules.JumpSpeed(g, h)`, terminal speed). The move carries an `AirborneFlag`.
  - `PlayerLocomotion` keeps the CharacterController for collision only, moving by the committed vertical speed, and reports `airborne`.
  - The binder half is `IsActive => !BinderEnvironment.IsHeadless`.
- **Occupancy:** `interact.occupants` is a per-actor bitmask (`OccupancyRules`). "Was inside" is derived from the committed bit, not from binder memory.
- **Request ids:** inventory, quest and dialogue use `NarrativeSubmitter.NextRequestId()` = `GameplayRequestIds.OfCommand(issuer, step, ++serial)`. Two hundred grants get 200 distinct ids, and ids issued after a reload never collide with the ring.
- **`failSubmitted` retry:** the quest tracker resubmits a fail every `ResubmitFrames` with a fresh request id until it is committed.
- **`player.restoreStamina{amount}` (route `player.route.restore-stamina`):**
  - `PlayerRules.RestoreStamina` clamps to `staminaMax`. When nothing changes it returns a typed refusal: `PlayerRefusal.StaminaUnchanged` (already full), or `PlayerRefusal.InvalidAmount` for a non-positive amount.
  - Issuers: host, or the logic port (`player-restore-stamina`, exactly once through the obligation id).
- **New `ActionKind` values:** `RestoreStamina = 17 {amount}` and `Buy = 18 {vendor, item, count}`. Both go through the narrative delivery ports (`player-restore-stamina` on the actor's entity target, and `buy` on the player inventory), exactly once like the other ports.

### Residency-aware binders (A7)
- `PrefabViewBinder` pools views per region and does not instantiate views for non-resident regions (`RegionResidencySet`).
- `AnimatorBinder`, `NavMeshAgentBinder`, `NpcAnimatorBinder`, `NpcBubbleBinder` and `InteractableBinder` implement `IResidencyAware` and skip non-resident regions.

### Recipe revision vs saves (A8)
- `DefinitionCanonicalizer` computes two stamps:
  - a **content** stamp over every field;
  - a **structural** stamp over the structural fields only.
- The per-type structural lists, documented in the file header:
  - `entity.definition`: prefab, variants (structural stamp of each), interactionKind, overridableFields.
  - `entity.variant`: prefab.
  - A type with no list is fully structural.
- The recipe revision (`ManifestDefinition.Revision`, the catalog recipe implementation id, the bake report) derives from the structural stamp.
- A cosmetic or tuning edit therefore keeps the revision, and a structural edit makes a restore refuse with `RecipeRevisionMismatch`. W-PERSIST-02/03 cover both.

### SADR-013 tuning (A9)
- Numeric tuning is Live:
  - `PlayerSession.Retune()` re-reads `definition.ToTuning().WithVertical(...)`;
  - `NpcModule.RetuneAll()` and `InteractionModule.RetuneAll()` re-read each record's profile.
- Option (a) declaration binding is left to the declaration owner (section 5). Every other field stays Rebuild. The decision row in `docs/game-core/10-decisions-and-open-questions.md` was updated.

### Bake (A10)
- `Entry.Verify` and planning never mint GUIDs, never call `SetDirty` and never save.
- `Plan` refuses un-minted or malformed ids with GP-ID-001/002.
- `DefinitionCanonicalizer.Validate` refuses references that are neither assets nor authored objects.
- `NarrativeBake` follows the same rules.
- `ChangedFiles` is sorted ordinal and holds each path once.
- `.gitattributes` pins `*.catalog.json`, `*.g.cs`, `*.bake.json`, `*.manifest.json`, `*.asmdef` and `*.json` to `eol=lf`.

### GameBoot and GameApplication (A11)
- **GameBoot:**
  - uses `TryBoot`;
  - an `InvalidOperationException` or `ArgumentException` during `Install` disposes the player session and shuts the narrative world down, which stops the root;
  - `ConfigureSaves(options)` is added.
- **GameApplication:** `ResetSessionStatics()` runs at `RuntimeInitializeOnLoadMethod(SubsystemRegistration)` and resets `Current`, `LastFailure`, `BootCount`, `FailureCount` and `BootFailed`.
- **SaveService:** `RebindAdapterFrame(kept)` runs on a refused restore, on a failed round trip, and in `TestRoundTrip`'s `finally`. It runs the definition's adapter-frame factory for the kept root, so the shared gameplay presentation frame binds back. `FrameRebinds` counts this.
- `GameApplication.Register` from `SubsystemRegistration` is **not** wired (section 6).

### Player walk-speed regression (coordinator follow-up after P2.1's evidence)

**Finding: no regression in the player kernel or input; the evidence measured the main menu.**

P2.1's evidence (`artifacts/studio/evidence/P2.1/16-play-mode.png`, step 16) reported "W held 2.5 s moved the player 0.25 m". The cause is that `StudioUiEvidence` step 15 enters Play mode and queues W while P1.5's UI is still on its boot screen, the **main menu**:
- `UiRuntimeOptions.StartScreen = UiScreen.Menu`.
- `ScreenFlowRules.PausesGameplay(Menu)` is true.
- `UiAudioBootstrap.PauseGatedIntentSource` therefore drops all movement.

The evidence never dispatches `newgame`. It also measures `FindAnyObjectByType<CharacterController>()`, but both the resolver rig (`[Player Rig]`) and the `Player.prefab` view carry a CharacterController, so it reads whichever is found first rather than the committed pose. The 0.25 m reading is exactly one clamped move (2.5 m/s × the 100 ms move window), which is consistent with rig settling noise rather than walking.

Reproduced headlessly in `Tests/P1_7a/PlayMode/PlayerWalkSpeed.cs`, booting the real `Boot.unity` / `GameBoot` with the P1.5 UI and audio:

| Phase | Setup | Result |
|---|---|---|
| A | main menu, W via a queued Input System keyboard state, 60 frames | **0.000 m**; the gated move is zero (P1.5 design) |
| B | HUD, W via the Input System | 0.000 m (diagnostic only); batchmode has no focused game view (`Application.isFocused` false), so a queued keyboard state never reaches the action map |
| C | HUD (after `newgame`), scripted W behind the **real** pause gate, 150 steps × 20 ms, CharacterController resolver | **7.550 m of 7.500 m** expected (151 resolutions, 0 ungrounded, 0 refused) |
| D | same as C, kinematic resolver | 7.550 m of 7.500 m |
| E | 182 ms frames (P2.1's mean frame), 14 frames | 3.750 m; the per-move clamp `speed × moveWindow` caps it, against 6.37 m at wall-clock speed |

Asserted: A < 0.05 m; C ≥ 90 % of walk × time; D and E within 10 %.

**Files that must change (not mine):**
1. `games/hollowmere/Assets/Hollowmere/Tests/P2_1/Editor/StudioUiEvidence.cs`, step 15 (P2.1 / integrator):
   - dispatch `UiAudioBootstrap.RigOf(boot.gameObject).Ui.Dispatcher.Dispatch("newgame")` (or press New Game) and wait for `UiScreen.Hud` before queueing W;
   - measure the player's committed `world.posX/posZ` slots (`GameBoot.PlayerExtension.Player`) instead of the first CharacterController found.
2. Optional, P1.5 / P3.1 (product decision): start the Studio Play viewport on the HUD (`UiRuntimeOptions.StartScreen`) if Studio play-testing should skip the menu.

**Secondary finding (left open, content tuning):** below about 10 fps a frame's move is clamped to `speed × moveWindow` (100 ms in `Player/PlayerDefinition.asset`). At P2.1's 5.5 fps Editor viewport the player covers about 55 % of wall-clock speed. This is the kernel's anti-teleport bound, kept by design. Raising `moveWindowMilliseconds` in the content (P3.1; it is Live tuning via `PlayerSession.Retune`) widens it.

## 2. Verification (host `myubuntu`, Unity 6000.0.75f1, .NET 8)

| Suite | Command | Result | Duration | Result file (host, `~/wkspace/gc-studio/p1.7a/.unity-logs/`) |
|---|---|---|---|---|
| dotnet solution | `studio/tools/dotnet-test.sh p1.7a` | **1797 passed, 0 failed, 5 skipped** (1802 in 20 projects; the 5 skips are the live-etos client tests) | 123 s | TRX per project; console `RESULT dotnet test dotnet/GameCore.sln: PASS` |
| ↳ Rules.Gameplay | `studio/tools/dotnet-test.sh p1.7a dotnet/tests/GameCore.Rules.Gameplay.Tests` | **297 / 297** (38 new P1_7a: 28 hardening + 10 occupancy) | 53 s | — |
| Hollowmere EditMode | `unity-compile.sh p1.7a games/hollowmere --tests EditMode` | **121 passed, 0 failed, 5 skipped** (126; skips: 4 live-etos tests need `GAMECORE_ETOS_LIVE=1`, and 1 preview needs a graphics device) | 120 s | `games_hollowmere-editmode-20261005T125727-a1.xml` |
| Hollowmere PlayMode | `unity-compile.sh p1.7a games/hollowmere --tests PlayMode` | **11 / 11** (6 new P1_7a) | 60 s | `games_hollowmere-playmode-20261005T132421-a1.xml` |
| Validation EditMode | `unity-compile.sh p1.7a unity/GameCore.Validation --tests EditMode` | **1312 / 1312** | 611 s | `unity_GameCore.Validation-editmode-20261005T130327-a1.xml` |
| Validation PlayMode | `unity-compile.sh p1.7a unity/GameCore.Validation --tests PlayMode` | **84 / 84** | 220 s | `unity_GameCore.Validation-playmode-20261005T131831-a1.xml` |
| Mac checkers | `python3 tools/check_game_core_csharp.py`, `tools/validate_game_core_docs.py`, `tools/make_unity_metas.py` | ok (993 C# files), docs passed, metas created | — | — |

**New P1.7a tests**

- **`Tests/P1_7a/PlayMode/HollowmereSaveRestore.cs`** (the `[P1.7a]` log lines record the timings):
  - **`FreshBootRestore_ReattachesTheNarrativeLayer_AndDeliversInFlightWorkExactlyOnce`** — W-PERSIST-01, plus W-PLUG-01/04/06 and the outbox replay. It:
    1. plays to the Belfry with the quest at stage 2;
    2. moves Odd with `world.place` and spawns one runtime target;
    3. rings the bell and captures in the frame whose pump recorded the bell's obligations (1 open);
    4. closes the world and **fresh-boots**, then restores.
    
    It then asserts:
    - the narrative layer re-attached (`Reattachments == 1`) on the restored owner;
    - the open obligation was reinstated;
    - facts, Maren's slots in an unloaded region, Odd's moved pose, `interact.occupants` and the runtime target with `world.spawnOrdinal` are all restored;
    - the `TestRoundTrip` slot hash equals the capture's, and the frame was rebound;
    - the streamer reconciled (Belfry Resident, Village Unloaded);
    - the in-flight bell work applied exactly once (`bell_rung`, stage 3, `RingBellOnUse` fired once, 0 drops);
    - home → `dialogue.start` (Maren's "You rang it") works.
    
    Next it captures while the reward grants are in flight and restores in place: the lantern and coins land exactly once. Replaying the in-flight rows on top changes nothing, and the replays answer `AlreadyApplied`. Finally, one sanctioned pump per frame and no violations. Total 318 ms of the test body.
  - **`CosmeticEditRestores_StructuralEditRefusesWithRecipeRevisionMismatch`** — W-PERSIST-02/03 on in-memory manifest copies:
    - a content-stamp-only edit keeps the revision and restores;
    - a structural-stamp edit refuses with `save.catalog-mismatch` / `ProductionRestoreRefusal.RecipeRevisionMismatch` (the "captured at revision … this build registers revision …" detail) and leaves the running world.
  - **`TwoHundredGrants_GetDistinctRequestIds_AndGrantsAfterAReloadStillApply`** — W-PLUG-08: 200 single-coin grants reach 200 coins with 0 refusals. After a capture and restore, 8 more grants apply (208).
  - **`SameFrameIndexedCommands_GiveIdenticalSlots_AcrossTwoBoots`** — determinism: the canonical slot hash is identical across two boots fed the same frame-indexed commands (`8376214e…`, steps 21/21).
  - **`APortalCondition_RefusesTravelWithAStableCode_UntilItHolds`** — coordinator addition to A4:
    - travel through a gated portal is refused with GP-WLD-013 and an explain record naming `narrative.fact.gate_open`;
    - after the key purchase sets the fact, travel passes.
- **`Tests/P1_7a/PlayMode/PlayerWalkSpeed.cs`** — see the walk-speed section above.
- **`Tests/P1_7a/EditMode/BakeHardeningTests.cs`** (fork) covers A8/A10:
  - Verify mints and dirties nothing;
  - Plan refuses unminted ids;
  - the canonicalizer refuses bad references;
  - `ChangedFiles` is sorted;
  - the structural field lists name real fields;
  - cosmetic and structural edits move the right stamp;
  - the committed manifest revision equals the structural stamp's.
- **dotnet `P1_7a/GameplayHardeningRulesTests.cs`** — vertical motion, `restoreStamina`, request-id derivation, `GameplayClock`, the streaming reconcile matrix, quest prerequisites and dependents.
- **dotnet `P1_7a/OccupancyRulesTests.cs`** — the occupancy bit.

**Host bake outputs.** The structural stamps change the catalog recipe ids and fingerprint (`7d9e8e46…` → `2141c797…`). The host EditMode run re-baked Hollowmere, and the four outputs are committed as host outputs:
- `World/Catalog/Hollowmere.bake.json`
- `World/Catalog/HollowmereCatalog.catalog.json`
- `World/Generated/HollowmereCatalog.g.cs`
- `World/Hollowmere.manifest.asset` (now carries `structuralStamp`)

A second EditMode run left the host tree clean, so the bake is stable.

## 3. Review findings → fix / decision

| # | Finding (review / architect item) | Fix or decision | Evidence |
|---|---|---|---|
| 1 | Narrative layer dies on `SaveService.Restore` (A2) | `NarrativeComposer.Attach` / `AttachRestored`; `GameBoot.UseSaves` / `PrepareRestored` / `Reattach` (UI rig path or `RootChanged`) | W-PERSIST-01 (fresh boot), slot-2 in-place restore |
| 2 | Two delivery owners per world; the save captured the wrong outbox (A1) | Narrative delivery adopts the `DeliveryFactory` owner (`NarrativeDelivery.Configure` / `FactoryFor`) | `Saves.Modules.Delivery` is the same instance as `Delivery.Owner` |
| 3 | Obligations created after the pump; a save in between lost them (A1) | Step tap: kernels hand committed events to `NarrativeDelivery.OnCommitted` / `OnActionDemand` in the committing step | Capture in the committing frame (1 open) → delivered exactly once after a fresh boot |
| 4 | Dedup on an 8-entry ring (A1) | Dedup on the obligation id (open claim + terminal retention → `IdempotencyConflict` → `AlreadyApplied`); every port carries the id | Outbox replay after restore: 0 extra grants, `AlreadyApplied` ≥ 2 |
| 5 | Not every port covered (A1) | grant, consume, buy, pickup, set-fact, dialogue-start, quest-*, logic-evaluate, run-actions, entity-*, world-travel, player-restore-stamina | Port table in section 1 |
| 6 | Runtime-spawned targets lost, spawn ordinal in memory (A2) | Re-register from `root.Targets` (recipe → definition); `world.spawnOrdinal` slot | `RestoredRuntimeTargets == 1`, `CommittedOrdinal == 1` after a fresh boot |
| 7 | Streamer ignores restored residency (A3) | First-tick reconcile; back-off; latch GP-WLD-032; Editor GP-WLD-031 | W-PERSIST-01 residency asserts; `StreamingReconcileTests` |
| 8 | Duplicate pose truth `player.pos*` / `npc.pos*` vs `world.pos*` (A4) | `world.pos*` authoritative and written in the same step; the others are mirrors never read for decisions; `world.place` adopted; issuer-restricted; explain records | W-PLUG-01 (Odd stays after the region cycle and after restore); P1.3 PlayMode walk |
| 9 | Portal condition (coordinator, A4) | `Travel` evaluates `conditionRef` via `IConditionEvaluator`; GP-WLD-013/014 + explain | `APortalCondition_…` |
| 10 | Quest prerequisites / fail closes dependents (coordinator, A4) | `QuestRules.PrerequisitesMet`, `DependentsOf`, `CloseDependent`; kernel refusal code 5; same-batch close | `QuestDependencyTests` |
| 11 | Frozen domain time in command-driven worlds (A5) | `GameplayClock`; logic `NowMs`, NPC schedules | `GameplayClockTests`; determinism test |
| 12 | Vertical velocity and grounded in binder memory (A6) | `player.verticalSpeed` / `player.grounded` slots, integrated in `PlayerRules`; CharacterController for collision only; locomotion binder inactive headless | `VerticalMotionTests`; P1.3 EditMode `PlayerMoves_…` |
| 13 | `ProximityInteractor` "was inside" in memory (A6) | Per-actor occupancy bit in `interact.occupants` | `OccupancyRulesTests`; occupants restored in W-PERSIST-01 |
| 14 | Request ids from a non-persistent serial (A6) | `GameplayRequestIds.OfCommand(issuer, step, serial)`; obligations negative | `RequestIdTests`; W-PLUG-08 |
| 15 | `failSubmitted` never retried (A6) | Quest tracker resubmits the fail with a fresh id | `QuestKernel` tracker |
| 16 | Binders instantiate and animate non-resident regions (A7) | `PrefabViewBinder` per-region pool; `IResidencyAware` on the listed binders | Hollowmere PlayMode suites |
| 17 | Tint edit breaks saves (A8, W-PERSIST-02/03) | Structural stamp → recipe revision; explicit per-type lists until P1.7b's `Structural` marker | `CosmeticEditRestores_…`; `BakeHardeningTests` |
| 18 | Install config never reaches systems (A9, SADR-013) | Numeric tuning Live via `Retune` / `RetuneAll`; option (a) binding deferred to the declaration owner; decision row updated | Doc row; docs validator |
| 19 | Verify mints GUIDs / SetDirty; unsorted ChangedFiles; CRLF drift (A10) | Validate before stamping; no minting; sorted, distinct; `.gitattributes` | `BakeHardeningTests` |
| 20 | GameBoot half-wired on failure; GameApplication statics survive domain reload off; round trip leaves the frame on a scratch root (A11) | `TryBoot` + shutdown on `Install` failure; `ResetSessionStatics`; `RebindAdapterFrame` | `FrameRebinds ≥ 1` in W-PERSIST-01 |
| 21 | Bootstrap fallback (A11, `GameApplication.Register` at SubsystemRegistration) | **Deferred**: needs the Hollowmere definition and manifest loadable before any scene (asset placement), which is P3.1 | Section 6 |
| 22 | `player.restoreStamina` (coordinator, P3.1) | Route, rules, kernel handler, logic port | `RestoreStaminaTests` |
| 23 | `ActionKind` `RestoreStamina` / `Buy` (coordinator, P3.1) | Enum values 17/18, describe entries, delivery ports | Port table |
| 24 | Walk speed 0.25 m in P2.1 evidence (coordinator) | Not a kernel or input regression: the evidence held W on the main menu; fix the evidence harness | `PlayerWalkSpeed` (7.55 m / 7.5 m on the HUD) |

## 4. API changes

**Slots added:**
- `world.spawnOrdinal` (`GameplaySlots.SpawnOrdinal`, region domain, on the anchor region)
- `player.verticalSpeed` (`PlayerMotionSlots.VerticalSpeed`)
- `player.grounded` (`PlayerMotionSlots.Grounded`)

**Slots removed:** none. `player.posX/Y/Z`, `player.yaw`, `player.regionKey` and `npc.posX/Z`, `npc.yaw` are kept as **derived mirrors** of `world.pos*` / `world.region`. They are written in the same step and never read for decisions. Studio and tests should read `world.*`.

**Routes and schemas added:**
- `player.route.restore-stamina`, command `player.command.restore-stamina` v1 (amount [, request id]), event `player.event.stamina-restored` v1 (A = amount, B = before, C = after), with buffer and lane `player.buffer.restore-stamina` / `player.order.restore-stamina`.
- `world.travel` accepts an optional trailing request id (`TravelPayload.LengthWithRequest = 12`).
- Narrative routes gained optional trailing request-id ints:
  - logic evaluate (5 + 1), run-actions (3 + 1)
  - dialogue start (3 + 1)
  - quest set-objective (2 + 1)
  - entity commands `EntityCommand.Encode(v, req)`

**Contracts:**
- `GameplayClock`: `DefaultStepMilliseconds`, `StepLength`, `StepTimeMs`, `NowMs32`
- `GameplayIssuers`
- `GameplayRequestIds` (`OfCommand`, `OfObligation`)
- `PlayerStaminaIds`
- `IGameplayStepTap`, `GameplayStepTaps`

**World:**
- `GameplayWorld.StepTap`, `RestoredRuntimeTargets`, `Spawner.CommittedOrdinal`
- `WorldModule.StudioIssuer`, `AnchorTarget`, `RecentRefusals`, `LastRefusalCode`, `Conditions`
- `ManifestPortal.conditionRef`
- `ManifestDefinition.structuralStamp`, `Revision`
- `RegionStreamer.Reconciliations`, `LatchedRegions`, `IsLatched`, `ResetLatch`, `Diagnostics`
- Rules: `WorldRefusalCodes`, `StreamingRules`, `ReconcileAction`

**Narrative:**
- `NarrativeComposer.TryBoot`, `Attach(root, plan, content, modules, seedSlots, owner)`, `Attach(world, …)`, `AttachRestored(service, world, content, modules)`
- `NarrativeRuntime(…, owner)`, `.Tap`
- `NarrativeDelivery(runtime, owner)`, with statics `OwnerIdOf`, `CreateOwner`, `FactoryFor` and `Configure`
- `NarrativeDelivery` counters: `Described`, `Undescribable`, `Dropped`, `LastDropDetail`, `Triggered`, `Signals`, `ActionDemands`, `Claims`, `ClaimRefusals`, `Settled`
- `NarrativeSubmitter.NextRequestId()`
- `StepEventBatch.CommitAll(plane, message, tap)`
- `LogicModule.TryTrigger`, `AlreadyApplied`
- `QuestModule.PrerequisiteRefusals`, `DependentsClosed`; tracker `EdgeSignals`

**Rules:**
- `ActionKind.RestoreStamina = 17`, `ActionKind.Buy = 18`
- `PlayerRules.RestoreStamina`, `JumpSpeed`, `AirborneFlag`
- `PlayerTuning.WithVertical`
- `PlayerMove.FromFlags`
- `QuestModel(…, prerequisites, dependents)`
- `QuestRefusal.PrerequisitesUnmet = 5`
- `QuestRules.PrerequisitesMet`, `Start(quest, state, statusOf, events)`, `DependentsOf`, `CloseDependent`
- `OccupancyRules`

**Player / NPC / interaction:**
- `IPlayerMotionResolver.Resolve(from, horizontal, verticalSpeed, deltaTime, out airborne)` (signature change)
- `PlayerSession.Retune()`, `NpcModule.RetuneAll()`, `InteractionModule.RetuneAll()`
- `NpcModule` counter `Adoptions`

**App:**
- `SaveService.FrameRebinds`
- `GameApplication.ResetSessionStatics()` (internal, `SubsystemRegistration`)

**Hollowmere:** `GameBoot.Reattachments`, `ConfigureSaves(options)`, `UseSaves(service)`, `PrepareRestored(service)`, `Reattach(service, world)`.

### Edits outside the exclusive paths (minimal; please review at integration)
- `games/hollowmere/Assets/Hollowmere/Boot/GameBoot.cs` (+115/−16 lines): header note; `TryBoot`; `Install` split out of `Start` with shutdown on failure; `Reattachments`; `ConfigureSaves`; `UseSaves`; `PrepareRestored`; `Reattach`. Nothing else in Hollowmere's boot changed.
- `Packages/com.gamecore.gameplay.player/Runtime/PlayerDeclarations.cs` (P1.7b): vertical slots; restore-stamina buffer, route and lane.
- `Packages/com.gamecore.gameplay.world/Runtime/WorldDeclarations.cs` (P1.7b): the `world.spawnOrdinal` slot row.
- `Packages/com.gamecore.gameplay.world/Runtime/RegionManifest.cs`: `ManifestPortal.conditionRef`, `ManifestDefinition.structuralStamp` and `Revision`.
- `Packages/com.gamecore.gameplay.compile/Core/{BakeModel,BakeReportWriter,CatalogDescriptionWriter,DefinitionHashing}.cs`: the structural hash rides the bake model into the catalog recipe id and the report (A8).
- `Packages/com.gamecore.gameplay.player/Runtime/{PortalProbe,InteractionFocus,ThirdPersonCamera}.cs`: read `world.pos*` / `world.yaw` / `world.region` (A4).
- `Packages/com.gamecore.gameplay.logic/Runtime/NarrativeServices.cs`: the pickup request id comes from `Submitter.NextRequestId()` (A6).
- `dotnet/tests/GameCore.Rules.Gameplay.Tests/GameCore.Rules.Gameplay.Tests.csproj`: `<Compile Include="P1_7a/**/*.cs" />`.
- `games/hollowmere/Assets/Hollowmere/Tests/P1_3/EditMode/P13KernelTests.cs` (2 assertions):
  - the `PlayerState()` helper reads the new vertical slots;
  - `AnAttachWithoutSeeding_…` now expects seeding to mirror the authoritative `world.pos` instead of teleporting back to the bake (A4).
- Hollowmere baked outputs (4 files, host bake; see section 2).

## 5. Needed from P1.7b and P3.1

**P1.7b (declarations, definitions, tools):**
1. `PortalDefinition.conditionRef` (and spawn point). Wire it in `Entry`'s manifest writer into `ManifestPortal.conditionRef`. That writer is in my `Entry.cs`, so I or the integrator will add the one line when the field reaches main.
2. `QuestDefinition.prerequisites` / `dependents`, and the converter (`NarrativeContent.cs`) populating `QuestModel.Prerequisites` / `Dependents`. The kernel semantics are in place.
3. An AuthorRef category for the `Buy` action's vendor field on `ActionEntry`. The converter already maps target → Key, inventory → Key2, value → Value.
4. `AuthorField(Structural = true)`, so the canonicalizer's explicit per-type structural lists can be replaced by the attribute.
5. Fold `WorldRefusalCodes` (GP-WLD-013..040) into `GameplayDiagnosticCodes` and the catalog of codes.
6. `ReflectedTool`: no `SetDirty` on read-only tools (the A10 rule for tools).
7. SADR-013 option (a): declare `RuleConfigBinding` rows for the numeric tuning fields, so the `Retune` paths can also run from install config.
8. Declare `player.restoreStamina` in the player tool catalog (tool id / AuthorRef); the route itself is in place.

**P3.1 (Hollowmere):**
1. Create the `SaveService` with `GameBoot.ConfigureSaves(options)` (it sets `DeliveryFactory`), then call `GameBoot.UseSaves(service)`.
2. The A11 `GameApplication.Register` path at `SubsystemRegistration`. It needs the Hollowmere definition and region/content manifests loadable before any scene (Resources or Addressables placement).
3. Optionally raise `PlayerDefinition.moveWindowMilliseconds` if low-fps Editor play should not slow the player (section 1).

**P2.1 / integrator:** fix the evidence harness as described in the walk-speed section (`StudioUiEvidence.cs` step 15).

## 6. Left open
- **Presentation ports are at-least-once after a restore.** `play-audio` and `show-message` acknowledge on dispatch, so a restore taken between dispatch and acknowledgement replays the sound or message. Gameplay ports are exactly once.
- **Edge-objective counts:** two signals for the same objective committed in the same step can count once, because the quest-objective obligation carries an absolute count.
- **`HasRoom` reserve heuristic:** `OpenCount + n×4 ≤ Capacity (256)` refuses a step's batch when the outbox could overflow. Drops are counted and logged (`Dropped`, `LastDropDetail`); none occurred in any run.
- **A11 bootstrap registration** at `SubsystemRegistration` is deferred to P3.1 (asset placement, section 5).
- **Stale comments in `InteractionContracts.cs`** (not mine) still describe count-based occupancy.
- **Low-fps clamp:** `speed × moveWindow` per frame (section 1). This is a content decision.
- **The Input System path cannot be verified headless:** batchmode keyboard states do not reach the action map, so `PlayerWalkSpeed` phase B is diagnostic only. A graphical run of the corrected P2.1 evidence closes it.
- **Pre-existing warning** `NpcBinders.cs:130` (CS8602) is unchanged.

# P1.1 entities-world-compile

Catalog rows 1 (entities) and 2 (regions/portals/streaming) of the gameplay plugin library, the gameplay compile
pipeline, the Hollowmere three-region world, and the application root's world time driver (P0.4 leftover 3).

Branch: `worktree-agent-ad61857ccbcbba79a`. Host clone: `~/wkspace/gc-studio/p1.1`.

## What was built

| Package / path | Content |
|---|---|
| `Packages/com.gamecore.gameplay.contracts` (engine-free) | `AuthoringIds` (lowercase non-zero D-format GUIDs; `TargetIdFor` = `Derive("auth."+id)`, `ScopeIdFor`, `DefinitionIdFor`, `StableKey`, `RevisionOfContentStamp`), `GameplayIds`/`SlotNames.Of`/`GameplayIdRegistry` (route/command/event/slot/stage/buffer/owner ids with collision detection), `IAuthoredObject`, `IDefinitionAsset`, `IAuthoredRegion`, units (mm, mrad, ms, milli-scale), `IPresentationBinder`, `IResidencyAware`, `IResidencyQuery`, `ICommittedSlotReader`, `RegionResidency`, `GameplayDiagnosticCodes` (GP-ID/ENT/WLD/CMP), payload writer/reader, catalog names, mirror authoring attributes and enums. |
| `Packages/com.gamecore.rules.gameplay` (one asmdef `GameCore.Rules.Gameplay`, noEngineReferences) | `Runtime/Entities/EntityRules` (spawn/despawn/variant/scale/visible transitions with typed refusals), `Runtime/World/WorldRules` (residency 0..3 state machine with exactly five legal transitions, `RegionGraph`, `TravelRules`). NUnit tests in `Tests/`. |
| `Packages/com.gamecore.gameplay.entities` | Authoring: `AuthoredEntity`, `EntityDefinition` [Authorable("entity.definition")], `VariantDefinition`, `OverrideSet`. Kernel half: manifest (slots `entity.alive/variant/scaleMilli/visible`, stage, buffers), routes `entity.spawn/despawn/setVariant`, events EntitySpawned/EntityDespawned/EntityVariantChanged, one spawn recipe per definition whose exact revision is its content hash, `[DisableAutoCreation] EntityCommandSystem` with a per-world `EntityModule` (no static state). Binders: `PrefabViewBinder`, `AnimatorBinder`, `AudioSourceBinder` (hook), `InteractionTargetBinder` (stub), all inactive under batchmode+nographics. Editor: `entity.place/duplicate/replaceDefinition/setVariant/applyOverride/layoutRing/layoutLine` ([AuthorOperation], Undo, GP-coded refusals) and `EntityValidator`. |
| `Packages/com.gamecore.gameplay.world` | Authoring: `RegionDefinition`, `PortalDefinition`, `WorldDefinition`, `AuthoredRegion`, `RegionBounds`, `RegionPortal`. `RegionManifest` (baked, integer-only). Kernel half: region slots `world.residency/visits`, entity slots `world.region/posX/posY/posZ/yaw`, routes `world.travel`, host-only `world.setResidency`, `world.place`, events RegionEntered/RegionLeft/ResidencyChanged/EntityPlaced, `[DisableAutoCreation] WorldCommandSystem` + `WorldModule`. Runtime: `WorldBuilder` (Build/Attach), `GameplayBoot`, `GameplayCatalog`, `GameplayWorld`, `GameplayCommands`, `GameplayPresentationFrame`, `EntitySpawner`, `RegionStreamer`, `UnitySceneLoader`/`ImmediateSceneLoader`, `GameplayWorldBehaviour`. Editor: `world.connectRegions/addPortal/setSpawnPoint` and `WorldValidator`. |
| `Packages/com.gamecore.gameplay.compile` | Core (Unity-free, Editor-only asmdef): bake model, `DefinitionHashing` (SHA-256 over canonical authorable fields), `CanonicalJson`, `CatalogDescriptionWriter` (gamecore.catalog-description/1), `BakeValidator`, `BakeReportWriter`. Editor: `DefinitionCanonicalizer` (reflection over [AuthorField]/[AuthorRef]), `WorldReader` (scenes opened additively, closed without saving), `Entry.Bake` / `Entry.Verify`, `BakeStaleMarker` (AssetModificationProcessor). |
| `Packages/com.gamecore.unity.app` (world time driver only) | `GameApplicationTimeFrame` (new) and a minimal `GameApplicationRoot` change: the root creates `Time` (a `WorldTimeDriver`), adopts the schedule adaptation's `NativeTable`, runs the driver inside the pump counter (`PumpCounter -> TimeFrame -> game frame`) and calls `OnWorldPaused/OnWorldResumed` on Pause/Resume. |
| `games/hollowmere` | Manifest adds the five packages. `Assets/Hollowmere/World/Editor/HollowmereWorldAuthoring` authors the world through the tools; `Boot/GameBoot` (+ `Boot/Debug` fly camera and keyboard travel, flagged REPLACED BY P1.3); `World/Generated` asmdef for the generated catalog; tests under `Tests/P1_1`. Region scenes, definitions, portals, the WorldDefinition, `Boot/Boot.unity` and the bake outputs are produced on the host by the authoring test and committed from there (see "Verification"). |
| `dotnet` | `src/GameCore.Rules.Gameplay`, `tests/GameCore.Rules.Gameplay.Tests` (rules tests + contracts + compile Core + Local parity/compile tests), both in `GameCore.sln`. |

## API

### Authoring identity by convention (for Studio discovery without a type dependency)

Every authored component/ScriptableObject - `AuthoredEntity`, `EntityDefinition`, `VariantDefinition`,
`RegionDefinition`, `PortalDefinition`, `WorldDefinition` - carries:

* `[SerializeField] private string authoringId` (lowercase non-zero D-format GUID, minted once; a prefab asset never carries one),
* a public read-only `string AuthoringId { get; }`,
* `GameCore.Gameplay.Contracts.IAuthoredObject { string AuthoringId { get; } }`; the definitions also implement
  `IDefinitionAsset : IAuthoredObject { string DefinitionName; string ContentStamp; }`,
* `[Authorable("<type id>")]`: `entity.instance`, `entity.definition`, `entity.variant`, `world.region`, `world.portal`, `world.definition`.

The kernel target of an authored object is `AuthoringIds.TargetIdFor(id)` = `StableNameKeyDerivation.Derive("auth." + id)`,
proved equal to `GameCore.Studio.Model.IdDerivation.TargetIdFor` by a dotnet test.

**Mirror attributes.** Gameplay runtime packages must not depend on `com.gamecore.studio.*`, so
`GameCore.Gameplay.Contracts` declares `AuthorableAttribute`, `AuthorFieldAttribute`, `AuthorRefAttribute`,
`AuthorOperationAttribute`, `AuthorArgAttribute`, `AuthorValidatorAttribute` and the enums `AuthoringKind`,
`AuthorScope`, `ToolTier`, `RuntimeApply` with the identical names, property names/types/defaults, constructors,
`AttributeUsage` and enum members of `GameCore.Studio.Model` (dotnet parity test). **P1.6 integration question:**
`ToolCatalogBuilder` reads attributes with typed `GetCustomAttribute<Studio.Model.X>()`; to see the gameplay tools it
must also match attributes by type name (`AuthorOperationAttribute` etc.) and read the same-named properties, or the
gameplay packages get a studio-owned adapter later. Nothing in P1.1 depends on either choice.

### Residency query (unloaded-region detection)

`GameCore.Gameplay.Contracts.IResidencyQuery` is implemented by `GameCore.Gameplay.World.RegionStreamer`:

```csharp
IReadOnlyList<string> RegionIds { get; }                       // region authoring ids
bool TryGetResidency(string regionId, out RegionResidency r);   // committed world.residency slot
RegionResidency ResidencyOf(string regionId);                   // Unloaded when unknown
```

Reach it with `GameplayWorldBehaviour.World.Streamer` (scene object) or `GameplayWorld.Streamer`; `RegionResidency`
is `Unloaded=0, Loading=1, Resident=2, Unloading=3`. Studio can bind by interface name `IResidencyQuery`.

### Runtime

* `WorldBuilder.Build(RegionManifest, ICatalog, ContentHash, WorldBuildOptions?) -> WorldBuildPlan` (application
  definition: world scope root, one scope seed per region, one seed boot step per region target and per authored
  entity, both plugins mounted at the world scope, both systems, routes/lanes/readers, recipes, command-driven world)
  and `WorldBuilder.Attach(GameApplicationRoot, WorldBuildPlan) -> GameplayWorld` (seeds all slots while paused,
  hands each command system its module).
* `GameplayBoot.Boot(manifest, options, buildOptions, start)` and `GameplayCatalog.TryBuild(typeName, ...)` (the
  generated catalog is resolved by the full name recorded in the manifest, so gameplay code never references the
  generated assembly).
* `GameplayWorld`: `Commands` (player issuer: `Travel`, `Place`, `Spawn`, `Despawn`, `SetVariant`, `Submit`),
  `Spawner.TrySpawn` (runtime targets: ScopeCreate under the region scope -> drain -> `PublishSpawn` -> register ->
  seed), `Streamer`, `Slots` (`ICommittedSlotReader`), `CreateViews(parent)`, `AddBinder`, `AddInput`
  (`IGameplayInputSource`, P1.3), `ReadEvents`, `RequestPortalTravel`, `UseSceneLoader`, `Shutdown`.
* Events: payload = target id (16 bytes) + int32s. `EntityEvent` (target, value); `WorldEvent` (target, a, b, c, d):
  RegionEntered/RegionLeft = (from region key, to region key, portal key), ResidencyChanged = (new, old),
  EntityPlaced = (x, y, z, yaw).
* `GameApplicationRoot.Time` (`WorldTimeDriver`) and `GameApplicationRoot.TimeFrame` (`GameApplicationTimeFrame`).
* **Root replacement after a restore (P1.2).** A `GameplayWorld` is bound to exactly one `GameApplicationRoot`;
  nothing in P1.1 caches `GameApplication.Current`. `SaveService.Restore` composes a new root through
  `GameApplicationRoot.TryCompose` (so the time frame and the plan's adapter frame rebind automatically:
  `WithAdapterFrame(app => frame.BindRoot(app))`). A game that restores must, on `SaveService.RootChanged`, call
  `GameplayWorld.Shutdown()` on the old world and `WorldBuilder.Attach(newRoot, plan, seedSlots: false)` (restored
  slots are kept), then re-create views and call `Streamer.Observe`. Runtime-spawned targets are not re-registered in
  the new `EntityModule` (they are not in the manifest); save/load is outside P1.1, so this path is documented, not
  tested here.

### Compile

* `GameCore.Gameplay.Compile.Entry.Bake()` / `Entry.Verify()` (the project's single WorldDefinition, conventional
  paths), `Entry.Bake(world, paths[, refreshAssetDatabase])`, `Entry.Verify(world, paths)`; menu items
  GameCore/Gameplay/Bake World and Verify World Bake.
* Outputs for `<dir>/<World>.asset`: `<dir>/Catalog/<World>Catalog.catalog.json`, `<dir>/Generated/<World>Catalog.g.cs`
  (+ coverage, via the content compiler), `<dir>/Catalog/<World>.bake.json`, `<dir>/<World>.manifest.asset`, and each
  EntityDefinition's `contentStamp`.

## Decisions

1. **No studio dependency**: mirror attributes (above) plus a dotnet shape-parity test.
2. **Despawn is logical** (`entity.alive = 0`, the target stays registered); `entity.spawn` revives it. New runtime
   targets go through `EntitySpawner` (P-024 spawn path).
3. **Initial slot values are seeded after boot** (`LiveTargetSeeder.TrySeedSlot`) while the world is paused; state
   slot specs are not auto-initialized by the publisher.
4. **Residency is driven only by the streamer** through host-issuer `world.setResidency` commands; the world system
   refuses any other issuer and any illegal transition. All regions start Unloaded; every region target (and every
   authored entity in an unloaded region) is a live kernel target from the first boundary.
5. **Views live in the boot scene** under one root per region, deactivated while the region is not Resident; region
   scene proxies (`AuthoredEntity`) are deactivated on load. Presentation reads committed slots only (P-045).
6. **World temporal model: CommandDriven**, target capacity 256.
7. **Portals form a triangle** (village-marsh, marsh-belfry, belfry-village): two portal ends per region and the loop
   village -> marsh -> belfry -> village is valid travel.
8. **Content hash** = SHA-256 over `gamecore.gameplay.definition/1`, the type id and the [AuthorField]/[AuthorRef]
   fields in ordinal order; referenced authorable definitions contribute their own hash (a variant edit changes its
   definition); assets contribute `asset:<guid>:<fileId>`. The recipe revision is the first 8 digest bytes.
9. **Bake on demand**; on save `BakeStaleMarker` only stale-marks (SessionState). An opt-in auto-bake
   (`EditorPrefs GameCore.Gameplay.AutoBake`) schedules a bake after the save only when the last measured bake took
   <= 1000 ms; a bake never runs inside the save callback.
10. **Time driver inside the pump**: `WorldTimeDriver.PumpFrame` pumps the host itself, which would be a second pump
    path, so `GameApplicationTimeFrame` runs exactly its algorithm split around the application's one pump.
11. **Scene loading**: Editor uses `EditorSceneManager.LoadSceneAsyncInPlayMode` (no build-settings entry needed);
    a player uses `SceneManager.LoadSceneAsync(path)`, which needs the region scenes in EditorBuildSettings (outside
    P1.1's paths; see "Open").

## Verification

All runs are on the host (`myubuntu`, Unity 6000.0.75f1, .NET 8) at synced commit `68d17c2` (after the merge of
main `52add45`), one Unity instance. Logs and NUnit XML are in `~/wkspace/gc-studio/p1.1/.unity-logs/`.

| Command | Result | Duration |
|---|---|---|
| `studio/tools/unity-compile.sh p1.1 games/hollowmere --tests EditMode --filter 'Hollowmere\.P1_1\..*'` | PASS, 16/16, 0 skipped (`games_hollowmere-editmode-20261005T062518-a1.{log,xml}`) | 175 s |
| `studio/tools/unity-compile.sh p1.1 games/hollowmere --tests PlayMode --filter 'Hollowmere\.P1_1\..*'` | PASS, 1/1 `ThreeRegionLoop.TravelsVillageMarshBelfryVillage` (`games_hollowmere-playmode-20261005T062956-a1.{log,xml}`) | 131 s (test 1.85 s) |
| `studio/tools/dotnet-test.sh p1.1 dotnet/tests/GameCore.Rules.Gameplay.Tests` | PASS, 70/70 | 315 ms test time |
| `studio/tools/dotnet-test.sh p1.1 dotnet/GameCore.sln` | PASS, 19 test assemblies, 0 failures | 146 s |
| `python3 tools/check_package_metadata.py` | package metadata and asmdef-derived dependencies agree (29 packages) | - |
| `python3 tools/check_game_core_csharp.py` | ok | - |
| `python3 tools/validate_game_core_docs.py` | passed | - |

EditMode (all Passed): AuthoringBakeTests - AuthorAndBake_ProducesTheThreeRegionWorld (4.56 s),
BakeTwice_IsByteIdentical_AndVerifyPasses, OneFieldChange_ChangesOnlyThatDefinitionsRevision,
VariantEdit_ChangesTheRevisionOfItsDefinition; AuthoringToolTests - AuthoredEntity_IdIsStableOnPrefabInstantiation_AndThePrefabCarriesNone,
EntityTools_RoundTrip, WorldTools_RoundTrip, HollowmereScenes_PassTheValidators; GameplayWorldTests -
Boot_SeedsEveryAuthoredTarget_AndEveryRegionStartsUnloaded, Streamer_BringsTheStartRegionResident_ThroughTheLegalResidencySteps,
Travel_LoopsVillageMarshBelfryVillage_WithEventsResidencyAndPosePersistence, Travel_ToTheSameRegion_OrFromAnUnknownTraveller_IsRefused,
SetResidency_FromAGameplayIssuer_IsRefused, EntityCommands_DespawnSpawnAndSetVariant_FollowTheRules,
Spawner_PublishesANewRuntimeTarget_InItsRegionScope, TimeDriver_AdvancesClocksOnCommittedSteps_FeedsWakes_AndFollowsPause.

Bake numbers (EditMode log): world = 3 regions, 3 portals, 7 definitions, 30 entities; catalog fingerprint
`425508a971072415b8f57e43894083a7f75396c1a47db4691b1a9c1b0e6ec899`. Rebake 554 ms, Verify 287 ms (byte identity);
author+bake from nothing 3215 ms (first run, bake 576 ms). After the full EditMode and PlayMode runs (which rebake
the world) the host clone's `git status` was clean: the committed outputs are reproduced byte for byte.

B-REGION (PlayMode, batchmode/nographics, so frame times are not representative of a player; one run, no perf
benchmark):

| Leg | Time | Frames |
|---|---|---|
| boot (Boot.unity load -> world Running, start region Resident) | 1474 ms | 35 |
| Thornwick Village -> Blackmere Marsh | 7 ms | 6 |
| Blackmere Marsh -> Drowned Belfry | 323 ms | 17 |
| Drowned Belfry -> Thornwick Village | 5 ms | 20 |
| loop total | 43 frames, 43 sanctioned pumps (one per frame); counter sanctioned=80, duplicate=0, bypass=0 | |

How the content was produced: the world is authored by `HollowmereWorldAuthoring.AuthorAndBake()` (through the
entity/world tools) inside the host EditMode test `AuthorAndBake_ProducesTheThreeRegionWorld`; the generated scenes,
assets, bake outputs and the re-resolved `packages-lock.json` were committed in the host clone (`341a652`) and merged
back; metas for the five files written without an AssetDatabase refresh were made with `tools/make_unity_metas.py`.
The authoring is idempotent: with the world asset present it only rebakes.

Host incident: the first Unity attempt hung in `PackageManager::Project::ResolvePackages` during the initial
`Library` rebuild (log silent 605 s; main-thread backtrace captured with `sudo -n gdb`), was killed by the
watchdog and the retry ran normally (docs/operator/editor-hang.md). Not a P1.1 fault; recorded for the hang log.

## Open

* EditorBuildSettings (ProjectSettings) are outside P1.1's paths: a Hollowmere player build needs
  `Assets/Hollowmere/Boot/Boot.unity` first and the three region scenes added. The old `Scenes/Boot.unity` stays.
* P1.6: attribute discovery by name (see API). `IResidencyQuery` is ready for Studio's unloaded-region detection.
* P1.3 replaces `Boot/Debug` (fly camera, keyboard travel) with the player controller; it plugs in through
  `GameplayWorld.AddInput(IGameplayInputSource)` and portal triggers (`RegionPortal` -> `RequestPortalTravel`,
  which needs a Rigidbody on the traveller's view).
* IL2CPP/managed stripping: `GameplayCatalog` calls the generated `BuildCatalog()` by reflection; a stripped player
  needs a link.xml entry (or a direct reference from game code) for the generated catalog class.
* P1.2 integration: no test exercises `SaveService.Restore` with a gameplay world (see API "Root replacement").
  The new world's streamer reads residency from the restored `world.residency` slots (unverified), but scenes loaded by the old
  streamer stay loaded; a restore helper that unloads them (or adopts them) belongs to the save/load packet.
* Animator bindings are data-only in the Hollowmere content (no Animator controllers authored).

# GameCore Studio plugin developer guide

Use the shipped entities and interaction packages as the gameplay package pattern. Use the pressure-plate sample for an agent-proposed mechanism. This guide describes the committed implementation and keeps the integration blockers recorded by the packet owners. ([P1.1](packets/P1.1-entities-world-compile.md), [P1.3](packets/P1.3-player-npc-interaction.md), [P2.4](packets/P2.4-staging-lane.md))

## Package and assembly shape

| Part | Pattern to follow | Source |
|---|---|---|
| `package.json` | `com.gamecore.*` packages use version `1.0.0`, Unity `6000.0`, accurate name/displayName/description and the exact asmdef-derived dependency set. Do not copy dependencies blindly. | [Package contract §1, §6, §9](../operator/packages.md), [entities manifest](../../Packages/com.gamecore.gameplay.entities/package.json) |
| `Runtime/` | Definitions, declarations, command systems, per-world modules and presentation. Reference contracts, rules, runtime and the exact Unity assemblies used. No runtime dependency on Studio. | [entities Runtime asmdef](../../Packages/com.gamecore.gameplay.entities/Runtime/GameCore.Gameplay.Entities.asmdef), [P1.1 mirror decision](packets/P1.1-entities-world-compile.md#authoring-identity-by-convention-for-studio-discovery-without-a-type-dependency) |
| `Editor/` | Authoring tools, validators and catalog contributors; asmdef `includePlatforms: ["Editor"]`. Gameplay mirror metadata does not require a Studio reference. | [entities Editor asmdef](../../Packages/com.gamecore.gameplay.entities/Editor/GameCore.Gameplay.Entities.Editor.asmdef), [interaction tools](../../Packages/com.gamecore.gameplay.interaction/Editor/InteractionTools.cs) |
| Pure rules | Reusable groups currently live at `com.gamecore.rules.gameplay/Runtime/<Group>/` under one `GameCore.Rules.Gameplay` asmdef, `noEngineReferences: true`. 02 now records this layout. | [P1.1 §What was built](packets/P1.1-entities-world-compile.md#what-was-built), [rules asmdef](../../Packages/com.gamecore.rules.gameplay/Runtime/GameCore.Rules.Gameplay.asmdef), [02 layout](02-architecture.md#3-repository-layout-new-and-changed-paths) |
| Dotnet mirror | Compile the same pure sources into netstandard2.1, test under net8.0 with C# 9, nullable enabled and warnings as errors. Add group test sources to the test project. | [rules csproj](../../dotnet/src/GameCore.Rules.Gameplay/GameCore.Rules.Gameplay.csproj), [test csproj](../../dotnet/tests/GameCore.Rules.Gameplay.Tests/GameCore.Rules.Gameplay.Tests.csproj), [build prerequisites](../operator/build-and-run.md#1-prerequisites) |
| Unity tests | Package Editor tests and game integration tests use explicit test asmdefs; the project lists package tests in `testables`. Tests needing Play Mode belong in the game's PlayMode assembly. | [P1.6 §Built](packets/P1.6-studio-core-unity.md#built), [P1.3 §Delivered](packets/P1.3-player-npc-interaction.md#delivered) |
| `.meta` | Preserve existing GUIDs and commit metas for new Unity files/folders. `make_unity_metas.py` creates missing GUID-only metas; Unity adds importer data on import. | [Package contract §9](../operator/packages.md#9-studio-gameplay-and-games-projects-sadr-014), [meta generator](../../tools/make_unity_metas.py) |
| Project references | Add `file:../../../Packages/<name>` from a `games/<game>/Packages` manifest. Resolve on the host and commit the lock. The checker needs at least one lock source; every lock containing the package must agree. | [P0.2](packets/P0.2-projects-tooling.md#what-was-built), [Package contract §9](../operator/packages.md#9-studio-gameplay-and-games-projects-sadr-014) |

Dependencies include references from Editor and test asmdefs, not just Runtime. Project-provided test runners/NUnit and optional `versionDefines` are not package dependencies. Packages cannot reference embedding-project Assets assemblies. Kernel packages cannot depend on gameplay, Studio or rules.gameplay. Allowlisted engine references use project pins; built-in engine modules need no package dependency. Only the documented Studio/gameplay Newtonsoft precompiled reference is allowed beyond NUnit. ([Package contract §6–§9](../operator/packages.md), [checker](../../tools/check_package_metadata.py))

## Cross-cutting contract

Use declared int32 slots for authoritative progress, with millimetres, milliradians, milliseconds and integer currency. Derived caches are reconstructed; private component fields must not be the only copy of saved state. Commands carry request identities and stable refusals; binders read committed state after commit. A plugin declares a real PackageContentHash, slots, routes, stages and state policy, and passes manifest validation. Keep mutable state per world, register systems explicitly and release presentation at detach. These are requirements for new plugins, not a claim that every existing path has completed integrated save/restore proof. ([Catalog conventions and cross-cutting requirements](05-plugin-catalog.md), [SADR-004](02-architecture.md#8-decision-records-sadr--studio-adr-numbering-continues-after-the-kernels-adr-017), [P1.4 outbox decision](packets/P1.4-dialogue-quest-logic-inventory.md#decisions-where-05-was-silent))

## Metadata and identity

Gameplay runtime types use the attributes in **GameCore.Gameplay.Contracts**. Studio Model carries matching attributes, but its assembly is Editor-only. The mirror keeps runtime packages free of Studio dependencies; the parity test checks shape, and P1.6's `AuthoringMetadata` reads attributes by name and converts enum members by name. A runtime dependency on Studio is not the solution to discovery. ([P1.1 §Authoring identity](packets/P1.1-entities-world-compile.md#authoring-identity-by-convention-for-studio-discovery-without-a-type-dependency), [P1.6 §Decisions](packets/P1.6-studio-core-unity.md#decisions))

| Attribute | What to declare | Source |
|---|---|---|
| `[Authorable("type.id")]` | Stable type id, display name, allowed scopes, RuntimeApplicability and documentation. The index node's type is this id. | [P0.3 API summary](packets/P0.3-studio-model.md#api-summary-namespace-gamecorestudiomodel-assembly-gamecorestudiomodel-editor-only) |
| `[AuthorField]` | Type override when supported, units, min/max/step, required flag and documentation. Constraints feed inspectors and validators. | Same API summary |
| `[AuthorRef]` | Reference Category, Required and documentation. Category describes the referenced authorable type/category, not an arbitrary display label. | Same API summary and validator rules |
| `[AuthorOperation("group.operation")]` | Tier, runtime applicability, scope, target kinds, project/target prerequisites and validator type. | Same API summary |
| `[AuthorArg]` | Exported argument name, type, required flag, units/range/category and documentation. The authorable target binds separately; additional references must be annotated arguments. | Same API summary; [P2.3 open item 3](packets/P2.3-studio-views.md#left-open-for-the-integrator-and-owners) |
| `[AuthorValidator("group.validator")]` | Stable validator id and Codes. Provide validation methods used by the tool and inspector paths. | [P0.3](packets/P0.3-studio-model.md), [interaction validator](../../Packages/com.gamecore.gameplay.interaction/Editor/InteractionTools.cs) |

A concrete signature from the interaction package demonstrates a Compose/Rebuild tool, a definition target and annotated positional arguments. Use its implementation's validate-before-write behavior as well as its metadata. ([InteractionTools.cs](../../Packages/com.gamecore.gameplay.interaction/Editor/InteractionTools.cs), `AddDoor`)

```csharp
[AuthorOperation("interaction.addDoor", Tier = ToolTier.Compose,
    RuntimeApplicability = RuntimeApply.Rebuild,
    Validator = typeof(InteractionValidator), Requires = "world.region",
    RequiresOnTarget = "interaction.interactable",
    Doc = "Places a door or gate interactable (its definition's entity) at a location (m) and heading (deg).")]
public static AuthoredEntity AddDoor(
    InteractableDefinition door,
    [AuthorArg(Unit = "m", Doc = "World position.")] Vector3 location,
    [AuthorArg(Unit = "deg", Required = false, Doc = "Heading around +Y.")] float yaw = 0f,
    [AuthorArg(Required = false, Doc = "Object name.")] string name = "")
```

Identity uses a serialized `authoringId`, public `AuthoringId`, and `IAuthoredObject`. Definitions also implement `IDefinitionAsset` with `DefinitionName` and `ContentStamp`. IDs are lowercase, nonzero D-format GUIDs. Use `AuthoringIds.TargetIdFor(id)`, which calls `StableNameKeyDerivation.Derive("auth." + id)`. Never persist Unity instance ids, Entity indexes or session handles as authoring identity. Prefab assets carry no entity authoring id; placed instances receive one. Unity Ctrl+D can copy a serialized id, so the Studio duplicate tool is the currently documented safe duplication path; EntityValidator.ValidateScene reports duplicate IDs (GP-ID); use entity.duplicate for a fresh identity. ([EntityValidator](../../Packages/com.gamecore.gameplay.entities/Editor/EntityValidator.cs#L137)) ([P1.1 §API](packets/P1.1-entities-world-compile.md#api), [P1.6 §Left open](packets/P1.6-studio-core-unity.md#left-open))

The authoringId value type and nested-list inverse are implemented; typed refs and ReadOnly flags come from P1.7b. ([P1.6 follow-up](packets/P1.6-studio-core-unity.md), [P1.7b §1](packets/P1.7b-gameplay-hardening-metadata.md#1-what-was-built))

## Slots, routes, events and systems

Follow `InteractionDeclarations`, `InteractionKernel` and `InteractionWorldExtension` together. The declarations expose plugin identity, owner/domain schema, state slots, stage/buffers, bounded command lanes, routes, payload readers and factory registrations. Interaction uses state, uses, cooldown and occupants slots, three command routes and committed success/refusal events. Progress-bearing slots follow PreserveDormant policies. The pure transition logic belongs in `InteractionRules`; the managed system applies those transitions under the declared schedule. ([InteractionDeclarations.cs](../../Packages/com.gamecore.gameplay.interaction/Runtime/InteractionDeclarations.cs), [InteractionKernel.cs](../../Packages/com.gamecore.gameplay.interaction/Runtime/InteractionKernel.cs), [P1.3 §Interaction](packets/P1.3-player-npc-interaction.md#interaction-comgamecoregameplayinteraction), [05 cross-cutting requirements](05-plugin-catalog.md#cross-cutting-requirements-for-every-package))

Create systems with `[DisableAutoCreation]`; provide their registration through the app definition or a world extension. `GameApplicationRoot` owns composition, publication, provenance, time and the single pump. Never add a second `World.Update` or `PumpFrame` route. Supply a real generated catalog/hash; a missing or stale registration must fail boot, not fall back to an empty game. ([P0.4 §1–§2](packets/P0.4-kernel-app.md), [P1.1 time driver decision](packets/P1.1-entities-world-compile.md#decisions), [headless §1](../operator/headless.md#1-production-entry-points))

### Three extension seams

| Seam | Implement / register | Lifecycle and limits | Source |
|---|---|---|---|
| `IGameplayWorldExtension` | Name, Plugins, Systems, Routes, Lanes, BindReaders, Validate, Attach, Detach; add to `WorldBuildOptions.Extensions`. | Build adds declarations and mount steps; Attach supplies modules while paused. `seedSlots:false` must preserve restored slots. Shutdown detaches in reverse order. | [GameplayWorldExtension.cs](../../Packages/com.gamecore.gameplay.world/Runtime/GameplayWorldExtension.cs), [P1.3 seams](packets/P1.3-player-npc-interaction.md#seams-in-p11-code-integrator-please-review) |
| `IGameplayWorldTargets` | Optional additional recipes and world-scope session targets. | Added by P1.5 for UI/audio; use this alongside the earlier extension seam where appropriate. Narrative still uses NarrativeComposer. | [P1.5 §What was built and decision 1](packets/P1.5-ui-audio.md), [P1.4 composition decision](packets/P1.4-dialogue-quest-logic-inventory.md#decisions-where-05-was-silent) |
| `IGameplayCatalogContributor` | Editor-side public parameterless constructor and `Contribution` containing package name, schemas and entries. | TypeCache discovery; ordinal package order; duplicate package/schema/registration names refused. Add a contributor rather than editing the compiler's static registration list. | [CatalogContributions.cs](../../Packages/com.gamecore.gameplay.compile/Core/CatalogContributions.cs), [P1.3 seams](packets/P1.3-player-npc-interaction.md#seams-in-p11-code-integrator-please-review) |
| `IGameplayBakeExtension` | Plan / Write / Verify for package-specific outputs. | TypeCache discovery ordered by ExtensionId; logic uses it for the narrative content manifest. | [GameplayBakeExtensions.cs](../../Packages/com.gamecore.gameplay.compile/Editor/Extensions/GameplayBakeExtensions.cs), [P1.4 §What was built](packets/P1.4-dialogue-quest-logic-inventory.md#what-was-built) |

Presentation implements `IPresentationBinder.BinderName`, `IsActive` and `Present(ICommittedSlotReader)`. `IResidencyAware.OnResidencyChanged(regionId, residency)` suspends views outside Resident; logical targets survive unload. Engine-object binders must skip headless batchmode with no graphics device. UI/audio keep their nonvisual runtimes alive while omitting UI roots and AudioSources. Binders read committed slots and do not write authoritative state. ([PresentationContracts.cs](../../Packages/com.gamecore.gameplay.contracts/Runtime/PresentationContracts.cs), [P1.1 decisions](packets/P1.1-entities-world-compile.md#decisions), [P1.5 decision 9](packets/P1.5-ui-audio.md#decisions-where-05-was-silent))

## Bake, revisions and saves

Choose **GameCore/Gameplay/Bake World**, then **GameCore/Gameplay/Verify World Bake**. `Entry.Bake` emits catalog description, generated catalog/coverage, bake report, region manifest and definition stamps. Saving only stale-marks a bake by default; opt-in auto-bake is scheduled only if the last bake took at most 1000 ms. A second bake must reproduce bytes. ([P1.1 §Compile and Decisions](packets/P1.1-entities-world-compile.md), [catalog byte-identity procedure](../operator/catalog-generation.md#4-proving-byte-identity))

Definition hashes cover canonical authorable fields, type id and recursively referenced definitions; asset references contribute GUID/fileId. Recipe revision derives from the structural stamp; the content stamp still covers all fields (P1.7a A8). A referenced variant edit can therefore change its parent definition revision. Package-content hash, definition stamp, recipe revision and overall catalog fingerprint have different roles; retain all the generated outputs from a content change. ([P1.1 decision 8 and Verification](packets/P1.1-entities-world-compile.md), [InteractionDeclarations.PackageContentHash](../../Packages/com.gamecore.gameplay.interaction/Runtime/InteractionDeclarations.cs))

| Change | Save consequence / obligation | Source |
|---|---|---|
| Ordinary slot value | Captured through declared owner/slot rows; do not hide progress only in a component. | [SADR-004](02-architecture.md), [P1.2 §1.1](packets/P1.2-save-restore.md#11-production-restore-builder-comgamecoreunityruntime-runtimepersistenceproductioncs) |
| Slot schema version | Register an unambiguous forward `SlotMigrationStep` and bind the owner/slot in SlotSchemaCatalog or SaveSchemaDefinition. Missing path is `save.migration-path-missing`. | [P1.2 §1.2 and §3](packets/P1.2-save-restore.md) |
| Recipe revision | Restore requires the exact captured revision. Declaring a compatible catalog alone does not migrate recipes. | [P1.2 §1.1 and §3](packets/P1.2-save-restore.md) |
| Catalog fingerprint | Must be identical or explicitly compatible; recipe/schema checks still apply. | [P1.2 §1.2](packets/P1.2-save-restore.md) |
| Clock schema / component state | No executable clock/recipe migration or arbitrary component-state capture is supplied. | [P1.2 §5](packets/P1.2-save-restore.md#5-open-items) |
| Restore success | New WorldId/root. Dispose old bindings, use ActiveRoot/RootChanged, reattach with `seedSlots:false`, recreate presentation, resume issuer sequences and reconstruct derived caches. | [P1.1 root replacement](packets/P1.1-entities-world-compile.md#runtime), [P1.2 §3 and §5](packets/P1.2-save-restore.md) |
| Pending cross-plugin effects | Persist/reinstate the outbox and stable receiver request ids. Narrative uses `RequestIdOf(outboxId)` and a bounded request ring; P1.7a registers it with the single delivery owner. | [P1.4 outbox decision and Open](packets/P1.4-dialogue-quest-logic-inventory.md) |

Provide game checkpoint codecs through `SaveServiceOptions.Codecs`; no shared game codec catalog exists in these packets. Rebind narrative modules, player/NPC/interaction sessions and loaded scenes, including runtime-spawned targets. P1.7a/P1.7c and P3.1 provide production Hollowmere integration; P4.2d acceptance remains row-specific. APP-1 updates `GameApplication.Current` before stopping the old root; consumers still rebind their sessions through RootChanged. ([P1.1 §Runtime and Open](packets/P1.1-entities-world-compile.md), [P1.2 §5](packets/P1.2-save-restore.md#5-open-items), [P1.5 §Open](packets/P1.5-ui-audio.md#open))

## Tools, validation and diagnostics

The catalog has **Configure**, **Compose**, **Mechanism** tiers. Configure changes existing fields/references; Compose changes structure/content; Mechanism proposes a package for staging. There is no Agent tier. Dialogue/audio media tools use Compose after P1.7b; service availability comes from the registered gateway after R2-G. ([Authoring §5](03-authoring-contracts.md#5-tools-owner-studiocore-edit-registry-populated-by-plugins), [P1.4 decisions](packets/P1.4-dialogue-quest-logic-inventory.md#decisions-where-05-was-silent), [P1.5 decision 10](packets/P1.5-ui-audio.md#decisions-where-05-was-silent))

Studio discovers metadata, builds ToolEntry records and exports `Library/GameCoreStudio/tool-catalog.json`. Context lists applicable entries and generates arguments/fields; the same tools drive manual and agent changes. Implement `IStudioTool` for a custom engine tool or use reflected `[AuthorOperation]` methods; `ILiveOpTranslator` is the separate runtime-translation seam. `set` supports `{field,value}` or `{fields}` and validates dynamic values with FieldValueChecker. Query/direct tools use `ToolRegistry.Invoke`; mutation goes through ChangeSetEngine. ([P1.6 §API and Decisions](packets/P1.6-studio-core-unity.md), [ContextPanelView.cs](../../Packages/com.gamecore.studio.ui/Editor/Context/ContextPanelView.cs))

Validators must report code, message and, when useful, hint, target/op and structured data. `Conflict` includes expected/actual; `StaleTarget` means missing/unloaded. Keep optional JSON members absent, never null. Worker candidates may not claim Applied state, outcomes, applied timestamps or GameCore operation links. Catalog revision checking happens where request and candidate meet. ([Authoring §9](03-authoring-contracts.md#9-diagnostics), [P0.3 validator rules](packets/P0.3-studio-model.md), [P1.6 decisions](packets/P1.6-studio-core-unity.md#decisions))

Core baseline diagnostic codes are `StaleTarget`, `Conflict`, `UnknownTool`, `InvalidArgs`, `MissingPrerequisite`, `ScopeNotAllowed`, `ValidationFailed`, `Refused`, `CandidateInvalid`, `StaleContext`, `StageFailed`, `LedgerConflict`, `NotConfigured`, `OutcomeUnknown`, `Blocked`, `MediaTypeForbidden`, `MediaPathForbidden`, `MediaImporterInvalid` and `ArtifactSourceForbidden`. Transport/companion codes keep their snake_case spellings; see the [creator troubleshooting table](08-creator-guide.md#troubleshooting). Gameplay validators use their package's stable GP/refusal codes. ([P0.3 API summary](packets/P0.3-studio-model.md#api-summary-namespace-gamecorestudiomodel-assembly-gamecorestudiomodel-editor-only), [ETOS §2](04-etos-integration.md#2-unity--companion-protocol-through-etos), [P1.3 interaction refusals](packets/P1.3-player-npc-interaction.md#interaction-comgamecoregameplayinteraction))

World reference arguments and pure-tool ReadOnly dispatch are fixed by P1.7b/R2-E. Nested references are core-owned; production discovery excludes fixture assemblies. Runtime commands belong to the Editor-only studio.gameplay adapter. ([R2-A](packets/R2-A-core-edit-recovery.md), [R2-E](packets/R2-E-views.md), [ADAPT-SPLIT](packets/ADAPT-SPLIT.md))

## Host verification

Run these on a dedicated host clone named `<packet>` after adding/resolving the package. Substitute its actual test namespace for the filter. These are invocation forms, not results from this documentation packet. ([Script headers](../../studio/tools/unity-compile.sh), [dotnet runner](../../studio/tools/dotnet-test.sh), [P1.3 verification](packets/P1.3-player-npc-interaction.md#verified))

```sh
studio/tools/dotnet-test.sh <packet> dotnet/tests/GameCore.Rules.Gameplay.Tests
studio/tools/unity-compile.sh <packet> games/hollowmere --tests EditMode --filter '<test-namespace>\..*'
studio/tools/unity-compile.sh <packet> games/hollowmere --tests PlayMode --filter '<test-namespace>\..*'
python3 tools/check_package_metadata.py
python3 tools/check_package_metadata.py --self-test
python3 tools/check_game_core_csharp.py
python3 tools/check_game_core_csharp.py --self-test
```

Use `python3 tools/make_unity_metas.py` when authoring missing metas, then inspect and commit them. This command writes files. `--sync-lock` also writes; it is not a substitute for Unity resolving a new package graph. `GAMECORE_OFFLINE=1` is only the documented opt-out when the advisory feed is unreachable, and means the NuGet audit is NotRun. ([Meta generator](../../tools/make_unity_metas.py), [package contract](../operator/packages.md#7-the-lock-file), [build §2.1](../operator/build-and-run.md#21-gamecore_offline1-the-one-documented-way-to-run-without-the-nuget-audit))

| Test layer | Expected coverage | Existing pattern |
|---|---|---|
| Dotnet | Pure accepted/refused transitions, units/bounds, deterministic ordering, duplicate request handling, metadata parity. | [P1.1 Verification](packets/P1.1-entities-world-compile.md#verification), [P1.3 Verified](packets/P1.3-player-npc-interaction.md#verified) |
| EditMode | Definition validation, catalog discovery, tool apply/undo, bake byte identity, explicit system/route wiring, save refusal/migration behavior. Include nested references when your fields have them. | [P1.6 Verified](packets/P1.6-studio-core-unity.md#verified-host-myubuntu-unity-6000075f1-one-instance), [P1.2 Verification](packets/P1.2-save-restore.md#2-verification), [P2.3 blockers](packets/P2.3-studio-views.md#left-open-for-the-integrator-and-owners) |
| PlayMode | Real game boot and committed effects, residency unload/reload, restore rebinding, one pump per frame, cleanup. A headless pass does not prove graphics/audio output. | [P1.3 Verified](packets/P1.3-player-npc-interaction.md#verified), [P1.5 Verification](packets/P1.5-ui-audio.md#verification) |
| Player/integrated | Generated registrations survive IL2CPP stripping; game behavior and budgets on the delivered revision. Integration owner runs V1 gates. | [Operator profile](../operator/profile.md), [Verification rules](07-verification-matrix.md), [Plan §3](06-implementation-plan.md#3-integration-checks-run-by-fable-per-merge) |

## Worked mechanism: pressure plate

1. Read `samples/mechanisms/pressure-plate/package`. Its package is **com.hollowmere.mechanism.pressureplate 0.1.0**, distinct from the `com.gamecore.*` 1.0.0 library rule. Rules and Runtime/Generated/Editor assemblies are separate, and Tests/Rules runs under dotnet and Unity. ([Sample Layout](../../samples/mechanisms/pressure-plate/README.md#layout))
2. Follow `PressurePlateRules.Press`: load adds one actor, unload subtracts one, and pressed means weight ≥ threshold. Check `plate.not-loaded`, `plate.overloaded`, `plate.unknown`, `plate.invalid-definition`. Runtime owns two int32 slots, `plate.pressed` and `plate.weight`, and a bounded `plate.press` lane of 16 rows. Accepted commands emit Pressed, Released or WeightChanged so even a non-flipping change has a committed event. ([Sample The mechanism](../../samples/mechanisms/pressure-plate/README.md#the-mechanism))
3. Follow `PressurePlateMechanism.Extend`, then `Attach` and `Place`/`Press`. Extend copies the base app definition, composes catalogs, registers plugin/system/route/readers/recipe/mount step. It still extends the built definition; its “WorldBuilder has no seam” note predates P1.3/P1.5 seams. It shares the payload-reader table and repeats an internal catalog comparator; these are sample limits. ([Sample Public API and Open items](../../samples/mechanisms/pressure-plate/README.md), [P1.3 seams](packets/P1.3-player-npc-interaction.md#seams-in-p11-code-integrator-please-review))
4. Follow `make-catalog.py` and `make-candidate.py`. The candidate carries change-set.json, a retained package archive, proposal and digests; regenerate candidates after any package file changes. The proposal names rules tests, catalog and smoke entry. ([Sample Regenerating](../../samples/mechanisms/pressure-plate/README.md#regenerating), [P2.4 API](packets/P2.4-staging-lane.md))
5. Run the full lane in a scratch clone with the built companion binary. A steps subset cannot yield an admission pass. The clean candidate must pass all seven checks; the forbidden overlay fails scan before execution and the failing-test overlay fails EditMode. ([Stage steps and verdict](../../studio/stage/README.md), [P2.4 Verified](packets/P2.4-staging-lane.md#verified-host-myubuntu-unity-6000075f1-net-8-rust-1971))

The four sample regeneration/check commands exercised by P4.2b are below. Regeneration writes only the maintained sample in a dedicated developer clone; do not modify a received worker candidate to make acceptance pass. ([sample §Regenerating](../../samples/mechanisms/pressure-plate/README.md#regenerating), [P4.2b §Requests](packets/P4.2b-live-acceptance.md#requests-to-other-packets))

```sh
python3 samples/mechanisms/pressure-plate/make-catalog.py
python3 samples/mechanisms/pressure-plate/make-catalog.py --check
python3 samples/mechanisms/pressure-plate/make-candidate.py
python3 samples/mechanisms/pressure-plate/make-candidate.py --check
```

### Build and provision before staging

The CLI requires a locally built binary **and a separately provisioned versioned cache**. P4.2b supplied the binary but launched straight into a fresh root; neither the host NuGet cache nor an old `_warm/Library` is automatically that root's cache. The runtime consequently refused `cache_invalid`. `stage-cache` below asks the built binary for the exact `_warm/<sha256>` path, provisions that path and checks all three offline inputs before a stage can consume cold grace. The identity binds the source project's `ProjectVersion.txt`, trusted package manifests and compiled-in NuGet/Unity-metadata/UPM lock bytes; do not substitute `cache-key.sh`'s historical version-only key. ([P4.2b receipt](../../artifacts/studio/verification/W-DOC-02/p42b-plugin-guide-built-prerequisite-20261006T045350.370953Z/command.log), [cache.py](../../studio/stage/cache.py), [cache_version](../../studio/agent/src/stage/pipeline.rs))

Prerequisites on the Linux host:

- Python 3.11+, Rust and .NET 8; a companion built from this checkout, not an installed immutable release or a sibling's build.
- Unity **6000.0.75f1** at the [stage launcher's fixed installation path](../../studio/agent/src/stage/sandbox.rs), the pre-provisioned `gamecore-stage:6000.0.75f1-v1` Docker image, working no-network Unity licensing and no concurrent Editor for this packet. No installed companion or etosd restart is needed.
- The pinned NuGet closure in `$HOME/.nuget/packages`, public UPM payloads in `$HOME/.cache/Unity/upm`, and a trusted resolved Library containing the exact [Unity metadata lock](../../studio/stage/cache/unity-metadata-lock.json) plus `PackageCache`. For a fresh checkout, first resolve/import it with `studio/tools/unity-compile.sh plugin-guide games/hollowmere`, then use `games/hollowmere/Library`. An operator's existing trusted Library is also an acceptable **seed only if every pinned digest matches**. Missing or changed bytes are a prerequisite failure: never change locks, skip checks or copy a complete cache root to hide it. ([Pinned NuGet closure](../../studio/stage/cache/cache-lock.json), [public UPM lock](../../studio/stage/cache/upm-lock.json))

```sh
export PATH="$HOME/.dotnet:$HOME/.cargo/bin:$PATH"
cargo build --release --manifest-path studio/agent/Cargo.toml
guide_work="$(mktemp -d "${TMPDIR:-/tmp}/gc-stage-guide.XXXXXX")"
export GAMECORE_STAGE_ROOT="$guide_work/stage"
python3 studio/etos/install-state.py stage-cache \
  --stage-root "$GAMECORE_STAGE_ROOT" --repo "$PWD" \
  --source-project "$PWD/games/hollowmere" \
  --offline-from "$HOME/.nuget/packages" \
  --unity-library "$PWD/games/hollowmere/Library" \
  --upm-from "$HOME/.cache/Unity/upm"
python3 studio/etos/install-state.py stage-cache \
  --stage-root "$GAMECORE_STAGE_ROOT" --repo "$PWD" \
  --source-project "$PWD/games/hollowmere" --verify
studio/agent/target/release/gamecore-studio stage run plate-example \
  --repo "$PWD" --root "$GAMECORE_STAGE_ROOT" --budget-s 360 \
  --candidate "$PWD/samples/mechanisms/pressure-plate/candidate" \
  --source-project "$PWD/games/hollowmere" \
  --verdict-out "$GAMECORE_STAGE_ROOT/plate-verdict.json"
```

The node-independent installer action does not read credentials, register projects, install binaries or restart services; do not pass its node `--root` option. Its stage-tool equivalent is `bash studio/stage/provision-cache.sh --stage-root "$GAMECORE_STAGE_ROOT" --repo "$PWD" --source-project "$PWD/games/hollowmere"` with the same three seed arguments. `--verify` in this root-derived mode requires NuGet **and** pinned analysis metadata **and** public UPM **and** the offline Unity PackageCache. Low-level exact-directory provisioning remains available for dependency-only sandbox probes; it is not the complete guide prerequisite. If NuGet is not already populated, omit `--offline-from` for an explicit host-only locked restore; candidate execution stays `--network none`.

For a repeatable diagnostic walkthrough after the build, [guide-flow.sh](../../studio/stage/guide-flow.sh) runs all four sample regeneration/check commands above, the two installer commands and the complete Docker stage. Its first argument must **not already exist**. To use a locally built debug binary, set `GAMECORE_STAGE_BINARY="$PWD/studio/agent/target/debug/gamecore-studio"`; otherwise it uses the release build. Example:

```sh
guide_work="$(mktemp -d "${TMPDIR:-/tmp}/gc-stage-guide.XXXXXX")"
bash studio/stage/guide-flow.sh "$guide_work/stage" "$PWD/games/hollowmere/Library"
```

The public UPM cache is mutable: a newly resolved Library does not guarantee it still contains every pinned historical registry record. R7-A's default `$HOME/.cache/Unity/upm` lacked one pinned record and correctly refused before staging. On this host, the operator's verified `stage/_warm/<version>/upm` retained the complete public closure. Supply that directory as the fourth `guide-flow.sh` argument (third is the NuGet source), or as `--upm-from` above. Provisioning copies only the pinned public records into the new private root and verifies every digest; it does not copy the warm root, candidate assemblies or cold-grace markers. The exact exercised invocation and full Docker XML are retained in [R7-A W-DOC-02](../../artifacts/studio/verification/W-DOC-02/r7-a/README.md).

Retain `stage/plate-verdict.json`, `stage/plate-example/out` and the command output. A diagnostic stage success requires all seven verdict steps passing, zero forbidden hits and `confinement: docker`, with the actual EditMode/PlayMode XML retained. Provisioning seeds only pinned metadata and public package inputs, never a warm `ArtifactDB`; the first cold stage retains the existing once-only 1800 s grace, then the 360 s warm budget. Never remove `.cold-grace-used`, widen deadlines or retry by disguising an existing cache as fresh. A cache preflight pass is not a stage pass; an unsigned CLI stage pass is not authenticated admission or the new lever exercise.

This private-root CLI is diagnostic only. For service staging, provision the exact **owner/version** cache returned for the registered `(app, project)` namespace; use the derivation in [provision-p42d.sh](../../artifacts/studio/verification/TOOLS/provision-p42d.sh), with complete verification (`provision-cache.sh "$stage_cache" --verify --complete`). Its source project and trusted package root must be the registration's exact checkout. Never mount the live project into the sandbox. ([P4.2d §Stage and admission](packets/P4.2d-live-rerun.md#stage-and-admission))

The historical `studio/tools/verify-all.sh final-guides` / `guide-stage` routes invoke the frozen P4.2b drivers, including a `1752ca8a` source assertion and paths under `.evidence/p42b-*`; they intentionally retain the original failing receipts and do not run this updated guide. Use `guide-flow.sh` above for new diagnostic evidence. Close W-DOC-02 only after the actual lever extension and the authenticated Stage → Admit → restored-world/undo flow below are also evidenced; cache provisioning alone does not change the row's acceptance criteria.

### Authenticated Stage to Admit

1. Register the paired project from its actual source checkout using [10](10-install-build-run.md). Submit the local candidate with `CompanionClient.StageAppCandidateAsync` through the authenticated app-origin route; worker candidates retained by the companion use `StageAsync`. The candidate panel's **Stage** follows these paths. Fixed sample IDs are project-scoped; do not reuse another project's request ID or delete a creator's journal.
2. Retain the job ID; poll job state. Fetch `/v1/stage/{job}/verdict` and verify that exact signed record through `/verify`. Only a complete trusted pass enables **Admit**. An unsigned CLI verdict or **Record verdict** file is insufficient.
3. Explicit creator **Admit** starts capture/stop/compile/restore/smoke. Use History for pending recovery and admission undo; no public `mechanism.admit` tool may substitute.
4. Observe the actual live world, restoration, smoke and undo. The historical P4.2d `catalog_mismatch` failure was subsequently resolved for the maintained pressure plate: [R6-E](packets/R6-E-resumer-rebinding.md) proves authenticated recovery and [W-MECH-01](../../artifacts/studio/verification/W-MECH-01/README.md) retains the later complete-row evidence. That does not establish a new lever. Follow the independent lever walkthrough below; only its own signed stage, live restoration and undo evidence can establish W-DOC-02.

([04 §6](04-etos-integration.md#6-staging-code-admission), [P4.2d §Stage and admission](packets/P4.2d-live-rerun.md#stage-and-admission), [W-DOC-02](../../artifacts/studio/verification/W-DOC-02/README.md))

### GameBoot admission binding

Keep the hook in the game's trusted Editor assembly. Hollowmere's `HollowmereStudioAdmission` rebinds on Editor/session startup and `GameBoot.SavesInstalled`; `GameBoot.AdmissionReady(service)` checks the current restored-capable world. The binding is:

```csharp
StudioAdmissionServices.BindAdmission(
    runtime,
    () => service,
    () => boot != null && boot.AdmissionReady(service),
    verdict => StudioAdmissionServices.RunSmokeTest(runtime, verdict,
        (type, method, steps) => smoke.RunAdmittedSmokeEntry(verdict, type, method, steps)));
```

The tri-state smoke callback returns Pending/Succeeded/Failed and advances on game frames. Hollowmere allows up to 120 proposal steps with a 240-poll allowance; it checks a trusted registered entry and exact admitted assembly/package identity. It does not invoke arbitrary candidate-named callbacks. Persist progress or fail safely after recovery; mark Applied only after smoke succeeds. ([HollowmereStudioAdmission.cs](../../games/hollowmere/Assets/Hollowmere/Authoring/Editor/HollowmereStudioAdmission.cs), [P3.1 §3](packets/P3.1-hollowmere-complete.md#3-studio-admission-in-the-real-game-r2-g-request-4-as-superseded-by-r2-g2), [R2-B2](packets/R2-B-admission.md#r2-b2--asynchronous-admission-smoke-packetmd))

### New lever: author, stage, admit, observe and undo

W-DOC-02 requires a **new lever mechanism**, not a renamed pressure plate or an existing `interaction.addDoor` placement. The maintained recipe is [Mechanisms/Lever~](../../games/hollowmere/Assets/Hollowmere/Mechanisms/Lever~/). Unity ignores the directory ending in `~`; its C# sources are **not imported into the live game**. The generated candidate below also stays outside live Assets/Packages. Never remove the tilde, copy this package into live imports yourself, or treat an unsigned CLI verdict as admission authority.

The package is **com.hollowmere.mechanism.lever 0.1.0**. Its pure rule is a bistable latch: one toggle changes `0 → 1` or `1 → 0`, and the position remains latched until another toggle. There is no actor weight, threshold or occupancy. `lever.state` is an authoritative int32 slot with PreserveDormant policy. `lever.toggle` accepts one int32 turn count, exactly `1`; its ingress lane is bounded to **16 commands / 64 payload bytes per step**, with reject-before-mutation overflow. Accepted transitions reserve and commit `LeverToggled{target,state}` before writing the slot; malformed, unknown-target, invalid-state, invalid-command and event-budget refusals do not mutate it. The binder rotates the handle and changes its indicator from committed slots after the ordinary game pump; it skips a null graphics device in batchmode and releases its view on detach.

| Recipe part | Concrete implementation |
|---|---|
| Pure rules and tests | `package/Rules/LeverRules.cs`, `package/Tests/Rules/LeverRulesTests.cs`: alternating latch states, corrupt-state refusal and rejection of zero/multiple turns. Stage compiles these same pure sources for dotnet and Unity. |
| Kernel and target | `LeverDeclarations`, `LeverKernel`: distinct stable names under `gameplay.hollowmere.lever.*`, one recipe, one world-scope lever target, command reader, bounded lane, system and committed event. `LiveTargetSeeder.TrySeed` creates empty slot storage and does **not** invoke the recipe applier; explicit post-seed initialization is therefore required. `Attach(seedSlots:false)` never overwrites restored slots. |
| Normal gameplay composition | Public sealed `Hollowmere.Mechanism.Lever.LeverWorldExtension`, public parameterless constructor, assembly `Hollowmere.Mechanism.Lever`; implements `IGameplayWorldExtension` and `IGameplayWorldTargets`. `public int State { get; }` reads committed `0/1` (`-1` means absent/detached); `public bool Toggle()` enqueues only and returns ingress admission, **not** completion. It uses the root's resumed operation sequence and never pumps. The reviewed `public bool InitializeNewTarget()` initializes missing state to `0` after target creation, preserving existing `0/1` state. Fresh `Attach(true)` and sandbox startup invoke it after root attachment; the trusted live adapter invokes it only after additive target/mount composition. |
| Player interaction | The runtime **Toggle lever** button near the top-left of the game view, below the HUD, calls the same bounded `Toggle()` command once per click. Its **Lever: Off/On** label and the 3D handle change only after a committed slot update; unavailable targets disable the button. The binder owns its runtime UI Toolkit panel and releases it on detach. This is a real player control, not an Editor tool or autonomous smoke loop. |
| Catalog | `make-catalog.py` uses the repository emitter to generate `Hollowmere.Mechanism.Lever.Generated.LeverCatalog` in assembly `Hollowmere.Mechanism.Lever.Generated`, its coverage, a hash over the other package files, and stable metas. The reviewed game registry composes this exact catalog with the existing world catalog, preserving the actual `CatalogSet` fingerprint. There is no candidate Editor contributor/discovery hook. |
| Sandbox only | `Hollowmere.Mechanism.Lever.LeverSmoke.Begin()` returns the Stage harness session with `Root`, `Step(int)`, `SlotHash()` and `Dispose()`. Three scheduled toggles exercise `0 → 1 → 0 → 1` with the normal PlayerLoop. **The live admission adapter never invokes Begin.** |

**Why this sample does not use `IGameplayCatalogContributor`:** the admission path calls the regular bake's `Entry.Verify`; it does not regenerate the checked-in game catalog. Discovering a new contributor there would change the staged base-world fingerprint while the runtime still loads the old generated base catalog. The reviewed trusted composition seam therefore adds the exact lever generated catalog explicitly, with the real combined fingerprint, and supplies the normal `WorldBuildOptions.Extensions` plan. It does not substitute a baseline hash for the combined catalog or discover arbitrary candidate catalog types. The normal contributor seam above remains the standard for packages delivered together with a regular rebake.

#### 1. Author an external candidate

Run at the repository root in a dedicated developer checkout. Python 3.11+ is sufficient for these generation commands; they do not build, install or execute candidate C#. `make-candidate.py` reuses the maintained sample's canonical tarball and change-set schema machinery, verifies catalog/hash/metas are current, and refuses output below live Assets/Packages. The proposal names the real rule-test sources; it does not invent pre-stage passing test counts.

```sh
lever_recipe="$PWD/games/hollowmere/Assets/Hollowmere/Mechanisms/Lever~"
lever_work="$(mktemp -d "${TMPDIR:-/tmp}/gc-lever-guide.XXXXXX")"
python3 "$lever_recipe/make-catalog.py"
python3 "$lever_recipe/make-catalog.py" --check
python3 "$lever_recipe/make-candidate.py" --output "$lever_work/candidate"
python3 "$lever_recipe/make-candidate.py" --output "$lever_work/candidate" --check
```

The candidate is exactly `$lever_work/candidate/change-set.json` plus `artifacts/package.tgz` and `artifacts/proposal.json`; the command prints their digests. Author changes under `Lever~/package`, then rerun both generators. Regenerate before submitting, never alter an already staged candidate to make its retained verdict pass. The default candidate ID is scoped to the paired project; a repeated independent attempt in the same project must supply a new schema-valid `--id` rather than erase its journal.

#### 2. Run the signed service walkthrough

Meet [the provisioning prerequisites above](#build-and-provision-before-staging) first: local companion build, exact licensed Docker image, pinned NuGet/public UPM inputs and a trusted resolved Unity Library. This graphical walkthrough additionally requires the host's working `:1` display and no concurrent Editor for this project. The project must not already have `UserSettings/GameCoreStudio.json`; use a dedicated checkout rather than deleting or reading another creator's pairing. The operator's etos binaries must already exist under `$HOME/.local/opt/etos/bin`; the runner starts a **private scratch node**, never modifies/restarts installed services, and configures no providers, workers or paid operations.

```sh
export PATH="$HOME/.dotnet:$HOME/.cargo/bin:$PATH"
cargo build --release --manifest-path studio/agent/Cargo.toml
python3 games/hollowmere/Assets/Hollowmere/Tests/R8_B/Lever/run-probe.py \
  walkthrough "$lever_work/walkthrough" \
  --candidate "$lever_work/candidate" \
  --companion "$PWD/studio/agent/target/release/gamecore-studio" \
  --unity-library "$PWD/games/hollowmere/Library" \
  --upm-from "$HOME/.cache/Unity/upm"
```

On the first attempt, `$lever_work/walkthrough` must not exist and must be outside the checkout. If the mutable default public UPM cache lacks pinned historical records, pass the complete verified public `upm` directory described above; never relax its lock/digest checks or copy a whole warm root. The runner derives the exact app/project owner namespace and provisions its versioned cache before submitting candidate bytes. It uses the real production `CompanionClient.StageAppCandidateAsync`, retains/polls the service job, and fetches/verifies the signed verdict. **Invoking this walkthrough explicitly authorizes its driver to perform creator Admit and normal History Undo after the trusted pass**; it is not a read-only staging command and does not wait for manual button clicks. No pre-Admit package import is part of this command.

To correct a failed candidate, generate a **new change-set id**, then invoke the same work directory with that candidate. The runner archives the preceding evidence and reuses the same private node state, owner/version cache and cold-grace marker. After an admission has succeeded it requires a matching verified Undo receipt before accepting another candidate. Never reset the cache or creator journal to disguise a retry. Admission or undo failures require their normal durable recovery path.

The source Editor exports the complete `StageCandidateRequest` and current tool catalog, then exits and releases its host lease. The Unity-excluded `Submit~/LeverSubmit.csproj` invokes the production `CompanionClient.StageAppCandidateAsync` using the production pairing resolver. Only then may the companion launch a sandbox Editor. A second graphical Editor opens after Stage has finished to perform creator review/admission. This serializes this packet's source and sandbox Editors rather than holding two slots during an HTTP request.

The driver performs and records these same creator workflow boundaries in its owned graphical Editor:

1. Resolve the lever identity and the complete **signed seven-step Docker verdict** through the production candidate coordinator. A cache preflight, the old registry probe, an unsigned CLI verdict, or a partial step set does not enable admission.
2. Explicitly invoke the coordinator's `Admit(entry, captureAndStop: true)` action. This captures the old world, stops Play, installs/compiles the package, verifies the signed catalog prediction, composes the reviewed catalogs and restores. It is the trusted driver's creator action, not a candidate callback or public `mechanism.admit` tool.
3. Wait for restored Play and the trusted smoke to finish through normal game frames. Before adding new lever topology, the restored world must complete an equal save roundtrip; the walkthrough separately verifies that the captured **nine OldCoin** inventory state survives admission. This is not an assertion that full-world initial and final hashes remain equal across advancing NPC/game frames. Then the game-owned adapter mounts the declared plugin/seeds only an absent target, observes the real committed sequence **off/on/off (`0,1,0`)**, and records the normal pump evidence. Merely returning `true` from `Toggle()` is not success.
4. After admission smoke, activate the attached runtime `lever-toggle` button through its ordinary UI submit event, wait for committed **on**, activate it again and wait for committed **off**. The driver retains `interactive-lever.json` and `lever-off-before`, `lever-on`, `lever-off-after` Play screenshots plus camera captures of the actual playing world (`*-play.png` and `*-world.png`), rather than recreating the lever in a preview scene.
5. Explicitly invoke `runtime.History.Undo(changeSetId)` and wait for the previous catalog to restore and the lever package to disappear, rather than deleting package files. Retain `$lever_work/walkthrough/evidence`, the signed verdict and retained stage outputs, package/proposal digests, equal-roundtrip and nine-coin witnesses, committed state observations, graphical captures and undo result. A timeout or refusal is retained as failure, never rewritten as acceptance.

For the equivalent manual flow in a separately paired project, use the candidate panel's **Stage**, review the verified complete verdict, explicitly click **Admit**, inspect restored Play, then use admission **Undo** in History. Do not run that manual flow concurrently with the automated walkthrough on the same project.

The literal workflow is qualified by [R8-C's signed run on `09430b30`](../../artifacts/studio/verification/W-DOC-02/r8-c/README.md): all seven Stage steps, authenticated creator Admit, restored nine-coin state, committed off/on/off, visible runtime control, readable `:1` captures and normal Undo pass. The evidence separately retains failed development attempts and three unchanged broad-suite projection failures; this is W-DOC-02 acceptance, not an all-green project or a promise that another project's different mechanism is reviewed.

#### 3. Reviewed game-author process for another mechanism

The old fixed pressure-plate-only dispatch was a genuine R8-B blocker. [SADR-056](decisions/SADR-056-trusted-game-extension-registry.md) records its replacement by the reviewed, game-owned [HollowmereExtensionRegistry](../../games/hollowmere/Assets/Hollowmere/Authoring/Editor/HollowmereExtensionRegistry.cs), not by candidate self-registration. Its immutable per-instance `Entries` and ordinal `Find(smokeType, smokeMethod)` retain the pressure-plate identity and explicitly name the lever package, runtime assembly and extension type. The trusted resolver verifies the loaded type's assembly and that its asmdef belongs to `<project>/Packages/com.hollowmere.mechanism.lever` with a matching package manifest. Unknown smoke entries, aliases, wrong package ownership and incompatible public signatures fail closed. `RunAdmittedSmokeEntry(StageVerdict,string,string,int)` remains the entry boundary.

For a genuinely new mechanism, a **game author**, in a reviewed trusted-source change before candidate admission, must add its exact package/assembly/type/smoke identity and implement a bounded game-owned live adapter that checks restored checkpoint continuity and observes its declared committed effects. Use `WorldBuildOptions.Extensions` and optional `IGameplayWorldTargets`; integrate the exact generated catalog through the reviewed composition seam for admission, or use a normal contributor when delivering a regular rebake. Do not discover arbitrary candidate callbacks or add a candidate-side registry API. Review minimum/maximum smoke steps, headless behavior, fresh/restored attachment, preserved slots, one-pump behavior and normal undo. Only after that trusted integration may a separate candidate go through the same authenticated Stage and explicit Admit boundary. A registry source change alone confers neither a signed verdict nor creator authorization.

## Delivery checklist

Each row is a review requirement derived from the cited contract or known failure, not an additional product capability. ([Plan delivery rules](06-implementation-plan.md#1-working-method))

| Check | Evidence to deliver | Source |
|---|---|---|
| Package graph | Exact dependencies, preserved metas, resolved lock, passing metadata/C# checks. | [Package contract](../operator/packages.md) |
| Authoring | Stable ids, recognized types/categories, discoverable tools, shared validation codes. | [Authoring §1, §4, §9](03-authoring-contracts.md) |
| Runtime | Explicit system registration, bounded routes, slots for progress, committed events/refusals, one pump. | [05 cross-cutting](05-plugin-catalog.md#cross-cutting-requirements-for-every-package), [P0.4 API](packets/P0.4-kernel-app.md#2-public-api-of-comgamecoreunityapp-for-p11--p12--p16) |
| Presentation | Headless skip, residency teardown, no authoritative writes. | [PresentationContracts.cs](../../Packages/com.gamecore.gameplay.contracts/Runtime/PresentationContracts.cs) |
| Persistence | Exact recipe compatibility, migration tests, outbox capture/replay, attach without seeding and new-session bindings. | [P1.2](packets/P1.2-save-restore.md), [P1.4 Open](packets/P1.4-dialogue-quest-logic-inventory.md#open) |
| Bake | Deterministic description/output and updated stamps; verify stripping roots in the player. | [P1.1](packets/P1.1-entities-world-compile.md), [operator catalogs](../operator/catalog-generation.md) |
| Mechanism | Scan/checkers before code; full exact-byte verdict; admission/rollback/undo tests; no claimed capture without game hook. | [P2.4](packets/P2.4-staging-lane.md) |
| Handoff | Packet note with public API, source revision, commands/results and all remaining blockers. Reserve shared-file edits for integrator. | [Plan §1–§3](06-implementation-plan.md) |

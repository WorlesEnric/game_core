# GameCore Studio plugin developer guide

Use the shipped entities and interaction packages as the gameplay package pattern. Use the pressure-plate sample for an agent-proposed mechanism. This guide describes the committed implementation and keeps the integration blockers recorded by the packet owners. ([P1.1](packets/P1.1-entities-world-compile.md), [P1.3](packets/P1.3-player-npc-interaction.md), [P2.4](packets/P2.4-staging-lane.md))

## Package and assembly shape

| Part | Pattern to follow | Source |
|---|---|---|
| `package.json` | `com.gamecore.*` packages use version `1.0.0`, Unity `6000.0`, accurate name/displayName/description and the exact asmdef-derived dependency set. Do not copy dependencies blindly. | [Package contract §1, §6, §9](../operator/packages.md), [entities manifest](../../Packages/com.gamecore.gameplay.entities/package.json) |
| `Runtime/` | Definitions, declarations, command systems, per-world modules and presentation. Reference contracts, rules, runtime and the exact Unity assemblies used. No runtime dependency on Studio. | [entities Runtime asmdef](../../Packages/com.gamecore.gameplay.entities/Runtime/GameCore.Gameplay.Entities.asmdef), [P1.1 mirror decision](packets/P1.1-entities-world-compile.md#authoring-identity-by-convention-for-studio-discovery-without-a-type-dependency) |
| `Editor/` | Authoring tools, validators and catalog contributors; asmdef `includePlatforms: ["Editor"]`. Gameplay mirror metadata does not require a Studio reference. | [entities Editor asmdef](../../Packages/com.gamecore.gameplay.entities/Editor/GameCore.Gameplay.Entities.Editor.asmdef), [interaction tools](../../Packages/com.gamecore.gameplay.interaction/Editor/InteractionTools.cs) |
| Pure rules | Reusable groups currently live at `com.gamecore.rules.gameplay/Runtime/<Group>/` under one `GameCore.Rules.Gameplay` asmdef, `noEngineReferences: true`. This differs from 02's original one-asmdef-per-group sketch. | [P1.1 §What was built](packets/P1.1-entities-world-compile.md#what-was-built), [rules asmdef](../../Packages/com.gamecore.rules.gameplay/Runtime/GameCore.Rules.Gameplay.asmdef), [02 layout](02-architecture.md#3-repository-layout-new-and-changed-paths) |
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

Identity uses a serialized `authoringId`, public `AuthoringId`, and `IAuthoredObject`. Definitions also implement `IDefinitionAsset` with `DefinitionName` and `ContentStamp`. IDs are lowercase, nonzero D-format GUIDs. Use `AuthoringIds.TargetIdFor(id)`, which calls `StableNameKeyDerivation.Derive("auth." + id)`. Never persist Unity instance ids, Entity indexes or session handles as authoring identity. Prefab assets carry no entity authoring id; placed instances receive one. Unity Ctrl+D can copy a serialized id, so the Studio duplicate tool is the currently documented safe duplication path; duplicate-id validation remains open. ([P1.1 §API](packets/P1.1-entities-world-compile.md#api), [P1.6 §Left open](packets/P1.6-studio-core-unity.md#left-open))

**Current blocker:** gameplay uses `[AuthorField(Type = "authoringId")]` in narrative definitions, but Studio's value-type reader rejects that type. P2.3 used an uncommitted host alias for some evidence. Do not copy that type override into a new Studio-facing field and claim it works; resolution belongs to the integration owner. Nested-list `set` inverses also block dialogue undo. ([P2.3 open items 1–2](packets/P2.3-studio-views.md#left-open-for-the-integrator-and-owners))

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

Definition hashes cover canonical authorable fields, type id and recursively referenced definitions; asset references contribute GUID/fileId. Recipe revision is the first eight digest bytes. A referenced variant edit can therefore change its parent definition revision. Package-content hash, definition stamp, recipe revision and overall catalog fingerprint have different roles; retain all the generated outputs from a content change. ([P1.1 decision 8 and Verification](packets/P1.1-entities-world-compile.md), [InteractionDeclarations.PackageContentHash](../../Packages/com.gamecore.gameplay.interaction/Runtime/InteractionDeclarations.cs))

| Change | Save consequence / obligation | Source |
|---|---|---|
| Ordinary slot value | Captured through declared owner/slot rows; do not hide progress only in a component. | [SADR-004](02-architecture.md), [P1.2 §1.1](packets/P1.2-save-restore.md#11-production-restore-builder-comgamecoreunityruntime-runtimepersistenceproductioncs) |
| Slot schema version | Register an unambiguous forward `SlotMigrationStep` and bind the owner/slot in SlotSchemaCatalog or SaveSchemaDefinition. Missing path is `save.migration-path-missing`. | [P1.2 §1.2 and §3](packets/P1.2-save-restore.md) |
| Recipe revision | Restore requires the exact captured revision. Declaring a compatible catalog alone does not migrate recipes. | [P1.2 §1.1 and §3](packets/P1.2-save-restore.md) |
| Catalog fingerprint | Must be identical or explicitly compatible; recipe/schema checks still apply. | [P1.2 §1.2](packets/P1.2-save-restore.md) |
| Clock schema / component state | No executable clock/recipe migration or arbitrary component-state capture is supplied. | [P1.2 §5](packets/P1.2-save-restore.md#5-open-items) |
| Restore success | New WorldId/root. Dispose old bindings, use ActiveRoot/RootChanged, reattach with `seedSlots:false`, recreate presentation, resume issuer sequences and reconstruct derived caches. | [P1.1 root replacement](packets/P1.1-entities-world-compile.md#runtime), [P1.2 §3 and §5](packets/P1.2-save-restore.md) |
| Pending cross-plugin effects | Persist/reinstate the outbox and stable receiver request ids. Narrative uses `RequestIdOf(outboxId)` and a bounded request ring; its save-composer registration remains open. | [P1.4 outbox decision and Open](packets/P1.4-dialogue-quest-logic-inventory.md) |

Provide game checkpoint codecs through `SaveServiceOptions.Codecs`; no shared game codec catalog exists in these packets. Rebind narrative modules, player/NPC/interaction sessions and loaded scenes, including runtime-spawned targets. P1.2's headless restore proof and P1.5's test-codec UI proof do not demonstrate this full game integration. `GameApplication.Current` is not updated to the restored root; code using it needs explicit review. ([P1.1 §Runtime and Open](packets/P1.1-entities-world-compile.md), [P1.2 §5](packets/P1.2-save-restore.md#5-open-items), [P1.5 §Open](packets/P1.5-ui-audio.md#open))

## Tools, validation and diagnostics

The catalog has **Configure**, **Compose**, **Mechanism** tiers. Configure changes existing fields/references; Compose changes structure/content; Mechanism proposes a package for staging. There is no Agent tier. Current dialogue/audio media tools use inconsistent tiers, documented by P1.4/P1.5, so a new tier requires a contract decision rather than a local enum addition. ([Authoring §5](03-authoring-contracts.md#5-tools-owner-studiocore-edit-registry-populated-by-plugins), [P1.4 decisions](packets/P1.4-dialogue-quest-logic-inventory.md#decisions-where-05-was-silent), [P1.5 decision 10](packets/P1.5-ui-audio.md#decisions-where-05-was-silent))

Studio discovers metadata, builds ToolEntry records and exports `Library/GameCoreStudio/tool-catalog.json`. Context lists applicable entries and generates arguments/fields; the same tools drive manual and agent changes. Implement `IStudioTool` for a custom engine tool or use reflected `[AuthorOperation]` methods; `ILiveOpTranslator` is the separate runtime-translation seam. `set` supports `{field,value}` or `{fields}` and validates dynamic values with FieldValueChecker. Query/direct tools use `ToolRegistry.Invoke`; mutation goes through ChangeSetEngine. ([P1.6 §API and Decisions](packets/P1.6-studio-core-unity.md), [ContextPanelView.cs](../../Packages/com.gamecore.studio.ui/Editor/Context/ContextPanelView.cs))

Validators must report code, message and, when useful, hint, target/op and structured data. `Conflict` includes expected/actual; `StaleTarget` means missing/unloaded. Keep optional JSON members absent, never null. Worker candidates may not claim Applied state, outcomes, applied timestamps or GameCore operation links. Catalog revision checking happens where request and candidate meet. ([Authoring §9](03-authoring-contracts.md#9-diagnostics), [P0.3 validator rules](packets/P0.3-studio-model.md), [P1.6 decisions](packets/P1.6-studio-core-unity.md#decisions))

Registered Studio diagnostic codes are `StaleTarget`, `Conflict`, `UnknownTool`, `InvalidArgs`, `MissingPrerequisite`, `ScopeNotAllowed`, `ValidationFailed`, `Refused`, `CandidateInvalid`, `StaleContext`, `StageFailed`, `LedgerConflict`, `NotConfigured`, `OutcomeUnknown` and `Blocked`. Transport/companion codes keep their snake_case spellings; see the [creator troubleshooting table](08-creator-guide.md#troubleshooting). Gameplay validators use their package's stable GP/refusal codes. ([P0.3 API summary](packets/P0.3-studio-model.md#api-summary-namespace-gamecorestudiomodel-assembly-gamecorestudiomodel-editor-only), [ETOS §2](04-etos-integration.md#2-unity--companion-protocol-through-etos), [P1.3 interaction refusals](packets/P1.3-player-npc-interaction.md#interaction-comgamecoregameplayinteraction))

Current integration traps: extra world references lack AuthorArg bindings; pure preview/simulate/explain tools are not marked ReadOnly; nested references are contributed by Views rather than core; testable fixture tools leak into the project catalog. A clean-project catalog assertion must account for these instead of claiming all discovered tools are shipping tools. ([P2.3 open items 3–6](packets/P2.3-studio-views.md#left-open-for-the-integrator-and-owners), [P1.6 §Left open](packets/P1.6-studio-core-unity.md#left-open))

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

The following uses the binary path from the companion install and the CLI options from P2.4. It is for an operator running verification, not a command executed by this docs packet. ([P0.5 §5](packets/P0.5-companion.md#5-expectations-for-p01-install), [P2.4 CLI](packets/P2.4-staging-lane.md))

```sh
studio/agent/target/release/gamecore-studio stage run plate-example \
  --candidate samples/mechanisms/pressure-plate/candidate \
  --source-project games/hollowmere --verdict-out /tmp/plate-verdict.json
```

6. In the candidate panel, record the verdict and **Admit** while Play is stopped. The verdict covers change-set id, proposal/package digests and every file. Admission recompiles, checks and verifies the predicted live catalog set, or rolls back. Undo must use `StageAdmission.Undo`; History routes it there. ([P2.4 §Decisions](packets/P2.4-staging-lane.md#decisions), [P2.1 §Decisions](packets/P2.1-studio-ui.md#decisions))
7. Inspect the [P2.4 evidence](../../artifacts/studio/evidence/P2.4/README.md): the plate pressed/released in Thornwick Village and undo restored the catalog hash. This proves stage/admit/game-smoke/undo, not full production checkpoint resumption. The six-minute stage budget was measured with warm caches; a game must register its own capture hook. Fixed sample ids apply once per project; `w-mech-01.sh --reset-journal` is scratch-only. ([P2.4 Verified and Open items](packets/P2.4-staging-lane.md))

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

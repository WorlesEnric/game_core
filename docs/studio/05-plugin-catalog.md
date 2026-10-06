# GameCore Studio: plugin capability catalog

**Status:** final inventory as of P4.2d (2026-10-06). One row per capability group of the mandate (§9), naming the
package, its authoritative state (int32 slots per SADR-004), commands/events, Unity-side binders, authoring
definitions, Studio tools, and the group's acceptance test. Every group has a Unity-free `Rules` half under
`com.gamecore.rules.gameplay/Runtime/<Group>/` (tested with dotnet) and an ECS/Unity half in its own package. Kernel
isolation rules (`tools/gc024_genre_audit.py`) apply: the kernel never names these packages.

Conventions:
- Slots are named `<group>.<name>`; values are `int`. Positions are millimetres; angles are milliradians; durations are milliseconds; money is integer units.
- Every command carries a client `RequestId`; duplicate ids are acknowledged, not re-applied (kernel `RequestLedger`).
- Every refusal is a stable code in `GameCore.Rules.Gameplay.<Group>.Refusals`.
- Every group registers `[Authorable]` metadata and at least one Studio validator.
- Presentation binders run on the main thread after step commit, from the committed image; they never write state.

| # | Group / package suffix | Runtime capability | Authoring surface / reference categories | Final tool IDs | Acceptance / source |
|---|---|---|---|---|---|
| 1 | World and regions, `com.gamecore.gameplay.world` | Region residency, visits, canonical world pose and spawn ordinal; additive streaming with restored-residency reconciliation. | WorldDefinition, RegionDefinition, PortalDefinition; world.region | `world.addPortal`, `world.configurePortal`, `world.connectRegions`, `world.setRegionBounds`, `world.setSpawnPoint` | W-PLUG-01; [P1.1-entities-world-compile](packets/P1.1-entities-world-compile.md) |
| 2 | Entities and presentation, `com.gamecore.gameplay.entities` | Alive, variant, scale and visibility slots; prefab/material presentation follows committed state and residency. | EntityDefinition, VariantDefinition, AuthoredEntity; entity.definition, entity.variant, entity.instance | `entity.applyOverride`, `entity.duplicate`, `entity.layoutLine`, `entity.layoutRing`, `entity.place`, `entity.replaceDefinition`, `entity.setMaterialTexture`, `entity.setVariant` | W-PLUG-02; [P1.1-entities-world-compile](packets/P1.1-entities-world-compile.md) |
| 3 | Player exploration, `com.gamecore.gameplay.player` | Committed movement, stamina, vertical speed/grounded; input, camera, focus and typed interaction dispatch. | PlayerDefinition and InputProfile; player.definition | `player.setCamera`, `player.setSpawn`, `player.tuneMovement` | W-PLUG-03; [P1.3-player-npc-interaction](packets/P1.3-player-npc-interaction.md) |
| 4 | NPCs, `com.gamecore.gameplay.npc` | Behaviour, patrol progress, schedule and appearance; world pose remains authoritative across unload/restore. | NpcDefinition, BehaviourDefinition, ScheduleDefinition; npc.definition | `npc.addAt`, `npc.setAppearance`, `npc.setBehaviour`, `npc.setDialogue`, `npc.setPatrol`, `npc.setSchedule` | W-PLUG-04; [P1.3-player-npc-interaction](packets/P1.3-player-npc-interaction.md) |
| 5 | Interaction, `com.gamecore.gameplay.interaction` | Interactable state/uses/cooldown/occupancy; committed success/refusal, conditions and action delivery. | InteractableDefinition, TriggerDefinition; interaction.interactable | `interaction.addDoor`, `interaction.addExaminable`, `interaction.addTrigger`, `interaction.explain`, `interaction.linkCondition`, `interaction.setActions`, `interaction.setStates` | W-PLUG-05; [P1.3-player-npc-interaction](packets/P1.3-player-npc-interaction.md) |
| 6 | Dialogue and narrative, `com.gamecore.gameplay.dialogue` | Conversation progression, visited state and facts; condition evaluation and exactly-once outbox effects at committed boundaries. | DialogueGraph, FactDefinition; dialogue.graph, narrative.fact | `dialogue.addChoice`, `dialogue.addLine`, `dialogue.generateVoice`, `dialogue.linkCondition`, `dialogue.preview`, `dialogue.setConsequence`, `dialogue.setFact`, `dialogue.setFactCondition` | W-PLUG-06; [P1.4-dialogue-quest-logic-inventory](packets/P1.4-dialogue-quest-logic-inventory.md) |
| 7 | Quests and progression, `com.gamecore.gameplay.quest` | Status/stage/branch/objectives; prerequisites, dependent failure closure, terminal actions and committed runtime query. | QuestDefinition; quest.quest | `quest.addObjective`, `quest.addStage`, `quest.inspectRuntime`, `quest.linkReward`, `quest.setBranch`, `quest.setConsequence`, `quest.setPrerequisites`, `quest.simulate` | W-PLUG-07; [P1.7c-gameplay-runtime-followups](packets/P1.7c-gameplay-runtime-followups.md) |
| 8 | Items and economy, `com.gamecore.gameplay.inventory` | Inventory counts, currency and pickup/trade operations; obligation-based grant/consume/buy delivery. | ItemDefinition, VendorDefinition, LootTable; inventory.item, inventory.vendor | `inventory.bindUse`, `inventory.grantStarting`, `inventory.placeItem`, `inventory.setPrice`, `inventory.setStock` | W-PLUG-08; [P1.4-dialogue-quest-logic-inventory](packets/P1.4-dialogue-quest-logic-inventory.md) |
| 9 | Reusable rules, `com.gamecore.gameplay.logic` | Rule firing/cooldowns/counters, typed conditions/actions, explain traces and numeric per-install config. | RuleDefinition, ConditionSet, ActionSet; logic.rule, logic.conditionSet, logic.actionSet | `logic.addRule`, `logic.explain`, `logic.test`, `logic.whyNot`, `authoring.migrateRefs` | W-PLUG-09; [P1.7c-gameplay-runtime-followups](packets/P1.7c-gameplay-runtime-followups.md) |
| 10 | UI and player flow, `com.gamecore.gameplay.ui` | Screen flow, committed view models and typed commands; HUD objective binding includes QuestStageTitle. | UiDocumentDefinition, screen/theme/binding definitions | `ui.addScreen`, `ui.bind`, `ui.previewScreen`, `ui.setCommand`, `ui.setTheme` | W-AI-04, W-GAME-05; [P1.5-ui-audio](packets/P1.5-ui-audio.md) |
| 11 | Audio and atmosphere, `com.gamecore.gameplay.audio` | Music/ambience/settings and presentation cues; resident audio binders and generated voice through the registered gateway. | AudioBankDefinition, music/ambience definitions; audio.clip, audio.musicState | `audio.assignClip`, `audio.generateSfx`, `audio.generateVoice`, `audio.setAmbience`, `audio.setMusicState` | W-PLUG-11; [P1.5-ui-audio](packets/P1.5-ui-audio.md) |
| 12 | Persistence and recovery, `com.gamecore.gameplay.save` | Slot/composition/clock checkpoint capture, exact recipe restore and forward slot migrations; game-owned codecs and rebinding. | SaveSchemaDefinition; SaveService in Unity app | `save.inspect`, `save.testRoundTrip` | W-PERSIST-01..03; [P1.2-save-restore](packets/P1.2-save-restore.md) |

These are capability summaries, not a replacement for declaration slot/route schemas. The exact authoring flags are listed below; gameplay hardening supersedes original packet limitations. Studio gameplay integration is the separate Editor-only `com.gamecore.studio.gameplay` package for revision-checked live actions and trusted admission bindings; it is excluded from stage slots. ([P1.7a](packets/P1.7a-gameplay-hardening-runtime.md), [P1.7b](packets/P1.7b-gameplay-hardening-metadata.md), [ADAPT-SPLIT](packets/ADAPT-SPLIT.md))

## Cross-cutting requirements for every package

- `package.json` version `1.0.0`, `unity 6000.0`, exact dependency set (checker), lock entry in `games/hollowmere` and `games/cleanproof`.
- `Runtime/` asmdef references: `GameCore.Contracts`, `GameCore.Unity.Runtime`, `GameCore.Unity.Adapters`, `GameCore.Gameplay.Entities` (where needed), `Unity.Entities`, `Unity.Collections`, `Unity.Mathematics`, plus the allowlisted engine assemblies the group needs (Input System for player, AI Navigation for npc, UI Toolkit modules for ui).
- `Editor/` asmdef holds authoring metadata registration, inspectors generated by Studio (no hand-written inspectors except for custom previews), validators and the group's tool implementations (uses gameplay mirror metadata; Studio adapters live in com.gamecore.studio.gameplay).
- `Runtime/<Group>/` (in `com.gamecore.rules.gameplay`, one Rules.Gameplay assembly) is `noEngineReferences: true`; every transition function is pure and tested in `dotnet/tests/GameCore.Rules.Gameplay.Tests`.
- Plugin manifest: generated by `com.gamecore.gameplay.compile` from the definitions (slots, routes, stages, state policies `PreserveDormant` for progress-bearing slots), with a real `PackageContentHash` (ManifestValidator is run at admission; SADR-010).
- No static mutable state; per-world modules are attached in the application root; SubsystemRegistration resets.
- Explainability: every refusal and skipped rule writes to the trace with the inputs it read.
- Headless safety: every binder is skipped when `Application.isBatchMode` and `-nographics` (the V1 probes stay valid).

## Amendments (P1.7b gameplay hardening, 2026-10-04)

- **Tool ids.** The tool columns above name the built ids. Where only the naming differed, the built id was kept and
  the column amended: `player.setSpeed` → `player.tuneMovement`, `player.setStart` → `player.setSpawn`,
  `interaction.setCondition` → `interaction.linkCondition`, `dialogue.addNode` → `dialogue.addLine`,
  `dialogue.setCondition` → `dialogue.linkCondition`, `quest.setReward` → `quest.linkReward`, `ui.setBinding` →
  `ui.bind`, `ui.setStyle` → `ui.setTheme`. Built tools the columns did not list are added (`npc.setBehaviour`,
  `interaction.addExaminable/addTrigger/setStates`, `dialogue.setFact/preview`, `quest.simulate`,
  `inventory.setStock`, `logic.test`, `ui.addScreen`, `audio.generateSfx`). `dialogue.graphView` and
  `world.flowView` are struck: they are Studio views (P2.3), not authoring tools. `ui.editText` is still to build.
- **Pure tools.** `dialogue.preview`, `quest.simulate`, `quest.inspectRuntime`, `logic.explain`, `logic.test`,
  `logic.whyNot` and `interaction.explain` are `[AuthorOperation(ReadOnly = true)]`: they change nothing, so
  `ToolRegistry.Invoke` runs them directly and returns their output; every other gameplay tool goes through a change set.
- **Engine binding.** Every gameplay tool binds all its parameters through `ChangeSetEngine`: the target is the first
  `[Authorable]` parameter and every other parameter is an `[AuthorArg]` (references by category). `world.addPortal`
  targets the portal (optional `region` argument; default: the portal's region whose scene is open) and
  `world.setSpawnPoint` targets the region definition (its scene marker is updated when the scene is open).
- **Media tools.** The shared `ToolTier` has no Agent member, so `dialogue.generateVoice` and `audio.generate*` are
  `Compose` tools with the registered media gateway (R2-G supersedes the old `agent.media` prerequisite).
- **References (B1).** Every cross-definition reference is an `[AuthorRef]` whose category is the target's
  `[Authorable]` type id (`dialogue.graph`, `logic.conditionSet`, `logic.actionSet`, `narrative.fact`,
  `quest.quest`, `inventory.item`, `inventory.vendor`, `world.region`, ...). Non-type categories: `entity.instance`
  (a placed entity by authoring id), `audio.clip` (a bank clip id or an AudioClip),
  `audio.musicState` (a state id). The P1.4 pseudo-category `narrative.subject` is gone: conditions, actions, rule
  triggers, objectives and rewards hold one typed field per kind. `authoring.migrateRefs` rewrites older content.
- **Definitions (B2).** Row 1: `RegionDefinition` has bounds, neighbours, named spawn points and an ambience ref;
  `PortalDefinition` has an arrival spawn point per side and a condition-set ref ("the ferryman's favour OR the
  repaired punt" is an Any-mode condition set); `WorldDefinition` has a start spawn point. Row 7: `QuestDefinition`
  has `prerequisites` (`quest.quest`; a failed prerequisite closes its dependents) and completion / failure action
  sets.
- **Structural fields (B4).** `[AuthorField]`/`[AuthorRef]` carry `Structural` (default false); the catalog writes
  `"structural": true` on fields that shape what the runtime builds (prefab, variant set, kind, slot layout, region
  and portal topology), so changes distinguish rebuilds from numeric retuning.
- **Agreed with P3.1.** `player.restoreStamina` (route `player.route.restore-stamina`, schema
  `player.command.restore-stamina` v1, payload one int32 `amount` in stamina units, > 0) and the logic actions
  `ActionKind.RestoreStamina = 17` (value = amount) and `ActionKind.Buy = 18` (key = item, key2 = vendor, value =
  count; `ActionEntry.vendor` / `item` refs). Declared in P1.7b; their handlers and ports are P1.7a's.
- **Save (row 12).** `com.gamecore.gameplay.save` depends on no Studio package: its definition uses the mirror
  attributes of `com.gamecore.gameplay.contracts`, carries an authoring id, and its Studio operations and validator
  live in the Editor assembly `GameCore.Gameplay.Save.Editor`.

## Reference game content built on the catalog (Hollowmere)

| Content | Delivered game |
|---|---|
| Regions | Thornwick Village, Blackmere Marsh, the Drowned Belfry |
| NPCs | Maren (innkeeper/rumour), Odd (ferryman), Pip (shrine order), Hale (gate), Belfry Echo, Bram (lantern) |
| Quest | The Drowned Bell: Rumour, Lantern, Crossing, Belfry; lantern loss failure |
| Endings | A Silence; B The Toll; C The Freed Echo |
| Authoring | Game-owned `story.*`, `world.*`, `dress.*`, `media.*`, `closure.*`, `migrate.refs` phases run as Studio-journaled change sets |

([P3.1 §1](packets/P3.1-hollowmere-complete.md#1-what-the-game-is))

## Final tool flags and capabilities

The requested [model catalog sample](../../dotnet/tests/GameCore.Studio.Model.Tests/Samples/tool-catalog.json) has **four fixture tools**, including `dialogue.addNode` and `npc.place`; it is a serialization example, not the twelve-group production export. The [Saltmarsh export](../../artifacts/studio/verification/W-TOOL-01/20261005T2021267101990Z/tool-catalog.json) has 94 entries. The following table compares that retained export with current attribute declarations. Exported applicability can be more restrictive than an attribute; callers use the actual project's catalog. This discrepancy is recorded as O58 in the [packet](packets/P4.3-final-docs.md#left-open). Schemas are generated and unchanged. ([W-TOOL-01](../../artifacts/studio/verification/W-TOOL-01/README.md), [P1.7b §B3/B4](packets/P1.7b-gameplay-hardening-metadata.md))

`RO` means ReadOnly, `Live` means declared `RuntimeApply.Live`, `RT` means RuntimeOnly; false is explicit here even when omitted in JSON. `Requires` lists project/target prerequisites; definitions also expose capability categories through their metadata. Empty means no attribute prerequisite, not guaranteed provider/world availability. ([metadata](../../Packages/com.gamecore.gameplay.contracts/Runtime/AuthoringMetadata.cs), [R2-G §Implemented](packets/R2-G-gameplay-adapters.md#implemented))

| Tool | Tier | Declared apply / Live | RO / RT | Requires | Retained exported apply | Declaration |
|---|---|---|---|---|---|---|
| `audio.assignClip` | Configure | Live / true | false / false | none | Live | [AudioTools.cs](../../Packages/com.gamecore.gameplay.audio/Editor/AudioTools.cs#L27) |
| `audio.generateSfx` | Compose | Live / true | false / false | none | Live | [AudioTools.cs](../../Packages/com.gamecore.gameplay.audio/Editor/AudioTools.cs#L208) |
| `audio.generateVoice` | Compose | Live / true | false / false | none | Live | [AudioTools.cs](../../Packages/com.gamecore.gameplay.audio/Editor/AudioTools.cs#L180) |
| `audio.setAmbience` | Configure | Rebuild / false | false / false | none | Rebuild | [AudioTools.cs](../../Packages/com.gamecore.gameplay.audio/Editor/AudioTools.cs#L69) |
| `audio.setMusicState` | Configure | Rebuild / false | false / false | none | Rebuild | [AudioTools.cs](../../Packages/com.gamecore.gameplay.audio/Editor/AudioTools.cs#L113) |
| `authoring.migrateRefs` | Compose | Rebuild / false | false / false | none | Rebuild | [LogicTools.cs](../../Packages/com.gamecore.gameplay.logic/Editor/LogicTools.cs#L211) |
| `dialogue.addChoice` | Compose | Rebuild / false | false / false | none | Rebuild | [DialogueTools.cs](../../Packages/com.gamecore.gameplay.dialogue/Editor/DialogueTools.cs#L62) |
| `dialogue.addLine` | Compose | Rebuild / false | false / false | none | Rebuild | [DialogueTools.cs](../../Packages/com.gamecore.gameplay.dialogue/Editor/DialogueTools.cs#L34) |
| `dialogue.generateVoice` | Compose | Rebuild / false | false / false | none | Rebuild | [DialogueTools.cs](../../Packages/com.gamecore.gameplay.dialogue/Editor/DialogueTools.cs#L288) |
| `dialogue.linkCondition` | Compose | Rebuild / false | false / false | none | Rebuild | [DialogueTools.cs](../../Packages/com.gamecore.gameplay.dialogue/Editor/DialogueTools.cs#L110) |
| `dialogue.preview` | Configure | Live / true | true / false | none | Rebuild | [DialogueTools.cs](../../Packages/com.gamecore.gameplay.dialogue/Editor/DialogueTools.cs#L224) |
| `dialogue.setConsequence` | Configure | Rebuild / false | false / false | none | Rebuild | [DialogueTools.cs](../../Packages/com.gamecore.gameplay.dialogue/Editor/DialogueTools.cs#L266) |
| `dialogue.setFact` | Configure | Rebuild / false | false / false | none | Compile | [DialogueTools.cs](../../Packages/com.gamecore.gameplay.dialogue/Editor/DialogueTools.cs#L163) |
| `dialogue.setFactCondition` | Configure | Rebuild / false | false / false | none | Rebuild | [DialogueTools.cs](../../Packages/com.gamecore.gameplay.dialogue/Editor/DialogueTools.cs#L146) |
| `entity.applyOverride` | Configure | Rebuild / false | false / false | none | Rebuild | [EntityTools.cs](../../Packages/com.gamecore.gameplay.entities/Editor/EntityTools.cs#L164) |
| `entity.duplicate` | Compose | Rebuild / false | false / false | none | Rebuild | [EntityTools.cs](../../Packages/com.gamecore.gameplay.entities/Editor/EntityTools.cs#L83) |
| `entity.layoutLine` | Compose | Rebuild / false | false / false | `world.region` | Rebuild | [EntityTools.cs](../../Packages/com.gamecore.gameplay.entities/Editor/EntityTools.cs#L219) |
| `entity.layoutRing` | Compose | Rebuild / false | false / false | `world.region` | Rebuild | [EntityTools.cs](../../Packages/com.gamecore.gameplay.entities/Editor/EntityTools.cs#L187) |
| `entity.place` | Compose | Rebuild / false | false / false | `world.region` | Rebuild | [EntityTools.cs](../../Packages/com.gamecore.gameplay.entities/Editor/EntityTools.cs#L31) |
| `entity.replaceDefinition` | Configure | Rebuild / false | false / false | none | Rebuild | [EntityTools.cs](../../Packages/com.gamecore.gameplay.entities/Editor/EntityTools.cs#L114) |
| `entity.setMaterialTexture` | Configure | Rebuild / false | false / false | none | Rebuild | [EntityTools.cs](../../Packages/com.gamecore.gameplay.entities/Editor/EntityTools.cs#L249) |
| `entity.setVariant` | Configure | Live / true | false / false | none | Rebuild | [EntityTools.cs](../../Packages/com.gamecore.gameplay.entities/Editor/EntityTools.cs#L148) |
| `interaction.addDoor` | Compose | Rebuild / false | false / false | `world.region`, `interaction.interactable` | Rebuild | [InteractionTools.cs](../../Packages/com.gamecore.gameplay.interaction/Editor/InteractionTools.cs#L34) |
| `interaction.addExaminable` | Compose | Rebuild / false | false / false | `world.region`, `interaction.interactable` | Rebuild | [InteractionTools.cs](../../Packages/com.gamecore.gameplay.interaction/Editor/InteractionTools.cs#L46) |
| `interaction.addTrigger` | Compose | Rebuild / false | false / false | `world.region`, `interaction.trigger` | Rebuild | [InteractionTools.cs](../../Packages/com.gamecore.gameplay.interaction/Editor/InteractionTools.cs#L76) |
| `interaction.explain` | Configure | Live / true | true / false | `interaction.interactable` | Rebuild | [InteractionTools.cs](../../Packages/com.gamecore.gameplay.interaction/Editor/InteractionTools.cs#L224) |
| `interaction.linkCondition` | Configure | Rebuild / false | false / false | `interaction.interactable` | Rebuild | [InteractionTools.cs](../../Packages/com.gamecore.gameplay.interaction/Editor/InteractionTools.cs#L142) |
| `interaction.setActions` | Configure | Rebuild / false | false / false | `interaction.interactable` | Rebuild | [InteractionTools.cs](../../Packages/com.gamecore.gameplay.interaction/Editor/InteractionTools.cs#L199) |
| `interaction.setStates` | Configure | Rebuild / false | false / false | `interaction.interactable` | Rebuild | [InteractionTools.cs](../../Packages/com.gamecore.gameplay.interaction/Editor/InteractionTools.cs#L98) |
| `inventory.bindUse` | Configure | Rebuild / false | false / false | none | Rebuild | [InventoryTools.cs](../../Packages/com.gamecore.gameplay.inventory/Editor/InventoryTools.cs#L150) |
| `inventory.grantStarting` | Configure | Rebuild / false | false / false | none | Rebuild | [InventoryTools.cs](../../Packages/com.gamecore.gameplay.inventory/Editor/InventoryTools.cs#L25) |
| `inventory.placeItem` | Compose | Rebuild / false | false / false | none | Compile | [InventoryTools.cs](../../Packages/com.gamecore.gameplay.inventory/Editor/InventoryTools.cs#L48) |
| `inventory.setPrice` | Configure | Rebuild / false | false / false | none | Rebuild | [InventoryTools.cs](../../Packages/com.gamecore.gameplay.inventory/Editor/InventoryTools.cs#L115) |
| `inventory.setStock` | Configure | Rebuild / false | false / false | none | Rebuild | [InventoryTools.cs](../../Packages/com.gamecore.gameplay.inventory/Editor/InventoryTools.cs#L88) |
| `logic.addRule` | Mechanism | Rebuild / false | false / false | none | Compile | [LogicTools.cs](../../Packages/com.gamecore.gameplay.logic/Editor/LogicTools.cs#L33) |
| `logic.explain` | Configure | Live / true | true / false | none | Rebuild | [LogicTools.cs](../../Packages/com.gamecore.gameplay.logic/Editor/LogicTools.cs#L75) |
| `logic.test` | Configure | Live / true | true / false | none | Rebuild | [LogicTools.cs](../../Packages/com.gamecore.gameplay.logic/Editor/LogicTools.cs#L83) |
| `logic.whyNot` | Configure | Live / true | true / false | none | Compile | [LogicTools.cs](../../Packages/com.gamecore.gameplay.logic/Editor/LogicTools.cs#L107) |
| `npc.addAt` | Compose | Rebuild / false | false / false | `world.region`, `npc.definition` | Rebuild | [NpcTools.cs](../../Packages/com.gamecore.gameplay.npc/Editor/NpcTools.cs#L30) |
| `npc.setAppearance` | Configure | Rebuild / false | false / false | `npc.definition` | Rebuild | [NpcTools.cs](../../Packages/com.gamecore.gameplay.npc/Editor/NpcTools.cs#L182) |
| `npc.setBehaviour` | Configure | Rebuild / false | false / false | `npc.definition` | Rebuild | [NpcTools.cs](../../Packages/com.gamecore.gameplay.npc/Editor/NpcTools.cs#L140) |
| `npc.setDialogue` | Configure | Rebuild / false | false / false | `npc.definition` | Rebuild | [NpcTools.cs](../../Packages/com.gamecore.gameplay.npc/Editor/NpcTools.cs#L160) |
| `npc.setPatrol` | Configure | Rebuild / false | false / false | `npc.definition` | Rebuild | [NpcTools.cs](../../Packages/com.gamecore.gameplay.npc/Editor/NpcTools.cs#L68) |
| `npc.setSchedule` | Configure | Rebuild / false | false / false | `npc.definition` | Rebuild | [NpcTools.cs](../../Packages/com.gamecore.gameplay.npc/Editor/NpcTools.cs#L119) |
| `player.setCamera` | Configure | Rebuild / false | false / false | `player.definition` | Rebuild | [PlayerTools.cs](../../Packages/com.gamecore.gameplay.player/Editor/PlayerTools.cs#L80) |
| `player.setSpawn` | Configure | Rebuild / false | false / false | `world.region`, `entity.instance` | Rebuild | [PlayerTools.cs](../../Packages/com.gamecore.gameplay.player/Editor/PlayerTools.cs#L23) |
| `player.tuneMovement` | Configure | Rebuild / false | false / false | `player.definition` | Rebuild | [PlayerTools.cs](../../Packages/com.gamecore.gameplay.player/Editor/PlayerTools.cs#L51) |
| `quest.addObjective` | Compose | Rebuild / false | false / false | none | Rebuild | [QuestTools.cs](../../Packages/com.gamecore.gameplay.quest/Editor/QuestTools.cs#L54) |
| `quest.addStage` | Compose | Rebuild / false | false / false | none | Rebuild | [QuestTools.cs](../../Packages/com.gamecore.gameplay.quest/Editor/QuestTools.cs#L33) |
| `quest.inspectRuntime` | Configure | Live / true | true / false | none | Rebuild | [QuestTools.cs](../../Packages/com.gamecore.gameplay.quest/Editor/QuestTools.cs#L269) |
| `quest.linkReward` | Compose | Rebuild / false | false / false | none | Rebuild | [QuestTools.cs](../../Packages/com.gamecore.gameplay.quest/Editor/QuestTools.cs#L103) |
| `quest.setBranch` | Configure | Rebuild / false | false / false | none | Rebuild | [QuestTools.cs](../../Packages/com.gamecore.gameplay.quest/Editor/QuestTools.cs#L220) |
| `quest.setConsequence` | Configure | Rebuild / false | false / false | none | Rebuild | [QuestTools.cs](../../Packages/com.gamecore.gameplay.quest/Editor/QuestTools.cs#L254) |
| `quest.setPrerequisites` | Configure | Rebuild / false | false / false | none | Rebuild | [QuestTools.cs](../../Packages/com.gamecore.gameplay.quest/Editor/QuestTools.cs#L192) |
| `quest.simulate` | Configure | Live / true | true / false | none | Rebuild | [QuestTools.cs](../../Packages/com.gamecore.gameplay.quest/Editor/QuestTools.cs#L126) |
| `save.inspect` | Configure | Live / true | false / false | none | Rebuild | [SaveStudioOperations.cs](../../Packages/com.gamecore.gameplay.save/Editor/SaveStudioOperations.cs#L21) |
| `save.testRoundTrip` | Configure | Live / true | false / false | none | Rebuild | [SaveStudioOperations.cs](../../Packages/com.gamecore.gameplay.save/Editor/SaveStudioOperations.cs#L38) |
| `ui.addScreen` | Compose | Live / true | false / false | none | Rebuild | [UiTools.cs](../../Packages/com.gamecore.gameplay.ui/Editor/UiTools.cs#L62) |
| `ui.bind` | Configure | Live / true | false / false | none | Live | [UiTools.cs](../../Packages/com.gamecore.gameplay.ui/Editor/UiTools.cs#L27) |
| `ui.previewScreen` | Configure | Live / true | false / false | none | Rebuild | [UiTools.cs](../../Packages/com.gamecore.gameplay.ui/Editor/UiTools.cs#L149) |
| `ui.setCommand` | Configure | Live / true | false / false | none | Live | [UiTools.cs](../../Packages/com.gamecore.gameplay.ui/Editor/UiTools.cs#L45) |
| `ui.setTheme` | Configure | Live / true | false / false | none | Rebuild | [UiTools.cs](../../Packages/com.gamecore.gameplay.ui/Editor/UiTools.cs#L118) |
| `world.addPortal` | Compose | Rebuild / false | false / false | `world.region`, `world.portal` | Rebuild | [WorldTools.cs](../../Packages/com.gamecore.gameplay.world/Editor/WorldTools.cs#L117) |
| `world.configurePortal` | Configure | Rebuild / false | false / false | `world.portal` | Rebuild | [WorldTools.cs](../../Packages/com.gamecore.gameplay.world/Editor/WorldTools.cs#L351) |
| `world.connectRegions` | Compose | Rebuild / false | false / false | `world.definition` | Rebuild | [WorldTools.cs](../../Packages/com.gamecore.gameplay.world/Editor/WorldTools.cs#L33) |
| `world.setRegionBounds` | Configure | Rebuild / false | false / false | `world.region` | Rebuild | [WorldTools.cs](../../Packages/com.gamecore.gameplay.world/Editor/WorldTools.cs#L386) |
| `world.setSpawnPoint` | Compose | Rebuild / false | false / false | `world.region` | Rebuild | [WorldTools.cs](../../Packages/com.gamecore.gameplay.world/Editor/WorldTools.cs#L172) |

Runtime-only declarations `world.travel`, `dialogue.start`, `entity.spawn`, `entity.despawn`, `npc.goTo` are registered by the Editor gameplay adapter for the active world, with `RuntimeOnly=true`, `RuntimeApply.Live` and revision validation. They are separate from definition authoring tools and are not present in the retained Edit-mode Saltmarsh export. ([R2-G §Implemented](packets/R2-G-gameplay-adapters.md#implemented), [WorldLiveOpTranslator](../../Packages/com.gamecore.studio.gameplay/Editor/WorldLiveOpTranslator.cs))

Media tools are Compose; there is no Agent tier. The old `agent.media` authored-index prerequisite was removed; an actual `IMediaGenerationGatewayProvider` registration determines availability. No SFX provider is configured. Internal `mechanism.admit`/`mechanism.remove` never belong to the worker catalog. ([R2-G §Implemented](packets/R2-G-gameplay-adapters.md#implemented), [R2-B](packets/R2-B-admission.md))

## Plugin developer notes from Saltmarsh

| Finding | Reuse obligation / remaining seam | Source |
|---|---|---|
| CP-01 codecs | Saltmarsh owns thirteen production serializers; shared production codec factory remains an owner request. Do not ship the test-only Hollowmere binding as a package API. | [P4.1 §Reusability findings](packets/P4.1-clean-proof.md#reusability-findings) |
| CP-02 scaffolding | Game-owned `saltmarsh.author` phases construct scenes/boot through the engine; generic `scene.create` remains future work. | Same section |
| CP-03 generated imports | Author/bake, then test/build in a separate Editor invocation; preserve generated catalogs in `link.xml`. | Same section |
| CP-04 metadata arrays | Compiler-generated static readonly arrays are not deeply immutable; only compiler owner may change generated format/fingerprint semantics. | Same section |
| CP-05 display | Linux player needs a reachable display/X11 backend even under `-nographics`; use the documented run wrapper or private xvfb. | Same section; [10](10-install-build-run.md) |

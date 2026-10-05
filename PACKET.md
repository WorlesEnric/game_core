# P1.4 dialogue-quest-logic-inventory

Catalog rows 6 (dialogue & narrative), 7 (quests), 8 (items & economy) and 9 (reusable rules/logic) of the gameplay
plugin library, their pure rules halves, a bake extension point in the gameplay compile pipeline, and the Hollowmere
quest "The Drowned Bell".

Branch: `worktree-agent-a833890c84121beac`. Host clone: `~/wkspace/gc-studio/p1.4`.

## What was built

| Package / path | Content |
|---|---|
| `Packages/com.gamecore.gameplay.contracts/Runtime/Narrative/` (additive, engine-free) | `NarrativeIds` (route/command/event/slot/owner ids of the four plugins, `narrative.fact.<name>` slots, request-id derivation `NarrativeKeys.RequestIdOf(outboxId)`), `NarrativeCatalogNames`, `NarrativeDefinitions` (`INarrativeDefinition`, `IFactDefinition`, `NarrativeKinds`), `NarrativeSeams` (`EvaluationContext`, `IConversationStarter`, `IConditionEvaluator`, `IActionRunner`, feedback/message sinks, `IVoiceLinePlayer`, `IExplainSource`+`ExplainRecord`, `IMediaGenerationGateway`+`NotConfiguredMediaGateway`, a null object for each), `NarrativeViewModels` (`DialogueViewModel`, `JournalViewModel`, `InventoryViewModel` and `IDialogueView`/`IJournalView`/`IInventoryView` with null views). |
| `Packages/com.gamecore.rules.gameplay/Runtime/{Dialogue,Quest,Inventory,Logic}` (pure, noEngineReferences) | `DialogueRules` (graph validation, node stepping, option availability, visited bits), `QuestRules` (stage/objective/branch progression, fail conditions, reward selection per branch), `InventoryRules` (stacking, weight, slots, grant/consume/drop/transfer/trade with typed refusals, request-id ring of 8), `ConditionRules`/`ActionRules`/`RuleRules` (condition sets over facts/items/currency/quests/visited/time, the action kinds, trigger matching, once/cooldown/counter, `BoundedRing<T>`). |
| `dotnet/tests/GameCore.Rules.Gameplay.Tests/{Dialogue,Quest,Inventory,Logic}` | 131 NUnit tests (>= 12 per area). |
| `Packages/com.gamecore.gameplay.logic` | Authoring: `NarrativeDefinitionAsset` base, `GameplayContentSet` (the authored list), `GameplayContentManifest` (baked), `ConditionSetDefinition`, `ActionSetDefinition`, `RuleDefinition`. Runtime: `NarrativeComposer`/`NarrativeComposition`/`NarrativeRuntime`/`NarrativeWorld` (composition onto the P1.1 world plan), `NarrativeContent` (models + index), `NarrativeHost` (event routing, one delivery pump per frame), `NarrativeDelivery` (outbox ports), `NarrativeConditionEvaluator`, `NarrativeActionRunner`, `ExplainTrace` (ring of 256), `LogicModule` + `[DisableAutoCreation] LogicCommandSystem` (stage `logic`, after world/inventory/quest/dialogue). Editor: `NarrativeBake` + `LogicBakeExtension`, `NarrativeAuthoring` helpers, `logic.addRule/explain/test`, `LogicValidator`. |
| `Packages/com.gamecore.gameplay.dialogue` | `FactDefinition`, `DialogueGraphDefinition` (line/choice/branch/action/end nodes, edges by port), `DialogueContentConverter`, `DialogueModule` + `DialogueCommandSystem`, `DialogueRunner` (`IConversationStarter`), `DialoguePresenter`, `VoiceLinePlayer`. Editor: `dialogue.addLine/addChoice/linkCondition/setFact/preview/generateVoice`, `DialogueValidator`, `DialogueBakeExtension`. |
| `Packages/com.gamecore.gameplay.quest` | `QuestDefinition`, `ObjectiveDefinition`, `QuestContentConverter`, `QuestModule` + `QuestTracker` + `QuestCommandSystem`, `JournalPresenter`, `ObjectiveMarker`. Editor: `quest.addStage/addObjective/linkReward/simulate`, `QuestValidator`, `QuestBakeExtension`. |
| `Packages/com.gamecore.gameplay.inventory` | `ItemDefinition`, `InventoryDefinition`, `VendorDefinition`, `LootTableDefinition`, `WorldItemDefinition`, `InventoryContentConverter`, `InventoryModule` + `InventoryCommandSystem`, `InventoryCommands`, `InventoryPresenter`, `WorldItemBinder`, `VendorBinder`. Editor: `inventory.grantStarting/placeItem/setStock`, `InventoryValidator`, `InventoryBakeExtension`. |
| `Packages/com.gamecore.gameplay.compile/Editor/Extensions/GameplayBakeExtensions.cs` (new) | `IGameplayBakeExtension` (Contribute/Write/Verify), `GameplayBakeContext`, discovery through `TypeCache` ordered by `ExtensionId`. |
| `games/hollowmere` | Manifest adds the four packages (lock re-resolved on the host). `Assets/Hollowmere/Rules/Runtime` (`Hollowmere.Narrative`: module list and `Boot`), `Rules/Editor/HollowmereNarrativeAuthoring` (authors the content through the tools and bakes), the authored content under `Dialogue/`, `Quests/`, `Items/`, `Rules/`, tests under `Tests/P1_4`. |

### Out-of-path edits (allowed minimal call sites)

* `Packages/com.gamecore.gameplay.compile/Core/CatalogDescriptionWriter.cs`: `Write(world, naming)` delegates to a new
  overload `Write(world, naming, extraSchemas, extraEntries)`; with no extras the output is byte-identical (P1.1 bake
  outputs unchanged when no extension contributes).
* `Packages/com.gamecore.gameplay.compile/Editor/Entry.cs`: `Compute` calls `GameplayBakeExtensions.Contribute` and
  passes the contributed schemas/entries to the writer; `BakeCore` calls `GameplayBakeExtensions.Write` after the
  manifest; `Verify` calls `GameplayBakeExtensions.Verify`; header comment step 6.
* `dotnet/src/GameCore.Rules.Gameplay` csproj Compile line (done before this packet started).

## API (for P1.5, P3.1, P3.2)

### Booting a world with narrative

```csharp
var modules = new HollowmereNarrativeModules();          // Logic, Inventory, Quest, Dialogue (game-owned list)
NarrativeWorld game = HollowmereNarrative.Boot(regionManifest, contentManifest, modules, options, start: true);
// generic form: NarrativeComposer.Boot(RegionManifest, GameplayContentManifest, IReadOnlyList<INarrativeModule>,
//                                      GameApplicationBootOptions?, WorldBuildOptions?, bool start)
game.Shutdown();                                          // disposes delivery, detaches the world, stops the root
```

`NarrativeWorld` exposes `World` (the P1.1 `GameplayWorld`), `Runtime`, `Root`, `Host`, `Conditions`, `Actions`,
`Conversations`, `Explain`, `Delivery`. The content manifest must match the world (`FormatId`, `WorldId`), otherwise
boot fails with ContentStale. P1.1's `GameBoot` still boots without narrative; switching the boot scene is left to
the owner of the boot scene (P1.5/P3.1).

### Seams (namespace `GameCore.Gameplay.Contracts.Narrative`)

```csharp
bool IConversationStarter.TryStart(string npcAuthoringId, string dialogueGraphRef);          // DialogueRunner
bool IConditionEvaluator.Evaluate(string conditionSetRef, in EvaluationContext ctx, out string failedCondition);
bool IActionRunner.TryRun(string actionSetRef, in EvaluationContext ctx);                   // "inventory.pickup" = pick up ctx's world item
void IVoiceLinePlayer.Play(VoiceLineRequest request);
void INarrativeFeedbackSink.OnFeedback(NarrativeFeedbackCue cue);   // playAudio actions -> runtime.UseFeedback(sink)
void INarrativeMessageSink.Show(NarrativeMessage message);          // showMessage actions -> runtime.UseMessages(sink)
void IDialogueView.Show(DialogueViewModel m); void IJournalView.Show(JournalViewModel m); void IInventoryView.Show(InventoryViewModel m);
IExplainSource: Count, Recent(max), TryExplain(ruleRef, out ExplainRecord?)                // game.Explain
```

Refs are authoring ids or definition names. `EvaluationContext.ForSubject(authoringId)` builds a context about a
subject with the player as actor; condition refs also accept the shorthand `narrative.fact.<name>[op N]`.
Views are assigned on the modules before boot (`modules.Dialogue.View`, `modules.Quest.View`,
`modules.Inventory.View`, `modules.Dialogue.VoicePlayer`); presenters push only when the committed state changed.

### Commands and slots

* Dialogue: `dialogue.start{graph, speaker, listener}`, `choose{index}`, `advance`, `interrupt`, `narrative.setFact{fact, value, request}`
  via `DialogueRunner` (`TryStart`, `Advance`, `Choose`, `Interrupt`, `SetFact`). Slots `dialogue.active/node/speaker/choiceCount/serial`,
  visited bit words per graph, facts `narrative.fact.<name>`. Events LineShown, ChoiceOffered, ChoiceMade, DialogueEnded, FactSet.
* Quest: `quest.start`, `advance{stage, branch}`, `setObjective{n, count}`, `fail`, `complete`; slots `quest.status/stage/branch`,
  `quest.obj.<n>.count/done`; events QuestStarted, StageEntered, ObjectiveUpdated, QuestCompleted, QuestFailed, RewardGranted.
  Objectives advance automatically from committed events (`QuestTracker`): Fact, Collect, Reach, Talk, Interact.
* Inventory: `inv.grant{item, count, requestId}` (idempotent), `consume`, `drop`, `transfer`, `buy/sell{vendor}`,
  `inventory.pickup{worldItem}` via `InventoryCommands`; slots `inv.item.<k>/inv.count.<k>/inv.currency`, `item.taken`,
  vendor stock; events ItemGranted, ItemConsumed, ItemDropped, TradeDone, InventoryFull, ItemPickedUp.
* Logic: `logic.evaluate` and `logic.runActions` (host-submitted); slots `logic.fired/cooldownMs/counter/invocations`;
  events RuleFired, RuleSkipped, ActionsRun, ActionDue.

Every cross-plugin effect (rewards, rule actions, dialogue actions, use effects) is an outbox obligation delivered by
`NarrativeDelivery` with `requestId = RequestIdOf(outboxId)`; a replayed obligation answers AlreadyApplied.

## Decisions (where 05 was silent)

* **Content is a list, not a scene.** Narrative definitions are ScriptableObjects listed on one `GameplayContentSet`
  per world (`world` ref). The bake (logic's `IGameplayBakeExtension`) writes `<set>.content.asset`
  (`GameplayContentManifest`, format `gamecore.gameplay-content/1`: entries sorted by authoring id with kind, key,
  name and content stamp; facts sorted by name; SHA-256 content hash) and stamps every definition. The dialogue, quest
  and inventory extensions add their plugin's catalog registrations only when the world has content, so a world
  without narrative bakes byte-identically to P1.1.
* **One file per ScriptableObject class.** Unity resolves an asset's script by file name; the definition classes each
  live in `<Class>.cs` (supporting entry types stay in `*Definitions.cs`).
* **Keys.** Every definition key is `AuthoringIds.StableKey(authoringId)`; refs in tools and seams accept the
  authoring id or the definition name. Ids use the package stems (`inventory.command.grant`, `quest.event.completed`,
  ...) as P1.1 does; 05's short names (`inv.grant`) appear as route names in the catalog.
* **Facts** are int32 slots `narrative.fact.<name>` on one narrative hub target; flags are 0/1. A fact is persistent
  by default; a non-persistent fact (Hollowmere's `pip_asked`) is reset when the conversation that set it ends.
  Facts are written by `narrative.setFact` (dialogue plugin) only, with a request ring so a replay changes nothing.
* **Stage order** world -> inventory -> quest -> dialogue -> logic (`optionalAfter`), so a rule sees the step's
  committed gameplay effects.
* **Every cross-plugin effect is an outbox obligation.** Dialogue action nodes, rule actions, item use effects and
  quest rewards commit an `ActionDue`/`RewardGranted` event; `NarrativeDelivery` (a `WorldDeliveryOwner`, capacity
  256, 64 per pass, one pump per frame from the narrative host) delivers it through one port per target command with
  `requestId = RequestIdOf(outboxId)`. Targets keep a ring of the last 8 request ids: a duplicate is
  `IdempotencyConflict` -> the port answers AlreadyApplied. `InventoryFull` commits without consuming the id, so a
  later retry can still apply. playAudio/showMessage go to presentation sinks once per outbox id.
* **Quest branches.** Objectives carry a branch (0 = every branch). A stage passes when all its branch-0 objectives
  and all objectives of one branch are done; the first branch completed is recorded in `quest.branch` and selects the
  branch rewards. Objectives advance from committed events and committed levels (`QuestTracker`): Fact, Collect
  (item count), Reach (RegionEntered), Talk (conversation with the graph), Interact (interaction succeeded).
* **setInteractableState** sets the target entity's variant (`entity.setVariant`); spawn/despawn/travel use the P1.1
  entity and world commands.
* **Rules** trigger on committed events (RegionEntered, interaction succeeded, FactSet, item and trade events, quest
  events, dialogue ended / choice made, rule fired, actions run) with an optional subject and value filter, then
  conditions, once/cooldown/max-fires limits; the decision and every condition read go to the 256-entry explain ring.
* **`dialogue.generateVoice`** is declared `ToolTier.Mechanism` (the mirror enum has no Agent tier) and calls
  `IMediaGenerationGateway`, which is `NotConfiguredMediaGateway` until P3.x wires etos TTS; it returns
  `NotConfigured` and changes nothing.
* **Interaction seams.** P1.3 declares its own `IConditionEvaluator`/`IActionRunner` in `GameCore.Gameplay.Contracts`
  with different signatures; the P1.4 seams keep the brief's signatures in `GameCore.Gameplay.Contracts.Narrative`.
  `NarrativeInteractions.Use(world, subjectId, conditionRef, actionRef, out failed)` evaluates and runs the pair
  the way an interaction does; the PlayMode test uses it for the gate and the bell.
* **Hollowmere has no gate or NPC entities yet.** NPCs speak by name; the marsh gate is the fixed subject id
  `HollowmereNarrative.GateId`. Rewards: the lantern and `maren_grateful` on both branches, the three coins back on
  the pay branch. Odd's stall sells the gate key for three old coins; `odd_paid` is set by the `OddPaidOnTrade` rule
  on TradeDone (subject the stall, value the gate key); persuading Odd (needs `maren_trusts_player`, hidden otherwise)
  grants the key through the outbox.
* **No save format change.** Narrative state is ordinary slots of ordinary targets (facts, quest, inventory, rule
  slots), which P1.2's save already covers. The narrative outbox is its own `WorldDeliveryOwner`
  (`game.Delivery.Owner.ToRecords()` / `game.Delivery.Reinstate(rows)`); the PlayMode test replays it, but adding it
  to P1.2's checkpoint sections is listed under Open.

## Verification

VERIFICATION_PLACEHOLDER

## Open

* **P1.3 adapter.** Bridge P1.3's `GameCore.Gameplay.Contracts.IConditionEvaluator`/`IActionRunner`
  (`ConditionVerdict Evaluate(string, InteractionContext)`, `void Run(string, InteractionContext)`) to
  `NarrativeWorld.Conditions`/`Actions` when the two packets merge (a two-class adapter; `InteractionContext` ->
  `EvaluationContext` with the actor key and subject id). Until then `NarrativeInteractions.Use` stands in for
  `interact.use`, and logic listens to P1.3's `interaction.event.succeeded` by schema name.
* **Boot scene.** `GameBoot` (P1.1) still boots without narrative; switching it to `HollowmereNarrative.Boot` and
  assigning the UI views/feedback sinks belongs to the boot-scene owner (P1.5/P3.1).
* **Gate and NPC entities.** When P1.3 places Maren, Odd, Pip, Hale and a gate entity, set `speakerEntityId` on the
  graphs and replace `GateId` with the gate entity's authoring id (one asset field each).
* **ToolTier Agent.** If Studio adds an Agent tier to the mirror enums, move `dialogue.generateVoice` to it.
* **ToolCatalogBuilder** (P1.6) must match the mirror attributes by name to list the 16 narrative tools (same
  question as P1.1).
* **Narrative outbox in the checkpoint.** Register `NarrativeDelivery.Owner` with P1.2's save composer so pending
  rewards survive a save/load (records and reinstate exist and are tested; the registration is P1.2's call site).
* **Loot tables** are authored and converted but no runtime command rolls them yet (no catalog row needs it in P1).

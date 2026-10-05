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

DECISIONS_PLACEHOLDER

## Verification

VERIFICATION_PLACEHOLDER

## Open

OPEN_PLACEHOLDER

#nullable enable
using System;
using System.IO;
using System.Linq;
using GameCore.Gameplay.Dialogue;
using GameCore.Gameplay.Inventory;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Quest;
using GameCore.Rules.Gameplay.Dialogue;
using GameCore.Rules.Gameplay.Logic;
using GameCore.Rules.Gameplay.Quest;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hollowmere.R7_B.Model
{
    public sealed class ItemDeletionImpactTests
    {
        [Test]
        public void W_MODEL_02_DeleteItemListsReferencingDialogueLineAndObjective()
        {
            string folder = "Assets/R7BImpact_" + Guid.NewGuid().ToString("N");
            string state = Path.Combine(Path.GetTempPath(), "r7b-impact-" + Guid.NewGuid().ToString("N"));
            StudioRuntime? runtime = null;
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            try
            {
                ItemDefinition item = ScriptableObject.CreateInstance<ItemDefinition>();
                item.Configure("R7 oil flask", 10, 100, 2, null, null);
                AssetDatabase.CreateAsset(item, folder + "/Oil.asset");
                ConditionSetDefinition condition = ScriptableObject.CreateInstance<ConditionSetDefinition>();
                condition.Add(new ConditionEntry { kind = ConditionKind.ItemCount, item = item, value = 1 });
                AssetDatabase.CreateAsset(condition, folder + "/HasOil.asset");
                DialogueGraphDefinition dialogue = ScriptableObject.CreateInstance<DialogueGraphDefinition>();
                dialogue.Configure("Maren", string.Empty, 0);
                DialogueNodeEntry choice = new DialogueNodeEntry { kind = DialogueNodeKind.Choice, text = "Do you have oil?" };
                choice.options.Add(new DialogueOptionEntry { text = "Here is the oil flask.", condition = condition, hideWhenUnavailable = true });
                dialogue.AddNode(choice);
                AssetDatabase.CreateAsset(dialogue, folder + "/Maren.asset");
                QuestDefinition quest = ScriptableObject.CreateInstance<QuestDefinition>();
                quest.Configure("Light the lantern", null, null);
                quest.AddStage(new QuestStageEntry { title = "Find oil" });
                quest.AddObjective(new ObjectiveDefinition { kind = ObjectiveKind.Collect, item = item, required = 1, text = "Collect the oil flask" });
                AssetDatabase.CreateAsset(quest, folder + "/Lantern.asset");
                AssetDatabase.SaveAssets();
                runtime = StudioRuntime.Create(new StudioRuntimeOptions
                {
                    Paths = new StudioPaths(Directory.GetParent(Application.dataPath)!.FullName, state, "r7b-impact"),
                    Log = new MemoryStudioLog(),
                    SearchFolders = new[] { folder },
                    IndexScope = AuthoringSourceScope.All,
                    LoadIndexCache = false,
                });
                runtime.Index.Rebuild();
                AuthoringRef target = runtime.Resolver.BuildRef(item, AuthorScope.Definition)!;
                AuthoringRef dialogueRef = runtime.Resolver.BuildRef(dialogue, AuthorScope.Definition)!;
                AuthoringRef questRef = runtime.Resolver.BuildRef(quest, AuthorScope.Definition)!;
                ImpactReport impact = runtime.Index.ImpactOf(target);
                Assert.That(impact.Items.Any(x => x.Ref.SameTarget(dialogueRef) && x.Field == "nodes[0].options[0].condition" && x.Depth == 2), Is.True,
                    "The dialogue line must be identified through its item condition, not merely the containing graph name.");
                Assert.That(impact.Items.Any(x => x.Ref.SameTarget(questRef) && x.Field == "objectives[0].item" && x.Depth == 1), Is.True,
                    "The collect objective must be identified by its indexed field path.");
                ChangeSet deletion = StudioRuntime.Single("Delete oil after reviewing impact", IntentOrigin.Manual,
                    new Operation("delete", "delete", target));
                StagedChangeSet staged = runtime.Engine.Stage(deletion, new StageOptions { Previews = false });
                Assert.That(staged.Ok, Is.True, StudioJson.Serialize(staged.AllDiagnostics));
                JArray preview = (JArray)staged.Operations[0].Preview!["impact"]!;
                Assert.That(preview.Any(x => (string?)x["field"] == "nodes[0].options[0].condition"), Is.True);
                Assert.That(preview.Any(x => (string?)x["field"] == "objectives[0].item"), Is.True);
                ApplyReport applied = runtime.Engine.Apply(staged);
                Assert.That(applied.Ok, Is.True, StudioJson.Serialize(applied.Diagnostics));
                Assert.That(AssetDatabase.LoadAssetAtPath<ItemDefinition>(folder + "/Oil.asset"), Is.Null);
                Assert.That(runtime.History.Undo(applied.Entry.Id).Ok, Is.True);
                Assert.That(AssetDatabase.LoadAssetAtPath<ItemDefinition>(folder + "/Oil.asset"), Is.Not.Null);
                string? evidence = Environment.GetEnvironmentVariable("GAMECORE_R7B_MODEL_OUT");
                if (!string.IsNullOrEmpty(evidence))
                {
                    Directory.CreateDirectory(evidence);
                    File.WriteAllText(Path.Combine(evidence, "impact.json"), new JObject
                    {
                        ["row"] = "W-MODEL-02", ["passed"] = true,
                        ["dialogueLine"] = dialogue.Nodes[0].options[0].text, ["objective"] = quest.Objectives[0].text,
                        ["preview"] = preview.DeepClone(), ["deletion"] = StudioJson.ToToken(applied.Entry),
                        ["undoRestoredItem"] = true,
                    }.ToString());
                }
            }
            finally
            {
                runtime?.Dispose();
                AssetDatabase.DeleteAsset(folder);
                if (Directory.Exists(state)) Directory.Delete(state, true);
            }
        }
    }
}

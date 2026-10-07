#nullable enable
using System;
using System.Collections;
using System.Reflection;
using System.IO;
using GameCore.Studio.Fixtures;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.Edit.Tests
{
    public sealed class DefinitionProjectionTests
    {
        private StudioTestBed _bed = null!;

        [SetUp]
        public void SetUp() => _bed = new StudioTestBed();

        [TearDown]
        public void TearDown() => _bed.Dispose();

        [Test]
        public void R9A_CreatedReferenceAndDependentSetAssignApplyInDependencyOrderAndUndo()
        {
            FixtureItemDefinition previous = _bed.CreateItem("Previous", 2f);
            FixtureNpcDefinition npc = _bed.CreateNpc("Carrier", item: previous);
            _bed.Runtime.Index.Rebuild();
            string path = _bed.Folder + "/NewItem.asset";
            var created = new AuthoringRef(AuthoringKind.Definition, path: path, scope: AuthorScope.Definition);
            ChangeSet change = StudioTestBed.NewChangeSet("Create and equip an edited item", null,
                StudioTestBed.Op("equip", "assign", _bed.Ref(npc),
                    new JObject { ["field"] = "startingItem", ["value"] = path }, "edit"),
                new Operation("edit", "set", created,
                    new JObject { ["field"] = "weight", ["value"] = 7 }, new[] { "create" }, Preconditions.None),
                StudioTestBed.Op("create", "create", null, new JObject
                {
                    ["type"] = "fixture.item", ["name"] = "NewItem", ["path"] = path,
                    ["fields"] = new JObject { ["displayName"] = "Lantern", ["weight"] = 1 },
                }));

            StagedChangeSet staged = _bed.Runtime.Engine.Stage(change);

            Assert.That(staged.Ok, Is.True, string.Join("; ", staged.AllDiagnostics));
            Assert.That(npc.startingItem, Is.SameAs(previous), "Stage must not assign the detached proposed object to a live asset.");
            Assert.That(File.Exists(path), Is.False, "Stage must not materialize the newly composed asset.");
            ApplyReport applied = _bed.Runtime.Engine.Apply(staged);
            Assert.That(applied.State, Is.EqualTo(ChangeSetState.Applied), string.Join("; ", applied.Diagnostics));
            FixtureItemDefinition item = AssetDatabase.LoadAssetAtPath<FixtureItemDefinition>(path);
            Assert.That(item, Is.Not.Null);
            Assert.That(item.weight, Is.EqualTo(7));
            Assert.That(npc.startingItem, Is.SameAs(item));

            HistoryResult undone = _bed.Runtime.History.Undo(change.Id);

            Assert.That(undone.Ok, Is.True, string.Join("; ", undone.Diagnostics));
            Assert.That(npc.startingItem, Is.SameAs(previous));
            Assert.That(AssetDatabase.LoadAssetAtPath<FixtureItemDefinition>(path), Is.Null);
        }

        [Test]
        public void R9A_CreatedDefinitionFieldsResolveAnEarlierCreatedReferenceWithoutLiveWrites()
        {
            string itemPath = _bed.Folder + "/Gift.asset";
            string npcPath = _bed.Folder + "/Recipient.asset";
            ChangeSet change = StudioTestBed.NewChangeSet("Create a recipient with its gift", null,
                StudioTestBed.Op("npc", "create", null, new JObject
                {
                    ["type"] = "fixture.npc", ["name"] = "Recipient", ["path"] = npcPath,
                    ["fields"] = new JObject { ["greeting"] = "Thank you", ["startingItem"] = itemPath },
                }, "item"),
                StudioTestBed.Op("item", "create", null, new JObject
                {
                    ["type"] = "fixture.item", ["name"] = "Gift", ["path"] = itemPath,
                    ["fields"] = new JObject { ["displayName"] = "Gift", ["weight"] = 3 },
                }));

            StagedChangeSet staged = _bed.Runtime.Engine.Stage(change);

            Assert.That(staged.Ok, Is.True, string.Join("; ", staged.AllDiagnostics));
            Assert.That(File.Exists(itemPath), Is.False);
            Assert.That(File.Exists(npcPath), Is.False);
            ApplyReport applied = _bed.Runtime.Engine.Apply(staged);
            Assert.That(applied.State, Is.EqualTo(ChangeSetState.Applied), string.Join("; ", applied.Diagnostics));
            FixtureItemDefinition item = AssetDatabase.LoadAssetAtPath<FixtureItemDefinition>(itemPath);
            FixtureNpcDefinition npc = AssetDatabase.LoadAssetAtPath<FixtureNpcDefinition>(npcPath);
            Assert.That(npc.startingItem, Is.SameAs(item));
            Assert.That(npc.greeting, Is.EqualTo("Thank you"));
            Assert.That(item.weight, Is.EqualTo(3));

            HistoryResult undone = _bed.Runtime.History.Undo(change.Id);

            Assert.That(undone.Ok, Is.True, string.Join("; ", undone.Diagnostics));
            Assert.That(AssetDatabase.LoadAssetAtPath<FixtureNpcDefinition>(npcPath), Is.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<FixtureItemDefinition>(itemPath), Is.Null);
        }

        [Test]
        public void R9A_DeferredCreatedTargetStillRefusesOutOfRangeFieldsBeforeWrites()
        {
            string path = _bed.Folder + "/TooHeavy.asset";
            var target = new AuthoringRef(AuthoringKind.Definition, path: path, scope: AuthorScope.Definition);
            ChangeSet change = StudioTestBed.NewChangeSet("Reject an invalid dependent edit", null,
                new Operation("edit", "set", target,
                    new JObject { ["field"] = "weight", ["value"] = 101 }, new[] { "create" }, Preconditions.None),
                StudioTestBed.Op("create", "create", null, new JObject
                {
                    ["type"] = "fixture.item", ["name"] = "TooHeavy", ["path"] = path,
                    ["fields"] = new JObject { ["weight"] = 1 },
                }));

            StagedChangeSet staged = _bed.Runtime.Engine.Stage(change);

            Assert.That(staged.Ok, Is.False, "Deferred targets must retain the field's maximum of 100 kg.");
            Assert.That(staged.AllDiagnostics, Has.Some.Matches<Diagnostic>(diagnostic =>
                diagnostic.Code == DiagnosticCodes.InvalidArgs && diagnostic.Where?.OpId == "edit"));
            ApplyReport refused = _bed.Runtime.Engine.Apply(staged);
            Assert.That(refused.State, Is.EqualTo(ChangeSetState.Rejected));
            Assert.That(File.Exists(path), Is.False);
            Assert.That(AssetDatabase.LoadAssetAtPath<FixtureItemDefinition>(path), Is.Null);
        }

        [Test]
        public void R9A_FinalValidatorTraversesRepairedFactThroughUnchangedCondition()
        {
            Type? factType = Type.GetType("GameCore.Gameplay.Dialogue.FactDefinition, GameCore.Gameplay.Dialogue");
            Type? conditionType = Type.GetType("GameCore.Gameplay.Logic.ConditionSetDefinition, GameCore.Gameplay.Logic");
            Assert.That(factType, Is.Not.Null);
            Assert.That(conditionType, Is.Not.Null);
            ScriptableObject fact = CreateAsset(factType!, "Fact");
            ScriptableObject condition = CreateAsset(conditionType!, "Condition");
            using (var serialized = new SerializedObject(fact))
            {
                serialized.FindProperty("factName").stringValue = "INVALID NAME";
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            using (var serialized = new SerializedObject(condition))
            {
                SerializedProperty entries = serialized.FindProperty("conditions");
                entries.arraySize = 1;
                SerializedProperty entry = entries.GetArrayElementAtIndex(0);
                SerializedProperty kind = entry.FindPropertyRelative("kind");
                kind.enumValueIndex = Array.IndexOf(kind.enumNames, "Fact");
                entry.FindPropertyRelative("fact").objectReferenceValue = fact;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            using StudioRuntime runtime = DialogueRuntime(out ScriptableObject graph, factType!, conditionType!);
            using (var serialized = new SerializedObject(graph))
            {
                SerializedProperty node = serialized.FindProperty("nodes").GetArrayElementAtIndex(0);
                SerializedProperty kind = node.FindPropertyRelative("kind");
                kind.enumValueIndex = Array.IndexOf(kind.enumNames, "Branch");
                node.FindPropertyRelative("condition").objectReferenceValue = condition;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
            runtime.Index.Rebuild();
            string graphBefore = EditorJsonUtility.ToJson(graph);
            string conditionBefore = EditorJsonUtility.ToJson(condition);
            string factBefore = EditorJsonUtility.ToJson(fact);
            byte[] conditionBytes = File.ReadAllBytes(AssetDatabase.GetAssetPath(condition));
            ChangeSet change = StudioTestBed.NewChangeSet("Repair a transitive graph dependency", null,
                StudioTestBed.Set("repair", runtime.Resolver.BuildRef(fact)!, "factName", "shrine_lit"),
                StudioTestBed.Set("speaker", runtime.Resolver.BuildRef(graph)!, "speaker", "Odd"));

            StagedChangeSet staged = runtime.Engine.Stage(change);

            Assert.That(staged.Ok, Is.True, string.Join("; ", staged.AllDiagnostics));
            Assert.That(EditorJsonUtility.ToJson(graph), Is.EqualTo(graphBefore));
            Assert.That(EditorJsonUtility.ToJson(condition), Is.EqualTo(conditionBefore));
            Assert.That(EditorJsonUtility.ToJson(fact), Is.EqualTo(factBefore));
            Assert.That(File.ReadAllBytes(AssetDatabase.GetAssetPath(condition)), Is.EqualTo(conditionBytes));
            ApplyReport applied = runtime.Engine.Apply(staged);
            Assert.That(applied.State, Is.EqualTo(ChangeSetState.Applied), string.Join("; ", applied.Diagnostics));
            Assert.That(DialogueDiagnostics(graph), Is.Empty);
            Assert.That(EditorJsonUtility.ToJson(condition), Is.EqualTo(conditionBefore), "Projection must not rewrite the live intermediary.");

            HistoryResult undone = runtime.History.Undo(change.Id);

            Assert.That(undone.Ok, Is.True, string.Join("; ", undone.Diagnostics));
            Assert.That(EditorJsonUtility.ToJson(graph), Is.EqualTo(graphBefore));
            Assert.That(EditorJsonUtility.ToJson(fact), Is.EqualTo(factBefore));

            ScriptableObject CreateAsset(Type type, string name)
            {
                ScriptableObject asset = ScriptableObject.CreateInstance(type);
                asset.name = name;
                using var serialized = new SerializedObject(asset);
                serialized.FindProperty("authoringId").stringValue = Guid.NewGuid().ToString("D");
                serialized.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(asset, _bed.Folder + "/" + name + ".asset");
                return asset;
            }
        }

        [Test]
        public void R9A_ReflectedCompositionThenConnectValidatesOnlyTheFinalGraphAndUndoRestores()
        {
            using StudioRuntime runtime = DialogueRuntime(out ScriptableObject graph);
            AuthoringRef reference = runtime.Resolver.BuildRef(graph)!;
            string before = EditorJsonUtility.ToJson(graph);
            byte[] bytes = File.ReadAllBytes(AssetDatabase.GetAssetPath(graph));
            var edges = new JArray(new JObject { ["from"] = 0, ["port"] = "Next", ["option"] = 0, ["to"] = 1 });
            ChangeSet change = StudioTestBed.NewChangeSet("Add and connect a line", null,
                StudioTestBed.Op("connect", "set", reference,
                    new JObject { ["field"] = "edges", ["value"] = edges }, "append"),
                StudioTestBed.Op("append", "dialogue.addLine", reference,
                    new JObject { ["text"] = "The bell rings", ["after"] = -1 }));

            StagedChangeSet staged = runtime.Engine.Stage(change);

            Assert.That(staged.Ok, Is.True, string.Join("; ", staged.AllDiagnostics));
            Assert.That(EditorJsonUtility.ToJson(graph), Is.EqualTo(before));
            Assert.That(File.ReadAllBytes(AssetDatabase.GetAssetPath(graph)), Is.EqualTo(bytes));
            ApplyReport applied = runtime.Engine.Apply(staged);
            Assert.That(applied.State, Is.EqualTo(ChangeSetState.Applied), string.Join("; ", applied.Diagnostics));
            using (var serialized = new SerializedObject(graph))
            {
                Assert.That(serialized.FindProperty("nodes").arraySize, Is.EqualTo(2));
                Assert.That(serialized.FindProperty("nodes").GetArrayElementAtIndex(1).FindPropertyRelative("text").stringValue,
                    Is.EqualTo("The bell rings"));
                Assert.That(serialized.FindProperty("edges").GetArrayElementAtIndex(0).FindPropertyRelative("to").intValue, Is.EqualTo(1));
            }
            Assert.That(DialogueDiagnostics(graph), Is.Empty, "The accepted graph must satisfy the real mandatory validator.");

            HistoryResult undone = runtime.History.Undo(change.Id);

            Assert.That(undone.Ok, Is.True, string.Join("; ", undone.Diagnostics));
            Assert.That(EditorJsonUtility.ToJson(graph), Is.EqualTo(before));
        }

        [Test]
        public void R9A_ReflectedCompositionWithFinalUnreachableNodeIsRefusedWithoutWrites()
        {
            using StudioRuntime runtime = DialogueRuntime(out ScriptableObject graph);
            string before = EditorJsonUtility.ToJson(graph);
            ChangeSet change = StudioTestBed.NewChangeSet("Leave a disconnected line", null,
                StudioTestBed.Op("append", "dialogue.addLine", runtime.Resolver.BuildRef(graph),
                    new JObject { ["text"] = "Unreachable", ["after"] = -1 }));

            StagedChangeSet staged = runtime.Engine.Stage(change);

            Assert.That(staged.Ok, Is.False);
            Assert.That(staged.AllDiagnostics, Has.Some.Matches<Diagnostic>(diagnostic =>
                diagnostic.Code == "GP-DLG-005" || diagnostic.Message.Contains("[GP-DLG-005]")));
            ApplyReport refused = runtime.Engine.Apply(staged);
            Assert.That(refused.State, Is.EqualTo(ChangeSetState.Rejected));
            Assert.That(EditorJsonUtility.ToJson(graph), Is.EqualTo(before));
        }

        private StudioRuntime DialogueRuntime(out ScriptableObject graph, params Type[] additionalTypes)
        {
            Type? type = Type.GetType("GameCore.Gameplay.Dialogue.DialogueGraphDefinition, GameCore.Gameplay.Dialogue");
            Type? tools = Type.GetType("GameCore.Gameplay.Dialogue.Editor.DialogueTools, GameCore.Gameplay.Dialogue.Editor");
            Assert.That(type, Is.Not.Null, "The real gameplay dialogue definition is required by this regression.");
            Assert.That(tools, Is.Not.Null);
            graph = ScriptableObject.CreateInstance(type!);
            graph.name = "ProjectionGraph";
            using (var serialized = new SerializedObject(graph))
            {
                serialized.FindProperty("authoringId").stringValue = Guid.NewGuid().ToString("D");
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            MethodInfo addLine = tools!.GetMethod("AddLine")!;
            addLine.Invoke(null, new object?[] { graph, "Start", string.Empty, -1, null });
            AssetDatabase.CreateAsset(graph, _bed.Folder + "/ProjectionGraph.asset");
            AssetDatabase.SaveAssets();
            var types = new Type[additionalTypes.Length + 1];
            types[0] = type!;
            Array.Copy(additionalTypes, 0, types, 1, additionalTypes.Length);
            var runtime = StudioRuntime.Create(new StudioRuntimeOptions
            {
                Paths = new StudioPaths(StudioTestBed.ProjectRoot, _bed.StateRoot, "projection-validation"),
                Log = _bed.Log,
                TypeSource = () => types,
                ToolMethodSource = () => new[] { addLine },
                SearchFolders = new[] { _bed.Folder },
                LoadIndexCache = false,
            });
            runtime.Index.Rebuild();
            return runtime;
        }

        private static IEnumerable DialogueDiagnostics(ScriptableObject graph)
        {
            Type? validator = Type.GetType("GameCore.Gameplay.Dialogue.Editor.DialogueValidator, GameCore.Gameplay.Dialogue.Editor");
            Assert.That(validator, Is.Not.Null);
            return (IEnumerable)validator!.GetMethod("Validate")!.Invoke(null, new object[] { graph })!;
        }
    }
}

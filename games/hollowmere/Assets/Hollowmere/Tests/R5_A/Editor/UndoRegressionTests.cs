#nullable enable
using System;
using System.IO;
using System.Collections;
using UnityEngine.TestTools;
using System.Linq;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.Npc;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hollowmere.R5_A
{
    public sealed class UndoRegressionTests
    {
        private const string Graph = "Assets/Hollowmere/Dialogue/Graphs/Odd.asset";
        private StudioRuntime runtime = null!;
        private byte[] graphBefore = null!;
        private string state = string.Empty;
        private string folder = string.Empty;
        private static string Project => Directory.GetParent(Application.dataPath)!.FullName;
        private static string Repo => Path.GetFullPath(Path.Combine(Project, "../.."));
        private static ChangeSet Witness(string row, string run, string name) => StudioJson.Deserialize<ChangeSet>(File.ReadAllText(
            Path.Combine(Repo, "artifacts/studio/verification", row, run, "workflow", name, "candidate.json")));

        [SetUp]
        public void SetUp()
        {
            graphBefore = File.ReadAllBytes(Graph);
            EditorSceneManager.OpenScene("Assets/Hollowmere/Regions/ThornwickVillage.unity");
            state = Path.Combine(Path.GetTempPath(), "r5-a-" + Guid.NewGuid().ToString("N"));
            folder = "Assets/R5_A_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            OpenRuntime();
        }

        private void OpenRuntime()
        {
            runtime = StudioRuntime.Create(new StudioRuntimeOptions { Paths = new StudioPaths(Project, state, "r5-a"),
                LoadIndexCache = false, Log = new MemoryStudioLog() });
            runtime.Index.Rebuild();
        }

        [TearDown]
        public void TearDown()
        {
            runtime?.Dispose();
            Undo.ClearAll();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(folder);
            AssetDatabase.SaveAssets();
            File.WriteAllBytes(Graph, graphBefore);
            AssetDatabase.ImportAsset(Graph, ImportAssetOptions.ForceUpdate);
            if (Directory.Exists(state)) Directory.Delete(state, true);
        }

        [Test]
        public void R5_01_FerrymanWitness_UndoRemovesNpc_RedoKeepsIdentity()
        {
            ChangeSet candidate = Witness("W-AI-02", "p42c-text2-20261006T071746.894107Z", "ferryman2");
            string[] before = Entities();
            ApplyReport report = runtime.Engine.Apply(candidate);
            Assert.That(report.Ok, Is.True, string.Join(" | ", report.Diagnostics));
            string created = Entities().Except(before).Single();
            Assert.That(Entities().Length, Is.EqualTo(before.Length + 1));
            HistoryResult undo = runtime.History.Undo(candidate.Id);
            Assert.That(undo.Ok, Is.True, string.Join(" | ", undo.Diagnostics));
            Assert.That(Entities(), Is.EquivalentTo(before), "reported Undone must remove the created NPC");
            Assert.That(runtime.Resolver.Find(new AuthoringRef(AuthoringKind.Entity, created)), Is.Null);
            Assert.That(runtime.Index.FindNode(new AuthoringRef(AuthoringKind.Entity, created)), Is.Null);
            Assert.That(runtime.History.Redo(candidate.Id).Ok, Is.True);
            Assert.That(Entities().Except(before), Is.EqualTo(new[] { created }));
            Assert.That(runtime.History.Undo(candidate.Id).Ok, Is.True);
        }

        [Test]
        public void R5_02_OddWitness_FinalPostimageUndoesAfterRuntimeReopen()
        {
            ChangeSet candidate = Witness("W-AI-03", "p42c-narrative-20261006T073303.291662Z", "odd-line");
            ApplyReport report = runtime.Engine.Apply(candidate);
            Assert.That(report.Ok, Is.True, string.Join(" | ", report.Diagnostics));
            AssetDatabase.SaveAssets();
            runtime.Journal.Write(StudioJson.Deserialize<ChangeSet>(File.ReadAllText(Path.Combine(Repo,
                "artifacts/studio/verification/W-AI-06/p42c-reopen-20261006T073813.299352Z/workflow/odd-line/journal-undo.json"))));
            runtime.Dispose();
            Undo.ClearAll();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.ImportAsset(Graph, ImportAssetOptions.ForceUpdate);
            OpenRuntime();
            HistoryResult undo = runtime.History.Undo(candidate.Id);
            Assert.That(undo.Ok, Is.True, string.Join(" | ", undo.Diagnostics));
            Assert.That(runtime.History.Redo(candidate.Id).Ok, Is.True);
            Assert.That(runtime.History.Undo(candidate.Id).Ok, Is.True);
        }

        [TestCase("create")]
        [TestCase("duplicate")]
        [TestCase("place")]
        [TestCase("addComponent")]
        public void R5_01_CoreCreators_PrepareIdentity_UndoRedo(string tool)
        {
            var args = new JObject();
            AuthoringRef? target = null;
            if (tool == "create" || tool == "addComponent") args["type"] = "entity.instance";
            if (tool == "duplicate") target = runtime.Resolver.BuildRef(UnityEngine.Object.FindObjectsByType<AuthoredEntity>(FindObjectsSortMode.None)[0]);
            if (tool == "place")
            {
                AuthoredEntity source = UnityEngine.Object.FindObjectsByType<AuthoredEntity>(FindObjectsSortMode.None)[0];
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source.gameObject, folder + "/Placed.prefab");
                args["prefab"] = StudioJson.ToToken(runtime.Resolver.BuildRef(prefab)!);
                args["position"] = new JArray(4f, 0.083f, 1.1f);
            }
            if (tool == "addComponent") target = runtime.Resolver.BuildRef(new GameObject("R5 component host"));
            var candidate = StudioRuntime.Single("R5 creator " + tool, IntentOrigin.Manual, new Operation("op1", tool, target, args));
            string[] before = Entities();
            bool prepared = false;
            runtime.Engine.Options.FaultHook = (point, _) =>
            {
                if (point != EngineFaultPoint.AfterPrepared) return;
                UndoPayload? payload = UndoPayload.Parse(runtime.Journal.Read(candidate.Id)!.Outcomes![0].Undo!.Inverse, out _);
                if (payload!.Replay == null) return;
                Assert.That(payload.Operations.Count, Is.GreaterThan(0));
                Assert.That(Entities(), Is.EquivalentTo(before), "prepare precedes creation");
                prepared = true;
            };
            ApplyReport report = runtime.Engine.Apply(candidate);
            runtime.Engine.Options.FaultHook = null;
            Assert.That(report.Ok, Is.True, string.Join(" | ", report.Diagnostics));
            Assert.That(prepared, Is.True, "identity and inverse must be durable before creation");
            string created = Entities().Except(before).Single();
            Assert.That(runtime.History.Undo(candidate.Id).Ok, Is.True);
            Assert.That(Entities(), Is.EquivalentTo(before));
            Assert.That(runtime.History.Redo(candidate.Id).Ok, Is.True);
            Assert.That(Entities().Except(before), Is.EqualTo(new[] { created }));
            Assert.That(runtime.History.Undo(candidate.Id).Ok, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void R5_01_SetPatrol_RestoresBehaviourAndCreationIdentity(bool existing)
        {
            string path = folder + "/Npc.asset";
            AssetDatabase.CopyAsset("Assets/Hollowmere/Npcs/Definitions/Odd.asset", path);
            NpcDefinition npc = AssetDatabase.LoadAssetAtPath<NpcDefinition>(path);
            var serialized = new SerializedObject(npc);
            serialized.FindProperty("authoringId").stringValue = Guid.NewGuid().ToString("D");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            npc.SetBehaviour(null);
            if (existing)
                GameCore.Gameplay.Npc.Editor.NpcTools.SetPatrol(npc, new[] { Vector3.zero }, 2);
            EditorUtility.SetDirty(npc);
            AssetDatabase.SaveAssets();
            string? original = npc.Behaviour == null ? null : EditorJsonUtility.ToJson(npc.Behaviour);
            runtime.Index.Rebuild();
            var candidate = StudioRuntime.Single("R5 patrol", IntentOrigin.Manual, new Operation("op1", "npc.setPatrol",
                runtime.Resolver.BuildRef(npc, AuthorScope.Definition), new JObject { ["points"] = new JArray { new JArray(1, 0, 2) } }));
            ApplyReport report = runtime.Engine.Apply(candidate);
            Assert.That(report.Ok, Is.True, string.Join(" | ", report.Diagnostics));
            string createdId = npc.Behaviour!.AuthoringId;
            string behaviourPath = AssetDatabase.GetAssetPath(npc.Behaviour);
            HistoryResult undo = runtime.History.Undo(candidate.Id);
            Assert.That(undo.Ok, Is.True, string.Join(" | ", undo.Diagnostics));
            if (existing) Assert.That(EditorJsonUtility.ToJson(npc.Behaviour), Is.EqualTo(original));
            else { Assert.That(npc.Behaviour == null, Is.True); Assert.That(File.Exists(behaviourPath), Is.False); }
            Assert.That(runtime.History.Redo(candidate.Id).Ok, Is.True);
            Assert.That(npc.Behaviour!.AuthoringId, Is.EqualTo(createdId));
            Assert.That(runtime.History.Undo(candidate.Id).Ok, Is.True);
        }

        [TestCase("create")]
        [TestCase("duplicate")]
        public void R5_01_CoreAssetCreators_PreparePathAndIdentity_UndoRedo(string tool)
        {
            AuthoringRef? target = null;
            var args = new JObject { ["type"] = "npc.behaviour", ["name"] = "Created", ["path"] = folder + "/Created.asset" };
            if (tool == "duplicate")
            {
                var source = ScriptableObject.CreateInstance<BehaviourDefinition>();
                source.EnsureAuthoringId();
                AssetDatabase.CreateAsset(source, folder + "/Source.asset");
                target = runtime.Resolver.BuildRef(source, AuthorScope.Definition);
                args.Remove("type");
            }
            ChangeSet candidate = StudioRuntime.Single("R5 asset creator", IntentOrigin.Manual, new Operation("op1", tool, target, args));
            bool prepared = false;
            runtime.Engine.Options.FaultHook = (point, _) =>
            {
                if (point != EngineFaultPoint.AfterPrepared) return;
                UndoPayload? payload = UndoPayload.Parse(runtime.Journal.Read(candidate.Id)!.Outcomes![0].Undo!.Inverse, out _);
                if (payload!.Replay?["assetPath"] == null) return;
                Assert.That(File.Exists((string)payload.Replay["assetPath"]!), Is.False);
                Assert.That((string?)payload.Replay["authoringId"], Is.Not.Null.And.Not.Empty);
                prepared = true;
            };
            ApplyReport applied = runtime.Engine.Apply(candidate);
            runtime.Engine.Options.FaultHook = null;
            Assert.That(applied.Ok, Is.True, string.Join(" | ", applied.Diagnostics));
            Assert.That(prepared, Is.True);
            BehaviourDefinition created = AssetDatabase.LoadAssetAtPath<BehaviourDefinition>(folder + "/Created.asset");
            string id = created.AuthoringId;
            Assert.That(runtime.History.Undo(candidate.Id).Ok, Is.True);
            Assert.That(File.Exists(folder + "/Created.asset"), Is.False);
            Assert.That(runtime.Index.FindNode(new AuthoringRef(AuthoringKind.Definition, id)), Is.Null);
            Assert.That(runtime.History.Redo(candidate.Id).Ok, Is.True);
            Assert.That(AssetDatabase.LoadAssetAtPath<BehaviourDefinition>(folder + "/Created.asset").AuthoringId, Is.EqualTo(id));
            Assert.That(runtime.History.Undo(candidate.Id).Ok, Is.True);
        }

        [Test]
        public void R5_01_Replace_PreparesOriginalHierarchyBeforeMutation()
        {
            AuthoredEntity original = UnityEngine.Object.FindObjectsByType<AuthoredEntity>(FindObjectsSortMode.None)[0];
            string id = original.AuthoringId;
            string name = original.name;
            Vector3 position = original.transform.position;
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(original.gameObject, folder + "/Replacement.prefab");
            ChangeSet candidate = StudioRuntime.Single("R5 replace", IntentOrigin.Manual, new Operation("op1", "replace",
                runtime.Resolver.BuildRef(original), new JObject { ["with"] = StudioJson.ToToken(runtime.Resolver.BuildRef(prefab)!) }));
            bool prepared = false;
            runtime.Engine.Options.FaultHook = (point, _) =>
            {
                if (point != EngineFaultPoint.AfterPrepared) return;
                UndoPayload? payload = UndoPayload.Parse(runtime.Journal.Read(candidate.Id)!.Outcomes![0].Undo!.Inverse, out _);
                if (!payload!.Operations.Any(op => op.Tool == "history.restoreObject")) return;
                Assert.That(original != null, Is.True);
                prepared = true;
            };
            Assert.That(runtime.Engine.Apply(candidate).Ok, Is.True);
            runtime.Engine.Options.FaultHook = null;
            Assert.That(prepared, Is.True);
            Assert.That(runtime.History.Undo(candidate.Id).Ok, Is.True);
            var restored = (AuthoredEntity)runtime.Resolver.Find(new AuthoringRef(AuthoringKind.Entity, id))!;
            Assert.That(restored.name, Is.EqualTo(name));
            Assert.That(restored.transform.position, Is.EqualTo(position));
            Assert.That(runtime.History.Redo(candidate.Id).Ok, Is.True);
            Assert.That(runtime.History.Undo(candidate.Id).Ok, Is.True);
        }

        [Test]
        public void R5_01_CrashAtPreparedNpcCreationRetainsDeletionAndReplay()
        {
            ChangeSet witness = Witness("W-AI-02", "p42c-text2-20261006T071746.894107Z", "ferryman2");
            string[] before = Entities();
            Assert.That(runtime.Registry.Catalog.FindTool("history.deleteCreated"), Is.Null, "prepared inverse is internal");
            runtime.Engine.Options.FaultHook = (point, _) =>
            {
                if (point != EngineFaultPoint.AfterPrepared) return;
                UndoPayload? payload = UndoPayload.Parse(runtime.Journal.Read(witness.Id)!.Outcomes![0].Undo!.Inverse, out _);
                if (!payload!.Operations.Any(op => op.Tool == "history.deleteCreated")) return;
                Assert.That(payload.Replay?["authoringId"], Is.Not.Null);
                Assert.That(Entities(), Is.EquivalentTo(before));
                throw new SimulatedCrashException("R5 creation checkpoint");
            };
            Assert.Throws<SimulatedCrashException>(() => runtime.Engine.Apply(witness));
            runtime.Dispose();
            OpenRuntime();
            Assert.That(runtime.History.ResumeInterrupted(witness.Id).Ok, Is.False);
            HistoryResult rollback = runtime.History.RollbackInterrupted(witness.Id);
            Assert.That(rollback.Ok, Is.True, string.Join(" | ", rollback.Diagnostics));
            Assert.That(Entities(), Is.EquivalentTo(before));
        }

        [Test]
        public void R5_02_FinalPostimageStillRefusesLaterExternalEdit()
        {
            ChangeSet candidate = Witness("W-AI-03", "p42c-narrative-20261006T073303.291662Z", "odd-line");
            Assert.That(runtime.Engine.Apply(candidate).Ok, Is.True);
            UnityEngine.Object graph = AssetDatabase.LoadMainAssetAtPath(Graph);
            var serialized = new SerializedObject(graph);
            SerializedProperty nodes = serialized.FindProperty("nodes");
            nodes.GetArrayElementAtIndex(nodes.arraySize - 1).FindPropertyRelative("text").stringValue = "Later creator edit";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            HistoryResult undo = runtime.History.Undo(candidate.Id);
            Assert.That(undo.Ok, Is.False);
            Assert.That(undo.Diagnostics.Any(d => d.Code == DiagnosticCodes.Conflict), Is.True);
        }

        private static string[] Entities() => UnityEngine.Object.FindObjectsByType<AuthoredEntity>(FindObjectsSortMode.None)
            .Select(e => e.AuthoringId).OrderBy(id => id, StringComparer.Ordinal).ToArray();
    }
}

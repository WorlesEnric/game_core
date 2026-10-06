#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Linq;
using GameCore.Gameplay.Compile;
using GameCore.Gameplay.Dialogue;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Npc;
using GameCore.Gameplay.World;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using GameCore.Studio.Gameplay;
using Newtonsoft.Json.Linq;
using Hollowmere.Boot;
using Hollowmere.P3_2.Workflows;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hollowmere.R6_F
{
    public sealed class ContentClosureTests
    {
        private const string BackupKey = "R6.F.ClosureBackup";
        private const string CandidateId = "cs_01M49500E9KFTHBKGBCXWW3VZC";
        private const string GraphPath = "Assets/Hollowmere/Dialogue/Graphs/FerrymanBram.asset";
        private const string ContentPath = "Assets/Hollowmere/Rules/HollowmereContent.asset";
        private const string Village = "Assets/Hollowmere/Regions/ThornwickVillage.unity";
        private static string Project => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static string Backup => SessionState.GetString(BackupKey, "");

        private static StudioRuntime Runtime() => StudioRuntime.Create(new StudioRuntimeOptions
        {
            Paths = new StudioPaths(Project, Path.Combine(Backup, "state"), "r6-f"),
            SearchFolders = new[] { "Assets/Hollowmere" }, LoadIndexCache = false,
        });

        private static ChangeSet Candidate()
        {
            string path = Path.GetFullPath(Path.Combine(Project, "../../artifacts/studio/verification/W-AI-02/p42f-npc-20261006T174113.356793Z/workflow/ferryman2/candidate.json"));
            byte[] bytes = File.ReadAllBytes(path);
            Assert.That(ContentStamp.Sha256Hex(bytes), Is.EqualTo("fb19405db6643d7a0243826aee8ce33ff92a049af23cf990c82abba700045d70"));
            return StudioJson.Deserialize<ChangeSet>(System.Text.Encoding.UTF8.GetString(bytes));
        }

        private static void SaveBaseline()
        {
            string backup = Path.Combine(Path.GetTempPath(), "r6-f-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backup);
            SessionState.SetString(BackupKey, backup);
            foreach (string folder in new[] { "World", "Rules", "Dialogue", "Npcs", "Regions" })
                foreach (string file in Directory.GetFiles("Assets/Hollowmere/" + folder, "*", SearchOption.AllDirectories))
                {
                    string destination = Path.Combine(backup, file);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(file, destination);
                }
        }

        [UnityTest]
        public IEnumerator R6_F_Request2_GraphCreationEnrollsAndHistoryRestoresClosure()
        {
            SaveBaseline();
            EditorSceneManager.OpenScene(Village, OpenSceneMode.Single);
            string id;
            using (var runtime = Runtime())
            {
                runtime.Index.Rebuild();
                var args = (JObject)Candidate().Operations.Single(o => o.OpId == "bellDialogue").Args!.DeepClone();
                args.Remove("type");
                var change = StudioRuntime.Single("Create enrolled graph", IntentOrigin.Manual,
                    new Operation("graph", "dialogue.createGraph", args: args, preconditions: Preconditions.None));
                id = change.Id;
                SessionState.SetString("R6.F.GraphChange", id);
                var result = runtime.Engine.Apply(change);
                Assert.That(result.State, Is.EqualTo(ChangeSetState.Applied), string.Join("; ", result.Diagnostics.Select(d => d.Message)));
                Assert.That(AssetDatabase.LoadAssetAtPath<GameplayContentSet>(ContentPath).Definitions,
                    Does.Contain(AssetDatabase.LoadAssetAtPath<DialogueGraphDefinition>(GraphPath)));
                AssetDatabase.SaveAssets();
                Bake();
            }
            yield return new RecompileScripts(false);
            EditorSceneManager.OpenScene(S.BootScene, OpenSceneMode.Single);
            yield return new EnterPlayMode();
            yield return WorkflowPlayChecks.Until(() => UnityEngine.Object.FindFirstObjectByType<GameBoot>()?.Narrative != null, "enrolled graph boot", 60);
            yield return WorkflowPlayChecks.StartGame();
            yield return ObserveBell(false);
            yield return new ExitPlayMode();
            EditorSceneManager.OpenScene(Village, OpenSceneMode.Single);
            Undo.ClearAll();
            using (var runtime = Runtime())
            {
                runtime.Index.Rebuild();
                var result = runtime.History.Undo(SessionState.GetString("R6.F.GraphChange", ""));
                Assert.That(result.Ok, Is.True, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
                Assert.That(AssetDatabase.LoadAssetAtPath<DialogueGraphDefinition>(GraphPath), Is.Null);
                AssetDatabase.SaveAssets();
                Assert.That(File.ReadAllBytes(ContentPath), Is.EqualTo(File.ReadAllBytes(Path.Combine(Backup, ContentPath))));
            }
            Assert.That(SessionState.GetBool("R6.F.DialogueStarted", false), Is.True, SessionState.GetString("R6.F.DialogueReason", ""));
        }

        [Test]
        public void R6_F_Request2_ValidatorRejectsExistingUnenrolledGraph()
        {
            SaveBaseline();
            EditorSceneManager.OpenScene(Village, OpenSceneMode.Single);
            var graph = ScriptableObject.CreateInstance<DialogueGraphDefinition>();
            AssetDatabase.CreateAsset(graph, GraphPath);
            using (var runtime = Runtime())
            {
                runtime.Index.Rebuild();
                var value = (JObject)StudioJson.ToToken(Candidate());
                var ops = (JArray)value["operations"]!;
                ops.Single(o => (string?)o["opId"] == "bellDialogue").Remove();
                foreach (var op in ops)
                    foreach (var dep in ((JArray)op["dependsOn"]!).Where(d => (string?)d == "bellDialogue").ToArray()) dep.Remove();
                var diagnostics = DialogueClosureTools.ValidateCandidate(runtime, StudioJson.Deserialize<ChangeSet>(value.ToString()));
                Assert.That(diagnostics.Any(d => d.Message.Contains("npc_dialogue_not_enrolled", StringComparison.Ordinal)), Is.True,
                    string.Join("; ", diagnostics.Select(d => d.Message)));
                Assert.That(AssetDatabase.LoadAssetAtPath<GameplayContentSet>(ContentPath).Definitions, Has.No.Member(graph));
                var change = StudioJson.Deserialize<ChangeSet>(value.ToString());
                var staged = runtime.Engine.Stage(change, new StageOptions
                {
                    Mode = ValidationMode.Candidate, ToolCatalogRevision = runtime.Registry.Catalog.Revision,
                });
                Assert.That(staged.Ok, Is.False);
                Assert.That(staged.AllDiagnostics.Any(d => d.Message.Contains("npc_dialogue_not_enrolled", StringComparison.Ordinal)), Is.True,
                    string.Join("; ", staged.AllDiagnostics.Select(d => d.Message)));
                var applied = runtime.Engine.Apply(staged);
                Assert.That(applied.State, Is.EqualTo(ChangeSetState.Rejected));
                Assert.That(UnityEngine.Object.FindObjectsByType<AuthoredEntity>(FindObjectsSortMode.None).Length, Is.EqualTo(20));
                Assert.That(AssetDatabase.LoadAssetAtPath<GameplayContentSet>(ContentPath).Definitions, Has.No.Member(graph));
            }
        }

        [UnityTest]
        public IEnumerator R6_F_Request2_UnchangedCandidateStartsPresentsBellEndsAndDurableUndoRestores20()
        {
            SaveBaseline();
            EditorSceneManager.OpenScene(Village, OpenSceneMode.Single);
            Assert.That(UnityEngine.Object.FindObjectsByType<AuthoredEntity>(FindObjectsSortMode.None).Length, Is.EqualTo(20));
            using (var runtime = Runtime())
            {
                runtime.Index.Rebuild();
                var staged = runtime.Engine.Stage(Candidate(), new StageOptions
                {
                    Mode = ValidationMode.Candidate, ToolCatalogRevision = runtime.Registry.Catalog.Revision,
                });
                Assert.That(staged.Ok, Is.True, string.Join("; ", staged.AllDiagnostics.Select(d => d.Message)));
                var applied = runtime.Engine.Apply(staged);
                Assert.That(applied.State, Is.EqualTo(ChangeSetState.Applied), string.Join("; ", applied.Diagnostics.Select(d => d.Message)));
                Assert.That(UnityEngine.Object.FindObjectsByType<AuthoredEntity>(FindObjectsSortMode.None).Length, Is.EqualTo(21));
                var graph = AssetDatabase.LoadAssetAtPath<DialogueGraphDefinition>(GraphPath);
                var content = AssetDatabase.LoadAssetAtPath<GameplayContentSet>(ContentPath);
                SessionState.SetBool("R6.F.CandidateEnrolled", content.Definitions.Contains(graph));
                TestContext.WriteLine("Retained candidate enrolled=" + content.Definitions.Contains(graph));
                AssetDatabase.SaveAssets();
                EditorSceneManager.SaveOpenScenes();
                Bake();
            }
            yield return new RecompileScripts(false);
            EditorSceneManager.OpenScene(S.BootScene, OpenSceneMode.Single);
            yield return new EnterPlayMode();
            yield return WorkflowPlayChecks.Until(() => UnityEngine.Object.FindFirstObjectByType<GameBoot>()?.Narrative != null, "R6-F boot", 60);
            yield return WorkflowPlayChecks.StartGame();
            yield return ObserveBell(true);
            yield return new ExitPlayMode();
            EditorSceneManager.OpenScene(Village, OpenSceneMode.Single);
            using (var runtime = Runtime())
            {
                runtime.Index.Rebuild();
                var undone = runtime.History.Undo(CandidateId);
                Assert.That(undone.Ok, Is.True, string.Join("; ", undone.Diagnostics.Select(d => d.Message)));
                Assert.That(runtime.Journal.Read(CandidateId)!.EffectiveState, Is.EqualTo(ChangeSetState.Undone));
                Assert.That(UnityEngine.Object.FindObjectsByType<AuthoredEntity>(FindObjectsSortMode.None).Length, Is.EqualTo(20));
                Assert.That(AssetDatabase.LoadAssetAtPath<DialogueGraphDefinition>(GraphPath), Is.Null);
                Assert.That(AssetDatabase.LoadAssetAtPath<GameplayContentSet>(ContentPath).Definitions.All(d => d != null), Is.True, "undo cannot leave a dangling closure member");
                AssetDatabase.SaveAssets();
                Assert.That(File.ReadAllBytes(ContentPath), Is.EqualTo(File.ReadAllBytes(Path.Combine(Backup, ContentPath))), "durable inverse restores exact content-set bytes");
            }
            Assert.That(SessionState.GetBool("R6.F.DialogueStarted", false), Is.True, SessionState.GetString("R6.F.DialogueReason", ""));
            Assert.That(SessionState.GetBool("R6.F.CandidateEnrolled", false), Is.True, "same-change-set graph enrollment");
        }

        private static IEnumerator ObserveBell(bool retainedCandidate)
        {
            Assert.That(Application.isPlaying, Is.True);
            var boot = UnityEngine.Object.FindFirstObjectByType<GameBoot>();
            var npc = boot.NpcExtension!.Records.Single(n => n.DisplayName == (retainedCandidate ? "Ferryman Bram" : "Maren"));
            var runner = boot.Modules!.Dialogue.Runner!;
            string graphRef = retainedCandidate ? npc.DialogueGraph : AssetDatabase.LoadAssetAtPath<DialogueGraphDefinition>(GraphPath).AuthoringId;
            bool started = runner.Start(npc.AuthoringId, graphRef, out string reason);
            SessionState.SetBool("R6.F.DialogueStarted", started);
            SessionState.SetString("R6.F.DialogueReason", reason);
            TestContext.WriteLine("DialogueRunner.Start: " + started + "; reason=" + reason);
            // Assert after durable undo so refusal evidence also retains the restored roster.
            if (!started) yield break;
            yield return WorkflowPlayChecks.Until(() => boot.Modules.Dialogue.Presenter!.Last.Active, "Bram dialogue active");
            Assert.That(boot.Modules.Dialogue.Presenter!.Last.Text, Is.EqualTo("The bell still rings across the water. I hear it from my ferry every night."));
            yield return WorkflowPlayChecks.Until(() => boot.Modules.Dialogue.Presenter!.Last.CanAdvance, "Bram bell line can advance");
            Assert.That(runner.Advance(), Is.True);
            yield return WorkflowPlayChecks.Until(() => !boot.Modules.Dialogue.Presenter!.Last.Active, "Bram conversation ended");
            TestContext.WriteLine("Bell line presented; conversation ended; retainedCandidate=" + retainedCandidate);
        }

        private static void Bake()
        {
            var world = AssetDatabase.LoadAssetAtPath<WorldDefinition>("Assets/Hollowmere/World/Hollowmere.asset");
            var bake = Entry.Bake(world, BakePaths.ConventionFor(AssetDatabase.GetAssetPath(world)), false);
            Assert.That(bake.Succeeded, Is.True, bake.ToString());
        }

        [UnityTearDown]
        public IEnumerator RestoreFixture()
        {
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (Backup.Length == 0) yield break;
            AssetDatabase.SaveAssets();
            foreach (string path in new[] { GraphPath, "Assets/Hollowmere/Npcs/Definitions/FerrymanBramEntity.asset", "Assets/Hollowmere/Npcs/Definitions/FerrymanBramPatrol.asset", "Assets/Hollowmere/Npcs/Definitions/FerrymanBram.asset" })
                if (!File.Exists(Path.Combine(Backup, path))) AssetDatabase.DeleteAsset(path);
            foreach (string file in Directory.GetFiles(Path.Combine(Backup, "Assets"), "*", SearchOption.AllDirectories))
            {
                string target = file.Substring(Backup.Length + 1);
                if (!File.Exists(target) || !File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(target))) File.Copy(file, target, true);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            Directory.Delete(Backup, true);
            SessionState.EraseString(BackupKey);
            yield return new RecompileScripts(false);
        }
    }
}

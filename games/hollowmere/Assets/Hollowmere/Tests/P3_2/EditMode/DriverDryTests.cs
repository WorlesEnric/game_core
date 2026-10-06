#nullable enable
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Hollowmere.P3_2.Workflows;
using Driver = Hollowmere.P3_2.Workflows.Workflows;

namespace Hollowmere.P3_2.Headless
{
    public sealed class DriverDryTests
    {
        [Test]
        public void R5_07_TextSelectionDoesNotLookForOddInVillage()
        {
            Assert.That(Driver.For("text2").Any(s => s.Name == "select Maren, Pip, Odd"), Is.False,
                "P4.2c W-AI-02: Odd lives in Marsh; use his indexed definition in the multi-selection");
        }

        [Test]
        public void R5_07_NarrativeOpensMarshBeforeSelectingOdd()
        {
            var steps = Driver.For("narrative").ToList();
            int odd = steps.FindIndex(s => s.Name == "select Odd");
            Assert.That(steps.Take(odd).Any(s => s.Name.Contains("BlackmereMarsh")), Is.True);
        }

        [TestCase("text2", "W-AI-02")]
        [TestCase("narrative", "W-AI-03")]
        [TestCase("narrative", "W-AI-05")]
        public void R5_07_PlayEffectIsRequiredBeforeAcceptance(string workflow, string row)
        {
            Assert.That(Driver.For(workflow).Any(s => s.Name == "Play effect " + row), Is.True);
        }

        [Test]
        public void R5_07_QuestSimulationUsesIndexedOilFlask()
        {
            var method = typeof(Driver).GetMethod("QuestSimulate", BindingFlags.NonPublic | BindingFlags.Static)!;
            method.Invoke(null, new object[] { "r5-dry", "regression" });
            string path = Path.Combine(WorkflowRunner.OutputDir, "r5-dry/quest-simulate-regression.json");
            string result = File.ReadAllText(path);
            Assert.That(result, Does.Not.Contain("GP-QST-004"), "P4.2c retained simulator refusal is a failure");
            Assert.That(result, Does.Not.Contain("error:"));
            Assert.That(result, Does.Not.Contain("oil_flask"));
        }
        [Test]
        public void R5_07_RealIndexResolvesMarshOddAndOilIdentity()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(S.MarshScene, UnityEditor.SceneManagement.OpenSceneMode.Single);
            Assert.That(UnityEngine.GameObject.Find("Odd"), Is.Not.Null);
            string state = Path.Combine(Path.GetTempPath(), "r5-driver-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                using var runtime = GameCore.Studio.Edit.StudioRuntime.Create(new GameCore.Studio.Edit.StudioRuntimeOptions
                {
                    Paths = new GameCore.Studio.Edit.StudioPaths(WorkflowRunner.ProjectRoot, state, "r5-dry"),
                    SearchFolders = new[] { "Assets/Hollowmere" },
                    LoadIndexCache = false,
                });
                string id = WorkflowPlayChecks.OilFlaskIdentity(runtime);
                var oil = UnityEditor.AssetDatabase.LoadAssetAtPath<GameCore.Gameplay.Inventory.ItemDefinition>("Assets/Hollowmere/Items/OilFlask.asset");
                Assert.That(id, Is.EqualTo(oil.AuthoringId));
                Assert.That(runtime.Index.Snapshot().Nodes.Any(n => n.Name == "Odd"), Is.True);
            }
            finally
            {
                if (Directory.Exists(state)) Directory.Delete(state, true);
            }
        }

        [TestCase("GP-QST-004: nothing named oil_flask")]
        [TestCase("refused: unknown item")]
        [TestCase("error: simulator exception")]
        public void R5_07_RefusalsCannotPass(string result)
        {
            Assert.Throws<System.InvalidOperationException>(() => WorkflowPlayChecks.CheckedToolOutput(() => result));
        }

        [Test]
        public void R5_07_ExceptionsCannotPass()
        {
            Assert.Throws<System.ArgumentException>(() => WorkflowPlayChecks.CheckedToolOutput(() => throw new System.ArgumentException("GP-QST-004")));
        }

        [TestCase("npc")]
        [TestCase("dialogue")]
        [TestCase("quest")]
        public void R5_07_EditTimeChecksCannotPassAsPlay(string kind)
        {
            var receipt = new Newtonsoft.Json.Linq.JObject();
            var observe = kind == "npc" ? WorkflowPlayChecks.Npc(null!, "", receipt) : kind == "dialogue"
                ? WorkflowPlayChecks.Dialogue(null!, new[] { "new line" }, receipt) : WorkflowPlayChecks.Quest(null!, "", receipt);
            Assert.Throws<System.InvalidOperationException>(() => observe.MoveNext());
        }

        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator R5_07_DryBootObservesLogicAndRefusesHeadlessNavAndUnchangedQuest()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(S.BootScene, UnityEditor.SceneManagement.OpenSceneMode.Single);
            yield return new UnityEngine.TestTools.EnterPlayMode();
            yield return WorkflowPlayChecks.Until(() => UnityEngine.Object.FindFirstObjectByType<Hollowmere.Boot.GameBoot>()?.Narrative != null, "Hollowmere boot", 60);
            yield return WorkflowPlayChecks.StartGame();
            var boot = UnityEngine.Object.FindFirstObjectByType<Hollowmere.Boot.GameBoot>();
            var receipt = new Newtonsoft.Json.Linq.JObject();
            // Unmodified shipped content tests the real observers, not paid-candidate acceptance.
            string marenId = boot.NpcExtension!.Records.Single(n => n.DisplayName == "Maren").AuthoringId;
            yield return WorkflowPlayChecks.NpcMotion(boot, marenId, receipt);
            Assert.That((bool?)receipt["committedMotionObserved"], Is.True);
            Assert.That(marenId, Is.Not.Null.And.Not.Empty, "NPC authored ID survives the observation");
            boot = UnityEngine.Object.FindFirstObjectByType<Hollowmere.Boot.GameBoot>();
            Assert.That(boot, Is.Not.Null, "boot remains available after committed motion");
            Refuses(WorkflowPlayChecks.Npc(boot, marenId, new Newtonsoft.Json.Linq.JObject()), "requires graphical Play");
            yield return WorkflowPlayChecks.Dialogue(boot, new[] { "You lit the old shrine. My mother tended it. I owe you a crossing." }, receipt);
            Assert.That(receipt["unlitLines"], Is.Not.Null);
            Assert.That(receipt["litLines"], Is.Not.Null);
            var oil = UnityEditor.AssetDatabase.LoadAssetAtPath<GameCore.Gameplay.Inventory.ItemDefinition>("Assets/Hollowmere/Items/OilFlask.asset");
            Refuses(WorkflowPlayChecks.Quest(boot, oil.AuthoringId, new Newtonsoft.Json.Linq.JObject()), "must require two indexed oil flasks");
            yield return new UnityEngine.TestTools.ExitPlayMode();
        }

        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator R5_07_UnmodifiedRetainedCandidatesHaveDialogueAndQuestPlayEffects()
        {
            string backup = Path.Combine(Path.GetTempPath(), "r5-replay-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backup);
            File.Copy(WorkflowPlayChecks.QuestPath, Path.Combine(backup, "quest.asset"));
            File.Copy(WorkflowPlayChecks.OddPath, Path.Combine(backup, "odd.asset"));
            UnityEditor.SessionState.SetString("R5.P32.ReplayBackup", backup);
            string repo = Path.GetFullPath(Path.Combine(WorkflowRunner.ProjectRoot, "../.."));
            string witness = Path.Combine(repo, "artifacts/studio/verification/W-AI-03/p42c-narrative-20261006T073303.291662Z/workflow");
            using (var runtime = GameCore.Studio.Edit.StudioRuntime.Create(new GameCore.Studio.Edit.StudioRuntimeOptions
            {
                Paths = new GameCore.Studio.Edit.StudioPaths(WorkflowRunner.ProjectRoot, Path.Combine(backup, "state"), "r5-replay"),
                SearchFolders = new[] { "Assets/Hollowmere" }, LoadIndexCache = false,
            }))
            {
                runtime.Index.Rebuild();
                foreach (string tag in new[] { "odd-line", "quest" })
                {
                    var candidate = GameCore.Studio.Model.StudioJson.Deserialize<GameCore.Studio.Model.ChangeSet>(File.ReadAllText(Path.Combine(witness, tag, "candidate.json")));
                    var staged = runtime.Engine.Stage(candidate, new GameCore.Studio.Edit.StageOptions
                    {
                        Mode = GameCore.Studio.Model.ValidationMode.Candidate,
                        ToolCatalogRevision = runtime.Registry.Catalog.Revision,
                    });
                    Assert.That(staged.Ok, Is.True, string.Join("; ", staged.AllDiagnostics.Select(d => d.Message)));
                    var applied = runtime.Engine.Apply(staged);
                    Assert.That(applied.State, Is.EqualTo(GameCore.Studio.Model.ChangeSetState.Applied),
                        string.Join("; ", applied.Diagnostics.Select(d => d.Message)));
                }
                UnityEditor.AssetDatabase.SaveAssets();
            }
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(S.BootScene, UnityEditor.SceneManagement.OpenSceneMode.Single);
            yield return new UnityEngine.TestTools.EnterPlayMode();
            yield return WorkflowPlayChecks.Until(() => UnityEngine.Object.FindFirstObjectByType<Hollowmere.Boot.GameBoot>()?.Narrative != null, "Hollowmere replay boot", 60);
            yield return WorkflowPlayChecks.StartGame();
            var boot = UnityEngine.Object.FindFirstObjectByType<Hollowmere.Boot.GameBoot>();
            var receipt = new Newtonsoft.Json.Linq.JObject();
            yield return WorkflowPlayChecks.Dialogue(boot, new[] { "The shrine has its light back. Even the black water seems less lonely tonight." }, receipt);
            var oil = UnityEditor.AssetDatabase.LoadAssetAtPath<GameCore.Gameplay.Inventory.ItemDefinition>("Assets/Hollowmere/Items/OilFlask.asset");
            yield return WorkflowPlayChecks.Quest(boot, oil.AuthoringId, receipt);
            Assert.That((int?)receipt["stageAfterOne"], Is.EqualTo(1));
            Assert.That((int?)receipt["stageAfterTwo"], Is.EqualTo(2));
            TestContext.WriteLine(receipt.ToString());
            yield return new UnityEngine.TestTools.ExitPlayMode();
        }

        // Keep captured locals out of the UnityTest iterator: EnterPlayMode reload does not preserve its display class.
        private static void Refuses(System.Collections.IEnumerator probe, string detail)
        {
            Assert.That(() => probe.MoveNext(), Throws.TypeOf<System.InvalidOperationException>().With.Message.Contains(detail));
        }

        [UnityEngine.TestTools.UnityTearDown]
        public System.Collections.IEnumerator LeavePlay()
        {
            if (UnityEditor.EditorApplication.isPlaying) yield return new UnityEngine.TestTools.ExitPlayMode();
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            string backup = UnityEditor.SessionState.GetString("R5.P32.ReplayBackup", "");
            if (backup.Length > 0)
            {
                File.Copy(Path.Combine(backup, "quest.asset"), WorkflowPlayChecks.QuestPath, true);
                File.Copy(Path.Combine(backup, "odd.asset"), WorkflowPlayChecks.OddPath, true);
                UnityEditor.AssetDatabase.ImportAsset(WorkflowPlayChecks.QuestPath, UnityEditor.ImportAssetOptions.ForceUpdate);
                UnityEditor.AssetDatabase.ImportAsset(WorkflowPlayChecks.OddPath, UnityEditor.ImportAssetOptions.ForceUpdate);
                UnityEditor.SessionState.EraseString("R5.P32.ReplayBackup");
                Directory.Delete(backup, true);
            }
        }
    }
}

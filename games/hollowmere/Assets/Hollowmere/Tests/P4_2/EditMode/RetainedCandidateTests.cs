#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Linq;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Logic.Editor;
using GameCore.Gameplay.Npc;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Etos.Testing;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hollowmere.P4_2
{
    public sealed class RetainedCandidateTests
    {
        // Exact bytes from artifacts/studio/verification/W-AI-02/
        // p42k-npc-20261007T191317.157379Z/workflow/ferryman2/candidate.json (6095 bytes).
        private const string Fixture = "Assets/Hollowmere/Tests/P4_2/EditMode/Fixtures/FerrymanElianCandidate.json";
        private const string FixtureSha256 = "839a6c4907699a312434de3d0cd70c66804d484c1900cf3d409c1124cbb3849b";
        private const string Content = "Assets/Hollowmere/Rules/HollowmereContent.asset";
        private const string Roster = "Assets/Hollowmere/Npcs/NpcRoster.asset";
        private const string Graph = "Assets/Hollowmere/Dialogue/Graphs/FerrymanElian.asset";
        private const string Village = "Assets/Hollowmere/Regions/ThornwickVillage.unity";

        // Workflows.Roster and the retained roster-before.json count AuthoredEntity instances,
        // not NpcRoster definitions (six in the committed project).
        private static int SceneRosterCount() => UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
            FindObjectsInactive.Include, FindObjectsSortMode.None).Count(value => value.GetType().Name == "AuthoredEntity");

        [UnityTest]
        public IEnumerator R11_A_RetainedFerrymanCandidateEnrollsOnceAppliesAndUndoes()
        {
            string project = Directory.GetParent(Application.dataPath)!.FullName;
            byte[] fixtureBytes = File.ReadAllBytes(Path.Combine(project, Fixture));
            Assert.That(ContentStamp.Sha256Hex(fixtureBytes), Is.EqualTo(FixtureSha256), "retained worker output must not be rewritten");
            JObject candidate = JObject.Parse(System.Text.Encoding.UTF8.GetString(fixtureBytes));
            JObject retained = (JObject)candidate.DeepClone();
            string id = (string)candidate["id"]!;
            string[] created = candidate["operations"]!.Where(op => (string?)op["tool"] == "create")
                .Select(op => (string)op["args"]!["path"]!).ToArray();
            foreach (string path in created)
            {
                Assert.That(File.Exists(Path.Combine(project, path)), Is.False, "candidate output already exists: " + path);
                Assert.That(File.Exists(Path.Combine(project, path + ".meta")), Is.False, "candidate output metadata already exists: " + path);
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
                Assert.That(SceneManager.GetSceneAt(i).isDirty, Is.False, "regression requires saved scenes and must not discard user edits");
            Assert.That(EditorUtility.IsDirty(AssetDatabase.LoadMainAssetAtPath(Content)), Is.False);
            Assert.That(EditorUtility.IsDirty(AssetDatabase.LoadMainAssetAtPath(Roster)), Is.False);
            SceneSetup[] scenes = EditorSceneManager.GetSceneManagerSetup();
            byte[] contentBefore = File.ReadAllBytes(Path.Combine(project, Content));
            byte[] rosterBefore = File.ReadAllBytes(Path.Combine(project, Roster));
            byte[] sceneBefore = File.ReadAllBytes(Path.Combine(project, Village));
            string state = Path.Combine(Path.GetTempPath(), "gcstudio-r11-a-" + Guid.NewGuid().ToString("N"));
            StudioRuntime? runtime = null;
            bool applied = false;
            try
            {
                EditorSceneManager.OpenScene(Village, OpenSceneMode.Single);
                runtime = StudioRuntime.Create(new StudioRuntimeOptions
                {
                    Paths = new StudioPaths(project, state),
                    SearchFolders = new[] { "Assets/Hollowmere" },
                    IndexScope = AuthoringSourceScope.All,
                    LoadIndexCache = false,
                });
                var content = AssetDatabase.LoadAssetAtPath<GameplayContentSet>(Content);
                var roster = AssetDatabase.LoadAssetAtPath<NpcRoster>(Roster);
                Assert.That(content, Is.Not.Null);
                Assert.That(roster, Is.Not.Null);
                Assert.That(SceneRosterCount(), Is.EqualTo(20), "retained scene-entity roster baseline");
                Assert.That(roster.Npcs.Count, Is.EqualTo(6), "committed NPC-definition roster baseline");
                int definitionsBefore = content.Definitions.Count;
                RefreshStamps(candidate, runtime);
                Assert.That(JToken.DeepEquals(WithoutStamps(candidate), WithoutStamps(retained)), Is.True,
                    "only current reference/base-version stamps may change; never repair or remove worker operations");

                ToolCatalog catalog = runtime.Registry.Catalog;
                string revision = catalog.Revision ?? catalog.ComputeRevision();
                string? evidence = Environment.GetEnvironmentVariable("GAMECORE_R11_EVIDENCE");
                if (!string.IsNullOrEmpty(evidence))
                {
                    Directory.CreateDirectory(evidence);
                    File.WriteAllText(Path.Combine(evidence, "tool-catalog-after.json"), StudioJson.Serialize(catalog));
                    File.WriteAllText(Path.Combine(evidence, "fixture-stamps.json"), new JObject
                    {
                        ["fixtureSha256"] = FixtureSha256,
                        ["stampsRebased"] = !JToken.DeepEquals(candidate, retained),
                        ["candidate"] = candidate.DeepClone(),
                        ["helper"] = "ChangeSetEngine.Rebase (Workflows.RebaseAndApply)",
                    }.ToString());
                }

                // Only the retained HTTP response is substituted. The real client, gateway, Candidate-mode
                // staging, apply and history run unchanged; there is no submit, worker task or paid call.
                using (var companion = new FakeCompanion())
                {
                    companion.Start();
                    companion.FailNext("/api/v1/agents/gamecore-studio/http/v1/candidates/" + id, 200,
                        new JObject
                        {
                            ["changeSetId"] = id,
                            ["changeSet"] = candidate,
                            ["artifacts"] = new JArray(),
                            ["toolCatalogRevision"] = revision,
                        });
                    using (var client = new CompanionClient(new EtosClientOptions
                    {
                        ProjectId = new string('a', 64), NodeUrl = companion.NodeUrl,
                    }, new EtosCredentials(FakeCompanion.AppKey, companion.NodeUrl, "retained-r11-a")))
                    {
                        var queue = new MainThreadQueue();
                        using (var gateway = new EtosAgentGateway(client, runtime, queue, new MemoryCursorStore()))
                        {
                            runtime.Services.AgentGateway = gateway;
                            var importTask = gateway.ImportCandidateAsync(id);
                            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
                            while (!importTask.IsCompleted)
                            {
                                queue.Pump();
                                Assert.That(DateTime.UtcNow, Is.LessThan(deadline), "retained gateway import timed out");
                                yield return null;
                            }
                            queue.Pump();
                            Assert.That(importTask.IsFaulted, Is.False, importTask.Exception?.GetBaseException().ToString());
                            CandidateImport imported = importTask.GetAwaiter().GetResult();
                            Assert.That(imported.Ok, Is.True, Describe(imported.Diagnostics));
                            Assert.That(imported.Diagnostics, Is.Empty);
                            Assert.That(imported.Staged!.Options.Mode, Is.EqualTo(ValidationMode.Candidate));
                            Assert.That(imported.Staged.AllDiagnostics, Is.Empty);
                            StagedOperation enrollment = imported.Staged.Operations.Single(op => op.OpId == "enroll-dialogue");
                            Assert.That((bool?)enrollment.Preview?["alreadyListed"], Is.True,
                                "preview must include the earlier graph creator's implicit enrollment");
                            Assert.That((int?)enrollment.Preview?["index"], Is.EqualTo(definitionsBefore));
                            Assert.That(File.Exists(Path.Combine(project, Graph)), Is.False, "preview must not create the graph");
                            Assert.That(File.ReadAllBytes(Path.Combine(project, Content)), Is.EqualTo(contentBefore));

                            ApplyReport report = gateway.Apply(id);
                            applied = report.Ok;
                            Assert.That(report.Ok, Is.True, Describe(report.Diagnostics));
                            Assert.That(report.Diagnostics, Is.Empty);
                            Assert.That(SceneRosterCount(), Is.EqualTo(21));
                            Assert.That(roster.Npcs.Count, Is.EqualTo(7));
                            ScriptableObject graph = AssetDatabase.LoadAssetAtPath<ScriptableObject>(Graph);
                            Assert.That(graph, Is.Not.Null);
                            Assert.That(content.Definitions.Count(definition => definition == graph), Is.EqualTo(1));
                            Assert.That(content.Definitions.Count, Is.EqualTo(definitionsBefore + 1));
                            Assert.That(roster.Npcs.Count(npc => npc.DisplayName == "Ferryman Elian"), Is.EqualTo(1));
                            Assert.That(LogicValidator.ValidateSet(content).Any(d => d.Code == "GP-LOG-002"), Is.False);

                            var undo = runtime.History.Undo(id);
                            Assert.That(undo.Ok, Is.True, Describe(undo.Diagnostics));
                            applied = false;
                            AssetDatabase.SaveAssets();
                            Assert.That(SceneRosterCount(), Is.EqualTo(20));
                            Assert.That(roster.Npcs.Count, Is.EqualTo(6));
                            Assert.That(content.Definitions.Count, Is.EqualTo(definitionsBefore));
                            Assert.That(File.ReadAllBytes(Path.Combine(project, Content)), Is.EqualTo(contentBefore),
                                "normal history undo must restore exact content bytes, not normalized content or a backup");
                            Assert.That(File.ReadAllBytes(Path.Combine(project, Roster)), Is.EqualTo(rosterBefore));
                            Assert.That(File.ReadAllBytes(Path.Combine(project, Village)), Is.EqualTo(sceneBefore));
                            foreach (string path in created)
                            {
                                Assert.That(File.Exists(Path.Combine(project, path)), Is.False, "undo must remove " + path);
                                Assert.That(File.Exists(Path.Combine(project, path + ".meta")), Is.False, "undo must remove metadata for " + path);
                            }
                            Assert.That(companion.Calls.Count, Is.EqualTo(1), "retained import must not submit work or invoke a provider");
                            Assert.That(companion.Calls[0].Method, Is.EqualTo("GET"));
                        }
                    }
                }
            }
            finally
            {
                try
                {
                    if (applied && runtime != null) runtime.History.Undo(id);
                }
                finally
                {
                    runtime?.Dispose();
                    // Failure-only safety net for shared project fixtures; all restoration assertions above
                    // run before this cleanup, so it cannot make a broken production undo pass.
                    foreach (string path in created) AssetDatabase.DeleteAsset(path);
                    RestoreIfChanged(project, Content, contentBefore);
                    RestoreIfChanged(project, Roster, rosterBefore);
                    RestoreIfChanged(project, Village, sceneBefore);
                    if (scenes.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(scenes);
                    else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    if (Directory.Exists(state)) Directory.Delete(state, true);
                }
            }
        }

        [Test]
        public void R11_A_ExplicitDuplicateInTemporaryContentSetStillReportsGpLog002()
        {
            var original = AssetDatabase.LoadAssetAtPath<GameplayContentSet>(Content);
            Assert.That(original, Is.Not.Null);
            var duplicate = ScriptableObject.CreateInstance<GameplayContentSet>();
            string path = "Assets/Hollowmere/Tests/P4_2/EditMode/R11Duplicate-" + Guid.NewGuid().ToString("N") + ".asset";
            try
            {
                duplicate.Configure(original.World, original.Definitions.Concat(new[] { original.Definitions[0] }));
                AssetDatabase.CreateAsset(duplicate, path);
                Assert.That(LogicValidator.ValidateSet(duplicate).Any(d => d.Code == "GP-LOG-002"), Is.True,
                    "idempotent assignment must not weaken the content-set duplicate validator");
            }
            finally
            {
                if (!AssetDatabase.DeleteAsset(path)) UnityEngine.Object.DestroyImmediate(duplicate);
            }
        }

        private static void RefreshStamps(JObject candidate, StudioRuntime runtime)
        {
            // The same production rebase used by Workflows.RebaseAndApply/CandidateCoordinator.Rebase.
            // Only take its operations and base-version witnesses; preserve the retained envelope.
            ToolCatalog catalog = runtime.Registry.Catalog;
            StagedChangeSet staged = runtime.Engine.Stage(StudioJson.Deserialize<ChangeSet>(candidate.ToString()),
                new StageOptions { Mode = ValidationMode.Candidate, ToolCatalogRevision = catalog.Revision ?? catalog.ComputeRevision() });
            try
            {
                if (staged.Operations.Any(op => op.Conflict != null)) staged = runtime.Engine.Rebase(staged);
                candidate["operations"] = StudioJson.ToToken(staged.ChangeSet.Operations);
                candidate["baseVersions"] = StudioJson.ToToken(staged.ChangeSet.BaseVersions!);
            }
            finally
            {
                runtime.Engine.Discard(staged);
            }
        }

        private static JObject WithoutStamps(JObject value)
        {
            var copy = (JObject)value.DeepClone();
            foreach (JProperty stamp in copy.Descendants().OfType<JProperty>().Where(p => p.Name == "stamp").ToArray())
                stamp.Remove();
            return copy;
        }

        private static string Describe(System.Collections.Generic.IReadOnlyList<Diagnostic> diagnostics) =>
            string.Join("\n", diagnostics.Select(d => d.Code + ": " + d.Message));

        private static void RestoreIfChanged(string project, string path, byte[] before)
        {
            string absolute = Path.Combine(project, path);
            if (File.Exists(absolute) && File.ReadAllBytes(absolute).SequenceEqual(before)) return;
            File.WriteAllBytes(absolute, before);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
    }
}

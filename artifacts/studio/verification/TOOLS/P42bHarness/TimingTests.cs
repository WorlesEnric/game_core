#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using GameCore.Contracts;
using GameCore.Gameplay.World;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.Npc;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using GameCore.Unity.App;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace P42b.Acceptance
{
    public sealed class TimingTests
    {
        [SetUp]
        public void RequireEvidenceDirectory()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GAMECORE_P42_EVIDENCE")))
            {
                Assert.Ignore("P4.2 timing acceptance requires an explicit GAMECORE_P42_EVIDENCE directory; run artifacts/studio/verification/TOOLS/rows-p42l.py timing.");
            }
        }

        private static string Output => Environment.GetEnvironmentVariable("GAMECORE_P42_EVIDENCE") ?? throw new InvalidOperationException("Evidence directory required");
        private static double P95(IEnumerable<double> values)
        {
            double[] sorted = values.OrderBy(x => x).ToArray();
            return sorted[(int)Math.Ceiling(sorted.Length * .95) - 1];
        }
        private static JObject Samples(string name, double[] values, double budget)
        {
            return new JObject { ["name"] = name, ["ms"] = new JArray(values), ["count"] = values.Length,
                ["p95Ms"] = P95(values), ["budgetMs"] = budget, ["pass"] = P95(values) <= budget };
        }
        private static void Write(string name, JObject data)
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, name + ".json"), data.ToString());
        }

        [Test]
        public void R2_38_B_SELECT_100PicksAnd500CandidateMarquee()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string state = Path.Combine(Path.GetTempPath(), "p42b-pick-" + Guid.NewGuid().ToString("N"));
            using var runtime = StudioRuntime.Create(new StudioRuntimeOptions { Paths = new StudioPaths(Path.GetDirectoryName(Application.dataPath)!, state, "p42b"), LoadIndexCache = false });
            Camera camera = new GameObject("DatasetCamera").AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 15; camera.aspect = 2;
            camera.transform.position = new Vector3(0, 0, -30);
            var objects = new List<GameObject>();
            try
            {
                for (int i = 0; i < 500; i++)
                {
                    GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name = "Pick-" + i; cube.transform.position = new Vector3(i % 25 - 12, i / 25 - 9.5f, 0);
                    cube.transform.localScale = Vector3.one * .7f; objects.Add(cube);
                }
                EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/P42bHarness/PickingDataset.unity");
                Physics.SyncTransforms();
                var service = new PickingService(camera, new Rect(0, 0, 1200, 600), runtime.Resolver, runtime.Identity,
                    options: new PickOptions { IncludeGroundCandidate = false });
                double[] picks = new double[100]; double[] marquee = new double[100];
                for (int i = 0; i < 100; i++)
                {
                    int target = i * 5;
                    PickResult result = service.Pick(service.FromViewport(camera.WorldToViewportPoint(objects[target].transform.position)));
                    Assert.That(result.Candidates.Count, Is.GreaterThanOrEqualTo(1));
                    picks[i] = result.Timings.TotalMs;
                    PickResult box = service.Marquee(new Rect(0, 0, 1200, 600), true);
                    Assert.That(box.Targets().Count, Is.EqualTo(500), "all 500 unique candidate objects must resolve");
                    marquee[i] = box.Timings.TotalMs;
                }
                Write("selection", new JObject { ["candidateCount"] = 500, ["picks"] = Samples("pick", picks, 16),
                    ["marquee"] = Samples("marquee", marquee, 50), ["unity"] = Application.unityVersion });
                Assert.That(P95(picks), Is.LessThanOrEqualTo(16));
                Assert.That(P95(marquee), Is.LessThanOrEqualTo(50));
            }
            finally
            {
                foreach (GameObject item in objects) Object.DestroyImmediate(item);
                Object.DestroyImmediate(camera.gameObject);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        [Test]
        public void R2_38_B_APPLY_20RealSingleTargetEdits()
        {
            EditorSceneManager.OpenScene("Assets/Hollowmere/Regions/ThornwickVillage.unity");
            GameObject well = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None).First(x => x.name == "Village Well");
            Vector3 original = well.transform.position;
            string state = Path.Combine(Path.GetTempPath(), "p42b-apply-" + Guid.NewGuid().ToString("N"));
            using var runtime = StudioRuntime.Create(new StudioRuntimeOptions { Paths = new StudioPaths(Path.GetDirectoryName(Application.dataPath)!, state, "p42b"), LoadIndexCache = false });
            runtime.Index.Rebuild();
            double[] times = new double[20]; var entries = new JArray();
            try
            {
                for (int i = 0; i < 20; i++)
                {
                    ChangeSet cs = MoveChangeSets.Build(runtime, well, original + Vector3.right * ((i + 1) * .01f))!;
                    var staged = runtime.Engine.Stage(cs);
                    Assert.That(staged.Ok, Is.True, string.Join(";", staged.Diagnostics));
                    Stopwatch timer = Stopwatch.StartNew();
                    var result = runtime.Engine.Apply(staged); timer.Stop();
                    times[i] = timer.Elapsed.TotalMilliseconds;
                    entries.Add(StudioJson.ToToken(result.Entry));
                    Assert.That(result.State, Is.EqualTo(ChangeSetState.Applied));
                    Assert.That(well.transform.position.x, Is.EqualTo(original.x + (i + 1) * .01f).Within(.0001));
                }
                Write("apply", new JObject { ["single"] = Samples("single-target", times, 200), ["journal"] = entries });
                Assert.That(P95(times), Is.LessThanOrEqualTo(200));
            }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
        }

        [Test]
        public void R2_38_B_APPLY_20MarshAllNpcEdits()
        {
            EditorSceneManager.OpenScene("Assets/Hollowmere/Regions/BlackmereMarsh.unity");
            var definitions = AssetDatabase.FindAssets("t:NpcDefinition", new[] { "Assets/Hollowmere" })
                .Select(g => AssetDatabase.LoadAssetAtPath<NpcDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(n => n != null).Select(n => n.Entity).ToArray();
            var targets = Object.FindObjectsByType<AuthoredEntity>(FindObjectsSortMode.None)
                .Where(e => definitions.Contains(e.Definition)).ToArray();
            Assert.That(targets.Length, Is.GreaterThan(0), "real Marsh NPC set required");
            var original = targets.Select(t => t.transform.position).ToArray();
            string state = Path.Combine(Path.GetTempPath(), "p42b-region-" + Guid.NewGuid().ToString("N"));
            using var runtime = StudioRuntime.Create(new StudioRuntimeOptions { Paths = new StudioPaths(Path.GetDirectoryName(Application.dataPath)!, state, "p42b"), LoadIndexCache = false });
            runtime.Index.Rebuild();
            double[] times = new double[20]; var entries = new JArray();
            try
            {
                for (int i = 0; i < 20; i++)
                {
                    var ops = new List<Operation>();
                    for (int n = 0; n < targets.Length; n++)
                    {
                        Operation op = MoveChangeSets.Build(runtime, targets[n].gameObject, original[n] + Vector3.right * ((i + 1) * .01f))!.Operations[0];
                        ops.Add(new Operation("npc" + n, op.Tool, op.Target, op.Args));
                    }
                    var cs = new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId, new Intent("Marsh all NPC timing", IntentOrigin.Manual), ops);
                    var staged = runtime.Engine.Stage(cs);
                    Assert.That(staged.Ok, Is.True, string.Join(";", staged.AllDiagnostics));
                    Stopwatch timer = Stopwatch.StartNew();
                    var applied = runtime.Engine.Apply(staged); timer.Stop(); times[i] = timer.Elapsed.TotalMilliseconds;
                    entries.Add(StudioJson.ToToken(applied.Entry));
                    Assert.That(applied.State, Is.EqualTo(ChangeSetState.Applied));
                }
                Write("region-apply", new JObject { ["targets"] = new JArray(targets.Select(t => t.name)),
                    ["region"] = Samples("Marsh-all-NPC", times, 1000), ["journal"] = entries });
                Assert.That(P95(times), Is.LessThanOrEqualTo(1000));
            }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
        }

        [UnityTest]
        [Timeout(240000)]
        public IEnumerator R2_38_B_COMPOSE_20PreparesInReferenceWorld()
        {
            EditorSceneManager.OpenScene("Assets/Hollowmere/Boot/Boot.unity");
            yield return new EnterPlayMode();
            DateTime deadline = DateTime.UtcNow.AddSeconds(90);
            while ((Object.FindAnyObjectByType<Hollowmere.Game.HollowmereGame>()?.World?.Streamer.IsSettled != true) && DateTime.UtcNow < deadline) yield return null;
            var world = Object.FindAnyObjectByType<Hollowmere.Game.HollowmereGame>()?.World;
            Assert.That(world, Is.Not.Null);
            var root = world!.Root;
            Assert.That(world.Entities.RecordCount, Is.InRange(1, 1500));
            double[] samples = new double[20]; var receipts = new JArray();
            root.Pause();
            for (int i = 0; i < 20; i++)
            {
                ScopeId scope = new ScopeId(StableNameKeyDerivation.Derive("p42b-scope-" + i));
                var payload = WorldBuilder.ScopeCreate(scope, WorldBuilder.RootScopeOf(world.Manifest.WorldId));
                OperationId operation = root.NextOperation();
                Stopwatch timer = Stopwatch.StartNew();
                var admitted = root.Lane.SubmitEdit(payload, operation, root.Lane.Committed.Revision);
                timer.Stop(); samples[i] = timer.Elapsed.TotalMilliseconds;
                receipts.Add(new JObject { ["staged"] = admitted.Staged, ["code"] = admitted.Code.ToString(), ["ms"] = samples[i] });
                Assert.That(admitted.Staged, Is.True, admitted.Code.ToString());
                // Cancel prepared work: this isolates preparation without publishing an unsynchronized world image.
                root.Lane.Cancel(root.NextOperation(), operation);
            }
            Write("compose", new JObject { ["prepare"] = Samples("kernel-prepare", samples, 300), ["receipts"] = receipts,
                ["targetCount"] = world.Entities.RecordCount,
                ["measurement"] = "CompositionHost.SubmitEdit on the real paused Hollowmere world; each prepared scope edit is cancelled before publication." });
            SessionState.SetFloat("P42b.compose.p95", (float)P95(samples));
            yield return new ExitPlayMode();
            Assert.That(SessionState.GetFloat("P42b.compose.p95", float.MaxValue), Is.LessThanOrEqualTo(300));
        }
    }
}

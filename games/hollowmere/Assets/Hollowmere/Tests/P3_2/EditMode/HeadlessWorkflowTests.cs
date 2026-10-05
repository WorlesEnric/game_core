// Hollowmere.P3_2.Headless - the headless variants of the P3.2 workflows (studio/tools/workflow-p3.2-harness.sh): the
// Studio runtime (temporary state root: the journal and artifacts never touch the project's Studio/ folder) and P2.2's
// EtosAgentGateway against the REAL node, no window. Ignored unless GAMECORE_ETOS_LIVE=1; the key file comes from
// GAMECORE_ETOS_KEY_FILE and is never printed. Evidence (redacted, checked for the key) goes to GCS_P32_OUT.
//   H1 typed edit on a selection: Maren + Village Well, "Move Maren two metres north." -> staged candidate -> apply ->
//      journal -> undo (scene restored) -> B-AGENT-UX timeline;
//   H2 media: generate.image with max_cost_usd 0.25 -> verified asset.import -> assign Lantern.icon -> undo both; tts line
//      -> verified import -> undo; generate.image with max_cost_usd 0.001 (whatever the node answers is recorded);
//   H3 cancel: a request cancelled while its task runs; the acknowledgement time and the final state.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameCore.Studio.Authoring;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hollowmere.P3_2.Headless
{
    [Category("Live")]
    public sealed class HeadlessWorkflowTests
    {
        private const string Village = "Assets/Hollowmere/Regions/ThornwickVillage.unity";
        private const string Folder = "Assets/Hollowmere/Generated/P3_2/Headless";
        private const string LanternItem = "Assets/Hollowmere/Items/Lantern.asset";

        private Harness? _h;

        private Harness H => _h!;

        [SetUp]
        public void SetUp()
        {
            if (Environment.GetEnvironmentVariable("GAMECORE_ETOS_LIVE") != "1")
            {
                Assert.Ignore("P3.2 headless workflows run only with GAMECORE_ETOS_LIVE=1 (studio/tools/workflow-p3.2-harness.sh)");
            }

            EditorSceneManager.OpenScene(Village, OpenSceneMode.Single);
            _h = new Harness();
        }

        [TearDown]
        public void TearDown()
        {
            if (_h != null)
            {
                Evidence("log-" + TestContext.CurrentContext.Test.MethodName, new JObject { ["log"] = new JArray(_h.Log.Recent.Select(e => e.ToString()).ToArray()) });
                _h.Dispose();
                _h = null;
            }

            if (AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.DeleteAsset(Folder);
            }
        }

        [UnityTest]
        [Timeout(1500000)]
        public IEnumerator H1_TypedEdit_OnASelection()
        {
            GameObject maren = GameObject.Find("Maren") ?? throw new InvalidOperationException("no Maren");
            GameObject well = GameObject.Find("Village Well") ?? throw new InvalidOperationException("no Village Well");
            Vector3 before = maren.transform.position;
            List<JObject> timeline = new List<JObject>();
            CandidateImport? import = null;
            string id = IdDerivation.NewChangeSetId();
            long submitted = 0;
            H.Gateway.RequestChanged += view =>
            {
                if (view.RequestId == id)
                {
                    long now = Now();
                    timeline.Add(new JObject { ["recvMs"] = now, ["fromSubmitMs"] = now - submitted, ["state"] = view.State, ["taskStatus"] = view.TaskStatus, ["local"] = view.LocalState, ["updatedAt"] = view.UpdatedAt, ["visibleLagMs"] = now - view.UpdatedAt, ["tasks"] = new JArray(view.Tasks.ToArray()) });
                }
            };
            H.Gateway.CandidateStaged += result =>
            {
                if (result.RequestId == id)
                {
                    import = result;
                }
            };
            H.Gateway.Start();
            yield return H.Await(H.Gateway.RefreshStatusAsync(), 60, "hello");

            AgentRequest request = GameCore.Studio.Etos.AgentRequestBuilder.ForObjects(H.Runtime, new UnityEngine.Object[] { maren, well }, "Move Maren two metres north.");
            request.ChangeSetId = id;
            submitted = Now();
            Task<string> submit = H.Gateway.SubmitAsync(request, CancellationToken.None);
            yield return H.Await(submit, 120, "the submit");
            long accepted = Now();
            yield return H.Until(() => import != null || Settled(H.Gateway.Requests.FirstOrDefault(r => r.RequestId == id)), 1200, "a candidate or a settled request", fail: false);
            long candidateAt = Now();
            RequestView? view = H.Gateway.Requests.FirstOrDefault(r => r.RequestId == id);
            JObject evidence = new JObject
            {
                ["changeSetId"] = id,
                ["intent"] = request.Intent,
                ["selection"] = StudioJson.ToToken(request.Selection),
                ["attachments"] = new JArray(request.Attachments.Select(a => a.Name).ToArray()),
                ["submitToAcceptedMs"] = accepted - submitted,
                ["submitToCandidateMs"] = import != null ? candidateAt - submitted : (long?)null,
                ["timeline"] = new JArray(timeline.ToArray()),
                ["final"] = view == null ? null : new JObject { ["state"] = view.State, ["tasks"] = new JArray(view.Tasks.ToArray()), ["outcome"] = view.Outcome?.DeepClone() },
            };
            if (import?.Staged != null)
            {
                evidence["importMs"] = Math.Round(import.Milliseconds, 1);
                evidence["candidate"] = StudioJson.ToToken(import.Staged.ChangeSet);
                evidence["stagedOk"] = import.Staged.Ok;
                ApplyReport report = H.Gateway.Apply(id);
                evidence["apply"] = new JObject { ["state"] = report.State.ToString(), ["ms"] = Math.Round(report.Milliseconds, 1), ["outcomes"] = new JArray(report.Outcomes.Select(o => StudioJson.ToToken(o)).ToArray()) };
                evidence["positionApplied"] = Vec(maren.transform.position);
                evidence["journalApplied"] = H.Runtime.Journal.Read(id)?.EffectiveState.ToString();
                Stopwatch undo = Stopwatch.StartNew();
                HistoryResult undone = H.Runtime.History.Undo(id);
                evidence["undo"] = new JObject { ["ok"] = undone.Ok, ["state"] = undone.State?.ToString(), ["ms"] = Math.Round(undo.Elapsed.TotalMilliseconds, 1) };
                evidence["positionUndone"] = Vec(maren.transform.position);
            }

            evidence["positionBefore"] = Vec(before);
            Evidence("h1-typed-edit", evidence);
            Assert.That(import, Is.Not.Null, "no candidate: " + (view == null ? "no request view" : view.State + " " + view.Outcome?.ToString(Formatting.None)));
            Assert.That(Vector3.Distance(maren.transform.position, before), Is.LessThan(0.001f), "undo restores the position");
        }

        [UnityTest]
        [Timeout(1200000)]
        public IEnumerator H2_Media_GenerateImportAssign()
        {
            yield return H.Await(H.Gateway.RefreshStatusAsync(), 60, "hello");
            EtosMediaGenerator media = new EtosMediaGenerator(H.Gateway, H.Runtime, H.Queue);
            Stopwatch watch = Stopwatch.StartNew();
            string iconPath = Folder + "/lantern_icon_headless.png";
            Task<MediaImport> icon = media.GenerateImageAsync("A small inventory icon of an old brass marsh lantern, hand-painted game UI style, plain background.", iconPath, 256, 0.25, null);
            yield return H.Await(icon, 420, "the image");
            JObject image = Media(icon.Result, watch.ElapsedMilliseconds, iconPath, 0.25);

            JObject assign = new JObject();
            if (icon.Result.Ok)
            {
                UnityEngine.Object item = AssetDatabase.LoadMainAssetAtPath(LanternItem);
                AuthoringRef target = H.Runtime.Resolver.BuildRef(item, null, true)!;
                ArtifactRef source = icon.Result.Artifact!;
                string spritePath = Folder + "/lantern_icon_headless_sprite.png";
                Operation bind = new Operation("op1", "bind", target, new JObject
                {
                    ["field"] = "icon",
                    ["artifact"] = new JObject { ["artifact"] = "sha256:" + source.Sha256 },
                    ["path"] = spritePath,
                    ["importer"] = new JObject { ["textureType"] = "Sprite", ["spriteImportMode"] = "Single" },
                });
                ChangeSet set = new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId, new Intent("Bind the generated lantern icon (headless)", IntentOrigin.Manual), new[] { bind },
                    artifacts: new[] { new ArtifactRef(source.Sha256, source.MediaType, source.Bytes, "lantern_icon_headless_sprite.png", null, "source") });
                ApplyReport report = H.Runtime.Engine.Apply(set);
                assign["state"] = report.State.ToString();
                assign["ms"] = Math.Round(report.Milliseconds, 1);
                assign["outcomes"] = new JArray(report.Outcomes.Select(o => StudioJson.ToToken(o)).ToArray());
                assign["diagnostics"] = new JArray(report.Diagnostics.Select(d => StudioJson.ToToken(d)).ToArray());
                assign["textureType"] = (AssetImporter.GetAtPath(iconPath) as TextureImporter)?.textureType.ToString();
                if (report.Ok)
                {
                    assign["undo"] = H.Runtime.History.Undo(set.Id).Ok;
                }

                assign["undoImport"] = H.Runtime.History.Undo(icon.Result.Report!.Entry.Id).Ok;
                assign["fileAfterUndo"] = File.Exists(Path.Combine(Harness.ProjectRoot, iconPath));
            }

            watch.Restart();
            string wavPath = Folder + "/maren_line_headless.wav";
            Task<MediaImport> speech = media.GenerateSpeechAsync("The bell has been silent for three winters.", wavPath, null, 0.10, null);
            yield return H.Await(speech, 300, "tts");
            JObject tts = Media(speech.Result, watch.ElapsedMilliseconds, wavPath, 0.10);
            if (speech.Result.Report != null && speech.Result.Report.Ok)
            {
                tts["undo"] = H.Runtime.History.Undo(speech.Result.Report.Entry.Id).Ok;
            }

            watch.Restart();
            Task<OpResult> budget = H.Gateway.GenerateAsync(new OpRequest("image", new JObject { ["prompt"] = "A flat green square swatch." }, 0.001), CancellationToken.None);
            yield return H.Await(budget, 420, "the 0.001 image");
            JObject ceiling = new JObject
            {
                ["maxCostUsd"] = 0.001,
                ["ms"] = watch.ElapsedMilliseconds,
                ["succeeded"] = budget.Result.Succeeded,
                ["sha256"] = budget.Result.Sha256,
                ["refusal"] = budget.Result.Refusal == null ? null : StudioJson.ToToken(budget.Result.Refusal),
                ["opState"] = budget.Result.State?.DeepClone(),
            };
            Evidence("h2-media", new JObject { ["image"] = image, ["assign"] = assign, ["tts"] = tts, ["budget"] = ceiling });
            Assert.That(icon.Result.Ok, Is.True, "image: " + icon.Result.Problem?.Code + " " + icon.Result.Problem?.Message);
            Assert.That(speech.Result.Ok, Is.True, "tts: " + speech.Result.Problem?.Code + " " + speech.Result.Problem?.Message);
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator H3_Cancel_WhileRunning()
        {
            GameObject pip = GameObject.Find("Pip") ?? throw new InvalidOperationException("no Pip");
            string id = IdDerivation.NewChangeSetId();
            List<JObject> timeline = new List<JObject>();
            H.Gateway.RequestChanged += view =>
            {
                if (view.RequestId == id)
                {
                    timeline.Add(new JObject { ["recvMs"] = Now(), ["state"] = view.State, ["taskStatus"] = view.TaskStatus, ["updatedAt"] = view.UpdatedAt });
                }
            };
            H.Gateway.Start();
            yield return H.Await(H.Gateway.RefreshStatusAsync(), 60, "hello");
            AgentRequest request = GameCore.Studio.Etos.AgentRequestBuilder.ForObjects(H.Runtime, new UnityEngine.Object[] { pip }, "Give Pip a patrol around the village square, four points about three metres apart.");
            request.ChangeSetId = id;
            Task<string> submit = H.Gateway.SubmitAsync(request, CancellationToken.None);
            yield return H.Await(submit, 120, "the submit");
            yield return H.Until(() => H.Gateway.Requests.FirstOrDefault(r => r.RequestId == id)?.TaskStatus == "running", 300, "a running task", fail: false);
            long cancelAt = Now();
            Task cancel = H.Gateway.CancelAsync(id, CancellationToken.None);
            yield return H.Await(cancel, 60, "the cancel");
            long cancelReturned = Now();
            yield return H.Until(() => H.Gateway.Requests.FirstOrDefault(r => r.RequestId == id)?.State == "cancelled" || Settled(H.Gateway.Requests.FirstOrDefault(r => r.RequestId == id)), 120, "cancelled", fail: false);
            long settled = Now();
            RequestView? view = H.Gateway.Requests.FirstOrDefault(r => r.RequestId == id);
            Evidence("h3-cancel", new JObject
            {
                ["changeSetId"] = id,
                ["cancelCallMs"] = cancelReturned - cancelAt,
                ["cancelToStateMs"] = settled - cancelAt,
                ["final"] = view == null ? null : new JObject { ["state"] = view.State, ["taskStatus"] = view.TaskStatus, ["tasks"] = new JArray(view.Tasks.ToArray()), ["outcome"] = view.Outcome?.DeepClone(), ["hasCandidate"] = view.HasCandidate },
                ["staged"] = H.Gateway.Staged.ContainsKey(id),
                ["timeline"] = new JArray(timeline.ToArray()),
            });
            Assert.That(view?.State, Is.EqualTo("cancelled"));
        }

        // -------------------------------------------------------------------------------------------- helpers

        private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private static JArray Vec(Vector3 v) => new JArray(Math.Round(v.x, 3), Math.Round(v.y, 3), Math.Round(v.z, 3));

        private static bool Settled(RequestView? view)
        {
            return view != null && (view.State == "failed" || view.State == "cancelled" || view.State == "needs_clarification" || view.State == "candidate_invalid" || view.State == "unresolved" || view.LocalState == "import_failed");
        }

        private static JObject Media(MediaImport import, long ms, string path, double maxCost)
        {
            return new JObject
            {
                ["ok"] = import.Ok,
                ["ms"] = ms,
                ["maxCostUsd"] = maxCost,
                ["provider"] = import.Result.Provider,
                ["providerSha256"] = import.Result.Sha256,
                ["importedSha256"] = import.Artifact?.Sha256,
                ["path"] = path,
                ["journal"] = import.Report?.Entry.Id,
                ["journalState"] = import.Report?.State.ToString(),
                ["problem"] = import.Problem == null ? null : StudioJson.ToToken(import.Problem),
                ["opState"] = import.Result.State?.DeepClone(),
            };
        }

        private void Evidence(string name, JObject content)
        {
            string? dir = Environment.GetEnvironmentVariable("GCS_P32_OUT");
            if (string.IsNullOrEmpty(dir) || _h == null)
            {
                return;
            }

            string text = EtosRedaction.Redact(content.ToString(Formatting.Indented));
            Assert.That(_h.Credentials.AppearsIn(text), Is.False, "evidence must never carry the key");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "headless-" + name + ".json"), text + "\n");
        }

        /// <summary>A Studio runtime with a temporary state root and the live gateway (P2.2's harness, reduced).</summary>
        private sealed class Harness : IDisposable
        {
            public Harness()
            {
                StateRoot = Path.Combine(Path.GetTempPath(), "gcstudio-p32-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(StateRoot);
                Log = new MemoryStudioLog();
                Runtime = StudioRuntime.Create(new StudioRuntimeOptions
                {
                    Paths = new StudioPaths(ProjectRoot, StateRoot, "p32-headless"),
                    Log = Log,
                    SearchFolders = new[] { "Assets/Hollowmere" },
                    IndexScope = AuthoringSourceScope.All,
                    LoadIndexCache = false,
                });
                string keyFile = Environment.GetEnvironmentVariable(EtosCredentials.KeyFileVariable) ?? EtosCredentials.DefaultKeyFile() ?? string.Empty;
                Credentials = EtosCredentials.FromKeyFile(keyFile);
                Client = new CompanionClient(new EtosClientOptions { NodeUrl = Credentials.NodeUrl ?? "http://127.0.0.1:7410", DefaultMaxCostUsd = 0.50, Log = line => Log.Write(StudioLogLevel.Debug, "etos.client", EtosRedaction.Redact(line)) }, Credentials);
                Queue = new MainThreadQueue(Log);
                Gateway = new EtosAgentGateway(Client, Runtime, Queue, new MemoryCursorStore(), new EtosGatewayOptions { Backoff = new BackoffPolicy(TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(3)), GeneratedFolder = Folder }, Log);
                Runtime.Services.AgentGateway = Gateway;
                Runtime.Index.Rebuild();
            }

            public static string ProjectRoot => Directory.GetParent(Application.dataPath)!.FullName;

            public string StateRoot { get; }

            public MemoryStudioLog Log { get; }

            public StudioRuntime Runtime { get; }

            public EtosCredentials Credentials { get; }

            public CompanionClient Client { get; }

            public MainThreadQueue Queue { get; }

            public EtosAgentGateway Gateway { get; }

            public IEnumerator Await(Task task, double seconds, string what)
            {
                DateTime end = DateTime.UtcNow.AddSeconds(seconds);
                while (!task.IsCompleted)
                {
                    Queue.Pump();
                    if (DateTime.UtcNow > end)
                    {
                        Assert.Fail("timed out after " + seconds + " s waiting for " + what);
                    }

                    yield return null;
                }

                Queue.Pump();
            }

            public IEnumerator Until(Func<bool> condition, double seconds, string what, bool fail = true)
            {
                DateTime end = DateTime.UtcNow.AddSeconds(seconds);
                while (true)
                {
                    Queue.Pump();
                    if (condition())
                    {
                        yield break;
                    }

                    if (DateTime.UtcNow > end)
                    {
                        if (fail)
                        {
                            Assert.Fail("timed out after " + seconds + " s waiting for " + what);
                        }

                        yield break;
                    }

                    yield return null;
                }
            }

            public void Dispose()
            {
                Gateway.Dispose();
                Client.Dispose();
                Runtime.Dispose();
                try
                {
                    Directory.Delete(StateRoot, true);
                }
                catch (IOException)
                {
                    // reclaimed later
                }
            }
        }
    }
}

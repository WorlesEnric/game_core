#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Dialogue;
using GameCore.Gameplay.Npc;
using GameCore.Gameplay.World;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Model;
using GameCore.Studio.UI;
using Hollowmere.Boot;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using RegionResidency = GameCore.Gameplay.Contracts.RegionResidency;
using AuthorScope = GameCore.Studio.Model.AuthorScope;
using SelectionMode = GameCore.Studio.Model.SelectionMode;

namespace Hollowmere.R8_A
{
    // No replacement runtime, publisher, stage service, scene loader or query response is installed.
    // Reflection observes pending native scene IO and permits the same driver to compile before R8-A.
    [InitializeOnLoad]
    public sealed class Acceptance : ScriptableSingleton<Acceptance>
    {
        private const string StateKey = "Hollowmere.R8_A.Acceptance";
        [NonSerialized] private bool busy;
        [NonSerialized] private CandidateEntry? candidate;
        [NonSerialized] private Task<Diagnostic?>? stagePoll;
        [NonSerialized] private EditorWindow? stageWindow;
        private static StudioUiContext Context => StudioUiSession.Context;
        private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        private static string Project => Directory.GetParent(Application.dataPath)!.FullName;
        private static JObject State => JObject.Parse(SessionState.GetString(StateKey, "{}"));
        private static JObject Config => JObject.Parse(File.ReadAllText(Argument("-gcR8AConfig")));
        private static string Argument(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, flag);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : string.Empty;
        }
        static Acceptance() { EditorApplication.update += Tick; }
        private static void Tick() { if ((string?)State["phase"] != null) instance.Poll(); }
        private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        private static void Save(JObject state) => SessionState.SetString(StateKey, state.ToString(Formatting.None));
        private static void Evidence(string name, JToken value)
        {
            string path = Path.Combine((string)Config["evidence"]!, name);
            StudioPaths.WriteAllTextAtomic(path, new SecretRedactor().Redact(value.ToString(Formatting.Indented)) + "\n");
        }

        public static void Run()
        {
            try
            {
                Require(Application.isBatchMode, "R8-A is batch Editor only");
                Require(!File.Exists(EtosSettings.PathFor(Project)), "Existing project ETOS settings must not be read or overwritten");
                Require((string)Config["projectId"]! == EtosProjectContext.LoadProjectId(Project), "Scratch project identity mismatch");
                Require(!Directory.Exists(Path.Combine(Project, "Packages/com.hollowmere.mechanism.pressureplate")), "Existing admitted package must not be changed");
                string pending = StageAdmission.Of(StudioServices.Runtime).StateRoot;
                Require(!Directory.Exists(pending) || Directory.GetFiles(pending, "pending-*.json").Length == 0, "Existing pending admission must be resolved by its owner");
                bool cancellation = Argument("-gcR8APhase") == "cancel";
                string witness = cancellation ? "cancel-editor-started.json" : "editor-started.json";
                Require(!File.Exists(Path.Combine((string)Config["evidence"]!, witness)), "Use fresh evidence; tasks are never silently repeated");
                JObject state = new JObject { ["phase"] = cancellation ? "stage" : "query", ["startedMs"] = Now };
                Save(state);
                Evidence(witness, new JObject { ["isBatchMode"] = Application.isBatchMode,
                    ["projectId"] = EtosProjectContext.LoadProjectId(Project), ["sourceRevision"] = EtosProjectContext.SourceRevision(Project) });
                EditorSceneManager.OpenScene("Assets/Hollowmere/Regions/ThornwickVillage.unity");
            }
            catch (Exception error) { Fail(error); }
        }

        private async void Poll()
        {
            if (busy || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            busy = true;
            try
            {
                JObject state = State;
                Require(Now - (long)state["startedMs"]! < 900000, "R8-A acceptance deadline; partial receipts remain retained");
                string phase = (string)state["phase"]!;
                if (phase == "query")
                {
                    Require(EtosStudioSession.EnsureStarted(), "Production ETOS session unavailable");
                    EtosAgentGateway gateway = EtosStudioSession.Gateway!;
                    Require(gateway.Client.Options.NodeUrl.TrimEnd('/') == ((string)Config["nodeUrl"]!).TrimEnd('/'), "Refusing non-scratch node");
                    await gateway.Client.HelloAsync();
                    Context.Runtime.Index.Rebuild();
                    // This is only a drain of production lifecycle work, never PostIndexDeltaAsync in the harness.
                    object? publisher = gateway.GetType().GetProperty("IndexPublication")?.GetValue(gateway);
                    if (publisher != null)
                    {
                        MethodInfo flush = publisher.GetType().GetMethod("FlushAsync")!;
                        await (Task)flush.Invoke(publisher, new object[] { CancellationToken.None })!;
                    }
                    var npc = AssetDatabase.LoadAssetAtPath<NpcDefinition>("Assets/Hollowmere/Npcs/Definitions/Bram.asset");
                    Require(npc != null && npc.Dialogue is DialogueGraphDefinition, "Real Bram dialogue is missing");
                    var graph = (DialogueGraphDefinition)npc.Dialogue!;
                    AuthoringRef selected = Context.Runtime.Resolver.BuildRef(npc, AuthorScope.Definition, true)!;
                    Require(selected != null, "Bram definition cannot be selected");
                    Context.Selection.Set(new[] { selected });
                    var spec = new JObject { ["graphAssetGuid"] = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(graph)),
                        ["graphAuthoringId"] = graph.AuthoringId, ["owner"] = ContentStamp.Sha256Hex(Encoding.UTF8.GetBytes(
                            new JArray(gateway.Client.Options.AppName, gateway.Client.Options.ProjectId).ToString(Formatting.None))) };
                    Evidence("selection.json", new JObject { ["selection"] = StudioJson.ToToken(Context.Selection.Capture(SelectionMode.Edit)),
                        ["spec"] = spec, ["graphPath"] = AssetDatabase.GetAssetPath(graph),
                        ["nodes"] = JArray.FromObject(graph.Nodes.Select((node, index) => new { index, node.text })) });
                    string intent = "Read-only selected Bram dialogue query. Run actual worker-side etos query against gc_definition to discover graph, "
                        + "then selected-graph and unfiltered gc_dialogue_node. No index-slice substituted for results, no RG writes, no media. "
                        + "R8A_QUERY_SPEC=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(spec.ToString(Formatting.None)));
                    PreparedRequest request = Context.Requests.Build(intent, Context.Selection.Capture(SelectionMode.Edit));
                    state["requestId"] = request.ChangeSetId;
                    state["phase"] = "query-result";
                    Save(state);
                    Evidence("query-request.json", new JObject { ["requestId"] = request.ChangeSetId,
                        ["request"] = JObject.FromObject(request.Request), ["indexRevision"] = Context.Runtime.Index.Revision });
                    PromptSubmission submitted = await Context.Submit(request);
                    Require(submitted.Accepted, "Worker request refused: " + submitted.Refusal?.Message);
                }
                else if (phase == "query-result")
                {
                    RequestInfo request = await EtosStudioSession.Gateway!.Client.GetRequestAsync((string)state["requestId"]!);
                    Evidence("query-request-state.json", request.Raw);
                    string status = (string?)request.Raw["state"] ?? string.Empty;
                    if (status == "requested" || status == "running" || status == "waiting") return;
                    Require(status == "needs_clarification", "Real worker query did not complete: " + request.Raw);
                    string question = (string?)request.Raw["outcome"]?["question"] ?? string.Empty;
                    const string begin = "R8A_QUERY_BEGIN", end = "R8A_QUERY_END";
                    int first = question.IndexOf(begin, StringComparison.Ordinal), last = question.IndexOf(end, StringComparison.Ordinal);
                    Require(first >= 0 && last > first, "Worker did not return its authentic query receipt");
                    JObject receipt = JObject.Parse(question.Substring(first + begin.Length, last - first - begin.Length));
                    Evidence("worker-query-receipt.json", receipt);
                    Require((bool?)receipt["pass"] == true, "Worker queries did not return real rows");
                    JObject selected = JObject.Parse(File.ReadAllText(Path.Combine((string)Config["evidence"]!, "selection.json")));
                    var rows = (JArray)receipt["selected"]!["rows"]!;
                    foreach (JToken node in (JArray)selected["nodes"]!)
                        Require(rows.Any(row => (int?)row["node_index"] == (int?)node["index"] && (string?)row["text"] == (string?)node["text"]),
                            "Returned graph rows do not match the real serialized dialogue node " + node["index"]);
                    Require(rows.Count == ((JArray)selected["nodes"]!).Count, "Selected query has duplicated, stale or missing nodes");
                    Require(((JArray)receipt["all"]!["rows"]!).Any(row => (string?)row["graph"] == (string?)receipt["graph"]), "Unfiltered query omitted selected graph");
                    ChangeSet retained = StageAdmission.Of(Context.Runtime).RetainCandidate((string)Config["candidate"]!);
                    StageCandidateRequest stageRequest = StageAdmission.Of(Context.Runtime).BuildStageRequest(retained, Project);
                    Evidence("stage-context.json", new JObject { ["request"] = JObject.FromObject(stageRequest),
                        ["changeSet"] = StudioJson.ToToken(retained), ["toolCatalog"] = StudioJson.ToToken(Context.Runtime.Registry.Catalog) });
                    state["phase"] = "region-loading"; Save(state);
                    EditorSceneManager.OpenScene("Assets/Hollowmere/Boot/Boot.unity");
                    EditorApplication.isPlaying = true;
                }
                else if (phase == "stage")
                {
                    Require(EtosStudioSession.EnsureStarted(), "Production ETOS session unavailable");
                    JObject queued = JObject.Parse(File.ReadAllText(Path.Combine((string)Config["evidence"]!, "stage-queued.json")));
                    JObject context = JObject.Parse(File.ReadAllText(Path.Combine((string)Config["evidence"]!, "stage-context.json")));
                    ChangeSet retained = StageAdmission.Of(Context.Runtime).RetainCandidate((string)Config["candidate"]!);
                    // Restore the actual authenticated job receipt through the coordinator's ordinary recovery seam.
                    string key = "GameCore.Studio.UI.Stage." + ContentStamp.Sha256Hex(Encoding.UTF8.GetBytes(Project)) + "." + retained.Id;
                    SessionState.SetString(key, new JObject { ["jobId"] = queued["jobId"], ["request"] = context["request"] }.ToString(Formatting.None));
                    candidate = Context.Candidates.Add(retained.Id, retained, Context.Runtime.Registry.Catalog.Revision);
                    state["changeSetId"] = retained.Id; state["phase"] = "stage-running"; Save(state);
                    stagePoll = Context.Candidates.RefreshStage(candidate);
                }
                else if (phase == "stage-running")
                {
                    Require(candidate != null && stagePoll != null, "Unexpected reload before stage cancellation");
                    if (candidate.StageJobId == null)
                    {
                        if (stagePoll.IsCompleted) throw new InvalidOperationException("Stage request failed: " + (await stagePoll)?.Message);
                        return;
                    }
                    StageJobInfo job = await EtosStudioSession.Gateway!.Client.GetStageAsync(candidate.StageJobId);
                    Evidence("stage-running.json", job.Raw);
                    string running = Path.Combine((string)Config["evidence"]!, "container-running.json");
                    if (!File.Exists(running))
                    {
                        Require(job.State == "queued" || job.State == "running", "Stage ended before real Docker witness: " + job.Raw);
                        return;
                    }
                    Require(job.State == "running", "Cancellation requires an actually running stage");
                    await ProbeForeign(job.JobId);
                    Type? windowType = typeof(StudioUiContext).Assembly.GetType("GameCore.Studio.UI.StudioStageWindow");
                    Require(windowType != null, "Stage cancellation panel missing (before-fix regression)");
                    stageWindow = EditorWindow.GetWindow(windowType!); stageWindow.Show();
                    state["jobId"] = job.JobId; state["phase"] = "stage-click"; Save(state);
                }
                else if (phase == "stage-click")
                {
                    Require(stageWindow != null && candidate != null, "Stage window was lost");
                    Button? cancel = stageWindow.rootVisualElement.Q<Button>("stage-cancel-" + candidate.Id);
                    Button? admit = stageWindow.rootVisualElement.Q<Button>("stage-admit-" + candidate.Id);
                    if (cancel?.panel == null) return;
                    Require(cancel.enabledInHierarchy && admit != null && !admit.enabledInHierarchy, "Running Cancel must be enabled and Admit disabled");
                    Evidence("stage-ui-before.json", new JObject { ["jobId"] = state["jobId"], ["cancelAttached"] = true,
                        ["cancelEnabled"] = true, ["admitEnabled"] = false, ["input"] = "NavigationSubmitEvent on the real Stage panel Button" });
                    state["phase"] = "stage-cancelled"; Save(state);
                    using (NavigationSubmitEvent input = NavigationSubmitEvent.GetPooled()) { input.target = cancel; cancel.SendEvent(input); }
                }
                else if (phase == "stage-cancelled")
                {
                    StageJobInfo job = await EtosStudioSession.Gateway!.Client.GetStageAsync((string)state["jobId"]!);
                    if (job.State == "running" || job.State == "cancelling") return;
                    Require(job.State == "cancelled", "Stage cancel did not produce cancelled: " + job.Raw);
                    if (!stagePoll!.IsCompleted) return;
                    await stagePoll;
                    Button? admit = stageWindow!.rootVisualElement.Q<Button>("stage-admit-" + candidate!.Id);
                    Require(admit != null && !admit.enabledInHierarchy && !Context.Candidates.CanAdmit(candidate), "Cancelled stage enabled Admit");
                    try { await EtosStudioSession.Gateway.Client.FetchTrustedVerdictAsync(job.JobId); throw new InvalidOperationException("Cancelled job issued a verdict"); }
                    catch (EtosException refusal) { Require(refusal.Error.Status == 404, "Verdict refusal must be 404"); }
                    Evidence("stage-cancelled.json", job.Raw);
                    Evidence("stage-ui-after.json", new JObject { ["jobId"] = job.JobId, ["admitEnabled"] = false,
                        ["verifiedVerdict"] = candidate.VerifiedVerdict != null, ["verdictHttpStatus"] = 404,
                        ["journal"] = StudioJson.ToToken(Context.Runtime.Journal.Read(candidate.Id)) });
                    Evidence("cancel-editor-result.json", new JObject { ["status"] = "PASS", ["stageJobId"] = job.JobId,
                        ["stageCancellation"] = true, ["visualVerification"] = false });
                    stageWindow.Close();
                    SessionState.EraseString(StateKey); EditorApplication.Exit(0);
                }
                else if (phase == "region-loading")
                {
                    if (!EditorApplication.isPlaying) return;
                    GameBoot? boot = UnityEngine.Object.FindAnyObjectByType<GameBoot>();
                    if (boot?.World == null) return;
                    AsyncOperation? native = PendingNativeLoad(boot.World.Streamer, out string region);
                    if (native == null) return;
                    Evidence("region-cancel-before.json", new JObject { ["region"] = region, ["nativeIsDone"] = native.isDone,
                        ["nativeProgress"] = native.progress, ["loadsStarted"] = boot.World.Streamer.LoadsStarted,
                        ["loader"] = boot.World.Streamer.Loader.GetType().FullName, ["frame"] = Time.frameCount });
                    boot.World.Streamer.Cancel();
                    state["phase"] = "region-cancelled"; Save(state);
                }
                else if (phase == "region-cancelled")
                {
                    GameBoot? boot = UnityEngine.Object.FindAnyObjectByType<GameBoot>();
                    Require(boot?.World != null, "Production game stopped during region cancellation");
                    RegionStreamer streamer = boot!.World!.Streamer;
                    if (!streamer.IsSettled) return;
                    bool unloaded = streamer.Regions.All(region => streamer.ResidencyOf(region.AuthoringId) == RegionResidency.Unloaded
                        && !SceneManager.GetSceneByPath(region.ScenePath).isLoaded);
                    Require(streamer.IsCancelled && unloaded && streamer.UnloadsStarted > 0 && streamer.LoadFailures == 0,
                        "Cancelled load did not complete real scene teardown and committed Unloaded residency");
                    Evidence("region-cancel-after.json", new JObject { ["cancelled"] = streamer.IsCancelled, ["settled"] = streamer.IsSettled,
                        ["allScenesUnloaded"] = unloaded, ["loadsStarted"] = streamer.LoadsStarted,
                        ["unloadsStarted"] = streamer.UnloadsStarted, ["loadFailures"] = streamer.LoadFailures,
                        ["regions"] = JArray.FromObject(streamer.Regions.Select(region => new { region.AuthoringId, region.ScenePath,
                            residency = streamer.ResidencyOf(region.AuthoringId).ToString(), loaded = SceneManager.GetSceneByPath(region.ScenePath).isLoaded })) });
                    Evidence("editor-result.json", new JObject { ["status"] = "PASS", ["queryRequestId"] = state["requestId"],
                        ["regionCancellation"] = true, ["workerQuery"] = true, ["visualVerification"] = false });
                    SessionState.EraseString(StateKey); EditorApplication.Exit(0);
                }
            }
            catch (Exception error) { Fail(error); }
            finally { busy = false; }
        }

        private static async Task ProbeForeign(string job)
        {
            JObject evidence = new JObject();
            foreach (bool foreignApp in new[] { false, true })
            {
                var credentials = EtosCredentials.FromKeyFile((string)Config[foreignApp ? "foreignPairingFile" : "pairingFile"]!);
                using (var client = new CompanionClient(new EtosClientOptions { NodeUrl = (string)Config["nodeUrl"]!,
                    AppName = foreignApp ? "r8a-foreign" : "gamecore-unity", ProjectId = foreignApp ? (string)Config["projectId"]! : new string('f', 64) }, credentials))
                {
                    // ProbeAsync accepts arbitrary method and body only through the production client's public HTTP API.
                    // Reflection keeps this acceptance driver source-compatible with the baseline without CancelStageAsync.
                    MethodInfo? method = typeof(CompanionClient).GetMethod("CancelStageAsync");
                    Require(method != null, "Authenticated running-stage cancel client missing (before-fix regression)");
                    try
                    {
                        await (Task<StageJobInfo>)method!.Invoke(client, new object[] { job, CancellationToken.None })!;
                        throw new InvalidOperationException("Foreign owner cancelled the stage");
                    }
                    catch (EtosException refusal)
                    {
                        Require(refusal.Error.Status == 404, "Foreign owner must receive 404");
                        evidence[foreignApp ? "foreignAppStatus" : "foreignProjectStatus"] = refusal.Error.Status;
                    }
                }
            }
            using (var http = new HttpClient())
            using (HttpResponseMessage response = await http.PostAsync((string)Config["nodeUrl"]! + "/api/v1/agents/gamecore-studio/http/v1/stage/" + job + "/cancel", new StringContent("{}", Encoding.UTF8, "application/json")))
            {
                Require((int)response.StatusCode == 401, "Unauthenticated cancel must return 401");
                evidence["unauthenticatedStatus"] = (int)response.StatusCode;
            }
            Evidence("stage-authority.json", evidence);
            return;
        }

        private static AsyncOperation? PendingNativeLoad(RegionStreamer streamer, out string region)
        {
            region = string.Empty;
            var states = (IEnumerable)typeof(RegionStreamer).GetField("regions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(streamer)!;
            foreach (object state in states)
            {
                Type type = state.GetType();
                object? operation = type.GetProperty("Operation")!.GetValue(state);
                object? record = type.GetProperty("Record")!.GetValue(state);
                if (operation == null || record == null) continue;
                string id = (string)record.GetType().GetProperty("AuthoringId")!.GetValue(record)!;
                if (streamer.ResidencyOf(id) != RegionResidency.Loading) continue;
                // Unwrap the production DeferredRegionLoader and UnitySceneLoader, without invoking or modifying IO.
                for (int depth = 0; operation != null && depth < 3; depth++)
                {
                    if (operation is AsyncOperation native)
                    {
                        if (!native.isDone) { region = id; return native; }
                        break;
                    }
                    operation = operation.GetType().GetField("operation", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(operation);
                }
            }
            return null;
        }

        private static void Fail(Exception error)
        {
            try { Evidence("editor-failure.json", new JObject { ["status"] = "FAIL", ["state"] = State, ["error"] = new SecretRedactor().Redact(error.ToString()) }); }
            finally { SessionState.EraseString(StateKey); Debug.LogError(new SecretRedactor().Redact(error.ToString())); EditorApplication.Exit(1); }
        }
    }
}

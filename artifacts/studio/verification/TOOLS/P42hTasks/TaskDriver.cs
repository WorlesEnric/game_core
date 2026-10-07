#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GameCore.Gameplay.Dialogue;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.Npc;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Model;
using GameCore.Studio.UI;
using GameCore.Studio.Views;
using Hollowmere.Boot;
using Hollowmere.P2_1.Evidence;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using SelectionMode = GameCore.Studio.Model.SelectionMode;

namespace P42h.Tasks
{
    // This is an executeMethod driver, not a test or a replacement gateway. Every request,
    // candidate import, tray action, world command and reconnect uses the installed product.
    public sealed class TaskDriver : ScriptableSingleton<TaskDriver>
    {
        private const string StateKey = "P42h.Tasks.State";
        private const string Village = "Assets/Hollowmere/Regions/ThornwickVillage.unity";
        private const string BramId = "2825db71-11a1-4155-b1db-9e7734111d9e";
        private const string PulsePath = "Assets/Hollowmere/Tests/P42hTasks/CompilePulse.cs";
        [NonSerialized] private Task<PromptSubmission>? submission;
        [NonSerialized] private Task<RequestInfo>? requestRead;
        [NonSerialized] private Task<CandidateInfo>? candidateRead;
        [NonSerialized] private Task<EtosError?>? probe;
        [NonSerialized] private EtosAgentGateway? observedGateway;
        [NonSerialized] private EventStream? replay;
        [NonSerialized] private StudioViewContext? views;
        [NonSerialized] private ConcurrentQueue<JObject>? frames;
        [NonSerialized] private double nextRead;
        [NonSerialized] private string lastRequest = string.Empty;
        [NonSerialized] private volatile string ownedRequest = string.Empty;

        private static StudioUiContext Context => StudioUiSession.Context;
        private static JObject Load() => JObject.Parse(SessionState.GetString(StateKey, "{}"));
        private static void Save(JObject state) => SessionState.SetString(StateKey, state.ToString(Formatting.None));
        private static string Text(JObject state, string key) => (string?)state[key] ?? string.Empty;
        private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        private static int Phase(JObject state) => (int?)state["phase"] ?? 0;
        private static void Write(JObject state, string name, JToken value) => File.WriteAllText(Path.Combine(Text(state, "output"), name), value.ToString(Formatting.Indented) + "\n");
        private static void Event(JObject state, string kind, JObject data)
        {
            data["kind"] = kind;
            data["utcMs"] = Now;
            File.AppendAllText(Path.Combine(Text(state, "output"), "observations.jsonl"), data.ToString(Formatting.None) + "\n");
        }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        public static void JoinedMove() => Begin("move", "W-EDIT-01");
        public static void SelectedNpcQuery() => Begin("query", "W-ETOS-04");
        public static void TrayCancel() => Begin("cancel", "W-ETOS-05");
        public static void SourceReload() => Begin("reload", "W-ETOS-09");
        public static void StageCancelProbe() => Begin("stage-probe", "W-REC-03");

        private static void Begin(string lane, string row)
        {
            string output = Environment.GetEnvironmentVariable("GAMECORE_P42H_OUT") ?? throw new InvalidOperationException("GAMECORE_P42H_OUT is required.");
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(output);
            Require(!File.Exists(Path.Combine(output, "started.json")), "Use a fresh output directory; this driver never silently repeats a worker request.");
            var state = new JObject { ["lane"] = lane, ["row"] = row, ["phase"] = 1, ["output"] = output,
                ["startedMs"] = Now, ["processId"] = System.Diagnostics.Process.GetCurrentProcess().Id,
                ["requestId"] = "", ["taskIds"] = new JArray(), ["sourceProject"] = Path.GetFullPath(Path.Combine(Application.dataPath, "..")) };
            Save(state);
            Write(state, "started.json", state);
            Hook();
        }

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            CompilationPipeline.assemblyCompilationFinished -= Compiled;
            CompilationPipeline.assemblyCompilationFinished += Compiled;
        }

        private static void Compiled(string assembly, CompilerMessage[] messages)
        {
            JObject state = Load();
            if (Text(state, "lane") != "reload" || Phase(state) != 30) return;
            var errors = messages.Where(m => m.type == CompilerMessageType.Error).Select(m => m.message).ToArray();
            Event(state, "assembly-compiled", new JObject { ["assembly"] = assembly, ["errors"] = new JArray(errors) });
            if (Path.GetFileNameWithoutExtension(assembly) == "P42h.Tasks") state["harnessCompiled"] = errors.Length == 0;
            if (errors.Length != 0) state["compileErrors"] = new JArray(errors);
            Save(state);
        }

        private static void BeforeReload()
        {
            JObject state = Load();
            if (Text(state, "lane") != "reload" || Phase(state) != 30) return;
            instance.Flush(state);
            long cursor = instance.observedGateway?.Events.Cursor ?? EtosStudioSession.Gateway?.Events.Cursor ?? 0;
            state["cursorBeforeReload"] = cursor;
            state["beforeReloadMs"] = Now;
            TaskRow? row = Context.Tasks.Find(Text(state, "requestId"));
            state["rowBeforeReload"] = row == null ? null : Row(row);
            Event(state, "before-real-domain-reload", new JObject { ["cursor"] = cursor, ["row"] = state["rowBeforeReload"]?.DeepClone() });
            Save(state);
        }

        [DidReloadScripts]
        private static void Reloaded()
        {
            JObject state = Load();
            if (Text(state, "lane") != "reload" || Phase(state) != 30) return;
            state["afterReloadMs"] = Now;
            state["compiledToken"] = (string?)typeof(CompilePulse).GetField(nameof(CompilePulse.Token))!.GetRawConstantValue();
            state["reloadCallback"] = true;
            // Product shutdown may acknowledge a final already-handled frame after our before callback.
            // Read the completed durable cursor before starting the new gateway; never rewind it.
            state["resumeCursor"] = new FileCursorStore(Text(state, "cursorFile")).Load();
            Save(state);
            // Subscribe before the first main-thread queue drain; hello readiness is not a replay barrier.
            EtosStudioSession.EnsureStarted();
            if (EtosStudioSession.Gateway != null) instance.Observe(state, EtosStudioSession.Gateway);
        }

        private static void Tick()
        {
            JObject state = Load();
            if (Phase(state) == 0) return;
            try
            {
                instance.Flush(state);
                if (Now - (long)state["startedMs"]! > 600000)
                {
                    Finish(state, "FAIL", "Bounded ten-minute driver deadline; no success inferred from a missing outcome.");
                    return;
                }
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                instance.Advance(state);
                Save(state);
            }
            catch (Exception error)
            {
                Write(state, "error.json", new JObject { ["type"] = error.GetType().FullName, ["message"] = EtosRedaction.Redact(error.ToString()), ["phase"] = Phase(state) });
                Finish(state, "FAIL", EtosRedaction.Redact(error.Message));
            }
        }

        private void Advance(JObject state)
        {
            string lane = Text(state, "lane");
            if (Phase(state) == 1)
            {
                if (Application.isBatchMode)
                {
                    Finish(state, "BLOCKED", "Graphical Editor required; use the graphical adapter through unity-batch on display :1.");
                    return;
                }
                Require(Path.GetFileName(Text(state, "sourceProject")) == "hollowmere", "Run this clone's games/hollowmere project.");
                EditorSceneManager.OpenScene(lane == "move" ? "Assets/Hollowmere/Boot/Boot.unity" : Village);
                EditorApplication.ExecuteMenuItem("GameCore/Studio/Open Studio");
                StudioTasksWindow.Open();
                EtosStudioSession.EnsureStarted();
                state["phase"] = lane == "move" ? 2 : 4;
                Save(state);
                if (lane == "move") EditorApplication.EnterPlaymode();
                return;
            }
            if (Phase(state) == 2)
            {
                if (!EditorApplication.isPlaying) return;
                var boot = UnityEngine.Object.FindFirstObjectByType<GameBoot>();
                var rig = UnityEngine.Object.FindFirstObjectByType<Hollowmere.UiAudio.HollowmereUiAudio>();
                if (boot?.Saves == null || rig == null) return;
                Require(rig.Ui.Dispatcher.Dispatch("newgame").Accepted, "Production newgame UI command refused.");
                state["phase"] = 3;
                state["newGameMs"] = Now;
                return;
            }
            if (Phase(state) == 3)
            {
                var boot = UnityEngine.Object.FindFirstObjectByType<GameBoot>();
                var rig = UnityEngine.Object.FindFirstObjectByType<Hollowmere.UiAudio.HollowmereUiAudio>();
                if (boot?.World == null || boot.Saves == null || !boot.AdmissionReady(boot.Saves) || rig == null
                    || rig.Ui.Screen != GameCore.Rules.Gameplay.Ui.UiScreen.Hud || Now - (long)state["newGameMs"]! < 1500) return;
                state["phase"] = 4;
            }
            EtosStudioSession.EnsureStarted();
            EtosAgentGateway? gateway = EtosStudioSession.Gateway;
            if (gateway != null) Observe(state, gateway);
            if (gateway == null || !Context.Gateway.Status.AgentReady)
            {
                if (Now - (long)state["startedMs"]! > 90000)
                    Finish(state, "BLOCKED", "Installed gateway not ready: " + (EtosStudioSession.Problem?.Message ?? Context.Gateway.Status.Problem?.Message ?? "no authenticated hello"));
                return;
            }
            if (Phase(state) == 4)
            {
                Write(state, "hello.json", gateway.Hello?.Raw ?? new JObject());
                state["worker"] = gateway.Options.DesignWorker;
                if (lane == "stage-probe") { BeginStageProbe(state, gateway.Client); return; }
                PrepareAndSubmit(state);
                return;
            }
            if (Phase(state) == 5)
            {
                Require(submission != null, "Submission handle lost outside the deliberate reload lane.");
                if (!submission!.IsCompleted) return;
                PromptSubmission answer = submission.GetAwaiter().GetResult();
                Write(state, "submission.json", new JObject { ["accepted"] = answer.Accepted, ["changeSetId"] = answer.ChangeSetId,
                    ["requestId"] = answer.RequestId, ["refusal"] = answer.Refusal == null ? null : StudioJson.ToToken(answer.Refusal) });
                if (!answer.Accepted) { Finish(state, "BLOCKED", "Installed worker submission refused: " + answer.Refusal?.Code + ": " + answer.Refusal?.Message); return; }
                Require(answer.RequestId == Text(state, "requestId"), "Submission changed the entrance request identity.");
                state["phase"] = 6;
                return;
            }
            if (Phase(state) == 40)
            {
                if (probe == null || !probe.IsCompleted) return;
                EtosError? error = probe.GetAwaiter().GetResult();
                Write(state, "stage-route-probe.json", new JObject { ["method"] = "GET", ["path"] = Text(state, "probePath"),
                    ["status"] = error?.Status ?? 200, ["code"] = error?.Code, ["message"] = error?.Message,
                    ["meaning"] = "Read-only unsupported-route probe, not a cancellation attempt. No staging job was submitted or discarded." });
                Finish(state, "BLOCKED", "Authenticated app-origin staging exists, but no creator stage cancellation client method, server route, or running-slot cancellation contract exists. See stage-cancellation-seam.json; region cancellation is outside this slice.");
                return;
            }
            if (Phase(state) == 30)
            {
                if ((bool?)state["reloadCallback"] != true) return;
                Require((bool?)state["harnessCompiled"] == true && Text(state, "compiledToken") == Text(state, "compileToken"), "Reload callback did not load the actually changed harness source.");
                Require((int)state["processId"]! == System.Diagnostics.Process.GetCurrentProcess().Id, "Reload unexpectedly crossed Editor processes.");
                Require((long?)state["beforeReloadMs"] != null, "No actual beforeAssemblyReload witness.");
                state["gatewayStartsAfterReload"] = EtosStudioSession.instance.Starts;
                Require((int)state["gatewayStartsAfterReload"]! > (int)state["gatewayStartsBeforeReload"]!, "Production ETOS session did not restart after domain reload.");
                state["phase"] = 31;
                StudioTasksWindow.Open();
                long start = (long)state["resumeCursor"]!;
                Require(start >= (long)state["cursorBeforeReload"]!, "Durable event cursor regressed across reload.");
                replay = new EventStream(gateway.Client, new MemoryCursorStore(start));
                replay.Received += frame => Enqueue(frame, "audit", Text(state, "requestId"));
                replay.Start();
                Event(state, "after-real-domain-reload", new JObject { ["compiledToken"] = state["compiledToken"], ["cursor"] = gateway.Events.Cursor,
                    ["persistedCursor"] = ReadCursor(gateway), ["starts"] = EtosStudioSession.instance.Starts });
            }
            if (Phase(state) == 12) { CompleteMove(state); return; }
            RequestInfo? latest = Poll(state, gateway.Client);
            if (latest == null) return;
            TaskRow? task = Context.Tasks.Find(Text(state, "requestId"));
            if (task != null)
            {
                state["lastTrayRow"] = Row(task);
                state["taskIds"] = new JArray(task.taskIds);
            }
            if (Phase(state) == 6)
            {
                if (lane == "move") { AwaitMove(state, latest); return; }
                if (lane == "query") { AwaitQuery(state, latest); return; }
                if (latest.IsTerminal)
                {
                    Finish(state, "FAIL", "Task settled before running-task creator interaction: " + latest.State);
                    return;
                }
                if (latest.TaskStatus != "running" || task?.etosStatus != "running" || task.State != AgentRequestState.Running) return;
                Require(latest.Tasks.Count == 1 && task.taskIds.Count == 1 && latest.Tasks[0] == task.taskIds[0], "Running request has duplicate or mismatched task IDs.");
                state["originalTaskId"] = latest.Tasks[0];
                state["runningRequest"] = latest.Raw;
                state["runningTray"] = Row(task);
                if (lane == "cancel") { ClickCancel(state, task); state["phase"] = 20; }
                else TriggerCompilation(state, gateway);
                return;
            }
            if (Phase(state) == 31)
            {
                if (latest.IsTerminal) { Finish(state, "FAIL", "Task was no longer in flight when real compilation/reload returned: " + latest.State); return; }
                if (task == null || latest.TaskStatus != "running" || task.etosStatus != "running") return;
                Require(latest.Tasks.SequenceEqual(new[] { Text(state, "originalTaskId") }) && task.taskIds.SequenceEqual(latest.Tasks), "Reload duplicated or changed the task.");
                Require(Context.Tasks.Rows.Count(r => r.requestId == latest.RequestId) == 1, "Reload duplicated the tray row.");
                Require(task.createdTicks == (long)state["runningTray"]!["createdTicks"]!, "Reload recreated rather than preserved the tray row identity.");
                TaskTrayView tray = Tray();
                tray.Select(task.changeSetId);
                Require(tray.Q<VisualElement>("task-" + task.changeSetId)?.panel != null, "Recovered row is not attached to the visible task tray.");
                state["rowAfterReload"] = Row(task);
                state["sameRunningTaskAfterReload"] = true;
                Write(state, "reload-recovered.json", new JObject { ["request"] = latest.Raw, ["tray"] = Row(task), ["cursor"] = gateway.Events.Cursor,
                    ["rowCount"] = Context.Tasks.Rows.Count(r => r.requestId == latest.RequestId), ["compiledToken"] = state["compiledToken"] });
                Shot(state, "reload-same-running-row");
                ClickCancel(state, task);
                state["phase"] = 20;
                return;
            }
            if (Phase(state) == 20)
            {
                if (!latest.IsTerminal || task == null || task.State != AgentRequestState.Cancelled) return;
                Require(latest.State == RequestStates.Cancelled && latest.TaskStatus == "cancelled", "Companion and ETOS task must both acknowledge cancelled.");
                Require(!latest.HasCandidate && Context.Candidates.Find(latest.RequestId) == null, "A cancelled task produced a candidate.");
                Require(latest.Tasks.SequenceEqual(new[] { Text(state, "originalTaskId") }) && task.taskIds.SequenceEqual(latest.Tasks), "Cancellation duplicated or changed the task ID.");
                Require(Context.Tasks.Rows.Count(r => r.requestId == latest.RequestId) == 1, "More than one tray row owns the request.");
                if (state["cancelledMs"] == null) { state["cancelledMs"] = Now; state["finalCursor"] = gateway.Events.Cursor; return; }
                if (Now - (long)state["cancelledMs"]! < 3000) return;
                Write(state, "cancelled.json", new JObject { ["request"] = latest.Raw, ["tray"] = Row(task), ["candidate"] = false,
                    ["ackMs"] = (long)state["cancelledMs"]! - (long)state["clickedCancelMs"]!, ["stableForMs"] = Now - (long)state["cancelledMs"]! });
                Shot(state, "cancelled-tray");
                if (lane == "reload")
                {
                    if (replay == null || replay.Cursor < gateway.Events.Cursor) return;
                    if (!VerifyReplay(state, gateway)) return;
                }
                Finish(state, "PASS", lane == "reload"
                    ? "Actual changed-source compilation and domain reload preserved the same running task and one attached tray row; production cursor continuation matches independent authenticated ledger replay; attached Cancel then yielded one cancelled task and no candidate."
                    : "Attached real tray Cancel button was activated while ETOS and tray both reported running; one unchanged task became cancelled, with no candidate or duplicate row after a three-second settle observation.");
            }
        }

        private void PrepareAndSubmit(JObject state)
        {
            string lane = Text(state, "lane");
            Context.Runtime.Index.Rebuild();
            AuthoringRef target;
            string intent;
            if (lane == "move")
            {
                var entity = UnityEngine.Object.FindObjectsByType<AuthoredEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single(e => e.AuthoringId == BramId);
                target = Context.Runtime.Resolver.BuildRef(entity, AuthorScope.Instance, true) ?? throw new InvalidOperationException("Bram has no scoped authoring reference.");
                // Obtain the same production bridge bound by Studio views; do not install a harness translator.
                views = StudioViewContext.ForProject();
                Require(Context.Runtime.Live.IsAvailable && Context.Runtime.Engine.RuntimeMoves.Gateway != null, "No running world or installed runtime placement adapter.");
                Vector3 desired = entity.transform.position + new Vector3(0.5f, 0, 0);
                float yaw = entity.transform.eulerAngles.y;
                state["positionBefore"] = Vector(entity.transform.position);
                state["desiredPosition"] = Vector(desired);
                state["desiredYaw"] = yaw;
                state["target"] = StudioJson.ToToken(target);
                state["sceneHashBefore"] = ContentStamp.Sha256Hex(File.ReadAllBytes(Village));
                intent = "Move only the selected live Bram instance in the running GameCore world, not authored content. "
                    + "Use exactly one catalog runtime.move operation, scope Instance, copied selected reference and stamp. "
                    + "The creator measured the live desired absolute world position as " + Vector(desired).ToString(Formatting.None)
                    + " and yaw in degrees as " + yaw.ToString("R", CultureInfo.InvariantCulture) + ". "
                    + "Copy selection mode Play. No ordinary authored move, prefab edits, assets, generation, dialogue or other operations. Do not claim a committed operation ID; the creator will apply the unchanged candidate and observe the real receipt.";
            }
            else
            {
                NpcDefinition npc = AssetDatabase.LoadAssetAtPath<NpcDefinition>("Assets/Hollowmere/Npcs/Definitions/Bram.asset");
                Require(npc != null && npc.Dialogue is DialogueGraphDefinition, "Selected Bram NPC dialogue prerequisite missing.");
                target = Context.Runtime.Resolver.BuildRef(npc, AuthorScope.Definition, true) ?? throw new InvalidOperationException("Selected NPC has no authored reference.");
                var graph = (DialogueGraphDefinition)npc.Dialogue!;
                state["selectedNpc"] = StudioJson.ToToken(target);
                state["graphId"] = graph.AuthoringId;
                state["graphPath"] = AssetDatabase.GetAssetPath(graph);
                state["expectedTexts"] = new JArray(graph.Nodes.Select(n => n.text).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct());
                if (lane == "query")
                {
                    intent = "Read-only creator inspection of selected NPC Bram. Execute an actual worker-side `etos query` of gc_dialogue_node "
                        + "(binding gamecore-studio) to return the selected NPC's dialogue nodes. Resolve his graph from the provided NPC selection/context; "
                        + "the creator identifies its authoring ID as " + graph.AuthoringId + ". Inspect `etos query --help` if needed, then query the actual RG table. "
                        + "Do not substitute index-slice.json, /etos/rg files, invented rows or a local evaluator for the requested etos query. "
                        + "This is read-only: no changeset and no media. After the real query, ask the single follow-up 'Would you like any dialogue changes?' "
                        + "using /outputs/clarification.json with status needs-clarification. In its question string, before that question, retain exactly one receipt between "
                        + "P42H_QUERY_BEGIN and P42H_QUERY_END: JSON {command:<the exact executed etos query shell command>,exitCode:<actual integer>," 
                        + "stdout:<exact query stdout string>,stderr:<exact query stderr string>,graphId:<resolved graph ID>}. "
                        + "Preserve failures verbatim; never claim query success without executing it. Do not write unlisted extra output files.";
                }
                else
                {
                    intent = "Plan a read-only narrative review of selected NPC Bram, without generating any media or proposing a changeset. "
                        + "The creator is explicitly exercising " + (lane == "reload" ? "a real source compilation/domain reload during your running task" : "the attached task tray Cancel control")
                        + ". After reading inputs and before any narrative work, execute the shell command `sleep 90` once so the creator can interact while the real task remains in flight. "
                        + "If you are not cancelled, inspect the existing graph and emit only /outputs/clarification.json with status needs-clarification and one question about the desired narrative change. "
                        + "Do not mutate the project, invent a task state, or use image/TTS/voice/describe/3D operations.";
                }
            }
            Context.Selection.Set(new[] { target });
            SelectionSnapshot selection = Context.Selection.Capture(lane == "move" ? SelectionMode.Play : SelectionMode.Edit);
            PreparedRequest prepared = Context.Requests.Build(intent, selection);
            state["requestId"] = prepared.ChangeSetId;
            ownedRequest = prepared.ChangeSetId;
            state["submittedMs"] = Now;
            Write(state, "request.json", new JObject { ["requestId"] = prepared.ChangeSetId, ["intent"] = intent,
                ["selection"] = StudioJson.ToToken(selection), ["request"] = JObject.FromObject(prepared.Request),
                ["toolCatalogRevision"] = prepared.ToolCatalogRevision, ["contextBytes"] = prepared.ContextBytes,
                ["contextTruncated"] = prepared.ContextTruncated });
            state["phase"] = 5;
            Save(state); // Entrance identity is durable before the only Submit call.
            submission = Context.Submit(prepared);
            Shot(state, "submitted");
        }

        private RequestInfo? Poll(JObject state, CompanionClient client)
        {
            if (requestRead == null)
            {
                if (EditorApplication.timeSinceStartup < nextRead) return null;
                nextRead = EditorApplication.timeSinceStartup + 0.5;
                requestRead = client.GetRequestAsync(Text(state, "requestId"));
                return null;
            }
            if (!requestRead.IsCompleted) return null;
            RequestInfo request = requestRead.GetAwaiter().GetResult();
            requestRead = null;
            string serial = request.Raw.ToString(Formatting.None);
            if (serial != lastRequest)
            {
                Event(state, "authenticated-request", new JObject { ["request"] = request.Raw });
                lastRequest = serial;
                Write(state, "latest-request.json", request.Raw);
            }
            state["taskIds"] = new JArray(request.Tasks);
            File.WriteAllText(Path.Combine(Text(state, "output"), "task-ids.txt"), string.Join("\n", request.Tasks) + "\n");
            return request;
        }

        private void AwaitMove(JObject state, RequestInfo request)
        {
            if (!request.IsTerminal) return;
            if (!request.HasCandidate)
            {
                Finish(state, "FAIL", "Worker produced no unchanged live-move candidate: " + request.State + "; " + request.Outcome?.ToString(Formatting.None));
                return;
            }
            CandidateEntry? entry = Context.Candidates.Find(request.RequestId);
            if (entry == null) return;
            if (candidateRead == null) { candidateRead = observedGateway!.Client.GetCandidateAsync(request.RequestId); return; }
            if (!candidateRead.IsCompleted) return;
            CandidateInfo candidate = candidateRead.GetAwaiter().GetResult();
            Write(state, "candidate-envelope.json", candidate.Raw);
            Write(state, "candidate-before-apply.json", StudioJson.ToToken(entry.ChangeSet));
            // Product import adds trustworthy links. Compare all other candidate fields exactly; no harness repair.
            JObject imported = (JObject)StudioJson.ToToken(entry.ChangeSet);
            JObject supplied = (JObject)candidate.ChangeSet.DeepClone();
            imported.Remove("links"); supplied.Remove("links");
            Require(JToken.DeepEquals(imported, supplied), "Imported candidate differs from the companion candidate beyond production-added links; no repair permitted.");
            Require(entry.ChangeSet.Operations.Count == 1, "The worker did not keep the live move to one operation.");
            Operation operation = entry.ChangeSet.Operations[0];
            Require(operation.Tool == RuntimeMovePromotion.ToolId && operation.Target?.Scope == AuthorScope.Instance && operation.Target.AuthoringId == BramId,
                "Worker candidate is not the requested scoped runtime move of Bram.");
            Require(operation.Args?["position"] is JArray actualPosition && actualPosition.Count == 3
                && actualPosition.Values<float>().SequenceEqual(((JArray)state["desiredPosition"]!).Values<float>())
                && Math.Abs(((double?)operation.Args?["yaw"] - (double?)state["desiredYaw"]) ?? double.MaxValue) < 0.0001,
                "Worker candidate changed the measured desired move.");
            Require((entry.ChangeSet.Artifacts?.Count ?? 0) == 0, "Unexpected media/artifact generation in live move lane.");
            state["candidateHash"] = ContentStamp.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(candidate.ChangeSet.ToString(Formatting.None)));
            state["phase"] = 12;
            StudioCandidatesWindow.Open(entry.Id);
            ApplyReport report = Context.Candidates.Apply(entry);
            Write(state, "apply-report.json", new JObject { ["state"] = report.State.ToString(), ["ok"] = report.Ok,
                ["entry"] = StudioJson.ToToken(report.Entry), ["diagnostics"] = JToken.FromObject(report.Diagnostics) });
            Require(report.Ok && report.State == ChangeSetState.Applied, "Unchanged worker candidate failed real apply.");
            Require(request.Tasks.Count > 0 && report.Entry.Links?.EtosTasks != null && request.Tasks.All(id => report.Entry.Links.EtosTasks.Contains(id)), "History lost joined ETOS task IDs.");
            Require(report.Entry.Outcomes?.Count == 1 && report.Entry.Outcomes[0].Status == OutcomeStatus.Applied
                && report.Entry.Outcomes[0].GameCoreOps?.Count == 1 && report.Entry.Links.GameCoreOps?.Count == 1,
                "History has no unique applied GameCore operation receipt.");
            state["operationId"] = report.Entry.Outcomes![0].GameCoreOps![0];
            Require(report.Entry.Links.GameCoreOps![0] == Text(state, "operationId"), "History links disagree with the applied outcome operation ID.");
            state["appliedMs"] = Now;
        }

        private void CompleteMove(JObject state)
        {
            ChangeSet entry = Context.Runtime.Journal.Read(Text(state, "requestId")) ?? throw new InvalidOperationException("Applied History entry disappeared.");
            Operation op = entry.Operations[0];
            JArray position = (JArray)op.Args!["position"]!;
            var desired = new Vector3((float)position[0], (float)position[1], (float)position[2]);
            bool committed = Context.Runtime.Engine.RuntimeMoves.Gateway!.IsAt(op.Target!, desired, (float)op.Args["yaw"]!, Text(state, "operationId"));
            if (!committed)
            {
                Require(Now - (long)state["appliedMs"]! < 15000, "Runtime move never became a Committed receipt at the requested pose; an admitted ID alone is insufficient.");
                return;
            }
            StudioHistoryWindow.Open();
            HistoryPanelView history = EditorWindow.GetWindow<StudioHistoryWindow>().View ?? throw new InvalidOperationException("History panel missing.");
            if (state["historyShownMs"] == null)
            {
                history.Select(entry.Id);
                state["historyShownMs"] = Now;
                return;
            }
            if (Now - (long)state["historyShownMs"]! < 1000) return;
            Require(history.panel != null, "History panel is not attached.");
            string[] labels = history.Q<ScrollView>("history-details").Query<Label>().ToList().Select(l => l.text).ToArray();
            Require(entry.Links!.EtosTasks!.All(id => labels.Any(t => t.Contains(id))) && labels.Any(t => t.Contains(Text(state, "operationId"))),
                "Attached History details do not display both task and GameCore operation IDs.");
            Require(ContentStamp.Sha256Hex(File.ReadAllBytes(Village)) == Text(state, "sceneHashBefore"), "Live move mutated authored scene bytes.");
            Write(state, "joined-history.json", new JObject { ["entry"] = StudioJson.ToToken(entry), ["visibleLabels"] = new JArray(labels),
                ["operationId"] = Text(state, "operationId"), ["committedReceiptAndPose"] = committed,
                ["desiredPosition"] = position, ["isPlaying"] = EditorApplication.isPlaying, ["authoredSceneUnchanged"] = true,
                ["commitWitness"] = "Production ReflectionGameplayBridge.IsAt checks current world session, exact request receipt Outcome.Kind == Committed, and all four committed pose slots." });
            Shot(state, "joined-task-gamecore-history");
            Finish(state, "PASS", "Installed-worker candidate applied without harness changes; attached History shows joined task and GameCore operation IDs, and the production bridge confirms that exact operation committed the requested live pose without changing authored scene bytes.");
        }

        private static void AwaitQuery(JObject state, RequestInfo request)
        {
            if (!request.IsTerminal) return;
            Write(state, "worker-query-outcome.json", request.Raw);
            string question = (string?)request.Outcome?["question"] ?? "";
            int begin = question.IndexOf("P42H_QUERY_BEGIN", StringComparison.Ordinal);
            int end = question.IndexOf("P42H_QUERY_END", StringComparison.Ordinal);
            if (begin < 0 || end <= begin)
            {
                Finish(state, "FAIL", "Worker did not retain its requested etos query command and exact result in the clarification receipt.");
                return;
            }
            JObject receipt = JObject.Parse(question.Substring(begin + "P42H_QUERY_BEGIN".Length, end - begin - "P42H_QUERY_BEGIN".Length).Trim());
            Write(state, "worker-query-receipt.json", receipt);
            Require(request.State == RequestStates.NeedsClarification && !request.HasCandidate && Context.Candidates.Find(request.RequestId) == null, "Read-only query unexpectedly proposed a candidate.");
            string stdout = (string?)receipt["stdout"] ?? "";
            bool resultMatches = ((JArray)state["expectedTexts"]!).Values<string>().All(text => stdout.Contains(text!));
            bool success = (int?)receipt["exitCode"] == 0 && (string?)receipt["graphId"] == Text(state, "graphId")
                && ((string?)receipt["command"] ?? "").Contains("etos") && ((string?)receipt["command"] ?? "").Contains("query") && resultMatches;
            state["queryReceiptMatchesSelectedGraph"] = success;
            Write(state, "query-result-check.json", new JObject { ["matchesSelectedGraph"] = success, ["graphId"] = state["graphId"],
                ["expectedTexts"] = state["expectedTexts"], ["provenance"] = "Authenticated worker output; independent worker tool-call trace still required to distinguish actual etos query from a textual claim." });
            Shot(state, "selected-npc-query-result");
            Finish(state, success ? "BLOCKED" : "FAIL", success
                ? "Selected NPC worker request and matching query stdout retained. Complete W-ETOS-04 only after collecting the same task's actual tool-call trace showing this etos query and response; authenticated worker prose alone is not execution proof."
                : "Worker query receipt failed: exit code, selected graph binding or graph dialogue texts disagree. Raw query result retained unchanged.");
        }

        private static TaskTrayView Tray()
        {
            TaskTrayView tray = EditorWindow.GetWindow<StudioTasksWindow>().View ?? throw new InvalidOperationException("Production task tray missing.");
            Require(tray.panel != null, "Task tray must be attached to the graphical Editor panel.");
            return tray;
        }

        private static void ClickCancel(JObject state, TaskRow row)
        {
            TaskTrayView tray = Tray();
            tray.Select(row.changeSetId);
            Button button = tray.Q<VisualElement>("task-" + row.changeSetId)?.Q<Button>("cancel") ?? throw new InvalidOperationException("Real row Cancel button not found.");
            Require(button.panel != null && button.enabledInHierarchy && row.etosStatus == "running", "Cancel requires an attached, enabled control on the running task row.");
            Shot(state, "running-before-cancel");
            state["clickedCancelMs"] = Now;
            Event(state, "creator-cancel-button", new JObject { ["requestId"] = row.requestId, ["taskIds"] = new JArray(row.taskIds),
                ["control"] = "task-" + row.changeSetId + "/cancel", ["attached"] = true, ["enabled"] = true,
                ["input"] = "NavigationSubmitEvent dispatched to the actual attached Button, not TaskTrayView.Cancel or gateway.CancelAsync." });
            using (NavigationSubmitEvent input = NavigationSubmitEvent.GetPooled())
            {
                input.target = button;
                button.SendEvent(input);
            }
        }

        private static void TriggerCompilation(JObject state, EtosAgentGateway gateway)
        {
            Require(File.Exists(PulsePath), "Install CompilePulse.cs at " + PulsePath + " before the reload lane.");
            string token = Guid.NewGuid().ToString("N");
            state["compileToken"] = token;
            state["gatewayStartsBeforeReload"] = EtosStudioSession.instance.Starts;
            state["cursorAtCompileRequest"] = gateway.Events.Cursor;
            state["cursorFile"] = CursorPath(gateway);
            state["sourceBeforeSha256"] = ContentStamp.Sha256Hex(File.ReadAllBytes(PulsePath));
            state["phase"] = 30;
            Save(state);
            Shot(state, "running-before-source-compilation");
            string source = "#nullable enable\nnamespace P42h.Tasks { public static class CompilePulse { public const string Token = \"" + token + "\"; } }\n";
            File.WriteAllText(PulsePath, source);
            state["sourceAfterSha256"] = ContentStamp.Sha256Hex(File.ReadAllBytes(PulsePath));
            Require(Text(state, "sourceAfterSha256") != Text(state, "sourceBeforeSha256"), "Compilation trigger did not change source bytes.");
            Write(state, "source-compilation-request.json", new JObject { ["path"] = PulsePath, ["beforeSha256"] = state["sourceBeforeSha256"],
                ["afterSha256"] = state["sourceAfterSha256"], ["token"] = token, ["taskId"] = state["originalTaskId"],
                ["cursor"] = gateway.Events.Cursor, ["runningRequest"] = state["runningRequest"] });
            Save(state);
            AssetDatabase.ImportAsset(PulsePath, ImportAssetOptions.ForceUpdate);
            CompilationPipeline.RequestScriptCompilation();
        }

        private void Observe(JObject state, EtosAgentGateway gateway)
        {
            ownedRequest = Text(state, "requestId");
            frames ??= new ConcurrentQueue<JObject>();
            if (ReferenceEquals(observedGateway, gateway)) return;
            observedGateway = gateway;
            string generation = (bool?)state["reloadCallback"] == true ? "afterReload" : "beforeReload";
            // Capture values here: stream callbacks never call Unity SessionState off the main thread.
            gateway.Events.Received += frame => Enqueue(frame, generation, ownedRequest);
            Event(state, "gateway-observer-attached", new JObject { ["generation"] = generation, ["cursor"] = gateway.Events.Cursor,
                ["persistedCursor"] = ReadCursor(gateway), ["starts"] = EtosStudioSession.instance.Starts });
        }

        private void Enqueue(EventFrame frame, string generation, string request)
        {
            // Only the owned request's payload is retained; other app frames supply cursor/type continuity.
            frames!.Enqueue(new JObject { ["cursor"] = frame.Cursor, ["at"] = frame.At,
                ["receivedAt"] = frame.ReceivedAt, ["generation"] = generation, ["type"] = frame.Type,
                ["requestId"] = frame.RequestId, ["data"] = frame.RequestId == request ? frame.Data.DeepClone() : null });
        }

        private void Flush(JObject state)
        {
            if (frames == null || Text(state, "output").Length == 0) return;
            while (frames.TryDequeue(out JObject? frame))
                File.AppendAllText(Path.Combine(Text(state, "output"), "events.jsonl"), frame.ToString(Formatting.None) + "\n");
        }

        private static string CursorPath(EtosAgentGateway gateway)
        {
            string scope = ContentStamp.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(gateway.Client.Options.AppName + "\n" + gateway.Client.Options.ProjectId));
            return Path.Combine(Context.Runtime.Paths.LibraryRoot, scope + "-" + EtosStudioSession.CursorFileName);
        }

        private static long ReadCursor(EtosAgentGateway gateway) => new FileCursorStore(CursorPath(gateway)).Load();

        private bool VerifyReplay(JObject state, EtosAgentGateway gateway)
        {
            Flush(state);
            long after = (long)state["resumeCursor"]!;
            long through = gateway.Events.Cursor;
            string path = Path.Combine(Text(state, "output"), "events.jsonl");
            List<JObject> all = File.ReadLines(path).Select(JObject.Parse).ToList();
            List<JObject> actual = all.Where(f => (string?)f["generation"] == "afterReload" && (long)f["cursor"]! > after && (long)f["cursor"]! <= through).ToList();
            List<JObject> audit = all.Where(f => (string?)f["generation"] == "audit" && (long)f["cursor"]! <= through).ToList();
            if (audit.Count == 0) return false;
            bool exact = actual.Select(f => (long)f["cursor"]!).SequenceEqual(audit.Select(f => (long)f["cursor"]!));
            int terminal = actual.Count(f => (string?)f["requestId"] == Text(state, "requestId") && (string?)f["type"] == "request"
                && (string?)f["data"]?["state"] == RequestStates.Cancelled);
            Write(state, "cursor-replay.json", new JObject { ["after"] = after, ["through"] = through, ["persistedCursor"] = ReadCursor(gateway),
                ["productionCursors"] = new JArray(actual.Select(f => f["cursor"])), ["independentReplayCursors"] = new JArray(audit.Select(f => f["cursor"])),
                ["exactNoGapNoDuplicate"] = exact, ["ownedTerminalEventCount"] = terminal });
            Require(exact && actual.Count != 0 && terminal == 1, "Production post-reload cursor continuation does not exactly match the independent replay or has not exactly one cancellation outcome.");
            Require((bool?)state["sameRunningTaskAfterReload"] == true, "No same-running-task tray witness after reload.");
            return true;
        }

        private void BeginStageProbe(JObject state, CompanionClient client)
        {
            const string seam = "CompanionClient.StageAppCandidateAsync(changeSetId, projectId, sourceRevision, catalogRevision, changeSet, toolCatalog, artifactBytes, ct) signs exact UTF-8 payload and POSTs /v1/stage/app-candidate. GetStageAsync(jobId) reads status. There is no CancelStage API; DiscardStageAsync is not cancellation.";
            var methods = typeof(CompanionClient).GetMethods().Where(m => m.Name.Contains("Stage")).Select(m => m.ToString()).ToArray();
            Write(state, "stage-cancellation-seam.json", new JObject { ["row"] = "W-REC-03", ["status"] = "BLOCKED", ["seam"] = seam,
                ["installedClientMethods"] = new JArray(methods), ["sourceEvidence"] = new JArray(
                    "Packages/com.gamecore.studio.etos/Client/CompanionClient.cs:155-208",
                    "Packages/com.gamecore.studio.etos/Editor/CompanionStageService.cs:39-75",
                    "studio/agent/src/api.rs:100-124", "studio/agent/src/stage.rs:280-288"),
                ["missingContract"] = "Authenticated app/project-owned running-job cancel endpoint, cancellation receipt/state, slot/process teardown and proof slot became reusable.",
                ["appOriginRequestShape"] = new JObject { ["route"] = "POST /v1/stage/app-candidate", ["envelope"] = "payloadBase64, signature",
                    ["payload"] = "app, request{changeSetId,projectId,sourceRevision,catalogRevision}, changeSet, toolCatalog, files[{bytesBase64}]",
                    ["authority"] = "Production client HMAC and transient authenticated X-GameCore-Stage-Key; no credentials retained." },
                ["notAttempted"] = "No expensive stage submission solely to prove absent cancellation; no discard, no fabricated cancel action, no host kill, no region-load claim." });
            string job = Environment.GetEnvironmentVariable("GAMECORE_P42H_STAGE_JOB") ?? "p42h-cancellation-route-probe";
            state["probePath"] = client.BasePath + "/v1/stage/" + Uri.EscapeDataString(job) + "/cancel";
            probe = client.ProbeAsync(Text(state, "probePath"));
            state["phase"] = 40;
        }

        private static JObject Row(TaskRow row) => new JObject { ["changeSetId"] = row.changeSetId, ["requestId"] = row.requestId,
            ["state"] = row.state, ["etosStatus"] = row.etosStatus, ["taskIds"] = new JArray(row.taskIds), ["worker"] = row.worker,
            ["sequence"] = row.sequence, ["localState"] = row.localState, ["createdTicks"] = row.createdTicks,
            ["diagnostics"] = new JArray(row.diagnostics), ["question"] = row.question };
        private static JArray Vector(Vector3 value) => new JArray(value.x, value.y, value.z);
        private static void Shot(JObject state, string name)
        {
            foreach (EditorWindow window in Resources.FindObjectsOfTypeAll<EditorWindow>()) window.Repaint();
            string? problem = UnityWindowCapture.CaptureStudio(Path.Combine(Text(state, "output"), name + ".png"), false);
            Event(state, "graphical-capture", new JObject { ["file"] = name + ".png", ["problem"] = problem });
            Require(problem == null, "Graphical capture failed: " + problem);
        }
        private static void Finish(JObject state, string status, string detail)
        {
            state["phaseAtFinish"] = Phase(state);
            state["phase"] = 0;
            state["status"] = status;
            state["detail"] = detail;
            state["finishedMs"] = Now;
            Save(state);
            instance.Flush(state);
            Write(state, "result.json", state);
            // Runtime-only moves are not undone or promoted. Editor shutdown disposes their isolated Play world.
            // Reload changed only the installed harness PulsePath; restore that copy from TOOLS after exit.
            EditorApplication.Exit(status == "PASS" ? 0 : status == "BLOCKED" ? 2 : 1);
        }
    }
}

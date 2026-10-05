// Hollowmere.P3_2.Workflows - reusable steps of the AI-workflow recordings: open a region in the Studio layout, wait for
// the live gateway, pick in the viewport (click, overlap list, marquee, point-at), type and send a prompt from the prompt
// bar, follow the request in the task tray (answering one clarification from the tray), preview and compare the
// candidate in the candidate panel, apply it (policy as given), undo/redo it from the History panel, reject it. Every
// step records what the creator would see (keyframes) and the raw data (JSON under the request's tag folder).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using GameCore.Studio.Authoring;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos;
using GameCore.Studio.Model;
using GameCore.Studio.UI;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hollowmere.P3_2.Workflows
{
    /// <summary>Per-request bookkeeping that the gateway timeline hooks update.</summary>
    public static class LiveRequests
    {
        /// <summary>Submit completions (thread pool), read on the main thread.</summary>
        private static readonly Dictionary<string, (long At, Diagnostic? Refusal, string? RequestId)> Accepted = new Dictionary<string, (long, Diagnostic?, string?)>();

        public static void Track(string tag, string id, Task<PromptSubmission>? submit)
        {
            P32State.instance.MapId(id, tag);
            if (submit != null)
            {
                submit.ContinueWith(done =>
                {
                    lock (Accepted)
                    {
                        PromptSubmission? handle = done.Status == TaskStatus.RanToCompletion ? done.Result : null;
                        Accepted[id] = (WorkflowRunner.NowMs, handle?.Refusal ?? (done.Exception != null ? new Diagnostic("transport", done.Exception.GetBaseException().Message) : null), handle?.RequestId);
                    }
                });
            }
        }

        public static void TrackNullable(string tag, string id, Task<PromptSubmission?> submit)
        {
            Track(tag, id, submit.ContinueWith(done => done.Status == TaskStatus.RanToCompletion && done.Result != null ? done.Result : new PromptSubmission(id, null, new Diagnostic("refused", "the prompt bar did not send"))));
        }

        /// <summary>Moves submit completions into the persistent record.</summary>
        public static void Collect(string tag)
        {
            JObject r = P32State.instance.Req(tag);
            bool changed = false;
            lock (Accepted)
            {
                foreach (string id in Ids(r))
                {
                    if (Accepted.TryGetValue(id, out var value) && r["accepted"]?[id] == null)
                    {
                        JObject accepted = r["accepted"] as JObject ?? new JObject();
                        accepted[id] = new JObject { ["atMs"] = value.At, ["refusal"] = value.Refusal == null ? null : StudioJson.ToToken(value.Refusal), ["requestId"] = value.RequestId };
                        r["accepted"] = accepted;
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                P32State.instance.SetReq(tag, r);
            }
        }

        public static IEnumerable<string> Ids(JObject r)
        {
            foreach (JToken id in r["ids"] as JArray ?? new JArray())
            {
                yield return (string)id!;
            }
        }

        public static void OnEvent(string tag, RequestView view, long now)
        {
            JObject r = P32State.instance.Req(tag);
            JObject first = r["firstEvent"] as JObject ?? new JObject();
            if (first[view.RequestId] == null)
            {
                first[view.RequestId] = now;
            }

            r["firstEvent"] = first;
            JObject states = r["stateAt"] as JObject ?? new JObject();
            string key = view.RequestId + ":" + view.State + (view.LocalState != null ? "/" + view.LocalState : string.Empty);
            if (states[key] == null)
            {
                states[key] = now;
            }

            r["stateAt"] = states;
            // Only state transitions carry a fresh companion updatedAt (task_progress events re-raise the view with the
            // request's previous updatedAt), so the visibility lag is measured on transitions only.
            JArray lags = r["lags"] as JArray ?? new JArray();
            JObject last = r["lastState"] as JObject ?? new JObject();
            string current = view.State + "/" + view.TaskStatus;
            if (view.UpdatedAt > 1_000_000_000_000 && (string?)last[view.RequestId] != current)
            {
                lags.Add(now - view.UpdatedAt);
            }

            last[view.RequestId] = current;
            r["lastState"] = last;

            r["lags"] = lags;
            P32State.instance.SetReq(tag, r);
        }

        public static void OnStaged(string tag, CandidateImport import, long now)
        {
            JObject r = P32State.instance.Req(tag);
            r["stagedAtMs"] = now;
            r["importMs"] = Math.Round(import.Milliseconds, 1);
            r["importOk"] = import.Ok;
            P32State.instance.SetReq(tag, r);
        }
    }

    /// <summary>Step factories.</summary>
    public static class S
    {
        public const string VillageScene = "Assets/Hollowmere/Regions/ThornwickVillage.unity";
        public const string MarshScene = "Assets/Hollowmere/Regions/BlackmereMarsh.unity";
        public const string BootScene = "Assets/Hollowmere/Boot/Boot.unity";
        public const double DefaultTimeoutSeconds = 900;

        private static P32State St => P32State.instance;

        /// <summary>The open Studio viewport (opened once; not re-focused on every call).</summary>
        public static StudioViewportWindow Viewport()
        {
            StudioViewportWindow? viewport = Resources.FindObjectsOfTypeAll<StudioViewportWindow>().FirstOrDefault(w => w != null);
            if (viewport == null)
            {
                viewport = StudioViewportWindow.Open();
            }

            viewport.EnsureGui();
            return viewport;
        }

        public static StudioUiContext Context => Viewport().Context;

        public static PromptBar Prompt => Viewport().Prompt ?? throw new InvalidOperationException("The viewport has no prompt bar.");

        public static Step Do(string name, Func<bool> run) => new Step(name, run);

        public static Step Note(string name, Action action) => new Step(name, () =>
        {
            action();
            return true;
        });

        /// <summary>Waits <paramref name="seconds"/> of editor time.</summary>
        public static Step Wait(string name, double seconds) => new Step(name, () =>
        {
            string key = "wait." + name;
            if (St.Get(key) == null)
            {
                St.Set(key, EditorApplication.timeSinceStartup + seconds);
                return false;
            }

            return EditorApplication.timeSinceStartup >= St.Num(key);
        });

        // ------------------------------------------------------------------------------------------------ layout

        public static Step OpenScene(string scene) => new Step("open " + Path.GetFileNameWithoutExtension(scene), () =>
        {
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            StudioMenu.OpenStudio(new Rect(40f, 40f, 1600f, 900f), true);
            if (EditorWindow.HasOpenInstances<FirstRunWizardWindow>())
            {
                EditorWindow.GetWindow<FirstRunWizardWindow>().Close();
            }

            StudioViewportWindow viewport = Viewport();
            viewport.SetMode(ViewportMode.Select);
            WorkflowRunner.Log("layout", "Opened " + scene + " in the Studio layout (viewport, context, tasks, candidates, history).", null);
            return true;
        });

        /// <summary>
        /// Places the five Studio windows over the 1920x1080 display once they are mapped (the window manager ignores the
        /// sizes OpenStudio asks for before the windows are shown) and records where they ended up.
        /// </summary>
        public static Step Relayout() => new Step("relayout", () =>
        {
            int pass = (int)St.Num("relayout.pass");
            Dictionary<Type, Rect> layout = new Dictionary<Type, Rect>
            {
                [typeof(StudioViewportWindow)] = new Rect(20f, 70f, 1240f, 640f),
                [typeof(StudioContextWindow)] = new Rect(1272f, 70f, 628f, 990f),
                [typeof(StudioTasksWindow)] = new Rect(20f, 722f, 410f, 338f),
                [typeof(StudioCandidatesWindow)] = new Rect(436f, 722f, 410f, 338f),
                [typeof(StudioHistoryWindow)] = new Rect(852f, 722f, 408f, 338f),
            };
            JObject actual = new JObject();
            foreach (EditorWindow window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (window != null && layout.TryGetValue(window.GetType(), out Rect rect))
                {
                    if (pass < 2)
                    {
                        window.minSize = new Vector2(Mathf.Min(rect.width, 320f), Mathf.Min(rect.height, 200f));
                        window.position = rect;
                        window.Repaint();
                    }

                    actual[window.GetType().Name] = window.position.ToString();
                }
            }

            St.Set("relayout.pass", pass + 1);
            if (pass < 2)
            {
                return false;
            }

            Viewport().RenderNow();
            WorkflowRunner.Log("relayout", "Studio windows placed: " + actual.ToString(Newtonsoft.Json.Formatting.None), new JObject { ["windows"] = actual });
            return true;
        });

        /// <summary>Waits until the live gateway reports the node and the agent ready.</summary>
        public static Step WaitGateway() => new Step("wait for the gateway", () =>
        {
            StudioUiContext context = Context;
            ProviderStatus status = context.Gateway.Status;
            Prompt.Refresh();
            bool ready = status.NodeReachable && status.AgentReady;
            if (!ready && St.Waits++ < 180)
            {
                if (St.Waits % 15 == 0)
                {
                    WorkflowRunner.Log("gateway-wait", "waiting for the gateway: " + ProviderNames.ConnectionOf(status) + " (" + context.Gateway.GetType().Name + ")", null);
                }

                return false;
            }

            JObject providers = new JObject
            {
                ["gateway"] = context.Gateway.GetType().Name,
                ["connection"] = ProviderNames.ConnectionOf(status).ToString(),
                ["image"] = status.Image.ToString(),
                ["tts"] = status.Tts.ToString(),
                ["voice"] = status.Voice.ToString(),
                ["describe"] = status.Describe.ToString(),
                ["3d"] = status.ThreeD.ToString(),
                ["companion"] = status.CompanionVersion,
                ["problem"] = status.Problem == null ? null : StudioJson.ToToken(status.Problem),
            };
            WorkflowRunner.Json("providers.json", providers);
            WorkflowRunner.Shot("gateway", "Prompt bar provider chips: " + providers.ToString(Newtonsoft.Json.Formatting.None), providers);
            if (!ready)
            {
                WorkflowRunner.MarkFailed("the gateway never became ready: " + ProviderNames.ConnectionOf(status));
            }

            return true;
        });

        // ------------------------------------------------------------------------------------------------ picking

        public static GameObject? Find(string name)
        {
            GameObject? exact = GameObject.Find(name);
            if (exact != null)
            {
                return exact;
            }

            foreach (Transform t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID))
            {
                if (string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return t.gameObject;
                }
            }

            return null;
        }

        public static GameObject Require(string name) => Find(name) ?? throw new InvalidOperationException("No object named '" + name + "' in the open scene(s).");

        public static void FrameOn(IReadOnlyList<GameObject> targets, float pad = 1.5f)
        {
            StudioViewportWindow viewport = Viewport();
            Bounds bounds = new Bounds(targets[0].transform.position, Vector3.one * 2f);
            foreach (GameObject target in targets)
            {
                bounds.Encapsulate(target.transform.position);
                foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>())
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            bounds.Expand(bounds.size.magnitude * pad);
            viewport.Renderer.ForceFreeCamera = true;
            viewport.Renderer.Frame(bounds);
            viewport.RenderNow();
        }

        /// <summary>Clicks each object in the viewport (first Replace, then Add), using the overlap list when needed.</summary>
        public static Step Select(params string[] names) => new Step("select " + string.Join(", ", names), () =>
        {
            StudioViewportWindow viewport = Viewport();
            viewport.SetMode(ViewportMode.Select);
            List<GameObject> targets = names.Select(Require).ToList();
            FrameOn(targets);
            List<string> how = new List<string>();
            for (int i = 0; i < targets.Count; i++)
            {
                GameObject target = targets[i];
                Rect? rect = viewport.ScreenRectOf(target);
                SelectionOp op = i == 0 ? SelectionOp.Replace : SelectionOp.Add;
                if (rect == null)
                {
                    AuthoringRef? reference = viewport.Context.Selection.RefOf(target);
                    if (reference != null)
                    {
                        viewport.Context.Selection.Set(new[] { reference }, op);
                    }

                    how.Add(target.name + " (off screen: selected from the hierarchy)");
                    continue;
                }

                how.Add(target.name + " (" + Click(viewport, target, rect.Value.center, op) + ")");
            }

            viewport.Overlap?.Hide();
            StudioContextWindow.Open();
            string badges = string.Join(", ", viewport.Context.Selection.Describe().Select(b => b.Label + " [" + b.TypeId + "]"));
            WorkflowRunner.Shot("select", "Viewport Select: " + string.Join("; ", how) + ". Selection: " + badges + ".", new JObject { ["selection"] = badges, ["bSelect"] = viewport.Timings.Report() });
            return true;
        });

        private static string Click(StudioViewportWindow viewport, GameObject target, Vector2 point, SelectionOp op)
        {
            IReadOnlyList<PickCandidate> candidates = viewport.ClickAt(point, op);
            if (candidates.Count == 0 || IsPartOf(candidates[0], target))
            {
                return candidates.Count + " candidate(s), nearest";
            }

            foreach (PickCandidate candidate in candidates)
            {
                if (IsPartOf(candidate, target))
                {
                    if (op == SelectionOp.Add)
                    {
                        viewport.Context.Selection.Set(new[] { candidates[0].Ref }, SelectionOp.Toggle);
                    }

                    viewport.Picker.Choose(candidate, false, op == SelectionOp.Add ? SelectionOp.Add : SelectionOp.Replace);
                    return candidates.Count + " candidate(s), chosen from the overlap list";
                }
            }

            AuthoringRef? reference = viewport.Context.Selection.RefOf(target);
            if (reference != null)
            {
                viewport.Context.Selection.Set(new[] { reference }, op);
            }

            return candidates.Count + " candidate(s), target not under the cursor: selected from the hierarchy";
        }

        private static bool IsPartOf(PickCandidate candidate, GameObject target)
        {
            GameObject? hit = candidate.HitObject;
            return hit != null && (hit.transform.IsChildOf(target.transform) || target.transform.IsChildOf(hit.transform));
        }

        /// <summary>Selects a project asset (the Studio selection mirrors it into Unity's Selection).</summary>
        public static Step SelectAsset(params string[] paths) => new Step("select asset " + string.Join(", ", paths.Select(Path.GetFileNameWithoutExtension)), () =>
        {
            StudioUiContext context = Context;
            List<AuthoringRef> refs = new List<AuthoringRef>();
            List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
            foreach (string path in paths)
            {
                UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path) ?? throw new InvalidOperationException("No asset at " + path);
                objects.Add(asset);
                AuthoringRef reference = context.Selection.RefOf(asset) ?? throw new InvalidOperationException("No authoring ref for " + path);
                refs.Add(reference);
            }

            context.Selection.Set(refs, SelectionOp.Replace);
            Selection.objects = objects.ToArray();
            StudioContextWindow.Open();
            string badges = string.Join(", ", context.Selection.Describe().Select(b => b.Label + " [" + b.TypeId + "]"));
            WorkflowRunner.Shot("select-asset", "Selected " + badges + " (project asset; the context panel shows its generated inspector and tools).", new JObject { ["selection"] = badges });
            return true;
        });

        /// <summary>Adds project assets to the current selection (Ctrl-click in the Project window).</summary>
        public static Step AddAsset(params string[] paths) => new Step("add asset " + string.Join(", ", paths.Select(Path.GetFileNameWithoutExtension)), () =>
        {
            StudioUiContext context = Context;
            foreach (string path in paths)
            {
                UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path) ?? throw new InvalidOperationException("No asset at " + path);
                AuthoringRef reference = context.Selection.RefOf(asset) ?? throw new InvalidOperationException("No authoring ref for " + path);
                context.Selection.Set(new[] { reference }, SelectionOp.Add);
            }

            StudioContextWindow.Open();
            string badges = string.Join(", ", context.Selection.Describe().Select(b => b.Label + " [" + b.TypeId + "]"));
            WorkflowRunner.Shot("add-asset", "Added " + string.Join(", ", paths.Select(Path.GetFileNameWithoutExtension)) + " to the selection: " + badges + ".", new JObject { ["selection"] = badges });
            return true;
        });

        /// <summary>Right-click point-at on the ground near <paramref name="near"/> plus an offset (metres).</summary>
        public static Step PointAt(string near, Vector3 offset) => new Step("point-at near " + near, () =>
        {
            StudioViewportWindow viewport = Viewport();
            GameObject anchor = Require(near);
            Vector3 world = anchor.transform.position + offset;
            FrameOn(new List<GameObject> { anchor }, 1.5f);
            Rect area = viewport.ImageRect;
            List<Vector2> points = new List<Vector2>();
            Vector2? projected = viewport.Project(world);
            if (projected != null && area.Contains(projected.Value))
            {
                points.Add(projected.Value);
            }

            points.Add(new Vector2(area.width * 0.62f, area.height * 0.82f));
            points.Add(new Vector2(area.width * 0.5f, area.height * 0.9f));
            points.Add(new Vector2(area.width * 0.35f, area.height * 0.85f));
            viewport.Overlap?.Hide();
            LocationPick location = viewport.PointAtLocation(points[0]);
            JArray tries = new JArray();
            foreach (Vector2 point in points)
            {
                location = viewport.PointAtLocation(point);
                tries.Add(new JObject { ["point"] = new JArray(Math.Round(point.x, 1), Math.Round(point.y, 1)), ["hit"] = location.Hit, ["source"] = location.Source.ToString() });
                if (location.Hit)
                {
                    break;
                }
            }

            JObject extra = new JObject { ["wanted"] = Vec(world), ["tries"] = tries, ["hit"] = location.Hit, ["position"] = Vec(location.Position), ["source"] = location.Source.ToString(), ["surface"] = location.Surface != null ? location.Surface.name : null };
            St.Set("pointAt", Vec(location.Position));
            WorkflowRunner.Shot("point-at", "Right-click point-at near " + near + ": " + (location.Hit ? location.Source + " location at " + Vec(location.Position) : "no ground hit") + " added to the selection.", extra);
            if (!location.Hit)
            {
                throw new InvalidOperationException("point-at found no location");
            }

            return true;
        });

        public static JArray Vec(Vector3 v) => new JArray(Math.Round(v.x, 3), Math.Round(v.y, 3), Math.Round(v.z, 3));

        // ------------------------------------------------------------------------------------------------ prompt

        /// <summary>
        /// Types <paramref name="text"/> into the prompt bar and presses Send (design worker). With
        /// <paramref name="worker"/> = "mechanism" the text is shown in the bar but the request is built for gc-mechanic
        /// with the same builder and submitted through the same context (the prompt bar has no worker switch).
        /// </summary>
        public static Step Send(string tag, string text, string worker = "design") => new Step("send " + tag, () =>
        {
            StudioUiContext context = Context;
            PromptBar prompt = Prompt;
            prompt.Text = text;
            prompt.Refresh();
            if (prompt.DisabledReason != null && St.Waits++ < 60)
            {
                return false;
            }

            if (prompt.DisabledReason != null)
            {
                throw new InvalidOperationException("Send is disabled: " + prompt.DisabledReason);
            }

            StudioTasksWindow.Open();
            WorkflowRunner.Shot(tag + "-prompt", "Prompt bar: \"" + text + "\" typed with the selection shown in the context panel; Send is enabled.", null);
            JObject r = new JObject { ["prompt"] = text, ["worker"] = worker, ["submittedMs"] = WorkflowRunner.NowMs, ["submittedEditor"] = EditorApplication.timeSinceStartup, ["ids"] = new JArray() };
            PreparedRequest prepared;
            if (worker == "design")
            {
                Task<PromptSubmission?> submit = prompt.SubmitAsync();
                prepared = prompt.LastRequest ?? throw new InvalidOperationException("The prompt bar built no request.");
                ((JArray)r["ids"]!).Add(prepared.ChangeSetId);
                St.SetReq(tag, r);
                LiveRequests.TrackNullable(tag, prepared.ChangeSetId, submit);
            }
            else
            {
                SelectionSnapshot selection = context.Selection.Capture(EditorApplication.isPlaying ? GameCore.Studio.Model.SelectionMode.Play : GameCore.Studio.Model.SelectionMode.Edit);
                prepared = context.Requests.Build(text, selection, IntentOrigin.Agent, null, null, null, worker);
                ((JArray)r["ids"]!).Add(prepared.ChangeSetId);
                St.SetReq(tag, r);
                LiveRequests.Track(tag, prepared.ChangeSetId, context.Submit(prepared));
            }

            RecordRequest(tag, prepared, "request.json");
            WorkflowRunner.Write(tag + "/prompt.txt", text + "\n");
            WorkflowRunner.Recording(true);
            WorkflowRunner.Log(tag + "-sent", "Sent \"" + text + "\" as " + prepared.ChangeSetId + " (worker " + worker + ", slice " + prepared.ContextBytes + " bytes" + (prepared.ContextTruncated ? ", truncated" : string.Empty) + ").", null);
            return true;
        });

        public static void RecordRequest(string tag, PreparedRequest prepared, string file)
        {
            JArray attachments = new JArray();
            foreach (Attachment attachment in prepared.Request.Attachments)
            {
                JObject a = new JObject { ["name"] = attachment.Name, ["mediaType"] = attachment.MediaType, ["role"] = attachment.Role, ["bytes"] = attachment.Data.Length };
                if (attachment.MediaType == "application/json" && attachment.Data.Length < 64 * 1024)
                {
                    a["content"] = JToken.Parse(System.Text.Encoding.UTF8.GetString(attachment.Data));
                }

                attachments.Add(a);
            }

            WorkflowRunner.Json(tag + "/" + file, new JObject
            {
                ["changeSetId"] = prepared.ChangeSetId,
                ["intent"] = StudioJson.ToToken(prepared.Intent),
                ["parent"] = prepared.Parent,
                ["worker"] = prepared.Request.Mode,
                ["toolCatalogRevision"] = prepared.ToolCatalogRevision,
                ["contextBytes"] = prepared.ContextBytes,
                ["contextTruncated"] = prepared.ContextTruncated,
                ["contextOmittedNodes"] = prepared.ContextOmittedNodes,
                ["selection"] = StudioJson.ToToken(prepared.Selection),
                ["attachments"] = attachments,
            });
            WorkflowRunner.Json(tag + "/" + Path.GetFileNameWithoutExtension(file) + "-index-slice.json", StudioJson.ToToken(prepared.Request.ContextSlice));
        }

        /// <summary>The current change-set id of a tag (the follow-up after a clarification).</summary>
        public static string IdOf(string tag) => (string?)St.Req(tag)["id"] ?? LiveRequests.Ids(St.Req(tag)).LastOrDefault() ?? string.Empty;

        public static CandidateEntry? EntryOf(string tag)
        {
            string id = IdOf(tag);
            return id.Length == 0 ? null : Context.Candidates.Find(id);
        }

        private static bool IsFinal(AgentRequestState state)
        {
            return state == AgentRequestState.TaskFailed || state == AgentRequestState.Refused || state == AgentRequestState.Cancelled
                || state == AgentRequestState.Unresolved || state == AgentRequestState.CandidateInvalid || state == AgentRequestState.NeedsClarification;
        }

        /// <summary>
        /// Follows a request in the task tray until it yields a candidate or settles. A clarification is answered once
        /// from the tray with <paramref name="answer"/> (given the question), when provided; the follow-up becomes the
        /// tag's request.
        /// </summary>
        public static Step Await(string tag, Func<string, string>? answer = null, double timeoutSeconds = DefaultTimeoutSeconds) => new Step("await " + tag, () =>
        {
            StudioUiContext context = Context;
            LiveRequests.Collect(tag);
            JObject r = St.Req(tag);
            string? last = LiveRequests.Ids(r).LastOrDefault();
            if (last == null)
            {
                r["result"] = "not_sent";
                St.SetReq(tag, r);
                WorkflowRunner.Log(tag + "-not-sent", "Nothing was sent for " + tag + " (see the previous step).", null);
                return true;
            }

            string id = last;
            r["id"] = id;
            St.SetReq(tag, r);
            TaskRow? row = context.Tasks.Find(id);
            CandidateEntry? entry = context.Candidates.Find(id);
            double waited = EditorApplication.timeSinceStartup - (double)(r["submittedEditor"] ?? EditorApplication.timeSinceStartup);
            bool timedOut = waited > timeoutSeconds;

            if (row != null && entry == null && row.State == AgentRequestState.NeedsClarification && answer != null && r["answered"] == null && !timedOut)
            {
                StudioTasksWindow.Open();
                TaskTrayView tray = EditorWindow.GetWindow<StudioTasksWindow>().View ?? throw new InvalidOperationException("The task tray is not open.");
                tray.Select(row.changeSetId);
                string question = row.question;
                string reply = answer(question);
                WorkflowRunner.Shot(tag + "-clarification", "The worker (" + row.worker + ") asked after " + waited.ToString("0", CultureInfo.InvariantCulture) + " s: \"" + question + "\". Answered from the tray: \"" + reply + "\".", new JObject { ["question"] = question, ["answer"] = reply, ["tasks"] = new JArray(row.taskIds.ToArray()) });
                WorkflowRunner.Json(tag + "/clarification.json", new JObject { ["changeSetId"] = row.changeSetId, ["question"] = question, ["answer"] = reply, ["afterSeconds"] = Math.Round(waited, 1), ["tasks"] = new JArray(row.taskIds.ToArray()) });
                Task<PromptSubmission?> submit = tray.Answer(row, reply);
                TaskRow? followUp = context.Tasks.Rows.LastOrDefault(x => x.parent == row.changeSetId);
                r["answered"] = true;
                r["question"] = question;
                r["answer"] = reply;
                if (followUp != null)
                {
                    ((JArray)r["ids"]!).Add(followUp.changeSetId);
                    r["id"] = followUp.changeSetId;
                    St.SetReq(tag, r);
                    LiveRequests.TrackNullable(tag, followUp.changeSetId, submit);
                    WorkflowRunner.Log(tag + "-answered", "Follow-up " + followUp.changeSetId + " (parent " + row.changeSetId + ").", null);
                }
                else
                {
                    St.SetReq(tag, r);
                    WorkflowRunner.MarkFailed(tag + ": no follow-up row after answering the clarification");
                }

                return false;
            }

            bool final = row != null && IsFinal(row.State);
            if (entry == null && !final && !timedOut)
            {
                if (St.Waits++ % 20 == 0)
                {
                    WorkflowRunner.Log(tag + "-waiting", id + ": " + (row != null ? row.StateLabel + " " + row.localState + " " + row.progress : "no row yet") + " after " + waited.ToString("0", CultureInfo.InvariantCulture) + " s", null);
                }

                return false;
            }

            Settle(tag, id, row, entry, timedOut ? "timeout" : null);
            return true;
        });

        private static void Settle(string tag, string id, TaskRow? row, CandidateEntry? entry, string? why)
        {
            StudioUiContext context = Context;
            JObject r = St.Req(tag);
            long now = WorkflowRunner.NowMs;
            RequestView? view = context.Gateway.Requests.FirstOrDefault(v => v.RequestId == id);
            string result = entry != null ? (entry.Stage == CandidateStage.Invalid ? "candidate_invalid" : "candidate") : why ?? (row != null ? AgentRequestStates.Wire(row.State) : "no_row");
            r["result"] = result;
            r["settledMs"] = now;
            JArray tasks = new JArray();
            foreach (string rid in LiveRequests.Ids(r))
            {
                TaskRow? each = context.Tasks.Find(rid);
                RequestView? v = context.Gateway.Requests.FirstOrDefault(x => x.RequestId == rid);
                foreach (string t in (v?.Tasks ?? (IReadOnlyList<string>)Array.Empty<string>()).Concat(each?.taskIds ?? new List<string>()))
                {
                    if (!tasks.Any(x => (string?)x == t))
                    {
                        tasks.Add(t);
                    }
                }
            }

            r["tasks"] = tasks;
            St.SetReq(tag, r);
            WorkflowRunner.Write(tag + "/task-ids.txt", string.Join("\n", tasks.Select(t => (string)t!)) + "\n");
            JObject outcome = new JObject
            {
                ["result"] = result,
                ["changeSetId"] = id,
                ["requests"] = new JArray(LiveRequests.Ids(r).Select(x => new JValue(x))),
                ["tasks"] = tasks,
                ["row"] = row == null ? null : new JObject
                {
                    ["state"] = row.state,
                    ["label"] = row.StateLabel,
                    ["worker"] = row.worker,
                    ["etosStatus"] = row.etosStatus,
                    ["localState"] = row.localState,
                    ["progress"] = row.progress,
                    ["waitingReason"] = row.waitingReason,
                    ["question"] = row.question,
                    ["parent"] = row.parent,
                    ["diagnostics"] = new JArray(row.Diagnostics().Select(d => StudioJson.ToToken(d)).ToArray()),
                },
                ["view"] = view == null ? null : new JObject
                {
                    ["state"] = view.State,
                    ["taskStatus"] = view.TaskStatus,
                    ["attempt"] = view.Attempt,
                    ["local"] = view.LocalState,
                    ["outcome"] = view.Outcome?.DeepClone(),
                    ["outcomeLabel"] = view.OutcomeLabel,
                    ["diagnostics"] = new JArray(view.Diagnostics.Select(d => StudioJson.ToToken(d)).ToArray()),
                },
            };
            WorkflowRunner.Json(tag + "/outcome.json", outcome);
            if (entry != null)
            {
                WorkflowRunner.Json(tag + "/candidate.json", StudioJson.ToToken(entry.ChangeSet));
                WorkflowRunner.Json(tag + "/candidate-review.json", new JObject
                {
                    ["stage"] = entry.Stage.ToString(),
                    ["gatewayStaged"] = entry.GatewayStaged,
                    ["summary"] = entry.Summary,
                    ["badges"] = new JArray(entry.Badges.ToArray()),
                    ["problems"] = new JArray(entry.Problems.Select(d => StudioJson.ToToken(d)).ToArray()),
                    ["stagedDiagnostics"] = entry.Staged == null ? null : new JArray(entry.Staged.AllDiagnostics.Select(d => StudioJson.ToToken(d)).ToArray()),
                    ["stagedOk"] = entry.Staged?.Ok,
                });
                StudioCandidatesWindow.Open(entry.Id);
            }

            StudioTasksWindow.Open();
            TaskTrayView? tray = EditorWindow.GetWindow<StudioTasksWindow>().View;
            tray?.Select(id);
            Timings(tag);
            double seconds = ((double)(now - (long)(r["submittedMs"] ?? now))) / 1000.0;
            WorkflowRunner.Shot(tag + "-settled", "Request " + id + " settled after " + seconds.ToString("0", CultureInfo.InvariantCulture) + " s: " + result
                + (entry != null ? " (" + entry.Stage + ", " + entry.Summary + ")" : row != null ? " (" + row.StateLabel + (row.question.Length > 0 ? ": \"" + row.question + "\"" : string.Empty) + ")" : string.Empty)
                + "; tasks " + string.Join(",", tasks.Select(t => (string)t!)) + ".", new JObject { ["result"] = result });
            if (entry == null && result != "needs_clarification")
            {
                WorkflowRunner.Log(tag + "-no-candidate", "No candidate for " + tag + ": " + result, null);
            }
        }

        /// <summary>B-AGENT-UX numbers of one tag (timings.json).</summary>
        public static JObject Timings(string tag)
        {
            JObject r = St.Req(tag);
            long submitted = (long?)r["submittedMs"] ?? 0;
            JObject t = new JObject { ["submittedMs"] = submitted };
            foreach (string id in LiveRequests.Ids(r))
            {
                JObject one = new JObject();
                long? accepted = (long?)r["accepted"]?[id]?["atMs"];
                long? first = (long?)r["firstEvent"]?[id];
                one["submitToAcceptedMs"] = accepted.HasValue ? accepted - submitted : null;
                one["submitToFirstEventMs"] = first.HasValue ? first - submitted : null;
                one["refusal"] = r["accepted"]?[id]?["refusal"]?.DeepClone();
                JObject states = new JObject();
                foreach (JProperty p in (r["stateAt"] as JObject ?? new JObject()).Properties())
                {
                    if (p.Name.StartsWith(id + ":", StringComparison.Ordinal))
                    {
                        states[p.Name.Substring(id.Length + 1)] = (long)p.Value - submitted;
                    }
                }

                one["stateMsFromSubmit"] = states;
                t[id] = one;
            }

            long? staged = (long?)r["stagedAtMs"];
            t["submitToCandidateMs"] = staged.HasValue ? staged - submitted : null;
            t["candidateImportMs"] = r["importMs"];
            t["settledMs"] = r["settledMs"] != null ? (long)r["settledMs"]! - submitted : (long?)null;
            JArray lags = r["lags"] as JArray ?? new JArray();
            List<long> sorted = lags.Select(x => (long)x).OrderBy(x => x).ToList();
            t["visibleLagMs"] = new JObject
            {
                ["n"] = sorted.Count,
                ["max"] = sorted.Count > 0 ? sorted[sorted.Count - 1] : (long?)null,
                ["p95"] = sorted.Count > 0 ? sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(sorted.Count * 0.95) - 1)] : (long?)null,
                ["note"] = "Unity receipt of a RequestView minus the companion's updatedAt (same host clock)",
            };
            foreach (string key in new[] { "previewMs", "applyMs", "undoMs", "redoMs", "cancelAckMs", "admitMs", "stageWallMs" })
            {
                if (r[key] != null)
                {
                    t[key] = r[key]!.DeepClone();
                }
            }

            WorkflowRunner.Json(tag + "/timings.json", t);
            return t;
        }

        // ------------------------------------------------------------------------------------------------ review

        /// <summary>Opens the candidate in the panel, stages it when the gateway did not, and shows before/after.</summary>
        public static Step Preview(string tag) => new Step("preview " + tag, () =>
        {
            CandidateEntry entry = EntryOf(tag) ?? throw new InvalidOperationException("No candidate for " + tag + " (" + St.Req(tag)["result"] + ").");
            StudioUiContext context = Context;
            StudioCandidatesWindow.Open(entry.Id);
            if (entry.Stage == CandidateStage.Received)
            {
                context.Candidates.Preview(entry);
            }

            JObject r = St.Req(tag);
            r["previewMs"] = entry.StageMilliseconds.HasValue ? Math.Round(entry.StageMilliseconds.Value, 1) : r["importMs"];
            St.SetReq(tag, r);
            WorkflowRunner.Json(tag + "/staged.json", new JObject
            {
                ["stage"] = entry.Stage.ToString(),
                ["ok"] = entry.Staged?.Ok,
                ["operations"] = entry.Staged == null ? null : new JArray(entry.Staged.Operations.Select(o => new JObject { ["opId"] = o.Operation.OpId, ["tool"] = o.Operation.Tool, ["diagnostics"] = new JArray(o.Diagnostics.Select(d => StudioJson.ToToken(d)).ToArray()) }).ToArray()),
                ["diagnostics"] = entry.Staged == null ? null : new JArray(entry.Staged.AllDiagnostics.Select(d => StudioJson.ToToken(d)).ToArray()),
                ["previewMs"] = r["previewMs"],
            });
            if (entry.Stage == CandidateStage.Previewing)
            {
                context.Candidates.ShowAfter(entry, false);
                Viewport().RenderNow();
                WorkflowRunner.Shot(tag + "-compare-before", "Candidate panel, Compare: before (" + entry.Summary + ").", null);
                context.Candidates.ShowAfter(entry, true);
                Viewport().RenderNow();
            }

            WorkflowRunner.Shot(tag + "-preview", "Candidate panel: " + entry.Stage + " (" + (entry.Staged?.Ok == true ? "ok" : "diagnostics") + ", preview " + r["previewMs"] + " ms); badges " + string.Join(", ", entry.Badges) + "; ghosts in the viewport.", null);
            return true;
        });

        /// <summary>Applies the candidate (policy as given); copies the journal entry.</summary>
        public static Step Apply(string tag, ApplyPolicy policy = ApplyPolicy.AllOrNothing, string label = "apply") => new Step(label + " " + tag, () =>
        {
            CandidateEntry entry = EntryOf(tag) ?? throw new InvalidOperationException("No candidate for " + tag);
            StudioUiContext context = Context;
            if (policy != ApplyPolicy.AllOrNothing)
            {
                context.Candidates.SetPolicy(entry, policy);
            }

            ApplyReport report = context.Candidates.Apply(entry);
            JObject r = St.Req(tag);
            r[label + "Ms"] = entry.ApplyMilliseconds.HasValue ? Math.Round(entry.ApplyMilliseconds.Value, 1) : Math.Round(report.Milliseconds, 1);
            if (label == "apply")
            {
                r["applyMs"] = r[label + "Ms"];
            }

            St.SetReq(tag, r);
            JObject result = new JObject
            {
                ["policy"] = policy.ToString(),
                ["state"] = report.State.ToString(),
                ["ok"] = report.Ok,
                ["rolledBack"] = report.RolledBack,
                ["milliseconds"] = Math.Round(report.Milliseconds, 1),
                ["outcomes"] = new JArray(report.Outcomes.Select(o => StudioJson.ToToken(o)).ToArray()),
                ["diagnostics"] = new JArray(report.Diagnostics.Select(d => StudioJson.ToToken(d)).ToArray()),
            };
            WorkflowRunner.Json(tag + "/" + label + "-report.json", result);
            CopyJournal(tag, entry.Id, label);
            StudioHistoryWindow.Open();
            EditorWindow.GetWindow<StudioHistoryWindow>().View?.Select(entry.Id);
            WorkflowRunner.Shot(tag + "-" + label, "Apply (" + policy + "): " + report.State + " in " + r[label + "Ms"] + " ms; outcomes " + string.Join(", ", report.Outcomes.Select(o => o.OpId + "=" + o.Status)) + ". History lists the entry.", result);
            return true;
        });

        public static Step Undo(string tag) => History(tag, true);

        public static Step Redo(string tag) => History(tag, false);

        private static Step History(string tag, bool undo) => new Step((undo ? "undo " : "redo ") + tag, () =>
        {
            string id = IdOf(tag);
            StudioHistoryWindow.Open();
            HistoryPanelView? panel = EditorWindow.GetWindow<StudioHistoryWindow>().View;
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            HistoryResult result = panel != null ? (undo ? panel.Undo(id) : panel.Redo(id)) : (undo ? Context.Runtime.History.Undo(id) : Context.Runtime.History.Redo(id));
            watch.Stop();
            JObject r = St.Req(tag);
            r[undo ? "undoMs" : "redoMs"] = Math.Round(watch.Elapsed.TotalMilliseconds, 1);
            St.SetReq(tag, r);
            string label = undo ? "undo" : "redo";
            JObject data = new JObject
            {
                ["ok"] = result.Ok,
                ["changeSetId"] = result.ChangeSetId,
                ["state"] = result.State?.ToString(),
                ["milliseconds"] = Math.Round(watch.Elapsed.TotalMilliseconds, 1),
                ["diagnostics"] = new JArray(result.Diagnostics.Select(d => StudioJson.ToToken(d)).ToArray()),
                ["status"] = panel?.StatusText,
            };
            WorkflowRunner.Json(tag + "/" + label + "-result.json", data);
            CopyJournal(tag, id, label);
            panel?.Select(id);
            WorkflowRunner.Shot(tag + "-" + label, "History " + label + ": " + (result.Ok ? result.State?.ToString() : "refused") + " in " + data["milliseconds"] + " ms.", data);
            if (!result.Ok)
            {
                WorkflowRunner.MarkFailed(tag + " " + label + " refused: " + string.Join("; ", result.Diagnostics.Select(d => d.Code + ": " + d.Message)));
            }

            return true;
        });

        public static Step Reject(string tag, string reason) => new Step("reject " + tag, () =>
        {
            CandidateEntry? entry = EntryOf(tag);
            if (entry == null)
            {
                WorkflowRunner.Log(tag + "-reject", "nothing to reject (" + St.Req(tag)["result"] + ")", null);
                return true;
            }

            if (entry.IsOpen)
            {
                Context.Candidates.Reject(entry, reason);
            }

            CopyJournal(tag, entry.Id, "rejected");
            WorkflowRunner.Shot(tag + "-rejected", "Rejected after review (" + reason + "); journal " + Context.Runtime.Journal.Read(entry.Id)?.EffectiveState + "; the scene is unchanged.", null);
            return true;
        });

        public static void CopyJournal(string tag, string id, string label)
        {
            Journal journal = Context.Runtime.Journal;
            string path = journal.PathOf(id);
            if (File.Exists(path))
            {
                WorkflowRunner.Write(tag + "/journal-" + label + ".json", File.ReadAllText(path));
            }
        }

        // ------------------------------------------------------------------------------------------------ checks

        public static string Sha256Of(string assetPath)
        {
            string full = Path.Combine(WorkflowRunner.ProjectRoot, assetPath);
            if (!File.Exists(full))
            {
                return "missing";
            }

            using SHA256 sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(File.ReadAllBytes(full)).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }

        /// <summary>sha256 of each asset (to prove what changed and what did not).</summary>
        public static JObject Hashes(params string[] assetPaths)
        {
            JObject result = new JObject();
            foreach (string path in assetPaths)
            {
                result[path] = Sha256Of(path);
            }

            return result;
        }

        /// <summary>Records the hashes under <paramref name="key"/> (state and a JSON file).</summary>
        public static Step Snapshot(string tag, string key, params string[] assetPaths) => new Step("snapshot " + tag + " " + key, () =>
        {
            AssetDatabase.SaveAssets();
            JObject hashes = Hashes(assetPaths);
            St.Set("hash." + tag + "." + key, hashes);
            WorkflowRunner.Json(tag + "/hashes-" + key + ".json", hashes);
            return true;
        });

        /// <summary>Invokes a reflected Studio tool's static method by its [AuthorOperation] id (verification only).</summary>
        public static object? InvokeTool(string toolId, params object?[] args)
        {
            foreach (System.Reflection.MethodInfo method in TypeCache.GetMethodsWithAttribute<GameCore.Gameplay.Contracts.AuthorOperationAttribute>())
            {
                foreach (object attribute in method.GetCustomAttributes(typeof(GameCore.Gameplay.Contracts.AuthorOperationAttribute), false))
                {
                    if (((GameCore.Gameplay.Contracts.AuthorOperationAttribute)attribute).ToolId == toolId && method.GetParameters().Length == args.Length)
                    {
                        return method.Invoke(null, args);
                    }
                }
            }

            throw new InvalidOperationException("No tool method " + toolId + " with " + args.Length + " parameter(s).");
        }
    }
}

// Hollowmere.P3_2.Workflows - the P3.2 AI-workflow recordings (studio/tools/workflow-p3.2-*.sh). Started interactively
// on the host's display with
//   Unity -projectPath <copy> -executeMethod Hollowmere.P3_2.Workflows.WorkflowRunner.Run
// and GCS_P32_WORKFLOW=<name> GCS_P32_OUT=<dir>. A step machine (step index in SessionState, state in a
// ScriptableSingleton, so it continues across the domain reloads of Play Mode and of a mechanism admission) drives the
// real Studio windows (viewport picking, prompt bar, task tray, candidate panel, history panel) against the real
// gateway (P2.2's EtosAgentGateway, started by its session on load) and the live node. Nothing here edits Studio or
// gameplay code; it only clicks what a creator would click and records what happened:
//   - run-log.jsonl: every step (UTC, caption, extra data);
//   - timeline.jsonl: every RequestView the gateway raised (Unity receipt time vs the companion's updatedAt) and every
//     candidate the gateway staged (import milliseconds);
//   - <tag>/: per request prompt.txt, request.json, index-slice.json, task-ids.txt, candidate.json, outcome.json,
//     journal-*.json, timings.json;
//   - keyframes/*.png: the Studio windows' own pixels at each step (UnityWindowCapture from P2.1, never the desktop);
//   - frames/*.png: the same composition every RecordSeconds while a request is in flight (the driver encodes them as a
//     video).
// The editor exits when done (exit code 0, or 1 when a step threw or a capture failed). The app key is never read here
// (P2.2's session reads its key file) and no ETOS settings page is opened.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Model;
using GameCore.Studio.UI;
using Hollowmere.P2_1.Evidence;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Hollowmere.P3_2.Workflows
{
    /// <summary>One step: returns true to advance, false to be called again on the next pump.</summary>
    public sealed class Step
    {
        public Step(string name, Func<bool> run)
        {
            Name = name;
            Run = run;
        }

        public string Name { get; }

        public Func<bool> Run { get; }
    }

    /// <summary>The step machine.</summary>
    [InitializeOnLoad]
    public static class WorkflowRunner
    {
        private const string StepKey = "GameCore.Studio.P32.Step";
        private const string ActiveKey = "GameCore.Studio.P32.Active";
        private const string FailedKey = "GameCore.Studio.P32.Failed";
        private const double StepSeconds = 1.0;
        private const double RecordSeconds = 2.0;

        private static EtosAgentGateway? _hooked;

        static WorkflowRunner()
        {
            if (SessionState.GetBool(ActiveKey, false))
            {
                EditorApplication.update += Pump;
            }
        }

        /// <summary>The -executeMethod entry.</summary>
        public static void Run()
        {
            string workflow = Environment.GetEnvironmentVariable("GCS_P32_WORKFLOW") ?? "smoke";
            Directory.CreateDirectory(OutputDir);
            Directory.CreateDirectory(Path.Combine(OutputDir, "keyframes"));
            Directory.CreateDirectory(Path.Combine(OutputDir, "frames"));
            P32State.instance.Reset(workflow);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetInt(StepKey, 0);
            SessionState.SetBool(FailedKey, false);
            SessionState.SetBool("GameCore.Studio.P32.FirstRunWas", StudioUiSettings.FirstRunDone);
            StudioUiSettings.FirstRunDone = true;
            Log("start", "workflow " + workflow + " started (Unity " + Application.unityVersion + ", batch " + Application.isBatchMode + ")", null);
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
        }

        /// <summary>Where everything goes (GCS_P32_OUT, else Temp/P3.2-workflows).</summary>
        public static string OutputDir
        {
            get
            {
                string? fromEnv = Environment.GetEnvironmentVariable("GCS_P32_OUT");
                return string.IsNullOrEmpty(fromEnv) ? Path.Combine(ProjectRoot, "Temp", "P3.2-workflows") : fromEnv!;
            }
        }

        public static string ProjectRoot => Directory.GetParent(Application.dataPath)!.FullName;

        public static string Workflow => P32State.instance.Workflow;

        public static int CurrentStep => SessionState.GetInt(StepKey, 0);

        public static string CurrentName { get; private set; } = string.Empty;

        /// <summary>Marks the run failed (exit code 1) without stopping it.</summary>
        public static void MarkFailed(string why)
        {
            SessionState.SetBool(FailedKey, true);
            Log("problem", why, null);
        }

        /// <summary>Starts or stops the frame sequence of the recording.</summary>
        public static void Recording(bool on)
        {
            P32State.instance.Recording = on;
            P32State.instance.NextFrameAt = 0;
        }

        /// <summary>Called on every editor update (fast polling such as voice transcript revisions); not persisted.</summary>
        public static event Action? Watch;

        private static void Pump()
        {
            P32State state = P32State.instance;
            Hook();
            try
            {
                Watch?.Invoke();
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                Debug.LogWarning("[P3.2 workflow] watch: " + error.Message);
            }

            if (state.Recording && EditorApplication.timeSinceStartup >= state.NextFrameAt && !EditorApplication.isCompiling)
            {
                state.NextFrameAt = EditorApplication.timeSinceStartup + RecordSeconds;
                Frame(null);
            }

            if (EditorApplication.timeSinceStartup < state.NextAt || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            IReadOnlyList<Step> steps = Workflows.For(state.Workflow);
            int index = SessionState.GetInt(StepKey, 0);
            if (index >= steps.Count)
            {
                Finish();
                return;
            }

            Step step = steps[index];
            CurrentName = step.Name;
            try
            {
                if (step.Run())
                {
                    SessionState.SetInt(StepKey, index + 1);
                    state.Waits = 0;
                }
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                SessionState.SetBool(FailedKey, true);
                Log("error", step.Name + ": " + error.GetType().Name + ": " + error.Message, new JObject { ["stack"] = error.StackTrace });
                Debug.LogError("[P3.2 workflow] step " + index + " " + step.Name + " failed: " + error);
                SessionState.SetInt(StepKey, index + 1);
                state.Waits = 0;
            }

            state.NextAt = EditorApplication.timeSinceStartup + StepSeconds;
        }

        private static void Finish()
        {
            bool failed = SessionState.GetBool(FailedKey, false);
            Recording(false);
            Log("done", failed ? "finished with problems" : "finished", null);
            StudioUiSettings.FirstRunDone = SessionState.GetBool("GameCore.Studio.P32.FirstRunWas", true);
            SessionState.SetBool(ActiveKey, false);
            EditorApplication.update -= Pump;
            if (Environment.GetEnvironmentVariable("GCS_P32_NO_EXIT") == "1")
            {
                return;
            }

            EditorApplication.Exit(failed ? 1 : 0);
        }

        // ------------------------------------------------------------------------------------------- gateway timeline

        /// <summary>Subscribes the timeline to the live gateway (again after every domain reload or gateway restart).</summary>
        private static void Hook()
        {
            EtosAgentGateway? gateway = EtosStudioSession.Gateway;
            if (gateway == null || ReferenceEquals(gateway, _hooked))
            {
                return;
            }

            if (_hooked != null)
            {
                _hooked.RequestChanged -= OnRequestChanged;
                _hooked.CandidateStaged -= OnCandidateStaged;
            }

            _hooked = gateway;
            gateway.RequestChanged += OnRequestChanged;
            gateway.CandidateStaged += OnCandidateStaged;
            Log("gateway", "timeline subscribed to " + gateway.GetType().Name, null);
        }

        public static long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private static void OnRequestChanged(RequestView view)
        {
            long now = NowMs;
            string? tag = P32State.instance.TagOf(view.RequestId);
            JObject line = new JObject
            {
                ["recvMs"] = now,
                ["kind"] = "request",
                ["tag"] = tag,
                ["requestId"] = view.RequestId,
                ["state"] = view.State,
                ["taskStatus"] = view.TaskStatus,
                ["local"] = view.LocalState,
                ["progress"] = view.Progress,
                ["attempt"] = view.Attempt,
                ["tasks"] = new JArray(view.Tasks.ToArray()),
                ["updatedAt"] = view.UpdatedAt,
                ["createdAt"] = view.CreatedAt,
                ["seq"] = view.Seq,
                ["visibleLagMs"] = view.UpdatedAt > 1_000_000_000_000 ? now - view.UpdatedAt : (long?)null,
            };
            if (view.Outcome != null)
            {
                line["outcome"] = view.Outcome.DeepClone();
            }

            Append("timeline.jsonl", line);
            if (tag != null)
            {
                LiveRequests.OnEvent(tag, view, now);
            }
        }

        private static void OnCandidateStaged(CandidateImport import)
        {
            long now = NowMs;
            string? tag = P32State.instance.TagOf(import.RequestId);
            Append("timeline.jsonl", new JObject
            {
                ["recvMs"] = now,
                ["kind"] = "staged",
                ["tag"] = tag,
                ["requestId"] = import.RequestId,
                ["ok"] = import.Ok,
                ["importMs"] = Math.Round(import.Milliseconds, 1),
                ["diagnostics"] = new JArray(import.Diagnostics.Select(d => StudioJson.ToToken(d)).ToArray()),
            });
            if (tag != null)
            {
                LiveRequests.OnStaged(tag, import, now);
            }
        }

        // ------------------------------------------------------------------------------------------------ evidence

        /// <summary>A keyframe of the Studio windows plus a run-log line.</summary>
        public static void Shot(string name, string caption, JObject? extra = null)
        {
            P32State state = P32State.instance;
            int n = ++state.Shots;
            string file = n.ToString("000", CultureInfo.InvariantCulture) + "-" + Slug(name) + ".png";
            string? problem = Capture(Path.Combine(OutputDir, "keyframes", file));
            JObject line = extra ?? new JObject();
            line["keyframe"] = "keyframes/" + file;
            if (problem != null)
            {
                line["captureProblem"] = problem;
            }

            Frame(null);
            Log(name, caption, line);
        }

        /// <summary>One frame of the recording.</summary>
        public static void Frame(string? label)
        {
            P32State state = P32State.instance;
            int n = ++state.Frames;
            string path = Path.Combine(OutputDir, "frames", "f" + n.ToString("00000", CultureInfo.InvariantCulture) + ".png");
            Capture(path);
        }

        private static string? Capture(string path)
        {
            try
            {
                return UnityWindowCapture.CaptureStudio(path, Environment.GetEnvironmentVariable("GCS_FLIP") == "1");
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                return "capture: " + error.GetType().Name + ": " + error.Message;
            }
        }

        /// <summary>One run-log line (also echoed to the Editor log).</summary>
        public static void Log(string name, string caption, JObject? extra)
        {
            JObject line = extra ?? new JObject();
            line["utc"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            line["step"] = SessionState.GetInt(StepKey, 0);
            line["stepName"] = CurrentName;
            line["name"] = name;
            line["caption"] = caption;
            Append("run-log.jsonl", line);
            Debug.Log("[P3.2 workflow] " + EtosRedaction.Redact(line.ToString(Formatting.None)));
        }

        /// <summary>Writes a JSON (redacted) under the output directory.</summary>
        public static void Json(string relative, JToken content)
        {
            Write(relative, content.ToString(Formatting.Indented) + "\n");
        }

        public static void Write(string relative, string text)
        {
            string path = Path.Combine(OutputDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, EtosRedaction.Redact(text));
        }

        public static void Append(string relative, JObject line)
        {
            string path = Path.Combine(OutputDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, EtosRedaction.Redact(line.ToString(Formatting.None)) + "\n");
        }

        public static string Slug(string text)
        {
            char[] chars = text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
            string slug = new string(chars).Trim('-');
            while (slug.Contains("--"))
            {
                slug = slug.Replace("--", "-");
            }

            return slug.Length > 48 ? slug.Substring(0, 48) : slug;
        }
    }

    /// <summary>Run state that survives domain reloads (in-memory ScriptableSingleton).</summary>
    public sealed class P32State : ScriptableSingleton<P32State>
    {
        [SerializeField] private string workflow = "smoke";
        [SerializeField] private string data = "{}";
        [SerializeField] private int shots;
        [SerializeField] private int frames;
        [SerializeField] private bool recording;

        public string Workflow => workflow;

        public int Shots
        {
            get => shots;
            set => shots = value;
        }

        public int Frames
        {
            get => frames;
            set => frames = value;
        }

        public bool Recording
        {
            get => recording;
            set => recording = value;
        }

        public double NextAt { get; set; }

        public double NextFrameAt { get; set; }

        public int Waits { get; set; }

        public void Reset(string name)
        {
            workflow = name;
            data = "{}";
            shots = 0;
            frames = 0;
            recording = false;
            NextAt = 0;
            NextFrameAt = 0;
            Waits = 0;
        }

        public JObject Data
        {
            get => JObject.Parse(string.IsNullOrEmpty(data) ? "{}" : data);
            set => data = value.ToString(Formatting.None);
        }

        public JToken? Get(string key) => Data[key];

        public string Str(string key) => (string?)Data[key] ?? string.Empty;

        public double Num(string key) => Data[key]?.Type == JTokenType.Float || Data[key]?.Type == JTokenType.Integer ? (double)Data[key]! : 0;

        public void Set(string key, JToken? value)
        {
            JObject all = Data;
            all[key] = value ?? JValue.CreateNull();
            Data = all;
        }

        /// <summary>Per-request record (LiveRequests).</summary>
        public JObject Req(string tag) => (Data["req"]?[tag] as JObject) ?? new JObject();

        public void SetReq(string tag, JObject value)
        {
            JObject all = Data;
            JObject reqs = all["req"] as JObject ?? new JObject();
            reqs[tag] = value;
            all["req"] = reqs;
            Data = all;
        }

        public void MapId(string id, string tag)
        {
            JObject all = Data;
            JObject ids = all["ids"] as JObject ?? new JObject();
            ids[id] = tag;
            all["ids"] = ids;
            Data = all;
        }

        public string? TagOf(string id) => (string?)Data["ids"]?[id];
    }
}

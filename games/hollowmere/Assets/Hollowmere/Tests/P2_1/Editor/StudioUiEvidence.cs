// Hollowmere.P2_1.Evidence - the P2.1 graphical evidence run (studio/tools/evidence-p2.1.sh). Started interactively on
// the host's display with -executeMethod Hollowmere.P2_1.Evidence.StudioUiEvidence.Run (not batch mode). A step machine
// (step index in SessionState, so it continues across the domain reload of entering Play Mode) opens Thornwick Village,
// lays out the Studio, then: first-run guide, Select click on the Village Well, Inspect hover card, marquee, point-at
// marker, a canned candidate (a move of the well, built locally as evidence input) previewed with its ghost, applied
// and undone from History, the prompt bar's disabled reason without a gateway, then Play Mode from Boot.unity with the
// viewport in Play mode on the player camera, W held through the Input System (the player must move), the pump
// indicator, Maren (patrolling NPC) and the well selected in Play, and Pause. After each step the display is
// grabbed with ffmpeg (x11grab) into GCS_EVIDENCE_DIR; evidence-log.jsonl records the step, time, pump readout and
// B-SELECT timings. The editor exits when done (exit code 0, or 1 when a step failed).
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using GameCore.Studio.Authoring;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using GameCore.Studio.UI;
using GameCore.Unity.App;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Debug = UnityEngine.Debug;

namespace Hollowmere.P2_1.Evidence
{
    /// <summary>The evidence step machine.</summary>
    [InitializeOnLoad]
    public static class StudioUiEvidence
    {
        public const string VillageScene = "Assets/Hollowmere/Regions/ThornwickVillage.unity";
        public const string BootScene = "Assets/Hollowmere/Boot/Boot.unity";
        private const string StepKey = "GameCore.Studio.P21.Evidence.Step";
        private const string ActiveKey = "GameCore.Studio.P21.Evidence.Active";
        private const string FailedKey = "GameCore.Studio.P21.Evidence.Failed";
        private const string FirstRunKey = "GameCore.Studio.P21.Evidence.FirstRunWas";
        private const double StepSeconds = 2.5;

        static StudioUiEvidence()
        {
            if (SessionState.GetBool(ActiveKey, false))
            {
                EditorApplication.update += Pump;
            }
        }

        /// <summary>The -executeMethod entry.</summary>
        public static void Run()
        {
            Directory.CreateDirectory(OutputDir);
            File.WriteAllText(LogPath, string.Empty);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetInt(StepKey, 0);
            SessionState.SetBool(FailedKey, false);
            SessionState.SetBool(FirstRunKey, StudioUiSettings.FirstRunDone);
            StudioUiSettings.FirstRunDone = false;
            EvidenceClock.instance.Reset();
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
        }

        /// <summary>Where screenshots and the log go (GCS_EVIDENCE_DIR, else Temp/P2.1-evidence).</summary>
        public static string OutputDir
        {
            get
            {
                string? fromEnv = Environment.GetEnvironmentVariable("GCS_EVIDENCE_DIR");
                return string.IsNullOrEmpty(fromEnv) ? Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Temp", "P2.1-evidence") : fromEnv!;
            }
        }

        private static string LogPath => Path.Combine(OutputDir, "evidence-log.jsonl");

        private static void Pump()
        {
            EvidenceClock clock = EvidenceClock.instance;
            if (EditorApplication.timeSinceStartup < clock.NextAt || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            int step = SessionState.GetInt(StepKey, 0);
            try
            {
                bool advance = RunStep(step);
                if (advance)
                {
                    SessionState.SetInt(StepKey, step + 1);
                }
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                SessionState.SetBool(FailedKey, true);
                Log(step, "error", error.GetType().Name + ": " + error.Message, null);
                Debug.LogError("[P2.1 evidence] step " + step + " failed: " + error);
                SessionState.SetInt(StepKey, step + 1);
            }

            clock.NextAt = EditorApplication.timeSinceStartup + StepSeconds;
        }

        /// <summary>Runs one step; false to retry the same step later (waiting for Play Mode, the world, layout).</summary>
        private static bool RunStep(int step)
        {
            EvidenceClock clock = EvidenceClock.instance;
            switch (step)
            {
                case 0:
                    EditorSceneManager.OpenScene(VillageScene, OpenSceneMode.Single);
                    StudioMenu.OpenStudio();
                    return true;
                case 1:
                {
                    StudioViewportWindow viewport = Viewport();
                    viewport.SetMode(ViewportMode.Select);
                    GameObject? target = PlacedEntity(viewport);
                    if (target != null)
                    {
                        clock.TargetName = target.name;
                        FrameOn(viewport, target);
                    }

                    Shot(step, "first-run", "First-run guide over the Studio layout (viewport, context, tasks, candidates, history).");
                    return true;
                }

                case 2:
                    CloseFirstRun();
                    Shot(step, "studio-layout", "GameCore/Studio/Open Studio over Thornwick Village (Edit mode, free camera framed on " + clock.TargetName + ").");
                    return true;
                case 3:
                {
                    StudioViewportWindow viewport = Viewport();
                    GameObject? target = Find(clock.TargetName);
                    Rect? rect = target == null ? null : viewport.ScreenRectOf(target);
                    if (rect == null)
                    {
                        throw new InvalidOperationException("No placed entity on screen.");
                    }

                    IReadOnlyList<PickCandidate> candidates = viewport.ClickAt(rect.Value.center, SelectionOp.Replace);
                    StudioContextWindow.Open();
                    Shot(step, "select-click", "Select mode: click on " + clock.TargetName + " (" + candidates.Count + " candidate(s)); the context panel shows the generated inspector and tools.");
                    return true;
                }

                case 4:
                {
                    StudioViewportWindow viewport = Viewport();
                    viewport.SetMode(ViewportMode.Inspect);
                    GameObject? target = Find(clock.TargetName);
                    Rect? rect = target == null ? null : viewport.ScreenRectOf(target);
                    if (rect != null)
                    {
                        viewport.HoverAt(rect.Value.center);
                    }

                    Shot(step, "inspect-hover", "Inspect mode: hover card with type id, name, authoring id, definition@revision and stale badge.");
                    return true;
                }

                case 5:
                {
                    StudioViewportWindow viewport = Viewport();
                    viewport.SetMode(ViewportMode.Select);
                    Rect area = viewport.ImageRect;
                    PickResult result = viewport.MarqueeSelect(new Rect(area.width * 0.1f, area.height * 0.1f, area.width * 0.8f, area.height * 0.8f), SelectionOp.Replace, false);
                    Shot(step, "marquee", "Marquee (partial containment): " + result.Targets().Count + " object(s) selected; badges show residency/stale state.");
                    return true;
                }

                case 6:
                {
                    StudioViewportWindow viewport = Viewport();
                    Rect area = viewport.ImageRect;
                    LocationPick location = viewport.PointAtLocation(new Vector2(area.width * 0.35f, area.height * 0.85f));
                    Shot(step, "point-at", "Right-click point-at: " + (location.Hit ? location.Source + " location marker" : "no ground hit") + " added to the selection.");
                    return true;
                }

                case 7:
                {
                    StudioViewportWindow viewport = Viewport();
                    GameObject? target = Find(clock.TargetName) ?? throw new InvalidOperationException("The target disappeared.");
                    StudioUiContext context = viewport.Context;
                    context.Selection.Set(new[] { context.Selection.RefOf(target)! });
                    ChangeSet typed = MoveChangeSets.Build(context.Runtime, target, target.transform.position + new Vector3(2.5f, 0f, 0f))
                        ?? throw new InvalidOperationException("No move change set for " + target.name);
                    ChangeSet candidate = new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId, new Intent("Move " + target.name + " closer to the well (P2.1 evidence candidate)", IntentOrigin.Agent), typed.Operations,
                        requirements: CandidateRequirements.Implied(context.Runtime, typed));
                    ToolCatalog catalog = context.Runtime.Registry.Catalog;
                    CandidateEntry entry = context.Candidates.Add(new AgentCandidate("req_p21_evidence", candidate, catalog.Revision ?? catalog.ComputeRevision()));
                    clock.CandidateId = entry.Id;
                    StagedChangeSet staged = context.Candidates.Preview(entry);
                    StudioCandidatesWindow.Open(entry.Id);
                    Shot(step, "candidate-preview", "Candidate panel: Preview staged (" + (staged.Ok ? "ok" : "diagnostics") + ", " + Ms(entry.StageMilliseconds) + "), ghost in the viewport, strip bottom-right.");
                    return true;
                }

                case 8:
                {
                    StudioUiContext context = Viewport().Context;
                    CandidateEntry entry = context.Candidates.Find(clock.CandidateId) ?? throw new InvalidOperationException("The candidate disappeared.");
                    ApplyReport report = context.Candidates.Apply(entry);
                    StudioHistoryWindow.Open();
                    Shot(step, "candidate-applied", "Apply (" + report.State + " in " + Ms(entry.ApplyMilliseconds) + "); the History panel lists the agent entry.");
                    return true;
                }

                case 9:
                {
                    HistoryPanelView? history = EditorWindow.GetWindow<StudioHistoryWindow>().View;
                    HistoryResult result = history != null ? history.Undo(null) : Viewport().Context.Runtime.History.Undo();
                    Shot(step, "history-undo", "History Undo: " + (result.Ok ? "reverted " + result.ChangeSetId : "refused") + ".");
                    return true;
                }

                case 10:
                {
                    StudioViewportWindow viewport = Viewport();
                    if (viewport.Prompt != null)
                    {
                        viewport.Prompt.Text = "Make these villagers greet the player";
                    }

                    Shot(step, "prompt-bar", "Prompt bar with provider chips; Send is disabled with the reason (no gateway registered on this host run).");
                    return true;
                }

                case 11:
                    EditorSceneManager.OpenScene(BootScene, OpenSceneMode.Single);
                    EditorApplication.EnterPlaymode();
                    return true;
                case 12:
                {
                    if (!EditorApplication.isPlaying)
                    {
                        return false;
                    }

                    GameApplicationRoot? root = GameApplication.Current;
                    if ((root == null || root.State != GameApplicationState.Running) && clock.Waits++ < 12)
                    {
                        return false;
                    }

                    clock.Waits = 0;
                    StudioViewportWindow viewport = Viewport();
                    viewport.Renderer.ForceFreeCamera = false;
                    viewport.SetMode(ViewportMode.Play);
                    viewport.Focus();
                    CharacterController? player = UnityEngine.Object.FindAnyObjectByType<CharacterController>();
                    clock.PlayerStart = player != null ? player.transform.position : Vector3.zero;
                    clock.HasPlayer = player != null;
                    Keyboard keyboard = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
                    Log(step, "input", "Viewport focused in Play mode; W held through the Input System (player " + (player != null ? player.name : "not found") + ").", null);
                    return true;
                }

                case 13:
                {
                    StudioViewportWindow viewport = Viewport();
                    viewport.Focus();
                    CharacterController? player = UnityEngine.Object.FindAnyObjectByType<CharacterController>();
                    float moved = player != null && clock.HasPlayer ? Vector3.Distance(player.transform.position, clock.PlayerStart) : -1f;
                    Shot(step, "play-mode", "Play mode in the viewport: the player camera (ThirdPersonCamera's camera), input routed to the game"
                        + " (routing " + (viewport.Routing.Active ? "active" : "inactive") + ", " + viewport.Routing.EventsRouted + " Input System event(s)); W held for "
                        + StepSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s moved the player " + (moved < 0f ? "n/a" : moved.ToString("0.00", CultureInfo.InvariantCulture) + " m")
                        + "; pump " + viewport.Pump.Text() + ".");
                    Keyboard? keyboard = Keyboard.current;
                    if (keyboard != null)
                    {
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    }

                    return true;
                }

                case 14:
                {
                    StudioViewportWindow viewport = Viewport();
                    GameObject? maren = FindByName("Maren");
                    GameObject? well = FindByName("Well");
                    viewport.SetMode(ViewportMode.Select);
                    if (maren != null)
                    {
                        FrameOn(viewport, maren, well);
                    }

                    List<string> picked = new List<string>();
                    foreach ((GameObject? target, SelectionOp op) in new[] { (maren, SelectionOp.Replace), (well, SelectionOp.Add) })
                    {
                        Rect? rect = target == null ? null : viewport.ScreenRectOf(target);
                        if (rect == null)
                        {
                            picked.Add((target != null ? target.name : "?") + " off screen");
                            continue;
                        }

                        IReadOnlyList<PickCandidate> candidates = viewport.ClickAt(rect.Value.center, op);
                        picked.Add(target!.name + " (" + candidates.Count + " candidate(s))");
                    }

                    viewport.Overlap?.Hide();
                    StringBuilder selected = new StringBuilder();
                    foreach (SelectionBadge badge in viewport.Context.Selection.Describe())
                    {
                        selected.Append(selected.Length == 0 ? string.Empty : ", ").Append(badge.Label).Append(" [").Append(badge.TypeId).Append(']');
                    }

                    Shot(step, "play-select", "Select in Play (free camera framed on Maren): clicked " + string.Join(", ", picked) + "; selection: " + selected + ".");
                    return true;
                }

                case 15:
                {
                    StudioViewportWindow viewport = Viewport();
                    GameApplicationRoot? root = GameApplication.Current;
                    if (root != null && root.State == GameApplicationState.Running)
                    {
                        root.Pause();
                    }

                    Shot(step, "paused", "Pause bound to the application root; pump " + viewport.Pump.Text() + ".");
                    return true;
                }

                case 16:
                    if (EditorApplication.isPlaying)
                    {
                        EditorApplication.ExitPlaymode();
                    }

                    return true;
                case 17:
                {
                    if (EditorApplication.isPlaying)
                    {
                        return false;
                    }

                    Finish();
                    return true;
                }

                default:
                    return true;
            }
        }

        private static void Finish()
        {
            StudioUiSettings.FirstRunDone = SessionState.GetBool(FirstRunKey, false);
            bool failed = SessionState.GetBool(FailedKey, false);
            Log(99, "done", failed ? "finished with failures" : "finished", null);
            SessionState.SetBool(ActiveKey, false);
            EditorApplication.update -= Pump;
            EditorApplication.Exit(failed ? 1 : 0);
        }

        private static StudioViewportWindow Viewport()
        {
            StudioViewportWindow viewport = StudioViewportWindow.Open();
            viewport.EnsureGui();
            return viewport;
        }

        private static void CloseFirstRun()
        {
            if (EditorWindow.HasOpenInstances<FirstRunWizardWindow>())
            {
                EditorWindow.GetWindow<FirstRunWizardWindow>().Close();
            }
        }

        private static GameObject? PlacedEntity(StudioViewportWindow viewport)
        {
            StudioRuntime runtime = viewport.Context.Runtime;
            GameObject? fallback = null;
            foreach (Collider collider in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.InstanceID))
            {
                if (collider.bounds.size.magnitude > 40f)
                {
                    continue;
                }

                Transform? current = collider.transform;
                while (current != null)
                {
                    MonoBehaviour? authored = runtime.Identity.FindAuthoredComponent(current.gameObject);
                    if (authored != null && runtime.Identity.Describe(authored)?.TypeId == "entity.instance")
                    {
                        string lower = current.name.ToLowerInvariant();
                        if (lower == "well" || lower.Contains("village well"))
                        {
                            return current.gameObject;
                        }

                        fallback ??= current.gameObject;
                        break;
                    }

                    current = current.parent;
                }
            }

            return fallback;
        }

        private static void FrameOn(StudioViewportWindow viewport, GameObject target, GameObject? also = null)
        {
            Bounds bounds = new Bounds(target.transform.position, Vector3.one * 2f);
            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>())
            {
                bounds.Encapsulate(renderer.bounds);
            }

            if (also != null)
            {
                bounds.Encapsulate(also.transform.position);
            }

            bounds.Expand(bounds.size.magnitude * 1.5f);
            viewport.Renderer.ForceFreeCamera = true;
            viewport.Renderer.Frame(bounds);
            viewport.RenderNow();
        }

        private static GameObject? Find(string name) => string.IsNullOrEmpty(name) ? null : GameObject.Find(name);

        /// <summary>The first active scene object whose name contains <paramref name="fragment"/> and that has a renderer.</summary>
        private static GameObject? FindByName(string fragment)
        {
            foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.InstanceID))
            {
                Transform? current = renderer.transform;
                while (current != null)
                {
                    if (current.name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return current.gameObject;
                    }

                    current = current.parent;
                }
            }

            return null;
        }

        private static string Ms(double? value) => value.HasValue ? value.Value.ToString("0.0", CultureInfo.InvariantCulture) + " ms" : "n/a";

        private static void Shot(int step, string name, string caption)
        {
            string file = step.ToString("00", CultureInfo.InvariantCulture) + "-" + name + ".png";
            string path = Path.Combine(OutputDir, file);
            string display = Environment.GetEnvironmentVariable("DISPLAY") ?? ":1";
            string? size = Environment.GetEnvironmentVariable("GCS_SCREEN");
            string arguments = "-y -loglevel error -f x11grab" + (string.IsNullOrEmpty(size) ? string.Empty : " -video_size " + size) + " -i " + display + " -frames:v 1 \"" + path + "\"";
            string? problem = null;
            try
            {
                using Process process = Process.Start(new ProcessStartInfo("ffmpeg", arguments) { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true })!;
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit(15000);
                if (process.ExitCode != 0)
                {
                    problem = "ffmpeg exit " + process.ExitCode + ": " + stderr.Trim();
                }
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                problem = "ffmpeg: " + error.Message;
            }

            StudioViewportWindow? viewport = EditorWindow.HasOpenInstances<StudioViewportWindow>() ? EditorWindow.GetWindow<StudioViewportWindow>(StudioWindowIds.ViewportTitle, false) : null;
            JObject extra = new JObject
            {
                ["file"] = file,
                ["pixelsPerPoint"] = EditorGUIUtility.pixelsPerPoint,
                ["playing"] = EditorApplication.isPlaying,
            };
            if (viewport != null)
            {
                extra["pump"] = viewport.Pump.Report();
                extra["bSelect"] = viewport.Timings.Report();
                extra["render"] = viewport.Renderer.LastRenderMs.ToString("0.00", CultureInfo.InvariantCulture) + " ms";
                extra["mode"] = viewport.Mode.ToString();
                RenderTexture? texture = viewport.Texture;
                if (texture != null)
                {
                    extra["texture"] = texture.width + "x" + texture.height;
                }

                UnityEngine.UIElements.VisualElement? area = viewport.Area;
                if (area != null)
                {
                    extra["area"] = area.contentRect.width.ToString("0", CultureInfo.InvariantCulture) + "x" + area.contentRect.height.ToString("0", CultureInfo.InvariantCulture);
                }
            }

            if (problem != null)
            {
                extra["problem"] = problem;
                SessionState.SetBool(FailedKey, true);
            }

            Log(step, name, caption, extra);
        }

        private static void Log(int step, string name, string caption, JObject? extra)
        {
            JObject line = extra ?? new JObject();
            line["step"] = step;
            line["name"] = name;
            line["caption"] = caption;
            line["utc"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            File.AppendAllText(LogPath, line.ToString(Newtonsoft.Json.Formatting.None) + "\n");
            Debug.Log("[P2.1 evidence] " + line.ToString(Newtonsoft.Json.Formatting.None));
        }
    }

    /// <summary>Per-domain evidence timing and the names the steps hand to each other (re-seeded after a reload).</summary>
    public sealed class EvidenceClock : ScriptableSingleton<EvidenceClock>
    {
        [SerializeField]
        private string targetName = string.Empty;

        [SerializeField]
        private string candidateId = string.Empty;

        public double NextAt { get; set; }

        public int Waits { get; set; }

        [SerializeField]
        private Vector3 playerStart;

        [SerializeField]
        private bool hasPlayer;

        public Vector3 PlayerStart
        {
            get => playerStart;
            set => playerStart = value;
        }

        public bool HasPlayer
        {
            get => hasPlayer;
            set => hasPlayer = value;
        }

        public string TargetName
        {
            get => targetName;
            set => targetName = value ?? string.Empty;
        }

        public string CandidateId
        {
            get => candidateId;
            set => candidateId = value ?? string.Empty;
        }

        public void Reset()
        {
            NextAt = 0;
            Waits = 0;
            targetName = string.Empty;
            candidateId = string.Empty;
        }
    }
}

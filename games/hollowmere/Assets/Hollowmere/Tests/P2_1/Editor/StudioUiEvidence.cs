// Hollowmere.P2_1.Evidence - the P2.1 graphical evidence run (studio/tools/evidence-p2.1.sh). Started interactively on
// the host's display with -executeMethod Hollowmere.P2_1.Evidence.StudioUiEvidence.Run (not batch mode). A step machine
// (step index in SessionState, so it continues across the domain reload of entering Play Mode) opens Thornwick Village,
// lays out the Studio, then: first-run guide, Select click on the Village Well, Inspect hover card, marquee, point-at
// marker, a canned candidate (a move of the well, built locally as evidence input) previewed with its ghost, applied
// and undone from History, then one live prompt through the real gateway (P2.2's EtosAgentGateway; the well selected,
// the companion's gc-designer answers, the etos gateway stages the candidate, the panel adopts it, the run rejects it so
// the scene stays as it was; the key never appears on screen), then Play Mode from Boot.unity with the
// viewport in Play mode on the player camera, W held through the Input System (the player must move), the pump
// indicator, Maren (patrolling NPC) and the well selected in Play, and Pause. After each step the Studio windows' own
// pixels are composed into a PNG in GCS_EVIDENCE_DIR (UnityWindowCapture; the desktop is never grabbed);
// evidence-log.jsonl records the step, time, pump readout and B-SELECT timings. The editor exits when done (exit code
// 0, or 1 when a step failed).
#nullable enable
using System;
using System.Collections.Generic;
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

        /// <summary>The one prompt sent to the real gateway (gc-designer), with the Village Well selected.</summary>
        private const string LivePrompt = "Move this well one metre to the east";

        /// <summary>The answer given when the worker asks for a clarification.</summary>
        private const string LiveAnswer = "East is +X in this project (scene-context.json axes). Move the Village Well exactly 1 metre along +X; change nothing else.";

        /// <summary>How long the live request may take before the run records it as unanswered.</summary>
        private const double LiveTimeoutSeconds = 540;

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
                    // The layout tiles over a 1600x900 area (the host's main editor window may be small).
                    StudioMenu.OpenStudio(new Rect(40f, 40f, 1600f, 900f), true);
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
                    StudioUiSettings.FirstRunDone = true;
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

                    string how = SelectTarget(viewport, target!, rect.Value.center, SelectionOp.Replace);
                    StudioContextWindow.Open();
                    Shot(step, "select-click", "Select mode: click on " + clock.TargetName + " (" + how + "); the overlap list shows every object under the cursor and the context panel the generated inspector and tools.");
                    return true;
                }

                case 4:
                {
                    StudioViewportWindow viewport = Viewport();
                    viewport.Overlap?.Hide();
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
                    GameObject? well = Find(clock.TargetName);
                    Vector2? ground = well == null ? null : viewport.Project(well.transform.position + new Vector3(2.5f, 0f, -1.5f));
                    Vector2 point = ground.HasValue && area.Contains(ground.Value) ? ground.Value : new Vector2(area.width * 0.5f, area.height * 0.9f);
                    viewport.Overlap?.Hide();
                    LocationPick location = viewport.PointAtLocation(point);
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
                    CandidateEntry entry = context.Candidates.Add("req_p21_evidence", candidate, catalog.Revision ?? catalog.ComputeRevision());
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
                    // The real gateway (P2.2's EtosAgentGateway, registered by its session on load) must report the
                    // companion's agent as connected before the prompt can be sent.
                    StudioViewportWindow viewport = Viewport();
                    GameObject? target = Find(clock.TargetName) ?? throw new InvalidOperationException("The target disappeared.");
                    viewport.Context.Selection.Set(new[] { viewport.Context.Selection.RefOf(target)! });
                    PromptBar prompt = viewport.Prompt ?? throw new InvalidOperationException("The viewport has no prompt bar.");
                    prompt.Text = LivePrompt;
                    prompt.Refresh();
                    ProviderStatus status = viewport.Context.Gateway.Status;
                    if (prompt.DisabledReason != null && clock.Waits++ < 48)
                    {
                        if (clock.Waits % 8 == 0)
                        {
                            Debug.Log("[P2.1 evidence] waiting for the gateway: " + prompt.DisabledReason);
                        }

                        return false;
                    }

                    clock.Waits = 0;
                    Shot(step, "prompt-bar", "Prompt bar with the live provider chips (" + ProviderNames.ConnectionOf(status) + ", gateway " + viewport.Context.Gateway.GetType().Name + "); "
                        + (prompt.DisabledReason == null ? "Send is enabled for \"" + LivePrompt + "\" with the well selected." : "Send is disabled: " + prompt.DisabledReason));
                    return true;
                }

                case 11:
                {
                    StudioViewportWindow viewport = Viewport();
                    PromptBar prompt = viewport.Prompt ?? throw new InvalidOperationException("The viewport has no prompt bar.");
                    if (prompt.DisabledReason != null)
                    {
                        throw new InvalidOperationException("The live prompt cannot be sent: " + prompt.DisabledReason);
                    }

                    clock.LiveStartedAt = EditorApplication.timeSinceStartup;
                    _ = prompt.SubmitAsync();
                    clock.LiveId = prompt.LastRequest?.ChangeSetId ?? string.Empty;
                    StudioTasksWindow.Open();
                    Log(step, "live-submit", "Submitted \"" + LivePrompt + "\" through the real gateway as " + clock.LiveId + " (slice " + prompt.LastRequest?.ContextBytes + " bytes).", null);
                    return true;
                }

                case 12:
                {
                    StudioUiContext context = Viewport().Context;
                    TaskRow? row = context.Tasks.Find(clock.LiveId);
                    CandidateEntry? entry = context.Candidates.Find(clock.LiveId);
                    bool final = row != null && (row.State == AgentRequestState.TaskFailed || row.State == AgentRequestState.Refused || row.State == AgentRequestState.Cancelled
                        || row.State == AgentRequestState.Unresolved || row.State == AgentRequestState.CandidateInvalid || row.State == AgentRequestState.NeedsClarification);
                    double waited = EditorApplication.timeSinceStartup - clock.LiveStartedAt;
                    if (row != null && entry == null && row.State == AgentRequestState.NeedsClarification && !clock.Answered && waited < LiveTimeoutSeconds)
                    {
                        // The worker asked a question: answer it from the tray as a creator would (a new request whose
                        // parent is the first one), then keep waiting for the follow-up's candidate.
                        StudioTasksWindow.Open();
                        TaskTrayView? tray = EditorWindow.GetWindow<StudioTasksWindow>().View;
                        if (tray == null)
                        {
                            throw new InvalidOperationException("The task tray is not open.");
                        }

                        tray.Select(row.changeSetId);
                        Shot(step, "live-clarification", "The worker (" + row.worker + ") asked for a clarification after " + waited.ToString("0", CultureInfo.InvariantCulture) + " s: \"" + row.question + "\"; the tray shows it with the inline answer.");
                        string parent = row.changeSetId;
                        _ = tray.Answer(row, LiveAnswer);
                        TaskRow? followUp = null;
                        foreach (TaskRow candidateRow in context.Tasks.Rows)
                        {
                            if (candidateRow.parent == parent)
                            {
                                followUp = candidateRow;
                            }
                        }

                        clock.Answered = true;
                        clock.LiveId = followUp?.changeSetId ?? clock.LiveId;
                        Log(step, "live-answer", "Answered \"" + LiveAnswer + "\" as " + clock.LiveId + " (parent " + parent + ").", null);
                        return false;
                    }

                    if (entry == null && !final && waited < LiveTimeoutSeconds)
                    {
                        if (clock.Waits++ % 8 == 0)
                        {
                            Debug.Log("[P2.1 evidence] live request " + clock.LiveId + ": " + (row != null ? row.StateLabel + " " + row.localState + " " + row.progress : "no row") + " after " + waited.ToString("0", CultureInfo.InvariantCulture) + " s");
                        }

                        return false;
                    }

                    clock.Waits = 0;
                    if (entry != null)
                    {
                        StudioCandidatesWindow.Open(entry.Id);
                    }

                    StudioTasksWindow.Open();
                    string outcome = entry != null
                        ? "candidate " + entry.Stage + (entry.GatewayStaged ? " (staged by the etos gateway, adopted by the panel)" : string.Empty) + ": " + entry.Summary
                        : row != null ? "request " + row.StateLabel + (row.question.Length > 0 ? " (\"" + row.question + "\")" : string.Empty) + (row.Diagnostics().Count > 0 ? " - " + row.Diagnostics()[0].Code + ": " + row.Diagnostics()[0].Message : string.Empty) : "no answer";
                    Shot(step, "live-request", "Live gateway round trip for \"" + LivePrompt + "\" (" + clock.LiveId + ") after " + waited.ToString("0", CultureInfo.InvariantCulture) + " s: " + outcome
                        + (row != null ? "; worker " + row.worker + ", etos " + row.etosStatus + ", tasks " + string.Join(",", row.taskIds) : string.Empty) + ".");
                    if (entry == null)
                    {
                        SessionState.SetBool(FailedKey, true);
                    }

                    return true;
                }

                case 13:
                {
                    StudioUiContext context = Viewport().Context;
                    CandidateEntry? entry = context.Candidates.Find(clock.LiveId);
                    if (entry != null && entry.IsOpen)
                    {
                        context.Candidates.Reject(entry, "P2.1 evidence run: reviewed, not applied.");
                    }

                    Log(step, "live-reject", entry != null ? "The live candidate was reviewed and rejected (journal " + context.Runtime.Journal.Read(entry.Id)?.EffectiveState + "); the scene is unchanged." : "No live candidate to reject.", null);
                    return true;
                }

                case 14:
                    EditorSceneManager.OpenScene(BootScene, OpenSceneMode.Single);
                    EditorApplication.EnterPlaymode();
                    return true;
                case 15:
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

                case 16:
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

                case 17:
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

                        picked.Add(target!.name + " (" + SelectTarget(viewport, target!, rect.Value.center, op) + ")");
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

                case 18:
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

                case 19:
                    if (EditorApplication.isPlaying)
                    {
                        EditorApplication.ExitPlaymode();
                    }

                    return true;
                case 20:
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

        /// <summary>
        /// Clicks at <paramref name="point"/>; when the nearest candidate is not <paramref name="target"/>, picks the target
        /// from the overlap list as a user would. Returns how it was selected.
        /// </summary>
        private static string SelectTarget(StudioViewportWindow viewport, GameObject target, Vector2 point, SelectionOp op)
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

            return candidates.Count + " candidate(s), target not under the cursor";
        }

        private static bool IsPartOf(PickCandidate candidate, GameObject target)
        {
            GameObject? hit = candidate.HitObject;
            return hit != null && (hit.transform.IsChildOf(target.transform) || target.transform.IsChildOf(hit.transform));
        }

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
            string? problem;
            try
            {
                problem = UnityWindowCapture.CaptureStudio(path, Environment.GetEnvironmentVariable("GCS_FLIP") == "1");
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                problem = "capture: " + error.GetType().Name + ": " + error.Message;
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

        [SerializeField]
        private string liveId = string.Empty;

        [SerializeField]
        private double liveStartedAt;

        [SerializeField]
        private bool answered;

        public bool Answered
        {
            get => answered;
            set => answered = value;
        }

        public string LiveId
        {
            get => liveId;
            set => liveId = value ?? string.Empty;
        }

        public double LiveStartedAt
        {
            get => liveStartedAt;
            set => liveStartedAt = value;
        }

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
            liveId = string.Empty;
            liveStartedAt = 0;
            answered = false;
        }
    }
}

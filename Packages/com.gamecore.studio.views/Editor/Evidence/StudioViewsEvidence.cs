// GameCore.Studio.Views - graphical evidence for P2.3 (studio/tools/evidence-p2.3.sh runs it on the host's display :1).
//
//   Unity -projectPath games/hollowmere -executeMethod GameCore.Studio.Views.Evidence.StudioViewsEvidence.Run \
//         -p23Out <dir> [-p23NoPlay]
//
// A frame-driven script (EditorApplication.update): it opens ThornwickVillage, builds a Studio runtime over Hollowmere
// with a temporary state root, opens each view in a floating 1280x720 window, sets it up (roots, graph, preview,
// simulation, inspected candidate...), waits for layout and repaint, and captures the window's screen pixels
// (InternalEditorUtility.ReadScreenPixel) to <dir>/<shot>.png, downscaled until it is at most 300 KB. When the read
// returns a blank image, it writes <dir>/.request-<shot>.json with the window rect and waits for the shell script to
// capture it with `import -window root -crop`. A synthetic 2,000-node graph is shown on the canvas with its frame
// timings. Then (unless -p23NoPlay) it enters Play Mode in Boot.unity and captures the Quests and World views with the
// running world's overlays, resuming after the domain reload from SessionState. It writes <dir>/capture.json and exits.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using GameCore.Studio.Views.Canvas;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameCore.Studio.Views.Evidence
{
    public sealed class StudioViewsEvidence : ScriptableSingleton<StudioViewsEvidence>
    {
        public const int MaxBytes = 300 * 1024;
        private const string StepKey = "gamecore.p23.evidence.step";
        private const string OutKey = "gamecore.p23.evidence.out";
        private const string LogKey = "gamecore.p23.evidence.log";
        private const string NoPlayKey = "gamecore.p23.evidence.noplay";
        private const string Root = "Assets/Hollowmere";

        [NonSerialized]
        private StudioRuntime? _runtime;

        [NonSerialized]
        private StudioViewContext? _context;

        [NonSerialized]
        private EditorWindow? _window;

        [NonSerialized]
        private double _waitUntil;

        [NonSerialized]
        private string _stateRoot = string.Empty;

        [NonSerialized]
        private int _requestWait;

        private static readonly IReadOnlyList<string> Shots = new[]
        {
            "01-relationships-maren", "02-relationships-impact-lantern", "03-dialogue-maren-preview", "04-quests-drowned-bell",
            "05-world-hollowmere", "06-tables-items", "07-changes-conflict", "08-changes-dependencies", "09-changes-journal",
            "10-canvas-2000-nodes", "11-quests-live", "12-world-live",
        };

        /// <summary>-executeMethod entry.</summary>
        public static void Run()
        {
            string output = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "..", "..", "artifacts", "studio", "evidence", "P2.3");
            string[] args = Environment.GetCommandLineArgs();
            bool noPlay = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-p23Out" && i + 1 < args.Length)
                {
                    output = args[i + 1];
                }

                noPlay |= args[i] == "-p23NoPlay";
            }

            Directory.CreateDirectory(output);
            SessionState.SetString(OutKey, Path.GetFullPath(output));
            SessionState.SetString(LogKey, new JArray().ToString());
            SessionState.SetBool(NoPlayKey, noPlay);
            SessionState.SetInt(StepKey, 0);
            Note("start", "output " + output + ", play " + !noPlay);
            EditorApplication.update += Tick;
        }

        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if (SessionState.GetInt(StepKey, -1) >= 0)
            {
                EditorApplication.update += Tick;
            }
        }

        private static void Tick()
        {
            int step = SessionState.GetInt(StepKey, -1);
            if (step < 0)
            {
                EditorApplication.update -= Tick;
                return;
            }

            StudioViewsEvidence self = instance;
            if (EditorApplication.timeSinceStartup < self._waitUntil)
            {
                return;
            }

            try
            {
                self.Advance(step);
            }
            catch (Exception error)
            {
                Note("error", "step " + step + ": " + error);
                SessionState.SetInt(StepKey, step + 1);
                self._waitUntil = EditorApplication.timeSinceStartup + 0.5;
            }
        }

        private void Advance(int step)
        {
            switch (step)
            {
                case 0:
                    EditorSceneManager.OpenScene(Root + "/Regions/ThornwickVillage.unity", OpenSceneMode.Single);
                    CreateContext();
                    Next(step, 1.0);
                    return;
                case 1:
                    Show<RelationshipsWindow>(view =>
                    {
                        RelationshipsView relationships = (RelationshipsView)view;
                        relationships.Depth = 2;
                        relationships.SetRoots(new[] { Key(Root + "/Npcs/Definitions/Maren.asset") });
                    });
                    Next(step, 2.5);
                    return;
                case 2:
                    Shoot(Shots[0], step);
                    return;
                case 3:
                    Show<RelationshipsWindow>(view =>
                    {
                        RelationshipsView relationships = (RelationshipsView)view;
                        relationships.ImpactMode = true;
                        relationships.Depth = 1;
                        relationships.SetRoots(new[] { Key(Root + "/Items/Lantern.asset") });
                    });
                    Next(step, 2.5);
                    return;
                case 4:
                    Shoot(Shots[1], step);
                    return;
                case 5:
                    Show<DialogueWindow>(view =>
                    {
                        DialogueView dialogue = (DialogueView)view;
                        dialogue.ShowGraph(_context!.Graph().RefOf(Key(Root + "/Dialogue/Graphs/Maren.asset"))!);
                        dialogue.SetFact("bell_rung", 1);
                        ToolInvocation? preview = dialogue.RunPreview();
                        Note("dialogue.preview", preview?.Text ?? "none");
                        dialogue.SelectNode(0);
                    });
                    Next(step, 2.5);
                    return;
                case 6:
                    Shoot(Shots[2], step);
                    return;
                case 7:
                    Show<QuestsWindow>(view =>
                    {
                        QuestsView quests = (QuestsView)view;
                        quests.ShowQuest(_context!.Graph().RefOf(Key(Root + "/Quests/DrownedBell.asset"))!);
                        QuestSimulation? simulation = quests.RunSimulation(1);
                        Note("quest.simulate", simulation == null ? "none" : QuestEdits.Describe(simulation));
                        quests.Canvas.Select(QuestsView.CompleteId);
                    });
                    Next(step, 2.5);
                    return;
                case 8:
                    Shoot(Shots[3], step);
                    return;
                case 9:
                    Show<WorldWindow>(view =>
                    {
                        WorldView world = (WorldView)view;
                        world.Refresh();
                        WorldDocument? document = world.Document;
                        if (document != null)
                        {
                            foreach (WorldRegion region in document.Regions)
                            {
                                if (region.SceneOpen)
                                {
                                    world.Canvas.Select(region.Key);
                                }
                            }
                        }
                    });
                    Next(step, 2.5);
                    return;
                case 10:
                    Shoot(Shots[4], step);
                    return;
                case 11:
                    Show<TablesWindow>(view => ((TablesView)view).ShowTab(TablesView.ItemsTab));
                    Next(step, 2.0);
                    return;
                case 12:
                    Shoot(Shots[5], step);
                    return;
                case 13:
                    Show<ChangesWindow>(view =>
                    {
                        ChangesView changes = (ChangesView)view;
                        ChangeSetInspection inspection = changes.Inspect(ConflictingCandidate());
                        Note("changes.inspect", "ok " + inspection.Ok + ", conflicts " + inspection.ConflictCount);
                    });
                    Next(step, 2.0);
                    return;
                case 14:
                    Shoot(Shots[6], step);
                    return;
                case 15:
                    ((ChangesView)((StudioViewWindow)_window!).View!).ShowTab(ChangesView.DependenciesTab);
                    Next(step, 2.5);
                    return;
                case 16:
                    Note("dependencies", "packages " + ((ChangesView)((StudioViewWindow)_window!).View!).Packages?.Packages.Count + ", problems " + ((ChangesView)((StudioViewWindow)_window!).View!).Packages?.ProblemCount);
                    Shoot(Shots[7], step);
                    return;
                case 17:
                    MakeJournalEntries();
                    ChangesView journal = (ChangesView)((StudioViewWindow)_window!).View!;
                    journal.ShowTab(ChangesView.JournalTab);
                    if (journal.Timeline.Count >= 2)
                    {
                        journal.Diff(journal.Timeline[1], journal.Timeline[0]);
                    }

                    Note("journal", journal.Timeline.Count + " entries");
                    Next(step, 2.0);
                    return;
                case 18:
                    Shoot(Shots[8], step);
                    return;
                case 19:
                    ShowSyntheticCanvas();
                    Next(step, 0.1);
                    return;
                case 20:
                    EvidenceCanvasWindow canvasWindow = (EvidenceCanvasWindow)_window!;
                    if (canvasWindow.Canvas.LayoutPending)
                    {
                        _waitUntil = EditorApplication.timeSinceStartup + 0.05;
                        return;
                    }

                    canvasWindow.Canvas.FrameAll();
                    canvasWindow.Report();
                    Note("canvas-2000", canvasWindow.Timings);
                    Next(step, 2.0);
                    return;
                case 21:
                    Shoot(Shots[9], step);
                    return;
                case 22:
                    CloseWindow();
                    DisposeContext();
                    if (SessionState.GetBool(NoPlayKey, false))
                    {
                        SessionState.SetInt(StepKey, 100);
                        return;
                    }

                    EditorSceneManager.OpenScene(Root + "/Boot/Boot.unity", OpenSceneMode.Single);
                    SessionState.SetInt(StepKey, 23);
                    _waitUntil = EditorApplication.timeSinceStartup + 0.5;
                    EditorApplication.isPlaying = true;
                    return;
                case 23:
                    if (!EditorApplication.isPlaying)
                    {
                        _waitUntil = EditorApplication.timeSinceStartup + 0.5;
                        return;
                    }

                    Next(step, 8.0);
                    return;
                case 24:
                    CreateContext();
                    Note("gameplay bridge", _context!.Gameplay.Describe + ", narrative " + _context.Gameplay.HasNarrative);
                    Show<QuestsWindow>(view => ((QuestsView)view).Refresh());
                    Next(step, 2.5);
                    return;
                case 25:
                    Shoot(Shots[10], step);
                    return;
                case 26:
                    Show<WorldWindow>(view =>
                    {
                        WorldView world = (WorldView)view;
                        world.Refresh();
                        if (world.Document != null && world.Document.Regions.Count > 0)
                        {
                            world.Canvas.Select(world.Document.Regions[0].Key);
                            Note("world live residency", string.Join(", ", Residencies(world.Document)));
                        }
                    });
                    Next(step, 2.5);
                    return;
                case 27:
                    Shoot(Shots[11], step);
                    return;
                case 28:
                    CloseWindow();
                    DisposeContext();
                    EditorApplication.isPlaying = false;
                    SessionState.SetInt(StepKey, 100);
                    _waitUntil = EditorApplication.timeSinceStartup + 2.0;
                    return;
                default:
                    if (EditorApplication.isPlaying)
                    {
                        EditorApplication.isPlaying = false;
                        _waitUntil = EditorApplication.timeSinceStartup + 1.0;
                        return;
                    }

                    Finish();
                    return;
            }
        }

        private void Next(int step, double wait)
        {
            SessionState.SetInt(StepKey, step + 1);
            _waitUntil = EditorApplication.timeSinceStartup + wait;
        }

        private void CreateContext()
        {
            DisposeContext();
            _stateRoot = Path.Combine(Path.GetTempPath(), "gcstudio-p23-evidence-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_stateRoot);
            _runtime = StudioRuntime.Create(new StudioRuntimeOptions
            {
                Paths = new StudioPaths(Directory.GetParent(Application.dataPath)!.FullName, _stateRoot, "p23-evidence"),
                Log = new MemoryStudioLog(),
                SearchFolders = new[] { Root },
                IndexScope = AuthoringSourceScope.All,
                LoadIndexCache = false,
            });
            ListSelectionBridge selection = new ListSelectionBridge();
            _context = new StudioViewContext(_runtime, selection, new ReflectionGameplayBridge(), true);
        }

        private void DisposeContext()
        {
            _context?.Dispose();
            _context = null;
            _runtime?.Dispose();
            _runtime = null;
            if (_stateRoot.Length > 0 && Directory.Exists(_stateRoot))
            {
                Directory.Delete(_stateRoot, true);
            }
        }

        private void Show<T>(Action<StudioViewBase> setUp)
            where T : StudioViewWindow
        {
            CloseWindow();
            T window = EditorWindow.CreateWindow<T>();
            window.position = new Rect(40f, 40f, StudioViewIds.DefaultWidth, StudioViewIds.DefaultHeight);
            window.Show();
            window.Host(_context!);
            setUp(window.View!);
            window.Focus();
            window.Repaint();
            _window = window;
        }

        private void ShowSyntheticCanvas()
        {
            CloseWindow();
            EvidenceCanvasWindow window = EditorWindow.CreateWindow<EvidenceCanvasWindow>("Relationships (2,000 synthetic nodes)");
            window.position = new Rect(40f, 40f, StudioViewIds.DefaultWidth, StudioViewIds.DefaultHeight);
            window.Show();
            window.Build(2000, 13);
            _window = window;
        }

        private void CloseWindow()
        {
            if (_window != null)
            {
                _window.Close();
            }

            _window = null;
        }

        private void Shoot(string shot, int step)
        {
            string output = SessionState.GetString(OutKey, string.Empty);
            string path = Path.Combine(output, shot + ".png");
            string request = Path.Combine(output, ".request-" + shot + ".json");
            if (_requestWait > 0)
            {
                if (File.Exists(path) && !File.Exists(request))
                {
                    Note(shot, "captured by the shell script (import), " + new FileInfo(path).Length + " bytes");
                    _requestWait = 0;
                    Next(step, 0.3);
                    return;
                }

                if (--_requestWait == 0)
                {
                    Note(shot, "no capture: the screen read was blank and no external capture arrived");
                    Next(step, 0.3);
                    return;
                }

                _waitUntil = EditorApplication.timeSinceStartup + 0.25;
                return;
            }

            EditorWindow window = _window!;
            window.Repaint();
            Rect rect = window.position;
            int width = Mathf.RoundToInt(rect.width);
            int height = Mathf.RoundToInt(rect.height);
            Color[] pixels = InternalEditorUtility.ReadScreenPixel(rect.position, width, height);
            if (IsBlank(pixels))
            {
                File.WriteAllText(request, new JObject { ["x"] = Mathf.RoundToInt(rect.x), ["y"] = Mathf.RoundToInt(rect.y), ["width"] = width, ["height"] = height, ["png"] = path }.ToString());
                _requestWait = 80;
                _waitUntil = EditorApplication.timeSinceStartup + 0.25;
                return;
            }

            long bytes = WritePng(pixels, width, height, path);
            Note(shot, width + "x" + height + " read from the screen, " + bytes + " bytes");
            Next(step, 0.3);
        }

        private static bool IsBlank(Color[] pixels)
        {
            if (pixels.Length == 0)
            {
                return true;
            }

            HashSet<int> colours = new HashSet<int>();
            for (int i = 0; i < pixels.Length; i += 97)
            {
                Color32 colour = pixels[i];
                colours.Add(colour.r | (colour.g << 8) | (colour.b << 16));
                if (colours.Count > 8)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Encodes the pixels (bottom-up rows) as PNG, halving towards 300 KB as needed.</summary>
        public static long WritePng(Color[] pixels, int width, int height, string path)
        {
            float scale = 1f;
            byte[] png;
            while (true)
            {
                int w = Mathf.Max(1, Mathf.RoundToInt(width * scale));
                int h = Mathf.Max(1, Mathf.RoundToInt(height * scale));
                Texture2D texture = new Texture2D(w, h, TextureFormat.RGB24, false);
                if (w == width && h == height)
                {
                    texture.SetPixels(pixels);
                }
                else
                {
                    Color[] scaled = new Color[w * h];
                    for (int y = 0; y < h; y++)
                    {
                        int sy = Mathf.Min(height - 1, Mathf.FloorToInt(y / scale));
                        for (int x = 0; x < w; x++)
                        {
                            int sx = Mathf.Min(width - 1, Mathf.FloorToInt(x / scale));
                            scaled[y * w + x] = pixels[sy * width + sx];
                        }
                    }

                    texture.SetPixels(scaled);
                }

                texture.Apply();
                png = texture.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(texture);
                if (png.Length <= MaxBytes || scale < 0.3f)
                {
                    break;
                }

                scale *= 0.8f;
            }

            File.WriteAllBytes(path, png);
            return png.Length;
        }

        private string Key(string assetPath)
        {
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            AuthoringRef? reference = asset == null ? null : _runtime!.Resolver.BuildRef(asset, null, false);
            return reference?.IdentityKey ?? assetPath;
        }

        private ChangeSet ConflictingCandidate()
        {
            UnityEngine.Object lantern = AssetDatabase.LoadMainAssetAtPath(Root + "/Items/Lantern.asset");
            UnityEngine.Object coin = AssetDatabase.LoadMainAssetAtPath(Root + "/Items/OldCoin.asset");
            AuthoringRef stale = _runtime!.Resolver.BuildRef(lantern, null, true)!.WithStamp(_runtime.Resolver.ComputeStamp(coin));
            return new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId, new Intent("Agent: make the lantern cheaper and the coin worth less", IntentOrigin.Agent), new[]
            {
                ViewEdits.SetOp("op1", stale, "price", 3),
                ViewEdits.Op("op2", BuiltInToolIdsExt.Set, _runtime.Resolver.BuildRef(coin, null, true), new JObject { ["field"] = "price", ["value"] = 2 }, new[] { "op1" }),
            });
        }

        /// <summary>Two table edits (one row commit, one multi-row apply) and their undos, so the journal has entries.</summary>
        private void MakeJournalEntries()
        {
            TableModel items = TableModel.ForType(_runtime!.Registry.Catalog, _context!.Graph(), "inventory.item", "Items");
            List<TableRow> rows = new List<TableRow>();
            foreach (TableRow row in items.AllRows)
            {
                if (rows.Count < 3)
                {
                    rows.Add(row);
                }
            }

            if (rows.Count == 0)
            {
                return;
            }

            ApplyReport one = _context.Edits.Apply(TableModel.CommitRow(rows[0], new JObject { ["price"] = 20 }, "Items"));
            ApplyReport many = _context.Edits.Apply(TableModel.ApplyToRows(rows, "price", new JValue(9), "Items"));
            HistoryResult undoMany = _context.Edits.Undo(many.Entry.Id);
            HistoryResult undoOne = _context.Edits.Undo(one.Entry.Id);
            Note("journal entries", ViewEdits.Describe(one) + " | " + ViewEdits.Describe(many) + " | undo " + undoMany.Ok + "/" + undoOne.Ok);
        }

        private static IEnumerable<string> Residencies(WorldDocument world)
        {
            foreach (WorldRegion region in world.Regions)
            {
                yield return region.Name + "=" + region.Residency;
            }
        }

        private static void Note(string what, string detail)
        {
            JArray log;
            try
            {
                log = JArray.Parse(SessionState.GetString(LogKey, "[]"));
            }
            catch (Newtonsoft.Json.JsonException)
            {
                log = new JArray();
            }

            log.Add(new JObject { ["what"] = what, ["detail"] = detail, ["at"] = DateTime.UtcNow.ToString("o") });
            SessionState.SetString(LogKey, log.ToString());
            UnityEngine.Debug.Log("[P2.3 evidence] " + what + ": " + detail);
        }

        private void Finish()
        {
            string output = SessionState.GetString(OutKey, string.Empty);
            File.WriteAllText(Path.Combine(output, "capture.json"), SessionState.GetString(LogKey, "[]"));
            SessionState.EraseInt(StepKey);
            EditorApplication.update -= Tick;
            UnityEngine.Debug.Log("[P2.3 evidence] done: " + output);
            EditorApplication.Exit(0);
        }
    }

    /// <summary>A window hosting the graph canvas over a synthetic graph (the 2,000-node evidence shot).</summary>
    public sealed class EvidenceCanvasWindow : EditorWindow
    {
        [NonSerialized]
        private GraphCanvas? _canvas;

        [NonSerialized]
        private Neighbourhood? _neighbourhood;

        [NonSerialized]
        private double _buildMilliseconds;

        public GraphCanvas Canvas => _canvas ??= new GraphCanvas();

        public string Timings { get; private set; } = string.Empty;

        public void Build(int count, int fanOut)
        {
            Stopwatch watch = Stopwatch.StartNew();
            List<IndexNode> nodes = new List<IndexNode>(count);
            List<IndexEdge> edges = new List<IndexEdge>(count);
            string[] types = { "npc.definition", "dialogue.graph", "quest.quest", "inventory.item", "world.region", "entity.instance" };
            for (int i = 0; i < count; i++)
            {
                AuthoringRef reference = new AuthoringRef(AuthoringKind.Definition, authoringId: "synthetic-" + i);
                nodes.Add(new IndexNode(reference, types[i % types.Length], "Node " + i));
                if (i > 0)
                {
                    edges.Add(new IndexEdge(nodes[(i - 1) / fanOut].Ref, reference, i % 5 == 0 ? EdgeKind.Contains : EdgeKind.References));
                }
            }

            IndexGraph graph = IndexGraph.Build(new SemanticIndex(1, "synthetic", nodes, edges));
            _neighbourhood = RelationshipsModel.Build(graph, new[] { nodes[0].Ref.IdentityKey }, 3, new HashSet<EdgeKind>(EdgeKinds.All));
            _buildMilliseconds = watch.Elapsed.TotalMilliseconds;
            rootVisualElement.Clear();
            rootVisualElement.Add(Canvas);
            RelationshipsCanvas.Show(Canvas, _neighbourhood);
        }

        public void Report()
        {
            GraphLayout? layout = Canvas.Layout;
            Timings = (_neighbourhood?.Nodes.Count ?? 0) + " nodes; graph+neighbourhood " + _buildMilliseconds.ToString("0.0") + " ms; layout "
                + (layout == null ? "-" : layout.Steps + " slices, max " + layout.MaxStepMilliseconds.ToString("0.00") + " ms, total " + layout.TotalMilliseconds.ToString("0.0") + " ms")
                + "; refresh " + Canvas.LastRefreshMilliseconds.ToString("0.00") + " ms; edge paint " + Canvas.LastEdgePaintMilliseconds.ToString("0.00") + " ms; compact " + Canvas.IsCompact;
            Canvas.StatusText = Timings;
        }
    }
}

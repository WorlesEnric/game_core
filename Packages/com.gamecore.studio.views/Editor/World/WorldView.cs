// GameCore.Studio.Views - W-VIEW-04 World (GameCore/Studio/World, F2 / SR-2.x).
// The world's regions on the canvas (a ring; accent = residency colour; badges: start region, residency, indexed
// object count, spawn point), each portal as two directed edges, and an NPC schedule timeline strip below. Clicking a
// region selects it; double-click (or Enter) loads its scene additively in Edit Mode - EditorSceneManager.OpenScene
// with OpenSceneMode.Additive: editor scene management, not an asset edit, so it is not a change set; closing asks to
// save modified scenes first - and in Play Mode asks the running game to travel there (world.travel through
// IGameplayCommandBridge). The inspector connects regions, places portal ends and moves the spawn point through
// WorldEdits (change sets).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using GameCore.Studio.Views.Canvas;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace GameCore.Studio.Views
{
    public sealed class WorldView : StudioViewBase
    {
        private readonly GraphCanvas _canvas;
        private readonly VisualElement _inspector;
        private readonly VisualElement _timeline;
        private string? _selectedKey;

        public WorldView(StudioViewContext context)
            : base(context, StudioViewIds.World)
        {
            AddButton("Frame", () => _canvas!.FrameAll());
            AddSpacer();
            AddButton("Travel here", () => TravelSelected(), "Play Mode: world.travel to the selected region.");
            VisualElement column = new VisualElement();
            column.style.flexGrow = 1f;
            column.style.flexDirection = FlexDirection.Column;
            _canvas = new GraphCanvas();
            _canvas.SelectionChanged += card =>
            {
                _selectedKey = card?.Id;
                if (card?.Payload is WorldRegion region)
                {
                    Context.Selection.Select(new[] { region.Ref });
                }

                BuildInspector();
            };
            _canvas.NodeActivated += card =>
            {
                if (card.Payload is WorldRegion region)
                {
                    Activate(region);
                }
            };
            column.Add(_canvas);
            _timeline = new VisualElement { name = "schedule-timeline" };
            _timeline.style.flexShrink = 0f;
            _timeline.style.maxHeight = 150f;
            _timeline.style.paddingLeft = 6f;
            _timeline.style.paddingRight = 6f;
            _timeline.style.borderTopWidth = 1f;
            _timeline.style.borderTopColor = new Color(0f, 0f, 0f, 0.3f);
            column.Add(_timeline);
            Body.Add(column);
            ScrollView scroll = new ScrollView();
            scroll.style.width = 340f;
            scroll.style.flexShrink = 0f;
            _inspector = Panel("Region", 340f);
            scroll.Add(_inspector);
            Body.Add(scroll);
        }

        public WorldDocument? Document { get; private set; }

        public GraphCanvas Canvas => _canvas;

        /// <summary>Loads the region's scene additively (Edit Mode) or travels there (Play Mode).</summary>
        public string Activate(WorldRegion region)
        {
            if (Context.IsPlaying)
            {
                GameplayCommandResult result = Context.Gameplay.Travel(region.AuthoringId);
                SetStatus(result.ToString(), result.Ok ? ViewPalette.Good : ViewPalette.Warn);
                return result.ToString();
            }

            if (region.ScenePath.Length == 0)
            {
                SetStatus(region.Name + " has no scene path.", ViewPalette.Warn);
                return "no scene";
            }

            Scene scene = SceneManager.GetSceneByPath(region.ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                EditorSceneManager.OpenScene(region.ScenePath, OpenSceneMode.Additive);
                SetStatus("Opened " + region.ScenePath + " additively (editor scene management; not a change set).", ViewPalette.Info);
            }

            Context.Runtime.Index.Flush();
            Refresh();
            return "opened";
        }

        public void CloseScene(WorldRegion region)
        {
            Scene scene = SceneManager.GetSceneByPath(region.ScenePath);
            if (!scene.IsValid() || SceneManager.sceneCount <= 1)
            {
                return;
            }

            if (scene.isDirty && !EditorSceneManager.SaveModifiedScenesIfUserWantsTo(new[] { scene }))
            {
                return;
            }

            EditorSceneManager.CloseScene(scene, true);
            Refresh();
        }

        /// <summary>Connects two regions with a new portal (see <see cref="WorldEdits.ConnectRegions"/>); newest report last.</summary>
        public IReadOnlyList<ApplyReport> Connect(WorldRegion a, WorldRegion b)
        {
            if (Document == null)
            {
                return Array.Empty<ApplyReport>();
            }

            IReadOnlyList<ApplyReport> reports = new[] { Context.Edits.Apply(WorldEdits.ConnectRegions(Context.Runtime, Document, a, b)) };
            Refresh();
            return reports;
        }

        protected override void OnRefresh()
        {
            IndexGraph graph = Context.Graph();
            Document = WorldDocument.Load(Context, graph);
            if (Document == null)
            {
                _canvas.SetGraph(Array.Empty<CanvasNode>(), Array.Empty<CanvasEdge>());
                _inspector.Clear();
                _timeline.Clear();
                SetStatus("No world.definition in the index.");
                return;
            }

            List<CanvasNode> cards = new List<CanvasNode>();
            int count = Document.Regions.Count;
            float radius = Mathf.Max(260f, count * 90f);
            for (int i = 0; i < count; i++)
            {
                WorldRegion region = Document.Regions[i];
                float angle = Mathf.PI * 2f * i / Mathf.Max(1, count) - Mathf.PI * 0.5f;
                CanvasNode card = new CanvasNode(region.Key, region.Name)
                {
                    Subtitle = region.ScenePath.Length > 0 ? System.IO.Path.GetFileNameWithoutExtension(region.ScenePath) : "world.region",
                    Accent = ViewSupport.ResidencyColor(region.Residency),
                    Position = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * 0.7f),
                    HasPosition = true,
                    Size = new Vector2(230f, 78f),
                    Payload = region,
                };
                if (region.IsStart)
                {
                    card.Badges.Add(new CanvasBadge("★ start", ViewPalette.Warn));
                }

                if (region.Residency.Length > 0)
                {
                    card.Badges.Add(new CanvasBadge(region.Residency, ViewSupport.ResidencyColor(region.Residency)));
                }

                card.Badges.Add(new CanvasBadge(region.EntityCount + " objects", ViewPalette.Neutral));
                if (region.Spawn.HasValue)
                {
                    Vector3 spawn = region.Spawn.Value;
                    card.Badges.Add(new CanvasBadge("spawn " + spawn.x.ToString("0.#") + "," + spawn.y.ToString("0.#") + "," + spawn.z.ToString("0.#"), ViewPalette.Info));
                }

                cards.Add(card);
            }

            List<CanvasEdge> edges = new List<CanvasEdge>();
            foreach (WorldPortal portal in Document.Portals)
            {
                if (portal.RegionA == null || portal.RegionB == null)
                {
                    continue;
                }

                edges.Add(new CanvasEdge(portal.RegionA, portal.RegionB, portal.Name) { Color = ViewPalette.Teal, Width = 2f, Payload = portal });
                edges.Add(new CanvasEdge(portal.RegionB, portal.RegionA, string.Empty) { Color = ViewPalette.Teal, Width = 2f, Payload = portal });
            }

            _canvas.SetGraph(cards, edges, null, keepPositions: false, frame: RefreshCount <= 1);
            if (_selectedKey != null)
            {
                _canvas.Select(_selectedKey);
            }

            _canvas.StatusText = Document.Name + ": " + Document.Regions.Count + " regions, " + Document.Portals.Count + " portals" + (Context.IsPlaying ? " (live residency)" : string.Empty);
            BuildInspector();
            BuildTimeline();
        }

        private void BuildInspector()
        {
            _inspector.Clear();
            WorldDocument? world = Document;
            if (world == null)
            {
                return;
            }

            WorldRegion? region = _selectedKey == null ? null : world.Region(_selectedKey);
            if (region == null)
            {
                _inspector.Add(Text(world.Name + ": select a region (click, arrows). Double-click or Enter loads it additively (Edit Mode) or travels there (Play Mode)."));
                return;
            }

            _inspector.Add(Text(region.Name, 12f, FontStyle.Bold));
            _inspector.Add(Text(region.ScenePath + "\n" + (region.Residency.Length > 0 ? region.Residency : "residency unknown") + ", " + region.EntityCount + " indexed objects"));
            if (Context.IsPlaying)
            {
                _inspector.Add(new Button(() => Activate(region)) { text = "Travel here" });
            }
            else if (!region.SceneOpen)
            {
                _inspector.Add(new Button(() => Activate(region)) { text = "Load additively" });
            }
            else
            {
                _inspector.Add(new Button(() => CloseScene(region)) { text = "Close scene" });
            }

            List<WorldRegion> others = new List<WorldRegion>();
            List<string> otherNames = new List<string>();
            foreach (WorldRegion other in world.Regions)
            {
                if (other.Key != region.Key)
                {
                    others.Add(other);
                    otherNames.Add(other.Name + (world.Between(region.Key, other.Key) != null ? " (connected)" : string.Empty));
                }
            }

            if (others.Count > 0)
            {
                _inspector.Add(Text("Connect regions", 11f, FontStyle.Bold));
                PopupField<string> to = new PopupField<string>("To", otherNames, 0);
                _inspector.Add(to);
                _inspector.Add(new Button(() =>
                {
                    if (Document != null)
                    {
                        Connect(region, others[to.index]);
                    }
                }) { text = "Connect (portal)" });
            }

            List<WorldPortal> portals = new List<WorldPortal>();
            List<string> portalNames = new List<string>();
            foreach (WorldPortal portal in world.Portals)
            {
                if (portal.RegionA == region.Key || portal.RegionB == region.Key)
                {
                    portals.Add(portal);
                    portalNames.Add(portal.Name);
                }
            }

            Vector3 origin = region.Spawn ?? Vector3.zero;
            if (portals.Count > 0)
            {
                _inspector.Add(Text("Place portal end", 11f, FontStyle.Bold));
                PopupField<string> portalPicker = new PopupField<string>("Portal", portalNames, 0);
                Vector3Field position = new Vector3Field("Position") { value = origin + new Vector3(4f, 0f, 0f) };
                FloatField yaw = new FloatField("Yaw") { value = 0f };
                _inspector.Add(portalPicker);
                _inspector.Add(position);
                _inspector.Add(yaw);
                _inspector.Add(new Button(() => ApplyEdit(ViewEdits.Build("World: add portal end in " + region.Name, new[] { WorldEdits.AddPortal(portals[portalPicker.index], region, position.value, yaw.value) }))) { text = "Add portal (world.addPortal)" });
                IReadOnlyList<string> unbound = ToolBinding.UnboundParameters(WorldEdits.AddPortalTool);
                if (unbound.Count > 0)
                {
                    _inspector.Add(Text("world.addPortal cannot receive '" + string.Join("', '", unbound) + "' through the engine yet; the change set is refused until P1.1/P3.1 declare it (PACKET.md).", 10f));
                }
            }

            _inspector.Add(Text("Spawn point", 11f, FontStyle.Bold));
            Vector3Field spawnField = new Vector3Field("Position") { value = origin };
            FloatField spawnYaw = new FloatField("Yaw") { value = 0f };
            _inspector.Add(spawnField);
            _inspector.Add(spawnYaw);
            _inspector.Add(new Button(() => ApplyEdit(ViewEdits.Build("World: spawn point of " + region.Name, new[] { WorldEdits.SetSpawnPoint(Context.Runtime, region, spawnField.value, spawnYaw.value) }))) { text = "Set spawn point" });
            if (!region.SceneOpen)
            {
                _inspector.Add(Text("Load the region to edit its scene (spawn point, portal ends).", 10f));
            }
        }

        private void BuildTimeline()
        {
            _timeline.Clear();
            WorldDocument? world = Document;
            if (world == null || world.Schedules.Count == 0)
            {
                _timeline.Add(Text("No NPC schedules.", 10f));
                return;
            }

            _timeline.Add(Text("NPC schedules (one day)", 11f, FontStyle.Bold));
            foreach (NpcSchedule schedule in world.Schedules)
            {
                VisualElement row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.height = 18f;
                row.style.marginBottom = 2f;
                Label name = new Label(schedule.Npc);
                name.style.width = 110f;
                name.style.fontSize = 10f;
                row.Add(name);
                VisualElement bar = new VisualElement();
                bar.style.flexGrow = 1f;
                bar.style.flexDirection = FlexDirection.Row;
                float day = schedule.DayLength > 0f ? schedule.DayLength : 1f;
                for (int i = 0; i < schedule.Phases.Count; i++)
                {
                    SchedulePhase phase = schedule.Phases[i];
                    float end = i + 1 < schedule.Phases.Count ? schedule.Phases[i + 1].Start : day;
                    float share = Mathf.Clamp01((end - phase.Start) / day);
                    Label segment = new Label(phase.Name) { tooltip = phase.Name + " from " + phase.Start.ToString("0") + " s" + (phase.Behaviour.Length > 0 ? ": " + phase.Behaviour : string.Empty) };
                    segment.style.width = Length.Percent(share * 100f);
                    segment.style.backgroundColor = Color.HSVToRGB(i * 0.17f % 1f, 0.45f, 0.7f);
                    segment.style.color = Color.white;
                    segment.style.fontSize = 9f;
                    segment.style.overflow = Overflow.Hidden;
                    segment.style.borderRightWidth = 1f;
                    segment.style.borderRightColor = new Color(0f, 0f, 0f, 0.4f);
                    bar.Add(segment);
                }

                row.Add(bar);
                _timeline.Add(row);
            }
        }

        private void TravelSelected()
        {
            WorldRegion? region = _selectedKey == null || Document == null ? null : Document.Region(_selectedKey);
            if (region == null)
            {
                SetStatus("Select a region first.", ViewPalette.Warn);
                return;
            }

            GameplayCommandResult result = Context.Gameplay.Travel(region.AuthoringId);
            SetStatus(result.ToString(), result.Ok ? ViewPalette.Good : ViewPalette.Warn);
        }
    }

    public sealed class WorldWindow : StudioViewWindow
    {
        protected override string ViewTitle => "World";

        [MenuItem(StudioViewIds.WorldMenu, false, 2103)]
        public static void OpenWindow() => Open<WorldWindow>();

        protected override StudioViewBase CreateView(StudioViewContext context) => new WorldView(context);
    }
}

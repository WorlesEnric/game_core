// GameCore.Studio.Views - the graph canvas shared by the relationships, dialogue, world and change-set views.
//
// Decision (documented in PACKET.md): an own UI Toolkit canvas instead of UnityEditor.Experimental.GraphView. GraphView
// creates a VisualElement per node, port and edge and lays all of them out every frame, which does not hold 2,000-node
// neighbourhoods within a 16 ms frame; it is also experimental API. This canvas keeps nodes as data and virtualises:
//   * cards exist only for nodes inside the visible rect (a spatial grid finds them; a pool of at most MaxCards
//     elements is rebound), with a compact level of detail (nodes painted as rectangles) when zoomed out or crowded;
//   * edges are painted with Painter2D in world coordinates inside the panned/zoomed content element, batched per
//     colour into one path, so panning and zooming only change two style transforms and never regenerate meshes;
//   * layout is lazy and time-sliced (GraphLayout.Step with an 8 ms budget per editor frame).
// Keyboard: arrows move the selection to the nearest card in that direction, Enter activates (double-click), F frames
// the selection, A frames everything, +/- zoom, [ and ] cycle cards in order, Delete asks the view to delete.
// Mouse: click selects, double-click activates, drag a card to move it, drag the background (or middle-drag) to pan,
// wheel zooms around the pointer, Ctrl/Cmd-drag from one card to another asks the view to connect them.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameCore.Studio.Views.Canvas
{
    public sealed class GraphCanvas : VisualElement
    {
        public const int MaxCards = 300;
        public const float CompactZoom = 0.4f;
        public const double LayoutBudgetMs = 8.0;
        private const float GridCell = 512f;
        private const int MaxEdgeLabels = 200;

        private readonly VisualElement _content;
        private readonly VisualElement _edgeLayer;
        private readonly VisualElement _nodeLayer;
        private readonly VisualElement _labelLayer;
        private readonly Label _status;
        private readonly List<NodeCard> _pool = new List<NodeCard>();
        private readonly List<Label> _labelPool = new List<Label>();
        private readonly Dictionary<string, CanvasNode> _byId = new Dictionary<string, CanvasNode>(StringComparer.Ordinal);
        private readonly Dictionary<long, List<CanvasNode>> _grid = new Dictionary<long, List<CanvasNode>>();
        private readonly List<CanvasNode> _visible = new List<CanvasNode>();
        private readonly List<CanvasNode> _nodes = new List<CanvasNode>();
        private readonly List<CanvasEdge> _edges = new List<CanvasEdge>();
        private readonly HashSet<string> _visibilitySeen = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<Color, List<CanvasEdge>> _edgeGroups = new Dictionary<Color, List<CanvasEdge>>();
        private readonly Stack<List<CanvasNode>> _gridPool = new Stack<List<CanvasNode>>();
        private readonly Stopwatch _refreshWatch = new Stopwatch();
        private readonly Stopwatch _paintWatch = new Stopwatch();

        private Vector2 _pan = new Vector2(40f, 40f);
        private float _zoom = 1f;
        private Vector2 _worldMin;
        private Vector2 _worldMax;
        private Vector2? _viewportOverride;
        private GraphLayout? _layout;
        private IVisualElementScheduledItem? _layoutTicker;
        private bool _frameWhenLaidOut;

        // True while the view shows "everything, fitted" and the user has not panned or zoomed since: a resize refits.
        private bool _fitted;
        private string? _selectedId;
        private Gesture _gesture;
        private int _pointerId = -1;
        private Vector2 _pressLocal;
        private Vector2 _lastLocal;
        private CanvasNode? _pressed;
        private Vector2 _pressedStart;
        private Vector2 _connectEnd;
        private bool _compact;

        public GraphCanvas()
        {
            focusable = true;
            tabIndex = 0;
            style.flexGrow = 1f;
            style.overflow = Overflow.Hidden;
            style.backgroundColor = ViewPalette.CanvasBackground;
            _content = new VisualElement { name = "graph-content", pickingMode = PickingMode.Ignore };
            _content.style.position = Position.Absolute;
            _content.style.left = 0f;
            _content.style.top = 0f;
            _content.style.transformOrigin = new TransformOrigin(new Length(0f), new Length(0f), 0f);
            _edgeLayer = new VisualElement { name = "graph-edges", pickingMode = PickingMode.Ignore };
            _edgeLayer.style.position = Position.Absolute;
            _edgeLayer.generateVisualContent += OnGenerateEdges;
            _nodeLayer = new VisualElement { name = "graph-nodes", pickingMode = PickingMode.Ignore };
            _nodeLayer.style.position = Position.Absolute;
            _labelLayer = new VisualElement { name = "graph-labels", pickingMode = PickingMode.Ignore };
            _labelLayer.style.position = Position.Absolute;
            _content.Add(_edgeLayer);
            _content.Add(_labelLayer);
            _content.Add(_nodeLayer);
            Add(_content);
            _status = new Label { pickingMode = PickingMode.Ignore };
            _status.style.position = Position.Absolute;
            _status.style.right = 8f;
            _status.style.bottom = 4f;
            _status.style.fontSize = 10f;
            _status.style.color = ViewPalette.SubText;
            Add(_status);
            ApplyTransform();
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<DetachFromPanelEvent>(_ => StopTicker());
        }

        private enum Gesture
        {
            None,
            Pan,
            DragNode,
            Connect,
        }

        /// <summary>A card was selected (null: the selection was cleared).</summary>
        public event Action<CanvasNode?>? SelectionChanged;

        /// <summary>A card was double-clicked or Enter was pressed on it.</summary>
        public event Action<CanvasNode>? NodeActivated;

        /// <summary>Ctrl/Cmd-drag from the first card to the second.</summary>
        public event Action<CanvasNode, CanvasNode>? ConnectRequested;

        /// <summary>Delete/Backspace on a selected card.</summary>
        public event Action<CanvasNode>? DeleteRequested;

        public IReadOnlyList<CanvasNode> Nodes => _nodes;

        public IReadOnlyList<CanvasEdge> Edges => _edges;

        public CanvasNode? Selected => _selectedId != null && _byId.TryGetValue(_selectedId, out CanvasNode? node) ? node : null;

        public Vector2 Pan => _pan;

        public float Zoom => _zoom;

        /// <summary>True while a layout is still running in slices.</summary>
        public bool LayoutPending => _layout != null && !_layout.Done;

        public GraphLayout? Layout => _layout;

        /// <summary>Cards currently bound (the virtualised subset).</summary>
        public int VisibleCardCount { get; private set; }

        /// <summary>Nodes inside the visible rect at the last refresh.</summary>
        public int VisibleNodeCount => _visible.Count;

        public bool IsCompact => _compact;

        public double LastRefreshMilliseconds { get; private set; }

        public double LastEdgePaintMilliseconds { get; private set; }

        /// <summary>A text shown in the lower right corner (counts, truncation notes).</summary>
        public string StatusText
        {
            get => _status.text;
            set => _status.text = value ?? string.Empty;
        }

        /// <summary>Tests (no panel) give the viewport size explicitly.</summary>
        public void SetViewportSize(Vector2 size)
        {
            _viewportOverride = size;
            Refresh();
        }

        /// <summary>
        /// Replaces the graph. Cards whose id existed before keep their position when <paramref name="keepPositions"/>;
        /// the rest are placed by a lazy layered layout from <paramref name="roots"/>.
        /// </summary>
        public void SetGraph(IReadOnlyList<CanvasNode> nodes, IReadOnlyList<CanvasEdge> edges, IEnumerable<string>? roots = null, bool keepPositions = true, bool vertical = false, bool frame = false)
        {
            Dictionary<string, CanvasNode> previous = new Dictionary<string, CanvasNode>(_byId, StringComparer.Ordinal);
            _nodes.Clear();
            if (nodes != null) _nodes.AddRange(nodes);
            _edges.Clear();
            if (edges != null) _edges.AddRange(edges);
            RefreshEdgeStyles();
            _byId.Clear();
            bool needsLayout = false;
            foreach (CanvasNode node in _nodes)
            {
                _byId[node.Id] = node;
                if (!node.HasPosition && keepPositions && previous.TryGetValue(node.Id, out CanvasNode? old) && old.HasPosition)
                {
                    node.Position = old.Position;
                    node.HasPosition = true;
                }

                needsLayout |= !node.HasPosition;
            }

            if (_selectedId != null && !_byId.ContainsKey(_selectedId))
            {
                _selectedId = null;
            }

            StopTicker();
            _layout = needsLayout ? new GraphLayout(_nodes, _edges, roots, vertical) : null;
            _frameWhenLaidOut = frame || previous.Count == 0;
            if (_layout != null)
            {
                StepLayout(LayoutBudgetMs * 0.5);
                if (!_layout.Done && panel != null)
                {
                    _layoutTicker = schedule.Execute(() => StepLayout(LayoutBudgetMs)).Every(1);
                }
            }
            else
            {
                Rebuild();
                if (_frameWhenLaidOut)
                {
                    FrameAll();
                }
            }
        }

        /// <summary>Runs one layout slice and refreshes; true when the layout is finished.</summary>
        public bool StepLayout(double budgetMs)
        {
            if (_layout == null)
            {
                return true;
            }

            bool done = _layout.Step(budgetMs);
            if (done)
            {
                StopTicker();
                Rebuild();
                if (_frameWhenLaidOut)
                {
                    _frameWhenLaidOut = false;
                    FrameAll();
                }
            }

            return done;
        }

        public CanvasNode? Find(string id) => _byId.TryGetValue(id, out CanvasNode? node) ? node : null;

        /// <summary>Selects a card (and optionally frames it); raises SelectionChanged when it changed.</summary>
        public void Select(string? id, bool frame = false)
        {
            if (id != null && !_byId.ContainsKey(id))
            {
                id = null;
            }

            bool changed = !string.Equals(id, _selectedId, StringComparison.Ordinal);
            _selectedId = id;
            if (frame && id != null)
            {
                FrameNode(_byId[id]);
            }
            else
            {
                Refresh();
            }

            if (changed)
            {
                SelectionChanged?.Invoke(Selected);
            }
        }

        public void FrameAll()
        {
            if (_nodes.Count == 0)
            {
                return;
            }

            Rect bounds = BoundsOf(_nodes);
            FrameRect(bounds, 1.0f);
            _fitted = true;
        }

        public void FrameNode(CanvasNode node)
        {
            Rect rect = node.Rect;
            rect.xMin -= 200f;
            rect.xMax += 200f;
            rect.yMin -= 120f;
            rect.yMax += 120f;
            FrameRect(rect, Mathf.Max(_zoom, 0.8f));
            _fitted = false;
        }

        public void ZoomBy(float factor, Vector2? aroundLocal = null)
        {
            Vector2 viewport = Viewport;
            Vector2 around = aroundLocal ?? viewport * 0.5f;
            Vector2 world = (around - _pan) / _zoom;
            _zoom = Mathf.Clamp(_zoom * factor, 0.05f, 3f);
            _pan = around - world * _zoom;
            _fitted = false;
            ApplyTransform();
            Refresh();
        }

        /// <summary>Re-indexes positions, repaints edges and rebinds the visible cards.</summary>
        public void Rebuild()
        {
            foreach (List<CanvasNode> bucket in _grid.Values)
            {
                bucket.Clear();
                _gridPool.Push(bucket);
            }
            _grid.Clear();
            _worldMin = new Vector2(float.MaxValue, float.MaxValue);
            _worldMax = new Vector2(float.MinValue, float.MinValue);
            foreach (CanvasNode node in _nodes)
            {
                if (!node.HasPosition)
                {
                    continue;
                }

                AddToGrid(node);
                _worldMin = Vector2.Min(_worldMin, node.Position);
                _worldMax = Vector2.Max(_worldMax, node.Position + node.Size);
            }

            if (_worldMin.x > _worldMax.x)
            {
                _worldMin = Vector2.zero;
                _worldMax = Vector2.one;
            }

            _worldMin -= new Vector2(64f, 64f);
            _worldMax += new Vector2(64f, 64f);
            _edgeLayer.style.left = _worldMin.x;
            _edgeLayer.style.top = _worldMin.y;
            _edgeLayer.style.width = _worldMax.x - _worldMin.x;
            _edgeLayer.style.height = _worldMax.y - _worldMin.y;
            _edgeLayer.MarkDirtyRepaint();
            Refresh();
        }

        /// <summary>Rebinds the cards of the visible rect (cheap; runs on pan, zoom, selection and data changes).</summary>
        public void Refresh()
        {
            _refreshWatch.Restart();
            Vector2 viewport = Viewport;
            Rect visible = new Rect(-_pan / _zoom, viewport / _zoom);
            visible.xMin -= 32f;
            visible.yMin -= 32f;
            visible.xMax += 32f;
            visible.yMax += 32f;
            _visible.Clear();
            CollectVisible(visible, _visible);
            bool compact = _zoom < CompactZoom || _visible.Count > MaxCards;
            if (compact != _compact)
            {
                _compact = compact;
                _edgeLayer.MarkDirtyRepaint();
            }

            int bound = 0;
            if (!_compact)
            {
                foreach (CanvasNode node in _visible)
                {
                    NodeCard card = CardAt(bound++);
                    card.Bind(node, string.Equals(node.Id, _selectedId, StringComparison.Ordinal));
                }
            }

            for (int i = bound; i < _pool.Count; i++)
            {
                _pool[i].Unbind();
            }

            VisibleCardCount = bound;
            RefreshEdgeLabels(visible);
            LastRefreshMilliseconds = _refreshWatch.Elapsed.TotalMilliseconds;
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (_fitted && evt.oldRect.size != evt.newRect.size && evt.newRect.width > 1f && evt.newRect.height > 1f)
            {
                FrameAll();
                return;
            }

            Refresh();
        }

        private Vector2 Viewport
        {
            get
            {
                if (_viewportOverride.HasValue)
                {
                    return _viewportOverride.Value;
                }

                Rect rect = contentRect;
                return rect.width > 1f && rect.height > 1f ? rect.size : new Vector2(StudioViewIds.DefaultWidth, StudioViewIds.DefaultHeight);
            }
        }

        private void FrameRect(Rect bounds, float maxZoom)
        {
            Vector2 viewport = Viewport;
            float zoom = Mathf.Min(viewport.x / Mathf.Max(bounds.width, 1f), viewport.y / Mathf.Max(bounds.height, 1f)) * 0.92f;
            _zoom = Mathf.Clamp(zoom, 0.05f, maxZoom);
            _pan = viewport * 0.5f - bounds.center * _zoom;
            ApplyTransform();
            Refresh();
        }

        private void ApplyTransform()
        {
            _content.style.translate = new Translate(new Length(_pan.x), new Length(_pan.y), 0f);
            _content.style.scale = new Scale(new Vector3(_zoom, _zoom, 1f));
        }

        private void StopTicker()
        {
            _layoutTicker?.Pause();
            _layoutTicker = null;
        }

        private NodeCard CardAt(int index)
        {
            while (_pool.Count <= index)
            {
                NodeCard card = new NodeCard();
                _pool.Add(card);
                _nodeLayer.Add(card);
            }

            return _pool[index];
        }

        private void RefreshEdgeLabels(Rect visible)
        {
            int used = 0;
            if (!_compact && _zoom >= 0.55f)
            {
                foreach (CanvasEdge edge in _edges)
                {
                    if (used >= MaxEdgeLabels)
                    {
                        break;
                    }

                    if (edge.Label.Length == 0 || !_byId.TryGetValue(edge.From, out CanvasNode? from) || !_byId.TryGetValue(edge.To, out CanvasNode? to))
                    {
                        continue;
                    }

                    Vector2 middle = (Anchor(from, to.Center) + Anchor(to, from.Center)) * 0.5f;
                    if (!visible.Contains(middle))
                    {
                        continue;
                    }

                    while (_labelPool.Count <= used)
                    {
                        Label created = new Label { pickingMode = PickingMode.Ignore };
                        created.style.position = Position.Absolute;
                        created.style.fontSize = 9f;
                        created.style.paddingLeft = 3f;
                        created.style.paddingRight = 3f;
                        created.style.backgroundColor = new Color(0f, 0f, 0f, 0.35f);
                        created.style.color = new Color(0.92f, 0.92f, 0.92f);
                        created.style.borderTopLeftRadius = 3f;
                        created.style.borderTopRightRadius = 3f;
                        created.style.borderBottomLeftRadius = 3f;
                        created.style.borderBottomRightRadius = 3f;
                        _labelPool.Add(created);
                        _labelLayer.Add(created);
                    }

                    Label label = _labelPool[used++];
                    label.text = edge.Label;
                    label.style.left = middle.x - 30f;
                    label.style.top = middle.y - 7f;
                    label.style.display = DisplayStyle.Flex;
                }
            }

            for (int i = used; i < _labelPool.Count; i++)
            {
                _labelPool[i].style.display = DisplayStyle.None;
            }
        }

        /// <summary>Call after changing edge colors or highlights; panning and painting reuse these buckets.</summary>
        public void RefreshEdgeStyles()
        {
            foreach (List<CanvasEdge> bucket in _edgeGroups.Values) bucket.Clear();
            foreach (CanvasEdge edge in _edges)
            {
                Color color = edge.Highlighted ? ViewPalette.EdgeHighlight : edge.Color;
                if (!_edgeGroups.TryGetValue(color, out List<CanvasEdge>? bucket))
                {
                    bucket = new List<CanvasEdge>();
                    _edgeGroups.Add(color, bucket);
                }
                bucket.Add(edge);
            }
            _edgeLayer.MarkDirtyRepaint();
        }

        private void OnGenerateEdges(MeshGenerationContext context)
        {
            _paintWatch.Restart();
            Painter2D painter = context.painter2D;
            Vector2 origin = _worldMin;
            bool arrows = _edges.Count <= 600 && !_compact;
            foreach (KeyValuePair<Color, List<CanvasEdge>> group in _edgeGroups)
            {
                if (group.Value.Count == 0) continue;
                painter.strokeColor = group.Key;
                painter.lineWidth = _compact ? 1f : group.Value[0].Width;
                painter.BeginPath();
                foreach (CanvasEdge edge in group.Value)
                {
                    if (!_byId.TryGetValue(edge.From, out CanvasNode? from) || !_byId.TryGetValue(edge.To, out CanvasNode? to) || !from.HasPosition || !to.HasPosition)
                    {
                        continue;
                    }

                    Vector2 start = Anchor(from, to.Center) - origin;
                    Vector2 end = Anchor(to, from.Center) - origin;
                    painter.MoveTo(start);
                    painter.LineTo(end);
                    if (arrows)
                    {
                        Vector2 direction = (end - start).normalized;
                        if (direction.sqrMagnitude > 0f)
                        {
                            Vector2 normal = new Vector2(-direction.y, direction.x);
                            painter.MoveTo(end);
                            painter.LineTo(end - direction * 9f + normal * 4.5f);
                            painter.MoveTo(end);
                            painter.LineTo(end - direction * 9f - normal * 4.5f);
                        }
                    }
                }

                painter.Stroke();
            }

            if (_compact)
            {
                foreach (CanvasNode node in _nodes)
                {
                    if (!node.HasPosition)
                    {
                        continue;
                    }

                    bool selected = string.Equals(node.Id, _selectedId, StringComparison.Ordinal);
                    painter.fillColor = selected ? ViewPalette.Selection : (node.Highlighted ? ViewPalette.Highlight : node.Accent);
                    painter.BeginPath();
                    Vector2 position = node.Position - origin;
                    painter.MoveTo(position);
                    painter.LineTo(position + new Vector2(node.Size.x, 0f));
                    painter.LineTo(position + node.Size);
                    painter.LineTo(position + new Vector2(0f, node.Size.y));
                    painter.ClosePath();
                    painter.Fill();
                }
            }

            if (_gesture == Gesture.Connect && _pressed != null)
            {
                painter.strokeColor = ViewPalette.Selection;
                painter.lineWidth = 2f;
                painter.BeginPath();
                painter.MoveTo(_pressed.Center - origin);
                painter.LineTo(_connectEnd - origin);
                painter.Stroke();
            }

            LastEdgePaintMilliseconds = _paintWatch.Elapsed.TotalMilliseconds;
        }

        /// <summary>Where an edge leaves a card towards a point (the card's border).</summary>
        private static Vector2 Anchor(CanvasNode node, Vector2 toward)
        {
            Vector2 center = node.Center;
            Vector2 delta = toward - center;
            if (delta.sqrMagnitude < 0.01f)
            {
                return center;
            }

            Vector2 half = node.Size * 0.5f;
            float scale = Mathf.Min(half.x / Mathf.Max(Mathf.Abs(delta.x), 0.001f), half.y / Mathf.Max(Mathf.Abs(delta.y), 0.001f));
            return center + delta * Mathf.Min(scale, 1f);
        }

        private void AddToGrid(CanvasNode node)
        {
            int x0 = Mathf.FloorToInt(node.Position.x / GridCell);
            int y0 = Mathf.FloorToInt(node.Position.y / GridCell);
            int x1 = Mathf.FloorToInt((node.Position.x + node.Size.x) / GridCell);
            int y1 = Mathf.FloorToInt((node.Position.y + node.Size.y) / GridCell);
            for (int x = x0; x <= x1; x++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    long key = CellKey(x, y);
                    if (!_grid.TryGetValue(key, out List<CanvasNode>? list))
                    {
                        list = _gridPool.Count > 0 ? _gridPool.Pop() : new List<CanvasNode>();
                        _grid.Add(key, list);
                    }

                    list.Add(node);
                }
            }
        }

        private void CollectVisible(Rect visible, List<CanvasNode> into)
        {
            int x0 = Mathf.FloorToInt(visible.xMin / GridCell);
            int y0 = Mathf.FloorToInt(visible.yMin / GridCell);
            int x1 = Mathf.FloorToInt(visible.xMax / GridCell);
            int y1 = Mathf.FloorToInt(visible.yMax / GridCell);
            long cells = (long)(x1 - x0 + 1) * (y1 - y0 + 1);
            _visibilitySeen.Clear();
            if (cells > _grid.Count)
            {
                foreach (List<CanvasNode> list in _grid.Values)
                {
                    AddVisible(list, visible, into, _visibilitySeen);
                }

                return;
            }

            for (int x = x0; x <= x1; x++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    if (_grid.TryGetValue(CellKey(x, y), out List<CanvasNode>? list))
                    {
                        AddVisible(list, visible, into, _visibilitySeen);
                    }
                }
            }
        }

        private static void AddVisible(List<CanvasNode> list, Rect visible, List<CanvasNode> into, HashSet<string> seen)
        {
            foreach (CanvasNode node in list)
            {
                if (node.Rect.Overlaps(visible) && seen.Add(node.Id))
                {
                    into.Add(node);
                }
            }
        }

        private static long CellKey(int x, int y) => ((long)x << 32) ^ (uint)y;

        private CanvasNode? NodeAt(Vector2 world)
        {
            long key = CellKey(Mathf.FloorToInt(world.x / GridCell), Mathf.FloorToInt(world.y / GridCell));
            if (!_grid.TryGetValue(key, out List<CanvasNode>? list))
            {
                return null;
            }

            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].Rect.Contains(world))
                {
                    return list[i];
                }
            }

            return null;
        }

        private Vector2 LocalOf(Vector2 panelPosition) => this.WorldToLocal(panelPosition);

        private Vector2 WorldOf(Vector2 local) => (local - _pan) / _zoom;

        private void OnWheel(WheelEvent evt)
        {
            float factor = evt.delta.y > 0f ? 0.9f : 1.1f;
            ZoomBy(factor, LocalOf(evt.mousePosition));
            evt.StopPropagation();
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            Focus();
            Vector2 local = LocalOf(evt.position);
            CanvasNode? hit = _compact ? null : NodeAt(WorldOf(local));
            if (_compact && evt.button == 0)
            {
                hit = NodeAt(WorldOf(local));
            }

            _pressLocal = local;
            _lastLocal = local;
            _pointerId = evt.pointerId;
            if (evt.button == 2 || (evt.button == 0 && evt.altKey))
            {
                _gesture = Gesture.Pan;
            }
            else if (evt.button == 0 && hit != null)
            {
                Select(hit.Id);
                if (evt.clickCount >= 2)
                {
                    _gesture = Gesture.None;
                    NodeActivated?.Invoke(hit);
                    evt.StopPropagation();
                    return;
                }

                _pressed = hit;
                _pressedStart = hit.Position;
                _gesture = evt.actionKey ? Gesture.Connect : Gesture.DragNode;
                _connectEnd = hit.Center;
            }
            else if (evt.button == 0)
            {
                Select(null);
                _gesture = Gesture.Pan;
            }
            else
            {
                return;
            }

            this.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (_gesture == Gesture.None || evt.pointerId != _pointerId)
            {
                return;
            }

            Vector2 local = LocalOf(evt.position);
            Vector2 delta = local - _lastLocal;
            _lastLocal = local;
            switch (_gesture)
            {
                case Gesture.Pan:
                    _pan += delta;
                    _fitted = false;
                    ApplyTransform();
                    Refresh();
                    break;
                case Gesture.DragNode:
                    if (_pressed != null && (local - _pressLocal).sqrMagnitude > 9f)
                    {
                        _pressed.Position = _pressedStart + (local - _pressLocal) / _zoom;
                        Rebuild();
                    }

                    break;
                case Gesture.Connect:
                    _connectEnd = WorldOf(local);
                    _edgeLayer.MarkDirtyRepaint();
                    break;
            }

            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId != _pointerId)
            {
                return;
            }

            if (_gesture == Gesture.Connect && _pressed != null)
            {
                CanvasNode? target = NodeAt(WorldOf(LocalOf(evt.position)));
                CanvasNode source = _pressed;
                _gesture = Gesture.None;
                _edgeLayer.MarkDirtyRepaint();
                if (target != null && !ReferenceEquals(target, source))
                {
                    ConnectRequested?.Invoke(source, target);
                }
            }

            _gesture = Gesture.None;
            _pressed = null;
            if (this.HasPointerCapture(evt.pointerId))
            {
                this.ReleasePointer(evt.pointerId);
            }

            _pointerId = -1;
            evt.StopPropagation();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            CanvasNode? selected = Selected;
            switch (evt.keyCode)
            {
                case KeyCode.LeftArrow:
                    MoveSelection(new Vector2(-1f, 0f));
                    break;
                case KeyCode.RightArrow:
                    MoveSelection(new Vector2(1f, 0f));
                    break;
                case KeyCode.UpArrow:
                    MoveSelection(new Vector2(0f, -1f));
                    break;
                case KeyCode.DownArrow:
                    MoveSelection(new Vector2(0f, 1f));
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (selected != null)
                    {
                        NodeActivated?.Invoke(selected);
                    }

                    break;
                case KeyCode.F:
                    if (selected != null)
                    {
                        FrameNode(selected);
                    }

                    break;
                case KeyCode.A:
                    FrameAll();
                    break;
                case KeyCode.Equals:
                case KeyCode.Plus:
                case KeyCode.KeypadPlus:
                    ZoomBy(1.2f);
                    break;
                case KeyCode.Minus:
                case KeyCode.KeypadMinus:
                    ZoomBy(1f / 1.2f);
                    break;
                case KeyCode.LeftBracket:
                    Cycle(-1);
                    break;
                case KeyCode.RightBracket:
                    Cycle(1);
                    break;
                case KeyCode.Delete:
                case KeyCode.Backspace:
                    if (selected != null)
                    {
                        DeleteRequested?.Invoke(selected);
                    }

                    break;
                default:
                    return;
            }

            evt.StopPropagation();
        }

        /// <summary>Moves the selection to the nearest card in a direction (keyboard navigation).</summary>
        public void MoveSelection(Vector2 direction)
        {
            CanvasNode? current = Selected;
            if (current == null)
            {
                if (_nodes.Count > 0)
                {
                    Select(_nodes[0].Id, true);
                }

                return;
            }

            CanvasNode? best = null;
            float bestScore = float.MaxValue;
            foreach (CanvasNode node in _nodes)
            {
                if (ReferenceEquals(node, current) || !node.HasPosition)
                {
                    continue;
                }

                Vector2 delta = node.Center - current.Center;
                float along = Vector2.Dot(delta, direction);
                if (along <= 1f)
                {
                    continue;
                }

                float across = Mathf.Abs(Vector2.Dot(delta, new Vector2(-direction.y, direction.x)));
                float score = along + across * 2f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = node;
                }
            }

            if (best != null)
            {
                Select(best.Id, true);
            }
        }

        private void Cycle(int step)
        {
            if (_nodes.Count == 0)
            {
                return;
            }

            int index = Selected == null ? -1 : _nodes.IndexOf(Selected);
            index = (index + step + _nodes.Count) % _nodes.Count;
            Select(_nodes[index].Id, true);
        }

        private static Rect BoundsOf(IReadOnlyList<CanvasNode> nodes)
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            foreach (CanvasNode node in nodes)
            {
                if (!node.HasPosition)
                {
                    continue;
                }

                min = Vector2.Min(min, node.Position);
                max = Vector2.Max(max, node.Position + node.Size);
            }

            return min.x > max.x ? new Rect(0f, 0f, 100f, 100f) : Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>One pooled card.</summary>
        private sealed class NodeCard : VisualElement
        {
            private readonly VisualElement _accent;
            private readonly Label _title;
            private readonly Label _subtitle;
            private readonly VisualElement _badges;
            private readonly List<Label> _badgeLabels = new List<Label>();
            private readonly List<Label> _details = new List<Label>();
            private readonly VisualElement _body;

            public NodeCard()
            {
                pickingMode = PickingMode.Ignore;
                style.position = Position.Absolute;
                style.flexDirection = FlexDirection.Row;
                style.backgroundColor = ViewPalette.CardBackground;
                SetRadius(this, 5f);
                SetBorder(this, 1f, new Color(0f, 0f, 0f, 0.3f));
                _accent = new VisualElement { pickingMode = PickingMode.Ignore };
                _accent.style.width = 4f;
                _accent.style.borderTopLeftRadius = 5f;
                _accent.style.borderBottomLeftRadius = 5f;
                Add(_accent);
                _body = new VisualElement { pickingMode = PickingMode.Ignore };
                _body.style.flexGrow = 1f;
                _body.style.paddingLeft = 6f;
                _body.style.paddingRight = 6f;
                _body.style.paddingTop = 3f;
                _body.style.overflow = Overflow.Hidden;
                Add(_body);
                _title = MakeLabel(12f, FontStyle.Bold, ViewPalette.Text);
                _subtitle = MakeLabel(10f, FontStyle.Normal, ViewPalette.SubText);
                _badges = new VisualElement { pickingMode = PickingMode.Ignore };
                _badges.style.flexDirection = FlexDirection.Row;
                _badges.style.flexWrap = Wrap.Wrap;
                _body.Add(_title);
                _body.Add(_subtitle);
                _body.Add(_badges);
            }

            public void Bind(CanvasNode node, bool selected)
            {
                style.display = DisplayStyle.Flex;
                style.left = node.Position.x;
                style.top = node.Position.y;
                style.width = node.Size.x;
                style.height = node.Size.y;
                style.opacity = node.Dimmed ? 0.4f : 1f;
                _accent.style.backgroundColor = node.Accent;
                Color border = selected ? ViewPalette.Selection : (node.Highlighted ? ViewPalette.Highlight : new Color(0f, 0f, 0f, 0.3f));
                SetBorder(this, selected || node.Highlighted ? 2f : 1f, border);
                _title.text = node.Title;
                _subtitle.text = node.Subtitle;
                _subtitle.style.display = node.Subtitle.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                for (int i = 0; i < node.Badges.Count; i++)
                {
                    while (_badgeLabels.Count <= i)
                    {
                        Label badge = MakeLabel(9f, FontStyle.Bold, Color.white);
                        badge.style.paddingLeft = 4f;
                        badge.style.paddingRight = 4f;
                        badge.style.marginRight = 3f;
                        badge.style.marginTop = 1f;
                        SetRadius(badge, 3f);
                        _badgeLabels.Add(badge);
                        _badges.Add(badge);
                    }

                    _badgeLabels[i].text = node.Badges[i].Text;
                    _badgeLabels[i].style.backgroundColor = node.Badges[i].Color;
                    _badgeLabels[i].style.display = DisplayStyle.Flex;
                }

                for (int i = node.Badges.Count; i < _badgeLabels.Count; i++)
                {
                    _badgeLabels[i].style.display = DisplayStyle.None;
                }

                int detailCount = Math.Min(node.Details.Count, 4);
                for (int i = 0; i < detailCount; i++)
                {
                    while (_details.Count <= i)
                    {
                        Label detail = MakeLabel(10f, FontStyle.Normal, ViewPalette.SubText);
                        _details.Add(detail);
                        _body.Add(detail);
                    }

                    _details[i].text = node.Details[i];
                    _details[i].style.display = DisplayStyle.Flex;
                }

                for (int i = detailCount; i < _details.Count; i++)
                {
                    _details[i].style.display = DisplayStyle.None;
                }
            }

            public void Unbind()
            {
                style.display = DisplayStyle.None;
            }

            private static Label MakeLabel(float size, FontStyle weight, Color color)
            {
                Label label = new Label { pickingMode = PickingMode.Ignore };
                label.style.fontSize = size;
                label.style.unityFontStyleAndWeight = weight;
                label.style.color = color;
                label.style.whiteSpace = WhiteSpace.NoWrap;
                label.style.overflow = Overflow.Hidden;
                label.style.textOverflow = TextOverflow.Ellipsis;
                label.style.marginLeft = 0f;
                label.style.paddingLeft = 0f;
                return label;
            }
        }

        internal static void SetRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        internal static void SetBorder(VisualElement element, float width, Color color)
        {
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
        }
    }
}

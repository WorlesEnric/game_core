// GameCore.Studio.Views - the data a graph canvas shows: nodes (cards) and edges, plus the views' shared palette.
#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.Views.Canvas
{
    /// <summary>A small coloured label on a card (type, stale, residency, condition, visited...).</summary>
    public sealed class CanvasBadge
    {
        public CanvasBadge(string text, Color color)
        {
            Text = text ?? string.Empty;
            Color = color;
        }

        public string Text { get; }

        public Color Color { get; }
    }

    /// <summary>One card on the canvas. Positions are world units; the canvas pans and zooms over them.</summary>
    public sealed class CanvasNode
    {
        public CanvasNode(string id, string title)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Title = title ?? string.Empty;
        }

        public string Id { get; }

        public string Title { get; set; }

        public string Subtitle { get; set; } = string.Empty;

        public List<CanvasBadge> Badges { get; } = new List<CanvasBadge>();

        public List<string> Details { get; } = new List<string>();

        public Color Accent { get; set; } = ViewPalette.Neutral;

        public Vector2 Position { get; set; }

        public Vector2 Size { get; set; } = new Vector2(220f, 72f);

        /// <summary>True when the position is fixed (by the view or a previous layout); layout leaves it alone.</summary>
        public bool HasPosition { get; set; }

        /// <summary>Layer hint for the layered layout (e.g. BFS depth); -1 = computed.</summary>
        public int Layer { get; set; } = -1;

        /// <summary>Highlighted cards get a bright outline (preview path, impact, search hits).</summary>
        public bool Highlighted { get; set; }

        /// <summary>Dimmed cards are drawn faded (filtered out but kept for context).</summary>
        public bool Dimmed { get; set; }

        /// <summary>The view's own object behind the card (an index key, a node index...).</summary>
        public object? Payload { get; set; }

        public Rect Rect => new Rect(Position, Size);

        public Vector2 Center => Position + Size * 0.5f;
    }

    /// <summary>A directed edge between two cards.</summary>
    public sealed class CanvasEdge
    {
        public CanvasEdge(string from, string to, string label = "")
        {
            From = from ?? throw new ArgumentNullException(nameof(from));
            To = to ?? throw new ArgumentNullException(nameof(to));
            Label = label ?? string.Empty;
        }

        public string From { get; }

        public string To { get; }

        public string Label { get; set; }

        public Color Color { get; set; } = ViewPalette.Edge;

        public float Width { get; set; } = 1.5f;

        public bool Highlighted { get; set; }

        public object? Payload { get; set; }
    }

    /// <summary>The views' colours (theme-aware where it matters).</summary>
    public static class ViewPalette
    {
        public static readonly Color Neutral = new Color(0.45f, 0.5f, 0.58f);
        public static readonly Color Edge = new Color(0.55f, 0.6f, 0.68f, 0.9f);
        public static readonly Color EdgeHighlight = new Color(1f, 0.78f, 0.2f, 1f);
        public static readonly Color Selection = new Color(0.25f, 0.6f, 1f, 1f);
        public static readonly Color Highlight = new Color(1f, 0.78f, 0.2f, 1f);
        public static readonly Color Good = new Color(0.3f, 0.75f, 0.4f);
        public static readonly Color Warn = new Color(0.95f, 0.7f, 0.2f);
        public static readonly Color Bad = new Color(0.9f, 0.35f, 0.3f);
        public static readonly Color Info = new Color(0.35f, 0.6f, 0.95f);
        public static readonly Color Muted = new Color(0.5f, 0.5f, 0.5f);
        public static readonly Color Purple = new Color(0.65f, 0.45f, 0.9f);
        public static readonly Color Teal = new Color(0.25f, 0.7f, 0.7f);

        public static Color CardBackground => EditorGUIUtility.isProSkin ? new Color(0.2f, 0.21f, 0.23f, 1f) : new Color(0.93f, 0.93f, 0.94f, 1f);

        public static Color CanvasBackground => EditorGUIUtility.isProSkin ? new Color(0.14f, 0.145f, 0.155f, 1f) : new Color(0.82f, 0.83f, 0.85f, 1f);

        public static Color Text => EditorGUIUtility.isProSkin ? new Color(0.9f, 0.9f, 0.9f) : new Color(0.1f, 0.1f, 0.1f);

        public static Color SubText => EditorGUIUtility.isProSkin ? new Color(0.65f, 0.67f, 0.7f) : new Color(0.3f, 0.3f, 0.32f);

        public static Color Grid => EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.04f) : new Color(0f, 0f, 0f, 0.06f);

        /// <summary>A stable accent per type id (hash into a small hue wheel).</summary>
        public static Color ForType(string type)
        {
            if (string.IsNullOrEmpty(type))
            {
                return Neutral;
            }

            if (type.StartsWith("world.", StringComparison.Ordinal))
            {
                return Teal;
            }

            if (type.StartsWith("dialogue.", StringComparison.Ordinal))
            {
                return Purple;
            }

            if (type.StartsWith("quest.", StringComparison.Ordinal))
            {
                return Warn;
            }

            if (type.StartsWith("npc.", StringComparison.Ordinal))
            {
                return Good;
            }

            if (type.StartsWith("inventory.", StringComparison.Ordinal))
            {
                return new Color(0.85f, 0.55f, 0.3f);
            }

            if (type.StartsWith("logic.", StringComparison.Ordinal) || type.StartsWith("narrative.", StringComparison.Ordinal))
            {
                return Info;
            }

            if (type.StartsWith("entity.", StringComparison.Ordinal))
            {
                return new Color(0.6f, 0.7f, 0.35f);
            }

            uint hash = 2166136261u;
            foreach (char character in type)
            {
                hash = (hash ^ character) * 16777619u;
            }

            return Color.HSVToRGB((hash % 360u) / 360f, 0.45f, 0.8f);
        }
    }
}

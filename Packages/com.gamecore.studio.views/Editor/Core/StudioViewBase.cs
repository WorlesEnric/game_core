// GameCore.Studio.Views - the shared shell of every view: a VisualElement bound to a StudioViewContext (so tests and the
// evidence entry build views without windows), a toolbar, a status line for the last change-set report, and an
// EditorWindow host per view id. Views refresh from StudioViewContext.Changed (index, engine, journal, play mode).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using GameCore.Studio.Views.Canvas;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameCore.Studio.Views
{
    /// <summary>The base of the six views.</summary>
    public abstract class StudioViewBase : VisualElement, IDisposable
    {
        private readonly Label _status;
        private bool _disposed;

        protected StudioViewBase(StudioViewContext context, string viewId)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            ViewId = viewId;
            name = viewId;
            focusable = true;
            style.flexGrow = 1f;
            style.flexDirection = FlexDirection.Column;
            style.minWidth = StudioViewIds.MinWidth;
            style.minHeight = StudioViewIds.MinHeight;
            Toolbar = new Toolbar();
            Toolbar.style.flexShrink = 0f;
            Add(Toolbar);
            Body = new VisualElement { name = "view-body" };
            Body.style.flexGrow = 1f;
            Body.style.flexDirection = FlexDirection.Row;
            Add(Body);
            _status = new Label { name = "view-status" };
            _status.style.flexShrink = 0f;
            _status.style.paddingLeft = 6f;
            _status.style.paddingTop = 2f;
            _status.style.paddingBottom = 2f;
            _status.style.fontSize = 10f;
            _status.style.whiteSpace = WhiteSpace.NoWrap;
            _status.style.overflow = Overflow.Hidden;
            _status.style.borderTopWidth = 1f;
            _status.style.borderTopColor = new Color(0f, 0f, 0f, 0.25f);
            Add(_status);
            Context.Changed += OnContextChanged;
            Context.Edits.Reported += OnReported;
            Context.Selection.SelectionChanged += OnBridgeSelectionChanged;
        }

        public StudioViewContext Context { get; }

        public string ViewId { get; }

        public Toolbar Toolbar { get; }

        public VisualElement Body { get; }

        public string StatusText => _status.text;

        /// <summary>Refreshes count since creation (tests check incremental behaviour).</summary>
        public int RefreshCount { get; private set; }

        /// <summary>Reads the current index/journal/play state into the view.</summary>
        public void Refresh()
        {
            RefreshCount++;
            OnRefresh();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Context.Changed -= OnContextChanged;
            Context.Edits.Reported -= OnReported;
            Context.Selection.SelectionChanged -= OnBridgeSelectionChanged;
            OnDispose();
        }

        public void SetStatus(string text, Color? color = null)
        {
            _status.text = text ?? string.Empty;
            _status.style.color = color ?? ViewPalette.SubText;
        }

        /// <summary>Applies a change set built by the view and shows the outcome.</summary>
        protected ApplyReport ApplyEdit(ChangeSet changeSet)
        {
            ApplyReport report = Context.Edits.Apply(changeSet);
            Refresh();
            return report;
        }

        protected abstract void OnRefresh();

        protected virtual void OnSelectionChanged()
        {
        }

        protected virtual void OnDispose()
        {
        }

        protected ToolbarButton AddButton(string text, Action action, string tooltip = "")
        {
            ToolbarButton button = new ToolbarButton(action) { text = text, tooltip = tooltip };
            Toolbar.Add(button);
            return button;
        }

        protected void AddSpacer()
        {
            Toolbar.Add(new ToolbarSpacer { style = { flexGrow = 1f } });
        }

        protected ToolbarSearchField AddSearch(Action<string> changed)
        {
            ToolbarSearchField search = new ToolbarSearchField();
            search.style.width = 180f;
            search.RegisterValueChangedCallback(evt => changed(evt.newValue ?? string.Empty));
            Toolbar.Add(search);
            return search;
        }

        /// <summary>A vertical side panel with a title (inspectors, preview, rules).</summary>
        public static VisualElement Panel(string title, float width)
        {
            VisualElement panel = new VisualElement();
            panel.style.width = width;
            panel.style.minWidth = 200f;
            panel.style.flexShrink = 0f;
            panel.style.borderLeftWidth = 1f;
            panel.style.borderLeftColor = new Color(0f, 0f, 0f, 0.3f);
            panel.style.paddingLeft = 6f;
            panel.style.paddingRight = 6f;
            panel.style.paddingTop = 4f;
            Label header = new Label(title);
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginBottom = 4f;
            panel.Add(header);
            return panel;
        }

        public static Label Text(string text, float size = 11f, FontStyle weight = FontStyle.Normal)
        {
            Label label = new Label(text);
            label.style.fontSize = size;
            label.style.unityFontStyleAndWeight = weight;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        /// <summary>A small coloured chip.</summary>
        public static Label Chip(string text, Color color)
        {
            Label chip = new Label(text);
            chip.style.backgroundColor = color;
            chip.style.color = Color.white;
            chip.style.fontSize = 9f;
            chip.style.unityFontStyleAndWeight = FontStyle.Bold;
            chip.style.paddingLeft = 4f;
            chip.style.paddingRight = 4f;
            chip.style.marginRight = 3f;
            GraphCanvas.SetRadius(chip, 3f);
            return chip;
        }

        private void OnContextChanged()
        {
            if (!_disposed)
            {
                Refresh();
            }
        }

        private void OnReported(ApplyReport report)
        {
            SetStatus(ViewEdits.Describe(report), report.Ok ? ViewPalette.Good : ViewPalette.Bad);
        }

        private void OnBridgeSelectionChanged()
        {
            if (!_disposed)
            {
                OnSelectionChanged();
            }
        }
    }

    /// <summary>The window hosting one view (menu GameCore/Studio/...; minimum 640x360, opened at 1280x720).</summary>
    public abstract class StudioViewWindow : EditorWindow
    {
        private StudioViewContext? _ownedContext;

        /// <summary>The hosted view (null until CreateGUI ran or when the runtime failed).</summary>
        public StudioViewBase? View { get; private set; }

        protected abstract string ViewTitle { get; }

        protected abstract StudioViewBase CreateView(StudioViewContext context);

        /// <summary>Opens (or focuses) a window of type <typeparamref name="T"/> at the default size.</summary>
        public static T Open<T>() where T : StudioViewWindow
        {
            T window = GetWindow<T>();
            window.minSize = new Vector2(StudioViewIds.MinWidth, StudioViewIds.MinHeight);
            if (window.position.width < StudioViewIds.MinWidth || window.position.height < StudioViewIds.MinHeight)
            {
                window.position = new Rect(60f, 60f, StudioViewIds.DefaultWidth, StudioViewIds.DefaultHeight);
            }

            window.Show();
            window.Focus();
            return window;
        }

        /// <summary>Hosts a view over an explicit context (evidence, integrator layouts); the caller owns the context.</summary>
        public void Host(StudioViewContext context)
        {
            rootVisualElement.Clear();
            View?.Dispose();
            _ownedContext?.Dispose();
            _ownedContext = null;
            View = CreateView(context);
            rootVisualElement.Add(View);
            View.Refresh();
        }

        public void CreateGUI()
        {
            titleContent = new GUIContent(ViewTitle);
            minSize = new Vector2(StudioViewIds.MinWidth, StudioViewIds.MinHeight);
            if (View != null)
            {
                rootVisualElement.Add(View);
                return;
            }

            try
            {
                _ownedContext = StudioViewContext.ForProject();
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                rootVisualElement.Add(new HelpBox("The Studio runtime is not available: " + error.Message, HelpBoxMessageType.Error));
                return;
            }

            try
            {
                View = CreateView(_ownedContext);
            }
            catch (InvalidOperationException error)
            {
                rootVisualElement.Add(new HelpBox(error.Message, HelpBoxMessageType.Warning));
                return;
            }

            rootVisualElement.Add(View);
            View.Refresh();
        }

        /// <summary>Drops the hosted view and builds it again (a plugin window rebinding to another id).</summary>
        protected void Rebuild()
        {
            View?.Dispose();
            View = null;
            _ownedContext?.Dispose();
            _ownedContext = null;
            rootVisualElement.Clear();
            CreateGUI();
        }

        private void OnDisable()
        {
            View?.Dispose();
            View = null;
            _ownedContext?.Dispose();
            _ownedContext = null;
        }
    }

    /// <summary>An in-memory selection bridge (tests, evidence, hosts without P2.1).</summary>
    public sealed class ListSelectionBridge : IStudioSelectionBridge
    {
        private readonly List<AuthoringRef> _current = new List<AuthoringRef>();

        public IReadOnlyList<AuthoringRef> Current => _current;

        public AuthoringRef? Focused { get; private set; }

        public event Action? SelectionChanged;

        public void Select(IReadOnlyList<AuthoringRef> refs)
        {
            _current.Clear();
            _current.AddRange(refs ?? Array.Empty<AuthoringRef>());
            SelectionChanged?.Invoke();
        }

        public void Focus(AuthoringRef target)
        {
            Focused = target;
            Select(new[] { target });
        }
    }
}

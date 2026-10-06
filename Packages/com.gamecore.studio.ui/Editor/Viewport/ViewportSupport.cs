// GameCore.Studio.UI - viewport helpers: modes, Play-mode input routing to the game (Input System), B-SELECT timing
// samples and the overlap list popup.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

namespace GameCore.Studio.UI
{
    /// <summary>Viewport interaction modes (Tab cycles them).</summary>
    public enum ViewportMode
    {
        /// <summary>Input goes to the game (Play Mode with a running world).</summary>
        Play,
        /// <summary>Input goes to picking: hover highlight, click, shift-add, ctrl-toggle, marquee, point-at.</summary>
        Select,
        /// <summary>Hover shows the authoring card; click selects.</summary>
        Inspect,
    }

    /// <summary>
    /// Routes keyboard and mouse to the game while the viewport is focused in Play mode: the Input System's editor
    /// play-mode behaviour is switched to "all device input always goes to the game view" and its background behaviour
    /// to "ignore focus", so the game's input sources (Hollowmere: PlayerInputAdapter over InputSystemIntentSource and
    /// Input/Player.inputactions) read the devices although the Game view is not focused; both settings are restored
    /// when routing stops. There is no second input path.
    /// </summary>
    public sealed class PlayInputRouting : IDisposable
    {
        private readonly Action<InputEventPtr, InputDevice> _handler;
        private bool _active;
        private bool _subscribed;
        private Func<bool>? _blockInput;
        private InputSettings.EditorInputBehaviorInPlayMode _previous;
        private InputSettings.BackgroundBehavior _previousBackground;

        public PlayInputRouting()
        {
            _handler = OnEvent;
        }

        public bool Active => _active;

        public void Guard(Func<bool> blockInput)
        {
            _blockInput = blockInput;
            Subscribe();
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            InputSystem.onEvent += _handler;
            _subscribed = true;
        }

        /// <summary>Input System events seen while routing (keyboard/mouse/gamepad state and delta events).</summary>
        public long EventsRouted { get; private set; }

        /// <summary>Starts or stops routing.</summary>
        public void Update(bool route)
        {
            if (route && !_active)
            {
                InputSettings settings = InputSystem.settings;
                _previous = settings.editorInputBehaviorInPlayMode;
                _previousBackground = settings.backgroundBehavior;
                settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                // With all input going to the game, the background behaviour applies as in a player; the Game view is
                // not focused while the viewport is, so devices must not be disabled for lack of focus.
                settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                Subscribe();
                _active = true;
            }
            else if (!route && _active)
            {
                Stop();
            }
        }

        public void Dispose()
        {
            Stop();
            if (_subscribed) InputSystem.onEvent -= _handler;
            _subscribed = false;
            _blockInput = null;
        }

        private void Stop()
        {
            if (!_active)
            {
                return;
            }

            _active = false;
            // Cancel held actions before another control can receive input.
            foreach (InputDevice device in InputSystem.devices)
                if (device is Keyboard || device is Mouse || device is Gamepad) InputSystem.ResetDevice(device);
            InputSettings settings = InputSystem.settings;
            if (settings != null)
            {
                settings.editorInputBehaviorInPlayMode = _previous;
                settings.backgroundBehavior = _previousBackground;
            }
        }

        private void OnEvent(InputEventPtr eventPtr, InputDevice device)
        {
            if (_blockInput?.Invoke() == true && (device is Keyboard || device is Mouse || device is Gamepad))
            {
                eventPtr.handled = true;
                return;
            }
            if (_active) EventsRouted++;
        }
    }

    /// <summary>B-SELECT samples (hover, click, marquee, point-at) in milliseconds.</summary>
    public sealed class SelectTimings
    {
        public const int Capacity = 512;

        private readonly Dictionary<string, List<double>> _samples = new Dictionary<string, List<double>>(StringComparer.Ordinal);

        public void Add(string kind, double milliseconds)
        {
            if (!_samples.TryGetValue(kind, out List<double>? list))
            {
                list = new List<double>();
                _samples[kind] = list;
            }

            if (list.Count >= Capacity)
            {
                list.RemoveAt(0);
            }

            list.Add(milliseconds);
        }

        public int Count(string kind) => _samples.TryGetValue(kind, out List<double>? list) ? list.Count : 0;

        /// <summary>The p-th percentile (0..100) of a kind, or NaN without samples.</summary>
        public double Percentile(string kind, double p)
        {
            if (!_samples.TryGetValue(kind, out List<double>? list) || list.Count == 0)
            {
                return double.NaN;
            }

            List<double> sorted = new List<double>(list);
            sorted.Sort();
            int index = (int)Math.Ceiling((p / 100.0) * sorted.Count) - 1;
            return sorted[Math.Max(0, Math.Min(sorted.Count - 1, index))];
        }

        /// <summary>"click n=12 p50=1.2 p95=2.3 max=3.1; ..." for logs and evidence.</summary>
        public string Report()
        {
            List<string> parts = new List<string>();
            List<string> kinds = new List<string>(_samples.Keys);
            kinds.Sort(StringComparer.Ordinal);
            foreach (string kind in kinds)
            {
                List<double> list = _samples[kind];
                double max = 0;
                foreach (double value in list)
                {
                    max = Math.Max(max, value);
                }

                parts.Add(kind + " n=" + list.Count.ToString(CultureInfo.InvariantCulture)
                    + " p50=" + Percentile(kind, 50).ToString("0.00", CultureInfo.InvariantCulture)
                    + " p95=" + Percentile(kind, 95).ToString("0.00", CultureInfo.InvariantCulture)
                    + " max=" + max.ToString("0.00", CultureInfo.InvariantCulture) + " ms");
            }

            return parts.Count == 0 ? "no picks" : string.Join("; ", parts);
        }
    }

    /// <summary>The overlap list (03 s2): candidates under the cursor in depth order with occlusion flags.</summary>
    public sealed class OverlapPopup : VisualElement
    {
        private readonly Action<PickCandidate, PickChoice> _choose;
        private readonly Func<PickCandidate, PickChoice, bool>? _available;
        private bool _shown;

        /// <param name="choose">Called with the chosen candidate and whether "this part" was chosen.</param>
        public OverlapPopup(Action<PickCandidate, bool> choose)
            : this((candidate, choice) => choose(candidate, choice == PickChoice.Part), null)
        {
        }

        public OverlapPopup(Action<PickCandidate, PickChoice> choose, Func<PickCandidate, PickChoice, bool>? available)
        {
            _choose = choose ?? throw new ArgumentNullException(nameof(choose));
            _available = available;
            name = "overlap-popup";
            AddToClassList("gcs-popup");
            style.position = Position.Absolute;
            style.display = DisplayStyle.None;
            RegisterCallback<GeometryChangedEvent>(_ => ClampToParent());
        }

        public IReadOnlyList<PickCandidate> Candidates { get; private set; } = Array.Empty<PickCandidate>();

        public bool Visible => _shown && Candidates.Count > 0;

        public void Show(IReadOnlyList<PickCandidate> candidates, Vector2 at, Func<AuthoringRef, string> nameOf)
        {
            ScrollView list = Begin(candidates, "objects here (nearest first)");
            for (int i = 0; i < candidates.Count; i++)
            {
                PickCandidate candidate = candidates[i];
                VisualElement row = new VisualElement();
                row.AddToClassList("gcs-row");
                AddChoice(row, candidate, PickChoice.Logical, LabelFor(candidate, i, nameOf), "overlap-" + i);
                if (candidate.Part != null)
                    AddChoice(row, candidate, PickChoice.Part, "part " + candidate.Part, "overlap-part-" + i);
                if (_available != null)
                {
                    AddChoice(row, candidate, PickChoice.Prefab, "Prefab", "overlap-prefab-" + i);
                    AddChoice(row, candidate, PickChoice.InstanceScope, "Scope: Instance", "overlap-scope-" + i);
                }
                row.style.flexWrap = Wrap.Wrap;
                list.Add(row);
            }
            Present(at);
        }

        public void ShowMarquee(IReadOnlyList<PickCandidate> candidates, Vector2 at, Func<AuthoringRef, string> nameOf,
            Action<IReadOnlyList<PickCandidate>> apply)
        {
            ScrollView list = Begin(candidates, "marquee candidates · choose objects to keep");
            var included = new List<Toggle>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                var toggle = new Toggle(LabelFor(candidates[i], i, nameOf))
                    { name = "overlap-include-" + i, value = true };
                included.Add(toggle);
                list.Add(toggle);
            }
            Add(new Button(() =>
            {
                var chosen = new List<PickCandidate>();
                for (int i = 0; i < candidates.Count; i++)
                    if (included[i].value) chosen.Add(candidates[i]);
                Hide();
                apply(chosen);
            }) { name = "overlap-apply", text = "Select chosen objects" });
            Present(at);
        }

        private ScrollView Begin(IReadOnlyList<PickCandidate> candidates, string title)
        {
            Candidates = candidates;
            Clear();
            var label = new Label(StudioStyles.Safe(candidates.Count + " " + title));
            label.AddToClassList("gcs-section__title");
            Add(label);
            var list = new ScrollView(ScrollViewMode.Vertical);
            list.style.flexShrink = 1;
            Add(list);
            return list;
        }

        private static string LabelFor(PickCandidate candidate, int index, Func<AuthoringRef, string> nameOf)
            => StudioStyles.Safe((index + 1).ToString(CultureInfo.InvariantCulture) + ". " + nameOf(candidate.Ref)
                + " · " + candidate.Ref.Kind + " · " + candidate.Distance.ToString("0.0", CultureInfo.InvariantCulture)
                + " m" + (candidate.Occluded ? " · occluded" : string.Empty));

        private void AddChoice(VisualElement row, PickCandidate candidate, PickChoice choice, string text, string buttonName)
        {
            var button = new Button(() => { Hide(); _choose(candidate, choice); })
                { text = StudioStyles.Safe(text), name = buttonName };
            button.AddToClassList("gcs-popup__item");
            button.SetEnabled(_available == null || _available(candidate, choice));
            row.Add(button);
        }

        private void Present(Vector2 at)
        {
            style.left = at.x;
            style.top = at.y;
            style.display = DisplayStyle.Flex;
            _shown = true;
            ClampToParent();
        }

        private void ClampToParent()
        {
            if (!_shown || parent == null || !float.IsFinite(parent.contentRect.width)) return;
            float width = Mathf.Max(0, parent.contentRect.width - 16);
            float height = Mathf.Max(0, parent.contentRect.height - 16);
            style.maxWidth = width;
            style.maxHeight = height;
            style.left = Mathf.Clamp(style.left.value.value, 8, Mathf.Max(8, width - layout.width));
            style.top = Mathf.Clamp(style.top.value.value, 8, Mathf.Max(8, height - layout.height));
        }

        public void Hide()
        {
            style.display = DisplayStyle.None;
            Candidates = Array.Empty<PickCandidate>();
            _shown = false;
        }
    }
}

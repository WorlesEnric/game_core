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
        private InputSettings.EditorInputBehaviorInPlayMode _previous;
        private InputSettings.BackgroundBehavior _previousBackground;

        public PlayInputRouting()
        {
            _handler = OnEvent;
        }

        public bool Active => _active;

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
                InputSystem.onEvent += _handler;
                _active = true;
            }
            else if (!route && _active)
            {
                Stop();
            }
        }

        public void Dispose() => Stop();

        private void Stop()
        {
            if (!_active)
            {
                return;
            }

            _active = false;
            InputSystem.onEvent -= _handler;
            InputSettings settings = InputSystem.settings;
            if (settings != null)
            {
                settings.editorInputBehaviorInPlayMode = _previous;
                settings.backgroundBehavior = _previousBackground;
            }
        }

        private void OnEvent(InputEventPtr eventPtr, InputDevice device)
        {
            EventsRouted++;
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
        private readonly Action<PickCandidate, bool> _choose;
        private bool _shown;

        /// <param name="choose">Called with the chosen candidate and whether "this part" was chosen.</param>
        public OverlapPopup(Action<PickCandidate, bool> choose)
        {
            _choose = choose ?? throw new ArgumentNullException(nameof(choose));
            name = "overlap-popup";
            AddToClassList("gcs-popup");
            style.position = Position.Absolute;
            style.display = DisplayStyle.None;
        }

        public IReadOnlyList<PickCandidate> Candidates { get; private set; } = Array.Empty<PickCandidate>();

        public bool Visible => _shown && Candidates.Count > 0;

        public void Show(IReadOnlyList<PickCandidate> candidates, Vector2 at, Func<AuthoringRef, string> nameOf)
        {
            Candidates = candidates;
            Clear();
            Label title = new Label(candidates.Count + " objects here (nearest first)");
            title.AddToClassList("gcs-section__title");
            Add(title);
            for (int i = 0; i < candidates.Count; i++)
            {
                PickCandidate candidate = candidates[i];
                VisualElement row = new VisualElement();
                row.AddToClassList("gcs-row");
                string text = (i + 1).ToString(CultureInfo.InvariantCulture) + ". " + nameOf(candidate.Ref) + " · " + candidate.Ref.Kind
                    + " · " + candidate.Distance.ToString("0.0", CultureInfo.InvariantCulture) + " m" + (candidate.Occluded ? " · occluded" : string.Empty)
                    + (candidate.Source == PickSource.Ui ? " · ui" : string.Empty);
                Button choose = new Button(() =>
                {
                    Hide();
                    _choose(candidate, false);
                })
                { text = text, name = "overlap-" + i.ToString(CultureInfo.InvariantCulture) };
                choose.AddToClassList("gcs-popup__item");
                choose.EnableInClassList("gcs-popup__item--occluded", candidate.Occluded);
                row.Add(choose);
                if (candidate.Part != null)
                {
                    row.Add(new Button(() =>
                    {
                        Hide();
                        _choose(candidate, true);
                    }) { text = "part " + candidate.Part, tooltip = "Select this part (the logical owner stays the target)" });
                }

                Add(row);
            }

            style.left = at.x;
            style.top = at.y;
            style.display = DisplayStyle.Flex;
            _shown = true;
        }

        public void Hide()
        {
            style.display = DisplayStyle.None;
            Candidates = Array.Empty<PickCandidate>();
            _shown = false;
        }
    }
}

// GameCore.Studio.UI - the viewport's pump assertion indicator (02 s7, SR-8.2, W-UI-04, B-FRAME). The application root
// installs exactly one pump; its GameApplicationPumpCounter counts sanctioned pumps, duplicate pumps in one host frame
// and pumps that bypassed the application pump. The monitor samples the counter once per editor update and reports
// pumps per frame over a sliding window of frames, plus the frame time. In Play with a running world the indicator is
// red unless every observed frame had exactly one sanctioned pump and no violation appeared. The viewport itself never
// pumps: it only renders the game camera into a texture.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Unity.App;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.UI
{
    /// <summary>Health of the one-pump invariant as the indicator shows it.</summary>
    public enum PumpHealth
    {
        /// <summary>No running world (Edit mode, or Play without a GameCore application).</summary>
        NoWorld,
        /// <summary>The world is paused, stopped or the editor is paused: no pumps are expected.</summary>
        Idle,
        /// <summary>Exactly one sanctioned pump per frame, no violation.</summary>
        Ok,
        /// <summary>Pumps per frame differ from 1, or a duplicate/bypass pump was counted.</summary>
        Violation,
    }

    /// <summary>Samples the application root's pump counter.</summary>
    public sealed class PumpMonitor
    {
        /// <summary>Frames in the sliding window.</summary>
        public const int Window = 120;

        private readonly Queue<FrameSample> _samples = new Queue<FrameSample>();
        private GameApplicationRoot? _root;
        private int _lastFrame = -1;
        private int _lastSanctioned;
        private int _lastViolations;
        private int _windowFrames;
        private int _windowPumps;
        private double _windowFrameMs;

        public PumpHealth Health { get; private set; } = PumpHealth.NoWorld;

        /// <summary>Sanctioned pumps per frame over the window (NaN without frames).</summary>
        public double PumpsPerFrame => _windowFrames == 0 ? double.NaN : (double)_windowPumps / _windowFrames;

        /// <summary>Mean frame time over the window, ms.</summary>
        public double FrameMs => _windowFrames == 0 ? 0 : _windowFrameMs / _windowFrames;

        public double MaxFrameMs { get; private set; }

        public int DuplicatePumps { get; private set; }

        public int BypassPumps { get; private set; }

        /// <summary>Frames observed since the world appeared (whole Play session).</summary>
        public long FramesObserved { get; private set; }

        public long PumpsObserved { get; private set; }

        /// <summary>Frames whose sanctioned pump count was not exactly one while running.</summary>
        public long FramesOffByOne { get; private set; }

        public string LastViolation { get; private set; } = string.Empty;

        public string StateText { get; private set; } = "no world";

        /// <summary>Samples the current application (call once per editor update).</summary>
        public void Sample()
        {
            GameApplicationRoot? root = EditorApplication.isPlaying ? GameApplication.Current : null;
            if (root == null || root.State == GameApplicationState.Stopped)
            {
                if (_root != null)
                {
                    Reset();
                }

                Health = PumpHealth.NoWorld;
                StateText = EditorApplication.isPlaying ? "no GameCore world" : "edit mode";
                return;
            }

            GameApplicationPumpCounter counter = root.PumpCounter;
            if (!ReferenceEquals(root, _root))
            {
                Reset();
                _root = root;
                _lastFrame = Time.frameCount;
                _lastSanctioned = counter.SanctionedPumps;
                _lastViolations = counter.Violations;
                DuplicatePumps = counter.DuplicateFramePumps;
                BypassPumps = counter.BypassPumps;
            }

            int frame = Time.frameCount;
            if (frame == _lastFrame)
            {
                return;
            }

            int frames = frame - _lastFrame;
            int pumps = counter.SanctionedPumps - _lastSanctioned;
            bool running = root.State == GameApplicationState.Running && !EditorApplication.isPaused;
            float delta = Time.unscaledDeltaTime * 1000f;
            _lastFrame = frame;
            _lastSanctioned = counter.SanctionedPumps;
            bool newViolation = counter.Violations != _lastViolations;
            _lastViolations = counter.Violations;
            DuplicatePumps = counter.DuplicateFramePumps;
            BypassPumps = counter.BypassPumps;
            if (newViolation)
            {
                LastViolation = counter.LastViolation;
            }

            StateText = root.State.ToString().ToLowerInvariant() + (EditorApplication.isPaused ? " (editor paused)" : string.Empty);
            if (!running)
            {
                Health = newViolation ? PumpHealth.Violation : PumpHealth.Idle;
                return;
            }

            FramesObserved += frames;
            PumpsObserved += pumps;
            if (pumps != frames)
            {
                FramesOffByOne += Math.Abs(frames - pumps);
            }

            Push(new FrameSample(frames, pumps, delta));
            MaxFrameMs = Math.Max(MaxFrameMs, delta);
            Health = newViolation || _windowPumps != _windowFrames ? PumpHealth.Violation : PumpHealth.Ok;
        }

        /// <summary>Indicator text: "1.00 pumps/frame · 16.6 ms".</summary>
        public string Text()
        {
            if (Health == PumpHealth.NoWorld)
            {
                return "pump: " + StateText;
            }

            string ratio = double.IsNaN(PumpsPerFrame) ? "-" : PumpsPerFrame.ToString("0.00", CultureInfo.InvariantCulture);
            string text = ratio + " pumps/frame · " + FrameMs.ToString("0.0", CultureInfo.InvariantCulture) + " ms";
            if (DuplicatePumps > 0 || BypassPumps > 0)
            {
                text += " · dup " + DuplicatePumps.ToString(CultureInfo.InvariantCulture) + " bypass " + BypassPumps.ToString(CultureInfo.InvariantCulture);
            }

            if (Health == PumpHealth.Idle)
            {
                text += " · " + StateText;
            }

            return text;
        }

        /// <summary>One line for logs and evidence.</summary>
        public string Report()
        {
            return "health=" + Health + " pumpsPerFrame=" + (double.IsNaN(PumpsPerFrame) ? "n/a" : PumpsPerFrame.ToString("0.000", CultureInfo.InvariantCulture))
                + " framesObserved=" + FramesObserved.ToString(CultureInfo.InvariantCulture)
                + " pumpsObserved=" + PumpsObserved.ToString(CultureInfo.InvariantCulture)
                + " framesOffByOne=" + FramesOffByOne.ToString(CultureInfo.InvariantCulture)
                + " duplicate=" + DuplicatePumps.ToString(CultureInfo.InvariantCulture)
                + " bypass=" + BypassPumps.ToString(CultureInfo.InvariantCulture)
                + " meanFrameMs=" + FrameMs.ToString("0.00", CultureInfo.InvariantCulture)
                + " maxFrameMs=" + MaxFrameMs.ToString("0.00", CultureInfo.InvariantCulture)
                + " state=" + StateText;
        }

        public void Reset()
        {
            _root = null;
            _samples.Clear();
            _windowFrames = 0;
            _windowPumps = 0;
            _windowFrameMs = 0;
            _lastFrame = -1;
            FramesObserved = 0;
            PumpsObserved = 0;
            FramesOffByOne = 0;
            MaxFrameMs = 0;
            DuplicatePumps = 0;
            BypassPumps = 0;
            LastViolation = string.Empty;
        }

        private void Push(FrameSample sample)
        {
            _samples.Enqueue(sample);
            _windowFrames += sample.Frames;
            _windowPumps += sample.Pumps;
            _windowFrameMs += sample.FrameMs * sample.Frames;
            while (_windowFrames > Window && _samples.Count > 1)
            {
                FrameSample old = _samples.Dequeue();
                _windowFrames -= old.Frames;
                _windowPumps -= old.Pumps;
                _windowFrameMs -= old.FrameMs * old.Frames;
            }
        }

        private readonly struct FrameSample
        {
            public FrameSample(int frames, int pumps, double frameMs)
            {
                Frames = frames;
                Pumps = pumps;
                FrameMs = frameMs;
            }

            public int Frames { get; }

            public int Pumps { get; }

            public double FrameMs { get; }
        }
    }
}

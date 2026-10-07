#nullable enable
using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Hollowmere.Game
{
    public enum FrameSubsystem { Autoplay, AutoplayLog, Presentation, FrameRecorder }

    /// <summary>Elapsed main-thread scopes, keyed by work frame, not the following delta sample.
    /// Nested scopes overlap; their durations must never be added together.</summary>
    public sealed class FrameAttribution
    {
        private readonly int[] frames = { -1, -1, -1 };
        private readonly double[,] milliseconds = new double[3, 4];

        public Scope Measure(int frame, FrameSubsystem subsystem) => new Scope(this, frame, subsystem);

        public void Record(int frame, FrameSubsystem subsystem, double elapsedMilliseconds)
        {
            if (frame < 0 || (uint)subsystem >= 4 ||
                double.IsNaN(elapsedMilliseconds) || double.IsInfinity(elapsedMilliseconds) || elapsedMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(elapsedMilliseconds));
            int slot = frame % frames.Length;
            if (frames[slot] != frame)
            {
                frames[slot] = frame;
                for (int i = 0; i < 4; i++) milliseconds[slot, i] = 0;
            }
            milliseconds[slot, (int)subsystem] += elapsedMilliseconds;
        }

        public double Milliseconds(int frame, FrameSubsystem subsystem) =>
            frame >= 0 && frames[frame % frames.Length] == frame ? milliseconds[frame % frames.Length, (int)subsystem] : 0;

        public string Describe(int workFrame)
        {
            // Prefer the more specific nested log scope when both it and Autoplay exceed the limit.
            string owner = "unattributed";
            if (Milliseconds(workFrame, FrameSubsystem.AutoplayLog) > 100) owner = "autoplay.log";
            else if (Milliseconds(workFrame, FrameSubsystem.Autoplay) > 100) owner = "autoplay";
            else if (Milliseconds(workFrame, FrameSubsystem.Presentation) > 100) owner = "game.presentation";
            else if (Milliseconds(workFrame, FrameSubsystem.FrameRecorder) > 100) owner = "frame.recorder";
            var text = new StringBuilder(owner);
            for (int i = 0; i < 4; i++)
                text.Append(',').Append(Milliseconds(workFrame, (FrameSubsystem)i).ToString("F3", CultureInfo.InvariantCulture));
            return text.ToString();
        }

        public readonly struct Scope : IDisposable
        {
            private readonly FrameAttribution? owner;
            private readonly int frame;
            private readonly FrameSubsystem subsystem;
            private readonly long started;
            internal Scope(FrameAttribution owner, int frame, FrameSubsystem subsystem)
            {
                this.owner = owner;
                this.frame = frame;
                this.subsystem = subsystem;
                started = Stopwatch.GetTimestamp();
            }
            public void Dispose() => owner?.Record(frame, subsystem,
                (Stopwatch.GetTimestamp() - started) * (1000.0 / Stopwatch.Frequency));
        }
    }
}

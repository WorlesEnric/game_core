// GameCore.Unity.Adapters — the adapter frame: what one host frame asks of the installed engine adapters (GC-019).
//
// Normative sources: 00 P-002 (an `EngineAdapter` admits observations and presents committed output; it is a role,
// not a second authority), P-045 (observers see immutable images at publication only; irreversible output adapters
// consume committed output), 04 s3 (the pump algorithm: "collect adapter input and completed host callbacks", then
// "obtain the number of logical steps", then "update presentation from the last published snapshot") and 04 s7
// ("Presentation work never causes a second authoritative simulation update").
//
// There is exactly one update path in V1 and it is the existing application pump (04 s3). An adapter never installs a
// PlayerLoop node, a MonoBehaviour `Update`, or a second world pump; it is *called by* the pump at the two points the
// pump algorithm already names. This file owns those two points as a narrow port plus the per-world registry the
// pump consults, so the adapters stay a caller of the one update path rather than another one.
//
// The two calls are deliberately separated and ordered:
//   * `CollectInput` runs before the host's own pump, so a command it admits into the world's existing command port
//     is part of the demand the very same frame consumes (P-037, P-042).
//   * `Present` runs after the host's own pump, so it can only read what that pump committed (P-045). It cannot
//     advance a step, admit a command, or make gameplay depend on a renderer.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Execution;

namespace GameCore.Unity.Adapters
{
    /// <summary>Result of one adapter-frame call (P-058: an unavailable adapter is explicitly reported, not silent).</summary>
    public enum AdapterFrameOutcome
    {
        /// <summary>No adapter frame is registered for the world: the pump did nothing and says so.</summary>
        Skipped = 0,

        /// <summary>The call ran and its own counters carry the outcome.</summary>
        Completed = 1,

        /// <summary>The call refused: an adapter is installed but cannot do the work (for example, no source).</summary>
        Refused = 2,

        /// <summary>The call threw; the pump records it and the world keeps running (P-031: no simulation is unwound).</summary>
        Faulted = 3,
    }

    /// <summary>
    /// What one adapter-frame call did. Counts are observations, never a second authority: <see cref="Items"/> and
    /// <see cref="Views"/> describe adapter output only.
    /// </summary>
    public readonly struct AdapterFrameReport
    {
        public AdapterFrameReport(AdapterFrameOutcome outcome, string adapter, int items, int views, string detail)
        {
            Outcome = outcome;
            Adapter = adapter ?? string.Empty;
            Items = items;
            Views = views;
            Detail = detail ?? string.Empty;
        }

        public AdapterFrameOutcome Outcome { get; }

        /// <summary>Name of the adapter that produced this report; empty for a skipped world.</summary>
        public string Adapter { get; }

        /// <summary>Admitted commands, installed asset leases or presented targets, by adapter.</summary>
        public int Items { get; }

        /// <summary>Views touched by a presentation call; zero is the ordinary headless answer.</summary>
        public int Views { get; }

        public string Detail { get; }

        public bool Ran => Outcome == AdapterFrameOutcome.Completed;

        public static AdapterFrameReport Skipped(string detail) =>
            new AdapterFrameReport(AdapterFrameOutcome.Skipped, string.Empty, 0, 0, detail);

        public static AdapterFrameReport Completed(string adapter, int items, int views, string detail) =>
            new AdapterFrameReport(AdapterFrameOutcome.Completed, adapter, items, views, detail);

        public static AdapterFrameReport Refused(string adapter, string detail) =>
            new AdapterFrameReport(AdapterFrameOutcome.Refused, adapter, 0, 0, detail);

        public static AdapterFrameReport Faulted(string adapter, string detail) =>
            new AdapterFrameReport(AdapterFrameOutcome.Faulted, adapter, 0, 0, detail);

        public override string ToString() =>
            string.IsNullOrEmpty(Adapter)
                ? Outcome.ToString()
                : Outcome.ToString() + "(" + Adapter + ": items=" + Items.ToString(CultureInfo.InvariantCulture)
                    + ", views=" + Views.ToString(CultureInfo.InvariantCulture) + ", " + Detail + ")";
    }

    /// <summary>
    /// The engine adapters of one world, as the single application pump sees them. Both members must be safe to call
    /// on an idle world: an idle command-driven world still collects input and still presents, it just commits no
    /// step (P-036, 04 s3).
    /// </summary>
    public interface IAdapterFrame
    {
        /// <summary>Identity of the world this frame belongs to; it never drives another world (P-004).</summary>
        WorldId World { get; }

        /// <summary>
        /// Samples host input, stamps it and offers each typed command to the world's existing command port. A
        /// sampled device event is not itself a committed game event: this call only produces admissions (04 s7).
        /// Runs before the host pump's step admission.
        /// </summary>
        AdapterFrameReport CollectInput();

        /// <summary>
        /// Presents the last published committed output into the registered views. It reads committed output only
        /// and never advances a step (P-045). Runs after the host pump.
        /// </summary>
        AdapterFrameReport Present();
    }

    /// <summary>
    /// Per-process registry of the adapter frame of each owned world. The pump looks a world up by identity, so a
    /// frame can never be driven for a world it does not belong to, and a new Play Mode session cannot inherit the
    /// previous session's frame (04 s9).
    /// </summary>
    public static class AdapterFrameRegistry
    {
        private static readonly Dictionary<Id128, IAdapterFrame> frames = new Dictionary<Id128, IAdapterFrame>();
        private static readonly List<Id128> order = new List<Id128>();

        /// <summary>Adapter-frame calls the pump made since the last reset, by direction.</summary>
        public static int InputCalls { get; private set; }

        public static int PresentationCalls { get; private set; }

        /// <summary>Calls that found no registered frame, so "no adapter installed" is visible rather than implied.</summary>
        public static int SkippedCalls { get; private set; }

        /// <summary>Calls that threw; the pump keeps running and the failure is counted (P-031).</summary>
        public static int FaultedCalls { get; private set; }

        /// <summary>Last report of each direction, for evidence and for the probe's step details.</summary>
        public static AdapterFrameReport LastInputReport { get; private set; }

        public static AdapterFrameReport LastPresentationReport { get; private set; }

        public static int Count => frames.Count;

        public static IReadOnlyList<IAdapterFrame> Frames()
        {
            var all = new List<IAdapterFrame>(order.Count);
            for (int i = 0; i < order.Count; i++)
            {
                if (frames.TryGetValue(order[i], out IAdapterFrame? frame) && frame != null)
                {
                    all.Add(frame);
                }
            }

            return all;
        }

        /// <summary>
        /// Registers one world's adapter frame. A replacement is explicit (a second frame for the same world is a
        /// caller error, reported as <c>true</c> rather than silently shadowing the first).
        /// </summary>
        public static bool Register(IAdapterFrame frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            if (frame.World.Session.IsDefault)
            {
                throw new ArgumentException("An adapter frame must name a live world session (P-004).", nameof(frame));
            }

            bool replaced = frames.ContainsKey(frame.World.Session);
            if (!replaced)
            {
                order.Add(frame.World.Session);
                order.Sort(CompareIds);
            }

            frames[frame.World.Session] = frame;
            return replaced;
        }

        /// <summary>Removes one world's frame; the caller that installed it is the caller that removes it (04 s9).</summary>
        public static bool Unregister(WorldId world) =>
            frames.Remove(world.Session) && RemoveOrder(world.Session);

        public static bool TryGet(WorldId world, out IAdapterFrame? frame) =>
            frames.TryGetValue(world.Session, out frame);

        /// <summary>Adapter frames of this process, in canonical world order (P-008).</summary>
        public static IReadOnlyList<WorldId> Worlds()
        {
            var worlds = new List<WorldId>(order.Count);
            for (int i = 0; i < order.Count; i++)
            {
                worlds.Add(new WorldId(order[i]));
            }

            return worlds;
        }

        /// <summary>
        /// The pump's input point. It runs on the main thread, like the world pump it precedes (04 s3), and it
        /// reports rather than throws so an adapter defect cannot abort a host frame.
        /// </summary>
        public static AdapterFrameReport CollectInput(WorldId world)
        {
            GameCoreThreading.RequireMainThread("AdapterFrameRegistry.CollectInput");
            InputCalls++;
            if (!frames.TryGetValue(world.Session, out IAdapterFrame? frame) || frame == null)
            {
                SkippedCalls++;
                LastInputReport = AdapterFrameReport.Skipped("no adapter frame is registered for this world");
                return LastInputReport;
            }

            LastInputReport = Invoke(frame, collectingInput: true);
            return LastInputReport;
        }

        /// <summary>The pump's presentation point, after the world pump has committed whatever it was going to commit.</summary>
        public static AdapterFrameReport Present(WorldId world)
        {
            GameCoreThreading.RequireMainThread("AdapterFrameRegistry.Present");
            PresentationCalls++;
            if (!frames.TryGetValue(world.Session, out IAdapterFrame? frame) || frame == null)
            {
                SkippedCalls++;
                LastPresentationReport = AdapterFrameReport.Skipped("no adapter frame is registered for this world");
                return LastPresentationReport;
            }

            LastPresentationReport = Invoke(frame, collectingInput: false);
            return LastPresentationReport;
        }

        /// <summary>Clears every registration; called by the per-session reset path (04 s9).</summary>
        public static void Reset()
        {
            frames.Clear();
            order.Clear();
            InputCalls = 0;
            PresentationCalls = 0;
            SkippedCalls = 0;
            FaultedCalls = 0;
            LastInputReport = AdapterFrameReport.Skipped("reset");
            LastPresentationReport = AdapterFrameReport.Skipped("reset");
        }

        private static AdapterFrameReport Invoke(IAdapterFrame frame, bool collectingInput)
        {
            try
            {
                return collectingInput ? frame.CollectInput() : frame.Present();
            }
            catch (Exception failure)
            {
                FaultedCalls++;
                return AdapterFrameReport.Faulted(
                    frame.GetType().Name,
                    failure.GetType().Name + ": " + failure.Message);
            }
        }

        private static bool RemoveOrder(Id128 session)
        {
            for (int i = 0; i < order.Count; i++)
            {
                if (order[i].Equals(session))
                {
                    order.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        private static int CompareIds(Id128 left, Id128 right) => left.CompareTo(right);
    }
}

// GameCore.Execution.Tests.Delivery - the scripted-crash fixture of the durable delivery seam (GC-021).
//
// Normative sources: docs/game-core/04-unity-integration.md s6 ("An early Unity integration probe must throw from a
// managed stage after a write and prove that the next stage never runs" — the same discipline applied to a
// persistence boundary: a fault is placed at a *named* boundary, and the failure that follows it has one defined
// meaning) and docs/game-core/00-core-protocols.md P-045 (delivery is at-least-once, and the boundary between "the
// destination mutated" and "this process recorded that it did" is exactly the window a durable outbox must survive).
//
// WHY THIS FILE LIVES IN THE TEST FOLDER
//
// `DurableDeliveryAdapter` can only *report* a persistence boundary through `IDeliveryStepHook`; it cannot crash.
// The scripting marker, its boundary enum and its exception therefore live here, in the test assembly, and are
// compiled into the pure dotnet test project and the Unity EditMode suite — never into a shipping build. A release
// player that somehow reached this file would contain no crash-on-demand capability, so 04 s6's release-surface rule
// needs no new carve-out for GC-021.
//
// It is deterministic by construction: one marker names one boundary, raises exactly once, and reports every boundary
// it observed. A probability or an elapsed-time trigger could not prove that a *specific* pair of boundaries behaves
// asymmetrically, and that asymmetry is the whole claim ("a crash before the append loses the obligation; a crash
// after it keeps it").
#nullable enable
using System;
using GameCore.Execution.Delivery;

namespace GameCore.Execution.Tests.Delivery
{
    /// <summary>
    /// One scripted persistence boundary a crash can be placed at (GC-021). Each value names a *side* of a boundary,
    /// so a test proves both that the write happened and that it had not happened yet.
    /// </summary>
    public enum DeliveryCrashPoint
    {
        /// <summary>No crash is armed; the adapter runs to completion.</summary>
        None = 0,

        /// <summary>Reached before an obligation is appended to the journal: nothing was persisted.</summary>
        BeforeAppend = 1,

        /// <summary>Reached after the append and before the in-memory outbox applies it: the obligation survived.</summary>
        AfterAppend = 2,

        /// <summary>Reached before an attempt is handed to the destination: the destination was not touched.</summary>
        BeforeDelivery = 3,

        /// <summary>Reached after the destination was asked and before the attempt is recorded: the ack-loss window.</summary>
        AfterDelivery = 4,

        /// <summary>Reached before an acknowledgement is persisted: the destination's mutation stands unrecorded.</summary>
        BeforeAcknowledge = 5,

        /// <summary>Reached after the acknowledgement was persisted: a redelivery must find it and do nothing.</summary>
        AfterAcknowledge = 6,
    }

    /// <summary>The process died at a scripted persistence boundary; a test asserts this and then recovers.</summary>
    public sealed class DeliveryCrashException : Exception
    {
        public DeliveryCrashException(DeliveryCrashPoint point, string detail)
            : base(detail)
        {
            Point = point;
        }

        public DeliveryCrashException()
        {
            Point = DeliveryCrashPoint.None;
        }

        public DeliveryCrashException(string message)
            : base(message)
        {
            Point = DeliveryCrashPoint.None;
        }

        public DeliveryCrashException(string message, Exception innerException)
            : base(message, innerException)
        {
            Point = DeliveryCrashPoint.None;
        }

        /// <summary>The scripted boundary the process died at (GC-021).</summary>
        public DeliveryCrashPoint Point { get; }
    }

    /// <summary>
    /// A deterministic crash placed at one persistence boundary, as the delivery seam's test hook. It raises at most
    /// once, so a recovery path that reaches the same boundary a second time is not silently interrupted — which is
    /// what lets one test run "crash, recover, redeliver, acknowledge" in a single process (P-045, P-049).
    /// </summary>
    public sealed class DeliveryCrashMarker : IDeliveryStepHook
    {
        public DeliveryCrashMarker(DeliveryCrashPoint point)
        {
            Point = point;
        }

        /// <summary>The boundary this marker raises at; <see cref="DeliveryCrashPoint.None"/> never raises.</summary>
        public DeliveryCrashPoint Point { get; }

        /// <summary>True when the marker will raise at some boundary (an explicit opt-in, never a default).</summary>
        public bool Armed => Point != DeliveryCrashPoint.None;

        /// <summary>True once the marker has raised; the marker is spent and will not raise again.</summary>
        public bool Spent { get; private set; }

        /// <summary>How many boundaries were observed, so a test can prove the path really reached them.</summary>
        public int ReachCount { get; private set; }

        /// <summary>True when <paramref name="boundary"/> is the one this marker is armed at.</summary>
        public bool Observes(string boundary) => Armed && string.Equals(boundary, NameOf(Point), StringComparison.Ordinal);

        /// <summary>
        /// Called by the adapter at each boundary. Raises a <see cref="DeliveryCrashException"/> the first time the
        /// armed boundary is reached and records the crossing either way.
        /// </summary>
        public void Reach(string boundary, string detail)
        {
            ReachCount++;
            if (Spent || !Observes(boundary))
            {
                return;
            }

            Spent = true;
            throw new DeliveryCrashException(
                Point,
                "scripted delivery crash at " + boundary + ": " + detail + " (GC-021).");
        }

        /// <summary>The seam name of one scripted boundary, so a marker and the adapter agree on the vocabulary.</summary>
        public static string NameOf(DeliveryCrashPoint point)
        {
            switch (point)
            {
                case DeliveryCrashPoint.BeforeAppend: return DeliveryBoundaries.BeforeAppend;
                case DeliveryCrashPoint.AfterAppend: return DeliveryBoundaries.AfterAppend;
                case DeliveryCrashPoint.BeforeDelivery: return DeliveryBoundaries.BeforeDelivery;
                case DeliveryCrashPoint.AfterDelivery: return DeliveryBoundaries.AfterDelivery;
                case DeliveryCrashPoint.BeforeAcknowledge: return DeliveryBoundaries.BeforeAcknowledge;
                case DeliveryCrashPoint.AfterAcknowledge: return DeliveryBoundaries.AfterAcknowledge;
                default: return DeliveryBoundaries.None;
            }
        }

        public override string ToString() =>
            "crashMarker(" + Point.ToString() + (Spent ? ",spent" : ",live") + ")";
    }
}

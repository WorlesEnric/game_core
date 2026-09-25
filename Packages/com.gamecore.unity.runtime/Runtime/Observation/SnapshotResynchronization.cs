// GameCore.Execution.Observation — snapshot resynchronization for a lagging reader (GC-016).
//
// P-045: "Lagging readers receive `CursorExpired` and resynchronize from a snapshot." O-17's probe requires the
// expired cursor to return "an explicit error/resync token". This type is that answer: one value naming the newest
// committed image a reader must restart from, the event cursor at that boundary, and how many events the reader
// can no longer receive. It never pretends the gap did not happen and it never advances the world.
#nullable enable
using System;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Execution.Observation
{
    /// <summary>
    /// Where a lagging reader restarts from: the newest retained committed image, the event cursor at that
    /// boundary, and the count of events retention dropped before it. <see cref="Resynchronized"/> is false only
    /// when the world has published nothing to restart from, in which case <see cref="Code"/> names why.
    /// </summary>
    public sealed class SnapshotResynchronization
    {
        private SnapshotResynchronization(
            bool resynchronized,
            SnapshotToken token,
            EventCursor cursor,
            ulong droppedEvents,
            int retainedImages,
            DiagnosticCode code)
        {
            Resynchronized = resynchronized;
            Token = token;
            Cursor = cursor;
            DroppedEvents = droppedEvents;
            RetainedImages = retainedImages;
            Code = code;
        }

        public bool Resynchronized { get; }

        /// <summary>The image a resynchronizing reader should lease; default when nothing is retained.</summary>
        public SnapshotToken Token { get; }

        /// <summary>The cursor a resynchronizing reader continues from: the last committed event of the world.</summary>
        public EventCursor Cursor { get; }

        /// <summary>Events retention dropped; the reader's gap, reported rather than hidden (P-045).</summary>
        public ulong DroppedEvents { get; }

        /// <summary>Committed images retained at the moment of resynchronization (retention evidence).</summary>
        public int RetainedImages { get; }

        public DiagnosticCode Code { get; }

        public string CodeText => DiagnosticCodeText.Of(Code);

        public static SnapshotResynchronization From(
            SnapshotToken token,
            EventCursor cursor,
            ulong droppedEvents,
            int retainedImages) =>
            new SnapshotResynchronization(true, token, cursor, droppedEvents, retainedImages, DiagnosticCode.None);

        /// <summary>Nothing has been published (or nothing is retained), so there is no snapshot to restart from.</summary>
        public static SnapshotResynchronization Unavailable(WorldId world, ulong droppedEvents) =>
            new SnapshotResynchronization(
                false,
                default(SnapshotToken),
                new EventCursor(world, EventSequence.Zero),
                droppedEvents,
                0,
                DiagnosticCode.ResultExpired);

        public override string ToString() =>
            (Resynchronized ? "Resync" : "ResyncUnavailable")
            + "(" + Token.ToString() + ", dropped=" + DroppedEvents.ToString(CultureInfo.InvariantCulture)
            + ", images=" + RetainedImages.ToString(CultureInfo.InvariantCulture)
            + (Resynchronized ? ")" : ", " + CodeText + ")");
    }
}

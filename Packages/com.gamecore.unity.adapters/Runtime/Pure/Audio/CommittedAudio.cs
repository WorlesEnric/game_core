// GameCore.Unity.Adapters.Audio — the committed-output audio stage: a sink port, a stage that consumes committed
// events only, and the exact-once/idempotency rules an irreversible output adapter needs (GC-020).
//
// Normative sources: 00 P-045 ("Irreversible output adapters consume only committed events, use explicit external
// idempotency keys, and persist an outbox when delivery must survive crashes"), P-002 (`EngineAdapter` "presents
// committed output"), 04 s7's Audio row ("None by default | Committed events trigger playback with stable event IDs
// for duplicate suppression; an audio failure does not undo simulation") and 04 s7's "Headless compositions omit
// presentation services ... gameplay cannot depend on an audio device or a renderer to progress."
//
// WHY THE SINK IS A PORT. The headless qualification player runs with Unity audio DISABLED: FMOD/PulseAudio crashed
// at exit (crash-139), so re-enabling audio in the headless probe is forbidden. The committed-output logic therefore
// must be testable without a live audio device — every rule below is engine-free and lives behind
// <see cref="IAudioOutputSink"/>, whose test implementation records calls instead of playing anything. Only
// <c>UnityAudioOutputSink</c> touches `AudioSource`/`AudioClip`, and it is installed by an audio-enabled application,
// never by the headless validation player.
//
// The stage consumes the world's own `CommittedEventStore` through `ICommittedEventReader` (P-045's committed
// boundary) and never the live ECS state, so an audio failure can never rewind simulation and a replayed page can
// never double-play: the cursor and the per-event identity both guard it.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Unity.Adapters.Audio
{
    /// <summary>What one audio submission did (P-045: delivery is at-least-once, playback is exactly-once).</summary>
    public enum AudioSubmissionOutcome
    {
        /// <summary>The sink started playback for a newly seen event.</summary>
        Played = 0,

        /// <summary>The event identity was already played, so the duplicate was suppressed.</summary>
        SuppressedDuplicate = 1,

        /// <summary>The sink is unavailable (no device, disposed); the event stays unplayed and is reported.</summary>
        RefusedUnavailable = 2,

        /// <summary>The sink rejected the cue itself (unknown cue, bad payload), with its own reason.</summary>
        RefusedBySink = 3,
    }

    /// <summary>
    /// The engine port of an audio output. An implementation must never throw out of
    /// <see cref="TryPlay"/>: a playback failure is a value, because it must not undo a committed step (04 s7).
    /// </summary>
    public interface IAudioOutputSink
    {
        /// <summary>Stable identity of this sink, for diagnostics (never a Unity instance id).</summary>
        string SinkName { get; }

        /// <summary>False when the sink cannot play; submissions then report `RefusedUnavailable`.</summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Starts playback of one committed cue. <paramref name="cue"/> is the stable cue identity and
        /// <paramref name="eventKey"/> is the world's own committed event identity, which an implementation may use as
        /// its engine-level duplicate key. False with a non-empty <paramref name="detail"/> is a refusal.
        /// </summary>
        bool TryPlay(Id128 cue, Id128 eventKey, FrozenPayload payload, out string detail);
    }

    /// <summary>One committed event's audio cue, resolved from its payload schema by the installing application.</summary>
    public readonly struct AudioCueBinding
    {
        /// <summary>The payload schema whose committed events produce this cue (P-045's schema identity).</summary>
        public readonly SchemaRef Schema;

        /// <summary>The stable cue identity handed to the sink.</summary>
        public readonly Id128 Cue;

        /// <summary>Builds one binding.</summary>
        public AudioCueBinding(SchemaRef schema, Id128 cue)
        {
            Schema = schema;
            Cue = cue;
        }
    }

    /// <summary>The closed cue table: a committed event's schema resolves to at most one cue, or to nothing at all.</summary>
    public sealed class AudioCueTable
    {
        private readonly List<AudioCueBinding> bindings = new List<AudioCueBinding>();

        /// <summary>Cue bindings declared, in canonical schema order (P-008; never dictionary order).</summary>
        public IReadOnlyList<AudioCueBinding> Bindings => bindings;

        /// <summary>Declares one cue binding; a duplicate schema is refused so one event cannot play twice.</summary>
        public bool TryAdd(SchemaRef schema, Id128 cue)
        {
            if (schema.Id.IsDefault || cue.IsDefault)
            {
                return false;
            }

            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].Schema.Id.Value.Equals(schema.Id.Value))
                {
                    return false;
                }
            }

            bindings.Add(new AudioCueBinding(schema, cue));
            bindings.Sort(CompareBindings);
            return true;
        }

        /// <summary>Resolves one committed event's schema to its cue; false means this schema is not audible.</summary>
        public bool TryResolve(SchemaRef schema, out Id128 cue)
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].Schema.Id.Value.Equals(schema.Id.Value))
                {
                    cue = bindings[i].Cue;
                    return true;
                }
            }

            cue = default(Id128);
            return false;
        }

        private static int CompareBindings(AudioCueBinding left, AudioCueBinding right) =>
            left.Schema.Id.Value.CompareTo(right.Schema.Id.Value);
    }

    /// <summary>
    /// The committed-output audio stage. It reads committed events from the world's own reader, resolves each
    /// audible event to its cue, and submits it to the sink with the world's event identity as the duplicate key.
    ///
    /// Three properties make it safe for an irreversible output adapter:
    ///   * it reads the committed event store only, so it can never observe a tentative or uncommitted value (P-045);
    ///   * it advances its own cursor only after a submission was decided, and it suppresses a repeated event
    ///     identity even if a page is replayed or read twice (P-045's at-least-once delivery);
    ///   * a sink refusal leaves the event counted and unplayed, so an application can retry it, while nothing in
    ///     simulation is unwound (04 s7).
    /// </summary>
    public sealed class CommittedAudioStage
    {
        private readonly ICommittedEventReader reader;
        private readonly IAudioOutputSink sink;
        private readonly AudioCueTable cues;
        private readonly Dictionary<Id128, byte> played = new Dictionary<Id128, byte>();
        private readonly List<Id128> playedOrder = new List<Id128>();

        private readonly int maxPlayed;

        private EventCursor cursor;

        /// <summary>Builds the stage over one world's committed-event reader and one output sink.</summary>
        public CommittedAudioStage(
            WorldId world,
            ICommittedEventReader reader,
            IAudioOutputSink sink,
            AudioCueTable cues,
            int maxPlayed = 256)
        {
            if (world.Session.IsDefault)
            {
                throw new ArgumentException("an audio stage must name a live world session (P-004).", nameof(world));
            }

            World = world;
            this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
            this.cues = cues ?? throw new ArgumentNullException(nameof(cues));
            this.maxPlayed = maxPlayed < 1 ? 1 : maxPlayed;
            cursor = new EventCursor(world, default(EventSequence));
        }

        /// <summary>The world this stage presents for; it never reads another world's events (P-004).</summary>
        public WorldId World { get; }

        /// <summary>Events withdrawn from the reader (whether or not they were audible).</summary>
        public int ReadCount { get; private set; }

        /// <summary>Events a cue resolved for, i.e. the audible ones.</summary>
        public int AudibleCount { get; private set; }

        /// <summary>Playback submissions the sink accepted.</summary>
        public int PlayedCount { get; private set; }

        /// <summary>Submissions suppressed because that event identity had already played (P-045, 04 s7).</summary>
        public int SuppressedCount { get; private set; }

        /// <summary>Submissions refused because the sink was unavailable; these remain unplayed and retryable.</summary>
        public int RefusedCount { get; private set; }

        /// <summary>Submissions the sink itself rejected; these remain unplayed and retryable.</summary>
        public int SinkRejectionCount { get; private set; }

        /// <summary>Events read whose schema no cue declares, so a silently inaudible event is visible.</summary>
        public int InaudibleCount { get; private set; }

        /// <summary>Cue identities this stage has played, in play order (a stable audit trail).</summary>
        public IReadOnlyList<Id128> PlayedEventKeys => playedOrder;

        /// <summary>The cursor one page read consumes from.</summary>
        public EventCursor Cursor => cursor;

        /// <summary>
        /// Reads and presents the next page of committed events. It never advances the world, admits a command or
        /// writes gameplay state; a sink refusal is reported and does not stop the rest of the page (P-045).
        /// </summary>
        public int PresentNextPage(int maxEvents)
        {
            int limit = maxEvents < 1 ? 1 : maxEvents;
            CommittedEventPage page = reader.Read(cursor, limit);
            if (page == null || page.Events == null)
            {
                return 0;
            }

            int playedThisPage = 0;
            for (int i = 0; i < page.Events.Count; i++)
            {
                CommittedEvent committed = page.Events[i];
                ReadCount++;
                if (!committed.Cursor.World.Session.Equals(World.Session))
                {
                    // A foreign world's page is not this stage's to present (P-004).
                    continue;
                }

                if (!cues.TryResolve(committed.Schema, out Id128 cue))
                {
                    InaudibleCount++;
                    continue;
                }

                AudibleCount++;
                Id128 eventKey = EventKeyOf(committed);
                if (played.ContainsKey(eventKey))
                {
                    // The identity was already played: at-least-once delivery must not become a repeated cue.
                    SuppressedCount++;
                    continue;
                }

                if (!sink.IsAvailable)
                {
                    RefusedCount++;
                    continue;
                }

                if (!sink.TryPlay(cue, eventKey, committed.Payload, out string _))
                {
                    SinkRejectionCount++;
                    continue;
                }

                Remember(eventKey);
                PlayedCount++;
                playedThisPage++;
            }

            cursor = page.NextCursor;
            return playedThisPage;
        }

        /// <summary>
        /// The stable external idempotency key of one committed event: the world session and the event sequence,
        /// which is what P-045's `(WorldId, event sequence)` deduplication names. It is a value, so a caller can
        /// hand it to its own durable outbox.
        /// </summary>
        public static Id128 EventKeyOf(CommittedEvent committed)
        {
            unchecked
            {
                ulong sequence = committed.Cursor.Sequence.Value;
                ulong session = committed.Cursor.World.Session.Low;
                ulong high = committed.Cursor.World.Session.High ^ sequence;
                return new Id128(high, session ^ (sequence * 0x9E3779B97F4A7C15UL));
            }
        }

        /// <summary>Clears the played set; the caller asserts it is doing so deliberately (a fresh session).</summary>
        public void ResetPlayedSet()
        {
            played.Clear();
            playedOrder.Clear();
        }

        private void Remember(Id128 eventKey)
        {
            played[eventKey] = 1;
            playedOrder.Add(eventKey);
            while (playedOrder.Count > maxPlayed)
            {
                Id128 oldest = playedOrder[0];
                playedOrder.RemoveAt(0);
                played.Remove(oldest);
            }
        }
    }

    /// <summary>
    /// A sink that plays nothing and records everything: the qualification player's audio output. It exists because
    /// the headless player runs with Unity audio disabled (crash-139), so the committed-output logic must be provable
    /// without a device.
    /// </summary>
    public sealed class RecordingAudioSink : IAudioOutputSink
    {
        private readonly List<Played> plays = new List<Played>();

        /// <summary>When true, every submission is refused as unavailable (the disabled-device case).</summary>
        public bool Unavailable { get; set; }

        /// <summary>When true, the next submission is rejected by the sink itself.</summary>
        public bool RejectNext { get; set; }

        /// <summary>The device state this sink reports on every submission.</summary>
        public bool IsAvailable => !Unavailable;

        /// <summary>Stable name of this recording sink.</summary>
        public string SinkName => "recording-audio-sink";

        /// <summary>Submissions the sink accepted, in order; nothing was played.</summary>
        public IReadOnlyList<Played> Plays => plays;

        /// <summary>Rejections this sink performed.</summary>
        public int RejectionCount { get; private set; }

        /// <inheritdoc />
        public bool TryPlay(Id128 cue, Id128 eventKey, FrozenPayload payload, out string detail)
        {
            detail = string.Empty;
            if (Unavailable)
            {
                detail = "this recording sink reports no audio device";
                return false;
            }

            if (RejectNext)
            {
                RejectNext = false;
                RejectionCount++;
                detail = "this recording sink was configured to reject one submission";
                return false;
            }

            if (payload == null)
            {
                detail = "a cue carries the committed payload it was resolved from (P-045)";
                return false;
            }

            plays.Add(new Played(cue, eventKey, payload.Length));
            return true;
        }

        /// <summary>One recorded submission.</summary>
        public readonly struct Played
        {
            /// <summary>The stable cue identity.</summary>
            public readonly Id128 Cue;

            /// <summary>The world's committed event identity used as the duplicate key.</summary>
            public readonly Id128 EventKey;

            /// <summary>Payload length of the committed event, so a recorded play names what produced it.</summary>
            public readonly int PayloadLength;

            /// <summary>Builds one record.</summary>
            public Played(Id128 cue, Id128 eventKey, int payloadLength)
            {
                Cue = cue;
                EventKey = eventKey;
                PayloadLength = payloadLength;
            }

            /// <inheritdoc />
            public override string ToString() =>
                Cue.ToString() + ":" + PayloadLength.ToString(CultureInfo.InvariantCulture);
        }
    }
}

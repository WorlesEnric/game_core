// GameCore.Unity.Runtime (engine-free part, compiled as GameCore.Execution) - delivery identity (GC-021).
//
// Normative sources: docs/game-core/00-core-protocols.md P-004 (stable 128-bit identities; a name is never an
// identity, and an all-zero identity is invalid), P-008 (comparisons use canonical big-endian stable-ID bytes and
// explicit numeric keys; registration timing and dictionary enumeration never decide precedence), P-045
// ("irreversible output adapters consume only committed events, use explicit external idempotency keys, and persist
// an outbox when delivery must survive crashes") and P-054 (wire/save data uses integer versions, canonical byte
// order and explicit field ids; no CLR assembly name or engine handle is an identity).
//
// One delivery obligation is identified by three stable 128-bit values, all derived from committed data rather than
// from creation order:
//
//   OutboxId        identifies the obligation itself: the unit a world enqueues, delivers, acknowledges, rejects or
//                   compensates. It is derived from the *committed event identity* the obligation came from, so a
//                   duplicate observation of one committed event derives the same obligation rather than a second
//                   one (07 s5's "a duplicate source receipt ... uses the same durable RewardId").
//   DestinationId   identifies the destination endpoint that owns the mutation (a declared domain in the receiving
//                   package). It is supplied by the caller, never invented here, because the kernel does not know
//                   what a destination is (P-001).
//   IdempotencyKey  is the explicit external idempotency key P-045 requires of an irreversible output adapter. It is
//                   derived from the obligation and its destination together, under its own domain label, so it is
//                   never equal to the obligation id and never collides across destinations.
//
// Nothing here is a runtime handle, an entity index, a lease or a payload: an obligation is a stable value a capture
// can write and a restore can rebuild (P-005). The recipient's own vocabulary (what a mutation means, what a
// compensation is) is not a kernel concept and is deliberately absent (P-003).
#nullable enable
using System;
using System.Globalization;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Execution.Delivery
{
    /// <summary>What an enqueue did, so a refusal is never confused with a successful commit (P-052).</summary>
    public enum OutboxAdmission
    {
        /// <summary>The obligation is committed and tracked.</summary>
        Accepted = 0,

        /// <summary>
        /// The same obligation identity was already committed. Nothing was added and nothing was lost: a duplicate
        /// observation of one committed event is one obligation (P-050's idempotency rule applied to delivery).
        /// </summary>
        Duplicate = 1,

        /// <summary>
        /// The outbox holds as many open obligations as its declared capacity allows. This is the explicit capacity
        /// exhaustion result: an obligation is refused loudly rather than dropped silently (P-043's "no silent drop",
        /// applied to an outbox instead of a buffer).
        /// </summary>
        AtCapacity = 2,

        /// <summary>The requested obligation is malformed (a default identity, a missing schema, no payload schema).</summary>
        Invalid = 3,

        /// <summary>
        /// The obligation requires durability, but the outbox was configured volatile. Refused rather than accepted
        /// and then lost with the session, because P-045 forbids presenting an in-memory obligation as a durable one.
        /// </summary>
        DurabilityUnavailable = 4,

        /// <summary>
        /// The adapter could not append the obligation to its journal, so the commit was never made durable and the
        /// outbox was not advanced past what is persisted (P-045, P-052).
        /// </summary>
        JournalRefused = 5,
    }

    /// <summary>Every state one delivery attempt can end in, as the caller observes it (P-045, P-052).</summary>
    public enum DeliveryOutcome
    {
        /// <summary>Nothing to report; used only as the default value of an out parameter.</summary>
        None = 0,

        /// <summary>The obligation was handed to the destination in this attempt and is now `Delivered`.</summary>
        Delivered = 1,

        /// <summary>
        /// The obligation already reached a terminal state, so nothing was handed over. A redelivery answered this
        /// way applies no destination mutation a second time (P-045).
        /// </summary>
        AlreadyTerminal = 2,

        /// <summary>The destination confirmed the mutation and the obligation is now `Acknowledged`.</summary>
        Acknowledged = 3,

        /// <summary>
        /// The destination refused the obligation terminally and the refusal is recorded. The row is retained so the
        /// refusal stays observable rather than becoming a silent drop (P-052).
        /// </summary>
        Rejected = 4,

        /// <summary>
        /// The destination could not complete the mutation and an explicit compensation was recorded instead. The
        /// kernel defines no universal compensation rule; the caller names the reason (GC-021's non-goal, P-003).
        /// </summary>
        Compensated = 5,

        /// <summary>
        /// The outbox cannot accept the obligation because it is at capacity. Reported instead of dropping it
        /// (P-043, P-052).
        /// </summary>
        CapacityExhausted = 6,

        /// <summary>The named obligation is not tracked by this outbox, so no attempt was made.</summary>
        NoObligation = 7,

        /// <summary>
        /// The obligation requires durability, but the adapter in use was configured volatile. A caller that needs a
        /// commit to survive a crash must not be told an in-memory obligation is one (P-045).
        /// </summary>
        VolatileNotDurable = 8,

        /// <summary>The attempt itself could not be recorded, so the obligation stays where it was (P-052).</summary>
        NotRecorded = 9,

        /// <summary>
        /// The attempt does not address the port it was handed to: another destination's identity, or a payload
        /// schema the destination does not accept. Nothing was handed over and nothing was reinterpreted (P-034,
        /// P-054).
        /// </summary>
        UnsupportedAttempt = 10,
    }

    /// <summary>
    /// The three stable identities of one delivery obligation. It is the destination command's idempotency boundary:
    /// an adapter that redelivers reuses the same key, so a destination that deduplicates by it applies the mutation
    /// once (P-045, P-050).
    /// </summary>
    public readonly struct DeliveryKey : IEquatable<DeliveryKey>
    {
        public DeliveryKey(Id128 outboxId, Id128 destinationId, Id128 idempotencyKey)
        {
            if (outboxId.IsDefault)
            {
                throw new ArgumentException(
                    "A delivery obligation identity is never the all-zero value (P-004).", nameof(outboxId));
            }

            if (destinationId.IsDefault)
            {
                throw new ArgumentException(
                    "A delivery destination identity is never the all-zero value (P-004).", nameof(destinationId));
            }

            if (idempotencyKey.IsDefault)
            {
                throw new ArgumentException(
                    "An external idempotency key is never the all-zero value (P-045).", nameof(idempotencyKey));
            }

            OutboxId = outboxId;
            DestinationId = destinationId;
            IdempotencyKey = idempotencyKey;
        }

        /// <summary>Stable identity of the obligation; the unit a world tracks and an outbox section carries.</summary>
        public readonly Id128 OutboxId;

        /// <summary>Stable identity of the destination endpoint that owns the mutation.</summary>
        public readonly Id128 DestinationId;

        /// <summary>The explicit external idempotency key the destination command carries (P-045).</summary>
        public readonly Id128 IdempotencyKey;
        /// <summary>
        /// Derives the three identities of one obligation from the committed event it came from and the destination
        /// it is addressed to. The derivation is a pure SHA-256 over canonical big-endian words under two domain
        /// labels, so the same committed event and destination always derive the same key, on any host, without a
        /// counter, a clock, a thread identity or an enumeration order (P-004, P-008).
        /// </summary>
        /// <param name="sourceWorld">Session the committed event was published by.</param>
        /// <param name="sourceEvent">The committed event's own sequence in that session (P-045).</param>
        /// <param name="destinationId">Stable destination identity; supplied by the caller, never invented here.</param>
        /// <param name="destinationSchema">Payload schema the destination command carries, so a schema change is a
        /// different obligation rather than a silent reinterpretation of bytes (P-006, P-054).</param>
        public static DeliveryKey Derive(
            WorldId sourceWorld,
            EventSequence sourceEvent,
            Id128 destinationId,
            SchemaRef destinationSchema)
        {
            if (sourceWorld.Session.IsDefault)
            {
                throw new ArgumentException(
                    "The committed event's session identity is never the all-zero value (P-004).",
                    nameof(sourceWorld));
            }

            if (sourceEvent.Value == 0UL)
            {
                throw new ArgumentException(
                    "A committed event sequence starts at one; zero names no committed event (P-045).",
                    nameof(sourceEvent));
            }

            if (destinationId.IsDefault)
            {
                throw new ArgumentException(
                    "A delivery destination identity is never the all-zero value (P-004).", nameof(destinationId));
            }

            if (destinationSchema.Id.Value.IsDefault)
            {
                throw new ArgumentException(
                    "A destination command declares a payload schema; an all-zero schema id is not one (P-004).",
                    nameof(destinationSchema));
            }

            Id128 outboxId = Derive128(
                ObligationLabel,
                sourceWorld.Session.High,
                sourceWorld.Session.Low,
                sourceEvent.Value,
                destinationId.High,
                destinationId.Low,
                destinationSchema.Id.Value.High,
                destinationSchema.Id.Value.Low,
                destinationSchema.Version);

            Id128 idempotencyKey = Derive128(
                IdempotencyLabel,
                outboxId.High,
                outboxId.Low,
                destinationId.High,
                destinationId.Low,
                destinationSchema.Id.Value.High,
                destinationSchema.Id.Value.Low,
                destinationSchema.Version);

            return new DeliveryKey(outboxId, destinationId, idempotencyKey);
        }

        /// <summary>The domain label under which an obligation identity is derived (P-004: names are not identities).</summary>
        public const string ObligationLabel = "gamecore.delivery.obligation.v1";

        /// <summary>The domain label under which an external idempotency key is derived (P-045).</summary>
        public const string IdempotencyLabel = "gamecore.delivery.idempotency.v1";

        public bool Equals(DeliveryKey other) =>
            OutboxId.Equals(other.OutboxId)
            && DestinationId.Equals(other.DestinationId)
            && IdempotencyKey.Equals(other.IdempotencyKey);

        public override bool Equals(object? obj) => obj is DeliveryKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + OutboxId.GetHashCode();
                hash = (hash * 31) + DestinationId.GetHashCode();
                hash = (hash * 31) + IdempotencyKey.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(DeliveryKey left, DeliveryKey right) => left.Equals(right);

        public static bool operator !=(DeliveryKey left, DeliveryKey right) => !left.Equals(right);

        public override string ToString() =>
            "deliveryKey(" + OutboxId.ToString() + "," + DestinationId.ToString() + ")";

        /// <summary>
        /// One SHA-256 under a domain label over big-endian 64-bit words; the first 16 digest bytes are the identity.
        /// The label is length-prefixed so no two labels can collide by concatenation (P-004, P-054).
        /// </summary>
        private static Id128 Derive128(string label, params ulong[] words)
        {
            byte[] labelBytes = Encoding.UTF8.GetBytes(label);
            var material = new byte[4 + labelBytes.Length + (words.Length * 8)];
            WriteBigEndian((ulong)labelBytes.Length, material, 0);
            Array.Copy(labelBytes, 0, material, 4, labelBytes.Length);
            int offset = 4 + labelBytes.Length;
            for (int i = 0; i < words.Length; i++)
            {
                WriteBigEndian(words[i], material, offset);
                offset += 8;
            }

            ContentHash digest = ContentHash.Compute(material);
            byte[] bytes = digest.ToArray();
            return new Id128(
                Id128Codec.ReadUInt64BigEndian(bytes, 0),
                Id128Codec.ReadUInt64BigEndian(bytes, 8));
        }

        private static void WriteBigEndian(ulong value, byte[] destination, int offset)
        {
            destination[offset] = (byte)(value >> 56);
            destination[offset + 1] = (byte)(value >> 48);
            destination[offset + 2] = (byte)(value >> 40);
            destination[offset + 3] = (byte)(value >> 32);
            destination[offset + 4] = (byte)(value >> 24);
            destination[offset + 5] = (byte)(value >> 16);
            destination[offset + 6] = (byte)(value >> 8);
            destination[offset + 7] = (byte)value;
        }
    }

    /// <summary>
    /// One tracked delivery obligation: its stable identities, the destination command it carries, and how far it has
    /// progressed. Mutable because a delivery advances the state; every field is either a stable value or a frozen
    /// copy, so an obligation can be projected into a checkpoint row at any committed boundary (P-005, P-053).
    ///
    /// `Order` is the obligation's canonical position inside its outbox (P-008): it is assigned at commit from a
    /// monotonic per-outbox counter, never from a dictionary order or a thread, so the checkpoint rows of one outbox
    /// are stable across captures and restores (TEST-022).
    /// </summary>
    public sealed class DeliveryObligation
    {
        private byte[] payload;

        internal DeliveryObligation(
            DeliveryKey key,
            SchemaRef payloadSchema,
            byte[]? payload,
            EventSequence sourceEvent,
            LogicalStepId step,
            AssemblyEpoch epoch,
            OperationId causal,
            uint order,
            uint terminalRetention)
        {
            Key = key;
            PayloadSchema = payloadSchema;
            this.payload = payload == null ? Array.Empty<byte>() : (byte[])payload.Clone();
            SourceEvent = sourceEvent;
            Step = step;
            Epoch = epoch;
            Causal = causal;
            Order = order;
            State = OutboxDeliveryState.Pending;
            Attempts = 0U;
            Retention = terminalRetention;
        }

        public DeliveryKey Key { get; }

        public SchemaRef PayloadSchema { get; }

        public EventSequence SourceEvent { get; }

        public LogicalStepId Step { get; }

        public AssemblyEpoch Epoch { get; }

        /// <summary>The step's causal request, so a redelivery reuses the operation identity P-050 requires.</summary>
        public OperationId Causal { get; }

        /// <summary>Canonical position of this obligation inside its outbox (P-008).</summary>
        public uint Order { get; }

        /// <summary>How many terminal rows this obligation's destination may retain (P-045's bounded retention).</summary>
        public uint Retention { get; internal set; }

        public OutboxDeliveryState State { get; internal set; }

        public uint Attempts { get; internal set; }

        /// <summary>The diagnostic reason recorded with a terminal refusal or compensation (P-052).</summary>
        public DiagnosticCode Reason { get; internal set; }

        /// <summary>
        /// True when this obligation still owes the destination something: it has not been handed over yet
        /// (`Pending`) or it was handed over and the destination's outcome is not known (`Delivered`). Only
        /// `Acknowledged`, `Rejected` and `Compensated` are terminal, so a delivery whose acknowledgement was lost
        /// stays redeliverable — which is what P-045's at-least-once contract requires.
        /// </summary>
        public bool IsOpen =>
            State == OutboxDeliveryState.Pending || State == OutboxDeliveryState.Delivered;

        public bool IsTerminal => !IsOpen;

        /// <summary>A copy of the destination command bytes; callers never receive the stored array (P-054).</summary>
        public byte[] PayloadBytes()
        {
            var copy = new byte[payload.Length];
            Array.Copy(payload, copy, payload.Length);
            return copy;
        }

        public override string ToString() =>
            "obligation(" + Key.OutboxId.ToString() + "," + State.ToString() + ",attempts="
            + Attempts.ToString(CultureInfo.InvariantCulture) + ")";
    }
}

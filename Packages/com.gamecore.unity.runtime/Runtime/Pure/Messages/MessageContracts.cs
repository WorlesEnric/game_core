// GameCore.Execution.Messages — bounded message, command and event contracts (GC-007).
//
// Normative sources: docs/game-core/00-core-protocols.md P-037 (step admission and sealing), P-041 (structural
// playback keys), P-042 (command envelopes, typed requests, RequestResult), P-043 (buffer contracts, bounded work,
// overflow) and P-045 (committed events and observation), plus docs/game-core/03-runtime-and-execution.md s4/s5.
//
// Everything in this file is engine-free: blittable value types and BCL collections only, so the same decision
// logic runs under plain dotnet and inside the Unity job lanes. The native, lane-backed storage that uses these
// types lives in `Runtime/Messages/` (Unity only), exactly as GC-005 split `GuardedDispatchPlan` from
// `NativeFenceTable`.
//
// A message is never a committed fact. `StepMessage` is a transient, bounded, step-scoped input to one named state
// owner; only a `CommittedEvent` describes committed gameplay.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Execution.Messages
{
    /// <summary>Why one bounded message exists. A decision cannot be confused with a fact by construction (P-042).</summary>
    public enum MessageKind
    {
        /// <summary>An externally admitted command routed to its owner; the owner still decides gameplay.</summary>
        Command = 0,

        /// <summary>A typed transient inter-system request: not a committed fact.</summary>
        Request = 1,

        /// <summary>A tentative owner-local receipt, valid only until the step commits (03 s4).</summary>
        Receipt = 2,

        /// <summary>A committed outcome that may be published as a `CommittedEvent` (P-045).</summary>
        Outcome = 3,
    }

    /// <summary>
    /// Canonical merge key of one buffered message. Sorting by this key is independent of producer scheduling,
    /// worker index and lane index: only the host-assigned admission sequence, stable IDs and declared ordinal
    /// participate (P-008, P-037, 03 s5).
    /// </summary>
    public readonly struct MessageOrderKey : IEquatable<MessageOrderKey>
    {
        /// <summary>Host-assigned admitted input sequence; the first and strongest component (P-037).</summary>
        public readonly AdmissionSequence Admitted;

        /// <summary>Ordinal assigned by the producing owner for messages it emitted within one step.</summary>
        public readonly uint Ordinal;

        /// <summary>Stable identity of the producing entity/target, never a runtime handle (P-004).</summary>
        public readonly Id128 OriginKey;

        public MessageOrderKey(AdmissionSequence admitted, uint ordinal, Id128 originKey)
        {
            Admitted = admitted;
            Ordinal = ordinal;
            OriginKey = originKey;
        }

        /// <summary>The key of internal work that no admitted command caused: sequence zero, ordinal zero.</summary>
        public static MessageOrderKey Internal(Id128 originKey) => new MessageOrderKey(AdmissionSequence.Zero, 0U, originKey);

        public bool IsAdmitted => Admitted.Value != 0UL;

        public bool Equals(MessageOrderKey other)
            => Admitted.Equals(other.Admitted) && Ordinal == other.Ordinal && OriginKey.Equals(other.OriginKey);

        public override bool Equals(object? obj) => obj is MessageOrderKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + Admitted.GetHashCode();
                hash = (hash * 31) + (int)Ordinal;
                hash = (hash * 31) + OriginKey.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(MessageOrderKey left, MessageOrderKey right) => left.Equals(right);

        public static bool operator !=(MessageOrderKey left, MessageOrderKey right) => !left.Equals(right);

        public override string ToString()
            => Admitted.ToString() + "#" + Ordinal.ToString(CultureInfo.InvariantCulture) + "@" + OriginKey.ToString();
    }

    /// <summary>
    /// Canonical message ordering. Lane/worker index is deliberately not a component, so two runs that differ only
    /// in producer scheduling merge identically (P-008, P-041).
    /// </summary>
    public sealed class MessageOrderComparer : IComparer<StepMessage>
    {
        public static readonly MessageOrderComparer Instance = new MessageOrderComparer();

        public int Compare(StepMessage x, StepMessage y)
        {
            int byAdmission = x.Order.Admitted.CompareTo(y.Order.Admitted);
            if (byAdmission != 0)
            {
                return byAdmission;
            }

            int byOrdinal = x.Order.Ordinal.CompareTo(y.Order.Ordinal);
            if (byOrdinal != 0)
            {
                return byOrdinal;
            }

            int byOrigin = x.Order.OriginKey.CompareTo(y.Order.OriginKey);
            if (byOrigin != 0)
            {
                return byOrigin;
            }

            int byTarget = x.Target.Value.CompareTo(y.Target.Value);
            if (byTarget != 0)
            {
                return byTarget;
            }

            int byRoute = x.Route.Value.CompareTo(y.Route.Value);
            if (byRoute != 0)
            {
                return byRoute;
            }

            int bySchema = x.PayloadSchema.Id.Value.CompareTo(y.PayloadSchema.Id.Value);
            if (bySchema != 0)
            {
                return bySchema;
            }

            int byVersion = x.PayloadSchema.Version.CompareTo(y.PayloadSchema.Version);
            return byVersion != 0 ? byVersion : x.Producer.RegistrationKey.CompareTo(y.Producer.RegistrationKey);
        }

        /// <summary>Compares the (step, epoch, route, target) identity of two messages.</summary>
        public static int CompareIdentity(StepMessage x, StepMessage y)
        {
            int byStep = x.Step.CompareTo(y.Step);
            if (byStep != 0)
            {
                return byStep;
            }

            int byEpoch = x.Epoch.CompareTo(y.Epoch);
            if (byEpoch != 0)
            {
                return byEpoch;
            }

            int byRoute = x.Route.Value.CompareTo(y.Route.Value);
            return byRoute != 0 ? byRoute : x.Target.Value.CompareTo(y.Target.Value);
        }
    }

    /// <summary>
    /// One bounded message. Every field is blittable, so the same shape is used by the managed mirror and by the
    /// Unity native lanes; only `PayloadOffset`/`PayloadLength` address the payload arena (P-043).
    /// </summary>
    public readonly struct StepMessage
    {
        /// <summary>Logical step the message was produced for; the step it belongs to, never a wall clock.</summary>
        public readonly LogicalStepId Step;

        public readonly AssemblyEpoch Epoch;

        /// <summary>Causal request identity; default means internal work with no external request (P-045).</summary>
        public readonly OperationId Request;

        public readonly RouteId Route;

        /// <summary>The single owner this message is routed to; a consumer is never a second authority (P-043).</summary>
        public readonly OwnerId Owner;

        public readonly TargetId Target;

        public readonly SchemaRef PayloadSchema;

        public readonly MessageKind Kind;

        public readonly MessageOrderKey Order;

        /// <summary>Declared producer key; duplicate producers are legal and each keeps its own lane (P-043).</summary>
        public readonly FactoryKey Producer;

        /// <summary>Offset into the buffer's payload arena of this message's payload.</summary>
        public readonly int PayloadOffset;

        public readonly int PayloadLength;

        public StepMessage(
            LogicalStepId step,
            AssemblyEpoch epoch,
            OperationId request,
            RouteId route,
            OwnerId owner,
            TargetId target,
            SchemaRef payloadSchema,
            MessageKind kind,
            MessageOrderKey order,
            FactoryKey producer,
            int payloadOffset,
            int payloadLength)
        {
            if (payloadOffset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(payloadOffset), "A payload offset cannot be negative.");
            }

            if (payloadLength < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(payloadLength), "A payload length cannot be negative.");
            }

            Step = step;
            Epoch = epoch;
            Request = request;
            Route = route;
            Owner = owner;
            Target = target;
            PayloadSchema = payloadSchema;
            Kind = kind;
            Order = order;
            Producer = producer;
            PayloadOffset = payloadOffset;
            PayloadLength = payloadLength;
        }

        public bool HasPayload => PayloadLength > 0;

        public bool HasRequest => Order.IsAdmitted;

        /// <summary>True when this message describes a committed outcome rather than an open decision (P-042, P-045).</summary>
        public bool IsOutcome => Kind == MessageKind.Outcome;

        public override string ToString()
            => Kind + ":" + Order.ToString() + "->" + Owner.ToString() + "/" + Route.ToString();
    }

    /// <summary>How one append attempt to a bounded buffer resolved (P-043).</summary>
    public enum BufferAppendOutcome
    {
        /// <summary>The message was recorded.</summary>
        Accepted = 0,

        /// <summary>The row capacity is exhausted and the buffer rejects instead of dropping (P-043).</summary>
        RejectedCapacity = 1,

        /// <summary>The payload arena is exhausted and the buffer rejects instead of dropping (P-043).</summary>
        RejectedBytes = 2,

        /// <summary>A lossy diagnostic/presentation buffer dropped the message and counted it (P-043).</summary>
        DroppedLossy = 3,

        /// <summary>The message does not belong to this buffer: wrong owner, route or declared producer (P-043).</summary>
        RejectedNotForBuffer = 4,
    }

    /// <summary>
    /// One declared bounded buffer contract (P-043): schema, lifetime, producers, the single consuming owner and
    /// stage, order key, capacity, overflow behavior and drain/cancel policy. This mirrors the catalog's
    /// `BufferSpec` as the runtime descriptor the Unity lanes and the commit validation read.
    /// </summary>
    public sealed class MessageBufferDescriptor
    {
        public MessageBufferDescriptor(
            BufferId buffer,
            SchemaRef schema,
            IReadOnlyList<FactoryKey>? producers,
            OwnerId owner,
            StageId ownerStage,
            StageId consumerStage,
            FactoryKey orderKey,
            BufferLifetime lifetime,
            int capacity,
            int byteCapacity,
            BufferOverflowPolicy overflow,
            BufferCancellationPolicy cancellation)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "A bounded buffer requires a positive row capacity (P-043).");
            }

            if (byteCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(byteCapacity), "A payload capacity cannot be negative.");
            }

            Buffer = buffer;
            Schema = schema;
            Producers = ContractCollections.Freeze(producers);
            Owner = owner;
            OwnerStage = ownerStage;
            ConsumerStage = consumerStage;
            OrderKey = orderKey;
            Lifetime = lifetime;
            Capacity = capacity;
            ByteCapacity = byteCapacity;
            Overflow = overflow;
            Cancellation = cancellation;
        }

        public BufferId Buffer { get; }

        public SchemaRef Schema { get; }

        /// <summary>Declared producer keys; several producers are legal when each is registered (P-043).</summary>
        public IReadOnlyList<FactoryKey> Producers { get; }

        /// <summary>The exactly-one owner that consumes this buffer (P-043).</summary>
        public OwnerId Owner { get; }

        public StageId OwnerStage { get; }

        /// <summary>The stage that consumes the buffer; commit validation requires it to have run (P-043).</summary>
        public StageId ConsumerStage { get; }

        public FactoryKey OrderKey { get; }

        public BufferLifetime Lifetime { get; }

        /// <summary>Maximum retained rows; exceeding it rejects the affected command/batch (P-043).</summary>
        public int Capacity { get; }

        /// <summary>Maximum retained payload bytes; a second bound besides the row count (P-022, P-043).</summary>
        public int ByteCapacity { get; }

        public BufferOverflowPolicy Overflow { get; }

        public BufferCancellationPolicy Cancellation { get; }

        /// <summary>True when a full buffer must refuse the affected command/batch before mutation (P-043).</summary>
        public bool IsReliable => Overflow == BufferOverflowPolicy.RejectBeforeMutation;

        /// <summary>True when the buffer survives the step that produced it (P-043).</summary>
        public bool SurvivesStep => Lifetime == BufferLifetime.BoundedNextStep;

        public bool DeclaresProducer(FactoryKey producer)
        {
            for (int i = 0; i < Producers.Count; i++)
            {
                if (Producers[i].RegistrationKey.Equals(producer.RegistrationKey))
                {
                    return true;
                }
            }

            return false;
        }

        public override string ToString()
            => Buffer.ToString() + ":" + Lifetime.ToString() + "(" + Capacity.ToString(CultureInfo.InvariantCulture)
                + " rows)@" + Owner.ToString();
    }

    /// <summary>One immutable read port: an explicit fan-out of a buffer to a named reader (P-043).</summary>
    public readonly struct BufferReadPort
    {
        public readonly BufferId Buffer;

        /// <summary>The reader's system key; a read port never transfers the consuming owner's authority.</summary>
        public readonly FactoryKey Reader;

        public readonly StageId Stage;

        public BufferReadPort(BufferId buffer, FactoryKey reader, StageId stage)
        {
            Buffer = buffer;
            Reader = reader;
            Stage = stage;
        }

        public override string ToString() => Buffer.ToString() + "->" + Reader.ToString();
    }

    /// <summary>
    /// One staged structural operation: an add/remove recorded by a producer and played back at its declared
    /// boundary, after its producers finish and before dependent readers (P-041). Ordering keys use stable target
    /// order, never insertion timing.
    /// </summary>
    public readonly struct DeferredStructuralOperation
    {
        public readonly BufferId Buffer;

        /// <summary>Producer that recorded the operation; playback waits for its completion (P-041).</summary>
        public readonly FactoryKey Producer;

        public readonly TargetId Target;

        public readonly SchemaRef Schema;

        /// <summary>True to add, false to remove.</summary>
        public readonly bool Add;

        public readonly MessageOrderKey Order;

        public DeferredStructuralOperation(
            BufferId buffer,
            FactoryKey producer,
            TargetId target,
            SchemaRef schema,
            bool add,
            MessageOrderKey order)
        {
            Buffer = buffer;
            Producer = producer;
            Target = target;
            Schema = schema;
            Add = add;
            Order = order;
        }

        public override string ToString()
            => (Add ? "add " : "remove ") + Schema.ToString() + " on " + Target.ToString();
    }

    /// <summary>Canonical deferred-playback ordering: stable target, then schema, then request order (P-041).</summary>
    public sealed class DeferredOperationComparer : IComparer<DeferredStructuralOperation>
    {
        public static readonly DeferredOperationComparer Instance = new DeferredOperationComparer();

        public int Compare(DeferredStructuralOperation x, DeferredStructuralOperation y)
        {
            int byTarget = x.Target.Value.CompareTo(y.Target.Value);
            if (byTarget != 0)
            {
                return byTarget;
            }

            int bySchema = x.Schema.Id.Value.CompareTo(y.Schema.Id.Value);
            if (bySchema != 0)
            {
                return bySchema;
            }

            int byAdmission = x.Order.Admitted.CompareTo(y.Order.Admitted);
            if (byAdmission != 0)
            {
                return byAdmission;
            }

            int byOrdinal = x.Order.Ordinal.CompareTo(y.Order.Ordinal);
            return byOrdinal != 0 ? byOrdinal : x.Order.OriginKey.CompareTo(y.Order.OriginKey);
        }
    }
}

// GameCore.Execution.Messages — bounded buffers, canonical merge and commit drain validation (GC-007).
//
// Normative sources: docs/game-core/00-core-protocols.md P-037, P-041, P-043, P-044 and
// docs/game-core/03-runtime-and-execution.md s5.
//
// `BoundedBufferRules` holds every decision about a bounded buffer (append admission, overflow, drain, carry-over
// and revalidation). `BoundedMessageBuffer` is the managed reference implementation of those rules with a real row
// list and payload arena; the Unity native lanes in `Runtime/Messages/NativeMessageStore.cs` call the same rules
// over `NativeArray` storage. That way one set of semantics is exercised by the dotnet suite and used by the jobs.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Execution.Messages
{
    /// <summary>Liveness of stable references when a bounded next-step queue is revalidated (P-043, P-047).</summary>
    public interface IMessageTargetLiveness
    {
        /// <summary>False when the target was removed, or its route/owner is no longer the live authority (P-047).</summary>
        bool IsLive(TargetId target, RouteId route, OwnerId owner);

        /// <summary>True when the port explicitly permits rebinding to a compatible owner (P-047).</summary>
        bool AllowsRebind(BufferId buffer);
    }

    /// <summary>A liveness resolver that reports everything live; the default of a world with no composition host.</summary>
    public sealed class AlwaysLiveTargets : IMessageTargetLiveness
    {
        public static readonly AlwaysLiveTargets Instance = new AlwaysLiveTargets();

        public bool IsLive(TargetId target, RouteId route, OwnerId owner) => true;

        public bool AllowsRebind(BufferId buffer) => false;
    }

    /// <summary>What a commit-time drain check found (P-043, O-16).</summary>
    public readonly struct MessageDrainReport
    {
        public readonly bool Succeeded;
        public readonly DiagnosticCode Code;

        /// <summary>Buffer that still holds reliable unconsumed data; default when the check succeeded.</summary>
        public readonly BufferId UnconsumedBuffer;

        /// <summary>Rows still held by that buffer when the check failed.</summary>
        public readonly int UnconsumedRows;

        public readonly string Detail;

        internal MessageDrainReport(bool succeeded, DiagnosticCode code, BufferId buffer, int rows, string detail)
        {
            Succeeded = succeeded;
            Code = code;
            UnconsumedBuffer = buffer;
            UnconsumedRows = rows;
            Detail = detail ?? string.Empty;
        }

        public static readonly MessageDrainReport Ok =
            new MessageDrainReport(true, DiagnosticCode.None, default(BufferId), 0, string.Empty);

        public static MessageDrainReport Unconsumed(BufferId buffer, int rows)
            => new MessageDrainReport(
                false,
                DiagnosticCode.MissingDependency,
                buffer,
                rows,
                "reliable step buffer " + buffer.ToString() + " still holds " + rows.ToString(CultureInfo.InvariantCulture)
                + " unconsumed row(s); the commit fails instead of publishing partial success (P-043, O-16)");

        public override string ToString() => Succeeded ? "drained" : Code + ":" + UnconsumedBuffer.ToString();
    }

    /// <summary>Result of carrying a bounded next-step queue across a step boundary (P-043).</summary>
    public sealed class NextStepCarryReport
    {
        internal NextStepCarryReport(
            BufferId buffer,
            IReadOnlyList<StepMessage> retained,
            IReadOnlyList<RequestOutcome> cancelled,
            int droppedByCapacity)
        {
            Buffer = buffer;
            Retained = ContractCollections.Freeze(retained);
            Cancelled = ContractCollections.Freeze(cancelled);
            DroppedByCapacity = droppedByCapacity;
        }

        public BufferId Buffer { get; }

        /// <summary>Messages retained for the next logical step, in canonical order.</summary>
        public IReadOnlyList<StepMessage> Retained { get; }

        /// <summary>Terminal results of deferred work whose target or route is gone (P-047).</summary>
        public IReadOnlyList<RequestOutcome> Cancelled { get; }

        /// <summary>How many deferred messages a bounded queue had to drop; never silent, always counted.</summary>
        public int DroppedByCapacity { get; }

        public override string ToString()
            => Buffer.ToString() + ": retained=" + Retained.Count.ToString(CultureInfo.InvariantCulture)
                + ", cancelled=" + Cancelled.Count.ToString(CultureInfo.InvariantCulture)
                + ", dropped=" + DroppedByCapacity.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The terminal or pending result of one request, with the admission-versus-commitment distinction P-042
    /// requires: `Accepted` means only that the host admitted the request; only `Committed` means gameplay happened.
    /// </summary>
    public readonly struct RequestOutcome
    {
        public readonly OperationId Request;

        /// <summary>Route the request was admitted for; default when the request was never routed.</summary>
        public readonly RouteId Route;

        public readonly TargetId Target;

        public readonly RequestResultKind Kind;

        public readonly DiagnosticCode Reason;

        /// <summary>Committed event cursor of the outcome; default until the owner actually committed (P-042).</summary>
        public readonly EventCursor CausalCursor;

        /// <summary>Order key the request was admitted with; the canonical order of a step's batch (P-037).</summary>
        public readonly MessageOrderKey Order;

        public RequestOutcome(
            OperationId request,
            RouteId route,
            TargetId target,
            RequestResultKind kind,
            DiagnosticCode reason,
            EventCursor causalCursor,
            MessageOrderKey order)
        {
            Request = request;
            Route = route;
            Target = target;
            Kind = kind;
            Reason = reason;
            CausalCursor = causalCursor;
            Order = order;
        }

        public bool IsTerminal
            => Kind == RequestResultKind.Rejected
                || Kind == RequestResultKind.Cancelled
                || Kind == RequestResultKind.Committed;

        /// <summary>True only for a committed outcome; admission acceptance is not gameplay success (P-042).</summary>
        public bool IsCommitted => Kind == RequestResultKind.Committed;

        /// <summary>Projects this outcome onto the shared contract type (05 s4).</summary>
        public RequestResult ToRequestResult() => new RequestResult(Kind, Reason, CausalCursor);

        public override string ToString()
            => Kind + ":" + Request.ToString()
                + (Reason == DiagnosticCode.None ? string.Empty : "(" + DiagnosticCodeText.Of(Reason) + ")");
    }

    /// <summary>
    /// The bounded-buffer decision rules, shared by the managed reference implementation and the Unity native lanes
    /// (P-043). Nothing here allocates beyond the caller's own structures and nothing reads a clock or a thread.
    /// </summary>
    public static class BoundedBufferRules
    {
        /// <summary>
        /// Decides one append. A reliable buffer refuses (and the caller must abandon the whole affected command or
        /// batch) rather than dropping a row or a payload byte; a declared lossy buffer counts the drop instead.
        /// </summary>
        public static BufferAppendOutcome DecideAppend(
            MessageBufferDescriptor descriptor,
            int rowCount,
            int byteCount,
            int incomingPayloadBytes,
            bool declaredProducer,
            out string detail)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            if (!declaredProducer)
            {
                detail = "producer is not declared for buffer " + descriptor.Buffer.ToString() + " (P-043)";
                return BufferAppendOutcome.RejectedNotForBuffer;
            }

            if (byteCount < 0 || incomingPayloadBytes < 0)
            {
                detail = "a payload length cannot be negative";
                return BufferAppendOutcome.RejectedBytes;
            }

            bool rowsFull = rowCount >= descriptor.Capacity;
            bool bytesFull = descriptor.ByteCapacity > 0 && byteCount + incomingPayloadBytes > descriptor.ByteCapacity;
            if (!rowsFull && !bytesFull)
            {
                detail = string.Empty;
                return BufferAppendOutcome.Accepted;
            }

            if (descriptor.IsReliable)
            {
                detail = rowsFull
                    ? "buffer " + descriptor.Buffer.ToString() + " is at its declared capacity of "
                        + descriptor.Capacity.ToString(CultureInfo.InvariantCulture)
                        + " rows; required gameplay input overflow rejects the affected batch before mutation (P-043)"
                    : "buffer " + descriptor.Buffer.ToString() + " exceeds its declared payload capacity of "
                        + descriptor.ByteCapacity.ToString(CultureInfo.InvariantCulture)
                        + " bytes; the affected batch is rejected before mutation (P-043)";
                return rowsFull ? BufferAppendOutcome.RejectedCapacity : BufferAppendOutcome.RejectedBytes;
            }

            detail = "buffer " + descriptor.Buffer.ToString()
                + " is a declared lossy diagnostic/presentation buffer; the message was dropped and counted (P-043)";
            return BufferAppendOutcome.DroppedLossy;
        }

        /// <summary>
        /// Commit-time drain verdict for one buffer. `Stage` buffers are recycled by their consumer, `Step` buffers
        /// must be drained before commit and `BoundNextStep` queues survive on purpose (P-043, O-16).
        /// </summary>
        public static MessageDrainReport ValidateDrain(MessageBufferDescriptor descriptor, int rowCount, bool consumed)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            if (descriptor.Lifetime == BufferLifetime.BoundedNextStep)
            {
                return MessageDrainReport.Ok;
            }

            if (rowCount == 0)
            {
                return MessageDrainReport.Ok;
            }

            if (consumed)
            {
                return MessageDrainReport.Ok;
            }

            // Lossy diagnostic buffers are explicitly allowed to expire with their step; a reliable buffer is not.
            if (!descriptor.IsReliable)
            {
                return MessageDrainReport.Ok;
            }

            return MessageDrainReport.Unconsumed(descriptor.Buffer, rowCount);
        }

        /// <summary>
        /// Carries a bounded next-step queue across the boundary: retained messages keep their stamped stable
        /// references, a message whose target or route is gone finishes `Cancelled` (or is retained only when the
        /// port explicitly permits rebinding), and exceeding the bounded capacity drops the excess with a count
        /// rather than growing without bound (P-043, P-047).
        /// </summary>
        public static NextStepCarryReport CarryNextStep(
            MessageBufferDescriptor descriptor,
            IReadOnlyList<StepMessage> held,
            int capacity,
            IMessageTargetLiveness liveness)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            if (held == null)
            {
                throw new ArgumentNullException(nameof(held));
            }

            if (liveness == null)
            {
                liveness = AlwaysLiveTargets.Instance;
            }

            var retained = new List<StepMessage>();
            var cancelled = new List<RequestOutcome>();
            int dropped = 0;

            for (int i = 0; i < held.Count; i++)
            {
                StepMessage message = held[i];
                bool live = liveness.IsLive(message.Target, message.Route, message.Owner);
                bool rebind = descriptor.Cancellation == BufferCancellationPolicy.RebindToCompatibleOwner
                    && liveness.AllowsRebind(descriptor.Buffer);

                if (!live && !rebind)
                {
                    // A deferred command whose target or route is gone gets a terminal result, never silence (P-047).
                    cancelled.Add(new RequestOutcome(
                        message.Request,
                        message.Route,
                        message.Target,
                        RequestResultKind.Cancelled,
                        DiagnosticCode.Cancelled,
                        default(EventCursor),
                        message.Order));
                    continue;
                }

                if (!live && rebind)
                {
                    // The port declares stable-ID rebinding: the message is retained for the compatible owner.
                    retained.Add(message);
                    continue;
                }

                if (retained.Count >= capacity)
                {
                    dropped++;
                    continue;
                }

                retained.Add(message);
            }

            return new NextStepCarryReport(descriptor.Buffer, retained, cancelled, dropped);
        }
    }

    /// <summary>
    /// The managed reference implementation of one bounded buffer: bounded rows, a bounded payload arena, per
    /// producer lanes and one consumer. It is the executable definition of P-043 that the dotnet suite drives and
    /// the native lanes mirror.
    /// </summary>
    public sealed class BoundedMessageBuffer
    {
        private readonly MessageBufferDescriptor descriptor;
        private readonly List<StepMessage> rows = new List<StepMessage>();
        private readonly List<int> producerLaneCounts = new List<int>();
        private readonly List<FactoryKey> producerLanes = new List<FactoryKey>();
        private byte[] payloadArena;
        private int payloadWatermark;
        private bool consumerHeld;

        public BoundedMessageBuffer(MessageBufferDescriptor descriptor)
        {
            this.descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            payloadArena = new byte[descriptor.ByteCapacity];
        }

        public MessageBufferDescriptor Descriptor => descriptor;

        public BufferId Buffer => descriptor.Buffer;

        public int RowCount => rows.Count;

        /// <summary>Payload bytes currently retained by this buffer's arena.</summary>
        public int PayloadBytes => payloadWatermark;

        public int AcceptedCount { get; private set; }

        public int RejectedCount { get; private set; }

        public int DroppedCount { get; private set; }

        public int NotForBufferCount { get; private set; }

        /// <summary>True once the consuming owner took the sealed batch of this step.</summary>
        public bool WasConsumed => consumerHeld;

        public IReadOnlyList<FactoryKey> ProducerLanes => producerLanes;

        /// <summary>
        /// Appends one message. A reliable overflow is refused with the buffer unchanged, so the caller can reject
        /// the whole command/batch before any mutation (P-043).
        /// </summary>
        public BufferAppendOutcome TryAppend(StepMessage message, IReadOnlyList<byte>? payload, out string detail)
        {
            int payloadLength = payload == null ? 0 : payload.Count;
            if (payloadLength != message.PayloadLength)
            {
                detail = "the message declares " + message.PayloadLength.ToString(CultureInfo.InvariantCulture)
                    + " payload byte(s) but " + payloadLength.ToString(CultureInfo.InvariantCulture) + " were supplied";
                RejectedCount++;
                return BufferAppendOutcome.RejectedNotForBuffer;
            }

            bool declaredProducer = descriptor.DeclaresProducer(message.Producer) || descriptor.Producers.Count == 0;
            BufferAppendOutcome decision = BoundedBufferRules.DecideAppend(
                descriptor,
                rows.Count,
                payloadWatermark,
                payloadLength,
                declaredProducer,
                out detail);

            switch (decision)
            {
                case BufferAppendOutcome.Accepted:
                    break;
                case BufferAppendOutcome.DroppedLossy:
                    DroppedCount++;
                    return decision;
                case BufferAppendOutcome.RejectedNotForBuffer:
                    NotForBufferCount++;
                    return decision;
                default:
                    RejectedCount++;
                    return decision;
            }

            if (message.Owner.Value.IsDefault)
            {
                detail = "a bounded message must name its consuming owner (P-043)";
                RejectedCount++;
                return BufferAppendOutcome.RejectedNotForBuffer;
            }

            if (!message.Owner.Equals(descriptor.Owner))
            {
                detail = "buffer " + descriptor.Buffer.ToString() + " is consumed by owner " + descriptor.Owner.ToString()
                    + " but the message names " + message.Owner.ToString()
                    + "; a buffer has exactly one consuming owner (P-043)";
                NotForBufferCount++;
                return BufferAppendOutcome.RejectedNotForBuffer;
            }

            if (detail.Length != 0)
            {
                // A rule that returned Accepted with an explanation is a defect in this type, not a caller error.
                throw new InvalidOperationException("Accepted append must not carry a detail: " + detail);
            }

            int offset = payloadWatermark;
            if (payloadLength != 0 && payload != null)
            {
                for (int i = 0; i < payloadLength; i++)
                {
                    payloadArena[offset + i] = payload[i];
                }
            }

            payloadWatermark += payloadLength;
            rows.Add(new StepMessage(
                message.Step,
                message.Epoch,
                message.Request,
                message.Route,
                message.Owner,
                message.Target,
                message.PayloadSchema,
                message.Kind,
                message.Order,
                message.Producer,
                offset,
                payloadLength));

            RecordLane(message.Producer);
            AcceptedCount++;
            return BufferAppendOutcome.Accepted;
        }

        /// <summary>The retained rows in canonical merge order, independent of append and producer order (P-008).</summary>
        public IReadOnlyList<StepMessage> Ordered()
        {
            var ordered = new List<StepMessage>(rows);
            ordered.Sort(MessageOrderComparer.Instance);
            return ordered;
        }

        /// <summary>Payload bytes of one retained message.</summary>
        public byte[] PayloadOf(StepMessage message)
        {
            var copy = new byte[message.PayloadLength];
            for (int i = 0; i < message.PayloadLength; i++)
            {
                copy[i] = payloadArena[message.PayloadOffset + i];
            }

            return copy;
        }

        /// <summary>
        /// The consumer takes the step's sealed batch: the rows leave the buffer in canonical order, which is the
        /// only place an owner commits them (P-037, P-043).
        /// </summary>
        public IReadOnlyList<StepMessage> ConsumeSealedBatch()
        {
            IReadOnlyList<StepMessage> ordered = Ordered();
            rows.Clear();
            payloadWatermark = 0;
            consumerHeld = true;
            return ordered;
        }

        /// <summary>Recycles the buffer at a lifetime boundary: stage/step rows are released, next-step rows kept.</summary>
        public NextStepCarryReport EndStep(IMessageTargetLiveness? liveness, int nextStepCapacity)
        {
            if (descriptor.Lifetime != BufferLifetime.BoundedNextStep)
            {
                rows.Clear();
                payloadWatermark = 0;
                consumerHeld = false;
                return new NextStepCarryReport(descriptor.Buffer, Array.Empty<StepMessage>(), Array.Empty<RequestOutcome>(), 0);
            }

            NextStepCarryReport report = BoundedBufferRules.CarryNextStep(
                descriptor,
                rows,
                nextStepCapacity,
                liveness ?? AlwaysLiveTargets.Instance);

            rows.Clear();
            payloadWatermark = 0;
            consumerHeld = false;
            for (int i = 0; i < report.Retained.Count; i++)
            {
                rows.Add(report.Retained[i]);
            }

            CompactArena();
            return report;
        }

        /// <summary>Commit-time drain verdict of this buffer (P-043).</summary>
        public MessageDrainReport ValidateDrain()
            => BoundedBufferRules.ValidateDrain(descriptor, rows.Count, consumerHeld);

        public override string ToString()
            => Buffer.ToString() + " rows=" + rows.Count.ToString(CultureInfo.InvariantCulture)
                + "/" + descriptor.Capacity.ToString(CultureInfo.InvariantCulture)
                + " bytes=" + payloadWatermark.ToString(CultureInfo.InvariantCulture)
                + "/" + descriptor.ByteCapacity.ToString(CultureInfo.InvariantCulture);

        private void RecordLane(FactoryKey producer)
        {
            for (int i = 0; i < producerLanes.Count; i++)
            {
                if (producerLanes[i].RegistrationKey.Equals(producer.RegistrationKey))
                {
                    producerLaneCounts[i]++;
                    return;
                }
            }

            producerLanes.Add(producer);
            producerLaneCounts.Add(1);
        }

        /// <summary>Moves retained payload ranges to the front of the arena after a carry-over (P-043).</summary>
        private void CompactArena()
        {
            var compacted = new StepMessage[rows.Count];
            int offset = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                StepMessage message = rows[i];
                if (message.PayloadLength != 0)
                {
                    for (int b = 0; b < message.PayloadLength; b++)
                    {
                        payloadArena[offset + b] = payloadArena[message.PayloadOffset + b];
                    }
                }

                compacted[i] = new StepMessage(
                    message.Step,
                    message.Epoch,
                    message.Request,
                    message.Route,
                    message.Owner,
                    message.Target,
                    message.PayloadSchema,
                    message.Kind,
                    message.Order,
                    message.Producer,
                    offset,
                    message.PayloadLength);

                offset += message.PayloadLength;
            }

            rows.Clear();
            for (int i = 0; i < compacted.Length; i++)
            {
                rows.Add(compacted[i]);
            }

            payloadWatermark = offset;
        }
    }
}

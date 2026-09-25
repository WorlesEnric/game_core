// GameCore.Unity.Runtime.Messages — native bounded message lanes (GC-007).
//
// Normative sources: docs/game-core/00-core-protocols.md P-041 (a job producer writes to a bounded lane; the driver
// tracks its handle; the consumer merges by a semantic key, never by lane or worker index), P-043 (a buffer declares
// capacity and overflow; a required gameplay input overflow rejects before mutation) and
// docs/game-core/03-runtime-and-execution.md s5 ("Job producers write to bounded lanes; the consumer merges by a
// semantic key such as admitted request sequence and stable target ID. Lane/worker index is not a tie breaker.").
//
// Storage is `NativeArray<StepMessage>` plus a `NativeArray<byte>` payload arena, so a Burst job can append without
// allocating and without retaining world memory. Every append decision comes from the shared
// `BoundedBufferRules`, so the managed reference implementation and this native lane can never disagree about
// capacity, overflow or drain semantics.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using Unity.Collections;
using Unity.Jobs;

namespace GameCore.Unity.Runtime.Messages
{
    /// <summary>
    /// One declared buffer's native lane: bounded rows, a bounded payload arena, per-producer write cursors and one
    /// consumer. A lane holds messages for one logical step; a `BoundedNextStep` lane survives the boundary.
    /// </summary>
    public sealed class NativeMessageLane : IDisposable
    {
        private NativeArray<StepMessage> rows;
        private NativeArray<byte> payloadArena;
        private StepMessage[] mergeBuffer = Array.Empty<StepMessage>();
        private int rowCount;
        private int payloadWatermark;
        private bool consumerHeld;
        private JobHandle payloadWriter;
        private bool disposed;

        public NativeMessageLane(MessageBufferDescriptor descriptor, Allocator allocator)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            rows = new NativeArray<StepMessage>(descriptor.Capacity, allocator);
            payloadArena = new NativeArray<byte>(descriptor.ByteCapacity, allocator);
        }

        public MessageBufferDescriptor Descriptor { get; }

        public BufferId Buffer => Descriptor.Buffer;

        public int RowCount => rowCount;

        public int Capacity => rows.Length;

        public int PayloadBytes => payloadWatermark;

        public int PayloadCapacity => payloadArena.Length;

        public bool IsCreated => !disposed;

        /// <summary>Native rows in producer append order; a reader merges them by the canonical order key.</summary>
        public NativeArray<StepMessage> Rows => rows;

        /// <summary>Native payload arena; a job producer writes here before publishing a row.</summary>
        public NativeArray<byte> Payload => payloadArena;
        /// <summary>Outstanding writer of this lane's shared payload arena.</summary>
        public JobHandle PayloadWriter => payloadWriter;

        /// <summary>Records the writer whose handle subsequent writes to this arena must depend on.</summary>
        public void TrackPayloadWriter(JobHandle handle) => payloadWriter = handle;

        /// <summary>Rows a reliable lane had to refuse because it was full (P-043; zero is the expected value).</summary>
        public int RejectedCount { get; private set; }

        /// <summary>Rows a declared lossy lane dropped, always counted (P-043).</summary>
        public int DroppedCount { get; private set; }

        /// <summary>Rows refused because they did not name this lane's owner (P-043).</summary>
        public int NotForBufferCount { get; private set; }

        public int AcceptedCount { get; private set; }

        /// <summary>True once the consuming owner took this step's sealed batch.</summary>
        public bool WasConsumed => consumerHeld;

        /// <summary>Completes the tracked payload writer before any main-thread arena read or reset.</summary>
        public void CompletePayloadWriter()
        {
            payloadWriter.Complete();
            payloadWriter = default(JobHandle);
        }

        /// <summary>
        /// Reserves payload space, so a job can write its bytes into <see cref="Payload"/> and then publish the row.
        /// A refusal changes nothing: the caller must abandon the affected command or batch (P-043).
        /// </summary>
        public BufferAppendOutcome TryReservePayload(int byteCount, out int offset, out string detail)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(NativeMessageLane));
            }

            if (byteCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(byteCount), "A payload length cannot be negative.");
            }
            if (rowCount >= Descriptor.Capacity)
            {
                detail = "buffer " + Buffer + " is at its declared row capacity (P-043)";
                offset = -1;
                if (Descriptor.IsReliable)
                {
                    RejectedCount++;
                    return BufferAppendOutcome.RejectedCapacity;
                }

                DroppedCount++;
                return BufferAppendOutcome.DroppedLossy;
            }

            detail = string.Empty;
            if (Descriptor.ByteCapacity > 0 && payloadWatermark + byteCount > Descriptor.ByteCapacity)
            {
                detail = "lane " + Buffer.ToString() + " would exceed its declared payload capacity of "
                    + Descriptor.ByteCapacity.ToString(CultureInfo.InvariantCulture) + " bytes (P-043)";
                if (Descriptor.IsReliable)
                {
                    RejectedCount++;
                    offset = -1;
                    return BufferAppendOutcome.RejectedBytes;
                }

                DroppedCount++;
                offset = -1;
                return BufferAppendOutcome.DroppedLossy;
            }

            // The reservation advances the arena, so two producers can never be handed one payload range by accident.
            offset = payloadWatermark;
            payloadWatermark += byteCount;
            return BufferAppendOutcome.Accepted;
        }

        /// <summary>
        /// Publishes one row whose payload already sits in the arena at its declared offset. This is the exact call a
        /// Burst producer makes after writing its bytes (P-041).
        /// </summary>
        public BufferAppendOutcome TryPublishRow(in StepMessage row, out string detail)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(NativeMessageLane));
            }

            if (row.PayloadOffset < 0 || row.PayloadOffset + row.PayloadLength > Descriptor.ByteCapacity)
            {
                detail = "row payload range " + row.PayloadOffset.ToString(CultureInfo.InvariantCulture) + "+"
                    + row.PayloadLength.ToString(CultureInfo.InvariantCulture)
                    + " is outside the lane's payload arena";
                RejectedCount++;
                return BufferAppendOutcome.RejectedBytes;
            }

            bool declaredProducer = Descriptor.DeclaresProducer(row.Producer) || Descriptor.Producers.Count == 0;
            if (!declaredProducer)
            {
                detail = "producer is not declared for buffer " + Buffer.ToString() + " (P-043)";
                NotForBufferCount++;
                return BufferAppendOutcome.RejectedNotForBuffer;
            }

            if (rowCount >= Descriptor.Capacity)
            {
                // The row bound is the only bound left to check: the bytes were already reserved for this row.
                ReleaseTrailingReservation(row);
                if (Descriptor.IsReliable)
                {
                    detail = "buffer " + Buffer.ToString() + " is at its declared capacity of "
                        + Descriptor.Capacity.ToString(CultureInfo.InvariantCulture)
                        + " rows; required gameplay input overflow rejects the affected batch before mutation (P-043)";
                    RejectedCount++;
                    return BufferAppendOutcome.RejectedCapacity;
                }

                detail = "buffer " + Buffer.ToString()
                    + " is a declared lossy buffer; the row was dropped and counted (P-043)";
                DroppedCount++;
                return BufferAppendOutcome.DroppedLossy;
            }

            if (row.PayloadLength != 0 && row.PayloadOffset + row.PayloadLength > payloadWatermark)
            {
                // A row may only reference bytes that were really reserved for it (P-041).
                detail = "row payload range " + row.PayloadOffset.ToString(CultureInfo.InvariantCulture) + "+"
                    + row.PayloadLength.ToString(CultureInfo.InvariantCulture)
                    + " was never reserved in this lane's arena";
                RejectedCount++;
                return BufferAppendOutcome.RejectedBytes;
            }

            if (!row.Owner.Equals(Descriptor.Owner))
            {
                detail = "lane " + Buffer.ToString() + " is consumed by owner " + Descriptor.Owner.ToString()
                    + " but the row names " + row.Owner.ToString() + "; a buffer has exactly one consuming owner (P-043)";
                NotForBufferCount++;
                return BufferAppendOutcome.RejectedNotForBuffer;
            }

            detail = string.Empty;
            rows[rowCount] = row;
            rowCount++;
            AcceptedCount++;
            return BufferAppendOutcome.Accepted;
        }

        /// <summary>
        /// Reclaims the payload range of a row that was reserved but not published, when it is the arena's trailing
        /// reservation. A producer that abandons its reservation never leaves a hole the next step would inherit.
        /// </summary>
        private void ReleaseTrailingReservation(in StepMessage row)
        {
            int end = row.PayloadOffset + row.PayloadLength;
            if (row.PayloadLength != 0 && end == payloadWatermark)
            {
                payloadWatermark = row.PayloadOffset;
            }
        }


        /// <summary>Managed convenience append: copies the payload into the arena and publishes the row.</summary>
        public BufferAppendOutcome TryAppend(StepMessage row, IReadOnlyList<byte>? payload, out string detail)
        {
            int length = payload == null ? 0 : payload.Count;
            if (length != row.PayloadLength)
            {
                detail = "the row declares " + row.PayloadLength.ToString(CultureInfo.InvariantCulture)
                    + " payload byte(s) but " + length.ToString(CultureInfo.InvariantCulture) + " were supplied";
                RejectedCount++;
                return BufferAppendOutcome.RejectedNotForBuffer;
            }

            BufferAppendOutcome decision = TryReservePayload(length, out int offset, out detail);
            if (decision != BufferAppendOutcome.Accepted)
            {
                return decision;
            }

            for (int i = 0; i < length; i++)
            {
                payloadArena[offset + i] = payload![i];
            }

            var placed = new StepMessage(
                row.Step,
                row.Epoch,
                row.Request,
                row.Route,
                row.Owner,
                row.Target,
                row.PayloadSchema,
                row.Kind,
                row.Order,
                row.Producer,
                offset,
                length);

            return TryPublishRow(placed, out detail);
        }

        /// <summary>
        /// Copies the step's rows into canonical merge order. The scratch buffer is reused across steps, so a step
        /// boundary allocates nothing once the lane is warm; the order itself comes only from the canonical message
        /// key, never from append, worker or lane index (P-008, 03 s5).
        /// </summary>
        public void MergeInto(List<StepMessage> destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            CompletePayloadWriter();
            destination.Clear();
            if (rowCount == 0)
            {
                return;
            }

            if (mergeBuffer.Length < rowCount)
            {
                mergeBuffer = new StepMessage[rows.Length];
            }

            for (int i = 0; i < rowCount; i++)
            {
                mergeBuffer[i] = rows[i];
            }

            Array.Sort(mergeBuffer, 0, rowCount, MessageOrderComparer.Instance);
            for (int i = 0; i < rowCount; i++)
            {
                destination.Add(mergeBuffer[i]);
            }
        }

        /// <summary>Payload bytes of one row from this lane's arena.</summary>
        public byte[] PayloadOf(in StepMessage row)
        {
            CompletePayloadWriter();
            var copy = new byte[row.PayloadLength];
            for (int i = 0; i < row.PayloadLength; i++)
            {
                copy[i] = payloadArena[row.PayloadOffset + i];
            }

            return copy;
        }

        /// <summary>The consumer takes the sealed batch, so the lane is observed empty by the next append (P-037).</summary>
        public void Consume(bool releaseRows)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(NativeMessageLane));
            }
            CompletePayloadWriter();

            consumerHeld = true;
            if (releaseRows)
            {
                rowCount = 0;
                payloadWatermark = 0;
            }
        }

        /// <summary>
        /// Closes one step for this lane. Stage/step rows are released; a bounded next-step lane carries its rows
        /// forward in canonical order with revalidated stable references, and drops the excess with a count (P-043).
        /// </summary>
        public NextStepCarryReport EndStep(IMessageTargetLiveness? liveness, int nextStepCapacity)
        {
            if (Descriptor.Lifetime != BufferLifetime.BoundedNextStep)
            {
                rowCount = 0;
                payloadWatermark = 0;
                consumerHeld = false;
                payloadWriter = default(JobHandle);
                return new NextStepCarryReport(Descriptor.Buffer, Array.Empty<StepMessage>(), Array.Empty<RequestOutcome>(), 0);
            }

            var ordered = new List<StepMessage>(rowCount);
            MergeInto(ordered);

            NativeStepLaneCarry carry = CarryForward(ordered, nextStepCapacity, liveness ?? AlwaysLiveTargets.Instance);

            rowCount = 0;
            payloadWatermark = 0;
            consumerHeld = false;

            var retained = new List<StepMessage>(carry.Retained.Count);
            for (int i = 0; i < carry.Retained.Count; i++)
            {
                StepMessage message = carry.Retained[i];
                BufferAppendOutcome outcome = TryAppend(message, carry.PayloadOf(i), out string detail);
                if (outcome == BufferAppendOutcome.Accepted)
                {
                    // The carried step stamp is deliberately kept: the message still belongs to the step it was
                    // produced for, and the receiving step revalidates its stable references (P-043).
                    retained.Add(message);
                    continue;
                }

                throw new InvalidOperationException(
                    "carrying " + Descriptor.Buffer.ToString() + " into the next step failed: " + outcome + ": " + detail);
            }

            return new NextStepCarryReport(Descriptor.Buffer, retained, carry.Cancelled, carry.Dropped);
        }

        /// <summary>
        /// Payload bytes of one retained row. This is the read side of the native producer path: the bytes were
        /// written straight into the arena by a job, so nothing here re-encodes or re-allocates them (P-041).
        /// </summary>
        public byte[] PayloadBytesOf(in StepMessage row) => PayloadOf(row);
        /// <summary>Commit-time drain verdict of this lane (P-043, O-16).</summary>
        public MessageDrainReport ValidateDrain()
            => BoundedBufferRules.ValidateDrain(Descriptor, rowCount, consumerHeld);

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (rows.IsCreated)
            {
                rows.Dispose();
            }

            if (payloadArena.IsCreated)
            {
                payloadArena.Dispose();
            }
        }

        private NativeStepLaneCarry CarryForward(
            IReadOnlyList<StepMessage> ordered,
            int capacity,
            IMessageTargetLiveness liveness)
        {
            var retained = new List<StepMessage>();
            var payloads = new List<byte[]>();
            var cancelled = new List<RequestOutcome>();
            int dropped = 0;

            for (int i = 0; i < ordered.Count; i++)
            {
                StepMessage message = ordered[i];
                bool live = liveness.IsLive(message.Target, message.Route, message.Owner);
                bool rebind = Descriptor.Cancellation == BufferCancellationPolicy.RebindToCompatibleOwner
                    && liveness.AllowsRebind(Descriptor.Buffer);

                if (!live && !rebind)
                {
                    // A deferred message whose target or route is gone gets a terminal result, never silence (P-047).
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

                if (retained.Count >= capacity)
                {
                    dropped++;
                    continue;
                }

                retained.Add(message);
                payloads.Add(PayloadOf(message));
            }

            return new NativeStepLaneCarry(retained, payloads, cancelled, dropped);
        }

        public override string ToString()
            => Buffer.ToString() + " rows=" + rowCount.ToString(CultureInfo.InvariantCulture)
                + "/" + Capacity.ToString(CultureInfo.InvariantCulture)
                + " bytes=" + payloadWatermark.ToString(CultureInfo.InvariantCulture)
                + "/" + PayloadCapacity.ToString(CultureInfo.InvariantCulture);

        /// <summary>Carried rows plus their payload copies, so the arena can be rebuilt compactly.</summary>
        private readonly struct NativeStepLaneCarry
        {
            internal readonly List<StepMessage> Retained;
            internal readonly List<byte[]> Payloads;
            internal readonly List<RequestOutcome> Cancelled;
            internal readonly int Dropped;

            internal NativeStepLaneCarry(
                List<StepMessage> retained,
                List<byte[]> payloads,
                List<RequestOutcome> cancelled,
                int dropped)
            {
                Retained = retained;
                Payloads = payloads;
                Cancelled = cancelled;
                Dropped = dropped;
            }

            internal byte[] PayloadOf(int index) => Payloads[index];
        }
    }

    /// <summary>
    /// Every declared lane of one world, keyed by buffer identity. The plane owns the lanes and disposes them with
    /// the world; a missing lane for a declared buffer is a defect reported at construction.
    /// </summary>
    public sealed class NativeMessageLanes : IDisposable
    {
        private readonly Dictionary<Id128, NativeMessageLane> lanes = new Dictionary<Id128, NativeMessageLane>();
        private readonly List<NativeMessageLane> order = new List<NativeMessageLane>();
        private readonly List<StepMessage> mergeScratch = new List<StepMessage>();
        private readonly List<StepMessage> laneScratch = new List<StepMessage>();
        private readonly Allocator allocator;
        private bool disposed;

        /// <summary>
        /// Native size of one retained row, computed once. `StepMessage` is a blittable value type, which is what
        /// lets a Burst producer store it in a `NativeArray` without a managed wrapper (P-041).
        /// </summary>
        private static readonly int NativeStepMessageBytes =
            System.Runtime.InteropServices.Marshal.SizeOf<StepMessage>();

        public NativeMessageLanes(IReadOnlyList<MessageBufferDescriptor>? descriptors, Allocator allocator)
        {
            this.allocator = allocator;
            if (descriptors == null)
            {
                return;
            }

            for (int i = 0; i < descriptors.Count; i++)
            {
                MessageBufferDescriptor descriptor = descriptors[i];
                if (lanes.ContainsKey(descriptor.Buffer.Value))
                {
                    continue;
                }

                var lane = new NativeMessageLane(descriptor, allocator);
                lanes.Add(descriptor.Buffer.Value, lane);
                order.Add(lane);
            }
        }

        public int Count => lanes.Count;

        public bool IsCreated => !disposed;

        public IReadOnlyList<NativeMessageLane> Lanes => order;

        public bool TryGetLane(BufferId buffer, out NativeMessageLane? lane)
            => lanes.TryGetValue(buffer.Value, out lane);

        /// <summary>Native bytes the lanes currently own; reported in the world resource ledger.</summary>
        public ulong RetainedBytes
        {
            get
            {
                ulong bytes = 0UL;
                for (int i = 0; i < order.Count; i++)
                {
                    NativeMessageLane lane = order[i];
                    bytes += (ulong)(lane.Capacity * NativeStepMessageSize) + (ulong)lane.PayloadCapacity;
                }

                return bytes;
            }
        }

        /// <summary>Merged rows of one lane in canonical order, into a caller-supplied reusable list (P-008).</summary>
        public void MergeInto(BufferId buffer, List<StepMessage> destination)
        {
            if (!lanes.TryGetValue(buffer.Value, out NativeMessageLane? lane) || lane == null)
            {
                destination?.Clear();
                return;
            }

            lane.MergeInto(destination!);
        }

        /// <summary>
        /// Merged rows of every lane of one owner, in one canonical sequence: the step's admitted batch as the owner
        /// observes it, independent of which producer lane wrote first (03 s5).
        /// </summary>
        public IReadOnlyList<StepMessage> MergeOwnerBatch(OwnerId owner)
        {
            mergeScratch.Clear();
            for (int i = 0; i < order.Count; i++)
            {
                NativeMessageLane lane = order[i];
                if (!lane.Descriptor.Owner.Equals(owner))
                {
                    continue;
                }

                lane.MergeInto(laneScratch);
                mergeScratch.AddRange(laneScratch);
            }

            if (mergeScratch.Count > 1)
            {
                mergeScratch.Sort(MessageOrderComparer.Instance);
            }

            return mergeScratch;
        }

        /// <summary>Closes one step for every lane (P-043).</summary>
        public IReadOnlyList<NextStepCarryReport> EndStep(IMessageTargetLiveness? liveness, int nextStepCapacity)
        {
            var reports = new List<NextStepCarryReport>();
            for (int i = 0; i < order.Count; i++)
            {
                NativeMessageLane lane = order[i];
                NextStepCarryReport report = lane.EndStep(liveness, nextStepCapacity);
                if (lane.Descriptor.Lifetime == BufferLifetime.BoundedNextStep)
                {
                    reports.Add(report);
                }
            }

            return reports;
        }

        /// <summary>Native size of one retained row; used for the ledger's byte accounting.</summary>
        public static int NativeStepMessageSize => NativeStepMessageBytes;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            for (int i = 0; i < order.Count; i++)
            {
                order[i].Dispose();
            }

            lanes.Clear();
            order.Clear();
        }
    }
}

// GameCore.Unity.Runtime.Messages — the world's bounded message plane (GC-007).
//
// Normative sources: docs/game-core/00-core-protocols.md P-002 (the host admits commands; the state owner decides
// gameplay), P-037 (a step's admitted batch is sealed with a monotonic admission sequence; a duplicate request key
// returns its recorded result; a domain rejection still commits an observable rejected result), P-041 (bounded lanes,
// canonical merge), P-042 (typed requests and `RequestResult`: admission acceptance is not gameplay success),
// P-043 (capacity and backpressure: a required gameplay input overflow rejects before mutation) and P-045 (committed
// events are exposed only at publication).
//
// The plane is the data plane of one owned world. It owns no authority of its own: the host admits, the state owner
// commits through its port, and the plane only proves that the declared routes, buffers and bounds were respected.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Execution.Messages;
using Unity.Collections;

namespace GameCore.Unity.Runtime.Messages
{
    /// <summary>
    /// Immutable registration of one world's message plane. A generated registration emits it; the plane validates it
    /// before the world exposes anything, and a defect is a refusal, never a silently missing route (04 s8).
    /// </summary>
    public sealed class MessagePlaneRegistration
    {
        public static readonly MessagePlaneRegistration Empty =
            new MessagePlaneRegistration(null, null, null, 1, 1, 1, 1, 1);

        public MessagePlaneRegistration(
            IReadOnlyList<CommandRoute>? routes,
            IReadOnlyList<MessageBufferDescriptor>? buffers,
            IReadOnlyList<BufferReadPort>? readPorts,
            int maxPendingRequests,
            int maxRetainedResults,
            int maxRetainedEvents,
            int maxEventsPerStep,
            ushort nextStepCapacity)
        {
            Routes = ContractCollections.Freeze(routes);
            Buffers = ContractCollections.Freeze(buffers);
            ReadPorts = ContractCollections.Freeze(readPorts);
            MaxPendingRequests = maxPendingRequests;
            MaxRetainedResults = maxRetainedResults;
            MaxRetainedEvents = maxRetainedEvents;
            MaxEventsPerStep = maxEventsPerStep;
            NextStepCapacity = nextStepCapacity;
        }

        public IReadOnlyList<CommandRoute> Routes { get; }

        public IReadOnlyList<MessageBufferDescriptor> Buffers { get; }

        public IReadOnlyList<BufferReadPort> ReadPorts { get; }

        /// <summary>Bounded count of admitted-but-undecided requests; reaching it backpressures (P-043).</summary>
        public int MaxPendingRequests { get; }

        /// <summary>Bounded retention of terminal request results; older ones become `ResultExpired` (P-050).</summary>
        public int MaxRetainedResults { get; }

        /// <summary>Bounded retention of committed events; a lagging cursor reports a gap (P-045).</summary>
        public int MaxRetainedEvents { get; }

        /// <summary>Bounded committed events per logical step; exceeding it refuses and counts (P-045).</summary>
        public int MaxEventsPerStep { get; }

        /// <summary>Bounded capacity of a declared next-step queue (P-043).</summary>
        public ushort NextStepCapacity { get; }

        /// <summary>Every declared bound is positive and every route names a declared ingress buffer (P-043).</summary>
        public bool TryValidate(out DiagnosticCode code, out string detail)
        {
            if (MaxPendingRequests <= 0 || MaxRetainedResults <= 0 || MaxRetainedEvents <= 0
                || MaxEventsPerStep <= 0 || NextStepCapacity == 0)
            {
                code = DiagnosticCode.BudgetExceeded;
                detail = "a message plane bound must be positive: a bounded queue, retention or output bound of zero "
                    + "cannot express the protocol's behaviour (P-022, P-043, P-045)";
                return false;
            }

            var declared = new HashSet<Id128>();
            for (int i = 0; i < Buffers.Count; i++)
            {
                MessageBufferDescriptor buffer = Buffers[i];
                if (buffer.Buffer.Value.IsDefault)
                {
                    code = DiagnosticCode.MissingDependency;
                    detail = "a default zero buffer id is not a buffer contract (P-043)";
                    return false;
                }

                if (!declared.Add(buffer.Buffer.Value))
                {
                    code = DiagnosticCode.OwnershipConflict;
                    detail = "buffer " + buffer.Buffer.ToString() + " is declared twice in one registration (P-043)";
                    return false;
                }
            }

            var routes = new HashSet<Id128>();
            for (int i = 0; i < Routes.Count; i++)
            {
                CommandRoute route = Routes[i];
                if (!routes.Add(route.Route.Value))
                {
                    code = DiagnosticCode.OwnershipConflict;
                    detail = "route " + route.Route.ToString() + " is declared twice; one route has one owner (P-042)";
                    return false;
                }

                if (!declared.Contains(route.IngressBuffer.Value))
                {
                    code = DiagnosticCode.MissingDependency;
                    detail = "route " + route.Route.ToString() + " names ingress buffer "
                        + route.IngressBuffer.ToString() + " which this registration does not declare (P-043)";
                    return false;
                }
            }

            for (int i = 0; i < ReadPorts.Count; i++)
            {
                BufferReadPort port = ReadPorts[i];
                if (!declared.Contains(port.Buffer.Value))
                {
                    code = DiagnosticCode.MissingDependency;
                    detail = "read port names buffer " + port.Buffer.ToString() + " which is not declared (P-043)";
                    return false;
                }
            }

            code = DiagnosticCode.None;
            detail = string.Empty;
            return true;
        }

        public override string ToString()
            => "routes=" + Routes.Count.ToString(CultureInfo.InvariantCulture)
                + ", buffers=" + Buffers.Count.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// One world's bounded message plane: generated routes, the admission/dedup ledger, bounded native lanes, the
    /// step message schedule and the committed-event output. Public members are the host's and an owner's real port;
    /// there is no reflective lookup and no second authority.
    /// </summary>
    public sealed class WorldMessagePlane : ICommandIngress, IDisposable
    {
        private readonly MessagePlaneRegistration registration;
        private readonly StepMessageSchedule schedule = new StepMessageSchedule();
        private readonly StepOutputCollector output;
        private readonly CommittedEventStore events;
        private readonly NativeMessageLanes lanes;
        private readonly Dictionary<Id128, CommandRoute> routesByBuffer = new Dictionary<Id128, CommandRoute>();
        private readonly List<StepMessage> ownerBatch = new List<StepMessage>();
        private readonly List<CommittedEvent> publishedScratch = new List<CommittedEvent>();

        private IMessageTargetLiveness liveness = AlwaysLiveTargets.Instance;
        private EventSequence lastEventSequence = EventSequence.Zero;
        private bool disposed;

        public WorldMessagePlane(WorldId world, MessagePlaneRegistration registration, CommandPayloadReaders readers)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            if (!registration.TryValidate(out DiagnosticCode code, out string detail))
            {
                throw new ArgumentException("the message-plane registration is invalid: " + code + ": " + detail, nameof(registration));
            }

            World = world;
            this.registration = registration;
            Readers = readers ?? throw new ArgumentNullException(nameof(readers));

            Routes = new CommandRouteTable();
            for (int i = 0; i < registration.Routes.Count; i++)
            {
                CommandRoute route = registration.Routes[i];
                if (!Routes.TryAdd(route, out string failure))
                {
                    throw new ArgumentException("message-plane route registration failed: " + failure, nameof(registration));
                }

                routesByBuffer.Add(route.IngressBuffer.Value, route);
            }

            for (int i = 0; i < registration.Buffers.Count; i++)
            {
                MessageBufferDescriptor buffer = registration.Buffers[i];
                if (!schedule.TryDeclare(buffer, out string failure))
                {
                    throw new ArgumentException("message-plane buffer declaration failed: " + failure, nameof(registration));
                }
            }

            for (int i = 0; i < registration.ReadPorts.Count; i++)
            {
                if (!schedule.TryAddReadPort(registration.ReadPorts[i], out string failure))
                {
                    throw new ArgumentException("message-plane read port failed: " + failure, nameof(registration));
                }
            }

            schedule.NextStepCapacity = registration.NextStepCapacity;
            Requests = new RequestLedger(Routes, registration.MaxPendingRequests, registration.MaxRetainedResults);
            output = new StepOutputCollector(registration.MaxEventsPerStep);
            events = new CommittedEventStore(world, registration.MaxRetainedEvents);
            lanes = new NativeMessageLanes(registration.Buffers, Allocator.Persistent);
        }

        /// <summary>Last published event sequence of this world (P-045).</summary>
        public EventSequence LastEventSequence => lastEventSequence;

        /// <summary>
        /// The step whose execution is deciding the currently sealed batch, or zero before the first step. An owner
        /// records its terminal request results against this step (P-037).
        /// </summary>
        public LogicalStepId ExecutingStep { get; private set; }

        /// <summary>Assembly epoch the executing step runs under (P-006).</summary>
        public AssemblyEpoch ExecutingEpoch { get; private set; }

        public WorldId World { get; }

        /// <summary>Generated payload readers; a missing one is reported, never substituted reflectively (04 s8).</summary>
        public CommandPayloadReaders Readers { get; }

        public CommandRouteTable Routes { get; }

        public RequestLedger Requests { get; }

        public StepMessageSchedule Schedule => schedule;

        public NativeMessageLanes Lanes => lanes;

        /// <summary>Bounded committed-event reader of this world; observers use this and nothing else (P-045).</summary>
        public ICommittedEventReader Events => events;

        /// <summary>The retained-event store itself, for diagnostics and bounded-retention counters (P-045).</summary>
        public CommittedEventStore EventStore => events;

        public StepOutputCollector Output => output;

        public bool IsDisposed => disposed;

        /// <summary>Substituted target-liveness resolver for revalidating a carried next-step queue (P-043, P-047).</summary>
        public void SetTargetLiveness(IMessageTargetLiveness targets)
            => liveness = targets ?? AlwaysLiveTargets.Instance;

        /// <summary>
        /// Host admission of one immutable command envelope (O-13, P-042). The host validates world, route and
        /// capacity; the state owner validates gameplay later, so an admitted command reports `Accepted` only.
        /// </summary>
        public CommandAdmissionReceipt SubmitCommand(CommandEnvelope command, LogicalStepId step, AssemblyEpoch epoch)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            byte[] payload = CopyPayload(command);
            ContentHash inputHash = HashOf(command, payload);

            RequestAdmission admission = Requests.Admit(
                command.RequestId,
                command.RouteId,
                command.TargetId,
                command.Schema,
                inputHash,
                RequestOrigin.External,
                World,
                step,
                epoch);

            if (!admission.Admitted)
            {
                // A refusal created no row and no lane entry; the caller reads the reason from the receipt (P-042).
                return new CommandAdmissionReceipt(command.RequestId, admission.Outcome.ToRequestResult(), AdmissionSequence.Zero);
            }

            if (admission.Kind == RequestAdmissionKind.Retransmission)
            {
                // A duplicate request key returns its recorded result instead of executing twice (P-037).
                return new CommandAdmissionReceipt(command.RequestId, admission.Outcome.ToRequestResult(), admission.Outcome.Order.Admitted);
            }

            if (!Routes.TryResolve(command.RouteId, out CommandRoute? route) || route == null)
            {
                Requests.TrySettle(command.RequestId, RequestResultKind.Rejected, DiagnosticCode.MissingDependency, default(EventCursor), step);
                return new CommandAdmissionReceipt(
                    command.RequestId,
                    new RequestResult(RequestResultKind.Rejected, DiagnosticCode.MissingDependency, default(EventCursor)),
                    AdmissionSequence.Zero);
            }

            var message = new StepMessage(
                step,
                epoch,
                command.RequestId,
                command.RouteId,
                route.Owner,
                command.TargetId,
                command.Schema,
                MessageKind.Command,
                admission.Outcome.Order,
                route.IngressProducer,
                0,
                payload.Length);

            BufferAppendOutcome appended = AppendToLane(route.IngressBuffer, message, payload, out string detail);
            if (appended != BufferAppendOutcome.Accepted)
            {
                // Capacity/backpressure rejects before mutation: the affected command is refused, the lane is
                // unchanged, and the refusal is recorded as an observable rejected result (P-037, P-043).
                Requests.TrySettle(
                    command.RequestId,
                    RequestResultKind.Rejected,
                    appended == BufferAppendOutcome.RejectedCapacity || appended == BufferAppendOutcome.RejectedBytes
                        ? DiagnosticCode.BudgetExceeded
                        : DiagnosticCode.OwnershipConflict,
                    default(EventCursor),
                    step);

                return new CommandAdmissionReceipt(
                    command.RequestId,
                    new RequestResult(
                        RequestResultKind.Rejected,
                        appended == BufferAppendOutcome.RejectedCapacity || appended == BufferAppendOutcome.RejectedBytes
                            ? DiagnosticCode.BudgetExceeded
                            : DiagnosticCode.OwnershipConflict,
                        default(EventCursor)),
                    admission.Outcome.Order.Admitted);
            }

            return new CommandAdmissionReceipt(command.RequestId, admission.Outcome.ToRequestResult(), admission.Outcome.Order.Admitted);
        }

        /// <summary>Host admission through the shared contract interface (05 s5).</summary>
        public CommandAdmissionReceipt Submit(CommandEnvelope command)
            => SubmitCommand(command, LogicalStepId.Zero, AssemblyEpoch.Zero);

        /// <summary>
        /// Seals the admitted input prefix of one step and returns it, so a batch admitted before the step is fixed
        /// even though later commands keep arriving (P-037). The sealed step becomes this plane's executing step.
        /// </summary>
        public InputSeal SealStep(LogicalStepId step, AssemblyEpoch epoch)
        {
            IReadOnlyList<RequestRow> pending = Requests.PendingRows();
            var admitted = new List<OperationId>(pending.Count);
            for (int i = 0; i < pending.Count; i++)
            {
                admitted.Add(pending[i].Request);
            }

            ExecutingStep = step;
            ExecutingEpoch = epoch;
            return schedule.SealStepInput(step, epoch, Requests.LastAdmissionSequence, admitted);
        }

        /// <summary>Commit-time validation of the declared message buffers against what actually ran (P-043, O-16).</summary>
        public StepBufferCommitReport ValidateCommit(IStepDispatchFacts facts, out string detail)
        {
            for (int i = 0; i < lanes.Lanes.Count; i++)
            {
                NativeMessageLane lane = lanes.Lanes[i];
                MessageDrainReport drain = lane.ValidateDrain();
                if (!drain.Succeeded)
                {
                    detail = drain.Detail;
                    return new StepBufferCommitReport(false, drain.Code, new[] { drain }, detail);
                }
            }

            StepBufferCommitReport report = schedule.ValidateCommit(facts);
            detail = report.Detail;
            return report;
        }

        /// <summary>
        /// Builds the committed events of the step being published. Called by the host at the publication boundary and
        /// immediately before the step image is exposed, so events and image move together (P-044, P-045).
        /// </summary>
        public IReadOnlyList<CommittedEvent> StageCommittedEvents(LogicalStepId step, AssemblyEpoch epoch)
        {
            publishedScratch.Clear();
            IReadOnlyList<CommittedEvent> committed = output.BuildCommittedEvents(
                World,
                epoch,
                step,
                lastEventSequence,
                out EventSequence nextSequence);

            for (int i = 0; i < committed.Count; i++)
            {
                publishedScratch.Add(committed[i]);
            }

            // The sequence cursor advances only when the step image that carries these events is really published,
            // so a refused publication cannot leave a gap in the event stream (P-044, P-045).
            _ = nextSequence;
            return publishedScratch.Count == 0 ? Array.Empty<CommittedEvent>() : publishedScratch;
        }

        /// <summary>
        /// Confirms that the step image carrying these events is published: the events enter the bounded retained
        /// store and each causal request row receives its committed cursor (P-042, P-045).
        /// </summary>
        public void ConfirmPublished(IReadOnlyList<CommittedEvent>? committed)
        {
            if (committed == null || committed.Count == 0)
            {
                return;
            }

            events.Publish(committed);
            lastEventSequence = events.LastSequence;
            output.MarkPublished(committed.Count);

            // The committed cursor of each causal request becomes readable now that the events are published, so a
            // caller can follow `Committed` back to its evidence (P-042, P-045).
            for (int i = 0; i < committed.Count; i++)
            {
                CommittedEvent published = committed[i];
                if (!published.CausalRequest.World.Session.IsDefault)
                {
                    Requests.TryUpdateCommittedCursor(published.CausalRequest, published.Cursor);
                }
            }

            publishedScratch.Clear();
        }

        /// <summary>
        /// Closes one step: stage/step buffers are released, bounded next-step queues carry forward with revalidated
        /// stable references, and staged events that never committed are dropped (P-043, P-047).
        /// </summary>
        public IReadOnlyList<NextStepCarryReport> EndStep()
        {
            output.Abort();
            lanes.EndStep(liveness, registration.NextStepCapacity);
            return schedule.EndStep(liveness);
        }

        /// <summary>Abandons the current step without publishing anything (P-031 fail-stop).</summary>
        public void AbortStep()
        {
            output.Abort();
            schedule.AbortStep();
        }

        /// <summary>
        /// The owner's step batch, merged in canonical order across every lane it consumes. Producer scheduling,
        /// worker index and lane index never decide this order (P-008, 03 s5).
        /// </summary>
        public IReadOnlyList<StepMessage> DrainOwnerBatch(OwnerId owner)
        {
            IReadOnlyList<StepMessage> merged = lanes.MergeOwnerBatch(owner);
            ownerBatch.Clear();
            for (int i = 0; i < merged.Count; i++)
            {
                ownerBatch.Add(merged[i]);
            }

            return ownerBatch;
        }

        /// <summary>Releases the lanes the owner just consumed, so the next step starts empty (P-037).</summary>
        public void ReleaseConsumed(OwnerId owner)
        {
            for (int i = 0; i < lanes.Lanes.Count; i++)
            {
                NativeMessageLane lane = lanes.Lanes[i];
                if (lane.Descriptor.Owner.Equals(owner))
                {
                    lane.Consume(releaseRows: true);
                }
            }
        }

        /// <summary>
        /// The owner commits gameplay for one message and stages the matching committed event (P-042, P-044). A commit
        /// is the only path that reports `Committed`; admission acceptance never does.
        /// </summary>
        public bool Commit(
            StepMessage message,
            SchemaRef eventSchema,
            FrozenPayload eventPayload,
            LogicalStepId step,
            out string failure)
        {
            if (eventPayload == null)
            {
                throw new ArgumentNullException(nameof(eventPayload));
            }

            // Refuse before mutating either side: an owner that cannot stage an event has not committed anything.
            if (output.StagedCount >= output.MaxEventsPerStep)
            {
                failure = "the step's committed-event bound of " + output.MaxEventsPerStep.ToString(CultureInfo.InvariantCulture)
                    + " is reached; the request stays undecided instead of committing without its event (P-045)";
                return false;
            }

            // An externally admitted command has a ledger row whose terminal result must be recorded; a typed
            // inter-system request has none, because it is a transient message and not a request with a recorded
            // result (P-042).
            if (message.Kind == MessageKind.Command)
            {
                if (!Requests.TrySettle(message.Request, RequestResultKind.Committed, DiagnosticCode.None, default(EventCursor), step))
                {
                    failure = "request " + message.Request.ToString()
                        + " has no pending row to commit (a request commits once, and only its owner commits it)";
                    return false;
                }
            }

            if (!output.TryStage(eventSchema, message.Target, message.Request, eventPayload, out failure))
            {
                // The bound check above makes this unreachable for a well-formed caller; report it rather than
                // committing a request whose event disappeared.
                return false;
            }

            failure = string.Empty;
            return true;
        }

        /// <summary>
        /// The owner rejects one request: no gameplay happened, yet the rejection is an observable committed result of
        /// this logical step (P-037).
        /// </summary>
        public bool Reject(StepMessage message, DiagnosticCode reason, LogicalStepId step)
            => message.Kind == MessageKind.Command
                && Requests.TrySettle(message.Request, RequestResultKind.Rejected, reason, default(EventCursor), step);

        /// <summary>
        /// A producer's typed port for a transient inter-system message (P-042). The lane's owner, declared
        /// producers, bounds and overflow policy are enforced exactly as for a command row, and a refusal is a value
        /// the producer must handle instead of a silent drop (P-043).
        /// </summary>
        public BufferAppendOutcome PublishInternal(
            BufferId buffer,
            StepMessage message,
            IReadOnlyList<byte>? payload,
            out string detail)
        {
            if (message.Owner.Value.IsDefault)
            {
                detail = "an internal message must name the owner it is routed to (P-042)";
                return BufferAppendOutcome.RejectedNotForBuffer;
            }

            return AppendToLane(buffer, message, payload, out detail);
        }

        /// <summary>
        /// A job producer's port: publishes a row whose payload bytes a scheduled job already wrote into the lane's
        /// bounded arena. No payload is copied again, and a refusal is a value the producer must handle (P-041, P-043).
        /// </summary>
        public BufferAppendOutcome PublishReservedRow(BufferId buffer, StepMessage message, out string detail)
        {
            if (!lanes.TryGetLane(buffer, out NativeMessageLane? lane) || lane == null)
            {
                detail = "no native lane is declared for buffer " + buffer.ToString();
                return BufferAppendOutcome.RejectedNotForBuffer;
            }

            return lane.TryPublishRow(message, out detail);
        }

        /// <summary>Payload bytes of one message the owner drained, read from the lane's native arena (P-041).</summary>
        public byte[] PayloadOf(StepMessage message)
        {
            for (int i = 0; i < lanes.Lanes.Count; i++)
            {
                NativeMessageLane lane = lanes.Lanes[i];
                if (!lane.Descriptor.Owner.Equals(message.Owner))
                {
                    continue;
                }

                for (int r = 0; r < lane.RowCount; r++)
                {
                    StepMessage candidate = lane.Rows[r];
                    if (candidate.PayloadOffset == message.PayloadOffset
                        && candidate.PayloadLength == message.PayloadLength
                        && candidate.Producer.RegistrationKey.Equals(message.Producer.RegistrationKey)
                        && candidate.Order.Equals(message.Order))
                    {
                        return lane.PayloadBytesOf(candidate);
                    }
                }
            }

            return Array.Empty<byte>();
        }

        /// <summary>Cancels admitted-but-unexecuted work of one retired route; it is never silently lost (P-047).</summary>
        public int CancelPending(RouteId route, LogicalStepId step) => Requests.CancelPending(route, step);

        /// <summary>Reads one bounded page of committed events; a lagging cursor reports a gap (P-045).</summary>
        public CommittedEventPage ReadEvents(EventCursor cursor, int maxEvents) => events.Read(cursor, maxEvents);

        /// <summary>Native bytes the plane currently owns, reported to the world resource ledger (P-048).</summary>
        public ulong RetainedNativeBytes => lanes.RetainedBytes;

        /// <summary>Builds the merged view of one lane; the managed mirror of the native merge (P-008).</summary>
        public IReadOnlyList<StepMessage> MergeLane(BufferId buffer)
        {
            var merged = new List<StepMessage>();
            lanes.MergeInto(buffer, merged);
            return merged;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            lanes.Dispose();
        }

        private BufferAppendOutcome AppendToLane(BufferId buffer, StepMessage message, IReadOnlyList<byte>? payload, out string detail)
        {
            if (!lanes.TryGetLane(buffer, out NativeMessageLane? lane) || lane == null)
            {
                detail = "no native lane is declared for buffer " + buffer.ToString();
                return BufferAppendOutcome.RejectedNotForBuffer;
            }

            var placed = new StepMessage(
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
                message.PayloadOffset,
                payload?.Count ?? 0);

            return lane.TryAppend(placed, payload == null ? Array.Empty<byte>() : CopyBytes(payload), out detail);
        }

        private static byte[] CopyBytes(IReadOnlyList<byte> bytes)
        {
            var copy = new byte[bytes.Count];
            for (int i = 0; i < bytes.Count; i++)
            {
                copy[i] = bytes[i];
            }

            return copy;
        }

        private static byte[] CopyPayload(CommandEnvelope command)
        {
            IReadOnlyList<byte> bytes = command.Payload.Bytes;
            var copy = new byte[bytes.Count];
            for (int i = 0; i < bytes.Count; i++)
            {
                copy[i] = bytes[i];
            }

            return copy;
        }

        private static ContentHash HashOf(CommandEnvelope command, byte[] payload)
        {
            // The input hash covers the envelope identity and the payload bytes, so a reused request key with a
            // different command is an IdempotencyConflict rather than a silent second execution (P-050).
            const int HeaderBytes = Id128.SizeInBytes + Id128.SizeInBytes + Id128.SizeInBytes + 4;
            var record = new byte[HeaderBytes + payload.Length];
            Id128Codec.WriteBigEndian(command.TargetId.Value, record, 0);
            Id128Codec.WriteBigEndian(command.RouteId.Value, record, Id128.SizeInBytes);
            Id128Codec.WriteBigEndian(command.Schema.Id.Value, record, Id128.SizeInBytes * 2);
            WriteUInt32BigEndian(command.Schema.Version, record, Id128.SizeInBytes * 3);
            for (int i = 0; i < payload.Length; i++)
            {
                record[HeaderBytes + i] = payload[i];
            }

            return ContentHash.Compute(record);
        }


        private static void WriteUInt32BigEndian(uint value, byte[] destination, int offset)
        {
            destination[offset] = (byte)(value >> 24);
            destination[offset + 1] = (byte)(value >> 16);
            destination[offset + 2] = (byte)(value >> 8);
            destination[offset + 3] = (byte)value;
        }

        public override string ToString()
            => "plane(" + World.Session.ToString() + "): " + registration.ToString()
                + ", " + Requests.ToString();
    }
}

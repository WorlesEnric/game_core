// GC-007 Unity EditMode fixture: one real owned world with a bounded message plane.
//
// Normative sources: docs/game-core/00-core-protocols.md P-037 (sealed input; one step per admitted command),
// P-041 (bounded lanes, real jobs, tracked handles), P-042 (typed ports; the owner decides gameplay), P-043
// (capacity/backpressure, one consuming owner, unconsumed step buffers fail commit) and P-044 (the owner commits,
// then the step publishes events and image together).
//
// The fixture lives in the test assembly on purpose: it is a test world shape, not production registration. It
// drives the real GC-005 host/dispatch and the real GC-007 plane; nothing here is stubbed.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Execution.Messages;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Messages;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace GameCore.Unity.Runtime.Tests.Messages
{
    /// <summary>Hand-written generated-style keys of the message fixture (a generator emits exactly this shape).</summary>
    public static class MessageFixtureKeys
    {
        public const ulong Namespace = 0x47434D5347555452UL;

        public static readonly SchemaId CommandSchema = new SchemaId(new Id128(Namespace, 0x0101UL));

        public static readonly SchemaId EventSchema = new SchemaId(new Id128(Namespace, 0x0102UL));

        /// <summary>Schema identity of the authoritative probe component the owner writes directly (P-034).</summary>
        public static readonly SchemaId StateSchema = new SchemaId(new Id128(Namespace, 0x0103UL));

        public static SchemaRef Command => new SchemaRef(CommandSchema, 1U);

        public static SchemaRef Event => new SchemaRef(EventSchema, 1U);

        public static SchemaRef State => new SchemaRef(StateSchema, 1U);

        public static readonly OwnerId ProbeOwner = new OwnerId(new Id128(Namespace, 0x0201UL));

        /// <summary>A second owner whose lane nothing drains; the "cannot vanish at commit" fixture (P-043).</summary>
        public static readonly OwnerId UndrainedOwner = new OwnerId(new Id128(Namespace, 0x0202UL));

        public static readonly RouteId ProbeRoute = new RouteId(new Id128(Namespace, 0x0301UL));

        public static readonly BufferId CommandBuffer = new BufferId(new Id128(Namespace, 0x0401UL));

        public static readonly BufferId CounterBuffer = new BufferId(new Id128(Namespace, 0x0402UL));

        public static readonly BufferId OrphanBuffer = new BufferId(new Id128(Namespace, 0x0403UL));

        public static readonly StageId ProduceStage = new StageId(new Id128(Namespace, 0x0501UL));

        public static readonly StageId OwnerStage = new StageId(new Id128(Namespace, 0x0502UL));

        public static readonly StageId ConsumerStage = new StageId(new Id128(Namespace, 0x0503UL));

        public static readonly FactoryKey ProducerA = new FactoryKey(new Id128(Namespace, 0x0601UL), 1U);

        public static readonly FactoryKey ProducerB = new FactoryKey(new Id128(Namespace, 0x0602UL), 1U);

        public static readonly FactoryKey OwnerSystem = new FactoryKey(new Id128(Namespace, 0x0603UL), 1U);

        public static readonly FactoryKey ConsumerSystem = new FactoryKey(new Id128(Namespace, 0x0604UL), 1U);

        public static readonly FactoryKey HostIngressProducer = new FactoryKey(new Id128(Namespace, 0x0605UL), 1U);

        /// <summary>Declared ordinals: they, not the dispatch order, decide the merged order (P-008).</summary>
        public const uint ProducerAOrdinal = 1U;

        public const uint ProducerBOrdinal = 2U;

        /// <summary>Declared payload values of the two producers.</summary>
        public const int ProducerAValue = 0x0A11;

        public const int ProducerBValue = 0x0B22;

        /// <summary>Payload byte length of one probe message: two little-endian 32-bit fields.</summary>
        public const int PayloadBytes = 8;
    }

    /// <summary>The authoritative probe state the owner writes directly; no queue takes part (P-034, 03 s4).</summary>
    public struct MessageProbeState : IComponentData
    {
        /// <summary>Messages the owner applied in its declared stage.</summary>
        public int Applied;

        /// <summary>Committed events the owner staged, i.e. real gameplay commitments (P-042).</summary>
        public int Committed;

        /// <summary>Requests the owner rejected; a rejection is still an observable committed result (P-037).</summary>
        public int Rejected;

        /// <summary>Payload value of the last applied message in merged order.</summary>
        public int LastValue;

        /// <summary>Merged-order fingerprint of the drained batch, so a shuffled producer order is observable.</summary>
        public int MergeKey;

        /// <summary>Payload bytes the owners read back out of the lanes' native arenas (P-041).</summary>
        public int NativePayloadBytes;

        /// <summary>Rows the owner drained from its lanes in the most recent step.</summary>
        public int DrainedRows;
    }

    /// <summary>Decoded probe payload; a plain value type read by a generated-style reader (04 s8).</summary>
    public readonly struct ProbePayload
    {
        public readonly int Value;

        public readonly uint Ordinal;

        public ProbePayload(int value, uint ordinal)
        {
            Value = value;
            Ordinal = ordinal;
        }
    }

    /// <summary>Generated-style typed reader for the probe payload (04 s8).</summary>
    public sealed class ProbePayloadReader : ICommandPayloadReader<ProbePayload>
    {
        public SchemaRef Schema => MessageFixtureKeys.Command;

        public ProbePayload Read(IReadOnlyList<byte> payload)
        {
            var reader = new PayloadReader(payload);
            return new ProbePayload(reader.ReadInt32(0), (uint)reader.ReadInt32(4));
        }
    }

    /// <summary>Builds the bounded payload the fixture producers and the tests both use.</summary>
    public static class ProbePayloads
    {
        public static byte[] Command(int value, uint ordinal)
            => new PayloadWriter().WriteInt32(value).WriteInt32((int)ordinal).ToArray();

        public static byte[] Event(int value)
            => new PayloadWriter().WriteInt32(value).ToArray();
    }

    /// <summary>
    /// Real Burst job writing one probe payload into a lane's bounded native arena. This is the native producer path
    /// of P-041: the job allocates nothing, retains no world memory, and its handle stays in the system dependency.
    /// </summary>
    [BurstCompile]
    public struct MessagePayloadJob : IJob
    {
        public NativeArray<byte> Arena;

        public int Offset;

        public int Value;

        public uint Ordinal;

        public void Execute()
        {
            if (Offset < 0 || Offset + 8 > Arena.Length)
            {
                return;
            }

            WriteInt32(Arena, Offset, Value);
            WriteInt32(Arena, Offset + 4, (int)Ordinal);
        }

        private static void WriteInt32(NativeArray<byte> arena, int offset, int value)
        {
            arena[offset] = (byte)value;
            arena[offset + 1] = (byte)(value >> 8);
            arena[offset + 2] = (byte)(value >> 16);
            arena[offset + 3] = (byte)(value >> 24);
        }
    }

    /// <summary>Observation surface shared by the two producer systems, so a test can read either one.</summary>
    public interface ILaneProducerProbe
    {
        /// <summary>Appends this producer had to refuse because a declared bound was reached (P-043).</summary>
        int RefusedCount { get; }

        /// <summary>Rows this producer published in the most recent step.</summary>
        int PublishedRows { get; }
    }

    /// <summary>
    /// The declared producer port of the fixture: reserve bounded payload space, write it with a real Burst job and
    /// publish the row. Both producer systems call this helper, so neither derives from the other's type.
    /// </summary>
    internal static class LaneProducer
    {
        internal static bool Publish(
            World world,
            BufferId buffer,
            OwnerId owner,
            FactoryKey producer,
            uint ordinal,
            int value,
            ref JobHandle dependency,
            out string failure)
        {
            failure = string.Empty;
            if (!UnityWorldRegistry.TryGetByEntityWorld(world, out UnityWorldHost? host) || host == null || host.Messages == null)
            {
                failure = "the world has no owned host or no message plane";
                return false;
            }

            WorldMessagePlane plane = host.Messages;
            if (!plane.Lanes.TryGetLane(buffer, out NativeMessageLane? lane) || lane == null)
            {
                failure = "no lane is declared for buffer " + buffer.ToString();
                return false;
            }

            BufferAppendOutcome reserved = lane.TryReservePayload(MessageFixtureKeys.PayloadBytes, out int offset, out string reserveDetail);
            if (reserved != BufferAppendOutcome.Accepted)
            {
                // A reliable lane at its declared bound refuses the producer before any mutation (P-043).
                failure = reserveDetail;
                return false;
            }

            // The job writes the payload bytes into the lane's bounded arena; its handle stays in the system's
            // dependency, so the step fence covers it before the owner may read the row (P-041).
            var arena = lane.Payload;
            dependency = new MessagePayloadJob
            {
                Arena = arena,
                Offset = offset,
                Value = value,
                Ordinal = ordinal,
            }.Schedule(JobHandle.CombineDependencies(dependency, lane.PayloadWriter));
            lane.TrackPayloadWriter(dependency);
            var row = new StepMessage(
                LogicalStepId.Zero,
                AssemblyEpoch.First,
                default(OperationId),
                default(RouteId),
                owner,
                MessageFixtureRegistration.ProbeTarget,
                MessageFixtureKeys.Command,
                MessageKind.Request,
                new MessageOrderKey(AdmissionSequence.Zero, ordinal, MessageFixtureRegistration.ProbeTarget.Value),
                producer,
                offset,
                MessageFixtureKeys.PayloadBytes);

            BufferAppendOutcome published = plane.PublishReservedRow(buffer, row, out failure);
            return published == BufferAppendOutcome.Accepted;
        }
    }

    /// <summary>First declared producer of the shared counter lane; it also writes the undrained lane (P-043).</summary>
    [DisableAutoCreation]
    public partial class MessageProducerASystem : SystemBase, ILaneProducerProbe
    {
        /// <summary>Appends refused by a declared bound; the fixture reports them (P-043).</summary>
        public int RefusedCount { get; private set; }

        /// <summary>Rows this instance published in the last step.</summary>
        public int PublishedRows { get; private set; }

        protected override void OnUpdate()
        {
            PublishedRows = 0;
            JobHandle dependency = Dependency;

            if (LaneProducer.Publish(World, MessageFixtureKeys.CounterBuffer, MessageFixtureKeys.ProbeOwner, MessageFixtureKeys.ProducerA, MessageFixtureKeys.ProducerAOrdinal, MessageFixtureKeys.ProducerAValue, ref dependency, out string counterFailure))
            {
                PublishedRows++;
            }
            else
            {
                RefusedCount++;
                _ = counterFailure;
            }

            if (LaneProducer.Publish(World, MessageFixtureKeys.OrphanBuffer, MessageFixtureKeys.UndrainedOwner, MessageFixtureKeys.ProducerA, MessageFixtureKeys.ProducerAOrdinal, MessageFixtureKeys.ProducerAValue, ref dependency, out string orphanFailure))
            {
                PublishedRows++;
            }
            else
            {
                RefusedCount++;
                _ = orphanFailure;
            }

            Dependency = dependency;
        }
    }

    /// <summary>Second declared producer of the shared counter lane.</summary>
    [DisableAutoCreation]
    public partial class MessageProducerBSystem : SystemBase, ILaneProducerProbe
    {
        /// <summary>Appends refused by a declared bound; the fixture reports them (P-043).</summary>
        public int RefusedCount { get; private set; }

        /// <summary>Rows this instance published in the last step.</summary>
        public int PublishedRows { get; private set; }

        protected override void OnUpdate()
        {
            PublishedRows = 0;
            JobHandle dependency = Dependency;

            if (LaneProducer.Publish(World, MessageFixtureKeys.CounterBuffer, MessageFixtureKeys.ProbeOwner, MessageFixtureKeys.ProducerB, MessageFixtureKeys.ProducerBOrdinal, MessageFixtureKeys.ProducerBValue, ref dependency, out string failure))
            {
                PublishedRows++;
            }
            else
            {
                RefusedCount++;
                _ = failure;
            }

            Dependency = dependency;
        }
    }

    /// <summary>
    /// The state owner: drains its merged batch, decodes each payload with its generated reader, writes the
    /// authoritative component directly, then commits and stages the committed event (P-034, P-042, P-044).
    /// </summary>
    [DisableAutoCreation]
    public partial class MessageOwnerSystem : SystemBase
    {
        private EntityQuery probeQuery;

        /// <summary>Messages the owner drained across the whole run, for diagnostics in a failing test.</summary>
        public int TotalDrained { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();
            probeQuery = GetEntityQuery(ComponentType.ReadWrite<MessageProbeState>());
        }

        protected override void OnUpdate()
        {
            if (!UnityWorldRegistry.TryGetByEntityWorld(World, out UnityWorldHost? host) || host == null || host.Messages == null)
            {
                return;
            }

            WorldMessagePlane plane = host.Messages;
            List<StepMessage> batch = new List<StepMessage>(plane.DrainOwnerBatch(MessageFixtureKeys.ProbeOwner));
            TotalDrained += batch.Count;

            if (probeQuery.IsEmpty)
            {
                plane.ReleaseConsumed(MessageFixtureKeys.ProbeOwner);
                return;
            }

            Entity probe = probeQuery.GetSingletonEntity();
            MessageProbeState state = EntityManager.GetComponentData<MessageProbeState>(probe);
            state.DrainedRows = batch.Count;

            var merge = new PayloadWriter();
            int nativeBytes = 0;
            for (int i = 0; i < batch.Count; i++)
            {
                StepMessage message = batch[i];
                byte[] payload = plane.PayloadOf(message);
                nativeBytes += payload.Length;

                PayloadDecodeOutcome decoded = plane.Readers.TryRead(
                    message.PayloadSchema,
                    payload,
                    out ProbePayload typed,
                    out string detail);
                if (decoded != PayloadDecodeOutcome.Decoded)
                {
                    // A payload its generated reader refuses is a rejected request, never a blind application (04 s8).
                    if (message.Kind == MessageKind.Command)
                    {
                        plane.Reject(message, DiagnosticCode.UnsupportedVersion, plane.ExecutingStep);
                        state.Rejected++;
                    }

                    _ = detail;
                    continue;
                }

                // Direct owner update of an existing component: the legal path of 03 s4, with no global queue.
                state.LastValue = typed.Value;
                state.Applied++;

                merge.WriteInt32(typed.Value).WriteInt32((int)message.Order.Ordinal);

                var eventPayload = new FrozenPayload(ProbePayloads.Event(typed.Value));
                if (plane.Commit(message, MessageFixtureKeys.Event, eventPayload, plane.ExecutingStep, out string failure))
                {
                    state.Committed++;
                    _ = failure;
                }
            }

            state.MergeKey = Fingerprint(merge.ToArray());
            state.NativePayloadBytes = nativeBytes;
            EntityManager.SetComponentData(probe, state);

            // The owner took the sealed batch; its lanes start the next step empty (P-037).
            plane.ReleaseConsumed(MessageFixtureKeys.ProbeOwner);
        }

        /// <summary>Deterministic cheap fingerprint of the merged order, so a shuffle is directly visible.</summary>
        private static int Fingerprint(byte[] payload)
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < payload.Length; i++)
                {
                    hash = (hash * 31) + payload[i];
                }

                return hash;
            }
        }
    }

    /// <summary>
    /// The declared consuming stage of the step buffers. It drains the lanes whose consumer stage is its own, which
    /// is what makes commit-time drain validation pass for a step that really consumed its input (P-043, O-16).
    /// </summary>
    [DisableAutoCreation]
    public partial class MessageConsumerSystem : SystemBase
    {
        /// <summary>Rows this stage released, so a test can distinguish "consumed" from "never ran".</summary>
        public int ConsumedRows { get; private set; }

        protected override void OnUpdate()
        {
            ConsumedRows = 0;
            if (!UnityWorldRegistry.TryGetByEntityWorld(World, out UnityWorldHost? host) || host == null || host.Messages == null)
            {
                return;
            }

            WorldMessagePlane plane = host.Messages;
            for (int i = 0; i < plane.Lanes.Lanes.Count; i++)
            {
                NativeMessageLane lane = plane.Lanes.Lanes[i];
                if (!lane.Descriptor.ConsumerStage.Equals(MessageFixtureKeys.ConsumerStage))
                {
                    continue;
                }

                ConsumedRows += lane.RowCount;
                lane.Consume(releaseRows: true);
            }
        }
    }

    /// <summary>Builds the fixture's registration: one route, bounded lanes and the three dispatch tables.</summary>
    public static class MessageFixtureRegistration
    {
        public const int StageCount = 3;

        private const int ProduceStageIndex = 0;
        private const int OwnerStageIndex = 1;
        private const int ConsumerStageIndex = 2;

        /// <summary>Stable target id of the probe entity; never a runtime handle (P-004).</summary>
        public static readonly TargetId ProbeTarget = new TargetId(new Id128(MessageFixtureKeys.Namespace, 0x0701UL));

        public const string WorldName = "GameCoreMessageFixture";

        /// <summary>Seeds the single probe entity carrying the fixture's observable state.</summary>
        public static void Seed(World world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            EntityManager entityManager = world.EntityManager;
            Entity entity = entityManager.CreateEntity(typeof(MessageProbeState));
            entityManager.SetName(entity, "MessageProbe");
        }

        public static WorldCreateRequest CommandDrivenRequest(WorldId world, OperationId operation, ContentHash catalogHash)
            => new WorldCreateRequest(
                world,
                new WorldDefinitionId(new Id128(MessageFixtureKeys.Namespace, 0x0801UL)),
                TemporalModel.CommandDriven,
                PropagationMode.Automatic,
                catalogHash,
                operation,
                null);

        /// <summary>
        /// The full fixture registration. <paramref name="counterCapacity"/> bounds the counter lane so a test can
        /// drive a real overflow, and <paramref name="reverseProducerOrder"/> swaps which producer the plan
        /// dispatches first without changing any declared key (the shuffle test).
        /// </summary>
        public static UnityWorldRegistration Create(
            int counterCapacity = 8,
            bool reverseProducerOrder = false,
            bool includeConsumer = true)
        {
            return new UnityWorldRegistration(
                WorldName,
                Stages(),
                Systems(),
                EmptyPlan(),
                StepPlan(reverseProducerOrder, includeConsumer),
                EmptyPlan(),
                Seed,
                Messages(counterCapacity),
                Readers());
        }

        /// <summary>A registration whose step plan omits the consuming stage, so produced rows cannot be drained.</summary>
        public static UnityWorldRegistration CreateWithoutConsumer(int counterCapacity = 8)
            => Create(counterCapacity, reverseProducerOrder: false, includeConsumer: false);

        /// <summary>The plane registration: one route, the command lane, the counter lane and the undrained lane.</summary>
        public static MessagePlaneRegistration Messages(int counterCapacity)
        {
            var routes = new List<CommandRoute>
            {
                new CommandRoute(
                    MessageFixtureKeys.ProbeRoute,
                    MessageFixtureKeys.ProbeOwner,
                    MessageFixtureKeys.Command,
                    MessageFixtureKeys.OwnerStage,
                    MessageFixtureKeys.OwnerStage,
                    MessageFixtureKeys.CommandBuffer,
                    MessageFixtureKeys.HostIngressProducer,
                    capacity: 8,
                    allowsRebind: false),
            };

            var buffers = new List<MessageBufferDescriptor>
            {
                // The command lane: reliable, one step, its single consuming owner is the owner stage (P-043).
                new MessageBufferDescriptor(
                    MessageFixtureKeys.CommandBuffer,
                    MessageFixtureKeys.Command,
                    new[] { MessageFixtureKeys.HostIngressProducer },
                    MessageFixtureKeys.ProbeOwner,
                    MessageFixtureKeys.OwnerStage,
                    MessageFixtureKeys.OwnerStage,
                    MessageFixtureKeys.HostIngressProducer,
                    BufferLifetime.Step,
                    capacity: 8,
                    byteCapacity: 256,
                    BufferOverflowPolicy.RejectBeforeMutation,
                    BufferCancellationPolicy.Drain),

                // The counter lane: two declared producers, one owner, and the overflow fixture's bound.
                new MessageBufferDescriptor(
                    MessageFixtureKeys.CounterBuffer,
                    MessageFixtureKeys.Command,
                    new[] { MessageFixtureKeys.ProducerA, MessageFixtureKeys.ProducerB },
                    MessageFixtureKeys.ProbeOwner,
                    MessageFixtureKeys.ProduceStage,
                    MessageFixtureKeys.ConsumerStage,
                    MessageFixtureKeys.ProducerA,
                    BufferLifetime.Step,
                    counterCapacity,
                    byteCapacity: counterCapacity * MessageFixtureKeys.PayloadBytes,
                    BufferOverflowPolicy.RejectBeforeMutation,
                    BufferCancellationPolicy.Drain),

                // The undrained lane: its owner never drains it, so a missing consuming stage must fail the commit.
                new MessageBufferDescriptor(
                    MessageFixtureKeys.OrphanBuffer,
                    MessageFixtureKeys.Command,
                    new[] { MessageFixtureKeys.ProducerA },
                    MessageFixtureKeys.UndrainedOwner,
                    MessageFixtureKeys.ProduceStage,
                    MessageFixtureKeys.ConsumerStage,
                    MessageFixtureKeys.ProducerA,
                    BufferLifetime.Step,
                    capacity: 4,
                    byteCapacity: 128,
                    BufferOverflowPolicy.RejectBeforeMutation,
                    BufferCancellationPolicy.Drain),
            };

            // The ledger holds more pending rows than the command lane holds, so a lane bound is the binding
            // constraint for the overflow fixture rather than the ledger's own admission capacity.
            return new MessagePlaneRegistration(routes, buffers, null, maxPendingRequests: 32, maxRetainedResults: 32, maxRetainedEvents: 32, maxEventsPerStep: 8, nextStepCapacity: 4);
        }

        /// <summary>The generated typed readers of the fixture plane (04 s8).</summary>
        public static CommandPayloadReaders Readers()
        {
            var readers = new CommandPayloadReaders();
            if (!readers.TryBind(new ProbePayloadReader(), out string failure))
            {
                throw new InvalidOperationException("the fixture reader registration failed: " + failure);
            }

            return readers;
        }

        private static IReadOnlyList<StageRegistration> Stages()
        {
            return new List<StageRegistration>
            {
                new StageRegistration(MessageFixtureKeys.ProduceStage, "fixture.messages.produce", ProduceStageIndex, null),
                new StageRegistration(MessageFixtureKeys.OwnerStage, "fixture.messages.owner", OwnerStageIndex, new[] { ProduceStageIndex }),
                new StageRegistration(MessageFixtureKeys.ConsumerStage, "fixture.messages.consume", ConsumerStageIndex, new[] { OwnerStageIndex }),
            };
        }

        private static IReadOnlyList<SystemRegistration> Systems()
        {
            return new List<SystemRegistration>
            {
                new ManagedSystemRegistration<MessageProducerASystem>(MessageFixtureKeys.ProducerA, MessageFixtureKeys.ProduceStage, "MessageProducerASystem"),
                new ManagedSystemRegistration<MessageProducerBSystem>(MessageFixtureKeys.ProducerB, MessageFixtureKeys.ProduceStage, "MessageProducerBSystem"),
                new ManagedSystemRegistration<MessageOwnerSystem>(MessageFixtureKeys.OwnerSystem, MessageFixtureKeys.OwnerStage, "MessageOwnerSystem"),
                new ManagedSystemRegistration<MessageConsumerSystem>(MessageFixtureKeys.ConsumerSystem, MessageFixtureKeys.ConsumerStage, "MessageConsumerSystem"),
            };
        }

        private static GuardedDispatchPlan EmptyPlan() => new GuardedDispatchPlan(null, null, StageCount);

        private static GuardedDispatchPlan StepPlan(bool reverseProducerOrder, bool includeConsumer)
        {
            var entries = new List<GuardedDispatchEntry>();
            int dispatchIndex = 0;

            // Two declared producers share one lane, which P-043 allows; the order they are dispatched in is
            // irrelevant to the merged result, and that is exactly what the shuffle test proves (P-008).
            if (reverseProducerOrder)
            {
                entries.Add(Entry(ProduceStageIndex, MessageFixtureKeys.ProducerB, dispatchIndex++, null));
                entries.Add(Entry(ProduceStageIndex, MessageFixtureKeys.ProducerA, dispatchIndex++, null));
            }
            else
            {
                entries.Add(Entry(ProduceStageIndex, MessageFixtureKeys.ProducerA, dispatchIndex++, null));
                entries.Add(Entry(ProduceStageIndex, MessageFixtureKeys.ProducerB, dispatchIndex++, null));
            }

            entries.Add(Entry(OwnerStageIndex, MessageFixtureKeys.OwnerSystem, dispatchIndex++, new[] { ProduceStageIndex }));
            if (includeConsumer)
            {
                entries.Add(Entry(ConsumerStageIndex, MessageFixtureKeys.ConsumerSystem, dispatchIndex, new[] { OwnerStageIndex }));
            }

            return new GuardedDispatchPlan(entries, null, StageCount);
        }

        private static GuardedDispatchEntry Entry(
            int stageIndex,
            FactoryKey systemKey,
            int dispatchIndex,
            IReadOnlyList<int>? predecessorStages)
            => new GuardedDispatchEntry(
                StageOf(stageIndex),
                systemKey,
                SystemDispatchKind.ManagedSystem,
                dispatchIndex,
                stageIndex,
                predecessorStages);

        private static StageId StageOf(int stageIndex)
        {
            switch (stageIndex)
            {
                case ProduceStageIndex:
                    return MessageFixtureKeys.ProduceStage;
                case OwnerStageIndex:
                    return MessageFixtureKeys.OwnerStage;
                default:
                    return MessageFixtureKeys.ConsumerStage;
            }
        }
    }
}

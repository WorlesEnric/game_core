// GC-007 Unity EditMode tests — authority, requests, bounded buffers and committed output in real worlds.
//
// Normative sources: TEST-013 (authority, direct writes, requests and buffers), TEST-014 (committed events and
// consistent observation), TEST-009 (publication visibility) and 09 GC-007. Every case below runs a real
// `Unity.Entities.World` through the GC-005 host and the GC-007 plane; the pure ownership half of TEST-013 lives in
// Packages/com.gamecore.planning/Tests/Ownership.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Planning.Ownership;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Messages;
using NUnit.Framework;
using Unity.Entities;

namespace GameCore.Unity.Runtime.Tests.Messages
{
    /// <summary>World-level authority, request, buffer and output behaviour (TEST-013, TEST-014).</summary>
    [TestFixture]
    public sealed class WorldMessagePlaneTests
    {
        private const ulong SessionSalt = 0x4D5347504C414E45UL;

        private static readonly Id128 Issuer = new Id128(0x4953535545524D53UL, 1UL);

        private static ulong sessionSequence;

        [TearDown]
        public void TearDown() => UnityWorldRegistry.ResetAll();

        [Test]
        public void ACommandRoutesToItsOwnerAndCommitsAtTheStepBoundary()
        {
            UnityWorldHost host = CreateWorld(MessageFixtureRegistration.Create());
            WorldMessagePlane plane = Plane(host);

            CommandAdmissionReceipt receipt = Submit(host, 1UL, 0x1234);
            Assert.That(receipt.Admitted, Is.True, receipt.Result.Reason.ToString());
            Assert.That(receipt.Result.Kind, Is.EqualTo(RequestResultKind.Accepted), "Admission acceptance is not gameplay success (P-042).");

            WorldPumpResult pump = host.PumpFrame(1_000_000UL);
            Assert.That(pump.Pumped, Is.True);
            Assert.That(pump.Advance, Is.Not.Null);
            Assert.That(pump.Advance!.Outcome, Is.EqualTo(Outcome.Published), host.FaultDetail);
            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.First), "One admitted command is exactly one logical step (P-036).");

            MessageProbeState state = ReadProbe(host);
            Assert.That(state.Applied, Is.EqualTo(3), "Two produced rows plus the routed command reached the owner.");
            Assert.That(state.Committed, Is.EqualTo(3), "The owner staged one committed event per applied message (P-044).");
            Assert.That(state.LastValue, Is.EqualTo(0x1234), "The command has the highest canonical order key, so it is applied last.");
            Assert.That(state.NativePayloadBytes, Is.EqualTo(3 * MessageFixtureKeys.PayloadBytes));

            RequestRow row = Row(plane, receipt.Request);
            Assert.That(row.Outcome.Kind, Is.EqualTo(RequestResultKind.Committed));
            Assert.That(row.Outcome.CausalCursor.World.Session, Is.EqualTo(host.World.Session));
            Assert.That(row.TerminalStep, Is.EqualTo(plane.ExecutingStep), "The decision is recorded against the executing step (P-037).");

            CommittedEventPage page = plane.ReadEvents(new EventCursor(host.World, EventSequence.Zero), 8);
            Assert.That(page.Outcome, Is.EqualTo(CursorOutcome.Ok));
            Assert.That(page.Events.Count, Is.EqualTo(3), "A step exposes its committed events together with its image (P-044, P-045).");
            Assert.That(page.Events[0].Step, Is.EqualTo(host.CurrentStep), "Events belong to the committed step the image carries.");
            Assert.That(page.Events[0].Epoch, Is.EqualTo(host.CurrentEpoch));
            Assert.That(host.Publications.PublishedCount, Is.EqualTo(2), "The initial image plus the committed step.");
        }

        [Test]
        public void AdmissionAcceptanceIsDistinguishableFromGameplayCommitment()
        {
            UnityWorldHost host = CreateWorld(MessageFixtureRegistration.Create());
            WorldMessagePlane plane = Plane(host);

            CommandAdmissionReceipt receipt = Submit(host, 1UL, 7);
            RequestRow beforeStep = Row(plane, receipt.Request);

            Assert.That(beforeStep.Outcome.Kind, Is.EqualTo(RequestResultKind.Accepted), "Admission acceptance is its own result kind (P-042).");
            Assert.That(beforeStep.IsPending, Is.True);
            Assert.That(beforeStep.Outcome.CausalCursor.World.Session.IsDefault, Is.True, "No committed cursor exists before the owner decides.");
            Assert.That(ReadProbe(host).Applied, Is.EqualTo(0), "No gameplay state changes at admission.");
            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.Zero));

            host.PumpFrame(1_000_000UL);

            RequestRow afterStep = Row(plane, receipt.Request);
            Assert.That(afterStep.Outcome.Kind, Is.EqualTo(RequestResultKind.Committed));
            Assert.That(afterStep.Outcome.CausalCursor.World.Session, Is.EqualTo(host.World.Session), "The committed cursor is readable after publication.");
            Assert.That(afterStep.IsPending, Is.False);
        }

        [Test]
        public void ADuplicateCommandReturnsItsRecordedResultAndExecutesOnce()
        {
            UnityWorldHost host = CreateWorld(MessageFixtureRegistration.Create());
            WorldMessagePlane plane = Plane(host);

            var envelope = Envelope(host, 1UL, 99);
            CommandAdmissionReceipt first = host.Submit(envelope);
            CommandAdmissionReceipt duplicate = host.Submit(envelope);

            Assert.That(first.Admitted, Is.True);
            Assert.That(duplicate.Admitted, Is.True, "A duplicate request key is coalesced, not refused (P-037).");
            Assert.That(duplicate.Result.Kind, Is.EqualTo(first.Result.Kind));
            Assert.That(plane.Requests.DuplicateCount, Is.EqualTo(1));
            Assert.That(plane.Requests.AdmittedCount, Is.EqualTo(1), "One request key owns one row.");
            Assert.That(host.PendingDemand, Is.EqualTo(1UL), "A retransmission creates no second unit of demand.");

            host.PumpFrame(1_000_000UL);

            MessageProbeState state = ReadProbe(host);
            Assert.That(state.LastValue, Is.EqualTo(99));
            Assert.That(state.Committed, Is.EqualTo(3), "The duplicate never executes a second time.");
            Assert.That(plane.ReadEvents(new EventCursor(host.World, EventSequence.Zero), 8).Events.Count, Is.EqualTo(3));
        }

        [Test]
        public void OverflowRejectsBeforeMutationAndLeavesStateUnchanged()
        {
            // The counter lane is declared with capacity 1, so the second producer's row cannot be accepted.
            UnityWorldHost host = CreateWorld(MessageFixtureRegistration.Create(counterCapacity: 1));
            WorldMessagePlane plane = Plane(host);

            host.RequestWake(1U);
            WorldPumpResult pump = host.PumpFrame(1_000_000UL);
            Assert.That(pump.Advance!.Outcome, Is.EqualTo(Outcome.Published), host.FaultDetail);

            Assert.That(Producer(host, MessageFixtureKeys.ProducerA).RefusedCount, Is.EqualTo(0));
            Assert.That(Producer(host, MessageFixtureKeys.ProducerB).RefusedCount, Is.EqualTo(1), "A reliable lane at its bound refuses (P-043).");

            Assert.That(plane.Lanes.TryGetLane(MessageFixtureKeys.CounterBuffer, out NativeMessageLane? lane), Is.True);
            Assert.That(lane!.RejectedCount, Is.EqualTo(1), "The refusal is counted, not silently dropped.");
            Assert.That(lane.RowCount, Is.EqualTo(0), "The consumed lane is empty at the step boundary.");

            MessageProbeState state = ReadProbe(host);
            Assert.That(state.Applied, Is.EqualTo(1), "Only the accepted row mutated authoritative state.");
            Assert.That(state.Committed, Is.EqualTo(1));
            Assert.That(state.LastValue, Is.EqualTo(MessageFixtureKeys.ProducerAValue));
            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.First));
        }

        [Test]
        public void ACommandThatOverflowsItsIngressLaneIsRefusedBeforeTheStep()
        {
            UnityWorldHost host = CreateWorld(MessageFixtureRegistration.Create());
            WorldMessagePlane plane = Plane(host);

            // Nine admitted commands exceed the command lane's declared capacity of eight. The lane refuses the ninth
            // without mutating itself, and the refusal is recorded as an observable rejected result (P-037, P-043).
            CommandAdmissionReceipt last = default(CommandAdmissionReceipt)!;
            for (ulong sequence = 1UL; sequence <= 9UL; sequence++)
            {
                last = Submit(host, sequence, (int)(0x100UL + sequence));
            }

            Assert.That(last.Admitted, Is.False);
            Assert.That(last.Result.Kind, Is.EqualTo(RequestResultKind.Rejected));
            Assert.That(last.Result.Reason, Is.EqualTo(DiagnosticCode.BudgetExceeded), "The refusal names the capacity bound.");
            Assert.That(plane.Lanes.TryGetLane(MessageFixtureKeys.CommandBuffer, out NativeMessageLane? lane), Is.True);
            Assert.That(lane!.RowCount, Is.EqualTo(8), "The lane holds exactly its declared capacity.");
            Assert.That(lane.RejectedCount, Is.EqualTo(1), "The overflow is counted, never a silent drop.");
            Assert.That(plane.Requests.TryGet(last.Request, out RequestRow? refused), Is.True, "The refused command keeps an observable result row.");
            Assert.That(refused!.Outcome.Kind, Is.EqualTo(RequestResultKind.Rejected));
        }

        [Test]
        public void AReliableBufferThatCannotBeDrainedFaultsInsteadOfPublishing()
        {
            // The step plan omits the consuming stage, so the undrained lane's row can never be consumed.
            UnityWorldHost host = CreateWorld(MessageFixtureRegistration.CreateWithoutConsumer());
            WorldMessagePlane plane = Plane(host);

            host.RequestWake(1U);
            WorldPumpResult pump = host.PumpFrame(1_000_000UL);

            Assert.That(pump.Advance, Is.Not.Null);
            Assert.That(pump.Advance!.Accepted, Is.False);
            Assert.That(pump.Advance.Outcome, Is.EqualTo(Outcome.Faulted));
            Assert.That(host.Lifecycle, Is.EqualTo(WorldLifecycleState.Faulted));
            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.Zero), "No step advanced, no image published (P-031, P-043).");
            Assert.That(host.Publications.PublishedCount, Is.EqualTo(1), "Only the initial assembly image exists.");
            Assert.That(
                plane.ReadEvents(new EventCursor(host.World, EventSequence.Zero), 8).Events.Count,
                Is.EqualTo(0),
                "Staged events of a step that never committed are not observable (P-044).");

            MessageProbeState state = ReadProbe(host);
            Assert.That(state.Applied, Is.EqualTo(2), "The owner did write before the commit failed; fail-stop does not roll that back (P-031).");
        }

        [Test]
        public void ShuffledProducerOrderCommitsTheSameResult()
        {
            UnityWorldHost forward = CreateWorld(MessageFixtureRegistration.Create());
            UnityWorldHost reversed = CreateWorld(MessageFixtureRegistration.Create(reverseProducerOrder: true));

            forward.RequestWake(1U);
            reversed.RequestWake(1U);
            forward.PumpFrame(1_000_000UL);
            reversed.PumpFrame(1_000_000UL);

            MessageProbeState first = ReadProbe(forward);
            MessageProbeState second = ReadProbe(reversed);

            Assert.That(first.DrainedRows, Is.EqualTo(2));
            Assert.That(second.DrainedRows, Is.EqualTo(2));
            Assert.That(second.MergeKey, Is.EqualTo(first.MergeKey), "The merge order comes from declared keys, not dispatch order (P-008).");
            Assert.That(second.LastValue, Is.EqualTo(first.LastValue));
            Assert.That(second.Applied, Is.EqualTo(first.Applied));
            Assert.That(second.Committed, Is.EqualTo(first.Committed));

            IReadOnlyList<CommittedEvent> forwardEvents = Plane(forward).ReadEvents(new EventCursor(forward.World, EventSequence.Zero), 8).Events;
            IReadOnlyList<CommittedEvent> reversedEvents = Plane(reversed).ReadEvents(new EventCursor(reversed.World, EventSequence.Zero), 8).Events;

            Assert.That(reversedEvents.Count, Is.EqualTo(forwardEvents.Count));
            for (int i = 0; i < forwardEvents.Count; i++)
            {
                Assert.That(reversedEvents[i].Cursor.Sequence, Is.EqualTo(forwardEvents[i].Cursor.Sequence), "Event identity order is canonical.");
                Assert.That(reversedEvents[i].Step, Is.EqualTo(forwardEvents[i].Step));
                Assert.That(reversedEvents[i].Schema, Is.EqualTo(forwardEvents[i].Schema));

                IReadOnlyList<byte> reversedPayload = reversedEvents[i].Payload.Bytes;
                IReadOnlyList<byte> forwardPayload = forwardEvents[i].Payload.Bytes;
                Assert.That(reversedPayload.Count, Is.EqualTo(forwardPayload.Count));
                for (int b = 0; b < forwardPayload.Count; b++)
                {
                    Assert.That(
                        reversedPayload[b],
                        Is.EqualTo(forwardPayload[b]),
                        "Committed event order and payload must be identical across the two producer orderings (P-008).");
                }
            }
        }

        [Test]
        public void ValidatedPartitionsExecuteWithRealJobsSafely()
        {
            // The pure authority module accepts the two declared partitions before the world runs (P-034, P-040).
            OwnershipReport authority = OwnerAuthorityValidator.Validate(
                new OwnerAuthorityDeclaration(
                    new List<WriterDeclaration>
                    {
                        new WriterDeclaration(
                            MessageFixtureKeys.ProduceStage,
                            MessageFixtureKeys.ProducerA,
                            MessageFixtureKeys.ProbeOwner,
                            PartitionedAccess(MessageFixtureKeys.State, OwnershipFixturePartition(1UL)),
                            SystemMultiplicity.PerPartition,
                            null,
                            null),
                        new WriterDeclaration(
                            MessageFixtureKeys.ProduceStage,
                            MessageFixtureKeys.ProducerB,
                            MessageFixtureKeys.ProbeOwner,
                            PartitionedAccess(MessageFixtureKeys.State, OwnershipFixturePartition(2UL)),
                            SystemMultiplicity.PerPartition,
                            null,
                            null),
                    },
                    null,
                    null,
                    new[] { MessageFixtureKeys.ProduceStage, MessageFixtureKeys.OwnerStage, MessageFixtureKeys.ConsumerStage }));

            Assert.That(authority.IsValid, Is.True, authority.Describe());
            Assert.That(authority.Map.PartitionCount, Is.EqualTo(2));

            // And the same declaration runs in a real world: each producer schedules a real Burst job, every handle
            // completes inside the step fence, and the consumer still sees a drained lane (P-041, P-043).
            UnityWorldHost host = CreateWorld(MessageFixtureRegistration.Create());
            host.RequestWake(1U);
            host.PumpFrame(1_000_000UL);

            Assert.That(host.Driver.CommittedStepCount, Is.EqualTo(1));
            Assert.That(host.Ledger.OutstandingJobCount, Is.EqualTo(0), "Every scheduled producer job completed before the commit (P-041).");
            Assert.That(host.Ledger.JobCount, Is.GreaterThanOrEqualTo(2), "Both producers really scheduled work.");
            Assert.That(host.Ledger.QuarantinedJobCount, Is.EqualTo(0), "No job had to be quarantined.");
            Assert.That(ReadProbe(host).NativePayloadBytes, Is.EqualTo(2 * MessageFixtureKeys.PayloadBytes), "The owner read both native jobs' bytes.");
            Assert.That(host.Lifecycle, Is.EqualTo(WorldLifecycleState.Running));
        }

        [Test]
        public void CommittedEventsExposeOnlyAtPublicationAndRetentionReportsAGap()
        {
            UnityWorldHost host = CreateWorld(MessageFixtureRegistration.Create());
            WorldMessagePlane plane = Plane(host);

            Submit(host, 1UL, 5);
            host.PumpFrame(1_000_000UL);

            EventCursor fromStart = new EventCursor(host.World, EventSequence.Zero);
            CommittedEventPage first = plane.ReadEvents(fromStart, 1);
            Assert.That(first.Outcome, Is.EqualTo(CursorOutcome.Ok));
            Assert.That(first.Events.Count, Is.EqualTo(1));

            CommittedEventPage second = plane.ReadEvents(first.NextCursor, 8);
            Assert.That(second.Events.Count, Is.EqualTo(2), "The remainder of the step's events follows the same cursor chain.");
            Assert.That(plane.Events.Read(first.NextCursor, 1).Events.Count, Is.EqualTo(1), "A reader re-reading the same identity sees a redelivery, not a new fact.");
            Assert.That(plane.Output.PublishedCount, Is.EqualTo(3), "Only published events are counted.");

            // A cursor beyond the last published event cannot be answered with a false continuation (P-045).
            CommittedEventPage tooFar = plane.ReadEvents(new EventCursor(host.World, new EventSequence(10_000UL)), 8);
            Assert.That(tooFar.Outcome, Is.EqualTo(CursorOutcome.CursorExpired));
            Assert.That(tooFar.Events.Count, Is.EqualTo(0));

            WorldId foreign = new WorldId(new Id128(SessionSalt, 0xFFFFUL));
            CommittedEventPage otherWorld = plane.ReadEvents(new EventCursor(foreign, EventSequence.Zero), 8);
            Assert.That(otherWorld.Outcome, Is.EqualTo(CursorOutcome.CursorExpired));
        }

        [Test]
        public void ABoundedEventStreamKeepsItsRetentionBound()
        {
            // A plane whose retention is two events: the third publication drops the oldest and reports the gap.
            WorldId session = NextSession();
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
                    capacity: 4,
                    allowsRebind: false),
            };

            var buffers = new List<MessageBufferDescriptor>
            {
                new MessageBufferDescriptor(
                    MessageFixtureKeys.CommandBuffer,
                    MessageFixtureKeys.Command,
                    new[] { MessageFixtureKeys.HostIngressProducer },
                    MessageFixtureKeys.ProbeOwner,
                    MessageFixtureKeys.OwnerStage,
                    MessageFixtureKeys.OwnerStage,
                    MessageFixtureKeys.HostIngressProducer,
                    BufferLifetime.Step,
                    capacity: 4,
                    byteCapacity: 128,
                    BufferOverflowPolicy.RejectBeforeMutation,
                    BufferCancellationPolicy.Drain),
            };

            var registration = new MessagePlaneRegistration(routes, buffers, null, maxPendingRequests: 4, maxRetainedResults: 8, maxRetainedEvents: 2, maxEventsPerStep: 4, nextStepCapacity: 2);
            var plane = new WorldMessagePlane(session, registration, MessageFixtureRegistration.Readers());

            plane.ConfirmPublished(new[]
            {
                Event(host: session, sequence: 1UL),
            });
            plane.ConfirmPublished(new[]
            {
                Event(session, 2UL),
                Event(session, 3UL),
            });

            Assert.That(plane.EventStore.Count, Is.EqualTo(2), "Retention is bounded by count (P-045).");
            Assert.That(plane.LastEventSequence, Is.EqualTo(new EventSequence(3UL)));
            Assert.That(plane.EventStore.DroppedCount, Is.EqualTo(1), "The drop is counted, never silent.");

            CommittedEventPage expired = plane.ReadEvents(new EventCursor(session, EventSequence.Zero), 8);
            Assert.That(expired.Outcome, Is.EqualTo(CursorOutcome.CursorExpired), "A cursor behind retention must be told, not silently skipped.");
            Assert.That(expired.NextCursor.Sequence, Is.EqualTo(new EventSequence(2UL)), "The gap report names where retention now starts.");

            // A cursor immediately below the first retained event is still continuous: the reader has seen event 1.
            CommittedEventPage continuous = plane.ReadEvents(new EventCursor(session, EventSequence.First), 8);
            Assert.That(continuous.Outcome, Is.EqualTo(CursorOutcome.Ok));
            Assert.That(continuous.Events.Count, Is.EqualTo(2));
            Assert.That(continuous.Events[0].Cursor.Sequence, Is.EqualTo(new EventSequence(2UL)));

            plane.Dispose();
        }

        private static CommittedEvent Event(WorldId host, ulong sequence)
            => new CommittedEvent(
                new EventCursor(host, new EventSequence(sequence)),
                MessageFixtureKeys.Event,
                AssemblyEpoch.First,
                LogicalStepId.Zero,
                default(OperationId),
                new FrozenPayload(new byte[] { (byte)sequence }));

        private static AccessSet PartitionedAccess(SchemaRef schema, Id128 partition)
            => new AccessSet(new[] { new AccessDeclaration(schema, AccessMode.Write, partition) });

        private static Id128 OwnershipFixturePartition(ulong ordinal)
            => new Id128(0x4743303037504152UL, ordinal);

        private static WorldId NextSession()
        {
            sessionSequence++;
            return new WorldId(new Id128(SessionSalt, sessionSequence));
        }

        private static UnityWorldHost CreateWorld(UnityWorldRegistration registration)
        {
            WorldId session = NextSession();
            bool created = UnityWorldRegistry.TryCreate(
                MessageFixtureRegistration.CommandDrivenRequest(session, new OperationId(session, Issuer, 1UL), ContentHash.Empty),
                registration,
                out UnityWorldHost? host,
                out WorldCreateResult result);

            Assert.That(created, Is.True, result.Code + ": " + result.Detail);
            Assert.That(host, Is.Not.Null);
            return host!;
        }

        private static WorldMessagePlane Plane(UnityWorldHost host)
        {
            Assert.That(host.Messages, Is.Not.Null, "the fixture registration declares a message plane");
            return host.Messages!;
        }

        private static CommandEnvelope Envelope(UnityWorldHost host, ulong sequence, int value)
            => new CommandEnvelope(
                new OperationId(host.World, Issuer, sequence),
                MessageFixtureKeys.ProbeRoute,
                MessageFixtureRegistration.ProbeTarget,
                MessageFixtureKeys.Command,
                null,
                new FrozenPayload(ProbePayloads.Command(value, (uint)sequence)));

        private static CommandAdmissionReceipt Submit(UnityWorldHost host, ulong sequence, int value)
            => host.Submit(Envelope(host, sequence, value));

        private static RequestRow Row(WorldMessagePlane plane, OperationId request)
        {
            Assert.That(plane.Requests.TryGet(request, out RequestRow? row), Is.True, "the request row is retained");
            Assert.That(row, Is.Not.Null);
            return row!;
        }

        private static MessageProbeState ReadProbe(UnityWorldHost host)
        {
            EntityManager entityManager = host.EntityWorld.EntityManager;
            using (EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadWrite<MessageProbeState>()))
            {
                Assert.That(query.IsEmpty, Is.False, "the fixture world state is seeded");
                return entityManager.GetComponentData<MessageProbeState>(query.GetSingletonEntity());
            }
        }

        private static ILaneProducerProbe Producer(UnityWorldHost host, FactoryKey key)
        {
            Assert.That(host.Systems.TryResolve(key, out SystemDispatchTarget target), Is.True, key.ToString());
            Assert.That(target.ManagedSystem, Is.Not.Null);
            var producer = target.ManagedSystem as ILaneProducerProbe;
            Assert.That(producer, Is.Not.Null, "the registered producer exposes the fixture probe surface");
            return producer!;
        }
    }
}

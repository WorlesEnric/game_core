// GC-007 pure tests — bounded request/buffer semantics and committed-event output (TEST-013, TEST-014, TEST-022).
//
// Normative sources: 00-core-protocols.md P-007 (bounded retention), P-008 (canonical ordering and determinism),
// P-037 (sealed input, duplicate request keys, observable rejected results), P-041 (deferred playback ordering),
// P-042 (typed requests; admission acceptance is not gameplay success), P-043 (capacity, overflow, drain rules) and
// P-045 (committed events and cursors). These run in-process with no Unity, exactly like GC-005's pure suites; the
// world-level half lives in Packages/com.gamecore.unity.runtime/Tests/Messages.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using NUnit.Framework;

namespace GameCore.Execution.Tests
{
    /// <summary>Bounded buffers, canonical merge and commit-time drain validation (P-041, P-043).</summary>
    [TestFixture]
    public sealed class BoundedMessageBufferTests
    {
        private static readonly WorldId World = new WorldId(new Id128(0x4D53474255464652UL, 1UL));

        private static readonly OwnerId Owner = new OwnerId(new Id128(0x4D53474255464652UL, 0x10UL));

        private static readonly OwnerId OtherOwner = new OwnerId(new Id128(0x4D53474255464652UL, 0x11UL));

        private static readonly SchemaRef Schema = new SchemaRef(new SchemaId(new Id128(0x4D53474255464652UL, 0x20UL)), 1U);

        private static readonly StageId OwnerStage = new StageId(new Id128(0x4D53474255464652UL, 0x30UL));

        private static readonly StageId ConsumerStage = new StageId(new Id128(0x4D53474255464652UL, 0x31UL));

        private static readonly FactoryKey ProducerA = new FactoryKey(new Id128(0x4D53474255464652UL, 0x40UL), 1U);

        private static readonly FactoryKey ProducerB = new FactoryKey(new Id128(0x4D53474255464652UL, 0x41UL), 1U);

        private static readonly FactoryKey Undeclared = new FactoryKey(new Id128(0x4D53474255464652UL, 0x42UL), 1U);

        private static readonly BufferId Buffer = new BufferId(new Id128(0x4D53474255464652UL, 0x50UL));

        private static MessageBufferDescriptor Descriptor(
            int capacity = 2,
            BufferOverflowPolicy overflow = BufferOverflowPolicy.RejectBeforeMutation,
            BufferLifetime lifetime = BufferLifetime.Step,
            BufferCancellationPolicy cancellation = BufferCancellationPolicy.Drain)
            => new MessageBufferDescriptor(
                Buffer,
                Schema,
                new[] { ProducerA, ProducerB },
                Owner,
                OwnerStage,
                ConsumerStage,
                ProducerA,
                lifetime,
                capacity,
                byteCapacity: 8,
                overflow,
                cancellation);

        private static StepMessage Message(FactoryKey producer, uint ordinal, ulong admitted, int originOrdinal)
            => new StepMessage(
                LogicalStepId.Zero,
                AssemblyEpoch.First,
                default(OperationId),
                default(RouteId),
                Owner,
                new TargetId(new Id128(0x4D53474255464652UL, 0x60UL + (ulong)originOrdinal)),
                Schema,
                MessageKind.Request,
                new MessageOrderKey(new AdmissionSequence(admitted), ordinal, new Id128(0x4D53474255464652UL, 0x60UL + (ulong)originOrdinal)),
                producer,
                0,
                2);

        [Test]
        public void AFreshRowIsAcceptedAndMergedInCanonicalOrder()
        {
            var bounded = new BoundedMessageBuffer(Descriptor());
            byte[] payload = { 0x0A, 0x01 };

            Assert.That(bounded.TryAppend(Message(ProducerA, 2U, 1UL, 0), payload, out string first), Is.EqualTo(BufferAppendOutcome.Accepted), first);
            Assert.That(bounded.TryAppend(Message(ProducerB, 1U, 1UL, 1), payload, out string second), Is.EqualTo(BufferAppendOutcome.Accepted), second);

            IReadOnlyList<StepMessage> ordered = bounded.Ordered();

            Assert.That(ordered.Count, Is.EqualTo(2));
            Assert.That(ordered[0].Order.Ordinal, Is.EqualTo(1U), "The declared ordinal decides the merge order, not the append order (P-008).");
            Assert.That(ordered[1].Order.Ordinal, Is.EqualTo(2U));
            Assert.That(bounded.PayloadOf(ordered[0]).Length, Is.EqualTo(2));
        }

        [Test]
        public void AFullReliableBufferRefusesWithoutMutatingItself()
        {
            var bounded = new BoundedMessageBuffer(Descriptor(capacity: 1));
            byte[] payload = { 0x01, 0x02 };

            Assert.That(bounded.TryAppend(Message(ProducerA, 1U, 1UL, 0), payload, out _), Is.EqualTo(BufferAppendOutcome.Accepted));
            int rowsBefore = bounded.RowCount;
            int bytesBefore = bounded.PayloadBytes;

            BufferAppendOutcome refused = bounded.TryAppend(Message(ProducerB, 2U, 1UL, 1), payload, out string detail);

            Assert.That(refused, Is.EqualTo(BufferAppendOutcome.RejectedCapacity), detail);
            Assert.That(bounded.RowCount, Is.EqualTo(rowsBefore), "A refused append leaves the buffer unchanged (P-043).");
            Assert.That(bounded.PayloadBytes, Is.EqualTo(bytesBefore));
            Assert.That(bounded.RejectedCount, Is.EqualTo(1), "The refusal is counted, never a silent drop.");
        }

        [Test]
        public void ALossyBufferDropsAndCountsInsteadOfRefusing()
        {
            var bounded = new BoundedMessageBuffer(Descriptor(capacity: 1, overflow: BufferOverflowPolicy.LossyWithCounters));
            byte[] payload = { 0x01, 0x02 };

            bounded.TryAppend(Message(ProducerA, 1U, 1UL, 0), payload, out _);
            BufferAppendOutcome dropped = bounded.TryAppend(Message(ProducerB, 2U, 1UL, 1), payload, out _);

            Assert.That(dropped, Is.EqualTo(BufferAppendOutcome.DroppedLossy));
            Assert.That(bounded.DroppedCount, Is.EqualTo(1));
            Assert.That(bounded.RowCount, Is.EqualTo(1));
        }

        [Test]
        public void AMessageForAnotherOwnerOrProducerIsRefused()
        {
            var bounded = new BoundedMessageBuffer(Descriptor());
            byte[] payload = { 0x01, 0x02 };

            var wrongOwner = new StepMessage(
                LogicalStepId.Zero,
                AssemblyEpoch.First,
                default(OperationId),
                default(RouteId),
                OtherOwner,
                new TargetId(new Id128(0x4D53474255464652UL, 0x61UL)),
                Schema,
                MessageKind.Request,
                MessageOrderKey.Internal(new Id128(0x4D53474255464652UL, 0x61UL)),
                ProducerA,
                0,
                2);

            Assert.That(bounded.TryAppend(wrongOwner, payload, out string ownerDetail), Is.EqualTo(BufferAppendOutcome.RejectedNotForBuffer), ownerDetail);
            Assert.That(bounded.TryAppend(Message(Undeclared, 1U, 1UL, 0), payload, out string producerDetail), Is.EqualTo(BufferAppendOutcome.RejectedNotForBuffer), producerDetail);
            Assert.That(bounded.RowCount, Is.EqualTo(0));
        }

        [Test]
        public void TheConsumingOwnerTakesTheSealedBatchInCanonicalOrder()
        {
            var bounded = new BoundedMessageBuffer(Descriptor());
            byte[] payload = { 0x01, 0x02 };

            bounded.TryAppend(Message(ProducerB, 5U, 2UL, 1), payload, out _);
            bounded.TryAppend(Message(ProducerA, 1U, 1UL, 0), payload, out _);

            IReadOnlyList<StepMessage> sealedBatch = bounded.ConsumeSealedBatch();

            Assert.That(sealedBatch.Count, Is.EqualTo(2));
            Assert.That(sealedBatch[0].Order.Ordinal, Is.EqualTo(1U));
            Assert.That(sealedBatch[1].Order.Ordinal, Is.EqualTo(5U));
            Assert.That(bounded.RowCount, Is.EqualTo(0), "The owner takes the whole sealed batch (P-037).");
            Assert.That(bounded.WasConsumed, Is.True);
        }

        [Test]
        public void UndrainedReliableRowsFailTheCommitValidation()
        {
            var schedule = new StepMessageSchedule();
            Assert.That(schedule.TryDeclare(Descriptor(), out string failure), Is.True, failure);

            Assert.That(schedule.TryGetBuffer(Buffer, out BoundedMessageBuffer? bounded), Is.True);
            byte[] payload = { 0x01, 0x02 };
            bounded!.TryAppend(Message(ProducerA, 1U, 1UL, 0), payload, out _);

            var producedOnly = new Facts(ProducerA, ranStages: new[] { OwnerStage });
            StepBufferCommitReport unconsumed = schedule.ValidateCommit(producedOnly);

            Assert.That(unconsumed.Succeeded, Is.False, "A reliable step buffer that its consumer never took fails the commit (P-043, O-16).");
            Assert.That(unconsumed.Code, Is.EqualTo(DiagnosticCode.MissingDependency));
            Assert.That(unconsumed.Drains[0].UnconsumedBuffer, Is.EqualTo(Buffer));

            // The consuming stage runs and takes the sealed batch, exactly as an owner does inside its stage.
            IReadOnlyList<StepMessage> taken = bounded.ConsumeSealedBatch();
            Assert.That(taken.Count, Is.EqualTo(1));

            var consumed = new Facts(ProducerA, ranStages: new[] { OwnerStage, ConsumerStage });
            Assert.That(
                schedule.ValidateCommit(consumed).Succeeded,
                Is.True,
                "The same rows pass once the consuming stage really took them (P-043, O-16).");

            var lossyDescriptor = Descriptor(capacity: 2, overflow: BufferOverflowPolicy.LossyWithCounters);
            var lossySchedule = new StepMessageSchedule();
            lossySchedule.TryDeclare(lossyDescriptor, out _);
            lossySchedule.TryGetBuffer(Buffer, out BoundedMessageBuffer? lossy);
            lossy!.TryAppend(Message(ProducerA, 1U, 1UL, 0), payload, out _);
            Assert.That(
                lossySchedule.ValidateCommit(producedOnly).Succeeded,
                Is.True,
                "A declared lossy diagnostic buffer is allowed to expire with its step (P-043).");
        }

        [Test]
        public void ADeclaredLifetimePairAndDuplicateIdentityReject()
        {
            var schedule = new StepMessageSchedule();
            Assert.That(schedule.TryDeclare(Descriptor(), out _), Is.True);
            Assert.That(schedule.TryDeclare(Descriptor(), out string duplicate), Is.False, "One buffer has one contract.");
            Assert.That(duplicate.Length, Is.GreaterThan(0));

            var otherLifetime = Descriptor(lifetime: BufferLifetime.BoundedNextStep);
            Assert.That(
                schedule.TryDeclare(otherLifetime, out string lifetimeFailure),
                Is.False,
                "One owner cannot declare two lifetimes for one schema/stage pair (P-043).");
            Assert.That(lifetimeFailure.Length, Is.GreaterThan(0));
        }

        [Test]
        public void TheInputSealIsMonotonicAndCarriesTheAdmittedPrefix()
        {
            var schedule = new StepMessageSchedule();
            var first = new OperationId(World, new Id128(0x4D53474255464652UL, 0x70UL), 1UL);
            var second = new OperationId(World, new Id128(0x4D53474255464652UL, 0x70UL), 2UL);

            InputSeal seal = schedule.SealStepInput(LogicalStepId.First, AssemblyEpoch.First, new AdmissionSequence(2UL), new[] { first, second });

            Assert.That(seal.AdmittedCommands, Is.EqualTo(2));
            Assert.That(seal.Cutoff, Is.EqualTo(new AdmissionSequence(2UL)));
            Assert.That(schedule.LastAdmittedRequest, Is.EqualTo(second));

            Assert.Throws<InvalidOperationException>(
                () => schedule.SealStepInput(LogicalStepId.First, AssemblyEpoch.First, new AdmissionSequence(1UL), null),
                "A backward cutoff would reorder admitted input (P-037).");
        }

        [Test]
        public void DeferredStructuralPlaybackIsCanonicalAndWaitsForItsProducer()
        {
            var deferred = new BufferId(new Id128(0x4D53474255464652UL, 0x51UL));
            var schedule = new StepMessageSchedule();
            var descriptor = new MessageBufferDescriptor(
                deferred,
                Schema,
                new[] { ProducerA, ProducerB },
                Owner,
                OwnerStage,
                ConsumerStage,
                ProducerA,
                BufferLifetime.Step,
                capacity: 4,
                byteCapacity: 0,
                BufferOverflowPolicy.RejectBeforeMutation,
                BufferCancellationPolicy.Drain);
            Assert.That(schedule.TryDeclare(descriptor, out string failure), Is.True, failure);

            TargetId later = new TargetId(new Id128(0x4D53474255464652UL, 0x91UL));
            TargetId earlier = new TargetId(new Id128(0x4D53474255464652UL, 0x90UL));

            Assert.That(schedule.TryRecordStructural(deferred, ProducerA, later, Schema, true, MessageOrderKey.Internal(later.Value), out string first), Is.True, first);
            Assert.That(schedule.TryRecordStructural(deferred, ProducerB, earlier, Schema, false, MessageOrderKey.Internal(earlier.Value), out string second), Is.True, second);
            Assert.That(schedule.PendingStructuralCount, Is.EqualTo(2));

            IReadOnlyList<DeferredStructuralOperation> waiting = schedule.Playback(new[] { ProducerB });

            Assert.That(waiting.Count, Is.EqualTo(1), "Only the completed producer's operation is played back (P-041).");
            Assert.That(waiting[0].Target, Is.EqualTo(earlier));
            Assert.That(schedule.PendingStructuralCount, Is.EqualTo(1), "The unfinished producer keeps its operation queued.");

            IReadOnlyList<DeferredStructuralOperation> rest = schedule.Playback(new[] { ProducerA });
            Assert.That(rest.Count, Is.EqualTo(1));
            Assert.That(rest[0].Target, Is.EqualTo(later));
            Assert.That(schedule.PendingStructuralCount, Is.EqualTo(0));
        }

        [Test]
        public void ADeclaredDeferredBufferRefusesAnUndeclaredProducerAndOverflow()
        {
            var deferred = new BufferId(new Id128(0x4D53474255464652UL, 0x52UL));
            var schedule = new StepMessageSchedule();
            var descriptor = new MessageBufferDescriptor(
                deferred,
                Schema,
                new[] { ProducerA },
                Owner,
                OwnerStage,
                ConsumerStage,
                ProducerA,
                BufferLifetime.Step,
                capacity: 1,
                byteCapacity: 0,
                BufferOverflowPolicy.RejectBeforeMutation,
                BufferCancellationPolicy.Drain);
            Assert.That(schedule.TryDeclare(descriptor, out _), Is.True);

            TargetId target = new TargetId(new Id128(0x4D53474255464652UL, 0x92UL));
            Assert.That(
                schedule.TryRecordStructural(deferred, ProducerB, target, Schema, true, MessageOrderKey.Internal(target.Value), out string undeclared),
                Is.False);
            Assert.That(undeclared.Length, Is.GreaterThan(0));

            Assert.That(schedule.TryRecordStructural(deferred, ProducerA, target, Schema, true, MessageOrderKey.Internal(target.Value), out _), Is.True);
            Assert.That(
                schedule.TryRecordStructural(deferred, ProducerA, target, Schema, true, MessageOrderKey.Internal(target.Value), out string full),
                Is.False,
                "A deferred buffer at its declared capacity rejects the affected batch before mutation (P-043).");
            Assert.That(full.Length, Is.GreaterThan(0));
        }

        [Test]
        public void AnUnconsumedNextStepQueueCarriesForwardWithTerminalCancellations()
        {
            var nextStep = new BufferId(new Id128(0x4D53474255464652UL, 0x53UL));
            var descriptor = new MessageBufferDescriptor(
                nextStep,
                Schema,
                new[] { ProducerA },
                Owner,
                OwnerStage,
                ConsumerStage,
                ProducerA,
                BufferLifetime.BoundedNextStep,
                capacity: 4,
                byteCapacity: 64,
                BufferOverflowPolicy.RejectBeforeMutation,
                BufferCancellationPolicy.Drain);

            StepMessage live = Message(ProducerA, 1U, 1UL, 0);
            StepMessage dead = Message(ProducerA, 2U, 1UL, 1);

            IReadOnlyList<StepMessage> held = new[] { live, dead };
            var liveness = new OnlyLive(live.Target, live.Route, live.Owner);
            NextStepCarryReport carried = BoundedBufferRules.CarryNextStep(descriptor, held, capacity: 4, liveness);

            Assert.That(carried.Retained.Count, Is.EqualTo(1), "A message whose target is gone is not retained (P-043, P-047).");
            Assert.That(carried.Retained[0].Order.Ordinal, Is.EqualTo(1U));
            Assert.That(carried.Cancelled.Count, Is.EqualTo(1));
            Assert.That(carried.Cancelled[0].Kind, Is.EqualTo(RequestResultKind.Cancelled));
            Assert.That(carried.Cancelled[0].Request, Is.EqualTo(dead.Request));
            Assert.That(carried.Cancelled[0].Reason, Is.EqualTo(DiagnosticCode.Cancelled));

            NextStepCarryReport bounded = BoundedBufferRules.CarryNextStep(descriptor, held, capacity: 1, liveness: AlwaysLiveTargets.Instance);
            Assert.That(bounded.Retained.Count, Is.EqualTo(1), "A bounded next-step queue never grows past its capacity.");
            Assert.That(bounded.DroppedByCapacity, Is.EqualTo(1), "The excess is counted, never silent.");
        }

        private sealed class Facts : IStepDispatchFacts
        {
            private readonly FactoryKey producer;
            private readonly IReadOnlyList<StageId> stages;

            internal Facts(FactoryKey producer, IReadOnlyList<StageId> ranStages)
            {
                this.producer = producer;
                stages = ranStages;
            }

            public bool Ran(FactoryKey candidate) => candidate.RegistrationKey.Equals(producer.RegistrationKey);

            public bool RanStage(StageId stage)
            {
                for (int i = 0; i < stages.Count; i++)
                {
                    if (stages[i].Equals(stage))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private sealed class OnlyLive : IMessageTargetLiveness
        {
            private readonly TargetId target;
            private readonly RouteId route;
            private readonly OwnerId owner;

            internal OnlyLive(TargetId target, RouteId route, OwnerId owner)
            {
                this.target = target;
                this.route = route;
                this.owner = owner;
            }

            public bool IsLive(TargetId candidate, RouteId candidateRoute, OwnerId candidateOwner)
                => candidate.Value.Equals(target.Value)
                    && candidateRoute.Value.Equals(route.Value)
                    && candidateOwner.Equals(owner);

            public bool AllowsRebind(BufferId buffer) => false;
        }
    }

    /// <summary>Request admission, dedup, capacity, terminal results and bounded retention (P-037, P-042, P-050).</summary>
    [TestFixture]
    public sealed class RequestLedgerTests
    {
        private static readonly WorldId World = new WorldId(new Id128(0x4D53475245515545UL, 1UL));

        private static readonly WorldId OtherWorld = new WorldId(new Id128(0x4D53475245515545UL, 2UL));

        private static readonly Id128 Issuer = new Id128(0x4D53475245515545UL, 0x10UL);

        private static readonly OwnerId Owner = new OwnerId(new Id128(0x4D53475245515545UL, 0x20UL));

        private static readonly SchemaRef Schema = new SchemaRef(new SchemaId(new Id128(0x4D53475245515545UL, 0x30UL)), 1U);

        private static readonly TargetId Target = new TargetId(new Id128(0x4D53475245515545UL, 0x40UL));

        private static readonly RouteId Route = new RouteId(new Id128(0x4D53475245515545UL, 0x50UL));

        private static readonly RouteId OtherRoute = new RouteId(new Id128(0x4D53475245515545UL, 0x51UL));

        private static readonly StageId OwnerStage = new StageId(new Id128(0x4D53475245515545UL, 0x60UL));

        private static readonly StageId ConsumerStage = new StageId(new Id128(0x4D53475245515545UL, 0x61UL));

        private static readonly BufferId Buffer = new BufferId(new Id128(0x4D53475245515545UL, 0x70UL));

        private static readonly FactoryKey Producer = new FactoryKey(new Id128(0x4D53475245515545UL, 0x80UL), 1U);

        private static readonly ContentHash Input = ContentHash.Compute(new byte[] { 1, 2, 3 });

        private static CommandRouteTable Routes(bool withOtherRoute = false)
        {
            var table = new CommandRouteTable();
            Assert.That(
                table.TryAdd(new CommandRoute(Route, Owner, Schema, OwnerStage, ConsumerStage, Buffer, Producer, capacity: 4, allowsRebind: false), out string first),
                Is.True,
                first);

            if (withOtherRoute)
            {
                Assert.That(
                    table.TryAdd(new CommandRoute(OtherRoute, Owner, Schema, OwnerStage, ConsumerStage, Buffer, Producer, capacity: 4, allowsRebind: false), out string second),
                    Is.True,
                    second);
            }

            return table;
        }

        private static RequestLedger Ledger(CommandRouteTable routes, int maxPending = 2, int maxRetained = 2)
            => new RequestLedger(routes, maxPending, maxRetained);

        [Test]
        public void AFreshAdmissionIsAcceptedAndNotYetCommitted()
        {
            RequestLedger ledger = Ledger(Routes());
            RequestAdmission admission = ledger.Admit(Request(1UL), Route, Target, Schema, Input, RequestOrigin.External, World, LogicalStepId.Zero, AssemblyEpoch.First);

            Assert.That(admission.Admitted, Is.True);
            Assert.That(admission.Outcome.Kind, Is.EqualTo(RequestResultKind.Accepted), "Admission acceptance is its own result kind (P-042).");
            Assert.That(admission.Outcome.CausalCursor.World.Session.IsDefault, Is.True);
            Assert.That(ledger.PendingCount, Is.EqualTo(1));
            Assert.That(ledger.LastAdmissionSequence, Is.EqualTo(new AdmissionSequence(1UL)));
        }

        [Test]
        public void ADuplicateRequestKeyReturnsItsRecordedResult()
        {
            RequestLedger ledger = Ledger(Routes());
            OperationId request = Request(1UL);
            ledger.Admit(request, Route, Target, Schema, Input, RequestOrigin.External, World, LogicalStepId.Zero, AssemblyEpoch.First);
            ledger.TrySettle(request, RequestResultKind.Committed, DiagnosticCode.None, default(EventCursor), LogicalStepId.First);

            RequestAdmission duplicate = ledger.Admit(request, Route, Target, Schema, Input, RequestOrigin.External, World, LogicalStepId.First, AssemblyEpoch.First);

            Assert.That(duplicate.Kind, Is.EqualTo(RequestAdmissionKind.Retransmission));
            Assert.That(duplicate.Outcome.Kind, Is.EqualTo(RequestResultKind.Committed), "The recorded result is returned, not a second execution (P-037).");
            Assert.That(ledger.DuplicateCount, Is.EqualTo(1));
            Assert.That(ledger.AdmittedCount, Is.EqualTo(1));
        }

        [Test]
        public void AReusedKeyWithDifferentInputIsAnIdempotencyConflict()
        {
            RequestLedger ledger = Ledger(Routes());
            OperationId request = Request(1UL);
            ledger.Admit(request, Route, Target, Schema, Input, RequestOrigin.External, World, LogicalStepId.Zero, AssemblyEpoch.First);

            RequestAdmission conflict = ledger.Admit(
                request,
                Route,
                Target,
                Schema,
                ContentHash.Compute(new byte[] { 9, 9, 9 }),
                RequestOrigin.External,
                World,
                LogicalStepId.Zero,
                AssemblyEpoch.First);

            Assert.That(conflict.Kind, Is.EqualTo(RequestAdmissionKind.IdempotencyConflict));
            Assert.That(conflict.Code, Is.EqualTo(DiagnosticCode.IdempotencyConflict));
            Assert.That(ledger.ConflictCount, Is.EqualTo(1));
            Assert.That(ledger.AdmittedCount, Is.EqualTo(1), "The original row is never overwritten (P-050).");
        }

        [Test]
        public void CapacityBackpressureRecordsNothing()
        {
            RequestLedger ledger = Ledger(Routes(), maxPending: 1);
            ledger.Admit(Request(1UL), Route, Target, Schema, Input, RequestOrigin.External, World, LogicalStepId.Zero, AssemblyEpoch.First);

            RequestAdmission refused = ledger.Admit(Request(2UL), Route, Target, Schema, Input, RequestOrigin.External, World, LogicalStepId.Zero, AssemblyEpoch.First);

            Assert.That(refused.Kind, Is.EqualTo(RequestAdmissionKind.CapacityRejected));
            Assert.That(refused.Code, Is.EqualTo(DiagnosticCode.BudgetExceeded));
            Assert.That(ledger.RowCount, Is.EqualTo(1), "Nothing was recorded for the refused request (P-043).");
            Assert.That(ledger.LastAdmissionSequence, Is.EqualTo(new AdmissionSequence(1UL)), "No sequence was consumed.");
        }

        [Test]
        public void UnknownAndRetiredRoutesAndSchemaMismatchesAreRefused()
        {
            CommandRouteTable routes = Routes(withOtherRoute: true);
            RequestLedger ledger = Ledger(routes);

            RequestAdmission unknown = ledger.Admit(
                Request(1UL),
                new RouteId(new Id128(0x4D53475245515545UL, 0x99UL)),
                Target,
                Schema,
                Input,
                RequestOrigin.External,
                World,
                LogicalStepId.Zero,
                AssemblyEpoch.First);

            Assert.That(unknown.Kind, Is.EqualTo(RequestAdmissionKind.RouteUnknown));

            RequestAdmission mismatch = ledger.Admit(
                Request(2UL),
                Route,
                Target,
                new SchemaRef(Schema.Id, 2U),
                Input,
                RequestOrigin.External,
                World,
                LogicalStepId.Zero,
                AssemblyEpoch.First);

            Assert.That(mismatch.Kind, Is.EqualTo(RequestAdmissionKind.SchemaMismatch));
            Assert.That(mismatch.Code, Is.EqualTo(DiagnosticCode.UnsupportedVersion));

            Assert.That(routes.Retire(OtherRoute), Is.True);
            RequestAdmission retired = ledger.Admit(
                Request(3UL),
                OtherRoute,
                Target,
                Schema,
                Input,
                RequestOrigin.External,
                World,
                LogicalStepId.Zero,
                AssemblyEpoch.First);

            Assert.That(retired.Kind, Is.EqualTo(RequestAdmissionKind.RouteRetired));
            Assert.That(retired.Code, Is.EqualTo(DiagnosticCode.Cancelled), "Retired work is cancelled, never silently lost (P-047).");
        }

        [Test]
        public void ARequestOfAnotherWorldIncarnationIsRefused()
        {
            RequestLedger ledger = Ledger(Routes());
            var foreign = new OperationId(OtherWorld, Issuer, 1UL);

            RequestAdmission refused = ledger.Admit(foreign, Route, Target, Schema, Input, RequestOrigin.External, World, LogicalStepId.Zero, AssemblyEpoch.First);

            Assert.That(refused.Kind, Is.EqualTo(RequestAdmissionKind.StaleReference));
            Assert.That(refused.Code, Is.EqualTo(DiagnosticCode.StaleHandle));
            Assert.That(ledger.RowCount, Is.EqualTo(0));
        }

        [Test]
        public void AnOutOfOrderIssuerSequenceIsRefused()
        {
            RequestLedger ledger = Ledger(Routes());
            ledger.Admit(Request(5UL), Route, Target, Schema, Input, RequestOrigin.External, World, LogicalStepId.Zero, AssemblyEpoch.First);

            RequestAdmission reused = ledger.Admit(Request(4UL), Route, Target, Schema, Input, RequestOrigin.External, World, LogicalStepId.Zero, AssemblyEpoch.First);

            Assert.That(reused.Kind, Is.EqualTo(RequestAdmissionKind.SequenceViolation));
            Assert.That(ledger.SequenceViolationCount, Is.EqualTo(1));
            Assert.That(ledger.RowCount, Is.EqualTo(1));
        }

        [Test]
        public void TerminalResultsDistinguishCommitmentFromRejectionAndCancellation()
        {
            RequestLedger ledger = Ledger(Routes(), maxPending: 4, maxRetained: 8);
            OperationId committed = Request(1UL);
            OperationId rejected = Request(2UL);
            OperationId cancelled = Request(3UL);

            ledger.Admit(committed, Route, Target, Schema, Input, RequestOrigin.External, World, LogicalStepId.Zero, AssemblyEpoch.First);
            ledger.Admit(rejected, Route, Target, Schema, Input, RequestOrigin.External, World, LogicalStepId.Zero, AssemblyEpoch.First);
            ledger.Admit(cancelled, Route, Target, Schema, Input, RequestOrigin.External, World, LogicalStepId.Zero, AssemblyEpoch.First);

            var cursor = new EventCursor(World, EventSequence.First);
            Assert.That(ledger.TrySettle(committed, RequestResultKind.Committed, DiagnosticCode.None, cursor, LogicalStepId.First), Is.True);
            Assert.That(ledger.TrySettle(rejected, RequestResultKind.Rejected, DiagnosticCode.OwnershipConflict, default(EventCursor), LogicalStepId.First), Is.True);
            Assert.That(ledger.TrySettle(cancelled, RequestResultKind.Cancelled, DiagnosticCode.Cancelled, default(EventCursor), LogicalStepId.First), Is.True);

            Assert.That(ledger.CommittedCount, Is.EqualTo(1));
            Assert.That(ledger.RejectedCount, Is.EqualTo(1));
            Assert.That(ledger.CancelledCount, Is.EqualTo(1));
            Assert.That(ledger.PendingCount, Is.EqualTo(0));

            ledger.TryGet(committed, out RequestRow? committedRow);
            Assert.That(committedRow!.Outcome.IsCommitted, Is.True);
            Assert.That(committedRow.Outcome.CausalCursor, Is.EqualTo(cursor));
            Assert.That(committedRow.TerminalStep, Is.EqualTo(LogicalStepId.First));

            ledger.TryGet(rejected, out RequestRow? rejectedRow);
            Assert.That(rejectedRow!.Outcome.IsCommitted, Is.False, "A rejection is terminal but is not a gameplay commitment (P-042).");
            Assert.That(rejectedRow.Outcome.IsTerminal, Is.True);

            Assert.That(ledger.TrySettle(committed, RequestResultKind.Committed, DiagnosticCode.None, cursor, LogicalStepId.First), Is.False, "A request settles once.");
            Assert.Throws<ArgumentException>(() => ledger.TrySettle(committed, RequestResultKind.Accepted, DiagnosticCode.None, default(EventCursor), LogicalStepId.First));
        }

        [Test]
        public void TheCommittedCursorIsAttachedAfterPublication()
        {
            RequestLedger ledger = Ledger(Routes());
            OperationId request = Request(1UL);
            ledger.Admit(request, Route, Target, Schema, Input, RequestOrigin.External, World, LogicalStepId.Zero, AssemblyEpoch.First);
            ledger.TrySettle(request, RequestResultKind.Committed, DiagnosticCode.None, default(EventCursor), LogicalStepId.First);

            var cursor = new EventCursor(World, new EventSequence(7UL));
            Assert.That(ledger.TryUpdateCommittedCursor(request, cursor), Is.True);

            ledger.TryGet(request, out RequestRow? row);
            Assert.That(row!.Outcome.CausalCursor, Is.EqualTo(cursor));

            OperationId rejected = Request(2UL);
            ledger.Admit(rejected, Route, Target, Schema, Input, RequestOrigin.External, World, LogicalStepId.Zero, AssemblyEpoch.First);
            ledger.TrySettle(rejected, RequestResultKind.Rejected, DiagnosticCode.OwnershipConflict, default(EventCursor), LogicalStepId.First);
            Assert.That(
                ledger.TryUpdateCommittedCursor(rejected, cursor),
                Is.False,
                "Only an outcome that really committed may carry a committed cursor (P-042).");
        }

        [Test]
        public void CancellingPendingWorkLeavesSettledRowsUntouched()
        {
            RequestLedger ledger = Ledger(Routes(withOtherRoute: true), maxPending: 4, maxRetained: 8);
            OperationId first = Request(1UL);
            OperationId second = Request(2UL);
            ledger.Admit(first, Route, Target, Schema, Input, RequestOrigin.External, World, LogicalStepId.Zero, AssemblyEpoch.First);
            ledger.Admit(second, Route, Target, Schema, Input, RequestOrigin.External, World, LogicalStepId.Zero, AssemblyEpoch.First);

            Assert.That(ledger.TrySettle(first, RequestResultKind.Committed, DiagnosticCode.None, default(EventCursor), LogicalStepId.First), Is.True);
            Assert.That(ledger.CancelPending(Route, LogicalStepId.First), Is.EqualTo(1), "Only the pending request is cancelled (P-047).");

            ledger.TryGet(first, out RequestRow? committedRow);
            Assert.That(committedRow!.Outcome.Kind, Is.EqualTo(RequestResultKind.Committed), "Cancellation never implies rollback (P-051).");

            ledger.TryGet(second, out RequestRow? cancelledRow);
            Assert.That(cancelledRow!.Outcome.Kind, Is.EqualTo(RequestResultKind.Cancelled));
        }

        [Test]
        public void BoundedResultRetentionTurnsDroppedIdentitiesIntoResultExpired()
        {
            RequestLedger ledger = Ledger(Routes(), maxPending: 4, maxRetained: 1);

            for (ulong sequence = 1UL; sequence <= 3UL; sequence++)
            {
                OperationId request = Request(sequence);
                ledger.Admit(request, Route, Target, Schema, Input, RequestOrigin.External, World, LogicalStepId.Zero, AssemblyEpoch.First);
                ledger.TrySettle(request, RequestResultKind.Rejected, DiagnosticCode.OwnershipConflict, default(EventCursor), LogicalStepId.First);
            }

            Assert.That(ledger.RowCount, Is.EqualTo(1), "Retention is bounded (P-050).");
            Assert.That(ledger.IsExpired(Request(1UL)), Is.True, "Expired is distinct from unknown (05 s5).");

            RequestAdmission expired = ledger.Admit(Request(1UL), Route, Target, Schema, Input, RequestOrigin.External, World, LogicalStepId.Zero, AssemblyEpoch.First);
            Assert.That(expired.Kind, Is.EqualTo(RequestAdmissionKind.SequenceViolation));
            Assert.That(expired.Code, Is.EqualTo(DiagnosticCode.ResultExpired), "An expired identity is never re-executed (P-050).");
        }

        [Test]
        public void PendingRowsAreReportedInCanonicalAdmissionOrder()
        {
            RequestLedger ledger = Ledger(Routes(), maxPending: 4, maxRetained: 8);
            ledger.Admit(new OperationId(World, Issuer, 3UL), Route, Target, Schema, Input, RequestOrigin.External, World, LogicalStepId.Zero, AssemblyEpoch.First);
            ledger.Admit(new OperationId(World, new Id128(Issuer.High, Issuer.Low + 1UL), 1UL), Route, Target, Schema, Input, RequestOrigin.External, World, LogicalStepId.Zero, AssemblyEpoch.First);
            ledger.Admit(new OperationId(World, new Id128(Issuer.High, Issuer.Low + 2UL), 2UL), Route, Target, Schema, Input, RequestOrigin.External, World, LogicalStepId.Zero, AssemblyEpoch.First);

            IReadOnlyList<RequestRow> pending = ledger.PendingRows();

            Assert.That(pending.Count, Is.EqualTo(3));
            Assert.That(pending[0].Request.IssuerSequence, Is.EqualTo(3UL), "The host-assigned admission sequence orders the batch (P-037).");
            Assert.That(pending[2].Request.IssuerSequence, Is.EqualTo(2UL));
        }

        private static OperationId Request(ulong sequence) => new OperationId(World, Issuer, sequence);
    }

    /// <summary>Committed events, bounded retention and cursor behaviour (P-007, P-044, P-045).</summary>
    [TestFixture]
    public sealed class CommittedEventStoreTests
    {
        private static readonly WorldId World = new WorldId(new Id128(0x4D53474556454E54UL, 1UL));

        private static readonly WorldId OtherWorld = new WorldId(new Id128(0x4D53474556454E54UL, 2UL));

        private static readonly SchemaRef Schema = new SchemaRef(new SchemaId(new Id128(0x4D53474556454E54UL, 0x10UL)), 1U);

        private static readonly TargetId Target = new TargetId(new Id128(0x4D53474556454E54UL, 0x20UL));

        [Test]
        public void OneBuildProducesCanonicallyOrderedEventsForTheCommittedStep()
        {
            var collector = new StepOutputCollector(maxEventsPerStep: 4);
            var request = new OperationId(World, new Id128(0x4D53474556454E54UL, 0x30UL), 2UL);

            Assert.That(collector.TryStage(Schema, Target, request, new FrozenPayload(new byte[] { 0x02 }), out string second), Is.True, second);
            Assert.That(collector.TryStage(Schema, Target, default(OperationId), new FrozenPayload(new byte[] { 0x01 }), out string first), Is.True, first);

            IReadOnlyList<CommittedEvent> events = collector.BuildCommittedEvents(
                World,
                AssemblyEpoch.First,
                LogicalStepId.First,
                EventSequence.Zero,
                out EventSequence next);

            Assert.That(events.Count, Is.EqualTo(2));
            Assert.That(events[0].Cursor.Sequence, Is.EqualTo(EventSequence.First));
            Assert.That(events[1].Cursor.Sequence, Is.EqualTo(new EventSequence(2UL)));
            Assert.That(events[1].CausalRequest, Is.EqualTo(request), "The causal request is the strongest canonical event key (P-045).");
            Assert.That(events[0].Step, Is.EqualTo(LogicalStepId.First));
            Assert.That(events[0].Epoch, Is.EqualTo(AssemblyEpoch.First));
            Assert.That(next, Is.EqualTo(new EventSequence(2UL)));
            Assert.That(collector.StagedCount, Is.EqualTo(0), "The staging list is emptied only at the publication boundary.");
        }

        [Test]
        public void ABoundedStepOutputRefusesInsteadOfGrowing()
        {
            var collector = new StepOutputCollector(maxEventsPerStep: 1);
            var payload = new FrozenPayload(new byte[] { 0x01 });

            Assert.That(collector.TryStage(Schema, Target, default(OperationId), payload, out _), Is.True);
            Assert.That(collector.TryStage(Schema, Target, default(OperationId), payload, out string failure), Is.False);
            Assert.That(failure.Length, Is.GreaterThan(0));
            Assert.That(collector.RefusedCount, Is.EqualTo(1));

            collector.Abort();
            Assert.That(collector.StagedCount, Is.EqualTo(0), "A step that never commits drops its staged events (P-031).");
        }

        [Test]
        public void TheStoreReportsGapsInsteadOfFalseContinuity()
        {
            var store = new CommittedEventStore(World, retention: 2);
            store.Publish(new[] { Event(1UL), Event(2UL) });
            store.Publish(new[] { Event(3UL) });

            Assert.That(store.Count, Is.EqualTo(2), "Retention is bounded by count (P-045).");
            Assert.That(store.DroppedCount, Is.EqualTo(1));
            Assert.That(store.FirstRetainedSequence, Is.EqualTo(new EventSequence(2UL)));

            CommittedEventPage gap = store.Read(new EventCursor(World, EventSequence.Zero), 8);
            Assert.That(gap.Outcome, Is.EqualTo(CursorOutcome.CursorExpired));
            Assert.That(gap.Events.Count, Is.EqualTo(0));
            Assert.That(store.ResyncRequiredCount, Is.EqualTo(1), "A lagging reader must resynchronize from a snapshot.");

            CommittedEventPage continuous = store.Read(new EventCursor(World, EventSequence.First), 8);
            Assert.That(continuous.Outcome, Is.EqualTo(CursorOutcome.Ok));
            Assert.That(continuous.Events.Count, Is.EqualTo(2));

            CommittedEventPage beyond = store.Read(new EventCursor(World, new EventSequence(50UL)), 8);
            Assert.That(beyond.Outcome, Is.EqualTo(CursorOutcome.CursorExpired));

            CommittedEventPage foreign = store.Read(new EventCursor(OtherWorld, EventSequence.Zero), 8);
            Assert.That(foreign.Outcome, Is.EqualTo(CursorOutcome.CursorExpired), "A cursor of another incarnation is never answered (P-004).");
        }

        [Test]
        public void ARepeatedPageReadIsCountedAsARedelivery()
        {
            var store = new CommittedEventStore(World, retention: 4);
            store.Publish(new[] { Event(1UL), Event(2UL) });

            EventCursor cursor = new EventCursor(World, EventSequence.Zero);
            CommittedEventPage first = store.Read(cursor, 1);
            CommittedEventPage again = store.Read(cursor, 1);

            Assert.That(first.Events.Count, Is.EqualTo(1));
            Assert.That(again.Events[0].Cursor, Is.EqualTo(first.Events[0].Cursor), "The same identity is returned for the same cursor.");
            Assert.That(store.RedeliveryCount, Is.EqualTo(1), "Delivery is at-least-once within retention (P-045).");
        }

        [Test]
        public void APublicationFromAnotherWorldIncarnationAndANonIncreasingSequenceAreRefused()
        {
            var store = new CommittedEventStore(World, retention: 2);
            store.Publish(new[] { Event(2UL) });

            Assert.Throws<ArgumentException>(() => store.Publish(new[] { Event(3UL, OtherWorld) }));
            Assert.Throws<ArgumentException>(() => store.Publish(new[] { Event(2UL) }));
            Assert.Throws<ArgumentOutOfRangeException>(() => store.Read(new EventCursor(World, EventSequence.Zero), 0));
        }

        private static CommittedEvent Event(ulong sequence, WorldId? world = null)
            => new CommittedEvent(
                new EventCursor(world ?? World, new EventSequence(sequence)),
                Schema,
                AssemblyEpoch.First,
                LogicalStepId.First,
                default(OperationId),
                new FrozenPayload(new byte[] { (byte)sequence }));
    }

    /// <summary>Typed payload ports without reflection (04 s8, P-042).</summary>
    [TestFixture]
    public sealed class CommandPayloadReaderTests
    {
        private static readonly SchemaRef Schema = new SchemaRef(new SchemaId(new Id128(0x4D53475041594C44UL, 1UL)), 1U);

        private sealed class IntReader : ICommandPayloadReader<int>
        {
            public SchemaRef Schema { get; }

            internal IntReader(SchemaRef schema)
            {
                Schema = schema;
            }

            public int Read(IReadOnlyList<byte> payload) => payload[0];
        }

        [Test]
        public void ARegisteredReaderDecodesItsSchemaAndAMissIsReported()
        {
            var readers = new CommandPayloadReaders();
            Assert.That(readers.TryBind(new IntReader(Schema), out string failure), Is.True, failure);
            Assert.That(readers.TryBind(new IntReader(Schema), out string duplicate), Is.False);
            Assert.That(duplicate.Length, Is.GreaterThan(0));

            Assert.That(readers.CanRead(Schema), Is.True);
            Assert.That(
                readers.TryRead<int>(Schema, new byte[] { 0x2A }, out int value, out string detail),
                Is.EqualTo(PayloadDecodeOutcome.Decoded),
                detail);
            Assert.That(value, Is.EqualTo(0x2A));

            Assert.That(
                readers.TryRead<int>(new SchemaRef(Schema.Id, 2U), new byte[] { 0x01 }, out _, out string version),
                Is.EqualTo(PayloadDecodeOutcome.VersionMismatch),
                "A reader decodes exactly its declared version (P-006).");

            Assert.That(
                readers.TryRead<int>(
                    new SchemaRef(new SchemaId(new Id128(0x4D53475041594C44UL, 2UL)), 1U),
                    new byte[] { 0x01 },
                    out _,
                    out string missing),
                Is.EqualTo(PayloadDecodeOutcome.NoReader),
                "A missing reader is reported, never substituted reflectively (04 s8).");

            Assert.That(
                readers.TryRead<int>(Schema, Array.Empty<byte>(), out _, out string malformed),
                Is.EqualTo(PayloadDecodeOutcome.Malformed),
                malformed.Length > 0 ? malformed : "the reader refused the payload");
        }

        [Test]
        public void TheFixturePayloadWriterRoundTripsThroughThePayloadReader()
        {
            byte[] payload = new PayloadWriter().WriteInt32(0x1234).WriteInt32(7).ToArray();
            var reader = new PayloadReader(payload);

            Assert.That(reader.ReadInt32(0), Is.EqualTo(0x1234));
            Assert.That(reader.ReadInt32(4), Is.EqualTo(7));
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadInt32(5));
        }
    }
}

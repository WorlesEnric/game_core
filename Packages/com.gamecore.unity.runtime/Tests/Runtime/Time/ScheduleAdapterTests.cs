// GameCore.Unity.Runtime EditMode tests - the compiled schedule installed as the guarded ordered dispatch table
// (GC-009, TEST-012 and TEST-022 subsets).
//
// These assertions join the Planning compiler to GC-005's dispatcher on a real world: the compiled topological
// order becomes the executed order, the declared buffers become native resource slots with playback bindings, and
// a schedule the adapter cannot dispatch honestly is refused with a witness instead of being installed. They also
// assert the neutrality claim TEST-021 depends on: a card/narrative-shaped schedule needs no combat, physics or
// animation stage, and an empty schedule installs an empty table.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Execution.Time;
using GameCore.Planning.Scheduling;
using GameCore.Unity.Runtime.Time;
using NUnit.Framework;

namespace GameCore.Unity.Runtime.Tests.Time
{
    [TestFixture]
    public sealed class ScheduleDispatchAdapterTests
    {
        private const ulong SessionSalt = 0x4743534348454455UL;

        private static readonly Id128 Issuer = new Id128(0x4953535545525443UL, 1UL);

        private static ulong sessionSequence;

        [TearDown]
        public void TearDown()
        {
            UnityWorldRegistry.ResetAll();
            TimeFixtureModule.DetachAll();
        }

        private static ScheduleDispatchKindTable Kinds()
            => new ScheduleDispatchKindTable()
                .Add(TimeFixtureKeys.ProduceSystem, SystemDispatchKind.ManagedSystem)
                .Add(TimeFixtureKeys.ConsumeSystem, SystemDispatchKind.ManagedSystem);

        /// <summary>A card/narrative-shaped schedule over the fixture's two systems and one declared buffer.</summary>
        private static ScheduleDeclarations FixtureDeclarations()
        {
            SchemaRef result = new SchemaRef(new SchemaId(new Id128(TimeFixtureKeys.Namespace, 0x9101UL)), 1U);

            var produceStage = new StageSpec(
                TimeFixtureKeys.ProduceStage,
                1U,
                new Id128(TimeFixtureKeys.Namespace, 0x9501UL),
                HostAffinity.ManagedMain,
                null,
                null,
                new AccessSet(new[] { new AccessDeclaration(result, AccessMode.Write, default(Id128)) }),
                null,
                null,
                null,
                null,
                new[]
                {
                    new SystemSpec(
                        TimeFixtureKeys.ProduceSystem,
                        SystemMultiplicity.World,
                        new AccessSet(new[] { new AccessDeclaration(result, AccessMode.Write, default(Id128)) }),
                        null,
                        null,
                        null,
                        null),
                },
                new[] { new BufferPort(TimeFixtureKeys.ResultBuffer, PortDirection.Producer, TimeFixtureKeys.ProduceStage) });

            var consumeStage = new StageSpec(
                TimeFixtureKeys.ConsumeStage,
                1U,
                new Id128(TimeFixtureKeys.Namespace, 0x9501UL),
                HostAffinity.ManagedMain,
                null,
                null,
                new AccessSet(new[] { new AccessDeclaration(result, AccessMode.Read, default(Id128)) }),
                null,
                null,
                null,
                null,
                new[]
                {
                    new SystemSpec(
                        TimeFixtureKeys.ConsumeSystem,
                        SystemMultiplicity.World,
                        new AccessSet(new[] { new AccessDeclaration(result, AccessMode.Read, default(Id128)) }),
                        null,
                        null,
                        null,
                        null),
                },
                new[] { new BufferPort(TimeFixtureKeys.ResultBuffer, PortDirection.Consumer, TimeFixtureKeys.ConsumeStage) });

            var stages = new List<StageSpec> { produceStage, consumeStage };
            var buffers = new List<BufferSpec>
            {
                new BufferSpec(
                    TimeFixtureKeys.ResultBuffer,
                    result,
                    new[] { TimeFixtureKeys.ProduceSystem },
                    TimeFixtureKeys.ProduceStage,
                    TimeFixtureKeys.ConsumeStage,
                    new FactoryKey(new Id128(TimeFixtureKeys.Namespace, 0x9601UL), 1U),
                    BufferLifetime.Stage,
                    16,
                    BufferOverflowPolicy.RejectBeforeMutation,
                    BufferCancellationPolicy.Drain),
            };

            return new ScheduleDeclarations(stages, buffers);
        }

        private static CompiledSchedule Compile(ScheduleDeclarations declarations)
        {
            ScheduleCompilation compilation = ScheduleCompiler.Compile(declarations);
            Assert.That(compilation.Succeeded, Is.True, compilation.Explain());
            return compilation.Schedule!;
        }

        // ---------------------------------------------------------------- adaptation

        [Test]
        public void ACompiledScheduleBecomesAnOrderedDispatchTableWithNativeResourceSlots()
        {
            CompiledSchedule schedule = Compile(FixtureDeclarations());
            ScheduleAdaptation adaptation = CompiledScheduleAdapter.Adapt(schedule, Kinds());

            Assert.That(adaptation.Succeeded, Is.True, adaptation.Explain());
            Assert.That(adaptation.Witnesses.Count, Is.EqualTo(0));
            Assert.That(adaptation.StepPlan, Is.Not.Null);
            Assert.That(adaptation.StepPlan!.Entries.Count, Is.EqualTo(2));
            Assert.That(adaptation.StepPlan.Entries[0].SystemKey, Is.EqualTo(TimeFixtureKeys.ProduceSystem));
            Assert.That(adaptation.StepPlan.Entries[1].SystemKey, Is.EqualTo(TimeFixtureKeys.ConsumeSystem));
            Assert.That(adaptation.StepPlan.Entries[1].PredecessorStages, Is.EqualTo(new[] { 0 }));
            Assert.That(adaptation.StepPlan.StageCount, Is.EqualTo(2));
            Assert.That(
                adaptation.StepPlan.TryValidate(out DiagnosticCode code, out string detail),
                Is.True,
                code + ": " + detail);

            Assert.That(adaptation.Stages.Count, Is.EqualTo(2));
            Assert.That(adaptation.Stages[1].PredecessorStages, Is.EqualTo(new[] { 0 }));

            // The declared buffer became a native resource slot with a playback binding (P-041, P-043).
            Assert.That(adaptation.Buffers.Count, Is.EqualTo(1));
            Assert.That(adaptation.Buffers[0].Slot, Is.EqualTo(0));
            Assert.That(adaptation.Buffers[0].Buffer, Is.EqualTo(TimeFixtureKeys.ResultBuffer));
            Assert.That(adaptation.Buffers[0].OwnerStageIndex, Is.EqualTo(0));
            Assert.That(adaptation.Buffers[0].ConsumerStageIndex, Is.EqualTo(1));
            Assert.That(adaptation.Buffers[0].ProducerStageIndexes, Is.EqualTo(new[] { 0 }));
            Assert.That(adaptation.NativeTable, Is.Not.Null);
            Assert.That(adaptation.NativeTable!.SlotCount, Is.EqualTo(1));
        }

        [Test]
        public void TheAdapterUsesTheCompiledOrderAndNotTheDeclarationOrder()
        {
            ScheduleDeclarations declarations = FixtureDeclarations();
            CompiledSchedule forward = Compile(declarations);
            CompiledSchedule reversed = Compile(new ScheduleDeclarations(
                new List<StageSpec> { declarations.Stages[1], declarations.Stages[0] },
                declarations.Buffers));

            Assert.That(reversed.Hash, Is.EqualTo(forward.Hash), "the compiled order is declaration-order independent");

            ScheduleAdaptation first = CompiledScheduleAdapter.Adapt(forward, Kinds());
            ScheduleAdaptation second = CompiledScheduleAdapter.Adapt(reversed, Kinds());

            Assert.That(second.Succeeded, Is.True, second.Explain());
            Assert.That(second.StepPlan!.Entries[0].SystemKey, Is.EqualTo(first.StepPlan!.Entries[0].SystemKey),
                "both adaptations install the compiled order, never the order they were handed");
        }

        [Test]
        public void AnEmptyScheduleInstallsAnEmptyValidTable()
        {
            ScheduleAdaptation adaptation = CompiledScheduleAdapter.Adapt(Compile(ScheduleDeclarations.Empty), Kinds());

            Assert.That(adaptation.Succeeded, Is.True, adaptation.Explain());
            Assert.That(adaptation.StepPlan!.IsEmpty, Is.True, "the kernel mandates no stage (P-001, P-059)");
            Assert.That(adaptation.Stages.Count, Is.EqualTo(0));
            Assert.That(adaptation.Buffers.Count, Is.EqualTo(0));
            Assert.That(adaptation.NativeTable!.SlotCount, Is.EqualTo(0));
        }

        [Test]
        public void AGeneratedRegistrationGapIsRefusedInsteadOfSkippingTheEntry()
        {
            CompiledSchedule schedule = Compile(FixtureDeclarations());
            var kinds = new ScheduleDispatchKindTable()
                .Add(TimeFixtureKeys.ProduceSystem, SystemDispatchKind.ManagedSystem);

            ScheduleAdaptation adaptation = CompiledScheduleAdapter.Adapt(schedule, kinds);

            Assert.That(adaptation.Succeeded, Is.False);
            Assert.That(adaptation.Code, Is.EqualTo(DiagnosticCode.MissingDependency));
            Assert.That(adaptation.Witnesses[0].System, Is.EqualTo(TimeFixtureKeys.ConsumeSystem));
            Assert.That(adaptation.StepPlan, Is.Null, "a refused adaptation exposes no partial table");
        }

        [Test]
        public void APlaybackPointOutsideTheConsumersPredecessorsIsRefused()
        {
            // Hand-built schedule: two stages in canonical order with no edge between them, so the producing stage
            // (index 0) is in range but is neither a declared predecessor of the consumer (index 1) nor reachable
            // through compiled edges. The adapter must refuse it rather than install a table where the consumer could
            // read before its producer finished (P-040, P-041).
            var produceStage = new ScheduleStage(
                0,
                TimeFixtureKeys.ProduceStage,
                1U,
                new Id128(TimeFixtureKeys.Namespace, 0x9502UL),
                HostAffinity.ManagedMain,
                new AccessSet(null),
                null,
                null,
                null,
                null,
                null);

            var consumeStage = new ScheduleStage(
                1,
                TimeFixtureKeys.ConsumeStage,
                1U,
                new Id128(TimeFixtureKeys.Namespace, 0x9502UL),
                HostAffinity.ManagedMain,
                new AccessSet(null),
                null,
                null,
                null,
                new[]
                {
                    new ScheduleEntry(
                        0,
                        1,
                        TimeFixtureKeys.ConsumeStage,
                        TimeFixtureKeys.ConsumeSystem,
                        SystemMultiplicity.World,
                        new AccessSet(null),
                        null,
                        null),
                },
                null);

            var point = new SchedulePlaybackPoint(
                0,
                TimeFixtureKeys.ResultBuffer,
                new SchemaRef(new SchemaId(new Id128(TimeFixtureKeys.Namespace, 0x9101UL)), 1U),
                default(FactoryKey),
                BufferLifetime.Stage,
                BufferOverflowPolicy.RejectBeforeMutation,
                BufferCancellationPolicy.Drain,
                16,
                TimeFixtureKeys.ProduceStage,
                0,
                TimeFixtureKeys.ConsumeStage,
                1,
                new[] { 0 },
                new[] { TimeFixtureKeys.ProduceSystem },
                new[] { TimeFixtureKeys.ConsumeSystem });

            var schedule = new CompiledSchedule(
                new[] { produceStage, consumeStage },
                consumeStage.Systems,
                null,
                new[] { point },
                new[]
                {
                    new BufferBinding(
                        TimeFixtureKeys.ResultBuffer,
                        new[] { TimeFixtureKeys.ProduceSystem },
                        TimeFixtureKeys.ConsumeStage),
                },
                new ExecutionPlan(
                    new[]
                    {
                        PlanNode.ForStage(TimeFixtureKeys.ProduceStage),
                        PlanNode.ForStage(TimeFixtureKeys.ConsumeStage),
                    },
                    null,
                    ContentHash.Empty));

            ScheduleAdaptation adaptation = CompiledScheduleAdapter.Adapt(schedule, Kinds());

            Assert.That(adaptation.Succeeded, Is.False);
            Assert.That(adaptation.Code, Is.EqualTo(DiagnosticCode.AmbiguousOrder), adaptation.Explain());
            Assert.That(adaptation.Witnesses[0].Buffer, Is.EqualTo(TimeFixtureKeys.ResultBuffer));
            Assert.That(adaptation.Witnesses[0].Stage, Is.EqualTo(TimeFixtureKeys.ConsumeStage));
        }

        // ---------------------------------------------------------------- execution through the real world

        [Test]
        public void TheCompiledScheduleDrivesTheRealStepGroupInItsCompiledOrder()
        {
            ScheduleAdaptation adaptation = CompiledScheduleAdapter.Adapt(Compile(FixtureDeclarations()), Kinds());
            Assert.That(adaptation.Succeeded, Is.True, adaptation.Explain());

            sessionSequence++;
            var world = new WorldId(new Id128(SessionSalt, sessionSequence));
            var operation = new OperationId(world, Issuer, 1UL);

            // The same fixture systems, but the installed table is the one the compiler produced. The adapter selects
            // exactly the generated registrations its entries name, so the unused output system is not registered.
            var registration = new UnityWorldRegistration(
                "GameCoreCompiledScheduleWorld",
                adaptation.Stages,
                CompiledScheduleAdapter.Registrations(adaptation, TimeFixtureRegistration.Systems()),
                GuardedDispatchPlan.Empty,
                adaptation.StepPlan!,
                GuardedDispatchPlan.Empty,
                entityWorld => TimeFixtureWorldState.Seed(entityWorld));

            bool created = UnityWorldRegistry.TryCreate(
                TimeFixtureRegistration.CommandDrivenRequest(world, operation),
                registration,
                out UnityWorldHost? host,
                out WorldCreateResult result);

            Assert.That(created, Is.True, result.Code + ": " + result.Detail);
            TimeFixtureModule module = TimeFixtureModule.Attach(host!.EntityWorld, host, 0, 0, 1);
            module.Time = new WorldTimeDriver(host, new StepInputCutoff(8, 16), new PluginClockRegistry(4), 1U);
            module.Time.AdoptResourceTable(module.Native);

            var requestKey = new Id128(TimeFixtureKeys.Namespace, 0x9701UL);
            var requestSchema = new SchemaRef(new SchemaId(new Id128(TimeFixtureKeys.Namespace, 0x9702UL)), 1U);
            Assert.That(module.Time.TryAdmitCommand(requestKey, requestSchema, out _, out _), Is.True);

            TimeFrameReport frame = module.Time.PumpFrame(1_000_000UL);

            Assert.That(frame.StepsCommitted, Is.EqualTo(1UL));
            Assert.That(
                TimeFixtureWorldState.TryRead(host.EntityWorld.EntityManager, out TimeFixtureTrail trail),
                Is.True);
            Assert.That(trail.ProduceCount, Is.EqualTo(1));
            Assert.That(trail.ConsumeCount, Is.EqualTo(1), "the compiled table dispatched both stages in order");
            Assert.That(trail.ConsumedValue, Is.EqualTo(101));
            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.First));
            Assert.That(host.StepGroup.InstalledPlan.Entries.Count, Is.EqualTo(2));
            Assert.That(host.Driver.LastDrain.Succeeded, Is.True);
            Assert.That(host.Driver.IsFaulted, Is.False);
        }

        [Test]
        public void NoCompiledScheduleRequiresACombatPhysicsOrAnimationStage()
        {
            CompiledSchedule schedule = Compile(FixtureDeclarations());

            Assert.That(schedule.Stages.Count, Is.EqualTo(2));
            string[] forbidden = { "combat", "physics", "animation", "turn", "actor", "vitality" };
            for (int i = 0; i < schedule.Edges.Count; i++)
            {
                Assert.That(schedule.Edges[i].Required, Is.True);
            }

            ScheduleAdaptation adaptation = CompiledScheduleAdapter.Adapt(schedule, Kinds());
            Assert.That(adaptation.Succeeded, Is.True, adaptation.Explain());
            for (int i = 0; i < adaptation.StepPlan!.Entries.Count; i++)
            {
                string stage = adaptation.StepPlan.Entries[i].Stage.ToString().ToLowerInvariant();
                for (int f = 0; f < forbidden.Length; f++)
                {
                    Assert.That(stage, Does.Not.Contain(forbidden[f]),
                        "no kernel-mandated " + forbidden[f] + " stage exists (P-001, TEST-021)");
                }
            }
        }
    }
}

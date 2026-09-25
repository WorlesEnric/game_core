// GameCore.Planning tests — GC-015: generated slot layouts, the four executors and explicit reset (P-032, P-033).
//
// Every case below asserts on a production verdict (`SlotLayoutGenerator`, `SlotStatePolicySet`, `StatePolicyExecutor`,
// `OwnerTransferValidator`) and, where the requirement is about live storage, on the plan the publication would
// apply: which dispositions it carries and which values the bounded scratch holds. Nothing is asserted from the
// fixture's own bookkeeping.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Planning.Ownership;
using GameCore.Planning.StatePolicies;
using NUnit.Framework;

namespace GameCore.Planning.Tests
{
    [TestFixture]
    public sealed class StatePolicyTests
    {
        private static readonly SlotId ValueSlot = StatePolicyFixtureIds.QuestValueSlot;

        private static readonly SlotId VersionSlot = StatePolicyFixtureIds.QuestVersionSlot;

        private static readonly SlotId TrailSlot = StatePolicyFixtureIds.TrailSlot;
        private static readonly SlotId FactSlot = StatePolicyFixtureIds.FactSlot;

        private static readonly FactoryKey QuestLayout = StatePolicyFixtureIds.Layout(1UL);

        private static readonly FactoryKey TrailLayout = StatePolicyFixtureIds.Layout(2UL);

        // ------------------------------------------------------------------ generated layouts (P-033)

        [Test]
        public void OneLayoutImplementingSeveralSlotsIsASharedComponentWithOneOwner()
        {
            var specs = new List<StateSlotSpec>
            {
                StatePoliciesFixture.Spec(
                    ValueSlot, StatePolicyFixtureIds.QuestOwner, StatePolicyFixtureIds.QuestSchema, QuestLayout,
                    StatePoliciesFixture.QuestFields(0x10UL), LastSupportPolicy.PreserveDormant, default(FactoryKey), default(FactoryKey)),
                StatePoliciesFixture.Spec(
                    VersionSlot, StatePolicyFixtureIds.QuestOwner, StatePolicyFixtureIds.QuestSchema, QuestLayout,
                    StatePoliciesFixture.QuestFields(0x11UL), LastSupportPolicy.PreserveDormant, default(FactoryKey), default(FactoryKey)),
            };

            Assert.That(
                SlotLayoutGenerator.TryGenerate(specs, out SlotLayoutTable? table, out DiagnosticCode code, out string detail),
                Is.True,
                detail);

            Assert.That(code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(table!.PhysicalComponentCount, Is.EqualTo(1), "one layout key is one physical component (P-033)");
            Assert.That(table.SharedComponentCount, Is.EqualTo(1), "the component implements two slots through a field mapping");
            Assert.That(table.SlotsInOrder.Count, Is.EqualTo(2));
            Assert.That(table.TryGet(ValueSlot, out GeneratedSlotLayout? layout), Is.True);
            Assert.That(layout!.Owner, Is.EqualTo(StatePolicyFixtureIds.QuestOwner), "one physical owner per component (P-033)");
            Assert.That(layout.ImplementsSeveralSlots, Is.True);
            Assert.That(layout.Fields.Count, Is.EqualTo(2), "both declared fields are mapped into the one component");
            Assert.That(layout.StorageKind, Is.EqualTo(SlotStorageKind.SharedComponent));
        }

        [Test]
        public void TwoLayoutsOfOneSchemaAreRecordedAsSplitStorage()
        {
            var specs = new List<StateSlotSpec>
            {
                StatePoliciesFixture.Spec(
                    ValueSlot, StatePolicyFixtureIds.QuestOwner, StatePolicyFixtureIds.QuestSchema, QuestLayout,
                    StatePoliciesFixture.QuestFields(0x10UL), LastSupportPolicy.PreserveDormant, default(FactoryKey), default(FactoryKey)),
                StatePoliciesFixture.Spec(
                    VersionSlot, StatePolicyFixtureIds.QuestOwner, StatePolicyFixtureIds.QuestSchema, TrailLayout,
                    StatePoliciesFixture.QuestFields(0x11UL), LastSupportPolicy.PreserveDormant, default(FactoryKey), default(FactoryKey)),
            };

            Assert.That(
                SlotLayoutGenerator.TryGenerate(specs, out SlotLayoutTable? table, out DiagnosticCode _, out string detail),
                Is.True,
                detail);

            Assert.That(table!.PhysicalComponentCount, Is.EqualTo(2));
            Assert.That(table.SplitStorageCount, Is.EqualTo(2), "two storages of one schema are the declared split (P-033)");
        }

        [Test]
        public void TwoOwnersOfOnePhysicalLayoutAreRejected()
        {
            var specs = new List<StateSlotSpec>
            {
                StatePoliciesFixture.Spec(
                    ValueSlot, StatePolicyFixtureIds.QuestOwner, StatePolicyFixtureIds.QuestSchema, QuestLayout,
                    StatePoliciesFixture.QuestFields(0x10UL), LastSupportPolicy.PreserveDormant, default(FactoryKey), default(FactoryKey)),
                StatePoliciesFixture.Spec(
                    VersionSlot, StatePolicyFixtureIds.GateOwner, StatePolicyFixtureIds.QuestSchema, QuestLayout,
                    StatePoliciesFixture.QuestFields(0x11UL), LastSupportPolicy.PreserveDormant, default(FactoryKey), default(FactoryKey)),
            };

            Assert.That(
                SlotLayoutGenerator.TryGenerate(specs, out SlotLayoutTable? table, out DiagnosticCode code, out string detail),
                Is.False);
            Assert.That(table, Is.Null);
            Assert.That(code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(detail, Does.Contain("one component layout has one physical owner"));
        }

        [Test]
        public void TwoSlotsClaimingOneFieldOfOneComponentAreRejected()
        {
            var specs = new List<StateSlotSpec>
            {
                StatePoliciesFixture.Spec(
                    ValueSlot, StatePolicyFixtureIds.QuestOwner, StatePolicyFixtureIds.QuestSchema, QuestLayout,
                    StatePoliciesFixture.QuestFields(0x10UL), LastSupportPolicy.PreserveDormant, default(FactoryKey), default(FactoryKey)),
                // The same layout claims the same field for a second slot: competing initializers (P-033).
                StatePoliciesFixture.Spec(
                    VersionSlot, StatePolicyFixtureIds.QuestOwner, StatePolicyFixtureIds.QuestSchema, QuestLayout,
                    StatePoliciesFixture.QuestFields(0x10UL), LastSupportPolicy.PreserveDormant, default(FactoryKey), default(FactoryKey)),
            };

            Assert.That(
                SlotLayoutGenerator.TryGenerate(specs, out SlotLayoutTable? _, out DiagnosticCode code, out string detail),
                Is.False);
            Assert.That(code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(detail, Does.Contain("competing component initializers"));
        }

        [Test]
        public void ASlotWithoutAGeneratedLayoutKeyIsRefused()
        {
            var specs = new List<StateSlotSpec>
            {
                StatePoliciesFixture.Spec(
                    ValueSlot, StatePolicyFixtureIds.QuestOwner, StatePolicyFixtureIds.QuestSchema, default(FactoryKey),
                    StatePoliciesFixture.QuestFields(0x10UL), LastSupportPolicy.PreserveDormant, default(FactoryKey), default(FactoryKey)),
            };

            Assert.That(
                SlotLayoutGenerator.TryGenerate(specs, out SlotLayoutTable? _, out DiagnosticCode code, out string detail),
                Is.False);
            Assert.That(code, Is.EqualTo(DiagnosticCode.MissingDependency));
            Assert.That(detail, Does.Contain("declares no physical layout key"));
        }

        // ------------------------------------------------------------------ declared policy sets (P-032)

        [Test]
        public void OneSlotDeclaredTwiceWithDifferentOwnersIsRefused()
        {
            var specs = new List<StateSlotSpec>
            {
                StatePoliciesFixture.Spec(
                    ValueSlot, StatePolicyFixtureIds.QuestOwner, StatePolicyFixtureIds.QuestSchema, QuestLayout,
                    StatePoliciesFixture.QuestFields(0x10UL), LastSupportPolicy.PreserveDormant, default(FactoryKey), default(FactoryKey)),
                StatePoliciesFixture.Spec(
                    ValueSlot, StatePolicyFixtureIds.GateOwner, StatePolicyFixtureIds.QuestSchema, QuestLayout,
                    StatePoliciesFixture.QuestFields(0x10UL), LastSupportPolicy.PreserveDormant, default(FactoryKey), default(FactoryKey)),
            };

            Assert.That(
                SlotStatePolicySet.TryBuild(specs, out SlotStatePolicySet? set, out DiagnosticCode code, out string detail),
                Is.False);
            Assert.That(set, Is.Null);
            Assert.That(code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(detail, Does.Contain("one slot has one declaration per catalog revision"));
        }

        [Test]
        public void ALiveKeyWhoseOwnerContradictsTheDeclarationIsAnOwnershipConflict()
        {
            SlotStatePolicySet set = StatePoliciesFixture.Set(Durable(ValueSlot));

            Assert.That(set.TryFind(StatePoliciesFixture.Key(ValueSlot, StatePolicyFixtureIds.QuestOwner), out _, out DiagnosticCode ok, out _), Is.True);
            Assert.That(ok, Is.EqualTo(DiagnosticCode.None));

            Assert.That(
                set.TryFind(StatePoliciesFixture.Key(ValueSlot, StatePolicyFixtureIds.GateOwner), out SlotStatePolicy? conflicting, out DiagnosticCode code, out string detail),
                Is.False);
            Assert.That(conflicting, Is.Null);
            Assert.That(code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(detail, Does.Contain("names owner"));
        }

        // ------------------------------------------------------------------ Preserve (P-020, P-032)

        [Test]
        public void ReconfigurationPreservesTheRuntimeValueAndStagesNothing()
        {
            SlotStatePolicySet set = StatePoliciesFixture.Set(Durable(ValueSlot));
            var registry = new MigrationRegistry(new List<ISlotMigration>
            {
                new PolicyMigration(StatePolicyFixtureIds.QuestMigration, 1U, StatePolicyFixtureIds.QuestSchema.Version, 10, true),
            });
            var scratch = new MigrationScratch(4096UL, 64UL);

            StatePolicyPlan plan = StatePolicyExecutor.Execute(
                set,
                new List<LiveSlotState> { StatePoliciesFixture.Live(ValueSlot, 2U, 41) },
                new List<StatePolicyRequest> { StatePolicyRequest.Preserve(StatePoliciesFixture.Key(ValueSlot, StatePolicyFixtureIds.QuestOwner)) },
                registry,
                new DeclaredSlotMigrationRegistry(set),
                StatePoliciesFixture.InitialValues(),
                scratch);

            Assert.That(plan.Succeeded, Is.True, plan.Detail);
            Assert.That(plan.PreservedCount, Is.EqualTo(1));
            Assert.That(plan.Dispositions.Count, Is.EqualTo(1));
            Assert.That(plan.Dispositions[0].Kind, Is.EqualTo(StateDispositionKind.Retain), "no reset, no rewrite (P-020)");
            Assert.That(plan.Migrations, Is.Empty);
            Assert.That(plan.HasEffectiveDisposition, Is.False, "a preserve plans no live change (P-006)");
            Assert.That(scratch.ReservedSlots, Is.EqualTo(0), "nothing was staged");
        }

        // ------------------------------------------------------------------ PreserveDormant (P-032)

        [Test]
        public void TheLastSupportLossOfDurableStateRetainsItDormant()
        {
            SlotStatePolicySet set = StatePoliciesFixture.Set(Durable(ValueSlot));
            var registry = new MigrationRegistry(null);
            var scratch = new MigrationScratch(4096UL, 64UL);

            StatePolicyPlan plan = StatePolicyExecutor.Execute(
                set,
                new List<LiveSlotState> { StatePoliciesFixture.Live(ValueSlot, 2U, 41) },
                new List<StatePolicyRequest>
                {
                    StatePolicyRequest.PreserveDormant(StatePoliciesFixture.Key(ValueSlot, StatePolicyFixtureIds.QuestOwner)),
                },
                registry,
                new DeclaredSlotMigrationRegistry(set),
                null,
                scratch);

            Assert.That(plan.Succeeded, Is.True, plan.Detail);
            Assert.That(plan.RetainedDormantCount, Is.EqualTo(1));
            Assert.That(plan.Dispositions[0].Kind, Is.EqualTo(StateDispositionKind.RetainDormant));
            Assert.That(plan.Decisions[0].WritesValue, Is.False, "dormant retention writes no value");
            Assert.That(scratch.ReservedSlots, Is.EqualTo(0));
        }

        [Test]
        public void ADormantRetentionOfADisposableSlotIsRefusedWithItsOwnCode()
        {
            // The declared policy is the only legal one: a derived slot may not claim dormant retention (P-032).
            SlotStatePolicy derived = StatePoliciesFixture.Derived(TrailSlot, StatePolicyFixtureIds.QuestOwner, StatePolicyFixtureIds.TrailSchema, TrailLayout);
            SlotStatePolicySet set = StatePoliciesFixture.Set(derived);
            var scratch = new MigrationScratch(4096UL, 64UL);

            StatePolicyPlan plan = StatePolicyExecutor.Execute(
                set,
                new List<LiveSlotState>
                {
                    new LiveSlotState(
                        new StateSlotKey(StatePolicyFixtureIds.Target(1UL), StatePolicyFixtureIds.QuestOwner, TrailSlot), 1U, 5),
                },
                new List<StatePolicyRequest>
                {
                    StatePolicyRequest.PreserveDormant(new StateSlotKey(StatePolicyFixtureIds.Target(1UL), StatePolicyFixtureIds.QuestOwner, TrailSlot)),
                },
                new MigrationRegistry(null),
                new DeclaredSlotMigrationRegistry(set),
                null,
                scratch);

            Assert.That(plan.Succeeded, Is.False);
            Assert.That(plan.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(plan.Dispositions, Is.Empty, "a refused execution plans no disposition (P-029)");
        }

        // ------------------------------------------------------------------ RemoveDerived (P-032, P-033)

        [Test]
        public void RemovingTheLastSupportOfDerivedStateRetractsIt()
        {
            SlotStatePolicy derived = StatePoliciesFixture.Derived(TrailSlot, StatePolicyFixtureIds.QuestOwner, StatePolicyFixtureIds.TrailSchema, TrailLayout);
            SlotStatePolicySet set = StatePoliciesFixture.Set(derived);
            var key = new StateSlotKey(StatePolicyFixtureIds.Target(1UL), StatePolicyFixtureIds.QuestOwner, TrailSlot);

            StatePolicyPlan plan = StatePolicyExecutor.Execute(
                set,
                new List<LiveSlotState> { new LiveSlotState(key, 1U, 5) },
                new List<StatePolicyRequest> { StatePolicyRequest.RemoveDerived(key) },
                new MigrationRegistry(null),
                new DeclaredSlotMigrationRegistry(set),
                null,
                new MigrationScratch(4096UL, 64UL));

            Assert.That(plan.Succeeded, Is.True, plan.Detail);
            Assert.That(plan.RemovedDerivedCount, Is.EqualTo(1));
            Assert.That(plan.Dispositions[0].Kind, Is.EqualTo(StateDispositionKind.Retract));
            Assert.That(plan.HasEffectiveDisposition, Is.True);
        }

        [Test]
        public void RemovingADurableSlotAsDerivedIsRefused()
        {
            SlotStatePolicySet set = StatePoliciesFixture.Set(Durable(ValueSlot));
            var key = StatePoliciesFixture.Key(ValueSlot, StatePolicyFixtureIds.QuestOwner);

            StatePolicyPlan plan = StatePolicyExecutor.Execute(
                set,
                new List<LiveSlotState> { StatePoliciesFixture.Live(ValueSlot, 2U, 41) },
                new List<StatePolicyRequest> { StatePolicyRequest.RemoveDerived(key) },
                new MigrationRegistry(null),
                new DeclaredSlotMigrationRegistry(set),
                null,
                new MigrationScratch(4096UL, 64UL));

            Assert.That(plan.Succeeded, Is.False, "the declared policy is the only legal one (P-032)");
            Assert.That(plan.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
        }

        // ------------------------------------------------------------------ Migrate (P-029, P-032)

        [Test]
        public void ADeclaredVersionChangeRunsOnTheCopyInsideTheScratch()
        {
            SlotStatePolicySet set = StatePoliciesFixture.Set(Durable(ValueSlot));
            var registry = new MigrationRegistry(new List<ISlotMigration>
            {
                new PolicyMigration(StatePolicyFixtureIds.QuestMigration, 1U, StatePolicyFixtureIds.QuestSchema.Version, 10, true),
            });
            var scratch = new MigrationScratch(4096UL, 64UL);

            StatePolicyPlan plan = StatePolicyExecutor.Execute(
                set,
                new List<LiveSlotState> { StatePoliciesFixture.Live(ValueSlot, 1U, 41) },
                null,
                registry,
                new DeclaredSlotMigrationRegistry(set),
                null,
                scratch);

            Assert.That(plan.Succeeded, Is.True, plan.Detail);
            Assert.That(plan.MigratedCount, Is.EqualTo(1));
            Assert.That(plan.Migrations.Count, Is.EqualTo(1));
            Assert.That(plan.Migrations[0].FromVersion, Is.EqualTo(1U));
            Assert.That(plan.Migrations[0].ToVersion, Is.EqualTo(StatePolicyFixtureIds.QuestSchema.Version));
            Assert.That(plan.Migrations[0].MigrationKey, Is.EqualTo(StatePolicyFixtureIds.QuestMigration));
            Assert.That(plan.Dispositions[0].Kind, Is.EqualTo(StateDispositionKind.Migrate));
            Assert.That(scratch.TryRead(plan.Migrations[0].Slot, out int migrated), Is.True);
            Assert.That(migrated, Is.EqualTo(51), "the pure transform ran on the copied value");
            Assert.That(plan.ScratchHighWaterBytes, Is.EqualTo(64UL), "the pass reports its temporary storage (P-022)");
        }

        [Test]
        public void AnUnregisteredMigrationRejectsThePassAndLeavesNoResidue()
        {
            SlotStatePolicySet set = StatePoliciesFixture.Set(Durable(ValueSlot));
            var scratch = new MigrationScratch(4096UL, 64UL);

            StatePolicyPlan plan = StatePolicyExecutor.Execute(
                set,
                new List<LiveSlotState> { StatePoliciesFixture.Live(ValueSlot, 1U, 41) },
                null,
                new MigrationRegistry(null),
                new DeclaredSlotMigrationRegistry(set),
                null,
                scratch);

            Assert.That(plan.Succeeded, Is.False);
            Assert.That(plan.Code, Is.EqualTo(DiagnosticCode.MigrationRequired));
            Assert.That(plan.Dispositions, Is.Empty);
            Assert.That(plan.Migrations, Is.Empty);
            Assert.That(scratch.ReservedSlots, Is.EqualTo(0), "a refused migration stages nothing (P-032)");
        }

        [Test]
        public void AMigrationThatRefusesItsInputReleasesEveryReservationItMade()
        {
            // Two live slots, the second one refused by its own migration: the pass must release the first slot's
            // reservation too, so the old assembly keeps its state and its scratch (P-029).
            SlotStatePolicySet set = StatePoliciesFixture.Set(
                Durable(ValueSlot),
                Durable(VersionSlot));
            var registry = new MigrationRegistry(new List<ISlotMigration>
            {
                new PolicyMigration(StatePolicyFixtureIds.QuestMigration, 1U, StatePolicyFixtureIds.QuestSchema.Version, 10, true),
            });
            var scratch = new MigrationScratch(4096UL, 64UL);

            StatePolicyPlan plan = StatePolicyExecutor.Execute(
                set,
                new List<LiveSlotState>
                {
                    StatePoliciesFixture.Live(ValueSlot, 1U, 41),
                    StatePoliciesFixture.Live(VersionSlot, 1U, -3),
                },
                null,
                registry,
                new DeclaredSlotMigrationRegistry(set),
                null,
                scratch);

            Assert.That(plan.Succeeded, Is.False, "the migration refused its input");
            Assert.That(plan.Dispositions, Is.Empty);
            Assert.That(scratch.ReservedSlots, Is.EqualTo(0));
            Assert.That(
                scratch.TryRead(StatePoliciesFixture.Live(ValueSlot, 1U, 41).Slot, out _),
                Is.False,
                "the released reservation is gone, not leaked");
        }

        [Test]
        public void ScratchBeyondTheConfiguredBudgetRefusesThePass()
        {
            // Temporary storage is a hard, configured limit (P-022): one slot of scratch against a smaller capacity.
            SlotStatePolicySet set = StatePoliciesFixture.Set(Durable(ValueSlot));
            var registry = new MigrationRegistry(new List<ISlotMigration>
            {
                new PolicyMigration(StatePolicyFixtureIds.QuestMigration, 1U, 1U, 10, true),
            });
            var scratch = new MigrationScratch(32UL, 64UL);

            StatePolicyPlan plan = StatePolicyExecutor.Execute(
                set,
                new List<LiveSlotState> { StatePoliciesFixture.Live(ValueSlot, 1U, 41) },
                null,
                registry,
                new DeclaredSlotMigrationRegistry(set),
                null,
                scratch);

            Assert.That(plan.Succeeded, Is.False);
            Assert.That(plan.Code, Is.EqualTo(DiagnosticCode.BudgetExceeded));
            Assert.That(scratch.BudgetExceededCount, Is.EqualTo(1));
            Assert.That(plan.Dispositions, Is.Empty);
        }

        // ------------------------------------------------------------------ Reset (P-032)

        [Test]
        public void ADeclaredResetWithAReasonWritesTheDeclaredInitialValue()
        {
            SlotStatePolicySet set = StatePoliciesFixture.Set(
                Durable(ValueSlot, resetPermitted: true, resetReason: "content repair"));
            var scratch = new MigrationScratch(4096UL, 64UL);

            StatePolicyPlan plan = StatePolicyExecutor.Execute(
                set,
                new List<LiveSlotState> { StatePoliciesFixture.Live(ValueSlot, 2U, 41) },
                new List<StatePolicyRequest>
                {
                    StatePolicyRequest.Reset(StatePoliciesFixture.Key(ValueSlot, StatePolicyFixtureIds.QuestOwner), "content repair"),
                },
                new MigrationRegistry(null),
                new DeclaredSlotMigrationRegistry(set),
                StatePoliciesFixture.InitialValues(),
                scratch);

            Assert.That(plan.Succeeded, Is.True, plan.Detail);
            Assert.That(plan.ResetCount, Is.EqualTo(1));
            Assert.That(plan.Dispositions[0].Kind, Is.EqualTo(StateDispositionKind.Reset));
            Assert.That(plan.Decisions[0].PolicyKey, Is.EqualTo(StatePolicyFixtureIds.QuestInit));
            Assert.That(plan.Decisions[0].Reason, Is.EqualTo("content repair"));
            Assert.That(scratch.TryRead(plan.Decisions[0].Live, out int staged), Is.True);
            Assert.That(staged, Is.EqualTo(7), "a reset writes the declared initialization policy, never an implicit zero");
        }

        [Test]
        public void AnUndeclaredResetIsRefused()
        {
            SlotStatePolicySet set = StatePoliciesFixture.Set(Durable(ValueSlot));

            StatePolicyPlan plan = StatePolicyExecutor.Execute(
                set,
                new List<LiveSlotState> { StatePoliciesFixture.Live(ValueSlot, 2U, 41) },
                new List<StatePolicyRequest>
                {
                    StatePolicyRequest.Reset(StatePoliciesFixture.Key(ValueSlot, StatePolicyFixtureIds.QuestOwner), "because I said so"),
                },
                new MigrationRegistry(null),
                new DeclaredSlotMigrationRegistry(set),
                StatePoliciesFixture.InitialValues(),
                new MigrationScratch(4096UL, 64UL));

            Assert.That(plan.Succeeded, Is.False, "an unpermitted reset is never implicit (P-032)");
            Assert.That(plan.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(plan.Detail, Does.Contain("does not declare a permitted reset"));
            Assert.That(plan.Dispositions, Is.Empty);
        }

        [Test]
        public void APermittedResetWithoutAReasonIsRefused()
        {
            SlotStatePolicySet set = StatePoliciesFixture.Set(
                Durable(ValueSlot, resetPermitted: true, resetReason: "content repair"));

            StatePolicyPlan plan = StatePolicyExecutor.Execute(
                set,
                new List<LiveSlotState> { StatePoliciesFixture.Live(ValueSlot, 2U, 41) },
                new List<StatePolicyRequest>
                {
                    StatePolicyRequest.Reset(StatePoliciesFixture.Key(ValueSlot, StatePolicyFixtureIds.QuestOwner), string.Empty),
                },
                new MigrationRegistry(null),
                new DeclaredSlotMigrationRegistry(set),
                StatePoliciesFixture.InitialValues(),
                new MigrationScratch(4096UL, 64UL));

            Assert.That(plan.Succeeded, Is.False);
            Assert.That(plan.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(plan.Detail, Does.Contain("explicit proposal reason"));
        }

        [Test]
        public void AResetWhoseInitializationPolicyIsUnregisteredIsRefused()
        {
            SlotStatePolicySet set = StatePoliciesFixture.Set(
                Durable(ValueSlot, resetPermitted: true, resetReason: "content repair"));

            StatePolicyPlan plan = StatePolicyExecutor.Execute(
                set,
                new List<LiveSlotState> { StatePoliciesFixture.Live(ValueSlot, 2U, 41) },
                new List<StatePolicyRequest>
                {
                    StatePolicyRequest.Reset(StatePoliciesFixture.Key(ValueSlot, StatePolicyFixtureIds.QuestOwner), "content repair"),
                },
                new MigrationRegistry(null),
                new DeclaredSlotMigrationRegistry(set),
                null,
                new MigrationScratch(4096UL, 64UL));

            Assert.That(plan.Succeeded, Is.False);
            Assert.That(plan.Code, Is.EqualTo(DiagnosticCode.MissingDependency));
            Assert.That(plan.Detail, Does.Contain("no registered value"));
        }

        [Test]
        public void AManifestSupportedResetProducesAResettablePolicy()
        {
            SlotStatePolicy policy = DeclaredPolicy(resetSupported: true, resetReason: "content repair");
            SlotStatePolicySet set = StatePoliciesFixture.Set(policy);
            var scratch = new MigrationScratch(4096UL, 64UL);

            Assert.That(policy.Options.ResetPermitted, Is.True,
                "a slot's own manifest declaration is the only authority for a reset (P-032)");
            Assert.That(policy.Options.ResetReason, Is.EqualTo("content repair"));

            StatePolicyPlan plan = StatePolicyExecutor.Execute(
                set,
                new List<LiveSlotState> { StatePoliciesFixture.Live(ValueSlot, 2U, 41) },
                new List<StatePolicyRequest>
                {
                    StatePolicyRequest.Reset(StatePoliciesFixture.Key(ValueSlot, StatePolicyFixtureIds.QuestOwner), "content repair"),
                },
                new MigrationRegistry(null),
                new DeclaredSlotMigrationRegistry(set),
                StatePoliciesFixture.InitialValues(),
                scratch);

            Assert.That(plan.Succeeded, Is.True, plan.Detail);
            Assert.That(plan.ResetCount, Is.EqualTo(1));
            Assert.That(plan.Dispositions[0].Kind, Is.EqualTo(StateDispositionKind.Reset));
            Assert.That(plan.Decisions[0].PolicyKey, Is.EqualTo(StatePolicyFixtureIds.QuestInit));
            Assert.That(plan.Decisions[0].Reason, Is.EqualTo("content repair"));
            Assert.That(scratch.TryRead(plan.Decisions[0].Live, out int staged), Is.True);
            Assert.That(staged, Is.EqualTo(7), "a reset writes the declared initialization policy, never an implicit zero");
        }

        [Test]
        public void AManifestWithoutResetSupportRefusesAnExecutedReset()
        {
            SlotStatePolicy policy = DeclaredPolicy(resetSupported: false, resetReason: string.Empty);
            SlotStatePolicySet set = StatePoliciesFixture.Set(policy);
            var key = StatePoliciesFixture.Key(ValueSlot, StatePolicyFixtureIds.QuestOwner);
            var scratch = new MigrationScratch(4096UL, 64UL);

            Assert.That(policy.Options.ResetPermitted, Is.False,
                "a manifest that declares no reset support permits none (P-032)");

            StatePolicyPlan plan = StatePolicyExecutor.Execute(
                set,
                new List<LiveSlotState> { StatePoliciesFixture.Live(ValueSlot, 2U, 41) },
                new List<StatePolicyRequest> { StatePolicyRequest.Reset(key, "content repair") },
                new MigrationRegistry(null),
                new DeclaredSlotMigrationRegistry(set),
                StatePoliciesFixture.InitialValues(),
                scratch);

            Assert.That(plan.Succeeded, Is.False,
                "an unpermitted reset is refused, never applied as zero initialization (P-032)");
            Assert.That(plan.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(plan.Detail, Does.Contain("does not declare a permitted reset"));
            Assert.That(plan.RefusedCount, Is.EqualTo(1));
            Assert.That(plan.Dispositions, Is.Empty);
            Assert.That(plan.StagedValues, Is.Empty);
            Assert.That(scratch.TryRead(key, out int _), Is.False, "a refused reset stages nothing");
        }

        [Test]
        public void AManifestSupportedResetWithoutAReasonIsADeclarationError()
        {
            StateSlotSpec spec = StatePoliciesFixture.Spec(
                ValueSlot,
                StatePolicyFixtureIds.QuestOwner,
                StatePolicyFixtureIds.QuestSchema,
                QuestLayout,
                StatePoliciesFixture.QuestFields(0x10UL),
                LastSupportPolicy.PreserveDormant,
                StatePolicyFixtureIds.QuestMigration,
                default(FactoryKey),
                true,
                string.Empty);
            SlotStatePolicy policy = SlotStatePolicy.FromSpec(spec);

            SlotPolicyResult declared = SlotPolicyValidator.ValidateDeclaration(policy.Declaration);

            Assert.That(declared.Succeeded, Is.False, "a supported reset must record the reason P-032 requires");
            Assert.That(declared.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(declared.Detail, Does.Contain("without recording the explicit reason"));

            SlotStatePolicySet set = StatePoliciesFixture.Set(policy);
            Assert.That(set.TryValidateDeclarations(out DiagnosticCode code, out string detail), Is.False, detail);
            Assert.That(code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
        }

        // ------------------------------------------------------------------ TransferTo and owner transfer (P-025, P-032)

        [Test]
        public void TheLastSupportLossOfATransferableSlotNamesItsDestination()
        {
            SlotStatePolicySet set = TransferSet();
            SlotStatePolicy transferable = set.Policies[0];
            var key = StatePoliciesFixture.Key(FactSlot, StatePolicyFixtureIds.QuestOwner);

            StatePolicyPlan plan = StatePolicyExecutor.Execute(
                set,
                new List<LiveSlotState>
                {
                    new LiveSlotState(key, 2U, 9),
                },
                new List<StatePolicyRequest>
                {
                    StatePolicyRequest.LastSupportTransfer(key, StatePolicyFixtureIds.TransferOwner, StatePolicyFixtureIds.Target(1UL)),
                },
                new MigrationRegistry(null),
                new DeclaredSlotMigrationRegistry(set),
                null,
                new MigrationScratch(4096UL, 64UL));

            Assert.That(plan.Succeeded, Is.True, plan.Detail);
            Assert.That(transferable.LastSupport, Is.EqualTo(LastSupportPolicy.TransferTo));
            Assert.That(plan.TransferredCount, Is.EqualTo(1));
            Assert.That(plan.Dispositions[0].Kind, Is.EqualTo(StateDispositionKind.Transfer));
            Assert.That(plan.Dispositions[0].DestinationOwner, Is.EqualTo(StatePolicyFixtureIds.TransferOwner));
            Assert.That(plan.Decisions[0].Destination.Target, Is.EqualTo(StatePolicyFixtureIds.Target(1UL)));
            Assert.That(plan.Decisions[0].DeclaredLastSupportTransfer, Is.True);
            Assert.That(plan.Decisions[0].MovesToAnotherOwner, Is.True, "the value moves to another owner's storage key");
            Assert.That(plan.Decisions[0].PolicyKey, Is.EqualTo(StatePolicyFixtureIds.QuestTransfer));
        }

        [Test]
        public void ATransferToAnUndeclaredOwnerIsRefused()
        {
            SlotStatePolicySet set = TransferSet();
            SlotStatePolicy transferable = set.Policies[0];
            var key = StatePoliciesFixture.Key(FactSlot, StatePolicyFixtureIds.QuestOwner);

            OwnerTransferResult result = OwnerTransferValidator.Validate(
                transferable,
                key,
                StatePolicyFixtureIds.Owner(9UL),
                StatePolicyFixtureIds.Target(1UL),
                true,
                set,
                null);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Code, Is.EqualTo(DiagnosticCode.MissingDependency));
            Assert.That(result.Detail, Does.Contain("not declared by this catalog revision"));
        }

        [Test]
        public void ATransferToTheSameOwnerIsRefused()
        {
            SlotStatePolicySet set = TransferSet();
            SlotStatePolicy transferable = set.Policies[0];

            OwnerTransferResult result = OwnerTransferValidator.Validate(
                transferable,
                StatePoliciesFixture.Key(FactSlot, StatePolicyFixtureIds.QuestOwner),
                StatePolicyFixtureIds.QuestOwner,
                StatePolicyFixtureIds.Target(1UL),
                true,
                set,
                null);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
        }

        [Test]
        public void AnExplicitOwnerTransferNeedsTheDeclaredTransferPolicy()
        {
            SlotStatePolicySet set = TransferSet();
            SlotStatePolicy dormantOnly = Durable(ValueSlot);
            SlotStatePolicy transferable = set.Policies[0];

            OwnerTransferResult refused = OwnerTransferValidator.Validate(
                dormantOnly,
                StatePoliciesFixture.Key(ValueSlot, StatePolicyFixtureIds.QuestOwner),
                StatePolicyFixtureIds.TransferOwner,
                StatePolicyFixtureIds.Target(1UL),
                false,
                set,
                null);

            Assert.That(refused.Succeeded, Is.False, "an explicit transfer needs the declared owner-transfer policy");
            Assert.That(refused.Code, Is.EqualTo(DiagnosticCode.MissingDependency));

            OwnerTransferResult accepted = OwnerTransferValidator.Validate(
                transferable,
                StatePoliciesFixture.Key(FactSlot, StatePolicyFixtureIds.QuestOwner),
                StatePolicyFixtureIds.TransferOwner,
                StatePolicyFixtureIds.Target(2UL),
                false,
                set,
                null);

            Assert.That(accepted.Succeeded, Is.True, accepted.Detail);
            Assert.That(accepted.Destination.Owner, Is.EqualTo(StatePolicyFixtureIds.TransferOwner));
            Assert.That(accepted.Destination.Target, Is.EqualTo(StatePolicyFixtureIds.Target(2UL)));
            Assert.That(accepted.PolicyKey, Is.EqualTo(StatePolicyFixtureIds.QuestTransfer));
        }

        [Test]
        public void ATransferDestinationThatAnotherOwnerAlreadyHoldsIsRefused()
        {
            SlotStatePolicySet set = TransferSet();

            // A live row already claims the destination owner for the same slot, so the declaration and the live key
            // disagree about who owns the state: one owner per slot (P-034).
            StatePolicyPlan plan = StatePolicyExecutor.Execute(
                set,
                new List<LiveSlotState>
                {
                    new LiveSlotState(StatePoliciesFixture.Key(FactSlot, StatePolicyFixtureIds.QuestOwner), 2U, 9),
                    new LiveSlotState(StatePoliciesFixture.Key(FactSlot, StatePolicyFixtureIds.TransferOwner), 2U, 4),
                },
                new List<StatePolicyRequest>
                {
                    StatePolicyRequest.LastSupportTransfer(
                        StatePoliciesFixture.Key(FactSlot, StatePolicyFixtureIds.QuestOwner),
                        StatePolicyFixtureIds.TransferOwner,
                        StatePolicyFixtureIds.Target(1UL)),
                },
                new MigrationRegistry(null),
                new DeclaredSlotMigrationRegistry(set),
                null,
                new MigrationScratch(4096UL, 64UL));

            Assert.That(plan.Succeeded, Is.False);
            Assert.That(plan.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(plan.Dispositions, Is.Empty);
        }

        // ------------------------------------------------------------------ support sets keep unrelated state (P-033)

        [Test]
        public void RemovingOneOfTwoSupportsKeepsTheOtherSupportAndTheValue()
        {
            var slots = new List<SlotAuthorityDeclaration>
            {
                Durable(ValueSlot).Declaration,
            };
            var registry = new SupportSetRegistry(slots, null);
            var slot = StatePoliciesFixture.Key(ValueSlot, StatePolicyFixtureIds.QuestOwner);
            var first = new SupportRecord(
                slot, StatePolicyFixtureIds.Provider(1UL), StatePolicyFixtureIds.Rule(1UL), StatePolicyFixtureIds.Capability(1UL));
            var second = new SupportRecord(
                slot, StatePolicyFixtureIds.Provider(2UL), StatePolicyFixtureIds.Rule(2UL), StatePolicyFixtureIds.Capability(2UL));

            Assert.That(registry.Add(first).Applied, Is.True);
            Assert.That(registry.Add(second).Applied, Is.True);
            Assert.That(registry.SupportCount(slot), Is.EqualTo(2), "support is a set of identities, not a last-writer flag (P-017)");

            SupportSetDelta retracted = registry.Retract(first);
            Assert.That(retracted.Outcome, Is.EqualTo(SupportSetOutcome.RetractedWhileSupported));
            Assert.That(retracted.RemainingSupportCount, Is.EqualTo(1));
            Assert.That(retracted.Lifetime, Is.EqualTo(DerivedLifetimeDecision.KeepActive));
            Assert.That(registry.HasSupport(slot), Is.True, "the surviving support keeps the structure (P-033)");
            Assert.That(registry.SupportCount(slot), Is.EqualTo(1));
            Assert.That(registry.RetractedCount, Is.EqualTo(1));
        }

        [Test]
        public void TheLastSupportLossFollowsTheDeclaredPolicyExactly()
        {
            var dormantRegistry = new SupportSetRegistry(
                new List<SlotAuthorityDeclaration> { Durable(ValueSlot).Declaration }, null);
            var derivedRegistry = new SupportSetRegistry(
                new List<SlotAuthorityDeclaration>
                {
                    StatePoliciesFixture.Derived(TrailSlot, StatePolicyFixtureIds.QuestOwner, StatePolicyFixtureIds.TrailSchema, TrailLayout).Declaration,
                },
                null);
            var transferRegistry = new SupportSetRegistry(
                new List<SlotAuthorityDeclaration>
                {
                    StatePoliciesFixture.Transferable(
                        FactSlot, StatePolicyFixtureIds.QuestOwner, StatePolicyFixtureIds.QuestSchema, QuestLayout,
                        StatePolicyFixtureIds.QuestTransfer).Declaration,
                },
                null);

            Assert.That(dormantRegistry.Add(Support(ValueSlot, 1UL)).Applied, Is.True);
            Assert.That(derivedRegistry.Add(Support(TrailSlot, 1UL)).Applied, Is.True);
            Assert.That(transferRegistry.Add(Support(FactSlot, 1UL)).Applied, Is.True);

            SupportSetDelta dormant = dormantRegistry.Retract(Support(ValueSlot, 1UL));
            SupportSetDelta derived = derivedRegistry.Retract(Support(TrailSlot, 1UL));
            SupportSetDelta transferred = transferRegistry.Retract(Support(FactSlot, 1UL));

            Assert.That(dormant.Lifetime, Is.EqualTo(DerivedLifetimeDecision.RetainDormant));
            Assert.That(dormant.EndedActiveLife, Is.True);
            Assert.That(derived.Lifetime, Is.EqualTo(DerivedLifetimeDecision.RemoveDerived));
            Assert.That(transferred.Lifetime, Is.EqualTo(DerivedLifetimeDecision.TransferPending));
            Assert.That(dormantRegistry.RetainedDormantCount, Is.EqualTo(1));
            Assert.That(derivedRegistry.RemovedDerivedCount, Is.EqualTo(1));
            Assert.That(transferRegistry.TransferPendingCount, Is.EqualTo(1));
            Assert.That(derivedRegistry.RetractedCount, Is.EqualTo(1));
            Assert.That(transferRegistry.RetractedCount, Is.EqualTo(1));
        }

        [Test]
        public void ASharedComponentRejectsRemovalWhileItsRecipeStillRequiresIt()
        {
            var slot = StatePoliciesFixture.Key(ValueSlot, StatePolicyFixtureIds.QuestOwner);
            var registry = new SupportSetRegistry(
                new List<SlotAuthorityDeclaration> { Durable(ValueSlot).Declaration },
                new List<StateSlotKey> { slot });

            registry.Add(Support(ValueSlot, 1UL));
            SupportSetDelta delta = registry.Retract(Support(ValueSlot, 1UL));

            Assert.That(delta.Outcome, Is.EqualTo(SupportSetOutcome.RetainedByRecipeRequirement));
            Assert.That(delta.Applied, Is.True);
            Assert.That(delta.Lifetime, Is.EqualTo(DerivedLifetimeDecision.KeepActive),
                "a base recipe requirement keeps the component after its final derived support (P-033)");
            Assert.That(registry.RetainedByRecipeCount, Is.EqualTo(1));
        }

        // ------------------------------------------------------------------ helpers

        private static SlotStatePolicy Durable(
            SlotId slot,
            bool resetPermitted = false,
            string resetReason = "")
            => StatePoliciesFixture.Durable(
                slot,
                StatePolicyFixtureIds.QuestOwner,
                StatePolicyFixtureIds.QuestSchema,
                QuestLayout,
                StatePoliciesFixture.QuestFields(0x10UL),
                StatePolicyFixtureIds.QuestMigration,
                resetPermitted,
                resetReason);

        /// <summary>
        /// The quest value slot as one manifest declaration with its own reset support, projected through the
        /// production set builder, so a test reads the policy its declaration actually produces (P-032).
        /// </summary>
        private static SlotStatePolicy DeclaredPolicy(bool resetSupported, string resetReason)
        {
            StateSlotSpec spec = StatePoliciesFixture.Spec(
                ValueSlot,
                StatePolicyFixtureIds.QuestOwner,
                StatePolicyFixtureIds.QuestSchema,
                QuestLayout,
                StatePoliciesFixture.QuestFields(0x10UL),
                LastSupportPolicy.PreserveDormant,
                StatePolicyFixtureIds.QuestMigration,
                default(FactoryKey),
                resetSupported,
                resetReason);
            var specs = new List<StateSlotSpec> { spec };
            if (!SlotStatePolicySet.TryBuild(specs, out SlotStatePolicySet? set, out DiagnosticCode code, out string detail)
                || set == null
                || set.Count != 1)
            {
                throw new InvalidOperationException(
                    "the declared slot was refused: " + code.ToString() + ": " + detail);
            }

            return set.Policies[0];
        }

        /// <summary>
        /// The transfer fixture: the fact slot's last support transfers to `TransferOwner`, and that owner is a
        /// declared owner of this revision through its own slot, which is what "named available owner" means (P-032).
        /// </summary>
        private static SlotStatePolicySet TransferSet()
            => StatePoliciesFixture.Set(
                StatePoliciesFixture.Transferable(
                    FactSlot, StatePolicyFixtureIds.QuestOwner, StatePolicyFixtureIds.QuestSchema, QuestLayout,
                    StatePolicyFixtureIds.QuestTransfer),
                StatePoliciesFixture.Durable(
                    VersionSlot, StatePolicyFixtureIds.TransferOwner, StatePolicyFixtureIds.QuestSchema, QuestLayout,
                    StatePoliciesFixture.QuestFields(0x11UL), default(FactoryKey)));

        private static SupportRecord Support(SlotId slot, ulong ordinal)
            => new SupportRecord(
                StatePoliciesFixture.Key(slot, StatePolicyFixtureIds.QuestOwner),
                StatePolicyFixtureIds.Provider(ordinal),
                StatePolicyFixtureIds.Rule(ordinal),
                StatePolicyFixtureIds.Capability(ordinal));
    }
}

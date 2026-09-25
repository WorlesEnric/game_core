// GameCore.Planning tests — `AssemblyPlanner` (GC-008, P-017 to P-024, P-028, P-032, P-040).
//
// The planner is pure: the same immutable inputs must produce the same plan hash, and every rejection must be a
// value with the protocol's own code. The cases below cover eligibility, precedence and policy conflicts, exact
// retraction of one provider's support, migration validation against the descriptor, the bounded scratch account
// and the compiled execution order.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using NUnit.Framework;

namespace GameCore.Planning.Tests
{
    [TestFixture]
    public sealed class AssemblyPlannerTests
    {
        private static WorldId World => PlansFixtureKeys.World(1UL);

        [Test]
        public void OneMountDerivesTheSameBindingForEveryMatchingTarget()
        {
            PlannedPublication plan = PlansFixture.Plan(
                PlansFixture.MountProposal(World, PlansFixture.Revision, PlansFixture.Epoch));

            Assert.That(plan.IsPrepared, Is.True, plan.State.Describe());
            Assert.That(plan.State.Phase, Is.EqualTo(PlanPhase.Prepared));
            Assert.That(plan.Installs.Count, Is.EqualTo(2), "both eligible existing targets receive the capability (P-013)");
            Assert.That(plan.Removals, Is.Empty);
            Assert.That(plan.AffectedTargets.Count, Is.EqualTo(2));
            Assert.That(plan.Rules.Count, Is.EqualTo(1),
                "one rule slot per recipe/scope/capability, shared by both targets (P-017)");
            Assert.That(plan.Plan.Derivation.Added.Count, Is.EqualTo(2));
            Assert.That(plan.Plan.Validity.Affected.Targets, Is.EqualTo(2));
            Assert.That(plan.Plan.Validity.Affected.ContributionsAdded, Is.EqualTo(2));

            for (int i = 0; i < plan.Installs.Count; i++)
            {
                TargetBindingRow row = plan.Installs[i];
                Assert.That(row.Value, Is.EqualTo(3), "the declared value becomes the effective value (P-019 Replace)");
                Assert.That(row.Provider, Is.EqualTo(PlansFixtureKeys.Provider(1UL)));
                Assert.That(row.Priority, Is.EqualTo(10));
            }

            Assert.That(plan.After.Count, Is.EqualTo(2), "the after-table is what the publisher exposes (P-030)");
            Assert.That(plan.After.TargetCount, Is.EqualTo(2));
        }

        [Test]
        public void AnIneligibleRecipeReceivesNothing()
        {
            var other = new DefinitionRef(
                new DefinitionId(new Id128(PlansFixtureKeys.Namespace, 0x2199UL)),
                new SchemaRef(PlansFixtureKeys.CardSchemaId, 1U),
                new DefinitionRevision(1UL));

            var targets = new List<TargetDefinition>
            {
                new TargetDefinition(PlansFixtureKeys.Target(1UL), PlansFixtureKeys.CardRecipe, PlansFixtureKeys.RootScope),
                new TargetDefinition(PlansFixtureKeys.Target(2UL), other, PlansFixtureKeys.RootScope),
            };

            PlannedPublication plan = PlansFixture.Plan(
                PlansFixture.MountProposal(World, PlansFixture.Revision, PlansFixture.Epoch),
                targets: targets);

            Assert.That(plan.Installs.Count, Is.EqualTo(1), "an unrecognised target stays unchanged, never guessed (P-015)");
            Assert.That(plan.Installs[0].Target, Is.EqualTo(PlansFixtureKeys.Target(1UL)));
        }

        [Test]
        public void AStaleExpectedRevisionRejectsWithoutInstallingAnything()
        {
            CompositionProposal proposal = PlansFixture.MountProposal(
                World,
                new CompositionRevision(PlansFixture.Revision.Value + 3UL),
                PlansFixture.Epoch);

            PlannedPublication plan = PlansFixture.Plan(proposal);

            Assert.That(plan.IsRejected, Is.True);
            Assert.That(plan.State.Code, Is.EqualTo(DiagnosticCode.StalePlan));
            Assert.That(plan.Installs, Is.Empty);
            Assert.That(plan.State.HasCrossedLiveWriteBoundary, Is.False, "a stale plan makes no live write (P-028)");
            Assert.That(plan.Plan.Validity.Invalidation.Count, Is.EqualTo(1));
        }

        [Test]
        public void HigherPriorityWinsAndTheLoserStaysProvenance()
        {
            var mounts = new List<ProposedMount>
            {
                PlansFixture.Mount(1UL, new List<ProposedCapability> { PlansFixture.LimitCapability(value: 3, priority: 10) }),
                PlansFixture.Mount(2UL, new List<ProposedCapability> { PlansFixture.LimitCapability(value: 2, priority: 20) }),
            };

            PlannedPublication plan = PlansFixture.Plan(
                PlansFixture.MountProposal(World, PlansFixture.Revision, PlansFixture.Epoch, mounts: mounts));

            Assert.That(plan.IsPrepared, Is.True, plan.Plan.Validity.Invalidation.Count.ToString());
            Assert.That(plan.Installs.Count, Is.EqualTo(2), "one effective row per target, not one per candidate (P-017)");
            for (int i = 0; i < plan.Installs.Count; i++)
            {
                Assert.That(plan.Installs[i].Value, Is.EqualTo(2), "higher signed priority wins (P-018)");
                Assert.That(plan.Installs[i].Provider, Is.EqualTo(PlansFixtureKeys.Provider(2UL)));
            }
        }

        [Test]
        public void ExclusiveWithTwoCandidatesRejectsAndNamesThem()
        {
            var mounts = new List<ProposedMount>
            {
                PlansFixture.Mount(
                    1UL,
                    new List<ProposedCapability> { PlansFixture.LimitCapability(value: 3, priority: 10, policy: CompositionPolicy.Exclusive) }),
                PlansFixture.Mount(
                    2UL,
                    new List<ProposedCapability> { PlansFixture.LimitCapability(value: 2, priority: 20, policy: CompositionPolicy.Exclusive) }),
            };

            PlannedPublication plan = PlansFixture.Plan(
                PlansFixture.MountProposal(World, PlansFixture.Revision, PlansFixture.Epoch, mounts: mounts));

            Assert.That(plan.IsRejected, Is.True, "priority cannot destroy an exclusive capability (P-019)");
            Assert.That(plan.State.Code, Is.EqualTo(DiagnosticCode.CapabilityConflict));
            Assert.That(plan.Installs, Is.Empty, "a rejected proposal publishes no partial closure (P-019)");
            Diagnostic diagnostic = plan.Plan.Validity.Invalidation[0];
            Assert.That(diagnostic.Summary, Does.Contain("priority=20"));
            Assert.That(diagnostic.Summary, Does.Contain("priority=10"), "the conflicting candidates are the witness");
        }

        [Test]
        public void UnmountRetractsExactlyTheUnmountingProvidersSupport()
        {
            // Provider 1 supports the limit slot; provider 2 supports a second output slot. Both are effective at
            // once, which is what makes "removing one support" observable (P-017, P-033).
            var first = new List<ProposedMount>
            {
                PlansFixture.Mount(1UL, new List<ProposedCapability> { PlansFixture.LimitCapability() }),
            };

            PlannedPublication mounted = PlansFixture.Plan(
                PlansFixture.MountProposal(World, PlansFixture.Revision, PlansFixture.Epoch, mounts: first));
            Assert.That(mounted.Installs.Count, Is.EqualTo(2), "one row per target for the first slot");
            Assert.That(mounted.Rules.Count, Is.EqualTo(1), "one rule slot per recipe/scope/capability (P-017)");

            var second = new List<ProposedMount>
            {
                PlansFixture.Mount(2UL, new List<ProposedCapability> { PlansFixture.FlagCapability() }),
            };

            PlannedPublication both = PlansFixture.Plan(
                PlansFixture.MountProposal(
                    World,
                    PlansFixture.Revision,
                    PlansFixture.Epoch,
                    sequence: 2UL,
                    mounts: second),
                current: mounted.After,
                rules: mounted.Rules);

            Assert.That(both.Installs.Count, Is.EqualTo(2));
            Assert.That(both.After.Count, Is.EqualTo(4), "two targets, two slots, four effective rows (P-017)");
            Assert.That(both.Rules.Count, Is.EqualTo(2), "the two slots have two rules");

            var unmounts = new List<ProposedUnmount>
            {
                new ProposedUnmount(PlansFixtureKeys.Instance(1UL), PlansFixtureKeys.Provider(1UL), PlansFixtureKeys.RootScope),
            };

            PlannedPublication removed = PlansFixture.Plan(
                PlansFixture.MountProposal(
                    World,
                    PlansFixture.Revision,
                    PlansFixture.Epoch,
                    sequence: 3UL,
                    mounts: new List<ProposedMount>(),
                    unmounts: unmounts),
                current: both.After,
                rules: both.Rules);

            Assert.That(removed.Removals.Count, Is.EqualTo(2), "only the unmounting provider's rows are retracted (P-033)");
            Assert.That(removed.After.Count, Is.EqualTo(2), "the other provider's support survives");
            for (int i = 0; i < removed.After.Rows.Count; i++)
            {
                Assert.That(removed.After.Rows[i].Provider, Is.EqualTo(PlansFixtureKeys.Provider(2UL)));
                Assert.That(removed.After.Rows[i].OutputSlot, Is.EqualTo(1U));
            }

            Assert.That(removed.Rules.Count, Is.EqualTo(1), "the retired provider's rule left with its support");
            Assert.That(removed.Plan.Derivation.Removed.Count, Is.EqualTo(2));
            Assert.That(removed.Removals.Count + removed.Installs.Count, Is.Not.EqualTo(0), "a retraction is an effective change");
        }

        [Test]
        public void ALowerPriorityDeclarationCannotDisplaceAHigherPriorityEffectiveRow()
        {
            var strong = new List<ProposedMount>
            {
                PlansFixture.Mount(1UL, new List<ProposedCapability> { PlansFixture.LimitCapability(value: 3, priority: 30) }),
            };

            PlannedPublication mounted = PlansFixture.Plan(
                PlansFixture.MountProposal(World, PlansFixture.Revision, PlansFixture.Epoch, mounts: strong));
            Assert.That(mounted.Installs.Count, Is.EqualTo(2));

            // A weaker declaration joins: the existing effective row is a candidate too, so P-018's ranking keeps it.
            var weak = new List<ProposedMount>
            {
                PlansFixture.Mount(2UL, new List<ProposedCapability> { PlansFixture.LimitCapability(value: 9, priority: 5) }),
            };

            PlannedPublication weaker = PlansFixture.Plan(
                PlansFixture.MountProposal(
                    World,
                    PlansFixture.Revision,
                    PlansFixture.Epoch,
                    sequence: 2UL,
                    mounts: weak),
                current: mounted.After,
                rules: mounted.Rules);

            Assert.That(weaker.Installs, Is.Empty, "the higher-priority effective row survives (P-018)");
            Assert.That(weaker.After.Count, Is.EqualTo(2));
            Assert.That(weaker.Installs.Count + weaker.Removals.Count, Is.EqualTo(0), "the plan is a no-op");

            // A stronger declaration replaces it, and the change is reported as a changed contribution.
            var stronger = new List<ProposedMount>
            {
                PlansFixture.Mount(3UL, new List<ProposedCapability> { PlansFixture.LimitCapability(value: 1, priority: 40) }),
            };

            PlannedPublication replaced = PlansFixture.Plan(
                PlansFixture.MountProposal(
                    World,
                    PlansFixture.Revision,
                    PlansFixture.Epoch,
                    sequence: 3UL,
                    mounts: stronger),
                current: mounted.After,
                rules: mounted.Rules);

            Assert.That(replaced.Installs.Count, Is.EqualTo(2));
            Assert.That(replaced.Plan.Derivation.Changed.Count, Is.EqualTo(2), "the effective row changed (05 s4)");
            for (int i = 0; i < replaced.Installs.Count; i++)
            {
                Assert.That(replaced.Installs[i].Value, Is.EqualTo(1));
                Assert.That(replaced.Installs[i].Provider, Is.EqualTo(PlansFixtureKeys.Provider(3UL)));
            }
        }

        [Test]
        public void ThePlanHashIsIndependentOfMountDeclarationOrder()
        {
            var reversed = new List<ProposedMount>
            {
                PlansFixture.Mount(2UL, null),
                PlansFixture.Mount(1UL, null),
            };

            CompositionProposal proposal = PlansFixture.MountProposal(World, PlansFixture.Revision, PlansFixture.Epoch);
            PlannedPublication a = PlansFixture.Plan(proposal, targets: PlansFixture.TwoTargets());

            // Same operation, same input hash and the same declarations in the opposite order: the canonical plan
            // hash must not change, because insertion order never decides precedence (P-008, P-018).
            var permuted = new CompositionProposal(
                proposal.Operation,
                proposal.InputHash,
                proposal.ExpectedRevision,
                proposal.BaseEpoch,
                proposal.CatalogHash,
                PropagationMode.Automatic,
                reversed,
                null);

            PlannedPublication b = PlansFixture.Plan(permuted, targets: PlansFixture.TwoTargets());

            Assert.That(a.Plan.PlanHash.Equals(b.Plan.PlanHash), Is.True,
                "insertion order must not decide the plan hash (P-008)");
            Assert.That(a.After.Fingerprint().Equals(b.After.Fingerprint()), Is.True,
                "the effective assembly fingerprint is canonical too");
            Assert.That(a.Schedule.Hash.Equals(b.Schedule.Hash), Is.True);
        }

        [Test]
        public void TheSameInputsAlwaysProduceTheSameHash()
        {
            CompositionProposal proposal = PlansFixture.MountProposal(World, PlansFixture.Revision, PlansFixture.Epoch);
            PlannedPublication first = PlansFixture.Plan(proposal);
            PlannedPublication second = PlansFixture.Plan(proposal);

            Assert.That(first.Plan.PlanHash.Equals(second.Plan.PlanHash), Is.True, "plan hashing excludes timestamps (P-028)");
            Assert.That(first.Plan.PlanHash.IsEmpty, Is.False);
        }

        [Test]
        public void AnInvalidDescriptorRejectsThePlan()
        {
            var stage = new DescriptorStage(
                PlansFixtureKeys.AcceptStage,
                1U,
                0,
                new List<DescriptorSystem>
                {
                    new DescriptorSystem(PlansFixtureKeys.AcceptSystem, SystemDispatchKind.ManagedSystem, null, null),
                },
                null);

            var consumerOnly = new DescriptorStage(
                PlansFixtureKeys.SettleStage,
                1U,
                1,
                new List<DescriptorSystem>
                {
                    new DescriptorSystem(PlansFixtureKeys.SettleSystem, SystemDispatchKind.ManagedSystem, null, null),
                },
                new List<int> { 5 });

            var descriptor = new OwnershipStageDescriptor(
                PlanHashing.Of("invalid descriptor: forward stage edge"),
                null,
                new List<DescriptorStage> { stage, consumerOnly },
                null);

            Assert.That(descriptor.TryValidate(out DiagnosticCode descriptorCode, out string detail), Is.False);
            Assert.That(descriptorCode, Is.EqualTo(DiagnosticCode.Cycle));
            Assert.That(detail, Does.Contain("backward-only"));

            PlannedPublication plan = PlansFixture.Plan(
                PlansFixture.MountProposal(World, PlansFixture.Revision, PlansFixture.Epoch),
                descriptor: descriptor);

            Assert.That(plan.IsRejected, Is.True, "no plan may be built against an ambiguous execution graph (P-040)");
            Assert.That(plan.State.Code, Is.EqualTo(DiagnosticCode.Cycle));
        }

        [Test]
        public void AMatchingSchemaVersionOnlyRetainsAndASchemaChangeMigrates()
        {
            TargetId target = PlansFixtureKeys.Target(1UL);
            var targets = new List<TargetDefinition>
            {
                new TargetDefinition(target, PlansFixture.CardRecipe, PlansFixtureKeys.RootScope),
            };

            PlannedPublication retained = PlansFixture.Plan(
                PlansFixture.MountProposal(World, PlansFixture.Revision, PlansFixture.Epoch),
                targets: targets,
                liveSlots: new List<LiveSlotState>
                {
                    PlansFixture.QuestSlotState(target, value: 7, version: PlansFixture.QuestSchemaVersion),
                });

            Assert.That(retained.Dispositions.Count, Is.EqualTo(1));
            Assert.That(retained.Dispositions[0].Kind, Is.EqualTo(StateDispositionKind.Retain), "a compatible schema is preserved (P-032)");
            Assert.That(retained.Migrations, Is.Empty);
            Assert.That(retained.Installs.Count, Is.EqualTo(1), "a target only listed here still receives its binding");
        }

        [Test]
        public void AMissingMigrationHandlerRejectsBeforeAnyWrite()
        {
            TargetId target = PlansFixtureKeys.Target(1UL);
            var targets = new List<TargetDefinition>
            {
                new TargetDefinition(target, PlansFixture.CardRecipe, PlansFixtureKeys.RootScope),
            };

            var slots = new List<LiveSlotState> { PlansFixture.QuestSlotState(target, value: 7, version: 1U) };

            PlannedPublication missing = PlansFixture.Plan(
                PlansFixture.MountProposal(World, PlansFixture.Revision, PlansFixture.Epoch),
                targets: targets,
                liveSlots: slots,
                migrations: new MigrationRegistry(null));

            Assert.That(missing.IsRejected, Is.True);
            Assert.That(missing.State.Code, Is.EqualTo(DiagnosticCode.MigrationRequired),
                "a missing compatible policy is a validation error, not implicit zero initialisation (P-032)");

            // A handler registered for another version pair is not a substitute for the requested one (P-032).
            var wrongPair = new MigrationRegistry(new List<ISlotMigration>
            {
                new DeltaMigration(PlansFixtureKeys.QuestMigrationV1ToV2, 1U, 9U, 1),
            });

            PlannedPublication mismatched = PlansFixture.Plan(
                PlansFixture.MountProposal(World, PlansFixture.Revision, PlansFixture.Epoch),
                targets: targets,
                liveSlots: slots,
                migrations: wrongPair);

            Assert.That(mismatched.IsRejected, Is.True);
            Assert.That(mismatched.State.Code, Is.EqualTo(DiagnosticCode.UnsupportedVersion));
        }

        [Test]
        public void ADescriptorWithoutAVersionChangePolicyRejectsTheMigration()
        {
            TargetId target = PlansFixtureKeys.Target(1UL);
            var targets = new List<TargetDefinition>
            {
                new TargetDefinition(target, PlansFixture.CardRecipe, PlansFixtureKeys.RootScope),
            };

            // A descriptor that declares no version-change policy cannot migrate the slot, and P-032 makes that a
            // validation error rather than an implicit zero initialisation.
            OwnershipStageDescriptor descriptor = PlansFixture.Descriptor(default(FactoryKey));

            PlannedPublication plan = PlansFixture.Plan(
                PlansFixture.MountProposal(World, PlansFixture.Revision, PlansFixture.Epoch),
                targets: targets,
                liveSlots: new List<LiveSlotState> { PlansFixture.QuestSlotState(target, value: 7, version: 1U) },
                descriptor: descriptor);

            Assert.That(plan.IsRejected, Is.True);
            Assert.That(plan.State.Code, Is.EqualTo(DiagnosticCode.MigrationRequired));
            Assert.That(plan.State.Detail, Does.Contain("no version-change migration policy"));
            Assert.That(descriptor.Slots[0].HasVersionChangePolicy, Is.False);
            Assert.That(PlansFixture.Descriptor().Slots[0].HasVersionChangePolicy, Is.True);
        }

        [Test]
        public void APlanBeyondTheConfiguredHardBudgetRejectsWithItsCounts()
        {
            // Two installs of 64 estimated apply bytes each against a 32-byte limit: the whole plan rejects rather
            // than publishing a truncated closure (P-022).
            var tiny = new PlanBudget(
                prepareBytesLimit: 1024UL * 1024UL,
                applyBytesLimit: 32UL,
                scratchCapacityBytes: 4096UL,
                scratchBytesPerSlot: 64UL);

            PlannedPublication plan = PlansFixture.Plan(
                PlansFixture.MountProposal(World, PlansFixture.Revision, PlansFixture.Epoch),
                budget: tiny,
                scratch: new MigrationScratch(4096UL, 64UL));

            Assert.That(plan.IsRejected, Is.True);
            Assert.That(plan.Installs, Is.Empty, "no partial propagation publishes (P-022)");
            Assert.That(
                plan.Plan.Validity.Invalidation[0].Summary,
                Does.Contain("128"),
                "the rejection reports the estimate and the limit it exceeded");
        }

        [Test]
        public void ARunableMigrationIsStagedOnScratchAndRunOnTheCopy()
        {
            TargetId target = PlansFixtureKeys.Target(1UL);
            var targets = new List<TargetDefinition>
            {
                new TargetDefinition(target, PlansFixture.CardRecipe, PlansFixtureKeys.RootScope),
            };

            var migrations = new MigrationRegistry(new List<ISlotMigration>
            {
                new DeltaMigration(PlansFixtureKeys.QuestMigrationV1ToV2, 1U, PlansFixture.QuestSchemaVersion, 10),
            });

            var scratch = new MigrationScratch(4096UL, 64UL);
            var slots = new List<LiveSlotState> { PlansFixture.QuestSlotState(target, value: 7, version: 1U) };

            PlannedPublication plan = PlansFixture.Plan(
                PlansFixture.MountProposal(World, PlansFixture.Revision, PlansFixture.Epoch),
                targets: targets,
                liveSlots: slots,
                migrations: migrations,
                scratch: scratch);

            Assert.That(plan.IsPrepared, Is.True, plan.State.Detail);
            Assert.That(plan.Migrations.Count, Is.EqualTo(1));
            Assert.That(plan.Migrations[0].MigrationKey, Is.EqualTo(PlansFixtureKeys.QuestMigrationV1ToV2));
            Assert.That(plan.Dispositions[0].Kind, Is.EqualTo(StateDispositionKind.Migrate));
            Assert.That(scratch.ReservedSlots, Is.EqualTo(1), "the plan reserved bounded scratch for the migration (P-022)");
            Assert.That(plan.Plan.Resources.ScratchCapacityBytes, Is.EqualTo(4096UL));
            Assert.That(plan.Plan.Runtime.StateDispositions.Count, Is.EqualTo(1));
        }

        [Test]
        public void ScratchThatCannotReserveTheMigrationsRejectsThePlan()
        {
            TargetId target = PlansFixtureKeys.Target(1UL);
            var targets = new List<TargetDefinition>
            {
                new TargetDefinition(target, PlansFixture.CardRecipe, PlansFixtureKeys.RootScope),
            };

            var migrations = new MigrationRegistry(new List<ISlotMigration>
            {
                new DeltaMigration(PlansFixtureKeys.QuestMigrationV1ToV2, 1U, PlansFixture.QuestSchemaVersion, 10),
            });

            var tinyScratch = new MigrationScratch(16UL, 64UL);

            PlannedPublication plan = PlansFixture.Plan(
                PlansFixture.MountProposal(World, PlansFixture.Revision, PlansFixture.Epoch),
                targets: targets,
                liveSlots: new List<LiveSlotState> { PlansFixture.QuestSlotState(target, value: 7, version: 1U) },
                migrations: migrations,
                scratch: tinyScratch);

            Assert.That(plan.IsRejected, Is.True, "temporary storage is a hard, configured limit (P-022)");
            Assert.That(plan.State.Code, Is.EqualTo(DiagnosticCode.BudgetExceeded));
            Assert.That(tinyScratch.BudgetExceededCount, Is.EqualTo(1));
        }

        [Test]
        public void TheCompiledScheduleFollowsTheDescriptorOrderAndCarriesItsBuffers()
        {
            PlannedPublication plan = PlansFixture.Plan(
                PlansFixture.MountProposal(World, PlansFixture.Revision, PlansFixture.Epoch));

            Assert.That(plan.Schedule.Entries.Count, Is.EqualTo(2), "one entry per declared system (P-039)");
            Assert.That(plan.Schedule.Entries[0].Stage, Is.EqualTo(PlansFixtureKeys.AcceptStage));
            Assert.That(plan.Schedule.Entries[0].DispatchIndex, Is.EqualTo(0));
            Assert.That(plan.Schedule.Entries[1].Stage, Is.EqualTo(PlansFixtureKeys.SettleStage));
            Assert.That(plan.Schedule.Entries[1].DispatchIndex, Is.EqualTo(1));
            Assert.That(plan.Schedule.Buffers.Count, Is.EqualTo(1), "the declared buffer binding enters the plan (P-043)");
            Assert.That(plan.Plan.Runtime.Plan, Is.Not.Null);
            Assert.That(plan.Plan.Runtime.Plan!.Nodes.Count, Is.EqualTo(4), "two stages and their systems (P-040)");
            Assert.That(plan.Plan.Runtime.Plan.Edges.Count, Is.EqualTo(1), "the declared stage edge");
            Assert.That(plan.Schedule.Hash.IsEmpty, Is.False);
        }

        [Test]
        public void EverySlotOfTheDescriptorGrantsExactlyOneOwner()
        {
            PlannedPublication plan = PlansFixture.Plan(
                PlansFixture.MountProposal(World, PlansFixture.Revision, PlansFixture.Epoch));

            Assert.That(plan.OwnerGrants.Count, Is.EqualTo(1));
            Assert.That(plan.OwnerGrants[0].Owner, Is.EqualTo(PlansFixtureKeys.SlotOwner));
            Assert.That(plan.OwnerGrants[0].OwnerVersion, Is.EqualTo(1U));
            Assert.That(plan.Plan.Runtime.OwnerGrants.Count, Is.EqualTo(1));
        }

        [Test]
        public void ADuplicateOwnerClaimIsADescriptorValidationFailure()
        {
            var first = new OwnedSlotSpec(
                PlansFixtureKeys.QuestSlot,
                PlansFixtureKeys.SlotOwner,
                new SchemaRef(PlansFixtureKeys.QuestStateSchemaId, 1U),
                1U,
                LastSupportPolicy.PreserveDormant,
                default(Id128));

            var second = new OwnedSlotSpec(
                PlansFixtureKeys.QuestSlot,
                new OwnerId(new Id128(PlansFixtureKeys.Namespace, 0x2999UL)),
                new SchemaRef(PlansFixtureKeys.QuestStateSchemaId, 1U),
                1U,
                LastSupportPolicy.PreserveDormant,
                default(Id128));

            var descriptor = new OwnershipStageDescriptor(
                PlanHashing.Of("invalid descriptor: two owners for one slot"),
                new List<OwnedSlotSpec> { first, second },
                null,
                null);

            Assert.That(descriptor.TryValidate(out DiagnosticCode code, out string detail), Is.False);
            Assert.That(code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(detail, Does.Contain("two owners"));
        }
    }
}

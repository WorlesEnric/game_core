// GameCore.Unity.Runtime.Tests — the assembly publication suite (GC-008).
//
// Every case drives the real modules: the pure `AssemblyPlanner` over the frozen ownership/stage descriptor, the
// real `AssemblyPublisher` against a real owned `Unity.Entities.World`, and live ECS storage read back through the
// target registry. Nothing asserts a managed model of the world.
//
// The gate sentence this suite implements, from `docs/game-core/09-implementation-guide.md` (GC-008):
//
//   "A prepared mount changes multiple actual Entities targets in one visible epoch; an observer sees old or new.
//    Stale plans and prewrite migration failure preserve old state; injected failure after the first write faults
//    without publishing or resuming. Future spawned targets appear fully assembled."
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using GameCore.Contracts;
using GameCore.Planning;
using GameCore.Unity.Fixtures;
using GameCore.Unity.Runtime;
using NUnit.Framework;
using Unity.Entities;

namespace GameCore.Unity.Runtime.Tests.Assembly
{
    [TestFixture]
    public sealed class AssemblyPublisherTests
    {
        private static ulong sessionSequence = 0x4730384153534553UL;

        private List<UnityWorldHost> hosts = new List<UnityWorldHost>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < hosts.Count; i++)
            {
                if (hosts[i].Lifecycle != WorldLifecycleState.Disposed)
                {
                    hosts[i].Stop(new OperationId(hosts[i].World, AssemblyFixtureKeys.Issuer, 9999UL), "fixture teardown");
                }
            }

            hosts.Clear();
            UnityWorldRegistry.ResetAll();
        }

        private sealed class Fixture
        {
            public Fixture(UnityWorldHost world, AssemblyPublisher publisher, AssemblyFixtureApplier applier, AssemblyFixtureGate gate)
            {
                World = world;
                Publisher = publisher;
                Applier = applier;
                Gate = gate;
            }

            public UnityWorldHost World { get; }

            public AssemblyPublisher Publisher { get; }

            public AssemblyFixtureApplier Applier { get; }

            public AssemblyFixtureGate Gate { get; }
        }

        /// <summary>
        /// One owned command-driven world, its real publisher and the two targets that exist before any publication.
        /// The world's initial assembly publishes revision/epoch 1 (05 s2), which is the first publication of the one
        /// series this world and its composition lane share (P-006).
        /// </summary>
        private Fixture CreateFixture(uint capacity = 8U, bool seedTargets = true)
        {
            sessionSequence++;
            var world = new WorldId(new Id128(sessionSequence, 0x0001UL));
            var operation = AssemblyFixtureKeys.Operation(world, 1UL);

            bool created = UnityWorldRegistry.TryCreate(
                FixtureRegistration.CommandDrivenRequest(world, operation, ContentHash.Empty),
                FixtureRegistration.Create(FixtureWorldShape.CommandDriven, false),
                out UnityWorldHost? host,
                out WorldCreateResult result);

            Assert.That(created, Is.True, result.Code + ": " + result.Detail);
            Assert.That(host, Is.Not.Null);
            hosts.Add(host!);

            var applier = new AssemblyFixtureApplier();
            var gate = new AssemblyFixtureGate();
            var publisher = new AssemblyPublisher(
                host!,
                new TargetRegistry(world, capacity),
                AssemblyFixtureRecipes.Catalog(applier),
                AssemblyFixtureRecipes.Migrations(),
                AssemblyFixtureDescriptor.Build());

            var fixture = new Fixture(host!, publisher, applier, gate);

            if (seedTargets)
            {
                AssemblyFixturePlans.SeedTarget(
                    publisher,
                    AssemblyFixtureKeys.Target(1UL),
                    slotValue: 7,
                    slotVersion: 1U);
                AssemblyFixturePlans.SeedTarget(
                    publisher,
                    AssemblyFixtureKeys.Target(2UL),
                    slotValue: 9,
                    slotVersion: 1U);
            }

            return fixture;
        }

        /// <summary>
        /// Adopts the next composition publication of the world's ONE series (P-006) and returns the inert
        /// acquisitions for it. The fixture's world publishes its initial assembly as revision/epoch 1 (05 s2), so
        /// the `ordinal`-th publication of this world is revision/epoch `ordinal + 1`: there is no offset between the
        /// composition publication and the assembly it becomes.
        /// </summary>
        private static InertAcquisitionSet AdoptAndAcquire(
            Fixture fixture,
            ulong ordinal,
            out CompositionRevision laneRevision,
            out AssemblyEpoch laneEpoch)
        {
            laneRevision = new CompositionRevision(ordinal + 1UL);
            laneEpoch = new AssemblyEpoch(ordinal + 1UL);

            Assert.That(
                fixture.Publisher.TryAdoptLanePublication(laneRevision, laneEpoch, out AssemblyEpoch worldEpoch, out DiagnosticCode code),
                Is.True,
                "a publication must belong to an adopted composition publication: " + code);
            Assert.That(worldEpoch, Is.EqualTo(laneEpoch),
                "P-006: the composition publication IS the world's next assembly, with no offset");

            return new InertAcquisitionSet(fixture.Gate, AssemblyFixtureKeys.Operation(fixture.World.World, ordinal));
        }

        private static PlannedPublication MountPlan(
            Fixture fixture,
            ulong laneSequence,
            IReadOnlyList<ProposedMount>? mounts = null,
            IReadOnlyList<LiveSlotState>? liveSlots = null,
            InertAcquisitionSet? acquisitions = null,
            CompositionProposal? proposal = null)
        {
            CompositionProposal effective = proposal ??
                AssemblyFixturePlans.MountProposal(fixture.Publisher, laneSequence, mounts);

            return AssemblyFixturePlans.Plan(
                fixture.Publisher,
                effective,
                liveSlots: liveSlots,
                acquisitions: acquisitions);
        }

        [Test]
        public void OnePublicationChangesEveryTargetInOneVisibleEpoch()
        {
            Fixture fixture = CreateFixture();
            InertAcquisitionSet acquisitions = AdoptAndAcquire(
                fixture,
                1UL,
                out CompositionRevision laneRevision,
                out AssemblyEpoch laneEpoch);

            // Two staged leases: they must stay inert while the plan is only prepared, and open at publication (P-029).
            Assert.That(
                acquisitions.TryAcquire(new ResourceKey(new Id128(AssemblyFixtureKeys.Namespace, 0x0900UL)), 64UL, null, out _),
                Is.True);
            Assert.That(
                acquisitions.TryAcquire(new ResourceKey(new Id128(AssemblyFixtureKeys.Namespace, 0x0901UL)), 64UL, null, out _),
                Is.True);
            Assert.That(acquisitions.CanEmitGameplay, Is.False, "a staged lease cannot emit gameplay (P-029)");

            var liveSlots = new List<LiveSlotState>
            {
                AssemblyFixturePlans.QuestSlot(AssemblyFixtureKeys.Target(1UL), 7, 1U),
                AssemblyFixturePlans.QuestSlot(AssemblyFixtureKeys.Target(2UL), 9, 1U),
            };

            PlannedPublication plan = MountPlan(fixture, 1UL, liveSlots: liveSlots, acquisitions: acquisitions);
            Assert.That(plan.IsPrepared, Is.True, plan.State.Describe());

            AssemblyEpoch epochBefore = fixture.World.CurrentEpoch;
            AssemblyPublicationReport report = fixture.Publisher.Publish(plan);

            Assert.That(report.Published, Is.True, report.ToString());
            Assert.That(report.Outcome, Is.EqualTo(Outcome.Published));
            Assert.That(report.WorldEpochBefore, Is.EqualTo(epochBefore));
            Assert.That(
                report.WorldEpochAfter,
                Is.EqualTo(laneEpoch),
                "P-006: the published assembly epoch is exactly the composition epoch the operation reported");
            Assert.That(fixture.World.CurrentEpoch, Is.EqualTo(report.WorldEpochAfter));
            Assert.That(fixture.Publisher.AdoptedLaneEpoch, Is.EqualTo(laneEpoch));
            Assert.That(fixture.Publisher.PublishedRevision, Is.EqualTo(laneRevision),
                "P-006: the published assembly revision is exactly the composition revision the operation reported");
            Assert.That(report.MigratedSlots, Is.EqualTo(2), "both live slots were migrated on scratch (P-029)");
            Assert.That(report.StructuralWrites, Is.GreaterThanOrEqualTo(2), "both targets received a live binding row");

            // One visible epoch: the epoch-bound dispatch table, the bindings and the token all name it.
            PublishedWorldView view = fixture.Publisher.Published;
            Assert.That(view.Epoch, Is.EqualTo(report.WorldEpochAfter));
            Assert.That(view.Token.AssemblyEpoch, Is.EqualTo(report.WorldEpochAfter));
            Assert.That(view.Token.LogicalStepId, Is.EqualTo(LogicalStepId.Zero), "a publication never increments a step (P-006)");
            Assert.That(view.DispatchTable, Is.Not.Null);
            Assert.That(view.DispatchTable!.Epoch, Is.EqualTo(report.WorldEpochAfter));
            Assert.That(view.BoundTargetCount, Is.EqualTo(2));
            Assert.That(view.BindingRowCount, Is.EqualTo(2));
            Assert.That(view.Schedule.Entries.Count, Is.EqualTo(3), "the descriptor's three declared stages (P-039)");
            Assert.That(view.Gates.Count, Is.EqualTo(3));
            Assert.That(view.Rules.Count, Is.EqualTo(1),
                "one rule slot per recipe/scope/capability, not one per target (P-017)");

            // The live storage agrees: each target carries exactly one active row and its migrated state.
            for (int i = 0; i < view.Targets.Count; i++)
            {
                TargetId target = view.Targets[i];
                IReadOnlyList<CapabilityBinding> rows = fixture.Publisher.ReadBindingRows(target);
                Assert.That(rows.Count, Is.EqualTo(1), "one effective row for target " + target.ToString());
                Assert.That(rows[0].Value, Is.EqualTo(3));
                Assert.That(rows[0].Provider, Is.EqualTo(AssemblyFixtureKeys.Provider(1UL)));
                Assert.That(rows[0].IsActive, Is.True);

                IReadOnlyList<TargetSlotState> slots = fixture.Publisher.ReadSlotStates(target);
                Assert.That(slots.Count, Is.EqualTo(1));
                Assert.That(slots[0].SchemaVersion, Is.EqualTo(AssemblyFixtureKeys.QuestSchemaVersion),
                    "the migrated schema version is what live storage now holds (P-032)");
                Assert.That(slots[0].Value, Is.EqualTo(target.Equals(AssemblyFixtureKeys.Target(1UL)) ? 17 : 19),
                    "the pure migration ran on the copied value and replaced the live one");
            }

            // The execution graph was installed at the fence: the published order is the descriptor's order.
            Assert.That(fixture.World.StepGroup.InstalledPlan.Entries.Count, Is.EqualTo(3));
            Assert.That(fixture.World.StepGroup.BoundEpoch, Is.EqualTo(report.WorldEpochAfter));
            Assert.That(fixture.World.Publications.PublishedCount, Is.EqualTo(2),
                "the initial assembly plus the new assembly image (P-030)");
            Assert.That(fixture.Gate.AcquireCount, Is.EqualTo(2));
            Assert.That(fixture.Gate.ReleasedCount, Is.EqualTo(0),
                "a published plan keeps its acquired leases live; only its migration scratch is released (P-029, P-048)");
            Assert.That(plan.Scratch.IsEmpty, Is.True, "the migration scratch is released once the publication is visible");
            Assert.That(plan.State.Phase, Is.EqualTo(PlanPhase.Published));
            Assert.That(acquisitions.CanEmitGameplay, Is.True, "publication is the only moment the staged gates open (P-029)");
        }

        [Test]
        public void AConcurrentObserverNeverSeesAMixedAssembly()
        {
            Fixture fixture = CreateFixture();

            // The observer captures one published view reference per iteration and validates its internal
            // consistency: the epoch, the token, the epoch-bound dispatch table and the rows of every bound target
            // must all describe the same assembly (P-030, TEST-009).
            var observer = new AssemblyObserver(fixture);
            Thread thread = new Thread(observer.Run);
            thread.IsBackground = true;
            thread.Start();

            // Wait for the observer to have taken at least one view, so the publication loop always has a live
            // observer and the assertions below are not timing-dependent.
            for (int spin = 0; spin < 500 && observer.Observations == 0; spin++)
            {
                Thread.Sleep(1);
            }

            Assert.That(observer.Observations, Is.GreaterThan(0), "the observer thread must be reading the view");

            try
            {
                for (ulong sequence = 1UL; sequence <= 6UL; sequence++)
                {
                    InertAcquisitionSet acquisitions = AdoptAndAcquire(
                        fixture,
                        sequence,
                        out _,
                        out AssemblyEpoch laneEpoch);

                    // Each publication raises the priority, so every iteration is a real change of the effective row
                    // (an equal-priority declaration would be a no-op under P-018's rank).
                    IReadOnlyList<ProposedMount> mounts = new List<ProposedMount>
                    {
                        AssemblyFixturePlans.Mount(
                            sequence,
                            capability: AssemblyFixturePlans.LimitCapability(
                                value: 3,
                                priority: 10 + (int)sequence)),
                    };

                    PlannedPublication plan = MountPlan(fixture, sequence, mounts, acquisitions: acquisitions);
                    AssemblyPublicationReport report = fixture.Publisher.Publish(plan);
                    Assert.That(report.Published, Is.True, report.ToString());
                    Assert.That(report.WorldEpochAfter, Is.EqualTo(laneEpoch),
                        "every publication lands on exactly the composition epoch it came from (P-006)");
                    Assert.That(fixture.Publisher.PublishedRevision.Value, Is.EqualTo(fixture.World.CurrentEpoch.Value),
                        "revision and epoch name the same publication (P-006)");
                }
            }
            finally
            {
                observer.Stop();
                thread.Join(2000);
            }

            Assert.That(observer.Observations, Is.GreaterThan(0), "the observer thread ran while publications happened");
            Assert.That(observer.Violations, Is.Empty, string.Join("; ", observer.Violations));
            Assert.That(observer.DistinctEpochs.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(observer.ViolationCount, Is.EqualTo(0));
        }

        [Test]
        public void AStalePlanIsRejectedBeforeAnyWrite()
        {
            Fixture fixture = CreateFixture();
            InertAcquisitionSet acquisitions = AdoptAndAcquire(fixture, 1UL, out _, out AssemblyEpoch laneEpoch);

            CompositionProposal fresh = AssemblyFixturePlans.MountProposal(fixture.Publisher, 1UL);
            CompositionProposal stale = AssemblyFixturePlans.WithStaleBase(
                fresh,
                new CompositionRevision(fixture.Publisher.PublishedRevision.Value + 4UL),
                laneEpoch);

            PlannedPublication plan = AssemblyFixturePlans.Plan(
                fixture.Publisher,
                stale,
                acquisitions: acquisitions);

            AssemblyEpoch epochBefore = fixture.World.CurrentEpoch;
            int imagesBefore = fixture.World.Publications.PublishedCount;
            int rowsBefore = fixture.Publisher.ReadBindingRows(AssemblyFixtureKeys.Target(1UL)).Count;

            AssemblyPublicationReport report = fixture.Publisher.Publish(plan);

            Assert.That(report.Outcome, Is.EqualTo(Outcome.Rejected));
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.StalePlan));
            Assert.That(report.StructuralWrites, Is.EqualTo(0), "a stale plan makes no live write (P-028)");
            Assert.That(report.CrossedLiveWriteBoundary, Is.False);
            Assert.That(fixture.World.CurrentEpoch, Is.EqualTo(epochBefore));
            Assert.That(fixture.World.Publications.PublishedCount, Is.EqualTo(imagesBefore));
            Assert.That(fixture.Publisher.ReadBindingRows(AssemblyFixtureKeys.Target(1UL)).Count, Is.EqualTo(rowsBefore));
            Assert.That(fixture.Publisher.Published.BindingRowCount, Is.EqualTo(0), "the published assembly is the old one");
            Assert.That(plan.State.Phase, Is.EqualTo(PlanPhase.Rejected));

            // A plan whose expected revision matches but whose base epoch is not the published one is equally stale:
            // the plan was prepared against another assembly image (P-028, P-030).
            CompositionProposal staleEpoch = AssemblyFixturePlans.WithStaleBase(
                fresh,
                fixture.Publisher.PublishedRevision,
                new AssemblyEpoch(epochBefore.Value + 5UL));
            PlannedPublication epochPlan = AssemblyFixturePlans.Plan(
                fixture.Publisher,
                staleEpoch,
                acquisitions: new InertAcquisitionSet(fixture.Gate, fresh.Operation));

            AssemblyPublicationReport epochReport = fixture.Publisher.Publish(epochPlan);
            Assert.That(epochReport.Outcome, Is.EqualTo(Outcome.Rejected));
            Assert.That(epochReport.Code, Is.EqualTo(DiagnosticCode.StalePlan),
                "the base epoch is rechecked as well as the revision (P-030)");
            Assert.That(fixture.Publisher.StalePlanCount, Is.EqualTo(2));
        }

        /// <summary>
        /// P-006's one publication series, checked as the equality the orchestrator review asked for: after the join
        /// and after every publication, the composition lane's published revision/epoch ARE the world's published
        /// ones. There is no offset in either direction and no second counter to report.
        /// </summary>
        [Test]
        public void TheCompositionAndPublishedSeriesAreOneAfterEveryPublication()
        {
            Fixture fixture = CreateFixture();

            // After the join: the world published its initial assembly as revision/epoch 1 (05 s2), and a lane joined
            // to it reports exactly that pair — that is what CompositionLaneSeed.InitialAssembly is for.
            Assert.That(fixture.World.CurrentEpoch, Is.EqualTo(AssemblyEpoch.First));
            Assert.That(fixture.World.PublishedCompositionRevision, Is.EqualTo(CompositionRevision.First));
            Assert.That(fixture.Publisher.PublishedEpoch, Is.EqualTo(fixture.World.CurrentEpoch));
            Assert.That(fixture.Publisher.PublishedRevision, Is.EqualTo(fixture.World.PublishedCompositionRevision));

            for (ulong ordinal = 1UL; ordinal <= 3UL; ordinal++)
            {
                InertAcquisitionSet acquisitions = AdoptAndAcquire(
                    fixture,
                    ordinal,
                    out CompositionRevision laneRevision,
                    out AssemblyEpoch laneEpoch);

                // Each publication raises the priority, so every iteration is a real change of the effective row; an
                // identical re-proposal would be a `NoChange` and would publish no epoch at all (P-006, P-018).
                IReadOnlyList<ProposedMount> stronger = new List<ProposedMount>
                {
                    AssemblyFixturePlans.Mount(
                        1UL,
                        capability: AssemblyFixturePlans.LimitCapability(
                            value: 3,
                            priority: 10 + (int)ordinal)),
                };

                AssemblyPublicationReport report = fixture.Publisher.Publish(
                    MountPlan(fixture, ordinal, stronger, acquisitions: acquisitions));
                Assert.That(report.Published, Is.True, report.ToString());

                // Equality, not agreement-by-offset: the numbers published are the numbers the composition
                // publication carried, and the plan's own base recheck saw the same pair before the write.
                Assert.That(report.WorldEpochAfter, Is.EqualTo(laneEpoch));
                Assert.That(fixture.World.CurrentEpoch, Is.EqualTo(laneEpoch));
                Assert.That(fixture.Publisher.PublishedRevision, Is.EqualTo(laneRevision));
                Assert.That(fixture.World.PublishedCompositionRevision, Is.EqualTo(laneRevision));
                Assert.That(
                    AssemblyPublisher.MatchesPublishedAssembly(
                        laneRevision,
                        laneEpoch,
                        fixture.World.PublishedCompositionRevision,
                        fixture.World.CurrentEpoch),
                    Is.True);
                Assert.That(fixture.Publisher.Published.Token.AssemblyEpoch, Is.EqualTo(laneEpoch));
                Assert.That(fixture.Publisher.Published.Revision, Is.EqualTo(laneRevision));
            }

            Assert.That(fixture.Publisher.PublicationCount, Is.EqualTo(1 + 3),
                "the slot counts the initial assembly it was constructed with, plus the three publications");
        }

        /// <summary>
        /// A composition publication that is not the next assembly of the series is refused before any live write, and
        /// one number is never shared by two publications (P-006, P-050).
        /// </summary>
        [Test]
        public void AStaleOrRepeatedCompositionPublicationIsRefusedBeforeAnyWrite()
        {
            Fixture fixture = CreateFixture();

            AssemblyEpoch published = fixture.World.CurrentEpoch;
            CompositionRevision publishedRevision = fixture.Publisher.PublishedRevision;

            // 1. A publication behind the series: the epoch the world already published.
            Assert.That(
                fixture.Publisher.TryAdoptLanePublication(publishedRevision, published, out _, out DiagnosticCode stale),
                Is.False);
            Assert.That(stale, Is.EqualTo(DiagnosticCode.StalePlan));

            // 2. A publication ahead of the series: skipping a number would publish an assembly nobody proposed.
            Assert.That(
                fixture.Publisher.TryAdoptLanePublication(
                    new CompositionRevision(publishedRevision.Value + 2UL),
                    new AssemblyEpoch(published.Value + 2UL),
                    out _,
                    out DiagnosticCode ahead),
                Is.False);
            Assert.That(ahead, Is.EqualTo(DiagnosticCode.StalePlan));

            // 3. Two different numbers in one pair: P-006 increments them together.
            Assert.That(
                fixture.Publisher.TryAdoptLanePublication(
                    new CompositionRevision(publishedRevision.Value + 1UL),
                    new AssemblyEpoch(published.Value + 2UL),
                    out _,
                    out DiagnosticCode inconsistent),
                Is.False);
            Assert.That(inconsistent, Is.EqualTo(DiagnosticCode.UnsupportedVersion));

            Assert.That(fixture.World.CurrentEpoch, Is.EqualTo(published), "no refused adoption moves the world");

            // 4. The real next publication is adopted and published; the same number can never be used twice.
            InertAcquisitionSet first = AdoptAndAcquire(fixture, 1UL, out CompositionRevision laneRevision, out AssemblyEpoch laneEpoch);
            AssemblyPublicationReport report = fixture.Publisher.Publish(
                MountPlan(fixture, 1UL, acquisitions: first));
            Assert.That(report.Published, Is.True, report.ToString());
            Assert.That(report.WorldEpochAfter, Is.EqualTo(laneEpoch));

            Assert.That(
                fixture.Publisher.TryAdoptLanePublication(laneRevision, laneEpoch, out _, out DiagnosticCode repeated),
                Is.False,
                "a publication that already produced an assembly cannot produce a second one (P-006)");
            Assert.That(repeated, Is.EqualTo(DiagnosticCode.StalePlan));

            // 5. A plan published without adopting a composition publication is refused as stale as well. The
            //    proposal is a real change (a higher-priority declaration), so the refusal can only come from the
            //    missing adoption — a no-op plan would return NoChange earlier and prove nothing.
            IReadOnlyList<ProposedMount> stronger = new List<ProposedMount>
            {
                AssemblyFixturePlans.Mount(
                    2UL,
                    capability: AssemblyFixturePlans.LimitCapability(value: 5, priority: 50)),
            };

            CompositionProposal orphanProposal = AssemblyFixturePlans.MountProposal(
                fixture.Publisher,
                2UL,
                stronger);

            PlannedPublication orphan = AssemblyFixturePlans.Plan(
                fixture.Publisher,
                orphanProposal,
                acquisitions: new InertAcquisitionSet(
                    fixture.Gate,
                    AssemblyFixtureKeys.Operation(fixture.World.World, 2UL)));

            Assert.That(orphan.IsPrepared, Is.True, "the stronger declaration is a real change: " + orphan.State.Describe());

            AssemblyEpoch before = fixture.World.CurrentEpoch;
            int imagesBefore = fixture.World.Publications.PublishedCount;
            AssemblyPublicationReport refused = fixture.Publisher.Publish(orphan);

            Assert.That(refused.Outcome, Is.EqualTo(Outcome.Rejected),
                "a publication that belongs to no composition publication cannot be an assembly (P-006)");
            Assert.That(refused.Code, Is.EqualTo(DiagnosticCode.StalePlan));
            Assert.That(refused.StructuralWrites, Is.EqualTo(0));
            Assert.That(refused.CrossedLiveWriteBoundary, Is.False);
            Assert.That(fixture.World.CurrentEpoch, Is.EqualTo(before));
            Assert.That(fixture.World.Publications.PublishedCount, Is.EqualTo(imagesBefore));
            Assert.That(orphan.State.Phase, Is.EqualTo(PlanPhase.Rejected));
        }

        [Test]
        public void APrewriteMigrationFailurePreservesTheOldAssemblyAndKeepsRunning()
        {
            Fixture fixture = CreateFixture();
            InertAcquisitionSet acquisitions = AdoptAndAcquire(fixture, 1UL, out _, out _);
            Assert.That(
                acquisitions.TryAcquire(new ResourceKey(new Id128(AssemblyFixtureKeys.Namespace, 0x0910UL)), 64UL, null, out _),
                Is.True);

            var liveSlots = new List<LiveSlotState>
            {
                AssemblyFixturePlans.QuestSlot(AssemblyFixtureKeys.Target(1UL), 7, 1U),
                AssemblyFixturePlans.QuestSlot(AssemblyFixtureKeys.Target(2UL), 9, 1U),
            };

            PlannedPublication plan = MountPlan(fixture, 1UL, liveSlots: liveSlots, acquisitions: acquisitions);
            Assert.That(plan.IsPrepared, Is.True, plan.State.Describe());

            // The injection fires inside the migration stage, i.e. before the first live write (P-029).
            fixture.Publisher.Faults.FailDuringMigration = true;

            AssemblyEpoch epochBefore = fixture.World.CurrentEpoch;
            AssemblyPublicationReport report = fixture.Publisher.Publish(plan);

            Assert.That(report.Outcome, Is.EqualTo(Outcome.Rejected));
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.ResourceUnavailable));
            Assert.That(report.StructuralWrites, Is.EqualTo(0));
            Assert.That(report.CrossedLiveWriteBoundary, Is.False);
            Assert.That(fixture.Publisher.Faults.MigrationInjections, Is.EqualTo(1));
            Assert.That(fixture.World.Lifecycle, Is.EqualTo(WorldLifecycleState.Running),
                "a prewrite failure leaves the old assembly running (P-029)");

            Assert.That(fixture.Gate.ReleasedCount, Is.EqualTo(1),
                "a prewrite failure releases its staged acquisitions in reverse order (P-029)");
            Assert.That(acquisitions.RetainedLeaseIds(), Is.Empty);
            Assert.That(acquisitions.CanEmitGameplay, Is.False);
            Assert.That(fixture.World.CurrentEpoch, Is.EqualTo(epochBefore), "no epoch is published");
            Assert.That(fixture.Publisher.Published.BindingRowCount, Is.EqualTo(0));
            Assert.That(fixture.Publisher.PrewriteFailureCount, Is.EqualTo(1));
            Assert.That(plan.State.Phase, Is.EqualTo(PlanPhase.Rejected));
            Assert.That(plan.State.HasCrossedLiveWriteBoundary, Is.False);

            // No target gained a row and no state was replaced, so the old assembly is intact (05 s4 `Rejected`).
            for (int i = 0; i < 2; i++)
            {
                TargetId target = AssemblyFixtureKeys.Target((ulong)(i + 1));
                Assert.That(fixture.Publisher.ReadBindingRows(target), Is.Empty);
                IReadOnlyList<TargetSlotState> slots = fixture.Publisher.ReadSlotStates(target);
                Assert.That(slots[0].SchemaVersion, Is.EqualTo(1U), "live state kept its schema version");
                Assert.That(slots[0].Value, Is.EqualTo(i == 0 ? 7 : 9));
            }

            // The world still executes and still accepts a later, correct publication.
            fixture.World.NotifyCommandAdmitted(1U);
            fixture.World.PumpFrame(1_000_000UL);
            Assert.That(fixture.World.CurrentStep, Is.EqualTo(LogicalStepId.First), "the old assembly keeps running");

            fixture.Publisher.Faults.FailDuringMigration = false;
            InertAcquisitionSet retry = AdoptAndAcquire(fixture, 2UL, out _, out _);
            PlannedPublication retryPlan = MountPlan(fixture, 2UL, liveSlots: liveSlots, acquisitions: retry);
            AssemblyPublicationReport retryReport = fixture.Publisher.Publish(retryPlan);

            Assert.That(retryReport.Published, Is.True, retryReport.ToString());
            Assert.That(retryReport.MigratedSlots, Is.EqualTo(2));
            Assert.That(fixture.Publisher.Published.BindingRowCount, Is.EqualTo(2));
        }

        [Test]
        public void APostwriteFailureFaultsTheWorldWithoutPublishingOrResuming()
        {
            Fixture fixture = CreateFixture();
            InertAcquisitionSet acquisitions = AdoptAndAcquire(fixture, 1UL, out _, out _);

            var liveSlots = new List<LiveSlotState>
            {
                AssemblyFixturePlans.QuestSlot(AssemblyFixtureKeys.Target(1UL), 7, 1U),
                AssemblyFixturePlans.QuestSlot(AssemblyFixtureKeys.Target(2UL), 9, 1U),
            };

            PlannedPublication plan = MountPlan(fixture, 1UL, liveSlots: liveSlots, acquisitions: acquisitions);
            fixture.Publisher.Faults.FailAfterFirstLiveWrite = true;

            AssemblyEpoch epochBefore = fixture.World.CurrentEpoch;
            int imagesBefore = fixture.World.Publications.PublishedCount;
            int faultsBefore = fixture.World.FaultCount;

            AssemblyPublicationReport report = fixture.Publisher.Publish(plan);

            Assert.That(report.Outcome, Is.EqualTo(Outcome.Faulted));
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.ApplyFault));
            Assert.That(report.CrossedLiveWriteBoundary, Is.True);
            Assert.That(report.PublishedToken, Is.Null, "no image is published after a postwrite fault (P-031)");
            Assert.That(report.StructuralWrites, Is.GreaterThan(0), "the failure happened after live writes");
            Assert.That(fixture.Publisher.Faults.PostWriteInjections, Is.EqualTo(1));

            Assert.That(fixture.World.Lifecycle, Is.EqualTo(WorldLifecycleState.Faulted));
            Assert.That(fixture.World.FaultCount, Is.EqualTo(faultsBefore + 1));
            Assert.That(fixture.World.FaultCode, Is.EqualTo(DiagnosticCode.ApplyFault));
            Assert.That(fixture.World.CurrentEpoch, Is.EqualTo(epochBefore), "no epoch is published (P-031)");
            Assert.That(fixture.World.Publications.PublishedCount, Is.EqualTo(imagesBefore));
            Assert.That(fixture.Publisher.Published.Epoch, Is.EqualTo(epochBefore),
                "the last committed view stays the only safe observation");
            Assert.That(plan.State.Phase, Is.EqualTo(PlanPhase.Faulted));
            Assert.That(fixture.Publisher.PostwriteFaultCount, Is.EqualTo(1));

            // The world accepts no further work: neither a step nor another publication.
            fixture.World.NotifyCommandAdmitted(1U);
            WorldPumpResult pump = fixture.World.PumpFrame(2_000_000UL);
            Assert.That(pump.Pumped, Is.False, "a faulted world admits nothing (P-031)");
            Assert.That(fixture.World.CurrentStep, Is.EqualTo(LogicalStepId.Zero));

            InertAcquisitionSet later = new InertAcquisitionSet(
                fixture.Gate,
                AssemblyFixtureKeys.Operation(fixture.World.World, 2UL));
            PlannedPublication next = MountPlan(fixture, 1UL, acquisitions: later);
            AssemblyPublicationReport second = fixture.Publisher.Publish(next);
            Assert.That(second.Outcome, Is.EqualTo(Outcome.Rejected));
            Assert.That(second.Code, Is.EqualTo(DiagnosticCode.ApplyFault));
            Assert.That(second.CrossedLiveWriteBoundary, Is.False);
        }

        [Test]
        public void AFutureSpawnAppearsFullyAssembledInItsFirstVisibleEpoch()
        {
            Fixture fixture = CreateFixture();

            // First publication: mount provider 1 for the two existing targets (P-013).
            InertAcquisitionSet first = AdoptAndAcquire(fixture, 1UL, out _, out _);
            AssemblyPublicationReport mounted = fixture.Publisher.Publish(
                MountPlan(fixture, 1UL, acquisitions: first));
            Assert.That(mounted.Published, Is.True, mounted.ToString());

            // Second publication: a second provider joins, so the derived rules of the recipe change.
            InertAcquisitionSet second = AdoptAndAcquire(fixture, 2UL, out _, out _);
            IReadOnlyList<ProposedMount> mounts = new List<ProposedMount>
            {
                AssemblyFixturePlans.Mount(
                    2UL,
                    capability: AssemblyFixturePlans.LimitCapability(value: 2, priority: 20)),
            };
            AssemblyPublicationReport extended = fixture.Publisher.Publish(
                MountPlan(fixture, 2UL, mounts, acquisitions: second));
            Assert.That(extended.Published, Is.True, extended.ToString());

            // A target that did not exist when either publication happened. It must appear with its complete
            // effective assembly in one epoch, not progressively wired over later frames (P-024).
            // The spawn belongs to the next composition publication of the one series (revision/epoch 4 here), and
            // it is validated against the published revision it was prepared from (P-006, P-024).
            InertAcquisitionSet spawn = AdoptAndAcquire(fixture, 3UL, out CompositionRevision spawnRevision, out AssemblyEpoch spawnEpoch);
            var request = new AssemblySpawnRequest(
                AssemblyFixtureKeys.CardRecipe,
                AssemblyFixtureKeys.RootScope,
                AssemblyFixtureKeys.Target(3UL),
                AssemblyFixtureKeys.Operation(fixture.World.World, 3UL),
                fixture.Publisher.PublishedRevision,
                spawnRevision,
                spawnEpoch);

            AssemblyPublicationReport spawned = fixture.Publisher.Spawn(request);

            Assert.That(spawned.Published, Is.True, spawned.ToString());
            Assert.That(fixture.Publisher.SpawnedCount, Is.EqualTo(1));
            Assert.That(spawned.WorldEpochAfter, Is.EqualTo(spawnEpoch),
                "the spawn lands on exactly the composition epoch its request named (P-006)");

            Entity entity = fixture.Publisher.Registry.EntityOf(AssemblyFixtureKeys.Target(3UL));
            Assert.That(entity, Is.Not.EqualTo(Entity.Null));
            Assert.That(fixture.Applier.AppliedCount, Is.EqualTo(1), "the recipe's base layout was installed once");

            IReadOnlyList<CapabilityBinding> rows = fixture.Publisher.ReadBindingRows(AssemblyFixtureKeys.Target(3UL));
            Assert.That(rows.Count, Is.EqualTo(1), "the spawned target carries every currently derived binding at once");
            Assert.That(rows[0].Value, Is.EqualTo(2), "the winning candidate of the current rules (P-018)");
            Assert.That(rows[0].Provider, Is.EqualTo(AssemblyFixtureKeys.Provider(2UL)));
            Assert.That(rows[0].IsActive, Is.True);

            // Its stamp is published at the epoch of the spawn, and the published binding table already lists it.
            AssemblyStamp stamp = fixture.World.EntityWorld.EntityManager.GetComponentData<AssemblyStamp>(entity);
            Assert.That(stamp.Published, Is.EqualTo(1U), "a target is only visible once its assembly is complete (P-024)");
            Assert.That(stamp.AssemblyEpoch, Is.EqualTo(spawned.WorldEpochAfter.Value));
            Assert.That(fixture.Publisher.Published.Bindings.HasTarget(AssemblyFixtureKeys.Target(3UL)), Is.True);
            Assert.That(fixture.Publisher.Published.BindingRowCount, Is.EqualTo(3));

            // A spawn prepared against an older composition revision is refused rather than activated stale (P-024):
            // composition revision 1 is the world's initial assembly, not the next publication.
            InertAcquisitionSet stale = AdoptAndAcquire(fixture, 4UL, out CompositionRevision staleRevision, out AssemblyEpoch staleEpoch);
            var staleRequest = new AssemblySpawnRequest(
                AssemblyFixtureKeys.CardRecipe,
                AssemblyFixtureKeys.RootScope,
                AssemblyFixtureKeys.Target(4UL),
                AssemblyFixtureKeys.Operation(fixture.World.World, 4UL),
                new CompositionRevision(1UL),
                staleRevision,
                staleEpoch);
            Assert.That(staleRequest.PreparedRevision.Equals(fixture.Publisher.PublishedRevision), Is.False,
                "the request deliberately names a revision the world no longer publishes (P-024)");

            AssemblyPublicationReport refused = fixture.Publisher.Spawn(staleRequest);
            Assert.That(refused.Outcome, Is.EqualTo(Outcome.Rejected));
            Assert.That(refused.Code, Is.EqualTo(DiagnosticCode.StalePlan));
            Assert.That(fixture.Publisher.SpawnRejectionCount, Is.EqualTo(1));
            Assert.That(fixture.Publisher.Registry.IsLive(AssemblyFixtureKeys.Target(4UL)), Is.False,
                "a refused spawn leaves no target behind");
        }

        [Test]
        public void ADespawnedHandleIsRejectedForeverAndOnlyItsOwnStorageIsDestroyed()
        {
            Fixture fixture = CreateFixture();

            InertAcquisitionSet adopt = AdoptAndAcquire(fixture, 1UL, out _, out _);
            AssemblyPublicationReport mounted = fixture.Publisher.Publish(MountPlan(fixture, 1UL, acquisitions: adopt));
            Assert.That(mounted.Published, Is.True, mounted.ToString());

            Assert.That(fixture.Publisher.Registry.TryGetHandle(AssemblyFixtureKeys.Target(1UL), out TargetHandle handle), Is.True);
            Assert.That(fixture.Publisher.Registry.TryResolveTarget(AssemblyFixtureKeys.Target(1UL), out _, out Entity entity), Is.True);

            InertAcquisitionSet despawnAdopt = AdoptAndAcquire(fixture, 2UL, out _, out AssemblyEpoch despawnEpoch);
            AssemblyPublicationReport despawned = fixture.Publisher.Despawn(
                handle,
                AssemblyFixtureKeys.Operation(fixture.World.World, 20UL),
                new CompositionRevision(despawnEpoch.Value),
                despawnEpoch);

            Assert.That(despawned.Published, Is.True, despawned.ToString());
            Assert.That(fixture.Publisher.DespawnedCount, Is.EqualTo(1));
            Assert.That(fixture.World.EntityWorld.EntityManager.Exists(entity), Is.False,
                "despawn destroys the target's own recipe-owned entity (P-024)");
            Assert.That(fixture.Publisher.Registry.IsLive(AssemblyFixtureKeys.Target(1UL)), Is.False);
            Assert.That(fixture.Publisher.Published.Bindings.HasTarget(AssemblyFixtureKeys.Target(1UL)), Is.False,
                "its contributions retracted from the published assembly");

            // The other target is untouched, including its migrated state (P-033: unrelated state survives).
            Assert.That(fixture.Publisher.Published.Bindings.HasTarget(AssemblyFixtureKeys.Target(2UL)), Is.True);
            Assert.That(fixture.Publisher.ReadSlotStates(AssemblyFixtureKeys.Target(2UL)).Count, Is.EqualTo(1));

            // The retired handle is stale for every later caller, and never resolves to a reused slot (P-005).
            Assert.That(fixture.Publisher.TryResolveHandle(handle, out _, out _), Is.False);
            Assert.That(fixture.Publisher.StaleHandleRejectionCount, Is.GreaterThanOrEqualTo(1));

            AssemblyPublicationReport secondDespawn = fixture.Publisher.Despawn(
                handle,
                AssemblyFixtureKeys.Operation(fixture.World.World, 21UL),
                new CompositionRevision(3UL),
                new AssemblyEpoch(3UL));
            Assert.That(secondDespawn.Outcome, Is.EqualTo(Outcome.Rejected));
            Assert.That(secondDespawn.Code, Is.EqualTo(DiagnosticCode.StaleHandle));
            Assert.That(secondDespawn.CrossedLiveWriteBoundary, Is.False);

            // Re-registering the same stable identity yields a new generation, so the old handle is still dead.
            AssemblyFixturePlans.SeedTarget(fixture.Publisher, AssemblyFixtureKeys.Target(1UL), 1, 1U);
            Assert.That(fixture.Publisher.Registry.TryGetHandle(AssemblyFixtureKeys.Target(1UL), out TargetHandle recreated), Is.True);
            Assert.That(recreated.Generation, Is.GreaterThan(handle.Generation));
            Assert.That(fixture.Publisher.TryResolveHandle(handle, out _, out _), Is.False);
        }

        [Test]
        public void APlanThatChangesNothingPublishesNoEpoch()
        {
            Fixture fixture = CreateFixture();

            InertAcquisitionSet adopt = AdoptAndAcquire(fixture, 1UL, out _, out _);
            AssemblyPublicationReport mounted = fixture.Publisher.Publish(MountPlan(fixture, 1UL, acquisitions: adopt));
            Assert.That(mounted.Published, Is.True, mounted.ToString());

            AssemblyEpoch epochBefore = fixture.World.CurrentEpoch;
            int imagesBefore = fixture.World.Publications.PublishedCount;

            InertAcquisitionSet repeat = AdoptAndAcquire(fixture, 2UL, out _, out _);
            PlannedPublication plan = MountPlan(fixture, 2UL, acquisitions: repeat);
            AssemblyPublicationReport report = fixture.Publisher.Publish(plan);

            Assert.That(report.Outcome, Is.EqualTo(Outcome.NoChange));
            Assert.That(fixture.World.CurrentEpoch, Is.EqualTo(epochBefore), "NoChange increments nothing (P-006)");
            Assert.That(fixture.World.Publications.PublishedCount, Is.EqualTo(imagesBefore));
            Assert.That(report.WorldEpochAfter, Is.EqualTo(epochBefore));
        }

        /// <summary>
        /// The concurrent observer of the publication suite: it captures one published view reference per iteration
        /// and validates that the view is internally consistent. A mixture of old and new assembly would show as a
        /// violation, which is the property P-030 requires and TEST-009 probes.
        /// </summary>
        private sealed class AssemblyObserver
        {
            private readonly Fixture fixture;
            private readonly List<string> violations = new List<string>();
            private readonly List<ulong> epochs = new List<ulong>();
            private volatile bool running = true;

            public AssemblyObserver(Fixture fixture)
            {
                this.fixture = fixture;
            }

            public int Observations { get; private set; }

            /// <summary>Count of recorded consistency violations; the observer's whole point (P-030).</summary>
            public int ViolationCount { get; private set; }

            public IReadOnlyList<string> Violations => violations;

            public IReadOnlyList<ulong> DistinctEpochs => epochs;

            public void Run()
            {
                while (running)
                {
                    PublishedWorldView view = fixture.Publisher.Published;
                    Observations++;

                    // One reference, so every field below belongs to the same assembly.
                    if (!view.Token.AssemblyEpoch.Equals(view.Epoch))
                    {
                        Record("token epoch " + view.Token.AssemblyEpoch.Value + " != view epoch " + view.Epoch.Value);
                    }

                    if (view.DispatchTable != null && !view.DispatchTable.Epoch.Equals(view.Epoch))
                    {
                        Record("dispatch table epoch does not match the view epoch");
                    }

                    if (view.Schedule.Entries.Count != 0 && view.Gates.Count != view.Schedule.Entries.Count)
                    {
                        Record("gate table and schedule disagree (" + view.Gates.Count + " vs " + view.Schedule.Entries.Count + ")");
                    }

                    for (int i = 0; i < view.Bindings.Rows.Count; i++)
                    {
                        TargetBindingRow row = view.Bindings.Rows[i];
                        if (!view.Bindings.TryGet(row.Target, row.Capability, row.OutputSlot, out TargetBindingRow found)
                            || !found.HasSameContent(row))
                        {
                            Record("binding table is internally inconsistent for " + row.Target.ToString());
                        }
                    }

                    bool known = false;
                    for (int i = 0; i < epochs.Count; i++)
                    {
                        if (epochs[i] == view.Epoch.Value)
                        {
                            known = true;
                            break;
                        }
                    }

                    if (!known)
                    {
                        epochs.Add(view.Epoch.Value);
                    }

                    // A published view whose epoch is ahead of the world's mirror would be a mixed publication.
                    if (view.Epoch.CompareTo(fixture.World.CurrentEpoch) > 0)
                    {
                        Record("published view is ahead of the world epoch");
                    }
                }
            }

            public void Stop() => running = false;

            private void Record(string violation)
            {
                lock (violations)
                {
                    if (violations.Count < 8)
                    {
                        violations.Add(violation);
                    }
                }

                ViolationCount++;
            }
        }
    }
}

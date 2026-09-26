// Directed checkpoint schema migration planning tests (GC-018). Normative sources: P-054 ("generated
// serializers and registered directed version migrations replace reflection-based type construction. Migration
// paths must be unique for a requested source/target pair; ambiguity is rejected") and 05 s6 ("migration
// registration is a directed graph per schema ... two possible paths to the same destination reject").
//
// Planning runs no migration: every test asserts the *answer* a restore gets before it writes anything, so a
// silent pick or a reverse traversal would be visible here.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using NUnit.Framework;

namespace GameCore.Contracts.Tests
{
    /// <summary>One registered step of the test graphs; the planner only reads its key, from and to.</summary>
    internal sealed class TestMigrationStep : ISchemaMigrationStep
    {
        internal TestMigrationStep(FactoryKey key, SchemaRef from, SchemaRef to)
        {
            Key = key;
            From = from;
            To = to;
        }

        public FactoryKey Key { get; }

        public SchemaRef From { get; }

        public SchemaRef To { get; }
    }

    [TestFixture]
    public sealed class CheckpointMigrationTests
    {
        private static readonly Id128 SchemaKey = new Id128(0x7300000000000001UL, 0x7300000000000002UL);
        private static readonly Id128 OtherSchemaKey = new Id128(0x7400000000000001UL, 0x7400000000000002UL);

        private static SchemaRef Version(uint version) => new SchemaRef(new SchemaId(SchemaKey), version);

        private static SchemaRef OtherVersion(uint version) => new SchemaRef(new SchemaId(OtherSchemaKey), version);

        private static FactoryKey StepKey(ulong ordinal) =>
            new FactoryKey(CheckpointTestRecords.Id(ordinal + 3000UL), 1U);

        private static TestMigrationStep Step(uint from, uint to, ulong keyOrdinal) =>
            new TestMigrationStep(StepKey(keyOrdinal), Version(from), Version(to));

        private static CheckpointMigrationRegistry Registry(params ISchemaMigrationStep[] steps) =>
            new CheckpointMigrationRegistry(steps);

        [Test]
        public void PlanningAVersionAgainstItselfIsCurrent()
        {
            var registry = new CheckpointMigrationRegistry(null);
            MigrationPlan plan = registry.Plan(Version(2), Version(2));

            Assert.That(plan.Outcome, Is.EqualTo(MigrationPlanOutcome.Current));
            Assert.That(plan.IsRunnable, Is.True);
            Assert.That(plan.RequiresMigration, Is.False);
            Assert.That(plan.Steps, Is.Empty);
            Assert.That(plan.PathCount, Is.EqualTo(0));
            Assert.That(plan.Code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(plan.From, Is.EqualTo(Version(2)));
            Assert.That(plan.To, Is.EqualTo(Version(2)));
            Assert.That(plan.SchemaId, Is.EqualTo(new SchemaId(SchemaKey)));
            Assert.That(plan.Detail, Is.Not.Empty);

            // A registered chain past the current version does not make the current version require a migration.
            CheckpointMigrationRegistry withSteps = Registry(Step(1, 2, 1));
            MigrationPlan stillCurrent = withSteps.Plan(Version(2), Version(2));
            Assert.That(stillCurrent.Outcome, Is.EqualTo(MigrationPlanOutcome.Current));
            Assert.That(stillCurrent.Steps, Is.Empty);
        }

        [Test]
        public void AUniqueChainIsPlannedInExecutionOrder()
        {
            TestMigrationStep first = Step(1, 2, 1);
            TestMigrationStep second = Step(2, 3, 2);
            CheckpointMigrationRegistry registry = Registry(first, second);

            Assert.That(registry.IsWellFormed, Is.True);
            Assert.That(registry.Steps.Count, Is.EqualTo(2));
            Assert.That(registry.Rejections, Is.Empty);
            Assert.That(registry.DuplicateKeys, Is.Empty);

            MigrationPlan plan = registry.Plan(Version(1), Version(3));
            Assert.That(plan.Outcome, Is.EqualTo(MigrationPlanOutcome.Unique));
            Assert.That(plan.IsRunnable, Is.True);
            Assert.That(plan.RequiresMigration, Is.True);
            Assert.That(plan.Code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(plan.PathCount, Is.EqualTo(1));
            Assert.That(plan.Steps.Count, Is.EqualTo(2));
            Assert.That(plan.Steps[0], Is.SameAs(first));
            Assert.That(plan.Steps[1], Is.SameAs(second));
            Assert.That(plan.Steps[0].From.Version, Is.EqualTo(1U));
            Assert.That(plan.Steps[0].To.Version, Is.EqualTo(2U));
            Assert.That(plan.Steps[1].From.Version, Is.EqualTo(2U));
            Assert.That(plan.Steps[1].To.Version, Is.EqualTo(3U));
            Assert.That(plan.From, Is.EqualTo(Version(1)));
            Assert.That(plan.To, Is.EqualTo(Version(3)));

            // Each registered pair is runnable on its own as well.
            Assert.That(registry.Plan(Version(1), Version(2)).Outcome, Is.EqualTo(MigrationPlanOutcome.Unique));
            Assert.That(registry.Plan(Version(2), Version(3)).Outcome, Is.EqualTo(MigrationPlanOutcome.Unique));
        }

        [Test]
        public void TwoChainsToTheSameDestinationAreAmbiguous()
        {
            CheckpointMigrationRegistry registry = Registry(Step(1, 3, 1), Step(1, 2, 2), Step(2, 3, 3));

            Assert.That(registry.IsWellFormed, Is.True);
            MigrationPlan plan = registry.Plan(Version(1), Version(3));

            Assert.That(plan.Outcome, Is.EqualTo(MigrationPlanOutcome.Ambiguous));
            Assert.That(plan.PathCount, Is.EqualTo(2));
            Assert.That(plan.PathCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(plan.IsRunnable, Is.False, "the caller is never asked to pick a path");
            Assert.That(plan.RequiresMigration, Is.False);
            Assert.That(plan.Steps, Is.Empty);
            Assert.That(plan.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(plan.Detail, Does.Contain("distinct chains"));
        }

        [Test]
        public void TwoDistinctStepsFromTheRequestedSourceAreAmbiguous()
        {
            // Two different v2->v3 steps give the requested v2->v3 pair two distinct chains, which is exactly the
            // ambiguity P-054 rejects: "migration paths must be unique for a requested source/target pair". The
            // catalog must resolve this at registration or refuse the pair; it must never pick a step.
            CheckpointMigrationRegistry registry = Registry(Step(1, 2, 1), Step(2, 3, 2), Step(2, 3, 3));

            Assert.That(registry.IsWellFormed, Is.True);
            MigrationPlan plan = registry.Plan(Version(2), Version(3));

            Assert.That(plan.Outcome, Is.EqualTo(MigrationPlanOutcome.Ambiguous));
            Assert.That(plan.PathCount, Is.EqualTo(2));
            Assert.That(plan.IsRunnable, Is.False, "the caller is never asked to pick a path");
            Assert.That(plan.RequiresMigration, Is.False);
            Assert.That(plan.Steps, Is.Empty);
            Assert.That(plan.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(plan.From, Is.EqualTo(Version(2)));
            Assert.That(plan.To, Is.EqualTo(Version(3)));
            Assert.That(plan.Detail, Does.Contain("distinct chains"));
        }

        [Test]
        public void ARouteThatForksBelowTheRequestedSourceDoesNotMakeThePairAmbiguous()
        {
            // The triangle {1->2, 2->3, 1->3} carries two chains into version 3, but only one of them starts at
            // v2. P-054 scopes uniqueness to the requested pair, so v2->v3 is planned as the single v2->v3 step
            // and the unrequested v1->v3 route does not refuse it.
            CheckpointMigrationRegistry registry = Registry(Step(1, 2, 1), Step(2, 3, 2), Step(1, 3, 3));

            Assert.That(registry.IsWellFormed, Is.True);
            MigrationPlan plan = registry.Plan(Version(2), Version(3));

            Assert.That(plan.Outcome, Is.EqualTo(MigrationPlanOutcome.Unique));
            Assert.That(plan.IsRunnable, Is.True);
            Assert.That(plan.RequiresMigration, Is.True);
            Assert.That(plan.PathCount, Is.EqualTo(1));
            Assert.That(plan.Steps.Count, Is.EqualTo(1));
            Assert.That(plan.Steps[0].From.Version, Is.EqualTo(2U));
            Assert.That(plan.Steps[0].To.Version, Is.EqualTo(3U));
            Assert.That(plan.From, Is.EqualTo(Version(2)));
            Assert.That(plan.To, Is.EqualTo(Version(3)));

            // The same graph requested from v1 does carry two chains from the requested source, so that pair
            // stays ambiguous.
            MigrationPlan fromTheRoot = registry.Plan(Version(1), Version(3));
            Assert.That(fromTheRoot.Outcome, Is.EqualTo(MigrationPlanOutcome.Ambiguous));
            Assert.That(fromTheRoot.PathCount, Is.EqualTo(2));
        }

        [Test]
        public void ASourceTheUniqueChainSkipsIsUnreachable()
        {
            // One direct v1->v3 step: no chain starts at v2, so a document declaring v2 has no path even though
            // the destination is reached from v1.
            CheckpointMigrationRegistry registry = Registry(Step(1, 3, 1));

            MigrationPlan plan = registry.Plan(Version(2), Version(3));
            Assert.That(plan.Outcome, Is.EqualTo(MigrationPlanOutcome.Unreachable));
            Assert.That(plan.Code, Is.EqualTo(DiagnosticCode.MigrationRequired));
            Assert.That(plan.IsRunnable, Is.False);
            Assert.That(plan.Steps, Is.Empty);
            Assert.That(plan.PathCount, Is.EqualTo(0));
            Assert.That(plan.Detail, Does.Contain("no registered chain"));
        }

        [Test]
        public void ADestinationNoRegisteredChainReachesIsUnreachable()
        {
            CheckpointMigrationRegistry registry = Registry(Step(1, 2, 1));
            MigrationPlan plan = registry.Plan(Version(1), Version(3));

            Assert.That(plan.Outcome, Is.EqualTo(MigrationPlanOutcome.Unreachable));
            Assert.That(plan.Code, Is.EqualTo(DiagnosticCode.MigrationRequired));
            Assert.That(plan.IsRunnable, Is.False);
            Assert.That(plan.RequiresMigration, Is.False);
            Assert.That(plan.Steps, Is.Empty);
            Assert.That(plan.PathCount, Is.EqualTo(0));
            Assert.That(plan.Detail, Does.Contain("no registered chain"));
        }

        [Test]
        public void AVersionMoveWithNoRegisteredStepForThatSchemaIsUnreachable()
        {
            var registry = new CheckpointMigrationRegistry(null);

            MigrationPlan plan = registry.Plan(Version(1), Version(2));
            Assert.That(plan.Outcome, Is.EqualTo(MigrationPlanOutcome.Unreachable));
            Assert.That(plan.Code, Is.EqualTo(DiagnosticCode.MigrationRequired));
            Assert.That(plan.Detail, Does.Contain("no migration step is registered"));
        }

        [Test]
        public void ADowngradeIsNeverATraversalOfAForwardStep()
        {
            CheckpointMigrationRegistry registry = Registry(Step(1, 2, 1));

            MigrationPlan plan = registry.Plan(Version(2), Version(1));
            Assert.That(plan.Outcome, Is.EqualTo(MigrationPlanOutcome.Unreachable));
            Assert.That(plan.Code, Is.EqualTo(DiagnosticCode.MigrationRequired));
            Assert.That(plan.Steps, Is.Empty);
            Assert.That(plan.PathCount, Is.EqualTo(0));
            Assert.That(plan.Detail, Does.Contain("forward"));

            // The graph is directed forward only, so a registered reverse step is refused at registration and no
            // reverse edge can ever exist.
            CheckpointMigrationRegistry withReverse = Registry(Step(1, 2, 1), Step(2, 1, 2));
            Assert.That(withReverse.Steps.Count, Is.EqualTo(1));
            Assert.That(withReverse.Rejections.Count, Is.EqualTo(1));
            Assert.That(withReverse.IsWellFormed, Is.False);
            Assert.That(
                withReverse.Plan(Version(2), Version(1)).Outcome,
                Is.EqualTo(MigrationPlanOutcome.Unreachable));
        }

        [Test]
        public void PlanningAcrossSchemaIdentitiesIsRefused()
        {
            CheckpointMigrationRegistry registry = Registry(Step(1, 2, 1));

            MigrationPlan plan = registry.Plan(OtherVersion(1), Version(1));
            Assert.That(plan.Outcome, Is.EqualTo(MigrationPlanOutcome.UnknownSchema));
            Assert.That(plan.Code, Is.EqualTo(DiagnosticCode.UnsupportedVersion));
            Assert.That(plan.IsRunnable, Is.False);
            Assert.That(plan.Steps, Is.Empty);
            Assert.That(plan.PathCount, Is.EqualTo(0));
            Assert.That(plan.Detail, Does.Contain("crosses schema"));

            Assert.That(
                registry.Plan(OtherVersion(1), Version(3)).Outcome,
                Is.EqualTo(MigrationPlanOutcome.UnknownSchema));
            Assert.That(
                registry.Plan(Version(1), OtherVersion(3)).Outcome,
                Is.EqualTo(MigrationPlanOutcome.UnknownSchema));
        }

        [Test]
        public void RegistrationRejectsIllFormedSteps()
        {
            var zeroKey = new FactoryKey(Id128.Zero, 1U);
            CheckpointMigrationRegistry registry = Registry(
                Step(2, 2, 1),
                new TestMigrationStep(StepKey(2), Version(1), OtherVersion(2)),
                new TestMigrationStep(zeroKey, Version(1), Version(2)),
                new TestMigrationStep(
                    StepKey(4),
                    new SchemaRef(new SchemaId(Id128.Zero), 1U),
                    Version(2)));

            Assert.That(registry.Steps, Is.Empty);
            Assert.That(registry.Rejections.Count, Is.EqualTo(4));
            Assert.That(registry.IsWellFormed, Is.False);
            Assert.That(registry.DuplicateKeys, Is.Empty);
            for (int i = 0; i < registry.Rejections.Count; i++)
            {
                Assert.That(registry.Rejections[i], Is.Not.Empty);
            }

            Assert.That(registry.Rejections[0], Does.Contain("forward only"));
            Assert.That(registry.Rejections[1], Does.Contain("two schema identities"));
            Assert.That(registry.Rejections[2], Does.Contain("all-zero schema or key"));
            Assert.That(registry.Rejections[3], Does.Contain("all-zero schema or key"));
        }

        [Test]
        public void RegistrationReportsADuplicateFactoryKey()
        {
            CheckpointMigrationRegistry registry = Registry(Step(1, 2, 1), Step(2, 3, 1));

            Assert.That(registry.Steps.Count, Is.EqualTo(1));
            Assert.That(registry.Steps[0].From.Version, Is.EqualTo(1U));
            Assert.That(registry.DuplicateKeys.Count, Is.EqualTo(1));
            Assert.That(registry.DuplicateKeys[0], Is.EqualTo(StepKey(1)));
            Assert.That(registry.Rejections.Count, Is.EqualTo(1));
            Assert.That(registry.Rejections[0], Does.Contain("share registration key"));
            Assert.That(registry.IsWellFormed, Is.False);

            // The surviving step is still planned: a catalogue defect is reported, not hidden.
            Assert.That(registry.Plan(Version(1), Version(2)).Outcome, Is.EqualTo(MigrationPlanOutcome.Unique));
        }

        [Test]
        public void AWellFormedGraphWithoutDuplicatesReportsCleanRegistration()
        {
            CheckpointMigrationRegistry registry = Registry(Step(1, 2, 1), Step(2, 3, 2), Step(3, 4, 3));

            Assert.That(registry.Steps.Count, Is.EqualTo(3));
            Assert.That(registry.Rejections, Is.Empty);
            Assert.That(registry.DuplicateKeys, Is.Empty);
            Assert.That(registry.IsWellFormed, Is.True);
        }

        [Test]
        public void PlanningEveryMigrationRefusesACapturedSchemaTheCatalogDoesNotCarry()
        {
            CheckpointMigrationRegistry registry = Registry(Step(1, 2, 1));

            bool ok = registry.TryPlanAll(
                new[] { Version(1) },
                new[] { OtherVersion(1) },
                out IReadOnlyList<MigrationPlan> plans,
                out MigrationPlan? refused,
                out string detail);

            Assert.That(ok, Is.False);
            Assert.That(refused, Is.Not.Null);
            Assert.That(refused!.Outcome, Is.EqualTo(MigrationPlanOutcome.UnknownSchema));
            Assert.That(refused.Code, Is.EqualTo(DiagnosticCode.UnsupportedVersion));
            Assert.That(refused.IsRunnable, Is.False);
            Assert.That(plans, Is.Empty);
            Assert.That(detail, Is.Not.Empty);
            Assert.That(detail, Is.EqualTo(refused.Detail));

            // Absent input is not a refusal: there is nothing to plan.
            Assert.That(
                registry.TryPlanAll(null, null, out IReadOnlyList<MigrationPlan> none, out MigrationPlan? noRefusal, out string noDetail),
                Is.True);
            Assert.That(none, Is.Empty);
            Assert.That(noRefusal, Is.Null);
            Assert.That(noDetail, Is.Empty);
        }

        [Test]
        public void PlanningEveryMigrationRefusesAnAmbiguousGraph()
        {
            CheckpointMigrationRegistry ambiguous = Registry(Step(1, 3, 1), Step(1, 2, 2), Step(2, 3, 3));

            bool ok = ambiguous.TryPlanAll(
                new[] { Version(1) },
                new[] { Version(3) },
                out IReadOnlyList<MigrationPlan> plans,
                out MigrationPlan? refused,
                out string detail);

            Assert.That(ok, Is.False);
            Assert.That(refused, Is.Not.Null);
            Assert.That(refused!.Outcome, Is.EqualTo(MigrationPlanOutcome.Ambiguous));
            Assert.That(refused.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(refused.PathCount, Is.EqualTo(2));
            Assert.That(plans, Is.Empty);
            Assert.That(detail, Is.Not.Empty);
        }

        [Test]
        public void PlanningEveryMigrationReturnsOnlyTheChainsARestoreActuallyNeeds()
        {
            CheckpointMigrationRegistry registry = Registry(Step(1, 2, 1), Step(2, 3, 2));

            Assert.That(
                registry.TryPlanAll(
                    new[] { Version(1) },
                    new[] { Version(3) },
                    out IReadOnlyList<MigrationPlan> plans,
                    out MigrationPlan? refused,
                    out string detail),
                Is.True,
                detail);
            Assert.That(refused, Is.Null);
            Assert.That(plans.Count, Is.EqualTo(1));
            Assert.That(plans[0].Outcome, Is.EqualTo(MigrationPlanOutcome.Unique));
            Assert.That(plans[0].Steps.Count, Is.EqualTo(2));
            Assert.That(plans[0].RequiresMigration, Is.True);

            // A capture already at the destination needs no migration, so it contributes no plan.
            Assert.That(
                registry.TryPlanAll(
                    new[] { Version(3) },
                    new[] { Version(3) },
                    out IReadOnlyList<MigrationPlan> currentPlans,
                    out MigrationPlan? currentRefused,
                    out string currentDetail),
                Is.True,
                currentDetail);
            Assert.That(currentRefused, Is.Null);
            Assert.That(currentPlans, Is.Empty);
        }

        [Test]
        public void TheChainLengthBoundRefusesAGapItWillNotSearch()
        {
            Assert.That(CheckpointMigrationRegistry.MaxChainLength, Is.EqualTo(64));

            var steps = new List<ISchemaMigrationStep>();
            for (uint version = 1; version < 65U; version++)
            {
                steps.Add(Step(version, version + 1U, version));
            }

            var registry = new CheckpointMigrationRegistry(steps);
            Assert.That(registry.Steps.Count, Is.EqualTo(64));
            Assert.That(registry.IsWellFormed, Is.True);

            // A gap of exactly MaxChainLength is still searched and planned as one unique chain.
            MigrationPlan atBound = registry.Plan(Version(1), Version(65));
            Assert.That(atBound.Outcome, Is.EqualTo(MigrationPlanOutcome.Unique));
            Assert.That(atBound.Steps.Count, Is.EqualTo(64));
            Assert.That(atBound.PathCount, Is.EqualTo(1));

            // One version further is refused rather than searched, and it returns instead of hanging (P-022).
            MigrationPlan beyondBound = registry.Plan(Version(1), Version(66));
            Assert.That(beyondBound.Outcome, Is.EqualTo(MigrationPlanOutcome.Unreachable));
            Assert.That(beyondBound.Code, Is.EqualTo(DiagnosticCode.MigrationRequired));
            Assert.That(beyondBound.Steps, Is.Empty);
            Assert.That(beyondBound.PathCount, Is.EqualTo(0));
            Assert.That(beyondBound.Detail, Does.Contain("chain bound"));
        }
    }
}

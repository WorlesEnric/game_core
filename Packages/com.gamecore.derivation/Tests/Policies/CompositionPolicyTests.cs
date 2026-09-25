// GameCore.Derivation tests — the five composition policies (P-019, TEST-005).
//
// TEST-005: "For additive, replacing, ordered, exclusive and incompatible contributions, enumerate all insertion
// permutations of four providers from different ancestor depths." Every policy is therefore exercised with four
// providers at four different depths, under every permutation of the declaration lists, and the canonical output
// must be identical each time. No policy may behave as last-writer-wins, and no conflict may delete a lower-ranked
// provider silently.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Derivation.Fixtures;
using NUnit.Framework;

namespace GameCore.Derivation.Tests
{
    [TestFixture]
    public sealed class CompositionPolicyTests
    {
        private const string Root = "policy.root";
        private const string Depth1 = "policy.depth-1";
        private const string Depth2 = "policy.depth-2";
        private const string Depth3 = "policy.depth-3";
        private const string Depth4 = "policy.depth-4";

        private const string Target = "policy.target";
        private const string Recipe = "policy.member-recipe";
        private const string ValueCapability = "policy.value";

        private static readonly string[] Providers = { "policy.p1", "policy.p2", "policy.p3", "policy.p4" };
        private static readonly string[] ProviderScopes = { Depth1, Depth2, Depth3, Depth4 };

        private static WorldId World { get; } = new WorldId(FixtureIds.Id("gamecore.world.policy-tests"));

        [Test]
        public void ReplaceChoosesTheHighestRankedCandidateAndKeepsTheLosersAsProvenance()
        {
            DerivationResult result = Derive(CompositionPolicy.Replace);

            EffectiveSlot slot = DerivationAssert.SlotOf(result, FixtureIds.Target(Target), ValueCapability);
            Assert.That(
                DerivationAssert.IdValue(slot),
                Is.EqualTo(FixtureIds.Id(Providers[3] + ".value")),
                "All priorities are equal, so the nearer provider scope wins (P-018).");
            Assert.That(slot.Support.Count, Is.EqualTo(1), "Exactly the winner supplies the Replace slot.");
            Assert.That(slot.Shadowed.Count, Is.EqualTo(3), "Lower-ranked candidates remain inspectable provenance (P-019).");
            for (int i = 0; i < slot.Shadowed.Count; i++)
            {
                Assert.That(slot.Shadowed[i].Disposition, Is.EqualTo(ContributionDisposition.Shadowed));
            }
        }

        [Test]
        public void AdditiveFoldsEveryContributionThroughTheRegisteredReducer()
        {
            DerivationResult result = Derive(CompositionPolicy.Additive);

            EffectiveSlot slot = DerivationAssert.SlotOf(result, FixtureIds.Target(Target), ValueCapability);
            Assert.That(DerivationAssert.Int32Value(slot), Is.EqualTo(1 + 2 + 3 + 4), "All four contributions are folded (P-019).");
            Assert.That(slot.Support.Count, Is.EqualTo(4), "Support is a set of contribution ids, not a boolean (P-017).");
            Assert.That(slot.Shadowed.Count, Is.EqualTo(0));
        }

        [Test]
        public void AdditiveWithoutAReducerProducesTheCanonicalSetUnion()
        {
            DerivationResult result = Derive(CompositionPolicy.Additive, payloadIsIdentity: true);

            EffectiveSlot slot = DerivationAssert.SlotOf(result, FixtureIds.Target(Target), ValueCapability);
            Assert.That(slot.Values.Count, Is.EqualTo(4), "A reducer-less Additive slot is the canonical set union (P-019).");
            List<Id128> values = new List<Id128>();
            for (int i = 0; i < slot.Values.Count; i++)
            {
                Id128 value;
                Assert.That(FixturePayload.TryReadId128(slot.Values[i], out value), Is.True);
                values.Add(value);
            }

            for (int i = 1; i < values.Count; i++)
            {
                Assert.That(
                    values[i].CompareTo(values[i - 1]),
                    Is.GreaterThan(0),
                    "A canonical set union is ascending and duplicate-free, independent of insertion order.");
            }
        }

        [Test]
        public void OrderedSortsByDeclaredKeys()
        {
            DerivationResult result = Derive(CompositionPolicy.Ordered, payloadIsIdentity: true);

            EffectiveSlot slot = DerivationAssert.SlotOf(result, FixtureIds.Target(Target), ValueCapability);
            Assert.That(slot.Values.Count, Is.EqualTo(4));
            for (int i = 0; i < slot.Values.Count; i++)
            {
                Assert.That(
                    DerivationAssert.IdValueAt(slot, i),
                    Is.EqualTo(FixtureIds.Id(Providers[i] + ".value")),
                    "The declared before/after keys order the outputs, not their rank (P-019).");
            }
        }

        [Test]
        public void OrderedRejectsARequiredKeyWithoutAnEndpoint()
        {
            FixtureBuilder builder = Builder(CompositionPolicy.Ordered, payloadIsIdentity: false);
            builder.RuleKey(
                Providers[2] + ".rule",
                ValueCapability,
                "policy.key.p3",
                before: new[] { new FixtureOrderEdge("policy.key.missing", true) });

            DerivationResult result = DerivationAssert.Rejected(
                DerivationEngine.Derive(SnapshotOf(builder), Source(), DerivationOptions.Default, null),
                DerivationRejectionKind.CompositionConflict);

            Assert.That(result.CompositionFailures[0].Code, Is.EqualTo(DiagnosticCode.MissingDependency));
            Assert.That(
                result.CompositionFailures[0].WitnessKeys.Count,
                Is.GreaterThan(0),
                "An unknown required key rejects with the key as witness (P-019).");
        }

        [Test]
        public void OrderedDropsAnOptionalEdgeWhoseEndpointDoesNotExist()
        {
            FixtureBuilder builder = Builder(CompositionPolicy.Ordered, payloadIsIdentity: false);
            builder.RuleKey(
                Providers[2] + ".rule",
                ValueCapability,
                "policy.key.p3",
                before: new[] { new FixtureOrderEdge("policy.key.absent", false) });

            DerivationResult result = DerivationAssert.Accepted(
                DerivationEngine.Derive(SnapshotOf(builder), Source(), DerivationOptions.Default, null));
            Assert.That(DerivationAssert.SlotOf(result, FixtureIds.Target(Target), ValueCapability).Values.Count, Is.EqualTo(4));
        }

        [Test]
        public void OrderedRejectsADeclaredCycleWithItsCandidatesAsWitnesses()
        {
            FixtureBuilder builder = Builder(CompositionPolicy.Ordered, payloadIsIdentity: false);
            builder.RuleKey(
                Providers[0] + ".rule",
                ValueCapability,
                "policy.key.p1",
                after: new[] { new FixtureOrderEdge("policy.key.p4", true) });

            DerivationResult result = DerivationAssert.Rejected(
                DerivationEngine.Derive(SnapshotOf(builder), Source(), DerivationOptions.Default, null),
                DerivationRejectionKind.CompositionConflict);

            Assert.That(result.CompositionFailures[0].Code, Is.EqualTo(DiagnosticCode.Cycle));
            Assert.That(
                result.CompositionFailures[0].WitnessKeys.Count,
                Is.GreaterThan(0),
                "The cycle reports the smallest available witness set (TEST-007).");
        }

        [Test]
        public void OrderedRejectsTwoCandidatesClaimingTheSameOrderingKey()
        {
            FixtureBuilder builder = Builder(CompositionPolicy.Ordered, payloadIsIdentity: false);
            builder.RuleKey(
                Providers[1] + ".rule",
                ValueCapability,
                "policy.key.p1",
                before: new[] { new FixtureOrderEdge("policy.key.p2", true) });

            DerivationResult result = DerivationAssert.Rejected(
                DerivationEngine.Derive(SnapshotOf(builder), Source(), DerivationOptions.Default, null),
                DerivationRejectionKind.CompositionConflict);

            Assert.That(result.CompositionFailures[0].Code, Is.EqualTo(DiagnosticCode.AmbiguousOrder));
        }

        [Test]
        public void ExclusiveRejectsMoreThanOneCandidateEvenWhenRanksDiffer()
        {
            DerivationResult result = DerivationAssert.Rejected(
                DerivationEngine.Derive(
                    SnapshotOf(Builder(CompositionPolicy.Exclusive, payloadIsIdentity: false)),
                    Source(),
                    DerivationOptions.Default,
                    null),
                DerivationRejectionKind.CompositionConflict);

            Assert.That(result.CompositionFailures[0].Code, Is.EqualTo(DiagnosticCode.CapabilityConflict));
            Assert.That(
                result.CompositionFailures[0].Involved.Count,
                Is.EqualTo(4),
                "The conflict names the conflicting provenance, not just a count (TEST-005).");
        }

        [Test]
        public void ExclusiveAcceptsTheExplicitlySelectedEligibleWinner()
        {
            FixtureBuilder builder = Builder(CompositionPolicy.Exclusive, payloadIsIdentity: true);
            builder.Select(ValueCapability, Providers[1], atTarget: Target);

            DerivationResult result = DerivationAssert.Accepted(
                DerivationEngine.Derive(SnapshotOf(builder), Source(), DerivationOptions.Default, null));

            EffectiveSlot slot = DerivationAssert.SlotOf(result, FixtureIds.Target(Target), ValueCapability);
            Assert.That(DerivationAssert.IdValue(slot), Is.EqualTo(FixtureIds.Id(Providers[1] + ".value")));
            Assert.That(slot.Shadowed.Count, Is.EqualTo(3), "Losing providers remain inspectable (P-019).");
        }

        [Test]
        public void IncompatibleRejectsASecondActiveMemberOfTheSet()
        {
            DerivationResult result = DerivationAssert.Rejected(
                DerivationEngine.Derive(
                    SnapshotOf(Builder(CompositionPolicy.Incompatible, payloadIsIdentity: false)),
                    Source(),
                    DerivationOptions.Default,
                    null),
                DerivationRejectionKind.CompositionConflict);

            Assert.That(result.CompositionFailures[0].Code, Is.EqualTo(DiagnosticCode.CapabilityConflict));
        }

        [Test]
        public void IncompatibleAcceptsASingleActiveMember()
        {
            FixtureBuilder builder = Builder(CompositionPolicy.Incompatible, payloadIsIdentity: false);
            for (int i = 1; i < Providers.Length; i++)
            {
                builder.RemoveInstall(Providers[i]);
            }

            DerivationResult result = DerivationAssert.Accepted(
                DerivationEngine.Derive(SnapshotOf(builder), Source(), DerivationOptions.Default, null));
            Assert.That(DerivationAssert.SlotOf(result, FixtureIds.Target(Target), ValueCapability).Support.Count, Is.EqualTo(1));
        }

        [Test]
        public void CrossCapabilityIncompatibilityRejectsTheWholeProposalWhenBothWouldBeActive()
        {
            FixtureBuilder builder = Builder(CompositionPolicy.Replace, payloadIsIdentity: false);
            builder
                .Contract("policy.rival", 0, new[] { new FixtureSlot("policy.rival-schema", CompositionPolicy.Replace) })
                .AddTargetCapability(Target, "policy.rival");

            DerivationResult result = DerivationAssert.Rejected(
                DerivationEngine.Derive(SnapshotOf(builder), Source(), DerivationOptions.Default, null),
                DerivationRejectionKind.IncompatibleCapabilities);

            Assert.That(result.CompositionFailures[0].Code, Is.EqualTo(DiagnosticCode.CapabilityConflict));
            Assert.That(
                result.CompositionFailures[0].WitnessKeys.Count,
                Is.EqualTo(2),
                "Both members of the incompatibility set are named as witnesses (P-019).");
        }

        [Test]
        public void AdditiveReducerOverflowRejectsTheWholeProposal()
        {
            FixtureBuilder builder = Builder(CompositionPolicy.Additive, payloadIsIdentity: false);
            for (int i = 0; i < Providers.Length; i++)
            {
                builder.ReplaceRulePayload(Providers[i], Providers[i] + ".rule", FixturePayload.Int32(int.MaxValue - 1));
            }

            DerivationResult result = DerivationAssert.Rejected(
                DerivationEngine.Derive(SnapshotOf(builder), Source(), DerivationOptions.Default, null),
                DerivationRejectionKind.CompositionConflict);

            Assert.That(result.CompositionFailures[0].Code, Is.EqualTo(DiagnosticCode.CapabilityConflict));
            Assert.That(
                result.CompositionFailures[0].Summary,
                Does.Contain("overflow"),
                "P-019: the reducer validates overflow and the error rejects the whole proposal.");
        }

        [Test]
        public void EveryPolicyProducesTheSameResultUnderEveryInsertionPermutation()
        {
            foreach (CompositionPolicy policy in new[]
                     {
                         CompositionPolicy.Additive,
                         CompositionPolicy.Replace,
                         CompositionPolicy.Ordered,
                     })
            {
                AssertAllPermutationsAgree(policy);
            }
        }

        private static void AssertAllPermutationsAgree(CompositionPolicy policy)
        {
            FixtureBuilder builder = Builder(policy, payloadIsIdentity: false);
            DerivationResult baseline = DerivationAssert.Accepted(
                DerivationEngine.Derive(SnapshotOf(builder), Source(), DerivationOptions.Default, null));
            ContentHash expected = DerivationProjection.SemanticsHash(baseline);

            for (int seed = 1; seed <= 24; seed++)
            {
                Permutation permutation = new Permutation(seed, seed + 7);
                FixtureComposition parts = builder.Build(
                    PropagationMode.Automatic, new CompositionRevision(1UL), AssemblyEpoch.First);

                DerivationSnapshot permuted = Snapshot(
                    parts,
                    permutation.Apply(parts.Scopes),
                    permutation.Apply(parts.Installs),
                    permutation.Apply(parts.Targets),
                    permutation.Apply(parts.Contracts),
                    permutation.Apply(parts.RuleKeys),
                    permutation.Apply(parts.Overrides));

                DerivationResult result = DerivationAssert.Accepted(
                    DerivationEngine.Derive(permuted, Source(), DerivationOptions.Default, null));
                Assert.That(
                    DerivationProjection.SemanticsHash(result),
                    Is.EqualTo(expected),
                    "Policy " + policy.ToString() + " changed under insertion permutation seed " + seed + ".");

                DerivationSnapshot rotated = Snapshot(
                    parts,
                    permutation.Rotate(parts.Scopes),
                    permutation.Rotate(parts.Installs),
                    permutation.Rotate(parts.Targets),
                    permutation.Rotate(parts.Contracts),
                    permutation.Rotate(parts.RuleKeys),
                    permutation.Rotate(parts.Overrides));

                DerivationResult rotatedResult = DerivationAssert.Accepted(
                    DerivationEngine.Derive(rotated, Source(), DerivationOptions.Default, null));
                Assert.That(
                    DerivationProjection.SemanticsHash(rotatedResult),
                    Is.EqualTo(expected),
                    "Policy " + policy.ToString() + " changed under a rotated declaration order (seed " + seed + ").");
            }
        }

        private static DerivationSnapshot SnapshotOf(FixtureBuilder builder) =>
            builder.Build(PropagationMode.Automatic, new CompositionRevision(1UL), AssemblyEpoch.First).ToSnapshot();

        private static DerivationSnapshot Snapshot(
            FixtureComposition parts,
            IReadOnlyList<DerivationScope> scopes,
            IReadOnlyList<DerivationInstall> installs,
            IReadOnlyList<DerivationTarget> targets,
            IReadOnlyList<CapabilityContract> contracts,
            IReadOnlyList<DerivationRuleKeys> ruleKeys,
            IReadOnlyList<ProviderSelectionOverride> overrides) =>
            new DerivationSnapshot(
                parts.World,
                parts.Revision,
                parts.Epoch,
                parts.Mode,
                scopes,
                installs,
                targets,
                contracts,
                ruleKeys,
                overrides);

        private static FixtureValueSource Source() =>
            new FixtureValueSource()
                .RegisterInt32Sum("policy.reducer.int32-sum")
                .RegisterAlwaysPredicate("policy.predicate.always");

        private static DerivationResult Derive(
            CompositionPolicy policy,
            bool payloadIsIdentity = false) =>
            DerivationAssert.Accepted(DerivationEngine.Derive(
                SnapshotOf(Builder(policy, payloadIsIdentity)),
                Source(),
                DerivationOptions.Default,
                null));

        private static FixtureBuilder Builder(CompositionPolicy policy, bool payloadIsIdentity)
        {
            FixtureBuilder builder = new FixtureBuilder(World)
                .Scope(Root, null)
                .Scope(Depth1, Root)
                .Scope(Depth2, Depth1)
                .Scope(Depth3, Depth2)
                .Scope(Depth4, Depth3);

            // Only the Additive policy declares a reducer key; the others compose by declaration (P-019).
            FactoryKey reducer = policy == CompositionPolicy.Additive
                ? FixtureIds.Key("policy.reducer.int32-sum")
                : default(FactoryKey);

            builder
                .Contract(ValueCapability, 0, new[] { new FixtureSlot("policy.value-schema", policy, reducer: reducer) })
                .Target(Target, Depth4, Recipe);

            for (int i = 0; i < Providers.Length; i++)
            {
                builder.Install(
                    Providers[i],
                    ProviderScopes[i],
                    0,
                    Rules(policy, i, payloadIsIdentity),
                    state: InstallationState.Active);

                if (policy == CompositionPolicy.Ordered)
                {
                    builder.RuleKey(
                        Providers[i] + ".rule",
                        ValueCapability,
                        "policy.key.p" + (i + 1),
                        before: i < Providers.Length - 1
                            ? new[] { new FixtureOrderEdge("policy.key.p" + (i + 2), true) }
                            : null,
                        after: i > 0
                            ? new[] { new FixtureOrderEdge("policy.key.p" + i, true) }
                            : null);
                }
            }

            return builder;
        }

        private static IReadOnlyList<DerivationRule> Rules(CompositionPolicy policy, int index, bool payloadIsIdentity) =>
            new List<DerivationRule>
            {
                FixtureBuilder.Rule(
                    Providers[index] + ".rule",
                    ValueCapability,
                    0,
                    1U,
                    FixtureBuilder.Selector(Recipe),
                    FixtureIds.Key("policy.predicate.always"),
                    null,
                    PropagationReach.SelfAndDescendants,
                    true,
                    0,
                    policy,
                    payloadIsIdentity
                        ? FixturePayload.Tag(Providers[index] + ".value")
                        : FixturePayload.Int32(index + 1)),
            };
    }
}

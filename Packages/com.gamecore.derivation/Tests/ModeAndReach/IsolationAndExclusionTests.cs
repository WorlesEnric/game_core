// GameCore.Derivation tests — isolation and exclusions (P-016, TEST-006's denial half, REF-P02).
//
// P-016: a capability boundary blocks outside rules on the boundary scope and its descendants while providers
// installed at or below it still work; a descendant cannot reopen an ancestor boundary; sibling branches do not
// inherit each other's providers; exclusions target a capability, a rule or a provider on one target or a scope
// subtree; and denial along the propagation path wins over imports, opt-ins and selection overrides in both modes.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Derivation.Fixtures;
using NUnit.Framework;

namespace GameCore.Derivation.Tests
{
    [TestFixture]
    public sealed class IsolationAndExclusionTests
    {
        private const string Root = "iso.root";
        private const string ProviderScope = "iso.provider-scope";
        private const string Outside = "iso.outside";
        private const string Boundary = "iso.boundary";
        private const string InsideBoundary = "iso.boundary-inside";
        private const string InnerProviderScope = "iso.inner-provider-scope";
        private const string OtherBranch = "iso.other-branch";

        private const string Capability = "iso.role-grant";
        private const string OtherCapability = "iso.tint";
        private const string Recipe = "iso.member-recipe";

        private const string Provider = "iso.provider";
        private const string InnerProvider = "iso.inner-provider";

        private const string OutsideTarget = "iso.target-outside";
        private const string InsideTarget = "iso.target-inside-boundary";
        private const string SiblingTarget = "iso.target-sibling";
        private const string InsideProviderTarget = "iso.target-inside-provider";

        private static WorldId World { get; } = new WorldId(FixtureIds.Id("gamecore.world.isolation-tests"));

        [Test]
        public void ACapabilityBoundaryBlocksAnOutsideRuleForTheBoundarySubtree()
        {
            DerivationResult result = Derive(PropagationMode.Automatic, isolateAllCapabilities: false);

            Assert.That(DerivationAssert.HasCapability(result, FixtureIds.Target(OutsideTarget), Capability), Is.True);
            Assert.That(
                DerivationAssert.HasCapability(result, FixtureIds.Target(InsideTarget), Capability),
                Is.False,
                "A boundary blocks outside rules on its scope and descendants (P-016).");

            CandidateDecision? decision = FindDecision(result, FixtureIds.Target(InsideTarget), Capability);
            Assert.That(decision, Is.Not.Null);
            Assert.That(decision!.Status, Is.EqualTo(CandidateStatus.BlockedByBoundary));
            Assert.That(decision.Reason, Is.EqualTo(CandidateRejectionReason.CapabilityBoundary));
            Assert.That(decision.Boundaries.Count, Is.GreaterThan(0));
            Assert.That(
                decision.Boundaries[0].BoundaryScope,
                Is.EqualTo(FixtureIds.Scope(Boundary)),
                "The explanation identifies the blocker (REF-P02).");
        }

        [Test]
        public void AProviderInstalledInsideTheBoundaryStillWorks()
        {
            DerivationResult result = Derive(PropagationMode.Automatic, isolateAllCapabilities: false);

            Assert.That(
                DerivationAssert.HasCapability(result, FixtureIds.Target(InsideProviderTarget), Capability),
                Is.True,
                "A provider installed at or below the boundary still contributes inside it (P-016).");
        }

        [Test]
        public void AnOptInOrImportCannotReopenABoundaryInEitherMode()
        {
            foreach (PropagationMode mode in new[] { PropagationMode.Automatic, PropagationMode.Conservative })
            {
                FixtureBuilder builder = Builder(isolateAllCapabilities: false);
                builder.ReplaceTarget(
                    InsideTarget,
                    InsideBoundary,
                    Recipe,
                    new List<SchemaRef> { FixtureIds.SchemaRef(Recipe) },
                    null,
                    new[] { Capability, Provider });

                DerivationResult result = DerivationAssert.Accepted(
                    DerivationEngine.Derive(Snapshot(builder, mode), Source(), DerivationOptions.Default, null));

                Assert.That(
                    DerivationAssert.HasCapability(result, FixtureIds.Target(InsideTarget), Capability),
                    Is.False,
                    "Denial along the propagation path wins over imports and opt-ins in " + mode.ToString() + " (P-016).");
            }
        }

        [Test]
        public void ASiblingBranchIsNeitherBlockedNorGrantedByAnotherBranchesBoundary()
        {
            DerivationResult result = Derive(PropagationMode.Automatic, isolateAllCapabilities: false);

            Assert.That(
                DerivationAssert.HasCapability(result, FixtureIds.Target(SiblingTarget), Capability),
                Is.True,
                "Sibling branches do not inherit each other's boundaries; the boundary subtree has no spillover (P-016).");
        }

        [Test]
        public void AWildcardBoundaryBlocksEveryCapabilityBelowIt()
        {
            DerivationResult result = Derive(PropagationMode.Automatic, isolateAllCapabilities: true);

            Assert.That(DerivationAssert.HasCapability(result, FixtureIds.Target(InsideTarget), Capability), Is.False);
            Assert.That(
                DerivationAssert.HasCapability(result, FixtureIds.Target(SiblingTarget), Capability),
                Is.True,
                "`*` means all contracts for the boundary subtree only (P-016).");
        }

        [Test]
        public void ANamedBoundaryBlocksOnlyTheNamedCapability()
        {
            FixtureBuilder builder = Builder(isolateAllCapabilities: false);
            AddSecondCapability(builder);

            DerivationResult result = DerivationAssert.Accepted(
                DerivationEngine.Derive(Snapshot(builder, PropagationMode.Automatic), Source(), DerivationOptions.Default, null));

            Assert.That(DerivationAssert.HasCapability(result, FixtureIds.Target(InsideTarget), Capability), Is.False);
            Assert.That(
                DerivationAssert.HasCapability(result, FixtureIds.Target(InsideTarget), OtherCapability),
                Is.True,
                "A named boundary blocks only the named contracts (P-016).");
        }

        [Test]
        public void ATargetExclusionOfOneCapabilityLeavesUnrelatedCapabilitiesAvailable()
        {
            FixtureBuilder builder = Builder(isolateAllCapabilities: false);
            AddSecondCapability(builder);
            builder.ReplaceTarget(
                OutsideTarget,
                Outside,
                Recipe,
                new List<SchemaRef> { FixtureIds.SchemaRef(Recipe) },
                new[]
                {
                    new ExclusionRule(
                        ExclusionTargetKind.Capability, FixtureIds.Capability(Capability).Value, default(ScopeId), default(TargetId), false),
                });

            DerivationResult result = DerivationAssert.Accepted(
                DerivationEngine.Derive(Snapshot(builder, PropagationMode.Automatic), SourceB(), DerivationOptions.Default, null));

            Assert.That(
                DerivationAssert.HasCapability(result, FixtureIds.Target(OutsideTarget), Capability),
                Is.False,
                "A target exclusion of the named capability denies it on that target (P-016).");
            Assert.That(
                DerivationAssert.HasCapability(result, FixtureIds.Target(OutsideTarget), OtherCapability),
                Is.True,
                "Unrelated capabilities on the same target survive (TEST-006).");

            CandidateDecision? decision = FindDecision(result, FixtureIds.Target(OutsideTarget), Capability);
            Assert.That(decision!.Status, Is.EqualTo(CandidateStatus.Excluded));
            Assert.That(decision.Exclusions.Count, Is.GreaterThan(0));
            Assert.That(decision.Exclusions[0].FromScope, Is.False);
        }

        [Test]
        public void AProviderExclusionOnAScopeSubtreeStopsOnlyThatProvidersContribution()
        {
            FixtureBuilder builder = Builder(isolateAllCapabilities: false);
            builder.AddScopeExclusion(
                Outside,
                new ExclusionRule(
                    ExclusionTargetKind.Provider,
                    FixtureIds.Instance(Provider).Value,
                    FixtureIds.Scope(Outside),
                    default(TargetId),
                    true));

            DerivationResult result = DerivationAssert.Accepted(
                DerivationEngine.Derive(Snapshot(builder, PropagationMode.Automatic), Source(), DerivationOptions.Default, null));

            Assert.That(
                DerivationAssert.HasCapability(result, FixtureIds.Target(OutsideTarget), Capability),
                Is.False,
                "A provider exclusion on the subtree denies that provider's contribution inside it (P-016).");
            Assert.That(
                DerivationAssert.HasCapability(result, FixtureIds.Target(SiblingTarget), Capability),
                Is.True,
                "The exclusion does not spill outside the subtree it was declared on (P-016).");
        }

        [Test]
        public void ARuleExclusionDeniesOnlyThatRule()
        {
            FixtureBuilder builder = Builder(isolateAllCapabilities: false);
            AddSecondRule(builder, "iso.other-rule", OtherCapability);
            builder.ReplaceTarget(
                OutsideTarget,
                Outside,
                Recipe,
                new List<SchemaRef> { FixtureIds.SchemaRef(Recipe) },
                new[]
                {
                    new ExclusionRule(
                        ExclusionTargetKind.Rule,
                        FixtureIds.Rule(Provider + ".grant").Value,
                        default(ScopeId),
                        default(TargetId),
                        false),
                });

            DerivationResult result = DerivationAssert.Accepted(
                DerivationEngine.Derive(Snapshot(builder, PropagationMode.Automatic), SourceB(), DerivationOptions.Default, null));

            Assert.That(DerivationAssert.HasCapability(result, FixtureIds.Target(OutsideTarget), Capability), Is.False);
            Assert.That(DerivationAssert.HasCapability(result, FixtureIds.Target(OutsideTarget), OtherCapability), Is.True);
        }

        private static CandidateDecision? FindDecision(DerivationResult result, TargetId target, string capability)
        {
            IReadOnlyList<CandidateDecision> decisions = result.DecisionsOf(target, FixtureIds.Capability(capability));
            for (int i = 0; i < decisions.Count; i++)
            {
                if (decisions[i].Status == CandidateStatus.Emitted
                    || decisions[i].Status == CandidateStatus.Shadowed
                    || decisions[i].Status == CandidateStatus.Excluded
                    || decisions[i].Status == CandidateStatus.BlockedByBoundary)
                {
                    return decisions[i];
                }
            }

            return decisions.Count > 0 ? decisions[0] : null;
        }

        private static DerivationResult Derive(PropagationMode mode, bool isolateAllCapabilities)
        {
            FixtureBuilder builder = Builder(isolateAllCapabilities);
            return DerivationAssert.Accepted(
                DerivationEngine.Derive(Snapshot(builder, mode), Source(), DerivationOptions.Default, null));
        }

        private static DerivationSnapshot Snapshot(FixtureBuilder builder, PropagationMode mode) =>
            builder.Build(mode, new CompositionRevision(1UL), AssemblyEpoch.First).ToSnapshot();

        private static FixtureValueSource Source() =>
            new FixtureValueSource().RegisterAlwaysPredicate("iso.predicate.always");

        private static FixtureValueSource SourceB() => Source();

        private static FixtureBuilder Builder(bool isolateAllCapabilities)
        {
            FixtureBuilder builder = new FixtureBuilder(World)
                .Scope(Root, null)
                .Scope(ProviderScope, Root)
                .Scope(Outside, ProviderScope)
                .Scope(OtherBranch, ProviderScope);

            if (isolateAllCapabilities)
            {
                builder.Scope(Boundary, ProviderScope, isolateAllCapabilities: true);
            }
            else
            {
                builder.Scope(Boundary, ProviderScope, isolatedCapabilities: new[] { Capability });
            }

            builder
                .Scope(InsideBoundary, Boundary)
                .Scope(InnerProviderScope, InsideBoundary)
                .Contract(Capability, 0, new[] { new FixtureSlot(Recipe, CompositionPolicy.Replace) });

            builder
                .Target(OutsideTarget, Outside, Recipe)
                .Target(InsideTarget, InsideBoundary, Recipe)
                .Target(SiblingTarget, OtherBranch, Recipe)
                .Target(InsideProviderTarget, InnerProviderScope, Recipe);

            builder.Install(Provider, ProviderScope, 0, Rules(), state: InstallationState.Active);
            builder.Install(InnerProvider, InnerProviderScope, 0, RulesFor(InnerProvider, Capability), state: InstallationState.Active);

            return builder;
        }

        private static void AddSecondCapability(FixtureBuilder builder)
        {
            builder
                .Contract(OtherCapability, 0, new[] { new FixtureSlot(Recipe, CompositionPolicy.Replace) })
                .Install("iso.tint-provider", ProviderScope, 0, RulesFor("iso.tint-provider", OtherCapability), state: InstallationState.Active);
        }

        private static void AddSecondRule(FixtureBuilder builder, string provider, string capability)
        {
            builder
                .Contract(capability, 0, new[] { new FixtureSlot(Recipe, CompositionPolicy.Replace) })
                .Install(provider, ProviderScope, 0, RulesFor(provider, capability), state: InstallationState.Active);
        }

        private static IReadOnlyList<DerivationRule> Rules() => RulesFor(Provider, Capability);

        private static IReadOnlyList<DerivationRule> RulesFor(string provider, string capability) =>
            new List<DerivationRule>
            {
                FixtureBuilder.Rule(
                    provider + ".grant",
                    capability,
                    0,
                    1U,
                    FixtureBuilder.Selector(Recipe),
                    FixtureIds.Key("iso.predicate.always"),
                    null,
                    PropagationReach.SelfAndDescendants,
                    true,
                    0,
                    CompositionPolicy.Replace,
                    FixturePayload.Tag(provider + ".value")),
            };
    }
}

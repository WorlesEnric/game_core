// GameCore.Derivation tests — precedence, overrides, shared support and identity (P-017, P-018).
//
// P-018's total rank is "higher signed Priority, then nearer provider scope, then ascending PluginInstanceId,
// RuleId and OutputSlot bytes", and 02 s6's worked example is exactly the case where a nearer provider loses to a
// higher explicit priority. Explicit `SelectProvider` data is legal only for `Replace`/`Exclusive`; a missing
// selection rejects rather than falling back. P-017 then requires that support survives partial retraction and
// that a contribution's identity outlives a payload reconfiguration.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Derivation.Fixtures;
using NUnit.Framework;

namespace GameCore.Derivation.Tests
{
    [TestFixture]
    public sealed class PrecedenceAndSupportTests
    {
        private const string Root = "prec.root";
        private const string Near = "prec.near";
        private const string Far = "prec.far";
        private const string Target = "prec.target";
        private const string Recipe = "prec.member-recipe";
        private const string Capability = "prec.value";
        private const string NearProvider = "prec.near-provider";
        private const string FarProvider = "prec.far-provider";

        private static WorldId World { get; } = new WorldId(FixtureIds.Id("gamecore.world.precedence-tests"));

        [Test]
        public void AHigherExplicitPriorityBeatsANearerProviderScope()
        {
            // 02 s6: raising the far provider's explicit priority above the near one chooses the far value
            // even though the near provider is nearer (P-018: higher signed priority first).
            FixtureBuilder builder = Builder(CompositionPolicy.Replace, nearPriority: 0, farPriority: 10);
            DerivationResult result = Derivation(Of(builder));

            EffectiveSlot slot = DerivationAssert.SlotOf(result, FixtureIds.Target(Target), Capability);
            Assert.That(DerivationAssert.IdValue(slot), Is.EqualTo(FixtureIds.Id(FarProvider + ".value")));
        }

        [Test]
        public void EqualPriorityIsBrokenByTheNearerProviderScope()
        {
            FixtureBuilder builder = Builder(CompositionPolicy.Replace, nearPriority: 0, farPriority: 0);
            DerivationResult result = Derivation(Of(builder));

            EffectiveSlot slot = DerivationAssert.SlotOf(result, FixtureIds.Target(Target), Capability);
            Assert.That(DerivationAssert.IdValue(slot), Is.EqualTo(FixtureIds.Id(NearProvider + ".value")));
        }

        [Test]
        public void EqualPriorityAndDepthAreBrokenByAscendingProviderIdentity()
        {
            // Two providers installed in the same far scope at equal priority: only the ascending provider
            // installation identity can separate them, so the near provider is left out of this fixture.
            FixtureBuilder builder = Builder(CompositionPolicy.Replace, nearPriority: 0, farPriority: 0);
            builder.RemoveInstall(NearProvider);
            builder.Install("prec.far-provider-two", Far, 0, Rules("prec.far-provider-two", "prec.far-two.value", CompositionPolicy.Replace, 0), state: InstallationState.Active);

            DerivationResult result = Derivation(Of(builder));
            EffectiveSlot slot = DerivationAssert.SlotOf(result, FixtureIds.Target(Target), Capability);

            Id128 first = FixtureIds.Installation(FarProvider).Value;
            Id128 second = FixtureIds.Installation("prec.far-provider-two").Value;
            Id128 expected = first.CompareTo(second) < 0
                ? FixtureIds.Id(FarProvider + ".value")
                : FixtureIds.Id("prec.far-two.value");
            Assert.That(
                DerivationAssert.IdValue(slot),
                Is.EqualTo(expected),
                "At equal priority and depth the ascending provider installation identity decides (P-018).");
        }

        [Test]
        public void AnExplicitSelectionOverrideChoosesTheEligibleCandidate()
        {
            FixtureBuilder builder = Builder(CompositionPolicy.Replace, nearPriority: 0, farPriority: 0);
            builder.Select(Capability, FarProvider, atTarget: Target);

            DerivationResult result = Derivation(Of(builder));
            EffectiveSlot slot = DerivationAssert.SlotOf(result, FixtureIds.Target(Target), Capability);
            Assert.That(
                DerivationAssert.IdValue(slot),
                Is.EqualTo(FixtureIds.Id(FarProvider + ".value")),
                "An explicit eligible SelectProvider chooses the value regardless of rank (02 s6).");
            Assert.That(slot.Shadowed.Count, Is.EqualTo(1), "The rank winner stays as shadowed provenance.");
        }

        [Test]
        public void AScopeAddressedOverrideAppliesToTheSubtreeWhenDeclaredSo()
        {
            FixtureBuilder builder = Builder(CompositionPolicy.Replace, nearPriority: 0, farPriority: 0);
            builder.Select(Capability, FarProvider, atScope: Near, subtree: true);

            DerivationResult result = Derivation(Of(builder));
            EffectiveSlot slot = DerivationAssert.SlotOf(result, FixtureIds.Target(Target), Capability);
            Assert.That(DerivationAssert.IdValue(slot), Is.EqualTo(FixtureIds.Id(FarProvider + ".value")));
        }

        [Test]
        public void AnOverrideNamingAnIneligibleCandidateRejectsInsteadOfFallingBack()
        {
            FixtureBuilder builder = Builder(CompositionPolicy.Replace, nearPriority: 0, farPriority: 0);
            builder.Select(Capability, "prec.not-a-provider", atTarget: Target);

            DerivationResult result = DerivationAssert.Rejected(
                DerivationEngine.Derive(Of(builder), Source(), DerivationOptions.Default, null),
                DerivationRejectionKind.CompositionConflict);

            Assert.That(result.CompositionFailures[0].Code, Is.EqualTo(DiagnosticCode.MissingDependency));
            Assert.That(
                result.CompositionFailures[0].Summary,
                Does.Contain("not eligible"),
                "A missing selection rejects the plan rather than falling back (P-018).");
        }

        [Test]
        public void TwoOverridesNamingDifferentProvidersForOneSlotAreAmbiguous()
        {
            FixtureBuilder builder = Builder(CompositionPolicy.Replace, nearPriority: 0, farPriority: 0);
            builder.Select(Capability, FarProvider, atTarget: Target);
            builder.Select(Capability, NearProvider, atTarget: Target);

            DerivationResult result = DerivationAssert.Rejected(
                DerivationEngine.Derive(Of(builder), Source(), DerivationOptions.Default, null),
                DerivationRejectionKind.CompositionConflict);
            Assert.That(result.CompositionFailures[0].Code, Is.EqualTo(DiagnosticCode.CapabilityConflict));
        }

        [Test]
        public void ASelectionOverrideForANonSelectablePolicyIsACatalogError()
        {
            foreach (CompositionPolicy policy in new[]
                     {
                         CompositionPolicy.Additive,
                         CompositionPolicy.Ordered,
                         CompositionPolicy.Incompatible,
                     })
            {
                FixtureBuilder builder = Builder(policy, nearPriority: 0, farPriority: 0);
                builder.Select(Capability, FarProvider, atTarget: Target);

                DerivationResult result = DerivationAssert.Rejected(
                    DerivationEngine.Derive(Of(builder), Source(), DerivationOptions.Default, null),
                    DerivationRejectionKind.ValidationFailed);

                Assert.That(
                    result.ValidationProblems[0].Subject,
                    Is.EqualTo("selection-illegal-policy"),
                    "SelectProvider is legal only for Replace or Exclusive (P-018).");
            }
        }

        [Test]
        public void RetractingOneOfTwoSupportersPreservesTheSurvivorAndReportsTheLostSupport()
        {
            FixtureBuilder builder = Builder(CompositionPolicy.Additive, nearPriority: 0, farPriority: 0);
            DerivationSnapshot before = Of(builder);
            DerivationResult previous = Derivation(before);

            EffectiveSlot shared = DerivationAssert.SlotOf(previous, FixtureIds.Target(Target), Capability);
            Assert.That(shared.Support.Count, Is.EqualTo(2), "Two contributions support the derived slot (P-017).");

            builder.RemoveInstall(FarProvider);
            DerivationResult after = DerivationAssert.Accepted(
                DerivationEngine.Derive(Of(builder), Source(), DerivationOptions.Default, previous));

            EffectiveSlot surviving = DerivationAssert.SlotOf(after, FixtureIds.Target(Target), Capability);
            Assert.That(surviving.Support.Count, Is.EqualTo(1), "The surviving support is preserved, not deleted (TEST-005).");
            Assert.That(surviving.Support[0].Provider, Is.EqualTo(FixtureIds.Installation(NearProvider)));
            Assert.That(surviving.IsEmpty, Is.False);

            DerivationDelta delta = after.Delta!;
            Assert.That(delta.Removed.Count, Is.EqualTo(1));
            Assert.That(delta.Removed[0].Provider, Is.EqualTo(FixtureIds.Installation(FarProvider)));
            Assert.That(delta.AffectedTargets.Count, Is.EqualTo(1));
            Assert.That(delta.AffectedTargets[0], Is.EqualTo(FixtureIds.Target(Target)));
            Assert.That(
                delta.Slots.Count,
                Is.EqualTo(1),
                "The slot changed rather than being removed and recreated: the other contributor still supports it.");
            Assert.That(delta.Slots[0].Removed, Is.False);
            Assert.That(delta.Slots[0].LostSupport.Count, Is.EqualTo(1));
        }

        [Test]
        public void RemovingTheLastSupporterRemovesTheSlotAndItsSupport()
        {
            FixtureBuilder builder = Builder(CompositionPolicy.Replace, nearPriority: 0, farPriority: 0);
            DerivationResult previous = Derivation(Of(builder));
            builder.RemoveInstall(NearProvider);
            builder.RemoveInstall(FarProvider);

            DerivationResult after = DerivationAssert.Accepted(
                DerivationEngine.Derive(Of(builder), Source(), DerivationOptions.Default, previous));

            Assert.That(DerivationAssert.HasCapability(after, FixtureIds.Target(Target), Capability), Is.False);
            Assert.That(after.AssemblyOf(FixtureIds.Target(Target))!.IsBaseOnly, Is.True);
            Assert.That(after.Delta!.Slots.Count, Is.EqualTo(1));
            Assert.That(after.Delta!.Slots[0].Removed, Is.True);
            Assert.That(after.Delta!.Slots[0].LostSupport.Count, Is.EqualTo(1), "A Replace slot is supported by exactly its winner; the loser was shadowed, not support (P-019).");
        }

        [Test]
        public void ReconfiguringAPayloadChangesTheValueWhileTheContributionIdentitySurvives()
        {
            FixtureBuilder builder = Builder(CompositionPolicy.Replace, nearPriority: 0, farPriority: 0);
            DerivationResult previous = Derivation(Of(builder));
            ContributionKey before = DerivationAssert
                .SlotOf(previous, FixtureIds.Target(Target), Capability)
                .Support[0]
                .Key;

            builder.ReplaceRulePayload(NearProvider, NearProvider + ".rule", FixturePayload.Tag("prec.near.reconfigured"));
            DerivationResult after = DerivationAssert.Accepted(
                DerivationEngine.Derive(Of(builder), Source(), DerivationOptions.Default, previous));

            EffectiveSlot slot = DerivationAssert.SlotOf(after, FixtureIds.Target(Target), Capability);
            ContributionKey now = slot.Support[0].Key;
            Assert.That(
                now,
                Is.EqualTo(before),
                "Configuration changes identity-preservingly: the same contribution key, a new immutable revision (P-017).");
            Assert.That(DerivationAssert.IdValue(slot), Is.EqualTo(FixtureIds.Id("prec.near.reconfigured")));
            Assert.That(after.Delta!.Changed.Count, Is.EqualTo(1), "A reconfiguration is a change, not a remove plus add.");
            Assert.That(after.Delta!.Added.Count, Is.EqualTo(0));
            Assert.That(after.Delta!.Removed.Count, Is.EqualTo(0));
        }

        [Test]
        public void AnInactiveInstallationContributesNothingAndItsPriorContributionsRetract()
        {
            FixtureBuilder builder = Builder(CompositionPolicy.Replace, nearPriority: 0, farPriority: 0);
            DerivationResult previous = Derivation(Of(builder));

            // P-012: loss of a required dependency retracts the consumer's active contributions in the same plan.
            builder.ReplaceInstallState(NearProvider, InstallationState.WaitingForDependencies);
            DerivationResult after = DerivationAssert.Accepted(
                DerivationEngine.Derive(Of(builder), Source(), DerivationOptions.Default, previous));

            EffectiveSlot slot = DerivationAssert.SlotOf(after, FixtureIds.Target(Target), Capability);
            Assert.That(slot.Support.Count, Is.EqualTo(1));
            Assert.That(slot.Support[0].Provider, Is.EqualTo(FixtureIds.Installation(FarProvider)));
            Assert.That(after.Delta!.Removed.Count, Is.EqualTo(1));
        }

        [Test]
        public void ReplayingTheSameDerivationProducesTheSameResultHash()
        {
            FixtureBuilder builder = Builder(CompositionPolicy.Additive, nearPriority: 0, farPriority: 0);
            FixtureComposition parts = builder.Build(PropagationMode.Automatic, new CompositionRevision(1UL), AssemblyEpoch.First);

            DerivationResult first = Derivation(parts.ToSnapshot());
            DerivationResult second = Derivation(parts.ToSnapshot());

            Assert.That(second.ResultHash, Is.EqualTo(first.ResultHash), "A replay creates no duplicate assembly (TEST-004).");
            Assert.That(
                DerivationProjection.SemanticsHash(second),
                Is.EqualTo(DerivationProjection.SemanticsHash(first)));
        }

        private static FixtureBuilder Builder(CompositionPolicy policy, int nearPriority, int farPriority)
        {
            FixtureBuilder builder = new FixtureBuilder(World)
                .Scope(Root, null)
                .Scope(Far, Root)
                .Scope(Near, Far)
                .Contract(Capability, 0, new[]
                {
                    new FixtureSlot(
                        "prec.value-schema",
                        policy,
                        reducer: policy == CompositionPolicy.Additive
                            ? FixtureIds.Key("prec.reducer.int32-sum")
                            : default(FactoryKey)),
                })
                .Target(Target, Near, Recipe);

            builder.Install(NearProvider, Near, nearPriority, Rules(NearProvider, NearProvider + ".value", policy, 0), state: InstallationState.Active);
            builder.Install(FarProvider, Far, farPriority, Rules(FarProvider, FarProvider + ".value", policy, 0), state: InstallationState.Active);
            return builder;
        }

        private static IReadOnlyList<DerivationRule> Rules(
            string provider,
            string payloadName,
            CompositionPolicy policy,
            int priority)
        {
            FrozenPayload payload = policy == CompositionPolicy.Additive
                ? FixturePayload.Int32(1)
                : FixturePayload.Tag(payloadName);

            return new List<DerivationRule>
            {
                FixtureBuilder.Rule(
                    provider + ".rule",
                    Capability,
                    0,
                    1U,
                    FixtureBuilder.Selector(Recipe),
                    FixtureIds.Key("prec.predicate.always"),
                    null,
                    PropagationReach.SelfAndDescendants,
                    true,
                    priority,
                    policy,
                    payload),
            };
        }

        private static DerivationSnapshot Of(FixtureBuilder builder) =>
            builder.Build(PropagationMode.Automatic, new CompositionRevision(1UL), AssemblyEpoch.First).ToSnapshot();

        private static DerivationResult Derivation(DerivationSnapshot snapshot) =>
            DerivationAssert.Accepted(DerivationEngine.Derive(snapshot, Source(), DerivationOptions.Default, null));

        private static FixtureValueSource Source() =>
            new FixtureValueSource()
                .RegisterInt32Sum("prec.reducer.int32-sum")
                .RegisterAlwaysPredicate("prec.predicate.always");
    }
}

// GameCore.Derivation tests — complete provenance and cost visibility (P-026, P-022, TEST-004's explain clause).
//
// P-026: `Explain(target, capability, token)` returns matching and rejected rules, source scope path, descriptor
// evidence, exclusions/boundaries, mode gate, stratum, candidates, composition decisions, support ids and the
// resulting recipe hash; records stay reconstructable for the retained epoch. P-022: each operation reports the
// counts and reasons, and the diagnostic retains no unbounded string tree per entity.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Derivation.Fixtures;
using NUnit.Framework;

namespace GameCore.Derivation.Tests
{
    [TestFixture]
    public sealed class ProvenanceTests
    {
        private static WorldId World { get; } = new WorldId(FixtureIds.Id("gamecore.world.provenance-tests"));

        [Test]
        public void AnExplanationNamesTheProviderRuleScopePathStratumAndRecipeHash()
        {
            DerivationResult result = Narrative();
            TargetId mara = FixtureIds.Target(NarrativeComposition.Mara);

            DerivationExplanation? explanation = result.ExplanationOf(
                mara, FixtureIds.Capability(NarrativeComposition.ConversationBinding));

            Assert.That(explanation, Is.Not.Null);
            Assert.That(explanation!.Winners.Count, Is.EqualTo(1), "The matching rule is listed with its support.");
            Assert.That(
                explanation.Winners[0].Provider,
                Is.EqualTo(new ProviderInstallationId(FixtureIds.Instance(NarrativeComposition.ChapterOneInstall).Value)));
            Assert.That(
                explanation.Winners[0].Rule,
                Is.EqualTo(FixtureIds.Rule(NarrativeComposition.DialogueRule("chapter-one"))));
            Assert.That(explanation.Mode, Is.EqualTo(PropagationMode.Automatic));
            Assert.That(explanation.Stratum, Is.EqualTo(NarrativeComposition.BindingStratum));
            Assert.That(explanation.RecipeHash.IsEmpty, Is.False, "The explanation carries the resulting recipe hash.");
            Assert.That(explanation.DescriptorEvidence.Count, Is.GreaterThan(0), "Descriptor evidence is interned (P-015).");
            Assert.That(
                explanation.ScopePath.Count,
                Is.EqualTo(3),
                "The scope path walks from the target scope up to the world root (P-026).");
            Assert.That(explanation.ScopePath[0], Is.EqualTo(FixtureIds.Scope(NarrativeComposition.Village)));
            Assert.That(explanation.ScopePath[1], Is.EqualTo(FixtureIds.Scope(NarrativeComposition.ChapterOne)));
            Assert.That(explanation.ScopePath[2], Is.EqualTo(FixtureIds.Scope(NarrativeComposition.StoryWorld)));

            CandidateDecision decision = explanation.Decisions[0];
            Assert.That(decision.ModeGate, Is.EqualTo(ModeGateDecision.AutomaticDescendantGrant));
            Assert.That(decision.ProviderDepth, Is.EqualTo(1), "Chapter One is one level below the world root.");
            Assert.That(decision.Status, Is.EqualTo(CandidateStatus.Emitted));
        }

        [Test]
        public void AModeSwitchMovesTheStoryIntoOneCoherentExplanation()
        {
            Derivations both = Derive();

            CandidateDecision automaticDecision = SingleDecision(
                both.Automatic, NarrativeComposition.Mara, NarrativeComposition.ConversationBinding);
            CandidateDecision conservativeDecision = SingleDecision(
                both.Conservative, NarrativeComposition.Mara, NarrativeComposition.ConversationBinding);

            Assert.That(automaticDecision.ModeGate, Is.EqualTo(ModeGateDecision.AutomaticDescendantGrant));
            Assert.That(conservativeDecision.Status, Is.EqualTo(CandidateStatus.ModeDenied));
            Assert.That(conservativeDecision.ModeGate, Is.EqualTo(ModeGateDecision.ConservativeNotGranted));
            Assert.That(
                conservativeDecision.Diagnostic,
                Is.EqualTo(DiagnosticCode.Ineligible),
                "A gated candidate is reported Ineligible, not silently dropped (P-015, P-026).");
        }

        [Test]
        public void ALosingCandidateIsProvenanceAndNeverSupport()
        {
            FixtureBuilder builder = new FixtureBuilder(World)
                .Scope("prov.root", null)
                .Scope("prov.near", "prov.root")
                .Contract("prov.value", 0, new[] { new FixtureSlot("prov.value-schema", CompositionPolicy.Replace) })
                .Target("prov.target", "prov.near", "prov.member-recipe");

            builder.Install("prov.near-provider", "prov.near", 0, Rules("prov.near-provider"), state: InstallationState.Active);
            builder.Install("prov.far-provider", "prov.root", 0, Rules("prov.far-provider"), state: InstallationState.Active);

            DerivationResult result = DerivationAssert.Accepted(DerivationEngine.Derive(
                builder.Build(PropagationMode.Automatic, new CompositionRevision(1UL), AssemblyEpoch.First).ToSnapshot(),
                new FixtureValueSource().RegisterAlwaysPredicate("prov.predicate.always"),
                DerivationOptions.Default,
                null));

            DerivationExplanation? explanation = result.ExplanationOf(
                FixtureIds.Target("prov.target"), FixtureIds.Capability("prov.value"));
            Assert.That(explanation!.Winners.Count, Is.EqualTo(1));
            Assert.That(explanation.Shadowed.Count, Is.EqualTo(1), "The losing candidate stays inspectable (P-019).");
            Assert.That(
                explanation.Shadowed[0].Provider,
                Is.EqualTo(new ProviderInstallationId(FixtureIds.Instance("prov.far-provider").Value)));
            Assert.That(
                explanation.Winners[0].Key,
                Is.Not.EqualTo(explanation.Shadowed[0].Key),
                "A losing candidate is never part of the support set (P-017).");
            Assert.That(explanation.Decisions.Count, Is.EqualTo(2));
        }

        [Test]
        public void AnIsolationBoundaryIsRecordedAsTheOriginatingBlocker()
        {
            DerivationResult result = Narrative();
            DerivationExplanation? explanation = result.ExplanationOf(
                FixtureIds.Target(NarrativeComposition.Display),
                FixtureIds.Capability(NarrativeComposition.ConversationBinding));

            Assert.That(explanation, Is.Not.Null, "The museum target still gets an explanation (TEST-004).");
            Assert.That(explanation!.Winners.Count, Is.EqualTo(0));
            Assert.That(explanation.Decisions.Count, Is.EqualTo(1));
            Assert.That(explanation.Decisions[0].Status, Is.EqualTo(CandidateStatus.BlockedByBoundary));
            Assert.That(explanation.Decisions[0].Boundaries.Count, Is.EqualTo(1));
            Assert.That(
                explanation.Decisions[0].Boundaries[0].BoundaryScope,
                Is.EqualTo(FixtureIds.Scope(NarrativeComposition.Museum)),
                "The explanation identifies the blocker and its scope (REF-P02).");
            Assert.That(explanation.Decisions[0].EvidenceKeys.Count, Is.GreaterThan(0));
        }

        [Test]
        public void ATargetOutsideEveryCandidatePopulationGetsNoInventedExplanationButStaysUnchanged()
        {
            DerivationResult result = Narrative();
            TargetId crowd = FixtureIds.Target(NarrativeComposition.CrowdProp);

            TargetAssembly? assembly = result.AssemblyOf(crowd);
            Assert.That(assembly, Is.Not.Null, "Every live target has an assembly, even when nothing derives for it.");
            Assert.That(assembly!.IsBaseOnly, Is.True, "An incompatible target receives nothing (TEST-004).");
            Assert.That(assembly.Slots.Count, Is.EqualTo(0));
            Assert.That(
                result.ExplanationOf(crowd, FixtureIds.Capability(NarrativeComposition.ConversationBinding)),
                Is.Null,
                "No rule ever considered this target, so no explanation is invented for it (P-015).");
        }

        [Test]
        public void AnIneligibleSelectorVersionIsReportedWithItsOwnStatus()
        {
            FixtureBuilder builder = new FixtureBuilder(World)
                .Scope("prov.root", null)
                .Scope("prov.leaf", "prov.root")
                .Contract("prov.value", 0, new[] { new FixtureSlot("prov.value-schema", CompositionPolicy.Replace) })
                .Target("prov.target", "prov.leaf", "prov.member-recipe", recipeSchemaVersion: 2U)
                .Install("prov.provider", "prov.root", 0, Rules("prov.provider"), state: InstallationState.Active);

            DerivationResult result = DerivationAssert.Accepted(DerivationEngine.Derive(
                builder.Build(PropagationMode.Automatic, new CompositionRevision(1UL), AssemblyEpoch.First).ToSnapshot(),
                new FixtureValueSource().RegisterAlwaysPredicate("prov.predicate.always"),
                DerivationOptions.Default,
                null));

            CandidateDecision decision = result.DecisionsOf(
                FixtureIds.Target("prov.target"), FixtureIds.Capability("prov.value"))[0];
            Assert.That(decision.Status, Is.EqualTo(CandidateStatus.SelectorVersionMismatch));
            Assert.That(decision.Reason, Is.EqualTo(CandidateRejectionReason.SelectorVersionMismatch));
            Assert.That(decision.Diagnostic, Is.EqualTo(DiagnosticCode.Ineligible));
        }

        [Test]
        public void TheExplainReaderPagesBoundedRecordsAndLabelsItsSource()
        {
            DerivationResult result = Narrative();
            TargetId mara = FixtureIds.Target(NarrativeComposition.Mara);
            CapabilityId capability = FixtureIds.Capability(NarrativeComposition.ConversationBinding);
            SnapshotToken token = new SnapshotToken(World, result.Snapshot.Epoch, LogicalStepId.Zero);

            DerivationExplainReader published = DerivationExplainReader.Published(result);
            ExplanationPage page = published.Explain(mara, capability, token, ExplanationPageRequest.FirstPage(1U));

            Assert.That(page.Source, Is.EqualTo(ExplanationSource.PublishedComposition));
            Assert.That(page.Matching.Count, Is.EqualTo(1));
            Assert.That(page.Rejected.Count, Is.EqualTo(0));
            Assert.That(page.TotalMatching, Is.EqualTo(1UL));
            Assert.That(page.ScopePath.Count, Is.GreaterThan(0));
            Assert.That(page.RecipeHash.IsEmpty, Is.False);
            Assert.That(page.Mode, Is.EqualTo(PropagationMode.Automatic));
            Assert.That(page.HasMore, Is.False);

            DerivationExplainReader staged = DerivationExplainReader.Staged(result);
            ExplanationPage stagedPage = staged.Explain(mara, capability, token, ExplanationPageRequest.FirstPage(4U));
            Assert.That(
                stagedPage.Source,
                Is.EqualTo(ExplanationSource.StagedPlan),
                "Staged diagnostics are labelled and never presented as world observation (05 s5).");

            ExplanationPage unknown = published.Explain(
                FixtureIds.Target(NarrativeComposition.CrowdProp),
                capability,
                token,
                ExplanationPageRequest.FirstPage(4U));
            Assert.That(unknown.TotalMatching, Is.EqualTo(0UL));
            Assert.That(unknown.TotalRejected, Is.EqualTo(0UL));
        }

        [Test]
        public void TheCountersReportCostAndIndexVisitsWithoutUnboundedStrings()
        {
            Derivations both = Derive();

            CostCounters counters = both.Automatic.Counters;
            Assert.That(counters.WithinBudget, Is.True);
            Assert.That(counters.ExaminedCandidates, Is.GreaterThan(0));
            Assert.That(counters.EmittedContributions, Is.GreaterThan(0));
            Assert.That(counters.IndexBucketsVisited, Is.GreaterThan(0));
            Assert.That(counters.IndexTargetsVisited, Is.GreaterThan(0), "The walk is index-driven (P-023).");
            Assert.That(counters.RulesEvaluated, Is.GreaterThan(0));
            Assert.That(counters.TemporaryBytes, Is.GreaterThan(0L));
            Assert.That(counters.TopFanOutCauses.Count, Is.EqualTo(0));
            Assert.That(
                DerivationProjection.CounterText(both.Automatic),
                Does.Contain("candidates="),
                "The cost report is a bounded, stable text, not a per-entity string tree (P-022).");
        }

        [Test]
        public void ARandomizedSmallOperationSequenceKeepsEveryExplanationReconstructable()
        {
            // A fourth of the differential sweep, asserted on provenance rather than values: whatever the sequence
            // produced, every decision must name a rule that exists and every winner must be a known candidate.
            for (uint seed = 1; seed <= 25U; seed++)
            {
                FixtureComposition parts = RandomComposition.Build(seed);
                DerivationSnapshot snapshot = parts.ToSnapshot();
                FixtureValueSource values = RandomComposition.ValueSource();

                DerivationResult result = DerivationEngine.Derive(snapshot, values, DerivationOptions.Default, null);
                if (!result.Accepted)
                {
                    continue;
                }

                for (int i = 0; i < result.Decisions.Count; i++)
                {
                    CandidateDecision decision = result.Decisions[i];
                    Assert.That(
                        snapshot.TryGetInstall(new PluginInstanceId(decision.Provider.Value), out DerivationInstall? install),
                        Is.True,
                        "Seed " + seed + ": a decision names a declared installation.");
                    Assert.That(install!.Manifest.DerivationRules.Count, Is.GreaterThan(0));
                    Assert.That(snapshot.TryGetTarget(decision.Target, out DerivationTarget? _), Is.True);
                }

                for (int i = 0; i < result.Explanations.Count; i++)
                {
                    DerivationExplanation explanation = result.Explanations[i];
                    for (int w = 0; w < explanation.Winners.Count; w++)
                    {
                        Assert.That(
                            explanation.Winners[w].Target,
                            Is.EqualTo(explanation.Target),
                            "Seed " + seed + ": support identity belongs to the explained target (P-017).");
                    }
                }
            }
        }

        private struct Derivations
        {
            public DerivationResult Automatic;
            public DerivationResult Conservative;
        }

        private static Derivations Derive()
        {
            FixtureBuilder builder = NarrativeComposition.Builder();
            FixtureValueSource values = NarrativeComposition.ValueSource();

            Derivations result;
            result.Automatic = DerivationAssert.Accepted(DerivationEngine.Derive(
                builder.Build(PropagationMode.Automatic, new CompositionRevision(1UL), AssemblyEpoch.First).ToSnapshot(),
                values,
                DerivationOptions.Default,
                null));
            result.Conservative = DerivationAssert.Accepted(DerivationEngine.Derive(
                builder.Build(PropagationMode.Conservative, new CompositionRevision(1UL), AssemblyEpoch.First).ToSnapshot(),
                values,
                DerivationOptions.Default,
                null));
            return result;
        }

        private static DerivationResult Narrative() => Derive().Automatic;

        private static CandidateDecision SingleDecision(DerivationResult result, string target, string capability)
        {
            IReadOnlyList<CandidateDecision> decisions = result.DecisionsOf(
                FixtureIds.Target(target), FixtureIds.Capability(capability));
            Assert.That(decisions.Count, Is.EqualTo(1), "Expected exactly one candidate decision for " + target + ".");
            return decisions[0];
        }

        private static IReadOnlyList<DerivationRule> Rules(string provider) =>
            new List<DerivationRule>
            {
                FixtureBuilder.Rule(
                    provider + ".rule",
                    "prov.value",
                    0,
                    1U,
                    FixtureBuilder.Selector("prov.member-recipe"),
                    FixtureIds.Key("prov.predicate.always"),
                    null,
                    PropagationReach.SelfAndDescendants,
                    true,
                    0,
                    CompositionPolicy.Replace,
                    FixturePayload.Tag(provider + ".value")),
            };
    }
}

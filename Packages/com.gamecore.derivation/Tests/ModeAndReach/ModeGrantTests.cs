// GameCore.Derivation tests — mode semantics and reach (P-013, P-014, P-015, TEST-006's derivation half).
//
// The mode gate of 02 s5 is one small table, so it is tested as a table: a `LocalOnly` rule is permitted at its
// installation scope in both modes; a descendant rule is permitted in Automatic without any import; in
// Conservative it needs export plus an explicit import, or a complete target opt-in. Denial along the propagation
// path wins over imports and opt-ins in both modes, and a `Descendants` rule that includes the provider's own
// scope still goes through the gate (P-013: "This mode gate also applies to same-scope targets selected by a
// Descendants rule's SelfAndDescendants selector").
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Derivation.Fixtures;
using NUnit.Framework;

namespace GameCore.Derivation.Tests
{
    [TestFixture]
    public sealed class ModeGrantTests
    {
        private const string LocalOnlyCapability = "test.local-only";
        private const string LocalOnlySchema = "test.local-only-recipe";
        private const string DescendantCapability = "test.role-grant";
        private const string DescendantSchema = "test.member-recipe";

        private const string Root = "test.root";
        private const string ProviderScope = "test.provider-scope";
        private const string Child = "test.child";
        private const string OtherBranch = "test.other-branch";
        private const string Provider = "test.provider";

        private const string SameScopeTarget = "test.target-in-provider-scope";
        private const string ChildTarget = "test.target-in-child";
        private const string OtherTarget = "test.target-in-other-branch";
        private const string OptedInTarget = "test.target-opted-in";
        private const string FutureTarget = "test.target-created-later";
        private const string ForeignTarget = "test.target-foreign";

        /// <summary>Immutable descriptor tag that makes a target an eligible occupant (P-015).</summary>
        private const string EligibleTag = "test.tag.occupant";

        private static WorldId World { get; } = new WorldId(FixtureIds.Id("gamecore.world.mode-tests"));

        [Test]
        public void AutomaticGrantsEligibleDescendantsWithoutAnyImport()
        {
            DerivationResult result = Derive(PropagationMode.Automatic);

            Assert.That(DerivationAssert.HasCapability(result, FixtureIds.Target(ChildTarget), DescendantCapability), Is.True);
            Assert.That(
                DerivationAssert.HasCapability(result, FixtureIds.Target(OtherTarget), DescendantCapability),
                Is.False,
                "A sibling branch is never in the reach domain of a descendant rule (P-011, P-013).");

            // No import is manufactured or required: the descriptor still declares none (P-013).
            DerivationSnapshot snapshot = result.Snapshot;
            Assert.That(snapshot.TryGetTarget(FixtureIds.Target(ChildTarget), out DerivationTarget? child), Is.True);
            Assert.That(child!.Descriptor.Imports.Count, Is.EqualTo(0));
            Assert.That(child.Descriptor.OptIns.Count, Is.EqualTo(0));
        }

        [Test]
        public void AutomaticAppliesToAFutureDescendantWithNoPerInstanceImport()
        {
            // FutureTarget is declared by the fixture as a compatible occupant created after the mount; the
            // interesting case is a target that did not exist when the provider was mounted.
            DerivationResult result = Derive(PropagationMode.Automatic);

            Assert.That(
                DerivationAssert.HasCapability(result, FixtureIds.Target(FutureTarget), DescendantCapability),
                Is.True,
                "A future eligible descendant derives in Automatic without an instance-specific import (TEST-004).");
        }

        [Test]
        public void ConservativeDeniesADescendantWithoutExportImportOrOptIn()
        {
            DerivationResult result = Derive(PropagationMode.Conservative);

            Assert.That(
                DerivationAssert.HasCapability(result, FixtureIds.Target(ChildTarget), DescendantCapability),
                Is.False,
                "Conservative without an export-plus-import grant and without an opt-in denies the candidate (P-013).");
            Assert.That(
                DerivationAssert.HasCapability(result, FixtureIds.Target(OptedInTarget), DescendantCapability),
                Is.True,
                "A complete target opt-in naming the provider installation and capability is one valid grant (P-013).");

            CandidateDecision? childDecision = FindDecision(result, FixtureIds.Target(ChildTarget), DescendantCapability);
            Assert.That(childDecision, Is.Not.Null);
            Assert.That(childDecision!.Status, Is.EqualTo(CandidateStatus.ModeDenied));
            Assert.That(childDecision.Reason, Is.EqualTo(CandidateRejectionReason.ModeNotGranted));
            Assert.That(childDecision.ModeGate, Is.EqualTo(ModeGateDecision.ConservativeNotGranted));
        }

        [Test]
        public void ConservativeGrantsAnExportedCapabilityThatTheTargetImports()
        {

            FixtureBuilder builder = Builder();
            builder.AddTargetImport(ChildTarget, DescendantCapability, Provider);

            DerivationResult result = DerivationAssert.Accepted(
                DerivationEngine.Derive(Snapshot(builder, PropagationMode.Conservative), Source(), DerivationOptions.Default, null));

            Assert.That(
                DerivationAssert.HasCapability(result, FixtureIds.Target(ChildTarget), DescendantCapability),
                Is.True,
                "ExportToDescendants plus an explicit import is a valid Conservative grant (P-013).");
            CandidateDecision? decision = FindDecision(result, FixtureIds.Target(ChildTarget), DescendantCapability);
            Assert.That(decision!.ModeGate, Is.EqualTo(ModeGateDecision.ConservativeExportedAndImported));
        }
        [Test]
        public void ConservativeGrantsAnExportedCapabilityImportedByTheTargetScope()
        {
            // A scope import is the "reusable recipe or scope configuration" variant of 02 s5.
            FixtureBuilder builder = new FixtureBuilder(World)
                .Scope(Root, null)
                .Scope(ProviderScope, Root)
                .Scope(Child, ProviderScope, importProviderPairs: new[] { DescendantCapability, Provider })
                .Scope(OtherBranch, Root)
                .Target(ChildTarget, Child, DescendantSchema)
                .Contract(DescendantCapability, 0, new[] { new FixtureSlot(DescendantSchema, CompositionPolicy.Replace) })
                .Install(Provider, ProviderScope, 0, Rules(), state: InstallationState.Active);

            DerivationResult result = DerivationAssert.Accepted(
                DerivationEngine.Derive(Snapshot(builder, PropagationMode.Conservative), Source(), DerivationOptions.Default, null));

            Assert.That(DerivationAssert.HasCapability(result, FixtureIds.Target(ChildTarget), DescendantCapability), Is.True);
            CandidateDecision? decision = FindDecision(result, FixtureIds.Target(ChildTarget), DescendantCapability);
            Assert.That(decision!.ModeGate, Is.EqualTo(ModeGateDecision.ConservativeExportedAndImported));
        }

        [Test]
        public void AnOptInCannotGrantAnIneligibleTarget()
        {
            // ForeignTarget carries the complete opt-in and the selector schema but not the occupant tag the
            // rule's static predicate requires, so eligibility still fails in both modes.
            DerivationResult automatic = Derive(PropagationMode.Automatic);
            DerivationResult conservative = Derive(PropagationMode.Conservative);

            Assert.That(DerivationAssert.HasCapability(automatic, FixtureIds.Target(ForeignTarget), DescendantCapability), Is.False);
            Assert.That(
                DerivationAssert.HasCapability(conservative, FixtureIds.Target(ForeignTarget), DescendantCapability),
                Is.False,
                "Opt-in is not permission to bypass eligibility (P-013).");

            CandidateDecision decision = SingleDecision(conservative, ForeignTarget, DescendantCapability);
            Assert.That(
                decision.Status,
                Is.EqualTo(CandidateStatus.PredicateRejected),
                "The denial is eligibility, not the mode gate: the opt-in was honoured and then the predicate refused.");
            Assert.That(decision.ModeGate, Is.EqualTo(ModeGateDecision.ConservativeTargetOptIn));
        }

        [Test]
        public void LocalOnlyIsPermittedAtItsInstallationScopeInBothModesAndNeverReachesDescendants()
        {
            foreach (PropagationMode mode in new[] { PropagationMode.Automatic, PropagationMode.Conservative })
            {
                DerivationResult result = Derive(mode);

                Assert.That(
                    DerivationAssert.HasCapability(result, FixtureIds.Target(SameScopeTarget), LocalOnlyCapability),
                    Is.True,
                    "A LocalOnly rule applies at its installation scope in " + mode.ToString() + " (P-013).");
                Assert.That(
                    DerivationAssert.HasCapability(result, FixtureIds.Target(ChildTarget), LocalOnlyCapability),
                    Is.False,
                    "LocalOnly never propagates to descendants (" + mode.ToString() + ").");

                CandidateDecision? decision = FindDecision(result, FixtureIds.Target(SameScopeTarget), LocalOnlyCapability);
                Assert.That(decision, Is.Not.Null);
                Assert.That(decision!.ModeGate, Is.EqualTo(ModeGateDecision.LocalOnlyRule));
            }
        }

        [Test]
        public void ADescendantsRuleStillUsesTheModeGateForATargetInTheProvidersOwnScope()
        {
            // The provider scope holds SameScopeTarget, and the descendant rule's SelfAndDescendants selector
            // includes that scope: Automatic grants it, Conservative does not (P-013).
            Assert.That(
                DerivationAssert.HasCapability(
                    Derive(PropagationMode.Automatic), FixtureIds.Target(SameScopeTarget), DescendantCapability),
                Is.True);
            Assert.That(
                DerivationAssert.HasCapability(
                    Derive(PropagationMode.Conservative), FixtureIds.Target(SameScopeTarget), DescendantCapability),
                Is.False,
                "A same-scope target of a Descendants rule is gated exactly like a descendant target (P-013).");
        }

        [Test]
        public void SwitchingModesChangesOnlyGatedRulesAndKeepsLocalOnlyContributionsAndStableState()
        {
            DerivationResult automatic = Derive(PropagationMode.Automatic);
            DerivationResult conservative = Derive(PropagationMode.Conservative);

            // The LocalOnly contribution survives both directions; the gated one does not survive Conservative.
            Assert.That(DerivationAssert.HasCapability(automatic, FixtureIds.Target(ChildTarget), LocalOnlyCapability), Is.False);
            Assert.That(DerivationAssert.HasCapability(conservative, FixtureIds.Target(ChildTarget), LocalOnlyCapability), Is.False);
            Assert.That(DerivationAssert.HasCapability(automatic, FixtureIds.Target(SameScopeTarget), LocalOnlyCapability), Is.True);
            Assert.That(DerivationAssert.HasCapability(conservative, FixtureIds.Target(SameScopeTarget), LocalOnlyCapability), Is.True);

            // The switch is a normal re-derivation of the same world: the target identity and its base recipe
            // survive, and the mode itself is the only world-level difference (P-014).
            TargetAssembly? child = conservative.AssemblyOf(FixtureIds.Target(ChildTarget));
            Assert.That(child, Is.Not.Null);
            Assert.That(child!.BaseRecipe, Is.EqualTo(automatic.AssemblyOf(FixtureIds.Target(ChildTarget))!.BaseRecipe));
            Assert.That(conservative.Snapshot.Mode, Is.EqualTo(PropagationMode.Conservative));
        }

        [Test]
        public void AnUngatedRuleIsUnaffectedByTheModeGateTable()
        {
            // A rule whose reach never leaves its own scope behaves like LocalOnly for the same-scope target; the
            // table is checked through the returned decision so "otherwise => not permitted" is not guessed.
            DerivationResult automatic = Derive(PropagationMode.Automatic);
            DerivationResult conservative = Derive(PropagationMode.Conservative);
            CandidateDecision? local = FindDecision(conservative, FixtureIds.Target(SameScopeTarget), LocalOnlyCapability);
            CandidateDecision? gate = FindDecision(automatic, FixtureIds.Target(SameScopeTarget), DescendantCapability);

            Assert.That(local!.ModeGate, Is.EqualTo(ModeGateDecision.LocalOnlyRule));
            Assert.That(gate!.ModeGate, Is.EqualTo(ModeGateDecision.AutomaticDescendantGrant));
        }

        private static CandidateDecision SingleDecision(DerivationResult result, string target, string capability)
        {
            IReadOnlyList<CandidateDecision> decisions = result.DecisionsOf(
                FixtureIds.Target(target), FixtureIds.Capability(capability));
            Assert.That(decisions.Count, Is.EqualTo(1), "Expected exactly one candidate decision for " + target + ".");
            return decisions[0];
        }

        private static CandidateDecision? FindDecision(DerivationResult result, TargetId target, string capability)
        {
            IReadOnlyList<CandidateDecision> decisions = result.DecisionsOf(target, FixtureIds.Capability(capability));
            for (int i = 0; i < decisions.Count; i++)
            {
                if (decisions[i].Status == CandidateStatus.Emitted || decisions[i].Status == CandidateStatus.Shadowed)
                {
                    return decisions[i];
                }
            }

            return decisions.Count > 0 ? decisions[0] : null;
        }

        private static DerivationResult Derive(PropagationMode mode) =>
            DerivationAssert.Accepted(
                DerivationEngine.Derive(Snapshot(Builder(), mode), Source(), DerivationOptions.Default, null));

        private static DerivationSnapshot Snapshot(FixtureBuilder builder, PropagationMode mode) =>
            builder.Build(mode, new CompositionRevision(1UL), AssemblyEpoch.First).ToSnapshot();

        private static FixtureValueSource Source() =>
            new FixtureValueSource()
                .RegisterAlwaysPredicate("test.predicate.always")
                // Eligibility is a declared-descriptor property: the occupant tag is immutable descriptor data
                // (P-015), so an opt-in cannot manufacture it (P-013).
                .RegisterTagPredicate("test.predicate.occupant", FixtureIds.Id(EligibleTag));

        private static FixtureBuilder Builder()
        {
            FixtureBuilder builder = new FixtureBuilder(World)
                .Scope(Root, null)
                .Scope(ProviderScope, Root)
                .Scope(Child, ProviderScope)
                .Scope(OtherBranch, Root);

            builder
                .Contract(LocalOnlyCapability, 0, new[] { new FixtureSlot(LocalOnlySchema, CompositionPolicy.Replace) })
                .Contract(DescendantCapability, 0, new[] { new FixtureSlot(DescendantSchema, CompositionPolicy.Replace) });

            builder
                // The same-scope target advertises the descendant rule's selector too, so the mode gate really is
                // exercised for a target inside the provider's own scope (P-013).
                .Target(SameScopeTarget, ProviderScope, DescendantSchema, tags: new[] { EligibleTag })
                .Target(ChildTarget, Child, DescendantSchema, tags: new[] { EligibleTag })
                .Target(OtherTarget, OtherBranch, DescendantSchema, tags: new[] { EligibleTag })
                .Target(FutureTarget, Child, DescendantSchema, tags: new[] { EligibleTag })
                // The opt-in is complete — it names the provider installation and the capability — but this
                // target does not carry the occupant tag, so it stays ineligible in both modes.
                .Target(ForeignTarget, Child, DescendantSchema, optInProviderPairs: new[] { DescendantCapability, Provider })
                // The complete opt-in of the target that MUST be granted in Conservative mode.
                .Target(OptedInTarget, Child, DescendantSchema, tags: new[] { EligibleTag }, optInProviderPairs: new[] { DescendantCapability, Provider });

            builder.Install(Provider, ProviderScope, 0, Rules(), state: InstallationState.Active);
            return builder;
        }

        private static IReadOnlyList<DerivationRule> Rules() =>
            new List<DerivationRule>
            {
                FixtureBuilder.Rule(
                    "test.local-only",
                    LocalOnlyCapability,
                    0,
                    1U,
                    FixtureBuilder.Selector(DescendantSchema),
                    FixtureIds.Key("test.predicate.always"),
                    null,
                    PropagationReach.LocalOnly,
                    false,
                    0,
                    CompositionPolicy.Replace,
                    FixturePayload.Tag("test.local-only.value")),

                FixtureBuilder.Rule(
                    "test.descendant",
                    DescendantCapability,
                    0,
                    1U,
                    FixtureBuilder.Selector(DescendantSchema),
                    FixtureIds.Key("test.predicate.occupant"),
                    null,
                    PropagationReach.SelfAndDescendants,
                    true,
                    0,
                    CompositionPolicy.Replace,
                    FixturePayload.Tag("test.descendant.value")),
            };
    }
}

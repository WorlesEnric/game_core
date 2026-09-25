#nullable enable
using GameCore.Contracts;
using GameCore.Derivation.Fixtures;
using NUnit.Framework;

namespace GameCore.Rules.Narrative.Tests
{
    /// <summary>
    /// The anti-drift test of GC-010: every name this package declares must be the same name the frozen derivation
    /// fixture declares, and the production derivation must turn both into the same identity. A gameplay assembly
    /// may not reference a fixture assembly, so the names are declared twice on purpose and this fixture is what
    /// keeps the two declarations from diverging (NarrativeCompositionNames' file header, P-004).
    /// </summary>
    [TestFixture]
    public sealed class IdentityAgreementTests
    {
        private static readonly string[] ScopeNames =
        {
            NarrativeCompositionNames.StoryWorld,
            NarrativeCompositionNames.ChapterOne,
            NarrativeCompositionNames.ChapterTwo,
            NarrativeCompositionNames.Village,
            NarrativeCompositionNames.Grove,
            NarrativeCompositionNames.Museum,
            NarrativeCompositionNames.Harbor,
        };

        private static readonly string[] FixtureScopeNames =
        {
            NarrativeComposition.StoryWorld,
            NarrativeComposition.ChapterOne,
            NarrativeComposition.ChapterTwo,
            NarrativeComposition.Village,
            NarrativeComposition.Grove,
            NarrativeComposition.Museum,
            NarrativeComposition.Harbor,
        };

        private static readonly string[] RecipeNames =
        {
            NarrativeCompositionNames.VillagerRecipe,
            NarrativeCompositionNames.QuestGateRecipe,
            NarrativeCompositionNames.QuestEncounterRecipe,
            NarrativeCompositionNames.DecorativeCrowdRecipe,
        };

        private static readonly string[] FixtureRecipeNames =
        {
            NarrativeComposition.VillagerRecipe,
            NarrativeComposition.QuestGateRecipe,
            NarrativeComposition.QuestEncounterRecipe,
            NarrativeComposition.DecorativeCrowdRecipe,
        };

        private static readonly string[] TargetNames =
        {
            NarrativeCompositionNames.Mara,
            NarrativeCompositionNames.GateEast,
            NarrativeCompositionNames.CrowdProp,
            NarrativeCompositionNames.EncounterOak,
            NarrativeCompositionNames.Display,
            NarrativeCompositionNames.Sailor,
        };

        private static readonly string[] FixtureTargetNames =
        {
            NarrativeComposition.Mara,
            NarrativeComposition.GateEast,
            NarrativeComposition.CrowdProp,
            NarrativeComposition.EncounterOak,
            NarrativeComposition.Display,
            NarrativeComposition.Sailor,
        };

        [Test]
        public void TheScopeNamesAgreeAndDeriveTheSameScopeIdentity()
        {
            Assert.That(ScopeNames.Length, Is.EqualTo(7), "the seven scopes of 07 s3.1 are all covered here.");
            Assert.That(FixtureScopeNames.Length, Is.EqualTo(ScopeNames.Length));

            for (int i = 0; i < ScopeNames.Length; i++)
            {
                Assert.That(ScopeNames[i], Is.EqualTo(FixtureScopeNames[i]), "scope name " + i + " drifted apart.");
                Assert.That(NarrativeIds.Scope(ScopeNames[i]), Is.EqualTo(FixtureIds.Scope(FixtureScopeNames[i])));
                Assert.That(NarrativeIds.Id(ScopeNames[i]), Is.EqualTo(FixtureIds.Id(FixtureScopeNames[i])));
            }
        }

        [Test]
        public void TheRecipeNamesAgreeAndDeriveTheSameDefinitionIdentity()
        {
            Assert.That(RecipeNames.Length, Is.EqualTo(4), "the four reusable recipes of 07 s3.1 are all covered here.");

            for (int i = 0; i < RecipeNames.Length; i++)
            {
                Assert.That(RecipeNames[i], Is.EqualTo(FixtureRecipeNames[i]), "recipe name " + i + " drifted apart.");
                Assert.That(
                    NarrativeIds.Definition(RecipeNames[i]).Value,
                    Is.EqualTo(FixtureIds.Definition(FixtureRecipeNames[i]).Value));
                Assert.That(NarrativeIds.Id(RecipeNames[i]), Is.EqualTo(FixtureIds.Id(FixtureRecipeNames[i])));

                DefinitionRef rulesRecipe = NarrativeIds.Recipe(RecipeNames[i], FixtureRecipeNames[i]);
                DefinitionRef fixtureRecipe = FixtureIds.Recipe(FixtureRecipeNames[i], FixtureRecipeNames[i]);
                Assert.That(rulesRecipe.Id.Value, Is.EqualTo(fixtureRecipe.Id.Value));
                Assert.That(rulesRecipe.Schema.Id.Value, Is.EqualTo(fixtureRecipe.Schema.Id.Value));
                Assert.That(rulesRecipe.Schema.Version, Is.EqualTo(fixtureRecipe.Schema.Version));
            }
        }

        [Test]
        public void TheTargetNamesAgreeAndDeriveTheSameTargetIdentity()
        {
            Assert.That(TargetNames.Length, Is.EqualTo(6), "the six live targets of 07 s3.1 are all covered here.");

            for (int i = 0; i < TargetNames.Length; i++)
            {
                Assert.That(TargetNames[i], Is.EqualTo(FixtureTargetNames[i]), "target name " + i + " drifted apart.");
                Assert.That(
                    NarrativeIds.Target(TargetNames[i]).Value,
                    Is.EqualTo(FixtureIds.Target(FixtureTargetNames[i]).Value));
                Assert.That(NarrativeIds.Id(TargetNames[i]), Is.EqualTo(FixtureIds.Id(FixtureTargetNames[i])));
            }
        }

        [Test]
        public void TheChapterTagsInstallationsAndStrataAgreeWithTheFixtureVocabulary()
        {
            Assert.That(NarrativeChapters.ChapterOneTag, Is.EqualTo(NarrativeComposition.ChapterOne));
            Assert.That(NarrativeChapters.ChapterTwoTag, Is.EqualTo(NarrativeComposition.ChapterTwo));

            Assert.That(NarrativeCompositionNames.ChapterOneInstall, Is.EqualTo(NarrativeComposition.ChapterOneInstall));
            Assert.That(NarrativeCompositionNames.ChapterTwoInstall, Is.EqualTo(NarrativeComposition.ChapterTwoInstall));
            Assert.That(
                NarrativeIds.Installation(NarrativeCompositionNames.ChapterOneInstall).Value,
                Is.EqualTo(FixtureIds.Installation(NarrativeComposition.ChapterOneInstall).Value));
            Assert.That(
                NarrativeIds.Installation(NarrativeCompositionNames.ChapterTwoInstall).Value,
                Is.EqualTo(FixtureIds.Installation(NarrativeComposition.ChapterTwoInstall).Value));

            Assert.That(NarrativeCompositionNames.BindingStratum, Is.EqualTo(NarrativeComposition.BindingStratum));
            Assert.That(NarrativeCompositionNames.ChoiceStratum, Is.EqualTo(NarrativeComposition.ChoiceStratum));
            Assert.That(
                NarrativeCompositionNames.BindingStratum,
                Is.LessThan(NarrativeCompositionNames.ChoiceStratum));
        }

        [Test]
        public void ThePredicateAndRuleNamesAgreeWithTheFixtureVocabularyForEveryChapter()
        {
            Assert.That(
                NarrativeCompositionNames.AlwaysPredicateName,
                Is.EqualTo(NarrativeComposition.AlwaysPredicate),
                "the always-accepting predicate is one generated registration (P-009).");
            Assert.That(
                NarrativeIds.Key(NarrativeCompositionNames.AlwaysPredicateName).RegistrationKey,
                Is.EqualTo(FixtureIds.Key(NarrativeComposition.AlwaysPredicate).RegistrationKey));

            Assert.That(NarrativeCompositionNames.DialogueRuleSuffix, Is.EqualTo(NarrativeComposition.DialogueSuffix));
            Assert.That(NarrativeCompositionNames.GateRuleSuffix, Is.EqualTo(NarrativeComposition.GateSuffix));
            Assert.That(NarrativeCompositionNames.HookBeginRuleSuffix, Is.EqualTo(NarrativeComposition.HookBeginSuffix));
            Assert.That(NarrativeCompositionNames.HookOfferRuleSuffix, Is.EqualTo(NarrativeComposition.HookOfferSuffix));
            Assert.That(NarrativeCompositionNames.ChoiceRuleSuffix, Is.EqualTo(NarrativeComposition.ChoiceSuffix));
            Assert.That(NarrativeCompositionNames.RewardRuleSuffix, Is.EqualTo(NarrativeComposition.RewardSuffix));

            for (int i = 0; i < NarrativeChapters.Tags.Count; i++)
            {
                string tag = NarrativeChapters.Tags[i];

                Assert.That(NarrativeCompositionNames.DialogueRule(tag), Is.EqualTo(NarrativeComposition.DialogueRule(tag)));
                Assert.That(NarrativeCompositionNames.GateRule(tag), Is.EqualTo(NarrativeComposition.GateRule(tag)));
                Assert.That(NarrativeCompositionNames.HookBeginRule(tag), Is.EqualTo(NarrativeComposition.HookBeginRule(tag)));
                Assert.That(NarrativeCompositionNames.ChoiceRule(tag), Is.EqualTo(NarrativeComposition.ChoiceRule(tag)));
                Assert.That(
                    tag + NarrativeCompositionNames.HookOfferRuleSuffix,
                    Is.EqualTo(NarrativeComposition.HookOfferRule(tag)));
                Assert.That(
                    NarrativeIds.Rule(NarrativeCompositionNames.DialogueRule(tag)).Value,
                    Is.EqualTo(FixtureIds.Rule(NarrativeComposition.DialogueRule(tag)).Value));
            }
        }

        [Test]
        public void EveryTypedIdentityHelperWrapsTheSameStableNameKey()
        {
            const string Name = NarrativeCompositionNames.Mara;
            Id128 key = NarrativeIds.Id(Name);

            Assert.That(key.IsDefault, Is.False, "a stable name never derives the invalid zero identity (P-004).");
            Assert.That(NarrativeIds.Scope(Name).Value, Is.EqualTo(key));
            Assert.That(NarrativeIds.Target(Name).Value, Is.EqualTo(key));
            Assert.That(NarrativeIds.Instance(Name).Value, Is.EqualTo(key));
            Assert.That(NarrativeIds.Installation(Name).Value, Is.EqualTo(key));
            Assert.That(NarrativeIds.Definition(Name).Value, Is.EqualTo(key));
            Assert.That(NarrativeIds.Capability(Name).Value, Is.EqualTo(key));
            Assert.That(NarrativeIds.Slot(Name).Value, Is.EqualTo(key));
            Assert.That(NarrativeIds.Rule(Name).Value, Is.EqualTo(key));
            Assert.That(NarrativeIds.Owner(Name).Value, Is.EqualTo(key));
            Assert.That(NarrativeIds.Stage(Name).Value, Is.EqualTo(key));
            Assert.That(NarrativeIds.Buffer(Name).Value, Is.EqualTo(key));
            Assert.That(NarrativeIds.Route(Name).Value, Is.EqualTo(key));
            Assert.That(NarrativeIds.Schema(Name).Value, Is.EqualTo(key));
            Assert.That(NarrativeIds.Key(Name).RegistrationKey, Is.EqualTo(key));
            Assert.That(NarrativeIds.Key(Name).KeyVersion, Is.EqualTo(1U));
            Assert.That(NarrativeIds.SchemaRef(Name).Id.Value, Is.EqualTo(key));
            Assert.That(NarrativeIds.SchemaRef(Name, 3U).Version, Is.EqualTo(3U));
            Assert.That(NarrativeIds.CapabilityRef(Name).Capability.Value, Is.EqualTo(key));
            Assert.That(NarrativeIds.Recipe(Name, NarrativeCompositionNames.VillagerRecipe).Id.Value, Is.EqualTo(key));

            Assert.That(
                NarrativeIds.OwnerPackage,
                Is.EqualTo(NarrativeIds.Id(NarrativeCompositionNames.OwnerPackageName)),
                "the package identity is the derived identity of the declared package name.");
            Assert.That(NarrativeIds.Issuer.IsDefault, Is.False);
            Assert.That(NarrativeIds.DomainClock.IsDefault, Is.False);
        }

        [Test]
        public void StableNameKeyDerivationRoundTripsTheIdentityIntoItsCanonicalTextForm()
        {
            const string Name = NarrativeCompositionNames.StoryWorld;

            Assert.That(StableNameKeyDerivation.IsCanonicalStableName(Name), Is.True);

            Id128 derived = StableNameKeyDerivation.Derive(Name);
            Assert.That(derived.IsDefault, Is.False);
            Assert.That(
                derived,
                Is.EqualTo(StableNameKeyDerivation.Derive(Name)),
                "the derivation is a pure function of the stable name (P-004, P-008).");
            Assert.That(derived, Is.EqualTo(FixtureIds.Id(Name)));
            Assert.That(NarrativeIds.Id(Name), Is.EqualTo(derived));

            Assert.That(derived.ToString().Length, Is.EqualTo(32));
            Assert.That(Id128Codec.TryParseHex(derived.ToString(), out Id128 parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(derived), "the canonical hex text form round-trips the identity.");
            Assert.That(parsed.High, Is.EqualTo(derived.High));
            Assert.That(parsed.Low, Is.EqualTo(derived.Low));

            Assert.That(
                NarrativeIds.Id(NarrativeCompositionNames.ChapterOne),
                Is.Not.EqualTo(NarrativeIds.Id(NarrativeCompositionNames.ChapterTwo)),
                "two different stable names never derive one identity.");
        }
    }
}

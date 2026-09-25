// GameCore.Rules.Cards tests — the market's stable names and derived identities (GC-011).
//
// The id literals below were computed independently of this package:
//
//   python3 -c "import hashlib
//   for n in ['cards.set-bonus','cards.reducer.int32-sum','cards.predicate.always','cards.card-seat-recipe','cards.match']:
//       d=hashlib.sha256(n.encode('utf-8')).digest()[:16]
//       print(n, int.from_bytes(d[0:8],'big'), int.from_bytes(d[8:16],'big'))"
//
// so a drift in `StableNameKeyDerivation`, in a vocabulary string or in a wrapper fails here rather than
// silently publishing a catalog whose identities no longer match the GC-006 card fixture (P-004).
#nullable enable
using GameCore.Contracts;
using NUnit.Framework;

namespace GameCore.Rules.Cards.Tests
{
    [TestFixture]
    public sealed class CardIdentityTests
    {
        [Test]
        public void IdMatchesTheIndependentlyComputedSha256Derivation()
        {
            Assert.That(
                CardIdentity.Id("cards.set-bonus"),
                Is.EqualTo(new Id128(7088730204099440885UL, 10964624383422039370UL)),
                "SHA-256 over UTF-8, first 16 digest bytes big-endian (05 s3).");
            Assert.That(
                CardIdentity.Id("cards.reducer.int32-sum"),
                Is.EqualTo(new Id128(17895918389576217266UL, 8075095241628404505UL)));
            Assert.That(
                CardIdentity.Id("cards.predicate.always"),
                Is.EqualTo(new Id128(15621504480724796288UL, 1165841324583542580UL)));
            Assert.That(
                CardIdentity.Id("cards.card-seat-recipe"),
                Is.EqualTo(new Id128(8918050714062773298UL, 11722017089174798826UL)));
            Assert.That(
                CardIdentity.Id("cards.match"),
                Is.EqualTo(new Id128(12165443358975438261UL, 1349616581690588928UL)));
        }

        [Test]
        public void TypedWrappersAndTheirRawFactoriesAgree()
        {
            Assert.That(
                CardIdentity.Scope("cards.match").Value,
                Is.EqualTo(new Id128(12165443358975438261UL, 1349616581690588928UL)));
            Assert.That(
                ScopeId.FromRaw(12165443358975438261UL, 1349616581690588928UL),
                Is.EqualTo(CardIdentity.Scope("cards.match")));
            Assert.That(
                CardIdentity.Target(CardVocabulary.SeatA).Value,
                Is.EqualTo(CardIdentity.Id(CardVocabulary.SeatA)));
            Assert.That(
                CardIdentity.Capability(CardVocabulary.SetBonus).Value,
                Is.EqualTo(CardIdentity.Id(CardVocabulary.SetBonus)));
            Assert.That(
                CardIdentity.Slot(CardVocabulary.SetBonus + ".slot-0").Value,
                Is.EqualTo(CardIdentity.Id(CardVocabulary.SetBonus + ".slot-0")));
            Assert.That(CardIdentity.Owner("cards.owner.table").Value, Is.EqualTo(CardIdentity.Id("cards.owner.table")));
            Assert.That(CardIdentity.Rule("cards.festival-scoring.set-bonus").Value, Is.EqualTo(CardIdentity.Id("cards.festival-scoring.set-bonus")));
            Assert.That(CardIdentity.Stage("cards.commit").Value, Is.EqualTo(CardIdentity.Id("cards.commit")));
            Assert.That(CardIdentity.PluginType("cards.plugin.table").Value, Is.EqualTo(CardIdentity.Id("cards.plugin.table")));
            Assert.That(CardIdentity.Instance("cards.instance.table").Value, Is.EqualTo(CardIdentity.Id("cards.instance.table")));
            Assert.That(
                CardIdentity.Buffer("cards.buffer.seat-hand").Value,
                Is.EqualTo(CardIdentity.Id("cards.buffer.seat-hand")));
            Assert.That(CardIdentity.Route("cards.route.submit-set").Value, Is.EqualTo(CardIdentity.Id("cards.route.submit-set")));
            Assert.That(
                CardIdentity.Definition(CardVocabulary.CardSeatRecipe).Value,
                Is.EqualTo(CardIdentity.Id(CardVocabulary.CardSeatRecipe)));
            Assert.That(
                CardIdentity.SchemaId(CardVocabulary.EffectiveSetBonusSchema).Value,
                Is.EqualTo(CardIdentity.Id(CardVocabulary.EffectiveSetBonusSchema)));
        }

        [Test]
        public void AProviderInstallationSharesItsInstanceIdentity()
        {
            Assert.That(
                CardIdentity.Provider(CardVocabulary.FestivalScoring).Value,
                Is.EqualTo(CardIdentity.Instance(CardVocabulary.FestivalScoring).Value),
                "An installation identity is the instance identity of the mounted declaration (05 s2).");
            Assert.That(
                CardIdentity.Provider(CardVocabulary.FestivalScoring),
                Is.Not.EqualTo(CardIdentity.Provider(CardVocabulary.QuietScoring)));
        }

        [Test]
        public void VersionedReferencesCarryTheirVersionAndTheFirstRevision()
        {
            SchemaRef schema = CardIdentity.SchemaRef(CardVocabulary.EffectiveSetBonusSchema);
            Assert.That(schema.Id, Is.EqualTo(CardIdentity.SchemaId(CardVocabulary.EffectiveSetBonusSchema)));
            Assert.That(schema.Version, Is.EqualTo(1U));
            Assert.That(
                CardIdentity.SchemaRef(CardVocabulary.EffectiveSetBonusSchema, 4U).Version,
                Is.EqualTo(4U));

            CapabilityRef capability = CardIdentity.CapabilityRef(CardVocabulary.SetBonus);
            Assert.That(capability.Capability, Is.EqualTo(CardIdentity.Capability(CardVocabulary.SetBonus)));
            Assert.That(capability.Version, Is.EqualTo(1U));

            ContractRef contract = CardIdentity.Contract(CardVocabulary.CardRuleLibrary, 2U);
            Assert.That(contract.ContractId, Is.EqualTo(CardIdentity.Id(CardVocabulary.CardRuleLibrary)));
            Assert.That(contract.Version, Is.EqualTo(2U));

            FactoryKey key = CardIdentity.Key(CardVocabulary.BonusReducer, 3U);
            Assert.That(key.RegistrationKey, Is.EqualTo(CardIdentity.Id(CardVocabulary.BonusReducer)));
            Assert.That(key.KeyVersion, Is.EqualTo(3U));

            DefinitionRef recipe = CardIdentity.Recipe(CardVocabulary.CardSeatRecipe, CardVocabulary.SetBonus);
            Assert.That(recipe.Id, Is.EqualTo(CardIdentity.Definition(CardVocabulary.CardSeatRecipe)));
            Assert.That(recipe.Schema, Is.EqualTo(CardIdentity.SchemaRef(CardVocabulary.SetBonus)));
            Assert.That(recipe.Revision, Is.EqualTo(DefinitionRevision.First));
        }

        [Test]
        public void VocabulariesAreByteIdenticalToTheGc006CardFixture()
        {
            Assert.That(CardVocabulary.Match, Is.EqualTo("cards.match"));
            Assert.That(CardVocabulary.TableArea, Is.EqualTo("cards.table-area"));
            Assert.That(CardVocabulary.LeagueA, Is.EqualTo("cards.league-a"));
            Assert.That(CardVocabulary.LeagueB, Is.EqualTo("cards.league-b"));
            Assert.That(CardVocabulary.Spectators, Is.EqualTo("cards.spectators"));
            Assert.That(CardVocabulary.SeatAScope, Is.EqualTo("cards.seat-a-scope"));
            Assert.That(CardVocabulary.SeatBScope, Is.EqualTo("cards.seat-b-scope"));
            Assert.That(CardVocabulary.SeatCScope, Is.EqualTo("cards.seat-c-scope"));
            Assert.That(CardVocabulary.Practice, Is.EqualTo("cards.practice"));

            Assert.That(CardVocabulary.FestivalScoring, Is.EqualTo("cards.festival-scoring"));
            Assert.That(CardVocabulary.NestedFestival, Is.EqualTo("cards.nested-festival"));
            Assert.That(CardVocabulary.QuietScoring, Is.EqualTo("cards.quiet-scoring"));
            Assert.That(CardVocabulary.CardTableRuntime, Is.EqualTo("cards.card-table-runtime"));
            Assert.That(CardVocabulary.CardRuleLibrary, Is.EqualTo("cards.card-rule-library"));

            Assert.That(CardVocabulary.CardSeatRecipe, Is.EqualTo("cards.card-seat-recipe"));
            Assert.That(CardVocabulary.MarketTableRecipe, Is.EqualTo("cards.market-table-recipe"));
            Assert.That(CardVocabulary.ScoreboardViewRecipe, Is.EqualTo("cards.scoreboard-view-recipe"));

            Assert.That(CardVocabulary.SeatA, Is.EqualTo("seat-a"));
            Assert.That(CardVocabulary.SeatB, Is.EqualTo("seat-b"));
            Assert.That(CardVocabulary.SeatC, Is.EqualTo("seat-c"));
            Assert.That(CardVocabulary.SeatD, Is.EqualTo("seat-d"));
            Assert.That(CardVocabulary.PracticeSeat, Is.EqualTo("practice-seat"));
            Assert.That(CardVocabulary.Scoreboard, Is.EqualTo("scoreboard"));
            Assert.That(CardVocabulary.TableOne, Is.EqualTo("table-1"));

            Assert.That(CardVocabulary.SetBonus, Is.EqualTo("cards.set-bonus"));
            Assert.That(CardVocabulary.DrawPolicy, Is.EqualTo("cards.draw-policy"));
            Assert.That(CardVocabulary.MarketTarget, Is.EqualTo("cards.market-target"));

            Assert.That(CardVocabulary.EffectiveSetBonusSchema, Is.EqualTo("cards.effective-set-bonus"));
            Assert.That(CardVocabulary.DrawPolicySchema, Is.EqualTo("cards.draw-policy-binding"));
            Assert.That(CardVocabulary.MarketBindingSchema, Is.EqualTo("cards.market-binding"));

            Assert.That(CardVocabulary.SetBonusSuffix, Is.EqualTo(".set-bonus"));
            Assert.That(CardVocabulary.DrawPolicySuffix, Is.EqualTo(".draw-policy"));
            Assert.That(CardVocabulary.MarketTargetRule, Is.EqualTo("cards.market-target-binding"));

            Assert.That(CardVocabulary.BonusReducer, Is.EqualTo("cards.reducer.int32-sum"));
            Assert.That(CardVocabulary.AlwaysPredicate, Is.EqualTo("cards.predicate.always"));
        }

        [Test]
        public void DocumentedBonusValuesAndStratumAreUnchanged()
        {
            Assert.That(CardVocabulary.BonusStratum, Is.EqualTo(0), "`cards.set-bonus` occupies stratum 0 (P-021).");
            Assert.That(CardVocabulary.FestivalBonus, Is.EqualTo(2));
            Assert.That(CardVocabulary.NestedFestivalBonus, Is.EqualTo(3));
            Assert.That(CardVocabulary.QuietBonus, Is.EqualTo(1));
        }

        [Test]
        public void DerivedStableIdentitiesMatchTheContractAndSlotDerivations()
        {
            Assert.That(CardVocabulary.SetBonusCapability, Is.EqualTo(CardIdentity.Capability("cards.set-bonus")));
            Assert.That(
                CardVocabulary.SetBonusSlot,
                Is.EqualTo(CardIdentity.Slot("cards.set-bonus.slot-0")),
                "A contract's first output slot is `<capability>.slot-0` (05 s2).");
            Assert.That(CardVocabulary.SetBonusContract, Is.EqualTo(CardIdentity.CapabilityRef("cards.set-bonus")));
            Assert.That(
                CardVocabulary.EffectiveSetBonusSchemaRef,
                Is.EqualTo(CardIdentity.SchemaRef("cards.effective-set-bonus")));
            Assert.That(CardVocabulary.BonusReducerKey, Is.EqualTo(CardIdentity.Key("cards.reducer.int32-sum")));
            Assert.That(CardVocabulary.AlwaysPredicateKey, Is.EqualTo(CardIdentity.Key("cards.predicate.always")));
            Assert.That(
                CardVocabulary.ScoringRule(CardVocabulary.FestivalScoring),
                Is.EqualTo(CardIdentity.Rule("cards.festival-scoring.set-bonus")));
            Assert.That(
                CardVocabulary.ScoringRule(CardVocabulary.NestedFestival),
                Is.EqualTo(CardIdentity.Rule("cards.nested-festival.set-bonus")));
            Assert.That(
                CardVocabulary.SelectorSchema(CardVocabulary.CardSeatRecipe),
                Is.EqualTo(CardIdentity.SchemaRef("cards.card-seat-recipe")));
        }
    }
}

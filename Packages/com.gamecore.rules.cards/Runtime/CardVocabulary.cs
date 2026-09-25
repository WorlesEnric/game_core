// GameCore.Rules.Cards — the card-market vocabulary of 07 s2 (GC-011).
//
// The stable names are byte-identical to `GameCore.Derivation.Fixtures.CardComposition`, which is the GC-006
// reusable card fixture: the rules package must not re-declare the market with different names, because the
// fixture's derived identities and this package's derived identities are compared as the same world.
//
// Shape (07 s2.1):
//
//   match                  [CardTableRuntime]
//   ├── table-area         table-1 : MarketTableRecipe
//   ├── league-a           [FestivalScoring]
//   │   ├── seat-a-scope   seat-a : CardSeatRecipe        (a nested festival can also mount here: +3)
//   │   ├── seat-b-scope   seat-b : CardSeatRecipe
//   │   └── practice       [CapabilityIsolation: cards.set-bonus]  practice-seat : CardSeatRecipe
//   ├── league-b           [QuietScoring]
//   │   └── seat-c-scope   seat-c : CardSeatRecipe
//   └── spectators         scoreboard : ScoreboardViewRecipe
//
// `cards.set-bonus` is `Additive` with a registered Int32 reducer: one provenance record per source, and reducing
// the effective integer bonus never assumes associativity beyond the registered reducer (P-019). The Practice
// seat is eligible and beneath the festival provider, but its isolation boundary blocks the contribution in
// either mode: a full target opt-in does not bypass a boundary (P-016). `cards.draw-policy` is `Exclusive`.
#nullable enable
using GameCore.Contracts;

namespace GameCore.Rules.Cards
{
    /// <summary>The card market's stable names and the identities derived from them (07 s2, GC-011).</summary>
    public static class CardVocabulary
    {
        // Scopes (07 s2.1)
        /// <summary>The match root scope; the card table is mounted here.</summary>
        public const string Match = "cards.match";

        /// <summary>The scope holding the market table target.</summary>
        public const string TableArea = "cards.table-area";

        /// <summary>League A's scope; the festival scoring provider is mounted here.</summary>
        public const string LeagueA = "cards.league-a";

        /// <summary>League B's scope; the quiet scoring provider is mounted here.</summary>
        public const string LeagueB = "cards.league-b";

        /// <summary>The scope holding the scoreboard view target.</summary>
        public const string Spectators = "cards.spectators";

        /// <summary>Seat A's scope.</summary>
        public const string SeatAScope = "cards.seat-a-scope";

        /// <summary>Seat B's scope.</summary>
        public const string SeatBScope = "cards.seat-b-scope";

        /// <summary>Seat C's scope.</summary>
        public const string SeatCScope = "cards.seat-c-scope";

        /// <summary>The practice scope; isolated from `cards.set-bonus`, so a festival bonus never reaches it (P-016).</summary>
        public const string Practice = "cards.practice";

        // Installations
        /// <summary>The festival scoring provider: one Additive `cards.set-bonus` rule at +2.</summary>
        public const string FestivalScoring = "cards.festival-scoring";

        /// <summary>The nested festival provider mounted under Seat A's scope at +3.</summary>
        public const string NestedFestival = "cards.nested-festival";

        /// <summary>The quiet scoring provider at +1 for League B.</summary>
        public const string QuietScoring = "cards.quiet-scoring";

        /// <summary>The table state executor: it owns the table, hand and score state (07 s2.2).</summary>
        public const string CardTableRuntime = "cards.card-table-runtime";

        /// <summary>The rule-library provider: it exports its definition lookup service (07 s2.2).</summary>
        public const string CardRuleLibrary = "cards.card-rule-library";

        // Target descriptors (reusable recipes)
        /// <summary>The seat target recipe; every seat target is declared against it.</summary>
        public const string CardSeatRecipe = "cards.card-seat-recipe";

        /// <summary>The market table target recipe; the only recipe the market binding rule selects.</summary>
        public const string MarketTableRecipe = "cards.market-table-recipe";

        /// <summary>The scoreboard view recipe; it takes neither scoring slot.</summary>
        public const string ScoreboardViewRecipe = "cards.scoreboard-view-recipe";

        // Targets
        /// <summary>The first league seat target.</summary>
        public const string SeatA = "seat-a";

        /// <summary>The second league seat target.</summary>
        public const string SeatB = "seat-b";

        /// <summary>The quiet league's seat target.</summary>
        public const string SeatC = "seat-c";

        /// <summary>A seat target created after the fixture composition was declared.</summary>
        public const string SeatD = "seat-d";

        /// <summary>The practice seat target, beneath the isolation boundary.</summary>
        public const string PracticeSeat = "practice-seat";

        /// <summary>The scoreboard view target.</summary>
        public const string Scoreboard = "scoreboard";

        /// <summary>The market table target.</summary>
        public const string TableOne = "table-1";

        // Derived capabilities
        /// <summary>The Additive scoring capability whose effective value is the seat's bonus.</summary>
        public const string SetBonus = "cards.set-bonus";

        /// <summary>The Exclusive draw-policy capability (07 s2.4).</summary>
        public const string DrawPolicy = "cards.draw-policy";

        /// <summary>The market binding capability.</summary>
        public const string MarketTarget = "cards.market-target";

        // Payload schemas carried by those slots (07 s2.2 rows)
        /// <summary>The payload schema of the `cards.set-bonus` slot.</summary>
        public const string EffectiveSetBonusSchema = "cards.effective-set-bonus";

        /// <summary>The payload schema of the `cards.draw-policy` slot.</summary>
        public const string DrawPolicySchema = "cards.draw-policy-binding";

        /// <summary>The payload schema of the `cards.market-target` slot.</summary>
        public const string MarketBindingSchema = "cards.market-binding";

        // Rules
        /// <summary>Rule-name suffix of every scoring provider's `cards.set-bonus` rule.</summary>
        public const string SetBonusSuffix = ".set-bonus";

        /// <summary>Rule-name suffix of every draw-policy provider's rule.</summary>
        public const string DrawPolicySuffix = ".draw-policy";

        /// <summary>The market binding rule; its selector is the market table recipe.</summary>
        public const string MarketTargetRule = "cards.market-target-binding";

        // Generated registrations: the registered Int32 reducer and the always-accepting predicate
        /// <summary>The registered Int32 sum reducer bound to the `cards.set-bonus` slot (P-009).</summary>
        public const string BonusReducer = "cards.reducer.int32-sum";

        /// <summary>The registered always-accepting predicate every card rule uses.</summary>
        public const string AlwaysPredicate = "cards.predicate.always";

        /// <summary>`cards.set-bonus` occupies stratum 0; nothing in this market depends on it (P-021).</summary>
        public const int BonusStratum = 0;

        /// <summary>The festival provider's bonus (+2); two festivals give +5 (07 s2.1).</summary>
        public const int FestivalBonus = 2;

        /// <summary>The nested festival provider's bonus (+3).</summary>
        public const int NestedFestivalBonus = 3;

        /// <summary>The quiet provider's bonus (+1).</summary>
        public const int QuietBonus = 1;

        /// <summary>The `cards.set-bonus` capability identity.</summary>
        public static CapabilityId SetBonusCapability { get; } = CardIdentity.Capability(SetBonus);

        /// <summary>
        /// Slot 0 of `cards.set-bonus`, derived exactly as a contract declaration derives it
        /// (`&lt;capability&gt;.slot-0`, 05 s2), which is the same derivation the fixture builder uses.
        /// </summary>
        public static SlotId SetBonusSlot { get; } = CardIdentity.Slot(SetBonus + ".slot-0");

        /// <summary>The versioned capability reference of the scoring slot.</summary>
        public static CapabilityRef SetBonusContract { get; } = CardIdentity.CapabilityRef(SetBonus);

        /// <summary>The versioned schema reference of the scoring slot's payload.</summary>
        public static SchemaRef EffectiveSetBonusSchemaRef { get; } =
            CardIdentity.SchemaRef(EffectiveSetBonusSchema);

        /// <summary>The generated key a scoring catalog binds the Int32 sum reducer under.</summary>
        public static FactoryKey BonusReducerKey { get; } = CardIdentity.Key(BonusReducer);

        /// <summary>The generated key a scoring catalog binds the always-accepting predicate under.</summary>
        public static FactoryKey AlwaysPredicateKey { get; } = CardIdentity.Key(AlwaysPredicate);

        /// <summary>The `cards.set-bonus` rule identity of one provider: `&lt;install&gt;` + <see cref="SetBonusSuffix"/>.</summary>
        public static RuleId ScoringRule(string installStableName) =>
            CardIdentity.Rule(installStableName + SetBonusSuffix);

        /// <summary>The selector schema identity of one target recipe, as a rule declaration names it.</summary>
        public static SchemaRef SelectorSchema(string recipeStableName) =>
            CardIdentity.SchemaRef(recipeStableName);
    }
}

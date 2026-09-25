// GameCore.Gameplay.Cards — the card table's stable identities and declared vocabulary (GC-011).
//
// Normative sources: docs/game-core/07-reference-compositions.md s2 (the card market), 00 P-004 (stable 128-bit
// identities with a catalog name for diagnostics), P-034 (one owner per authoritative domain) and 04 s8
// (registration is data: a precompiled factory key resolves a plugin, never reflection).
//
// Every identity here is derived from a canonical stable name with the production
// `GameCore.Contracts.StableNameKeyDerivation`, which is the same rule the content compiler uses for the
// generated card catalog and the same rule `GameCore.Rules.Cards.CardIdentity` applies to the shared card-market
// vocabulary. The market's own names (scopes, recipes, targets, capabilities, the reducer and the predicate) are
// NOT re-declared here: they are read from `CardVocabulary`, so the rules package and the gameplay package cannot
// disagree about what `cards.set-bonus` or `cards.card-seat-recipe` means.
//
// Four plugin instances share one precompiled factory key and one configuration schema, exactly as 04 s8
// requires (a catalog declaration resolves one manifest per plugin *type*): the table runtime, the rule library
// and the scoring provider are three declarations of one prebuilt plugin.
#nullable enable
using GameCore.Contracts;
using GameCore.Rules.Cards;

namespace GameCore.Gameplay.Cards
{
    /// <summary>The card table's stable identities, domains, slots, buffers, routes and declared bounds.</summary>
    public static class CardTableKeys
    {
        // ---------------------------------------------------------------- plugin declarations

        /// <summary>
        /// The precompiled plugin factory this build's catalog registers for every card plugin declaration
        /// (`cards.factory.card-table-plugin`, a `PluginFactory` registration).
        /// </summary>
        public static readonly FactoryKey PluginFactoryKey = CardIdentity.Key("cards.factory.card-table-plugin");

        /// <summary>
        /// The configuration schema every card declaration is admitted under (`cards.schema.card-config`, the one
        /// schema the generated card catalog registers). The catalog validates both the factory key and this
        /// schema before a mount is planned (P-009, P-020).
        /// </summary>
        public static readonly SchemaRef ConfigSchema = CardIdentity.SchemaRef("cards.schema.card-config");

        /// <summary>Stable plugin type of one card declaration; one declaration per plugin type (P-009).</summary>
        public static PluginTypeId PluginType(string declarationStableName) =>
            CardIdentity.PluginType(declarationStableName + ".type");

        /// <summary>Stable instance identity of one card mount; it survives a remount (P-005).</summary>
        public static PluginInstanceId Instance(string declarationStableName) =>
            CardIdentity.Instance(declarationStableName + ".instance");

        // ---------------------------------------------------------------- stages

        /// <summary>`cards.input`: it consumes the admitted command envelopes (07 s2.3).</summary>
        public static readonly StageId InputStage = CardIdentity.Stage("cards.stage.input");

        /// <summary>`cards.validate`: it reads state and builds one bounded draft (07 s2.3).</summary>
        public static readonly StageId ValidateStage = CardIdentity.Stage("cards.stage.validate");

        /// <summary>`cards.commit`: the sole writer of table, hands and score (07 s2.3).</summary>
        public static readonly StageId CommitStage = CardIdentity.Stage("cards.stage.commit");

        /// <summary>`cards.output`: it prepares the coherent committed output (07 s2.3).</summary>
        public static readonly StageId OutputStage = CardIdentity.Stage("cards.stage.output");

        // ---------------------------------------------------------------- systems

        /// <summary>Generated dispatch key of the `cards.input` system (`cards.system.input`).</summary>
        public static readonly FactoryKey InputSystem = CardIdentity.Key("cards.system.input");

        /// <summary>Generated dispatch key of the `cards.validate` system (`cards.system.validate`).</summary>
        public static readonly FactoryKey ValidateSystem = CardIdentity.Key("cards.system.validate");

        /// <summary>Generated dispatch key of the `cards.commit` system (`cards.system.commit`).</summary>
        public static readonly FactoryKey CommitSystem = CardIdentity.Key("cards.system.commit");

        /// <summary>Generated dispatch key of the `cards.output` system (`cards.system.output`).</summary>
        public static readonly FactoryKey OutputSystem = CardIdentity.Key("cards.system.output");

        /// <summary>The four card system keys in dispatch order, so a scenario can assert the registered set.</summary>
        public static readonly FactoryKey[] SystemKeys =
        {
            InputSystem,
            ValidateSystem,
            CommitSystem,
            OutputSystem,
        };

        // ---------------------------------------------------------------- owner, domains, slots

        /// <summary>
        /// The single logical owner of the card table's authoritative state. One instance of the table runtime
        /// owns several ECS entities (the table and every seat), and one owner is what makes the settlement of
        /// several entities one bounded domain decision instead of a cross-owner request (P-034, P-044).
        /// </summary>
        public static readonly OwnerId TableOwner = CardIdentity.Owner("cards.owner.table");

        /// <summary>Domain of `TableState { ActiveSeat, TurnNumber, Version }` (07 s2.2).</summary>
        public static readonly SchemaRef TableDomain = CardIdentity.SchemaRef("cards.domain.table-state");

        /// <summary>Domain of `SeatHand[]` and `SeatScore { Total }`, one per seat entity (07 s2.2).</summary>
        public static readonly SchemaRef SeatDomain = CardIdentity.SchemaRef("cards.domain.seat-state");

        /// <summary>Domain of the decoded command draft the input stage writes.</summary>
        public static readonly SchemaRef CommandDraftDomain = CardIdentity.SchemaRef("cards.domain.command-draft");

        /// <summary>Domain of the validated decision draft the validate stage builds (07 s2.2 `CardDecision`).</summary>
        public static readonly SchemaRef DecisionDraftDomain = CardIdentity.SchemaRef("cards.domain.decision-draft");

        /// <summary>Domain of the committed output rows the output stage writes (07 s2.2 `SetCommitted`).</summary>
        public static readonly SchemaRef OutputDomain = CardIdentity.SchemaRef("cards.domain.committed-output");

        public static readonly SlotId TableSlot = CardIdentity.Slot("cards.slot.table-state");
        public static readonly SlotId SeatSlot = CardIdentity.Slot("cards.slot.seat-state");
        public static readonly SlotId CommandDraftSlot = CardIdentity.Slot("cards.slot.command-draft");
        public static readonly SlotId DecisionDraftSlot = CardIdentity.Slot("cards.slot.decision-draft");
        public static readonly SlotId OutputSlot = CardIdentity.Slot("cards.slot.committed-output");

        /// <summary>
        /// Physical layout key of each declared slot. The physical storage of a card domain is this package's own
        /// component/buffer (a hand is a set of card rows, which one `int` slot row cannot hold), so the layout
        /// key names that component's ownership claim; the fields below are the physical fields it lays out.
        /// </summary>
        public static readonly FactoryKey TableLayout = CardIdentity.Key("cards.layout.table-state");

        public static readonly FactoryKey SeatLayout = CardIdentity.Key("cards.layout.seat-state");
        public static readonly FactoryKey CommandDraftLayout = CardIdentity.Key("cards.layout.command-draft");
        public static readonly FactoryKey DecisionDraftLayout = CardIdentity.Key("cards.layout.decision-draft");
        public static readonly FactoryKey OutputLayout = CardIdentity.Key("cards.layout.committed-output");

        /// <summary>
        /// Physical component schema of a slot's layout. A declared domain and the physical component that stores
        /// it carry the same schema identity, because in this package the domain *is* that component's schema:
        /// one authoritative domain, one storage shape, one version.
        /// </summary>
        public static readonly SchemaRef TableComponent = CardIdentity.SchemaRef("cards.schema.table-state");

        public static readonly SchemaRef SeatComponent = CardIdentity.SchemaRef("cards.schema.seat-state");

        public static readonly SchemaRef CommandDraftComponent = CardIdentity.SchemaRef("cards.schema.command-draft");

        public static readonly SchemaRef DecisionDraftComponent = CardIdentity.SchemaRef("cards.schema.decision-draft");

        public static readonly SchemaRef OutputComponent = CardIdentity.SchemaRef("cards.schema.committed-output");

        /// <summary>Field key of `TableState.ActiveSeat`.</summary>
        public static readonly FactoryKey ActiveSeatField = CardIdentity.Key("cards.field.table.active-seat");

        /// <summary>Field key of `TableState.TurnNumber`.</summary>
        public static readonly FactoryKey TurnNumberField = CardIdentity.Key("cards.field.table.turn-number");

        /// <summary>Field key of `TableState.TableVersion`.</summary>
        public static readonly FactoryKey TableVersionField = CardIdentity.Key("cards.field.table.table-version");

        /// <summary>Field key of `SeatState.Ordinal`.</summary>
        public static readonly FactoryKey SeatOrdinalField = CardIdentity.Key("cards.field.seat.ordinal");

        /// <summary>Field key of `SeatState.Score`.</summary>
        public static readonly FactoryKey SeatScoreField = CardIdentity.Key("cards.field.seat.score");

        /// <summary>Field key of the seat's hand rows, which the seat entity's own hand buffer stores.</summary>
        public static readonly FactoryKey HandRowsField = CardIdentity.Key("cards.field.seat.hand-rows");

        /// <summary>Field key of the decoded command draft rows.</summary>
        public static readonly FactoryKey CommandDraftField = CardIdentity.Key("cards.field.command-draft.rows");

        /// <summary>Field key of the validated decision draft rows.</summary>
        public static readonly FactoryKey DecisionDraftField = CardIdentity.Key("cards.field.decision-draft.rows");

        /// <summary>Field key of the committed output rows.</summary>
        public static readonly FactoryKey OutputField = CardIdentity.Key("cards.field.committed-output.rows");

        // ---------------------------------------------------------------- declared schedule buffer

        /// <summary>
        /// The declared step buffer between `cards.validate` and `cards.commit` (07 s2.3): the validate stage
        /// produces the bounded decision draft and the commit stage is its single consumer, so the compiled
        /// schedule carries a real producer-before-consumer edge (P-043).
        /// </summary>
        public static readonly BufferId DecisionBuffer = CardIdentity.Buffer("cards.buffer.decision");

        /// <summary>Order key of the decision buffer; consumption order is declared, never incidental (P-008).</summary>
        public static readonly FactoryKey DecisionOrderKey = CardIdentity.Key("cards.order.decision");

        // ---------------------------------------------------------------- command lanes and schemas

        /// <summary>The route an ordinary card command is admitted through (`cards.route.command`).</summary>
        public static readonly RouteId CommandRoute = CardIdentity.Route("cards.route.command");

        /// <summary>
        /// The route an explicit atomic batch envelope is admitted through (P-037: "a domain can declare an
        /// atomic batch envelope as one command"). It carries a bounded candidate set, which is how a
        /// simultaneous contest arrives as one command instead of as an unordered race.
        /// </summary>
        public static readonly RouteId BatchRoute = CardIdentity.Route("cards.route.batch");

        /// <summary>Ingress buffer of the ordinary command route.</summary>
        public static readonly BufferId CommandLane = CardIdentity.Buffer("cards.buffer.command-lane");

        /// <summary>Ingress buffer of the batch-envelope route.</summary>
        public static readonly BufferId BatchLane = CardIdentity.Buffer("cards.buffer.batch-lane");

        /// <summary>Producer key the host's ingress rows carry, so a lane's origin is declared (P-043).</summary>
        public static readonly FactoryKey HostIngressProducer = CardIdentity.Key("cards.producer.host");

        /// <summary>Order key of the ordinary command lane (P-008: admission sequence first).</summary>
        public static readonly FactoryKey CommandOrderKey = CardIdentity.Key("cards.order.command");

        /// <summary>Order key of the batch-envelope lane.</summary>
        public static readonly FactoryKey BatchOrderKey = CardIdentity.Key("cards.order.batch");

        /// <summary>Payload schema of one ordinary card command (`cards.schema.card-command`).</summary>
        public static readonly SchemaRef CommandSchema = CardIdentity.SchemaRef("cards.schema.card-command");

        /// <summary>Payload schema of one atomic batch envelope (`cards.schema.card-batch`).</summary>
        public static readonly SchemaRef BatchSchema = CardIdentity.SchemaRef("cards.schema.card-batch");

        /// <summary>Schema of the committed result event one settled command exposes (P-045).</summary>
        public static readonly SchemaRef ResultSchema = CardIdentity.SchemaRef("cards.schema.card-result");

        // ---------------------------------------------------------------- service contract

        /// <summary>The card-rule lookup contract `CardRuleLibrary` exports and the table declares once (07 s2.1).</summary>
        public static readonly ContractRef LookupContract = CardIdentity.Contract("cards.contract.definition-lookup");

        /// <summary>Generated key of the exported lookup service implementation (P-012).</summary>
        public static readonly FactoryKey LookupServiceFactory = CardIdentity.Key("cards.service.definition-lookup");

        // ---------------------------------------------------------------- recipes

        /// <summary>The seat recipe, as a target definition reference resolves it (P-015, P-024).</summary>
        public static readonly DefinitionRef SeatRecipe = RecipeOfStableName(CardVocabulary.CardSeatRecipe);

        /// <summary>The market table recipe.</summary>
        public static readonly DefinitionRef MarketTableRecipe = RecipeOfStableName(CardVocabulary.MarketTableRecipe);

        /// <summary>The scoreboard view recipe: no card rule selects it, so it stays on its base layout (P-015).</summary>
        public static readonly DefinitionRef ScoreboardViewRecipe =
            RecipeOfStableName(CardVocabulary.ScoreboardViewRecipe);

        /// <summary>One target recipe as a definition reference: `&lt;recipe&gt;.definition` at revision one.</summary>
        public static DefinitionRef RecipeOfStableName(string recipeStableName) =>
            CardIdentity.Recipe(recipeStableName + ".definition", recipeStableName);

        // ---------------------------------------------------------------- world and operation identity

        /// <summary>World definition of a card-table world.</summary>
        public static readonly WorldDefinitionId WorldDefinition =
            new WorldDefinitionId(CardIdentity.Id("cards.world-definition.card-table"));

        /// <summary>Issuer of every operation this fixture mints; a stable id, never a timing value (P-050).</summary>
        public static readonly Id128 Issuer = CardIdentity.Id("cards.issuer.card-market");

        // ---------------------------------------------------------------- declared bounds and seeded state

        /// <summary>
        /// Capacity of the table's command draft, decision draft and committed output rows. One entry per
        /// admitted command of one sealed step, so the bound is the lane's batch bound.
        /// </summary>
        public const int DraftCapacity = 8;

        /// <summary>Capacity of the table's market rows; the bounded market assignment (07 s2.2).</summary>
        public const int MarketCapacity = 8;

        /// <summary>Capacity of the table's deck rows.</summary>
        public const int DeckCapacity = 8;

        /// <summary>The table's seeded version: the first accepted command advances it to <see cref="TableVersionAfterOneCommit"/>.</summary>
        public const uint SeededTableVersion = 1U;

        /// <summary>Table version after exactly one committed settlement.</summary>
        public const uint TableVersionAfterOneCommit = 2U;

        /// <summary>The active seat of every seeded match: seat ordinal 0 (`seat-a`).</summary>
        public const uint SeededActiveSeat = 0U;

        /// <summary>Seat ordinal of `seat-a`, which is also the match's initial active seat.</summary>
        public const uint SeatAOrdinal = 0U;

        /// <summary>Seat ordinal of `seat-b`.</summary>
        public const uint SeatBOrdinal = 1U;

        /// <summary>Seat ordinal of `seat-c`, the quiet league's seat.</summary>
        public const uint SeatCOrdinal = 2U;

        /// <summary>
        /// Seat ordinal of the practice seat. It is deliberately not the ordinal a later spawned `seat-d` claims,
        /// so the two seats stay distinguishable in a deterministic ordinal order (P-008).
        /// </summary>
        public const uint PracticeOrdinal = 4U;

        /// <summary>
        /// Binding rows the two scoring mounts install in total: one `cards.set-bonus` row for each of the two
        /// League A seats (festival, +2) and one for the quiet league's seat (+1). The ineligible scoreboard and the
        /// eligible-but-isolated practice seat receive none (07 s2.1, P-015, P-016).
        /// </summary>
        public const int MountInstalledRowCount = 3;

        /// <summary>Score every seeded seat starts from (07 s2.3's example: "its score is 4").</summary>
        public const int SeededSeatScore = 4;

        /// <summary>Cards every seeded seat starts holding (07 s2.3: `{c1,c2,c3}` plus one spare).</summary>
        public const int SeededHandCount = 4;

        /// <summary>The first seeded card identity; card `n` of a seat is <c>SeatCard(ordinal, n)</c>.</summary>
        public const ulong FirstCardOrdinal = 1UL;

        /// <summary>
        /// One seeded card identity. Card ids are raw 64-bit values the table stores in its buffers (07 s2.2:
        /// "this fixture stores card IDs in table and hand buffers"), so the fixture derives them arithmetically
        /// from the seat ordinal and the card index instead of minting ad-hoc identities. The hundred-wide stride
        /// keeps one seat's cards disjoint from every other seat's.
        /// </summary>
        public static CardId SeatCard(uint seatOrdinal, int index) =>
            new CardId((FirstCardOrdinal + (ulong)index) + (100UL * (ulong)(seatOrdinal + 1U)));

        /// <summary>
        /// One card identity the market holds. Its stride is disjoint from every seat's and from the deck's, so
        /// "one authoritative assignment per card" (07 s2.2) is a structural property of the fixture rather than
        /// something the assertions have to police.
        /// </summary>
        public static CardId MarketCard(int index) => new CardId(900UL + (ulong)index);

        /// <summary>One undealt deck card identity; disjoint from every seat's cards and from the market's.</summary>
        public static CardId DeckCard(int index) => new CardId(800UL + (ulong)index);

        /// <summary>
        /// The score a committed set leaves in the addressed seat for the fixture's seeded state:
        /// <see cref="SeededSeatScore"/> plus `10 + effective bonus` (07 s2.3's example: 4 becomes 16 at +2).
        /// </summary>
        public static int ScoreAfterOneSet(int effectiveBonus) =>
            SeededSeatScore + CardSetRules.SetScoreDelta(effectiveBonus);
    }
}

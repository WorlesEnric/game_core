// GameCore.Derivation fixtures — the chapter-quest composition of 07 s3 (GC-006).
//
// Reusable narrative descriptors so GC-010 can run the narrative slice without re-declaring the chapter story.
// Structure (07 s3.1):
//
//   story-world
//   ├── chapter-one            [chapter-narrative-one]
//   │   ├── village            npc-mara : VillagerRecipe, gate-east : QuestGateRecipe, crowd-prop : DecorativeCrowdRecipe
//   │   ├── grove              encounter-oak : QuestEncounterRecipe
//   │   └── museum             [CapabilityIsolation: *]  npc-display : VillagerRecipe
//   └── chapter-two            [chapter-narrative-two]
//       └── harbor             npc-sailor : VillagerRecipe
//
// Capability strata follow the chain of 02 s4: stratum 0 binds a conversation, stratum 1 derives the choice
// surface only when that conversation binding is already final, stratum 2 derives the optional reward binding
// from the choice surface. The Museum deliberately proves that a compatible recipe and physical proximity do not
// bypass scope isolation, and Chapter Two proves sibling branches do not inherit each other's providers.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Derivation.Fixtures
{
    /// <summary>The chapter-quest fixture of 07 s3, with its documented stable names and layer strata.</summary>
    public static class NarrativeComposition
    {
        // Scopes (07 s3.1)
        public const string StoryWorld = "story-world";
        public const string ChapterOne = "chapter-one";
        public const string ChapterTwo = "chapter-two";
        public const string Village = "village";
        public const string Grove = "grove";
        public const string Museum = "museum";
        public const string Harbor = "harbor";

        // Installations
        public const string ChapterOneInstall = "chapter-narrative-one";
        public const string ChapterTwoInstall = "chapter-narrative-two";

        // Target descriptors (reusable recipes)
        public const string VillagerRecipe = "narrative.villager-recipe";
        public const string QuestGateRecipe = "narrative.quest-gate-recipe";
        public const string QuestEncounterRecipe = "narrative.quest-encounter-recipe";
        public const string DecorativeCrowdRecipe = "narrative.decorative-crowd-recipe";

        // Targets
        public const string Mara = "npc-mara";
        public const string GateEast = "gate-east";
        public const string CrowdProp = "crowd-prop";
        public const string EncounterOak = "encounter-oak";
        public const string Display = "npc-display";
        public const string Sailor = "npc-sailor";

        // Derived capabilities (07 s3.1 table)
        public const string ConversationBinding = "narrative.conversation-binding";
        public const string GateConditionBinding = "narrative.gate-condition-binding";
        public const string EncounterHookBinding = "narrative.encounter-hook-binding";
        public const string DialogueChoice = "narrative.dialogue-choice";
        public const string RewardBinding = "narrative.reward-binding";

        // Payload schemas carried by those slots (07 s3.2 assembly-bridge rows)
        public const string DialogueBindingSchema = "narrative.dialogue-binding";
        public const string GateConditionSchema = "narrative.gate-condition-binding";
        public const string EncounterHookSchema = "narrative.encounter-hook-binding";
        public const string DialogueChoiceSchema = "narrative.dialogue-choice";
        public const string RewardBindingSchema = "narrative.reward-binding";

        // Rule names of one chapter's chain. Every one is `<chapterTag><suffix>`, so both chapters share the shape
        // and a test can address the rule of a specific chapter without copying a literal.
        public const string DialogueSuffix = ".dialogue";
        public const string GateSuffix = ".gate";
        public const string HookBeginSuffix = ".hook.begin";
        public const string HookOfferSuffix = ".hook.offer";
        public const string ChoiceSuffix = ".choice";
        public const string RewardSuffix = ".reward";

        // Ordering keys of the Ordered encounter-hook slot (07 s3.1: BeginScene precedes OfferChoice)
        public const string BeginSceneKey = "narrative.hook.begin-scene";
        public const string OfferChoiceKey = "narrative.hook.offer-choice";

        // Generated registrations used by this fixture
        public const string AlwaysPredicate = "narrative.predicate.always";

        /// <summary>Declared strata of the narrative chain; stratum 0 binds, 1 chooses, 2 rewards (02 s4, P-021).</summary>
        public const int BindingStratum = 0;
        public const int ChoiceStratum = 1;
        public const int RewardStratum = 2;

        /// <summary>The world id every narrative fixture run uses; tests may override it (P-004).</summary>
        public static WorldId DefaultWorld { get; } = new WorldId(FixtureIds.Id("gamecore.world.narrative"));

        /// <summary>
        /// Builds the composition. Chapter One and Chapter Two are separate installations so the sibling-branch and
        /// reparent cases are expressible; both chapters of 07 export their capabilities.
        /// </summary>
        public static FixtureBuilder Builder(bool mountChapterTwo = true)
        {

            FixtureBuilder builder = new FixtureBuilder(DefaultWorld)
                .Scope(StoryWorld, null)
                .Scope(ChapterOne, StoryWorld)
                .Scope(Village, ChapterOne)
                .Scope(Grove, ChapterOne)
                .Scope(Museum, ChapterOne, isolateAllCapabilities: true)
                .Scope(ChapterTwo, StoryWorld)
                .Scope(Harbor, ChapterTwo);

            // Capability contracts: one slot per derived capability, with the policy 07 s3.1 declares.
            builder
                .Contract(ConversationBinding, BindingStratum, new[]
                {
                    new FixtureSlot(DialogueBindingSchema, CompositionPolicy.Replace),
                })
                .Contract(GateConditionBinding, BindingStratum, new[]
                {
                    new FixtureSlot(GateConditionSchema, CompositionPolicy.Replace),
                })
                .Contract(EncounterHookBinding, BindingStratum, new[]
                {
                    new FixtureSlot(EncounterHookSchema, CompositionPolicy.Ordered),
                })
                .Contract(DialogueChoice, ChoiceStratum, new[]
                {
                    new FixtureSlot(DialogueChoiceSchema, CompositionPolicy.Replace),
                })
                .Contract(RewardBinding, RewardStratum, new[]
                {
                    new FixtureSlot(RewardBindingSchema, CompositionPolicy.Replace),
                });

            // Targets: recipe descriptors, no capability imports and no opt-ins. In Automatic nothing needs them.
            builder
                .Target(Mara, Village, VillagerRecipe, tags: new[] { "narrative.character" })
                .Target(GateEast, Village, QuestGateRecipe, tags: new[] { "narrative.gate" })
                .Target(CrowdProp, Village, DecorativeCrowdRecipe)
                .Target(EncounterOak, Grove, QuestEncounterRecipe, tags: new[] { "narrative.encounter" })
                .Target(Display, Museum, VillagerRecipe, tags: new[] { "narrative.character" })
                .Target(Sailor, Harbor, VillagerRecipe, tags: new[] { "narrative.character" });

            builder.Install(
                ChapterOneInstall,
                ChapterOne,
                0,
                ChapterOneRules(chapterTag: "chapter-one"),
                state: InstallationState.Active);

            if (mountChapterTwo)
            {
                builder.Install(
                    ChapterTwoInstall,
                    ChapterTwo,
                    0,
                    ChapterTwoRules(chapterTag: "chapter-two"),
                    state: InstallationState.Active);
            }

            // Ordered encounter hooks: BeginScene precedes OfferChoice through declared hook edges (P-019). Each
            // chapter's hook rules carry their own key set, because a rule identity belongs to one installation.
            RegisterHookKeys(builder, "chapter-one");
            if (mountChapterTwo)
            {
                RegisterHookKeys(builder, "chapter-two");
            }

            return builder;
        }

        /// <summary>Registers the two hook ordering keys of one chapter's encounter rules (P-019).</summary>
        public static FixtureBuilder RegisterHookKeys(FixtureBuilder builder, string chapterTag)
        {
            builder.RuleKey(
                HookBeginRule(chapterTag),
                EncounterHookBinding,
                BeginSceneKey,
                before: new[] { new FixtureOrderEdge(OfferChoiceKey, true) });

            builder.RuleKey(
                HookOfferRule(chapterTag),
                EncounterHookBinding,
                OfferChoiceKey,
                after: new[] { new FixtureOrderEdge(BeginSceneKey, true) });

            return builder;
        }

        /// <summary>The stable-name rule of one chapter's dialogue binding.</summary>
        public static string DialogueRule(string chapterTag) => chapterTag + DialogueSuffix;

        /// <summary>The stable-name rule of one chapter's gate condition binding.</summary>
        public static string GateRule(string chapterTag) => chapterTag + GateSuffix;

        /// <summary>The stable-name rule of one chapter's "begin scene" encounter hook.</summary>
        public static string HookBeginRule(string chapterTag) => chapterTag + HookBeginSuffix;

        /// <summary>The stable-name rule of one chapter's "offer choice" encounter hook.</summary>
        public static string HookOfferRule(string chapterTag) => chapterTag + HookOfferSuffix;

        /// <summary>The stable-name rule of one chapter's dialogue choice surface.</summary>
        public static string ChoiceRule(string chapterTag) => chapterTag + ChoiceSuffix;

        /// <summary>The stable-name rule of one chapter's optional reward binding.</summary>
        public static string RewardRule(string chapterTag) => chapterTag + RewardSuffix;

        /// <summary>The value source this fixture registers: one always-accepting predicate, no reducers.</summary>
        public static FixtureValueSource ValueSource() =>
            new FixtureValueSource().RegisterAlwaysPredicate(AlwaysPredicate);

        /// <summary>Chapter One's rule set, with the chapter's own payload as the graph definition identity.</summary>
        public static IReadOnlyList<DerivationRule> ChapterOneRules(string chapterTag) => Rules(chapterTag);

        /// <summary>Chapter Two's rule set: same shapes, different definitions and a different graph payload.</summary>
        public static IReadOnlyList<DerivationRule> ChapterTwoRules(string chapterTag) => Rules(chapterTag);

        /// <summary>
        /// The rule chain both chapters declare. The payload is the chapter's definition identity, so two chapters
        /// contribute the same slot shape with different values and the `Replace` policy picks by precedence.
        /// </summary>
        public static IReadOnlyList<DerivationRule> Rules(string chapterTag)
        {
            FrozenPayload graph = FixturePayload.Tag(chapterTag + ".dialogue-graph");
            FrozenPayload condition = FixturePayload.Tag(chapterTag + ".gate-condition");
            FrozenPayload begin = FixturePayload.Tag(chapterTag + ".hook.begin");
            FrozenPayload offer = FixturePayload.Tag(chapterTag + ".hook.offer");
            FrozenPayload choice = FixturePayload.Tag(chapterTag + ".choice-surface");
            FrozenPayload reward = FixturePayload.Tag(chapterTag + ".reward-binding");

            return new List<DerivationRule>
            {
                FixtureBuilder.Rule(
                    DialogueRule(chapterTag),
                    ConversationBinding,
                    BindingStratum,
                    1U,
                    FixtureBuilder.Selector(VillagerRecipe),
                    FixtureIds.Key(AlwaysPredicate),
                    null,
                    PropagationReach.SelfAndDescendants,
                    true,
                    0,
                    CompositionPolicy.Replace,
                    graph),

                FixtureBuilder.Rule(
                    GateRule(chapterTag),
                    GateConditionBinding,
                    BindingStratum,
                    1U,
                    FixtureBuilder.Selector(QuestGateRecipe),
                    FixtureIds.Key(AlwaysPredicate),
                    null,
                    PropagationReach.SelfAndDescendants,
                    true,
                    0,
                    CompositionPolicy.Replace,
                    condition),

                FixtureBuilder.Rule(
                    HookBeginRule(chapterTag),
                    EncounterHookBinding,
                    BindingStratum,
                    1U,
                    FixtureBuilder.Selector(QuestEncounterRecipe),
                    FixtureIds.Key(AlwaysPredicate),
                    null,
                    PropagationReach.SelfAndDescendants,
                    true,
                    0,
                    CompositionPolicy.Ordered,
                    begin),

                FixtureBuilder.Rule(
                    HookOfferRule(chapterTag),
                    EncounterHookBinding,
                    BindingStratum,
                    1U,
                    FixtureBuilder.Selector(QuestEncounterRecipe),
                    FixtureIds.Key(AlwaysPredicate),
                    null,
                    PropagationReach.SelfAndDescendants,
                    true,
                    0,
                    CompositionPolicy.Ordered,
                    offer),

                // Stratum 1 reads the finalized stratum-0 conversation binding of the same target (P-021).
                FixtureBuilder.Rule(
                    ChoiceRule(chapterTag),
                    DialogueChoice,
                    ChoiceStratum,
                    1U,
                    FixtureBuilder.Selector(VillagerRecipe),
                    FixtureIds.Key(AlwaysPredicate),
                    FixtureBuilder.Inputs(ConversationBinding),
                    PropagationReach.SelfAndDescendants,
                    true,
                    0,
                    CompositionPolicy.Replace,
                    choice),

                // Stratum 2 reads the finalized stratum-1 choice surface (02 s4 optional reward binding).
                FixtureBuilder.Rule(
                    RewardRule(chapterTag),
                    RewardBinding,
                    RewardStratum,
                    1U,
                    FixtureBuilder.Selector(VillagerRecipe),
                    FixtureIds.Key(AlwaysPredicate),
                    FixtureBuilder.Inputs(DialogueChoice),
                    PropagationReach.SelfAndDescendants,
                    true,
                    0,
                    CompositionPolicy.Replace,
                    reward),
            };
        }

        /// <summary>
        /// The rule names of one chapter's chain, so a test can assert a specific rule's provenance without
        /// re-deriving its stable name.
        /// </summary>
        public static IReadOnlyList<string> RuleNames(string chapterTag) =>
            new List<string>
            {
                DialogueRule(chapterTag),
                GateRule(chapterTag),
                HookBeginRule(chapterTag),
                HookOfferRule(chapterTag),
                ChoiceRule(chapterTag),
                RewardRule(chapterTag),
            };

        /// <summary>The chapter payloads of one tag: the deterministic values a test can compare against.</summary>
        public static FrozenPayload GraphPayload(string chapterTag) => FixturePayload.Tag(chapterTag + ".dialogue-graph");
    }
}

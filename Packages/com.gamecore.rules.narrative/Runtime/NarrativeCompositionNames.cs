// GameCore.Rules.Narrative — the narrative composition's stable vocabulary (P-004, 05 section 3).
//
// GC-006 froze the narrative descriptors in `GameCore.Derivation.Fixtures.NarrativeComposition` and its rules
// module was delivered for reuse by GC-010 (09 GC-006: "Reusable narrative descriptors so GC-010 can run the
// narrative slice without re-declaring the chapter story"). A gameplay assembly may not reference a fixture
// assembly, so the same stable NAMES are declared here as the narrative package's own vocabulary and every
// identity is derived from them with the documented production rule (`StableNameKeyDerivation`). The names are
// plain strings; a test asserts equality with `NarrativeComposition`, so the two vocabularies cannot drift while
// the dependency direction stays `gameplay -> kernel`.
//
// Fixing the names here is what makes the identity rule verifiable: a schema, slot, stage or system identity of
// this package is `SHA-256(name)[0..16]`, never a CLR name and never a registration ordinal (P-054).
#nullable enable
using System;
using GameCore.Contracts;

namespace GameCore.Rules.Narrative
{
    /// <summary>
    /// The stable names of the chapter-quest composition: scopes, recipes, targets and the derivation fixture's
    /// capability/rule names, plus this package's own stage, slot, schema and system vocabulary.
    /// </summary>
    public static class NarrativeCompositionNames
    {
        // ------------------------------------------------------------------ scopes and targets (07 section 3.1)

        public const string StoryWorld = "story-world";
        public const string ChapterOne = "chapter-one";
        public const string ChapterTwo = "chapter-two";
        public const string Village = "village";
        public const string Grove = "grove";
        public const string Museum = "museum";
        public const string Harbor = "harbor";

        /// <summary>Installation identity of one chapter provider (07 section 3.1 `ChapterNarrative`).</summary>
        public const string ChapterOneInstall = "chapter-narrative-one";

        public const string ChapterTwoInstall = "chapter-narrative-two";

        // Reusable recipes (07 section 3.1 table): a recipe declares the contract, never a chapter import.
        public const string VillagerRecipe = "narrative.villager-recipe";
        public const string QuestGateRecipe = "narrative.quest-gate-recipe";
        public const string QuestEncounterRecipe = "narrative.quest-encounter-recipe";
        public const string DecorativeCrowdRecipe = "narrative.decorative-crowd-recipe";

        /// <summary>The world-level quest ledger's own recipe; no chapter rule selects it (07 section 3.1).</summary>
        public const string QuestLedgerRecipe = "narrative.quest-ledger-recipe";

        /// <summary>Recipe of the target that does not exist yet: one spawn publishes it fully assembled (P-024).</summary>
        public const string FutureVillagerRecipe = "narrative.future-villager-recipe";

        /// <summary>Recipe of the forward provider's rule: registered, and used by no live target.</summary>
        public const string ForwardVillagerRecipe = "narrative.forward-villager-recipe";

        // Live targets (07 section 3.1 tree).
        public const string Mara = "npc-mara";
        public const string GateEast = "gate-east";
        public const string CrowdProp = "crowd-prop";
        public const string EncounterOak = "encounter-oak";
        public const string Display = "npc-display";
        public const string Sailor = "npc-sailor";

        /// <summary>The world-level ledger target: it carries the durable fact slots (07 section 3.2).</summary>
        public const string QuestLedger = "quest-ledger";

        /// <summary>The future villager the spawn publication creates under `village` (P-024, TEST-004).</summary>
        public const string FutureVillager = "npc-newcomer";

        // ------------------------------------------------------------------ derivation vocabulary (GC-006 reuse)

        /// <summary>Generated always-accepting static predicate of every chapter rule (P-009, P-015).</summary>
        public const string AlwaysPredicateName = "narrative.predicate.always";

        /// <summary>Chapter suffixes of the derivation fixture's rule names; the rule identity is `<tag><suffix>`.</summary>
        public const string DialogueRuleSuffix = ".dialogue";
        public const string GateRuleSuffix = ".gate";
        public const string HookBeginRuleSuffix = ".hook.begin";
        public const string HookOfferRuleSuffix = ".hook.offer";
        public const string ChoiceRuleSuffix = ".choice";
        public const string RewardRuleSuffix = ".reward";

        /// <summary>Rule name of one chapter's dialogue binding, in the derivation fixture's own shape.</summary>
        public static string DialogueRule(string chapterTag) => chapterTag + DialogueRuleSuffix;

        public static string GateRule(string chapterTag) => chapterTag + GateRuleSuffix;

        public static string HookBeginRule(string chapterTag) => chapterTag + HookBeginRuleSuffix;

        public static string ChoiceRule(string chapterTag) => chapterTag + ChoiceRuleSuffix;

        // ------------------------------------------------------------------ this package's own vocabulary

        /// <summary>
        /// Capabilities this package derives. A derived binding row carries exactly one canonical int32 value
        /// (05 section 6), and the value is the chapter's binding ordinal, so the capability identity says what the
        /// number means and the chapter's provenance says where it came from (P-017).
        /// </summary>
        public const string DialogueBindingCapability = "narrative.binding.dialogue";

        public const string GateBindingCapability = "narrative.binding.gate";

        public const string EncounterBindingCapability = "narrative.binding.encounter-hook";

        /// <summary>
        /// Stratum-1 binding derived from the finalized dialogue binding, exactly like the fixture's choice rule
        /// (P-021: a rule reads only strictly lower strata).
        /// </summary>
        public const string ChoiceBindingCapability = "narrative.binding.choice";

        /// <summary>Declared strata of the narrative chain: 0 binds, 1 chooses (02 section 4, P-021).</summary>
        public const int BindingStratum = 0;

        public const int ChoiceStratum = 1;

        // Schemas of this package's own payloads and domains.
        public const string DialogueBindingSchema = "narrative.schema.dialogue-binding";
        public const string GateBindingSchema = "narrative.schema.gate-binding";
        public const string EncounterBindingSchema = "narrative.schema.encounter-hook-binding";
        public const string ChoiceBindingSchema = "narrative.schema.choice-binding";

        /// <summary>Payload schema of one admitted choice command: two big-endian int32 scalars (05 section 6).</summary>
        public const string ChoiceCommandSchema = "narrative.schema.choice-command";

        /// <summary>Payload schema of the dialogue stage's typed quest-mutation request (P-042).</summary>
        public const string QuestMutationSchema = "narrative.schema.quest-mutation";

        /// <summary>Committed-event schema of one accepted choice (P-044, P-045).</summary>
        public const string ChoiceCommittedSchema = "narrative.schema.choice-committed";

        /// <summary>Committed-event schema of one durable fact transition (P-044, P-045).</summary>
        public const string QuestFactCommittedSchema = "narrative.schema.quest-fact-committed";

        /// <summary>Committed-event schema of one gate decision change.</summary>
        public const string GateChangedSchema = "narrative.schema.gate-changed";

        // Authoritative domains and their single owners (P-034).
        public const string ConversationDomain = "narrative.domain.conversation-state";
        public const string QuestDomain = "narrative.domain.quest-fact-state";
        public const string GateDomain = "narrative.domain.gate-state";
        public const string EncounterDomain = "narrative.domain.encounter-state";

        public const string DialogueOwnerName = "narrative.owner.dialogue";
        public const string QuestOwnerName = "narrative.owner.quest";
        public const string GateOwnerName = "narrative.owner.gate";
        public const string EncounterOwnerName = "narrative.owner.encounter";

        public const string ConversationSlotName = "narrative.slot.conversation";
        public const string QuestSlotName = "narrative.slot.quest-fact";
        public const string GateSlotName = "narrative.slot.gate";
        public const string EncounterSlotName = "narrative.slot.encounter";

        // Stages and their systems, in the reference graph's order (07 section 3.2).
        public const string InputStageName = "narrative.input";
        public const string QuestStageName = "narrative.quest";
        public const string GateStageName = "narrative.gates";
        public const string EncounterStageName = "narrative.encounters";
        public const string OutputStageName = "narrative.output";

        public const string InputSystemName = "narrative.system.input";
        public const string QuestSystemName = "narrative.system.quest";
        public const string GateSystemName = "narrative.system.gates";
        public const string EncounterSystemName = "narrative.system.encounters";
        public const string OutputSystemName = "narrative.system.output";

        // Declared stage-local step buffers (P-041, P-043).
        public const string ChoiceRequestBufferName = "narrative.buffer.choice-request";
        public const string FactChangeBufferName = "narrative.buffer.fact-change";
        public const string FactObservedBufferName = "narrative.buffer.fact-observed";

        public const string ChoiceRequestOrderKeyName = "narrative.order.choice-request";
        public const string FactChangeOrderKeyName = "narrative.order.fact-change";
        public const string FactObservedOrderKeyName = "narrative.order.fact-observed";

        // Message-plane lane and route identities (P-042, P-043).
        public const string ChoiceRouteName = "narrative.route.choice";
        public const string QuestMutationRouteName = "narrative.route.quest-mutation";
        public const string ChoiceBufferName = "narrative.buffer.choice";
        public const string HostIngressProducerName = "narrative.producer.host";
        public const string ChoiceOrderKeyName = "narrative.order.choice";

        // Registered migrations (P-029, P-032).
        public const string ConversationMigrationName = "narrative.migration.conversation.v1-v2";

        /// <summary>Registered base-layout applier key of every narrative recipe (04 section 6).</summary>
        public const string RecipeApplierName = "narrative.recipe.applier";

        /// <summary>The package identity every narrative stage declaration names (P-039).</summary>
        public const string OwnerPackageName = "narrative.package";

        /// <summary>Message-plane lane carrying the dialogue stage's typed quest-mutation request (P-042).</summary>
        public const string QuestMutationBufferName = "narrative.buffer.quest-mutation";

        public const string QuestMutationOrderKeyName = "narrative.order.quest-mutation";

        // The forward provider's own words. Its rule selects a recipe no live target uses, so mounting it publishes a
        // real composition revision whose derivation changes no target (P-006, P-024).
        public const string ForwardChapterTag = "narrative-forward";
        public const string ForwardRuleSuffix = ".binding";
        public const string ForwardCapability = "narrative.binding.forward";
        public const string ForwardSchema = "narrative.schema.forward-binding";

        /// <summary>Prefix of one durable fact's owned slots.</summary>
        public const string FactSlotPrefix = "narrative.slot.fact.";

        /// <summary>Prefix of the physical fields the quest domain's layout declares per fact slot (P-033).</summary>
        public const string FactFieldPrefix = "narrative.field.fact.";

        /// <summary>
        /// Owned slot holding the bridge-permit fact's value. The protocol addresses owner state as
        /// `(TargetId, OwnerId, SlotId)`, so one declared fact is one slot: the fact's key never has to be written
        /// into a component and the ledger needs no second authoritative store (P-032, P-034).
        /// </summary>
        public const string FactBridgePermitValueSlotName = FactSlotPrefix + "bridge-permit.value";

        /// <summary>Owned slot holding the same fact's version, so a stale evaluation is observable (P-032).</summary>
        public const string FactBridgePermitVersionSlotName = FactSlotPrefix + "bridge-permit.version";

        /// <summary>Owned slot holding the harbor-permit fact's value (the sibling chapter's condition).</summary>
        public const string FactHarborPermitValueSlotName = FactSlotPrefix + "harbor-permit.value";

        /// <summary>Owned slot holding the harbor-permit fact's version.</summary>
        public const string FactHarborPermitVersionSlotName = FactSlotPrefix + "harbor-permit.version";

        public const string FactBridgePermitValueFieldName = FactFieldPrefix + "bridge-permit.value";

        public const string FactBridgePermitVersionFieldName = FactFieldPrefix + "bridge-permit.version";

        public const string FactHarborPermitValueFieldName = FactFieldPrefix + "harbor-permit.value";

        public const string FactHarborPermitVersionFieldName = FactFieldPrefix + "harbor-permit.version";

        /// <summary>The owned value slot of one fact slot tag; the two declared facts are constants above.</summary>
        public static string FactValueSlotName(string factSlotTag) => FactSlotPrefix + factSlotTag + ".value";

        /// <summary>The owned version slot of one fact slot tag.</summary>
        public static string FactVersionSlotName(string factSlotTag) => FactSlotPrefix + factSlotTag + ".version";

        /// <summary>The physical field one fact value slot owns inside the quest domain's layout.</summary>
        public static string FactValueFieldName(string factSlotTag) => FactFieldPrefix + factSlotTag + ".value";

        /// <summary>The physical field one fact version slot owns inside the quest domain's layout.</summary>
        public static string FactVersionFieldName(string factSlotTag) => FactFieldPrefix + factSlotTag + ".version";

        /// <summary>Generated layout key of one declared domain (P-032).</summary>
        public static string DomainLayoutName(string domainName) => domainName + ".layout";

        /// <summary>Generated initialization-policy key of one declared domain (P-032).</summary>
        public static string DomainInitName(string domainName) => domainName + ".init";

        /// <summary>Generated configuration-update-policy key of one declared domain (P-020, P-032).</summary>
        public static string DomainConfigChangeName(string domainName) => domainName + ".config-change";

        /// <summary>Physical field of a single-slot domain's layout (P-033).</summary>
        public static string DomainFieldName(string domainName) => domainName + ".field";

        // Generated keys of the four declared domains. Every one is a stable name derived by the production rule,
        // so a layout or policy key is never a CLR type name (P-004, P-032).
        public const string ConversationDomainLayoutName = ConversationDomain + ".layout";
        public const string ConversationDomainInitName = ConversationDomain + ".init";
        public const string ConversationDomainConfigChangeName = ConversationDomain + ".config-change";
        public const string ConversationDomainFieldName = ConversationDomain + ".field";
        public const string ConversationMigrationKeyName = ConversationDomain + ".policy";

        public const string QuestDomainLayoutName = QuestDomain + ".layout";
        public const string QuestDomainInitName = QuestDomain + ".init";
        public const string QuestDomainConfigChangeName = QuestDomain + ".config-change";
        public const string QuestDomainFieldName = QuestDomain + ".field";
        public const string QuestTransferKeyName = QuestDomain + ".transfer";

        public const string GateDomainLayoutName = GateDomain + ".layout";
        public const string GateDomainInitName = GateDomain + ".init";
        public const string GateDomainConfigChangeName = GateDomain + ".config-change";
        public const string GateDomainFieldName = GateDomain + ".field";
        public const string GateRebindKeyName = GateDomain + ".rebind";

        public const string EncounterDomainLayoutName = EncounterDomain + ".layout";
        public const string EncounterDomainInitName = EncounterDomain + ".init";
        public const string EncounterDomainConfigChangeName = EncounterDomain + ".config-change";
        public const string EncounterDomainFieldName = EncounterDomain + ".field";
    }

    /// <summary>
    /// Stable identities of the narrative composition, derived from the names above with the production
    /// derivation (P-004). The shapes mirror the derivation fixture's helpers exactly, and a test asserts that both
    /// produce the same identity for the same name.
    /// </summary>
    public static class NarrativeIds
    {
        /// <summary>The production derivation of one stable name; never a CLR name (P-004).</summary>
        public static Id128 Id(string stableName) => StableNameKeyDerivation.Derive(stableName);

        public static ScopeId Scope(string stableName) => new ScopeId(Id(stableName));

        public static TargetId Target(string stableName) => new TargetId(Id(stableName));

        public static PluginInstanceId Instance(string stableName) => new PluginInstanceId(Id(stableName));

        public static ProviderInstallationId Installation(string stableName) =>
            new ProviderInstallationId(Instance(stableName).Value);

        public static DefinitionId Definition(string stableName) => new DefinitionId(Id(stableName));

        public static CapabilityId Capability(string stableName) => new CapabilityId(Id(stableName));

        public static SlotId Slot(string stableName) => new SlotId(Id(stableName));

        public static RuleId Rule(string stableName) => new RuleId(Id(stableName));

        public static OwnerId Owner(string stableName) => new OwnerId(Id(stableName));

        public static StageId Stage(string stableName) => new StageId(Id(stableName));

        public static BufferId Buffer(string stableName) => new BufferId(Id(stableName));

        public static RouteId Route(string stableName) => new RouteId(Id(stableName));

        public static FactoryKey Key(string stableName, uint version = 1U) => new FactoryKey(Id(stableName), version);

        public static SchemaRef SchemaRef(string stableName, uint version = 1U) => new SchemaRef(Id(stableName), version);

        public static SchemaId Schema(string stableName) => new SchemaId(Id(stableName));

        public static CapabilityRef CapabilityRef(string stableName, uint version = 1U) =>
            new CapabilityRef(Capability(stableName), version);

        /// <summary>A recipe reference: definition identity, its declared schema and the first definition revision.</summary>
        public static DefinitionRef Recipe(string stableName, string schemaName, uint schemaVersion = 1U) =>
            new DefinitionRef(Definition(stableName), SchemaRef(schemaName, schemaVersion), DefinitionRevision.First);

        /// <summary>The package identity every stage of this package declares.</summary>
        public static readonly Id128 OwnerPackage = Id(NarrativeCompositionNames.OwnerPackageName);

        /// <summary>Stable issuer of every operation identity this package's fixture mints (P-050).</summary>
        public static readonly Id128 Issuer = Id("narrative.issuer");

        /// <summary>Stable identity of the fixture's one registered domain clock (GC-009 temporal driver).</summary>
        public static readonly Id128 DomainClock = Id("narrative.clock.domain");
    }
}

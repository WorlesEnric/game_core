// GameCore.Gameplay.Narrative — the narrative slice's stable identities (P-004, 05 section 3).
//
// Every identity below is derived from a stable NAME with the production `StableNameKeyDerivation`, exactly like a
// generated registration table (04 section 8: registration is data, never reflection). The names themselves belong
// to the Unity-free rules assembly (`GameCore.Rules.Narrative.NarrativeCompositionNames`), so the package that
// declares a stage and the package that validates a choice cannot disagree about what a name means, and the identity
// literal of a stage, system or buffer is reproducible without reading this file.
//
// The vocabulary is deliberately small: five authoritative domains, their single owners, their owned slots, the six
// stages of the reference graph (07 section 3.2), the declared buffers connecting them, the message plane's route and
// lanes, one registered migration and one base-layout applier. Nothing here is an actor, vitality, physics or
// animation concept (P-001, TEST-021).
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Rules.Narrative;

namespace GameCore.Gameplay.Narrative
{
    /// <summary>Stable identities of the narrative gameplay slice.</summary>
    public static class NarrativeKeys
    {
        // ---------------------------------------------------------------- composition identities

        /// <summary>The world root scope of the chapter-quest composition (07 section 3.1).</summary>
        public static readonly ScopeId RootScope = NarrativeIds.Scope(NarrativeCompositionNames.StoryWorld);

        public static readonly ScopeId ChapterOneScope = NarrativeIds.Scope(NarrativeCompositionNames.ChapterOne);

        public static readonly ScopeId ChapterTwoScope = NarrativeIds.Scope(NarrativeCompositionNames.ChapterTwo);

        public static readonly ScopeId VillageScope = NarrativeIds.Scope(NarrativeCompositionNames.Village);

        public static readonly ScopeId GroveScope = NarrativeIds.Scope(NarrativeCompositionNames.Grove);

        /// <summary>The isolated branch: a compatible recipe is still unreachable from a provider above it (P-016).</summary>
        public static readonly ScopeId MuseumScope = NarrativeIds.Scope(NarrativeCompositionNames.Museum);

        public static readonly ScopeId HarborScope = NarrativeIds.Scope(NarrativeCompositionNames.Harbor);

        /// <summary>Installations of the two chapter providers (07 section 3.1 `ChapterNarrative`).</summary>
        public static readonly PluginInstanceId ChapterOneInstall =
            NarrativeIds.Instance(NarrativeCompositionNames.ChapterOneInstall);

        public static readonly PluginInstanceId ChapterTwoInstall =
            NarrativeIds.Instance(NarrativeCompositionNames.ChapterTwoInstall);

        /// <summary>Installation of the forward provider: its rule selects a recipe no live target uses.</summary>
        public static readonly PluginInstanceId ForwardInstall = NarrativeIds.Instance("narrative.forward-provider");

        // ---------------------------------------------------------------- live targets

        public static readonly TargetId Mara = NarrativeIds.Target(NarrativeCompositionNames.Mara);

        public static readonly TargetId GateEast = NarrativeIds.Target(NarrativeCompositionNames.GateEast);

        public static readonly TargetId CrowdProp = NarrativeIds.Target(NarrativeCompositionNames.CrowdProp);

        public static readonly TargetId EncounterOak = NarrativeIds.Target(NarrativeCompositionNames.EncounterOak);

        public static readonly TargetId Display = NarrativeIds.Target(NarrativeCompositionNames.Display);

        public static readonly TargetId Sailor = NarrativeIds.Target(NarrativeCompositionNames.Sailor);

        /// <summary>The world-level ledger target: it carries the durable fact slots (07 section 3.2).</summary>
        public static readonly TargetId QuestLedger = NarrativeIds.Target(NarrativeCompositionNames.QuestLedger);

        /// <summary>The target that does not exist yet: one spawn publishes it fully assembled (P-024).</summary>
        public static readonly TargetId FutureVillager = NarrativeIds.Target(NarrativeCompositionNames.FutureVillager);

        // ---------------------------------------------------------------- reusable recipes

        /// <summary>A recipe reference: its definition identity, its declared recipe schema and revision 1.</summary>
        public static DefinitionRef Recipe(string recipeName) =>
            NarrativeIds.Recipe(recipeName + ".definition", recipeName);

        public static readonly DefinitionRef VillagerRecipe = Recipe(NarrativeCompositionNames.VillagerRecipe);

        public static readonly DefinitionRef QuestGateRecipe = Recipe(NarrativeCompositionNames.QuestGateRecipe);

        public static readonly DefinitionRef QuestEncounterRecipe =
            Recipe(NarrativeCompositionNames.QuestEncounterRecipe);

        public static readonly DefinitionRef DecorativeCrowdRecipe =
            Recipe(NarrativeCompositionNames.DecorativeCrowdRecipe);

        public static readonly DefinitionRef QuestLedgerRecipe = Recipe(NarrativeCompositionNames.QuestLedgerRecipe);

        public static readonly DefinitionRef ForwardVillagerRecipe =
            Recipe(NarrativeCompositionNames.ForwardVillagerRecipe);

        // ---------------------------------------------------------------- derived capabilities

        public static readonly CapabilityId DialogueBinding =
            NarrativeIds.Capability(NarrativeCompositionNames.DialogueBindingCapability);

        public static readonly CapabilityId GateBinding =
            NarrativeIds.Capability(NarrativeCompositionNames.GateBindingCapability);

        public static readonly CapabilityId EncounterBinding =
            NarrativeIds.Capability(NarrativeCompositionNames.EncounterBindingCapability);

        public static readonly CapabilityId ChoiceBinding =
            NarrativeIds.Capability(NarrativeCompositionNames.ChoiceBindingCapability);

        public static readonly CapabilityId ForwardBinding =
            NarrativeIds.Capability(NarrativeCompositionNames.ForwardCapability);

        // ---------------------------------------------------------------- authoritative domains and owners

        public static readonly SchemaRef ConversationDomain =
            NarrativeIds.SchemaRef(NarrativeCompositionNames.ConversationDomain, 2U);

        public static readonly SchemaRef QuestDomain =
            NarrativeIds.SchemaRef(NarrativeCompositionNames.QuestDomain, 1U);

        public static readonly SchemaRef GateDomain = NarrativeIds.SchemaRef(NarrativeCompositionNames.GateDomain, 1U);

        public static readonly SchemaRef EncounterDomain =
            NarrativeIds.SchemaRef(NarrativeCompositionNames.EncounterDomain, 1U);

        public static readonly SchemaRef TrailDomain = NarrativeIds.SchemaRef("narrative.domain.step-trail", 1U);

        /// <summary>
        /// Owner of the host ingress lane. It is a routing identity only: the route the host admits a command to owns
        /// the bounded lane that command waits in, while the state owner that validates the choice is the dialogue
        /// owner, which receives the forwarded row with the same causal request (P-042).
        /// </summary>
        public static readonly OwnerId IngressOwner = NarrativeIds.Owner("narrative.owner.ingress");

        public static readonly OwnerId DialogueOwner = NarrativeIds.Owner(NarrativeCompositionNames.DialogueOwnerName);

        public static readonly OwnerId QuestOwner = NarrativeIds.Owner(NarrativeCompositionNames.QuestOwnerName);

        public static readonly OwnerId GateOwner = NarrativeIds.Owner(NarrativeCompositionNames.GateOwnerName);

        public static readonly OwnerId EncounterOwner =
            NarrativeIds.Owner(NarrativeCompositionNames.EncounterOwnerName);

        public static readonly OwnerId TrailOwner = NarrativeIds.Owner("narrative.owner.trail");

        // ---------------------------------------------------------------- owned state slots

        public static readonly SlotId ConversationNodeSlot = NarrativeIds.Slot("narrative.slot.conversation.node");

        public static readonly SlotId ConversationStatusSlot =
            NarrativeIds.Slot("narrative.slot.conversation.status");

        public static readonly SlotId GateDecisionSlot = NarrativeIds.Slot("narrative.slot.gate.decision");

        public static readonly SlotId GateEvaluatedVersionSlot =
            NarrativeIds.Slot("narrative.slot.gate.evaluated-version");

        public static readonly SlotId EncounterStatusSlot = NarrativeIds.Slot("narrative.slot.encounter.status");

        public static readonly SlotId TrailStepsSlot = NarrativeIds.Slot("narrative.slot.trail.steps");

        public static readonly SlotId TrailProjectedSlot = NarrativeIds.Slot("narrative.slot.trail.projected");

        public static readonly SlotId TrailFactsSlot = NarrativeIds.Slot("narrative.slot.trail.facts");

        public static readonly SlotId TrailHooksSlot = NarrativeIds.Slot("narrative.slot.trail.hooks");

        public static readonly SlotId TrailGateDecisionsSlot =
            NarrativeIds.Slot("narrative.slot.trail.gate-decisions");

        /// <summary>The owned value slot of one fact slot tag (P-032: one declared fact is one slot).</summary>
        public static SlotId FactValueSlot(string factSlotTag) =>
            NarrativeIds.Slot(NarrativeCompositionNames.FactValueSlotName(factSlotTag));

        /// <summary>The owned version slot of one fact slot tag.</summary>
        public static SlotId FactVersionSlot(string factSlotTag) =>
            NarrativeIds.Slot(NarrativeCompositionNames.FactVersionSlotName(factSlotTag));

        /// <summary>The value slot of the bridge-permit fact, the one Chapter One's condition reads.</summary>
        public static readonly SlotId BridgePermitValueSlot = FactValueSlot(NarrativeFacts.BridgePermitSlotTag);

        public static readonly SlotId BridgePermitVersionSlot = FactVersionSlot(NarrativeFacts.BridgePermitSlotTag);

        public static readonly SlotId HarborPermitValueSlot = FactValueSlot(NarrativeFacts.HarborPermitSlotTag);

        public static readonly SlotId HarborPermitVersionSlot = FactVersionSlot(NarrativeFacts.HarborPermitSlotTag);

        // ---------------------------------------------------------------- generated layout/policy keys

        public static FactoryKey Key(string stableName) => NarrativeIds.Key(stableName);

        public static readonly FactoryKey ConversationLayout =
            Key(NarrativeCompositionNames.ConversationDomainLayoutName);

        public static readonly FactoryKey ConversationInit = Key(NarrativeCompositionNames.ConversationDomainInitName);

        public static readonly FactoryKey ConversationConfigChange =
            Key(NarrativeCompositionNames.ConversationDomainConfigChangeName);

        public static readonly FactoryKey ConversationNodeField = Key("narrative.field.conversation.node");

        public static readonly FactoryKey ConversationStatusField = Key("narrative.field.conversation.status");

        public static readonly FactoryKey QuestLayout = Key(NarrativeCompositionNames.QuestDomainLayoutName);

        public static readonly FactoryKey QuestInit = Key(NarrativeCompositionNames.QuestDomainInitName);

        public static readonly FactoryKey QuestConfigChange = Key(NarrativeCompositionNames.QuestDomainConfigChangeName);

        public static readonly FactoryKey QuestTransfer = Key(NarrativeCompositionNames.QuestTransferKeyName);

        public static readonly FactoryKey GateLayout = Key(NarrativeCompositionNames.GateDomainLayoutName);

        public static readonly FactoryKey GateInit = Key(NarrativeCompositionNames.GateDomainInitName);

        public static readonly FactoryKey GateConfigChange = Key(NarrativeCompositionNames.GateDomainConfigChangeName);

        public static readonly FactoryKey GateDecisionField = Key("narrative.field.gate.decision");

        public static readonly FactoryKey GateEvaluatedVersionField = Key("narrative.field.gate.evaluated-version");

        public static readonly FactoryKey EncounterLayout = Key(NarrativeCompositionNames.EncounterDomainLayoutName);

        public static readonly FactoryKey EncounterInit = Key(NarrativeCompositionNames.EncounterDomainInitName);

        public static readonly FactoryKey EncounterConfigChange =
            Key(NarrativeCompositionNames.EncounterDomainConfigChangeName);

        public static readonly FactoryKey EncounterStatusField = Key("narrative.field.encounter.status");

        public static readonly FactoryKey TrailLayout = Key("narrative.domain.step-trail.layout");

        public static readonly FactoryKey TrailInit = Key("narrative.domain.step-trail.init");

        public static readonly FactoryKey TrailConfigChange = Key("narrative.domain.step-trail.config-change");

        public static readonly FactoryKey TrailStepsField = Key("narrative.field.trail.steps");

        public static readonly FactoryKey TrailProjectedField = Key("narrative.field.trail.projected");

        public static readonly FactoryKey TrailFactsField = Key("narrative.field.trail.facts");

        public static readonly FactoryKey TrailHooksField = Key("narrative.field.trail.hooks");

        public static readonly FactoryKey TrailGateDecisionsField = Key("narrative.field.trail.gate-decisions");

        /// <summary>Registered migration of the conversation node slot (P-029, P-032).</summary>
        public static readonly FactoryKey ConversationNodeMigration =
            Key("narrative.migration.conversation.node.v1-v2");

        /// <summary>Registered migration of the conversation status slot.</summary>
        public static readonly FactoryKey ConversationStatusMigration =
            Key("narrative.migration.conversation.status.v1-v2");

        /// <summary>Generated base-layout applier key of every narrative recipe (04 section 6).</summary>
        public static readonly FactoryKey RecipeApplier = Key(NarrativeCompositionNames.RecipeApplierName);

        // ---------------------------------------------------------------- stages, systems, buffers and routes

        public static readonly StageId InputStage = NarrativeIds.Stage(NarrativeCompositionNames.InputStageName);

        public static readonly StageId DialogueStage = NarrativeIds.Stage("narrative.dialogue");

        public static readonly StageId QuestStage = NarrativeIds.Stage(NarrativeCompositionNames.QuestStageName);

        public static readonly StageId GateStage = NarrativeIds.Stage(NarrativeCompositionNames.GateStageName);

        public static readonly StageId EncounterStage = NarrativeIds.Stage(NarrativeCompositionNames.EncounterStageName);

        public static readonly StageId OutputStage = NarrativeIds.Stage(NarrativeCompositionNames.OutputStageName);

        public static readonly FactoryKey InputSystem = NarrativeIds.Key(NarrativeCompositionNames.InputSystemName);

        public static readonly FactoryKey DialogueSystem = NarrativeIds.Key("narrative.system.dialogue");

        public static readonly FactoryKey QuestSystem = NarrativeIds.Key(NarrativeCompositionNames.QuestSystemName);

        public static readonly FactoryKey GateSystem = NarrativeIds.Key(NarrativeCompositionNames.GateSystemName);

        public static readonly FactoryKey EncounterSystem =
            NarrativeIds.Key(NarrativeCompositionNames.EncounterSystemName);

        public static readonly FactoryKey OutputSystem = NarrativeIds.Key(NarrativeCompositionNames.OutputSystemName);

        public static readonly BufferId ChoiceBuffer = NarrativeIds.Buffer(NarrativeCompositionNames.ChoiceBufferName);

        public static readonly BufferId ChoiceRequestBuffer =
            NarrativeIds.Buffer(NarrativeCompositionNames.ChoiceRequestBufferName);

        public static readonly BufferId FactChangeBuffer =
            NarrativeIds.Buffer(NarrativeCompositionNames.FactChangeBufferName);

        public static readonly BufferId FactObservedBuffer =
            NarrativeIds.Buffer(NarrativeCompositionNames.FactObservedBufferName);

        public static readonly BufferId EncounterObservedBuffer =
            NarrativeIds.Buffer("narrative.buffer.encounter-observed");

        public static readonly RouteId ChoiceRoute = NarrativeIds.Route(NarrativeCompositionNames.ChoiceRouteName);

        public static readonly FactoryKey HostIngressProducer =
            NarrativeIds.Key(NarrativeCompositionNames.HostIngressProducerName);

        public static readonly FactoryKey ChoiceOrderKey = NarrativeIds.Key(NarrativeCompositionNames.ChoiceOrderKeyName);

        public static readonly FactoryKey ChoiceRequestOrderKey =
            NarrativeIds.Key(NarrativeCompositionNames.ChoiceRequestOrderKeyName);

        public static readonly FactoryKey FactChangeOrderKey =
            NarrativeIds.Key(NarrativeCompositionNames.FactChangeOrderKeyName);

        public static readonly FactoryKey FactObservedOrderKey =
            NarrativeIds.Key(NarrativeCompositionNames.FactObservedOrderKeyName);

        public static readonly FactoryKey EncounterObservedOrderKey =
            NarrativeIds.Key("narrative.order.encounter-observed");

        // ---------------------------------------------------------------- payload and event schemas

        public static readonly SchemaRef ChoiceCommandSchema =
            NarrativeIds.SchemaRef(NarrativeCompositionNames.ChoiceCommandSchema, 1U);

        public static readonly SchemaRef QuestMutationSchema =
            NarrativeIds.SchemaRef(NarrativeCompositionNames.QuestMutationSchema, 1U);

        public static readonly SchemaRef FactObservedSchema =
            NarrativeIds.SchemaRef("narrative.schema.fact-observed", 1U);

        public static readonly SchemaRef ChoiceCommittedSchema =
            NarrativeIds.SchemaRef(NarrativeCompositionNames.ChoiceCommittedSchema, 1U);

        public static readonly SchemaRef GateChangedSchema =
            NarrativeIds.SchemaRef(NarrativeCompositionNames.GateChangedSchema, 1U);

        // ---------------------------------------------------------------- operation identities

        /// <summary>Stable issuer of every operation identity this slice's fixture mints (P-050).</summary>
        public static readonly Id128 Issuer = NarrativeIds.Issuer;

        /// <summary>Stable clock identity of the one registered domain clock (GC-009 temporal driver).</summary>
        public static readonly Id128 DomainClock = NarrativeIds.DomainClock;

        /// <summary>Owning package of every stage this slice declares (P-039).</summary>
        public static readonly Id128 OwnerPackage = NarrativeIds.OwnerPackage;

        /// <summary>The world-definition identity of a chapter-quest world (P-004).</summary>
        public static readonly WorldDefinitionId WorldDefinition =
            new WorldDefinitionId(NarrativeIds.Id("narrative.world-definition"));

        /// <summary>Stable plugin-type identity of one provider, from a small ordinal.</summary>
        public static PluginTypeId PluginTypeId(ulong ordinal) =>
            new PluginTypeId(new Id128(0x4E41525241544956UL, 0x0000000000000100UL + ordinal));

        /// <summary>One installed instance identity, from a small ordinal.</summary>
        public static PluginInstanceId Instance(ulong ordinal) =>
            new PluginInstanceId(new Id128(0x4E41525241544E53UL, 0x0000000000000200UL + ordinal));

        /// <summary>
        /// True when a recipe is one the reference chapters select. The test is the recipe identity itself, never a
        /// name prefix or a nearby object: an ineligible target stays on its base recipe (P-015).
        /// </summary>
        public static bool IsChapterEligibleRecipe(DefinitionRef recipe)
            => recipe.Equals(VillagerRecipe)
                || recipe.Equals(QuestGateRecipe)
                || recipe.Equals(QuestEncounterRecipe);

        /// <summary>One operation identity of this world's issuer (P-050).</summary>
        public static OperationId Operation(WorldId world, ulong sequence) => new OperationId(world, Issuer, sequence);

        /// <summary>The migration keys this slice registers, in canonical order (P-032).</summary>
        public static IReadOnlyList<FactoryKey> MigrationKeys { get; } = new[]
        {
            ConversationNodeMigration,
            ConversationStatusMigration,
        };
    }
}

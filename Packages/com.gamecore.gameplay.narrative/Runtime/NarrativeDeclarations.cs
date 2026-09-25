// GameCore.Gameplay.Narrative — the chapter provider's declarations (P-009, P-013, P-015, P-039, P-043).
//
// One chapter is one plugin instance mounted at a chapter scope. It declares:
//
//   * four capability contracts (`Replace`, one output slot each) and one derivation rule per contract, whose
//     selector is the REUSABLE recipe — `narrative.villager-recipe`, `narrative.quest-gate-recipe`,
//     `narrative.quest-encounter-recipe` — and never a concrete target. That is what makes automatic propagation
//     possible with no per-instance import: the recipe is the compatibility declaration, and the chapter contributes
//     the content (P-013, P-015);
//   * the stratum-1 choice rule, whose declared input is the same target's stratum-0 dialogue binding, so the
//     choice surface appears only after a conversation binding is final on that target (P-021);
//   * six stages in the reference graph's order (07 section 3.2), each with one system and its own access set;
//   * four stage-local step buffers with one consuming owner and exactly one producer each (P-043);
//   * five authoritative domains, each with exactly one owner and its own owned slots (P-032, P-034).
//
// Everything a chapter contributes to a target is one canonical int32: the chapter's binding ordinal. A binding row
// holds exactly one int32, so a chapter's content identity and its row value are the same number, and the chapter's
// provenance identifies which installation wrote it (P-017, GC-008's derived-variant transfer).
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Rules.Narrative;

namespace GameCore.Gameplay.Narrative
{
    /// <summary>Generated-style manifest declarations of a chapter provider and of the slice's static vocabulary.</summary>
    public static class NarrativeDeclarations
    {
        /// <summary>Version string a manifest of this package declares.</summary>
        public const string PackageVersion = "0.1.0";

        /// <summary>Declared row capacity of one bounded lane.</summary>
        private const int LaneCapacity = 4;

        // ---------------------------------------------------------------- capability contracts

        /// <summary>One capability contract with exactly one output slot and its declared policy (P-017, P-019).</summary>
        public static CapabilityContract Contract(string capabilityName, string schemaName, int stratum, string policy)
        {
            SlotId slot = NarrativeIds.Slot(capabilityName + ".slot");

            return new CapabilityContract(
                NarrativeIds.CapabilityRef(capabilityName, 1U),
                stratum,
                new List<OutputSlotSchema> { new OutputSlotSchema(slot, NarrativeIds.SchemaRef(schemaName, 1U)) },
                new List<SlotCompositionPolicy>
                {
                    new SlotCompositionPolicy(slot, PolicyOf(policy), default(FactoryKey)),
                },
                null);
        }

        /// <summary>
        /// One derivation rule from a reusable recipe selector to one output capability. The payload is the chapter's
        /// binding ordinal in the canonical big-endian int32 encoding of 05 section 6, so the derived value transfers
        /// into a binding row without a second interpretation, and only one contribution supports the slot.
        /// </summary>
        public static DerivationRule Rule(
            string chapterTag,
            string ruleSuffix,
            string outputCapability,
            string schemaName,
            int stratum,
            string selectorRecipeName,
            string inputCapabilityName,
            string policy,
            int value)
        {
            IReadOnlyList<CapabilityRef>? inputs = string.IsNullOrEmpty(inputCapabilityName)
                ? null
                : new List<CapabilityRef> { NarrativeIds.CapabilityRef(inputCapabilityName, 1U) };

            return new DerivationRule(
                NarrativeIds.Rule(chapterTag + ruleSuffix),
                NarrativeIds.CapabilityRef(outputCapability, 1U),
                stratum,
                1U,
                new List<SchemaRef> { NarrativeIds.SchemaRef(selectorRecipeName, 1U) },
                NarrativeIds.Key(NarrativeCompositionNames.AlwaysPredicateName),
                inputs,
                PropagationReach.SelfAndDescendants,
                true,
                0,
                PolicyOf(policy),
                NarrativePayloadCodecFrozen(value));
        }

        /// <summary>
        /// The four bindings a chapter contributes, built from the rules assembly's plan so this package cannot
        /// declare a rule the content model does not name (the plan is the single declaration source).
        /// </summary>
        public static IReadOnlyList<DerivationRule> Rules(string chapterTag, int bindingOrdinal)
        {
            var rules = new List<DerivationRule>(NarrativeDerivationPlan.Bindings.Count);
            for (int i = 0; i < NarrativeDerivationPlan.Bindings.Count; i++)
            {
                NarrativeBindingPlan plan = NarrativeDerivationPlan.Bindings[i];
                rules.Add(Rule(
                    chapterTag,
                    plan.RuleSuffix,
                    plan.CapabilityName,
                    plan.SchemaName,
                    plan.Stratum,
                    plan.SelectorRecipeName,
                    plan.InputCapabilityName,
                    plan.DeclaredPolicy,
                    bindingOrdinal));
            }

            return rules;
        }

        /// <summary>Every capability contract a chapter declares, in the plan's canonical order.</summary>
        public static IReadOnlyList<CapabilityContract> Contracts()
        {
            var contracts = new List<CapabilityContract>(NarrativeDerivationPlan.Bindings.Count);
            for (int i = 0; i < NarrativeDerivationPlan.Bindings.Count; i++)
            {
                NarrativeBindingPlan plan = NarrativeDerivationPlan.Bindings[i];
                contracts.Add(Contract(plan.CapabilityName, plan.SchemaName, plan.Stratum, plan.DeclaredPolicy));
            }

            return contracts;
        }

        // ---------------------------------------------------------------- manifests

        /// <summary>
        /// Chapter One's manifest: it binds the village and grove recipes in its own scope subtree and declares the
        /// whole execution, ownership and state surface of the slice.
        /// </summary>
        public static PluginManifest ChapterProvider(
            PluginTypeId pluginType,
            FactoryKey factoryKey,
            SchemaRef configSchema)
        {
            return Manifest(pluginType, factoryKey, configSchema, NarrativeChapters.ChapterOneTag, true);
        }

        /// <summary>
        /// Chapter Two's manifest: the same declaration shape with its own rule identities and its own binding
        /// ordinal, so a sibling branch receives only its own chapter's content (07 section 3.1).
        /// </summary>
        public static PluginManifest ChapterTwoProvider(
            PluginTypeId pluginType,
            FactoryKey factoryKey,
            SchemaRef configSchema)
        {
            return Manifest(pluginType, factoryKey, configSchema, NarrativeChapters.ChapterTwoTag, false);
        }

        /// <summary>
        /// The forward provider: it declares one rule for a recipe no live target uses, so mounting it publishes a
        /// real composition revision whose derivation changes no target. The world's assembly for that publication is
        /// a spawn, which is what keeps the two counters on one series (P-006, P-024).
        /// </summary>
        public static PluginManifest ForwardProvider(
            PluginTypeId pluginType,
            FactoryKey factoryKey,
            SchemaRef configSchema)
        {
            return new PluginManifest(
                pluginType,
                PackageVersion,
                ContentHash.Empty,
                new SupportedProtocolRange(1, 0, 0),
                null,
                configSchema,
                factoryKey,
                null,
                null,
                new List<CapabilityContract>
                {
                    Contract(
                        NarrativeCompositionNames.ForwardCapability,
                        NarrativeCompositionNames.ForwardSchema,
                        0,
                        NarrativeDerivationPlan.ReplacePolicy),
                },
                new List<DerivationRule>
                {
                    Rule(
                        NarrativeCompositionNames.ForwardChapterTag,
                        NarrativeCompositionNames.ForwardRuleSuffix,
                        NarrativeCompositionNames.ForwardCapability,
                        NarrativeCompositionNames.ForwardSchema,
                        0,
                        NarrativeCompositionNames.ForwardVillagerRecipe,
                        string.Empty,
                        NarrativeDerivationPlan.ReplacePolicy,
                        1),
                },
                null,
                null,
                null,
                null,
                null);
        }

        /// <summary>
        /// One chapter's manifest. Only the first provider declares the slice's execution and ownership surface
        /// (stages, buffers, state slots): a repeated buffer contract is rejected by the schedule compiler, so a
        /// second provider that re-declared it would make the whole catalog revision uncompilable (P-039, P-043).
        /// A second chapter therefore declares exactly what it contributes — its capability contracts and its rules.
        /// </summary>
        private static PluginManifest Manifest(
            PluginTypeId pluginType,
            FactoryKey factoryKey,
            SchemaRef configSchema,
            string chapterTag,
            bool declareExecutionSurface)
        {
            int ordinal = NarrativeChapters.Get(chapterTag).BindingOrdinal;

            return new PluginManifest(
                pluginType,
                PackageVersion,
                ContentHash.Empty,
                new SupportedProtocolRange(1, 0, 0),
                null,
                configSchema,
                factoryKey,
                null,
                null,
                Contracts(),
                Rules(chapterTag, ordinal),
                null,
                declareExecutionSurface ? Slots() : null,
                declareExecutionSurface ? Stages() : null,
                declareExecutionSurface ? Buffers() : null,
                null);
        }

        // ---------------------------------------------------------------- stages

        /// <summary>
        /// The six stages of the reference graph (07 section 3.2), with the declared order the graph fixes:
        /// `input → dialogue`, `dialogue → quest`, `quest → gates`, `quest → encounters`, and `gates`/`encounters`/
        /// `dialogue`/`input` before `output`. One system per stage; one owner per domain (P-034, P-039, P-040).
        /// </summary>
        public static IReadOnlyList<StageSpec> Stages()
        {
            var input = new StageSpec(
                NarrativeKeys.InputStage,
                1U,
                NarrativeKeys.OwnerPackage,
                HostAffinity.ManagedMain,
                null,
                null,
                Access(new AccessDeclaration(NarrativeKeys.TrailDomain, AccessMode.ReadWrite, default(Id128))),
                null,
                null,
                null,
                null,
                new List<SystemSpec> { Entry(NarrativeKeys.InputSystem, SystemMultiplicity.World, NarrativeKeys.TrailDomain, AccessMode.ReadWrite) },
                null);

            var dialogue = new StageSpec(
                NarrativeKeys.DialogueStage,
                1U,
                NarrativeKeys.OwnerPackage,
                HostAffinity.ManagedMain,
                null,
                null,
                Access(new AccessDeclaration(NarrativeKeys.ConversationDomain, AccessMode.ReadWrite, default(Id128))),
                null,
                new List<StageId> { NarrativeKeys.InputStage },
                null,
                null,
                new List<SystemSpec> { Entry(NarrativeKeys.DialogueSystem, SystemMultiplicity.World, NarrativeKeys.ConversationDomain, AccessMode.ReadWrite) },
                null);

            var quest = new StageSpec(
                NarrativeKeys.QuestStage,
                1U,
                NarrativeKeys.OwnerPackage,
                HostAffinity.ManagedMain,
                null,
                null,
                Access(new AccessDeclaration(NarrativeKeys.QuestDomain, AccessMode.ReadWrite, default(Id128))),
                null,
                new List<StageId> { NarrativeKeys.DialogueStage },
                null,
                null,
                new List<SystemSpec> { Entry(NarrativeKeys.QuestSystem, SystemMultiplicity.World, NarrativeKeys.QuestDomain, AccessMode.ReadWrite) },
                null);

            var gates = new StageSpec(
                NarrativeKeys.GateStage,
                1U,
                NarrativeKeys.OwnerPackage,
                HostAffinity.ManagedMain,
                null,
                null,
                Access(new AccessDeclaration(NarrativeKeys.GateDomain, AccessMode.ReadWrite, default(Id128))),
                null,
                new List<StageId> { NarrativeKeys.QuestStage },
                null,
                null,
                new List<SystemSpec>
                {
                    Entry(NarrativeKeys.GateSystem, SystemMultiplicity.World, NarrativeKeys.GateDomain, AccessMode.ReadWrite),
                },
                null);

            var encounters = new StageSpec(
                NarrativeKeys.EncounterStage,
                1U,
                NarrativeKeys.OwnerPackage,
                HostAffinity.ManagedMain,
                null,
                null,
                Access(new AccessDeclaration(NarrativeKeys.EncounterDomain, AccessMode.ReadWrite, default(Id128))),
                null,
                new List<StageId> { NarrativeKeys.QuestStage },
                null,
                null,
                new List<SystemSpec>
                {
                    Entry(NarrativeKeys.EncounterSystem, SystemMultiplicity.World, NarrativeKeys.EncounterDomain, AccessMode.ReadWrite),
                },
                null);

            var output = new StageSpec(
                NarrativeKeys.OutputStage,
                1U,
                NarrativeKeys.OwnerPackage,
                HostAffinity.ManagedMain,
                null,
                null,
                Access(new AccessDeclaration(NarrativeKeys.TrailDomain, AccessMode.ReadWrite, default(Id128))),
                null,
                new List<StageId>
                {
                    NarrativeKeys.InputStage,
                    NarrativeKeys.DialogueStage,
                    NarrativeKeys.GateStage,
                    NarrativeKeys.EncounterStage,
                },
                null,
                null,
                new List<SystemSpec> { Entry(NarrativeKeys.OutputSystem, SystemMultiplicity.World, NarrativeKeys.TrailDomain, AccessMode.ReadWrite) },
                null);

            return new List<StageSpec> { input, dialogue, quest, gates, encounters, output };
        }

        /// <summary>One system entry with its single owned domain and its declared access mode (P-034, P-039).</summary>
        public static SystemSpec Entry(FactoryKey key, SystemMultiplicity multiplicity, SchemaRef domain, AccessMode mode)
        {
            return new SystemSpec(
                key,
                multiplicity,
                Access(new AccessDeclaration(domain, mode, default(Id128))),
                null,
                null,
                null,
                null);
        }

        public static IReadOnlyList<StageSpec> StageOf(StageId stage, IReadOnlyList<StageSpec> stages)
        {
            for (int i = 0; i < stages.Count; i++)
            {
                if (stages[i].StageId.Equals(stage))
                {
                    return new List<StageSpec> { stages[i] };
                }
            }

            return new List<StageSpec>();
        }

        private static AccessSet Access(params AccessDeclaration[] declarations) => new AccessSet(declarations);

        // ---------------------------------------------------------------- buffers

        /// <summary>
        /// The four stage-local step buffers plus the ingress lane. Each declares exactly one producer, one consuming
        /// owner, one lifetime and one bounded capacity, so a required row that cannot be delivered rejects before
        /// mutation instead of being dropped (P-041, P-043).
        /// </summary>
        public static IReadOnlyList<BufferSpec> Buffers()
        {
            return new List<BufferSpec>
            {
                Buffer(
                    NarrativeKeys.ChoiceBuffer,
                    NarrativeKeys.ChoiceCommandSchema,
                    new List<FactoryKey> { NarrativeKeys.HostIngressProducer },
                    NarrativeKeys.InputStage,
                    NarrativeKeys.InputStage,
                    NarrativeKeys.ChoiceOrderKey,
                    BufferLifetime.Step,
                    LaneCapacity),
                Buffer(
                    NarrativeKeys.ChoiceRequestBuffer,
                    NarrativeKeys.ChoiceCommandSchema,
                    new List<FactoryKey> { NarrativeKeys.InputSystem },
                    NarrativeKeys.DialogueStage,
                    NarrativeKeys.DialogueStage,
                    NarrativeKeys.ChoiceRequestOrderKey,
                    BufferLifetime.Stage,
                    LaneCapacity),
                Buffer(
                    NarrativeKeys.FactChangeBuffer,
                    NarrativeKeys.QuestMutationSchema,
                    new List<FactoryKey> { NarrativeKeys.DialogueSystem },
                    NarrativeKeys.QuestStage,
                    NarrativeKeys.QuestStage,
                    NarrativeKeys.FactChangeOrderKey,
                    BufferLifetime.Stage,
                    LaneCapacity),
                Buffer(
                    NarrativeKeys.FactObservedBuffer,
                    NarrativeKeys.FactObservedSchema,
                    new List<FactoryKey> { NarrativeKeys.QuestSystem },
                    NarrativeKeys.GateStage,
                    NarrativeKeys.GateStage,
                    NarrativeKeys.FactObservedOrderKey,
                    BufferLifetime.Stage,
                    LaneCapacity),
                Buffer(
                    NarrativeKeys.EncounterObservedBuffer,
                    NarrativeKeys.FactObservedSchema,
                    new List<FactoryKey> { NarrativeKeys.QuestSystem },
                    NarrativeKeys.EncounterStage,
                    NarrativeKeys.EncounterStage,
                    NarrativeKeys.EncounterObservedOrderKey,
                    BufferLifetime.Stage,
                    LaneCapacity),
            };
        }

        private static BufferSpec Buffer(
            BufferId buffer,
            SchemaRef schema,
            IReadOnlyList<FactoryKey> producers,
            StageId ownerStage,
            StageId consumerStage,
            FactoryKey orderKey,
            BufferLifetime lifetime,
            int capacity)
        {
            return new BufferSpec(
                buffer,
                schema,
                producers,
                ownerStage,
                consumerStage,
                orderKey,
                lifetime,
                capacity,
                BufferOverflowPolicy.RejectBeforeMutation,
                BufferCancellationPolicy.Drain);
        }

        // ---------------------------------------------------------------- state slots

        /// <summary>
        /// The owned state slots of the five authoritative domains. One owner per domain (P-034); each domain's
        /// physical layout declares the fields its slots own (P-033). The conversation domain declares schema version
        /// 2 while a target seeded before its chapter is covered holds version 1, so the plan stages a registered
        /// migration on bounded scratch (P-029, P-032).
        /// </summary>
        public static IReadOnlyList<StateSlotSpec> Slots()
        {
            return new List<StateSlotSpec>
            {
                // Conversation: the dialogue owner's two slots (node, status), one layout, one field each (P-033).
                Slot(
                    NarrativeKeys.ConversationNodeSlot,
                    NarrativeKeys.DialogueOwner,
                    NarrativeKeys.ConversationDomain,
                    NarrativeKeys.ConversationLayout,
                    NarrativeKeys.ConversationNodeField,
                    NarrativeKeys.ConversationInit,
                    NarrativeKeys.ConversationConfigChange,
                    NarrativeKeys.ConversationNodeMigration,
                    LastSupportPolicy.PreserveDormant,
                    default(FactoryKey),
                    new List<FactoryKey> { NarrativeKeys.ConversationNodeMigration }),

                Slot(
                    NarrativeKeys.ConversationStatusSlot,
                    NarrativeKeys.DialogueOwner,
                    NarrativeKeys.ConversationDomain,
                    NarrativeKeys.ConversationLayout,
                    NarrativeKeys.ConversationStatusField,
                    NarrativeKeys.ConversationInit,
                    NarrativeKeys.ConversationConfigChange,
                    NarrativeKeys.ConversationStatusMigration,
                    LastSupportPolicy.PreserveDormant,
                    default(FactoryKey),
                    new List<FactoryKey> { NarrativeKeys.ConversationStatusMigration }),

                // Facts: the ledger's own slots, one value and one version per declared fact (P-032).
                FactSlot(NarrativeKeys.BridgePermitValueSlot, NarrativeKeys.Key(NarrativeCompositionNames.FactBridgePermitValueFieldName)),
                FactSlot(NarrativeKeys.BridgePermitVersionSlot, NarrativeKeys.Key(NarrativeCompositionNames.FactBridgePermitVersionFieldName)),
                FactSlot(NarrativeKeys.HarborPermitValueSlot, NarrativeKeys.Key(NarrativeCompositionNames.FactHarborPermitValueFieldName)),
                FactSlot(NarrativeKeys.HarborPermitVersionSlot, NarrativeKeys.Key(NarrativeCompositionNames.FactHarborPermitVersionFieldName)),

                // Gates: the gate owner's decision and the fact version it was evaluated from.
                Slot(
                    NarrativeKeys.GateDecisionSlot,
                    NarrativeKeys.GateOwner,
                    NarrativeKeys.GateDomain,
                    NarrativeKeys.GateLayout,
                    NarrativeKeys.GateDecisionField,
                    NarrativeKeys.GateInit,
                    NarrativeKeys.GateConfigChange,
                    default(FactoryKey),
                    LastSupportPolicy.PreserveDormant,
                    default(FactoryKey),
                    null),

                Slot(
                    NarrativeKeys.GateEvaluatedVersionSlot,
                    NarrativeKeys.GateOwner,
                    NarrativeKeys.GateDomain,
                    NarrativeKeys.GateLayout,
                    NarrativeKeys.GateEvaluatedVersionField,
                    NarrativeKeys.GateInit,
                    NarrativeKeys.GateConfigChange,
                    default(FactoryKey),
                    LastSupportPolicy.PreserveDormant,
                    default(FactoryKey),
                    null),

                // Encounters: the encounter owner's lifecycle status.
                Slot(
                    NarrativeKeys.EncounterStatusSlot,
                    NarrativeKeys.EncounterOwner,
                    NarrativeKeys.EncounterDomain,
                    NarrativeKeys.EncounterLayout,
                    NarrativeKeys.EncounterStatusField,
                    NarrativeKeys.EncounterInit,
                    NarrativeKeys.EncounterConfigChange,
                    default(FactoryKey),
                    LastSupportPolicy.PreserveDormant,
                    default(FactoryKey),
                    null),

                // Trail: the compiled order's own observability, written by the two ordered stages (P-034, P-040).
                Slot(
                    NarrativeKeys.TrailStepsSlot,
                    NarrativeKeys.TrailOwner,
                    NarrativeKeys.TrailDomain,
                    NarrativeKeys.TrailLayout,
                    NarrativeKeys.TrailStepsField,
                    NarrativeKeys.TrailInit,
                    NarrativeKeys.TrailConfigChange,
                    default(FactoryKey),
                    LastSupportPolicy.RemoveDerived,
                    default(FactoryKey),
                    null),

                Slot(
                    NarrativeKeys.TrailProjectedSlot,
                    NarrativeKeys.TrailOwner,
                    NarrativeKeys.TrailDomain,
                    NarrativeKeys.TrailLayout,
                    NarrativeKeys.TrailProjectedField,
                    NarrativeKeys.TrailInit,
                    NarrativeKeys.TrailConfigChange,
                    default(FactoryKey),
                    LastSupportPolicy.RemoveDerived,
                    default(FactoryKey),
                    null),

                Slot(
                    NarrativeKeys.TrailFactsSlot,
                    NarrativeKeys.TrailOwner,
                    NarrativeKeys.TrailDomain,
                    NarrativeKeys.TrailLayout,
                    NarrativeKeys.TrailFactsField,
                    NarrativeKeys.TrailInit,
                    NarrativeKeys.TrailConfigChange,
                    default(FactoryKey),
                    LastSupportPolicy.RemoveDerived,
                    default(FactoryKey),
                    null),

                Slot(
                    NarrativeKeys.TrailHooksSlot,
                    NarrativeKeys.TrailOwner,
                    NarrativeKeys.TrailDomain,
                    NarrativeKeys.TrailLayout,
                    NarrativeKeys.TrailHooksField,
                    NarrativeKeys.TrailInit,
                    NarrativeKeys.TrailConfigChange,
                    default(FactoryKey),
                    LastSupportPolicy.RemoveDerived,
                    default(FactoryKey),
                    null),

                Slot(
                    NarrativeKeys.TrailGateDecisionsSlot,
                    NarrativeKeys.TrailOwner,
                    NarrativeKeys.TrailDomain,
                    NarrativeKeys.TrailLayout,
                    NarrativeKeys.TrailGateDecisionsField,
                    NarrativeKeys.TrailInit,
                    NarrativeKeys.TrailConfigChange,
                    default(FactoryKey),
                    LastSupportPolicy.RemoveDerived,
                    default(FactoryKey),
                    null),
            };
        }

        private static StateSlotSpec FactSlot(SlotId slot, FactoryKey field)
        {
            return Slot(
                slot,
                NarrativeKeys.QuestOwner,
                NarrativeKeys.QuestDomain,
                NarrativeKeys.QuestLayout,
                field,
                NarrativeKeys.QuestInit,
                NarrativeKeys.QuestConfigChange,
                default(FactoryKey),
                LastSupportPolicy.PreserveDormant,
                NarrativeKeys.QuestTransfer,
                null);
        }

        private static StateSlotSpec Slot(
            SlotId slot,
            OwnerId owner,
            SchemaRef domain,
            FactoryKey layout,
            FactoryKey field,
            FactoryKey init,
            FactoryKey configChange,
            FactoryKey migration,
            LastSupportPolicy lastSupport,
            FactoryKey transfer,
            IReadOnlyList<FactoryKey>? migrationKeys)
        {
            return new StateSlotSpec(
                slot,
                owner,
                domain,
                layout,
                new List<FieldOwnership> { new FieldOwnership(domain, field.RegistrationKey) },
                init,
                configChange,
                migration,
                lastSupport,
                transfer,
                migrationKeys);
        }

        private static CompositionPolicy PolicyOf(string policyName)
        {
            if (string.Equals(policyName, "Additive", System.StringComparison.Ordinal))
            {
                return CompositionPolicy.Additive;
            }

            if (string.Equals(policyName, "Ordered", System.StringComparison.Ordinal))
            {
                return CompositionPolicy.Ordered;
            }

            if (string.Equals(policyName, "Exclusive", System.StringComparison.Ordinal))
            {
                return CompositionPolicy.Exclusive;
            }

            if (string.Equals(policyName, "Incompatible", System.StringComparison.Ordinal))
            {
                return CompositionPolicy.Incompatible;
            }

            return CompositionPolicy.Replace;
        }

        /// <summary>The canonical big-endian int32 slot value of 05 section 6, written without a fixture dependency.</summary>
        private static FrozenPayload NarrativePayloadCodecFrozen(int value)
        {
            return new FrozenPayload(NarrativePayloadCodec.EncodeInt32(value));
        }
    }
}

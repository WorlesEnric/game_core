// GameCore.Rules.Narrative — every name the narrative slice registers (P-001, P-059, TEST-021).
//
// TEST-021 asks a reference composition to prove that it registers no compulsory actor/action/vitality/physics/
// animation state or stage. The proof is an audit over the complete set of names the slice introduces — scopes,
// recipes, targets, capabilities, schemas, domains, owners, slots, stages, systems, buffers, routes and
// registrations — so the set must be declared in one place rather than collected at run time.
//
// This list is content-only (strings): the rules package is engine-free, so the same list is audited in a plain
// test, in the Editor and in a player, and the audit's outcome is part of the canonical trace.
#nullable enable
using System.Collections.Generic;

namespace GameCore.Rules.Narrative
{
    /// <summary>The complete name set of the narrative slice, in a fixed order.</summary>
    public static class NarrativeRegistrations
    {
        /// <summary>
        /// Names in declaration order: scopes, recipes, targets, capabilities, schemas, domains, owners, slots,
        /// stages, systems, buffers, routes, order keys, migrations, the applier, the package and the predicate.
        /// </summary>
        public static IReadOnlyList<string> AllNames { get; } = new[]
        {
            // Scopes (07 section 3.1).
            NarrativeCompositionNames.StoryWorld,
            NarrativeCompositionNames.ChapterOne,
            NarrativeCompositionNames.ChapterTwo,
            NarrativeCompositionNames.Village,
            NarrativeCompositionNames.Grove,
            NarrativeCompositionNames.Museum,
            NarrativeCompositionNames.Harbor,

            // Reusable recipes.
            NarrativeCompositionNames.VillagerRecipe,
            NarrativeCompositionNames.QuestGateRecipe,
            NarrativeCompositionNames.QuestEncounterRecipe,
            NarrativeCompositionNames.DecorativeCrowdRecipe,
            NarrativeCompositionNames.QuestLedgerRecipe,
            NarrativeCompositionNames.FutureVillagerRecipe,
            NarrativeCompositionNames.ForwardVillagerRecipe,

            // Live targets and the future one.
            NarrativeCompositionNames.Mara,
            NarrativeCompositionNames.GateEast,
            NarrativeCompositionNames.CrowdProp,
            NarrativeCompositionNames.EncounterOak,
            NarrativeCompositionNames.Display,
            NarrativeCompositionNames.Sailor,
            NarrativeCompositionNames.QuestLedger,
            NarrativeCompositionNames.FutureVillager,

            // Derived capabilities (this package's own, chapter-ordinal valued).
            NarrativeCompositionNames.DialogueBindingCapability,
            NarrativeCompositionNames.GateBindingCapability,
            NarrativeCompositionNames.EncounterBindingCapability,
            NarrativeCompositionNames.ChoiceBindingCapability,

            // Payload and domain schemas.
            NarrativeCompositionNames.DialogueBindingSchema,
            NarrativeCompositionNames.GateBindingSchema,
            NarrativeCompositionNames.EncounterBindingSchema,
            NarrativeCompositionNames.ChoiceBindingSchema,
            NarrativeCompositionNames.ChoiceCommandSchema,
            NarrativeCompositionNames.QuestMutationSchema,
            NarrativeCompositionNames.QuestFactCommittedSchema,
            NarrativeCompositionNames.GateChangedSchema,

            // Authoritative domains and their single owners.
            NarrativeCompositionNames.ConversationDomain,
            NarrativeCompositionNames.QuestDomain,
            NarrativeCompositionNames.GateDomain,
            NarrativeCompositionNames.EncounterDomain,
            NarrativeCompositionNames.DialogueOwnerName,
            NarrativeCompositionNames.QuestOwnerName,
            NarrativeCompositionNames.GateOwnerName,
            NarrativeCompositionNames.EncounterOwnerName,

            // Owned state slots.
            NarrativeCompositionNames.ConversationSlotName,
            NarrativeCompositionNames.QuestSlotName,
            NarrativeCompositionNames.GateSlotName,
            NarrativeCompositionNames.EncounterSlotName,

            // Stages and systems of the reference graph.
            NarrativeCompositionNames.InputStageName,
            NarrativeCompositionNames.QuestStageName,
            NarrativeCompositionNames.GateStageName,
            NarrativeCompositionNames.EncounterStageName,
            NarrativeCompositionNames.OutputStageName,
            NarrativeCompositionNames.InputSystemName,
            NarrativeCompositionNames.QuestSystemName,
            NarrativeCompositionNames.GateSystemName,
            NarrativeCompositionNames.EncounterSystemName,
            NarrativeCompositionNames.OutputSystemName,

            // Message-plane lane, route, order key and host ingress producer.
            NarrativeCompositionNames.ChoiceBufferName,
            NarrativeCompositionNames.ChoiceRouteName,
            NarrativeCompositionNames.QuestMutationRouteName,
            NarrativeCompositionNames.QuestMutationBufferName,
            NarrativeCompositionNames.ChoiceOrderKeyName,
            NarrativeCompositionNames.QuestMutationOrderKeyName,
            NarrativeCompositionNames.HostIngressProducerName,

            // Owned fact slots and the physical fields the quest domain's layout declares per slot (P-032, P-033).
            NarrativeCompositionNames.FactBridgePermitValueSlotName,
            NarrativeCompositionNames.FactBridgePermitVersionSlotName,
            NarrativeCompositionNames.FactHarborPermitValueSlotName,
            NarrativeCompositionNames.FactHarborPermitVersionSlotName,
            NarrativeCompositionNames.FactBridgePermitValueFieldName,
            NarrativeCompositionNames.FactBridgePermitVersionFieldName,
            NarrativeCompositionNames.FactHarborPermitValueFieldName,
            NarrativeCompositionNames.FactHarborPermitVersionFieldName,

            // Generated layout/initialization/configuration/policy keys of the four declared domains.
            NarrativeCompositionNames.ConversationDomainLayoutName,
            NarrativeCompositionNames.ConversationDomainInitName,
            NarrativeCompositionNames.ConversationDomainConfigChangeName,
            NarrativeCompositionNames.ConversationDomainFieldName,
            NarrativeCompositionNames.QuestDomainLayoutName,
            NarrativeCompositionNames.QuestDomainInitName,
            NarrativeCompositionNames.QuestDomainConfigChangeName,
            NarrativeCompositionNames.GateDomainLayoutName,
            NarrativeCompositionNames.GateDomainInitName,
            NarrativeCompositionNames.GateDomainConfigChangeName,
            NarrativeCompositionNames.GateDomainFieldName,
            NarrativeCompositionNames.EncounterDomainLayoutName,
            NarrativeCompositionNames.EncounterDomainInitName,
            NarrativeCompositionNames.EncounterDomainConfigChangeName,
            NarrativeCompositionNames.EncounterDomainFieldName,

            NarrativeCompositionNames.ChoiceRequestBufferName,
            NarrativeCompositionNames.FactChangeBufferName,
            NarrativeCompositionNames.FactObservedBufferName,
            NarrativeCompositionNames.ChoiceRequestOrderKeyName,
            NarrativeCompositionNames.FactChangeOrderKeyName,
            NarrativeCompositionNames.FactObservedOrderKeyName,


            // Registrations: the migration, the base-layout applier, the package and the static predicate.
            NarrativeCompositionNames.ConversationMigrationName,
            NarrativeCompositionNames.RecipeApplierName,
            NarrativeCompositionNames.OwnerPackageName,
            NarrativeCompositionNames.AlwaysPredicateName,
        };

        /// <summary>The number of registered names the audit examines.</summary>
        public static int Count => AllNames.Count;

        /// <summary>Audits every registered name for genre vocabulary (P-001, TEST-021).</summary>
        public static GenreAuditReport Audit() => NarrativeGenreAudit.Audit(AllNames);
    }
}

// GameCore.Gameplay.Narrative.Fixtures — the narrative slice's precompiled spawn recipes (04 section 6, P-024).
//
// A recipe is a reusable compatibility declaration, not an instance: its descriptor lists the schemas the target
// supports, and its applier installs the *base layout* — the target's own state storage and initial values. Derived
// rows are never installed here; the assembly publisher adds them inside the publication fence, so a spawned target's
// first visible image is already its complete effective assembly (P-024).
//
// The applier writes real slots: a villager starts with a fresh conversation state at the declared schema version, a
// gate with a closed decision, an encounter idle, and the world-level ledger with its durable fact slots. A slot
// written at the declared version needs no migration; the scenario separately seeds version 1 state for one target so
// the registered migration path really runs (P-029, P-032).
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Rules.Narrative;
using RulesNarrativeFacts = GameCore.Rules.Narrative.NarrativeFacts;
using GameCore.Unity.Runtime;
using Unity.Entities;

namespace GameCore.Gameplay.Narrative.Fixtures
{
    /// <summary>Generated-style base-layout applier of every narrative recipe (04 section 6).</summary>
    public sealed class NarrativeRecipeApplier : ISpawnApplier
    {
        public FactoryKey Key => NarrativeKeys.RecipeApplier;

        /// <summary>Targets whose base layout this applier installed.</summary>
        public int AppliedCount { get; private set; }

        /// <summary>Slots this applier installed across every target.</summary>
        public int InstalledSlotCount { get; private set; }

        public void ApplyBaseLayout(EntityManager entityManager, Entity entity, SpawnRecipe recipe)
        {
            // The marker carries the recipe identity; the slots below are the target's own state storage (P-032).
            entityManager.AddComponentData(entity, new NarrativeTargetMarker
            {
                RecipeSchemaHigh = recipe.Recipe.Schema.Id.Value.High,
                RecipeSchemaLow = recipe.Recipe.Schema.Id.Value.Low,
                RecipeRevision = recipe.Recipe.Revision.Value,
            });

            if (!entityManager.HasBuffer<TargetSlotState>(entity))
            {
                entityManager.AddBuffer<TargetSlotState>(entity);
            }

            DynamicBuffer<TargetSlotState> slots = entityManager.GetBuffer<TargetSlotState>(entity);

            if (recipe.Recipe.Equals(NarrativeKeys.VillagerRecipe))
            {
                // A villager starts with a fresh conversation: the chapter's opening node and an idle status, at the
                // schema version the descriptor declares (so nothing needs migrating).
                Add(slots, NarrativeKeys.ConversationNodeSlot, NarrativeKeys.DialogueOwner,
                    NarrativeKeys.ConversationDomain.Version, NarrativeDialogueRules.PermitResultNode - 1);
                Add(slots, NarrativeKeys.ConversationStatusSlot, NarrativeKeys.DialogueOwner,
                    NarrativeKeys.ConversationDomain.Version, NarrativeConversationStatus.Idle);
            }
            else if (recipe.Recipe.Equals(NarrativeKeys.QuestGateRecipe))
            {
                Add(slots, NarrativeKeys.GateDecisionSlot, NarrativeKeys.GateOwner,
                    NarrativeKeys.GateDomain.Version, NarrativeGateRules.Closed);
                Add(slots, NarrativeKeys.GateEvaluatedVersionSlot, NarrativeKeys.GateOwner,
                    NarrativeKeys.GateDomain.Version, 0);
            }
            else if (recipe.Recipe.Equals(NarrativeKeys.QuestEncounterRecipe))
            {
                Add(slots, NarrativeKeys.EncounterStatusSlot, NarrativeKeys.EncounterOwner,
                    NarrativeKeys.EncounterDomain.Version, NarrativeEncounterStatus.Idle);
            }
            else if (recipe.Recipe.Equals(NarrativeKeys.QuestLedgerRecipe))
            {
                // The ledger's durable facts: one value slot and one version slot per declared fact key (P-032).
                for (int i = 0; i < RulesNarrativeFacts.DeclaredFactKeys.Count; i++)
                {
                    string factKey = RulesNarrativeFacts.DeclaredFactKeys[i];
                    bool known = RulesNarrativeFacts.TryGetFactSlotTag(factKey, out string slotTag);
                    if (!known)
                    {
                        continue;
                    }

                    Add(slots, NarrativeKeys.FactValueSlot(slotTag), NarrativeKeys.QuestOwner,
                        NarrativeKeys.QuestDomain.Version, RulesNarrativeFacts.InitialValue);
                    Add(slots, NarrativeKeys.FactVersionSlot(slotTag), NarrativeKeys.QuestOwner,
                        NarrativeKeys.QuestDomain.Version, RulesNarrativeFacts.InitialVersion);
                }

                // The trail's five slots: the compiled order's own observability (P-034, P-040).
                Add(slots, NarrativeKeys.TrailStepsSlot, NarrativeKeys.TrailOwner,
                    NarrativeKeys.TrailDomain.Version, 0);
                Add(slots, NarrativeKeys.TrailProjectedSlot, NarrativeKeys.TrailOwner,
                    NarrativeKeys.TrailDomain.Version, 0);
                Add(slots, NarrativeKeys.TrailFactsSlot, NarrativeKeys.TrailOwner,
                    NarrativeKeys.TrailDomain.Version, 0);
                Add(slots, NarrativeKeys.TrailHooksSlot, NarrativeKeys.TrailOwner,
                    NarrativeKeys.TrailDomain.Version, 0);
                Add(slots, NarrativeKeys.TrailGateDecisionsSlot, NarrativeKeys.TrailOwner,
                    NarrativeKeys.TrailDomain.Version, 0);
            }

            AppliedCount++;
        }

        private void Add(DynamicBuffer<TargetSlotState> slots, SlotId slot, OwnerId owner, uint version, int value)
        {
            if (AssemblyStorage.TryFindSlot(slots, owner, slot, out int row))
            {
                TargetSlotState existing = slots[row];
                existing.SchemaVersion = version;
                existing.Value = value;
                existing.Active = 1;
                slots[row] = existing;
                return;
            }

            slots.Add(new TargetSlotState
            {
                Slot = slot,
                Owner = owner,
                SchemaVersion = version,
                Value = value,
                Active = 1,
            });
            InstalledSlotCount++;
        }
    }

    /// <summary>The slice's recipes and its closed recipe catalog (P-015, 04 section 6).</summary>
    public static class NarrativeRecipes
    {
        public static SpawnRecipe Villager(NarrativeRecipeApplier applier)
            => Recipe(NarrativeKeys.VillagerRecipe, NarrativeCompositionNames.VillagerRecipe, applier);

        public static SpawnRecipe QuestGate(NarrativeRecipeApplier applier)
            => Recipe(NarrativeKeys.QuestGateRecipe, NarrativeCompositionNames.QuestGateRecipe, applier);

        public static SpawnRecipe QuestEncounter(NarrativeRecipeApplier applier)
            => Recipe(NarrativeKeys.QuestEncounterRecipe, NarrativeCompositionNames.QuestEncounterRecipe, applier);

        public static SpawnRecipe DecorativeCrowd(NarrativeRecipeApplier applier)
            => Recipe(NarrativeKeys.DecorativeCrowdRecipe, NarrativeCompositionNames.DecorativeCrowdRecipe, applier);

        public static SpawnRecipe QuestLedger(NarrativeRecipeApplier applier)
            => Recipe(NarrativeKeys.QuestLedgerRecipe, NarrativeCompositionNames.QuestLedgerRecipe, applier);

        /// <summary>The forward provider's recipe: registered, and used by no live target (see the handoff).</summary>
        public static SpawnRecipe Forward(NarrativeRecipeApplier applier)
            => Recipe(NarrativeKeys.ForwardVillagerRecipe, NarrativeCompositionNames.ForwardVillagerRecipe, applier);

        /// <summary>The world's closed recipe catalog over one applier instance.</summary>
        public static SpawnRecipeCatalog Catalog(NarrativeRecipeApplier applier)
        {
            return new SpawnRecipeCatalog(new List<SpawnRecipe>
            {
                Villager(applier),
                QuestGate(applier),
                QuestEncounter(applier),
                DecorativeCrowd(applier),
                QuestLedger(applier),
                Forward(applier),
            });
        }

        private static SpawnRecipe Recipe(DefinitionRef recipe, string recipeSchemaName, ISpawnApplier applier)
        {
            var descriptor = new TargetDescriptor(
                recipe,
                new List<SchemaRef> { NarrativeIds.SchemaRef(recipeSchemaName, 1U) },
                null,
                null,
                default(AssetAdapterDescriptor),
                null,
                null,
                null,
                null);

            return new SpawnRecipe(
                recipe,
                descriptor,
                new List<SchemaRef> { NarrativeIds.SchemaRef(recipeSchemaName, 1U) },
                applier);
        }
    }
}

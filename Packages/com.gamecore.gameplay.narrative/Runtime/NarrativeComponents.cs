// GameCore.Gameplay.Narrative — the narrative slice's ECS component inventory (P-001, P-032, TEST-021).
//
// Authoritative gameplay state lives in the published `TargetSlotState` buffer of the target that owns it, exactly
// as the protocol addresses state: `(TargetId, OwnerId, SlotId)`. The component below is therefore not a second
// state store — it is the immutable *assembly-derived* marker of a target's selected recipe, installed by the
// recipe's own base-layout applier (04 section 6) and never written by a gameplay system.
//
// The slice declares exactly one component type, and it is deliberately not an actor, vitality, physics or
// animation type: the inventory below is what the genre-neutrality step of the scenario audits (TEST-021).
#nullable enable
using System.Collections.Generic;
using Unity.Entities;

namespace GameCore.Gameplay.Narrative
{
    /// <summary>
    /// Base-layout marker of one narrative target: its recipe identity, so a fixture can tell a ledger entity from a
    /// villager, a gate or an encounter without consulting a second authority (P-015, P-024).
    /// </summary>
    public struct NarrativeTargetMarker : IComponentData
    {
        /// <summary>Stable recipe schema id of the target's recipe; zero before a base layout is installed.</summary>
        public ulong RecipeSchemaHigh;

        public ulong RecipeSchemaLow;

        /// <summary>Recipe revision the target was created at (P-024).</summary>
        public ulong RecipeRevision;
    }

    /// <summary>The slice's declared ECS component inventory, so a neutrality audit has a fixed set to examine.</summary>
    public static class NarrativeComponentInventory
    {
        /// <summary>Component type names the narrative slice registers, in declaration order.</summary>
        public static IReadOnlyList<string> Names { get; } = new[]
        {
            "NarrativeTargetMarker",
        };

        /// <summary>The number of component types the slice declares.</summary>
        public static int Count => Names.Count;
    }
}

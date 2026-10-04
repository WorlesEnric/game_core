// GameCore.Gameplay.Contracts - stable diagnostic codes of the player, NPC and interaction packages (P1.3).
//
// Same rules as GameplayDiagnosticCodes: a code is never renumbered or reused. Validators, tools and runtime
// diagnostics report these; rule refusals carry their own stable codes (GameCore.Rules.Gameplay.*.Refusals).
#nullable enable
using System.Collections.Generic;

namespace GameCore.Gameplay.Contracts
{
    /// <summary>Stable codes GP-PLY-nnn, GP-NPC-nnn and GP-INT-nnn.</summary>
    public static class PlayerNpcInteractionCodes
    {
        // Player
        public const string PlayerMissingDefinition = "GP-PLY-001";
        public const string PlayerNotInWorld = "GP-PLY-002";
        public const string PlayerTuningOutOfRange = "GP-PLY-003";
        public const string PlayerCameraOutOfRange = "GP-PLY-004";
        public const string PlayerSpawnOutsideRegion = "GP-PLY-005";
        public const string PlayerMissingInputActions = "GP-PLY-006";
        public const string PlayerNotFocusEntity = "GP-PLY-007";

        // NPCs
        public const string NpcMissingEntityDefinition = "GP-NPC-001";
        public const string NpcDuplicateEntityDefinition = "GP-NPC-002";
        public const string NpcSpeedOutOfRange = "GP-NPC-003";
        public const string NpcPatrolEmpty = "GP-NPC-004";
        public const string NpcPatrolOutsideRegion = "GP-NPC-005";
        public const string NpcScheduleMalformed = "GP-NPC-006";
        public const string NpcNotPlaced = "GP-NPC-007";
        public const string NpcNoConversationSystem = "GP-NPC-020";

        // Interaction
        public const string InteractableMissingEntityDefinition = "GP-INT-001";
        public const string InteractableDuplicateEntityDefinition = "GP-INT-002";
        public const string InteractableIllegalState = "GP-INT-003";
        public const string InteractableMissingPrompt = "GP-INT-004";
        public const string InteractableLockWithoutCondition = "GP-INT-005";
        public const string InteractableNotPlaced = "GP-INT-006";
        public const string TriggerSizeOutOfRange = "GP-INT-007";

        /// <summary>Every code, in declaration order.</summary>
        public static IReadOnlyList<string> All { get; } = System.Array.AsReadOnly(new[]
        {
            PlayerMissingDefinition, PlayerNotInWorld, PlayerTuningOutOfRange, PlayerCameraOutOfRange,
            PlayerSpawnOutsideRegion, PlayerMissingInputActions, PlayerNotFocusEntity,
            NpcMissingEntityDefinition, NpcDuplicateEntityDefinition, NpcSpeedOutOfRange, NpcPatrolEmpty,
            NpcPatrolOutsideRegion, NpcScheduleMalformed, NpcNotPlaced, NpcNoConversationSystem,
            InteractableMissingEntityDefinition, InteractableDuplicateEntityDefinition, InteractableIllegalState,
            InteractableMissingPrompt, InteractableLockWithoutCondition, InteractableNotPlaced, TriggerSizeOutOfRange,
        });
    }
}

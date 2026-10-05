// GameCore.Gameplay.Contracts - stable diagnostic codes of the gameplay plugin library (P1.1).
//
// Codes are stable strings: validators, tools, the bake and the runtime report them, and Studio shows them. A code is
// never renumbered or reused; a retired code stays reserved.
#nullable enable
using System.Collections.Generic;

namespace GameCore.Gameplay.Contracts
{
    /// <summary>Stable gameplay diagnostic codes (GP-&lt;area&gt;-&lt;nnn&gt;).</summary>
    public static class GameplayDiagnosticCodes
    {
        // Authoring identity
        public const string MissingAuthoringId = "GP-ID-001";
        public const string InvalidAuthoringId = "GP-ID-002";
        public const string DuplicateAuthoringId = "GP-ID-003";
        public const string StableKeyCollision = "GP-ID-004";

        // Entities
        public const string EntityMissingDefinition = "GP-ENT-001";
        public const string EntityOutsideRegion = "GP-ENT-002";
        public const string EntityVariantOutOfRange = "GP-ENT-003";
        public const string EntityUnknownOverride = "GP-ENT-004";
        public const string EntityScaleOutOfRange = "GP-ENT-005";
        public const string DefinitionMissingPrefab = "GP-ENT-006";
        public const string EntityMaterialTextureInvalid = "GP-ENT-007";
        public const string EntityAlreadyAlive = "GP-ENT-010";
        public const string EntityAlreadyDead = "GP-ENT-011";
        public const string EntityNotAlive = "GP-ENT-012";
        public const string EntityUnchanged = "GP-ENT-013";

        // World
        public const string RegionMissingScene = "GP-WLD-001";
        public const string RegionMissingBounds = "GP-WLD-002";
        public const string PortalUnconnected = "GP-WLD-003";
        public const string PortalTargetsOwnRegion = "GP-WLD-004";
        public const string WorldMissingStartRegion = "GP-WLD-005";
        public const string WorldUnknownRegion = "GP-WLD-006";
        public const string RegionMultipleInScene = "GP-WLD-007";
        public const string TravelNoPortal = "GP-WLD-010";
        public const string TravelSameRegion = "GP-WLD-011";
        public const string TravelUnknownRegion = "GP-WLD-012";
        public const string ResidencyIllegalTransition = "GP-WLD-020";
        public const string ResidencyNotHost = "GP-WLD-021";

        // World runtime refusals and streaming (P1.7a's GameCore.Rules.Gameplay.World.WorldRefusalCodes, folded in by P1.7b;
        // the values are identical and a dotnet test keeps them so)
        public const string TravelConditionFailed = "GP-WLD-013";
        public const string TravelConditionUnknown = "GP-WLD-014";
        public const string TravelStaleTraveller = "GP-WLD-015";
        public const string TravelMissingPortal = "GP-WLD-016";
        public const string TravelAlreadyApplied = "GP-WLD-017";
        public const string PlaceNotAllowed = "GP-WLD-022";
        public const string PlaceStaleTarget = "GP-WLD-023";
        public const string SceneLoadFailed = "GP-WLD-030";
        public const string SceneNotInBuild = "GP-WLD-031";
        public const string SceneLoadLatched = "GP-WLD-032";
        public const string WorldMalformedCommand = "GP-WLD-040";

        // Compile
        public const string BakeStale = "GP-CMP-001";
        public const string BakeVerifyMismatch = "GP-CMP-002";
        public const string BakeSceneUnreadable = "GP-CMP-003";
        public const string CatalogStale = "GP-CMP-004";

        /// <summary>Every code, in declaration order.</summary>
        public static IReadOnlyList<string> All { get; } = System.Array.AsReadOnly(new[]
        {
            MissingAuthoringId, InvalidAuthoringId, DuplicateAuthoringId, StableKeyCollision,
            EntityMissingDefinition, EntityOutsideRegion, EntityVariantOutOfRange, EntityUnknownOverride,
            EntityScaleOutOfRange, DefinitionMissingPrefab, EntityMaterialTextureInvalid, EntityAlreadyAlive, EntityAlreadyDead, EntityNotAlive,
            EntityUnchanged,
            RegionMissingScene, RegionMissingBounds, PortalUnconnected, PortalTargetsOwnRegion, WorldMissingStartRegion,
            WorldUnknownRegion, RegionMultipleInScene, TravelNoPortal, TravelSameRegion, TravelUnknownRegion,
            ResidencyIllegalTransition, ResidencyNotHost,
            TravelConditionFailed, TravelConditionUnknown, TravelStaleTraveller, TravelMissingPortal, TravelAlreadyApplied,
            PlaceNotAllowed, PlaceStaleTarget, SceneLoadFailed, SceneNotInBuild, SceneLoadLatched, WorldMalformedCommand,
            BakeStale, BakeVerifyMismatch, BakeSceneUnreadable, CatalogStale,
        });
    }

    /// <summary>One gameplay diagnostic: a stable code, the authored object it is about, and a message.</summary>
    public sealed class GameplayDiagnostic
    {
        public GameplayDiagnostic(string code, string subjectId, string message)
        {
            Code = code ?? string.Empty;
            SubjectId = subjectId ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public string Code { get; }

        /// <summary>Authoring id (or asset path) of the object the diagnostic is about; empty when global.</summary>
        public string SubjectId { get; }

        public string Message { get; }

        public override string ToString() => Code + " [" + SubjectId + "] " + Message;
    }
}

#nullable enable
// Hollowmere.Mechanism.PressurePlate - authoring objects of the pressure plate (W-MECH-01 sample).
//
// PressurePlateDefinition is the authorable definition (mirror type id pressureplate.definition): the plate's threshold
// and maximum weight. PressurePlateAuthoring is a placed plate in a region scene; both carry one serialized authoring id,
// minted once (Reset/OnValidate when empty) and never re-derived, exactly like the P1.1 entity objects, and the kernel
// target id of a placed plate is derived from it (AuthoringIds.TargetIdFor).
using System;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using Hollowmere.Mechanism.PressurePlate.Rules;
using UnityEngine;

namespace Hollowmere.Mechanism.PressurePlate
{
    /// <summary>Default definition values of a plate placed without a definition asset.</summary>
    public static class PlateDefaults
    {
        public const int Threshold = 1;

        public const int MaxWeight = 4;

        /// <summary>Upper bound of both authorable values.</summary>
        public const int Limit = 64;
    }

    /// <summary>Refusal codes of the plate authoring tools (GP-style: the message starts with the code).</summary>
    public static class PlateDiagnosticCodes
    {
        /// <summary>The definition values are invalid (threshold below 1, maximum below threshold, or above the limit).</summary>
        public const string InvalidDefinition = "GP-PLATE-001";

        /// <summary>The target scene is not loaded.</summary>
        public const string SceneNotLoaded = "GP-PLATE-002";

        /// <summary>The location is not a finite position.</summary>
        public const string InvalidLocation = "GP-PLATE-003";
    }

    /// <summary>The definition a placed pressure plate is evaluated against.</summary>
    [Authorable("pressureplate.definition", DisplayName = "Pressure Plate Definition", Scope = AuthorScope.Definition,
        RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "A pressure plate definition: the weight at which the plate is pressed and the largest weight it accepts.")]
    [CreateAssetMenu(menuName = "Hollowmere/Mechanisms/Pressure Plate Definition", fileName = "PressurePlateDefinition")]
    public sealed class PressurePlateDefinition : ScriptableObject, IDefinitionAsset
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorField(Min = 1, Max = PlateDefaults.Limit, Required = true, Doc = "Actors on the plate at which it is pressed.")]
        [SerializeField] private int threshold = PlateDefaults.Threshold;

        [AuthorField(Min = 1, Max = PlateDefaults.Limit, Required = true, Doc = "Largest number of actors the plate accepts.")]
        [SerializeField] private int maxWeight = PlateDefaults.MaxWeight;

        [SerializeField] private string contentStamp = string.Empty;

        public string AuthoringId => authoringId;

        public string DefinitionName => name;

        public string ContentStamp => contentStamp;

        public int Threshold => threshold;

        public int MaxWeight => maxWeight;

        /// <summary>The rules' view of the definition.</summary>
        public PlateSpec Spec => new PlateSpec(threshold, maxWeight);

        /// <summary>True when the values are a valid plate (threshold >= 1, max >= threshold, both within the limit).</summary>
        public bool IsValid => IsValidPair(threshold, maxWeight);

        public static bool IsValidPair(int threshold, int maxWeight) =>
            new PlateSpec(threshold, maxWeight).IsValid && maxWeight <= PlateDefaults.Limit;

        public bool EnsureAuthoringId()
        {
            string next = AuthoringIdField.Ensure(authoringId);
            bool changed = !string.Equals(next, authoringId, StringComparison.Ordinal);
            authoringId = next;
            return changed;
        }

        /// <summary>Configures the definition in one call (authoring tools and tests).</summary>
        public void Configure(int plateThreshold, int plateMaxWeight)
        {
            threshold = plateThreshold;
            maxWeight = plateMaxWeight;
        }

        /// <summary>Written by a bake: the content stamp of the authorable fields.</summary>
        public void SetContentStamp(string stamp) => contentStamp = stamp ?? string.Empty;

        private void Reset() => EnsureAuthoringId();

        private void OnValidate()
        {
            if (!AuthoringIds.IsValid(authoringId))
            {
                EnsureAuthoringId();
            }
        }
    }

    /// <summary>A placed pressure plate in a region scene: the authoring proxy of one plate target.</summary>
    [Authorable("pressureplate.instance", DisplayName = "Pressure Plate", Scope = AuthorScope.Instance | AuthorScope.Prefab,
        RuntimeApplicability = RuntimeApply.Rebuild, Doc = "A placed pressure plate: one kernel target with a plate definition.")]
    [DisallowMultipleComponent]
    public sealed class PressurePlateAuthoring : MonoBehaviour, IAuthoredObject
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorRef(Category = "pressureplate.definition", Required = false,
            Doc = "The plate definition; without one the plate uses threshold 1 and maximum weight 4.")]
        [SerializeField] private PressurePlateDefinition? definition;

        public string AuthoringId => authoringId;

        public PressurePlateDefinition? Definition => definition;

        public int Threshold => definition != null ? definition.Threshold : PlateDefaults.Threshold;

        public int MaxWeight => definition != null ? definition.MaxWeight : PlateDefaults.MaxWeight;

        /// <summary>The kernel target id of this plate (empty id until minted).</summary>
        public TargetId TargetId => AuthoringIds.IsValid(authoringId) ? AuthoringIds.TargetIdFor(authoringId) : default(TargetId);

        public bool EnsureAuthoringId()
        {
            string next = AuthoringIdField.Ensure(authoringId);
            bool changed = !string.Equals(next, authoringId, StringComparison.Ordinal);
            authoringId = next;
            return changed;
        }

        public void SetDefinition(PressurePlateDefinition? plateDefinition) => definition = plateDefinition;

        /// <summary>Places this authored plate in a booted world (scope = its region's scope).</summary>
        public bool PlaceIn(PressurePlateWorld world, ScopeId scope)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            return world.Place(TargetId, scope, Threshold, MaxWeight);
        }

        private void Reset() => EnsureAuthoringId();

        private void OnValidate()
        {
            if (!AuthoringIds.IsValid(authoringId))
            {
                EnsureAuthoringId();
            }
        }
    }
}

// GameCore.Gameplay.Entities - VariantDefinition (P1.1).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using UnityEngine;

namespace GameCore.Gameplay.Entities
{
    /// <summary>A named variant of an entity definition: an optional replacement prefab and a tint.</summary>
    [Authorable("entity.variant", DisplayName = "Entity Variant", Scope = AuthorScope.Definition,
        RuntimeApplicability = RuntimeApply.Rebuild, Doc = "A variant of an entity definition (replacement prefab and tint).")]
    [CreateAssetMenu(menuName = "GameCore/Gameplay/Entity Variant", fileName = "EntityVariant")]
    public sealed class VariantDefinition : ScriptableObject, IAuthoredObject
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorRef(Category = "asset.prefab", Required = false, Doc = "Replaces the definition's prefab when set.")]
        [SerializeField] private GameObject? prefab;

        [AuthorField(Doc = "Tint applied to the view's renderers (white = none).")]
        [SerializeField] private Color tint = Color.white;

        public string AuthoringId => authoringId;

        public GameObject? Prefab => prefab;

        public Color Tint => tint;

        /// <summary>Mints the authoring id when it is missing; returns true when it changed.</summary>
        public bool EnsureAuthoringId()
        {
            string next = AuthoringIdField.Ensure(authoringId);
            bool changed = !string.Equals(next, authoringId, StringComparison.Ordinal);
            authoringId = next;
            return changed;
        }

        public void Configure(GameObject? replacementPrefab, Color variantTint)
        {
            prefab = replacementPrefab;
            tint = variantTint;
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

// GameCore.Gameplay.Entities - EntityDefinition (P1.1).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using UnityEngine;

namespace GameCore.Gameplay.Entities
{
    [CreateAssetMenu(menuName = "GameCore/Gameplay/Entity Definition", fileName = "EntityDefinition")]
    public sealed class EntityDefinition : ScriptableObject, IDefinitionAsset
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorRef(Category = "asset.prefab", Doc = "The view prefab instantiated for every placed entity.")]
        [SerializeField] private GameObject? prefab;

        [AuthorField(Unit = GameplayUnits.MilliUnit, Min = 1, Max = 100000, Doc = "Uniform scale in thousandths.")]
        [SerializeField] private int defaultScaleMilli = GameplayUnits.ScaleOne;

        [AuthorField(Doc = "Whether a placed entity starts visible.")]
        [SerializeField] private bool startsVisible = true;

        [AuthorField(Doc = "Whether a placed entity starts alive (spawned).")]
        [SerializeField] private bool startsAlive = true;

        [AuthorRef(Category = "entity.variant", Required = false, Doc = "Variants 1..n; variant 0 is the definition itself.")]
        [SerializeField] private List<VariantDefinition> variants = new List<VariantDefinition>();

        [AuthorField(Doc = "Fields a placed entity may override: scaleMilli, visible, alive, tint.")]
        [SerializeField] private List<string> overridableFields = new List<string> { OverrideSet.ScaleMilli, OverrideSet.Visible, OverrideSet.Tint };

        [AuthorField(Doc = "Animator integer parameters driven from entity slots.")]
        [SerializeField] private List<AnimatorSlotBinding> animatorBindings = new List<AnimatorSlotBinding>();

        [AuthorField(Doc = "Interaction kind exposed to interaction systems (empty = not interactable).")]
        [SerializeField] private string interactionKind = string.Empty;

        [SerializeField] private string contentStamp = string.Empty;

        public string AuthoringId => authoringId;

        public string DefinitionName => name;

        public string ContentStamp => contentStamp;

        public GameObject? Prefab => prefab;

        public int DefaultScaleMilli => defaultScaleMilli;

        public bool StartsVisible => startsVisible;

        public bool StartsAlive => startsAlive;

        public IReadOnlyList<VariantDefinition> Variants => variants;

        /// <summary>Variant count including variant 0 (the definition itself).</summary>
        public int VariantCount => variants.Count + 1;

        public IReadOnlyList<string> OverridableFields => overridableFields;

        public IReadOnlyList<AnimatorSlotBinding> AnimatorBindings => animatorBindings;

        public string InteractionKind => interactionKind;

        public bool IsOverridable(string field) => overridableFields.Contains(field);

        /// <summary>The variant at <paramref name="index"/> (1..n), or null for variant 0 or an out-of-range index.</summary>
        public VariantDefinition? VariantAt(int index) =>
            index >= 1 && index <= variants.Count ? variants[index - 1] : null;

        public bool EnsureAuthoringId()
        {
            string next = AuthoringIdField.Ensure(authoringId);
            bool changed = !string.Equals(next, authoringId, StringComparison.Ordinal);
            authoringId = next;
            return changed;
        }

        /// <summary>Written by the bake: the content stamp of the authorable fields.</summary>
        public void SetContentStamp(string stamp) => contentStamp = stamp ?? string.Empty;

        /// <summary>Configures the definition in one call (authoring tools and tests).</summary>
        public void Configure(GameObject? viewPrefab, int scaleMilli, bool visible, bool alive)
        {
            prefab = viewPrefab;
            defaultScaleMilli = scaleMilli;
            startsVisible = visible;
            startsAlive = alive;
        }

        public void SetVariants(IReadOnlyList<VariantDefinition> list)
        {
            variants = new List<VariantDefinition>(list);
        }

        public void SetOverridableFields(IReadOnlyList<string> fields)
        {
            overridableFields = new List<string>(fields);
        }

        public void SetAnimatorBindings(IReadOnlyList<AnimatorSlotBinding> bindings)
        {
            animatorBindings = new List<AnimatorSlotBinding>(bindings);
        }

        public void SetInteractionKind(string kind) => interactionKind = kind ?? string.Empty;

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

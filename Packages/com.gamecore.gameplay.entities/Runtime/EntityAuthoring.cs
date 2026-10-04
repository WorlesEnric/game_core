// GameCore.Gameplay.Entities - authored entity types (P1.1, Studio 03 s1/s4).
//
// Authored objects carry one serialized authoring id, minted once (Reset/OnValidate when empty) and never re-derived;
// everything the kernel uses is derived from it (AuthoringIds). A prefab asset keeps an empty id: each placed instance
// mints its own on instantiation, so two instances of one prefab never share an identity, and re-validating an
// instance never changes its id. Duplicating a placed entity is done with the entity.duplicate tool, which mints a
// fresh id; a plain copy that kept the id is reported by the validator (GP-ID-003) and refused by the bake.
//
// Authorable metadata uses the gameplay mirror of Studio's attributes (GameCore.Gameplay.Contracts.Authorable* ...);
// the gameplay runtime packages never reference com.gamecore.studio.*.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using UnityEngine;

namespace GameCore.Gameplay.Entities
{
    /// <summary>Mints and repairs serialized authoring ids; shared by every authored type.</summary>
    public static class AuthoringIdField
    {
        /// <summary>Returns <paramref name="current"/> when valid, otherwise a freshly minted id.</summary>
        public static string Ensure(string? current) => AuthoringIds.IsValid(current) ? current! : AuthoringIds.Mint();
    }

    /// <summary>One field override of a placed entity: a declared overridable field name and its canonical value text.</summary>
    [Serializable]
    public sealed class OverrideEntry
    {
        [SerializeField] private string field = string.Empty;
        [SerializeField] private string value = string.Empty;

        public OverrideEntry()
        {
        }

        public OverrideEntry(string field, string value)
        {
            this.field = field ?? string.Empty;
            this.value = value ?? string.Empty;
        }

        public string Field => field;

        public string Value => value;
    }

    /// <summary>
    /// The override set of a placed entity. Recognised fields: <c>scaleMilli</c> (int), <c>visible</c> (true/false),
    /// <c>alive</c> (true/false) and <c>tint</c> (#rrggbb); a definition declares which of them its instances may override.
    /// </summary>
    [Serializable]
    public sealed class OverrideSet
    {
        public const string ScaleMilli = "scaleMilli";
        public const string Visible = "visible";
        public const string Alive = "alive";
        public const string Tint = "tint";

        [SerializeField] private List<OverrideEntry> entries = new List<OverrideEntry>();

        public IReadOnlyList<OverrideEntry> Entries => entries;

        public int Count => entries.Count;

        public bool TryGet(string field, out string value)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].Field, field, StringComparison.Ordinal))
                {
                    value = entries[i].Value;
                    return true;
                }
            }

            value = string.Empty;
            return false;
        }

        /// <summary>Sets (or replaces) one override; an empty value removes it.</summary>
        public void Set(string field, string value)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].Field, field, StringComparison.Ordinal))
                {
                    if (string.IsNullOrEmpty(value))
                    {
                        entries.RemoveAt(i);
                    }
                    else
                    {
                        entries[i] = new OverrideEntry(field, value);
                    }

                    return;
                }
            }

            if (!string.IsNullOrEmpty(value))
            {
                entries.Add(new OverrideEntry(field, value));
            }
        }

        public void Clear() => entries.Clear();
    }

    /// <summary>Recognised override fields and their canonical value formats.</summary>
    public static class OverrideFields
    {
        public static bool IsKnown(string field) =>
            field == OverrideSet.ScaleMilli || field == OverrideSet.Visible || field == OverrideSet.Alive || field == OverrideSet.Tint;

        /// <summary>Validates an override value; <paramref name="error"/> explains a refusal.</summary>
        public static bool IsValidValue(string field, string value, out string error)
        {
            error = string.Empty;
            switch (field)
            {
                case OverrideSet.ScaleMilli:
                    if (int.TryParse(value, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int scale)
                        && scale >= 1 && scale <= 100000)
                    {
                        return true;
                    }

                    error = "scaleMilli is an integer in [1, 100000]";
                    return false;
                case OverrideSet.Visible:
                case OverrideSet.Alive:
                    if (value == "true" || value == "false")
                    {
                        return true;
                    }

                    error = field + " is true or false";
                    return false;
                case OverrideSet.Tint:
                    if (value.Length == 7 && value[0] == '#' && IsHex(value, 1))
                    {
                        return true;
                    }

                    error = "tint is #rrggbb";
                    return false;
                default:
                    error = "'" + field + "' is not an overridable field";
                    return false;
            }
        }

        public static bool TryInt(string value, out int result) =>
            int.TryParse(value, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out result);

        private static bool IsHex(string value, int start)
        {
            for (int i = start; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                {
                    return false;
                }
            }

            return true;
        }
    }

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

    /// <summary>Binds one entity slot member to an Animator integer parameter (AnimatorBinder).</summary>
    [Serializable]
    public sealed class AnimatorSlotBinding
    {
        [SerializeField] private string slot = "variant";
        [SerializeField] private string parameter = string.Empty;

        public AnimatorSlotBinding()
        {
        }

        public AnimatorSlotBinding(string slot, string parameter)
        {
            this.slot = slot;
            this.parameter = parameter;
        }

        /// <summary>Entity slot member: alive, variant, scaleMilli or visible.</summary>
        public string Slot => slot;

        public string Parameter => parameter;
    }

    /// <summary>The definition a placed entity instantiates: prefab, defaults, variants and what instances may override.</summary>
    [Authorable("entity.definition", DisplayName = "Entity Definition", Scope = AuthorScope.Definition,
        RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "An entity definition: the prefab a placed entity presents, its defaults, variants and overridable fields.")]
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

    /// <summary>
    /// A placed entity in a region scene: the authoring proxy of one kernel target. At play time a running gameplay world
    /// deactivates the proxy and presents the target through its definition's prefab instead.
    /// </summary>
    [Authorable("entity.instance", DisplayName = "Authored Entity", Scope = AuthorScope.Instance | AuthorScope.Prefab,
        RuntimeApplicability = RuntimeApply.Rebuild, Doc = "A placed entity: one kernel target with a definition, a variant and overrides.")]
    [DisallowMultipleComponent]
    public sealed class AuthoredEntity : MonoBehaviour, IAuthoredObject
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorRef(Category = "entity.definition", Doc = "The definition this entity instantiates.")]
        [SerializeField] private EntityDefinition? definition;

        [AuthorField(Min = 0, Doc = "Variant index: 0 is the definition itself, 1..n its variants.")]
        [SerializeField] private int variant;

        [AuthorField(Doc = "Per-instance overrides of the definition's overridable fields.")]
        [SerializeField] private OverrideSet overrides = new OverrideSet();

        public string AuthoringId => authoringId;

        public EntityDefinition? Definition => definition;

        public int Variant => variant;

        public OverrideSet Overrides => overrides;

        /// <summary>
        /// The region this entity belongs to: the one authored region of its scene (auto-detected, never serialized).
        /// Null when the scene has no region, which the validator reports.
        /// </summary>
        public IAuthoredRegion? Region
        {
            get
            {
                if (!gameObject.scene.IsValid())
                {
                    return null;
                }

                GameObject[] roots = gameObject.scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    MonoBehaviour[] behaviours = roots[i].GetComponentsInChildren<MonoBehaviour>(true);
                    for (int b = 0; b < behaviours.Length; b++)
                    {
                        if (behaviours[b] is IAuthoredRegion region)
                        {
                            return region;
                        }
                    }
                }

                return null;
            }
        }

        public string RegionId
        {
            get
            {
                IAuthoredRegion? region = Region;
                return region != null ? region.AuthoringId : string.Empty;
            }
        }

        /// <summary>The kernel target id of this placed entity (empty id until minted).</summary>
        public GameCore.Contracts.TargetId TargetId =>
            AuthoringIds.IsValid(authoringId) ? AuthoringIds.TargetIdFor(authoringId) : default(GameCore.Contracts.TargetId);

        public bool EnsureAuthoringId()
        {
            string next = AuthoringIdField.Ensure(authoringId);
            bool changed = !string.Equals(next, authoringId, StringComparison.Ordinal);
            authoringId = next;
            return changed;
        }

        /// <summary>Replaces the authoring id with a fresh one (the entity.duplicate tool on the copy).</summary>
        public void RemintAuthoringId() => authoringId = AuthoringIds.Mint();

        /// <summary>Clears the id (a prefab asset never carries one).</summary>
        public void ClearAuthoringId() => authoringId = string.Empty;

        public void SetDefinition(EntityDefinition? value) => definition = value;

        public void SetVariant(int value) => variant = value;

        private void Reset() => MintIfPlaced();

        private void OnValidate() => MintIfPlaced();

        /// <summary>Mints the id on a placed instance; a prefab asset (no valid scene) keeps an empty id.</summary>
        private void MintIfPlaced()
        {
            if (!gameObject.scene.IsValid())
            {
                return;
            }

            if (AuthoringIds.IsValid(authoringId))
            {
                return;
            }

#if UNITY_EDITOR
            // Prefab mode edits the prefab asset through a preview scene: the asset itself keeps an empty id.
            if (UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(gameObject) != null)
            {
                return;
            }
#endif

            EnsureAuthoringId();
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
            if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(this))
            {
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
            }
#endif
        }
    }
}

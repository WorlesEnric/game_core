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
}

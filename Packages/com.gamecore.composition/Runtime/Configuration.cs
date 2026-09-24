// GameCore.Composition — immutable configuration documents, patches and provenance (P-020).
//
// P-020 is explicit about what reconfiguration may and may not do: contract fields use scalar replacement,
// canonical set union, or a registered bounded reducer; arrays are ordered values; schema defaults, inherited
// contributions and local configuration patches are separate provenance layers in that order; a local patch
// has an explicit mask of fields; null and missing are distinct; and no generic deep merge is permitted. That
// is exactly what this file implements — one canonical field-keyed document, layered composition with
// per-field provenance, and no recursive merge anywhere.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Composition
{
    /// <summary>Canonical value kind of one configuration field (05 s3, P-020).</summary>
    public enum ConfigValueKind
    {
        /// <summary>An explicit null: the field is cleared, which is distinct from the field being absent.</summary>
        Null = 0,

        UInt32 = 1,
        UInt64 = 2,
        Int32 = 3,
        Int64 = 4,
        Bool = 5,
        Id = 6,
        Bytes = 7,
        Utf8 = 8,

        /// <summary>Canonical set-union field: composition is the sorted unique union (P-020).</summary>
        IdSet = 9,

        /// <summary>Ordered id array: an ordered value replaced as a whole unless a registered reducer applies (P-020).</summary>
        OrderedIds = 10,
    }

    /// <summary>
    /// One immutable configuration field value. Exactly one representation is live per kind; the unused fields
    /// are default. Equality is by kind and canonical content, so a value can be hashed and compared without a
    /// type switch leaking into caller code.
    /// </summary>
    public readonly struct ConfigFieldValue : IEquatable<ConfigFieldValue>
    {
        private readonly ulong numeric;
        private readonly long signed;
        private readonly bool boolean;
        private readonly Id128 id;
        private readonly FrozenPayload? bytes;
        private readonly string? text;
        private readonly IReadOnlyList<Id128>? ids;

        private ConfigFieldValue(
            ConfigValueKind kind,
            ulong numeric,
            long signed,
            bool boolean,
            Id128 id,
            FrozenPayload? bytes,
            string? text,
            IReadOnlyList<Id128>? ids)
        {
            Kind = kind;
            this.numeric = numeric;
            this.signed = signed;
            this.boolean = boolean;
            this.id = id;
            this.bytes = bytes;
            this.text = text;
            this.ids = ids;
        }

        public ConfigValueKind Kind { get; }

        public static ConfigFieldValue Null { get; } =
            new ConfigFieldValue(ConfigValueKind.Null, 0UL, 0L, false, Id128.Zero, null, null, null);

        public static ConfigFieldValue OfUInt32(uint value) =>
            new ConfigFieldValue(ConfigValueKind.UInt32, value, 0L, false, Id128.Zero, null, null, null);

        public static ConfigFieldValue OfUInt64(ulong value) =>
            new ConfigFieldValue(ConfigValueKind.UInt64, value, 0L, false, Id128.Zero, null, null, null);

        public static ConfigFieldValue OfInt32(int value) =>
            new ConfigFieldValue(ConfigValueKind.Int32, 0UL, value, false, Id128.Zero, null, null, null);

        public static ConfigFieldValue OfInt64(long value) =>
            new ConfigFieldValue(ConfigValueKind.Int64, 0UL, value, false, Id128.Zero, null, null, null);

        public static ConfigFieldValue OfBool(bool value) =>
            new ConfigFieldValue(ConfigValueKind.Bool, 0UL, 0L, value, Id128.Zero, null, null, null);

        public static ConfigFieldValue OfId(Id128 value) =>
            new ConfigFieldValue(ConfigValueKind.Id, 0UL, 0L, false, value, null, null, null);

        public static ConfigFieldValue OfBytes(FrozenPayload value) =>
            new ConfigFieldValue(ConfigValueKind.Bytes, 0UL, 0L, false, Id128.Zero, value ?? throw new ArgumentNullException(nameof(value)), null, null);

        public static ConfigFieldValue OfText(string value) =>
            new ConfigFieldValue(ConfigValueKind.Utf8, 0UL, 0L, false, Id128.Zero, null, value ?? throw new ArgumentNullException(nameof(value)), null);

        /// <summary>Canonical set-union value: duplicates are removed and the set is stored in ascending id order.</summary>
        public static ConfigFieldValue OfIdSet(IReadOnlyList<Id128>? value) =>
            new ConfigFieldValue(ConfigValueKind.IdSet, 0UL, 0L, false, Id128.Zero, null, null, CanonicalOrder.Sort(value, CanonicalOrder.Compare));

        public static ConfigFieldValue OfOrderedIds(IReadOnlyList<Id128>? value) =>
            new ConfigFieldValue(
                ConfigValueKind.OrderedIds,
                0UL,
                0L,
                false,
                Id128.Zero,
                null,
                null,
                ContractCollections.Freeze(value == null ? null : new List<Id128>(value)));

        public uint AsUInt32 => (uint)numeric;

        public ulong AsUInt64 => numeric;

        public int AsInt32 => (int)signed;

        public long AsInt64 => signed;

        public bool AsBool => boolean;

        public Id128 AsId => id;

        public FrozenPayload? AsBytes => bytes;

        public string? AsText => text;

        public IReadOnlyList<Id128> AsIds => ids ?? Array.Empty<Id128>();

        /// <summary>Id-set union with another id set; ordered ids are never unioned (P-020).</summary>
        public ConfigFieldValue UnionWith(ConfigFieldValue other)
        {
            if (Kind != ConfigValueKind.IdSet || other.Kind != ConfigValueKind.IdSet)
            {
                throw new InvalidOperationException("Only two id-set values can be unioned.");
            }

            List<Id128> merged = new List<Id128>(AsIds.Count + other.AsIds.Count);
            merged.AddRange(AsIds);
            merged.AddRange(other.AsIds);
            merged.Sort(CanonicalOrder.Compare);

            List<Id128> unique = new List<Id128>(merged.Count);
            for (int i = 0; i < merged.Count; i++)
            {
                if (i == 0 || !merged[i].Equals(merged[i - 1]))
                {
                    unique.Add(merged[i]);
                }
            }

            return OfIdSet(unique);
        }

        public bool Equals(ConfigFieldValue other)
        {
            if (Kind != other.Kind)
            {
                return false;
            }

            switch (Kind)
            {
                case ConfigValueKind.Null:
                    return true;
                case ConfigValueKind.UInt32:
                case ConfigValueKind.UInt64:
                    return AsUInt64 == other.AsUInt64;
                case ConfigValueKind.Int32:
                case ConfigValueKind.Int64:
                    return AsInt64 == other.AsInt64;
                case ConfigValueKind.Bool:
                    return AsBool == other.AsBool;
                case ConfigValueKind.Id:
                    return AsId.Equals(other.AsId);
                case ConfigValueKind.Bytes:
                    return BytesEqual(AsBytes, other.AsBytes);
                case ConfigValueKind.Utf8:
                    return string.Equals(AsText, other.AsText, StringComparison.Ordinal);
                default:
                    return IdsEqual(AsIds, other.AsIds);
            }
        }

        public override bool Equals(object? obj) => obj is ConfigFieldValue other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (int)Kind;
                hash = (hash * 31) + AsUInt64.GetHashCode();
                hash = (hash * 31) + AsInt64.GetHashCode();
                hash = (hash * 31) + AsBool.GetHashCode();
                hash = (hash * 31) + AsId.GetHashCode();
                hash = (hash * 31) + (AsText == null ? 0 : AsText.GetHashCode());
                IReadOnlyList<Id128> list = AsIds;
                for (int i = 0; i < list.Count; i++)
                {
                    hash = (hash * 31) + list[i].GetHashCode();
                }

                return hash;
            }
        }

        public static bool operator ==(ConfigFieldValue left, ConfigFieldValue right) => left.Equals(right);

        public static bool operator !=(ConfigFieldValue left, ConfigFieldValue right) => !left.Equals(right);

        public override string ToString() => Kind + "(" + Describe() + ")";

        private string Describe()
        {
            switch (Kind)
            {
                case ConfigValueKind.Null:
                    return "null";
                case ConfigValueKind.UInt32:
                case ConfigValueKind.UInt64:
                    return AsUInt64.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case ConfigValueKind.Int32:
                case ConfigValueKind.Int64:
                    return AsInt64.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case ConfigValueKind.Bool:
                    return AsBool ? "true" : "false";
                case ConfigValueKind.Id:
                    return AsId.ToString();
                case ConfigValueKind.Utf8:
                    return AsText ?? string.Empty;
                case ConfigValueKind.Bytes:
                    return (AsBytes == null ? 0 : AsBytes.Length).ToString(System.Globalization.CultureInfo.InvariantCulture) + " bytes";
                default:
                    return AsIds.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ids";
            }
        }

        private static bool BytesEqual(FrozenPayload? left, FrozenPayload? right)
        {
            if (left == null || right == null)
            {
                return left == null && right == null;
            }

            if (left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (left.Bytes[i] != right.Bytes[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IdsEqual(IReadOnlyList<Id128> left, IReadOnlyList<Id128> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!left[i].Equals(right[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>One field of a configuration document: stable field key plus canonical value (05 s6).</summary>
    public readonly struct ConfigField
    {
        public readonly Id128 Key;
        public readonly ConfigFieldValue Value;

        public ConfigField(Id128 key, ConfigFieldValue value)
        {
            Key = key;
            Value = value;
        }

        public override string ToString() => Key.ToString() + "=" + Value.ToString();
    }

    /// <summary>
    /// Immutable configuration document: fields with stable keys in canonical order, no duplicates. A patch is
    /// the same shape; its mask is exactly its field-key set, so an absent field is untouched while an explicit
    /// null clears it (P-020).
    /// </summary>
    public sealed class ConfigDocument
    {
        private readonly IReadOnlyList<ConfigField> fields;

        /// <summary>Builds a document, rejecting a duplicate field key rather than silently choosing one.</summary>
        public ConfigDocument(IReadOnlyList<ConfigField>? source)
        {
            IReadOnlyList<ConfigField> sorted = CanonicalOrder.Sort(source, CompareFields);
            for (int i = 1; i < sorted.Count; i++)
            {
                if (sorted[i].Key.Equals(sorted[i - 1].Key))
                {
                    throw new ArgumentException("A configuration document declares one value per field key.", nameof(source));
                }
            }

            fields = sorted;
        }

        public static ConfigDocument Empty { get; } = new ConfigDocument(null);

        public IReadOnlyList<ConfigField> Fields => fields;

        public int Count => fields.Count;

        public bool IsEmpty => fields.Count == 0;

        /// <summary>True when the mask contains the key, whether or not the patch value is an explicit null.</summary>
        public bool HasField(Id128 key)
        {
            for (int i = 0; i < fields.Count; i++)
            {
                if (fields[i].Key.Equals(key))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryGetField(Id128 key, out ConfigFieldValue value)
        {
            for (int i = 0; i < fields.Count; i++)
            {
                if (fields[i].Key.Equals(key))
                {
                    value = fields[i].Value;
                    return true;
                }
            }

            value = ConfigFieldValue.Null;
            return false;
        }

        /// <summary>An explicit-null patch entry for one field; distinct from omitting the field (P-020).</summary>
        public static ConfigDocument Clear(params Id128[] keys)
        {
            if (keys == null)
            {
                throw new ArgumentNullException(nameof(keys));
            }

            List<ConfigField> entries = new List<ConfigField>(keys.Length);
            for (int i = 0; i < keys.Length; i++)
            {
                entries.Add(new ConfigField(keys[i], ConfigFieldValue.Null));
            }

            return new ConfigDocument(entries);
        }

        public static ConfigDocument Of(params ConfigField[] fields) => new ConfigDocument(fields);

        /// <summary>One layer of the P-020 composition order.</summary>
        public ConfigLayer AsLayer(ConfigLayerOrigin origin, Id128 source) => new ConfigLayer(origin, source, this);

        private static int CompareFields(ConfigField left, ConfigField right) => CanonicalOrder.Compare(left.Key, right.Key);
    }

    /// <summary>Provenance layer of a configuration value (P-020: defaults, inherited, then local patch).</summary>
    public enum ConfigLayerOrigin
    {
        /// <summary>Schema defaults: the lowest layer.</summary>
        SchemaDefaults = 0,

        /// <summary>Inherited contribution from an ancestor scope or provider.</summary>
        InheritedContribution = 1,

        /// <summary>Local configuration patch: the highest layer.</summary>
        LocalPatch = 2,
    }

    /// <summary>One named configuration layer with its provenance source key.</summary>
    public sealed class ConfigLayer
    {
        public ConfigLayer(ConfigLayerOrigin origin, Id128 source, ConfigDocument document)
        {
            Origin = origin;
            Source = source;
            Document = document ?? throw new ArgumentNullException(nameof(document));
        }

        public ConfigLayerOrigin Origin { get; }

        /// <summary>Stable identity of the contributing declaration (scope or installation).</summary>
        public Id128 Source { get; }

        public ConfigDocument Document { get; }
    }

    /// <summary>Where an effective field value came from; explicit null is recorded, not hidden.</summary>
    public readonly struct ConfigFieldProvenance
    {
        public readonly Id128 Key;
        public readonly ConfigLayerOrigin Origin;
        public readonly Id128 Source;
        public readonly bool IsExplicitNull;

        public ConfigFieldProvenance(Id128 key, ConfigLayerOrigin origin, Id128 source, bool isExplicitNull)
        {
            Key = key;
            Origin = origin;
            Source = source;
            IsExplicitNull = isExplicitNull;
        }

        public override string ToString() =>
            Key.ToString() + ":" + Origin.ToString() + "@" + Source.ToString() + (IsExplicitNull ? "(null)" : string.Empty);
    }

    /// <summary>Effective configuration plus per-field provenance, or a rejection code.</summary>
    public sealed class ConfigComposeResult
    {
        private ConfigComposeResult(DiagnosticCode code, ConfigDocument value, IReadOnlyList<ConfigFieldProvenance>? provenance)
        {
            Code = code;
            Value = value;
            Provenance = ContractCollections.Freeze(provenance);
        }

        public DiagnosticCode Code { get; }

        public ConfigDocument Value { get; }

        /// <summary>Sorted by field key, so provenance ordering never depends on layer order.</summary>
        public IReadOnlyList<ConfigFieldProvenance> Provenance { get; }

        public bool Succeeded => Code == DiagnosticCode.None;

        public static ConfigComposeResult Composed(ConfigDocument value, IReadOnlyList<ConfigFieldProvenance> provenance) =>
            new ConfigComposeResult(DiagnosticCode.None, value, provenance);

        /// <summary>The layers disagree about a field's kind at a point where no canonical composition exists.</summary>
        public static ConfigComposeResult Rejected(DiagnosticCode code, ConfigDocument value) =>
            new ConfigComposeResult(code, value, null);
    }

    /// <summary>
    /// Layered configuration composer. Layer order is schema defaults, inherited contributions, then the local
    /// patch; each layer is applied field by field, so this is scalar replacement plus explicit set union, never
    /// a generic deep merge (P-020).
    /// </summary>
    public static class ConfigComposer
    {
        public static ConfigComposeResult Compose(IReadOnlyList<ConfigLayer>? layers)
        {
            if (layers == null || layers.Count == 0)
            {
                return ConfigComposeResult.Composed(ConfigDocument.Empty, Array.Empty<ConfigFieldProvenance>());
            }

            Dictionary<Id128, ConfigFieldValue> values = new Dictionary<Id128, ConfigFieldValue>();
            Dictionary<Id128, ConfigFieldProvenance> provenance = new Dictionary<Id128, ConfigFieldProvenance>();

            for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                ConfigLayer layer = layers[layerIndex];
                if (layer == null)
                {
                    throw new ArgumentException("A configuration layer must not be null.", nameof(layers));
                }

                IReadOnlyList<ConfigField> fields = layer.Document.Fields;
                for (int i = 0; i < fields.Count; i++)
                {
                    ConfigField field = fields[i];
                    ConfigFieldValue incoming = field.Value;

                    if (incoming.Kind == ConfigValueKind.Null)
                    {
                        // Explicit null clears the field; it is not the same as the patch omitting the key.
                        values.Remove(field.Key);
                        provenance[field.Key] = new ConfigFieldProvenance(field.Key, layer.Origin, layer.Source, true);
                        continue;
                    }

                    if (values.TryGetValue(field.Key, out ConfigFieldValue existing))
                    {
                        if (existing.Kind == ConfigValueKind.IdSet && incoming.Kind == ConfigValueKind.IdSet)
                        {
                            values[field.Key] = existing.UnionWith(incoming);
                        }
                        else if (existing.Kind != incoming.Kind)
                        {
                            // No silent coercion between representations; the caller's schema is wrong.
                            return ConfigComposeResult.Rejected(DiagnosticCode.UnsupportedVersion, Current(values));
                        }
                        else
                        {
                            values[field.Key] = incoming;
                        }
                    }
                    else
                    {
                        values[field.Key] = incoming;
                    }

                    provenance[field.Key] = new ConfigFieldProvenance(field.Key, layer.Origin, layer.Source, false);
                }
            }

            List<ConfigFieldProvenance> orderedProvenance = new List<ConfigFieldProvenance>(provenance.Count);
            foreach (KeyValuePair<Id128, ConfigFieldProvenance> pair in provenance)
            {
                orderedProvenance.Add(pair.Value);
            }

            orderedProvenance.Sort(CompareProvenance);
            return ConfigComposeResult.Composed(Current(values), orderedProvenance);
        }

        private static ConfigDocument Current(Dictionary<Id128, ConfigFieldValue> values)
        {
            List<ConfigField> fields = new List<ConfigField>(values.Count);
            foreach (KeyValuePair<Id128, ConfigFieldValue> pair in values)
            {
                fields.Add(new ConfigField(pair.Key, pair.Value));
            }

            return new ConfigDocument(fields);
        }

        private static int CompareProvenance(ConfigFieldProvenance left, ConfigFieldProvenance right) =>
            CanonicalOrder.Compare(left.Key, right.Key);
    }
}

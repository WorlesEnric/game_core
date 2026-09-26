// GameCore.Gameplay.Rewards — the reward installation's configuration schema value and serializer (GC-024).
//
// Normative sources: 07 s5 (a mounted plugin is admitted under one configuration schema) and 05 s6 (a schema
// document is a canonical envelope with declared fields and a trailing checksum, so two writers of the same
// content produce the same bytes).
//
// This is the hand-written generated-style pair the sibling gameplay packages carry as their fixture table's
// serializer (Packages/com.gamecore.gameplay.cards/Fixtures/Runtime/CardCatalogTable.cs:42-66), moved into this
// package's Runtime/ because the installer needs the catalog and therefore needs the serializer that catalog
// registers: `GeneratedSerializerBase` supplies the header, the required-feature gate, the declared-field walk and
// the checksum rules, so the only thing written here is the one declared field.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Gameplay.Rewards
{
    /// <summary>Generated-style value of the reward configuration schema (one required 32-bit field).</summary>
    public readonly struct RewardsConfigValue
    {
        /// <summary>Field id 1, wire type UInt32, required: the configuration document's version.</summary>
        public readonly uint SchemaVersion;

        /// <summary>Builds one configuration value.</summary>
        public RewardsConfigValue(uint schemaVersion)
        {
            SchemaVersion = schemaVersion;
        }

        /// <summary>Diagnostic form; never an identity (P-004).</summary>
        public override string ToString() =>
            "RewardsConfigValue(SchemaVersion=" + SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// Serializer of the reward configuration schema, derived from the production
    /// <see cref="GeneratedSerializerBase"/> so the header, required-feature gate, declared-field walk and checksum
    /// rules are the shared implementation (05 s6, P-054).
    /// </summary>
    public sealed class RewardsConfigSerializer : GeneratedSerializerBase
    {
        private static readonly GeneratedFieldSlot[] DeclaredFieldSlots =
        {
            new GeneratedFieldSlot(1, WireType.UInt32, true),
        };

        /// <summary>Creates the serializer under its generated key and schema.</summary>
        public RewardsConfigSerializer(FactoryKey key, SchemaRef schema)
            : base(key, schema, RewardsCatalog.SupportedFeatureIds)
        {
        }

        /// <summary>Declared fields in ascending field-id order.</summary>
        protected override IReadOnlyList<GeneratedFieldSlot> DeclaredFields => DeclaredFieldSlots;

        /// <summary>Writes one value as a canonical envelope document with a trailing checksum.</summary>
        public byte[] Serialize(RewardsConfigValue value)
        {
            EnvelopeWriter writer = CreateWriter();
            writer.WriteUInt32Field(1, value.SchemaVersion);
            writer.WriteChecksum();
            return writer.ToArray();
        }
    }
}

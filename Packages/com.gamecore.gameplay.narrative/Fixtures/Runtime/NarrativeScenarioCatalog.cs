// GameCore.Gameplay.Narrative.Fixtures — the narrative slice's hand-written generated-style catalog.
//
// The committed generated catalog (`unity/GameCore.Validation/.../ProbeCatalog.g.cs`) is the real compiler output,
// and the scenario runs over it as well. This table exists for the second half of the same proof: a catalog written
// in exactly the shape the compiler emits, validated by the same production `ImmutableCatalog`, needs no build step
// and still mounts the same precompiled provider (04 section 8, P-009).
//
// Layout mirrors `GameCore.Unity.Fixtures.W1GateCatalog`: one `FactoryKey` constant per entry, a
// `FactoryRegistration[]` table carrying owner package and implementation identity, one `SchemaRegistration`, one
// generated value type, one generated serializer, and `Build()`/`Fingerprint()` computed by the production
// functions so a hand-written table is checked exactly like generated output.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Gameplay.Narrative.Fixtures
{
    /// <summary>Generated-style value of the narrative catalog's declared schema (one required 32-bit field).</summary>
    public readonly struct NarrativeCatalogRecord
    {
        /// <summary>Field id 1, wire type Int32, required.</summary>
        public readonly int Ordinal;

        public NarrativeCatalogRecord(int ordinal)
        {
            Ordinal = ordinal;
        }

        public override string ToString() => "NarrativeCatalogRecord(Ordinal=" + Ordinal + ")";
    }

    /// <summary>Generated-style serializer of the narrative catalog's schema (P-054, P-055).</summary>
    public sealed class NarrativeCatalogRecordSerializer : GeneratedSerializerBase
    {
        private static readonly GeneratedFieldSlot[] DeclaredFieldSlots =
        {
            new GeneratedFieldSlot(1, WireType.Int32, true),
        };
        public NarrativeCatalogSerializerKeyHolder Keys { get; } = new NarrativeCatalogSerializerKeyHolder();

        public NarrativeCatalogRecordSerializer()
            : base(NarrativeScenarioCatalog.SerializerKey, NarrativeScenarioCatalog.RecordSchema, NarrativeScenarioCatalog.SupportedFeatureIds)
        {
        }

        /// <summary>Declared fields in ascending field-id order.</summary>
        protected override IReadOnlyList<GeneratedFieldSlot> DeclaredFields => DeclaredFieldSlots;

        /// <summary>Writes one value as a canonical envelope document with a trailing checksum.</summary>
        public byte[] Serialize(NarrativeCatalogRecord value)
        {
            EnvelopeWriter writer = CreateWriter();
            writer.WriteInt32Field(1, value.Ordinal);
            writer.WriteChecksum();
            return writer.ToArray();
        }

        /// <summary>Validates one document against this schema and decodes its declared field.</summary>
        public bool TryDeserialize(byte[] document, out NarrativeCatalogRecord value, out EnvelopeError error)
        {
            value = default(NarrativeCatalogRecord);
            var buffer = new GeneratedFieldBuffer();
            if (!TryReadDeclaredFields(document, buffer, out error))
            {
                return false;
            }

            var reader = new EnvelopeReader(document);
            int decoded = 0;
            for (int i = 0; i < buffer.Count; i++)
            {
                if (!reader.TrySeekTo(buffer.RecordOffset(i)))
                {
                    error = reader.LastError;
                    return false;
                }

                EnvelopeField field = buffer.Field(i);
                if (field.FieldId != 1)
                {
                    continue;
                }

                if (!reader.TryReadField(out EnvelopeField recorded))
                {
                    error = reader.LastError;
                    return false;
                }

                if (!reader.TryReadInt32(recorded, out int fieldValue))
                {
                    error = reader.LastError;
                    return false;
                }

                decoded = fieldValue;
            }

            value = new NarrativeCatalogRecord(decoded);
            error = EnvelopeError.None;
            return true;
        }
    }

    public static class NarrativeScenarioCatalog
    {
        public const string PluginFactoryStableName = "gamecore.narrative.plugin.fixture";

        public const string SerializerStableName = "gamecore.narrative.schema.record";

        /// <summary>Generated key version used by every registration in this table.</summary>
        public const uint KeyVersion = 1U;

        /// <summary>Declared protocol feature this build supports (P-055).</summary>
        public static readonly Id128[] SupportedFeatureIds =
        {
            new Id128(0x4E41525241544631UL, 0x0000000000000001UL),
        };

        /// <summary>Plugin factory key the narrative catalog registers (kind `PluginFactory`).</summary>
        public static readonly FactoryKey PluginFactoryKey =
            new FactoryKey(StableNameKeyDerivation.Derive(PluginFactoryStableName), KeyVersion);

        /// <summary>Generated serializer key of the declared record schema.</summary>
        public static readonly FactoryKey SerializerKey =
            new FactoryKey(StableNameKeyDerivation.Derive(SerializerStableName), KeyVersion);

        /// <summary>The configuration schema every narrative manifest declares in this catalog.</summary>
        public static readonly SchemaRef RecordSchema =
            new SchemaRef(new SchemaId(StableNameKeyDerivation.Derive("gamecore.narrative.schema.config-record")), 1U);

        /// <summary>
        /// Re-derives both literal keys from their stable names with the production rule, so a literal and its stable
        /// name cannot drift apart silently (P-004).
        /// </summary>
        public static bool DerivationHolds() =>
            PluginFactoryKey.RegistrationKey.Equals(StableNameKeyDerivation.Derive(PluginFactoryStableName))
            && SerializerKey.RegistrationKey.Equals(StableNameKeyDerivation.Derive(SerializerStableName));

        private static readonly Id128 OwnerPackage = Id128.Zero;

        private static readonly Id128 PluginImplementation =
            StableNameKeyDerivation.Derive("gamecore.narrative.implementation.fixture");

        /// <summary>One validated serializer instance per declared schema, in the same order as the table.</summary>
        public static readonly ISchemaSerializer[] Serializers =
        {
            new NarrativeCatalogRecordSerializer(),
        };

        /// <summary>Validates this table with the production catalog rules, or reports the exact rejections.</summary>
        public static CatalogBuildResult Build() =>
            ImmutableCatalog.Build(Factories(), Schemas(), SupportedFeatureIds, Serializers);

        /// <summary>The fingerprint an emitted file would carry for this table (P-028).</summary>
        public static ContentHash Fingerprint() =>
            CatalogFingerprint.Compute(Factories(), Schemas(), SupportedFeatureIds);

        /// <summary>The registration table, in the shape the emitter writes.</summary>
        public static FactoryRegistration[] Factories() =>
            new[]
            {
                new FactoryRegistration(PluginFactoryKey, FactoryKind.PluginFactory, OwnerPackage, PluginImplementation, 1U),
                new FactoryRegistration(SerializerKey, FactoryKind.Serializer, OwnerPackage, RecordSchema.Id.Value, RecordSchema.Version),
            };

        /// <summary>Accepted schema registrations, in canonical schema-id order.</summary>
        public static SchemaRegistration[] Schemas() =>
            new[]
            {
                new SchemaRegistration(RecordSchema, OwnerPackage, SerializerKey, true),
            };
    }
}

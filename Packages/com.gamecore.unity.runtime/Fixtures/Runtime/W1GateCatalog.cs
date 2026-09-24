// GameCore.Unity.Fixtures — W1 integration gate catalog: a hand-written registration table in exactly the shape
// `GameCore.Content.Compiler` emits (GC-003), validated by the production `ImmutableCatalog`.
//
// The emitter writes, per group: one `FactoryKey` constant per entry, a `BoundRegistration<T>[]` table with direct
// constructor references, a matching `FactoryRegistration[]` table with owner package and implementation identity,
// one `SchemaRegistration`, one generated value type, one generated serializer and the `Serializers` array, then
// calls `ImmutableCatalog.Build` over the union (see
// `unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs`). This fixture reproduces
// that shape by hand — the same convention `FixtureKeys`/`FixtureRegistration` use for systems — so the W1 gate
// exercises the real contract validation path without a build step.
//
// The committed generated catalog (`ProbeCatalog`) is the real compiler output; the gate's Unity EditMode test and
// its player probe run the identical scenario over it as well, so a drift between hand-written and generated
// conventions is visible.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Unity.Fixtures
{
    /// <summary>Generated-style value of the gate's declared schema (one required 32-bit field).</summary>
    public readonly struct W1GateRecord
    {
        /// <summary>Field id 1, wire type Int32, required.</summary>
        public readonly int Value;

        public W1GateRecord(int value)
        {
            Value = value;
        }

        public override string ToString() => "W1GateRecord(Value=" + Value + ")";
    }

    /// <summary>
    /// Generated-style serializer of the gate's declared schema, derived from the production
    /// <see cref="GeneratedSerializerBase"/> so the header, required-feature gate, declared-field walk and
    /// checksum rules are the shared implementation (P-054, P-055).
    /// </summary>
    public sealed class W1GateRecordSerializer : GeneratedSerializerBase
    {
        private static readonly GeneratedFieldSlot[] DeclaredFieldSlots =
        {
            new GeneratedFieldSlot(1, WireType.Int32, true),
        };

        public W1GateRecordSerializer()
            : base(W1GateCatalog.SerializerKey, W1GateKeys.CatalogSchema, W1GateCatalog.SupportedFeatureIds)
        {
        }

        /// <summary>Declared fields in ascending field-id order.</summary>
        protected override IReadOnlyList<GeneratedFieldSlot> DeclaredFields => DeclaredFieldSlots;

        /// <summary>Writes one value as a canonical envelope document with a trailing checksum.</summary>
        public byte[] Serialize(W1GateRecord value)
        {
            EnvelopeWriter writer = CreateWriter();
            writer.WriteInt32Field(1, value.Value);
            writer.WriteChecksum();
            return writer.ToArray();
        }

        /// <summary>Validates one document against this schema and decodes its declared fields.</summary>
        public bool TryDeserialize(byte[] document, out W1GateRecord value, out EnvelopeError error)
        {
            value = default(W1GateRecord);
            GeneratedFieldBuffer buffer = new GeneratedFieldBuffer();
            if (!TryReadDeclaredFields(document, buffer, out error))
            {
                return false;
            }

            EnvelopeReader reader = new EnvelopeReader(document);
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

                int fieldValue;
                if (!reader.TryReadInt32(recorded, out fieldValue))
                {
                    error = reader.LastError;
                    return false;
                }

                decoded = fieldValue;
            }

            value = new W1GateRecord(decoded);
            error = EnvelopeError.None;
            return true;
        }
    }

    /// <summary>Generated-style registration table of the gate, validated by the production immutable catalog.</summary>
    public static class W1GateCatalog
    {
        public const string PluginFactoryStableName = "gamecore.w1gate.plugin.fixture";

        public const string SerializerStableName = "gamecore.w1gate.schema.record";

        /// <summary>Generated key version used by every registration in this table.</summary>
        public const uint KeyVersion = 1U;

        /// <summary>
        /// Plugin factory key the gate's catalog registers (kind <see cref="FactoryKind.PluginFactory"/>). The
        /// literal is the documented derivation (<see cref="StableNameKeyDerivation"/>) of
        /// <see cref="PluginFactoryStableName"/>; <see cref="DerivationHolds"/> re-checks it against the
        /// production rule, exactly as the GC-001 probe keys are checked (P-004, GC-003 conventions).
        /// </summary>
        public static readonly FactoryKey PluginFactoryKey = new FactoryKey(
            new Id128(0x12D8C2F6119160C5UL, 0x3C6F9F08A3FA5338UL),
            KeyVersion);

        /// <summary>Generated serializer key of <see cref="W1GateKeys.CatalogSchema"/>, derived like the factory key.</summary>
        public static readonly FactoryKey SerializerKey = new FactoryKey(
            new Id128(0x7DDC0714828C7BD9UL, 0x88D897DDABFB12B1UL),
            KeyVersion);

        /// <summary>Declared protocol feature this build supports (P-055).</summary>
        public static readonly Id128[] SupportedFeatureIds =
        {
            new Id128(W1GateKeys.Namespace, 0x00D1UL),
        };

        /// <summary>
        /// Re-derives both literal keys from their stable names with the production rule, so a literal and its
        /// stable name cannot drift apart silently (P-004). The W1 gate records this as its first observation.
        /// </summary>
        public static bool DerivationHolds() =>
            PluginFactoryKey.RegistrationKey.Equals(StableNameKeyDerivation.Derive(PluginFactoryStableName))
            && SerializerKey.RegistrationKey.Equals(StableNameKeyDerivation.Derive(SerializerStableName));

        /// <summary>Owner package identity of every gate registration; default means kernel-owned.</summary>
        private static readonly Id128 OwnerPackage = Id128.Zero;

        private static readonly Id128 PluginImplementation = new Id128(W1GateKeys.Namespace, 0x00E1UL);

        /// <summary>One validated serializer instance per declared schema, in the same order as the table.</summary>
        public static readonly ISchemaSerializer[] Serializers =
        {
            new W1GateRecordSerializer(),
        };

        /// <summary>
        /// Validates this table with the production catalog rules and returns the immutable catalog, or the exact
        /// structured rejections (P-009). Nothing is exposed on rejection.
        /// </summary>
        public static CatalogBuildResult Build()
            => ImmutableCatalog.Build(Factories(), Schemas(), SupportedFeatureIds, Serializers);

        /// <summary>
        /// The fingerprint an emitted file would carry for this table, computed by the same production function the
        /// compiler uses, so a hand-written table can be checked exactly like generated output (P-028).
        /// </summary>
        public static ContentHash Fingerprint()
            => CatalogFingerprint.Compute(Factories(), Schemas(), SupportedFeatureIds);

        /// <summary>
        /// The registration table, in the shape the emitter writes: one entry per registered key, each carrying its
        /// own owner package and precompiled implementation identity, plus the serializer entry of the declared
        /// schema (GC-003 catalog conventions).
        /// </summary>
        public static FactoryRegistration[] Factories() =>
            new[]
            {
                new FactoryRegistration(PluginFactoryKey, FactoryKind.PluginFactory, OwnerPackage, PluginImplementation, 1U),
                new FactoryRegistration(SerializerKey, FactoryKind.Serializer, OwnerPackage, W1GateKeys.CatalogSchema.Id.Value, W1GateKeys.CatalogSchema.Version),
            };

        /// <summary>Accepted schema registrations, in canonical schema-id order.</summary>
        public static SchemaRegistration[] Schemas() =>
            new[]
            {
                new SchemaRegistration(W1GateKeys.CatalogSchema, OwnerPackage, SerializerKey, true),
            };
    }
}

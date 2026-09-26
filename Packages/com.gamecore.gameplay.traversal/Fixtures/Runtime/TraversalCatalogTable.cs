// GameCore.Gameplay.Traversal.Fixtures — the traversal course's hand-written generated-style catalog table (GC-020).
//
// The traversal slice must be resolvable through the *generated* catalog of 04 s8, not only through the gameplay
// package's own direct references, so this file reproduces the table the emitter writes by hand, in exactly that
// shape: one `FactoryKey` constant per entry, a table of direct constructor references, a matching
// `FactoryRegistration[]` table, one schema registration and its serializer, plus the table's fingerprint. A drift
// between the hand-written and the generated conventions is therefore visible rather than hidden.
//
// Every literal key below is the documented derivation (`StableNameKeyDerivation.Derive`): SHA-256 over the UTF-8
// stable name, first 16 digest bytes read as two big-endian 64-bit words (P-004, 05 s3). The stable names are the
// ones the gameplay package's `TraversalKeys` and the rules package's `TraversalVocabulary` declare, so the gameplay
// package, the rule package and a generated catalog cannot disagree about what `traversal.integrate`, the registered
// reducer or the configured schema means. `DerivationHolds` re-derives every literal from its stable name
// (P-004), the way the GC-001 probe keys are checked.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Rules.Traversal;

namespace GameCore.Gameplay.Traversal.Fixtures
{
    /// <summary>Generated-style value of the traversal configuration schema (one required 32-bit field).</summary>
    public readonly struct TraversalConfigValue
    {
        /// <summary>Field id 1, wire type UInt32, required: the configuration document's version.</summary>
        public readonly uint SchemaVersion;

        /// <summary>Builds one configuration value.</summary>
        public TraversalConfigValue(uint schemaVersion)
        {
            SchemaVersion = schemaVersion;
        }

        /// <summary>Diagnostic form; never an identity (P-004).</summary>
        public override string ToString() => "TraversalConfigValue(SchemaVersion=" + SchemaVersion + ")";
    }

    /// <summary>
    /// Generated-style serializer of the traversal configuration schema, derived from the production
    /// <see cref="GeneratedSerializerBase"/> so the header, required-feature gate, declared-field walk and checksum
    /// rules are the shared implementation (05 s6, P-054).
    /// </summary>
    public sealed class TraversalConfigSerializer : GeneratedSerializerBase
    {
        private static readonly GeneratedFieldSlot[] DeclaredFieldSlots =
        {
            new GeneratedFieldSlot(1, WireType.UInt32, true),
        };

        /// <summary>Creates the serializer under its generated key and schema.</summary>
        public TraversalConfigSerializer(FactoryKey key, SchemaRef schema)
            : base(key, schema, TraversalCatalogTable.SupportedFeatureIds)
        {
        }

        /// <summary>Declared fields in ascending field-id order.</summary>
        protected override IReadOnlyList<GeneratedFieldSlot> DeclaredFields => DeclaredFieldSlots;

        /// <summary>Writes one value as a canonical envelope document with a trailing checksum.</summary>
        public byte[] Serialize(TraversalConfigValue value)
        {
            EnvelopeWriter writer = CreateWriter();
            writer.WriteUInt32Field(1, value.SchemaVersion);
            writer.WriteChecksum();
            return writer.ToArray();
        }
    }

    /// <summary>
    /// Generated-style registration table of the traversal course: the plugin factory, the five compiled system keys
    /// of 07 s4.2's stage graph, the registered acceleration reducer, the registered static predicate and the
    /// configuration schema. The one configured schema is accepted in exactly one version and is required, because
    /// every traversal declaration is admitted under it (P-009, P-020).
    /// </summary>
    public static class TraversalCatalogTable
    {
        /// <summary>Stable name of the plugin factory registration.</summary>
        public const string PluginFactoryStableName = "traversal.factory.course-plugin";

        /// <summary>Stable name of the configured traversal schema registration.</summary>
        public const string ConfigSchemaStableName = "traversal.schema.course-config";

        /// <summary>Stable name of the registered `traversal.acceleration` reducer.</summary>
        public const string ReducerStableName = "traversal.reducer.vec3i-sum";

        /// <summary>Stable name of the registered always-accepting predicate.</summary>
        public const string PredicateStableName = "traversal.predicate.always";

        /// <summary>Generated key version every registration in this table uses.</summary>
        public const uint KeyVersion = 1U;

        /// <summary>
        /// The precompiled plugin factory key. The literal is the derivation of
        /// <see cref="PluginFactoryStableName"/>; <see cref="DerivationHolds"/> re-checks it (P-004).
        /// </summary>
        public static readonly FactoryKey PluginFactoryKey =
            new FactoryKey(new Id128(0x6AB4EE28C7914A6DUL, 0x1C321510AA2A39C2UL), KeyVersion);

        /// <summary>Generated key of the `traversal.input` system.</summary>
        public static readonly FactoryKey InputSystemKey =
            new FactoryKey(new Id128(0xB893DD85CA45AEE4UL, 0x77CF897C4154EE95UL), KeyVersion);

        /// <summary>Generated key of the `traversal.integrate` system.</summary>
        public static readonly FactoryKey IntegrateSystemKey =
            new FactoryKey(new Id128(0x0B710434FE439447UL, 0x697BF7843E390FDCUL), KeyVersion);

        /// <summary>Generated key of the `traversal.sense` system.</summary>
        public static readonly FactoryKey SenseSystemKey =
            new FactoryKey(new Id128(0x4B816113044D9926UL, 0x1E5061764CBC5C0BUL), KeyVersion);

        /// <summary>Generated key of the `traversal.checkpoints` system.</summary>
        public static readonly FactoryKey CheckpointSystemKey =
            new FactoryKey(new Id128(0xA8D4EA54F8DAFB72UL, 0x37C88D74316D2478UL), KeyVersion);

        /// <summary>Generated key of the `traversal.output` system.</summary>
        public static readonly FactoryKey OutputSystemKey =
            new FactoryKey(new Id128(0xA7B1FB1A30A57005UL, 0xAB331F36B6F78AE9UL), KeyVersion);

        /// <summary>Generated key of the registered `traversal.reducer.vec3i-sum`.</summary>
        public static readonly FactoryKey ReducerKey =
            new FactoryKey(new Id128(0xAC39BBF7CC19582FUL, 0xFF8FDDC39D1DB6D2UL), KeyVersion);

        /// <summary>Generated key of the registered `traversal.predicate.always`.</summary>
        public static readonly FactoryKey PredicateKey =
            new FactoryKey(new Id128(0x2FB8E2A0A8F97F28UL, 0xF73C805A090FAFF2UL), KeyVersion);

        /// <summary>Generated serializer key of the traversal configuration schema.</summary>
        public static readonly FactoryKey ConfigSerializerKey =
            new FactoryKey(new Id128(0xA84CDEF288CAB0B5UL, 0xDC1283D7CD208E72UL), KeyVersion);

        /// <summary>
        /// The traversal configuration schema the catalog accepts, in the one version the server declares. It is the
        /// same identity as <see cref="ConfigSerializerKey"/>, because a schema registration points at the serializer
        /// key derived from its own stable name (05 s3).
        /// </summary>
        public static readonly SchemaRef ConfigSchema =
            new SchemaRef(new SchemaId(new Id128(0xA84CDEF288CAB0B5UL, 0xDC1283D7CD208E72UL)), 1U);

        /// <summary>
        /// Declared protocol features this build supports. The traversal catalog declares none: the course needs no
        /// optional protocol feature, and declaring one would make this hand-written table disagree with the
        /// generated catalog's own description.
        /// </summary>
        public static readonly Id128[] SupportedFeatureIds = new Id128[0];

        /// <summary>Owner package identity of every traversal registration: the traversal gameplay package.</summary>
        public static readonly Id128 OwnerPackage = TraversalDeclarations.OwnerPackage;

        /// <summary>The registered acceleration reducer instance this build resolves (P-019).</summary>
        public static readonly TraversalAccelerationReducer Reducer = new TraversalAccelerationReducer(ReducerKey);

        /// <summary>The registered static predicate instance this build resolves (P-015).</summary>
        public static readonly TraversalAlwaysPredicate Predicate = new TraversalAlwaysPredicate(PredicateKey);

        /// <summary>
        /// Re-derives every literal key from its stable name with the production rule, so a literal and its stable
        /// name cannot drift apart silently (P-004).
        /// </summary>
        public static bool DerivationHolds()
        {
            return PluginFactoryKey.RegistrationKey.Equals(StableNameKeyDerivation.Derive(PluginFactoryStableName))
                && ReducerKey.RegistrationKey.Equals(StableNameKeyDerivation.Derive(ReducerStableName))
                && PredicateKey.RegistrationKey.Equals(StableNameKeyDerivation.Derive(PredicateStableName))
                && ConfigSerializerKey.RegistrationKey.Equals(StableNameKeyDerivation.Derive(ConfigSchemaStableName))
                && InputSystemKey.RegistrationKey.Equals(StableNameKeyDerivation.Derive("traversal.system.input"))
                && IntegrateSystemKey.RegistrationKey.Equals(StableNameKeyDerivation.Derive("traversal.system.integrate"))
                && SenseSystemKey.RegistrationKey.Equals(StableNameKeyDerivation.Derive("traversal.system.sense"))
                && CheckpointSystemKey.RegistrationKey.Equals(
                    StableNameKeyDerivation.Derive("traversal.system.checkpoints"))
                && OutputSystemKey.RegistrationKey.Equals(StableNameKeyDerivation.Derive("traversal.system.output"));
        }

        /// <summary>One validated serializer instance per declared schema, in the same order as the table.</summary>
        public static ISchemaSerializer[] Serializers() =>
            new ISchemaSerializer[] { new TraversalConfigSerializer(ConfigSerializerKey, ConfigSchema) };

        /// <summary>
        /// Validates this table with the production catalog rules and returns the immutable catalog, or the exact
        /// structured rejections (P-009). Nothing is exposed on rejection.
        /// </summary>
        public static CatalogBuildResult Build() =>
            ImmutableCatalog.Build(Factories(), Schemas(), SupportedFeatureIds, Serializers());

        /// <summary>
        /// The fingerprint an emitted file would carry for this table, computed by the same production function the
        /// compiler uses, so a hand-written table is checked exactly like generated output (P-028).
        /// </summary>
        public static ContentHash Fingerprint() =>
            CatalogFingerprint.Compute(Factories(), Schemas(), SupportedFeatureIds);

        /// <summary>
        /// The registration table in the shape the emitter writes: one entry per registered key, each carrying its
        /// own owner package and precompiled implementation identity, plus the serializer entry of the declared
        /// schema.
        /// </summary>
        public static FactoryRegistration[] Factories() =>
            new[]
            {
                new FactoryRegistration(
                    PluginFactoryKey, FactoryKind.PluginFactory, OwnerPackage,
                    PluginFactoryKey.RegistrationKey, KeyVersion),
                new FactoryRegistration(
                    ReducerKey, FactoryKind.Reducer, OwnerPackage,
                    ReducerKey.RegistrationKey, KeyVersion),
                new FactoryRegistration(
                    PredicateKey, FactoryKind.StaticPredicate, OwnerPackage,
                    PredicateKey.RegistrationKey, KeyVersion),
                new FactoryRegistration(
                    InputSystemKey, FactoryKind.SystemFactory, OwnerPackage,
                    InputSystemKey.RegistrationKey, KeyVersion),
                new FactoryRegistration(
                    IntegrateSystemKey, FactoryKind.SystemFactory, OwnerPackage,
                    IntegrateSystemKey.RegistrationKey, KeyVersion),
                new FactoryRegistration(
                    SenseSystemKey, FactoryKind.SystemFactory, OwnerPackage,
                    SenseSystemKey.RegistrationKey, KeyVersion),
                new FactoryRegistration(
                    CheckpointSystemKey, FactoryKind.SystemFactory, OwnerPackage,
                    CheckpointSystemKey.RegistrationKey, KeyVersion),
                new FactoryRegistration(
                    OutputSystemKey, FactoryKind.SystemFactory, OwnerPackage,
                    OutputSystemKey.RegistrationKey, KeyVersion),
                new FactoryRegistration(
                    ConfigSerializerKey, FactoryKind.Serializer, OwnerPackage,
                    ConfigSchema.Id.Value, ConfigSchema.Version),
            };

        /// <summary>Accepted schema registrations, in canonical schema-id order.</summary>
        public static SchemaRegistration[] Schemas() =>
            new[]
            {
                new SchemaRegistration(ConfigSchema, OwnerPackage, ConfigSerializerKey, true),
            };
    }
}

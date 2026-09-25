// GameCore.Gameplay.Cards.Fixtures — the card market's hand-written generated-style catalog table (GC-011).
//
// The production card catalog is the content compiler's committed output
// (`unity/GameCore.Validation/Catalogs/CardCatalog.catalog.json` -> `GeneratedCards/CardCatalog.g.cs`), which the
// Unity EditMode test and the player probe both run against. This file reproduces the same table by hand, in
// exactly the shape the emitter writes (one `FactoryKey` constant per entry, a table of direct constructor
// references, a matching `FactoryRegistration[]` table, one schema registration and its serializer), so a drift
// between the hand-written and the generated conventions is visible rather than hidden.
//
// Every literal key below is the documented derivation (`StableNameKeyDerivation.Derive`) of the stable name
// beside it, which is the same derivation the gameplay package's `CardTableKeys` and the rules package's
// `CardVocabulary` use. `DerivationHolds` re-checks the literals against that derivation (P-004), the way the
// GC-001 probe keys are checked.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Rules.Cards;

namespace GameCore.Gameplay.Cards.Fixtures
{
    /// <summary>Generated-style value of the card configuration schema (one required 32-bit field).</summary>
    public readonly struct CardConfigValue
    {
        /// <summary>Field id 1, wire type UInt32, required: the configuration document's version.</summary>
        public readonly uint SchemaVersion;

        /// <summary>Builds one configuration value.</summary>
        public CardConfigValue(uint schemaVersion)
        {
            SchemaVersion = schemaVersion;
        }

        /// <summary>Diagnostic form; never an identity (P-004).</summary>
        public override string ToString() => "CardConfigValue(SchemaVersion=" + SchemaVersion + ")";
    }

    /// <summary>
    /// Generated-style serializer of the card configuration schema, derived from the production
    /// <see cref="GeneratedSerializerBase"/> so the header, required-feature gate, declared-field walk and checksum
    /// rules are the shared implementation (05 s6, P-054).
    /// </summary>
    public sealed class CardConfigSerializer : GeneratedSerializerBase
    {
        private static readonly GeneratedFieldSlot[] DeclaredFieldSlots =
        {
            new GeneratedFieldSlot(1, WireType.UInt32, true),
        };

        /// <summary>Creates the serializer under its generated key and schema.</summary>
        public CardConfigSerializer(FactoryKey key, SchemaRef schema)
            : base(key, schema, CardCatalogTable.SupportedFeatureIds)
        {
        }

        /// <summary>Declared fields in ascending field-id order.</summary>
        protected override IReadOnlyList<GeneratedFieldSlot> DeclaredFields => DeclaredFieldSlots;

        /// <summary>Writes one value as a canonical envelope document with a trailing checksum.</summary>
        public byte[] Serialize(CardConfigValue value)
        {
            EnvelopeWriter writer = CreateWriter();
            writer.WriteUInt32Field(1, value.SchemaVersion);
            writer.WriteChecksum();
            return writer.ToArray();
        }
    }

    /// <summary>
    /// Generated-style registration table of the card market: the plugin factory, the four compiled system keys,
    /// the registered reducer, the registered static predicate and the configuration schema.
    /// </summary>
    public static class CardCatalogTable
    {
        /// <summary>Stable name of the plugin factory registration.</summary>
        public const string PluginFactoryStableName = "cards.factory.card-table-plugin";

        /// <summary>Stable name of the configured card schema registration.</summary>
        public const string ConfigSchemaStableName = "cards.schema.card-config";

        /// <summary>Stable name of the registered `cards.set-bonus` reducer.</summary>
        public const string ReducerStableName = "cards.reducer.int32-sum";

        /// <summary>Stable name of the registered always-accepting predicate.</summary>
        public const string PredicateStableName = "cards.predicate.always";

        /// <summary>Generated key version every registration in this table uses.</summary>
        public const uint KeyVersion = 1U;

        /// <summary>
        /// The precompiled plugin factory key. The literal is the derivation of
        /// <see cref="PluginFactoryStableName"/>; <see cref="DerivationHolds"/> re-checks it (P-004).
        /// </summary>
        public static readonly FactoryKey PluginFactoryKey =
            new FactoryKey(new Id128(0xBB026AC0599E6783UL, 0x83F12FFCA82E5B2BUL), KeyVersion);

        /// <summary>Generated key of the `cards.input` system.</summary>
        public static readonly FactoryKey InputSystemKey =
            new FactoryKey(new Id128(0x4522EE4E182F61DCUL, 0xE6E7466B929DEE07UL), KeyVersion);

        /// <summary>Generated key of the `cards.validate` system.</summary>
        public static readonly FactoryKey ValidateSystemKey =
            new FactoryKey(new Id128(0xE113F54073E28D83UL, 0x0803E540BE4EEDFBUL), KeyVersion);

        /// <summary>Generated key of the `cards.commit` system.</summary>
        public static readonly FactoryKey CommitSystemKey =
            new FactoryKey(new Id128(0xA61B22DF091D9AC4UL, 0xC299AE0D0AE30F30UL), KeyVersion);

        /// <summary>Generated key of the `cards.output` system.</summary>
        public static readonly FactoryKey OutputSystemKey =
            new FactoryKey(new Id128(0x2CB9BBFC9BB6CC94UL, 0x95666C211E20EE0FUL), KeyVersion);

        /// <summary>Generated key of the registered `cards.reducer.int32-sum`.</summary>
        public static readonly FactoryKey ReducerKey =
            new FactoryKey(new Id128(0xF85B12F56C1232B2UL, 0x70108056ACB54B19UL), KeyVersion);

        /// <summary>Generated key of the registered `cards.predicate.always`.</summary>
        public static readonly FactoryKey PredicateKey =
            new FactoryKey(new Id128(0xD8CABB8F16C64380UL, 0x102DE6820045DF34UL), KeyVersion);

        /// <summary>Generated serializer key of the card configuration schema.</summary>
        public static readonly FactoryKey ConfigSerializerKey =
            new FactoryKey(new Id128(0x02BB49501222979CUL, 0xFEA308CE5F5F2CA1UL), KeyVersion);

        /// <summary>The card configuration schema the catalog accepts.</summary>
        public static readonly SchemaRef ConfigSchema =
            new SchemaRef(new SchemaId(new Id128(0x02BB49501222979CUL, 0xFEA308CE5F5F2CA1UL)), 1U);

        /// <summary>
        /// Declared protocol features this build supports. The card catalog declares none: the card slice needs no
        /// optional protocol feature, and declaring one would make this hand-written table disagree with the
        /// committed generated catalog's own description.
        /// </summary>
        public static readonly Id128[] SupportedFeatureIds = new Id128[0];

        /// <summary>Owner package identity of every card registration: the card gameplay package.</summary>
        public static readonly Id128 OwnerPackage = CardTableDeclarations.OwnerPackage;

        /// <summary>The registered reducer instance this build resolves (P-019).</summary>
        public static readonly CardSetBonusReducer Reducer = new CardSetBonusReducer(ReducerKey);

        /// <summary>The registered static predicate instance this build resolves (P-015).</summary>
        public static readonly CardAlwaysPredicate Predicate = new CardAlwaysPredicate(PredicateKey);

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
                && InputSystemKey.RegistrationKey.Equals(StableNameKeyDerivation.Derive("cards.system.input"))
                && ValidateSystemKey.RegistrationKey.Equals(StableNameKeyDerivation.Derive("cards.system.validate"))
                && CommitSystemKey.RegistrationKey.Equals(StableNameKeyDerivation.Derive("cards.system.commit"))
                && OutputSystemKey.RegistrationKey.Equals(StableNameKeyDerivation.Derive("cards.system.output"));
        }

        /// <summary>One validated serializer instance per declared schema, in the same order as the table.</summary>
        public static ISchemaSerializer[] Serializers() =>
            new ISchemaSerializer[] { new CardConfigSerializer(ConfigSerializerKey, ConfigSchema) };

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
                    ValidateSystemKey, FactoryKind.SystemFactory, OwnerPackage,
                    ValidateSystemKey.RegistrationKey, KeyVersion),
                new FactoryRegistration(
                    CommitSystemKey, FactoryKind.SystemFactory, OwnerPackage,
                    CommitSystemKey.RegistrationKey, KeyVersion),
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

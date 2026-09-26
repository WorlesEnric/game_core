// GameCore.Gameplay.Rewards — the registration table of the reward installation (GC-024).
//
// Normative sources: 04 s8 ("registration is data: a precompiled factory key resolves a plugin, never reflection")
// and 00 P-009 (a mount whose factory key or configuration schema the catalog does not accept is refused before
// activation), P-028 (the table's fingerprint is computed by the same production function the content compiler
// uses, so a hand-written table is checked exactly like generated output) and P-032 (a version change resolves a
// registered migration handler by key).
//
// This file is in Runtime/ rather than Fixtures/ because the installer needs it: a mount resolves its manifest
// through `GameCore.Unity.Runtime.Integration.CatalogManifestSource`, which refuses a declaration whose
// `FactoryKey` is not registered as a `FactoryKind.PluginFactory` or whose `ConfigSchema` the catalog does not
// accept (CatalogManifestSource.cs:165-198). The table mirrors
// `Packages/com.gamecore.gameplay.cards/Fixtures/Runtime/CardCatalogTable.cs:161-230` — one registration per
// precompiled key, one schema registration with its serializer — with this package's own keys. No separate
// `Fixtures/` assembly is added: this package ships no fixture-only type, so there is nothing a second assembly
// would hold.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Planning;

namespace GameCore.Gameplay.Rewards
{
    /// <summary>
    /// The hand-written generated-style registration table of the reward installation: the plugin factory, the two
    /// stage systems, the registered outbox migration, the configuration schema with its serializer.
    /// </summary>
    public static class RewardsCatalog
    {
        /// <summary>Generated key version every registration in this table uses (05 s3).</summary>
        public const uint KeyVersion = 1U;

        /// <summary>Stable name of the configuration schema's serializer registration.</summary>
        public const string ConfigSerializerStableName = "gamecore.rewards.serializer.config";

        /// <summary>
        /// Declared protocol features this build supports. The reward installation declares none: it needs no
        /// optional protocol feature, and declaring one would claim a capability the shipped kernel does not have.
        /// </summary>
        public static readonly Id128[] SupportedFeatureIds = new Id128[0];

        /// <summary>Generated serializer key of the configuration schema (05 s6).</summary>
        public static readonly FactoryKey ConfigSerializerKey = RewardsKeys.Key(ConfigSerializerStableName);

        /// <summary>One validated serializer instance per declared schema, in the same order as the table.</summary>
        public static ISchemaSerializer[] Serializers() =>
            new ISchemaSerializer[] { new RewardsConfigSerializer(ConfigSerializerKey, RewardsKeys.ConfigSchema) };

        /// <summary>
        /// Validates this table with the production catalog rules and returns the immutable catalog, or the exact
        /// structured rejections (P-009). Nothing is exposed on rejection.
        /// </summary>
        public static CatalogBuildResult Build() =>
            ImmutableCatalog.Build(Factories(), Schemas(), SupportedFeatureIds, Serializers());

        /// <summary>
        /// The fingerprint an emitted file would carry for this table, computed by the same production function the
        /// compiler uses (P-028).
        /// </summary>
        public static ContentHash Fingerprint() =>
            CatalogFingerprint.Compute(Factories(), Schemas(), SupportedFeatureIds);

        /// <summary>
        /// The registered migration handlers of this revision. A `StatePolicyCatalog` is built from these (its
        /// `Build(manifests, migrations, initialValues)`), and a handler that is not registered there is a
        /// `MigrationRequired` refusal rather than an implicit reset (P-032). Each call returns fresh instances, so
        /// a caller that wants the counters of the instance its pass used registers
        /// <see cref="RewardsInstallation.OutboxMigration"/> instead.
        /// </summary>
        public static IReadOnlyList<ISlotMigration> Migrations() =>
            new List<ISlotMigration> { RewardsOutboxSlot.RegisteredMigration() };

        /// <summary>
        /// The registration table in the shape the emitter writes: one entry per registered key, each carrying its
        /// own owner package and precompiled implementation identity, plus the serializer entry of the declared
        /// schema.
        /// </summary>
        public static FactoryRegistration[] Factories() =>
            new[]
            {
                new FactoryRegistration(
                    RewardsKeys.PluginFactory, FactoryKind.PluginFactory, RewardsKeys.OwnerPackage,
                    RewardsKeys.PluginFactory.RegistrationKey, KeyVersion),
                new FactoryRegistration(
                    RewardsKeys.EnqueueSystem, FactoryKind.SystemFactory, RewardsKeys.OwnerPackage,
                    RewardsKeys.EnqueueSystem.RegistrationKey, KeyVersion),
                new FactoryRegistration(
                    RewardsKeys.AckSystem, FactoryKind.SystemFactory, RewardsKeys.OwnerPackage,
                    RewardsKeys.AckSystem.RegistrationKey, KeyVersion),
                new FactoryRegistration(
                    RewardsKeys.OutboxMigration, FactoryKind.Migration, RewardsKeys.OwnerPackage,
                    RewardsKeys.OutboxMigration.RegistrationKey, KeyVersion),
                new FactoryRegistration(
                    ConfigSerializerKey, FactoryKind.Serializer, RewardsKeys.OwnerPackage,
                    RewardsKeys.ConfigSchema.Id.Value, RewardsKeys.ConfigSchema.Version),
            };

        /// <summary>Accepted schema registrations, in canonical schema-id order.</summary>
        public static SchemaRegistration[] Schemas() =>
            new[]
            {
                new SchemaRegistration(
                    RewardsKeys.ConfigSchema, RewardsKeys.OwnerPackage, ConfigSerializerKey, true),
            };
    }
}

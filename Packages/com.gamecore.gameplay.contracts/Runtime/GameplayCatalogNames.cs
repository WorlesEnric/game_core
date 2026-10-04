// GameCore.Gameplay.Contracts - the stable names of every generated gameplay catalog registration (P-009).
//
// The runtime declarations (manifests, slot specs, recipes) and the bake's catalog description both read these names,
// so the key a manifest asks for and the key the content compiler emits are derived from one string: the gameplay
// catalog key of name N is StableNameKeyDerivation.Derive("gameplay." + N) at key version 1 (GameplayIds.Key).
// P1.5 appended the UI and audio plugins (catalog rows 10-11) after the P1.1 entries; appending keeps every earlier
// registration's key and position.
#nullable enable
using System.Collections.Generic;

namespace GameCore.Gameplay.Contracts
{
    /// <summary>Stable names (without the <c>gameplay.</c> prefix) of the generated gameplay catalog.</summary>
    public static class GameplayCatalogNames
    {
        public const string EntitiesPackage = "package.entities";
        public const string WorldPackage = "package.world";
        public const string UiPackage = "package.ui";
        public const string AudioPackage = "package.audio";

        public const string EntitiesPlugin = "entities.plugin";
        public const string WorldPlugin = "world.plugin";
        public const string UiPlugin = "ui.plugin";
        public const string AudioPlugin = "audio.plugin";

        public const string EntitiesCommandSystem = "entities.system.command";
        public const string WorldCommandSystem = "world.system.command";
        public const string UiCommandSystem = "ui.system.command";
        public const string AudioCommandSystem = "audio.system.command";

        public const string EntityApplier = "entities.applier";
        public const string RegionApplier = "world.region-applier";
        public const string UiSessionApplier = "ui.session-applier";
        public const string AudioSessionApplier = "audio.session-applier";

        public const string EntityLayout = "entities.layout.entity";
        public const string RegionLayout = "world.layout.region";
        public const string PlacementLayout = "world.layout.placement";
        public const string UiSessionLayout = "ui.layout.session";
        public const string AudioSessionLayout = "audio.layout.session";

        public const string EntitiesConfigSchema = "entities.schema.config";
        public const string WorldConfigSchema = "world.schema.config";
        public const string EntityDomainSchema = "entities.domain.entity";
        public const string RegionDomainSchema = "world.domain.region";
        public const string PlacementDomainSchema = "world.domain.placement";
        public const string UiConfigSchema = "ui.schema.config";
        public const string UiSessionDomainSchema = "ui.domain.session";
        public const string AudioConfigSchema = "audio.schema.config";
        public const string AudioSessionDomainSchema = "audio.domain.session";

        /// <summary>Prefix of a definition's recipe registration: <c>recipe.&lt;authoring id&gt;</c>.</summary>
        public const string DefinitionRecipePrefix = "recipe.";

        /// <summary>One catalog schema: its schema name, serializer name and generated C# names.</summary>
        public sealed class SchemaName
        {
            public SchemaName(string schema, string serializer, string typeStem, string ownerPackage)
            {
                Schema = schema;
                Serializer = serializer;
                TypeStem = typeStem;
                OwnerPackage = ownerPackage;
            }

            public string Schema { get; }

            public string Serializer { get; }

            /// <summary>Stem of the generated value/serializer/key names (e.g. <c>EntitiesConfig</c>).</summary>
            public string TypeStem { get; }

            public string OwnerPackage { get; }
        }

        /// <summary>One static catalog registration: group, stable name, generated key name and owner package.</summary>
        public sealed class EntryName
        {
            public EntryName(string group, string name, string keyName, string ownerPackage)
            {
                Group = group;
                Name = name;
                KeyName = keyName;
                OwnerPackage = ownerPackage;
            }

            /// <summary>One of <c>PluginFactory</c>, <c>SystemFactory</c>, <c>LayoutApply</c>.</summary>
            public string Group { get; }

            public string Name { get; }

            public string KeyName { get; }

            public string OwnerPackage { get; }
        }

        /// <summary>Every schema of the gameplay catalog (one UInt32 SchemaVersion field each), in canonical order.</summary>
        public static IReadOnlyList<SchemaName> Schemas { get; } = System.Array.AsReadOnly(new[]
        {
            new SchemaName(EntitiesConfigSchema, "entities.serializer.config", "EntitiesConfig", EntitiesPackage),
            new SchemaName(EntityDomainSchema, "entities.serializer.domain-entity", "EntityDomain", EntitiesPackage),
            new SchemaName(WorldConfigSchema, "world.serializer.config", "WorldConfig", WorldPackage),
            new SchemaName(RegionDomainSchema, "world.serializer.domain-region", "RegionDomain", WorldPackage),
            new SchemaName(PlacementDomainSchema, "world.serializer.domain-placement", "PlacementDomain", WorldPackage),
            new SchemaName(UiConfigSchema, "ui.serializer.config", "UiConfig", UiPackage),
            new SchemaName(UiSessionDomainSchema, "ui.serializer.domain-session", "UiSessionDomain", UiPackage),
            new SchemaName(AudioConfigSchema, "audio.serializer.config", "AudioConfig", AudioPackage),
            new SchemaName(AudioSessionDomainSchema, "audio.serializer.domain-session", "AudioSessionDomain", AudioPackage),
        });

        /// <summary>Every static registration of the gameplay catalog, in canonical order.</summary>
        public static IReadOnlyList<EntryName> StaticEntries { get; } = System.Array.AsReadOnly(new[]
        {
            new EntryName("PluginFactory", EntitiesPlugin, "EntitiesPluginKey", EntitiesPackage),
            new EntryName("PluginFactory", WorldPlugin, "WorldPluginKey", WorldPackage),
            new EntryName("SystemFactory", EntitiesCommandSystem, "EntitiesCommandSystemKey", EntitiesPackage),
            new EntryName("SystemFactory", WorldCommandSystem, "WorldCommandSystemKey", WorldPackage),
            new EntryName("LayoutApply", EntityApplier, "EntityApplierKey", EntitiesPackage),
            new EntryName("LayoutApply", RegionApplier, "RegionApplierKey", WorldPackage),
            new EntryName("LayoutApply", EntityLayout, "EntityLayoutKey", EntitiesPackage),
            new EntryName("LayoutApply", RegionLayout, "RegionLayoutKey", WorldPackage),
            new EntryName("LayoutApply", PlacementLayout, "PlacementLayoutKey", WorldPackage),
            new EntryName("PluginFactory", UiPlugin, "UiPluginKey", UiPackage),
            new EntryName("PluginFactory", AudioPlugin, "AudioPluginKey", AudioPackage),
            new EntryName("SystemFactory", UiCommandSystem, "UiCommandSystemKey", UiPackage),
            new EntryName("SystemFactory", AudioCommandSystem, "AudioCommandSystemKey", AudioPackage),
            new EntryName("LayoutApply", UiSessionApplier, "UiSessionApplierKey", UiPackage),
            new EntryName("LayoutApply", AudioSessionApplier, "AudioSessionApplierKey", AudioPackage),
            new EntryName("LayoutApply", UiSessionLayout, "UiSessionLayoutKey", UiPackage),
            new EntryName("LayoutApply", AudioSessionLayout, "AudioSessionLayoutKey", AudioPackage),
        });
    }
}

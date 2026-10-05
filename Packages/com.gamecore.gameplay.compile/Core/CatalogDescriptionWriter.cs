// GameCore.Gameplay.Compile - the gameplay catalog description (gamecore.catalog-description/1, GC-003).
//
// The description is the content compiler's input. It always declares the static gameplay registrations
// (GameplayCatalogNames: the two plugin factories, the two command systems, the recipe appliers, the slot layouts and
// the five one-field schemas) and, per baked entity definition, one LayoutApply registration
// `gameplay.recipe.<authoring id>` whose implementation id is the first sixteen bytes of the definition's content hash.
// A definition edit therefore changes exactly one registration and the catalog fingerprint, which is how a running
// game detects a stale catalog (GP-CMP-004), and adding a definition adds exactly one registration.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;

namespace GameCore.Gameplay.Compile
{
    /// <summary>Generated C# names of one catalog.</summary>
    public sealed class CatalogNaming
    {
        public CatalogNaming(string @namespace, string className)
        {
            if (string.IsNullOrEmpty(@namespace) || string.IsNullOrEmpty(className))
            {
                throw new ArgumentException("A catalog needs a namespace and a class name.");
            }

            Namespace = @namespace;
            ClassName = className;
        }

        public string Namespace { get; }

        public string ClassName { get; }

        public string FileName => ClassName + ".g.cs";
    }

    /// <summary>Writes the catalog description of one baked world.</summary>
    public static class CatalogDescriptionWriter
    {
        public const string FormatId = "gamecore.catalog-description/1";

        public const string InterfaceType = "GameCore.Gameplay.Contracts.IGameplayCatalogEntry";

        public const string ImplementationType = "GameplayCatalogEntry";

        /// <summary>The C# key name of a definition's recipe registration.</summary>
        public static string RecipeKeyName(string definitionId) => "Recipe" + definitionId.Replace("-", string.Empty);

        /// <summary>The stable name (without prefix) of a definition's recipe registration.</summary>
        public static string RecipeName(string definitionId) => GameplayCatalogNames.DefinitionRecipePrefix + definitionId;

        public static string Write(BakedWorld world, CatalogNaming naming) =>
            Write(world, naming, Array.Empty<GameplayCatalogNames.SchemaName>(), Array.Empty<GameplayCatalogNames.EntryName>());

        /// <summary>
        /// The description with extra registrations of plugin packages (GameplayBakeExtensions, P1.4): extra schemas
        /// follow the static ones, extra entries follow the static ones of their group. With none it is byte-identical
        /// to <see cref="Write(BakedWorld, CatalogNaming)"/>.
        /// </summary>
        public static string Write(
            BakedWorld world,
            CatalogNaming naming,
            IReadOnlyList<GameplayCatalogNames.SchemaName> extraSchemas,
            IReadOnlyList<GameplayCatalogNames.EntryName> extraEntries)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (naming == null)
            {
                throw new ArgumentNullException(nameof(naming));
            }

            var json = new CanonicalJson();
            json.BeginObject();
            json.String("descriptionFormat", FormatId);
            json.String("protocolVersion", "1.0");
            json.String("namespace", naming.Namespace);
            json.String("className", naming.ClassName);
            json.String("fileName", naming.FileName);
            json.EmptyArray("supportedFeatureIds");

            json.BeginArray("schemas");
            var schemas = new List<GameplayCatalogNames.SchemaName>(GameplayCatalogNames.Schemas);
            if (extraSchemas != null)
            {
                schemas.AddRange(extraSchemas);
            }

            var entries = new List<GameplayCatalogNames.EntryName>(GameplayCatalogNames.StaticEntries);
            if (extraEntries != null)
            {
                entries.AddRange(extraEntries);
            }

            for (int i = 0; i < schemas.Count; i++)
            {
                GameplayCatalogNames.SchemaName schema = schemas[i];
                json.BeginObject();
                json.String("stableName", GameplayIds.StableName(schema.Serializer));
                json.String("valueTypeName", schema.TypeStem + "Value");
                json.String("serializerTypeName", schema.TypeStem + "Serializer");
                json.String("serializerKeyName", schema.TypeStem + "SerializerKey");
                json.String("schemaId", GameplayIds.Hex(GameplayIds.Id(schema.Schema)));
                json.Number("schemaVersion", 1L);
                json.Number("serializerKeyVersion", 1L);
                json.String("ownerPackageId", GameplayIds.Hex(GameplayIds.Id(schema.OwnerPackage)));
                json.Bool("required", true);
                json.BeginArray("fields");
                json.BeginObject();
                json.Number("id", 1L);
                json.String("name", "SchemaVersion");
                json.String("wireType", "UInt32");
                json.Bool("required", true);
                json.EndObject();
                json.EndArray();
                json.EndObject();
            }

            json.EndArray();

            json.BeginArray("groups");
            WriteStaticGroup(json, entries, "PluginFactory", "PluginRegistrations", "PluginKeys", "TryGetPlugin");
            WriteStaticGroup(json, entries, "SystemFactory", "SystemRegistrations", "SystemKeys", "TryGetSystem");
            WriteStaticGroup(json, entries, "LayoutApply", "LayoutRegistrations", "LayoutKeys", "TryGetLayout");
            if (world.Definitions.Count > 0)
            {
                WriteRecipeGroup(json, world);
            }

            json.EndArray();

            json.BeginObject("code");
            json.BeginArray("usingDirectives");
            json.StringItem("GameCore.Gameplay.Contracts");
            json.EndArray();
            json.EndObject();
            json.EndObject();
            return json.ToString();
        }

        private static void WriteGroupHeader(CanonicalJson json, string kind, string table, string keys, string lookup)
        {
            json.BeginObject();
            json.String("tableName", table);
            json.String("keysName", keys);
            json.String("lookupMethodName", lookup);
            json.String("interfaceType", InterfaceType);
            json.String("kind", kind);
            json.BeginArray("entries");
        }

        private static void WriteStaticGroup(CanonicalJson json, IReadOnlyList<GameplayCatalogNames.EntryName> entries, string kind, string table, string keys, string lookup)
        {
            WriteGroupHeader(json, kind, table, keys, lookup);
            for (int i = 0; i < entries.Count; i++)
            {
                GameplayCatalogNames.EntryName entry = entries[i];
                if (!string.Equals(entry.Group, kind, StringComparison.Ordinal))
                {
                    continue;
                }

                WriteEntry(
                    json,
                    GameplayIds.StableName(entry.Name),
                    entry.KeyName,
                    GameplayIds.Hex(GameplayIds.Id(entry.OwnerPackage)),
                    GameplayIds.Hex(GameplayIds.Id("impl." + entry.Name)));
            }

            json.EndArray();
            json.EndObject();
        }

        private static void WriteRecipeGroup(CanonicalJson json, BakedWorld world)
        {
            WriteGroupHeader(json, "LayoutApply", "RecipeRegistrations", "RecipeKeys", "TryGetRecipe");
            var definitions = new List<BakedDefinition>(world.Definitions);
            definitions.Sort((l, r) => string.CompareOrdinal(l.AuthoringId, r.AuthoringId));
            string owner = GameplayIds.Hex(GameplayIds.Id(GameplayCatalogNames.EntitiesPackage));
            for (int i = 0; i < definitions.Count; i++)
            {
                BakedDefinition definition = definitions[i];
                WriteEntry(
                    json,
                    GameplayIds.StableName(RecipeName(definition.AuthoringId)),
                    RecipeKeyName(definition.AuthoringId),
                    owner,
                    DefinitionHashing.ImplementationIdOf(definition.ContentHash));
            }

            json.EndArray();
            json.EndObject();
        }

        private static void WriteEntry(CanonicalJson json, string stableName, string keyName, string owner, string implementation)
        {
            json.BeginObject();
            json.String("stableName", stableName);
            json.String("keyName", keyName);
            json.Number("keyVersion", 1L);
            json.String("ownerPackageId", owner);
            json.String("implementationId", implementation);
            json.String("implementationExpression", "new " + ImplementationType + "(" + keyName + ")");
            json.EndObject();
        }
    }
}

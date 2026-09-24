// GameCore.Unity.Runtime — integration seam between the generated catalog (GC-003) and the mount path of the
// control lane (GC-004).
//
// The control lane resolves a mount's `PluginManifest` and its configuration-schema defaults through
// `GameCore.Composition.IPluginManifestSource` (`CompositionState.cs`); in production that data comes from the
// generated catalog, never from reflection or a later discovery step (P-009). This class is that bridge: it
// accepts generated-style declarations, refuses every declaration whose precompiled factory or configuration
// schema the built catalog does not register, and reports a miss as a value.
//
// Nothing here is discovered at runtime: the declaration table is generated-style application input (the same
// shape `GameCore.Unity.Fixtures.FixtureKeys` uses), and the catalog is the validated immutable catalog built by
// generated code. The W1 integration gate is the first consumer; GC-006+ mount the same way.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;

namespace GameCore.Unity.Runtime.Integration
{
    /// <summary>Why one generated-style declaration was refused while the manifest source was constructed.</summary>
    public readonly struct CatalogDeclarationRejection
    {
        public CatalogDeclarationRejection(
            PluginTypeId pluginType,
            FactoryKey factoryKey,
            DiagnosticCode code,
            string detail)
        {
            PluginType = pluginType;
            FactoryKey = factoryKey;
            Code = code;
            Detail = detail ?? string.Empty;
        }

        /// <summary>The declared plugin type; a default value means the declaration named no real identity.</summary>
        public PluginTypeId PluginType { get; }

        public FactoryKey FactoryKey { get; }

        /// <summary>The catalog's own rejection code, so a refusal is never a bespoke one (P-009).</summary>
        public DiagnosticCode Code { get; }

        public string Detail { get; }

        public override string ToString() =>
            PluginType.ToString() + " -> " + DiagnosticCodeText.Of(Code) + ": " + Detail;
    }

    /// <summary>
    /// One generated-style plugin declaration: the manifest a mount resolves and the configuration-schema
    /// defaults the manifest's declared schema contributes as the lowest configuration layer (P-020).
    /// </summary>
    public readonly struct CatalogPluginDeclaration
    {
        public CatalogPluginDeclaration(PluginManifest manifest, ConfigDocument? schemaDefaults)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            SchemaDefaults = schemaDefaults ?? ConfigDocument.Empty;
        }

        public PluginManifest Manifest { get; }

        /// <summary>Declared schema defaults; an empty document means the schema declares no default fields.</summary>
        public ConfigDocument SchemaDefaults { get; }
    }

    /// <summary>
    /// Catalog-backed <see cref="IPluginManifestSource"/>: every accepted declaration is validated against the
    /// built catalog before it can be mounted, and every miss is counted instead of substituted (P-009).
    /// </summary>
    public sealed class CatalogManifestSource : IPluginManifestSource
    {
        private readonly Dictionary<Id128, CatalogPluginDeclaration> byPluginType =
            new Dictionary<Id128, CatalogPluginDeclaration>();

        private readonly Dictionary<Id128, CatalogPluginDeclaration> byConfigSchema =
            new Dictionary<Id128, CatalogPluginDeclaration>();

        private readonly IReadOnlyList<CatalogDeclarationRejection> rejected;

        public CatalogManifestSource(ICatalog catalog, IReadOnlyList<CatalogPluginDeclaration>? declarations)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

            List<CatalogDeclarationRejection> refusals = new List<CatalogDeclarationRejection>();
            if (declarations != null)
            {
                for (int i = 0; i < declarations.Count; i++)
                {
                    Admit(declarations[i], refusals);
                }
            }

            rejected = Array.AsReadOnly(refusals.ToArray());
        }

        /// <summary>The validated production catalog every declaration was checked against.</summary>
        public ICatalog Catalog { get; }

        /// <summary>Declarations the catalog accepted; these are the only ones a mount can resolve.</summary>
        public int AcceptedCount { get; private set; }

        /// <summary>Declarations refused at construction, with the catalog's own rejection code.</summary>
        public IReadOnlyList<CatalogDeclarationRejection> Rejected => rejected;

        /// <summary>Mount resolutions that found no accepted declaration for the requested plugin type.</summary>
        public int ManifestMissCount { get; private set; }

        /// <summary>Configuration-default resolutions that found no accepted declaration for the schema.</summary>
        public int ConfigDefaultsMissCount { get; private set; }

        /// <inheritdoc />
        public bool TryGetManifest(PluginTypeId pluginType, out PluginManifest? manifest)
        {
            if (byPluginType.TryGetValue(pluginType.Value, out CatalogPluginDeclaration declaration))
            {
                manifest = declaration.Manifest;
                return true;
            }

            ManifestMissCount++;
            manifest = null;
            return false;
        }

        /// <inheritdoc />
        public bool TryGetConfigDefaults(SchemaRef schema, out ConfigDocument? defaults)
        {
            if (byConfigSchema.TryGetValue(schema.Id.Value, out CatalogPluginDeclaration declaration))
            {
                defaults = declaration.SchemaDefaults;
                return true;
            }

            ConfigDefaultsMissCount++;
            defaults = null;
            return false;
        }

        private void Admit(CatalogPluginDeclaration declaration, List<CatalogDeclarationRejection> refusals)
        {
            PluginManifest manifest = declaration.Manifest;

            if (manifest.PluginTypeId.IsDefault)
            {
                refusals.Add(new CatalogDeclarationRejection(
                    manifest.PluginTypeId,
                    manifest.FactoryKey,
                    DiagnosticCode.MissingDependency,
                    "a default zero plugin type is not a stable identity (P-004)"));
                return;
            }

            if (byPluginType.ContainsKey(manifest.PluginTypeId.Value))
            {
                refusals.Add(new CatalogDeclarationRejection(
                    manifest.PluginTypeId,
                    manifest.FactoryKey,
                    DiagnosticCode.OwnershipConflict,
                    "one plugin type is declared twice in the same registration table"));
                return;
            }

            CatalogLookup factory = Catalog.Lookup(manifest.FactoryKey);
            if (!factory.Found || factory.Factory == null)
            {
                // P-009: a mount whose precompiled factory the catalog does not register must reject before
                // activation, never be resolved through reflection or a convenient default.
                refusals.Add(new CatalogDeclarationRejection(
                    manifest.PluginTypeId,
                    manifest.FactoryKey,
                    factory.Code,
                    "the catalog registers no factory for " + manifest.FactoryKey));
                return;
            }

            if (factory.Factory.Kind != FactoryKind.PluginFactory)
            {
                refusals.Add(new CatalogDeclarationRejection(
                    manifest.PluginTypeId,
                    manifest.FactoryKey,
                    DiagnosticCode.OwnershipConflict,
                    "the catalog key " + manifest.FactoryKey + " is registered as "
                    + factory.Factory.Kind + ", not a PluginFactory"));
                return;
            }

            CatalogLookup schema = Catalog.LookupSchema(manifest.ConfigSchema);
            if (!schema.Found)
            {
                refusals.Add(new CatalogDeclarationRejection(
                    manifest.PluginTypeId,
                    manifest.FactoryKey,
                    schema.Code,
                    "the catalog does not accept configuration schema " + manifest.ConfigSchema));
                return;
            }

            byPluginType[manifest.PluginTypeId.Value] = declaration;
            byConfigSchema[manifest.ConfigSchema.Id.Value] = declaration;
            AcceptedCount++;
        }
    }
}

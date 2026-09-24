// GameCore.Unity.Fixtures — W1 integration gate fixture: stable identities, generated-style declarations and
// edit payloads for the scenario that joins the generated catalog (GC-003), the control lane (GC-004) and the
// owned Unity world (GC-005).
//
// Like `FixtureKeys`/`FixtureRegistration`, everything here is hand-written "generated style": stable key
// literals plus direct typed references, with no reflection and no runtime discovery (04 s8). The fixture is a
// qualification fixture, not a supported gameplay assembly (GC-005 handoff, item 9).
#nullable enable
using System;
using GameCore.Composition;
using GameCore.Contracts;

namespace GameCore.Unity.Fixtures
{
    /// <summary>Stable identities of the W1 gate fixture, in their own namespace word.</summary>
    public static class W1GateKeys
    {
        /// <summary>Namespace word of every gate key, so a fixture key can never collide with a package key.</summary>
        public const ulong Namespace = 0x4731474154455731UL;

        /// <summary>Plugin type the generated catalog has a PluginFactory registration for.</summary>
        public static readonly PluginTypeId PluginType = new PluginTypeId(new Id128(Namespace, 0x0001UL));

        /// <summary>Plugin type no catalog build registers; the gate's catalog-miss case.</summary>
        public static readonly PluginTypeId AbsentPluginType = new PluginTypeId(new Id128(Namespace, 0x0002UL));

        /// <summary>A precompiled factory key no catalog build registers (P-009 miss, never substituted).</summary>
        public static readonly FactoryKey AbsentPluginFactory = new FactoryKey(new Id128(Namespace, 0x00A1UL), 1U);

        /// <summary>
        /// The configuration schema the committed generated probe catalog registers
        /// (`gamecore.validation.schema.probe-record`, version 1). A manifest mounted by the gate declares it, so
        /// the catalog validates both the factory key and the configuration schema of the declaration.
        /// </summary>
        public static readonly SchemaRef CatalogSchema = new SchemaRef(
            new SchemaId(new Id128(0x4BF5B435956D00ADUL, 0x605292D344334459UL)),
            1U);

        /// <summary>Stable installation identity of one gate mount.</summary>
        public static PluginInstanceId Instance(ulong ordinal) =>
            new PluginInstanceId(new Id128(Namespace, 0x0100UL + ordinal));

        /// <summary>Root scope identity of one gate world.</summary>
        public static ScopeId RootScope(ulong ordinal) => new ScopeId(new Id128(Namespace, 0x0200UL + ordinal));

        /// <summary>Stable issuer identity of this fixture's control-lane operations (P-050).</summary>
        public static readonly Id128 Issuer = new Id128(Namespace, 0x0300UL);
    }

    /// <summary>Generated-style manifest declarations: the shape a build-time generator emits, written by hand.</summary>
    public static class W1GateManifests
    {
        /// <summary>
        /// A manifest with one identity, one configuration schema and one precompiled factory key, and no service,
        /// capability, derivation, stage, buffer or resource declaration: every empty array means "none", never
        /// "discover later" (P-009).
        /// </summary>
        public static PluginManifest Plain(PluginTypeId pluginType, FactoryKey factoryKey, SchemaRef configSchema)
        {
            return new PluginManifest(
                pluginType,
                "1.0.0",
                ContentHash.Empty,
                new SupportedProtocolRange(1, 0, 0),
                null,
                configSchema,
                factoryKey,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        }
    }

    /// <summary>Edit payload builders for the gate, carrying the configuration hashes the applier re-derives.</summary>
    public static class W1GatePayloads
    {
        /// <summary>
        /// O-03 mount of one plugin instance at one scope. The declared configuration hash is the canonical hash of
        /// the effective configuration (schema defaults over the local patch), exactly as
        /// `CompositionEditApplier.ValidateConfigDocument` recomputes it (P-020).
        /// </summary>
        public static CompositionEditPayload Mount(
            PluginManifest manifest,
            PluginInstanceId instance,
            ScopeId scope,
            ConfigDocument? schemaDefaults)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            ConfigDocument local = ConfigDocument.Empty;
            ConfigDocument effective = Effective(manifest, instance, schemaDefaults, local);

            return new CompositionEditPayload(
                CompositionEditSubject.InstallMount,
                scope,
                default(ScopeId),
                false,
                null,
                null,
                null,
                null,
                manifest.PluginTypeId,
                instance,
                DefinitionRevision.First,
                ConfigDocumentCodec.HashOf(effective),
                local,
                0,
                null,
                PropagationMode.Automatic);
        }

        /// <summary>The effective configuration a mount publishes: schema defaults, then the local declaration.</summary>
        private static ConfigDocument Effective(
            PluginManifest manifest,
            PluginInstanceId instance,
            ConfigDocument? schemaDefaults,
            ConfigDocument local)
        {
            ConfigComposeResult composed = ConfigComposer.Compose(new[]
            {
                new ConfigLayer(ConfigLayerOrigin.SchemaDefaults, manifest.ConfigSchema.Id.Value, schemaDefaults ?? ConfigDocument.Empty),
                new ConfigLayer(ConfigLayerOrigin.LocalPatch, instance.Value, local),
            });

            return composed.Value;
        }
    }
}

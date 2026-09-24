// GameCore.Composition tests — manifest builders for the resolver and lifecycle fixtures.
//
// These helpers declare only what a test needs, and every declaration array that is left empty means "none"
// rather than "discover later" (P-009).
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Composition.Tests
{
    /// <summary>Builders for manifests, service declarations and state slots used by the composition tests.</summary>
    public static class Manifests
    {
        /// <summary>A manifest with no declarations beyond its identity and configuration schema.</summary>
        public static PluginManifest Plain(
            PluginTypeId type,
            IdFactory ids,
            SchemaId? configSchema = null,
            IReadOnlyList<ServiceExport>? exports = null,
            IReadOnlyList<ServiceDependency>? dependencies = null,
            IReadOnlyList<StateSlotSpec>? slots = null,
            IReadOnlyList<DerivationRule>? rules = null)
        {
            SchemaId schema = configSchema ?? ids.Schema();
            return new PluginManifest(
                type,
                "1.0.0",
                new ContentHash(new byte[ContentHash.SizeInBytes]),
                new SupportedProtocolRange(1, 0, 0),
                null,
                new SchemaRef(schema, 1U),
                ids.Factory(),
                exports,
                dependencies,
                null,
                rules,
                null,
                slots,
                null,
                null,
                null);
        }

        /// <summary>One service export declaration (P-011).</summary>
        public static ServiceExport Export(
            ContractRef contract,
            Id128 factoryKey,
            ServiceVisibility visibility = ServiceVisibility.Private,
            bool overridesAncestor = false,
            ServiceBindingKind bindingKind = ServiceBindingKind.Single) =>
            new ServiceExport(contract, new FactoryKey(factoryKey, 1U), visibility, overridesAncestor, bindingKind);

        /// <summary>One service dependency declaration; the version range accepts the declared contract version.</summary>
        public static ServiceDependency Requires(
            ContractRef contract,
            bool isRequired = true,
            ServiceResolutionDomain domain = ServiceResolutionDomain.AncestorsAndSelf,
            ProviderInstallationId selectedProvider = default(ProviderInstallationId),
            Id128 fallbackKey = default(Id128)) =>
            new ServiceDependency(
                contract,
                new VersionRange(contract.Version, contract.Version),
                isRequired,
                domain,
                selectedProvider,
                new FactoryKey(fallbackKey, 1U));

        /// <summary>One state slot with an explicit last-support policy (P-032).</summary>
        public static StateSlotSpec Slot(
            SlotId slot,
            OwnerId owner,
            SchemaRef schema,
            LastSupportPolicy lastSupport,
            Id128 transferPolicy = default(Id128),
            Id128 migrationKey = default(Id128)) =>
            new StateSlotSpec(
                slot,
                owner,
                schema,
                new FactoryKey(new Id128(0x6C61796F7574UL, 1UL), 1U),
                null,
                new FactoryKey(new Id128(0x696E6974UL, 1UL), 1U),
                new FactoryKey(new Id128(0x636F6E666967UL, 1UL), 1U),
                new FactoryKey(new Id128(0x76657273696F6EUL, 1UL), 1U),
                lastSupport,
                new FactoryKey(transferPolicy, 1U),
                migrationKey.IsDefault ? null : new[] { new FactoryKey(migrationKey, 1U) });
    }
}

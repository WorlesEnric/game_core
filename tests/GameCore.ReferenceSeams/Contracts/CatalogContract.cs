// Test-only reference seam for the shared GameCore.Contracts surface (TestOnlyMarker.cs).
// Catalog lookup contract from docs/game-core/05-contracts-and-data-model.md s3 and P-009: a registered key is
// resolved by explicit lookup that reports not-found or an unsupported version. No reflection-driven
// discovery, no ambient fallback and no convenient default (P-009, P-015).
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.Contracts
{
    /// <summary>Kind of generated registration a <see cref="FactoryKey"/> names (P-009).</summary>
    public enum FactoryKind
    {
        PluginFactory = 0,
        SystemFactory = 1,
        Reducer = 2,
        StaticPredicate = 3,
        Serializer = 4,
        Migration = 5,
        ResourceFactory = 6,
        LayoutApply = 7,
        SchemaFactory = 8,
        StatePolicy = 9,
    }

    /// <summary>One registered generated key. Nothing here is instantiated or invoked by the seam.</summary>
    public sealed class FactoryRegistration
    {
        public FactoryRegistration(
            FactoryKey key,
            FactoryKind kind,
            Id128 ownerPackageId,
            Id128 implementationId,
            uint contractVersion)
        {
            Key = key;
            Kind = kind;
            OwnerPackageId = ownerPackageId;
            ImplementationId = implementationId;
            ContractVersion = contractVersion;
        }

        public FactoryKey Key { get; }

        public FactoryKind Kind { get; }

        /// <summary>Stable plugin-type identity that owns this registration; default means kernel-owned.</summary>
        public Id128 OwnerPackageId { get; }

        /// <summary>Stable identity of the precompiled implementation the key maps to.</summary>
        public Id128 ImplementationId { get; }

        public uint ContractVersion { get; }
    }

    /// <summary>One registered schema with its accepted version and owning package (P-009, P-054).</summary>
    public sealed class SchemaRegistration
    {
        public SchemaRegistration(SchemaRef schema, Id128 ownerPackageId, FactoryKey serializer, bool isRequired)
        {
            Schema = schema;
            OwnerPackageId = ownerPackageId;
            Serializer = serializer;
            IsRequired = isRequired;
        }

        public SchemaRef Schema { get; }

        public Id128 OwnerPackageId { get; }

        public FactoryKey Serializer { get; }

        /// <summary>True when an unknown version of this schema must reject instead of being skipped.</summary>
        public bool IsRequired { get; }
    }

    /// <summary>What a lookup asked for; the failure descriptions below carry it back for diagnostics.</summary>
    public readonly struct CatalogRequest
    {
        public readonly FactoryKey Key;
        public readonly SchemaRef Schema;

        private CatalogRequest(FactoryKey key, SchemaRef schema)
        {
            Key = key;
            Schema = schema;
        }

        public static CatalogRequest ForKey(FactoryKey key) => new CatalogRequest(key, default(SchemaRef));

        public static CatalogRequest ForSchema(SchemaRef schema) => new CatalogRequest(default(FactoryKey), schema);

        public override string ToString() =>
            Key.RegistrationKey.IsDefault ? "schema:" + Schema.ToString() : "key:" + Key.ToString();
    }

    /// <summary>
    /// Result of one catalog lookup. Not-found and unsupported-version are explicit outcomes with a stable
    /// code, never silence and never a substituted default (P-009).
    /// </summary>
    public sealed class CatalogLookup
    {
        private CatalogLookup(bool found, DiagnosticCode code, CatalogRequest request, FactoryRegistration? factory, SchemaRegistration? schema)
        {
            Found = found;
            Code = code;
            Request = request;
            Factory = factory;
            Schema = schema;
        }

        public bool Found { get; }

        /// <summary><see cref="DiagnosticCode.None"/> when found; otherwise the rejection code.</summary>
        public DiagnosticCode Code { get; }

        public CatalogRequest Request { get; }

        public FactoryRegistration? Factory { get; }

        public SchemaRegistration? Schema { get; }

        public static CatalogLookup FactoryFound(FactoryRegistration registration)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            return new CatalogLookup(true, DiagnosticCode.None, CatalogRequest.ForKey(registration.Key), registration, null);
        }

        public static CatalogLookup SchemaFound(SchemaRegistration registration)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            return new CatalogLookup(true, DiagnosticCode.None, CatalogRequest.ForSchema(registration.Schema), null, registration);
        }

        /// <summary>A key that is not registered: missing precompiled factory (P-009).</summary>
        public static CatalogLookup MissingKey(FactoryKey key) =>
            new CatalogLookup(false, DiagnosticCode.MissingDependency, CatalogRequest.ForKey(key), null, null);

        /// <summary>A registered schema identity asked for at a version the catalog does not accept.</summary>
        public static CatalogLookup UnsupportedSchemaVersion(SchemaRef schema) =>
            new CatalogLookup(false, DiagnosticCode.UnsupportedVersion, CatalogRequest.ForSchema(schema), null, null);

        public static CatalogLookup MissingSchema(SchemaRef schema) =>
            new CatalogLookup(false, DiagnosticCode.MissingDependency, CatalogRequest.ForSchema(schema), null, null);

        public string Describe() => Found ? "found" : DiagnosticCodeText.Of(Code) + "(" + Request + ")";
    }

    /// <summary>
    /// Read-only catalog lookup seam. Implementations are generated or explicitly registered; a consumer can
    /// only ask for a key it already declares, and a miss is reported rather than resolved by reflection.
    /// </summary>
    public interface ICatalog
    {
        /// <summary>Catalog fingerprint the plan/checkpoint hashes compare against (P-028, P-053).</summary>
        ContentHash Fingerprint { get; }

        /// <summary>Resolves one generated key; a miss returns <see cref="DiagnosticCode.MissingDependency"/>.</summary>
        CatalogLookup Lookup(FactoryKey key);

        /// <summary>
        /// Resolves one schema reference. An unknown identity is <see cref="DiagnosticCode.MissingDependency"/>;
        /// a known identity at an unaccepted version is <see cref="DiagnosticCode.UnsupportedVersion"/>.
        /// </summary>
        CatalogLookup LookupSchema(SchemaRef schema);

        /// <summary>Registered keys in canonical identity order, independent of registration timing (P-008).</summary>
        IReadOnlyList<FactoryKey> FactoryKeysInCanonicalOrder();
    }
}

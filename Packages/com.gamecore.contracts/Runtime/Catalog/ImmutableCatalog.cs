// GameCore.Contracts - immutable production catalog (GC-003). Normative sources:
// docs/game-core/00-core-protocols.md P-009 (the catalog rejects duplicate ids, unknown schema/contract
// versions, missing precompiled factories, unsupported required features and missing policies before
// activation, and reports a miss instead of substituting a default), P-008 (canonical order) and
// docs/game-core/05-contracts-and-data-model.md s1/s3.
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.Contracts
{
    /// <summary>Outcome of building a catalog from generated registration tables.</summary>
    /// <remarks>
    /// A rejection is a value, not an exception: the caller decides whether the failure is a world-creation
    /// rejection, a mount rejection or a build-time error. Nothing is exposed until every table validates.
    /// </remarks>
    public sealed class CatalogBuildResult
    {
        internal CatalogBuildResult(ImmutableCatalog? catalog, IReadOnlyList<Diagnostic> diagnostics)
        {
            Catalog = catalog;
            Diagnostics = diagnostics ?? Array.Empty<Diagnostic>();
        }

        /// <summary>The built catalog, or null when the description was rejected.</summary>
        public ImmutableCatalog? Catalog { get; }

        /// <summary>Structured rejections; empty on success.</summary>
        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        public bool Succeeded => Catalog != null;

        /// <summary>One-line description for diagnostics and build logs; never an empty string.</summary>
        public string Describe()
        {
            if (Succeeded)
            {
                return "ok";
            }

            if (Diagnostics.Count == 0)
            {
                return "rejected without a diagnostic";
            }

            string[] parts = new string[Diagnostics.Count];
            for (int i = 0; i < Diagnostics.Count; i++)
            {
                parts[i] = Diagnostics[i].CodeText + "(" + Diagnostics[i].Summary + ")";
            }

            return string.Join("; ", parts);
        }

        /// <summary>A rejected catalog description; order of diagnostics is preserved as produced.</summary>
        public static CatalogBuildResult Failure(IReadOnlyList<Diagnostic>? diagnostics) =>
            new CatalogBuildResult(null, ContractCollections.Freeze(diagnostics));
    }

    /// <summary>
    /// Immutable generated catalog. It answers key and schema lookups in canonical identity order, reports a
    /// miss explicitly (never a convenient default and never reflection), carries the catalog fingerprint that
    /// plans and checkpoints compare against, and validates the serializer binding of every schema (P-009,
    /// P-028, P-054).
    /// </summary>
    public sealed class ImmutableCatalog : ICatalog
    {
        private readonly FactoryRegistration[] factories;
        private readonly SchemaRegistration[] schemas;
        private readonly ISchemaSerializer[] serializers;
        private readonly Id128[] supportedFeatureIds;
        private readonly FactoryKey[] factoryKeys;
        private readonly ContentHash fingerprint;

        private ImmutableCatalog(
            FactoryRegistration[] factories,
            SchemaRegistration[] schemas,
            ISchemaSerializer[] serializers,
            Id128[] supportedFeatureIds)
        {
            this.factories = factories;
            this.schemas = schemas;
            this.serializers = serializers;
            this.supportedFeatureIds = supportedFeatureIds;
            fingerprint = CatalogFingerprint.Compute(factories, schemas, supportedFeatureIds);

            FactoryKey[] keys = new FactoryKey[factories.Length];
            for (int i = 0; i < factories.Length; i++)
            {
                keys[i] = factories[i].Key;
            }

            factoryKeys = keys;
        }

        /// <summary>
        /// Validates and freezes one generated catalog description. Null or empty tables describe an empty
        /// catalog, which is legal: an explicit empty catalog is a valid world definition with no plugins.
        /// </summary>
        public static CatalogBuildResult Build(
            IReadOnlyList<FactoryRegistration>? factories,
            IReadOnlyList<SchemaRegistration>? schemas,
            IReadOnlyList<Id128>? supportedFeatureIds,
            IReadOnlyList<ISchemaSerializer>? serializers)
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();

            FactoryRegistration[] factoryTable = CatalogOrdering.SortFactories(factories);
            SchemaRegistration[] schemaTable = CatalogOrdering.SortSchemas(schemas);
            ISchemaSerializer[] serializerTable = CatalogOrdering.SortSerializers(serializers);
            Id128[] featureTable = CatalogOrdering.SortIds(supportedFeatureIds);

            ValidateFactories(factoryTable, diagnostics);
            ValidateSchemas(schemaTable, serializerTable, diagnostics);
            ValidateSerializers(serializerTable, schemaTable, diagnostics);
            ValidateFeatures(featureTable, diagnostics);

            if (diagnostics.Count != 0)
            {
                return CatalogBuildResult.Failure(diagnostics);
            }

            return new CatalogBuildResult(
                new ImmutableCatalog(factoryTable, schemaTable, serializerTable, featureTable),
                Array.Empty<Diagnostic>());
        }

        /// <summary>Catalog fingerprint of the accepted tables (P-028, P-053).</summary>
        public ContentHash Fingerprint => fingerprint;

        /// <summary>Registered factory keys in canonical identity order, independent of registration order (P-008).</summary>
        public IReadOnlyList<FactoryKey> FactoryKeysInCanonicalOrder() => Array.AsReadOnly(factoryKeys);

        /// <summary>Accepted factory registrations in canonical identity order.</summary>
        public IReadOnlyList<FactoryRegistration> Factories => Array.AsReadOnly(factories);

        /// <summary>Accepted schema registrations in canonical schema-id order.</summary>
        public IReadOnlyList<SchemaRegistration> Schemas => Array.AsReadOnly(schemas);

        /// <summary>Validated generated serializers in canonical key order.</summary>
        public IReadOnlyList<ISchemaSerializer> Serializers => Array.AsReadOnly(serializers);

        /// <summary>Feature ids this build supports, in canonical identity order (P-055).</summary>
        public IReadOnlyList<Id128> SupportedFeatureIds => Array.AsReadOnly(supportedFeatureIds);

        /// <summary>True when the build declares the feature; an undeclared required feature must reject (P-055).</summary>
        public bool SupportsFeature(Id128 featureId)
        {
            for (int i = 0; i < supportedFeatureIds.Length; i++)
            {
                if (supportedFeatureIds[i].Equals(featureId))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Resolves one generated key; a miss is reported, never substituted (P-009).</summary>
        public CatalogLookup Lookup(FactoryKey key)
        {
            if (key.RegistrationKey.IsDefault)
            {
                return CatalogLookup.MissingKey(key);
            }

            int index = CatalogOrdering.FindFactory(factories, key);
            return index >= 0 ? CatalogLookup.FactoryFound(factories[index]) : CatalogLookup.MissingKey(key);
        }

        /// <summary>
        /// Resolves one schema reference. A known identity at an unaccepted version is
        /// <see cref="DiagnosticCode.UnsupportedVersion"/>, an unknown identity is
        /// <see cref="DiagnosticCode.MissingDependency"/> (P-009, P-055).
        /// </summary>
        public CatalogLookup LookupSchema(SchemaRef schema)
        {
            if (schema.Id.Value.IsDefault)
            {
                return CatalogLookup.MissingSchema(schema);
            }

            int index = CatalogOrdering.FindSchema(schemas, schema);
            if (index >= 0)
            {
                return CatalogLookup.SchemaFound(schemas[index]);
            }

            return CatalogOrdering.HoldsAnyVersionOfSchema(schemas, schema.Id)
                ? CatalogLookup.UnsupportedSchemaVersion(schema)
                : CatalogLookup.MissingSchema(schema);
        }

        /// <summary>The serializer bound to one schema, or false when the schema is unknown or unbound.</summary>
        public bool TryGetSerializer(SchemaRef schema, out ISchemaSerializer? serializer)
        {
            serializer = null;
            int schemaIndex = CatalogOrdering.FindSchema(schemas, schema);
            if (schemaIndex < 0)
            {
                return false;
            }

            int serializerIndex = CatalogOrdering.FindSerializer(serializers, schemas[schemaIndex].Serializer);
            if (serializerIndex < 0)
            {
                return false;
            }

            serializer = serializers[serializerIndex];
            return true;
        }

        private static void ValidateFactories(FactoryRegistration[] table, List<Diagnostic> diagnostics)
        {
            for (int i = 0; i < table.Length; i++)
            {
                if (table[i].Key.RegistrationKey.IsDefault)
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.MissingDependency,
                        "a default zero factory key is not a catalog identity",
                        default(Id128)));
                    continue;
                }

                if (i > 0 && CatalogOrdering.CompareFactoryKeys(table[i - 1].Key, table[i].Key) == 0)
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.OwnershipConflict,
                        "duplicate factory key registration",
                        table[i].Key.RegistrationKey));
                    continue;
                }

                if (i > 0 && table[i - 1].Key.RegistrationKey.Equals(table[i].Key.RegistrationKey))
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.OwnershipConflict,
                        "one factory key identity is registered at two key versions",
                        table[i].Key.RegistrationKey));
                }
            }
        }

        private static void ValidateSchemas(
            SchemaRegistration[] table,
            ISchemaSerializer[] serializers,
            List<Diagnostic> diagnostics)
        {
            for (int i = 0; i < table.Length; i++)
            {
                SchemaRegistration registration = table[i];
                if (registration.Schema.Id.Value.IsDefault)
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.MissingDependency,
                        "a default zero schema id is not a catalog identity",
                        default(Id128)));
                    continue;
                }

                if (i > 0 && CatalogOrdering.CompareSchemaRegistrations(table[i - 1], registration) == 0)
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.OwnershipConflict,
                        "duplicate schema registration",
                        registration.Schema.Id.Value));
                    continue;
                }

                if (i > 0 && table[i - 1].Schema.Id.Value.Equals(registration.Schema.Id.Value))
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.OwnershipConflict,
                        "one schema identity is registered at two versions",
                        registration.Schema.Id.Value));
                }

                if (registration.Serializer.RegistrationKey.IsDefault)
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.MissingDependency,
                        "schema registration declares no precompiled serializer",
                        registration.Schema.Id.Value));
                    continue;
                }

                int serializerIndex = CatalogOrdering.FindSerializer(serializers, registration.Serializer);
                if (serializerIndex < 0)
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.MissingDependency,
                        "missing precompiled serializer for schema " + registration.Schema,
                        registration.Serializer.RegistrationKey));
                    continue;
                }

                if (!serializers[serializerIndex].Schema.Equals(registration.Schema))
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.MissingDependency,
                        "serializer " + serializers[serializerIndex].Key + " serves " +
                        serializers[serializerIndex].Schema + " but the schema registration declares " +
                        registration.Schema,
                        registration.Serializer.RegistrationKey));
                }
            }
        }

        private static void ValidateSerializers(ISchemaSerializer[] table, SchemaRegistration[] schemas, List<Diagnostic> diagnostics)
        {
            for (int i = 0; i < table.Length; i++)
            {
                ISchemaSerializer serializer = table[i];
                if (serializer.Key.RegistrationKey.IsDefault)
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.MissingDependency,
                        "a default zero serializer key is not a catalog identity",
                        default(Id128)));
                    continue;
                }

                if (i > 0 && CatalogOrdering.CompareFactoryKeys(table[i - 1].Key, serializer.Key) == 0)
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.OwnershipConflict,
                        "duplicate serializer key registration",
                        serializer.Key.RegistrationKey));
                    continue;
                }

                if (CatalogOrdering.FindSchema(schemas, serializer.Schema) < 0)
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.MissingDependency,
                        "serializer " + serializer.Key + " serves schema " + serializer.Schema +
                        ", which the catalog does not register",
                        serializer.Schema.Id.Value));
                }
            }
        }

        private static void ValidateFeatures(Id128[] table, List<Diagnostic> diagnostics)
        {
            for (int i = 0; i < table.Length; i++)
            {
                if (table[i].IsDefault)
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.MissingDependency,
                        "a default zero feature id is not a declared protocol feature",
                        default(Id128)));
                    continue;
                }

                if (i > 0 && table[i - 1].Equals(table[i]))
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.OwnershipConflict,
                        "duplicate supported feature id",
                        table[i]));
                }
            }
        }
    }

    /// <summary>Diagnostic construction for catalog and manifest validation (00 s9 phase Validation).</summary>
    internal static class CatalogDiagnostics
    {
        /// <summary>
        /// One validation rejection. Catalog rejections require changed content unless the structure itself is
        /// invalid, in which case no retry of the same input can succeed (P-049).
        /// </summary>
        internal static Diagnostic Reject(DiagnosticCode code, string summary, Id128 involvedId)
        {
            RetryClassification retry = code == DiagnosticCode.Cycle || code == DiagnosticCode.AmbiguousOrder
                ? RetryClassification.NotRetryable
                : RetryClassification.RequiresChangedInput;

            return new Diagnostic(
                code,
                OperationPhase.Validation,
                default(OperationId),
                ContentHash.Empty,
                involvedId.IsDefault ? null : new[] { involvedId },
                null,
                1L,
                1L,
                retry,
                summary);
        }

        /// <summary>One validation rejection that also names the generated keys involved.</summary>
        internal static Diagnostic Reject(DiagnosticCode code, string summary, Id128 involvedId, FactoryKey involvedKey)
        {
            Diagnostic baseDiagnostic = Reject(code, summary, involvedId);
            return new Diagnostic(
                code,
                baseDiagnostic.Phase,
                baseDiagnostic.Operation,
                baseDiagnostic.PlanHash,
                baseDiagnostic.InvolvedIds,
                new[] { involvedKey },
                baseDiagnostic.Count,
                baseDiagnostic.BudgetLimit,
                baseDiagnostic.Retry,
                summary);
        }
    }
}

// Test-only deterministic stub (namespace GameCore.TestFixtures) for the W0 reference seam.
// In-memory catalog double: duplicate declaration detection and a reproducible catalog hash. It performs
// no reflection-based discovery and never falls back to a convenient default (P-009).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.TestFixtures
{
    /// <summary>
    /// Deterministic in-memory catalog: manifests, explicitly registered generated keys and schemas, with
    /// canonical-order enumeration. It implements the shared <see cref="ICatalog"/> lookup contract so W1 peers
    /// compile against the same miss-reporting seam they will use in production. No reflection-based discovery
    /// and no fallback to a convenient default (P-009, P-015).
    /// </summary>
    public sealed class StubCatalog : ICatalog
    {
        private readonly Dictionary<PluginTypeId, PluginManifest> byType = new Dictionary<PluginTypeId, PluginManifest>();
        private readonly Dictionary<Id128, FactoryRegistration> factories = new Dictionary<Id128, FactoryRegistration>();
        private readonly Dictionary<Id128, SchemaRegistration> schemas = new Dictionary<Id128, SchemaRegistration>();

        public int Count => byType.Count;

        public int FactoryCount => factories.Count;

        public int SchemaCount => schemas.Count;

        /// <summary>Fingerprint over the registered keys and schemas, independent of registration order (P-008).</summary>
        public ContentHash Fingerprint
        {
            get
            {
                IReadOnlyList<FactoryKey> keys = FactoryKeysInCanonicalOrder();
                byte[] buffer = new byte[keys.Count * (Id128.SizeInBytes + 4)];
                int offset = 0;
                for (int i = 0; i < keys.Count; i++)
                {
                    Id128Codec.WriteBigEndian(keys[i].RegistrationKey, buffer, offset);
                    offset += Id128.SizeInBytes;
                    buffer[offset] = (byte)(keys[i].KeyVersion >> 24);
                    buffer[offset + 1] = (byte)(keys[i].KeyVersion >> 16);
                    buffer[offset + 2] = (byte)(keys[i].KeyVersion >> 8);
                    buffer[offset + 3] = (byte)keys[i].KeyVersion;
                    offset += 4;
                }

                return ContentHash.Compute(buffer);
            }
        }

        /// <summary>Registers one generated key; an empty result means accepted.</summary>
        public IReadOnlyList<Diagnostic> RegisterFactory(FactoryRegistration registration)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            List<Diagnostic> diagnostics = new List<Diagnostic>();
            if (registration.Key.RegistrationKey.IsDefault)
            {
                diagnostics.Add(Duplicate(DiagnosticCode.MissingDependency, "A default zero key is not a catalog identity.", registration.Key.RegistrationKey));
                return diagnostics;
            }

            if (factories.ContainsKey(registration.Key.RegistrationKey))
            {
                diagnostics.Add(Duplicate(DiagnosticCode.OwnershipConflict, "Duplicate factory key.", registration.Key.RegistrationKey));
                return diagnostics;
            }

            factories.Add(registration.Key.RegistrationKey, registration);
            return diagnostics;
        }

        /// <summary>Registers one supported schema version; an empty result means accepted.</summary>
        public IReadOnlyList<Diagnostic> RegisterSchema(SchemaRegistration registration)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            List<Diagnostic> diagnostics = new List<Diagnostic>();
            if (schemas.ContainsKey(registration.Schema.Id.Value))
            {
                diagnostics.Add(Duplicate(DiagnosticCode.OwnershipConflict, "Duplicate schema id.", registration.Schema.Id.Value));
                return diagnostics;
            }

            schemas.Add(registration.Schema.Id.Value, registration);
            return diagnostics;
        }

        public CatalogLookup Lookup(FactoryKey key)
        {
            if (key.RegistrationKey.IsDefault)
            {
                return CatalogLookup.MissingKey(key);
            }

            if (!factories.TryGetValue(key.RegistrationKey, out FactoryRegistration? found))
            {
                return CatalogLookup.MissingKey(key);
            }

            if (found.Key.KeyVersion != key.KeyVersion)
            {
                return CatalogLookup.MissingKey(key);
            }

            return CatalogLookup.FactoryFound(found);
        }

        public CatalogLookup LookupSchema(SchemaRef schema)
        {
            if (!schemas.TryGetValue(schema.Id.Value, out SchemaRegistration? found))
            {
                return CatalogLookup.MissingSchema(schema);
            }

            if (found.Schema.Version != schema.Version)
            {
                return CatalogLookup.UnsupportedSchemaVersion(schema);
            }

            return CatalogLookup.SchemaFound(found);
        }

        /// <summary>Registered keys in canonical identity order, never registration order (P-008).</summary>
        public IReadOnlyList<FactoryKey> FactoryKeysInCanonicalOrder()
        {
            FactoryKey[] keys = new FactoryKey[factories.Count];
            int index = 0;
            foreach (FactoryRegistration registration in factories.Values)
            {
                keys[index] = registration.Key;
                index++;
            }

            Array.Sort(keys, CompareFactoryKeys);
            return Array.AsReadOnly(keys);
        }

        private static int CompareFactoryKeys(FactoryKey left, FactoryKey right)
        {
            int byId = left.RegistrationKey.CompareTo(right.RegistrationKey);
            return byId != 0 ? byId : left.KeyVersion.CompareTo(right.KeyVersion);
        }

        /// <summary>Adds one manifest; an empty result means accepted.</summary>
        public IReadOnlyList<Diagnostic> Add(PluginManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            List<Diagnostic> diagnostics = new List<Diagnostic>();
            if (byType.ContainsKey(manifest.PluginTypeId))
            {
                diagnostics.Add(Duplicate(DiagnosticCode.OwnershipConflict, "Duplicate plugin type id.", manifest.PluginTypeId.Value));
                return diagnostics;
            }

            CollectDuplicateDeclarations(manifest, diagnostics);
            if (diagnostics.Count != 0)
            {
                return diagnostics;
            }

            byType.Add(manifest.PluginTypeId, manifest);
            return diagnostics;
        }

        public PluginManifest? Find(PluginTypeId pluginTypeId) =>
            byType.TryGetValue(pluginTypeId, out PluginManifest? found) ? found : null;

        /// <summary>Plugin type ids sorted by canonical big-endian identity bytes, never by insertion order.</summary>
        public IReadOnlyList<PluginTypeId> TypeIdsInCanonicalOrder()
        {
            PluginTypeId[] ids = new PluginTypeId[byType.Count];
            byType.Keys.CopyTo(ids, 0);
            Array.Sort(ids, ComparePluginTypes);
            return Array.AsReadOnly(ids);
        }

        public IReadOnlyList<PluginManifest> ManifestsInCanonicalOrder()
        {
            IReadOnlyList<PluginTypeId> ids = TypeIdsInCanonicalOrder();
            PluginManifest[] manifests = new PluginManifest[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                manifests[i] = byType[ids[i]];
            }

            return Array.AsReadOnly(manifests);
        }

        /// <summary>SHA-256 over the canonical-order (type id bytes, package content hash) pairs.</summary>
        public ContentHash CatalogHash()
        {
            IReadOnlyList<PluginManifest> manifests = ManifestsInCanonicalOrder();
            byte[] buffer = new byte[manifests.Count * (Id128.SizeInBytes + ContentHash.SizeInBytes)];
            int offset = 0;
            for (int i = 0; i < manifests.Count; i++)
            {
                Id128Codec.WriteBigEndian(manifests[i].PluginTypeId.Value, buffer, offset);
                offset += Id128.SizeInBytes;
                manifests[i].PackageContentHash.CopyTo(buffer, offset);
                offset += ContentHash.SizeInBytes;
            }

            return ContentHash.Compute(buffer);
        }

        /// <summary>Detects duplicate stable ids inside one manifest (one live owner per id, P-004).</summary>
        public IReadOnlyList<Diagnostic> ValidateUniqueDeclarations(PluginManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            List<Diagnostic> diagnostics = new List<Diagnostic>();
            CollectDuplicateDeclarations(manifest, diagnostics);
            return diagnostics;
        }

        private static void CollectDuplicateDeclarations(PluginManifest manifest, List<Diagnostic> diagnostics)
        {
            HashSet<Id128> capabilityIds = new HashSet<Id128>();
            foreach (CapabilityContract contract in manifest.CapabilityContracts)
            {
                if (!capabilityIds.Add(contract.Capability.Capability.Value))
                {
                    diagnostics.Add(Duplicate(DiagnosticCode.CapabilityConflict, "Duplicate capability id.", contract.Capability.Capability.Value));
                }
            }

            HashSet<Id128> ruleIds = new HashSet<Id128>();
            foreach (DerivationRule rule in manifest.DerivationRules)
            {
                if (!ruleIds.Add(rule.RuleId.Value))
                {
                    diagnostics.Add(Duplicate(DiagnosticCode.OwnershipConflict, "Duplicate rule id.", rule.RuleId.Value));
                }
            }

            HashSet<Id128> stageIds = new HashSet<Id128>();
            foreach (StageSpec stage in manifest.Stages)
            {
                if (!stageIds.Add(stage.StageId.Value))
                {
                    diagnostics.Add(Duplicate(DiagnosticCode.OwnershipConflict, "Duplicate stage id.", stage.StageId.Value));
                }
            }

            HashSet<Id128> slotIds = new HashSet<Id128>();
            foreach (StateSlotSpec slot in manifest.StateSlots)
            {
                if (!slotIds.Add(slot.SlotId.Value))
                {
                    diagnostics.Add(Duplicate(DiagnosticCode.OwnershipConflict, "Duplicate state-slot id.", slot.SlotId.Value));
                }
            }

            HashSet<Id128> bufferIds = new HashSet<Id128>();
            foreach (BufferSpec buffer in manifest.Buffers)
            {
                if (!bufferIds.Add(buffer.BufferId.Value))
                {
                    diagnostics.Add(Duplicate(DiagnosticCode.OwnershipConflict, "Duplicate buffer id.", buffer.BufferId.Value));
                }
            }

            HashSet<Id128> resourceKeys = new HashSet<Id128>();
            foreach (ResourceSpec resource in manifest.Resources)
            {
                if (!resourceKeys.Add(resource.ResourceKey.Value))
                {
                    diagnostics.Add(Duplicate(DiagnosticCode.OwnershipConflict, "Duplicate resource key.", resource.ResourceKey.Value));
                }
            }
        }

        private static Diagnostic Duplicate(DiagnosticCode code, string summary, Id128 involved) =>
            new Diagnostic(
                code,
                OperationPhase.Validation,
                default(OperationId),
                ContentHash.Empty,
                new[] { involved },
                null,
                1L,
                1L,
                RetryClassification.RequiresChangedInput,
                summary);

        private static int ComparePluginTypes(PluginTypeId left, PluginTypeId right) => left.CompareTo(right);
    }
}

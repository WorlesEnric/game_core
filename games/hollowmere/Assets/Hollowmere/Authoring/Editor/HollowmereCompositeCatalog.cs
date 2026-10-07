#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Studio.Edit;

namespace Hollowmere.Authoring
{
    /// <summary>Reviewed additive catalog set; the game's baked catalog remains unchanged.</summary>
    internal sealed class HollowmereCompositeCatalog : ICatalog
    {
        private readonly ICatalog world;
        private readonly IReadOnlyList<ICatalog> mechanisms;
        private readonly IReadOnlyList<FactoryKey> keys;

        internal HollowmereCompositeCatalog(ICatalog world, IReadOnlyList<ICatalog> mechanisms)
        {
            this.world = world;
            this.mechanisms = new List<ICatalog>(mechanisms).AsReadOnly();
            var merged = new List<FactoryKey>(world.FactoryKeysInCanonicalOrder());
            var seen = new HashSet<FactoryKey>(merged);
            var fingerprints = new List<string>();
            foreach (ICatalog mechanism in mechanisms)
            {
                fingerprints.Add(mechanism.Fingerprint.ToHex());
                foreach (FactoryKey key in mechanism.FactoryKeysInCanonicalOrder())
                {
                    if (!seen.Add(key)) throw new InvalidOperationException("An admitted catalog shadows an existing factory key: " + key);
                    merged.Add(key);
                }
            }
            merged.Sort((left, right) =>
            {
                int order = left.RegistrationKey.CompareTo(right.RegistrationKey);
                return order != 0 ? order : left.KeyVersion.CompareTo(right.KeyVersion);
            });
            keys = merged.AsReadOnly();
            if (!ContentHash.TryParseHex(CatalogSet.Combine(world.Fingerprint.ToHex(), fingerprints), out ContentHash fingerprint))
                throw new InvalidOperationException("The trusted catalog set has no valid fingerprint");
            Fingerprint = fingerprint;
        }

        public ContentHash Fingerprint { get; }
        public IReadOnlyList<FactoryKey> FactoryKeysInCanonicalOrder() => keys;

        public CatalogLookup Lookup(FactoryKey key)
        {
            CatalogLookup found = world.Lookup(key);
            if (found.Found) return found;
            foreach (ICatalog mechanism in mechanisms)
            {
                CatalogLookup own = mechanism.Lookup(key);
                if (own.Found) return own;
            }
            return found;
        }

        public CatalogLookup LookupSchema(SchemaRef schema)
        {
            CatalogLookup found = world.LookupSchema(schema);
            bool declared = found.Found || found.Code == DiagnosticCode.UnsupportedVersion;
            foreach (ICatalog mechanism in mechanisms)
            {
                CatalogLookup own = mechanism.LookupSchema(schema);
                if (!own.Found && own.Code != DiagnosticCode.UnsupportedVersion) continue;
                if (declared) throw new InvalidOperationException("An admitted catalog shadows an existing schema identity: " + schema);
                found = own;
                declared = true;
            }
            return found;
        }
    }
}

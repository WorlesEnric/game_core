#nullable enable
// Hollowmere.Mechanism.PressurePlate - catalog composition of a world catalog with mechanism catalogs (W-MECH-01).
//
// A mechanism package ships its own generated catalog; it never regenerates the world's. A world that admits
// mechanisms runs a composite catalog whose fingerprint is the catalog-set hash:
//
//   CatalogSet.Combine(world, mechanisms) =
//       lowercase hex SHA-256 of UTF-8 ("gamecore.catalog-set/1\n" + world + "\n" + join("\n", sort-ordinal(mechanisms)))
//
// and, with no mechanism, the world fingerprint unchanged (so a world without mechanisms keeps its baked fingerprint).
// The formula is a contract: the Studio admission path re-implements it to verify a staged composition.
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using GameCore.Contracts;

namespace Hollowmere.Mechanism.PressurePlate
{
    /// <summary>The catalog-set fingerprint of a world catalog composed with mechanism catalogs.</summary>
    public static class CatalogSet
    {
        /// <summary>Header line of the hashed text.</summary>
        public const string Format = "gamecore.catalog-set/1";

        /// <summary>
        /// Lowercase hex SHA-256 of the UTF-8 text <c>Format + "\n" + world + "\n" + mechanisms sorted ordinal joined by
        /// "\n"</c>; <paramref name="worldFingerprintHex"/> unchanged when there is no mechanism. Inputs are hashed as
        /// given (callers pass lowercase hex fingerprints).
        /// </summary>
        public static string Combine(string worldFingerprintHex, IEnumerable<string> mechanismFingerprintHexes)
        {
            if (worldFingerprintHex == null)
            {
                throw new ArgumentNullException(nameof(worldFingerprintHex));
            }

            if (mechanismFingerprintHexes == null)
            {
                throw new ArgumentNullException(nameof(mechanismFingerprintHexes));
            }

            var mechanisms = new List<string>();
            foreach (string mechanism in mechanismFingerprintHexes)
            {
                if (mechanism == null)
                {
                    throw new ArgumentException("A mechanism fingerprint cannot be null.", nameof(mechanismFingerprintHexes));
                }

                mechanisms.Add(mechanism);
            }

            if (mechanisms.Count == 0)
            {
                return worldFingerprintHex;
            }

            mechanisms.Sort(StringComparer.Ordinal);
            string text = Format + "\n" + worldFingerprintHex + "\n" + string.Join("\n", mechanisms);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var hex = new StringBuilder(digest.Length * 2);
                for (int i = 0; i < digest.Length; i++)
                {
                    hex.Append(digest[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                }

                return hex.ToString();
            }
        }
    }

    /// <summary>
    /// A world catalog composed with mechanism catalogs: lookups try the world first, then each mechanism; the canonical
    /// key order is the merged order of every part (registration key, then key version, as the generated tables sort);
    /// the fingerprint is <see cref="CatalogSet.Combine"/> of the parts. A key or schema identity registered by two
    /// parts is a composition defect and is refused at construction.
    /// </summary>
    public sealed class CompositeCatalog : ICatalog
    {
        private readonly ICatalog world;
        private readonly ICatalog[] mechanisms;
        private readonly FactoryKey[] keys;

        public CompositeCatalog(ICatalog world, IReadOnlyList<ICatalog> mechanisms)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            if (mechanisms == null)
            {
                throw new ArgumentNullException(nameof(mechanisms));
            }

            this.mechanisms = new ICatalog[mechanisms.Count];
            var hexes = new List<string>(mechanisms.Count);
            var merged = new List<FactoryKey>(world.FactoryKeysInCanonicalOrder());
            var seen = new HashSet<FactoryKey>(merged);
            for (int i = 0; i < mechanisms.Count; i++)
            {
                ICatalog mechanism = mechanisms[i] ?? throw new ArgumentException("A mechanism catalog cannot be null.", nameof(mechanisms));
                this.mechanisms[i] = mechanism;
                hexes.Add(mechanism.Fingerprint.ToHex());
                IReadOnlyList<FactoryKey> own = mechanism.FactoryKeysInCanonicalOrder();
                for (int k = 0; k < own.Count; k++)
                {
                    if (!seen.Add(own[k]))
                    {
                        throw new ArgumentException(
                            "factory key " + own[k] + " is registered by two catalogs of the set; a mechanism registers only its own keys");
                    }

                    merged.Add(own[k]);
                }
            }

            merged.Sort(CompareKeys);
            keys = merged.ToArray();
            WorldFingerprint = world.Fingerprint;
            string combined = CatalogSet.Combine(world.Fingerprint.ToHex(), hexes);
            if (!ContentHash.TryParseHex(combined, out ContentHash fingerprint))
            {
                throw new InvalidOperationException("the catalog-set hash " + combined + " is not a content hash");
            }

            Fingerprint = fingerprint;
        }

        /// <summary>The catalog-set fingerprint (<see cref="CatalogSet.Combine"/>).</summary>
        public ContentHash Fingerprint { get; }

        /// <summary>The fingerprint of the world part alone.</summary>
        public ContentHash WorldFingerprint { get; }

        public int MechanismCount => mechanisms.Length;

        public CatalogLookup Lookup(FactoryKey key)
        {
            CatalogLookup found = world.Lookup(key);
            if (found.Found)
            {
                return found;
            }

            for (int i = 0; i < mechanisms.Length; i++)
            {
                CatalogLookup own = mechanisms[i].Lookup(key);
                if (own.Found)
                {
                    return own;
                }
            }

            return found;
        }

        public CatalogLookup LookupSchema(SchemaRef schema)
        {
            CatalogLookup found = world.LookupSchema(schema);
            if (found.Found || found.Code == DiagnosticCode.UnsupportedVersion)
            {
                return found;
            }

            for (int i = 0; i < mechanisms.Length; i++)
            {
                CatalogLookup own = mechanisms[i].LookupSchema(schema);
                if (own.Found || own.Code == DiagnosticCode.UnsupportedVersion)
                {
                    return own;
                }
            }

            return found;
        }

        public IReadOnlyList<FactoryKey> FactoryKeysInCanonicalOrder() => keys;

        /// <summary>The generated tables' canonical key order: registration key (big-endian id), then key version.</summary>
        private static int CompareKeys(FactoryKey left, FactoryKey right)
        {
            int byId = left.RegistrationKey.CompareTo(right.RegistrationKey);
            return byId != 0 ? byId : left.KeyVersion.CompareTo(right.KeyVersion);
        }
    }
}

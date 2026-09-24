// GameCore.Contracts - internal canonical ordering and lookup helpers (GC-003).
// Sorting and lookup order is the protocol's canonical big-endian stable-id order (P-008): never declaration
// order, registration timing, dictionary enumeration or hash order. Internal on purpose: it is an
// implementation detail of the catalog, not part of the frozen public surface.
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.Contracts
{
    /// <summary>Canonical ordering and binary-search helpers for generated registration tables (P-008).</summary>
    internal static class CatalogOrdering
    {
        internal static FactoryRegistration[] SortFactories(IReadOnlyList<FactoryRegistration>? source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<FactoryRegistration>();
            }

            List<FactoryRegistration> copy = new List<FactoryRegistration>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                FactoryRegistration entry = source[i];
                if (entry == null)
                {
                    throw new ArgumentException("A factory registration table cannot contain null entries.", nameof(source));
                }

                copy.Add(entry);
            }

            copy.Sort(CompareFactoryRegistrations);
            return copy.ToArray();
        }

        internal static SchemaRegistration[] SortSchemas(IReadOnlyList<SchemaRegistration>? source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<SchemaRegistration>();
            }

            List<SchemaRegistration> copy = new List<SchemaRegistration>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                SchemaRegistration entry = source[i];
                if (entry == null)
                {
                    throw new ArgumentException("A schema registration table cannot contain null entries.", nameof(source));
                }

                copy.Add(entry);
            }

            copy.Sort(CompareSchemaRegistrations);
            return copy.ToArray();
        }

        internal static ISchemaSerializer[] SortSerializers(IReadOnlyList<ISchemaSerializer>? source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<ISchemaSerializer>();
            }

            List<ISchemaSerializer> copy = new List<ISchemaSerializer>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                ISchemaSerializer entry = source[i];
                if (entry == null)
                {
                    throw new ArgumentException("A serializer table cannot contain null entries.", nameof(source));
                }

                copy.Add(entry);
            }

            copy.Sort(CompareSerializers);
            return copy.ToArray();
        }

        internal static Id128[] SortIds(IReadOnlyList<Id128>? source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<Id128>();
            }

            Id128[] copy = new Id128[source.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = source[i];
            }

            Array.Sort(copy, CompareIds);
            return copy;
        }

        internal static int CompareIds(Id128 left, Id128 right) => left.CompareTo(right);

        internal static int CompareFactoryKeys(FactoryKey left, FactoryKey right)
        {
            int byId = left.RegistrationKey.CompareTo(right.RegistrationKey);
            return byId != 0 ? byId : left.KeyVersion.CompareTo(right.KeyVersion);
        }

        internal static int CompareFactoryRegistrations(FactoryRegistration left, FactoryRegistration right) =>
            CompareFactoryKeys(left.Key, right.Key);

        internal static int CompareSchemaRegistrations(SchemaRegistration left, SchemaRegistration right)
        {
            int byId = left.Schema.Id.Value.CompareTo(right.Schema.Id.Value);
            return byId != 0 ? byId : left.Schema.Version.CompareTo(right.Schema.Version);
        }

        internal static int CompareSerializers(ISchemaSerializer left, ISchemaSerializer right) =>
            CompareFactoryKeys(left.Key, right.Key);

        /// <summary>Index of an exact registration key in a table sorted by <see cref="CompareFactoryRegistrations"/>, or -1.</summary>
        internal static int FindFactory(FactoryRegistration[] table, FactoryKey key)
        {
            int low = 0;
            int high = table.Length - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) / 2);
                int comparison = CompareFactoryKeys(table[middle].Key, key);
                if (comparison == 0)
                {
                    return middle;
                }

                if (comparison < 0)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return -1;
        }

        /// <summary>Index of an exact schema in a table sorted by <see cref="CompareSchemaRegistrations"/>, or -1.</summary>
        internal static int FindSchema(SchemaRegistration[] table, SchemaRef schema)
        {
            int low = 0;
            int high = table.Length - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) / 2);
                SchemaRegistration candidate = table[middle];
                int byId = candidate.Schema.Id.Value.CompareTo(schema.Id.Value);
                int comparison = byId != 0 ? byId : candidate.Schema.Version.CompareTo(schema.Version);
                if (comparison == 0)
                {
                    return middle;
                }

                if (comparison < 0)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return -1;
        }

        /// <summary>True when a table sorted by schema id holds that identity at some other version.</summary>
        internal static bool HoldsAnyVersionOfSchema(SchemaRegistration[] table, SchemaId id)
        {
            for (int i = 0; i < table.Length; i++)
            {
                if (table[i].Schema.Id.Value.Equals(id.Value))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Index of a serializer key in a table sorted by <see cref="CompareSerializers"/>, or -1.</summary>
        internal static int FindSerializer(ISchemaSerializer[] table, FactoryKey key)
        {
            int low = 0;
            int high = table.Length - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) / 2);
                int comparison = CompareFactoryKeys(table[middle].Key, key);
                if (comparison == 0)
                {
                    return middle;
                }

                if (comparison < 0)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return -1;
        }
    }
}

// GameCore.Planning — generated, mutually exclusive partition ids (GC-007).
//
// Normative sources: docs/game-core/00-core-protocols.md P-034, P-040 and docs/game-core/03-runtime-and-execution.md s4.
// V1 accepts writer disjointness only from validated, mutually exclusive `PartitionId` assignments, never from an
// arbitrary query predicate. A generated partition id is derived from the owning domain's stable identity, so two
// different writers can never receive the same id by accident and a rerun of the same declaration set produces the
// same ids (P-008: no registration timing or worker index decides the value).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Planning.Ownership
{
    /// <summary>One writer's claim on one authoritative domain: a partition id, explicit or generated.</summary>
    public readonly struct PartitionAssignment
    {
        public readonly SchemaRef Domain;
        public readonly OwnerId Owner;
        public readonly FactoryKey Writer;
        public readonly Id128 PartitionId;

        /// <summary>True when the id came from <see cref="PartitionIdGenerator"/> rather than the declaration.</summary>
        public readonly bool Generated;

        public PartitionAssignment(SchemaRef domain, OwnerId owner, FactoryKey writer, Id128 partitionId, bool generated)
        {
            Domain = domain;
            Owner = owner;
            Writer = writer;
            PartitionId = partitionId;
            Generated = generated;
        }

        /// <summary>False when the writer left this domain unpartitioned, which never proves disjointness.</summary>
        public bool IsPartitioned => !PartitionId.IsDefault;

        public override string ToString()
            => Domain.ToString() + "/" + Writer.ToString() + ":" + PartitionId.ToString()
                + (Generated ? "(generated)" : "(declared)");
    }

    /// <summary>
    /// Deterministic partition-id derivation. The value is the first 128 bits of the SHA-256 over the canonical
    /// big-endian bytes of (salt, owner, domain id, domain version, writer key, key version); no clock, thread or
    /// registration order participates (P-008).
    /// </summary>
    public sealed class PartitionIdGenerator
    {
        /// <summary>Fixed category salt of every derived partition id, so a partition can never equal a plain id.</summary>
        public const ulong Salt = 0x4743504152544954UL;

        /// <summary>Canonical input scope of <see cref="Next"/>, recorded beside every produced value.</summary>
        public const string Scope =
            "SHA-256 over 72 canonical bytes: salt (8), owner id (16), domain schema id (16), domain version (8), "
            + "writer registration key (16), writer key version (8); first 128 bits, high word then low word";

        /// <summary>Derives the partition id of one writer's claim on one domain.</summary>
        public Id128 Next(OwnerId owner, SchemaRef domain, FactoryKey writer)
        {
            byte[] record = new byte[72];
            WriteUInt64BigEndian(Salt, record, 0);
            Id128Codec.WriteBigEndian(owner.Value, record, 8);
            Id128Codec.WriteBigEndian(domain.Id.Value, record, 24);
            WriteUInt64BigEndian(domain.Version, record, 40);
            Id128Codec.WriteBigEndian(writer.RegistrationKey, record, 48);
            WriteUInt64BigEndian(writer.KeyVersion, record, 64);

            byte[] digest = ContentHash.Compute(record).ToArray();
            return Id128Codec.ReadBigEndian(digest, 0);
        }

        /// <summary>
        /// True when two assignments provably cannot touch the same rows: either they claim different domains, or
        /// both are partitioned with different partition ids. A partitioned writer against an unpartitioned one is
        /// never disjoint (P-040), and neither is the same partition id twice.
        /// </summary>
        public static bool ProvablyDisjoint(PartitionAssignment left, PartitionAssignment right)
        {
            if (!left.Domain.Id.Value.Equals(right.Domain.Id.Value))
            {
                return true;
            }

            return left.IsPartitioned && right.IsPartitioned && !left.PartitionId.Equals(right.PartitionId);
        }

        /// <summary>True when two assignments name the same domain and the same partition id.</summary>
        public static bool Overlaps(PartitionAssignment left, PartitionAssignment right)
            => left.Domain.Id.Value.Equals(right.Domain.Id.Value)
                && left.IsPartitioned
                && right.IsPartitioned
                && left.PartitionId.Equals(right.PartitionId);

        internal static void WriteUInt64BigEndian(ulong value, byte[] destination, int offset)
        {
            destination[offset] = (byte)(value >> 56);
            destination[offset + 1] = (byte)(value >> 48);
            destination[offset + 2] = (byte)(value >> 40);
            destination[offset + 3] = (byte)(value >> 32);
            destination[offset + 4] = (byte)(value >> 24);
            destination[offset + 5] = (byte)(value >> 16);
            destination[offset + 6] = (byte)(value >> 8);
            destination[offset + 7] = (byte)value;
        }

        /// <summary>Canonical comparison of two assignment lists, independent of the order they were built in.</summary>
        public static int CompareAssignments(PartitionAssignment left, PartitionAssignment right)
        {
            int byDomain = left.Domain.Id.Value.CompareTo(right.Domain.Id.Value);
            if (byDomain != 0)
            {
                return byDomain;
            }

            int byWriter = left.Writer.RegistrationKey.CompareTo(right.Writer.RegistrationKey);
            return byWriter != 0 ? byWriter : left.PartitionId.CompareTo(right.PartitionId);
        }
    }

    /// <summary>Canonical ordering helpers shared by the ownership validator and its maps.</summary>
    internal static class OwnershipOrdering
    {
        internal static void SortAssignments(List<PartitionAssignment> assignments)
            => assignments.Sort(PartitionIdGenerator.CompareAssignments);
    }
}

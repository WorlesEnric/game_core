// GameCore.Planning - semantic schedule compilation (GC-009).
// Normative source: docs/game-core/00-core-protocols.md P-008 (canonical big-endian stable-ID order, ordinal
// identifiers, explicit numeric keys; registration timing and dictionary order must never decide precedence).
// Unity-free: BCL subset only, no UnityEngine/Unity.* reference (01 section 1).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Planning.Scheduling
{
    /// <summary>
    /// Canonical <see cref="FactoryKey"/> order: registration key bytes first, then the key version. Used for every
    /// ready-set tie-break inside a stage, so the compiled order never depends on declaration order (P-008, P-040).
    /// </summary>
    public sealed class FactoryKeyComparer : IComparer<FactoryKey>
    {
        public static readonly FactoryKeyComparer Instance = new FactoryKeyComparer();

        public int Compare(FactoryKey x, FactoryKey y)
        {
            int order = x.RegistrationKey.CompareTo(y.RegistrationKey);
            return order != 0 ? order : x.KeyVersion.CompareTo(y.KeyVersion);
        }
    }

    /// <summary>
    /// Canonical access-declaration order: schema id bytes, schema version, access mode, then partition id bytes.
    /// Declaration order inside an access set is therefore never semantic (P-008, P-040).
    /// </summary>
    public sealed class AccessDeclarationComparer : IComparer<AccessDeclaration>
    {
        public static readonly AccessDeclarationComparer Instance = new AccessDeclarationComparer();

        public int Compare(AccessDeclaration x, AccessDeclaration y)
        {
            int order = x.Schema.Id.Value.CompareTo(y.Schema.Id.Value);
            if (order != 0)
            {
                return order;
            }

            order = x.Schema.Version.CompareTo(y.Schema.Version);
            if (order != 0)
            {
                return order;
            }

            order = ((int)x.Mode).CompareTo((int)y.Mode);
            return order != 0 ? order : x.PartitionId.CompareTo(y.PartitionId);
        }
    }

    /// <summary>Canonical buffer order: buffer id bytes then consuming stage id.</summary>
    public sealed class BufferBindingComparer : IComparer<BufferBinding>
    {
        public static readonly BufferBindingComparer Instance = new BufferBindingComparer();

        public int Compare(BufferBinding? x, BufferBinding? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x == null)
            {
                return -1;
            }

            if (y == null)
            {
                return 1;
            }

            int order = x.Buffer.CompareTo(y.Buffer);
            return order != 0 ? order : x.ConsumerStage.CompareTo(y.ConsumerStage);
        }
    }
}

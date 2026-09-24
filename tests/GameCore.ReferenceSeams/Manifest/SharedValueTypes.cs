// Test-only reference seam for the shared GameCore.Contracts surface (see TestOnlyMarker.cs).
// Immutable value DTOs for docs/game-core/05-contracts-and-data-model.md. Pure data plus canonical
// comparison/byte writers only: no ECS, world runtime, ownership or validation policy lives here.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;

namespace GameCore.Contracts
{
    /// <summary>Defensive immutable copies for DTO declaration arrays (05 s1).</summary>
    public static class ContractCollections
    {
        /// <summary>Copies the sequence into a read-only wrapper; null or empty yields <see cref="Array.Empty{T}"/>.</summary>
        public static IReadOnlyList<T> Freeze<T>(IReadOnlyList<T>? source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<T>();
            }

            T[] copy = new T[source.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = source[i];
            }

            return Array.AsReadOnly(copy);
        }
    }

    /// <summary>
    /// Immutable payload ownership: clone caller input and expose only a read-only wrapper (05 s1).
    /// Full schema/bounds validation belongs to generated constructors and host admission.
    /// </summary>
    public sealed class FrozenPayload
    {
        private readonly IReadOnlyList<byte> bytes;

        public FrozenPayload(byte[] source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            bytes = Array.AsReadOnly((byte[])source.Clone());
        }

        public IReadOnlyList<byte> Bytes => bytes;

        public int Length => bytes.Count;
    }

    /// <summary>
    /// 32-byte content/catalog/plan hash. Canonical bytes are the SHA-256 output order; this is never a
    /// platform-formatted Guid (05 s2, P-054).
    /// </summary>
    public readonly struct ContentHash : IEquatable<ContentHash>
    {
        public const int SizeInBytes = 32;

        private const int HexLength = SizeInBytes * 2;

        public static readonly ContentHash Empty = new ContentHash(new byte[SizeInBytes]);

        private readonly ulong h0;
        private readonly ulong h1;
        private readonly ulong h2;
        private readonly ulong h3;

        public ContentHash(byte[] source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (source.Length != SizeInBytes)
            {
                throw new ArgumentException("A content hash is exactly 32 bytes.", nameof(source));
            }

            h0 = Id128Codec.ReadUInt64BigEndian(source, 0);
            h1 = Id128Codec.ReadUInt64BigEndian(source, 8);
            h2 = Id128Codec.ReadUInt64BigEndian(source, 16);
            h3 = Id128Codec.ReadUInt64BigEndian(source, 24);
        }

        /// <summary>True when every byte is zero; used to detect an unset hash in fixtures and diagnostics.</summary>
        public bool IsEmpty => h0 == 0UL && h1 == 0UL && h2 == 0UL && h3 == 0UL;

        public static ContentHash Compute(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            using (SHA256 sha = SHA256.Create())
            {
                return new ContentHash(sha.ComputeHash(data));
            }
        }

        public byte[] ToArray()
        {
            byte[] bytes = new byte[SizeInBytes];
            CopyTo(bytes, 0);
            return bytes;
        }

        public void CopyTo(byte[] destination, int offset)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (offset < 0 || offset + SizeInBytes > destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "Destination must hold 32 bytes from offset.");
            }

            Id128Codec.WriteUInt64BigEndian(h0, destination, offset);
            Id128Codec.WriteUInt64BigEndian(h1, destination, offset + 8);
            Id128Codec.WriteUInt64BigEndian(h2, destination, offset + 16);
            Id128Codec.WriteUInt64BigEndian(h3, destination, offset + 24);
        }

        public string ToHex() => CanonicalHex.ToHex(ToArray(), 0, SizeInBytes);

        /// <summary>
        /// Parses the one canonical form: exactly 64 lowercase hex characters, no whitespace and no sign.
        /// Uppercase input is rejected rather than normalized (P-054).
        /// </summary>
        public static bool TryParseHex(string? text, out ContentHash value)
        {
            value = Empty;
            if (text == null || text.Length != HexLength)
            {
                return false;
            }

            byte[] bytes = new byte[SizeInBytes];
            if (!CanonicalHex.TryParseBytes(text, bytes, 0))
            {
                return false;
            }

            value = new ContentHash(bytes);
            return true;
        }

        public bool Equals(ContentHash other) => h0 == other.h0 && h1 == other.h1 && h2 == other.h2 && h3 == other.h3;

        public override bool Equals(object? obj) => obj is ContentHash other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + h0.GetHashCode();
                hash = (hash * 31) + h1.GetHashCode();
                hash = (hash * 31) + h2.GetHashCode();
                hash = (hash * 31) + h3.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(ContentHash left, ContentHash right) => left.Equals(right);

        public static bool operator !=(ContentHash left, ContentHash right) => !left.Equals(right);

        public override string ToString() => "sha256:" + ToHex();
    }

    /// <summary>Integer protocol version (P-055). Major changes are incompatible.</summary>
    public readonly struct ProtocolVersion : IEquatable<ProtocolVersion>, IComparable<ProtocolVersion>
    {
        public readonly int Major;
        public readonly int Minor;

        public ProtocolVersion(int major, int minor)
        {
            Major = major;
            Minor = minor;
        }

        public bool Equals(ProtocolVersion other) => Major == other.Major && Minor == other.Minor;

        public override bool Equals(object? obj) => obj is ProtocolVersion other && Equals(other);

        public override int GetHashCode() => (Major * 397) ^ Minor;

        public int CompareTo(ProtocolVersion other)
        {
            int major = Major.CompareTo(other.Major);
            return major != 0 ? major : Minor.CompareTo(other.Minor);
        }

        public static bool operator ==(ProtocolVersion left, ProtocolVersion right) => left.Equals(right);

        public static bool operator !=(ProtocolVersion left, ProtocolVersion right) => !left.Equals(right);

        public override string ToString() =>
            Major.ToString(CultureInfo.InvariantCulture) + "." + Minor.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>A manifest's declared protocol support: one major plus a min/max minor (P-055).</summary>
    public readonly struct SupportedProtocolRange
    {
        public readonly int Major;
        public readonly int MinMinor;
        public readonly int MaxMinor;

        public SupportedProtocolRange(int major, int minMinor, int maxMinor)
        {
            Major = major;
            MinMinor = minMinor;
            MaxMinor = maxMinor;
        }

        public override string ToString() =>
            Major.ToString(CultureInfo.InvariantCulture) + "." +
            MinMinor.ToString(CultureInfo.InvariantCulture) + "-" +
            MaxMinor.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Inclusive contract version range. <see cref="MaxVersion"/> equal to <see cref="uint.MaxValue"/>
    /// means open-ended (05 s3 ServiceDependency).
    /// </summary>
    public readonly struct VersionRange : IEquatable<VersionRange>
    {
        public readonly uint MinVersion;
        public readonly uint MaxVersion;

        public VersionRange(uint minVersion, uint maxVersion)
        {
            MinVersion = minVersion;
            MaxVersion = maxVersion;
        }

        public bool Equals(VersionRange other) => MinVersion == other.MinVersion && MaxVersion == other.MaxVersion;

        public override bool Equals(object? obj) => obj is VersionRange other && Equals(other);

        public override int GetHashCode() => ((int)MinVersion * 397) ^ (int)MaxVersion;

        public static bool operator ==(VersionRange left, VersionRange right) => left.Equals(right);

        public static bool operator !=(VersionRange left, VersionRange right) => !left.Equals(right);

        public override string ToString() =>
            MinVersion.ToString(CultureInfo.InvariantCulture) + ".." + MaxVersion.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Declared access mode of one schema for one system (P-039 to P-041).</summary>
    public enum AccessMode
    {
        Read = 0,
        Write = 1,
        ReadWrite = 2,
    }

    /// <summary>
    /// One per-system access declaration. A non-default <see cref="PartitionId"/> claims a validated
    /// disjoint partition instead of a whole-schema conflict (P-040).
    /// </summary>
    public readonly struct AccessDeclaration : IEquatable<AccessDeclaration>
    {
        public readonly SchemaRef Schema;
        public readonly AccessMode Mode;
        public readonly Id128 PartitionId;

        public AccessDeclaration(SchemaRef schema, AccessMode mode, Id128 partitionId)
        {
            Schema = schema;
            Mode = mode;
            PartitionId = partitionId;
        }

        public bool IsPartitioned => !PartitionId.IsDefault;

        public bool Equals(AccessDeclaration other) =>
            Schema.Equals(other.Schema) && Mode == other.Mode && PartitionId.Equals(other.PartitionId);

        public override bool Equals(object? obj) => obj is AccessDeclaration other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + Schema.GetHashCode();
                hash = (hash * 31) + (int)Mode;
                hash = (hash * 31) + PartitionId.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(AccessDeclaration left, AccessDeclaration right) => left.Equals(right);

        public static bool operator !=(AccessDeclaration left, AccessDeclaration right) => !left.Equals(right);

        public override string ToString() => Schema.ToString() + ":" + Mode.ToString();
    }

    /// <summary>Read/write set declared by a stage or one system entry (P-039).</summary>
    public sealed class AccessSet
    {
        public AccessSet(IReadOnlyList<AccessDeclaration>? declarations)
        {
            Declarations = ContractCollections.Freeze(declarations);
        }

        public IReadOnlyList<AccessDeclaration> Declarations { get; }
    }

    /// <summary>Output-slot schema declared by a capability contract (05 s3).</summary>
    public sealed class OutputSlotSchema
    {
        public OutputSlotSchema(SlotId slot, SchemaRef schema)
        {
            Slot = slot;
            Schema = schema;
        }

        public SlotId Slot { get; }

        public SchemaRef Schema { get; }
    }

    /// <summary>Per-slot composition policy plus reducer key/version (05 s3).</summary>
    public sealed class SlotCompositionPolicy
    {
        public SlotCompositionPolicy(SlotId slot, CompositionPolicy policy, FactoryKey reducer)
        {
            Slot = slot;
            Policy = policy;
            Reducer = reducer;
        }

        public SlotId Slot { get; }

        public CompositionPolicy Policy { get; }

        public FactoryKey Reducer { get; }
    }

    /// <summary>Instance-level explicit service selection inside the visibility boundary (P-011).</summary>
    public readonly struct ServiceSelection
    {
        public readonly ContractRef Contract;
        public readonly ProviderInstallationId Provider;

        public ServiceSelection(ContractRef contract, ProviderInstallationId provider)
        {
            Contract = contract;
            Provider = provider;
        }

        public override string ToString() => Contract.ToString() + " -> " + Provider.ToString();
    }

    /// <summary>Field-level ownership inside one physical component (P-022).</summary>
    public readonly struct FieldOwnership
    {
        public readonly SchemaRef ComponentSchema;
        public readonly Id128 FieldKey;

        public FieldOwnership(SchemaRef componentSchema, Id128 fieldKey)
        {
            ComponentSchema = componentSchema;
            FieldKey = fieldKey;
        }

        public override string ToString() => ComponentSchema.ToString() + "#" + FieldKey.ToString();
    }

    /// <summary>Asset adapter descriptor referenced by a target recipe (P-020).</summary>
    public readonly struct AssetAdapterDescriptor
    {
        public readonly Id128 AdapterId;
        public readonly uint Version;

        public AssetAdapterDescriptor(Id128 adapterId, uint version)
        {
            AdapterId = adapterId;
            Version = version;
        }

        public override string ToString() => AdapterId.ToString() + "@" + Version.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Named service/capability isolation set; <see cref="AllContracts"/> means `*` (P-016).</summary>
    public sealed class IsolationSet
    {
        public IsolationSet(bool allContracts, IReadOnlyList<Id128>? contracts)
        {
            AllContracts = allContracts;
            Contracts = ContractCollections.Freeze(contracts);
        }

        public bool AllContracts { get; }

        public IReadOnlyList<Id128> Contracts { get; }
    }

    /// <summary>
    /// Exclusion of one capability, rule or provider on one target or scope subtree (P-016).
    /// A default (zero) scope or target means that selector is unset.
    /// </summary>
    public readonly struct ExclusionRule
    {
        public readonly ExclusionTargetKind Kind;
        public readonly Id128 TargetId;
        public readonly ScopeId AtScope;
        public readonly TargetId AtTarget;
        public readonly bool AppliesToSubtree;

        public ExclusionRule(
            ExclusionTargetKind kind,
            Id128 targetId,
            ScopeId atScope,
            TargetId atTarget,
            bool appliesToSubtree)
        {
            Kind = kind;
            TargetId = targetId;
            AtScope = atScope;
            AtTarget = atTarget;
            AppliesToSubtree = appliesToSubtree;
        }

        public bool HasScope => !AtScope.IsDefault;

        public bool HasTarget => !AtTarget.IsDefault;

        public override string ToString() => Kind.ToString() + ":" + TargetId.ToString();
    }
}

// Test-only reference seam for the shared GameCore.Contracts surface (see TestOnlyMarker.cs).
// Generated-shape source: emitted from the identity/version tables in docs/game-core/05-contracts-and-data-model.md
// and P-004/P-005 in docs/game-core/00-core-protocols.md. GC-003 replaces it with catalog-generated
// output that must reproduce this surface; never hand-edit members.
#nullable enable
using System;

namespace GameCore.Contracts
{
    /// <summary>Stable 128-bit world-definition identity (P-004).</summary>
    public readonly struct WorldDefinitionId : IEquatable<WorldDefinitionId>, IComparable<WorldDefinitionId>
    {
        public readonly Id128 Value;

        public WorldDefinitionId(Id128 value)
        {
            Value = value;
        }

        public static WorldDefinitionId FromRaw(ulong high, ulong low) => new WorldDefinitionId(new Id128(high, low));

        /// <summary>Default zero IDs are invalid catalog identities (05 s2).</summary>
        public bool IsDefault => Value.IsDefault;

        public bool Equals(WorldDefinitionId other) => Value.Equals(other.Value);
        public override bool Equals(object? obj) => obj is WorldDefinitionId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(WorldDefinitionId other) => Value.CompareTo(other.Value);
        public static bool operator ==(WorldDefinitionId left, WorldDefinitionId right) => left.Equals(right);
        public static bool operator !=(WorldDefinitionId left, WorldDefinitionId right) => !left.Equals(right);
        public override string ToString() => "WorldDefinitionId(" + Value.ToString() + ")";
    }

    /// <summary>Stable 128-bit scope identity (P-004).</summary>
    public readonly struct ScopeId : IEquatable<ScopeId>, IComparable<ScopeId>
    {
        public readonly Id128 Value;

        public ScopeId(Id128 value)
        {
            Value = value;
        }

        public static ScopeId FromRaw(ulong high, ulong low) => new ScopeId(new Id128(high, low));

        /// <summary>Default zero IDs are invalid catalog identities (05 s2).</summary>
        public bool IsDefault => Value.IsDefault;

        public bool Equals(ScopeId other) => Value.Equals(other.Value);
        public override bool Equals(object? obj) => obj is ScopeId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(ScopeId other) => Value.CompareTo(other.Value);
        public static bool operator ==(ScopeId left, ScopeId right) => left.Equals(right);
        public static bool operator !=(ScopeId left, ScopeId right) => !left.Equals(right);
        public override string ToString() => "ScopeId(" + Value.ToString() + ")";
    }

    /// <summary>Stable 128-bit target identity (P-004).</summary>
    public readonly struct TargetId : IEquatable<TargetId>, IComparable<TargetId>
    {
        public readonly Id128 Value;

        public TargetId(Id128 value)
        {
            Value = value;
        }

        public static TargetId FromRaw(ulong high, ulong low) => new TargetId(new Id128(high, low));

        /// <summary>Default zero IDs are invalid catalog identities (05 s2).</summary>
        public bool IsDefault => Value.IsDefault;

        public bool Equals(TargetId other) => Value.Equals(other.Value);
        public override bool Equals(object? obj) => obj is TargetId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(TargetId other) => Value.CompareTo(other.Value);
        public static bool operator ==(TargetId left, TargetId right) => left.Equals(right);
        public static bool operator !=(TargetId left, TargetId right) => !left.Equals(right);
        public override string ToString() => "TargetId(" + Value.ToString() + ")";
    }

    /// <summary>Stable 128-bit state-slot declaration identity (05 StateSlotKey/StateSlotSpec).</summary>
    public readonly struct SlotId : IEquatable<SlotId>, IComparable<SlotId>
    {
        public readonly Id128 Value;

        public SlotId(Id128 value)
        {
            Value = value;
        }

        public static SlotId FromRaw(ulong high, ulong low) => new SlotId(new Id128(high, low));

        /// <summary>Default zero IDs are invalid catalog identities (05 s2).</summary>
        public bool IsDefault => Value.IsDefault;

        public bool Equals(SlotId other) => Value.Equals(other.Value);
        public override bool Equals(object? obj) => obj is SlotId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(SlotId other) => Value.CompareTo(other.Value);
        public static bool operator ==(SlotId left, SlotId right) => left.Equals(right);
        public static bool operator !=(SlotId left, SlotId right) => !left.Equals(right);
        public override string ToString() => "SlotId(" + Value.ToString() + ")";
    }

    /// <summary>Stable 128-bit plugin-type identity (P-004).</summary>
    public readonly struct PluginTypeId : IEquatable<PluginTypeId>, IComparable<PluginTypeId>
    {
        public readonly Id128 Value;

        public PluginTypeId(Id128 value)
        {
            Value = value;
        }

        public static PluginTypeId FromRaw(ulong high, ulong low) => new PluginTypeId(new Id128(high, low));

        /// <summary>Default zero IDs are invalid catalog identities (05 s2).</summary>
        public bool IsDefault => Value.IsDefault;

        public bool Equals(PluginTypeId other) => Value.Equals(other.Value);
        public override bool Equals(object? obj) => obj is PluginTypeId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(PluginTypeId other) => Value.CompareTo(other.Value);
        public static bool operator ==(PluginTypeId left, PluginTypeId right) => left.Equals(right);
        public static bool operator !=(PluginTypeId left, PluginTypeId right) => !left.Equals(right);
        public override string ToString() => "PluginTypeId(" + Value.ToString() + ")";
    }

    /// <summary>Stable 128-bit plugin-instance identity (P-004).</summary>
    public readonly struct PluginInstanceId : IEquatable<PluginInstanceId>, IComparable<PluginInstanceId>
    {
        public readonly Id128 Value;

        public PluginInstanceId(Id128 value)
        {
            Value = value;
        }

        public static PluginInstanceId FromRaw(ulong high, ulong low) => new PluginInstanceId(new Id128(high, low));

        /// <summary>Default zero IDs are invalid catalog identities (05 s2).</summary>
        public bool IsDefault => Value.IsDefault;

        public bool Equals(PluginInstanceId other) => Value.Equals(other.Value);
        public override bool Equals(object? obj) => obj is PluginInstanceId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(PluginInstanceId other) => Value.CompareTo(other.Value);
        public static bool operator ==(PluginInstanceId left, PluginInstanceId right) => left.Equals(right);
        public static bool operator !=(PluginInstanceId left, PluginInstanceId right) => !left.Equals(right);
        public override string ToString() => "PluginInstanceId(" + Value.ToString() + ")";
    }

    /// <summary>Stable 128-bit capability identity (P-004).</summary>
    public readonly struct CapabilityId : IEquatable<CapabilityId>, IComparable<CapabilityId>
    {
        public readonly Id128 Value;

        public CapabilityId(Id128 value)
        {
            Value = value;
        }

        public static CapabilityId FromRaw(ulong high, ulong low) => new CapabilityId(new Id128(high, low));

        /// <summary>Default zero IDs are invalid catalog identities (05 s2).</summary>
        public bool IsDefault => Value.IsDefault;

        public bool Equals(CapabilityId other) => Value.Equals(other.Value);
        public override bool Equals(object? obj) => obj is CapabilityId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(CapabilityId other) => Value.CompareTo(other.Value);
        public static bool operator ==(CapabilityId left, CapabilityId right) => left.Equals(right);
        public static bool operator !=(CapabilityId left, CapabilityId right) => !left.Equals(right);
        public override string ToString() => "CapabilityId(" + Value.ToString() + ")";
    }

    /// <summary>Stable 128-bit schema identity (P-004).</summary>
    public readonly struct SchemaId : IEquatable<SchemaId>, IComparable<SchemaId>
    {
        public readonly Id128 Value;

        public SchemaId(Id128 value)
        {
            Value = value;
        }

        public static SchemaId FromRaw(ulong high, ulong low) => new SchemaId(new Id128(high, low));

        /// <summary>Default zero IDs are invalid catalog identities (05 s2).</summary>
        public bool IsDefault => Value.IsDefault;

        public bool Equals(SchemaId other) => Value.Equals(other.Value);
        public override bool Equals(object? obj) => obj is SchemaId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(SchemaId other) => Value.CompareTo(other.Value);
        public static bool operator ==(SchemaId left, SchemaId right) => left.Equals(right);
        public static bool operator !=(SchemaId left, SchemaId right) => !left.Equals(right);
        public override string ToString() => "SchemaId(" + Value.ToString() + ")";
    }

    /// <summary>Stable 128-bit logical state-owner identity (P-004).</summary>
    public readonly struct OwnerId : IEquatable<OwnerId>, IComparable<OwnerId>
    {
        public readonly Id128 Value;

        public OwnerId(Id128 value)
        {
            Value = value;
        }

        public static OwnerId FromRaw(ulong high, ulong low) => new OwnerId(new Id128(high, low));

        /// <summary>Default zero IDs are invalid catalog identities (05 s2).</summary>
        public bool IsDefault => Value.IsDefault;

        public bool Equals(OwnerId other) => Value.Equals(other.Value);
        public override bool Equals(object? obj) => obj is OwnerId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(OwnerId other) => Value.CompareTo(other.Value);
        public static bool operator ==(OwnerId left, OwnerId right) => left.Equals(right);
        public static bool operator !=(OwnerId left, OwnerId right) => !left.Equals(right);
        public override string ToString() => "OwnerId(" + Value.ToString() + ")";
    }

    /// <summary>Stable 128-bit execution-stage identity (P-004).</summary>
    public readonly struct StageId : IEquatable<StageId>, IComparable<StageId>
    {
        public readonly Id128 Value;

        public StageId(Id128 value)
        {
            Value = value;
        }

        public static StageId FromRaw(ulong high, ulong low) => new StageId(new Id128(high, low));

        /// <summary>Default zero IDs are invalid catalog identities (05 s2).</summary>
        public bool IsDefault => Value.IsDefault;

        public bool Equals(StageId other) => Value.Equals(other.Value);
        public override bool Equals(object? obj) => obj is StageId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(StageId other) => Value.CompareTo(other.Value);
        public static bool operator ==(StageId left, StageId right) => left.Equals(right);
        public static bool operator !=(StageId left, StageId right) => !left.Equals(right);
        public override string ToString() => "StageId(" + Value.ToString() + ")";
    }

    /// <summary>Stable 128-bit definition identity (P-004).</summary>
    public readonly struct DefinitionId : IEquatable<DefinitionId>, IComparable<DefinitionId>
    {
        public readonly Id128 Value;

        public DefinitionId(Id128 value)
        {
            Value = value;
        }

        public static DefinitionId FromRaw(ulong high, ulong low) => new DefinitionId(new Id128(high, low));

        /// <summary>Default zero IDs are invalid catalog identities (05 s2).</summary>
        public bool IsDefault => Value.IsDefault;

        public bool Equals(DefinitionId other) => Value.Equals(other.Value);
        public override bool Equals(object? obj) => obj is DefinitionId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(DefinitionId other) => Value.CompareTo(other.Value);
        public static bool operator ==(DefinitionId left, DefinitionId right) => left.Equals(right);
        public static bool operator !=(DefinitionId left, DefinitionId right) => !left.Equals(right);
        public override string ToString() => "DefinitionId(" + Value.ToString() + ")";
    }

    /// <summary>Stable 128-bit derivation-rule identity (05 ContributionKey).</summary>
    public readonly struct RuleId : IEquatable<RuleId>, IComparable<RuleId>
    {
        public readonly Id128 Value;

        public RuleId(Id128 value)
        {
            Value = value;
        }

        public static RuleId FromRaw(ulong high, ulong low) => new RuleId(new Id128(high, low));

        /// <summary>Default zero IDs are invalid catalog identities (05 s2).</summary>
        public bool IsDefault => Value.IsDefault;

        public bool Equals(RuleId other) => Value.Equals(other.Value);
        public override bool Equals(object? obj) => obj is RuleId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(RuleId other) => Value.CompareTo(other.Value);
        public static bool operator ==(RuleId left, RuleId right) => left.Equals(right);
        public static bool operator !=(RuleId left, RuleId right) => !left.Equals(right);
        public override string ToString() => "RuleId(" + Value.ToString() + ")";
    }

    /// <summary>Stable 128-bit provider-installation identity (05 ContributionKey).</summary>
    public readonly struct ProviderInstallationId : IEquatable<ProviderInstallationId>, IComparable<ProviderInstallationId>
    {
        public readonly Id128 Value;

        public ProviderInstallationId(Id128 value)
        {
            Value = value;
        }

        public static ProviderInstallationId FromRaw(ulong high, ulong low) => new ProviderInstallationId(new Id128(high, low));

        /// <summary>Default zero IDs are invalid catalog identities (05 s2).</summary>
        public bool IsDefault => Value.IsDefault;

        public bool Equals(ProviderInstallationId other) => Value.Equals(other.Value);
        public override bool Equals(object? obj) => obj is ProviderInstallationId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(ProviderInstallationId other) => Value.CompareTo(other.Value);
        public static bool operator ==(ProviderInstallationId left, ProviderInstallationId right) => left.Equals(right);
        public static bool operator !=(ProviderInstallationId left, ProviderInstallationId right) => !left.Equals(right);
        public override string ToString() => "ProviderInstallationId(" + Value.ToString() + ")";
    }

    /// <summary>Stable 128-bit buffer-declaration identity (05 BufferSpec).</summary>
    public readonly struct BufferId : IEquatable<BufferId>, IComparable<BufferId>
    {
        public readonly Id128 Value;

        public BufferId(Id128 value)
        {
            Value = value;
        }

        public static BufferId FromRaw(ulong high, ulong low) => new BufferId(new Id128(high, low));

        /// <summary>Default zero IDs are invalid catalog identities (05 s2).</summary>
        public bool IsDefault => Value.IsDefault;

        public bool Equals(BufferId other) => Value.Equals(other.Value);
        public override bool Equals(object? obj) => obj is BufferId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(BufferId other) => Value.CompareTo(other.Value);
        public static bool operator ==(BufferId left, BufferId right) => left.Equals(right);
        public static bool operator !=(BufferId left, BufferId right) => !left.Equals(right);
        public override string ToString() => "BufferId(" + Value.ToString() + ")";
    }

    /// <summary>Stable 128-bit managed-resource key (05 ResourceSpec).</summary>
    public readonly struct ResourceKey : IEquatable<ResourceKey>, IComparable<ResourceKey>
    {
        public readonly Id128 Value;

        public ResourceKey(Id128 value)
        {
            Value = value;
        }

        public static ResourceKey FromRaw(ulong high, ulong low) => new ResourceKey(new Id128(high, low));

        /// <summary>Default zero IDs are invalid catalog identities (05 s2).</summary>
        public bool IsDefault => Value.IsDefault;

        public bool Equals(ResourceKey other) => Value.Equals(other.Value);
        public override bool Equals(object? obj) => obj is ResourceKey other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(ResourceKey other) => Value.CompareTo(other.Value);
        public static bool operator ==(ResourceKey left, ResourceKey right) => left.Equals(right);
        public static bool operator !=(ResourceKey left, ResourceKey right) => !left.Equals(right);
        public override string ToString() => "ResourceKey(" + Value.ToString() + ")";
    }

    /// <summary>Stable 128-bit command-route identity (05 CommandEnvelope).</summary>
    public readonly struct RouteId : IEquatable<RouteId>, IComparable<RouteId>
    {
        public readonly Id128 Value;

        public RouteId(Id128 value)
        {
            Value = value;
        }

        public static RouteId FromRaw(ulong high, ulong low) => new RouteId(new Id128(high, low));

        /// <summary>Default zero IDs are invalid catalog identities (05 s2).</summary>
        public bool IsDefault => Value.IsDefault;

        public bool Equals(RouteId other) => Value.Equals(other.Value);
        public override bool Equals(object? obj) => obj is RouteId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(RouteId other) => Value.CompareTo(other.Value);
        public static bool operator ==(RouteId left, RouteId right) => left.Equals(right);
        public static bool operator !=(RouteId left, RouteId right) => !left.Equals(right);
        public override string ToString() => "RouteId(" + Value.ToString() + ")";
    }
}

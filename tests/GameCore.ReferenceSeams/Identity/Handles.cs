// Test-only reference seam for the shared GameCore.Contracts surface (see TestOnlyMarker.cs).
// Generated-shape source: emitted from the identity/version tables in docs/game-core/05-contracts-and-data-model.md
// and P-004/P-005 in docs/game-core/00-core-protocols.md. GC-003 replaces it with catalog-generated
// output that must reproduce this surface; never hand-edit members.
#nullable enable
using System;
using System.Globalization;

namespace GameCore.Contracts
{
    /// <summary>Fresh 128-bit session identity. Never reused, including on checkpoint restore (P-004).</summary>
    public readonly struct WorldId : IEquatable<WorldId>
    {
        public readonly Id128 Session;

        public WorldId(Id128 session)
        {
            Session = session;
        }

        public bool Equals(WorldId other) =>
            Session.Equals(other.Session);

        public override bool Equals(object? obj) => obj is WorldId other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (Session.GetHashCode());
                return hash;
            }
        }

        public static bool operator ==(WorldId left, WorldId right) => left.Equals(right);
        public static bool operator !=(WorldId left, WorldId right) => !left.Equals(right);

        public override string ToString() => "WorldId(" + Session.ToString() + ")";
    }

    /// <summary>Mutating-operation identity: shared sequence namespace for control operations and commands (P-050).</summary>
    public readonly struct OperationId : IEquatable<OperationId>
    {
        public readonly WorldId World;
        public readonly Id128 IssuerId;
        public readonly ulong IssuerSequence;

        public OperationId(WorldId world, Id128 issuerId, ulong issuerSequence)
        {
            World = world;
            IssuerId = issuerId;
            IssuerSequence = issuerSequence;
        }

        public bool Equals(OperationId other) =>
            World.Equals(other.World)
            && IssuerId.Equals(other.IssuerId)
            && IssuerSequence == other.IssuerSequence;

        public override bool Equals(object? obj) => obj is OperationId other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (World.GetHashCode());
                hash = (hash * 31) + (IssuerId.GetHashCode());
                hash = (hash * 31) + IssuerSequence;
                return hash;
            }
        }

        public static bool operator ==(OperationId left, OperationId right) => left.Equals(right);
        public static bool operator !=(OperationId left, OperationId right) => !left.Equals(right);

        public override string ToString() => "OperationId(" + World.ToString() + ", " + IssuerId.ToString() + ", " + IssuerSequence.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Runtime handle to one target; validate world, generation, liveness and expected category before use (P-005). Not persisted.</summary>
    public readonly struct TargetHandle : IEquatable<TargetHandle>
    {
        public readonly WorldId World;
        public readonly int Slot;
        public readonly ulong Generation;

        public TargetHandle(WorldId world, int slot, ulong generation)
        {
            World = world;
            Slot = slot;
            Generation = generation;
        }

        public bool Equals(TargetHandle other) =>
            World.Equals(other.World)
            && Slot == other.Slot
            && Generation == other.Generation;

        public override bool Equals(object? obj) => obj is TargetHandle other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (World.GetHashCode());
                hash = (hash * 31) + Slot;
                hash = (hash * 31) + Generation;
                return hash;
            }
        }

        public static bool operator ==(TargetHandle left, TargetHandle right) => left.Equals(right);
        public static bool operator !=(TargetHandle left, TargetHandle right) => !left.Equals(right);

        public override string ToString() => "TargetHandle(" + World.ToString() + ", " + Slot.ToString(CultureInfo.InvariantCulture) + ", " + Generation.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Runtime handle to one scope; adds a scope generation to the target-handle shape (P-005).</summary>
    public readonly struct ScopeHandle : IEquatable<ScopeHandle>
    {
        public readonly WorldId World;
        public readonly int Slot;
        public readonly ulong ScopeGeneration;

        public ScopeHandle(WorldId world, int slot, ulong scopeGeneration)
        {
            World = world;
            Slot = slot;
            ScopeGeneration = scopeGeneration;
        }

        public bool Equals(ScopeHandle other) =>
            World.Equals(other.World)
            && Slot == other.Slot
            && ScopeGeneration == other.ScopeGeneration;

        public override bool Equals(object? obj) => obj is ScopeHandle other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (World.GetHashCode());
                hash = (hash * 31) + Slot;
                hash = (hash * 31) + ScopeGeneration;
                return hash;
            }
        }

        public static bool operator ==(ScopeHandle left, ScopeHandle right) => left.Equals(right);
        public static bool operator !=(ScopeHandle left, ScopeHandle right) => !left.Equals(right);

        public override string ToString() => "ScopeHandle(" + World.ToString() + ", " + Slot.ToString(CultureInfo.InvariantCulture) + ", " + ScopeGeneration.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Runtime handle to one installation; adds an installation generation, which changes on unmount/remount (P-005).</summary>
    public readonly struct PluginHandle : IEquatable<PluginHandle>
    {
        public readonly WorldId World;
        public readonly int Slot;
        public readonly ulong InstallationGeneration;

        public PluginHandle(WorldId world, int slot, ulong installationGeneration)
        {
            World = world;
            Slot = slot;
            InstallationGeneration = installationGeneration;
        }

        public bool Equals(PluginHandle other) =>
            World.Equals(other.World)
            && Slot == other.Slot
            && InstallationGeneration == other.InstallationGeneration;

        public override bool Equals(object? obj) => obj is PluginHandle other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (World.GetHashCode());
                hash = (hash * 31) + Slot;
                hash = (hash * 31) + InstallationGeneration;
                return hash;
            }
        }

        public static bool operator ==(PluginHandle left, PluginHandle right) => left.Equals(right);
        public static bool operator !=(PluginHandle left, PluginHandle right) => !left.Equals(right);

        public override string ToString() => "PluginHandle(" + World.ToString() + ", " + Slot.ToString(CultureInfo.InvariantCulture) + ", " + InstallationGeneration.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Immutable observation identity; composition publication may keep the same step (P-006).</summary>
    public readonly struct SnapshotToken : IEquatable<SnapshotToken>
    {
        public readonly WorldId World;
        public readonly ulong AssemblyEpoch;
        public readonly ulong LogicalStepId;

        public SnapshotToken(WorldId world, ulong assemblyEpoch, ulong logicalStepId)
        {
            World = world;
            AssemblyEpoch = assemblyEpoch;
            LogicalStepId = logicalStepId;
        }

        public bool Equals(SnapshotToken other) =>
            World.Equals(other.World)
            && AssemblyEpoch == other.AssemblyEpoch
            && LogicalStepId == other.LogicalStepId;

        public override bool Equals(object? obj) => obj is SnapshotToken other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (World.GetHashCode());
                hash = (hash * 31) + AssemblyEpoch;
                hash = (hash * 31) + LogicalStepId;
                return hash;
            }
        }

        public static bool operator ==(SnapshotToken left, SnapshotToken right) => left.Equals(right);
        public static bool operator !=(SnapshotToken left, SnapshotToken right) => !left.Equals(right);

        public override string ToString() => "SnapshotToken(" + World.ToString() + ", " + AssemblyEpoch.ToString(CultureInfo.InvariantCulture) + ", " + LogicalStepId.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Async work identity; validated both at ingress and at completion dispatch (P-047).</summary>
    public readonly struct AsyncWorkToken : IEquatable<AsyncWorkToken>
    {
        public readonly OperationId Operation;
        public readonly PluginInstanceId PluginInstanceId;
        public readonly ulong InstallationGeneration;
        public readonly ulong ActivationEpoch;
        public readonly uint WorkOrdinal;

        public AsyncWorkToken(
            OperationId operation,
            PluginInstanceId pluginInstanceId,
            ulong installationGeneration,
            ulong activationEpoch,
            uint workOrdinal)
        {
            Operation = operation;
            PluginInstanceId = pluginInstanceId;
            InstallationGeneration = installationGeneration;
            ActivationEpoch = activationEpoch;
            WorkOrdinal = workOrdinal;
        }

        public bool Equals(AsyncWorkToken other) =>
            Operation.Equals(other.Operation)
            && PluginInstanceId.Equals(other.PluginInstanceId)
            && InstallationGeneration == other.InstallationGeneration
            && ActivationEpoch == other.ActivationEpoch
            && WorkOrdinal == other.WorkOrdinal;

        public override bool Equals(object? obj) => obj is AsyncWorkToken other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (Operation.GetHashCode());
                hash = (hash * 31) + (PluginInstanceId.GetHashCode());
                hash = (hash * 31) + InstallationGeneration;
                hash = (hash * 31) + ActivationEpoch;
                hash = (hash * 31) + WorkOrdinal;
                return hash;
            }
        }

        public static bool operator ==(AsyncWorkToken left, AsyncWorkToken right) => left.Equals(right);
        public static bool operator !=(AsyncWorkToken left, AsyncWorkToken right) => !left.Equals(right);

        public override string ToString() => "AsyncWorkToken(" + Operation.ToString() + ", " + PluginInstanceId.ToString() + ", " + InstallationGeneration.ToString(CultureInfo.InvariantCulture) + ", " + ActivationEpoch.ToString(CultureInfo.InvariantCulture) + ", " + WorkOrdinal.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Schema identity plus integer version; content compatibility, not liveness (P-006).</summary>
    public readonly struct SchemaRef : IEquatable<SchemaRef>
    {
        public readonly SchemaId Id;
        public readonly uint Version;

        public SchemaRef(SchemaId id, uint version)
        {
            Id = id;
            Version = version;
        }

        public bool Equals(SchemaRef other) =>
            Id.Equals(other.Id)
            && Version == other.Version;

        public override bool Equals(object? obj) => obj is SchemaRef other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (Id.GetHashCode());
                hash = (hash * 31) + Version;
                return hash;
            }
        }

        public static bool operator ==(SchemaRef left, SchemaRef right) => left.Equals(right);
        public static bool operator !=(SchemaRef left, SchemaRef right) => !left.Equals(right);

        public override string ToString() => "SchemaRef(" + Id.ToString() + ", " + Version.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Immutable revision lookup; does not itself hold an asset lease (05 s2).</summary>
    public readonly struct DefinitionRef : IEquatable<DefinitionRef>
    {
        public readonly DefinitionId Id;
        public readonly SchemaRef Schema;
        public readonly ulong Revision;

        public DefinitionRef(DefinitionId id, SchemaRef schema, ulong revision)
        {
            Id = id;
            Schema = schema;
            Revision = revision;
        }

        public bool Equals(DefinitionRef other) =>
            Id.Equals(other.Id)
            && Schema.Equals(other.Schema)
            && Revision == other.Revision;

        public override bool Equals(object? obj) => obj is DefinitionRef other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (Id.GetHashCode());
                hash = (hash * 31) + (Schema.GetHashCode());
                hash = (hash * 31) + Revision;
                return hash;
            }
        }

        public static bool operator ==(DefinitionRef left, DefinitionRef right) => left.Equals(right);
        public static bool operator !=(DefinitionRef left, DefinitionRef right) => !left.Equals(right);

        public override string ToString() => "DefinitionRef(" + Id.ToString() + ", " + Schema.ToString() + ", " + Revision.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Contribution identity, stable across payload reconfiguration (05 s2).</summary>
    public readonly struct ContributionKey : IEquatable<ContributionKey>
    {
        public readonly ProviderInstallationId Provider;
        public readonly RuleId Rule;
        public readonly TargetId Target;
        public readonly CapabilityId Capability;
        public readonly uint OutputSlot;

        public ContributionKey(
            ProviderInstallationId provider,
            RuleId rule,
            TargetId target,
            CapabilityId capability,
            uint outputSlot)
        {
            Provider = provider;
            Rule = rule;
            Target = target;
            Capability = capability;
            OutputSlot = outputSlot;
        }

        public bool Equals(ContributionKey other) =>
            Provider.Equals(other.Provider)
            && Rule.Equals(other.Rule)
            && Target.Equals(other.Target)
            && Capability.Equals(other.Capability)
            && OutputSlot == other.OutputSlot;

        public override bool Equals(object? obj) => obj is ContributionKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (Provider.GetHashCode());
                hash = (hash * 31) + (Rule.GetHashCode());
                hash = (hash * 31) + (Target.GetHashCode());
                hash = (hash * 31) + (Capability.GetHashCode());
                hash = (hash * 31) + OutputSlot;
                return hash;
            }
        }

        public static bool operator ==(ContributionKey left, ContributionKey right) => left.Equals(right);
        public static bool operator !=(ContributionKey left, ContributionKey right) => !left.Equals(right);

        public override string ToString() => "ContributionKey(" + Provider.ToString() + ", " + Rule.ToString() + ", " + Target.ToString() + ", " + Capability.ToString() + ", " + OutputSlot.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Mutable state lifetime identity, distinct from the provider's contribution key (05 s2).</summary>
    public readonly struct StateSlotKey : IEquatable<StateSlotKey>
    {
        public readonly TargetId Target;
        public readonly OwnerId Owner;
        public readonly SlotId Slot;

        public StateSlotKey(TargetId target, OwnerId owner, SlotId slot)
        {
            Target = target;
            Owner = owner;
            Slot = slot;
        }

        public bool Equals(StateSlotKey other) =>
            Target.Equals(other.Target)
            && Owner.Equals(other.Owner)
            && Slot.Equals(other.Slot);

        public override bool Equals(object? obj) => obj is StateSlotKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (Target.GetHashCode());
                hash = (hash * 31) + (Owner.GetHashCode());
                hash = (hash * 31) + (Slot.GetHashCode());
                return hash;
            }
        }

        public static bool operator ==(StateSlotKey left, StateSlotKey right) => left.Equals(right);
        public static bool operator !=(StateSlotKey left, StateSlotKey right) => !left.Equals(right);

        public override string ToString() => "StateSlotKey(" + Target.ToString() + ", " + Owner.ToString() + ", " + Slot.ToString() + ")";
    }

    /// <summary>Retained committed-event cursor; a lagging reader receives CursorExpired (P-045).</summary>
    public readonly struct EventCursor : IEquatable<EventCursor>
    {
        public readonly WorldId World;
        public readonly ulong Sequence;

        public EventCursor(WorldId world, ulong sequence)
        {
            World = world;
            Sequence = sequence;
        }

        public bool Equals(EventCursor other) =>
            World.Equals(other.World)
            && Sequence == other.Sequence;

        public override bool Equals(object? obj) => obj is EventCursor other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (World.GetHashCode());
                hash = (hash * 31) + Sequence;
                return hash;
            }
        }

        public static bool operator ==(EventCursor left, EventCursor right) => left.Equals(right);
        public static bool operator !=(EventCursor left, EventCursor right) => !left.Equals(right);

        public override string ToString() => "EventCursor(" + World.ToString() + ", " + Sequence.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Service contract identity plus version (P-011).</summary>
    public readonly struct ContractRef : IEquatable<ContractRef>
    {
        public readonly Id128 ContractId;
        public readonly uint Version;

        public ContractRef(Id128 contractId, uint version)
        {
            ContractId = contractId;
            Version = version;
        }

        public bool Equals(ContractRef other) =>
            ContractId.Equals(other.ContractId)
            && Version == other.Version;

        public override bool Equals(object? obj) => obj is ContractRef other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (ContractId.GetHashCode());
                hash = (hash * 31) + Version;
                return hash;
            }
        }

        public static bool operator ==(ContractRef left, ContractRef right) => left.Equals(right);
        public static bool operator !=(ContractRef left, ContractRef right) => !left.Equals(right);

        public override string ToString() => "ContractRef(" + ContractId.ToString() + ", " + Version.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Capability identity plus version (P-004, P-015).</summary>
    public readonly struct CapabilityRef : IEquatable<CapabilityRef>
    {
        public readonly CapabilityId Capability;
        public readonly uint Version;

        public CapabilityRef(CapabilityId capability, uint version)
        {
            Capability = capability;
            Version = version;
        }

        public bool Equals(CapabilityRef other) =>
            Capability.Equals(other.Capability)
            && Version == other.Version;

        public override bool Equals(object? obj) => obj is CapabilityRef other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (Capability.GetHashCode());
                hash = (hash * 31) + Version;
                return hash;
            }
        }

        public static bool operator ==(CapabilityRef left, CapabilityRef right) => left.Equals(right);
        public static bool operator !=(CapabilityRef left, CapabilityRef right) => !left.Equals(right);

        public override string ToString() => "CapabilityRef(" + Capability.ToString() + ", " + Version.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Full explicit target opt-in naming provider installation and capability (P-013).</summary>
    public readonly struct TargetOptIn : IEquatable<TargetOptIn>
    {
        public readonly ProviderInstallationId ProviderInstallationId;
        public readonly CapabilityId CapabilityId;

        public TargetOptIn(ProviderInstallationId providerInstallationId, CapabilityId capabilityId)
        {
            ProviderInstallationId = providerInstallationId;
            CapabilityId = capabilityId;
        }

        public bool Equals(TargetOptIn other) =>
            ProviderInstallationId.Equals(other.ProviderInstallationId)
            && CapabilityId.Equals(other.CapabilityId);

        public override bool Equals(object? obj) => obj is TargetOptIn other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (ProviderInstallationId.GetHashCode());
                hash = (hash * 31) + (CapabilityId.GetHashCode());
                return hash;
            }
        }

        public static bool operator ==(TargetOptIn left, TargetOptIn right) => left.Equals(right);
        public static bool operator !=(TargetOptIn left, TargetOptIn right) => !left.Equals(right);

        public override string ToString() => "TargetOptIn(" + ProviderInstallationId.ToString() + ", " + CapabilityId.ToString() + ")";
    }

    /// <summary>Explicit capability import of one provider installation (P-013).</summary>
    public readonly struct CapabilityImport : IEquatable<CapabilityImport>
    {
        public readonly CapabilityId CapabilityId;
        public readonly ProviderInstallationId ProviderInstallationId;

        public CapabilityImport(CapabilityId capabilityId, ProviderInstallationId providerInstallationId)
        {
            CapabilityId = capabilityId;
            ProviderInstallationId = providerInstallationId;
        }

        public bool Equals(CapabilityImport other) =>
            CapabilityId.Equals(other.CapabilityId)
            && ProviderInstallationId.Equals(other.ProviderInstallationId);

        public override bool Equals(object? obj) => obj is CapabilityImport other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (CapabilityId.GetHashCode());
                hash = (hash * 31) + (ProviderInstallationId.GetHashCode());
                return hash;
            }
        }

        public static bool operator ==(CapabilityImport left, CapabilityImport right) => left.Equals(right);
        public static bool operator !=(CapabilityImport left, CapabilityImport right) => !left.Equals(right);

        public override string ToString() => "CapabilityImport(" + CapabilityId.ToString() + ", " + ProviderInstallationId.ToString() + ")";
    }

    /// <summary>Generated registration/factory key plus its version (05 s3, P-009).</summary>
    public readonly struct FactoryKey : IEquatable<FactoryKey>
    {
        public readonly Id128 RegistrationKey;
        public readonly uint KeyVersion;

        public FactoryKey(Id128 registrationKey, uint keyVersion)
        {
            RegistrationKey = registrationKey;
            KeyVersion = keyVersion;
        }

        public bool Equals(FactoryKey other) =>
            RegistrationKey.Equals(other.RegistrationKey)
            && KeyVersion == other.KeyVersion;

        public override bool Equals(object? obj) => obj is FactoryKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (RegistrationKey.GetHashCode());
                hash = (hash * 31) + KeyVersion;
                return hash;
            }
        }

        public static bool operator ==(FactoryKey left, FactoryKey right) => left.Equals(right);
        public static bool operator !=(FactoryKey left, FactoryKey right) => !left.Equals(right);

        public override string ToString() => "FactoryKey(" + RegistrationKey.ToString() + ", " + KeyVersion.ToString(CultureInfo.InvariantCulture) + ")";
    }
}

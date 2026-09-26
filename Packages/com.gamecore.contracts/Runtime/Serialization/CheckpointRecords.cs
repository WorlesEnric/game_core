// GameCore.Contracts - checkpoint record values, authored for GC-018. Normative sources:
// docs/game-core/00-core-protocols.md P-004 (checkpoint records carry stable 128-bit identities), P-005 (no
// runtime handle, `Entity` index, generation or lease id is ever persisted), P-032 (active and dormant slot state
// is authoritative state and is saved), P-053 (the exact contents of a checkpoint) and 05 s6 (explicit field ids,
// canonical byte order, bounded lengths, checked list counts).
//
// Every record below is the value type of one generated serializer in the checkpoint catalog
// (unity/GameCore.Validation/Catalogs/CheckpointCatalog.catalog.json). Field order is ascending field id, which is
// also the wire order, and each type is immutable so a decoded record cannot be silently edited before it is
// restored. A record uses `ulong` halves rather than the generated identity wrappers because the record is decoded
// from bytes: the wrapper is reconstructed from the halves by the restore path, and an all-zero identity is
// rejected there rather than being smuggled through as a default wrapper (P-004).
//
// Records deliberately carry counts and per-record ordering where a caller needs to check that the document it
// decoded is internally consistent (for example "the header says 12 targets and 12 target records arrived"), so a
// truncated or padded document rejects instead of restoring a partial world (P-053, 05 s6).
#nullable enable
using System;
using System.Globalization;

namespace GameCore.Contracts
{
    /// <summary>The one header record of a checkpoint document: identity, position, counts and queue disposition.</summary>
    public readonly struct HeaderRecordValue
    {
        public readonly ulong WorldDefinitionHigh;
        public readonly ulong WorldDefinitionLow;

        /// <summary>
        /// Session the checkpoint was captured from. It is recorded for diagnosis and for the "old handles never
        /// become valid" check; it is never the restored session, which is a fresh caller-reserved WorldId (P-004, P-049).
        /// </summary>
        public readonly ulong SourceSessionHigh;
        public readonly ulong SourceSessionLow;

        public readonly uint ProtocolMajor;
        public readonly uint ProtocolMinor;
        public readonly uint TemporalModel;
        public readonly ulong StepDurationTicks;
        public readonly ulong TicksPerSecond;
        public readonly uint MaxStepsPerPump;

        /// <summary>1 when the fixed-step clock reads unscaled host time (P-036).</summary>
        public readonly bool UsesUnscaledHostClock;

        public readonly ulong LogicalStep;

        /// <summary>Retained fixed-step debt at the captured boundary (P-036).</summary>
        public readonly ulong TimeDebtTicks;

        /// <summary>Domain seconds advanced by explicit command; a command-driven world starts at zero (P-038).</summary>
        public readonly double DomainSeconds;

        /// <summary>Admitted commands and wakes still waiting for a logical step (P-036, P-037).</summary>
        public readonly ulong PendingDemand;

        public readonly uint PropagationMode;

        /// <summary>Catalog fingerprint of the capturing build; a mismatch is a content-revision rejection (P-028, P-053).</summary>
        public readonly ulong CatalogFingerprintA;
        public readonly ulong CatalogFingerprintB;
        public readonly ulong CatalogFingerprintC;
        public readonly ulong CatalogFingerprintD;

        /// <summary>The capture's queued-external-command disposition (P-053, 06 s7).</summary>
        public readonly uint QueuePolicy;

        /// <summary>Highest host-assigned admission sequence that was sealed into a step before capture (P-037).</summary>
        public readonly ulong AdmissionCutoff;

        /// <summary>How many queued external commands the capture explicitly rejected (P-053).</summary>
        public readonly uint RejectedQueuedCount;

        /// <summary>Sequence of the last committed event, so a restored reader resumes its cursor (P-045).</summary>
        public readonly ulong LastEventSequence;

        public readonly uint ScopeCount;
        public readonly uint InstallCount;
        public readonly uint SelectionCount;
        public readonly uint TargetCount;
        public readonly uint SlotCount;
        public readonly uint GrantCount;
        public readonly uint ClockCount;
        public readonly uint CommandCount;
        public readonly uint MessageCount;
        public readonly uint RngStreamCount;
        public readonly uint CursorCount;

        /// <summary>Published composition revision of the source world at the boundary (P-006).</summary>
        public readonly ulong SourcePublishedRevision;

        /// <summary>Published assembly epoch of the source world at the boundary (P-006).</summary>
        public readonly ulong SourcePublishedEpoch;

        /// <summary>Host clock rate declared by the source world (P-036).</summary>
        public readonly ulong SourceHostTicksPerSecond;

        /// <summary>
        /// Immutable-definition revisions the document depends on. Recorded as a count so a restore can refuse a
        /// document whose definitions it does not carry rather than silently defaulting them (P-054).
        /// </summary>
        public readonly uint ContentRevisionCount;

        public HeaderRecordValue(
            ulong worldDefinitionHigh,
            ulong worldDefinitionLow,
            ulong sourceSessionHigh,
            ulong sourceSessionLow,
            uint protocolMajor,
            uint protocolMinor,
            uint temporalModel,
            ulong stepDurationTicks,
            ulong ticksPerSecond,
            uint maxStepsPerPump,
            bool usesUnscaledHostClock,
            ulong logicalStep,
            ulong timeDebtTicks,
            double domainSeconds,
            ulong pendingDemand,
            uint propagationMode,
            ulong catalogFingerprintA,
            ulong catalogFingerprintB,
            ulong catalogFingerprintC,
            ulong catalogFingerprintD,
            uint queuePolicy,
            ulong admissionCutoff,
            uint rejectedQueuedCount,
            ulong lastEventSequence,
            uint scopeCount,
            uint installCount,
            uint selectionCount,
            uint targetCount,
            uint slotCount,
            uint grantCount,
            uint clockCount,
            uint commandCount,
            uint messageCount,
            uint rngStreamCount,
            uint cursorCount,
            ulong sourcePublishedRevision,
            ulong sourcePublishedEpoch,
            ulong sourceHostTicksPerSecond,
            uint contentRevisionCount)
        {
            WorldDefinitionHigh = worldDefinitionHigh;
            WorldDefinitionLow = worldDefinitionLow;
            SourceSessionHigh = sourceSessionHigh;
            SourceSessionLow = sourceSessionLow;
            ProtocolMajor = protocolMajor;
            ProtocolMinor = protocolMinor;
            TemporalModel = temporalModel;
            StepDurationTicks = stepDurationTicks;
            TicksPerSecond = ticksPerSecond;
            MaxStepsPerPump = maxStepsPerPump;
            UsesUnscaledHostClock = usesUnscaledHostClock;
            LogicalStep = logicalStep;
            TimeDebtTicks = timeDebtTicks;
            DomainSeconds = domainSeconds;
            PendingDemand = pendingDemand;
            PropagationMode = propagationMode;
            CatalogFingerprintA = catalogFingerprintA;
            CatalogFingerprintB = catalogFingerprintB;
            CatalogFingerprintC = catalogFingerprintC;
            CatalogFingerprintD = catalogFingerprintD;
            QueuePolicy = queuePolicy;
            AdmissionCutoff = admissionCutoff;
            RejectedQueuedCount = rejectedQueuedCount;
            LastEventSequence = lastEventSequence;
            ScopeCount = scopeCount;
            InstallCount = installCount;
            SelectionCount = selectionCount;
            TargetCount = targetCount;
            SlotCount = slotCount;
            GrantCount = grantCount;
            ClockCount = clockCount;
            CommandCount = commandCount;
            MessageCount = messageCount;
            RngStreamCount = rngStreamCount;
            CursorCount = cursorCount;
            SourcePublishedRevision = sourcePublishedRevision;
            SourcePublishedEpoch = sourcePublishedEpoch;
            SourceHostTicksPerSecond = sourceHostTicksPerSecond;
            ContentRevisionCount = contentRevisionCount;
        }

        public WorldDefinitionId WorldDefinition => WorldDefinitionId.FromRaw(WorldDefinitionHigh, WorldDefinitionLow);

        public WorldId SourceSession => new WorldId(new Id128(SourceSessionHigh, SourceSessionLow));

        /// <summary>
        /// Enum view of the temporal model field. The cast names its type fully because this type declares a field
        /// whose simple name is the same as the enum type's (P-036).
        /// </summary>
        public GameCore.Contracts.TemporalModel Temporal =>
            (GameCore.Contracts.TemporalModel)TemporalModel;

        /// <summary>Enum view of the propagation-mode field (P-013).</summary>
        public GameCore.Contracts.PropagationMode Propagation =>
            (GameCore.Contracts.PropagationMode)PropagationMode;

        public CheckpointQueuePolicy Policy => (CheckpointQueuePolicy)QueuePolicy;

        public ContentHash CatalogFingerprint => CanonicalId32.Collate(
            CatalogFingerprintA, CatalogFingerprintB, CatalogFingerprintC, CatalogFingerprintD);

        /// <summary>True when the document declares the one supported protocol major/minor (P-055).</summary>
        public bool IsSupportedProtocol =>
            ProtocolMajor == CheckpointFormat.ProtocolMajor && ProtocolMinor == CheckpointFormat.ProtocolMinor;

        /// <summary>
        /// True when every declared count is satisfied by the record counts the document actually delivered. A
        /// document that disagrees is refused rather than partially restored (P-053).
        /// </summary>
        public bool CountsMatch(
            int scopes,
            int installs,
            int selections,
            int targets,
            int slots,
            int grants,
            int clocks,
            int commands,
            int messages,
            int rngStreams,
            int cursors) =>
            (long)ScopeCount == scopes
            && (long)InstallCount == installs
            && (long)SelectionCount == selections
            && (long)TargetCount == targets
            && (long)SlotCount == slots
            && (long)GrantCount == grants
            && (long)ClockCount == clocks
            && (long)CommandCount == commands
            && (long)MessageCount == messages
            && (long)RngStreamCount == rngStreams
            && (long)CursorCount == cursors;

        public override string ToString() =>
            "header(def=" + WorldDefinition.ToString() + ",step=" + LogicalStep.ToString(CultureInfo.InvariantCulture)
            + ",scope=" + ScopeCount.ToString(CultureInfo.InvariantCulture)
            + ",install=" + InstallCount.ToString(CultureInfo.InvariantCulture)
            + ",target=" + TargetCount.ToString(CultureInfo.InvariantCulture)
            + ",slot=" + SlotCount.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>One scope: stable identity, committed parent edge, depth and boundary-member counts (P-010, P-016).</summary>
    public readonly struct ScopeRecordValue
    {
        public readonly ulong ScopeHigh;
        public readonly ulong ScopeLow;
        public readonly ulong ParentHigh;
        public readonly ulong ParentLow;

        /// <summary>Depth below the root; the root is 0 and the registry checks a child's depth against its parent.</summary>
        public readonly uint Depth;

        public readonly uint Mode;

        /// <summary>Installs declared directly at this scope, in canonical order (P-010).</summary>
        public readonly uint InstallCount;

        /// <summary>Grants declared at this scope, in canonical order (P-013, P-016).</summary>
        public readonly uint GrantCount;

        public readonly bool ServiceIsolationAll;
        public readonly bool CapabilityIsolationAll;
        public readonly uint ServiceIsolationCount;
        public readonly uint CapabilityIsolationCount;

        public ScopeRecordValue(
            ulong scopeHigh,
            ulong scopeLow,
            ulong parentHigh,
            ulong parentLow,
            uint depth,
            uint mode,
            uint installCount,
            uint grantCount,
            bool serviceIsolationAll,
            bool capabilityIsolationAll,
            uint serviceIsolationCount,
            uint capabilityIsolationCount)
        {
            ScopeHigh = scopeHigh;
            ScopeLow = scopeLow;
            ParentHigh = parentHigh;
            ParentLow = parentLow;
            Depth = depth;
            Mode = mode;
            InstallCount = installCount;
            GrantCount = grantCount;
            ServiceIsolationAll = serviceIsolationAll;
            CapabilityIsolationAll = capabilityIsolationAll;
            ServiceIsolationCount = serviceIsolationCount;
            CapabilityIsolationCount = capabilityIsolationCount;
        }

        public ScopeId Scope => new ScopeId(new Id128(ScopeHigh, ScopeLow));

        public ScopeId Parent => new ScopeId(new Id128(ParentHigh, ParentLow));

        public PropagationMode Propagation => (PropagationMode)Mode;

        /// <summary>True for the one scope with no parent (P-010).</summary>
        public bool IsRoot => ParentHigh == 0UL && ParentLow == 0UL;

        public override string ToString() =>
            "scope(" + Scope.ToString() + ",depth=" + Depth.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>One installation: identity, configuration revision and lifecycle state (P-004, P-046).</summary>
    public readonly struct InstallRecordValue
    {
        public readonly ulong InstanceHigh;
        public readonly ulong InstanceLow;
        public readonly ulong PluginTypeHigh;
        public readonly ulong PluginTypeLow;
        public readonly ulong ScopeHigh;
        public readonly ulong ScopeLow;

        /// <summary>Immutable configuration revision the installation is mounted at (P-020).</summary>
        public readonly ulong ConfigRevision;

        public readonly ulong ConfigHashA;
        public readonly ulong ConfigHashB;
        public readonly ulong ConfigHashC;
        public readonly ulong ConfigHashD;

        public readonly int Priority;

        /// <summary>Installation generation; changes on unmount/remount, not on reconfigure (P-005).</summary>
        public readonly ulong Generation;

        /// <summary>Activation epoch; changes whenever the installation loses or gains execution authority (P-006).</summary>
        public readonly ulong ActivationEpoch;

        public readonly uint State;

        public readonly uint ConfigFieldCount;

        /// <summary>Canonical encoding of the installation's configuration document (P-020, 05 s6).</summary>
        public readonly byte[]? ConfigBytes;

        public readonly uint SelectionCount;

        /// <summary>
        /// 1 when this installation carries an explicit configuration document. A mounted installation with zero
        /// configuration is distinct from one whose document failed to decode, so the flag is explicit rather than
        /// inferred from a zero-length byte array (05 s6's null semantics).
        /// </summary>
        public readonly bool HasConfigDocument;

        public InstallRecordValue(
            ulong instanceHigh,
            ulong instanceLow,
            ulong pluginTypeHigh,
            ulong pluginTypeLow,
            ulong scopeHigh,
            ulong scopeLow,
            ulong configRevision,
            ulong configHashA,
            ulong configHashB,
            ulong configHashC,
            ulong configHashD,
            int priority,
            ulong generation,
            ulong activationEpoch,
            uint state,
            uint configFieldCount,
            byte[]? configBytes,
            uint selectionCount,
            bool hasConfigDocument)
        {
            InstanceHigh = instanceHigh;
            InstanceLow = instanceLow;
            PluginTypeHigh = pluginTypeHigh;
            PluginTypeLow = pluginTypeLow;
            ScopeHigh = scopeHigh;
            ScopeLow = scopeLow;
            ConfigRevision = configRevision;
            ConfigHashA = configHashA;
            ConfigHashB = configHashB;
            ConfigHashC = configHashC;
            ConfigHashD = configHashD;
            Priority = priority;
            Generation = generation;
            ActivationEpoch = activationEpoch;
            State = state;
            ConfigFieldCount = configFieldCount;
            ConfigBytes = configBytes;
            SelectionCount = selectionCount;
            HasConfigDocument = hasConfigDocument;
        }

        public PluginInstanceId Instance => new PluginInstanceId(new Id128(InstanceHigh, InstanceLow));

        public PluginTypeId PluginType => new PluginTypeId(new Id128(PluginTypeHigh, PluginTypeLow));

        public ScopeId Scope => new ScopeId(new Id128(ScopeHigh, ScopeLow));

        public ContentHash ConfigHash =>
            CanonicalId32.Collate(ConfigHashA, ConfigHashB, ConfigHashC, ConfigHashD);

        public InstallationState Lifecycle => (InstallationState)State;

        public DefinitionRevision Revision => new DefinitionRevision(ConfigRevision);

        public override string ToString() =>
            "install(" + Instance.ToString() + "," + Lifecycle.ToString() + ")";
    }

    /// <summary>One instance-level service selection: which provider an instance chose (P-011).</summary>
    public readonly struct SelectionRecordValue
    {
        public readonly ulong InstanceHigh;
        public readonly ulong InstanceLow;
        public readonly ulong ContractHigh;
        public readonly ulong ContractLow;
        public readonly uint ContractVersion;
        public readonly ulong ProviderHigh;
        public readonly ulong ProviderLow;

        /// <summary>Explicit canonical order of the selections inside one installation (P-008).</summary>
        public readonly uint Order;

        public SelectionRecordValue(
            ulong instanceHigh,
            ulong instanceLow,
            ulong contractHigh,
            ulong contractLow,
            uint contractVersion,
            ulong providerHigh,
            ulong providerLow,
            uint order)
        {
            InstanceHigh = instanceHigh;
            InstanceLow = instanceLow;
            ContractHigh = contractHigh;
            ContractLow = contractLow;
            ContractVersion = contractVersion;
            ProviderHigh = providerHigh;
            ProviderLow = providerLow;
            Order = order;
        }

        public PluginInstanceId Instance => new PluginInstanceId(new Id128(InstanceHigh, InstanceLow));

        public ContractRef Contract => new ContractRef(new Id128(ContractHigh, ContractLow), ContractVersion);

        public ProviderInstallationId Provider => new ProviderInstallationId(new Id128(ProviderHigh, ProviderLow));

        public ServiceSelection ToSelection() => new ServiceSelection(Contract, Provider);

        public override string ToString() => "selection(" + Instance.ToString() + " -> " + Provider.ToString() + ")";
    }

    /// <summary>
    /// One target: its stable identity, owning scope, recipe and the registry slot it occupied in the source
    /// world. The source slot and generation are recorded as evidence that the restored world's indices differ;
    /// they are never used to rebuild an entity mapping (P-004, P-005, P-053).
    /// </summary>
    public readonly struct TargetRecordValue
    {
        public readonly ulong TargetHigh;
        public readonly ulong TargetLow;
        public readonly ulong ScopeHigh;
        public readonly ulong ScopeLow;
        public readonly ulong DefinitionHigh;
        public readonly ulong DefinitionLow;
        public readonly ulong SchemaHigh;
        public readonly ulong SchemaLow;
        public readonly uint SchemaVersion;
        public readonly ulong ContentRevision;

        /// <summary>Source-world registry slot; diagnostic evidence only, never a restored mapping (P-005).</summary>
        public readonly uint SourceSlot;

        /// <summary>Source-world handle generation; diagnostic evidence only (P-005).</summary>
        public readonly ulong SourceGeneration;

        public TargetRecordValue(
            ulong targetHigh,
            ulong targetLow,
            ulong scopeHigh,
            ulong scopeLow,
            ulong definitionHigh,
            ulong definitionLow,
            ulong schemaHigh,
            ulong schemaLow,
            uint schemaVersion,
            ulong contentRevision,
            uint sourceSlot,
            ulong sourceGeneration)
        {
            TargetHigh = targetHigh;
            TargetLow = targetLow;
            ScopeHigh = scopeHigh;
            ScopeLow = scopeLow;
            DefinitionHigh = definitionHigh;
            DefinitionLow = definitionLow;
            SchemaHigh = schemaHigh;
            SchemaLow = schemaLow;
            SchemaVersion = schemaVersion;
            ContentRevision = contentRevision;
            SourceSlot = sourceSlot;
            SourceGeneration = sourceGeneration;
        }

        public TargetId Target => new TargetId(new Id128(TargetHigh, TargetLow));

        public ScopeId Scope => new ScopeId(new Id128(ScopeHigh, ScopeLow));

        public DefinitionRef Recipe =>
            new DefinitionRef(
                new DefinitionId(new Id128(DefinitionHigh, DefinitionLow)),
                new SchemaRef(new SchemaId(new Id128(SchemaHigh, SchemaLow)), SchemaVersion),
                new DefinitionRevision(ContentRevision));

        public override string ToString() =>
            "target(" + Target.ToString() + ",slot=" + SourceSlot.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// One authoritative owner state slot of one target, active or dormant (P-032, P-053). A dormant slot is
    /// `Active == false` and is still authoritative state, so a checkpoint that dropped it would silently delete
    /// durable progress.
    /// </summary>
    public readonly struct SlotRecordValue
    {
        public readonly ulong TargetHigh;
        public readonly ulong TargetLow;
        public readonly ulong OwnerHigh;
        public readonly ulong OwnerLow;
        public readonly ulong SlotHigh;
        public readonly ulong SlotLow;
        public readonly uint SchemaVersion;
        public readonly int Value;

        /// <summary>True while an active owner writes this slot; false when it is dormant but retained (P-032).</summary>
        public readonly bool Active;

        public SlotRecordValue(
            ulong targetHigh,
            ulong targetLow,
            ulong ownerHigh,
            ulong ownerLow,
            ulong slotHigh,
            ulong slotLow,
            uint schemaVersion,
            int value,
            bool active)
        {
            TargetHigh = targetHigh;
            TargetLow = targetLow;
            OwnerHigh = ownerHigh;
            OwnerLow = ownerLow;
            SlotHigh = slotHigh;
            SlotLow = slotLow;
            SchemaVersion = schemaVersion;
            Value = value;
            Active = active;
        }

        public StateSlotKey Key =>
            new StateSlotKey(
                new TargetId(new Id128(TargetHigh, TargetLow)),
                new OwnerId(new Id128(OwnerHigh, OwnerLow)),
                new SlotId(new Id128(SlotHigh, SlotLow)));

        public override string ToString() =>
            "slot(" + Key.ToString() + "@" + SchemaVersion.ToString(CultureInfo.InvariantCulture)
            + (Active ? ",active" : ",dormant") + ")";
    }

    /// <summary>What one grant record declares: P-013 explicit import or opt-in, P-016 exclusion, P-011 override.</summary>
    public enum GrantKind
    {
        /// <summary>P-013: a capability imported at a scope in Conservative mode.</summary>
        ScopeImport = 0,

        /// <summary>P-013: a complete explicit target opt-in naming provider and capability.</summary>
        TargetOptIn = 1,

        /// <summary>P-016: an exclusion rule at a scope or one target.</summary>
        Exclusion = 2,

        /// <summary>P-016: a named contract member of a scope's service-isolation set.</summary>
        ServiceIsolationMember = 3,

        /// <summary>P-016: a named capability member of a scope's capability-isolation set.</summary>
        CapabilityIsolationMember = 4,
    }

    /// <summary>
    /// One explicit propagation grant: an import, a target opt-in, an exclusion or one isolation member. The
    /// protocol's denial and opt-in data are composition inputs, so they are saved rather than re-derived from the
    /// catalog: a restored world must not silently reopen a boundary (P-016, P-053).
    /// </summary>
    public readonly struct GrantRecordValue
    {
        public readonly uint Kind;
        public readonly ulong ScopeHigh;
        public readonly ulong ScopeLow;
        public readonly ulong TargetHigh;
        public readonly ulong TargetLow;
        public readonly ulong CapabilityHigh;
        public readonly ulong CapabilityLow;
        public readonly uint CapabilityVersion;
        public readonly ulong ProviderHigh;
        public readonly ulong ProviderLow;
        public readonly ulong RuleHigh;
        public readonly ulong RuleLow;
        public readonly ulong ContractHigh;
        public readonly ulong ContractLow;
        public readonly uint ContractVersion;

        /// <summary>The excluded or isolated subject identity (capability, rule or provider id) (P-016).</summary>
        public readonly ulong SubjectHigh;
        public readonly ulong SubjectLow;

        /// <summary>An exclusion that applies to a whole subtree rather than one target (P-016).</summary>
        public readonly bool AppliesToSubtree;

        /// <summary>`*` for an isolation set that names every contract (P-016).</summary>
        public readonly bool AllContracts;

        public readonly uint ExclusionKind;

        /// <summary>Canonical order of this grant among the grants of its own key (P-008).</summary>
        public readonly uint Order;

        public GrantRecordValue(
            uint kind,
            ulong scopeHigh,
            ulong scopeLow,
            ulong targetHigh,
            ulong targetLow,
            ulong capabilityHigh,
            ulong capabilityLow,
            uint capabilityVersion,
            ulong providerHigh,
            ulong providerLow,
            ulong ruleHigh,
            ulong ruleLow,
            ulong contractHigh,
            ulong contractLow,
            uint contractVersion,
            ulong subjectHigh,
            ulong subjectLow,
            bool appliesToSubtree,
            bool allContracts,
            uint exclusionKind,
            uint order)
        {
            Kind = kind;
            ScopeHigh = scopeHigh;
            ScopeLow = scopeLow;
            TargetHigh = targetHigh;
            TargetLow = targetLow;
            CapabilityHigh = capabilityHigh;
            CapabilityLow = capabilityLow;
            CapabilityVersion = capabilityVersion;
            ProviderHigh = providerHigh;
            ProviderLow = providerLow;
            RuleHigh = ruleHigh;
            RuleLow = ruleLow;
            ContractHigh = contractHigh;
            ContractLow = contractLow;
            ContractVersion = contractVersion;
            SubjectHigh = subjectHigh;
            SubjectLow = subjectLow;
            AppliesToSubtree = appliesToSubtree;
            AllContracts = allContracts;
            ExclusionKind = exclusionKind;
            Order = order;
        }

        public GrantKind Grant => (GrantKind)Kind;

        public ScopeId Scope => new ScopeId(new Id128(ScopeHigh, ScopeLow));

        public TargetId Target => new TargetId(new Id128(TargetHigh, TargetLow));

        public CapabilityId Capability => new CapabilityId(new Id128(CapabilityHigh, CapabilityLow));

        public ProviderInstallationId Provider => new ProviderInstallationId(new Id128(ProviderHigh, ProviderLow));

        public RuleId Rule => new RuleId(new Id128(RuleHigh, RuleLow));

        public ContractRef Contract => new ContractRef(new Id128(ContractHigh, ContractLow), ContractVersion);

        public Id128 Subject => new Id128(SubjectHigh, SubjectLow);

        public ExclusionTargetKind Exclusion => (ExclusionTargetKind)ExclusionKind;

        /// <summary>The imported capability of a <see cref="GrantKind.ScopeImport"/> (P-013).</summary>
        public CapabilityImport ToImport() => new CapabilityImport(Capability, Provider);

        /// <summary>The exclusion rule of an <see cref="GrantKind.Exclusion"/> (P-016).</summary>
        public ExclusionRule ToExclusion() =>
            new ExclusionRule(Exclusion, Subject, Scope, Target, AppliesToSubtree);

        public override string ToString() => "grant(" + Grant.ToString() + "," + Capability.ToString() + ")";
    }

    /// <summary>Which of the two clock facts one record carries; P-038 and P-053.</summary>
    public enum ClockRowKind
    {
        /// <summary>A registered plugin clock declaration.</summary>
        Declaration = 0,

        /// <summary>One scheduled wake of a clock and its remaining delay.</summary>
        Wake = 1,
    }

    /// <summary>How much of a wake's delay remains; mirrors the runtime's wake state without inventing one (P-038).</summary>
    public enum ClockWakeState
    {
        Pending = 0,
        Due = 1,
        Consumed = 2,
        Cancelled = 3,
    }

    /// <summary>
    /// One plugin clock declaration or one scheduled wake, with its remaining delay. A transient clock does not
    /// persist (<c>Persists == false</c>) and is re-registered by its declaration on restore (P-038, P-053).
    /// </summary>
    public readonly struct ClockRecordValue
    {
        public readonly uint RowKind;
        public readonly ulong ClockHigh;
        public readonly ulong ClockLow;
        public readonly uint ClockKind;
        public readonly uint PausePolicy;
        public readonly bool Persists;
        public readonly ulong WakeHigh;
        public readonly ulong WakeLow;
        public readonly ulong PayloadSchemaHigh;
        public readonly ulong PayloadSchemaLow;
        public readonly uint PayloadSchemaVersion;
        public readonly ulong ScheduledAtSequence;
        public readonly ulong RemainingSteps;
        public readonly ulong RemainingTicks;
        public readonly uint WakeState;
        public readonly uint Order;

        public ClockRecordValue(
            uint rowKind,
            ulong clockHigh,
            ulong clockLow,
            uint clockKind,
            uint pausePolicy,
            bool persists,
            ulong wakeHigh,
            ulong wakeLow,
            ulong payloadSchemaHigh,
            ulong payloadSchemaLow,
            uint payloadSchemaVersion,
            ulong scheduledAtSequence,
            ulong remainingSteps,
            ulong remainingTicks,
            uint wakeState,
            uint order)
        {
            RowKind = rowKind;
            ClockHigh = clockHigh;
            ClockLow = clockLow;
            ClockKind = clockKind;
            PausePolicy = pausePolicy;
            Persists = persists;
            WakeHigh = wakeHigh;
            WakeLow = wakeLow;
            PayloadSchemaHigh = payloadSchemaHigh;
            PayloadSchemaLow = payloadSchemaLow;
            PayloadSchemaVersion = payloadSchemaVersion;
            ScheduledAtSequence = scheduledAtSequence;
            RemainingSteps = remainingSteps;
            RemainingTicks = remainingTicks;
            WakeState = wakeState;
            Order = order;
        }

        public ClockRowKind Row => (ClockRowKind)RowKind;

        public Id128 ClockId => new Id128(ClockHigh, ClockLow);

        public Id128 WakeId => new Id128(WakeHigh, WakeLow);

        /// <summary>Declared clock kind; meaningful only for a <see cref="ClockRowKind.Declaration"/> row.</summary>
        public uint DeclaredClockKind => ClockKind;

        /// <summary>Declared pause policy; meaningful only for a declaration row.</summary>
        public uint DeclaredPausePolicy => PausePolicy;

        public SchemaRef PayloadSchema =>
            new SchemaRef(new SchemaId(new Id128(PayloadSchemaHigh, PayloadSchemaLow)), PayloadSchemaVersion);

        public ClockWakeState State => (ClockWakeState)WakeState;

        public bool IsDeclaration => Row == ClockRowKind.Declaration;

        public override string ToString() => "clock(" + Row.ToString() + "," + ClockId.ToString() + ")";
    }

    /// <summary>
    /// One admitted but not yet executed external command. When the capture's policy is
    /// <see cref="CheckpointQueuePolicy.IncludeQueued"/> these records re-admit into the restored world at the same
    /// request identity, so duplicate suppression still holds across the checkpoint (P-037, P-050, P-053).
    /// </summary>
    public readonly struct CommandRecordValue
    {
        public readonly ulong IssuerHigh;
        public readonly ulong IssuerLow;
        public readonly ulong IssuerSequence;
        public readonly ulong RouteHigh;
        public readonly ulong RouteLow;
        public readonly ulong TargetHigh;
        public readonly ulong TargetLow;
        public readonly ulong SchemaHigh;
        public readonly ulong SchemaLow;
        public readonly uint SchemaVersion;
        public readonly ulong AdmittedStep;
        public readonly ulong AdmittedEpoch;
        public readonly ulong AdmissionSequence;
        public readonly uint OrderOrdinal;
        public readonly uint OriginKind;
        public readonly ulong InputHashA;
        public readonly ulong InputHashB;
        public readonly ulong InputHashC;
        public readonly ulong InputHashD;
        public readonly byte[]? Payload;

        public CommandRecordValue(
            ulong issuerHigh,
            ulong issuerLow,
            ulong issuerSequence,
            ulong routeHigh,
            ulong routeLow,
            ulong targetHigh,
            ulong targetLow,
            ulong schemaHigh,
            ulong schemaLow,
            uint schemaVersion,
            ulong admittedStep,
            ulong admittedEpoch,
            ulong admissionSequence,
            uint orderOrdinal,
            uint originKind,
            ulong inputHashA,
            ulong inputHashB,
            ulong inputHashC,
            ulong inputHashD,
            byte[]? payload)
        {
            IssuerHigh = issuerHigh;
            IssuerLow = issuerLow;
            IssuerSequence = issuerSequence;
            RouteHigh = routeHigh;
            RouteLow = routeLow;
            TargetHigh = targetHigh;
            TargetLow = targetLow;
            SchemaHigh = schemaHigh;
            SchemaLow = schemaLow;
            SchemaVersion = schemaVersion;
            AdmittedStep = admittedStep;
            AdmittedEpoch = admittedEpoch;
            AdmissionSequence = admissionSequence;
            OrderOrdinal = orderOrdinal;
            OriginKind = originKind;
            InputHashA = inputHashA;
            InputHashB = inputHashB;
            InputHashC = inputHashC;
            InputHashD = inputHashD;
            Payload = payload;
        }

        /// <summary>Route id the command was admitted for.</summary>
        public RouteId Route => new RouteId(new Id128(RouteHigh, RouteLow));

        public TargetId Target => new TargetId(new Id128(TargetHigh, TargetLow));

        public SchemaRef Schema => new SchemaRef(new SchemaId(new Id128(SchemaHigh, SchemaLow)), SchemaVersion);

        /// <summary>Issuing identity of the admitted command; the world half is supplied by the restore target (P-050).</summary>
        public Id128 IssuerId => new Id128(IssuerHigh, IssuerLow);

        /// <summary>
        /// The request identity inside one world. The document does not carry a session, because a checkpoint's
        /// records are captured from one world and re-admitted into another; the restored world supplies its own
        /// fresh id, so an old request identity can never address the new session (P-004, P-049, P-050).
        /// </summary>
        public OperationId RequestIdIn(WorldId world) => new OperationId(world, IssuerId, IssuerSequence);

        /// <summary>Canonical input hash of the admitted command document (P-050).</summary>
        public ContentHash InputHash => CanonicalId32.Collate(InputHashA, InputHashB, InputHashC, InputHashD);

        public FrozenPayload Frozen => new FrozenPayload(Payload ?? Array.Empty<byte>());

        public override string ToString() =>
            "command(" + Route.ToString() + ",seq=" + IssuerSequence.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// One bounded next-step message retained across the boundary (P-043, P-053). Its payload bytes are carried
    /// beside the row, so a restored world re-stamps the arena instead of replaying a stale offset.
    /// </summary>
    public readonly struct MessageRecordValue
    {
        public readonly ulong Step;
        public readonly ulong Epoch;
        public readonly ulong RequestIssuerHigh;
        public readonly ulong RequestIssuerLow;
        public readonly ulong RequestSequence;
        public readonly ulong RouteHigh;
        public readonly ulong RouteLow;
        public readonly ulong OwnerHigh;
        public readonly ulong OwnerLow;
        public readonly ulong TargetHigh;
        public readonly ulong TargetLow;
        public readonly ulong PayloadSchemaHigh;
        public readonly ulong PayloadSchemaLow;
        public readonly uint PayloadSchemaVersion;
        public readonly uint MessageKind;
        public readonly ulong OrderAdmitted;
        public readonly uint OrderOrdinal;
        public readonly ulong OriginKeyHigh;
        public readonly ulong OriginKeyLow;
        public readonly ulong ProducerKeyHigh;
        public readonly ulong ProducerKeyLow;
        public readonly uint ProducerKeyVersion;
        public readonly ulong BufferHigh;
        public readonly ulong BufferLow;
        public readonly bool HasPayload;
        public readonly bool HasRequest;
        public readonly bool IsOutcome;
        public readonly byte[]? Payload;

        public MessageRecordValue(
            ulong step,
            ulong epoch,
            ulong requestIssuerHigh,
            ulong requestIssuerLow,
            ulong requestSequence,
            ulong routeHigh,
            ulong routeLow,
            ulong ownerHigh,
            ulong ownerLow,
            ulong targetHigh,
            ulong targetLow,
            ulong payloadSchemaHigh,
            ulong payloadSchemaLow,
            uint payloadSchemaVersion,
            uint messageKind,
            ulong orderAdmitted,
            uint orderOrdinal,
            ulong originKeyHigh,
            ulong originKeyLow,
            ulong producerKeyHigh,
            ulong producerKeyLow,
            uint producerKeyVersion,
            ulong bufferHigh,
            ulong bufferLow,
            bool hasPayload,
            bool hasRequest,
            bool isOutcome,
            byte[]? payload)
        {
            Step = step;
            Epoch = epoch;
            RequestIssuerHigh = requestIssuerHigh;
            RequestIssuerLow = requestIssuerLow;
            RequestSequence = requestSequence;
            RouteHigh = routeHigh;
            RouteLow = routeLow;
            OwnerHigh = ownerHigh;
            OwnerLow = ownerLow;
            TargetHigh = targetHigh;
            TargetLow = targetLow;
            PayloadSchemaHigh = payloadSchemaHigh;
            PayloadSchemaLow = payloadSchemaLow;
            PayloadSchemaVersion = payloadSchemaVersion;
            MessageKind = messageKind;
            OrderAdmitted = orderAdmitted;
            OrderOrdinal = orderOrdinal;
            OriginKeyHigh = originKeyHigh;
            OriginKeyLow = originKeyLow;
            ProducerKeyHigh = producerKeyHigh;
            ProducerKeyLow = producerKeyLow;
            ProducerKeyVersion = producerKeyVersion;
            BufferHigh = bufferHigh;
            BufferLow = bufferLow;
            HasPayload = hasPayload;
            HasRequest = hasRequest;
            IsOutcome = isOutcome;
            Payload = payload;
        }

        public RouteId Route => new RouteId(new Id128(RouteHigh, RouteLow));

        public OwnerId Owner => new OwnerId(new Id128(OwnerHigh, OwnerLow));

        public TargetId Target => new TargetId(new Id128(TargetHigh, TargetLow));

        public BufferId Buffer => new BufferId(new Id128(BufferHigh, BufferLow));

        public SchemaRef PayloadSchema =>
            new SchemaRef(new SchemaId(new Id128(PayloadSchemaHigh, PayloadSchemaLow)), PayloadSchemaVersion);

        public FactoryKey Producer => new FactoryKey(new Id128(ProducerKeyHigh, ProducerKeyLow), ProducerKeyVersion);

        public Id128 OriginKey => new Id128(OriginKeyHigh, OriginKeyLow);

        public override string ToString() =>
            "message(" + Route.ToString() + ",step=" + Step.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// One deterministic random stream's position (P-008, P-053). V1 declares streams rather than drawing from a
    /// hidden global generator, so repeatability given identical seeds is a property of the recorded state.
    /// </summary>
    public readonly struct RngRecordValue
    {
        public readonly ulong StreamHigh;
        public readonly ulong StreamLow;
        public readonly ulong State;

        /// <summary>Stream key of the generator; a stream is identified by (stream id, key), never by a name.</summary>
        public readonly ulong StreamKey;

        public readonly ulong DrawCount;

        public RngRecordValue(ulong streamHigh, ulong streamLow, ulong state, ulong streamKey, ulong drawCount)
        {
            StreamHigh = streamHigh;
            StreamLow = streamLow;
            State = state;
            StreamKey = streamKey;
            DrawCount = drawCount;
        }

        public Id128 StreamId => new Id128(StreamHigh, StreamLow);

        public override string ToString() =>
            "rng(" + StreamId.ToString() + ",draws=" + DrawCount.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Which kind of cursor one record carries; P-045, P-050 and P-053.</summary>
    public enum CursorRowKind
    {
        /// <summary>The world's committed-event cursor, so a restored reader resumes where it left off (P-045).</summary>
        EventCursor = 0,

        /// <summary>One issuer's admission high-water mark, so restore keeps duplicate suppression (P-050).</summary>
        IssuerHighWater = 1,
    }

    /// <summary>One event cursor or one per-issuer high-water mark (P-045, P-050, P-053).</summary>
    public readonly struct CursorRecordValue
    {
        public readonly uint RowKind;
        public readonly ulong IssuerHigh;
        public readonly ulong IssuerLow;
        public readonly ulong Sequence;
        public readonly ulong SessionHigh;
        public readonly ulong SessionLow;

        public CursorRecordValue(
            uint rowKind,
            ulong issuerHigh,
            ulong issuerLow,
            ulong sequence,
            ulong sessionHigh,
            ulong sessionLow)
        {
            RowKind = rowKind;
            IssuerHigh = issuerHigh;
            IssuerLow = issuerLow;
            Sequence = sequence;
            SessionHigh = sessionHigh;
            SessionLow = sessionLow;
        }

        public CursorRowKind Row => (CursorRowKind)RowKind;

        public Id128 IssuerId => new Id128(IssuerHigh, IssuerLow);

        /// <summary>Session the cursor was captured in; a restored world re-stamps it with its own fresh id (P-004).</summary>
        public WorldId SourceSession => new WorldId(new Id128(SessionHigh, SessionLow));

        public EventSequence Event => new EventSequence(Sequence);

        public AdmissionSequence Admission => new AdmissionSequence(Sequence);

        public override string ToString() => "cursor(" + Row.ToString() + "," + Sequence.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// Collates four big-endian 64-bit words into the 32-byte content hash they encode, and back (05 s6, P-054).
    /// It is public because a capture and a restore run outside this assembly: the checkpoint records carry a
    /// `ContentHash` as four `UInt64` fields, which is the envelope's only 256-bit representation, and both the
    /// engine-free persistence layer and the Unity reader have to convert between the two forms.
    /// </summary>
    public static class CanonicalId32
    {
        /// <summary>The 32-byte hash whose bytes are the four words in big-endian order (05 s6).</summary>
        public static ContentHash Collate(ulong a, ulong b, ulong c, ulong d)
        {
            var bytes = new byte[ContentHash.SizeInBytes];
            WriteBigEndian(a, bytes, 0);
            WriteBigEndian(b, bytes, 8);
            WriteBigEndian(c, bytes, 16);
            WriteBigEndian(d, bytes, 24);
            return new ContentHash(bytes);
        }

        /// <summary>The four big-endian words of one hash, i.e. the exact inverse of <see cref="Collate"/>.</summary>
        public static void Split(ContentHash hash, out ulong a, out ulong b, out ulong c, out ulong d)
        {
            byte[] bytes = hash.ToArray();
            a = ReadBigEndian(bytes, 0);
            b = ReadBigEndian(bytes, 8);
            c = ReadBigEndian(bytes, 16);
            d = ReadBigEndian(bytes, 24);
        }

        private static void WriteBigEndian(ulong value, byte[] destination, int offset)
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

        private static ulong ReadBigEndian(byte[] source, int offset) =>
            ((ulong)source[offset] << 56)
            | ((ulong)source[offset + 1] << 48)
            | ((ulong)source[offset + 2] << 40)
            | ((ulong)source[offset + 3] << 32)
            | ((ulong)source[offset + 4] << 24)
            | ((ulong)source[offset + 5] << 16)
            | ((ulong)source[offset + 6] << 8)
            | source[offset + 7];
    }
}

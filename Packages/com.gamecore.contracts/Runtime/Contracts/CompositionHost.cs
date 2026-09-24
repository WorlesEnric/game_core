// GameCore.Contracts - production shared contract type (GC-003). Unity-free: BCL subset only, no
// UnityEngine/Unity.* reference, no runtime reflection and no second ECS facade (01 s1, P-058).
// Normative sources: docs/game-core/00-core-protocols.md and docs/game-core/05-contracts-and-data-model.md.
// The public surface of this assembly is API-compatible with the frozen W0 reference seam
// (tests/GameCore.ReferenceSeams); additions are reviewed in artifacts/gc-003/HANDOFF.md.
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.Contracts
{
    /// <summary>
    /// Handle returned immediately when a mutating control operation is admitted (P-051). It is the durable
    /// retrieval key for the operation's pending or terminal result.
    /// </summary>
    public readonly struct OperationStatusHandle : IEquatable<OperationStatusHandle>
    {
        public readonly OperationId Operation;

        /// <summary>Published composition revision the submission was validated against (P-027).</summary>
        public readonly CompositionRevision SubmittedAgainst;

        public OperationStatusHandle(OperationId operation, CompositionRevision submittedAgainst)
        {
            Operation = operation;
            SubmittedAgainst = submittedAgainst;
        }

        public bool Equals(OperationStatusHandle other) =>
            Operation.Equals(other.Operation) && SubmittedAgainst.Equals(other.SubmittedAgainst);

        public override bool Equals(object? obj) => obj is OperationStatusHandle other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + Operation.GetHashCode();
                hash = (hash * 31) + SubmittedAgainst.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(OperationStatusHandle left, OperationStatusHandle right) => left.Equals(right);

        public static bool operator !=(OperationStatusHandle left, OperationStatusHandle right) => !left.Equals(right);

        public override string ToString() => "Handle(" + Operation.ToString() + "@" + SubmittedAgainst.ToString() + ")";
    }

    /// <summary>Retention and capacity settings of the serialized control lane (P-050, P-051).</summary>
    public sealed class ControlLaneCapacitySettings
    {
        public ControlLaneCapacitySettings(int maxQueuedOperations, int maxRetainedResults)
        {
            MaxQueuedOperations = maxQueuedOperations;
            MaxRetainedResults = maxRetainedResults;
        }

        public static ControlLaneCapacitySettings Default { get; } = new ControlLaneCapacitySettings(256, 4096);

        public int MaxQueuedOperations { get; }

        public int MaxRetainedResults { get; }
    }

    /// <summary>
    /// Operation expiry and retry settings. Retryable transient failures use bounded attempts with new
    /// operation IDs; correctness failures require changed input (P-049).
    /// </summary>
    public sealed class OperationExpirySettings
    {
        public OperationExpirySettings(int boundedTransientAttempts, ulong retainedResultSteps)
        {
            BoundedTransientAttempts = boundedTransientAttempts;
            RetainedResultSteps = retainedResultSteps;
        }

        public static OperationExpirySettings Default { get; } = new OperationExpirySettings(3, 0UL);

        public int BoundedTransientAttempts { get; }

        /// <summary>Logical steps a terminal result stays retrievable; 0 means retention is count-based only.</summary>
        public ulong RetainedResultSteps { get; }
    }

    /// <summary>Host-configured control-lane settings exposed for inspection (P-060 evidence discipline).</summary>
    public sealed class CompositionHostSettings
    {
        public CompositionHostSettings(ControlLaneCapacitySettings capacity, OperationExpirySettings expiry)
        {
            Capacity = capacity;
            Expiry = expiry;
        }

        public ControlLaneCapacitySettings Capacity { get; }

        public OperationExpirySettings Expiry { get; }
    }

    /// <summary>
    /// One ledger row. A retransmission of the same operation id with the same input hash returns this same
    /// row unchanged; a conflicting reuse is rejected without overwriting the original result (P-050).
    /// </summary>
    public sealed class OperationLedgerEntry
    {
        public OperationLedgerEntry(
            OperationStatusHandle handle,
            ContentHash inputHash,
            Outcome outcome,
            DiagnosticCode code,
            CompositionRevision publishedRevision,
            AssemblyEpoch publishedEpoch,
            SnapshotToken? publishedSnapshot)
        {
            Handle = handle;
            InputHash = inputHash;
            Outcome = outcome;
            Code = code;
            PublishedRevision = publishedRevision;
            PublishedEpoch = publishedEpoch;
            PublishedSnapshot = publishedSnapshot;
        }

        public OperationStatusHandle Handle { get; }

        public ContentHash InputHash { get; }

        public Outcome Outcome { get; }

        public DiagnosticCode Code { get; }

        public CompositionRevision PublishedRevision { get; }

        public AssemblyEpoch PublishedEpoch { get; }

        public SnapshotToken? PublishedSnapshot { get; }

        public bool IsTerminal => Outcome != Outcome.Pending;
    }

    /// <summary>How a status read resolved; expired retention is distinct from an unknown handle (05 s5).</summary>
    public enum OperationReadOutcome
    {
        Found = 0,
        Unknown = 1,
        Expired = 2,
    }

    /// <summary>Result of one operation status read; no mutation (05 s5, P-051).</summary>
    public sealed class OperationReadResult
    {
        private OperationReadResult(OperationReadOutcome outcome, OperationLedgerEntry? entry, DiagnosticCode code)
        {
            Outcome = outcome;
            Entry = entry;
            Code = code;
        }

        public OperationReadOutcome Outcome { get; }

        /// <summary>Present only when <see cref="Outcome"/> is <see cref="OperationReadOutcome.Found"/>.</summary>
        public OperationLedgerEntry? Entry { get; }

        public DiagnosticCode Code { get; }

        public static OperationReadResult Found(OperationLedgerEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            return new OperationReadResult(OperationReadOutcome.Found, entry, DiagnosticCode.None);
        }

        /// <summary>The handle was never admitted on this lane.</summary>
        public static OperationReadResult Unknown() =>
            new OperationReadResult(OperationReadOutcome.Unknown, null, DiagnosticCode.None);

        /// <summary>The result existed but its retention window has closed (P-050, 05 s5).</summary>
        public static OperationReadResult Expired() =>
            new OperationReadResult(OperationReadOutcome.Expired, null, DiagnosticCode.ResultExpired);
    }

    /// <summary>
    /// Scope snapshot. A live target has exactly one owner scope and the scope tree is rooted and acyclic
    /// (P-010). Isolation and exclusion data travel with the scope because denial along the propagation path
    /// wins over imports and opt-ins (P-016).
    /// </summary>
    public sealed class ScopeSnapshot
    {
        public ScopeSnapshot(
            ScopeId scope,
            ScopeId parent,
            PropagationMode mode,
            IsolationSet serviceIsolation,
            IsolationSet capabilityIsolation,
            IReadOnlyList<ExclusionRule>? exclusions,
            IReadOnlyList<PluginInstanceId>? installs)
        {
            Scope = scope;
            Parent = parent;
            Mode = mode;
            ServiceIsolation = serviceIsolation;
            CapabilityIsolation = capabilityIsolation;
            Exclusions = ContractCollections.Freeze(exclusions);
            Installs = ContractCollections.Freeze(installs);
        }

        public ScopeId Scope { get; }

        /// <summary>Default means the world root: the only scope without a parent (P-010).</summary>
        public ScopeId Parent { get; }

        public bool IsRoot => Parent.IsDefault;

        /// <summary>World-level propagation mode as observed at this scope (P-013).</summary>
        public PropagationMode Mode { get; }

        public IsolationSet ServiceIsolation { get; }

        public IsolationSet CapabilityIsolation { get; }

        public IReadOnlyList<ExclusionRule> Exclusions { get; }

        /// <summary>Installs registered directly at this scope, not in descendants (P-010).</summary>
        public IReadOnlyList<PluginInstanceId> Installs { get; }
    }

    /// <summary>One installation record: explicit saved assembly data, never derived from creation order (P-004).</summary>
    public sealed class InstallRecord
    {
        public InstallRecord(
            PluginInstanceId instance,
            PluginTypeId pluginType,
            ScopeId scope,
            DefinitionRevision configRevision,
            ContentHash configHash,
            int priority,
            InstallationGeneration generation,
            ActivationEpoch activationEpoch)
        {
            Instance = instance;
            PluginType = pluginType;
            Scope = scope;
            ConfigRevision = configRevision;
            ConfigHash = configHash;
            Priority = priority;
            Generation = generation;
            ActivationEpoch = activationEpoch;
        }

        public PluginInstanceId Instance { get; }

        public PluginTypeId PluginType { get; }

        public ScopeId Scope { get; }

        public DefinitionRevision ConfigRevision { get; }

        public ContentHash ConfigHash { get; }

        public int Priority { get; }

        /// <summary>Changes on unmount/remount, not on ordinary reconfigure (P-005).</summary>
        public InstallationGeneration Generation { get; }

        /// <summary>Changes whenever the installation gains or loses execution authority (P-006).</summary>
        public ActivationEpoch ActivationEpoch { get; }
    }

    /// <summary>
    /// Epoch-bound service binding (P-007): contract identity, provider installation, the activation epoch the
    /// binding was resolved under, and the lease that keeps the provider alive.
    /// </summary>
    public sealed class ServiceBinding
    {
        public ServiceBinding(
            ContractRef contract,
            ProviderInstallationId provider,
            ActivationEpoch activationEpoch,
            Id128 leaseId,
            ServiceBindingKind bindingKind,
            bool isFallback)
        {
            Contract = contract;
            Provider = provider;
            ActivationEpoch = activationEpoch;
            LeaseId = leaseId;
            BindingKind = bindingKind;
            IsFallback = isFallback;
        }

        public ContractRef Contract { get; }

        public ProviderInstallationId Provider { get; }

        public ActivationEpoch ActivationEpoch { get; }

        /// <summary>Process-local lease identity; resolved once per assembly, never per target per frame (P-007).</summary>
        public Id128 LeaseId { get; }

        public ServiceBindingKind BindingKind { get; }

        /// <summary>True when an optional dependency rebound to its declared fallback (P-012).</summary>
        public bool IsFallback { get; }
    }

    /// <summary>Installation snapshot: record, lifecycle state, current bindings and diagnostics (P-046).</summary>
    public sealed class InstallSnapshot
    {
        public InstallSnapshot(
            InstallRecord record,
            InstallationState state,
            IReadOnlyList<ServiceBinding>? bindings,
            IReadOnlyList<Diagnostic>? diagnostics)
        {
            Record = record;
            State = state;
            Bindings = ContractCollections.Freeze(bindings);
            Diagnostics = ContractCollections.Freeze(diagnostics);
        }

        public InstallRecord Record { get; }

        public InstallationState State { get; }

        public IReadOnlyList<ServiceBinding> Bindings { get; }

        /// <summary>Why the installation is waiting or failed; empty when active (P-012, P-046).</summary>
        public IReadOnlyList<Diagnostic> Diagnostics { get; }
    }

    /// <summary>The visible committed composition: scopes, installs and the version domains they belong to.</summary>
    public sealed class CompositionStateSnapshot
    {
        public CompositionStateSnapshot(
            WorldId world,
            CompositionRevision revision,
            AssemblyEpoch epoch,
            LogicalStepId step,
            PropagationMode mode,
            IReadOnlyList<ScopeSnapshot>? scopes,
            IReadOnlyList<InstallSnapshot>? installs)
        {
            World = world;
            Revision = revision;
            Epoch = epoch;
            Step = step;
            Mode = mode;
            Scopes = ContractCollections.Freeze(scopes);
            Installs = ContractCollections.Freeze(installs);
        }

        public WorldId World { get; }

        public CompositionRevision Revision { get; }

        public AssemblyEpoch Epoch { get; }

        public LogicalStepId Step { get; }

        public PropagationMode Mode { get; }

        public IReadOnlyList<ScopeSnapshot> Scopes { get; }

        public IReadOnlyList<InstallSnapshot> Installs { get; }
    }

    /// <summary>One scope/install/config/mode edit submitted on the control lane (O-02 to O-08).</summary>
    public sealed class CompositionEditRequest
    {
        public CompositionEditRequest(
            OperationId operation,
            CompositionRevision expectedRevision,
            CompositionEditKind kind,
            FrozenPayload editPayload)
        {
            Operation = operation;
            ExpectedRevision = expectedRevision;
            Kind = kind;
            EditPayload = editPayload ?? throw new ArgumentNullException(nameof(editPayload));
        }

        public OperationId Operation { get; }

        public CompositionRevision ExpectedRevision { get; }

        public CompositionEditKind Kind { get; }

        public FrozenPayload EditPayload { get; }
    }

    /// <summary>
    /// Serialized control-lane contract (P-002, P-051). Every mutation is submitted here, admitted against the last
    /// published revision, and observed through status reads; no caller mutates live world state directly.
    /// </summary>
    public interface ICompositionHost
    {
        WorldId World { get; }

        CompositionHostSettings Settings { get; }

        /// <summary>Admits a scope/install/config/mode edit; the handle is returned before the result exists.</summary>
        OperationStatusHandle Submit(CompositionEditRequest request);

        /// <summary>Reads the pending or terminal result of one admitted operation; pure, no mutation.</summary>
        OperationReadResult Read(OperationStatusHandle handle);

        /// <summary>Visible committed composition, including waiting installations (P-012, P-046).</summary>
        CompositionStateSnapshot Snapshot();

        /// <summary>Default means an unknown scope; this never invents a node or a default policy.</summary>
        ScopeSnapshot? FindScope(ScopeId scope);

        /// <summary>Null means no such installation is registered in this world.</summary>
        InstallSnapshot? FindInstall(PluginInstanceId instance);
    }
}

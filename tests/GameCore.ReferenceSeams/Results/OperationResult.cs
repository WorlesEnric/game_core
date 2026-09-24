// Test-only reference seam for the shared GameCore.Contracts surface (see TestOnlyMarker.cs).
// Result shape from docs/game-core/05-contracts-and-data-model.md s4 and the compilable skeleton
// examples/GameCore.Contracts.cs. The skeleton's four-argument constructor is preserved; the
// old/new revision-epoch, diagnostics and cleanup/quarantine data required by 05 s4 are added.
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.Contracts
{
    /// <summary>
    /// Terminal or pending operation result. The distinctions Outcome carries are load-bearing:
    /// PublishedWithCleanupErrors is already authoritative, Rejected made no live writes, Faulted
    /// left live storage unsafe, and NoChange incremented no revision (05 s4).
    /// </summary>
    public sealed class OperationResult
    {
        /// <summary>Skeleton-compatible shape: no revision/epoch or cleanup detail recorded.</summary>
        public OperationResult(OperationId operation, Outcome outcome, string diagnosticCode, SnapshotToken? publishedSnapshot)
            : this(
                operation,
                outcome,
                diagnosticCode,
                publishedSnapshot,
                CompositionRevision.Zero,
                CompositionRevision.Zero,
                AssemblyEpoch.Zero,
                AssemblyEpoch.Zero,
                null,
                null,
                null)
        {
        }

        public OperationResult(
            OperationId operation,
            Outcome outcome,
            string diagnosticCode,
            SnapshotToken? publishedSnapshot,
            CompositionRevision oldRevision,
            CompositionRevision newRevision,
            AssemblyEpoch oldEpoch,
            AssemblyEpoch newEpoch,
            IReadOnlyList<Diagnostic>? diagnostics,
            IReadOnlyList<Id128>? cleanupReferences,
            IReadOnlyList<Id128>? quarantineReferences)
        {
            Operation = operation;
            Outcome = outcome;
            DiagnosticCode = diagnosticCode ?? throw new ArgumentNullException(nameof(diagnosticCode));
            PublishedSnapshot = publishedSnapshot;
            OldRevision = oldRevision;
            NewRevision = newRevision;
            OldEpoch = oldEpoch;
            NewEpoch = newEpoch;
            Diagnostics = ContractCollections.Freeze(diagnostics);
            CleanupReferences = ContractCollections.Freeze(cleanupReferences);
            QuarantineReferences = ContractCollections.Freeze(quarantineReferences);
        }

        public OperationId Operation { get; }

        public Outcome Outcome { get; }

        /// <summary>Stable textual diagnostic code, empty when the outcome carries none.</summary>
        public string DiagnosticCode { get; }

        public SnapshotToken? PublishedSnapshot { get; }

        public CompositionRevision OldRevision { get; }

        public CompositionRevision NewRevision { get; }

        public AssemblyEpoch OldEpoch { get; }

        public AssemblyEpoch NewEpoch { get; }

        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        /// <summary>Retained resource ids from a published cleanup error (P-048).</summary>
        public IReadOnlyList<Id128> CleanupReferences { get; }

        /// <summary>Resources still reachable by unfinished work and therefore quarantined (P-048).</summary>
        public IReadOnlyList<Id128> QuarantineReferences { get; }

        public bool IsTerminal => Outcome != Outcome.Pending;
    }

    /// <summary>Result of a typed inter-system request; admission acceptance is not gameplay success (P-042).</summary>
    public sealed class RequestResult
    {
        public RequestResult(RequestResultKind kind, DiagnosticCode reason, EventCursor causalCursor)
        {
            Kind = kind;
            Reason = reason;
            CausalCursor = causalCursor;
        }

        public RequestResultKind Kind { get; }

        public DiagnosticCode Reason { get; }

        /// <summary>Committed event cursor when the request was committed; default when not applicable.</summary>
        public EventCursor CausalCursor { get; }
    }

    /// <summary>Installation state snapshot exposed by the composition host (P-046).</summary>
    public sealed class InstallationStatus
    {
        public InstallationStatus(
            PluginInstanceId instance,
            ScopeId scope,
            InstallationState state,
            InstallationGeneration generation,
            ActivationEpoch activationEpoch,
            IReadOnlyList<Diagnostic>? diagnostics)
        {
            Instance = instance;
            Scope = scope;
            State = state;
            Generation = generation;
            ActivationEpoch = activationEpoch;
            Diagnostics = ContractCollections.Freeze(diagnostics);
        }

        public PluginInstanceId Instance { get; }

        public ScopeId Scope { get; }

        public InstallationState State { get; }

        public InstallationGeneration Generation { get; }

        public ActivationEpoch ActivationEpoch { get; }

        public IReadOnlyList<Diagnostic> Diagnostics { get; }
    }
}

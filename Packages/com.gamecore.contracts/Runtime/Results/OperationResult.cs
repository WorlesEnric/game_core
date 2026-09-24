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
    /// Terminal or pending operation result. The distinctions Outcome carries are load-bearing:
    /// PublishedWithCleanupErrors is already authoritative, Rejected made no live writes, Faulted
    /// left live storage unsafe, and NoChange incremented no revision (05 s4).
    /// </summary>
    public sealed class OperationResult
    {
        /// <summary>Skeleton-compatible arity: no revision/epoch or cleanup detail recorded.</summary>
        public OperationResult(OperationId operation, Outcome outcome, DiagnosticCode code, SnapshotToken? publishedSnapshot)
            : this(
                operation,
                outcome,
                code,
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
            DiagnosticCode code,
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
            Code = code;
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

        /// <summary>Structured code; <see cref="DiagnosticCode.None"/> when the outcome carries none.</summary>
        public DiagnosticCode Code { get; }

        /// <summary>The normative literal for <see cref="Code"/>, for diagnostics and cross-checking (00 s9).</summary>
        public string CodeText => DiagnosticCodeText.Of(Code);

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
}

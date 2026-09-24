// Test-only reference seam for the shared GameCore.Contracts surface (see TestOnlyMarker.cs).
// Committed-event and composition-observer payloads from docs/game-core/00-core-protocols.md P-045 and
// 05-contracts-and-data-model.md s4. Hashing and publication policy live in later waves; this is data.
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.Contracts
{
    /// <summary>
    /// One committed event. Stable ids, schema/revision, step, epoch, event sequence and causal request
    /// are observable together at publication only (P-045).
    /// </summary>
    public sealed class CommittedEvent
    {
        public CommittedEvent(
            EventCursor cursor,
            SchemaRef schema,
            AssemblyEpoch epoch,
            LogicalStepId step,
            OperationId causalRequest,
            FrozenPayload payload)
        {
            Cursor = cursor;
            Schema = schema;
            Epoch = epoch;
            Step = step;
            CausalRequest = causalRequest;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        public EventCursor Cursor { get; }

        public SchemaRef Schema { get; }

        public AssemblyEpoch Epoch { get; }

        public LogicalStepId Step { get; }

        public OperationId CausalRequest { get; }

        public FrozenPayload Payload { get; }
    }

    /// <summary>Bounded committed-event page with its cursor outcome (P-045).</summary>
    public sealed class CommittedEventPage
    {
        public CommittedEventPage(CursorOutcome outcome, IReadOnlyList<CommittedEvent>? events, EventCursor nextCursor)
        {
            Outcome = outcome;
            Events = ContractCollections.Freeze(events);
            NextCursor = nextCursor;
        }

        public CursorOutcome Outcome { get; }

        public IReadOnlyList<CommittedEvent> Events { get; }

        public EventCursor NextCursor { get; }
    }

    /// <summary>Payload of a successful composition publication (P-029, P-045).</summary>
    public sealed class CompositionPublishedEvent
    {
        public CompositionPublishedEvent(
            OperationId operation,
            CompositionRevision oldRevision,
            CompositionRevision newRevision,
            AssemblyEpoch oldEpoch,
            AssemblyEpoch newEpoch,
            ContentHash planHash,
            AffectedCounts counts)
        {
            Operation = operation;
            OldRevision = oldRevision;
            NewRevision = newRevision;
            OldEpoch = oldEpoch;
            NewEpoch = newEpoch;
            PlanHash = planHash;
            Counts = counts;
        }

        public OperationId Operation { get; }

        public CompositionRevision OldRevision { get; }

        public CompositionRevision NewRevision { get; }

        public AssemblyEpoch OldEpoch { get; }

        public AssemblyEpoch NewEpoch { get; }

        public ContentHash PlanHash { get; }

        public AffectedCounts Counts { get; }
    }

    /// <summary>Payload of a rejected composition proposal; the old visible revision is retained (P-029).</summary>
    public sealed class CompositionRejectedEvent
    {
        public CompositionRejectedEvent(OperationId operation, ContentHash planHash, IReadOnlyList<Diagnostic>? diagnostics)
        {
            Operation = operation;
            PlanHash = planHash;
            Diagnostics = ContractCollections.Freeze(diagnostics);
        }

        public OperationId Operation { get; }

        public ContentHash PlanHash { get; }

        public IReadOnlyList<Diagnostic> Diagnostics { get; }
    }
}

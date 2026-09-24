// Test-only reference seam for the shared GameCore.Contracts surface (TestOnlyMarker.cs).
// API boundary contracts from docs/game-core/05-contracts-and-data-model.md s5, plus the observer and
// callback-gate seams required by P-045 and P-047. W1 peers implement these against the frozen surface;
// no ECS, world runtime or Unity type appears here.
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.Contracts
{
    /// <summary>Frozen composition proposal submitted onto the serialized control lane (O-02 to O-08).</summary>
    public sealed class CompositionProposal
    {
        public CompositionProposal(
            OperationId operation,
            ulong expectedRevision,
            FrozenPayload catalogHash,
            SchemaRef editSchema,
            FrozenPayload editPayload)
        {
            Operation = operation;
            ExpectedRevision = expectedRevision;
            CatalogHash = catalogHash ?? throw new ArgumentNullException(nameof(catalogHash));
            EditSchema = editSchema;
            EditPayload = editPayload ?? throw new ArgumentNullException(nameof(editPayload));
        }

        public OperationId Operation { get; }

        /// <summary>Last published composition revision the proposal was checked against (P-027).</summary>
        public ulong ExpectedRevision { get; }

        /// <summary>32-byte SHA-256 value in the complete generated schema.</summary>
        public FrozenPayload CatalogHash { get; }

        public SchemaRef EditSchema { get; }

        public FrozenPayload EditPayload { get; }
    }

    /// <summary>Immutable command envelope submitted for host admission (P-042).</summary>
    public sealed class CommandEnvelope
    {
        public CommandEnvelope(
            OperationId requestId,
            RouteId routeId,
            TargetId targetId,
            SchemaRef schema,
            ulong? expectedDomainVersion,
            FrozenPayload payload)
        {
            RequestId = requestId;
            RouteId = routeId;
            TargetId = targetId;
            Schema = schema;
            ExpectedDomainVersion = expectedDomainVersion;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        public OperationId RequestId { get; }

        public RouteId RouteId { get; }

        public TargetId TargetId { get; }

        public SchemaRef Schema { get; }

        public ulong? ExpectedDomainVersion { get; }

        public FrozenPayload Payload { get; }
    }

    /// <summary>One matching or rejected derivation record inside an explanation page (P-029).</summary>
    public sealed class ExplanationRecord
    {
        public ExplanationRecord(
            Id128 recordKey,
            RuleId rule,
            ProviderInstallationId provider,
            DiagnosticCode rejection,
            IReadOnlyList<Id128>? evidenceKeys)
        {
            RecordKey = recordKey;
            Rule = rule;
            Provider = provider;
            Rejection = rejection;
            EvidenceKeys = ContractCollections.Freeze(evidenceKeys);
        }

        public Id128 RecordKey { get; }

        public RuleId Rule { get; }

        public ProviderInstallationId Provider { get; }

        /// <summary><see cref="DiagnosticCode.None"/> when the rule matched.</summary>
        public DiagnosticCode Rejection { get; }

        public IReadOnlyList<Id128> EvidenceKeys { get; }
    }

    /// <summary>Immutable explanation page for one target/capability at one published token (P-029).</summary>
    public sealed class ExplanationPage
    {
        public ExplanationPage(
            TargetId target,
            CapabilityId capability,
            SnapshotToken token,
            IReadOnlyList<ExplanationRecord>? matching,
            IReadOnlyList<ExplanationRecord>? rejected,
            IReadOnlyList<ScopeId>? scopePath,
            PropagationMode mode,
            int stratum,
            ContentHash recipeHash,
            IReadOnlyList<StateDisposition>? stateDispositions)
        {
            Target = target;
            Capability = capability;
            Token = token;
            Matching = ContractCollections.Freeze(matching);
            Rejected = ContractCollections.Freeze(rejected);
            ScopePath = ContractCollections.Freeze(scopePath);
            Mode = mode;
            Stratum = stratum;
            RecipeHash = recipeHash;
            StateDispositions = ContractCollections.Freeze(stateDispositions);
        }

        public TargetId Target { get; }

        public CapabilityId Capability { get; }

        public SnapshotToken Token { get; }

        public IReadOnlyList<ExplanationRecord> Matching { get; }

        public IReadOnlyList<ExplanationRecord> Rejected { get; }

        public IReadOnlyList<ScopeId> ScopePath { get; }

        public PropagationMode Mode { get; }

        public int Stratum { get; }

        public ContentHash RecipeHash { get; }

        public IReadOnlyList<StateDisposition> StateDispositions { get; }
    }

    /// <summary>Frozen request for one managed-resource preparation (P-007, O-23).</summary>
    public sealed class ManagedResourceRequest
    {
        public ManagedResourceRequest(ResourceKey resource, AsyncWorkToken token, FrozenPayload frozenConfig)
        {
            Resource = resource;
            Token = token;
            FrozenConfig = frozenConfig ?? throw new ArgumentNullException(nameof(frozenConfig));
        }

        public ResourceKey Resource { get; }

        public AsyncWorkToken Token { get; }

        public FrozenPayload FrozenConfig { get; }
    }

    /// <summary>Gated managed lease; the disposer is recorded before the value is exposed (P-048).</summary>
    public interface IManagedResourceLease : IDisposable
    {
        ResourceKey Resource { get; }

        Id128 LeaseId { get; }

        AsyncWorkToken Token { get; }
    }

    /// <summary>Fenced world state handed to a generated apply path: base epoch plus settled jobs (P-030).</summary>
    public interface IFencedWorldState
    {
        WorldId World { get; }

        AssemblyEpoch BaseEpoch { get; }

        bool JobsSettled { get; }
    }

    /// <summary>Apply classification; an exception after the first live write faults the world (P-031).</summary>
    public enum ApplyOutcome
    {
        Applied = 0,
        RejectedBeforeWrite = 1,
        FaultedAfterWrite = 2,
    }

    /// <summary>Request for one generated recipe apply (05 s5).</summary>
    public sealed class RecipeApplyRequest
    {
        public RecipeApplyRequest(DefinitionRef recipe, TargetHandle target)
        {
            Recipe = recipe;
            Target = target;
        }

        public DefinitionRef Recipe { get; }

        public TargetHandle Target { get; }
    }

    /// <summary>Result of one generated recipe apply (05 s5).</summary>
    public sealed class RecipeApplyResult
    {
        public RecipeApplyResult(ApplyOutcome outcome, DiagnosticCode code, IReadOnlyList<Id128>? writtenSchemas)
        {
            Outcome = outcome;
            Code = code;
            WrittenSchemas = ContractCollections.Freeze(writtenSchemas);
        }

        public ApplyOutcome Outcome { get; }

        public DiagnosticCode Code { get; }

        public IReadOnlyList<Id128> WrittenSchemas { get; }
    }

    /// <summary>Request for one generated migration; pure, versioned and without I/O (05 s5, P-054).</summary>
    public sealed class MigrationRequest
    {
        public MigrationRequest(StateSlotKey slot, SchemaRef fromSchema, SchemaRef toSchema, FrozenPayload oldState)
        {
            Slot = slot;
            FromSchema = fromSchema;
            ToSchema = toSchema;
            OldState = oldState ?? throw new ArgumentNullException(nameof(oldState));
        }

        public StateSlotKey Slot { get; }

        public SchemaRef FromSchema { get; }

        public SchemaRef ToSchema { get; }

        public FrozenPayload OldState { get; }
    }

    /// <summary>Result of one generated migration; failure leaves the old live state untouched (P-054).</summary>
    public sealed class MigrationResult
    {
        public MigrationResult(bool succeeded, DiagnosticCode code, FrozenPayload? newState)
        {
            Succeeded = succeeded;
            Code = code;
            NewState = newState;
        }

        public bool Succeeded { get; }

        public DiagnosticCode Code { get; }

        public FrozenPayload? NewState { get; }
    }

    /// <summary>Submission of a frozen composition proposal (05 s5).</summary>
    public interface ICompositionCommands
    {
        OperationId Submit(CompositionProposal proposal);
    }

    /// <summary>Admission of an immutable command envelope (05 s5).</summary>
    public interface ICommandIngress
    {
        OperationId Submit(CommandEnvelope command);
    }

    /// <summary>Immutable operation status read; no mutation (05 s5).</summary>
    public interface IOperationReader
    {
        OperationResult Read(OperationId operation);
    }

    /// <summary>Serialized cancellation cutoff (05 s5, P-051).</summary>
    public interface IOperationControl
    {
        CancelOutcome Cancel(OperationId cancellationOperation, OperationId target);
    }

    /// <summary>Disposable immutable snapshot lease; never exposes writable ECS data (P-045).</summary>
    public interface ISnapshotLease : IDisposable
    {
        SnapshotToken Token { get; }

        FrozenPayload State { get; }
    }

    /// <summary>Acquisition of a retained snapshot lease (05 s5).</summary>
    public interface IObservationReader
    {
        ISnapshotLease Acquire(SnapshotToken token);
    }

    /// <summary>Read-only explanation lookup for a published epoch (05 s5, P-029).</summary>
    public interface IExplanationReader
    {
        ExplanationPage Explain(TargetId target, CapabilityId capability, SnapshotToken token);
    }

    /// <summary>Control-plane managed-resource factory (05 s5).</summary>
    public interface IManagedResourceFactory
    {
        IManagedResourceLease Prepare(ManagedResourceRequest request);
    }

    /// <summary>Generated Unity-side apply seam; the only live ECS assembly path (01 s1, P-030).</summary>
    public interface IRecipeApplyBridge
    {
        RecipeApplyResult ApplyRecipe(RecipeApplyRequest request, IFencedWorldState world);
    }

    /// <summary>Generated migration seam; pure and versioned, no ECS write (05 s5).</summary>
    public interface IStateMigrator
    {
        MigrationResult Migrate(MigrationRequest request);
    }

    /// <summary>Safe-boundary publication of one validated plan (01 s1, P-031).</summary>
    public interface IAssemblyPublisher
    {
        OperationResult Publish(ChangePlan plan);
    }

    /// <summary>Composition publication/rejection observer (P-029, P-045).</summary>
    public interface ICompositionObserver
    {
        void OnCompositionPublished(CompositionPublishedEvent published);

        void OnCompositionRejected(CompositionRejectedEvent rejected);
    }

    /// <summary>Bounded committed-event reader (P-045).</summary>
    public interface ICommittedEventReader
    {
        CommittedEventPage Read(EventCursor cursor, int maxEvents);
    }

    /// <summary>
    /// Callback gate evaluated on dispatch and again on completion; a stale activation or retired route is
    /// discarded rather than published (P-047).
    /// </summary>
    public interface ICallbackGate
    {
        CallbackGateDecision Evaluate(AsyncWorkToken token);
    }
}

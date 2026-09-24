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

    /// <summary>Where an explanation came from; a staged plan is never reported as world observation (05 s5).</summary>
    public enum ExplanationSource
    {
        /// <summary>Derived from the committed published assembly at the requested token.</summary>
        PublishedComposition = 0,

        /// <summary>Staged plan diagnostics: labelled distinctly and never presented as world observation.</summary>
        StagedPlan = 1,
    }

    /// <summary>Bounded page request for one explanation read (P-029).</summary>
    public readonly struct ExplanationPageRequest
    {
        public readonly uint Offset;
        public readonly uint MaxRecords;

        public ExplanationPageRequest(uint offset, uint maxRecords)
        {
            Offset = offset;
            MaxRecords = maxRecords;
        }

        public static ExplanationPageRequest FirstPage(uint maxRecords) => new ExplanationPageRequest(0U, maxRecords);

        public bool IsValid => MaxRecords > 0U;

        public override string ToString() =>
            Offset.ToString(System.Globalization.CultureInfo.InvariantCulture) + "+" +
            MaxRecords.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Immutable explanation page for one target/capability at one token. Records may be interned, so the page
    /// is bounded and reports how to continue; diagnostics never retain an unbounded string tree per entity.
    /// </summary>
    public sealed class ExplanationPage
    {
        public ExplanationPage(
            TargetId target,
            CapabilityId capability,
            SnapshotToken token,
            ExplanationSource source,
            ExplanationPageRequest request,
            IReadOnlyList<ExplanationRecord>? matching,
            IReadOnlyList<ExplanationRecord>? rejected,
            IReadOnlyList<ScopeId>? scopePath,
            PropagationMode mode,
            int stratum,
            ContentHash recipeHash,
            IReadOnlyList<StateDisposition>? stateDispositions,
            ulong totalMatching,
            ulong totalRejected)
        {
            Target = target;
            Capability = capability;
            Token = token;
            Source = source;
            Request = request;
            Matching = ContractCollections.Freeze(matching);
            Rejected = ContractCollections.Freeze(rejected);
            ScopePath = ContractCollections.Freeze(scopePath);
            Mode = mode;
            Stratum = stratum;
            RecipeHash = recipeHash;
            StateDispositions = ContractCollections.Freeze(stateDispositions);
            TotalMatching = totalMatching;
            TotalRejected = totalRejected;
        }

        public TargetId Target { get; }

        public CapabilityId Capability { get; }

        public SnapshotToken Token { get; }

        /// <summary>Staged-plan pages are labelled; they cannot be mistaken for published observation (05 s5).</summary>
        public ExplanationSource Source { get; }

        public ExplanationPageRequest Request { get; }

        public IReadOnlyList<ExplanationRecord> Matching { get; }

        public IReadOnlyList<ExplanationRecord> Rejected { get; }

        public IReadOnlyList<ScopeId> ScopePath { get; }

        public PropagationMode Mode { get; }

        public int Stratum { get; }

        public ContentHash RecipeHash { get; }

        public IReadOnlyList<StateDisposition> StateDispositions { get; }

        public ulong TotalMatching { get; }

        public ulong TotalRejected { get; }

        /// <summary>Offset to request next; <see cref="HasMore"/> false means this is the final page.</summary>
        public uint NextOffset => Request.Offset + (uint)Matching.Count + (uint)Rejected.Count;

        public bool HasMore => (ulong)NextOffset < TotalMatching + TotalRejected;
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

    /// <summary>
    /// Gate in front of a prepared managed callback or subscription. A prepared resource routes to an inert
    /// gate, never directly to a gameplay handler, and a closed gate stays closed across retirement (P-047).
    /// </summary>
    public interface IResourceGate
    {
        bool IsOpen { get; }

        /// <summary>Irreversible for this gate: an activation epoch that closes it never reopens it.</summary>
        void Close();
    }

    /// <summary>
    /// Gated managed lease. The recorded disposer and the readiness state exist before the value is exposed, so
    /// a partially successful acquisition path still has cleanup recorded immediately (P-007, P-048).
    /// </summary>
    public interface IManagedResourceLease : IDisposable
    {
        ResourceKey Resource { get; }

        Id128 LeaseId { get; }

        AsyncWorkToken Token { get; }

        /// <summary>Pending while the lease is staged behind its gate; a staged lease is not usable yet.</summary>
        ResourceReadiness Readiness { get; }

        /// <summary>Registered disposer key, recorded at acquisition time (P-048).</summary>
        FactoryKey Disposer { get; }

        /// <summary>Inert until the owning activation publishes; consumed callbacks check it on dispatch too.</summary>
        IResourceGate Gate { get; }

        /// <summary>True once its single permitted disposal has run (P-048: dispose each lease at most once).</summary>
        bool IsDisposed { get; }
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
        public MigrationRequest(
            StateSlotKey slot,
            SchemaRef fromSchema,
            SchemaRef toSchema,
            FrozenPayload oldState,
            FrozenPayload config)
        {
            Slot = slot;
            FromSchema = fromSchema;
            ToSchema = toSchema;
            OldState = oldState ?? throw new ArgumentNullException(nameof(oldState));
            Config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public StateSlotKey Slot { get; }

        public SchemaRef FromSchema { get; }

        public SchemaRef ToSchema { get; }

        public FrozenPayload OldState { get; }

        /// <summary>Immutable configuration the migration is allowed to read; never live world state (05 s5).</summary>
        public FrozenPayload Config { get; }
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
        /// <summary>Admits the proposal and returns its retrieval handle; no live change happens yet.</summary>
        OperationStatusHandle Submit(CompositionProposal proposal);
    }

    /// <summary>
    /// Admission receipt for one command envelope: the request identity plus the protocol's request result.
    /// Admission acceptance is not gameplay success (P-042, 05 s5).
    /// </summary>
    public sealed class CommandAdmissionReceipt
    {
        public CommandAdmissionReceipt(OperationId request, RequestResult result, AdmissionSequence acceptedSequence)
        {
            Request = request;
            Result = result ?? throw new ArgumentNullException(nameof(result));
            AcceptedSequence = acceptedSequence;
        }

        public OperationId Request { get; }

        public RequestResult Result { get; }

        /// <summary>Host-assigned admitted sequence used for canonical ordering in replay (P-008, P-037).</summary>
        public AdmissionSequence AcceptedSequence { get; }

        public bool Admitted => Result.Kind == RequestResultKind.Accepted || Result.Kind == RequestResultKind.Committed;
    }

    /// <summary>Admission of an immutable command envelope; the caller cannot retain a mutable payload (05 s5).</summary>
    public interface ICommandIngress
    {
        CommandAdmissionReceipt Submit(CommandEnvelope command);
    }

    /// <summary>Immutable operation status read; no mutation. Expiry and unknown handles are distinguished (05 s5).</summary>
    public interface IOperationReader
    {
        OperationReadResult Read(OperationId operation);
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

    /// <summary>Outcome of one snapshot acquisition attempt.</summary>
    public enum SnapshotAcquireOutcome
    {
        Acquired = 0,

        /// <summary>The token is outside retention: report and let the reader resynchronize (P-045).</summary>
        Expired = 1,

        /// <summary>Retention is at capacity, so no new lease is granted; leased memory is never overwritten (P-007).</summary>
        Backpressure = 2,

        /// <summary>The token belongs to another world incarnation (P-004, P-005).</summary>
        ForeignWorld = 3,
    }

    /// <summary>
    /// Result of one acquisition attempt. Expected refusals are values, not exceptions: backpressure and expiry
    /// are normal bounded-retention outcomes that the caller must handle (P-007, P-045).
    /// </summary>
    public sealed class SnapshotAcquireResult
    {
        private SnapshotAcquireResult(SnapshotAcquireOutcome outcome, SnapshotToken token, ISnapshotLease? lease, DiagnosticCode code)
        {
            Outcome = outcome;
            Token = token;
            Lease = lease;
            Code = code;
        }

        public SnapshotAcquireOutcome Outcome { get; }

        public SnapshotToken Token { get; }

        /// <summary>Non-null only for <see cref="SnapshotAcquireOutcome.Acquired"/>.</summary>
        public ISnapshotLease? Lease { get; }

        public DiagnosticCode Code { get; }

        public bool Succeeded => Outcome == SnapshotAcquireOutcome.Acquired;

        public static SnapshotAcquireResult Acquired(SnapshotToken token, ISnapshotLease lease)
        {
            if (lease == null)
            {
                throw new ArgumentNullException(nameof(lease));
            }

            return new SnapshotAcquireResult(SnapshotAcquireOutcome.Acquired, token, lease, DiagnosticCode.None);
        }

        public static SnapshotAcquireResult Expired(SnapshotToken token) =>
            new SnapshotAcquireResult(SnapshotAcquireOutcome.Expired, token, null, DiagnosticCode.CursorExpired);

        public static SnapshotAcquireResult Backpressure(SnapshotToken token) =>
            new SnapshotAcquireResult(SnapshotAcquireOutcome.Backpressure, token, null, DiagnosticCode.SnapshotBackpressure);

        public static SnapshotAcquireResult ForeignWorld(SnapshotToken token) =>
            new SnapshotAcquireResult(SnapshotAcquireOutcome.ForeignWorld, token, null, DiagnosticCode.StaleHandle);
    }

    /// <summary>Acquisition of a retained snapshot lease; a refusal is returned, never thrown (05 s5, P-007).</summary>
    public interface IObservationReader
    {
        SnapshotAcquireResult Acquire(SnapshotToken token);
    }

    /// <summary>Read-only explanation lookup for a published epoch (05 s5, P-029).</summary>
    public interface IExplanationReader
    {
        /// <summary>Bounded page of matching and rejected records for the published epoch.</summary>
        ExplanationPage Explain(TargetId target, CapabilityId capability, SnapshotToken token, ExplanationPageRequest page);
    }

    /// <summary>
    /// Staged-plan diagnostics: a distinct contract and label from published explanations, because staged data can
    /// inspect a plan but must never be mistaken for world observation (00 s9, 05 s5).
    /// </summary>
    public interface IStagedPlanDiagnostics
    {
        /// <summary>Bounded page of staged diagnostics for one still-unpublished operation.</summary>
        ExplanationPage ReadStaged(OperationId operation, TargetId target, CapabilityId capability, ExplanationPageRequest page);
    }

    /// <summary>Control-plane managed-resource factory (05 s5).</summary>
    public interface IManagedResourceFactory
    {
        IManagedResourceLease Prepare(ManagedResourceRequest request);
    }

    /// <summary>Generated Unity-side apply contract; the only live ECS assembly path (01 s1, P-030).</summary>
    public interface IRecipeApplyBridge
    {
        RecipeApplyResult ApplyRecipe(RecipeApplyRequest request, IFencedWorldState world);
    }

    /// <summary>Generated migration contract; pure and versioned, no ECS write (05 s5).</summary>
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

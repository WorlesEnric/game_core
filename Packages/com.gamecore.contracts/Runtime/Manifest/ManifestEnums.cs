// GameCore.Contracts - production shared contract type (GC-003). Unity-free: BCL subset only, no
// UnityEngine/Unity.* reference, no runtime reflection and no second ECS facade (01 s1, P-058).
// Normative sources: docs/game-core/00-core-protocols.md and docs/game-core/05-contracts-and-data-model.md.
// The public surface of this assembly is API-compatible with the frozen W0 reference seam
// (tests/GameCore.ReferenceSeams); additions are reviewed in artifacts/gc-003/HANDOFF.md.
#nullable enable

namespace GameCore.Contracts
{
    /// <summary>World-level propagation mode; one setting per world (P-013).</summary>
    public enum PropagationMode
    {
        Automatic = 0,
        Conservative = 1,
    }

    /// <summary>Selected temporal model (P-058); the kernel supplies no universal phase table.</summary>
    public enum TemporalModel
    {
        FixedStep = 0,
        CommandDriven = 1,
    }

    /// <summary>Per-slot composition policy (P-018).</summary>
    public enum CompositionPolicy
    {
        Additive = 0,
        Replace = 1,
        Ordered = 2,
        Exclusive = 3,
        Incompatible = 4,
    }

    /// <summary>Policy applied when the final support for derived state is removed (P-033).</summary>
    public enum LastSupportPolicy
    {
        RemoveDerived = 0,
        PreserveDormant = 1,
        TransferTo = 2,
    }

    /// <summary>Non-throwing terminal/settled outcome of a mutating operation (05 s4).</summary>
    public enum Outcome
    {
        Pending = 0,
        Accepted = 1,
        Committed = 2,
        Published = 3,
        PublishedWithCleanupErrors = 4,
        NoChange = 5,
        Rejected = 6,
        Cancelled = 7,
        Faulted = 8,
    }

    /// <summary>Result of a serialized cancellation cutoff (P-051).</summary>
    public enum CancelOutcome
    {
        Cancelled = 0,
        TooLate = 1,
        Unknown = 2,
        ResultExpired = 3,
        /// <summary>The cancellation operation ID was already used with a different target or input (P-050); the original row is kept.</summary>
        IdempotencyConflict = 4,
        /// <summary>The cancellation request itself was not admitted (session, issuer, capacity or validation failure); nothing changed.</summary>
        Rejected = 5,
    }

    /// <summary>Installation lifecycle state (P-046).</summary>
    public enum InstallationState
    {
        Registered = 0,
        WaitingForDependencies = 1,
        Preparing = 2,
        Active = 3,
        Quiescing = 4,
        Suspended = 5,
        Retiring = 6,
        Disposed = 7,
        Failed = 8,
    }

    /// <summary>Host-visible world lifecycle state (O-01, O-19, O-26).</summary>
    public enum WorldLifecycleState
    {
        Created = 0,
        Running = 1,
        Paused = 2,
        Stopping = 3,
        Disposed = 4,
        Faulted = 5,
    }

    /// <summary>Kind of change applied by one composition edit (05 s4).</summary>
    public enum CompositionEditKind
    {
        Add = 0,
        Update = 1,
        Remove = 2,
        Reparent = 3,
    }

    /// <summary>Visibility of a service export (P-011).</summary>
    public enum ServiceVisibility
    {
        Private = 0,
        ExportToDescendants = 1,
    }

    /// <summary>Single-binding or multi-binding service contract (P-011).</summary>
    public enum ServiceBindingKind
    {
        Single = 0,
        Multi = 1,
    }

    /// <summary>Domain in which a declared service dependency may resolve (P-011).</summary>
    public enum ServiceResolutionDomain
    {
        /// <summary>Own scope and visible ancestors; sibling search remains forbidden (P-011).</summary>
        AncestorsAndSelf = 0,

        /// <summary>Own scope only.</summary>
        SelfOnly = 1,

        /// <summary>Explicitly imported world service only.</summary>
        WorldImported = 2,
    }

    /// <summary>System instance multiplicity declared by a stage entry (P-039).</summary>
    public enum SystemMultiplicity
    {
        World = 0,
        PerPartition = 1,
    }

    /// <summary>Host affinity of a declared stage (P-039).</summary>
    public enum HostAffinity
    {
        ManagedMain = 0,
        BurstJob = 1,
        AdapterMainThread = 2,
    }

    /// <summary>Buffer lifetime contract (P-043).</summary>
    public enum BufferLifetime
    {
        Stage = 0,
        Step = 1,
        BoundedNextStep = 2,
    }

    /// <summary>Overflow behavior of a bounded buffer (P-043).</summary>
    public enum BufferOverflowPolicy
    {
        /// <summary>Required gameplay input rejects before mutation; never a silent drop.</summary>
        RejectBeforeMutation = 0,

        /// <summary>Diagnostic/presentation only, with counters.</summary>
        LossyWithCounters = 1,
    }

    /// <summary>Drain/cancel/rebind disposition of a buffer port (P-043).</summary>
    public enum BufferCancellationPolicy
    {
        Drain = 0,
        Discard = 1,
        RebindToCompatibleOwner = 2,
    }

    /// <summary>Failure classification recorded for a registered resource factory (P-049).</summary>
    public enum FailureClassification
    {
        Retriable = 0,
        CorrectnessRequiresChangedInput = 1,
        Fatal = 2,
    }

    /// <summary>Propagation reach of one derivation rule (P-013).</summary>
    public enum PropagationReach
    {
        SelfAndDescendants = 0,
        DescendantsOnly = 1,

        /// <summary>Never propagates (P-013).</summary>
        LocalOnly = 2,
    }

    /// <summary>Kind of exclusion target (P-016).</summary>
    public enum ExclusionTargetKind
    {
        Capability = 0,
        Rule = 1,
        Provider = 2,
    }

    /// <summary>Kind of staged state disposition (P-033).</summary>
    public enum StateDispositionKind
    {
        Retain = 0,
        Retract = 1,
        Transfer = 2,
        Migrate = 3,
    }

    /// <summary>Readiness of a staged managed-resource acquisition (05 s4).</summary>
    public enum ResourceReadiness
    {
        Pending = 0,
        Ready = 1,
        Failed = 2,
        Retiring = 3,
    }

    /// <summary>Result kind of a typed inter-system request (P-042).</summary>
    public enum RequestResultKind
    {
        Accepted = 0,
        Rejected = 1,
        Cancelled = 2,
        Committed = 3,
    }

    /// <summary>Cursor read outcome; a lagging reader resynchronizes from a snapshot (P-045).</summary>
    public enum CursorOutcome
    {
        Ok = 0,
        CursorExpired = 1,
    }

    /// <summary>Decision of a callback gate evaluated on dispatch and on completion (P-004, P-047).</summary>
    public enum CallbackGateDecision
    {
        Dispatch = 0,

        /// <summary>The completion was stamped by another world incarnation (P-004).</summary>
        DiscardForeignWorld = 1,

        /// <summary>The activation epoch or installation generation is no longer current (P-005, P-047).</summary>
        DiscardStaleActivation = 2,

        /// <summary>The route was retired or suspended, so the work is discarded, not delivered (P-047).</summary>
        DiscardRetiredRoute = 3,

        /// <summary>The publication fence is closed, so nothing is delivered through it (P-047).</summary>
        DiscardPostPublicationFence = 4,
    }

    /// <summary>Retry classification of a diagnostic (00 s9).</summary>
    public enum RetryClassification
    {
        NotRetryable = 0,
        RetrySameInput = 1,
        RequiresChangedInput = 2,
    }

    /// <summary>Phase in which a diagnostic was produced (05 s4, O-09).</summary>
    public enum OperationPhase
    {
        Validation = 0,
        Planning = 1,
        Preparation = 2,
        Apply = 3,
        Publication = 4,
        Cleanup = 5,
    }

    /// <summary>
    /// Stable diagnostic codes required by 00 s9 plus the retention code required by P-007, where bounded
    /// snapshot retention rejects a new lease with SnapshotBackpressure instead of overwriting leased memory.
    /// Literal names match the normative text exactly; <see cref="DiagnosticCodeText"/> maps each value to
    /// that literal string.
    /// </summary>
    public enum DiagnosticCode
    {
        None = 0,
        StaleHandle = 1,
        StalePlan = 2,
        MissingDependency = 3,
        ServiceConflict = 4,
        CapabilityConflict = 5,
        AmbiguousOrder = 6,
        Cycle = 7,
        Ineligible = 8,
        UnsupportedVersion = 9,
        OwnershipConflict = 10,
        BudgetExceeded = 11,
        MigrationRequired = 12,
        ResourceUnavailable = 13,
        Cancelled = 14,
        TooLate = 15,
        IdempotencyConflict = 16,
        ResultExpired = 17,
        ApplyFault = 18,
        TeardownBlocked = 19,
        CursorExpired = 20,
        SnapshotBackpressure = 21,
    }
}

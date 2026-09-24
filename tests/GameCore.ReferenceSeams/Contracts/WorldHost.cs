// Test-only reference seam for the shared GameCore.Contracts surface (TestOnlyMarker.cs).
// World host and execution driver seam for GC-005: world creation/lifecycle, guarded step dispatch, the
// ordered dispatch table and the world/job resource ledger. Derived from docs/game-core/04-unity-integration.md
// s3-4, 05-contracts-and-data-model.md s4-5 and P-002/P-031/P-035/P-036/P-041/P-045/P-047/P-048. Keys and
// descriptors only: no Unity type, no Entity, no JobHandle and no native container appears here.
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.Contracts
{
    /// <summary>Fixed-step temporal configuration (P-036). Integer ticks keep the control path exact.</summary>
    public sealed class FixedStepSettings
    {
        public FixedStepSettings(ulong stepDurationTicks, ulong ticksPerSecond, uint maxStepsPerPump, bool usesUnscaledHostClock)
        {
            StepDurationTicks = stepDurationTicks;
            TicksPerSecond = ticksPerSecond;
            MaxStepsPerPump = maxStepsPerPump;
            UsesUnscaledHostClock = usesUnscaledHostClock;
        }

        /// <summary>Positive step duration (P-036); zero is an invalid configuration.</summary>
        public ulong StepDurationTicks { get; }

        public ulong TicksPerSecond { get; }

        /// <summary>Catch-up limit: at most this many steps per pump; remaining time stays as <see cref="TimeDebt"/>.</summary>
        public uint MaxStepsPerPump { get; }

        public bool UsesUnscaledHostClock { get; }

        public bool IsValid => StepDurationTicks != 0UL && TicksPerSecond != 0UL && MaxStepsPerPump != 0U;
    }

    /// <summary>Request to create one world with a caller-reserved fresh session id (O-01, P-004, P-035).</summary>
    public sealed class WorldCreateRequest
    {
        public WorldCreateRequest(
            WorldId world,
            WorldDefinitionId definition,
            TemporalModel temporalModel,
            PropagationMode mode,
            ContentHash catalogHash,
            OperationId operation,
            FixedStepSettings? fixedStep)
        {
            World = world;
            Definition = definition;
            TemporalModel = temporalModel;
            Mode = mode;
            CatalogHash = catalogHash;
            Operation = operation;
            FixedStep = fixedStep;
        }

        /// <summary>Caller-reserved before allocation; never reused, including on checkpoint restore (P-004).</summary>
        public WorldId World { get; }

        public WorldDefinitionId Definition { get; }

        /// <summary>Chosen at creation; changing it requires checkpoint/recreation in V1 (P-036).</summary>
        public TemporalModel TemporalModel { get; }

        public PropagationMode Mode { get; }

        public ContentHash CatalogHash { get; }

        public OperationId Operation { get; }

        /// <summary>Required for <see cref="TemporalModel.FixedStep"/>; null for a command-driven world.</summary>
        public FixedStepSettings? FixedStep { get; }

        public bool IsValid =>
            !World.Session.IsDefault &&
            !Definition.IsDefault &&
            (TemporalModel != TemporalModel.FixedStep || (FixedStep != null && FixedStep.IsValid));
    }

    /// <summary>Outcome of a world creation request; the created world is not exposed before initial publication.</summary>
    public sealed class WorldCreateResult
    {
        public WorldCreateResult(bool created, WorldId world, WorldLifecycleState lifecycle, DiagnosticCode code, string detail)
        {
            Created = created;
            World = world;
            Lifecycle = lifecycle;
            Code = code;
            Detail = detail ?? string.Empty;
        }

        public bool Created { get; }

        public WorldId World { get; }

        public WorldLifecycleState Lifecycle { get; }

        public DiagnosticCode Code { get; }

        public string Detail { get; }
    }

    /// <summary>Request for one guarded driver advance (O-14, P-036/P-044).</summary>
    public sealed class StepAdvanceRequest
    {
        public StepAdvanceRequest(WorldId world, AssemblyEpoch baseEpoch, LogicalStepId expectedStep, ulong requestedSteps, TimeDebt retainedDebt)
        {
            World = world;
            BaseEpoch = baseEpoch;
            ExpectedStep = expectedStep;
            RequestedSteps = requestedSteps;
            RetainedDebt = retainedDebt;
        }

        public WorldId World { get; }

        public AssemblyEpoch BaseEpoch { get; }

        public LogicalStepId ExpectedStep { get; }

        /// <summary>Steps the temporal driver admitted for this pump; zero is legal and advances nothing (P-036).</summary>
        public ulong RequestedSteps { get; }

        public TimeDebt RetainedDebt { get; }
    }

    /// <summary>Result of one guarded advance. A faulted world publishes no epoch and no snapshot (P-031).</summary>
    public sealed class StepAdvanceResult
    {
        public StepAdvanceResult(
            bool accepted,
            Outcome outcome,
            DiagnosticCode code,
            LogicalStepId step,
            AssemblyEpoch epoch,
            TimeDebt debt,
            SnapshotToken? publishedSnapshot)
        {
            Accepted = accepted;
            Outcome = outcome;
            Code = code;
            Step = step;
            Epoch = epoch;
            Debt = debt;
            PublishedSnapshot = publishedSnapshot;
        }

        public bool Accepted { get; }

        public Outcome Outcome { get; }

        public DiagnosticCode Code { get; }

        public LogicalStepId Step { get; }

        public AssemblyEpoch Epoch { get; }

        /// <summary>Debt remaining after this pump; never silently discarded (P-036).</summary>
        public TimeDebt Debt { get; }

        public SnapshotToken? PublishedSnapshot { get; }
    }

    /// <summary>How one generated dispatch entry is invoked by the host (04 s4).</summary>
    public enum SystemDispatchKind
    {
        /// <summary>Managed <c>SystemBase.Update()</c> entry, invoked directly by the guarded dispatcher.</summary>
        ManagedSystem = 0,

        /// <summary>Unmanaged <c>SystemHandle.Update(World.Unmanaged)</c> entry.</summary>
        UnmanagedSystem = 1,

        /// <summary>Adapter infrastructure group (ingress, step or output), not a protocol stage.</summary>
        InfrastructureGroup = 2,
    }

    /// <summary>
    /// One entry of the ordered dispatch table. Keys and indices only: the adapter maps a key to its concrete
    /// system type through the generated registry, so the seam stays free of Unity types (04 s4, P-039/P-040).
    /// </summary>
    public readonly struct SystemDispatchEntry
    {
        public readonly StageId Stage;
        public readonly FactoryKey SystemKey;
        public readonly SystemDispatchKind Kind;

        /// <summary>Position in the flattened topological order; ascending execution order (P-040).</summary>
        public readonly int DispatchIndex;

        public SystemDispatchEntry(StageId stage, FactoryKey systemKey, SystemDispatchKind kind, int dispatchIndex)
        {
            Stage = stage;
            SystemKey = systemKey;
            Kind = kind;
            DispatchIndex = dispatchIndex;
        }

        public bool Equals(SystemDispatchEntry other) =>
            Stage.Equals(other.Stage) && SystemKey.Equals(other.SystemKey) && Kind == other.Kind && DispatchIndex == other.DispatchIndex;

        public override bool Equals(object? obj) => obj is SystemDispatchEntry other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + Stage.GetHashCode();
                hash = (hash * 31) + SystemKey.GetHashCode();
                hash = (hash * 31) + (int)Kind;
                hash = (hash * 31) + DispatchIndex;
                return hash;
            }
        }

        public override string ToString() =>
            DispatchIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + Kind + ":" + Stage.ToString() + "/" + SystemKey.ToString();
    }

    /// <summary>
    /// Ordered dispatch table installed at the assembly fence. The stock group's exception-swallowing update
    /// loop is bypassed: the host dispatches these entries in this order and stops at the first failure (04 s4).
    /// </summary>
    public sealed class OrderedDispatchTable
    {
        public OrderedDispatchTable(AssemblyEpoch epoch, IReadOnlyList<SystemDispatchEntry>? entries, IReadOnlyList<BufferBinding>? bufferBindings)
        {
            Epoch = epoch;
            Entries = ContractCollections.Freeze(entries);
            BufferBindings = ContractCollections.Freeze(bufferBindings);
        }

        public AssemblyEpoch Epoch { get; }

        /// <summary>Entries in ascending <see cref="SystemDispatchEntry.DispatchIndex"/> order.</summary>
        public IReadOnlyList<SystemDispatchEntry> Entries { get; }

        public IReadOnlyList<BufferBinding> BufferBindings { get; }

        /// <summary>True when the table is a strictly ascending sequence with no duplicate index or system key.</summary>
        public bool IsWellFormed()
        {
            HashSet<int> indices = new HashSet<int>();
            HashSet<FactoryKey> keys = new HashSet<FactoryKey>();
            int previous = -1;
            for (int i = 0; i < Entries.Count; i++)
            {
                SystemDispatchEntry entry = Entries[i];
                if (entry.DispatchIndex <= previous || !indices.Add(entry.DispatchIndex) || !keys.Add(entry.SystemKey))
                {
                    return false;
                }

                previous = entry.DispatchIndex;
            }

            return true;
        }
    }

    /// <summary>Request to dispatch one step's systems through the guarded ordered table (P-031, P-041).</summary>
    public sealed class StageDispatchRequest
    {
        public StageDispatchRequest(WorldId world, AssemblyEpoch baseEpoch, LogicalStepId step, OrderedDispatchTable table)
        {
            World = world;
            BaseEpoch = baseEpoch;
            Step = step;
            Table = table ?? throw new ArgumentNullException(nameof(table));
        }

        public WorldId World { get; }

        public AssemblyEpoch BaseEpoch { get; }

        public LogicalStepId Step { get; }

        public OrderedDispatchTable Table { get; }
    }

    /// <summary>Result of one guarded dispatch; a caught system exception stops dispatch immediately (04 s4).</summary>
    public sealed class DispatchRunResult
    {
        public DispatchRunResult(
            bool completed,
            int dispatchedCount,
            int stoppedAtIndex,
            DiagnosticCode code,
            FactoryKey failingSystemKey,
            IReadOnlyList<FactoryKey>? unreachedSystemKeys)
        {
            Completed = completed;
            DispatchedCount = dispatchedCount;
            StoppedAtIndex = stoppedAtIndex;
            Code = code;
            FailingSystemKey = failingSystemKey;
            UnreachedSystemKeys = ContractCollections.Freeze(unreachedSystemKeys);
        }

        public bool Completed { get; }

        public int DispatchedCount { get; }

        /// <summary>Dispatch index of the failing entry; -1 when dispatch completed.</summary>
        public int StoppedAtIndex { get; }

        public DiagnosticCode Code { get; }

        /// <summary>Default when dispatch completed.</summary>
        public FactoryKey FailingSystemKey { get; }

        /// <summary>Entries that must not have executed after the failure (P-031).</summary>
        public IReadOnlyList<FactoryKey> UnreachedSystemKeys { get; }
    }

    /// <summary>Kind of tracked world resource (03 s1, P-041, P-048).</summary>
    public enum WorldResourceKind
    {
        ManagedLease = 0,
        Subscription = 1,
        SystemRegistration = 2,
        NativeContainer = 3,
        ScheduledJob = 4,
        ScratchAllocation = 5,
        WorldStorage = 6,
        IdentityIndex = 7,
    }

    /// <summary>Acquisition and retirement state of one tracked resource (P-048).</summary>
    public enum ResourceRetirementState
    {
        Acquired = 0,
        Ready = 1,
        Retiring = 2,
        Retired = 3,

        /// <summary>Still reachable by unfinished work; retained, never freed on a timeout (P-048).</summary>
        Quarantined = 4,

        Failed = 5,
    }

    /// <summary>
    /// One world/job ledger record: what was acquired, who owns it, in which order, and whether it is retired
    /// or quarantined. Lease ids are process-local and never serialized as world identity (05 s4, P-048).
    /// </summary>
    public readonly struct WorldResourceRecord
    {
        public readonly Id128 ResourceId;
        public readonly WorldResourceKind Kind;
        public readonly ResourceKey Key;
        public readonly OwnerId Owner;
        public readonly PluginInstanceId Instance;
        public readonly ResourceRetirementState State;

        /// <summary>Resource this one depends on, so retirement runs in reverse dependency order (P-048).</summary>
        public readonly Id128 DependsOn;

        /// <summary>Acquisition ordinal within its instance; leases dispose in reverse acquisition order (P-048).</summary>
        public readonly uint AcquisitionOrdinal;

        /// <summary>Reported size in bytes for allocations and containers; 0 when not applicable.</summary>
        public readonly ulong Bytes;

        public WorldResourceRecord(
            Id128 resourceId,
            WorldResourceKind kind,
            ResourceKey key,
            OwnerId owner,
            PluginInstanceId instance,
            ResourceRetirementState state,
            Id128 dependsOn,
            uint acquisitionOrdinal,
            ulong bytes)
        {
            ResourceId = resourceId;
            Kind = kind;
            Key = key;
            Owner = owner;
            Instance = instance;
            State = state;
            DependsOn = dependsOn;
            AcquisitionOrdinal = acquisitionOrdinal;
            Bytes = bytes;
        }

        public bool IsRetained => State == ResourceRetirementState.Acquired ||
                                  State == ResourceRetirementState.Ready ||
                                  State == ResourceRetirementState.Retiring ||
                                  State == ResourceRetirementState.Quarantined;

        public bool HasDependency => !DependsOn.IsDefault;

        public override string ToString() =>
            Kind + ":" + ResourceId.ToString() + ":" + State;
    }

    /// <summary>One tracked job handle record (P-041, P-048).</summary>
    public readonly struct JobLedgerRecord
    {
        public readonly Id128 JobId;
        public readonly StageId Stage;
        public readonly FactoryKey SystemKey;
        public readonly AssemblyEpoch Epoch;
        public readonly LogicalStepId Step;
        public readonly bool Completed;

        /// <summary>True when the handle must stay tracked because work may still read world memory (P-048).</summary>
        public readonly bool RetainedByQuarantine;

        public JobLedgerRecord(
            Id128 jobId,
            StageId stage,
            FactoryKey systemKey,
            AssemblyEpoch epoch,
            LogicalStepId step,
            bool completed,
            bool retainedByQuarantine)
        {
            JobId = jobId;
            Stage = stage;
            SystemKey = systemKey;
            Epoch = epoch;
            Step = step;
            Completed = completed;
            RetainedByQuarantine = retainedByQuarantine;
        }

        public override string ToString() =>
            "job:" + JobId.ToString() + (Completed ? ":completed" : ":outstanding");
    }

    /// <summary>Snapshot of the world resource and job ledgers (03 s1, P-048).</summary>
    public sealed class WorldResourceLedgerSnapshot
    {
        public WorldResourceLedgerSnapshot(
            WorldId world,
            AssemblyEpoch epoch,
            IReadOnlyList<WorldResourceRecord>? resources,
            IReadOnlyList<JobLedgerRecord>? jobs,
            ulong quarantinedBytes)
        {
            World = world;
            Epoch = epoch;
            Resources = ContractCollections.Freeze(resources);
            Jobs = ContractCollections.Freeze(jobs);
            QuarantinedBytes = quarantinedBytes;
        }

        public WorldId World { get; }

        public AssemblyEpoch Epoch { get; }

        public IReadOnlyList<WorldResourceRecord> Resources { get; }

        public IReadOnlyList<JobLedgerRecord> Jobs { get; }

        /// <summary>Bytes retained behind quarantine; reported separately from live allocation (P-060).</summary>
        public ulong QuarantinedBytes { get; }

        /// <summary>Count of records still quarantined; exhaustion is observable, never a silent drop (06 s5).</summary>
        public int QuarantinedCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Resources.Count; i++)
                {
                    if (Resources[i].State == ResourceRetirementState.Quarantined)
                    {
                        count++;
                    }
                }

                return count;
            }
        }
    }

    /// <summary>World lifecycle change notification; delivered after the publication pointer switched (P-035, P-045).</summary>
    public sealed class WorldLifecycleChange
    {
        public WorldLifecycleChange(WorldId world, WorldLifecycleState previous, WorldLifecycleState current, DiagnosticCode code)
        {
            World = world;
            Previous = previous;
            Current = current;
            Code = code;
        }

        public WorldId World { get; }

        public WorldLifecycleState Previous { get; }

        public WorldLifecycleState Current { get; }

        public DiagnosticCode Code { get; }
    }

    /// <summary>One committed step: the epoch/step pair that identifies its immutable image (P-044, P-045).</summary>
    public sealed class StepCommitEvent
    {
        public StepCommitEvent(SnapshotToken token, IReadOnlyList<CommittedEvent>? events, EventSequence firstEventSequence, ContentHash stateHash)
        {
            Token = token;
            Events = ContractCollections.Freeze(events);
            FirstEventSequence = firstEventSequence;
            StateHash = stateHash;
        }

        public SnapshotToken Token { get; }

        public IReadOnlyList<CommittedEvent> Events { get; }

        /// <summary>Sequence of the first committed event of this step; a step with no events leaves it unchanged.</summary>
        public EventSequence FirstEventSequence { get; }

        /// <summary>Canonical hash of committed stable fields; excludes timestamps and native layout (TEST-022).</summary>
        public ContentHash StateHash { get; }
    }

    /// <summary>
    /// Step-commit and world-lifecycle observer. Delivery happens only after publication switched, so a
    /// subscriber failure is a cleanup diagnostic and cannot roll back a committed step (P-031, P-045).
    /// </summary>
    public interface IWorldLifecycleObserver
    {
        void OnWorldLifecycleChanged(WorldLifecycleChange change);

        void OnStepCommitted(StepCommitEvent committed);
    }

    /// <summary>
    /// Owned world lifecycle seam (P-002, P-035). One host owns one world; worlds never share runtime handles,
    /// and a faulted world is closed to admission until it is recreated (P-031).
    /// </summary>
    public interface IWorldHost
    {
        WorldId World { get; }

        WorldLifecycleState Lifecycle { get; }

        AssemblyEpoch CurrentEpoch { get; }

        LogicalStepId CurrentStep { get; }

        TemporalModel TemporalModel { get; }

        TimeDebt RetainedDebt { get; }

        /// <summary>Creates the world; it is not Running before its initial validated assembly publishes (P-035).</summary>
        WorldCreateResult Create(WorldCreateRequest request);

        /// <summary>Pauses or resumes at a committed boundary (O-26); changes host status, not epoch or step.</summary>
        OperationResult SetRunState(OperationId operation, WorldLifecycleState destination);

        /// <summary>Closes ingress, settles jobs and retires resources in dependency order (O-19, P-048).</summary>
        OperationResult Stop(OperationId operation, string reason);

        WorldResourceLedgerSnapshot ReadResourceLedger();
    }

    /// <summary>
    /// Guarded execution seam (P-031, P-041, P-044). The driver owns step admission, ordered dispatch, fault
    /// latching and commit; a failure after the first live write faults the world and publishes nothing.
    /// </summary>
    public interface IExecutionDriver
    {
        WorldId World { get; }

        AssemblyEpoch CurrentEpoch { get; }

        LogicalStepId CurrentStep { get; }

        /// <summary>True once a post-write failure latched: no further dispatch, step or publication occurs.</summary>
        bool IsFaulted { get; }

        DiagnosticCode FaultCode { get; }

        /// <summary>Admits and runs at most the requested steps, retaining unspent time as debt (P-036).</summary>
        StepAdvanceResult Advance(StepAdvanceRequest request);

        /// <summary>Dispatches one step's ordered table; stops at the first system failure (04 s4).</summary>
        DispatchRunResult Dispatch(StageDispatchRequest request);
    }
}

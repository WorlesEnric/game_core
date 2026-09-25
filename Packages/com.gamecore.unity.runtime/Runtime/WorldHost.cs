#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Execution.Messages;
using GameCore.Unity.Runtime.Messages;
using Unity.Core;
using Unity.Entities;
using UnityWorld = Unity.Entities.World;

namespace GameCore.Unity.Runtime
{
    /// <summary>
    /// Private host surface the guarded driver needs. It stays off the public API: advancing the step counter,
    /// latching a fault and supplying the clock are host authority, not a second control path (P-002).
    /// </summary>
    internal interface IWorldExecutionContext
    {
        WorldId World { get; }

        WorldLifecycleState Lifecycle { get; }

        AssemblyEpoch CurrentEpoch { get; }

        LogicalStepId CurrentStep { get; }

        /// <summary>
        /// The world's bounded message plane, or null when the registration declares none. The driver seals step
        /// input and validates declared buffers through it; the owner commits through its port (P-037, P-043).
        /// </summary>
        WorldMessagePlane? Messages { get; }

        WorldResourceLedger Ledger { get; }

#if GAMECORE_FAULT_INJECTION
        /// <summary>
        /// The world's deterministic fault latch (GC-017). The publisher, the execution driver and the staged
        /// resource gate of one world share this one instance, so a TEST-016 boundary armed by a fault test is the
        /// boundary the real apply path reaches. Neither this member nor the type it names exists in a shipping
        /// compilation.
        /// </summary>
        AssemblyFaultInjection Faults { get; }
#endif

        GameCoreStepGroup StepGroup { get; }

        /// <summary>Supplies the simulation clock of one step before its systems run (04 s3, P-036, P-038).</summary>
        void ApplyStepClock(LogicalStepId step);

        /// <summary>Advances <c>LogicalStepId</c> and exposes the committed image together (P-044).</summary>
        bool TryCommitStep(LogicalStepId committedStep, int dispatchedCount, out SnapshotToken token);

        /// <summary>Enters the terminal fault state; admission stays closed and no epoch or image publishes (P-031).</summary>
        void EnterFaulted(DiagnosticCode code, string detail);

        /// <summary>Consumes admitted command/wake demand by the number of committed steps (P-036, P-037).</summary>
        void ConsumeDemand(ulong steps);
    }

    /// <summary>Outcome of one host frame for one owned world.</summary>
    public readonly struct WorldPumpResult
    {
        public WorldPumpResult(
            bool pumped,
            bool reentrant,
            WorldLifecycleState lifecycle,
            TemporalSample sample,
            StepAdvanceResult? advance,
            bool ingressDispatched,
            bool outputDispatched,
            DiagnosticCode code)
        {
            Pumped = pumped;
            Reentrant = reentrant;
            Lifecycle = lifecycle;
            Sample = sample;
            Advance = advance;
            IngressDispatched = ingressDispatched;
            OutputDispatched = outputDispatched;
            Code = code;
        }

        /// <summary>False when the world was not routable (faulted, stopping, disposed) or a reentrant pump was refused.</summary>
        public bool Pumped { get; }

        public bool Reentrant { get; }

        public WorldLifecycleState Lifecycle { get; }

        public TemporalSample Sample { get; }

        /// <summary>Null when no step was requested, which is the normal idle or paused frame.</summary>
        public StepAdvanceResult? Advance { get; }

        public bool IngressDispatched { get; }

        public bool OutputDispatched { get; }

        public DiagnosticCode Code { get; }
    }

    /// <summary>
    /// The sole authority for one owned protocol world (P-002, P-035). Each world owns exactly one
    /// <see cref="Unity.Entities.World"/>, one ordered dispatch path and one resource ledger; there is no global
    /// mutable host state and no World singleton.
    /// </summary>
    public sealed class UnityWorldHost : IWorldHost, IWorldExecutionContext, ICommandIngress, IDisposable
    {
        /// <summary>Synthetic owner of host-owned infrastructure resources (never plugin state).</summary>
        public static readonly OwnerId HostOwner =
            new OwnerId(new Id128(0x47434F5245484F53UL, 0x545245534F555243UL));

        private const ulong ResourceKeySalt = 0x67637265736B6579UL;

        /// <summary>Host clock rate of a world with no fixed-step configuration: 100 ns ticks (P-036).</summary>
        public const ulong DefaultHostTicksPerSecond = 10_000_000UL;

        private readonly UnityWorld entityWorld;
        private readonly WorldCreateRequest request;
        private readonly UnityWorldRegistration registration;
        private readonly SystemDispatchCatalog catalog;
        private readonly WorldResourceLedger ledger;
        private readonly ObservationHub observations = new ObservationHub();
        private readonly StepPublicationStore publications;
        private readonly ITemporalAccumulator temporal;
        private readonly UnityExecutionDriver driver;
        private readonly GameCoreIngressGroup ingressGroup;
        private readonly WorldMessagePlane? messages;
        private readonly GameCoreStepGroup stepGroup;
        private readonly GameCoreOutputGroup outputGroup;
        private readonly IdSequence resourceKeys = new IdSequence(ResourceKeySalt);

        /// <summary>
        /// One published assembly reference of this world (GC-008). Null until an
        /// <see cref="AssemblyPublisher"/> joins the world; from then on it is the authority for the published
        /// epoch, so a reader of <see cref="CurrentEpoch"/> and a reader of the assembly can never disagree (P-030).
        /// </summary>
        private PublishedAssemblySlot? assemblySlot;

        private Id128 worldStorageResource;
        private Id128 identityIndexResource;
        private Id128 messagePlaneResource;
        private EventSequence lastEventSequence = EventSequence.Zero;

        private WorldLifecycleState lifecycle = WorldLifecycleState.Created;
        private AssemblyEpoch currentEpoch = AssemblyEpoch.Zero;
        private LogicalStepId currentStep = LogicalStepId.Zero;

        /// <summary>
        /// Published composition revision of this world before an assembly publisher joined it. The initial assembly
        /// publishes revision 1 together with epoch 1 (05 s2), so a world that has just been created already
        /// publishes the first publication of the one series.
        /// </summary>
        private CompositionRevision publishedCompositionRevision = CompositionRevision.First;

        private ulong pendingDemand;
        private double domainSeconds;
        private float stepSeconds;
        private ulong hostTicksPerSecond = DefaultHostTicksPerSecond;

        private bool resumeResetsOrigin;
        private bool hostOriginCaptured;
        private ulong hostTimeOrigin;
        private bool pumping;
        private bool disposed;

        private UnityWorldHost(
            UnityWorld entityWorld,
            WorldCreateRequest request,
            UnityWorldRegistration registration)
        {
            this.entityWorld = entityWorld;
            this.request = request;
            this.registration = registration;
            DiagnosticName = registration.WorldName + ":" + request.World.Session.ToString();

            catalog = new SystemDispatchCatalog();
            ledger = new WorldResourceLedger(request.World);
            publications = new StepPublicationStore(request.World, 32, 16);
            temporal = TemporalAccumulators.Create(request.TemporalModel, request.FixedStep, 0UL);

            if (request.TemporalModel == TemporalModel.FixedStep && request.FixedStep != null)
            {
                stepSeconds = (float)(request.FixedStep.StepDurationTicks / (double)request.FixedStep.TicksPerSecond);
                hostTicksPerSecond = request.FixedStep.TicksPerSecond;
            }

            ingressGroup = entityWorld.CreateSystemManaged<GameCoreIngressGroup>();
            stepGroup = entityWorld.CreateSystemManaged<GameCoreStepGroup>();
            outputGroup = entityWorld.CreateSystemManaged<GameCoreOutputGroup>();

            driver = new UnityExecutionDriver(this, temporal, registration.StepPlan);

            if (registration.Messages != null)
            {
                // The plane is created with the world and before the first publication: its routes are generated
                // data, and an invalid declaration refuses world creation instead of mounting a partial plane (P-042).
                messages = new WorldMessagePlane(request.World, registration.Messages, registration.MessageReaders ?? new CommandPayloadReaders());
            }

            ingressGroup.Bind(registration.IngressPlan, AssemblyEpoch.First, catalog, driver);
            stepGroup.Bind(registration.StepPlan, AssemblyEpoch.First, catalog, driver);
            outputGroup.Bind(registration.OutputPlan, AssemblyEpoch.First, catalog, driver);
        }

        public WorldId World => request.World;

        public UnityWorld EntityWorld => entityWorld;

        /// <summary>World name plus session id, so every lifecycle and step log line identifies the incarnation.</summary>
        public string DiagnosticName { get; }

        public UnityWorldRegistration Registration => registration;

        public WorldCreateRequest Request => request;

        public WorldLifecycleState Lifecycle => lifecycle;

        /// <summary>
        /// Published assembly epoch (P-006). Once an assembly publisher has joined the world this reads the one
        /// published view, so the epoch a caller sees and the bindings/schedule an observer sees come from the same
        /// switch; before that it is the epoch the world published at creation.
        /// </summary>
        public AssemblyEpoch CurrentEpoch
        {
            get
            {
                PublishedAssemblySlot? slot = assemblySlot;
                return slot != null ? slot.Read().Epoch : currentEpoch;
            }
        }

        public LogicalStepId CurrentStep => currentStep;

        public TemporalModel TemporalModel => request.TemporalModel;

        public TimeDebt RetainedDebt => temporal.RetainedDebt;

        public ITemporalAccumulator Temporal => temporal;

        public UnityExecutionDriver Driver => driver;

        public WorldResourceLedger Ledger => ledger;

#if GAMECORE_FAULT_INJECTION
        /// <summary>
        /// This world's fault latch (GC-017). It reaches the fault boundaries the publisher, the driver and the
        /// staged-resource gate of this world own, so one arm covers the whole apply boundary. The whole member —
        /// including this allocation — is inside the qualification guard, so a shipping world allocates no latch.
        /// </summary>
        public AssemblyFaultInjection Faults { get; } = new AssemblyFaultInjection();

        AssemblyFaultInjection IWorldExecutionContext.Faults => Faults;
#endif

        /// <summary>This world's registered system catalog: key to concrete instance, never discovered reflectively.</summary>
        public ISystemDispatchCatalog Systems => catalog;
        public StepPublicationStore Publications => publications;

        public ObservationHub Observations => observations;

        /// <summary>
        /// This world's bounded message plane, or null when its registration declares none. It is the only path by
        /// which a command is admitted and a committed event becomes observable (P-042, P-045).
        /// </summary>
        public WorldMessagePlane? Messages => messages;

        public GameCoreIngressGroup IngressGroup => ingressGroup;

        public GameCoreStepGroup StepGroup => stepGroup;

        public GameCoreOutputGroup OutputGroup => outputGroup;

        /// <summary>Host frames routed to this world; the idle-pump counter (TEST-011, TEST-018).</summary>
        public int PumpCount { get; private set; }

        /// <summary>Frames whose pump was refused by the reentrancy guard.</summary>
        public int ReentrantPumpCount { get; private set; }

        public int FaultCount { get; private set; }

        public DiagnosticCode FaultCode { get; private set; }

        public string FaultDetail { get; private set; } = string.Empty;

        /// <summary>Tracked jobs settled by <see cref="Stop"/> before storage was released (P-048).</summary>
        public int SettledJobCount { get; private set; }

        /// <summary>Admitted commands and registered wake requests waiting for a logical step (P-036, P-037).</summary>
        public ulong PendingDemand => pendingDemand;

        /// <summary>Domain seconds advanced by explicit command only; a command-driven world starts at zero (P-038).</summary>
        public double DomainSeconds => domainSeconds;

        /// <summary>Host clock rate this world declares; the pump converts host seconds with it (P-036).</summary>
        public ulong HostTicksPerSecond => hostTicksPerSecond;

        /// <summary>
        /// Host tick the world's simulation clock started from, captured at its first routable pump. A world never
        /// accrues debt for host time before it existed (P-036).
        /// </summary>
        public ulong HostTimeOrigin => hostTimeOrigin;

        public bool IsEntityWorldCreated => entityWorld.IsCreated;

        /// <summary>True while a host pump is executing, i.e. inside the step boundary (P-030).</summary>
        public bool IsPumping => pumping;

        /// <summary>
        /// Joins the one assembly publisher of this world (GC-008). From this point the published view is the
        /// authority for the current assembly epoch, and the assembly publication commit is a single switch (P-030).
        /// </summary>
        internal void AttachAssemblySlot(PublishedAssemblySlot slot)
        {
            if (slot == null)
            {
                throw new ArgumentNullException(nameof(slot));
            }

            GameCoreThreading.RequireMainThread("UnityWorldHost.AttachAssemblySlot");

            assemblySlot = slot;
        }

        /// <summary>
        /// The one serialized assembly commit (P-030, 04 s5): publish the immutable image for the new epoch, move the
        /// epoch mirror, then switch the complete published view in a single reference write. Nothing in this method
        /// throws at a point where the world would be left without a published view, and a refusal changes nothing.
        /// </summary>
        internal bool TryPublishAssembly(AssemblyEpoch nextEpoch, PublishedWorldView view, out SnapshotToken token)
        {
            token = default(SnapshotToken);
            GameCoreThreading.RequireMainThread("UnityWorldHost.TryPublishAssembly");

            PublishedAssemblySlot? slot = assemblySlot;
            if (slot == null)
            {
                return false;
            }

            if (lifecycle != WorldLifecycleState.Running && lifecycle != WorldLifecycleState.Paused)
            {
                return false;
            }

            if (pumping)
            {
                // The assembly may only be replaced at an end-of-step or idle boundary (P-030).
                return false;
            }

            PublishedWorldView current = slot.Read();
            if (nextEpoch.CompareTo(current.Epoch) <= 0)
            {
                return false;
            }

            if (!view.Epoch.Equals(nextEpoch))
            {
                return false;
            }

            var committedToken = new SnapshotToken(World, nextEpoch, currentStep);
            if (!view.Token.Equals(committedToken))
            {
                // The preconstructed view must name exactly the image being published; a mismatch would expose a
                // view whose token points at another publication (P-030).
                return false;
            }

            ContentHash stateHash = StepFingerprint.Compute(World, nextEpoch, currentStep, view.BindingRowCount);
            var committed = new StepCommitEvent(committedToken, null, lastEventSequence, stateHash);
            if (!publications.Publish(committed))
            {
                return false;
            }

            currentEpoch = nextEpoch;
            slot.Switch(view);
            token = committedToken;

            // Delivery happens after the switch, so a subscriber failure is a delivery diagnostic that cannot
            // unpublish the assembly (P-030, P-045).
            observations.NotifyStepCommitted(committed);
            return true;
        }

        /// <summary>
        /// Published composition revision of this world (P-006). It is the revision of the last assembly the world
        /// published, which is the same publication as <see cref="CurrentEpoch"/>; before any assembly publisher or
        /// adoption the world has published only its initial assembly, which 05 s2 places at revision 1.
        /// </summary>
        public CompositionRevision PublishedCompositionRevision
        {
            get
            {
                PublishedAssemblySlot? slot = assemblySlot;
                return slot != null ? slot.Read().Revision : publishedCompositionRevision;
            }
        }

        /// <summary>
        /// Adopts one published composition operation as the world's next assembly (P-006), which is the W1 path when
        /// no <see cref="AssemblyPublisher"/> owns the world's assembly: the composition publication *is* the
        /// published assembly, so the epoch mirror advances to the number the operation reported instead of to an
        /// offset of it. The pair must be exactly the next publication of the one series and the world must be able
        /// to accept it, otherwise nothing changes (P-005, P-006, P-031).
        /// </summary>
        internal bool TryAdoptPublishedComposition(
            CompositionRevision revision,
            AssemblyEpoch epoch,
            out DiagnosticCode code,
            out string detail)
        {
            GameCoreThreading.RequireMainThread("UnityWorldHost.TryAdoptPublishedComposition");

            code = DiagnosticCode.None;
            detail = string.Empty;

            if (lifecycle != WorldLifecycleState.Running && lifecycle != WorldLifecycleState.Paused)
            {
                code = FaultCode == DiagnosticCode.None ? DiagnosticCode.ApplyFault : FaultCode;
                detail = "world " + DiagnosticName + " is " + lifecycle + " and accepts no publication (P-031).";
                return false;
            }

            if (pumping)
            {
                code = DiagnosticCode.TooLate;
                detail = "a step is in progress; a composition publication belongs at a boundary (P-030).";
                return false;
            }

            if (!revision.Value.Equals(epoch.Value))
            {
                // P-006 increments revision and epoch together; a pair that disagrees is two series in one value.
                code = DiagnosticCode.UnsupportedVersion;
                detail = "composition revision " + revision.Value.ToString(CultureInfo.InvariantCulture)
                    + " and epoch " + epoch.Value.ToString(CultureInfo.InvariantCulture)
                    + " name different publications (P-006).";
                return false;
            }

            if (!currentEpoch.TryIncrement(out AssemblyEpoch nextEpoch) ||
                !publishedCompositionRevision.TryIncrement(out CompositionRevision nextRevision))
            {
                code = DiagnosticCode.BudgetExceeded;
                detail = "the world's publication counters are exhausted; a world cannot wrap (P-005).";
                return false;
            }

            if (!nextEpoch.Equals(epoch) || !nextRevision.Equals(revision))
            {
                code = DiagnosticCode.StalePlan;
                detail = "the composition publication is revision "
                    + revision.Value.ToString(CultureInfo.InvariantCulture)
                    + "/epoch " + epoch.Value.ToString(CultureInfo.InvariantCulture)
                    + " but the world's next assembly is revision "
                    + nextRevision.Value.ToString(CultureInfo.InvariantCulture)
                    + "/epoch " + nextEpoch.Value.ToString(CultureInfo.InvariantCulture)
                    + "; a stale composition publication never advances the world (P-006, P-028).";
                return false;
            }

            currentEpoch = nextEpoch;
            publishedCompositionRevision = nextRevision;
            return true;
        }

        /// <summary>
        /// Enters the terminal fault state from the publication path (P-031): admission stays closed, no epoch or
        /// new image is published, and the last committed image remains the only safe observation.
        /// </summary>
        internal void EnterFaulted(DiagnosticCode code, string detail) =>
            ((IWorldExecutionContext)this).EnterFaulted(code, detail);

        /// <summary>
        /// Creates the Unity world, its explicitly approved systems and its initial published assembly. A failure
        /// before the first live write disposes the staging world and reports a rejection (P-028, P-029).
        /// </summary>
        internal static UnityWorldHost? CreateCore(
            WorldCreateRequest request,
            UnityWorldRegistration registration,
            out WorldCreateResult failure)
        {
            string worldName = registration.WorldName + ":" + request.World.Session.ToString();
            var created = new UnityWorld(worldName, WorldFlags.Game);

            try
            {
                var host = new UnityWorldHost(created, request, registration);
                host.CreateRegisteredSystems();
                host.SeedWorldState();
                host.AcquireHostResources();
                host.lifecycle = WorldLifecycleState.Running;
                host.currentEpoch = AssemblyEpoch.First;
                host.currentStep = LogicalStepId.Zero;
                host.PublishInitialAssembly();
                failure = new WorldCreateResult(
                    true,
                    request.World,
                    WorldLifecycleState.Running,
                    DiagnosticCode.None,
                    "World created, its systems registered and its initial assembly published.");
                return host;
            }
            catch (Exception exception)
            {
                if (created.IsCreated)
                {
                    created.Dispose();
                }

                failure = new WorldCreateResult(
                    false,
                    request.World,
                    WorldLifecycleState.Created,
                    DiagnosticCode.ResourceUnavailable,
                    "World creation failed before the first live write: " + exception.GetType().FullName + ": "
                    + exception.Message);
                return null;
            }
        }

        /// <summary>Idempotent creation: a repeated request returns the same live world (O-01).</summary>
        public WorldCreateResult Create(WorldCreateRequest createRequest)
        {
            if (createRequest == null)
            {
                throw new ArgumentNullException(nameof(createRequest));
            }

            GameCoreThreading.RequireMainThread("UnityWorldHost.Create");

            if (!createRequest.World.Session.Equals(World.Session))
            {
                return new WorldCreateResult(false, World, lifecycle, DiagnosticCode.StaleHandle, "Request names another world incarnation.");
            }

            if (!createRequest.Definition.Equals(request.Definition))
            {
                return new WorldCreateResult(false, World, lifecycle, DiagnosticCode.MissingDependency, "Request names a different world definition.");
            }

            if (createRequest.TemporalModel != request.TemporalModel)
            {
                return new WorldCreateResult(false, World, lifecycle, DiagnosticCode.UnsupportedVersion, "Temporal model differs from the host configuration; changing it requires checkpoint/recreation (P-036).");
            }

            return new WorldCreateResult(true, World, lifecycle, DiagnosticCode.None, "World already created; repeated creation returns the same world.");
        }

        /// <summary>Pauses or resumes at a committed boundary; no epoch, revision or step change (P-035, O-26).</summary>
        public OperationResult SetRunState(OperationId operation, WorldLifecycleState destination)
        {
            GameCoreThreading.RequireMainThread("UnityWorldHost.SetRunState");

            if (destination == lifecycle)
            {
                return new OperationResult(operation, Outcome.NoChange, DiagnosticCode.None, CurrentToken());
            }

            if (destination != WorldLifecycleState.Running && destination != WorldLifecycleState.Paused)
            {
                return new OperationResult(operation, Outcome.Rejected, DiagnosticCode.UnsupportedVersion, null);
            }

            if (lifecycle != WorldLifecycleState.Running && lifecycle != WorldLifecycleState.Paused)
            {
                return new OperationResult(operation, Outcome.Rejected, DiagnosticCode.TeardownBlocked, null);
            }

            WorldLifecycleState previous = lifecycle;
            if (destination == WorldLifecycleState.Paused)
            {
                temporal.Pause();
                lifecycle = WorldLifecycleState.Paused;
            }
            else
            {
                // Resume resets the host sample origin at the next pump, so paused host time adds no debt (O-26).
                resumeResetsOrigin = true;
                lifecycle = WorldLifecycleState.Running;
            }

            observations.NotifyLifecycle(new WorldLifecycleChange(World, previous, lifecycle, DiagnosticCode.None));
            return new OperationResult(operation, Outcome.Published, DiagnosticCode.None, CurrentToken());
        }

        /// <summary>
        /// Stops the world: ingress closes, tracked jobs settle, resources retire in reverse dependency order and
        /// only then is the ECS storage disposed. A resource still reachable by unfinished work keeps the world in
        /// Stopping and reports TeardownBlocked instead of a false Disposed (O-19, P-048).
        /// </summary>
        public OperationResult Stop(OperationId operation, string reason)
        {
            GameCoreThreading.RequireMainThread("UnityWorldHost.Stop");
            _ = reason;

            if (lifecycle == WorldLifecycleState.Disposed)
            {
                return new OperationResult(operation, Outcome.NoChange, DiagnosticCode.None, null);
            }

            if (pumping)
            {
                return new OperationResult(operation, Outcome.Rejected, DiagnosticCode.TeardownBlocked, null);
            }

            if (lifecycle != WorldLifecycleState.Stopping)
            {
                WorldLifecycleState previous = lifecycle;
                lifecycle = WorldLifecycleState.Stopping;
                observations.NotifyLifecycle(new WorldLifecycleChange(World, previous, lifecycle, DiagnosticCode.None));
            }

            // 1. Ingress is closed: the lifecycle check already refuses further pumps and steps.
            // 2. Every tracked job settles; already submitted work finishes before its storage is released (P-047).
            SettledJobCount += driver.SettleRetainedJobs();

            // 3. Resources retire in reverse dependency order, attempting every independent cleanup (P-048).
            RetirementOutcome retirement = ledger.RetireAll(default(Id128));

            bool blocked = driver.RetainedJobs.CompletionFailureCount > 0
                || !retirement.AllRetired
                || ledger.RetainedResourceCount > 0;
            if (blocked)
            {
                // A stuck user keeps its resources pinned: elapsed time only reports, it never authorizes free.
                return new OperationResult(
                    operation,
                    Outcome.Rejected,
                    DiagnosticCode.TeardownBlocked,
                    null,
                    CompositionRevision.Zero,
                    CompositionRevision.Zero,
                    currentEpoch,
                    currentEpoch,
                    null,
                    null,
                    RetainedResourceIds());
            }

            lifecycle = WorldLifecycleState.Disposed;
            observations.NotifyLifecycle(new WorldLifecycleChange(World, WorldLifecycleState.Stopping, lifecycle, DiagnosticCode.None));
            DisposeEntityWorld();

            // The world is terminal, so the driver's ledger buffers are released here as well: a stopped world that
            // is never explicitly disposed must not retain native allocations. Dispose() stays idempotent.
            driver.Dispose();
            messages?.Dispose();
            UnityWorldRegistry.Remove(World);
            return new OperationResult(operation, Outcome.Published, DiagnosticCode.None, null);
        }

        public WorldResourceLedgerSnapshot ReadResourceLedger() => ledger.Snapshot(currentEpoch);

        /// <summary>One host frame: ingress, at most the admitted logical steps, then output (04 s3).</summary>
        public WorldPumpResult PumpFrame(ulong hostTicksNow)
        {
            GameCoreThreading.RequireMainThread("UnityWorldHost.PumpFrame");

            if (pumping)
            {
                ReentrantPumpCount++;
                return new WorldPumpResult(
                    false, true, lifecycle, TemporalSample.Idle(temporal.RetainedDebt), null, false, false,
                    DiagnosticCode.TeardownBlocked);
            }

            if (lifecycle != WorldLifecycleState.Running && lifecycle != WorldLifecycleState.Paused)
            {
                return new WorldPumpResult(
                    false, false, lifecycle, TemporalSample.Idle(temporal.RetainedDebt), null, false, false,
                    LifecycleRefusalCode());
            }

            pumping = true;
            try
            {
                PumpCount++;

                if (!hostOriginCaptured)
                {
                    // The world's simulation clock starts at its first routable pump: elapsed host time before the
                    // world existed is not simulation debt (P-036).
                    hostOriginCaptured = true;
                    hostTimeOrigin = hostTicksNow;
                    temporal.ResetHostTimeOrigin(0UL);
                }

                ulong elapsedHostTicks = hostTicksNow >= hostTimeOrigin ? hostTicksNow - hostTimeOrigin : 0UL;

                if (resumeResetsOrigin)
                {
                    temporal.Resume(elapsedHostTicks);
                    resumeResetsOrigin = false;
                }

                // Ingress and output run on host frames even when the world is paused or idle; they never
                // synthesize a simulation step (04 s3, P-036).
                bool ingressOk = DispatchPeripheral(ingressGroup);

                TemporalSample sample = TemporalSample.Idle(temporal.RetainedDebt);
                StepAdvanceResult? advance = null;
                DiagnosticCode code = ingressOk ? DiagnosticCode.None : DiagnosticCode.ApplyFault;

                if (lifecycle == WorldLifecycleState.Running && !driver.IsFaulted)
                {
                    sample = temporal.Sample(elapsedHostTicks, pendingDemand);
                    if (!sample.Accepted)
                    {
                        driver.NoteRejectedSample();
                        code = sample.Code;
                    }
                    else if (sample.HasWork)
                    {
                        advance = driver.Advance(new StepAdvanceRequest(
                            World,
                            currentEpoch,
                            currentStep,
                            sample.AdmittedSteps,
                            temporal.RetainedDebt));

                        if (advance.Accepted && advance.Outcome != Outcome.Published)
                        {
                            code = advance.Code;
                        }
                    }
                }

                bool outputOk = DispatchPeripheral(outputGroup);
                if (!outputOk)
                {
                    code = DiagnosticCode.ApplyFault;
                }

                return new WorldPumpResult(
                    true,
                    false,
                    lifecycle,
                    sample,
                    advance,
                    ingressOk,
                    outputOk,
                    code);
            }
            finally
            {
                pumping = false;
            }
        }

        /// <summary>Admits commands into the bounded host demand that a command-driven world consumes (P-037).</summary>
        public void NotifyCommandAdmitted(uint admittedCommands)
        {
            if (admittedCommands == 0U)
            {
                return;
            }

            pendingDemand += admittedCommands;
        }

        /// <summary>Registers a wake request: a command-driven world advances for it without a player command (P-036).</summary>
        public void RequestWake(uint wakeCount)
        {
            if (wakeCount == 0U)
            {
                return;
            }

            pendingDemand += wakeCount;
        }

        /// <summary>Advances domain time explicitly; a command-driven world never infers it from host frames (P-038).</summary>
        public void AdvanceDomainTime(double seconds)
        {
            if (seconds > 0.0)
            {
                domainSeconds += seconds;
            }
        }

        void IWorldExecutionContext.ApplyStepClock(LogicalStepId step)
        {
            if (request.TemporalModel == TemporalModel.FixedStep)
            {
                entityWorld.SetTime(new TimeData(step.Value * (double)stepSeconds, stepSeconds));
                return;
            }

            // CommandDriven does not derive elapsed simulation seconds from rendered frames or step count (04 s3).
            entityWorld.SetTime(new TimeData(domainSeconds, 0f));
        }

        bool IWorldExecutionContext.TryCommitStep(LogicalStepId committedStep, int dispatchedCount, out SnapshotToken token)
        {
            ContentHash stateHash = StepFingerprint.Compute(World, currentEpoch, committedStep, dispatchedCount);
            token = new SnapshotToken(World, currentEpoch, committedStep);

            // The step's committed events and its immutable image are prepared together and exposed together: a
            // step that faults before this point publishes neither (P-044, P-045).
            IReadOnlyList<CommittedEvent> stepEvents = messages == null
                ? Array.Empty<CommittedEvent>()
                : messages.StageCommittedEvents(committedStep, currentEpoch);
            EventSequence firstEventSequence = messages == null ? lastEventSequence : messages.LastEventSequence;
            var committed = new StepCommitEvent(token, stepEvents, firstEventSequence, stateHash);

            // Publish first, advance the step second, so a refused publication leaves the committed image untouched
            // and the step counter and the exposed image always move together (P-044).
            if (!publications.Publish(committed))
            {
                return false;
            }

            messages?.ConfirmPublished(stepEvents);
            if (messages != null && stepEvents.Count != 0)
            {
                lastEventSequence = messages.LastEventSequence;
            }

            currentStep = committedStep;
            observations.NotifyStepCommitted(committed);
            return true;
        }

        /// <summary>
        /// Host admission of one immutable command envelope (O-13). A world with no declared plane, or one that
        /// cannot execute, refuses before admission so no ledger row is created for work it cannot run (P-031, P-042).
        /// </summary>
        public CommandAdmissionReceipt Submit(CommandEnvelope command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            if (messages == null)
            {
                return new CommandAdmissionReceipt(
                    command.RequestId,
                    new RequestResult(RequestResultKind.Rejected, DiagnosticCode.MissingDependency, default(EventCursor)),
                    AdmissionSequence.Zero);
            }

            if (lifecycle != WorldLifecycleState.Running && lifecycle != WorldLifecycleState.Paused)
            {
                return new CommandAdmissionReceipt(
                    command.RequestId,
                    new RequestResult(RequestResultKind.Rejected, LifecycleRefusalCode(), default(EventCursor)),
                    AdmissionSequence.Zero);
            }

            AdmissionSequence before = messages.Requests.LastAdmissionSequence;
            CommandAdmissionReceipt receipt = messages.SubmitCommand(command, currentStep, currentEpoch);
            if (receipt.Admitted && receipt.AcceptedSequence > before)
            {
                // Only a fresh admission creates demand; retransmission never runs the command twice.
                NotifyCommandAdmitted(1U);
            }

            return receipt;
        }

        void IWorldExecutionContext.EnterFaulted(DiagnosticCode code, string detail)
        {
            if (lifecycle == WorldLifecycleState.Faulted)
            {
                return;
            }

            WorldLifecycleState previous = lifecycle;
            lifecycle = WorldLifecycleState.Faulted;
            FaultCode = code;
            FaultDetail = detail ?? string.Empty;
            FaultCount++;

            // A faulted world can never execute queued commands or wakes; do not expose them as runnable demand.
            pendingDemand = 0UL;

            // Admission stays closed and no epoch or new snapshot is published; the last committed image remains
            // inspectable (P-031).
            observations.NotifyLifecycle(new WorldLifecycleChange(World, previous, lifecycle, code));
        }

        void IWorldExecutionContext.ConsumeDemand(ulong steps)
        {
            pendingDemand = pendingDemand > steps ? pendingDemand - steps : 0UL;
        }

        /// <summary>Releases host-owned storage. Idempotent; a disposed world stays disposed.</summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            OperationResult result = Stop(
                new OperationId(World, HostOwner.Value, ulong.MaxValue),
                "host disposal");
            if (lifecycle != WorldLifecycleState.Disposed)
            {
                throw new InvalidOperationException("World disposal blocked: " + result.Code);
            }

            disposed = true;
        }

        private void CreateRegisteredSystems()
        {
            for (int i = 0; i < registration.Systems.Count; i++)
            {
                SystemRegistration systemRegistration = registration.Systems[i];
                if (!systemRegistration.TryCreate(entityWorld, catalog, out string failure))
                {
                    throw new InvalidOperationException(
                        "Registering system " + systemRegistration.DiagnosticName + " failed: " + failure);
                }
            }
        }

        private void SeedWorldState()
        {
            Action<UnityWorld>? seed = registration.SeedWorldState;
            if (seed != null)
            {
                seed(entityWorld);
            }
        }

        private void AcquireHostResources()
        {
            worldStorageResource = ledger.Acquire(
                WorldResourceKind.WorldStorage,
                new ResourceKey(resourceKeys.Next()),
                HostOwner,
                default(PluginInstanceId),
                default(Id128),
                0UL);

            identityIndexResource = ledger.Acquire(
                WorldResourceKind.IdentityIndex,
                new ResourceKey(resourceKeys.Next()),
                HostOwner,
                default(PluginInstanceId),
                worldStorageResource,
                0UL);

            if (messages != null)
            {
                // The plane's bounded lanes are native allocations with a declared lifetime: tracked here so
                // teardown retires them in reverse dependency order and a stuck user keeps them pinned (P-048).
                messagePlaneResource = ledger.Acquire(
                    WorldResourceKind.NativeContainer,
                    new ResourceKey(resourceKeys.Next()),
                    HostOwner,
                    default(PluginInstanceId),
                    identityIndexResource,
                    messages.RetainedNativeBytes);
            }

            // Every created system is a tracked registration: a system stays allocated while it is scheduled, and
            // removal retires it in reverse dependency order (04 s4, P-048).
            for (int i = 0; i < registration.Systems.Count; i++)
            {
                ledger.Acquire(
                    WorldResourceKind.SystemRegistration,
                    new ResourceKey(resourceKeys.Next()),
                    HostOwner,
                    default(PluginInstanceId),
                    identityIndexResource,
                    0UL);
            }
        }

        private void PublishInitialAssembly()
        {
            // The initial assembly publishes epoch 1 while the step stays 0 (05 s2); a created world becomes Running
            // only after this publication (P-035).
            ContentHash stateHash = StepFingerprint.Compute(World, currentEpoch, currentStep, 0);
            var token = new SnapshotToken(World, currentEpoch, currentStep);
            var committed = new StepCommitEvent(token, null, lastEventSequence, stateHash);
            publications.Publish(committed);

            observations.NotifyLifecycle(
                new WorldLifecycleChange(World, WorldLifecycleState.Created, lifecycle, DiagnosticCode.None));
        }

        private bool DispatchPeripheral(GuardedSystemGroup group)
        {
            DispatchRunResult run = group.DispatchRun(currentEpoch, currentStep, group.BoundTable);
            if (!run.Completed)
            {
                // A failing ingress or output system is a post-write failure like any other (P-031).
                driver.LatchFault(
                    run.Code,
                    "Peripheral dispatch stopped at entry "
                    + run.StoppedAtIndex.ToString(CultureInfo.InvariantCulture) + ".");
                return false;
            }

            NativeFenceTable? fences = group.Fences;
            if (fences != null && !fences.CompleteAndReset())
            {
                driver.LatchFault(DiagnosticCode.ApplyFault, "Completing a peripheral fence failed.");
                return false;
            }

            driver.CompleteStepJobs();
            return true;
        }

        private SnapshotToken? CurrentToken()
            => lifecycle == WorldLifecycleState.Created ? null : new SnapshotToken(World, currentEpoch, currentStep);

        private DiagnosticCode LifecycleRefusalCode()
        {
            switch (lifecycle)
            {
                case WorldLifecycleState.Created:
                    return DiagnosticCode.MissingDependency;
                case WorldLifecycleState.Faulted:
                    return FaultCode == DiagnosticCode.None ? DiagnosticCode.ApplyFault : FaultCode;
                default:
                    return DiagnosticCode.TeardownBlocked;
            }
        }

        /// <summary>Resources still retained (quarantined or not yet retired), reported on a blocked stop (P-048).</summary>
        private IReadOnlyList<Id128> RetainedResourceIds()
        {
            var ids = new List<Id128>();
            WorldResourceLedgerSnapshot snapshot = ledger.Snapshot(currentEpoch);
            for (int i = 0; i < snapshot.Resources.Count; i++)
            {
                if (snapshot.Resources[i].IsRetained)
                {
                    ids.Add(snapshot.Resources[i].ResourceId);
                }
            }

            return ids;
        }

        private void DisposeEntityWorld()
        {
            ingressGroup.Unbind();
            stepGroup.Unbind();
            outputGroup.Unbind();

            if (entityWorld.IsCreated)
            {
                entityWorld.Dispose();
            }
        }
    }

    /// <summary>
    /// Owned-world registry: the application's single live-host index. Baking and Editor inspection worlds are
    /// outside this registry, and each protocol world has exactly one host (04 s3, P-004).
    /// </summary>
    public static class UnityWorldRegistry
    {
        private static readonly List<UnityWorldHost> hosts = new List<UnityWorldHost>();
        private static readonly Dictionary<Id128, UnityWorldHost> bySession = new Dictionary<Id128, UnityWorldHost>();

        public static int Count => hosts.Count;

        public static IReadOnlyList<UnityWorldHost> Hosts => hosts;

        public static bool TryGet(WorldId world, out UnityWorldHost? host)
            => bySession.TryGetValue(world.Session, out host);

        /// <summary>
        /// Resolves the owned host that drives one Unity world. A gameplay system uses this to reach its own world's
        /// bounded ports; it never scans for hosts or assumes a globally reachable one (P-002, P-058).
        /// </summary>
        public static bool TryGetByEntityWorld(UnityWorld world, out UnityWorldHost? host)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            for (int i = 0; i < hosts.Count; i++)
            {
                if (ReferenceEquals(hosts[i].EntityWorld, world))
                {
                    host = hosts[i];
                    return true;
                }
            }

            host = null;
            return false;
        }

        /// <summary>
        /// Creates one owned world. A repeated request for a live session returns that same world; a second world
        /// with the same session id is never created (P-004, O-01).
        /// </summary>
        public static bool TryCreate(
            WorldCreateRequest request,
            UnityWorldRegistration registration,
            out UnityWorldHost? host,
            out WorldCreateResult result)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            GameCoreThreading.RequireMainThread("UnityWorldRegistry.TryCreate");
            host = null;

            if (!request.IsValid)
            {
                result = new WorldCreateResult(
                    false,
                    request.World,
                    WorldLifecycleState.Created,
                    DiagnosticCode.UnsupportedVersion,
                    "The create request is invalid: a world session, a definition and, for FixedStep, a valid fixed-step configuration are required (O-01, P-036).");
                return false;
            }

            if (bySession.TryGetValue(request.World.Session, out UnityWorldHost? existing) && existing != null)
            {
                host = existing;
                result = new WorldCreateResult(
                    true,
                    request.World,
                    existing.Lifecycle,
                    DiagnosticCode.None,
                    "A live host for this session already exists; repeated creation returns the same world (O-01).");
                return true;
            }

            if (!registration.TryValidate(out DiagnosticCode code, out string detail))
            {
                result = new WorldCreateResult(false, request.World, WorldLifecycleState.Created, code, detail);
                return false;
            }

            UnityWorldHost? created = UnityWorldHost.CreateCore(request, registration, out WorldCreateResult failure);
            if (created == null)
            {
                result = failure;
                return false;
            }

            hosts.Add(created);
            bySession.Add(request.World.Session, created);
            host = created;
            result = failure;
            return true;
        }

        /// <summary>Forgets one world; returns false when it was not registered.</summary>
        public static bool Remove(WorldId world)
        {
            if (!bySession.TryGetValue(world.Session, out UnityWorldHost? host) || host == null)
            {
                return false;
            }

            bySession.Remove(world.Session);
            hosts.Remove(host);
            return true;
        }

        /// <summary>
        /// Disposes every surviving host and clears the registry. This is the domain-reload reset path: no static
        /// host reference survives a new Play Mode session (04 s9).
        /// </summary>
        public static int ResetAll()
        {
            int disposedCount = 0;
            for (int i = hosts.Count - 1; i >= 0; i--)
            {
                hosts[i].Dispose();
                disposedCount++;
            }

            hosts.Clear();
            bySession.Clear();
            return disposedCount;
        }
    }
}

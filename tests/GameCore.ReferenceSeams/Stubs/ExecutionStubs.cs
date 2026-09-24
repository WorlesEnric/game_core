// Test-only deterministic stub (namespace GameCore.TestFixtures) for the W0 reference seam.
// Execution-side doubles for W1 peers: a scripted apply bridge, a migrator and a resource factory with
// explicit counters. Failure behavior is scripted, never timing-dependent (P-030, P-031, P-048).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.TestFixtures
{
    /// <summary>Stub fenced world state handed to an apply path: base epoch plus job settlement (P-030).</summary>
    public sealed class StubFencedWorldState : IFencedWorldState
    {
        public StubFencedWorldState(WorldId world, AssemblyEpoch baseEpoch, bool jobsSettled)
        {
            World = world;
            BaseEpoch = baseEpoch;
            JobsSettled = jobsSettled;
        }

        public WorldId World { get; }

        public AssemblyEpoch BaseEpoch { get; }

        public bool JobsSettled { get; }
    }

    /// <summary>Raised by the stub resource factory when preparation is scripted to fail (P-049).</summary>
    public sealed class ResourcePreparationException : Exception
    {
        public ResourcePreparationException(DiagnosticCode code, string message)
            : base(message)
        {
            Code = code;
        }

        public DiagnosticCode Code { get; }
    }

    /// <summary>
    /// Inert gate for a staged resource. Prepared callbacks and subscriptions sit behind a closed gate until
    /// their activation publishes; closing is irreversible for the activation epoch (P-047).
    /// </summary>
    public sealed class StubResourceGate : IResourceGate
    {
        public StubResourceGate(bool isOpen)
        {
            IsOpen = isOpen;
        }

        public bool IsOpen { get; private set; }

        public int CloseCount { get; private set; }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;
            CloseCount++;
        }

        /// <summary>Opens the gate exactly once at publication; a closed gate never reopens (P-047).</summary>
        public bool OpenOnPublication()
        {
            if (CloseCount != 0)
            {
                return false;
            }

            IsOpen = true;
            return true;
        }
    }

    /// <summary>
    /// Stub lease with disposal-once semantics, a recorded disposer key, an explicit readiness state and an
    /// observable gate (P-007, P-048).
    /// </summary>
    public sealed class StubResourceLease : IManagedResourceLease
    {
        private readonly Action<Id128> onDispose;

        public StubResourceLease(
            ResourceKey resource,
            Id128 leaseId,
            AsyncWorkToken token,
            FactoryKey disposer,
            IResourceGate gate,
            Action<Id128> onDispose)
        {
            Resource = resource;
            LeaseId = leaseId;
            Token = token;
            Disposer = disposer;
            Gate = gate ?? throw new ArgumentNullException(nameof(gate));
            this.onDispose = onDispose;
        }

        public ResourceKey Resource { get; }

        public Id128 LeaseId { get; }

        public AsyncWorkToken Token { get; }

        /// <summary>Staged until publication; a pending lease is not usable gameplay state (P-038).</summary>
        public ResourceReadiness Readiness { get; private set; } = ResourceReadiness.Pending;

        public FactoryKey Disposer { get; }

        public IResourceGate Gate { get; }

        public bool IsDisposed { get; private set; }

        public int DisposeCount { get; private set; }

        /// <summary>Marks the lease ready at publication, behind its still-gated callback path (P-047).</summary>
        public void MarkReady() => Readiness = ResourceReadiness.Ready;

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;
            DisposeCount++;
            Gate.Close();
            onDispose(LeaseId);
        }
    }

    /// <summary>Stub managed-resource factory: deterministic lease ids, counted prepares and disposals.</summary>
    public sealed class StubResourceFactory : IManagedResourceFactory
    {
        private readonly DeterministicIds leaseIds = new DeterministicIds(0x7265736F75726365UL);

        public int PrepareCount { get; private set; }

        public int DisposeCount { get; private set; }

        public int RequestedAheadOfCompletionCount { get; private set; }

        /// <summary>When set, the next preparation throws and clears the flag (P-049 retry fixture).</summary>
        public bool FailNextPrepare { get; set; }

        /// <summary>Disposer key recorded on every lease this factory hands out (P-048).</summary>
        public FactoryKey DisposerKey { get; set; }

        public IManagedResourceLease Prepare(ManagedResourceRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (FailNextPrepare)
            {
                FailNextPrepare = false;
                throw new ResourcePreparationException(DiagnosticCode.ResourceUnavailable, "Scripted preparation failure.");
            }

            PrepareCount++;
            return new StubResourceLease(
                request.Resource,
                leaseIds.NextId(),
                request.Token,
                DisposerKey,
                new StubResourceGate(false),
                OnLeaseDisposed);
        }

        /// <summary>Records an acquisition attempt made after its async token was already discarded.</summary>
        public void NoteLateAcquisition(AsyncWorkToken token, ICallbackGate gate)
        {
            if (gate == null)
            {
                throw new ArgumentNullException(nameof(gate));
            }

            if (gate.Evaluate(token) != CallbackGateDecision.Dispatch)
            {
                RequestedAheadOfCompletionCount++;
            }
        }

        private void OnLeaseDisposed(Id128 leaseId)
        {
            _ = leaseId;
            DisposeCount++;
        }
    }

    /// <summary>
    /// Stub generated apply/migration seam. Faulting targets are listed explicitly so a post-write fault is
    /// reproducible without injected timing (P-031).
    /// </summary>
    public sealed class ScriptedRecipeApplyBridge : IRecipeApplyBridge, IStateMigrator
    {
        private readonly Dictionary<TargetHandle, bool> faultingTargets = new Dictionary<TargetHandle, bool>();
        private readonly List<RecipeApplyRequest> applied = new List<RecipeApplyRequest>();
        private readonly List<MigrationRequest> migrations = new List<MigrationRequest>();
        private readonly AssemblyEpoch expectedBaseEpoch;

        public ScriptedRecipeApplyBridge(AssemblyEpoch expectedBaseEpoch)
        {
            this.expectedBaseEpoch = expectedBaseEpoch;
        }

        public IReadOnlyList<RecipeApplyRequest> Applied => applied;

        public IReadOnlyList<MigrationRequest> Migrations => migrations;

        /// <summary>When set, every migration request fails and leaves the old live state (P-054).</summary>
        public bool FailMigration { get; set; }

        public void FaultAfterWrite(TargetHandle target) => faultingTargets[target] = true;

        public RecipeApplyResult ApplyRecipe(RecipeApplyRequest request, IFencedWorldState world)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (!world.BaseEpoch.Equals(expectedBaseEpoch))
            {
                return new RecipeApplyResult(ApplyOutcome.RejectedBeforeWrite, DiagnosticCode.StalePlan, null);
            }

            if (!world.JobsSettled)
            {
                return new RecipeApplyResult(ApplyOutcome.RejectedBeforeWrite, DiagnosticCode.AmbiguousOrder, null);
            }

            if (faultingTargets.ContainsKey(request.Target))
            {
                return new RecipeApplyResult(ApplyOutcome.FaultedAfterWrite, DiagnosticCode.ApplyFault, null);
            }

            applied.Add(request);
            return new RecipeApplyResult(ApplyOutcome.Applied, DiagnosticCode.None, new[] { request.Recipe.Schema.Id.Value });
        }

        public MigrationResult Migrate(MigrationRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (FailMigration)
            {
                return new MigrationResult(false, DiagnosticCode.MigrationRequired, null);
            }

            migrations.Add(request);
            return new MigrationResult(true, DiagnosticCode.None, request.OldState);
        }
    }
}

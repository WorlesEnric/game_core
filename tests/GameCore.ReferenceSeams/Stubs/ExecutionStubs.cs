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

    /// <summary>Stub lease with disposal-once semantics and observable counters (P-048).</summary>
    public sealed class StubResourceLease : IManagedResourceLease
    {
        private readonly Action<Id128> onDispose;

        public StubResourceLease(ResourceKey resource, Id128 leaseId, AsyncWorkToken token, Action<Id128> onDispose)
        {
            Resource = resource;
            LeaseId = leaseId;
            Token = token;
            this.onDispose = onDispose;
        }

        public ResourceKey Resource { get; }

        public Id128 LeaseId { get; }

        public AsyncWorkToken Token { get; }

        public bool IsDisposed { get; private set; }

        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;
            DisposeCount++;
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
            return new StubResourceLease(request.Resource, leaseIds.NextId(), request.Token, OnLeaseDisposed);
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

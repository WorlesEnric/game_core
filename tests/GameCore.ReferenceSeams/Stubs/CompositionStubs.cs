// Test-only deterministic stub (namespace GameCore.TestFixtures) for the W0 reference seam.
// Composition control-lane double for W1 peers: in-memory scopes, installs and an operation ledger whose
// admission, duplicate, conflict, stale and retention outcomes are reproducible from call order alone. No
// clock, no thread, no dictionary enumeration order decides an outcome (P-008, P-046, P-050, P-051).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.TestFixtures
{
    /// <summary>Deterministic in-memory composition host implementing the shared <see cref="ICompositionHost"/> seam.</summary>
    public sealed class StubCompositionHost : ICompositionHost
    {
        private readonly Dictionary<OperationId, OperationLedgerEntry> ledger = new Dictionary<OperationId, OperationLedgerEntry>();
        private readonly List<OperationId> ledgerOrder = new List<OperationId>();
        private readonly HashSet<OperationId> expired = new HashSet<OperationId>();
        private readonly Dictionary<Id128, ScopeSnapshot> scopes = new Dictionary<Id128, ScopeSnapshot>();
        private readonly Dictionary<Id128, InstallSnapshot> installs = new Dictionary<Id128, InstallSnapshot>();
        private readonly int retainedResultLimit;

        public StubCompositionHost(
            WorldId world,
            CompositionRevision publishedRevision,
            AssemblyEpoch publishedEpoch,
            PropagationMode mode,
            CompositionHostSettings settings,
            int retainedResultLimit)
        {
            World = world;
            PublishedRevision = publishedRevision;
            PublishedEpoch = publishedEpoch;
            Mode = mode;
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (retainedResultLimit <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(retainedResultLimit), "A retained-result limit must be positive.");
            }

            this.retainedResultLimit = retainedResultLimit;
        }

        public WorldId World { get; }

        public CompositionHostSettings Settings { get; }

        public CompositionRevision PublishedRevision { get; private set; }

        public AssemblyEpoch PublishedEpoch { get; private set; }

        public PropagationMode Mode { get; private set; }

        public int SubmittedCount { get; private set; }

        public int DuplicateCount { get; private set; }

        public int ConflictCount { get; private set; }

        public int StaleCount { get; private set; }

        public int CapacityRejectedCount { get; private set; }

        public int ExpireCount { get; private set; }

        public void RegisterScope(ScopeSnapshot scope)
        {
            if (scope == null)
            {
                throw new ArgumentNullException(nameof(scope));
            }

            scopes[scope.Scope.Value] = scope;
        }

        public void RegisterInstall(InstallSnapshot install)
        {
            if (install == null)
            {
                throw new ArgumentNullException(nameof(install));
            }

            installs[install.Record.Instance.Value] = install;
        }

        public OperationStatusHandle Submit(CompositionEditRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            OperationStatusHandle handle = new OperationStatusHandle(request.Operation, PublishedRevision);
            ContentHash inputHash = HashOf(request.EditPayload);

            if (ledger.TryGetValue(request.Operation, out OperationLedgerEntry existing))
            {
                if (existing.InputHash == inputHash)
                {
                    // Idempotent retransmission: the original outcome is returned unchanged (P-050).
                    DuplicateCount++;
                    return existing.Handle;
                }

                // Conflicting reuse is rejected without overwriting the original result (P-050).
                ConflictCount++;
                return existing.Handle;
            }

            SubmittedCount++;
            OperationLedgerEntry entry;
            if (request.ExpectedRevision.Value != PublishedRevision.Value)
            {
                StaleCount++;
                entry = Entry(request.Operation, inputHash, Outcome.Rejected, DiagnosticCode.StalePlan);
            }
            else if (ledgerOrder.Count >= Settings.Capacity.MaxQueuedOperations)
            {
                CapacityRejectedCount++;
                entry = Entry(request.Operation, inputHash, Outcome.Rejected, DiagnosticCode.BudgetExceeded);
            }
            else
            {
                PublishedRevision = new CompositionRevision(PublishedRevision.Value + 1UL);
                PublishedEpoch = new AssemblyEpoch(PublishedEpoch.Value + 1UL);
                entry = new OperationLedgerEntry(
                    new OperationStatusHandle(request.Operation, new CompositionRevision(PublishedRevision.Value - 1UL)),
                    inputHash,
                    Outcome.Published,
                    DiagnosticCode.None,
                    PublishedRevision,
                    PublishedEpoch,
                    new SnapshotToken(World, PublishedEpoch, LogicalStepId.Zero));
            }

            ledger.Add(request.Operation, entry);
            ledgerOrder.Add(request.Operation);
            Trim();
            return entry.Handle;
        }

        public OperationReadResult Read(OperationStatusHandle handle)
        {
            if (expired.Contains(handle.Operation))
            {
                return OperationReadResult.Expired();
            }

            return ledger.TryGetValue(handle.Operation, out OperationLedgerEntry found)
                ? OperationReadResult.Found(found)
                : OperationReadResult.Unknown();
        }

        public CompositionStateSnapshot Snapshot()
        {
            List<ScopeSnapshot> orderedScopes = new List<ScopeSnapshot>();
            foreach (ScopeSnapshot scope in scopes.Values)
            {
                orderedScopes.Add(scope);
            }

            orderedScopes.Sort(CompareScopes);

            List<InstallSnapshot> orderedInstalls = new List<InstallSnapshot>();
            foreach (InstallSnapshot install in installs.Values)
            {
                orderedInstalls.Add(install);
            }

            orderedInstalls.Sort(CompareInstalls);

            return new CompositionStateSnapshot(World, PublishedRevision, PublishedEpoch, LogicalStepId.Zero, Mode, orderedScopes, orderedInstalls);
        }

        public ScopeSnapshot? FindScope(ScopeId scope) =>
            scopes.TryGetValue(scope.Value, out ScopeSnapshot? found) ? found : null;

        public InstallSnapshot? FindInstall(PluginInstanceId instance) =>
            installs.TryGetValue(instance.Value, out InstallSnapshot? found) ? found : null;

        /// <summary>Sets the world-level mode directly; a real host does this through an admitted proposal (P-014).</summary>
        public void SetModeForFixture(PropagationMode mode) => Mode = mode;

        private OperationLedgerEntry Entry(OperationId operation, ContentHash inputHash, Outcome outcome, DiagnosticCode code) =>
            new OperationLedgerEntry(
                new OperationStatusHandle(operation, PublishedRevision),
                inputHash,
                outcome,
                code,
                PublishedRevision,
                PublishedEpoch,
                null);

        private void Trim()
        {
            while (ledgerOrder.Count > retainedResultLimit)
            {
                OperationId oldest = ledgerOrder[0];
                ledgerOrder.RemoveAt(0);
                ledger.Remove(oldest);
                expired.Add(oldest);
                ExpireCount++;
            }
        }

        private static ContentHash HashOf(FrozenPayload payload)
        {
            byte[] copy = new byte[payload.Length];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = payload.Bytes[i];
            }

            return ContentHash.Compute(copy);
        }

        private static int CompareScopes(ScopeSnapshot left, ScopeSnapshot right) =>
            left.Scope.Value.CompareTo(right.Scope.Value);

        private static int CompareInstalls(InstallSnapshot left, InstallSnapshot right) =>
            left.Record.Instance.Value.CompareTo(right.Record.Instance.Value);
    }
}

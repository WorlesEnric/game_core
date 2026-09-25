// GameCore.Composition.Diagnostics — staged-operation status (GC-016).
//
// O-25's probe is exact: "repeated; every effective capability has complete provenance and losing/excluded candidate
// reasons" *and* "distinguish published state from staged status explicitly". `IStagedPlanDiagnostics` (05 s5) is the
// frozen name for the second half, and this file is its production-status companion: one immutable status value per
// operation that says which kind of state it is describing.
//
// The distinction is a field, not a convention: `StagedStatusSource` is `PublishedComposition` only when a
// publication really happened, `StagedPlan` while a proposal is admitted and unpublished, and `None` when the ledger
// has nothing (unknown or expired). A caller therefore cannot read a staged plan as world observation by accident.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Composition.Diagnostics
{
    /// <summary>Which state one operation status describes; the two are never conflated (00 s9, 05 s5).</summary>
    public enum StagedStatusSource
    {
        /// <summary>No retained ledger row: unknown or expired, and no staged proposal either.</summary>
        None = 0,

        /// <summary>The published composition: the operation settled and its result names a revision/epoch.</summary>
        PublishedComposition = 1,

        /// <summary>A staged proposal that has not published; it inspects a plan, never the running world.</summary>
        StagedPlan = 2,
    }

    /// <summary>
    /// Immutable status of one operation: its ledger read, its staged proposal when it has one, and the resource
    /// leases it prepared. Nothing here mutates the lane or the world (P-051).
    /// </summary>
    public sealed class StagedOperationStatus
    {
        public StagedOperationStatus(
            OperationId operation,
            OperationReadOutcome readOutcome,
            Outcome outcome,
            DiagnosticCode code,
            CompositionRevision publishedRevision,
            AssemblyEpoch publishedEpoch,
            SnapshotToken? publishedSnapshot,
            StagedStatusSource source,
            bool isTerminal,
            bool hasStagedPlan,
            ContentHash stagedPlanHash,
            bool stagedIsNoChange,
            DiagnosticCode stagedCode,
            int stagedActivationCount,
            int stagedRetirementCount,
            int stagedDispositionCount,
            int stagedResourceLeaseCount,
            string detail)
        {
            Operation = operation;
            ReadOutcome = readOutcome;
            Outcome = outcome;
            Code = code;
            CodeText = DiagnosticCodeText.Of(code);
            PublishedRevision = publishedRevision;
            PublishedEpoch = publishedEpoch;
            PublishedSnapshot = publishedSnapshot;
            Source = source;
            IsTerminal = isTerminal;
            HasStagedPlan = hasStagedPlan;
            StagedPlanHash = stagedPlanHash ?? ContentHash.Empty;
            StagedIsNoChange = stagedIsNoChange;
            StagedCode = stagedCode;
            StagedActivationCount = stagedActivationCount;
            StagedRetirementCount = stagedRetirementCount;
            StagedDispositionCount = stagedDispositionCount;
            StagedResourceLeaseCount = stagedResourceLeaseCount;
            Detail = detail ?? string.Empty;
        }

        public OperationId Operation { get; }

        /// <summary>How the ledger read resolved: found, unknown or expired (05 s5).</summary>
        public OperationReadOutcome ReadOutcome { get; }

        public Outcome Outcome { get; }

        /// <summary>The operation's terminal or pending code; `None` while nothing was decided.</summary>
        public DiagnosticCode Code { get; }

        public string CodeText { get; }

        /// <summary>Published revision/epoch of a settled operation; zero when it never published.</summary>
        public CompositionRevision PublishedRevision { get; }

        public AssemblyEpoch PublishedEpoch { get; }

        /// <summary>The image a settled operation published, or null for a rejection, `NoChange` or a staged plan.</summary>
        public SnapshotToken? PublishedSnapshot { get; }

        /// <summary>The load-bearing distinction: published composition, staged plan, or nothing.</summary>
        public StagedStatusSource Source { get; }

        public bool IsTerminal { get; }

        public bool HasStagedPlan { get; }

        /// <summary>Semantic hash of the staged proposal; empty when there is none (05 s4).</summary>
        public ContentHash StagedPlanHash { get; }

        public bool StagedIsNoChange { get; }

        /// <summary>Code of the staged proposal itself: `None` when it validated, its refusal code otherwise.</summary>
        public DiagnosticCode StagedCode { get; }

        public string StagedCodeText => DiagnosticCodeText.Of(StagedCode);

        /// <summary>Installations the staged proposal would activate, in provider-before-consumer order (P-012).</summary>
        public int StagedActivationCount { get; }

        /// <summary>Installations the staged proposal would retire, in teardown order (P-046, P-048).</summary>
        public int StagedRetirementCount { get; }

        /// <summary>State dispositions the staged proposal declares (P-032, P-033).</summary>
        public int StagedDispositionCount { get; }

        /// <summary>Managed-resource leases prepared and still inert for this operation (P-029).</summary>
        public int StagedResourceLeaseCount { get; }

        /// <summary>Human-readable note; never an input to precedence or identity.</summary>
        public string Detail { get; }

        /// <summary>True when this status describes state an observer may treat as committed world state.</summary>
        public bool DescribesPublishedState =>
            Source == StagedStatusSource.PublishedComposition && PublishedSnapshot != null;

        public override string ToString() =>
            "StagedStatus(" + Operation.ToString() + ", " + Source.ToString() + ", " + Outcome.ToString()
            + ", staged=" + (HasStagedPlan ? "yes" : "no") + ")";
    }

    /// <summary>
    /// Reads staged-operation status from a real control lane. Every value comes from the lane's own ledger, staged
    /// plan and staged resource table; nothing is inferred and nothing is mutated (P-051).
    /// </summary>
    public sealed class StagedStatusReader
    {
        private readonly CompositionHost lane;

        public StagedStatusReader(CompositionHost lane)
        {
            this.lane = lane ?? throw new ArgumentNullException(nameof(lane));
        }

        public CompositionHost Lane => lane;

        public int ReadCount { get; private set; }

        /// <summary>Reads that found a staged, unpublished proposal.</summary>
        public int StagedCount { get; private set; }

        /// <summary>Reads whose ledger row was outside retention.</summary>
        public int ExpiredCount { get; private set; }

        /// <summary>Reads that found no row at all.</summary>
        public int UnknownCount { get; private set; }

        /// <summary>Reads the status of one operation by identity (O-25).</summary>
        public StagedOperationStatus Read(OperationId operation) =>
            Read(new OperationStatusHandle(operation, lane.Committed.Revision));

        /// <summary>Reads the status of one operation by retrieval handle.</summary>
        public StagedOperationStatus Read(OperationStatusHandle handle)
        {
            ReadCount++;
            OperationReadResult read = lane.Read(handle);
            CompositionEditPlan? plan = lane.StagedPlan(handle.Operation);
            IReadOnlyList<StagedLease> leases = plan != null
                ? lane.StagedLeases(handle.Operation)
                : Array.Empty<StagedLease>();
            OperationLedgerEntry? entry = read.Entry;

            StagedStatusSource source = StagedStatusSource.None;
            if (plan != null)
            {
                source = StagedStatusSource.StagedPlan;
                StagedCount++;
            }
            else if (entry != null && entry.PublishedEpoch.Value != 0UL)
            {
                source = StagedStatusSource.PublishedComposition;
            }

            if (read.Outcome == OperationReadOutcome.Expired)
            {
                ExpiredCount++;
            }
            else if (read.Outcome == OperationReadOutcome.Unknown)
            {
                UnknownCount++;
            }

            return new StagedOperationStatus(
                handle.Operation,
                read.Outcome,
                entry != null ? entry.Outcome : Outcome.Pending,
                entry != null ? entry.Code : read.Code,
                entry != null ? entry.PublishedRevision : CompositionRevision.Zero,
                entry != null ? entry.PublishedEpoch : AssemblyEpoch.Zero,
                entry != null ? entry.PublishedSnapshot : null,
                source,
                entry != null && entry.IsTerminal,
                plan != null,
                plan != null ? plan.PlanHash() : ContentHash.Empty,
                plan != null && plan.IsNoChange,
                plan != null ? plan.Code : DiagnosticCode.None,
                plan != null ? plan.ActivationOrder.Count : 0,
                plan != null ? plan.RetiredInstances.Count : 0,
                plan != null ? plan.StateDispositions.Count : 0,
                leases.Count,
                Describe(read, plan, source));
        }

        private static string Describe(OperationReadResult read, CompositionEditPlan? plan, StagedStatusSource source)
        {
            switch (source)
            {
                case StagedStatusSource.StagedPlan:
                    return "staged proposal of " + (plan != null && plan.Succeeded ? "valid" : "refused")
                        + " shape; no live change before publication (P-029)";
                case StagedStatusSource.PublishedComposition:
                    return "published composition result";
                default:
                    return read.Outcome == OperationReadOutcome.Expired
                        ? "the operation's retained result window has closed (P-050)"
                        : "the lane never admitted this operation";
            }
        }

        public override string ToString() =>
            "StagedStatusReader(reads=" + ReadCount.ToString(CultureInfo.InvariantCulture)
            + ", staged=" + StagedCount.ToString(CultureInfo.InvariantCulture) + ")";
    }
}

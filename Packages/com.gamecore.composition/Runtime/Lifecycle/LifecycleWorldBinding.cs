// GameCore.Composition — the seam a lifecycle change needs into the live world (P-046, P-047, P-048).
//
// P-048 fixes the teardown order: close ingress; settle the current step; fence tracked readers/jobs;
// retract/publicize the new assembly; retire old resources in reverse dependency order. Four of those five steps
// are things only the world backend can do — closing a command route, reaching a step boundary, retracting
// derived binding rows, and reporting which native work is still in flight. The composition package is
// Unity-free, so those four are expressed here as one interface and implemented by the Unity glue
// (`GameCore.Unity.Runtime/Lifecycle`) or by a recording double in tests.
//
// The interface is deliberately narrow. It is not an "unload hook": every method names one step of the P-048
// order and returns the observation the sequencer records. A backend that cannot do a step must say so
// (`LifecycleStepSettlement.Faulted` / an empty fence), because the sequencer refuses to report `Disposed` on the
// strength of an assumption.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Composition
{
    /// <summary>How one step boundary resolved when a composition change asked the world to settle (P-030).</summary>
    public sealed class LifecycleStepSettlement
    {
        public LifecycleStepSettlement(bool settled, bool faulted, LogicalStepId committedStep, string detail)
        {
            Settled = settled;
            Faulted = faulted;
            CommittedStep = committedStep;
            Detail = detail ?? string.Empty;
        }

        /// <summary>True when no step is in flight any more: the boundary the change publishes at has been reached.</summary>
        public bool Settled { get; }

        /// <summary>True when settling faulted; a fault keeps the change from publishing an assembly (P-031).</summary>
        public bool Faulted { get; }

        /// <summary>Last committed logical step observed at the boundary; settling never advances it (P-030).</summary>
        public LogicalStepId CommittedStep { get; }

        public string Detail { get; }

        public static LifecycleStepSettlement At(LogicalStepId step) => new LifecycleStepSettlement(true, false, step, string.Empty);

        public static LifecycleStepSettlement Fault(LogicalStepId step, string detail) => new LifecycleStepSettlement(false, true, step, detail);
    }

    /// <summary>What closing one activation's ingress actually closed, so the sequencer reports a fact (P-047).</summary>
    public sealed class LifecycleIngressClosure
    {
        public LifecycleIngressClosure(PluginInstanceId instance, ActivationStamp stamp, int closedRoutes, int cancelledPending)
        {
            Instance = instance;
            Stamp = stamp;
            ClosedRoutes = closedRoutes;
            CancelledPending = cancelledPending;
        }

        public PluginInstanceId Instance { get; }

        /// <summary>Generation and epoch whose ingress was closed; a reopened gate belongs to this stamp (P-005).</summary>
        public ActivationStamp Stamp { get; }

        /// <summary>Command routes closed for this activation (P-047).</summary>
        public int ClosedRoutes { get; }

        /// <summary>Accepted-but-unexecuted commands that finished `Cancelled(RouteRetired)` (P-047).</summary>
        public int CancelledPending { get; }

        public bool ClosedAnything => ClosedRoutes != 0 || CancelledPending != 0;
    }

    /// <summary>
    /// What retracting one installation's active behavior did. P-046/P-048 say the *assembly publication* retracts
    /// the derived rows; whether this binding performed that publication itself or the caller owns it is a fact a
    /// caller must be able to read rather than assume, so both are reported.
    /// </summary>
    public sealed class ContributionRetraction
    {
        public ContributionRetraction(
            PluginInstanceId instance,
            int attributedRows,
            int retractedRows,
            bool publishedByBinding,
            int closedOwners,
            int closedRoutes,
            string detail)
        {
            Instance = instance;
            AttributedRows = attributedRows;
            RetractedRows = retractedRows;
            PublishedByBinding = publishedByBinding;
            ClosedOwners = closedOwners;
            ClosedRoutes = closedRoutes;
            Detail = detail ?? string.Empty;
        }

        public PluginInstanceId Instance { get; }

        /// <summary>Derived binding rows in the published assembly attributed to this installation (P-017).</summary>
        public int AttributedRows { get; }

        /// <summary>Rows actually removed by this call; zero when the caller owns the publication.</summary>
        public int RetractedRows { get; }

        /// <summary>True when this binding published the retraction itself (P-030).</summary>
        public bool PublishedByBinding { get; }

        /// <summary>Owners whose command routes were closed with the retraction (P-047).</summary>
        public int ClosedOwners { get; }

        /// <summary>Command routes closed with the retraction (P-047).</summary>
        public int ClosedRoutes { get; }

        public string Detail { get; }

        /// <summary>
        /// True when the retraction is real: either this binding published it, or the caller still owes the
        /// assembly publication that carries it. A zero-row, zero-route result for an installation that had rows
        /// would be a false retraction, which is why the two counts are reported separately.
        /// </summary>
        public bool Retracted => RetractedRows != 0 || PublishedByBinding;

        public override string ToString() =>
            "retraction(" + Instance.ToString() + ", attributed=" + AttributedRows.ToString(CultureInfo.InvariantCulture)
            + ", retracted=" + RetractedRows.ToString(CultureInfo.InvariantCulture)
            + (PublishedByBinding ? ", published" : ", caller-publishes") + ")";
    }

    /// <summary>
    /// The world-side half of a lifecycle change. One instance per composition host; the host supplies it and the
    /// coordinator calls it in the P-048 order. Nothing here reads a clock.
    /// </summary>
    public interface ILifecycleWorldBinding
    {
        /// <summary>Closes new managed callbacks/commands of one activation and freezes its admission (P-047).</summary>
        LifecycleIngressClosure CloseIngress(PluginInstanceId instance, ActivationStamp stamp);

        /// <summary>
        /// Reopens the ingress of an activation whose teardown aborted before any live write; the diagram's one
        /// path back to `Active` (06 s1).
        /// </summary>
        bool ReopenIngress(PluginInstanceId instance, ActivationStamp stamp);

        /// <summary>
        /// Reaches a step boundary: the current step commits or faults, and no new step is admitted (P-030). A
        /// caller that is already at an idle boundary reports the committed step unchanged.
        /// </summary>
        LifecycleStepSettlement SettleCurrentStep();

        /// <summary>
        /// Fences tracked readers and jobs of one activation and reports the resources those users may still
        /// reach. Those resources are quarantined rather than released (P-047, P-048).
        /// </summary>
        IReadOnlyList<Id128> FenceUsers(PluginInstanceId instance, ActivationStamp stamp);

        /// <summary>
        /// Retracts the active behavior an installation contributes: its declared owners' command routes close and
        /// its derived binding rows retract with the assembly, never its state slots and never another
        /// installation's rows (P-046, P-033). The result says whether this binding published the retraction or the
        /// caller still owes that publication, so a caller never has to assume which.
        /// </summary>
        ContributionRetraction RetractContributions(PluginInstanceId instance, ActivationStamp stamp, OperationId operation);
    }

    /// <summary>
    /// Binding for a host with no live ECS storage: a pure composition lane that owns services, resources and
    /// gates but no world. It reports every step honestly as a no-op with zero effect, so a caller can tell the
    /// difference between "nothing to close" and "the close was not observed".
    /// </summary>
    public sealed class CompositionOnlyLifecycleBinding : ILifecycleWorldBinding
    {
        private LogicalStepId currentStep;

        public int CloseCalls { get; private set; }

        public int ReopenCalls { get; private set; }

        public int SettleCalls { get; private set; }

        public int FenceCalls { get; private set; }

        public int RetractCalls { get; private set; }

        /// <summary>Logical step this lane reports at a boundary; the world owns the real value (P-006).</summary>
        public void SetCommittedStep(LogicalStepId step) => currentStep = step;

        public LifecycleIngressClosure CloseIngress(PluginInstanceId instance, ActivationStamp stamp)
        {
            CloseCalls++;
            return new LifecycleIngressClosure(instance, stamp, 0, 0);
        }

        public bool ReopenIngress(PluginInstanceId instance, ActivationStamp stamp)
        {
            _ = instance;
            _ = stamp;
            ReopenCalls++;
            return true;
        }

        public LifecycleStepSettlement SettleCurrentStep()
        {
            SettleCalls++;
            return LifecycleStepSettlement.At(currentStep);
        }

        public IReadOnlyList<Id128> FenceUsers(PluginInstanceId instance, ActivationStamp stamp)
        {
            _ = instance;
            _ = stamp;
            FenceCalls++;
            return Array.Empty<Id128>();
        }

        public ContributionRetraction RetractContributions(PluginInstanceId instance, ActivationStamp stamp, OperationId operation)
        {
            _ = stamp;
            _ = operation;
            RetractCalls++;
            return new ContributionRetraction(
                instance,
                0,
                0,
                false,
                0,
                0,
                "this lane owns no live storage, so it retracts nothing itself; the caller publishes the assembly");
        }
    }
}

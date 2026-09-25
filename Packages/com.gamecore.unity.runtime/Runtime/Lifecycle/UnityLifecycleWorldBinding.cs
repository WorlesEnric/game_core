// GameCore.Unity.Runtime.Lifecycle — the Unity half of the P-046/P-048 lifecycle seam.
//
// `GameCore.Composition` owns the lifecycle *decisions*: activation attempts, the P-046 transition table, teardown
// order, quarantine and the resource ledger. It is Unity-free, so the steps that only a real world can perform are
// expressed there as `ILifecycleWorldBinding` and implemented here:
//
//   * **close ingress** (P-047): the command routes of the owners the installation declares close, and accepted but
//     unexecuted commands finish `Cancelled(RouteRetired)` at the step cutoff.
//   * **settle the current step** (P-030/P-047): tracked step jobs complete before any assembly changes, so an
//     in-flight step reaches its commit/fault boundary first.
//   * **fence users** (P-047): the resources an activation's unfinished work may still reach are reported so the
//     coordinator quarantines them instead of releasing them.
//   * **retract the closure** (P-046/P-033): the derived binding rows attributed to the installation are counted,
//     and the fact that the assembly publication carries their removal is reported explicitly rather than assumed.
//
// Nothing here reads a clock: "a timeout never authorizes free" is true because no duration is ever consulted.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Execution.Messages;

namespace GameCore.Unity.Runtime.Lifecycle
{
    /// <summary>
    /// One installation's declared command owners: the routes that close when its ingress closes (P-047). A plugin
    /// declares its owners through its state slots (P-034), so the mapping is content, not a hand-maintained list.
    /// </summary>
    public sealed class InstallationIngressOwners
    {
        public InstallationIngressOwners(PluginInstanceId instance, IReadOnlyList<OwnerId>? owners)
        {
            Instance = instance;
            Owners = ContractCollections.Freeze(owners);
        }

        public PluginInstanceId Instance { get; }

        /// <summary>Owners whose routes close with this installation, in declared order.</summary>
        public IReadOnlyList<OwnerId> Owners { get; }

        /// <summary>
        /// Builds the mapping from one installation's manifest: every owner named by a declared state slot closes
        /// with the installation, which is exactly the authority its systems may write (P-034, P-047).
        /// </summary>
        public static InstallationIngressOwners FromManifest(PluginInstanceId instance, InstallEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            IReadOnlyList<StateSlotSpec> slots = entry.Manifest.StateSlots;
            List<OwnerId> owners = new List<OwnerId>();
            for (int i = 0; i < slots.Count; i++)
            {
                OwnerId owner = slots[i].Owner;
                if (owner.IsDefault || ContainsOwner(owners, owner))
                {
                    continue;
                }

                owners.Add(owner);
            }

            return new InstallationIngressOwners(instance, owners);
        }

        private static bool ContainsOwner(List<OwnerId> owners, OwnerId candidate)
        {
            for (int i = 0; i < owners.Count; i++)
            {
                if (owners[i].Equals(candidate))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Unity-world implementation of the lifecycle binding. One instance per composition host; it reads the real
    /// route table, step driver, resource ledger and published assembly, and reports what it actually observed.
    /// </summary>
    public sealed class UnityLifecycleWorldBinding : ILifecycleWorldBinding
    {
        private readonly UnityWorldHost world;
        private readonly AssemblyPublisher publisher;
        private readonly Dictionary<Id128, InstallationIngressOwners> owners = new Dictionary<Id128, InstallationIngressOwners>();
        private readonly Dictionary<Id128, List<Id128>> closedRoutes = new Dictionary<Id128, List<Id128>>();
        private readonly List<Id128> closedOrder = new List<Id128>();

        public UnityLifecycleWorldBinding(UnityWorldHost world, AssemblyPublisher publisher)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        }

        /// <summary>Installations whose ingress this binding has closed; a close is not a timeout-limited state.</summary>
        public int ClosedInstallationCount { get; private set; }

        public int ReopenedInstallationCount { get; private set; }

        public int SettleCount { get; private set; }

        public int FenceCount { get; private set; }

        public int RetractCount { get; private set; }

        /// <summary>Routes retired by a close, across every installation (P-047).</summary>
        public int RetiredRouteCount { get; private set; }

        /// <summary>Accepted but unexecuted commands that finished `Cancelled(RouteRetired)` (P-047).</summary>
        public int CancelledPendingCount { get; private set; }

        /// <summary>Step-job completions reached while settling a boundary (P-030).</summary>
        public int SettledJobCount { get; private set; }

        /// <summary>Declares which owners an installation's ingress closes. Called at mount for the mounted entry.</summary>
        public void DeclareIngressOwners(InstallEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            owners[entry.Instance.Value] = InstallationIngressOwners.FromManifest(entry.Instance, entry);
        }

        /// <summary>Declares the owner mapping explicitly, for a host that resolves owners outside a manifest.</summary>
        public void DeclareIngressOwners(InstallationIngressOwners declaration)
        {
            if (declaration == null)
            {
                throw new ArgumentNullException(nameof(declaration));
            }

            owners[declaration.Instance.Value] = declaration;
        }

        /// <summary>
        /// P-047: new managed callbacks and commands from this activation stop. The route table retires the owners'
        /// routes and the request ledger cancels the commands that were accepted but not yet executed.
        /// </summary>
        public LifecycleIngressClosure CloseIngress(PluginInstanceId instance, ActivationStamp stamp)
        {
            WorldMessagePlane? plane = world.Messages;
            int closed = 0;
            int cancelled = 0;
            if (plane != null && owners.TryGetValue(instance.Value, out InstallationIngressOwners? declared) && declared != null)
            {
                for (int i = 0; i < declared.Owners.Count; i++)
                {
                    OwnerId owner = declared.Owners[i];
                    closed += plane.Routes.RetireOwner(owner);
                    cancelled += plane.Requests.CancelPendingOfOwner(owner, world.CurrentStep);
                }
            }

            ClosedInstallationCount++;
            RetiredRouteCount += closed;
            CancelledPendingCount += cancelled;
            Track(instance, closed);
            return new LifecycleIngressClosure(instance, stamp, closed, cancelled);
        }

        /// <summary>
        /// Reopens the ingress of an activation whose teardown aborted before any live write. Only routes this
        /// binding retired are reopened, so an unrelated retirement is never undone (06 s1, P-047).
        /// </summary>
        public bool ReopenIngress(PluginInstanceId instance, ActivationStamp stamp)
        {
            _ = stamp;
            WorldMessagePlane? plane = world.Messages;
            if (plane == null || !closedRoutes.TryGetValue(instance.Value, out List<Id128>? routes) || routes == null)
            {
                return false;
            }

            int reopened = 0;
            for (int i = 0; i < routes.Count; i++)
            {
                if (plane.Routes.Reopen(new RouteId(routes[i])))
                {
                    reopened++;
                }
            }

            closedRoutes.Remove(instance.Value);
            closedOrder.Remove(instance.Value);
            if (reopened != 0)
            {
                ReopenedInstallationCount++;
            }

            return reopened != 0;
        }

        /// <summary>
        /// P-030: reach the step boundary. Every step job the driver tracked completes here, so an in-flight step
        /// finishes before the assembly that its bindings refer to is replaced. The committed step is read from the
        /// world afterwards, never advanced by this call.
        /// </summary>
        public LifecycleStepSettlement SettleCurrentStep()
        {
            SettleCount++;
            if (world.IsPumping)
            {
                // Settling from inside a pump would ask the step to wait for itself; the honest answer is that the
                // boundary is the caller's own publication point (P-030).
                return LifecycleStepSettlement.At(world.CurrentStep);
            }

            if (world.Driver.IsFaulted)
            {
                return LifecycleStepSettlement.Fault(world.CurrentStep, world.Driver.FaultDetail);
            }

            try
            {
                int settled = world.Driver.SettleRetainedJobs();
                SettledJobCount += settled;
            }
            catch (Exception failure)
            {
                // A completion that throws cannot prove the step stopped; that is a fault, not a settled boundary.
                return LifecycleStepSettlement.Fault(world.CurrentStep, failure.Message);
            }

            return LifecycleStepSettlement.At(world.CurrentStep);
        }

        /// <summary>
        /// P-047/P-048: the resources this activation's unfinished work may still reach. They are read from the
        /// world's own resource ledger, filtered by installation, and only the still-retained ones are reported.
        /// </summary>
        public IReadOnlyList<Id128> FenceUsers(PluginInstanceId instance, ActivationStamp stamp)
        {
            FenceCount++;
            List<Id128> fenced = new List<Id128>();
            WorldResourceLedgerSnapshot snapshot = world.ReadResourceLedger();
            for (int i = 0; i < snapshot.Resources.Count; i++)
            {
                WorldResourceRecord record = snapshot.Resources[i];
                if (!record.Instance.Equals(instance) || !record.IsRetained)
                {
                    continue;
                }

                if (record.State == ResourceRetirementState.Retired)
                {
                    continue;
                }

                fenced.Add(record.ResourceId);
            }

            // The activation stamp is not used as a filter here on purpose: the ledger already scopes records by
            // installation, and the caller's stamp decides *which* leases retire (P-048), not which are still live.
            _ = stamp;
            return fenced;
        }

        /// <summary>
        /// P-046/P-033: the derived rows this installation contributed, and the owners whose routes closed with
        /// them. The rows themselves leave with the assembly publication this operation carries; this binding
        /// reports that it did not perform that publication, so the caller knows it still owes it.
        /// </summary>
        public ContributionRetraction RetractContributions(PluginInstanceId instance, ActivationStamp stamp, OperationId operation)
        {
            _ = stamp;
            _ = operation;
            RetractCount++;
            int attributed = CountAttributedRows(instance);
            int closedRoutes = RetiredRoutesFor(instance).Count;
            return new ContributionRetraction(
                instance,
                attributed,
                0,
                false,
                closedRoutes,
                closedRoutes,
                attributed == 0
                    ? "the installation contributed no derived row to the published assembly"
                    : "the " + attributed + " derived row(s) attributed to this installation retract with the assembly publication this operation carries (P-033)");
        }

        /// <summary>
        /// Derived binding rows of the published assembly whose provider is this installation (P-017). A row is
        /// attributed by provider installation identity, so a shared component's other supporters are untouched.
        /// </summary>
        public int CountAttributedRows(PluginInstanceId instance)
        {
            TargetBindingTable bindings = publisher.Published.Bindings;
            int count = 0;
            for (int i = 0; i < bindings.Rows.Count; i++)
            {
                if (bindings.Rows[i].Provider.Value.Equals(instance.Value))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Routes this binding retired for one installation, in retirement order (P-047).</summary>
        public IReadOnlyList<Id128> RetiredRoutesFor(PluginInstanceId instance) =>
            closedRoutes.TryGetValue(instance.Value, out List<Id128>? routes) && routes != null
                ? routes
                : Array.Empty<Id128>();

        /// <summary>
        /// Records which routes this installation's close retired, read back from the real route table so the record
        /// is a fact about the table rather than a list this binding remembered on its own.
        /// </summary>
        private void Track(PluginInstanceId instance, int closedCount)
        {
            if (closedCount == 0)
            {
                return;
            }

            if (!closedRoutes.TryGetValue(instance.Value, out List<Id128>? routes) || routes == null)
            {
                routes = new List<Id128>();
                closedRoutes.Add(instance.Value, routes);
                closedOrder.Add(instance.Value);
                closedOrder.Sort(CompareIds);
            }

            // The route ids themselves are the table's; re-reading them keeps this record a fact, not a guess.
            WorldMessagePlane? plane = world.Messages;
            if (plane == null || !owners.TryGetValue(instance.Value, out InstallationIngressOwners? declared) || declared == null)
            {
                return;
            }

            routes.Clear();
            for (int i = 0; i < declared.Owners.Count; i++)
            {
                IReadOnlyList<CommandRoute> all = plane.Routes.RoutesInCanonicalOrder();
                for (int r = 0; r < all.Count; r++)
                {
                    if (all[r].Owner.Equals(declared.Owners[i]) && plane.Routes.IsRetired(all[r].Route))
                    {
                        routes.Add(all[r].Route.Value);
                    }
                }
            }
        }

        private static int CompareIds(Id128 left, Id128 right) => left.CompareTo(right);
    }
}

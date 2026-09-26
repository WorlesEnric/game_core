// GameCore.Gameplay.Rewards — the mounted reward installation (GC-024).
//
// Normative sources: docs/game-core/07-reference-compositions.md s5 and 00 P-003 ("no new universal gameplay
// Effect API"), P-032 (last-support policies: `RemoveDerived`, `PreserveDormant` or `TransferTo`, and a version
// change through a registered `Migrate`), P-042/P-043 (bounded declared work), P-045 (an obligation is persisted
// before it is delivered), P-046/P-047 (lifecycle and job fences) and P-048 ("resources still reachable by
// unfinished work are quarantined and retained; elapsed timeout only reports `TeardownBlocked`, never authorizes
// free").
//
// WHAT THIS TYPE IS. 07 s5's `NarrativeCardRewards` as a *mounted installation*: it owns one
// `NarrativeCardRewardBridge` (GC-021's durable outbox core, unchanged), it declares one state slot whose
// last-support policy is `PreserveDormant` (`RewardsOutboxSlot.Spec()`), and it exposes the four lifecycle
// operations that drive the shipped mechanisms — never a new kernel rule and never a universal effect:
//
//   ArmPendingWork / CompletePendingWork
//        Acquire the declared outbox *resource lease* (the world-side lease in `UnityWorldHost.Ledger`, plus — once
//        a composition lane has been presented — a lease in the lane's `ResourceLedger`) and register a
//        `JobFenceRegistry` job that holds them while the work is pending. The acquire/track pair is the one the
//        GC-022 unload-stress scenario drives:
//        `UnityWorldHost.Ledger.Acquire(WorldResourceKind.ManagedLease, ResourceKey, OwnerId, PluginInstanceId,
//        Id128 dependsOn, ulong bytes)` + `Ledger.MarkReady(Id128)` (LifecycleStressScenario.cs:1881-1894) followed
//        by `Controller.JobFence.Track(jobId, instance, stage, systemKey, epoch, step, new List<Id128> { ... },
//        handle)` (LifecycleStressScenario.cs:1097-1106) with `ManagedResourceLease`
//        (Packages/com.gamecore.composition/Runtime/Lifecycle/ManagedResources.cs:98) and the lane's own
//        `JobFenceRegistry.Track` (JobFenceRegistry.cs:142).
//   TryDrain
//        One state-policy pass: `StateMigrationPipeline.Execute` (StateMigrationPipeline.cs:73-118) with
//        `StatePolicyRequest.Migrate(slot, RewardsKeys.OutboxMigration)` while work is pending and
//        `StatePolicyRequest.PreserveDormant(slot)` once it is drained, and the slot value is written from the
//        live pending count first. This is 07 s5's precondition and its `PreserveDormant` declaration, driven
//        explicitly because no publication runs a policy pass on its own: `StateMigrationPipeline` has exactly two
//        callers in the tree, both qualification scenarios (W4GateScenario.cs:579 and
//        Tests/Cards/CardStatePolicyScenario.cs:268).
//   TryUnmount
//        The O-07 unmount of this installation's instance through the lane. With pending work the teardown cannot
//        settle — `TeardownSequencer.Unload` step 3-5 quarantines every resource in
//        `jobs.OutstandingResourcesFor(instance)`, `settleAll` becomes false and the report is
//        `DiagnosticCode.TeardownBlocked` (TeardownSequencer.cs:261-336); `InstallationLifecycleCoordinator.Commit`
//        then leaves the installation `Retiring` with a non-zero `Resources.RetainedCountFor(instance)`
//        (InstallationLifecycleCoordinator.cs:448-459). Drained, the same publication reaches `Disposed`.
//   TransferOutboxTo
//        07 s5's alternative: `StatePolicyRequest.LastSupportTransfer(slot, destinationOwner, destinationTarget)`
//        through `OwnerTransferValidator.Validate` (OwnerTransferValidator.cs:85-237) whose availability lever is
//        `IsAvailableOwner(set, destinationOwner)` over the revision's declared owners. See that method for the one
//        sub-clause the shipped contract cannot express for this declaration.
//
// THERE IS NO UNMOUNT-TIME DORMANT WRITE. `CompositionEditApplier.DispositionsFor` maps a `PreserveDormant`
// last-support loss to `StateDispositionKind.Retain` — a no-op `AssemblyPublisher` ignores
// (CompositionEditApplier.cs:1121-1161, AssemblyPublisher.cs:1324-1331) — so an unmount payload can never produce
// dormant retention. Only the state-policy path produces `RetainDormant`, which is why `TryDrain` exists at all.
//
// Every operation returns a `RewardsLifecycleResult` and never throws for a refusal; each `Detail` names this
// installation's id, the outbox slot's id and the pending-work count (P-052).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Execution.Delivery;
using GameCore.Gameplay.Integration.RewardOutbox;
using GameCore.Planning.StatePolicies;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Delivery;
using GameCore.Unity.Runtime.StateMigration;
using GameCore.Unity.Runtime.Time;

namespace GameCore.Gameplay.Rewards
{
    /// <summary>
    /// The mounted reward installation: it owns one `NarrativeCardRewardBridge`, one declared outbox state slot and
    /// the job-fenced resource lease that 07 s5's "unmounting with pending work rejects until it drains" is
    /// carried by.
    /// </summary>
    public sealed class RewardsInstallation : IDisposable
    {
        /// <summary>Declared bytes of the outbox resource lease; a lease records what it holds (P-048).</summary>
        private const ulong OutboxResourceBytes = 256UL;

        /// <summary>Identity salt of this installation's lane leases and fence jobs (process-local, never persisted).</summary>
        private const ulong LaneLeaseSalt = 0x5245574C4E4C5331UL;

        private const ulong LaneJobSalt = 0x5245574C4E4A4231UL;

        private readonly UnityWorldHost host;
        private readonly NarrativeCardRewardBridge bridge;
        private readonly RewardsOutboxPreconditionMigration migration = new RewardsOutboxPreconditionMigration();

        /// <summary>The composition lane this installation was last presented with (P-046).</summary>
        private CompositionHost? lane;

        private TargetId slotTarget;
        private bool hasSlotTarget;
        private bool armed;
        private ulong armedWorkOrdinal;
        private Id128 worldLeaseId;
        private Id128 laneLeaseId;
        private Id128 laneJobId;
        private bool hasLaneFence;
        private ulong leaseOrdinal;
        private ulong jobOrdinal;
        private ulong operationOrdinal;
        private StatePolicyPlan? lastPolicyPlan;
        private bool disposed;

        private RewardsInstallation(UnityWorldHost host, NarrativeCardRewardBridge bridge)
        {
            this.host = host;
            this.bridge = bridge;
        }

        /// <summary>
        /// Builds the installation over a world: it constructs the one bridge with this installation's own
        /// identity, so the caller cannot assemble the installation inconsistently (the owner id is the
        /// installation's instance identity, and the issuer is this package's declared issuer — P-004, P-017,
        /// P-050).
        /// </summary>
        public static RewardsInstallation Mount(
            UnityWorldHost host,
            WorldTimeDriver time,
            RewardCatalog catalog,
            int capacity,
            int terminalRetention,
            OutboxDurability durability,
            IDeliveryJournal? journal = null,
            IDeliveryStepHook? hook = null)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            var bridge = new NarrativeCardRewardBridge(
                host,
                time,
                RewardsKeys.Installation.Value,
                RewardsKeys.Issuer,
                catalog,
                capacity,
                terminalRetention,
                durability,
                journal,
                hook);
            return new RewardsInstallation(host, bridge);
        }

        /// <summary>The world this installation was mounted in.</summary>
        public UnityWorldHost Host => host;

        /// <summary>
        /// The reward bridge of 07 s5 (GC-021, unchanged): its outbox, its destination port and its own
        /// `Run`/`TryDescribe`/`TryReinstate` surface. The installation exposes it rather than wrapping it, so an
        /// existing caller keeps reading `Bridge.Owner.*` and `Bridge.Destination.*`.
        /// </summary>
        public NarrativeCardRewardBridge Bridge => bridge;

        /// <summary>07 s5's `NarrativeCardRewards` installation identity.</summary>
        public PluginInstanceId Instance => RewardsKeys.Installation;

        /// <summary>The authoritative owner of the declared outbox slot (P-034).</summary>
        public OwnerId OutboxOwnerId => RewardsKeys.OutboxOwner;

        /// <summary>
        /// Pending work: the open obligations of the bridge's outbox — the same number
        /// `DurableOutbox.OpenCount` reports without building the list, and the value written into the outbox slot
        /// each pass reads.
        /// </summary>
        public int PendingWorkCount => bridge.Owner.Outbox.OpenObligations().Count;

        /// <summary>True while this installation holds its outbox resource lease for unfinished work (P-047, P-048).</summary>
        public bool HasOutstandingWorkLease => armed;

        /// <summary>True when the held lease is also fenced by a registered job in a composition lane (P-047).</summary>
        public bool HasLaneFence => hasLaneFence;

        /// <summary>
        /// The one registered migration handler this installation owns. A caller that wants a pass's counters must
        /// register *this* instance in the `StatePolicyCatalog` its pipeline was built from; the key is the same
        /// either way, so the pass resolves it (P-032).
        /// </summary>
        public RewardsOutboxPreconditionMigration OutboxMigration => migration;

        /// <summary>The plan of the most recent policy pass this installation ran; null before the first one.</summary>
        public StatePolicyPlan? LastPolicyPlan => lastPolicyPlan;

        /// <summary>
        /// One bridge pass, so an installation can drive 07 s5's observe/enqueue/dispatch order itself
        /// (`rewards.dispatch` is a managed post-publication observer, not an execution stage). The returned report
        /// is the bridge's own, unchanged (P-052).
        /// </summary>
        public RewardBridgePassReport RunBridgePass(OperationId causal, int maxEvents, int maxDispatches) =>
            bridge.Run(causal, maxEvents, maxDispatches);

        /// <summary>
        /// Arms this installation's pending work: it acquires the declared outbox resource lease in the world's
        /// ledger and, when a composition lane is known, the same lease plus a registered, job-fenced entry in the
        /// lane — the acquire/complete/track pair the GC-022 unload-stress scenario drives (P-047, P-048). A
        /// second arm while one is outstanding is refused: one outstanding work ordinal per installation.
        /// </summary>
        public RewardsLifecycleResult ArmPendingWork(UnityWorldHost host, ulong workOrdinal)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            ThrowIfDisposed();
            if (armed)
            {
                return Result(
                    RewardsLifecycleOutcome.Unsupported,
                    DiagnosticCode.IdempotencyConflict,
                    "pending work " + armedWorkOrdinal.ToString(CultureInfo.InvariantCulture)
                    + " is already armed; work ordinal " + workOrdinal.ToString(CultureInfo.InvariantCulture)
                    + " was not armed.");
            }

            worldLeaseId = host.Ledger.Acquire(
                WorldResourceKind.ManagedLease,
                RewardsKeys.OutboxResource,
                RewardsKeys.OutboxOwner,
                Instance,
                Id128.Zero,
                OutboxResourceBytes);
            if (worldLeaseId.IsDefault)
            {
                return Result(
                    RewardsLifecycleOutcome.Unsupported,
                    DiagnosticCode.ResourceUnavailable,
                    "the world's resource ledger returned no lease identity for the outbox resource.");
            }

            host.Ledger.MarkReady(worldLeaseId);
            armed = true;
            armedWorkOrdinal = workOrdinal;

            string fenceDetail = "no composition lane has been presented yet; the world-side lease is held until a"
                + " lane is available to fence it (P-048).";
            if (lane != null && !TryArmLaneFence(host, lane, workOrdinal, out fenceDetail))
            {
                armed = false;
                host.Ledger.Retire(worldLeaseId);
                return Result(RewardsLifecycleOutcome.Unsupported, DiagnosticCode.ResourceUnavailable, fenceDetail);
            }

            return Result(
                RewardsLifecycleOutcome.Settled,
                DiagnosticCode.None,
                "armed pending work " + workOrdinal.ToString(CultureInfo.InvariantCulture) + " on world lease "
                + worldLeaseId.ToString() + "; " + fenceDetail);
        }

        /// <summary>
        /// Completes the armed work: the registered job finishes (the one event that stops it fencing its
        /// resources), the quarantine an unmount attempt admitted is released, a removal that waited on it settles,
        /// and the world-side lease retires. Nothing here is driven by elapsed time (P-048).
        /// </summary>
        public RewardsLifecycleResult CompletePendingWork(UnityWorldHost host, ulong workOrdinal)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            ThrowIfDisposed();
            if (!armed)
            {
                return Result(
                    RewardsLifecycleOutcome.Unsupported,
                    DiagnosticCode.MissingDependency,
                    "no pending work is armed; there is nothing for work ordinal "
                    + workOrdinal.ToString(CultureInfo.InvariantCulture) + " to complete.");
            }

            if (armedWorkOrdinal != workOrdinal)
            {
                return Result(
                    RewardsLifecycleOutcome.Unsupported,
                    DiagnosticCode.MissingDependency,
                    "work ordinal " + workOrdinal.ToString(CultureInfo.InvariantCulture)
                    + " is not the armed one (" + armedWorkOrdinal.ToString(CultureInfo.InvariantCulture)
                    + "); the armed work keeps its lease and its fence.");
            }

            bool jobCompleted = true;
            if (hasLaneFence && lane != null)
            {
                jobCompleted = lane.Lifecycle.Jobs.Complete(laneJobId);
                if (!jobCompleted)
                {
                    return Result(
                        RewardsLifecycleOutcome.Unsupported,
                        DiagnosticCode.IdempotencyConflict,
                        "the composition job fence refused to complete job " + laneJobId.ToString()
                        + "; a completion it rejects is stale work, so the lease stays retained (P-047, P-048).");
                }
            }

            CleanupReport release = CleanupReport.Empty;
            bool removalSettled = false;
            string settleDetail = "no composition lane has been presented.";
            string fenceDetail = hasLaneFence
                ? "job=" + laneJobId.ToString() + ", laneLease=" + laneLeaseId.ToString()
                : "job=none, laneLease=none";
            if (lane != null)
            {
                release = lane.Lifecycle.ReleaseQuarantineFor(Instance);
                removalSettled = TrySettleRemoval(lane, out settleDetail);
            }

            bool worldLeaseRetired = host.Ledger.Retire(worldLeaseId);
            armed = false;
            hasLaneFence = false;

            return Result(
                RewardsLifecycleOutcome.Settled,
                DiagnosticCode.None,
                "completed work " + workOrdinal.ToString(CultureInfo.InvariantCulture) + "; " + fenceDetail
                + ", jobCompleted=" + (jobCompleted ? "True" : "False")
                + ", quarantinedReleases=" + release.Retired.Count.ToString(CultureInfo.InvariantCulture)
                + ", stillRetained=" + release.Quarantined.Count.ToString(CultureInfo.InvariantCulture)
                + ", removalSettled=" + (removalSettled ? "True" : "False")
                + ", worldLeaseRetired=" + (worldLeaseRetired ? "True" : "False") + "; " + settleDetail);
        }

        /// <summary>
        /// One state-policy pass over this installation's outbox slot (07 s5's two clauses in one entry point): with
        /// pending work it requests the registered version change, whose migration refuses the copied count
        /// (`RefusedPrecondition`); drained, it requests the declared `PreserveDormant` decision, which the pass
        /// turns into `RetainDormant` (`Settled`). The slot value is written from the live pending count first, so
        /// the pass reads the outbox's own number rather than a remembered one.
        /// </summary>
        public RewardsLifecycleResult TryDrain(StateMigrationPipeline policies, IReadOnlyList<TargetId> targets)
        {
            if (policies == null)
            {
                throw new ArgumentNullException(nameof(policies));
            }

            ThrowIfDisposed();
            if (!TryGetSlotTarget(out TargetId target, out string resolveDetail))
            {
                return Result(RewardsLifecycleOutcome.Unsupported, DiagnosticCode.MissingDependency, resolveDetail);
            }

            int pending = PendingWorkCount;
            if (!WriteOutboxSlot(host, target, pending, out string writeDetail))
            {
                return Result(RewardsLifecycleOutcome.Unsupported, DiagnosticCode.ResourceUnavailable, writeDetail);
            }

            StateSlotKey key = RewardsOutboxSlot.KeyOf(target);
            if (pending != 0)
            {
                StatePolicyRequest migrate = StatePolicyRequest.Migrate(key, RewardsKeys.OutboxMigration);
                return RunPolicyPass(
                    policies,
                    targets,
                    target,
                    key,
                    migrate,
                    "07 s5's precondition: " + pending.ToString(CultureInfo.InvariantCulture)
                    + " pending obligation(s) must drain before the outbox slot may cross to version "
                    + RewardsKeys.OutboxDeclaredSchemaVersion.ToString(CultureInfo.InvariantCulture)
                    + ", so the registered migration refuses the copied count");
            }

            StatePolicyRequest dormant = StatePolicyRequest.PreserveDormant(key);
            return RunPolicyPass(
                policies,
                targets,
                target,
                key,
                dormant,
                "07 s5's declaration: the drained outbox takes its declared PreserveDormant decision, so the"
                + " completed outbox is retained with no active writer");
        }

        /// <summary>
        /// The precondition pass on its own: it always requests the registered version change, so a scenario can
        /// prove that the migration refuses while work is pending and applies once it has drained — on the copied
        /// value, never on live state (P-029, P-032).
        /// </summary>
        public RewardsLifecycleResult TryMigrateOutboxSlot(
            StateMigrationPipeline policies,
            IReadOnlyList<TargetId> targets)
        {
            if (policies == null)
            {
                throw new ArgumentNullException(nameof(policies));
            }

            ThrowIfDisposed();
            if (!TryGetSlotTarget(out TargetId target, out string resolveDetail))
            {
                return Result(RewardsLifecycleOutcome.Unsupported, DiagnosticCode.MissingDependency, resolveDetail);
            }

            int pending = PendingWorkCount;
            if (!WriteOutboxSlot(host, target, pending, out string writeDetail))
            {
                return Result(RewardsLifecycleOutcome.Unsupported, DiagnosticCode.ResourceUnavailable, writeDetail);
            }

            StateSlotKey key = RewardsOutboxSlot.KeyOf(target);
            return RunPolicyPass(
                policies,
                targets,
                target,
                key,
                StatePolicyRequest.Migrate(key, RewardsKeys.OutboxMigration),
                "the registered migration is 07 s5's precondition on the copied pending count ("
                + pending.ToString(CultureInfo.InvariantCulture) + " pending)");
        }

        /// <summary>
        /// The O-07 unmount of this installation's instance through the lane, returning what the publication
        /// actually reported. With pending work the teardown quarantines the fenced lease, `settleAll` is false and
        /// the answer is `RefusedPendingWork` with `DiagnosticCode.TeardownBlocked` and the installation left
        /// `Retiring`; drained, the same publication reaches `Disposed` with nothing retained (P-048).
        /// </summary>
        public RewardsLifecycleResult TryUnmount(UnityWorldHost host, CompositionHost lane)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            if (lane == null)
            {
                throw new ArgumentNullException(nameof(lane));
            }

            ThrowIfDisposed();
            this.lane = lane;

            // A lease armed before any lane was presented is fenced here, before the teardown asks: the same
            // acquire/track pair, at the last moment at which the fence can still be registered (P-047).
            if (armed && !hasLaneFence
                && !TryArmLaneFence(host, lane, armedWorkOrdinal, out string armDetail))
            {
                return Result(RewardsLifecycleOutcome.Unsupported, DiagnosticCode.ResourceUnavailable, armDetail);
            }

            if (lane.Committed.TryGetInstall(Instance, out InstallEntry? existing) && existing != null)
            {
                int retainedNow = lane.Resources.RetainedCountFor(Instance);
                if (existing.State == InstallationState.Disposed && retainedNow == 0)
                {
                    return Result(
                        RewardsLifecycleOutcome.Settled,
                        DiagnosticCode.None,
                        "the installation is already Disposed in this lane with nothing retained"
                        + " (Resources.RetainedCountFor=0).");
                }

                if (existing.State == InstallationState.Retiring)
                {
                    if (retainedNow != 0)
                    {
                        // A previous unmount already published the removal and its P-048 order retained the held
                        // lease, so the installation is still Retiring. Repeating the request reports the same
                        // refusal instead of asking the lane to remove an installation it has already retired
                        // (P-046, P-048).
                        return Result(
                            RewardsLifecycleOutcome.RefusedPendingWork,
                            DiagnosticCode.TeardownBlocked,
                            "the installation is Retiring with "
                            + retainedNow.ToString(CultureInfo.InvariantCulture)
                            + " retained resource lease(s), so its removal cannot settle yet; the held outbox"
                            + " lease is fenced by unfinished work (P-047, P-048).");
                    }

                    if (TrySettleRemoval(lane, out string alreadySettled))
                    {
                        return Result(RewardsLifecycleOutcome.Settled, DiagnosticCode.None, alreadySettled);
                    }
                }
            }

            OperationId operation = NextOperation(host);
            EditAdmission admission = lane.SubmitEdit(
                RewardsMounts.Unmount(Instance), operation, lane.Committed.Revision);
            if (admission.Kind != AdmissionKind.Fresh || admission.Code != DiagnosticCode.None
                || admission.Plan == null)
            {
                DiagnosticCode refused = admission.Code != DiagnosticCode.None
                    ? admission.Code
                    : DiagnosticCode.UnsupportedVersion;
                return Result(
                    RewardsLifecycleOutcome.Unsupported,
                    refused,
                    "the composition lane refused the O-07 unmount (" + admission.Kind.ToString()
                    + ", " + DiagnosticCodeText.Of(refused) + "); the installation was not removed and its state is"
                    + " untouched (P-051).");
            }

            IReadOnlyList<PublishedOperation> published = lane.Drain();
            LifecycleCommitReport? lifecycle = null;
            for (int i = 0; i < published.Count; i++)
            {
                if (published[i].Operation.Equals(operation))
                {
                    lifecycle = published[i].Lifecycle;
                }
            }

            if (lifecycle == null)
            {
                return Result(
                    RewardsLifecycleOutcome.Unsupported,
                    DiagnosticCode.StalePlan,
                    "the publication boundary produced no lifecycle report for operation "
                    + operation.ToString() + "; nothing about this removal can be reported (P-050).");
            }

            bool blocked = lifecycle.Code == DiagnosticCode.TeardownBlocked
                || lifecycle.Blocked
                || lifecycle.BlockedByJobFence
                || TeardownBlockedFor(lifecycle);
            int retained = lane.Resources.RetainedCountFor(Instance);
            if (blocked || retained != 0)
            {
                return Result(
                    RewardsLifecycleOutcome.RefusedPendingWork,
                    DiagnosticCode.TeardownBlocked,
                    "the O-07 unmount of " + Instance.ToString() + " did not settle: teardownCode="
                    + DiagnosticCodeText.Of(lifecycle.Code) + ", retained="
                    + retained.ToString(CultureInfo.InvariantCulture) + ", quarantined="
                    + lane.Lifecycle.Quarantine.EntriesFor(Instance).Count.ToString(CultureInfo.InvariantCulture)
                    + ", blockedByJobFence=" + (lifecycle.BlockedByJobFence ? "True" : "False")
                    + ", fencedResources="
                    + lane.Lifecycle.Jobs.OutstandingResourcesFor(Instance).Count.ToString(CultureInfo.InvariantCulture)
                    + "; the installation stays Retiring, so a re-run repeats this refusal until the work drains"
                    + " (P-048).");
            }

            if (!lane.Committed.TryGetInstall(Instance, out InstallEntry? after) || after == null
                || after.State != InstallationState.Disposed)
            {
                return Result(
                    RewardsLifecycleOutcome.Unsupported,
                    DiagnosticCode.ResourceUnavailable,
                    "the O-07 unmount published, but the installation did not reach Disposed (state="
                    + (after == null ? "absent" : after.State.ToString()) + ", retained="
                    + retained.ToString(CultureInfo.InvariantCulture) + "); the removal is reported as it is.");
            }

            return Result(
                RewardsLifecycleOutcome.Settled,
                DiagnosticCode.None,
                "the O-07 unmount settled: the installation reached Disposed with Resources.RetainedCountFor=0 and"
                + " the completed outbox is still owned by " + RewardsKeys.OutboxOwner.ToString()
                + " (P-003: no committed reward was undone).");
        }

        /// <summary>
        /// 07 s5's alternative: hand the durable outbox to a named compatible owner. The shipped transfer contract
        /// is asked first exactly as 07 s5 words it — `StatePolicyRequest.LastSupportTransfer(slot,
        /// destinationOwner, destinationTarget)` validated by `OwnerTransferValidator.Validate` against the same
        /// revision's declared owners — and its verdict is reported verbatim. The durable rows then really move
        /// (`ToDormantRows` into the destination's own outbox), because a `PreserveDormant` last-support declaration
        /// cannot authorize a `TransferTo` request at all: `SlotPolicyValidator` refuses a last-support loss whose
        /// applied policy is not the declared one (SlotPolicyValidator.cs:487-497), and one slot has one last-support
        /// policy per revision (P-032). See the class header of `RewardsKeys.OutboxTransferPolicy` for that clause.
        /// </summary>
        public RewardsLifecycleResult TransferOutboxTo(
            RewardsInstallation destination,
            StateMigrationPipeline policies,
            IReadOnlyList<TargetId> targets)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (policies == null)
            {
                throw new ArgumentNullException(nameof(policies));
            }

            ThrowIfDisposed();
            if (ReferenceEquals(destination, this))
            {
                return Result(
                    RewardsLifecycleOutcome.Unsupported,
                    DiagnosticCode.OwnershipConflict,
                    "a transfer must name another installation: moving an outbox onto its own owner is not a"
                    + " transfer (P-034).");
            }

            if (!TryGetSlotTarget(out TargetId target, out string resolveDetail))
            {
                return Result(RewardsLifecycleOutcome.Unsupported, DiagnosticCode.MissingDependency, resolveDetail);
            }

            StateSlotKey key = RewardsOutboxSlot.KeyOf(target);
            OwnerId destinationOwner = destination.OutboxOwnerId;
            destination.TryGetSlotTarget(out TargetId destinationTarget, out string destinationDetail);
            StatePolicyRequest transfer =
                StatePolicyRequest.LastSupportTransfer(key, destinationOwner, destinationTarget);

            string validation = DescribeTransferValidation(key, destinationOwner, destinationTarget, transfer);
            StatePolicyPlan plan = policies.Execute(
                WithSlotTarget(targets, target),
                new List<StatePolicyRequest> { transfer });
            lastPolicyPlan = plan;

            IReadOnlyList<OutboxRecordValue> rows = ToDormantRows();
            if (rows.Count == 0)
            {
                return Result(
                    RewardsLifecycleOutcome.Unsupported,
                    DiagnosticCode.ResourceUnavailable,
                    "there is no durable outbox row to transfer; " + validation);
            }

            if (!destination.TryAdoptDormantRows(rows, out string adoptDetail))
            {
                return Result(
                    RewardsLifecycleOutcome.Unsupported,
                    DiagnosticCode.ResourceUnavailable,
                    "the destination refused the outbox rows: " + adoptDetail + "; " + validation);
            }

            int destinationOpen = destination.PendingWorkCount;
            bool retired = RewardsSlotStorage.Write(
                host,
                target,
                RewardsKeys.OutboxDeclaredSchemaVersion,
                0,
                false,
                out string retireDetail);
            if (!retired)
            {
                return Result(
                    RewardsLifecycleOutcome.Unsupported,
                    DiagnosticCode.ResourceUnavailable,
                    "the rows moved, but the source slot could not be written as drained: " + retireDetail);
            }

            return Result(
                RewardsLifecycleOutcome.Transferred,
                plan.Code,
                "the durable outbox moved: rows=" + rows.Count.ToString(CultureInfo.InvariantCulture)
                + ", destinationOpenObligations=" + destinationOpen.ToString(CultureInfo.InvariantCulture)
                + ", destination=" + destination.Instance.ToString() + "; " + validation
                + "; slotPlan=" + (plan.Succeeded ? "applied" : DiagnosticCodeText.Of(plan.Code))
                + " (" + plan.Detail + "); sourceSlot=" + retireDetail
                + "; destinationSlot=" + destinationDetail);
        }

        /// <summary>
        /// The durable rows a capture of this installation's outbox records — the projection "preserved dormant"
        /// and "transferred to a compatible owner" both persist (P-045, P-053).
        /// </summary>
        public IReadOnlyList<OutboxRecordValue> ToDormantRows() => bridge.Owner.ToRecords();

        /// <summary>
        /// Reinstates the rows a kept or transferred outbox carries, so the obligations outlive the world that
        /// committed them (P-045, P-049, P-053). The bridge's own reinstate path is used, so its counter and its
        /// outbox see the same adoption.
        /// </summary>
        public bool TryAdoptDormantRows(IReadOnlyList<OutboxRecordValue> rows, out string detail) =>
            bridge.TryReinstate(rows, out detail);

        /// <summary>
        /// Reads the live outbox slot of one target. `pendingCount` is the stored pending-work value, `dormant` is
        /// true when the row is retained with no active writer (the `PreserveDormant` shape of P-032), and a target
        /// with no such row returns false with the reason rather than a zero that was never stored.
        /// </summary>
        public bool TryReadOutboxSlot(
            UnityWorldHost host,
            TargetId slotTarget,
            out int pendingCount,
            out bool dormant,
            out string detail)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            ThrowIfDisposed();
            return RewardsSlotStorage.TryRead(
                host, slotTarget, out _, out pendingCount, out dormant, out detail);
        }

        /// <summary>
        /// Writes the live pending-work count into the outbox row the migration precondition reads, preserving the
        /// version the row already records (rewriting a migrated row at the seeded version would claim a version the
        /// value never crossed, P-032). The target is remembered, so `TryDrain` needs no target argument.
        /// </summary>
        public bool WriteOutboxSlot(
            UnityWorldHost host,
            TargetId slotTarget,
            int pendingCount,
            out string detail)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            ThrowIfDisposed();
            uint version = RewardsKeys.OutboxSeededSchemaVersion;
            if (RewardsSlotStorage.TryRead(host, slotTarget, out uint storedVersion, out _, out _, out _))
            {
                version = storedVersion;
            }

            return WriteRow(host, slotTarget, version, pendingCount, true, out detail);
        }

        /// <summary>
        /// Seeds the outbox row at the version the registered migration reads (`OutboxSeededSchemaVersion`), so a
        /// state-policy pass over it requests a `Migrate` — the seeded state 07 s5's precondition is defined
        /// against (P-032).
        /// </summary>
        public bool SeedOutboxSlot(
            UnityWorldHost host,
            TargetId slotTarget,
            int pendingCount,
            out string detail)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            ThrowIfDisposed();
            return WriteRow(
                host, slotTarget, RewardsKeys.OutboxSeededSchemaVersion, pendingCount, true, out detail);
        }

        /// <summary>The target this installation's outbox row lives on, resolved from storage when not yet written.</summary>
        public bool TryGetSlotTarget(out TargetId target, out string detail)
        {
            if (hasSlotTarget)
            {
                target = slotTarget;
                detail = "the recorded outbox target is " + slotTarget.ToString() + ".";
                return true;
            }

            if (RewardsSlotStorage.TryFindSlotTarget(host, out target, out detail))
            {
                slotTarget = target;
                hasSlotTarget = true;
                return true;
            }

            return false;
        }

        public override string ToString() =>
            "rewardsInstallation(" + Instance.ToString() + ", pending="
            + PendingWorkCount.ToString(CultureInfo.InvariantCulture) + ", armed=" + (armed ? "1" : "0") + ")";

        /// <summary>
        /// Releases this installation. A pending lease is deliberately NOT retired here: an unfinished job must keep
        /// its resource retained, and a dispose that freed it would be exactly the timeout-driven release P-048
        /// forbids — the world's own ledger and the lane's quarantine keep reporting it until the work completes.
        /// The bridge is disposed (idempotently), so a caller that also disposes it directly is unaffected.
        /// </summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (!armed && !worldLeaseId.IsDefault)
            {
                host.Ledger.Retire(worldLeaseId);
            }

            bridge.Dispose();
        }

        /// <summary>
        /// Registers the held outbox lease in a composition lane as a tracked job: one lease in the lane's own
        /// `ResourceLedger`, one `TrackedJob` naming that lease AND the world-side lease, so
        /// `JobFenceRegistry.OutstandingResourcesFor(instance)` reports both and the teardown defers their release
        /// (P-047, P-048).
        /// </summary>
        private bool TryArmLaneFence(
            UnityWorldHost host,
            CompositionHost lane,
            ulong workOrdinal,
            out string detail)
        {
            detail = string.Empty;
            if (hasLaneFence)
            {
                detail = "job " + laneJobId.ToString() + " already fences lease " + laneLeaseId.ToString() + ".";
                return true;
            }

            if (!lane.Committed.TryGetInstall(Instance, out InstallEntry? entry) || entry == null)
            {
                detail = "installation " + Instance.ToString()
                    + " is not mounted in composition lane " + lane.World.ToString()
                    + ", so its pending work cannot be fenced there (P-046, P-047).";
                return false;
            }

            var token = new AsyncWorkToken(
                NextOperation(host),
                Instance,
                entry.Record.Generation,
                entry.Record.ActivationEpoch,
                (uint)workOrdinal);
            leaseOrdinal++;
            laneLeaseId = new Id128(LaneLeaseSalt, Instance.Value.Low + leaseOrdinal);
            var lease = new ManagedResourceLease(
                RewardsKeys.OutboxResource,
                laneLeaseId,
                token,
                RewardsKeys.OutboxDisposer,
                new ManagedResourceGate(),
                null);
            // `GameCore.Composition.ResourceLedger.Acquire` takes the lease, its kind, its owner, the installation
            // it belongs to, its acquisition ordinal and one resource it depends on (ManagedResources.cs:370-376).
            // It takes no byte count: the lane's ledger records the lease, while the world's own ledger is what
            // records bytes (WorldResourceLedger.Acquire, WorldResourceLedger.cs:118-124), which is why the
            // world-side lease above passes OutboxResourceBytes.
            if (!lane.Resources.Acquire(
                    lease,
                    WorldResourceKind.ManagedLease,
                    RewardsKeys.OutboxOwner,
                    Instance,
                    (uint)leaseOrdinal,
                    Id128.Zero))
            {
                detail = "the composition resource ledger already tracks lease " + laneLeaseId.ToString()
                    + "; one lease identity is one acquisition (P-048).";
                return false;
            }

            lane.Resources.MarkReady(laneLeaseId);
            jobOrdinal++;
            laneJobId = new Id128(LaneJobSalt, Instance.Value.Low + jobOrdinal);
            lane.Lifecycle.Jobs.Track(
                laneJobId,
                Instance,
                RewardsKeys.EnqueueStage,
                RewardsKeys.EnqueueSystem,
                host.CurrentEpoch,
                host.CurrentStep,
                new List<Id128> { laneLeaseId, worldLeaseId });
            hasLaneFence = true;
            detail = "job " + laneJobId.ToString() + " fences lease " + laneLeaseId.ToString() + " and world lease "
                + worldLeaseId.ToString() + " in lane " + lane.World.ToString() + " (P-047).";
            return true;
        }

        /// <summary>
        /// One policy pass, with the slot target added to the caller's target list when it is missing (the pass
        /// copies live slots through `LiveTargetSeeder.ReadLiveSlots`, which silently skips a target it cannot
        /// resolve), and the plan classified into the outcome the caller asserts.
        /// </summary>
        private RewardsLifecycleResult RunPolicyPass(
            StateMigrationPipeline policies,
            IReadOnlyList<TargetId> targets,
            TargetId slotTarget,
            StateSlotKey key,
            StatePolicyRequest request,
            string clause)
        {
            StatePolicyPlan plan = policies.Execute(
                WithSlotTarget(targets, slotTarget),
                new List<StatePolicyRequest> { request });
            lastPolicyPlan = plan;

            if (!plan.Succeeded)
            {
                return Result(
                    RewardsLifecycleOutcome.RefusedPrecondition,
                    plan.Code,
                    clause + "; the pass was refused (" + DiagnosticCodeText.Of(plan.Code) + "): " + plan.Detail
                    + "; decisions=" + plan.Decisions.Count.ToString(CultureInfo.InvariantCulture)
                    + ", refused=" + plan.RefusedCount.ToString(CultureInfo.InvariantCulture)
                    + ", migrationInvocations=" + migration.Invocations.ToString(CultureInfo.InvariantCulture)
                    + ", migrationRefusals=" + migration.Refusals.ToString(CultureInfo.InvariantCulture));
            }

            if (!HasDecisionFor(plan, key))
            {
                return Result(
                    RewardsLifecycleOutcome.Unsupported,
                    DiagnosticCode.MissingDependency,
                    clause + "; the pass decided nothing for " + key.ToString()
                    + ", so the revision this pipeline runs declares no such slot or the target list does not"
                    + " reach it (P-032): " + plan.ToString());
            }

            if (request.Intent == StatePolicyIntent.Migrate)
            {
                return plan.MigratedCount != 0
                    ? Result(
                        RewardsLifecycleOutcome.Settled,
                        DiagnosticCode.None,
                        clause + "; migrated=" + plan.MigratedCount.ToString(CultureInfo.InvariantCulture)
                        + ", migrationInvocations=" + migration.Invocations.ToString(CultureInfo.InvariantCulture)
                        + "; " + plan.ToString())
                    : Result(
                        RewardsLifecycleOutcome.Unsupported,
                        DiagnosticCode.MigrationRequired,
                        clause + "; the pass decided no version change for " + key.ToString()
                        + " (" + plan.ToString() + ").");
            }

            return plan.RetainedDormantCount != 0
                ? Result(
                    RewardsLifecycleOutcome.Settled,
                    DiagnosticCode.None,
                    clause + "; retainedDormant="
                    + plan.RetainedDormantCount.ToString(CultureInfo.InvariantCulture)
                    + ", preserved=" + plan.PreservedCount.ToString(CultureInfo.InvariantCulture)
                    + "; " + plan.ToString())
                : Result(
                    RewardsLifecycleOutcome.Unsupported,
                    DiagnosticCode.MissingDependency,
                    clause + "; the pass decided no dormant retention for " + key.ToString()
                    + " (" + plan.ToString() + ").");
        }

        /// <summary>
        /// The transfer request's own verdict, reported verbatim: the declaration this package ships is read with
        /// `SlotStatePolicySet.TryBuildFromManifests`, the destination owner is checked against the same revision's
        /// declared owners, and the answer is the validator's code and detail — never a paraphrase.
        /// </summary>
        private string DescribeTransferValidation(
            StateSlotKey key,
            OwnerId destinationOwner,
            TargetId destinationTarget,
            StatePolicyRequest request)
        {
            if (!SlotStatePolicySet.TryBuildFromManifests(
                    RewardsDeclaration.Manifests(),
                    out SlotStatePolicySet? set,
                    out DiagnosticCode buildCode,
                    out string buildDetail)
                || set == null)
            {
                return "the slot declaration could not be read for a transfer validation ("
                    + DiagnosticCodeText.Of(buildCode) + "): " + buildDetail;
            }

            if (!set.TryFind(key, out SlotStatePolicy? policy, out DiagnosticCode findCode, out string findDetail)
                || policy == null)
            {
                return "the outbox slot is not declared by this revision for a transfer validation ("
                    + DiagnosticCodeText.Of(findCode) + "): " + findDetail;
            }

            var declaredMigrations = new DeclaredSlotMigrationRegistry(set);
            OwnerTransferResult verdict = OwnerTransferValidator.Validate(
                policy,
                key,
                destinationOwner,
                destinationTarget,
                request.DeclaredLastSupportTransfer,
                set,
                declaredMigrations);
            return "ownerTransferValidation(" + DiagnosticCodeText.Of(verdict.Code) + "): " + verdict.ToString();
        }

        /// <summary>True when the publication's own teardown report for this installation was blocked (P-047, P-048).</summary>
        private bool TeardownBlockedFor(LifecycleCommitReport lifecycle)
        {
            for (int i = 0; i < lifecycle.Teardowns.Count; i++)
            {
                TeardownReport teardown = lifecycle.Teardowns[i];
                if (teardown.Instance.Equals(Instance)
                    && (teardown.Code == DiagnosticCode.TeardownBlocked || teardown.BlockedByJobFence))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Settles a removal whose retention the completed work released: `Retiring -> Disposed` is the one lawful
        /// edge, and the lane refuses it as a value rather than forcing it, so a still-retained installation is
        /// reported and unchanged (P-048).
        /// </summary>
        private bool TrySettleRemoval(CompositionHost lane, out string detail)
        {
            detail = string.Empty;
            if (!lane.Committed.TryGetInstall(Instance, out InstallEntry? entry) || entry == null)
            {
                detail = "installation " + Instance.ToString() + " is not recorded in this lane.";
                return false;
            }

            if (entry.State == InstallationState.Disposed)
            {
                detail = "installation " + Instance.ToString() + " is Disposed with retained="
                    + lane.Resources.RetainedCountFor(Instance).ToString(CultureInfo.InvariantCulture) + ".";
                return true;
            }

            if (entry.State != InstallationState.Retiring)
            {
                detail = "installation " + Instance.ToString() + " is " + entry.State.ToString()
                    + ", so there is no removal to settle.";
                return false;
            }

            LifecycleTransition settle = lane.SettleRetiredInstall(Instance);
            detail = "SettleRetiredInstall(" + Instance.ToString() + ")=" + settle.ToString()
                + ", retained=" + lane.Resources.RetainedCountFor(Instance).ToString(CultureInfo.InvariantCulture)
                + ", quarantined="
                + lane.Lifecycle.Quarantine.EntriesFor(Instance).Count.ToString(CultureInfo.InvariantCulture)
                + ".";
            return settle.Allowed;
        }

        /// <summary>
        /// Writes one outbox row and records the target it was written to, which is how `TryDrain` finds the live
        /// row without a target argument.
        /// </summary>
        private bool WriteRow(
            UnityWorldHost host,
            TargetId target,
            uint schemaVersion,
            int pendingCount,
            bool active,
            out string detail)
        {
            bool written = RewardsSlotStorage.Write(host, target, schemaVersion, pendingCount, active, out detail);
            if (written)
            {
                slotTarget = target;
                hasSlotTarget = true;
            }

            return written;
        }

        /// <summary>`targets` plus this installation's own slot target, in that order and without duplicates.</summary>
        private static IReadOnlyList<TargetId> WithSlotTarget(IReadOnlyList<TargetId>? targets, TargetId slotTarget)
        {
            var combined = new List<TargetId>();
            if (targets != null)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    if (!targets[i].Equals(slotTarget))
                    {
                        combined.Add(targets[i]);
                    }
                }
            }

            combined.Add(slotTarget);
            return combined;
        }

        /// <summary>True when the pass produced a decision for one live slot key (P-032).</summary>
        private static bool HasDecisionFor(StatePolicyPlan plan, StateSlotKey key)
        {
            for (int i = 0; i < plan.Decisions.Count; i++)
            {
                if (plan.Decisions[i].Live.Equals(key))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>One operation identity of this installation's issuer (P-050).</summary>
        private OperationId NextOperation(UnityWorldHost host)
        {
            operationOrdinal++;
            return new OperationId(host.World, RewardsKeys.Issuer, operationOrdinal);
        }

        /// <summary>
        /// One result whose detail always names the installation, the outbox slot, the outbox owner and the
        /// pending-work count, so a caller never has to guess which installation or which slot a refusal was about
        /// (the conformance step asserts those substrings).
        /// </summary>
        private RewardsLifecycleResult Result(
            RewardsLifecycleOutcome outcome,
            DiagnosticCode code,
            string evidence)
        {
            return new RewardsLifecycleResult(
                outcome,
                code,
                "instance=" + Instance.ToString()
                + "; slot=" + RewardsKeys.OutboxSlot.ToString()
                + "; owner=" + RewardsKeys.OutboxOwner.ToString()
                + "; pending=" + PendingWorkCount.ToString(CultureInfo.InvariantCulture)
                + "; " + (evidence ?? string.Empty));
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(RewardsInstallation));
            }
        }
    }
}

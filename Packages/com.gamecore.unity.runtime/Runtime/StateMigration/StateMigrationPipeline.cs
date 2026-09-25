// GameCore.Unity.Runtime — the Unity-side state-migration pipeline (GC-015).
//
// One call is one policy pass over copied live state: it reads the live slot values of the affected targets, executes
// the declared policies (`GameCore.Planning.StatePolicies.StatePolicyExecutor`) on bounded scratch, and returns the
// `StatePolicyPlan` a publication applies. The pipeline never writes ECS storage itself: `AssemblyPublisher` owns the
// fence and the apply stage, so a refused execution leaves the old assembly and its state untouched because nothing
// live was ever touched here (P-029, P-030).
//
// What it adds beside the pure executor is the observability the protocol asks for, read from real storage:
//   * the ownership transfers that resolved, with their destination key and authorizing policy (P-025, P-032);
//   * the dormant slots: which keys retain a value with no active writer, and the value they retained (P-032);
//   * the temporary-storage high-water mark of the last pass, so migration bounds are measurable (P-022).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Planning;
using GameCore.Planning.StatePolicies;

namespace GameCore.Unity.Runtime.StateMigration
{
    /// <summary>Executes the declared state policies of one world against copies of its live state (GC-015).</summary>
    public sealed class StateMigrationPipeline
    {
        private readonly UnityWorldHost world;
        private readonly AssemblyPublisher publisher;
        private readonly Integration.LiveTargetSeeder seeder;
        private readonly StatePolicyCatalog catalog;
        private readonly PlanBudget budget;

        private readonly List<OwnerTransferResult> transfers = new List<OwnerTransferResult>();
        private readonly DormantStateRegistry dormant = new DormantStateRegistry();

        public StateMigrationPipeline(
            UnityWorldHost world,
            AssemblyPublisher publisher,
            Integration.LiveTargetSeeder seeder,
            StatePolicyCatalog catalog,
            PlanBudget budget)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            this.seeder = seeder ?? throw new ArgumentNullException(nameof(seeder));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.budget = budget ?? throw new ArgumentNullException(nameof(budget));

            if (!ReferenceEquals(publisher.World, world))
            {
                throw new ArgumentException(
                    "The assembly publisher belongs to another world incarnation than the host (P-004).", nameof(publisher));
            }
        }

        public StatePolicyCatalog Catalog => catalog;

        /// <summary>Dormant slots of this world, with the value each retained (P-032).</summary>
        public DormantStateRegistry Dormant => dormant;

        /// <summary>Ownership transfers that resolved, in execution order (P-025, P-032).</summary>
        public IReadOnlyList<OwnerTransferResult> Transfers => transfers;

        /// <summary>The most recent execution; null before the first call.</summary>
        public StatePolicyPlan? LastPlan { get; private set; }

        public int ExecutionsCount { get; private set; }

        public int RefusedCount { get; private set; }

        /// <summary>
        /// Executes one policy pass for the named targets. The caller publishes the returned plan through the planner
        /// and publisher; a refused plan carries no dispositions, so it can never be published partially (P-029).
        /// </summary>
        public StatePolicyPlan Execute(
            IReadOnlyList<TargetId>? targets,
            IReadOnlyList<StatePolicyRequest>? requests)
            => Execute(targets, requests, null);

        /// <summary>
        /// Executes one policy pass against an explicit policy set instead of the catalog's own. A caller that must
        /// declare a slot differently for this pass (a manifest-supported reset, say) passes the set it validated;
        /// the observations below stay the same (P-032).
        /// </summary>
        public StatePolicyPlan Execute(
            IReadOnlyList<TargetId>? targets,
            IReadOnlyList<StatePolicyRequest>? requests,
            SlotStatePolicySet? policyOverride)
        {
            if (!catalog.Succeeded || catalog.Policies == null)
            {
                RefusedCount++;
                StatePolicyPlan refused = StatePolicyPlan.Refused(
                    catalog.Code == DiagnosticCode.None ? DiagnosticCode.MissingDependency : catalog.Code,
                    "this revision declares no usable state-policy surface: " + catalog.Describe());
                LastPlan = refused;
                return refused;
            }

            SlotStatePolicySet policySet = policyOverride ?? catalog.Policies;
            IReadOnlyList<LiveSlotState> live = seeder.ReadLiveSlots(targets);
            var scratch = new MigrationScratch(budget.ScratchCapacityBytes, budget.ScratchBytesPerSlot);
            StatePolicyPlan plan = StatePolicyExecutor.Execute(
                policySet,
                live,
                requests,
                catalog.Migrations,
                new DeclaredSlotMigrationRegistry(policySet),
                catalog.InitialValues,
                scratch);
            LastPlan = plan;
            if (!plan.Succeeded)
            {
                RefusedCount++;
                return plan;
            }

            RecordObservations(plan, live);
            return plan;
        }

        /// <summary>True when a slot currently has an active writer in this world's published storage (P-032).</summary>
        public bool HasActiveWriter(StateSlotKey slot)
        {
            IReadOnlyList<TargetSlotState> rows = publisher.ReadSlotStates(slot.Target);
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Owner.Equals(slot.Owner) && rows[i].Slot.Equals(slot.Slot))
                {
                    return rows[i].IsActive;
                }
            }

            return false;
        }

        /// <summary>Copies the live slot rows of one target, so a test reads storage rather than a report (P-032).</summary>
        public IReadOnlyList<TargetSlotState> ReadSlots(TargetId target) => publisher.ReadSlotStates(target);

        public override string ToString()
            => "statePolicyExecutions=" + ExecutionsCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ", refused=" + RefusedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ", dormant=" + dormant.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// Records what the applied decisions mean for this world: dormant slots and their retained values, and the
        /// transfers that resolved. Everything recorded is a decision the executor already made (P-025, P-032).
        /// </summary>
        private void RecordObservations(StatePolicyPlan plan, IReadOnlyList<LiveSlotState> live)
        {
            for (int i = 0; i < plan.Decisions.Count; i++)
            {
                StatePolicyDecision decision = plan.Decisions[i];
                switch (decision.Intent)
                {
                    case StatePolicyIntent.TransferTo:
                        transfers.Add(OwnerTransferResult.Resolved(
                            decision.Live,
                            decision.Destination,
                            decision.PolicyKey,
                            decision.DeclaredLastSupportTransfer));
                        break;

                    case StatePolicyIntent.PreserveDormant:
                        for (int l = 0; l < live.Count; l++)
                        {
                            if (live[l].Slot.Equals(decision.Live))
                            {
                                dormant.MarkDormant(decision.Live, live[l].Value, live[l].SchemaVersion);
                                break;
                            }
                        }

                        break;

                    case StatePolicyIntent.Reset:
                    case StatePolicyIntent.Migrate:
                        // A slot written again has an active writer, so its dormant record is dropped (P-032).
                        dormant.Reactivate(decision.Live);
                        break;

                    default:
                        break;
                }
            }
        }
    }
}

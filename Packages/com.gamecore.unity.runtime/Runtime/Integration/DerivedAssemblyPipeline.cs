// GameCore.Unity.Runtime — the W2 integration chain: mount → derive → validate → compile → plan → publish.
//
// Each Wave 2 module owns one step of this chain and each was delivered and proven against its own fixtures. The
// chain itself is the integration surface the W2 exit gate exists for, and every step in it is the real module
// (09 Wave 2: "Independent seam fixtures do not substitute for this integration"):
//
//   CompositionHost (GC-004)  admits and publishes the composition edit that mounts a provider;
//   CompositionDerivationInput (this folder)  freezes the committed composition into GC-006's indexed input;
//   DerivationEngine (GC-006)  computes every target's effective assembly and the contribution delta;
//   DerivedCompositionProposal (this folder)  translates that result into GC-008's immutable proposal;
//   OwnershipSchedulePipeline (this folder)  validates owners/partitions (GC-007) and compiles the stage DAG (GC-009);
//   AssemblyPlanner + AssemblyPublisher (GC-008)  prepare the plan and publish bindings, schedule and snapshot at
//                                               one epoch, which is the composition publication's own epoch.
//
// The chain adds no protocol of its own. It never synthesises a provider, an owner, a partition, a stage or a value;
// a step that cannot be expressed is refused with the diagnostic code the owning module produced, and the refusal is
// reported next to the reports of the steps that did run.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Planning;
using CompositionProposal = GameCore.Planning.CompositionProposal;

namespace GameCore.Unity.Runtime.Integration
{
    /// <summary>How one complete chain run ended.</summary>
    public enum DerivedAssemblyOutcome
    {
        /// <summary>The world published an assembly for the committed composition publication.</summary>
        Published = 0,

        /// <summary>
        /// Derivation produced no target assembly, so there is no effective change to publish (P-006 `NoChange`).
        /// The composition publication still stands; the world's assembly for it is published separately.
        /// </summary>
        NoTargetChange = 1,

        /// <summary>A step of the chain refused; nothing was written and the old assembly stays usable (P-028).</summary>
        Refused = 2,
    }

    /// <summary>
    /// One chain run: the reports of every real module that ran, plus the identities the gate asserts on. Nothing
    /// here is computed by the chain itself.
    /// </summary>
    public sealed class DerivedAssemblyReport
    {
        public OperationId Operation { get; set; }

        public DerivedAssemblyOutcome Outcome { get; set; }

        public DiagnosticCode Code { get; set; }

        public string Detail { get; set; } = string.Empty;

        /// <summary>True when this run published a spawned target rather than a derived binding set.</summary>
        public bool IsSpawn { get; set; }

        public DerivationInputReport? Input { get; set; }

        public DerivationResult? Derivation { get; set; }

        /// <summary>
        /// The invalidation of this run's derivation: which targets were re-derived and which were carried, with
        /// the P-023 counters behind it (GC-013). Null when the run refused before deriving.
        /// </summary>
        public InvalidationClosureResult? Invalidation { get; set; }

        /// <summary>The work counters of the incremental path; the same object as the derivation's counters.</summary>
        public InvalidationCounters? IncrementalCounters { get; set; }

        public DerivationProposalReport? Proposal { get; set; }

        public PlannedPublication? Plan { get; set; }

        public AssemblyPublicationReport? Publication { get; set; }

        /// <summary>Lane publication this assembly belongs to (P-006: one series, no offset).</summary>
        public CompositionRevision LaneRevision { get; set; }

        public AssemblyEpoch LaneEpoch { get; set; }

        public AssemblyEpoch WorldEpochBefore { get; set; }

        public AssemblyEpoch WorldEpochAfter { get; set; }

        /// <summary>True when the lane's published pair equals the world's published pair after this run.</summary>
        public bool CountersJoined { get; set; }

        public bool Succeeded => Outcome == DerivedAssemblyOutcome.Published;

        /// <summary>Binding rows the prepared plan installs; zero for a refusal and for a `NoTargetChange` run.</summary>
        public int InstalledRows => Plan != null ? Plan.Installs.Count : 0;

        /// <summary>Binding rows the prepared plan retracts (P-033).</summary>
        public int RetractedRows => Plan != null ? Plan.Removals.Count : 0;

        /// <summary>State slots the plan migrates on bounded scratch (P-029, P-032).</summary>
        public int MigratedSlots => Plan != null ? Plan.Migrations.Count : 0;

        public string Describe()
        {
            var text = new System.Text.StringBuilder();
            text.Append(IsSpawn ? "spawn" : "publish").Append(' ').Append(Operation.ToString())
                .Append(": ").Append(Outcome.ToString());
            if (Code != DiagnosticCode.None)
            {
                text.Append('(').Append(DiagnosticCodeText.Of(Code)).Append(')');
            }

            text.Append("; lane=").Append(LaneRevision.Value.ToString(CultureInfo.InvariantCulture))
                .Append('/').Append(LaneEpoch.Value.ToString(CultureInfo.InvariantCulture))
                .Append("; worldEpoch=").Append(WorldEpochBefore.Value.ToString(CultureInfo.InvariantCulture))
                .Append("->").Append(WorldEpochAfter.Value.ToString(CultureInfo.InvariantCulture))
                .Append("; joined=").Append(CountersJoined);
            if (Input != null)
            {
                text.Append("; ").Append(Input.Describe());
            }

            if (Derivation != null)
            {
                text.Append("; derivation=").Append(Derivation.Accepted ? "accepted" : Derivation.Rejection.ToString())
                    .Append('(').Append(Derivation.Assemblies.Count.ToString(CultureInfo.InvariantCulture))
                    .Append(" targets, ").Append(Derivation.Delta != null ? Derivation.Delta.ToString() : "<no delta>")
                    .Append(')');
            }

            if (Invalidation != null)
            {
                text.Append("; ").Append(Invalidation.Describe());
            }

            if (Proposal != null)
            {
                text.Append("; ").Append(Proposal.Describe());
            }

            if (Publication != null)
            {
                text.Append("; publication=").Append(Publication.Outcome.ToString())
                    .Append("(writes=").Append(Publication.StructuralWrites.ToString(CultureInfo.InvariantCulture))
                    .Append(", migrations=").Append(Publication.MigratedSlots.ToString(CultureInfo.InvariantCulture))
                    .Append(')');
            }

            if (Detail.Length != 0)
            {
                text.Append("; ").Append(Detail);
            }

            return text.ToString();
        }
    }

    /// <summary>
    /// Drives the whole W2 chain for one owned world: the mounted provider is derived for, the derived assembly is
    /// validated and compiled, and the result is published at the composition publication's own epoch.
    /// </summary>
    public sealed class DerivedAssemblyPipeline
    {
        private readonly UnityWorldHost world;
        private readonly CompositionHost lane;
        private readonly AssemblyPublisher publisher;
        private readonly LiveTargetIndex targets;
        private readonly LiveTargetSeeder seeder;
        private readonly IDerivationValueSource values;
        private readonly IReadOnlyList<DerivationRuleKeys> ruleKeys;
        private readonly IReadOnlyList<ProviderSelectionOverride> overrides;
        private readonly MigrationRegistry migrations;
        private readonly IPlanResourceGate gates;
        private readonly PlanBudget budget;

        private DerivationResult? previousDerivation;
        private readonly DerivedRecipeCache? recipeCache;

        public DerivedAssemblyPipeline(
            UnityWorldHost world,
            CompositionHost lane,
            AssemblyPublisher publisher,
            LiveTargetIndex targets,
            LiveTargetSeeder seeder,
            IDerivationValueSource values,
            IReadOnlyList<DerivationRuleKeys>? ruleKeys,
            IReadOnlyList<ProviderSelectionOverride>? overrides,
            MigrationRegistry migrations,
            IPlanResourceGate gates,
            PlanBudget budget,
            DerivedRecipeCache? recipeCache = null)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.lane = lane ?? throw new ArgumentNullException(nameof(lane));
            this.publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            this.targets = targets ?? throw new ArgumentNullException(nameof(targets));
            this.seeder = seeder ?? throw new ArgumentNullException(nameof(seeder));
            this.values = values ?? throw new ArgumentNullException(nameof(values));
            this.ruleKeys = ruleKeys ?? Array.Empty<DerivationRuleKeys>();
            this.overrides = overrides ?? Array.Empty<ProviderSelectionOverride>();
            this.migrations = migrations ?? throw new ArgumentNullException(nameof(migrations));
            this.gates = gates ?? throw new ArgumentNullException(nameof(gates));
            this.budget = budget ?? throw new ArgumentNullException(nameof(budget));
            this.recipeCache = recipeCache;

            if (!lane.World.Session.Equals(world.World.Session))
            {
                throw new ArgumentException(
                    "The control lane belongs to another world incarnation than the host (P-004).", nameof(lane));
            }

            if (!ReferenceEquals(publisher.World, world))
            {
                throw new ArgumentException(
                    "The assembly publisher belongs to another world incarnation than the host (P-004).",
                    nameof(publisher));
            }
        }

        /// <summary>The most recent accepted derivation, used as the delta base of the next one (P-023).</summary>
        public DerivationResult? PreviousDerivation => previousDerivation;

        /// <summary>
        /// The invalidation of the most recent derivation: the dirty targets, their scopes and the P-023 counters
        /// that show the incremental path visited only the affected part of the world (GC-013).
        /// </summary>
        public InvalidationClosureResult? PreviousInvalidation { get; private set; }

        /// <summary>Chain runs that ended in a publication.</summary>
        public int PublishedCount { get; private set; }

        /// <summary>Chain runs whose derivation produced no target change (P-006 `NoChange`).</summary>
        public int NoTargetChangeCount { get; private set; }

        /// <summary>Chain runs a module refused.</summary>
        public int RefusedCount { get; private set; }

        /// <summary>
        /// Derives the committed composition and publishes the resulting assembly at the lane's own publication
        /// epoch. The composition edit that produced the committed state has already been admitted and published by
        /// the caller; this call is the world's half of that one publication.
        /// </summary>
        public DerivedAssemblyReport PublishDerived(OperationId operation)
        {
            DerivedAssemblyReport report = Derive(operation);
            report.IsSpawn = false;
            if (report.Outcome != DerivedAssemblyOutcome.NoTargetChange)
            {
                return Finish(report);
            }

            // The composition publication carries no target change, so there is nothing to install. The world's
            // assembly for that publication is published by a separate call (a spawn, for example), which is what
            // keeps the two counters on one series.
            NoTargetChangeCount++;
            return Finish(report);
        }

        /// <summary>
        /// Publishes one spawned target as the world's assembly for the lane's committed composition publication
        /// (P-024). The variant is validated against the world's published revision, and the target's rows are
        /// exactly the rules the current assembly publishes for its recipe and scope, so its first visible image is
        /// already its complete effective assembly.
        /// </summary>
        public DerivedAssemblyReport PublishSpawn(
            OperationId operation,
            TargetId target,
            DefinitionRef recipe,
            ScopeId scope)
        {
            DerivedAssemblyReport report = Derive(operation);
            report.IsSpawn = true;

            // A spawn carries the composition publication's own numbers, so the derivation that belongs to that
            // publication must have produced no target change: anything else would leave a derived assembly
            // unpublished, and this refuses instead of dropping it silently (P-006, P-024).
            if (report.Outcome == DerivedAssemblyOutcome.Refused)
            {
                return Finish(report);
            }

            if (report.Outcome == DerivedAssemblyOutcome.Published)
            {
                report.Outcome = DerivedAssemblyOutcome.Refused;
                report.Code = DiagnosticCode.TooLate;
                report.Detail = "the derivation for this composition publication already published an assembly, so"
                    + " the publication number a spawn needs is consumed; a spawn carries the assembly of a"
                    + " publication whose derivation had no target change (P-006, P-024).";
                RefusedCount++;
                return report;
            }

            // The recipe and its revision are validated by the publisher itself against the world's published
            // revision (P-024), so a target that does not exist yet needs no entry in the composition index: its
            // descriptor is resolved by the publisher's own recipe catalog, never guessed here.
            var request = new AssemblySpawnRequest(
                recipe,
                scope,
                target,
                operation,
                publisher.PublishedRevision,
                lane.Committed.Revision,
                lane.Committed.Epoch);

            AssemblyPublicationReport spawned = publisher.Spawn(request);
            report.Publication = spawned;
            report.WorldEpochAfter = world.CurrentEpoch;
            report.CountersJoined = AssemblyPublisher.MatchesPublishedAssembly(
                lane.Committed.Revision,
                lane.Committed.Epoch,
                world.PublishedCompositionRevision,
                world.CurrentEpoch);

            if (!spawned.Published)
            {
                report.Outcome = DerivedAssemblyOutcome.Refused;
                report.Code = spawned.Code;
                report.Detail = "the spawn was refused: " + spawned.Detail;
                RefusedCount++;
                return report;
            }

            report.Outcome = DerivedAssemblyOutcome.Published;
            report.Code = DiagnosticCode.None;
            PublishedCount++;
            return report;
        }

        /// <summary>
        /// Runs the pure half of the chain against the lane's committed composition: freeze the input, derive, and
        /// translate the result into a proposal. No world state is touched, so a caller can inspect a derivation
        /// before deciding whether anything should be published.
        /// </summary>
        public DerivedAssemblyReport Derive(OperationId operation)
        {
            var report = new DerivedAssemblyReport
            {
                Operation = operation,
                LaneRevision = lane.Committed.Revision,
                LaneEpoch = lane.Committed.Epoch,
                WorldEpochBefore = world.CurrentEpoch,
                WorldEpochAfter = world.CurrentEpoch,
            };

            DerivationInputTargets targetView = targets.BuildDerivationTargets();
            if (!targetView.Succeeded)
            {
                report.Outcome = DerivedAssemblyOutcome.Refused;
                report.Code = targetView.Code;
                report.Detail = targetView.Detail;
                RefusedCount++;
                return report;
            }

            DerivationInputReport input = CompositionDerivationInput.Build(
                lane.Committed,
                targetView.Targets,
                ruleKeys,
                overrides);
            report.Input = input;
            if (!input.Succeeded || input.Snapshot == null)
            {
                report.Outcome = DerivedAssemblyOutcome.Refused;
                report.Code = input.Code;
                report.Detail = input.Detail;
                RefusedCount++;
                return report;
            }

            // GC-013: the incremental engine. It computes exactly what a full recomputation would - the
            // differential sweep against the reference evaluator asserts that after every one of 50 x 500
            // operations - while examining only the invalidated part of the world: it falls back to the full engine
            // whenever there is no accepted base, the world mode moved, the catalog changed or the previous run
            // had no provenance to carry, and otherwise re-derives the dirty targets and carries the rest.
            IncrementalDerivationOutcome incremental = IncrementalDerivationEngine.Derive(
                input.Snapshot,
                values,
                DerivationOptions.Default,
                previousDerivation,
                null,
                recipeCache);
            DerivationResult derivation = incremental.Result;
            report.Derivation = derivation;
            report.Invalidation = incremental.Invalidation;
            PreviousInvalidation = incremental.Invalidation;
            report.IncrementalCounters = incremental.Counters;
            if (!derivation.Accepted)
            {
                report.Outcome = DerivedAssemblyOutcome.Refused;
                report.Code = derivation.DiagnosticCode;
                report.Detail = "derivation rejected: " + derivation.Rejection.ToString();
                RefusedCount++;
                return report;
            }

            previousDerivation = derivation;
            if (derivation.Delta != null && derivation.Delta.IsEmpty)
            {
                report.Outcome = DerivedAssemblyOutcome.NoTargetChange;
                report.Code = DiagnosticCode.None;
                report.Detail = "derivation changed no target assembly relative to the published composition";
                return report;
            }

            DerivationProposalReport proposal = DerivedCompositionProposal.Build(
                derivation,
                lane.Committed,
                publisher.PublishedRevision,
                world.CurrentEpoch,
                DerivedCompositionProposal.InputHashOf(derivation),
                input.Snapshot.SnapshotHash,
                operation,
                publisher.Published.Bindings);
            report.Proposal = proposal;
            if (proposal.Outcome == DerivationProposalOutcome.NoAssemblies)
            {
                report.Outcome = DerivedAssemblyOutcome.NoTargetChange;
                report.Code = DiagnosticCode.None;
                report.Detail = proposal.Detail;
                return report;
            }

            if (!proposal.Succeeded || proposal.Proposal == null)
            {
                report.Outcome = DerivedAssemblyOutcome.Refused;
                report.Code = proposal.Code;
                report.Detail = proposal.Detail;
                RefusedCount++;
                return report;
            }

            return Publish(operation, report, proposal.Proposal);
        }

        private DerivedAssemblyReport Publish(
            OperationId operation,
            DerivedAssemblyReport report,
            CompositionProposal proposal)
        {
            // The assembly belongs to the adopted composition publication: exactly the next value of the one series
            // on both counters, using the lane's own numbers rather than an offset (P-006).
            if (!publisher.TryAdoptLanePublication(
                    lane.Committed.Revision,
                    lane.Committed.Epoch,
                    out AssemblyEpoch worldEpoch,
                    out DiagnosticCode adoptCode))
            {
                report.Outcome = DerivedAssemblyOutcome.Refused;
                report.Code = adoptCode;
                report.Detail = "the assembly publisher refused to adopt composition publication "
                    + lane.Committed.Revision.Value.ToString(CultureInfo.InvariantCulture) + "/"
                    + lane.Committed.Epoch.Value.ToString(CultureInfo.InvariantCulture)
                    + " as the world's next assembly at epoch "
                    + worldEpoch.Value.ToString(CultureInfo.InvariantCulture)
                    + "; the pair must be exactly the next publication of the one series (P-006).";
                RefusedCount++;
                return report;
            }

            var liveSlots = seeder.ReadLiveSlots(TargetIds());
            PlannedPublication plan = AssemblyPlanner.Build(
                proposal,
                publisher.Descriptor,
                publisher.PublishedRevision,
                world.CurrentEpoch,
                publisher.Published.Bindings,
                publisher.Published.Rules,
                targets.PlannerTargets(),
                liveSlots,
                migrations,
                new MigrationScratch(budget.ScratchCapacityBytes, budget.ScratchBytesPerSlot),
                new InertAcquisitionSet(gates, operation),
                budget);
            report.Plan = plan;

            if (!plan.IsPrepared)
            {
                report.Outcome = DerivedAssemblyOutcome.Refused;
                report.Code = plan.State.Code;
                report.Detail = "the plan is " + plan.State.Phase.ToString() + ": " + plan.State.Detail;
                RefusedCount++;
                return report;
            }

            AssemblyPublicationReport publication = publisher.Publish(plan);
            report.Publication = publication;
            report.WorldEpochAfter = world.CurrentEpoch;
            report.CountersJoined = AssemblyPublisher.MatchesPublishedAssembly(
                lane.Committed.Revision,
                lane.Committed.Epoch,
                world.PublishedCompositionRevision,
                world.CurrentEpoch);

            if (!publication.Published)
            {
                report.Outcome = publication.Outcome == Outcome.NoChange
                    ? DerivedAssemblyOutcome.NoTargetChange
                    : DerivedAssemblyOutcome.Refused;
                report.Code = publication.Code;
                report.Detail = publication.Detail;
                if (report.Outcome == DerivedAssemblyOutcome.NoTargetChange)
                {
                    NoTargetChangeCount++;
                }
                else
                {
                    RefusedCount++;
                }

                return report;
            }

            report.Outcome = DerivedAssemblyOutcome.Published;
            report.Code = DiagnosticCode.None;
            PublishedCount++;
            return report;
        }

        private DerivedAssemblyReport Finish(DerivedAssemblyReport report)
        {
            report.WorldEpochAfter = world.CurrentEpoch;
            report.CountersJoined = AssemblyPublisher.MatchesPublishedAssembly(
                lane.Committed.Revision,
                lane.Committed.Epoch,
                world.PublishedCompositionRevision,
                world.CurrentEpoch);
            return report;
        }

        private IReadOnlyList<TargetId> TargetIds()
        {
            IReadOnlyList<LiveTarget> live = targets.Targets;
            var ids = new List<TargetId>(live.Count);
            for (int i = 0; i < live.Count; i++)
            {
                ids.Add(live[i].Target);
            }

            return ids;
        }
    }
}

// GameCore.Unity.Runtime — `AssemblyPublisher`: the one place where a validated, prepared plan becomes visible ECS
// storage (GC-008).
//
// Normative sources: 00 P-002 (`AssemblyPublisher` applies a validated plan at a safe boundary and is the only
// participant that writes live storage), P-024 (spawn/despawn: a target first becomes query-visible with its
// complete effective assembly; despawn retracts its contributions, closes its route and destroys only recipe-owned
// entities), P-028/P-029 (recheck the expected revision at application; copy only the state needed for migration
// into bounded scratch and run pure fallible migrations there *before* the first live write; a preparation or
// migration failure releases staged leases in reverse dependency order and leaves the old assembly intact), P-030
// (the fence: close admission, drain old jobs, revalidate, apply, install bindings/schedule/gates and construct the
// complete image, then switch revision/epoch/image/gates together and reopen admission), P-031 (a failure after the
// first live write faults the world: admission stays closed, no epoch or image publishes, no simulation resumes)
// and 04 s5 (staging is unobservable; the commit is a nonthrowing single-reference switch).
//
// The step order below is the protocol's order, and every step that can fail happens *before* the first live write
// except the apply itself:
//
//   1. refuse a world that cannot publish (faulted, stopping, disposed, created) or a step already in progress;
//   2. refuse a plan that is not validated and prepared, and recheck its expected revision/base epoch -> StalePlan;
//   3. fence: complete every tracked fence and handle of the old assembly, and close admission while it swaps;
//   4. migrate on scratch: read the planned live slot values, run the registered pure migrations on the copies; a
//      failure here releases the staged acquisitions and leaves the old assembly and its state untouched (P-029);
//   5. apply: binding rows and state dispositions are written to live storage; this is the postwrite cutoff;
//   6. commit: rebuild the execution order, construct the complete view (bindings + rules + schedule + gates +
//      snapshot token) and switch it once through the host (P-030).
//
// There is ONE publication series here (P-006): the counters a composition operation reports are the counters the
// world publishes. A lane joined to a world is seeded from that world's published assembly at construction
// (`GameCore.Composition.CompositionLaneSeed`), so `CompositionRevision`/`AssemblyEpoch` each move by one per
// publication on both sides, and the publisher *asserts* the equality: a plan adopted at a lane epoch other than
// the published one is refused as `StalePlan` before any write. The invariant this file enforces is one number per
// publication and no two publications sharing a number.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Planning;
using Unity.Entities;

namespace GameCore.Unity.Runtime
{
    /// <summary>One spawn request: a precompiled recipe, the scope it joins and the operation that proposed it (O-12).</summary>
    public readonly struct AssemblySpawnRequest
    {
        public readonly DefinitionRef Recipe;
        public readonly ScopeId Scope;
        public readonly TargetId Target;
        public readonly OperationId Operation;

        /// <summary>
        /// Composition revision the caller's derived variant was computed against (P-024). It must be the revision
        /// the world publishes now: a variant prepared before an intervening edit is rejected `StalePlan` and
        /// recomputed, never published half-assembled.
        /// </summary>
        public readonly CompositionRevision PreparedRevision;

        /// <summary>The composition publication this spawn belongs to; it is exactly the world's next assembly.</summary>
        public readonly CompositionRevision LaneRevision;

        public readonly AssemblyEpoch LaneEpoch;

        public AssemblySpawnRequest(
            DefinitionRef recipe,
            ScopeId scope,
            TargetId target,
            OperationId operation,
            CompositionRevision preparedRevision,
            CompositionRevision laneRevision,
            AssemblyEpoch laneEpoch)
        {
            Recipe = recipe;
            Scope = scope;
            Target = target;
            Operation = operation;
            PreparedRevision = preparedRevision;
            LaneRevision = laneRevision;
            LaneEpoch = laneEpoch;
        }

        public override string ToString() => Target.ToString() + "<-" + Recipe.ToString();
    }

    /// <summary>What the publisher decided for one attempt; the outcome distinctions are the protocol's (05 s4).</summary>
    public sealed class AssemblyPublicationReport
    {
        public AssemblyPublicationReport(
            PublicationRecord record,
            AssemblyEpoch worldEpochBefore,
            AssemblyEpoch worldEpochAfter,
            AssemblyEpoch laneEpoch,
            int structuralWrites,
            int migratedSlots,
            int drainedHandles,
            SnapshotToken? publishedToken,
            string detail)
        {
            Record = record;
            WorldEpochBefore = worldEpochBefore;
            WorldEpochAfter = worldEpochAfter;
            LaneEpoch = laneEpoch;
            StructuralWrites = structuralWrites;
            MigratedSlots = migratedSlots;
            DrainedHandles = drainedHandles;
            PublishedToken = publishedToken;
            Detail = detail ?? string.Empty;
        }

        public PublicationRecord Record { get; }

        public AssemblyEpoch WorldEpochBefore { get; }

        public AssemblyEpoch WorldEpochAfter { get; }

        /// <summary>Lane publication that this attempt joins; equals the world epoch when published.</summary>
        public AssemblyEpoch LaneEpoch { get; }

        /// <summary>Live ECS rows written by the apply step; zero means no live write happened (05 s4 `Rejected`).</summary>
        public int StructuralWrites { get; }

        public int MigratedSlots { get; }

        public int DrainedHandles { get; }

        public SnapshotToken? PublishedToken { get; }

        public string Detail { get; }

        public Outcome Outcome => Record.Outcome;

        public DiagnosticCode Code => Record.Code;

        public bool Published => Record.Published;

        public bool CrossedLiveWriteBoundary => Record.CrossedLiveWriteBoundary;

        public override string ToString() =>
            Record.Outcome.ToString()
            + (Code == DiagnosticCode.None ? string.Empty : "(" + DiagnosticCodeText.Of(Code) + ")")
            + " epoch " + WorldEpochBefore.Value.ToString(CultureInfo.InvariantCulture)
            + "->" + WorldEpochAfter.Value.ToString(CultureInfo.InvariantCulture)
            + " writes=" + StructuralWrites.ToString(CultureInfo.InvariantCulture)
            + (Detail.Length == 0 ? string.Empty : ": " + Detail);
    }

    /// <summary>Injected faults for the fault-boundary tests; a production pipeline never arms these (TEST-016).</summary>
    public sealed class AssemblyFaultInjection
    {
        /// <summary>Throws inside the prewrite migration stage, i.e. before any live write (P-029).</summary>
        public bool FailDuringMigration { get; set; }

        /// <summary>Throws after the apply stage has already written at least one live row (P-031).</summary>
        public bool FailAfterFirstLiveWrite { get; set; }

        /// <summary>Times the prewrite injection fired; a value, so a test can assert it fired exactly once.</summary>
        public int MigrationInjections { get; private set; }

        public int PostWriteInjections { get; private set; }

        /// <summary>Raises the prewrite injection if armed; called by the publisher inside the migration stage.</summary>
        public void MaybeFailDuringMigration()
        {
            if (!FailDuringMigration)
            {
                return;
            }

            MigrationInjections++;
            throw new InvalidOperationException(
                "injected prewrite failure: the old assembly must stay intact and keep running (P-029)");
        }

        /// <summary>Raises the postwrite injection if armed; called after the first live write (P-031).</summary>
        public void MaybeFailAfterFirstLiveWrite()
        {
            if (!FailAfterFirstLiveWrite)
            {
                return;
            }

            PostWriteInjections++;
            throw new InvalidOperationException(
                "injected postwrite failure: the world must fault without publishing or resuming (P-031)");
        }
    }

    /// <summary>
    /// The publisher of one owned world. It owns the target registry, the published assembly reference and the
    /// descriptor that gives a compiled schedule its fence indices; the host owns lifecycle, the logical step and
    /// the one visible switch.
    /// </summary>
    public sealed class AssemblyPublisher
    {
        private readonly UnityWorldHost world;
        private readonly TargetRegistry registry;
        private readonly SpawnRecipeCatalog recipes;
        private readonly MigrationRegistry migrations;
        private readonly OwnershipStageDescriptor descriptor;
        private readonly PublishedAssemblySlot assembly;

        private readonly List<UsedPublication> usedPublications = new List<UsedPublication>();

        private int publicationOrdinal;
        private int appliedBindingRowCount;

        public AssemblyPublisher(
            UnityWorldHost world,
            TargetRegistry registry,
            SpawnRecipeCatalog recipes,
            MigrationRegistry migrations,
            OwnershipStageDescriptor descriptor)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
            this.migrations = migrations ?? throw new ArgumentNullException(nameof(migrations));
            this.descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));

            if (!registry.World.Session.Equals(world.World.Session))
            {
                throw new ArgumentException(
                    "The target registry belongs to another world incarnation than the host (P-004).",
                    nameof(registry));
            }

            if (!descriptor.TryValidate(out DiagnosticCode code, out string detail))
            {
                throw new ArgumentException(
                    "The ownership/stage descriptor is not valid, so no assembly can be published against it: "
                    + detail + " (" + code + ").",
                    nameof(descriptor));
            }

            PublishedRevision = CompositionRevision.First;
            AdoptedLaneRevision = CompositionRevision.Zero;
            AdoptedLaneEpoch = AssemblyEpoch.Zero;
            HasAdoptedPublication = false;

            assembly = new PublishedAssemblySlot(new PublishedWorldView(
                world.CurrentEpoch,
                PublishedRevision,
                new SnapshotToken(world.World, world.CurrentEpoch, world.CurrentStep),
                null,
                TargetBindingTable.Empty,
                null,
                CompiledSchedule.Empty,
                null,
                0));

            // Joining the publisher makes the published view the authority for "which assembly is current".
            world.AttachAssemblySlot(assembly);
        }

        public UnityWorldHost World => world;

        public TargetRegistry Registry => registry;

        public SpawnRecipeCatalog Recipes => recipes;

        public MigrationRegistry Migrations => migrations;

        public OwnershipStageDescriptor Descriptor => descriptor;

        /// <summary>Injected-fault switches; unset in production (TEST-016).</summary>
        public AssemblyFaultInjection Faults { get; } = new AssemblyFaultInjection();

        /// <summary>The one published assembly reference; every reader captures one reference from here (P-030).</summary>
        public PublishedWorldView Published => assembly.Read();

        /// <summary>World-series composition revision this publisher has published (05 s2: it starts at 1).</summary>
        public CompositionRevision PublishedRevision { get; private set; }

        /// <summary>
        /// Published assembly epoch of the last publication, which is also the composition epoch that produced it:
        /// P-006 has one series, so there is no second counter to report (see <see cref="AdoptedLaneEpoch"/> for the
        /// composition publication a caller is about to turn into an assembly).
        /// </summary>
        public AssemblyEpoch PublishedEpoch => Published.Epoch;

        /// <summary>Assembly publications committed, the world's initial assembly included.</summary>
        public int PublicationCount => assembly.SwitchCount;

        /// <summary>Attempts refused before any live write because the plan was stale (P-028).</summary>
        public int StalePlanCount { get; private set; }

        /// <summary>Attempts refused before any live write because migration could not run on scratch (P-029).</summary>
        public int PrewriteFailureCount { get; private set; }

        /// <summary>Attempts that faulted the world after the first live write (P-031).</summary>
        public int PostwriteFaultCount { get; private set; }

        /// <summary>Targets spawned by one publication each; a spawn is never visible half-assembled (P-024).</summary>
        public int SpawnedCount { get; private set; }

        public int DespawnedCount { get; private set; }

        /// <summary>Spawns refused because the recipe is unknown or was prepared against another revision (P-024).</summary>
        public int SpawnRejectionCount { get; private set; }

        /// <summary>Handles naming a despawned or foreign target; every one is rejected as stale (P-005).</summary>
        public int StaleHandleRejectionCount { get; private set; }

        /// <summary>Binding rows currently installed in live storage, for diagnosis.</summary>
        public int AppliedBindingRowCount => appliedBindingRowCount;

        /// <summary>
        /// True when the lane's published revision/epoch pair is exactly the world's published pair. This is the
        /// equality P-006 asks for between the two modules, checked as one value so a half-advanced pair cannot pass.
        /// </summary>
        public static bool MatchesPublishedAssembly(
            CompositionRevision laneRevision,
            AssemblyEpoch laneEpoch,
            CompositionRevision worldRevision,
            AssemblyEpoch worldEpoch) =>
            laneRevision.Equals(worldRevision) && laneEpoch.Equals(worldEpoch);

        /// <summary>
        /// Publishes one planned assembly at a step boundary (P-030). The outcome is `NoChange` (no increment),
        /// `Rejected` (no live write), `Published`/`PublishedWithCleanupErrors`, or `Faulted` (postwrite failure).
        ///
        /// The publication belongs to the composition publication adopted through
        /// <see cref="TryAdoptLanePublication"/>, and the equality P-006 requires is *asserted* here: the adopted
        /// composition epoch must be the world epoch this publication lands on. A mismatch — a stale adoption, a
        /// repeated one, or a lane that was never seeded from the world — is `StalePlan` and no write happens.
        /// </summary>
        public AssemblyPublicationReport Publish(PlannedPublication publication)
        {
            if (publication == null)
            {
                throw new ArgumentNullException(nameof(publication));
            }

            GameCoreThreading.RequireMainThread("AssemblyPublisher.Publish");

            AssemblyEpoch epochBefore = world.CurrentEpoch;
            AssemblyEpoch laneEpoch = AdoptedLaneEpoch;

            // 1. A world that cannot publish refuses before anything else happens; a faulted world never resumes.
            if (world.Lifecycle != WorldLifecycleState.Running && world.Lifecycle != WorldLifecycleState.Paused)
            {
                return Refuse(
                    publication.Plan.Operation,
                    epochBefore,
                    laneEpoch,
                    DiagnosticCode.ApplyFault,
                    "world " + world.DiagnosticName + " is " + world.Lifecycle
                    + " and accepts no publication (P-031).");
            }

            if (world.IsPumping)
            {
                // A publication belongs at an end-of-step or idle boundary, never inside a step (P-030).
                return Refuse(
                    publication.Plan.Operation,
                    epochBefore,
                    laneEpoch,
                    DiagnosticCode.TooLate,
                    "a step is in progress; a publication belongs at an end-of-step or idle boundary (P-030).");
            }

            if (publication.IsRejected)
            {
                if (publication.State.Code == DiagnosticCode.StalePlan) StalePlanCount++;
                return Refuse(
                    publication.Plan.Operation,
                    epochBefore,
                    laneEpoch,
                    publication.State.Code,
                    "the plan is " + publication.State.Phase + ": " + publication.State.Detail);
            }

            if (!publication.IsPrepared)
            {
                return Refuse(
                    publication.Plan.Operation,
                    epochBefore,
                    laneEpoch,
                    DiagnosticCode.StalePlan,
                    "the plan is not prepared; publication requires a validated, prepared plan (P-027).");
            }

            // 2. Recheck the expected revision and base epoch before touching anything (P-028, P-030).
            if (!publication.State.RecheckBase(PublishedRevision, epochBefore, out DiagnosticCode recheckCode))
            {
                StalePlanCount++;
                return Refuse(
                    publication.Plan.Operation,
                    epochBefore,
                    laneEpoch,
                    recheckCode,
                    "the plan was validated against revision "
                    + publication.Plan.BaseRevision.Value.ToString(CultureInfo.InvariantCulture)
                    + "/epoch " + publication.Plan.BaseEpoch.Value.ToString(CultureInfo.InvariantCulture)
                    + " but the world publishes revision "
                    + PublishedRevision.Value.ToString(CultureInfo.InvariantCulture)
                    + "/epoch " + epochBefore.Value.ToString(CultureInfo.InvariantCulture)
                    + "; it is discarded without mutation and regenerated under a new operation id (P-028).");
            }

            // A plan whose assembly is effectively identical is a no-op: nothing increments and no image publishes
            // (P-006). A state migration or retraction alone *is* an effective change, so it still publishes.
            if (!HasEffectiveChange(publication))
            {
                // The adopted composition publication produced no assembly, so it stays available: one number is
                // never consumed by a no-op, and the world's unchanged-assembly (or spawn) publication for it may
                // still adopt the same pair (P-006, P-024).
                if (HasAdoptedPublication)
                {
                    HasAdoptedPublication = false;
                }

                publication.Scratch.ReleaseAll();
                publication.State.TryNoChange("the proposal changed no effective binding");
                return new AssemblyPublicationReport(
                    new PublicationRecord(
                        publication.Plan.Operation,
                        Outcome.NoChange,
                        DiagnosticCode.None,
                        "the assembly is unchanged; no revision or epoch increment (P-006).",
                        null,
                        PublishedRevision,
                        PublishedRevision,
                        epochBefore,
                        epochBefore,
                        null,
                        null),
                    epochBefore,
                    epochBefore,
                    laneEpoch,
                    0,
                    0,
                    0,
                    null,
                    "no assembly change");
            }

            // 3. P-006 has one publication series: the composition publication this assembly belongs to must be
            //    exactly the next value of the published one, on both counters. Anything else is stale and is
            //    refused before a single live write (P-006, P-028).
            if (!HasAdoptedPublication)
            {
                publication.State.TryReject(
                    DiagnosticCode.StalePlan,
                    "no composition publication was adopted for this assembly");
                return Refuse(
                    publication.Plan.Operation,
                    epochBefore,
                    laneEpoch,
                    DiagnosticCode.StalePlan,
                    "no composition publication was adopted; the world publishes epoch "
                    + epochBefore.Value.ToString(CultureInfo.InvariantCulture)
                    + " and cannot advance to an assembly nobody proposed (P-006).");
            }

            if (!NextEpoch(epochBefore).Equals(AdoptedLaneEpoch) ||
                !NextRevision(PublishedRevision).Equals(AdoptedLaneRevision))
            {
                publication.State.TryReject(
                    DiagnosticCode.StalePlan,
                    "the adopted composition publication is not the next assembly");
                return Refuse(
                    publication.Plan.Operation,
                    epochBefore,
                    laneEpoch,
                    DiagnosticCode.StalePlan,
                    "the adopted composition publication is revision "
                    + AdoptedLaneRevision.Value.ToString(CultureInfo.InvariantCulture)
                    + "/epoch " + AdoptedLaneEpoch.Value.ToString(CultureInfo.InvariantCulture)
                    + " but the next assembly of this world is revision "
                    + NextRevision(PublishedRevision).Value.ToString(CultureInfo.InvariantCulture)
                    + "/epoch " + NextEpoch(epochBefore).Value.ToString(CultureInfo.InvariantCulture)
                    + "; the composition series and the published series must be the same one (P-006).");
            }

            // P-006 increments the revision and the epoch at the same publication, so both are taken from the
            // adopted composition publication rather than recomputed: the numbers are the lane's, not an offset.
            AssemblyEpoch nextEpoch = AdoptedLaneEpoch;
            CompositionRevision nextRevision = AdoptedLaneRevision;

            // 4. The fence: admission closes and every tracked handle of the old assembly completes before its
            //    storage is touched (P-030, P-041, P-047).
            int drained = FenceOldAssembly();

            // 5. Migration on scratch. Everything here works on copied values, so failure leaves live state alone.
            if (!TryMigrateOnScratch(
                publication,
                out int migratedSlots,
                out DiagnosticCode migrationCode,
                out string migrationDetail))
            {
                PrewriteFailureCount++;
                AcquisitionCleanup cleanup = publication.Acquisitions.ReleaseAll();
                publication.Scratch.ReleaseAll();
                publication.State.TryReject(migrationCode, migrationDetail);
                return new AssemblyPublicationReport(
                    new PublicationRecord(
                        publication.Plan.Operation,
                        Outcome.Rejected,
                        migrationCode,
                        migrationDetail,
                        null,
                        PublishedRevision,
                        PublishedRevision,
                        epochBefore,
                        epochBefore,
                        cleanup.Failed.Count != 0 ? cleanup.Failed : null,
                        cleanup.Quarantined.Count != 0 ? cleanup.Quarantined : null),
                    epochBefore,
                    epochBefore,
                    laneEpoch,
                    0,
                    0,
                    drained,
                    null,
                    migrationDetail);
            }

            // 6. Apply. From here on a failure is a postwrite fault: no epoch, no image and no resumption (P-031).
            publication.State.TryBeginApplying(out _);
            int writes = 0;
            try
            {
                ApplyStructuralAndState(publication, ref writes);
            }
            catch (Exception exception)
            {
                return FaultAfterLiveWrite(publication, epochBefore, laneEpoch, drained, writes, exception);
            }

            // The stamp of every affected target names the epoch being published, and it is written inside the fence
            // with the rest of the apply stage rather than after the commit (P-030, 04 s5).
            writes += StampTargets(publication.AffectedTargets, nextEpoch, nextRevision);

            // 7. Commit: construct the complete image and switch it once through the host (P-030).
            return Commit(publication, epochBefore, laneEpoch, nextEpoch, nextRevision, drained, migratedSlots, writes);
        }

        /// <summary>Publishes a composition revision whose validated derivation changed no target bindings.</summary>
        public AssemblyPublicationReport PublishUnchangedAssembly(OperationId operation, CompositionRevision laneRevision, AssemblyEpoch laneEpoch)
        {
            GameCoreThreading.RequireMainThread("AssemblyPublisher.PublishUnchangedAssembly");
            AssemblyEpoch epochBefore = world.CurrentEpoch;
            if ((world.Lifecycle != WorldLifecycleState.Running && world.Lifecycle != WorldLifecycleState.Paused)
                || world.IsPumping)
            {
                return Refuse(operation, epochBefore, laneEpoch, DiagnosticCode.ApplyFault,
                    "the world cannot publish at this boundary");
            }

            if (!TryAdoptLanePublication(laneRevision, laneEpoch, out _, out DiagnosticCode code))
            {
                return Refuse(operation, epochBefore, laneEpoch, code, "the composition publication is not next");
            }

            PublishedWorldView previous = Published;
            AssemblyPublicationReport report = CommitView(
                operation, null, previous, previous.Bindings, previous.Rules, Array.Empty<TargetId>(),
                epochBefore, laneEpoch, laneRevision, publicationOrdinal + 1, laneEpoch, 0, 0, 0,
                "published an unchanged target assembly for the next composition revision");
            if (report.Published)
            {
                PublishedRevision = laneRevision;
                publicationOrdinal++;
                MarkPublicationUsed(laneRevision, laneEpoch);
            }

            return report;
        }

        /// <summary>
        /// Records the composition publication the next assembly belongs to. P-006 has one series, so the pair must
        /// be exactly the next value of the published one: the world publishes the numbers the composition lane
        /// published, with no offset in either direction. A pair that is not the next publication — a repeated or
        /// stale one, or a lane that was never seeded from the world — is refused (P-006, P-050).
        /// </summary>
        public bool TryAdoptLanePublication(
            CompositionRevision laneRevision,
            AssemblyEpoch laneEpoch,
            out AssemblyEpoch worldEpoch,
            out DiagnosticCode code)
        {
            worldEpoch = world.CurrentEpoch;

            if (!laneRevision.Value.Equals(laneEpoch.Value))
            {
                // P-006 increments revision and epoch together; a pair that disagrees is two series in one value.
                code = DiagnosticCode.UnsupportedVersion;
                return false;
            }

            if (!NextRevision(PublishedRevision).Equals(laneRevision) ||
                !NextEpoch(world.CurrentEpoch).Equals(laneEpoch))
            {
                // The composition publication is not the next assembly of the one series this world publishes.
                code = DiagnosticCode.StalePlan;
                return false;
            }

            if (!IsUnusedPublication(laneRevision, laneEpoch, adopting: true))
            {
                // One number per publication and never shared: a pair that was already published, or that is
                // already adopted and pending, cannot be adopted again (P-006, P-050).
                code = DiagnosticCode.StalePlan;
                return false;
            }

            AdoptedLaneRevision = laneRevision;
            AdoptedLaneEpoch = laneEpoch;
            HasAdoptedPublication = true;
            worldEpoch = laneEpoch;
            code = DiagnosticCode.None;
            return true;
        }

        /// <summary>
        /// The composition publication this publisher will turn into the next assembly; null-valued (the default)
        /// until <see cref="TryAdoptLanePublication"/> accepted one. <see cref="Publish"/> refuses without it.
        /// </summary>
        public CompositionRevision AdoptedLaneRevision { get; private set; }

        public AssemblyEpoch AdoptedLaneEpoch { get; private set; }

        /// <summary>True once an adopted composition publication is waiting for its assembly.</summary>
        public bool HasAdoptedPublication { get; private set; }

        /// <summary>
        /// Spawns one target from a precompiled recipe and publishes it with its complete effective assembly in one
        /// visible epoch (P-024). The target is created pending, its identity and slot are allocated, and its rows
        /// are exactly the ones the current published rules match, so nothing is wired in a later frame.
        /// </summary>
        public AssemblyPublicationReport Spawn(AssemblySpawnRequest request)
        {
            GameCoreThreading.RequireMainThread("AssemblyPublisher.Spawn");

            AssemblyEpoch epochBefore = world.CurrentEpoch;
            CompositionRevision laneRevision = request.LaneRevision;
            AssemblyEpoch laneEpoch = request.LaneEpoch;

            if (world.Lifecycle != WorldLifecycleState.Running && world.Lifecycle != WorldLifecycleState.Paused)
            {
                SpawnRejectionCount++;
                return Refuse(request.Operation, epochBefore, laneEpoch, DiagnosticCode.ApplyFault,
                    "world " + world.DiagnosticName + " is " + world.Lifecycle + " and accepts no spawn (P-031).");
            }

            // P-006 has one publication series: the composition publication the spawn belongs to must name one
            // publication and be exactly the next value of the published one, on both counters.
            if (!laneRevision.Value.Equals(laneEpoch.Value) ||
                !NextEpoch(epochBefore).Equals(laneEpoch) ||
                !NextRevision(PublishedRevision).Equals(laneRevision))
            {
                SpawnRejectionCount++;
                return Refuse(request.Operation, epochBefore, laneEpoch, DiagnosticCode.StalePlan,
                    "a spawn publishes the next assembly of the one publication series; the request names revision "
                    + laneRevision.Value.ToString(CultureInfo.InvariantCulture) + "/epoch "
                    + laneEpoch.Value.ToString(CultureInfo.InvariantCulture) + " but the next assembly is revision "
                    + NextRevision(PublishedRevision).Value.ToString(CultureInfo.InvariantCulture) + "/epoch "
                    + NextEpoch(epochBefore).Value.ToString(CultureInfo.InvariantCulture) + " (P-006).");
            }

            if (!IsUnusedPublication(laneRevision, laneEpoch, adopting: false))
            {
                SpawnRejectionCount++;
                return Refuse(request.Operation, epochBefore, laneEpoch, DiagnosticCode.StalePlan,
                    "composition publication " + laneEpoch.Value.ToString(CultureInfo.InvariantCulture)
                    + " was already consumed; one number is never shared by two publications (P-006).");
            }

            // The variant was computed against a composition revision; P-024 validates it against the revision the
            // world publishes now, so a variant prepared before an intervening edit is refused rather than
            // activated stale.
            if (!recipes.TryValidateForPublication(
                    request.Recipe,
                    PublishedRevision,
                    request.PreparedRevision,
                    out SpawnRecipe? recipe,
                    out DiagnosticCode recipeCode)
                || recipe == null)
            {
                SpawnRejectionCount++;
                return Refuse(request.Operation, epochBefore, laneEpoch, recipeCode,
                    "recipe " + request.Recipe.ToString() + " cannot be spawned against published revision "
                    + PublishedRevision.Value.ToString(CultureInfo.InvariantCulture) + " (prepared against "
                    + request.PreparedRevision.Value.ToString(CultureInfo.InvariantCulture) + ") (P-024).");
            }

            int drained = FenceOldAssembly();
            EntityManager entityManager = world.EntityWorld.EntityManager;
            Entity created = entityManager.CreateEntity();
            if (!registry.TryAllocate(request.Target, created, out TargetHandle handle, out DiagnosticCode allocateCode))
            {
                entityManager.DestroyEntity(created);
                SpawnRejectionCount++;
                return Refuse(request.Operation, epochBefore, laneEpoch, allocateCode,
                    "target " + request.Target.ToString() + " was refused by the registry (P-004, P-005).");
            }

            // P-006 increments the revision and the epoch at the same publication, so both are the request's own
            // numbers: the composition series and the published series are the same one.
            AssemblyEpoch nextEpoch = laneEpoch;
            CompositionRevision nextRevision = laneRevision;

            PublishedWorldView view = assembly.Read();
            Entity entity = registry.EntityOf(request.Target);
            IReadOnlyList<DerivedBindingRule> matching = view.RulesFor(request.Recipe, request.Scope);
            try
            {
                entityManager.AddComponentData(entity, new TargetIdentity
                {
                    Target = request.Target,
                    Generation = handle.Generation,
                });

                // Stamped as published inside the fence: the target's identity, its rows and its epoch are all part
                // of this apply stage, and the pointer switch is what makes them visible (P-024, P-030).
                entityManager.AddComponentData(entity, new AssemblyStamp
                {
                    AssemblyEpoch = nextEpoch.Value,
                    CompositionRevision = nextRevision.Value,
                    Slot = handle.Slot,
                    Published = 1,
                });

                recipe.Applier.ApplyBaseLayout(entityManager, entity, recipe);

                // One row per currently derived rule that matches this recipe and scope, so the target's first
                // visible image is already its complete effective assembly (P-013, P-024) — including each row's
                // full support set, so a spawned target inherits a composed `Additive` binding with all its
                // supporters rather than only its top-ranked candidate (P-017, P-019).
                entityManager.AddBuffer<CapabilityBinding>(entity);
                entityManager.AddBuffer<CapabilitySupportRow>(entity);
                DynamicBuffer<CapabilityBinding> bindings = entityManager.GetBuffer<CapabilityBinding>(entity);
                DynamicBuffer<CapabilitySupportRow> spawnedSupports = entityManager.GetBuffer<CapabilitySupportRow>(entity);
                for (int i = 0; i < matching.Count; i++)
                {
                    bindings.Add(ToBinding(matching[i]));
                    AppendSupportRows(spawnedSupports, matching[i]);
                }
            }
            catch (Exception exception)
            {
                registry.RetireTarget(request.Target, out _, out _, out _);
                return Fault(
                    request.Operation,
                    epochBefore,
                    laneEpoch,
                    "spawn of " + request.Target.ToString() + " failed while installing its rows: " + Describe(exception));
            }

            var rows = new List<TargetBindingRow>(matching.Count);
            for (int i = 0; i < matching.Count; i++)
            {
                rows.Add(RowOf(matching[i], request.Target));
            }

            AssemblyPublicationReport report = CommitView(
                request.Operation,
                null,
                view,
                view.Bindings.Merge(rows, null, new[] { request.Target }),
                view.Rules,
                new[] { request.Target },
                epochBefore,
                nextEpoch,
                nextRevision,
                publicationOrdinal + 1,
                laneEpoch,
                drained,
                0,
                1,
                "spawned " + request.Target.ToString() + " from " + request.Recipe.ToString()
                + " with " + matching.Count.ToString(CultureInfo.InvariantCulture) + " derived rows (P-024)");

            if (report.Published)
            {
                SpawnedCount++;
                publicationOrdinal++;
                PublishedRevision = nextRevision;
                MarkPublicationUsed(laneRevision, laneEpoch);
            }

            return report;
        }

        /// <summary>
        /// Despawns one target: its contributions retract, its route closes and its recipe-owned entity is destroyed
        /// (P-024). A handle taken before the despawn is rejected as `StaleHandle` from then on (P-005).
        /// </summary>
        public AssemblyPublicationReport Despawn(
            TargetHandle handle,
            OperationId operation,
            CompositionRevision laneRevision,
            AssemblyEpoch laneEpoch)
        {
            GameCoreThreading.RequireMainThread("AssemblyPublisher.Despawn");

            AssemblyEpoch epochBefore = world.CurrentEpoch;

            if (!registry.TryResolve(handle, out TargetId target, out _, out DiagnosticCode resolveCode))
            {
                StaleHandleRejectionCount++;
                return Refuse(operation, epochBefore, laneEpoch, resolveCode,
                    "handle " + handle.ToString() + " does not name a live target of this world (P-005).");
            }

            // P-006 has one publication series: the composition publication the despawn belongs to must be exactly
            // the next value of the published one, on both counters.
            if (!laneRevision.Value.Equals(laneEpoch.Value) ||
                !NextEpoch(epochBefore).Equals(laneEpoch) ||
                !NextRevision(PublishedRevision).Equals(laneRevision) ||
                !IsUnusedPublication(laneRevision, laneEpoch, adopting: false))
            {
                return Refuse(operation, epochBefore, laneEpoch, DiagnosticCode.StalePlan,
                    "a despawn publishes the next assembly of the one publication series; the request names revision "
                    + laneRevision.Value.ToString(CultureInfo.InvariantCulture) + "/epoch "
                    + laneEpoch.Value.ToString(CultureInfo.InvariantCulture) + " but the next assembly is revision "
                    + NextRevision(PublishedRevision).Value.ToString(CultureInfo.InvariantCulture) + "/epoch "
                    + NextEpoch(epochBefore).Value.ToString(CultureInfo.InvariantCulture) + " (P-006).");
            }

            AssemblyEpoch nextEpoch = laneEpoch;
            CompositionRevision nextRevision = laneRevision;
            PublishedWorldView view = assembly.Read();
            int drained = FenceOldAssembly();

            if (!registry.Retire(handle, out _, out Entity owned, out DiagnosticCode retireCode))
            {
                StaleHandleRejectionCount++;
                return Refuse(operation, epochBefore, laneEpoch, retireCode,
                    "target " + target.ToString() + " was already retired.");
            }

            EntityManager entityManager = world.EntityWorld.EntityManager;
            if (owned != Entity.Null && world.EntityWorld.IsCreated && entityManager.Exists(owned))
            {
                // Only the target's own entity is destroyed; auxiliary entities belong to its recipe's adapter (P-024).
                entityManager.DestroyEntity(owned);
            }

            AssemblyPublicationReport report = CommitView(
                operation,
                null,
                view,
                view.Bindings.WithoutTarget(target),
                view.Rules,
                new[] { target },
                epochBefore,
                nextEpoch,
                nextRevision,
                publicationOrdinal + 1,
                laneEpoch,
                drained,
                0,
                1,
                "despawned " + target.ToString() + " and retracted its contributions (P-024)");

            if (report.Published)
            {
                DespawnedCount++;
                publicationOrdinal++;
                PublishedRevision = nextRevision;
                MarkPublicationUsed(laneRevision, laneEpoch);
            }

            return report;
        }

        /// <summary>Resolves a handle for a caller that needs the entity: stale handles are refused, never mapped (P-005).</summary>
        public bool TryResolveHandle(TargetHandle handle, out TargetId target, out Entity entity)
        {
            if (registry.TryResolve(handle, out target, out entity, out _))
            {
                return true;
            }

            StaleHandleRejectionCount++;
            return false;
        }

        /// <summary>Copies the live binding rows of one target in canonical order, for tests and diagnostics.</summary>
        public IReadOnlyList<CapabilityBinding> ReadBindingRows(TargetId target)
        {
            var rows = new List<CapabilityBinding>();
            if (!registry.TryResolveTarget(target, out _, out Entity entity))
            {
                return rows;
            }

            EntityManager entityManager = world.EntityWorld.EntityManager;
            if (!entityManager.HasBuffer<CapabilityBinding>(entity))
            {
                return rows;
            }

            DynamicBuffer<CapabilityBinding> bindings = entityManager.GetBuffer<CapabilityBinding>(entity);
            for (int i = 0; i < bindings.Length; i++)
            {
                rows.Add(bindings[i]);
            }

            return rows;
        }

        /// <summary>
        /// Copies the live support rows of one target in canonical order (P-017), optionally narrowed to one
        /// `(capability, output slot)`. This is how a caller observes that an `Additive` slot was composed from
        /// several providers and which value each of them contributed (P-019, P-026).
        /// </summary>
        public IReadOnlyList<CapabilitySupportRow> ReadSupportRows(TargetId target)
        {
            var rows = new List<CapabilitySupportRow>();
            if (!registry.TryResolveTarget(target, out _, out Entity entity))
            {
                return rows;
            }

            EntityManager entityManager = world.EntityWorld.EntityManager;
            if (!entityManager.HasBuffer<CapabilitySupportRow>(entity))
            {
                return rows;
            }

            DynamicBuffer<CapabilitySupportRow> supports = entityManager.GetBuffer<CapabilitySupportRow>(entity);
            for (int i = 0; i < supports.Length; i++)
            {
                rows.Add(supports[i]);
            }

            return rows;
        }

        /// <summary>
        /// Copies the support rows of one target's specific binding slot; a slot with no support rows returns an
        /// empty list rather than a synthesised single entry, so a caller can tell "no support recorded" from
        /// "one supporter recorded" (P-017).
        /// </summary>
        public IReadOnlyList<CapabilitySupportRow> ReadSupportRows(
            TargetId target,
            CapabilityId capability,
            uint outputSlot)
        {
            var rows = new List<CapabilitySupportRow>();
            if (!registry.TryResolveTarget(target, out _, out Entity entity))
            {
                return rows;
            }

            EntityManager entityManager = world.EntityWorld.EntityManager;
            if (!entityManager.HasBuffer<CapabilitySupportRow>(entity))
            {
                return rows;
            }

            AssemblyStorage.CollectSupports(
                entityManager.GetBuffer<CapabilitySupportRow>(entity),
                capability,
                outputSlot,
                rows);
            return rows;
        }

        /// <summary>Copies the live state slots of one target, for tests and diagnostics (P-032).</summary>
        public IReadOnlyList<TargetSlotState> ReadSlotStates(TargetId target)
        {
            var slots = new List<TargetSlotState>();
            if (!registry.TryResolveTarget(target, out _, out Entity entity))
            {
                return slots;
            }

            EntityManager entityManager = world.EntityWorld.EntityManager;
            if (!entityManager.HasBuffer<TargetSlotState>(entity))
            {
                return slots;
            }

            DynamicBuffer<TargetSlotState> buffer = entityManager.GetBuffer<TargetSlotState>(entity);
            for (int i = 0; i < buffer.Length; i++)
            {
                slots.Add(buffer[i]);
            }

            return slots;
        }

        private int FenceOldAssembly()
        {
            int drained = 0;

            // Every tracked fence of the old assembly completes before its storage is touched (P-030, P-041).
            NativeFenceTable? stepFences = world.StepGroup.Fences;
            if (stepFences != null && stepFences.IsCreated && stepFences.CompleteAndReset())
            {
                drained++;
            }

            NativeFenceTable? ingressFences = world.IngressGroup.Fences;
            if (ingressFences != null && ingressFences.IsCreated && ingressFences.CompleteAndReset())
            {
                drained++;
            }

            NativeFenceTable? outputFences = world.OutputGroup.Fences;
            if (outputFences != null && outputFences.IsCreated && outputFences.CompleteAndReset())
            {
                drained++;
            }

            world.Driver.CompleteStepJobs();
            drained += world.Driver.SettleRetainedJobs();
            return drained;
        }

        private bool TryMigrateOnScratch(
            PlannedPublication publication,
            out int migratedSlots,
            out DiagnosticCode code,
            out string detail)
        {
            migratedSlots = 0;
            code = DiagnosticCode.None;
            detail = string.Empty;

            try
            {
                Faults.MaybeFailDuringMigration();
            }
            catch (Exception exception)
            {
                code = DiagnosticCode.ResourceUnavailable;
                detail = "prewrite migration failure: " + Describe(exception);
                return false;
            }

            // The source version and value come from the live slot copy; the target version comes from the
            // descriptor's declared slot schema (P-029, P-032).
            for (int i = 0; i < publication.Migrations.Count; i++)
            {
                PlannedMigration migration = publication.Migrations[i];
                StateSlotKey slot = migration.Slot;

                if (!TryReadLiveSlot(slot, out int value, out uint version))
                {
                    code = DiagnosticCode.MigrationRequired;
                    detail = "live state for slot " + slot.ToString()
                        + " could not be copied into scratch, so its migration cannot run (P-029).";
                    return false;
                }

                if (version != migration.FromVersion)
                {
                    code = DiagnosticCode.StalePlan;
                    detail = "slot " + slot.ToString() + " is at schema version "
                        + version.ToString(CultureInfo.InvariantCulture) + " but the plan expects "
                        + migration.FromVersion.ToString(CultureInfo.InvariantCulture)
                        + "; the plan was prepared against another state (P-028, P-032).";
                    return false;
                }

                if (!publication.Scratch.TryMigrate(
                        slot,
                        migration.MigrationKey,
                        version,
                        value,
                        migrations,
                        out MigrationOutcome outcome))
                {
                    code = outcome.Code == DiagnosticCode.None ? DiagnosticCode.MigrationRequired : outcome.Code;
                    detail = "migration " + migration.MigrationKey.ToString() + " rejected slot "
                        + slot.ToString() + " (" + DiagnosticCodeText.Of(code)
                        + "); the old assembly keeps its state and keeps running (P-029).";
                    return false;
                }

                migratedSlots++;
            }

            return true;
        }

        private void ApplyStructuralAndState(PlannedPublication publication, ref int writes)
        {
            EntityManager entityManager = world.EntityWorld.EntityManager;

            for (int i = 0; i < publication.Installs.Count; i++)
            {
                TargetBindingRow row = publication.Installs[i];
                if (registry.TryResolveTarget(row.Target, out _, out Entity entity))
                {
                    int before = writes;
                    writes += InstallBindingRow(entityManager, entity, row);
                    if (before == 0 && writes > 0) Faults.MaybeFailAfterFirstLiveWrite();
                }
            }

            for (int i = 0; i < publication.Removals.Count; i++)
            {
                TargetBindingRow row = publication.Removals[i];
                if (registry.TryResolveTarget(row.Target, out _, out Entity entity))
                {
                    int before = writes;
                    writes += RemoveBindingRow(entityManager, entity, row);
                    if (before == 0 && writes > 0) Faults.MaybeFailAfterFirstLiveWrite();
                }
            }

            // State dispositions: a migrated scratch value replaces its live slot value, a retraction clears derived
            // data, and every other slot keeps exactly the value it had (P-029, P-032 `Preserve`).
            for (int i = 0; i < publication.Dispositions.Count; i++)
            {
                StateDisposition disposition = publication.Dispositions[i];
                if (!registry.TryResolveTarget(disposition.Slot.Target, out _, out Entity entity))
                {
                    continue;
                }

                if (disposition.Kind == StateDispositionKind.Migrate)
                {
                    if (!publication.Scratch.TryRead(disposition.Slot, out int migrated))
                    {
                        // Scratch was validated before this point, so a missing value means the plan and the
                        // migration account disagree; substituting a default here would be implicit zero
                        // initialisation, which P-032 forbids.
                        throw new InvalidOperationException(
                            "scratch holds no migrated value for " + disposition.Slot.ToString());
                    }

                    int before = writes;
                    writes += WriteSlotState(
                        entityManager,
                        entity,
                        disposition.Slot,
                        migrated,
                        SchemaVersionOf(disposition.Slot.Slot));
                    if (before == 0 && writes > 0) Faults.MaybeFailAfterFirstLiveWrite();
                }
                else if (disposition.Kind == StateDispositionKind.Retract)
                {
                    int before = writes;
                    writes += ClearSlotState(entityManager, entity, disposition.Slot);
                    if (before == 0 && writes > 0) Faults.MaybeFailAfterFirstLiveWrite();
                }
            }

        }

        private static bool HasEffectiveChange(PlannedPublication publication)
        {
            if (publication.Installs.Count != 0 || publication.Removals.Count != 0)
            {
                return true;
            }

            for (int i = 0; i < publication.Dispositions.Count; i++)
            {
                StateDispositionKind kind = publication.Dispositions[i].Kind;
                if (kind == StateDispositionKind.Migrate || kind == StateDispositionKind.Retract)
                {
                    return true;
                }
            }

            return false;
        }

        private AssemblyPublicationReport Commit(
            PlannedPublication publication,
            AssemblyEpoch epochBefore,
            AssemblyEpoch laneEpoch,
            AssemblyEpoch nextEpoch,
            CompositionRevision nextRevision,
            int drained,
            int migratedSlots,
            int writes)
        {
            AssemblyPublicationReport report = CommitView(
                publication.Plan.Operation,
                publication,
                Published,
                publication.After,
                publication.Rules,
                publication.AffectedTargets,
                epochBefore,
                nextEpoch,
                nextRevision,
                publicationOrdinal + 1,
                laneEpoch,
                drained,
                migratedSlots,
                writes,
                "published " + publication.Installs.Count.ToString(CultureInfo.InvariantCulture)
                + " binding rows and retracted " + publication.Removals.Count.ToString(CultureInfo.InvariantCulture)
                + " for " + publication.AffectedTargets.Count.ToString(CultureInfo.InvariantCulture)
                + " target(s) from lane epoch " + laneEpoch.Value.ToString(CultureInfo.InvariantCulture));

            if (report.Published)
            {
                appliedBindingRowCount = publication.After.Count;
                publicationOrdinal++;

                // The revision decided before the apply stage is the one this publication lands on; the lane counters
                // were recorded when the publication was adopted (TryAdoptLanePublication).
                PublishedRevision = nextRevision;
                MarkPublicationUsed(AdoptedLaneRevision, AdoptedLaneEpoch);
            }

            return report;
        }

        private AssemblyPublicationReport CommitView(
            OperationId operation,
            PlannedPublication? publication,
            PublishedWorldView previous,
            TargetBindingTable bindings,
            IReadOnlyList<DerivedBindingRule> rules,
            IReadOnlyList<TargetId> affected,
            AssemblyEpoch epochBefore,
            AssemblyEpoch nextEpoch,
            CompositionRevision nextRevision,
            int ordinal,
            AssemblyEpoch laneEpoch,
            int drained,
            int migratedSlots,
            int writes,
            string detail)
        {
            _ = affected;
            var token = new SnapshotToken(world.World, nextEpoch, world.CurrentStep);

            // A spawn or despawn changes the binding table but not the execution graph, so it re-epochs the schedule
            // the previous assembly published instead of dropping it (P-030, 04 s5).
            CompiledSchedule schedule = publication != null ? publication.Schedule : previous.Schedule;
            OrderedDispatchTable? table =
                publication != null || previous.DispatchTable != null ? schedule.ToTable(nextEpoch) : null;
            var gates = new List<PublishedGate>();
            for (int i = 0; i < schedule.Entries.Count; i++)
            {
                gates.Add(new PublishedGate(schedule.Entries[i].SystemKey, nextEpoch, true));
            }

            var view = new PublishedWorldView(
                nextEpoch,
                nextRevision,
                token,
                table,
                bindings,
                rules,
                schedule,
                gates,
                ordinal);

            // The execution graph of the new assembly is installed at the fence with gates validated closed; nothing
            // dispatches in this window because the world is not pumping (P-030, 04 s4).
            if (publication != null && !TryRebindSchedule(publication, nextEpoch, out string rebindDetail))
            {
                // The step group already writes authoritative state, so a rebind failure is postwrite (P-031).
                publication.State.TryFault(DiagnosticCode.ApplyFault, rebindDetail);
                PostwriteFaultCount++;
                world.EnterFaulted(DiagnosticCode.ApplyFault, rebindDetail);
                return new AssemblyPublicationReport(
                    new PublicationRecord(
                        operation,
                        Outcome.Faulted,
                        DiagnosticCode.ApplyFault,
                        rebindDetail,
                        null,
                        PublishedRevision,
                        PublishedRevision,
                        epochBefore,
                        epochBefore,
                        null,
                        publication.Acquisitions.RetainedLeaseIds()),
                    epochBefore,
                    world.CurrentEpoch,
                    nextEpoch,
                    writes,
                    migratedSlots,
                    drained,
                    null,
                    rebindDetail);
            }

            if (!world.TryPublishAssembly(nextEpoch, view, out SnapshotToken published))
            {
                if (publication != null)
                {
                    publication.State.TryFault(DiagnosticCode.ApplyFault, "the host refused the assembly commit");
                }

                PostwriteFaultCount++;
                return new AssemblyPublicationReport(
                    new PublicationRecord(
                        operation,
                        Outcome.Faulted,
                        DiagnosticCode.ApplyFault,
                        "the host refused the assembly commit after live writes; the world stops (P-031).",
                        null,
                        PublishedRevision,
                        PublishedRevision,
                        epochBefore,
                        epochBefore,
                        null,
                        publication != null ? publication.Acquisitions.RetainedLeaseIds() : null),
                    epochBefore,
                    world.CurrentEpoch,
                    nextEpoch,
                    writes,
                    migratedSlots,
                    drained,
                    null,
                    "commit refused");
            }

            int opened = publication != null ? publication.Acquisitions.OpenGates() : 0;
            Outcome outcome = Outcome.Published;
            DiagnosticCode code = DiagnosticCode.None;
            if (publication != null)
            {
                publication.Scratch.ReleaseAll();
                if (!publication.State.TryPublish(Outcome.Published, out _))
                {
                    // An illegal transition here is a publisher defect, not a protocol outcome; failing loudly is
                    // safer than reporting a publication whose plan state contradicts it.
                    throw new InvalidOperationException(
                        "the plan state " + publication.State.Phase + " cannot publish; the publisher drove it illegally.");
                }

                outcome = publication.State.Outcome;
                code = publication.State.Code;
            }

            // Publication is visible: the record is the honest report of what the image now contains.
            var record = new PublicationRecord(
                operation,
                outcome,
                code,
                detail + "; " + opened.ToString(CultureInfo.InvariantCulture) + " staged gates opened",
                published,
                previous.Revision,
                nextRevision,
                epochBefore,
                nextEpoch,
                null,
                null);

            return new AssemblyPublicationReport(
                record,
                epochBefore,
                nextEpoch,
                laneEpoch,
                writes,
                migratedSlots,
                drained,
                published,
                record.Detail);
        }

        private bool TryRebindSchedule(PlannedPublication publication, AssemblyEpoch epoch, out string detail)
        {
            var entries = new List<GuardedDispatchEntry>(publication.Schedule.Entries.Count);
            int stageCount = descriptor.StageCount;
            for (int i = 0; i < publication.Schedule.Entries.Count; i++)
            {
                SystemDispatchEntry entry = publication.Schedule.Entries[i];
                if (!descriptor.TryGetStage(entry.Stage, out DescriptorStage? stage) || stage == null)
                {
                    detail = "the compiled schedule names stage " + entry.Stage.ToString()
                        + ", which the descriptor does not declare (P-039).";
                    return false;
                }

                entries.Add(new GuardedDispatchEntry(
                    entry.Stage,
                    entry.SystemKey,
                    entry.Kind,
                    entry.DispatchIndex,
                    stage.StageIndex,
                    stage.PredecessorStages));
            }

            var plan = new GuardedDispatchPlan(entries, publication.Schedule.Buffers, stageCount);
            if (!plan.TryValidate(out DiagnosticCode code, out string planDetail))
            {
                detail = "the compiled step schedule is not well formed: " + planDetail + " (" + code + ").";
                return false;
            }

            world.StepGroup.Bind(plan, epoch, world.Systems, world.Driver);
            detail = string.Empty;
            return true;
        }

        private AssemblyPublicationReport FaultAfterLiveWrite(
            PlannedPublication publication,
            AssemblyEpoch epochBefore,
            AssemblyEpoch laneEpoch,
            int drained,
            int writes,
            Exception exception)
        {
            string detail = "postwrite failure: " + Describe(exception);
            publication.State.TryFault(DiagnosticCode.ApplyFault, detail);
            PostwriteFaultCount++;

            // Admission stays closed, no epoch or image publishes and no simulation resumes; the staged acquisitions
            // stay retained behind the fault because unfinished work may still reach them (P-031, P-048).
            world.EnterFaulted(DiagnosticCode.ApplyFault, detail);

            var record = new PublicationRecord(
                publication.Plan.Operation,
                Outcome.Faulted,
                DiagnosticCode.ApplyFault,
                detail,
                null,
                PublishedRevision,
                PublishedRevision,
                epochBefore,
                epochBefore,
                null,
                publication.Acquisitions.RetainedLeaseIds());

            return new AssemblyPublicationReport(
                record,
                epochBefore,
                world.CurrentEpoch,
                laneEpoch,
                writes,
                0,
                drained,
                null,
                detail);
        }

        private AssemblyPublicationReport Refuse(
            OperationId operation,
            AssemblyEpoch epochBefore,
            AssemblyEpoch laneEpoch,
            DiagnosticCode code,
            string detail)
        {
            var record = new PublicationRecord(
                operation,
                Outcome.Rejected,
                code == DiagnosticCode.None ? DiagnosticCode.StalePlan : code,
                detail,
                null,
                PublishedRevision,
                PublishedRevision,
                epochBefore,
                epochBefore,
                null,
                null);

            return new AssemblyPublicationReport(
                record,
                epochBefore,
                epochBefore,
                laneEpoch,
                0,
                0,
                0,
                null,
                detail);
        }

        private AssemblyPublicationReport Fault(
            OperationId operation,
            AssemblyEpoch epochBefore,
            AssemblyEpoch laneEpoch,
            string detail)
        {
            world.EnterFaulted(DiagnosticCode.ApplyFault, detail);
            PostwriteFaultCount++;
            var record = new PublicationRecord(
                operation,
                Outcome.Faulted,
                DiagnosticCode.ApplyFault,
                detail,
                null,
                PublishedRevision,
                PublishedRevision,
                epochBefore,
                world.CurrentEpoch,
                null,
                null);

            return new AssemblyPublicationReport(
                record,
                epochBefore,
                world.CurrentEpoch,
                laneEpoch,
                0,
                0,
                0,
                null,
                detail);
        }

        private int InstallBindingRow(EntityManager entityManager, Entity entity, TargetBindingRow row)
        {
            DynamicBuffer<CapabilityBinding> bindings = entityManager.HasBuffer<CapabilityBinding>(entity)
                ? entityManager.GetBuffer<CapabilityBinding>(entity)
                : entityManager.AddBuffer<CapabilityBinding>(entity);

            CapabilityBinding installed = new CapabilityBinding
            {
                Capability = row.Capability,
                CapabilityVersion = row.CapabilityVersion,
                OutputSlot = row.OutputSlot,
                Value = row.Value,
                Provider = row.Provider,
                ProviderGeneration = row.ProviderGeneration,
                Priority = row.Priority,
                Schema = row.Schema,
                Active = 1,
                SupporterCount = row.SupporterCount,
            };

            int written = 1;
            bool replaced = false;
            for (int i = 0; i < bindings.Length; i++)
            {
                if (bindings[i].Capability.Equals(row.Capability) && bindings[i].OutputSlot == row.OutputSlot)
                {
                    // Replacing a row keeps its identity and changes only its content (P-017).
                    bindings[i] = installed;
                    replaced = true;
                    break;
                }
            }

            if (!replaced)
            {
                bindings.Add(installed);
            }

            written += WriteSupportRows(entityManager, entity, row);
            return written;
        }

        /// <summary>
        /// Publishes one row's support set (P-017): one `CapabilitySupportRow` per supporter, replacing the rows of
        /// the same `(capability, output slot)` so a re-derivation cannot leave a stale supporter behind, and
        /// removing the rows of supporters this publication no longer names — which is how removing one of two
        /// providers stays observable in the published storage (P-033).
        /// </summary>
        private static int WriteSupportRows(EntityManager entityManager, Entity entity, TargetBindingRow row)
        {
            DynamicBuffer<CapabilitySupportRow> supports = entityManager.HasBuffer<CapabilitySupportRow>(entity)
                ? entityManager.GetBuffer<CapabilitySupportRow>(entity)
                : entityManager.AddBuffer<CapabilitySupportRow>(entity);

            int written = 0;
            for (int i = supports.Length - 1; i >= 0; i--)
            {
                CapabilitySupportRow existing = supports[i];
                if (!existing.Capability.Equals(row.Capability) || existing.OutputSlot != row.OutputSlot)
                {
                    continue;
                }

                if (!ContainsSupportIdentity(row.Supports, existing.Provider, existing.Rule))
                {
                    supports.RemoveAt(i);
                    written++;
                }
            }

            for (int s = 0; s < row.Supports.Count; s++)
            {
                CapabilitySupport support = row.Supports[s];
                var published = new CapabilitySupportRow
                {
                    Capability = row.Capability,
                    OutputSlot = row.OutputSlot,
                    Provider = support.Provider,
                    ProviderGeneration = support.ProviderGeneration,
                    Rule = support.Rule,
                    Value = support.Value,
                    Priority = support.Priority,
                };

                int existingIndex;
                if (AssemblyStorage.TryFindSupport(
                        supports,
                        row.Capability,
                        row.OutputSlot,
                        support.Provider,
                        support.Rule,
                        out existingIndex))
                {
                    if (!SupportsEqual(supports[existingIndex], published))
                    {
                        supports[existingIndex] = published;
                        written++;
                    }

                    continue;
                }

                supports.Add(published);
                written++;
            }

            return written;
        }

        private static bool ContainsSupportIdentity(
            IReadOnlyList<CapabilitySupport> supports,
            ProviderInstallationId provider,
            RuleId rule)
        {
            for (int i = 0; i < supports.Count; i++)
            {
                if (supports[i].Provider.Equals(provider) && supports[i].Rule.Equals(rule))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SupportsEqual(CapabilitySupportRow left, CapabilitySupportRow right) =>
            left.Provider.Equals(right.Provider)
            && left.Rule.Equals(right.Rule)
            && left.ProviderGeneration == right.ProviderGeneration
            && left.Value == right.Value
            && left.Priority == right.Priority;

        private static int RemoveBindingRow(EntityManager entityManager, Entity entity, TargetBindingRow row)
        {
            if (!entityManager.HasBuffer<CapabilityBinding>(entity))
            {
                return 0;
            }

            int removed = 0;
            DynamicBuffer<CapabilityBinding> bindings = entityManager.GetBuffer<CapabilityBinding>(entity);
            for (int i = 0; i < bindings.Length; i++)
            {
                if (bindings[i].Capability.Equals(row.Capability) && bindings[i].OutputSlot == row.OutputSlot)
                {
                    // Exactly one support is removed; state another contributor still supports survives (P-033).
                    bindings.RemoveAt(i);
                    removed = 1;
                    break;
                }
            }

            if (removed == 0)
            {
                return 0;
            }

            if (entityManager.HasBuffer<CapabilitySupportRow>(entity))
            {
                DynamicBuffer<CapabilitySupportRow> supports = entityManager.GetBuffer<CapabilitySupportRow>(entity);
                for (int i = supports.Length - 1; i >= 0; i--)
                {
                    CapabilitySupportRow existing = supports[i];
                    if (existing.Capability.Equals(row.Capability)
                        && existing.OutputSlot == row.OutputSlot
                        && existing.Provider.Equals(row.Provider))
                    {
                        // The retracting provider loses exactly its own support rows; a co-supporter keeps its own.
                        supports.RemoveAt(i);
                        removed++;
                    }
                }
            }

            return removed;
        }

        private int WriteSlotState(EntityManager entityManager, Entity entity, StateSlotKey slot, int value, uint version)
        {
            DynamicBuffer<TargetSlotState> slots = entityManager.HasBuffer<TargetSlotState>(entity)
                ? entityManager.GetBuffer<TargetSlotState>(entity)
                : entityManager.AddBuffer<TargetSlotState>(entity);

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Owner.Equals(slot.Owner) && slots[i].Slot.Equals(slot.Slot))
                {
                    TargetSlotState updated = slots[i];
                    updated.Value = value;
                    updated.SchemaVersion = version;
                    updated.Active = 1;
                    slots[i] = updated;
                    return 1;
                }
            }

            slots.Add(new TargetSlotState
            {
                Slot = slot.Slot,
                Owner = slot.Owner,
                SchemaVersion = version,
                Value = value,
                Active = 1,
            });

            return 1;
        }

        /// <summary>
        /// Appends one rule's support rows to a freshly created support buffer (P-017). Used by the spawn path,
        /// which builds the whole buffer at once and therefore needs no replace/remove pass.
        /// </summary>
        private static void AppendSupportRows(DynamicBuffer<CapabilitySupportRow> destination, DerivedBindingRule rule)
        {
            for (int s = 0; s < rule.Supports.Count; s++)
            {
                CapabilitySupport support = rule.Supports[s];
                destination.Add(new CapabilitySupportRow
                {
                    Capability = rule.Capability,
                    OutputSlot = rule.OutputSlot,
                    Provider = support.Provider,
                    ProviderGeneration = support.ProviderGeneration,
                    Rule = support.Rule,
                    Value = support.Value,
                    Priority = support.Priority,
                });
            }
        }

        private static CapabilityBinding ToBinding(DerivedBindingRule rule)
        {
            return new CapabilityBinding
            {
                Capability = rule.Capability,
                CapabilityVersion = rule.CapabilityVersion,
                OutputSlot = rule.OutputSlot,
                Value = rule.Value,
                Priority = rule.Priority,
                Schema = rule.Schema,
                Provider = rule.Provider,
                ProviderGeneration = rule.ProviderGeneration,
                Active = 1,
                SupporterCount = rule.SupporterCount,
            };
        }

        private static TargetBindingRow RowOf(DerivedBindingRule rule, TargetId target) =>
            new TargetBindingRow(
                target,
                rule.Capability,
                rule.CapabilityVersion,
                rule.OutputSlot,
                rule.Value,
                rule.Provider,
                rule.ProviderGeneration,
                rule.Priority,
                rule.Schema,
                rule.Supports);

        private static int ClearSlotState(EntityManager entityManager, Entity entity, StateSlotKey slot)
        {
            if (!entityManager.HasBuffer<TargetSlotState>(entity))
            {
                return 0;
            }

            DynamicBuffer<TargetSlotState> slots = entityManager.GetBuffer<TargetSlotState>(entity);
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Owner.Equals(slot.Owner) && slots[i].Slot.Equals(slot.Slot))
                {
                    // `RemoveDerived` clears derived data; durable owner state is never dropped silently (P-032).
                    slots.RemoveAt(i);
                    return 1;
                }
            }

            return 0;
        }

        private bool TryReadLiveSlot(StateSlotKey slot, out int value, out uint version)
        {
            value = 0;
            version = 0;
            if (!registry.TryResolveTarget(slot.Target, out _, out Entity entity))
            {
                return false;
            }

            EntityManager entityManager = world.EntityWorld.EntityManager;
            if (!entityManager.HasBuffer<TargetSlotState>(entity))
            {
                return false;
            }

            DynamicBuffer<TargetSlotState> slots = entityManager.GetBuffer<TargetSlotState>(entity);
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Owner.Equals(slot.Owner) && slots[i].Slot.Equals(slot.Slot))
                {
                    value = slots[i].Value;
                    version = slots[i].SchemaVersion;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Writes the published stamp of every affected target inside the fence, so the entity's own epoch and the
        /// assembly it belongs to agree at the moment the pointer switches (P-024, P-030). Returns the rows written,
        /// which is part of the apply stage's write count.
        /// </summary>
        private int StampTargets(IReadOnlyList<TargetId> affected, AssemblyEpoch epoch, CompositionRevision revision)
        {
            EntityManager entityManager = world.EntityWorld.EntityManager;
            int writes = 0;
            for (int i = 0; i < affected.Count; i++)
            {
                if (SetPublishedStamp(entityManager, affected[i], epoch, revision))
                {
                    writes++;
                }
            }

            return writes;
        }

        private bool SetPublishedStamp(EntityManager entityManager, TargetId target, AssemblyEpoch epoch, CompositionRevision revision)
        {
            if (!registry.TryResolveTarget(target, out TargetHandle handle, out Entity entity))
            {
                return false;
            }

            if (!entityManager.HasComponent<AssemblyStamp>(entity))
            {
                return false;
            }

            AssemblyStamp stamp = entityManager.GetComponentData<AssemblyStamp>(entity);
            stamp.AssemblyEpoch = epoch.Value;
            stamp.CompositionRevision = revision.Value;
            stamp.Slot = handle.Slot;
            stamp.Published = 1;
            entityManager.SetComponentData(entity, stamp);
            return true;
        }

        private uint SchemaVersionOf(SlotId slot)
        {
            if (descriptor.TryGetSlot(slot, out OwnedSlotSpec? spec) && spec != null)
            {
                return spec.Schema.Version;
            }

            return 0U;
        }

        private static CompositionRevision NextRevision(CompositionRevision current)
        {
            if (current.TryIncrement(out CompositionRevision next))
            {
                return next;
            }

            return current;
        }


        /// <summary>
        /// The next assembly epoch after one publication (05 s2: the initial assembly is 1, so the first composition
        /// publication lands on 2). An exhausted counter returns the same value, which the callers then reject rather
        /// than wrap (P-005).
        /// </summary>
        private static AssemblyEpoch NextEpoch(AssemblyEpoch current)
        {
            if (current.TryIncrement(out AssemblyEpoch next))
            {
                return next;
            }

            return current;
        }

        /// <summary>
        /// Whether one composition publication is still available: a number is used by exactly one assembly, so a
        /// pair that was published, or that some earlier publication already consumed, cannot be used again
        /// (P-006). <paramref name="adopting"/> additionally refuses the pair that is currently adopted and waiting,
        /// which the adoption path must reject; a spawn or despawn that is *consuming* its own adopted publication
        /// passes false, because that pair is exactly the one it is about to publish.
        /// </summary>
        private bool IsUnusedPublication(CompositionRevision revision, AssemblyEpoch epoch, bool adopting)
        {
            if (revision.CompareTo(PublishedRevision) <= 0 || epoch.CompareTo(Published.Epoch) <= 0)
            {
                return false;
            }

            if (adopting
                && HasAdoptedPublication
                && AdoptedLaneRevision.Equals(revision)
                && AdoptedLaneEpoch.Equals(epoch))
            {
                // Already adopted: a second adoption of the same publication would publish two assemblies for one
                // composition change, which P-006 forbids.
                return false;
            }

            for (int i = 0; i < usedPublications.Count; i++)
            {
                if (usedPublications[i].Revision.Equals(revision) && usedPublications[i].Epoch.Equals(epoch))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Records that one composition publication produced an assembly; it can never be reused (P-006).</summary>
        private void MarkPublicationUsed(CompositionRevision revision, AssemblyEpoch epoch)
        {
            HasAdoptedPublication = false;
            usedPublications.Add(new UsedPublication(revision, epoch));
        }

        /// <summary>One composition publication that already produced an assembly in this world.</summary>
        private readonly struct UsedPublication
        {
            public UsedPublication(CompositionRevision revision, AssemblyEpoch epoch)
            {
                Revision = revision;
                Epoch = epoch;
            }

            public CompositionRevision Revision { get; }

            public AssemblyEpoch Epoch { get; }
        }

        private static string Describe(Exception exception) =>
            exception.GetType().FullName + ": " + exception.Message;
    }
}

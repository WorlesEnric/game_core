// GameCore.Unity.Runtime - the composition seam of the production restore builder (SADR-012 (studio)).
//
// Normative sources: O-21 ("C/unexposed new world -> B; validate schema/catalog, rebuild identities/composition, repair
// references, restore state and cursors before Running"), 06 s7 (the restore order), P-006 (one publication series),
// P-030 (an edit is one publication), P-049 (a new WorldId) and P-053 (what a checkpoint contains).
//
// Every V1 restore builder was a validation fixture that knew its family's recipes, values and plugins. The production
// builder knows none of that. What a game owns - how its world is created and composed - comes through one interface,
// `IProductionWorldComposer`; what the kernel owns - the order of the rebuild, the exact-revision recipe check, the
// temporal origin, target/slot seeding, composition replay, clocks, random streams, next-step messages, commands and
// outbox - stays in `ProductionRestoreTargetBuilder`. The two meet in `ProductionWorldParts`, which is exactly the set
// of kernel objects an application root already composes for a fresh world.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Execution.Persistence;
using GameCore.Execution.Time;
using GameCore.Unity.Runtime.Delivery;
using GameCore.Unity.Runtime.Integration;

namespace GameCore.Unity.Runtime.Persistence
{
    /// <summary>What one replayed composition edit produced (P-030).</summary>
    public enum ProductionEditResult
    {
        /// <summary>The edit was admitted and the world published the assembly of that one publication.</summary>
        Published = 0,

        /// <summary>The lane found the edit already true and published nothing.</summary>
        NoChange = 1,

        /// <summary>The lane or the world refused the edit; the restore refuses.</summary>
        Refused = 2,
    }

    /// <summary>Submits one composition edit to the staging world and answers the world for its publication.</summary>
    public delegate ProductionEditResult ProductionEditApplier(
        CompositionEditPayload payload,
        out DiagnosticCode code,
        out string detail);

    /// <summary>Why a production restore build refused (SADR-012). Every value carries a protocol code too (P-052).</summary>
    public enum ProductionRestoreRefusal
    {
        None = 0,

        /// <summary>The plan needs a migration the builder does not run; slot rows are migrated before planning.</summary>
        MigrationNotApplied = 1,

        /// <summary>A captured target names a recipe this build does not register (P-015, P-024).</summary>
        RecipeMissing = 2,

        /// <summary>A captured target names a recipe at another revision than this build's (P-024).</summary>
        RecipeRevisionMismatch = 3,

        /// <summary>The checkpoint's temporal model is not the world's (P-036).</summary>
        TemporalModelMismatch = 4,

        /// <summary>The unexposed world could not be created (O-01).</summary>
        WorldCreationFailed = 5,

        /// <summary>The game's composer refused to compose the staging world.</summary>
        CompositionFailed = 6,

        /// <summary>A captured target or state row could not be seeded (P-004, P-032).</summary>
        StateSeedRefused = 7,

        /// <summary>A captured composition edit was refused while it was replayed (P-010, P-013, P-016).</summary>
        CompositionReplayRefused = 8,

        /// <summary>Captured clocks or wakes could not be re-registered (P-038).</summary>
        ClockRestoreRefused = 9,

        /// <summary>Captured random streams could not be rebuilt (P-053).</summary>
        RngRestoreRefused = 10,

        /// <summary>A captured next-step message could not be re-appended (P-043).</summary>
        MessageRestoreRefused = 11,

        /// <summary>A captured command could not be re-admitted on the new session (P-037, P-050).</summary>
        CommandReadmissionRefused = 12,

        /// <summary>The captured outbox has no delivery owner to be reinstated into, or did not reinstate (P-045).</summary>
        OutboxRefused = 13,
    }

    /// <summary>
    /// The kernel-side modules a capture reads beyond the composition and the targets (P-053): plugin clocks, random
    /// streams, the next-step buffers that cross a step boundary, the frozen payloads of admitted commands and the
    /// delivery owner. A game composes one per world; the production builder fills the restored world's instance,
    /// and the same instance is what the next capture of that world reads.
    /// </summary>
    public sealed class ProductionWorldModules
    {
        private readonly Dictionary<OperationId, FrozenPayload> commandPayloads = new Dictionary<OperationId, FrozenPayload>();
        private readonly List<PluginClockSpec> declaredClocks;
        private readonly List<BufferId> nextStepBuffers;

        public ProductionWorldModules(
            IReadOnlyList<PluginClockSpec>? declaredClocks = null,
            int maxWakes = 64,
            IReadOnlyList<BufferId>? nextStepBuffers = null)
        {
            this.declaredClocks = declaredClocks == null ? new List<PluginClockSpec>() : new List<PluginClockSpec>(declaredClocks);
            this.nextStepBuffers = nextStepBuffers == null ? new List<BufferId>() : new List<BufferId>(nextStepBuffers);
            Clocks = new PluginClockRegistry(maxWakes);
            for (int i = 0; i < this.declaredClocks.Count; i++)
            {
                Clocks.TryRegister(this.declaredClocks[i], out DiagnosticCode _);
            }
        }

        /// <summary>The game's declared plugin clocks (their diagnostic names survive a restore through these).</summary>
        public IReadOnlyList<PluginClockSpec> DeclaredClocks => declaredClocks;

        /// <summary>The live clock registry: pending wakes with their remaining delay (P-038).</summary>
        public PluginClockRegistry Clocks { get; private set; }

        /// <summary>The world's random streams (P-053).</summary>
        public RngStreamTable Rng { get; set; } = new RngStreamTable();

        /// <summary>Next-step buffers whose rows cross a step boundary and are captured (P-043, P-053).</summary>
        public IReadOnlyList<BufferId> NextStepBuffers => nextStepBuffers;

        /// <summary>Frozen payloads of admitted external commands, by request (P-037, P-053).</summary>
        public IReadOnlyDictionary<OperationId, FrozenPayload> CommandPayloads => commandPayloads;

        /// <summary>The world's delivery owner, or null for a world with no external effect (P-045).</summary>
        public WorldDeliveryOwner? Delivery { get; set; }

        /// <summary>Outbox rows a world without a delivery owner reports to a capture (normally empty).</summary>
        public IReadOnlyList<OutboxRecordValue> OutboxRows =>
            Delivery == null ? Array.Empty<OutboxRecordValue>() : Delivery.ToRecords();

        /// <summary>Remembers the frozen payload of one admitted command so a capture can carry it (P-053).</summary>
        public void RecordCommandPayload(OperationId request, FrozenPayload payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            commandPayloads[request] = payload;
        }

        /// <summary>Forgets a command's payload once the request has settled.</summary>
        public bool ForgetCommandPayload(OperationId request) => commandPayloads.Remove(request);

        /// <summary>Clock specs the next capture declares: the registry's live set (P-038).</summary>
        public IReadOnlyList<PluginClockSpec> ClockSpecs => Clocks.Clocks;

        /// <summary>Replaces the clock registry with an empty one of the same bound, before a restore fills it.</summary>
        internal void ResetClocks(int maxWakes)
        {
            Clocks = new PluginClockRegistry(maxWakes);
        }

        /// <summary>The declared name of a clock, or a stable fallback for a clock the game no longer declares.</summary>
        internal string NameOf(Id128 clockId)
        {
            for (int i = 0; i < declaredClocks.Count; i++)
            {
                if (declaredClocks[i].ClockId.Equals(clockId))
                {
                    return declaredClocks[i].DiagnosticName;
                }
            }

            return "restored-clock-" + clockId.ToString();
        }
    }

    /// <summary>
    /// The composed staging world a game hands back to the production builder: the same kernel objects an
    /// application root composes for a fresh world, plus the edit applier the replay submits through (P-006, P-030).
    /// </summary>
    public sealed class ProductionWorldParts
    {
        public ProductionWorldParts(
            UnityWorldHost host,
            TargetRegistry registry,
            AssemblyPublisher publisher,
            LiveTargetIndex targets,
            LiveTargetSeeder seeder,
            CompositionHost lane,
            ProductionEditApplier applyEdit,
            ProductionWorldModules modules,
            object? owner)
        {
            Host = host ?? throw new ArgumentNullException(nameof(host));
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            Publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            Targets = targets ?? throw new ArgumentNullException(nameof(targets));
            Seeder = seeder ?? throw new ArgumentNullException(nameof(seeder));
            Lane = lane ?? throw new ArgumentNullException(nameof(lane));
            ApplyEdit = applyEdit ?? throw new ArgumentNullException(nameof(applyEdit));
            Modules = modules ?? throw new ArgumentNullException(nameof(modules));
            Owner = owner;
        }

        public UnityWorldHost Host { get; }

        public TargetRegistry Registry { get; }

        public AssemblyPublisher Publisher { get; }

        public LiveTargetIndex Targets { get; }

        public LiveTargetSeeder Seeder { get; }

        public CompositionHost Lane { get; }

        public ProductionEditApplier ApplyEdit { get; }

        public ProductionWorldModules Modules { get; }

        /// <summary>Whatever the game composed around these parts (an application root, for example).</summary>
        public object? Owner { get; }
    }

    /// <summary>What the composer is asked to compose: the captured scope tree is part of the lane seed (P-010).</summary>
    public sealed class ProductionComposeRequest
    {
        public ProductionComposeRequest(
            WorldId session,
            RestorePlan plan,
            RestoredTemporalOrigin origin,
            IReadOnlyList<ScopeRecord> seedScopes,
            int targetCapacity)
        {
            Session = session;
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            Origin = origin ?? throw new ArgumentNullException(nameof(origin));
            SeedScopes = seedScopes ?? throw new ArgumentNullException(nameof(seedScopes));
            TargetCapacity = targetCapacity;
        }

        public WorldId Session { get; }

        public RestorePlan Plan { get; }

        public RestoredTemporalOrigin Origin { get; }

        /// <summary>
        /// Every captured non-root scope with its parent, depth, isolation sets and exclusions (imports excluded: an
        /// import names an installation, which exists only after the install replay). Composing the lane with this
        /// seed recreates the whole scope tree with zero publications (P-010, P-016).
        /// </summary>
        public IReadOnlyList<ScopeRecord> SeedScopes { get; }

        /// <summary>The minimum target registry capacity the restored world needs.</summary>
        public int TargetCapacity { get; }
    }

    /// <summary>
    /// The game half of a production restore. A composer knows how its world is created (request, registration) and
    /// composed (registry, publisher, lane, pipeline); it knows nothing about checkpoints. The kernel half creates the
    /// world unexposed at the restored temporal origin, hands it to <see cref="TryCompose"/>, and rebuilds everything a
    /// checkpoint carries on the parts it gets back.
    /// </summary>
    public interface IProductionWorldComposer
    {
        /// <summary>The recipes this build registers, checked before any world exists (P-024).</summary>
        SpawnRecipeCatalog Recipes { get; }

        /// <summary>The temporal model this build's world runs (P-036).</summary>
        TemporalModel TemporalModel { get; }

        /// <summary>The creation request of the restored session's world (P-049: a new WorldId).</summary>
        WorldCreateRequest CreateRequest(WorldId session);

        /// <summary>The registration the restored world is created with: the same one a fresh world uses.</summary>
        UnityWorldRegistration CreateRegistration();

        /// <summary>
        /// Composes the unexposed staging world. The world must be Running when this returns, because the executor
        /// validates a running world (O-21). A false result leaves the host for the builder to discard.
        /// </summary>
        bool TryCompose(
            UnityWorldHost staging,
            ProductionComposeRequest request,
            out ProductionWorldParts? parts,
            out DiagnosticCode code,
            out string detail);

        /// <summary>
        /// Undoes whatever the composer registered outside the world (an adapter frame, for example) for a staging
        /// world the restore discards. The builder disposes the world itself afterwards when the executor does not.
        /// </summary>
        void Abandon(ProductionWorldParts parts);
    }

    /// <summary>
    /// Measured facts of one production restore build (SADR-012): what was rebuilt, how many assembly publications
    /// it cost and how long each phase took. The batching contract it reports: targets, slots and the whole scope
    /// tree cost zero publications; each replayed installation, each scope's imports, a root boundary that differs
    /// and a world mode that differs cost exactly one publication each, independent of the target count.
    /// </summary>
    public sealed class ProductionRestoreReport
    {
        public ProductionRestoreRefusal Refusal { get; internal set; }

        public DiagnosticCode Code { get; internal set; }

        public string Detail { get; internal set; } = string.Empty;

        public bool Built => Refusal == ProductionRestoreRefusal.None && Code == DiagnosticCode.None;

        public RestoredTemporalOrigin Origin { get; internal set; } = RestoredTemporalOrigin.Fresh;

        public int Targets { get; internal set; }

        public int Slots { get; internal set; }

        public int DormantSlots { get; internal set; }

        public int PrunedSlots { get; internal set; }

        /// <summary>Captured non-root scopes carried by the lane seed: zero publications for any number of them.</summary>
        public int SeededScopes { get; internal set; }

        public int ReplayedInstalls { get; internal set; }

        public int ReplayedImportScopes { get; internal set; }

        public int ReplayedRootBoundaries { get; internal set; }

        public int ReplayedModes { get; internal set; }

        public int Clocks { get; internal set; }

        public int Wakes { get; internal set; }

        public int RngStreams { get; internal set; }

        public int NextStepMessages { get; internal set; }

        public int ReadmittedCommands { get; internal set; }

        public int OutboxRows { get; internal set; }

        /// <summary>Assembly publications the composition replay performed (the initial assembly not included).</summary>
        public int Publications { get; internal set; }

        /// <summary>Replayed edits the lane answered with no change (no publication).</summary>
        public int NoChangeEdits { get; internal set; }

        public double CreateMilliseconds { get; internal set; }

        public double ComposeMilliseconds { get; internal set; }

        public double SeedMilliseconds { get; internal set; }

        public double ReplayMilliseconds { get; internal set; }

        public double ModulesMilliseconds { get; internal set; }

        public double TotalMilliseconds { get; internal set; }

        public string Describe() =>
            (Built ? "built" : "refused " + Refusal.ToString() + "/" + DiagnosticCodeText.Of(Code) + ": " + Detail)
            + "; " + Origin.ToString()
            + "; targets=" + Targets.ToString(CultureInfo.InvariantCulture)
            + " slots=" + Slots.ToString(CultureInfo.InvariantCulture)
            + " (dormant " + DormantSlots.ToString(CultureInfo.InvariantCulture)
            + ", pruned " + PrunedSlots.ToString(CultureInfo.InvariantCulture) + ")"
            + " seededScopes=" + SeededScopes.ToString(CultureInfo.InvariantCulture)
            + " installs=" + ReplayedInstalls.ToString(CultureInfo.InvariantCulture)
            + " importScopes=" + ReplayedImportScopes.ToString(CultureInfo.InvariantCulture)
            + " rootBoundaries=" + ReplayedRootBoundaries.ToString(CultureInfo.InvariantCulture)
            + " modes=" + ReplayedModes.ToString(CultureInfo.InvariantCulture)
            + " publications=" + Publications.ToString(CultureInfo.InvariantCulture)
            + " noChange=" + NoChangeEdits.ToString(CultureInfo.InvariantCulture)
            + " clocks=" + Clocks.ToString(CultureInfo.InvariantCulture)
            + " wakes=" + Wakes.ToString(CultureInfo.InvariantCulture)
            + " rng=" + RngStreams.ToString(CultureInfo.InvariantCulture)
            + " nextStep=" + NextStepMessages.ToString(CultureInfo.InvariantCulture)
            + " commands=" + ReadmittedCommands.ToString(CultureInfo.InvariantCulture)
            + " outbox=" + OutboxRows.ToString(CultureInfo.InvariantCulture)
            + "; ms create=" + Ms(CreateMilliseconds) + " compose=" + Ms(ComposeMilliseconds)
            + " seed=" + Ms(SeedMilliseconds) + " replay=" + Ms(ReplayMilliseconds)
            + " modules=" + Ms(ModulesMilliseconds) + " total=" + Ms(TotalMilliseconds);

        private static string Ms(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The default edit applier over a lane, a derived-assembly pipeline and a publisher: submit, drain, publish the
    /// derived assembly, and answer a no-target-change derivation with the unchanged assembly so the lane's pair and
    /// the world's pair stay one series (P-006, P-030). An application root supplies its own applier through its
    /// validate-before-commit bridge instead (SADR-011).
    /// </summary>
    public static class ProductionRestoreEdits
    {
        public static ProductionEditApplier ThroughPipeline(
            CompositionHost lane,
            DerivedAssemblyPipeline pipeline,
            AssemblyPublisher publisher,
            Func<OperationId> nextOperation)
        {
            if (lane == null)
            {
                throw new ArgumentNullException(nameof(lane));
            }

            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            if (publisher == null)
            {
                throw new ArgumentNullException(nameof(publisher));
            }

            if (nextOperation == null)
            {
                throw new ArgumentNullException(nameof(nextOperation));
            }

            return (CompositionEditPayload payload, out DiagnosticCode code, out string detail) =>
            {
                code = DiagnosticCode.None;
                detail = string.Empty;
                EditAdmission admission = lane.SubmitEdit(payload, nextOperation(), lane.Committed.Revision);
                if (!admission.Staged && admission.Plan != null && admission.Plan.Succeeded && admission.Plan.IsNoChange)
                {
                    return ProductionEditResult.NoChange;
                }

                if (!admission.Staged)
                {
                    code = admission.Code;
                    detail = "the composition edit was refused by the staging lane (" + admission.Kind + "/"
                        + DiagnosticCodeText.Of(admission.Code) + ").";
                    return ProductionEditResult.Refused;
                }

                IReadOnlyList<PublishedOperation> published = lane.Drain();
                if (published.Count == 0 || published[0].Outcome == Outcome.Rejected)
                {
                    code = published.Count == 0 ? DiagnosticCode.ApplyFault : published[0].Code;
                    detail = "the staging lane answered the edit with "
                        + (published.Count == 0 ? "no publication" : published[0].Outcome + "/" + DiagnosticCodeText.Of(published[0].Code))
                        + ".";
                    return ProductionEditResult.Refused;
                }

                if (published[0].Outcome == Outcome.NoChange)
                {
                    return ProductionEditResult.NoChange;
                }

                DerivedAssemblyReport report = pipeline.PublishDerived(nextOperation());
                if (report.Outcome == DerivedAssemblyOutcome.Refused)
                {
                    code = report.Code;
                    detail = "the staging world refused the assembly for the edit: " + report.Describe();
                    return ProductionEditResult.Refused;
                }

                if (report.Outcome == DerivedAssemblyOutcome.NoTargetChange)
                {
                    AssemblyPublicationReport unchanged = publisher.PublishUnchangedAssembly(
                        nextOperation(), lane.Committed.Revision, lane.Committed.Epoch);
                    if (!unchanged.Published)
                    {
                        code = unchanged.Code;
                        detail = "the unchanged staging assembly was refused: " + unchanged.Detail;
                        return ProductionEditResult.Refused;
                    }
                }

                return ProductionEditResult.Published;
            };
        }
    }
}

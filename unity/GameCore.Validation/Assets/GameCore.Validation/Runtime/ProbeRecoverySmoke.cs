// GameCore.Validation.ProbeHost — the marker-free release player's recovery smoke.
//
// `-probeRecoverySmoke` drives the production recovery path with a REAL file checkpoint and NO fault latches: the
// release clone removes the fault-injection assembly, so this mode has to prove the same sequence the GC-027
// scenario proves with the production seams alone. It runs both surviving GC-013 families (narrative and cards)
// through the same eight observations and reports one digest over the sixteen steps, which the EditMode suite
// recomputes from `QualifiedNames()`.
//
// Nothing here models a kernel module. The world chain is the real one GC-018's executor builds, the capture is
// `CheckpointPublication.CaptureAndPublish` over a real `FileCheckpointStore` under the `-probeResult` directory,
// and the recovery is `WorldRecovery.Recover`/`Restart` (O-20, O-22). Every value a step reports was read from
// those modules; no helper returns a silent false, and a step that cannot do what its name says fails with the
// code and detail it received.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Execution;
using GameCore.Execution.Persistence;
using GameCore.Execution.Recovery;
using GameCore.Execution.Time;
using GameCore.Planning;
using GameCore.Rules.Narrative;
using GameCore.Unity.Fixtures;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Persistence;
using GameCore.Unity.Runtime.Recovery;
using GameCore.Unity.Runtime.Time;
using GameCore.Validation.Generated;
using GameCore.Validation.GeneratedCards;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>One named recovery-smoke observation: its qualified name, its verdict and the values it read.</summary>
    public sealed class RecoverySmokeStep
    {
        public RecoverySmokeStep(string name, bool passed, string detail)
        {
            Name = name ?? string.Empty;
            Passed = passed;
            Detail = detail ?? string.Empty;
        }

        public string Name { get; }

        public bool Passed { get; }

        public string Detail { get; }

        public override string ToString() => Name + ": " + (Passed ? "Pass" : "Fail") + " (" + Detail + ")";
    }

    /// <summary>
    /// The release-side recovery smoke: one real file checkpoint per family, the production capture and
    /// publication, the two refusals a recovery must make, a recover and a restart that each publish a new
    /// session, the recovered world's own live state, and a teardown that leaves the registry where it found it.
    /// </summary>
    public static class ProbeRecoverySmoke
    {
        /// <summary>
        /// The eight bare observations one family records, in emission order. They are the same for both families:
        /// the family is the label the probe qualifies them with, so a renamed or dropped observation changes the
        /// digest rather than shrinking the claim.
        /// </summary>
        private static readonly string[] FamilySteps =
        {
            "recovery-smoke-source-world-captures-to-a-real-file",
            "recovery-smoke-published-envelope-reloads-from-disk",
            "recovery-smoke-refuses-a-live-source",
            "recovery-smoke-refuses-a-stopped-source",
            "recovery-smoke-recover-publishes-a-new-session",
            "recovery-smoke-restart-publishes-a-new-session",
            "recovery-smoke-recovered-world-is-authoritative",
            "recovery-smoke-teardown-is-clean",
        };

        /// <summary>Name of the one digest observation the mode records after both families.</summary>
        private const string DigestName = "recovery-smoke-digest";

        /// <summary>Length of the canonical digest the mode reports; a different length is not a digest.</summary>
        private const int DigestLength = 64;

        public static void Run(ProbeReport report, string? resultPath)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            if (string.IsNullOrEmpty(resultPath))
            {
                report.Add(ProbeOutcome.Fail(
                    "recovery-smoke-result-path",
                    "the mode needs -probeResult so it has a real directory to publish a checkpoint file into"));
                return;
            }

            // The one place this file reads the result location: the checkpoint of each family is a real file
            // beside the structured result, derived from the result path exactly as the probe's other artifacts are.
            string? directory = Path.GetDirectoryName(Path.GetFullPath(resultPath!));
            string home = string.IsNullOrEmpty(directory) ? "." : directory!;
            var steps = new List<RecoverySmokeStep>(FamilySteps.Length * 2);

            RunFamily(report, steps, Gc013NarrativeHost.Label, home);
            RunFamily(report, steps, Gc013CardsHost.Label, home);
            AddDigest(report, steps);
        }

        /// <summary>
        /// The eight bare names one family records, in order. The label is the qualification the caller recomputes
        /// the digest with; the table itself is a property of the sequence rather than of a genre, so it is the
        /// same list for every family and is returned as a copy a caller cannot mutate.
        /// </summary>
        public static string[] ObservationNames(string label)
        {
            _ = label;
            return (string[])FamilySteps.Clone();
        }

        /// <summary>
        /// Every qualified observation name of one run, in emission order: both families, each in the order its
        /// eight observations are recorded. It is the table the digest is computed over, so the EditMode suite can
        /// rebuild the same lines from the observed steps.
        /// </summary>
        public static string[] QualifiedNames()
        {
            string[] narrative = QualifiedNamesFor(Gc013NarrativeHost.Label);
            string[] cards = QualifiedNamesFor(Gc013CardsHost.Label);
            var all = new string[narrative.Length + cards.Length];
            narrative.CopyTo(all, 0);
            cards.CopyTo(all, narrative.Length);
            return all;
        }

        private static string[] QualifiedNamesFor(string label)
        {
            var names = new string[FamilySteps.Length];
            for (int i = 0; i < FamilySteps.Length; i++)
            {
                names[i] = label + "/" + FamilySteps[i];
            }

            return names;
        }

        /// <summary>
        /// Runs one family's section and records its eight observations into the report and the run table. The
        /// whole section is wrapped so an exception that escapes a step body still records every one of that
        /// family's steps, as a failure carrying the exception text: a truncated section can never report fewer
        /// observations than the digest expects.
        /// </summary>
        private static void RunFamily(
            ProbeReport report,
            List<RecoverySmokeStep> steps,
            string label,
            string directory)
        {
            var recorded = new List<RecoverySmokeStep>(FamilySteps.Length);
            string error = string.Empty;
            try
            {
                IGc018Family family = BuildFamily(label);
                string checkpointPath = Path.Combine(directory, "recovery-smoke-" + label + ".checkpoint");
                new FamilyRun(family, checkpointPath).RunAll(recorded);
            }
            catch (Exception exception)
            {
                error = DescribeException(exception);
            }

            string[] bare = ObservationNames(label);
            for (int i = 0; i < bare.Length; i++)
            {
                string qualified = label + "/" + bare[i];
                RecoverySmokeStep? found = Find(recorded, qualified);
                if (found == null)
                {
                    found = new RecoverySmokeStep(
                        qualified,
                        false,
                        error.Length == 0 ? "the observation was never reached" : error);
                    recorded.Add(found);
                }

                steps.Add(found);
                report.Add(found.Passed
                    ? ProbeOutcome.Pass(found.Name, found.Detail)
                    : ProbeOutcome.Fail(found.Name, found.Detail));
            }
        }

        /// <summary>
        /// The one digest observation: a canonical digest over the sixteen family lines, and a pass only when every
        /// one of those steps passed and the digest has the shape a digest has. The lines are built in
        /// <see cref="QualifiedNames"/> order, so the EditMode suite recomputes the same literal from the observed
        /// steps.
        /// </summary>
        private static void AddDigest(ProbeReport report, List<RecoverySmokeStep> steps)
        {
            string[] qualified = QualifiedNames();
            var lines = new List<string>(qualified.Length);
            var failed = new List<string>();
            bool allPassed = steps.Count == qualified.Length;
            for (int i = 0; i < qualified.Length; i++)
            {
                RecoverySmokeStep? found = Find(steps, qualified[i]);
                bool passed = found != null && found.Passed;
                if (!passed)
                {
                    allPassed = false;
                    failed.Add(qualified[i]);
                }

                lines.Add(qualified[i] + "=" + (passed ? "pass" : "fail"));
            }

            string digest = NarrativeDigest.OfLines(lines);
            bool digestHeld = allPassed && digest.Length == DigestLength;
            string detail = "digest=" + digest
                + "; observations=" + steps.Count.ToString(CultureInfo.InvariantCulture)
                + "; expectedObservations=" + qualified.Length.ToString(CultureInfo.InvariantCulture)
                + "; failed=" + failed.Count.ToString(CultureInfo.InvariantCulture)
                + (failed.Count == 0 ? string.Empty : ": " + string.Join(" | ", failed.ToArray()));

            report.Add(digestHeld
                ? ProbeOutcome.Pass(DigestName, detail)
                : ProbeOutcome.Fail(DigestName, detail));
        }

        /// <summary>
        /// Builds one family's adapter exactly the way the surviving GC-018 runners do: the committed generated
        /// catalog through the production catalog rules, its emitted fingerprint literal checked against the
        /// catalog this run derived over (P-028), and the family's own declaration set (P-009).
        /// </summary>
        private static IGc018Family BuildFamily(string label)
        {
            if (string.Equals(label, Gc013NarrativeHost.Label, StringComparison.Ordinal))
            {
                CatalogBuildResult build = ProbeCatalog.BuildVerifiedCatalog(out ContentHash _);
                if (build.Catalog == null)
                {
                    throw new InvalidOperationException(
                        "the committed generated catalog was rejected by the production catalog rules: "
                        + build.Describe());
                }

                ImmutableCatalog catalog = build.Catalog;
                if (!ContentHash.TryParseHex(ProbeCatalog.CatalogFingerprint, out ContentHash emitted)
                    || !catalog.Fingerprint.Equals(emitted))
                {
                    throw new InvalidOperationException(
                        "the generated catalog's emitted fingerprint literal is not the catalog this run derived "
                        + "over (P-028).");
                }

                return new Gc013NarrativeHost.NarrativeFamily(
                    catalog,
                    Gc013NarrativeHost.Declarations(ProbeCatalog.FixturePluginKey, W1GateKeys.CatalogSchema),
                    ProbeCatalog.CatalogFingerprint);
            }

            CatalogBuildResult cards = CardCatalog.BuildVerifiedCatalog(out ContentHash _);
            if (cards.Catalog == null)
            {
                throw new InvalidOperationException(
                    "the committed generated card catalog was rejected by the production catalog rules: "
                    + cards.Describe());
            }

            ImmutableCatalog cardCatalog = cards.Catalog;
            if (!ContentHash.TryParseHex(CardCatalog.CatalogFingerprint, out ContentHash emittedCard)
                || !cardCatalog.Fingerprint.Equals(emittedCard))
            {
                throw new InvalidOperationException(
                    "the generated card catalog's emitted fingerprint literal is not the catalog this run derived "
                    + "over (P-028).");
            }

            return new Gc013CardsHost.CardFamily(
                cardCatalog,
                Gc013CardsHost.Declarations(),
                CardCatalog.CatalogFingerprint);
        }

        private static RecoverySmokeStep? Find(List<RecoverySmokeStep> steps, string qualifiedName)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (string.Equals(steps[i].Name, qualifiedName, StringComparison.Ordinal))
                {
                    return steps[i];
                }
            }

            return null;
        }

        private static string DescribeException(Exception exception)
            => "unhandled " + exception.GetType().FullName + ": " + exception.Message;

        /// <summary>
        /// One family's whole section: the real source world and its pipeline, one real file checkpoint of it, the
        /// two refusals a recovery over a live and over a retired session must make, a recover and a restart that
        /// each publish a new session from that file, the recovered world's own live rows, and a teardown that
        /// returns the registry to the size the section started from.
        /// </summary>
        private sealed class FamilyRun
        {
            /// <summary>Staged lease ceiling of the family's plan resource gate, in bytes (P-022).</summary>
            private const ulong StagedByteCeiling = 1024UL * 1024UL;

            private const ulong ScratchCapacityBytes = 4096UL;
            private const ulong ScratchBytesPerSlot = 64UL;
            private const ulong PrepareBytesLimit = 1024UL * 1024UL;

            /// <summary>Remaining delay the one scheduled wake carries across the boundary (P-038, P-053).</summary>
            private const ulong WakeDelaySteps = 5UL;

            /// <summary>Random streams the source world declares; their positions are part of a checkpoint (P-008).</summary>
            private const int RngStreamCount = 2;

            /// <summary>Values drawn from each stream before the capture, so the saved position is non-trivial.</summary>
            private const int RngDrawsPerStream = 3;

            /// <summary>Sessions one recovery attempt is allowed to reserve; the bound is the attempt bound (P-050).</summary>
            private const int ReservationCapacity = 16;

            private readonly IGc018Family family;
            private readonly string label;
            private readonly string checkpointPath;
            private readonly IdSequence sessionSequence;
            private readonly RecoveryTranscript transcript = new RecoveryTranscript(64);

            private readonly Dictionary<OperationId, FrozenPayload> commandPayloads =
                new Dictionary<OperationId, FrozenPayload>();

            private readonly List<UnityWorldHost> createdHosts = new List<UnityWorldHost>();
            private readonly List<Gc018FamilyRestoreBuilder> attemptBuilders = new List<Gc018FamilyRestoreBuilder>();
            private readonly List<LiveSlotState> sourceSlots = new List<LiveSlotState>();

            private readonly int registryBeforeCreate;

            private ulong operationSequence;

            private PipelineDescriptorReport? descriptor;
            private ContentHash catalogFingerprint;
            private CheckpointCodecSet? codecs;

            private WorldId sourceWorld;
            private WorldCreateRequest? sourceRequest;
            private UnityWorldHost? host;
            private TargetRegistry? registry;
            private LiveTargetIndex? targets;
            private LiveTargetSeeder? seeder;
            private AssemblyPublisher? publisher;
            private CompositionHost? lane;
            private WorldCompositionBridge? bridge;
            private DerivedAssemblyPipeline? pipeline;
            private WorldTimeDriver? time;
            private RngStreamTable? rng;
            private FileCheckpointStore? store;
            private CheckpointPublicationResult? publication;

            private WorldId secondSourceWorld;
            private WorldId recoveredSession;
            private Gc018FamilyRestoreBuilder? recoveryBuilder;

            /// <summary>Why the last edit or assembly publication was refused; reported beside the step it failed.</summary>
            private string lastFailure = string.Empty;

            public FamilyRun(IGc018Family family, string checkpointPath)
            {
                this.family = family ?? throw new ArgumentNullException(nameof(family));
                this.checkpointPath = checkpointPath ?? throw new ArgumentNullException(nameof(checkpointPath));
                label = family.Label;
                sessionSequence = new IdSequence(family.SessionSalt);

                // The size the section started from: read before anything is created, so the teardown observation
                // is meaningful even when the section failed before it built its source world.
                registryBeforeCreate = UnityWorldRegistry.Count;
            }

            public void RunAll(List<RecoverySmokeStep> steps)
            {
                steps.Add(SourceWorldCapturesToARealFile());
                steps.Add(PublishedEnvelopeReloadsFromDisk());
                steps.Add(RefusesALiveSource());
                steps.Add(RefusesAStoppedSource());
                steps.Add(RecoverPublishesANewSession());
                steps.Add(RestartPublishesANewSession());
                steps.Add(RecoveredWorldIsAuthoritative());
                steps.Add(TeardownIsClean());
            }

            // ------------------------------------------------------------------ 1. the source world and its file checkpoint

            /// <summary>
            /// Builds the family's real command-driven world, seeds its declared live and dormant rows, publishes
            /// its composition edits, queues the family's own command, and captures the world at its committed
            /// boundary into a REAL file through the production publication path (O-20).
            /// </summary>
            private RecoverySmokeStep SourceWorldCapturesToARealFile()
            {
                const string name = "recovery-smoke-source-world-captures-to-a-real-file";
                try
                {
                    PipelineDescriptorReport compiled = family.CompilePipeline();
                    descriptor = compiled;
                    if (!compiled.Succeeded || compiled.Descriptor == null || compiled.Adaptation == null
                        || compiled.Compilation == null)
                    {
                        return Step(name, false, "the ownership and schedule pipeline refused: " + compiled.Describe());
                    }

                    if (!ContentHash.TryParseHex(family.CatalogFingerprint, out catalogFingerprint))
                    {
                        return Step(name, false,
                            "the family's catalog fingerprint literal is not 64 lowercase hex characters: "
                            + family.CatalogFingerprint);
                    }

                    if (!Gc018CheckpointCodecs.TryBuild(
                            out CheckpointSerializerBindings? _, out CheckpointCodecSet? builtCodecs, out string codecDetail)
                        || builtCodecs == null)
                    {
                        return Step(name, false, "the committed checkpoint codecs did not bind: " + codecDetail);
                    }

                    codecs = builtCodecs;
                    sourceWorld = new WorldId(sessionSequence.Next());
                    sourceRequest = family.CreateRequest(sourceWorld, NextOperation(sourceWorld));
                    bool created = UnityWorldRegistry.TryCreate(
                        sourceRequest,
                        family.CreateRegistration(compiled.Adaptation!),
                        out UnityWorldHost? createdHost,
                        out WorldCreateResult createResult);
                    host = createdHost;
                    if (!created || host == null)
                    {
                        return Step(name, false,
                            "world creation failed: " + createResult.Code + ": " + createResult.Detail);
                    }

                    Track(host);

                    registry = new TargetRegistry(sourceWorld, 16);
                    publisher = new AssemblyPublisher(
                        host, registry, family.CreateRecipes(), family.CreateMigrations(), compiled.Descriptor!);
                    targets = new LiveTargetIndex(publisher.Recipes);
                    seeder = new LiveTargetSeeder(host, registry, targets);

                    IDerivationValueSource valueSource = family.CreateValues();
                    lane = CompositionHost.CreateDefault(
                        sourceWorld,
                        family.WorldRootScope,
                        new CatalogManifestSource(family.Catalog, family.Declarations),
                        null,
                        family.LaneSeed,
                        new DerivationModeSwitchValidator(valueSource, TargetView));
                    bridge = new WorldCompositionBridge(host, lane, publisher);
                    pipeline = new DerivedAssemblyPipeline(
                        host,
                        lane,
                        publisher,
                        targets,
                        seeder,
                        valueSource,
                        null,
                        null,
                        publisher.Migrations,
                        new StagedResourceGate(StagedByteCeiling, family.Issuer),
                        new PlanBudget(PrepareBytesLimit, PrepareBytesLimit, ScratchCapacityBytes, ScratchBytesPerSlot));
                    time = new WorldTimeDriver(host, new StepInputCutoff(8, 16), new PluginClockRegistry(8), 1U);
                    time.AdoptResourceTable(compiled.Adaptation!.NativeTable!);

                    // The declared clock: one persistent logical-step clock with one pending wake, so the capture
                    // carries a clock fact rather than an empty list (P-038, P-053).
                    PluginClockSpec clockSpec = new PluginClockSpec(
                        family.WakeClockId,
                        "recovery-smoke-" + label,
                        PluginClockKind.LogicalStep,
                        WakePausePolicy.Defer,
                        true);
                    if (!time.Clocks.TryRegister(clockSpec, out DiagnosticCode clockCode))
                    {
                        return Step(name, false, "registering the declared plugin clock "
                            + family.WakeClockId.ToString() + " was refused: " + clockCode);
                    }

                    Id128 wakeId = new Id128(family.WakeClockId.High, family.WakeClockId.Low ^ 0x00000000000000FFUL);
                    if (!time.TryScheduleWake(
                            family.WakeClockId,
                            wakeId,
                            family.WakePayloadSchema,
                            WakeDelaySteps,
                            0UL,
                            out WakeRecord? wake,
                            out DiagnosticCode wakeCode)
                        || wake == null)
                    {
                        return Step(name, false, "scheduling the declared wake was refused: " + wakeCode);
                    }

                    rng = new RngStreamTable();
                    for (int i = 0; i < RngStreamCount; i++)
                    {
                        var streamId = new Id128(family.WakeClockId.High ^ 0x5253UL, 100UL + (ulong)i);
                        if (!rng.TryDeclare(
                                streamId,
                                7UL + (ulong)i,
                                0x1234UL + (ulong)i,
                                out RngStream? stream,
                                out string declareDetail)
                            || stream == null)
                        {
                            return Step(name, false, "declaring random stream " + streamId.ToString()
                                + " was refused: " + declareDetail);
                        }

                        for (int d = 0; d < RngDrawsPerStream; d++)
                        {
                            stream.Next();
                        }
                    }

                    bool setupEdits = PublishEdits(family.SetupEdits);
                    bool seeded = family.SeedTargets(new Gc013WorldContext(host, targets, seeder));

                    // The dormant row this scenario declares beside the family's own active one: one target carries
                    // both, which is what makes the recovered read a comparison of two rows (P-032).
                    if (!seeder.TrySeedSlot(
                            family.DormantTarget,
                            family.DormantOwner,
                            family.DormantSlot,
                            family.DormantVersion,
                            family.DormantValue,
                            false,
                            out DiagnosticCode dormantCode,
                            out string dormantDetail))
                    {
                        return Step(name, false, "seeding the declared dormant slot was refused: " + dormantCode
                            + ": " + dormantDetail);
                    }

                    bool spareScope = PublishEdit(family.SpareScopeEdits[0], "spare-scope");
                    bool enrichment = PublishEdit(family.BoundaryEnrichment(), "boundary-enrichment");
                    CompositionEditPayload providerMount = family.MountProvider();
                    bool conservativeMode = PublishEdit(
                        family.ModeSet(PropagationMode.Conservative), "mode-set-conservative");
                    bool providerMounted = PublishEdit(providerMount, "provider-mount");
                    var scopeImports = new List<CapabilityImport>
                    {
                        new CapabilityImport(
                            family.DerivedCapability,
                            new ProviderInstallationId(providerMount.Instance.Value)),
                    };
                    bool importGranted = PublishEdit(
                        Gc018Scenario.ScopeImports(family.EnrichedScope, scopeImports), "scope-import");

                    if (!setupEdits || !seeded || !spareScope || !enrichment || !conservativeMode || !providerMounted
                        || !importGranted)
                    {
                        return Step(name, false, "the source world's declared edits or targets were refused"
                            + DescribeFailure());
                    }

                    // The source's live rows, read before the capture: the restored world's own storage is compared
                    // against exactly these rows (P-032, P-053).
                    IReadOnlyList<LiveSlotState> recorded =
                        seeder.ReadLiveSlots(new List<TargetId> { family.MovedTarget, family.DormantTarget });
                    sourceSlots.Clear();
                    for (int i = 0; i < recorded.Count; i++)
                    {
                        sourceSlots.Add(recorded[i]);
                    }

                    OperationId queuedOperation = NextOperation(sourceWorld);
                    CommandEnvelope envelope = family.QueuedCommand(sourceWorld, queuedOperation);
                    CommandAdmissionReceipt receipt = host.Submit(envelope);
                    bool admitted = receipt.Admitted;
                    if (admitted)
                    {
                        commandPayloads[queuedOperation] = envelope.Payload;
                    }

                    CaptureContext captureContext = new CaptureContext(
                        sourceWorld,
                        sourceRequest.Definition,
                        catalogFingerprint,
                        targets,
                        registry,
                        time.Clocks.Clocks,
                        time.Clocks,
                        rng,
                        lane.Committed.Mode,
                        Gc018Scenario.StepDurationTicks,
                        Gc018Scenario.TicksPerSecond,
                        Gc018Scenario.MaxStepsPerPump,
                        false,
                        lane,
                        publisher,
                        Array.Empty<BufferId>(),
                        commandPayloads);

                    var reader = new UnityCommittedBoundaryReader(host, captureContext);
                    bool atBoundary = reader.IsAtCommittedBoundary;
                    store = new FileCheckpointStore(checkpointPath);
                    CheckpointPublicationResult published = CheckpointPublication.CaptureAndPublish(
                        new CheckpointPublicationRequest(
                            reader,
                            new CheckpointCaptureRequest(
                                sourceWorld, codecs, CheckpointQueuePolicy.RejectQueued, catalogFingerprint),
                            store,
                            queuedOperation),
                        transcript);
                    publication = published;

                    bool pass = published.Succeeded
                        && store.Exists
                        && host.Lifecycle == WorldLifecycleState.Running
                        && admitted;
                    return Step(name, pass,
                        "session=" + sourceWorld.Session.ToString()
                        + "; atBoundary=" + (atBoundary ? "true" : "false")
                        + "; path=" + checkpointPath
                        + "; bytes=" + published.Stored.DocumentBytes.ToString(CultureInfo.InvariantCulture)
                        + "; documentHash=" + published.Stored.DocumentHash.ToHex()
                        + "; publishCount=" + store.PublishCount.ToString(CultureInfo.InvariantCulture)
                        + "; replaceCount=" + store.ReplaceCount.ToString(CultureInfo.InvariantCulture)
                        + "; admitted=" + (admitted ? "true" : "false")
                        + "; publication=" + published.ToString()
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    return Step(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 2. the envelope on disk

            /// <summary>
            /// A SECOND store over the same path reads the document back through the production envelope reader and
            /// must report the identical stored identity, with no temporary artifact left behind (06 s7, O-20).
            /// </summary>
            private RecoverySmokeStep PublishedEnvelopeReloadsFromDisk()
            {
                const string name = "recovery-smoke-published-envelope-reloads-from-disk";
                try
                {
                    if (store == null || publication == null || !publication.Succeeded)
                    {
                        return Step(name, false, "no publication succeeded, so there is no stored document to reload");
                    }

                    FileCheckpointStore reload = new FileCheckpointStore(checkpointPath);
                    bool read = reload.TryRead(
                        out byte[]? document,
                        out StoredCheckpoint loaded,
                        out DiagnosticCode readCode,
                        out string readDetail);
                    StoredCheckpoint stored = publication.Stored;
                    bool identical = read
                        && document != null
                        && document.Length > 0
                        && loaded.DocumentBytes == stored.DocumentBytes
                        && loaded.DocumentChecksum == stored.DocumentChecksum
                        && loaded.EnvelopeChecksum == stored.EnvelopeChecksum
                        && loaded.DocumentHash.Equals(stored.DocumentHash);
                    bool noTemporaryArtifact = !File.Exists(store.TemporaryLocation)
                        && store.TemporaryArtifactRemoved
                        && reload.TemporaryArtifactRemoved;

                    bool pass = identical && noTemporaryArtifact;
                    return Step(name, pass,
                        "reloaded=" + (read ? "true" : "false")
                        + "; documentBytes=" + (read
                            ? loaded.DocumentBytes.ToString(CultureInfo.InvariantCulture)
                            : "<none>")
                        + "; documentHash=" + (read ? loaded.DocumentHash.ToHex() : "<none>")
                        + "; envelopeChecksum=" + (read
                            ? loaded.EnvelopeChecksum.ToString("x16", CultureInfo.InvariantCulture)
                            : "<none>")
                        + "; temporaryArtifactRemoved=" + (noTemporaryArtifact ? "true" : "false")
                        + "; read=" + readCode + "(" + readDetail + ")");
                }
                catch (Exception exception)
                {
                    return Step(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 3. a live source is not recoverable

            /// <summary>
            /// A recovery is a failover over a world that is not running: the still-running source must be refused
            /// with <c>TooLate</c>, with no destination and an unchanged registry (O-22, P-002, P-049).
            /// </summary>
            private RecoverySmokeStep RefusesALiveSource()
            {
                const string name = "recovery-smoke-refuses-a-live-source";
                try
                {
                    if (host == null || descriptor == null || codecs == null || store == null || lane == null)
                    {
                        return Step(name, false, "the source world, its lane, its codecs or the store is missing");
                    }

                    WorldRecoveryReport report = WorldRecovery.Recover(BuildContext(sourceWorld, lane.Committed.Mode));
                    bool refused = report.Outcome == Outcome.Rejected
                        && report.Code == DiagnosticCode.TooLate
                        && report.DestinationHost == null
                        && report.RegistryCountAfter == report.RegistryCountBefore;

                    return Step(name, refused,
                        "outcome=" + report.Outcome.ToString()
                        + "; code=" + report.Code.ToString()
                        + "; destination=" + (report.DestinationHost == null
                            ? "<none>"
                            : report.DestinationHost.World.Session.ToString())
                        + "; registryUnchanged=" + (report.RegistryCountAfter == report.RegistryCountBefore
                            ? "true"
                            : "false")
                        + "; registry=" + report.RegistryCountBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + report.RegistryCountAfter.ToString(CultureInfo.InvariantCulture)
                        + "; detail=" + report.Detail);
                }
                catch (Exception exception)
                {
                    return Step(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 4. a retired source is a stale handle

            /// <summary>
            /// Once the source is stopped through its own host it is no longer in the registry at all, so a second
            /// recovery over that session is refused as a stale handle and still exposes nothing (O-22, P-005).
            /// </summary>
            private RecoverySmokeStep RefusesAStoppedSource()
            {
                const string name = "recovery-smoke-refuses-a-stopped-source";
                try
                {
                    if (host == null || descriptor == null || codecs == null || store == null || lane == null)
                    {
                        return Step(name, false, "the source world, its lane, its codecs or the store is missing");
                    }

                    OperationResult stop = host.Stop(NextOperation(sourceWorld), "recovery smoke: source retired");
                    bool disposed = host.Lifecycle == WorldLifecycleState.Disposed;
                    bool registered = UnityWorldRegistry.TryGet(sourceWorld, out UnityWorldHost? _);

                    WorldRecoveryReport report = WorldRecovery.Recover(BuildContext(sourceWorld, lane.Committed.Mode));
                    bool refused = report.Outcome == Outcome.Rejected
                        && report.Code == DiagnosticCode.StaleHandle
                        && report.DestinationHost == null
                        && report.RegistryCountAfter == report.RegistryCountBefore;
                    bool pass = disposed && !registered && refused;

                    return Step(name, pass,
                        "outcome=" + report.Outcome.ToString()
                        + "; code=" + report.Code.ToString()
                        + "; sourceLifecycle=" + host.Lifecycle.ToString()
                        + "; registered=" + (registered ? "true" : "false")
                        + "; stop=" + stop.Outcome.ToString()
                        + "; registry=" + report.RegistryCountBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + report.RegistryCountAfter.ToString(CultureInfo.InvariantCulture));
                }
                catch (Exception exception)
                {
                    return Step(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 5. a recover publishes a new session

            /// <summary>
            /// A recovery over a registered world that is not running reads the file checkpoint and publishes a
            /// fresh, registered incarnation with the captured active and dormant rows (O-22, P-031, P-053).
            /// </summary>
            private RecoverySmokeStep RecoverPublishesANewSession()
            {
                const string name = "recovery-smoke-recover-publishes-a-new-session";
                try
                {
                    if (descriptor == null || codecs == null || store == null)
                    {
                        return Step(name, false, "the compiled descriptor, the codecs or the store is missing");
                    }

                    secondSourceWorld = new WorldId(sessionSequence.Next());
                    bool created = UnityWorldRegistry.TryCreate(
                        family.CreateRequest(secondSourceWorld, NextOperation(secondSourceWorld)),
                        family.CreateRegistration(descriptor.Adaptation!),
                        out UnityWorldHost? createdHost,
                        out WorldCreateResult createResult);
                    if (!created || createdHost == null)
                    {
                        return Step(name, false, "the second source world could not be created: "
                            + createResult.Code + ": " + createResult.Detail);
                    }

                    Track(createdHost);

                    // A recovery reads a world that is not live, and `UnityWorldHost.CreateCore` brings every world
                    // it creates up `Running` (the initial assembly is published before `TryCreate` returns). The
                    // honest non-live state a recovery actually reads is the terminal fault state, so the second
                    // source is retired into it through the driver's own production post-write fault seam - the
                    // same `LatchFault` path `WorldHost` itself uses when a peripheral dispatch or a fence fails
                    // (P-031) - and the lifecycle this section really read is what the detail reports.
                    createdHost.Driver.LatchFault(
                        DiagnosticCode.ApplyFault, "recovery smoke: the second source is retired faulted");
                    WorldLifecycleState before = createdHost.Lifecycle;

                    WorldRecoveryReport report = WorldRecovery.Recover(
                        BuildContext(secondSourceWorld, PropagationMode.Automatic));
                    UnityWorldHost? destination = report.DestinationHost;
                    if (destination != null)
                    {
                        Track(destination);
                    }

                    recoveryBuilder = attemptBuilders.Count == 0
                        ? null
                        : attemptBuilders[attemptBuilders.Count - 1];
                    recoveredSession = destination == null ? default(WorldId) : destination.World;
                    RestoreOutcome? restore = report.Restore;
                    bool registeredDestination = destination != null
                        && UnityWorldRegistry.TryGet(destination.World, out UnityWorldHost? _);
                    bool restoredState = restore != null
                        && restore.RestoredTargets > 0
                        && restore.RestoredSlots > 0
                        && restore.DormantSlots >= 1;
                    bool documentPresent = report.Restart != null && report.Restart.DocumentPresent;

                    bool pass = report.Recovered
                        && report.DestinationIsFreshIncarnation
                        && restoredState
                        && documentPresent
                        && before == WorldLifecycleState.Faulted
                        && report.SourceLifecycleAfter == WorldLifecycleState.Disposed
                        && registeredDestination;

                    return Step(name, pass,
                        "outcome=" + report.Outcome.ToString()
                        + "; source=" + secondSourceWorld.Session.ToString()
                        + "; destination=" + (destination == null ? "<none>" : destination.World.Session.ToString())
                        + "; restoredTargets=" + (restore == null
                            ? "0"
                            : restore.RestoredTargets.ToString(CultureInfo.InvariantCulture))
                        + "; restoredSlots=" + (restore == null
                            ? "0"
                            : restore.RestoredSlots.ToString(CultureInfo.InvariantCulture))
                        + "; dormantSlots=" + (restore == null
                            ? "0"
                            : restore.DormantSlots.ToString(CultureInfo.InvariantCulture))
                        + "; documentPresent=" + (documentPresent ? "true" : "false")
                        + "; sourceLifecycle=" + before.ToString() + "->" + report.SourceLifecycleAfter.ToString()
                        + "; sourceNote=the second source is retired faulted before the recovery, because TryCreate "
                        + "publishes the initial assembly and a created world is already Running"
                        + "; detail=" + report.Detail);
                }
                catch (Exception exception)
                {
                    return Step(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 6. a restart publishes a new session

            /// <summary>
            /// A restart has no in-process source at all: it reads the same verified document with the previous
            /// session named but never contacted, publishes a third distinct incarnation, and honestly owes nothing
            /// (P-053, P-049).
            /// </summary>
            private RecoverySmokeStep RestartPublishesANewSession()
            {
                const string name = "recovery-smoke-restart-publishes-a-new-session";
                try
                {
                    if (descriptor == null || codecs == null || store == null)
                    {
                        return Step(name, false, "the compiled descriptor, the codecs or the store is missing");
                    }

                    WorldRecoveryReport report = WorldRecovery.Restart(
                        BuildContext(sourceWorld, PropagationMode.Automatic), sourceWorld);
                    UnityWorldHost? destination = report.DestinationHost;
                    if (destination != null)
                    {
                        Track(destination);
                    }

                    RestoreOutcome? restore = report.Restore;
                    bool restoredState = restore != null
                        && restore.RestoredTargets > 0
                        && restore.RestoredSlots > 0
                        && restore.DormantSlots >= 1;
                    bool documentPresent = report.Restart != null && report.Restart.DocumentPresent;
                    bool sessionsDiffer = destination != null
                        && !destination.World.Session.Equals(sourceWorld.Session)
                        && !destination.World.Session.Equals(secondSourceWorld.Session)
                        && !destination.World.Session.Equals(recoveredSession);
                    bool registeredDestination = destination != null
                        && UnityWorldRegistry.TryGet(destination.World, out UnityWorldHost? _);

                    bool pass = report.Recovered
                        && report.DestinationIsFreshIncarnation
                        && restoredState
                        && documentPresent
                        && sessionsDiffer
                        && report.ObligationsOwed == 0
                        && report.DeliveryStateIntact
                        && registeredDestination;

                    return Step(name, pass,
                        "outcome=" + report.Outcome.ToString()
                        + "; previous=" + sourceWorld.Session.ToString()
                        + "; destination=" + (destination == null ? "<none>" : destination.World.Session.ToString())
                        + "; documentPresent=" + (documentPresent ? "true" : "false")
                        + "; sessionsDiffer=" + (sessionsDiffer ? "true" : "false")
                        + "; owed=" + report.ObligationsOwed.ToString(CultureInfo.InvariantCulture)
                        + "; restoredTargets=" + (restore == null
                            ? "0"
                            : restore.RestoredTargets.ToString(CultureInfo.InvariantCulture))
                        + "; restoredSlots=" + (restore == null
                            ? "0"
                            : restore.RestoredSlots.ToString(CultureInfo.InvariantCulture))
                        + "; dormantSlots=" + (restore == null
                            ? "0"
                            : restore.DormantSlots.ToString(CultureInfo.InvariantCulture))
                        + "; detail=" + report.Detail);
                }
                catch (Exception exception)
                {
                    return Step(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 7. the recovered world's own state

            /// <summary>
            /// The recovered world is authoritative, not a plan: its own live storage, read through the same
            /// rebuilder that staged it, carries the rows the source committed, and a handle minted in the old
            /// session resolves to nothing (P-032, P-005).
            /// </summary>
            private RecoverySmokeStep RecoveredWorldIsAuthoritative()
            {
                const string name = "recovery-smoke-recovered-world-is-authoritative";
                try
                {
                    if (recoveryBuilder == null || recoveryBuilder.Seeder == null)
                    {
                        return Step(name, false, "the rebuilder that staged the recovered world, or its seeder, is missing");
                    }

                    IReadOnlyList<LiveSlotState> recovered =
                        recoveryBuilder.Seeder.ReadLiveSlots(new List<TargetId> { family.MovedTarget, family.DormantTarget });
                    bool rowsEqual = recovered.Count == sourceSlots.Count;
                    for (int i = 0; rowsEqual && i < sourceSlots.Count; i++)
                    {
                        rowsEqual = RowEquals(sourceSlots[i], recovered[i]);
                    }

                    bool oldSessionRefused = !UnityWorldRegistry.TryGet(sourceWorld, out UnityWorldHost? _);
                    UnityWorldHost? staging = recoveryBuilder.Staging;
                    bool pass = rowsEqual
                        && recoveryBuilder.RebuiltTargetCount > 0
                        && recoveryBuilder.RebuiltSlotCount > 0
                        && staging != null
                        && staging.Lifecycle == WorldLifecycleState.Running
                        && oldSessionRefused;

                    return Step(name, pass,
                        "sourceRows=" + sourceSlots.Count.ToString(CultureInfo.InvariantCulture)
                        + "; recoveredRows=" + recovered.Count.ToString(CultureInfo.InvariantCulture)
                        + "; rowsEqual=" + (rowsEqual ? "true" : "false")
                        + "; rebuiltTargets=" + recoveryBuilder.RebuiltTargetCount.ToString(CultureInfo.InvariantCulture)
                        + "; rebuiltSlots=" + recoveryBuilder.RebuiltSlotCount.ToString(CultureInfo.InvariantCulture)
                        + "; oldSessionRefused=" + (oldSessionRefused ? "true" : "false")
                        + "; stagingLifecycle=" + (staging == null ? "<none>" : staging.Lifecycle.ToString()));
                }
                catch (Exception exception)
                {
                    return Step(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 8. teardown

            /// <summary>
            /// Every world this section created is stopped and disposed, the family runtime is detached from every
            /// restored world first, and the registry and every ledger are back where the section found them
            /// (P-035, P-048).
            /// </summary>
            private RecoverySmokeStep TeardownIsClean()
            {
                const string name = "recovery-smoke-teardown-is-clean";
                try
                {
                    int before = registryBeforeCreate;
                    bool disposed = true;
                    var blockage = new List<string>();

                    for (int i = 0; i < attemptBuilders.Count; i++)
                    {
                        attemptBuilders[i].DetachRuntime();
                    }

                    int outstanding = 0;
                    int retained = 0;
                    for (int i = 0; i < createdHosts.Count; i++)
                    {
                        UnityWorldHost subject = createdHosts[i];
                        try
                        {
                            if (subject.Lifecycle != WorldLifecycleState.Disposed)
                            {
                                UnityWorldHost stopping = subject;
                                OperationResult stop = stopping.Stop(
                                    NextOperation(stopping.World), "recovery smoke: teardown");
                                _ = stop;
                            }

                            subject.Dispose();
                        }
                        catch (Exception exception)
                        {
                            disposed = false;
                            blockage.Add(subject.World.Session.ToString() + ":" + exception.Message);
                        }

                        if (subject.Lifecycle != WorldLifecycleState.Disposed)
                        {
                            disposed = false;
                        }

                        outstanding += subject.Ledger.OutstandingJobCount;
                        retained += subject.Ledger.RetainedResourceCount;
                    }

                    int after = UnityWorldRegistry.Count;
                    bool pass = createdHosts.Count > 0
                        && after == before
                        && outstanding == 0
                        && retained == 0
                        && disposed;

                    return Step(name, pass,
                        "registry=" + before.ToString(CultureInfo.InvariantCulture)
                        + "->" + after.ToString(CultureInfo.InvariantCulture)
                        + "; outstanding=" + outstanding.ToString(CultureInfo.InvariantCulture)
                        + "; retained=" + retained.ToString(CultureInfo.InvariantCulture)
                        + "; disposed=" + (disposed ? "true" : "false")
                        + "; worlds=" + createdHosts.Count.ToString(CultureInfo.InvariantCulture)
                        + (blockage.Count == 0 ? string.Empty : "; blocked=" + string.Join(" | ", blockage.ToArray())));
                }
                catch (Exception exception)
                {
                    return Step(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ helpers

            /// <summary>
            /// One recovery context over the file checkpoint: the request names the source session, the definition
            /// and temporal model of that family's own world, the catalog fingerprint the document must match, the
            /// migration graph (empty, so an unmigratable document is refused rather than guessed) and the schemas
            /// this build allocates (P-054). The rebuilder is created per attempt because it owns the staging world
            /// it creates, so a retry needs a fresh one (P-049).
            /// </summary>
            private WorldRecoveryContext BuildContext(WorldId sourceSession, PropagationMode mode)
            {
                WorldCreateRequest request = family.CreateRequest(sourceSession, NextOperation(sourceSession));
                return new WorldRecoveryContext(
                    new WorldRecoveryRequest(
                        sourceSession,
                        request.Definition,
                        request.TemporalModel,
                        mode,
                        catalogFingerprint,
                        store,
                        new CheckpointMigrationRegistry(new List<ISchemaMigrationStep>()),
                        AllocatedSchemas(),
                        true,
                        null),
                    family.CreateRegistration(descriptor!.Adaptation!),
                    () => new WorldId(sessionSequence.Next()),
                    NextOperation,
                    new RestoreReservationLedger(ReservationCapacity),
                    codecs!,
                    CreateAttemptBuilder,
                    RecoveryRetryPolicy.SingleAttempt,
                    transcript,
                    null,
                    null);
            }

            /// <summary>Builds the one rebuilder of one attempt and keeps it for the teardown observation (P-049).</summary>
            private IRestoreTargetBuilder CreateAttemptBuilder(WorldId session, OperationId operation)
            {
                _ = session;
                _ = operation;
                var created = new Gc018FamilyRestoreBuilder(
                    family,
                    descriptor!,
                    catalogFingerprint,
                    UnityWorldRegistry.Count,
                    NextOperation);
                attemptBuilders.Add(created);
                return created;
            }

            /// <summary>
            /// The schemas this build can allocate at the version it carries: the checkpoint document container,
            /// every recipe the family's catalog registers, and the payload schema the declared wake names
            /// (P-054). A captured schema version outside this set has no path to this build's version, which is
            /// what a restore must refuse rather than guess.
            /// </summary>
            private IReadOnlyList<SchemaRef> AllocatedSchemas()
            {
                var schemas = new List<SchemaRef> { CheckpointFormat.DocumentSchema };
                SpawnRecipeCatalog recipes = family.CreateRecipes();
                for (int i = 0; i < recipes.Recipes.Count; i++)
                {
                    AddDistinct(schemas, recipes.Recipes[i].Recipe.Schema);
                }

                AddDistinct(schemas, family.WakePayloadSchema);
                return schemas;
            }

            private static void AddDistinct(List<SchemaRef> schemas, SchemaRef candidate)
            {
                for (int i = 0; i < schemas.Count; i++)
                {
                    if (schemas[i].Equals(candidate))
                    {
                        return;
                    }
                }

                schemas.Add(candidate);
            }

            private bool PublishEdits(IReadOnlyList<CompositionEditPayload> payloads)
            {
                for (int i = 0; i < payloads.Count; i++)
                {
                    if (!PublishEdit(payloads[i], "setup-edit-" + i.ToString(CultureInfo.InvariantCulture)))
                    {
                        return false;
                    }
                }

                return true;
            }

            /// <summary>
            /// Applies one composition edit and publishes the world's assembly for that same publication. P-006 has
            /// one publication series, so an edit the world does not answer leaves the lane one publication ahead
            /// and every later adoption is refused as stale: the two halves are always done together.
            /// </summary>
            private bool PublishEdit(CompositionEditPayload payload, string editLabel)
            {
                if (!ApplyEdit(payload, editLabel))
                {
                    return false;
                }

                return PublishPendingAssembly();
            }

            private bool ApplyEdit(CompositionEditPayload payload, string editLabel)
            {
                if (lane == null || pipeline == null || host == null || publisher == null)
                {
                    lastFailure = editLabel + ": the world or its pipeline is missing";
                    return false;
                }

                EditAdmission admission = lane.SubmitEdit(
                    payload, NextOperation(host.World), lane.Committed.Revision);
                if (!admission.Staged)
                {
                    lastFailure = editLabel + ": the edit was refused by the lane (" + admission.Kind + "/"
                        + admission.Code + ")";
                    return false;
                }

                IReadOnlyList<PublishedOperation> published = lane.Drain();
                if (published.Count == 0 || published[0].Outcome == Outcome.Rejected)
                {
                    lastFailure = editLabel + ": the publication was refused ("
                        + (published.Count > 0
                            ? published[0].Outcome.ToString() + "/" + published[0].Code
                            : "none")
                        + ")";
                    return false;
                }

                return true;
            }

            /// <summary>
            /// Publishes the world's assembly for the lane's committed publication: the derived assembly when the
            /// derivation changed target bindings, and the unchanged assembly when it did not (P-006, P-030).
            /// </summary>
            private bool PublishPendingAssembly()
            {
                if (pipeline == null || publisher == null || lane == null || host == null)
                {
                    lastFailure = "the world or its pipeline is missing";
                    return false;
                }

                DerivedAssemblyReport report = pipeline.PublishDerived(NextOperation(host.World));
                if (report.Outcome == DerivedAssemblyOutcome.Refused)
                {
                    lastFailure = "the world refused the assembly: " + report.Describe();
                    return false;
                }

                if (report.Outcome == DerivedAssemblyOutcome.NoTargetChange)
                {
                    AssemblyPublicationReport unchanged = publisher.PublishUnchangedAssembly(
                        NextOperation(host.World), lane.Committed.Revision, lane.Committed.Epoch);
                    if (!unchanged.Published)
                    {
                        lastFailure = "the unchanged assembly was refused: " + unchanged.Detail;
                        return false;
                    }
                }

                return MatchesPublishedAssembly();
            }

            private IReadOnlyList<DerivationTarget> TargetView()
            {
                if (targets == null)
                {
                    return Array.Empty<DerivationTarget>();
                }

                DerivationInputTargets view = targets.BuildDerivationTargets();
                return view.Succeeded ? view.Targets : Array.Empty<DerivationTarget>();
            }

            private bool MatchesPublishedAssembly()
            {
                if (lane == null || publisher == null || host == null)
                {
                    return false;
                }

                return AssemblyPublisher.MatchesPublishedAssembly(
                    lane.Committed.Revision,
                    lane.Committed.Epoch,
                    publisher.PublishedRevision,
                    host.CurrentEpoch);
            }

            /// <summary>Keeps one created world for the teardown observation; no world outlives its section (P-048).</summary>
            private void Track(UnityWorldHost created)
            {
                if (!createdHosts.Contains(created))
                {
                    createdHosts.Add(created);
                }
            }

            /// <summary>Reserves one operation identity of this section, strictly increasing per issuer (P-050).</summary>
            private OperationId NextOperation(WorldId world)
            {
                operationSequence++;
                return new OperationId(world, family.Issuer, operationSequence);
            }

            private RecoverySmokeStep Step(string bareName, bool passed, string detail)
                => new RecoverySmokeStep(label + "/" + bareName, passed, detail);

            private string DescribeFailure()
                => lastFailure.Length == 0 ? string.Empty : "; failure=" + lastFailure;

            /// <summary>One live row equals another when its target, owner, slot, schema version and value all do.</summary>
            private static bool RowEquals(LiveSlotState left, LiveSlotState right)
                => left.Slot.Target.Equals(right.Slot.Target)
                && left.Slot.Owner.Equals(right.Slot.Owner)
                && left.Slot.Slot.Equals(right.Slot.Slot)
                && left.SchemaVersion == right.SchemaVersion
                && left.Value == right.Value;
        }
    }
}

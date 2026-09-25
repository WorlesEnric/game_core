// GameCore.Observation.Tests — GC-016: the real-world harness every observation test builds on.
//
// The tests of this assembly must assert on the observation storage, the provenance and the diagnostics of REAL
// Unity worlds, not on a managed model of them. This file therefore builds two complete worlds exactly the way
// the family fixtures do — the control lane over the declared catalog, the real assembly publisher, the live
// target index and seeder, the derived-assembly pipeline and the family's own step systems — and then exposes the
// handles the observation surfaces are read through.
//
// Two discipline rules shape it:
//
//   * every creation failure throws with the diagnostic code and detail the real module reported, so a harness
//     that cannot build a world fails loudly instead of leaving a half-built world for a test to misread;
//   * the family-specific inputs a test needs (the next mount to publish, the committed-event schema of the
//     family's own plane, the command the family's systems settle) are data of the real catalog and the real
//     world, never a constant this file invents.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Derivation.Fixtures;
using GameCore.Execution;
using GameCore.Execution.Messages;
using GameCore.Execution.Observation;
using GameCore.Execution.Time;
using GameCore.Gameplay.Cards;
using GameCore.Gameplay.Cards.Fixtures;
using GameCore.Gameplay.Narrative;
using GameCore.Gameplay.Narrative.Fixtures;
using GameCore.Planning;
using GameCore.Planning.Ownership;
using GameCore.Planning.Scheduling;
using GameCore.Rules.Cards;
using GameCore.Rules.Narrative;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Time;
using Unity.Entities;

namespace GameCore.Observation.Tests
{
    /// <summary>
    /// One real gameplay world of one family, together with the objects the GC-016 observation surfaces are read
    /// through: its host and observation storage, its control lane, its assembly publisher, its live target index
    /// and seeder, its derived-assembly pipeline and its time driver. Nothing here is a stand-in for a production
    /// module; every member is the module's own object.
    /// </summary>
    public sealed class ObservationFamilyWorld : IDisposable
    {
        private readonly Id128 issuer;
        private ulong operationSequence;
        private bool disposed;

        internal ObservationFamilyWorld(
            string family,
            WorldId world,
            Id128 issuer,
            ulong lastOperationSequence,
            UnityWorldHost host,
            CompositionHost lane,
            WorldCompositionBridge bridge,
            AssemblyPublisher publisher,
            TargetRegistry registry,
            LiveTargetIndex targets,
            LiveTargetSeeder seeder,
            DerivedAssemblyPipeline pipeline,
            WorldTimeDriver time,
            ScopeId rootScope,
            SchemaRef committedEventSchema,
            CompositionEditPayload stagedEdit,
            NarrativeModule? narrative,
            CardTableModule? cards)
        {
            Family = family;
            World = world;
            this.issuer = issuer;
            operationSequence = lastOperationSequence;
            Host = host;
            Lane = lane;
            Bridge = bridge;
            Publisher = publisher;
            Registry = registry;
            Targets = targets;
            Seeder = seeder;
            Pipeline = pipeline;
            Time = time;
            RootScope = rootScope;
            CommittedEventSchema = committedEventSchema;
            StagedEdit = stagedEdit;
            Narrative = narrative;
            Cards = cards;
        }

        /// <summary>Family this world belongs to; one of the harness's two family names.</summary>
        public string Family { get; }

        public WorldId World { get; }

        public Id128 Issuer => issuer;

        /// <summary>The owned world host; its <c>Observation</c> member is the surface under test.</summary>
        public UnityWorldHost Host { get; }

        public CompositionHost Lane { get; }

        public WorldCompositionBridge Bridge { get; }

        public AssemblyPublisher Publisher { get; }

        public TargetRegistry Registry { get; }

        public LiveTargetIndex Targets { get; }

        public LiveTargetSeeder Seeder { get; }

        public DerivedAssemblyPipeline Pipeline { get; }

        public WorldTimeDriver Time { get; }

        /// <summary>The lane's root scope: the world root for narrative, the match for cards.</summary>
        public ScopeId RootScope { get; }

        /// <summary>Committed-event schema this family's own plane publishes, used when building an event stream.</summary>
        public SchemaRef CommittedEventSchema { get; }

        /// <summary>
        /// A real composition edit this family's lane accepts and that has not been published yet: its next
        /// provider mount. It is the edit the staged-status observation stages and drains.
        /// </summary>
        public CompositionEditPayload StagedEdit { get; }

        public NarrativeModule? Narrative { get; }

        public CardTableModule? Cards { get; }

        /// <summary>The report of the most recent setup publication this harness drove.</summary>
        public DerivedAssemblyReport? LastReport { get; private set; }

        /// <summary>The most recent setup publication whose planned assembly carried a real plan object.</summary>
        public DerivedAssemblyReport? LastPlannedReport { get; private set; }

        /// <summary>The publisher's report of the most recent setup publication.</summary>
        public AssemblyPublicationReport? LastPublication { get; private set; }

        /// <summary>A fresh operation id of this world, issued from the family's own issuer identity (P-050).</summary>
        public OperationId NextOperation()
        {
            operationSequence++;
            return new OperationId(World, issuer, operationSequence);
        }

        /// <summary>The newest committed image token; throws when the world has published none at all (P-045).</summary>
        public SnapshotToken LatestBoundary()
        {
            if (!Host.Observation.TryGetLatestBoundary(out SnapshotToken token))
            {
                throw new InvalidOperationException(
                    "the " + Family + " world has published no committed image for an observer to lease (P-045).");
            }

            return token;
        }

        /// <summary>Leases the newest committed boundary through the frozen reader the checkpoint seam names.</summary>
        public CommittedBoundaryLeaseResult LeaseLatestBoundary(int maxEvents)
            => Host.Observation.LeaseCommittedBoundary(CommittedBoundaryRequest.Latest(maxEvents));

        /// <summary>
        /// Submits one real command of this family and pumps exactly one frame, so the world commits exactly one
        /// logical step. The command is built from the live state the family's own rules validate against.
        /// </summary>
        public ulong CommitOneStep()
        {
            CommandAdmissionReceipt receipt = SubmitFamilyCommand();
            if (!receipt.Admitted)
            {
                throw new InvalidOperationException(
                    "the " + Family + " host refused its own command: " + receipt.Result.Kind
                    + " (" + receipt.Result.Code + ")");
            }

            ulong before = Host.CurrentStep.Value;
            TimeFrameReport frame = Time.PumpFrame(ObservationFamilyHarness.IdleTicksPerFrame);
            ulong committed = Host.CurrentStep.Value - before;
            if (committed != 1UL)
            {
                throw new InvalidOperationException(
                    "the " + Family + " world committed " + committed.ToString(CultureInfo.InvariantCulture)
                    + " steps for one admitted command; the time frame reported "
                    + frame.Pump.Code.ToString() + " with " + frame.StepsCommitted.ToString(CultureInfo.InvariantCulture)
                    + " committed steps");
            }

            return Host.CurrentStep.Value;
        }

        /// <summary>Submits one real command envelope of this family without pumping; the caller advances the world.</summary>
        public CommandAdmissionReceipt SubmitFamilyCommand()
        {
            if (Narrative != null)
            {
                return SubmitNarrativeChoice();
            }

            if (Cards != null)
            {
                return SubmitCardTransfer();
            }

            throw new InvalidOperationException("the " + Family + " world has no family module to command.");
        }

        /// <summary>
        /// Publishes one composition edit on the lane and the world assembly that belongs to it, exactly as the
        /// family fixtures do: submit, publish at the boundary, derive, and publish the unchanged assembly when the
        /// derivation changed no target (P-006). Throws when any real module refuses.
        /// </summary>
        public DerivedAssemblyReport PublishSetupEdit(CompositionEditPayload payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            EditAdmission admission = Lane.SubmitEdit(payload, NextOperation(), Lane.Committed.Revision);
            if (!admission.Staged)
            {
                throw new InvalidOperationException(
                    "the " + Family + " lane refused a setup edit: " + admission.Kind
                    + " (" + admission.Code + "): " + Describe(admission.Diagnostics));
            }

            IReadOnlyList<PublishedOperation> published = Lane.Drain();
            if (published.Count == 0 || published[0].Outcome == Outcome.Rejected)
            {
                throw new InvalidOperationException(
                    "the " + Family + " lane published no assembly for a setup edit: "
                    + (published.Count == 0 ? "no publication" : published[0].Outcome.ToString()));
            }

            DerivedAssemblyReport report = Pipeline.PublishDerived(NextOperation());
            if (report.Outcome == DerivedAssemblyOutcome.Refused)
            {
                throw new InvalidOperationException(
                    "the " + Family + " pipeline refused a setup derivation: " + report.Describe());
            }

            if (report.Outcome == DerivedAssemblyOutcome.NoTargetChange)
            {
                AssemblyPublicationReport unchanged = Publisher.PublishUnchangedAssembly(
                    NextOperation(), Lane.Committed.Revision, Lane.Committed.Epoch);
                if (!unchanged.Published)
                {
                    throw new InvalidOperationException(
                        "the " + Family + " publisher refused the unchanged assembly: " + unchanged.Detail);
                }
            }

            if (!AssemblyPublisher.MatchesPublishedAssembly(
                    Lane.Committed.Revision, Lane.Committed.Epoch, Publisher.PublishedRevision, Host.CurrentEpoch))
            {
                throw new InvalidOperationException(
                    "the " + Family + " world and its lane published different assemblies: lane="
                    + Lane.Committed.Revision.Value.ToString(CultureInfo.InvariantCulture)
                    + "/" + Lane.Committed.Epoch.Value.ToString(CultureInfo.InvariantCulture)
                    + " world=" + Publisher.PublishedRevision.Value.ToString(CultureInfo.InvariantCulture)
                    + "/" + Host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture));
            }

            LastReport = report;
            if (report.Plan != null)
            {
                LastPlannedReport = report;
            }

            LastPublication = report.Publication;
            return report;
        }

        /// <summary>
        /// Drains the lane's staged tail and publishes the world assembly for it, reporting the real operations the
        /// lane published. The caller has already inspected the staged status this drain settles.
        /// </summary>
        public DerivedAssemblyReport PublishStagedAndDerive(out IReadOnlyList<PublishedOperation> published)
        {
            published = Lane.Drain();
            DerivedAssemblyReport report = Pipeline.PublishDerived(NextOperation());
            if (report.Outcome == DerivedAssemblyOutcome.Refused)
            {
                throw new InvalidOperationException(
                    "the " + Family + " pipeline refused the staged derivation: " + report.Describe());
            }

            if (report.Outcome == DerivedAssemblyOutcome.NoTargetChange)
            {
                AssemblyPublicationReport unchanged = Publisher.PublishUnchangedAssembly(
                    NextOperation(), Lane.Committed.Revision, Lane.Committed.Epoch);
                if (!unchanged.Published)
                {
                    throw new InvalidOperationException(
                        "the " + Family + " publisher refused the unchanged assembly: " + unchanged.Detail);
                }
            }

            LastReport = report;
            if (report.Plan != null)
            {
                LastPlannedReport = report;
            }

            LastPublication = report.Publication;
            return report;
        }

        /// <summary>
        /// A real committed event of this world's own event stream shape: the family's committed-event schema, a
        /// strictly increasing sequence, the world's epoch and the causal operation. Used only where a test needs a
        /// bounded event stream it fully controls.
        /// </summary>
        public CommittedEvent CommittedEventOf(ulong sequence, ulong step, FrozenPayload payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            return new CommittedEvent(
                new EventCursor(World, new EventSequence(sequence)),
                CommittedEventSchema,
                Host.CurrentEpoch,
                new LogicalStepId(step),
                NextOperation(),
                payload);
        }

        /// <summary>One committed step image of this world's own shape, for a local bounded store.</summary>
        public StepCommitEvent StepCommitOf(ulong step, int dispatchedCount)
        {
            var token = new SnapshotToken(World, Host.CurrentEpoch, new LogicalStepId(step));
            ContentHash stateHash = StepFingerprint.Compute(World, Host.CurrentEpoch, token.LogicalStepId, dispatchedCount);
            return new StepCommitEvent(token, null, EventSequence.Zero, stateHash);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Narrative?.Dispose();
            Cards?.Dispose();
        }

        private CommandAdmissionReceipt SubmitNarrativeChoice()
        {
            NarrativeModule module = Narrative!;
            EntityManager entityManager = Host.EntityWorld.EntityManager;
            if (!module.TryEntity(NarrativeKeys.Mara, out Entity mara) || !entityManager.Exists(mara))
            {
                throw new InvalidOperationException(
                    "the narrative world has no live Mara target for the choice route to address (P-042).");
            }

            int node = NarrativeState.ReadOrDefault(
                entityManager, mara, NarrativeKeys.DialogueOwner, NarrativeKeys.ConversationNodeSlot,
                NarrativeChapters.Get(NarrativeChapters.ChapterOneTag).OpeningNodeOrdinal);

            // Declining is a declared choice that commits a result event and requests no fact transition, so the
            // command really settles one step of gameplay (NarrativeDialogueRules.Validate).
            var choice = new NarrativeChoice(node, NarrativeDialogueRules.DeclineChoice);
            var envelope = new CommandEnvelope(
                NextOperation(),
                NarrativeKeys.ChoiceRoute,
                NarrativeKeys.Mara,
                NarrativeKeys.ChoiceCommandSchema,
                null,
                new FrozenPayload(NarrativePayloadCodec.EncodeChoice(choice)));
            return Host.Submit(envelope);
        }

        private CommandAdmissionReceipt SubmitCardTransfer()
        {
            CardTableModule module = Cards!;
            EntityManager entityManager = Host.EntityWorld.EntityManager;
            if (module.TableEntity == Entity.Null || !entityManager.Exists(module.TableEntity))
            {
                throw new InvalidOperationException(
                    "the card world has no live table for the command route to address (P-042).");
            }

            if (!module.TrySeat(CardTableKeys.SeatAOrdinal, out Entity holder)
                || !module.TrySeat(CardTableKeys.SeatBOrdinal, out Entity _))
            {
                throw new InvalidOperationException(
                    "the card world has no live seat pair for the transfer the fixture's own rules settle (07 s2.3).");
            }

            CardTableState table = CardTableAccess.ReadTable(entityManager, module.TableEntity);
            IReadOnlyList<CardId> hand = CardTableAccess.ReadCards(entityManager, holder);
            if (hand.Count == 0)
            {
                throw new InvalidOperationException(
                    "the card world's seat " + holder.ToString() + " holds no card to transfer.");
            }

            // The live table version is the command's own guard (P-042), and the card is one the holder really has.
            var command = new CardCommandPayload(
                CardCommandKind.Transfer,
                CardTableKeys.SeatAOrdinal,
                CardTableKeys.SeatBOrdinal,
                table.TableVersion,
                hand[0],
                default(CardId),
                default(CardId));
            var envelope = new CommandEnvelope(
                NextOperation(),
                CardTableKeys.CommandRoute,
                CardIdentity.Target(CardVocabulary.TableOne),
                CardTableKeys.CommandSchema,
                null,
                CardPayloadCodec.WriteCommand(command));
            return Host.Submit(envelope);
        }

        internal static string Describe(IReadOnlyList<Diagnostic>? diagnostics)
        {
            if (diagnostics == null || diagnostics.Count == 0)
            {
                return "<none>";
            }

            var text = new System.Text.StringBuilder();
            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (i != 0)
                {
                    text.Append(" | ");
                }

                text.Append(diagnostics[i].CodeText).Append(": ").Append(diagnostics[i].Summary);
            }

            return text.ToString();
        }
    }

    /// <summary>
    /// Builds the two real family worlds the GC-016 observations run against. The construction order is the one
    /// the family fixtures themselves use, so a harness world is the same world the slice scenarios drive.
    /// </summary>
    public static class ObservationFamilyHarness
    {
        public const string NarrativeFamily = "narrative";

        public const string CardsFamily = "cards";

        /// <summary>Host ticks one pumped frame stands for; a command-driven world ignores elapsed host time.</summary>
        public const ulong IdleTicksPerFrame = 1_000_000UL;

        private const ulong ScratchCapacityBytes = 4096UL;

        private const ulong ScratchBytesPerSlot = 64UL;

        private const ulong StagedByteCeiling = 1024UL * 1024UL;

        private static readonly ulong NarrativeSalt = 0x47433031364E4152UL;

        private static readonly ulong CardsSalt = 0x4743303136434152UL;

        private static int creations;

        /// <summary>The two family names, in the order the fixtures declare them.</summary>
        public static IReadOnlyList<string> Families() => new[] { NarrativeFamily, CardsFamily };

        /// <summary>Builds one family's real world; every failure throws with the module's own code and detail.</summary>
        public static ObservationFamilyWorld Create(string family)
        {
            if (string.Equals(family, NarrativeFamily, StringComparison.Ordinal))
            {
                return CreateNarrative();
            }

            if (string.Equals(family, CardsFamily, StringComparison.Ordinal))
            {
                return CreateCards();
            }

            throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown observation family.");
        }

        private static ObservationFamilyWorld CreateNarrative()
        {
            var sessions = new IdSequence(NarrativeSalt, (ulong)Interlocked.Increment(ref creations));
            WorldId world = new WorldId(sessions.Next());
            ulong operations = 0UL;

            CatalogBuildResult build = NarrativeScenarioCatalog.Build();
            var catalog = build.Catalog ?? throw new InvalidOperationException(
                "the narrative scenario catalog was rejected: " + build.Describe());

            IReadOnlyList<CatalogPluginDeclaration> declarations = new List<CatalogPluginDeclaration>
            {
                ChapterDeclaration(1UL, true),
                ChapterDeclaration(2UL, false),
            };

            var kinds = new ScheduleDispatchKindTable()
                .Add(NarrativeKeys.InputSystem, SystemDispatchKind.ManagedSystem)
                .Add(NarrativeKeys.DialogueSystem, SystemDispatchKind.ManagedSystem)
                .Add(NarrativeKeys.QuestSystem, SystemDispatchKind.ManagedSystem)
                .Add(NarrativeKeys.GateSystem, SystemDispatchKind.ManagedSystem)
                .Add(NarrativeKeys.EncounterSystem, SystemDispatchKind.ManagedSystem)
                .Add(NarrativeKeys.OutputSystem, SystemDispatchKind.ManagedSystem);

            PipelineDescriptorReport descriptorReport = OwnershipSchedulePipeline.Build(
                Manifests(declarations), kinds, new NarrativeSlotMigrations());
            if (!descriptorReport.Succeeded)
            {
                throw new InvalidOperationException(
                    "the narrative ownership/schedule pipeline was refused: " + descriptorReport.Code
                    + ": " + descriptorReport.Detail);
            }

            WorldCreateRequest request = NarrativeRegistration.CommandDrivenRequest(
                world, NarrativeKeys.Operation(world, NextSequence(ref operations)), ContentHash.Empty);
            UnityWorldRegistration registration = NarrativeRegistration.Create(
                descriptorReport.Adaptation!, NarrativeRegistration.Systems());

            UnityWorldRegistry.TryCreate(
                request, registration, out UnityWorldHost? createdHost, out WorldCreateResult createResult);
            UnityWorldHost host = createdHost ?? throw new InvalidOperationException(
                "the narrative world could not be created: " + createResult.Code + ": " + createResult.Detail);

            NarrativeModule module = NarrativeModule.Attach(host, descriptorReport.Compilation!.Schedule!);

            var registry = new TargetRegistry(world, 16);
            var applier = new NarrativeRecipeApplier();
            var publisher = new AssemblyPublisher(
                host,
                registry,
                NarrativeRecipes.Catalog(applier),
                new MigrationRegistry(NarrativeMigrations()),
                descriptorReport.Descriptor!);
            var targets = new LiveTargetIndex(publisher.Recipes);
            var seeder = new LiveTargetSeeder(host, registry, targets);

            SeedTarget(seeder, module, NarrativeKeys.Mara, NarrativeKeys.VillageScope, NarrativeKeys.VillagerRecipe);
            SeedTarget(seeder, module, NarrativeKeys.GateEast, NarrativeKeys.VillageScope, NarrativeKeys.QuestGateRecipe);
            SeedTarget(seeder, module, NarrativeKeys.CrowdProp, NarrativeKeys.VillageScope, NarrativeKeys.DecorativeCrowdRecipe);
            SeedTarget(seeder, module, NarrativeKeys.EncounterOak, NarrativeKeys.GroveScope, NarrativeKeys.QuestEncounterRecipe);
            SeedTarget(seeder, module, NarrativeKeys.Display, NarrativeKeys.MuseumScope, NarrativeKeys.VillagerRecipe);
            SeedTarget(seeder, module, NarrativeKeys.Sailor, NarrativeKeys.HarborScope, NarrativeKeys.VillagerRecipe);
            SeedTarget(seeder, module, NarrativeKeys.QuestLedger, NarrativeKeys.RootScope, NarrativeKeys.QuestLedgerRecipe);
            if (seeder.TryGetEntity(NarrativeKeys.QuestLedger, out Entity ledger))
            {
                module.SetRootEntity(ledger);
            }

            var lane = CompositionHost.CreateDefault(
                world,
                NarrativeKeys.RootScope,
                new CatalogManifestSource(catalog, declarations),
                null,
                CompositionLaneSeed.InitialAssembly.WithScopes(NarrativeScopes.DeclaredChildren()));
            var bridge = new WorldCompositionBridge(host, lane, publisher);
            var pipeline = new DerivedAssemblyPipeline(
                host,
                lane,
                publisher,
                targets,
                seeder,
                NarrativeValues(),
                null,
                null,
                publisher.Migrations,
                new StagedResourceGate(StagedByteCeiling, NarrativeKeys.Issuer),
                DefaultBudget());

            // The conversation state of the one target a chapter will cover, one schema version behind the
            // declaration, so the mount's own publication runs the registered migration (P-029, P-032).
            SeedSlot(seeder, NarrativeKeys.Mara, NarrativeKeys.DialogueOwner, NarrativeKeys.ConversationNodeSlot, 1U, 0);
            SeedSlot(
                seeder, NarrativeKeys.Mara, NarrativeKeys.DialogueOwner, NarrativeKeys.ConversationStatusSlot,
                1U, NarrativeConversationStatus.Idle);

            var time = new WorldTimeDriver(host, new StepInputCutoff(8, 16), new PluginClockRegistry(8), 1U);
            time.AdoptResourceTable(descriptorReport.Adaptation!.NativeTable!);

            var family = new ObservationFamilyWorld(
                NarrativeFamily,
                world,
                NarrativeKeys.Issuer,
                operations,
                host,
                lane,
                bridge,
                publisher,
                registry,
                targets,
                seeder,
                pipeline,
                time,
                NarrativeKeys.RootScope,
                NarrativeKeys.ChoiceCommittedSchema,
                ChapterTwoMount(declarations),
                module,
                null);

            // The chapter's own mount is the real composition publication this world derives and publishes for:
            // it is what selects a conversation for the village targets the choice route addresses.
            family.PublishSetupEdit(
                NarrativeMounts.Mount(
                    declarations[0].Manifest,
                    NarrativeKeys.ChapterOneInstall,
                    NarrativeKeys.ChapterOneScope,
                    declarations[0].SchemaDefaults));
            return family;
        }

        private static ObservationFamilyWorld CreateCards()
        {
            var sessions = new IdSequence(CardsSalt, (ulong)Interlocked.Increment(ref creations));
            WorldId world = new WorldId(sessions.Next());
            ulong operations = 0UL;

            CatalogBuildResult build = CardCatalogTable.Build();
            var catalog = build.Catalog ?? throw new InvalidOperationException(
                "the card scenario catalog was rejected: " + build.Describe());
            IReadOnlyList<CatalogPluginDeclaration> declarations = CardTableFixture.Declarations();

            PipelineDescriptorReport descriptorReport = OwnershipSchedulePipeline.Build(
                CardTableFixture.Manifests(), CardTableRegistration.DispatchKinds(), new SlotMigrationRegistry());
            if (!descriptorReport.Succeeded)
            {
                throw new InvalidOperationException(
                    "the card ownership/schedule pipeline was refused: " + descriptorReport.Code
                    + ": " + descriptorReport.Detail);
            }

            WorldCreateRequest request = CardTableRegistration.CommandDrivenRequest(
                world, new OperationId(world, CardTableFixture.Issuer, NextSequence(ref operations)), ContentHash.Empty);
            UnityWorldRegistration registration = CardTableRegistration.Create(
                descriptorReport.Adaptation!, CardTableRegistration.Systems());

            UnityWorldRegistry.TryCreate(
                request, registration, out UnityWorldHost? createdHost, out WorldCreateResult createResult);
            UnityWorldHost host = createdHost ?? throw new InvalidOperationException(
                "the card world could not be created: " + createResult.Code + ": " + createResult.Detail);

            CardTableModule module = CardTableModule.Attach(host);

            var registry = new TargetRegistry(world, 16);
            var seatApplier = new CardSeatApplier();
            var tableApplier = new MarketTableApplier();
            var publisher = new AssemblyPublisher(
                host,
                registry,
                CardTableRecipes.Catalog(seatApplier, tableApplier),
                new MigrationRegistry(new List<ISlotMigration>()),
                descriptorReport.Descriptor!);
            var targets = new LiveTargetIndex(publisher.Recipes);
            var seeder = new LiveTargetSeeder(host, registry, targets);

            var lane = CompositionHost.CreateDefault(
                world,
                CardMarketComposition.MatchScope,
                new CatalogManifestSource(catalog, declarations),
                null,
                CompositionLaneSeed.InitialAssembly);
            var bridge = new WorldCompositionBridge(host, lane, publisher);
            var pipeline = new DerivedAssemblyPipeline(
                host,
                lane,
                publisher,
                targets,
                seeder,
                CardDerivationValueSource.Default(),
                null,
                null,
                publisher.Migrations,
                new StagedResourceGate(StagedByteCeiling, CardTableKeys.Issuer),
                DefaultBudget());

            var time = new WorldTimeDriver(host, new StepInputCutoff(8, 16), new PluginClockRegistry(8), 1U);
            time.AdoptResourceTable(descriptorReport.Adaptation!.NativeTable!);

            var family = new ObservationFamilyWorld(
                CardsFamily,
                world,
                CardTableFixture.Issuer,
                operations,
                host,
                lane,
                bridge,
                publisher,
                registry,
                targets,
                seeder,
                pipeline,
                time,
                CardMarketComposition.MatchScope,
                CardTableKeys.ResultSchema,
                CardTablePayloads.Mount(
                    CardTableFixture.ScoringDeclaration(false).Manifest,
                    CardTableFixture.QuietScoringInstance,
                    CardIdentity.Scope(CardVocabulary.LeagueB)),
                null,
                module);

            // The market's scope tree, then its targets, then the table runtime, the rule library and the scoring
            // provider: the same control-lane sequence the card fixtures publish (O-02, O-03).
            IReadOnlyList<CompositionEditPayload> scopeCreates = CardMarketComposition.ScopeCreates();
            for (int i = 0; i < scopeCreates.Count; i++)
            {
                family.PublishSetupEdit(scopeCreates[i]);
            }

            if (!CardTableFixture.SeedMarket(seeder, module, out DiagnosticCode seedCode, out string seedDetail))
            {
                throw new InvalidOperationException(
                    "seeding the card market failed: " + seedCode + ": " + seedDetail);
            }

            family.PublishSetupEdit(
                CardTablePayloads.Mount(
                    CardTableFixture.TableRuntimeDeclaration().Manifest,
                    CardTableFixture.TableRuntimeInstance,
                    CardMarketComposition.MatchScope));
            family.PublishSetupEdit(
                CardTablePayloads.Mount(
                    CardTableFixture.RuleLibraryDeclaration().Manifest,
                    CardTableFixture.RuleLibraryInstance,
                    CardMarketComposition.MatchScope));
            family.PublishSetupEdit(
                CardTablePayloads.Mount(
                    CardTableFixture.ScoringDeclaration(true).Manifest,
                    CardTableFixture.FestivalScoringInstance,
                    CardIdentity.Scope(CardVocabulary.LeagueA)));
            return family;
        }

        private static CatalogPluginDeclaration ChapterDeclaration(ulong ordinal, bool first)
        {
            PluginManifest manifest = first
                ? NarrativeDeclarations.ChapterProvider(
                    NarrativeKeys.PluginTypeId(ordinal),
                    NarrativeScenarioCatalog.PluginFactoryKey,
                    NarrativeScenarioCatalog.RecordSchema)
                : NarrativeDeclarations.ChapterTwoProvider(
                    NarrativeKeys.PluginTypeId(ordinal),
                    NarrativeScenarioCatalog.PluginFactoryKey,
                    NarrativeScenarioCatalog.RecordSchema);

            return new CatalogPluginDeclaration(manifest, ConfigDocument.Empty);
        }

        /// <summary>O-03 mount of the sibling chapter provider: the edit the staged-status observation drains.</summary>
        private static CompositionEditPayload ChapterTwoMount(IReadOnlyList<CatalogPluginDeclaration> declarations)
            => NarrativeMounts.Mount(
                declarations[1].Manifest,
                NarrativeKeys.ChapterTwoInstall,
                NarrativeKeys.ChapterTwoScope,
                declarations[1].SchemaDefaults);

        private static IReadOnlyList<PluginManifest> Manifests(IReadOnlyList<CatalogPluginDeclaration> declarations)
        {
            var manifests = new List<PluginManifest>(declarations.Count);
            for (int i = 0; i < declarations.Count; i++)
            {
                manifests.Add(declarations[i].Manifest);
            }

            return manifests;
        }

        private static IReadOnlyList<ISlotMigration> NarrativeMigrations()
            => new List<ISlotMigration>
            {
                new NarrativeConversationNodeMigration(),
                new NarrativeConversationStatusMigration(),
            };

        private static IDerivationValueSource NarrativeValues()
            => new FixtureValueSource().RegisterAlwaysPredicate(NarrativeCompositionNames.AlwaysPredicateName);

        private static PlanBudget DefaultBudget()
            => new PlanBudget(1024UL * 1024UL, 1024UL * 1024UL, ScratchCapacityBytes, ScratchBytesPerSlot);

        private static void SeedTarget(
            LiveTargetSeeder seeder,
            NarrativeModule module,
            TargetId target,
            ScopeId scope,
            DefinitionRef recipe)
        {
            if (!seeder.TrySeed(target, scope, recipe, out TargetHandle _, out DiagnosticCode code, out string detail))
            {
                throw new InvalidOperationException(
                    "seeding the narrative target " + target + " failed: " + code + ": " + detail);
            }

            if (!seeder.TryGetEntity(target, out Entity entity))
            {
                throw new InvalidOperationException(
                    "the seeder created " + target + " but the registry cannot resolve it (P-005).");
            }

            module.MapTarget(target, entity);
        }

        private static void SeedSlot(
            LiveTargetSeeder seeder,
            TargetId target,
            OwnerId owner,
            SlotId slot,
            uint version,
            int value)
        {
            if (!seeder.TrySeedSlot(target, owner, slot, version, value, out DiagnosticCode code, out string detail))
            {
                throw new InvalidOperationException(
                    "seeding " + target + "/" + slot + " failed: " + code + ": " + detail);
            }
        }

        private static ulong NextSequence(ref ulong sequence)
        {
            sequence++;
            return sequence;
        }
    }
}

// GameCore.Gameplay.Cards.Fixtures — the GC-011 card slice scenario (the Wave 3 exit demonstration).
//
// The gate sentence this file implements, from `docs/game-core/09-implementation-guide.md` (Wave 3 — Two genuinely
// different running compositions): "Run narrative and cards with the same kernel. Show zero idle command steps,
// automatic existing/future targets, a narrative state change and a card domain transfer."
//
// Every check runs the real modules over real storage, exactly as the Wave 2 gate does:
//
//   * GC-004's `CompositionHost` admits and publishes the market's scope tree and its provider mounts;
//   * GC-006's `CompositionDerivationInput` + `DerivationEngine` derive the effective scoring configuration for
//     every live seat, over the committed composition;
//   * GC-007's ownership validator and message plane carry the bounded command lane and publish the committed
//     result, and GC-009's compiler and temporal drivers produce and drive the four-stage settlement plan;
//   * GC-008's planner and publisher install the binding rows in real ECS storage and spawn a future seat fully
//     assembled;
//   * this package's own `cards.commit` settles the table and its seats as one bounded domain decision.
//
// The runner is shared by the Unity EditMode test (`GameCore.Cards.Tests` under `unity/GameCore.Validation`) and by
// the standalone player probe mode `-probeCards`, so the same scenario is proven in the Editor and in an IL2CPP
// player.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Execution;
using GameCore.Execution.Messages;
using GameCore.Execution.Time;
using GameCore.Planning;
using CompiledSchedule = GameCore.Planning.Scheduling.CompiledSchedule;
using GameCore.Rules.Cards;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Messages;
using GameCore.Unity.Runtime.Time;
using Unity.Entities;

namespace GameCore.Gameplay.Cards.Fixtures
{
    /// <summary>One named scenario observation: what was checked and the observed values.</summary>
    public sealed class CardStep
    {
        /// <summary>Builds one observation.</summary>
        public CardStep(string name, bool passed, string detail)
        {
            Name = name;
            Passed = passed;
            Detail = detail ?? string.Empty;
        }

        /// <summary>Stable observation name.</summary>
        public string Name { get; }

        /// <summary>Whether the observation held.</summary>
        public bool Passed { get; }

        /// <summary>The observed values the verdict was computed from.</summary>
        public string Detail { get; }

        /// <summary>One-line form.</summary>
        public override string ToString() => Name + ": " + (Passed ? "Pass" : "Fail") + " (" + Detail + ")";
    }

    /// <summary>
    /// Facts the scenario observed, exposed so a caller can assert on them instead of trusting a boolean. Every
    /// value is read from live module state at the moment named in its own comment.
    /// </summary>
    public sealed class CardFacts
    {
        /// <summary>Fingerprint of the catalog the run was given, so a probe can name the committed literal (P-028).</summary>
        public string CatalogFingerprint { get; set; } = string.Empty;

        // ---------------------------------------------------------------- compilation and ownership
        public int CompiledStageCount { get; set; }
        public int CompiledSystemCount { get; set; }
        public string ScheduleHash { get; set; } = string.Empty;
        public int ScheduleEdgeCount { get; set; }
        public int SchedulePlaybackPointCount { get; set; }
        public int InputStageIndex { get; set; }
        public int CommitStageIndex { get; set; }
        public bool BufferEdgeValidateBeforeCommit { get; set; }
        public int OwnershipDomainCount { get; set; }
        public int ValidatedSlotPolicyCount { get; set; }
        public int WriterCount { get; set; }
        public bool SingleOwnerPerDomain { get; set; }

        // ---------------------------------------------------------------- world and live targets
        public string WorldSession { get; set; } = string.Empty;
        public int LiveTargetCount { get; set; }
        public int SeatCount { get; set; }
        public ulong LaneEpochAfterSetup { get; set; }
        public ulong WorldEpochAfterSetup { get; set; }
        public bool CountersJoinedAfterSetup { get; set; }

        // ---------------------------------------------------------------- the mount reaches existing seats
        public int SeatABonusRowCount { get; set; }
        public int SeatABonusValue { get; set; }
        public bool SeatABonusIsActive { get; set; }
        public int SeatBBonusValue { get; set; }
        public int SeatCBonusValue { get; set; }
        public int ScoreboardRowCount { get; set; }
        public int PracticeSeatBonusRowCount { get; set; }
        public int MountInstalledRows { get; set; }
        public int ReducerRegistrations { get; set; }
        public int PredicateEvaluations { get; set; }

        // ---------------------------------------------------------------- one command, one step
        public bool CommandAdmitted { get; set; }
        public string CommandAdmissionKind { get; set; } = string.Empty;
        public ulong StepsAfterCommand { get; set; }
        public int SeatAHandBefore { get; set; }
        public int SeatAHandAfterCommit { get; set; }
        public int SeatAScoreBefore { get; set; }
        public int SeatAScoreAfterCommit { get; set; }
        public uint TableVersionAfterCommit { get; set; }
        public uint TurnNumberAfterCommit { get; set; }
        public int CommittedEventCount { get; set; }
        public int CommittedEventScoreDelta { get; set; }
        public int CommittedEventScoreAfter { get; set; }
        public int CommittedEventCardCount { get; set; }
        public ulong CommittedEventStep { get; set; }
        public bool CommittedEventMatchesLiveState { get; set; }
        public int CommittedWrites { get; set; }

        // ---------------------------------------------------------------- dedup: a duplicate transfers once
        public bool DuplicateAdmitted { get; set; }
        public string DuplicateAdmissionKind { get; set; } = string.Empty;
        public ulong StepsAfterDuplicate { get; set; }
        public int SeatBHandAfterDuplicate { get; set; }
        public int SeatBScoreAfterDuplicate { get; set; }
        public ulong PendingDemandAfterDuplicate { get; set; }
        public int DuplicateRejections { get; set; }

        // ---------------------------------------------------------------- a rejected settlement writes nothing
        public bool RejectedCommandAdmitted { get; set; }
        public string RejectedRequestKind { get; set; } = string.Empty;
        public ulong StepsAfterRejection { get; set; }
        public int SeatAHandAfterRejection { get; set; }
        public int SeatBHandAfterRejection { get; set; }
        public int SeatAScoreAfterRejection { get; set; }
        public uint TableVersionAfterRejection { get; set; }
        public int RejectedDecisionCount { get; set; }
        public int StaleCommitRejections { get; set; }

        // ---------------------------------------------------------------- a transfer commits both sides
        public int TransferGiverHandBefore { get; set; }
        public int TransferGiverHandAfter { get; set; }
        public int TransferReceiverHandBefore { get; set; }
        public int TransferReceiverHandAfter { get; set; }
        public uint TableVersionAfterTransfer { get; set; }
        public ulong StepsAfterTransfer { get; set; }
        public int TransferLostCard { get; set; }
        public int TransferGainedCard { get; set; }

        // ---------------------------------------------------------------- the future seat
        public int SpawnedSeatBonusValue { get; set; }
        public ulong SpawnedSeatStampEpoch { get; set; }
        public bool SpawnedSeatStampPublished { get; set; }
        public ulong LaneEpochAfterSpawn { get; set; }
        public ulong WorldEpochAfterSpawn { get; set; }
        public bool CountersJoinedAfterSpawn { get; set; }
        public bool ForwardDerivationHadNoTargetChange { get; set; }

        // ---------------------------------------------------------------- idle and teardown
        public int IdleFrames { get; set; }
        public int IdleStepsCommitted { get; set; }
        public int IdlePublishedImages { get; set; }
        public int IdleDispatchRuns { get; set; }
        public ulong PendingDemandAfterIdle { get; set; }
        public int OutstandingJobsAfterTeardown { get; set; }
        public int RetainedResourcesAfterTeardown { get; set; }
        public int RegistryAfterTeardown { get; set; }

        /// <summary>One-line digest of every recorded fact, so a probe archives values beside verdicts.</summary>
        public string Describe()
        {
            return "catalogFingerprint=" + CatalogFingerprint
                + "; stages=" + CompiledStageCount.ToString(CultureInfo.InvariantCulture)
                + "; systems=" + CompiledSystemCount.ToString(CultureInfo.InvariantCulture)
                + "; scheduleHash=" + ScheduleHash
                + "; edges=" + ScheduleEdgeCount.ToString(CultureInfo.InvariantCulture)
                + "; playback=" + SchedulePlaybackPointCount.ToString(CultureInfo.InvariantCulture)
                + "; inputStage=" + InputStageIndex.ToString(CultureInfo.InvariantCulture)
                + "; commitStage=" + CommitStageIndex.ToString(CultureInfo.InvariantCulture)
                + "; bufferEdge=" + BufferEdgeValidateBeforeCommit
                + "; domains=" + OwnershipDomainCount.ToString(CultureInfo.InvariantCulture)
                + "; slotPolicies=" + ValidatedSlotPolicyCount.ToString(CultureInfo.InvariantCulture)
                + "; writers=" + WriterCount.ToString(CultureInfo.InvariantCulture)
                + "; singleOwnerPerDomain=" + SingleOwnerPerDomain
                + "; session=" + WorldSession
                + "; liveTargets=" + LiveTargetCount.ToString(CultureInfo.InvariantCulture)
                + "; seats=" + SeatCount.ToString(CultureInfo.InvariantCulture)
                + "; laneEpochAfterSetup=" + LaneEpochAfterSetup.ToString(CultureInfo.InvariantCulture)
                + "; worldEpochAfterSetup=" + WorldEpochAfterSetup.ToString(CultureInfo.InvariantCulture)
                + "; joinedAfterSetup=" + CountersJoinedAfterSetup
                + "; seatARows=" + SeatABonusRowCount.ToString(CultureInfo.InvariantCulture)
                + "; seatAValue=" + SeatABonusValue.ToString(CultureInfo.InvariantCulture)
                + "; seatAActive=" + SeatABonusIsActive
                + "; seatBValue=" + SeatBBonusValue.ToString(CultureInfo.InvariantCulture)
                + "; seatCValue=" + SeatCBonusValue.ToString(CultureInfo.InvariantCulture)
                + "; scoreboardRows=" + ScoreboardRowCount.ToString(CultureInfo.InvariantCulture)
                + "; practiceRows=" + PracticeSeatBonusRowCount.ToString(CultureInfo.InvariantCulture)
                + "; installedRows=" + MountInstalledRows.ToString(CultureInfo.InvariantCulture)
                + "; reducers=" + ReducerRegistrations.ToString(CultureInfo.InvariantCulture)
                + "; predicateEvals=" + PredicateEvaluations.ToString(CultureInfo.InvariantCulture)
                + "; commandAdmitted=" + CommandAdmitted
                + "; commandAdmission=" + CommandAdmissionKind
                + "; steps=" + StepsAfterCommand.ToString(CultureInfo.InvariantCulture)
                + "; handBefore=" + SeatAHandBefore.ToString(CultureInfo.InvariantCulture)
                + "; handAfter=" + SeatAHandAfterCommit.ToString(CultureInfo.InvariantCulture)
                + "; scoreBefore=" + SeatAScoreBefore.ToString(CultureInfo.InvariantCulture)
                + "; scoreAfter=" + SeatAScoreAfterCommit.ToString(CultureInfo.InvariantCulture)
                + "; tableVersion=" + TableVersionAfterCommit.ToString(CultureInfo.InvariantCulture)
                + "; turnNumber=" + TurnNumberAfterCommit.ToString(CultureInfo.InvariantCulture)
                + "; events=" + CommittedEventCount.ToString(CultureInfo.InvariantCulture)
                + "; eventDelta=" + CommittedEventScoreDelta.ToString(CultureInfo.InvariantCulture)
                + "; eventScoreAfter=" + CommittedEventScoreAfter.ToString(CultureInfo.InvariantCulture)
                + "; eventCards=" + CommittedEventCardCount.ToString(CultureInfo.InvariantCulture)
                + "; eventStep=" + CommittedEventStep.ToString(CultureInfo.InvariantCulture)
                + "; eventMatchesLive=" + CommittedEventMatchesLiveState
                + "; writes=" + CommittedWrites.ToString(CultureInfo.InvariantCulture)
                + "; duplicateAdmitted=" + DuplicateAdmitted
                + "; duplicateAdmission=" + DuplicateAdmissionKind
                + "; stepsAfterDuplicate=" + StepsAfterDuplicate.ToString(CultureInfo.InvariantCulture)
                + "; seatBHand=" + SeatBHandAfterDuplicate.ToString(CultureInfo.InvariantCulture)
                + "; seatBScore=" + SeatBScoreAfterDuplicate.ToString(CultureInfo.InvariantCulture)
                + "; demandAfterDuplicate=" + PendingDemandAfterDuplicate.ToString(CultureInfo.InvariantCulture)
                + "; duplicateRejections=" + DuplicateRejections.ToString(CultureInfo.InvariantCulture)
                + "; rejectedAdmitted=" + RejectedCommandAdmitted
                + "; rejectedKind=" + RejectedRequestKind
                + "; stepsAfterRejection=" + StepsAfterRejection.ToString(CultureInfo.InvariantCulture)
                + "; seatAHandAfterRejection=" + SeatAHandAfterRejection.ToString(CultureInfo.InvariantCulture)
                + "; seatBHandAfterRejection=" + SeatBHandAfterRejection.ToString(CultureInfo.InvariantCulture)
                + "; seatAScoreAfterRejection=" + SeatAScoreAfterRejection.ToString(CultureInfo.InvariantCulture)
                + "; tableVersionAfterRejection=" + TableVersionAfterRejection.ToString(CultureInfo.InvariantCulture)
                + "; rejectedDecisions=" + RejectedDecisionCount.ToString(CultureInfo.InvariantCulture)
                + "; staleCommitRejections=" + StaleCommitRejections.ToString(CultureInfo.InvariantCulture)
                + "; giverBefore=" + TransferGiverHandBefore.ToString(CultureInfo.InvariantCulture)
                + "; giverAfter=" + TransferGiverHandAfter.ToString(CultureInfo.InvariantCulture)
                + "; receiverBefore=" + TransferReceiverHandBefore.ToString(CultureInfo.InvariantCulture)
                + "; receiverAfter=" + TransferReceiverHandAfter.ToString(CultureInfo.InvariantCulture)
                + "; tableVersionAfterTransfer=" + TableVersionAfterTransfer.ToString(CultureInfo.InvariantCulture)
                + "; stepsAfterTransfer=" + StepsAfterTransfer.ToString(CultureInfo.InvariantCulture)
                + "; transferLost=" + TransferLostCard.ToString(CultureInfo.InvariantCulture)
                + "; transferGained=" + TransferGainedCard.ToString(CultureInfo.InvariantCulture)
                + "; batchAdmitted=" + BatchAdmitted
                + "; batchCandidates=" + BatchCandidateCount.ToString(CultureInfo.InvariantCulture)
                + "; contestWinner=" + ContestWinnerSeat.ToString(CultureInfo.InvariantCulture)
                + "; contestSequence=" + ContestWinnerSequence.ToString(CultureInfo.InvariantCulture)
                + "; contestHolderHand=" + ContestHolderHandBefore.ToString(CultureInfo.InvariantCulture)
                + "->" + ContestHolderHandAfter.ToString(CultureInfo.InvariantCulture)
                + "; contestWinnerHand=" + ContestWinnerHandBefore.ToString(CultureInfo.InvariantCulture)
                + "->" + ContestWinnerHandAfter.ToString(CultureInfo.InvariantCulture)
                + "; contestLoserHand=" + ContestLoserHandBefore.ToString(CultureInfo.InvariantCulture)
                + "->" + ContestLoserHandAfter.ToString(CultureInfo.InvariantCulture)
                + "; contestVersion=" + ContestTableVersion.ToString(CultureInfo.InvariantCulture)
                + "; contestSteps=" + ContestSteps.ToString(CultureInfo.InvariantCulture)
                + "; spawnedBonus=" + SpawnedSeatBonusValue.ToString(CultureInfo.InvariantCulture)
                + "; spawnedStampEpoch=" + SpawnedSeatStampEpoch.ToString(CultureInfo.InvariantCulture)
                + "; spawnedPublished=" + SpawnedSeatStampPublished
                + "; spawnedInView=" + SpawnedSeatInView
                + "; laneEpochAfterSpawn=" + LaneEpochAfterSpawn.ToString(CultureInfo.InvariantCulture)
                + "; worldEpochAfterSpawn=" + WorldEpochAfterSpawn.ToString(CultureInfo.InvariantCulture)
                + "; joinedAfterSpawn=" + CountersJoinedAfterSpawn
                + "; forwardNoTargetChange=" + ForwardDerivationHadNoTargetChange
                + "; idleFrames=" + IdleFrames.ToString(CultureInfo.InvariantCulture)
                + "; idleSteps=" + IdleStepsCommitted.ToString(CultureInfo.InvariantCulture)
                + "; idleImages=" + IdlePublishedImages.ToString(CultureInfo.InvariantCulture)
                + "; idleDispatchRuns=" + IdleDispatchRuns.ToString(CultureInfo.InvariantCulture)
                + "; pendingDemandAfterIdle=" + PendingDemandAfterIdle.ToString(CultureInfo.InvariantCulture)
                + "; outstandingAfterTeardown=" + OutstandingJobsAfterTeardown.ToString(CultureInfo.InvariantCulture)
                + "; retainedResourcesAfterTeardown=" + RetainedResourcesAfterTeardown.ToString(CultureInfo.InvariantCulture)
                + "; registryAfterTeardown=" + RegistryAfterTeardown.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>True when the future seat was visible in the published view (spawn observation).</summary>
        public bool SpawnedSeatInView { get; set; }

        // ---------------------------------------------------------------- the atomic batch envelope (P-037)
        public bool BatchAdmitted { get; set; }
        public int BatchCandidateCount { get; set; }
        public int ContestWinnerSeat { get; set; }
        public ulong ContestWinnerSequence { get; set; }
        public int ContestHolderHandBefore { get; set; }
        public int ContestHolderHandAfter { get; set; }
        public int ContestWinnerHandBefore { get; set; }
        public int ContestWinnerHandAfter { get; set; }
        public int ContestLoserHandBefore { get; set; }
        public int ContestLoserHandAfter { get; set; }
        public uint ContestTableVersion { get; set; }
        public ulong ContestSteps { get; set; }
    }

    /// <summary>Full result of one scenario run: the named observations plus the facts they were computed from.</summary>
    public sealed class CardScenarioResult
    {
        /// <summary>Builds one result.</summary>
        public CardScenarioResult(IReadOnlyList<CardStep> steps, CardFacts facts)
        {
            Steps = steps;
            Facts = facts;
        }

        /// <summary>The named observations, in execution order.</summary>
        public IReadOnlyList<CardStep> Steps { get; }

        /// <summary>The facts every verdict was computed from.</summary>
        public CardFacts Facts { get; }

        /// <summary>True when every observation passed and there was at least one.</summary>
        public bool AllPassed
        {
            get
            {
                for (int i = 0; i < Steps.Count; i++)
                {
                    if (!Steps[i].Passed)
                    {
                        return false;
                    }
                }

                return Steps.Count > 0;
            }
        }

        /// <summary>One-line digest naming every failed observation.</summary>
        public string Describe()
        {
            var failed = new List<string>();
            for (int i = 0; i < Steps.Count; i++)
            {
                if (!Steps[i].Passed)
                {
                    failed.Add(Steps[i].Name + " (" + Steps[i].Detail + ")");
                }
            }

            return failed.Count == 0
                ? Steps.Count.ToString(CultureInfo.InvariantCulture) + " card checks passed"
                : failed.Count.ToString(CultureInfo.InvariantCulture) + " card check(s) failed: "
                    + string.Join(" | ", failed.ToArray());
        }
    }

    /// <summary>Runs the GC-011 card scenario against real modules only.</summary>
    public static class CardMarketScenario
    {
        /// <summary>Host ticks handed to the time driver; the world captures its origin at its first pump.</summary>
        private const ulong HostTicks = 1_000_000UL;

        /// <summary>Frames the world is pumped while it must stay still (a command-driven world's idle proof).</summary>
        private const int IdleFrames = 8;

        private const ulong ScratchCapacityBytes = 4096UL;

        private const ulong ScratchBytesPerSlot = 64UL;

        private const ulong StagedByteCeiling = 1024UL * 1024UL;

        /// <summary>Runs the scenario over the hand-written generated-style table in this fixture assembly.</summary>
        public static CardScenarioResult RunFixtureCatalog()
        {
            CatalogBuildResult build = CardCatalogTable.Build();
            if (build.Catalog == null)
            {
                throw new InvalidOperationException("the hand-written card catalog was rejected: " + build.Describe());
            }

            return Run(
                build.Catalog,
                CardTableFixture.Declarations(),
                new FactoryKey(new Id128(CardTableKeys.Issuer.High, CardTableKeys.Issuer.Low), 1U),
                CardTableKeys.PluginType("cards.absent-plugin"),
                CardCatalogTable.Fingerprint());
        }

        /// <summary>
        /// Runs the scenario against one catalog. <paramref name="declarations"/> are the generated-style
        /// declarations the mounts resolve; <paramref name="absentFactoryKey"/> and
        /// <paramref name="absentPluginType"/> must be unregistered, so the P-009 miss stays observable; and
        /// <paramref name="declaredFingerprint"/> is the fingerprint the declarations were published with, so a
        /// built catalog that disagrees fails the run (P-028).
        /// </summary>
        public static CardScenarioResult Run(
            ImmutableCatalog catalog,
            IReadOnlyList<CatalogPluginDeclaration> declarations,
            FactoryKey absentFactoryKey,
            PluginTypeId absentPluginType,
            ContentHash declaredFingerprint)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (declarations == null || declarations.Count < 2)
            {
                throw new ArgumentException(
                    "the scenario mounts a table runtime and at least one scoring provider, so it needs both"
                    + " generated-style declarations.",
                    nameof(declarations));
            }

            return new Executor(catalog, declarations, absentFactoryKey, absentPluginType, declaredFingerprint).Run();
        }

        private sealed class Executor
        {
            private readonly ImmutableCatalog catalog;
            private readonly IReadOnlyList<CatalogPluginDeclaration> declarations;
            private readonly FactoryKey absentFactoryKey;
            private readonly PluginTypeId absentPluginType;
            private readonly ContentHash declaredFingerprint;
            private readonly List<CardStep> steps = new List<CardStep>();
            private readonly CardFacts facts = new CardFacts();
            private readonly IdSequence sessionSequence = new IdSequence(0x4341524453455331UL);
            private readonly CardSeatApplier seatApplier = new CardSeatApplier();
            private readonly MarketTableApplier tableApplier = new MarketTableApplier();
            private readonly CardDerivationValueSource values = CardDerivationValueSource.Default();

            private UnityWorldHost? host;
            private CardTableModule? module;
            private CompositionHost? lane;
            private AssemblyPublisher? publisher;
            private TargetRegistry? registry;
            private LiveTargetIndex? targets;
            private LiveTargetSeeder? seeder;
            private DerivedAssemblyPipeline? pipeline;
            private WorldCompositionBridge? bridge;
            private PipelineDescriptorReport? descriptorReport;
            private WorldTimeDriver? time;
            private ulong operationSequence;
            private int installedRows;
            private int registryBeforeCreate;

            /// <summary>Creates one run.</summary>
            public Executor(
                ImmutableCatalog catalog,
                IReadOnlyList<CatalogPluginDeclaration> declarations,
                FactoryKey absentFactoryKey,
                PluginTypeId absentPluginType,
                ContentHash declaredFingerprint)
            {
                this.catalog = catalog;
                this.declarations = declarations;
                this.absentFactoryKey = absentFactoryKey;
                this.absentPluginType = absentPluginType;
                this.declaredFingerprint = declaredFingerprint;
            }

            /// <summary>Runs every observation in order.</summary>
            public CardScenarioResult Run()
            {
                CheckCatalogAndDeclarations();
                CompileOwnershipAndSchedule();
                CreateWorldAndMarket();
                MountProvidersAndPublish();
                ProveAutomaticPropagationToExistingSeats();
                ProveIdleWorldBeforeCommand();
                ExecuteOneCommand();
                ProveDuplicateCommandTransfersOnce();
                ProveRejectedSettlementChangesNothing();
                ProveTransferCommitsBothSides();
                ProveBatchEnvelopeResolvesOneWinner();
                SpawnFutureSeatAndProveInheritance();
                ProveIdleWorldAndTearDown();

                return new CardScenarioResult(steps, facts);
            }

            // ---------------------------------------------------------------- 1. catalog and declarations

            private void CheckCatalogAndDeclarations()
            {
                const string name = "cards-catalog-and-declarations";
                try
                {
                    CatalogLookup factory = catalog.Lookup(declarations[0].Manifest.FactoryKey);
                    CatalogLookup absent = catalog.Lookup(absentFactoryKey);
                    bool fingerprintAsDeclared = catalog.Fingerprint.Equals(declaredFingerprint);
                    facts.CatalogFingerprint = catalog.Fingerprint.ToHex();

                    var manifests = new CatalogManifestSource(catalog, declarations);
                    bool allAccepted = manifests.AcceptedCount == declarations.Count && manifests.Rejected.Count == 0;
                    bool unregistered = manifests.TryGetManifest(absentPluginType, out PluginManifest? resolved);

                    // The registered reducer and predicate are the two derivation keys the catalog holds; the
                    // committed card catalog registers exactly the same two (P-009, 04 s8).
                    bool reducerRegistered = CardCatalogTable.DerivationHolds();

                    bool pass = fingerprintAsDeclared
                        && allAccepted
                        && !unregistered
                        && resolved == null
                        && factory.Found
                        && factory.Factory != null
                        && factory.Factory.Kind == FactoryKind.PluginFactory
                        && reducerRegistered
                        && !absent.Found
                        && absent.Code == DiagnosticCode.MissingDependency;

                    steps.Add(new CardStep(name, pass,
                        "fingerprint=" + catalog.Fingerprint.ToHex()
                        + "; fingerprintAsDeclared=" + fingerprintAsDeclared
                        + "; accepted=" + manifests.AcceptedCount.ToString(CultureInfo.InvariantCulture)
                        + "; rejected=" + manifests.Rejected.Count.ToString(CultureInfo.InvariantCulture)
                        + "; factoryLookup=" + factory.Describe()
                        + "; unknownKey=" + absent.Describe()
                        + "; keyDerivationHolds=" + reducerRegistered
                        + "; unregisteredResolved=" + (resolved != null)));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardStep(name, false, DescribeException(exception)));
                }
            }

            // ---------------------------------------------------------------- 2. GC-007 ownership + GC-009 schedule

            private void CompileOwnershipAndSchedule()
            {
                const string name = "cards-ownership-and-schedule-compiled";
                try
                {
                    PipelineDescriptorReport report = OwnershipSchedulePipeline.Build(
                        CardTableFixture.Manifests(),
                        CardTableRegistration.DispatchKinds(),
                        new MigrationRegistry(new List<ISlotMigration>()));
                    descriptorReport = report;

                    if (!report.Succeeded || report.Descriptor == null || report.Compilation == null || report.Adaptation == null
                        || report.Ownership == null)
                    {
                        steps.Add(new CardStep(name, false, "the pipeline refused: " + report.Describe()));
                        return;
                    }

                    CompiledSchedule schedule = report.Compilation.Schedule!;
                    facts.CompiledStageCount = schedule.StageCount;
                    facts.CompiledSystemCount = schedule.SystemCount;
                    facts.ScheduleHash = schedule.Hash.ToHex();
                    facts.ScheduleEdgeCount = schedule.Edges.Count;
                    facts.SchedulePlaybackPointCount = schedule.PlaybackPoints.Count;

                    bool inputIndex = schedule.TryGetStageIndex(CardTableKeys.InputStage, out int input);
                    bool commitIndex = schedule.TryGetStageIndex(CardTableKeys.CommitStage, out int commit);
                    facts.InputStageIndex = inputIndex ? input : -1;
                    facts.CommitStageIndex = commitIndex ? commit : -1;
                    facts.BufferEdgeValidateBeforeCommit =
                        inputIndex && commitIndex && schedule.HasEdge(input, commit);

                    facts.OwnershipDomainCount = report.Ownership!.Map.DomainCount;
                    facts.WriterCount = report.Descriptor.Stages.Count;
                    facts.ValidatedSlotPolicyCount = report.SlotPolicies.Count;

                    // One owner per declared domain: the table runtime owns every domain its systems write
                    // (P-034), which is what makes one commit stage settle several entities.
                    bool singleOwner = true;
                    for (int i = 0; i < report.Descriptor.Slots.Count; i++)
                    {
                        singleOwner &= report.Descriptor.Slots[i].Owner.Equals(CardTableKeys.TableOwner);
                    }

                    facts.SingleOwnerPerDomain = singleOwner;

                    bool allSlotsValidated = report.SlotPolicies.Count >= 5;
                    for (int i = 0; i < report.SlotPolicies.Count; i++)
                    {
                        allSlotsValidated &= report.SlotPolicies[i].Succeeded;
                    }

                    bool pass = report.Compilation.Succeeded
                        && report.Adaptation.Succeeded
                        && report.Descriptor.TryValidate(out DiagnosticCode _, out string _)
                        && report.Ownership.IsValid
                        && schedule.StageCount == 4
                        && schedule.SystemCount == 4
                        && schedule.PlaybackPoints.Count == 1
                        && facts.BufferEdgeValidateBeforeCommit
                        && singleOwner
                        && allSlotsValidated
                        && !schedule.Hash.IsEmpty;

                    steps.Add(new CardStep(name, pass,
                        "compilation=" + report.Compilation.Explain()
                        + "; adaptation=" + report.Adaptation.Explain()
                        + "; descriptor=" + report.Describe()
                        + "; ownershipValid=" + report.Ownership.IsValid
                        + "; domains=" + facts.OwnershipDomainCount.ToString(CultureInfo.InvariantCulture)
                        + "; singleOwnerPerDomain=" + singleOwner
                        + "; schedule=" + schedule.Describe()));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardStep(name, false, DescribeException(exception)));
                }
            }

            // ---------------------------------------------------------------- 3. the world, the market, its targets

            private void CreateWorldAndMarket()
            {
                const string name = "cards-world-and-market-seeded";
                try
                {
                    if (descriptorReport == null || descriptorReport.Descriptor == null || descriptorReport.Adaptation == null)
                    {
                        steps.Add(new CardStep(name, false, "the descriptor was not built"));
                        return;
                    }

                    registryBeforeCreate = UnityWorldRegistry.Count;
                    WorldId world = NextSession();
                    facts.WorldSession = world.Session.ToString();

                    WorldCreateRequest request = CardTableRegistration.CommandDrivenRequest(
                        world,
                        NextOperation(world),
                        ContentHash.Empty);
                    UnityWorldRegistration registration = CardTableRegistration.Create(
                        descriptorReport.Adaptation!,
                        CardTableRegistration.Systems());

                    bool created = UnityWorldRegistry.TryCreate(
                        request,
                        registration,
                        out UnityWorldHost? createdHost,
                        out WorldCreateResult result);
                    host = createdHost;
                    if (!created || host == null)
                    {
                        steps.Add(new CardStep(name, false, "world creation failed: " + result.Code + ": " + result.Detail));
                        return;
                    }

                    module = CardTableModule.Attach(host);

                    registry = new TargetRegistry(world, 16);
                    SpawnRecipeCatalog recipes = CardTableRecipes.Catalog(seatApplier, tableApplier);
                    publisher = new AssemblyPublisher(
                        host,
                        registry,
                        recipes,
                        new MigrationRegistry(new List<ISlotMigration>()),
                        descriptorReport.Descriptor);
                    targets = new LiveTargetIndex(publisher.Recipes);
                    seeder = new LiveTargetSeeder(host, registry, targets);

                    bool seeded = CardTableFixture.SeedMarket(seeder, module, out DiagnosticCode seedCode, out string seedDetail);
                    if (!seeded)
                    {
                        steps.Add(new CardStep(name, false, "seeding failed: " + seedCode + ": " + seedDetail));
                        return;
                    }

                    lane = CompositionHost.CreateDefault(
                        world,
                        CardMarketComposition.MatchScope,
                        new CatalogManifestSource(catalog, declarations),
                        null,
                        CompositionLaneSeed.InitialAssembly);
                    bridge = new WorldCompositionBridge(host, lane, publisher);

                    pipeline = new DerivedAssemblyPipeline(
                        host,
                        lane,
                        publisher,
                        targets,
                        seeder,
                        values,
                        null,
                        null,
                        publisher.Migrations,
                        new StagedResourceGate(StagedByteCeiling, CardTableKeys.Issuer),
                        new PlanBudget(1024UL * 1024UL, 1024UL * 1024UL, ScratchCapacityBytes, ScratchBytesPerSlot));

                    time = new WorldTimeDriver(host, new StepInputCutoff(8, 16), new PluginClockRegistry(8), 1U);
                    time.AdoptResourceTable(descriptorReport.Adaptation.NativeTable!);

                    facts.LiveTargetCount = targets.Count;
                    facts.SeatCount = module.SeatCount;

                    // The market's scope tree is created top-down and the table runtime is mounted at the match
                    // root; both are ordinary control-lane edits (O-02, O-03).
                    bool scopesCreated = PublishEdits(CardMarketComposition.ScopeCreates());
                    bool tableMounted = PublishEdits(new List<CompositionEditPayload>
                    {
                        CardTablePayloads.Mount(
                            CardTableFixture.TableRuntimeDeclaration().Manifest,
                            CardTableFixture.TableRuntimeInstance,
                            CardMarketComposition.MatchScope),
                        CardTablePayloads.Mount(
                            CardTableFixture.RuleLibraryDeclaration().Manifest,
                            CardTableFixture.RuleLibraryInstance,
                            CardMarketComposition.MatchScope),
                    });

                    facts.LaneEpochAfterSetup = lane.Committed.Epoch.Value;
                    facts.WorldEpochAfterSetup = host.CurrentEpoch.Value;
                    facts.CountersJoinedAfterSetup = AssemblyPublisher.MatchesPublishedAssembly(
                        lane.Committed.Revision,
                        lane.Committed.Epoch,
                        publisher.PublishedRevision,
                        host.CurrentEpoch);

                    bool pass = scopesCreated
                        && tableMounted
                        && facts.LiveTargetCount == 6
                        && facts.SeatCount == 4
                        && lane.Committed.Mode == PropagationMode.Automatic
                        && host.Lifecycle == WorldLifecycleState.Running
                        && host.CurrentStep.Equals(LogicalStepId.Zero);

                    steps.Add(new CardStep(name, pass,
                        "session=" + world.Session.ToString()
                        + "; registryBefore=" + registryBeforeCreate.ToString(CultureInfo.InvariantCulture)
                        + "; liveTargets=" + facts.LiveTargetCount.ToString(CultureInfo.InvariantCulture)
                        + "; seats=" + facts.SeatCount.ToString(CultureInfo.InvariantCulture)
                        + "; lane=" + lane.Committed.Revision.Value.ToString(CultureInfo.InvariantCulture)
                        + "/" + lane.Committed.Epoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; worldEpoch=" + host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; mode=" + lane.Committed.Mode
                        + "; scopesCreated=" + scopesCreated
                        + "; tableMounted=" + tableMounted));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardStep(name, false, DescribeException(exception)));
                }
            }

            // ---------------------------------------------------------------- 4. the mount reaches existing seats

            private void MountProvidersAndPublish()
            {
                const string name = "cards-mount-reaches-existing-seats";
                try
                {
                    if (lane == null || host == null || pipeline == null || publisher == null)
                    {
                        steps.Add(new CardStep(name, false, "the world or its pipeline is missing"));
                        return;
                    }

                    // 07 s2.1: the festival provider is mounted at League A and the quiet provider at League B, and
                    // in Automatic mode both reach every eligible descendant with no per-instance import (P-013).
                    // Two separate publications, one per provider, so each is a real composition revision whose
                    // derivation installs its own rows and the lane/world counters stay joined at every boundary.
                    bool mounted = PublishEdits(CardTableFixture.MarketMounts(true, true));
                    // What the two mount publications really installed, read from their own reports: one row per
                    // compatible seat, and none for the ineligible or isolated targets.
                    facts.MountInstalledRows = installedRows;
                    facts.ReducerRegistrations = values.ReductionCount;
                    facts.PredicateEvaluations = values.EvaluationCount;
                    facts.LaneEpochAfterSetup = lane.Committed.Epoch.Value;
                    facts.WorldEpochAfterSetup = host.CurrentEpoch.Value;
                    facts.CountersJoinedAfterSetup = AssemblyPublisher.MatchesPublishedAssembly(
                        lane.Committed.Revision,
                        lane.Committed.Epoch,
                        publisher.PublishedRevision,
                        host.CurrentEpoch);

                    facts.SeatABonusRowCount = publisher.ReadBindingRows(CardTableFixture.SeatTarget(CardTableKeys.SeatAOrdinal)).Count;
                    facts.SeatBBonusValue = publisher.ReadBindingRows(CardTableFixture.SeatTarget(CardTableKeys.SeatBOrdinal)).Count > 0
                        ? publisher.ReadBindingRows(CardTableFixture.SeatTarget(CardTableKeys.SeatBOrdinal))[0].Value
                        : int.MinValue;
                    facts.SeatCBonusValue = publisher.ReadBindingRows(CardTableFixture.SeatTarget(CardTableKeys.SeatCOrdinal)).Count > 0
                        ? publisher.ReadBindingRows(CardTableFixture.SeatTarget(CardTableKeys.SeatCOrdinal))[0].Value
                        : int.MinValue;
                    facts.ScoreboardRowCount = publisher.ReadBindingRows(CardIdentity.Target(CardVocabulary.Scoreboard)).Count;
                    facts.PracticeSeatBonusRowCount =
                        publisher.ReadBindingRows(CardIdentity.Target(CardVocabulary.PracticeSeat)).Count;

                    if (facts.SeatABonusRowCount == 1)
                    {
                        CapabilityBinding row = publisher.ReadBindingRows(CardTableFixture.SeatTarget(CardTableKeys.SeatAOrdinal))[0];
                        facts.SeatABonusValue = row.Value;
                        facts.SeatABonusIsActive = row.IsActive;
                    }

                    // 07 s2.1: the festival contributes +2, the quiet provider +1, the ineligible scoreboard and
                    // the eligible-but-isolated practice seat receive nothing at all, and mounting awards no points.
                    bool pass = mounted
                        && facts.CountersJoinedAfterSetup
                        && facts.SeatABonusRowCount == 1
                        && facts.SeatABonusValue == CardVocabulary.FestivalBonus
                        && facts.SeatABonusIsActive
                        && facts.SeatBBonusValue == CardVocabulary.FestivalBonus
                        && facts.SeatCBonusValue == CardVocabulary.QuietBonus
                        && facts.ScoreboardRowCount == 0
                        && facts.PracticeSeatBonusRowCount == 0
                        && facts.MountInstalledRows == CardTableKeys.MountInstalledRowCount
                        && values.ReductionCount >= 3
                        && SeatScoresUntouched();

                    steps.Add(new CardStep(name, pass,
                        "mounted=" + mounted
                        + "; installedRows=" + facts.MountInstalledRows.ToString(CultureInfo.InvariantCulture)
                        + "; seatARows=" + facts.SeatABonusRowCount.ToString(CultureInfo.InvariantCulture)
                        + "; seatAValue=" + facts.SeatABonusValue.ToString(CultureInfo.InvariantCulture)
                        + "; seatBValue=" + facts.SeatBBonusValue.ToString(CultureInfo.InvariantCulture)
                        + "; seatCValue=" + facts.SeatCBonusValue.ToString(CultureInfo.InvariantCulture)
                        + "; scoreboardRows=" + facts.ScoreboardRowCount.ToString(CultureInfo.InvariantCulture)
                        + "; practiceRows=" + facts.PracticeSeatBonusRowCount.ToString(CultureInfo.InvariantCulture)
                        + "; reductions=" + values.ReductionCount.ToString(CultureInfo.InvariantCulture)
                        + "; predicateEvals=" + values.EvaluationCount.ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardStep(name, false, DescribeException(exception)));
                }
            }

            private bool SeatScoresUntouched()
            {
                if (module == null)
                {
                    return false;
                }

                for (int i = 0; i < module.SeatCount; i++)
                {
                    if (!module.TrySeat((uint)i, out Entity seat))
                    {
                        continue;
                    }

                    if (module.Host.EntityWorld.EntityManager.GetComponentData<CardSeatState>(seat).Score
                        != CardTableKeys.SeededSeatScore)
                    {
                        return false;
                    }
                }

                return true;
            }

            // ---------------------------------------------------------------- 5. idle before any command

            private void ProveIdleWorldBeforeCommand()
            {
                const string name = "cards-idle-before-command-commits-zero-steps";
                try
                {
                    if (host == null || time == null)
                    {
                        steps.Add(new CardStep(name, false, "no time driver"));
                        return;
                    }

                    ulong committed = 0UL;
                    for (int i = 0; i < IdleFrames; i++)
                    {
                        committed += time.PumpFrame(HostTicks).StepsCommitted;
                    }

                    bool pass = committed == 0UL
                        && host.CurrentStep.Equals(LogicalStepId.Zero)
                        && host.PendingDemand == 0UL
                        && time.Clocks.PendingWakeCount == 0;

                    steps.Add(new CardStep(name, pass,
                        "frames=" + IdleFrames.ToString(CultureInfo.InvariantCulture)
                        + "; steps=" + committed.ToString(CultureInfo.InvariantCulture)
                        + "; demand=" + host.PendingDemand.ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardStep(name, false, DescribeException(exception)));
                }
            }

            // ---------------------------------------------------------------- 6. one command, one step

            private void ExecuteOneCommand()
            {
                const string name = "cards-one-command-commits-both-sides";
                try
                {
                    if (host == null || time == null || module == null || lane == null || bridge == null)
                    {
                        steps.Add(new CardStep(name, false, "the world or its pipeline is missing"));
                        return;
                    }

                    WorldMessagePlane? plane = host.Messages;
                    if (plane == null)
                    {
                        steps.Add(new CardStep(name, false, "the world has no message plane"));
                        return;
                    }

                    facts.SeatAHandBefore = HandCount(CardTableKeys.SeatAOrdinal);
                    facts.SeatAScoreBefore = SeatScore(CardTableKeys.SeatAOrdinal);

                    // 07 s2.3's example command: seat A plays its three held cards against table version 1.
                    var command = new CardCommandPayload(
                        CardCommandKind.SubmitSet,
                        CardTableKeys.SeatAOrdinal,
                        CardTableKeys.SeatAOrdinal,
                        CardTableKeys.SeededTableVersion,
                        CardTableKeys.SeatCard(CardTableKeys.SeatAOrdinal, 0),
                        CardTableKeys.SeatCard(CardTableKeys.SeatAOrdinal, 1),
                        CardTableKeys.SeatCard(CardTableKeys.SeatAOrdinal, 2));

                    CommandAdmissionReceipt receipt = SubmitCommand(command, null);
                    facts.CommandAdmitted = receipt.Admitted;
                    facts.CommandAdmissionKind = receipt.Result.Kind.ToString();

                    TimeFrameReport frame = time.PumpFrame(HostTicks);
                    facts.StepsAfterCommand = host.CurrentStep.Value;
                    facts.SeatAHandAfterCommit = HandCount(CardTableKeys.SeatAOrdinal);
                    facts.SeatAScoreAfterCommit = SeatScore(CardTableKeys.SeatAOrdinal);

                    CardTableState table = CardTableAccess.ReadTable(host.EntityWorld.EntityManager, module.TableEntity);
                    facts.TableVersionAfterCommit = table.TableVersion;
                    facts.TurnNumberAfterCommit = table.TurnNumber;
                    facts.CommittedWrites = module.AppliedWriteCount;

                    CommittedEventPage page = plane.ReadEvents(new EventCursor(host.World, EventSequence.Zero), 8);
                    facts.CommittedEventCount = page.Events.Count;
                    if (page.Events.Count > 0)
                    {
                        CommittedEvent committed = page.Events[0];
                        facts.CommittedEventStep = committed.Step.Value;
                        if (CardResultCodec.TryRead(committed.Payload.Bytes, out CardResultPayload decoded))
                        {
                            facts.CommittedEventScoreDelta = decoded.ScoreDelta;
                            facts.CommittedEventScoreAfter = decoded.ScoreAfter;
                            facts.CommittedEventCardCount = decoded.CardCount;
                            // P-044/P-045: the record the owner published names the value live storage holds.
                            facts.CommittedEventMatchesLiveState = decoded.ScoreAfter == facts.SeatAScoreAfterCommit
                                && decoded.TableVersion == facts.TableVersionAfterCommit;
                        }
                    }

                    bridge.SyncStepFromWorld();

                    // The base score is 10 and the festival bonus is +2, so 07 s2.3's example resolves to
                    // 4 + 12 = 16 for seat A, with exactly its three cards removed and the table advanced.
                    int expectedScore = CardTableKeys.ScoreAfterOneSet(CardVocabulary.FestivalBonus);
                    bool pass = receipt.Admitted
                        && frame.StepsCommitted == 1UL
                        && facts.StepsAfterCommand == 1UL
                        && facts.SeatAHandAfterCommit == facts.SeatAHandBefore - CardSetRules.SetCardCount
                        && facts.SeatAScoreAfterCommit == expectedScore
                        && facts.TableVersionAfterCommit == CardTableKeys.TableVersionAfterOneCommit
                        && facts.TurnNumberAfterCommit == 1U
                        && facts.CommittedEventCount == 1
                        && facts.CommittedEventScoreDelta == CardSetRules.SetScoreDelta(CardVocabulary.FestivalBonus)
                        && facts.CommittedEventScoreAfter == expectedScore
                        && facts.CommittedEventCardCount == CardSetRules.SetCardCount
                        && facts.CommittedEventStep == 1UL
                        && facts.CommittedEventMatchesLiveState
                        && facts.CommittedWrites == CardSetRules.SetCardCount + 2
                        && module.CommittedSettlementCount == 1
                        && host.PendingDemand == 0UL
                        && !host.Driver.IsFaulted;

                    steps.Add(new CardStep(name, pass,
                        "admitted=" + receipt.Admitted
                        + "; admission=" + facts.CommandAdmissionKind
                        + "; steps=" + facts.StepsAfterCommand.ToString(CultureInfo.InvariantCulture)
                        + "; hand=" + facts.SeatAHandBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + facts.SeatAHandAfterCommit.ToString(CultureInfo.InvariantCulture)
                        + "; score=" + facts.SeatAScoreBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + facts.SeatAScoreAfterCommit.ToString(CultureInfo.InvariantCulture)
                        + "; expectedScore=" + expectedScore.ToString(CultureInfo.InvariantCulture)
                        + "; tableVersion=" + facts.TableVersionAfterCommit.ToString(CultureInfo.InvariantCulture)
                        + "; events=" + facts.CommittedEventCount.ToString(CultureInfo.InvariantCulture)
                        + "; eventDelta=" + facts.CommittedEventScoreDelta.ToString(CultureInfo.InvariantCulture)
                        + "; writes=" + facts.CommittedWrites.ToString(CultureInfo.InvariantCulture)
                        + "; faulted=" + host.Driver.IsFaulted));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardStep(name, false, DescribeException(exception)));
                }
            }

            // ---------------------------------------------------------------- 7. a duplicate transfers once

            private void ProveDuplicateCommandTransfersOnce()
            {
                const string name = "cards-duplicate-command-transfers-once";
                try
                {
                    if (host == null || time == null || module == null)
                    {
                        steps.Add(new CardStep(name, false, "no world"));
                        return;
                    }

                    WorldMessagePlane plane = host.Messages!;
                    int rejectionsBefore = plane.Requests.RejectedCount + plane.Requests.DuplicateCount;

                    var command = new CardCommandPayload(
                        CardCommandKind.Transfer,
                        CardTableKeys.SeatBOrdinal,
                        CardTableKeys.SeatAOrdinal,
                        CardTableKeys.TableVersionAfterOneCommit,
                        CardTableKeys.SeatCard(CardTableKeys.SeatBOrdinal, 3),
                        default(CardId),
                        default(CardId));

                    int receiverBefore = HandCount(CardTableKeys.SeatAOrdinal);
                    ulong stepBefore = host.CurrentStep.Value;

                    // The same request key twice: the first is admitted and executes, the second returns the
                    // recorded result and executes nothing (P-037, P-050).
                    OperationId operation = NextOperation(host.World);
                    CommandAdmissionReceipt first = SubmitCommand(command, operation);
                    CommandAdmissionReceipt second = SubmitCommand(command, operation);

                    facts.DuplicateAdmitted = second.Admitted;
                    facts.DuplicateAdmissionKind = second.Result.Kind.ToString();

                    time.PumpFrame(HostTicks);
                    facts.StepsAfterDuplicate = host.CurrentStep.Value;
                    facts.SeatBHandAfterDuplicate = HandCount(CardTableKeys.SeatBOrdinal);
                    facts.SeatBScoreAfterDuplicate = SeatScore(CardTableKeys.SeatBOrdinal);
                    facts.PendingDemandAfterDuplicate = host.PendingDemand;
                    facts.DuplicateRejections = (plane.Requests.RejectedCount + plane.Requests.DuplicateCount) - rejectionsBefore;

                    int receiverAfter = HandCount(CardTableKeys.SeatAOrdinal);

                    bool pass = first.Admitted
                        && facts.StepsAfterDuplicate == stepBefore + 1UL
                        && plane.Requests.DuplicateCount >= 1
                        && receiverAfter == receiverBefore + 1
                        && facts.SeatBHandAfterDuplicate == CardTableKeys.SeededHandCount - 1
                        && facts.PendingDemandAfterDuplicate == 0UL
                        && module.CommittedSettlementCount == 2;

                    steps.Add(new CardStep(name, pass,
                        "first=" + first.Result.Kind
                        + "; second=" + second.Result.Kind
                        + "; duplicateCount=" + plane.Requests.DuplicateCount.ToString(CultureInfo.InvariantCulture)
                        + "; steps=" + stepBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + facts.StepsAfterDuplicate.ToString(CultureInfo.InvariantCulture)
                        + "; receiver=" + receiverBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + receiverAfter.ToString(CultureInfo.InvariantCulture)
                        + "; giverHand=" + facts.SeatBHandAfterDuplicate.ToString(CultureInfo.InvariantCulture)
                        + "; demand=" + facts.PendingDemandAfterDuplicate.ToString(CultureInfo.InvariantCulture)
                        + "; settlements=" + module.CommittedSettlementCount.ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardStep(name, false, DescribeException(exception)));
                }
            }

            // ---------------------------------------------------------------- 8. a rejection writes nothing

            private void ProveRejectedSettlementChangesNothing()
            {
                const string name = "cards-rejected-settlement-changes-nothing";
                try
                {
                    if (host == null || time == null || module == null)
                    {
                        steps.Add(new CardStep(name, false, "no world"));
                        return;
                    }

                    WorldMessagePlane plane = host.Messages!;
                    CardTableState tableBefore = CardTableAccess.ReadTable(host.EntityWorld.EntityManager, module.TableEntity);
                    int handABefore = HandCount(CardTableKeys.SeatAOrdinal);
                    int handBBefore = HandCount(CardTableKeys.SeatBOrdinal);
                    int scoreABefore = SeatScore(CardTableKeys.SeatAOrdinal);
                    ulong stepBefore = host.CurrentStep.Value;

                    // Seat C does not hold seat A's cards and it is a different hand's turn: the settlement must
                    // reject, and a rejection may not move a card, a score or the table version (07 s2.3).
                    var command = new CardCommandPayload(
                        CardCommandKind.SubmitSet,
                        CardTableKeys.SeatCOrdinal,
                        CardTableKeys.SeatCOrdinal,
                        tableBefore.TableVersion,
                        CardTableKeys.SeatCard(CardTableKeys.SeatAOrdinal, 3),
                        CardTableKeys.SeatCard(CardTableKeys.SeatAOrdinal, 4),
                        CardTableKeys.SeatCard(CardTableKeys.SeatAOrdinal, 5));

                    CommandAdmissionReceipt receipt = SubmitCommand(command, null);
                    facts.RejectedCommandAdmitted = receipt.Admitted;
                    facts.RejectedRequestKind = receipt.Result.Kind.ToString();

                    time.PumpFrame(HostTicks);
                    facts.StepsAfterRejection = host.CurrentStep.Value;
                    facts.SeatAHandAfterRejection = HandCount(CardTableKeys.SeatAOrdinal);
                    facts.SeatBHandAfterRejection = HandCount(CardTableKeys.SeatBOrdinal);
                    facts.SeatAScoreAfterRejection = SeatScore(CardTableKeys.SeatAOrdinal);
                    CardTableState tableAfter = CardTableAccess.ReadTable(host.EntityWorld.EntityManager, module.TableEntity);
                    facts.TableVersionAfterRejection = tableAfter.TableVersion;
                    facts.RejectedDecisionCount = module.RejectedDecisionCount;
                    facts.StaleCommitRejections = module.StaleCommitRejectionCount;

                    // Admission acceptance is not gameplay success (P-042): the command is admitted, the step
                    // commits, and the recorded result is a rejection with the state untouched.
                    bool pass = receipt.Admitted
                        && facts.StepsAfterRejection == stepBefore + 1UL
                        && plane.Requests.RejectedCount >= 1
                        && facts.SeatAHandAfterRejection == handABefore
                        && facts.SeatBHandAfterRejection == handBBefore
                        && facts.SeatAScoreAfterRejection == scoreABefore
                        && facts.TableVersionAfterRejection == tableBefore.TableVersion
                        && facts.RejectedDecisionCount >= 1
                        && !host.Driver.IsFaulted;

                    steps.Add(new CardStep(name, pass,
                        "admitted=" + receipt.Admitted
                        + "; result=" + facts.RejectedRequestKind
                        + "; steps=" + stepBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + facts.StepsAfterRejection.ToString(CultureInfo.InvariantCulture)
                        + "; handA=" + handABefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + facts.SeatAHandAfterRejection.ToString(CultureInfo.InvariantCulture)
                        + "; handB=" + handBBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + facts.SeatBHandAfterRejection.ToString(CultureInfo.InvariantCulture)
                        + "; scoreA=" + scoreABefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + facts.SeatAScoreAfterRejection.ToString(CultureInfo.InvariantCulture)
                        + "; version=" + tableBefore.TableVersion.ToString(CultureInfo.InvariantCulture)
                        + "->" + facts.TableVersionAfterRejection.ToString(CultureInfo.InvariantCulture)
                        + "; rejectedDecisions=" + facts.RejectedDecisionCount.ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardStep(name, false, DescribeException(exception)));
                }
            }

            // ---------------------------------------------------------------- 9. a transfer commits both sides

            private void ProveTransferCommitsBothSides()
            {
                const string name = "cards-transfer-commits-both-sides";
                try
                {
                    if (host == null || time == null || module == null)
                    {
                        steps.Add(new CardStep(name, false, "no world"));
                        return;
                    }

                    WorldMessagePlane plane = host.Messages!;
                    CardTableState table = CardTableAccess.ReadTable(host.EntityWorld.EntityManager, module.TableEntity);

                    facts.TransferGiverHandBefore = HandCount(CardTableKeys.SeatCOrdinal);
                    facts.TransferReceiverHandBefore = HandCount(CardTableKeys.SeatBOrdinal);
                    CardId moved = CardTableKeys.SeatCard(CardTableKeys.SeatCOrdinal, 0);

                    var command = new CardCommandPayload(
                        CardCommandKind.Transfer,
                        CardTableKeys.SeatCOrdinal,
                        CardTableKeys.SeatBOrdinal,
                        table.TableVersion,
                        moved,
                        default(CardId),
                        default(CardId));

                    ulong stepBefore = host.CurrentStep.Value;
                    CommandAdmissionReceipt receipt = SubmitCommand(command, null);
                    time.PumpFrame(HostTicks);

                    facts.TransferGiverHandAfter = HandCount(CardTableKeys.SeatCOrdinal);
                    facts.TransferReceiverHandAfter = HandCount(CardTableKeys.SeatBOrdinal);
                    CardTableState after = CardTableAccess.ReadTable(host.EntityWorld.EntityManager, module.TableEntity);
                    facts.TableVersionAfterTransfer = after.TableVersion;
                    facts.StepsAfterTransfer = host.CurrentStep.Value;

                    if (module.TrySeat(CardTableKeys.SeatBOrdinal, out Entity receiver))
                    {
                        IReadOnlyList<CardId> gained = CardTableAccess.ReadCards(host.EntityWorld.EntityManager, receiver);
                        for (int i = 0; i < gained.Count; i++)
                        {
                            if (gained[i].Equals(moved))
                            {
                                facts.TransferGainedCard++;
                            }
                        }
                    }

                    if (module.TrySeat(CardTableKeys.SeatCOrdinal, out Entity giver))
                    {
                        IReadOnlyList<CardId> lost = CardTableAccess.ReadCards(host.EntityWorld.EntityManager, giver);
                        for (int i = 0; i < lost.Count; i++)
                        {
                            if (lost[i].Equals(moved))
                            {
                                facts.TransferLostCard++;
                            }
                        }
                    }

                    // Both sides move in one step, or neither does: the receiver gained the exact card the giver
                    // lost, and the table advanced once (P-044).
                    bool pass = receipt.Admitted
                        && facts.StepsAfterTransfer == stepBefore + 1UL
                        && facts.TransferGiverHandAfter == facts.TransferGiverHandBefore - 1
                        && facts.TransferReceiverHandAfter == facts.TransferReceiverHandBefore + 1
                        && facts.TransferGainedCard == 1
                        && facts.TransferLostCard == 0
                        && facts.TableVersionAfterTransfer == table.TableVersion + 1U
                        && !host.Driver.IsFaulted;

                    steps.Add(new CardStep(name, pass,
                        "result=" + receipt.Result.Kind
                        + "; giver=" + facts.TransferGiverHandBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + facts.TransferGiverHandAfter.ToString(CultureInfo.InvariantCulture)
                        + "; receiver=" + facts.TransferReceiverHandBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + facts.TransferReceiverHandAfter.ToString(CultureInfo.InvariantCulture)
                        + "; gained=" + facts.TransferGainedCard.ToString(CultureInfo.InvariantCulture)
                        + "; stillHeld=" + facts.TransferLostCard.ToString(CultureInfo.InvariantCulture)
                        + "; version=" + table.TableVersion.ToString(CultureInfo.InvariantCulture)
                        + "->" + facts.TableVersionAfterTransfer.ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardStep(name, false, DescribeException(exception)));
                }
            }

            // ---------------------------------------------------------------- 9b. the atomic batch envelope

            /// <summary>
            /// 07 s2.3's simultaneous contest, submitted as one atomic batch envelope (P-037: "a domain can declare
            /// an atomic batch envelope as one command"). Two seats bid for one card the holder has; the resolver
            /// picks the smallest (seat, sequence), so at most one bid can consume the card and the loser's hand is
            /// untouched. The envelope travels its own declared route and is decoded by the reader bound to its
            /// schema, which is what makes the `CardContest` path real instead of declared.
            /// </summary>
            private void ProveBatchEnvelopeResolvesOneWinner()
            {
                const string name = "cards-batch-envelope-resolves-one-winner";
                try
                {
                    if (host == null || time == null || module == null)
                    {
                        steps.Add(new CardStep(name, false, "no world"));
                        return;
                    }

                    WorldMessagePlane plane = host.Messages!;
                    CardTableState table = CardTableAccess.ReadTable(host.EntityWorld.EntityManager, module.TableEntity);

                    // Seat A holds one remaining card from its own set plus the one it received; seat B and seat C
                    // both bid for it. Seat C bids the lower sequence, so it wins regardless of the slot order.
                    CardId contested = CardTableKeys.SeatCard(CardTableKeys.SeatAOrdinal, CardSetRules.SetCardCount);
                    var envelope = new CardBatchPayload(
                        0x4341524453455431UL,
                        contested,
                        CardTableKeys.SeatAOrdinal,
                        table.TableVersion,
                        2,
                        CardTableKeys.SeatBOrdinal,
                        7UL,
                        CardTableKeys.SeatCOrdinal,
                        3UL,
                        0U,
                        0UL,
                        0U,
                        0UL);

                    facts.BatchCandidateCount = envelope.CandidateCount;
                    facts.ContestHolderHandBefore = HandCount(CardTableKeys.SeatAOrdinal);
                    facts.ContestWinnerHandBefore = HandCount(CardTableKeys.SeatCOrdinal);
                    facts.ContestLoserHandBefore = HandCount(CardTableKeys.SeatBOrdinal);
                    ulong stepBefore = host.CurrentStep.Value;

                    OperationId operation = NextOperation(host.World);
                    var command = new CommandEnvelope(
                        operation,
                        CardTableKeys.BatchRoute,
                        CardIdentity.Target(CardVocabulary.TableOne),
                        CardTableKeys.BatchSchema,
                        null,
                        CardPayloadCodec.WriteBatch(envelope));
                    CommandAdmissionReceipt receipt = host.Submit(command);
                    facts.BatchAdmitted = receipt.Admitted;

                    time.PumpFrame(HostTicks);

                    facts.ContestSteps = host.CurrentStep.Value;
                    facts.ContestHolderHandAfter = HandCount(CardTableKeys.SeatAOrdinal);
                    facts.ContestWinnerHandAfter = HandCount(CardTableKeys.SeatCOrdinal);
                    facts.ContestLoserHandAfter = HandCount(CardTableKeys.SeatBOrdinal);
                    CardTableState after = CardTableAccess.ReadTable(host.EntityWorld.EntityManager, module.TableEntity);
                    facts.ContestTableVersion = after.TableVersion;

                    facts.ContestWinnerSeat = module.LastContestWinnerSeat;
                    facts.ContestWinnerSequence = module.LastContestWinnerSequence;

                    // Exactly one candidate consumed the card: the holder lost it, the lower-sequence bidder
                    // gained it, the other bidder's hand is unchanged, and the table advanced exactly once.
                    bool pass = receipt.Admitted
                        && facts.BatchCandidateCount == 2
                        && facts.ContestSteps == stepBefore + 1UL
                        && facts.ContestWinnerSeat == (int)CardTableKeys.SeatCOrdinal
                        && facts.ContestWinnerSequence == 3UL
                        && facts.ContestHolderHandAfter == facts.ContestHolderHandBefore - 1
                        && facts.ContestWinnerHandAfter == facts.ContestWinnerHandBefore + 1
                        && facts.ContestLoserHandAfter == facts.ContestLoserHandBefore
                        && facts.ContestTableVersion == table.TableVersion + 1U
                        && module.DecodedBatchCount == 1
                        && !host.Driver.IsFaulted;

                    steps.Add(new CardStep(name, pass,
                        "admitted=" + receipt.Admitted
                        + "; result=" + receipt.Result.Kind
                        + "; candidates=" + facts.BatchCandidateCount.ToString(CultureInfo.InvariantCulture)
                        + "; winner=" + facts.ContestWinnerSeat.ToString(CultureInfo.InvariantCulture)
                        + "@" + facts.ContestWinnerSequence.ToString(CultureInfo.InvariantCulture)
                        + "; holder=" + facts.ContestHolderHandBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + facts.ContestHolderHandAfter.ToString(CultureInfo.InvariantCulture)
                        + "; winnerHand=" + facts.ContestWinnerHandBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + facts.ContestWinnerHandAfter.ToString(CultureInfo.InvariantCulture)
                        + "; loserHand=" + facts.ContestLoserHandBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + facts.ContestLoserHandAfter.ToString(CultureInfo.InvariantCulture)
                        + "; version=" + table.TableVersion.ToString(CultureInfo.InvariantCulture)
                        + "->" + facts.ContestTableVersion.ToString(CultureInfo.InvariantCulture)
                        + "; batches=" + module.DecodedBatchCount.ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardStep(name, false, DescribeException(exception)));
                }
            }

            // ---------------------------------------------------------------- 10. the future seat inherits the modifier

            private void SpawnFutureSeatAndProveInheritance()
            {
                const string name = "cards-future-seat-inherits-modifier";
                try
                {
                    if (host == null || lane == null || pipeline == null || publisher == null || targets == null)
                    {
                        steps.Add(new CardStep(name, false, "the world or its pipeline is missing"));
                        return;
                    }

                    // The rule library is unmounted first, so this composition publication has no derivable target
                    // change and the world's assembly for it is the spawn (P-006, P-024).
                    // Unmounting the rule library is a real composition publication whose derivation changes no
                    // target assembly, so the world's assembly for it is the spawn: the lane's publication number
                    // is the epoch the spawn publishes at, and both counters end on the same value (P-006, P-024).
                    OperationId forwardOperation = NextOperation(host.World);
                    bool unmounted = ApplyEditAndReport(
                        CardTablePayloads.Unmount(CardTableFixture.RuleLibraryInstance),
                        forwardOperation,
                        out DerivedAssemblyReport forward);
                    facts.ForwardDerivationHadNoTargetChange = forward.Outcome == DerivedAssemblyOutcome.NoTargetChange;
                    facts.LaneEpochAfterSpawn = lane.Committed.Epoch.Value;

                    TargetId futureSeat = CardTableFixture.SeatTarget((uint)(CardTableKeys.SeatCOrdinal + 1U));
                    seatApplier.NextOrdinal = CardTableKeys.SeatCOrdinal + 1U;
                    seatApplier.InitialScore = CardTableKeys.SeededSeatScore;

                    DerivedAssemblyReport spawn = pipeline.PublishSpawn(
                        NextOperation(host.World),
                        futureSeat,
                        CardTableKeys.SeatRecipe,
                        CardIdentity.Scope(CardVocabulary.SeatAScope));

                    facts.WorldEpochAfterSpawn = host.CurrentEpoch.Value;
                    facts.CountersJoinedAfterSpawn = spawn.CountersJoined;

                    if (!targets.TryRegister(
                            futureSeat,
                            CardIdentity.Scope(CardVocabulary.SeatAScope),
                            CardTableKeys.SeatRecipe,
                            out DiagnosticCode registeredCode,
                            out string registeredDetail))
                    {
                        steps.Add(new CardStep(name, false, "index registration failed: " + registeredCode + ": " + registeredDetail));
                        return;
                    }

                    if (publisher.Registry.TryResolveTarget(futureSeat, out TargetHandle _, out Entity spawned))
                    {
                        IReadOnlyList<CapabilityBinding> rows = publisher.ReadBindingRows(futureSeat);
                        if (rows.Count > 0)
                        {
                            facts.SpawnedSeatBonusValue = rows[0].Value;
                        }

                        module?.BindSeat(CardTableKeys.SeatCOrdinal + 1U, spawned);

                        EntityManager entityManager = host.EntityWorld.EntityManager;
                        if (entityManager.HasComponent<AssemblyStamp>(spawned))
                        {
                            AssemblyStamp stamp = entityManager.GetComponentData<AssemblyStamp>(spawned);
                            facts.SpawnedSeatStampEpoch = stamp.AssemblyEpoch;
                            facts.SpawnedSeatStampPublished = stamp.Published != 0U;
                        }
                    }

                    facts.SpawnedSeatInView = publisher.Published.Bindings.HasTarget(futureSeat);

                    // The seat did not exist when the provider mounted, and it has the +2 contribution in its very
                    // first visible image with no local import (P-013, P-024).
                    bool pass = unmounted
                        && facts.ForwardDerivationHadNoTargetChange
                        && spawn.Outcome == DerivedAssemblyOutcome.Published
                        && spawn.IsSpawn
                        && facts.CountersJoinedAfterSpawn
                        && facts.SpawnedSeatBonusValue == CardVocabulary.FestivalBonus
                        && facts.SpawnedSeatStampEpoch == facts.WorldEpochAfterSpawn
                        && facts.SpawnedSeatStampPublished
                        && facts.SpawnedSeatInView;

                    steps.Add(new CardStep(name, pass,
                        "unmounted=" + unmounted
                        + "; forwardNoTargetChange=" + facts.ForwardDerivationHadNoTargetChange
                        + "; spawn=" + spawn.Describe()
                        + "; spawnedBonus=" + facts.SpawnedSeatBonusValue.ToString(CultureInfo.InvariantCulture)
                        + "; stampEpoch=" + facts.SpawnedSeatStampEpoch.ToString(CultureInfo.InvariantCulture)
                        + "; published=" + facts.SpawnedSeatStampPublished
                        + "; inView=" + facts.SpawnedSeatInView
                        + "; laneEpoch=" + facts.LaneEpochAfterSpawn.ToString(CultureInfo.InvariantCulture)
                        + "; worldEpoch=" + facts.WorldEpochAfterSpawn.ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardStep(name, false, DescribeException(exception)));
                }
            }

            // ---------------------------------------------------------------- 11. idle again, then teardown

            private void ProveIdleWorldAndTearDown()
            {
                const string name = "cards-idle-world-performs-zero-steps";
                try
                {
                    if (host == null || time == null)
                    {
                        steps.Add(new CardStep(name, false, "no time driver"));
                        return;
                    }

                    ulong stepBefore = host.CurrentStep.Value;
                    int imagesBefore = host.Publications.PublishedCount;
                    int runsBefore = host.StepGroup.DispatchRunCount;

                    ulong committed = 0UL;
                    for (int i = 0; i < IdleFrames; i++)
                    {
                        committed += time.PumpFrame(HostTicks).StepsCommitted;
                    }

                    facts.IdleFrames = IdleFrames;
                    facts.IdleStepsCommitted = (int)committed;
                    facts.IdlePublishedImages = host.Publications.PublishedCount;
                    facts.IdleDispatchRuns = host.StepGroup.DispatchRunCount - runsBefore;
                    facts.PendingDemandAfterIdle = host.PendingDemand;

                    bool pass = committed == 0UL
                        && host.CurrentStep.Value == stepBefore
                        && facts.IdlePublishedImages == imagesBefore
                        && facts.IdleDispatchRuns == 0
                        && facts.PendingDemandAfterIdle == 0UL
                        && time.Clocks.PendingWakeCount == 0;

                    steps.Add(new CardStep(name, pass,
                        "frames=" + IdleFrames.ToString(CultureInfo.InvariantCulture)
                        + "; steps=" + committed.ToString(CultureInfo.InvariantCulture)
                        + "; images=" + imagesBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + facts.IdlePublishedImages.ToString(CultureInfo.InvariantCulture)
                        + "; dispatchRuns=" + facts.IdleDispatchRuns.ToString(CultureInfo.InvariantCulture)
                        + "; demand=" + facts.PendingDemandAfterIdle.ToString(CultureInfo.InvariantCulture)));
                    TearDownSafely();
                }
                catch (Exception exception)
                {
                    steps.Add(new CardStep(name, false, DescribeException(exception)));
                }
            }

            private void TearDownSafely()
            {
                const string name = "cards-teardown-settles-and-disposes";
                try
                {
                    if (host == null)
                    {
                        steps.Add(new CardStep(name, false, "no world"));
                        return;
                    }

                    if (time != null)
                    {
                        time.Clear(out int _, out int _);
                    }

                    CardTableModule.DetachAll();
                    OperationResult stop = host.Stop(NextOperation(host.World), "gc-011 card teardown");
                    UnityWorldHost stopped = host;
                    stopped.Dispose();

                    facts.OutstandingJobsAfterTeardown = stopped.Ledger.OutstandingJobCount;
                    facts.RetainedResourcesAfterTeardown = stopped.Ledger.RetainedResourceCount;
                    facts.RegistryAfterTeardown = UnityWorldRegistry.Count;

                    bool pass = (stop.Outcome == Outcome.Published || stop.Outcome == Outcome.NoChange)
                        && facts.OutstandingJobsAfterTeardown == 0
                        && facts.RetainedResourcesAfterTeardown == 0
                        && facts.RegistryAfterTeardown == registryBeforeCreate;

                    steps.Add(new CardStep(name, pass,
                        "stop=" + stop.Outcome + "(" + stop.Code + ")"
                        + "; outstandingAfter=" + facts.OutstandingJobsAfterTeardown.ToString(CultureInfo.InvariantCulture)
                        + "; retainedResources=" + facts.RetainedResourcesAfterTeardown.ToString(CultureInfo.InvariantCulture)
                        + "; registryAfter=" + facts.RegistryAfterTeardown.ToString(CultureInfo.InvariantCulture)
                        + "; registryBefore=" + registryBeforeCreate.ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardStep(name, false, DescribeException(exception)));
                }
            }

            // ---------------------------------------------------------------- helpers

            /// <summary>
            /// Applies one composition edit and publishes the world's assembly for that same publication. P-006 has
            /// one publication series, so an edit that the world does not answer leaves the lane one publication
            /// ahead and every later adoption is refused as stale: the two halves are therefore always done
            /// together, and a `NoTargetChange` derivation is a valid answer (the publication still stands).
            /// </summary>
            private bool PublishEdit(CompositionEditPayload payload)
            {
                if (lane == null || pipeline == null || host == null)
                {
                    return false;
                }

                string editName = "cards-edit-" + payload.Subject.ToString();
                EditAdmission admission = lane.SubmitEdit(payload, NextOperation(lane.World), lane.Committed.Revision);
                if (!admission.Staged)
                {
                    steps.Add(new CardStep(
                        editName, false, "the edit was refused: " + admission.Kind + "/" + admission.Code));
                    return false;
                }

                IReadOnlyList<PublishedOperation> published = lane.Drain();
                if (published.Count == 0 || published[0].Outcome == Outcome.Rejected)
                {
                    steps.Add(new CardStep(
                        editName,
                        false,
                        "the publication was refused: " + (published.Count > 0 ? published[0].Outcome.ToString() : "none")));
                    return false;
                }

                DerivedAssemblyReport report = pipeline.PublishDerived(NextOperation(host.World));
                if (report.Outcome == DerivedAssemblyOutcome.Refused)
                {
                    steps.Add(new CardStep(editName, false, "the world refused the assembly: " + report.Describe()));
                    return false;
                }

                installedRows += report.InstalledRows;
                return report.CountersJoined;
            }

            /// <summary>
            /// Applies one edit, publishes the world's assembly for it and reports that derivation, so a step that
            /// needs the publication's own derivation outcome does not derive a second time for a consumed number.
            /// </summary>
            private bool ApplyEditAndReport(
                CompositionEditPayload payload,
                OperationId operation,
                out DerivedAssemblyReport report)
            {
                report = new DerivedAssemblyReport { Operation = operation, Outcome = DerivedAssemblyOutcome.Refused };
                if (lane == null || pipeline == null || host == null)
                {
                    return false;
                }

                string editName = "cards-edit-" + payload.Subject.ToString();
                EditAdmission admission = lane.SubmitEdit(payload, operation, lane.Committed.Revision);
                if (!admission.Staged)
                {
                    steps.Add(new CardStep(editName, false, "the edit was refused: " + admission.Kind + "/" + admission.Code));
                    return false;
                }

                IReadOnlyList<PublishedOperation> published = lane.Drain();
                if (published.Count == 0 || published[0].Outcome == Outcome.Rejected)
                {
                    steps.Add(new CardStep(editName, false, "the publication was refused"));
                    return false;
                }

                report = pipeline.PublishDerived(operation);
                if (report.Outcome == DerivedAssemblyOutcome.Refused)
                {
                    steps.Add(new CardStep(editName, false, "the world refused the assembly: " + report.Describe()));
                    return false;
                }

                installedRows += report.InstalledRows;
                return report.CountersJoined;
            }

            /// <summary>Applies a sequence of edits, each with its own publication, in admission order.</summary>
            private bool PublishEdits(IReadOnlyList<CompositionEditPayload> payloads)
            {
                for (int i = 0; i < payloads.Count; i++)
                {
                    if (!PublishEdit(payloads[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            private CommandAdmissionReceipt SubmitCommand(
                in CardCommandPayload command,
                OperationId? operation)
            {
                if (host == null || module == null)
                {
                    return new CommandAdmissionReceipt(
                        default(OperationId),
                        new RequestResult(RequestResultKind.Rejected, DiagnosticCode.ApplyFault, default(EventCursor)),
                        AdmissionSequence.Zero);
                }

                OperationId request = operation ?? NextOperation(host.World);
                var envelope = new CommandEnvelope(
                    request,
                    CardTableKeys.CommandRoute,
                    CardIdentity.Target(CardVocabulary.TableOne),
                    CardTableKeys.CommandSchema,
                    null,
                    CardPayloadCodec.WriteCommand(command));
                return host.Submit(envelope);
            }

            private int HandCount(uint seatOrdinal)
            {
                if (module == null || host == null || !module.TrySeat(seatOrdinal, out Entity seat))
                {
                    return -1;
                }

                return CardTableAccess.ReadCards(host.EntityWorld.EntityManager, seat).Count;
            }

            private int SeatScore(uint seatOrdinal)
            {
                if (module == null || host == null || !module.TrySeat(seatOrdinal, out Entity seat))
                {
                    return int.MinValue;
                }

                return host.EntityWorld.EntityManager.GetComponentData<CardSeatState>(seat).Score;
            }

            private WorldId NextSession() => new WorldId(sessionSequence.Next());

            private OperationId NextOperation(WorldId world)
            {
                operationSequence++;
                return new OperationId(world, CardTableFixture.Issuer, operationSequence);
            }

            private static string DescribeException(Exception exception)
                => "unhandled " + exception.GetType().FullName + ": " + exception.Message;
        }
    }
}

// GameCore.Validation.ProbeHost — the GC-024 combined cross-family world (07 s5, 07:251-278).
//
// WHAT 07 s5 ASKS FOR, AND WHAT THIS FILE BUILDS
//
// 07:251-278 specifies ONE `CommandDriven` world that carries the chapter-quest packages, the card-table packages
// and the `NarrativeCardRewards` bridge, whose reward is delivered narrative -> card through GC-021's durable,
// idempotent outbox. The shipped harness recorded that as a gap at `Gc021Scenario.cs:33-36`; this file closes it.
//
// ONE ROOT, TWO FAMILIES (P-010). A combined world has one root scope, so both families' declared trees are
// remapped under `CrossRootScope`: a narrative record whose parent was `NarrativeKeys.RootScope`, and a card
// scope-create payload whose parent was `CardMarketComposition.MatchScope`, becomes a depth-1 child of the combined
// root, and everything deeper keeps its own parent at depth 2. No gameplay scope is renamed (07 s5's `CardTent` is
// the card market's own `cards.table-area` in this revision), so the tree simply has one root instead of two.
//
// ONE SCHEDULE, ONE REGISTRATION, ONE LANE. The merged manifest set is compiled by
// `OwnershipSchedulePipeline.Build` with `MergedDispatchKinds`, which answers every compiled system key from either
// family's own `DispatchKinds()` table; the merged `UnityWorldRegistration` unions both families' `Systems()`, their
// plane routes and buffers, and their typed payload readers. The merged plane's retained-event bound is raised to
// 32 because the reward path reads several committed events per pass and the narrative plane's own 8 is too small
// for a combined world. Both families' targets are seeded into the SAME `(host, targets, seeder)` and BOTH modules
// are attached, which is legal because each is a per-`World` static registry (`NarrativeModule.Attach`,
// `CardTableModule.Attach`).
//
// THE REWARD FLOW (07:267-278). One committed Chapter One choice becomes one durable obligation whose destination
// is not yet touched (persist-then-apply, P-045); the obligation is then dispatched and acknowledged, which is the
// only way this card package can express a grant (an owner-committed `Transfer`, GC-021 s6 item 7); and the same
// obligation is handed over again in a new session built from the rows the acknowledgement window left behind, so
// the destination reports `AlreadyApplied` and no second card moves.
//
// THE 07:276 ROWS ARE PERFORMED, NOT REPORTED. The bridge is mounted as a real installation
// (`Packages/com.gamecore.gameplay.rewards`, assembly `GameCore.Gameplay.Rewards`), so all four of 07:276's claims
// have somewhere to happen: the installation registers a job-fenced resource lease for its pending work, so the O-07
// unmount cannot settle and the lane answers `TeardownBlocked`; the outbox slot is declared with `PreserveDormant`
// and a registered v1 -> v2 migration whose body refuses a copied pending count that is not zero, so the prewrite
// migration refuses while work is pending and the drained slot is retained dormant by the policy pass's own
// disposition; a fresh obligation is carried to an explicitly selected compatible owner; and the card tent's scoring
// provider leaves without reversing the issued card or the committed score (P-029, P-032, P-045, P-047, P-048).
//
// WHERE THE INSTALLATION LIVES. The installation is mounted through the package's own payload at the card tent's
// scope (`CardMarketComposition.TableScope`), and its declared outbox row lives on the card table's target
// (`OutboxSlotTarget`). That declaration reaches this world's lane manifest source and its state-policy catalog, so
// the slot's `PreserveDormant` last-support policy and its registered migration are declared exactly where P-032
// reads them. The compiled ownership schedule the world registers (`FamilyDeclarations()`) stays the two families'
// own set, because a declaration whose plugin ships no host-side system cannot be in a world's dispatch table:
// `CompiledScheduleAdapter.Registrations` requires one registration per compiled entry
// (ScheduleDispatchAdapter.cs:372-400), and 07 s5 defines this plugin's runtime as the mounted installation, which
// this run drives at idle boundaries rather than from inside a step (P-030, P-037).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Derivation.Fixtures;
using GameCore.Execution;
using GameCore.Execution.Delivery;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Cards;
using GameCore.Gameplay.Cards.Fixtures;
using GameCore.Gameplay.Integration.RewardOutbox;
using GameCore.Gameplay.Narrative;
using GameCore.Gameplay.Narrative.Fixtures;
using GameCore.Gameplay.Rewards;
using GameCore.Planning;
using GameCore.Planning.StatePolicies;
using GameCore.Unity.Runtime.StateMigration;
using GameCore.ReferenceConformance;
using GameCore.Rules.Cards;
using GameCore.Rules.Narrative;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Messages;
using GameCore.Unity.Runtime.Time;
using Unity.Entities;
using PlanningCompositionProposal = GameCore.Planning.CompositionProposal;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// The combined cross-family world: one `CommandDriven` world carrying both families, driven through 07 s5's
    /// reward flow, with its normalized trace compared against the transcribed `cross` table by the fixture oracle.
    /// </summary>
    public static class ConformanceCrossWorld
    {
        /// <summary>World name; the host appends the session id, so every incarnation is inspectable.</summary>
        public const string WorldName = "GameCoreConformanceCrossWorld";

        /// <summary>The conformance label every observation of this run is qualified with.</summary>
        public const string Label = "cross";

        /// <summary>Salt of the session-id sequence, so a combined-world run is reproducible (P-008).</summary>
        public const ulong SessionSalt = 0x434F4E4643524F53UL;

        /// <summary>The step-name prefix every observation of this run carries.</summary>
        public const string StepPrefix = "conformance/" + Label + "/";

        /// <summary>Registered target capacity: both families' trees and the world-level ledger fit inside it.</summary>
        private const int TargetCapacity = 48;

        /// <summary>Host ticks every pump of this run advances, exactly as the sibling scenarios pump (P-036).</summary>
        private const ulong IdlePumpTicks = 1000000UL;

        /// <summary>Bounds of the reward owner, as the bridge's constructor takes them (07 s5).</summary>
        private const int RewardCapacity = 8;

        private const int RewardTerminalRetention = 8;

        /// <summary>Bound of one committed-event poll and of one dispatch pass (P-043's bounded work).</summary>
        private const int RewardEventWindow = 16;

        private const int RewardDispatchWindow = 4;

        /// <summary>
        /// Committed events a combined world's plane retains. The reward path reads several committed events per
        /// pass (the choice, the fact observation, the gate decision), so the narrative plane's own 8 is too small
        /// here and the merged plane declares 32 (P-045's retention, 07 s5 step 1).
        /// </summary>
        private const int MergedRetainedEvents = 32;

        private const ulong StagedByteCeiling = 1024UL * 1024UL;

        private const ulong ScratchCapacityBytes = 4096UL;

        private const ulong ScratchBytesPerSlot = 64UL;

        private const ulong PrepareBytesLimit = 1024UL * 1024UL;

        /// <summary>
        /// The combined root scope (P-010): the only scope of this world without a parent. Both families' trees hang
        /// beneath it, and the world-level `QuestLedger` is seeded in it because 07 s3.1 puts the durable facts at
        /// the world level.
        /// </summary>
        public static readonly ScopeId CrossRootScope =
            new ScopeId(StableNameKeyDerivation.Derive("gc024.festival-world"));

        /// <summary>
        /// Stable identity of the reward bridge's delivery owner. It is deliberately not the card destination's
        /// identity: one destination has one owner and one owner has one outbox (P-017, P-034).
        /// </summary>
        public static readonly Id128 RewardOwnerId = new Id128(0x434F4E4643524F53UL, 1UL);

        /// <summary>
        /// Every observation name this run records, in execution order, without the prefix. `ObservationNames()`
        /// returns exactly these names qualified with <see cref="StepPrefix"/>, so a renamed or dropped observation
        /// fails the harness instead of shrinking the run silently (P-060). The order is `ReferenceScripts.Cross()`'s
        /// own: the three setup operations, the seven rows of the `cross` table's script, the teardown, the
        /// action-surface audit and the oracle's verdict.
        /// </summary>
        private static readonly string[] RecordedSuffixes =
        {
            "reward-flow/world",
            "reward-flow/setup-0",
            "reward-flow/setup-1",
            "reward-flow/setup-2",
            "reward-enqueue",
            "reward-unmount-pending",
            "reward-settle",
            "reward-drain-then-unmount",
            "reward-unmount-transfer",
            "reward-redelivery",
            "reward-scoring-unmount-keeps-card",
            "reward-flow/teardown",
            "no-action-surface-in-card-or-narrative",
            "verdict",
        };

        /// <summary>Every step name this run records, for the harness's "every name was observed" check.</summary>
        public static IReadOnlyList<string> ObservationNames()
        {
            var names = new List<string>(RecordedSuffixes.Length);
            for (int i = 0; i < RecordedSuffixes.Length; i++)
            {
                names.Add(StepPrefix + RecordedSuffixes[i]);
            }

            return names;
        }

        /// <summary>
        /// Builds the combined world, drives 07 s5's reward flow through it, records the whole thing into one
        /// canonical trace and hands that trace to the fixture oracle. The world is always torn down, and a step that
        /// could not be performed is reported `Unsupported` with its reason rather than faked.
        /// </summary>
        public static ConformanceTableResult Run()
        {
            ConformanceScript? script = ReferenceScripts.ById(Label);
            ConformanceTable? table = ReferenceTables.ById(Label);
            if (script == null || table == null)
            {
                throw new InvalidOperationException(
                    "no 07 table or script carries the id '" + Label + "' (GC-024).");
            }

            var steps = new List<ConformanceObservation>();
            var trace = new ConformanceTrace(Label);
            var outcomes = new List<ConformanceOracle.RowOutcomeReport>();
            var sessions = new IdSequence(SessionSalt);
            string unhandled = string.Empty;

            var world = new CrossWorld(sessions, TargetCapacity);
            CrossDeliveryHook? hook = null;
            RewardsInstallation? rewardInstallation = null;
            RewardsInstallation? destinationInstallation = null;
            NarrativeModule? narrativeModule = null;
            CardTableModule? cardModule = null;
            string outstanding = "outstandingWork=none";
            // Every name this run does not reach in its own order is reported here, so a red run still observes
            // exactly the names `ObservationNames()` declares (P-060).
            int reached = 3;
            try
            {
                if (!world.Ready)
                {
                    ReportFrom(steps, 0, "the combined world could not be built: " + world.Failure);
                    reached = RecordedSuffixes.Length - 3;
                }
                else
                {
                    steps.Add(new ConformanceObservation(
                        StepPrefix + RecordedSuffixes[0],
                        world.MatchesPublishedAssembly(),
                        "session=" + world.Host!.World.Session.ToString()
                        + "; mode=" + world.Lane!.Committed.Mode
                        + "; targets=" + world.Targets!.Count.ToString(CultureInfo.InvariantCulture)
                        + "; scopes=" + world.Lane.Committed.Scopes.Count.ToString(CultureInfo.InvariantCulture)
                        + "; stages=" + world.Descriptor!.Descriptor!.Stages.Count.ToString(CultureInfo.InvariantCulture)
                        + "; systems=" + world.Registration.Systems.Count.ToString(CultureInfo.InvariantCulture)
                        + "; routes=" + world.Registration.Messages!.Routes.Count.ToString(CultureInfo.InvariantCulture)
                        + "; retainedEvents="
                        + world.Registration.Messages.MaxRetainedEvents.ToString(CultureInfo.InvariantCulture)
                        + "; joined=" + world.MatchesPublishedAssembly()
                        + "; root=" + CrossRootScope.ToString()));

                    // 1. The Chapter One provider, so the permit choice can commit (07:267). The mount is the
                    //    narrative package's own payload for the declaration this world carries, published through
                    //    the lane exactly as `ConformanceWorld.PublishEdit` publishes an edit (P-006).
                    ConformanceOperationResult provider = world.PublishEdit(
                        NarrativeMounts.Mount(
                            world.Declarations[0].Manifest,
                            NarrativeKeys.ChapterOneInstall,
                            NarrativeKeys.ChapterOneScope,
                            world.Declarations[0].SchemaDefaults),
                        "mount-chapter-one");
                    steps.Add(new ConformanceObservation(
                        StepPrefix + RecordedSuffixes[1],
                        provider.Published,
                        "mount-provider: " + provider.Detail));

                    // 2. The reward installation of 07 s5 (GC-024). The content declares the node the chapter-one
                    //    permit choice lands on; the definition's card is the one the card fixture really stocks at
                    //    the holding seat, so nothing here is a card concept this file invented (P-001, P-034,
                    //    P-054). The installation is the mounted thing: the package's own O-03 payload puts it in
                    //    the card tent's scope, and the bridge is constructed through it, so the run cannot
                    //    assemble the two inconsistently (P-020).
                    var content = Gc021RewardContent.ForNode(NarrativeDialogueRules.PermitResultNode);
                    RewardCatalog catalog = content.ToCatalog(requiresDurability: true, out string contentDetail);
                    hook = new CrossDeliveryHook();
                    var journal = new MemoryDeliveryJournal("memory://gc024-cross-rewards");
                    rewardInstallation = RewardsInstallation.Mount(
                        world.Host!,
                        world.Time!,
                        catalog,
                        RewardCapacity,
                        RewardTerminalRetention,
                        OutboxDurability.Durable,
                        journal,
                        hook);
                    hook.Attach(rewardInstallation.Bridge);
                    ConformanceOperationResult mountedInstallation = world.PublishEdit(
                        RewardsMounts.Mount(RewardsKeys.Installation, CardMarketComposition.TableScope),
                        "mount-rewards-installation");
                    steps.Add(new ConformanceObservation(
                        StepPrefix + RecordedSuffixes[2],
                        mountedInstallation.Published
                            && catalog.Count == 1 && catalog.RequiresDurability && contentDetail.Length == 0,
                        "mount-reward-bridge: content={" + content.Describe() + "}"
                        + "; definitions=" + catalog.Count.ToString(CultureInfo.InvariantCulture)
                        + "; durability=" + rewardInstallation.Bridge.Owner.Outbox.Durability
                        + "; installation=" + RewardsKeys.Installation.ToString()
                        + "; scope=" + CardMarketComposition.TableScope.ToString()
                        + "; laneState=" + world.LaneInstallState()
                        + "; mount={" + mountedInstallation.Outcome + ": "
                        + Clip(mountedInstallation.Detail, 160) + "}"
                        + "; detail=" + (contentDetail.Length == 0 ? "declared" : contentDetail)));

                    // 3. The card tent's own scoring provider (suffix 3), mounted exactly as the card market's own
                    //    conformance host mounts it in its world, so the last row's unmount has a provider to
                    //    retract and the contribution it retracts is a real one (07:276, P-017).
                    ConformanceOperationResult scoringProvider = world.PublishEdit(
                        CardTablePayloads.Mount(
                            Gc013CardsHost.Declarations()[1].Manifest,
                            CardTableFixture.FestivalScoringInstance,
                            CardIdentity.Scope(CardVocabulary.LeagueA)),
                        "mount-festival-scoring");
                    steps.Add(new ConformanceObservation(
                        StepPrefix + RecordedSuffixes[3],
                        scoringProvider.Published,
                        "mount-scoring-provider: " + scoringProvider.Detail));

                    reached = 4;
                    RunRewardFlow(
                        world,
                        script,
                        rewardInstallation,
                        hook,
                        trace,
                        outcomes,
                        steps,
                        out narrativeModule,
                        out cardModule,
                        out destinationInstallation);
                    reached = RecordedSuffixes.Length - 3;
                }
            }
            catch (Exception exception)
            {
                unhandled = "unhandled " + exception.GetType().FullName + ": " + exception.Message;
                ReportFrom(steps, reached, unhandled);
                reached = RecordedSuffixes.Length - 3;
            }
            finally
            {
                // A work lease this run armed and never completed is completed here, so the world's own stop sees
                // no unfinished job still holding a resource and the teardown row reports what really happened
                // (P-047, P-048). A completed lease is not retired by `Dispose` on its own, which is why this is
                // the run's job rather than the installation's.
                if (rewardInstallation != null && world.Host != null && rewardInstallation.HasOutstandingWorkLease)
                {
                    RewardsLifecycleResult completion =
                        rewardInstallation.CompletePendingWork(world.Host, TransferWorkOrdinal);
                    outstanding = "outstandingWork=" + completion.Outcome + "/" + completion.Code + "("
                        + Clip(completion.Detail, 160) + ")";
                }

                if (destinationInstallation != null)
                {
                    // The destination installation this run mounted for the transfer owns a second delivery owner
                    // over the same world, so it is disposed before the world stops.
                    destinationInstallation.Dispose();
                    destinationInstallation = null;
                }

                if (rewardInstallation != null)
                {
                    // The installation owns the bridge, so disposing it releases the delivery owner.
                    rewardInstallation.Dispose();
                    rewardInstallation = null;
                }

                cardModule?.Dispose();
                narrativeModule?.Dispose();
                Outcome stop = world.StopAndDispose();
                steps.Add(new ConformanceObservation(
                    StepPrefix + RecordedSuffixes[11],
                    unhandled.Length == 0 && (stop == Outcome.Published || stop == Outcome.NoChange),
                    "stop=" + stop
                    + "; registry=" + UnityWorldRegistry.Count.ToString(CultureInfo.InvariantCulture)
                    + "; " + outstanding
                    + (unhandled.Length == 0 ? string.Empty : "; " + unhandled)));
            }

            // The action-surface audit (TEST-021) is the run's own claim about the composition it just built.
            CrossCompositionAudit audit = AuditCombinedComposition();
            steps.Add(new ConformanceObservation(
                StepPrefix + RecordedSuffixes[12],
                audit.Clean,
                audit.Describe()));

            ConformanceVerdict verdict = ConformanceOracle.CompareScript(script!, trace, outcomes);
            steps.Add(new ConformanceObservation(
                StepPrefix + RecordedSuffixes[13],
                verdict.Passed,
                verdict.Describe()));
            return new ConformanceTableResult(Label, steps, trace, verdict, trace.ToDocument());
        }

        /// <summary>
        /// The target the installation's declared outbox row lives on: the card table's own target, inside the scope
        /// the installation is mounted at (`CardMarketComposition.TableScope`). The row's value is the pending-work
        /// count the drain row reads and the migration precondition copies (P-032, P-045).
        /// </summary>
        private static TargetId OutboxSlotTarget => CardIdentity.Target(CardVocabulary.TableOne);

        /// <summary>Work ordinal of the obligation the enqueue row arms, and of the one the transfer row carries.</summary>
        private const ulong EnqueueWorkOrdinal = 1UL;

        private const ulong TransferWorkOrdinal = 2UL;

        /// <summary>
        /// The rows of `ReferenceScripts.Cross()`, in the script's own order: 07:267's one committed choice becomes
        /// one durable obligation; 07:276's unmount is refused while that work is pending; 07:269-270 dispatch and
        /// acknowledge it; the drained outbox is preserved dormant and the unmount then settles; a fresh obligation
        /// is carried to an explicitly selected compatible owner; 07:272's redelivery after an acknowledgement loss
        /// mutates nothing; and the card tent's scoring provider leaves without reversing the issued card or the
        /// committed score (07:267-276, P-003, P-029, P-032, P-045, P-047, P-048).
        ///
        /// The script's two precondition steps of the transfer row (`reward-unmount-transfer/pre1` and `/pre2`) are
        /// executed here too, in their script order, because their expectations are the before state that row's
        /// assertions are made against — they are recorded under their own row ids and reported to the oracle.
        /// </summary>
        private static void RunRewardFlow(
            CrossWorld world,
            ConformanceScript script,
            RewardsInstallation installation,
            CrossDeliveryHook hook,
            ConformanceTrace trace,
            List<ConformanceOracle.RowOutcomeReport> outcomes,
            List<ConformanceObservation> steps,
            out NarrativeModule? narrativeModule,
            out CardTableModule? cardModule,
            out RewardsInstallation? destination)
        {
            destination = null;
            NarrativeCardRewardBridge source = installation.Bridge;
            UnityWorldHost host = world.Host!;

            // ---------------------------------------------------------------- 07:267 — reward-enqueue
            IReadOnlyList<string> enqueueFields = FieldsOf(script, "reward-enqueue");
            List<string> enqueueVocabBefore = ReadVocabulary(world, source, installation);
            List<string> enqueueBefore = ReadAll(world, source, installation, enqueueFields);

            // 07:267 — one choice on the narrative family's own route, at the node its live conversation sits on and
            // with the declared permit choice, so `NarrativeDialogueRules.Validate` accepts it (P-042, 07 s3.2).
            ConformanceOperationResult committed = world.SubmitAndPump(
                new CommandEnvelope(
                    world.NextOperation(),
                    NarrativeKeys.ChoiceRoute,
                    NarrativeKeys.Mara,
                    NarrativeKeys.ChoiceCommandSchema,
                    null,
                    new FrozenPayload(NarrativePayloadCodec.EncodeChoice(
                        new NarrativeChoice(NarrativeDialogueRules.PermitResultNode - 1,
                            NarrativeDialogueRules.PermitChoice)))),
                "commit-permit-choice");

            // One persist-only pass: the obligation exists before anything is handed over (`maxDispatches` = 0 is
            // P-045's persist-then-apply order, 07 s5 steps 1-2).
            RewardBridgePassReport enqueue = installation.RunBridgePass(world.NextOperation(), RewardEventWindow, 0);

            // The pending work is armed and the outbox slot seeded with its own count, so the next row's unmount
            // finds work to refuse and the registered migration has a copied pending count to read (P-045, P-047).
            RewardsLifecycleResult armed = installation.ArmPendingWork(host, EnqueueWorkOrdinal);
            bool seeded = installation.SeedOutboxSlot(
                host, OutboxSlotTarget, installation.PendingWorkCount, out string seedDetail);

            List<string> enqueueAfter = ReadAll(world, source, installation, enqueueFields);
            List<string> enqueueVocabAfter = ReadVocabulary(world, source, installation);
            StepRecord(trace, "reward-enqueue", enqueueFields, enqueueBefore, enqueueAfter);
            RecordSnapshot(trace, "reward-enqueue", ConformancePhase.Before, enqueueVocabBefore);
            RecordSnapshot(trace, "reward-enqueue", ConformancePhase.After, enqueueVocabAfter);

            var enqueueReadings = new RowReadings(enqueueFields, enqueueBefore, enqueueAfter);
            bool enqueuePassed = committed.Published
                && armed.Settled
                && seeded
                && enqueueReadings.Require(ConformanceFields.OutboxRecognised, "0", "1")
                && enqueueReadings.Require(ConformanceFields.OutboxOpen, "0", "1")
                && enqueueReadings.Hold(ConformanceFields.OutboxAcknowledged, "0")
                && enqueueReadings.Hold(ConformanceFields.OutboxMutations, "0")
                && enqueueReadings.Preserved(ConformanceFields.RewardRecipientHandSize)
                && enqueueReadings.Preserved(ConformanceFields.RewardHolderHandSize)
                && source.Owner.IsDurable;
            outcomes.Add(new ConformanceOracle.RowOutcomeReport("reward-enqueue", enqueuePassed, enqueuePassed
                ? string.Empty
                : "the committed choice did not become one open, unapplied obligation with its work armed"));
            steps.Add(new ConformanceObservation(
                StepPrefix + RecordedSuffixes[4],
                enqueuePassed,
                RowDetail("reward-enqueue", "Published", enqueueReadings, enqueueVocabBefore, enqueueVocabAfter)
                + "; committed={" + committed.Outcome + ": " + committed.Detail + "}"
                + "; pass={" + enqueue + "}"
                + "; arm={" + armed.Outcome + "/" + armed.Code + ": " + Clip(armed.Detail, 160) + "}"
                + "; slot={" + (seeded ? "seeded" : "REFUSED") + ": " + Clip(seedDetail, 120) + "}"
                + "; recognised=" + source.RecognisedCount.ToString(CultureInfo.InvariantCulture)
                + "; durable=" + source.Owner.IsDurable
                + "; destinationMutations=" + source.Destination.CommittedCount.ToString(CultureInfo.InvariantCulture)
                + "; describe=" + Clip(source.LastDescribeDetail, 120)));

            // ---------------------------------------------------------------- 07:276 — reward-unmount-pending
            IReadOnlyList<string> pendingFields = FieldsOf(script, "reward-unmount-pending");
            List<string> pendingVocabBefore = ReadVocabulary(world, source, installation);
            List<string> pendingBefore = ReadAll(world, source, installation, pendingFields);
            int pendingCount = installation.PendingWorkCount;

            // The O-07 unmount while an obligation is still open: the installation's job-fenced outbox lease makes
            // the teardown unable to settle, so the lane answers `TeardownBlocked` and the installation stays mounted
            // (07:276, P-047, P-048). `RefusedPendingWork` with that code is the row's own claim.
            RewardsLifecycleResult refusal = installation.TryUnmount(host, world.Lane!);

            // 07:276's other half on the same row: the registered v1 -> v2 migration refuses the copied pending
            // count, so this pass refuses before any live write and no assembly may be published for the lane's own
            // O-07 publication yet (P-029). The same pass succeeds once the work has drained, which is the next row.
            RewardsLifecycleResult precondition = installation.TryMigrateOutboxSlot(world.Policies!, world.TargetIds());

            List<string> pendingAfter = ReadAll(world, source, installation, pendingFields);
            List<string> pendingVocabAfter = ReadVocabulary(world, source, installation);
            StepRecord(trace, "reward-unmount-pending", pendingFields, pendingBefore, pendingAfter);
            RecordSnapshot(trace, "reward-unmount-pending", ConformancePhase.Before, pendingVocabBefore);
            RecordSnapshot(trace, "reward-unmount-pending", ConformancePhase.After, pendingVocabAfter);

            var pendingReadings = new RowReadings(pendingFields, pendingBefore, pendingAfter);
            bool unmountRefused = refusal.Outcome == RewardsLifecycleOutcome.RefusedPendingWork
                && refusal.Code == DiagnosticCode.TeardownBlocked
                && refusal.Detail.Contains(RewardsKeys.Installation.ToString())
                && refusal.Detail.Contains(RewardsKeys.OutboxSlot.ToString())
                && refusal.Detail.Contains(pendingCount.ToString(CultureInfo.InvariantCulture));
            bool preconditionRefused = precondition.Outcome == RewardsLifecycleOutcome.RefusedPrecondition
                && precondition.Code == DiagnosticCode.MigrationRequired
                && installation.OutboxMigration.Invocations == 1
                && installation.OutboxMigration.Refusals == 1;
            bool pendingPassed = unmountRefused
                && preconditionRefused
                && pendingReadings.Hold(ConformanceFields.RewardsInstallationState, "mounted")
                && pendingReadings.Hold(ConformanceFields.OutboxOpen, "1")
                && pendingReadings.Hold(ConformanceFields.OutboxPendingWork, "1")
                && pendingReadings.Hold(ConformanceFields.OutboxMutations, "0")
                && pendingReadings.Hold(ConformanceFields.OutboxRows, "1")
                && pendingReadings.Hold(ConformanceFields.BridgePermit, "1")
                && pendingReadings.Preserved(ConformanceFields.RewardRecipientHandSize)
                && pendingReadings.Preserved(ConformanceFields.RewardHolderHandSize);
            // The row is `RefusedKeepsAssembly`: the operation must be reported as it really answered, and the
            // oracle fails the row if the unmount settled (the old assembly would not then be standing) — P-047.
            outcomes.Add(new ConformanceOracle.RowOutcomeReport(
                "reward-unmount-pending", refusal.Settled, refusal.Detail));
            steps.Add(new ConformanceObservation(
                StepPrefix + RecordedSuffixes[5],
                pendingPassed,
                RowDetail("reward-unmount-pending", "Refused", pendingReadings, pendingVocabBefore, pendingVocabAfter)
                + "; unmount={" + refusal.Outcome + "/" + refusal.Code + ": " + Clip(refusal.Detail, 200) + "}"
                + "; migrationPrecondition={" + precondition.Outcome + "/" + precondition.Code + ": "
                + Clip(precondition.Detail, 200) + "}"
                + "; migrationInvocations="
                + installation.OutboxMigration.Invocations.ToString(CultureInfo.InvariantCulture)
                + "; migrationRefusals="
                + installation.OutboxMigration.Refusals.ToString(CultureInfo.InvariantCulture)
                + "; laneAssembly=withheld (P-029: the pass runs first, the assembly follows once the work drains)"));

            // ---------------------------------------------------------------- 07:269-270 — reward-settle
            IReadOnlyList<string> settleFields = FieldsOf(script, "reward-settle");
            List<string> settleVocabBefore = ReadVocabulary(world, source, installation);
            List<string> settleBefore = ReadAll(world, source, installation, settleFields);
            int handABefore = HandSize(world, CardTableKeys.SeatAOrdinal);
            int handBBefore = HandSize(world, CardTableKeys.SeatBOrdinal);
            IReadOnlyList<DeliveryObligation> openBefore = source.Owner.Outbox.OpenObligations();

            // The dispatch half: the bridge hands the open obligation to the card destination, which submits the
            // card family's own transfer command through the ordinary command port and applies it (07 s5 step 3).
            RewardBridgePassReport settle = installation.RunBridgePass(
                default(OperationId), RewardEventWindow, RewardDispatchWindow);

            // The acknowledgement half, which 07 s5 calls the ordered `rewards.ack` stage: the destination's answer is
            // recorded through the delivery owner's own adapter, so the obligation becomes terminal and the
            // destination's cursor advances. Acknowledging before the destination answered would be a state it never
            // reported, so the ids acknowledged here are the ones the pass was handed (P-045).
            int acknowledged = 0;
            var acknowledgements = new List<string>(openBefore.Count);
            for (int i = 0; i < openBefore.Count; i++)
            {
                DeliveryOutcome outcome = source.Owner.Adapter.TryAcknowledge(
                    openBefore[i].Key.OutboxId, out DiagnosticCode acknowledgeCode, out string acknowledgeDetail);
                if (outcome == DeliveryOutcome.Acknowledged)
                {
                    acknowledged++;
                }

                acknowledgements.Add(outcome + "/" + acknowledgeCode + "(" + Clip(acknowledgeDetail, 80) + ")");
            }

            List<string> settleAfter = ReadAll(world, source, installation, settleFields);
            List<string> settleVocabAfter = ReadVocabulary(world, source, installation);
            StepRecord(trace, "reward-settle", settleFields, settleBefore, settleAfter);
            RecordSnapshot(trace, "reward-settle", ConformancePhase.Before, settleVocabBefore);
            RecordSnapshot(trace, "reward-settle", ConformancePhase.After, settleVocabAfter);

            int handAAfter = HandSize(world, CardTableKeys.SeatAOrdinal);
            int handBAfter = HandSize(world, CardTableKeys.SeatBOrdinal);
            var settleReadings = new RowReadings(settleFields, settleBefore, settleAfter);
            bool settlePassed = settle.Dispatched == 1
                && acknowledged == 1
                && source.Owner.AcknowledgedCount == 1
                && settleReadings.Require(ConformanceFields.OutboxOpen, "1", "0")
                && settleReadings.Require(ConformanceFields.OutboxAcknowledged, "0", "1")
                && settleReadings.Require(ConformanceFields.OutboxMutations, "0", "1")
                && settleReadings.Require(ConformanceFields.RewardRecipientHandSize, "4", "5")
                && settleReadings.Require(ConformanceFields.RewardHolderHandSize, "4", "3")
                && settleReadings.Hold(ConformanceFields.BridgePermit, "1")
                && settleReadings.Hold(ConformanceFields.RewardRecipientTotal, "4")
                && settleReadings.Hold(ConformanceFields.RewardHolderTotal, "4")
                && settleReadings.Hold(ConformanceFields.BridgePermitVersion, "2")
                && handAAfter == handABefore + 1
                && handBAfter == handBBefore - 1;
            outcomes.Add(new ConformanceOracle.RowOutcomeReport("reward-settle", settlePassed, settlePassed
                ? string.Empty
                : "the admitted reward did not commit exactly one transfer and acknowledge it"));
            steps.Add(new ConformanceObservation(
                StepPrefix + RecordedSuffixes[6],
                settlePassed,
                RowDetail("reward-settle", "Published", settleReadings, settleVocabBefore, settleVocabAfter)
                + "; pass={" + settle + "}"
                + "; acknowledged=" + source.Owner.AcknowledgedCount.ToString(CultureInfo.InvariantCulture)
                + "; ack={" + string.Join(",", acknowledgements.ToArray()) + "}"
                + "; open=" + source.Owner.Outbox.OpenCount.ToString(CultureInfo.InvariantCulture)
                + "; mutations=" + source.Destination.CommittedCount.ToString(CultureInfo.InvariantCulture)
                + "; hand-a=" + handABefore.ToString(CultureInfo.InvariantCulture) + "->"
                + handAAfter.ToString(CultureInfo.InvariantCulture)
                + "; hand-b=" + handBBefore.ToString(CultureInfo.InvariantCulture) + "->"
                + handBAfter.ToString(CultureInfo.InvariantCulture)
                + "; attemptWindow=" + hook.DeliveredRowCount.ToString(CultureInfo.InvariantCulture)
                + " row(s) at " + DeliveryBoundaries.AfterDelivery));

            // ---------------------------------------------------------------- 07:276 — reward-drain-then-unmount
            IReadOnlyList<string> drainFields = FieldsOf(script, "reward-drain-then-unmount");
            List<string> drainVocabBefore = ReadVocabulary(world, source, installation);
            List<string> drainBefore = ReadAll(world, source, installation, drainFields);

            // (a) The scratch-migration precondition on the drained value: the registered migration now accepts its
            //     copied count of 0, where the unmount row observed the same pass refuse it (P-029).
            RewardsLifecycleResult migrated = installation.TryMigrateOutboxSlot(world.Policies!, world.TargetIds());

            // (b) The declaration's own decision: with nothing pending, the slot takes its declared
            //     `PreserveDormant` decision, which the policy plan carries as a `RetainDormant` disposition (P-032).
            RewardsLifecycleResult drained = installation.TryDrain(world.Policies!, world.TargetIds());

            // (c) The world's assembly for the lane's own O-07 publication, published with that plan: the plan's
            //     dispositions are the publication's, so the apply stage marks the retained row dormant
            //     (`AssemblyPublisher.MarkSlotDormant`). The lane published the unmount on the previous row and P-029
            //     puts the pass before the first live write, so this is where that assembly belongs.
            bool published = world.TryPublishPolicyPlan(installation.LastPolicyPlan, out string publicationDetail);

            // (d) The work itself completes: the registered job finishes, the quarantine the refused unmount admitted
            //     is released, the removal settles and the world-side lease retires (P-047, P-048).
            RewardsLifecycleResult completedWork = installation.CompletePendingWork(host, EnqueueWorkOrdinal);

            // (e) And the unmount that was refused now settles, because nothing is retained any more (07:276).
            RewardsLifecycleResult settled = installation.TryUnmount(host, world.Lane!);

            List<string> drainAfter = ReadAll(world, source, installation, drainFields);
            List<string> drainVocabAfter = ReadVocabulary(world, source, installation);
            StepRecord(trace, "reward-drain-then-unmount", drainFields, drainBefore, drainAfter);
            RecordSnapshot(trace, "reward-drain-then-unmount", ConformancePhase.Before, drainVocabBefore);
            RecordSnapshot(trace, "reward-drain-then-unmount", ConformancePhase.After, drainVocabAfter);

            var drainReadings = new RowReadings(drainFields, drainBefore, drainAfter);
            bool drainPassed = migrated.Settled
                && drained.Settled
                && published
                && completedWork.Settled
                && settled.Settled
                && installation.OutboxMigration.Invocations == 2
                && installation.OutboxMigration.Refusals == 1
                && drainReadings.Hold(ConformanceFields.OutboxOpen, "0")
                && drainReadings.Hold(ConformanceFields.OutboxAcknowledged, "1")
                && drainReadings.Require(ConformanceFields.OutboxPendingWork, "1", "0")
                && drainReadings.Require(ConformanceFields.OutboxSlotDormant, "false", "true")
                && drainReadings.Require(ConformanceFields.OutboxRetainedLeases, "1", "0")
                && drainReadings.Require(ConformanceFields.RewardsInstallationState, "mounted", "dormant")
                && drainReadings.Hold(ConformanceFields.OutboxRows, "1")
                && drainReadings.Hold(ConformanceFields.BridgePermit, "1");
            outcomes.Add(new ConformanceOracle.RowOutcomeReport("reward-drain-then-unmount", drainPassed, drainPassed
                ? string.Empty
                : "the drained outbox was not preserved dormant, or the unmount did not settle"));
            steps.Add(new ConformanceObservation(
                StepPrefix + RecordedSuffixes[7],
                drainPassed,
                RowDetail("reward-drain-then-unmount", "Published", drainReadings, drainVocabBefore, drainVocabAfter)
                + "; migrate={" + migrated.Outcome + "/" + migrated.Code + ": " + Clip(migrated.Detail, 160) + "}"
                + "; drain={" + drained.Outcome + "/" + drained.Code + ": " + Clip(drained.Detail, 160) + "}"
                + "; publication=" + Clip(publicationDetail, 200)
                + "; completedWork={" + completedWork.Outcome + "/" + completedWork.Code + ": "
                + Clip(completedWork.Detail, 160) + "}"
                + "; unmount={" + settled.Outcome + "/" + settled.Code + ": " + Clip(settled.Detail, 160) + "}"
                + "; migrationInvocations="
                + installation.OutboxMigration.Invocations.ToString(CultureInfo.InvariantCulture)
                + "; migrationRefusals="
                + installation.OutboxMigration.Refusals.ToString(CultureInfo.InvariantCulture)));

            // ---------------------------------------------------------------- 07:276 — reward-unmount-transfer
            // The script's two precondition steps: one more committed choice admits a second obligation, and its work
            // is armed, so the transfer has real pending work to carry. Both are executed in script order and recorded
            // under their own row ids, because their expectations are the transfer row's before state.
            const string preCommitRow = "reward-unmount-transfer/pre1";
            const string preArmRow = "reward-unmount-transfer/pre2";
            IReadOnlyList<string> preCommitFields = ScriptFieldsOf(script, preCommitRow);
            IReadOnlyList<string> preArmFields = ScriptFieldsOf(script, preArmRow);

            List<string> preCommitBefore = ReadAll(world, source, installation, preCommitFields);
            // The second choice names the node the conversation really sits on: the first commit moved it to the
            // permit result node, and a choice naming a stale node is refused by `NarrativeDialogueRules.Validate`.
            // The node is read from the package's own storage, never remembered by this file (P-042).
            bool nodeRead = world.TryEntity(NarrativeKeys.Mara, out Entity mara)
                && NarrativeState.TryRead(
                    host.EntityWorld.EntityManager,
                    mara,
                    NarrativeKeys.DialogueOwner,
                    NarrativeKeys.ConversationNodeSlot,
                    out int liveNode,
                    out uint _);
            ConformanceOperationResult secondCommit = nodeRead
                ? world.SubmitAndPump(
                    new CommandEnvelope(
                        world.NextOperation(),
                        NarrativeKeys.ChoiceRoute,
                        NarrativeKeys.Mara,
                        NarrativeKeys.ChoiceCommandSchema,
                        null,
                        new FrozenPayload(NarrativePayloadCodec.EncodeChoice(
                            new NarrativeChoice(liveNode, NarrativeDialogueRules.PermitChoice)))),
                    "commit-second-permit-choice")
                : new ConformanceOperationResult(
                    ConformanceOperationOutcome.Unsupported,
                    "the live conversation node could not be read, so no second choice was submitted");
            RewardBridgePassReport secondEnqueue = installation.RunBridgePass(
                world.NextOperation(), RewardEventWindow, 0);
            List<string> preCommitAfter = ReadAll(world, source, installation, preCommitFields);
            var preCommitReadings = new RowReadings(preCommitFields, preCommitBefore, preCommitAfter);
            bool preCommitPassed = secondCommit.Published
                && secondEnqueue.RewardsEnqueued == 1
                && preCommitReadings.Require(ConformanceFields.OutboxOpen, "0", "1")
                && preCommitReadings.Require(ConformanceFields.OutboxRows, "1", "2");
            StepRecord(trace, preCommitRow, preCommitFields, preCommitBefore, preCommitAfter);
            outcomes.Add(new ConformanceOracle.RowOutcomeReport(preCommitRow, preCommitPassed, preCommitPassed
                ? string.Empty
                : "the second choice did not admit one more obligation"));

            List<string> preArmBefore = ReadAll(world, source, installation, preArmFields);
            RewardsLifecycleResult secondArm = installation.ArmPendingWork(host, TransferWorkOrdinal);
            bool secondSlot = installation.WriteOutboxSlot(
                host, OutboxSlotTarget, installation.PendingWorkCount, out string secondSlotDetail);
            List<string> preArmAfter = ReadAll(world, source, installation, preArmFields);
            var preArmReadings = new RowReadings(preArmFields, preArmBefore, preArmAfter);
            bool preArmPassed = secondArm.Settled
                && secondSlot
                && preArmReadings.Require(ConformanceFields.OutboxPendingWork, "0", "1");
            StepRecord(trace, preArmRow, preArmFields, preArmBefore, preArmAfter);
            outcomes.Add(new ConformanceOracle.RowOutcomeReport(preArmRow, preArmPassed, preArmPassed
                ? string.Empty
                : "the new obligation's work was not armed with its slot value written"));

            // The destination installation: a second installation of the same declared identity over the same world,
            // which is the "restored session" the row's own prose names. It holds the carried rows, so the row's four
            // readings are its own counts rather than an inference from the source (P-045, REF-X01).
            IReadOnlyList<string> transferFields = FieldsOf(script, "reward-unmount-transfer");
            var restoredJournal = new MemoryDeliveryJournal("memory://gc024-cross-rewards-restored");
            destination = RewardsInstallation.Mount(
                host,
                world.Time!,
                source.Catalog,
                RewardCapacity,
                RewardTerminalRetention,
                OutboxDurability.Durable,
                restoredJournal,
                hook);
            List<string> transferVocabBefore = ReadVocabulary(world, destination, installation);
            List<string> transferBefore = ReadAll(world, destination, installation, transferFields);
            RewardsLifecycleResult transferred = installation.TransferOutboxTo(
                destination, world.Policies!, world.TargetIds());
            List<string> transferAfter = ReadAll(world, destination, installation, transferFields);
            List<string> transferVocabAfter = ReadVocabulary(world, destination, installation);
            StepRecord(trace, "reward-unmount-transfer", transferFields, transferBefore, transferAfter);
            RecordSnapshot(trace, "reward-unmount-transfer", ConformancePhase.Before, transferVocabBefore);
            RecordSnapshot(trace, "reward-unmount-transfer", ConformancePhase.After, transferVocabAfter);

            var transferReadings = new RowReadings(transferFields, transferBefore, transferAfter);
            bool transferPassed = preCommitPassed
                && preArmPassed
                && transferred.Outcome == RewardsLifecycleOutcome.Transferred
                && transferReadings.Require(ConformanceFields.OutboxRows, "0", "2")
                && transferReadings.Require(ConformanceFields.OutboxOpen, "0", "1")
                && transferReadings.Hold(ConformanceFields.OutboxMutations, "0")
                && transferReadings.Hold(ConformanceFields.BridgePermit, "1");
            outcomes.Add(new ConformanceOracle.RowOutcomeReport("reward-unmount-transfer", transferPassed, transferPassed
                ? string.Empty
                : "the selected compatible owner did not take the outbox rows intact"));
            steps.Add(new ConformanceObservation(
                StepPrefix + RecordedSuffixes[8],
                transferPassed,
                RowDetail("reward-unmount-transfer", "Published", transferReadings, transferVocabBefore,
                    transferVocabAfter)
                + "; preCommit={" + preCommitReadings.Render() + "} ("
                + preCommitReadings.Require(ConformanceFields.OutboxRows, "1", "2") + "/"
                + secondEnqueue.RewardsEnqueued.ToString(CultureInfo.InvariantCulture) + " enqueued)"
                + "; preArm={" + preArmReadings.Render() + "}"
                + "; arm={" + secondArm.Outcome + "/" + secondArm.Code + ": " + Clip(secondArm.Detail, 140) + "}"
                + "; slot={" + (secondSlot ? "written" : "REFUSED") + ": " + Clip(secondSlotDetail, 120) + "}"
                + "; transfer={" + transferred.Outcome + "/" + transferred.Code + ": "
                + Clip(transferred.Detail, 260) + "}"
                + "; sourceRows=" + source.Owner.Outbox.Count.ToString(CultureInfo.InvariantCulture)
                + "; sourceOpen=" + source.Owner.Outbox.OpenCount.ToString(CultureInfo.InvariantCulture)
                + "; destinationRows=" + destination.Owner.Outbox.Count.ToString(CultureInfo.InvariantCulture)
                + "; destinationOpen=" + destination.Owner.Outbox.OpenCount.ToString(CultureInfo.InvariantCulture)));

            // ---------------------------------------------------------------- 07:272 — reward-redelivery
            IReadOnlyList<string> redeliveryFields = FieldsOf(script, "reward-redelivery");
            List<string> redeliveryVocabBefore = ReadVocabulary(world, destination, installation);
            List<string> redeliveryBefore = ReadAll(world, destination, installation, redeliveryFields);

            // The same obligation again, after its acknowledgement was lost: the hook fires at `AfterDelivery`, which
            // is the exact window in which the destination has applied the effect and the obligation is not yet
            // settled, so the acknowledgement is genuinely lost and the outbox still owes it (P-045, P-149's seam).
            hook.Attach(destination.Bridge);
            hook.Arm();
            bool redelivered = false;
            string crashBoundary = DeliveryBoundaries.None;
            try
            {
                destination.RunBridgePass(default(OperationId), RewardEventWindow, RewardDispatchWindow);
            }
            catch (CrossDeliveryCrashException crash)
            {
                redelivered = true;
                crashBoundary = crash.Boundary;
            }

            List<string> redeliveryAfter = ReadAll(world, destination, installation, redeliveryFields);
            List<string> redeliveryVocabAfter = ReadVocabulary(world, destination, installation);
            StepRecord(trace, "reward-redelivery", redeliveryFields, redeliveryBefore, redeliveryAfter);
            RecordSnapshot(trace, "reward-redelivery", ConformancePhase.Before, redeliveryVocabBefore);
            RecordSnapshot(trace, "reward-redelivery", ConformancePhase.After, redeliveryVocabAfter);

            var redeliveryReadings = new RowReadings(redeliveryFields, redeliveryBefore, redeliveryAfter);
            bool redeliveryPassed = redelivered
                && string.Equals(crashBoundary, DeliveryBoundaries.AfterDelivery, StringComparison.Ordinal)
                && redeliveryReadings.Require(ConformanceFields.OutboxAlreadyApplied, "0", "1")
                && redeliveryReadings.Hold(ConformanceFields.OutboxMutations, "0")
                && redeliveryReadings.Hold(ConformanceFields.OutboxOpen, "1")
                && redeliveryReadings.Hold(ConformanceFields.OutboxRows, "2")
                && redeliveryReadings.Hold(ConformanceFields.BridgePermit, "1");
            outcomes.Add(new ConformanceOracle.RowOutcomeReport("reward-redelivery", redeliveryPassed, redeliveryPassed
                ? string.Empty
                : "the redelivery did not report AlreadyApplied while leaving the outbox open and unmutated"));
            steps.Add(new ConformanceObservation(
                StepPrefix + RecordedSuffixes[9],
                redeliveryPassed,
                RowDetail("reward-redelivery", "Published", redeliveryReadings, redeliveryVocabBefore,
                    redeliveryVocabAfter)
                + "; alreadyApplied="
                + destination.Destination.AlreadyPresentCount.ToString(CultureInfo.InvariantCulture)
                + "; mutations=" + destination.Destination.CommittedCount.ToString(CultureInfo.InvariantCulture)
                + "; submits=" + destination.Destination.SubmittedCount.ToString(CultureInfo.InvariantCulture)
                + "; crashBoundary=" + crashBoundary
                + "; windowRows=" + hook.DeliveredRowCount.ToString(CultureInfo.InvariantCulture)
                + "; hookSpent=" + hook.Spent));

            // ---------------------------------------------------------------- 07:276 — reward-scoring-unmount-keeps-card
            IReadOnlyList<string> scoringFields = FieldsOf(script, "reward-scoring-unmount-keeps-card");
            List<string> scoringVocabBefore = ReadVocabulary(world, source, installation);
            List<string> scoringBefore = ReadAll(world, source, installation, scoringFields);

            // Retracting the scoring provider is a capability change, not a gameplay effect: the issued card and the
            // committed score stay exactly as they are (P-003, P-032).
            ConformanceOperationResult scoringUnmounted = world.PublishEdit(
                CardTablePayloads.Unmount(CardTableFixture.FestivalScoringInstance), "unmount-festival-scoring");

            List<string> scoringAfter = ReadAll(world, source, installation, scoringFields);
            List<string> scoringVocabAfter = ReadVocabulary(world, source, installation);
            StepRecord(trace, "reward-scoring-unmount-keeps-card", scoringFields, scoringBefore, scoringAfter);
            RecordSnapshot(trace, "reward-scoring-unmount-keeps-card", ConformancePhase.Before, scoringVocabBefore);
            RecordSnapshot(trace, "reward-scoring-unmount-keeps-card", ConformancePhase.After, scoringVocabAfter);

            var scoringReadings = new RowReadings(scoringFields, scoringBefore, scoringAfter);
            bool scoringPassed = scoringUnmounted.Published
                && scoringReadings.Require(ConformanceFields.SeatBonus(0U), "2", ConformanceValue.None)
                && scoringReadings.Hold(ConformanceFields.RewardRecipientHandSize, "5")
                && scoringReadings.Hold(ConformanceFields.RewardHolderHandSize, "3")
                && scoringReadings.Hold(ConformanceFields.RewardRecipientTotal, "4")
                && scoringReadings.Hold(ConformanceFields.OutboxAcknowledged, "1");
            outcomes.Add(new ConformanceOracle.RowOutcomeReport(
                "reward-scoring-unmount-keeps-card", scoringPassed, scoringPassed
                    ? string.Empty
                    : "the scoring provider's removal disturbed the issued card or the committed score"));
            steps.Add(new ConformanceObservation(
                StepPrefix + RecordedSuffixes[10],
                scoringPassed,
                RowDetail("reward-scoring-unmount-keeps-card", "Published", scoringReadings, scoringVocabBefore,
                    scoringVocabAfter)
                + "; unmount={" + scoringUnmounted.Outcome + ": " + Clip(scoringUnmounted.Detail, 200) + "}"
                + "; acknowledged=" + source.Owner.AcknowledgedCount.ToString(CultureInfo.InvariantCulture)));

            narrativeModule = world.Narrative;
            cardModule = world.CardTable;
        }

        /// <summary>
        /// One row's own two readings, keyed by the field keys its table row declares, so a step's pass flag restates
        /// the row it executes and a field the run never read can never pass: a missing key compares as absent.
        /// </summary>
        private sealed class RowReadings
        {
            private readonly Dictionary<string, string> before;
            private readonly Dictionary<string, string> after;
            private readonly List<string> order = new List<string>();

            public RowReadings(IReadOnlyList<string> fields, List<string> beforeValues, List<string> afterValues)
            {
                before = new Dictionary<string, string>(fields.Count, StringComparer.Ordinal);
                after = new Dictionary<string, string>(fields.Count, StringComparer.Ordinal);
                for (int i = 0; i < fields.Count; i++)
                {
                    if (!before.ContainsKey(fields[i]))
                    {
                        order.Add(fields[i]);
                    }

                    before[fields[i]] = beforeValues[i];
                    after[fields[i]] = afterValues[i];
                }
            }

            /// <summary>True when the field really moved between two exact tokens (07's `Require`).</summary>
            public bool Require(string field, string was, string now)
                => string.Equals(Value(before, field), was, StringComparison.Ordinal)
                    && string.Equals(Value(after, field), now, StringComparison.Ordinal);

            /// <summary>True when the field reads one exact token on both sides (07's `Unchanged`).</summary>
            public bool Hold(string field, string value)
                => string.Equals(Value(before, field), value, StringComparison.Ordinal)
                    && string.Equals(Value(after, field), value, StringComparison.Ordinal);

            /// <summary>True when the row's two readings of the field are identical (07's `Preserved`).</summary>
            public bool Preserved(string field)
            {
                string? first = Value(before, field);
                string? second = Value(after, field);
                return first != null && second != null && string.Equals(first, second, StringComparison.Ordinal);
            }

            /// <summary>The row's two readings as `field=before->after`, the canonical spelling a detail carries.</summary>
            public string Render()
            {
                var parts = new List<string>(order.Count);
                for (int i = 0; i < order.Count; i++)
                {
                    parts.Add(order[i] + "=" + Value(before, order[i]) + "->" + Value(after, order[i]));
                }

                return string.Join("; ", parts.ToArray());
            }

            private static string Value(Dictionary<string, string> readings, string field)
                => readings.TryGetValue(field, out string? found) ? found : ConformanceValue.None;
        }

        /// <summary>
        /// One row's step detail: its own outcome, the row's two readings and the whole `ConformanceFields.Cross()`
        /// vocabulary on both sides of the operation, so a reviewer sees the state the assertions were made against
        /// (P-026) and the harness's own field clauses are checkable in the result JSON.
        /// </summary>
        private static string RowDetail(
            string rowId,
            string outcome,
            RowReadings readings,
            List<string> vocabBefore,
            List<string> vocabAfter)
            => ConformanceScenario.StepPrefix + Label + "/" + rowId
                + "; outcome=" + outcome
                + "; " + readings.Render()
                + "; state={" + Render(vocabBefore, vocabAfter) + "}";

        /// <summary>
        /// Reports every observation of the reward flow this run did not reach, so a failed run is red on every name
        /// it claims. The teardown, the audit and the verdict are added by `Run` itself and are never reported here.
        /// </summary>
        private static void ReportFrom(List<ConformanceObservation> steps, int firstSuffix, string reason)
        {
            for (int i = firstSuffix; i < RecordedSuffixes.Length - 3; i++)
            {
                steps.Add(new ConformanceObservation(StepPrefix + RecordedSuffixes[i], false, reason));
            }
        }

        // ------------------------------------------------------------------ trace recording

        /// <summary>
        /// The 07 row's own declared fields, in the order the fixture table declares them: the table is the document's
        /// own column set, so a row's fields are read from there (P-026).
        /// </summary>
        private static IReadOnlyList<string> FieldsOf(ConformanceScript script, string rowId)
        {
            _ = script;
            ConformanceTable? table = ReferenceTables.ById(Label);
            var fields = new List<string>();
            if (table == null)
            {
                return fields;
            }

            for (int r = 0; r < table.Rows.Count; r++)
            {
                if (!string.Equals(table.Rows[r].RowId, rowId, StringComparison.Ordinal))
                {
                    continue;
                }

                IReadOnlyList<ConformanceExpectation> expectations = table.Rows[r].Expectations;
                for (int e = 0; e < expectations.Count; e++)
                {
                    fields.Add(expectations[e].Field);
                }

                return fields;
            }

            return fields;
        }

        /// <summary>
        /// One script step's own declared fields: a precondition (`&lt;row&gt;/pre&lt;n&gt;`) carries its expectations in
        /// the script rather than in a table row, so it is read from the script that declares it (P-026).
        /// </summary>
        private static IReadOnlyList<string> ScriptFieldsOf(ConformanceScript script, string rowId)
        {
            IReadOnlyList<ConformanceStep> declared = script.Steps();
            for (int s = 0; s < declared.Count; s++)
            {
                if (!string.Equals(declared[s].RowId, rowId, StringComparison.Ordinal))
                {
                    continue;
                }

                var fields = new List<string>(declared[s].Expectations.Count);
                for (int e = 0; e < declared[s].Expectations.Count; e++)
                {
                    fields.Add(declared[s].Expectations[e].Field);
                }

                return fields;
            }

            return new List<string>();
        }

        /// <summary>
        /// One row's declared fields on one side of its operation. `current` is the installation whose outbox and
        /// destination counters the `outbox.*` fields read — the source for the reward's own life, the destination
        /// for the rows that assert at the owner which now holds the carried obligation — and `slotOwner` is the
        /// installation the declared outbox slot and the world's resource ledger belong to (P-034, P-045).
        /// </summary>
        private static List<string> ReadAll(
            CrossWorld world,
            RewardsInstallation current,
            RewardsInstallation slotOwner,
            IReadOnlyList<string> fields)
        {
            var values = new List<string>(fields.Count);
            for (int i = 0; i < fields.Count; i++)
            {
                values.Add(ReadField(world, current, slotOwner, fields[i]));
            }

            return values;
        }

        private static void StepRecord(
            ConformanceTrace trace,
            string rowId,
            IReadOnlyList<string> fields,
            IReadOnlyList<string> before,
            IReadOnlyList<string> after)
        {
            for (int i = 0; i < fields.Count; i++)
            {
                trace.Record(Label, rowId, ConformancePhase.Before, fields[i], before[i]);
                trace.Record(Label, rowId, ConformancePhase.After, fields[i], after[i]);
            }
        }

        /// <summary>
        /// The whole `ConformanceFields.Cross()` vocabulary read once, in the vocabulary's own canonical order, so a
        /// row's `/state` snapshot and the row's detail render the same reading (P-026).
        /// </summary>
        private static List<string> ReadVocabulary(
            CrossWorld world, RewardsInstallation current, RewardsInstallation slotOwner)
        {
            IReadOnlyList<ConformanceField> declared = ConformanceFields.Cross();
            var values = new List<string>(declared.Count);
            for (int f = 0; f < declared.Count; f++)
            {
                values.Add(ReadField(world, current, slotOwner, declared[f].Key));
            }

            return values;
        }

        /// <summary>
        /// Records one vocabulary reading under the row's own `/state` id, so it never competes with the row's own
        /// assertions: the trace carries the state the assertions were made against, not only the assertions (P-026).
        /// </summary>
        private static void RecordSnapshot(
            ConformanceTrace trace, string rowId, ConformancePhase phase, List<string> values)
        {
            IReadOnlyList<ConformanceField> declared = ConformanceFields.Cross();
            for (int f = 0; f < declared.Count && f < values.Count; f++)
            {
                trace.TryRecord(Label, rowId + "/state", phase, declared[f].Key, values[f]);
            }
        }

        /// <summary>One vocabulary reading's two sides as `key=before->after`, in the vocabulary's canonical order.</summary>
        private static string Render(List<string> before, List<string> after)
        {
            IReadOnlyList<ConformanceField> declared = ConformanceFields.Cross();
            var parts = new List<string>(declared.Count);
            for (int f = 0; f < declared.Count && f < before.Count && f < after.Count; f++)
            {
                parts.Add(declared[f].Key + "=" + before[f] + "->" + after[f]);
            }

            return string.Join("; ", parts.ToArray());
        }

        // ------------------------------------------------------------------ reading the real world

        /// <summary>
        /// One canonical field of `ConformanceFields.Cross()`, read from the live world: the durable fact and the
        /// conversation through the narrative package's own state accessor (P-034), the outbox counts from the
        /// installation that owns them and its destination port (P-045), the installation's declared slot through the
        /// reward package's own slot API (`TryReadOutboxSlot`, P-032), the retained leases from the world's resource
        /// ledger (P-048), and the card table's hands, scores and derived bonus rows through the card package's own
        /// accessors (P-017, P-032). A field this world cannot read is `none` with no guessed value.
        /// </summary>
        private static string ReadField(
            CrossWorld world,
            RewardsInstallation current,
            RewardsInstallation slotOwner,
            string field)
        {
            switch (field)
            {
                case ConformanceFields.BridgePermit:
                    return Owned(world, NarrativeKeys.QuestLedger, NarrativeKeys.QuestOwner,
                        NarrativeKeys.BridgePermitValueSlot);
                case ConformanceFields.BridgePermitVersion:
                    return Owned(world, NarrativeKeys.QuestLedger, NarrativeKeys.QuestOwner,
                        NarrativeKeys.BridgePermitVersionSlot);
                case ConformanceFields.MaraConversationStatus:
                    return Owned(world, NarrativeKeys.Mara, NarrativeKeys.DialogueOwner,
                        NarrativeKeys.ConversationStatusSlot);
                case ConformanceFields.OutboxRecognised:
                    return Int(current.Bridge.RecognisedCount);
                case ConformanceFields.OutboxOpen:
                    return Int(current.Owner.Outbox.OpenCount);
                case ConformanceFields.OutboxAcknowledged:
                    return Int(current.Owner.AcknowledgedCount);
                case ConformanceFields.OutboxAlreadyApplied:
                    return Int(current.Bridge.Destination.AlreadyPresentCount);
                case ConformanceFields.OutboxMutations:
                    return Int(current.Bridge.Destination.CommittedCount);
                case ConformanceFields.OutboxRows:
                    return Int(current.Owner.Outbox.Count);
                case ConformanceFields.OutboxPendingWork:
                    return TryReadSlot(world, slotOwner, out int pendingWork, out bool _, out string _)
                        ? Int(pendingWork)
                        : ConformanceValue.None;
                case ConformanceFields.OutboxSlotDormant:
                    return TryReadSlot(world, slotOwner, out int _, out bool dormant, out string _)
                        ? ConformanceValue.Bool(dormant)
                        : ConformanceValue.None;
                case ConformanceFields.RewardsInstallationState:
                    return InstallationState(world, slotOwner);
                case ConformanceFields.OutboxRetainedLeases:
                    return Int(RetainedLeases(world, slotOwner));
                case ConformanceFields.RewardRecipientHand:
                    return Hand(world, CardTableKeys.SeatAOrdinal);
                case ConformanceFields.RewardHolderHand:
                    return Hand(world, CardTableKeys.SeatBOrdinal);
                case ConformanceFields.RewardRecipientHandSize:
                    return Int(HandSize(world, CardTableKeys.SeatAOrdinal));
                case ConformanceFields.RewardHolderHandSize:
                    return Int(HandSize(world, CardTableKeys.SeatBOrdinal));
                case ConformanceFields.RewardRecipientTotal:
                    return Score(world, CardTableKeys.SeatAOrdinal);
                case ConformanceFields.RewardHolderTotal:
                    return Score(world, CardTableKeys.SeatBOrdinal);
                case ConformanceFields.RewardTableVersion:
                    return world.TableVersion();
                case ConformanceFields.WorldStep:
                    return ConformanceValue.UInt(world.Host!.CurrentStep.Value);
                default:
                    // The scoring row's own field: the seat's effective `cards.set-bonus` value, read from the
                    // published binding rows the card package derives it from, exactly as the card market's own
                    // conformance host reads it (07 s2, P-019).
                    if (string.Equals(field, ConformanceFields.SeatBonus(0U), StringComparison.Ordinal))
                    {
                        return Bonus(world, CardTableKeys.SeatAOrdinal);
                    }

                    // A key this world owns no reading for is a recorded absence with no guessed value: an
                    // unobserved expectation then fails instead of passing for the wrong reason (P-026).
                    return ConformanceValue.None;
            }
        }

        /// <summary>
        /// The installation's own state as the row's `outbox.installation` token: `dormant` when its declared outbox
        /// row is retained with no active writer, `mounted` while the lane still records it un-disposed, and `none`
        /// once the lane has disposed it with nothing retained (P-032, P-046).
        /// </summary>
        private static string InstallationState(CrossWorld world, RewardsInstallation installation)
        {
            if (TryReadSlot(world, installation, out int _, out bool dormant, out string _) && dormant)
            {
                return "dormant";
            }

            if (world.Lane != null
                && world.Lane.Committed.TryGetInstall(installation.Instance, out InstallEntry? entry)
                && entry != null
                && entry.State != InstallationState.Disposed)
            {
                return "mounted";
            }

            return ConformanceValue.None;
        }

        /// <summary>
        /// One live outbox row through the reward package's own reader. `TryGetSlotTarget` is the installation's own
        /// answer for where its declared row lives, and a target without a row is a reported miss rather than a zero
        /// that was never stored (P-032, P-045).
        /// </summary>
        private static bool TryReadSlot(
            CrossWorld world,
            RewardsInstallation installation,
            out int pendingWork,
            out bool dormant,
            out string detail)
        {
            pendingWork = 0;
            dormant = false;
            if (world.Host == null)
            {
                detail = "the world has no host";
                return false;
            }

            if (!installation.TryGetSlotTarget(out TargetId target, out detail))
            {
                return false;
            }

            return installation.TryReadOutboxSlot(world.Host, target, out pendingWork, out dormant, out detail);
        }

        /// <summary>
        /// How many of this installation's resource leases the world's own ledger still retains: the number that
        /// makes an unmount refusal `TeardownBlocked` rather than a false `Disposed`, and `0` after a settled
        /// teardown (P-047, P-048). `WorldResourceLedger` reports retained records with their owning instance and
        /// state, so the count is a count of the ledger's own rows and not a remembered number.
        /// </summary>
        private static int RetainedLeases(CrossWorld world, RewardsInstallation installation)
        {
            if (world.Host == null)
            {
                return 0;
            }

            WorldResourceLedgerSnapshot snapshot = world.Host.ReadResourceLedger();
            int retained = 0;
            for (int i = 0; i < snapshot.Resources.Count; i++)
            {
                WorldResourceRecord record = snapshot.Resources[i];
                if (record.Instance.Equals(installation.Instance) && record.IsRetained)
                {
                    retained++;
                }
            }

            return retained;
        }

        /// <summary>
        /// One seat's effective set-bonus value: the active `cards.set-bonus` binding row the published assembly
        /// carries for the seat's target, or `none` when no provider supports it any more (07 s2, P-017, P-019).
        /// </summary>
        private static string Bonus(CrossWorld world, uint ordinal)
        {
            if (world.Publisher == null)
            {
                return ConformanceValue.None;
            }

            TargetId target = CardTableFixture.SeatTarget(ordinal);
            IReadOnlyList<CapabilityBinding> rows = world.Publisher.ReadBindingRows(target);
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].IsActive
                    && rows[i].OutputSlot == 0U
                    && rows[i].Capability.Equals(CardVocabulary.SetBonusCapability))
                {
                    return Int(rows[i].Value);
                }
            }

            return ConformanceValue.None;
        }

        private static string Owned(CrossWorld world, TargetId target, OwnerId owner, SlotId slot)
        {
            if (!world.TryEntity(target, out Entity entity))
            {
                return ConformanceValue.None;
            }

            return NarrativeState.TryRead(
                    world.Host!.EntityWorld.EntityManager, entity, owner, slot, out int value, out uint _)
                ? Int(value)
                : ConformanceValue.None;
        }

        private static string Hand(CrossWorld world, uint ordinal)
        {
            if (!world.TrySeat(ordinal, out Entity seat))
            {
                return ConformanceValue.None;
            }

            CardHand hand = CardTableAccess.ReadHand(world.Host!.EntityWorld.EntityManager, seat, ordinal);
            var cards = new List<string>(hand.Count);
            for (int i = 0; i < hand.Count; i++)
            {
                cards.Add("c" + hand.Card(i).Value.ToString(CultureInfo.InvariantCulture));
            }

            cards.Sort(StringComparer.Ordinal);
            return ConformanceValue.Set(cards);
        }

        private static int HandSize(CrossWorld world, uint ordinal)
        {
            if (!world.TrySeat(ordinal, out Entity seat))
            {
                return 0;
            }

            return CardTableAccess.ReadHand(world.Host!.EntityWorld.EntityManager, seat, ordinal).Count;
        }

        private static string Score(CrossWorld world, uint ordinal)
        {
            if (!world.TrySeat(ordinal, out Entity seat))
            {
                return ConformanceValue.None;
            }

            EntityManager entityManager = world.Host!.EntityWorld.EntityManager;
            if (!entityManager.Exists(seat) || !entityManager.HasComponent<CardSeatState>(seat))
            {
                return ConformanceValue.None;
            }

            return Int(entityManager.GetComponentData<CardSeatState>(seat).Score);
        }

        private static string Int(int value) => ConformanceValue.Int(value);

        private static string Clip(string text, int limit)
            => text.Length <= limit ? text : text.Substring(0, limit);

        // ------------------------------------------------------------------ the merged world's parts

        /// <summary>
        /// The two families' own manifest declarations, which is the set this world compiles and registers. It is the
        /// ownership and schedule surface the world really runs: see <see cref="LaneDeclarations"/> for why the reward
        /// installation's declaration is not part of it. `Gc013CardsHost.Declarations()` already carries
        /// `CardTableFixture.Declarations()` as its first four entries, so adding that set a second time would be a
        /// duplicate declaration rather than a union (P-009).
        /// </summary>
        private static List<CatalogPluginDeclaration> FamilyDeclarations()
        {
            var merged = new List<CatalogPluginDeclaration>(
                Gc013NarrativeHost.Declarations(
                    NarrativeScenarioCatalog.PluginFactoryKey, NarrativeScenarioCatalog.RecordSchema));
            merged.AddRange(Gc013CardsHost.Declarations());
            return merged;
        }

        /// <summary>
        /// The declarations the LANE resolves manifests from: the two families' own set plus 07 s5's
        /// `NarrativeCardRewards` declaration. The installation is a real install of this world's composition — its
        /// mount and unmount are lane publications, its slot's last-support policy and its registered migration are
        /// read from this manifest — while the compiled ownership schedule the world registers stays the families' own
        /// set, because a compiled system entry needs exactly one host registration
        /// (`CompiledScheduleAdapter.Registrations`) and this plugin's runtime is the mounted installation rather
        /// than a host system (07 s5, P-009, P-032).
        /// </summary>
        private static List<CatalogPluginDeclaration> LaneDeclarations()
        {
            List<CatalogPluginDeclaration> merged = FamilyDeclarations();
            merged.Add(RewardsDeclaration.Declaration());
            return merged;
        }

        /// <summary>
        /// The union catalog every declaration of this world resolves through. The three tables have disjoint keys, so
        /// the combined world carries the union of their registrations: a declaration whose factory key or
        /// configuration schema resolved in its own package's world must resolve here too (P-009, P-028).
        /// </summary>
        private static ImmutableCatalog BuildCombinedCatalog(out string detail)
        {
            var factories = new List<FactoryRegistration>(NarrativeScenarioCatalog.Factories());
            factories.AddRange(CardCatalogTable.Factories());
            factories.AddRange(RewardsCatalog.Factories());
            var schemas = new List<SchemaRegistration>(NarrativeScenarioCatalog.Schemas());
            schemas.AddRange(CardCatalogTable.Schemas());
            schemas.AddRange(RewardsCatalog.Schemas());
            var serializers = new List<ISchemaSerializer>(NarrativeScenarioCatalog.Serializers);
            serializers.AddRange(CardCatalogTable.Serializers());
            serializers.AddRange(RewardsCatalog.Serializers());
            var features = new List<Id128>(NarrativeScenarioCatalog.SupportedFeatureIds);
            features.AddRange(CardCatalogTable.SupportedFeatureIds);
            features.AddRange(RewardsCatalog.SupportedFeatureIds);

            CatalogBuildResult built = ImmutableCatalog.Build(factories, schemas, features, serializers);
            if (built.Catalog == null)
            {
                detail = "the union of the three catalogs was rejected: " + built.Describe();
                throw new InvalidOperationException(detail);
            }

            detail = "factories=" + factories.Count.ToString(CultureInfo.InvariantCulture)
                + "; schemas=" + schemas.Count.ToString(CultureInfo.InvariantCulture)
                + "; fingerprint=" + built.Catalog.Fingerprint.ToHex();
            return built.Catalog;
        }

        /// <summary>
        /// The merged dispatch-kind resolver: both families' generated tables, answering from either. Every compiled
        /// system key must resolve, or `OwnershipSchedulePipeline.Build` returns an adaptation witness instead of a
        /// descriptor (P-028, 04 s8).
        /// </summary>
        private sealed class MergedDispatchKinds : IScheduleDispatchKindResolver
        {
            private readonly ScheduleDispatchKindTable narrative;
            private readonly ScheduleDispatchKindTable cards;
            private readonly ScheduleDispatchKindTable rewards;

            public MergedDispatchKinds(
                ScheduleDispatchKindTable narrative,
                ScheduleDispatchKindTable cards,
                ScheduleDispatchKindTable rewards)
            {
                this.narrative = narrative;
                this.cards = cards;
                this.rewards = rewards;
            }

            public bool TryResolveKind(FactoryKey systemKey, out SystemDispatchKind kind)
                => narrative.TryResolveKind(systemKey, out kind)
                    || cards.TryResolveKind(systemKey, out kind)
                    || rewards.TryResolveKind(systemKey, out kind);

            public int Count => narrative.Count + cards.Count + rewards.Count;
        }
        /// <summary>The narrative slice's own dispatch-kind table, as its family's `CompilePipeline` declares it.</summary>
        private static ScheduleDispatchKindTable NarrativeDispatchKinds() =>
            new ScheduleDispatchKindTable()
                .Add(NarrativeKeys.InputSystem, SystemDispatchKind.ManagedSystem)
                .Add(NarrativeKeys.DialogueSystem, SystemDispatchKind.ManagedSystem)
                .Add(NarrativeKeys.QuestSystem, SystemDispatchKind.ManagedSystem)
                .Add(NarrativeKeys.GateSystem, SystemDispatchKind.ManagedSystem)
                .Add(NarrativeKeys.EncounterSystem, SystemDispatchKind.ManagedSystem)
                .Add(NarrativeKeys.OutputSystem, SystemDispatchKind.ManagedSystem);

        /// <summary>
        /// The reward installation's own two declared systems (`rewards.enqueue`, `rewards.ack`). They resolve so the
        /// merged schedule compiles with the plugin's declaration present, and they are managed systems because
        /// 07 s5 defines the plugin's runtime as the mounted installation rather than as an unmanaged ECS entry.
        /// </summary>
        private static ScheduleDispatchKindTable RewardsDispatchKinds() =>
            new ScheduleDispatchKindTable()
                .Add(RewardsKeys.EnqueueSystem, SystemDispatchKind.ManagedSystem)
                .Add(RewardsKeys.AckSystem, SystemDispatchKind.ManagedSystem);

        /// <summary>
        /// The combined world's compiled ownership and schedule descriptor: the merged manifest set through the real
        /// GC-007 validator and GC-009 compiler, with the merged dispatch-kind resolver (P-040).
        /// </summary>
        private static PipelineDescriptorReport BuildMergedDescriptor(
            IReadOnlyList<CatalogPluginDeclaration> declarations)
        {
            var manifests = new List<PluginManifest>(declarations.Count);
            for (int i = 0; i < declarations.Count; i++)
            {
                manifests.Add(declarations[i].Manifest);
            }

            return OwnershipSchedulePipeline.Build(
                manifests,
                new MergedDispatchKinds(
                    NarrativeDispatchKinds(), CardTableRegistration.DispatchKinds(), RewardsDispatchKinds()),
                new SlotMigrationRegistry());
        }

        /// <summary>Both families' scope trees, remapped onto the combined root (P-010).</summary>
        private static List<ScopeRecord> MergedScopes()
        {
            var scopes = new List<ScopeRecord>();
            IReadOnlyList<ScopeRecord> narrative = NarrativeScopes.DeclaredChildren();
            for (int i = 0; i < narrative.Count; i++)
            {
                ScopeRecord record = narrative[i];
                bool atRoot = record.Parent.Equals(NarrativeKeys.RootScope);
                scopes.Add(new ScopeRecord(
                    record.Scope,
                    atRoot ? CrossRootScope : record.Parent,
                    atRoot ? 1 : 2,
                    record.ServiceIsolation,
                    record.CapabilityIsolation,
                    record.Exclusions,
                    record.Grants));
            }

            IReadOnlyList<CompositionEditPayload> creates = CardMarketComposition.ScopeCreates();
            for (int i = 0; i < creates.Count; i++)
            {
                CompositionEditPayload payload = creates[i];
                if (payload.Subject != CompositionEditSubject.ScopeCreate)
                {
                    continue;
                }

                bool atRoot = payload.Parent.Equals(CardMarketComposition.MatchScope);
                scopes.Add(new ScopeRecord(
                    payload.Scope,
                    atRoot ? CrossRootScope : payload.Parent,
                    atRoot ? 1 : 2,
                    payload.ServiceIsolation,
                    payload.CapabilityIsolation,
                    payload.Exclusions,
                    null));
            }

            return scopes;
        }

        /// <summary>
        /// Both families' planes as one: the routes and buffers are unioned, the retained-event bound is raised to
        /// <see cref="MergedRetainedEvents"/> because the reward path reads several committed events per pass, and
        /// every other bound is the larger of the two (P-043, P-045).
        /// </summary>
        private static MessagePlaneRegistration MergedMessages()
        {
            MessagePlaneRegistration narrative = NarrativeRegistration.Messages();
            MessagePlaneRegistration cards = CardTableRegistration.Messages();

            var routes = new List<CommandRoute>(narrative.Routes);
            routes.AddRange(cards.Routes);
            var buffers = new List<MessageBufferDescriptor>(narrative.Buffers);
            buffers.AddRange(cards.Buffers);

            return new MessagePlaneRegistration(
                routes,
                buffers,
                null,
                Math.Max(narrative.MaxPendingRequests, cards.MaxPendingRequests),
                Math.Max(narrative.MaxRetainedResults, cards.MaxRetainedResults),
                Math.Max(MergedRetainedEvents, Math.Max(narrative.MaxRetainedEvents, cards.MaxRetainedEvents)),
                Math.Max(narrative.MaxEventsPerStep, cards.MaxEventsPerStep),
                (ushort)Math.Max(narrative.NextStepCapacity, cards.NextStepCapacity));
        }

        /// <summary>Both families' typed readers bound onto ONE reader set, by schema (04 s8).</summary>
        private static CommandPayloadReaders MergedReaders()
        {
            var readers = new CommandPayloadReaders();
            Bind(readers, new NarrativeChoicePayloadReader());
            Bind(readers, new NarrativeMutationPayloadReader());
            Bind(readers, new NarrativeObservationPayloadReader());
            Bind(readers, new CardCommandReader());
            Bind(readers, new CardBatchReader());
            return readers;
        }

        private static void Bind<T>(CommandPayloadReaders readers, ICommandPayloadReader<T> reader)
        {
            if (!readers.TryBind(reader, out string failure))
            {
                throw new InvalidOperationException(
                    "binding a merged payload reader was refused: " + failure + " (04 s8).");
            }
        }

        /// <summary>Both families' systems, unioned into one registration (P-039).</summary>
        private static List<SystemRegistration> MergedSystems()
        {
            var systems = new List<SystemRegistration>(NarrativeRegistration.Systems());
            systems.AddRange(CardTableRegistration.Systems());
            return systems;
        }

        /// <summary>Both families' recipes in one catalog, and both families' slot migrations in one registry.</summary>
        private static SpawnRecipeCatalog MergedRecipes()
        {
            var recipes = new List<SpawnRecipe>(NarrativeRecipes.Catalog(new NarrativeRecipeApplier()).Recipes);
            recipes.AddRange(CardTableRecipes.Catalog(new CardSeatApplier(), new MarketTableApplier()).Recipes);
            return new SpawnRecipeCatalog(recipes);
        }

        /// <summary>
        /// Both families' slot migrations plus the reward installation's registered v1 -> v2 outbox migration, so the
        /// registry the planner and the assembly publisher resolve through carries the handler the declaration names
        /// (a missing handler is a `MigrationRequired` refusal, P-032).
        /// </summary>
        private static MigrationRegistry MergedMigrations() =>
            new MigrationRegistry(new List<ISlotMigration>
            {
                new NarrativeConversationNodeMigration(),
                new NarrativeConversationStatusMigration(),
                new RewardsOutboxPreconditionMigration(),
            });

        /// <summary>
        /// The merged derivation value source: the narrative fixture's registered reducer and always-predicate plus
        /// the card market's registered `cards.set-bonus` reducer and its own predicate. A key neither family
        /// registers is a miss, exactly as it is in either family's own world (P-009, P-019).
        /// </summary>
        private sealed class MergedValueSource : IDerivationValueSource
        {
            private readonly IDerivationValueSource narrative;
            private readonly IDerivationValueSource cards;

            public MergedValueSource(IDerivationValueSource narrative, IDerivationValueSource cards)
            {
                this.narrative = narrative;
                this.cards = cards;
            }

            public bool IsReductionRegistered(FactoryKey reducer)
                => narrative.IsReductionRegistered(reducer) || cards.IsReductionRegistered(reducer);

            public bool IsPredicateRegistered(FactoryKey predicate)
                => narrative.IsPredicateRegistered(predicate) || cards.IsPredicateRegistered(predicate);

            public bool TryReduce(FactoryKey reducer, IReadOnlyList<FrozenPayload> inputs, out FrozenPayload? result)
                => narrative.TryReduce(reducer, inputs, out result) || cards.TryReduce(reducer, inputs, out result);

            public bool TryEvaluate(FactoryKey predicate, DerivationPredicateContext context, out bool result)
                => narrative.TryEvaluate(predicate, context, out result) || cards.TryEvaluate(predicate, context, out result);
        }

        // ------------------------------------------------------------------ the combined world

        /// <summary>
        /// The combined world itself: one `UnityWorldHost` created from the merged registration, one lane joined to
        /// its initial assembly with both families' scope trees, both families' targets seeded into the SAME
        /// `(host, targets, seeder)`, and BOTH modules attached. It mirrors `ConformanceWorld`'s module chain and
        /// `ConformanceWorld.PublishEdit`'s publication pairing, because a combined world is not one family's world
        /// and cannot be built through one family's `ConformanceWorld.Build`.
        /// </summary>
        private sealed class CrossWorld : IDisposable
        {
            private readonly IdSequence sessions;
            private readonly int targetCapacity;
            private ulong operationSequence;
            private ulong hostTicks;

            public CrossWorld(IdSequence sessions, int targetCapacity)
            {
                this.sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
                this.targetCapacity = targetCapacity;
                try
                {
                    Create();
                }
                catch (Exception exception)
                {
                    Failure = exception.GetType().FullName + ": " + exception.Message;
                }
            }

            public bool Ready { get; private set; }

            public string Failure { get; private set; } = string.Empty;

            public ImmutableCatalog? CombinedCatalog { get; private set; }

            /// <summary>
            /// The declarations the lane resolves manifests from (the two families' set plus 07 s5's reward
            /// installation), which is also the manifest set the world's state-policy catalog is built from.
            /// </summary>
            public IReadOnlyList<CatalogPluginDeclaration> Declarations { get; private set; } =
                Array.Empty<CatalogPluginDeclaration>();

            public UnityWorldHost? Host { get; private set; }

            public CompositionHost? Lane { get; private set; }

            public AssemblyPublisher? Publisher { get; private set; }

            public DerivedAssemblyPipeline? Pipeline { get; private set; }

            public WorldTimeDriver? Time { get; private set; }

            public LiveTargetIndex? Targets { get; private set; }

            public LiveTargetSeeder? Seeder { get; private set; }

            public PipelineDescriptorReport? Descriptor { get; private set; }

            public UnityWorldRegistration? Registration { get; private set; }

            public NarrativeModule? Narrative { get; private set; }

            public CardTableModule? CardTable { get; private set; }

            public string CatalogDetail { get; private set; } = string.Empty;

            /// <summary>
            /// One composition edit plus the world's assembly publication for it, exactly as
            /// `ConformanceWorld.PublishEdit` pairs the two: P-006 has one publication series, so an edit the world
            /// does not answer would leave the lane one publication ahead and every later adoption would be stale.
            /// </summary>
            public ConformanceOperationResult PublishEdit(CompositionEditPayload payload, string label)
            {
                if (Host == null || Lane == null || Pipeline == null || Publisher == null)
                {
                    return new ConformanceOperationResult(
                        ConformanceOperationOutcome.Unsupported, label + ": the world or its pipeline is missing");
                }

                OperationId operation = NextOperation();
                EditAdmission admission = Lane.SubmitEdit(payload, operation, Lane.Committed.Revision);
                if (!admission.Staged)
                {
                    return new ConformanceOperationResult(
                        ConformanceOperationOutcome.Refused,
                        label + ": the lane refused the edit (" + admission.Kind + "/"
                        + DiagnosticCodeText.Of(admission.Code) + ")");
                }

                IReadOnlyList<PublishedOperation> published = Lane.Drain();
                if (published.Count == 0 || published[0].Outcome == Outcome.Rejected)
                {
                    return new ConformanceOperationResult(
                        ConformanceOperationOutcome.Refused,
                        label + ": the publication was refused ("
                        + (published.Count > 0
                            ? published[0].Outcome + "/" + DiagnosticCodeText.Of(published[0].Code)
                            : "none")
                        + ")");
                }

                DerivedAssemblyReport report = Pipeline.PublishDerived(operation);
                if (report.Outcome == DerivedAssemblyOutcome.Refused)
                {
                    return new ConformanceOperationResult(
                        ConformanceOperationOutcome.Refused,
                        label + ": the world refused the assembly: " + report.Describe());
                }

                if (report.Outcome == DerivedAssemblyOutcome.NoTargetChange)
                {
                    AssemblyPublicationReport unchanged = Publisher.PublishUnchangedAssembly(
                        NextOperation(), Lane.Committed.Revision, Lane.Committed.Epoch);
                    if (!unchanged.Published)
                    {
                        return new ConformanceOperationResult(
                            ConformanceOperationOutcome.Unsupported,
                            label + ": the unchanged assembly publication was refused: " + unchanged.Detail);
                    }
                }

                if (!MatchesPublishedAssembly())
                {
                    return new ConformanceOperationResult(
                        ConformanceOperationOutcome.Unsupported,
                        label + ": the lane and the world's assembly counters disagree (P-006)");
                }

                return new ConformanceOperationResult(ConformanceOperationOutcome.Published, label);
            }

            /// <summary>One typed command through the world's own port plus one command-driven step (P-036, P-042).</summary>
            public ConformanceOperationResult SubmitAndPump(CommandEnvelope envelope, string label)
            {
                if (Host == null || Time == null)
                {
                    return new ConformanceOperationResult(
                        ConformanceOperationOutcome.Unsupported, label + ": the world or its clock is missing");
                }

                CommandAdmissionReceipt receipt = Host.Submit(envelope);
                if (!receipt.Admitted)
                {
                    return new ConformanceOperationResult(
                        ConformanceOperationOutcome.Refused,
                        label + ": the command was not admitted (" + receipt.Result.Kind + "/"
                        + DiagnosticCodeText.Of(receipt.Result.Reason) + ")");
                }

                hostTicks += IdlePumpTicks;
                TimeFrameReport frame = Time.PumpFrame(hostTicks);
                if (frame.StepsCommitted == 0UL)
                {
                    return new ConformanceOperationResult(
                        ConformanceOperationOutcome.Refused,
                        label + ": the pump committed no step (code=" + DiagnosticCodeText.Of(frame.Pump.Code) + ")");
                }

                return new ConformanceOperationResult(
                    ConformanceOperationOutcome.Published,
                    label + ": committed "
                    + frame.StepsCommitted.ToString(CultureInfo.InvariantCulture) + " step(s)");
            }

            public bool MatchesPublishedAssembly()
            {
                if (Lane == null || Publisher == null || Host == null)
                {
                    return false;
                }

                return AssemblyPublisher.MatchesPublishedAssembly(
                    Lane.Committed.Revision, Lane.Committed.Epoch, Publisher.PublishedRevision, Host.CurrentEpoch);
            }
            /// <summary>The world's declared state-policy surface, and the pipeline one policy pass runs over it.</summary>
            public StatePolicyCatalog? PolicyCatalog { get; private set; }

            public StateMigrationPipeline? Policies { get; private set; }

            /// <summary>Every live target of this world, in the order the seeder registered them.</summary>
            public IReadOnlyList<TargetId> TargetIds()
            {
                IReadOnlyList<LiveTarget> live = Targets == null ? Array.Empty<LiveTarget>() : Targets.Targets;
                var ids = new List<TargetId>(live.Count);
                for (int i = 0; i < live.Count; i++)
                {
                    ids.Add(live[i].Target);
                }

                return ids;
            }

            /// <summary>One line naming the lane's record of the reward installation, for a step's detail (P-046).</summary>
            public string LaneInstallState()
            {
                if (Lane == null)
                {
                    return "no lane";
                }

                return Lane.Committed.TryGetInstall(RewardsKeys.Installation, out InstallEntry? entry) && entry != null
                    ? entry.State + "/retained="
                        + Lane.Resources.RetainedCountFor(RewardsKeys.Installation)
                            .ToString(CultureInfo.InvariantCulture)
                    : "not installed";
            }

            /// <summary>
            /// Publishes the world's assembly for the composition publication the lane already committed, with the
            /// executed state-policy plan as its own: the plan's dispositions are the publication's, so a
            /// `RetainDormant` decision marks the retained outbox row dormant in the same fence (P-029, P-032). A
            /// refused pass is reported with its code and publishes nothing.
            /// </summary>
            public bool TryPublishPolicyPlan(StatePolicyPlan? plan, out string detail)
            {
                detail = string.Empty;
                if (Host == null || Lane == null || Publisher == null || Targets == null || Seeder == null)
                {
                    detail = "the world is missing a part, so no policy publication can be made";
                    return false;
                }

                if (plan == null)
                {
                    detail = "no state-policy pass was executed, so there is no plan to publish";
                    return false;
                }

                if (!plan.Succeeded)
                {
                    detail = "the state-policy pass was refused (" + DiagnosticCodeText.Of(plan.Code) + "): "
                        + plan.Detail;
                    return false;
                }

                if (!Publisher.TryAdoptLanePublication(
                        Lane.Committed.Revision,
                        Lane.Committed.Epoch,
                        out AssemblyEpoch _,
                        out DiagnosticCode adoptCode))
                {
                    detail = "the publisher refused to adopt the lane's committed publication "
                        + Lane.Committed.Revision.Value.ToString(CultureInfo.InvariantCulture) + "/"
                        + Lane.Committed.Epoch.Value.ToString(CultureInfo.InvariantCulture)
                        + " (" + DiagnosticCodeText.Of(adoptCode) + ")";
                    return false;
                }

                OperationId operation = NextOperation();
                var proposal = new PlanningCompositionProposal(
                    operation,
                    ContentHash.Empty,
                    Publisher.PublishedRevision,
                    Host.CurrentEpoch,
                    ContentHash.Empty,
                    Lane.Committed.Mode,
                    null,
                    null);
                PlannedPublication planned = AssemblyPlanner.Build(
                    proposal,
                    Publisher.Descriptor,
                    Publisher.PublishedRevision,
                    Host.CurrentEpoch,
                    Publisher.Published.Bindings,
                    Publisher.Published.Rules,
                    Targets.PlannerTargets(),
                    Seeder.ReadLiveSlots(TargetIds()),
                    Publisher.Migrations,
                    new MigrationScratch(ScratchCapacityBytes, ScratchBytesPerSlot),
                    new InertAcquisitionSet(
                        new StagedResourceGate(StagedByteCeiling, NarrativeKeys.Issuer), operation),
                    new PlanBudget(PrepareBytesLimit, PrepareBytesLimit, ScratchCapacityBytes, ScratchBytesPerSlot),
                    plan);
                if (!planned.IsPrepared)
                {
                    detail = "the plan for the policy publication is " + planned.State.Phase + ": "
                        + planned.State.Code + ": " + planned.State.Detail;
                    return false;
                }

                AssemblyPublicationReport publication = Publisher.Publish(planned);
                detail = "policyPublication=" + publication.Outcome + "/" + publication.Code
                    + ", dispositions=" + planned.Dispositions.Count.ToString(CultureInfo.InvariantCulture)
                    + ", migrations=" + planned.Migrations.Count.ToString(CultureInfo.InvariantCulture)
                    + ", detail=" + Clip(publication.Detail, 160);
                return publication.Published;
            }

            /// <summary>The manifests of the declarations this world's lane resolves (P-009, P-032).</summary>
            private IReadOnlyList<PluginManifest> LaneManifests()
            {
                var manifests = new List<PluginManifest>(Declarations.Count);
                for (int i = 0; i < Declarations.Count; i++)
                {
                    manifests.Add(Declarations[i].Manifest);
                }

                return manifests;
            }

            public OperationId NextOperation()
            {
                operationSequence++;
                return new OperationId(Host!.World, NarrativeKeys.Issuer, operationSequence);
            }

            public bool TryEntity(TargetId target, out Entity entity)
            {
                entity = Entity.Null;
                return Seeder != null && Seeder.TryGetEntity(target, out entity);
            }

            public bool TrySeat(uint ordinal, out Entity seat)
            {
                seat = Entity.Null;
                if (CardTable == null)
                {
                    return false;
                }

                return CardTable.TrySeat(ordinal, out seat) && seat != Entity.Null;
            }

            public string TableVersion()
            {
                if (CardTable == null || CardTable.TableEntity == Entity.Null || Host == null)
                {
                    return ConformanceValue.None;
                }

                return ConformanceValue.Int(
                    CardTableAccess.ReadTable(Host.EntityWorld.EntityManager, CardTable.TableEntity).TableVersion);
            }

            public Outcome StopAndDispose()
            {
                Outcome outcome = Outcome.NoChange;
                if (Host != null)
                {
                    OperationResult stop = Host.Stop(NextOperation(), "gc-024 combined conformance world teardown");
                    outcome = stop.Outcome;
                    Host.Dispose();
                    Host = null;
                }

                return outcome;
            }

            public void Dispose()
            {
                if (Host == null)
                {
                    return;
                }

                StopAndDispose();
            }

            private void Create()
            {
                string catalogDetail;
                CombinedCatalog = BuildCombinedCatalog(out catalogDetail);
                CatalogDetail = catalogDetail;
                Declarations = LaneDeclarations();

                Descriptor = BuildMergedDescriptor(FamilyDeclarations());
                if (!Descriptor.Succeeded
                    || Descriptor.Descriptor == null
                    || Descriptor.Adaptation == null
                    || Descriptor.Compilation == null)
                {
                    Failure = "the merged ownership and schedule pipeline refused: " + Descriptor.Describe();
                    return;
                }

                WorldId world = new WorldId(sessions.Next());
                var request = new WorldCreateRequest(
                    world,
                    NarrativeKeys.WorldDefinition,
                    TemporalModel.CommandDriven,
                    PropagationMode.Automatic,
                    ContentHash.Empty,
                    NextOperationForCreation(world),
                    null);
                Registration = new UnityWorldRegistration(
                    WorldName,
                    Descriptor.Adaptation.Stages,
                    MergedSystems(),
                    GuardedDispatchPlan.Empty,
                    Descriptor.Adaptation.StepPlan,
                    GuardedDispatchPlan.Empty,
                    null,
                    MergedMessages(),
                    MergedReaders());
                bool created = UnityWorldRegistry.TryCreate(
                    request, Registration, out UnityWorldHost? createdHost, out WorldCreateResult result);
                Host = createdHost;
                if (!created || Host == null)
                {
                    Failure = "world creation failed: " + result.Code + ": " + result.Detail;
                    return;
                }

                var registry = new TargetRegistry(world, checked((uint)targetCapacity));
                Publisher = new AssemblyPublisher(
                    Host, registry, MergedRecipes(), MergedMigrations(), Descriptor.Descriptor);
                Targets = new LiveTargetIndex(Publisher.Recipes);
                Seeder = new LiveTargetSeeder(Host, registry, Targets);

                Lane = CompositionHost.CreateDefault(
                    world,
                    CrossRootScope,
                    new CatalogManifestSource(CombinedCatalog, Declarations),
                    null,
                    CompositionLaneSeed.InitialAssembly.WithScopes(MergedScopes()));
                _ = new WorldCompositionBridge(Host, Lane, Publisher);

                SeedBothFamilies();

                // Both modules are attached exactly where each family attaches its own: the narrative module with
                // every live target mapped and the ledger as its root entity, the card module with the table bound
                // and every seat bound to its ordinal. Each is a per-`World` static registry, so they coexist.
                Narrative = NarrativeModule.Attach(Host, Descriptor.Compilation.Schedule!);
                MapNarrativeTargets();
                CardTable = CardTableModule.Attach(Host);
                BindCardTable();

                Pipeline = new DerivedAssemblyPipeline(
                    Host,
                    Lane,
                    Publisher,
                    Targets,
                    Seeder,
                    new MergedValueSource(NarrativeValues(), CardDerivationValueSource.Default()),
                    null,
                    null,
                    Publisher.Migrations,
                    new StagedResourceGate(StagedByteCeiling, NarrativeKeys.Issuer),
                    new PlanBudget(PrepareBytesLimit, PrepareBytesLimit, ScratchCapacityBytes, ScratchBytesPerSlot));
                Time = new WorldTimeDriver(Host, new StepInputCutoff(8, 16), new PluginClockRegistry(8), 1U);
                Time.AdoptResourceTable(Descriptor.Adaptation.NativeTable!);

                // The world's own state-policy surface, built from the very manifests the lane resolves, so a slot
                // policy a row asserts on is the declaration's own field and not an override (P-032). The reward
                // installation's registered migration is registered here under its declared key.
                PolicyCatalog = StatePolicyCatalog.Build(
                    LaneManifests(),
                    MergedMigrations().Migrations,
                    null);
                Policies = new StateMigrationPipeline(Host, Publisher, Seeder, PolicyCatalog,
                    new PlanBudget(PrepareBytesLimit, PrepareBytesLimit, ScratchCapacityBytes, ScratchBytesPerSlot));

                Ready = true;
            }

            /// <summary>
            /// Both families' declared targets into ONE `(host, targets, seeder)`, each through its own family's own
            /// seeding, plus the world-level `QuestLedger` the chapter's durable facts live on (07 s3.1, s3.2). The
            /// ledger is seeded at the combined root because a combined world has one root (P-010), and its own
            /// recipe installs the fact value/version slots and the trail counters.
            /// </summary>
            private void SeedBothFamilies()
            {
                var context = new Gc013WorldContext(Host!, Targets!, Seeder!);
                if (!new Gc013NarrativeHost.NarrativeFamily(
                        CombinedCatalog!,
                        Gc013NarrativeHost.Declarations(
                            NarrativeScenarioCatalog.PluginFactoryKey, NarrativeScenarioCatalog.RecordSchema),
                        NarrativeScenarioCatalog.Fingerprint().ToHex()).SeedTargets(context))
                {
                    throw new InvalidOperationException("the narrative family refused to seed its declared targets");
                }

                if (!new Gc013CardsHost.CardFamily(
                        CombinedCatalog!,
                        Gc013CardsHost.Declarations(),
                        CardCatalogTable.Fingerprint().ToHex()).SeedTargets(context))
                {
                    throw new InvalidOperationException("the card family refused to seed its declared targets");
                }

                if (!Seeder!.TrySeed(
                        NarrativeKeys.QuestLedger,
                        CrossRootScope,
                        NarrativeKeys.QuestLedgerRecipe,
                        out TargetHandle _,
                        out DiagnosticCode code,
                        out string detail))
                {
                    throw new InvalidOperationException(
                        "seeding the world-level quest ledger was refused: " + code + ": " + detail);
                }

                // Each declared recipe's base layout, exactly as a spawn's applier installs it: a recipe this
                // package does not declare (the card market's) is skipped rather than failed, because the two
                // families share this composition (04 s6, P-024, P-032).
                var applier = new NarrativeRecipeApplier();
                SpawnRecipeCatalog recipes = NarrativeRecipes.Catalog(applier);
                IReadOnlyList<LiveTarget> live = Targets!.Targets;
                for (int i = 0; i < live.Count; i++)
                {
                    if (!Seeder.TryGetEntity(live[i].Target, out Entity entity) || entity == Entity.Null)
                    {
                        throw new InvalidOperationException(
                            "live target " + live[i].Target.ToString() + " has no live entity (P-005)");
                    }

                    if (recipes.TryResolve(live[i].Recipe, out SpawnRecipe? recipe, out DiagnosticCode _)
                        && recipe != null)
                    {
                        applier.ApplyBaseLayout(Host!.EntityWorld.EntityManager, entity, recipe);
                    }
                }
            }

            private void MapNarrativeTargets()
            {
                IReadOnlyList<LiveTarget> live = Targets!.Targets;
                for (int i = 0; i < live.Count; i++)
                {
                    if (Seeder!.TryGetEntity(live[i].Target, out Entity entity) && entity != Entity.Null)
                    {
                        Narrative!.MapTarget(live[i].Target, entity);
                    }
                }

                if (Seeder.TryGetEntity(NarrativeKeys.QuestLedger, out Entity ledger) && ledger != Entity.Null)
                {
                    Narrative!.SetRootEntity(ledger);
                }
            }

            private void BindCardTable()
            {
                IReadOnlyList<LiveTarget> live = Targets!.Targets;
                for (int i = 0; i < live.Count; i++)
                {
                    TargetId target = live[i].Target;
                    if (!Seeder!.TryGetEntity(target, out Entity entity) || entity == Entity.Null)
                    {
                        continue;
                    }

                    if (target.Equals(CardIdentity.Target(CardVocabulary.TableOne)))
                    {
                        CardTable!.BindTable(entity);
                        continue;
                    }

                    if (target.Equals(CardIdentity.Target(CardVocabulary.PracticeSeat)))
                    {
                        CardTable!.BindSeat(CardTableKeys.PracticeOrdinal, entity);
                        continue;
                    }

                    for (uint ordinal = CardTableKeys.SeatAOrdinal; ordinal <= CardTableKeys.SeatCOrdinal; ordinal++)
                    {
                        if (target.Equals(CardTableFixture.SeatTarget(ordinal)))
                        {
                            CardTable!.BindSeat(ordinal, entity);
                            break;
                        }
                    }
                }

                if (CardTable!.TableEntity == Entity.Null)
                {
                    throw new InvalidOperationException(
                        "the combined market has no table entity, so 07 s5's destination cannot be addressed (P-005)");
                }
            }

            private static IDerivationValueSource NarrativeValues() =>
                new FixtureValueSource().RegisterAlwaysPredicate(NarrativeCompositionNames.AlwaysPredicateName);

            private OperationId NextOperationForCreation(WorldId world)
            {
                operationSequence++;
                return new OperationId(world, NarrativeKeys.Issuer, operationSequence);
            }
        }

        // ------------------------------------------------------------------ the acknowledgement-loss seam

        /// <summary>
        /// A deterministic delivery crash, modelled on the seam's own test markers: the seam can only *report* a
        /// boundary, so a probe host that needs the acknowledgement window to be observable installs a hook that
        /// throws at it. It is armed only for the redelivery, raises at most once, and snapshots the rows the
        /// window left behind so the redelivery can be built from them (P-045, P-049).
        /// </summary>
        private sealed class CrossDeliveryHook : IDeliveryStepHook
        {
            private readonly List<string> reached = new List<string>();
            private NarrativeCardRewardBridge? bridge;
            private bool armed;

            /// <summary>Every boundary the adapter reported to this hook, in order.</summary>
            public IReadOnlyList<string> ReachedBoundaries => reached;

            /// <summary>True once the crash has been raised; a second reach is reported and returns.</summary>
            public bool Spent { get; private set; }

            /// <summary>The outbox rows the `after-delivery` window left behind, as the world projects them.</summary>
            public IReadOnlyList<OutboxRecordValue>? DeliveredRows { get; private set; }

            public int DeliveredRowCount => DeliveredRows == null ? 0 : DeliveredRows.Count;

            public void Attach(NarrativeCardRewardBridge owner) => bridge = owner;

            /// <summary>Arms the crash for the next reach of <see cref="DeliveryBoundaries.AfterDelivery"/>.</summary>
            public void Arm() => armed = true;

            public void Reach(string boundary, string detail)
            {
                reached.Add(boundary);
                if (!string.Equals(boundary, DeliveryBoundaries.AfterDelivery, StringComparison.Ordinal))
                {
                    return;
                }

                // The window itself: the destination was asked and the attempt is not yet recorded, so this is the
                // exact state a restored session sees. Capturing it is a reading of the world, not a fabrication.
                if (bridge != null)
                {
                    DeliveredRows = bridge.Owner.ToRecords();
                }

                if (armed && !Spent)
                {
                    Spent = true;
                    throw new CrossDeliveryCrashException(boundary, detail);
                }
            }
        }

        /// <summary>Raised by <see cref="CrossDeliveryHook"/> at the boundary it was armed for.</summary>
        private sealed class CrossDeliveryCrashException : Exception
        {
            public CrossDeliveryCrashException(string boundary, string detail)
                : base("scripted delivery crash at " + boundary + ": " + detail + " (GC-024).")
            {
                Boundary = boundary;
                Detail = detail ?? string.Empty;
            }

            public string Boundary { get; }

            public string Detail { get; }
        }

        // ------------------------------------------------------------------ the combined composition audit

        /// <summary>
        /// Walks the combined world's declarations, its compiled descriptor and the loaded assembly graph for any
        /// traversal (action) identity, reusing the two audits that already exist rather than re-deriving them
        /// (TEST-021, P-059). <see cref="WalkedEntries"/> counts what was really read, so a clean verdict from a
        /// scan that read nothing is impossible.
        /// </summary>
        public static CrossCompositionAudit AuditCombinedComposition()
        {
            IReadOnlyList<CatalogPluginDeclaration> declarations = FamilyDeclarations();
            PipelineDescriptorReport descriptor = BuildMergedDescriptor(declarations);
            W6FamilyAudit family = W6CompositionAudit.WalkFamily(
                Label, declarations, descriptor, W6CompositionAudit.CourseSurface());

            var findings = new List<string>();
            for (int i = 0; i < family.Offenders.Count; i++)
            {
                findings.Add(family.Offenders[i]);
            }

            W6AdapterAssemblyFacts adapters = W6CompositionAudit.AdapterAssemblies();
            LoadedAssemblyReport loaded = KernelAssemblyAudit.AuditLoadedAssemblies();

            // TEST-021's claim: the optional physics/animation/audio halves are absent from the card and narrative
            // gameplay assemblies, while the traversal course (which this combined world does not carry) owns them.
            if (adapters.NarrativeReferencesAdapters)
            {
                findings.Add(W6CompositionAudit.NarrativeAssemblyName + ":referencesAdapters");
            }

            if (adapters.CardsReferencesAdapters)
            {
                findings.Add(W6CompositionAudit.CardsAssemblyName + ":referencesAdapters");
            }

            if (!adapters.AllLoaded)
            {
                findings.Add("adapterAssemblies:notLoaded");
            }

            for (int i = 0; i < loaded.ForbiddenReferences.Count; i++)
            {
                findings.Add("kernelReference:" + loaded.ForbiddenReferences[i]);
            }

            for (int i = 0; i < loaded.DuplicateKernelAssemblies.Count; i++)
            {
                findings.Add("duplicateKernel:" + loaded.DuplicateKernelAssemblies[i]);
            }

            for (int i = 0; i < loaded.InspectionFailures.Count; i++)
            {
                findings.Add("inspectionFailure:" + loaded.InspectionFailures[i]);
            }

            if (!descriptor.Succeeded)
            {
                findings.Add("mergedDescriptor:" + descriptor.Outcome.ToString());
            }

            int walked = family.Walked
                + family.DeclaredStages
                + adapters.Families.Count
                + loaded.KernelAssemblies.Count
                + loaded.KernelReferenceCount;
            string detail = "owner=" + family.Owner
                + "; declarations=" + declarations.Count.ToString(CultureInfo.InvariantCulture)
                + "; walked=" + family.Walked.ToString(CultureInfo.InvariantCulture)
                + "; declaredStages=" + family.DeclaredStages.ToString(CultureInfo.InvariantCulture)
                + "; descriptor=" + descriptor.Outcome
                + "; descriptorStages=" + (descriptor.Descriptor == null
                    ? 0
                    : descriptor.Descriptor.Stages.Count)
                + "; descriptorSlots=" + (descriptor.Descriptor == null
                    ? 0
                    : descriptor.Descriptor.Slots.Count)
                + "; assemblies=" + adapters.Families.Count.ToString(CultureInfo.InvariantCulture)
                + "; narrativeAdapters=" + adapters.NarrativeReferencesAdapters
                + "; cardsAdapters=" + adapters.CardsReferencesAdapters
                + "; kernelAssemblies=" + loaded.KernelAssemblies.Count.ToString(CultureInfo.InvariantCulture)
                + "; kernelReferences=" + loaded.KernelReferenceCount.ToString(CultureInfo.InvariantCulture)
                + "; forbiddenReferences=" + loaded.ForbiddenReferences.Count.ToString(CultureInfo.InvariantCulture)
                + "; walkedEntries=" + walked.ToString(CultureInfo.InvariantCulture);
            return new CrossCompositionAudit(walked, findings, detail);
        }
    }

    /// <summary>
    /// What the combined composition carries of the traversal course's (action) surface, plus the assembly-graph
    /// half. <see cref="Clean"/> requires the walk to have read something: a genre nobody read cannot look clean.
    /// </summary>
    public sealed class CrossCompositionAudit
    {
        public CrossCompositionAudit(int walkedEntries, IReadOnlyList<string> findings, string detail)
        {
            WalkedEntries = walkedEntries;
            Findings = findings ?? Array.Empty<string>();
            Detail = detail ?? string.Empty;
        }

        /// <summary>Declarations, descriptor entries and assemblies this audit really read; zero is a red audit.</summary>
        public int WalkedEntries { get; }

        /// <summary>Every traversal identity or forbidden assembly edge the combined composition carries.</summary>
        public IReadOnlyList<string> Findings { get; }

        /// <summary>One line naming every count this audit read.</summary>
        public string Detail { get; }

        /// <summary>True when the composition was really walked and carries no action surface.</summary>
        public bool Clean => Findings.Count == 0 && WalkedEntries > 0;

        public string Describe()
        {
            var parts = new List<string>(Findings.Count);
            for (int i = 0; i < Findings.Count; i++)
            {
                parts.Add(Findings[i]);
            }

            return (Clean ? "cross-composition-audit: clean" : "cross-composition-audit: not clean")
                + "; findings=" + (parts.Count == 0 ? "<none>" : string.Join(",", parts.ToArray()))
                + "; " + Detail;
        }
    }
}

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
// WHAT IS NOT FAKED. 07:276's pending-work unmount refusal is reported, not performed: `NarrativeCardRewards` is an
// ordinary caller-owned object in this revision and not a mounted installation, so its `PreserveDormant`
// declaration has nowhere to live (exactly what `artifacts/gc-021/HANDOFF.md` s7 item 5 records). The step is
// `Unsupported` with that reason, it is never reported as published, and the pending obligation it is about is
// still recorded so a reviewer can see the state.
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
using GameCore.ReferenceConformance;
using GameCore.Rules.Cards;
using GameCore.Rules.Narrative;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Messages;
using GameCore.Unity.Runtime.Time;
using Unity.Entities;

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
        /// fails the harness instead of shrinking the run silently (P-060).
        /// </summary>
        private static readonly string[] RecordedSuffixes =
        {
            "reward-flow/world",
            "reward-flow/setup-0",
            "reward-flow/setup-1",
            "reward-enqueue",
            "reward-bridge-removal",
            "reward-settle",
            "reward-redelivery",
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
            NarrativeCardRewardBridge? bridge = null;
            RewardsInstallation? rewardInstallation = null;
            CrossDeliveryHook? hook = null;
            NarrativeModule? narrativeModule = null;
            CardTableModule? cardModule = null;
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

                    // 2. The reward bridge of 07 s5. The content declares the node the chapter-one permit choice
                    //    lands on; the definition's card is the one the card fixture really stocks at the holding
                    //    seat, so nothing here is a card concept this file invented (P-001, P-034, P-054).
                    var content = Gc021RewardContent.ForNode(NarrativeDialogueRules.PermitResultNode);
                    RewardCatalog catalog = content.ToCatalog(requiresDurability: true, out string contentDetail);
                    hook = new CrossDeliveryHook();
                    var journal = new MemoryDeliveryJournal("memory://gc024-cross-rewards");
                    // The bridge of 07 s5 is constructed THROUGH the reward installation (GC-024), which owns it
                    // and supplies its own declared identity and issuer; the bridge itself is unchanged, so every
                    // member this run reads (`RecognisedCount`, `Owner.Outbox.*`, `Owner.ToRecords()`,
                    // `Destination.*`, `Owner.Reinstate`) is the same one.
                    rewardInstallation = RewardsInstallation.Mount(
                        world.Host!,
                        world.Time!,
                        catalog,
                        RewardCapacity,
                        RewardTerminalRetention,
                        OutboxDurability.Durable,
                        journal,
                        hook);
                    bridge = rewardInstallation.Bridge;
                    hook.Attach(bridge);
                    steps.Add(new ConformanceObservation(
                        StepPrefix + RecordedSuffixes[2],
                        catalog.Count == 1 && catalog.RequiresDurability && contentDetail.Length == 0,
                        "mount-reward-bridge: content={" + content.Describe() + "}"
                        + "; definitions=" + catalog.Count.ToString(CultureInfo.InvariantCulture)
                        + "; durability=" + bridge.Owner.Outbox.Durability
                        + "; detail=" + (contentDetail.Length == 0 ? "declared" : contentDetail)));

                    RunRewardFlow(world, script, bridge, hook, trace, outcomes, steps, out narrativeModule,
                        out cardModule);
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
                if (rewardInstallation != null)
                {
                    // The installation owns the bridge, so disposing it releases the delivery owner.
                    rewardInstallation.Dispose();
                    rewardInstallation = null;
                }
                else if (bridge != null)
                {
                    bridge.Dispose();
                }

                cardModule?.Dispose();
                narrativeModule?.Dispose();
                Outcome stop = world.StopAndDispose();
                steps.Add(new ConformanceObservation(
                    StepPrefix + RecordedSuffixes[7],
                    unhandled.Length == 0 && (stop == Outcome.Published || stop == Outcome.NoChange),
                    "stop=" + stop
                    + "; registry=" + UnityWorldRegistry.Count.ToString(CultureInfo.InvariantCulture)
                    + (unhandled.Length == 0 ? string.Empty : "; " + unhandled)));
            }

            // The action-surface audit (TEST-021) is the run's own claim about the composition it just built.
            CrossCompositionAudit audit = AuditCombinedComposition();
            steps.Add(new ConformanceObservation(
                StepPrefix + RecordedSuffixes[8],
                audit.Clean,
                audit.Describe()));

            ConformanceVerdict verdict = ConformanceOracle.CompareScript(script!, trace, outcomes);
            steps.Add(new ConformanceObservation(
                StepPrefix + RecordedSuffixes[9],
                verdict.Passed,
                verdict.Describe()));
            return new ConformanceTableResult(Label, steps, trace, verdict, trace.ToDocument());
        }

        /// <summary>
        /// The reward flow of 07:267-278: the choice commits the fact, its event and the pending reward together;
        /// the admitted reward is dispatched and acknowledged; and the same obligation is delivered again from the
        /// rows the acknowledgement window left behind.
        /// </summary>
        private static void RunRewardFlow(
            CrossWorld world,
            ConformanceScript script,
            NarrativeCardRewardBridge bridge,
            CrossDeliveryHook hook,
            ConformanceTrace trace,
            List<ConformanceOracle.RowOutcomeReport> outcomes,
            List<ConformanceObservation> steps,
            out NarrativeModule? narrativeModule,
            out CardTableModule? cardModule)
        {
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

            // One bridge pass that observes the committed events and commits the obligation without handing
            // anything over (`maxDispatches` = 0), which is P-045's persist-then-apply order: the obligation exists
            // before the destination is touched (07 s5 steps 1-2).
            RewardBridgePassReport enqueue = bridge.Run(world.NextOperation(), RewardEventWindow, 0);

            string enqueueRow = "reward-enqueue";
            IReadOnlyList<string> enqueueFields = FieldsOf(script, enqueueRow);
            List<string> enqueueBefore = ReadAll(world, bridge, enqueueFields);
            StepRecord(trace, enqueueRow, enqueueFields, enqueueBefore, enqueueBefore);
            Snapshot(trace, world, bridge, enqueueRow, fieldsBefore: true);
            Snapshot(trace, world, bridge, enqueueRow, fieldsBefore: false);

            bool enqueueOpen = bridge.Owner.Outbox.OpenCount == 1;
            bool enqueueUnmutated = bridge.Destination.CommittedCount == 0 && bridge.Destination.SubmittedCount == 0;
            bool enqueuePassed = committed.Published
                && enqueueOpen
                && bridge.RecognisedCount >= 1
                && enqueueUnmutated
                && bridge.Owner.IsDurable;
            outcomes.Add(new ConformanceOracle.RowOutcomeReport(enqueueRow, enqueuePassed, enqueuePassed
                ? string.Empty
                : "the committed choice did not become one open, unapplied obligation"));
            steps.Add(new ConformanceObservation(
                StepPrefix + RecordedSuffixes[3],
                enqueuePassed,
                ConformanceScenario.StepPrefix + Label + "/" + enqueueRow
                + "; outcome=" + (committed.Published ? "Published" : "Refused")
                + "; " + committed.Detail
                + "; pass={" + enqueue + "}"
                + "; recognised=" + bridge.RecognisedCount.ToString(CultureInfo.InvariantCulture)
                + "; open=" + bridge.Owner.Outbox.OpenCount.ToString(CultureInfo.InvariantCulture)
                + "; durable=" + bridge.Owner.IsDurable
                + "; destinationMutations="
                + bridge.Destination.CommittedCount.ToString(CultureInfo.InvariantCulture)
                + "; destinationSubmits="
                + bridge.Destination.SubmittedCount.ToString(CultureInfo.InvariantCulture)
                + "; describe=" + Clip(bridge.LastDescribeDetail, 120)));

            // 07:276 — unmounting with pending work. Reported, never faked: see ProvePendingUnmountIsRefused.
            ProvePendingUnmountIsRefused(world, bridge, script, trace, outcomes, steps);

            // 07:269-270 — dispatch the admitted reward and acknowledge it. The destination is asked between the
            // two delivery boundaries, so the hook's `AfterDelivery` reach is the acknowledgement window itself.
            List<string> settleBefore = ReadAll(world, bridge, FieldsOf(script, "reward-settle"));
            int handABefore = HandSize(world, CardTableKeys.SeatAOrdinal);
            int handBBefore = HandSize(world, CardTableKeys.SeatBOrdinal);
            RewardBridgePassReport settle = bridge.Run(default(OperationId), RewardEventWindow, RewardDispatchWindow);
            IReadOnlyList<string> settleFields = FieldsOf(script, "reward-settle");
            List<string> settleAfter = ReadAll(world, bridge, settleFields);
            StepRecord(trace, "reward-settle", settleFields, settleBefore, settleAfter);
            Snapshot(trace, world, bridge, "reward-settle", fieldsBefore: true);
            Snapshot(trace, world, bridge, "reward-settle", fieldsBefore: false);

            int handAAfter = HandSize(world, CardTableKeys.SeatAOrdinal);
            int handBAfter = HandSize(world, CardTableKeys.SeatBOrdinal);
            bool settlePassed = settle.Dispatched == 1
                && bridge.Owner.Outbox.OpenCount == 0
                && bridge.Owner.AcknowledgedCount == 1
                && bridge.Destination.CommittedCount == 1
                && handAAfter == handABefore + 1
                && handBAfter == handBBefore - 1;
            outcomes.Add(new ConformanceOracle.RowOutcomeReport("reward-settle", settlePassed, settlePassed
                ? string.Empty
                : "the admitted reward did not commit exactly one transfer and acknowledge it"));
            steps.Add(new ConformanceObservation(
                StepPrefix + RecordedSuffixes[5],
                settlePassed,
                ConformanceScenario.StepPrefix + Label + "/reward-settle; outcome=Published; pass={" + settle + "}"
                + "; acknowledged=" + bridge.Owner.AcknowledgedCount.ToString(CultureInfo.InvariantCulture)
                + "; open=" + bridge.Owner.Outbox.OpenCount.ToString(CultureInfo.InvariantCulture)
                + "; mutations=" + bridge.Destination.CommittedCount.ToString(CultureInfo.InvariantCulture)
                + "; hand-a=" + handABefore.ToString(CultureInfo.InvariantCulture) + "->"
                + handAAfter.ToString(CultureInfo.InvariantCulture)
                + "; hand-b=" + handBBefore.ToString(CultureInfo.InvariantCulture) + "->"
                + handBAfter.ToString(CultureInfo.InvariantCulture)
                + "; attemptWindow=" + hook.DeliveredRowCount.ToString(CultureInfo.InvariantCulture)
                + " row(s) at " + DeliveryBoundaries.AfterDelivery));

            // 07:272 — the same obligation again, after the acknowledgement was lost. The rows the acknowledgement
            // window left behind are reinstated into the durable owner (a restored session sees exactly those rows),
            // the same obligation is handed over again, the destination answers `AlreadyApplied`, and the crash hook
            // fires at `AfterDelivery` so the acknowledgement is genuinely lost a second time (P-045, P-149's seam).
            IReadOnlyList<string> redeliveryFields = FieldsOf(script, "reward-redelivery");
            Dictionary<string, string> redeliveryUnrelated = ReadVocabulary(world, bridge);
            List<string> redeliveryBefore = ReadAll(world, bridge, redeliveryFields);
            string reinstate = "not attempted";
            bool redelivered = false;
            string crashBoundary = DeliveryBoundaries.None;
            DiagnosticCode reinstateCode = DiagnosticCode.None;
            string reinstateDetail = string.Empty;
            IReadOnlyList<OutboxRecordValue>? deliveredRows = hook.DeliveredRows;
            if (deliveredRows != null
                && bridge.Owner.TryReinstate(deliveredRows, out reinstateCode, out reinstateDetail))
            {
                reinstate = "rows=" + hook.DeliveredRowCount.ToString(CultureInfo.InvariantCulture)
                    + "; open=" + bridge.Owner.Outbox.OpenCount.ToString(CultureInfo.InvariantCulture)
                    + "; detail=" + Clip(reinstateDetail, 80);
                hook.Arm();
                try
                {
                    bridge.Run(default(OperationId), RewardEventWindow, RewardDispatchWindow);
                }
                catch (CrossDeliveryCrashException crash)
                {
                    crashBoundary = crash.Boundary;
                    redelivered = true;
                }
            }
            else
            {
                reinstate = "refused (code=" + reinstateCode + "): " + reinstateDetail;
            }

            List<string> redeliveryAfter = ReadAll(world, bridge, redeliveryFields);
            StepRecord(trace, "reward-redelivery", redeliveryFields, redeliveryBefore, redeliveryAfter);
            Snapshot(trace, world, bridge, "reward-redelivery", fieldsBefore: true);
            Snapshot(trace, world, bridge, "reward-redelivery", fieldsBefore: false);

            bool redeliveryPassed = redelivered
                && string.Equals(crashBoundary, DeliveryBoundaries.AfterDelivery, StringComparison.Ordinal)
                && bridge.Destination.AlreadyPresentCount == 1
                && bridge.Destination.CommittedCount == 1
                && Preserved(redeliveryUnrelated, ReadVocabulary(world, bridge), ConformanceFields.RewardRecipientHandSize)
                && Preserved(redeliveryUnrelated, ReadVocabulary(world, bridge), ConformanceFields.RewardHolderHandSize)
                && Preserved(redeliveryUnrelated, ReadVocabulary(world, bridge), ConformanceFields.RewardRecipientHand)
                && Preserved(redeliveryUnrelated, ReadVocabulary(world, bridge), ConformanceFields.RewardHolderHand)
                && Preserved(redeliveryUnrelated, ReadVocabulary(world, bridge), ConformanceFields.RewardRecipientTotal)
                && Preserved(redeliveryUnrelated, ReadVocabulary(world, bridge), ConformanceFields.RewardHolderTotal)
                && Preserved(redeliveryUnrelated, ReadVocabulary(world, bridge), ConformanceFields.RewardTableVersion);
            outcomes.Add(new ConformanceOracle.RowOutcomeReport("reward-redelivery", redeliveryPassed, redeliveryPassed
                ? string.Empty
                : "the redelivery did not report AlreadyApplied with unrelated state preserved"));
            steps.Add(new ConformanceObservation(
                StepPrefix + RecordedSuffixes[6],
                redeliveryPassed,
                ConformanceScenario.StepPrefix + Label + "/reward-redelivery; outcome=Published"
                + "; alreadyApplied=" + bridge.Destination.AlreadyPresentCount.ToString(CultureInfo.InvariantCulture)
                + "; mutations=" + bridge.Destination.CommittedCount.ToString(CultureInfo.InvariantCulture)
                + "; submits=" + bridge.Destination.SubmittedCount.ToString(CultureInfo.InvariantCulture)
                + "; reinstate={" + reinstate + "}"
                + "; crashBoundary=" + crashBoundary
                + "; hookSpent=" + hook.Spent));

            narrativeModule = world.Narrative;
            cardModule = world.CardTable;
        }

        /// <summary>
        /// 07:276 — "unmounting with pending work rejects until it drains or transfers". The bridge is an ordinary
        /// caller-owned object in this revision, not a mounted installation, so its `PreserveDormant` declaration
        /// has nowhere to live and there is no package API that can perform the refusal. The step is reported
        /// `Unsupported` with that exact reason and is never reported as published; the fields the row demands are
        /// read from the live world (so the pending obligation is visible to a reviewer) and the operation is
        /// recorded as not published, which is the `RefusedKeepsAssembly` half of the row.
        /// </summary>
        private static void ProvePendingUnmountIsRefused(
            CrossWorld world,
            NarrativeCardRewardBridge bridge,
            ConformanceScript script,
            ConformanceTrace trace,
            List<ConformanceOracle.RowOutcomeReport> outcomes,
            List<ConformanceObservation> steps)
        {
            IReadOnlyList<string> fields = FieldsOf(script, "reward-bridge-removal");
            List<string> readings = ReadAll(world, bridge, fields);
            StepRecord(trace, "reward-bridge-removal", fields, readings, readings);
            Snapshot(trace, world, bridge, "reward-bridge-removal", fieldsBefore: true);
            Snapshot(trace, world, bridge, "reward-bridge-removal", fieldsBefore: false);

            // A RECORDED GAP, not a pass and not a failure: the row is executed as far as this revision allows (the
            // pending obligation is read from the live world, so a reviewer sees the state the refusal would protect),
            // the operation is reported as not published with its reason — which is the row's `RefusedKeepsAssembly`
            // half, satisfied honestly because nothing was done — and the step's status is `RecordedGap`, so
            // `ConformanceTableResult.AllPassed` stays false and `ConformanceDocGaps` names the clause this revision
            // cannot satisfy (07:276's PreserveDormant declaration, which has nowhere to live while the bridge is an
            // ordinary caller-owned object rather than a mounted installation).
            ConformanceDocGap gap = ConformanceDocGaps.ById(ConformanceDocGaps.RewardBridgeRemovalId)
                ?? throw new InvalidOperationException(
                    "the reward bridge removal row is a recorded gap, but the fixture declares no gap with that id");
            outcomes.Add(new ConformanceOracle.RowOutcomeReport("reward-bridge-removal", false, gap.Missing));
            steps.Add(new ConformanceObservation(
                StepPrefix + RecordedSuffixes[4],
                ConformanceStepStatus.RecordedGap,
                ConformanceScenario.StepPrefix + Label + "/reward-bridge-removal; outcome=Unsupported; gap="
                + gap.GapId + "; clause=" + gap.Clause + "; missing=" + gap.Missing
                + "; pendingWork=open=" + bridge.Owner.Outbox.OpenCount.ToString(CultureInfo.InvariantCulture)
                + "; mutations=" + bridge.Destination.CommittedCount.ToString(CultureInfo.InvariantCulture)
                + "; acknowledged=" + bridge.Owner.AcknowledgedCount.ToString(CultureInfo.InvariantCulture)
                + "; unmountAttempts=0; evidence=" + gap.Evidence));
        }

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
        /// The 07 row's own declared fields, in the order the fixture table declares them. The script and the table
        /// carry the same fields for one row id; the table is the document's own column set, so it is read from
        /// there and the script's precondition expectations (a subset) are satisfied by the same two readings.
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

        private static List<string> ReadAll(
            CrossWorld world, NarrativeCardRewardBridge bridge, IReadOnlyList<string> fields)
        {
            var values = new List<string>(fields.Count);
            for (int i = 0; i < fields.Count; i++)
            {
                values.Add(ReadField(world, bridge, fields[i]));
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
        /// The whole `ConformanceFields.Cross()` vocabulary on one side of one step, recorded under the row's own
        /// `/state` id so it never competes with the row's assertions: the trace carries the state the assertions
        /// were made against, not only the assertions (P-026).
        /// </summary>
        private static void Snapshot(
            ConformanceTrace trace, CrossWorld world, NarrativeCardRewardBridge bridge, string rowId, bool fieldsBefore)
        {
            ConformancePhase phase = fieldsBefore ? ConformancePhase.Before : ConformancePhase.After;
            IReadOnlyList<ConformanceField> declared = ConformanceFields.Cross();
            for (int f = 0; f < declared.Count; f++)
            {
                trace.TryRecord(
                    Label,
                    rowId + "/state",
                    phase,
                    declared[f].Key,
                    ReadField(world, bridge, declared[f].Key));
            }
        }

        /// <summary>
        /// The whole `ConformanceFields.Cross()` vocabulary read once, keyed by the field key: the unrelated state a
        /// reward must not disturb, and the state a row's assertions are made against (P-026, P-034).
        /// </summary>
        private static Dictionary<string, string> ReadVocabulary(CrossWorld world, NarrativeCardRewardBridge bridge)
        {
            IReadOnlyList<ConformanceField> declared = ConformanceFields.Cross();
            var values = new Dictionary<string, string>(declared.Count, StringComparer.Ordinal);
            for (int f = 0; f < declared.Count; f++)
            {
                values[declared[f].Key] = ReadField(world, bridge, declared[f].Key);
            }

            return values;
        }

        /// <summary>
        /// True when one field read identically on both sides of the step. A field the snapshot does not carry is
        /// reported as not preserved, so a key nobody read cannot pass as unchanged.
        /// </summary>
        private static bool Preserved(
            Dictionary<string, string> before, Dictionary<string, string> after, string field)
        {
            if (!before.TryGetValue(field, out string? first) || !after.TryGetValue(field, out string? second))
            {
                return false;
            }

            return string.Equals(first, second, StringComparison.Ordinal);
        }

        // ------------------------------------------------------------------ reading the real world

        /// <summary>
        /// One canonical field of `ConformanceFields.Cross()`, read from the live world: the durable fact and the
        /// conversation through the narrative package's own state accessor (P-034), the outbox counts from the
        /// delivery owner and its destination port (P-045), and the card table's hands, scores and version through
        /// the card package's own accessors (P-032).
        /// </summary>
        private static string ReadField(CrossWorld world, NarrativeCardRewardBridge bridge, string field)
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
                    return Int(bridge.RecognisedCount);
                case ConformanceFields.OutboxOpen:
                    return Int(bridge.Owner.Outbox.OpenCount);
                case ConformanceFields.OutboxAcknowledged:
                    return Int(bridge.Owner.AcknowledgedCount);
                case ConformanceFields.OutboxAlreadyApplied:
                    return Int(bridge.Destination.AlreadyPresentCount);
                case ConformanceFields.OutboxMutations:
                    return Int(bridge.Destination.CommittedCount);
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
                    // A key this world owns no reading for is a recorded absence with no guessed value: an
                    // unobserved expectation then fails instead of passing for the wrong reason (P-026).
                    return ConformanceValue.None;
            }
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
        /// The merged manifest declarations of the combined world: the narrative fixture's generated-style set plus
        /// the card family's declared set. `Gc013CardsHost.Declarations()` already carries
        /// `CardTableFixture.Declarations()` as its first four entries, so adding that set a second time would be a
        /// duplicate declaration rather than a union (P-009).
        /// </summary>
        private static List<CatalogPluginDeclaration> MergedDeclarations()
        {
            var merged = new List<CatalogPluginDeclaration>(
                Gc013NarrativeHost.Declarations(
                    NarrativeScenarioCatalog.PluginFactoryKey, NarrativeScenarioCatalog.RecordSchema));
            merged.AddRange(Gc013CardsHost.Declarations());
            return merged;
        }

        /// <summary>
        /// The union catalog both families' declarations resolve through. The two fixture catalogs are separate
        /// tables with disjoint keys, so the combined world carries the union of their registrations: a declaration
        /// whose factory key or configuration schema resolved in its own world must resolve here too (P-009, P-028).
        /// </summary>
        private static ImmutableCatalog BuildCombinedCatalog(out string detail)
        {
            var factories = new List<FactoryRegistration>(NarrativeScenarioCatalog.Factories());
            factories.AddRange(CardCatalogTable.Factories());
            var schemas = new List<SchemaRegistration>(NarrativeScenarioCatalog.Schemas());
            schemas.AddRange(CardCatalogTable.Schemas());
            var serializers = new List<ISchemaSerializer>(NarrativeScenarioCatalog.Serializers);
            serializers.AddRange(CardCatalogTable.Serializers());
            var features = new List<Id128>(NarrativeScenarioCatalog.SupportedFeatureIds);
            features.AddRange(CardCatalogTable.SupportedFeatureIds);

            CatalogBuildResult built = ImmutableCatalog.Build(factories, schemas, features, serializers);
            if (built.Catalog == null)
            {
                detail = "the union of the two fixture catalogs was rejected: " + built.Describe();
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

            public MergedDispatchKinds(ScheduleDispatchKindTable narrative, ScheduleDispatchKindTable cards)
            {
                this.narrative = narrative;
                this.cards = cards;
            }

            public bool TryResolveKind(FactoryKey systemKey, out SystemDispatchKind kind)
                => narrative.TryResolveKind(systemKey, out kind) || cards.TryResolveKind(systemKey, out kind);

            public int Count => narrative.Count + cards.Count;
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
                new MergedDispatchKinds(NarrativeDispatchKinds(), CardTableRegistration.DispatchKinds()),
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

        private static MigrationRegistry MergedMigrations() =>
            new MigrationRegistry(new List<ISlotMigration>
            {
                new NarrativeConversationNodeMigration(),
                new NarrativeConversationStatusMigration(),
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

            /// <summary>The merged declaration set the lane resolves manifests from and the schedule compiled.</summary>
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
                Declarations = MergedDeclarations();

                Descriptor = BuildMergedDescriptor(Declarations);
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
            IReadOnlyList<CatalogPluginDeclaration> declarations = MergedDeclarations();
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

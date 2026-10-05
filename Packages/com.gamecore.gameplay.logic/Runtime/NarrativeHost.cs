// GameCore.Gameplay.Logic - the narrative host stage and the narrative delivery (P1.4; in-step outbox P1.7a).
//
// NarrativeHost is one IGameplayInputSource: it runs in the world's input phase, before the frame's one pump, and
//   1. reads the events committed by the previous pump (its own cursor) and hands each to the listeners (the quest
//      tracker reads level objectives and fail conditions; presenters follow dialogue and journal state);
//   2. lets every listener finish the frame (the quest tracker submits quest.setObjective for changed level objectives);
//   3. pumps the narrative delivery: open obligations are handed to their destination ports, which submit ordinary
//      commands (never pumping the world).
// Everything it submits is applied by the next pump, so the world is still pumped exactly once per frame.
//
// NarrativeDelivery (P1.7a, A1) is the world's one delivery owner and its in-step outbox:
//   * One owner per world. The delivery adopts the WorldDeliveryOwner SaveService's DeliveryFactory made for the root
//     (FactoryFor / CreateOwner), so a checkpoint captures - and a restore reinstates - exactly the obligations the
//     narrative layer delivers. There is no second owner.
//   * Obligations are made inside the committing step. NarrativeDelivery is the world's IGameplayStepTap: every kernel
//     hands it the events it commits (StepEventBatch, the world, entity and interaction kernels), and in that same step
//     it records an obligation for every delivery the event owes - each ActionDue action, each RewardGranted reward,
//     each rule a trigger event selects (logic.evaluate), each quest edge objective a talk/interact signal advances
//     (quest.setObjective) and each interactable action a use or trigger entry demands (logic.runActions or
//     inventory.pickup). A capture taken after the pump therefore holds every owed delivery; nothing waits in a host
//     cursor that a restore would lose.
//   * Exactly once rests on the obligation, not on an eight-entry ring. An obligation's request id derives from its
//     identity (negative range); the destination kernel claims it through the tap in the step that applies it (open ->
//     apply, settled or unknown -> IdempotencyConflict) and settles (acknowledges) it in that same step, so the outbox's
//     terminal state and the destination's effect commit together. Destinations with a request ring also push the id,
//     which keeps a replay of stale outbox rows (P1.4's DrownedBell replay) answered AlreadyApplied.
//   * Every port carries a request id: grant, consume, pickup, buy, set-fact, dialogue-start, quest-start/advance/
//     complete/fail/objective, logic-evaluate, run-actions, entity-spawn/despawn/set-variant, world-travel and
//     player-restore-stamina. Presentation ports (play-audio, show-message) present at most once per obligation within
//     a session; after a restore an unsettled presentation obligation presents again (at least once, documented).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Execution.Delivery;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Inventory;
using GameCore.Rules.Gameplay.Logic;
using GameCore.Rules.Gameplay.Quest;
using GameCore.Unity.App;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Delivery;
using GameCore.Unity.Runtime.Messages;
using GameCore.Unity.Runtime.Persistence;

namespace GameCore.Gameplay.Logic
{
    /// <summary>Receives the committed events of each frame (before the frame's pump).</summary>
    public interface INarrativeEventListener
    {
        void OnEvent(CommittedEvent committed);

        /// <summary>Called once per frame after every event of the frame was handed out.</summary>
        void AfterEvents(int frame);
    }

    /// <summary>The narrative host stage (input phase).</summary>
    public sealed class NarrativeHost : IGameplayInputSource
    {
        public const int MaxEventsPerFrame = 512;

        private readonly NarrativeRuntime runtime;
        private EventCursor cursor;

        public NarrativeHost(NarrativeRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            cursor = new EventCursor(runtime.Host.World, EventSequence.Zero);
        }

        public int Frames { get; private set; }

        public int EventsRead { get; private set; }

        public int CursorExpiries { get; private set; }

        public int Collect(GameplayWorld world)
        {
            WorldMessagePlane? plane = runtime.Host.Messages;
            if (plane == null)
            {
                return 0;
            }

            Frames++;
            int before = runtime.Submitter.Submitted;
            CommittedEventPage page = plane.ReadEvents(cursor, MaxEventsPerFrame);
            if (page.Outcome == CursorOutcome.CursorExpired)
            {
                CursorExpiries++;
                cursor = new EventCursor(runtime.Host.World, plane.LastEventSequence);
            }
            else
            {
                IReadOnlyList<INarrativeEventListener> listeners = runtime.Listeners;
                for (int e = 0; e < page.Events.Count; e++)
                {
                    EventsRead++;
                    for (int l = 0; l < listeners.Count; l++)
                    {
                        listeners[l].OnEvent(page.Events[e]);
                    }
                }

                cursor = page.NextCursor;
            }

            for (int l = 0; l < runtime.Listeners.Count; l++)
            {
                runtime.Listeners[l].AfterEvents(Frames);
            }

            runtime.Delivery.Pump();
            return runtime.Submitter.Submitted - before;
        }
    }

    /// <summary>
    /// A destination port that submits one narrative (or P1.1) command. Obligation payload: target id, then the command's
    /// int32 values with the obligation's request id already in place. Outcomes: Applied when this attempt submitted the
    /// command (the destination settles the obligation in the step that applies it); Unavailable while the submitted
    /// command is pending, or committed without settling (inventory full) or deferred by a capacity refusal, until
    /// <see cref="RetryPasses"/> passed; AlreadyApplied when the destination answered IdempotencyConflict; Refused when
    /// the command was rejected for any other reason.
    /// </summary>
    public sealed class NarrativeCommandPort : IDestinationPort
    {
        /// <summary>Delivery passes before a command that did not settle its obligation is submitted again.</summary>
        public const int RetryPasses = 60;

        private readonly NarrativeRuntime runtime;
        private readonly RouteId route;
        private readonly int ints;
        private readonly Func<int[], FrozenPayload>? encode;
        private readonly Dictionary<Id128, Pending> pending = new Dictionary<Id128, Pending>();

        public NarrativeCommandPort(NarrativeRuntime runtime, string name, RouteId route, SchemaRef schema, int ints, Func<int[], FrozenPayload>? encode = null)
        {
            this.runtime = runtime;
            this.route = route;
            this.ints = ints;
            this.encode = encode;
            Name = name;
            DestinationId = NarrativeDelivery.DestinationOf(name);
            CommandSchema = schema;
        }

        public string Name { get; }

        public Id128 DestinationId { get; }

        public SchemaRef CommandSchema { get; }

        /// <summary>Values in an obligation payload of this port (the request id included).</summary>
        public int Ints => ints;

        public int Submitted { get; private set; }

        public int AlreadyApplied { get; private set; }

        public int Retries { get; private set; }

        /// <summary>Forgets the submissions in flight (the outbox rows were replaced: every open obligation is new to the port).</summary>
        internal void ClearPending() => pending.Clear();

        public DestinationOutcome TryApply(in DeliveryAttempt attempt, out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            if (!NarrativeDelivery.TryDecode(attempt.PayloadBytes(), ints, out TargetId target, out int[] values))
            {
                code = DiagnosticCode.UnsupportedVersion;
                detail = Name + ": malformed obligation payload";
                return DestinationOutcome.Refused;
            }

            if (pending.TryGetValue(attempt.OutboxId, out Pending? earlier) && earlier != null)
            {
                bool waited = runtime.Delivery.Pass - earlier.Pass >= RetryPasses;
                if (runtime.Submitter.TryOutcome(earlier.Request, out RequestResultKind kind, out DiagnosticCode reason))
                {
                    switch (kind)
                    {
                        case RequestResultKind.Accepted:
                            return DestinationOutcome.Unavailable;
                        case RequestResultKind.Committed:
                            if (!waited)
                            {
                                detail = Name + ": committed without settling (deferred)";
                                return DestinationOutcome.Unavailable;
                            }

                            break;
                        default:
                            if (reason == DiagnosticCode.IdempotencyConflict)
                            {
                                pending.Remove(attempt.OutboxId);
                                AlreadyApplied++;
                                return DestinationOutcome.AlreadyApplied;
                            }

                            if (reason == DiagnosticCode.BudgetExceeded)
                            {
                                if (!waited)
                                {
                                    detail = Name + ": refused for capacity (deferred)";
                                    return DestinationOutcome.Unavailable;
                                }

                                break;
                            }

                            pending.Remove(attempt.OutboxId);
                            code = reason == DiagnosticCode.None ? DiagnosticCode.Ineligible : reason;
                            detail = Name + ": the command was refused (" + code + ")";
                            return DestinationOutcome.Refused;
                    }
                }
                else if (!waited)
                {
                    return DestinationOutcome.Unavailable;
                }

                Retries++;
            }

            FrozenPayload payload = encode != null ? encode(values) : NarrativeCommands.Ints(values);
            CommandAdmissionReceipt receipt = runtime.Submitter.Submit(route, target, CommandSchema, payload);
            if (!receipt.Admitted)
            {
                detail = Name + ": admission refused (" + receipt.Result.Kind + ")";
                return DestinationOutcome.Unavailable;
            }

            Submitted++;
            pending[attempt.OutboxId] = new Pending(receipt.Request, runtime.Delivery.Pass);
            return DestinationOutcome.Applied;
        }

        private sealed class Pending
        {
            public Pending(OperationId request, int pass)
            {
                Request = request;
                Pass = pass;
            }

            public OperationId Request { get; }

            public int Pass { get; }
        }
    }

    /// <summary>A destination port for presentation actions (playAudio, showMessage): it calls the sink once per obligation.</summary>
    public sealed class NarrativePresentationPort : IDestinationPort
    {
        private readonly NarrativeRuntime runtime;
        private readonly HashSet<Id128> applied = new HashSet<Id128>();

        public NarrativePresentationPort(NarrativeRuntime runtime, string name, SchemaRef schema)
        {
            this.runtime = runtime;
            Name = name;
            DestinationId = NarrativeDelivery.DestinationOf(name);
            CommandSchema = schema;
        }

        public string Name { get; }

        public Id128 DestinationId { get; }

        public SchemaRef CommandSchema { get; }

        public int Presented { get; private set; }

        public DestinationOutcome TryApply(in DeliveryAttempt attempt, out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            if (applied.Contains(attempt.OutboxId))
            {
                return DestinationOutcome.AlreadyApplied;
            }

            if (!NarrativeDelivery.TryDecode(attempt.PayloadBytes(), 3, out TargetId _, out int[] values)
                || !runtime.Models.TryGet(values[0], out ActionSetModel? set) || set == null
                || values[1] < 0 || values[1] >= set.Actions.Count)
            {
                code = DiagnosticCode.UnsupportedVersion;
                detail = Name + ": unknown action";
                return DestinationOutcome.Refused;
            }

            ActionModel action = set.Actions[values[1]];
            string source = "actions:" + set.Name;
            if (action.Kind == ActionKind.PlayAudio)
            {
                runtime.Feedback.OnFeedback(new FeedbackCue(runtime.Index.EntityAuthoringIdOf(values[2]), action.Text, 0, 0, 0, 0));
            }
            else
            {
                runtime.Messages.Show(new NarrativeMessage(action.Text, source));
            }

            applied.Add(attempt.OutboxId);
            Presented++;
            return DestinationOutcome.Applied;
        }
    }

    /// <summary>
    /// The narrative outbox of one world (P1.7a, A1): the world's one delivery owner, the in-step obligation recorder
    /// (IGameplayStepTap) and the destination ports.
    /// </summary>
    public sealed class NarrativeDelivery : IGameplayStepTap, IDisposable
    {
        public const int Capacity = 256;
        public const int TerminalRetention = 128;
        public const int MaxObligationsPerPass = 64;

        /// <summary>Obligations reserved per committed event when a command asks for room (refuse before mutation).</summary>
        public const int ReservePerEvent = 4;

        public static readonly SchemaRef PlayAudioSchema = GameplayIds.Schema("logic.delivery.play-audio", 1U);
        public static readonly SchemaRef ShowMessageSchema = GameplayIds.Schema("logic.delivery.show-message", 1U);

        /// <summary>Placeholder value replaced by the obligation's derived request id.</summary>
        private const int RequestSlot = int.MinValue;

        private readonly NarrativeRuntime runtime;
        private readonly Dictionary<string, IDestinationPort> ports = new Dictionary<string, IDestinationPort>(StringComparer.Ordinal);
        private readonly Dictionary<int, Id128> claimed = new Dictionary<int, Id128>();
        private readonly List<ObjectiveSignal> signal = new List<ObjectiveSignal>(1);
        private ulong ordinalStep = ulong.MaxValue;
        private int ordinal;

        public NarrativeDelivery(NarrativeRuntime runtime)
            : this(runtime, null)
        {
        }

        /// <summary>
        /// The delivery of <paramref name="runtime"/>'s world on <paramref name="owner"/> (the owner SaveService's
        /// DeliveryFactory made for the root; a restored root's owner already holds the reinstated obligations), or on a
        /// new owner when null. The owner must carry this world's narrative owner id (one owner per world).
        /// </summary>
        public NarrativeDelivery(NarrativeRuntime runtime, WorldDeliveryOwner? owner)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            OwnerId = OwnerIdOf(runtime.Index.WorldId);
            if (owner != null && !owner.OwnerId.Equals(OwnerId))
            {
                throw new InvalidOperationException(NarrativeDiagnosticCodes.ContentStale
                    + ": the delivery owner " + owner.OwnerId + " is not the narrative owner of world " + runtime.Index.WorldId
                    + "; make SaveServiceOptions.DeliveryFactory return NarrativeDelivery.CreateOwner (one owner per world)");
            }

            if (owner != null && owner.IsDisposed)
            {
                throw new InvalidOperationException("the delivery owner of world " + runtime.Index.WorldId + " is disposed");
            }

            Owner = owner ?? CreateOwner(runtime.Host, runtime.Index.WorldId);
            RegisterPorts();
        }

        public Id128 OwnerId { get; }

        /// <summary>The kernel delivery owner (outbox, adapter). SaveService captures its records.</summary>
        public WorldDeliveryOwner Owner { get; }

        public int Pass { get; private set; }

        /// <summary>Obligations recorded in-step (every kind).</summary>
        public int Described { get; private set; }

        /// <summary>Owed deliveries that could not be described (unknown action, missing target).</summary>
        public int Undescribable { get; private set; }

        /// <summary>Owed deliveries the outbox refused (at capacity); each is also logged once in <see cref="LastDropDetail"/>.</summary>
        public int Dropped { get; private set; }

        public string LastDropDetail { get; private set; } = string.Empty;

        /// <summary>Rule evaluations recorded from trigger events.</summary>
        public int Triggered { get; private set; }

        /// <summary>Quest edge-objective updates recorded from talk/interact signals.</summary>
        public int Signals { get; private set; }

        /// <summary>Interactable actions recorded from committed uses and trigger entries.</summary>
        public int ActionDemands { get; private set; }

        public int Claims { get; private set; }

        public int ClaimRefusals { get; private set; }

        public int Settled { get; private set; }

        public IReadOnlyDictionary<string, IDestinationPort> Ports => ports;

        public static Id128 DestinationOf(string portName) => GameplayIds.Id("narrative.port." + portName);

        /// <summary>The narrative owner id of a world.</summary>
        public static Id128 OwnerIdOf(string worldId) => GameplayIds.Id("narrative.outbox." + worldId);

        /// <summary>A new narrative delivery owner for a world (what SaveServiceOptions.DeliveryFactory returns).</summary>
        public static WorldDeliveryOwner CreateOwner(UnityWorldHost host, string worldId) =>
            new WorldDeliveryOwner(host, OwnerIdOf(worldId), Capacity, TerminalRetention, OutboxDurability.Volatile);

        /// <summary>
        /// The DeliveryFactory of a booted narrative world: its own owner for its root (so the save captures the
        /// obligations the narrative delivers), a new narrative owner for any other (restored) root.
        /// </summary>
        public static Func<GameApplicationRoot, ProductionWorldModules, WorldDeliveryOwner?> FactoryFor(NarrativeWorld world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            WorldDeliveryOwner initial = world.Delivery.Owner;
            GameApplicationRoot initialRoot = world.Root;
            string worldId = world.Runtime.Index.WorldId;
            return (root, modules) => ReferenceEquals(root, initialRoot) && !initial.IsDisposed ? initial : CreateOwner(root.Host, worldId);
        }

        /// <summary>Points <paramref name="options"/>' DeliveryFactory at <see cref="FactoryFor"/>(<paramref name="world"/>).</summary>
        public static void Configure(SaveServiceOptions options, NarrativeWorld world)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            options.DeliveryFactory = FactoryFor(world);
        }

        /// <summary>One delivery pass: hand open obligations to their ports (obligations are recorded in-step).</summary>
        public void Pump()
        {
            if (Owner.IsDisposed)
            {
                return;
            }

            Pass++;
            Owner.DispatchOpenObligations(MaxObligationsPerPass);
        }

        /// <summary>Reinstates outbox records (a restore, or a replay test); every reinstated obligation is delivered again, at most once in effect.</summary>
        public bool Reinstate(IReadOnlyList<OutboxRecordValue> rows, out string detail)
        {
            bool ok = Owner.TryReinstate(rows, out DiagnosticCode code, out detail);
            if (!ok && detail.Length == 0)
            {
                detail = code.ToString();
            }

            if (ok)
            {
                claimed.Clear();
                foreach (IDestinationPort port in ports.Values)
                {
                    if (port is NarrativeCommandPort command)
                    {
                        command.ClearPending();
                    }
                }
            }

            return ok;
        }

        public void Dispose() => Owner.Dispose();

        // ---- IGameplayStepTap (in-step) -----------------------------------------------------------------------------

        public bool HasRoom(int obligations)
        {
            if (Owner.IsDisposed)
            {
                return true;
            }

            long wanted = (long)Math.Max(0, obligations) * ReservePerEvent;
            return Owner.Outbox.OpenCount + wanted <= Owner.Outbox.Capacity;
        }

        public void OnCommitted(SchemaRef schema, FrozenPayload payload, OperationId causal)
        {
            if (Owner.IsDisposed)
            {
                return;
            }

            if (schema.Equals(LogicIds.ActionDueEvent) || schema.Equals(QuestIds.RewardGrantedEvent))
            {
                if (NarrativeEvent.TryDecode(payload, out NarrativeEvent e))
                {
                    bool described = schema.Equals(LogicIds.ActionDueEvent) ? DescribeAction(e, causal) : DescribeReward(e, causal);
                    if (!described)
                    {
                        Undescribable++;
                    }
                }
            }

            RecordTriggers(schema, payload, causal);
            RecordSignals(schema, payload, causal);
        }

        public bool OnActionDemand(string actionRef, string subjectAuthoringId, int subjectKey, int actorKey, int state, OperationId causal)
        {
            if (Owner.IsDisposed || string.IsNullOrEmpty(actionRef))
            {
                return false;
            }

            int actor = actorKey != 0 ? actorKey : runtime.ActorKey;
            NarrativeModelSet models = runtime.Models;
            if (string.Equals(actionRef, NarrativeActionRunner.Pickup, StringComparison.Ordinal))
            {
                int worldItem = subjectKey;
                if (!models.TryGetWorldItem(worldItem, out WorldItemModel? _)
                    && !string.IsNullOrEmpty(subjectAuthoringId) && models.TryResolve(subjectAuthoringId, out int resolved))
                {
                    worldItem = resolved;
                }

                if (!models.TryGetWorldItem(worldItem, out WorldItemModel? item) || item == null || !runtime.Index.TryInventoryTarget(0, out TargetId inventory))
                {
                    Undescribable++;
                    return false;
                }

                ActionDemands++;
                return Obligate("pickup", InventoryIds.PickupCommand, inventory, causal, item.Key, RequestSlot);
            }

            if (!models.TryResolve(actionRef, out int key) || !models.TryGet(key, out ActionSetModel? set) || set == null)
            {
                Undescribable++;
                return false;
            }

            ActionDemands++;
            return Obligate("run-actions", LogicIds.RunActionsCommand, runtime.Index.HubTarget, causal, set.Key, actor, subjectKey, RequestSlot);
        }

        public ObligationClaim Claim(int requestId)
        {
            if (!GameplayRequestIds.IsObligation(requestId))
            {
                return ObligationClaim.NotObligation;
            }

            if (!Owner.IsDisposed)
            {
                IReadOnlyList<DeliveryObligation> open = Owner.Outbox.OpenObligations();
                for (int i = 0; i < open.Count; i++)
                {
                    Id128 outboxId = open[i].Key.OutboxId;
                    if (GameplayRequestIds.OfObligation(outboxId) == requestId)
                    {
                        claimed[requestId] = outboxId;
                        Claims++;
                        return ObligationClaim.Apply;
                    }
                }
            }

            ClaimRefusals++;
            return ObligationClaim.AlreadyApplied;
        }

        public void Settle(int requestId)
        {
            if (Owner.IsDisposed || !claimed.TryGetValue(requestId, out Id128 outboxId))
            {
                return;
            }

            claimed.Remove(requestId);
            DeliveryOutcome outcome = Owner.Adapter.TryAcknowledge(outboxId, out DiagnosticCode _, out string _);
            if (outcome == DeliveryOutcome.Acknowledged)
            {
                Settled++;
            }
        }

        // ---- decoding helpers -------------------------------------------------------------------------------------

        /// <summary>Decodes an obligation payload: target id, then exactly <paramref name="ints"/> int32 values.</summary>
        public static bool TryDecode(byte[] bytes, int ints, out TargetId target, out int[] values)
        {
            target = default(TargetId);
            values = new int[ints];
            if (bytes == null || bytes.Length != 16 + ints * 4)
            {
                return false;
            }

            var reader = new GameplayPayloadReader(bytes);
            target = new TargetId(reader.Id());
            for (int i = 0; i < ints; i++)
            {
                values[i] = reader.Int32();
            }

            return true;
        }

        public static byte[] Encode(TargetId target, params int[] values)
        {
            var writer = new GameplayPayloadWriter().Id(target.Value);
            for (int i = 0; i < values.Length; i++)
            {
                writer.Int32(values[i]);
            }

            return writer.ToArray();
        }

        // ---- what an event owes -----------------------------------------------------------------------------------

        private bool DescribeAction(NarrativeEvent e, OperationId causal)
        {
            if (!runtime.Models.TryGet(e.A, out ActionSetModel? set) || set == null || e.B < 0 || e.B >= set.Actions.Count)
            {
                return false;
            }

            ActionModel action = set.Actions[e.B];
            int actor = e.D != 0 ? e.D : runtime.ActorKey;
            int subject = e.E;
            NarrativeIndex index = runtime.Index;
            switch (action.Kind)
            {
                case ActionKind.SetFact:
                case ActionKind.AddFact:
                {
                    int value = ActionRules.FactValueAfter(action, runtime.State.Fact(action.Key));
                    return Obligate("set-fact", DialogueIds.SetFactCommand, index.StateTarget, causal, action.Key, value, RequestSlot);
                }

                case ActionKind.Grant:
                case ActionKind.Consume:
                {
                    if (!index.TryInventoryTarget(action.Key2, out TargetId inventory))
                    {
                        return false;
                    }

                    bool grant = action.Kind == ActionKind.Grant;
                    return Obligate(grant ? "grant" : "consume", grant ? InventoryIds.GrantCommand : InventoryIds.ConsumeCommand,
                        inventory, causal, action.Key, action.Value, RequestSlot);
                }

                case ActionKind.Buy:
                {
                    // Key = item, Key2 = vendor, Value = count; addressed to the actor's inventory (InventoryCommands.Buy).
                    if (action.Key2 == 0 || !index.TryInventoryTarget(0, out TargetId inventory))
                    {
                        return false;
                    }

                    return Obligate("buy", InventoryIds.BuyCommand, inventory, causal, action.Key2, action.Key, Math.Max(1, action.Value), RequestSlot);
                }

                case ActionKind.Pickup:
                {
                    int worldItem = action.Ref.Length > 0 && runtime.Models.TryResolve(action.Ref, out int resolved) ? resolved : subject;
                    if (!index.TryInventoryTarget(0, out TargetId inventory))
                    {
                        return false;
                    }

                    return Obligate("pickup", InventoryIds.PickupCommand, inventory, causal, worldItem, RequestSlot);
                }

                case ActionKind.StartQuest:
                case ActionKind.CompleteQuest:
                case ActionKind.FailQuest:
                {
                    TargetId quest = index.TargetOf(NarrativeTargetKind.Quest, action.Key);
                    if (quest.IsDefault)
                    {
                        return false;
                    }

                    string port = action.Kind == ActionKind.StartQuest ? "quest-start" : (action.Kind == ActionKind.CompleteQuest ? "quest-complete" : "quest-fail");
                    SchemaRef schema = action.Kind == ActionKind.StartQuest ? QuestIds.StartCommand : (action.Kind == ActionKind.CompleteQuest ? QuestIds.CompleteCommand : QuestIds.FailCommand);
                    return Obligate(port, schema, quest, causal, RequestSlot);
                }

                case ActionKind.AdvanceQuest:
                {
                    TargetId quest = index.TargetOf(NarrativeTargetKind.Quest, action.Key);
                    return !quest.IsDefault && Obligate("quest-advance", QuestIds.AdvanceCommand, quest, causal, action.Value, RequestSlot);
                }

                case ActionKind.SetInteractableState:
                case ActionKind.Spawn:
                case ActionKind.Despawn:
                {
                    int entityKey = action.Ref.Length > 0 ? NarrativeRefs.EntityKey(action.Ref) : subject;
                    if (entityKey == 0 || !index.TryEntityTarget(entityKey, out TargetId entity))
                    {
                        return false;
                    }

                    if (action.Kind == ActionKind.SetInteractableState)
                    {
                        return Obligate("entity-set-variant", EntityDeclarations.SetVariantCommand, entity, causal, action.Value, RequestSlot);
                    }

                    return action.Kind == ActionKind.Spawn
                        ? Obligate("entity-spawn", EntityDeclarations.SpawnCommand, entity, causal, 0, RequestSlot)
                        : Obligate("entity-despawn", EntityDeclarations.DespawnCommand, entity, causal, 0, RequestSlot);
                }

                case ActionKind.Travel:
                {
                    if (!index.TryEntityTarget(actor == runtime.ActorKey ? 0 : actor, out TargetId traveller) || !AuthoringIds.IsValid(action.Ref))
                    {
                        return false;
                    }

                    return Obligate("world-travel", WorldDeclarations.TravelCommand, traveller, causal, AuthoringIds.StableKey(action.Ref), 0, RequestSlot);
                }

                case ActionKind.RestoreStamina:
                {
                    // Value = amount; addressed to the actor (the player unless the event names another actor).
                    if (!index.TryEntityTarget(actor == runtime.ActorKey ? 0 : actor, out TargetId player))
                    {
                        return false;
                    }

                    return Obligate("player-restore-stamina", PlayerStaminaIds.RestoreCommand, player, causal, action.Value, RequestSlot);
                }

                case ActionKind.StartDialogue:
                {
                    int speaker = action.Ref.Length > 0 ? NarrativeRefs.EntityKey(action.Ref) : 0;
                    return Obligate("dialogue-start", DialogueIds.StartCommand, index.StateTarget, causal, action.Key, speaker, actor, RequestSlot);
                }

                case ActionKind.PlayAudio:
                    return Obligate("play-audio", PlayAudioSchema, index.HubTarget, causal, set.Key, e.B, subject);
                case ActionKind.ShowMessage:
                    return Obligate("show-message", ShowMessageSchema, index.HubTarget, causal, set.Key, e.B, subject);
                default:
                    return false;
            }
        }

        private bool DescribeReward(NarrativeEvent e, OperationId causal)
        {
            if (e.C == QuestIds.RewardItem)
            {
                return runtime.Index.TryInventoryTarget(0, out TargetId inventory)
                    && Obligate("grant", InventoryIds.GrantCommand, inventory, causal, e.D, e.E, RequestSlot);
            }

            if (e.C == QuestIds.RewardFact)
            {
                return Obligate("set-fact", DialogueIds.SetFactCommand, runtime.Index.StateTarget, causal, e.D, e.E, RequestSlot);
            }

            return false;
        }

        /// <summary>Every rule a trigger event selects is evaluated through one logic.evaluate obligation, in priority order.</summary>
        private void RecordTriggers(SchemaRef schema, FrozenPayload payload, OperationId causal)
        {
            if (!LogicModule.TryTrigger(runtime, schema, payload, out TriggerKind kind, out int key, out int value, out int actor, out int subject))
            {
                return;
            }

            List<RuleModel> rules = RuleRules.Select(runtime.Models.Rules, kind, key, value);
            for (int i = 0; i < rules.Count; i++)
            {
                TargetId target = runtime.Index.TargetOf(NarrativeTargetKind.Rule, rules[i].Key);
                if (target.IsDefault)
                {
                    continue;
                }

                Triggered++;
                Obligate("logic-evaluate", LogicIds.EvaluateCommand, target, causal, actor, subject, (int)kind, key, value, RequestSlot);
            }
        }

        /// <summary>A talk or interact signal advances every matching edge objective through one quest.setObjective obligation.</summary>
        private void RecordSignals(SchemaRef schema, FrozenPayload payload, OperationId causal)
        {
            if (!TrySignal(schema, payload, out ObjectiveKind kind, out int key) || key == 0)
            {
                return;
            }

            signal.Clear();
            signal.Add(new ObjectiveSignal(kind, key));
            foreach (QuestModel quest in runtime.Models.Quests)
            {
                TargetId target = runtime.Index.TargetOf(NarrativeTargetKind.Quest, quest.Key);
                if (target.IsDefault)
                {
                    continue;
                }

                QuestState state = ReadQuest(quest, target);
                if (state.Status != QuestRules.Active)
                {
                    continue;
                }

                List<ObjectiveUpdate> updates = QuestTracking.Pending(quest, state, runtime.State, runtime.ActorKey, signal);
                for (int i = 0; i < updates.Count; i++)
                {
                    ObjectiveUpdate update = updates[i];
                    if (update.Objective < 0 || update.Objective >= quest.Objectives.Count || quest.Objectives[update.Objective].IsLevel)
                    {
                        continue;
                    }

                    Signals++;
                    Obligate("quest-objective", QuestIds.SetObjectiveCommand, target, causal, update.Objective, update.Count, RequestSlot);
                }
            }
        }

        private bool TrySignal(SchemaRef schema, FrozenPayload payload, out ObjectiveKind kind, out int key)
        {
            kind = ObjectiveKind.Talk;
            key = 0;
            if (schema.Equals(DialogueIds.EndedEvent))
            {
                if (!NarrativeEvent.TryDecode(payload, out NarrativeEvent ended))
                {
                    return false;
                }

                key = ended.A;
                return true;
            }

            if (schema.Equals(LogicDeclarations.InteractionSucceededEvent))
            {
                if (payload == null || payload.Length < 16)
                {
                    return false;
                }

                var reader = new GameplayPayloadReader(payload.Bytes);
                kind = ObjectiveKind.Interact;
                key = runtime.Index.EntityKeyOf(new TargetId(reader.Id()));
                return true;
            }

            if (schema.Equals(LogicIds.ActionsRunEvent) && NarrativeEvent.TryDecode(payload, out NarrativeEvent run) && run.C != 0)
            {
                kind = ObjectiveKind.Interact;
                key = run.C;
                return true;
            }

            return false;
        }

        private QuestState ReadQuest(QuestModel quest, TargetId target)
        {
            ICommittedSlotReader slots = runtime.Slots;
            var state = new QuestState(quest.Objectives.Count);
            state.Status = slots.ReadOrDefault(target, QuestIds.Owner, QuestIds.Status, 0);
            state.Stage = slots.ReadOrDefault(target, QuestIds.Owner, QuestIds.Stage, 0);
            state.Branch = slots.ReadOrDefault(target, QuestIds.Owner, QuestIds.Branch, 0);
            for (int n = 0; n < quest.Objectives.Count; n++)
            {
                state.Counts[n] = slots.ReadOrDefault(target, QuestIds.Owner, QuestIds.ObjectiveCount(n), 0);
                state.Done[n] = slots.ReadOrDefault(target, QuestIds.Owner, QuestIds.ObjectiveDone(n), 0);
            }

            return state;
        }

        // ---- the obligation itself --------------------------------------------------------------------------------

        /// <summary>
        /// Records one obligation in the executing step. Its identity derives from the world, the logical step and the
        /// obligation's ordinal within the step - deterministic for two boots fed the same commands, and unique across a
        /// restore because a restored world resumes at the captured step (SADR-012). The request id derives from it.
        /// </summary>
        private bool Obligate(string portName, SchemaRef schema, TargetId target, OperationId causal, params int[] values)
        {
            WorldMessagePlane? plane = runtime.Host.Messages;
            LogicalStepId step = plane != null ? plane.ExecutingStep : runtime.Host.CurrentStep;
            if (step.Value != ordinalStep)
            {
                ordinalStep = step.Value;
                ordinal = 0;
            }

            ordinal++;
            Id128 outboxId = GameplayIds.Id("narrative.obligation." + runtime.Index.WorldId + "."
                + step.Value.ToString(CultureInfo.InvariantCulture) + "." + ordinal.ToString(CultureInfo.InvariantCulture));
            int requestId = GameplayRequestIds.OfObligation(outboxId);
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == RequestSlot)
                {
                    values[i] = requestId;
                }
            }

            var key = new DeliveryKey(outboxId, DestinationOf(portName), outboxId);
            OutboxAdmission admission = Owner.Adapter.TryCommit(
                key,
                schema,
                Encode(target, values),
                EventSequence.Zero,
                step,
                default(AssemblyEpoch),
                causal,
                false,
                out DeliveryObligation? _,
                out DiagnosticCode code,
                out string detail);
            if (admission == OutboxAdmission.Accepted)
            {
                Described++;
                return true;
            }

            Dropped++;
            LastDropDetail = portName + " at step " + step.Value.ToString(CultureInfo.InvariantCulture) + ": " + admission + " " + code + " " + detail;
            UnityEngine.Debug.LogWarning("[narrative] an owed delivery was refused by the outbox: " + LastDropDetail);
            return false;
        }

        private void RegisterPorts()
        {
            Register(new NarrativeCommandPort(runtime, "grant", InventoryIds.GrantRoute, InventoryIds.GrantCommand, 3));
            Register(new NarrativeCommandPort(runtime, "consume", InventoryIds.ConsumeRoute, InventoryIds.ConsumeCommand, 3));
            Register(new NarrativeCommandPort(runtime, "pickup", InventoryIds.PickupRoute, InventoryIds.PickupCommand, 2));
            Register(new NarrativeCommandPort(runtime, "buy", InventoryIds.BuyRoute, InventoryIds.BuyCommand, 4));
            Register(new NarrativeCommandPort(runtime, "set-fact", DialogueIds.SetFactRoute, DialogueIds.SetFactCommand, 3));
            Register(new NarrativeCommandPort(runtime, "dialogue-start", DialogueIds.StartRoute, DialogueIds.StartCommand, 4));
            Register(new NarrativeCommandPort(runtime, "quest-start", QuestIds.StartRoute, QuestIds.StartCommand, 1));
            Register(new NarrativeCommandPort(runtime, "quest-advance", QuestIds.AdvanceRoute, QuestIds.AdvanceCommand, 2));
            Register(new NarrativeCommandPort(runtime, "quest-complete", QuestIds.CompleteRoute, QuestIds.CompleteCommand, 1));
            Register(new NarrativeCommandPort(runtime, "quest-fail", QuestIds.FailRoute, QuestIds.FailCommand, 1));
            Register(new NarrativeCommandPort(runtime, "quest-objective", QuestIds.SetObjectiveRoute, QuestIds.SetObjectiveCommand, 3));
            Register(new NarrativeCommandPort(runtime, "logic-evaluate", LogicIds.EvaluateRoute, LogicIds.EvaluateCommand, 6));
            Register(new NarrativeCommandPort(runtime, "run-actions", LogicIds.RunActionsRoute, LogicIds.RunActionsCommand, 4));
            Register(new NarrativeCommandPort(runtime, "entity-spawn", EntityDeclarations.SpawnRoute, EntityDeclarations.SpawnCommand, 2,
                values => EntityCommand.Encode(values[0], values[1])));
            Register(new NarrativeCommandPort(runtime, "entity-despawn", EntityDeclarations.DespawnRoute, EntityDeclarations.DespawnCommand, 2,
                values => EntityCommand.Encode(values[0], values[1])));
            Register(new NarrativeCommandPort(runtime, "entity-set-variant", EntityDeclarations.SetVariantRoute, EntityDeclarations.SetVariantCommand, 2,
                values => EntityCommand.Encode(values[0], values[1])));
            Register(new NarrativeCommandPort(runtime, "world-travel", WorldDeclarations.TravelRoute, WorldDeclarations.TravelCommand, 3,
                values => TravelPayload.Encode(values[0], values[1], values[2])));
            Register(new NarrativeCommandPort(runtime, "player-restore-stamina", PlayerStaminaIds.RestoreRoute, PlayerStaminaIds.RestoreCommand, 2));
            Register(new NarrativePresentationPort(runtime, "play-audio", PlayAudioSchema));
            Register(new NarrativePresentationPort(runtime, "show-message", ShowMessageSchema));
        }

        private void Register(IDestinationPort port)
        {
            string name = port is NarrativeCommandPort command ? command.Name : ((NarrativePresentationPort)port).Name;
            if (!Owner.TryRegisterDestination(port, out string detail))
            {
                throw new InvalidOperationException("narrative delivery port " + name + " refused: " + detail);
            }

            ports[name] = port;
        }
    }
}

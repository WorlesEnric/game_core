// GameCore.Gameplay.Logic - the narrative host stage and the narrative delivery (P1.4).
//
// NarrativeHost is one IGameplayInputSource: it runs in the world's input phase, before the frame's one pump, and
//   1. reads the events committed by the previous pump (its own cursor) and hands each to the listeners (the logic
//      module turns trigger events into logic.evaluate commands; the quest tracker collects talk/interact signals);
//   2. lets every listener finish the frame (the quest tracker submits quest.setObjective for changed objectives);
//   3. pumps the narrative delivery: committed ActionDue and RewardGranted events become outbox obligations, and open
//      obligations are handed to their destination ports, which submit ordinary commands (never pumping the world).
// Everything it submits is applied by the next pump, so the world is still pumped exactly once per frame.
//
// NarrativeDelivery owns a WorldDeliveryOwner (the kernel outbox, GC-021). An obligation's identity derives from the
// committed event (DeliveryKey.Derive), so seeing an event twice never makes a second obligation; the request id a
// port submits derives from the obligation, and the destination keeps a ring of applied request ids, so redelivery -
// after a lost acknowledgement, or a replayed outbox - answers AlreadyApplied and mutates nothing.
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
using GameCore.Rules.Gameplay.Logic;
using GameCore.Rules.Gameplay.Quest;
using GameCore.Unity.Runtime.Delivery;
using GameCore.Unity.Runtime.Messages;

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

    /// <summary>How a destination recognises an applied request: a ring of request ids on the target.</summary>
    public sealed class RequestRingSpec
    {
        public RequestRingSpec(OwnerId owner, Func<int, SlotId> slotOf, int requestIndex)
        {
            Owner = owner;
            SlotOf = slotOf;
            RequestIndex = requestIndex;
        }

        public OwnerId Owner { get; }

        public Func<int, SlotId> SlotOf { get; }

        /// <summary>Index of the request id among the command's int32 values.</summary>
        public int RequestIndex { get; }
    }

    /// <summary>
    /// A destination port that submits one narrative (or P1.1) command. Obligation payload: target id, then the command's
    /// int32 values. Outcomes: AlreadyApplied when the target's ring holds the request id (or a ringless command
    /// committed); Unavailable while the submitted command is pending (or deferred by a capacity refusal); Refused when
    /// the command was rejected; Applied when this attempt submitted it.
    /// </summary>
    public sealed class NarrativeCommandPort : IDestinationPort
    {
        /// <summary>Delivery passes before a command that committed without applying (inventory full) is retried.</summary>
        public const int RetryPasses = 60;

        private readonly NarrativeRuntime runtime;
        private readonly RouteId route;
        private readonly int ints;
        private readonly RequestRingSpec? ring;
        private readonly Func<int[], FrozenPayload>? encode;
        private readonly Dictionary<Id128, Pending> pending = new Dictionary<Id128, Pending>();

        public NarrativeCommandPort(NarrativeRuntime runtime, string name, RouteId route, SchemaRef schema, int ints, RequestRingSpec? ring, Func<int[], FrozenPayload>? encode = null)
        {
            this.runtime = runtime;
            this.route = route;
            this.ints = ints;
            this.ring = ring;
            this.encode = encode;
            Name = name;
            DestinationId = NarrativeDelivery.DestinationOf(name);
            CommandSchema = schema;
        }

        public string Name { get; }

        public Id128 DestinationId { get; }

        public SchemaRef CommandSchema { get; }

        public int Submitted { get; private set; }

        public int AlreadyApplied { get; private set; }

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

            int requestId = ring != null ? values[ring.RequestIndex] : 0;
            if (ring != null && RingHolds(target, requestId))
            {
                pending.Remove(attempt.OutboxId);
                AlreadyApplied++;
                return DestinationOutcome.AlreadyApplied;
            }

            if (pending.TryGetValue(attempt.OutboxId, out Pending? earlier) && earlier != null)
            {
                if (runtime.Submitter.TryOutcome(earlier.Request, out RequestResultKind kind, out DiagnosticCode reason))
                {
                    switch (kind)
                    {
                        case RequestResultKind.Accepted:
                            return DestinationOutcome.Unavailable;
                        case RequestResultKind.Committed:
                            if (ring == null)
                            {
                                pending.Remove(attempt.OutboxId);
                                AlreadyApplied++;
                                return DestinationOutcome.AlreadyApplied;
                            }

                            if (runtime.Delivery.Pass - earlier.Pass < RetryPasses)
                            {
                                detail = Name + ": committed without applying (deferred)";
                                return DestinationOutcome.Unavailable;
                            }

                            break;
                        default:
                            pending.Remove(attempt.OutboxId);
                            if (reason == DiagnosticCode.IdempotencyConflict)
                            {
                                AlreadyApplied++;
                                return DestinationOutcome.AlreadyApplied;
                            }

                            code = reason == DiagnosticCode.None ? DiagnosticCode.Ineligible : reason;
                            detail = Name + ": the command was refused (" + code + ")";
                            return DestinationOutcome.Refused;
                    }
                }
                else if (ring == null)
                {
                    pending.Remove(attempt.OutboxId);
                    AlreadyApplied++;
                    return DestinationOutcome.AlreadyApplied;
                }
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

        private bool RingHolds(TargetId target, int requestId)
        {
            if (ring == null || !RequestRing.IsTracked(requestId))
            {
                return false;
            }

            for (int i = 0; i < NarrativeKeys.RequestRingSize; i++)
            {
                if (runtime.Slots.ReadOrDefault(target, ring.Owner, ring.SlotOf(i), 0) == requestId)
                {
                    return true;
                }
            }

            return false;
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
                runtime.Feedback.OnFeedback(new NarrativeFeedbackCue(action.Text, source, values[2]));
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

    /// <summary>The narrative outbox: ActionDue and RewardGranted events to destination commands, exactly once each.</summary>
    public sealed class NarrativeDelivery : IDeliveryObligationSource, IDisposable
    {
        public const int Capacity = 256;
        public const int TerminalRetention = 128;
        public const int MaxObligationsPerPass = 64;

        public static readonly SchemaRef PlayAudioSchema = GameplayIds.Schema("logic.delivery.play-audio", 1U);
        public static readonly SchemaRef ShowMessageSchema = GameplayIds.Schema("logic.delivery.show-message", 1U);

        private readonly NarrativeRuntime runtime;
        private readonly Dictionary<string, IDestinationPort> ports = new Dictionary<string, IDestinationPort>(StringComparer.Ordinal);

        public NarrativeDelivery(NarrativeRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            OwnerId = GameplayIds.Id("narrative.outbox." + runtime.Index.WorldId);
            Owner = new WorldDeliveryOwner(runtime.Host, OwnerId, Capacity, TerminalRetention, OutboxDurability.Volatile);
            var inventoryRing = new RequestRingSpec(InventoryIds.Owner, InventoryIds.Req, 2);
            var factRing = new RequestRingSpec(DialogueIds.Owner, DialogueIds.Req, 2);
            Register(new NarrativeCommandPort(runtime, "grant", InventoryIds.GrantRoute, InventoryIds.GrantCommand, 3, inventoryRing));
            Register(new NarrativeCommandPort(runtime, "consume", InventoryIds.ConsumeRoute, InventoryIds.ConsumeCommand, 3, inventoryRing));
            Register(new NarrativeCommandPort(runtime, "pickup", InventoryIds.PickupRoute, InventoryIds.PickupCommand, 2, new RequestRingSpec(InventoryIds.Owner, InventoryIds.Req, 1)));
            Register(new NarrativeCommandPort(runtime, "set-fact", DialogueIds.SetFactRoute, DialogueIds.SetFactCommand, 3, factRing));
            Register(new NarrativeCommandPort(runtime, "dialogue-start", DialogueIds.StartRoute, DialogueIds.StartCommand, 3, null));
            Register(new NarrativeCommandPort(runtime, "quest-start", QuestIds.StartRoute, QuestIds.StartCommand, 1, new RequestRingSpec(QuestIds.Owner, QuestIds.Req, 0)));
            Register(new NarrativeCommandPort(runtime, "quest-advance", QuestIds.AdvanceRoute, QuestIds.AdvanceCommand, 2, new RequestRingSpec(QuestIds.Owner, QuestIds.Req, 1)));
            Register(new NarrativeCommandPort(runtime, "quest-complete", QuestIds.CompleteRoute, QuestIds.CompleteCommand, 1, new RequestRingSpec(QuestIds.Owner, QuestIds.Req, 0)));
            Register(new NarrativeCommandPort(runtime, "quest-fail", QuestIds.FailRoute, QuestIds.FailCommand, 1, new RequestRingSpec(QuestIds.Owner, QuestIds.Req, 0)));
            Register(new NarrativeCommandPort(runtime, "entity-spawn", EntityDeclarations.SpawnRoute, EntityDeclarations.SpawnCommand, 1, null));
            Register(new NarrativeCommandPort(runtime, "entity-despawn", EntityDeclarations.DespawnRoute, EntityDeclarations.DespawnCommand, 1, null));
            Register(new NarrativeCommandPort(runtime, "entity-set-variant", EntityDeclarations.SetVariantRoute, EntityDeclarations.SetVariantCommand, 1, null));
            Register(new NarrativeCommandPort(runtime, "world-travel", WorldDeclarations.TravelRoute, WorldDeclarations.TravelCommand, 2, null,
                values => TravelPayload.Encode(values[0], values[1])));
            Register(new NarrativePresentationPort(runtime, "play-audio", PlayAudioSchema));
            Register(new NarrativePresentationPort(runtime, "show-message", ShowMessageSchema));
        }

        public Id128 OwnerId { get; }

        /// <summary>The kernel delivery owner (outbox, adapter, cursor). P1.2's save captures its records.</summary>
        public WorldDeliveryOwner Owner { get; }

        public int Pass { get; private set; }

        public int Described { get; private set; }

        public int Undescribable { get; private set; }

        public IReadOnlyDictionary<string, IDestinationPort> Ports => ports;

        public static Id128 DestinationOf(string portName) => GameplayIds.Id("narrative.port." + portName);

        /// <summary>One delivery pass: poll committed events into obligations, then hand open obligations to their ports.</summary>
        public void Pump()
        {
            if (Owner.IsDisposed)
            {
                return;
            }

            Pass++;
            Owner.PollCommittedEvents(this, default(OperationId), NarrativeHost.MaxEventsPerFrame);
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

            return ok;
        }

        public bool TryDescribe(in CommittedEvent committed, out DeliveryObligationRequest request)
        {
            request = default(DeliveryObligationRequest);
            bool due = committed.Schema.Equals(LogicIds.ActionDueEvent);
            bool reward = committed.Schema.Equals(QuestIds.RewardGrantedEvent);
            if ((!due && !reward) || !NarrativeEvent.TryDecode(committed.Payload, out NarrativeEvent e))
            {
                return false;
            }

            bool described = due ? DescribeAction(committed, e, out request) : DescribeReward(committed, e, out request);
            if (described)
            {
                Described++;
            }
            else
            {
                Undescribable++;
            }

            return described;
        }

        public void Dispose() => Owner.Dispose();

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

        private bool DescribeAction(CommittedEvent committed, NarrativeEvent e, out DeliveryObligationRequest request)
        {
            request = default(DeliveryObligationRequest);
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
                    return Request("set-fact", DialogueIds.SetFactCommand, committed, index.StateTarget, out request, action.Key, value, RequestSlot);
                }

                case ActionKind.Grant:
                case ActionKind.Consume:
                {
                    if (!index.TryInventoryTarget(action.Key2, out TargetId inventory))
                    {
                        return false;
                    }

                    bool grant = action.Kind == ActionKind.Grant;
                    return Request(grant ? "grant" : "consume", grant ? InventoryIds.GrantCommand : InventoryIds.ConsumeCommand,
                        committed, inventory, out request, action.Key, action.Value, RequestSlot);
                }

                case ActionKind.Pickup:
                {
                    int worldItem = action.Ref.Length > 0 && runtime.Models.TryResolve(action.Ref, out int resolved) ? resolved : subject;
                    if (!index.TryInventoryTarget(0, out TargetId inventory))
                    {
                        return false;
                    }

                    return Request("pickup", InventoryIds.PickupCommand, committed, inventory, out request, worldItem, RequestSlot);
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
                    return Request(port, schema, committed, quest, out request, RequestSlot);
                }

                case ActionKind.AdvanceQuest:
                {
                    TargetId quest = index.TargetOf(NarrativeTargetKind.Quest, action.Key);
                    return !quest.IsDefault && Request("quest-advance", QuestIds.AdvanceCommand, committed, quest, out request, action.Value, RequestSlot);
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
                        return Request("entity-set-variant", EntityDeclarations.SetVariantCommand, committed, entity, out request, action.Value);
                    }

                    return action.Kind == ActionKind.Spawn
                        ? Request("entity-spawn", EntityDeclarations.SpawnCommand, committed, entity, out request, 0)
                        : Request("entity-despawn", EntityDeclarations.DespawnCommand, committed, entity, out request, 0);
                }

                case ActionKind.Travel:
                {
                    if (!index.TryEntityTarget(actor == runtime.ActorKey ? 0 : actor, out TargetId traveller) || !AuthoringIds.IsValid(action.Ref))
                    {
                        return false;
                    }

                    return Request("world-travel", WorldDeclarations.TravelCommand, committed, traveller, out request, AuthoringIds.StableKey(action.Ref), 0);
                }

                case ActionKind.StartDialogue:
                {
                    int speaker = action.Ref.Length > 0 ? NarrativeRefs.EntityKey(action.Ref) : 0;
                    return Request("dialogue-start", DialogueIds.StartCommand, committed, index.StateTarget, out request, action.Key, speaker, actor);
                }

                case ActionKind.PlayAudio:
                    return Request("play-audio", PlayAudioSchema, committed, index.HubTarget, out request, set.Key, e.B, subject);
                case ActionKind.ShowMessage:
                    return Request("show-message", ShowMessageSchema, committed, index.HubTarget, out request, set.Key, e.B, subject);
                default:
                    return false;
            }
        }

        private bool DescribeReward(CommittedEvent committed, NarrativeEvent e, out DeliveryObligationRequest request)
        {
            request = default(DeliveryObligationRequest);
            if (e.C == QuestIds.RewardItem)
            {
                return runtime.Index.TryInventoryTarget(0, out TargetId inventory)
                    && Request("grant", InventoryIds.GrantCommand, committed, inventory, out request, e.D, e.E, RequestSlot);
            }

            if (e.C == QuestIds.RewardFact)
            {
                return Request("set-fact", DialogueIds.SetFactCommand, committed, runtime.Index.StateTarget, out request, e.D, e.E, RequestSlot);
            }

            return false;
        }

        /// <summary>Placeholder value replaced by the obligation's derived request id.</summary>
        private const int RequestSlot = int.MinValue;

        private bool Request(string portName, SchemaRef schema, CommittedEvent committed, TargetId target, out DeliveryObligationRequest request, params int[] values)
        {
            Id128 destination = DestinationOf(portName);
            DeliveryKey key = DeliveryKey.Derive(runtime.Host.World, committed.Cursor.Sequence, destination, schema);
            int requestId = NarrativeKeys.RequestIdOf(key.OutboxId);
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == RequestSlot)
                {
                    values[i] = requestId;
                }
            }

            request = new DeliveryObligationRequest(destination, schema, Encode(target, values), false);
            return true;
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

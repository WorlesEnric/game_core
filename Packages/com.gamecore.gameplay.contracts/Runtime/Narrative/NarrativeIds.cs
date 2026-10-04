// GameCore.Gameplay.Contracts.Narrative - stable identities of the dialogue, quest, inventory and logic plugins
// (P1.4, catalog rows 6-9).
//
// Like GameplaySlots (P1.1), every owner, slot, route, schema and target here is a pure derivation of a stable name, so
// a package (or Studio, or the UI of P1.5) can read another plugin's committed slots, decode its events or submit its
// commands without a type dependency on it:
//
//   dialogue plugin (owner gameplay.dialogue.owner)
//     on the narrative state target (one per world, DialogueIds.StateTarget):
//       dialogue.active, dialogue.graph (graph key), dialogue.node, dialogue.speaker (speaker key), dialogue.listener,
//       dialogue.choiceCount, dialogue.choiceMask (bit i = option i is available), dialogue.serial (bumps per shown
//       node), dialogue.reqHead + dialogue.req.<i> (ring of applied request ids), narrative.fact.<name> (one per fact)
//     on every dialogue graph target: dialogue.visited.<w> (bit n of word w = node 32w+n visited)
//   quest plugin (owner gameplay.quest.owner), on every quest target:
//     quest.status (0 inactive, 1 active, 2 completed, 3 failed), quest.stage, quest.branch, quest.obj.<n>.count,
//     quest.obj.<n>.done, quest.reqHead + quest.req.<i>
//   inventory plugin (owner gameplay.inventory.owner)
//     on every inventory target: inv.item.<k> (item key, 0 = empty), inv.count.<k>, inv.currency, inv.reqHead + inv.req.<i>
//     on every world item target: item.taken, item.key, item.count
//   logic plugin (owner gameplay.logic.owner)
//     on every rule target: logic.fired, logic.cooldownMs (domain ms at which the rule is ready again), logic.counter
//     on the logic hub target (one per world): logic.invocations
//
// Keys carried by int32 slots and payloads are positive int31 values: AuthoringIds.StableKey of a definition's
// authoring id, or NarrativeKeys.FactKey of a fact name. The bake refuses two objects with one key.
#nullable enable
using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Gameplay.Contracts.Narrative
{
    /// <summary>Keys of fact names and other name-addressed things.</summary>
    public static class NarrativeKeys
    {
        /// <summary>Longest fact name accepted by the bake.</summary>
        public const int MaxFactNameLength = 48;

        /// <summary>Size of every request-id ring (<c>&lt;group&gt;.req.0..7</c>).</summary>
        public const int RequestRingSize = 8;

        /// <summary>A fact name is lowercase: a letter, then letters, digits or underscores.</summary>
        public static bool IsValidFactName(string? name)
        {
            if (string.IsNullOrEmpty(name) || name!.Length > MaxFactNameLength)
            {
                return false;
            }

            if (name[0] < 'a' || name[0] > 'z')
            {
                return false;
            }

            for (int i = 1; i < name.Length; i++)
            {
                char c = name[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The positive int31 key of a fact name (<c>gameplay.fact.&lt;name&gt;</c>).</summary>
        public static int FactKey(string factName)
        {
            if (!IsValidFactName(factName))
            {
                throw new ArgumentException("'" + factName + "' is not a fact name (lowercase letter, then [a-z0-9_], at most "
                    + MaxFactNameLength.ToString(CultureInfo.InvariantCulture) + " characters).", nameof(factName));
            }

            return NameKey("gameplay.fact." + factName);
        }

        /// <summary>The positive int31 key of a speaker that has no authoring id (its display name, lowercased).</summary>
        public static int SpeakerNameKey(string speakerName)
        {
            string text = (speakerName ?? string.Empty).Trim().ToLowerInvariant();
            return NameKey("gameplay.speaker." + (text.Length == 0 ? "none" : text));
        }

        /// <summary>First four SHA-256 bytes of <paramref name="text"/>, big-endian, masked to a positive int31 (0 maps to 1).</summary>
        public static int NameKey(string text)
        {
            byte[] digest;
            using (SHA256 sha = SHA256.Create())
            {
                digest = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
            }

            int value = ((digest[0] & 0x7F) << 24) | (digest[1] << 16) | (digest[2] << 8) | digest[3];
            return value == 0 ? 1 : value;
        }

        /// <summary>The positive int31 request id of an obligation or a one-off command identity.</summary>
        public static int RequestIdOf(Id128 identity)
        {
            int value = (int)((identity.High >> 33) & 0x7FFFFFFFUL);
            return value == 0 ? 1 : value;
        }
    }

    /// <summary>Owner, slots, routes, schemas and targets of the dialogue plugin.</summary>
    public static class DialogueIds
    {
        public const uint SchemaVersion = 1U;

        public static readonly OwnerId Owner = GameplayIds.Owner("dialogue.owner");

        public static readonly SlotId Active = SlotNames.Of("dialogue", "active");
        public static readonly SlotId Graph = SlotNames.Of("dialogue", "graph");
        public static readonly SlotId Node = SlotNames.Of("dialogue", "node");
        public static readonly SlotId Speaker = SlotNames.Of("dialogue", "speaker");
        public static readonly SlotId Listener = SlotNames.Of("dialogue", "listener");
        public static readonly SlotId ChoiceCount = SlotNames.Of("dialogue", "choiceCount");
        public static readonly SlotId ChoiceMask = SlotNames.Of("dialogue", "choiceMask");
        public static readonly SlotId Serial = SlotNames.Of("dialogue", "serial");
        public static readonly SlotId ReqHead = SlotNames.Of("dialogue", "reqHead");

        /// <summary>Slot <c>dialogue.req.&lt;i&gt;</c> of the applied-request ring.</summary>
        public static SlotId Req(int index) => SlotNames.Of("dialogue", "req." + index.ToString(CultureInfo.InvariantCulture));

        /// <summary>Slot <c>dialogue.visited.&lt;w&gt;</c> of a graph target (bit n = node 32w + n).</summary>
        public static SlotId Visited(int word) => SlotNames.Of("dialogue", "visited." + word.ToString(CultureInfo.InvariantCulture));

        /// <summary>Slot <c>narrative.fact.&lt;name&gt;</c> of the narrative state target.</summary>
        public static SlotId Fact(string factName)
        {
            if (!NarrativeKeys.IsValidFactName(factName))
            {
                throw new ArgumentException("'" + factName + "' is not a fact name.", nameof(factName));
            }

            return SlotNames.Of("narrative", "fact." + factName);
        }

        /// <summary>The one narrative state target of a world (facts and the conversation).</summary>
        public static TargetId StateTarget(string worldId) => GameplayIds.Target("dialogue.state." + worldId);

        public static readonly RouteId StartRoute = GameplayIds.Route("dialogue.route.start");
        public static readonly RouteId ChooseRoute = GameplayIds.Route("dialogue.route.choose");
        public static readonly RouteId AdvanceRoute = GameplayIds.Route("dialogue.route.advance");
        public static readonly RouteId InterruptRoute = GameplayIds.Route("dialogue.route.interrupt");
        public static readonly RouteId SetFactRoute = GameplayIds.Route("narrative.route.set-fact");

        /// <summary>dialogue.start: graph key, speaker key, listener key.</summary>
        public static readonly SchemaRef StartCommand = GameplayIds.Schema("dialogue.command.start", 1U);

        /// <summary>dialogue.choose: option index.</summary>
        public static readonly SchemaRef ChooseCommand = GameplayIds.Schema("dialogue.command.choose", 1U);

        /// <summary>dialogue.advance: 0.</summary>
        public static readonly SchemaRef AdvanceCommand = GameplayIds.Schema("dialogue.command.advance", 1U);

        /// <summary>dialogue.interrupt: 0.</summary>
        public static readonly SchemaRef InterruptCommand = GameplayIds.Schema("dialogue.command.interrupt", 1U);

        /// <summary>narrative.setFact: fact key, value, request id.</summary>
        public static readonly SchemaRef SetFactCommand = GameplayIds.Schema("narrative.command.set-fact", 1U);

        /// <summary>DialogueStarted: A=graph, B=entry node, C=speaker, D=listener.</summary>
        public static readonly SchemaRef StartedEvent = GameplayIds.Schema("dialogue.event.started", 1U);

        /// <summary>LineShown: A=graph, B=node, C=speaker, D=listener, E=serial.</summary>
        public static readonly SchemaRef LineShownEvent = GameplayIds.Schema("dialogue.event.line-shown", 1U);

        /// <summary>ChoiceOffered: A=graph, B=node, C=choice count, D=choice mask, E=serial.</summary>
        public static readonly SchemaRef ChoiceOfferedEvent = GameplayIds.Schema("dialogue.event.choice-offered", 1U);

        /// <summary>ChoiceMade: A=graph, B=node, C=option index, D=next node.</summary>
        public static readonly SchemaRef ChoiceMadeEvent = GameplayIds.Schema("dialogue.event.choice-made", 1U);

        /// <summary>DialogueEnded: A=graph, B=last node, C=speaker, D=listener, E=reason (0 end node, 1 interrupt, 2 dead end).</summary>
        public static readonly SchemaRef EndedEvent = GameplayIds.Schema("dialogue.event.ended", 1U);

        /// <summary>FactSet: A=fact key, B=new value, C=previous value, D=request id (0 when set by a dialogue node).</summary>
        public static readonly SchemaRef FactSetEvent = GameplayIds.Schema("dialogue.event.fact-set", 1U);

        /// <summary>DialogueActionNode: A=graph, B=node, C=action set key, D=speaker, E=listener.</summary>
        public static readonly SchemaRef ActionNodeEvent = GameplayIds.Schema("dialogue.event.action-node", 1U);

        public const int EndReasonEndNode = 0;
        public const int EndReasonInterrupt = 1;
        public const int EndReasonDeadEnd = 2;
    }

    /// <summary>Owner, slots, routes and schemas of the quest plugin.</summary>
    public static class QuestIds
    {
        public static readonly OwnerId Owner = GameplayIds.Owner("quest.owner");

        public static readonly SlotId Status = SlotNames.Of("quest", "status");
        public static readonly SlotId Stage = SlotNames.Of("quest", "stage");
        public static readonly SlotId Branch = SlotNames.Of("quest", "branch");
        public static readonly SlotId ReqHead = SlotNames.Of("quest", "reqHead");

        public static SlotId Req(int index) => SlotNames.Of("quest", "req." + index.ToString(CultureInfo.InvariantCulture));

        /// <summary>Slot <c>quest.obj.&lt;n&gt;.count</c>.</summary>
        public static SlotId ObjectiveCount(int objective) =>
            SlotNames.Of("quest", "obj." + objective.ToString(CultureInfo.InvariantCulture) + ".count");

        /// <summary>Slot <c>quest.obj.&lt;n&gt;.done</c>.</summary>
        public static SlotId ObjectiveDone(int objective) =>
            SlotNames.Of("quest", "obj." + objective.ToString(CultureInfo.InvariantCulture) + ".done");

        public const int Inactive = 0;
        public const int Active = 1;
        public const int Completed = 2;
        public const int Failed = 3;

        public static readonly RouteId StartRoute = GameplayIds.Route("quest.route.start");
        public static readonly RouteId AdvanceRoute = GameplayIds.Route("quest.route.advance");
        public static readonly RouteId SetObjectiveRoute = GameplayIds.Route("quest.route.set-objective");
        public static readonly RouteId FailRoute = GameplayIds.Route("quest.route.fail");
        public static readonly RouteId CompleteRoute = GameplayIds.Route("quest.route.complete");

        /// <summary>quest.start: request id.</summary>
        public static readonly SchemaRef StartCommand = GameplayIds.Schema("quest.command.start", 1U);

        /// <summary>quest.advance: stage, request id.</summary>
        public static readonly SchemaRef AdvanceCommand = GameplayIds.Schema("quest.command.advance", 1U);

        /// <summary>quest.setObjective: objective index, count.</summary>
        public static readonly SchemaRef SetObjectiveCommand = GameplayIds.Schema("quest.command.set-objective", 1U);

        /// <summary>quest.fail: request id.</summary>
        public static readonly SchemaRef FailCommand = GameplayIds.Schema("quest.command.fail", 1U);

        /// <summary>quest.complete: request id.</summary>
        public static readonly SchemaRef CompleteCommand = GameplayIds.Schema("quest.command.complete", 1U);

        /// <summary>QuestStarted: A=quest key, B=first stage.</summary>
        public static readonly SchemaRef StartedEvent = GameplayIds.Schema("quest.event.started", 1U);

        /// <summary>StageEntered: A=quest key, B=stage, C=previous stage, D=branch.</summary>
        public static readonly SchemaRef StageEnteredEvent = GameplayIds.Schema("quest.event.stage-entered", 1U);

        /// <summary>ObjectiveUpdated: A=quest key, B=objective, C=count, D=done.</summary>
        public static readonly SchemaRef ObjectiveUpdatedEvent = GameplayIds.Schema("quest.event.objective-updated", 1U);

        /// <summary>QuestCompleted: A=quest key, B=last stage, C=branch.</summary>
        public static readonly SchemaRef CompletedEvent = GameplayIds.Schema("quest.event.completed", 1U);

        /// <summary>QuestFailed: A=quest key, B=stage.</summary>
        public static readonly SchemaRef FailedEvent = GameplayIds.Schema("quest.event.failed", 1U);

        /// <summary>RewardGranted: A=quest key, B=reward index, C=kind (1 item, 2 fact), D=item or fact key, E=count or value.</summary>
        public static readonly SchemaRef RewardGrantedEvent = GameplayIds.Schema("quest.event.reward-granted", 1U);

        public const int RewardItem = 1;
        public const int RewardFact = 2;
    }

    /// <summary>Owner, slots, routes and schemas of the inventory plugin.</summary>
    public static class InventoryIds
    {
        public static readonly OwnerId Owner = GameplayIds.Owner("inventory.owner");

        public static readonly SlotId Currency = SlotNames.Of("inv", "currency");
        public static readonly SlotId ReqHead = SlotNames.Of("inv", "reqHead");

        public static SlotId Req(int index) => SlotNames.Of("inv", "req." + index.ToString(CultureInfo.InvariantCulture));

        /// <summary>Slot <c>inv.item.&lt;k&gt;</c>: item key held in slot k (0 = empty).</summary>
        public static SlotId Item(int slot) => SlotNames.Of("inv", "item." + slot.ToString(CultureInfo.InvariantCulture));

        /// <summary>Slot <c>inv.count.&lt;k&gt;</c>: stack size in slot k.</summary>
        public static SlotId Count(int slot) => SlotNames.Of("inv", "count." + slot.ToString(CultureInfo.InvariantCulture));

        public static readonly SlotId ItemTaken = SlotNames.Of("item", "taken");
        public static readonly SlotId ItemKey = SlotNames.Of("item", "key");
        public static readonly SlotId ItemCount = SlotNames.Of("item", "count");

        public static readonly RouteId GrantRoute = GameplayIds.Route("inventory.route.grant");
        public static readonly RouteId ConsumeRoute = GameplayIds.Route("inventory.route.consume");
        public static readonly RouteId DropRoute = GameplayIds.Route("inventory.route.drop");
        public static readonly RouteId TransferRoute = GameplayIds.Route("inventory.route.transfer");
        public static readonly RouteId BuyRoute = GameplayIds.Route("inventory.route.buy");
        public static readonly RouteId SellRoute = GameplayIds.Route("inventory.route.sell");
        public static readonly RouteId PickupRoute = GameplayIds.Route("inventory.route.pickup");

        /// <summary>inv.grant: item key, count, request id.</summary>
        public static readonly SchemaRef GrantCommand = GameplayIds.Schema("inventory.command.grant", 1U);

        /// <summary>inv.consume: item key, count, request id.</summary>
        public static readonly SchemaRef ConsumeCommand = GameplayIds.Schema("inventory.command.consume", 1U);

        /// <summary>inv.drop: item key, count.</summary>
        public static readonly SchemaRef DropCommand = GameplayIds.Schema("inventory.command.drop", 1U);

        /// <summary>inv.transfer: destination inventory key, item key, count.</summary>
        public static readonly SchemaRef TransferCommand = GameplayIds.Schema("inventory.command.transfer", 1U);

        /// <summary>inv.buy: vendor key, item key, count, request id.</summary>
        public static readonly SchemaRef BuyCommand = GameplayIds.Schema("inventory.command.buy", 1U);

        /// <summary>inv.sell: vendor key, item key, count, request id.</summary>
        public static readonly SchemaRef SellCommand = GameplayIds.Schema("inventory.command.sell", 1U);

        /// <summary>inv.pickup: world item key, request id.</summary>
        public static readonly SchemaRef PickupCommand = GameplayIds.Schema("inventory.command.pickup", 1U);

        /// <summary>ItemGranted: A=item key, B=count granted, C=new total, D=request id, E=source (0 grant, 1 buy, 2 pickup, 3 transfer).</summary>
        public static readonly SchemaRef ItemGrantedEvent = GameplayIds.Schema("inventory.event.item-granted", 1U);

        /// <summary>ItemConsumed: A=item key, B=count, C=remaining, D=request id.</summary>
        public static readonly SchemaRef ItemConsumedEvent = GameplayIds.Schema("inventory.event.item-consumed", 1U);

        /// <summary>ItemDropped: A=item key, B=count, C=remaining.</summary>
        public static readonly SchemaRef ItemDroppedEvent = GameplayIds.Schema("inventory.event.item-dropped", 1U);

        /// <summary>ItemTransferred: A=item key, B=count, C=destination inventory key.</summary>
        public static readonly SchemaRef ItemTransferredEvent = GameplayIds.Schema("inventory.event.item-transferred", 1U);

        /// <summary>TradeDone: A=vendor key, B=item key, C=count, D=total price, E=direction (1 buy, 2 sell), F=request id.</summary>
        public static readonly SchemaRef TradeDoneEvent = GameplayIds.Schema("inventory.event.trade-done", 1U);

        /// <summary>InventoryFull: A=item key, B=requested count, C=reason (1 no slot, 2 weight).</summary>
        public static readonly SchemaRef InventoryFullEvent = GameplayIds.Schema("inventory.event.inventory-full", 1U);

        /// <summary>ItemPickedUp (on the world item target): A=world item key, B=item key, C=count, D=inventory key.</summary>
        public static readonly SchemaRef ItemPickedUpEvent = GameplayIds.Schema("inventory.event.item-picked-up", 1U);

        public const int SourceGrant = 0;
        public const int SourceBuy = 1;
        public const int SourcePickup = 2;
        public const int SourceTransfer = 3;
        public const int TradeBuy = 1;
        public const int TradeSell = 2;
    }

    /// <summary>Owner, slots, routes and schemas of the logic plugin.</summary>
    public static class LogicIds
    {
        public static readonly OwnerId Owner = GameplayIds.Owner("logic.owner");

        public static readonly SlotId Fired = SlotNames.Of("logic", "fired");
        public static readonly SlotId CooldownMs = SlotNames.Of("logic", "cooldownMs");
        public static readonly SlotId Counter = SlotNames.Of("logic", "counter");
        public static readonly SlotId Invocations = SlotNames.Of("logic", "invocations");

        /// <summary>The one logic hub target of a world (action-set runs are committed on it).</summary>
        public static TargetId HubTarget(string worldId) => GameplayIds.Target("logic.hub." + worldId);

        public static readonly RouteId EvaluateRoute = GameplayIds.Route("logic.route.evaluate");
        public static readonly RouteId RunActionsRoute = GameplayIds.Route("logic.route.run-actions");

        /// <summary>logic.evaluate (target = the rule): actor key, subject key, trigger kind, event value A, event value B.</summary>
        public static readonly SchemaRef EvaluateCommand = GameplayIds.Schema("logic.command.evaluate", 1U);

        /// <summary>logic.runActions (target = the hub): action set key, actor key, subject key.</summary>
        public static readonly SchemaRef RunActionsCommand = GameplayIds.Schema("logic.command.run-actions", 1U);

        /// <summary>RuleFired: A=rule key, B=fire count, C=actor key, D=subject key, E=trigger kind, F=event value A.</summary>
        public static readonly SchemaRef RuleFiredEvent = GameplayIds.Schema("logic.event.rule-fired", 1U);

        /// <summary>RuleSkipped: A=rule key, B=reason (1 condition, 2 cooldown, 3 once, 4 max fires), C=failed condition index, D=actor key.</summary>
        public static readonly SchemaRef RuleSkippedEvent = GameplayIds.Schema("logic.event.rule-skipped", 1U);

        /// <summary>ActionsRun: A=action set key, B=actor key, C=subject key, D=invocation ordinal.</summary>
        public static readonly SchemaRef ActionsRunEvent = GameplayIds.Schema("logic.event.actions-run", 1U);

        public const int SkipCondition = 1;
        public const int SkipCooldown = 2;
        public const int SkipOnce = 3;
        public const int SkipMaxFires = 4;
    }

    /// <summary>
    /// The event payload of every narrative event: the target it is about (16 bytes) and six int32 values (40 bytes).
    /// Each schema documents what A..F mean; unused values are zero.
    /// </summary>
    public readonly struct NarrativeEvent
    {
        public const int Length = 40;

        public NarrativeEvent(TargetId target, int a, int b, int c, int d, int e, int f)
        {
            Target = target;
            A = a;
            B = b;
            C = c;
            D = d;
            E = e;
            F = f;
        }

        public TargetId Target { get; }

        public int A { get; }

        public int B { get; }

        public int C { get; }

        public int D { get; }

        public int E { get; }

        public int F { get; }

        public static FrozenPayload Encode(TargetId target, int a, int b, int c, int d, int e, int f) =>
            new GameplayPayloadWriter().Id(target.Value).Int32(a).Int32(b).Int32(c).Int32(d).Int32(e).Int32(f).Freeze();

        public static bool TryDecode(FrozenPayload? payload, out NarrativeEvent decoded)
        {
            decoded = default(NarrativeEvent);
            if (payload == null || payload.Length != Length)
            {
                return false;
            }

            var reader = new GameplayPayloadReader(payload.Bytes);
            decoded = new NarrativeEvent(
                new TargetId(reader.Id()), reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32());
            return true;
        }

        public override string ToString() =>
            "event(" + A.ToString(CultureInfo.InvariantCulture) + "," + B.ToString(CultureInfo.InvariantCulture) + ","
            + C.ToString(CultureInfo.InvariantCulture) + "," + D.ToString(CultureInfo.InvariantCulture) + ","
            + E.ToString(CultureInfo.InvariantCulture) + "," + F.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Encoders of every narrative command payload (flat little-endian int32 records).</summary>
    public static class NarrativeCommands
    {
        public static FrozenPayload Ints(params int[] values)
        {
            var writer = new GameplayPayloadWriter();
            for (int i = 0; i < values.Length; i++)
            {
                writer.Int32(values[i]);
            }

            return writer.Freeze();
        }

        /// <summary>Decodes exactly <paramref name="count"/> int32 values; false for any other length.</summary>
        public static bool TryReadInts(System.Collections.Generic.IReadOnlyList<byte>? bytes, int count, out int[] values)
        {
            values = new int[count];
            if (bytes == null || bytes.Count != count * 4)
            {
                return false;
            }

            var reader = new GameplayPayloadReader(bytes);
            for (int i = 0; i < count; i++)
            {
                values[i] = reader.Int32();
            }

            return true;
        }

        public static FrozenPayload DialogueStart(int graphKey, int speakerKey, int listenerKey) => Ints(graphKey, speakerKey, listenerKey);

        public static FrozenPayload DialogueChoose(int option) => Ints(option);

        public static FrozenPayload DialogueAdvance() => Ints(0);

        public static FrozenPayload DialogueInterrupt() => Ints(0);

        public static FrozenPayload SetFact(int factKey, int value, int requestId) => Ints(factKey, value, requestId);

        public static FrozenPayload QuestStart(int requestId) => Ints(requestId);

        public static FrozenPayload QuestAdvance(int stage, int requestId) => Ints(stage, requestId);

        public static FrozenPayload QuestSetObjective(int objective, int count) => Ints(objective, count);

        public static FrozenPayload QuestFail(int requestId) => Ints(requestId);

        public static FrozenPayload QuestComplete(int requestId) => Ints(requestId);

        public static FrozenPayload Grant(int itemKey, int count, int requestId) => Ints(itemKey, count, requestId);

        public static FrozenPayload Consume(int itemKey, int count, int requestId) => Ints(itemKey, count, requestId);

        public static FrozenPayload Drop(int itemKey, int count) => Ints(itemKey, count);

        public static FrozenPayload Transfer(int destinationInventoryKey, int itemKey, int count) => Ints(destinationInventoryKey, itemKey, count);

        public static FrozenPayload Buy(int vendorKey, int itemKey, int count, int requestId) => Ints(vendorKey, itemKey, count, requestId);

        public static FrozenPayload Sell(int vendorKey, int itemKey, int count, int requestId) => Ints(vendorKey, itemKey, count, requestId);

        public static FrozenPayload Pickup(int worldItemKey, int requestId) => Ints(worldItemKey, requestId);

        public static FrozenPayload Evaluate(int actorKey, int subjectKey, int triggerKind, int eventA, int eventB) =>
            Ints(actorKey, subjectKey, triggerKind, eventA, eventB);

        public static FrozenPayload RunActions(int actionSetKey, int actorKey, int subjectKey) => Ints(actionSetKey, actorKey, subjectKey);
    }
}

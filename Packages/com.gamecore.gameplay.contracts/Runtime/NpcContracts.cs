// GameCore.Gameplay.Contracts - NPC identities and the conversation seam (P1.3, row 4).
//
//   npc plugin (owner gameplay.npc.owner), on every NPC entity target:
//     npc.state          0 idle, 1 patrol, 2 approach, 3 converse, 4 custom
//     npc.behaviour      the standing behaviour the state returns to (0 idle, 1 patrol, 4 custom)
//     npc.patrolIndex    index of the patrol point the NPC walks to (or waits at)
//     npc.mood           -100..100
//     npc.schedulePhase  index of the current schedule phase (-1 before the first evaluation or without a schedule)
//     npc.targetX/Z      the point the NPC walks to (mm)
//     npc.posX/Z         the NPC's live logical position (mm); world.pos* keeps its authored home pose
//     npc.yaw            heading (mrad)
//     npc.timerMs        wait or conversation hold left (ms)
//
// Talking to an NPC: player.interact on a focused NPC commits InteractRequested; the npc package's dispatcher calls
// IConversationStarter.TryStart(npcAuthoringId, graphRef) and holds the NPC in converse for the returned time
// (npc.converse; hold 0 ends a conversation). The dialogue package (P1.4) implements IConversationStarter; the graph
// reference is the dialogue graph's authoring id string, so there is no type dependency on P1.4.
#nullable enable
using GameCore.Contracts;

namespace GameCore.Gameplay.Contracts
{
    /// <summary>Owner, slots, routes and schemas of the npc plugin.</summary>
    public static class NpcSlots
    {
        public static readonly OwnerId Owner = GameplayIds.Owner("npc.owner");

        public static readonly SlotId State = SlotNames.Of("npc", "state");

        public static readonly SlotId Behaviour = SlotNames.Of("npc", "behaviour");

        public static readonly SlotId PatrolIndex = SlotNames.Of("npc", "patrolIndex");

        public static readonly SlotId Mood = SlotNames.Of("npc", "mood");

        public static readonly SlotId SchedulePhase = SlotNames.Of("npc", "schedulePhase");

        public static readonly SlotId TargetX = SlotNames.Of("npc", "targetX");

        public static readonly SlotId TargetZ = SlotNames.Of("npc", "targetZ");

        public static readonly SlotId PosX = SlotNames.Of("npc", "posX");

        public static readonly SlotId PosZ = SlotNames.Of("npc", "posZ");

        public static readonly SlotId Yaw = SlotNames.Of("npc", "yaw");

        public static readonly SlotId TimerMs = SlotNames.Of("npc", "timerMs");

        /// <summary>npc.setBehaviour: behaviour code (0 idle, 1 patrol, 4 custom).</summary>
        public static readonly RouteId SetBehaviourRoute = GameplayIds.Route("npc.route.set-behaviour");

        /// <summary>npc.goTo: x, z (mm).</summary>
        public static readonly RouteId GoToRoute = GameplayIds.Route("npc.route.go-to");

        /// <summary>npc.face: x, z (mm) of the point to face.</summary>
        public static readonly RouteId FaceRoute = GameplayIds.Route("npc.route.face");

        /// <summary>npc.setMood: mood -100..100.</summary>
        public static readonly RouteId SetMoodRoute = GameplayIds.Route("npc.route.set-mood");

        /// <summary>npc.converse: hold milliseconds (0 ends a conversation, int.MaxValue holds until ended).</summary>
        public static readonly RouteId ConverseRoute = GameplayIds.Route("npc.route.converse");

        public static readonly SchemaRef SetBehaviourCommand = GameplayIds.Schema("npc.command.set-behaviour", 1U);

        public static readonly SchemaRef GoToCommand = GameplayIds.Schema("npc.command.go-to", 1U);

        public static readonly SchemaRef FaceCommand = GameplayIds.Schema("npc.command.face", 1U);

        public static readonly SchemaRef SetMoodCommand = GameplayIds.Schema("npc.command.set-mood", 1U);

        public static readonly SchemaRef ConverseCommand = GameplayIds.Schema("npc.command.converse", 1U);

        /// <summary>NpcArrived: A=patrol index reached (-1 for a goTo or phase target), B=x, C=z (mm).</summary>
        public static readonly SchemaRef ArrivedEvent = GameplayIds.Schema("npc.event.arrived", 1U);

        /// <summary>NpcStateChanged: A=new state, B=previous state, C=standing behaviour, D=schedule phase.</summary>
        public static readonly SchemaRef StateChangedEvent = GameplayIds.Schema("npc.event.state-changed", 1U);

        /// <summary>NpcUpdated (a committed command that left npc.state unchanged): A=what (1 behaviour, 2 mood, 3 face, 4 goTo, 5 converse), B=value.</summary>
        public static readonly SchemaRef UpdatedEvent = GameplayIds.Schema("npc.event.updated", 1U);

        public const int Idle = 0;
        public const int Patrol = 1;
        public const int Approach = 2;
        public const int Converse = 3;
        public const int Custom = 4;

        /// <summary>A hold that lasts until npc.converse 0 ends it.</summary>
        public const int HoldUntilEnded = int.MaxValue;

        /// <summary>The state name ("idle", "patrol", ...).</summary>
        public static string StateName(int state)
        {
            switch (state)
            {
                case Idle: return "idle";
                case Patrol: return "patrol";
                case Approach: return "approach";
                case Converse: return "converse";
                case Custom: return "custom";
                default: return "state" + state;
            }
        }
    }

    /// <summary>The answer of a conversation start.</summary>
    public readonly struct ConversationStart
    {
        public ConversationStart(bool started, int holdMilliseconds, string detail)
        {
            Started = started;
            HoldMilliseconds = holdMilliseconds;
            Detail = detail ?? string.Empty;
        }

        /// <summary>A dialogue system took the conversation.</summary>
        public bool Started { get; }

        /// <summary>
        /// How long the NPC holds converse: a positive number of milliseconds, <see cref="NpcSlots.HoldUntilEnded"/>
        /// (the dialogue system ends it with npc.converse 0), or 0 to use the NPC definition's default hold.
        /// </summary>
        public int HoldMilliseconds { get; }

        /// <summary>A diagnostic line (always set when not started).</summary>
        public string Detail { get; }
    }

    /// <summary>Starts a conversation with an NPC. Implemented by the dialogue package (P1.4).</summary>
    public interface IConversationStarter
    {
        /// <param name="npcAuthoringId">Authoring id of the NPC's placed entity.</param>
        /// <param name="graphRef">The NPC definition's dialogue graph reference (an authoring id string; may be empty).</param>
        ConversationStart TryStart(string npcAuthoringId, string graphRef);
    }

    /// <summary>
    /// The default conversation starter: no dialogue system is installed. It starts nothing and answers with a
    /// GP-NPC diagnostic; the NPC still holds converse for its definition's default hold, so talking is visible.
    /// </summary>
    public sealed class NullConversationStarter : IConversationStarter
    {
        public int Requests { get; private set; }

        public string LastDetail { get; private set; } = string.Empty;

        public ConversationStart TryStart(string npcAuthoringId, string graphRef)
        {
            Requests++;
            LastDetail = PlayerNpcInteractionCodes.NpcNoConversationSystem + ": no conversation system is installed; NPC "
                + npcAuthoringId + " (graph '" + graphRef + "') holds converse briefly";
            return new ConversationStart(false, 0, LastDetail);
        }
    }
}

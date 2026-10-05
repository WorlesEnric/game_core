// GameCore.Gameplay.Npc - NPC commands, the NPCs as focus candidates, the talk dispatcher and the session (P1.3).
//
// Talk: the player plugin commits InteractRequested (A = focused key, B = the player's key) when player.interact is
// submitted with a focus. The NpcConversationDispatcher reads committed events with its own cursor before the pump;
// for an NPC key it asks IConversationStarter.TryStart(npc authoring id, dialogue graph ref). A started conversation
// holds the NPC in converse for the hold the starter returns (or until npc.converse 0); the null starter logs its
// GP-NPC-020 diagnostic and the NPC holds converse briefly (its definition's converse seconds). Either way the NPC faces
// the player (npc.face toward the committed player pose).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.World;
using UnityEngine;

namespace GameCore.Gameplay.Npc
{
    /// <summary>Submits npc commands with the world's gameplay issuer.</summary>
    public sealed class NpcCommands
    {
        private readonly GameplayWorld world;

        public NpcCommands(GameplayWorld world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public CommandAdmissionReceipt SetBehaviour(TargetId npc, int behaviour) =>
            world.Commands.Submit(NpcSlots.SetBehaviourRoute, npc, NpcSlots.SetBehaviourCommand, NpcCommandPayload.Encode(behaviour, 0));

        public CommandAdmissionReceipt GoTo(TargetId npc, int x, int z) =>
            world.Commands.Submit(NpcSlots.GoToRoute, npc, NpcSlots.GoToCommand, NpcCommandPayload.Encode(x, z));

        public CommandAdmissionReceipt Face(TargetId npc, int x, int z) =>
            world.Commands.Submit(NpcSlots.FaceRoute, npc, NpcSlots.FaceCommand, NpcCommandPayload.Encode(x, z));

        /// <summary>npc.face{target}: faces the committed position of another target (player, NPC or placed entity).</summary>
        public bool FaceTarget(TargetId npc, TargetId target) =>
            TryPosition(world.Slots, target, out int x, out int z) && Face(npc, x, z).Admitted;

        public CommandAdmissionReceipt SetMood(TargetId npc, int mood) =>
            world.Commands.Submit(NpcSlots.SetMoodRoute, npc, NpcSlots.SetMoodCommand, NpcCommandPayload.Encode(mood, 0));

        /// <summary>Holds the NPC in converse for <paramref name="holdMilliseconds"/>; 0 ends the conversation.</summary>
        public CommandAdmissionReceipt Converse(TargetId npc, int holdMilliseconds) =>
            world.Commands.Submit(NpcSlots.ConverseRoute, npc, NpcSlots.ConverseCommand, NpcCommandPayload.Encode(holdMilliseconds, 0));

        /// <summary>The committed planar position of a target: player.pos, then npc.pos, then world.pos.</summary>
        public static bool TryPosition(ICommittedSlotReader slots, TargetId target, out int x, out int z)
        {
            if (slots.TryRead(target, PlayerSlots.Owner, PlayerSlots.PosX, out x) && slots.TryRead(target, PlayerSlots.Owner, PlayerSlots.PosZ, out z))
            {
                return true;
            }

            if (slots.TryRead(target, NpcSlots.Owner, NpcSlots.PosX, out x) && slots.TryRead(target, NpcSlots.Owner, NpcSlots.PosZ, out z))
            {
                return true;
            }

            if (slots.TryRead(target, GameplaySlots.WorldOwner, GameplaySlots.PosX, out x) && slots.TryRead(target, GameplaySlots.WorldOwner, GameplaySlots.PosZ, out z))
            {
                return true;
            }

            z = 0;
            return false;
        }
    }

    /// <summary>One NPC as a focus candidate (position = committed npc.pos, region = world.region).</summary>
    public sealed class NpcInteractable : IInteractable
    {
        private readonly NpcRecord record;

        public NpcInteractable(NpcRecord record)
        {
            this.record = record ?? throw new ArgumentNullException(nameof(record));
        }

        public string AuthoringId => record.AuthoringId;

        public int Key => record.Key;

        public TargetId Target => record.Target;

        public int Priority => record.Definition != null ? record.Definition.FocusPriority : 1;

        public bool TryGetFocusPoint(ICommittedSlotReader slots, out int x, out int y, out int z, out int regionKey)
        {
            y = slots.TryRead(record.Target, GameplaySlots.WorldOwner, GameplaySlots.PosY, out int py) ? py : 0;
            regionKey = slots.TryRead(record.Target, GameplaySlots.WorldOwner, GameplaySlots.Region, out int region) ? region : 0;
            bool alive = !slots.TryRead(record.Target, GameplaySlots.EntityOwner, GameplaySlots.Alive, out int aliveValue) || aliveValue != 0;
            z = 0;
            return slots.TryRead(record.Target, NpcSlots.Owner, NpcSlots.PosX, out x)
                && slots.TryRead(record.Target, NpcSlots.Owner, NpcSlots.PosZ, out z)
                && alive;
        }

        public PromptRequest PromptFor(ICommittedSlotReader slots)
        {
            int state = slots.TryRead(record.Target, NpcSlots.Owner, NpcSlots.State, out int value) ? value : 0;
            return new PromptRequest(record.AuthoringId, "Talk to " + record.DisplayName, "npc.talk", state);
        }
    }

    /// <summary>Every NPC of the extension as focus candidates.</summary>
    public sealed class NpcInteractableSource : IInteractableSource
    {
        private readonly List<IInteractable> items = new List<IInteractable>();

        public NpcInteractableSource(NpcWorldExtension extension)
        {
            for (int i = 0; i < extension.Records.Count; i++)
            {
                items.Add(new NpcInteractable(extension.Records[i]));
            }
        }

        public void Collect(List<IInteractable> into) => into.AddRange(items);
    }

    /// <summary>Turns InteractRequested on a focused NPC into a conversation start and a converse hold.</summary>
    public sealed class NpcConversationDispatcher : IGameplayInputSource
    {
        private readonly List<CommittedEvent> events = new List<CommittedEvent>();
        private readonly GameplayEventCursor cursor = new GameplayEventCursor();
        private readonly NpcWorldExtension extension;
        private readonly NpcCommands commands;

        public NpcConversationDispatcher(NpcWorldExtension extension, NpcCommands commands)
        {
            this.extension = extension ?? throw new ArgumentNullException(nameof(extension));
            this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
        }

        public IConversationStarter Conversations { get; set; } = new NullConversationStarter();

        /// <summary>Receives the starter's detail when no conversation started (defaults to Debug.Log).</summary>
        public Action<string> Diagnostics { get; set; } = detail => Debug.Log("[gameplay.npc] " + detail);

        public int Talks { get; private set; }

        public int Started { get; private set; }

        public string LastDetail { get; private set; } = string.Empty;

        public int Collect(GameplayWorld world)
        {
            if (extension.Module == null)
            {
                return 0;
            }

            events.Clear();
            cursor.ReadInto(world, events);
            int submitted = 0;
            for (int i = 0; i < events.Count; i++)
            {
                CommittedEvent committed = events[i];
                if (!committed.Schema.Equals(PlayerSlots.InteractRequestedEvent)
                    || !GameplayActorEvent.TryDecode(committed.Payload, out GameplayActorEvent request)
                    || !extension.Module.TryGetByKey(request.A, out NpcRecord? npc) || npc == null)
                {
                    continue;
                }

                Talks++;
                ConversationStart start = Conversations.TryStart(npc.AuthoringId, npc.DialogueGraph);
                LastDetail = start.Detail;
                int hold;
                if (start.Started)
                {
                    Started++;
                    hold = start.HoldMilliseconds > 0 ? start.HoldMilliseconds : NpcSlots.HoldUntilEnded;
                }
                else
                {
                    Diagnostics(start.Detail);
                    hold = npc.Profile.ConverseMilliseconds;
                }

                if (commands.Converse(npc.Target, hold).Admitted)
                {
                    submitted++;
                }

                if (commands.FaceTarget(npc.Target, request.Target))
                {
                    submitted++;
                }
            }

            return submitted;
        }
    }

    /// <summary>The installed NPC presentation and talk loop of one world.</summary>
    public sealed class NpcSession
    {
        private NpcSession(NpcCommands commands, NpcInteractableSource candidates, NpcConversationDispatcher conversations)
        {
            Commands = commands;
            Candidates = candidates;
            Conversations = conversations;
        }

        public NpcCommands Commands { get; }

        public NpcInteractableSource Candidates { get; }

        public NpcConversationDispatcher Conversations { get; }

        public NavMeshAgentBinder? Navigation { get; private set; }

        /// <summary>Adds the talk dispatcher (input) and, when the world has views, the NPC binders.</summary>
        public static NpcSession Install(GameplayWorld world, NpcWorldExtension extension, IConversationStarter? conversations = null)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (extension == null || extension.Module == null)
            {
                throw new InvalidOperationException("the npc extension is not attached to this world");
            }

            var commands = new NpcCommands(world);
            var dispatcher = new NpcConversationDispatcher(extension, commands);
            if (conversations != null)
            {
                dispatcher.Conversations = conversations;
            }

            var session = new NpcSession(commands, new NpcInteractableSource(extension), dispatcher);
            world.AddInput(dispatcher);
            PrefabViewBinder? views = world.Views;
            if (views != null)
            {
                session.Navigation = new NavMeshAgentBinder(views, extension);
                world.AddBinder(session.Navigation);
                world.AddBinder(new NpcAnimatorBinder(views, extension));
                world.AddBinder(new NpcBubbleBinder(views, extension));
            }

            return session;
        }
    }
}

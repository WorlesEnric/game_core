// GameCore.Gameplay.Contracts - player identities and the seams the player package talks through (P1.3, row 3).
//
// Everything here is engine-free and additive. Slot, route and event identities are pure derivations of stable names
// (like GameplaySlots), so the UI (P1.5), dialogue/logic (P1.4) and Studio can read the player's committed slots and
// events, or submit its commands, without a type dependency on com.gamecore.gameplay.player. The interfaces are the
// only way the player package reaches UI and audio; each has a null object that is the default.
//
//   player plugin (owner gameplay.player.owner), on the player's entity target:
//     player.posX/posY/posZ   authoritative pose in millimetres (the player's live pose; world.pos* keeps the last
//                             world placement: spawn or portal arrival)
//     player.yaw              heading in milliradians [0, 6283)
//     player.stamina          0..max (PlayerDefinition.staminaMax)
//     player.focus            stable key (AuthoringIds.StableKey) of the focused interactable/NPC, or -1
//     player.regionKey        region key the pose belongs to (bookkeeping: a change of world.region means a travel)
//     player.regenDelayMs     stamina regeneration delay left (bookkeeping)
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Gameplay.Contracts
{
    /// <summary>Owner, slots, routes and schemas of the player plugin.</summary>
    public static class PlayerSlots
    {
        public static readonly OwnerId Owner = GameplayIds.Owner("player.owner");

        public static readonly SlotId PosX = SlotNames.Of("player", "posX");

        public static readonly SlotId PosY = SlotNames.Of("player", "posY");

        public static readonly SlotId PosZ = SlotNames.Of("player", "posZ");

        public static readonly SlotId Yaw = SlotNames.Of("player", "yaw");

        public static readonly SlotId Stamina = SlotNames.Of("player", "stamina");

        public static readonly SlotId Focus = SlotNames.Of("player", "focus");

        public static readonly SlotId RegionKey = SlotNames.Of("player", "regionKey");

        public static readonly SlotId RegenDelayMs = SlotNames.Of("player", "regenDelayMs");

        /// <summary>player.move: dx, dy, dz (mm), yaw (mrad), flags (1 run, 2 jump).</summary>
        public static readonly RouteId MoveRoute = GameplayIds.Route("player.route.move");

        /// <summary>player.interact: no argument (the focused key is read from player.focus).</summary>
        public static readonly RouteId InteractRoute = GameplayIds.Route("player.route.interact");

        /// <summary>player.setFocus: the stable key to focus, or -1.</summary>
        public static readonly RouteId SetFocusRoute = GameplayIds.Route("player.route.set-focus");

        public static readonly SchemaRef MoveCommand = GameplayIds.Schema("player.command.move", 1U);

        public static readonly SchemaRef InteractCommand = GameplayIds.Schema("player.command.interact", 1U);

        public static readonly SchemaRef SetFocusCommand = GameplayIds.Schema("player.command.set-focus", 1U);

        /// <summary>PlayerMoved: A=x, B=y, C=z (mm), D=yaw (mrad), committed by every accepted move.</summary>
        public static readonly SchemaRef MovedEvent = GameplayIds.Schema("player.event.moved", 1U);

        /// <summary>FocusChanged: A=previous key, B=new key (-1 = none).</summary>
        public static readonly SchemaRef FocusChangedEvent = GameplayIds.Schema("player.event.focus-changed", 1U);

        /// <summary>InteractRequested: A=focused key, B=the player's own stable key (the actor of interact.use).</summary>
        public static readonly SchemaRef InteractRequestedEvent = GameplayIds.Schema("player.event.interact-requested", 1U);

        /// <summary>Flag bits of player.move.</summary>
        public const int RunFlag = 1;

        public const int JumpFlag = 2;

        public const int NoFocus = -1;
    }

    /// <summary>
    /// A gameplay event payload: the target it is about (16 bytes) and four int32 values (the same layout as the world
    /// plugin's events). Player, NPC and interaction events all use it; each schema documents what A..D mean.
    /// </summary>
    public readonly struct GameplayActorEvent
    {
        public const int Length = 32;

        public GameplayActorEvent(TargetId target, int a, int b, int c, int d)
        {
            Target = target;
            A = a;
            B = b;
            C = c;
            D = d;
        }

        public TargetId Target { get; }

        public int A { get; }

        public int B { get; }

        public int C { get; }

        public int D { get; }

        public static FrozenPayload Encode(TargetId target, int a, int b, int c, int d) =>
            new GameplayPayloadWriter().Id(target.Value).Int32(a).Int32(b).Int32(c).Int32(d).Freeze();

        public static bool TryDecode(FrozenPayload? payload, out GameplayActorEvent decoded)
        {
            decoded = default(GameplayActorEvent);
            if (payload == null || payload.Length != Length)
            {
                return false;
            }

            var reader = new GameplayPayloadReader(payload.Bytes);
            decoded = new GameplayActorEvent(new TargetId(reader.Id()), reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32());
            return true;
        }
    }

    /// <summary>Typed UI intents raised by player input (P1.5 maps them to ui.* commands).</summary>
    public enum UiIntent
    {
        Pause = 0,
        Journal = 1,
        Inventory = 2,
    }

    /// <summary>Stable names of the UI intents.</summary>
    public static class UiIntents
    {
        public const string Pause = "ui.pause";
        public const string Journal = "ui.journal";
        public const string Inventory = "ui.inventory";

        public static string Name(UiIntent intent)
        {
            switch (intent)
            {
                case UiIntent.Pause: return Pause;
                case UiIntent.Journal: return Journal;
                case UiIntent.Inventory: return Inventory;
                default: return "ui.unknown";
            }
        }
    }

    /// <summary>Receives UI intents from player input. Implemented by the UI package (P1.5).</summary>
    public interface IUiIntentSink
    {
        void Raise(UiIntent intent);
    }

    /// <summary>The default UI intent sink: records the intents and does nothing else.</summary>
    public sealed class NullUiIntentSink : IUiIntentSink
    {
        private readonly List<UiIntent> raised = new List<UiIntent>();

        public IReadOnlyList<UiIntent> Raised => raised;

        public void Raise(UiIntent intent)
        {
            if (raised.Count < 256)
            {
                raised.Add(intent);
            }
        }
    }

    /// <summary>What an interaction prompt shows.</summary>
    public sealed class PromptRequest
    {
        public PromptRequest(string targetAuthoringId, string text, string kind, int state)
        {
            TargetAuthoringId = targetAuthoringId ?? string.Empty;
            Text = text ?? string.Empty;
            Kind = kind ?? string.Empty;
            State = state;
        }

        /// <summary>Authoring id of the focused entity.</summary>
        public string TargetAuthoringId { get; }

        /// <summary>Prompt text, e.g. "Talk to Maren" or "Open".</summary>
        public string Text { get; }

        /// <summary>"npc", "door", "gate", "examinable", "point" or "switch".</summary>
        public string Kind { get; }

        /// <summary>The interactable's committed state (interact.state), or the NPC's npc.state.</summary>
        public int State { get; }

        public override string ToString() => Kind + ":" + Text;
    }

    /// <summary>Shows and hides the interaction prompt. Implemented by the UI package (P1.5).</summary>
    public interface IPromptPresenter
    {
        void Show(PromptRequest prompt);

        void Hide();
    }

    /// <summary>The default prompt presenter: remembers the current prompt and draws nothing.</summary>
    public sealed class NullPromptPresenter : IPromptPresenter
    {
        public PromptRequest? Current { get; private set; }

        public int Shown { get; private set; }

        public void Show(PromptRequest prompt)
        {
            Current = prompt;
            Shown++;
        }

        public void Hide() => Current = null;
    }

    /// <summary>One footstep of a moving actor.</summary>
    public readonly struct FootstepEvent
    {
        public FootstepEvent(string actorAuthoringId, int foot, int x, int y, int z, bool running)
        {
            ActorAuthoringId = actorAuthoringId;
            Foot = foot;
            X = x;
            Y = y;
            Z = z;
            Running = running;
        }

        public string ActorAuthoringId { get; }

        /// <summary>0 left, 1 right.</summary>
        public int Foot { get; }

        public int X { get; }

        public int Y { get; }

        public int Z { get; }

        public bool Running { get; }
    }

    /// <summary>Receives footsteps (audio, P1.5). Presentation only: footsteps never change state.</summary>
    public interface IFootstepSink
    {
        void OnFootstep(FootstepEvent footstep);
    }

    /// <summary>The default footstep sink: counts footsteps.</summary>
    public sealed class NullFootstepSink : IFootstepSink
    {
        public int Count { get; private set; }

        public void OnFootstep(FootstepEvent footstep) => Count++;
    }
}

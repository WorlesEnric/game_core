// GameCore.Gameplay.Contracts - interaction identities, focus candidates and the condition/action/feedback seams
// (P1.3, row 5).
//
//   interaction plugin (owner gameplay.interaction.owner), on every interactable entity target:
//     interact.state       0 idle, 1 closed, 2 open, 3 locked, 4 used, 5 broken, 6 off, 7 on
//     interact.uses        successful uses
//     interact.cooldownMs  cooldown left (ms)
//   and on every trigger-volume entity target:
//     interact.occupants   actors inside the volume
//
// Conditions and actions are authoring-id strings on the InteractableDefinition, evaluated through IConditionEvaluator
// and run through IActionRunner (the logic package, P1.4, implements both). Conditions are evaluated inside the
// interaction command stage from committed slots, so they must be deterministic and read-only; actions run on the
// presentation side after InteractionSucceeded was committed and act only by submitting commands.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Gameplay.Contracts
{
    /// <summary>Owner, slots, routes and schemas of the interaction plugin.</summary>
    public static class InteractionSlots
    {
        public static readonly OwnerId Owner = GameplayIds.Owner("interaction.owner");

        public static readonly SlotId State = SlotNames.Of("interact", "state");

        public static readonly SlotId Uses = SlotNames.Of("interact", "uses");

        public static readonly SlotId CooldownMs = SlotNames.Of("interact", "cooldownMs");

        public static readonly SlotId Occupants = SlotNames.Of("interact", "occupants");

        /// <summary>interact.use: the actor's stable key (the target is the interactable).</summary>
        public static readonly RouteId UseRoute = GameplayIds.Route("interaction.route.use");

        /// <summary>interact.setState: the next state (logic actions and tools).</summary>
        public static readonly RouteId SetStateRoute = GameplayIds.Route("interaction.route.set-state");

        /// <summary>interact.trigger: the actor's stable key and 1 (entered) or 0 (left); the target is the volume.</summary>
        public static readonly RouteId TriggerRoute = GameplayIds.Route("interaction.route.trigger");

        public static readonly SchemaRef UseCommand = GameplayIds.Schema("interaction.command.use", 1U);

        public static readonly SchemaRef SetStateCommand = GameplayIds.Schema("interaction.command.set-state", 1U);

        public static readonly SchemaRef TriggerCommand = GameplayIds.Schema("interaction.command.trigger", 1U);

        /// <summary>InteractionSucceeded: A=actor key, B=new state, C=uses, D=previous state.</summary>
        public static readonly SchemaRef SucceededEvent = GameplayIds.Schema("interaction.event.succeeded", 1U);

        /// <summary>InteractionRefused: A=actor key, B=refusal code (InteractionRefusal value), C=state.</summary>
        public static readonly SchemaRef RefusedEvent = GameplayIds.Schema("interaction.event.refused", 1U);

        /// <summary>InteractableStateChanged: A=new state, B=previous state.</summary>
        public static readonly SchemaRef StateChangedEvent = GameplayIds.Schema("interaction.event.state-changed", 1U);

        /// <summary>TriggerEntered: A=actor key, B=occupants after.</summary>
        public static readonly SchemaRef TriggerEnteredEvent = GameplayIds.Schema("interaction.event.trigger-entered", 1U);

        /// <summary>TriggerExited: A=actor key, B=occupants after.</summary>
        public static readonly SchemaRef TriggerExitedEvent = GameplayIds.Schema("interaction.event.trigger-exited", 1U);

        /// <summary>The refusal code text of an InteractionRefused event's B value.</summary>
        public static string RefusalCode(int refusal)
        {
            switch (refusal)
            {
                case 1: return "interaction.locked";
                case 2: return "interaction.cooling-down";
                case 3: return "interaction.uses-exhausted";
                case 4: return "interaction.broken";
                case 5: return "interaction.already-used";
                case 6: return "interaction.condition-failed";
                case 7: return "interaction.out-of-range";
                case 8: return "interaction.unchanged";
                case 9: return "interaction.invalid-state";
                case 10: return "interaction.not-interactable";
                default: return "interaction.refused";
            }
        }
    }

    /// <summary>A condition verdict.</summary>
    public enum ConditionVerdict
    {
        /// <summary>The evaluator cannot decide (no logic system, unknown reference).</summary>
        Unknown = 0,
        True = 1,
        False = 2,
    }

    /// <summary>What a condition or action is evaluated about.</summary>
    public sealed class InteractionContext
    {
        public InteractionContext(string targetAuthoringId, int targetKey, int actorKey, int state, ICommittedSlotReader slots)
        {
            TargetAuthoringId = targetAuthoringId ?? string.Empty;
            TargetKey = targetKey;
            ActorKey = actorKey;
            State = state;
            Slots = slots;
        }

        /// <summary>Authoring id of the interactable's placed entity.</summary>
        public string TargetAuthoringId { get; }

        public int TargetKey { get; }

        /// <summary>Stable key of the actor (the player's key for player interactions).</summary>
        public int ActorKey { get; }

        /// <summary>The interactable's interact.state when the condition is asked.</summary>
        public int State { get; }

        /// <summary>Read-only access to the world's committed slots (the only state a condition may read).</summary>
        public ICommittedSlotReader Slots { get; }
    }

    /// <summary>Evaluates condition references (e.g. <c>narrative.fact.gate_open</c>). Implemented by P1.4 logic.</summary>
    public interface IConditionEvaluator
    {
        ConditionVerdict Evaluate(string conditionRef, InteractionContext context);
    }

    /// <summary>
    /// The default evaluator: every verdict is Unknown. A use precondition then passes ("always allowed"), while an
    /// unlock condition does not unlock (a locked door stays locked until a logic system says otherwise).
    /// </summary>
    public sealed class NullConditionEvaluator : IConditionEvaluator
    {
        public int Evaluations { get; private set; }

        public ConditionVerdict Evaluate(string conditionRef, InteractionContext context)
        {
            Evaluations++;
            return ConditionVerdict.Unknown;
        }
    }

    /// <summary>Runs action references after a committed success. Implemented by P1.4 logic.</summary>
    public interface IActionRunner
    {
        void Run(string actionRef, InteractionContext context);
    }

    /// <summary>The default action runner: does nothing.</summary>
    public sealed class NullActionRunner : IActionRunner
    {
        public int Runs { get; private set; }

        public void Run(string actionRef, InteractionContext context) => Runs++;
    }

    /// <summary>One feedback cue of an interaction (sound, VFX, animation): presentation only.</summary>
    public readonly struct FeedbackCue
    {
        public FeedbackCue(string targetAuthoringId, string cue, int state, int x, int y, int z)
        {
            TargetAuthoringId = targetAuthoringId;
            Cue = cue;
            State = state;
            X = x;
            Y = y;
            Z = z;
        }

        public string TargetAuthoringId { get; }

        /// <summary>The definition's cue for the outcome (e.g. "door.open"), or "refused:&lt;code&gt;".</summary>
        public string Cue { get; }

        public int State { get; }

        public int X { get; }

        public int Y { get; }

        public int Z { get; }
    }

    /// <summary>Receives interaction feedback cues (audio/VFX, P1.5).</summary>
    public interface IFeedbackSink
    {
        void OnFeedback(FeedbackCue cue);
    }

    /// <summary>The default feedback sink: counts cues.</summary>
    public sealed class NullFeedbackSink : IFeedbackSink
    {
        public int Count { get; private set; }

        public string LastCue { get; private set; } = string.Empty;

        public void OnFeedback(FeedbackCue cue)
        {
            Count++;
            LastCue = cue.Cue ?? string.Empty;
        }
    }

    /// <summary>
    /// Something the player can focus and interact with: an interactable or an NPC. Positions come from committed slots,
    /// so focus works headless and under replay.
    /// </summary>
    public interface IInteractable
    {
        string AuthoringId { get; }

        /// <summary>The positive stable key (AuthoringIds.StableKey) player.focus carries.</summary>
        int Key { get; }

        TargetId Target { get; }

        /// <summary>Focus priority: higher wins over nearer lower-priority candidates.</summary>
        int Priority { get; }

        /// <summary>The committed focus point (mm) and whether the candidate can be focused now.</summary>
        bool TryGetFocusPoint(ICommittedSlotReader slots, out int x, out int y, out int z, out int regionKey);

        /// <summary>The prompt for the candidate's committed state.</summary>
        PromptRequest PromptFor(ICommittedSlotReader slots);
    }

    /// <summary>A set of focus candidates (the npc and interaction runtimes each provide one).</summary>
    public interface IInteractableSource
    {
        void Collect(List<IInteractable> into);
    }
}

// GameCore.Rules.Gameplay.Interaction - pure interactable and trigger transitions (P1.3, catalog row 5).
//
// An interactable's authoritative state is three int32 slots owned by the interaction plugin: interact.state (one of
// InteractableStates), interact.uses (successful uses so far) and interact.cooldownMs (remaining cooldown). A use is
// checked in a fixed order - broken, cooling down, out of range, uses exhausted, locked, condition - and then follows
// the kind's state machine: a door or gate toggles closed/open, a switch toggles off/on, a point is used once, an
// examinable stays idle and only counts. A locked interactable opens only when its unlock condition is affirmatively
// true; an unknown verdict (no condition system installed) keeps it locked. A use precondition passes unless it is
// affirmatively false, so the null evaluator means "always allowed".
#nullable enable
namespace GameCore.Rules.Gameplay.Interaction
{
    /// <summary>The kind of an interactable.</summary>
    public enum InteractableKind
    {
        Door = 0,
        Gate = 1,
        Examinable = 2,
        Point = 3,
        Switch = 4,
    }

    /// <summary>Values of the interact.state slot.</summary>
    public static class InteractableStates
    {
        public const int Idle = 0;
        public const int Closed = 1;
        public const int Open = 2;
        public const int Locked = 3;
        public const int Used = 4;
        public const int Broken = 5;
        public const int Off = 6;
        public const int On = 7;

        public const int Count = 8;

        /// <summary>The state's lowercase name ("closed", "open", ...), or "state&lt;n&gt;" when unknown.</summary>
        public static string Name(int state)
        {
            switch (state)
            {
                case Idle: return "idle";
                case Closed: return "closed";
                case Open: return "open";
                case Locked: return "locked";
                case Used: return "used";
                case Broken: return "broken";
                case Off: return "off";
                case On: return "on";
                default: return "state" + state;
            }
        }

        /// <summary>Parses a state name; false when it is not one of the known names.</summary>
        public static bool TryParse(string name, out int state)
        {
            for (int i = 0; i < Count; i++)
            {
                if (string.Equals(Name(i), name, System.StringComparison.Ordinal))
                {
                    state = i;
                    return true;
                }
            }

            state = Idle;
            return false;
        }
    }

    /// <summary>A condition evaluation's answer, as the rules see it.</summary>
    public enum ConditionAnswer
    {
        /// <summary>No condition, or no system that can evaluate it.</summary>
        Unknown = 0,
        True = 1,
        False = 2,
    }

    /// <summary>The interactable's authoritative slots.</summary>
    public readonly struct InteractableSnapshot
    {
        public InteractableSnapshot(int state, int uses, int cooldownMilliseconds)
        {
            State = state;
            Uses = uses;
            CooldownMilliseconds = cooldownMilliseconds;
        }

        public int State { get; }

        public int Uses { get; }

        public int CooldownMilliseconds { get; }

        public override string ToString() =>
            "interactable(" + InteractableStates.Name(State) + " uses=" + Uses + " cooldown=" + CooldownMilliseconds + ")";
    }

    /// <summary>Integer profile of one interactable (converted from its definition).</summary>
    public readonly struct InteractableProfile
    {
        public InteractableProfile(InteractableKind kind, int maxUses, int cooldownMilliseconds, int rangeMillimetres)
        {
            Kind = kind;
            MaxUses = maxUses < 0 ? 0 : maxUses;
            CooldownMilliseconds = cooldownMilliseconds < 0 ? 0 : cooldownMilliseconds;
            RangeMillimetres = rangeMillimetres < 0 ? 0 : rangeMillimetres;
        }

        public InteractableKind Kind { get; }

        /// <summary>Successful uses allowed; 0 means unlimited.</summary>
        public int MaxUses { get; }

        public int CooldownMilliseconds { get; }

        /// <summary>Largest actor distance a use is accepted from; 0 disables the check.</summary>
        public int RangeMillimetres { get; }
    }

    /// <summary>Why a use or a state change was refused.</summary>
    public enum InteractionRefusal
    {
        None = 0,
        Locked = 1,
        CoolingDown = 2,
        UsesExhausted = 3,
        Broken = 4,
        AlreadyUsed = 5,
        ConditionFailed = 6,
        OutOfRange = 7,
        Unchanged = 8,
        InvalidState = 9,
        NotInteractable = 10,
    }

    /// <summary>Stable refusal codes of the interaction rules (the code an InteractionRefused event carries).</summary>
    public static class InteractionRefusals
    {
        public const string Locked = "interaction.locked";
        public const string CoolingDown = "interaction.cooling-down";
        public const string UsesExhausted = "interaction.uses-exhausted";
        public const string Broken = "interaction.broken";
        public const string AlreadyUsed = "interaction.already-used";
        public const string ConditionFailed = "interaction.condition-failed";
        public const string OutOfRange = "interaction.out-of-range";
        public const string Unchanged = "interaction.unchanged";
        public const string InvalidState = "interaction.invalid-state";
        public const string NotInteractable = "interaction.not-interactable";

        public static string Code(InteractionRefusal refusal)
        {
            switch (refusal)
            {
                case InteractionRefusal.Locked: return Locked;
                case InteractionRefusal.CoolingDown: return CoolingDown;
                case InteractionRefusal.UsesExhausted: return UsesExhausted;
                case InteractionRefusal.Broken: return Broken;
                case InteractionRefusal.AlreadyUsed: return AlreadyUsed;
                case InteractionRefusal.ConditionFailed: return ConditionFailed;
                case InteractionRefusal.OutOfRange: return OutOfRange;
                case InteractionRefusal.Unchanged: return Unchanged;
                case InteractionRefusal.InvalidState: return InvalidState;
                case InteractionRefusal.NotInteractable: return NotInteractable;
                default: return string.Empty;
            }
        }
    }

    /// <summary>The outcome of one use or state change.</summary>
    public readonly struct InteractionOutcome
    {
        public InteractionOutcome(InteractionRefusal refusal, InteractableSnapshot before, InteractableSnapshot after)
        {
            Refusal = refusal;
            Before = before;
            After = after;
        }

        public InteractionRefusal Refusal { get; }

        public InteractableSnapshot Before { get; }

        public InteractableSnapshot After { get; }

        public bool Succeeded => Refusal == InteractionRefusal.None;

        public bool StateChanged => Before.State != After.State;

        public static InteractionOutcome Refuse(InteractionRefusal refusal, InteractableSnapshot state) =>
            new InteractionOutcome(refusal, state, state);
    }

    /// <summary>Pure interactable transitions.</summary>
    public static class InteractionRules
    {
        /// <summary>The state a freshly placed interactable starts in.</summary>
        public static int InitialState(InteractableKind kind, bool locked)
        {
            if (locked && (kind == InteractableKind.Door || kind == InteractableKind.Gate))
            {
                return InteractableStates.Locked;
            }

            switch (kind)
            {
                case InteractableKind.Door:
                case InteractableKind.Gate:
                    return InteractableStates.Closed;
                case InteractableKind.Switch:
                    return InteractableStates.Off;
                default:
                    return InteractableStates.Idle;
            }
        }

        /// <summary>True when <paramref name="state"/> is a state an interactable of <paramref name="kind"/> can be in.</summary>
        public static bool IsLegal(InteractableKind kind, int state)
        {
            if (state == InteractableStates.Broken)
            {
                return true;
            }

            switch (kind)
            {
                case InteractableKind.Door:
                case InteractableKind.Gate:
                    return state == InteractableStates.Closed || state == InteractableStates.Open || state == InteractableStates.Locked;
                case InteractableKind.Switch:
                    return state == InteractableStates.Off || state == InteractableStates.On;
                case InteractableKind.Point:
                    return state == InteractableStates.Idle || state == InteractableStates.Used;
                case InteractableKind.Examinable:
                    return state == InteractableStates.Idle;
                default:
                    return false;
            }
        }

        /// <summary>
        /// One use by an actor <paramref name="distanceMillimetres"/> away, with the verdicts of the use precondition
        /// and of the unlock condition.
        /// </summary>
        public static InteractionOutcome Use(
            InteractableSnapshot state,
            InteractableProfile profile,
            int distanceMillimetres,
            ConditionAnswer precondition,
            ConditionAnswer unlock)
        {
            if (state.State == InteractableStates.Broken)
            {
                return InteractionOutcome.Refuse(InteractionRefusal.Broken, state);
            }

            if (state.CooldownMilliseconds > 0)
            {
                return InteractionOutcome.Refuse(InteractionRefusal.CoolingDown, state);
            }

            if (profile.RangeMillimetres > 0 && distanceMillimetres > profile.RangeMillimetres)
            {
                return InteractionOutcome.Refuse(InteractionRefusal.OutOfRange, state);
            }

            if (profile.MaxUses > 0 && state.Uses >= profile.MaxUses)
            {
                return InteractionOutcome.Refuse(InteractionRefusal.UsesExhausted, state);
            }

            if (!IsLegal(profile.Kind, state.State))
            {
                return InteractionOutcome.Refuse(InteractionRefusal.InvalidState, state);
            }

            int next;
            if (state.State == InteractableStates.Locked)
            {
                if (unlock != ConditionAnswer.True)
                {
                    return InteractionOutcome.Refuse(InteractionRefusal.Locked, state);
                }

                next = InteractableStates.Open;
            }
            else
            {
                if (precondition == ConditionAnswer.False)
                {
                    return InteractionOutcome.Refuse(InteractionRefusal.ConditionFailed, state);
                }

                switch (profile.Kind)
                {
                    case InteractableKind.Door:
                    case InteractableKind.Gate:
                        next = state.State == InteractableStates.Open ? InteractableStates.Closed : InteractableStates.Open;
                        break;
                    case InteractableKind.Switch:
                        next = state.State == InteractableStates.On ? InteractableStates.Off : InteractableStates.On;
                        break;
                    case InteractableKind.Point:
                        if (state.State == InteractableStates.Used)
                        {
                            return InteractionOutcome.Refuse(InteractionRefusal.AlreadyUsed, state);
                        }

                        next = InteractableStates.Used;
                        break;
                    default:
                        next = state.State;
                        break;
                }
            }

            var after = new InteractableSnapshot(next, state.Uses < int.MaxValue ? state.Uses + 1 : state.Uses, profile.CooldownMilliseconds);
            return new InteractionOutcome(InteractionRefusal.None, state, after);
        }

        /// <summary>Counts the cooldown down by <paramref name="elapsedMilliseconds"/>.</summary>
        public static InteractableSnapshot Tick(InteractableSnapshot state, int elapsedMilliseconds)
        {
            if (state.CooldownMilliseconds <= 0 || elapsedMilliseconds <= 0)
            {
                return state;
            }

            int remaining = state.CooldownMilliseconds - elapsedMilliseconds;
            return new InteractableSnapshot(state.State, state.Uses, remaining < 0 ? 0 : remaining);
        }

        /// <summary>interact.setState (logic actions, tools): any legal state of the kind; uses and cooldown are kept.</summary>
        public static InteractionOutcome SetState(InteractableSnapshot state, InteractableKind kind, int next)
        {
            if (!IsLegal(kind, next))
            {
                return InteractionOutcome.Refuse(InteractionRefusal.InvalidState, state);
            }

            if (state.State == next)
            {
                return InteractionOutcome.Refuse(InteractionRefusal.Unchanged, state);
            }

            return new InteractionOutcome(InteractionRefusal.None, state, new InteractableSnapshot(next, state.Uses, state.CooldownMilliseconds));
        }
    }

    /// <summary>The outcome of a trigger enter or exit.</summary>
    public readonly struct TriggerOutcome
    {
        public TriggerOutcome(int occupants, bool firstEntered, bool lastExited, bool accepted)
        {
            Occupants = occupants;
            FirstEntered = firstEntered;
            LastExited = lastExited;
            Accepted = accepted;
        }

        /// <summary>Actors inside the volume after the transition (the interact.occupants slot).</summary>
        public int Occupants { get; }

        /// <summary>The volume went from empty to occupied.</summary>
        public bool FirstEntered { get; }

        /// <summary>The volume went from occupied to empty.</summary>
        public bool LastExited { get; }

        public bool Accepted { get; }
    }

    /// <summary>Pure trigger-volume occupancy.</summary>
    public static class TriggerRules
    {
        /// <summary>An actor entered (<paramref name="entered"/>) or left the volume; exits never go below zero.</summary>
        public static TriggerOutcome Transit(int occupants, bool entered, int maxOccupants)
        {
            int current = occupants < 0 ? 0 : occupants;
            if (entered)
            {
                if (maxOccupants > 0 && current >= maxOccupants)
                {
                    return new TriggerOutcome(current, false, false, false);
                }

                return new TriggerOutcome(current + 1, current == 0, false, true);
            }

            if (current == 0)
            {
                return new TriggerOutcome(0, false, false, false);
            }

            return new TriggerOutcome(current - 1, false, current == 1, true);
        }
    }
}

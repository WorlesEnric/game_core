#nullable enable
namespace Hollowmere.Mechanism.Lever.Rules
{
    /// <summary>A bistable latch: every accepted toggle reverses its position; no occupancy or weight.</summary>
    public static class LeverRules
    {
        public const string InvalidState = "lever.invalid-state";
        public const string InvalidCommand = "lever.invalid-command";

        public static LeverTransition Toggle(int state, int turns)
        {
            if (state != 0 && state != 1)
            {
                return new LeverTransition(false, state, InvalidState);
            }

            // Exactly one turn per request: callers cannot bypass the bounded ingress lane with a repeat count.
            if (turns != 1)
            {
                return new LeverTransition(false, state, InvalidCommand);
            }

            return new LeverTransition(true, 1 - state, string.Empty);
        }
    }

    public readonly struct LeverTransition
    {
        public LeverTransition(bool accepted, int state, string refusal)
        {
            Accepted = accepted;
            State = state;
            Refusal = refusal;
        }

        public bool Accepted { get; }
        public int State { get; }
        public string Refusal { get; }
    }
}

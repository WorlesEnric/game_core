#nullable enable
// Hollowmere.Mechanism.PressurePlate.Rules - the Unity-free rules of a pressure plate (W-MECH-01 sample).
//
// A plate holds two authoritative int32 slots (SADR-004): plate.weight (actors standing on it) and plate.pressed (0/1).
// A press is one actor stepping on (load) or off (unload). The plate is pressed while weight >= threshold; a transition
// from released to pressed or back is what the command system turns into a PlatePressed / PlateReleased event. A
// refused press changes nothing and carries a typed refusal code.
using System;

namespace Hollowmere.Mechanism.PressurePlate.Rules
{
    /// <summary>Refusal codes of a press.</summary>
    public static class PlateRefusals
    {
        /// <summary>Stepping off a plate that carries no weight.</summary>
        public const string NotLoaded = "plate.not-loaded";

        /// <summary>Stepping on a plate already at its maximum weight.</summary>
        public const string Overloaded = "plate.overloaded";

        /// <summary>The command names a target the plate system does not know.</summary>
        public const string Unknown = "plate.unknown";

        /// <summary>The plate's definition is invalid (threshold below 1 or maximum below threshold).</summary>
        public const string InvalidDefinition = "plate.invalid-definition";
    }

    /// <summary>What a press did to the pressed flag.</summary>
    public enum PlateTransition
    {
        None = 0,
        Pressed = 1,
        Released = 2,
    }

    /// <summary>The authoritative state of one plate (its two slots).</summary>
    public readonly struct PlateState : IEquatable<PlateState>
    {
        public PlateState(int weight, int pressed)
        {
            Weight = weight;
            Pressed = pressed;
        }

        /// <summary>Actors standing on the plate.</summary>
        public int Weight { get; }

        /// <summary>1 while pressed, 0 otherwise.</summary>
        public int Pressed { get; }

        public bool IsPressed => Pressed != 0;

        public bool Equals(PlateState other) => Weight == other.Weight && Pressed == other.Pressed;

        public override bool Equals(object? obj) => obj is PlateState other && Equals(other);

        public override int GetHashCode() => (Weight * 397) ^ Pressed;

        public override string ToString() => "weight=" + Weight + " pressed=" + Pressed;
    }

    /// <summary>The definition values a plate is evaluated against.</summary>
    public readonly struct PlateSpec
    {
        public PlateSpec(int threshold, int maxWeight)
        {
            Threshold = threshold;
            MaxWeight = maxWeight;
        }

        /// <summary>Weight at which the plate is pressed (at least 1).</summary>
        public int Threshold { get; }

        /// <summary>Largest weight the plate accepts (at least the threshold).</summary>
        public int MaxWeight { get; }

        public bool IsValid => Threshold >= 1 && MaxWeight >= Threshold;
    }

    /// <summary>The outcome of one press.</summary>
    public readonly struct PlatePressResult
    {
        private PlatePressResult(bool accepted, PlateState state, PlateTransition transition, string refusal)
        {
            Accepted = accepted;
            State = state;
            Transition = transition;
            Refusal = refusal;
        }

        public bool Accepted { get; }

        /// <summary>The new state when accepted; the unchanged state when refused.</summary>
        public PlateState State { get; }

        public PlateTransition Transition { get; }

        /// <summary>A <see cref="PlateRefusals"/> code when refused; empty when accepted.</summary>
        public string Refusal { get; }

        public static PlatePressResult Accept(PlateState state, PlateTransition transition) =>
            new PlatePressResult(true, state, transition, string.Empty);

        public static PlatePressResult Refuse(PlateState state, string refusal) =>
            new PlatePressResult(false, state, PlateTransition.None, refusal);
    }

    /// <summary>The pure press transition.</summary>
    public static class PressurePlateRules
    {
        /// <summary>One actor steps on (<paramref name="load"/> true) or off the plate.</summary>
        public static PlatePressResult Press(PlateState state, bool load, PlateSpec spec)
        {
            if (!spec.IsValid)
            {
                return PlatePressResult.Refuse(state, PlateRefusals.InvalidDefinition);
            }

            int weight;
            if (load)
            {
                if (state.Weight >= spec.MaxWeight)
                {
                    return PlatePressResult.Refuse(state, PlateRefusals.Overloaded);
                }

                weight = state.Weight + 1;
            }
            else
            {
                if (state.Weight <= 0)
                {
                    return PlatePressResult.Refuse(state, PlateRefusals.NotLoaded);
                }

                weight = state.Weight - 1;
            }

            int pressed = weight >= spec.Threshold ? 1 : 0;
            PlateTransition transition = PlateTransition.None;
            if (pressed == 1 && state.Pressed == 0)
            {
                transition = PlateTransition.Pressed;
            }
            else if (pressed == 0 && state.Pressed != 0)
            {
                transition = PlateTransition.Released;
            }

            return PlatePressResult.Accept(new PlateState(weight, pressed), transition);
        }

        /// <summary>The refusal of a press on a target the system does not know.</summary>
        public static PlatePressResult Unknown() => PlatePressResult.Refuse(new PlateState(0, 0), PlateRefusals.Unknown);

        /// <summary>The state a freshly placed plate starts in.</summary>
        public static PlateState Initial() => new PlateState(0, 0);
    }
}

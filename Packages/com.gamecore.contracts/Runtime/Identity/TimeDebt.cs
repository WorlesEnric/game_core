// GameCore.Contracts - production shared contract type (GC-003). Unity-free: BCL subset only, no
// UnityEngine/Unity.* reference, no runtime reflection and no second ECS facade (01 s1, P-058).
// Normative sources: docs/game-core/00-core-protocols.md and docs/game-core/05-contracts-and-data-model.md.
// The public surface of this assembly is API-compatible with the frozen W0 reference seam
// (tests/GameCore.ReferenceSeams); additions are reviewed in artifacts/gc-003/HANDOFF.md.
#nullable enable
using System;
using System.Globalization;

namespace GameCore.Contracts
{
    /// <summary>
    /// Retained fixed-step time debt in ticks. Never negative: consumption below zero is refused rather than
    /// clamped, so a host bug cannot silently erase debt.
    /// </summary>
    public readonly struct TimeDebt : IEquatable<TimeDebt>, IComparable<TimeDebt>
    {
        public static readonly TimeDebt Zero = new TimeDebt(0UL);

        public readonly ulong Ticks;

        public TimeDebt(ulong ticks)
        {
            Ticks = ticks;
        }

        /// <summary>Accumulates elapsed time, refusing at the unsigned maximum instead of wrapping (P-005).</summary>
        public TimeDebt Add(ulong elapsedTicks, out bool accepted)
        {
            if (ulong.MaxValue - Ticks < elapsedTicks)
            {
                accepted = false;
                return this;
            }

            accepted = true;
            return new TimeDebt(Ticks + elapsedTicks);
        }

        /// <summary>Consumes admitted elapsed time; false means the debt does not cover the request.</summary>
        public TimeDebt Subtract(ulong consumedTicks, out bool accepted)
        {
            if (consumedTicks > Ticks)
            {
                accepted = false;
                return this;
            }

            accepted = true;
            return new TimeDebt(Ticks - consumedTicks);
        }

        /// <summary>Number of whole steps <paramref name="stepDurationTicks"/> the debt can fund.</summary>
        public ulong WholeSteps(ulong stepDurationTicks, out bool valid)
        {
            if (stepDurationTicks == 0UL)
            {
                valid = false;
                return 0UL;
            }

            valid = true;
            return Ticks / stepDurationTicks;
        }

        public bool Equals(TimeDebt other) => Ticks == other.Ticks;

        public override bool Equals(object? obj) => obj is TimeDebt other && Equals(other);

        public override int GetHashCode() => Ticks.GetHashCode();

        public int CompareTo(TimeDebt other) => Ticks.CompareTo(other.Ticks);

        public static bool operator ==(TimeDebt left, TimeDebt right) => left.Equals(right);

        public static bool operator !=(TimeDebt left, TimeDebt right) => !left.Equals(right);

        public static bool operator <(TimeDebt left, TimeDebt right) => left.Ticks < right.Ticks;

        public static bool operator >(TimeDebt left, TimeDebt right) => left.Ticks > right.Ticks;

        public override string ToString() => "TimeDebt(" + Ticks.ToString(CultureInfo.InvariantCulture) + ")";
    }
}

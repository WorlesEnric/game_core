// GameCore.Gameplay.Contracts - units and scales of authoritative gameplay state (SADR-004: deterministic integer
// slots). Positions are integer millimetres, angles integer milliradians, durations integer milliseconds and scales
// integer thousandths. Conversion from authoring floats rounds half away from zero, so the same float always bakes to
// the same integer on every platform.
#nullable enable
using System;

namespace GameCore.Gameplay.Contracts
{
    /// <summary>Integer units of gameplay slots and their conversions.</summary>
    public static class GameplayUnits
    {
        public const int MillimetresPerMetre = 1000;

        public const int MilliradiansPerRadian = 1000;

        public const int MillisecondsPerSecond = 1000;

        /// <summary>Scale value of 1.0 (<c>entity.scaleMilli</c>).</summary>
        public const int ScaleOne = 1000;

        /// <summary>One full turn in milliradians, rounded: 2 * pi * 1000.</summary>
        public const int MilliradiansPerTurn = 6283;

        public const string MillimetreUnit = "mm";

        public const string MilliradianUnit = "mrad";

        public const string MillisecondUnit = "ms";

        public const string MilliUnit = "milli";

        public static int ToMillimetres(double metres) => Round(metres * MillimetresPerMetre);

        public static double ToMetres(int millimetres) => millimetres / (double)MillimetresPerMetre;

        public static int ToMilliradians(double radians) => Round(radians * MilliradiansPerRadian);

        public static double ToRadians(int milliradians) => milliradians / (double)MilliradiansPerRadian;

        /// <summary>Degrees (an authoring yaw) to milliradians.</summary>
        public static int DegreesToMilliradians(double degrees) => ToMilliradians(degrees * Math.PI / 180.0);

        public static double MilliradiansToDegrees(int milliradians) => ToRadians(milliradians) * 180.0 / Math.PI;

        public static int ToMilliseconds(double seconds) => Round(seconds * MillisecondsPerSecond);

        public static int ToMilli(double value) => Round(value * ScaleOne);

        public static double FromMilli(int value) => value / (double)ScaleOne;

        /// <summary>Wraps a yaw into [0, 2*pi*1000) so equal headings bake to equal integers.</summary>
        public static int NormalizeYaw(int milliradians)
        {
            int wrapped = milliradians % MilliradiansPerTurn;
            return wrapped < 0 ? wrapped + MilliradiansPerTurn : wrapped;
        }

        /// <summary>Round half away from zero, clamped to the int32 range.</summary>
        public static int Round(double value)
        {
            if (double.IsNaN(value))
            {
                return 0;
            }

            double rounded = Math.Round(value, MidpointRounding.AwayFromZero);
            if (rounded >= int.MaxValue)
            {
                return int.MaxValue;
            }

            if (rounded <= int.MinValue)
            {
                return int.MinValue;
            }

            return (int)rounded;
        }
    }
}

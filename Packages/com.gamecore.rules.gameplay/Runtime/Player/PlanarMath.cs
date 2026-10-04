// GameCore.Rules.Gameplay.Player - deterministic integer plane geometry shared by the player, NPC and interaction
// rules (P1.3, SADR-004).
//
// Gameplay positions are integer millimetres and angles integer milliradians. Everything here is exact integer
// arithmetic (64-bit intermediates, truncation toward zero), so the same inputs give the same outputs on every platform
// and under replay. Yaw follows the engine's convention: 0 faces +Z, a quarter turn (1571 mrad) faces +X, and values
// are kept in [0, 6283).
#nullable enable
using System;

namespace GameCore.Rules.Gameplay.Player
{
    /// <summary>Integer distances, headings and stepping on the XZ plane.</summary>
    public static class PlanarMath
    {
        /// <summary>One full turn in milliradians (2 * pi * 1000, rounded).</summary>
        public const int Turn = 6283;

        /// <summary>Half a turn in milliradians (pi * 1000, rounded).</summary>
        public const int HalfTurn = 3142;

        /// <summary>A quarter turn in milliradians (pi / 2 * 1000, rounded).</summary>
        public const int QuarterTurn = 1571;

        // atan(i / 64) in milliradians, i = 0..64, rounded half up.
        private static readonly int[] AtanTable =
        {
            0, 16, 31, 47, 62, 78, 93, 109, 124, 140, 155, 170, 185, 200, 215, 230, 245, 260, 274, 289, 303, 317, 331,
            345, 359, 372, 386, 399, 412, 425, 438, 451, 464, 476, 488, 500, 512, 524, 536, 547, 559, 570, 581, 592, 602,
            613, 623, 633, 644, 653, 663, 673, 682, 692, 701, 710, 719, 728, 736, 745, 753, 761, 770, 778, 785,
        };

        /// <summary>Integer square root: the largest r with r * r &lt;= value (0 for value &lt;= 0).</summary>
        public static long Isqrt(long value)
        {
            if (value <= 0L)
            {
                return 0L;
            }

            long root = (long)Math.Sqrt(value);
            while (root * root > value)
            {
                root--;
            }

            while ((root + 1L) * (root + 1L) <= value)
            {
                root++;
            }

            return root;
        }

        /// <summary>Planar distance in millimetres, clamped to the int32 range.</summary>
        public static int Distance(int dx, int dz)
        {
            long squared = (long)dx * dx + (long)dz * dz;
            long root = Isqrt(squared);
            return root > int.MaxValue ? int.MaxValue : (int)root;
        }

        /// <summary>Planar distance between two points in millimetres.</summary>
        public static int Distance(int ax, int az, int bx, int bz) => Distance(Sub(bx, ax), Sub(bz, az));

        /// <summary>Wraps a heading into [0, <see cref="Turn"/>).</summary>
        public static int NormalizeYaw(int milliradians)
        {
            int wrapped = milliradians % Turn;
            return wrapped < 0 ? wrapped + Turn : wrapped;
        }

        /// <summary>The smallest absolute difference between two headings, in [0, <see cref="HalfTurn"/>].</summary>
        public static int AngleBetween(int a, int b)
        {
            int difference = NormalizeYaw(a - b);
            return difference > Turn - difference ? Turn - difference : difference;
        }

        /// <summary>
        /// The heading of the direction (<paramref name="dx"/>, <paramref name="dz"/>): 0 along +Z, a quarter turn along
        /// +X. The zero vector has heading 0.
        /// </summary>
        public static int YawTowards(int dx, int dz)
        {
            if (dx == 0 && dz == 0)
            {
                return 0;
            }

            long ax = dx < 0 ? -(long)dx : dx;
            long az = dz < 0 ? -(long)dz : dz;
            int fromZ = ax <= az ? AtanOfRatio(ax, az) : QuarterTurn - AtanOfRatio(az, ax);
            int yaw;
            if (dx >= 0 && dz >= 0)
            {
                yaw = fromZ;
            }
            else if (dx >= 0)
            {
                yaw = HalfTurn - fromZ;
            }
            else if (dz < 0)
            {
                yaw = HalfTurn + fromZ;
            }
            else
            {
                yaw = Turn - fromZ;
            }

            return NormalizeYaw(yaw);
        }

        /// <summary>
        /// Moves (<paramref name="x"/>, <paramref name="z"/>) toward (<paramref name="targetX"/>, <paramref name="targetZ"/>)
        /// by at most <paramref name="step"/> millimetres. Returns true when the target is reached (the result is then
        /// exactly the target).
        /// </summary>
        public static bool MoveToward(int x, int z, int targetX, int targetZ, int step, out int nextX, out int nextZ)
        {
            long dx = (long)targetX - x;
            long dz = (long)targetZ - z;
            long distance = Isqrt(dx * dx + dz * dz);
            if (distance <= step || distance == 0L)
            {
                nextX = targetX;
                nextZ = targetZ;
                return true;
            }

            if (step <= 0)
            {
                nextX = x;
                nextZ = z;
                return false;
            }

            nextX = (int)(x + dx * step / distance);
            nextZ = (int)(z + dz * step / distance);
            return false;
        }

        /// <summary>
        /// Scales (<paramref name="dx"/>, <paramref name="dz"/>) down to at most <paramref name="limit"/> millimetres
        /// long; returns true when it had to.
        /// </summary>
        public static bool ClampLength(ref int dx, ref int dz, int limit)
        {
            if (limit < 0)
            {
                limit = 0;
            }

            long squared = (long)dx * dx + (long)dz * dz;
            if (squared <= (long)limit * limit)
            {
                return false;
            }

            long length = Isqrt(squared);
            if (length == 0L)
            {
                dx = 0;
                dz = 0;
                return true;
            }

            dx = (int)((long)dx * limit / length);
            dz = (int)((long)dz * limit / length);
            return true;
        }

        /// <summary>Millimetres covered in <paramref name="milliseconds"/> at <paramref name="millimetresPerSecond"/>.</summary>
        public static int Travel(int millimetresPerSecond, int milliseconds)
        {
            if (millimetresPerSecond <= 0 || milliseconds <= 0)
            {
                return 0;
            }

            long travelled = (long)millimetresPerSecond * milliseconds / 1000L;
            return travelled > int.MaxValue ? int.MaxValue : (int)travelled;
        }

        /// <summary>Saturating int32 subtraction.</summary>
        public static int Sub(int a, int b)
        {
            long value = (long)a - b;
            return value > int.MaxValue ? int.MaxValue : (value < int.MinValue ? int.MinValue : (int)value);
        }

        /// <summary>Saturating int32 addition.</summary>
        public static int Add(int a, int b)
        {
            long value = (long)a + b;
            return value > int.MaxValue ? int.MaxValue : (value < int.MinValue ? int.MinValue : (int)value);
        }

        // atan(small / large) in milliradians for 0 <= small <= large, large > 0, by table interpolation.
        private static int AtanOfRatio(long small, long large)
        {
            long scaled = small * 64L;
            long index = scaled / large;
            if (index >= 64L)
            {
                return AtanTable[64];
            }

            long remainder = scaled % large;
            int low = AtanTable[index];
            int high = AtanTable[index + 1];
            return (int)(low + (high - low) * remainder / large);
        }
    }
}

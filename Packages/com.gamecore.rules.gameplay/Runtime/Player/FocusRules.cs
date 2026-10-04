// GameCore.Rules.Gameplay.Player - interaction focus ranking (P1.3).
//
// The presentation gathers focus candidates (interactables and NPCs near the player, from committed slot positions,
// optionally filtered by a physics sphere-cast when views exist) and asks these rules which one to focus. Ranking is
// integer and total: a candidate must be within range and inside the view cone (unless it is very close); the higher
// priority wins, then the better score, then the lower key. The current focus is kept while it stays valid and is not
// clearly beaten (hysteresis), so the prompt does not flicker between two equal candidates.
#nullable enable
using System.Collections.Generic;

namespace GameCore.Rules.Gameplay.Player
{
    /// <summary>One focus candidate relative to the player.</summary>
    public readonly struct FocusCandidate
    {
        public FocusCandidate(int key, int dx, int dz, int priority)
        {
            Key = key;
            Dx = dx;
            Dz = dz;
            Priority = priority;
        }

        /// <summary>Positive stable key of the candidate.</summary>
        public int Key { get; }

        /// <summary>Offset from the player in millimetres.</summary>
        public int Dx { get; }

        public int Dz { get; }

        /// <summary>Higher wins (NPCs and doors can outrank examinables).</summary>
        public int Priority { get; }
    }

    /// <summary>Integer focus tuning.</summary>
    public readonly struct FocusTuning
    {
        public FocusTuning(int rangeMillimetres, int halfAngleMilliradians, int closeRangeMillimetres, int hysteresisPercent)
        {
            RangeMillimetres = rangeMillimetres < 0 ? 0 : rangeMillimetres;
            HalfAngleMilliradians = halfAngleMilliradians < 0 ? 0 : (halfAngleMilliradians > PlanarMath.HalfTurn ? PlanarMath.HalfTurn : halfAngleMilliradians);
            CloseRangeMillimetres = closeRangeMillimetres < 0 ? 0 : closeRangeMillimetres;
            HysteresisPercent = hysteresisPercent < 0 ? 0 : (hysteresisPercent > 100 ? 100 : hysteresisPercent);
        }

        public int RangeMillimetres { get; }

        public int HalfAngleMilliradians { get; }

        /// <summary>Inside this distance the view cone is ignored.</summary>
        public int CloseRangeMillimetres { get; }

        /// <summary>The current focus is kept unless a rival scores this many percent better.</summary>
        public int HysteresisPercent { get; }

        /// <summary>2.5 m range, 60 degree half cone, 0.8 m close range, 20 % hysteresis.</summary>
        public static FocusTuning Default => new FocusTuning(2500, 1047, 800, 20);
    }

    /// <summary>Pure focus selection.</summary>
    public static class FocusRules
    {
        /// <summary>Score of a candidate (lower is better), or -1 when it is not focusable.</summary>
        public static int Score(FocusCandidate candidate, int viewerYaw, FocusTuning tuning)
        {
            if (candidate.Key <= 0)
            {
                return -1;
            }

            int distance = PlanarMath.Distance(candidate.Dx, candidate.Dz);
            if (distance > tuning.RangeMillimetres)
            {
                return -1;
            }

            int angle = distance == 0 ? 0 : PlanarMath.AngleBetween(PlanarMath.YawTowards(candidate.Dx, candidate.Dz), viewerYaw);
            bool close = distance <= tuning.CloseRangeMillimetres;
            if (!close && angle > tuning.HalfAngleMilliradians)
            {
                return -1;
            }

            // Distance as thousandths of the range plus the angle as thousandths of the cone, equally weighted.
            long range = tuning.RangeMillimetres < 1 ? 1 : tuning.RangeMillimetres;
            long cone = tuning.HalfAngleMilliradians < 1 ? 1 : tuning.HalfAngleMilliradians;
            long distanceTerm = distance * 1000L / range;
            long angleTerm = close ? 0L : angle * 1000L / cone;
            return (int)(distanceTerm + angleTerm);
        }

        /// <summary>The key to focus, or <see cref="PlayerRules.NoFocus"/> when no candidate qualifies.</summary>
        public static int Rank(IReadOnlyList<FocusCandidate> candidates, int viewerYaw, FocusTuning tuning, int currentFocus)
        {
            int bestKey = PlayerRules.NoFocus;
            int bestScore = -1;
            int bestPriority = int.MinValue;
            int currentScore = -1;
            int currentPriority = int.MinValue;
            if (candidates != null)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    FocusCandidate candidate = candidates[i];
                    int score = Score(candidate, viewerYaw, tuning);
                    if (score < 0)
                    {
                        continue;
                    }

                    if (candidate.Key == currentFocus)
                    {
                        currentScore = score;
                        currentPriority = candidate.Priority;
                    }

                    if (bestScore < 0 || Better(score, candidate.Priority, candidate.Key, bestScore, bestPriority, bestKey))
                    {
                        bestScore = score;
                        bestPriority = candidate.Priority;
                        bestKey = candidate.Key;
                    }
                }
            }

            if (currentScore >= 0 && bestKey != currentFocus && currentPriority >= bestPriority)
            {
                // Keep the current focus unless the rival is clearly better.
                long threshold = (long)currentScore * (100 - tuning.HysteresisPercent) / 100L;
                if (bestScore >= threshold)
                {
                    return currentFocus;
                }
            }

            return bestKey;
        }

        private static bool Better(int score, int priority, int key, int bestScore, int bestPriority, int bestKey)
        {
            if (priority != bestPriority)
            {
                return priority > bestPriority;
            }

            if (score != bestScore)
            {
                return score < bestScore;
            }

            return key < bestKey;
        }
    }
}

// GameCore.Gameplay.Contracts - GameplayClock: the one gameplay time base of a world (P1.7a, A5).
//
// A gameplay world is command-driven (WorldBuilder): its domain clock does not advance, so DomainSeconds stays at its
// origin and any time a rule read from it was frozen. Gameplay time is therefore derived from the logical step:
// now = step x stepMs. A fixed-step world advances its domain clock itself, and the clock reads that instead. Every
// gameplay time read - logic cooldowns and time conditions, NPC schedules - goes through these helpers, so two boots fed
// the same commands read the same times and a restored world (which resumes at the captured logical step, SADR-012)
// continues the same timeline.
//
// Pure functions of their arguments; no state.
#nullable enable
using GameCore.Contracts;

namespace GameCore.Gameplay.Contracts
{
    /// <summary>Exact outbox admission for a proposed batch; implemented by the world's delivery owner.</summary>
    public interface IGameplayDeliveryBudget
    {
        int CountActionDemand(string actionRef, string subjectAuthoringId, int subjectKey, int actorKey);

        bool HasRoomFor(System.Collections.Generic.IReadOnlyList<SchemaRef> schemas,
            System.Collections.Generic.IReadOnlyList<FrozenPayload> payloads, int actionDemands = 0);
    }

    /// <summary>The gameplay time base: milliseconds derived from the logical step (or the fixed-step domain clock).</summary>
    public static class GameplayClock
    {
        /// <summary>Milliseconds one command-driven step stands for (PlayerTuning.StepMs and the NpcRoster default).</summary>
        public const int DefaultStepMilliseconds = 20;

        /// <summary>The step length to use: <paramref name="stepMs"/> when positive, else the default.</summary>
        public static int StepLength(int stepMs) => stepMs > 0 ? stepMs : DefaultStepMilliseconds;

        /// <summary>Milliseconds since the world's step zero: <c>step x stepMs</c> (64-bit, saturating).</summary>
        public static long StepTimeMs(ulong step, int stepMs)
        {
            ulong length = (ulong)StepLength(stepMs);
            ulong limit = (ulong)long.MaxValue / length;
            return step >= limit ? long.MaxValue : (long)(step * length);
        }

        /// <summary>
        /// Gameplay milliseconds of a world: <c>step x stepMs</c> for a command-driven world, the domain clock for a
        /// fixed-step world.
        /// </summary>
        public static long NowMs(TemporalModel model, ulong step, int stepMs, double domainSeconds)
        {
            if (model == TemporalModel.FixedStep)
            {
                double ms = domainSeconds * GameplayUnits.MillisecondsPerSecond;
                if (double.IsNaN(ms) || ms <= 0.0)
                {
                    return 0L;
                }

                return ms >= long.MaxValue ? long.MaxValue : (long)ms;
            }

            return StepTimeMs(step, stepMs);
        }

        /// <summary>
        /// <see cref="NowMs(TemporalModel, ulong, int, double)"/> clamped to the int32 slot range (rule cooldown slots and
        /// condition inputs are int32: about 24.8 days of play at the default step).
        /// </summary>
        public static int NowMs32(TemporalModel model, ulong step, int stepMs, double domainSeconds) =>
            Clamp(NowMs(model, step, stepMs, domainSeconds));

        /// <summary>A 64-bit millisecond value clamped to [0, int.MaxValue].</summary>
        public static int Clamp(long ms) => ms <= 0L ? 0 : (ms >= int.MaxValue ? int.MaxValue : (int)ms);

        /// <summary>The number of whole steps that cover <paramref name="ms"/> (rounded up; never negative).</summary>
        public static long StepsFor(long ms, int stepMs)
        {
            if (ms <= 0L)
            {
                return 0L;
            }

            long length = StepLength(stepMs);
            return (ms + length - 1L) / length;
        }
    }
}

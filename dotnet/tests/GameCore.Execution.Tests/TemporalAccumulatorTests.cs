#nullable enable
using System;
using GameCore.Contracts;
using GameCore.Execution;
using NUnit.Framework;

namespace GameCore.Execution.Tests
{
    /// <summary>
    /// Temporal model tests (GC-005, TEST-011). They prove the fixed-step accumulator retains exact debt and never
    /// lengthens a step, that a command-driven world executes zero steps without demand, and that a paused world
    /// accrues no debt for the paused interval (P-035 to P-038).
    /// </summary>
    [TestFixture]
    public sealed class TemporalAccumulatorTests
    {
        private const ulong Millisecond = 10_000UL;

        private static FixedStepSettings Settings(ulong stepMs, uint catchUp)
            => new FixedStepSettings(stepMs * Millisecond, 1_000UL * Millisecond, catchUp, usesUnscaledHostClock: true);

        [Test]
        public void FixedStepAdmitsWholeStepsAndRetainsTheRemainder()
        {
            var accumulator = new FixedStepAccumulator(Settings(10UL, 8U), 0UL);

            TemporalSample first = accumulator.Sample(25UL * Millisecond, 0UL);
            Assert.That(first.Accepted, Is.True, first.ToString());
            Assert.That(first.AdmittedSteps, Is.EqualTo(2UL), "25 ms at 10 ms per step admits two whole steps.");
            Assert.That(first.RetainedDebt.Ticks, Is.EqualTo(5UL * Millisecond), "The remainder is retained, not discarded.");
            Assert.That(accumulator.AdmittedStepTotal, Is.EqualTo(2UL));

            TemporalSample second = accumulator.Sample(35UL * Millisecond, 0UL);
            Assert.That(second.AdmittedSteps, Is.EqualTo(1UL));
            Assert.That(second.RetainedDebt, Is.EqualTo(TimeDebt.Zero), "5 retained + 10 elapsed funds exactly one step.");

            TemporalSample third = accumulator.Sample(40UL * Millisecond, 0UL);
            Assert.That(third.AdmittedSteps, Is.EqualTo(0UL), "Below one step duration nothing advances.");
            Assert.That(third.RetainedDebt.Ticks, Is.EqualTo(5UL * Millisecond), "The sub-step remainder stays visible as debt.");
            Assert.That(third.HasWork, Is.False);
        }

        [Test]
        public void FixedStepCatchUpIsBoundedAndDebtIsNeverDropped()
        {
            var accumulator = new FixedStepAccumulator(Settings(10UL, 3U), 0UL);

            TemporalSample spike = accumulator.Sample(100UL * Millisecond, 0UL);
            Assert.That(spike.AdmittedSteps, Is.EqualTo(3UL), "The catch-up limit bounds one pump.");
            Assert.That(spike.RetainedDebt.Ticks, Is.EqualTo(70UL * Millisecond), "Unadmitted time stays as debt.");

            TemporalSample catchUp = accumulator.Sample((100UL * Millisecond) + 1UL, 0UL);
            Assert.That(catchUp.AdmittedSteps, Is.EqualTo(7UL), "Retained debt is spent on later pumps instead of a longer step.");
            Assert.That(catchUp.RetainedDebt.Ticks, Is.EqualTo(1UL), "Only the sub-step remainder stays as debt.");

            TemporalSample idle = accumulator.Sample((100UL * Millisecond) + 1UL, 0UL);
            Assert.That(idle.AdmittedSteps, Is.EqualTo(0UL));
            Assert.That(idle.RetainedDebt.Ticks, Is.EqualTo(1UL), "A sub-step remainder is retained rather than discarded.");
            Assert.That(accumulator.AdmittedStepTotal, Is.EqualTo(10UL));
        }

        [Test]
        public void FixedStepPauseAddsNoDebtAndPreservesTheAccumulator()
        {
            var accumulator = new FixedStepAccumulator(Settings(10UL, 4U), 0UL);
            accumulator.Sample(25UL * Millisecond, 0UL);
            TimeDebt beforePause = accumulator.RetainedDebt;
            Assert.That(beforePause.Ticks, Is.EqualTo(5UL * Millisecond));

            accumulator.Pause();
            TemporalSample paused = accumulator.Sample(10_000UL * Millisecond, 0UL);
            Assert.That(paused.AdmittedSteps, Is.EqualTo(0UL), "A paused world adds no steps.");
            Assert.That(paused.RetainedDebt, Is.EqualTo(beforePause), "Pausing preserves retained debt.");

            accumulator.Resume(10_000UL * Millisecond);
            TemporalSample resumed = accumulator.Sample(10_000UL * Millisecond, 0UL);
            Assert.That(resumed.AdmittedSteps, Is.EqualTo(0UL), "The paused interval adds no debt on resume.");
            Assert.That(resumed.RetainedDebt, Is.EqualTo(beforePause), "Resuming preserves the pre-pause debt.");

            TemporalSample after = accumulator.Sample(10_010UL * Millisecond, 0UL);
            Assert.That(after.AdmittedSteps, Is.EqualTo(1UL), "Retained debt and new time are spent normally after resume.");
            Assert.That(after.RetainedDebt.Ticks, Is.EqualTo(5UL * Millisecond));
        }

        [Test]
        public void FixedStepRejectsABackwardsHostClock()
        {
            var accumulator = new FixedStepAccumulator(Settings(10UL, 2U), 1_000UL);
            TemporalSample sample = accumulator.Sample(999UL, 0UL);

            Assert.That(sample.Accepted, Is.False);
            Assert.That(sample.Code, Is.EqualTo(DiagnosticCode.UnsupportedVersion));
            Assert.That(sample.RetainedDebt, Is.EqualTo(TimeDebt.Zero), "A rejected sample changes nothing.");

            TemporalSample recovered = accumulator.Sample(1_000UL, 0UL);
            Assert.That(recovered.Accepted, Is.True, "The origin never moves backwards, so the next legal sample works.");
            Assert.That(recovered.AdmittedSteps, Is.EqualTo(0UL));
        }

        [Test]
        public void CommandDrivenWorldExecutesZeroStepsWithoutDemand()
        {
            var accumulator = new CommandDrivenAccumulator(1U, 0UL);

            for (int frame = 1; frame <= 600; frame++)
            {
                TemporalSample sample = accumulator.Sample((ulong)frame * 16UL * Millisecond, 0UL);
                Assert.That(sample.Accepted, Is.True);
                Assert.That(sample.AdmittedSteps, Is.EqualTo(0UL), "No commands or wakes means zero logical steps.");
                Assert.That(sample.RetainedDebt, Is.EqualTo(TimeDebt.Zero), "A command-driven world has no elapsed simulation seconds.");
            }

            Assert.That(accumulator.AdmittedStepTotal, Is.EqualTo(0UL));
            Assert.That(accumulator.HostElapsedTicks, Is.GreaterThan(0UL), "Host time is still observed, it just does not advance simulation.");
        }

        [Test]
        public void CommandDrivenWorldAdmitsDemandUpToItsPerPumpLimit()
        {
            var accumulator = new CommandDrivenAccumulator(2U, 0UL);

            TemporalSample one = accumulator.Sample(16UL * Millisecond, 1UL);
            Assert.That(one.AdmittedSteps, Is.EqualTo(1UL));

            TemporalSample capped = accumulator.Sample(32UL * Millisecond, 5UL);
            Assert.That(capped.AdmittedSteps, Is.EqualTo(2UL), "At most the declared per-pump limit is admitted.");

            accumulator.Pause();
            TemporalSample whilePaused = accumulator.Sample(48UL * Millisecond, 3UL);
            Assert.That(whilePaused.AdmittedSteps, Is.EqualTo(0UL), "A paused world does not advance even with demand queued (P-035).");

            accumulator.Resume(48UL * Millisecond);
            TemporalSample resumed = accumulator.Sample(64UL * Millisecond, 1UL);
            Assert.That(resumed.AdmittedSteps, Is.EqualTo(1UL), "Queued demand survives the pause and is admitted on resume.");
        }

        [Test]
        public void FactoriesRejectAnInvalidConfiguration()
        {
            Assert.Throws<ArgumentException>(() => new FixedStepAccumulator(Settings(0UL, 1U), 0UL));
            Assert.Throws<ArgumentException>(() => new CommandDrivenAccumulator(0U, 0UL));
            Assert.Throws<ArgumentException>(() => TemporalAccumulators.Create(TemporalModel.FixedStep, null, 0UL));

            ITemporalAccumulator fixedStep = TemporalAccumulators.Create(TemporalModel.FixedStep, Settings(5UL, 1U), 0UL);
            Assert.That(fixedStep.Model, Is.EqualTo(TemporalModel.FixedStep));
            Assert.That(fixedStep.UsesUnscaledHostClock, Is.True);
            Assert.That(fixedStep.MaxStepsPerPump, Is.EqualTo(1U));

            ITemporalAccumulator command = TemporalAccumulators.Create(TemporalModel.CommandDriven, null, 0UL);
            Assert.That(command.Model, Is.EqualTo(TemporalModel.CommandDriven));
            Assert.That(command.MaxStepsPerPump, Is.EqualTo(1U), "One command or wake per logical step by default (P-037).");
        }
    }
}

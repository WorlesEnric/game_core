#nullable enable
using GameCore.Contracts;
using GameCore.Unity.Fixtures;
using GameCore.Unity.Runtime;
using NUnit.Framework;

namespace GameCore.Unity.Runtime.Tests
{
    /// <summary>
    /// Host-level temporal wiring tests (GC-005, TEST-011). The pure accumulator semantics are covered by the
    /// plain-dotnet suite; here the host must convert host time with the world's declared rate, bound catch-up,
    /// retain the remainder as debt, supply the logical clock to <c>World.Time</c>, and advance the unmanaged
    /// fixed-step stage through the guarded dispatcher (P-036, P-038).
    /// </summary>
    [TestFixture]
    public sealed class TemporalDriverTests
    {
        private const ulong SessionSalt = 0x54454D504F52414CUL;

        private static readonly Id128 Issuer = new Id128(0x4953535545525441UL, 1UL);

        private static ulong sessionSequence;

        [TearDown]
        public void TearDown() => UnityWorldRegistry.ResetAll();

        private static UnityWorldHost CreateFixedWorld()
        {
            sessionSequence++;
            var world = new WorldId(new Id128(SessionSalt, sessionSequence));
            var operation = new OperationId(world, Issuer, 1UL);

            bool created = UnityWorldRegistry.TryCreate(
                FixtureRegistration.FixedStepRequest(world, operation, ContentHash.Empty),
                FixtureRegistration.Create(FixtureWorldShape.FixedStep, includeFaultStage: false),
                out UnityWorldHost? host,
                out WorldCreateResult result);

            Assert.That(created, Is.True, result.Code + ": " + result.Detail);
            return host!;
        }

        [Test]
        public void FixedStepHostBoundsCatchUpAndRetainsTheUnspentRemainder()
        {
            UnityWorldHost host = CreateFixedWorld();
            Assert.That(host.HostTicksPerSecond, Is.EqualTo(FixtureRegistration.HostTicksPerSecond));
            Assert.That(host.Temporal.UsesUnscaledHostClock, Is.True);

            // The first routable pump fixes the world's clock origin; elapsed time is measured from there.
            const ulong origin = 5_000_000UL;
            WorldPumpResult opener = host.PumpFrame(origin);
            Assert.That(opener.Sample.AdmittedSteps, Is.EqualTo(0UL), "host time before the world's first pump is not simulation debt");

            // One second of host time at 10 ms per step is 100 steps; the fixture admits at most four per pump.
            WorldPumpResult first = host.PumpFrame(origin + FixtureRegistration.HostTicksPerSecond);
            Assert.That(first.Sample.AdmittedSteps, Is.EqualTo(4UL), "the per-pump catch-up limit bounds one frame");
            Assert.That(host.CurrentStep, Is.EqualTo(new LogicalStepId(4UL)));
            Assert.That(host.RetainedDebt.Ticks, Is.EqualTo(9_600_000UL), "unadmitted time is retained as exact debt");

            // The next pump has no new elapsed time; the retained debt still admits one bounded batch.
            WorldPumpResult second = host.PumpFrame(origin + FixtureRegistration.HostTicksPerSecond);
            Assert.That(second.Sample.AdmittedSteps, Is.EqualTo(4UL));
            Assert.That(second.Sample.RetainedDebt.Ticks, Is.EqualTo(9_200_000UL));
            Assert.That(host.CurrentStep, Is.EqualTo(new LogicalStepId(8UL)));

            WorldPumpResult third = host.PumpFrame(origin + FixtureRegistration.HostTicksPerSecond + 50_000UL);
            Assert.That(third.Sample.AdmittedSteps, Is.EqualTo(4UL));
            Assert.That(host.RetainedDebt.Ticks, Is.EqualTo(8_850_000UL), "the 5 ms remainder joins the debt instead of being dropped");

            Assert.That(
                FixtureWorldState.TryReadTrail(host.EntityWorld.EntityManager, out FixtureTrail trail),
                Is.True);
            Assert.That(trail.TickCount, Is.EqualTo(12), "the unmanaged fixed-step stage runs exactly once per admitted step");
            Assert.That(host.Publications.PublishedCount, Is.EqualTo(13), "one image per committed step plus the initial one");
        }

        [Test]
        public void APumpBelowOneStepDurationAdvancesNothingAndKeepsTheRemainder()
        {
            UnityWorldHost host = CreateFixedWorld();

            host.PumpFrame(0UL);
            WorldPumpResult below = host.PumpFrame(50_000UL);
            Assert.That(below.Sample.Accepted, Is.True, "an idle frame is normal, not an error");
            Assert.That(below.Sample.AdmittedSteps, Is.EqualTo(0UL));
            Assert.That(below.Sample.RetainedDebt.Ticks, Is.EqualTo(50_000UL), "the sub-step remainder is retained");
            Assert.That(below.Advance, Is.Null, "no step is requested below one step duration");
            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.Zero));
            Assert.That(host.Driver.CommittedStepCount, Is.EqualTo(0));

            WorldPumpResult crossing = host.PumpFrame(150_000UL);
            Assert.That(crossing.Sample.AdmittedSteps, Is.EqualTo(1UL), "retained debt plus new time funds exactly one step");
            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.First));
            Assert.That(host.RetainedDebt.Ticks, Is.EqualTo(50_000UL));

            WorldPumpResult idle = host.PumpFrame(150_000UL);
            Assert.That(idle.Sample.AdmittedSteps, Is.EqualTo(0UL));
            Assert.That(idle.Sample.RetainedDebt.Ticks, Is.EqualTo(50_000UL), "a sub-step remainder is never discarded");
            Assert.That(host.Driver.CommittedStepCount, Is.EqualTo(1));
        }

        [Test]
        public void FixedStepHostSuppliesTheLogicalClockToWorldTime()
        {
            UnityWorldHost host = CreateFixedWorld();

            host.PumpFrame(0UL);
            host.PumpFrame(FixtureRegistration.HostTicksPerSecond);

            // Four steps ran at logical indices 0..3 with a 10 ms step duration; World.Time carries that clock.
            double elapsed = host.EntityWorld.Time.ElapsedTime;
            Assert.That(elapsed, Is.EqualTo(0.03).Within(1e-6), "World.Time is the logical clock, not wall time (04 s3)");
            Assert.That(host.EntityWorld.Time.DeltaTime, Is.EqualTo(0.01f).Within(1e-6f));
        }

        [Test]
        public void CommandDrivenHostIgnoresElapsedHostTimeWithoutDemand()
        {
            sessionSequence++;
            var world = new WorldId(new Id128(SessionSalt, sessionSequence));
            var operation = new OperationId(world, Issuer, 2UL);

            bool created = UnityWorldRegistry.TryCreate(
                FixtureRegistration.CommandDrivenRequest(world, operation, ContentHash.Empty),
                FixtureRegistration.Create(FixtureWorldShape.CommandDriven, includeFaultStage: true),
                out UnityWorldHost? host,
                out WorldCreateResult result);

            Assert.That(created, Is.True, result.Code + ": " + result.Detail);
            Assert.That(host!.Temporal.UsesUnscaledHostClock, Is.False);

            for (ulong frame = 1UL; frame <= 10UL; frame++)
            {
                host.PumpFrame(frame * FixtureRegistration.HostTicksPerSecond);
            }

            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.Zero), "ten seconds of host time advance nothing (P-036)");
            Assert.That(host.RetainedDebt, Is.EqualTo(TimeDebt.Zero));
            Assert.That(host.Temporal.AdmittedStepTotal, Is.EqualTo(0UL));
            Assert.That(host.DomainSeconds, Is.EqualTo(0.0));

            host.AdvanceDomainTime(2.5);
            host.NotifyCommandAdmitted(1U);
            host.PumpFrame(11UL * FixtureRegistration.HostTicksPerSecond);

            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.First), "only admitted demand advances the step");
            Assert.That(host.DomainSeconds, Is.EqualTo(2.5), "explicit domain time is the only command-driven clock advance (P-038)");
        }
    }
}

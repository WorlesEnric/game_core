#nullable enable
using System.Collections;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Unity.Adapters;
using GameCore.Unity.Fixtures;
using GameCore.Unity.Runtime;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GameCore.Unity.Adapters.Tests
{
    /// <summary>
    /// Play Mode integration tests (GC-005, TEST-011/TEST-018). They run in a real player loop: the application
    /// bootstrap installs exactly one route, repeated installation is idempotent, the reset path leaves no stale
    /// node or host behind, and an idle command-driven world executes zero steps across real frames while ingress
    /// and presentation keep running.
    /// </summary>
    [TestFixture]
    public sealed class PlayerLoopIntegrationTests
    {
        private const ulong SessionSalt = 0x504C41594D4F4445UL;

        private static readonly Id128 Issuer = new Id128(0x495353554552504CUL, 1UL);

        private static ulong sessionSequence;

        private readonly List<UnityWorldHost> ownedHosts = new List<UnityWorldHost>();

        private bool pumpWasEnabled;

        [SetUp]
        public void SetUp()
        {
            pumpWasEnabled = GameCoreApplicationPump.IsEnabled;

            // Tests drive their own worlds explicitly so frame-by-frame counts stay exact.
            GameCoreApplicationPump.IsEnabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = ownedHosts.Count - 1; i >= 0; i--)
            {
                UnityWorldHost host = ownedHosts[i];
                host.Stop(new OperationId(host.World, Issuer, 99UL), "test teardown");
                host.Dispose();
            }

            ownedHosts.Clear();
            GameCoreApplicationPump.IsEnabled = pumpWasEnabled;
        }

        private UnityWorldHost CreateCommandWorld()
        {
            sessionSequence++;
            var world = new WorldId(new Id128(SessionSalt, sessionSequence));
            var operation = new OperationId(world, Issuer, 1UL);

            bool created = UnityWorldRegistry.TryCreate(
                FixtureRegistration.CommandDrivenRequest(world, operation, ContentHash.Empty),
                FixtureRegistration.Create(FixtureWorldShape.CommandDriven, includeFaultStage: true),
                out UnityWorldHost? host,
                out WorldCreateResult result);

            Assert.That(created, Is.True, result.Code + ": " + result.Detail);
            ownedHosts.Add(host!);
            return host!;
        }

        [Test]
        public void BootstrapInstalledExactlyOneApplicationWorldAndOneRoute()
        {
            Assert.That(
                GameCoreApplicationBootstrap.BootstrapCount,
                Is.EqualTo(1),
                "the application bootstrap runs once per Play Mode session");
            Assert.That(GameCoreApplicationComposition.HasRegistration, Is.True, "the application composition root registered a world shape");

            GameCorePlayerLoopInstaller.EnsureInstalled();
            Assert.That(GameCorePlayerLoopInstaller.CountInstalledNodes(), Is.EqualTo(1), "exactly one GameCore route exists in the loop");

            Assert.That(
                GameCoreApplicationBootstrap.LastWorldName,
                Does.Contain(":"),
                "the bootstrap names the world incarnation it created (TEST-018)");

            UnityWorldHost host = CreateCommandWorld();
            host.NotifyCommandAdmitted(1U);
            WorldPumpResult pumped = GameCoreApplicationPump.PumpWorld(host);

            Assert.That(pumped.Pumped, Is.True);
            Assert.That(pumped.Advance, Is.Not.Null);
            Assert.That(pumped.Advance!.Accepted, Is.True);
            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.First));
        }

        [Test]
        public void InstallingThePumpNodeIsIdempotent()
        {
            int first = GameCorePlayerLoopInstaller.EnsureInstalled();
            Assert.That(first, Is.EqualTo(1));

            int second = GameCorePlayerLoopInstaller.EnsureInstalled();
            Assert.That(second, Is.EqualTo(1), "installing twice must not create a second route");
            Assert.That(GameCorePlayerLoopInstaller.CountInstalledNodes(), Is.EqualTo(1));

            int removed = GameCorePlayerLoopInstaller.Remove();
            Assert.That(removed, Is.EqualTo(1));
            Assert.That(GameCorePlayerLoopInstaller.CountInstalledNodes(), Is.EqualTo(0));

            Assert.That(GameCorePlayerLoopInstaller.EnsureInstalled(), Is.EqualTo(1), "installing after removal restores one route");
        }

        [Test]
        public void AStaleGenerationTrampolineRefusesToPump()
        {
            GameCorePlayerLoopInstaller.EnsureInstalled();
            int framesBefore = GameCoreApplicationPump.FrameCount;
            int staleBefore = GameCoreApplicationPump.StaleTrampolineRefusalCount;

            GameCoreApplicationPump.PumpTrampoline(GameCorePlayerLoopInstaller.Generation + 1);

            Assert.That(GameCoreApplicationPump.StaleTrampolineRefusalCount, Is.EqualTo(staleBefore + 1), "a node from another session must not pump");
            Assert.That(GameCoreApplicationPump.FrameCount, Is.EqualTo(framesBefore));
        }

        [Test]
        public void ResetForNewSessionRemovesRoutesAndDisposesSurvivingHosts()
        {
            UnityWorldHost host = CreateCommandWorld();
            host.Stop(new OperationId(host.World, Issuer, 98UL), "test stop");
            ownedHosts.Clear();

            UnityWorldHost survivor = CreateCommandWorld();
            Assert.That(UnityWorldRegistry.TryGet(survivor.World, out _), Is.True);

            int generationBefore = GameCorePlayerLoopInstaller.Generation;
            int resetRunsBefore = GameCoreApplicationReset.RunCount;

            GameCoreApplicationReset.RunReset();

            Assert.That(GameCoreApplicationReset.RunCount, Is.EqualTo(resetRunsBefore + 1), "the reset path the attribute invokes is executable");
            Assert.That(GameCorePlayerLoopInstaller.Generation, Is.EqualTo(generationBefore + 1), "a new session invalidates earlier delegates");
            Assert.That(GameCoreApplicationReset.LastRemovedNodeCount, Is.EqualTo(1), "the previous session's route is removed");
            Assert.That(GameCorePlayerLoopInstaller.CountInstalledNodes(), Is.EqualTo(0));
            Assert.That(UnityWorldRegistry.Count, Is.EqualTo(0), "no static host reference survives the session boundary");
            Assert.That(survivor.Lifecycle, Is.EqualTo(WorldLifecycleState.Disposed));
            Assert.That(GameCoreApplicationPump.FrameCount, Is.EqualTo(0), "per-session pump counters reset");
            Assert.That(GameCoreApplicationPump.IsEnabled, Is.True);

            ownedHosts.Clear();

            // Leave the session with exactly one route so a later frame can pump again.
            Assert.That(GameCorePlayerLoopInstaller.EnsureInstalled(), Is.EqualTo(1));

            GameCoreApplicationPump.IsEnabled = false;
        }

        [UnityTest]
        public IEnumerator IdleCommandWorldAdvancesZeroStepsOverManyRealFrames()
        {
            UnityWorldHost host = CreateCommandWorld();
            GameCoreApplicationPump.IsEnabled = true;

            for (int frame = 0; frame < 20; frame++)
            {
                yield return null;
            }

            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.Zero), "an idle command-driven world executes zero simulation steps");
            Assert.That(host.Driver.CommittedStepCount, Is.EqualTo(0));
            Assert.That(host.Publications.PublishedCount, Is.EqualTo(1));
            Assert.That(host.PumpCount, Is.GreaterThan(0), "the loop did route host frames to this world");

            Assert.That(FixtureWorldState.TryReadTrail(host.EntityWorld.EntityManager, out FixtureTrail trail), Is.True);
            Assert.That(trail.AcceptCount, Is.EqualTo(0));
            Assert.That(trail.IngressCount, Is.GreaterThan(0), "ingress still runs on host frames of an idle world");
            Assert.That(trail.OutputCount, Is.GreaterThan(0), "presentation still runs on host frames of an idle world");
            Assert.That(trail.IngressCount, Is.EqualTo(host.PumpCount));
            Assert.That(trail.OutputCount, Is.EqualTo(host.PumpCount));

            GameCoreApplicationPump.IsEnabled = false;
        }

        [UnityTest]
        public IEnumerator CommandDrivenWorldAdvancesOncePerAdmittedCommand()
        {
            UnityWorldHost host = CreateCommandWorld();
            GameCoreApplicationPump.IsEnabled = true;
            host.NotifyCommandAdmitted(1U);

            LogicalStepId before = host.CurrentStep;
            for (int frame = 0; frame < 3; frame++)
            {
                yield return null;
            }

            Assert.That(host.CurrentStep, Is.EqualTo(new LogicalStepId(before.Value + 1UL)), "one admitted command advances exactly one step");
            Assert.That(host.PendingDemand, Is.EqualTo(0UL));

            for (int frame = 0; frame < 3; frame++)
            {
                yield return null;
            }

            Assert.That(host.CurrentStep, Is.EqualTo(new LogicalStepId(before.Value + 1UL)), "with no further demand the world stays idle");

            GameCoreApplicationPump.IsEnabled = false;
        }
    }
}

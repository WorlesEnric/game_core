// GameCore.App tests — P0.4 kernel-app acceptance (SADR-010, SADR-011, SADR-013).
//
// Every test boots the probe game through the MonoBehaviour-free entry `GameApplication.Boot(definition, options)`,
// with the PlayerLoop node and the default-world assignment switched off so EditMode global state is untouched, and
// a test-driven frame clock (EditMode has no advancing host frame). Pumps go through the one sanctioned application
// pump, `GameCoreApplicationPump.PumpFrame()`, so the pump counter sees exactly what a player would.
#nullable enable
using System;
using System.Text;
using System.Text.RegularExpressions;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Unity.Adapters;
using GameCore.Unity.App;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameCore.App.Tests
{
    [TestFixture]
    public sealed class GameApplicationRootTests
    {
        private long frame;
        private bool pumpWasEnabled;

        [SetUp]
        public void SetUp()
        {
            AppProbeRecorder.Reset();
            frame = 1000L;
            pumpWasEnabled = GameCoreApplicationPump.IsEnabled;
            GameCoreApplicationPump.IsEnabled = true;
            GameCoreThreading.CaptureMainThread();
        }

        [TearDown]
        public void TearDown()
        {
            GameApplicationRoot? current = GameApplication.Current;
            if (current != null)
            {
                current.Stop("test teardown");
            }

            GameCoreApplicationPump.IsEnabled = pumpWasEnabled;
            AppProbeRecorder.Reset();
        }

        // ------------------------------------------------------------------ SADR-010

        [Test]
        public void Boot_ComposesTheRootWithTheRealCatalogHash_AndOnePumpPath()
        {
            GameApplicationRoot root = Boot();

            Assert.That(root.State, Is.EqualTo(GameApplicationState.Ready), "a booted root waits for Start");
            Assert.That(root.Host.Lifecycle, Is.EqualTo(WorldLifecycleState.Paused));
            Assert.That(root.CatalogHash.IsEmpty, Is.False, "the world is created with the real fingerprint");
            Assert.That(root.CatalogHash.Equals(root.Definition.Catalog.Fingerprint), Is.True);
            Assert.That(GameCoreApplicationBootstrap.FallbackCount, Is.EqualTo(0));
            Assert.That(GameApplication.Current, Is.SameAs(root));
            Assert.That(GameApplication.LastFailure, Is.Null);

            // Every kernel part is composed, joined and reachable from the root.
            Assert.That(root.Manifests.AcceptedCount, Is.EqualTo(2));
            Assert.That(root.Lane.FindInstall(AppProbe.ProbeInstance), Is.Not.Null, "the boot script mounted plugin A");
            Assert.That(root.Targets.Count, Is.EqualTo(1));
            Assert.That(root.Bridge.Pipeline, Is.SameAs(root.Pipeline));
            Assert.That(root.Bridge.Preflight, Is.SameAs(root.Preflight));
            Assert.That(root.Preflight.Pipeline, Is.SameAs(root.Pipeline));
            Assert.That(root.Validators.Validators.Count, Is.EqualTo(2));
            Assert.That(root.Explanations.Store, Is.SameAs(root.Provenance));
            Assert.That(root.Observations, Is.Not.Null);
            Assert.That(
                AssemblyPublisher.MatchesPublishedAssembly(
                    root.Lane.Committed.Revision, root.Lane.Committed.Epoch, root.Publisher.PublishedRevision, root.Host.CurrentEpoch),
                Is.True,
                "lane and world publish one series after boot (P-006)");
            Assert.That(AdapterFrameRegistry.TryGet(root.World, out IAdapterFrame? frameOfWorld), Is.True);
            Assert.That(frameOfWorld, Is.SameAs(root.PumpCounter), "the pump counter is the world's adapter frame");

            Assert.That(root.Start().Outcome, Is.EqualTo(Outcome.Published));
            Assert.That(root.State, Is.EqualTo(GameApplicationState.Running));

            // Three host frames through the sanctioned pump: three counted pumps and no violation.
            PumpFrame();
            PumpFrame();
            PumpFrame();
            Assert.That(root.PumpCounter.SanctionedPumps, Is.EqualTo(3), root.PumpCounter.ToString());
            Assert.That(root.PumpCounter.Violations, Is.EqualTo(0), root.PumpCounter.LastViolation);

            Assert.That(root.Pause().Outcome, Is.EqualTo(Outcome.Published));
            Assert.That(root.Host.Lifecycle, Is.EqualTo(WorldLifecycleState.Paused));
            Assert.That(root.Resume().Outcome, Is.EqualTo(Outcome.Published));
            Assert.That(root.Host.Lifecycle, Is.EqualTo(WorldLifecycleState.Running));

            WorldId world = root.World;
            OperationResult stopped = root.Stop("smoke test");
            Assert.That(stopped.Outcome, Is.EqualTo(Outcome.Published), stopped.Code.ToString());
            Assert.That(root.State, Is.EqualTo(GameApplicationState.Stopped));
            Assert.That(root.Host.Lifecycle, Is.EqualTo(WorldLifecycleState.Disposed));
            Assert.That(AdapterFrameRegistry.TryGet(world, out IAdapterFrame? _), Is.False, "Stop unregisters the frame");
            Assert.That(GameApplication.Current, Is.Null);
        }

        [Test]
        public void PumpCounter_CountsAPumpThatBypassesTheApplicationPump()
        {
            GameApplicationRoot root = Boot();
            root.Start();
            PumpFrame();

            // A direct host pump is the second pump path SADR-010 forbids; the counter charges it at the next frame.
            root.Host.PumpFrame(0UL);
            PumpFrame();
            Assert.That(root.PumpCounter.BypassPumps, Is.EqualTo(1), root.PumpCounter.ToString());

            // The same host frame pumped twice is the other violation.
            frame--;
            PumpFrame();
            Assert.That(root.PumpCounter.DuplicateFramePumps, Is.EqualTo(1), root.PumpCounter.ToString());
            Assert.That(root.PumpCounter.Violations, Is.EqualTo(2));
        }

        [Test]
        public void Boot_WithACorruptedCatalog_FailsWithANamedCode_AndCreatesNoWorld()
        {
            int worldsBefore = UnityWorldRegistry.Count;
            GameApplicationDefinition definition = AppProbe.Definition()
                .WithCatalog(AppProbe.Definition().Build().Catalog, ContentHash.Compute(Encoding.UTF8.GetBytes("not the published catalog")))
                .Build();

            LogAssert.Expect(LogType.Error, new Regex("GameApplicationBootFailed\\{code=CatalogFingerprintMismatch"));
            GameApplicationBootException failure = Assert.Throws<GameApplicationBootException>(
                () => GameApplication.Boot(definition, Options()));

            Assert.That(failure.Failure.Code, Is.EqualTo(GameApplicationBootCode.CatalogFingerprintMismatch));
            Assert.That(failure.Failure.Diagnostic, Is.EqualTo(DiagnosticCode.UnsupportedVersion));
            Assert.That(failure.Failure.Stage, Is.EqualTo("catalog"));
            Assert.That(GameApplication.LastFailure, Is.SameAs(failure.Failure));
            Assert.That(GameApplication.Current, Is.Null);
            Assert.That(UnityWorldRegistry.Count, Is.EqualTo(worldsBefore), "no infrastructure-only world in its place");
        }

        [Test]
        public void Boot_WithoutACatalogHash_FailsInsteadOfCreatingAnEmptyHashWorld()
        {
            GameApplicationDefinition definition = AppProbe.Definition()
                .WithCatalog(AppProbe.Definition().Build().Catalog, ContentHash.Empty)
                .Build();

            LogAssert.Expect(LogType.Error, new Regex("GameApplicationBootFailed\\{code=CatalogHashMissing"));
            Assert.That(GameApplication.TryBoot(definition, Options(), out GameApplicationRoot? root, out GameApplicationBootFailed? failure), Is.False);
            Assert.That(root, Is.Null);
            Assert.That(failure, Is.Not.Null);
            Assert.That(failure!.Code, Is.EqualTo(GameApplicationBootCode.CatalogHashMissing));
        }

        [Test]
        public void Boot_WithARefusedBootEdit_DisposesTheHalfComposedWorld()
        {
            int worldsBefore = UnityWorldRegistry.Count;

            // The rival mount conflicts with plugin A over the same Exclusive capability: the world preflight refuses
            // it on the lane, so the boot script fails and the root does not exist half-composed.
            GameApplicationDefinition definition = AppProbe.Definition()
                .AddBootStep(GameApplicationBootStep.Apply("mount-rival", AppProbe.RivalMount()))
                .Build();

            LogAssert.Expect(LogType.Error, new Regex("GameApplicationBootFailed\\{code=BootEditRefused"));
            Assert.That(GameApplication.TryBoot(definition, Options(), out GameApplicationRoot? root, out GameApplicationBootFailed? failure), Is.False);
            Assert.That(root, Is.Null);
            Assert.That(failure!.Code, Is.EqualTo(GameApplicationBootCode.BootEditRefused));
            Assert.That(failure.Stage, Is.EqualTo("boot-step:mount-rival"));
            Assert.That(failure.Detail, Does.Contain("WorldPreflight"));
            Assert.That(UnityWorldRegistry.Count, Is.EqualTo(worldsBefore));
        }

        [Test]
        public void Bootstrap_HardFailurePolicy_LogsANamedFailure_AndCreatesNoFallbackWorld()
        {
            GameCoreBootstrapFailurePolicy policy = GameCoreApplicationBootstrap.FailurePolicy;
            ContentHash hash = GameCoreApplicationBootstrap.CatalogHash;
            Func<GameCoreApplicationCompositionRoot>? factory = GameCoreApplicationComposition.RootFactory;
            Action<UnityWorldHost>? hook = GameCoreApplicationBootstrap.WorldCreated;
            World? defaultWorld = World.DefaultGameObjectInjectionWorld;
            int fallbacks = GameCoreApplicationBootstrap.FallbackCount;
            int worldsBefore = UnityWorldRegistry.Count;
            try
            {
                GameCoreApplicationBootstrap.FailurePolicy = GameCoreBootstrapFailurePolicy.HardFailure;
                GameCoreApplicationBootstrap.CatalogHash = ContentHash.Empty;
                GameCoreApplicationBootstrap.WorldCreated = null;
                GameCoreApplicationComposition.RootFactory = null;

                LogAssert.Expect(LogType.Error, new Regex("application boot failed \\(MissingDependency\\)"));
                bool suppressed = new GameCoreApplicationBootstrap().Initialize("hard-failure test");

                Assert.That(suppressed, Is.True, "Unity's default world is suppressed even when the boot fails");
                Assert.That(GameCoreApplicationBootstrap.LastBootFailed, Is.True);
                Assert.That(GameCoreApplicationBootstrap.LastCode, Is.EqualTo(DiagnosticCode.MissingDependency));
                Assert.That(GameCoreApplicationBootstrap.FallbackCount, Is.EqualTo(fallbacks), "no fallback under HardFailure");
                Assert.That(UnityWorldRegistry.Count, Is.EqualTo(worldsBefore), "no infrastructure-only world was created");
                Assert.That(World.DefaultGameObjectInjectionWorld, Is.Null);
            }
            finally
            {
                GameCoreApplicationBootstrap.FailurePolicy = policy;
                GameCoreApplicationBootstrap.CatalogHash = hash;
                GameCoreApplicationBootstrap.WorldCreated = hook;
                GameCoreApplicationComposition.RootFactory = factory;
                World.DefaultGameObjectInjectionWorld = defaultWorld;
                GameCorePlayerLoopInstaller.Remove();
            }
        }

        // ------------------------------------------------------------------ SADR-011

        [Test]
        public void Submit_WithAStaleExpectedRevision_IsAStalePlanRefusal_WithNoPublication()
        {
            GameApplicationRoot root = Boot();
            CompositionRevision published = root.Lane.Committed.Revision;
            AssemblyEpoch epoch = root.Host.CurrentEpoch;
            int publications = root.Lane.PublicationCount;
            var stale = new CompositionRevision(published.Value - 1UL);

            WorldAdmissionReport report = root.Submit(AppProbe.Reconfigure(root.Lane, 1500, 2UL), stale);

            Assert.That(report.Outcome, Is.EqualTo(BridgeOutcome.AdmissionRejected));
            Assert.That(report.Refusal, Is.Not.Null);
            Assert.That(report.Refusal!.Code, Is.EqualTo(DiagnosticCode.StalePlan), report.Refusal.ToString());
            Assert.That(report.Refusal.Phase, Is.EqualTo(BridgeRefusalPhase.Admission));
            Assert.That(report.Refusal.KeptOneSeries, Is.True);
            Assert.That(root.Lane.Committed.Revision, Is.EqualTo(published), "no composition publication");
            Assert.That(root.Lane.PublicationCount, Is.EqualTo(publications));
            Assert.That(root.Host.CurrentEpoch, Is.EqualTo(epoch), "no assembly publication");
            Assert.That(root.Bridge.StaleExpectationCount, Is.EqualTo(1));
        }

        [Test]
        public void Submit_ARefusedWorldPlan_LeavesTheLaneUnchanged_AndTheWorldEditableAndCheckpointable()
        {
            GameApplicationRoot root = Boot();
            root.Start();
            CompositionRevision published = root.Lane.Committed.Revision;
            AssemblyEpoch epoch = root.Host.CurrentEpoch;

            WorldAdmissionReport refused = root.Submit(AppProbe.RivalMount());

            Assert.That(refused.Outcome, Is.EqualTo(BridgeOutcome.AdmissionRejected));
            Assert.That(refused.Refusal, Is.Not.Null);
            Assert.That(refused.Refusal!.Phase, Is.EqualTo(BridgeRefusalPhase.WorldPreflight), refused.Refusal.ToString());
            Assert.That(refused.Refusal.Code, Is.Not.EqualTo(DiagnosticCode.None));
            Assert.That(refused.Refusal.Witness, Is.Not.Empty, "the refusal carries the world's own witness");
            Assert.That(root.Preflight.Refusals, Is.EqualTo(1));
            Assert.That(root.Lane.Committed.Revision, Is.EqualTo(published), "the lane did not publish the refused edit");
            Assert.That(root.Lane.FindInstall(AppProbe.RivalInstance), Is.Null);
            Assert.That(root.Host.CurrentEpoch, Is.EqualTo(epoch));
            Assert.That(
                AssemblyPublisher.MatchesPublishedAssembly(
                    root.Lane.Committed.Revision, root.Lane.Committed.Epoch, root.Publisher.PublishedRevision, root.Host.CurrentEpoch),
                Is.True,
                "lane and world still publish one series (F5 closed)");

            // Still editable: the next edit, prepared against the same published revision, publishes on both halves.
            WorldAdmissionReport accepted = root.Submit(AppProbe.Reconfigure(root.Lane, 1200, 2UL));
            Assert.That(accepted.Outcome, Is.EqualTo(BridgeOutcome.Executed), accepted.Refusal?.ToString() ?? accepted.RefusalDetail);
            Assert.That(root.Lane.Committed.Revision.Value, Is.EqualTo(published.Value + 1UL));
            Assert.That(root.Host.CurrentEpoch.Value, Is.EqualTo(epoch.Value + 1UL));

            // Still checkpointable: one committed step, then the boundary reader reads the committed boundary.
            root.Host.NotifyCommandAdmitted(1U);
            PumpFrame();
            UnityCommittedBoundaryReaderAssert.ReadsBoundary(root);
        }

        [Test]
        public void Submit_ACompositionEdit_DoesNotAdvanceTheLogicalStep()
        {
            GameApplicationRoot root = Boot();
            root.Start();
            LogicalStepId step = root.Host.CurrentStep;
            AssemblyEpoch epoch = root.Host.CurrentEpoch;
            ulong demand = root.Host.PendingDemand;

            WorldAdmissionReport report = root.Submit(AppProbe.Reconfigure(root.Lane, 1300, 2UL));
            Assert.That(report.Outcome, Is.EqualTo(BridgeOutcome.Executed), report.Refusal?.ToString() ?? report.RefusalDetail);
            Assert.That(report.CommandSubmitted, Is.False, "a composition edit is not a command (04 s3)");
            Assert.That(report.DemandAfter, Is.EqualTo(demand));
            Assert.That(report.StepAfter, Is.EqualTo(step));
            Assert.That(root.Host.CurrentEpoch.Value, Is.EqualTo(epoch.Value + 1UL), "the edit published a new epoch");

            // An idle command-driven world commits nothing on the next frame either.
            PumpFrame();
            Assert.That(root.Host.CurrentStep, Is.EqualTo(step));
        }

        // ------------------------------------------------------------------ SADR-013

        [Test]
        public void Reconfigure_ANumericConfigField_IsObservedByTheSystemInTheNextStep()
        {
            GameApplicationRoot root = Boot();
            Assert.That(root.Seeder.TryGetEntity(AppProbe.Target, out Entity target), Is.True);
            AppProbeRecorder.Target = target;
            root.Start();

            // The mount published the schema default through the configuration binding, not the manifest payload.
            CommitOneStep(root);
            Assert.That(AppProbeRecorder.Found, Is.True, "the reader system found the probe binding row");
            Assert.That(AppProbeRecorder.Value, Is.EqualTo(AppProbe.DefaultConfiguredValue));
            Assert.That(AppProbeRecorder.Value, Is.Not.EqualTo(AppProbe.ManifestValue));

            int runsBefore = AppProbeRecorder.Runs;
            LogicalStepId stepBefore = root.Host.CurrentStep;
            WorldAdmissionReport report = root.Submit(AppProbe.Reconfigure(root.Lane, 2500, 2UL));
            Assert.That(report.Outcome, Is.EqualTo(BridgeOutcome.Executed), report.Refusal?.ToString() ?? report.RefusalDetail);
            Assert.That(report.Assembly, Is.Not.Null);
            Assert.That(report.Assembly!.Outcome, Is.EqualTo(DerivedAssemblyOutcome.Published), report.Assembly.Describe());
            Assert.That(AppProbeRecorder.Runs, Is.EqualTo(runsBefore), "the edit ran no system");

            CommitOneStep(root);
            Assert.That(root.Host.CurrentStep.Value, Is.EqualTo(stepBefore.Value + 1UL));
            Assert.That(AppProbeRecorder.Value, Is.EqualTo(2500), "the next step's system sees the reconfigured value");
        }

        // ------------------------------------------------------------------ helpers

        private GameApplicationRoot Boot() => GameApplication.Boot(AppProbe.Definition().Build(), Options());

        private GameApplicationBootOptions Options() => new GameApplicationBootOptions
        {
            InstallPlayerLoop = false,
            AssignDefaultWorld = false,
            PumpAssertions = false,
            FrameClock = () => frame,
        };

        private void PumpFrame()
        {
            frame++;
            GameCoreApplicationPump.PumpFrame();
        }

        private void CommitOneStep(GameApplicationRoot root)
        {
            LogicalStepId before = root.Host.CurrentStep;
            root.Host.NotifyCommandAdmitted(1U);
            PumpFrame();
            Assert.That(root.Host.CurrentStep.Value, Is.EqualTo(before.Value + 1UL), "one command commits one step");
        }
    }

    internal static class UnityCommittedBoundaryReaderAssert
    {
        public static void ReadsBoundary(GameApplicationRoot root)
        {
            var reader = root.CreateBoundaryReader();
            Assert.That(reader.IsAtCommittedBoundary, Is.True);
            bool read = reader.TryRead(
                root.World,
                out GameCore.Execution.Persistence.CommittedBoundarySnapshot? snapshot,
                out GameCore.Execution.Persistence.BoundaryRefusal refusal,
                out DiagnosticCode code,
                out string detail);
            Assert.That(read, Is.True, refusal + " " + code + ": " + detail);
            Assert.That(snapshot, Is.Not.Null);
        }
    }
}

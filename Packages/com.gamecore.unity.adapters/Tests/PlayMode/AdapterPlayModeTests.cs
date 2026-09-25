// GameCore.Unity.Adapters.PlayMode.Tests - Play Mode tests of the GC-019 adapter frame in a real player loop.
//
// These tests drive real frames: the application PlayerLoop node pumps every registered world, and the adapter frame
// is called at the pump algorithm's two named points (04 s3). They prove that an idle command-driven world presents
// on every host frame while committing zero steps, that destroying views leaves gameplay counters untouched, and
// that a real GameObject view is applied from a committed image and destroyed at teardown.
//
// Status: NotRun (pending orchestrator build host).
#nullable enable
using System.Collections;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Unity.Adapters;
using GameCore.Unity.Adapters.Fixtures;
using GameCore.Unity.Adapters.Input;
using GameCore.Unity.Adapters.Views;
using GameCore.Unity.Fixtures;
using GameCore.Unity.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameCore.Unity.Adapters.Tests.PlayMode
{
    /// <summary>
    /// TEST-018/TEST-019 Play Mode tests. The fixture timeout is in milliseconds (the Editor has an unresolved
    /// intermittent pre-dispatch hang).
    /// </summary>
    [TestFixture]
    [Timeout(120000)]
    public sealed class AdapterPlayModeTests
    {
        private const ulong SessionSalt = 0x4743303139504D44UL;

        private static readonly Id128 Issuer = new Id128(0x495353554552504DUL, 0x303139UL);
        private static readonly Id128 DeviceSourceId = new Id128(0x444556494345504DUL, 0x0000000000000001UL);

        private static ulong sessionSequence;

        private readonly List<UnityWorldHost> ownedHosts = new List<UnityWorldHost>();
        private readonly List<GameObject> ownedObjects = new List<GameObject>();

        private bool pumpWasEnabled;

        [SetUp]
        public void SetUp()
        {
            pumpWasEnabled = GameCoreApplicationPump.IsEnabled;

            // Tests drive the frames they assert on, so the automatic route is switched off until a test enables it.
            GameCoreApplicationPump.IsEnabled = false;
            AdapterFrameRegistry.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            AdapterFrameRegistry.Reset();

            for (int i = ownedHosts.Count - 1; i >= 0; i--)
            {
                UnityWorldHost host = ownedHosts[i];
                host.Stop(new OperationId(host.World, Issuer, 99UL), "play mode test teardown");
                host.Dispose();
            }

            ownedHosts.Clear();

            for (int i = ownedObjects.Count - 1; i >= 0; i--)
            {
                if (ownedObjects[i] != null)
                {
                    UnityEngine.Object.Destroy(ownedObjects[i]);
                }
            }

            ownedObjects.Clear();

            UnityWorldRegistry.ResetAll();
            AdapterFrameRegistry.Reset();
            GameCoreApplicationPump.IsEnabled = pumpWasEnabled;
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator AnIdleCommandWorldPresentsOnEveryHostFrameWithoutAdvancingAStep()
        {
            UnityWorldHost host = CreateCommandWorld();
            var binder = new RecordingViewBinder();
            var presented = new PresentedAdapter(host, binder, Target(0x10UL), firstValue: 5, secondValue: 9);

            Assert.That(AdapterFrameRegistry.Register(presented.Frame), Is.False);
            Assert.That(binder.LiveViewCount, Is.EqualTo(1), "the view exists before the next committed image is presented");

            // The application route is the only update path; make sure this session has exactly one route, so the
            // real frames below are the real pump's frames (04 s3).
            Assert.That(GameCorePlayerLoopInstaller.EnsureInstalled(), Is.EqualTo(1));

            GameCoreApplicationPump.IsEnabled = true;

            LogicalStepId stepBefore = host.CurrentStep;
            int framesBefore = GameCoreApplicationPump.FrameCount;

            for (int frameIndex = 0; frameIndex < 10; frameIndex++)
            {
                yield return null;
            }

            Assert.That(host.CurrentStep, Is.EqualTo(stepBefore), "a framework-presented frame commits no step (P-036)");
            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.Zero));
            Assert.That(host.Driver.CommittedStepCount, Is.EqualTo(0));
            Assert.That(GameCoreApplicationPump.FrameCount, Is.GreaterThan(framesBefore), "the application route was live");
            Assert.That(host.PumpCount, Is.GreaterThan(0), "the pump routed host frames to this world");
            Assert.That(presented.Frame.PresentPassCount, Is.EqualTo(host.PumpCount),
                "the input point and the presentation point run exactly once per routed host frame");
            Assert.That(presented.Frame.PresentPassCount, Is.GreaterThan(0), "an idle world still presents every host frame");
            Assert.That(AdapterFrameRegistry.InputCalls, Is.EqualTo(AdapterFrameRegistry.PresentationCalls));
            Assert.That(AdapterFrameRegistry.InputCalls, Is.GreaterThan(0));
            Assert.That(presented.Frame.UnboundSampleCount, Is.EqualTo(0), "the idle device source queues no sample");

            // Presentation followed the committed image once; presenting the same image again is refused, not
            // repeated, so no frame count can become a second authoritative update (P-036, P-045).
            Assert.That(presented.Source.RefreshCount, Is.EqualTo(2), "presentation only reads the committed image");
            Assert.That(presented.Registry.ApplyCount, Is.EqualTo(1));
            Assert.That(binder.ApplyCount, Is.EqualTo(1));
            Assert.That(presented.Registry.StaleApplyRefusalCount, Is.GreaterThan(0));
            Assert.That(presented.Registry.LiveViewCount, Is.EqualTo(1));
            Assert.That(binder.Applies[0].Token, Is.EqualTo(presented.PresentedToken));
            Assert.That(binder.Applies[0].Fields[0].Value, Is.EqualTo(9));

            // One explicit host frame: exactly one more presentation pass and still no committed step.
            int passesBefore = presented.Frame.PresentPassCount;
            LogicalStepId stepAtExplicitPump = host.CurrentStep;
            GameCoreApplicationPump.PumpFrame();

            Assert.That(presented.Frame.PresentPassCount, Is.EqualTo(passesBefore + 1), "one host frame is one presentation pass");
            Assert.That(host.CurrentStep, Is.EqualTo(stepAtExplicitPump));
            Assert.That(host.Driver.CommittedStepCount, Is.EqualTo(0));
            Assert.That(presented.Registry.ApplyCount, Is.EqualTo(1));

            GameCoreApplicationPump.IsEnabled = false;
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator DestroyingTheViewsLeavesGameplayCountersExactlyWhereTheyWere()
        {
            UnityWorldHost host = CreateCommandWorld();
            var binder = new RecordingViewBinder();
            var presented = new PresentedAdapter(host, binder, Target(0x20UL), firstValue: 1, secondValue: 2);

            Assert.That(AdapterFrameRegistry.Register(presented.Frame), Is.False);

            // One route, so the frames below are the application pump's own frames (04 s3).
            Assert.That(GameCorePlayerLoopInstaller.EnsureInstalled(), Is.EqualTo(1));
            GameCoreApplicationPump.IsEnabled = true;

            for (int frameIndex = 0; frameIndex < 3; frameIndex++)
            {
                yield return null;
            }

            Assert.That(presented.Registry.LiveViewCount, Is.EqualTo(1));
            Assert.That(binder.ApplyCount, Is.EqualTo(1));

            LogicalStepId stepBefore = host.CurrentStep;
            AssemblyEpoch epochBefore = host.CurrentEpoch;
            int publicationsBefore = host.Publications.PublishedCount;
            int pumpCountBefore = host.PumpCount;
            ulong demandBefore = host.PendingDemand;

            // A real command through the world's own port: this registration declares no plane, so the refusal is a
            // value and creates no demand (O-13, P-042).
            CommandAdmissionReceipt receipt = host.Submit(new CommandEnvelope(
                new OperationId(host.World, Issuer, 41UL),
                new RouteId(Key(0x41UL)),
                Target(0x20UL),
                new SchemaRef(new SchemaId(Key(0x42UL)), 1U),
                null,
                new FrozenPayload(new[] { (byte)1 })));

            Assert.That(receipt.Admitted, Is.False);
            Assert.That(receipt.Result.Kind, Is.EqualTo(RequestResultKind.Rejected));
            Assert.That(receipt.Result.Reason, Is.EqualTo(DiagnosticCode.MissingDependency));
            Assert.That(host.PendingDemand, Is.EqualTo(demandBefore));

            // Destroying the views destroys adapter objects only: no step, epoch or publication moves (P-024, P-034).
            int destroyed = presented.Presenter.DestroyAllViews();

            Assert.That(destroyed, Is.EqualTo(1));
            Assert.That(presented.Registry.LiveViewCount, Is.EqualTo(0));
            Assert.That(binder.LiveViewCount, Is.EqualTo(0));
            Assert.That(binder.LiveHandles().Count, Is.EqualTo(0));

            for (int frameIndex = 0; frameIndex < 5; frameIndex++)
            {
                yield return null;
            }

            Assert.That(host.CurrentStep, Is.EqualTo(stepBefore), "destroying views never advances gameplay");
            Assert.That(host.CurrentEpoch, Is.EqualTo(epochBefore));
            Assert.That(host.Publications.PublishedCount, Is.EqualTo(publicationsBefore));
            Assert.That(host.PendingDemand, Is.EqualTo(demandBefore));
            Assert.That(host.PumpCount, Is.GreaterThan(pumpCountBefore), "the world kept being pumped across the destruction");
            Assert.That(host.EntityWorld.IsCreated, Is.True, "gameplay storage is untouched by view destruction");
            Assert.That(presented.Registry.LiveViewCount, Is.EqualTo(0), "nothing recreates a view implicitly (P-024)");
            Assert.That(presented.Frame.PresentPassCount, Is.GreaterThan(0), "presentation keeps running with no view (TEST-018)");

            GameCoreApplicationPump.IsEnabled = false;
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator AGameObjectViewIsAppliedFromACommittedImageAndDestroyedAtTeardown()
        {
            UnityWorldHost host = CreateCommandWorld();
            var container = new GameObject("gc019-playmode-container");
            ownedObjects.Add(container);

            var binder = new GameObjectViewBinder(container.transform);
            var presented = new PresentedAdapter(host, binder, Target(0x30UL), firstValue: 5, secondValue: 9);

            Assert.That(AdapterFrameRegistry.Register(presented.Frame), Is.False);
            Assert.That(binder.LiveViewCount, Is.EqualTo(1));

            // One route, so the frames below are the application pump's own frames (04 s3).
            Assert.That(GameCorePlayerLoopInstaller.EnsureInstalled(), Is.EqualTo(1));

            GameCoreApplicationPump.IsEnabled = true;

            for (int frameIndex = 0; frameIndex < 4; frameIndex++)
            {
                yield return null;
            }

            Assert.That(Application.isPlaying, Is.True, "this assembly runs in a real player loop");
            Assert.That(binder.LiveViewCount, Is.EqualTo(1));
            Assert.That(binder.LiveObjects().Count, Is.EqualTo(1));
            Assert.That(binder.ApplyCount, Is.EqualTo(1), "the committed image is applied once, not once per frame");

            GameObject view = binder.LiveObjects()[0];
            Assert.That(view.name, Does.Contain(Target(0x30UL).ToString()));
            Assert.That(view.name, Does.Contain("-s0-e2"));
            Assert.That(view.transform.localScale.x, Is.EqualTo(9f), "a presentation field scales the view");
            Assert.That(view.transform.parent, Is.EqualTo(container.transform));

            // The frame's own teardown retires adapters only: one view, no leases, no gameplay (P-024, P-048, TEST-015).
            AdapterTeardownReport teardown = presented.Frame.Retire();

            Assert.That(teardown.ViewsDestroyed, Is.EqualTo(1));
            Assert.That(teardown.Assets, Is.Null, "this frame owns no asset table, so it retires no lease");
            Assert.That(teardown.Detail, Is.Not.Empty);
            Assert.That(binder.LiveViewCount, Is.EqualTo(0));
            Assert.That(binder.LiveObjects().Count, Is.EqualTo(0));
            Assert.That(presented.Registry.LiveViewCount, Is.EqualTo(0));
            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.Zero));
            Assert.That(host.EntityWorld.IsCreated, Is.True, "gameplay storage survives adapter teardown");

            yield return null;

            Assert.That(container != null, Is.True, "the container belongs to the application, not to the binder");
            Assert.That(container.transform.childCount, Is.EqualTo(0), "the destroyed view object has left the container");

            GameCoreApplicationPump.IsEnabled = false;
        }

        [Test]
        [Timeout(60000)]
        public void TheSessionResetPathLeavesNoAdapterFrameBehind()
        {
            UnityWorldHost host = CreateCommandWorld();
            var presented = new PresentedAdapter(host, new RecordingViewBinder(), Target(0x40UL), firstValue: 1, secondValue: 2);

            Assert.That(AdapterFrameRegistry.Register(presented.Frame), Is.False);
            Assert.That(AdapterFrameRegistry.Count, Is.EqualTo(1));
            Assert.That(AdapterFrameRegistry.CollectInput(host.World).Outcome, Is.EqualTo(AdapterFrameOutcome.Completed));
            Assert.That(AdapterFrameRegistry.InputCalls, Is.EqualTo(1));

            AdapterFrameRegistry.Reset();

            Assert.That(AdapterFrameRegistry.Count, Is.EqualTo(0));
            Assert.That(AdapterFrameRegistry.InputCalls, Is.EqualTo(0));
            Assert.That(AdapterFrameRegistry.PresentationCalls, Is.EqualTo(0));
            Assert.That(AdapterFrameRegistry.SkippedCalls, Is.EqualTo(0));
            Assert.That(AdapterFrameRegistry.FaultedCalls, Is.EqualTo(0));
            Assert.That(AdapterFrameRegistry.LastInputReport.Outcome, Is.EqualTo(AdapterFrameOutcome.Skipped));
            Assert.That(AdapterFrameRegistry.LastPresentationReport.Outcome, Is.EqualTo(AdapterFrameOutcome.Skipped));

            Assert.That(AdapterFrameRegistry.Register(presented.Frame), Is.False);
            Assert.That(AdapterFrameRegistry.Count, Is.EqualTo(1));

            // The production reset the [RuntimeInitializeOnLoadMethod] attribute invokes, run directly (TEST-018).
            GameCoreApplicationPump.IsEnabled = false;
            GameCoreApplicationReset.RunReset();

            Assert.That(AdapterFrameRegistry.Count, Is.EqualTo(0),
                "a new session cannot inherit the previous session's adapter frame (04 s9)");
            Assert.That(AdapterFrameRegistry.InputCalls, Is.EqualTo(0));
            Assert.That(UnityWorldRegistry.Count, Is.EqualTo(0));
            Assert.That(host.Lifecycle, Is.EqualTo(WorldLifecycleState.Disposed));

            // Leave the session with exactly one route so later frames can pump again.
            Assert.That(GameCorePlayerLoopInstaller.EnsureInstalled(), Is.EqualTo(1));
            GameCoreApplicationPump.IsEnabled = false;
        }

        // ---------------------------------------------------------------- helpers

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

        private static Id128 Key(ulong ordinal) => new Id128(0x47433031394B4559UL, ordinal);

        private static TargetId Target(ulong ordinal) => new TargetId(Key(ordinal));

        private static ScopeId Scope(ulong ordinal) => new ScopeId(Key(ordinal));

        private static CapabilityId Capability(ulong ordinal) => new CapabilityId(Key(ordinal));

        /// <summary>
        /// One test world's adapter frame plus the committed images it presents from. The view is created from the
        /// world's initial committed image and the frame is then pointed at the next committed assembly, which keeps
        /// the same logical step and moves the assembly epoch (P-006): that is exactly the publication an idle world
        /// presents from.
        /// </summary>
        private sealed class PresentedAdapter
        {
            public PresentedAdapter(UnityWorldHost host, IViewBinder binder, TargetId target, int firstValue, int secondValue)
            {
                Host = host;
                Target = target;
                Binder = binder;
                Registry = new ViewRegistry(host.World, 8U);
                Source = new CommittedImageSource(host.World);
                Presenter = new CommittedOutputPresenter(Registry, Source, binder);
                CreatedToken = new SnapshotToken(host.World, host.CurrentEpoch, host.CurrentStep);
                PresentedToken = new SnapshotToken(
                    host.World, new AssemblyEpoch(host.CurrentEpoch.Value + 1UL), host.CurrentStep);

                Assert.That(Source.Refresh(Image(CreatedToken, target, firstValue)), Is.True);
                Assert.That(Presenter.CreateView(target, 0U, out ViewRecord? createdRecord), Is.EqualTo(ViewCreateOutcome.Created));
                Assert.That(createdRecord, Is.Not.Null);
                Assert.That(Source.Refresh(Image(PresentedToken, target, secondValue)), Is.True);

                Frame = new WorldAdapterFrame(
                    host,
                    new TypedInputIngress(host.World, host),
                    new InputBindingTable(),
                    new QueuedDeviceInputSource(DeviceSourceId),
                    null,
                    null,
                    Presenter);
            }

            public UnityWorldHost Host { get; }

            public TargetId Target { get; }

            public IViewBinder Binder { get; }

            public ViewRegistry Registry { get; }

            public CommittedImageSource Source { get; }

            public CommittedOutputPresenter Presenter { get; }

            public WorldAdapterFrame Frame { get; }

            public SnapshotToken CreatedToken { get; }

            public SnapshotToken PresentedToken { get; }

            private static CommittedAssemblyImage Image(SnapshotToken token, TargetId target, int value)
            {
                var fields = new List<PresentationField>
                {
                    new PresentationField(Capability(0x70UL), 0U, value),
                };

                var entries = new List<CommittedTargetEntry>
                {
                    new CommittedTargetEntry(target, Scope(0x60UL), default(DefinitionRef), fields),
                };

                return new CommittedAssemblyImage(token, entries);
            }
        }
    }
}

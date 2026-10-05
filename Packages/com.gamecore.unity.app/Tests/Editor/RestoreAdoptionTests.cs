#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Derivation;
using GameCore.Unity.Adapters;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Fixtures;
using NUnit.Framework;

namespace GameCore.Unity.App.Tests
{
    [TestFixture]
    public sealed class RestoreAdoptionTests
    {
        private readonly List<GameApplicationRoot> roots = new List<GameApplicationRoot>();
        private string directory = string.Empty;

        [SetUp]
        public void SetUp()
        {
            Assert.That(GameApplication.Current, Is.Null, "the fixture requires an unbooted application");
            GameCoreThreading.CaptureMainThread();
            lifecycle = null;
            directory = Path.Combine(UnityEngine.Application.temporaryCachePath, "app-1-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            lifecycle = null;
            foreach (GameApplicationRoot root in roots) root.Stop("APP-1 teardown");
            roots.Clear();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void APP1_RestoreAdoptsCurrentAndPreservesRunState(bool paused)
        {
            GameApplicationRoot original = Boot();
            SaveService saves = Saves(original);
            AssertSuccess(saves.Capture("checkpoint"));
            if (paused) original.Pause();
            int boots = GameApplication.BootCount;
            for (int i = 0; i < 2; i++)
            {
                GameApplicationRoot previous = saves.ActiveRoot;
                AssertSuccess(saves.Restore("checkpoint"));
                Assert.That(GameApplication.Current, Is.SameAs(saves.ActiveRoot));
                Assert.That(previous.State, Is.EqualTo(GameApplicationState.Stopped));
                Assert.That(saves.ActiveRoot.State, Is.EqualTo(paused ? GameApplicationState.Paused : GameApplicationState.Running));
            }
            Assert.That(GameApplication.BootCount, Is.EqualTo(boots), "adoption is not a new bootstrap");
            saves.ActiveRoot.Stop("ordinary stop still clears Current");
            Assert.That(GameApplication.Current, Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void APP1_NonCurrentRestoreLeavesCurrentUntouched(bool noCurrent)
        {
            GameApplicationRoot current = Boot();
            GameApplicationDefinition definition = Definition();
            Assert.That(GameApplication.TryPrepare(definition, out var manifests, out var schedule, out var failure), Is.True);
            var session = GameCoreApplicationComposition.ReserveSessionId();
            var request = new WorldCreateRequest(session, definition.WorldDefinition, definition.TemporalModel,
                definition.Propagation, definition.CatalogHash, new OperationId(session, definition.Issuer, 1UL), definition.FixedStep);
            Assert.That(UnityWorldRegistry.TryCreate(request, GameApplication.CreateRegistration(definition, schedule!),
                out var host, out _), Is.True);
            GameApplicationRoot secondary = GameApplicationRoot.TryCompose(definition, host!, manifests!, schedule!, Options(), 1UL, out failure)!;
            Assert.That(secondary, Is.Not.Null, failure?.ToString());
            secondary.Start();
            SaveService saves = Saves(secondary);
            AssertSuccess(saves.Capture("checkpoint"));
            if (noCurrent) current.Stop("no registered application");
            AssertSuccess(saves.Restore("checkpoint"));
            Assert.That(GameApplication.Current, Is.SameAs(noCurrent ? null : current));
            Assert.That(saves.ActiveRoot, Is.Not.SameAs(secondary));
        }

        [Test]
        public void APP1_CurrentNeverNullDuringStartStopOrRootChanged()
        {
            GameApplicationRoot original = Boot();
            SaveService saves = Saves(original);
            AssertSuccess(saves.Capture("checkpoint"));
            var samples = new List<GameApplicationRoot?>();
            var stoppedCurrents = new List<GameApplicationRoot?>();
            var stoppedActives = new List<GameApplicationRoot>();
            int starts = 0;
            // The adapter factory subscribes before the staging root starts; callbacks record values because
            // the observation hub intentionally contains exceptions raised by lifecycle subscribers.
            lifecycle = change =>
            {
                samples.Add(GameApplication.Current);
                if (change.Current == WorldLifecycleState.Running) starts++;
                if (change.World.Equals(original.World))
                {
                    stoppedCurrents.Add(GameApplication.Current);
                    stoppedActives.Add(saves.ActiveRoot);
                }
            };
            saves.RootChanged += (oldRoot, newRoot) => samples.Add(GameApplication.Current);
            AssertSuccess(saves.Restore("checkpoint"));
            lifecycle = null;
            Assert.That(starts, Is.GreaterThan(0));
            Assert.That(stoppedCurrents.Count, Is.GreaterThan(0));
            Assert.That(samples, Has.None.Null);
            Assert.That(stoppedCurrents, Has.All.SameAs(saves.ActiveRoot));
            Assert.That(stoppedActives, Has.All.SameAs(saves.ActiveRoot));
            Assert.That(GameApplication.Current, Is.SameAs(saves.ActiveRoot));
        }

        [Test]
        public void APP1_RefusedRestoreAndScratchRoundTripKeepCurrent()
        {
            GameApplicationRoot original = Boot();
            SaveService saves = Saves(original);
            Assert.That(saves.Restore("missing").Succeeded, Is.False);
            Assert.That(GameApplication.Current, Is.SameAs(original));
            Assert.That(saves.TestRoundTrip().Equal, Is.True);
            Assert.That(GameApplication.Current, Is.SameAs(original));
            Assert.That(saves.ActiveRoot, Is.SameAs(original));
            Assert.That(original.State, Is.EqualTo(GameApplicationState.Running));
        }

        private Action<WorldLifecycleChange>? lifecycle;

        private GameApplicationDefinition Definition()
        {
            ICatalog catalog = W1GateCatalog.Build().Catalog!;
            var stage = new StageSpec(new StageId(StableNameKeyDerivation.Derive("app1.stage")), 1U,
                StableNameKeyDerivation.Derive("app1.package"), HostAffinity.ManagedMain,
                null, null, new AccessSet(null), null, null, null, null, null, null);
            var manifest = new PluginManifest(new PluginTypeId(StableNameKeyDerivation.Derive("app1.plugin")),
                "1.0.0", ContentHash.Empty, new SupportedProtocolRange(1, 0, 0), null,
                W1GateKeys.CatalogSchema, W1GateCatalog.PluginFactoryKey, null, null, null, null, null, null,
                new[] { stage }, null, null);
            return new GameApplicationDefinition.Builder("APP-1")
                .WithCatalog(catalog, catalog.Fingerprint)
                .WithValues(EmptyDerivationValueSource.Instance)
                .AddPlugin(new CatalogPluginDeclaration(manifest, null))
                .WithWorld(new WorldDefinitionId(StableNameKeyDerivation.Derive("app1.world")), TemporalModel.CommandDriven)
                .WithRootScope(new ScopeId(StableNameKeyDerivation.Derive("app1.root")))
                .WithIssuer(StableNameKeyDerivation.Derive("app1.issuer"))
                .WithAdapterFrame(root =>
                {
                    if (!roots.Contains(root)) roots.Add(root);
                    root.Observations.Add(new LifecycleObserver(change => lifecycle?.Invoke(change)));
                    return null;
                }).Build();
        }

        private GameApplicationRoot Boot() => GameApplication.Boot(Definition(), Options());

        private static GameApplicationBootOptions Options() => new GameApplicationBootOptions
        {
            InstallPlayerLoop = false, AssignDefaultWorld = false, StartImmediately = true, PumpAssertions = false,
        };

        private SaveService Saves(GameApplicationRoot root) => new SaveService(root,
            new SaveServiceOptions("app1", AdoptionTestCodecs.Create()) { Directory = directory, RestoreBootOptions = Options() });

        private static void AssertSuccess(SaveResult result) => Assert.That(result.Succeeded, Is.True, result.Refusal?.ToString());

        private sealed class LifecycleObserver : IWorldLifecycleObserver
        {
            private readonly Action<WorldLifecycleChange> observe;
            public LifecycleObserver(Action<WorldLifecycleChange> observe) => this.observe = observe;
            public void OnWorldLifecycleChanged(WorldLifecycleChange change) => observe(change);
            public void OnStepCommitted(StepCommitEvent committed) { }
        }
    }
}

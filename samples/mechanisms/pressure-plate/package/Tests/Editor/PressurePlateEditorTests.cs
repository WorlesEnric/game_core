#nullable enable
// Hollowmere.Mechanism.PressurePlate.Editor.Tests - the mechanism's EditMode suite (W-MECH-01 sample).
//
// Catalog: the generated catalog builds and its fingerprint is the generated constant. Catalog set: Combine vectors and
// the composite catalog. Extend: the extended definition carries exactly the plate's contributions. Smoke: two sequential
// smoke sessions pumped through the one sanctioned pump (PlayerLoop node off) end in the same slot hash. Tool: the
// mechanism.pressurePlate.add operation round-trips through Undo and refuses bad arguments. Nothing here writes a file.
using System;
using System.Collections.Generic;
using System.Text;
using GameCore.Contracts;
using GameCore.Gameplay.World;
using GameCore.Unity.Adapters;
using GameCore.Unity.App;
using Hollowmere.Mechanism.PressurePlate.Editor;
using Hollowmere.Mechanism.PressurePlate.Generated;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hollowmere.Mechanism.PressurePlate.Editor.Tests
{
    /// <summary>A one-key test catalog standing in for a world catalog.</summary>
    internal sealed class FixedCatalog : ICatalog
    {
        private readonly FactoryRegistration registration;

        internal FixedCatalog(string seed, FactoryKey key)
        {
            Fingerprint = ContentHash.Compute(Encoding.UTF8.GetBytes(seed));
            registration = new FactoryRegistration(key, FactoryKind.PluginFactory, default(Id128), key.RegistrationKey, 1U);
        }

        public ContentHash Fingerprint { get; }

        public CatalogLookup Lookup(FactoryKey key) =>
            key.Equals(registration.Key) ? CatalogLookup.FactoryFound(registration) : CatalogLookup.MissingKey(key);

        public CatalogLookup LookupSchema(SchemaRef schema) => CatalogLookup.MissingSchema(schema);

        public IReadOnlyList<FactoryKey> FactoryKeysInCanonicalOrder() => new[] { registration.Key };
    }

    [TestFixture]
    public sealed class PressurePlateCatalogTests
    {
        [Test]
        public void GeneratedCatalog_Builds_AndItsFingerprintIsTheGeneratedConstant()
        {
            CatalogBuildResult result = PressurePlateCatalog.BuildCatalog();
            Assert.That(result.Catalog, Is.Not.Null, "the generated catalog builds");
            Assert.That(result.Catalog!.Fingerprint.ToHex(), Is.EqualTo(PressurePlateCatalog.CatalogFingerprint));
            Assert.That(PressurePlateMechanism.Catalog().Fingerprint.ToHex(), Is.EqualTo(PressurePlateCatalog.CatalogFingerprint));
        }

        [Test]
        public void GeneratedCatalog_RegistersEveryDeclaredKeyAndSchema()
        {
            ICatalog catalog = PressurePlateMechanism.Catalog();
            Assert.That(catalog.Lookup(PressurePlateDeclarations.PluginFactory).Found, Is.True, "plugin factory");
            Assert.That(catalog.Lookup(PressurePlateDeclarations.CommandSystem).Found, Is.True, "command system");
            Assert.That(catalog.Lookup(PressurePlateDeclarations.Applier).Found, Is.True, "recipe applier");
            Assert.That(catalog.Lookup(PressurePlateDeclarations.Layout).Found, Is.True, "slot layout");
            Assert.That(catalog.LookupSchema(PressurePlateDeclarations.ConfigSchema).Found, Is.True, "config schema");
            Assert.That(catalog.LookupSchema(PressurePlateDeclarations.Domain).Found, Is.True, "domain schema");
            Assert.That(PressurePlateCatalog.PressurePlatePluginKey, Is.EqualTo(PressurePlateDeclarations.PluginFactory));
            Assert.That(PressurePlateCatalog.PressurePlateCommandSystemKey, Is.EqualTo(PressurePlateDeclarations.CommandSystem));
            Assert.That(PressurePlateCatalog.PlateApplierKey, Is.EqualTo(PressurePlateDeclarations.Applier));
            Assert.That(PressurePlateCatalog.PlateLayoutKey, Is.EqualTo(PressurePlateDeclarations.Layout));
        }

        [Test]
        public void CatalogSet_Combine_MatchesTheContractVectors()
        {
            string world = new string('a', 64);
            string first = new string('b', 64);
            const string second = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
            Assert.That(CatalogSet.Combine(world, new string[0]), Is.EqualTo(world), "no mechanism: the world fingerprint unchanged");
            Assert.That(CatalogSet.Combine(world, new[] { first }),
                Is.EqualTo("2a50ec3a4ebf8a9ec70cd99c837733bc8e53c4b9315059ae144c83005a0cdbf6"));
            Assert.That(CatalogSet.Combine(world, new[] { first, second }),
                Is.EqualTo("04deca6aa80f7d2d7f300e0773482e9233b4b14b012bfa33f192b750b1393c4c"));
            Assert.That(CatalogSet.Combine(world, new[] { second, first }),
                Is.EqualTo(CatalogSet.Combine(world, new[] { first, second })), "mechanisms are sorted ordinal");
        }

        [Test]
        public void CompositeCatalog_LooksUpEveryPart_InMergedCanonicalOrder()
        {
            var worldKey = new FactoryKey(StableNameKeyDerivation.Derive("test.world.plugin"), 1U);
            var world = new FixedCatalog("test-world", worldKey);
            ICatalog plate = PressurePlateMechanism.Catalog();
            var composite = new CompositeCatalog(world, new[] { plate });

            Assert.That(composite.Fingerprint.ToHex(),
                Is.EqualTo(CatalogSet.Combine(world.Fingerprint.ToHex(), new[] { PressurePlateCatalog.CatalogFingerprint })));
            Assert.That(composite.WorldFingerprint, Is.EqualTo(world.Fingerprint));
            Assert.That(composite.Lookup(worldKey).Found, Is.True);
            Assert.That(composite.Lookup(PressurePlateDeclarations.PluginFactory).Found, Is.True);
            Assert.That(composite.LookupSchema(PressurePlateDeclarations.Domain).Found, Is.True);
            Assert.That(composite.Lookup(new FactoryKey(StableNameKeyDerivation.Derive("test.absent"), 1U)).Found, Is.False);

            IReadOnlyList<FactoryKey> keys = composite.FactoryKeysInCanonicalOrder();
            Assert.That(keys.Count, Is.EqualTo(plate.FactoryKeysInCanonicalOrder().Count + 1));
            for (int i = 1; i < keys.Count; i++)
            {
                Assert.That(keys[i - 1].RegistrationKey.CompareTo(keys[i].RegistrationKey), Is.LessThan(0), "canonical order at " + i);
            }
        }

        [Test]
        public void CompositeCatalog_RefusesAKeyRegisteredTwice()
        {
            var world = new FixedCatalog("test-world", PressurePlateDeclarations.PluginFactory);
            Assert.Throws<ArgumentException>(() => new CompositeCatalog(world, new[] { PressurePlateMechanism.Catalog() }));
        }

        [Test]
        public void Extend_AddsExactlyThePlateContributions()
        {
            var worldKey = new FactoryKey(StableNameKeyDerivation.Derive("test.world.plugin"), 1U);
            var world = new FixedCatalog("test-world", worldKey);
            var rootScope = new ScopeId(StableNameKeyDerivation.Derive("test.root-scope"));
            GameApplicationDefinition baseDefinition = new GameApplicationDefinition.Builder("ExtendProbe")
                .WithCatalog(world, world.Fingerprint)
                .WithRootScope(rootScope)
                .WithValues(new GameplayValueSource())
                .WithTargetCapacity(32)
                .WithIssuer(StableNameKeyDerivation.Derive("test.issuer"))
                .Build();

            GameApplicationDefinition extended = PressurePlateMechanism.Extend(baseDefinition);

            Assert.That(extended.Name, Is.EqualTo(baseDefinition.Name));
            Assert.That(extended.CatalogHash, Is.EqualTo(extended.Catalog.Fingerprint));
            Assert.That(extended.CatalogHash.ToHex(),
                Is.EqualTo(CatalogSet.Combine(world.Fingerprint.ToHex(), new[] { PressurePlateCatalog.CatalogFingerprint })));
            Assert.That(extended.Plugins.Count, Is.EqualTo(baseDefinition.Plugins.Count + 1));
            Assert.That(extended.Systems.Count, Is.EqualTo(baseDefinition.Systems.Count + 1));
            Assert.That(extended.BootSteps.Count, Is.EqualTo(baseDefinition.BootSteps.Count + 1));
            Assert.That(extended.BootSteps[extended.BootSteps.Count - 1].Name, Is.EqualTo(PressurePlateMechanism.MountStepName));
            Assert.That(extended.RootScope, Is.EqualTo(rootScope));
            Assert.That(extended.TargetCapacity, Is.EqualTo(32));
            Assert.That(extended.Issuer, Is.EqualTo(baseDefinition.Issuer));
            Assert.That(extended.Messages, Is.Not.Null);
            Assert.That(extended.Messages!.Routes.Count, Is.EqualTo(1));
            Assert.That(extended.Messages.Routes[0].Route, Is.EqualTo(PressurePlateDeclarations.PressRoute));
            Assert.That(extended.MessageReaders, Is.Not.Null);
            Assert.That(extended.MessageReaders!.CanRead(PressurePlateDeclarations.PressCommand), Is.True);
            Assert.That(extended.DispatchKinds.TryResolveKind(PressurePlateDeclarations.CommandSystem, out SystemDispatchKind kind), Is.True);
            Assert.That(kind, Is.EqualTo(SystemDispatchKind.ManagedSystem));
            Assert.That(extended.Recipes.Count, Is.EqualTo(baseDefinition.Recipes.Count + 1));
            Assert.Throws<InvalidOperationException>(() => PressurePlateMechanism.Extend(extended), "a plate is composed once");
        }
    }

    [TestFixture]
    public sealed class PressurePlateSmokeTests
    {
        private bool pumpWasEnabled;
        private long frame;

        [SetUp]
        public void SetUp()
        {
            pumpWasEnabled = GameCoreApplicationPump.IsEnabled;
            GameCoreApplicationPump.IsEnabled = true;
            frame = 70000L;
        }

        [TearDown]
        public void TearDown()
        {
            GameApplicationRoot? current = GameApplication.Current;
            if (current != null && current.State != GameApplicationState.Stopped)
            {
                current.Stop("test teardown");
            }

            GameCoreApplicationPump.IsEnabled = pumpWasEnabled;
        }

        [Test]
        public void TwoSequentialSessions_EndInTheSameSlotHash_AndTheScheduledState()
        {
            string first = RunSession(out string emptyHash);
            string second = RunSession(out string _);
            Assert.That(first, Is.EqualTo(second), "the smoke world is deterministic across sessions");
            Assert.That(first, Is.Not.EqualTo(emptyHash), "the presses changed the plate slots");
            Assert.That(first, Has.Length.EqualTo(64));
        }

        private string RunSession(out string initialHash)
        {
            var options = new GameApplicationBootOptions
            {
                InstallPlayerLoop = false,
                AssignDefaultWorld = false,
                PumpAssertions = false,
                StartImmediately = true,
                FrameClock = () => frame,
            };

            using (PressurePlateSmokeSession session = PressurePlateSmoke.Begin(options))
            {
                initialHash = session.SlotHash();
                for (int f = 1; f <= PressurePlateSmoke.Steps; f++)
                {
                    session.Step(f);
                    frame++;
                    GameCoreApplicationPump.PumpFrame();
                }

                PressurePlateWorld plates = session.Plates;
                plates.Poll();
                Assert.That(plates.Weight(PressurePlateSmoke.PlateA), Is.EqualTo(0));
                Assert.That(plates.IsPressed(PressurePlateSmoke.PlateA), Is.False);
                Assert.That(plates.Weight(PressurePlateSmoke.PlateB), Is.EqualTo(1));
                Assert.That(plates.IsPressed(PressurePlateSmoke.PlateB), Is.False);
                Assert.That(plates.CountEvents(PlateEventKind.Pressed), Is.EqualTo(2), "A and B pressed once each");
                Assert.That(plates.CountEvents(PlateEventKind.Released), Is.EqualTo(2), "A and B released once each");
                Assert.That(plates.CountEvents(PlateEventKind.WeightChanged), Is.EqualTo(3));
                Assert.That(plates.Module.Committed, Is.EqualTo(7));
                Assert.That(plates.Module.RefusalCount(Rules.PlateRefusals.NotLoaded), Is.EqualTo(1));
                Assert.That(plates.Module.RefusalCount(Rules.PlateRefusals.Overloaded), Is.EqualTo(1));
                Assert.That(plates.Module.Malformed, Is.EqualTo(0));
                return session.SlotHash();
            }
        }
    }

    [TestFixture]
    public sealed class PressurePlateToolTests
    {
        private Scene scene;

        [SetUp]
        public void SetUp() => scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        [TearDown]
        public void TearDown() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        [Test]
        public void Add_PlacesAPlate_WithItsDefinition_AndUndoRemovesIt()
        {
            PressurePlateDefinition definition = ScriptableObject.CreateInstance<PressurePlateDefinition>();
            try
            {
                definition.Configure(2, 3);
                Undo.IncrementCurrentGroup();
                PressurePlateAuthoring plate = PressurePlateTools.AddIn(scene, new Vector3(1f, 0f, 2f), definition, "Probe Plate");

                Assert.That(plate.gameObject.scene, Is.EqualTo(scene));
                Assert.That(plate.gameObject.name, Is.EqualTo("Probe Plate"));
                Assert.That(plate.transform.position, Is.EqualTo(new Vector3(1f, 0f, 2f)));
                Assert.That(GameCore.Gameplay.Contracts.AuthoringIds.IsValid(plate.AuthoringId), Is.True, "the plate minted an id");
                Assert.That(plate.TargetId.Value.IsDefault, Is.False);
                Assert.That(plate.Definition, Is.SameAs(definition));
                Assert.That(plate.Threshold, Is.EqualTo(2));
                Assert.That(plate.MaxWeight, Is.EqualTo(3));
                Assert.That(GameCore.Gameplay.Contracts.AuthoringIds.IsValid(definition.AuthoringId), Is.True);
                Assert.That(scene.isDirty, Is.True);

                GameObject created = plate.gameObject;
                Undo.PerformUndo();
                Assert.That(created == null, Is.True, "undo removes the placed plate");
                Assert.That(scene.rootCount, Is.EqualTo(0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void Add_WithoutADefinition_UsesTheDefaults()
        {
            PressurePlateAuthoring plate = PressurePlateTools.AddIn(scene, Vector3.zero, null, string.Empty);
            Assert.That(plate.gameObject.name, Is.EqualTo("Pressure Plate"));
            Assert.That(plate.Threshold, Is.EqualTo(PlateDefaults.Threshold));
            Assert.That(plate.MaxWeight, Is.EqualTo(PlateDefaults.MaxWeight));
        }

        [Test]
        public void Add_RefusesBadArguments_AndChangesNothing()
        {
            PressurePlateDefinition invalid = ScriptableObject.CreateInstance<PressurePlateDefinition>();
            try
            {
                invalid.Configure(3, 2);
                var refusedDefinition = Assert.Throws<ArgumentException>(
                    () => PressurePlateTools.AddIn(scene, Vector3.zero, invalid, string.Empty));
                StringAssert.StartsWith(PlateDiagnosticCodes.InvalidDefinition, refusedDefinition.Message);

                var refusedLocation = Assert.Throws<ArgumentException>(
                    () => PressurePlateTools.AddIn(scene, new Vector3(float.NaN, 0f, 0f), null, string.Empty));
                StringAssert.StartsWith(PlateDiagnosticCodes.InvalidLocation, refusedLocation.Message);

                var refusedScene = Assert.Throws<ArgumentException>(
                    () => PressurePlateTools.AddIn(default(Scene), Vector3.zero, null, string.Empty));
                StringAssert.StartsWith(PlateDiagnosticCodes.SceneNotLoaded, refusedScene.Message);

                Assert.That(scene.rootCount, Is.EqualTo(0), "a refused call creates nothing");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(invalid);
            }
        }

        [Test]
        public void Add_CarriesTheMirrorOperationAttribute()
        {
            System.Reflection.MethodInfo? add = typeof(PressurePlateTools).GetMethod(nameof(PressurePlateTools.Add));
            Assert.That(add, Is.Not.Null);
            object[] attributes = add!.GetCustomAttributes(typeof(GameCore.Gameplay.Contracts.AuthorOperationAttribute), false);
            Assert.That(attributes.Length, Is.EqualTo(1));
            Assert.That(((GameCore.Gameplay.Contracts.AuthorOperationAttribute)attributes[0]).ToolId, Is.EqualTo("mechanism.pressurePlate.add"));
            object[] authorable = typeof(PressurePlateDefinition).GetCustomAttributes(typeof(GameCore.Gameplay.Contracts.AuthorableAttribute), false);
            Assert.That(authorable.Length, Is.EqualTo(1));
            Assert.That(((GameCore.Gameplay.Contracts.AuthorableAttribute)authorable[0]).ObjectTypeId, Is.EqualTo("pressureplate.definition"));
        }
    }
}

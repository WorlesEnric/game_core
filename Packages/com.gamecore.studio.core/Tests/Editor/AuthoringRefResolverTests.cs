// GameCore.Studio.Edit.Tests - authoring refs: round trip, destroyed (StaleTarget), moved (not blocking) and changed
// (Conflict with data {expected, actual}) targets (docs/studio/03-authoring-contracts.md s1, s2, s9).
#nullable enable
using GameCore.Studio.Authoring;
using GameCore.Studio.Fixtures;
using GameCore.Studio.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.Edit.Tests
{
    public sealed class AuthoringRefResolverTests
    {
        private StudioTestBed _bed = null!;

        [SetUp]
        public void SetUp() => _bed = new StudioTestBed();

        [TearDown]
        public void TearDown() => _bed.Dispose();

        [Test]
        public void BuildAndResolve_RoundTripsAssetsAndSceneObjects()
        {
            FixtureItemDefinition lantern = _bed.CreateItem("Lantern");
            FixtureAuthoredEntity entity = _bed.SpawnEntity("Ferryman", Vector3.one);
            GameObject plain = new GameObject("Signpost");
            _bed.SaveScene();

            AuthoringRef lanternRef = _bed.Ref(lantern);
            Assert.That(lanternRef.Kind, Is.EqualTo(AuthoringKind.Definition));
            Assert.That(lanternRef.AuthoringId, Is.EqualTo(lantern.AuthoringId));
            Assert.That(lanternRef.Stamp, Is.Not.Null);

            AuthoringRef entityRef = _bed.Ref(entity);
            Assert.That(entityRef.Kind, Is.EqualTo(AuthoringKind.Entity));
            AuthoringRef plainRef = _bed.Ref(plain);
            Assert.That(plainRef.Kind, Is.EqualTo(AuthoringKind.SceneObject));

            foreach (AuthoringRef reference in new[] { lanternRef, entityRef, plainRef })
            {
                ResolveResult result = _bed.Runtime.Resolver.Resolve(reference);
                Assert.That(result.IsCurrent, Is.True, reference.ToString());
            }

            Assert.That(_bed.Runtime.Resolver.Resolve(lanternRef).Object, Is.SameAs(lantern));
            Assert.That(_bed.Runtime.Resolver.Resolve(entityRef).Object, Is.SameAs(entity));
            Assert.That(_bed.Runtime.Resolver.Resolve(plainRef).Object, Is.SameAs(plain));

            AuthoringRef json = StudioJson.Deserialize<AuthoringRef>(StudioJson.Serialize(entityRef));
            Assert.That(_bed.Runtime.Resolver.Resolve(json).Object, Is.SameAs(entity), "a ref survives its JSON form");
        }

        [Test]
        public void Destroyed_IsStaleTarget()
        {
            FixtureAuthoredEntity entity = _bed.SpawnEntity("Ferryman", Vector3.zero);
            _bed.SaveScene();
            AuthoringRef reference = _bed.Ref(entity);
            Object.DestroyImmediate(entity.gameObject);

            ResolveResult result = _bed.Runtime.Resolver.Resolve(reference);
            Assert.That(result.Object, Is.Null);
            StaleEntry? entry = result.Report.Find(StaleReason.Destroyed);
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry!.Diagnostic.Code, Is.EqualTo(DiagnosticCodes.StaleTarget));
            Assert.That(entry.Blocking, Is.True);
        }

        [Test]
        public void Renamed_ResolvesAndReportsANonBlockingMove()
        {
            FixtureItemDefinition lantern = _bed.CreateItem("Lantern");
            AuthoringRef reference = _bed.Ref(lantern);
            string? problem = AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(lantern), "BrassLantern");
            Assert.That(string.IsNullOrEmpty(problem), Is.True, problem);

            ResolveResult result = _bed.Runtime.Resolver.Resolve(reference.WithStamp(null));
            Assert.That(result.Object, Is.SameAs(lantern));
            StaleEntry? moved = result.Report.Find(StaleReason.Moved);
            Assert.That(moved, Is.Not.Null);
            Assert.That(moved!.Blocking, Is.False);
            Assert.That(result.Report.IsStale, Is.False);
        }

        [Test]
        public void StampChange_IsConflictWithExpectedAndActual()
        {
            FixtureItemDefinition lantern = _bed.CreateItem("Lantern", 2f);
            AuthoringRef reference = _bed.Ref(lantern);
            SerializedObject serialized = new SerializedObject(lantern);
            serialized.FindProperty("weight").floatValue = 9f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            ResolveResult result = _bed.Runtime.Resolver.Resolve(reference);
            Assert.That(result.Object, Is.SameAs(lantern));
            StaleEntry? changed = result.Report.Find(StaleReason.StampChanged);
            Assert.That(changed, Is.Not.Null);
            Assert.That(changed!.Diagnostic.Code, Is.EqualTo(DiagnosticCodes.Conflict));
            Assert.That((string?)changed.Diagnostic.Data?["expected"], Is.EqualTo(reference.Stamp));
            Assert.That((string?)changed.Diagnostic.Data?["actual"], Is.EqualTo(result.CurrentStamp));
            Assert.That(result.CurrentStamp, Is.Not.EqualTo(reference.Stamp));
        }
    }
}

#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GameCore.Studio.Edit.Tests
{
    public sealed class CorePickRegressionTests
    {
        private StudioTestBed _bed = null!;
        private Camera _camera = null!;
        private readonly Rect _viewport = new Rect(0, 0, 1200, 600);

        [SetUp]
        public void SetUp()
        {
            _bed = new StudioTestBed();
            _camera = new GameObject("CorePickCamera").AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = 15;
            _camera.aspect = 2;
            _camera.transform.position = new Vector3(0, 0, -30);
            _camera.nearClipPlane = .1f;
            _camera.farClipPlane = 100;
        }

        [TearDown]
        public void TearDown() => _bed.Dispose();

        private PickingService Service(IAuthoringRefResolver? resolver = null) =>
            new PickingService(_camera, _viewport, resolver ?? _bed.Runtime.Resolver, _bed.Runtime.Identity);

        [Test]
        public void R2_38_CORE_PICK_SeededMarqueeMatchesNaiveBothContainmentModes()
        {
            var random = new System.Random(4238);
            for (int i = 0; i < 80; i++)
            {
                var entity = _bed.SpawnEntity("Owner" + i, new Vector3((float)random.NextDouble() * 70 - 35,
                    (float)random.NextDouble() * 40 - 20, (float)random.NextDouble() * 80 - 40));
                entity.transform.localScale = Vector3.one * (.2f + (float)random.NextDouble() * 3);
                if (i % 3 == 0)
                {
                    var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    child.transform.SetParent(entity.transform, false);
                    child.transform.localPosition = new Vector3(4, 0, 0);
                }
                if (i % 7 == 0) entity.GetComponent<Renderer>().enabled = false;
                if (i % 11 == 0) entity.gameObject.SetActive(false);
                if (i % 13 == 0) entity.gameObject.layer = 2;
            }
            _camera.cullingMask &= ~(1 << 2);
            _bed.SaveScene();
            var service = Service();
            for (int i = 0; i < 24; i++)
            {
                var rect = new Rect((float)random.NextDouble() * 1000, (float)random.NextDouble() * 500,
                    i % 2 == 0 ? 400 : -400, i % 3 == 0 ? -250 : 250);
                foreach (bool full in new[] { false, true })
                {
                    PickResult result = service.Marquee(rect, full);
                    CollectionAssert.AreEquivalent(Naive(rect, full), result.Candidates.Select(c => c.Owner));
                    foreach (PickCandidate candidate in result.Candidates)
                        Assert.That(candidate.Ref.SameTarget(_bed.Runtime.Resolver.BuildRef(candidate.Owner!)), Is.True);
                    Assert.That(result.Candidates.Select(c => c.Distance), Is.Ordered);
                }
            }
        }

        [Test]
        public void R2_38_CORE_PICK_ReusesStampedRefsAndImmutableCandidates()
        {
            for (int i = 0; i < 20; i++) _bed.SpawnEntity("Owner" + i, new Vector3(i - 10, 0, 0));
            _bed.SaveScene();
            var resolver = new CountingResolver(_bed.Runtime.Resolver);
            var service = Service(resolver);
            PickResult first = service.Marquee(_viewport, true);
            PickResult second = service.Marquee(_viewport, false);
            Assert.That(first.Candidates.Count, Is.EqualTo(20));
            Assert.That(resolver.Builds, Is.EqualTo(20), "repeated marquee must not serialize every owner again");
            Assert.That(second.Candidates[0], Is.SameAs(first.Candidates[0]));
            service.Marquee(new Rect(0, 0, 1, 1));
            Assert.That(first.Candidates.Count, Is.EqualTo(20), "a returned result must not share the mutable query buffer");
        }

        [UnityTest]
        public IEnumerator R2_38_CORE_PICK_EditorChangesInvalidateGeometryIdentityAndStamp()
        {
            var entity = _bed.SpawnEntity("Owner", Vector3.zero);
            _bed.SaveScene();
            var service = Service();
            PickResult first = service.Marquee(_viewport, true);
            Undo.RecordObject(entity.transform, "Move picking fixture");
            entity.transform.position = Vector3.right * 2;
            EditorUtility.SetDirty(entity.transform);
            Undo.FlushUndoRecordObjects();
            yield return null;
            yield return null;
            PickResult moved = service.Marquee(_viewport, true);
            Assert.That(moved.Candidates[0].Point, Is.Not.EqualTo(first.Candidates[0].Point));
            Assert.That(moved.Candidates[0].Ref.Stamp, Is.EqualTo(_bed.Ref(entity).Stamp));
            Assert.That(moved.Candidates[0].Ref.Stamp, Is.Not.EqualTo(first.Candidates[0].Ref.Stamp));
            Undo.PerformUndo();
            yield return null;
            Assert.That(service.Marquee(_viewport, true).Candidates[0].Ref.Stamp, Is.EqualTo(first.Candidates[0].Ref.Stamp));
            var added = _bed.SpawnEntity("Added", Vector3.left);
            _bed.SaveScene();
            yield return null;
            Assert.That(service.Marquee(_viewport, true).Candidates.Count, Is.EqualTo(2));
            Object.DestroyImmediate(entity.gameObject);
            yield return null;
            Assert.That(service.Marquee(_viewport, true).Candidates.Single().Owner, Is.EqualTo(added));
        }

        [Test]
        public void R2_38_CORE_PICK_CameraViewportAndExplicitInvalidation()
        {
            var entity = _bed.SpawnEntity("Owner", Vector3.zero);
            var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
            child.transform.SetParent(entity.transform, false);
            child.transform.localPosition = Vector3.right * 80;
            _bed.SaveScene();
            var service = Service();
            Assert.That(service.Marquee(_viewport, false).Candidates.Count, Is.EqualTo(1));
            Assert.That(service.Marquee(_viewport, true).Candidates, Is.Empty, "off-frustum child prevents full containment");
            child.SetActive(false);
            service.InvalidateMarqueeCache();
            Assert.That(service.Marquee(_viewport, true).Candidates.Count, Is.EqualTo(1));
            _camera.transform.position += Vector3.right * 100;
            Assert.That(service.Marquee(_viewport).Candidates, Is.Empty);
            _camera.transform.position -= Vector3.right * 100;
            service.Viewport = new Rect(2000, 0, 1200, 600);
            Assert.That(service.Marquee(_viewport).Candidates, Is.Empty);
            service.Viewport = _viewport;
            _camera.cullingMask = 0;
            Assert.That(service.Marquee(_viewport).Candidates, Is.Empty);
            _camera.cullingMask = ~0;
            Assert.That(service.Marquee(_viewport).Candidates.Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator R2_38_CORE_PICK_500CandidatesMedianAcrossEditorFramesBelow50Ms()
        {
            // Qualifying this test requires an otherwise idle Editor host, not just a spare slot.
            int editors = 0;
            foreach (string process in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(process), out _)) continue;
                try
                {
                    if (File.ReadAllText(Path.Combine(process, "comm")).Trim() != "Unity") continue;
                    string args = File.ReadAllText(Path.Combine(process, "cmdline"));
                    if (args.Length > 0 && !args.Contains("AssetImportWorker")) editors++;
                }
                catch (IOException) { } // A process that exited during the inventory is not active.
                catch (UnauthorizedAccessException) { }
            }
            Assert.That(editors, Is.EqualTo(1), "run with the solo-unity adapter through unity-batch on an idle host");
            for (int i = 0; i < 500; i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "Pick-" + i;
                cube.transform.position = new Vector3(i % 25 - 12, i / 25 - 9.5f, 0);
                cube.transform.localScale = Vector3.one * .7f;
            }
            _bed.SaveScene();
            var service = Service();
            double[] samples = new double[21];
            double renderer = 0, resolve = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                yield return null;
                PickResult result = service.Marquee(_viewport, true);
                Assert.That(result.Targets().Count, Is.EqualTo(500));
                samples[i] = result.Timings.TotalMs;
                renderer += result.Timings.RendererMs;
                resolve += result.Timings.ResolveMs;
            }
            Array.Sort(samples);
            UnityEngine.Debug.Log($"[CORE-PICK] N=21 median={samples[10]:F4}ms max={samples[20]:F4}ms rendererSum={renderer:F4}ms resolveSum={resolve:F4}ms");
            Assert.That(samples[10], Is.LessThan(50), "fixed B-SELECT budget, including geometry refresh on each Editor frame");
        }

        // Independent uncached oracle: group every active renderer first, then project each
        // corner and require any intersection or containment of every renderer of an owner.
        private IEnumerable<Object> Naive(Rect input, bool full)
        {
            var rect = Rect.MinMaxRect(Mathf.Min(input.xMin, input.xMax), Mathf.Min(input.yMin, input.yMax),
                Mathf.Max(input.xMin, input.xMax), Mathf.Max(input.yMin, input.yMax));
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(_camera);
            var groups = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(r => r.enabled && (r.gameObject.hideFlags & (HideFlags.DontSave | HideFlags.HideInHierarchy)) == 0
                    && (_camera.cullingMask & (1 << r.gameObject.layer)) != 0)
                .GroupBy(r => (Object?)_bed.Runtime.Identity.FindLogicalOwner(r.transform) ?? r.gameObject);
            foreach (var group in groups)
            {
                var hits = new List<bool>();
                foreach (Renderer renderer in group)
                {
                    Bounds bounds = renderer.bounds;
                    if (!GeometryUtility.TestPlanesAABB(planes, bounds)) { hits.Add(false); continue; }
                    var points = new List<Vector3>();
                    for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                        points.Add(_camera.WorldToViewportPoint(bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z))));
                    var front = points.Where(p => p.z > 0).Select(p => new Vector2(_viewport.x + p.x * _viewport.width,
                        _viewport.y + (1 - p.y) * _viewport.height)).ToArray();
                    if (front.Length == 0) { hits.Add(false); continue; }
                    var projected = Rect.MinMaxRect(front.Min(p => p.x), front.Min(p => p.y), front.Max(p => p.x), front.Max(p => p.y));
                    hits.Add(full ? front.Length == 8 && rect.Contains(projected.min) && rect.Contains(projected.max) : projected.Overlaps(rect));
                }
                if (full ? hits.All(h => h) : hits.Any(h => h)) yield return group.Key;
            }
        }

        private sealed class CountingResolver : IAuthoringRefResolver
        {
            private readonly IAuthoringRefResolver _inner;
            public CountingResolver(IAuthoringRefResolver inner) { _inner = inner; }
            public int Builds { get; private set; }
            public AuthoringRef? BuildRef(Object target, AuthorScope? scope = null, bool includeStamp = true)
            {
                Builds++;
                return _inner.BuildRef(target, scope, includeStamp);
            }
            public ResolveResult Resolve(AuthoringRef reference) => _inner.Resolve(reference);
            public string? ComputeStamp(Object target) => _inner.ComputeStamp(target);
        }
    }
}

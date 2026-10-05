// GameCore.Studio.UI.Tests - the Studio selection (two-way Unity mirror without feedback loops, persistence across a
// domain reload) and the viewport gestures over a PickingService fixture scene: click, shift-add, ctrl-toggle, empty
// click clears, the overlap list (two coincident objects within 4 px), marquee partial/full containment with the
// rectangle kept, point-at location, all producing the expected SelectionSnapshot. B-SELECT timings are logged.
#nullable enable
using System.Collections.Generic;
using GameCore.Studio.Authoring;
using GameCore.Studio.Fixtures;
using GameCore.Studio.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SelectionMode = GameCore.Studio.Model.SelectionMode;

namespace GameCore.Studio.UI.Tests
{
    public sealed class SelectionAndPickingTests
    {
        private static readonly Rect Viewport = new Rect(0f, 0f, 800f, 600f);
        private static readonly Vector2 Center = new Vector2(400f, 300f);

        private UiTestBed _bed = null!;
        private Camera _camera = null!;
        private SelectTimings _timings = null!;

        [SetUp]
        public void SetUp()
        {
            _bed = new UiTestBed();
            _camera = UiTestBed.CreateCamera(Viewport);
            _timings = new SelectTimings();
        }

        [TearDown]
        public void TearDown()
        {
            Debug.Log("[P2.1] B-SELECT " + _timings.Report());
            _bed.Dispose();
        }

        private ViewportPicker Picker(float groundHeight = -5f)
        {
            PickingService service = new PickingService(_camera, Viewport, _bed.Runtime.Resolver, _bed.Runtime.Identity, null, new PickOptions { GroundHeight = groundHeight });
            return new ViewportPicker(_bed.Context.Selection, () => service, _timings, () => 4);
        }

        [Test]
        public void Mirror_StudioToUnityAndBack_IsLoopSafe()
        {
            FixtureAuthoredEntity first = _bed.SpawnEntity("First", Vector3.zero);
            FixtureAuthoredEntity second = _bed.SpawnEntity("Second", new Vector3(3f, 0f, 0f));
            _bed.SaveScene();
            SelectionModel selection = _bed.Context.Selection;
            selection.AttachUnityMirror();
            int changes = 0;
            selection.Changed += () => changes++;

            selection.Set(new[] { _bed.Ref(first) });
            Assert.That(Selection.activeGameObject, Is.SameAs(first.gameObject), "Studio -> Unity");
            Assert.That(selection.MirrorWrites, Is.EqualTo(1));
            selection.OnUnitySelectionChanged();
            Assert.That(selection.MirrorEchoesIgnored, Is.EqualTo(1), "our own push comes back as an echo and is ignored");
            Assert.That(selection.MirrorReads, Is.EqualTo(0));
            Assert.That(changes, Is.EqualTo(1));

            Selection.objects = new Object[] { second.gameObject };
            selection.OnUnitySelectionChanged();
            Assert.That(selection.MirrorReads, Is.EqualTo(1), "Unity -> Studio");
            Assert.That(selection.Targets.Count, Is.EqualTo(1));
            Assert.That(selection.Targets[0].SameTarget(_bed.Ref(second)), Is.True);
            Assert.That(selection.MirrorWrites, Is.EqualTo(1), "adopting Unity's selection does not push it back");
            selection.OnUnitySelectionChanged();
            Assert.That(selection.MirrorReads, Is.EqualTo(1), "a repeated callback changes nothing");
            Assert.That(changes, Is.EqualTo(2));

            selection.Set(new[] { _bed.Ref(second) });
            Assert.That(selection.MirrorWrites, Is.EqualTo(1), "setting the same targets writes nothing");
        }

        [Test]
        public void Persistence_RestoresTargetsLocationAndMarqueeAfterReload()
        {
            FixtureAuthoredEntity first = _bed.SpawnEntity("First", Vector3.zero);
            _bed.SaveScene();
            Picker().PointAt(new Vector2(400f, 590f));
            _bed.Context.Selection.Set(new[] { _bed.Ref(first) }, SelectionOp.Replace, new RegionRect(new double[] { 1, 2, 3, 4 }));
            string saved = StudioSelection.Serialize(_bed.Context.Selection);

            SelectionModel restored = new SelectionModel(_bed.Runtime);
            StudioSelection.Load(restored, saved);
            Assert.That(restored.Targets.Count, Is.EqualTo(1));
            Assert.That(restored.Targets[0].SameTarget(_bed.Ref(first)), Is.True);
            Assert.That(restored.Location, Is.Not.Null);
            Assert.That(restored.RegionRect!.Screen, Is.EqualTo(new double[] { 1, 2, 3, 4 }));
            StudioSelection.Load(restored, "{not json");
            Assert.That(restored.Targets.Count, Is.EqualTo(1), "a damaged saved selection is ignored");
            restored.Dispose();
        }

        [Test]
        public void Click_ShiftAdd_CtrlToggle_EmptyClears()
        {
            FixtureAuthoredEntity middle = _bed.SpawnEntity("Middle", Vector3.zero);
            FixtureAuthoredEntity right = _bed.SpawnEntity("Right", new Vector3(3f, 0f, 0f));
            _bed.SaveScene();
            ViewportPicker picker = Picker();
            Vector2 rightPoint = (Vector2)ToViewport(right.transform.position);

            IReadOnlyList<PickCandidate> hit = picker.Click(Center, SelectionOp.Replace);
            Assert.That(hit.Count, Is.EqualTo(1), "ground is never an overlap candidate");
            AssertTargets(middle);
            picker.Click(rightPoint, SelectionOp.Add);
            AssertTargets(middle, right);
            picker.Click(Center, SelectionOp.Toggle);
            AssertTargets(right);
            picker.Click(new Vector2(20f, 20f), SelectionOp.Add);
            AssertTargets(right);
            picker.Click(new Vector2(20f, 20f), SelectionOp.Replace);
            AssertTargets();

            SelectionSnapshot snapshot = _bed.Context.Selection.Capture(SelectionMode.Edit);
            Assert.That(snapshot.Targets, Is.Empty);
            Assert.That(snapshot.Id, Does.StartWith("sel_"));
            Assert.That(_timings.Count("click"), Is.GreaterThanOrEqualTo(5));
        }

        [Test]
        public void Overlap_TwoObjectsUnderTheCursorAreListedNearestFirst()
        {
            FixtureAuthoredEntity front = _bed.SpawnEntity("Front", Vector3.zero);
            FixtureAuthoredEntity behind = _bed.SpawnEntity("Behind", new Vector3(0f, -1.5f, 5f));
            FixtureAuthoredEntity coincident = _bed.SpawnEntity("Coincident", Vector3.zero);
            _bed.SaveScene();

            IReadOnlyList<PickCandidate> candidates = Picker().Click(Center, SelectionOp.Replace);
            Assert.That(candidates.Count, Is.EqualTo(3), "the overlap list offers every object under the cursor");
            Assert.That(candidates[0].Distance, Is.LessThanOrEqualTo(candidates[1].Distance));
            Assert.That(candidates[1].Distance, Is.LessThanOrEqualTo(candidates[2].Distance));
            PickCandidate far = FindCandidate(candidates, behind);
            Assert.That(far.Occluded, Is.True, "objects behind the nearest surface are flagged occluded");
            Assert.That(FindCandidate(candidates, coincident).Occluded, Is.False);
            Assert.That(_bed.Context.Selection.Targets.Count, Is.EqualTo(1));
            Assert.That(_bed.Context.Selection.Targets[0].SameTarget(candidates[0].Ref), Is.True);
            Assert.That(_bed.Context.Selection.Targets[0].SameTarget(_bed.Ref(front)) || _bed.Context.Selection.Targets[0].SameTarget(_bed.Ref(coincident)), Is.True);

            OverlapPopup popup = new OverlapPopup((candidate, part) => Picker().Choose(candidate, part, SelectionOp.Replace));
            popup.Show(candidates, Center, reference => reference.Path ?? "?");
            Assert.That(popup.Visible, Is.True);
            Assert.That(popup.Candidates.Count, Is.EqualTo(3));
            Picker().Choose(far, false, SelectionOp.Replace);
            AssertTargets(behind);
        }

        [Test]
        public void Marquee_PartialAndFullContainment_KeepsTheRectangle()
        {
            FixtureAuthoredEntity left = _bed.SpawnEntity("Left", new Vector3(-3f, 0f, 0f));
            FixtureAuthoredEntity middle = _bed.SpawnEntity("Middle", Vector3.zero);
            FixtureAuthoredEntity right = _bed.SpawnEntity("Right", new Vector3(3f, 0f, 0f));
            _bed.SaveScene();
            ViewportPicker picker = Picker();
            Vector3 middlePoint = ToViewport(middle.transform.position);
            Vector3 rightPoint = ToViewport(right.transform.position);
            Rect straddling = Rect.MinMaxRect(middlePoint.x - 70f, middlePoint.y - 70f, rightPoint.x, middlePoint.y + 70f);

            picker.Marquee(straddling, SelectionOp.Replace, false);
            AssertTargets(middle, right);
            picker.Marquee(straddling, SelectionOp.Replace, true);
            AssertTargets(middle);
            picker.Marquee(new Rect(0f, 0f, 800f, 600f), SelectionOp.Replace, true);
            AssertTargets(left, middle, right);
            picker.Marquee(straddling, SelectionOp.Toggle, true);
            AssertTargets(left, right);

            SelectionSnapshot snapshot = _bed.Context.Selection.Capture(SelectionMode.Edit);
            Assert.That(snapshot.RegionRect, Is.Not.Null);
            Assert.That(snapshot.RegionRect!.Screen[0], Is.EqualTo(straddling.xMin).Within(0.01));
            Assert.That(snapshot.RegionRect.Screen[3], Is.EqualTo(straddling.yMax).Within(0.01));
            Assert.That(_timings.Count("marquee"), Is.EqualTo(4));
        }

        [Test]
        public void PointAt_AddsTheLocationToTheSnapshot()
        {
            FixtureAuthoredEntity npc = _bed.SpawnEntity("Npc", Vector3.zero);
            _bed.SaveScene();
            ViewportPicker picker = Picker();
            picker.Click(Center, SelectionOp.Replace);

            LocationPick ground = picker.PointAt(new Vector2(400f, 590f));
            Assert.That(ground.Hit, Is.True);
            Assert.That(ground.Position.y, Is.EqualTo(-5f).Within(0.01f));
            SelectionSnapshot snapshot = _bed.Context.Selection.Capture(SelectionMode.Edit);
            Assert.That(snapshot.Targets.Count, Is.EqualTo(2), "the target and the pointed-at location");
            Assert.That(snapshot.Targets[0].SameTarget(_bed.Ref(npc)), Is.True);
            Assert.That(snapshot.Targets[1].Kind, Is.EqualTo(AuthoringKind.Location));
            Assert.That(snapshot.Targets[1].Location!.Position[1], Is.EqualTo(-5.0).Within(0.01));
            UiTestBed.Timing("point-at", ground.Timings.TotalMs);
        }

        [Test]
        public void RuntimeViews_MapToTheirAuthoredObjects()
        {
            FixtureNpcDefinition maren = _bed.CreateNpc("Maren", "Morning");
            _bed.Runtime.Index.Rebuild();
            GameObject view = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            view.name = "NpcView(Maren)";
            view.AddComponent<EntityViewTag>().AuthoringId = maren.AuthoringId;
            PickingService service = new PickingService(_camera, Viewport, _bed.Runtime.Resolver, _bed.Runtime.Identity, null, new PickOptions { GroundHeight = -5f });
            RuntimeViewMapper mapper = new RuntimeViewMapper(_bed.Runtime);
            ViewportPicker picker = new ViewportPicker(_bed.Context.Selection, () => service, _timings, () => 4, mapper);

            IReadOnlyList<PickCandidate> candidates = picker.Click(Center, SelectionOp.Replace);
            Assert.That(candidates.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(mapper.Mapped, Is.GreaterThan(0));
            AssertTargets(maren);
            Assert.That(picker.Hover(Center)!.Ref.SameTarget(_bed.Ref(maren)), Is.True);
            picker.Marquee(new Rect(0f, 0f, 800f, 600f), SelectionOp.Replace, false);
            AssertTargets(maren);

            IReadOnlyList<PickCandidate> unmapped = Picker().Click(Center, SelectionOp.Replace);
            Assert.That(unmapped.Count == 0 || unmapped[0].Ref.AuthoringId == null, Is.True, "without the mapper the view is a plain scene object");
        }

        [Test]
        public void Badges_ReportStaleTargets()
        {
            FixtureAuthoredEntity doomed = _bed.SpawnEntity("Doomed", Vector3.zero);
            _bed.SaveScene();
            _bed.Context.Selection.Set(new[] { _bed.Ref(doomed) });
            Object.DestroyImmediate(doomed.gameObject);
            IReadOnlyList<SelectionBadge> badges = _bed.Context.Selection.Describe();
            Assert.That(badges.Count, Is.EqualTo(1));
            Assert.That(badges[0].Stale, Is.True);
        }

        private Vector3 ToViewport(Vector3 world)
        {
            Vector3 point = _camera.WorldToViewportPoint(world);
            return new Vector3(point.x * Viewport.width, (1f - point.y) * Viewport.height, point.z);
        }

        private PickCandidate FindCandidate(IReadOnlyList<PickCandidate> candidates, Object target)
        {
            AuthoringRef reference = _bed.Ref(target);
            foreach (PickCandidate candidate in candidates)
            {
                if (candidate.Ref.SameTarget(reference))
                {
                    return candidate;
                }
            }

            Assert.Fail("No candidate for " + target.name);
            return null!;
        }

        private void AssertTargets(params Object[] expected)
        {
            IReadOnlyList<AuthoringRef> targets = _bed.Context.Selection.Targets;
            Assert.That(targets.Count, Is.EqualTo(expected.Length), "targets: " + string.Join(", ", Names(targets)));
            foreach (Object target in expected)
            {
                AuthoringRef reference = _bed.Ref(target);
                bool found = false;
                foreach (AuthoringRef actual in targets)
                {
                    found |= actual.SameTarget(reference);
                }

                Assert.That(found, Is.True, target.name + " should be selected");
            }
        }

        private static IEnumerable<string> Names(IReadOnlyList<AuthoringRef> targets)
        {
            foreach (AuthoringRef target in targets)
            {
                yield return target.Path ?? target.AuthoringId ?? "?";
            }
        }
    }
}

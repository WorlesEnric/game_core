// GameCore.Studio.Edit.Tests - picking over a camera and viewport rect: depth ordering with occlusion, overlap groups,
// the ground candidate, marquee containment, PointAt with a Location ref, Validate of a selection, and a UI Toolkit
// pick; each test logs the Stopwatch timings of the service (docs/studio/03-authoring-contracts.md s2).
#nullable enable
using System.Collections;
using GameCore.Studio.Authoring;
using GameCore.Studio.Fixtures;
using GameCore.Studio.Model;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace GameCore.Studio.Edit.Tests
{
    public sealed class PickingServiceTests
    {
        private static readonly Rect Viewport = new Rect(0f, 0f, 800f, 600f);
        private static readonly Vector2 Center = new Vector2(400f, 300f);

        private StudioTestBed _bed = null!;
        private Camera _camera = null!;

        [SetUp]
        public void SetUp()
        {
            _bed = new StudioTestBed();
            GameObject cameraObject = new GameObject("PickCamera");
            _camera = cameraObject.AddComponent<Camera>();
            Vector3 eye = new Vector3(0f, 3f, -10f);
            _camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(Vector3.zero - eye));
            _camera.fieldOfView = 60f;
            _camera.aspect = Viewport.width / Viewport.height;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 200f;
        }

        [TearDown]
        public void TearDown() => _bed.Dispose();

        private PickingService Service(PickOptions? options = null)
        {
            return new PickingService(_camera, Viewport, _bed.Runtime.Resolver, _bed.Runtime.Identity, null, options ?? new PickOptions { GroundHeight = -5f });
        }

        [Test]
        public void Pick_OrdersByDepthMarksOcclusionAndAppendsTheGround()
        {
            FixtureAuthoredEntity front = _bed.SpawnEntity("Front", new Vector3(0f, 0f, 0f));
            FixtureAuthoredEntity back = _bed.SpawnEntity("Back", new Vector3(0f, -1.5f, 5f));
            _bed.SaveScene();

            PickResult result = Service().Pick(Center);
            Debug.Log("[P1.6] pick timings: " + result.Timings);

            Assert.That(result.Candidates.Count, Is.GreaterThanOrEqualTo(3));
            Assert.That(result.Default, Is.Not.Null);
            Assert.That(result.Default!.Ref.SameTarget(_bed.Ref(front)), Is.True, "the nearest authored owner is the default");
            Assert.That(result.Candidates[0].Occluded, Is.False);
            Assert.That(result.Candidates[1].Ref.SameTarget(_bed.Ref(back)), Is.True);
            Assert.That(result.Candidates[1].Occluded, Is.True);
            Assert.That(result.Candidates[result.Candidates.Count - 1].Source, Is.EqualTo(PickSource.Ground));
            Assert.That(result.OverlapList.Count, Is.EqualTo(result.Candidates.Count), "the overlap list offers every candidate under the cursor");
            Assert.That(result.Timings.TotalMs, Is.GreaterThanOrEqualTo(0.0));
        }

        [Test]
        public void Pick_CoincidentObjectsShareAnOverlapGroup()
        {
            FixtureAuthoredEntity first = _bed.SpawnEntity("First", Vector3.zero);
            FixtureAuthoredEntity second = _bed.SpawnEntity("Second", Vector3.zero);
            _bed.SaveScene();

            PickResult result = Service(new PickOptions { IncludeGroundCandidate = false }).Pick(Center);
            Debug.Log("[P1.6] overlap pick timings: " + result.Timings);
            Assert.That(result.Candidates.Count, Is.EqualTo(2));
            Assert.That(result.Candidates[0].OverlapGroup, Is.GreaterThanOrEqualTo(0));
            Assert.That(result.Candidates[0].OverlapGroup, Is.EqualTo(result.Candidates[1].OverlapGroup));
            Assert.That(result.Candidates[1].Occluded, Is.False, "coincident surfaces do not occlude each other");
            Assert.That(result.Targets(), Has.Some.Matches<AuthoringRef>(r => r.SameTarget(_bed.Ref(first))));
            Assert.That(result.Targets(), Has.Some.Matches<AuthoringRef>(r => r.SameTarget(_bed.Ref(second))));
        }

        [Test]
        public void Marquee_SelectsContainedOwners()
        {
            FixtureAuthoredEntity left = _bed.SpawnEntity("Left", new Vector3(-3f, 0f, 0f));
            FixtureAuthoredEntity middle = _bed.SpawnEntity("Middle", Vector3.zero);
            FixtureAuthoredEntity right = _bed.SpawnEntity("Right", new Vector3(3f, 0f, 0f));
            _bed.SaveScene();
            PickingService service = Service();

            PickResult centered = service.Marquee(new Rect(330f, 230f, 140f, 140f), true);
            Debug.Log("[P1.6] marquee timings: " + centered.Timings);
            Assert.That(centered.Targets(), Has.Some.Matches<AuthoringRef>(r => r.SameTarget(_bed.Ref(middle))));
            Assert.That(centered.Targets(), Has.None.Matches<AuthoringRef>(r => r.SameTarget(_bed.Ref(left)) || r.SameTarget(_bed.Ref(right))));

            PickResult all = service.Marquee(new Rect(0f, 0f, 800f, 600f), true);
            Assert.That(all.Targets().Count, Is.EqualTo(3));
        }

        [Test]
        public void PointAt_ReturnsALocationOnTheSurfaceOrTheGroundPlane()
        {
            _bed.SpawnEntity("Block", Vector3.zero);
            _bed.SaveScene();
            PickingService service = Service();

            LocationPick onBlock = service.PointAt(Center);
            Debug.Log("[P1.6] point-at timings: " + onBlock.Timings);
            Assert.That(onBlock.Hit, Is.True);
            Assert.That(onBlock.Source, Is.EqualTo(PickSource.Physics));
            Assert.That(onBlock.Position.z, Is.EqualTo(-0.5f).Within(0.01f));
            Assert.That(onBlock.Location!.Kind, Is.EqualTo(AuthoringKind.Location));
            Assert.That(onBlock.Location.Location!.Region, Is.Not.Empty);

            LocationPick onGround = service.PointAt(new Vector2(400f, 590f));
            Assert.That(onGround.Hit, Is.True);
            Assert.That(onGround.Position.y, Is.EqualTo(-5f).Within(0.01f));
        }

        [Test]
        public void Validate_ReportsTargetsThatNoLongerExist()
        {
            FixtureAuthoredEntity kept = _bed.SpawnEntity("Kept", Vector3.zero);
            FixtureAuthoredEntity removed = _bed.SpawnEntity("Removed", Vector3.right * 2f);
            _bed.SaveScene();
            SelectionSnapshot snapshot = new SelectionSnapshot(IdDerivation.NewSelectionId(System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), CryptoIdEntropy.Instance), SelectionMode.Edit, new[] { _bed.Ref(kept), _bed.Ref(removed) }, 0);
            Object.DestroyImmediate(removed.gameObject);

            StaleReport report = Service().Validate(snapshot);
            Assert.That(report.IsStale, Is.True);
            Assert.That(report.Entries.Count, Is.EqualTo(1));
            Assert.That(report.Entries[0].Reason, Is.EqualTo(StaleReason.Destroyed));
            Assert.That(report.Entries[0].Diagnostic.Code, Is.EqualTo(DiagnosticCodes.StaleTarget));
        }

        [UnityTest]
        public IEnumerator Pick_UiElementsComeFirst()
        {
            _bed.SpawnEntity("Behind", Vector3.zero);
            GameObject uiObject = new GameObject("Hud");
            UIDocument document = uiObject.AddComponent<UIDocument>();
            PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.targetTexture = new RenderTexture((int)Viewport.width, (int)Viewport.height, 0);
            document.panelSettings = settings;
            VisualElement button = new VisualElement { name = "TalkButton" };
            button.style.position = Position.Absolute;
            button.style.left = 350f;
            button.style.top = 250f;
            button.style.width = 100f;
            button.style.height = 100f;
            button.pickingMode = PickingMode.Position;
            for (int frame = 0; frame < 10 && document.rootVisualElement == null; frame++)
            {
                yield return null;
            }

            if (document.rootVisualElement == null)
            {
                Assert.Ignore("No runtime panel in this editor session (batch mode without a Game view).");
            }

            document.rootVisualElement!.Add(button);
            for (int frame = 0; frame < 10 && (float.IsNaN(button.layout.width) || button.layout.width <= 0f); frame++)
            {
                yield return null;
            }

            if (float.IsNaN(button.layout.width) || button.layout.width <= 0f)
            {
                Assert.Ignore("The runtime panel was not laid out in this editor session (batch mode without a Game view).");
            }

            PickResult result = Service().Pick(Center);
            Debug.Log("[P1.6] UI pick timings: " + result.Timings);
            Assert.That(result.Candidates.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(result.Candidates[0].Source, Is.EqualTo(PickSource.Ui));
            Assert.That(result.Candidates[0].Ref.Kind, Is.EqualTo(AuthoringKind.UiElement));
            StringAssert.Contains("#ui/", result.Candidates[0].Ref.Path);
            StringAssert.Contains("TalkButton", result.Candidates[0].Part);
            Object.DestroyImmediate(uiObject);
            Object.DestroyImmediate(settings.targetTexture);
            Object.DestroyImmediate(settings);
        }
    }
}

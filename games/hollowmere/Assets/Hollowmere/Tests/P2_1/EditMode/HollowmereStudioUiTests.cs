// Hollowmere.P2_1.Tests - the Studio UI over the real Hollowmere project: Thornwick Village opened in the editor, a
// placed entity picked through the viewport picker, the prompt request built from that selection (Hollowmere's
// catalog revision, depth-2 slice under the 64 KB cap), the context panel listing the gameplay tools for it, and the
// Studio windows opening at 1280x720. B-SELECT timings over the real scene are logged.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using GameCore.Studio.Authoring;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using GameCore.Studio.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using SelectionMode = GameCore.Studio.Model.SelectionMode;

namespace Hollowmere.P2_1.Tests
{
    public sealed class HollowmereStudioUiTests
    {
        private const string HollowmereFolder = "Assets/Hollowmere";
        private const string VillageScene = "Assets/Hollowmere/Regions/ThornwickVillage.unity";

        private string _stateRoot = string.Empty;
        private StudioRuntime? _runtime;
        private StudioUiContext? _context;
        private Camera? _camera;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.OpenScene(VillageScene, OpenSceneMode.Single);
            _stateRoot = Path.Combine(Path.GetTempPath(), "gcstudio-p21-hollowmere-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_stateRoot);
            _runtime = StudioRuntime.Create(new StudioRuntimeOptions
            {
                Paths = new StudioPaths(Directory.GetParent(Application.dataPath)!.FullName, _stateRoot, "p21-hollowmere"),
                Log = new MemoryStudioLog(),
                SearchFolders = new[] { HollowmereFolder },
                LoadIndexCache = false,
            });
            _runtime.Index.Rebuild();
            _context = new StudioUiContext(_runtime, () => NullAgentGateway.Instance, new SelectionModel(_runtime), new TaskLedger(new MemoryTaskRowStore()), false);
        }

        [TearDown]
        public void TearDown()
        {
            if (_camera != null)
            {
                UnityEngine.Object.DestroyImmediate(_camera.gameObject);
            }

            _context?.Dispose();
            _runtime?.Dispose();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (Directory.Exists(_stateRoot))
            {
                Directory.Delete(_stateRoot, true);
            }
        }

        [Test]
        public void PickPlacedEntity_BuildsARequestWithinTheCap()
        {
            StudioRuntime runtime = _runtime!;
            GameObject? placed = FindPlacedEntity(runtime);
            Assert.That(placed, Is.Not.Null, "Thornwick Village has placed authored entities with colliders");
            Rect viewport = new Rect(0f, 0f, 1280f, 720f);
            _camera = LookAt(placed!, viewport);
            PickingService service = new PickingService(_camera, viewport, runtime.Resolver, runtime.Identity, null, new PickOptions());
            SelectTimings timings = new SelectTimings();
            ViewportPicker picker = new ViewportPicker(_context!.Selection, () => service, timings, () => 4);

            IReadOnlyList<PickCandidate> candidates = picker.Click(viewport.center, SelectionOp.Replace);
            Assert.That(candidates.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(_context.Selection.Targets.Count, Is.EqualTo(1));
            UnityEngine.Object? resolved = _context.Selection.ResolvePrimary();
            GameObject? resolvedObject = resolved is Component component ? component.gameObject : resolved as GameObject;
            Assert.That(resolvedObject, Is.SameAs(placed), "the click selects the entity under the cursor");
            for (int i = 0; i < 20; i++)
            {
                picker.Hover(viewport.center + new Vector2(i * 3f, 0f));
            }

            picker.Marquee(new Rect(0f, 0f, viewport.width, viewport.height), SelectionOp.Replace, false);
            Assert.That(_context.Selection.Targets.Count, Is.GreaterThanOrEqualTo(1));
            LocationPick location = picker.PointAt(new Vector2(viewport.center.x, viewport.height - 10f));
            Debug.Log("[P2.1] hollowmere B-SELECT " + timings.Report() + "; point-at hit=" + location.Hit);

            SelectionSnapshot snapshot = _context.Selection.Capture(SelectionMode.Edit);
            PreparedRequest request = _context.Requests.Build("Make these feel more lived-in", snapshot);
            ToolCatalog catalog = runtime.Registry.Catalog;
            Assert.That(request.ToolCatalogRevision, Is.EqualTo(catalog.Revision ?? catalog.ComputeRevision()));
            Assert.That(request.ContextBytes, Is.LessThanOrEqualTo(AgentRequestBuilder.SliceByteCap));
            Assert.That(request.Selection.Targets.Count, Is.EqualTo(snapshot.Targets.Count));
            Debug.Log("[P2.1] hollowmere request slice " + request.ContextBytes + " bytes, truncated=" + request.ContextTruncated + ", omitted=" + request.ContextOmittedNodes);

            PromptSubmission handle = _context.Submit(request).Result;
            Assert.That(handle.Accepted, Is.False, "without a gateway the request is refused, never faked");
            Assert.That(handle.Refusal!.Code, Is.EqualTo("not_configured"));
            Assert.That(_context.Tasks.Find(request.ChangeSetId)!.State, Is.EqualTo(AgentRequestState.Refused));
        }

        [Test]
        public void ContextPanel_ListsGameplayToolsForAPlacedEntity()
        {
            StudioRuntime runtime = _runtime!;
            GameObject? placed = FindPlacedEntity(runtime);
            Assert.That(placed, Is.Not.Null);
            AuthoringRef? reference = _context!.Selection.RefOf(placed!);
            Assert.That(reference, Is.Not.Null);
            _context.Selection.Set(new[] { reference! });
            ContextPanelView panel = new ContextPanelView(_context);
            panel.Rebuild();
            Assert.That(panel.ShownTools.Count, Is.GreaterThan(0), "tools for " + reference);
            foreach (IStudioTool tool in panel.ShownTools)
            {
                Assert.That(tool.Internal, Is.False);
            }
        }

        [Test]
        public void SettingsPage_BuildsEverySection()
        {
            VisualElement root = new VisualElement();
            StudioSettingsProvider.Build(root);
            Assert.That(root.childCount, Is.GreaterThan(0));
            Assert.That(root.Q("settings-journal"), Is.Not.Null);
            Assert.That(root.Q("settings-agent-gateway"), Is.Not.Null);
        }

        private static GameObject? FindPlacedEntity(StudioRuntime runtime)
        {
            foreach (Collider collider in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.InstanceID))
            {
                if (collider.bounds.size.magnitude > 40f)
                {
                    continue;
                }

                Transform? current = collider.transform;
                while (current != null)
                {
                    MonoBehaviour? authored = runtime.Identity.FindAuthoredComponent(current.gameObject);
                    if (authored != null && runtime.Identity.Describe(authored)?.TypeId == "entity.instance")
                    {
                        return current.gameObject;
                    }

                    current = current.parent;
                }
            }

            return null;
        }

        private static Camera LookAt(GameObject target, Rect viewport)
        {
            Bounds bounds = new Bounds(target.transform.position, Vector3.one);
            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>())
            {
                bounds.Encapsulate(renderer.bounds);
            }

            foreach (Collider collider in target.GetComponentsInChildren<Collider>())
            {
                bounds.Encapsulate(collider.bounds);
            }

            Camera camera = new GameObject("P2_1 Camera").AddComponent<Camera>();
            float distance = Mathf.Max(4f, bounds.extents.magnitude * 3f);
            Vector3 eye = bounds.center + (new Vector3(0f, 0.6f, -1f).normalized * distance);
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(bounds.center - eye));
            camera.fieldOfView = 60f;
            camera.aspect = viewport.width / viewport.height;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 1000f;
            return camera;
        }
    }
}

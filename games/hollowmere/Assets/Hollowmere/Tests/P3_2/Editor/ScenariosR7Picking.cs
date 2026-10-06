// R7-B exact W-UI-02/W-UI-03 acceptance drivers. No gateway, provider, or Play Mode is used.
// Invoke ScenariosR7Picking.RunFence or RunLantern through the host batch lock, without -quit.
// Receipts distinguish engine/controller proof from rendered creator interaction; missing UI never passes.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.Npc;
using GameCore.Gameplay.World;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using GameCore.Studio.UI;
using Hollowmere.P2_1.Evidence;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Hollowmere.P3_2.Workflows
{
    public static class ScenariosR7Picking
    {
        public static void RunFence() => R7PickingRun.instance.Begin("W-UI-02");
        public static void RunLantern() => R7PickingRun.instance.Begin("W-UI-03");
    }

    public sealed class R7PickingRun : ScriptableSingleton<R7PickingRun>
    {
        private IEnumerator? _steps;
        private SceneSetup[]? _originalScenes;
        private string _temporaryAssets = string.Empty;
        private string _output = string.Empty;
        private string _row = string.Empty;
        private StudioRuntime? _runtime;
        private StudioUiContext? _context;
        private SelectionModel? _selection;
        private StudioViewportWindow? _window;
        private Camera? _camera;
        private GameObject? _cameraObject;
        private ViewportPicker? _picker;
        private PickingService? _service;
        private readonly List<string> _blockers = new List<string>();
        private readonly JArray _observations = new JArray();
        private double _deadline;
        private bool _shown;

        private StudioRuntime Runtime => _runtime ?? throw new InvalidOperationException("No picking runtime");
        private SelectionModel Selection => _selection ?? throw new InvalidOperationException("No selection model");
        private ViewportPicker Picker => _window != null ? _window.Picker : _picker!;
        private Rect Viewport => _window != null ? _window.ImageRect : new Rect(0, 0, 960, 540);

        public void Begin(string row)
        {
            if (_steps != null) throw new InvalidOperationException("A picking driver is already running");
            _row = row;
            _blockers.Clear();
            _observations.Clear();
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            _output = Path.Combine(root, "artifacts/studio/verification", row,
                "r7-b-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ"));
            Directory.CreateDirectory(_output);
            _deadline = EditorApplication.timeSinceStartup + 180;
            _steps = Execute();
            EditorApplication.update += Tick;
        }

        private void Tick()
        {
            try
            {
                Require(EditorApplication.timeSinceStartup < _deadline, "Picking driver exceeded its 180-second deadline");
                if (_steps != null && _steps.MoveNext()) return;
                Finish(_blockers.Count == 0 ? "PASS" : "BLOCKED", null);
            }
            catch (Exception error)
            {
                Finish("FAIL", error.ToString());
            }
        }

        private IEnumerator Execute()
        {
            Require(Application.isBatchMode, "This driver is batch-Editor only; do not run on the interactive desktop");
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Start in Edit Mode");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                Require(!SceneManager.GetSceneAt(i).isDirty, "Save or discard existing scene changes before this isolated scenario");
            _originalScenes = EditorSceneManager.GetSceneManagerSetup();
            _temporaryAssets = "Assets/R7Picking-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(_temporaryAssets);
            AssetDatabase.Refresh();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var region = new GameObject("Thornwick picking acceptance region").AddComponent<AuthoredRegion>();
            region.Configure(Load<RegionDefinition>("Assets/Hollowmere/World/Regions/ThornwickVillage.asset"), null);
            region.Bounds.Configure(new Vector3(0, 2, 0), new Vector3(40, 20, 40));
            var light = new GameObject("Acceptance sunlight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            light.transform.rotation = Quaternion.Euler(40, -30, 0);
            _runtime = StudioRuntime.Create(new StudioRuntimeOptions
            {
                Paths = new StudioPaths(Directory.GetParent(Application.dataPath)!.FullName,
                    Path.Combine(_output, "state"), "hollowmere-r7-picking"),
                LoadIndexCache = false,
                SearchFolders = new[] { "Assets/Hollowmere/World", "Assets/Hollowmere/Npcs" },
            });
            _selection = new SelectionModel(Runtime);
            _context = new StudioUiContext(Runtime, () => NullAgentGateway.Instance, Selection,
                new TaskLedger(new MemoryTaskRowStore()), false);
            if (ViewportRenderer.CanRender)
            {
                _window = CreateInstance<StudioViewportWindow>();
                _window.UseContext(_context);
                _window.position = new Rect(0, 0, 1280, 800);
                _window.Show();
                _shown = true;
                _window.EnsureGui();
                _window.SetMode(ViewportMode.Select);
                _window.Renderer.ForceFreeCamera = true;
                for (int frame = 0; frame < 120 && !ReadyLayout(); frame++) yield return null;
                Require(ReadyLayout(), "The real Studio viewport never obtained usable layout");
                _camera = _window.Renderer.ChooseCamera();
            }
            else
            {
                Block("capture_unavailable: the batch Editor has no graphics device (-nographics). Real offscreen viewport and GUIView captures require a graphics-enabled batch Editor, not display :1.");
                _cameraObject = new GameObject("R7 picking camera");
                _camera = _cameraObject.AddComponent<Camera>();
                _camera.enabled = false;
                _service = new PickingService(_camera, Viewport, Runtime.Resolver, Runtime.Identity);
                _picker = new ViewportPicker(Selection, () => _service!, new SelectTimings(), () => 4);
            }
            _camera.transform.SetPositionAndRotation(new Vector3(0, 1.2f, -14), Quaternion.identity);
            _camera.orthographic = true;
            _camera.orthographicSize = 3.8f;
            _camera.aspect = Viewport.width / Viewport.height;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 100;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.09f, 0.12f, 0.16f);
            IEnumerator scenario = _row == "W-UI-02" ? Fence(scene) : Lantern(scene);
            while (scenario.MoveNext()) yield return scenario.Current;
        }

        private IEnumerator Fence(Scene scene)
        {
            string[] names = { "Maren", "Odd", "Pip" };
            var npcs = new List<AuthoredEntity>();
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                var definition = Load<EntityDefinition>("Assets/Hollowmere/Npcs/Definitions/" + name + "Entity.asset");
                var npc = Load<NpcDefinition>("Assets/Hollowmere/Npcs/Definitions/" + name + ".asset");
                Require(npc.Entity == definition, name + " NPC definition must reference its real entity definition");
                npcs.Add(Place("Assets/Hollowmere/Npcs/Prefabs/NpcCapsule.prefab", definition,
                    name, new Vector3((i - 1) * 2.2f, 0, 1)));
            }
            // One rail crosses every NPC silhouette. Posts make this an actual fence, not an invisible occluder.
            GameObject rail = Cube("Fence rail", new Vector3(0, 1.2f, -1), new Vector3(8, 0.35f, 0.2f));
            Cube("Fence lower rail", new Vector3(0, 0.55f, -1), new Vector3(8, 0.18f, 0.2f));
            for (int i = 0; i < 5; i++) Cube("Fence post " + i, new Vector3((i - 2) * 1.8f, 0.8f, -1), new Vector3(0.16f, 1.6f, 0.25f));
            SaveRig(scene);
            yield return null;
            Record("fixture", new JObject
            {
                ["npcs"] = new JArray(npcs.Select(n => StudioJson.ToToken(Ref(n)))),
                ["fenceRail"] = StudioJson.ToToken(Ref(rail)),
                ["geometry"] = "NPC z=1; opaque fence rails/posts z=-1; camera z=-14 looking +Z",
            });
            Capture("01-three-npcs-behind-fence");
            Rect marquee = BoundsRect(npcs.SelectMany(n => n.GetComponentsInChildren<Renderer>()));
            marquee = Rect.MinMaxRect(marquee.xMin - 3, marquee.yMin - 3, marquee.xMax + 3, marquee.yMax + 3);
            PickResult result = _window != null
                ? _window.MarqueeSelect(marquee, SelectionOp.Replace, true)
                : Picker.Marquee(marquee, SelectionOp.Replace, true);
            foreach (AuthoredEntity npc in npcs) Require(Selection.Targets.Any(t => t.SameTarget(Ref(npc))), "Marquee omitted " + npc.name);
            Require(Selection.RegionRect != null, "Marquee rectangle missing from selection snapshot");
            Record("marquee", new JObject { ["result"] = Candidates(result.Candidates), ["selection"] = Snapshot(),
                ["resultingOverlapVisible"] = _window?.Overlap?.Visible ?? false });
            yield return null;
            Capture("02-marquee-result");
            if (_window?.Overlap?.Visible != true)
                Block("marquee_choices_missing: StudioViewportWindow.MarqueeSelect(Rect, SelectionOp, bool?) selects targets but does not present the resulting overlap choices. A later point-click list is not evidence of the requested marquee chooser.");
            var chosen = new List<PickCandidate>();
            foreach (AuthoredEntity npc in npcs)
            {
                Vector2 point = Project(new Vector3(npc.transform.position.x, 1.2f, 1));
                IReadOnlyList<PickCandidate> hits = Click(point);
                PickCandidate candidate = hits.Single(h => h.Ref.SameTarget(Ref(npc)));
                Require(candidate.Occluded, npc.name + " must be occluded by the fence");
                Require(hits.Any(h => h.Ref.SameTarget(Ref(rail)) && h.Distance < candidate.Distance), "Fence must precede " + npc.name);
                Record("point-overlap-" + npc.name, new JObject { ["candidates"] = Candidates(hits) });
                yield return null;
                Capture("03-overlap-" + npc.name);
                Choose(candidate, false, hits);
                AssertTargets(Ref(npc));
                Require(Selection.Parts.Count == 0, "Logical NPC choice retained a mesh subpart");
                chosen.Add(candidate);
                Record("chosen-" + npc.name, new JObject { ["selection"] = Snapshot() });
                yield return null;
                Capture("04-chosen-" + npc.name);
            }
            // This is explicitly below-UI coverage, never a substitute for the missing marquee choice command.
            Picker.Choose(chosen[0], false, SelectionOp.Replace);
            for (int i = 1; i < chosen.Count; i++) Picker.Choose(chosen[i], false, SelectionOp.Add);
            AssertTargets(npcs.Select(Ref).ToArray());
            Record("controller-only-three-npcs", new JObject { ["creatorInteraction"] = false, ["selection"] = Snapshot() });
        }

        private IEnumerator Lantern(Scene scene)
        {
            const string prefabPath = "Assets/Hollowmere/World/Prefabs/Lantern.prefab";
            AuthoredEntity lantern = Place(prefabPath, Load<EntityDefinition>("Assets/Hollowmere/World/Definitions/Lantern.asset"),
                "Lantern", Vector3.zero);
            // Real scenery behind the lantern makes the existing overlap UI available for the part/logical choices.
            Cube("Wall behind lantern", new Vector3(0, 1.2f, 2), new Vector3(4, 3, 0.2f));
            SaveRig(scene);
            AuthoringRef logical = Ref(lantern);
            AuthoringRef prefab = Runtime.Resolver.BuildRef(Load<GameObject>(prefabPath), AuthorScope.Prefab)!;
            AuthoringRef instanceScope = Runtime.Resolver.BuildRef(lantern, AuthorScope.Instance)!;
            Require(prefab != null && Runtime.Resolver.Resolve(prefab).Object != null, "Lantern prefab reference must resolve");
            Require(instanceScope.Scope == AuthorScope.Instance && instanceScope.SameTarget(logical), "Instance scope must preserve lantern identity");
            Record("fixture", new JObject { ["logical"] = StudioJson.ToToken(logical),
                ["prefab"] = StudioJson.ToToken(prefab), ["instanceScope"] = StudioJson.ToToken(instanceScope),
                ["part"] = "Mesh:Body" });
            yield return null;
            Capture("01-lantern");
            Vector2 point = Project(lantern.transform.Find("Body").GetComponent<Renderer>().bounds.center);
            IReadOnlyList<PickCandidate> hits = Click(point);
            PickCandidate body = hits.Single(h => h.Ref.SameTarget(logical) && h.Part == "Mesh:Body");
            Record("lantern-overlap", new JObject { ["candidates"] = Candidates(hits) });
            yield return null;
            Capture("02-lantern-chooser");
            Choose(body, true, hits);
            AssertTargets(logical);
            Require(Selection.Parts.Count == 1 && Selection.Parts[0].Owner.SameTarget(logical)
                && Selection.Parts[0].Part == "Mesh:Body", "Subpart selection must retain precisely Mesh:Body and its logical lantern owner");
            Record("subpart", new JObject { ["selection"] = Snapshot() });
            yield return null;
            Capture("03-lantern-subpart");
            hits = Click(point);
            body = hits.Single(h => h.Ref.SameTarget(logical) && h.Part == "Mesh:Body");
            Choose(body, false, hits);
            AssertTargets(logical);
            Require(Selection.Parts.Count == 0, "Logical choice must clear the previous subpart");
            Record("logical", new JObject { ["selection"] = Snapshot() });
            yield return null;
            Capture("04-lantern-logical");
            hits = Click(point);
            Record("available-creator-choices", new JObject
            {
                ["buttons"] = new JArray(_window?.Overlap?.Query<Button>().ToList().Select(b => b.text) ?? Enumerable.Empty<string>()),
                ["expectedPrefab"] = StudioJson.ToToken(prefab), ["expectedInstanceScope"] = StudioJson.ToToken(instanceScope),
                ["prefabAndScopeChosen"] = false,
            });
            yield return null;
            Capture("05-prefab-scope-missing");
            Block("lantern_prefab_scope_choices_missing: OverlapPopup.Show offers only logical and part buttons; ViewportPicker.Choose(PickCandidate, bool, SelectionOp) has no prefab or authoring-scope choice. The driver resolved the real prefab and Instance-scoped lantern refs but deliberately did not set them programmatically and claim a creator choice.");
        }

        private bool ReadyLayout() => _window != null && _window.Area?.panel != null
            && _window.ImageRect.width >= 640 && _window.ImageRect.height >= 360;

        private AuthoredEntity Place(string prefabPath, EntityDefinition definition, string name, Vector3 position)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(prefabPath));
            go.name = name;
            go.transform.position = position;
            var entity = go.GetComponent<AuthoredEntity>() ?? go.AddComponent<AuthoredEntity>();
            entity.RemintAuthoringId();
            entity.SetDefinition(definition);
            return entity;
        }

        private static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path)
            ?? throw new InvalidOperationException("Missing required shipped asset " + path);

        private static GameObject Cube(string name, Vector3 center, Vector3 scale)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = center;
            go.transform.localScale = scale;
            return go;
        }

        private void SaveRig(Scene scene)
        {
            Require(EditorSceneManager.SaveScene(scene, _temporaryAssets + "/Picking.unity"), "Could not save isolated picking fixture");
            Physics.SyncTransforms();
            Runtime.Index.Rebuild();
        }

        private AuthoringRef Ref(Object obj) => Runtime.Resolver.BuildRef(obj)
            ?? throw new InvalidOperationException("No authoring reference for " + obj.name);

        private Vector2 Project(Vector3 world)
        {
            Vector3 p = _camera!.WorldToViewportPoint(world);
            Require(p.z > 0 && p.x >= 0 && p.x <= 1 && p.y >= 0 && p.y <= 1, "Fixture target is outside the viewport");
            return new Vector2(p.x * Viewport.width, (1 - p.y) * Viewport.height);
        }

        private Rect BoundsRect(IEnumerable<Renderer> renderers)
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            foreach (Renderer renderer in renderers)
                for (int i = 0; i < 8; i++)
                {
                    Bounds b = renderer.bounds;
                    Vector2 p = Project(b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private IReadOnlyList<PickCandidate> Click(Vector2 point) => _window != null
            ? _window.ClickAt(point, SelectionOp.Replace) : Picker.Click(point, SelectionOp.Replace);

        private void Choose(PickCandidate candidate, bool part, IReadOnlyList<PickCandidate> hits)
        {
            if (_window == null)
            {
                Picker.Choose(candidate, part, SelectionOp.Replace);
                Record("controller-choice", new JObject { ["creatorInteraction"] = false, ["part"] = part });
                return;
            }
            OverlapPopup popup = _window.Overlap!;
            Require(popup.Visible, "The real overlap popup must be visible before choosing");
            int index = hits.ToList().IndexOf(candidate);
            Button logicalButton = popup.Q<Button>("overlap-" + index)
                ?? throw new InvalidOperationException("Real overlap button missing");
            Button button = part ? logicalButton.parent.Query<Button>().ToList().Single(b => b != logicalButton) : logicalButton;
            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }
            Require(!popup.Visible, "UI Toolkit submit did not activate the actual chooser button");
            Record("creator-button", new JObject { ["creatorInteraction"] = true, ["text"] = button.text,
                ["input"] = "NavigationSubmitEvent sent to attached real Button" });
        }

        private void AssertTargets(params AuthoringRef[] expected)
        {
            Require(Selection.Targets.Count == expected.Length
                && expected.All(e => Selection.Targets.Any(t => t.SameTarget(e))), "Selected identities differ from the expected exact set");
            foreach (AuthoringRef reference in Selection.Targets)
                Require(Runtime.Resolver.Resolve(reference).Object != null, "Selected target does not resolve");
        }

        private JToken Snapshot() => StudioJson.ToToken(Selection.Capture(GameCore.Studio.Model.SelectionMode.Edit));
        private static JArray Candidates(IEnumerable<PickCandidate> candidates) => new JArray(candidates.Select(c => new JObject
        {
            ["ref"] = StudioJson.ToToken(c.Ref), ["distance"] = c.Distance, ["occluded"] = c.Occluded,
            ["part"] = c.Part, ["source"] = c.Source.ToString(), ["overlapGroup"] = c.OverlapGroup,
        }));

        private void Capture(string name)
        {
            var receipt = new JObject { ["name"] = name, ["source"] = "batch Editor offscreen; no desktop capture",
                ["graphicsDevice"] = SystemInfo.graphicsDeviceType.ToString() };
            if (_window == null)
            {
                receipt["captured"] = false;
                receipt["reason"] = "No graphics device; no synthetic image or PASS substituted";
            }
            else
            {
                _window.Renderer.EnsureTarget(Mathf.RoundToInt(Viewport.width), Mathf.RoundToInt(Viewport.height));
                Require(_window.Renderer.Render(_camera!), "Real viewport render failed");
                RenderTexture target = _window.Texture!;
                RenderTexture previous = RenderTexture.active;
                var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                try
                {
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                    image.Apply();
                    File.WriteAllBytes(Path.Combine(_output, name + "-viewport.png"), image.EncodeToPNG());
                }
                finally
                {
                    RenderTexture.active = previous;
                    Object.DestroyImmediate(image);
                }
                string? problem = UnityWindowCapture.CaptureStudio(Path.Combine(_output, name + "-ui.png"), false);
                receipt["captured"] = problem == null;
                receipt["viewport"] = name + "-viewport.png";
                receipt["ui"] = name + "-ui.png";
                if (problem != null)
                {
                    receipt["problem"] = problem;
                    Block("ui_capture_unavailable: " + problem);
                }
            }
            Record("capture", receipt);
        }

        private void Record(string step, JObject data)
        {
            data["step"] = step;
            data["utc"] = DateTime.UtcNow.ToString("o");
            _observations.Add(data);
            File.WriteAllText(Path.Combine(_output, "observations.json"), _observations.ToString());
        }

        private void Block(string reason)
        {
            if (!_blockers.Contains(reason)) _blockers.Add(reason);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private void Finish(string verdict, string? error)
        {
            EditorApplication.update -= Tick;
            _steps = null;
            try
            {
                if (_window != null) { if (_shown) _window.Close(); else Object.DestroyImmediate(_window); }
                _context?.Dispose();
                _selection?.Dispose();
                _runtime?.Dispose();
                if (_cameraObject != null) Object.DestroyImmediate(_cameraObject);
                if (_originalScenes != null)
                {
                    if (_originalScenes.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(_originalScenes);
                    else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                }
                if (_temporaryAssets.Length > 0) AssetDatabase.DeleteAsset(_temporaryAssets);
            }
            catch (Exception cleanup)
            {
                verdict = "FAIL";
                error = (error ?? string.Empty) + "\nCleanup: " + cleanup;
            }
            File.WriteAllText(Path.Combine(_output, "receipt.json"), new JObject
            {
                ["row"] = _row, ["verdict"] = verdict, ["utc"] = DateTime.UtcNow.ToString("o"),
                ["batchMode"] = Application.isBatchMode, ["graphicsDevice"] = SystemInfo.graphicsDeviceType.ToString(),
                ["blockers"] = new JArray(_blockers), ["error"] = error,
                ["observations"] = "observations.json", ["fixture"] = "Isolated saved scene, real Hollowmere definitions and prefab instances; fixture deleted after run",
                ["paidOperations"] = 0,
            }.ToString());
            Debug.Log("R7-B " + _row + " " + verdict + ": " + _output);
            EditorApplication.Exit(verdict == "PASS" ? 0 : verdict == "BLOCKED" ? 2 : 1);
        }
    }
}

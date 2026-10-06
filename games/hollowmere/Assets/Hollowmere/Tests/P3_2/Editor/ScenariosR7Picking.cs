// R7-D exact W-UI-02/W-UI-03 acceptance drivers. No gateway, provider, or Play Mode is used.
// Invoke ScenariosR7Picking.RunFence or RunLantern in an isolated graphics-enabled Editor, without -quit.
// Receipts require actual creator choices and viewport/GUIView captures; controller-only proof never passes.
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
        private readonly List<string> _blockers = new List<string>();
        private readonly JArray _observations = new JArray();
        private double _deadline;
        private bool _shown;

        private StudioRuntime Runtime => _runtime ?? throw new InvalidOperationException("No picking runtime");
        private SelectionModel Selection => _selection ?? throw new InvalidOperationException("No selection model");
        private Rect Viewport => _window!.ImageRect;

        public void Begin(string row)
        {
            if (_steps != null) throw new InvalidOperationException("A picking driver is already running");
            _row = row;
            _blockers.Clear();
            _observations.Clear();
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            _output = Path.Combine(root, "artifacts/studio/verification", row,
                "r7-d-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ"));
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
            if (!ViewportRenderer.CanRender)
            {
                Block("capture_unavailable: a graphics-enabled Editor is required (batch or the explicitly authorized graphical display :1); -nographics cannot supply real viewport or GUIView captures. No controller-only scenario was substituted.");
                yield break;
            }
            _window = CreateInstance<StudioViewportWindow>();
            _window.UseContext(_context);
            _window.minSize = new Vector2(1280, 720);
            _window.position = new Rect(0, 0, 1280, 720);
            _window.Show();
            _shown = true;
            _window.EnsureGui();
            _window.SetMode(ViewportMode.Select);
            _window.Renderer.ForceFreeCamera = true;
            for (int frame = 0; frame < 120 && !ReadyLayout(); frame++)
            {
                _window.Repaint();
                EditorApplication.QueuePlayerLoopUpdate();
                yield return null;
            }
            Require(ReadyLayout(), "The real Studio viewport never obtained usable layout");
            _camera = _window.Renderer.ChooseCamera();
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
            foreach (object? frame in Capture("01-three-npcs-behind-fence")) yield return frame;
            Rect marquee = BoundsRect(npcs.SelectMany(n => n.GetComponentsInChildren<Renderer>()));
            marquee = Rect.MinMaxRect(marquee.xMin - 3, marquee.yMin - 3, marquee.xMax + 3, marquee.yMax + 3);
            PickResult result = _window!.MarqueeSelect(marquee, SelectionOp.Replace, true);
            foreach (AuthoredEntity npc in npcs) Require(Selection.Targets.Any(t => t.SameTarget(Ref(npc))), "Marquee omitted " + npc.name);
            Require(Selection.RegionRect != null, "Marquee rectangle missing from selection snapshot");
            Record("marquee", new JObject { ["result"] = Candidates(result.Candidates), ["selection"] = Snapshot(),
                ["resultingOverlapVisible"] = _window?.Overlap?.Visible ?? false });
            RegionRect regionRect = Selection.RegionRect!;
            Require(_window.Overlap?.Visible == true && _window.Overlap.panel != null,
                "Marquee must present the real attached result chooser; later point overlap is not a substitute");
            OverlapPopup popup = _window.Overlap!;
            Require(result.Candidates.Any(c => !npcs.Any(n => c.Ref.SameTarget(Ref(n)))),
                "Marquee must include fence geometry to prove its exclusion through the chooser");
            foreach (object? frame in Capture("02-marquee-chooser")) yield return frame;
            for (int i = 0; i < result.Candidates.Count; i++)
            {
                PickCandidate candidate = result.Candidates[i];
                Toggle include = popup.Q<Toggle>("overlap-include-" + i)
                    ?? throw new InvalidOperationException("Real marquee inclusion toggle missing at " + i);
                Require(include.panel != null && include.value, "Marquee candidates must start checked in attached toggles");
                bool keep = npcs.Any(n => candidate.Ref.SameTarget(Ref(n)));
                if (!keep) Submit(include);
                Require(include.value == keep, "Marquee toggle did not retain NPCs and exclude fence geometry");
                Record("marquee-choice", new JObject { ["ref"] = StudioJson.ToToken(candidate.Ref),
                    ["control"] = include.name, ["included"] = include.value, ["creatorInteraction"] = true,
                    ["input"] = keep ? "Initially checked attached Toggle" : "NavigationSubmitEvent sent to attached real Toggle" });
            }
            Button apply = popup.Q<Button>("overlap-apply")
                ?? throw new InvalidOperationException("Real marquee Apply button missing");
            foreach (object? frame in Capture("03-marquee-npc-choices")) yield return frame;
            Submit(apply);
            Require(!popup.Visible, "Marquee Apply did not activate the actual chooser button");
            AssertTargets(npcs.Select(Ref).ToArray());
            Require(Selection.Targets.Count == 3 && !Selection.Targets.Any(t => t.SameTarget(Ref(rail))),
                "Marquee Apply must select exactly the three NPCs, excluding the fence");
            Require(Selection.Parts.Count == 0, "Marquee NPC choices must remain logical targets without mesh parts");
            Require(Selection.RegionRect != null && Selection.RegionRect.Screen.SequenceEqual(regionRect.Screen),
                "Marquee Apply must retain the original selection region rectangle");
            Record("marquee-applied", new JObject { ["creatorInteraction"] = true, ["control"] = apply.name,
                ["input"] = "NavigationSubmitEvent sent to attached real Button", ["selection"] = Snapshot(),
                ["originalRegionRect"] = StudioJson.ToToken(regionRect) });
            foreach (object? frame in Capture("04-marquee-three-npcs-result")) yield return frame;
            foreach (AuthoredEntity npc in npcs)
            {
                Vector2 point = Project(new Vector3(npc.transform.position.x, 1.2f, 1));
                IReadOnlyList<PickCandidate> hits = Click(point);
                PickCandidate candidate = hits.Single(h => h.Ref.SameTarget(Ref(npc)));
                Require(candidate.Occluded, npc.name + " must be occluded by the fence");
                Require(hits.Any(h => h.Ref.SameTarget(Ref(rail)) && h.Distance < candidate.Distance), "Fence must precede " + npc.name);
                Record("point-overlap-" + npc.name, new JObject { ["candidates"] = Candidates(hits) });
                foreach (object? frame in Capture("05-overlap-" + npc.name)) yield return frame;
                Choose(candidate, PickChoice.Logical, hits);
                AssertTargets(Ref(npc));
                Require(Selection.Parts.Count == 0, "Logical NPC choice retained a mesh subpart");
                Record("chosen-" + npc.name, new JObject { ["selection"] = Snapshot() });
                foreach (object? frame in Capture("06-chosen-" + npc.name)) yield return frame;
            }
        }

        private IEnumerator Lantern(Scene scene)
        {
            const string prefabPath = "Assets/Hollowmere/World/Prefabs/Lantern.prefab";
            AuthoredEntity lantern = Place(prefabPath, Load<EntityDefinition>("Assets/Hollowmere/World/Definitions/Lantern.asset"),
                "Lantern", Vector3.zero);
            // Real scenery behind the lantern makes the actual overlap chooser available for every choice.
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
            foreach (object? frame in Capture("01-lantern")) yield return frame;
            Vector2 point = Project(lantern.transform.Find("Body").GetComponent<Renderer>().bounds.center);
            IReadOnlyList<PickCandidate> hits = Click(point);
            PickCandidate body = hits.Single(h => h.Ref.SameTarget(logical) && h.Part == "Mesh:Body");
            Record("lantern-overlap", new JObject { ["candidates"] = Candidates(hits) });
            foreach (object? frame in Capture("02-lantern-chooser")) yield return frame;
            Choose(body, PickChoice.Part, hits);
            AssertTargets(logical);
            Require(Selection.Parts.Count == 1 && Selection.Parts[0].Owner.SameTarget(logical)
                && Selection.Parts[0].Part == "Mesh:Body", "Subpart selection must retain precisely Mesh:Body and its logical lantern owner");
            Record("subpart", new JObject { ["selection"] = Snapshot() });
            foreach (object? frame in Capture("03-lantern-subpart")) yield return frame;
            hits = Click(point);
            body = hits.Single(h => h.Ref.SameTarget(logical) && h.Part == "Mesh:Body");
            foreach (object? frame in Capture("04-lantern-logical-chooser")) yield return frame;
            Choose(body, PickChoice.Logical, hits);
            AssertTargets(logical);
            Require(Selection.Parts.Count == 0, "Logical choice must clear the previous subpart");
            Record("logical", new JObject { ["selection"] = Snapshot() });
            foreach (object? frame in Capture("05-lantern-logical")) yield return frame;
            hits = Click(point);
            body = hits.Single(h => h.Ref.SameTarget(logical) && h.Part == "Mesh:Body");
            foreach (object? frame in Capture("06-lantern-prefab-chooser")) yield return frame;
            Choose(body, PickChoice.Prefab, hits);
            AssertTargets(prefab);
            AuthoringRef selectedPrefab = Selection.Targets[0];
            Require(selectedPrefab.Scope == AuthorScope.Prefab && selectedPrefab.IdentityKey == prefab.IdentityKey
                && selectedPrefab.AssetGuid == AssetDatabase.AssetPathToGUID(prefabPath)
                && selectedPrefab.Path == prefab.Path, "Prefab choice must identify exactly the originating Lantern prefab with Prefab scope");
            Require(Runtime.Resolver.Resolve(selectedPrefab).Object == Runtime.Resolver.Resolve(prefab).Object,
                "Prefab choice must resolve to the same shipped Lantern prefab as the expected reference");
            Require(Selection.Parts.Count == 0, "Prefab choice must not retain a mesh part");
            Record("prefab", new JObject { ["selection"] = Snapshot(), ["expectedPrefab"] = StudioJson.ToToken(prefab) });
            foreach (object? frame in Capture("07-lantern-prefab")) yield return frame;
            // Re-establish a real mesh choice so Instance scope proves that it clears an existing part.
            hits = Click(point);
            body = hits.Single(h => h.Ref.SameTarget(logical) && h.Part == "Mesh:Body");
            Choose(body, PickChoice.Part, hits);
            AssertTargets(logical);
            Require(Selection.Parts.Count == 1 && Selection.Parts[0].Owner.SameTarget(logical)
                && Selection.Parts[0].Part == "Mesh:Body", "Instance-scope setup must select the actual Body part");
            Record("subpart-before-instance-scope", new JObject { ["selection"] = Snapshot() });
            foreach (object? frame in Capture("08-lantern-subpart-before-scope")) yield return frame;
            hits = Click(point);
            body = hits.Single(h => h.Ref.SameTarget(logical) && h.Part == "Mesh:Body");
            foreach (object? frame in Capture("09-lantern-instance-scope-chooser")) yield return frame;
            Choose(body, PickChoice.InstanceScope, hits);
            AssertTargets(instanceScope);
            Require(Selection.Targets[0].Scope == AuthorScope.Instance
                && Selection.Targets[0].SameTarget(logical) && Selection.Targets[0].IdentityKey == logical.IdentityKey,
                "Instance scope must preserve the exact logical Lantern identity with Instance scope");
            Require(Selection.Parts.Count == 0, "Instance scope must clear the previous Body subpart");
            Record("instance-scope", new JObject { ["selection"] = Snapshot(),
                ["expectedInstanceScope"] = StudioJson.ToToken(instanceScope) });
            foreach (object? frame in Capture("10-lantern-instance-scope")) yield return frame;
        }

        private bool ReadyLayout() => _window != null && _window.Area?.panel != null
            && _window.position.width >= 1280 && _window.position.height >= 720
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

        private IReadOnlyList<PickCandidate> Click(Vector2 point) => _window!.ClickAt(point, SelectionOp.Replace);

        private void Choose(PickCandidate candidate, PickChoice choice, IReadOnlyList<PickCandidate> hits)
        {
            OverlapPopup popup = _window!.Overlap!;
            Require(popup.Visible && popup.panel != null, "The real attached overlap popup must be visible before choosing");
            int index = hits.ToList().IndexOf(candidate);
            Require(index >= 0, "Chosen candidate must belong to the displayed picking result");
            string prefix = choice == PickChoice.Part ? "overlap-part-"
                : choice == PickChoice.Prefab ? "overlap-prefab-"
                : choice == PickChoice.InstanceScope ? "overlap-scope-" : "overlap-";
            Button button = popup.Q<Button>(prefix + index)
                ?? throw new InvalidOperationException("Real overlap choice button missing: " + prefix + index);
            Submit(button);
            Require(!popup.Visible, "UI Toolkit submit did not activate the actual chooser button");
            Record("creator-button", new JObject { ["creatorInteraction"] = true, ["text"] = button.text,
                ["control"] = button.name, ["choice"] = choice.ToString(), ["candidate"] = StudioJson.ToToken(candidate.Ref),
                ["selection"] = Snapshot(), ["input"] = "NavigationSubmitEvent sent to attached real Button" });
        }

        private static void Submit(VisualElement control)
        {
            Require(control.panel != null && control.enabledInHierarchy,
                "Creator input requires an enabled control attached to the real panel: " + control.name);
            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = control;
                control.SendEvent(submit);
            }
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

        private IEnumerable<object?> Capture(string name)
        {
            Require(_window != null && _window.rootVisualElement.panel != null,
                "Evidence requires the real attached Studio window; no synthetic capture is permitted");
            double repaintUntil = EditorApplication.timeSinceStartup + 0.2;
            int repaintFrames = 0;
            while (repaintFrames < 6 || EditorApplication.timeSinceStartup < repaintUntil)
            {
                _window!.Repaint();
                _window.rootVisualElement.MarkDirtyRepaint();
                EditorApplication.QueuePlayerLoopUpdate();
                repaintFrames++;
                yield return null;
            }
            var receipt = new JObject { ["name"] = name,
                ["source"] = "Studio viewport RenderTexture readback and Studio Editor GUIView.GrabPixels window composite; no desktop capture",
                ["batchMode"] = Application.isBatchMode, ["display"] = Environment.GetEnvironmentVariable("DISPLAY"),
                ["repaintFrames"] = repaintFrames, ["graphicsDevice"] = SystemInfo.graphicsDeviceType.ToString() };
            _window!.Renderer.EnsureTarget(Mathf.RoundToInt(Viewport.width), Mathf.RoundToInt(Viewport.height));
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
                ["display"] = Environment.GetEnvironmentVariable("DISPLAY"),
                ["blockers"] = new JArray(_blockers), ["error"] = error,
                ["observations"] = "observations.json", ["fixture"] = "Isolated saved scene, real Hollowmere definitions and prefab instances; fixture deleted after run",
                ["paidOperations"] = 0,
            }.ToString());
            Debug.Log("R7-D " + _row + " " + verdict + ": " + _output);
            EditorApplication.Exit(verdict == "PASS" ? 0 : verdict == "BLOCKED" ? 2 : 1);
        }
    }
}

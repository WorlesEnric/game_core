// GameCore.Studio.Authoring - picking over a provided camera and viewport rectangle (docs/studio/03-authoring-contracts.md
// s2, SADR-007).
//
// Pick: UI Toolkit panel.Pick on runtime UIDocuments (overlay, distance 0), Physics.RaycastAll on colliders,
// renderer-bounds ray tests on renderers without a collider, then a ground-plane location (snapped to the NavMesh when
// one is present). World hits are depth-ordered; a hit is occluded when a different logical object lies in front of
// it by more than the overlap band (0.5 % of depth), and hits within the band share an overlap group. Every hit maps
// to its logical owner (the nearest authored component up the hierarchy) and names the part that was hit.
//
// Marquee: renderer bounds culled by the camera frustum, then their projected screen rectangles tested against the
// marquee (intersection, or full containment of every renderer of the owner).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Model;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;

namespace GameCore.Studio.Authoring
{
    /// <summary>The picking service over one camera and the rectangle its image is shown in.</summary>
    public sealed class PickingService : IPickingService
    {
        private readonly IAuthoringRefResolver _resolver;
        private readonly AuthoringIdentity _identity;
        private readonly IRegionLocator _regions;

        public PickingService(Camera camera, Rect viewport, IAuthoringRefResolver resolver, AuthoringIdentity? identity = null, IRegionLocator? regions = null, PickOptions? options = null)
        {
            Camera = camera != null ? camera : throw new ArgumentNullException(nameof(camera));
            Viewport = viewport;
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _identity = identity ?? new AuthoringIdentity();
            _regions = regions ?? new SceneRegionLocator();
            Options = options ?? new PickOptions();
        }

        /// <summary>The game camera whose image is shown in <see cref="Viewport"/>.</summary>
        public Camera Camera { get; set; }

        /// <summary>Where the camera image is shown, in the caller's pixel space (origin top-left).</summary>
        public Rect Viewport { get; set; }

        public PickOptions Options { get; }

        /// <summary>Timings of the most recent query.</summary>
        public PickTimings? LastTimings { get; private set; }

        /// <summary>Viewport coordinates (0..1, origin bottom-left, as Camera.ViewportPointToRay expects) of a point.</summary>
        public Vector2 ToViewport(Vector2 screenPoint)
        {
            float u = Viewport.width <= 0f ? 0f : (screenPoint.x - Viewport.x) / Viewport.width;
            float v = Viewport.height <= 0f ? 0f : 1f - ((screenPoint.y - Viewport.y) / Viewport.height);
            return new Vector2(u, v);
        }

        /// <summary>The caller-space point of viewport coordinates.</summary>
        public Vector2 FromViewport(Vector3 viewportPoint)
        {
            return new Vector2(Viewport.x + (viewportPoint.x * Viewport.width), Viewport.y + ((1f - viewportPoint.y) * Viewport.height));
        }

        /// <summary>The camera ray through a caller-space point.</summary>
        public Ray RayAt(Vector2 screenPoint)
        {
            Vector2 uv = ToViewport(screenPoint);
            return Camera.ViewportPointToRay(new Vector3(uv.x, uv.y, 0f));
        }

        public PickResult Pick(Vector2 screenPoint)
        {
            PickTimings timings = new PickTimings();
            System.Diagnostics.Stopwatch total = System.Diagnostics.Stopwatch.StartNew();
            System.Diagnostics.Stopwatch step = System.Diagnostics.Stopwatch.StartNew();
            List<PickCandidate> result = new List<PickCandidate>();
            if (!Viewport.Contains(screenPoint))
            {
                timings.TotalMs = total.Elapsed.TotalMilliseconds;
                LastTimings = timings;
                return new PickResult(result, timings);
            }

            Vector2 uv = ToViewport(screenPoint);
            Ray ray = Camera.ViewportPointToRay(new Vector3(uv.x, uv.y, 0f));
            float maxDistance = MaxDistance();
            double resolveMs = 0;

            List<UiHit> uiHits = new List<UiHit>();
            if (Options.IncludeUi)
            {
                CollectUi(uv, uiHits);
            }

            timings.UiMs = Lap(step);

            List<WorldHit> worldHits = new List<WorldHit>();
            Physics.SyncTransforms();
            RaycastHit[] physicsHits = Physics.RaycastAll(ray, maxDistance, Options.LayerMask, Options.Triggers);
            foreach (RaycastHit hit in physicsHits)
            {
                if (hit.collider == null || IsStudioInternal(hit.collider.gameObject))
                {
                    continue;
                }

                worldHits.Add(new WorldHit(hit.collider.gameObject, hit.distance, hit.point, PickSource.Physics));
            }

            timings.PhysicsMs = Lap(step);

            Plane[] frustum = GeometryUtility.CalculateFrustumPlanes(Camera);
            foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!IsPickableRenderer(renderer) || HasSolidCollider(renderer.gameObject))
                {
                    continue;
                }

                Bounds bounds = renderer.bounds;
                if (!GeometryUtility.TestPlanesAABB(frustum, bounds))
                {
                    continue;
                }

                if (bounds.IntersectRay(ray, out float distance) && distance <= maxDistance)
                {
                    worldHits.Add(new WorldHit(renderer.gameObject, distance, ray.GetPoint(distance), PickSource.Renderer));
                }
            }

            timings.RendererMs = Lap(step);

            PickCandidate? ground = null;
            if (Options.IncludeGroundCandidate)
            {
                ground = GroundCandidate(ray, maxDistance);
            }

            timings.GroundMs = Lap(step);

            uiHits.Sort((left, right) => right.SortingOrder.CompareTo(left.SortingOrder));
            foreach (UiHit hit in uiHits)
            {
                PickCandidate? candidate = BuildUiCandidate(hit);
                if (candidate != null)
                {
                    result.Add(candidate);
                }
            }

            worldHits.Sort((left, right) => left.Distance.CompareTo(right.Distance));
            List<PickCandidate> world = BuildWorldCandidates(worldHits);
            result.AddRange(world);
            if (ground != null)
            {
                result.Add(ground);
            }

            resolveMs += Lap(step);
            timings.ResolveMs = resolveMs;
            timings.TotalMs = total.Elapsed.TotalMilliseconds;
            LastTimings = timings;
            return new PickResult(result, timings);
        }

        public PickResult Marquee(Rect screenRect, bool requireFullContainment = false)
        {
            PickTimings timings = new PickTimings();
            System.Diagnostics.Stopwatch total = System.Diagnostics.Stopwatch.StartNew();
            System.Diagnostics.Stopwatch step = System.Diagnostics.Stopwatch.StartNew();
            Rect rect = Rect.MinMaxRect(
                Mathf.Min(screenRect.xMin, screenRect.xMax),
                Mathf.Min(screenRect.yMin, screenRect.yMax),
                Mathf.Max(screenRect.xMin, screenRect.xMax),
                Mathf.Max(screenRect.yMin, screenRect.yMax));

            Plane[] frustum = GeometryUtility.CalculateFrustumPlanes(Camera);
            Dictionary<UnityEngine.Object, MarqueeOwner> owners = new Dictionary<UnityEngine.Object, MarqueeOwner>();
            List<UnityEngine.Object> order = new List<UnityEngine.Object>();
            foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!IsPickableRenderer(renderer))
                {
                    continue;
                }

                MonoBehaviour? logical = _identity.FindLogicalOwner(renderer.transform);
                UnityEngine.Object owner = logical != null ? (UnityEngine.Object)logical : renderer.gameObject;
                if (!owners.TryGetValue(owner, out MarqueeOwner? accumulator))
                {
                    accumulator = new MarqueeOwner(logical != null ? logical.gameObject : renderer.gameObject);
                    owners.Add(owner, accumulator);
                    order.Add(owner);
                }

                accumulator.Total++;
                Bounds bounds = renderer.bounds;
                float distance = Vector3.Distance(Camera.transform.position, bounds.center);
                if (distance < accumulator.Distance)
                {
                    accumulator.Distance = distance;
                    accumulator.Point = bounds.center;
                }

                if (!GeometryUtility.TestPlanesAABB(frustum, bounds))
                {
                    continue;
                }

                ProjectBounds(bounds, out Rect projected, out bool anyFront, out bool allFront);
                if (anyFront && projected.Overlaps(rect))
                {
                    accumulator.Intersects = true;
                }

                if (allFront && rect.Contains(projected.min) && rect.Contains(projected.max))
                {
                    accumulator.Contained++;
                }
            }

            timings.RendererMs = Lap(step);
            List<KeyValuePair<UnityEngine.Object, MarqueeOwner>> selected = new List<KeyValuePair<UnityEngine.Object, MarqueeOwner>>();
            foreach (UnityEngine.Object owner in order)
            {
                MarqueeOwner accumulator = owners[owner];
                bool take = requireFullContainment ? accumulator.Total > 0 && accumulator.Contained == accumulator.Total : accumulator.Intersects;
                if (take)
                {
                    selected.Add(new KeyValuePair<UnityEngine.Object, MarqueeOwner>(owner, accumulator));
                }
            }

            selected.Sort((left, right) => left.Value.Distance.CompareTo(right.Value.Distance));
            List<PickCandidate> result = new List<PickCandidate>(selected.Count);
            foreach (KeyValuePair<UnityEngine.Object, MarqueeOwner> pair in selected)
            {
                AuthoringRef? target = _resolver.BuildRef(pair.Key);
                if (target != null)
                {
                    result.Add(new PickCandidate(target, pair.Key, pair.Value.GameObject, null, pair.Value.Distance, false, PickSource.Marquee, 0, pair.Value.Point));
                }
            }

            timings.ResolveMs = Lap(step);
            timings.TotalMs = total.Elapsed.TotalMilliseconds;
            LastTimings = timings;
            return new PickResult(result, timings);
        }

        public LocationPick PointAt(Vector2 screenPoint)
        {
            PickTimings timings = new PickTimings();
            System.Diagnostics.Stopwatch total = System.Diagnostics.Stopwatch.StartNew();
            Ray ray = RayAt(screenPoint);
            float maxDistance = MaxDistance();
            Vector3 position;
            Vector3 normal;
            PickSource source;
            GameObject? surface = null;
            Physics.SyncTransforms();
            if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, Options.LayerMask, QueryTriggerInteraction.Ignore) && !IsStudioInternal(hit.collider.gameObject))
            {
                position = hit.point;
                normal = hit.normal;
                source = PickSource.Physics;
                surface = hit.collider.gameObject;
            }
            else if (GroundPlaneHit(ray, maxDistance, out float enter))
            {
                position = ray.GetPoint(enter);
                normal = Vector3.up;
                source = PickSource.Ground;
            }
            else
            {
                timings.GroundMs = total.Elapsed.TotalMilliseconds;
                timings.TotalMs = timings.GroundMs;
                LastTimings = timings;
                return new LocationPick(null, Vector3.zero, Vector3.up, PickSource.Ground, null, timings);
            }

            if (NavMesh.SamplePosition(position, out NavMeshHit navHit, Options.NavMeshSampleDistance, NavMesh.AllAreas))
            {
                position = navHit.position;
                normal = navHit.normal.sqrMagnitude > 0f ? navHit.normal : normal;
                source = PickSource.NavMesh;
            }

            timings.GroundMs = total.Elapsed.TotalMilliseconds;
            AuthoringRef location = BuildLocation(position, normal, surface);
            timings.TotalMs = total.Elapsed.TotalMilliseconds;
            LastTimings = timings;
            return new LocationPick(location, position, normal, source, surface, timings);
        }

        public StaleReport Validate(SelectionSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            List<StaleEntry> entries = new List<StaleEntry>();
            List<AuthoringRef> refs = new List<AuthoringRef>(snapshot.Targets);
            if (snapshot.Parts != null)
            {
                foreach (PartRef part in snapshot.Parts)
                {
                    refs.Add(part.Owner);
                }
            }

            List<string> seen = new List<string>();
            foreach (AuthoringRef target in refs)
            {
                if (target.Kind == AuthoringKind.Location || seen.Contains(target.IdentityKey))
                {
                    continue;
                }

                seen.Add(target.IdentityKey);
                ResolveResult resolved = _resolver.Resolve(target);
                entries.AddRange(resolved.Stale);
                if (!resolved.Resolved && resolved.Stale.Count == 0)
                {
                    entries.Add(StaleEntry.Create(target, StaleReason.Destroyed));
                }
            }

            return entries.Count == 0 ? StaleReport.Empty : new StaleReport(entries);
        }

        private float MaxDistance() => Options.MaxDistance > 0f ? Options.MaxDistance : Camera.farClipPlane;

        private static double Lap(System.Diagnostics.Stopwatch step)
        {
            double elapsed = step.Elapsed.TotalMilliseconds;
            step.Restart();
            return elapsed;
        }

        private bool IsPickableRenderer(Renderer renderer)
        {
            if (renderer == null || !renderer.enabled || IsStudioInternal(renderer.gameObject))
            {
                return false;
            }

            return (Camera.cullingMask & (1 << renderer.gameObject.layer)) != 0;
        }

        /// <summary>Studio staging/preview objects are never pickable.</summary>
        private static bool IsStudioInternal(GameObject gameObject)
        {
            return (gameObject.hideFlags & (HideFlags.DontSave | HideFlags.HideInHierarchy)) != 0;
        }

        private bool HasSolidCollider(GameObject gameObject)
        {
            Collider[] colliders = gameObject.GetComponents<Collider>();
            foreach (Collider collider in colliders)
            {
                if (collider != null && collider.enabled && (Options.Triggers == QueryTriggerInteraction.Collide || !collider.isTrigger)
                    && (Options.LayerMask & (1 << gameObject.layer)) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private List<PickCandidate> BuildWorldCandidates(List<WorldHit> hits)
        {
            List<PickCandidate> candidates = new List<PickCandidate>();
            List<string> seen = new List<string>();
            List<string> ownerKeys = new List<string>();
            float fraction = Mathf.Max(0f, Options.OverlapDepthFraction);
            int group = -1;
            float groupStart = 0f;
            foreach (WorldHit hit in hits)
            {
                MonoBehaviour? logical = _identity.FindLogicalOwner(hit.GameObject.transform);
                UnityEngine.Object owner = logical != null ? (UnityEngine.Object)logical : hit.GameObject;
                string? part = logical != null && logical.gameObject == hit.GameObject ? null : (logical == null ? null : PartName(hit.GameObject));
                string key = owner.GetInstanceID().ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + (part ?? string.Empty);
                if (seen.Contains(key))
                {
                    continue;
                }

                AuthoringRef? target = _resolver.BuildRef(owner);
                if (target == null)
                {
                    continue;
                }

                seen.Add(key);
                string ownerKey = target.IdentityKey;
                bool occluded = false;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (!string.Equals(ownerKeys[i], ownerKey, StringComparison.Ordinal)
                        && candidates[i].Distance < hit.Distance - Band(hit.Distance, fraction))
                    {
                        occluded = true;
                        break;
                    }
                }

                if (group < 0 || hit.Distance > groupStart + Band(groupStart, fraction))
                {
                    group++;
                    groupStart = hit.Distance;
                }

                candidates.Add(new PickCandidate(target, owner, hit.GameObject, part, hit.Distance, occluded, hit.Source, group, hit.Point));
                ownerKeys.Add(ownerKey);
            }

            return candidates;
        }

        private static float Band(float depth, float fraction) => Mathf.Max(depth * fraction, 1e-4f);

        private static string PartName(GameObject gameObject)
        {
            if (gameObject.GetComponent<Renderer>() != null)
            {
                return "Mesh:" + gameObject.name;
            }

            if (gameObject.GetComponent<Collider>() != null)
            {
                return "Collider:" + gameObject.name;
            }

            return gameObject.name;
        }

        private PickCandidate? GroundCandidate(Ray ray, float maxDistance)
        {
            if (!GroundPlaneHit(ray, maxDistance, out float enter))
            {
                return null;
            }

            Vector3 position = ray.GetPoint(enter);
            Vector3 normal = Vector3.up;
            PickSource source = PickSource.Ground;
            if (NavMesh.SamplePosition(position, out NavMeshHit navHit, Options.NavMeshSampleDistance, NavMesh.AllAreas))
            {
                position = navHit.position;
                normal = navHit.normal.sqrMagnitude > 0f ? navHit.normal : normal;
                source = PickSource.NavMesh;
            }

            return new PickCandidate(BuildLocation(position, normal, null), null, null, null, enter, false, source, int.MaxValue, position);
        }

        private bool GroundPlaneHit(Ray ray, float maxDistance, out float enter)
        {
            Plane ground = new Plane(Vector3.up, new Vector3(0f, Options.GroundHeight, 0f));
            if (ground.Raycast(ray, out enter) && enter >= 0f && enter <= maxDistance)
            {
                return true;
            }

            enter = 0f;
            return false;
        }

        private AuthoringRef BuildLocation(Vector3 position, Vector3 normal, GameObject? hint)
        {
            string region = _regions.RegionAt(position, hint);
            return new AuthoringRef(
                AuthoringKind.Location,
                location: new LocationRef(
                    string.IsNullOrEmpty(region) ? SceneRegionLocator.FallbackRegion : region,
                    new double[] { Round(position.x), Round(position.y), Round(position.z) },
                    new double[] { Round(normal.x), Round(normal.y), Round(normal.z) }));
        }

        /// <summary>Millimetre precision keeps location refs stable across float noise (positions are mm in slots).</summary>
        private static double Round(float value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);

        private void ProjectBounds(Bounds bounds, out Rect projected, out bool anyFront, out bool allFront)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            float xMin = float.MaxValue;
            float yMin = float.MaxValue;
            float xMax = float.MinValue;
            float yMax = float.MinValue;
            anyFront = false;
            allFront = true;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
                Vector3 viewport = Camera.WorldToViewportPoint(corner);
                if (viewport.z <= 0f)
                {
                    allFront = false;
                    continue;
                }

                anyFront = true;
                Vector2 point = FromViewport(viewport);
                xMin = Mathf.Min(xMin, point.x);
                yMin = Mathf.Min(yMin, point.y);
                xMax = Mathf.Max(xMax, point.x);
                yMax = Mathf.Max(yMax, point.y);
            }

            projected = anyFront ? Rect.MinMaxRect(xMin, yMin, xMax, yMax) : Rect.zero;
        }

        private void CollectUi(Vector2 uv, List<UiHit> hits)
        {
            foreach (UIDocument document in UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (document == null || !document.isActiveAndEnabled || IsStudioInternal(document.gameObject))
                {
                    continue;
                }

                VisualElement? root = document.rootVisualElement;
                IPanel? panel = root?.panel;
                if (root == null || panel == null)
                {
                    continue;
                }

                Vector2 size = PanelSize(document, panel);
                if (size.x <= 0f || size.y <= 0f)
                {
                    continue;
                }

                Vector2 panelPoint = new Vector2(uv.x * size.x, (1f - uv.y) * size.y);
                VisualElement? picked = panel.Pick(panelPoint);
                if (picked == null || IsAncestorOrSelf(picked, root))
                {
                    continue;
                }

                hits.Add(new UiHit(document, root, picked, document.sortingOrder));
            }
        }

        private Vector2 PanelSize(UIDocument document, IPanel panel)
        {
            Rect layout = panel.visualTree.layout;
            if (!float.IsNaN(layout.width) && !float.IsNaN(layout.height) && layout.width > 0f && layout.height > 0f)
            {
                return new Vector2(layout.width, layout.height);
            }

            RenderTexture? target = document.panelSettings != null ? document.panelSettings.targetTexture : null;
            if (target != null)
            {
                return new Vector2(target.width, target.height);
            }

            return new Vector2(Camera.pixelWidth, Camera.pixelHeight);
        }

        private static bool IsAncestorOrSelf(VisualElement candidate, VisualElement element)
        {
            for (VisualElement? current = element; current != null; current = current.parent)
            {
                if (current == candidate)
                {
                    return true;
                }
            }

            return false;
        }

        private PickCandidate? BuildUiCandidate(UiHit hit)
        {
            MonoBehaviour? logical = _identity.FindLogicalOwner(hit.Document.transform);
            UnityEngine.Object owner = logical != null ? (UnityEngine.Object)logical : hit.Document;
            AuthoringRef? documentRef = _resolver.BuildRef(owner);
            if (documentRef == null)
            {
                return null;
            }

            string elementPath = ElementPath(hit.Element, hit.Root);
            AuthoringRef target = new AuthoringRef(
                AuthoringKind.UiElement,
                documentRef.AuthoringId,
                documentRef.Global,
                documentRef.AssetGuid,
                (documentRef.Path ?? hit.Document.gameObject.name) + "#ui/" + elementPath,
                documentRef.Definition,
                documentRef.Scope,
                documentRef.Stamp);
            return new PickCandidate(target, owner, hit.Document.gameObject, "Ui:" + elementPath, 0f, false, PickSource.Ui, -1, hit.Document.transform.position);
        }

        private static string ElementPath(VisualElement element, VisualElement root)
        {
            List<string> names = new List<string>();
            for (VisualElement? current = element; current != null && current != root; current = current.parent)
            {
                names.Insert(0, string.IsNullOrEmpty(current.name) ? current.GetType().Name : current.name);
            }

            return string.Join("/", names);
        }

        private sealed class WorldHit
        {
            public WorldHit(GameObject gameObject, float distance, Vector3 point, PickSource source)
            {
                GameObject = gameObject;
                Distance = distance;
                Point = point;
                Source = source;
            }

            public GameObject GameObject { get; }

            public float Distance { get; }

            public Vector3 Point { get; }

            public PickSource Source { get; }
        }

        private sealed class UiHit
        {
            public UiHit(UIDocument document, VisualElement root, VisualElement element, float sortingOrder)
            {
                Document = document;
                Root = root;
                Element = element;
                SortingOrder = sortingOrder;
            }

            public UIDocument Document { get; }

            public VisualElement Root { get; }

            public VisualElement Element { get; }

            public float SortingOrder { get; }
        }

        private sealed class MarqueeOwner
        {
            public MarqueeOwner(GameObject gameObject)
            {
                GameObject = gameObject;
            }

            public GameObject GameObject { get; }

            public int Total { get; set; }

            public int Contained { get; set; }

            public bool Intersects { get; set; }

            public float Distance { get; set; } = float.MaxValue;

            public Vector3 Point { get; set; }
        }
    }
}

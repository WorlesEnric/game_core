// GameCore.Studio.UI - picking gestures to selection changes (03 s2, SADR-007, B-SELECT), UI-free so tests drive it over
// a fixture scene: click (nearest candidate, shift-add, ctrl-toggle, empty ground clears), the overlap candidates
// (center plus four samples at the overlap radius, merged by target and part, UI first then depth, ground excluded),
// marquee (partial or full containment, the rectangle kept in the snapshot), point-at (ground/NavMesh Location) and hover.
// Every query's Stopwatch total is recorded in SelectTimings.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.UI
{
    /// <summary>The identity or subpart requested by the creator.</summary>
    public enum PickChoice { Logical, Part, Prefab, InstanceScope }

    /// <summary>Turns picks into selection changes.</summary>
    public sealed class ViewportPicker
    {
        private readonly SelectionModel _selection;
        private readonly Func<PickingService> _picking;
        private readonly Func<int> _overlapRadius;
        private readonly RuntimeViewMapper? _mapper;

        /// <param name="mapper">Re-targets picks on runtime views (Play mode) to their authored objects; null keeps picks as they are.</param>
        public ViewportPicker(SelectionModel selection, Func<PickingService> picking, SelectTimings timings, Func<int>? overlapRadius = null, RuntimeViewMapper? mapper = null)
        {
            _mapper = mapper;
            _selection = selection ?? throw new ArgumentNullException(nameof(selection));
            _picking = picking ?? throw new ArgumentNullException(nameof(picking));
            Timings = timings ?? throw new ArgumentNullException(nameof(timings));
            _overlapRadius = overlapRadius ?? (() => 4);
        }

        public SelectTimings Timings { get; }

        public SelectionModel Selection => _selection;

        /// <summary>Candidates under a point and within the overlap radius, merged, UI first then nearest first, no ground.</summary>
        public IReadOnlyList<PickCandidate> CandidatesAt(Vector2 point, out PickResult center)
        {
            PickingService picking = _picking();
            center = picking.Pick(point);
            Timings.Add("click", center.Timings.TotalMs);
            List<PickCandidate> merged = new List<PickCandidate>();
            List<string> keys = new List<string>();
            Merge(center, merged, keys, _mapper);
            float radius = _overlapRadius();
            if (radius > 0f)
            {
                Vector2[] offsets = { new Vector2(radius, 0f), new Vector2(-radius, 0f), new Vector2(0f, radius), new Vector2(0f, -radius) };
                foreach (Vector2 offset in offsets)
                {
                    Merge(picking.Pick(point + offset), merged, keys, _mapper);
                }
            }

            merged.Sort((left, right) =>
            {
                int ui = (left.Source == PickSource.Ui ? 0 : 1).CompareTo(right.Source == PickSource.Ui ? 0 : 1);
                return ui != 0 ? ui : left.Distance.CompareTo(right.Distance);
            });
            return merged;
        }

        /// <summary>
        /// A click: the nearest candidate is selected with <paramref name="op"/>; empty space clears (Replace). Returns the
        /// candidates; the caller shows the overlap list when there are two or more.
        /// </summary>
        public IReadOnlyList<PickCandidate> Click(Vector2 point, SelectionOp op)
        {
            IReadOnlyList<PickCandidate> candidates = CandidatesAt(point, out PickResult _);
            if (candidates.Count == 0)
            {
                if (op == SelectionOp.Replace)
                {
                    _selection.Set(Array.Empty<AuthoringRef>(), SelectionOp.Replace);
                }

                return candidates;
            }

            Choose(candidates[0], false, op);
            return candidates;
        }

        /// <summary>Selects one candidate (from a click or the overlap list); <paramref name="part"/> keeps the picked part.</summary>
        public void Choose(PickCandidate candidate, bool part, SelectionOp op)
            => Choose(candidate, part ? PickChoice.Part : PickChoice.Logical, op);

        public void Choose(PickCandidate candidate, PickChoice choice, SelectionOp op)
        {
            if (!TryResolveChoice(candidate, choice, out AuthoringRef target)) return;
            PartRef[]? parts = choice == PickChoice.Part && candidate.Part != null
                ? new[] { new PartRef(target, candidate.Part) } : null;
            _selection.Set(new[] { target }, op, null, parts);
        }

        public bool TryResolveChoice(PickCandidate candidate, PickChoice choice, out AuthoringRef target)
        {
            candidate = _mapper?.Map(candidate) ?? candidate;
            target = candidate.Ref;
            if (candidate.Source == PickSource.Ground || candidate.Source == PickSource.NavMesh) return false;
            if (choice == PickChoice.Logical || choice == PickChoice.Part) return true;
            UnityEngine.Object? owner = _selection.Runtime.Resolver.Find(target);
            GameObject? go = owner is Component component ? component.gameObject : owner as GameObject;
            if (go == null || EditorUtility.IsPersistent(go)) return false;
            if (choice == PickChoice.InstanceScope)
            {
                target = target.WithScope(AuthorScope.Instance);
                return true;
            }
            if (choice != PickChoice.Prefab) return false;
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            GameObject? prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            AuthoringRef? reference = prefab != null ? _selection.Runtime.Resolver.BuildRef(prefab, AuthorScope.Prefab) : null;
            if (reference == null) return false;
            target = reference;
            return true;
        }

        /// <summary>Marquee bounds carry no occlusion flags; query engine hits at each renderer silhouette.</summary>
        public bool HasMarqueeOverlap(IReadOnlyList<PickCandidate> candidates)
        {
            PickingService picking = _picking();
            var rectangles = new List<Rect>();
            foreach (PickCandidate candidate in candidates)
            {
                GameObject? go = candidate.Owner is Component component ? component.gameObject : candidate.Owner as GameObject;
                if (go == null) continue;
                Rect projected = default;
                bool any = false;
                foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>())
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    Bounds bounds = renderer.bounds;
                    Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
                    Vector2 max = new Vector2(float.MinValue, float.MinValue);
                    for (int corner = 0; corner < 8; corner++)
                    {
                        Vector3 point = picking.Camera.WorldToViewportPoint(bounds.center + Vector3.Scale(bounds.extents,
                            new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                        if (point.z <= 0) continue;
                        Vector2 screen = picking.FromViewport(point);
                        min = Vector2.Min(min, screen);
                        max = Vector2.Max(max, screen);
                    }
                    if (min.x > max.x) continue;
                    Rect rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                    projected = any ? Rect.MinMaxRect(Mathf.Min(projected.xMin, rect.xMin), Mathf.Min(projected.yMin, rect.yMin),
                        Mathf.Max(projected.xMax, rect.xMax), Mathf.Max(projected.yMax, rect.yMax)) : rect;
                    any = true;
                    PickResult hits = picking.Pick(rect.center);
                    foreach (PickCandidate hit in hits.Candidates)
                        if (hit.Occluded && hit.Ref.SameTarget(candidate.Ref)) return true;
                }
                if (!any) continue;
                foreach (Rect other in rectangles) if (other.Overlaps(projected)) return true;
                rectangles.Add(projected);
            }
            return false;
        }

        public void ApplyMarqueeChoices(IReadOnlyList<PickCandidate> candidates, Rect rect, SelectionOp op,
            IReadOnlyList<AuthoringRef> before, IReadOnlyList<PartRef> beforeParts)
        {
            var targets = op == SelectionOp.Replace ? new List<AuthoringRef>() : new List<AuthoringRef>(before);
            var parts = op == SelectionOp.Replace ? new List<PartRef>() : new List<PartRef>(beforeParts);
            foreach (PickCandidate candidate in candidates)
            {
                AuthoringRef target = (_mapper?.Map(candidate) ?? candidate).Ref;
                if (target.Kind == AuthoringKind.Location) continue;
                int index = targets.FindIndex(existing => existing.SameTarget(target));
                if (op == SelectionOp.Toggle && index >= 0)
                {
                    targets.RemoveAt(index);
                    parts.RemoveAll(part => part.Owner.SameTarget(target));
                }
                else if (index < 0) targets.Add(target);
            }
            _selection.Set(targets, SelectionOp.Replace, new RegionRect(new double[] { rect.xMin, rect.yMin, rect.xMax, rect.yMax }), parts);
        }

        /// <summary>A marquee over a viewport rectangle (the rectangle is kept as the snapshot's regionRect).</summary>
        public PickResult Marquee(Rect rect, SelectionOp op, bool fullContainment)
        {
            PickResult result = _picking().Marquee(rect, fullContainment);
            Timings.Add("marquee", result.Timings.TotalMs);
            List<AuthoringRef> targets = new List<AuthoringRef>();
            foreach (PickCandidate candidate in result.Candidates)
            {
                AuthoringRef target = (_mapper?.Map(candidate) ?? candidate).Ref;
                if (target.Kind != AuthoringKind.Location && !targets.Exists(existing => existing.SameTarget(target)))
                {
                    targets.Add(target);
                }
            }

            _selection.Set(targets, op, new RegionRect(new double[] { rect.xMin, rect.yMin, rect.xMax, rect.yMax }));
            return result;
        }

        /// <summary>Point-at: the ground (NavMesh when present) location becomes the selection's location.</summary>
        public LocationPick PointAt(Vector2 point)
        {
            LocationPick location = _picking().PointAt(point);
            Timings.Add("pointAt", location.Timings.TotalMs);
            if (location.Hit)
            {
                _selection.SetLocation(location.Location);
            }

            return location;
        }

        /// <summary>The nearest non-ground candidate under a point (hover).</summary>
        public PickCandidate? Hover(Vector2 point)
        {
            PickResult result = _picking().Pick(point);
            Timings.Add("hover", result.Timings.TotalMs);
            foreach (PickCandidate candidate in result.Candidates)
            {
                if (candidate.Source != PickSource.Ground && candidate.Source != PickSource.NavMesh)
                {
                    return _mapper?.Map(candidate) ?? candidate;
                }
            }

            return null;
        }

        private static void Merge(PickResult result, List<PickCandidate> merged, List<string> keys, RuntimeViewMapper? mapper)
        {
            foreach (PickCandidate picked in result.Candidates)
            {
                if (picked.Source == PickSource.Ground || picked.Source == PickSource.NavMesh)
                {
                    continue;
                }

                PickCandidate candidate = mapper?.Map(picked) ?? picked;
                string key = candidate.Ref.IdentityKey + "|" + (candidate.Part ?? string.Empty);
                int index = keys.IndexOf(key);
                if (index < 0)
                {
                    keys.Add(key);
                    merged.Add(candidate);
                }
                else if (candidate.Distance < merged[index].Distance)
                {
                    merged[index] = candidate;
                }
            }
        }
    }
}

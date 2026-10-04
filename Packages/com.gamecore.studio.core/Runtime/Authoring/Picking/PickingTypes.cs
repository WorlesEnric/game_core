// GameCore.Studio.Authoring - picking results (docs/studio/03-authoring-contracts.md s2, SADR-007).
// Candidates come from engine data only (physics, renderer bounds, UI Toolkit panels, ground/NavMesh sampling);
// vision is never a selection source.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Studio.Model;
using UnityEngine;

namespace GameCore.Studio.Authoring
{
    /// <summary>Which engine query produced a candidate.</summary>
    public enum PickSource
    {
        /// <summary>A runtime UI Toolkit panel (<c>IPanel.Pick</c>).</summary>
        Ui,
        /// <summary>A collider hit (<c>Physics.RaycastAll</c>).</summary>
        Physics,
        /// <summary>A renderer-bounds ray hit on an object without a collider.</summary>
        Renderer,
        /// <summary>The ground plane.</summary>
        Ground,
        /// <summary>A ground sample snapped to the NavMesh.</summary>
        NavMesh,
        /// <summary>A marquee (frustum) selection.</summary>
        Marquee,
    }

    /// <summary>One picking candidate (03 s2): the logical object, the picked part, depth and occlusion.</summary>
    public sealed class PickCandidate
    {
        public PickCandidate(AuthoringRef target, UnityEngine.Object? owner, GameObject? hitObject, string? part, float distance, bool occluded, PickSource source, int overlapGroup, Vector3 point)
        {
            Ref = target ?? throw new ArgumentNullException(nameof(target));
            Owner = owner;
            HitObject = hitObject;
            Part = part;
            Distance = distance;
            Occluded = occluded;
            Source = source;
            OverlapGroup = overlapGroup;
            Point = point;
        }

        /// <summary>Ref of the logical owner (an authored component when one is found up the hierarchy).</summary>
        public AuthoringRef Ref { get; }

        /// <summary>The logical owner object (authored component, UIDocument owner or plain GameObject).</summary>
        public UnityEngine.Object? Owner { get; }

        /// <summary>The GameObject that was actually hit (a subpart of <see cref="Owner"/> when different).</summary>
        public GameObject? HitObject { get; }

        /// <summary>Part name (<c>Mesh:Lantern_Glass</c>, <c>Collider:Body</c>, <c>Ui:Root/Button</c>); null when the owner itself was hit.</summary>
        public string? Part { get; }

        /// <summary>Distance from the camera along the ray (0 for UI overlay hits).</summary>
        public float Distance { get; }

        /// <summary>True when a different object lies in front of this one along the ray.</summary>
        public bool Occluded { get; }

        public PickSource Source { get; }

        /// <summary>Candidates with the same group index lie within the overlap depth band of each other.</summary>
        public int OverlapGroup { get; }

        /// <summary>World-space hit point.</summary>
        public Vector3 Point { get; }

        /// <summary>The <see cref="PartRef"/> of this candidate when a part was hit, else null.</summary>
        public PartRef? ToPartRef() => Part == null ? null : new PartRef(Ref, Part);

        public override string ToString() =>
            Source.ToString() + " " + Ref + (Part == null ? string.Empty : " part=" + Part)
            + " d=" + Distance.ToString("0.###", CultureInfo.InvariantCulture) + (Occluded ? " occluded" : string.Empty)
            + " g=" + OverlapGroup.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Stopwatch timings of one picking query, in milliseconds (B-SELECT budget evidence).</summary>
    public sealed class PickTimings
    {
        public double UiMs { get; internal set; }

        public double PhysicsMs { get; internal set; }

        public double RendererMs { get; internal set; }

        public double GroundMs { get; internal set; }

        /// <summary>Time spent turning hits into refs (stamps included).</summary>
        public double ResolveMs { get; internal set; }

        public double TotalMs { get; internal set; }

        public override string ToString()
        {
            return "total=" + Format(TotalMs) + "ms ui=" + Format(UiMs) + " physics=" + Format(PhysicsMs) + " renderer=" + Format(RendererMs)
                + " ground=" + Format(GroundMs) + " resolve=" + Format(ResolveMs);
        }

        private static string Format(double value) => value.ToString("0.000", CultureInfo.InvariantCulture);
    }

    /// <summary>The ordered candidate list of a pick or marquee.</summary>
    public sealed class PickResult
    {
        public PickResult(IReadOnlyList<PickCandidate> candidates, PickTimings timings)
        {
            Candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
            Timings = timings ?? throw new ArgumentNullException(nameof(timings));
        }

        /// <summary>
        /// Ordered: UI overlay hits (topmost document first), then world hits by depth, then the ground location.
        /// </summary>
        public IReadOnlyList<PickCandidate> Candidates { get; }

        /// <summary>The default choice (the first candidate), or null for an empty pick.</summary>
        public PickCandidate? Default => Candidates.Count == 0 ? null : Candidates[0];

        /// <summary>
        /// The overlap list shown to the user (03 s2): every candidate when more than one lies under the cursor
        /// (same depth band or an occluded chain), else empty.
        /// </summary>
        public IReadOnlyList<PickCandidate> OverlapList => Candidates.Count > 1 ? Candidates : (IReadOnlyList<PickCandidate>)Array.Empty<PickCandidate>();

        public PickTimings Timings { get; }

        /// <summary>The distinct logical targets, in candidate order (for a SelectionSnapshot).</summary>
        public IReadOnlyList<AuthoringRef> Targets()
        {
            List<AuthoringRef> targets = new List<AuthoringRef>();
            foreach (PickCandidate candidate in Candidates)
            {
                bool seen = false;
                foreach (AuthoringRef existing in targets)
                {
                    if (existing.SameTarget(candidate.Ref))
                    {
                        seen = true;
                        break;
                    }
                }

                if (!seen)
                {
                    targets.Add(candidate.Ref);
                }
            }

            return targets;
        }
    }

    /// <summary>A sampled world location (03 s2 <c>PointAt</c>).</summary>
    public sealed class LocationPick
    {
        public LocationPick(AuthoringRef? location, Vector3 position, Vector3 normal, PickSource source, GameObject? surface, PickTimings timings)
        {
            Location = location;
            Position = position;
            Normal = normal;
            Source = source;
            Surface = surface;
            Timings = timings ?? throw new ArgumentNullException(nameof(timings));
        }

        /// <summary>The Location ref (kind Location, region + position + normal); null when nothing was hit.</summary>
        public AuthoringRef? Location { get; }

        public bool Hit => Location != null;

        public Vector3 Position { get; }

        public Vector3 Normal { get; }

        /// <summary>Physics (a collider surface), Ground (the ground plane) or NavMesh (snapped).</summary>
        public PickSource Source { get; }

        /// <summary>The collider object the location lies on, when it came from physics.</summary>
        public GameObject? Surface { get; }

        public PickTimings Timings { get; }
    }

    /// <summary>Tunables of the picking service.</summary>
    public sealed class PickOptions
    {
        /// <summary>Physics layer mask (default: every layer).</summary>
        public int LayerMask { get; set; } = ~0;

        public QueryTriggerInteraction Triggers { get; set; } = QueryTriggerInteraction.Ignore;

        /// <summary>Height of the ground plane used when no surface is hit.</summary>
        public float GroundHeight { get; set; }

        /// <summary>Append a ground Location candidate to <c>Pick</c> results (03 s2).</summary>
        public bool IncludeGroundCandidate { get; set; } = true;

        /// <summary>Query runtime UI Toolkit documents.</summary>
        public bool IncludeUi { get; set; } = true;

        /// <summary>Maximum distance of a NavMesh snap from the sampled ground point.</summary>
        public float NavMeshSampleDistance { get; set; } = 2f;

        /// <summary>Relative depth band of an overlap group (03 s2: 0.5 %).</summary>
        public float OverlapDepthFraction { get; set; } = 0.005f;

        /// <summary>Maximum ray length; zero or less means the camera's far clip plane.</summary>
        public float MaxDistance { get; set; }
    }
}

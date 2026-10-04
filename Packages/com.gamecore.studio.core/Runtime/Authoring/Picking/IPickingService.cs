// GameCore.Studio.Authoring - the picking contract (docs/studio/03-authoring-contracts.md s2, SADR-007).
// Points and rectangles are in the caller's viewport pixel space: origin at the top-left of the rectangle where the
// game camera's image is shown (the Studio viewport's GUI space), y growing downwards.
#nullable enable
using GameCore.Studio.Model;
using UnityEngine;

namespace GameCore.Studio.Authoring
{
    /// <summary>Selection from engine data: click, marquee, point-at-location and stale validation.</summary>
    public interface IPickingService
    {
        /// <summary>Candidates under <paramref name="screenPoint"/>, ordered by depth (UI overlay first, ground last).</summary>
        PickResult Pick(Vector2 screenPoint);

        /// <summary>Logical objects whose renderer bounds fall in <paramref name="screenRect"/> (frustum test).</summary>
        PickResult Marquee(Rect screenRect, bool requireFullContainment = false);

        /// <summary>The ground (NavMesh when present) location under <paramref name="screenPoint"/> and its region.</summary>
        LocationPick PointAt(Vector2 screenPoint);

        /// <summary>Recomputes stamps and existence/residency of every ref in a snapshot (03 s2 stale check).</summary>
        StaleReport Validate(SelectionSnapshot snapshot);
    }
}

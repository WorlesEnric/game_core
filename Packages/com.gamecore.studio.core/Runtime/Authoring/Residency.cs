// GameCore.Studio.Authoring - region residency and region location (docs/studio/03-authoring-contracts.md s2,
// SADR-006). The world package (com.gamecore.gameplay.world, RegionStreamer) answers these through an adapter the
// integrator registers; the defaults treat everything as resident and name regions after their scene.
#nullable enable
using GameCore.Studio.Model;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameCore.Studio.Authoring
{
    /// <summary>Residency of a region (the values of the <c>world.residency</c> slot, catalog row 1).</summary>
    public enum RegionResidency
    {
        Unloaded = 0,
        Loading = 1,
        Resident = 2,
        Unloading = 3,
    }

    /// <summary>Answers whether the region holding an authored thing is loaded (03 s2 stale check).</summary>
    public interface IResidencyQuery
    {
        /// <summary>Residency of the region that holds <paramref name="target"/>.</summary>
        RegionResidency ResidencyOf(AuthoringRef target);
    }

    /// <summary>Default residency: every region is resident.</summary>
    public sealed class AllResidentQuery : IResidencyQuery
    {
        public RegionResidency ResidencyOf(AuthoringRef target) => RegionResidency.Resident;
    }

    /// <summary>Names the region that owns a world position (03 s2 <c>PointAt</c>).</summary>
    public interface IRegionLocator
    {
        /// <summary>Region id at <paramref name="position"/>; <paramref name="hint"/> is the object hit there, if any.</summary>
        string RegionAt(Vector3 position, GameObject? hint);
    }

    /// <summary>Default locator: the region is the (lowercased) name of the hit object's scene, else the active scene.</summary>
    public sealed class SceneRegionLocator : IRegionLocator
    {
        public const string FallbackRegion = "world";

        public string RegionAt(Vector3 position, GameObject? hint)
        {
            Scene scene = hint != null ? hint.scene : SceneManager.GetActiveScene();
            string name = scene.IsValid() ? scene.name : string.Empty;
            return string.IsNullOrEmpty(name) ? FallbackRegion : name.ToLowerInvariant();
        }
    }
}

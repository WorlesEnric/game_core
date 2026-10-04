// GameCore.Studio.Authoring - region residency and region location (docs/studio/03-authoring-contracts.md s2,
// SADR-006). The world package (com.gamecore.gameplay.world, RegionStreamer) answers these through an adapter the
// integrator registers; the defaults treat everything as resident and name regions after their scene.
#nullable enable
using System;
using System.Reflection;
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

    /// <summary>
    /// Adapts a gameplay residency query bound by interface name (<c>GameCore.Gameplay.Contracts.IResidencyQuery</c>,
    /// implemented by <c>RegionStreamer</c>): its <c>RegionResidency ResidencyOf(string regionId)</c> is called by
    /// reflection and the enum is converted by member name, so Studio needs no type dependency on gameplay. The region of
    /// a target comes from <see cref="RegionOf"/>; by default a region ref names itself, a location names its region, and
    /// anything else (no known region) counts as resident.
    /// </summary>
    public sealed class NamedResidencyQuery : IResidencyQuery
    {
        public const string InterfaceName = "IResidencyQuery";

        private readonly object _source;
        private readonly MethodInfo _residencyOf;

        private NamedResidencyQuery(object source, MethodInfo residencyOf)
        {
            _source = source;
            _residencyOf = residencyOf;
        }

        /// <summary>Region id of a target, or null when unknown (treated as resident).</summary>
        public Func<AuthoringRef, string?> RegionOf { get; set; } = DefaultRegionOf;

        /// <summary>Wraps <paramref name="source"/> when it implements an interface named IResidencyQuery with ResidencyOf(string).</summary>
        public static NamedResidencyQuery? TryWrap(object? source)
        {
            if (source == null)
            {
                return null;
            }

            foreach (Type contract in source.GetType().GetInterfaces())
            {
                if (contract.Name != InterfaceName)
                {
                    continue;
                }

                MethodInfo? method = contract.GetMethod("ResidencyOf", new[] { typeof(string) });
                if (method != null && method.ReturnType.IsEnum)
                {
                    return new NamedResidencyQuery(source, method);
                }
            }

            return null;
        }

        public static string? DefaultRegionOf(AuthoringRef target)
        {
            if (target.Kind == AuthoringKind.Region)
            {
                return target.AuthoringId;
            }

            return target.Kind == AuthoringKind.Location ? target.Location?.Region : null;
        }

        public RegionResidency ResidencyOf(AuthoringRef target)
        {
            string? region = RegionOf(target);
            if (string.IsNullOrEmpty(region))
            {
                return RegionResidency.Resident;
            }

            object? value = _residencyOf.Invoke(_source, new object[] { region! });
            return value != null && Enum.TryParse(value.ToString(), false, out RegionResidency residency) ? residency : RegionResidency.Unloaded;
        }
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

// GameCore.Gameplay.Contracts - presentation and residency seams (P1.1).
//
// Presentation never writes authoritative state: a binder reads the committed slot values of the targets it presents
// after the host pump (P-045) and maps them onto engine objects. A binder must be headless-safe: under batchmode with
// no graphics device it reports itself skipped and touches no engine object.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Gameplay.Contracts
{
    /// <summary>Residency of a region, the value of its <c>world.residency</c> slot.</summary>
    public enum RegionResidency
    {
        /// <summary>The region's scene is not loaded; its targets exist but have no views.</summary>
        Unloaded = 0,

        /// <summary>The scene load was requested and has not completed.</summary>
        Loading = 1,

        /// <summary>The scene is loaded and its views are live.</summary>
        Resident = 2,

        /// <summary>The scene unload was requested and has not completed; views are suspended.</summary>
        Unloading = 3,
    }

    /// <summary>
    /// One committed slot read for presentation: the value of <paramref name="slot"/> owned by <paramref name="owner"/> on
    /// a target, or false when the target does not hold it.
    /// </summary>
    public interface ICommittedSlotReader
    {
        bool TryRead(TargetId target, OwnerId owner, SlotId slot, out int value);
    }

    /// <summary>A presentation binder: maps committed slot values of its targets onto engine objects.</summary>
    public interface IPresentationBinder
    {
        /// <summary>Stable binder name, used in adapter reports.</summary>
        string BinderName { get; }

        /// <summary>False when the binder skips (headless batchmode, no graphics device): it then touches nothing.</summary>
        bool IsActive { get; }

        /// <summary>Presents the committed state of every bound target; returns the number of views touched.</summary>
        int Present(ICommittedSlotReader slots);
    }

    /// <summary>Something whose views follow region residency (suspended while not Resident).</summary>
    public interface IResidencyAware
    {
        /// <summary>Called when a region's residency changes; <paramref name="regionId"/> is its authoring id.</summary>
        void OnResidencyChanged(string regionId, RegionResidency residency);
    }

    /// <summary>
    /// Read access to region residency by authoring id (P1.1 coordination note: Studio detects an unloaded region through
    /// this interface, bound by name at integration).
    /// </summary>
    public interface IResidencyQuery
    {
        /// <summary>Authoring ids of every region the query knows, in canonical (ordinal) order.</summary>
        IReadOnlyList<string> RegionIds { get; }

        /// <summary>The committed residency of a region; false for an unknown region id.</summary>
        bool TryGetResidency(string regionId, out RegionResidency residency);

        /// <summary>The committed residency of a region, or Unloaded for an unknown region id.</summary>
        RegionResidency ResidencyOf(string regionId);
    }
}

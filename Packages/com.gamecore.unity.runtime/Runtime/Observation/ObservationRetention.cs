// GameCore.Execution.Observation — the bounded retention settings of one world's observation storage (GC-016).
//
// P-007: "Read leases pin immutable snapshots, not live writable components; bounded retention rejects new leases
// with `SnapshotBackpressure` rather than overwriting leased memory." P-045: "Event cursors and snapshots have
// configured retention bounds."
//
// This file is engine-free on purpose: it is compiled by `dotnet/src/GameCore.Execution` through the package's
// `Runtime/Observation` folder, so the retention arithmetic is testable without Unity, and Unity compiles the same
// source into `GameCore.Unity.Runtime`.
#nullable enable
using System;
using System.Globalization;

namespace GameCore.Execution.Observation
{
    /// <summary>
    /// The three bounds one world's observation storage is configured with: how many committed step images it
    /// keeps, how many committed events it keeps, and how many snapshot leases may be outstanding at once. Every
    /// bound is positive; a bound of zero would make the storage unable to answer the first observer at all.
    /// </summary>
    public readonly struct ObservationRetention
    {
        public ObservationRetention(int snapshotRetention, int eventRetention, int maxConcurrentLeases)
        {
            if (snapshotRetention <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(snapshotRetention), "Snapshot retention must be positive (P-045).");
            }

            if (eventRetention <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(eventRetention), "Event retention must be positive (P-045).");
            }

            if (maxConcurrentLeases <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxConcurrentLeases), "The snapshot lease pool must be positive (P-007).");
            }

            SnapshotRetention = snapshotRetention;
            EventRetention = eventRetention;
            MaxConcurrentLeases = maxConcurrentLeases;
        }

        /// <summary>Committed step images kept before the oldest unpinned image is evicted.</summary>
        public int SnapshotRetention { get; }

        /// <summary>Committed events kept before the oldest event is dropped with an explicit gap report.</summary>
        public int EventRetention { get; }

        /// <summary>Outstanding snapshot leases before a new acquisition is refused with backpressure.</summary>
        public int MaxConcurrentLeases { get; }

        /// <summary>
        /// The default of the owned world host: 32 committed images, 16 concurrent leases, 256 committed events.
        /// It matches what <c>UnityWorldHost</c> constructs today, so the settings are a record of the live bounds
        /// rather than a second policy.
        /// </summary>
        public static ObservationRetention Default => new ObservationRetention(32, 256, 16);

        /// <summary>
        /// The hard ceiling on retained images: the nominal window plus one image per possible lease, because a
        /// pinned image is never evicted and each live lease pins exactly one image (P-007). Above this the lease
        /// pool, not retention, is what refuses.
        /// </summary>
        public int MaxRetainedImages => SnapshotRetention + MaxConcurrentLeases;

        public override string ToString() =>
            "ObservationRetention(images=" + SnapshotRetention.ToString(CultureInfo.InvariantCulture)
            + ", events=" + EventRetention.ToString(CultureInfo.InvariantCulture)
            + ", leases=" + MaxConcurrentLeases.ToString(CultureInfo.InvariantCulture) + ")";
    }
}

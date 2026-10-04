// GameCore.Rules.Gameplay.World - region residency and travel rules (P1.1).
//
// Residency is a four-state machine carried in each region's `world.residency` slot:
//
//     Unloaded(0) --load--> Loading(1) --loaded--> Resident(2) --unload--> Unloading(3) --unloaded--> Unloaded(0)
//                           Loading(1) --cancelled/failed--> Unloaded(0)
//
// Only these five transitions are legal; anything else (including a no-op) is refused. Travel moves a traveller from
// its current region to a neighbouring region through a portal of the region graph; the graph is undirected and built
// from the baked portals, and its neighbour lists are sorted so every query is deterministic.
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.Rules.Gameplay.World
{
    /// <summary>Residency values of the <c>world.residency</c> slot.</summary>
    public static class Residency
    {
        public const int Unloaded = 0;
        public const int Loading = 1;
        public const int Resident = 2;
        public const int Unloading = 3;

        public static bool IsValid(int value) => value >= Unloaded && value <= Unloading;

        public static string Name(int value)
        {
            switch (value)
            {
                case Unloaded: return "Unloaded";
                case Loading: return "Loading";
                case Resident: return "Resident";
                case Unloading: return "Unloading";
                default: return "Invalid(" + value + ")";
            }
        }
    }

    /// <summary>Why a residency transition was refused.</summary>
    public enum ResidencyRefusal
    {
        None = 0,
        InvalidValue = 1,
        Unchanged = 2,
        IllegalTransition = 3,
    }

    /// <summary>The residency state machine.</summary>
    public static class ResidencyRules
    {
        /// <summary>Checks one transition; <see cref="ResidencyRefusal.None"/> means legal.</summary>
        public static ResidencyRefusal Check(int from, int to)
        {
            if (!Residency.IsValid(from) || !Residency.IsValid(to))
            {
                return ResidencyRefusal.InvalidValue;
            }

            if (from == to)
            {
                return ResidencyRefusal.Unchanged;
            }

            switch (from)
            {
                case Residency.Unloaded:
                    return to == Residency.Loading ? ResidencyRefusal.None : ResidencyRefusal.IllegalTransition;
                case Residency.Loading:
                    return to == Residency.Resident || to == Residency.Unloaded
                        ? ResidencyRefusal.None
                        : ResidencyRefusal.IllegalTransition;
                case Residency.Resident:
                    return to == Residency.Unloading ? ResidencyRefusal.None : ResidencyRefusal.IllegalTransition;
                default:
                    return to == Residency.Unloaded ? ResidencyRefusal.None : ResidencyRefusal.IllegalTransition;
            }
        }

        public static bool IsLegal(int from, int to) => Check(from, to) == ResidencyRefusal.None;

        /// <summary>
        /// The next residency on the way from <paramref name="current"/> towards being resident (<paramref name="wanted"/>
        /// true) or unloaded (false); returns <paramref name="current"/> when no step is needed or possible yet.
        /// </summary>
        public static int NextToward(int current, bool wanted)
        {
            if (wanted)
            {
                return current == Residency.Unloaded ? Residency.Loading : current;
            }

            return current == Residency.Resident ? Residency.Unloading : current;
        }

        /// <summary>True when targets of a region with this residency have live views.</summary>
        public static bool HasLiveViews(int residency) => residency == Residency.Resident;
    }

    /// <summary>One undirected portal link between two regions (region values are int31 stable keys).</summary>
    public readonly struct PortalLink
    {
        public PortalLink(int portal, int regionA, int regionB)
        {
            Portal = portal;
            RegionA = regionA;
            RegionB = regionB;
        }

        public int Portal { get; }

        public int RegionA { get; }

        public int RegionB { get; }

        public bool Connects(int from, int to) => (RegionA == from && RegionB == to) || (RegionA == to && RegionB == from);

        public override string ToString() => "portal " + Portal + " (" + RegionA + " <-> " + RegionB + ")";
    }

    /// <summary>Why a travel request was refused.</summary>
    public enum TravelRefusal
    {
        None = 0,
        UnknownRegion = 1,
        SameRegion = 2,
        NoPortal = 3,
        PortalMismatch = 4,
    }

    /// <summary>The undirected region graph of one world, built from its baked portals.</summary>
    public sealed class RegionGraph
    {
        private readonly HashSet<int> regions = new HashSet<int>();
        private readonly List<PortalLink> links = new List<PortalLink>();
        private readonly Dictionary<int, List<int>> neighbours = new Dictionary<int, List<int>>();

        public RegionGraph(IEnumerable<int> regionKeys, IEnumerable<PortalLink> portals)
        {
            if (regionKeys == null)
            {
                throw new ArgumentNullException(nameof(regionKeys));
            }

            if (portals == null)
            {
                throw new ArgumentNullException(nameof(portals));
            }

            foreach (int region in regionKeys)
            {
                regions.Add(region);
                if (!neighbours.ContainsKey(region))
                {
                    neighbours.Add(region, new List<int>());
                }
            }

            foreach (PortalLink link in portals)
            {
                links.Add(link);
                AddNeighbour(link.RegionA, link.RegionB);
                AddNeighbour(link.RegionB, link.RegionA);
            }

            links.Sort((left, right) => left.Portal.CompareTo(right.Portal));
            foreach (List<int> list in neighbours.Values)
            {
                list.Sort();
            }
        }

        public int RegionCount => regions.Count;

        public IReadOnlyList<PortalLink> Links => links;

        public bool Contains(int region) => regions.Contains(region);

        /// <summary>Neighbours of a region in ascending key order; empty for an unknown region.</summary>
        public IReadOnlyList<int> NeighboursOf(int region) =>
            neighbours.TryGetValue(region, out List<int>? list) ? list : (IReadOnlyList<int>)Array.Empty<int>();

        /// <summary>The lowest-keyed portal connecting two regions.</summary>
        public bool TryFindPortal(int from, int to, out PortalLink portal)
        {
            for (int i = 0; i < links.Count; i++)
            {
                if (links[i].Connects(from, to))
                {
                    portal = links[i];
                    return true;
                }
            }

            portal = default(PortalLink);
            return false;
        }

        public bool TryGetPortal(int portalKey, out PortalLink portal)
        {
            for (int i = 0; i < links.Count; i++)
            {
                if (links[i].Portal == portalKey)
                {
                    portal = links[i];
                    return true;
                }
            }

            portal = default(PortalLink);
            return false;
        }

        private void AddNeighbour(int from, int to)
        {
            if (!neighbours.TryGetValue(from, out List<int>? list))
            {
                list = new List<int>();
                neighbours.Add(from, list);
            }

            if (!list.Contains(to))
            {
                list.Add(to);
            }
        }
    }

    /// <summary>Travel validation.</summary>
    public static class TravelRules
    {
        /// <summary>
        /// Validates travel from <paramref name="from"/> to <paramref name="to"/>. A <paramref name="portalKey"/> of zero
        /// accepts any connecting portal; a non-zero key must name a portal that connects exactly these two regions.
        /// </summary>
        public static TravelRefusal Validate(RegionGraph graph, int from, int to, int portalKey)
        {
            if (graph == null)
            {
                throw new ArgumentNullException(nameof(graph));
            }

            if (!graph.Contains(from) || !graph.Contains(to))
            {
                return TravelRefusal.UnknownRegion;
            }

            if (from == to)
            {
                return TravelRefusal.SameRegion;
            }

            if (portalKey != 0)
            {
                if (!graph.TryGetPortal(portalKey, out PortalLink named))
                {
                    return TravelRefusal.NoPortal;
                }

                return named.Connects(from, to) ? TravelRefusal.None : TravelRefusal.PortalMismatch;
            }

            return graph.TryFindPortal(from, to, out PortalLink _) ? TravelRefusal.None : TravelRefusal.NoPortal;
        }
    }
}

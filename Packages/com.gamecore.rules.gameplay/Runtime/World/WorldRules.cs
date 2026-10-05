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

    /// <summary>
    /// Stable refusal and diagnostic codes of the world plugin's runtime (P1.7a). They extend the GP-WLD group of
    /// GameplayDiagnosticCodes (P1.1) without renumbering it; the gameplay contracts list them once the integrator folds
    /// them in (PACKET.md).
    /// </summary>
    public static class WorldRefusalCodes
    {
        /// <summary>world.travel: the portal's condition evaluated false.</summary>
        public const string TravelConditionFailed = "GP-WLD-013";

        /// <summary>world.travel: the portal's condition reference is unknown to the world's evaluator.</summary>
        public const string TravelConditionUnknown = "GP-WLD-014";

        /// <summary>world.travel: the traveller is not a live target with a region.</summary>
        public const string TravelStaleTraveller = "GP-WLD-015";

        /// <summary>world.travel: the portal or destination record is missing.</summary>
        public const string TravelMissingPortal = "GP-WLD-016";

        /// <summary>world.travel: the obligation that carried it was already applied.</summary>
        public const string TravelAlreadyApplied = "GP-WLD-017";

        /// <summary>world.place: the issuer is neither the host nor Studio.</summary>
        public const string PlaceNotAllowed = "GP-WLD-022";

        /// <summary>world.place: the target is not a live target with a region.</summary>
        public const string PlaceStaleTarget = "GP-WLD-023";

        /// <summary>A region scene failed to load (the streamer backs off and retries).</summary>
        public const string SceneLoadFailed = "GP-WLD-030";

        /// <summary>A region scene is not in the build settings: a player build cannot load it.</summary>
        public const string SceneNotInBuild = "GP-WLD-031";

        /// <summary>A region scene failed to load <see cref="StreamingRules.MaxLoadFailures"/> times; its streaming latched.</summary>
        public const string SceneLoadLatched = "GP-WLD-032";

        /// <summary>A malformed or unsupported world command payload.</summary>
        public const string MalformedCommand = "GP-WLD-040";

        /// <summary>Every runtime code, in declaration order.</summary>
        public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
        {
            TravelConditionFailed, TravelConditionUnknown, TravelStaleTraveller, TravelMissingPortal, TravelAlreadyApplied,
            PlaceNotAllowed, PlaceStaleTarget, SceneLoadFailed, SceneNotInBuild, SceneLoadLatched, MalformedCommand,
        });

        /// <summary>The code of a travel validation refusal (the P1.1 codes GP-WLD-010..012).</summary>
        public static string OfTravel(TravelRefusal refusal)
        {
            switch (refusal)
            {
                case TravelRefusal.UnknownRegion: return "GP-WLD-012";
                case TravelRefusal.SameRegion: return "GP-WLD-011";
                default: return "GP-WLD-010";
            }
        }
    }

    /// <summary>What the streamer does to reconcile a region's committed residency with its loaded scene.</summary>
    public enum ReconcileAction
    {
        None = 0,

        /// <summary>Committed Resident but the scene is not loaded (a restored world): load it, keep the residency.</summary>
        LoadScene = 1,

        /// <summary>Committed Unloaded, not wanted, but the scene is loaded (left over by the previous root): unload it.</summary>
        UnloadScene = 2,
    }

    /// <summary>Region streaming decisions that do not touch an engine object (P1.7a, A3).</summary>
    public static class StreamingRules
    {
        /// <summary>Consecutive load failures after which a region's streaming latches (until reset).</summary>
        public const int MaxLoadFailures = 3;

        /// <summary>Frames waited before the first retry; each further failure doubles it.</summary>
        public const int BaseBackoffFrames = 30;

        /// <summary>
        /// The reconciliation of one region on the first tick after an attach. A wanted region whose committed residency
        /// is Unloaded but whose scene is already loaded needs nothing here: the ordinary Loading step adopts the loaded
        /// scene.
        /// </summary>
        public static ReconcileAction Reconcile(int committed, bool sceneLoaded, bool wanted)
        {
            if (committed == Residency.Resident && !sceneLoaded)
            {
                return ReconcileAction.LoadScene;
            }

            if (committed == Residency.Unloaded && sceneLoaded && !wanted)
            {
                return ReconcileAction.UnloadScene;
            }

            return ReconcileAction.None;
        }

        /// <summary>True once a region failed <see cref="MaxLoadFailures"/> consecutive loads.</summary>
        public static bool IsLatched(int consecutiveFailures) => consecutiveFailures >= MaxLoadFailures;

        /// <summary>Frames to wait after <paramref name="consecutiveFailures"/> failures before loading again (0 for none).</summary>
        public static int BackoffFrames(int consecutiveFailures)
        {
            if (consecutiveFailures <= 0)
            {
                return 0;
            }

            int shift = Math.Min(consecutiveFailures - 1, 8);
            return BaseBackoffFrames << shift;
        }
    }
}

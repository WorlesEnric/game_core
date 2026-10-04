// GameCore.Rules.Gameplay.Tests - residency state machine and travel validation (P1.1).
#nullable enable
using System.Collections.Generic;
using GameCore.Rules.Gameplay.World;
using NUnit.Framework;

namespace GameCore.Rules.Gameplay.Tests
{
    public sealed class WorldRulesTests
    {
        private const int Village = 101;
        private const int Marsh = 202;
        private const int Belfry = 303;
        private const int Island = 404;
        private const int VillageMarsh = 11;
        private const int MarshBelfry = 22;

        private static RegionGraph Graph() => new RegionGraph(
            new[] { Belfry, Village, Marsh, Island },
            new List<PortalLink> { new PortalLink(MarshBelfry, Marsh, Belfry), new PortalLink(VillageMarsh, Village, Marsh) });

        [TestCase(Residency.Unloaded, Residency.Loading, ResidencyRefusal.None)]
        [TestCase(Residency.Loading, Residency.Resident, ResidencyRefusal.None)]
        [TestCase(Residency.Loading, Residency.Unloaded, ResidencyRefusal.None)]
        [TestCase(Residency.Resident, Residency.Unloading, ResidencyRefusal.None)]
        [TestCase(Residency.Unloading, Residency.Unloaded, ResidencyRefusal.None)]
        [TestCase(Residency.Unloaded, Residency.Resident, ResidencyRefusal.IllegalTransition)]
        [TestCase(Residency.Unloaded, Residency.Unloading, ResidencyRefusal.IllegalTransition)]
        [TestCase(Residency.Loading, Residency.Unloading, ResidencyRefusal.IllegalTransition)]
        [TestCase(Residency.Resident, Residency.Unloaded, ResidencyRefusal.IllegalTransition)]
        [TestCase(Residency.Resident, Residency.Loading, ResidencyRefusal.IllegalTransition)]
        [TestCase(Residency.Unloading, Residency.Resident, ResidencyRefusal.IllegalTransition)]
        [TestCase(Residency.Unloading, Residency.Loading, ResidencyRefusal.IllegalTransition)]
        [TestCase(Residency.Resident, Residency.Resident, ResidencyRefusal.Unchanged)]
        [TestCase(4, Residency.Unloaded, ResidencyRefusal.InvalidValue)]
        [TestCase(Residency.Unloaded, -1, ResidencyRefusal.InvalidValue)]
        public void Residency_HasExactlyFiveLegalTransitions(int from, int to, ResidencyRefusal expected)
        {
            Assert.That(ResidencyRules.Check(from, to), Is.EqualTo(expected));
        }

        [Test]
        public void Residency_EveryPairIsCheckedAndOnlyFiveAreLegal()
        {
            int legal = 0;
            for (int from = 0; from < 4; from++)
            {
                for (int to = 0; to < 4; to++)
                {
                    legal += ResidencyRules.IsLegal(from, to) ? 1 : 0;
                }
            }

            Assert.That(legal, Is.EqualTo(5));
        }

        [Test]
        public void NextToward_WalksTheMachineOneStepAtATime()
        {
            Assert.That(ResidencyRules.NextToward(Residency.Unloaded, true), Is.EqualTo(Residency.Loading));
            Assert.That(ResidencyRules.NextToward(Residency.Loading, true), Is.EqualTo(Residency.Loading));
            Assert.That(ResidencyRules.NextToward(Residency.Resident, true), Is.EqualTo(Residency.Resident));
            Assert.That(ResidencyRules.NextToward(Residency.Resident, false), Is.EqualTo(Residency.Unloading));
            Assert.That(ResidencyRules.NextToward(Residency.Unloaded, false), Is.EqualTo(Residency.Unloaded));
            Assert.That(ResidencyRules.HasLiveViews(Residency.Resident), Is.True);
            Assert.That(ResidencyRules.HasLiveViews(Residency.Unloading), Is.False);
        }

        [Test]
        public void Graph_NeighboursAreSortedAndUndirected()
        {
            RegionGraph graph = Graph();
            Assert.That(graph.NeighboursOf(Marsh), Is.EqualTo(new[] { Village, Belfry }));
            Assert.That(graph.NeighboursOf(Village), Is.EqualTo(new[] { Marsh }));
            Assert.That(graph.NeighboursOf(Island), Is.Empty);
            Assert.That(graph.NeighboursOf(999), Is.Empty);
            Assert.That(graph.Links[0].Portal, Is.EqualTo(VillageMarsh));
        }

        [TestCase(Village, Marsh, 0, TravelRefusal.None)]
        [TestCase(Marsh, Village, 0, TravelRefusal.None)]
        [TestCase(Marsh, Belfry, MarshBelfry, TravelRefusal.None)]
        [TestCase(Village, Belfry, 0, TravelRefusal.NoPortal)]
        [TestCase(Village, Village, 0, TravelRefusal.SameRegion)]
        [TestCase(Village, 999, 0, TravelRefusal.UnknownRegion)]
        [TestCase(Village, Marsh, MarshBelfry, TravelRefusal.PortalMismatch)]
        [TestCase(Village, Marsh, 77, TravelRefusal.NoPortal)]
        [TestCase(Island, Village, 0, TravelRefusal.NoPortal)]
        public void Travel_RequiresAConnectingPortal(int from, int to, int portal, TravelRefusal expected)
        {
            Assert.That(TravelRules.Validate(Graph(), from, to, portal), Is.EqualTo(expected));
        }

        [Test]
        public void Travel_ALoopThroughEveryRegionIsValidStepByStep()
        {
            RegionGraph graph = Graph();
            int[] loop = { Village, Marsh, Belfry, Marsh, Village };
            for (int i = 0; i + 1 < loop.Length; i++)
            {
                Assert.That(TravelRules.Validate(graph, loop[i], loop[i + 1], 0), Is.EqualTo(TravelRefusal.None), "leg " + i);
            }
        }
    }
}

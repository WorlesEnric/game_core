// Hollowmere.P1_7b.EditMode.Tests - world.connectRegions applied through the engine can be undone through the journal
// (HistoryService.Undo): the world's stamp right after the apply still matches at undo time, so no conflict is reported.
#nullable enable
using GameCore.Gameplay.World;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Hollowmere.P1_7b.EditMode.Tests
{
    public sealed class ConnectRegionsUndoTests
    {
        private HardeningTestBed? _bed;

        private HardeningTestBed Bed => _bed!;

        [SetUp]
        public void SetUp()
        {
            _bed = new HardeningTestBed("undo");
            _bed.Runtime.Index.Rebuild();
        }

        [TearDown]
        public void TearDown()
        {
            _bed?.Dispose();
            _bed = null;
        }

        [Test]
        public void ConnectRegions_OnTheHollowmereWorld_UndoesThroughTheJournal()
        {
            WorldDefinition world = HardeningTestBed.Load<WorldDefinition>("Hollowmere");
            Assert.That(world.Regions.Count, Is.GreaterThanOrEqualTo(2));
            int portals = world.Portals.Count;
            HardeningTestBed.EnsureTempFolder();
            ApplyReport report = Bed.Apply("world.connectRegions", world, new JObject
            {
                ["regionA"] = Bed.RefToken(world.Regions[0]),
                ["regionB"] = Bed.RefToken(world.Regions[1]),
                ["assetPath"] = HardeningTestBed.TempFolder + "/P17bExtraPortal.asset",
            });
            Assert.That(world.Portals.Count, Is.EqualTo(portals + 1));

            string? applied = Bed.Runtime.Resolver.ComputeStamp(world);
            JToken describedApplied = Bed.Invoke("inspect.describe", world, new JObject { ["includeReferrers"] = false });
            Bed.Runtime.Index.Flush();
            string? flushed = Bed.Runtime.Resolver.ComputeStamp(world);
            JToken describedFlushed = Bed.Invoke("inspect.describe", world, new JObject { ["includeReferrers"] = false });
            TestContext.WriteLine("stamp after apply " + applied + ", after flush " + flushed);
            if (!JToken.DeepEquals(describedApplied, describedFlushed))
            {
                TestContext.WriteLine("after apply:\n" + describedApplied + "\nafter flush:\n" + describedFlushed);
            }

            HistoryResult undo = Bed.Runtime.History.Undo(report.Entry.Id);
            Assert.That(undo.Ok, Is.True, string.Join("; ", undo.Diagnostics));
            Assert.That(world.Portals.Count, Is.EqualTo(portals), "undo restores the portal list");
        }
    }
}

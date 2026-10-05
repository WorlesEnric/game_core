#nullable enable
using System.Collections;
using System.IO;
using System.Threading;
using GameCore.Gameplay.Contracts;
using GameCore.Rules.Gameplay.Ui;
using GameCore.Unity.App;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Hollowmere.P3_1.PlayMode.Tests
{
    public sealed partial class FullQuestHeadless
    {
        [UnityTest]
        public IEnumerator P31b_SAVE_AsyncWriteRestoresCapturedHashAndConfirmsAfterWrite()
        {
            yield return Boot();
            Assert.That(game.Rig!.Dispatch("open.pause").Accepted, Is.True);
            yield return Until(() => game.Rig.Ui.Screen == UiScreen.Pause, "pause");
            Assert.That(game.Rig.Dispatch("open.save").Accepted, Is.True);
            yield return Until(() => game.Rig.Ui.Screen == UiScreen.Save, "save screen");
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            int writtenThread = 0;
            game.Saves!.Written += _ => writtenThread = Thread.CurrentThread.ManagedThreadId;
            Assert.That(game.Rig.Dispatch("save.1").Accepted, Is.True);
            Assert.That(game.Rig.PendingSave, Is.Not.Null);
            Assert.That(game.Rig.Ui.Models.SaveLoad.Status, Is.EqualTo("Saving…"));
            Assert.That(game.Rig.Dispatch("save.1").Accepted, Is.False, "no overlapping writers");
            Assert.That(game.Rig.Dispatch("delete.1").Accepted, Is.False);
            yield return Until(() => game.Rig.PendingSave!.IsCompleted, "async save");
            SaveResult captured = game.Rig.PendingSave!.GetAwaiter().GetResult();
            Assert.That(captured.Succeeded, Is.True, captured.ToString());
            Assert.That(writtenThread, Is.EqualTo(mainThread));
            Assert.That(game.Rig.Ui.Models.SaveLoad.Status, Is.EqualTo("Saved to slot-1"));
            SaveResult restored = game.Saves.Restore("slot-1");
            Assert.That(restored.Succeeded, Is.True, restored.ToString());
            Assert.That(restored.SlotHash, Is.EqualTo(captured.SlotHash));
            Assert.That(game.World!.Root.PumpCounter.Violations, Is.Zero);
        }

        [UnityTest]
        public IEnumerator P31b_SAVE_InterruptedCaptureAndFailedHeaderKeepPreviousSave()
        {
            yield return Boot();
            SaveService saves = game.Saves!;
            const string slot = "p31b-crash";
            SaveResult previous = saves.Capture(slot);
            Assert.That(previous.Succeeded, Is.True);
            string header = File.ReadAllText(saves.HeaderPath(slot));
            string document = saves.DocumentPath(slot);
            yield return null;
            SaveServiceCapture captured = saves.CaptureSnapshot(slot);
            Assert.That(captured.Result.Succeeded, Is.True);
            Assert.That(File.ReadAllText(saves.HeaderPath(slot)), Is.EqualTo(header), "capture never publishes");
            Assert.That(saves.DocumentPath(slot), Is.EqualTo(document));
            // A directory at the temporary-header path makes publication fail AFTER the new checkpoint is written.
            string partial = saves.HeaderPath(slot) + ".partial";
            if (File.Exists(partial)) File.Delete(partial);
            Directory.CreateDirectory(partial);
            var write = saves.WriteCapturedAsync(captured);
            yield return Until(() => write.IsCompleted, "interrupted publication");
            Directory.Delete(partial);
            Assert.That(write.GetAwaiter().GetResult().Succeeded, Is.False);
            Assert.That(File.ReadAllText(saves.HeaderPath(slot)), Is.EqualTo(header));
            Assert.That(saves.DocumentPath(slot), Is.EqualTo(document));
            SaveResult restored = saves.Restore(slot);
            Assert.That(restored.Succeeded, Is.True, restored.ToString());
            Assert.That(restored.SlotHash, Is.EqualTo(previous.SlotHash));
            saves.Delete(slot);
        }

        [UnityTest]
        public IEnumerator P31b_BELFRY_PreloadedRegionReallyUnloadsAndReloads()
        {
            yield return Boot();
            yield return Rumour();
            yield return Interact("Lantern (barn)", () => Item("Lantern") >= 1, "lantern");
            yield return Gate();
            yield return Interact("Bell Clapper", () => Item("BellClapper") == 1, "clapper");
            var world = game.World!;
            yield return Until(() => world.Streamer.IsSettled, "preload");
            Assert.That(world.Streamer.PreloadNeighbours, Is.True);
            Assert.That(world.Streamer.ResidencyOf(BelfryId), Is.EqualTo(RegionResidency.Resident));
            yield return Travel(BelfryId, "belfry");
            yield return Until(() => world.Streamer.IsSettled && !world.Streamer.PreloadNeighbours, "release neighbours");
            Assert.That(world.Streamer.ResidencyOf(MarshId), Is.EqualTo(RegionResidency.Unloaded));
            yield return Travel(VillageId, "village");
            yield return Until(() => world.Streamer.IsSettled, "belfry unload");
            Assert.That(world.Streamer.ResidencyOf(BelfryId), Is.EqualTo(RegionResidency.Unloaded));
            int loads = world.Streamer.LoadsStarted;
            yield return Travel(BelfryId, "belfry reload");
            yield return Until(() => world.Streamer.IsSettled, "reload complete");
            Assert.That(world.Streamer.LoadsStarted, Is.GreaterThan(loads));
            Assert.That(world.Streamer.LoadFailures, Is.Zero);
            Assert.That(world.Root.PumpCounter.Violations, Is.Zero);
        }
    }
}

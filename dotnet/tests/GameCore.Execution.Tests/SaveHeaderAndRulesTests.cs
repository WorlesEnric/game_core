// SADR-012 (studio) save slot header (unity.app SaveSlotHeader.cs) and gameplay save rules (rules.gameplay
// Runtime/Save/SaveRules.cs), compiled here from their package sources because both are engine-free single files.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Rules.Gameplay.Save;
using GameCore.Unity.App;
using NUnit.Framework;

namespace GameCore.Execution.Tests
{
    [TestFixture]
    public sealed class SaveSlotHeaderTests
    {
        private const string Fingerprint = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";
        private const string DocumentHash = "ffeeddccbbaa99887766554433221100ffeeddccbbaa99887766554433221100";

        private static SaveSlotHeader Sample(string? thumbnail = "thumbs/slot-1.png") =>
            new SaveSlotHeader(
                "slot-1",
                "hollowmere",
                Fingerprint,
                new[] { new SaveSchemaVersion("game.player.health.schema", 2U), new SaveSchemaVersion("game.quest \"log\"", 1U) },
                "region.old-mill",
                3723.25,
                new DateTime(2026, 10, 4, 12, 30, 15, 125, DateTimeKind.Utc),
                thumbnail,
                1234UL,
                DocumentHash,
                4096L,
                true);

        [Test]
        public void AHeaderRoundTripsThroughItsCanonicalJson()
        {
            SaveSlotHeader header = Sample();
            string json = header.ToJson();
            Assert.That(SaveSlotHeader.TryParse(json, out SaveSlotHeader? parsed, out string error), Is.True, error);
            Assert.That(parsed!.Format, Is.EqualTo(SaveSlotHeader.CurrentFormat));
            Assert.That(parsed.Slot, Is.EqualTo("slot-1"));
            Assert.That(parsed.GameId, Is.EqualTo("hollowmere"));
            Assert.That(parsed.CatalogFingerprint, Is.EqualTo(Fingerprint));
            Assert.That(parsed.SchemaVersions.Count, Is.EqualTo(2));
            Assert.That(parsed.SchemaVersions[0].Schema, Is.EqualTo("game.player.health.schema"));
            Assert.That(parsed.SchemaVersions[0].Version, Is.EqualTo(2U));
            Assert.That(parsed.SchemaVersions[1].Schema, Is.EqualTo("game.quest \"log\""), "strings are escaped and unescaped");
            Assert.That(parsed.RegionId, Is.EqualTo("region.old-mill"));
            Assert.That(parsed.PlayTimeSeconds, Is.EqualTo(3723.25));
            Assert.That(parsed.SavedAtUtc, Is.EqualTo(header.SavedAtUtc));
            Assert.That(parsed.SavedAtUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(parsed.ThumbnailPath, Is.EqualTo("thumbs/slot-1.png"));
            Assert.That(parsed.LogicalStep, Is.EqualTo(1234UL));
            Assert.That(parsed.DocumentHash, Is.EqualTo(DocumentHash));
            Assert.That(parsed.DocumentBytes, Is.EqualTo(4096L));
            Assert.That(parsed.TemporalContinuity, Is.True);
            Assert.That(parsed.ToJson(), Is.EqualTo(json), "the JSON form is canonical");
        }

        [Test]
        public void AHeaderWithoutAThumbnailWritesNullAndLargeStepsKeepPrecision()
        {
            var header = new SaveSlotHeader("auto-1", "g", Fingerprint, null, string.Empty, 0.0, DateTime.UtcNow, null,
                ulong.MaxValue, DocumentHash, 1L, false);
            string json = header.ToJson();
            Assert.That(json, Does.Contain("\"thumbnailPath\": null"));
            Assert.That(SaveSlotHeader.TryParse(json, out SaveSlotHeader? parsed, out string error), Is.True, error);
            Assert.That(parsed!.ThumbnailPath, Is.Null);
            Assert.That(parsed.LogicalStep, Is.EqualTo(ulong.MaxValue));
            Assert.That(parsed.SchemaVersions, Is.Empty);
        }

        [Test]
        public void UnknownKeysAreIgnoredForForwardCompatibility()
        {
            string json = Sample().ToJson().Replace("\"format\": 1,", "\"format\": 1, \"futureField\": { \"a\": [1, 2, true] },");
            Assert.That(SaveSlotHeader.TryParse(json, out SaveSlotHeader? parsed, out string error), Is.True, error);
            Assert.That(parsed!.Slot, Is.EqualTo("slot-1"));
        }

        [Test]
        public void MalformedOrIncompleteHeadersRefuseWithAReason()
        {
            string good = Sample().ToJson();
            AssertRefused(null, "empty");
            AssertRefused(string.Empty, "empty");
            AssertRefused("[1,2]", "not a JSON object");
            AssertRefused("{\"format\": 1", "not valid JSON");
            AssertRefused(good.Replace("\"gameId\": \"hollowmere\",", string.Empty), "gameId");
            AssertRefused(good.Replace(Fingerprint, "XYZ"), "fingerprint");
            AssertRefused(good.Replace("\"logicalStep\": 1234", "\"logicalStep\": -1"), "logicalStep");
            AssertRefused(good.Replace("\"logicalStep\": 1234", "\"logicalStep\": 1.5"), "logicalStep");
            AssertRefused(good.Replace("\"slot\": \"slot-1\"", "\"slot\": \"../evil\""), "slot names");
            AssertRefused(good.Replace("\"temporalContinuity\": true", "\"temporalContinuity\": \"yes\""), "temporalContinuity");
            AssertRefused(good.Replace("2026-10-04T12:30:15.125Z", "yesterday"), "savedAtUtc");
            AssertRefused(good + "garbage", "not valid JSON");
            AssertRefused(good.Replace("\"format\": 1,", "\"format\": 1, \"format\": 1,"), "duplicate key");
        }

        [Test]
        public void ANewerFormatSaysTheSaveIsFromANewerBuild()
        {
            string json = Sample().ToJson().Replace("\"format\": 1,", "\"format\": 2,");
            Assert.That(SaveSlotHeader.TryParse(json, out SaveSlotHeader? parsed, out string error), Is.False);
            Assert.That(parsed, Is.Null);
            Assert.That(error, Does.Contain("newer build"));
        }

        [Test]
        public void SlotNamesAreSafeFileStems()
        {
            foreach (string valid in new[] { "a", "slot-1", "auto_3", "quick", "0", new string('x', 64) })
            {
                Assert.That(SaveSlotNames.IsValid(valid), Is.True, valid);
            }

            foreach (string? invalid in new[] { null, string.Empty, "-a", "_a", "Slot", "a b", "a/b", "..", "a.b", new string('x', 65) })
            {
                Assert.That(SaveSlotNames.IsValid(invalid), Is.False, invalid ?? "null");
            }

            Assert.Throws<ArgumentException>(() => new SaveSlotHeader("Bad Name", "g", Fingerprint, null, "", 0.0, DateTime.UtcNow, null, 0UL, DocumentHash, 1L, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SaveSlotHeader("ok", "g", Fingerprint, null, "", double.NaN, DateTime.UtcNow, null, 0UL, DocumentHash, 1L, false));
        }

        private static void AssertRefused(string? json, string expected)
        {
            Assert.That(SaveSlotHeader.TryParse(json, out SaveSlotHeader? parsed, out string error), Is.False, "accepted: " + json);
            Assert.That(parsed, Is.Null);
            Assert.That(error, Does.Contain(expected), error);
        }
    }

    [TestFixture]
    public sealed class SaveRulesTests
    {
        [Test]
        public void DefaultGatesBlockCaptureInEveryListedStateAndRestoreOnlyInTransitions()
        {
            SaveGateRules rules = SaveGateRules.Default;
            Assert.That(rules.MayCapture(SaveGateState.None).Allowed, Is.True);
            SaveGateVerdict combat = rules.MayCapture(SaveGateState.Combat);
            Assert.That(combat.Allowed, Is.False);
            Assert.That(combat.CodeId, Is.EqualTo("save.unsafe-state"));
            Assert.That(combat.Hint, Does.Contain("combat"));
            Assert.That(rules.MayRestore(SaveGateState.Combat | SaveGateState.Cutscene).Allowed, Is.True);
            Assert.That(rules.MayRestore(SaveGateState.SceneTransition).Allowed, Is.False);
            Assert.That(rules.MayCapture(SaveGateState.Cutscene | SaveGateState.Dialogue).Hint, Does.Contain("a cutscene or a dialogue"));
        }

        [Test]
        public void TheSlotLayoutOffersQuickManualAndAutoSlotsOnly()
        {
            var policy = new SaveSlotPolicy(3, 2);
            Assert.That(policy.ManualSlot(1), Is.EqualTo("slot-1"));
            Assert.That(policy.AutoSlot(2), Is.EqualTo("auto-2"));
            foreach (string offered in new[] { "quick", "slot-1", "slot-3", "auto-1", "auto-2" })
            {
                Assert.That(policy.IsOffered(offered), Is.True, offered);
                Assert.That(SaveSlotNames.IsValid(offered), Is.True, "every offered slot is a valid save slot name: " + offered);
            }

            foreach (string? refused in new[] { null, "", "slot-0", "slot-4", "slot-01", "auto-3", "slot-", "slot-x", "quick2" })
            {
                Assert.That(policy.IsOffered(refused), Is.False, refused ?? "null");
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => policy.ManualSlot(4));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SaveSlotPolicy(-1, 1));
        }

        [Test]
        public void TheNextAutosaveFillsFreeSlotsThenOverwritesTheOldest()
        {
            var policy = new SaveSlotPolicy(1, 3);
            Assert.That(policy.NextAutosave(null), Is.EqualTo("auto-1"));
            var existing = new Dictionary<string, DateTime>
            {
                ["auto-1"] = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc),
            };
            Assert.That(policy.NextAutosave(existing), Is.EqualTo("auto-2"));
            existing["auto-2"] = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            existing["auto-3"] = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
            Assert.That(policy.NextAutosave(existing), Is.EqualTo("auto-2"));
        }
    }
}

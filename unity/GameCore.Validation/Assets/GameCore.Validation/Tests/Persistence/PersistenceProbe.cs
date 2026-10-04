// GameCore.Persistence tests - the synthetic save world of P1.2 (SADR-012 (studio)).
//
// The world is the P0.4 application probe (one catalog, one probe plugin mounted over one probe target, one stage)
// with owner state rows seeded on its targets, so a save carries slots, a composition with one installation, a step,
// domain seconds and (in the fixed-step variant) retained debt. The checkpoint codecs are the committed generated
// checkpoint catalog of the qualification project (GC-018), the same set every restore gate uses.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using GameCore.App.Tests;
using GameCore.Contracts;
using GameCore.Execution.Persistence;
using GameCore.Unity.App;
using GameCore.Unity.Runtime;
using GameCore.Validation.ProbeHost;
using NUnit.Framework;
using UnityEngine;

namespace GameCore.Persistence.Tests
{
    /// <summary>Vocabulary and helpers of the save probe.</summary>
    public static class PersistenceProbe
    {
        public const string GameId = "gamecore.persistence.probe";
        public const string HealthSchemaName = "persistence.probe.health.schema";

        public static readonly OwnerId Owner = new OwnerId(StableNameKeyDerivation.Derive("persistence.probe.owner"));
        public static readonly SlotId Health = new SlotId(StableNameKeyDerivation.Derive("persistence.probe.health"));
        public static readonly SlotId Dormant = new SlotId(StableNameKeyDerivation.Derive("persistence.probe.dormant"));
        public static readonly SchemaId HealthSchema = new SchemaId(StableNameKeyDerivation.Derive(HealthSchemaName));
        public static readonly Id128 InputSource = StableNameKeyDerivation.Derive("persistence.probe.input.source");

        public static SchemaRef HealthAt(uint version) => new SchemaRef(HealthSchema, version);

        public static CheckpointCodecSet Codecs()
        {
            Assert.That(Gc018CheckpointCodecs.TryBuild(out _, out CheckpointCodecSet? codecs, out string detail), Is.True, detail);
            return codecs!;
        }

        public static string TempDirectory() =>
            Path.Combine(Application.temporaryCachePath, "gc-p12-" + Guid.NewGuid().ToString("N"));

        public static SaveServiceOptions Options(string directory, uint healthVersion, SlotMigrationRegistry? migrations = null)
        {
            var options = new SaveServiceOptions(GameId, Codecs())
            {
                Directory = directory,
                RegionId = () => "persistence.region",
                PlayTimeSeconds = () => 12.5,
                SlotSchemas = new SlotSchemaCatalog(new[] { new SlotSchemaBinding(Owner, Health, HealthAt(healthVersion), HealthSchemaName) }),
                SlotMigrations = migrations ?? new SlotMigrationRegistry(),
                SchemaVersions = new[] { new SaveSchemaVersion(HealthSchemaName, healthVersion) },
            };
            return options;
        }

        /// <summary>Seeds the probe's owner rows on one target: an active health row and a dormant row.</summary>
        public static void SeedRows(GameApplicationRoot root, TargetId target, uint healthVersion, int health)
        {
            Assert.That(root.Seeder.TrySeedSlot(target, Owner, Health, healthVersion, health, true, out DiagnosticCode code, out string detail), Is.True, code + ": " + detail);
            Assert.That(root.Seeder.TrySeedSlot(target, Owner, Dormant, 1U, -7, false, out code, out detail), Is.True, code + ": " + detail);
        }

        public static TargetId ExtraTarget(int index) =>
            new TargetId(StableNameKeyDerivation.Derive("persistence.probe.target." + index.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        /// <summary>
        /// Rewrites a verified document record by record, optionally replacing the header, mapping every target row
        /// and appending one issuer high-water cursor - the test's way to fabricate documents a real world of the probe
        /// cannot produce (another catalog, another recipe revision, an issuer with history).
        /// </summary>
        public static byte[] Rebuild(
            byte[] document,
            CheckpointCodecSet codecs,
            IReadOnlyList<Id128> features,
            Func<HeaderRecordValue, HeaderRecordValue>? header = null,
            Func<TargetRecordValue, TargetRecordValue>? target = null,
            CursorRecordValue? extraCursor = null)
        {
            Assert.That(CheckpointDocument.TryRead(document, codecs, out CheckpointDocument? read, out DiagnosticCode code, out string detail), Is.True, detail);
            CheckpointDocument doc = read!;
            var serializer = new CheckpointSerializer(codecs, null, features);
            Copy<ScopeRecordValue>(doc, CheckpointRecordKind.Scope, serializer, null);
            Copy<InstallRecordValue>(doc, CheckpointRecordKind.Install, serializer, null);
            Copy<SelectionRecordValue>(doc, CheckpointRecordKind.Selection, serializer, null);
            Copy(doc, CheckpointRecordKind.Target, serializer, target);
            Copy<SlotRecordValue>(doc, CheckpointRecordKind.Slot, serializer, null);
            Copy<GrantRecordValue>(doc, CheckpointRecordKind.Grant, serializer, null);
            Copy<ClockRecordValue>(doc, CheckpointRecordKind.Clock, serializer, null);
            Copy<CommandRecordValue>(doc, CheckpointRecordKind.Command, serializer, null);
            Copy<MessageRecordValue>(doc, CheckpointRecordKind.Message, serializer, null);
            Copy<RngRecordValue>(doc, CheckpointRecordKind.Rng, serializer, null);
            Copy<CursorRecordValue>(doc, CheckpointRecordKind.Cursor, serializer, null);
            if (extraCursor.HasValue)
            {
                Assert.That(serializer.TryAdd(CheckpointRecordKind.Cursor, extraCursor.Value, out code, out detail), Is.True, detail);
            }

            Copy<OutboxRecordValue>(doc, CheckpointRecordKind.Outbox, serializer, null);
            HeaderRecordValue h = header != null ? header(doc.Header) : doc.Header;
            uint cursors = h.CursorCount + (extraCursor.HasValue ? 1U : 0U);
            var rebuilt = new HeaderRecordValue(
                h.WorldDefinitionHigh, h.WorldDefinitionLow, h.SourceSessionHigh, h.SourceSessionLow, h.ProtocolMajor,
                h.ProtocolMinor, h.TemporalModel, h.StepDurationTicks, h.TicksPerSecond, h.MaxStepsPerPump,
                h.UsesUnscaledHostClock, h.LogicalStep, h.TimeDebtTicks, h.DomainSeconds, h.PendingDemand, h.PropagationMode,
                h.CatalogFingerprintA, h.CatalogFingerprintB, h.CatalogFingerprintC, h.CatalogFingerprintD, h.QueuePolicy,
                h.AdmissionCutoff, h.RejectedQueuedCount, h.LastEventSequence, h.ScopeCount, h.InstallCount, h.SelectionCount,
                h.TargetCount, h.SlotCount, h.GrantCount, h.ClockCount, h.CommandCount, h.MessageCount, h.RngStreamCount,
                cursors, h.SourcePublishedRevision, h.SourcePublishedEpoch, h.SourceHostTicksPerSecond, h.ContentRevisionCount,
                h.OutboxCount);
            Assert.That(serializer.TrySerialize(rebuilt, out byte[] bytes, out code, out detail), Is.True, code + ": " + detail);
            return bytes;
        }

        /// <summary>A header naming another catalog fingerprint.</summary>
        public static HeaderRecordValue WithForeignCatalog(HeaderRecordValue h) =>
            new HeaderRecordValue(
                h.WorldDefinitionHigh, h.WorldDefinitionLow, h.SourceSessionHigh, h.SourceSessionLow, h.ProtocolMajor,
                h.ProtocolMinor, h.TemporalModel, h.StepDurationTicks, h.TicksPerSecond, h.MaxStepsPerPump,
                h.UsesUnscaledHostClock, h.LogicalStep, h.TimeDebtTicks, h.DomainSeconds, h.PendingDemand, h.PropagationMode,
                h.CatalogFingerprintA ^ 0x5A5AUL, h.CatalogFingerprintB, h.CatalogFingerprintC, h.CatalogFingerprintD, h.QueuePolicy,
                h.AdmissionCutoff, h.RejectedQueuedCount, h.LastEventSequence, h.ScopeCount, h.InstallCount, h.SelectionCount,
                h.TargetCount, h.SlotCount, h.GrantCount, h.ClockCount, h.CommandCount, h.MessageCount, h.RngStreamCount,
                h.CursorCount, h.SourcePublishedRevision, h.SourcePublishedEpoch, h.SourceHostTicksPerSecond, h.ContentRevisionCount,
                h.OutboxCount);

        /// <summary>A target row naming its recipe at another content revision.</summary>
        public static TargetRecordValue WithRecipeRevision(TargetRecordValue t, ulong revision) =>
            new TargetRecordValue(
                t.TargetHigh, t.TargetLow, t.ScopeHigh, t.ScopeLow, t.DefinitionHigh, t.DefinitionLow, t.SchemaHigh, t.SchemaLow,
                t.SchemaVersion, revision, t.SourceSlot, t.SourceGeneration);

        /// <summary>Writes a document and a matching header into a slot, as a SaveService capture would.</summary>
        public static void WriteSlot(SaveService service, string slot, byte[] document, bool continuity, ContentHash catalog)
        {
            Directory.CreateDirectory(service.Directory);
            var store = new GameCore.Execution.Recovery.FileCheckpointStore(service.DocumentPath(slot));
            Assert.That(store.TryPublish(document, out _, out DiagnosticCode code, out string detail), Is.True, code + ": " + detail);
            var header = new SaveSlotHeader(
                slot, GameId, catalog.ToHex(), null, string.Empty, 1.0, DateTime.UtcNow, null, 0UL,
                ContentHash.Compute(document).ToHex(), document.LongLength, continuity);
            File.WriteAllText(service.HeaderPath(slot), header.ToJson());
        }

        private static void Copy<T>(CheckpointDocument doc, CheckpointRecordKind kind, CheckpointSerializer serializer, Func<T, T>? map)
            where T : struct
        {
            Assert.That(doc.TryReadRecords<T>(kind, out IReadOnlyList<T> rows, out DiagnosticCode code, out string detail), Is.True, detail);
            for (int i = 0; i < rows.Count; i++)
            {
                T row = map != null ? map(rows[i]) : rows[i];
                Assert.That(serializer.TryAdd(kind, row, out code, out detail), Is.True, kind + ": " + detail);
            }
        }
    }
}

// GameCore.Persistence tests - P1.2 save-restore acceptance (SADR-012 (studio), catalog row 12).
//
// Every test boots the application probe through `GameApplication.Boot`, seeds owner rows, advances the world and
// saves it through the real SaveService into a temporary directory, then restores it through the production restore
// builder. Pumps go through the sanctioned application pump with a test frame clock, except the fixed-step test,
// which drives host ticks directly so the retained debt is exact.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using GameCore.App.Tests;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Execution.Persistence;
using GameCore.Gameplay.Save;
using GameCore.Planning;
using GameCore.Unity.Adapters;
using GameCore.Unity.Adapters.Input;
using GameCore.Unity.App;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Persistence;
using NUnit.Framework;

namespace GameCore.Persistence.Tests
{
    [TestFixture]
    public sealed class SaveRestoreTests
    {
        private long frame;
        private bool pumpWasEnabled;
        private string directory = string.Empty;
        private readonly List<SaveService> services = new List<SaveService>();
        private readonly List<GameApplicationRoot> roots = new List<GameApplicationRoot>();

        [SetUp]
        public void SetUp()
        {
            AppProbeRecorder.Reset();
            frame = 1000L;
            pumpWasEnabled = GameCoreApplicationPump.IsEnabled;
            GameCoreApplicationPump.IsEnabled = true;
            GameCoreThreading.CaptureMainThread();
            directory = PersistenceProbe.TempDirectory();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < services.Count; i++)
            {
                StopQuietly(services[i].ActiveRoot);
            }

            for (int i = 0; i < roots.Count; i++)
            {
                StopQuietly(roots[i]);
            }

            if (GameApplication.Current != null)
            {
                StopQuietly(GameApplication.Current);
            }

            services.Clear();
            roots.Clear();
            GameCoreApplicationPump.IsEnabled = pumpWasEnabled;
            AppProbeRecorder.Reset();
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
            catch (IOException)
            {
                // A leftover temporary directory is harmless; the next run uses a fresh one.
            }
        }

        // ------------------------------------------------------------------ round trip

        [Test]
        public void CaptureThenRestore_GivesAnEqualSlotHash_TheSameStepAndDomainTime_AndANewWorld()
        {
            GameApplicationRoot root = BootRunning();
            PersistenceProbe.SeedRows(root, AppProbe.Target, 1U, 40);
            CommitOneStep(root);
            CommitOneStep(root);
            CommitOneStep(root);
            root.Host.AdvanceDomainTime(2.5);
            SaveService service = Service(root, PersistenceProbe.Options(directory, 1U));

            SaveResult saved = service.Capture("slot-1", "thumbs/slot-1.png");
            Assert.That(saved.Succeeded, Is.True, saved.ToString());
            Assert.That(File.Exists(service.DocumentPath("slot-1")), Is.True);
            Assert.That(File.Exists(service.HeaderPath("slot-1")), Is.True);
            Assert.That(saved.Header!.LogicalStep, Is.EqualTo(3UL));
            Assert.That(saved.Header.TemporalContinuity, Is.True);
            Assert.That(saved.Header.GameId, Is.EqualTo(PersistenceProbe.GameId));
            Assert.That(saved.Header.CatalogFingerprint, Is.EqualTo(root.CatalogHash.ToHex()));
            Assert.That(saved.Header.RegionId, Is.EqualTo("persistence.region"));
            Assert.That(saved.Header.PlayTimeSeconds, Is.EqualTo(12.5));
            Assert.That(saved.Header.ThumbnailPath, Is.EqualTo("thumbs/slot-1.png"));
            Assert.That(root.State, Is.EqualTo(GameApplicationState.Running), "a capture of a running world resumes it");

            WorldId before = root.World;
            SaveResult restored = service.Restore("slot-1");
            Assert.That(restored.Succeeded, Is.True, restored + " | " + restored.Build?.Describe());
            GameApplicationRoot next = service.ActiveRoot;
            Assert.That(next, Is.Not.SameAs(root));
            Assert.That(next.World.Session, Is.Not.EqualTo(before.Session), "a restore creates a new WorldId (P-049)");
            Assert.That(restored.RestoredWorld.Session, Is.EqualTo(next.World.Session));
            Assert.That(root.State, Is.EqualTo(GameApplicationState.Stopped), "the previous root stops");
            Assert.That(next.State, Is.EqualTo(GameApplicationState.Running));
            Assert.That(next.Host.CurrentStep.Value, Is.EqualTo(3UL), "the step continues (SADR-012)");
            Assert.That(next.Host.DomainSeconds, Is.EqualTo(2.5), "domain time continues (P-038)");
            Assert.That(next.Host.RetainedDebt.Ticks, Is.EqualTo(0UL));
            Assert.That(restored.Origin!.Source, Is.EqualTo(TemporalOriginSource.Continued));
            Assert.That(next.Host.RestoredOrigin.IsContinued, Is.True);
            Assert.That(restored.Build!.Targets, Is.EqualTo(1));
            Assert.That(restored.Build.Slots, Is.EqualTo(2));
            Assert.That(restored.Build.DormantSlots, Is.EqualTo(1));
            Assert.That(restored.Build.ReplayedInstalls, Is.EqualTo(1));
            Assert.That(restored.Build.Publications, Is.EqualTo(1), "one install, one publication; targets and slots cost none");
            Assert.That(next.Lane.FindInstall(AppProbe.ProbeInstance), Is.Not.Null);

            SaveResult again = service.Capture("slot-2");
            Assert.That(again.Succeeded, Is.True, again.ToString());
            Assert.That(again.SlotHash, Is.EqualTo(saved.SlotHash), "capture -> restore -> capture keeps the canonical slot hash");
            Assert.That(again.Header!.LogicalStep, Is.EqualTo(3UL));

            CommitOneStep(next);
            Assert.That(next.Host.CurrentStep.Value, Is.EqualTo(4UL), "the restored world's next step follows the saved one");
        }

        [Test]
        public void FixedStepRetainedDebtIsRestoredAndOwedAtTheFirstRunningSample()
        {
            var fixedStep = new FixedStepSettings(100UL, 1000UL, 8U, false);
            GameApplicationRoot root = Boot(AppProbe.Definition().WithWorld(AppProbe.WorldDefinition, TemporalModel.FixedStep, fixedStep).Build());
            root.Start();
            PersistenceProbe.SeedRows(root, AppProbe.Target, 1U, 5);
            root.Host.PumpFrame(10000UL);
            root.Host.PumpFrame(10250UL);
            Assert.That(root.Host.CurrentStep.Value, Is.EqualTo(2UL));
            Assert.That(root.Host.RetainedDebt.Ticks, Is.EqualTo(50UL));
            SaveService service = Service(root, PersistenceProbe.Options(directory, 1U));

            Assert.That(service.Capture("fixed").Succeeded, Is.True);
            SaveResult restored = service.Restore("fixed");
            Assert.That(restored.Succeeded, Is.True, restored + " | " + restored.Build?.Describe());
            UnityWorldHost host = service.ActiveRoot.Host;
            Assert.That(host.CurrentStep.Value, Is.EqualTo(2UL));
            Assert.That(host.RetainedDebt.Ticks, Is.EqualTo(50UL), "restored debt is owed before the first sample");
            Assert.That(host.PendingRestoredDebtTicks, Is.EqualTo(50UL));

            host.PumpFrame(500000UL);
            Assert.That(host.CurrentStep.Value, Is.EqualTo(2UL), "the first pump only captures the host origin");
            Assert.That(host.RetainedDebt.Ticks, Is.EqualTo(50UL));
            host.PumpFrame(500060UL);
            Assert.That(host.CurrentStep.Value, Is.EqualTo(3UL), "50 restored + 60 new ticks admit one 100-tick step");
            Assert.That(host.RetainedDebt.Ticks, Is.EqualTo(10UL));
            Assert.That(host.PendingRestoredDebtTicks, Is.EqualTo(0UL));
        }

        [Test]
        public void IssuerSequencesContinueAboveTheSavedHighWaterMark()
        {
            GameApplicationRoot root = BootRunning();
            PersistenceProbe.SeedRows(root, AppProbe.Target, 1U, 1);
            CommitOneStep(root);
            SaveService service = Service(root, PersistenceProbe.Options(directory, 1U));
            Assert.That(service.Capture("issuer").Succeeded, Is.True);

            // The probe world has no message plane, so the saved document is given one issuer with history.
            CheckpointCodecSet codecs = service.Options.Codecs;
            Assert.That(new GameCore.Execution.Recovery.FileCheckpointStore(service.DocumentPath("issuer"))
                .TryRead(out byte[]? document, out _, out DiagnosticCode code, out string detail), Is.True, detail);
            var mark = new CursorRecordValue((uint)CursorRowKind.IssuerHighWater, PersistenceProbe.InputSource.High, PersistenceProbe.InputSource.Low, 41UL, root.World.Session.High, root.World.Session.Low);
            byte[] withIssuer = PersistenceProbe.Rebuild(document!, codecs, CheckpointFormat.TemporalContinuityFeatureIds, null, null, mark);
            PersistenceProbe.WriteSlot(service, "issuer", withIssuer, true, root.CatalogHash);

            SaveResult restored = service.Restore("issuer");
            Assert.That(restored.Succeeded, Is.True, restored + " | " + restored.Build?.Describe());
            UnityWorldHost host = service.ActiveRoot.Host;
            Assert.That(host.RestoredOrigin.TryGetIssuerHighWater(PersistenceProbe.InputSource, out ulong highWater), Is.True);
            Assert.That(highWater, Is.EqualTo(41UL));

            var frameOfInput = new WorldAdapterFrame(
                host,
                new TypedInputIngress(host.World, host),
                new InputBindingTable(),
                new SilentDevice(PersistenceProbe.InputSource));
            Assert.That(frameOfInput.ResumedFromSequence, Is.EqualTo(41UL));
            Assert.That(frameOfInput.DeviceSequence, Is.EqualTo(41UL), "the next stamped sequence is 42: no reuse (P-050)");
            frameOfInput.ResumeIssuerSequence(5UL);
            Assert.That(frameOfInput.DeviceSequence, Is.EqualTo(41UL), "a high-water mark never moves backwards");

            var unrelated = new WorldAdapterFrame(host, new TypedInputIngress(host.World, host), new InputBindingTable(), new SilentDevice(new Id128(9UL, 9UL)));
            Assert.That(unrelated.DeviceSequence, Is.EqualTo(0UL));
        }

        // ------------------------------------------------------------------ migrations

        [Test]
        public void AV1SaveIsMigratedToV2OnRestore()
        {
            GameApplicationRoot root = BootRunning();
            PersistenceProbe.SeedRows(root, AppProbe.Target, 1U, 7);
            SaveService v1 = Service(root, PersistenceProbe.Options(directory, 1U));
            Assert.That(v1.Capture("migrate").Succeeded, Is.True);

            var migrations = new SlotMigrationRegistry(new[]
            {
                new SlotMigrationStep("persistence.probe.health.v1-to-v2", PersistenceProbe.HealthAt(1U), PersistenceProbe.HealthAt(2U), value => value * 10),
            });
            SaveService v2 = Service(root, PersistenceProbe.Options(directory, 2U, migrations));
            SaveResult restored = v2.Restore("migrate");
            Assert.That(restored.Succeeded, Is.True, restored + " | " + restored.Build?.Describe());
            Assert.That(restored.Migration!.MigratedRows, Is.EqualTo(1));
            Assert.That(restored.Migration.AppliedMigrations, Is.EqualTo(new[] { "persistence.probe.health.v1-to-v2" }));

            IReadOnlyList<LiveSlotState> live = v2.ActiveRoot.Seeder.ReadLiveSlots(new[] { AppProbe.Target });
            LiveSlotState health = Find(live, PersistenceProbe.Health);
            Assert.That(health.SchemaVersion, Is.EqualTo(2U));
            Assert.That(health.Value, Is.EqualTo(70));
            Assert.That(Find(live, PersistenceProbe.Dormant).Value, Is.EqualTo(-7), "an unbound slot is carried unchanged");
        }

        [Test]
        public void AMissingMigrationRefusesAndLeavesTheRunningWorldUntouched()
        {
            GameApplicationRoot root = BootRunning();
            PersistenceProbe.SeedRows(root, AppProbe.Target, 1U, 7);
            SaveService v1 = Service(root, PersistenceProbe.Options(directory, 1U));
            Assert.That(v1.Capture("old").Succeeded, Is.True);

            SaveService v3 = Service(root, PersistenceProbe.Options(directory, 3U));
            SaveResult refused = v3.Restore("old");
            Assert.That(refused.Succeeded, Is.False);
            Assert.That(refused.Refusal!.Code, Is.EqualTo(SaveRefusalCode.MigrationPathMissing));
            Assert.That(refused.Refusal.KernelCode, Is.EqualTo(DiagnosticCode.MigrationRequired));
            Assert.That(refused.Refusal.Hint, Does.Contain(PersistenceProbe.HealthSchemaName + " v1 -> v3"));
            Assert.That(v3.ActiveRoot, Is.SameAs(root));
            Assert.That(root.State, Is.EqualTo(GameApplicationState.Running));
            Assert.That(root.Host.Lifecycle, Is.EqualTo(WorldLifecycleState.Running));
        }

        // ------------------------------------------------------------------ refusals

        [Test]
        public void ACorruptFileIsRefusedByItsChecksum()
        {
            GameApplicationRoot root = BootRunning();
            PersistenceProbe.SeedRows(root, AppProbe.Target, 1U, 3);
            SaveService service = Service(root, PersistenceProbe.Options(directory, 1U));
            Assert.That(service.Capture("corrupt").Succeeded, Is.True);

            byte[] raw = File.ReadAllBytes(service.DocumentPath("corrupt"));
            raw[raw.Length / 2] ^= 0xFF;
            File.WriteAllBytes(service.DocumentPath("corrupt"), raw);

            SaveResult refused = service.Restore("corrupt");
            Assert.That(refused.Refusal!.Code, Is.EqualTo(SaveRefusalCode.CorruptFile), refused.ToString());
            Assert.That(service.ActiveRoot, Is.SameAs(root));
            Assert.That(service.Inspect("corrupt").Refusal!.Code, Is.EqualTo(SaveRefusalCode.CorruptFile));
        }

        [Test]
        public void AHeaderThatNamesAnotherDocumentIsACorruptSlot()
        {
            GameApplicationRoot root = BootRunning();
            PersistenceProbe.SeedRows(root, AppProbe.Target, 1U, 3);
            SaveService service = Service(root, PersistenceProbe.Options(directory, 1U));
            Assert.That(service.Capture("a").Succeeded, Is.True);
            CommitOneStep(root);
            Assert.That(service.Capture("b").Succeeded, Is.True);
            File.Copy(service.HeaderPath("a"), service.HeaderPath("b"), true);

            SaveResult refused = service.Restore("b");
            Assert.That(refused.Refusal!.Code, Is.EqualTo(SaveRefusalCode.CorruptFile));
            Assert.That(refused.Refusal.Detail, Does.Contain("different saves"));
        }

        [Test]
        public void MissingSlotsInvalidNamesAndDeletesAreTyped()
        {
            GameApplicationRoot root = BootRunning();
            SaveService service = Service(root, PersistenceProbe.Options(directory, 1U));
            Assert.That(service.Restore("nothing-here").Refusal!.Code, Is.EqualTo(SaveRefusalCode.MissingSlot));
            Assert.That(service.Delete("nothing-here").Refusal!.Code, Is.EqualTo(SaveRefusalCode.MissingSlot));
            Assert.That(service.Capture("../escape").Refusal!.Code, Is.EqualTo(SaveRefusalCode.InvalidSlot));

            Assert.That(service.Capture("gone").Succeeded, Is.True);
            Assert.That(service.Exists("gone"), Is.True);
            Assert.That(service.Delete("gone").Succeeded, Is.True);
            Assert.That(service.Exists("gone"), Is.False);
            Assert.That(File.Exists(service.DocumentPath("gone")), Is.False);
        }

        [Test]
        public void ASaveFromAnotherCatalogIsRefusedUnlessTheBuildDeclaresItCompatible()
        {
            GameApplicationRoot root = BootRunning();
            PersistenceProbe.SeedRows(root, AppProbe.Target, 1U, 3);
            SaveService service = Service(root, PersistenceProbe.Options(directory, 1U));
            Assert.That(service.Capture("foreign").Succeeded, Is.True);
            Assert.That(new GameCore.Execution.Recovery.FileCheckpointStore(service.DocumentPath("foreign"))
                .TryRead(out byte[]? document, out _, out DiagnosticCode _, out string detail), Is.True, detail);
            CheckpointCodecSet codecs = service.Options.Codecs;
            byte[] foreign = PersistenceProbe.Rebuild(document!, codecs, CheckpointFormat.TemporalContinuityFeatureIds, PersistenceProbe.WithForeignCatalog);
            Assert.That(CheckpointDocument.TryRead(foreign, codecs, out CheckpointDocument? read, out DiagnosticCode _, out detail), Is.True, detail);
            PersistenceProbe.WriteSlot(service, "foreign", foreign, true, root.CatalogHash);

            SaveResult refused = service.Restore("foreign");
            Assert.That(refused.Refusal!.Code, Is.EqualTo(SaveRefusalCode.CatalogMismatch), refused.ToString());
            Assert.That(service.Inspect("foreign").Catalog, Is.EqualTo(CatalogCompatibility.Incompatible));

            SaveServiceOptions compatibleOptions = PersistenceProbe.Options(directory, 1U);
            compatibleOptions.CompatibleCatalogs = new[] { read!.Header.CatalogFingerprint };
            SaveService compatible = Service(root, compatibleOptions);
            SaveResult restored = compatible.Restore("foreign");
            Assert.That(restored.Succeeded, Is.True, restored + " | " + restored.Build?.Describe());
        }

        [Test]
        public void ARecipeAtAnotherRevisionIsATypedRefusalNamingBothRevisions()
        {
            GameApplicationRoot root = BootRunning();
            SaveService service = Service(root, PersistenceProbe.Options(directory, 1U));
            Assert.That(service.Capture("recipe").Succeeded, Is.True);
            Assert.That(new GameCore.Execution.Recovery.FileCheckpointStore(service.DocumentPath("recipe"))
                .TryRead(out byte[]? document, out _, out DiagnosticCode _, out string detail), Is.True, detail);
            byte[] stale = PersistenceProbe.Rebuild(
                document!, service.Options.Codecs, CheckpointFormat.TemporalContinuityFeatureIds, null, t => PersistenceProbe.WithRecipeRevision(t, 77UL));
            PersistenceProbe.WriteSlot(service, "recipe", stale, true, root.CatalogHash);

            SaveResult refused = service.Restore("recipe");
            Assert.That(refused.Refusal!.Code, Is.EqualTo(SaveRefusalCode.CatalogMismatch), refused.ToString());
            Assert.That(refused.Build!.Refusal, Is.EqualTo(ProductionRestoreRefusal.RecipeRevisionMismatch));
            Assert.That(refused.Build.Detail, Does.Contain("revision 77"));
            Assert.That(refused.Build.Detail, Does.Contain("revision " + AppProbe.Recipe.Revision.Value));
            Assert.That(refused.Build.Detail, Does.Contain(AppProbe.Recipe.Id.ToString()));
            Assert.That(service.ActiveRoot, Is.SameAs(root));
        }

        [Test]
        public void ARestoreDuringAnUnsafeStateIsRefused()
        {
            GameApplicationRoot root = BootRunning();
            SaveService service = Service(root, PersistenceProbe.Options(directory, 1U));
            Assert.That(service.Capture("unsafe").Succeeded, Is.True);
            root.Stop("make the active world unsafe");
            SaveResult refused = service.Restore("unsafe");
            Assert.That(refused.Refusal!.Code, Is.EqualTo(SaveRefusalCode.UnsafeState));
            Assert.That(service.Capture("unsafe-2").Refusal!.Code, Is.EqualTo(SaveRefusalCode.UnsafeState));
        }

        [Test]
        public void AnOldFormatCheckpointLoadsAtStepZeroAndReportsIt()
        {
            GameApplicationRoot root = BootRunning();
            PersistenceProbe.SeedRows(root, AppProbe.Target, 1U, 9);
            CommitOneStep(root);
            CommitOneStep(root);
            root.Host.AdvanceDomainTime(4.0);
            SaveService service = Service(root, PersistenceProbe.Options(directory, 1U));
            Assert.That(service.Capture("legacy").Succeeded, Is.True);

            Assert.That(new GameCore.Execution.Recovery.FileCheckpointStore(service.DocumentPath("legacy"))
                .TryRead(out byte[]? document, out _, out DiagnosticCode code, out string detail), Is.True, detail);
            Assert.That(CheckpointDocument.TryRead(document, service.Options.Codecs, out CheckpointDocument? read, out code, out detail), Is.True, detail);
            Assert.That(read!.TryRewrite(null, CheckpointFormat.KnownFeatureIds, out byte[] legacy, out code, out detail), Is.True, detail);
            PersistenceProbe.WriteSlot(service, "legacy", legacy, false, root.CatalogHash);
            Assert.That(service.Inspect("legacy").DeclaresTemporalContinuity, Is.False);

            SaveResult restored = service.Restore("legacy");
            Assert.That(restored.Succeeded, Is.True, restored + " | " + restored.Build?.Describe());
            Assert.That(restored.Origin!.Source, Is.EqualTo(TemporalOriginSource.LegacyStepZero));
            Assert.That(restored.Origin.Detail, Does.Contain(CheckpointFormat.TemporalContinuityFeatureStableName));
            Assert.That(service.ActiveRoot.Host.CurrentStep.Value, Is.EqualTo(0UL), "V1 step-0 semantics");
            Assert.That(service.ActiveRoot.Host.DomainSeconds, Is.EqualTo(0.0));
            Assert.That(service.ActiveRoot.Host.RestoredOrigin.IsLegacyStepZero, Is.True);
            Assert.That(restored.SlotHash, Is.EqualTo(SaveService.SlotHashOf(read)), "the state itself is restored exactly");
        }

        // ------------------------------------------------------------------ gameplay save plugin

        [Test]
        public void SaveCommandsRaiseOneEventEachAndTheCatalogListsSlots()
        {
            GameApplicationRoot root = BootRunning();
            PersistenceProbe.SeedRows(root, AppProbe.Target, 1U, 3);
            SaveService service = Service(root, PersistenceProbe.Options(directory, 1U));
            var host = new SaveCommandHost(service);
            var written = new List<SaveWritten>();
            var restoredEvents = new List<SaveRestored>();
            var refusedEvents = new List<SaveRefused>();
            host.Written += written.Add;
            host.Restored += restoredEvents.Add;
            host.Refused += refusedEvents.Add;

            host.Handle(SaveCommand.Capture("slot-1"));
            host.Handle(SaveCommand.Restore("slot-9"));
            host.Handle(SaveCommand.Restore("slot-1"));
            Assert.That(host.Handle("save.unknown", "slot-1"), Is.Null);

            Assert.That(written.Count, Is.EqualTo(1));
            Assert.That(restoredEvents.Count, Is.EqualTo(1));
            Assert.That(restoredEvents[0].Origin.IsContinued, Is.True);
            Assert.That(restoredEvents[0].RestoredWorld.Session, Is.Not.EqualTo(restoredEvents[0].PreviousWorld.Session));
            Assert.That(refusedEvents.Count, Is.EqualTo(2));
            Assert.That(refusedEvents[0].CodeId, Is.EqualTo("save.missing-slot"));
            Assert.That(refusedEvents[0].Hint, Is.Not.Empty);

            var catalog = new SaveServiceSlotCatalog(service);
            Assert.That(catalog.Slots.Count, Is.EqualTo(1));
            Assert.That(catalog.TryGet("slot-1", out SaveSlotHeader? header), Is.True);
            Assert.That(header!.GameId, Is.EqualTo(PersistenceProbe.GameId));
            File.WriteAllText(Path.Combine(service.Directory, "broken.json"), "{ not json");
            catalog.Refresh();
            Assert.That(catalog.Unreadable, Is.EqualTo(new[] { "broken" }));
        }

        [Test]
        public void StudioInspectAndRoundTripOperationsReportWithoutChangingTheGame()
        {
            GameApplicationRoot root = BootRunning();
            PersistenceProbe.SeedRows(root, AppProbe.Target, 1U, 3);
            CommitOneStep(root);
            SaveService service = Service(root, PersistenceProbe.Options(directory, 1U));
            Assert.That(service.Capture("studio").Succeeded, Is.True);
            SaveSchemaDefinition schema = UnityEngine.ScriptableObject.CreateInstance<SaveSchemaDefinition>();
            try
            {
                schema.gameId = PersistenceProbe.GameId;
                SaveSlotInspection inspection = SaveStudioOperations.Inspect(schema, "studio", service);
                Assert.That(inspection.Readable, Is.True, inspection.ToString());
                Assert.That(inspection.LogicalStep, Is.EqualTo(1UL));
                Assert.That(inspection.Slots, Is.EqualTo(2));
                Assert.That(inspection.Catalog, Is.EqualTo(CatalogCompatibility.Identical));
                Assert.That(inspection.Migration!.Succeeded, Is.True);

                SaveRoundTripReport report = SaveStudioOperations.TestRoundTrip(schema, service);
                Assert.That(report.Refusal, Is.Null, report.ToString());
                Assert.That(report.Equal, Is.True, report.Detail);
                Assert.That(report.RestoredWorld.Session, Is.Not.EqualTo(report.SourceWorld.Session));
                Assert.That(service.ActiveRoot, Is.SameAs(root), "the round trip keeps the running world");
                Assert.That(root.State, Is.EqualTo(GameApplicationState.Running));
                Assert.That(UnityWorldRegistry.TryGet(report.RestoredWorld, out UnityWorldHost? _), Is.False, "the round-trip world is stopped");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(schema);
            }
        }

        [Test]
        public void SaveSchemaDefinitionBuildsTheCatalogAndNamesMissingMigrationBodies()
        {
            SaveSchemaDefinition schema = UnityEngine.ScriptableObject.CreateInstance<SaveSchemaDefinition>();
            try
            {
                schema.gameId = PersistenceProbe.GameId;
                schema.slotSchemas.Add(new SaveSlotSchemaEntry { owner = "persistence.probe.owner", slot = "persistence.probe.health", schema = PersistenceProbe.HealthSchemaName, currentVersion = 2U });
                schema.migrations.Add(new SaveMigrationEntry { migrationId = "persistence.probe.health.v1-to-v2", schema = PersistenceProbe.HealthSchemaName, fromVersion = 1U, toVersion = 2U });
                schema.migrations.Add(new SaveMigrationEntry { migrationId = "persistence.probe.health.v0-to-v1", schema = PersistenceProbe.HealthSchemaName, fromVersion = 0U, toVersion = 1U });
                Assert.That(schema.Validate().IsValid, Is.True, schema.Validate().ToString());

                SlotSchemaCatalog catalog = schema.ToSlotSchemaCatalog();
                Assert.That(catalog.TryGet(PersistenceProbe.Owner, PersistenceProbe.Health, out SlotSchemaBinding binding), Is.True);
                Assert.That(binding.Current, Is.EqualTo(PersistenceProbe.HealthAt(2U)));

                var bodies = new[] { new SlotMigrationStep("persistence.probe.health.v1-to-v2", PersistenceProbe.HealthAt(1U), PersistenceProbe.HealthAt(2U), v => v) };
                SlotMigrationRegistry registry = schema.BuildMigrations(bodies, out IReadOnlyList<string> problems);
                Assert.That(registry.Count, Is.EqualTo(1));
                Assert.That(problems.Count, Is.EqualTo(1));
                Assert.That(problems[0], Does.Contain("save needs migration persistence.probe.health.v0-to-v1"));

                schema.migrations.Add(new SaveMigrationEntry { migrationId = "persistence.probe.backward", schema = PersistenceProbe.HealthSchemaName, fromVersion = 2U, toVersion = 1U });
                Assert.That(schema.Validate().IsValid, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(schema);
            }
        }

        [Test]
        public void TheRestoreHookSeamSuppliesTheProductionBuilder()
        {
            var hook = new SaveRestoreHook();
            GameApplicationRoot root = Boot(AppProbe.Definition().WithRestoreHook(hook).Build());
            Assert.That(root.TryCreateRestoreTargetBuilder(out IRestoreTargetBuilder? builder), Is.True);
            Assert.That(builder, Is.InstanceOf<ProductionRestoreTargetBuilder>());
            Assert.That(hook.LastComposer, Is.Not.Null);
            Assert.That(hook.LastComposer!.TemporalModel, Is.EqualTo(TemporalModel.CommandDriven));
        }

        // ------------------------------------------------------------------ batching measurement

        [Test]
        public void TwoHundredTargetsRestoreWithPublicationsIndependentOfTheTargetCount()
        {
            GameApplicationRoot root = Boot(AppProbe.Definition().WithTargetCapacity(256).Build());
            root.Start();
            PersistenceProbe.SeedRows(root, AppProbe.Target, 1U, 0);
            for (int i = 1; i < 200; i++)
            {
                TargetId target = PersistenceProbe.ExtraTarget(i);
                Assert.That(root.Seeder.TrySeed(target, AppProbe.RootScope, AppProbe.Recipe, out _, out DiagnosticCode code, out string detail), Is.True, code + ": " + detail);
                PersistenceProbe.SeedRows(root, target, 1U, i);
            }

            CommitOneStep(root);
            SaveService service = Service(root, PersistenceProbe.Options(directory, 1U));
            SaveResult saved = service.Capture("many");
            Assert.That(saved.Succeeded, Is.True, saved.ToString());

            Stopwatch clock = Stopwatch.StartNew();
            SaveResult restored = service.Restore("many");
            clock.Stop();
            Assert.That(restored.Succeeded, Is.True, restored + " | " + restored.Build?.Describe());
            ProductionRestoreReport build = restored.Build!;
            Assert.That(build.Targets, Is.EqualTo(200));
            Assert.That(build.Slots, Is.EqualTo(400));
            Assert.That(build.Publications, Is.EqualTo(1), "200 targets and 400 rows cost zero publications; the one install costs one");
            Assert.That(service.ActiveRoot.Targets.Count, Is.EqualTo(200));
            NUnit.Framework.TestContext.Out.WriteLine(
                "P1.2-RESTORE-200 capture_ms=" + saved.Milliseconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                + " restore_ms=" + clock.Elapsed.TotalMilliseconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                + " document_bytes=" + saved.Header!.DocumentBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " build=" + build.Describe());
        }

        // ------------------------------------------------------------------ helpers

        private GameApplicationRoot Boot(GameApplicationDefinition definition)
        {
            GameApplicationRoot root = GameApplication.Boot(definition, new GameApplicationBootOptions
            {
                InstallPlayerLoop = false,
                AssignDefaultWorld = false,
                PumpAssertions = false,
                FrameClock = () => frame,
            });
            roots.Add(root);
            return root;
        }

        private GameApplicationRoot BootRunning()
        {
            GameApplicationRoot root = Boot(AppProbe.Definition().Build());
            Assert.That(root.Start().Outcome, Is.EqualTo(Outcome.Published));
            return root;
        }

        private SaveService Service(GameApplicationRoot root, SaveServiceOptions options)
        {
            options.RestoreBootOptions = new GameApplicationBootOptions
            {
                InstallPlayerLoop = false,
                AssignDefaultWorld = false,
                PumpAssertions = false,
                FrameClock = () => frame,
            };
            var service = new SaveService(root, options);
            services.Add(service);
            return service;
        }

        private void CommitOneStep(GameApplicationRoot root)
        {
            LogicalStepId before = root.Host.CurrentStep;
            root.Host.NotifyCommandAdmitted(1U);
            frame++;
            GameCoreApplicationPump.PumpFrame();
            Assert.That(root.Host.CurrentStep.Value, Is.EqualTo(before.Value + 1UL), "one command commits one step");
        }

        private static LiveSlotState Find(IReadOnlyList<LiveSlotState> rows, SlotId slot)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Slot.Slot.Equals(slot))
                {
                    return rows[i];
                }
            }

            Assert.Fail("slot " + slot + " is not live");
            return default(LiveSlotState);
        }

        private static void StopQuietly(GameApplicationRoot root)
        {
            if (root.State != GameApplicationState.Stopped)
            {
                root.Stop("test teardown");
            }
        }

        private sealed class SilentDevice : IDeviceInputSource
        {
            public SilentDevice(Id128 source)
            {
                SourceId = source;
            }

            public Id128 SourceId { get; }

            public IReadOnlyList<DeviceInputSample> Sample() => Array.Empty<DeviceInputSample>();
        }
    }
}

// GameCore.Unity.App - the save service of an application root (SADR-012 (studio), catalog row 12).
//
// Normative sources: O-20 (capture at a committed boundary), O-21 (restore into a new, unexposed world that is
// validated before it runs), P-028 (catalog identity), P-032/P-054 (slot migrations), P-049 (a new WorldId), P-050
// (issuer sequences continue), P-053 (what a checkpoint holds) and P-055 (features gate behaviour).
//
// One service per running game. It owns the save directory (`Application.persistentDataPath/saves` by default) and
// the per-world capture modules (plugin clocks, random streams, next-step buffers, admitted command payloads and the
// delivery owner), and it does three things:
//
//   * Capture(slot) pauses a running world at its committed boundary when needed, captures it through
//     `CheckpointPublication.CaptureAndPublish`, declares the SADR-012 temporal-continuity feature on the document,
//     writes `<slot>.gcc` through the kernel's atomic file store and `<slot>.json` through an atomic replace, and
//     resumes the world;
//   * Restore(slot) verifies the files (envelope checksum, header/document hash, document decoding), applies the
//     catalog compatibility rule, migrates slot rows forward, rebuilds the world through the production restore
//     builder into a NEW session, stops the old root and makes the restored root the active one;
//   * Delete(slot) removes both files.
//
// Every refusal is a typed `SaveRefusal` (missing slot, corrupt file, catalog mismatch, migration path missing, unsafe
// state, ...). A refused restore never touches the running world.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using GameCore.Contracts;
using GameCore.Execution.Persistence;
using GameCore.Execution.Recovery;
using GameCore.Execution.Time;
using GameCore.Unity.Adapters;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Delivery;
using GameCore.Unity.Runtime.Persistence;
using GameCore.Unity.Runtime.Recovery;

namespace GameCore.Unity.App
{
    /// <summary>What a game declares to its save service (SADR-012).</summary>
    public sealed class SaveServiceOptions
    {
        public SaveServiceOptions(string gameId, CheckpointCodecSet codecs)
        {
            if (string.IsNullOrEmpty(gameId))
            {
                throw new ArgumentException("A save service names its game.", nameof(gameId));
            }

            GameId = gameId;
            Codecs = codecs ?? throw new ArgumentNullException(nameof(codecs));
        }

        public string GameId { get; }

        /// <summary>The generated checkpoint record codecs of the game build (P-054).</summary>
        public CheckpointCodecSet Codecs { get; }

        /// <summary>Save directory; null means <c>Application.persistentDataPath/saves</c>.</summary>
        public string? Directory { get; set; }

        /// <summary>The region the player is in, written to the slot header.</summary>
        public Func<string>? RegionId { get; set; }

        /// <summary>Accumulated play time in seconds, written to the slot header.</summary>
        public Func<double>? PlayTimeSeconds { get; set; }

        /// <summary>Wall clock of the header timestamp; null is <see cref="DateTime.UtcNow"/>.</summary>
        public Func<DateTime>? Clock { get; set; }

        /// <summary>The current schema of every slot a save carries (P-032).</summary>
        public SlotSchemaCatalog SlotSchemas { get; set; } = new SlotSchemaCatalog();

        /// <summary>The registered forward slot migrations (P-054).</summary>
        public SlotMigrationRegistry SlotMigrations { get; set; } = new SlotMigrationRegistry();

        /// <summary>Earlier catalog fingerprints this build declares compatible through its migrations.</summary>
        public IReadOnlyList<ContentHash> CompatibleCatalogs { get; set; } = Array.Empty<ContentHash>();

        /// <summary>Slot schema versions the header lists (normally one per save schema).</summary>
        public IReadOnlyList<SaveSchemaVersion> SchemaVersions { get; set; } = Array.Empty<SaveSchemaVersion>();

        /// <summary>The game's plugin clocks (P-038).</summary>
        public IReadOnlyList<PluginClockSpec> DeclaredClocks { get; set; } = Array.Empty<PluginClockSpec>();

        /// <summary>Bound of the plugin clock wake queue (P-038).</summary>
        public int MaxWakes { get; set; } = 64;

        /// <summary>Next-step buffers whose rows cross a step boundary (P-043).</summary>
        public IReadOnlyList<BufferId> NextStepBuffers { get; set; } = Array.Empty<BufferId>();

        /// <summary>Creates a world's delivery owner; null for a game with no external effect (P-045).</summary>
        public Func<GameApplicationRoot, ProductionWorldModules, WorldDeliveryOwner?>? DeliveryFactory { get; set; }

        /// <summary>Whether a capture of a running world pauses it at its boundary first (default true).</summary>
        public bool PauseForCapture { get; set; } = true;

        /// <summary>Queued external commands are captured and re-admitted (default) or rejected (P-053).</summary>
        public CheckpointQueuePolicy QueuePolicy { get; set; } = CheckpointQueuePolicy.IncludeQueued;

        /// <summary>Boot options of a restored root; null keeps the player loop and the default world untouched.</summary>
        public GameApplicationBootOptions? RestoreBootOptions { get; set; }
    }

    /// <summary>The outcome of one save operation (SADR-012).</summary>
    public sealed class SaveResult
    {
        internal SaveResult(string slot)
        {
            Slot = slot;
        }

        public string Slot { get; }

        public bool Succeeded => Refusal == null;

        public SaveRefusal? Refusal { get; internal set; }

        public SaveSlotHeader? Header { get; internal set; }

        /// <summary>The world the operation started from.</summary>
        public WorldId SourceWorld { get; internal set; }

        /// <summary>The restored world (a new WorldId, P-049); default for a capture or delete.</summary>
        public WorldId RestoredWorld { get; internal set; }

        public RestoredTemporalOrigin? Origin { get; internal set; }

        public SlotMigrationReport? Migration { get; internal set; }

        public ProductionRestoreReport? Build { get; internal set; }

        public RestoreOutcome? Restore { get; internal set; }

        /// <summary>Canonical hash of the slot rows the operation wrote or restored (lowercase hex).</summary>
        public string SlotHash { get; internal set; } = string.Empty;

        public double Milliseconds { get; internal set; }

        public override string ToString() =>
            (Succeeded ? "save ok " : "save refused ") + Slot
            + (Refusal == null ? string.Empty : " " + Refusal)
            + " (" + Milliseconds.ToString("0.###", CultureInfo.InvariantCulture) + " ms)";
    }

    /// <summary>Facts of one slot read without restoring it (the Studio's `save.inspect`).</summary>
    public sealed class SaveSlotInspection
    {
        internal SaveSlotInspection(string slot)
        {
            Slot = slot;
        }

        public string Slot { get; }

        public SaveSlotHeader? Header { get; internal set; }

        public SaveRefusal? Refusal { get; internal set; }

        public bool Readable => Refusal == null;

        public ulong LogicalStep { get; internal set; }

        public ulong TimeDebtTicks { get; internal set; }

        public double DomainSeconds { get; internal set; }

        public bool DeclaresTemporalContinuity { get; internal set; }

        public int Targets { get; internal set; }

        public int Slots { get; internal set; }

        public CatalogCompatibility Catalog { get; internal set; }

        /// <summary>The forward migration a restore would run, or the refusal it would meet.</summary>
        public SlotMigrationReport? Migration { get; internal set; }

        public override string ToString() =>
            "inspect(" + Slot + (Readable
                ? ",step=" + LogicalStep.ToString(CultureInfo.InvariantCulture) + ",targets="
                  + Targets.ToString(CultureInfo.InvariantCulture) + ",slots=" + Slots.ToString(CultureInfo.InvariantCulture)
                  + ",catalog=" + Catalog + ",continuity=" + DeclaresTemporalContinuity
                : ",refused " + Refusal) + ")";
    }

    /// <summary>The verdict of a capture-restore-capture round trip (the Studio's `save.testRoundTrip`).</summary>
    public sealed class SaveRoundTripReport
    {
        public bool Equal { get; internal set; }

        public string SourceSlotHash { get; internal set; } = string.Empty;

        public string RestoredSlotHash { get; internal set; } = string.Empty;

        public ulong SourceStep { get; internal set; }

        public ulong RestoredStep { get; internal set; }

        public WorldId SourceWorld { get; internal set; }

        public WorldId RestoredWorld { get; internal set; }

        public SaveRefusal? Refusal { get; internal set; }

        public string Detail { get; internal set; } = string.Empty;

        public override string ToString() =>
            "roundTrip(" + (Refusal != null ? "refused " + Refusal : (Equal ? "equal" : "DIFFERENT: " + Detail)) + ")";
    }

    /// <summary>The save service of one game (SADR-012).</summary>
    public sealed class SaveService
    {
        public const string DocumentExtension = ".gcc";
        public const string HeaderExtension = ".json";

        private readonly SaveServiceOptions options;
        private readonly RestoreReservationLedger reservations = new RestoreReservationLedger(64);

        public SaveService(GameApplicationRoot root, SaveServiceOptions options)
        {
            ActiveRoot = root ?? throw new ArgumentNullException(nameof(root));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            Modules = CreateModules();
            Modules.Delivery = options.DeliveryFactory?.Invoke(root, Modules);
            Directory = options.Directory ?? Path.Combine(UnityEngine.Application.persistentDataPath, "saves");
        }

        /// <summary>The root the service captures; a successful restore replaces it.</summary>
        public GameApplicationRoot ActiveRoot { get; private set; }

        /// <summary>The capture modules of the active root's world.</summary>
        public ProductionWorldModules Modules { get; private set; }

        public string Directory { get; }

        public SaveServiceOptions Options => options;

        /// <summary>Play time the last restored slot carried, so the game continues counting from it.</summary>
        public double LastRestoredPlayTimeSeconds { get; private set; }

        /// <summary>Raised after a successful restore with (old root, restored root).</summary>
        public event Action<GameApplicationRoot, GameApplicationRoot>? RootChanged;

        public event Action<SaveResult>? Written;

        public event Action<SaveResult>? Restored;

        public event Action<SaveResult>? Refused;

        public string DocumentPath(string slot) => Path.Combine(Directory, slot + DocumentExtension);

        public string HeaderPath(string slot) => Path.Combine(Directory, slot + HeaderExtension);

        // ------------------------------------------------------------------ capture

        /// <summary>Captures the active world into <paramref name="slot"/> (O-20, P-053).</summary>
        public SaveResult Capture(string slot, string? thumbnailPath = null)
        {
            Stopwatch clock = Stopwatch.StartNew();
            var result = new SaveResult(slot ?? string.Empty);
            GameApplicationRoot root = ActiveRoot;
            result.SourceWorld = root.World;
            if (!SaveSlotNames.IsValid(slot))
            {
                return Refuse(result, SaveRefusalCode.InvalidSlot, SaveSlotNames.Describe(slot), DiagnosticCode.UnsupportedVersion, string.Empty, clock);
            }

            if (!IsSafe(root, out string unsafeDetail))
            {
                return Refuse(result, SaveRefusalCode.UnsafeState, string.Empty, DiagnosticCode.TooLate, unsafeDetail, clock);
            }

            if (!TryCaptureDocument(root, true, out byte[] document, out CheckpointDocument? read, out SaveRefusal? refusal) || read == null)
            {
                result.Refusal = refusal;
                return Finish(result, clock);
            }

            var header = new SaveSlotHeader(
                slot!,
                options.GameId,
                root.CatalogHash.ToHex(),
                options.SchemaVersions,
                options.RegionId != null ? options.RegionId() : string.Empty,
                options.PlayTimeSeconds != null ? Math.Max(0.0, options.PlayTimeSeconds()) : 0.0,
                options.Clock != null ? options.Clock() : DateTime.UtcNow,
                thumbnailPath,
                read.Header.LogicalStep,
                ContentHash.Compute(document).ToHex(),
                document.LongLength,
                read.DeclaresTemporalContinuity);

            if (!TryWriteSlot(slot!, document, header, out string storageDetail))
            {
                return Refuse(result, SaveRefusalCode.StorageFailed, string.Empty, DiagnosticCode.ResourceUnavailable, storageDetail, clock);
            }

            result.Header = header;
            result.SlotHash = SlotHashOf(read);
            Finish(result, clock);
            Written?.Invoke(result);
            return result;
        }

        // ------------------------------------------------------------------ restore

        /// <summary>
        /// Restores <paramref name="slot"/> into a new world (O-21, P-049). On success the restored root is the active
        /// root and the previous root is stopped; on refusal the running world is untouched.
        /// </summary>
        public SaveResult Restore(string slot)
        {
            Stopwatch clock = Stopwatch.StartNew();
            var result = new SaveResult(slot ?? string.Empty);
            GameApplicationRoot root = ActiveRoot;
            result.SourceWorld = root.World;
            if (!SaveSlotNames.IsValid(slot))
            {
                return Refuse(result, SaveRefusalCode.InvalidSlot, SaveSlotNames.Describe(slot), DiagnosticCode.UnsupportedVersion, string.Empty, clock);
            }

            if (!TryReadSlot(slot!, out SaveSlotHeader? header, out byte[] document, out SaveRefusal? refusal) || header == null)
            {
                result.Refusal = refusal;
                return Finish(result, clock);
            }

            result.Header = header;
            if (!IsSafe(root, out string unsafeDetail))
            {
                return Refuse(result, SaveRefusalCode.UnsafeState, string.Empty, DiagnosticCode.TooLate, unsafeDetail, clock);
            }

            if (!TryRestoreBytes(root, document, result, out GameApplicationRoot? restored) || restored == null)
            {
                return Finish(result, clock);
            }

            // The restored root continues the old root's run state; the old root stops (O-19).
            bool wasRunning = root.State == GameApplicationState.Running;
            root.Stop("restored from save slot " + slot);
            if (!wasRunning)
            {
                restored.Pause();
            }

            ActiveRoot = restored;
            LastRestoredPlayTimeSeconds = header.PlayTimeSeconds;
            Finish(result, clock);
            RootChanged?.Invoke(root, restored);
            Restored?.Invoke(result);
            return result;
        }

        // ------------------------------------------------------------------ delete, list, inspect

        public SaveResult Delete(string slot)
        {
            Stopwatch clock = Stopwatch.StartNew();
            var result = new SaveResult(slot ?? string.Empty);
            result.SourceWorld = ActiveRoot.World;
            if (!SaveSlotNames.IsValid(slot))
            {
                return Refuse(result, SaveRefusalCode.InvalidSlot, SaveSlotNames.Describe(slot), DiagnosticCode.UnsupportedVersion, string.Empty, clock);
            }

            string documentPath = DocumentPath(slot!);
            string headerPath = HeaderPath(slot!);
            if (!File.Exists(documentPath) && !File.Exists(headerPath))
            {
                return Refuse(result, SaveRefusalCode.MissingSlot, string.Empty, DiagnosticCode.ResourceUnavailable,
                    "no save files exist for slot " + slot + " in " + Directory, clock);
            }

            try
            {
                if (File.Exists(headerPath))
                {
                    File.Delete(headerPath);
                }

                if (File.Exists(documentPath))
                {
                    File.Delete(documentPath);
                }
            }
            catch (Exception exception)
            {
                return Refuse(result, SaveRefusalCode.StorageFailed, string.Empty, DiagnosticCode.ResourceUnavailable,
                    exception.GetType().Name + ": " + exception.Message, clock);
            }

            return Finish(result, clock);
        }

        /// <summary>Headers of every readable slot, newest first; unreadable slots are listed in <paramref name="unreadable"/>.</summary>
        public IReadOnlyList<SaveSlotHeader> ListSlots(out IReadOnlyList<string> unreadable)
        {
            var headers = new List<SaveSlotHeader>();
            var broken = new List<string>();
            unreadable = broken;
            if (!System.IO.Directory.Exists(Directory))
            {
                return headers;
            }

            string[] files = System.IO.Directory.GetFiles(Directory, "*" + HeaderExtension);
            Array.Sort(files, StringComparer.Ordinal);
            for (int i = 0; i < files.Length; i++)
            {
                string slot = Path.GetFileNameWithoutExtension(files[i]);
                if (!SaveSlotNames.IsValid(slot))
                {
                    continue;
                }

                try
                {
                    if (SaveSlotHeader.TryParse(File.ReadAllText(files[i]), out SaveSlotHeader? header, out string _) && header != null)
                    {
                        headers.Add(header);
                        continue;
                    }
                }
                catch (IOException)
                {
                    // An unreadable header is listed as broken below.
                }

                broken.Add(slot);
            }

            headers.Sort((left, right) => right.SavedAtUtc.CompareTo(left.SavedAtUtc));
            return headers;
        }

        public bool Exists(string slot) =>
            SaveSlotNames.IsValid(slot) && File.Exists(DocumentPath(slot)) && File.Exists(HeaderPath(slot));

        /// <summary>Reads and verifies a slot and previews its migration without restoring it.</summary>
        public SaveSlotInspection Inspect(string slot)
        {
            var inspection = new SaveSlotInspection(slot ?? string.Empty);
            if (!SaveSlotNames.IsValid(slot))
            {
                inspection.Refusal = new SaveRefusal(SaveRefusalCode.InvalidSlot, SaveSlotNames.Describe(slot), DiagnosticCode.UnsupportedVersion, string.Empty);
                return inspection;
            }

            if (!TryReadSlot(slot!, out SaveSlotHeader? header, out byte[] document, out SaveRefusal? refusal) || header == null)
            {
                inspection.Refusal = refusal;
                return inspection;
            }

            inspection.Header = header;
            if (!CheckpointDocument.TryRead(document, options.Codecs, out CheckpointDocument? read, out DiagnosticCode code, out string detail) || read == null)
            {
                inspection.Refusal = new SaveRefusal(SaveRefusalCode.CorruptFile, string.Empty, code, detail);
                return inspection;
            }

            inspection.LogicalStep = read.Header.LogicalStep;
            inspection.TimeDebtTicks = read.Header.TimeDebtTicks;
            inspection.DomainSeconds = read.Header.DomainSeconds;
            inspection.DeclaresTemporalContinuity = read.DeclaresTemporalContinuity;
            inspection.Targets = (int)read.Header.TargetCount;
            inspection.Slots = (int)read.Header.SlotCount;
            inspection.Catalog = CheckpointCatalogCompatibility.Decide(
                read.Header.CatalogFingerprint, ActiveRoot.CatalogHash, options.CompatibleCatalogs, out string _);
            inspection.Migration = SlotMigrationExecutor.MigrateDocument(
                read, options.SlotSchemas, options.SlotMigrations, null, out byte[] _);
            return inspection;
        }

        /// <summary>
        /// Captures the active world, restores the capture into a new world, captures that world and compares the
        /// two canonical slot hashes and logical steps. Nothing is written to disk and the active root is kept: the
        /// restored world is stopped after the comparison.
        /// </summary>
        public SaveRoundTripReport TestRoundTrip()
        {
            var report = new SaveRoundTripReport();
            GameApplicationRoot root = ActiveRoot;
            report.SourceWorld = root.World;
            if (!IsSafe(root, out string unsafeDetail))
            {
                report.Refusal = new SaveRefusal(SaveRefusalCode.UnsafeState, string.Empty, DiagnosticCode.TooLate, unsafeDetail);
                return report;
            }

            if (!TryCaptureDocument(root, true, out byte[] document, out CheckpointDocument? source, out SaveRefusal? refusal) || source == null)
            {
                report.Refusal = refusal;
                return report;
            }

            report.SourceSlotHash = SlotHashOf(source);
            report.SourceStep = source.Header.LogicalStep;
            ProductionWorldModules keptModules = Modules;
            var scratch = new SaveResult("round-trip");
            if (!TryRestoreBytes(root, document, scratch, out GameApplicationRoot? restored) || restored == null)
            {
                Modules = keptModules;
                report.Refusal = scratch.Refusal;
                return report;
            }

            try
            {
                report.RestoredWorld = restored.World;
                if (!TryCaptureDocument(restored, false, out byte[] _, out CheckpointDocument? again, out refusal) || again == null)
                {
                    report.Refusal = refusal;
                    return report;
                }

                report.RestoredSlotHash = SlotHashOf(again);
                report.RestoredStep = again.Header.LogicalStep;
                report.Equal = report.SourceSlotHash == report.RestoredSlotHash && report.SourceStep == report.RestoredStep;
                report.Detail = "slots " + report.SourceSlotHash + " -> " + report.RestoredSlotHash + ", step "
                    + report.SourceStep.ToString(CultureInfo.InvariantCulture) + " -> "
                    + report.RestoredStep.ToString(CultureInfo.InvariantCulture);
                return report;
            }
            finally
            {
                restored.Stop("save round trip finished");
                Modules = keptModules;
            }
        }

        /// <summary>The canonical hash of a document's slot rows: sorted, field by field (lowercase hex).</summary>
        public static string SlotHashOf(CheckpointDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (!document.TryReadRecords<SlotRecordValue>(CheckpointRecordKind.Slot, out IReadOnlyList<SlotRecordValue> slots, out DiagnosticCode _, out string _))
            {
                return string.Empty;
            }

            return SlotHashOf(slots);
        }

        /// <summary>The canonical hash of slot rows: sorted lines of target|owner|slot|version|value|active.</summary>
        public static string SlotHashOf(IReadOnlyList<SlotRecordValue> slots)
        {
            var lines = new List<string>(slots.Count);
            for (int i = 0; i < slots.Count; i++)
            {
                SlotRecordValue row = slots[i];
                StateSlotKey key = row.Key;
                lines.Add(key.Target.ToString() + "|" + key.Owner.ToString() + "|" + key.Slot.ToString() + "|"
                    + row.SchemaVersion.ToString(CultureInfo.InvariantCulture) + "|"
                    + row.Value.ToString(CultureInfo.InvariantCulture) + "|" + (row.Active ? "1" : "0"));
            }

            lines.Sort(StringComparer.Ordinal);
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(string.Join("\n", lines.ToArray()));
            return ContentHash.Compute(bytes).ToHex();
        }

        // ------------------------------------------------------------------ internals

        private ProductionWorldModules CreateModules() =>
            new ProductionWorldModules(options.DeclaredClocks, options.MaxWakes, options.NextStepBuffers);

        /// <summary>The capture surface of <paramref name="root"/> with this service's modules (P-053).</summary>
        public CaptureContext CreateCaptureContext(GameApplicationRoot root)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            FixedStepSettings? fixedStep = root.Definition.FixedStep;
            ProductionWorldModules modules = Modules;
            return new CaptureContext(
                root.World,
                root.Definition.WorldDefinition,
                root.CatalogHash,
                root.Targets,
                root.Registry,
                modules.ClockSpecs,
                modules.Clocks,
                modules.Rng,
                root.Lane.Committed.Mode,
                fixedStep != null ? fixedStep.StepDurationTicks : 0UL,
                fixedStep != null ? fixedStep.TicksPerSecond : 0UL,
                fixedStep != null ? fixedStep.MaxStepsPerPump : 1U,
                fixedStep != null && fixedStep.UsesUnscaledHostClock,
                root.Lane,
                root.Publisher,
                modules.NextStepBuffers,
                modules.CommandPayloads,
                modules.OutboxRows);
        }

        private static bool IsSafe(GameApplicationRoot root, out string detail)
        {
            UnityWorldHost host = root.Host;
            if (root.State == GameApplicationState.Stopped)
            {
                detail = "the application root is stopped (O-19).";
                return false;
            }

            if (host.Lifecycle != WorldLifecycleState.Running && host.Lifecycle != WorldLifecycleState.Paused)
            {
                detail = "the world is " + host.Lifecycle + "; saves are taken and restored only from a running or paused world.";
                return false;
            }

            if (host.IsPumping)
            {
                detail = "the world is inside a step; a save happens at a committed boundary (P-030, P-053).";
                return false;
            }

            if (host.Driver.IsFaulted)
            {
                detail = "the world faulted (" + DiagnosticCodeText.Of(host.FaultCode) + "); a faulted world is recovered, not saved (P-031).";
                return false;
            }

            if (!AssemblyPublisher.MatchesPublishedAssembly(
                    root.Lane.Committed.Revision, root.Lane.Committed.Epoch, root.Publisher.PublishedRevision, host.CurrentEpoch))
            {
                detail = "a composition publication is in flight (P-006, P-030).";
                return false;
            }

            detail = string.Empty;
            return true;
        }

        /// <summary>
        /// Captures <paramref name="root"/> at its committed boundary through the kernel's capture-and-publish path
        /// into memory, then declares temporal continuity on the document (P-053, P-055, SADR-012).
        /// </summary>
        private bool TryCaptureDocument(
            GameApplicationRoot root,
            bool allowPause,
            out byte[] document,
            out CheckpointDocument? read,
            out SaveRefusal? refusal)
        {
            document = Array.Empty<byte>();
            read = null;
            refusal = null;
            bool resume = allowPause && options.PauseForCapture && root.State == GameApplicationState.Running;
            if (resume)
            {
                root.Pause();
            }

            try
            {
                var reader = new UnityCommittedBoundaryReader(root.Host, CreateCaptureContext(root));
                var capture = new CheckpointCaptureRequest(root.World, options.Codecs, options.QueuePolicy, root.CatalogHash);
                var memory = new MemoryCheckpointStore("memory://gamecore/save/" + root.World.Session.ToString());
                CheckpointPublicationResult published = CheckpointPublication.CaptureAndPublish(
                    new CheckpointPublicationRequest(reader, capture, memory, root.NextOperation()));
                if (!published.Succeeded)
                {
                    refusal = new SaveRefusal(SaveRefusalCode.CaptureFailed, string.Empty, published.Code, published.Detail);
                    return false;
                }

                if (!memory.TryRead(out byte[]? captured, out StoredCheckpoint _, out DiagnosticCode code, out string detail)
                    || captured == null
                    || !CheckpointDocument.TryRead(captured, options.Codecs, out CheckpointDocument? first, out code, out detail)
                    || first == null)
                {
                    refusal = new SaveRefusal(SaveRefusalCode.CaptureFailed, string.Empty, code, "the captured document did not read back: " + detail);
                    return false;
                }

                if (!first.TryRewrite(null, CheckpointFormat.TemporalContinuityFeatureIds, out byte[] continuous, out code, out detail)
                    || !CheckpointDocument.TryRead(continuous, options.Codecs, out read, out code, out detail)
                    || read == null)
                {
                    refusal = new SaveRefusal(SaveRefusalCode.CaptureFailed, string.Empty, code, "declaring temporal continuity failed: " + detail);
                    return false;
                }

                document = continuous;
                return true;
            }
            finally
            {
                if (resume && root.State == GameApplicationState.Paused)
                {
                    root.Resume();
                }
            }
        }

        private bool TryWriteSlot(string slot, byte[] document, SaveSlotHeader header, out string detail)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                var store = new FileCheckpointStore(DocumentPath(slot));
                if (!store.TryPublish(document, out StoredCheckpoint _, out DiagnosticCode code, out detail))
                {
                    detail = DiagnosticCodeText.Of(code) + ": " + detail;
                    return false;
                }

                string headerPath = HeaderPath(slot);
                string partial = headerPath + ".partial";
                File.WriteAllText(partial, header.ToJson(), new System.Text.UTF8Encoding(false));
                if (File.Exists(headerPath))
                {
                    File.Replace(partial, headerPath, null);
                }
                else
                {
                    File.Move(partial, headerPath);
                }

                detail = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                detail = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        private bool TryReadSlot(string slot, out SaveSlotHeader? header, out byte[] document, out SaveRefusal? refusal)
        {
            header = null;
            document = Array.Empty<byte>();
            refusal = null;
            string headerPath = HeaderPath(slot);
            string documentPath = DocumentPath(slot);
            if (!File.Exists(headerPath) || !File.Exists(documentPath))
            {
                refusal = new SaveRefusal(SaveRefusalCode.MissingSlot, string.Empty, DiagnosticCode.ResourceUnavailable,
                    "slot " + slot + " has " + (File.Exists(headerPath) ? "no checkpoint file" : "no header file") + " in " + Directory);
                return false;
            }

            string json;
            try
            {
                json = File.ReadAllText(headerPath);
            }
            catch (Exception exception)
            {
                refusal = new SaveRefusal(SaveRefusalCode.CorruptFile, string.Empty, DiagnosticCode.ResourceUnavailable,
                    exception.GetType().Name + ": " + exception.Message);
                return false;
            }

            if (!SaveSlotHeader.TryParse(json, out header, out string error) || header == null)
            {
                bool newer = error.IndexOf("newer build", StringComparison.Ordinal) >= 0;
                refusal = new SaveRefusal(newer ? SaveRefusalCode.NewerBuild : SaveRefusalCode.CorruptFile, string.Empty,
                    DiagnosticCode.UnsupportedVersion, error);
                return false;
            }

            if (!string.Equals(header.GameId, options.GameId, StringComparison.Ordinal))
            {
                refusal = new SaveRefusal(SaveRefusalCode.CatalogMismatch, "this save belongs to another game",
                    DiagnosticCode.UnsupportedVersion, "the slot was written by game '" + header.GameId + "' and this game is '" + options.GameId + "'");
                return false;
            }

            var store = new FileCheckpointStore(documentPath);
            if (!store.TryRead(out byte[]? bytes, out StoredCheckpoint _, out DiagnosticCode code, out string detail) || bytes == null)
            {
                refusal = new SaveRefusal(
                    code == DiagnosticCode.UnsupportedVersion ? SaveRefusalCode.NewerBuild : SaveRefusalCode.CorruptFile,
                    string.Empty, code, detail);
                return false;
            }

            string hash = ContentHash.Compute(bytes).ToHex();
            if (!string.Equals(hash, header.DocumentHash, StringComparison.Ordinal) || bytes.LongLength != header.DocumentBytes)
            {
                refusal = new SaveRefusal(SaveRefusalCode.CorruptFile, string.Empty, DiagnosticCode.ResourceUnavailable,
                    "the header names document " + header.DocumentHash + " (" + header.DocumentBytes.ToString(CultureInfo.InvariantCulture)
                    + " bytes) and the checkpoint file holds " + hash + " (" + bytes.LongLength.ToString(CultureInfo.InvariantCulture)
                    + " bytes); the slot's two files are from different saves");
                return false;
            }

            document = bytes;
            return true;
        }

        /// <summary>
        /// The restore core shared by <see cref="Restore"/> and <see cref="TestRoundTrip"/>: catalog rule, forward slot
        /// migration, production rebuild into a new session. On success the service's modules are the restored world's.
        /// </summary>
        private bool TryRestoreBytes(GameApplicationRoot root, byte[] document, SaveResult result, out GameApplicationRoot? restored)
        {
            restored = null;
            if (!CheckpointDocument.TryRead(document, options.Codecs, out CheckpointDocument? read, out DiagnosticCode code, out string detail) || read == null)
            {
                result.Refusal = new SaveRefusal(
                    code == DiagnosticCode.UnsupportedVersion ? SaveRefusalCode.NewerBuild : SaveRefusalCode.CorruptFile,
                    string.Empty, code, detail);
                return false;
            }

            CatalogCompatibility compatibility = CheckpointCatalogCompatibility.Decide(
                read.Header.CatalogFingerprint, root.CatalogHash, options.CompatibleCatalogs, out string catalogDetail);
            if (!CheckpointCatalogCompatibility.AllowsRestore(compatibility))
            {
                result.Refusal = new SaveRefusal(SaveRefusalCode.CatalogMismatch, string.Empty, DiagnosticCode.UnsupportedVersion, catalogDetail);
                return false;
            }

            SlotMigrationReport migration = SlotMigrationExecutor.MigrateDocument(
                read, options.SlotSchemas, options.SlotMigrations, null, out byte[] migrated);
            result.Migration = migration;
            if (!migration.Succeeded)
            {
                result.Refusal = new SaveRefusal(
                    migration.Refusal == SlotMigrationRefusal.Downgrade ? SaveRefusalCode.NewerBuild : SaveRefusalCode.MigrationPathMissing,
                    migration.Hint,
                    migration.Code,
                    migration.Detail);
                return false;
            }

            CheckpointDocument restoreDocument = read;
            if (!ReferenceEquals(migrated, read.RawBytes))
            {
                if (!CheckpointDocument.TryRead(migrated, options.Codecs, out CheckpointDocument? reread, out code, out detail)
                    || reread == null)
                {
                    result.Refusal = new SaveRefusal(SaveRefusalCode.CorruptFile, string.Empty, code, "the migrated document did not read back: " + detail);
                    return false;
                }

                restoreDocument = reread;
            }

            ProductionWorldModules restoredModules = CreateModules();
            var composer = new SaveRestoreComposer(
                root.Definition,
                options.RestoreBootOptions,
                () => restoredModules,
                options.DeliveryFactory);
            var builder = new ProductionRestoreTargetBuilder(composer, options.MaxWakes);
            builder.UseDocument(restoreDocument);
            WorldId session = GameCoreApplicationComposition.ReserveSessionId();
            var executor = new CheckpointRestoreExecutor(reservations, options.Codecs);
            RestoreOutcome outcome = executor.Restore(
                restoreDocument.RawBytes,
                session,
                root.NextOperation(),
                builder,
                new CheckpointMigrationRegistry(null),
                root.CatalogHash,
                null,
                compatibility == CatalogCompatibility.Identical);
            result.Restore = outcome;
            result.Build = builder.LastReport;
            result.Origin = builder.LastReport.Origin;
            if (!outcome.Restored)
            {
                builder.AbandonStaging();
                ProductionRestoreRefusal buildRefusal = builder.LastReport.Refusal;
                SaveRefusalCode refusalCode = buildRefusal == ProductionRestoreRefusal.RecipeMissing
                    || buildRefusal == ProductionRestoreRefusal.RecipeRevisionMismatch
                    || buildRefusal == ProductionRestoreRefusal.TemporalModelMismatch
                    ? SaveRefusalCode.CatalogMismatch
                    : SaveRefusalCode.RestoreRefused;
                result.Refusal = new SaveRefusal(refusalCode, string.Empty, outcome.Code, outcome.Stage + ": " + outcome.Detail);
                return false;
            }

            ProductionWorldParts? parts = builder.Parts;
            if (parts == null || !(parts.Owner is GameApplicationRoot restoredRoot))
            {
                result.Refusal = new SaveRefusal(SaveRefusalCode.RestoreRefused, string.Empty, DiagnosticCode.MissingDependency,
                    "the restore exposed a world but the composer returned no application root");
                return false;
            }

            Modules = restoredModules;
            result.RestoredWorld = restoredRoot.World;
            result.SlotHash = SlotHashOf(restoreDocument);
            restored = restoredRoot;
            return true;
        }

        private SaveResult Refuse(SaveResult result, SaveRefusalCode code, string hint, DiagnosticCode kernelCode, string detail, Stopwatch clock)
        {
            result.Refusal = new SaveRefusal(code, hint, kernelCode, detail);
            return Finish(result, clock);
        }

        private SaveResult Finish(SaveResult result, Stopwatch clock)
        {
            result.Milliseconds = clock.Elapsed.TotalMilliseconds;
            if (result.Refusal != null)
            {
                Refused?.Invoke(result);
            }

            return result;
        }
    }
}

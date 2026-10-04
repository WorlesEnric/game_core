// GameCore.Unity.Runtime (engine-free part, compiled as GameCore.Execution) - executable forward slot migrations
// (SADR-012 (studio)).
//
// Normative sources: P-032 (a version change uses a registered `Migrate`; a missing compatible policy is a validation
// error, never implicit zero initialization), P-054 ("generated serializers and registered directed version migrations
// replace reflection-based type construction. Migration paths must be unique for a requested source/target pair;
// ambiguity is rejected"), 05 s5 (`Migrate_*` is pure, versioned, has no I/O and no ECS writes) and 06 s7 ("Restore
// first validates catalog/schema versions and directed migration paths").
//
// V1 could *plan* a checkpoint migration (`CheckpointMigrationRegistry`) but nothing executed one, and every restore
// builder refused a plan that needed a step. This file is the executable half for the one kind of state SADR-004 lets
// gameplay own: int32 slot rows. A slot row records (target, owner, slot, schema version, value); the schema identity
// of an (owner, slot) pair is a manifest declaration (`StateSlotSpec.Schema`), so the catalog below maps the pair to
// its current `SchemaRef`, the registry holds pure forward steps per schema, and the executor rewrites each captured
// row whose version is older than the current one through the unique registered chain. Everything here is a pure
// function of its inputs: no world, no file, no clock.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Execution.Persistence
{
    /// <summary>
    /// A pure transform of one captured slot row into the value of the next schema version. It sees the whole row
    /// (target, owner, slot, version, value, disposition) so a migration may depend on which slot it is migrating, and
    /// it reports a refusal as a value rather than throwing (05 s5).
    /// </summary>
    public delegate bool SlotRecordTransform(SlotRecordValue source, out int migratedValue, out string detail);

    /// <summary>
    /// One registered forward migration of one slot schema from one version to a later one (P-054). The registration
    /// key is derived from the migration id, so a save schema can name the step it needs by id (`save needs migration
    /// X`) and the graph carries the key, never a delegate (05 s4).
    /// </summary>
    public sealed class SlotMigrationStep : ISchemaMigrationStep
    {
        private readonly Func<int, int>? valueTransform;
        private readonly SlotRecordTransform? recordTransform;

        public SlotMigrationStep(string migrationId, SchemaRef from, SchemaRef to, Func<int, int> transform)
            : this(migrationId, from, to, transform ?? throw new ArgumentNullException(nameof(transform)), null)
        {
        }

        public SlotMigrationStep(string migrationId, SchemaRef from, SchemaRef to, SlotRecordTransform transform)
            : this(migrationId, from, to, null, transform ?? throw new ArgumentNullException(nameof(transform)))
        {
        }

        private SlotMigrationStep(
            string migrationId,
            SchemaRef from,
            SchemaRef to,
            Func<int, int>? valueTransform,
            SlotRecordTransform? recordTransform)
        {
            if (!StableNameKeyDerivation.IsCanonicalStableName(migrationId))
            {
                throw new ArgumentException(
                    "A slot migration id is a canonical stable name (" + StableNameKeyDerivation.AllowedCharacters
                    + "); received '" + migrationId + "'.",
                    nameof(migrationId));
            }

            MigrationId = migrationId;
            Key = new FactoryKey(StableNameKeyDerivation.Derive(migrationId), 1U);
            From = from;
            To = to;
            this.valueTransform = valueTransform;
            this.recordTransform = recordTransform;
        }

        /// <summary>Stable id a save schema names this step by.</summary>
        public string MigrationId { get; }

        public FactoryKey Key { get; }

        public SchemaRef From { get; }

        public SchemaRef To { get; }

        /// <summary>Applies the pure transform to one row; a throwing transform is a refusal, never a crash (05 s5).</summary>
        public bool TryApply(SlotRecordValue row, out int migratedValue, out string detail)
        {
            try
            {
                if (valueTransform != null)
                {
                    migratedValue = valueTransform(row.Value);
                    detail = string.Empty;
                    return true;
                }

                string transformDetail = string.Empty;
                if (recordTransform != null && recordTransform(row, out migratedValue, out transformDetail))
                {
                    detail = string.Empty;
                    return true;
                }

                migratedValue = row.Value;
                detail = "migration " + MigrationId + " rejected slot " + row.Key.ToString()
                    + (string.IsNullOrEmpty(transformDetail) ? string.Empty : ": " + transformDetail);
                return false;
            }
            catch (Exception exception)
            {
                migratedValue = row.Value;
                detail = "migration " + MigrationId + " threw " + exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        public override string ToString() =>
            MigrationId + "(" + From.ToString() + "->" + To.ToString() + ")";
    }

    /// <summary>
    /// The registered forward slot migrations of one game build. Registration validates each step's shape through the
    /// kernel's directed graph (same schema id, strictly forward, unique key) so a malformed step is refused when it is
    /// registered rather than during a player's load (P-009, P-054).
    /// </summary>
    public sealed class SlotMigrationRegistry
    {
        private readonly List<SlotMigrationStep> steps = new List<SlotMigrationStep>();
        private readonly List<string> rejections = new List<string>();
        private CheckpointMigrationRegistry? graph;

        public SlotMigrationRegistry()
        {
        }

        public SlotMigrationRegistry(IEnumerable<SlotMigrationStep>? migrations)
        {
            if (migrations == null)
            {
                return;
            }

            foreach (SlotMigrationStep step in migrations)
            {
                Register(step);
            }
        }

        /// <summary>Accepted steps in registration order.</summary>
        public IReadOnlyList<SlotMigrationStep> Steps => steps;

        /// <summary>Why a candidate step was refused; empty when every registration was accepted.</summary>
        public IReadOnlyList<string> Rejections => rejections;

        public bool IsWellFormed => rejections.Count == 0;

        public int Count => steps.Count;

        /// <summary>Registers one step; false (with a recorded reason) for a malformed or duplicate step.</summary>
        public bool Register(SlotMigrationStep step)
        {
            if (step == null)
            {
                throw new ArgumentNullException(nameof(step));
            }

            for (int i = 0; i < steps.Count; i++)
            {
                if (string.Equals(steps[i].MigrationId, step.MigrationId, StringComparison.Ordinal))
                {
                    rejections.Add("migration id " + step.MigrationId + " is registered twice; one id names one step (P-009).");
                    return false;
                }
            }

            var probe = new CheckpointMigrationRegistry(new ISchemaMigrationStep[] { step });
            if (!probe.IsWellFormed)
            {
                rejections.Add(probe.Rejections[0]);
                return false;
            }

            steps.Add(step);
            graph = null;
            return true;
        }

        /// <summary>The registered steps as the kernel's directed migration graph (P-054).</summary>
        public CheckpointMigrationRegistry Graph
        {
            get
            {
                if (graph == null)
                {
                    var asSteps = new List<ISchemaMigrationStep>(steps.Count);
                    for (int i = 0; i < steps.Count; i++)
                    {
                        asSteps.Add(steps[i]);
                    }

                    graph = new CheckpointMigrationRegistry(asSteps);
                }

                return graph;
            }
        }

        /// <summary>The unique forward chain from <paramref name="from"/> to <paramref name="to"/>, or why there is none.</summary>
        public MigrationPlan Plan(SchemaRef from, SchemaRef to) => Graph.Plan(from, to);

        /// <summary>Finds a registered step by its migration id.</summary>
        public bool TryFind(string migrationId, out SlotMigrationStep? step)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (string.Equals(steps[i].MigrationId, migrationId, StringComparison.Ordinal))
                {
                    step = steps[i];
                    return true;
                }
            }

            step = null;
            return false;
        }
    }

    /// <summary>The current schema of one (owner, slot) pair, as its manifest declares it (P-032).</summary>
    public readonly struct SlotSchemaBinding
    {
        public SlotSchemaBinding(OwnerId owner, SlotId slot, SchemaRef current, string? name = null)
        {
            Owner = owner;
            Slot = slot;
            Current = current;
            Name = name ?? string.Empty;
        }

        public OwnerId Owner { get; }

        public SlotId Slot { get; }

        /// <summary>The schema id and the version this build writes for the slot.</summary>
        public SchemaRef Current { get; }

        /// <summary>Optional diagnostic name of the slot schema (a stable name such as `owner.slot-schema`); never an identity.</summary>
        public string Name { get; }

        public override string ToString() =>
            (Name.Length == 0 ? Current.Id.ToString() : Name) + " v" + Current.Version.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Which schema and version each (owner, slot) pair carries in this build. Built from the plugin manifests'
    /// state-slot declarations and, for slots a manifest does not declare, from explicit bindings a save schema adds.
    /// One pair has one current schema; a second, different binding is recorded as a conflict (P-034).
    /// </summary>
    public sealed class SlotSchemaCatalog
    {
        private readonly Dictionary<(Id128 owner, Id128 slot), SlotSchemaBinding> byPair =
            new Dictionary<(Id128 owner, Id128 slot), SlotSchemaBinding>();
        private readonly List<SlotSchemaBinding> ordered = new List<SlotSchemaBinding>();
        private readonly List<string> conflicts = new List<string>();

        public SlotSchemaCatalog()
        {
        }

        public SlotSchemaCatalog(IEnumerable<SlotSchemaBinding>? bindings)
        {
            if (bindings == null)
            {
                return;
            }

            foreach (SlotSchemaBinding binding in bindings)
            {
                Add(binding);
            }
        }

        /// <summary>A catalog of every state slot the manifests declare (P-032).</summary>
        public static SlotSchemaCatalog FromManifests(IEnumerable<PluginManifest>? manifests)
        {
            var catalog = new SlotSchemaCatalog();
            if (manifests == null)
            {
                return catalog;
            }

            foreach (PluginManifest manifest in manifests)
            {
                if (manifest == null)
                {
                    continue;
                }

                for (int i = 0; i < manifest.StateSlots.Count; i++)
                {
                    StateSlotSpec spec = manifest.StateSlots[i];
                    catalog.Add(new SlotSchemaBinding(spec.Owner, spec.SlotId, spec.Schema));
                }
            }

            return catalog;
        }

        public IReadOnlyList<SlotSchemaBinding> Bindings => ordered;

        /// <summary>Pairs bound to two different schemas; a non-empty list is a declaration defect (P-034).</summary>
        public IReadOnlyList<string> Conflicts => conflicts;

        public int Count => ordered.Count;

        /// <summary>Adds one binding; an identical repeat coalesces, a different one is a recorded conflict.</summary>
        public bool Add(SlotSchemaBinding binding)
        {
            var key = (binding.Owner.Value, binding.Slot.Value);
            if (byPair.TryGetValue(key, out SlotSchemaBinding existing))
            {
                if (existing.Current.Equals(binding.Current))
                {
                    return true;
                }

                conflicts.Add(
                    "slot " + binding.Slot.ToString() + " of owner " + binding.Owner.ToString() + " is bound to "
                    + existing.Current.ToString() + " and to " + binding.Current.ToString() + " (P-034).");
                return false;
            }

            byPair.Add(key, binding);
            ordered.Add(binding);
            return true;
        }

        public bool TryGet(OwnerId owner, SlotId slot, out SlotSchemaBinding binding) =>
            byPair.TryGetValue((owner.Value, slot.Value), out binding);
    }

    /// <summary>Why a slot migration did not produce migrated rows (P-052).</summary>
    public enum SlotMigrationRefusal
    {
        None = 0,

        /// <summary>No registered chain leads from the captured version to the current one (P-032, P-054).</summary>
        MigrationPathMissing = 1,

        /// <summary>Two registered chains lead to the current version; the build is ambiguous (P-054).</summary>
        AmbiguousPath = 2,

        /// <summary>The captured version is newer than this build's; migrations run forward only (P-054).</summary>
        Downgrade = 3,

        /// <summary>A registered pure transform rejected its input row (05 s5).</summary>
        TransformRejected = 4,

        /// <summary>The registry or the schema catalog is malformed (P-009, P-034).</summary>
        InvalidDeclarations = 5,
    }

    /// <summary>
    /// The outcome of migrating one set of captured slot rows: the rows to restore, or the one actionable reason they
    /// cannot be restored by this build (P-052). The rows are only present on success, so a refused migration can never
    /// be half applied (P-029).
    /// </summary>
    public sealed class SlotMigrationReport
    {
        private SlotMigrationReport(
            SlotMigrationRefusal refusal,
            DiagnosticCode code,
            string detail,
            string hint,
            IReadOnlyList<SlotRecordValue> slots,
            int migratedRows,
            int unboundRows,
            IReadOnlyList<string> appliedMigrations,
            SchemaRef refusedFrom,
            SchemaRef refusedTo)
        {
            Refusal = refusal;
            Code = code;
            Detail = detail;
            Hint = hint;
            Slots = slots;
            MigratedRows = migratedRows;
            UnboundRows = unboundRows;
            AppliedMigrations = appliedMigrations;
            RefusedFrom = refusedFrom;
            RefusedTo = refusedTo;
        }

        public bool Succeeded => Refusal == SlotMigrationRefusal.None;

        public SlotMigrationRefusal Refusal { get; }

        /// <summary>The protocol code of a refusal: `MigrationRequired`, `UnsupportedVersion` or `OwnershipConflict`.</summary>
        public DiagnosticCode Code { get; }

        public string Detail { get; }

        /// <summary>What a player or author can do about a refusal, in one sentence.</summary>
        public string Hint { get; }

        /// <summary>The rows to restore (migrated where needed, untouched otherwise); empty on refusal.</summary>
        public IReadOnlyList<SlotRecordValue> Slots { get; }

        /// <summary>Rows whose version changed.</summary>
        public int MigratedRows { get; }

        /// <summary>Rows whose (owner, slot) pair has no schema binding and was carried unchanged.</summary>
        public int UnboundRows { get; }

        /// <summary>Migration ids that ran at least once, in first-use order.</summary>
        public IReadOnlyList<string> AppliedMigrations { get; }

        /// <summary>The captured schema version a refusal could not migrate; default on success.</summary>
        public SchemaRef RefusedFrom { get; }

        /// <summary>The current schema version a refusal could not reach; default on success.</summary>
        public SchemaRef RefusedTo { get; }

        internal static SlotMigrationReport Success(
            IReadOnlyList<SlotRecordValue> slots,
            int migrated,
            int unbound,
            IReadOnlyList<string> applied) =>
            new SlotMigrationReport(
                SlotMigrationRefusal.None,
                DiagnosticCode.None,
                migrated == 0
                    ? "every bound slot row already carries its current schema version."
                    : migrated.ToString(CultureInfo.InvariantCulture) + " slot row(s) migrated through "
                        + string.Join(", ", ToArray(applied)) + ".",
                string.Empty,
                slots,
                migrated,
                unbound,
                applied,
                default(SchemaRef),
                default(SchemaRef));

        internal static SlotMigrationReport Refused(
            SlotMigrationRefusal refusal,
            DiagnosticCode code,
            string detail,
            string hint,
            SchemaRef from,
            SchemaRef to) =>
            new SlotMigrationReport(
                refusal,
                code,
                detail,
                hint,
                Array.Empty<SlotRecordValue>(),
                0,
                0,
                Array.Empty<string>(),
                from,
                to);

        private static string[] ToArray(IReadOnlyList<string> list)
        {
            var array = new string[list.Count];
            for (int i = 0; i < array.Length; i++)
            {
                array[i] = list[i];
            }

            return array;
        }

        public override string ToString() =>
            Succeeded
                ? "slotMigration(migrated=" + MigratedRows.ToString(CultureInfo.InvariantCulture) + ",unbound="
                    + UnboundRows.ToString(CultureInfo.InvariantCulture) + ")"
                : "slotMigration(refused " + Refusal.ToString() + ":" + DiagnosticCodeText.Of(Code) + ")";
    }

    /// <summary>
    /// Executes forward slot migrations on captured rows (SADR-012). Every row whose (owner, slot) pair is bound to a
    /// current schema is planned against it: the same version is carried, an older version runs the unique registered
    /// chain step by step, and anything else refuses the whole set with one actionable reason (P-032, P-054).
    /// </summary>
    public static class SlotMigrationExecutor
    {
        /// <summary>Migrates <paramref name="slots"/>; on refusal no row is returned.</summary>
        public static SlotMigrationReport Migrate(
            IReadOnlyList<SlotRecordValue> slots,
            SlotSchemaCatalog catalog,
            SlotMigrationRegistry registry)
        {
            if (slots == null)
            {
                throw new ArgumentNullException(nameof(slots));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (!registry.IsWellFormed || catalog.Conflicts.Count != 0)
            {
                string reason = !registry.IsWellFormed ? registry.Rejections[0] : catalog.Conflicts[0];
                return SlotMigrationReport.Refused(
                    SlotMigrationRefusal.InvalidDeclarations,
                    DiagnosticCode.OwnershipConflict,
                    "the slot migration declarations are malformed: " + reason,
                    "fix the save schema's slot migrations before loading this save",
                    default(SchemaRef),
                    default(SchemaRef));
            }

            var plans = new Dictionary<(Id128 schema, uint from, uint to), MigrationPlan>();
            var migrated = new List<SlotRecordValue>(slots.Count);
            var applied = new List<string>();
            int migratedRows = 0;
            int unbound = 0;

            for (int i = 0; i < slots.Count; i++)
            {
                SlotRecordValue row = slots[i];
                StateSlotKey key = row.Key;
                if (!catalog.TryGet(key.Owner, key.Slot, out SlotSchemaBinding binding))
                {
                    unbound++;
                    migrated.Add(row);
                    continue;
                }

                SchemaRef current = binding.Current;
                if (row.SchemaVersion == current.Version)
                {
                    migrated.Add(row);
                    continue;
                }

                var captured = new SchemaRef(current.Id, row.SchemaVersion);
                if (row.SchemaVersion > current.Version)
                {
                    return SlotMigrationReport.Refused(
                        SlotMigrationRefusal.Downgrade,
                        DiagnosticCode.UnsupportedVersion,
                        "slot " + key.ToString() + " was saved at " + Describe(binding, row.SchemaVersion)
                        + " and this build writes " + Describe(binding, current.Version)
                        + "; slot migrations run forward only (P-054).",
                        "this save was written by a newer build of the game; load it with that build",
                        captured,
                        current);
                }

                var planKey = (current.Id.Value, row.SchemaVersion, current.Version);
                if (!plans.TryGetValue(planKey, out MigrationPlan? plan))
                {
                    plan = registry.Plan(captured, current);
                    plans.Add(planKey, plan);
                }

                if (plan.Outcome == MigrationPlanOutcome.Ambiguous)
                {
                    return SlotMigrationReport.Refused(
                        SlotMigrationRefusal.AmbiguousPath,
                        DiagnosticCode.OwnershipConflict,
                        "slot " + key.ToString() + ": " + plan.Detail,
                        "the build registers " + plan.PathCount.ToString(CultureInfo.InvariantCulture)
                        + " migration chains for " + Describe(binding, row.SchemaVersion) + " -> v"
                        + current.Version.ToString(CultureInfo.InvariantCulture) + "; keep exactly one",
                        captured,
                        current);
                }

                if (!plan.IsRunnable)
                {
                    return SlotMigrationReport.Refused(
                        SlotMigrationRefusal.MigrationPathMissing,
                        DiagnosticCode.MigrationRequired,
                        "slot " + key.ToString() + " was saved at " + Describe(binding, row.SchemaVersion)
                        + " and no registered chain of slot migrations reaches v"
                        + current.Version.ToString(CultureInfo.InvariantCulture) + ": " + plan.Detail,
                        "save needs a migration for " + Describe(binding, row.SchemaVersion) + " -> v"
                        + current.Version.ToString(CultureInfo.InvariantCulture)
                        + "; register it in the save schema or load the save with the build that wrote it",
                        captured,
                        current);
                }

                int value = row.Value;
                uint version = row.SchemaVersion;
                for (int s = 0; s < plan.Steps.Count; s++)
                {
                    if (!(plan.Steps[s] is SlotMigrationStep step))
                    {
                        return SlotMigrationReport.Refused(
                            SlotMigrationRefusal.InvalidDeclarations,
                            DiagnosticCode.OwnershipConflict,
                            "the migration chain for slot " + key.ToString() + " contains a step that is not an executable"
                            + " slot migration (" + plan.Steps[s].Key.ToString() + ").",
                            "register slot migrations through SlotMigrationRegistry",
                            captured,
                            current);
                    }

                    var input = new SlotRecordValue(
                        key.Target.Value.High,
                        key.Target.Value.Low,
                        key.Owner.Value.High,
                        key.Owner.Value.Low,
                        key.Slot.Value.High,
                        key.Slot.Value.Low,
                        version,
                        value,
                        row.Active);
                    if (!step.TryApply(input, out int next, out string stepDetail))
                    {
                        return SlotMigrationReport.Refused(
                            SlotMigrationRefusal.TransformRejected,
                            DiagnosticCode.MigrationRequired,
                            "slot " + key.ToString() + ": " + stepDetail + " (05 s5).",
                            "migration " + step.MigrationId + " cannot convert this save's value; the save cannot be loaded"
                            + " by this build",
                            captured,
                            current);
                    }

                    value = next;
                    version = step.To.Version;
                    if (!applied.Contains(step.MigrationId))
                    {
                        applied.Add(step.MigrationId);
                    }
                }

                // The disposition is state, not schema: a dormant row stays dormant through a migration (P-032).
                migrated.Add(new SlotRecordValue(
                    key.Target.Value.High,
                    key.Target.Value.Low,
                    key.Owner.Value.High,
                    key.Owner.Value.Low,
                    key.Slot.Value.High,
                    key.Slot.Value.Low,
                    version,
                    value,
                    row.Active));
                migratedRows++;
            }

            return SlotMigrationReport.Success(migrated, migratedRows, unbound, applied);
        }

        /// <summary>
        /// Migrates the slot rows of a verified document and, when any row changed or <paramref name="features"/>
        /// differs from the declared set, writes the document again (every other record byte for byte). On success
        /// <paramref name="migratedDocument"/> holds the bytes to restore; on refusal it is empty (P-029, P-054).
        /// </summary>
        public static SlotMigrationReport MigrateDocument(
            CheckpointDocument document,
            SlotSchemaCatalog catalog,
            SlotMigrationRegistry registry,
            IReadOnlyList<Id128>? features,
            out byte[] migratedDocument)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            migratedDocument = Array.Empty<byte>();
            if (!document.TryReadRecords<SlotRecordValue>(CheckpointRecordKind.Slot, out IReadOnlyList<SlotRecordValue> slots, out DiagnosticCode code, out string detail))
            {
                return SlotMigrationReport.Refused(
                    SlotMigrationRefusal.InvalidDeclarations,
                    code,
                    "the document's slot records did not decode: " + detail,
                    "the save file is damaged",
                    default(SchemaRef),
                    default(SchemaRef));
            }

            SlotMigrationReport report = Migrate(slots, catalog, registry);
            if (!report.Succeeded)
            {
                return report;
            }

            bool sameFeatures = features == null || SameSet(features, document.DeclaredFeatureIds);
            if (report.MigratedRows == 0 && sameFeatures)
            {
                migratedDocument = document.RawBytes;
                return report;
            }

            if (!document.TryRewrite(
                    report.MigratedRows == 0 ? null : report.Slots,
                    features,
                    out byte[] rewritten,
                    out code,
                    out detail))
            {
                return SlotMigrationReport.Refused(
                    SlotMigrationRefusal.InvalidDeclarations,
                    code,
                    "writing the migrated document failed: " + detail,
                    "the save could not be migrated in memory; nothing was changed on disk",
                    default(SchemaRef),
                    default(SchemaRef));
            }

            migratedDocument = rewritten;
            return report;
        }

        private static bool SameSet(IReadOnlyList<Id128> left, IReadOnlyList<Id128> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                bool found = false;
                for (int j = 0; j < right.Count; j++)
                {
                    if (left[i].Equals(right[j]))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }

        private static string Describe(SlotSchemaBinding binding, uint version) =>
            (binding.Name.Length == 0 ? "schema " + binding.Current.Id.ToString() : binding.Name)
            + " v" + version.ToString(CultureInfo.InvariantCulture);
    }
}

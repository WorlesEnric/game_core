// GameCore.Contracts - directed checkpoint schema migration planning (GC-018). Normative sources:
// docs/game-core/00-core-protocols.md P-054 ("generated serializers and registered directed version migrations
// replace reflection-based type construction. Migration paths must be unique for a requested source/target pair;
// ambiguity is rejected"; "unknown required fields/schema versions reject") and 05 s6 ("migration registration is a
// directed graph per schema. Unique explicit path v1->v2->v3 is allowed; two possible paths to the same
// destination reject unless the catalog designates exactly one active path").
//
// The graph below is engine-free and side-effect free. Planning a path does not run a migration: it answers, before
// any restore writes anything, whether this build can legally get from a document's schema version to the version
// its catalog declares, and by which unique chain of registered steps. A set of steps that admits two chains to the
// same destination is refused, which is what "ambiguity is rejected" means in practice: the caller is never asked
// to pick a path, because a silent pick would make a restored world depend on graph traversal order.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameCore.Contracts
{
    /// <summary>
    /// One registered directed schema migration step: it transforms a document of
    /// <see cref="From"/> into the schema named by <see cref="To"/>. A step is pure and versioned (P-054).
    /// </summary>
    public interface ISchemaMigrationStep
    {
        /// <summary>Generated registration key of this step; the plan carries the key, never a delegate (05 s4).</summary>
        FactoryKey Key { get; }

        /// <summary>Schema and version this step reads.</summary>
        SchemaRef From { get; }

        /// <summary>Schema and version this step writes; the schema id must equal <see cref="From"/>'s.</summary>
        SchemaRef To { get; }
    }

    /// <summary>Outcome of planning one migration: the chain, or the reason no unique chain exists (P-054).</summary>
    public enum MigrationPlanOutcome
    {
        /// <summary>The document's version is the destination already; no step runs.</summary>
        Current = 0,

        /// <summary>Exactly one chain of registered steps reaches the destination.</summary>
        Unique = 1,

        /// <summary>Two or more distinct chains reach the destination; the catalog is ambiguous (P-054).</summary>
        Ambiguous = 2,

        /// <summary>No chain of registered steps reaches the destination (P-054).</summary>
        Unreachable = 3,

        /// <summary>The source or destination schema is unknown to this registry (P-055).</summary>
        UnknownSchema = 4,
    }

    /// <summary>
    /// The plan for one schema: the unique ordered chain of steps from a source version to the destination, or the
    /// reason there is none. Steps are recorded in execution order.
    /// </summary>
    public sealed class MigrationPlan
    {
        private MigrationPlan(
            MigrationPlanOutcome outcome,
            SchemaId schema,
            SchemaRef from,
            SchemaRef to,
            IReadOnlyList<ISchemaMigrationStep>? steps,
            int pathCount,
            string detail)
        {
            Outcome = outcome;
            SchemaId = schema;
            From = from;
            To = to;
            Steps = steps ?? Array.Empty<ISchemaMigrationStep>();
            PathCount = pathCount;
            Detail = detail;
        }

        /// <summary>Schema this plan applies to (id and version pair of the destination).</summary>
        public SchemaId SchemaId { get; }

        public SchemaRef From { get; }

        public SchemaRef To { get; }

        /// <summary>Steps in execution order; empty unless <see cref="Outcome"/> is <see cref="MigrationPlanOutcome.Unique"/>.</summary>
        public IReadOnlyList<ISchemaMigrationStep> Steps { get; }

        /// <summary>
        /// How many distinct chains reach the destination. It is meaningful for
        /// <see cref="MigrationPlanOutcome.Ambiguous"/> and is always 0 or 1 otherwise.
        /// </summary>
        public int PathCount { get; }

        public MigrationPlanOutcome Outcome { get; }

        public string Detail { get; }

        /// <summary>True when the plan needs at least one step and has one.</summary>
        public bool RequiresMigration => Outcome == MigrationPlanOutcome.Unique && Steps.Count != 0;

        /// <summary>Only a <see cref="MigrationPlanOutcome.Current"/> or a unique plan may be executed (P-054).</summary>
        public bool IsRunnable =>
            Outcome == MigrationPlanOutcome.Current || Outcome == MigrationPlanOutcome.Unique;

        /// <summary>The protocol code a refusal carries (P-052).</summary>
        public DiagnosticCode Code
        {
            get
            {
                switch (Outcome)
                {
                    case MigrationPlanOutcome.Current:
                    case MigrationPlanOutcome.Unique:
                        return DiagnosticCode.None;
                    case MigrationPlanOutcome.Unreachable:
                        return DiagnosticCode.MigrationRequired;
                    case MigrationPlanOutcome.UnknownSchema:
                        return DiagnosticCode.UnsupportedVersion;
                    default:
                        return DiagnosticCode.OwnershipConflict;
                }
            }
        }

        public static MigrationPlan Current(SchemaRef schema) =>
            new MigrationPlan(
                MigrationPlanOutcome.Current,
                schema.Id,
                schema,
                schema,
                null,
                0,
                "the document already declares the destination version.");

        public static MigrationPlan Unique(
            SchemaRef schema,
            SchemaRef from,
            SchemaRef to,
            IReadOnlyList<ISchemaMigrationStep> steps) =>
            new MigrationPlan(
                MigrationPlanOutcome.Unique,
                schema.Id,
                from,
                to,
                steps,
                1,
                "exactly one registered chain reaches the destination.");

        public static MigrationPlan Refused(
            MigrationPlanOutcome outcome,
            SchemaRef schema,
            SchemaRef from,
            SchemaRef to,
            int pathCount,
            string detail) =>
            new MigrationPlan(outcome, schema.Id, from, to, null, pathCount, detail);

        public override string ToString() =>
            Outcome.ToString() + "(" + From.ToString() + "->" + To.ToString()
            + ",steps=" + Steps.Count.ToString(CultureInfo.InvariantCulture)
            + ",paths=" + PathCount.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// The registered directed migration graph of one catalog revision. Registration validates every step's own
    /// shape (same schema id, forward-only version move, no duplicate key) so an ill-formed step is refused at
    /// construction rather than during a restore (P-009, P-054).
    /// </summary>
    public sealed class CheckpointMigrationRegistry
    {
        /// <summary>Bound on the version space searched per schema; a longer chain than this is a catalog error.</summary>
        public const int MaxChainLength = 64;

        private readonly List<ISchemaMigrationStep> steps = new List<ISchemaMigrationStep>();
        private readonly Dictionary<Id128, List<ISchemaMigrationStep>> bySchema =
            new Dictionary<Id128, List<ISchemaMigrationStep>>();
        private readonly List<FactoryKey> duplicateKeys = new List<FactoryKey>();
        private readonly List<string> rejections = new List<string>();

        public CheckpointMigrationRegistry(IReadOnlyList<ISchemaMigrationStep>? migrations)
        {
            if (migrations == null)
            {
                return;
            }

            for (int i = 0; i < migrations.Count; i++)
            {
                ISchemaMigrationStep step = migrations[i];
                if (step == null)
                {
                    continue;
                }

                if (!TryValidate(step, out string rejection))
                {
                    rejections.Add(rejection);
                    continue;
                }

                if (HasKey(step.Key))
                {
                    duplicateKeys.Add(step.Key);
                    rejections.Add(
                        "two migration steps share registration key " + step.Key.ToString()
                        + "; one key resolves to one handler (P-009).");
                    continue;
                }

                steps.Add(step);
                if (!bySchema.TryGetValue(step.From.Id.Value, out List<ISchemaMigrationStep>? forSchema))
                {
                    forSchema = new List<ISchemaMigrationStep>();
                    bySchema.Add(step.From.Id.Value, forSchema);
                }

                forSchema.Add(step);
            }
        }

        /// <summary>Accepted steps, in registration order.</summary>
        public IReadOnlyList<ISchemaMigrationStep> Steps => steps;

        /// <summary>Registration keys that appeared more than once; a non-empty list is a catalog defect (P-009).</summary>
        public IReadOnlyList<FactoryKey> DuplicateKeys => duplicateKeys;

        /// <summary>Human-readable reasons a candidate step was refused; empty when every step was accepted.</summary>
        public IReadOnlyList<string> Rejections => rejections;

        /// <summary>True when every candidate step was accepted and no key collided.</summary>
        public bool IsWellFormed => rejections.Count == 0;

        /// <summary>
        /// Plans the migration from one version of a schema to another. The answer is unique-or-rejected: a
        /// requested source/target pair with two distinct chains is <see cref="MigrationPlanOutcome.Ambiguous"/>,
        /// never a silent pick (P-054).
        /// </summary>
        public MigrationPlan Plan(SchemaRef from, SchemaRef to)
        {
            if (!from.Id.Equals(to.Id))
            {
                return MigrationPlan.Refused(
                    MigrationPlanOutcome.UnknownSchema,
                    from,
                    from,
                    to,
                    0,
                    "the source names schema " + from.Id.ToString() + " and the destination "
                    + to.Id.ToString() + "; a migration never crosses schema identities (P-054).");
            }

            if (from.Version == to.Version)
            {
                return MigrationPlan.Current(from);
            }

            if (from.Version > to.Version)
            {
                // The graph is directed forward only; a downgrade is not a registered step (P-054).
                return MigrationPlan.Refused(
                    MigrationPlanOutcome.Unreachable,
                    from,
                    from,
                    to,
                    0,
                    "the document declares version " + from.Version.ToString(CultureInfo.InvariantCulture)
                    + " and the catalog declares " + to.Version.ToString(CultureInfo.InvariantCulture)
                    + "; only forward migration steps are registered (P-054).");
            }

            if (!bySchema.TryGetValue(from.Id.Value, out List<ISchemaMigrationStep>? candidates))
            {
                return MigrationPlan.Refused(
                    MigrationPlanOutcome.Unreachable,
                    from,
                    from,
                    to,
                    0,
                    "no migration step is registered for schema " + from.Id.ToString() + " (P-054).");
            }

            // Count the distinct chains into every reached version, in ascending version order. Exact counts are
            // needed, not reachability: two chains into the destination is precisely the ambiguity P-054 rejects,
            // and a "visited" set alone would hide it. One ascending pass is exact because every registered step
            // strictly increases the version, so every predecessor of a version is processed before it; repeating
            // the pass would double-count.
            long span = (long)to.Version - from.Version;
            if (span > MaxChainLength)
            {
                return MigrationPlan.Refused(
                    MigrationPlanOutcome.Unreachable,
                    from,
                    from,
                    to,
                    0,
                    "the version gap from " + from.Version.ToString(CultureInfo.InvariantCulture) + " to "
                    + to.Version.ToString(CultureInfo.InvariantCulture) + " exceeds the "
                    + MaxChainLength.ToString(CultureInfo.InvariantCulture)
                    + "-step chain bound this registry searches (P-022, P-054).");
            }

            int steps3 = (int)span;
            var chains = new int[steps3 + 1];
            var firstStep = new ISchemaMigrationStep?[steps3 + 1];
            var predecessor = new int[steps3 + 1];
            for (int i = 0; i < chains.Length; i++)
            {
                chains[i] = 0;
                firstStep[i] = null;
                predecessor[i] = -1;
            }

            chains[0] = 1;

            for (int i = 0; i <= steps3; i++)
            {
                if (chains[i] == 0)
                {
                    continue;
                }

                uint version = from.Version + (uint)i;
                for (int c = 0; c < candidates.Count; c++)
                {
                    ISchemaMigrationStep step = candidates[c];
                    if (step.From.Version != version || step.To.Version > to.Version)
                    {
                        continue;
                    }

                    int target = (int)(step.To.Version - from.Version);
                    if (target <= i)
                    {
                        continue;
                    }

                    if (chains[target] == 0)
                    {
                        predecessor[target] = i;
                        firstStep[target] = step;
                    }

                    chains[target] = SaturatedAdd(chains[target], chains[i]);
                }
            }

            int destination = steps3;

            if (chains[destination] == 0)
            {
                return MigrationPlan.Refused(
                    MigrationPlanOutcome.Unreachable,
                    from,
                    from,
                    to,
                    0,
                    "no registered chain of migration steps reaches version "
                    + to.Version.ToString(CultureInfo.InvariantCulture) + " from "
                    + from.Version.ToString(CultureInfo.InvariantCulture) + " (P-054).");
            }

            if (chains[destination] > 1)
            {
                return MigrationPlan.Refused(
                    MigrationPlanOutcome.Ambiguous,
                    from,
                    from,
                    to,
                    chains[destination],
                    "the catalog registers " + chains[destination].ToString(CultureInfo.InvariantCulture)
                    + " distinct chains from version " + from.Version.ToString(CultureInfo.InvariantCulture)
                    + " to " + to.Version.ToString(CultureInfo.InvariantCulture)
                    + " for schema " + from.Id.ToString()
                    + "; a requested source/target pair must have exactly one path (P-054).");
            }

            var ordered = new List<ISchemaMigrationStep>();
            int cursor = destination;
            while (cursor > 0)
            {
                ISchemaMigrationStep? step = firstStep[cursor];
                if (step == null)
                {
                    // Unreachable given the relaxation above; reported rather than silently returning a short chain.
                    return MigrationPlan.Refused(
                        MigrationPlanOutcome.Unreachable,
                        from,
                        from,
                        to,
                        0,
                        "the unique chain to version "
                        + to.Version.ToString(CultureInfo.InvariantCulture)
                        + " could not be reconstructed (P-054).");
                }

                ordered.Insert(0, step);
                cursor = predecessor[cursor];
            }

            return MigrationPlan.Unique(from, from, to, ordered);
        }

        /// <summary>
        /// Plans every migration a restore needs at once: for each (schema, captured version) pair, the unique chain
        /// to the catalog's version. Returns false with the first refusal, so a restore reports one actionable
        /// reason instead of a list of symptoms (P-052).
        /// </summary>
        public bool TryPlanAll(
            IReadOnlyList<SchemaRef>? captured,
            IReadOnlyList<SchemaRef>? allocated,
            out IReadOnlyList<MigrationPlan> plans,
            out MigrationPlan? refused,
            out string detail)
        {
            plans = Array.Empty<MigrationPlan>();
            refused = null;
            detail = string.Empty;

            if (captured == null || allocated == null)
            {
                return true;
            }

            var built = new List<MigrationPlan>();
            for (int i = 0; i < captured.Count; i++)
            {
                SchemaRef source = captured[i];
                bool found = false;
                for (int j = 0; j < allocated.Count; j++)
                {
                    if (!allocated[j].Id.Equals(source.Id))
                    {
                        continue;
                    }

                    found = true;
                    MigrationPlan plan = Plan(source, allocated[j]);
                    if (!plan.IsRunnable)
                    {
                        refused = plan;
                        detail = "schema " + source.Id.ToString() + ": " + plan.Detail;
                        plans = Array.Empty<MigrationPlan>();
                        return false;
                    }

                    if (plan.RequiresMigration)
                    {
                        built.Add(plan);
                    }

                    break;
                }

                if (!found)
                {
                    // The catalog this build carries does not know the version the document declares (P-055).
                    var refusal = MigrationPlan.Refused(
                        MigrationPlanOutcome.UnknownSchema,
                        source,
                        source,
                        source,
                        0,
                        "the document declares schema " + source.Id.ToString() + " version "
                        + source.Version.ToString(CultureInfo.InvariantCulture)
                        + " which no serializer of this build accepts (P-055).");
                    refused = refusal;
                    detail = refusal.Detail;
                    plans = Array.Empty<MigrationPlan>();
                    return false;
                }
            }

            plans = built;
            return true;
        }

        private bool HasKey(FactoryKey key)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].Key.Equals(key))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryValidate(ISchemaMigrationStep step, out string rejection)
        {
            rejection = string.Empty;

            if (step.From.Id.Value.IsDefault || step.To.Id.Value.IsDefault
                || step.Key.RegistrationKey.IsDefault)
            {
                rejection = "a migration step carries an all-zero schema or key identity (P-004).";
                return false;
            }

            if (!step.From.Id.Equals(step.To.Id))
            {
                rejection = "a migration step moves between two schema identities ("
                    + step.From.Id.ToString() + " -> " + step.To.Id.ToString()
                    + "); a migration is registered per schema (P-054).";
                return false;
            }

            if (step.To.Version <= step.From.Version)
            {
                rejection = "a migration step for schema " + step.From.Id.ToString() + " moves version "
                    + step.From.Version.ToString(CultureInfo.InvariantCulture) + " to "
                    + step.To.Version.ToString(CultureInfo.InvariantCulture)
                    + "; the graph is directed forward only (P-054).";
                return false;
            }

            return true;
        }

        private static int SaturatedAdd(int left, int right)
        {
            // Path counts saturate: the caller only needs to distinguish 0, 1 and "more than one".
            const int Ceiling = 1 << 20;
            long sum = (long)left + right;
            return sum >= Ceiling ? Ceiling : (int)sum;
        }
    }
}

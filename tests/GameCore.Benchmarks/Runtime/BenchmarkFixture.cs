// GameCore.Benchmarks — the deterministic 1,000-scope/10,000-target composition fixture of 08 section 3.
//
// Normative source: docs/game-core/08-validation-and-performance.md section 3:
//
//   "Use a deterministic fixture generator whose seed and shape are recorded. The initial fixture has 1,000 scopes
//    and 10,000 targets, with three schema families, isolated and excluded branches, and both shared and unique
//    contribution sets. These are diagnostic loads, not assumptions about the eventual game. Include four update
//    sizes: 1 target, 100 targets, 10,000 targets and a whole-world mode switch. Add 1,000-target spawns under an
//    already-active provider and reparent a 100-target subtree between two providers."
//
// Every identity here is derived arithmetically from `(role, index)`, never from a formatted string, so a
// 10,000-target fixture is generated in one pass with no string building and one seed always yields one fixture.
// Nothing is mutated after construction: a workload composes a *variant* through `BenchmarkSnapshotBuilder`, which is
// what lets two variants be compared without either having been built from a half-mutated fixture (P-008).
//
// WHY THE UPDATE SIZES ARE SELECTED BY TAG RATHER THAN BY SCOPE. 08 asks for updates of 1, 100 and 10,000 *targets*.
// A scope-keyed selector gives an exact count only for the tree shape it was designed around, and this generator must
// accept any scale. The fixture therefore declares three tags — one on exactly one target, one on exactly 100, one on
// every target — and each update rule selects by tag, so the *affected* count is exact by construction at any scale.
// What a rule does NOT narrow is its candidate domain, so the runner reports `CandidatesMatched` and
// `ControlNodesVisited` beside the affected count instead of implying the two are the same number.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Derivation.Fixtures;

namespace GameCore.Benchmarks
{
    /// <summary>
    /// The recorded shape of one fixture: the pure composition's counts, the live ECS half's counts and the seed. Both
    /// halves are recorded because the pure derivation scale and the live-world scale are separate costs, and 08
    /// forbids hiding one delay behind the other's number.
    /// </summary>
    public readonly struct BenchmarkScale
    {
        public BenchmarkScale(int scopes, int targets, int liveScopes, int liveTargets, uint seed)
        {
            if (scopes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scopes));
            }

            if (targets <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targets));
            }

            if (liveScopes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(liveScopes));
            }

            if (liveTargets <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(liveTargets));
            }

            Scopes = scopes;
            Targets = targets;
            LiveScopes = liveScopes;
            LiveTargets = liveTargets;
            Seed = seed;
        }

        /// <summary>Scopes of the pure generated composition, including the world root.</summary>
        public int Scopes { get; }

        /// <summary>Targets of the pure generated composition.</summary>
        public int Targets { get; }

        /// <summary>Scopes of the live ECS world the runner seeds and drives.</summary>
        public int LiveScopes { get; }

        /// <summary>Live ECS targets of that world.</summary>
        public int LiveTargets { get; }

        public uint Seed { get; }

        /// <summary>08's declared fixture: 1,000 scopes and 10,000 targets.</summary>
        public static BenchmarkScale Default => new BenchmarkScale(
            BenchmarkWorkloads.DefaultScopes,
            BenchmarkWorkloads.DefaultTargets,
            BenchmarkWorkloads.DefaultScopes,
            BenchmarkWorkloads.DefaultTargets,
            BenchmarkWorkloads.DefaultSeed);

        public override string ToString() =>
            "scopes=" + Scopes.ToString(CultureInfo.InvariantCulture)
            + ";targets=" + Targets.ToString(CultureInfo.InvariantCulture)
            + ";liveScopes=" + LiveScopes.ToString(CultureInfo.InvariantCulture)
            + ";liveTargets=" + LiveTargets.ToString(CultureInfo.InvariantCulture)
            + ";seed=" + Seed.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Stable-identity derivation of the benchmark fixture. Each role occupies its own high byte, so no `(role, index)`
    /// pair collides with another role's and no generated identity is ever the zero identity a constructor refuses.
    /// </summary>
    public static class BenchmarkIds
    {
        /// <summary>Identity role of one generated object; the high byte of the identity.</summary>
        public enum Role : byte
        {
            Scope = 1,
            Target = 2,
            Capability = 3,
            Schema = 4,
            Slot = 5,
            Rule = 6,
            PluginType = 7,
            Instance = 8,
            Recipe = 9,
        }

        /// <summary>An odd constant with good bit dispersion; the low half of every generated identity.</summary>
        private const ulong GoldenOdd = 0x9E3779B97F4A7C15UL;

        /// <summary>Derives one stable identity from its role and index; deterministic and collision-free per role.</summary>
        public static Id128 Id(Role role, int index)
        {
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            ulong high = ((ulong)(byte)role << 56) | (ulong)(uint)index;
            ulong low = GoldenOdd * (ulong)(uint)(index + 1);
            return new Id128(high, low);
        }

        /// <summary>
        /// Mixes the recorded seed into an index. The seed may change *which* targets carry the seeded marker tag and
        /// the payloads of generated rules; it must never change the fixture's declared shape, because a seed that
        /// changed the shape would make two recorded runs incomparable rather than differently loaded.
        /// </summary>
        public static uint Seeded(uint seed, int index) => (unchecked((uint)index) * 2654435761U) ^ seed;

        public static ScopeId Scope(Role role, int index) => new ScopeId(Id(role, index));

        public static TargetId Target(int index) => new TargetId(Id(Role.Target, index));

        /// <summary>The `Id128` of one stable-name-derived capability; used for exclusions and isolation sets.</summary>
        public static Id128 CapabilityValue(string stableName) => StableNameKeyDerivation.Derive(stableName);

        public static CapabilityId Capability(string stableName) => new CapabilityId(CapabilityValue(stableName));

        public static SchemaId Schema(string stableName) => new SchemaId(StableNameKeyDerivation.Derive(stableName));

        public static SlotId Slot(string stableName) => new SlotId(StableNameKeyDerivation.Derive(stableName));

        public static DefinitionId Definition(string stableName) =>
            new DefinitionId(StableNameKeyDerivation.Derive(stableName));

        public static FactoryKey Key(string stableName, uint version = 1U) =>
            new FactoryKey(StableNameKeyDerivation.Derive(stableName), version);

        public static CapabilityRef CapabilityRef(string stableName, uint version = 1U) =>
            new CapabilityRef(Capability(stableName), version);

        public static SchemaRef SchemaRef(string stableName, uint version = 1U) =>
            new SchemaRef(Schema(stableName), version);

        /// <summary>One generated rule identity, unique per <c>(kind, index)</c> so two providers never share a rule id.</summary>
        public static RuleId Rule(int kind, int index) =>
            new RuleId(Id(Role.Rule, (kind * 1000000) + index));

        /// <summary>One generated installation identity.</summary>
        public static PluginInstanceId Instance(int index) => new PluginInstanceId(Id(Role.Instance, index));

        public static PluginTypeId PluginType(int index) => new PluginTypeId(Id(Role.PluginType, index));

        /// <summary>The provider-installation identity of an installation, as derivation reports it (P-017).</summary>
        public static ProviderInstallationId Installation(PluginInstanceId instance) =>
            new ProviderInstallationId(instance.Value);
    }

    /// <summary>Stable names the fixture's capabilities, slots, schemas, registrations and tags derive from.</summary>
    public static class BenchmarkNames
    {
        public const string SharedCapability = "bench.capability.shared";
        public const string LocalCapability = "bench.capability.local";
        public const string UpdateCapability = "bench.capability.update";

        public const string SharedSlot = "bench.slot.shared";
        public const string LocalSlot = "bench.slot.local";
        public const string UpdateSlot = "bench.slot.update";

        public const string SharedSchema = "bench.schema.shared-binding";
        public const string LocalSchema = "bench.schema.local-binding";
        public const string UpdateSchema = "bench.schema.update-binding";

        public const string Reducer = "bench.reducer.int32-sum";
        public const string AlwaysPredicate = "bench.predicate.always";
        public const string TagOnePredicate = "bench.predicate.tag-one";
        public const string TagHundredPredicate = "bench.predicate.tag-hundred";

        public const string TagAll = "bench.tag.all";
        public const string TagOne = "bench.tag.one";
        public const string TagHundred = "bench.tag.hundred";
        public const string TagSeeded = "bench.tag.seeded";

        /// <summary>Stable name of the <c>n</c>-th schema family's recipe schema.</summary>
        public static string FamilySchema(int family) =>
            "bench.schema.family-" + family.ToString(CultureInfo.InvariantCulture);

        /// <summary>Stable name of the <c>n</c>-th schema family's recipe definition.</summary>
        public static string FamilyRecipe(int family) =>
            "bench.recipe.family-" + family.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// One generated fixture: the immutable parts every workload variant shares plus the declared counts the runner
    /// asserts on. The fixture holds no snapshot of its own; <see cref="BenchmarkSnapshotBuilder"/> composes one.
    /// </summary>
    public sealed class BenchmarkFixture
    {
        /// <summary>Level-1 scopes under the world root: the shared-provider branches.</summary>
        public const int GroupCount = 10;

        /// <summary>Leaves under the first group: the reparent branch whose subtree has a known target count.</summary>
        public const int ReparentLeafCount = 10;

        /// <summary>Targets each reparent-branch leaf owns at the declared scale, giving the 100-target subtree.</summary>
        public const int ReparentTargetsPerLeaf = 10;

        /// <summary>Every n-th leaf carries a "unique" provider, so shared and unique contribution sets both exist.</summary>
        public const int UniqueProviderStride = 10;

        /// <summary>Schema families of the fixture; 08 asks for three.</summary>
        public const int SchemaFamilies = 3;

        /// <summary>Targets a hundred-target update affects, and the subtree the reparent workload moves.</summary>
        public const int HundredTargets = 100;

        /// <summary>One target in twenty carries the seeded marker tag, so a seed is observable without changing shape.</summary>
        public const int SeededTagStride = 20;

        internal BenchmarkFixture(
            BenchmarkScale scale,
            IReadOnlyList<DerivationScope> scopes,
            IReadOnlyList<DerivationInstall> installs,
            IReadOnlyList<DerivationTarget> targets,
            IReadOnlyList<CapabilityContract> contracts,
            IDerivationValueSource values,
            IReadOnlyList<ScopeId> groupScopes,
            IReadOnlyList<ScopeId> leafScopes,
            IReadOnlyList<int> targetsPerLeaf,
            ScopeId reparentScope,
            ScopeId secondProviderScope,
            int reparentScopeTargets,
            int uniqueProviderCount,
            int seededTagTargetCount)
        {
            Scale = scale;
            Scopes = scopes;
            Installs = installs;
            Targets = targets;
            Contracts = contracts;
            Values = values;
            GroupScopes = groupScopes;
            LeafScopes = leafScopes;
            TargetsPerLeaf = targetsPerLeaf;
            ReparentScope = reparentScope;
            SecondProviderScope = secondProviderScope;
            ReparentScopeTargets = reparentScopeTargets;
            UniqueProviderCount = uniqueProviderCount;
            SeededTagTargetCount = seededTagTargetCount;
        }

        public BenchmarkScale Scale { get; }

        /// <summary>Every scope, root first, in generated identity order.</summary>
        public IReadOnlyList<DerivationScope> Scopes { get; }

        /// <summary>The base installations: root and group shared providers plus the unique leaf providers.</summary>
        public IReadOnlyList<DerivationInstall> Installs { get; }

        /// <summary>Every target, in generated identity order.</summary>
        public IReadOnlyList<DerivationTarget> Targets { get; }

        /// <summary>The three declared contracts, each declared exactly once (P-017, P-019).</summary>
        public IReadOnlyList<CapabilityContract> Contracts { get; }

        /// <summary>The registered reducer and predicates the fixture's rules resolve (P-009's no-fallback rule).</summary>
        public IDerivationValueSource Values { get; }

        public IReadOnlyList<ScopeId> GroupScopes { get; }

        public IReadOnlyList<ScopeId> LeafScopes { get; }

        /// <summary>Targets owned by each leaf, in <see cref="LeafScopes"/> order.</summary>
        public IReadOnlyList<int> TargetsPerLeaf { get; }

        public ScopeId RootScope => Scopes[0].Scope;

        /// <summary>The subtree the reparent workload moves: the first group.</summary>
        public ScopeId ReparentScope { get; }

        /// <summary>Where it moves to: the second group, whose provider reaches it only after the move (P-025).</summary>
        public ScopeId SecondProviderScope { get; }

        /// <summary>Targets inside <see cref="ReparentScope"/>'s subtree; 100 at the declared scale.</summary>
        public int ReparentScopeTargets { get; }

        public int SharedProviderCount => GroupScopes.Count + 1;

        public int UniqueProviderCount { get; }

        public int SchemaFamilyCount => SchemaFamilies;

        /// <summary>Targets carrying the seeded marker tag; seed-dependent and shape-preserving.</summary>
        public int SeededTagTargetCount { get; }

        public CapabilityId SharedCapability => BenchmarkIds.Capability(BenchmarkNames.SharedCapability);

        public CapabilityId LocalCapability => BenchmarkIds.Capability(BenchmarkNames.LocalCapability);

        public CapabilityId UpdateCapability => BenchmarkIds.Capability(BenchmarkNames.UpdateCapability);

        public SlotId UpdateSlot => BenchmarkIds.Slot(BenchmarkNames.UpdateSlot);

        /// <summary>Targets an update of the named size affects: 1, 100 or every target (all tag-selected).</summary>
        public int TargetCountForSize(int size) => size >= Targets.Count ? Targets.Count : size;

        /// <summary>One-line shape of this fixture, recorded in every raw sample document.</summary>
        public string Describe() =>
            "fixture{scopes=" + Scopes.Count.ToString(CultureInfo.InvariantCulture)
            + ";targets=" + Targets.Count.ToString(CultureInfo.InvariantCulture)
            + ";groups=" + GroupScopes.Count.ToString(CultureInfo.InvariantCulture)
            + ";leaves=" + LeafScopes.Count.ToString(CultureInfo.InvariantCulture)
            + ";schemaFamilies=" + SchemaFamilies.ToString(CultureInfo.InvariantCulture)
            + ";sharedProviders=" + SharedProviderCount.ToString(CultureInfo.InvariantCulture)
            + ";uniqueProviders=" + UniqueProviderCount.ToString(CultureInfo.InvariantCulture)
            + ";baseInstalls=" + Installs.Count.ToString(CultureInfo.InvariantCulture)
            + ";reparentScopeTargets=" + ReparentScopeTargets.ToString(CultureInfo.InvariantCulture)
            + ";seededTagTargets=" + SeededTagTargetCount.ToString(CultureInfo.InvariantCulture)
            + ";seed=" + Scale.Seed.ToString(CultureInfo.InvariantCulture) + "}";


        /// <summary>
        /// The P-022 budget this fixture's declared load needs, derived from the fixture's own shape rather than from a
        /// hand-picked multiplier. P-022 permits a caller to configure a larger limit than the reference guardrails, and
        /// this fixture is deliberately far larger than the small gate fixtures those guardrails were sized for: every
        /// installation's rule can examine every target in its reach domain, so the candidate bound is
        /// `installs x targets` with headroom, the contribution bound is that same product, and the temporary-storage
        /// bound covers one candidate and one contribution record per examined candidate.
        ///
        /// One definition, used by the player's benchmark scenario AND by the suite, so the two cannot disagree about
        /// what "the fixture was accepted" means. The reference values stay visible through
        /// <see cref="PropagationBudget.Reference"/> and are recorded in every raw document beside this budget, because
        /// a performance revision must never move a protocol quota.
        /// </summary>
        public PropagationBudget SuggestedBudget
        {
            get
            {
                long product = (long)Installs.Count * Targets.Count;
                return new PropagationBudget(
                    (product * 2L) + PropagationBudget.DefaultMaxExaminedCandidates,
                    Math.Max(product, PropagationBudget.DefaultMaxEmittedContributions),
                    Math.Max((long)Targets.Count * 2L, PropagationBudget.DefaultMaxAffectedTargets),
                    Math.Max(
                        product * (PropagationBudget.CandidateByteCost + PropagationBudget.ContributionByteCost),
                        PropagationBudget.DefaultMaxTemporaryBytes),
                    PropagationBudget.DefaultPreparationDeadlineMilliseconds,
                    Math.Max(
                        (long)Targets.Count * 20000L,
                        PropagationBudget.DefaultMaxApplyCostEstimateMicroseconds));
            }
        }

        /// <summary>A builder over this fixture; every workload variant starts from a fresh one.</summary>
        public BenchmarkSnapshotBuilder Builder() => new BenchmarkSnapshotBuilder(this);
    }

    /// <summary>The deterministic generator of the 08 fixture. Equal scales and seeds produce equal fixtures.</summary>
    public static class BenchmarkFixtureGenerator
    {
        /// <summary>The smallest scale whose declared shape still holds: root, ten groups and ten reparent leaves.</summary>
        public const int MinimumScopes = 1 + BenchmarkFixture.GroupCount + BenchmarkFixture.ReparentLeafCount;

        public static BenchmarkFixture Generate(BenchmarkScale scale)
        {
            if (scale.Scopes < MinimumScopes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(scale),
                    "the fixture needs at least " + MinimumScopes.ToString(CultureInfo.InvariantCulture)
                    + " scopes (root, " + BenchmarkFixture.GroupCount.ToString(CultureInfo.InvariantCulture)
                    + " groups and " + BenchmarkFixture.ReparentLeafCount.ToString(CultureInfo.InvariantCulture)
                    + " reparent leaves) so its declared branches exist.");
            }

            IDerivationValueSource values = BuildValues();
            var scopes = new List<DerivationScope>(scale.Scopes);

            // Scope 0 is the world root: the only scope whose parent is the default identity (P-010).
            scopes.Add(new DerivationScope(
                BenchmarkIds.Scope(BenchmarkIds.Role.Scope, 0),
                default(ScopeId),
                new IsolationSet(false, null),
                null,
                null));

            var groupScopes = new List<ScopeId>(BenchmarkFixture.GroupCount);
            for (int g = 0; g < BenchmarkFixture.GroupCount; g++)
            {
                ScopeId group = BenchmarkIds.Scope(BenchmarkIds.Role.Scope, 1 + g);
                groupScopes.Add(group);
                scopes.Add(new DerivationScope(
                    group,
                    BenchmarkIds.Scope(BenchmarkIds.Role.Scope, 0),
                    new IsolationSet(false, null),
                    null,
                    null));
            }

            int leafCount = scale.Scopes - 1 - BenchmarkFixture.GroupCount;
            var leafScopes = new List<ScopeId>(leafCount);
            for (int leaf = 0; leaf < leafCount; leaf++)
            {
                ScopeId scope = BenchmarkIds.Scope(BenchmarkIds.Role.Scope, 1 + BenchmarkFixture.GroupCount + leaf);
                leafScopes.Add(scope);

                // The reparent branch is group 1's leaves; every other leaf is spread over groups 2..GroupCount.
                ScopeId parent = leaf < BenchmarkFixture.ReparentLeafCount
                    ? groupScopes[0]
                    : groupScopes[1 + ((leaf - BenchmarkFixture.ReparentLeafCount) % (BenchmarkFixture.GroupCount - 1))];

                // One declared subtree exclusion of the *shared* capability. Both the exclusion and the named
                // capability boundary below deliberately leave the update and local capabilities alone: an exclusion
                // of those would silently change every affected count the update-size workloads assert.
                IReadOnlyList<ExclusionRule>? exclusions = leaf == 0
                    ? new List<ExclusionRule>
                    {
                        new ExclusionRule(
                            ExclusionTargetKind.Capability,
                            BenchmarkIds.CapabilityValue(BenchmarkNames.SharedCapability),
                            scope,
                            default(TargetId),
                            true),
                    }
                    : null;

                // One leaf carries a named capability boundary instead of an exclusion set, so the fixture has both
                // shapes P-016 defines and the two stay distinguishable in a provenance record.
                IsolationSet isolation = leaf == BenchmarkFixture.ReparentLeafCount
                    ? new IsolationSet(
                        false,
                        new List<Id128> { BenchmarkIds.CapabilityValue(BenchmarkNames.SharedCapability) })
                    : new IsolationSet(false, null);

                scopes.Add(new DerivationScope(scope, parent, isolation, exclusions, null));
            }

            int seededTagTargets;
            List<int> targetsPerLeaf = DistributeTargets(scale, leafScopes, out List<DerivationTarget> targets,
                out seededTagTargets);
            IReadOnlyList<CapabilityContract> contracts = BuildContracts();
            IReadOnlyList<DerivationInstall> installs = BuildBaseInstalls(groupScopes, leafScopes, targetsPerLeaf);

            int reparentTargets = 0;
            for (int leaf = 0; leaf < BenchmarkFixture.ReparentLeafCount && leaf < targetsPerLeaf.Count; leaf++)
            {
                reparentTargets += targetsPerLeaf[leaf];
            }

            int uniqueProviderCount = 0;
            for (int leaf = 0; leaf < leafScopes.Count; leaf += BenchmarkFixture.UniqueProviderStride)
            {
                uniqueProviderCount++;
            }

            return new BenchmarkFixture(
                scale,
                scopes,
                installs,
                targets,
                contracts,
                values,
                groupScopes,
                leafScopes,
                targetsPerLeaf,
                groupScopes[0],
                groupScopes[1],
                reparentTargets,
                uniqueProviderCount,
                seededTagTargets);
        }

        /// <summary>
        /// Distributes the declared target count over the leaves. The reparent branch is sized first so its subtree's
        /// target count is known before the rest is spread, which is what makes "reparent a 100-target subtree" a
        /// checkable property rather than a hope about the tree shape.
        /// </summary>
        private static List<int> DistributeTargets(
            BenchmarkScale scale,
            IReadOnlyList<ScopeId> leafScopes,
            out List<DerivationTarget> targets,
            out int seededTagTargets)
        {
            var perLeaf = new List<int>(leafScopes.Count);
            targets = new List<DerivationTarget>(scale.Targets);
            int seeded = 0;
            int index = 0;

            int reparentLeaves = Math.Min(BenchmarkFixture.ReparentLeafCount, leafScopes.Count);
            int reparentBudget = Math.Min(
                scale.Targets,
                reparentLeaves * BenchmarkFixture.ReparentTargetsPerLeaf);
            int reparentPer = reparentBudget / reparentLeaves;
            int reparentExtra = reparentBudget % reparentLeaves;

            for (int leaf = 0; leaf < reparentLeaves; leaf++)
            {
                int count = reparentPer + (leaf < reparentExtra ? 1 : 0);
                perLeaf.Add(count);
                AppendTargets(scale, leafScopes[leaf], count, index, targets, ref seeded);
                index += count;
            }

            int remainingLeaves = leafScopes.Count - reparentLeaves;
            int remaining = scale.Targets - index;
            if (remainingLeaves <= 0)
            {
                // A tiny scale with no room after the reparent branch keeps every declared target in that branch
                // rather than dropping it: the generator never silently loses a target 08 asked for.
                for (int leaf = 0; leaf < reparentLeaves && remaining > 0; leaf++)
                {
                    perLeaf[leaf]++;
                    AppendTargets(scale, leafScopes[leaf], 1, index, targets, ref seeded);
                    index++;
                    remaining--;
                }

                seededTagTargets = seeded;
                return perLeaf;
            }

            int per = remaining / remainingLeaves;
            int extra = remaining % remainingLeaves;
            for (int leaf = 0; leaf < remainingLeaves; leaf++)
            {
                int count = per + (leaf < extra ? 1 : 0);
                perLeaf.Add(count);
                AppendTargets(scale, leafScopes[reparentLeaves + leaf], count, index, targets, ref seeded);
                index += count;
            }

            seededTagTargets = seeded;
            return perLeaf;
        }

        private static void AppendTargets(
            BenchmarkScale scale,
            ScopeId scope,
            int count,
            int firstIndex,
            List<DerivationTarget> into,
            ref int seededTagTargets)
        {
            for (int i = 0; i < count; i++)
            {
                int index = firstIndex + i;
                into.Add(BuildTarget(scale, index, scope, ref seededTagTargets));
            }
        }

        private static DerivationTarget BuildTarget(
            BenchmarkScale scale,
            int index,
            ScopeId scope,
            ref int seededTagTargets)
        {
            int family = index % BenchmarkFixture.SchemaFamilies;
            SchemaRef schema = BenchmarkIds.SchemaRef(BenchmarkNames.FamilySchema(family));
            var recipe = new DefinitionRef(
                BenchmarkIds.Definition(BenchmarkNames.FamilyRecipe(family)),
                schema,
                DefinitionRevision.First);

            var tags = new List<Id128>
            {
                StableNameKeyDerivation.Derive(BenchmarkNames.TagAll),
            };

            if (index < BenchmarkFixture.HundredTargets)
            {
                tags.Add(StableNameKeyDerivation.Derive(BenchmarkNames.TagHundred));
            }

            if (index == 0)
            {
                tags.Add(StableNameKeyDerivation.Derive(BenchmarkNames.TagOne));
            }

            if (BenchmarkIds.Seeded(scale.Seed, index) % BenchmarkFixture.SeededTagStride == 0U)
            {
                tags.Add(StableNameKeyDerivation.Derive(BenchmarkNames.TagSeeded));
                seededTagTargets++;
            }

            // One target carries a target-level exclusion of the shared capability: 08 asks for excluded branches and
            // TEST-008 asks that an exclusion change is an ordinary invalidating edit. The update and local
            // capabilities are untouched, so the exact affected counts stay exact.
            IReadOnlyList<ExclusionRule>? exclusions = index == 1
                ? new List<ExclusionRule>
                {
                    new ExclusionRule(
                        ExclusionTargetKind.Capability,
                        BenchmarkIds.CapabilityValue(BenchmarkNames.SharedCapability),
                        default(ScopeId),
                        BenchmarkIds.Target(index),
                        false),
                }
                : null;

            var descriptor = new TargetDescriptor(
                recipe,
                new List<SchemaRef> { schema },
                null,
                tags,
                default(AssetAdapterDescriptor),
                null,
                null,
                null,
                exclusions);

            return new DerivationTarget(BenchmarkIds.Target(index), scope, descriptor);
        }

        private static IDerivationValueSource BuildValues() =>
            new FixtureValueSource()
                .RegisterInt32Sum(BenchmarkNames.Reducer)
                .RegisterAlwaysPredicate(BenchmarkNames.AlwaysPredicate)
                .RegisterTagPredicate(
                    BenchmarkNames.TagOnePredicate,
                    StableNameKeyDerivation.Derive(BenchmarkNames.TagOne))
                .RegisterTagPredicate(
                    BenchmarkNames.TagHundredPredicate,
                    StableNameKeyDerivation.Derive(BenchmarkNames.TagHundred));

        private static IReadOnlyList<CapabilityContract> BuildContracts()
        {
            return new List<CapabilityContract>
            {
                Contract(BenchmarkNames.SharedCapability, BenchmarkNames.SharedSlot, BenchmarkNames.SharedSchema),
                Contract(BenchmarkNames.LocalCapability, BenchmarkNames.LocalSlot, BenchmarkNames.LocalSchema),
                Contract(BenchmarkNames.UpdateCapability, BenchmarkNames.UpdateSlot, BenchmarkNames.UpdateSchema),
            };
        }

        /// <summary>One `Additive` contract with a single output slot and the fixture's registered int32-sum reducer.</summary>
        internal static CapabilityContract Contract(string capability, string slot, string schema)
        {
            return new CapabilityContract(
                BenchmarkIds.CapabilityRef(capability),
                0,
                new List<OutputSlotSchema>
                {
                    new OutputSlotSchema(BenchmarkIds.Slot(slot), BenchmarkIds.SchemaRef(schema)),
                },
                new List<SlotCompositionPolicy>
                {
                    new SlotCompositionPolicy(
                        BenchmarkIds.Slot(slot),
                        CompositionPolicy.Additive,
                        BenchmarkIds.Key(BenchmarkNames.Reducer)),
                },
                null);
        }

        private static IReadOnlyList<DerivationInstall> BuildBaseInstalls(
            IReadOnlyList<ScopeId> groupScopes,
            IReadOnlyList<ScopeId> leafScopes,
            IReadOnlyList<int> targetsPerLeaf)
        {
            var installs = new List<DerivationInstall>();
            List<SchemaRef> selectors = FamilySelectors();

            // The root provider declares the shared contract, because a capability identity has one declaration per
            // revision; every other shared provider only contributes to it (the shape the reference compositions use).
            installs.Add(Install(
                index: 0,
                scope: BenchmarkIds.Scope(BenchmarkIds.Role.Scope, 0),
                declaresContract: true,
                capability: BenchmarkNames.SharedCapability,
                slot: BenchmarkNames.SharedSlot,
                schema: BenchmarkNames.SharedSchema,
                ruleKind: 1,
                selectors: selectors,
                predicate: BenchmarkNames.AlwaysPredicate,
                payload: 1));

            for (int g = 0; g < groupScopes.Count; g++)
            {
                installs.Add(Install(
                    index: 1 + g,
                    scope: groupScopes[g],
                    declaresContract: false,
                    capability: BenchmarkNames.SharedCapability,
                    slot: BenchmarkNames.SharedSlot,
                    schema: BenchmarkNames.SharedSchema,
                    ruleKind: 2,
                    selectors: selectors,
                    predicate: BenchmarkNames.AlwaysPredicate,
                    payload: 2 + g));
            }

            int uniqueOrdinal = 0;
            for (int leaf = 0; leaf < leafScopes.Count; leaf += BenchmarkFixture.UniqueProviderStride)
            {
                installs.Add(Install(
                    index: 100 + leaf,
                    scope: leafScopes[leaf],
                    declaresContract: uniqueOrdinal == 0,
                    capability: BenchmarkNames.LocalCapability,
                    slot: BenchmarkNames.LocalSlot,
                    schema: BenchmarkNames.LocalSchema,
                    ruleKind: 3,
                    selectors: selectors,
                    predicate: BenchmarkNames.AlwaysPredicate,
                    payload: leaf + 1));
                uniqueOrdinal++;
            }

            if (targetsPerLeaf.Count != leafScopes.Count)
            {
                throw new InvalidOperationException(
                    "the generator distributed targets over "
                    + targetsPerLeaf.Count.ToString(CultureInfo.InvariantCulture)
                    + " leaves but created " + leafScopes.Count.ToString(CultureInfo.InvariantCulture)
                    + " (P-010).");
            }

            return installs;
        }

        /// <summary>The three family recipe schemas every fixture rule selects (08's three schema families).</summary>
        internal static List<SchemaRef> FamilySelectors()
        {
            var selectors = new List<SchemaRef>(BenchmarkFixture.SchemaFamilies);
            for (int family = 0; family < BenchmarkFixture.SchemaFamilies; family++)
            {
                selectors.Add(BenchmarkIds.SchemaRef(BenchmarkNames.FamilySchema(family)));
            }

            return selectors;
        }

        /// <summary>
        /// One installation: an `Active` record at <paramref name="scope"/> under its own plugin type and rule
        /// identity, plus the manifest declaring that rule. Only the first provider of a capability declares the
        /// contract itself; the rest contribute to it, which keeps one capability identity to one declaration.
        /// </summary>
        internal static DerivationInstall Install(
            int index,
            ScopeId scope,
            bool declaresContract,
            string capability,
            string slot,
            string schema,
            int ruleKind,
            IReadOnlyList<SchemaRef> selectors,
            string predicate,
            int payload,
            bool exportToDescendants = true,
            PropagationReach reach = PropagationReach.SelfAndDescendants)
        {
            PluginInstanceId instance = BenchmarkIds.Instance(index);
            PluginTypeId pluginType = BenchmarkIds.PluginType(index);
            FactoryKey factoryKey = BenchmarkIds.Key(
                "bench.factory.provider-" + index.ToString(CultureInfo.InvariantCulture));

            var rule = new DerivationRule(
                BenchmarkIds.Rule(ruleKind, index),
                BenchmarkIds.CapabilityRef(capability),
                0,
                1U,
                selectors,
                BenchmarkIds.Key(predicate),
                null,
                reach,
                exportToDescendants,
                index,
                CompositionPolicy.Additive,
                FixturePayload.Int32(payload));

            var manifest = new PluginManifest(
                pluginType,
                "1.0.0",
                ContentHash.Empty,
                new SupportedProtocolRange(1, 0, 0),
                null,
                BenchmarkIds.SchemaRef(schema),
                factoryKey,
                null,
                null,
                declaresContract
                    ? new List<CapabilityContract> { Contract(capability, slot, schema) }
                    : new List<CapabilityContract>(),
                new List<DerivationRule> { rule },
                null,
                null,
                null,
                null,
                null);

            var record = new InstallRecord(
                instance,
                pluginType,
                scope,
                DefinitionRevision.First,
                ContentHash.Empty,
                index,
                InstallationGeneration.First,
                ActivationEpoch.First);

            return new DerivationInstall(record, InstallationState.Active, manifest);
        }
    }
}

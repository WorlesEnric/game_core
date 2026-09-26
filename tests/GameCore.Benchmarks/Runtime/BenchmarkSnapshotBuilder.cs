// GameCore.Benchmarks — workload variants of the generated fixture.
//
// One 08 fixture, many workloads. Each workload needs its own composition state: an update mounts and unmounts a
// provider, the spawn workload adds 1,000 targets and retires them again, the reparent workload moves a subtree, the
// mode-switch workload flips the world setting. Composing those as *variants* of one immutable fixture is what keeps
// the measurement honest in two ways:
//
//   * a variant is built from the fixture's own lists every time, so an earlier workload cannot leave residue that
//     makes a later one cheaper (a leaked mount would make the next unmount cheaper still);
//   * the revision and epoch advance explicitly, so a variant is a real publication step and the incremental engine
//     is asked a real question rather than being handed an identical snapshot and asked to be fast.
//
// The helpers here are pure: they build `DerivationInstall`, `DerivationTarget` and `DerivationScope` values and never
// touch a snapshot that already exists.
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
    /// Composes one immutable snapshot from the generated fixture plus the changes a workload makes. Every mutating
    /// method returns the builder so a workload reads as one expression; <see cref="Build"/> is the only place a
    /// snapshot is constructed, so a half-built variant is never observable.
    /// </summary>
    public sealed class BenchmarkSnapshotBuilder
    {
        private readonly List<DerivationScope> scopes;
        private readonly List<DerivationInstall> installs;
        private readonly List<DerivationTarget> targets;
        private readonly BenchmarkFixture fixture;

        private PropagationMode mode = PropagationMode.Automatic;
        private CompositionRevision revision = CompositionRevision.First;
        private AssemblyEpoch epoch = AssemblyEpoch.First;

        public BenchmarkSnapshotBuilder(BenchmarkFixture fixture)
        {
            this.fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
            scopes = new List<DerivationScope>(fixture.Scopes);
            installs = new List<DerivationInstall>(fixture.Installs);
            targets = new List<DerivationTarget>(fixture.Targets);
        }

        /// <summary>Scopes currently in the variant.</summary>
        public int ScopeCount => scopes.Count;

        /// <summary>Installations currently in the variant, including any mounted by this workload.</summary>
        public int InstallCount => installs.Count;

        /// <summary>Targets currently in the variant, including any spawned by this workload.</summary>
        public int TargetCount => targets.Count;

        public BenchmarkSnapshotBuilder WithMode(PropagationMode propagationMode)
        {
            mode = propagationMode;
            return this;
        }

        /// <summary>Advances the variant's publication series; a change is always a new revision and epoch (P-006).</summary>
        public BenchmarkSnapshotBuilder WithVersion(CompositionRevision nextRevision, AssemblyEpoch nextEpoch)
        {
            revision = nextRevision;
            epoch = nextEpoch;
            return this;
        }

        public BenchmarkSnapshotBuilder AddInstall(DerivationInstall install)
        {
            if (install == null)
            {
                throw new ArgumentNullException(nameof(install));
            }

            installs.Add(install);
            return this;
        }

        /// <summary>Unmounts one installation by identity; a no-op for an identity the variant does not hold.</summary>
        public BenchmarkSnapshotBuilder RemoveInstall(PluginInstanceId instance)
        {
            for (int i = 0; i < installs.Count; i++)
            {
                if (installs[i].Instance.Equals(instance))
                {
                    installs.RemoveAt(i);
                    return this;
                }
            }

            return this;
        }

        public BenchmarkSnapshotBuilder AddTargets(IReadOnlyList<DerivationTarget> extra)
        {
            if (extra == null)
            {
                throw new ArgumentNullException(nameof(extra));
            }

            for (int i = 0; i < extra.Count; i++)
            {
                targets.Add(extra[i]);
            }

            return this;
        }

        /// <summary>Retires the named targets (P-024); a target the variant does not hold is left alone.</summary>
        public BenchmarkSnapshotBuilder RemoveTargets(IReadOnlyList<TargetId> retired)
        {
            if (retired == null)
            {
                throw new ArgumentNullException(nameof(retired));
            }

            for (int r = 0; r < retired.Count; r++)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    if (targets[i].Target.Equals(retired[r]))
                    {
                        targets.RemoveAt(i);
                        break;
                    }
                }
            }

            return this;
        }

        /// <summary>
        /// Moves one scope under a new parent (P-025). The scope's identity, depth-relative position and the targets
        /// inside it are unchanged; only its ancestors change, which is exactly the union-of-ancestor-sets diff P-025
        /// describes.
        /// </summary>
        public BenchmarkSnapshotBuilder ReparentScope(ScopeId scope, ScopeId newParent)
        {
            for (int i = 0; i < scopes.Count; i++)
            {
                if (!scopes[i].Scope.Equals(scope))
                {
                    continue;
                }

                DerivationScope existing = scopes[i];
                scopes[i] = new DerivationScope(
                    existing.Scope,
                    newParent,
                    existing.CapabilityIsolation,
                    existing.Exclusions,
                    existing.Imports);
                return this;
            }

            throw new ArgumentException(
                "scope " + scope.ToString() + " is not part of this fixture, so it cannot be reparented (P-010).",
                nameof(scope));
        }

        /// <summary>The immutable snapshot of the variant as it currently stands.</summary>
        public DerivationSnapshot Build() =>
            new DerivationSnapshot(
                new WorldId(new Id128(0x42454E43484D4B31UL, 0x0000000000000001UL)),
                revision,
                epoch,
                mode,
                scopes,
                installs,
                targets,
                fixture.Contracts,
                null,
                null);
    }

    /// <summary>The workload-specific values a variant needs: update providers, spawned targets, live-state targets.</summary>
    public static class BenchmarkFixtureVariants
    {
        /// <summary>Rule identity kind of an update rule; the index half is the repetition number.</summary>
        private const int UpdateRuleKind = 11;

        /// <summary>Installation index base of an update provider; disjoint from every base installation index.</summary>
        private const int UpdateInstanceBase = 500000;

        /// <summary>Instance index stride between the three declared update sizes.</summary>
        private const int UpdateSizeStride = 100000;

        /// <summary>Repetitions one workload may run before two repetitions would share an installation identity.</summary>
        public const int MaxRepetitions = 99999;

        /// <summary>Target index base of a spawned target's generated identities.</summary>
        private const int SpawnTargetIndexBase = 1000000;

        /// <summary>Target index base of the live ECS authority fixture's generated identities.</summary>
        private const int LiveTargetIndexBase = 2000000;

        /// <summary>
        /// One update provider for the named update size, mounted at the world root: its rule selects the size's
        /// declared tag, so the affected target count is exact by construction (see the fixture header). The
        /// <paramref name="repetition"/> separates the identity of consecutive repetitions, so a repetition is a real
        /// mount of a distinct installation rather than a no-op against the previous one (P-004, P-006).
        /// </summary>
        public static DerivationInstall UpdateInstall(BenchmarkFixture fixture, int size, int repetition)
        {
            if (fixture == null)
            {
                throw new ArgumentNullException(nameof(fixture));
            }

            if (repetition < 0 || repetition > MaxRepetitions)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(repetition),
                    "an update repetition above " + MaxRepetitions.ToString(CultureInfo.InvariantCulture)
                    + " would reuse an installation identity (P-004).");
            }

            string predicate = size <= 1
                ? BenchmarkNames.TagOnePredicate
                : size >= fixture.Targets.Count
                    ? BenchmarkNames.AlwaysPredicate
                    : BenchmarkNames.TagHundredPredicate;

            // EVERY repetition declares the contract, not only the first. A capability identity has one declaration per
            // revision and two *identical* declarations are deduplicated, which is what lets a loop retract the
            // previous repetition's provider in the same snapshot: if only the first repetition carried the
            // declaration, retracting it would leave a rule emitting into an undeclared capability and the whole
            // proposal would be refused (P-017, P-019). Declaring it every time keeps one contract in the snapshot
            // whether one provider or none of the earlier ones is present.
            return BenchmarkFixtureGenerator.Install(
                index: UpdateInstanceBase + (SizeIndex(size) * UpdateSizeStride) + repetition,
                scope: fixture.RootScope,
                declaresContract: true,
                capability: BenchmarkNames.UpdateCapability,
                slot: BenchmarkNames.UpdateSlot,
                schema: BenchmarkNames.UpdateSchema,
                ruleKind: UpdateRuleKind,
                selectors: BenchmarkFixtureGenerator.FamilySelectors(),
                predicate: predicate,
                payload: size + 1);
        }

        /// <summary>The three declared update sizes map to three disjoint identity bands.</summary>
        private static int SizeIndex(int size)
        {
            if (size <= 1)
            {
                return 0;
            }

            return size >= BenchmarkFixture.HundredTargets ? 2 : 1;
        }

        /// <summary>
        /// <paramref name="count"/> targets spawned under an already-active group provider (P-024). They carry the
        /// fixture's family schemas and the all-targets tag only: the update-size tags stay exclusive to the targets
        /// the fixture declared them on, so a spawn can never change an update workload's affected count.
        /// </summary>
        public static IReadOnlyList<DerivationTarget> SpawnTargets(
            BenchmarkFixture fixture,
            ScopeId scope,
            int count,
            int repetition)
        {
            if (fixture == null)
            {
                throw new ArgumentNullException(nameof(fixture));
            }

            var spawned = new List<DerivationTarget>(count);
            for (int i = 0; i < count; i++)
            {
                int index = SpawnTargetIndexBase + (repetition * 100000) + i;
                int family = i % BenchmarkFixture.SchemaFamilies;
                SchemaRef schema = BenchmarkIds.SchemaRef(BenchmarkNames.FamilySchema(family));
                var recipe = new DefinitionRef(
                    BenchmarkIds.Definition(BenchmarkNames.FamilyRecipe(family)),
                    schema,
                    DefinitionRevision.First);

                var descriptor = new TargetDescriptor(
                    recipe,
                    new List<SchemaRef> { schema },
                    null,
                    new List<Id128> { StableNameKeyDerivation.Derive(BenchmarkNames.TagAll) },
                    default(AssetAdapterDescriptor),
                    null,
                    null,
                    null,
                    null);

                spawned.Add(new DerivationTarget(BenchmarkIds.Target(index), scope, descriptor));
            }

            return spawned;
        }

        /// <summary>Identity of the <c>n</c>-th spawned target of a repetition; used to retire them again (P-024).</summary>
        public static IReadOnlyList<TargetId> SpawnedTargetIds(int count, int repetition)
        {
            var ids = new List<TargetId>(count);
            for (int i = 0; i < count; i++)
            {
                ids.Add(BenchmarkIds.Target(SpawnTargetIndexBase + (repetition * 100000) + i));
            }

            return ids;
        }

        /// <summary>
        /// One live ECS target identity of the authority fixture: the target whose state slot the world's owner writes
        /// and then reads back. It is a generated identity of the live world rather than of the pure fixture, because
        /// the mutation proves a property of the world's storage rather than of the derivation.
        /// </summary>
        public static TargetId LiveStateTarget(int index) => BenchmarkIds.Target(LiveTargetIndexBase + index);

        /// <summary>One update size's declared target count, as the runner records it.</summary>
        public static int DeclaredUpdateSize(BenchmarkFixture fixture, int size) => fixture.TargetCountForSize(size);

        /// <summary>Describe one update provider for the raw document's notes.</summary>
        public static string DescribeUpdate(int size, int repetition) =>
            "updateSize=" + size.ToString(CultureInfo.InvariantCulture)
            + ";repetition=" + repetition.ToString(CultureInfo.InvariantCulture);
    }
}

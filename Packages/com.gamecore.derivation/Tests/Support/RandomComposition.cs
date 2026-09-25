// GameCore.Derivation tests — a seeded randomized small composition (GC-006, TEST-008).
//
// The differential test needs many small fixtures whose shape varies in every dimension the derivation depends
// on: scope depth, reach, export, priority, policy, payload, isolation, exclusions, overrides and lifecycle state.
// Everything is derived from one seed through an explicit LCG, so a failing seed is exactly reproducible and the
// counterexample can be kept (TEST-008: "Keep failed seeds and the reduced counterexample").
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Derivation.Fixtures;

namespace GameCore.Derivation.Tests
{
    /// <summary>Deterministic generator of small random compositions.</summary>
    public static class RandomComposition
    {
        public const string AlwaysPredicate = "rnd.predicate.always";
        public const string Int32Reducer = "rnd.reducer.int32-sum";

        /// <summary>The value source every random fixture shares; the same registration set for both engines.</summary>
        public static FixtureValueSource ValueSource() =>
            new FixtureValueSource()
                .RegisterInt32Sum(Int32Reducer)
                .RegisterAlwaysPredicate(AlwaysPredicate);

        /// <summary>Builds the composition of one seed. Equal seeds produce equal compositions.</summary>
        public static FixtureComposition Build(uint seed)
        {
            Lcg random = new Lcg(seed);
            WorldId world = new WorldId(FixtureIds.Id("gamecore.world.random"));
            string prefix = "rnd.s" + seed.ToString(System.Globalization.CultureInfo.InvariantCulture);

            // Scope tree: root, two to four children, and up to two grandchildren under the first child.
            string root = prefix + ".root";
            FixtureBuilder builder = new FixtureBuilder(world);
            builder.Scope(root, null);

            int branchCount = random.NextInclusive(2, 4);
            List<string> branches = new List<string>();
            for (int i = 0; i < branchCount; i++)
            {
                string branch = prefix + ".branch" + i;
                branches.Add(branch);
                bool isolate = random.Next(6) == 0;
                bool namedBoundary = !isolate && random.Next(8) == 0;
                builder.Scope(
                    branch,
                    root,
                    isolateAllCapabilities: isolate,
                    isolatedCapabilities: namedBoundary ? new[] { prefix + ".cap0" } : null);
            }

            string leaf = branches[0];
            if (random.NextBool())
            {
                leaf = prefix + ".leaf";
                builder.Scope(leaf, branches[0]);
            }

            // Capabilities: cap0 at stratum 0, cap1 at stratum 1 reading cap0 when the seed says so.
            CompositionPolicy policy0 = PolicyFor(random);
            CompositionPolicy policy1 = CompositionPolicy.Replace;
            FactoryKey reducer = policy0 == CompositionPolicy.Additive
                ? FixtureIds.Key(Int32Reducer)
                : default(FactoryKey);

            builder
                .Contract(prefix + ".cap0", 0, new[]
                {
                    new FixtureSlot(prefix + ".schema0", policy0, reducer: reducer),
                })
                .Contract(prefix + ".cap1", 1, new[]
                {
                    new FixtureSlot(prefix + ".schema1", policy1),
                });

            int targetCount = random.NextInclusive(2, 4);
            List<string> targets = new List<string>();
            List<string> sharedRecipeTargets = new List<string>();
            Dictionary<string, string> targetScope = new Dictionary<string, string>();
            for (int i = 0; i < targetCount; i++)
            {
                string target = prefix + ".t" + i;
                targets.Add(target);
                List<string> scopes = new List<string> { root };
                scopes.AddRange(branches);
                if (!leaf.Equals(branches[0]))
                {
                    scopes.Add(leaf);
                }

                string scope = scopes[random.Next(scopes.Count)];
                bool secondRecipe = random.Next(5) == 0;
                targetScope.Add(target, scope);
                if (!secondRecipe)
                {
                    sharedRecipeTargets.Add(target);
                }

                builder.Target(target, scope, secondRecipe ? otherRecipe : recipe);
            }

            // One occasional target-level exclusion of cap0, on a target that is otherwise compatible.
            if (random.Next(4) == 0 && sharedRecipeTargets.Count > 0)
            {
                string excluded = sharedRecipeTargets[random.Next(sharedRecipeTargets.Count)];
                builder.ReplaceTarget(
                    excluded,
                    targetScope[excluded],
                    recipe,
                    new List<SchemaRef> { FixtureIds.SchemaRef(recipe) },
                    new[]
                    {
                        new ExclusionRule(
                            ExclusionTargetKind.Capability,
                            FixtureIds.Capability(prefix + ".cap0").Value,
                            default(ScopeId),
                            default(TargetId),
                            false),
                    });
            }

            // Installations: one to three providers with one or two rules each.
            int providerCount = random.NextInclusive(1, 3);
            for (int p = 0; p < providerCount; p++)
            {
                List<string> scopes = new List<string> { root };
                scopes.AddRange(branches);
                string scope = scopes[random.Next(scopes.Count)];
                string provider = prefix + ".p" + p;
                int ruleCount = random.NextInclusive(1, 2);
                List<DerivationRule> rules = new List<DerivationRule>();
                for (int r = 0; r < ruleCount; r++)
                {
                    string capability = random.NextBool() ? prefix + ".cap0" : prefix + ".cap1";
                    CompositionPolicy policy = capability.EndsWith("0", StringComparison.Ordinal) ? policy0 : policy1;
                    CompositionPolicy rulePolicy = policy == CompositionPolicy.Additive && reducer.RegistrationKey.IsDefault
                        ? CompositionPolicy.Replace
                        : policy;

                    bool selectsOther = random.Next(6) == 0;
                    string schema = selectsOther ? otherRecipe : recipe;
                    int stratum = capability.EndsWith("0", StringComparison.Ordinal) ? 0 : 1;
                    PropagationReach reach = ReachFor(random);
                    FrozenPayload payload = policy == CompositionPolicy.Additive
                        ? FixturePayload.Int32(random.NextInclusive(0, 4))
                        : FixturePayload.Tag(provider + ".r" + r + ".value");

                    rules.Add(FixtureBuilder.Rule(
                        provider + ".r" + r,
                        capability,
                        stratum,
                        1U,
                        FixtureBuilder.Selector(schema),
                        FixtureIds.Key(AlwaysPredicate),
                        stratum == 1 && random.NextBool()
                            ? FixtureBuilder.Inputs(prefix + ".cap0")
                            : null,
                        reach,
                        random.NextBool(),
                        random.NextInclusive(-2, 2),
                        rulePolicy,
                        payload));

                    if (policy0 == CompositionPolicy.Ordered && capability.EndsWith("0", StringComparison.Ordinal))
                    {
                        string selfKey = provider + ".r" + r + ".key";
                        builder.RuleKey(
                            provider + ".r" + r,
                            capability,
                            selfKey,
                            before: null,
                            after: r == 0
                                ? null
                                : new[] { new FixtureOrderEdge(provider + ".r0.key", !random.NextBool()) });
                    }
                }

                InstallationState state = random.Next(7) == 0
                    ? InstallationState.WaitingForDependencies
                    : InstallationState.Active;
                builder.Install(provider, scope, random.NextInclusive(-1, 1), rules, state: state);
            }

            // One occasional explicit selection override (legal only for the Replace/Exclusive policies).
            if (random.Next(5) == 0 && providerCount >= 2 && policy0 != CompositionPolicy.Additive
                && policy0 != CompositionPolicy.Ordered)
            {
                builder.Select(prefix + ".cap0", prefix + ".p1", atTarget: targets[random.Next(targets.Count)]);
            }

            PropagationMode mode = random.NextBool() ? PropagationMode.Automatic : PropagationMode.Conservative;
            return builder.Build(mode, new CompositionRevision(1UL), AssemblyEpoch.First);
        }

        /// <summary>The seeds the differential test sweeps; a shared list so a failure names one seed.</summary>
        public static IReadOnlyList<uint> Seeds(int count, uint first)
        {
            List<uint> seeds = new List<uint>(count);
            for (int i = 0; i < count; i++)
            {
                seeds.Add(first + (uint)i);
            }

            return seeds;
        }

        private static CompositionPolicy PolicyFor(Lcg random)
        {
            switch (random.Next(7))
            {
                case 0:
                case 1:
                case 2:
                    return CompositionPolicy.Additive;
                case 3:
                    return CompositionPolicy.Exclusive;
                case 4:
                    return CompositionPolicy.Incompatible;
                case 5:
                    return CompositionPolicy.Ordered;
                default:
                    return CompositionPolicy.Replace;
            }
        }

        private static PropagationReach ReachFor(Lcg random)
        {
            switch (random.Next(3))
            {
                case 0:
                    return PropagationReach.LocalOnly;
                case 1:
                    return PropagationReach.DescendantsOnly;
                default:
                    return PropagationReach.SelfAndDescendants;
            }
        }
    }
}

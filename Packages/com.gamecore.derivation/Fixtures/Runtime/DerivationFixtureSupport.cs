// GameCore.Derivation fixtures — deterministic support shared by the derivation fixtures and tests (GC-006).
//
// Everything here is a pure function of its arguments: identities are derived from documented stable names with
// the production `StableNameKeyDerivation` (so a literal a later wave hard-codes and a fixture that derives it
// agree), payloads are fixed-width big-endian, and the value source registers explicit reducers and predicates
// with no reflection fallback. That is what makes a permutation test meaningful (P-008) and lets these fixtures be
// reused unchanged by GC-010 (narrative) and GC-011 (cards).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Derivation.Fixtures
{
    /// <summary>Stable-name-keyed identities of one fixture vocabulary.</summary>
    public static class FixtureIds
    {
        /// <summary>The documented derivation of every fixture identity (P-004, 05 s3).</summary>
        public static Id128 Id(string stableName) => StableNameKeyDerivation.Derive(stableName);

        public static ScopeId Scope(string stableName) => new ScopeId(Id(stableName));

        public static TargetId Target(string stableName) => new TargetId(Id(stableName));

        public static CapabilityId Capability(string stableName) => new CapabilityId(Id(stableName));

        public static SchemaId Schema(string stableName) => new SchemaId(Id(stableName));

        public static SlotId Slot(string stableName) => new SlotId(Id(stableName));

        public static RuleId Rule(string stableName) => new RuleId(Id(stableName));

        public static PluginTypeId PluginType(string stableName) => new PluginTypeId(Id(stableName));

        public static PluginInstanceId Instance(string stableName) => new PluginInstanceId(Id(stableName));

        public static DefinitionId Definition(string stableName) => new DefinitionId(Id(stableName));

        /// <summary>A generated registration key for a fixture reducer, predicate or factory (P-009).</summary>
        public static FactoryKey Key(string stableName, uint version = 1U) => new FactoryKey(Id(stableName), version);

        public static CapabilityRef CapabilityRef(string stableName, uint version = 1U) =>
            new CapabilityRef(Capability(stableName), version);

        public static SchemaRef SchemaRef(string stableName, uint version = 1U) =>
            new SchemaRef(Schema(stableName), version);

        public static DefinitionRef Recipe(string stableName, string schemaName, uint schemaVersion = 1U) =>
            new DefinitionRef(Definition(stableName), SchemaRef(schemaName, schemaVersion), DefinitionRevision.First);

        public static ContractRef Contract(string stableName, uint version = 1U) =>
            new ContractRef(Id(stableName), version);
    }

    /// <summary>Fixed-width big-endian payload encoding for fixture values (05 s6 wire conventions).</summary>
    public static class FixturePayload
    {
        /// <summary>Four-byte big-endian signed value: the fixture's integer configuration payload.</summary>
        public static FrozenPayload Int32(int value)
        {
            byte[] bytes = new byte[4];
            uint raw = unchecked((uint)value);
            bytes[0] = (byte)(raw >> 24);
            bytes[1] = (byte)(raw >> 16);
            bytes[2] = (byte)(raw >> 8);
            bytes[3] = (byte)raw;
            return new FrozenPayload(bytes);
        }

        /// <summary>Eight-byte big-endian unsigned identity: the fixture's reference payload.</summary>
        public static FrozenPayload Id128Payload(Id128 value)
        {
            byte[] bytes = new byte[16];
            Id128Codec.WriteBigEndian(value, bytes, 0);
            return new FrozenPayload(bytes);
        }

        public static FrozenPayload Tag(string stableName) => Id128Payload(FixtureIds.Id(stableName));

        public static bool TryReadInt32(FrozenPayload payload, out int value)
        {
            value = 0;
            IReadOnlyList<byte> bytes = payload.Bytes;
            if (bytes.Count != 4)
            {
                return false;
            }

            uint raw = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
            value = unchecked((int)raw);
            return true;
        }

        public static bool TryReadId128(FrozenPayload payload, out Id128 value)
        {
            value = Id128.Zero;
            IReadOnlyList<byte> bytes = payload.Bytes;
            if (bytes.Count != Id128.SizeInBytes)
            {
                return false;
            }

            byte[] copy = new byte[Id128.SizeInBytes];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = bytes[i];
            }

            value = Id128Codec.ReadBigEndian(copy, 0);
            return true;
        }
    }

    /// <summary>
    /// A generated-style registry of fixture reducers and predicates. Registration is explicit; an unregistered
    /// key reports a miss so the kernel never substitutes a default (P-009, P-028).
    /// </summary>
    public sealed class FixtureValueSource : IDerivationValueSource
    {
        private readonly Dictionary<Id128, Func<IReadOnlyList<FrozenPayload>, FrozenPayload>> reducers =
            new Dictionary<Id128, Func<IReadOnlyList<FrozenPayload>, FrozenPayload>>();

        private readonly Dictionary<Id128, Func<DerivationPredicateContext, bool>> predicates =
            new Dictionary<Id128, Func<DerivationPredicateContext, bool>>();

        /// <summary>Registers an Int32 sum with an explicit range check (P-019: no assumed associativity).</summary>
        public FixtureValueSource RegisterInt32Sum(string stableName)
        {
            reducers[FixtureIds.Id(stableName)] = SumInt32;
            return this;
        }

        /// <summary>Registers a canonical union of 16-byte identities (P-019 set union with a reducer key).</summary>
        public FixtureValueSource RegisterIdUnion(string stableName)
        {
            reducers[FixtureIds.Id(stableName)] = UnionIds;
            return this;
        }

        /// <summary>Registers a predicate that requires one declared descriptor tag (P-015).</summary>
        public FixtureValueSource RegisterTagPredicate(string stableName, Id128 tag)
        {
            predicates[FixtureIds.Id(stableName)] = context => context.Target.DeclaresTag(tag);
            return this;
        }

        /// <summary>Registers a predicate that requires one finalized lower-stratum capability (P-021).</summary>
        public FixtureValueSource RegisterLowerStratumPredicate(string stableName, CapabilityId capability)
        {
            predicates[FixtureIds.Id(stableName)] = context => context.HasEffectiveCapability(capability);
            return this;
        }

        /// <summary>Registers a predicate that always accepts: the "no extra condition" fixture rule.</summary>
        public FixtureValueSource RegisterAlwaysPredicate(string stableName)
        {
            predicates[FixtureIds.Id(stableName)] = context => true;
            return this;
        }

        public bool IsReductionRegistered(FactoryKey reducer) => reducers.ContainsKey(reducer.RegistrationKey);

        public bool IsPredicateRegistered(FactoryKey predicate) => predicates.ContainsKey(predicate.RegistrationKey);

        public bool TryReduce(FactoryKey reducer, IReadOnlyList<FrozenPayload> inputs, out FrozenPayload? result)
        {
            if (reducers.TryGetValue(reducer.RegistrationKey, out Func<IReadOnlyList<FrozenPayload>, FrozenPayload>? fold))
            {
                result = fold(inputs);
                return true;
            }

            result = null;
            return false;
        }

        public bool TryEvaluate(FactoryKey predicate, DerivationPredicateContext context, out bool result)
        {
            if (predicates.TryGetValue(predicate.RegistrationKey, out Func<DerivationPredicateContext, bool>? check))
            {
                result = check(context);
                return true;
            }

            result = false;
            return false;
        }

        private static FrozenPayload SumInt32(IReadOnlyList<FrozenPayload> inputs)
        {
            long total = 0L;
            for (int i = 0; i < inputs.Count; i++)
            {
                int value;
                if (!FixturePayload.TryReadInt32(inputs[i], out value))
                {
                    throw new ReducerFailureException(default(FactoryKey), "a value is not a 4-byte integer");
                }

                total += value;
                if (total > int.MaxValue || total < int.MinValue)
                {
                    // P-019: the reducer validates overflow/range and the error rejects the whole proposal.
                    throw new ReducerFailureException(default(FactoryKey), "the sum overflows Int32");
                }
            }

            return FixturePayload.Int32((int)total);
        }

        private static FrozenPayload UnionIds(IReadOnlyList<FrozenPayload> inputs)
        {
            List<Id128> unique = new List<Id128>();
            for (int i = 0; i < inputs.Count; i++)
            {
                Id128 value;
                if (!FixturePayload.TryReadId128(inputs[i], out value))
                {
                    throw new ReducerFailureException(default(FactoryKey), "a value is not a 16-byte identity");
                }

                bool duplicate = false;
                for (int j = 0; j < unique.Count; j++)
                {
                    if (unique[j].Equals(value))
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                {
                    unique.Add(value);
                }
            }

            unique.Sort(Id128Codec.CompareBigEndian);
            byte[] bytes = new byte[unique.Count * Id128.SizeInBytes];
            for (int i = 0; i < unique.Count; i++)
            {
                Id128Codec.WriteBigEndian(unique[i], bytes, i * Id128.SizeInBytes);
            }

            return new FrozenPayload(bytes);
        }
    }

    /// <summary>
    /// One fixture composition: the parts of a snapshot plus the world/revision/mode it was declared against.
    /// Tests permute the lists before building a snapshot, which is how "insertion order never participates" is
    /// checked without duplicating the fixture data (P-008).
    /// </summary>
    public sealed class FixtureComposition
    {
        public FixtureComposition(
            WorldId world,
            CompositionRevision revision,
            AssemblyEpoch epoch,
            PropagationMode mode,
            IReadOnlyList<DerivationScope> scopes,
            IReadOnlyList<DerivationInstall> installs,
            IReadOnlyList<DerivationTarget> targets,
            IReadOnlyList<CapabilityContract> contracts,
            IReadOnlyList<DerivationRuleKeys>? ruleKeys,
            IReadOnlyList<ProviderSelectionOverride>? overrides)
        {
            World = world;
            Revision = revision;
            Epoch = epoch;
            Mode = mode;
            Scopes = scopes;
            Installs = installs;
            Targets = targets;
            Contracts = contracts;
            RuleKeys = ruleKeys ?? Array.Empty<DerivationRuleKeys>();
            Overrides = overrides ?? Array.Empty<ProviderSelectionOverride>();
        }

        public WorldId World { get; }

        public CompositionRevision Revision { get; }

        public AssemblyEpoch Epoch { get; }

        public PropagationMode Mode { get; }

        public IReadOnlyList<DerivationScope> Scopes { get; }

        public IReadOnlyList<DerivationInstall> Installs { get; }

        public IReadOnlyList<DerivationTarget> Targets { get; }

        public IReadOnlyList<CapabilityContract> Contracts { get; }

        public IReadOnlyList<DerivationRuleKeys> RuleKeys { get; }

        public IReadOnlyList<ProviderSelectionOverride> Overrides { get; }

        /// <summary>Builds the snapshot. Every constructor canonicalizes its input, so order here never matters.</summary>
        public DerivationSnapshot ToSnapshot() =>
            new DerivationSnapshot(
                World,
                Revision,
                Epoch,
                Mode,
                Scopes,
                Installs,
                Targets,
                Contracts,
                RuleKeys,
                Overrides);

        /// <summary>
        /// A copy with the same data but the given list order. Feeding a permutation through this method is the
        /// canonical-order test: the result must be identical to the unpermuted one, in values and in provenance.
        /// </summary>
        public FixtureComposition WithOrder(Permutation permutation) =>
            new FixtureComposition(
                World,
                Revision,
                Epoch,
                Mode,
                permutation.Apply(Scopes),
                permutation.Apply(Installs),
                permutation.Apply(Targets),
                permutation.Apply(Contracts),
                permutation.Apply(RuleKeys),
                permutation.Apply(Overrides));

        /// <summary>A copy with the same data in a different world-level propagation mode (P-013).</summary>
        public FixtureComposition WithMode(PropagationMode mode) =>
            new FixtureComposition(
                World,
                Revision,
                Epoch,
                mode,
                Scopes,
                Installs,
                Targets,
                Contracts,
                RuleKeys,
                Overrides);

        /// <summary>A copy at another revision and epoch: the version-domain move a publication performs (P-006).</summary>
        public FixtureComposition WithVersion(CompositionRevision revision, AssemblyEpoch epoch) =>
            new FixtureComposition(
                World,
                revision,
                epoch,
                Mode,
                Scopes,
                Installs,
                Targets,
                Contracts,
                RuleKeys,
                Overrides);
    }

    /// <summary>Deterministic list permutation from a fixed seed; independent of any runtime hash order (P-008).</summary>
    public sealed class Permutation
    {
        private readonly int seed;
        private readonly int step;

        public Permutation(int seed, int step)
        {
            this.seed = seed;
            this.step = step;
        }

        /// <summary>A stable sort key for item <paramref name="index"/>: a seeded modular walk of the list.</summary>
        public int KeyFor(int index) => unchecked((index * (step + 1)) ^ (seed * 2654435761));

        /// <summary>The same items, reordered by the seeded key; ties keep their relative order.</summary>
        public IReadOnlyList<T> Apply<T>(IReadOnlyList<T> source)
        {
            if (source == null || source.Count < 2)
            {
                return source ?? Array.Empty<T>();
            }

            List<int> indices = new List<int>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                indices.Add(i);
            }

            indices.Sort((left, right) =>
            {
                int compare = KeyFor(left).CompareTo(KeyFor(right));
                return compare != 0 ? compare : left.CompareTo(right);
            });

            List<T> result = new List<T>(source.Count);
            for (int i = 0; i < indices.Count; i++)
            {
                result.Add(source[indices[i]]);
            }

            return result;
        }

        /// <summary>A rotation instead of a keyed shuffle; the second family of insertion orders.</summary>
        public IReadOnlyList<T> Rotate<T>(IReadOnlyList<T> source)
        {
            if (source == null || source.Count < 2)
            {
                return source ?? Array.Empty<T>();
            }

            int offset = ((seed % source.Count) + source.Count) % source.Count;
            List<T> result = new List<T>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                result.Add(source[(i + offset) % source.Count]);
            }

            return result;
        }
    }
}

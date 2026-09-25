// GameCore.Derivation — the generated-code seam of one derivation (GC-006).
//
// P-019: a reducer is pure, bounded, versioned and supplied by the contract's package; the kernel never infers
// numerical semantics. P-015: a rule's predicate is a static predicate; eligibility cannot depend on arbitrary
// live mutable state, wall time or callbacks with side effects. Both arrive as precompiled registrations keyed by
// `FactoryKey`, exactly like every other generated lookup in this protocol (P-009, 04 s8): a miss is reported and
// never substituted by reflection.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Derivation
{
    /// <summary>Everything a static predicate may read: declared descriptors and finalized lower-stratum capabilities.</summary>
    public sealed class DerivationPredicateContext
    {
        public DerivationPredicateContext(
            DerivationTarget target,
            IReadOnlyList<CapabilityRef>? declaredCapabilities,
            IReadOnlyList<CapabilityId>? effectiveCapabilities,
            IReadOnlyList<SchemaRef>? declaredSchemas,
            IReadOnlyList<Id128>? tags,
            int stratum)
        {
            Target = target;
            DeclaredCapabilities = ContractCollections.Freeze(declaredCapabilities);
            EffectiveCapabilities = ContractCollections.Freeze(effectiveCapabilities);
            DeclaredSchemas = ContractCollections.Freeze(declaredSchemas);
            Tags = ContractCollections.Freeze(tags);
            Stratum = stratum;
        }

        public DerivationTarget Target { get; }

        public IReadOnlyList<CapabilityRef> DeclaredCapabilities { get; }

        /// <summary>Capabilities already finalized at strictly lower strata (P-021).</summary>
        public IReadOnlyList<CapabilityId> EffectiveCapabilities { get; }

        public IReadOnlyList<SchemaRef> DeclaredSchemas { get; }

        public IReadOnlyList<Id128> Tags { get; }

        /// <summary>The stratum whose candidates are being evaluated.</summary>
        public int Stratum { get; }

        public bool HasEffectiveCapability(CapabilityId capability)
        {
            for (int i = 0; i < EffectiveCapabilities.Count; i++)
            {
                if (EffectiveCapabilities[i].Equals(capability))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// The generated reducer/static-predicate registry of one derivation (P-009, P-019). Implementations are
    /// explicit tables; there is no reflection fallback and no implicit default.
    /// </summary>
    public interface IDerivationValueSource
    {
        /// <summary>True when this key resolves to a registered reducer; a miss is a catalog error (P-028).</summary>
        bool IsReductionRegistered(FactoryKey reducer);

        /// <summary>True when this key resolves to a registered static predicate (P-015, P-028).</summary>
        bool IsPredicateRegistered(FactoryKey predicate);

        /// <summary>
        /// Folds values in precedence order through one registered reducer. False means the key is not registered,
        /// which is a catalog error reported by validation (P-028) rather than a silent identity. A registered
        /// reducer that detects an overflow or an out-of-range value throws
        /// <see cref="ReducerFailureException"/>, which rejects the whole proposal (P-019).
        /// </summary>
        bool TryReduce(FactoryKey reducer, IReadOnlyList<FrozenPayload> inputs, out FrozenPayload? result);

        /// <summary>Evaluates one registered static predicate; false means the key is not registered (P-015, P-028).</summary>
        bool TryEvaluate(FactoryKey predicate, DerivationPredicateContext context, out bool result);
    }

    /// <summary>
    /// Raised by a registered reducer that cannot produce a valid value (overflow or range). P-019 requires the
    /// error to reject the whole proposal, so this is never swallowed into a truncated or best-effort result.
    /// </summary>
    public sealed class ReducerFailureException : Exception
    {
        public ReducerFailureException(FactoryKey reducer, string reason)
            : base("Reducer " + reducer.ToString() + " rejected its inputs: " + reason)
        {
            Reducer = reducer;
        }

        public FactoryKey Reducer { get; }
    }

    /// <summary>
    /// An empty registry: every declared predicate and reducer reports a miss. Useful as the explicit
    /// "this catalog registers nothing" baseline, never as a silent fallback.
    /// </summary>
    public sealed class EmptyDerivationValueSource : IDerivationValueSource
    {
        public static EmptyDerivationValueSource Instance { get; } = new EmptyDerivationValueSource();

        public bool IsReductionRegistered(FactoryKey reducer) => false;

        public bool IsPredicateRegistered(FactoryKey predicate) => false;

        public bool TryReduce(FactoryKey reducer, IReadOnlyList<FrozenPayload> inputs, out FrozenPayload? result)
        {
            result = null;
            return false;
        }

        public bool TryEvaluate(FactoryKey predicate, DerivationPredicateContext context, out bool result)
        {
            result = false;
            return false;
        }
    }

    /// <summary>Deterministic 128-bit evidence keys for interned provenance records (P-004, P-026).</summary>
    public static class EvidenceKeys
    {
        /// <summary>
        /// First 16 bytes of SHA-256 over the canonical UTF-8 name, read as two big-endian 64-bit words: the same
        /// derivation rule as <see cref="StableNameKeyDerivation"/>, applied to a canonical textual key instead
        /// of a declared stable name.
        /// </summary>
        public static Id128 Derive(string canonicalKey)
        {
            if (canonicalKey == null)
            {
                throw new ArgumentNullException(nameof(canonicalKey));
            }

            byte[] digest;
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                digest = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(canonicalKey));
            }

            return Id128Codec.ReadBigEndian(digest, 0);
        }

        /// <summary>The canonical key of one candidate evaluation.</summary>
        public static string CandidateKey(
            ProviderInstallationId provider,
            RuleId rule,
            TargetId target,
            CapabilityId capability,
            uint outputSlot) =>
            "candidate:" + provider.ToString() + "/" + rule.ToString() + "/" + target.ToString()
            + "/" + capability.ToString() + "/" + outputSlot.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The canonical key of one explanation record (target plus capability).</summary>
        public static string ExplanationKey(TargetId target, CapabilityId capability) =>
            "explain:" + target.ToString() + "/" + capability.ToString();

        /// <summary>The canonical key of one descriptor or exclusion evidence item.</summary>
        public static string EvidenceKey(string kind, string canonicalValue) => kind + ":" + canonicalValue;
    }
}

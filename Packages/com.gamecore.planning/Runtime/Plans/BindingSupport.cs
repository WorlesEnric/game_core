// GameCore.Planning — the support set of one effective binding row (GC-012, Wave 4 kernel-gap correction).
//
// Normative sources: 00 P-017 ("Support from multiple contributions is a set of IDs, not a boolean owned by the
// last plugin"), P-018 (canonical candidate order), P-019 (`Additive` folds every contribution through the
// declared reducer, so one slot may carry several supporters and one composed value), P-026 (explanation names
// the supporting candidates) and P-033 ("removing one provider removes exactly its support").
//
// Why this type exists:
//
//   Before this file a binding row carried exactly one `Provider`, one generation and one value, and the
//   derivation-to-publication seam *refused* any effective slot with more than one supporter — see the header of
//   `DerivationProposalBridge`. That made P-017/P-019 representable only inside the pure derivation model
//   (`EffectiveSlot.Support`) and provable only by pure tests (`CardRulesTests.TryReduceBonusFoldsTheContributionsInAscendingIndexOrder`,
//   derivation `RefC04`). The composed value of an `Additive` slot with two providers therefore never reached a
//   live ECS world.
//
//   `CapabilitySupport` is the representation that was missing: one immutable record per contribution that
//   supports an effective slot, carried by `TargetBindingRow`, by the `DerivedBindingRule` a future spawn derives
//   from, and (through `GameCore.Unity.Runtime.CapabilitySupportRow`) by the published ECS storage. The row's
//   `Value` is the composed value of P-019; the support set names who supplied it.
//
// Ordering: the set is canonical (P-008) — sorted by descending priority, then ascending provider installation
// id, then ascending rule id. That is the sub-order of P-018 available at this layer; it exists so two runs that
// derive the same assembly report the same set in the same order, and it is never used to choose a value (the
// composition policy already did that).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Planning
{
    /// <summary>
    /// One contribution's support of one effective binding row (P-017). Its identity is
    /// `(Provider, Rule)`; value, generation and priority are the supporter's own contribution data, kept so an
    /// explanation (P-026) can describe each supporter without re-deriving it.
    /// </summary>
    public readonly struct CapabilitySupport : IEquatable<CapabilitySupport>
    {
        /// <summary>Provider installation that supplies this support (P-017).</summary>
        public readonly ProviderInstallationId Provider;

        /// <summary>Activation generation of that installation; it changes on remount, not on reconfigure (P-005).</summary>
        public readonly ulong ProviderGeneration;

        /// <summary>The derivation rule that emitted the contribution; it is part of `ContributionKey` (P-017).</summary>
        public readonly RuleId Rule;

        /// <summary>The supporter's own contribution value, before the slot policy composed the effective value.</summary>
        public readonly int Value;

        /// <summary>The supporter's manifest priority, the first P-018 rank component.</summary>
        public readonly int Priority;

        public CapabilitySupport(
            ProviderInstallationId provider,
            ulong providerGeneration,
            RuleId rule,
            int value,
            int priority)
        {
            Provider = provider;
            ProviderGeneration = providerGeneration;
            Rule = rule;
            Value = value;
            Priority = priority;
        }

        /// <summary>
        /// True when this record names no real contribution. Only the provider is required: a record whose rule is
        /// default was reconstructed from a row that predates recorded rule identity (a binding row read back from
        /// an assembly published before support sets existed), and rejecting it would drop a real supporter.
        /// </summary>
        public bool IsDefault => Provider.IsDefault;

        /// <summary>Support identity of P-017: the provider installation and the rule, independent of payload.</summary>
        public bool HasSameIdentity(CapabilitySupport other) => Provider.Equals(other.Provider) && Rule.Equals(other.Rule);

        public bool Equals(CapabilitySupport other) =>
            HasSameIdentity(other)
            && ProviderGeneration == other.ProviderGeneration
            && Value == other.Value
            && Priority == other.Priority;

        public override bool Equals(object? obj) => obj is CapabilitySupport other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Provider.GetHashCode();
                hash = (hash * 397) ^ ProviderGeneration.GetHashCode();
                hash = (hash * 397) ^ Rule.GetHashCode();
                hash = (hash * 397) ^ Value;
                return (hash * 397) ^ Priority;
            }
        }

        public static bool operator ==(CapabilitySupport left, CapabilitySupport right) => left.Equals(right);

        public static bool operator !=(CapabilitySupport left, CapabilitySupport right) => !left.Equals(right);

        /// <summary>
        /// Canonical order of a support set (P-008): higher priority first, then ascending provider id and rule id.
        /// Deterministic reporting only; precedence itself was decided by the composition policy (P-018, P-019).
        /// </summary>
        public static int CompareCanonical(CapabilitySupport left, CapabilitySupport right)
        {
            int priority = right.Priority.CompareTo(left.Priority);
            if (priority != 0)
            {
                return priority;
            }

            int provider = Id128Codec.CompareBigEndian(left.Provider.Value, right.Provider.Value);
            if (provider != 0)
            {
                return provider;
            }

            return Id128Codec.CompareBigEndian(left.Rule.Value, right.Rule.Value);
        }

        /// <summary>
        /// Canonical, de-duplicated, immutable support set: sorted by <see cref="CompareCanonical"/> and de-duplicated
        /// by support identity, so one contribution appears once even if a caller listed it twice (P-017). A default
        /// record and a null source both yield the empty set.
        /// </summary>
        public static IReadOnlyList<CapabilitySupport> Freeze(IReadOnlyList<CapabilitySupport>? supports)
        {
            if (supports == null || supports.Count == 0)
            {
                return Array.Empty<CapabilitySupport>();
            }

            var copy = new List<CapabilitySupport>(supports.Count);
            for (int i = 0; i < supports.Count; i++)
            {
                CapabilitySupport support = supports[i];
                if (support.IsDefault)
                {
                    continue;
                }

                bool duplicate = false;
                for (int j = 0; j < copy.Count; j++)
                {
                    if (copy[j].HasSameIdentity(support))
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                {
                    copy.Add(support);
                }
            }

            if (copy.Count == 0)
            {
                return Array.Empty<CapabilitySupport>();
            }

            copy.Sort(CompareCanonical);

            CapabilitySupport[] frozen = copy.ToArray();
            return Array.AsReadOnly(frozen);
        }

        /// <summary>The union of two support sets, in canonical order (P-017).</summary>
        public static IReadOnlyList<CapabilitySupport> Union(
            IReadOnlyList<CapabilitySupport>? left,
            IReadOnlyList<CapabilitySupport>? right)
        {
            int leftCount = left != null ? left.Count : 0;
            int rightCount = right != null ? right.Count : 0;
            if (leftCount == 0)
            {
                return Freeze(right);
            }

            if (rightCount == 0)
            {
                return Freeze(left);
            }

            var combined = new List<CapabilitySupport>(leftCount + rightCount);
            for (int i = 0; i < leftCount; i++)
            {
                combined.Add(left![i]);
            }

            for (int i = 0; i < rightCount; i++)
            {
                combined.Add(right![i]);
            }

            return Freeze(combined);
        }

        /// <summary>True when two sets hold the same supporters with the same content (P-017).</summary>
        public static bool SetEquals(IReadOnlyList<CapabilitySupport>? left, IReadOnlyList<CapabilitySupport>? right)
        {
            int leftCount = left != null ? left.Count : 0;
            int rightCount = right != null ? right.Count : 0;
            if (leftCount != rightCount)
            {
                return false;
            }

            for (int i = 0; i < leftCount; i++)
            {
                if (!left![i].Equals(right![i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>True when one set contains the other's every supporter identity (P-033 exact retraction).</summary>
        public static bool Contains(IReadOnlyList<CapabilitySupport>? set, CapabilitySupport support)
        {
            if (set == null)
            {
                return false;
            }

            for (int i = 0; i < set.Count; i++)
            {
                if (set[i].HasSameIdentity(support))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Canonical text of one support set, for facts digests and evidence (P-008).</summary>
        public static string Describe(IReadOnlyList<CapabilitySupport>? supports)
        {
            if (supports == null || supports.Count == 0)
            {
                return "<none>";
            }

            var text = new StringBuilder();
            for (int i = 0; i < supports.Count; i++)
            {
                if (i != 0)
                {
                    text.Append('+');
                }

                text.Append(supports[i].Provider.ToString())
                    .Append(':')
                    .Append(supports[i].Rule.ToString())
                    .Append('=')
                    .Append(supports[i].Value.ToString(CultureInfo.InvariantCulture));
            }

            return text.ToString();
        }

        public override string ToString() =>
            Provider.ToString() + "/" + Rule.ToString() + "=" + Value.ToString(CultureInfo.InvariantCulture);
    }
}

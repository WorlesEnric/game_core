// GameCore.Derivation tests — shared deterministic support (GC-006).
//
// No helper here reads a clock, a static counter or a system random source: seeded LCG streams and stable names
// only, so a failing seed can be replayed exactly (P-008, TEST-007 "keep failed seeds").
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Derivation.Fixtures;
using NUnit.Framework;

namespace GameCore.Derivation.Tests
{
    /// <summary>A tiny explicit LCG: the same seed produces the same stream on every host and runtime.</summary>
    public sealed class Lcg
    {
        private uint state;

        public Lcg(uint seed)
        {
            state = seed == 0U ? 0x9E3779B9U : seed;
        }

        public uint NextUInt()
        {
            state = unchecked((state * 1664525U) + 1013904223U);
            return state;
        }

        public int Next(int bound)
        {
            if (bound <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bound));
            }

            return (int)(NextUInt() % (uint)bound);
        }

        public bool NextBool() => (NextUInt() & 0x80000000U) != 0U;

        /// <summary>Inclusive range; used to pick priorities, depths and small counts.</summary>
        public int NextInclusive(int min, int max) => min + Next(max - min + 1);
    }

    /// <summary>Assertions shared by the derivation suites.</summary>
    public static class DerivationAssert
    {
        /// <summary>Asserts an accepted result and returns it, so a failure names the rejection instead of a null.</summary>
        public static DerivationResult Accepted(DerivationResult result)
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(
                result.Accepted,
                Is.True,
                "Expected an accepted derivation but got " + result.Rejection.ToString()
                + " with code " + result.DiagnosticCode.ToString()
                + FirstProblem(result));
            return result;
        }

        /// <summary>Asserts a rejection with a specific kind, and that no partial closure came with it.</summary>
        public static DerivationResult Rejected(DerivationResult result, DerivationRejectionKind kind)
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Accepted, Is.False, "Expected a rejection, but the derivation was accepted.");
            Assert.That(result.Rejection, Is.EqualTo(kind), "Rejection kind. " + FirstProblem(result));
            Assert.That(result.Assemblies.Count, Is.EqualTo(0), "A rejected proposal must not carry a partial assembly (P-022, P-028).");
            Assert.That(result.Contributions.Count, Is.EqualTo(0), "A rejected proposal must not carry partial contributions.");
            return result;
        }

        /// <summary>The supported contribution of one target/capability, or a failing assertion.</summary>
        public static EffectiveSlot SlotOf(DerivationResult result, TargetId target, string capability)
        {
            TargetAssembly? assembly = result.AssemblyOf(target);
            Assert.That(assembly, Is.Not.Null, "No assembly for " + target.ToString());
            IReadOnlyList<EffectiveSlot> slots = assembly!.Slots;
            CapabilityId id = FixtureIds.Capability(capability);
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].Capability.Equals(id))
                {
                    return slots[i];
                }
            }

            Assert.Fail("Target " + target.ToString() + " has no effective slot for " + capability + ".");
            return null!;
        }

        /// <summary>Reads one identity value out of a multi-valued (set-union or ordered) slot.</summary>
        public static Id128 IdValueAt(EffectiveSlot slot, int index)
        {
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            Assert.That(index, Is.LessThan(slot.Values.Count), "The slot has fewer values than the requested index.");
            Id128 value;
            Assert.That(FixturePayload.TryReadId128(slot.Values[index], out value), Is.True, "The value is not a 16-byte identity.");
            return value;
        }

        /// <summary>True when the target has an effective slot for that capability.</summary>
        public static bool HasCapability(DerivationResult result, TargetId target, string capability)
        {
            TargetAssembly? assembly = result.AssemblyOf(target);
            return assembly != null && assembly.HasCapability(FixtureIds.Capability(capability));
        }

        /// <summary>Reads the Int32 value of a single-valued slot.</summary>
        public static int Int32Value(EffectiveSlot slot)
        {
            Assert.That(slot.Values.Count, Is.EqualTo(1), "Expected a single reduced value.");
            int value;
            Assert.That(FixturePayload.TryReadInt32(slot.Values[0], out value), Is.True, "The value is not a 4-byte integer.");
            return value;
        }

        /// <summary>Reads the identity value of a single-valued slot.</summary>
        public static Id128 IdValue(EffectiveSlot slot)
        {
            Assert.That(slot.Values.Count, Is.EqualTo(1), "Expected a single composed value.");
            Id128 value;
            Assert.That(FixturePayload.TryReadId128(slot.Values[0], out value), Is.True, "The value is not a 16-byte identity.");
            return value;
        }

        private static string FirstProblem(DerivationResult result)
        {
            if (result.ValidationProblems.Count > 0)
            {
                return " First problem: " + result.ValidationProblems[0].ToString();
            }

            if (result.CompositionFailures.Count > 0)
            {
                return " First failure: " + result.CompositionFailures[0].ToString();
            }

            if (!result.Counters.WithinBudget)
            {
                return " Budget: " + DerivationProjection.CounterText(result);
            }

            return string.Empty;
        }
    }
}

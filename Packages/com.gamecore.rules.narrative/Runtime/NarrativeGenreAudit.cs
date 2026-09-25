// GameCore.Rules.Narrative — genre neutrality audit (P-001, P-059, TEST-021).
//
// P-001 forbids the kernel from requiring an actor, action, turn, combat, physics, animation, resource or reward
// schema, and TEST-021 asks each reference composition to prove that it registers no compulsory combat/actor/
// action/vitality/physics/animation state or stage. A narrative slice cannot prove that by absence of code alone:
// it must show that every name it registers — scope, recipe, capability, schema, owner, slot, stage, system, buffer
// and component — is narrative vocabulary and never genre vocabulary borrowed from another template.
//
// The audit is a pure string rule so it runs identically in a plain test, in the Editor and in the player, and its
// outcome is part of the canonical trace.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameCore.Rules.Narrative
{
    /// <summary>What one neutrality audit examined and what it found.</summary>
    public sealed class GenreAuditReport
    {
        private GenreAuditReport(int checkedCount, IReadOnlyList<string> forbiddenNames)
        {
            CheckedCount = checkedCount;
            ForbiddenNames = forbiddenNames;
        }

        /// <summary>How many registered names were examined.</summary>
        public int CheckedCount { get; }

        /// <summary>Registered names that carry a forbidden genre token, in the order they were examined.</summary>
        public IReadOnlyList<string> ForbiddenNames { get; }

        /// <summary>True when no registered name carries a forbidden genre token.</summary>
        public bool Neutral => ForbiddenNames.Count == 0;

        internal static GenreAuditReport Of(int checkedCount, List<string> forbiddenNames)
            => new GenreAuditReport(checkedCount, forbiddenNames);

        public string Describe()
            => "checked=" + CheckedCount.ToString(CultureInfo.InvariantCulture)
                + ", neutral=" + Neutral
                + ", forbidden=" + ForbiddenNames.Count.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The forbidden genre vocabulary and the audit over one composition's registered names. Tokens are matched
    /// case-insensitively against the whole name, so `narrative.gate-condition-binding` is neutral while
    /// `gamecore.actor-state` is not.
    /// </summary>
    public static class NarrativeGenreAudit
    {
        /// <summary>
        /// Genre tokens no narrative or card registration may carry. The list is the vocabulary P-001 and TEST-021
        /// name for the action family, plus the values that would smuggle a second game family into this one.
        /// </summary>
        public static IReadOnlyList<string> ForbiddenTokens { get; } = new[]
        {
            "actor",
            "vitality",
            "health",
            "hitpoint",
            "damage",
            "attack",
            "weapon",
            "combat",
            "physics",
            "rigidbody",
            "collision",
            "acceleration",
            "velocity",
            "animation",
            "animator",
            "turnstate",
            "turnsystem",
        };

        /// <summary>True when one registered name contains none of the forbidden tokens.</summary>
        public static bool IsNeutral(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            for (int i = 0; i < ForbiddenTokens.Count; i++)
            {
                if (name.IndexOf(ForbiddenTokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Audits every registered name of one composition. A null or empty list is refused rather than reported as
        /// neutral: an audit over nothing is not evidence (P-060).
        /// </summary>
        public static GenreAuditReport Audit(IReadOnlyList<string>? registeredNames)
        {
            if (registeredNames == null)
            {
                throw new ArgumentNullException(nameof(registeredNames));
            }

            if (registeredNames.Count == 0)
            {
                throw new ArgumentException(
                    "a neutrality audit needs the registered names of the composition it examines (P-060).",
                    nameof(registeredNames));
            }

            var forbidden = new List<string>();
            for (int i = 0; i < registeredNames.Count; i++)
            {
                if (!IsNeutral(registeredNames[i]))
                {
                    forbidden.Add(registeredNames[i]);
                }
            }

            return GenreAuditReport.Of(registeredNames.Count, forbidden);
        }

        /// <summary>Every name that failed the audit, or a marker when the composition is neutral.</summary>
        public static string Describe(GenreAuditReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            if (report.Neutral)
            {
                return report.Describe();
            }

            return report.Describe() + ": " + string.Join(", ", ToArray(report.ForbiddenNames));
        }

        private static string[] ToArray(IReadOnlyList<string> values)
        {
            var array = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                array[i] = values[i];
            }

            return array;
        }
    }
}

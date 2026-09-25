// GameCore.Validation.ProbeHost — the GC-019 integration-gate family contract.
//
// The gate sentence this contract serves, from `docs/game-core/09-implementation-guide.md` (Wave 5, GC-019):
//
//   "Connect host resources and views while maintaining one authority per state domain." — and, from the same
//   task's acceptance: "Late asset/input completions cannot write retired worlds; view destruction leaves gameplay
//   state intact; reparenting a Transform does not move composition."
//
// One runner, two family adapters, exactly as GC-013's and the Wave 4 gate's contracts have one runner and two
// adapters. Everything a family needs in order to be an ordinary GC-013 (or Wave 4) family — its catalog, lane seed,
// live targets, provider mount and the installation that mount creates — already comes from `IGc013Family`, so the
// GC-019 gate integrates the adapters with the *same* worlds the earlier gates run instead of building a third
// parallel implementation of a genre.
//
// This file therefore adds only the surface the adapters need from a genre:
//
//   * the family's *own* command identity (route, target, payload schema) and the payload it really sends over it, so
//     the input step submits a typed command a genre declares rather than one the adapter invented (P-042, 04 s7);
//   * the committed targets that must receive a view, so the presentation step presents exactly the targets the
//     family's own provider derived into (P-045);
//   * nothing else: the observation-name table, the executor and the step order stay in `Gc019Scenario`, exactly the
//     way `W4GateScenario` owns its own table.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Rules.Narrative;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>One named GC-019 observation: what was checked and the values it was computed from.</summary>
    public sealed class Gc019Step
    {
        public Gc019Step(string name, bool passed, string detail)
        {
            Name = name;
            Passed = passed;
            Detail = detail ?? string.Empty;
        }

        public string Name { get; }

        public bool Passed { get; }

        public string Detail { get; }

        public override string ToString() => Name + ": " + (Passed ? "Pass" : "Fail") + " (" + Detail + ")";
    }

    /// <summary>
    /// Full result of one GC-019 run: the named observations plus one digest over them, computed over the canonical
    /// `name=pass|fail` lines with the same digest function the narrative trace, the GC-013 result and the Wave 4 gate
    /// result use. A run that records a different set of observations (or a failing one) therefore cannot report the
    /// digest the observation table implies.
    /// </summary>
    public sealed class Gc019ScenarioResult
    {
        public Gc019ScenarioResult(string label, IReadOnlyList<Gc019Step> steps)
        {
            Label = label;
            Steps = steps;
            var lines = new List<string>(steps.Count);
            bool allPassed = steps.Count > 0;
            for (int i = 0; i < steps.Count; i++)
            {
                lines.Add(steps[i].Name + "=" + (steps[i].Passed ? "pass" : "fail"));
                allPassed &= steps[i].Passed;
            }

            AllPassed = allPassed;
            Digest = NarrativeDigest.OfLines(lines);
        }

        /// <summary>The family label every observation name of this run is qualified with.</summary>
        public string Label { get; }

        /// <summary>The named observations, in execution order, qualified with <see cref="Label"/>.</summary>
        public IReadOnlyList<Gc019Step> Steps { get; }

        /// <summary>Canonical digest over this run's observation names and pass flags.</summary>
        public string Digest { get; }

        public bool AllPassed { get; }

        public string Describe()
        {
            var failed = new List<string>();
            for (int i = 0; i < Steps.Count; i++)
            {
                if (!Steps[i].Passed)
                {
                    failed.Add(Steps[i].ToString());
                }
            }

            return "digest=" + Digest
                + "; label=" + Label
                + "; steps=" + Steps.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "; failed=" + failed.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + (failed.Count == 0 ? string.Empty : ": " + string.Join(" | ", failed.ToArray()));
        }
    }

    /// <summary>
    /// One genre's declared facts for the GC-019 adapter gate: everything <see cref="IGc013Family"/> declares, plus
    /// the one command identity, the one command payload and the committed view targets the adapters need.
    /// </summary>
    public interface IGc019Family : IGc013Family
    {
        /// <summary>The route the family's own typed command is admitted through (P-042).</summary>
        RouteId CommandRoute { get; }

        /// <summary>The target the family's command addresses: the live entity whose owner answers it.</summary>
        TargetId CommandTarget { get; }

        /// <summary>The payload schema of that route, which is also the schema registered for it (P-042).</summary>
        SchemaRef CommandSchema { get; }

        /// <summary>
        /// The family's own declared payload, encoded exactly the way its own compile unit and its own scenarios
        /// encode it: the narrative family's choice (one dialogue node ordinal and one choice ordinal) and the card
        /// family's ordinary command (kind, seat, counterparty, expected table version, three candidates). The
        /// adapter never invents a payload: it carries what the genre declares.
        /// </summary>
        /// <param name="value">
        /// One declared scalar of that payload, so the runner can submit payloads a genre's own rules accept without
        /// knowing the genre: the narrative family's choice ordinal (its rules declare `PermitChoice` = 1 and
        /// `DeclineChoice` = 2) and the card family's expected table version (its fixture seeds the table at
        /// `SeededTableVersion` = 1).
        /// </param>
        FrozenPayload CommandPayload(int value);

        /// <summary>
        /// Live targets that must receive a view: the committed targets the family's own mounted provider derived
        /// into, so a presented value always has a committed binding row to be equal to (P-045). Each of these must
        /// be a live target of the seeded world with at least one row in the published assembly.
        /// </summary>
        IReadOnlyList<TargetId> ViewTargets { get; }
    }
}

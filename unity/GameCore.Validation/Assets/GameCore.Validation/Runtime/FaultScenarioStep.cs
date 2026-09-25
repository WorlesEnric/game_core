// GameCore.Validation.ProbeHost — the GC-017 fault-scenario step result.
//
// TEST-016 rows this file serves, from `docs/game-core/08-validation-and-performance.md` (the fault matrix every row
// of which needs a deterministic injection point rather than a probability):
//
//   row 1  validation/enumeration refusal            `FaultBoundary.Validation`
//   row 2  resource acquisition or plan preparation  `FaultBoundary.Acquisition`
//   row 4  the publication fence                     `FaultBoundary.Fence`
//   row 5  migration before the first live write     `FaultBoundary.Migration`, `FaultBoundary.FirstLiveWrite`,
//                                                    `FaultBoundary.GateInstallation`
//   row 6  structural playback after a step's systems `FaultBoundary.StructuralPlayback`
//   row 8  cleanup of a refused operation's work      `FaultBoundary.Cleanup`
//   row 10 checkpoint/reference repair failure        `InitialDefinitionRecovery`
//
// Normative anchors: P-002 (the host is the sole authority for world lifecycle and creation), P-029 (a preparation
// or migration failure releases staged leases in reverse order and leaves the old assembly intact), P-030
// (publication switches the assembly at one serialized commit), P-031 (a failure after the first live write faults
// the world: admission stays closed, no epoch or image publishes, no simulation resumes), P-035 (a created world
// becomes `Running` only after its initial validated assembly publication), P-047/P-048 (in-flight work is fenced and
// resources unfinished work may still reach are quarantined, never freed on a timeout), P-049 (recovery creates a
// new `WorldId` from verified initial definitions; old callbacks and handles never become valid), P-051 (a
// cancellation/publication race is resolved by the serialized control lane at the stated cutoff), P-052 (a failure
// names its phase and its operation).
//
// Why the shape is what it is: the observation vocabulary of every other scenario in this project (`Gc013Step`,
// `W4GateStep`) is a *name plus a verdict plus the values the verdict was computed from*. A fault run needs the same
// thing for the same reason — an evidence file must show what was asserted and the numbers it was asserted against,
// because "the fault was injected" is not observable on its own. The step therefore carries its detail as text and
// never as a boolean a caller has to trust, and `FaultScenarioResult.Digest` is computed over the canonical
// `name=pass` lines so a renamed, reordered, added or dropped observation cannot report the expected literal.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Rules.Narrative;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>One named GC-017 observation: what was checked and the values it was computed from.</summary>
    public sealed class FaultScenarioStep
    {
        public FaultScenarioStep(string name, bool passed, string detail)
        {
            Name = name;
            Passed = passed;
            Detail = detail ?? string.Empty;
        }

        /// <summary>Qualified observation name, `<c>&lt;family label&gt;/&lt;frozen name&gt;</c>`.</summary>
        public string Name { get; }

        public bool Passed { get; }

        /// <summary>The values this observation was computed from, as `key=value` tokens.</summary>
        public string Detail { get; }

        public override string ToString() => Name + ": " + (Passed ? "Pass" : "Fail") + " (" + Detail + ")";
    }

    /// <summary>
    /// Full result of one GC-017 run: the named observations plus one digest over them. The digest is computed over
    /// the canonical `name=pass|fail` lines with the same digest function the narrative trace uses, so the standalone
    /// probe can archive one stable literal per family and a run that records a different set of observations (or a
    /// failing one) cannot report the expected digest (P-008, P-028).
    /// </summary>
    public sealed class FaultScenarioResult
    {
        public FaultScenarioResult(string label, IReadOnlyList<FaultScenarioStep> steps)
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
        public IReadOnlyList<FaultScenarioStep> Steps { get; }

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
}

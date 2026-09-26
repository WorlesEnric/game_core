// GameCore.Validation.ProbeHost - the Wave 5 integration-gate family contract.
//
// The gate sentence this contract serves, verbatim from `docs/game-core/09-implementation-guide.md` (Wave 5):
//
//   "Join retained observation, deterministic faults, checkpoint restore and common adapters in one actual world.
//    Show prewrite rejection, postwrite fail-stop, new-session restore, read-only snapshots and stale asset callback
//    rejection."
//
// One runner, two family adapters - the shape every earlier gate in this repository uses. The important difference
// from GC-016..GC-019 is that this gate owns *no* kernel module: it is the join. Everything it drives comes from the
// four finished tasks and the frozen family contracts they left behind:
//
//   * `IGc018Family` (checkpoint) supplies the genre's declared world - its catalog, scope tree, live targets,
//     provider mount, dormant slot, persistent clock, queued command and boundary enrichment. The gate builds one
//     real world of that genre exactly as `Gc018Scenario` does, and restores into a new one through the same
//     `Gc018FamilyRestoreBuilder`.
//   * `IGc019Family` (adapters) supplies the genre's own typed command identity and payload and the committed view
//     targets, so the adapters are bound to a world whose commands and targets the genre really declares.
//   * `GC-016`'s observation storage and `GC-017`'s fault latch are reached through the world itself
//     (`UnityWorldHost.Observation` / `UnityWorldHost.Faults`), never through a fixture.
//
// `IW5GateFamily` therefore adds no member: it is the two contracts one genre must satisfy at once, named so the
// runner can require both from one object and so a family that satisfies only one half fails to compile rather than
// at run time (P-001, P-002).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Rules.Narrative;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>One named Wave 5 gate observation: what was checked and the values it was computed from.</summary>
    public sealed class W5GateStep
    {
        public W5GateStep(string name, bool passed, string detail)
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
    /// Full result of one Wave 5 gate run: the named observations plus one digest over them, computed over the
    /// canonical `name=pass|fail` lines with the same digest function the narrative trace, the GC-013 result, the
    /// Wave 4 gate result, the GC-017 fault scenario, the GC-018 checkpoint round trip and the GC-019 adapter gate
    /// use. A run that records a different set of observations (or a failing one) therefore cannot report the digest
    /// the observation table implies (P-008).
    /// </summary>
    public sealed class W5GateScenarioResult
    {
        public W5GateScenarioResult(string label, IReadOnlyList<W5GateStep> steps)
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
        public IReadOnlyList<W5GateStep> Steps { get; }

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
                + "; steps=" + Steps.Count.ToString(CultureInfo.InvariantCulture)
                + "; failed=" + failed.Count.ToString(CultureInfo.InvariantCulture)
                + (failed.Count == 0 ? string.Empty : ": " + string.Join(" | ", failed.ToArray()));
        }
    }

    /// <summary>
    /// One genre's declared facts for the Wave 5 gate: everything the checkpoint contract declares (its command, its
    /// dormant slot, its persistent clock, its composition enrichment) and everything the adapter contract declares
    /// (its typed command identity and payload, its committed view targets, its stage runtime). No member is added,
    /// because the gate joins the four finished tasks rather than introducing a fifth surface (P-001).
    /// </summary>
    public interface IW5GateFamily : IGc018Family, IGc019Family
    {
    }
}

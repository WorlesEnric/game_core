// GameCore.Validation.ProbeHost — the Wave 6 integration-gate family contract.
//
// The gate sentence this contract serves, verbatim from `docs/game-core/09-implementation-guide.md` (Wave 6):
//
//   "Integrate the fixed-step action reference, durable reward delivery, unload stress and replay/cost counters.
//    Demonstrate that optional physics/animation are absent from cards/narrative. Complete 1,000-cycle teardown and
//    repeatability fixtures before broader qualification."
//
// One runner, THREE family adapters — the shape every earlier gate in this repository uses (one runner, N genres),
// here with the third genre GC-020 added. The gate owns *no* kernel module: it is the join of GC-020 (fixed-step
// traversal plus the optional engine stages), GC-021 (durable outbox and destination idempotency), GC-022 (unload
// stress, callback and lifecycle proof) and GC-023 (replay, differential propagation and cost counters).
//
// WHY THIS CONTRACT IS NOT `IGc019Family`. `IGc020Family.AttachStageRuntime(...)` and
// `IGc019Family.AttachStageRuntime(...)` have identical parameter lists and different return types, so one C# class
// can implement at most one of them (two such members would be a duplicate member, not an overload). The traversal
// course therefore satisfies `IGc020Family` and this gate's contract, while the narrative and card slices satisfy
// `IGc019Family` and this gate's contract. `W6StageRuntime` is the small union the gate needs from either shape: the
// genre's own stage runtime, attached exactly where the genre attaches it.
//
// THE LOOP THE THIRD OF THE GATE RUNS. `IW6Family` adds what a create/mount/step/unmount/teardown cycle needs beyond
// `IGc013Family`: the manifests the loop may mount, the per-cycle plugin instance identity, the genre's own mount and
// unmount payloads, the one typed command payload a cycle admits, and the stage runtime to attach. Every one of those
// is data or a payload the genre declares; the runner owns the cycles, the counters and the observations, so all
// three genres are driven through exactly the same loop (P-001, P-002, P-046).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Composition;
using GameCore.Rules.Narrative;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>One named Wave 6 gate observation: what was checked and the values it was computed from.</summary>
    public sealed class W6GateStep
    {
        public W6GateStep(string name, bool passed, string detail)
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
    /// Full result of one Wave 6 gate run: the named observations plus one digest over them, computed over the
    /// canonical `name=pass|fail` lines with the same digest function the narrative trace, the GC-013 result, the
    /// Wave 4 and Wave 5 gates, the GC-017 fault scenario, the GC-018 checkpoint round trip, the GC-019 adapter gate,
    /// the GC-020 course, the GC-021 delivery seat, GC-022's stress and GC-023's replay all use. A run that records a
    /// different set of observations (or a failing one) therefore cannot report the digest the observation table
    /// implies (P-008, TEST-022).
    /// </summary>
    public sealed class W6GateScenarioResult
    {
        public W6GateScenarioResult(string label, IReadOnlyList<W6GateStep> steps)
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
        public IReadOnlyList<W6GateStep> Steps { get; }

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

        public override string ToString() => Describe();
    }

    /// <summary>
    /// The genre's own stage runtime as this gate needs it: the narrative or card slice's adapter runtime
    /// (<see cref="Gc019StageRuntime"/>) or the traversal course's fixed-step runtime (<see cref="Gc020StageRuntime"/>).
    /// Exactly one of the two is non-null, and it is the very object the genre's own scenario attaches — the gate
    /// never builds a second one (P-002, P-043).
    /// </summary>
    public sealed class W6StageRuntime : IDisposable
    {
        public W6StageRuntime(string kind, Gc019StageRuntime? adapters, Gc020StageRuntime? traversal)
        {
            Kind = kind ?? string.Empty;
            Adapters = adapters;
            Traversal = traversal;
        }

        /// <summary>The genre label this runtime belongs to.</summary>
        public string Kind { get; }

        /// <summary>The narrative or card slice's adapter runtime, when this runtime is that genre's.</summary>
        public Gc019StageRuntime? Adapters { get; }

        /// <summary>The traversal course's fixed-step runtime, when this runtime is that genre's.</summary>
        public Gc020StageRuntime? Traversal { get; }

        public void Dispose()
        {
            Adapters?.Dispose();
            Traversal?.Dispose();
        }

        public override string ToString() => "w6StageRuntime(" + Kind
            + ",adapters=" + (Adapters != null) + ",traversal=" + (Traversal != null) + ")";
    }

    /// <summary>
    /// One genre's declared facts for the Wave 6 gate: everything <see cref="IGc013Family"/> declares (so the gate
    /// builds a real world of that genre exactly as the earlier gates do), plus what the three-family
    /// create/mount/step/unmount/teardown loop and the composition audit need from it.
    ///
    /// Every member is data, a payload builder or the genre's own runtime attachment. The runner owns the cycle count,
    /// the counters, the telemetry samples and the observations, so the three genres are driven through one loop
    /// (P-001).
    /// </summary>
    public interface IW6Family : IGc013Family
    {
        /// <summary>
        /// The manifests, dispatch identities and payload builders one cycle uses beyond the genre's catalog
        /// declarations: the installation the loop mounts and unmounts with a fresh identity per cycle. The four
        /// roles are the same four GC-022's stress declares, so a genre that satisfies both contracts answers with
        /// the same object rather than a second declaration set (P-009).
        /// </summary>
        LifecycleStressDeclarations StressDeclarations { get; }

        /// <summary>
        /// One fresh plugin-instance identity of this genre, from a small ordinal. A cycle never reuses another
        /// cycle's identity (P-004, P-005).
        /// </summary>
        PluginInstanceId StressInstance(ulong ordinal);

        /// <summary>The genre's own O-03 mount payload for one manifest at one scope (P-020).</summary>
        CompositionEditPayload StressMount(PluginManifest manifest, PluginInstanceId instance, ScopeId scope);

        /// <summary>The genre's own O-07 unmount payload for one installation (P-046, P-048).</summary>
        CompositionEditPayload StressUnmount(PluginInstanceId instance);

        /// <summary>The route the one command a cycle admits travels (P-042).</summary>
        RouteId CycleRoute { get; }

        /// <summary>The target that command addresses: a live target of the seeded world.</summary>
        TargetId CycleTarget { get; }

        /// <summary>The payload schema of that route, as the genre's own compile unit declares it.</summary>
        SchemaRef CycleSchema { get; }

        /// <summary>
        /// The genre's own declared payload for one cycle's step, encoded exactly the way the genre's own scenarios
        /// encode it, so the loop never invents a payload the genre's readers would refuse (P-042, 05 s6).
        /// </summary>
        FrozenPayload CyclePayload(ulong ordinal);

        /// <summary>
        /// Host-clock ticks one cycle's pump advances. A fixed-step genre advances exactly its declared step; a
        /// command-driven genre advances enough host time that the admitted command commits exactly one logical step
        /// (P-036).
        /// </summary>
        ulong CyclePumpTicks { get; }

        /// <summary>
        /// Attaches the genre's own stage runtime to its just-seeded world, exactly where the genre's own scenario
        /// attaches it (after seeding, before the lane's first publication). The runner disposes it in its teardown
        /// (P-043).
        /// </summary>
        W6StageRuntime AttachGateRuntime(
            UnityWorldHost host,
            PipelineDescriptorReport descriptor,
            LiveTargetIndex targets,
            LiveTargetSeeder seeder);
    }
}

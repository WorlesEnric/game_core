// GameCore.Validation.ProbeHost — the `-probeTraversal` player mode (GC-020).
//
// The GC-020 gate's runner (`Gc020Scenario`) is shared by the Unity EditMode assembly `GameCore.Gc020.Tests` and by
// this mode, so the same observations execute in the Editor and in a stripped headless IL2CPP player. The mode runs
// the fixture catalog's course world: there is no committed generated traversal catalog on this revision (the
// content-compiler emission of one is GC-025's catalog-coverage work), and the gate's header records that decision
// rather than pretending a second catalog ran.
//
// The headless player runs with Unity audio DISABLED (an FMOD/PulseAudio crash at exit was the reason, crash-139), so
// the gate's audio observation uses the engine-free recording sink and never constructs an `AudioSource`. The physics
// observation does use the real local `PhysicsScene`, because a local scene needs no renderer and no audio device.
#nullable enable
using System;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>The `-probeTraversal` mode: the real-time action reference, in the built player.</summary>
    public static class ProbeTraversal
    {
        /// <summary>
        /// Runs the traversal course's observations and adds one probe step per observation, plus the digest step
        /// whose detail carries the literal this mode and the EditMode suite both freeze.
        /// </summary>
        public static void Run(ProbeReport report)
        {
            try
            {
                Gc020TraversalHost.RunBoth(out Gc020ScenarioResult result, out Gc020ScenarioResult _);

                for (int i = 0; i < result.Steps.Count; i++)
                {
                    Gc020Step step = result.Steps[i];
                    report.Add(
                        step.Passed
                            ? ProbeOutcome.Pass(step.Name, step.Detail)
                            : ProbeOutcome.Fail(step.Name, step.Detail));
                }

                report.Add(
                    result.AllPassed
                        ? ProbeOutcome.Pass(
                            "gc020-" + result.Label + "-digest",
                            "digest=" + result.Digest
                            + "; expectedDigest=" + ExpectedDigest
                            + "; observations=" + result.Steps.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        : ProbeOutcome.Fail(
                            "gc020-" + result.Label + "-digest",
                            result.Describe()));
            }
            catch (Exception exception)
            {
                report.Add(
                    ProbeOutcome.Fail(
                        "gc020-scenario",
                        "unhandled " + exception.GetType().FullName + ": " + exception.Message));
            }
        }

        /// <summary>
        /// The digest of the course's observation table computed from its NAMES alone, so a renamed, reordered or
        /// dropped observation changes this literal instead of silently shrinking the gate (TEST-022).
        /// </summary>
        public static string ExpectedDigest => "PLACEHOLDER";

        /// <summary>The traversal course's label every observation name of this run is qualified with.</summary>
        public static string Label => Gc020TraversalHost.Label;
    }
}

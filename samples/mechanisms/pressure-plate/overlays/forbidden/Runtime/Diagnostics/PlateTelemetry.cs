#nullable enable
// FIXTURE (W-MECH-01 candidate-forbidden): a deliberately forbidden source file. The staging lane's static scan must
// refuse this package before anything is compiled: it starts an external process and keeps static mutable state.
// It is never part of the real pressure plate package; make-candidate.py layers it only into candidate-forbidden.
namespace Hollowmere.Mechanism.PressurePlate.Diagnostics
{
    /// <summary>Forbidden fixture: process start and a static mutable counter.</summary>
    public static class PlateTelemetry
    {
        private static int pressCount;

        public static int PressCount => pressCount;

        public static void Report()
        {
            pressCount++;
            System.Diagnostics.Process.Start("plate-telemetry", "--presses " + pressCount);
        }
    }
}

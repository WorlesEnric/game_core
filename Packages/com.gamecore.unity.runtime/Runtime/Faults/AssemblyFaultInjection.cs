// GameCore.Unity.Runtime — the fault latch of one owned world (GC-017).
//
// Normative sources: 00 P-029 (a preparation failure releases staged work and leaves the old assembly intact),
// P-030 (publication is one serialized commit), P-031 (a failure after the first live write faults the world and
// never resumes), P-047/P-048 (in-flight work is fenced and unreachable ownership is quarantined, never freed on a
// timeout) and P-052 (a failure names its phase, its operation and its provenance), with the boundary matrix of
// 08 TEST-016.
//
// This is the whole latch. It used to be declared inside `AssemblyPublisher.cs`; it moved here so that every latch
// type, every boundary name and every reach helper sit in one Unity-free folder that
// `dotnet/src/GameCore.Faults.ReleaseCheck` can compile both with and without the qualification symbol — which is
// how `tools/check_release_fault_free.py` shows that a shipping build of these sources contains no type, no boundary
// name and no allocation. The type kept its public name and its `GameCore.Unity.Runtime` namespace, so no caller
// changed.
namespace GameCore.Unity.Runtime
{
#if GAMECORE_FAULT_INJECTION
#nullable enable
using System;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Unity.Runtime.Faults;

    /// <summary>
    /// Injected faults for the fault-boundary tests; a production pipeline never arms these (TEST-016, GC-017).
    ///
    /// Two shapes live here and both are needed. <see cref="FailDuringMigration"/> and
    /// <see cref="FailAfterFirstLiveWrite"/> are the original GC-008 switches that throw directly out of the
    /// migration stage and out of the apply stage. <see cref="Arm"/> and <see cref="TryReach"/> are the GC-017
    /// enumerated latch: every TEST-016 boundary is named by <see cref="FaultBoundary"/>, armed by identity,
    /// reached at exactly one place in the real code path, and recorded — with its operation and plan identity —
    /// in <see cref="Trace"/>, so an evidence file shows the sequence instead of a boolean.
    ///
    /// Nothing arms a boundary in production, and a compilation that does not define
    /// <see cref="FaultCompilation.Symbol"/> carries no reaching implementation at all
    /// (<see cref="IsCompiledIn"/> reports which build this is).
    /// </summary>
    public sealed class AssemblyFaultInjection
    {
        private readonly bool[] armed = new bool[FaultBoundaryText.Count];
        private readonly int[] reachCounts = new int[FaultBoundaryText.Count];

        /// <summary>Throws inside the prewrite migration stage, i.e. before any live write (P-029).</summary>
        public bool FailDuringMigration { get; set; }

        /// <summary>Throws after the apply stage has already written at least one live row (P-031).</summary>
        public bool FailAfterFirstLiveWrite { get; set; }

        /// <summary>Times the prewrite injection fired; a value, so a test can assert it fired exactly once.</summary>
        public int MigrationInjections { get; private set; }

        public int PostWriteInjections { get; private set; }

        /// <summary>True when this compilation carries the GC-017 latch implementation at all.</summary>
        public bool IsCompiledIn => FaultCompilation.IsCompiledIn;

        /// <summary>Ordered trace of every boundary this world reached, injected or not (GC-017 evidence).</summary>
        public FaultTrace Trace { get; } = new FaultTrace();

        /// <summary>Boundary reaches attempted; every one is recorded in <see cref="Trace"/>.</summary>
        public int ReachCount { get; private set; }

        /// <summary>Faults actually raised; equals <see cref="FaultTrace.InjectedCount"/>.</summary>
        public int InjectedCount { get; private set; }

        /// <summary>Armed boundaries, so a test can assert a boundary was armed and then disarmed.</summary>
        public int ArmedBoundaryCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < armed.Length; i++)
                {
                    if (armed[i])
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>Raises the prewrite injection if armed; called by the publisher inside the migration stage.</summary>
        public void MaybeFailDuringMigration()
        {
            if (!FailDuringMigration)
            {
                return;
            }

            MigrationInjections++;
            throw new InvalidOperationException(
                "injected prewrite failure: the old assembly must stay intact and keep running (P-029)");
        }

        /// <summary>Raises the postwrite injection if armed; called after the first live write (P-031).</summary>
        public void MaybeFailAfterFirstLiveWrite()
        {
            if (!FailAfterFirstLiveWrite)
            {
                return;
            }

            PostWriteInjections++;
            throw new InvalidOperationException(
                "injected postwrite failure: the world must fault without publishing or resuming (P-031)");
        }

        /// <summary>Arms one boundary; chained so a test arms several in one expression.</summary>
        public AssemblyFaultInjection Arm(FaultBoundary boundary)
        {
            armed[Index(boundary)] = true;
            return this;
        }

        public AssemblyFaultInjection Disarm(FaultBoundary boundary)
        {
            armed[Index(boundary)] = false;
            return this;
        }

        /// <summary>Disarms every boundary; a test calls this before its next case so latches never leak.</summary>
        public AssemblyFaultInjection DisarmAll()
        {
            for (int i = 0; i < armed.Length; i++)
            {
                armed[i] = false;
            }

            return this;
        }

        public bool IsArmed(FaultBoundary boundary) => armed[Index(boundary)];

        /// <summary>Times one boundary was reached, whether or not it was armed.</summary>
        public int ReachCountOf(FaultBoundary boundary) => reachCounts[Index(boundary)];

        /// <summary>
        /// Reaches one boundary: records the reach with its operation and plan provenance, and raises
        /// <see cref="FaultInjectedException"/> when that boundary is armed (P-052: a failure names its phase and
        /// its operation). Returns false — having only recorded the reach — otherwise.
        /// </summary>
        public bool TryReach(FaultBoundary boundary, OperationId operation, ContentHash planHash, string detail)
        {
            int index = Index(boundary);
            ReachCount++;
            reachCounts[index]++;
            bool injected = armed[index];
            string text = detail ?? string.Empty;
            Trace.Add(boundary, operation, planHash, injected, text);
            if (!injected)
            {
                return false;
            }

            InjectedCount++;
            throw new FaultInjectedException(
                boundary,
                "injected " + FaultBoundaryText.Of(boundary) + " fault: " + text);
        }

        /// <summary>
        /// Reaches one boundary whose failure is a refusal value rather than an exception — the staged-resource
        /// gate's contract reports <c>ResourceUnavailable</c> instead of throwing (P-029). Records the reach with
        /// the same trace as <see cref="TryReach"/> and returns true when the armed boundary refused the call.
        /// </summary>
        public bool TryRefuse(FaultBoundary boundary, string detail)
        {
            int index = Index(boundary);
            ReachCount++;
            reachCounts[index]++;
            if (!armed[index])
            {
                return false;
            }

            InjectedCount++;
            Trace.Add(
                boundary,
                default(OperationId),
                ContentHash.Empty,
                true,
                (detail ?? string.Empty) + " (refused as a value)");
            return true;
        }

        /// <summary>One-line summary of the latch state and its trace, for a failure message or an evidence file.</summary>
        public string Describe() =>
            "compiledIn=" + (IsCompiledIn ? "1" : "0")
            + " armed=" + ArmedBoundaryCount.ToString(CultureInfo.InvariantCulture)
            + " reached=" + ReachCount.ToString(CultureInfo.InvariantCulture)
            + " injected=" + InjectedCount.ToString(CultureInfo.InvariantCulture)
            + " records=" + Trace.Count.ToString(CultureInfo.InvariantCulture);

        private static int Index(FaultBoundary boundary)
        {
            int index = (int)boundary;
            if (index < 0 || index >= FaultBoundaryText.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(boundary), "Unknown fault boundary " + index + ".");
            }

            return index;
        }
    }
#endif
}

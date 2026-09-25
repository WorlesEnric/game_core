// GameCore.Unity.Runtime.Faults — the deterministic fault latches of GC-017.
//
// Normative sources: `docs/game-core/08-validation-and-performance.md` TEST-016 (the fault matrix every row of
// which needs a deterministic injection point, not a probability), 00 P-029 (a preparation or migration failure
// releases staged leases in reverse dependency order and leaves the old assembly intact), P-030 (publication
// switches the assembly at one serialized commit), P-031 (a failure after the first live write faults the world:
// admission stays closed, no epoch or image publishes, no simulation resumes), P-047/P-048 (in-flight work is
// fenced and resources that unfinished work may still reach are quarantined, never freed on a timeout) and
// P-051 (a cancellation/publication race is resolved by the serialized control lane at the stated cutoff).
//
// Two rules shape this file:
//
//   * **A latch is deterministic, never probabilistic.** Every boundary is named once, armed by identity and
//     reached at one place in the real code path. A test arms a boundary, drives the real operation and asserts
//     the observable outcome; "random fault probability" would prove nothing about coverage.
//   * **A latch costs a release build nothing.** The reaching code exists only when GAMECORE_FAULT_INJECTION is
//     defined (`FaultCompilation.Symbol`); without it `FaultReach.Reach` is an inlined no-op and no boundary can
//     fire, and `AssemblyFaultInjection.IsCompiledIn` reports that plainly so a test that needs the latches fails
//     with an actionable message instead of passing vacuously. The boundary sites are publication and step
//     boundaries — never per entity, per frame or per system — so the compilation switch is the only reason a
//     release build differs from a build carrying the latches.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using GameCore.Contracts;

namespace GameCore.Unity.Runtime.Faults
{
    /// <summary>
    /// The named boundaries a fault can be injected at, exactly the rows of TEST-016's matrix that have an
    /// injection point in the apply/cancellation path. The order is the order of the matrix.
    /// </summary>
    public enum FaultBoundary
    {
        /// <summary>Manifest, dependency, capability closure or schedule validation (TEST-016 row 1).</summary>
        Validation = 0,

        /// <summary>Resource acquisition or plan preparation (TEST-016 row 2).</summary>
        Acquisition = 1,

        /// <summary>The publication fence: tracked handles and fenced readers are settled first (TEST-016 row 4).</summary>
        Fence = 2,

        /// <summary>Migration on bounded scratch, before the first live write (TEST-016 row 5, P-029).</summary>
        Migration = 3,

        /// <summary>After the first authoritative live write (TEST-016 row 5, P-031).</summary>
        FirstLiveWrite = 4,

        /// <summary>Structural playback after a step's systems ran, before its commit (TEST-016 row 6, P-041).</summary>
        StructuralPlayback = 5,

        /// <summary>Installing the new execution graph and opening staged gates (TEST-016 row 5, P-030).</summary>
        GateInstallation = 6,

        /// <summary>Cleanup of a refused or faulted operation's staged work (TEST-016 row 8, P-048).</summary>
        Cleanup = 7,
    }

    /// <summary>Stable diagnostic names of the boundaries, so a trace and an assertion name the same thing.</summary>
    public static class FaultBoundaryText
    {
        /// <summary>The names in <see cref="FaultBoundary"/>'s order; the same strings appear in TEST-016's table.</summary>
        public static readonly string[] Names =
        {
            "validation",
            "acquisition",
            "fence",
            "migration",
            "first-live-write",
            "structural-playback",
            "gate-installation",
            "cleanup",
        };

        /// <summary>Number of declared boundaries; a latch array is sized from this, not from a literal.</summary>
        public static int Count => Names.Length;

        public static string Of(FaultBoundary boundary)
        {
            int index = (int)boundary;
            return index >= 0 && index < Names.Length
                ? Names[index]
                : "boundary-" + index.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// One reached boundary, in order, with the provenance TEST-016 row 2 requires ("failure includes operation ID
    /// and provenance"): which boundary, which operation, which plan identity and what the injector said.
    /// </summary>
    public readonly struct FaultRecord
    {
        public readonly int Ordinal;
        public readonly FaultBoundary Boundary;
        public readonly OperationId Operation;
        public readonly ContentHash PlanHash;
        public readonly bool Injected;
        public readonly string Detail;

        public FaultRecord(
            int ordinal,
            FaultBoundary boundary,
            OperationId operation,
            ContentHash planHash,
            bool injected,
            string detail)
        {
            Ordinal = ordinal;
            Boundary = boundary;
            Operation = operation;
            PlanHash = planHash;
            Injected = injected;
            Detail = detail ?? string.Empty;
        }

        /// <summary>Canonical one-line form of one trace record; `boundary=<name> op=<id> plan=<hash> fired=<0|1>`.</summary>
        public string ToLine() =>
            "boundary=" + FaultBoundaryText.Of(Boundary)
            + " op=" + Operation.ToString()
            + " plan=" + PlanHash.ToHex()
            + " fired=" + (Injected ? "1" : "0")
            + " detail=" + Detail;

        public override string ToString() => ToLine();
    }

    /// <summary>
    /// The fault an armed latch raises. It is a distinct type so a production `catch (Exception)` can tell an
    /// injected fault from a real defect, and so a test never confuses the injection with the behaviour under test.
    /// It is a prewrite or postwrite classification decision of the *caller*, not of the exception.
    /// </summary>
    public sealed class FaultInjectedException : Exception
    {
        public FaultInjectedException(FaultBoundary boundary, string message)
            : base(message)
        {
            Boundary = boundary;
        }

        /// <summary>The boundary whose latch raised this fault.</summary>
        public FaultBoundary Boundary { get; }
    }

    /// <summary>
    /// The compilation switch of every GC-017 latch. A build that does not define
    /// <see cref="Symbol"/> carries no reaching implementation at all, so no boundary can fire and no fault record
    /// can be produced; <see cref="IsCompiledIn"/> exists so that fact is assertable rather than silent.
    /// The Unity validation project defines the symbol through the asmdef `versionDefines` entry on
    /// `com.unity.test-framework`; a shipping project that does not reference the Test Framework does not.
    /// </summary>
    public static class FaultCompilation
    {
        /// <summary>Preprocessor symbol that compiles the fault latches in.</summary>
        public const string Symbol = "GAMECORE_FAULT_INJECTION";

        /// <summary>True when this compilation carries the latch implementation.</summary>
        public static bool IsCompiledIn
        {
            get
            {
#if GAMECORE_FAULT_INJECTION
                return true;
#else
                return false;
#endif
            }
        }
    }

    /// <summary>
    /// The one reaching helper of every boundary site. Call sites are unconditional so the boundary is visible in
    /// the code that owns it; the effect exists only in a compilation that defines
    /// <see cref="FaultCompilation.Symbol"/>, and without it this returns immediately (the JIT inlines the
    /// constant `false`), leaving a release build with no latch work and no allocation.
    ///
    /// Two reach shapes exist because the two kinds of boundary site differ. <see cref="Reach"/> raises
    /// <see cref="FaultInjectedException"/> and suits a site whose caller classifies the failure into its own
    /// report (the publisher's refusal and fault paths). <see cref="Refuse"/> answers a boolean and suits a site
    /// that refuses a *value* rather than throwing (the staged-resource gate, whose contract returns
    /// <see cref="DiagnosticCode.ResourceUnavailable"/> instead of an exception).
    /// </summary>
    internal static class FaultReach
    {
        /// <summary>
        /// Reaches one boundary for one operation. Returns false when nothing was injected (the normal production
        /// path, and every path in a compilation without the symbol); raises <see cref="FaultInjectedException"/>
        /// when the boundary is armed.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Reach(
            AssemblyFaultInjection? faults,
            FaultBoundary boundary,
            OperationId operation,
            ContentHash planHash,
            string detail)
        {
#if GAMECORE_FAULT_INJECTION
            return faults != null && faults.TryReach(boundary, operation, planHash, detail);
#else
            _ = faults;
            _ = boundary;
            _ = operation;
            _ = planHash;
            _ = detail;
            return false;
#endif
        }

        /// <summary>
        /// Reaches one boundary whose failure is expressed as a refusal value rather than an exception. Returns
        /// true when the armed boundary refused the call, false on the production path.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Refuse(AssemblyFaultInjection? faults, FaultBoundary boundary, string detail)
        {
#if GAMECORE_FAULT_INJECTION
            return faults != null && faults.TryRefuse(boundary, detail);
#else
            _ = faults;
            _ = boundary;
            _ = detail;
            return false;
#endif
        }
    }

    /// <summary>
    /// Ordered trace of every boundary this process reached, so an evidence file can show the sequence rather
    /// than a boolean. One per world keeps two worlds' traces apart (P-004).
    /// </summary>
    public sealed class FaultTrace
    {
        private readonly List<FaultRecord> records = new List<FaultRecord>();

        /// <summary>Boundary reaches recorded, injected or not.</summary>
        public int Count => records.Count;

        /// <summary>Injected faults among them.</summary>
        public int InjectedCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < records.Count; i++)
                {
                    if (records[i].Injected)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public IReadOnlyList<FaultRecord> Records => records;

        /// <summary>Appends one record and returns it, so the caller reports exactly what was recorded.</summary>
        public FaultRecord Add(FaultBoundary boundary, OperationId operation, ContentHash planHash, bool injected, string detail)
        {
            var record = new FaultRecord(records.Count, boundary, operation, planHash, injected, detail);
            records.Add(record);
            return record;
        }

        /// <summary>Clears the trace; armed boundaries belong to the latch, not to the trace.</summary>
        public void Clear() => records.Clear();

        /// <summary>Reaches recorded for one boundary, in order (empty when the boundary was never reached).</summary>
        public IReadOnlyList<FaultRecord> Of(FaultBoundary boundary)
        {
            var found = new List<FaultRecord>();
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].Boundary == boundary)
                {
                    found.Add(records[i]);
                }
            }

            return found;
        }

        /// <summary>The trace as LF-separated canonical lines, in order; the form `artifacts/faults/` archives.</summary>
        public string Describe()
        {
            if (records.Count == 0)
            {
                return "<empty>";
            }

            var lines = new List<string>(records.Count);
            for (int i = 0; i < records.Count; i++)
            {
                lines.Add(records[i].ToLine());
            }

            return string.Join("\n", lines.ToArray());
        }
    }
}

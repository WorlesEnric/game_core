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
//   * **A latch costs a release build nothing, by construction.** Every type below is inside one
//     `#if GAMECORE_FAULT_INJECTION`, and that symbol reaches a compilation only through
//     `GameCore.Unity.Runtime.asmdef`'s `versionDefines` entry on the qualification marker package
//     `com.gamecore.fault-qualification`, which only the validation project's manifest references. A shipping
//     project that omits the marker compiles this namespace with an empty body: no latch type, no boundary name
//     table, no per-world array or object. Every call site in the runtime assembly is guarded the same way, so no
//     reach call survives either, and `FaultCompilation.IsCompiledIn` is the constant `true` rather than a runtime
//     answer — a build without the latch has no such type to ask. The namespace declaration stays outside the guard
//     so the assembly's `using GameCore.Unity.Runtime.Faults;` directives remain valid in both configurations; a
//     namespace declaration on its own emits no metadata.
namespace GameCore.Unity.Runtime.Faults
{
#if GAMECORE_FAULT_INJECTION
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using GameCore.Contracts;

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

        /// <summary>
        /// The committed boundary is being copied and serialized into a checkpoint document (O-20, P-053; GC-027).
        /// </summary>
        CheckpointCaptureCopy = 8,

        /// <summary>
        /// A captured document is being published to its store: the temporary write and the replacement (06 s7).
        /// </summary>
        CheckpointPublication = 9,

        /// <summary>
        /// A destination's composition, recipes and reference tables are being rebuilt from the recovery source
        /// (P-049's "validates/rebuilds composition and recipes"; GC-027).
        /// </summary>
        RestoreReferenceRepair = 10,

        /// <summary>
        /// A staged restored world has been written to and is not yet published (TEST-016 row 5, P-031; GC-027).
        /// </summary>
        RestoreApply = 11,

        /// <summary>
        /// A staged or recovered world is about to become the registry's published world (P-030, P-049; GC-027).
        /// </summary>
        RecoveryPublication = 12,
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
            "checkpoint-capture-copy",
            "checkpoint-publication",
            "restore-reference-repair",
            "restore-apply",
            "recovery-publication",
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
    /// `com.gamecore.fault-qualification`; shipping projects omit that explicit qualification package.
    /// </summary>
    public static class FaultCompilation
    {
        /// <summary>Preprocessor symbol that compiles the fault latches in.</summary>
        public const string Symbol = "GAMECORE_FAULT_INJECTION";

        /// <summary>
        /// True, always: this type exists only in a compilation that carries the latch implementation (the whole
        /// namespace body is inside the guard), so a test's assertion of it states that the qualification symbol is
        /// active rather than asking a runtime question. A shipping build has neither the type nor the call.
        /// </summary>
        public static bool IsCompiledIn => true;
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
        /// Reaches one boundary for one operation. Returns false when nothing was injected; raises
        /// <see cref="FaultInjectedException"/> when the boundary is armed. This method does not exist in a
        /// shipping compilation, and neither does any call to it, because every call site is inside the same guard.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Reach(
            AssemblyFaultInjection? faults,
            FaultBoundary boundary,
            OperationId operation,
            ContentHash planHash,
            string detail) =>
            faults != null && faults.TryReach(boundary, operation, planHash, detail);

        /// <summary>
        /// Reaches one boundary whose failure is expressed as a refusal value rather than an exception. Returns
        /// true when the armed boundary refused the call, false on the production path.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Refuse(AssemblyFaultInjection? faults, FaultBoundary boundary, string detail) =>
            faults != null && faults.TryRefuse(boundary, detail);
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
#endif
}

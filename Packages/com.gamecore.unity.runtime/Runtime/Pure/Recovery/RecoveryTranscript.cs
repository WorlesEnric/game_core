// GameCore.Unity.Runtime (engine-free part, compiled as GameCore.Execution) - recovery transcript (GC-027).
//
// Normative sources: 00 P-052 (errors name their phase and identity; a diagnostic is actionable, not a log line),
// P-053 (a checkpointed world's facts are recorded and comparable) and the task's own evidence requirement: "Crash/
// restart transcripts and outbox consistency reports under `artifacts/gc-027/`" and "Document the recovery behavior
// and data-loss boundary from executed evidence".
//
// WHAT THIS FILE IS
//
// The transcript one recovery attempt produces: an ordered, bounded list of the phases it passed through, each with
// the fault point it reached (if any) and the data-loss class at that point. It carries no timestamp, no host
// measure and no thread identity, so two runs of the same recovery produce the same transcript and the same digest -
// which is what makes a transcript an artifact a reviewer can compare instead of a log to be read.
//
// The digest is SHA-256 over the canonical text (the same hash family `ContentHash` uses everywhere else in the
// protocol), so an evidence file can pin one line and a scenario can assert it.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Execution.Recovery
{
    /// <summary>The phases a recovery transcript records, in the order a recovery reaches them (O-22, 06 s7).</summary>
    public enum RecoveryPhase
    {
        /// <summary>The faulted source's own state was read: lifecycle, fault code and fault count (P-031).</summary>
        SourceObserved = 0,

        /// <summary>The boundary was copied and serialized into a document (O-20).</summary>
        Capture = 1,

        /// <summary>The document was published to the store, or publication was refused (06 s7).</summary>
        Publish = 2,

        /// <summary>The document was read back from the store and verified (P-054).</summary>
        StoreRead = 3,

        /// <summary>The destination's composition, recipes and references were rebuilt (P-049).</summary>
        ReferenceRepair = 4,

        /// <summary>The destination's state was restored and it was validated before publication (O-21).</summary>
        Restore = 5,

        /// <summary>Delivery obligation rows were reinstated into the new session (P-045, P-053).</summary>
        OutboxReinstate = 6,

        /// <summary>The new session was published: a new world, running on its own epoch (P-030, P-035).</summary>
        PublishNewWorld = 7,

        /// <summary>The old faulted world was stopped; it is never resumed (P-049).</summary>
        SourceStopped = 8,

        /// <summary>An obligation was handed to its destination port (P-045).</summary>
        Delivery = 9,

        /// <summary>An obligation was settled: acknowledged, rejected or compensated (P-042, P-045).</summary>
        Settlement = 10,

        /// <summary>A fault was injected and reached at a named point (TEST-016).</summary>
        FaultPointReached = 11,

        /// <summary>The attempt was refused with a code and a reason (P-052).</summary>
        Refused = 12,

        /// <summary>A restart began from stored bytes only: no in-process state was carried over.</summary>
        Restart = 13,
    }

    /// <summary>
    /// One transcript line: which phase, what it observed, which injection point (if any) fired on the way, and the
    /// data-loss class at that point. The ordinal is assigned by the transcript, so a line is self-locating.
    /// </summary>
    public readonly struct RecoveryTranscriptLine
    {
        public RecoveryTranscriptLine(
            int ordinal,
            RecoveryPhase phase,
            string detail,
            string faultPointId,
            RecoveryDataLossClass dataLoss)
        {
            Ordinal = ordinal;
            Phase = phase;
            Detail = detail ?? string.Empty;
            FaultPointId = faultPointId ?? string.Empty;
            DataLoss = dataLoss;
        }

        public int Ordinal { get; }

        public RecoveryPhase Phase { get; }

        public string Detail { get; }

        /// <summary>The injection point this line records, or empty when none fired.</summary>
        public string FaultPointId { get; }

        public RecoveryDataLossClass DataLoss { get; }

        /// <summary>Canonical one-line form; the text the transcript digest is computed over.</summary>
        public string ToLine() =>
            Ordinal.ToString("D3", CultureInfo.InvariantCulture)
            + " " + Phase.ToString()
            + " loss=" + DataLoss.ToString()
            + (FaultPointId.Length == 0 ? string.Empty : " at=" + FaultPointId)
            + " " + Detail;

        public override string ToString() => ToLine();
    }

    /// <summary>
    /// The bounded transcript of one recovery. Bounded because every other list in this protocol is (P-043): the
    /// capacity is the caller's, an overflow is counted rather than grown, and the transcript never claims to hold
    /// more than it does.
    /// </summary>
    public sealed class RecoveryTranscript
    {
        private readonly List<RecoveryTranscriptLine> lines = new List<RecoveryTranscriptLine>();
        private readonly int capacity;

        public RecoveryTranscript(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "A transcript requires positive capacity.");
            }

            this.capacity = capacity;
        }

        public int Capacity => capacity;

        /// <summary>Lines recorded; never more than <see cref="Capacity"/>.</summary>
        public int Count => lines.Count;

        /// <summary>Lines refused because the transcript was full; counted, never silently dropped (P-043).</summary>
        public int OverflowCount { get; private set; }

        /// <summary>Faults recorded among the lines.</summary>
        public int FaultCount
        {
            get
            {
                int faults = 0;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].Phase == RecoveryPhase.FaultPointReached)
                    {
                        faults++;
                    }
                }

                return faults;
            }
        }

        public IReadOnlyList<RecoveryTranscriptLine> Lines => lines;

        /// <summary>
        /// Appends one line, assigning its ordinal. Returns the recorded line, or a line with ordinal -1 when the
        /// transcript is full (which the caller can see in <see cref="OverflowCount"/>).
        /// </summary>
        public RecoveryTranscriptLine Add(
            RecoveryPhase phase,
            string detail,
            RecoveryDataLossClass dataLoss,
            string faultPointId = "")
        {
            if (lines.Count >= capacity)
            {
                OverflowCount++;
                return new RecoveryTranscriptLine(-1, phase, detail, faultPointId, dataLoss);
            }

            var line = new RecoveryTranscriptLine(lines.Count, phase, detail, faultPointId, dataLoss);
            lines.Add(line);
            return line;
        }

        /// <summary>Records one reached injection point, so a faulted run names where it stopped (TEST-016).</summary>
        public RecoveryTranscriptLine AddFault(string faultPointId, string detail, RecoveryDataLossClass dataLoss) =>
            Add(RecoveryPhase.FaultPointReached, detail, dataLoss, faultPointId);

        /// <summary>
        /// The data-loss boundary this recovery was actually exposed to: the most severe class any recorded line
        /// carried. The ordering is the enum's own (none, attempt work, unpersisted obligation, since checkpoint), so
        /// the answer is derived from executed lines rather than asserted in prose.
        /// </summary>
        public RecoveryDataLossClass DataLossBoundary()
        {
            RecoveryDataLossClass worst = RecoveryDataLossClass.None;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].DataLoss > worst)
                {
                    worst = lines[i].DataLoss;
                }
            }

            return worst;
        }

        /// <summary>The transcript as LF-separated canonical lines; `<empty>` when nothing was recorded.</summary>
        public string Describe()
        {
            if (lines.Count == 0)
            {
                return "<empty>";
            }

            var text = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i != 0)
                {
                    text.Append('\n');
                }

                text.Append(lines[i].ToLine());
            }

            return text.ToString();
        }

        /// <summary>
        /// SHA-256 of the canonical transcript text: one value a scenario pins and an evidence file records. It
        /// changes when, and only when, the recovery did something different.
        /// </summary>
        public ContentHash Digest() => ContentHash.Compute(Encoding.UTF8.GetBytes(Describe()));

        /// <summary>Hex form of <see cref="Digest"/>, which is what an evidence file or a probe assertion carries.</summary>
        public string DigestHex() => Digest().ToHex();

        /// <summary>One-line summary for a report: how many lines, how many faults, the worst loss class.</summary>
        public string Summary() =>
            "transcript(lines=" + lines.Count.ToString(CultureInfo.InvariantCulture)
            + "/" + capacity.ToString(CultureInfo.InvariantCulture)
            + ",faults=" + FaultCount.ToString(CultureInfo.InvariantCulture)
            + ",loss=" + DataLossBoundary().ToString()
            + ",digest=" + DigestHex() + ")";

        public override string ToString() => Summary();
    }

    /// <summary>
    /// What a restart observed: the bytes it started from and the session it produced. It is the record that makes
    /// "restart" an injection point with a permitted observable result rather than a narrative claim - a restart
    /// carries no in-process state, so whatever the store holds is the whole of its input (P-049, P-053).
    /// </summary>
    public sealed class RecoveryRestartPoint
    {
        public RecoveryRestartPoint(
            string storeLocation,
            bool documentPresent,
            ContentHash documentHash,
            WorldId previousSession,
            WorldId newSession,
            DiagnosticCode code,
            string detail)
        {
            StoreLocation = storeLocation ?? string.Empty;
            DocumentPresent = documentPresent;
            DocumentHash = documentHash;
            PreviousSession = previousSession;
            NewSession = newSession;
            Code = code;
            Detail = detail ?? string.Empty;
        }

        /// <summary>Where the restart read from; diagnostic, never an identity (P-004).</summary>
        public string StoreLocation { get; }

        /// <summary>True when a document was present; false means the restart had nothing to recover from.</summary>
        public bool DocumentPresent { get; }

        /// <summary>Hash of the document the restart read, or <see cref="ContentHash.Empty"/> when none was present.</summary>
        public ContentHash DocumentHash { get; }

        /// <summary>The session the faulted world had; a restart never reuses it (P-004).</summary>
        public WorldId PreviousSession { get; }

        /// <summary>The session the restart produced, or the default id when it produced no world.</summary>
        public WorldId NewSession { get; }

        public DiagnosticCode Code { get; }

        public string Detail { get; }

        /// <summary>True when the restart produced a new incarnation different from the faulted one (P-049).</summary>
        public bool ProducedNewSession =>
            !NewSession.Session.IsDefault
            && !NewSession.Session.Equals(PreviousSession.Session);

        public string ToLine() =>
            "restart store=" + StoreLocation
            + " present=" + (DocumentPresent ? "1" : "0")
            + " document=" + DocumentHash.ToHex()
            + " was=" + PreviousSession.Session.ToString()
            + " now=" + (NewSession.Session.IsDefault ? "<none>" : NewSession.Session.ToString())
            + " code=" + DiagnosticCodeText.Of(Code)
            + (Detail.Length == 0 ? string.Empty : " detail=" + Detail);

        public override string ToString() => ToLine();
    }
}

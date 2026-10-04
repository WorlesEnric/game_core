// GameCore.Contracts - the temporal origin a restored world continues from (SADR-012 (studio), archive ADR-022).
//
// Normative sources: P-036 (fixed-step debt is retained exactly), P-038 (domain seconds advance only by explicit
// command), P-049 (a restore creates a new WorldId), P-050 (per-issuer high-water marks persist so an issuer never
// reuses a sequence), P-053 (a checkpoint contains step/time/debt and dedup cursors) and P-055 (a new behaviour is
// gated by a required feature id, never by guessing).
//
// V1 restored every world at logical step 0 with zero debt and zero domain seconds, although the header already
// recorded all three. SADR-012 makes the recorded values the restored world's origin, but only for a document whose
// container declares `CheckpointFormat.TemporalContinuityFeatureId`: a document written before that feature existed
// keeps its V1 meaning (step 0) and the restore reports that it did so. Nothing here changes a record's wire shape.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameCore.Contracts
{
    /// <summary>Where a world's temporal origin came from (SADR-012).</summary>
    public enum TemporalOriginSource
    {
        /// <summary>A freshly created world: step 0, no debt, no domain seconds, no issuer history.</summary>
        Fresh = 0,

        /// <summary>A restore that continues the checkpoint's step, debt, domain seconds and issuer sequences.</summary>
        Continued = 1,

        /// <summary>
        /// A restore of a document that does not declare temporal continuity: the V1 step-0 semantics apply and are
        /// reported, never silently substituted (P-055).
        /// </summary>
        LegacyStepZero = 2,
    }

    /// <summary>One issuer's admission high-water mark carried into a restored world (P-050).</summary>
    public readonly struct IssuerHighWaterMark
    {
        public IssuerHighWaterMark(Id128 issuer, ulong sequence)
        {
            Issuer = issuer;
            Sequence = sequence;
        }

        /// <summary>The issuer (an input source, a tool, a host lane) whose sequence continues.</summary>
        public Id128 Issuer { get; }

        /// <summary>The highest sequence the issuer had used; the restored issuer's next sequence is greater.</summary>
        public ulong Sequence { get; }

        public bool Equals(IssuerHighWaterMark other) => Issuer.Equals(other.Issuer) && Sequence == other.Sequence;

        public override bool Equals(object? obj) => obj is IssuerHighWaterMark other && Equals(other);

        public override int GetHashCode() => Issuer.GetHashCode() ^ Sequence.GetHashCode();

        public override string ToString() => Issuer.ToString() + "@" + Sequence.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The temporal origin a world is created at (SADR-012): its first published logical step, the fixed-step debt it
    /// owes, the domain seconds it starts from and the issuer sequences it continues. It is applied before the world's
    /// initial publication, so the restored world's first committed image already names the continued step.
    /// </summary>
    public sealed class RestoredTemporalOrigin
    {
        private readonly IssuerHighWaterMark[] marks;

        public RestoredTemporalOrigin(
            LogicalStepId logicalStep,
            ulong timeDebtTicks,
            double domainSeconds,
            IReadOnlyList<IssuerHighWaterMark>? issuerHighWaterMarks,
            TemporalOriginSource source,
            string? detail)
        {
            if (double.IsNaN(domainSeconds) || double.IsInfinity(domainSeconds) || domainSeconds < 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(domainSeconds), "Domain seconds are a finite, non-negative clock value (P-038).");
            }

            LogicalStep = logicalStep;
            TimeDebtTicks = timeDebtTicks;
            DomainSeconds = domainSeconds;
            Source = source;
            Detail = detail ?? string.Empty;

            var canonical = new List<IssuerHighWaterMark>();
            if (issuerHighWaterMarks != null)
            {
                for (int i = 0; i < issuerHighWaterMarks.Count; i++)
                {
                    IssuerHighWaterMark mark = issuerHighWaterMarks[i];
                    if (mark.Issuer.IsDefault)
                    {
                        continue;
                    }

                    int existing = canonical.FindIndex(m => m.Issuer.Equals(mark.Issuer));
                    if (existing < 0)
                    {
                        canonical.Add(mark);
                    }
                    else if (mark.Sequence > canonical[existing].Sequence)
                    {
                        // Two rows for one issuer keep the larger mark: a high-water mark never moves backwards (P-050).
                        canonical[existing] = mark;
                    }
                }
            }

            canonical.Sort((left, right) => left.Issuer.CompareTo(right.Issuer));
            marks = canonical.ToArray();
        }

        /// <summary>The origin of a world that is not a restore.</summary>
        public static RestoredTemporalOrigin Fresh { get; } =
            new RestoredTemporalOrigin(LogicalStepId.Zero, 0UL, 0.0, null, TemporalOriginSource.Fresh, "fresh world");

        /// <summary>The logical step the world's initial assembly is published at; its next step is one greater.</summary>
        public LogicalStepId LogicalStep { get; }

        /// <summary>Retained fixed-step debt the world owes at its first running sample (P-036); zero when command-driven.</summary>
        public ulong TimeDebtTicks { get; }

        /// <summary>Domain seconds the world starts from (P-038).</summary>
        public double DomainSeconds { get; }

        /// <summary>Issuer high-water marks, canonical by issuer, one per issuer (P-050).</summary>
        public IReadOnlyList<IssuerHighWaterMark> IssuerHighWaterMarks => marks;

        public TemporalOriginSource Source { get; }

        /// <summary>Why the origin is what it is, for the restore report.</summary>
        public string Detail { get; }

        /// <summary>True when the restore continues the captured clock rather than starting at step 0.</summary>
        public bool IsContinued => Source == TemporalOriginSource.Continued;

        /// <summary>True for a legacy document restored with V1 step-0 semantics (reported, P-055).</summary>
        public bool IsLegacyStepZero => Source == TemporalOriginSource.LegacyStepZero;

        /// <summary>The high-water mark of <paramref name="issuer"/>, when the origin carries one.</summary>
        public bool TryGetIssuerHighWater(Id128 issuer, out ulong sequence)
        {
            for (int i = 0; i < marks.Length; i++)
            {
                if (marks[i].Issuer.Equals(issuer))
                {
                    sequence = marks[i].Sequence;
                    return true;
                }
            }

            sequence = 0UL;
            return false;
        }

        /// <summary>
        /// The origin a checkpoint defines. With <paramref name="declaresTemporalContinuity"/> the header's step, debt
        /// and domain seconds are continued; without it the V1 meaning applies and every temporal value is zero. The
        /// issuer high-water marks come from the cursor rows either way, because those rows are V1 content whose
        /// whole purpose is duplicate suppression across a restore (P-050, P-053).
        /// </summary>
        public static RestoredTemporalOrigin FromCheckpoint(
            HeaderRecordValue header,
            IReadOnlyList<CursorRecordValue>? cursors,
            bool declaresTemporalContinuity)
        {
            var issuerMarks = new List<IssuerHighWaterMark>();
            if (cursors != null)
            {
                for (int i = 0; i < cursors.Count; i++)
                {
                    CursorRecordValue row = cursors[i];
                    if (row.Row == CursorRowKind.IssuerHighWater)
                    {
                        issuerMarks.Add(new IssuerHighWaterMark(row.IssuerId, row.Sequence));
                    }
                }
            }

            if (!declaresTemporalContinuity)
            {
                return new RestoredTemporalOrigin(
                    LogicalStepId.Zero,
                    0UL,
                    0.0,
                    issuerMarks,
                    TemporalOriginSource.LegacyStepZero,
                    "the checkpoint does not declare " + CheckpointFormat.TemporalContinuityFeatureStableName
                    + "; it was captured at step " + header.LogicalStep.ToString(CultureInfo.InvariantCulture)
                    + " and restores with V1 step-0 semantics (P-055).");
            }

            bool fixedStep = header.Temporal == TemporalModel.FixedStep;
            double domain = header.DomainSeconds;
            if (double.IsNaN(domain) || double.IsInfinity(domain) || domain < 0.0)
            {
                domain = 0.0;
            }

            return new RestoredTemporalOrigin(
                new LogicalStepId(header.LogicalStep),
                fixedStep ? header.TimeDebtTicks : 0UL,
                domain,
                issuerMarks,
                TemporalOriginSource.Continued,
                "continued from step " + header.LogicalStep.ToString(CultureInfo.InvariantCulture) + ", debt "
                + (fixedStep ? header.TimeDebtTicks : 0UL).ToString(CultureInfo.InvariantCulture) + " ticks, domain "
                + domain.ToString("R", CultureInfo.InvariantCulture) + " s.");
        }

        /// <summary>The origin of a verified document: its header, its cursor rows and its declared features.</summary>
        public static bool TryFromDocument(
            CheckpointDocument document,
            out RestoredTemporalOrigin origin,
            out DiagnosticCode code,
            out string detail)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (!document.TryReadRecords<CursorRecordValue>(CheckpointRecordKind.Cursor, out IReadOnlyList<CursorRecordValue> cursors, out code, out detail))
            {
                origin = Fresh;
                return false;
            }

            origin = FromCheckpoint(document.Header, cursors, document.DeclaresTemporalContinuity);
            return true;
        }

        public override string ToString() =>
            "origin(" + Source.ToString() + ",step=" + LogicalStep.Value.ToString(CultureInfo.InvariantCulture)
            + ",debt=" + TimeDebtTicks.ToString(CultureInfo.InvariantCulture)
            + ",domain=" + DomainSeconds.ToString("R", CultureInfo.InvariantCulture)
            + ",issuers=" + marks.Length.ToString(CultureInfo.InvariantCulture) + ")";
    }
}

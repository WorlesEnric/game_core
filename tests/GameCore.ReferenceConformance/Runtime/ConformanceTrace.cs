// GameCore.ReferenceConformance — the normalized reference-conformance trace (P-008, P-045, P-060).
//
// One trace document records what one real run of a 07 before/after table observed, in a fixed canonical text, so
// the same run reproduces the same bytes and a different observation cannot look like the same one:
//
//   format=gamecore.reference-conformance-trace/1
//   tables=<n>
//   rows=<n>
//   facts=<n>
//   <table>|<row>|<phase>|<field>=<value>
//   ...
//   digest=<sha256 over the ordered lines above, LF separated>
//
// The writer is hand-rolled for the same reason the narrative trace's is (04 s8 forbids runtime type discovery): a
// fixed writer is what makes "the same input produces the same bytes" checkable by eye. The document carries no
// clock, no machine path and no dictionary order — entries are emitted in canonical `(table, row, phase, field)`
// order regardless of the order a run recorded them (P-008).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Rules.Narrative;

namespace GameCore.ReferenceConformance
{
    /// <summary>Which side of one assembly operation a recorded fact describes (07's before/after columns).</summary>
    public enum ConformancePhase
    {
        /// <summary>The last published state before the operation (07's "Before").</summary>
        Before = 0,

        /// <summary>The first published state after successful publication (07's "After").</summary>
        After = 1,
    }

    /// <summary>
    /// The canonical text of one observed fact. Every value is a token, never prose: a decimal integer, a boolean,
    /// `none` for an absent contribution, a canonical set spelling, `same` for "unchanged by this operation", or a
    /// stable name. A run never records a claim it did not read.
    /// </summary>
    public static class ConformanceValue
    {
        /// <summary>The token of an absent contribution or an unbound target.</summary>
        public const string None = "none";

        /// <summary>The token meaning "this operation did not change it"; paired with the expected value.</summary>
        public const string Same = "same";

        /// <summary>Canonical decimal spelling of one observed integer.</summary>
        public static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>Canonical decimal spelling of one observed unsigned integer.</summary>
        public static string UInt(ulong value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>Canonical boolean spelling, lower case, never a culture-specific word.</summary>
        public static string Bool(bool value) => value ? "true" : "false";

        /// <summary>Canonical spelling of one observed ordered stable-identity set: <c>{a,b}</c>, or <c>{}</c>.</summary>
        public static string Set(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return "{}";
            }

            var text = new StringBuilder();
            text.Append('{');
            for (int i = 0; i < values.Count; i++)
            {
                if (i != 0)
                {
                    text.Append(',');
                }

                text.Append(values[i]);
            }

            text.Append('}');
            return text.ToString();
        }

        /// <summary>True when one canonical value is a well-formed token: non-empty and free of separators.</summary>
        public static bool IsCanonical(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            for (int i = 0; i < value!.Length; i++)
            {
                char c = value[i];
                if (c == '|' || c == '=' || c == '\n' || c == '\r')
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>One recorded fact of one run: which table row it belongs to, which phase, and what was read.</summary>
    public readonly struct ConformanceTraceEntry
    {
        public ConformanceTraceEntry(string tableId, string rowId, ConformancePhase phase, string field, string value)
        {
            TableId = tableId ?? throw new ArgumentNullException(nameof(tableId));
            RowId = rowId ?? throw new ArgumentNullException(nameof(rowId));
            Phase = phase;
            Field = field ?? throw new ArgumentNullException(nameof(field));
            Value = value ?? string.Empty;
            if (!ConformanceValue.IsCanonical(Value))
            {
                throw new ArgumentException(
                    "a recorded fact's value must be a canonical token, not prose containing a separator: '" + Value
                    + "' (P-008).", nameof(value));
            }
        }

        /// <summary>The 07 table this fact belongs to, e.g. <c>cards</c>.</summary>
        public string TableId { get; }

        /// <summary>The 07 table row this fact belongs to.</summary>
        public string RowId { get; }

        /// <summary>Which side of the operation this fact describes.</summary>
        public ConformancePhase Phase { get; }

        /// <summary>The canonical field key, e.g. <c>cards.seat-a.set-bonus</c>.</summary>
        public string Field { get; }

        /// <summary>The canonical observed value.</summary>
        public string Value { get; }

        /// <summary>The one canonical line this entry contributes to a trace document.</summary>
        public string ToLine() => TableId + "|" + RowId + "|" + PhaseToken(Phase) + "|" + Field + "=" + Value;

        /// <summary>The canonical token of one phase; a document never writes a CLR enum name (P-054).</summary>
        public static string PhaseToken(ConformancePhase phase) => phase == ConformancePhase.Before ? "before" : "after";

        /// <summary>Parses one phase token, or returns false for an unknown token rather than guessing.</summary>
        public static bool TryParsePhase(string token, out ConformancePhase phase)
        {
            if (string.Equals(token, "before", StringComparison.Ordinal))
            {
                phase = ConformancePhase.Before;
                return true;
            }

            if (string.Equals(token, "after", StringComparison.Ordinal))
            {
                phase = ConformancePhase.After;
                return true;
            }

            phase = ConformancePhase.Before;
            return false;
        }

        public override string ToString() => ToLine();
    }

    /// <summary>
    /// A run's normalized trace: every fact it read, plus the canonical document text and digest. Recording is
    /// append-only; serialization sorts, so a run that records in a different order still writes the same bytes.
    /// </summary>
    public sealed class ConformanceTrace
    {
        private readonly List<ConformanceTraceEntry> entries = new List<ConformanceTraceEntry>();
        private readonly HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>One run's trace, labelled with the run identity that produced it.</summary>
        public ConformanceTrace(string label)
        {
            Label = label ?? string.Empty;
        }

        /// <summary>The run this trace came from, e.g. <c>generated-catalog</c>.</summary>
        public string Label { get; }

        /// <summary>Every recorded fact, in the order it was recorded.</summary>
        public IReadOnlyList<ConformanceTraceEntry> Entries => entries;

        /// <summary>
        /// Records one fact. Recording the same `(table, row, phase, field)` twice throws: two observations of one
        /// fact are two different claims about one world, and a trace that silently kept the last one could not be
        /// compared row by row (P-030's "no observer sees a mixture").
        /// </summary>
        public void Record(string tableId, string rowId, ConformancePhase phase, string field, string value)
        {
            var entry = new ConformanceTraceEntry(tableId, rowId, phase, field, value);
            if (!keys.Add(entry.ToLine().Substring(0, entry.ToLine().IndexOf('='))))
            {
                throw new InvalidOperationException(
                    "the trace already recorded " + entry.TableId + "/" + entry.RowId + "/"
                    + ConformanceTraceEntry.PhaseToken(entry.Phase) + "/" + entry.Field
                    + "; one run observes one fact once (P-030).");
            }

            entries.Add(entry);
        }

        /// <summary>Records one fact if its key is not present yet, and reports whether it was recorded.</summary>
        public bool TryRecord(string tableId, string rowId, ConformancePhase phase, string field, string value)
        {
            var entry = new ConformanceTraceEntry(tableId, rowId, phase, field, value);
            string key = entry.ToLine().Substring(0, entry.ToLine().IndexOf('='));
            if (!keys.Add(key))
            {
                return false;
            }

            entries.Add(entry);
            return true;
        }

        /// <summary>How many facts this run recorded.</summary>
        public int Count => entries.Count;

        /// <summary>The recorded value of one fact, or null when the run never read it.</summary>
        public string? ValueOf(string tableId, string rowId, ConformancePhase phase, string field)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                ConformanceTraceEntry entry = entries[i];
                if (string.Equals(entry.TableId, tableId, StringComparison.Ordinal)
                    && string.Equals(entry.RowId, rowId, StringComparison.Ordinal)
                    && entry.Phase == phase
                    && string.Equals(entry.Field, field, StringComparison.Ordinal))
                {
                    return entry.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// The canonical document text: fixed header, the facts sorted by `(table, row, phase, field)`, then the
        /// digest over the body. Deterministic for a given set of observations (P-008).
        /// </summary>
        public string ToDocument()
        {
            List<ConformanceTraceEntry> sorted = Sorted();
            var body = new StringBuilder();
            int tables = 0;
            int rows = 0;
            string previousTable = string.Empty;
            string previousRow = string.Empty;
            for (int i = 0; i < sorted.Count; i++)
            {
                ConformanceTraceEntry entry = sorted[i];
                if (i != 0)
                {
                    body.Append('\n');
                }

                body.Append(entry.ToLine());
                if (!string.Equals(entry.TableId, previousTable, StringComparison.Ordinal))
                {
                    tables++;
                    previousTable = entry.TableId;
                    previousRow = string.Empty;
                }

                if (!string.Equals(entry.RowId, previousRow, StringComparison.Ordinal))
                {
                    rows++;
                    previousRow = entry.RowId;
                }
            }

            string bodyText = body.ToString();
            var header = new StringBuilder();
            header.Append("format=").Append(FormatName).Append('\n');
            header.Append("label=").Append(Label).Append('\n');
            header.Append("tables=").Append(tables.ToString(CultureInfo.InvariantCulture)).Append('\n');
            header.Append("rows=").Append(rows.ToString(CultureInfo.InvariantCulture)).Append('\n');
            header.Append("facts=").Append(sorted.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            header.Append(bodyText);
            if (bodyText.Length != 0)
            {
                header.Append('\n');
            }

            header.Append("digest=").Append(NarrativeDigest.OfText(bodyText));
            return header.ToString();
        }

        /// <summary>The digest this trace's document would carry (over its canonical body).</summary>
        public string Digest()
        {
            List<ConformanceTraceEntry> sorted = Sorted();
            var body = new StringBuilder();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (i != 0)
                {
                    body.Append('\n');
                }

                body.Append(sorted[i].ToLine());
            }

            return NarrativeDigest.OfText(body.ToString());
        }

        /// <summary>The trace's facts in canonical order, which is the document body's order.</summary>
        public List<ConformanceTraceEntry> Sorted()
        {
            var sorted = new List<ConformanceTraceEntry>(entries);
            sorted.Sort(Compare);
            return sorted;
        }

        private static int Compare(ConformanceTraceEntry left, ConformanceTraceEntry right)
        {
            int order = string.CompareOrdinal(left.TableId, right.TableId);
            if (order != 0)
            {
                return order;
            }

            order = string.CompareOrdinal(left.RowId, right.RowId);
            if (order != 0)
            {
                return order;
            }

            order = left.Phase.CompareTo(right.Phase);
            if (order != 0)
            {
                return order;
            }

            order = string.CompareOrdinal(left.Field, right.Field);
            return order != 0 ? order : string.CompareOrdinal(left.Value, right.Value);
        }

        /// <summary>The document format name; a reader refuses a document that does not declare it (P-054).</summary>
        public const string FormatName = "gamecore.reference-conformance-trace/1";

        /// <summary>
        /// Reads one canonical trace document back. Every structural disagreement is refused with a reason instead of
        /// returning a partially-read trace, and the digest is re-derived and compared, so a tampered document is a
        /// refusal rather than a quietly different observation set (P-054).
        /// </summary>
        public static bool TryParse(
            string? document,
            out ConformanceTrace? trace,
            out string detail)
        {
            trace = null;
            if (document == null)
            {
                detail = "no document was supplied";
                return false;
            }

            string[] lines = document.Replace("\r\n", "\n").Split('\n');
            if (lines.Length < 6)
            {
                detail = "a trace document has a five-line header, a body and a digest line; got "
                    + lines.Length.ToString(CultureInfo.InvariantCulture) + " line(s)";
                return false;
            }

            if (!string.Equals(lines[0], "format=" + FormatName, StringComparison.Ordinal))
            {
                detail = "the document declares '" + lines[0] + "', not 'format=" + FormatName + "'";
                return false;
            }

            if (!lines[1].StartsWith("label=", StringComparison.Ordinal))
            {
                detail = "the second line is '" + lines[1] + "', not the run label";
                return false;
            }

            int facts;
            if (!TryHeaderCount(lines[2], "tables", out int tables)
                || !TryHeaderCount(lines[3], "rows", out int rows)
                || !TryHeaderCount(lines[4], "facts", out facts))
            {
                detail = "the counts header is malformed: '" + lines[2] + "', '" + lines[3] + "', '" + lines[4] + "'";
                return false;
            }

            var body = new StringBuilder();
            var parsed = new ConformanceTrace(lines[1].Substring("label=".Length));
            var seenTables = new List<string>();
            var seenRows = new List<string>();
            string last = string.Empty;
            for (int i = 5; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.StartsWith("digest=", StringComparison.Ordinal))
                {
                    last = line.Substring("digest=".Length);
                    if (i != lines.Length - 1)
                    {
                        detail = "the digest line is not the document's last line";
                        return false;
                    }

                    break;
                }

                if (line.Length == 0)
                {
                    detail = "line " + i.ToString(CultureInfo.InvariantCulture) + " is empty";
                    return false;
                }

                if (!TryParseEntry(line, out ConformanceTraceEntry entry, out detail))
                {
                    return false;
                }

                parsed.entries.Add(entry);
                parsed.keys.Add(line.Substring(0, line.IndexOf('=')));
                if (body.Length != 0)
                {
                    body.Append('\n');
                }

                body.Append(line);
                if (seenTables.Count == 0
                    || !string.Equals(seenTables[seenTables.Count - 1], entry.TableId, StringComparison.Ordinal))
                {
                    seenTables.Add(entry.TableId);
                }

                if (seenRows.Count == 0
                    || !string.Equals(seenRows[seenRows.Count - 1], entry.RowId, StringComparison.Ordinal))
                {
                    seenRows.Add(entry.TableId + "/" + entry.RowId);
                }
            }

            if (last.Length == 0)
            {
                detail = "the document carries no digest line";
                return false;
            }

            string recomputed = NarrativeDigest.OfText(body.ToString());
            if (!string.Equals(recomputed, last, StringComparison.Ordinal))
            {
                detail = "the document's digest " + last + " does not match its body's " + recomputed;
                return false;
            }

            if (parsed.entries.Count != facts)
            {
                detail = "the header declares " + facts.ToString(CultureInfo.InvariantCulture)
                    + " fact(s) and the body carries " + parsed.entries.Count.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            if (seenTables.Count != tables)
            {
                detail = "the header declares " + tables.ToString(CultureInfo.InvariantCulture)
                    + " table(s) and the body carries " + seenTables.Count.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            if (seenRows.Count != rows)
            {
                detail = "the header declares " + rows.ToString(CultureInfo.InvariantCulture)
                    + " row(s) and the body carries " + seenRows.Count.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            trace = parsed;
            detail = "read " + parsed.entries.Count.ToString(CultureInfo.InvariantCulture)
                + " fact(s) over " + seenTables.Count.ToString(CultureInfo.InvariantCulture)
                + " table(s); digest=" + last;
            return true;
        }

        private static bool TryHeaderCount(string line, string name, out int value)
        {
            value = 0;
            string prefix = name + "=";
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            return int.TryParse(
                line.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryParseEntry(string line, out ConformanceTraceEntry entry, out string detail)
        {
            entry = default(ConformanceTraceEntry);
            int first = line.IndexOf('|');
            int second = first < 0 ? -1 : line.IndexOf('|', first + 1);
            int third = second < 0 ? -1 : line.IndexOf('|', second + 1);
            int equals = third < 0 ? -1 : line.IndexOf('=', third + 1);
            if (first <= 0 || second <= first + 1 || third <= second + 1 || equals <= third + 1)
            {
                detail = "the entry '" + line + "' is not <table>|<row>|<phase>|<field>=<value>";
                return false;
            }

            string phaseToken = line.Substring(second + 1, third - second - 1);
            if (!ConformanceTraceEntry.TryParsePhase(phaseToken, out ConformancePhase phase))
            {
                detail = "the entry '" + line + "' names the unknown phase '" + phaseToken + "'";
                return false;
            }

            string value = line.Substring(equals + 1);
            if (!ConformanceValue.IsCanonical(value))
            {
                detail = "the entry '" + line + "' carries a non-canonical value";
                return false;
            }

            entry = new ConformanceTraceEntry(
                line.Substring(0, first),
                line.Substring(first + 1, second - first - 1),
                phase,
                line.Substring(third + 1, equals - third - 1),
                value);
            detail = string.Empty;
            return true;
        }
    }
}

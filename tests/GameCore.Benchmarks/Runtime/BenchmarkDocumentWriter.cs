// GameCore.Benchmarks — the raw per-sample writers of the 08 measurement method.
//
// Normative source: docs/game-core/08-validation-and-performance.md section 3: "Store player logs, failing seed/trace,
// normalized hash outputs, memory/counter data and package locks as implementation artifacts". A summary is not a raw
// artifact: everything a summary states must be recomputable from these two documents, so both carry every sample
// rather than a folded statistic.
//
//   * JSON is the document: the recorded configuration, the per-phase distributions, the counter totals, the memory
//     categories and the gates, plus one entry per sample (ordinal, phase, duration, counter delta).
//   * CSV is the wide matrix: one row per sample and one column per schema counter, so an external tool can re-derive
//     every percentile without this assembly. Its column order is the schema's own id order, so a renamed counter
//     changes a header and never a column position.
//
// Both writers are dependency-free on purpose: this assembly compiles under netstandard2.1, where System.Text.Json is
// not available, and hand-rolling the document keeps the field order fixed and diffable between runs.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Benchmarks
{
    /// <summary>Serializes one workload's raw sample document as JSON and as CSV.</summary>
    public static class BenchmarkDocumentWriter
    {
        /// <summary>Format identifier of the sample document; a reader keys on this, never on the field order.</summary>
        public const string JsonFormat = "gamecore.benchmark.samples/1";

        /// <summary>Format identifier of the wide sample matrix.</summary>
        public const string CsvFormat = "gamecore.benchmark.samples.csv/1";

        /// <summary>Every counter name in schema id order; the CSV header's counter columns.</summary>
        public static IReadOnlyList<string> CounterColumns()
        {
            var names = new string[TelemetrySchema.CounterCount];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = TelemetrySchema.Name((TelemetryCounter)i);
            }

            return names;
        }

        /// <summary>The raw sample document of one workload, as the artifact JSON.</summary>
        public static string WriteJson(BenchmarkRunDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var json = new StringBuilder(4096);
            json.Append("{\n");
            AppendString(json, 1, "artifact", JsonFormat, true);
            AppendString(json, 1, "workload", document.WorkloadId, true);
            AppendString(json, 1, "kind", document.Kind.ToString(), true);
            AppendString(json, 1, "dimension", document.Dimension, true);
            AppendInt(json, 1, "runOrdinal", document.RunOrdinal, true);
            AppendInt(json, 1, "seed", (long)document.Seed, true);
            AppendInt(json, 1, "scopes", document.Scopes, true);
            AppendInt(json, 1, "targets", document.Targets, true);
            AppendInt(json, 1, "warmupSeconds", document.WarmupSeconds, true);
            AppendInt(json, 1, "durationSeconds", document.DurationSeconds, true);
            AppendInt(json, 1, "repetitionsRequested", document.RepetitionsRequested, true);
            AppendInt(json, 1, "repetitionsExecuted", document.RepetitionsExecuted, true);
            AppendInt(json, 1, "stepsAdvanced", document.StepsAdvanced, true);
            AppendInt(json, 1, "windowMicroseconds", document.WindowMicroseconds, true);
            AppendInt(json, 1, "warmupMicroseconds", document.WarmupMicroseconds, true);
            AppendBool(json, 1, "passed", document.Passed, true);

            var gates = new List<string>(document.Gates.Count);
            for (int i = 0; i < document.Gates.Count; i++)
            {
                BenchmarkGateResult gate = document.Gates[i];
                var item = new StringBuilder();
                item.Append(Indent(2)).Append("{\n");
                AppendString(item, 3, "name", gate.Name, true);
                AppendBool(item, 3, "passed", gate.Passed, true);
                AppendString(item, 3, "detail", gate.Detail, false);
                item.Append(Indent(2)).Append('}');
                gates.Add(item.ToString());
            }

            AppendArray(json, 1, "gates", gates, true);

            var notes = new List<string>(document.Notes.Count);
            for (int i = 0; i < document.Notes.Count; i++)
            {
                notes.Add(Indent(2) + "\"" + Escape(document.Notes[i]) + "\"");
            }

            AppendArray(json, 1, "notes", notes, true);

            var phases = new List<string>();
            for (int p = 0; p < PhaseOrder.Count; p++)
            {
                BenchmarkPhase phase = PhaseOrder[p];
                List<long> durations = document.DurationsOf(phase);
                if (durations.Count == 0)
                {
                    continue;
                }

                BenchmarkDistribution distribution = BenchmarkDistribution.Of(durations);
                var item = new StringBuilder();
                item.Append(Indent(2)).Append("{\n");
                AppendString(item, 3, "phase", phase.ToString(), true);
                AppendInt(item, 3, "count", distribution.Count, true);
                AppendInt(item, 3, "min", distribution.Min, true);
                AppendInt(item, 3, "p50", distribution.P50, true);
                AppendInt(item, 3, "p95", distribution.P95, true);
                AppendInt(item, 3, "p99", distribution.P99, true);
                AppendInt(item, 3, "max", distribution.Max, true);
                AppendNumber(item, 3, "mean", distribution.Mean, true);
                AppendInt(item, 3, "total", distribution.Total, false);
                item.Append(Indent(2)).Append('}');
                phases.Add(item.ToString());
            }

            AppendArray(json, 1, "phases", phases, true);

            BenchmarkMemoryCategories memory = document.Memory;
            var memoryBody = new StringBuilder();
            memoryBody.Append(Indent(1)).Append("\"memory\": {\n");
            AppendInt(memoryBody, 2, "managedHeapStartBytes", memory.ManagedHeapStartBytes, true);
            AppendInt(memoryBody, 2, "managedHeapEndBytes", memory.ManagedHeapBytes, true);
            AppendInt(memoryBody, 2, "managedAllocatedBytes", memory.ManagedAllocatedBytes, true);
            AppendBool(memoryBody, 2, "managedAllocationIsThreadComplete", memory.ManagedAllocationIsThreadComplete, true);
            AppendInt(memoryBody, 2, "nativeContainerBytes", memory.NativeContainerBytes, true);
            AppendInt(memoryBody, 2, "leaseBytes", memory.LeaseBytes, true);
            AppendInt(memoryBody, 2, "retainedEventBytes", memory.RetainedEventBytes, true);
            AppendInt(memoryBody, 2, "retainedEventCount", memory.RetainedEventCount, true);
            AppendInt(memoryBody, 2, "cacheBytes", memory.CacheBytes, true);
            AppendInt(memoryBody, 2, "cacheEntries", memory.CacheEntries, true);
            AppendInt(memoryBody, 2, "quarantineBytes", memory.QuarantineBytes, true);
            AppendInt(memoryBody, 2, "quarantineEntries", memory.QuarantineEntries, true);
            AppendInt(memoryBody, 2, "liveLeases", memory.LiveLeases, true);
            AppendInt(memoryBody, 2, "outstandingCallbacks", memory.OutstandingCallbacks, false);
            memoryBody.Append(Indent(1)).Append("},\n");
            json.Append(memoryBody);

            // `counters` is an OBJECT, not an array: it is a map from a counter's wire name to its value, and the
            // summarizer's budget join looks a counter up by that name. Rendering it as an array of `"name": value`
            // members would not be valid JSON, so the members are joined into braces here rather than reusing the
            // array helper the list-valued fields use.
            var counters = new List<string>();
            for (int i = 0; i < TelemetrySchema.CounterCount; i++)
            {
                var counter = (TelemetryCounter)i;
                long value = document.Totals.Get(counter);
                if (value == 0L)
                {
                    continue;
                }

                counters.Add(Indent(2) + "\"" + TelemetrySchema.Name(counter) + "\": "
                    + value.ToString(CultureInfo.InvariantCulture));
            }

            json.Append(Indent(1)).Append("\"counters\": {");
            for (int i = 0; i < counters.Count; i++)
            {
                json.Append(i == 0 ? "\n" : ",\n").Append(counters[i]);
            }

            if (counters.Count != 0)
            {
                json.Append('\n').Append(Indent(1));
            }

            json.Append("},\n");

            var samples = new List<string>(document.Samples.Count);
            for (int i = 0; i < document.Samples.Count; i++)
            {
                BenchmarkSample sample = document.Samples[i];
                var item = new StringBuilder();
                item.Append(Indent(2)).Append("{\n");
                AppendInt(item, 3, "ordinal", sample.Ordinal, true);
                AppendString(item, 3, "phase", sample.Phase.ToString(), true);
                AppendInt(item, 3, "microseconds", sample.Microseconds, true);
                AppendString(
                    item,
                    3,
                    "counters",
                    sample.Counters == null ? "0" : sample.Counters.Describe(),
                    false);
                item.Append(Indent(2)).Append('}');
                samples.Add(item.ToString());
            }

            AppendArray(json, 1, "samples", samples, false);
            json.Append("}\n");
            return json.ToString();
        }

        /// <summary>
        /// Appends one JSON array whose items are already rendered objects or scalars. An empty list is written as
        /// `[]`, so a reader never has to special-case a trailing comma and two documents diff cleanly.
        /// </summary>
        private static void AppendArray(
            StringBuilder json,
            int depth,
            string name,
            IReadOnlyList<string> items,
            bool trailingComma)
        {
            json.Append(Indent(depth)).Append('"').Append(name).Append("\": [");
            for (int i = 0; i < items.Count; i++)
            {
                json.Append(i == 0 ? "\n" : ",\n").Append(items[i]);
            }

            if (items.Count != 0)
            {
                json.Append('\n').Append(Indent(depth));
            }

            json.Append(']').Append(trailingComma ? ",\n" : "\n");
        }

        /// <summary>
        /// The wide raw matrix: one comment line naming the format and the run, one header row naming every counter in
        /// schema id order, then one row per sample.
        /// </summary>
        public static string WriteCsv(BenchmarkRunDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var csv = new StringBuilder(1024 + (document.Samples.Count * 96));
            csv.Append("# ").Append(CsvFormat)
                .Append(" workload=").Append(document.WorkloadId)
                .Append(" run=").Append(document.RunOrdinal.ToString(CultureInfo.InvariantCulture))
                .Append(" seed=").Append(document.Seed.ToString(CultureInfo.InvariantCulture))
                .Append(" scopes=").Append(document.Scopes.ToString(CultureInfo.InvariantCulture))
                .Append(" targets=").Append(document.Targets.ToString(CultureInfo.InvariantCulture))
                .Append(" warmupSeconds=").Append(document.WarmupSeconds.ToString(CultureInfo.InvariantCulture))
                .Append(" durationSeconds=").Append(document.DurationSeconds.ToString(CultureInfo.InvariantCulture))
                .Append(" repetitions=").Append(document.RepetitionsExecuted.ToString(CultureInfo.InvariantCulture))
                .Append('\n');

            csv.Append("workload,phase,ordinal,microseconds");
            IReadOnlyList<string> columns = CounterColumns();
            for (int i = 0; i < columns.Count; i++)
            {
                csv.Append(',').Append(columns[i]);
            }

            csv.Append('\n');

            for (int s = 0; s < document.Samples.Count; s++)
            {
                BenchmarkSample sample = document.Samples[s];
                csv.Append(document.WorkloadId).Append(',')
                    .Append(sample.Phase.ToString()).Append(',')
                    .Append(sample.Ordinal.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(sample.Microseconds.ToString(CultureInfo.InvariantCulture));

                for (int i = 0; i < TelemetrySchema.CounterCount; i++)
                {
                    long value = sample.Counters == null ? 0L : sample.Counters.Get((TelemetryCounter)i);
                    csv.Append(',').Append(value.ToString(CultureInfo.InvariantCulture));
                }

                csv.Append('\n');
            }

            return csv.ToString();
        }

        /// <summary>
        /// The document's per-phase distributions keyed as <c>phase/metric</c>, so a caller can compare a measured
        /// number to a budget row without re-deriving anything.
        /// </summary>
        public static Dictionary<string, double> MetricsOf(BenchmarkRunDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var metrics = new Dictionary<string, double>(StringComparer.Ordinal);
            for (int p = 0; p < PhaseOrder.Count; p++)
            {
                BenchmarkPhase phase = PhaseOrder[p];
                List<long> samples = document.DurationsOf(phase);
                if (samples.Count == 0)
                {
                    continue;
                }

                BenchmarkDistribution distribution = BenchmarkDistribution.Of(samples);
                for (int m = 0; m < SummaryMetrics.Length; m++)
                {
                    string metric = SummaryMetrics[m];
                    metrics[phase.ToString() + "/" + metric] = distribution.Metric(metric);
                }
            }

            return metrics;
        }

        /// <summary>The report order of the phases; every phase list follows it so two documents are comparable.</summary>
        public static readonly IReadOnlyList<BenchmarkPhase> PhaseOrder = Array.AsReadOnly(new[]
        {
            BenchmarkPhase.Warmup,
            BenchmarkPhase.Prepare,
            BenchmarkPhase.Wait,
            BenchmarkPhase.Apply,
            BenchmarkPhase.EndToEnd,
            BenchmarkPhase.Step,
            BenchmarkPhase.Change,
        });

        /// <summary>The distribution metrics a pipeline reports for every phase.</summary>
        public static readonly IReadOnlyList<string> SummaryMetrics = Array.AsReadOnly(new[]
        {
            BenchmarkMetrics.Count,
            BenchmarkMetrics.P50,
            BenchmarkMetrics.P95,
            BenchmarkMetrics.P99,
            BenchmarkMetrics.Max,
            BenchmarkMetrics.Mean,
        });

        private static string Indent(int depth) => new string(' ', depth * 2);

        private static void AppendString(StringBuilder json, int depth, string name, string value, bool trailingComma)
        {
            json.Append(Indent(depth)).Append('"').Append(name).Append("\": \"").Append(Escape(value))
                .Append(trailingComma ? "\",\n" : "\"\n");
        }

        private static void AppendInt(StringBuilder json, int depth, string name, long value, bool trailingComma)
        {
            json.Append(Indent(depth)).Append('"').Append(name).Append("\": ")
                .Append(value.ToString(CultureInfo.InvariantCulture))
                .Append(trailingComma ? ",\n" : "\n");
        }

        private static void AppendNumber(StringBuilder json, int depth, string name, double value, bool trailingComma)
        {
            json.Append(Indent(depth)).Append('"').Append(name).Append("\": ")
                .Append(value.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(trailingComma ? ",\n" : "\n");
        }

        private static void AppendBool(StringBuilder json, int depth, string name, bool value, bool trailingComma)
        {
            json.Append(Indent(depth)).Append('"').Append(name).Append("\": ")
                .Append(value ? "true" : "false")
                .Append(trailingComma ? ",\n" : "\n");
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var escaped = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                switch (character)
                {
                    case '"':
                        escaped.Append("\\\"");
                        break;

                    case '\\':
                        escaped.Append("\\\\");
                        break;

                    case '\n':
                        escaped.Append("\\n");
                        break;

                    case '\r':
                        escaped.Append("\\r");
                        break;

                    case '\t':
                        escaped.Append("\\t");
                        break;

                    default:
                        if (character < ' ')
                        {
                            escaped.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            escaped.Append(character);
                        }

                        break;
                }
            }

            return escaped.ToString();
        }
    }
}

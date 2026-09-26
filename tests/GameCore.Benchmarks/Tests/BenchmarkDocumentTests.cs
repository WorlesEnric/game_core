// GameCore.Benchmarks tests — the raw per-sample JSON document and the wide CSV matrix (GC-026).
//
// 08 asks for the raw per-sample artifact, not a summary: everything a summary states has to be recomputable from
// these two documents. This suite pins the document's identity (format literal, workload, gates, notes), that a
// phase with no samples contributes no phase entry (an empty distribution is not a zero measurement), that only
// non-zero counters are written, that the CSV header's counter columns follow the compact schema's own id order,
// and that both writers are byte-stable between calls on one document.
//
// The JSON is checked by a small hand-written validator rather than by System.Text.Json: this assembly compiles
// under netstandard2.1, where System.Text.Json is not available, and the point of the check is precisely that the
// hand-rolled writer produces a balanced document without a trailing comma.
//
// Sources in this folder run as plain-dotnet tests and as Unity EditMode tests.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using NUnit.Framework;

namespace GameCore.Benchmarks.Tests
{
    [TestFixture]
    public sealed class BenchmarkDocumentTests
    {
        [Test]
        public void TheJsonDocumentIsValidJsonAndNamesItsFormatWorkloadGatesAndNotes()
        {
            BenchmarkRunDocument document = BuildDocument();
            string json = BenchmarkDocumentWriter.WriteJson(document);

            AssertValidJson(json);
            Assert.That(json.Contains("\"artifact\": \"" + BenchmarkDocumentWriter.JsonFormat + "\"", StringComparison.Ordinal), Is.True);
            Assert.That(json.Contains("\"workload\": \"update-size-1\"", StringComparison.Ordinal), Is.True);
            Assert.That(json.Contains("\"kind\": \"Change\"", StringComparison.Ordinal), Is.True);
            Assert.That(json.Contains("\"dimension\": \"update-size\"", StringComparison.Ordinal), Is.True);
            Assert.That(json.Contains("\"seed\": 20260926", StringComparison.Ordinal), Is.True);
            Assert.That(json.Contains("\"targets\": 10000", StringComparison.Ordinal), Is.True);
            Assert.That(json.Contains("\"repetitionsExecuted\": 1000", StringComparison.Ordinal), Is.True);
            Assert.That(json.Contains("\"passed\": false", StringComparison.Ordinal), Is.True, "one gate failed, so the workload did not pass");

            for (int i = 0; i < document.Gates.Count; i++)
            {
                Assert.That(
                    json.Contains("\"name\": \"" + document.Gates[i].Name + "\"", StringComparison.Ordinal),
                    Is.True,
                    "gate " + i.ToString() + " is named in the document");
                Assert.That(
                    json.Contains(document.Gates[i].Detail, StringComparison.Ordinal),
                    Is.True,
                    "gate " + i.ToString() + " keeps its detail beside its flag");
            }

            Assert.That(CountOccurrences(json, "\"notes\": ["), Is.EqualTo(1));
            Assert.That(json.Contains("\"updateSize=1;repetition=0\"", StringComparison.Ordinal), Is.True);
            Assert.That(
                json.Contains("\"escaped \\\"note\\\" with a \\\\ backslash\"", StringComparison.Ordinal),
                Is.True,
                "a quote and a backslash in a note are escaped rather than written raw");

            // A phase field appears twice per sampled phase: once in its distribution entry and once per sample. The
            // entry is the one that owns a `count`, so that is what proves "one entry per phase that has samples".
            int sampledPhases = 0;
            for (int p = 0; p < BenchmarkDocumentWriter.PhaseOrder.Count; p++)
            {
                if (document.DurationsOf(BenchmarkDocumentWriter.PhaseOrder[p]).Count > 0)
                {
                    sampledPhases++;
                }
            }

            Assert.That(sampledPhases, Is.EqualTo(2), "this fixture samples exactly the Step and Apply phases");
            Assert.That(
                CountOccurrences(json, "\"count\": "),
                Is.EqualTo(sampledPhases),
                "one distribution entry per phase that has samples, and none for a phase that has none");
            Assert.That(
                CountOccurrences(json, "\"phase\": \"Step\""),
                Is.EqualTo(1 + document.DurationsOf(BenchmarkPhase.Step).Count),
                "the Step distribution entry plus one phase field per Step sample");
            Assert.That(
                CountOccurrences(json, "\"phase\": \"Apply\""),
                Is.EqualTo(1 + document.DurationsOf(BenchmarkPhase.Apply).Count),
                "the Apply distribution entry plus one phase field per Apply sample");
            Assert.That(CountOccurrences(json, "\"phase\": \"Wait\""), Is.EqualTo(0), "a phase with no samples contributes no entry and no sample field");
            Assert.That(CountOccurrences(json, "\"phase\": \"Warmup\""), Is.EqualTo(0));
            Assert.That(CountOccurrences(json, "\"phase\": \"EndToEnd\""), Is.EqualTo(0));
            Assert.That(CountOccurrences(json, "\"phase\": \"Change\""), Is.EqualTo(0));

            Assert.That(CountOccurrences(json, "\"memory\": {"), Is.EqualTo(1), "the memory split is one object, not a process total");
            string[] memoryFields =
            {
                "managedHeapStartBytes", "managedHeapEndBytes", "managedAllocatedBytes",
                "managedAllocationIsThreadComplete", "nativeContainerBytes", "leaseBytes", "retainedEventBytes",
                "retainedEventCount", "cacheBytes", "cacheEntries", "quarantineBytes", "quarantineEntries",
                "liveLeases", "outstandingCallbacks",
            };
            for (int i = 0; i < memoryFields.Length; i++)
            {
                Assert.That(
                    json.Contains("\"" + memoryFields[i] + "\": ", StringComparison.Ordinal),
                    Is.True,
                    "the memory object carries the frozen field " + memoryFields[i]);
            }

            Assert.That(CountOccurrences(json, "\"min\": "), Is.EqualTo(2), "each sampled phase reports a min");
            Assert.That(CountOccurrences(json, "\"mean\": "), Is.EqualTo(2), "each sampled phase reports a mean");
            Assert.That(CountOccurrences(json, "\"total\": "), Is.EqualTo(2), "each sampled phase reports a total");
        }

        [Test]
        public void TheJsonValidatorItselfRejectsAMalformedDocument()
        {
            // The document check above is only worth something if the validator can fail, so it is exercised on
            // three defects a hand-rolled writer really has: a trailing comma, an unclosed bracket and an
            // unterminated string.
            AssertValidJson("{ \"a\": [1, 2], \"b\": {\"c\": true} }");
            Assert.Throws<AssertionException>(() => AssertValidJson("{ \"a\": 1, }"), "a trailing comma before a closer is invalid");
            Assert.Throws<AssertionException>(() => AssertValidJson("{ \"a\": [1, 2]"), "an unclosed array is invalid");
            Assert.Throws<AssertionException>(() => AssertValidJson("{ \"a\": \"unterminated }"), "an unterminated string is invalid");
            Assert.Throws<AssertionException>(() => AssertValidJson("{ \"a\": [1, 2 }, }"), "a mismatched closer is invalid");
            Assert.Throws<AssertionException>(() => AssertValidJson("{\"a\": 1, \"b\": [2, 3,] }"), "a trailing comma inside an array is invalid");
        }

        [Test]
        public void TheJsonDocumentWritesOnlyNonZeroCountersAndOneSampleDeltaPerSample()
        {
            BenchmarkRunDocument document = BuildDocument();
            string json = BenchmarkDocumentWriter.WriteJson(document);

            Assert.That(CountOccurrences(json, "\"control-nodes-visited\": "), Is.EqualTo(1), "a non-zero total counter is written once");
            Assert.That(json.Contains("\"control-nodes-visited\": 17", StringComparison.Ordinal), Is.True);
            Assert.That(json.Contains("\"contributions-added\": 11", StringComparison.Ordinal), Is.True);
            Assert.That(CountOccurrences(json, "\"service-string-lookups\": "), Is.EqualTo(0), "a zero counter is omitted from the counters object");
            Assert.That(CountOccurrences(json, "\"stale-results\": "), Is.EqualTo(0));
            Assert.That(CountOccurrences(json, "\"quarantine-entries\": "), Is.EqualTo(0), "the counters object is the workload total, not the memory split");

            Assert.That(
                json.Contains("\"counters\": \"control-nodes-visited=3;candidates-matched=7\"", StringComparison.Ordinal),
                Is.True,
                "a sample keeps the counter delta it observed, in schema order");
            Assert.That(
                CountOccurrences(json, "\"counters\": \"0\""),
                Is.EqualTo(3),
                "a time-only sample reports no delta rather than an invented one");
        }

        [Test]
        public void BothWritersAreStableBetweenCallsOnOneDocument()
        {
            BenchmarkRunDocument document = BuildDocument();
            string firstJson = BenchmarkDocumentWriter.WriteJson(document);
            string secondJson = BenchmarkDocumentWriter.WriteJson(document);
            string firstCsv = BenchmarkDocumentWriter.WriteCsv(document);
            string secondCsv = BenchmarkDocumentWriter.WriteCsv(document);

            Assert.That(secondJson, Is.EqualTo(firstJson), "two writes of one document are byte-identical");
            Assert.That(secondCsv, Is.EqualTo(firstCsv), "two CSV writes of one document are byte-identical");

            BenchmarkRunDocument changed = BuildDocument();
            changed.Add(new BenchmarkSample(3, BenchmarkPhase.Step, 400L, null));
            string changedJson = BenchmarkDocumentWriter.WriteJson(changed);
            Assert.That(changedJson, Is.Not.EqualTo(firstJson), "an extra sample really changes the document");
            Assert.That(BenchmarkDocumentWriter.WriteCsv(changed), Is.Not.EqualTo(firstCsv));
        }

        [Test]
        public void TheCsvMatrixNamesItsFormatAndWritesOneRowAndEveryCounterColumnPerSample()
        {
            BenchmarkRunDocument document = BuildDocument();
            string csv = BenchmarkDocumentWriter.WriteCsv(document);
            string[] lines = SplitLines(csv);

            Assert.That(lines.Length, Is.EqualTo(2 + document.Samples.Count), "a comment, a header and one row per sample");
            Assert.That(
                lines[0].StartsWith("# " + BenchmarkDocumentWriter.CsvFormat, StringComparison.Ordinal),
                Is.True,
                "the first line is the format comment an external reader keys on");
            Assert.That(lines[0].Contains("workload=update-size-1", StringComparison.Ordinal), Is.True);
            Assert.That(lines[0].Contains("run=2", StringComparison.Ordinal), Is.True);
            Assert.That(lines[0].Contains("seed=20260926", StringComparison.Ordinal), Is.True);
            Assert.That(lines[0].Contains("scopes=1000", StringComparison.Ordinal), Is.True);
            Assert.That(lines[0].Contains("targets=10000", StringComparison.Ordinal), Is.True);
            Assert.That(lines[0].Contains("warmupSeconds=30", StringComparison.Ordinal), Is.True);
            Assert.That(lines[0].Contains("repetitions=1000", StringComparison.Ordinal), Is.True);
            Assert.That(lines[0].Contains("durationSeconds=0", StringComparison.Ordinal), Is.True);

            IReadOnlyList<string> columns = BenchmarkDocumentWriter.CounterColumns();
            Assert.That(columns.Count, Is.EqualTo(TelemetrySchema.CounterCount), "one column per counter of the compact schema");
            for (int i = 0; i < columns.Count; i++)
            {
                Assert.That(columns[i], Is.EqualTo(TelemetrySchema.Name((TelemetryCounter)i)), "the columns follow the schema's id order");
            }

            var seen = new HashSet<string>();
            for (int i = 0; i < columns.Count; i++)
            {
                Assert.That(seen.Add(columns[i]), Is.True, "counter column " + i.ToString() + " is a distinct name");
            }

            string expectedHeader = "workload,phase,ordinal,microseconds," + string.Join(",", columns);
            Assert.That(lines[1], Is.EqualTo(expectedHeader), "the header is the fixed four columns then the counter columns");

            for (int s = 0; s < document.Samples.Count; s++)
            {
                BenchmarkSample sample = document.Samples[s];
                string[] fields = lines[2 + s].Split(',');
                Assert.That(fields.Length, Is.EqualTo(4 + TelemetrySchema.CounterCount), "sample row " + s.ToString() + " is as wide as the header");
                Assert.That(fields[0], Is.EqualTo(document.WorkloadId));
                Assert.That(fields[1], Is.EqualTo(sample.Phase.ToString()));
                Assert.That(fields[2], Is.EqualTo(sample.Ordinal.ToString(CultureInfo.InvariantCulture)));
                Assert.That(fields[3], Is.EqualTo(sample.Microseconds.ToString(CultureInfo.InvariantCulture)));
                for (int i = 0; i < TelemetrySchema.CounterCount; i++)
                {
                    long expected = sample.Counters == null ? 0L : sample.Counters.Get((TelemetryCounter)i);
                    Assert.That(
                        long.Parse(fields[4 + i], CultureInfo.InvariantCulture),
                        Is.EqualTo(expected),
                        "sample " + s.ToString() + " counter " + columns[i]);
                }
            }
        }

        [Test]
        public void MetricsOfKeysArePhaseSlashMetricAndOnlySampledPhasesAppear()
        {
            BenchmarkRunDocument document = BuildDocument();
            Dictionary<string, double> metrics = BenchmarkDocumentWriter.MetricsOf(document);

            Assert.That(BenchmarkDocumentWriter.SummaryMetrics, Is.EqualTo(new[]
            {
                BenchmarkMetrics.Count,
                BenchmarkMetrics.P50,
                BenchmarkMetrics.P95,
                BenchmarkMetrics.P99,
                BenchmarkMetrics.Max,
                BenchmarkMetrics.Mean,
            }), "the summary metrics are the ones 08 asks a run to report");
            Assert.That(BenchmarkDocumentWriter.PhaseOrder, Is.EqualTo(new[]
            {
                BenchmarkPhase.Warmup,
                BenchmarkPhase.Prepare,
                BenchmarkPhase.Wait,
                BenchmarkPhase.Apply,
                BenchmarkPhase.EndToEnd,
                BenchmarkPhase.Step,
                BenchmarkPhase.Change,
            }), "the phase report order is fixed so two documents compare");

            Assert.That(
                metrics.Count,
                Is.EqualTo(2 * BenchmarkDocumentWriter.SummaryMetrics.Count),
                "only Step and Apply produced samples, so only their metrics exist");
            Assert.That(metrics.ContainsKey("Step/" + BenchmarkMetrics.Count), Is.True);
            Assert.That(metrics.ContainsKey("Step/" + BenchmarkMetrics.P50), Is.True);
            Assert.That(metrics.ContainsKey("Apply/" + BenchmarkMetrics.P99), Is.True);
            Assert.That(metrics.ContainsKey("Wait/" + BenchmarkMetrics.P95), Is.False, "an unsampled phase has no key");
            Assert.That(metrics.ContainsKey("Step"), Is.False, "the key is phase/metric, not one or the other");

            Assert.That(metrics["Step/" + BenchmarkMetrics.Count], Is.EqualTo(3.0));
            Assert.That(metrics["Step/" + BenchmarkMetrics.P50], Is.EqualTo(200.0));
            Assert.That(metrics["Step/" + BenchmarkMetrics.P95], Is.EqualTo(300.0));
            Assert.That(metrics["Step/" + BenchmarkMetrics.P99], Is.EqualTo(300.0));
            Assert.That(metrics["Step/" + BenchmarkMetrics.Max], Is.EqualTo(300.0));
            Assert.That(metrics["Step/" + BenchmarkMetrics.Mean], Is.EqualTo(200.0));
            Assert.That(metrics["Apply/" + BenchmarkMetrics.P50], Is.EqualTo(900.0));
            Assert.That(metrics["Apply/" + BenchmarkMetrics.Count], Is.EqualTo(1.0));

            // The metrics are exactly the document's own distribution, recomputed here.
            BenchmarkDistribution step = BenchmarkDistribution.Of(document.DurationsOf(BenchmarkPhase.Step));
            Assert.That(metrics["Step/" + BenchmarkMetrics.Max], Is.EqualTo((double)step.Max));
            Assert.That(metrics["Step/" + BenchmarkMetrics.Mean], Is.EqualTo(step.Mean));
        }

        [Test]
        public void TheWritersAndTheMetricReaderRejectAMissingDocument()
        {
            Assert.Throws<ArgumentNullException>(() => BenchmarkDocumentWriter.WriteJson(null!));
            Assert.Throws<ArgumentNullException>(() => BenchmarkDocumentWriter.WriteCsv(null!));
            Assert.Throws<ArgumentNullException>(() => BenchmarkDocumentWriter.MetricsOf(null!));
        }

        private static BenchmarkRunDocument BuildDocument()
        {
            var document = new BenchmarkRunDocument(
                BenchmarkWorkloads.UpdateSizeOne,
                BenchmarkWorkloadKind.Change,
                "update-size",
                1000,
                10000,
                0,
                30,
                1000,
                BenchmarkWorkloads.DefaultSeed,
                2);
            document.RepetitionsExecuted = 1000;
            document.StepsAdvanced = 10000L;
            document.WindowMicroseconds = 1000000L;
            document.Note("updateSize=1;repetition=0");
            document.Note("escaped \"note\" with a \\ backslash");
            document.Add(new BenchmarkGateResult("gate.affected-targets", true, "affected=1;candidates=10000"));
            document.Add(new BenchmarkGateResult("gate.budget-apply-pause", false, "p95=2500us;target=2000us"));

            var stepDelta = new TelemetryCounterSet();
            stepDelta.Set(TelemetryCounter.ControlNodesVisited, 3L);
            stepDelta.Set(TelemetryCounter.CandidatesMatched, 7L);
            document.Add(new BenchmarkSample(0, BenchmarkPhase.Step, 100L, stepDelta));
            document.Add(new BenchmarkSample(1, BenchmarkPhase.Step, 300L, null));
            document.Add(new BenchmarkSample(2, BenchmarkPhase.Step, 200L, null));
            document.Add(new BenchmarkSample(0, BenchmarkPhase.Apply, 900L, null));

            var totals = new TelemetryCounterSet();
            totals.Set(TelemetryCounter.ControlNodesVisited, 17L);
            totals.Set(TelemetryCounter.ContributionsAdded, 11L);
            document.SetTotals(totals);

            document.Memory = BenchmarkMemoryCategories.FromCounters(
                counters: MemoryCounters(),
                nativeContainerBytes: 21L,
                managedAllocatedBytes: 22L,
                managedThreadComplete: true,
                managedHeapStartBytes: 23L,
                managedHeapEndBytes: 24L);
            return document;
        }

        private static TelemetryCounterSet MemoryCounters()
        {
            var counters = new TelemetryCounterSet();
            counters.Set(TelemetryCounter.LeaseBytes, 11L);
            counters.Set(TelemetryCounter.RetainedEventBytes, 12L);
            counters.Set(TelemetryCounter.RetainedEventCount, 13L);
            counters.Set(TelemetryCounter.CacheBytes, 14L);
            counters.Set(TelemetryCounter.CacheEntries, 15L);
            counters.Set(TelemetryCounter.QuarantineBytes, 16L);
            counters.Set(TelemetryCounter.QuarantineEntries, 17L);
            counters.Set(TelemetryCounter.LiveLeases, 18L);
            counters.Set(TelemetryCounter.OutstandingCallbacks, 19L);
            return counters;
        }

        private static string[] SplitLines(string text)
        {
            var lines = new List<string>();
            string[] raw = text.Split('\n');
            for (int i = 0; i < raw.Length; i++)
            {
                string line = raw[i];
                if (line.EndsWith("\r", StringComparison.Ordinal))
                {
                    line = line.Substring(0, line.Length - 1);
                }

                if (line.Length != 0)
                {
                    lines.Add(line);
                }
            }

            return lines.ToArray();
        }

        private static int CountOccurrences(string text, string needle)
        {
            int count = 0;
            int index = 0;
            while (true)
            {
                int found = text.IndexOf(needle, index, StringComparison.Ordinal);
                if (found < 0)
                {
                    return count;
                }

                count++;
                index = found + needle.Length;
            }
        }

        /// <summary>
        /// The smallest useful JSON check: balanced braces/brackets outside strings, closed strings, and no comma
        /// directly before a closer. It fails loudly, which is what makes the document test above non-vacuous.
        /// </summary>
        private static void AssertValidJson(string json)
        {
            var open = new Stack<char>();
            bool inString = false;
            bool escaped = false;
            for (int i = 0; i < json.Length; i++)
            {
                char character = json[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (character == '\\')
                    {
                        escaped = true;
                    }
                    else if (character == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (character == '"')
                {
                    inString = true;
                    continue;
                }

                if (character == '{' || character == '[')
                {
                    open.Push(character);
                    continue;
                }

                if (character == '}' || character == ']')
                {
                    char expected = character == '}' ? '{' : '[';
                    Assert.That(
                        open.Count > 0 && open.Peek() == expected,
                        Is.True,
                        "closer '" + character.ToString() + "' at offset " + i.ToString() + " matches its opener");
                    open.Pop();
                    continue;
                }

                if (character == ',')
                {
                    int next = NextSignificant(json, i + 1);
                    Assert.That(next >= 0, Is.True, "a comma at offset " + i.ToString() + " is followed by a value");
                    Assert.That(
                        json[next] != '}' && json[next] != ']',
                        Is.True,
                        "no trailing comma before a closer at offset " + i.ToString());
                }
            }

            Assert.That(inString, Is.False, "every string is terminated");
            Assert.That(open.Count, Is.EqualTo(0), "every brace and bracket is closed");
            Assert.That(json.EndsWith("}", StringComparison.Ordinal), Is.True, "the document is one object");
        }

        private static int NextSignificant(string text, int from)
        {
            for (int i = from; i < text.Length; i++)
            {
                if (!char.IsWhiteSpace(text[i]))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}

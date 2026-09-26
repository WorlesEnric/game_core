// GameCore.Benchmarks — nearest-rank percentiles and the per-phase distribution of 08's measurement method.
//
// Normative source: docs/game-core/08-validation-and-performance.md section 3: "Collect per-sample p50/p95/p99/max and
// total counts, not only averages or FPS."
//
// Two decisions here are deliberate:
//
//   1. **Nearest-rank, documented and single-definition.** Interpolating percentiles differ between tools; a budget
//      comparison that changes its definition between runs is not evidence. `Percentile` is the nearest-rank
//      definition: with N samples sorted ascending, the p-th percentile is the element at index ceil(p/100 * N) - 1,
//      clamped. p50/p95/p99/max then come from one function, and the fixture tests pin the definition on vectors
//      whose answers are checkable by hand.
//   2. **The distribution is computed from the samples, not accumulated.** A running mean cannot produce a p99, and a
//      summary that cannot be recomputed from the raw document is not reproducible.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameCore.Benchmarks
{
    /// <summary>
    /// count/min/mean/p50/p95/p99/max/total of one phase's samples, in microseconds. An empty distribution reports
    /// <see cref="IsEmpty"/> and its percentiles are zero; a caller must check <see cref="IsEmpty"/> before treating a
    /// percentile as a measurement, which is why the summarizer compares to budgets through
    /// <see cref="PerformanceBudget.Verdict"/> using NaN for "no measurement".
    /// </summary>
    public readonly struct BenchmarkDistribution
    {
        private BenchmarkDistribution(
            int count,
            long min,
            long max,
            double mean,
            long total,
            long p50,
            long p95,
            long p99)
        {
            Count = count;
            Min = min;
            Max = max;
            Mean = mean;
            Total = total;
            P50 = p50;
            P95 = p95;
            P99 = p99;
        }

        public int Count { get; }

        public long Min { get; }

        public long Max { get; }

        public double Mean { get; }

        public long Total { get; }

        public long P50 { get; }

        public long P95 { get; }

        public long P99 { get; }

        public bool IsEmpty => Count == 0;

        /// <summary>Builds the distribution of <paramref name="samples"/>. The input is not modified.</summary>
        public static BenchmarkDistribution Of(IReadOnlyList<long> samples)
        {
            if (samples == null)
            {
                throw new ArgumentNullException(nameof(samples));
            }

            if (samples.Count == 0)
            {
                return new BenchmarkDistribution(0, 0L, 0L, 0.0, 0L, 0L, 0L, 0L);
            }

            var sorted = new long[samples.Count];
            for (int i = 0; i < samples.Count; i++)
            {
                long value = samples[i];
                if (value < 0L)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(samples),
                        "A duration sample is never negative; a negative duration is a clock or accounting defect.");
                }

                sorted[i] = value;
            }

            Array.Sort(sorted);

            long total = 0L;
            for (int i = 0; i < sorted.Length; i++)
            {
                total += sorted[i];
            }

            double mean = (double)total / sorted.Length;
            return new BenchmarkDistribution(
                sorted.Length,
                sorted[0],
                sorted[sorted.Length - 1],
                mean,
                total,
                BenchmarkStatistics.PercentileOfSorted(sorted, 50.0),
                BenchmarkStatistics.PercentileOfSorted(sorted, 95.0),
                BenchmarkStatistics.PercentileOfSorted(sorted, 99.0));
        }

        /// <summary>One named metric of this distribution; used by the budget comparison and the summary table.</summary>
        public double Metric(string metric)
        {
            switch (metric)
            {
                case BenchmarkMetrics.P50:
                    return P50;

                case BenchmarkMetrics.P95:
                    return P95;

                case BenchmarkMetrics.P99:
                    return P99;

                case BenchmarkMetrics.Max:
                    return Max;

                case BenchmarkMetrics.Mean:
                    return Mean;

                case BenchmarkMetrics.Count:
                    return Count;

                default:
                    return double.NaN;
            }
        }

        public string Describe() =>
            "count=" + Count.ToString(CultureInfo.InvariantCulture)
            + ";min=" + Min.ToString(CultureInfo.InvariantCulture)
            + ";p50=" + P50.ToString(CultureInfo.InvariantCulture)
            + ";p95=" + P95.ToString(CultureInfo.InvariantCulture)
            + ";p99=" + P99.ToString(CultureInfo.InvariantCulture)
            + ";max=" + Max.ToString(CultureInfo.InvariantCulture)
            + ";mean=" + Mean.ToString("0.##", CultureInfo.InvariantCulture)
            + ";total=" + Total.ToString(CultureInfo.InvariantCulture)
            + ";unit=us";

        public override string ToString() => Describe();
    }

    /// <summary>The nearest-rank percentile definition the whole benchmark uses.</summary>
    public static class BenchmarkStatistics
    {
        /// <summary>
        /// The p-th percentile of an ascending-sorted array by the nearest-rank rule: index
        /// <c>ceil(p/100 * N) - 1</c>, clamped into <c>[0, N-1]</c>. p=0 returns the minimum and p=100 the maximum.
        /// </summary>
        public static long PercentileOfSorted(long[] sorted, double percentile)
        {
            if (sorted == null)
            {
                throw new ArgumentNullException(nameof(sorted));
            }

            if (sorted.Length == 0)
            {
                return 0L;
            }

            if (percentile <= 0.0)
            {
                return sorted[0];
            }

            if (percentile >= 100.0)
            {
                return sorted[sorted.Length - 1];
            }

            double rank = Math.Ceiling(percentile / 100.0 * sorted.Length);
            int index = (int)rank - 1;
            if (index < 0)
            {
                index = 0;
            }

            if (index >= sorted.Length)
            {
                index = sorted.Length - 1;
            }

            return sorted[index];
        }

        /// <summary>Copies and sorts <paramref name="samples"/>, then takes the nearest-rank percentile.</summary>
        public static long Percentile(IReadOnlyList<long> samples, double percentile)
        {
            if (samples == null)
            {
                throw new ArgumentNullException(nameof(samples));
            }

            if (samples.Count == 0)
            {
                return 0L;
            }

            var sorted = new long[samples.Count];
            for (int i = 0; i < samples.Count; i++)
            {
                sorted[i] = samples[i];
            }

            Array.Sort(sorted);
            return PercentileOfSorted(sorted, percentile);
        }

        /// <summary>Pools several runs' sample lists into one, keeping every sample (never an average of runs).</summary>
        public static List<long> Pool(IReadOnlyList<IReadOnlyList<long>> perRun)
        {
            if (perRun == null)
            {
                throw new ArgumentNullException(nameof(perRun));
            }

            var pooled = new List<long>();
            for (int run = 0; run < perRun.Count; run++)
            {
                IReadOnlyList<long> samples = perRun[run];
                if (samples == null)
                {
                    continue;
                }

                for (int i = 0; i < samples.Count; i++)
                {
                    pooled.Add(samples[i]);
                }
            }

            return pooled;
        }
    }
}

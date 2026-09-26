// GameCore.Benchmarks tests — the nearest-rank percentile definition and the per-phase distribution (GC-026).
//
// 08 section 3 asks for "per-sample p50/p95/p99/max and total counts, not only averages or FPS". A budget comparison
// is only evidence if the percentile definition does not drift between tools, so this suite pins the definition on
// vectors whose answers are checkable by hand — including a vector where nearest-rank and linear interpolation
// disagree (that is the test that would fail if someone swapped in an interpolating implementation) — and pins that
// the distribution is *computed from the samples* rather than accumulated.
//
// Sources in this folder run as plain-dotnet tests and as Unity EditMode tests.
#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameCore.Benchmarks.Tests
{
    [TestFixture]
    public sealed class BenchmarkStatisticsTests
    {
        [TestCase(new[] { 4, 2, 9, 1 }, 50.0, 2L)]
        [TestCase(new[] { 4, 2, 9, 1 }, 95.0, 9L)]
        [TestCase(new[] { 4, 2, 9, 1 }, 99.0, 9L)]
        [TestCase(new[] { 4, 2, 9, 1 }, 0.0, 1L)]
        [TestCase(new[] { 4, 2, 9, 1 }, 100.0, 9L)]
        [TestCase(new[] { 4, 2, 9, 1 }, -5.0, 1L)]
        [TestCase(new[] { 4, 2, 9, 1 }, 150.0, 9L)]
        [TestCase(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, 50.0, 5L)]
        [TestCase(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, 95.0, 10L)]
        [TestCase(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, 99.0, 10L)]
        [TestCase(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, 10.0, 1L)]
        [TestCase(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, 11.0, 2L)]
        [TestCase(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, 90.0, 9L)]
        [TestCase(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, 91.0, 10L)]
        [TestCase(new[] { 7 }, 50.0, 7L)]
        [TestCase(new[] { 7 }, 99.0, 7L)]
        [TestCase(new[] { 5, 4, 3, 2, 1 }, 50.0, 3L)]
        public void TheNearestRankDefinitionIsPinnedByHandOnSortedAndUnsortedVectors(
            int[] samples,
            double percentile,
            long expected)
        {
            var values = new List<long>(samples.Length);
            for (int i = 0; i < samples.Length; i++)
            {
                values.Add(samples[i]);
            }

            Assert.That(BenchmarkStatistics.Percentile(values, percentile), Is.EqualTo(expected));
        }

        [Test]
        public void NearestRankIsAnObservedSampleNotAnInterpolatedOne()
        {
            // Linear interpolation gives 2.5 for p50 of [1,2,3,4]; nearest-rank gives the element at index
            // ceil(0.5 * 4) - 1 = 1, which is an actual measurement. A budget compared against an invented number
            // is not evidence, so this stays pinned.
            var four = new List<long> { 1L, 2L, 3L, 4L };
            Assert.That(BenchmarkStatistics.Percentile(four, 50.0), Is.EqualTo(2L), "p50 of [1,2,3,4] is 2, never 2.5");

            var two = new List<long> { 1L, 2L };
            Assert.That(BenchmarkStatistics.Percentile(two, 50.0), Is.EqualTo(1L), "p50 of [1,2] is 1, never 1.5");

            var three = new List<long> { 1L, 2L, 3L };
            Assert.That(BenchmarkStatistics.Percentile(three, 95.0), Is.EqualTo(3L), "p95 of three samples is the largest");

            var ten = new List<long> { 1L, 2L, 3L, 4L, 5L, 6L, 7L, 8L, 9L, 10L };
            Assert.That(BenchmarkStatistics.Percentile(ten, 50.0), Is.EqualTo(5L), "an even count keeps the lower middle sample");
            Assert.That(BenchmarkStatistics.Percentile(ten, 51.0), Is.EqualTo(6L), "just above the middle moves up one rank");
        }

        [Test]
        public void TheHundredSampleVectorPinsFiftyNinetyFiveAndNinetyNine()
        {
            var oneHundred = new List<long>(100);
            for (int i = 1; i <= 100; i++)
            {
                oneHundred.Add(i);
            }

            Assert.That(BenchmarkStatistics.Percentile(oneHundred, 50.0), Is.EqualTo(50L));
            Assert.That(BenchmarkStatistics.Percentile(oneHundred, 95.0), Is.EqualTo(95L));
            Assert.That(BenchmarkStatistics.Percentile(oneHundred, 99.0), Is.EqualTo(99L));
            Assert.That(BenchmarkStatistics.Percentile(oneHundred, 1.0), Is.EqualTo(1L));
            Assert.That(BenchmarkStatistics.Percentile(oneHundred, 100.0), Is.EqualTo(100L));
            Assert.That(BenchmarkStatistics.Percentile(oneHundred, 0.0), Is.EqualTo(1L));

            var sorted = new long[100];
            for (int i = 0; i < sorted.Length; i++)
            {
                sorted[i] = i + 1;
            }

            Assert.That(BenchmarkStatistics.PercentileOfSorted(sorted, 50.0), Is.EqualTo(50L), "the sorted-array entry point agrees");
            Assert.That(BenchmarkStatistics.PercentileOfSorted(sorted, 95.0), Is.EqualTo(95L));
            Assert.That(BenchmarkStatistics.PercentileOfSorted(sorted, 99.0), Is.EqualTo(99L));
        }

        [Test]
        public void AnEmptyDistributionReportsEmptyRatherThanAZeroMeasurement()
        {
            BenchmarkDistribution empty = BenchmarkDistribution.Of(new List<long>());

            Assert.That(empty.IsEmpty, Is.True);
            Assert.That(empty.Count, Is.EqualTo(0));
            Assert.That(empty.Min, Is.EqualTo(0L));
            Assert.That(empty.Max, Is.EqualTo(0L));
            Assert.That(empty.Total, Is.EqualTo(0L));
            Assert.That(empty.Mean, Is.EqualTo(0.0));
            Assert.That(empty.P50, Is.EqualTo(0L));
            Assert.That(empty.P95, Is.EqualTo(0L));
            Assert.That(empty.P99, Is.EqualTo(0L));
            Assert.That(empty.Metric(BenchmarkMetrics.P50), Is.EqualTo(0.0));
            Assert.That(BenchmarkStatistics.PercentileOfSorted(new long[0], 95.0), Is.EqualTo(0L));
            Assert.That(BenchmarkStatistics.Percentile(new List<long>(), 95.0), Is.EqualTo(0L));
            Assert.That(BenchmarkStatistics.Pool(new List<IReadOnlyList<long>>()).Count, Is.EqualTo(0));
        }

        [Test]
        public void ASingleSampleIsEveryPercentile()
        {
            BenchmarkDistribution single = BenchmarkDistribution.Of(new List<long> { 7L });

            Assert.That(single.IsEmpty, Is.False);
            Assert.That(single.Count, Is.EqualTo(1));
            Assert.That(single.Min, Is.EqualTo(7L));
            Assert.That(single.Max, Is.EqualTo(7L));
            Assert.That(single.Total, Is.EqualTo(7L));
            Assert.That(single.Mean, Is.EqualTo(7.0));
            Assert.That(single.P50, Is.EqualTo(7L));
            Assert.That(single.P95, Is.EqualTo(7L));
            Assert.That(single.P99, Is.EqualTo(7L));
        }

        [Test]
        public void TheDistributionIsComputedFromTheSamplesAndIsMonotonic()
        {
            List<long> samples = SeededVector(500);
            var copy = new List<long>(samples);
            BenchmarkDistribution distribution = BenchmarkDistribution.Of(samples);

            long total = 0L;
            long min = long.MaxValue;
            long max = long.MinValue;
            for (int i = 0; i < copy.Count; i++)
            {
                total += copy[i];
                min = copy[i] < min ? copy[i] : min;
                max = copy[i] > max ? copy[i] : max;
            }

            Assert.That(distribution.Count, Is.EqualTo(copy.Count));
            Assert.That(distribution.Total, Is.EqualTo(total), "the total is the sum of the samples, recomputed here");
            Assert.That(distribution.Min, Is.EqualTo(min));
            Assert.That(distribution.Max, Is.EqualTo(max));
            Assert.That(distribution.Mean, Is.EqualTo((double)total / copy.Count).Within(1e-9));
            Assert.That(distribution.P50, Is.LessThanOrEqualTo(distribution.P95), "p50 <= p95");
            Assert.That(distribution.P95, Is.LessThanOrEqualTo(distribution.P99), "p95 <= p99");
            Assert.That(distribution.P99, Is.LessThanOrEqualTo(distribution.Max), "p99 <= max");
            Assert.That(distribution.Min, Is.LessThanOrEqualTo(distribution.P50), "min <= p50");

            for (int i = 0; i < samples.Count; i++)
            {
                Assert.That(samples[i], Is.EqualTo(copy[i]), "the distribution does not reorder the caller's samples");
            }
        }

        [Test]
        public void TheNamedMetricsAreTheDistributionsOwnNumbersAndAnUnknownMetricIsNaN()
        {
            var values = new List<long>();
            for (int i = 1; i <= 10; i++)
            {
                values.Add(i);
            }

            BenchmarkDistribution distribution = BenchmarkDistribution.Of(values);

            Assert.That(distribution.Metric(BenchmarkMetrics.Count), Is.EqualTo(10.0));
            Assert.That(distribution.Metric(BenchmarkMetrics.P50), Is.EqualTo(5.0));
            Assert.That(distribution.Metric(BenchmarkMetrics.P95), Is.EqualTo(10.0));
            Assert.That(distribution.Metric(BenchmarkMetrics.P99), Is.EqualTo(10.0));
            Assert.That(distribution.Metric(BenchmarkMetrics.Max), Is.EqualTo(10.0));
            Assert.That(distribution.Metric(BenchmarkMetrics.Mean), Is.EqualTo(5.5));
            Assert.That(double.IsNaN(distribution.Metric("median-of-medians")), Is.True, "an unknown metric is not a zero");
            Assert.That(double.IsNaN(distribution.Metric(string.Empty)), Is.True);
        }

        [Test]
        public void PoolingKeepsEverySampleAndPreservesThePooledPercentile()
        {
            var first = new List<long>();
            var second = new List<long>();
            for (int i = 1; i <= 10; i++)
            {
                first.Add(i);
            }

            for (int i = 11; i <= 20; i++)
            {
                second.Add(i);
            }

            var perRun = new List<IReadOnlyList<long>> { first, second };
            List<long> pooled = BenchmarkStatistics.Pool(perRun);

            Assert.That(pooled.Count, Is.EqualTo(20), "every sample of every run survives the pooling");
            long total = 0L;
            for (int i = 0; i < pooled.Count; i++)
            {
                total += pooled[i];
            }

            Assert.That(total, Is.EqualTo(210L));

            var direct = new List<long>();
            for (int i = 1; i <= 20; i++)
            {
                direct.Add(i);
            }

            Assert.That(
                BenchmarkStatistics.Percentile(pooled, 95.0),
                Is.EqualTo(BenchmarkStatistics.Percentile(direct, 95.0)),
                "pooling runs must not fold them into an average first");
            Assert.That(BenchmarkStatistics.Percentile(pooled, 95.0), Is.EqualTo(19L));

            var withGap = new List<IReadOnlyList<long>> { null!, first };
            Assert.That(BenchmarkStatistics.Pool(withGap).Count, Is.EqualTo(10), "a run that produced no samples contributes none");
        }

        [Test]
        public void ANegativeDurationSampleIsRejectedRatherThanSorted()
        {
            var negative = new List<long> { 10L, -1L, 5L };
            Assert.Throws<ArgumentOutOfRangeException>(
                () => BenchmarkDistribution.Of(negative),
                "a negative duration is a clock or accounting defect, never a fast sample");
            Assert.Throws<ArgumentNullException>(() => BenchmarkDistribution.Of(null!));
            Assert.Throws<ArgumentNullException>(() => BenchmarkStatistics.Percentile(null!, 50.0));
            Assert.Throws<ArgumentNullException>(() => BenchmarkStatistics.PercentileOfSorted(null!, 50.0));
            Assert.Throws<ArgumentNullException>(() => BenchmarkStatistics.Pool(null!));
        }

        [Test]
        public void TheDistributionDescribesItselfInMicroseconds()
        {
            var values = new List<long> { 100L, 200L, 300L };
            BenchmarkDistribution distribution = BenchmarkDistribution.Of(values);

            Assert.That(distribution.Describe().Contains("count=3", StringComparison.Ordinal), Is.True);
            Assert.That(distribution.Describe().Contains("p50=200", StringComparison.Ordinal), Is.True);
            Assert.That(distribution.Describe().Contains("max=300", StringComparison.Ordinal), Is.True);
            Assert.That(distribution.Describe().Contains("total=600", StringComparison.Ordinal), Is.True);
            Assert.That(distribution.Describe().Contains("unit=us", StringComparison.Ordinal), Is.True);
            Assert.That(distribution.ToString(), Is.EqualTo(distribution.Describe()));
        }

        private static List<long> SeededVector(int count)
        {
            var samples = new List<long>(count);
            uint state = 123456789U;
            for (int i = 0; i < count; i++)
            {
                state = unchecked((state * 1664525U) + 1013904223U);
                samples.Add((long)(state % 5000U));
            }

            return samples;
        }
    }
}

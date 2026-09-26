// GameCore.Contracts — the compact telemetry containers: one counter set, sections, frames and a retained trace.
//
// The storage is deliberately flat and boring: one `long[]` per owner, canonical big-endian bytes for hashing,
// text only on demand. Nothing here reads a clock, and nothing here allocates per sample beyond the series
// dictionaries a duration owner explicitly creates.
//
// Hashing is over the *semantic* content of the sample only. A frame's canonical bytes are:
//
//   FrameFormat (UTF-8, length-prefixed) | int32 worldOrdinal | uint64 epoch | uint64 step |
//   section count | (section owner key, counter count, then (int32 id, int64 value) pairs in id order)...
//
// Owner keys are written in the order given: a collector sorts its owners by key first, so two frames of the same
// world state hash equal regardless of the order owners were registered in. Timestamps, pointer values, machine
// paths and formatting never enter the hash (P-027, TEST-022).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GameCore.Contracts
{
    /// <summary>
    /// One owner's counter values in the fixed compact schema (GC-023). Backed by exactly one array of
    /// <see cref="TelemetrySchema.CounterCount"/> integers; <see cref="Add"/> saturates rather than wrapping, so a
    /// long run cannot turn a large count into a small one.
    /// </summary>
    public sealed class TelemetryCounterSet
    {
        private readonly long[] values = new long[TelemetrySchema.CounterCount];

        /// <summary>Number of counters this set holds; the schema's fixed length.</summary>
        public int Count => values.Length;

        /// <summary>Reads one counter.</summary>
        public long Get(TelemetryCounter counter) => values[Checked(counter)];

        /// <summary>Writes one counter (a gauge, an identity or a fact of the sample).</summary>
        public void Set(TelemetryCounter counter, long value) => values[Checked(counter)] = value;

        /// <summary>Adds a delta to a cumulative counter, saturating at <see cref="long.MaxValue"/>.</summary>
        public void Add(TelemetryCounter counter, long delta)
        {
            int index = Checked(counter);
            long current = values[index];
            if (delta == 0L)
            {
                return;
            }

            if (delta > 0L && current > long.MaxValue - delta)
            {
                values[index] = long.MaxValue;
                return;
            }

            if (delta < 0L && current < long.MinValue - delta)
            {
                values[index] = long.MinValue;
                return;
            }

            values[index] = current + delta;
        }

        /// <summary>Raises a gauge to at least <paramref name="value"/>; a high-water mark never decreases.</summary>
        public void ObserveMax(TelemetryCounter counter, long value)
        {
            int index = Checked(counter);
            if (value > values[index])
            {
                values[index] = value;
            }
        }

        /// <summary>Zeroes every counter; a collector reuses one set across samples.</summary>
        public void Reset() => Array.Clear(values, 0, values.Length);

        /// <summary>True when every counter is zero.</summary>
        public bool IsZero
        {
            get
            {
                for (int i = 0; i < values.Length; i++)
                {
                    if (values[i] != 0L)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>Copies every counter from <paramref name="other"/>.</summary>
        public void CopyFrom(TelemetryCounterSet other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            Array.Copy(other.values, values, values.Length);
        }

        /// <summary>An independent copy; a retained frame owns its own numbers.</summary>
        public TelemetryCounterSet Clone()
        {
            var clone = new TelemetryCounterSet();
            clone.CopyFrom(this);
            return clone;
        }

        /// <summary>
        /// Merges <paramref name="other"/> into this set using the schema's per-counter aggregation policy:
        /// cumulative work sums, gauges and facts take the maximum.
        /// </summary>
        public void Merge(TelemetryCounterSet other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            for (int i = 0; i < values.Length; i++)
            {
                var counter = (TelemetryCounter)i;
                long value = other.values[i];
                if (TelemetrySchema.AggregationOf(counter) == TelemetryAggregation.Max)
                {
                    if (value > values[i])
                    {
                        values[i] = value;
                    }
                }
                else
                {
                    Add(counter, value);
                }
            }
        }

        /// <summary>Every counter in canonical id order, as a dense array copy.</summary>
        public long[] ToArray()
        {
            var copy = new long[values.Length];
            Array.Copy(values, copy, values.Length);
            return copy;
        }

        /// <summary>Compact one-line text of the non-zero counters, formatted on demand (08 s3).</summary>
        public string Describe()
        {
            var builder = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == 0L)
                {
                    continue;
                }

                if (builder.Length != 0)
                {
                    builder.Append(';');
                }

                builder.Append(TelemetrySchema.Name((TelemetryCounter)i))
                    .Append('=')
                    .Append(values[i].ToString(CultureInfo.InvariantCulture));
            }

            return builder.Length == 0 ? "0" : builder.ToString();
        }

        public override string ToString() => "TelemetryCounterSet{" + Describe() + "}";

        /// <summary>Appends this set's canonical bytes: counter count, then (int32 id, int64 value) pairs.</summary>
        public void AppendCanonical(List<byte> destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            TelemetryCanonical.AppendInt32(destination, values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                TelemetryCanonical.AppendInt32(destination, i);
                TelemetryCanonical.AppendInt64(destination, values[i]);
            }
        }

        /// <summary>Canonical hash of this set alone.</summary>
        public ContentHash Hash()
        {
            var bytes = new List<byte>(values.Length * 12 + 4);
            AppendCanonical(bytes);
            return ContentHash.Compute(bytes.ToArray());
        }

        private static int Checked(TelemetryCounter counter)
        {
            int index = (int)counter;
            if (index < 0 || index >= TelemetrySchema.CounterCount)
            {
                throw new ArgumentOutOfRangeException(nameof(counter));
            }

            return index;
        }
    }

    /// <summary>One runtime owner's contribution to a telemetry frame (GC-023).</summary>
    public sealed class TelemetrySection
    {
        public TelemetrySection(string owner, TelemetryCounterSet counters)
        {
            if (string.IsNullOrEmpty(owner))
            {
                throw new ArgumentException("A telemetry section needs a stable owner key.", nameof(owner));
            }

            Owner = owner;
            Counters = counters ?? throw new ArgumentNullException(nameof(counters));
        }

        /// <summary>Stable lower-case owner key; the same key as <see cref="ITelemetryOwner.TelemetryOwner"/>.</summary>
        public string Owner { get; }

        public TelemetryCounterSet Counters { get; }

        /// <summary>Appends the canonical bytes of this section: owner key, then the counter set.</summary>
        public void AppendCanonical(List<byte> destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            TelemetryCanonical.AppendString(destination, Owner);
            Counters.AppendCanonical(destination);
        }

        public string Describe() => Owner + "{" + Counters.Describe() + "}";

        public override string ToString() => Describe();
    }

    /// <summary>
    /// One sampled frame: a world ordinal, the assembly epoch and logical step it describes, and the sections of
    /// every owner that was sampled. The ordinal is a *fixture* number, not a runtime <see cref="WorldId"/>, so
    /// frames of two sessions of the same fixture compare equal (TEST-022's normalization rule); the runtime
    /// identities still differ and stale-handle validation is untouched.
    /// </summary>
    public sealed class TelemetryFrame
    {
        public TelemetryFrame(
            int worldOrdinal,
            AssemblyEpoch epoch,
            LogicalStepId step,
            IReadOnlyList<TelemetrySection>? sections)
        {
            WorldOrdinal = worldOrdinal;
            Epoch = epoch;
            Step = step;
            Sections = ContractCollections.Freeze(sections);
        }

        /// <summary>Fixture-assigned world ordinal; the comparison key that replaces a fresh runtime session id.</summary>
        public int WorldOrdinal { get; }

        public AssemblyEpoch Epoch { get; }

        public LogicalStepId Step { get; }

        /// <summary>Sections in the order the collector wrote them; a collector sorts by owner key.</summary>
        public IReadOnlyList<TelemetrySection> Sections { get; }

        /// <summary>Sections keyed by owner, for a lookup in a test or a report.</summary>
        public TelemetryCounterSet? CountersOf(string owner)
        {
            for (int i = 0; i < Sections.Count; i++)
            {
                if (string.Equals(Sections[i].Owner, owner, StringComparison.Ordinal))
                {
                    return Sections[i].Counters;
                }
            }

            return null;
        }

        /// <summary>Every counter of every section aggregated with the schema's policy.</summary>
        public TelemetryCounterSet Aggregate()
        {
            var total = new TelemetryCounterSet();
            for (int i = 0; i < Sections.Count; i++)
            {
                total.Merge(Sections[i].Counters);
            }

            return total;
        }

        /// <summary>Canonical bytes of this frame; see the file header for the exact layout.</summary>
        public void AppendCanonical(List<byte> destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            TelemetryCanonical.AppendString(destination, TelemetrySchema.FrameFormat);
            TelemetryCanonical.AppendInt32(destination, WorldOrdinal);
            TelemetryCanonical.AppendUInt64(destination, Epoch.Value);
            TelemetryCanonical.AppendUInt64(destination, Step.Value);
            TelemetryCanonical.AppendInt32(destination, Sections.Count);
            // Sections are written in owner-key order, so a frame's hash is a property of what was sampled rather
            // than of the order a caller happened to hand the sections over in (GC-023's frame comparison).
            var ordered = new List<TelemetrySection>(Sections);
            ordered.Sort(static (left, right) => string.CompareOrdinal(left.Owner, right.Owner));
            for (int i = 0; i < ordered.Count; i++)
            {
                ordered[i].AppendCanonical(destination);
            }
        }

        /// <summary>Canonical hash of this frame.</summary>
        public ContentHash Hash()
        {
            var bytes = new List<byte>(256);
            AppendCanonical(bytes);
            return ContentHash.Compute(bytes.ToArray());
        }

        public string Describe() =>
            "frame{world=" + WorldOrdinal.ToString(CultureInfo.InvariantCulture)
            + ";epoch=" + Epoch.Value.ToString(CultureInfo.InvariantCulture)
            + ";step=" + Step.Value.ToString(CultureInfo.InvariantCulture)
            + ";sections=" + Sections.Count.ToString(CultureInfo.InvariantCulture)
            + ";" + Aggregate().Describe() + "}";

        public override string ToString() => Describe();
    }

    /// <summary>
    /// A retained sequence of frames plus its chain hash: <c>chain[0] = frame[0].Hash</c>,
    /// <c>chain[i] = H(chain[i-1] || frame[i].Hash || serialized frame[i])</c>. Two runs of the same trace
    /// produce the same chain hash, and any divergence at any step changes every later link, which is what makes
    /// a trace a usable ordering/cost regression witness (TEST-022, TEST-023).
    /// </summary>
    public sealed class TelemetryTrace
    {
        public TelemetryTrace(string label, IReadOnlyList<TelemetryFrame>? frames)
        {
            Label = label ?? string.Empty;
            Frames = ContractCollections.Freeze(frames);
            ChainHash = ComputeChain(Frames);
        }

        /// <summary>Diagnostic label of the trace, e.g. <c>replay-cards-w4</c>. Never part of the chain hash.</summary>
        public string Label { get; }

        public IReadOnlyList<TelemetryFrame> Frames { get; }

        /// <summary>Chain hash over every retained frame, in order.</summary>
        public ContentHash ChainHash { get; }

        /// <summary>Canonical bytes of the whole trace: format, frame count, then each frame.</summary>
        public void AppendCanonical(List<byte> destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            TelemetryCanonical.AppendString(destination, TelemetrySchema.TraceFormat);
            TelemetryCanonical.AppendInt32(destination, Frames.Count);
            for (int i = 0; i < Frames.Count; i++)
            {
                Frames[i].AppendCanonical(destination);
            }
        }

        public string Describe() =>
            "trace{" + (Label.Length == 0 ? "unlabelled" : Label)
            + ";frames=" + Frames.Count.ToString(CultureInfo.InvariantCulture)
            + ";chain=" + ChainHash.ToHex() + "}";

        public override string ToString() => Describe();

        private static ContentHash ComputeChain(IReadOnlyList<TelemetryFrame> frames)
        {
            var bytes = new List<byte>(256);
            TelemetryCanonical.AppendString(bytes, TelemetrySchema.TraceFormat);
            TelemetryCanonical.AppendInt32(bytes, frames.Count);
            for (int i = 0; i < frames.Count; i++)
            {
                TelemetryCanonical.AppendHash(bytes, frames[i].Hash());
            }

            return ContentHash.Compute(bytes.ToArray());
        }
    }

    /// <summary>
    /// A bounded duration series keyed by a stable id: per-stage and job-wait durations are recorded against the
    /// stage they belong to, then folded into the two counters the schema names. Samples are summed, so the
    /// series is allocation-bounded by the number of distinct keys rather than by the number of samples.
    /// </summary>
    public sealed class TelemetrySeries
    {
        private readonly TelemetryCounter totalCounter;
        private readonly TelemetryCounter countCounter;
        private readonly Dictionary<Id128, int> indexByKey = new Dictionary<Id128, int>();
        private readonly List<Entry> entries = new List<Entry>();

        public TelemetrySeries(TelemetryCounter totalCounter, TelemetryCounter countCounter)
        {
            this.totalCounter = totalCounter;
            this.countCounter = countCounter;
        }

        /// <summary>Distinct keys recorded; one entry per key, in first-recorded order.</summary>
        public int KeyCount => entries.Count;

        /// <summary>Records one duration sample against one stable key.</summary>
        public void Record(Id128 key, long microseconds)
        {
            if (!indexByKey.TryGetValue(key, out int index))
            {
                index = entries.Count;
                indexByKey.Add(key, index);
                entries.Add(new Entry(key));
            }

            Entry entry = entries[index];
            entry.Total += microseconds;
            entry.Count++;
            if (microseconds > entry.Max)
            {
                entry.Max = microseconds;
            }

            entries[index] = entry;
        }

        /// <summary>Reads one key's samples: total microseconds, sample count and maximum.</summary>
        public bool TryGet(Id128 key, out long totalMicroseconds, out int count, out long maxMicroseconds)
        {
            if (indexByKey.TryGetValue(key, out int index))
            {
                Entry entry = entries[index];
                totalMicroseconds = entry.Total;
                count = (int)entry.Count;
                maxMicroseconds = entry.Max;
                return true;
            }

            totalMicroseconds = 0L;
            count = 0;
            maxMicroseconds = 0L;
            return false;
        }

        /// <summary>Keys in first-recorded order, with their aggregates (for a report; never per step).</summary>
        public IReadOnlyList<TelemetrySeriesEntry> Entries()
        {
            var result = new TelemetrySeriesEntry[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                result[i] = new TelemetrySeriesEntry(entries[i].Key, entries[i].Total, entries[i].Count, entries[i].Max);
            }

            return Array.AsReadOnly(result);
        }

        /// <summary>Folds the series into a counter set: total microseconds and sample count.</summary>
        public void WriteInto(TelemetryCounterSet into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            long total = 0L;
            long count = 0L;
            for (int i = 0; i < entries.Count; i++)
            {
                total += entries[i].Total;
                count += entries[i].Count;
            }

            into.Add(totalCounter, total);
            into.Add(countCounter, count);
        }

        /// <summary>Canonical bytes of the series: one (key, total, count, max) record per key, in key order.</summary>
        public void AppendCanonical(List<byte> destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            var ordered = new List<Entry>(entries);
            ordered.Sort(static (left, right) => left.Key.CompareTo(right.Key));
            TelemetryCanonical.AppendInt32(destination, ordered.Count);
            for (int i = 0; i < ordered.Count; i++)
            {
                TelemetryCanonical.AppendId128(destination, ordered[i].Key);
                TelemetryCanonical.AppendInt64(destination, ordered[i].Total);
                TelemetryCanonical.AppendInt64(destination, ordered[i].Count);
                TelemetryCanonical.AppendInt64(destination, ordered[i].Max);
            }
        }

        private struct Entry
        {
            public Entry(Id128 key)
            {
                Key = key;
                Total = 0L;
                Count = 0L;
                Max = 0L;
            }

            public Id128 Key;
            public long Total;
            public long Count;
            public long Max;
        }
    }

    /// <summary>One key's aggregate in a <see cref="TelemetrySeries"/>.</summary>
    public readonly struct TelemetrySeriesEntry
    {
        public TelemetrySeriesEntry(Id128 key, long totalMicroseconds, long count, long maxMicroseconds)
        {
            Key = key;
            TotalMicroseconds = totalMicroseconds;
            Count = count;
            MaxMicroseconds = maxMicroseconds;
        }

        public Id128 Key { get; }

        public long TotalMicroseconds { get; }

        public long Count { get; }

        public long MaxMicroseconds { get; }

        public override string ToString() =>
            Key.ToString() + ":" + TotalMicroseconds.ToString(CultureInfo.InvariantCulture)
            + "/" + Count.ToString(CultureInfo.InvariantCulture)
            + ":" + MaxMicroseconds.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A duration a host measured around one boundary, reported as microseconds. The kernel never reads a clock
    /// itself (P-008's determinism boundary and the P-022 preparation deadline are both host-supplied), so timing
    /// is an explicit input: a caller that wants durations passes the elapsed delta; a caller that does not passes
    /// zero, and the sample count stays honest about it.
    /// </summary>
    public static class TelemetryDurations
    {
        /// <summary>Converts a host tick delta to microseconds for the clock rate a host measured with.</summary>
        public static long TicksToMicroseconds(long ticks, long ticksPerSecond)
        {
            if (ticks <= 0L || ticksPerSecond <= 0L)
            {
                return 0L;
            }

            return (long)((double)ticks * 1000000.0 / ticksPerSecond);
        }
    }

    /// <summary>Canonical big-endian writers shared by the telemetry containers (05 s6's encoding order).</summary>
    internal static class TelemetryCanonical
    {
        public static void AppendInt32(List<byte> destination, int value)
        {
            uint bits = unchecked((uint)value);
            destination.Add((byte)(bits >> 24));
            destination.Add((byte)(bits >> 16));
            destination.Add((byte)(bits >> 8));
            destination.Add((byte)bits);
        }

        public static void AppendInt64(List<byte> destination, long value) =>
            AppendUInt64(destination, unchecked((ulong)value));

        public static void AppendUInt64(List<byte> destination, ulong value)
        {
            destination.Add((byte)(value >> 56));
            destination.Add((byte)(value >> 48));
            destination.Add((byte)(value >> 40));
            destination.Add((byte)(value >> 32));
            destination.Add((byte)(value >> 24));
            destination.Add((byte)(value >> 16));
            destination.Add((byte)(value >> 8));
            destination.Add((byte)value);
        }

        public static void AppendId128(List<byte> destination, Id128 value)
        {
            AppendUInt64(destination, value.High);
            AppendUInt64(destination, value.Low);
        }

        public static void AppendHash(List<byte> destination, ContentHash value)
        {
            byte[] bytes = value.ToArray();
            for (int i = 0; i < bytes.Length; i++)
            {
                destination.Add(bytes[i]);
            }
        }

        public static void AppendString(List<byte> destination, string value)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(value ?? string.Empty);
            AppendInt32(destination, utf8.Length);
            for (int i = 0; i < utf8.Length; i++)
            {
                destination.Add(utf8[i]);
            }
        }
    }
}

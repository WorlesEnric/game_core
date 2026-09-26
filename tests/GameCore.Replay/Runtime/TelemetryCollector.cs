// GameCore.Replay — telemetry collection, retention and the raw benchmark trace format (GC-023).
//
// Two separate things live here, and keeping them separate is the point:
//
//   1. `TelemetryCollector` samples a set of explicitly registered `ITelemetryOwner`s into `TelemetryFrame`s and
//      retains a bounded trace of them. Owners are sorted by their stable key and a frame carries exactly one
//      section per key (two owners sharing a key merge), so a frame is independent of registration order — which is
//      what makes two runs of the same world comparable frame by frame.
//   2. `BenchmarkTrace` is the *raw* trace format: a compact, line-oriented, allocation-light text form plus a
//      hand-rolled JSON document. Both are dependency-free and deterministic (this assembly compiles under
//      netstandard2.1, where System.Text.Json is not available, and the repository has no JSON writer).
//
// Retention is explicit, never implicit: `TelemetryRetention.Off` samples nothing into the trace while leaving the
// owners' counters alone, and `TelemetrySchema.IsCompiledIn` reports whether the *calling* compilation counts at
// all. A trace therefore says which of the two shapes produced it, and a cost claim cannot be made from a build
// that never counted (08 s3: "record the instrumented diagnostic build and release-like measurement build
// separately").
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Replay
{
    /// <summary>
    /// Samples registered runtime owners through the fixed compact schema and retains a bounded frame trace.
    /// Registration is explicit: a collector never discovers owners, so what a frame contains is a property of the
    /// composition root that created it rather than of reflection (P-001, P-051).
    /// </summary>
    public sealed class TelemetryCollector
    {
        private readonly List<ITelemetryOwner> owners = new List<ITelemetryOwner>();
        private readonly List<string> keys = new List<string>();
        private readonly List<TelemetryFrame> retained = new List<TelemetryFrame>();
        private readonly Dictionary<string, int> sectionCounters = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<TelemetrySection> sections = new List<TelemetrySection>();

        public TelemetryCollector(TelemetryRetention retention)
        {
            Retention = retention;
        }

        /// <summary>Bounded retention policy in force; see <see cref="TelemetryRetention"/>.</summary>
        public TelemetryRetention Retention { get; }

        /// <summary>Owners registered so far.</summary>
        public int OwnerCount => owners.Count;

        /// <summary>Samples attempted, whether or not a frame was retained.</summary>
        public int SampleCount { get; private set; }

        /// <summary>Samples refused by retention (bounded, counted, never a silent growth).</summary>
        public int DiscardedSampleCount { get; private set; }

        /// <summary>Sections written across every retained frame, so an empty frame is visible.</summary>
        public int SectionCount { get; private set; }

        /// <summary>Retained frames, oldest first.</summary>
        public IReadOnlyList<TelemetryFrame> Frames => retained;

        /// <summary>True when this build's callers counted anything at all (see <see cref="TelemetrySchema"/>).</summary>
        public bool CountingCompiledIn => TelemetrySchema.IsCompiledIn;

        /// <summary>Registers one owner. Registering the same owner twice is refused, not silently merged.</summary>
        public TelemetryCollector Add(ITelemetryOwner owner)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            if (string.IsNullOrEmpty(owner.TelemetryOwner))
            {
                throw new ArgumentException("A telemetry owner needs a stable key.", nameof(owner));
            }

            for (int i = 0; i < owners.Count; i++)
            {
                if (ReferenceEquals(owners[i], owner))
                {
                    return this;
                }
            }

            owners.Add(owner);
            keys.Add(owner.TelemetryOwner);
            return this;
        }

        /// <summary>Registers a minimal owner: a stable key plus a writer over the fixed schema.</summary>
        public TelemetryCollector Add(string key, Action<TelemetryCounterSet> write)
        {
            if (write == null)
            {
                throw new ArgumentNullException(nameof(write));
            }

            return Add(new DelegateTelemetryOwner(key, write));
        }

        /// <summary>
        /// Samples every registered owner into one frame of the given world ordinal, epoch and step. Owners are
        /// sampled in ascending key order and merged by key, so the frame is canonical.
        /// </summary>
        public TelemetryFrame Sample(int worldOrdinal, AssemblyEpoch epoch, LogicalStepId step)
        {
            SampleCount++;
            sections.Clear();
            sectionCounters.Clear();

            List<int> order = SortedOwnerOrder();
            for (int i = 0; i < order.Count; i++)
            {
                ITelemetryOwner owner = owners[order[i]];
                string key = keys[order[i]];
                int index;
                if (!sectionCounters.TryGetValue(key, out index))
                {
                    index = sections.Count;
                    sectionCounters.Add(key, index);
                    sections.Add(new TelemetrySection(key, new TelemetryCounterSet()));
                }

                owner.WriteTelemetry(sections[index].Counters);
            }

            var frame = new TelemetryFrame(worldOrdinal, epoch, step, sections);
            SectionCount += frame.Sections.Count;
            Retain(frame);
            return frame;
        }

        /// <summary>
        /// Samples exactly the given owners, bypassing the registered list. A run whose owner objects are rebuilt
        /// every step (a derivation result per step) samples through this overload, so registration cannot grow
        /// without bound.
        /// </summary>
        public TelemetryFrame Sample(
            int worldOrdinal, AssemblyEpoch epoch, LogicalStepId step, IReadOnlyList<ITelemetryOwner> explicitOwners)
        {
            if (explicitOwners == null)
            {
                throw new ArgumentNullException(nameof(explicitOwners));
            }

            SampleCount++;
            sections.Clear();
            sectionCounters.Clear();
            for (int i = 0; i < explicitOwners.Count; i++)
            {
                ITelemetryOwner owner = explicitOwners[i];
                string key = owner.TelemetryOwner;
                int index;
                if (!sectionCounters.TryGetValue(key, out index))
                {
                    index = sections.Count;
                    sectionCounters.Add(key, index);
                    sections.Add(new TelemetrySection(key, new TelemetryCounterSet()));
                }

                owner.WriteTelemetry(sections[index].Counters);
            }

            var frame = new TelemetryFrame(worldOrdinal, epoch, step, sections);
            SectionCount += frame.Sections.Count;
            Retain(frame);
            return frame;
        }

        /// <summary>The retained frames as one trace; the chain hash covers every retained frame in order.</summary>
        public TelemetryTrace Trace(string label) => new TelemetryTrace(label, retained);

        /// <summary>Drops retained frames; counters on the owners are untouched.</summary>
        public void Clear()
        {
            retained.Clear();
            SectionCount = 0;
        }

        /// <summary>Canonical summary of what this collector kept, printed on demand (08 s3).</summary>
        public string Describe() =>
            "telemetry{compiledIn=" + (CountingCompiledIn ? "1" : "0")
            + ";retention=" + Retention.ToString()
            + ";owners=" + OwnerCount.ToString(CultureInfo.InvariantCulture)
            + ";samples=" + SampleCount.ToString(CultureInfo.InvariantCulture)
            + ";retained=" + retained.Count.ToString(CultureInfo.InvariantCulture)
            + ";discarded=" + DiscardedSampleCount.ToString(CultureInfo.InvariantCulture)
            + ";sections=" + SectionCount.ToString(CultureInfo.InvariantCulture) + "}";

        public override string ToString() => Describe();

        private void Retain(TelemetryFrame frame)
        {
            if (!Retention.Enabled || retained.Count >= Retention.MaxFrames)
            {
                DiscardedSampleCount++;
                return;
            }

            if (frame.Sections.Count > Retention.MaxSections)
            {
                DiscardedSampleCount++;
                return;
            }

            retained.Add(frame);
        }

        private List<int> SortedOwnerOrder()
        {
            var order = new List<int>(owners.Count);
            for (int i = 0; i < owners.Count; i++)
            {
                order.Add(i);
            }

            List<string> localKeys = keys;
            order.Sort((left, right) =>
            {
                int byKey = string.CompareOrdinal(localKeys[left], localKeys[right]);
                return byKey != 0 ? byKey : left.CompareTo(right);
            });
            return order;
        }
    }

    /// <summary>One owner expressed as a key plus a writer; the seam a scenario uses for a bare counter set.</summary>
    internal sealed class DelegateTelemetryOwner : ITelemetryOwner
    {
        private readonly Action<TelemetryCounterSet> write;

        public DelegateTelemetryOwner(string key, Action<TelemetryCounterSet> write)
        {
            TelemetryOwner = key;
            this.write = write;
        }

        public string TelemetryOwner { get; }

        public void WriteTelemetry(TelemetryCounterSet into) => write(into);
    }

    /// <summary>
    /// The raw benchmark trace format (GC-023, 08 s3). One frame per line:
    ///
    /// <code>
    /// # gamecore.benchmark.trace/1
    /// # label=integer-10000 shape=branches=4;... seed=1234
    /// frame ordinal=0 epoch=1 step=1 sections=6 control-nodes-visited=0;candidates-matched=12;...
    /// summary frames=10000 discarded=0 chain=&lt;hex&gt;
    /// </code>
    ///
    /// The frame line carries every non-zero counter of the frame's aggregate as <c>name=value</c> pairs in
    /// ascending counter-id order, so the format is stable under a name change and comparable across runs. The JSON
    /// variant is the same data in the repository's artifact style (camelCase keys, fixed field order, two-space
    /// indent) for an evidence file.
    /// </summary>
    public static class BenchmarkTrace
    {
        /// <summary>Format identifier of the line-oriented raw trace.</summary>
        public const string Format = "gamecore.benchmark.trace/1";

        /// <summary>Writes one retained trace as the raw line format. Deterministic, no timestamps.</summary>
        public static string Write(TelemetryTrace trace)
        {
            if (trace == null)
            {
                throw new ArgumentNullException(nameof(trace));
            }

            var builder = new StringBuilder();
            builder.Append("# ").Append(Format).Append('\n');
            builder.Append("# label=").Append(trace.Label).Append('\n');
            builder.Append("# frames=").Append(trace.Frames.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            for (int i = 0; i < trace.Frames.Count; i++)
            {
                TelemetryFrame frame = trace.Frames[i];
                TelemetryCounterSet aggregate = frame.Aggregate();
                builder.Append("frame ordinal=").Append(frame.WorldOrdinal.ToString(CultureInfo.InvariantCulture))
                    .Append(" epoch=").Append(frame.Epoch.Value.ToString(CultureInfo.InvariantCulture))
                    .Append(" step=").Append(frame.Step.Value.ToString(CultureInfo.InvariantCulture))
                    .Append(" sections=").Append(frame.Sections.Count.ToString(CultureInfo.InvariantCulture))
                    .Append(' ');
                AppendAggregate(builder, aggregate);
                builder.Append('\n');
            }

            builder.Append("summary frames=").Append(trace.Frames.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" chain=").Append(trace.ChainHash.ToHex())
                .Append('\n');
            return builder.ToString();
        }

        /// <summary>Writes one retained trace as the artifact JSON document (camelCase, fixed field order).</summary>
        public static string WriteJson(TelemetryTrace trace)
        {
            if (trace == null)
            {
                throw new ArgumentNullException(nameof(trace));
            }

            var builder = new StringBuilder();
            builder.Append("{\n");
            builder.Append("  \"format\": \"").Append(Format).Append("\",\n");
            builder.Append("  \"label\": \"").Append(Escape(trace.Label)).Append("\",\n");
            builder.Append("  \"frameCount\": ").Append(trace.Frames.Count.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            builder.Append("  \"chainHash\": \"").Append(trace.ChainHash.ToHex()).Append("\",\n");
            builder.Append("  \"frames\": [");
            for (int i = 0; i < trace.Frames.Count; i++)
            {
                TelemetryFrame frame = trace.Frames[i];
                TelemetryCounterSet aggregate = frame.Aggregate();
                builder.Append(i == 0 ? "\n" : ",\n");
                builder.Append("    {\n");
                builder.Append("      \"ordinal\": ").Append(frame.WorldOrdinal.ToString(CultureInfo.InvariantCulture)).Append(",\n");
                builder.Append("      \"epoch\": ").Append(frame.Epoch.Value.ToString(CultureInfo.InvariantCulture)).Append(",\n");
                builder.Append("      \"step\": ").Append(frame.Step.Value.ToString(CultureInfo.InvariantCulture)).Append(",\n");
                builder.Append("      \"frameHash\": \"").Append(frame.Hash().ToHex()).Append("\",\n");
                builder.Append("      \"sections\": [");
                for (int s = 0; s < frame.Sections.Count; s++)
                {
                    builder.Append(s == 0 ? "\n" : ",\n");
                    builder.Append("        {\"owner\": \"").Append(Escape(frame.Sections[s].Owner))
                        .Append("\", \"counters\": \"")
                        .Append(Escape(frame.Sections[s].Counters.Describe())).Append("\"}");
                }

                builder.Append(frame.Sections.Count == 0 ? "],\n" : "\n      ],\n");
                builder.Append("      \"aggregate\": \"").Append(Escape(aggregate.Describe())).Append("\"\n");
                builder.Append("    }");
            }

            builder.Append(trace.Frames.Count == 0 ? "]\n" : "\n  ]\n");
            builder.Append("}\n");
            return builder.ToString();
        }

        /// <summary>
        /// One parsed frame header from the raw format, for a test that round-trips the trace. The counters are kept
        /// as the raw <c>name=value</c> text so a comparison cannot accidentally depend on parse order.
        /// </summary>
        public readonly struct RawFrame
        {
            public RawFrame(int ordinal, ulong epoch, ulong step, int sections, string counters)
            {
                Ordinal = ordinal;
                Epoch = epoch;
                Step = step;
                Sections = sections;
                Counters = counters ?? string.Empty;
            }

            public int Ordinal { get; }

            public ulong Epoch { get; }

            public ulong Step { get; }

            public int Sections { get; }

            public string Counters { get; }

            public override string ToString() =>
                "raw-frame{" + Ordinal.ToString(CultureInfo.InvariantCulture)
                + ";epoch=" + Epoch.ToString(CultureInfo.InvariantCulture)
                + ";step=" + Step.ToString(CultureInfo.InvariantCulture)
                + ";sections=" + Sections.ToString(CultureInfo.InvariantCulture)
                + ";counters=" + Counters + "}";
        }

        /// <summary>Reads the header and every frame line back; returns false on a malformed document.</summary>
        public static bool TryRead(string text, out IReadOnlyList<RawFrame> frames, out string failure)
        {
            frames = Array.Empty<RawFrame>();
            failure = string.Empty;
            if (string.IsNullOrEmpty(text))
            {
                failure = "the trace is empty";
                return false;
            }

            string[] lines = text.Split('\n');
            if (lines.Length < 2 || !lines[0].StartsWith("# " + Format, StringComparison.Ordinal))
            {
                failure = "the trace header is not " + Format;
                return false;
            }

            List<RawFrame> parsed = new List<RawFrame>();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                if (line.StartsWith("summary ", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!line.StartsWith("frame ", StringComparison.Ordinal))
                {
                    failure = "unexpected line: " + line;
                    return false;
                }

                string[] parts = line.Split(' ');
                int ordinal = 0;
                ulong epoch = 0UL;
                ulong step = 0UL;
                int sections = 0;
                int countersAt = -1;
                for (int p = 1; p < parts.Length; p++)
                {
                    string part = parts[p];
                    int equals = part.IndexOf('=');
                    if (equals <= 0)
                    {
                        continue;
                    }

                    string name = part.Substring(0, equals);
                    string value = part.Substring(equals + 1);
                    switch (name)
                    {
                        case "ordinal":
                            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ordinal);
                            break;
                        case "epoch":
                            ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out epoch);
                            break;
                        case "step":
                            ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out step);
                            break;
                        case "sections":
                            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out sections);
                            break;
                        default:
                            if (countersAt < 0)
                            {
                                countersAt = p;
                            }

                            break;
                    }

                    if (name == "sections")
                    {
                        countersAt = p + 1;
                    }
                }

                string counters = countersAt > 0 && countersAt < parts.Length
                    ? string.Join(" ", parts, countersAt, parts.Length - countersAt)
                    : string.Empty;
                parsed.Add(new RawFrame(ordinal, epoch, step, sections, counters));
            }

            frames = parsed.AsReadOnly();
            return true;
        }

        private static void AppendAggregate(StringBuilder builder, TelemetryCounterSet aggregate)
        {
            bool first = true;
            for (int i = 0; i < TelemetrySchema.CounterCount; i++)
            {
                var counter = (TelemetryCounter)i;
                long value = aggregate.Get(counter);
                if (value == 0L)
                {
                    continue;
                }

                if (!first)
                {
                    builder.Append(';');
                }

                builder.Append(TelemetrySchema.Name(counter))
                    .Append('=')
                    .Append(value.ToString(CultureInfo.InvariantCulture));
                first = false;
            }

            if (first)
            {
                builder.Append("none");
            }
        }

        private static string Escape(string value)
        {
            var builder = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                switch (character)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (character < ' ')
                        {
                            builder.Append("\\u").Append(((int)character).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            return builder.ToString();
        }
    }
}

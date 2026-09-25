// GameCore.Unity.Runtime (engine-free part, compiled as GameCore.Execution) - deterministic random streams
// (GC-018).
//
// Normative sources: 00 P-008 ("repeatability is required for pure integer/fixed-rule fixtures given identical
// build/catalog, composition, ordered admitted input, seed streams, and recorded engine observations") and P-053
// (a checkpoint contains "RNG streams").
//
// V1 had no random-stream type before this file, so a checkpoint had nothing to save and "identical seed streams"
// had no representation. The type below is deliberately small and boring: one 64-bit xorshift* generator per
// declared stream, identified by a stable 128-bit stream id and a 64-bit stream key, with an explicit draw count so
// a replay can prove it consumed the same number of values. It is *not* a cryptographic generator and makes no
// cross-platform bit-exactness claim beyond the integer arithmetic it uses, which is why the state is what a
// checkpoint stores rather than an expectation that a fresh generator reproduces a sequence (P-054, P-060).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Execution.Persistence
{
    /// <summary>One independent deterministic stream: stable identity, current state and draw count (P-008, P-053).</summary>
    public sealed class RngStream
    {
        /// <summary>Non-zero mixing constant; xorshift* needs a non-zero state to leave zero.</summary>
        private const ulong Multiplier = 2685821657736338717UL;

        /// <summary>Default state a stream starts from when a caller declares no explicit seed (P-008).</summary>
        public const ulong DefaultSeed = 0x9E3779B97F4A7C15UL;

        private ulong state;

        public RngStream(Id128 streamId, ulong streamKey, ulong seed)
        {
            if (streamId.IsDefault)
            {
                throw new ArgumentException("A random stream requires a stable non-zero identity (P-004).", nameof(streamId));
            }

            StreamId = streamId;
            StreamKey = streamKey;

            // A zero seed would pin the generator at zero; the mix below maps it to a usable non-zero state.
            state = seed != 0UL ? Mix(seed) : DefaultSeed;
        }

        /// <summary>Stable stream identity; part of the checkpoint record (P-004, P-053).</summary>
        public Id128 StreamId { get; }

        /// <summary>Stream key of the generator, so a catalog revision can change the mapping without reusing state.</summary>
        public ulong StreamKey { get; }

        /// <summary>Current generator state; exactly what a checkpoint stores (P-053).</summary>
        public ulong State => state;

        /// <summary>Values drawn since this stream was created or restored (P-008).</summary>
        public ulong DrawCount { get; private set; }

        /// <summary>Draws one 64-bit value and advances the stream.</summary>
        public ulong Next()
        {
            ulong x = state;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            state = x;
            DrawCount = DrawCount == ulong.MaxValue ? ulong.MaxValue : DrawCount + 1UL;
            return x * Multiplier;
        }

        /// <summary>Draws one value in `[0, bound)`; a non-positive bound is refused rather than clamped (P-019).</summary>
        public bool TryNextBounded(uint bound, out uint value)
        {
            if (bound == 0U)
            {
                value = 0U;
                return false;
            }

            // Rejection sampling, so the distribution does not depend on the modulo bias of a fixed divisor.
            uint threshold = (uint)((0x1_0000_0000UL - bound) % bound);
            while (true)
            {
                uint candidate = (uint)(Next() >> 32);
                if (candidate >= threshold)
                {
                    value = candidate % bound;
                    return true;
                }
            }
        }

        /// <summary>The record a capture writes for this stream (P-053).</summary>
        public RngRecordValue ToRecord() =>
            new RngRecordValue(StreamId.High, StreamId.Low, state, StreamKey, DrawCount);

        /// <summary>
        /// Restores one stream from its record, exactly inverting <see cref="ToRecord"/>: the recorded state is
        /// installed verbatim and the recorded draw count is kept, so a post-restore sequence continues rather than
        /// restarting. The state is not re-mixed here, because the record already carries a post-mix state and a
        /// zero state recorded by an implementation with a different generator must survive the round trip (P-053).
        /// </summary>
        public static RngStream Restore(RngRecordValue record)
        {
            if (record.StreamId.IsDefault)
            {
                throw new ArgumentException("A recorded random stream carries no identity (P-004).", nameof(record));
            }

            return new RngStream(record.StreamId, record.StreamKey, DefaultSeed)
            {
                state = record.State,
                DrawCount = record.DrawCount,
            };
        }

        private static ulong Mix(ulong value)
        {
            // SplitMix64 finalizer: a cheap, well-distributed map from a seed to a non-zero generator state.
            ulong z = value + 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public override string ToString() =>
            "rng(" + StreamId.ToString() + ",draws=" + DrawCount.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>The declared streams of one world, in canonical identity order (P-008).</summary>
    public sealed class RngStreamTable
    {
        private readonly Dictionary<Id128, RngStream> streams = new Dictionary<Id128, RngStream>();
        private readonly List<Id128> order = new List<Id128>();

        public RngStreamTable()
        {
        }

        public int Count => order.Count;

        /// <summary>Stable identities of the declared streams, in canonical order (P-008).</summary>
        public IReadOnlyList<Id128> StreamIds
        {
            get
            {
                var sorted = new List<Id128>(order);
                sorted.Sort();
                return sorted;
            }
        }

        /// <summary>
        /// Declares one stream. A duplicate identity is refused because one world rejects duplicate live stable
        /// identities in a category, and a second stream under one id would make the saved state ambiguous (P-004).
        /// </summary>
        public bool TryDeclare(Id128 streamId, ulong streamKey, ulong seed, out RngStream? stream, out string detail)
        {
            stream = null;
            detail = string.Empty;

            if (streamId.IsDefault)
            {
                detail = "a random stream requires a stable non-zero identity (P-004).";
                return false;
            }

            if (streams.ContainsKey(streamId))
            {
                detail = "stream " + streamId.ToString() + " is already declared (P-004).";
                return false;
            }

            var declared = new RngStream(streamId, streamKey, seed);
            streams.Add(streamId, declared);
            order.Add(streamId);
            stream = declared;
            return true;
        }

        /// <summary>Resolves one declared stream; a miss is a value and never an implicitly created stream (P-008).</summary>
        public bool TryGet(Id128 streamId, out RngStream? stream) => streams.TryGetValue(streamId, out stream);

        /// <summary>Every stream's record, in canonical identity order, for a capture (P-053).</summary>
        public IReadOnlyList<RngRecordValue> ToRecords()
        {
            List<Id128> sorted = new List<Id128>(order);
            sorted.Sort();

            var records = new List<RngRecordValue>(sorted.Count);
            for (int i = 0; i < sorted.Count; i++)
            {
                records.Add(streams[sorted[i]].ToRecord());
            }

            return records;
        }

        /// <summary>
        /// Restores a table from records. A duplicate identity is refused, so a corrupt document cannot inject two
        /// states for one stream (P-004, P-053).
        /// </summary>
        public static bool TryRestore(
            IReadOnlyList<RngRecordValue>? records,
            out RngStreamTable? table,
            out string detail)
        {
            table = null;
            detail = string.Empty;

            var restored = new RngStreamTable();
            if (records == null)
            {
                table = restored;
                return true;
            }

            for (int i = 0; i < records.Count; i++)
            {
                RngRecordValue record = records[i];
                if (record.StreamId.IsDefault)
                {
                    detail = "record " + i.ToString(CultureInfo.InvariantCulture)
                        + " carries the all-zero stream identity (P-004).";
                    return false;
                }

                if (restored.streams.ContainsKey(record.StreamId))
                {
                    detail = "stream " + record.StreamId.ToString()
                        + " appears more than once; one stream has one saved state (P-004, P-053).";
                    return false;
                }

                RngStream stream = RngStream.Restore(record);
                restored.streams.Add(record.StreamId, stream);
                restored.order.Add(record.StreamId);
            }

            table = restored;
            return true;
        }

        public override string ToString() =>
            "rngStreams=" + Count.ToString(CultureInfo.InvariantCulture);
    }
}

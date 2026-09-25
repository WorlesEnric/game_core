// GameCore.Unity.Adapters.Input — device samples and the small binding table that turns them into typed commands
// (GC-019).
//
// Normative sources: 00 P-037 (each step's batch is sealed by a host-assigned admission sequence; commands arriving
// after the cutoff wait), P-042 (a command carries a request key, target stable ids, payload schema/revision and an
// optional expected domain version), P-008 (a device ordinal is an explicit numeric key, never a registration order)
// and 04 s7's Input row: "Host samples UI/device input; validates and stamps typed commands with source
// sequence/world identity. A sampled key press is not itself a committed game event. First slice uses test commands
// and ordinary Unity callbacks, so no Input System package pin is implied."
//
// The binding table is deliberately tiny and has no callback: a binding says "device code N of kind K produces this
// route/target/schema with a canonical 4-byte big-endian scalar payload". A domain with a richer payload (the card
// family's 40-byte command, the narrative family's choice pair) builds its own `SampledInputCommand` and submits it
// through `TypedInputIngress`, which is the same seam with a payload the adapter does not need to understand.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Unity.Adapters.Input
{
    /// <summary>Which kind of device produced one sample (P-008: an explicit kind, not a discovered scheme).</summary>
    public enum InputDeviceKind
    {
        /// <summary>A discrete press/click; the value is 1 while held.</summary>
        Button = 0,

        /// <summary>A continuous axis reading, reported as a bounded integer.</summary>
        Axis = 1,
    }

    /// <summary>One raw device reading, before any command identity exists (04 s7).</summary>
    public readonly struct DeviceInputSample
    {
        public readonly InputDeviceKind Kind;

        /// <summary>The device's own ordinal key; declared by the binding, never inferred.</summary>
        public readonly int Code;

        /// <summary>The sampled value; a button reports 1, an axis reports its quantized reading.</summary>
        public readonly int Value;

        public DeviceInputSample(InputDeviceKind kind, int code, int value)
        {
            Kind = kind;
            Code = code;
            Value = value;
        }

        public override string ToString() =>
            Kind + "#" + Code.ToString(CultureInfo.InvariantCulture)
            + "=" + Value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// One device-to-command binding. The payload shape is the canonical 05 s6 scalar stream, so the adapter needs no
    /// domain knowledge to stamp a press into a legal envelope (P-042).
    /// </summary>
    public sealed class InputCommandBinding
    {
        public InputCommandBinding(
            InputDeviceKind kind,
            int code,
            RouteId route,
            TargetId target,
            SchemaRef schema,
            ulong? expectedDomainVersion)
        {
            Kind = kind;
            Code = code;
            Route = route;
            Target = target;
            Schema = schema;
            ExpectedDomainVersion = expectedDomainVersion;
        }

        public InputDeviceKind Kind { get; }

        public int Code { get; }

        public RouteId Route { get; }

        public TargetId Target { get; }

        public SchemaRef Schema { get; }

        public ulong? ExpectedDomainVersion { get; }

        /// <summary>
        /// Binds one sample to a stamped command. The payload is the sample's value as one big-endian `int32`, which
        /// is the wire convention the payload codecs in both reference families read (05 s6).
        /// </summary>
        public SampledInputCommand Bind(InputSourceStamp stamp, DeviceInputSample sample) =>
            new SampledInputCommand(
                stamp,
                Route,
                Target,
                Schema,
                ExpectedDomainVersion,
                new FrozenPayload(CommandPayloadCodec.Int32(sample.Value)));

        public override string ToString() =>
            Kind + "#" + Code.ToString(CultureInfo.InvariantCulture) + "->" + Route.ToString();
    }

    /// <summary>
    /// The declared bindings of one world's input adapter. Lookup is by (kind, code) in canonical numeric order, so
    /// the binding a press resolves to never depends on insertion order (P-008).
    /// </summary>
    public sealed class InputBindingTable
    {
        private readonly Dictionary<ulong, InputCommandBinding> bindings = new Dictionary<ulong, InputCommandBinding>();

        public int Count => bindings.Count;

        public InputBindingTable Add(InputCommandBinding binding)
        {
            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }

            bindings[KeyOf(binding.Kind, binding.Code)] = binding;
            return this;
        }

        public bool TryFind(InputDeviceKind kind, int code, out InputCommandBinding? binding) =>
            bindings.TryGetValue(KeyOf(kind, code), out binding);

        /// <summary>Bindings in canonical (kind, code) order; the deterministic order a fixture can assert.</summary>
        public IReadOnlyList<InputCommandBinding> Ordered()
        {
            var all = new List<InputCommandBinding>(bindings.Values);
            all.Sort((left, right) =>
            {
                int byKind = left.Kind.CompareTo(right.Kind);
                return byKind != 0 ? byKind : left.Code.CompareTo(right.Code);
            });
            return all;
        }

        private static ulong KeyOf(InputDeviceKind kind, int code) =>
            ((ulong)(uint)kind << 32) | (uint)code;
    }

    /// <summary>Canonical big-endian scalar payload bytes (05 s6), so the adapter never invents a wire shape.</summary>
    public static class CommandPayloadCodec
    {
        /// <summary>Bytes one big-endian `int32` scalar occupies.</summary>
        public const int Int32Bytes = 4;

        public static byte[] Int32(int value)
        {
            unchecked
            {
                uint raw = (uint)value;
                return new[]
                {
                    (byte)(raw >> 24),
                    (byte)(raw >> 16),
                    (byte)(raw >> 8),
                    (byte)raw,
                };
            }
        }

        /// <summary>Reads one big-endian `int32`; a payload of another length is refused.</summary>
        public static bool TryReadInt32(IReadOnlyList<byte>? payload, out int value)
        {
            value = 0;
            if (payload == null || payload.Count != Int32Bytes)
            {
                return false;
            }

            unchecked
            {
                uint raw = ((uint)payload[0] << 24)
                    | ((uint)payload[1] << 16)
                    | ((uint)payload[2] << 8)
                    | payload[3];
                value = (int)raw;
            }

            return true;
        }
    }
}

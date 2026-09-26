// GameCore.Unity.Runtime - the checkpoint codec adapter over the generated catalog (GC-018).
//
// Normative sources: 04 s8 (a runtime type is reached through a generated direct constructor reference, never
// reflection or runtime type discovery) and 05 s6 / P-054 (generated serializers replace reflection-based type
// construction; no CLR assembly name or engine handle serves as identity).
//
// The generated catalog (`GameCore.Validation.GeneratedCheckpoint.CheckpointCatalog`) exposes one serializer class
// per record schema, and each generated serializer takes the catalog's own nested value struct. The engine-free
// `ICheckpointRecordCodec<TValue>` seam is typed to the `GameCore.Contracts` record values, so this file is the
// only place that adapts between the two: one bridge per record kind holds the generated serializer's own
// `Serialize` and `TryDeserialize` methods as direct references plus two conversion delegates, and every encode
// and decode calls the actual generated method. Nothing is resolved by name, by reflection or by runtime type
// discovery, so IL2CPP stripping cannot remove a serializer this assembly names (04 s8 item 3).
//
// The Unity project's generated-checkpoint assembly cannot be referenced from this package (the dependency would run
// the wrong way), so this adapter is deliberately parameterised: the caller passes the thirteen generated serializers
// with their record-value conversions and receives a complete `CheckpointCodecSet`. The Unity validation project's
// family hosts do that with the committed generated catalog.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Unity.Runtime.Persistence
{
    /// <summary>
    /// One generated serializer as the codec seam sees it: the record value type the seam carries, with the
    /// generated `Serialize`/`TryDeserialize` pair behind it. The runtime package cannot name a catalog's nested
    /// value struct, so callers that hold both types bind
    /// <see cref="CheckpointRecordSerializer{TValue, TGenerated}"/> and everything downstream sees only
    /// <typeparamref name="TValue"/> (P-054).
    /// </summary>
    /// <typeparam name="TValue">The `GameCore.Contracts` record value this serializer serves at the seam.</typeparam>
    public interface ICheckpointRecordSerializer<TValue> where TValue : struct
    {
        /// <summary>Exact schema and version the bound generated serializer accepts (P-054).</summary>
        SchemaRef Schema { get; }

        /// <summary>Encodes one value by calling the bound generated serializer's own `Serialize`.</summary>
        byte[] Serialize(TValue value);

        /// <summary>Decodes one document by calling the bound generated serializer's own `TryDeserialize`.</summary>
        bool TryDeserialize(byte[] document, out TValue value, out EnvelopeError error);

        /// <summary>Envelope-level validation only, for the non-generic half of the seam (05 s6).</summary>
        bool TryValidate(byte[] document, out EnvelopeError error);
    }

    /// <summary>
    /// The thirteen generated serializers of one checkpoint catalog, as the adapter needs them: one instance per
    /// record kind, each exposing the generated `Serialize`/`TryDeserialize` pair of its own record type.
    /// </summary>
    public sealed class CheckpointSerializerBindings
    {
        public CheckpointSerializerBindings(
            ICheckpointRecordSerializer<HeaderRecordValue> header,
            ICheckpointRecordSerializer<ScopeRecordValue> scope,
            ICheckpointRecordSerializer<InstallRecordValue> install,
            ICheckpointRecordSerializer<SelectionRecordValue> selection,
            ICheckpointRecordSerializer<TargetRecordValue> target,
            ICheckpointRecordSerializer<SlotRecordValue> slot,
            ICheckpointRecordSerializer<GrantRecordValue> grant,
            ICheckpointRecordSerializer<ClockRecordValue> clock,
            ICheckpointRecordSerializer<CommandRecordValue> command,
            ICheckpointRecordSerializer<MessageRecordValue> message,
            ICheckpointRecordSerializer<RngRecordValue> rng,
            ICheckpointRecordSerializer<CursorRecordValue> cursor,
            ICheckpointRecordSerializer<OutboxRecordValue> outbox)
        {
            Header = header ?? throw new ArgumentNullException(nameof(header));
            Scope = scope ?? throw new ArgumentNullException(nameof(scope));
            Install = install ?? throw new ArgumentNullException(nameof(install));
            Selection = selection ?? throw new ArgumentNullException(nameof(selection));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Slot = slot ?? throw new ArgumentNullException(nameof(slot));
            Grant = grant ?? throw new ArgumentNullException(nameof(grant));
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            Command = command ?? throw new ArgumentNullException(nameof(command));
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Rng = rng ?? throw new ArgumentNullException(nameof(rng));
            Cursor = cursor ?? throw new ArgumentNullException(nameof(cursor));
            Outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        }

        public ICheckpointRecordSerializer<HeaderRecordValue> Header { get; }

        public ICheckpointRecordSerializer<ScopeRecordValue> Scope { get; }

        public ICheckpointRecordSerializer<InstallRecordValue> Install { get; }

        public ICheckpointRecordSerializer<SelectionRecordValue> Selection { get; }

        public ICheckpointRecordSerializer<TargetRecordValue> Target { get; }

        public ICheckpointRecordSerializer<SlotRecordValue> Slot { get; }

        public ICheckpointRecordSerializer<GrantRecordValue> Grant { get; }

        public ICheckpointRecordSerializer<ClockRecordValue> Clock { get; }

        public ICheckpointRecordSerializer<CommandRecordValue> Command { get; }

        public ICheckpointRecordSerializer<MessageRecordValue> Message { get; }

        public ICheckpointRecordSerializer<RngRecordValue> Rng { get; }

        public ICheckpointRecordSerializer<CursorRecordValue> Cursor { get; }

        public ICheckpointRecordSerializer<OutboxRecordValue> Outbox { get; }

        /// <summary>Every binding as a codec, in record-kind order, for <see cref="CheckpointCodecSet"/>.</summary>
        public IReadOnlyList<ICheckpointRecordCodec> Codecs() => new ICheckpointRecordCodec[]
        {
            new CheckpointRecordCodec<HeaderRecordValue>(CheckpointRecordKind.Header, Header),
            new CheckpointRecordCodec<ScopeRecordValue>(CheckpointRecordKind.Scope, Scope),
            new CheckpointRecordCodec<InstallRecordValue>(CheckpointRecordKind.Install, Install),
            new CheckpointRecordCodec<SelectionRecordValue>(CheckpointRecordKind.Selection, Selection),
            new CheckpointRecordCodec<TargetRecordValue>(CheckpointRecordKind.Target, Target),
            new CheckpointRecordCodec<SlotRecordValue>(CheckpointRecordKind.Slot, Slot),
            new CheckpointRecordCodec<GrantRecordValue>(CheckpointRecordKind.Grant, Grant),
            new CheckpointRecordCodec<ClockRecordValue>(CheckpointRecordKind.Clock, Clock),
            new CheckpointRecordCodec<CommandRecordValue>(CheckpointRecordKind.Command, Command),
            new CheckpointRecordCodec<MessageRecordValue>(CheckpointRecordKind.Message, Message),
            new CheckpointRecordCodec<RngRecordValue>(CheckpointRecordKind.Rng, Rng),
            new CheckpointRecordCodec<CursorRecordValue>(CheckpointRecordKind.Cursor, Cursor),
            new CheckpointRecordCodec<OutboxRecordValue>(CheckpointRecordKind.Outbox, Outbox),
        };

        /// <summary>A complete codec set, which a whole-world capture and restore require (P-053).</summary>
        public CheckpointCodecSet ToCodecSet() => new CheckpointCodecSet(Codecs());
    }

    /// <summary>
    /// The generated `TryDeserialize` shape of one record serializer: a document in, a value and the exact envelope
    /// error out. A generated serializer's own method group binds to this without any generated-file change.
    /// </summary>
    /// <typeparam name="TValue">The record value type this serializer serves.</typeparam>
    public delegate bool CheckpointDeserialize<TValue>(
        byte[] document,
        out TValue value,
        out EnvelopeError error) where TValue : struct;

    /// <summary>
    /// One generated serializer bound to its record kind across two value types: <typeparamref name="TValue"/> is
    /// the record value the codec seam carries, <typeparamref name="TGenerated"/> is the nested value struct the
    /// generated serializer's own methods take. The bridge holds the generated instance's `Serialize` and
    /// `TryDeserialize` methods as direct references, so the codec set is a set of direct references and no
    /// serializer is resolved by name at runtime (04 s8). The two conversion delegates are field-for-field copies
    /// the caller supplies once per world; construction happens once per world, never per record. Each encode and
    /// each decode calls the actual generated method — the bridge only moves the value across the two types.
    /// </summary>
    /// <typeparam name="TValue">The `GameCore.Contracts` record value this serializer serves at the seam.</typeparam>
    /// <typeparam name="TGenerated">The generated catalog's nested value struct of the same record kind.</typeparam>
    public sealed class CheckpointRecordSerializer<TValue, TGenerated> : ICheckpointRecordSerializer<TValue>
        where TValue : struct
        where TGenerated : struct
    {
        private readonly Func<TGenerated, byte[]> serialize;
        private readonly CheckpointDeserialize<TGenerated> deserialize;
        private readonly Func<TValue, TGenerated> toGenerated;
        private readonly Func<TGenerated, TValue> fromGenerated;

        public CheckpointRecordSerializer(
            SchemaRef schema,
            Func<TGenerated, byte[]> serialize,
            CheckpointDeserialize<TGenerated> deserialize,
            Func<TValue, TGenerated> toGenerated,
            Func<TGenerated, TValue> fromGenerated)
        {
            Schema = schema;
            this.serialize = serialize ?? throw new ArgumentNullException(nameof(serialize));
            this.deserialize = deserialize ?? throw new ArgumentNullException(nameof(deserialize));
            this.toGenerated = toGenerated ?? throw new ArgumentNullException(nameof(toGenerated));
            this.fromGenerated = fromGenerated ?? throw new ArgumentNullException(nameof(fromGenerated));
        }

        /// <summary>Exact schema and version the bound generated serializer accepts (P-054).</summary>
        public SchemaRef Schema { get; }

        /// <summary>Converts to the generated value struct, then calls the generated `Serialize` itself.</summary>
        public byte[] Serialize(TValue value) => serialize(toGenerated(value));

        /// <summary>
        /// Calls the generated `TryDeserialize` itself; a successful decode is converted to the seam's record
        /// value, and a failed one passes the exact envelope error through with a default value out.
        /// </summary>
        public bool TryDeserialize(byte[] document, out TValue value, out EnvelopeError error)
        {
            if (deserialize(document, out TGenerated generated, out error))
            {
                value = fromGenerated(generated);
                return true;
            }

            value = default(TValue);
            return false;
        }

        /// <summary>Envelope-level validation only, for the non-generic half of the seam (05 s6).</summary>
        public bool TryValidate(byte[] document, out EnvelopeError error)
            => deserialize(document, out TGenerated _, out error);

        public override string ToString()
            => "serializer(" + typeof(TValue).Name + "->" + typeof(TGenerated).Name + "@" + Schema.ToString() + ")";
    }

    /// <summary>Adapts one generated record serializer to the engine-free codec seam (P-054).</summary>
    public sealed class CheckpointRecordCodec<TValue> : ICheckpointRecordCodec<TValue> where TValue : struct
    {
        private readonly ICheckpointRecordSerializer<TValue> serializer;

        public CheckpointRecordCodec(CheckpointRecordKind kind, ICheckpointRecordSerializer<TValue> serializer)
        {
            if (!CheckpointFormat.IsDeclared(kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown checkpoint record kind.");
            }

            Kind = kind;
            this.serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        public CheckpointRecordKind Kind { get; }

        public SchemaRef Schema => serializer.Schema;

        public byte[] Encode(TValue value) => serializer.Serialize(value);

        public bool TryDecode(byte[] document, out TValue value, out EnvelopeError error)
            => serializer.TryDeserialize(document, out value, out error);

        public bool TryValidate(byte[] document, out EnvelopeError error) => serializer.TryValidate(document, out error);

        public override string ToString() => Kind.ToString() + "@" + Schema.ToString();
    }

    /// <summary>
    /// Builds the checkpoint codec set of one world from the generated serializers the caller supplies. A missing
    /// binding is reported rather than substituted, and the resulting set's completeness is the caller's evidence
    /// that every required serializer exists before a capture starts (P-053, P-054).
    /// </summary>
    public static class CheckpointCodecAdapter
    {
        /// <summary>The complete codec set of one checkpoint catalog revision.</summary>
        public static CheckpointCodecSet ToCodecSet(CheckpointSerializerBindings bindings)
        {
            if (bindings == null)
            {
                throw new ArgumentNullException(nameof(bindings));
            }

            return bindings.ToCodecSet();
        }

        /// <summary>
        /// Validates that every generated serializer serves the schema its record kind declares. The check is against
        /// <paramref name="expected"/> pairs, so a catalog whose serializer drifted to another schema is refused
        /// before a capture writes a document under the wrong schema id (P-054).
        /// </summary>
        public static bool TryValidateSchemas(
            CheckpointSerializerBindings bindings,
            IReadOnlyList<SchemaRef> expected,
            out DiagnosticCode code,
            out string detail)
        {
            if (bindings == null)
            {
                throw new ArgumentNullException(nameof(bindings));
            }

            if (expected == null)
            {
                code = DiagnosticCode.None;
                detail = string.Empty;
                return true;
            }

            IReadOnlyList<ICheckpointRecordCodec> codecs = bindings.Codecs();
            for (int i = 0; i < expected.Count; i++)
            {
                SchemaRef want = expected[i];
                bool matched = false;
                for (int c = 0; c < codecs.Count; c++)
                {
                    if (codecs[c].Schema.Id.Equals(want.Id))
                    {
                        matched = codecs[c].Schema.Version == want.Version;
                        if (matched)
                        {
                            break;
                        }
                    }
                }

                if (!matched)
                {
                    code = DiagnosticCode.UnsupportedVersion;
                    detail = "no generated serializer serves schema " + want.ToString()
                        + " at the version this build expects (P-054).";
                    return false;
                }
            }

            code = DiagnosticCode.None;
            detail = string.Empty;
            return true;
        }
    }
}

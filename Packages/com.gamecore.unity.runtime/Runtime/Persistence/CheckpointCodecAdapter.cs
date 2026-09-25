// GameCore.Unity.Runtime - the checkpoint codec adapter over the generated catalog (GC-018).
//
// Normative sources: 04 s8 (a runtime type is reached through a generated direct constructor reference, never
// reflection or runtime type discovery) and 05 s6 / P-054 (generated serializers replace reflection-based type
// construction; no CLR assembly name or engine handle serves as identity).
//
// The generated catalog (`GameCore.Validation.GeneratedCheckpoint.CheckpointCatalog`) exposes one serializer class
// per record schema. This file is the only place that adapts those generated serializers to the engine-free
// `ICheckpointRecordCodec<TValue>` seam: one small generic wrapper per record kind, constructed with the generated
// serializer instance. Nothing is resolved by name, and the adapter is a direct reference, so IL2CPP stripping
// cannot remove a serializer this assembly names (04 s8 item 3).
//
// The Unity project's generated-checkpoint assembly cannot be referenced from this package (the dependency would run
// the wrong way), so this adapter is deliberately parameterised: the caller passes the twelve generated serializers
// and receives a complete `CheckpointCodecSet`. The Unity validation project's family hosts do that with the
// committed generated catalog.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Persistence;

namespace GameCore.Unity.Runtime.Persistence
{
    /// <summary>
    /// The twelve generated serializers of one checkpoint catalog, as the adapter needs them: one instance per record
    /// kind, each exposing the generated `Serialize`/`TryDeserialize` pair of its own record type.
    /// </summary>
    public sealed class CheckpointSerializerBindings
    {
        public CheckpointSerializerBindings(
            CheckpointRecordSerializer<HeaderRecordValue> header,
            CheckpointRecordSerializer<ScopeRecordValue> scope,
            CheckpointRecordSerializer<InstallRecordValue> install,
            CheckpointRecordSerializer<SelectionRecordValue> selection,
            CheckpointRecordSerializer<TargetRecordValue> target,
            CheckpointRecordSerializer<SlotRecordValue> slot,
            CheckpointRecordSerializer<GrantRecordValue> grant,
            CheckpointRecordSerializer<ClockRecordValue> clock,
            CheckpointRecordSerializer<CommandRecordValue> command,
            CheckpointRecordSerializer<MessageRecordValue> message,
            CheckpointRecordSerializer<RngRecordValue> rng,
            CheckpointRecordSerializer<CursorRecordValue> cursor)
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
        }

        public CheckpointRecordSerializer<HeaderRecordValue> Header { get; }

        public CheckpointRecordSerializer<ScopeRecordValue> Scope { get; }

        public CheckpointRecordSerializer<InstallRecordValue> Install { get; }

        public CheckpointRecordSerializer<SelectionRecordValue> Selection { get; }

        public CheckpointRecordSerializer<TargetRecordValue> Target { get; }

        public CheckpointRecordSerializer<SlotRecordValue> Slot { get; }

        public CheckpointRecordSerializer<GrantRecordValue> Grant { get; }

        public CheckpointRecordSerializer<ClockRecordValue> Clock { get; }

        public CheckpointRecordSerializer<CommandRecordValue> Command { get; }

        public CheckpointRecordSerializer<MessageRecordValue> Message { get; }

        public CheckpointRecordSerializer<RngRecordValue> Rng { get; }

        public CheckpointRecordSerializer<CursorRecordValue> Cursor { get; }

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
    /// One generated serializer bound to its record kind. It holds the generated instance's own `Serialize` and
    /// `TryDeserialize` methods as direct references, so the codec set is a set of direct references and no
    /// serializer is resolved by name at runtime (04 s8). Construction happens once per world, never per record.
    /// </summary>
    public sealed class CheckpointRecordSerializer<TValue> where TValue : struct
    {
        private readonly Func<TValue, byte[]> serialize;
        private readonly CheckpointDeserialize<TValue> deserialize;

        public CheckpointRecordSerializer(
            SchemaRef schema,
            Func<TValue, byte[]> serialize,
            CheckpointDeserialize<TValue> deserialize)
        {
            Schema = schema;
            this.serialize = serialize ?? throw new ArgumentNullException(nameof(serialize));
            this.deserialize = deserialize ?? throw new ArgumentNullException(nameof(deserialize));
        }

        /// <summary>Exact schema and version the bound generated serializer accepts (P-054).</summary>
        public SchemaRef Schema { get; }

        public byte[] Serialize(TValue value) => serialize(value);

        public bool TryDeserialize(byte[] document, out TValue value, out EnvelopeError error)
            => deserialize(document, out value, out error);

        /// <summary>Envelope-level validation only, for the non-generic half of the seam (05 s6).</summary>
        public bool TryValidate(byte[] document, out EnvelopeError error)
            => deserialize(document, out TValue _, out error);

        public override string ToString() => "serializer(" + typeof(TValue).Name + "@" + Schema.ToString() + ")";
    }

    /// <summary>Adapts one generated record serializer to the engine-free codec seam (P-054).</summary>
    public sealed class CheckpointRecordCodec<TValue> : ICheckpointRecordCodec<TValue> where TValue : struct
    {
        private readonly CheckpointRecordSerializer<TValue> serializer;

        public CheckpointRecordCodec(CheckpointRecordKind kind, CheckpointRecordSerializer<TValue> serializer)
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

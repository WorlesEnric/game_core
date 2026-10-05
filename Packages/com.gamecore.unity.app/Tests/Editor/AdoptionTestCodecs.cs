#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Unity.App.Tests
{
    // The adoption fixture exercises real capture, file storage, restore, composition and lifecycle with a
    // per-fixture record codec double. Wire-format coverage belongs to the P1.2 persistence suite.
    internal static class AdoptionTestCodecs
    {
        public static CheckpointCodecSet Create() => new CheckpointCodecSet(new ICheckpointRecordCodec[]
        {
            new RecordCodec<HeaderRecordValue>(CheckpointRecordKind.Header),
            new RecordCodec<ScopeRecordValue>(CheckpointRecordKind.Scope),
            new RecordCodec<InstallRecordValue>(CheckpointRecordKind.Install),
            new RecordCodec<SelectionRecordValue>(CheckpointRecordKind.Selection),
            new RecordCodec<TargetRecordValue>(CheckpointRecordKind.Target),
            new RecordCodec<SlotRecordValue>(CheckpointRecordKind.Slot),
            new RecordCodec<GrantRecordValue>(CheckpointRecordKind.Grant),
            new RecordCodec<ClockRecordValue>(CheckpointRecordKind.Clock),
            new RecordCodec<CommandRecordValue>(CheckpointRecordKind.Command),
            new RecordCodec<MessageRecordValue>(CheckpointRecordKind.Message),
            new RecordCodec<RngRecordValue>(CheckpointRecordKind.Rng),
            new RecordCodec<CursorRecordValue>(CheckpointRecordKind.Cursor),
            new RecordCodec<OutboxRecordValue>(CheckpointRecordKind.Outbox),
        });

        private sealed class RecordCodec<T> : ICheckpointRecordCodec<T> where T : struct
        {
            private readonly List<T> records = new List<T>();
            public RecordCodec(CheckpointRecordKind kind)
            {
                Kind = kind;
                Schema = new SchemaRef(new SchemaId(StableNameKeyDerivation.Derive("app1.codec." + kind.ToString().ToLowerInvariant())), 1U);
            }
            public CheckpointRecordKind Kind { get; }
            public SchemaRef Schema { get; }
            public byte[] Encode(T value)
            {
                int index = records.IndexOf(value);
                if (index < 0) { index = records.Count; records.Add(value); }
                return BitConverter.GetBytes(index);
            }
            public bool TryValidate(byte[] document, out EnvelopeError error)
            {
                bool valid = document.Length == 4 && BitConverter.ToInt32(document, 0) >= 0
                    && BitConverter.ToInt32(document, 0) < records.Count;
                error = valid ? EnvelopeError.None : EnvelopeError.Truncated;
                return valid;
            }
            public bool TryDecode(byte[] document, out T value, out EnvelopeError error)
            {
                bool valid = TryValidate(document, out error);
                value = valid ? records[BitConverter.ToInt32(document, 0)] : default;
                return valid;
            }
        }
    }
}

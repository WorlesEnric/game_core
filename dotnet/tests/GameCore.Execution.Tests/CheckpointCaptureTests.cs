// GameCore.Execution.Tests - checkpoint capture tests and the test-only record codecs they need (GC-018, O-20,
// P-053).
//
// The execution package is engine-free on purpose, so a capture can be exercised with no Unity world at all: the
// boundary seam (ICommittedBoundaryReader) is the only thing capture knows about a running world. These tests drive
// the real CheckpointCapture over test-only codecs hand-written on EnvelopeWriter/EnvelopeReader, in the field order
// the committed checkpoint catalog declares (unity/GameCore.Validation/Catalogs/CheckpointCatalog.catalog.json), so a
// capture that succeeds produces a document CheckpointDocument.TryRead accepts and every record decodes back.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Persistence;
using NUnit.Framework;

namespace GameCore.Execution.Tests
{
    /// <summary>Stable identities of the checkpoint fixtures; one namespace word per identity category.</summary>
    internal static class CheckpointTestIds
    {
        internal static readonly Id128 WorldSession = new Id128(0x4743303138574F52UL, 1UL);
        internal static readonly Id128 ForeignSession = new Id128(0x4743303138464F52UL, 1UL);
        internal static readonly Id128 RestoredSession = new Id128(0x4743303138524553UL, 1UL);
        internal static readonly Id128 UnusedSession = new Id128(0x4743303138554E55UL, 1UL);
        internal static readonly Id128 WorldDefinition = new Id128(0x4743303138444546UL, 1UL);
        internal static readonly Id128 RootScope = new Id128(0x474330313853434FUL, 1UL);
        internal static readonly Id128 ChildScope = new Id128(0x474330313853434FUL, 2UL);
        internal static readonly Id128 SecondRootScope = new Id128(0x474330313853434FUL, 3UL);
        internal static readonly Id128 CycleScopeA = new Id128(0x4743303138435941UL, 1UL);
        internal static readonly Id128 CycleScopeB = new Id128(0x4743303138435942UL, 1UL);
        internal static readonly Id128 Installation = new Id128(0x4743303138494E53UL, 1UL);
        internal static readonly Id128 PluginType = new Id128(0x4743303138504C47UL, 1UL);
        internal static readonly Id128 Contract = new Id128(0x4743303138434F4EUL, 1UL);
        internal static readonly Id128 TargetA = new Id128(0x4743303138544152UL, 1UL);
        internal static readonly Id128 TargetB = new Id128(0x4743303138544152UL, 2UL);
        internal static readonly Id128 UndeclaredTarget = new Id128(0x4743303138554E44UL, 1UL);
        internal static readonly Id128 Owner = new Id128(0x47433031384F574EUL, 1UL);
        internal static readonly Id128 SlotActive = new Id128(0x4743303138534C54UL, 1UL);
        internal static readonly Id128 SlotDormant = new Id128(0x4743303138534C54UL, 2UL);
        internal static readonly Id128 SlotOther = new Id128(0x4743303138534C54UL, 3UL);
        internal static readonly Id128 Clock = new Id128(0x4743303138434C4BUL, 1UL);
        internal static readonly Id128 Wake = new Id128(0x474330313857414BUL, 1UL);
        internal static readonly Id128 RngStream = new Id128(0x4743303138524E47UL, 1UL);
        internal static readonly Id128 Capability = new Id128(0x4743303138434150UL, 1UL);
        internal static readonly Id128 RecipeDefinition = new Id128(0x4743303138524350UL, 1UL);
        internal static readonly Id128 RecipeSchema = new Id128(0x4743303138525343UL, 1UL);
        internal static readonly Id128 CommandIssuer = new Id128(0x4743303138434D44UL, 1UL);
        internal static readonly Id128 CommandRoute = new Id128(0x4743303138524F55UL, 1UL);
        internal static readonly Id128 MessageBuffer = new Id128(0x4743303138425546UL, 1UL);
        internal static readonly Id128 MessageProducer = new Id128(0x474330313850524FUL, 1UL);

        /// <summary>Catalog fingerprint of the fixture boundary, distinct from any fingerprint a request supplies.</summary>
        internal static readonly ContentHash CatalogFingerprint =
            ContentHash.Compute(new byte[] { 0x47, 0x43, 0x30, 0x31, 0x38, 0x43, 0x41, 0x54 });
    }

    /// <summary>The schema of each checkpoint record kind, taken from the committed checkpoint catalog (P-054).</summary>
    internal static class CheckpointTestSchemas
    {
        internal static readonly SchemaRef Header = Schema("d578e5fe72e484be8d8c015a17d8b66b");
        internal static readonly SchemaRef Scope = Schema("1bb5c533dab84e7604c8b98f1b97066a");
        internal static readonly SchemaRef Install = Schema("5666c5db5d34ad37d470140f8c18808f");
        internal static readonly SchemaRef Selection = Schema("4736afccc12b5b625adb5bcdbb714e3f");
        internal static readonly SchemaRef Target = Schema("b780328c6fa158f7bd88603db3ebae2f");
        internal static readonly SchemaRef Slot = Schema("325ebfc76f751ee2c2b4985df3d088a0");
        internal static readonly SchemaRef Grant = Schema("54d0f9cccec243b78414ec19245173c6");
        internal static readonly SchemaRef Clock = Schema("49f4779fa88d83ea0440e2bab679d545");
        internal static readonly SchemaRef Command = Schema("efe5ab3007e3f7b821d96f9264ab31ef");
        internal static readonly SchemaRef Message = Schema("744d60d006798058b50b656c613a21f3");
        internal static readonly SchemaRef Rng = Schema("cd069d4291975928edb3f9e9af5e037d");
        internal static readonly SchemaRef Cursor = Schema("1ddc64b87daa5363e1c1951d44ad5f07");

        private static SchemaRef Schema(string canonicalHex)
        {
            if (!Id128Codec.TryParseHex(canonicalHex, out Id128 id))
            {
                throw new ArgumentException("not a canonical 32-character lowercase hex id.", nameof(canonicalHex));
            }

            return new SchemaRef(new SchemaId(id), 1U);
        }
    }

    /// <summary>
    /// Typed access to the declared fields of one validated record document. Field ids of every checkpoint record
    /// schema are contiguous and ascending from 1 and every declared field is required, so a slot's index is its
    /// zero-based position in the schema's field table.
    /// </summary>
    internal readonly struct RecordFields
    {
        private readonly EnvelopeReader reader;
        private readonly GeneratedFieldBuffer buffer;
        private readonly int count;

        internal RecordFields(EnvelopeReader reader, GeneratedFieldBuffer buffer, int count)
        {
            this.reader = reader;
            this.buffer = buffer;
            this.count = count;
        }

        internal EnvelopeError Error => reader.LastError;

        internal bool UInt32(int index, out uint value)
        {
            value = 0;
            return Seek(index) && reader.TryReadUInt32(buffer.Field(index), out value);
        }

        internal bool UInt64(int index, out ulong value)
        {
            value = 0;
            return Seek(index) && reader.TryReadUInt64(buffer.Field(index), out value);
        }

        internal bool Int32(int index, out int value)
        {
            value = 0;
            return Seek(index) && reader.TryReadInt32(buffer.Field(index), out value);
        }

        internal bool Bool(int index, out bool value)
        {
            value = false;
            return Seek(index) && reader.TryReadBool(buffer.Field(index), out value);
        }

        internal bool Bytes(int index, out byte[]? value)
        {
            value = null;
            return Seek(index) && reader.TryReadBytes(buffer.Field(index), out value);
        }

        internal bool Float64(int index, out double value)
        {
            if (!Seek(index))
            {
                value = 0d;
                return false;
            }

            return reader.TryReadFloat64(buffer.Field(index), out value, out ulong _);
        }

        private bool Seek(int index)
        {
            if (index < 0 || index >= count || index >= buffer.Count
                || !reader.TrySeekTo(buffer.RecordOffset(index))
                || !reader.TryReadField(out EnvelopeField field))
            {
                return false;
            }

            return field.FieldId == buffer.Field(index).FieldId && field.Type == buffer.Field(index).Type;
        }
    }

    /// <summary>
    /// Base of the test-only record codecs. Encoding writes the declared field ids in ascending order and the
    /// trailing checksum; validation is the generated one-pass walk against the schema's field table, so a record
    /// document a codec produced is one a generated reader accepts (P-054).
    /// </summary>
    internal abstract class CheckpointTestRecordCodec<TValue> : ICheckpointRecordCodec<TValue>
        where TValue : struct
    {
        private readonly GeneratedFieldSlot[] slots;

        protected CheckpointTestRecordCodec(CheckpointRecordKind kind, SchemaRef schema, GeneratedFieldSlot[] slots)
        {
            Kind = kind;
            Schema = schema;
            this.slots = slots;
        }

        public CheckpointRecordKind Kind { get; }

        public SchemaRef Schema { get; }

        public bool TryValidate(byte[] document, out EnvelopeError error) =>
            GeneratedEnvelopeReader.TryReadDeclaredFields(
                document,
                Schema,
                CheckpointFormat.KnownFeatureIds,
                slots,
                new GeneratedFieldBuffer(),
                out error);

        public byte[] Encode(TValue value)
        {
            var writer = new EnvelopeWriter(
                new EnvelopeHeader(
                    CheckpointFormat.ProtocolMajor,
                    CheckpointFormat.ProtocolMinor,
                    Schema,
                    CheckpointFormat.KnownFeatureIds),
                CheckpointFormat.Limits);
            Write(writer, value);
            writer.WriteChecksum();
            return writer.ToArray();
        }

        public bool TryDecode(byte[] document, out TValue value, out EnvelopeError error)
        {
            RecordFields fields;
            if (!TryOpen(document, out fields, out error))
            {
                value = default(TValue);
                return false;
            }

            return Read(fields, out value, out error);
        }

        protected abstract void Write(EnvelopeWriter writer, TValue value);

        protected abstract bool Read(RecordFields fields, out TValue value, out EnvelopeError error);

        private bool TryOpen(byte[] document, out RecordFields fields, out EnvelopeError error)
        {
            var buffer = new GeneratedFieldBuffer();
            if (!GeneratedEnvelopeReader.TryReadDeclaredFields(
                    document,
                    Schema,
                    CheckpointFormat.KnownFeatureIds,
                    slots,
                    buffer,
                    out error))
            {
                fields = default(RecordFields);
                return false;
            }

            fields = new RecordFields(new EnvelopeReader(document), buffer, slots.Length);
            return true;
        }
    }

    /// <summary>Test-only codec for <see cref="HeaderRecordValue"/> (schema d578e5fe72e484be8d8c015a17d8b66b).</summary>
    internal sealed class HeaderRecordCodec : CheckpointTestRecordCodec<HeaderRecordValue>
    {
        private static readonly GeneratedFieldSlot[] Slots =
        {
            new GeneratedFieldSlot(1, WireType.UInt64, true),
            new GeneratedFieldSlot(2, WireType.UInt64, true),
            new GeneratedFieldSlot(3, WireType.UInt64, true),
            new GeneratedFieldSlot(4, WireType.UInt64, true),
            new GeneratedFieldSlot(5, WireType.UInt32, true),
            new GeneratedFieldSlot(6, WireType.UInt32, true),
            new GeneratedFieldSlot(7, WireType.UInt32, true),
            new GeneratedFieldSlot(8, WireType.UInt64, true),
            new GeneratedFieldSlot(9, WireType.UInt64, true),
            new GeneratedFieldSlot(10, WireType.UInt32, true),
            new GeneratedFieldSlot(11, WireType.Bool, true),
            new GeneratedFieldSlot(12, WireType.UInt64, true),
            new GeneratedFieldSlot(13, WireType.UInt64, true),
            new GeneratedFieldSlot(14, WireType.Float64, true),
            new GeneratedFieldSlot(15, WireType.UInt64, true),
            new GeneratedFieldSlot(16, WireType.UInt32, true),
            new GeneratedFieldSlot(17, WireType.UInt64, true),
            new GeneratedFieldSlot(18, WireType.UInt64, true),
            new GeneratedFieldSlot(19, WireType.UInt64, true),
            new GeneratedFieldSlot(20, WireType.UInt64, true),
            new GeneratedFieldSlot(21, WireType.UInt32, true),
            new GeneratedFieldSlot(22, WireType.UInt64, true),
            new GeneratedFieldSlot(23, WireType.UInt32, true),
            new GeneratedFieldSlot(24, WireType.UInt64, true),
            new GeneratedFieldSlot(25, WireType.UInt32, true),
            new GeneratedFieldSlot(26, WireType.UInt32, true),
            new GeneratedFieldSlot(27, WireType.UInt32, true),
            new GeneratedFieldSlot(28, WireType.UInt32, true),
            new GeneratedFieldSlot(29, WireType.UInt32, true),
            new GeneratedFieldSlot(30, WireType.UInt32, true),
            new GeneratedFieldSlot(31, WireType.UInt32, true),
            new GeneratedFieldSlot(32, WireType.UInt32, true),
            new GeneratedFieldSlot(33, WireType.UInt32, true),
            new GeneratedFieldSlot(34, WireType.UInt32, true),
            new GeneratedFieldSlot(35, WireType.UInt32, true),
            new GeneratedFieldSlot(36, WireType.UInt64, true),
            new GeneratedFieldSlot(37, WireType.UInt64, true),
            new GeneratedFieldSlot(38, WireType.UInt64, true),
            new GeneratedFieldSlot(39, WireType.UInt32, true)
        };

        internal HeaderRecordCodec()
            : base(CheckpointRecordKind.Header, CheckpointTestSchemas.Header, Slots)
        {
        }

        protected override void Write(EnvelopeWriter writer, HeaderRecordValue value)
        {
            writer.WriteUInt64Field(1, value.WorldDefinitionHigh);
            writer.WriteUInt64Field(2, value.WorldDefinitionLow);
            writer.WriteUInt64Field(3, value.SourceSessionHigh);
            writer.WriteUInt64Field(4, value.SourceSessionLow);
            writer.WriteUInt32Field(5, value.ProtocolMajor);
            writer.WriteUInt32Field(6, value.ProtocolMinor);
            writer.WriteUInt32Field(7, value.TemporalModel);
            writer.WriteUInt64Field(8, value.StepDurationTicks);
            writer.WriteUInt64Field(9, value.TicksPerSecond);
            writer.WriteUInt32Field(10, value.MaxStepsPerPump);
            writer.WriteBoolField(11, value.UsesUnscaledHostClock);
            writer.WriteUInt64Field(12, value.LogicalStep);
            writer.WriteUInt64Field(13, value.TimeDebtTicks);
            writer.WriteFloat64Field(14, value.DomainSeconds);
            writer.WriteUInt64Field(15, value.PendingDemand);
            writer.WriteUInt32Field(16, value.PropagationMode);
            writer.WriteUInt64Field(17, value.CatalogFingerprintA);
            writer.WriteUInt64Field(18, value.CatalogFingerprintB);
            writer.WriteUInt64Field(19, value.CatalogFingerprintC);
            writer.WriteUInt64Field(20, value.CatalogFingerprintD);
            writer.WriteUInt32Field(21, value.QueuePolicy);
            writer.WriteUInt64Field(22, value.AdmissionCutoff);
            writer.WriteUInt32Field(23, value.RejectedQueuedCount);
            writer.WriteUInt64Field(24, value.LastEventSequence);
            writer.WriteUInt32Field(25, value.ScopeCount);
            writer.WriteUInt32Field(26, value.InstallCount);
            writer.WriteUInt32Field(27, value.SelectionCount);
            writer.WriteUInt32Field(28, value.TargetCount);
            writer.WriteUInt32Field(29, value.SlotCount);
            writer.WriteUInt32Field(30, value.GrantCount);
            writer.WriteUInt32Field(31, value.ClockCount);
            writer.WriteUInt32Field(32, value.CommandCount);
            writer.WriteUInt32Field(33, value.MessageCount);
            writer.WriteUInt32Field(34, value.RngStreamCount);
            writer.WriteUInt32Field(35, value.CursorCount);
            writer.WriteUInt64Field(36, value.SourcePublishedRevision);
            writer.WriteUInt64Field(37, value.SourcePublishedEpoch);
            writer.WriteUInt64Field(38, value.SourceHostTicksPerSecond);
            writer.WriteUInt32Field(39, value.ContentRevisionCount);
        }

        protected override bool Read(RecordFields fields, out HeaderRecordValue value, out EnvelopeError error)
        {
            if (!fields.UInt64(0, out ulong fWorldDefinitionHigh)
                || !fields.UInt64(1, out ulong fWorldDefinitionLow)
                || !fields.UInt64(2, out ulong fSourceSessionHigh)
                || !fields.UInt64(3, out ulong fSourceSessionLow)
                || !fields.UInt32(4, out uint fProtocolMajor)
                || !fields.UInt32(5, out uint fProtocolMinor)
                || !fields.UInt32(6, out uint fTemporalModel)
                || !fields.UInt64(7, out ulong fStepDurationTicks)
                || !fields.UInt64(8, out ulong fTicksPerSecond)
                || !fields.UInt32(9, out uint fMaxStepsPerPump)
                || !fields.Bool(10, out bool fUsesUnscaledHostClock)
                || !fields.UInt64(11, out ulong fLogicalStep)
                || !fields.UInt64(12, out ulong fTimeDebtTicks)
                || !fields.Float64(13, out double fDomainSeconds)
                || !fields.UInt64(14, out ulong fPendingDemand)
                || !fields.UInt32(15, out uint fPropagationMode)
                || !fields.UInt64(16, out ulong fCatalogFingerprintA)
                || !fields.UInt64(17, out ulong fCatalogFingerprintB)
                || !fields.UInt64(18, out ulong fCatalogFingerprintC)
                || !fields.UInt64(19, out ulong fCatalogFingerprintD)
                || !fields.UInt32(20, out uint fQueuePolicy)
                || !fields.UInt64(21, out ulong fAdmissionCutoff)
                || !fields.UInt32(22, out uint fRejectedQueuedCount)
                || !fields.UInt64(23, out ulong fLastEventSequence)
                || !fields.UInt32(24, out uint fScopeCount)
                || !fields.UInt32(25, out uint fInstallCount)
                || !fields.UInt32(26, out uint fSelectionCount)
                || !fields.UInt32(27, out uint fTargetCount)
                || !fields.UInt32(28, out uint fSlotCount)
                || !fields.UInt32(29, out uint fGrantCount)
                || !fields.UInt32(30, out uint fClockCount)
                || !fields.UInt32(31, out uint fCommandCount)
                || !fields.UInt32(32, out uint fMessageCount)
                || !fields.UInt32(33, out uint fRngStreamCount)
                || !fields.UInt32(34, out uint fCursorCount)
                || !fields.UInt64(35, out ulong fSourcePublishedRevision)
                || !fields.UInt64(36, out ulong fSourcePublishedEpoch)
                || !fields.UInt64(37, out ulong fSourceHostTicksPerSecond)
                || !fields.UInt32(38, out uint fContentRevisionCount))
            {
                value = default(HeaderRecordValue);
                error = fields.Error;
                return false;
            }

            value = new HeaderRecordValue(fWorldDefinitionHigh, fWorldDefinitionLow, fSourceSessionHigh, fSourceSessionLow, fProtocolMajor, fProtocolMinor, fTemporalModel, fStepDurationTicks, fTicksPerSecond, fMaxStepsPerPump, fUsesUnscaledHostClock, fLogicalStep, fTimeDebtTicks, fDomainSeconds, fPendingDemand, fPropagationMode, fCatalogFingerprintA, fCatalogFingerprintB, fCatalogFingerprintC, fCatalogFingerprintD, fQueuePolicy, fAdmissionCutoff, fRejectedQueuedCount, fLastEventSequence, fScopeCount, fInstallCount, fSelectionCount, fTargetCount, fSlotCount, fGrantCount, fClockCount, fCommandCount, fMessageCount, fRngStreamCount, fCursorCount, fSourcePublishedRevision, fSourcePublishedEpoch, fSourceHostTicksPerSecond, fContentRevisionCount);
            error = EnvelopeError.None;
            return true;
        }
    }

    /// <summary>Test-only codec for <see cref="ScopeRecordValue"/> (schema 1bb5c533dab84e7604c8b98f1b97066a).</summary>
    internal sealed class ScopeRecordCodec : CheckpointTestRecordCodec<ScopeRecordValue>
    {
        private static readonly GeneratedFieldSlot[] Slots =
        {
            new GeneratedFieldSlot(1, WireType.UInt64, true),
            new GeneratedFieldSlot(2, WireType.UInt64, true),
            new GeneratedFieldSlot(3, WireType.UInt64, true),
            new GeneratedFieldSlot(4, WireType.UInt64, true),
            new GeneratedFieldSlot(5, WireType.UInt32, true),
            new GeneratedFieldSlot(6, WireType.UInt32, true),
            new GeneratedFieldSlot(7, WireType.UInt32, true),
            new GeneratedFieldSlot(8, WireType.UInt32, true),
            new GeneratedFieldSlot(9, WireType.Bool, true),
            new GeneratedFieldSlot(10, WireType.Bool, true),
            new GeneratedFieldSlot(11, WireType.UInt32, true),
            new GeneratedFieldSlot(12, WireType.UInt32, true)
        };

        internal ScopeRecordCodec()
            : base(CheckpointRecordKind.Scope, CheckpointTestSchemas.Scope, Slots)
        {
        }

        protected override void Write(EnvelopeWriter writer, ScopeRecordValue value)
        {
            writer.WriteUInt64Field(1, value.ScopeHigh);
            writer.WriteUInt64Field(2, value.ScopeLow);
            writer.WriteUInt64Field(3, value.ParentHigh);
            writer.WriteUInt64Field(4, value.ParentLow);
            writer.WriteUInt32Field(5, value.Depth);
            writer.WriteUInt32Field(6, value.Mode);
            writer.WriteUInt32Field(7, value.InstallCount);
            writer.WriteUInt32Field(8, value.GrantCount);
            writer.WriteBoolField(9, value.ServiceIsolationAll);
            writer.WriteBoolField(10, value.CapabilityIsolationAll);
            writer.WriteUInt32Field(11, value.ServiceIsolationCount);
            writer.WriteUInt32Field(12, value.CapabilityIsolationCount);
        }

        protected override bool Read(RecordFields fields, out ScopeRecordValue value, out EnvelopeError error)
        {
            if (!fields.UInt64(0, out ulong fScopeHigh)
                || !fields.UInt64(1, out ulong fScopeLow)
                || !fields.UInt64(2, out ulong fParentHigh)
                || !fields.UInt64(3, out ulong fParentLow)
                || !fields.UInt32(4, out uint fDepth)
                || !fields.UInt32(5, out uint fMode)
                || !fields.UInt32(6, out uint fInstallCount)
                || !fields.UInt32(7, out uint fGrantCount)
                || !fields.Bool(8, out bool fServiceIsolationAll)
                || !fields.Bool(9, out bool fCapabilityIsolationAll)
                || !fields.UInt32(10, out uint fServiceIsolationCount)
                || !fields.UInt32(11, out uint fCapabilityIsolationCount))
            {
                value = default(ScopeRecordValue);
                error = fields.Error;
                return false;
            }

            value = new ScopeRecordValue(fScopeHigh, fScopeLow, fParentHigh, fParentLow, fDepth, fMode, fInstallCount, fGrantCount, fServiceIsolationAll, fCapabilityIsolationAll, fServiceIsolationCount, fCapabilityIsolationCount);
            error = EnvelopeError.None;
            return true;
        }
    }

    /// <summary>Test-only codec for <see cref="InstallRecordValue"/> (schema 5666c5db5d34ad37d470140f8c18808f).</summary>
    internal sealed class InstallRecordCodec : CheckpointTestRecordCodec<InstallRecordValue>
    {
        private static readonly GeneratedFieldSlot[] Slots =
        {
            new GeneratedFieldSlot(1, WireType.UInt64, true),
            new GeneratedFieldSlot(2, WireType.UInt64, true),
            new GeneratedFieldSlot(3, WireType.UInt64, true),
            new GeneratedFieldSlot(4, WireType.UInt64, true),
            new GeneratedFieldSlot(5, WireType.UInt64, true),
            new GeneratedFieldSlot(6, WireType.UInt64, true),
            new GeneratedFieldSlot(7, WireType.UInt64, true),
            new GeneratedFieldSlot(8, WireType.UInt64, true),
            new GeneratedFieldSlot(9, WireType.UInt64, true),
            new GeneratedFieldSlot(10, WireType.UInt64, true),
            new GeneratedFieldSlot(11, WireType.UInt64, true),
            new GeneratedFieldSlot(12, WireType.Int32, true),
            new GeneratedFieldSlot(13, WireType.UInt64, true),
            new GeneratedFieldSlot(14, WireType.UInt64, true),
            new GeneratedFieldSlot(15, WireType.UInt32, true),
            new GeneratedFieldSlot(16, WireType.UInt32, true),
            new GeneratedFieldSlot(17, WireType.Bytes, true),
            new GeneratedFieldSlot(18, WireType.UInt32, true),
            new GeneratedFieldSlot(19, WireType.Bool, true)
        };

        internal InstallRecordCodec()
            : base(CheckpointRecordKind.Install, CheckpointTestSchemas.Install, Slots)
        {
        }

        protected override void Write(EnvelopeWriter writer, InstallRecordValue value)
        {
            writer.WriteUInt64Field(1, value.InstanceHigh);
            writer.WriteUInt64Field(2, value.InstanceLow);
            writer.WriteUInt64Field(3, value.PluginTypeHigh);
            writer.WriteUInt64Field(4, value.PluginTypeLow);
            writer.WriteUInt64Field(5, value.ScopeHigh);
            writer.WriteUInt64Field(6, value.ScopeLow);
            writer.WriteUInt64Field(7, value.ConfigRevision);
            writer.WriteUInt64Field(8, value.ConfigHashA);
            writer.WriteUInt64Field(9, value.ConfigHashB);
            writer.WriteUInt64Field(10, value.ConfigHashC);
            writer.WriteUInt64Field(11, value.ConfigHashD);
            writer.WriteInt32Field(12, value.Priority);
            writer.WriteUInt64Field(13, value.Generation);
            writer.WriteUInt64Field(14, value.ActivationEpoch);
            writer.WriteUInt32Field(15, value.State);
            writer.WriteUInt32Field(16, value.ConfigFieldCount);
            writer.WriteBytesField(17, value.ConfigBytes);
            writer.WriteUInt32Field(18, value.SelectionCount);
            writer.WriteBoolField(19, value.HasConfigDocument);
        }

        protected override bool Read(RecordFields fields, out InstallRecordValue value, out EnvelopeError error)
        {
            if (!fields.UInt64(0, out ulong fInstanceHigh)
                || !fields.UInt64(1, out ulong fInstanceLow)
                || !fields.UInt64(2, out ulong fPluginTypeHigh)
                || !fields.UInt64(3, out ulong fPluginTypeLow)
                || !fields.UInt64(4, out ulong fScopeHigh)
                || !fields.UInt64(5, out ulong fScopeLow)
                || !fields.UInt64(6, out ulong fConfigRevision)
                || !fields.UInt64(7, out ulong fConfigHashA)
                || !fields.UInt64(8, out ulong fConfigHashB)
                || !fields.UInt64(9, out ulong fConfigHashC)
                || !fields.UInt64(10, out ulong fConfigHashD)
                || !fields.Int32(11, out int fPriority)
                || !fields.UInt64(12, out ulong fGeneration)
                || !fields.UInt64(13, out ulong fActivationEpoch)
                || !fields.UInt32(14, out uint fState)
                || !fields.UInt32(15, out uint fConfigFieldCount)
                || !fields.Bytes(16, out byte[]? fConfigBytes)
                || !fields.UInt32(17, out uint fSelectionCount)
                || !fields.Bool(18, out bool fHasConfigDocument))
            {
                value = default(InstallRecordValue);
                error = fields.Error;
                return false;
            }

            value = new InstallRecordValue(fInstanceHigh, fInstanceLow, fPluginTypeHigh, fPluginTypeLow, fScopeHigh, fScopeLow, fConfigRevision, fConfigHashA, fConfigHashB, fConfigHashC, fConfigHashD, fPriority, fGeneration, fActivationEpoch, fState, fConfigFieldCount, fConfigBytes, fSelectionCount, fHasConfigDocument);
            error = EnvelopeError.None;
            return true;
        }
    }

    /// <summary>Test-only codec for <see cref="SelectionRecordValue"/> (schema 4736afccc12b5b625adb5bcdbb714e3f).</summary>
    internal sealed class SelectionRecordCodec : CheckpointTestRecordCodec<SelectionRecordValue>
    {
        private static readonly GeneratedFieldSlot[] Slots =
        {
            new GeneratedFieldSlot(1, WireType.UInt64, true),
            new GeneratedFieldSlot(2, WireType.UInt64, true),
            new GeneratedFieldSlot(3, WireType.UInt64, true),
            new GeneratedFieldSlot(4, WireType.UInt64, true),
            new GeneratedFieldSlot(5, WireType.UInt32, true),
            new GeneratedFieldSlot(6, WireType.UInt64, true),
            new GeneratedFieldSlot(7, WireType.UInt64, true),
            new GeneratedFieldSlot(8, WireType.UInt32, true)
        };

        internal SelectionRecordCodec()
            : base(CheckpointRecordKind.Selection, CheckpointTestSchemas.Selection, Slots)
        {
        }

        protected override void Write(EnvelopeWriter writer, SelectionRecordValue value)
        {
            writer.WriteUInt64Field(1, value.InstanceHigh);
            writer.WriteUInt64Field(2, value.InstanceLow);
            writer.WriteUInt64Field(3, value.ContractHigh);
            writer.WriteUInt64Field(4, value.ContractLow);
            writer.WriteUInt32Field(5, value.ContractVersion);
            writer.WriteUInt64Field(6, value.ProviderHigh);
            writer.WriteUInt64Field(7, value.ProviderLow);
            writer.WriteUInt32Field(8, value.Order);
        }

        protected override bool Read(RecordFields fields, out SelectionRecordValue value, out EnvelopeError error)
        {
            if (!fields.UInt64(0, out ulong fInstanceHigh)
                || !fields.UInt64(1, out ulong fInstanceLow)
                || !fields.UInt64(2, out ulong fContractHigh)
                || !fields.UInt64(3, out ulong fContractLow)
                || !fields.UInt32(4, out uint fContractVersion)
                || !fields.UInt64(5, out ulong fProviderHigh)
                || !fields.UInt64(6, out ulong fProviderLow)
                || !fields.UInt32(7, out uint fOrder))
            {
                value = default(SelectionRecordValue);
                error = fields.Error;
                return false;
            }

            value = new SelectionRecordValue(fInstanceHigh, fInstanceLow, fContractHigh, fContractLow, fContractVersion, fProviderHigh, fProviderLow, fOrder);
            error = EnvelopeError.None;
            return true;
        }
    }

    /// <summary>Test-only codec for <see cref="TargetRecordValue"/> (schema b780328c6fa158f7bd88603db3ebae2f).</summary>
    internal sealed class TargetRecordCodec : CheckpointTestRecordCodec<TargetRecordValue>
    {
        private static readonly GeneratedFieldSlot[] Slots =
        {
            new GeneratedFieldSlot(1, WireType.UInt64, true),
            new GeneratedFieldSlot(2, WireType.UInt64, true),
            new GeneratedFieldSlot(3, WireType.UInt64, true),
            new GeneratedFieldSlot(4, WireType.UInt64, true),
            new GeneratedFieldSlot(5, WireType.UInt64, true),
            new GeneratedFieldSlot(6, WireType.UInt64, true),
            new GeneratedFieldSlot(7, WireType.UInt64, true),
            new GeneratedFieldSlot(8, WireType.UInt64, true),
            new GeneratedFieldSlot(9, WireType.UInt32, true),
            new GeneratedFieldSlot(10, WireType.UInt64, true),
            new GeneratedFieldSlot(11, WireType.UInt32, true),
            new GeneratedFieldSlot(12, WireType.UInt64, true)
        };

        internal TargetRecordCodec()
            : base(CheckpointRecordKind.Target, CheckpointTestSchemas.Target, Slots)
        {
        }

        protected override void Write(EnvelopeWriter writer, TargetRecordValue value)
        {
            writer.WriteUInt64Field(1, value.TargetHigh);
            writer.WriteUInt64Field(2, value.TargetLow);
            writer.WriteUInt64Field(3, value.ScopeHigh);
            writer.WriteUInt64Field(4, value.ScopeLow);
            writer.WriteUInt64Field(5, value.DefinitionHigh);
            writer.WriteUInt64Field(6, value.DefinitionLow);
            writer.WriteUInt64Field(7, value.SchemaHigh);
            writer.WriteUInt64Field(8, value.SchemaLow);
            writer.WriteUInt32Field(9, value.SchemaVersion);
            writer.WriteUInt64Field(10, value.ContentRevision);
            writer.WriteUInt32Field(11, value.SourceSlot);
            writer.WriteUInt64Field(12, value.SourceGeneration);
        }

        protected override bool Read(RecordFields fields, out TargetRecordValue value, out EnvelopeError error)
        {
            if (!fields.UInt64(0, out ulong fTargetHigh)
                || !fields.UInt64(1, out ulong fTargetLow)
                || !fields.UInt64(2, out ulong fScopeHigh)
                || !fields.UInt64(3, out ulong fScopeLow)
                || !fields.UInt64(4, out ulong fDefinitionHigh)
                || !fields.UInt64(5, out ulong fDefinitionLow)
                || !fields.UInt64(6, out ulong fSchemaHigh)
                || !fields.UInt64(7, out ulong fSchemaLow)
                || !fields.UInt32(8, out uint fSchemaVersion)
                || !fields.UInt64(9, out ulong fContentRevision)
                || !fields.UInt32(10, out uint fSourceSlot)
                || !fields.UInt64(11, out ulong fSourceGeneration))
            {
                value = default(TargetRecordValue);
                error = fields.Error;
                return false;
            }

            value = new TargetRecordValue(fTargetHigh, fTargetLow, fScopeHigh, fScopeLow, fDefinitionHigh, fDefinitionLow, fSchemaHigh, fSchemaLow, fSchemaVersion, fContentRevision, fSourceSlot, fSourceGeneration);
            error = EnvelopeError.None;
            return true;
        }
    }

    /// <summary>Test-only codec for <see cref="SlotRecordValue"/> (schema 325ebfc76f751ee2c2b4985df3d088a0).</summary>
    internal sealed class SlotRecordCodec : CheckpointTestRecordCodec<SlotRecordValue>
    {
        private static readonly GeneratedFieldSlot[] Slots =
        {
            new GeneratedFieldSlot(1, WireType.UInt64, true),
            new GeneratedFieldSlot(2, WireType.UInt64, true),
            new GeneratedFieldSlot(3, WireType.UInt64, true),
            new GeneratedFieldSlot(4, WireType.UInt64, true),
            new GeneratedFieldSlot(5, WireType.UInt64, true),
            new GeneratedFieldSlot(6, WireType.UInt64, true),
            new GeneratedFieldSlot(7, WireType.UInt32, true),
            new GeneratedFieldSlot(8, WireType.Int32, true),
            new GeneratedFieldSlot(9, WireType.Bool, true)
        };

        internal SlotRecordCodec()
            : base(CheckpointRecordKind.Slot, CheckpointTestSchemas.Slot, Slots)
        {
        }

        protected override void Write(EnvelopeWriter writer, SlotRecordValue value)
        {
            writer.WriteUInt64Field(1, value.TargetHigh);
            writer.WriteUInt64Field(2, value.TargetLow);
            writer.WriteUInt64Field(3, value.OwnerHigh);
            writer.WriteUInt64Field(4, value.OwnerLow);
            writer.WriteUInt64Field(5, value.SlotHigh);
            writer.WriteUInt64Field(6, value.SlotLow);
            writer.WriteUInt32Field(7, value.SchemaVersion);
            writer.WriteInt32Field(8, value.Value);
            writer.WriteBoolField(9, value.Active);
        }

        protected override bool Read(RecordFields fields, out SlotRecordValue value, out EnvelopeError error)
        {
            if (!fields.UInt64(0, out ulong fTargetHigh)
                || !fields.UInt64(1, out ulong fTargetLow)
                || !fields.UInt64(2, out ulong fOwnerHigh)
                || !fields.UInt64(3, out ulong fOwnerLow)
                || !fields.UInt64(4, out ulong fSlotHigh)
                || !fields.UInt64(5, out ulong fSlotLow)
                || !fields.UInt32(6, out uint fSchemaVersion)
                || !fields.Int32(7, out int fValue)
                || !fields.Bool(8, out bool fActive))
            {
                value = default(SlotRecordValue);
                error = fields.Error;
                return false;
            }

            value = new SlotRecordValue(fTargetHigh, fTargetLow, fOwnerHigh, fOwnerLow, fSlotHigh, fSlotLow, fSchemaVersion, fValue, fActive);
            error = EnvelopeError.None;
            return true;
        }
    }

    /// <summary>Test-only codec for <see cref="GrantRecordValue"/> (schema 54d0f9cccec243b78414ec19245173c6).</summary>
    internal sealed class GrantRecordCodec : CheckpointTestRecordCodec<GrantRecordValue>
    {
        private static readonly GeneratedFieldSlot[] Slots =
        {
            new GeneratedFieldSlot(1, WireType.UInt32, true),
            new GeneratedFieldSlot(2, WireType.UInt64, true),
            new GeneratedFieldSlot(3, WireType.UInt64, true),
            new GeneratedFieldSlot(4, WireType.UInt64, true),
            new GeneratedFieldSlot(5, WireType.UInt64, true),
            new GeneratedFieldSlot(6, WireType.UInt64, true),
            new GeneratedFieldSlot(7, WireType.UInt64, true),
            new GeneratedFieldSlot(8, WireType.UInt32, true),
            new GeneratedFieldSlot(9, WireType.UInt64, true),
            new GeneratedFieldSlot(10, WireType.UInt64, true),
            new GeneratedFieldSlot(11, WireType.UInt64, true),
            new GeneratedFieldSlot(12, WireType.UInt64, true),
            new GeneratedFieldSlot(13, WireType.UInt64, true),
            new GeneratedFieldSlot(14, WireType.UInt64, true),
            new GeneratedFieldSlot(15, WireType.UInt32, true),
            new GeneratedFieldSlot(16, WireType.UInt64, true),
            new GeneratedFieldSlot(17, WireType.UInt64, true),
            new GeneratedFieldSlot(18, WireType.Bool, true),
            new GeneratedFieldSlot(19, WireType.Bool, true),
            new GeneratedFieldSlot(20, WireType.UInt32, true),
            new GeneratedFieldSlot(21, WireType.UInt32, true)
        };

        internal GrantRecordCodec()
            : base(CheckpointRecordKind.Grant, CheckpointTestSchemas.Grant, Slots)
        {
        }

        protected override void Write(EnvelopeWriter writer, GrantRecordValue value)
        {
            writer.WriteUInt32Field(1, value.Kind);
            writer.WriteUInt64Field(2, value.ScopeHigh);
            writer.WriteUInt64Field(3, value.ScopeLow);
            writer.WriteUInt64Field(4, value.TargetHigh);
            writer.WriteUInt64Field(5, value.TargetLow);
            writer.WriteUInt64Field(6, value.CapabilityHigh);
            writer.WriteUInt64Field(7, value.CapabilityLow);
            writer.WriteUInt32Field(8, value.CapabilityVersion);
            writer.WriteUInt64Field(9, value.ProviderHigh);
            writer.WriteUInt64Field(10, value.ProviderLow);
            writer.WriteUInt64Field(11, value.RuleHigh);
            writer.WriteUInt64Field(12, value.RuleLow);
            writer.WriteUInt64Field(13, value.ContractHigh);
            writer.WriteUInt64Field(14, value.ContractLow);
            writer.WriteUInt32Field(15, value.ContractVersion);
            writer.WriteUInt64Field(16, value.SubjectHigh);
            writer.WriteUInt64Field(17, value.SubjectLow);
            writer.WriteBoolField(18, value.AppliesToSubtree);
            writer.WriteBoolField(19, value.AllContracts);
            writer.WriteUInt32Field(20, value.ExclusionKind);
            writer.WriteUInt32Field(21, value.Order);
        }

        protected override bool Read(RecordFields fields, out GrantRecordValue value, out EnvelopeError error)
        {
            if (!fields.UInt32(0, out uint fKind)
                || !fields.UInt64(1, out ulong fScopeHigh)
                || !fields.UInt64(2, out ulong fScopeLow)
                || !fields.UInt64(3, out ulong fTargetHigh)
                || !fields.UInt64(4, out ulong fTargetLow)
                || !fields.UInt64(5, out ulong fCapabilityHigh)
                || !fields.UInt64(6, out ulong fCapabilityLow)
                || !fields.UInt32(7, out uint fCapabilityVersion)
                || !fields.UInt64(8, out ulong fProviderHigh)
                || !fields.UInt64(9, out ulong fProviderLow)
                || !fields.UInt64(10, out ulong fRuleHigh)
                || !fields.UInt64(11, out ulong fRuleLow)
                || !fields.UInt64(12, out ulong fContractHigh)
                || !fields.UInt64(13, out ulong fContractLow)
                || !fields.UInt32(14, out uint fContractVersion)
                || !fields.UInt64(15, out ulong fSubjectHigh)
                || !fields.UInt64(16, out ulong fSubjectLow)
                || !fields.Bool(17, out bool fAppliesToSubtree)
                || !fields.Bool(18, out bool fAllContracts)
                || !fields.UInt32(19, out uint fExclusionKind)
                || !fields.UInt32(20, out uint fOrder))
            {
                value = default(GrantRecordValue);
                error = fields.Error;
                return false;
            }

            value = new GrantRecordValue(fKind, fScopeHigh, fScopeLow, fTargetHigh, fTargetLow, fCapabilityHigh, fCapabilityLow, fCapabilityVersion, fProviderHigh, fProviderLow, fRuleHigh, fRuleLow, fContractHigh, fContractLow, fContractVersion, fSubjectHigh, fSubjectLow, fAppliesToSubtree, fAllContracts, fExclusionKind, fOrder);
            error = EnvelopeError.None;
            return true;
        }
    }

    /// <summary>Test-only codec for <see cref="ClockRecordValue"/> (schema 49f4779fa88d83ea0440e2bab679d545).</summary>
    internal sealed class ClockRecordCodec : CheckpointTestRecordCodec<ClockRecordValue>
    {
        private static readonly GeneratedFieldSlot[] Slots =
        {
            new GeneratedFieldSlot(1, WireType.UInt32, true),
            new GeneratedFieldSlot(2, WireType.UInt64, true),
            new GeneratedFieldSlot(3, WireType.UInt64, true),
            new GeneratedFieldSlot(4, WireType.UInt32, true),
            new GeneratedFieldSlot(5, WireType.UInt32, true),
            new GeneratedFieldSlot(6, WireType.Bool, true),
            new GeneratedFieldSlot(7, WireType.UInt64, true),
            new GeneratedFieldSlot(8, WireType.UInt64, true),
            new GeneratedFieldSlot(9, WireType.UInt64, true),
            new GeneratedFieldSlot(10, WireType.UInt64, true),
            new GeneratedFieldSlot(11, WireType.UInt32, true),
            new GeneratedFieldSlot(12, WireType.UInt64, true),
            new GeneratedFieldSlot(13, WireType.UInt64, true),
            new GeneratedFieldSlot(14, WireType.UInt64, true),
            new GeneratedFieldSlot(15, WireType.UInt32, true),
            new GeneratedFieldSlot(16, WireType.UInt32, true)
        };

        internal ClockRecordCodec()
            : base(CheckpointRecordKind.Clock, CheckpointTestSchemas.Clock, Slots)
        {
        }

        protected override void Write(EnvelopeWriter writer, ClockRecordValue value)
        {
            writer.WriteUInt32Field(1, value.RowKind);
            writer.WriteUInt64Field(2, value.ClockHigh);
            writer.WriteUInt64Field(3, value.ClockLow);
            writer.WriteUInt32Field(4, value.ClockKind);
            writer.WriteUInt32Field(5, value.PausePolicy);
            writer.WriteBoolField(6, value.Persists);
            writer.WriteUInt64Field(7, value.WakeHigh);
            writer.WriteUInt64Field(8, value.WakeLow);
            writer.WriteUInt64Field(9, value.PayloadSchemaHigh);
            writer.WriteUInt64Field(10, value.PayloadSchemaLow);
            writer.WriteUInt32Field(11, value.PayloadSchemaVersion);
            writer.WriteUInt64Field(12, value.ScheduledAtSequence);
            writer.WriteUInt64Field(13, value.RemainingSteps);
            writer.WriteUInt64Field(14, value.RemainingTicks);
            writer.WriteUInt32Field(15, value.WakeState);
            writer.WriteUInt32Field(16, value.Order);
        }

        protected override bool Read(RecordFields fields, out ClockRecordValue value, out EnvelopeError error)
        {
            if (!fields.UInt32(0, out uint fRowKind)
                || !fields.UInt64(1, out ulong fClockHigh)
                || !fields.UInt64(2, out ulong fClockLow)
                || !fields.UInt32(3, out uint fClockKind)
                || !fields.UInt32(4, out uint fPausePolicy)
                || !fields.Bool(5, out bool fPersists)
                || !fields.UInt64(6, out ulong fWakeHigh)
                || !fields.UInt64(7, out ulong fWakeLow)
                || !fields.UInt64(8, out ulong fPayloadSchemaHigh)
                || !fields.UInt64(9, out ulong fPayloadSchemaLow)
                || !fields.UInt32(10, out uint fPayloadSchemaVersion)
                || !fields.UInt64(11, out ulong fScheduledAtSequence)
                || !fields.UInt64(12, out ulong fRemainingSteps)
                || !fields.UInt64(13, out ulong fRemainingTicks)
                || !fields.UInt32(14, out uint fWakeState)
                || !fields.UInt32(15, out uint fOrder))
            {
                value = default(ClockRecordValue);
                error = fields.Error;
                return false;
            }

            value = new ClockRecordValue(fRowKind, fClockHigh, fClockLow, fClockKind, fPausePolicy, fPersists, fWakeHigh, fWakeLow, fPayloadSchemaHigh, fPayloadSchemaLow, fPayloadSchemaVersion, fScheduledAtSequence, fRemainingSteps, fRemainingTicks, fWakeState, fOrder);
            error = EnvelopeError.None;
            return true;
        }
    }

    /// <summary>Test-only codec for <see cref="CommandRecordValue"/> (schema efe5ab3007e3f7b821d96f9264ab31ef).</summary>
    internal sealed class CommandRecordCodec : CheckpointTestRecordCodec<CommandRecordValue>
    {
        private static readonly GeneratedFieldSlot[] Slots =
        {
            new GeneratedFieldSlot(1, WireType.UInt64, true),
            new GeneratedFieldSlot(2, WireType.UInt64, true),
            new GeneratedFieldSlot(3, WireType.UInt64, true),
            new GeneratedFieldSlot(4, WireType.UInt64, true),
            new GeneratedFieldSlot(5, WireType.UInt64, true),
            new GeneratedFieldSlot(6, WireType.UInt64, true),
            new GeneratedFieldSlot(7, WireType.UInt64, true),
            new GeneratedFieldSlot(8, WireType.UInt64, true),
            new GeneratedFieldSlot(9, WireType.UInt64, true),
            new GeneratedFieldSlot(10, WireType.UInt32, true),
            new GeneratedFieldSlot(11, WireType.UInt64, true),
            new GeneratedFieldSlot(12, WireType.UInt64, true),
            new GeneratedFieldSlot(13, WireType.UInt64, true),
            new GeneratedFieldSlot(14, WireType.UInt32, true),
            new GeneratedFieldSlot(15, WireType.UInt32, true),
            new GeneratedFieldSlot(16, WireType.UInt64, true),
            new GeneratedFieldSlot(17, WireType.UInt64, true),
            new GeneratedFieldSlot(18, WireType.UInt64, true),
            new GeneratedFieldSlot(19, WireType.UInt64, true),
            new GeneratedFieldSlot(20, WireType.Bytes, true)
        };

        internal CommandRecordCodec()
            : base(CheckpointRecordKind.Command, CheckpointTestSchemas.Command, Slots)
        {
        }

        protected override void Write(EnvelopeWriter writer, CommandRecordValue value)
        {
            writer.WriteUInt64Field(1, value.IssuerHigh);
            writer.WriteUInt64Field(2, value.IssuerLow);
            writer.WriteUInt64Field(3, value.IssuerSequence);
            writer.WriteUInt64Field(4, value.RouteHigh);
            writer.WriteUInt64Field(5, value.RouteLow);
            writer.WriteUInt64Field(6, value.TargetHigh);
            writer.WriteUInt64Field(7, value.TargetLow);
            writer.WriteUInt64Field(8, value.SchemaHigh);
            writer.WriteUInt64Field(9, value.SchemaLow);
            writer.WriteUInt32Field(10, value.SchemaVersion);
            writer.WriteUInt64Field(11, value.AdmittedStep);
            writer.WriteUInt64Field(12, value.AdmittedEpoch);
            writer.WriteUInt64Field(13, value.AdmissionSequence);
            writer.WriteUInt32Field(14, value.OrderOrdinal);
            writer.WriteUInt32Field(15, value.OriginKind);
            writer.WriteUInt64Field(16, value.InputHashA);
            writer.WriteUInt64Field(17, value.InputHashB);
            writer.WriteUInt64Field(18, value.InputHashC);
            writer.WriteUInt64Field(19, value.InputHashD);
            writer.WriteBytesField(20, value.Payload);
        }

        protected override bool Read(RecordFields fields, out CommandRecordValue value, out EnvelopeError error)
        {
            if (!fields.UInt64(0, out ulong fIssuerHigh)
                || !fields.UInt64(1, out ulong fIssuerLow)
                || !fields.UInt64(2, out ulong fIssuerSequence)
                || !fields.UInt64(3, out ulong fRouteHigh)
                || !fields.UInt64(4, out ulong fRouteLow)
                || !fields.UInt64(5, out ulong fTargetHigh)
                || !fields.UInt64(6, out ulong fTargetLow)
                || !fields.UInt64(7, out ulong fSchemaHigh)
                || !fields.UInt64(8, out ulong fSchemaLow)
                || !fields.UInt32(9, out uint fSchemaVersion)
                || !fields.UInt64(10, out ulong fAdmittedStep)
                || !fields.UInt64(11, out ulong fAdmittedEpoch)
                || !fields.UInt64(12, out ulong fAdmissionSequence)
                || !fields.UInt32(13, out uint fOrderOrdinal)
                || !fields.UInt32(14, out uint fOriginKind)
                || !fields.UInt64(15, out ulong fInputHashA)
                || !fields.UInt64(16, out ulong fInputHashB)
                || !fields.UInt64(17, out ulong fInputHashC)
                || !fields.UInt64(18, out ulong fInputHashD)
                || !fields.Bytes(19, out byte[]? fPayload))
            {
                value = default(CommandRecordValue);
                error = fields.Error;
                return false;
            }

            value = new CommandRecordValue(fIssuerHigh, fIssuerLow, fIssuerSequence, fRouteHigh, fRouteLow, fTargetHigh, fTargetLow, fSchemaHigh, fSchemaLow, fSchemaVersion, fAdmittedStep, fAdmittedEpoch, fAdmissionSequence, fOrderOrdinal, fOriginKind, fInputHashA, fInputHashB, fInputHashC, fInputHashD, fPayload);
            error = EnvelopeError.None;
            return true;
        }
    }

    /// <summary>Test-only codec for <see cref="MessageRecordValue"/> (schema 744d60d006798058b50b656c613a21f3).</summary>
    internal sealed class MessageRecordCodec : CheckpointTestRecordCodec<MessageRecordValue>
    {
        private static readonly GeneratedFieldSlot[] Slots =
        {
            new GeneratedFieldSlot(1, WireType.UInt64, true),
            new GeneratedFieldSlot(2, WireType.UInt64, true),
            new GeneratedFieldSlot(3, WireType.UInt64, true),
            new GeneratedFieldSlot(4, WireType.UInt64, true),
            new GeneratedFieldSlot(5, WireType.UInt64, true),
            new GeneratedFieldSlot(6, WireType.UInt64, true),
            new GeneratedFieldSlot(7, WireType.UInt64, true),
            new GeneratedFieldSlot(8, WireType.UInt64, true),
            new GeneratedFieldSlot(9, WireType.UInt64, true),
            new GeneratedFieldSlot(10, WireType.UInt64, true),
            new GeneratedFieldSlot(11, WireType.UInt64, true),
            new GeneratedFieldSlot(12, WireType.UInt64, true),
            new GeneratedFieldSlot(13, WireType.UInt64, true),
            new GeneratedFieldSlot(14, WireType.UInt32, true),
            new GeneratedFieldSlot(15, WireType.UInt32, true),
            new GeneratedFieldSlot(16, WireType.UInt64, true),
            new GeneratedFieldSlot(17, WireType.UInt32, true),
            new GeneratedFieldSlot(18, WireType.UInt64, true),
            new GeneratedFieldSlot(19, WireType.UInt64, true),
            new GeneratedFieldSlot(20, WireType.UInt64, true),
            new GeneratedFieldSlot(21, WireType.UInt64, true),
            new GeneratedFieldSlot(22, WireType.UInt32, true),
            new GeneratedFieldSlot(23, WireType.UInt64, true),
            new GeneratedFieldSlot(24, WireType.UInt64, true),
            new GeneratedFieldSlot(25, WireType.Bool, true),
            new GeneratedFieldSlot(26, WireType.Bool, true),
            new GeneratedFieldSlot(27, WireType.Bool, true),
            new GeneratedFieldSlot(28, WireType.Bytes, true)
        };

        internal MessageRecordCodec()
            : base(CheckpointRecordKind.Message, CheckpointTestSchemas.Message, Slots)
        {
        }

        protected override void Write(EnvelopeWriter writer, MessageRecordValue value)
        {
            writer.WriteUInt64Field(1, value.Step);
            writer.WriteUInt64Field(2, value.Epoch);
            writer.WriteUInt64Field(3, value.RequestIssuerHigh);
            writer.WriteUInt64Field(4, value.RequestIssuerLow);
            writer.WriteUInt64Field(5, value.RequestSequence);
            writer.WriteUInt64Field(6, value.RouteHigh);
            writer.WriteUInt64Field(7, value.RouteLow);
            writer.WriteUInt64Field(8, value.OwnerHigh);
            writer.WriteUInt64Field(9, value.OwnerLow);
            writer.WriteUInt64Field(10, value.TargetHigh);
            writer.WriteUInt64Field(11, value.TargetLow);
            writer.WriteUInt64Field(12, value.PayloadSchemaHigh);
            writer.WriteUInt64Field(13, value.PayloadSchemaLow);
            writer.WriteUInt32Field(14, value.PayloadSchemaVersion);
            writer.WriteUInt32Field(15, value.MessageKind);
            writer.WriteUInt64Field(16, value.OrderAdmitted);
            writer.WriteUInt32Field(17, value.OrderOrdinal);
            writer.WriteUInt64Field(18, value.OriginKeyHigh);
            writer.WriteUInt64Field(19, value.OriginKeyLow);
            writer.WriteUInt64Field(20, value.ProducerKeyHigh);
            writer.WriteUInt64Field(21, value.ProducerKeyLow);
            writer.WriteUInt32Field(22, value.ProducerKeyVersion);
            writer.WriteUInt64Field(23, value.BufferHigh);
            writer.WriteUInt64Field(24, value.BufferLow);
            writer.WriteBoolField(25, value.HasPayload);
            writer.WriteBoolField(26, value.HasRequest);
            writer.WriteBoolField(27, value.IsOutcome);
            writer.WriteBytesField(28, value.Payload);
        }

        protected override bool Read(RecordFields fields, out MessageRecordValue value, out EnvelopeError error)
        {
            if (!fields.UInt64(0, out ulong fStep)
                || !fields.UInt64(1, out ulong fEpoch)
                || !fields.UInt64(2, out ulong fRequestIssuerHigh)
                || !fields.UInt64(3, out ulong fRequestIssuerLow)
                || !fields.UInt64(4, out ulong fRequestSequence)
                || !fields.UInt64(5, out ulong fRouteHigh)
                || !fields.UInt64(6, out ulong fRouteLow)
                || !fields.UInt64(7, out ulong fOwnerHigh)
                || !fields.UInt64(8, out ulong fOwnerLow)
                || !fields.UInt64(9, out ulong fTargetHigh)
                || !fields.UInt64(10, out ulong fTargetLow)
                || !fields.UInt64(11, out ulong fPayloadSchemaHigh)
                || !fields.UInt64(12, out ulong fPayloadSchemaLow)
                || !fields.UInt32(13, out uint fPayloadSchemaVersion)
                || !fields.UInt32(14, out uint fMessageKind)
                || !fields.UInt64(15, out ulong fOrderAdmitted)
                || !fields.UInt32(16, out uint fOrderOrdinal)
                || !fields.UInt64(17, out ulong fOriginKeyHigh)
                || !fields.UInt64(18, out ulong fOriginKeyLow)
                || !fields.UInt64(19, out ulong fProducerKeyHigh)
                || !fields.UInt64(20, out ulong fProducerKeyLow)
                || !fields.UInt32(21, out uint fProducerKeyVersion)
                || !fields.UInt64(22, out ulong fBufferHigh)
                || !fields.UInt64(23, out ulong fBufferLow)
                || !fields.Bool(24, out bool fHasPayload)
                || !fields.Bool(25, out bool fHasRequest)
                || !fields.Bool(26, out bool fIsOutcome)
                || !fields.Bytes(27, out byte[]? fPayload))
            {
                value = default(MessageRecordValue);
                error = fields.Error;
                return false;
            }

            value = new MessageRecordValue(fStep, fEpoch, fRequestIssuerHigh, fRequestIssuerLow, fRequestSequence, fRouteHigh, fRouteLow, fOwnerHigh, fOwnerLow, fTargetHigh, fTargetLow, fPayloadSchemaHigh, fPayloadSchemaLow, fPayloadSchemaVersion, fMessageKind, fOrderAdmitted, fOrderOrdinal, fOriginKeyHigh, fOriginKeyLow, fProducerKeyHigh, fProducerKeyLow, fProducerKeyVersion, fBufferHigh, fBufferLow, fHasPayload, fHasRequest, fIsOutcome, fPayload);
            error = EnvelopeError.None;
            return true;
        }
    }

    /// <summary>Test-only codec for <see cref="RngRecordValue"/> (schema cd069d4291975928edb3f9e9af5e037d).</summary>
    internal sealed class RngRecordCodec : CheckpointTestRecordCodec<RngRecordValue>
    {
        private static readonly GeneratedFieldSlot[] Slots =
        {
            new GeneratedFieldSlot(1, WireType.UInt64, true),
            new GeneratedFieldSlot(2, WireType.UInt64, true),
            new GeneratedFieldSlot(3, WireType.UInt64, true),
            new GeneratedFieldSlot(4, WireType.UInt64, true),
            new GeneratedFieldSlot(5, WireType.UInt64, true)
        };

        internal RngRecordCodec()
            : base(CheckpointRecordKind.Rng, CheckpointTestSchemas.Rng, Slots)
        {
        }

        protected override void Write(EnvelopeWriter writer, RngRecordValue value)
        {
            writer.WriteUInt64Field(1, value.StreamHigh);
            writer.WriteUInt64Field(2, value.StreamLow);
            writer.WriteUInt64Field(3, value.State);
            writer.WriteUInt64Field(4, value.StreamKey);
            writer.WriteUInt64Field(5, value.DrawCount);
        }

        protected override bool Read(RecordFields fields, out RngRecordValue value, out EnvelopeError error)
        {
            if (!fields.UInt64(0, out ulong fStreamHigh)
                || !fields.UInt64(1, out ulong fStreamLow)
                || !fields.UInt64(2, out ulong fState)
                || !fields.UInt64(3, out ulong fStreamKey)
                || !fields.UInt64(4, out ulong fDrawCount))
            {
                value = default(RngRecordValue);
                error = fields.Error;
                return false;
            }

            value = new RngRecordValue(fStreamHigh, fStreamLow, fState, fStreamKey, fDrawCount);
            error = EnvelopeError.None;
            return true;
        }
    }

    /// <summary>Test-only codec for <see cref="CursorRecordValue"/> (schema 1ddc64b87daa5363e1c1951d44ad5f07).</summary>
    internal sealed class CursorRecordCodec : CheckpointTestRecordCodec<CursorRecordValue>
    {
        private static readonly GeneratedFieldSlot[] Slots =
        {
            new GeneratedFieldSlot(1, WireType.UInt32, true),
            new GeneratedFieldSlot(2, WireType.UInt64, true),
            new GeneratedFieldSlot(3, WireType.UInt64, true),
            new GeneratedFieldSlot(4, WireType.UInt64, true),
            new GeneratedFieldSlot(5, WireType.UInt64, true),
            new GeneratedFieldSlot(6, WireType.UInt64, true)
        };

        internal CursorRecordCodec()
            : base(CheckpointRecordKind.Cursor, CheckpointTestSchemas.Cursor, Slots)
        {
        }

        protected override void Write(EnvelopeWriter writer, CursorRecordValue value)
        {
            writer.WriteUInt32Field(1, value.RowKind);
            writer.WriteUInt64Field(2, value.IssuerHigh);
            writer.WriteUInt64Field(3, value.IssuerLow);
            writer.WriteUInt64Field(4, value.Sequence);
            writer.WriteUInt64Field(5, value.SessionHigh);
            writer.WriteUInt64Field(6, value.SessionLow);
        }

        protected override bool Read(RecordFields fields, out CursorRecordValue value, out EnvelopeError error)
        {
            if (!fields.UInt32(0, out uint fRowKind)
                || !fields.UInt64(1, out ulong fIssuerHigh)
                || !fields.UInt64(2, out ulong fIssuerLow)
                || !fields.UInt64(3, out ulong fSequence)
                || !fields.UInt64(4, out ulong fSessionHigh)
                || !fields.UInt64(5, out ulong fSessionLow))
            {
                value = default(CursorRecordValue);
                error = fields.Error;
                return false;
            }

            value = new CursorRecordValue(fRowKind, fIssuerHigh, fIssuerLow, fSequence, fSessionHigh, fSessionLow);
            error = EnvelopeError.None;
            return true;
        }
    }

    /// <summary>The test-only codec set: one hand-written codec per declared kind of record (P-054).</summary>
    internal static class CheckpointTestCodecs
    {
        /// <summary>A complete set, which a capture of a whole world requires.</summary>
        internal static CheckpointCodecSet Complete() => new CheckpointCodecSet(All());

        /// <summary>A set missing one kind, so an incomplete catalog can be exercised (P-053).</summary>
        internal static CheckpointCodecSet Without(CheckpointRecordKind omitted)
        {
            List<ICheckpointRecordCodec> kept = All();
            for (int i = kept.Count - 1; i >= 0; i--)
            {
                if (kept[i].Kind == omitted)
                {
                    kept.RemoveAt(i);
                }
            }

            return new CheckpointCodecSet(kept);
        }

        private static List<ICheckpointRecordCodec> All() => new List<ICheckpointRecordCodec>
        {
            new HeaderRecordCodec(),
            new ScopeRecordCodec(),
            new InstallRecordCodec(),
            new SelectionRecordCodec(),
            new TargetRecordCodec(),
            new SlotRecordCodec(),
            new GrantRecordCodec(),
            new ClockRecordCodec(),
            new CommandRecordCodec(),
            new MessageRecordCodec(),
            new RngRecordCodec(),
            new CursorRecordCodec(),
        };
    }

    /// <summary>
    /// The capture fixtures: the boundary snapshots, the request shapes over them and a counting boundary reader.
    /// The values are chosen so a bug that confused a stable identity with a source-world slot, or an active slot
    /// with a dormant one, cannot pass unnoticed.
    /// </summary>
    internal static class CheckpointTestFixture
    {
        internal static readonly WorldId World = new WorldId(CheckpointTestIds.WorldSession);
        internal static readonly WorldId ForeignWorld = new WorldId(CheckpointTestIds.ForeignSession);
        internal static readonly WorldId RestoredWorld = new WorldId(CheckpointTestIds.RestoredSession);
        internal static readonly WorldId UnusedWorld = new WorldId(CheckpointTestIds.UnusedSession);

        internal const ulong StepDurationTicks = 1000UL;
        internal const ulong TicksPerSecond = 10000UL;
        internal const uint MaxStepsPerPump = 4U;
        internal const ulong HostTicksPerSecond = 10000000UL;
        internal const ulong LogicalStep = 42UL;
        internal const ulong RetainedDebtTicks = 7UL;
        internal const double DomainSeconds = 1.5d;
        internal const ulong PendingDemand = 3UL;
        internal const ulong PublishedRevision = 5UL;
        internal const ulong PublishedEpoch = 9UL;
        internal const ulong LastEventSequence = 77UL;
        internal const ulong AdmissionCutoff = 88UL;
        internal const uint SourceSlot = 4242U;
        internal const ulong SourceGeneration = 0x5A5A5A5A5A5A5A5AUL;
        internal const int ActiveSlotValue = 11;
        internal const int DormantSlotValue = 22;
        internal const int OtherSlotValue = 33;
        internal const ulong RngState = 0x0123456789ABCDEFUL;
        internal const ulong RngDraws = 3UL;
        internal const ulong EventCursorSequence = 1234UL;
        internal const ulong IssuerHighWaterSequence = 9UL;
        internal const ulong MessageStep = 21UL;

        /// <summary>Builds one boundary snapshot; every argument list defaults to an empty category.</summary>
        internal static CommittedBoundarySnapshot Boundary(
            IReadOnlyList<ScopeRecordValue>? scopes = null,
            IReadOnlyList<InstallRecordValue>? installs = null,
            IReadOnlyList<SelectionRecordValue>? selections = null,
            IReadOnlyList<TargetRecordValue>? targets = null,
            IReadOnlyList<SlotRecordValue>? slots = null,
            IReadOnlyList<GrantRecordValue>? grants = null,
            IReadOnlyList<ClockRecordValue>? clocks = null,
            IReadOnlyList<CommandRecordValue>? commands = null,
            IReadOnlyList<MessageRecordValue>? messages = null,
            IReadOnlyList<RngRecordValue>? rngStreams = null,
            IReadOnlyList<CursorRecordValue>? cursors = null,
            ContentHash? catalogFingerprint = null)
        {
            return new CommittedBoundarySnapshot(
                World,
                new WorldDefinitionId(CheckpointTestIds.WorldDefinition),
                TemporalModel.FixedStep,
                StepDurationTicks,
                TicksPerSecond,
                MaxStepsPerPump,
                false,
                HostTicksPerSecond,
                new LogicalStepId(LogicalStep),
                new TimeDebt(RetainedDebtTicks),
                DomainSeconds,
                PendingDemand,
                PropagationMode.Automatic,
                new CompositionRevision(PublishedRevision),
                new AssemblyEpoch(PublishedEpoch),
                catalogFingerprint ?? CheckpointTestIds.CatalogFingerprint,
                new EventSequence(LastEventSequence),
                new AdmissionSequence(AdmissionCutoff),
                scopes,
                installs,
                selections,
                targets,
                slots,
                grants,
                clocks,
                commands,
                messages,
                rngStreams,
                cursors);
        }

        /// <summary>
        /// The rich boundary: two scopes, one installation, one selection, two targets, active and dormant state, a
        /// grant, a clock declaration and a wake, a pending next-step message, an RNG stream and both cursor kinds.
        /// </summary>
        internal static CommittedBoundarySnapshot Baseline() => Boundary(
            scopes: Scopes(),
            installs: Installs(),
            selections: Selections(),
            targets: Targets(),
            slots: Slots(),
            grants: Grants(),
            clocks: Clocks(),
            messages: Messages(),
            rngStreams: RngStreams(),
            cursors: Cursors());

        /// <summary>
        /// The smallest planable boundary: one root scope, one installation at it, one selection, one target in that
        /// scope, one active and one dormant slot, one RNG stream and one cursor.
        /// </summary>
        internal static CommittedBoundarySnapshot Minimum() => Boundary(
            scopes: SingleRootScope(),
            installs: Installs(),
            selections: Selections(),
            targets: new[] { Targets()[0] },
            slots: new[] { Slots()[0], Slots()[1] },
            rngStreams: RngStreams(),
            cursors: new[] { Cursors()[0] });

        /// <summary>The minimum boundary plus the queued external commands a queue policy has to account for.</summary>
        internal static CommittedBoundarySnapshot WithQueuedCommands(int count) => Boundary(
            scopes: Scopes(),
            installs: Installs(),
            selections: Selections(),
            targets: Targets(),
            slots: Slots(),
            commands: Commands(count),
            rngStreams: RngStreams(),
            cursors: Cursors());

        internal static ScopeRecordValue[] SingleRootScope() => new[] { RootScopeRecord() };

        internal static ScopeRecordValue[] Scopes() => new[] { RootScopeRecord(), ChildScopeRecord() };

        internal static InstallRecordValue[] Installs() => new[]
        {
            new InstallRecordValue(
                CheckpointTestIds.Installation.High, CheckpointTestIds.Installation.Low,
                CheckpointTestIds.PluginType.High, CheckpointTestIds.PluginType.Low,
                CheckpointTestIds.RootScope.High, CheckpointTestIds.RootScope.Low,
                1UL,
                1UL, 2UL, 3UL, 4UL,
                0,
                3UL,
                4UL,
                (uint)InstallationState.Active,
                2U,
                new byte[] { 0x01, 0x02, 0x03 },
                1U,
                true),
        };

        /// <summary>
        /// The selection chooses the one installation the fixture declares as its own provider, because a selection
        /// may only name installations the document declares (P-011).
        /// </summary>
        internal static SelectionRecordValue[] Selections() => new[]
        {
            new SelectionRecordValue(
                CheckpointTestIds.Installation.High, CheckpointTestIds.Installation.Low,
                CheckpointTestIds.Contract.High, CheckpointTestIds.Contract.Low,
                2U,
                CheckpointTestIds.Installation.High, CheckpointTestIds.Installation.Low,
                0U),
        };

        internal static TargetRecordValue[] Targets() => new[]
        {
            new TargetRecordValue(
                CheckpointTestIds.TargetA.High, CheckpointTestIds.TargetA.Low,
                CheckpointTestIds.RootScope.High, CheckpointTestIds.RootScope.Low,
                CheckpointTestIds.RecipeDefinition.High, CheckpointTestIds.RecipeDefinition.Low,
                CheckpointTestIds.RecipeSchema.High, CheckpointTestIds.RecipeSchema.Low,
                1U, 1UL, SourceSlot, SourceGeneration),
            new TargetRecordValue(
                CheckpointTestIds.TargetB.High, CheckpointTestIds.TargetB.Low,
                CheckpointTestIds.ChildScope.High, CheckpointTestIds.ChildScope.Low,
                CheckpointTestIds.RecipeDefinition.High, CheckpointTestIds.RecipeDefinition.Low,
                CheckpointTestIds.RecipeSchema.High, CheckpointTestIds.RecipeSchema.Low,
                1U, 1UL, 99U, 7UL),
        };

        internal static SlotRecordValue[] Slots() => new[]
        {
            Slot(CheckpointTestIds.TargetA, CheckpointTestIds.SlotActive, ActiveSlotValue, true),
            Slot(CheckpointTestIds.TargetA, CheckpointTestIds.SlotDormant, DormantSlotValue, false),
            Slot(CheckpointTestIds.TargetB, CheckpointTestIds.SlotOther, OtherSlotValue, true),
        };

        internal static SlotRecordValue Slot(Id128 target, Id128 slot, int value, bool active) => new SlotRecordValue(
            target.High, target.Low,
            CheckpointTestIds.Owner.High, CheckpointTestIds.Owner.Low,
            slot.High, slot.Low,
            1U, value, active);

        internal static GrantRecordValue[] Grants() => new[]
        {
            new GrantRecordValue(
                (uint)GrantKind.ScopeImport,
                CheckpointTestIds.RootScope.High, CheckpointTestIds.RootScope.Low,
                0UL, 0UL,
                CheckpointTestIds.Capability.High, CheckpointTestIds.Capability.Low,
                1U,
                CheckpointTestIds.Installation.High, CheckpointTestIds.Installation.Low,
                0UL, 0UL,
                0UL, 0UL,
                0U,
                0UL, 0UL,
                false, false,
                0U,
                0U),
        };

        internal static ClockRecordValue[] Clocks() => new[]
        {
            new ClockRecordValue(
                (uint)ClockRowKind.Declaration,
                CheckpointTestIds.Clock.High, CheckpointTestIds.Clock.Low,
                1U, 0U, true,
                0UL, 0UL,
                0UL, 0UL, 0U,
                0UL, 0UL, 0UL,
                (uint)ClockWakeState.Pending,
                0U),
            new ClockRecordValue(
                (uint)ClockRowKind.Wake,
                CheckpointTestIds.Clock.High, CheckpointTestIds.Clock.Low,
                0U, 0U, true,
                CheckpointTestIds.Wake.High, CheckpointTestIds.Wake.Low,
                CheckpointTestIds.RecipeSchema.High, CheckpointTestIds.RecipeSchema.Low,
                1U,
                5UL, 2UL, 3UL,
                (uint)ClockWakeState.Pending,
                0U),
        };

        internal static CommandRecordValue[] Commands(int count)
        {
            var commands = new CommandRecordValue[count];
            for (int i = 0; i < count; i++)
            {
                commands[i] = new CommandRecordValue(
                    CheckpointTestIds.CommandIssuer.High, CheckpointTestIds.CommandIssuer.Low,
                    (ulong)(i + 1),
                    CheckpointTestIds.CommandRoute.High, CheckpointTestIds.CommandRoute.Low,
                    CheckpointTestIds.TargetA.High, CheckpointTestIds.TargetA.Low,
                    CheckpointTestIds.RecipeSchema.High, CheckpointTestIds.RecipeSchema.Low,
                    1U,
                    10UL,
                    2UL,
                    (ulong)(i + 1),
                    (uint)i,
                    0U,
                    1UL, 2UL, 3UL, 4UL,
                    new byte[] { (byte)(0xA0 + i) });
            }

            return commands;
        }

        internal static MessageRecordValue[] Messages() => new[]
        {
            new MessageRecordValue(
                MessageStep,
                2UL,
                0UL, 0UL, 0UL,
                CheckpointTestIds.CommandRoute.High, CheckpointTestIds.CommandRoute.Low,
                CheckpointTestIds.Owner.High, CheckpointTestIds.Owner.Low,
                0UL, 0UL,
                CheckpointTestIds.RecipeSchema.High, CheckpointTestIds.RecipeSchema.Low,
                1U,
                1U,
                20UL,
                0U,
                CheckpointTestIds.MessageBuffer.High, CheckpointTestIds.MessageBuffer.Low,
                CheckpointTestIds.MessageProducer.High, CheckpointTestIds.MessageProducer.Low,
                1U,
                CheckpointTestIds.MessageBuffer.High, CheckpointTestIds.MessageBuffer.Low,
                true, false, false,
                new byte[] { 0x7E, 0x7F }),
        };

        internal static RngRecordValue[] RngStreams() => new[]
        {
            new RngRecordValue(
                CheckpointTestIds.RngStream.High, CheckpointTestIds.RngStream.Low,
                RngState, 7UL, RngDraws),
        };

        internal static CursorRecordValue[] Cursors() => new[]
        {
            new CursorRecordValue(
                (uint)CursorRowKind.EventCursor,
                0UL, 0UL,
                EventCursorSequence,
                CheckpointTestIds.WorldSession.High, CheckpointTestIds.WorldSession.Low),
            new CursorRecordValue(
                (uint)CursorRowKind.IssuerHighWater,
                CheckpointTestIds.CommandIssuer.High, CheckpointTestIds.CommandIssuer.Low,
                IssuerHighWaterSequence,
                CheckpointTestIds.WorldSession.High, CheckpointTestIds.WorldSession.Low),
        };

        internal static CheckpointCaptureRequest Request(
            CheckpointCodecSet? codecs = null,
            CheckpointQueuePolicy queuePolicy = CheckpointQueuePolicy.RejectQueued,
            ContentHash? catalogFingerprint = null,
            uint contentRevisionCount = 0U,
            byte protocolMajor = CheckpointFormat.ProtocolMajor,
            byte protocolMinor = CheckpointFormat.ProtocolMinor,
            WorldId? world = null) =>
            new CheckpointCaptureRequest(
                world ?? World,
                codecs ?? CheckpointTestCodecs.Complete(),
                queuePolicy,
                catalogFingerprint,
                null,
                contentRevisionCount,
                protocolMajor,
                protocolMinor);

        internal static CheckpointTestBoundaryReader Reader(CommittedBoundarySnapshot boundary) =>
            CheckpointTestBoundaryReader.At(World, boundary);

        private static ScopeRecordValue RootScopeRecord() => new ScopeRecordValue(
            CheckpointTestIds.RootScope.High, CheckpointTestIds.RootScope.Low,
            0UL, 0UL,
            0U, (uint)PropagationMode.Automatic,
            1U, 1U,
            false, false,
            0U, 0U);

        private static ScopeRecordValue ChildScopeRecord() => new ScopeRecordValue(
            CheckpointTestIds.ChildScope.High, CheckpointTestIds.ChildScope.Low,
            CheckpointTestIds.RootScope.High, CheckpointTestIds.RootScope.Low,
            1U, (uint)PropagationMode.Conservative,
            0U, 0U,
            true, true,
            2U, 3U);
    }

    /// <summary>
    /// A counting test boundary reader: it records whether a capture ever asked it to read, because "refused before
    /// anything was read" is a claim about calls, not only about the result (P-030, P-052).
    /// </summary>
    internal sealed class CheckpointTestBoundaryReader : ICommittedBoundaryReader
    {
        private readonly CommittedBoundarySnapshot? snapshot;
        private readonly BoundaryRefusal refusal;
        private readonly DiagnosticCode refusalCode;
        private readonly string refusalDetail;
        private readonly bool atBoundary;

        private CheckpointTestBoundaryReader(
            WorldId world,
            CommittedBoundarySnapshot? snapshot,
            bool atBoundary,
            BoundaryRefusal refusal,
            DiagnosticCode refusalCode,
            string refusalDetail)
        {
            World = world;
            this.snapshot = snapshot;
            this.atBoundary = atBoundary;
            this.refusal = refusal;
            this.refusalCode = refusalCode;
            this.refusalDetail = refusalDetail;
        }

        public WorldId World { get; }

        public bool IsAtCommittedBoundary => atBoundary;

        /// <summary>How many times a capture asked this reader for the boundary.</summary>
        internal int TryReadCalls { get; private set; }

        internal static CheckpointTestBoundaryReader At(WorldId world, CommittedBoundarySnapshot boundary) =>
            new CheckpointTestBoundaryReader(world, boundary, true, BoundaryRefusal.None, DiagnosticCode.None, "ok");

        internal static CheckpointTestBoundaryReader NotAtBoundary(WorldId world) =>
            new CheckpointTestBoundaryReader(
                world, null, false, BoundaryRefusal.NotAtBoundary, DiagnosticCode.TooLate, "a step is in progress");

        internal static CheckpointTestBoundaryReader Refusing(
            WorldId world, BoundaryRefusal refusal, DiagnosticCode code, string detail) =>
            new CheckpointTestBoundaryReader(world, null, true, refusal, code, detail);

        public bool TryRead(
            WorldId world,
            out CommittedBoundarySnapshot? read,
            out BoundaryRefusal boundaryRefusal,
            out DiagnosticCode code,
            out string detail)
        {
            TryReadCalls++;
            read = snapshot;
            boundaryRefusal = refusal;
            code = refusalCode;
            detail = refusalDetail;
            if (refusal != BoundaryRefusal.None)
            {
                read = null;
                return false;
            }

            return snapshot != null;
        }
    }

    /// <summary>
    /// Checkpoint capture tests (O-20, P-053): a capture is taken only at a committed boundary of the world the
    /// reader owns, only with a complete codec set, and it accounts for every queued external command rather than
    /// omitting one. Every refusal publishes no bytes at all, and what the header declares is exactly what the
    /// document carries.
    /// </summary>
    [TestFixture]
    public sealed class CheckpointCaptureTests
    {
        [Test]
        public void ACaptureAtACommittedBoundaryProducesAVerifiedDocument()
        {
            CommittedBoundarySnapshot boundary = CheckpointTestFixture.Baseline();
            CheckpointCodecSet codecs = CheckpointTestCodecs.Complete();
            CheckpointTestBoundaryReader reader = CheckpointTestFixture.Reader(boundary);

            CheckpointCaptureResult result = CheckpointCapture.Capture(reader, CheckpointTestFixture.Request(codecs));

            Assert.That(result.Captured, Is.True, result.Detail);
            Assert.That(result.Code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(result.Document, Is.Not.Empty);
            Assert.That(result.DocumentHash, Is.EqualTo(ContentHash.Compute(result.Document)));
            Assert.That(
                result.Boundary,
                Is.EqualTo(new SnapshotToken(
                    CheckpointTestFixture.World,
                    new AssemblyEpoch(CheckpointTestFixture.PublishedEpoch),
                    new LogicalStepId(CheckpointTestFixture.LogicalStep))));
            Assert.That(result.SourceWorld.Session, Is.EqualTo(CheckpointTestFixture.World.Session));
            Assert.That(reader.TryReadCalls, Is.EqualTo(1));
            AssertCountsMatch(result.Counts, boundary);

            // The bytes a capture produced are one the reader accepts, so publication has something complete to write.
            CheckpointDocument document = Read(result.Document, codecs);
            Assert.That(document.Header.IsSupportedProtocol, Is.True);
            AssertCountsMatch(document.Counts, boundary);
        }

        [Test]
        public void AForeignWorldIsRefusedWithStaleHandle()
        {
            CommittedBoundarySnapshot boundary = CheckpointTestFixture.Baseline();
            CheckpointTestBoundaryReader reader = CheckpointTestFixture.Reader(boundary);

            CheckpointCaptureResult result = CheckpointCapture.Capture(
                reader,
                CheckpointTestFixture.Request(world: CheckpointTestFixture.ForeignWorld));

            Assert.That(result.Captured, Is.False);
            Assert.That(result.Code, Is.EqualTo(DiagnosticCode.StaleHandle));
            Assert.That(result.Document, Is.Empty);
            Assert.That(result.DocumentHash.IsEmpty, Is.True);
            Assert.That(result.Detail, Is.Not.Empty);
            Assert.That(reader.TryReadCalls, Is.Zero);
        }

        [Test]
        public void ANonCommittedBoundaryIsRefusedWithTooLateAndNothingIsRead()
        {
            CheckpointTestBoundaryReader reader = CheckpointTestBoundaryReader.NotAtBoundary(CheckpointTestFixture.World);
            CheckpointCaptureRequest request = CheckpointTestFixture.Request();

            CheckpointCaptureResult first = CheckpointCapture.Capture(reader, request);

            Assert.That(first.Captured, Is.False);
            Assert.That(first.Code, Is.EqualTo(DiagnosticCode.TooLate));
            Assert.That(first.Document, Is.Empty);
            Assert.That(first.DocumentHash.IsEmpty, Is.True);
            Assert.That(first.Detail, Is.Not.Empty);
            Assert.That(reader.TryReadCalls, Is.Zero);

            // Repeating the refusal changes nothing: a refused capture never touches the world (P-030, P-052).
            CheckpointCaptureResult second = CheckpointCapture.Capture(reader, request);
            Assert.That(second.Captured, Is.False);
            Assert.That(second.Code, Is.EqualTo(DiagnosticCode.TooLate));
            Assert.That(second.Document, Is.Empty);
            Assert.That(reader.TryReadCalls, Is.Zero);
        }

        [Test]
        public void AnIncompleteCodecSetIsRefusedBeforeAnythingIsRead()
        {
            CommittedBoundarySnapshot boundary = CheckpointTestFixture.Baseline();
            CheckpointCodecSet codecs = CheckpointTestCodecs.Without(CheckpointRecordKind.Cursor);
            CheckpointTestBoundaryReader reader = CheckpointTestFixture.Reader(boundary);

            Assert.That(codecs.IsComplete, Is.False);
            Assert.That(codecs.CompleteKindCount, Is.EqualTo(CheckpointFormat.RecordKindCount - 1));
            Assert.That(codecs.MissingKinds(), Does.Contain(CheckpointRecordKind.Cursor));

            CheckpointCaptureResult result = CheckpointCapture.Capture(
                reader,
                CheckpointTestFixture.Request(codecs));

            Assert.That(result.Captured, Is.False);
            Assert.That(result.Code, Is.EqualTo(DiagnosticCode.MissingDependency));
            Assert.That(result.Document, Is.Empty);
            Assert.That(result.Detail, Is.Not.Empty);
            Assert.That(reader.TryReadCalls, Is.Zero);
        }

        [Test]
        public void AReaderRefusalPropagatesItsCodeAndWritesNoDocument()
        {
            CheckpointTestBoundaryReader reader = CheckpointTestBoundaryReader.Refusing(
                CheckpointTestFixture.World,
                BoundaryRefusal.WorldUnavailable,
                DiagnosticCode.ApplyFault,
                "the world is faulted and cannot be captured (P-031).");

            CheckpointCaptureResult result = CheckpointCapture.Capture(reader, CheckpointTestFixture.Request());

            Assert.That(result.Captured, Is.False);
            Assert.That(result.Code, Is.EqualTo(DiagnosticCode.ApplyFault));
            Assert.That(result.Document, Is.Empty);
            Assert.That(result.Detail, Is.EqualTo("the world is faulted and cannot be captured (P-031)."));
            Assert.That(reader.TryReadCalls, Is.EqualTo(1));
        }

        [Test]
        public void RejectQueuedAccountsForEveryCommandSoNoneIsAmbiguouslyOmitted()
        {
            CommittedBoundarySnapshot boundary = CheckpointTestFixture.WithQueuedCommands(3);
            CheckpointCodecSet codecs = CheckpointTestCodecs.Complete();

            CheckpointCaptureResult result = CheckpointCapture.Capture(
                CheckpointTestFixture.Reader(boundary),
                CheckpointTestFixture.Request(codecs, CheckpointQueuePolicy.RejectQueued));

            Assert.That(result.Captured, Is.True, result.Detail);
            Assert.That(result.Queue.Policy, Is.EqualTo(CheckpointQueuePolicy.RejectQueued));
            Assert.That(result.Queue.Offered, Is.EqualTo(3));
            Assert.That(result.Queue.Included, Is.Zero);
            Assert.That(result.Queue.Rejected, Is.EqualTo(3));
            Assert.That(result.Queue.IsAccountedFor, Is.True);
            Assert.That(result.Queue.Cutoff.Value, Is.EqualTo(boundary.AdmissionCutoff.Value));
            Assert.That(result.Header.Policy, Is.EqualTo(CheckpointQueuePolicy.RejectQueued));
            Assert.That(result.Header.CommandCount, Is.Zero);
            Assert.That(result.Header.RejectedQueuedCount, Is.EqualTo(3U));
            Assert.That(result.Counts.Commands, Is.Zero);

            CheckpointDocument document = Read(result.Document, codecs);
            Assert.That(document.Counts.Commands, Is.Zero);
            Assert.That(document.CountOf(CheckpointRecordKind.Command), Is.Zero);
            Assert.That(ReadRecords<CommandRecordValue>(document, CheckpointRecordKind.Command), Is.Empty);
        }

        [Test]
        public void IncludeQueuedCarriesEveryCommandIntoTheDocument()
        {
            CommittedBoundarySnapshot boundary = CheckpointTestFixture.WithQueuedCommands(3);
            CheckpointCodecSet codecs = CheckpointTestCodecs.Complete();

            CheckpointCaptureResult result = CheckpointCapture.Capture(
                CheckpointTestFixture.Reader(boundary),
                CheckpointTestFixture.Request(codecs, CheckpointQueuePolicy.IncludeQueued));

            Assert.That(result.Captured, Is.True, result.Detail);
            Assert.That(result.Queue.Policy, Is.EqualTo(CheckpointQueuePolicy.IncludeQueued));
            Assert.That(result.Queue.Offered, Is.EqualTo(3));
            Assert.That(result.Queue.Included, Is.EqualTo(3));
            Assert.That(result.Queue.Rejected, Is.Zero);
            Assert.That(result.Queue.IsAccountedFor, Is.True);
            Assert.That(result.Header.Policy, Is.EqualTo(CheckpointQueuePolicy.IncludeQueued));
            Assert.That(result.Header.CommandCount, Is.EqualTo(3U));
            Assert.That(result.Header.RejectedQueuedCount, Is.Zero);
            Assert.That(result.Counts.Commands, Is.EqualTo(3));

            CheckpointDocument document = Read(result.Document, codecs);
            IReadOnlyList<CommandRecordValue> commands =
                ReadRecords<CommandRecordValue>(document, CheckpointRecordKind.Command);
            Assert.That(commands.Count, Is.EqualTo(3));

            for (int i = 0; i < commands.Count; i++)
            {
                CommandRecordValue expected = boundary.QueuedCommands[i];
                Assert.That(commands[i].IssuerId, Is.EqualTo(expected.IssuerId));
                Assert.That(commands[i].IssuerSequence, Is.EqualTo(expected.IssuerSequence));
                Assert.That(commands[i].Route.Value, Is.EqualTo(expected.Route.Value));
                Assert.That(commands[i].Target.Value, Is.EqualTo(expected.Target.Value));
                Assert.That(commands[i].AdmissionSequence, Is.EqualTo(expected.AdmissionSequence));
                Assert.That(commands[i].InputHash, Is.EqualTo(expected.InputHash));
                Assert.That(commands[i].Payload, Is.Not.Null);
                Assert.That(expected.Payload, Is.Not.Null);
                Assert.That(commands[i].Payload, Is.EqualTo(expected.Payload));
            }
        }

        [Test]
        public void TheHeaderEchoesTheCommittedBoundaryAndItsCatalogFingerprint()
        {
            CommittedBoundarySnapshot boundary = CheckpointTestFixture.Baseline();
            ContentHash fingerprint = ContentHash.Compute(new byte[] { 9, 8, 7, 6, 5 });
            Assert.That(fingerprint, Is.Not.EqualTo(boundary.CatalogFingerprint));

            CheckpointCaptureResult result = CheckpointCapture.Capture(
                CheckpointTestFixture.Reader(boundary),
                CheckpointTestFixture.Request(CheckpointTestCodecs.Complete(), catalogFingerprint: fingerprint));

            Assert.That(result.Captured, Is.True, result.Detail);
            HeaderRecordValue header = result.Header;
            Assert.That(header.LogicalStep, Is.EqualTo(boundary.LogicalStep.Value));
            Assert.That(header.TimeDebtTicks, Is.EqualTo(boundary.RetainedDebt.Ticks));
            Assert.That(header.DomainSeconds, Is.EqualTo(boundary.DomainSeconds));
            Assert.That(header.PendingDemand, Is.EqualTo(boundary.PendingDemand));
            Assert.That(header.Temporal, Is.EqualTo(boundary.Model));
            Assert.That(header.Propagation, Is.EqualTo(boundary.Mode));
            Assert.That(header.SourcePublishedRevision, Is.EqualTo(boundary.PublishedRevision.Value));
            Assert.That(header.SourcePublishedEpoch, Is.EqualTo(boundary.PublishedEpoch.Value));
            Assert.That(header.LastEventSequence, Is.EqualTo(boundary.LastEventSequence.Value));
            Assert.That(header.AdmissionCutoff, Is.EqualTo(boundary.AdmissionCutoff.Value));

            // The four words are the collation of the fingerprint the request supplied, not the boundary's own
            // (P-028, P-053).
            Assert.That(header.CatalogFingerprint, Is.EqualTo(fingerprint));
            var words = new byte[ContentHash.SizeInBytes];
            Id128Codec.WriteBigEndian(new Id128(header.CatalogFingerprintA, header.CatalogFingerprintB), words, 0);
            Id128Codec.WriteBigEndian(new Id128(header.CatalogFingerprintC, header.CatalogFingerprintD), words, 16);
            Assert.That(new ContentHash(words), Is.EqualTo(fingerprint));
        }

        [Test]
        public void TheHeaderCarriesTheContentRevisionCountAndBothProtocolBytes()
        {
            CommittedBoundarySnapshot boundary = CheckpointTestFixture.Baseline();
            CheckpointCodecSet codecs = CheckpointTestCodecs.Complete();
            CheckpointCaptureRequest request = CheckpointTestFixture.Request(codecs, contentRevisionCount: 7U);

            CheckpointCaptureResult result = CheckpointCapture.Capture(CheckpointTestFixture.Reader(boundary), request);

            Assert.That(result.Captured, Is.True, result.Detail);
            Assert.That(result.Header.ContentRevisionCount, Is.EqualTo(7U));
            Assert.That(result.Header.ProtocolMajor, Is.EqualTo((uint)request.ProtocolMajor));
            Assert.That(result.Header.ProtocolMinor, Is.EqualTo((uint)request.ProtocolMinor));
            Assert.That(result.Header.IsSupportedProtocol, Is.True);

            CheckpointDocument document = Read(result.Document, codecs);
            Assert.That(document.Header.ContentRevisionCount, Is.EqualTo(7U));
            Assert.That(document.Header.ProtocolMajor, Is.EqualTo((uint)request.ProtocolMajor));
            Assert.That(document.Header.ProtocolMinor, Is.EqualTo((uint)request.ProtocolMinor));
        }

        [Test]
        public void ActiveAndDormantStateRoundTripsWithoutHandles()
        {
            CommittedBoundarySnapshot boundary = CheckpointTestFixture.Baseline();
            CheckpointCodecSet codecs = CheckpointTestCodecs.Complete();

            CheckpointCaptureResult result = CheckpointCapture.Capture(
                CheckpointTestFixture.Reader(boundary),
                CheckpointTestFixture.Request(codecs));
            Assert.That(result.Captured, Is.True, result.Detail);

            CheckpointDocument document = Read(result.Document, codecs);
            AssertCountsMatch(document.Counts, boundary);
            Assert.That(
                document.Header.CountsMatch(
                    document.Counts.Scopes,
                    document.Counts.Installs,
                    document.Counts.Selections,
                    document.Counts.Targets,
                    document.Counts.Slots,
                    document.Counts.Grants,
                    document.Counts.Clocks,
                    document.Counts.Commands,
                    document.Counts.Messages,
                    document.Counts.RngStreams,
                    document.Counts.Cursors),
                Is.True);

            // Dormant state is authoritative state: it survives the round trip as Active == false (P-032, P-053).
            IReadOnlyList<SlotRecordValue> slots = ReadRecords<SlotRecordValue>(document, CheckpointRecordKind.Slot);
            Assert.That(slots.Count, Is.EqualTo(3));
            Assert.That(slots[0].Active, Is.True);
            Assert.That(slots[0].Value, Is.EqualTo(CheckpointTestFixture.ActiveSlotValue));
            Assert.That(slots[1].Active, Is.False);
            Assert.That(slots[1].Value, Is.EqualTo(CheckpointTestFixture.DormantSlotValue));
            Assert.That(slots[2].Active, Is.True);
            Assert.That(slots[2].Value, Is.EqualTo(CheckpointTestFixture.OtherSlotValue));

            // The stable identities the document carries are exactly the ones the boundary declared, in capture
            // order: a restore rebuilds from these and from nothing else (P-004).
            IReadOnlyList<ScopeRecordValue> scopes = ReadRecords<ScopeRecordValue>(document, CheckpointRecordKind.Scope);
            Assert.That(
                Identities(scopes, scope => new Id128(scope.ScopeHigh, scope.ScopeLow)),
                Is.EqualTo(new List<Id128> { CheckpointTestIds.RootScope, CheckpointTestIds.ChildScope }));

            IReadOnlyList<InstallRecordValue> installs =
                ReadRecords<InstallRecordValue>(document, CheckpointRecordKind.Install);
            Assert.That(
                Identities(installs, install => new Id128(install.InstanceHigh, install.InstanceLow)),
                Is.EqualTo(new List<Id128> { CheckpointTestIds.Installation }));

            IReadOnlyList<SelectionRecordValue> selections =
                ReadRecords<SelectionRecordValue>(document, CheckpointRecordKind.Selection);
            Assert.That(selections.Count, Is.EqualTo(1));
            Assert.That(selections[0].Instance.Value, Is.EqualTo(CheckpointTestIds.Installation));
            Assert.That(selections[0].Provider.Value, Is.EqualTo(CheckpointTestIds.Installation));

            IReadOnlyList<GrantRecordValue> grants = ReadRecords<GrantRecordValue>(document, CheckpointRecordKind.Grant);
            Assert.That(grants.Count, Is.EqualTo(1));
            Assert.That(grants[0].Grant, Is.EqualTo(GrantKind.ScopeImport));
            Assert.That(grants[0].Scope.Value, Is.EqualTo(CheckpointTestIds.RootScope));

            IReadOnlyList<ClockRecordValue> clocks = ReadRecords<ClockRecordValue>(document, CheckpointRecordKind.Clock);
            Assert.That(clocks.Count, Is.EqualTo(2));
            Assert.That(clocks[0].Row, Is.EqualTo(ClockRowKind.Declaration));
            Assert.That(clocks[0].ClockId, Is.EqualTo(CheckpointTestIds.Clock));
            Assert.That(clocks[1].Row, Is.EqualTo(ClockRowKind.Wake));
            Assert.That(clocks[1].WakeId, Is.EqualTo(CheckpointTestIds.Wake));
            Assert.That(clocks[1].RemainingSteps, Is.EqualTo(2UL));

            IReadOnlyList<RngRecordValue> streams = ReadRecords<RngRecordValue>(document, CheckpointRecordKind.Rng);
            Assert.That(streams.Count, Is.EqualTo(1));
            Assert.That(streams[0].StreamId, Is.EqualTo(CheckpointTestIds.RngStream));
            Assert.That(streams[0].State, Is.EqualTo(CheckpointTestFixture.RngState));
            Assert.That(streams[0].DrawCount, Is.EqualTo(CheckpointTestFixture.RngDraws));

            IReadOnlyList<CursorRecordValue> cursors = ReadRecords<CursorRecordValue>(document, CheckpointRecordKind.Cursor);
            Assert.That(cursors.Count, Is.EqualTo(2));
            Assert.That(cursors[0].Row, Is.EqualTo(CursorRowKind.EventCursor));
            Assert.That(cursors[0].SourceSession.Session, Is.EqualTo(CheckpointTestFixture.World.Session));
            Assert.That(cursors[0].Event.Value, Is.EqualTo(CheckpointTestFixture.EventCursorSequence));
            Assert.That(cursors[1].Row, Is.EqualTo(CursorRowKind.IssuerHighWater));
            Assert.That(cursors[1].IssuerId, Is.EqualTo(CheckpointTestIds.CommandIssuer));
            Assert.That(cursors[1].Admission.Value, Is.EqualTo(CheckpointTestFixture.IssuerHighWaterSequence));

            IReadOnlyList<MessageRecordValue> messages =
                ReadRecords<MessageRecordValue>(document, CheckpointRecordKind.Message);
            Assert.That(messages.Count, Is.EqualTo(1));
            Assert.That(messages[0].Step, Is.EqualTo(CheckpointTestFixture.MessageStep));
            Assert.That(messages[0].Buffer.Value, Is.EqualTo(CheckpointTestIds.MessageBuffer));
            Assert.That(messages[0].Producer.RegistrationKey, Is.EqualTo(CheckpointTestIds.MessageProducer));
            Assert.That(messages[0].HasRequest, Is.False);
            Assert.That(messages[0].Payload, Is.Not.Null);
            Assert.That(messages[0].Payload, Is.EqualTo(new byte[] { 0x7E, 0x7F }));

            // P-053's probe: the target records carry the source world's registry slot and generation as *diagnostic*
            // evidence, and the restored identity is the stable TargetId. A restore that reused either handle would
            // decode a target whose identity is the slot number rather than the id the boundary declared (P-005).
            IReadOnlyList<TargetRecordValue> targets = ReadRecords<TargetRecordValue>(document, CheckpointRecordKind.Target);
            Assert.That(targets.Count, Is.EqualTo(2));
            Assert.That(targets[0].Target.Value, Is.EqualTo(CheckpointTestIds.TargetA));
            Assert.That(targets[0].SourceSlot, Is.EqualTo(CheckpointTestFixture.SourceSlot));
            Assert.That(targets[0].SourceGeneration, Is.EqualTo(CheckpointTestFixture.SourceGeneration));
            Assert.That(targets[0].Target.Value.Low, Is.Not.EqualTo((ulong)targets[0].SourceSlot));
            Assert.That(targets[0].Target.Value.High, Is.Not.EqualTo((ulong)targets[0].SourceSlot));
            Assert.That(targets[0].Target.Value.Low, Is.Not.EqualTo(targets[0].SourceGeneration));
            Assert.That(targets[0].Target.Value.High, Is.Not.EqualTo(targets[0].SourceGeneration));

            Assert.That(
                Identities(targets, target => new Id128(target.TargetHigh, target.TargetLow)),
                Is.EqualTo(new List<Id128> { CheckpointTestIds.TargetA, CheckpointTestIds.TargetB }));
        }

        [Test]
        public void AProtocolMajorTwoRequestIsRefusedInsteadOfWritingAnUnsupportedDocument()
        {
            CommittedBoundarySnapshot boundary = CheckpointTestFixture.Baseline();
            CheckpointCodecSet codecs = CheckpointTestCodecs.Complete();

            CheckpointCaptureResult result = CheckpointCapture.Capture(
                CheckpointTestFixture.Reader(boundary),
                CheckpointTestFixture.Request(codecs, protocolMajor: 2));

            Assert.That(result.Captured, Is.False);
            Assert.That(result.Code, Is.EqualTo(DiagnosticCode.UnsupportedVersion));
            Assert.That(result.Document, Is.Empty);
            Assert.That(result.DocumentHash.IsEmpty, Is.True);

            // A refused capture publishes no header, so the header a protocol-2 capture would have written is built
            // directly: it declares an unsupported protocol and the serializer refuses it rather than writing a
            // document a reader must reject (P-055).
            HeaderRecordValue unsupported = UnsupportedProtocolHeader();
            Assert.That(unsupported.IsSupportedProtocol, Is.False);
            Assert.That(unsupported.ProtocolMajor, Is.EqualTo(2U));

            var serializer = new CheckpointSerializer(codecs);
            Assert.That(
                serializer.TrySerialize(
                    unsupported,
                    out byte[] document,
                    out DiagnosticCode code,
                    out string detail),
                Is.False);
            Assert.That(code, Is.EqualTo(DiagnosticCode.UnsupportedVersion));
            Assert.That(document, Is.Empty);
            Assert.That(detail, Is.Not.Empty);
        }

        private static CheckpointDocument Read(byte[] document, CheckpointCodecSet codecs)
        {
            Assert.That(
                CheckpointDocument.TryRead(document, codecs, out CheckpointDocument? read, out DiagnosticCode code, out string detail),
                Is.True,
                detail);
            Assert.That(code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(read, Is.Not.Null);
            return read!;
        }

        private static IReadOnlyList<TValue> ReadRecords<TValue>(CheckpointDocument document, CheckpointRecordKind kind)
            where TValue : struct
        {
            Assert.That(
                document.TryReadRecords(kind, out IReadOnlyList<TValue> values, out DiagnosticCode code, out string detail),
                Is.True,
                detail);
            Assert.That(code, Is.EqualTo(DiagnosticCode.None));
            return values;
        }

        private static List<Id128> Identities<TValue>(IReadOnlyList<TValue> values, Func<TValue, Id128> identity)
        {
            var identities = new List<Id128>(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                identities.Add(identity(values[i]));
            }

            return identities;
        }

        private static void AssertCountsMatch(CheckpointCounts counts, CommittedBoundarySnapshot boundary)
        {
            Assert.That(counts.Scopes, Is.EqualTo(boundary.Scopes.Count));
            Assert.That(counts.Installs, Is.EqualTo(boundary.Installs.Count));
            Assert.That(counts.Selections, Is.EqualTo(boundary.Selections.Count));
            Assert.That(counts.Targets, Is.EqualTo(boundary.Targets.Count));
            Assert.That(counts.Slots, Is.EqualTo(boundary.Slots.Count));
            Assert.That(counts.Grants, Is.EqualTo(boundary.Grants.Count));
            Assert.That(counts.Clocks, Is.EqualTo(boundary.Clocks.Count));
            Assert.That(counts.Commands, Is.EqualTo(boundary.QueuedCommands.Count));
            Assert.That(counts.Messages, Is.EqualTo(boundary.NextStepMessages.Count));
            Assert.That(counts.RngStreams, Is.EqualTo(boundary.RngStreams.Count));
            Assert.That(counts.Cursors, Is.EqualTo(boundary.Cursors.Count));
        }

        /// <summary>
        /// The header record a future protocol would write: protocol 2.0 and no body records. It is built here rather
        /// than captured because a capture refuses to write it (P-055).
        /// </summary>
        private static HeaderRecordValue UnsupportedProtocolHeader() => new HeaderRecordValue(
            CheckpointTestIds.WorldDefinition.High,
            CheckpointTestIds.WorldDefinition.Low,
            CheckpointTestIds.WorldSession.High,
            CheckpointTestIds.WorldSession.Low,
            2U,
            0U,
            (uint)TemporalModel.FixedStep,
            CheckpointTestFixture.StepDurationTicks,
            CheckpointTestFixture.TicksPerSecond,
            CheckpointTestFixture.MaxStepsPerPump,
            false,
            CheckpointTestFixture.LogicalStep,
            CheckpointTestFixture.RetainedDebtTicks,
            CheckpointTestFixture.DomainSeconds,
            CheckpointTestFixture.PendingDemand,
            (uint)PropagationMode.Automatic,
            0UL,
            0UL,
            0UL,
            0UL,
            (uint)CheckpointQueuePolicy.RejectQueued,
            CheckpointTestFixture.AdmissionCutoff,
            0U,
            CheckpointTestFixture.LastEventSequence,
            0U,
            0U,
            0U,
            0U,
            0U,
            0U,
            0U,
            0U,
            0U,
            0U,
            0U,
            CheckpointTestFixture.PublishedRevision,
            CheckpointTestFixture.PublishedEpoch,
            CheckpointTestFixture.HostTicksPerSecond,
            0U);
    }
}

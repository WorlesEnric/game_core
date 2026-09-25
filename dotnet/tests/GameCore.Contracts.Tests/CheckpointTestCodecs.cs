// Checkpoint test-only codec set (GC-018).
//
// The production checkpoint codec set is *generated*: it is the GC-003 content compiler's output for the
// checkpoint catalog, reached through ICheckpointCodecSet. A test project cannot compile generated source, so
// this file hand-writes one codec per record kind directly against EnvelopeWriter/EnvelopeReader, in the shape the
// generated serializer base uses: a declared field table walked by GeneratedEnvelopeReader, then a decode that
// seeks to each field the walk recorded. A codec set does not have to be complete for most tests: TrySerialize
// needs the header codec plus the codecs of the kinds a capture adds records for, and IsComplete/MissingKinds have
// their own assertions in CheckpointDocumentTests.
//
// The record field ids below are this test codec's own declared layout, ascending from 1 within each kind and in
// the declaration order of the record struct's constructor. Only the *container* field ids (1 for the header,
// FirstRecordFieldId + kind ordinal for a body record) are fixed by CheckpointFormat.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using NUnit.Framework;

namespace GameCore.Contracts.Tests
{
    /// <summary>
    /// Read side of one hand-written record document: after the declared-field walk has validated the document,
    /// each field is decoded by seeking to the offset the walk recorded, exactly as a generated deserializer does.
    /// A field this reader was not told to declare is a test-codec defect, so it reports rather than defaulting.
    /// </summary>
    internal sealed class TestRecordReader
    {
        private readonly EnvelopeReader reader;
        private readonly GeneratedFieldBuffer buffer;

        private TestRecordReader(EnvelopeReader reader, GeneratedFieldBuffer buffer)
        {
            this.reader = reader;
            this.buffer = buffer;
        }

        internal static bool TryOpen(
            byte[] document,
            SchemaRef schema,
            IReadOnlyList<GeneratedFieldSlot> slots,
            out TestRecordReader? fields,
            out EnvelopeError error)
        {
            var buffer = new GeneratedFieldBuffer();
            if (!GeneratedEnvelopeReader.TryReadDeclaredFields(
                    document, schema, CheckpointFormat.KnownFeatureIds, slots, buffer, out error))
            {
                fields = null;
                return false;
            }

            fields = new TestRecordReader(new EnvelopeReader(document), buffer);
            error = EnvelopeError.None;
            return true;
        }

        internal ulong UInt64(int fieldId)
        {
            EnvelopeField field = Seek(fieldId);
            if (!reader.TryReadUInt64(field, out ulong value))
            {
                throw new InvalidOperationException(Describe(fieldId));
            }

            return value;
        }

        internal uint UInt32(int fieldId)
        {
            EnvelopeField field = Seek(fieldId);
            if (!reader.TryReadUInt32(field, out uint value))
            {
                throw new InvalidOperationException(Describe(fieldId));
            }

            return value;
        }

        internal int Int32(int fieldId)
        {
            EnvelopeField field = Seek(fieldId);
            if (!reader.TryReadInt32(field, out int value))
            {
                throw new InvalidOperationException(Describe(fieldId));
            }

            return value;
        }

        internal bool Bool(int fieldId)
        {
            EnvelopeField field = Seek(fieldId);
            if (!reader.TryReadBool(field, out bool value))
            {
                throw new InvalidOperationException(Describe(fieldId));
            }

            return value;
        }

        internal double Float64(int fieldId)
        {
            EnvelopeField field = Seek(fieldId);
            if (!reader.TryReadFloat64(field, out double value, out ulong _))
            {
                throw new InvalidOperationException(Describe(fieldId));
            }

            return value;
        }

        /// <summary>Null for an explicit null marker, which is how an absent byte array is written.</summary>
        internal byte[]? Bytes(int fieldId)
        {
            EnvelopeField field = Seek(fieldId);
            if (!reader.TryReadBytes(field, out byte[]? value))
            {
                throw new InvalidOperationException(Describe(fieldId));
            }

            return value;
        }

        internal ulong[] UInt64Range(int firstFieldId, int count)
        {
            var values = new ulong[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = UInt64(firstFieldId + i);
            }

            return values;
        }

        internal uint[] UInt32Range(int firstFieldId, int count)
        {
            var values = new uint[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = UInt32(firstFieldId + i);
            }

            return values;
        }

        private EnvelopeField Seek(int fieldId)
        {
            for (int i = 0; i < buffer.Count; i++)
            {
                if (buffer.Field(i).FieldId != fieldId)
                {
                    continue;
                }

                if (!reader.TrySeekTo(buffer.RecordOffset(i)) || !reader.TryReadField(out EnvelopeField field))
                {
                    break;
                }

                return field;
            }

            throw new InvalidOperationException(Describe(fieldId));
        }

        private static string Describe(int fieldId) =>
            "The test codec's document carries no decodable field "
            + fieldId.ToString(CultureInfo.InvariantCulture) + ".";
    }

    /// <summary>One hand-written record codec: declared slots, an encoder and a decoder over the test reader.</summary>
    internal sealed class TestCheckpointCodec<TValue> : ICheckpointRecordCodec<TValue>
        where TValue : struct
    {
        private readonly GeneratedFieldSlot[] slots;
        private readonly Func<TValue, byte[]> encoder;
        private readonly Func<TestRecordReader, TValue> decoder;

        internal TestCheckpointCodec(
            CheckpointRecordKind kind,
            SchemaRef schema,
            GeneratedFieldSlot[] slots,
            Func<TValue, byte[]> encoder,
            Func<TestRecordReader, TValue> decoder)
        {
            Kind = kind;
            Schema = schema;
            this.slots = slots;
            this.encoder = encoder;
            this.decoder = decoder;
        }

        public CheckpointRecordKind Kind { get; }

        public SchemaRef Schema { get; }

        /// <summary>Validates a record document through the same declared-field walk a generated codec uses.</summary>
        public bool TryValidate(byte[] document, out EnvelopeError error) =>
            GeneratedEnvelopeReader.TryReadDeclaredFields(
                document, Schema, CheckpointFormat.KnownFeatureIds, slots, new GeneratedFieldBuffer(), out error);

        public byte[] Encode(TValue value) => encoder(value);

        public bool TryDecode(byte[] document, out TValue value, out EnvelopeError error)
        {
            value = default(TValue);
            if (!TestRecordReader.TryOpen(document, Schema, slots, out TestRecordReader? fields, out error)
                || fields == null)
            {
                return false;
            }

            value = decoder(fields);
            return true;
        }
    }

    /// <summary>
    /// The test codec set: one codec per record kind, each declaring the schema derived from the kind's stable
    /// name (the same derivation the generated catalog uses) and a field layout matching its record struct. The
    /// sample builders give every field a distinct non-zero value, so a swapped word or a wrong field id fails an
    /// assertion instead of cancelling out.
    /// </summary>
    internal static class CheckpointTestRecords
    {
        internal static Id128 Id(ulong ordinal) =>
            new Id128(0x4A00000000000000UL + ordinal, 0x5B00000000000000UL + ordinal);

        internal static ScopeId ScopeIdOf(ulong ordinal) => new ScopeId(Id(ordinal));

        internal static TargetId TargetIdOf(ulong ordinal) => new TargetId(Id(ordinal));

        internal static OwnerId OwnerIdOf(ulong ordinal) => new OwnerId(Id(ordinal));

        internal static SlotId SlotIdOf(ulong ordinal) => new SlotId(Id(ordinal));

        internal static PluginInstanceId InstallIdOf(ulong ordinal) => new PluginInstanceId(Id(ordinal));

        internal static PluginTypeId PluginTypeIdOf(ulong ordinal) => new PluginTypeId(Id(ordinal));

        internal static BufferId BufferIdOf(ulong ordinal) => new BufferId(Id(ordinal));

        internal static RouteId RouteIdOf(ulong ordinal) => new RouteId(Id(ordinal));

        internal static CapabilityId CapabilityIdOf(ulong ordinal) => new CapabilityId(Id(ordinal));

        internal static ProviderInstallationId ProviderIdOf(ulong ordinal) =>
            new ProviderInstallationId(Id(ordinal));

        internal static RuleId RuleIdOf(ulong ordinal) => new RuleId(Id(ordinal));

        internal static DefinitionId DefinitionIdOf(ulong ordinal) => new DefinitionId(Id(ordinal));

        internal static SchemaId SchemaIdOf(ulong ordinal) => new SchemaId(Id(ordinal));

        internal static WorldId WorldOf(ulong ordinal) => new WorldId(Id(ordinal));

        /// <summary>Schema of one record kind, derived from its documented stable name.</summary>
        internal static SchemaRef SchemaOf(CheckpointRecordKind kind) =>
            new SchemaRef(
                new SchemaId(StableNameKeyDerivation.Derive(CheckpointFormat.SchemaStableNameOf(kind))),
                1U);

        /// <summary>
        /// A header with distinct, non-zero values in every field. The eleven counts are the arguments, in the
        /// declaration order of <see cref="HeaderRecordValue"/>'s constructor: scope, install, selection, target,
        /// slot, grant, clock, command, message, rng stream, cursor.
        /// </summary>
        internal static HeaderRecordValue Header(
            int scopes,
            int installs,
            int selections,
            int targets,
            int slots,
            int grants,
            int clocks,
            int commands,
            int messages,
            int rngStreams,
            int cursors)
        {
            return new HeaderRecordValue(
                0x1111111111111111UL,
                0x2222222222222222UL,
                0x3333333333333333UL,
                0x4444444444444444UL,
                1U,
                0U,
                (uint)TemporalModel.CommandDriven,
                0x5555555555555555UL,
                0x6666666666666666UL,
                7U,
                true,
                42UL,
                3UL,
                12.5d,
                9UL,
                (uint)PropagationMode.Conservative,
                0x7777777777777777UL,
                0x8888888888888888UL,
                0x9999999999999999UL,
                0xAAAAAAAAAAAAAAAAUL,
                (uint)CheckpointQueuePolicy.IncludeQueued,
                5UL,
                2U,
                4UL,
                (uint)scopes,
                (uint)installs,
                (uint)selections,
                (uint)targets,
                (uint)slots,
                (uint)grants,
                (uint)clocks,
                (uint)commands,
                (uint)messages,
                (uint)rngStreams,
                (uint)cursors,
                6UL,
                8UL,
                60UL,
                3U);
        }

        internal static HeaderRecordValue EmptyHeader() => Header(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        internal static ScopeRecordValue SampleScope(ulong ordinal, ulong parentOrdinal, uint depth)
        {
            Id128 scope = Id(ordinal);
            Id128 parent = parentOrdinal == 0UL ? Id128.Zero : Id(parentOrdinal);
            return new ScopeRecordValue(
                scope.High,
                scope.Low,
                parent.High,
                parent.Low,
                depth,
                (uint)PropagationMode.Automatic,
                7U,
                8U,
                true,
                false,
                9U,
                10U);
        }

        internal static ScopeRecordValue SampleRootScope(ulong ordinal) => SampleScope(ordinal, 0UL, 0U);

        internal static InstallRecordValue SampleInstall(ulong ordinal, ulong scopeOrdinal)
        {
            Id128 instance = Id(ordinal);
            Id128 pluginType = Id(ordinal + 800UL);
            Id128 scope = Id(scopeOrdinal);
            return new InstallRecordValue(
                instance.High,
                instance.Low,
                pluginType.High,
                pluginType.Low,
                scope.High,
                scope.Low,
                ordinal + 1UL,
                ordinal + 2UL,
                ordinal + 3UL,
                ordinal + 4UL,
                ordinal + 5UL,
                -7,
                ordinal + 6UL,
                ordinal + 7UL,
                (uint)InstallationState.Active,
                8U,
                new byte[] { 4, 5, 6 },
                9U,
                true);
        }

        internal static SelectionRecordValue SampleSelection(ulong instanceOrdinal, ulong providerOrdinal)
        {
            Id128 instance = Id(instanceOrdinal);
            Id128 contract = Id(instanceOrdinal + 900UL);
            Id128 provider = Id(providerOrdinal);
            return new SelectionRecordValue(
                instance.High,
                instance.Low,
                contract.High,
                contract.Low,
                2U,
                provider.High,
                provider.Low,
                3U);
        }

        internal static TargetRecordValue SampleTarget(ulong ordinal, ulong scopeOrdinal)
        {
            Id128 target = Id(ordinal);
            Id128 scope = Id(scopeOrdinal);
            Id128 definition = Id(ordinal + 1000UL);
            Id128 schema = Id(ordinal + 1001UL);
            return new TargetRecordValue(
                target.High,
                target.Low,
                scope.High,
                scope.Low,
                definition.High,
                definition.Low,
                schema.High,
                schema.Low,
                2U,
                ordinal + 3UL,
                7U,
                ordinal + 4UL);
        }

        internal static SlotRecordValue SampleSlot(
            ulong targetOrdinal,
            ulong ownerOrdinal,
            ulong slotOrdinal)
        {
            Id128 target = Id(targetOrdinal);
            Id128 owner = Id(ownerOrdinal);
            Id128 slot = Id(slotOrdinal);
            return new SlotRecordValue(
                target.High,
                target.Low,
                owner.High,
                owner.Low,
                slot.High,
                slot.Low,
                2U,
                -9,
                false);
        }

        internal static GrantRecordValue SampleGrant(
            uint kind,
            ulong scopeOrdinal,
            ulong targetOrdinal,
            ulong subjectOrdinal)
        {
            Id128 scope = Id(scopeOrdinal);
            Id128 target = Id(targetOrdinal);
            Id128 capability = Id(subjectOrdinal);
            Id128 provider = Id(subjectOrdinal + 1UL);
            Id128 rule = Id(subjectOrdinal + 2UL);
            Id128 contract = Id(subjectOrdinal + 3UL);
            Id128 subject = Id(subjectOrdinal + 4UL);
            return new GrantRecordValue(
                kind,
                scope.High,
                scope.Low,
                target.High,
                target.Low,
                capability.High,
                capability.Low,
                2U,
                provider.High,
                provider.Low,
                rule.High,
                rule.Low,
                contract.High,
                contract.Low,
                3U,
                subject.High,
                subject.Low,
                true,
                false,
                (uint)ExclusionTargetKind.Rule,
                4U);
        }

        internal static ClockRecordValue SampleClockDeclaration(ulong ordinal)
        {
            Id128 clock = Id(ordinal + 400UL);
            return new ClockRecordValue(
                0U,
                clock.High,
                clock.Low,
                2U,
                3U,
                true,
                0UL,
                0UL,
                0UL,
                0UL,
                0U,
                0UL,
                0UL,
                0UL,
                0U,
                1U);
        }

        internal static ClockRecordValue SampleClockWake(ulong ordinal)
        {
            Id128 clock = Id(ordinal + 400UL);
            Id128 wake = Id(ordinal + 401UL);
            Id128 payloadSchema = Id(ordinal + 402UL);
            return new ClockRecordValue(
                1U,
                clock.High,
                clock.Low,
                0U,
                0U,
                false,
                wake.High,
                wake.Low,
                payloadSchema.High,
                payloadSchema.Low,
                4U,
                ordinal + 6UL,
                ordinal + 7UL,
                ordinal + 8UL,
                1U,
                2U);
        }

        internal static CommandRecordValue SampleCommand(ulong ordinal, ulong targetOrdinal)
        {
            Id128 issuer = Id(ordinal + 200UL);
            Id128 route = Id(ordinal + 201UL);
            Id128 target = Id(targetOrdinal);
            Id128 schema = Id(ordinal + 202UL);
            return new CommandRecordValue(
                issuer.High,
                issuer.Low,
                ordinal,
                route.High,
                route.Low,
                target.High,
                target.Low,
                schema.High,
                schema.Low,
                2U,
                ordinal + 1UL,
                ordinal + 2UL,
                ordinal + 3UL,
                5U,
                6U,
                ordinal + 10UL,
                ordinal + 11UL,
                ordinal + 12UL,
                ordinal + 13UL,
                new byte[] { 1, 2, 3 });
        }

        internal static MessageRecordValue SampleMessage(
            ulong ordinal,
            ulong bufferOrdinal,
            ulong targetOrdinal)
        {
            Id128 request = Id(ordinal + 300UL);
            Id128 route = Id(ordinal + 301UL);
            Id128 owner = Id(ordinal + 302UL);
            Id128 target = Id(targetOrdinal);
            Id128 payloadSchema = Id(ordinal + 303UL);
            Id128 origin = Id(ordinal + 304UL);
            Id128 producer = Id(ordinal + 305UL);
            Id128 buffer = Id(bufferOrdinal);
            return new MessageRecordValue(
                ordinal + 1UL,
                ordinal + 2UL,
                request.High,
                request.Low,
                ordinal + 3UL,
                route.High,
                route.Low,
                owner.High,
                owner.Low,
                target.High,
                target.Low,
                payloadSchema.High,
                payloadSchema.Low,
                3U,
                4U,
                ordinal + 5UL,
                6U,
                origin.High,
                origin.Low,
                producer.High,
                producer.Low,
                7U,
                buffer.High,
                buffer.Low,
                true,
                true,
                false,
                new byte[] { 9, 8, 7 });
        }

        internal static RngRecordValue SampleRng(ulong ordinal)
        {
            Id128 stream = Id(ordinal + 700UL);
            return new RngRecordValue(stream.High, stream.Low, ordinal + 1UL, ordinal + 2UL, ordinal + 3UL);
        }

        internal static CursorRecordValue SampleCursor(uint rowKind, ulong ordinal)
        {
            Id128 issuer = Id(ordinal + 600UL);
            Id128 session = Id(ordinal + 601UL);
            return new CursorRecordValue(
                rowKind,
                issuer.High,
                issuer.Low,
                ordinal + 9UL,
                session.High,
                session.Low);
        }

        /// <summary>Every field of a header, so a container round-trip is a full equality, not a spot check.</summary>
        internal static void AssertHeaderEquals(HeaderRecordValue expected, HeaderRecordValue actual)
        {
            Assert.That(actual.WorldDefinitionHigh, Is.EqualTo(expected.WorldDefinitionHigh));
            Assert.That(actual.WorldDefinitionLow, Is.EqualTo(expected.WorldDefinitionLow));
            Assert.That(actual.SourceSessionHigh, Is.EqualTo(expected.SourceSessionHigh));
            Assert.That(actual.SourceSessionLow, Is.EqualTo(expected.SourceSessionLow));
            Assert.That(actual.ProtocolMajor, Is.EqualTo(expected.ProtocolMajor));
            Assert.That(actual.ProtocolMinor, Is.EqualTo(expected.ProtocolMinor));
            Assert.That(actual.TemporalModel, Is.EqualTo(expected.TemporalModel));
            Assert.That(actual.StepDurationTicks, Is.EqualTo(expected.StepDurationTicks));
            Assert.That(actual.TicksPerSecond, Is.EqualTo(expected.TicksPerSecond));
            Assert.That(actual.MaxStepsPerPump, Is.EqualTo(expected.MaxStepsPerPump));
            Assert.That(actual.UsesUnscaledHostClock, Is.EqualTo(expected.UsesUnscaledHostClock));
            Assert.That(actual.LogicalStep, Is.EqualTo(expected.LogicalStep));
            Assert.That(actual.TimeDebtTicks, Is.EqualTo(expected.TimeDebtTicks));
            Assert.That(actual.DomainSeconds, Is.EqualTo(expected.DomainSeconds));
            Assert.That(actual.PendingDemand, Is.EqualTo(expected.PendingDemand));
            Assert.That(actual.PropagationMode, Is.EqualTo(expected.PropagationMode));
            Assert.That(actual.CatalogFingerprint, Is.EqualTo(expected.CatalogFingerprint));
            Assert.That(actual.QueuePolicy, Is.EqualTo(expected.QueuePolicy));
            Assert.That(actual.AdmissionCutoff, Is.EqualTo(expected.AdmissionCutoff));
            Assert.That(actual.RejectedQueuedCount, Is.EqualTo(expected.RejectedQueuedCount));
            Assert.That(actual.LastEventSequence, Is.EqualTo(expected.LastEventSequence));
            Assert.That(actual.ScopeCount, Is.EqualTo(expected.ScopeCount));
            Assert.That(actual.InstallCount, Is.EqualTo(expected.InstallCount));
            Assert.That(actual.SelectionCount, Is.EqualTo(expected.SelectionCount));
            Assert.That(actual.TargetCount, Is.EqualTo(expected.TargetCount));
            Assert.That(actual.SlotCount, Is.EqualTo(expected.SlotCount));
            Assert.That(actual.GrantCount, Is.EqualTo(expected.GrantCount));
            Assert.That(actual.ClockCount, Is.EqualTo(expected.ClockCount));
            Assert.That(actual.CommandCount, Is.EqualTo(expected.CommandCount));
            Assert.That(actual.MessageCount, Is.EqualTo(expected.MessageCount));
            Assert.That(actual.RngStreamCount, Is.EqualTo(expected.RngStreamCount));
            Assert.That(actual.CursorCount, Is.EqualTo(expected.CursorCount));
            Assert.That(actual.SourcePublishedRevision, Is.EqualTo(expected.SourcePublishedRevision));
            Assert.That(actual.SourcePublishedEpoch, Is.EqualTo(expected.SourcePublishedEpoch));
            Assert.That(actual.SourceHostTicksPerSecond, Is.EqualTo(expected.SourceHostTicksPerSecond));
            Assert.That(actual.ContentRevisionCount, Is.EqualTo(expected.ContentRevisionCount));
            Assert.That(actual.WorldDefinition, Is.EqualTo(expected.WorldDefinition));
            Assert.That(actual.SourceSession, Is.EqualTo(expected.SourceSession));
            Assert.That(actual.Temporal, Is.EqualTo(expected.Temporal));
            Assert.That(actual.Propagation, Is.EqualTo(expected.Propagation));
            Assert.That(actual.Policy, Is.EqualTo(expected.Policy));
            Assert.That(actual.IsSupportedProtocol, Is.EqualTo(expected.IsSupportedProtocol));
        }

        private static void Run(List<GeneratedFieldSlot> slots, int firstFieldId, int count, WireType type)
        {
            for (int i = 0; i < count; i++)
            {
                slots.Add(new GeneratedFieldSlot(firstFieldId + i, type, true));
            }
        }

        /// <summary>Declared field table of one record kind, matching the encoder below field for field.</summary>
        internal static GeneratedFieldSlot[] SlotsOf(CheckpointRecordKind kind)
        {
            var slots = new List<GeneratedFieldSlot>();
            switch (kind)
            {
                case CheckpointRecordKind.Header:
                    Run(slots, 1, 4, WireType.UInt64);
                    Run(slots, 5, 3, WireType.UInt32);
                    Run(slots, 8, 2, WireType.UInt64);
                    Run(slots, 10, 1, WireType.UInt32);
                    Run(slots, 11, 1, WireType.Bool);
                    Run(slots, 12, 2, WireType.UInt64);
                    Run(slots, 14, 1, WireType.Float64);
                    Run(slots, 15, 1, WireType.UInt64);
                    Run(slots, 16, 1, WireType.UInt32);
                    Run(slots, 17, 4, WireType.UInt64);
                    Run(slots, 21, 1, WireType.UInt32);
                    Run(slots, 22, 1, WireType.UInt64);
                    Run(slots, 23, 1, WireType.UInt32);
                    Run(slots, 24, 1, WireType.UInt64);
                    Run(slots, 25, 11, WireType.UInt32);
                    Run(slots, 36, 3, WireType.UInt64);
                    Run(slots, 39, 1, WireType.UInt32);
                    break;
                case CheckpointRecordKind.Scope:
                    Run(slots, 1, 2, WireType.UInt64);
                    Run(slots, 3, 2, WireType.UInt64);
                    Run(slots, 5, 2, WireType.UInt32);
                    Run(slots, 7, 2, WireType.UInt32);
                    Run(slots, 9, 2, WireType.Bool);
                    Run(slots, 11, 2, WireType.UInt32);
                    break;
                case CheckpointRecordKind.Install:
                    Run(slots, 1, 2, WireType.UInt64);
                    Run(slots, 3, 2, WireType.UInt64);
                    Run(slots, 5, 2, WireType.UInt64);
                    Run(slots, 7, 1, WireType.UInt64);
                    Run(slots, 8, 4, WireType.UInt64);
                    Run(slots, 12, 1, WireType.Int32);
                    Run(slots, 13, 2, WireType.UInt64);
                    Run(slots, 15, 2, WireType.UInt32);
                    Run(slots, 17, 1, WireType.Bytes);
                    Run(slots, 18, 1, WireType.UInt32);
                    Run(slots, 19, 1, WireType.Bool);
                    break;
                case CheckpointRecordKind.Selection:
                    Run(slots, 1, 2, WireType.UInt64);
                    Run(slots, 3, 2, WireType.UInt64);
                    Run(slots, 5, 1, WireType.UInt32);
                    Run(slots, 6, 2, WireType.UInt64);
                    Run(slots, 8, 1, WireType.UInt32);
                    break;
                case CheckpointRecordKind.Target:
                    Run(slots, 1, 2, WireType.UInt64);
                    Run(slots, 3, 2, WireType.UInt64);
                    Run(slots, 5, 2, WireType.UInt64);
                    Run(slots, 7, 2, WireType.UInt64);
                    Run(slots, 9, 1, WireType.UInt32);
                    Run(slots, 10, 1, WireType.UInt64);
                    Run(slots, 11, 1, WireType.UInt32);
                    Run(slots, 12, 1, WireType.UInt64);
                    break;
                case CheckpointRecordKind.Slot:
                    Run(slots, 1, 2, WireType.UInt64);
                    Run(slots, 3, 2, WireType.UInt64);
                    Run(slots, 5, 2, WireType.UInt64);
                    Run(slots, 7, 1, WireType.UInt32);
                    Run(slots, 8, 1, WireType.Int32);
                    Run(slots, 9, 1, WireType.Bool);
                    break;
                case CheckpointRecordKind.Grant:
                    Run(slots, 1, 1, WireType.UInt32);
                    Run(slots, 2, 2, WireType.UInt64);
                    Run(slots, 4, 2, WireType.UInt64);
                    Run(slots, 6, 2, WireType.UInt64);
                    Run(slots, 8, 1, WireType.UInt32);
                    Run(slots, 9, 2, WireType.UInt64);
                    Run(slots, 11, 2, WireType.UInt64);
                    Run(slots, 13, 2, WireType.UInt64);
                    Run(slots, 15, 1, WireType.UInt32);
                    Run(slots, 16, 2, WireType.UInt64);
                    Run(slots, 18, 2, WireType.Bool);
                    Run(slots, 20, 2, WireType.UInt32);
                    break;
                case CheckpointRecordKind.Clock:
                    Run(slots, 1, 1, WireType.UInt32);
                    Run(slots, 2, 2, WireType.UInt64);
                    Run(slots, 4, 2, WireType.UInt32);
                    Run(slots, 6, 1, WireType.Bool);
                    Run(slots, 7, 2, WireType.UInt64);
                    Run(slots, 9, 2, WireType.UInt64);
                    Run(slots, 11, 1, WireType.UInt32);
                    Run(slots, 12, 3, WireType.UInt64);
                    Run(slots, 15, 2, WireType.UInt32);
                    break;
                case CheckpointRecordKind.Command:
                    Run(slots, 1, 2, WireType.UInt64);
                    Run(slots, 3, 1, WireType.UInt64);
                    Run(slots, 4, 2, WireType.UInt64);
                    Run(slots, 6, 2, WireType.UInt64);
                    Run(slots, 8, 2, WireType.UInt64);
                    Run(slots, 10, 1, WireType.UInt32);
                    Run(slots, 11, 3, WireType.UInt64);
                    Run(slots, 14, 2, WireType.UInt32);
                    Run(slots, 16, 4, WireType.UInt64);
                    Run(slots, 20, 1, WireType.Bytes);
                    break;
                case CheckpointRecordKind.Message:
                    Run(slots, 1, 2, WireType.UInt64);
                    Run(slots, 3, 2, WireType.UInt64);
                    Run(slots, 5, 1, WireType.UInt64);
                    Run(slots, 6, 2, WireType.UInt64);
                    Run(slots, 8, 2, WireType.UInt64);
                    Run(slots, 10, 2, WireType.UInt64);
                    Run(slots, 12, 2, WireType.UInt64);
                    Run(slots, 14, 2, WireType.UInt32);
                    Run(slots, 16, 1, WireType.UInt64);
                    Run(slots, 17, 1, WireType.UInt32);
                    Run(slots, 18, 2, WireType.UInt64);
                    Run(slots, 20, 2, WireType.UInt64);
                    Run(slots, 22, 1, WireType.UInt32);
                    Run(slots, 23, 2, WireType.UInt64);
                    Run(slots, 25, 3, WireType.Bool);
                    Run(slots, 28, 1, WireType.Bytes);
                    break;
                case CheckpointRecordKind.Rng:
                    Run(slots, 1, 2, WireType.UInt64);
                    Run(slots, 3, 3, WireType.UInt64);
                    break;
                case CheckpointRecordKind.Cursor:
                    Run(slots, 1, 1, WireType.UInt32);
                    Run(slots, 2, 2, WireType.UInt64);
                    Run(slots, 4, 1, WireType.UInt64);
                    Run(slots, 5, 2, WireType.UInt64);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind), kind, "No test codec declares that record kind.");
            }

            return slots.ToArray();
        }
        internal static ICheckpointRecordCodec<HeaderRecordValue> HeaderCodec() =>
            new TestCheckpointCodec<HeaderRecordValue>(
                CheckpointRecordKind.Header,
                SchemaOf(CheckpointRecordKind.Header),
                SlotsOf(CheckpointRecordKind.Header),
                CheckpointTestEncoding.EncodeHeader,
                CheckpointTestEncoding.DecodeHeader);

        internal static ICheckpointRecordCodec<ScopeRecordValue> ScopeCodec() =>
            new TestCheckpointCodec<ScopeRecordValue>(
                CheckpointRecordKind.Scope,
                SchemaOf(CheckpointRecordKind.Scope),
                SlotsOf(CheckpointRecordKind.Scope),
                CheckpointTestEncoding.EncodeScope,
                CheckpointTestEncoding.DecodeScope);

        internal static ICheckpointRecordCodec<InstallRecordValue> InstallCodec() =>
            new TestCheckpointCodec<InstallRecordValue>(
                CheckpointRecordKind.Install,
                SchemaOf(CheckpointRecordKind.Install),
                SlotsOf(CheckpointRecordKind.Install),
                CheckpointTestEncoding.EncodeInstall,
                CheckpointTestEncoding.DecodeInstall);

        internal static ICheckpointRecordCodec<SelectionRecordValue> SelectionCodec() =>
            new TestCheckpointCodec<SelectionRecordValue>(
                CheckpointRecordKind.Selection,
                SchemaOf(CheckpointRecordKind.Selection),
                SlotsOf(CheckpointRecordKind.Selection),
                CheckpointTestEncoding.EncodeSelection,
                CheckpointTestEncoding.DecodeSelection);

        internal static ICheckpointRecordCodec<TargetRecordValue> TargetCodec() =>
            new TestCheckpointCodec<TargetRecordValue>(
                CheckpointRecordKind.Target,
                SchemaOf(CheckpointRecordKind.Target),
                SlotsOf(CheckpointRecordKind.Target),
                CheckpointTestEncoding.EncodeTarget,
                CheckpointTestEncoding.DecodeTarget);

        internal static ICheckpointRecordCodec<SlotRecordValue> SlotCodec() =>
            new TestCheckpointCodec<SlotRecordValue>(
                CheckpointRecordKind.Slot,
                SchemaOf(CheckpointRecordKind.Slot),
                SlotsOf(CheckpointRecordKind.Slot),
                CheckpointTestEncoding.EncodeSlot,
                CheckpointTestEncoding.DecodeSlot);

        internal static ICheckpointRecordCodec<GrantRecordValue> GrantCodec() =>
            new TestCheckpointCodec<GrantRecordValue>(
                CheckpointRecordKind.Grant,
                SchemaOf(CheckpointRecordKind.Grant),
                SlotsOf(CheckpointRecordKind.Grant),
                CheckpointTestEncoding.EncodeGrant,
                CheckpointTestEncoding.DecodeGrant);

        internal static ICheckpointRecordCodec<ClockRecordValue> ClockCodec() =>
            new TestCheckpointCodec<ClockRecordValue>(
                CheckpointRecordKind.Clock,
                SchemaOf(CheckpointRecordKind.Clock),
                SlotsOf(CheckpointRecordKind.Clock),
                CheckpointTestEncoding.EncodeClock,
                CheckpointTestEncoding.DecodeClock);

        internal static ICheckpointRecordCodec<CommandRecordValue> CommandCodec() =>
            new TestCheckpointCodec<CommandRecordValue>(
                CheckpointRecordKind.Command,
                SchemaOf(CheckpointRecordKind.Command),
                SlotsOf(CheckpointRecordKind.Command),
                CheckpointTestEncoding.EncodeCommand,
                CheckpointTestEncoding.DecodeCommand);

        internal static ICheckpointRecordCodec<MessageRecordValue> MessageCodec() =>
            new TestCheckpointCodec<MessageRecordValue>(
                CheckpointRecordKind.Message,
                SchemaOf(CheckpointRecordKind.Message),
                SlotsOf(CheckpointRecordKind.Message),
                CheckpointTestEncoding.EncodeMessage,
                CheckpointTestEncoding.DecodeMessage);

        internal static ICheckpointRecordCodec<RngRecordValue> RngCodec() =>
            new TestCheckpointCodec<RngRecordValue>(
                CheckpointRecordKind.Rng,
                SchemaOf(CheckpointRecordKind.Rng),
                SlotsOf(CheckpointRecordKind.Rng),
                CheckpointTestEncoding.EncodeRng,
                CheckpointTestEncoding.DecodeRng);

        internal static ICheckpointRecordCodec<CursorRecordValue> CursorCodec() =>
            new TestCheckpointCodec<CursorRecordValue>(
                CheckpointRecordKind.Cursor,
                SchemaOf(CheckpointRecordKind.Cursor),
                SlotsOf(CheckpointRecordKind.Cursor),
                CheckpointTestEncoding.EncodeCursor,
                CheckpointTestEncoding.DecodeCursor);

        /// <summary>One codec, as the non-generic interface a codec set is built from.</summary>
        internal static ICheckpointRecordCodec Codec(CheckpointRecordKind kind)
        {
            switch (kind)
            {
                case CheckpointRecordKind.Header: return HeaderCodec();
                case CheckpointRecordKind.Scope: return ScopeCodec();
                case CheckpointRecordKind.Install: return InstallCodec();
                case CheckpointRecordKind.Selection: return SelectionCodec();
                case CheckpointRecordKind.Target: return TargetCodec();
                case CheckpointRecordKind.Slot: return SlotCodec();
                case CheckpointRecordKind.Grant: return GrantCodec();
                case CheckpointRecordKind.Clock: return ClockCodec();
                case CheckpointRecordKind.Command: return CommandCodec();
                case CheckpointRecordKind.Message: return MessageCodec();
                case CheckpointRecordKind.Rng: return RngCodec();
                case CheckpointRecordKind.Cursor: return CursorCodec();
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind), kind, "No test codec declares that record kind.");
            }
        }

        /// <summary>The typed codec of one kind, for a test that knows the value type it is asking for.</summary>
        internal static ICheckpointRecordCodec<TValue> TypedCodec<TValue>(CheckpointRecordKind kind)
            where TValue : struct
        {
            ICheckpointRecordCodec codec = Codec(kind);
            if (codec is ICheckpointRecordCodec<TValue> typed)
            {
                return typed;
            }

            throw new ArgumentException(
                "The test codec for " + kind + " is not a " + typeof(TValue).Name + " codec (P-054).",
                nameof(kind));
        }

        /// <summary>A header codec that reports any kind, so the codec set's ordinal check is reachable.</summary>
        internal static ICheckpointRecordCodec HeaderCodecForUndeclaredKind(CheckpointRecordKind kind) =>
            new TestCheckpointCodec<HeaderRecordValue>(
                kind,
                SchemaOf(CheckpointRecordKind.Header),
                SlotsOf(CheckpointRecordKind.Header),
                CheckpointTestEncoding.EncodeHeader,
                CheckpointTestEncoding.DecodeHeader);

        /// <summary>A complete set: one codec per declared kind.</summary>
        internal static CheckpointCodecSet CompleteSet()
        {
            var codecs = new List<ICheckpointRecordCodec>();
            for (int i = 0; i < CheckpointFormat.RecordKindCount; i++)
            {
                codecs.Add(Codec((CheckpointRecordKind)i));
            }

            return new CheckpointCodecSet(codecs);
        }

        /// <summary>A set containing the header codec plus the codecs of the requested body kinds.</summary>
        internal static CheckpointCodecSet SetOf(params CheckpointRecordKind[] kinds)
        {
            var codecs = new List<ICheckpointRecordCodec> { HeaderCodec() };
            for (int i = 0; i < kinds.Length; i++)
            {
                if (kinds[i] == CheckpointRecordKind.Header)
                {
                    continue;
                }

                codecs.Add(Codec(kinds[i]));
            }

            return new CheckpointCodecSet(codecs);
        }
    }

    /// <summary>
    /// The encoders and decoders of the test codecs. Each encoder writes ascending field ids matching
    /// <see cref="CheckpointTestRecords.SlotsOf"/>, and each decoder reads them back through the same ids, so a
    /// mismatched layout fails the round-trip rather than silently carrying the wrong words.
    /// </summary>
    internal static class CheckpointTestEncoding
    {
        internal static EnvelopeWriter NewWriter(CheckpointRecordKind kind) =>
            new EnvelopeWriter(
                new EnvelopeHeader(1, 0, CheckpointTestRecords.SchemaOf(kind), CheckpointFormat.KnownFeatureIds));

        internal static byte[] EncodeHeader(HeaderRecordValue value)
        {
            EnvelopeWriter writer = NewWriter(CheckpointRecordKind.Header);
            U64(writer, 1, value.WorldDefinitionHigh, value.WorldDefinitionLow);
            U64(writer, 3, value.SourceSessionHigh, value.SourceSessionLow);
            U32(writer, 5, value.ProtocolMajor, value.ProtocolMinor, value.TemporalModel);
            U64(writer, 8, value.StepDurationTicks, value.TicksPerSecond);
            writer.WriteUInt32Field(10, value.MaxStepsPerPump);
            writer.WriteBoolField(11, value.UsesUnscaledHostClock);
            U64(writer, 12, value.LogicalStep, value.TimeDebtTicks);
            writer.WriteFloat64Field(14, value.DomainSeconds);
            writer.WriteUInt64Field(15, value.PendingDemand);
            writer.WriteUInt32Field(16, value.PropagationMode);
            U64(
                writer,
                17,
                value.CatalogFingerprintA,
                value.CatalogFingerprintB,
                value.CatalogFingerprintC,
                value.CatalogFingerprintD);
            writer.WriteUInt32Field(21, value.QueuePolicy);
            writer.WriteUInt64Field(22, value.AdmissionCutoff);
            writer.WriteUInt32Field(23, value.RejectedQueuedCount);
            writer.WriteUInt64Field(24, value.LastEventSequence);
            U32(
                writer,
                25,
                value.ScopeCount,
                value.InstallCount,
                value.SelectionCount,
                value.TargetCount,
                value.SlotCount,
                value.GrantCount,
                value.ClockCount,
                value.CommandCount,
                value.MessageCount,
                value.RngStreamCount,
                value.CursorCount);
            U64(
                writer,
                36,
                value.SourcePublishedRevision,
                value.SourcePublishedEpoch,
                value.SourceHostTicksPerSecond);
            writer.WriteUInt32Field(39, value.ContentRevisionCount);
            writer.WriteChecksum();
            return writer.ToArray();
        }

        internal static HeaderRecordValue DecodeHeader(TestRecordReader fields)
        {
            ulong[] world = fields.UInt64Range(1, 4);
            uint[] protocol = fields.UInt32Range(5, 3);
            ulong[] cadence = fields.UInt64Range(8, 2);
            ulong[] step = fields.UInt64Range(12, 2);
            ulong[] fingerprint = fields.UInt64Range(17, 4);
            uint[] counts = fields.UInt32Range(25, 11);
            ulong[] published = fields.UInt64Range(36, 3);
            return new HeaderRecordValue(
                world[0],
                world[1],
                world[2],
                world[3],
                protocol[0],
                protocol[1],
                protocol[2],
                cadence[0],
                cadence[1],
                fields.UInt32(10),
                fields.Bool(11),
                step[0],
                step[1],
                fields.Float64(14),
                fields.UInt64(15),
                fields.UInt32(16),
                fingerprint[0],
                fingerprint[1],
                fingerprint[2],
                fingerprint[3],
                fields.UInt32(21),
                fields.UInt64(22),
                fields.UInt32(23),
                fields.UInt64(24),
                counts[0],
                counts[1],
                counts[2],
                counts[3],
                counts[4],
                counts[5],
                counts[6],
                counts[7],
                counts[8],
                counts[9],
                counts[10],
                published[0],
                published[1],
                published[2],
                fields.UInt32(39));
        }

        internal static byte[] EncodeScope(ScopeRecordValue value)
        {
            EnvelopeWriter writer = NewWriter(CheckpointRecordKind.Scope);
            U64(writer, 1, value.ScopeHigh, value.ScopeLow);
            U64(writer, 3, value.ParentHigh, value.ParentLow);
            U32(writer, 5, value.Depth, value.Mode);
            U32(writer, 7, value.InstallCount, value.GrantCount);
            writer.WriteBoolField(9, value.ServiceIsolationAll);
            writer.WriteBoolField(10, value.CapabilityIsolationAll);
            U32(writer, 11, value.ServiceIsolationCount, value.CapabilityIsolationCount);
            writer.WriteChecksum();
            return writer.ToArray();
        }

        internal static ScopeRecordValue DecodeScope(TestRecordReader fields)
        {
            ulong[] scope = fields.UInt64Range(1, 2);
            ulong[] parent = fields.UInt64Range(3, 2);
            uint[] mode = fields.UInt32Range(5, 2);
            uint[] boundary = fields.UInt32Range(7, 2);
            uint[] isolation = fields.UInt32Range(11, 2);
            return new ScopeRecordValue(
                scope[0],
                scope[1],
                parent[0],
                parent[1],
                mode[0],
                mode[1],
                boundary[0],
                boundary[1],
                fields.Bool(9),
                fields.Bool(10),
                isolation[0],
                isolation[1]);
        }

        internal static byte[] EncodeInstall(InstallRecordValue value)
        {
            EnvelopeWriter writer = NewWriter(CheckpointRecordKind.Install);
            U64(writer, 1, value.InstanceHigh, value.InstanceLow);
            U64(writer, 3, value.PluginTypeHigh, value.PluginTypeLow);
            U64(writer, 5, value.ScopeHigh, value.ScopeLow);
            writer.WriteUInt64Field(7, value.ConfigRevision);
            U64(writer, 8, value.ConfigHashA, value.ConfigHashB, value.ConfigHashC, value.ConfigHashD);
            writer.WriteInt32Field(12, value.Priority);
            U64(writer, 13, value.Generation, value.ActivationEpoch);
            U32(writer, 15, value.State, value.ConfigFieldCount);
            writer.WriteBytesField(17, value.ConfigBytes);
            writer.WriteUInt32Field(18, value.SelectionCount);
            writer.WriteBoolField(19, value.HasConfigDocument);
            writer.WriteChecksum();
            return writer.ToArray();
        }

        internal static InstallRecordValue DecodeInstall(TestRecordReader fields)
        {
            ulong[] instance = fields.UInt64Range(1, 2);
            ulong[] pluginType = fields.UInt64Range(3, 2);
            ulong[] scope = fields.UInt64Range(5, 2);
            ulong[] configHash = fields.UInt64Range(8, 4);
            ulong[] epochs = fields.UInt64Range(13, 2);
            uint[] state = fields.UInt32Range(15, 2);
            return new InstallRecordValue(
                instance[0],
                instance[1],
                pluginType[0],
                pluginType[1],
                scope[0],
                scope[1],
                fields.UInt64(7),
                configHash[0],
                configHash[1],
                configHash[2],
                configHash[3],
                fields.Int32(12),
                epochs[0],
                epochs[1],
                state[0],
                state[1],
                fields.Bytes(17),
                fields.UInt32(18),
                fields.Bool(19));
        }

        internal static byte[] EncodeSelection(SelectionRecordValue value)
        {
            EnvelopeWriter writer = NewWriter(CheckpointRecordKind.Selection);
            U64(writer, 1, value.InstanceHigh, value.InstanceLow);
            U64(writer, 3, value.ContractHigh, value.ContractLow);
            writer.WriteUInt32Field(5, value.ContractVersion);
            U64(writer, 6, value.ProviderHigh, value.ProviderLow);
            writer.WriteUInt32Field(8, value.Order);
            writer.WriteChecksum();
            return writer.ToArray();
        }

        internal static SelectionRecordValue DecodeSelection(TestRecordReader fields)
        {
            ulong[] instance = fields.UInt64Range(1, 2);
            ulong[] contract = fields.UInt64Range(3, 2);
            ulong[] provider = fields.UInt64Range(6, 2);
            return new SelectionRecordValue(
                instance[0],
                instance[1],
                contract[0],
                contract[1],
                fields.UInt32(5),
                provider[0],
                provider[1],
                fields.UInt32(8));
        }

        internal static byte[] EncodeTarget(TargetRecordValue value)
        {
            EnvelopeWriter writer = NewWriter(CheckpointRecordKind.Target);
            U64(writer, 1, value.TargetHigh, value.TargetLow);
            U64(writer, 3, value.ScopeHigh, value.ScopeLow);
            U64(writer, 5, value.DefinitionHigh, value.DefinitionLow);
            U64(writer, 7, value.SchemaHigh, value.SchemaLow);
            writer.WriteUInt32Field(9, value.SchemaVersion);
            writer.WriteUInt64Field(10, value.ContentRevision);
            writer.WriteUInt32Field(11, value.SourceSlot);
            writer.WriteUInt64Field(12, value.SourceGeneration);
            writer.WriteChecksum();
            return writer.ToArray();
        }

        internal static TargetRecordValue DecodeTarget(TestRecordReader fields)
        {
            ulong[] target = fields.UInt64Range(1, 2);
            ulong[] scope = fields.UInt64Range(3, 2);
            ulong[] definition = fields.UInt64Range(5, 2);
            ulong[] schema = fields.UInt64Range(7, 2);
            return new TargetRecordValue(
                target[0],
                target[1],
                scope[0],
                scope[1],
                definition[0],
                definition[1],
                schema[0],
                schema[1],
                fields.UInt32(9),
                fields.UInt64(10),
                fields.UInt32(11),
                fields.UInt64(12));
        }

        internal static byte[] EncodeSlot(SlotRecordValue value)
        {
            EnvelopeWriter writer = NewWriter(CheckpointRecordKind.Slot);
            U64(writer, 1, value.TargetHigh, value.TargetLow);
            U64(writer, 3, value.OwnerHigh, value.OwnerLow);
            U64(writer, 5, value.SlotHigh, value.SlotLow);
            writer.WriteUInt32Field(7, value.SchemaVersion);
            writer.WriteInt32Field(8, value.Value);
            writer.WriteBoolField(9, value.Active);
            writer.WriteChecksum();
            return writer.ToArray();
        }

        internal static SlotRecordValue DecodeSlot(TestRecordReader fields)
        {
            ulong[] target = fields.UInt64Range(1, 2);
            ulong[] owner = fields.UInt64Range(3, 2);
            ulong[] slot = fields.UInt64Range(5, 2);
            return new SlotRecordValue(
                target[0],
                target[1],
                owner[0],
                owner[1],
                slot[0],
                slot[1],
                fields.UInt32(7),
                fields.Int32(8),
                fields.Bool(9));
        }

        internal static byte[] EncodeGrant(GrantRecordValue value)
        {
            EnvelopeWriter writer = NewWriter(CheckpointRecordKind.Grant);
            writer.WriteUInt32Field(1, value.Kind);
            U64(writer, 2, value.ScopeHigh, value.ScopeLow);
            U64(writer, 4, value.TargetHigh, value.TargetLow);
            U64(writer, 6, value.CapabilityHigh, value.CapabilityLow);
            writer.WriteUInt32Field(8, value.CapabilityVersion);
            U64(writer, 9, value.ProviderHigh, value.ProviderLow);
            U64(writer, 11, value.RuleHigh, value.RuleLow);
            U64(writer, 13, value.ContractHigh, value.ContractLow);
            writer.WriteUInt32Field(15, value.ContractVersion);
            U64(writer, 16, value.SubjectHigh, value.SubjectLow);
            writer.WriteBoolField(18, value.AppliesToSubtree);
            writer.WriteBoolField(19, value.AllContracts);
            U32(writer, 20, value.ExclusionKind, value.Order);
            writer.WriteChecksum();
            return writer.ToArray();
        }

        internal static GrantRecordValue DecodeGrant(TestRecordReader fields)
        {
            ulong[] scope = fields.UInt64Range(2, 2);
            ulong[] target = fields.UInt64Range(4, 2);
            ulong[] capability = fields.UInt64Range(6, 2);
            ulong[] provider = fields.UInt64Range(9, 2);
            ulong[] rule = fields.UInt64Range(11, 2);
            ulong[] contract = fields.UInt64Range(13, 2);
            ulong[] subject = fields.UInt64Range(16, 2);
            return new GrantRecordValue(
                fields.UInt32(1),
                scope[0],
                scope[1],
                target[0],
                target[1],
                capability[0],
                capability[1],
                fields.UInt32(8),
                provider[0],
                provider[1],
                rule[0],
                rule[1],
                contract[0],
                contract[1],
                fields.UInt32(15),
                subject[0],
                subject[1],
                fields.Bool(18),
                fields.Bool(19),
                fields.UInt32(20),
                fields.UInt32(21));
        }

        internal static byte[] EncodeClock(ClockRecordValue value)
        {
            EnvelopeWriter writer = NewWriter(CheckpointRecordKind.Clock);
            writer.WriteUInt32Field(1, value.RowKind);
            U64(writer, 2, value.ClockHigh, value.ClockLow);
            U32(writer, 4, value.ClockKind, value.PausePolicy);
            writer.WriteBoolField(6, value.Persists);
            U64(writer, 7, value.WakeHigh, value.WakeLow);
            U64(writer, 9, value.PayloadSchemaHigh, value.PayloadSchemaLow);
            writer.WriteUInt32Field(11, value.PayloadSchemaVersion);
            U64(writer, 12, value.ScheduledAtSequence, value.RemainingSteps, value.RemainingTicks);
            U32(writer, 15, value.WakeState, value.Order);
            writer.WriteChecksum();
            return writer.ToArray();
        }

        internal static ClockRecordValue DecodeClock(TestRecordReader fields)
        {
            ulong[] clock = fields.UInt64Range(2, 2);
            uint[] kind = fields.UInt32Range(4, 2);
            ulong[] wake = fields.UInt64Range(7, 2);
            ulong[] payloadSchema = fields.UInt64Range(9, 2);
            ulong[] schedule = fields.UInt64Range(12, 3);
            uint[] state = fields.UInt32Range(15, 2);
            return new ClockRecordValue(
                fields.UInt32(1),
                clock[0],
                clock[1],
                kind[0],
                kind[1],
                fields.Bool(6),
                wake[0],
                wake[1],
                payloadSchema[0],
                payloadSchema[1],
                fields.UInt32(11),
                schedule[0],
                schedule[1],
                schedule[2],
                state[0],
                state[1]);
        }

        internal static byte[] EncodeCommand(CommandRecordValue value)
        {
            EnvelopeWriter writer = NewWriter(CheckpointRecordKind.Command);
            U64(writer, 1, value.IssuerHigh, value.IssuerLow);
            writer.WriteUInt64Field(3, value.IssuerSequence);
            U64(writer, 4, value.RouteHigh, value.RouteLow);
            U64(writer, 6, value.TargetHigh, value.TargetLow);
            U64(writer, 8, value.SchemaHigh, value.SchemaLow);
            writer.WriteUInt32Field(10, value.SchemaVersion);
            U64(writer, 11, value.AdmittedStep, value.AdmittedEpoch, value.AdmissionSequence);
            U32(writer, 14, value.OrderOrdinal, value.OriginKind);
            U64(writer, 16, value.InputHashA, value.InputHashB, value.InputHashC, value.InputHashD);
            writer.WriteBytesField(20, value.Payload);
            writer.WriteChecksum();
            return writer.ToArray();
        }

        internal static CommandRecordValue DecodeCommand(TestRecordReader fields)
        {
            ulong[] issuer = fields.UInt64Range(1, 2);
            ulong[] route = fields.UInt64Range(4, 2);
            ulong[] target = fields.UInt64Range(6, 2);
            ulong[] schema = fields.UInt64Range(8, 2);
            ulong[] admission = fields.UInt64Range(11, 3);
            uint[] order = fields.UInt32Range(14, 2);
            ulong[] inputHash = fields.UInt64Range(16, 4);
            return new CommandRecordValue(
                issuer[0],
                issuer[1],
                fields.UInt64(3),
                route[0],
                route[1],
                target[0],
                target[1],
                schema[0],
                schema[1],
                fields.UInt32(10),
                admission[0],
                admission[1],
                admission[2],
                order[0],
                order[1],
                inputHash[0],
                inputHash[1],
                inputHash[2],
                inputHash[3],
                fields.Bytes(20));
        }

        internal static byte[] EncodeMessage(MessageRecordValue value)
        {
            EnvelopeWriter writer = NewWriter(CheckpointRecordKind.Message);
            U64(writer, 1, value.Step, value.Epoch);
            U64(writer, 3, value.RequestIssuerHigh, value.RequestIssuerLow);
            writer.WriteUInt64Field(5, value.RequestSequence);
            U64(writer, 6, value.RouteHigh, value.RouteLow);
            U64(writer, 8, value.OwnerHigh, value.OwnerLow);
            U64(writer, 10, value.TargetHigh, value.TargetLow);
            U64(writer, 12, value.PayloadSchemaHigh, value.PayloadSchemaLow);
            U32(writer, 14, value.PayloadSchemaVersion, value.MessageKind);
            writer.WriteUInt64Field(16, value.OrderAdmitted);
            writer.WriteUInt32Field(17, value.OrderOrdinal);
            U64(writer, 18, value.OriginKeyHigh, value.OriginKeyLow);
            U64(writer, 20, value.ProducerKeyHigh, value.ProducerKeyLow);
            writer.WriteUInt32Field(22, value.ProducerKeyVersion);
            U64(writer, 23, value.BufferHigh, value.BufferLow);
            writer.WriteBoolField(25, value.HasPayload);
            writer.WriteBoolField(26, value.HasRequest);
            writer.WriteBoolField(27, value.IsOutcome);
            writer.WriteBytesField(28, value.Payload);
            writer.WriteChecksum();
            return writer.ToArray();
        }

        internal static MessageRecordValue DecodeMessage(TestRecordReader fields)
        {
            ulong[] step = fields.UInt64Range(1, 2);
            ulong[] request = fields.UInt64Range(3, 2);
            ulong[] route = fields.UInt64Range(6, 2);
            ulong[] owner = fields.UInt64Range(8, 2);
            ulong[] target = fields.UInt64Range(10, 2);
            ulong[] payloadSchema = fields.UInt64Range(12, 2);
            uint[] payloadAndKind = fields.UInt32Range(14, 2);
            ulong[] origin = fields.UInt64Range(18, 2);
            ulong[] producer = fields.UInt64Range(20, 2);
            ulong[] buffer = fields.UInt64Range(23, 2);
            return new MessageRecordValue(
                step[0],
                step[1],
                request[0],
                request[1],
                fields.UInt64(5),
                route[0],
                route[1],
                owner[0],
                owner[1],
                target[0],
                target[1],
                payloadSchema[0],
                payloadSchema[1],
                payloadAndKind[0],
                payloadAndKind[1],
                fields.UInt64(16),
                fields.UInt32(17),
                origin[0],
                origin[1],
                producer[0],
                producer[1],
                fields.UInt32(22),
                buffer[0],
                buffer[1],
                fields.Bool(25),
                fields.Bool(26),
                fields.Bool(27),
                fields.Bytes(28));
        }

        internal static byte[] EncodeRng(RngRecordValue value)
        {
            EnvelopeWriter writer = NewWriter(CheckpointRecordKind.Rng);
            U64(writer, 1, value.StreamHigh, value.StreamLow);
            U64(writer, 3, value.State, value.StreamKey, value.DrawCount);
            writer.WriteChecksum();
            return writer.ToArray();
        }

        internal static RngRecordValue DecodeRng(TestRecordReader fields)
        {
            ulong[] stream = fields.UInt64Range(1, 2);
            ulong[] state = fields.UInt64Range(3, 3);
            return new RngRecordValue(stream[0], stream[1], state[0], state[1], state[2]);
        }

        internal static byte[] EncodeCursor(CursorRecordValue value)
        {
            EnvelopeWriter writer = NewWriter(CheckpointRecordKind.Cursor);
            writer.WriteUInt32Field(1, value.RowKind);
            U64(writer, 2, value.IssuerHigh, value.IssuerLow);
            writer.WriteUInt64Field(4, value.Sequence);
            U64(writer, 5, value.SessionHigh, value.SessionLow);
            writer.WriteChecksum();
            return writer.ToArray();
        }

        internal static CursorRecordValue DecodeCursor(TestRecordReader fields)
        {
            ulong[] issuer = fields.UInt64Range(2, 2);
            ulong[] session = fields.UInt64Range(5, 2);
            return new CursorRecordValue(
                fields.UInt32(1),
                issuer[0],
                issuer[1],
                fields.UInt64(4),
                session[0],
                session[1]);
        }

        private static void U64(EnvelopeWriter writer, int firstFieldId, params ulong[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                writer.WriteUInt64Field(firstFieldId + i, values[i]);
            }
        }

        private static void U32(EnvelopeWriter writer, int firstFieldId, params uint[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                writer.WriteUInt32Field(firstFieldId + i, values[i]);
            }
        }
    }
}

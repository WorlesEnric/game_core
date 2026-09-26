// Checkpoint record value and format-helper tests (GC-018). Normative sources: P-004 and P-005 (a record carries
// stable 128-bit identities and never a runtime handle), P-008 (canonical order), P-052 (every failure carries a
// stable diagnostic code), P-053 (what a checkpoint contains) and P-054 (explicit field ids, no reflection).
//
// The hand-written test codec set these tests drive lives in CheckpointTestCodecs.cs; the production codec set is
// generated and no test project can compile generated source.
#nullable enable
using System;
using GameCore.Contracts;
using NUnit.Framework;

namespace GameCore.Contracts.Tests
{
    /// <summary>
    /// Every record kind's value survives an encode/decode round-trip through its codec, and the decoded value's
    /// accessor projections name the identities the constructor was given (P-004, P-005, P-053).
    /// </summary>
    [TestFixture]
    public sealed class CheckpointRecordRoundTripTests
    {
        private static TValue RoundTrip<TValue>(CheckpointRecordKind kind, TValue value)
            where TValue : struct
        {
            ICheckpointRecordCodec<TValue> codec = CheckpointTestRecords.TypedCodec<TValue>(kind);
            byte[] document = codec.Encode(value);
            Assert.That(codec.TryValidate(document, out EnvelopeError error), Is.True, error.ToString());
            Assert.That(
                codec.TryDecode(document, out TValue decoded, out EnvelopeError decodeError),
                Is.True,
                decodeError.ToString());
            return decoded;
        }

        [Test]
        public void HeaderRecordRoundTripsEveryField()
        {
            HeaderRecordValue header = CheckpointTestRecords.Header(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);
            HeaderRecordValue decoded = RoundTrip(CheckpointRecordKind.Header, header);

            CheckpointTestRecords.AssertHeaderEquals(header, decoded);
            Assert.That(decoded.Temporal, Is.EqualTo(TemporalModel.CommandDriven));
            Assert.That(decoded.Propagation, Is.EqualTo(PropagationMode.Conservative));
            Assert.That(decoded.Policy, Is.EqualTo(CheckpointQueuePolicy.IncludeQueued));
            Assert.That(decoded.IsSupportedProtocol, Is.True);
            Assert.That(
                decoded.WorldDefinition,
                Is.EqualTo(new WorldDefinitionId(new Id128(0x1111111111111111UL, 0x2222222222222222UL))));
            Assert.That(
                decoded.SourceSession,
                Is.EqualTo(new WorldId(new Id128(0x3333333333333333UL, 0x4444444444444444UL))));
            Assert.That(decoded.ScopeCount, Is.EqualTo(1U));
            Assert.That(decoded.CursorCount, Is.EqualTo(11U));
            Assert.That(decoded.DomainSeconds, Is.EqualTo(12.5d));
        }

        [Test]
        public void ScopeRecordRoundTripsIdentityParentDepthAndBoundaryFlags()
        {
            ScopeRecordValue decoded = RoundTrip(
                CheckpointRecordKind.Scope, CheckpointTestRecords.SampleScope(1, 2, 3));

            Assert.That(decoded.Scope, Is.EqualTo(CheckpointTestRecords.ScopeIdOf(1)));
            Assert.That(decoded.Parent, Is.EqualTo(CheckpointTestRecords.ScopeIdOf(2)));
            Assert.That(decoded.IsRoot, Is.False);
            Assert.That(decoded.Depth, Is.EqualTo(3U));
            Assert.That(decoded.Propagation, Is.EqualTo(PropagationMode.Automatic));
            Assert.That(decoded.InstallCount, Is.EqualTo(7U));
            Assert.That(decoded.GrantCount, Is.EqualTo(8U));
            Assert.That(decoded.ServiceIsolationAll, Is.True);
            Assert.That(decoded.CapabilityIsolationAll, Is.False);
            Assert.That(decoded.ServiceIsolationCount, Is.EqualTo(9U));
            Assert.That(decoded.CapabilityIsolationCount, Is.EqualTo(10U));
        }

        [Test]
        public void RootScopeRecordReportsNoParent()
        {
            ScopeRecordValue decoded = RoundTrip(
                CheckpointRecordKind.Scope, CheckpointTestRecords.SampleRootScope(1));

            Assert.That(decoded.IsRoot, Is.True);
            Assert.That(decoded.Parent, Is.EqualTo(new ScopeId(Id128.Zero)));
            Assert.That(decoded.Scope, Is.EqualTo(CheckpointTestRecords.ScopeIdOf(1)));
        }

        [Test]
        public void InstallRecordRoundTripsIdentityScopeConfigHashAndLifecycle()
        {
            InstallRecordValue install = CheckpointTestRecords.SampleInstall(1, 2);
            InstallRecordValue decoded = RoundTrip(CheckpointRecordKind.Install, install);

            Assert.That(decoded.Instance, Is.EqualTo(CheckpointTestRecords.InstallIdOf(1)));
            Assert.That(decoded.PluginType, Is.EqualTo(CheckpointTestRecords.PluginTypeIdOf(801)));
            Assert.That(decoded.Scope, Is.EqualTo(CheckpointTestRecords.ScopeIdOf(2)));
            Assert.That(decoded.Revision, Is.EqualTo(new DefinitionRevision(2UL)));
            Assert.That(decoded.ConfigHash, Is.EqualTo(install.ConfigHash));
            Assert.That(decoded.ConfigHash.ToArray(), Is.EqualTo(install.ConfigHash.ToArray()));
            Assert.That(decoded.Priority, Is.EqualTo(-7));
            Assert.That(decoded.Generation, Is.EqualTo(7UL));
            Assert.That(decoded.ActivationEpoch, Is.EqualTo(8UL));
            Assert.That(decoded.Lifecycle, Is.EqualTo(InstallationState.Active));
            Assert.That(decoded.ConfigFieldCount, Is.EqualTo(8U));
            Assert.That(decoded.ConfigBytes, Is.EqualTo(new byte[] { 4, 5, 6 }));
            Assert.That(decoded.SelectionCount, Is.EqualTo(9U));
            Assert.That(decoded.HasConfigDocument, Is.True);
        }

        [Test]
        public void SelectionRecordRoundTripsContractAndProvider()
        {
            SelectionRecordValue decoded = RoundTrip(
                CheckpointRecordKind.Selection, CheckpointTestRecords.SampleSelection(1, 2));

            Assert.That(decoded.Instance, Is.EqualTo(CheckpointTestRecords.InstallIdOf(1)));
            Assert.That(decoded.Contract, Is.EqualTo(new ContractRef(CheckpointTestRecords.Id(901), 2U)));
            Assert.That(decoded.Provider, Is.EqualTo(CheckpointTestRecords.ProviderIdOf(2)));
            Assert.That(decoded.Order, Is.EqualTo(3U));
            Assert.That(
                decoded.ToSelection(),
                Is.EqualTo(new ServiceSelection(decoded.Contract, decoded.Provider)));
        }

        [Test]
        public void TargetRecordRoundTripsIdentityScopeAndRecipe()
        {
            TargetRecordValue target = CheckpointTestRecords.SampleTarget(1, 2);
            TargetRecordValue decoded = RoundTrip(CheckpointRecordKind.Target, target);

            Assert.That(decoded.Target, Is.EqualTo(CheckpointTestRecords.TargetIdOf(1)));
            Assert.That(decoded.Scope, Is.EqualTo(CheckpointTestRecords.ScopeIdOf(2)));
            DefinitionRef recipe = decoded.Recipe;
            Assert.That(recipe.Id, Is.EqualTo(CheckpointTestRecords.DefinitionIdOf(1001)));
            Assert.That(recipe.Schema, Is.EqualTo(new SchemaRef(CheckpointTestRecords.SchemaIdOf(1002), 2U)));
            Assert.That(recipe.Revision, Is.EqualTo(new DefinitionRevision(4UL)));
            Assert.That(decoded.Recipe, Is.EqualTo(target.Recipe));
            Assert.That(decoded.SourceSlot, Is.EqualTo(7U));
            Assert.That(decoded.SourceGeneration, Is.EqualTo(5UL));
        }

        [Test]
        public void SlotRecordRoundTripsItsStateSlotKeyAndDormantFlag()
        {
            SlotRecordValue slot = CheckpointTestRecords.SampleSlot(1, 2, 3);
            SlotRecordValue decoded = RoundTrip(CheckpointRecordKind.Slot, slot);

            Assert.That(
                decoded.Key,
                Is.EqualTo(new StateSlotKey(
                    CheckpointTestRecords.TargetIdOf(1),
                    CheckpointTestRecords.OwnerIdOf(2),
                    CheckpointTestRecords.SlotIdOf(3))));
            Assert.That(decoded.Key, Is.EqualTo(slot.Key));
            Assert.That(decoded.SchemaVersion, Is.EqualTo(2U));
            Assert.That(decoded.Value, Is.EqualTo(-9));
            Assert.That(decoded.Active, Is.False);
        }

        [Test]
        public void GrantRecordRoundTripsItsImportProviderRuleAndContractIdentities()
        {
            GrantRecordValue decoded = RoundTrip(
                CheckpointRecordKind.Grant,
                CheckpointTestRecords.SampleGrant((uint)GrantKind.TargetOptIn, 1, 2, 3));

            Assert.That(decoded.Grant, Is.EqualTo(GrantKind.TargetOptIn));
            Assert.That(decoded.Scope, Is.EqualTo(CheckpointTestRecords.ScopeIdOf(1)));
            Assert.That(decoded.Target, Is.EqualTo(CheckpointTestRecords.TargetIdOf(2)));
            Assert.That(decoded.Capability, Is.EqualTo(CheckpointTestRecords.CapabilityIdOf(3)));
            Assert.That(decoded.Provider, Is.EqualTo(CheckpointTestRecords.ProviderIdOf(4)));
            Assert.That(decoded.Rule, Is.EqualTo(CheckpointTestRecords.RuleIdOf(5)));
            Assert.That(decoded.Contract, Is.EqualTo(new ContractRef(CheckpointTestRecords.Id(6), 3U)));
            Assert.That(decoded.Subject, Is.EqualTo(CheckpointTestRecords.Id(7)));
            Assert.That(decoded.Exclusion, Is.EqualTo(ExclusionTargetKind.Rule));
            Assert.That(decoded.AppliesToSubtree, Is.True);
            Assert.That(decoded.AllContracts, Is.False);
            Assert.That(decoded.Order, Is.EqualTo(4U));
        }

        [Test]
        public void ClockDeclarationRoundTripsItsDeclarationFields()
        {
            ClockRecordValue decoded = RoundTrip(
                CheckpointRecordKind.Clock, CheckpointTestRecords.SampleClockDeclaration(1));

            Assert.That(decoded.IsDeclaration, Is.True);
            Assert.That(decoded.Row, Is.EqualTo(ClockRowKind.Declaration));
            Assert.That(decoded.ClockId, Is.EqualTo(CheckpointTestRecords.Id(401)));
            Assert.That(decoded.DeclaredClockKind, Is.EqualTo(2U));
            Assert.That(decoded.DeclaredPausePolicy, Is.EqualTo(3U));
            Assert.That(decoded.Persists, Is.True);
            Assert.That(decoded.Order, Is.EqualTo(1U));
        }

        [Test]
        public void ClockWakeRoundTripsItsWakeIdentityAndPayloadSchema()
        {
            ClockRecordValue decoded = RoundTrip(
                CheckpointRecordKind.Clock, CheckpointTestRecords.SampleClockWake(1));

            Assert.That(decoded.IsDeclaration, Is.False);
            Assert.That(decoded.Row, Is.EqualTo(ClockRowKind.Wake));
            Assert.That(decoded.ClockId, Is.EqualTo(CheckpointTestRecords.Id(401)));
            Assert.That(decoded.WakeId, Is.EqualTo(CheckpointTestRecords.Id(402)));
            Assert.That(
                decoded.PayloadSchema,
                Is.EqualTo(new SchemaRef(CheckpointTestRecords.SchemaIdOf(403), 4U)));
            Assert.That(decoded.ScheduledAtSequence, Is.EqualTo(7UL));
            Assert.That(decoded.RemainingSteps, Is.EqualTo(8UL));
            Assert.That(decoded.RemainingTicks, Is.EqualTo(9UL));
            Assert.That(decoded.State, Is.EqualTo(ClockWakeState.Due));
            Assert.That(decoded.Order, Is.EqualTo(2U));
        }

        [Test]
        public void CommandRecordRoundTripsAndStampsTheSuppliedWorldIntoTheRequestId()
        {
            CommandRecordValue command = CheckpointTestRecords.SampleCommand(5, 1);
            CommandRecordValue decoded = RoundTrip(CheckpointRecordKind.Command, command);

            WorldId restoredWorld = CheckpointTestRecords.WorldOf(99);
            Assert.That(decoded.Route, Is.EqualTo(CheckpointTestRecords.RouteIdOf(206)));
            Assert.That(decoded.Target, Is.EqualTo(CheckpointTestRecords.TargetIdOf(1)));
            Assert.That(
                decoded.Schema,
                Is.EqualTo(new SchemaRef(CheckpointTestRecords.SchemaIdOf(207), 2U)));
            Assert.That(decoded.IssuerId, Is.EqualTo(CheckpointTestRecords.Id(205)));
            Assert.That(decoded.IssuerSequence, Is.EqualTo(5UL));
            Assert.That(
                decoded.RequestIdIn(restoredWorld),
                Is.EqualTo(new OperationId(restoredWorld, CheckpointTestRecords.Id(205), 5UL)));
            Assert.That(
                decoded.RequestIdIn(restoredWorld),
                Is.Not.EqualTo(decoded.RequestIdIn(CheckpointTestRecords.WorldOf(100))));
            Assert.That(decoded.AdmittedStep, Is.EqualTo(6UL));
            Assert.That(decoded.AdmittedEpoch, Is.EqualTo(7UL));
            Assert.That(decoded.AdmissionSequence, Is.EqualTo(8UL));
            Assert.That(decoded.OrderOrdinal, Is.EqualTo(5U));
            Assert.That(decoded.OriginKind, Is.EqualTo(6U));
            Assert.That(decoded.InputHash, Is.EqualTo(command.InputHash));
            Assert.That(decoded.Payload, Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(decoded.Frozen.Bytes, Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(decoded.Frozen.Length, Is.EqualTo(3));
        }

        [Test]
        public void MessageRecordRoundTripsProducerBufferAndRequestTarget()
        {
            MessageRecordValue decoded = RoundTrip(
                CheckpointRecordKind.Message, CheckpointTestRecords.SampleMessage(5, 1, 2));

            Assert.That(decoded.Step, Is.EqualTo(6UL));
            Assert.That(decoded.Epoch, Is.EqualTo(7UL));
            Assert.That(decoded.Route, Is.EqualTo(CheckpointTestRecords.RouteIdOf(306)));
            Assert.That(decoded.Owner, Is.EqualTo(CheckpointTestRecords.OwnerIdOf(307)));
            Assert.That(decoded.Target, Is.EqualTo(CheckpointTestRecords.TargetIdOf(2)));
            Assert.That(decoded.Buffer, Is.EqualTo(CheckpointTestRecords.BufferIdOf(1)));
            Assert.That(
                decoded.PayloadSchema,
                Is.EqualTo(new SchemaRef(CheckpointTestRecords.SchemaIdOf(308), 3U)));
            Assert.That(decoded.Producer, Is.EqualTo(new FactoryKey(CheckpointTestRecords.Id(310), 7U)));
            Assert.That(decoded.OriginKey, Is.EqualTo(CheckpointTestRecords.Id(309)));
            Assert.That(decoded.HasPayload, Is.True);
            Assert.That(decoded.HasRequest, Is.True);
            Assert.That(decoded.IsOutcome, Is.False);
            Assert.That(decoded.Payload, Is.EqualTo(new byte[] { 9, 8, 7 }));
        }

        [Test]
        public void MessageRecordWithAnAbsentPayloadRoundTripsAsTheDeclaredNoneState()
        {
            var message = new MessageRecordValue(
                1UL,
                2UL,
                0UL,
                0UL,
                0UL,
                0UL,
                0UL,
                0UL,
                0UL,
                0UL,
                0UL,
                0UL,
                0UL,
                0U,
                0U,
                0UL,
                0U,
                0UL,
                0UL,
                0UL,
                0UL,
                0U,
                0UL,
                0UL,
                false,
                false,
                false,
                null);
            MessageRecordValue decoded = RoundTrip(CheckpointRecordKind.Message, message);

            Assert.That(decoded.HasPayload, Is.False);
            Assert.That(decoded.HasRequest, Is.False);
            Assert.That(decoded.IsOutcome, Is.False);
            Assert.That(decoded.Payload, Is.Null);
            Assert.That(decoded.Producer.RegistrationKey.IsDefault, Is.True);
        }

        [Test]
        public void RngRecordRoundTripsItsStreamIdentityStateAndDrawCount()
        {
            RngRecordValue decoded = RoundTrip(
                CheckpointRecordKind.Rng, CheckpointTestRecords.SampleRng(5));

            Assert.That(decoded.StreamId, Is.EqualTo(CheckpointTestRecords.Id(705)));
            Assert.That(decoded.State, Is.EqualTo(6UL));
            Assert.That(decoded.StreamKey, Is.EqualTo(7UL));
            Assert.That(decoded.DrawCount, Is.EqualTo(8UL));
        }

        [Test]
        public void CursorRecordRoundTripsTheEventAndAdmissionSequenceViews()
        {
            CursorRecordValue eventCursor = RoundTrip(
                CheckpointRecordKind.Cursor,
                CheckpointTestRecords.SampleCursor((uint)CursorRowKind.EventCursor, 5));
            CursorRecordValue highWater = RoundTrip(
                CheckpointRecordKind.Cursor,
                CheckpointTestRecords.SampleCursor((uint)CursorRowKind.IssuerHighWater, 5));

            Assert.That(eventCursor.Row, Is.EqualTo(CursorRowKind.EventCursor));
            Assert.That(eventCursor.IssuerId, Is.EqualTo(CheckpointTestRecords.Id(605)));
            Assert.That(eventCursor.SourceSession, Is.EqualTo(CheckpointTestRecords.WorldOf(606)));
            Assert.That(eventCursor.Event, Is.EqualTo(new EventSequence(14UL)));
            Assert.That(eventCursor.Admission, Is.EqualTo(new AdmissionSequence(14UL)));
            Assert.That(eventCursor.Sequence, Is.EqualTo(14UL));
            Assert.That(highWater.Row, Is.EqualTo(CursorRowKind.IssuerHighWater));
            Assert.That(highWater.Event.Value, Is.EqualTo(14UL));
        }
    }

    /// <summary>
    /// The format's own helpers and constants: the field id of each kind, the stable name of each kind's schema,
    /// the collation of a 32-byte identity and the mapping from envelope errors to protocol codes
    /// (P-008, P-052, P-054, P-055, 05 s6).
    /// </summary>
    [TestFixture]
    public sealed class CheckpointFormatTests
    {
        [Test]
        public void FormatConstantsAndLimitsAreTheDocumentedBounds()
        {
            Assert.That(CheckpointFormat.FormatName, Is.EqualTo("gamecore.checkpoint/1"));
            Assert.That(CheckpointFormat.ProtocolMajor, Is.EqualTo(1));
            Assert.That(CheckpointFormat.ProtocolMinor, Is.EqualTo(0));
            Assert.That(CheckpointFormat.RecordKindCount, Is.EqualTo(12));
            Assert.That(CheckpointFormat.HeaderFieldId, Is.EqualTo(1));
            Assert.That(CheckpointFormat.FirstRecordFieldId, Is.EqualTo(100));
            Assert.That(CheckpointFormat.MaxDocumentBytes, Is.EqualTo(64 * 1024 * 1024));
            Assert.That(CheckpointFormat.MaxRecordBytes, Is.EqualTo(4 * 1024 * 1024));
            Assert.That(CheckpointFormat.MaxRecordCount, Is.EqualTo(1 << 20));
            Assert.That(CheckpointFormat.Limits.MaxDocumentBytes, Is.EqualTo(CheckpointFormat.MaxDocumentBytes));
            Assert.That(CheckpointFormat.Limits.MaxFieldBytes, Is.EqualTo(CheckpointFormat.MaxRecordBytes));
            Assert.That(CheckpointFormat.Limits.MaxListCount, Is.EqualTo(CheckpointFormat.MaxRecordCount));
            Assert.That(CheckpointFormat.Limits.MaxListBytes, Is.EqualTo(16 * 1024 * 1024));
            Assert.That(CheckpointFormat.Limits.MaxStringBytes, Is.EqualTo(4096));
            Assert.That(CheckpointFormat.Describe(), Does.Contain("records=12"));
        }

        [Test]
        public void KnownFeatureIdsHoldExactlyTheDerivedRequiredFeature()
        {
            Assert.That(CheckpointFormat.KnownFeatureIds.Count, Is.EqualTo(1));
            Assert.That(
                CheckpointFormat.KnownFeatureIds[0],
                Is.EqualTo(StableNameKeyDerivation.Derive(CheckpointFormat.FeatureStableName)));
            Assert.That(CheckpointFormat.KnownFeatureIds[0], Is.EqualTo(CheckpointFormat.RequiredFeatureId));
        }

        [Test]
        public void DocumentSchemaIsTheDerivedNameAtVersionOne()
        {
            Assert.That(
                CheckpointFormat.DocumentSchema.Id,
                Is.EqualTo(new SchemaId(
                    StableNameKeyDerivation.Derive(CheckpointFormat.DocumentSchemaStableName))));
            Assert.That(CheckpointFormat.DocumentSchema.Version, Is.EqualTo(1U));
        }

        [Test]
        public void TryKindOfFieldAcceptsExactlyTheTwelveRecordFieldIds()
        {
            for (int ordinal = 0; ordinal < CheckpointFormat.RecordKindCount; ordinal++)
            {
                int fieldId = CheckpointFormat.FirstRecordFieldId + ordinal;
                Assert.That(
                    CheckpointFormat.TryKindOfField(fieldId, out CheckpointRecordKind kind),
                    Is.True,
                    "field " + fieldId + " names a declared record kind");
                Assert.That(kind, Is.EqualTo((CheckpointRecordKind)ordinal));
                Assert.That(CheckpointFormat.FieldIdOf(kind), Is.EqualTo(fieldId));
                Assert.That(CheckpointFormat.IsDeclared(kind), Is.True);
            }

            Assert.That(CheckpointFormat.TryKindOfField(0, out CheckpointRecordKind _), Is.False);
            Assert.That(CheckpointFormat.TryKindOfField(1, out CheckpointRecordKind _), Is.False);
            Assert.That(
                CheckpointFormat.TryKindOfField(CheckpointFormat.FirstRecordFieldId - 1, out CheckpointRecordKind _),
                Is.False);
            Assert.That(CheckpointFormat.TryKindOfField(99, out CheckpointRecordKind _), Is.False);
            Assert.That(
                CheckpointFormat.TryKindOfField(
                    CheckpointFormat.FirstRecordFieldId + CheckpointFormat.RecordKindCount,
                    out CheckpointRecordKind _),
                Is.False);
            Assert.That(
                CheckpointFormat.IsDeclared((CheckpointRecordKind)CheckpointFormat.RecordKindCount),
                Is.False);
            Assert.That(CheckpointFormat.IsDeclared((CheckpointRecordKind)(-1)), Is.False);
        }

        [TestCase(CheckpointRecordKind.Header, "gamecore.checkpoint.schema.header")]
        [TestCase(CheckpointRecordKind.Scope, "gamecore.checkpoint.schema.scope")]
        [TestCase(CheckpointRecordKind.Install, "gamecore.checkpoint.schema.install")]
        [TestCase(CheckpointRecordKind.Selection, "gamecore.checkpoint.schema.selection")]
        [TestCase(CheckpointRecordKind.Target, "gamecore.checkpoint.schema.target")]
        [TestCase(CheckpointRecordKind.Slot, "gamecore.checkpoint.schema.slot")]
        [TestCase(CheckpointRecordKind.Grant, "gamecore.checkpoint.schema.grant")]
        [TestCase(CheckpointRecordKind.Clock, "gamecore.checkpoint.schema.clock")]
        [TestCase(CheckpointRecordKind.Command, "gamecore.checkpoint.schema.command")]
        [TestCase(CheckpointRecordKind.Message, "gamecore.checkpoint.schema.message")]
        [TestCase(CheckpointRecordKind.Rng, "gamecore.checkpoint.schema.rng")]
        [TestCase(CheckpointRecordKind.Cursor, "gamecore.checkpoint.schema.cursor")]
        public void SchemaStableNameOfNamesEachKind(CheckpointRecordKind kind, string expected)
        {
            Assert.That(CheckpointFormat.SchemaStableNameOf(kind), Is.EqualTo(expected));
            Assert.That(
                StableNameKeyDerivation.IsCanonicalStableName(expected),
                Is.True,
                "the stable name the format publishes must be canonical");
            Assert.That(
                CheckpointTestRecords.SchemaOf(kind).Id,
                Is.EqualTo(new SchemaId(StableNameKeyDerivation.Derive(expected))));
        }

        [Test]
        public void SchemaStableNameOfRejectsAnUndeclaredOrdinal()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CheckpointFormat.SchemaStableNameOf((CheckpointRecordKind)CheckpointFormat.RecordKindCount));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CheckpointFormat.SchemaStableNameOf((CheckpointRecordKind)(-1)));
        }

        [Test]
        public void CountsOfNamesEachKindAndRejectsAnUndeclaredOne()
        {
            var counts = new CheckpointCounts(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);

            Assert.That(counts.Total, Is.EqualTo(1 + 66));
            Assert.That(counts.Of(CheckpointRecordKind.Header), Is.EqualTo(1));
            Assert.That(counts.Of(CheckpointRecordKind.Scope), Is.EqualTo(1));
            Assert.That(counts.Of(CheckpointRecordKind.Install), Is.EqualTo(2));
            Assert.That(counts.Of(CheckpointRecordKind.Selection), Is.EqualTo(3));
            Assert.That(counts.Of(CheckpointRecordKind.Target), Is.EqualTo(4));
            Assert.That(counts.Of(CheckpointRecordKind.Slot), Is.EqualTo(5));
            Assert.That(counts.Of(CheckpointRecordKind.Grant), Is.EqualTo(6));
            Assert.That(counts.Of(CheckpointRecordKind.Clock), Is.EqualTo(7));
            Assert.That(counts.Of(CheckpointRecordKind.Command), Is.EqualTo(8));
            Assert.That(counts.Of(CheckpointRecordKind.Message), Is.EqualTo(9));
            Assert.That(counts.Of(CheckpointRecordKind.Rng), Is.EqualTo(10));
            Assert.That(counts.Of(CheckpointRecordKind.Cursor), Is.EqualTo(11));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => counts.Of((CheckpointRecordKind)CheckpointFormat.RecordKindCount));
            Assert.That(counts.ToString(), Does.Contain("target=4"));
        }

        [Test]
        public void CanonicalId32CollationIsBigEndianAndOrderSensitive()
        {
            var header = CheckpointTestRecords.Header(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            byte[] fingerprint = header.CatalogFingerprint.ToArray();

            Assert.That(fingerprint.Length, Is.EqualTo(ContentHash.SizeInBytes));
            Assert.That(
                fingerprint,
                Is.EqualTo(new byte[]
                {
                    0x77, 0x77, 0x77, 0x77, 0x77, 0x77, 0x77, 0x77,
                    0x88, 0x88, 0x88, 0x88, 0x88, 0x88, 0x88, 0x88,
                    0x99, 0x99, 0x99, 0x99, 0x99, 0x99, 0x99, 0x99,
                    0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA,
                }));
            Assert.That(header.CatalogFingerprint.IsEmpty, Is.False);
            Assert.That(header.CatalogFingerprint.ToHex().Length, Is.EqualTo(64));

            // Swapping the first two words must change the hash: collation is order-sensitive, not a set.
            InstallRecordValue install = CheckpointTestRecords.SampleInstall(1, 2);
            ContentHash configHash = install.ConfigHash;
            var swapped = new InstallRecordValue(
                install.InstanceHigh,
                install.InstanceLow,
                install.PluginTypeHigh,
                install.PluginTypeLow,
                install.ScopeHigh,
                install.ScopeLow,
                install.ConfigRevision,
                install.ConfigHashB,
                install.ConfigHashA,
                install.ConfigHashC,
                install.ConfigHashD,
                install.Priority,
                install.Generation,
                install.ActivationEpoch,
                install.State,
                install.ConfigFieldCount,
                install.ConfigBytes,
                install.SelectionCount,
                install.HasConfigDocument);
            Assert.That(swapped.ConfigHash, Is.Not.EqualTo(configHash));
            Assert.That(
                SwapFirstTwoBytes(configHash.ToArray()),
                Is.EqualTo(swapped.ConfigHash.ToArray()),
                "swapping the two words swaps their big-endian byte runs");

            CommandRecordValue command = CheckpointTestRecords.SampleCommand(5, 1);
            byte[] inputHash = command.InputHash.ToArray();
            Assert.That(inputHash.Length, Is.EqualTo(ContentHash.SizeInBytes));
            Assert.That(
                command.InputHash,
                Is.Not.EqualTo(CheckpointTestRecords.SampleCommand(6, 1).InputHash));
        }

        [Test]
        public void GrantRecordProjectsItsImportAndExclusionViews()
        {
            GrantRecordValue import = CheckpointTestRecords.SampleGrant(
                (uint)GrantKind.ScopeImport, 1, 2, 3);
            GrantRecordValue exclusion = CheckpointTestRecords.SampleGrant(
                (uint)GrantKind.Exclusion, 4, 5, 6);

            Assert.That(import.Grant, Is.EqualTo(GrantKind.ScopeImport));
            Assert.That(
                import.ToImport(),
                Is.EqualTo(new CapabilityImport(import.Capability, import.Provider)));
            Assert.That(
                import.ToImport().CapabilityId,
                Is.EqualTo(CheckpointTestRecords.CapabilityIdOf(3)));
            Assert.That(
                import.ToImport().ProviderInstallationId,
                Is.EqualTo(CheckpointTestRecords.ProviderIdOf(4)));

            Assert.That(exclusion.Grant, Is.EqualTo(GrantKind.Exclusion));
            ExclusionRule rule = exclusion.ToExclusion();
            Assert.That(rule.Kind, Is.EqualTo(ExclusionTargetKind.Rule));
            Assert.That(rule.TargetId, Is.EqualTo(CheckpointTestRecords.Id(10)));
            Assert.That(rule.AtScope, Is.EqualTo(CheckpointTestRecords.ScopeIdOf(4)));
            Assert.That(rule.AtTarget, Is.EqualTo(CheckpointTestRecords.TargetIdOf(5)));
            Assert.That(rule.AppliesToSubtree, Is.True);
            Assert.That(rule.HasScope, Is.True);
            Assert.That(rule.HasTarget, Is.True);
        }

        [Test]
        public void CheckpointErrorsMapsEveryEnvelopeErrorToARealDiagnosticCode()
        {
            EnvelopeError[] errors =
            {
                EnvelopeError.None,
                EnvelopeError.Truncated,
                EnvelopeError.BadMagic,
                EnvelopeError.UnsupportedVersion,
                EnvelopeError.UnknownWireType,
                EnvelopeError.FieldLengthMismatch,
                EnvelopeError.StringTooLong,
                EnvelopeError.FieldTooLong,
                EnvelopeError.ListCountExceeded,
                EnvelopeError.ListTooLarge,
                EnvelopeError.DocumentTooLarge,
                EnvelopeError.InvalidUtf8,
                EnvelopeError.ChecksumMismatch,
                EnvelopeError.InvalidBooleanValue,
                EnvelopeError.FieldIdOutOfRange,
                EnvelopeError.UnknownRequiredFeature,
                EnvelopeError.MissingRequiredField,
                EnvelopeError.DuplicateField,
            };

            for (int i = 0; i < errors.Length; i++)
            {
                DiagnosticCode code = CheckpointErrors.CodeFor(errors[i]);
                // CodeFor must name a code the protocol enumerates: a code without a literal would throw here.
                string literal = DiagnosticCodeText.Of(code);
                Assert.That(literal, Is.Not.Null.And.Not.Empty);
                Assert.That(CheckpointErrors.TextOf(code), Is.EqualTo(literal));
            }

            Assert.That(CheckpointErrors.CodeFor(EnvelopeError.None), Is.EqualTo(DiagnosticCode.None));
            Assert.That(
                CheckpointErrors.CodeFor(EnvelopeError.BadMagic),
                Is.EqualTo(DiagnosticCode.UnsupportedVersion));
            Assert.That(
                CheckpointErrors.CodeFor(EnvelopeError.UnknownRequiredFeature),
                Is.EqualTo(DiagnosticCode.UnsupportedVersion));
            Assert.That(
                CheckpointErrors.CodeFor(EnvelopeError.FieldIdOutOfRange),
                Is.EqualTo(DiagnosticCode.UnsupportedVersion));
            Assert.That(
                CheckpointErrors.CodeFor(EnvelopeError.Truncated),
                Is.EqualTo(DiagnosticCode.MissingDependency));
            Assert.That(
                CheckpointErrors.CodeFor(EnvelopeError.MissingRequiredField),
                Is.EqualTo(DiagnosticCode.MissingDependency));
            Assert.That(
                CheckpointErrors.CodeFor(EnvelopeError.DuplicateField),
                Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(
                CheckpointErrors.CodeFor(EnvelopeError.DocumentTooLarge),
                Is.EqualTo(DiagnosticCode.BudgetExceeded));
            Assert.That(
                CheckpointErrors.CodeFor(EnvelopeError.FieldTooLong),
                Is.EqualTo(DiagnosticCode.BudgetExceeded));
            Assert.That(
                CheckpointErrors.CodeFor(EnvelopeError.ChecksumMismatch),
                Is.EqualTo(DiagnosticCode.ResourceUnavailable));
            Assert.That(
                CheckpointErrors.CodeFor(EnvelopeError.FieldLengthMismatch),
                Is.EqualTo(DiagnosticCode.ResourceUnavailable));
        }

        [Test]
        public void RecordCodecRejectsTrailingBytesAndAnotherSchema()
        {
            ICheckpointRecordCodec<TargetRecordValue> codec = CheckpointTestRecords.TargetCodec();
            byte[] document = codec.Encode(CheckpointTestRecords.SampleTarget(1, 2));

            Assert.That(codec.TryValidate(document, out EnvelopeError error), Is.True, error.ToString());

            byte[] extended = new byte[document.Length + 1];
            Buffer.BlockCopy(document, 0, extended, 0, document.Length);
            Assert.That(codec.TryValidate(extended, out EnvelopeError trailingError), Is.False);
            Assert.That(trailingError, Is.EqualTo(EnvelopeError.FieldLengthMismatch));

            var foreign = new EnvelopeWriter(
                new EnvelopeHeader(
                    1,
                    0,
                    new SchemaRef(CheckpointTestRecords.SchemaIdOf(5000), 1U),
                    CheckpointFormat.KnownFeatureIds));
            foreign.WriteUInt64Field(1, 1UL);
            foreign.WriteChecksum();
            Assert.That(codec.TryValidate(foreign.ToArray(), out EnvelopeError schemaError), Is.False);
            Assert.That(schemaError, Is.EqualTo(EnvelopeError.UnsupportedVersion));
        }

        private static byte[] SwapFirstTwoBytes(byte[] source)
        {
            var result = new byte[source.Length];
            Buffer.BlockCopy(source, 0, result, 0, source.Length);
            Buffer.BlockCopy(source, 8, result, 0, 8);
            Buffer.BlockCopy(source, 0, result, 8, 8);
            return result;
        }
    }
}

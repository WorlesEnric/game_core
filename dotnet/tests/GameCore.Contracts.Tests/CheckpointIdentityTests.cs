// Checkpoint identity table and reference validation tests (GC-018). Normative sources: P-004 (a persisted
// reference is a stable 128-bit identity, and the all-zero identity is never a catalog identity), P-005 (a
// checkpoint carries no runtime handle), P-010/P-011/P-016/P-032/P-037/P-043 (the structural references a restore
// must resolve) and 05 s6 ("missing required targets reject; optional references become the declared None state").
//
// Every test builds its own table from record values, so the suite is order-independent.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using NUnit.Framework;

namespace GameCore.Contracts.Tests
{
    /// <summary>The first pass of a restore: which identities a document declares, and which references resolve.</summary>
    [TestFixture]
    public sealed class CheckpointIdentityTableTests
    {
        private static readonly WorldId TableWorld =
            new WorldId(new Id128(0x7100000000000001UL, 0x7100000000000002UL));

        private static readonly OperationId Operation =
            new OperationId(TableWorld, new Id128(0x7200000000000001UL, 0x7200000000000002UL), 9UL);

        private static CheckpointIdentityTable Table(
            IReadOnlyList<ScopeRecordValue>? scopes,
            IReadOnlyList<InstallRecordValue>? installs,
            IReadOnlyList<TargetRecordValue>? targets,
            IReadOnlyList<SlotRecordValue>? slots,
            IReadOnlyList<ClockRecordValue>? clocks,
            IReadOnlyList<MessageRecordValue>? messages,
            IReadOnlyList<RngRecordValue>? rngStreams) =>
            CheckpointIdentityTable.Build(
                TableWorld,
                scopes,
                installs,
                targets,
                slots,
                clocks,
                messages,
                rngStreams,
                Operation);

        private static CheckpointIdentityTable Empty() =>
            Table(null, null, null, null, null, null, null);

        private static bool HasDiagnostic(CheckpointIdentityTable table, DiagnosticCode code, string fragment)
        {
            for (int i = 0; i < table.Diagnostics.Count; i++)
            {
                Diagnostic diagnostic = table.Diagnostics[i];
                if (diagnostic.Code == code && diagnostic.Summary.Contains(fragment, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        [Test]
        public void AnEmptyDocumentDeclaresNothingAndToleratesAbsentInputs()
        {
            CheckpointIdentityTable table = Empty();

            Assert.That(table.World, Is.EqualTo(TableWorld));
            Assert.That(table.ScopeCount, Is.EqualTo(0));
            Assert.That(table.InstallCount, Is.EqualTo(0));
            Assert.That(table.TargetCount, Is.EqualTo(0));
            Assert.That(table.OwnerCount, Is.EqualTo(0));
            Assert.That(table.SlotCount, Is.EqualTo(0));
            Assert.That(table.Diagnostics, Is.Empty);
            Assert.That(table.ScopeIds, Is.Empty);
            Assert.That(table.InstallIds, Is.Empty);
            Assert.That(table.TargetIds, Is.Empty);
            Assert.That(table.HasScope(CheckpointTestRecords.ScopeIdOf(1)), Is.False);
            Assert.That(table.HasTarget(CheckpointTestRecords.TargetIdOf(1)), Is.False);

            // Every list is optional, so a caller that read only some categories validates against what it built.
            Assert.That(
                table.ValidateReferences(null, null, null, null, null, null, null, null, null),
                Is.True);
            Assert.That(table.Diagnostics, Is.Empty);
        }

        [Test]
        public void ASecondIdentityInOneCategoryIsRejectedAndTheFirstStaysAuthoritative()
        {
            TargetRecordValue first = CheckpointTestRecords.SampleTarget(1, 1);
            TargetRecordValue duplicate = CheckpointTestRecords.SampleTarget(1, 2);
            Assert.That(duplicate.Target, Is.EqualTo(first.Target), "the fixture shares one target identity");
            Assert.That(duplicate.Scope, Is.Not.EqualTo(first.Scope), "the two records are otherwise different");

            CheckpointIdentityTable table = Table(null, null, new[] { first, duplicate }, null, null, null, null);

            Assert.That(table.TargetCount, Is.EqualTo(1), "one world rejects duplicate live stable identities (P-004)");
            Assert.That(table.Diagnostics.Count, Is.EqualTo(1));
            Assert.That(table.Diagnostics[0].Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(table.Diagnostics[0].Phase, Is.EqualTo(OperationPhase.Validation));
            Assert.That(table.Diagnostics[0].Operation, Is.EqualTo(Operation));
            Assert.That(
                HasDiagnostic(table, DiagnosticCode.OwnershipConflict, "more than once"),
                Is.True,
                table.Diagnostics[0].Summary);
            Assert.That(table.HasTarget(first.Target), Is.True);
            Assert.That(table.TargetIds.Count, Is.EqualTo(1));
            Assert.That(table.TargetIds[0], Is.EqualTo(first.Target.Value));
        }

        [Test]
        public void AnAllZeroIdentityIsRejectedWithADiagnosticAndNeverIndexed()
        {
            var zeroScope = new ScopeRecordValue(0UL, 0UL, 0UL, 0UL, 0U, 0U, 0U, 0U, false, false, 0U, 0U);
            var zeroStream = new RngRecordValue(0UL, 0UL, 1UL, 2UL, 3UL);

            CheckpointIdentityTable table = Table(
                new[] { zeroScope },
                null,
                null,
                null,
                null,
                null,
                new[] { zeroStream });

            Assert.That(table.ScopeCount, Is.EqualTo(0), "the all-zero identity is never a catalog identity (P-004)");
            Assert.That(table.ScopeIds, Is.Empty);
            Assert.That(table.HasScope(new ScopeId(Id128.Zero)), Is.False);
            Assert.That(table.HasRngStream(Id128.Zero), Is.False);
            Assert.That(table.Diagnostics.Count, Is.EqualTo(2));
            for (int i = 0; i < table.Diagnostics.Count; i++)
            {
                Assert.That(table.Diagnostics[i].Code, Is.EqualTo(DiagnosticCode.MissingDependency));
                Assert.That(table.Diagnostics[i].Summary, Does.Contain("all-zero identity"));
            }

            Assert.That(
                table.ResolveRequired(Id128.Zero, IdentityCategory.Scope),
                Is.EqualTo(ReferenceResolution.Undeclared));
        }

        [Test]
        public void DeclaredIdentitiesAreExposedInCanonicalOrder()
        {
            CheckpointIdentityTable table = Table(
                null,
                null,
                new[]
                {
                    CheckpointTestRecords.SampleTarget(3, 1),
                    CheckpointTestRecords.SampleTarget(1, 1),
                    CheckpointTestRecords.SampleTarget(2, 1),
                },
                null,
                null,
                null,
                null);

            Assert.That(table.TargetCount, Is.EqualTo(3));
            Assert.That(table.Diagnostics, Is.Empty);
            Assert.That(
                table.TargetIds,
                Is.EqualTo(new[]
                {
                    CheckpointTestRecords.Id(1),
                    CheckpointTestRecords.Id(2),
                    CheckpointTestRecords.Id(3),
                }));
        }

        [Test]
        public void SlotsDeclareTheirOwnerAndSlotIdentities()
        {
            SlotRecordValue slot = CheckpointTestRecords.SampleSlot(1, 2, 3);
            CheckpointIdentityTable table = Table(
                null,
                null,
                new[] { CheckpointTestRecords.SampleTarget(1, 1) },
                new[] { slot },
                null,
                null,
                null);

            Assert.That(table.SlotCount, Is.EqualTo(1));
            Assert.That(table.OwnerCount, Is.EqualTo(1));
            Assert.That(table.Diagnostics, Is.Empty);
            Assert.That(table.HasSlot(CheckpointTestRecords.SlotIdOf(3)), Is.True);
            Assert.That(table.HasOwner(CheckpointTestRecords.OwnerIdOf(2)), Is.True);
            Assert.That(table.HasOwner(CheckpointTestRecords.OwnerIdOf(9)), Is.False);
            Assert.That(
                table.ResolveRequired(slot.Key.Owner.Value, IdentityCategory.Owner),
                Is.EqualTo(ReferenceResolution.Resolved));
            Assert.That(
                table.ResolveRequired(slot.Key.Slot.Value, IdentityCategory.Slot),
                Is.EqualTo(ReferenceResolution.Resolved));
        }

        [Test]
        public void ClockDeclarationsAreIndexedButWakeRowsAreNot()
        {
            ClockRecordValue declaration = CheckpointTestRecords.SampleClockDeclaration(1);
            ClockRecordValue wake = CheckpointTestRecords.SampleClockWake(1);

            CheckpointIdentityTable table = Table(
                null,
                null,
                null,
                null,
                new[] { declaration, wake },
                null,
                null);

            Assert.That(table.Diagnostics, Is.Empty);
            Assert.That(table.HasClock(declaration.ClockId), Is.True);
            Assert.That(
                table.ResolveRequired(declaration.ClockId, IdentityCategory.Clock),
                Is.EqualTo(ReferenceResolution.Resolved));

            // A wake names a clock and a wake identity, but only the declaration declares the clock category.
            CheckpointIdentityTable wakeOnly = Table(
                null,
                null,
                null,
                null,
                new[] { wake },
                null,
                null);
            Assert.That(wakeOnly.Diagnostics, Is.Empty);
            Assert.That(wakeOnly.HasClock(wake.ClockId), Is.False);
        }

        [Test]
        public void ResolveRequiredDistinguishesResolvedMissingAndUndeclared()
        {
            CheckpointIdentityTable table = Table(
                new[] { CheckpointTestRecords.SampleRootScope(1) },
                null,
                new[] { CheckpointTestRecords.SampleTarget(1, 1) },
                null,
                null,
                null,
                new[] { CheckpointTestRecords.SampleRng(5) });

            Assert.That(
                table.ResolveRequired(CheckpointTestRecords.Id(1), IdentityCategory.Scope),
                Is.EqualTo(ReferenceResolution.Resolved));
            Assert.That(
                table.ResolveRequired(CheckpointTestRecords.Id(42), IdentityCategory.Scope),
                Is.EqualTo(ReferenceResolution.Missing));
            Assert.That(
                table.ResolveRequired(Id128.Zero, IdentityCategory.Scope),
                Is.EqualTo(ReferenceResolution.Undeclared));
            Assert.That(
                table.ResolveRequired(CheckpointTestRecords.Id(1), IdentityCategory.Target),
                Is.EqualTo(ReferenceResolution.Resolved));
            Assert.That(
                table.ResolveRequired(CheckpointTestRecords.Id(3), IdentityCategory.Target),
                Is.EqualTo(ReferenceResolution.Missing));
            Assert.That(
                table.ResolveRequired(CheckpointTestRecords.Id(705), IdentityCategory.RngStream),
                Is.EqualTo(ReferenceResolution.Resolved));
            Assert.That(
                table.ResolveRequired(Id128.Zero, IdentityCategory.RngStream),
                Is.EqualTo(ReferenceResolution.Undeclared));
            Assert.That(
                table.ResolveRequired(CheckpointTestRecords.Id(1), IdentityCategory.Install),
                Is.EqualTo(ReferenceResolution.Missing));
            Assert.That(
                table.ResolveRequired(CheckpointTestRecords.Id(1), IdentityCategory.Buffer),
                Is.EqualTo(ReferenceResolution.Missing));
        }

        [Test]
        public void ANonRootScopeWithAnUndeclaredParentIsRejected()
        {
            ScopeRecordValue child = CheckpointTestRecords.SampleScope(1, 2, 1);
            CheckpointIdentityTable table = Table(new[] { child }, null, null, null, null, null, null);

            Assert.That(table.ScopeCount, Is.EqualTo(1));
            Assert.That(
                table.ValidateReferences(
                    new[] { child }, null, null, null, null, null, null, null, null),
                Is.False);
            Assert.That(table.Diagnostics.Count, Is.EqualTo(1));
            Assert.That(table.Diagnostics[0].Code, Is.EqualTo(DiagnosticCode.MissingDependency));
            Assert.That(HasDiagnostic(table, DiagnosticCode.MissingDependency, "parent"), Is.True);
        }

        [Test]
        public void ARootScopeIsAcceptedWithoutAParent()
        {
            ScopeRecordValue root = CheckpointTestRecords.SampleRootScope(1);
            CheckpointIdentityTable table = Table(new[] { root }, null, null, null, null, null, null);

            Assert.That(root.IsRoot, Is.True);
            Assert.That(
                table.ValidateReferences(new[] { root }, null, null, null, null, null, null, null, null),
                Is.True);
            Assert.That(table.Diagnostics, Is.Empty);
        }

        [Test]
        public void ATargetNamingAnUndeclaredScopeIsRejected()
        {
            TargetRecordValue target = CheckpointTestRecords.SampleTarget(1, 1);
            CheckpointIdentityTable table = Table(null, null, new[] { target }, null, null, null, null);

            Assert.That(
                table.ValidateReferences(null, null, new[] { target }, null, null, null, null, null, null),
                Is.False);
            Assert.That(table.Diagnostics.Count, Is.EqualTo(1));
            Assert.That(HasDiagnostic(table, DiagnosticCode.MissingDependency, "owner scope"), Is.True);
        }

        [Test]
        public void ATargetWithAnAllZeroRecipeIsRejected()
        {
            var noRecipe = new TargetRecordValue(
                CheckpointTestRecords.Id(1).High,
                CheckpointTestRecords.Id(1).Low,
                CheckpointTestRecords.Id(2).High,
                CheckpointTestRecords.Id(2).Low,
                0UL,
                0UL,
                0UL,
                0UL,
                1U,
                1UL,
                0U,
                0UL);
            CheckpointIdentityTable table = Table(
                new[] { CheckpointTestRecords.SampleRootScope(2) },
                null,
                new[] { noRecipe },
                null,
                null,
                null,
                null);

            Assert.That(
                table.ValidateReferences(null, null, new[] { noRecipe }, null, null, null, null, null, null),
                Is.False);
            Assert.That(HasDiagnostic(table, DiagnosticCode.MissingDependency, "all-zero recipe"), Is.True);
        }

        [Test]
        public void ASlotNamingAnUndeclaredTargetIsRejected()
        {
            SlotRecordValue slot = CheckpointTestRecords.SampleSlot(2, 1, 1);
            CheckpointIdentityTable table = Table(
                null,
                null,
                new[] { CheckpointTestRecords.SampleTarget(1, 1) },
                new[] { slot },
                null,
                null,
                null);

            Assert.That(
                table.ValidateReferences(null, null, null, new[] { slot }, null, null, null, null, null),
                Is.False);
            Assert.That(table.Diagnostics.Count, Is.EqualTo(1));
            Assert.That(HasDiagnostic(table, DiagnosticCode.MissingDependency, "names target"), Is.True);
        }

        [Test]
        public void ASelectionNamingAnUndeclaredProviderIsRejected()
        {
            InstallRecordValue instance = CheckpointTestRecords.SampleInstall(1, 1);
            SelectionRecordValue selection = CheckpointTestRecords.SampleSelection(1, 2);
            CheckpointIdentityTable table = Table(null, new[] { instance }, null, null, null, null, null);

            Assert.That(table.HasInstall(selection.Instance), Is.True);
            Assert.That(table.InstallCount, Is.EqualTo(1));
            Assert.That(
                table.ValidateReferences(null, null, null, null, new[] { selection }, null, null, null, null),
                Is.False);
            Assert.That(table.Diagnostics.Count, Is.EqualTo(1));
            Assert.That(HasDiagnostic(table, DiagnosticCode.MissingDependency, "provider"), Is.True);
        }

        [Test]
        public void ADeclaredSelectionResolves()
        {
            InstallRecordValue instance = CheckpointTestRecords.SampleInstall(1, 1);
            InstallRecordValue provider = CheckpointTestRecords.SampleInstall(2, 1);
            SelectionRecordValue selection = CheckpointTestRecords.SampleSelection(1, 2);
            CheckpointIdentityTable table = Table(
                null,
                new[] { instance, provider },
                null,
                null,
                null,
                null,
                null);

            Assert.That(table.InstallCount, Is.EqualTo(2));
            Assert.That(table.InstallIds.Count, Is.EqualTo(2));
            Assert.That(
                table.ValidateReferences(null, null, null, null, new[] { selection }, null, null, null, null),
                Is.True);
            Assert.That(table.Diagnostics, Is.Empty);
        }

        [Test]
        public void AMessageNamingAnUndeclaredBufferIsRejected()
        {
            MessageRecordValue message = CheckpointTestRecords.SampleMessage(5, 1, 2);
            CheckpointIdentityTable table = Table(
                null,
                null,
                new[] { CheckpointTestRecords.SampleTarget(2, 1) },
                null,
                null,
                null,
                null);

            Assert.That(table.HasBuffer(message.Buffer), Is.False);
            Assert.That(
                table.ValidateReferences(null, null, null, null, null, null, null, null, new[] { message }),
                Is.False);
            Assert.That(table.Diagnostics.Count, Is.EqualTo(1));
            Assert.That(HasDiagnostic(table, DiagnosticCode.MissingDependency, "buffer"), Is.True);
        }

        [Test]
        public void ADeclaredBufferMessageResolvesButItsRequestTargetMustExist()
        {
            MessageRecordValue message = CheckpointTestRecords.SampleMessage(5, 1, 2);
            CheckpointIdentityTable table = Table(
                null,
                null,
                null,
                null,
                null,
                new[] { message },
                null);

            Assert.That(table.HasBuffer(message.Buffer), Is.True);
            Assert.That(table.Diagnostics, Is.Empty);

            // HasRequest is true, so the request's target must exist as well.
            Assert.That(
                table.ValidateReferences(null, null, null, null, null, null, null, null, new[] { message }),
                Is.False);
            Assert.That(HasDiagnostic(table, DiagnosticCode.MissingDependency, "target"), Is.True);
        }

        [Test]
        public void AQueuedCommandNamingAnUndeclaredTargetIsRejected()
        {
            CommandRecordValue command = CheckpointTestRecords.SampleCommand(5, 1);
            CheckpointIdentityTable table = Empty();

            Assert.That(
                table.ValidateReferences(null, null, null, null, null, null, null, new[] { command }, null),
                Is.False);
            Assert.That(table.Diagnostics.Count, Is.EqualTo(1));
            Assert.That(table.Diagnostics[0].Code, Is.EqualTo(DiagnosticCode.MissingDependency));
            Assert.That(HasDiagnostic(table, DiagnosticCode.MissingDependency, "queued command names target"), Is.True);
        }

        [Test]
        public void AQueuedCommandForADeclaredTargetResolves()
        {
            CommandRecordValue command = CheckpointTestRecords.SampleCommand(5, 1);
            CheckpointIdentityTable table = Table(
                null,
                null,
                new[] { CheckpointTestRecords.SampleTarget(1, 1) },
                null,
                null,
                null,
                null);

            Assert.That(table.HasTarget(command.Target), Is.True);
            Assert.That(
                table.ValidateReferences(null, null, null, null, null, null, null, new[] { command }, null),
                Is.True);
            Assert.That(table.Diagnostics, Is.Empty);
        }

        [Test]
        public void AnAllZeroGrantScopeIsRejected()
        {
            // A grant whose scope is the all-zero identity: never a catalog identity (P-004), so it is refused
            // before any category lookup happens.
            var grant = new GrantRecordValue(
                (uint)GrantKind.ScopeImport,
                0UL,
                0UL,
                CheckpointTestRecords.Id(2).High,
                CheckpointTestRecords.Id(2).Low,
                CheckpointTestRecords.Id(3).High,
                CheckpointTestRecords.Id(3).Low,
                2U,
                CheckpointTestRecords.Id(4).High,
                CheckpointTestRecords.Id(4).Low,
                CheckpointTestRecords.Id(5).High,
                CheckpointTestRecords.Id(5).Low,
                CheckpointTestRecords.Id(6).High,
                CheckpointTestRecords.Id(6).Low,
                3U,
                CheckpointTestRecords.Id(7).High,
                CheckpointTestRecords.Id(7).Low,
                true,
                false,
                (uint)ExclusionTargetKind.Rule,
                4U);
            CheckpointIdentityTable table = Empty();

            Assert.That(
                table.ValidateReferences(null, null, null, null, null, new[] { grant }, null, null, null),
                Is.False);
            Assert.That(HasDiagnostic(table, DiagnosticCode.MissingDependency, "all-zero scope"), Is.True);
        }

        [Test]
        public void AnOptInForAnUndeclaredTargetIsRejected()
        {
            GrantRecordValue optIn = CheckpointTestRecords.SampleGrant(
                (uint)GrantKind.TargetOptIn, 1, 2, 3);
            CheckpointIdentityTable table = Table(
                new[] { CheckpointTestRecords.SampleRootScope(1) },
                null,
                null,
                null,
                null,
                null,
                null);

            Assert.That(table.HasScope(optIn.Scope), Is.True);
            Assert.That(
                table.ValidateReferences(null, null, null, null, null, new[] { optIn }, null, null, null),
                Is.False);
            Assert.That(HasDiagnostic(table, DiagnosticCode.MissingDependency, "opt-in names target"), Is.True);
        }

        [Test]
        public void AWakeWithoutAWakeIdentityIsRejected()
        {
            ClockRecordValue wake = new ClockRecordValue(
                1U,
                CheckpointTestRecords.Id(401).High,
                CheckpointTestRecords.Id(401).Low,
                0U,
                0U,
                false,
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
            CheckpointIdentityTable table = Empty();

            Assert.That(
                table.ValidateReferences(null, null, null, null, null, null, new[] { wake }, null, null),
                Is.False);
            Assert.That(HasDiagnostic(table, DiagnosticCode.MissingDependency, "wake identity"), Is.True);
        }

        [Test]
        public void ASelfConsistentMinimalDocumentValidates()
        {
            ScopeRecordValue root = CheckpointTestRecords.SampleRootScope(1);
            TargetRecordValue target = CheckpointTestRecords.SampleTarget(1, 1);
            SlotRecordValue slot = CheckpointTestRecords.SampleSlot(1, 2, 3);
            CheckpointIdentityTable table = Table(
                new[] { root },
                null,
                new[] { target },
                new[] { slot },
                null,
                null,
                null);

            Assert.That(table.ScopeCount, Is.EqualTo(1));
            Assert.That(table.TargetCount, Is.EqualTo(1));
            Assert.That(table.SlotCount, Is.EqualTo(1));
            Assert.That(table.OwnerCount, Is.EqualTo(1));
            Assert.That(table.Diagnostics, Is.Empty);
            Assert.That(table.World, Is.EqualTo(TableWorld));

            Assert.That(
                table.ValidateReferences(
                    new[] { root },
                    null,
                    new[] { target },
                    new[] { slot },
                    null,
                    null,
                    null,
                    null,
                    null),
                Is.True);
            Assert.That(table.Diagnostics, Is.Empty);
        }

        [Test]
        public void EveryCheckpointReferenceIsAnIdentityAReaderCanLookUp()
        {
            // The document stores no runtime handle (P-005): every reference the table validates is one of these
            // stable identity categories, and the same value type answers the same question on every record.
            CheckpointIdentityTable table = Table(
                new[] { CheckpointTestRecords.SampleRootScope(1) },
                new[] { CheckpointTestRecords.SampleInstall(1, 1) },
                new[] { CheckpointTestRecords.SampleTarget(1, 1) },
                new[] { CheckpointTestRecords.SampleSlot(1, 2, 3) },
                new[] { CheckpointTestRecords.SampleClockDeclaration(1) },
                new[] { CheckpointTestRecords.SampleMessage(5, 1, 1) },
                new[] { CheckpointTestRecords.SampleRng(5) });

            Assert.That(table.Diagnostics, Is.Empty);
            Assert.That(table.HasScope(CheckpointTestRecords.ScopeIdOf(1)), Is.True);
            Assert.That(table.HasInstall(CheckpointTestRecords.InstallIdOf(1)), Is.True);
            Assert.That(table.HasTarget(CheckpointTestRecords.TargetIdOf(1)), Is.True);
            Assert.That(table.HasSlot(CheckpointTestRecords.SlotIdOf(3)), Is.True);
            Assert.That(table.HasOwner(CheckpointTestRecords.OwnerIdOf(2)), Is.True);
            Assert.That(table.HasBuffer(CheckpointTestRecords.BufferIdOf(1)), Is.True);
            Assert.That(table.HasRngStream(CheckpointTestRecords.Id(705)), Is.True);
            Assert.That(table.HasClock(CheckpointTestRecords.Id(401)), Is.True);
            Assert.That(table.HasScope(CheckpointTestRecords.ScopeIdOf(2)), Is.False);
        }
    }
}

// GC-007 pure tests — slot dispositions and last-support policy validation (TEST-010, TEST-013).
//
// Normative sources: 00-core-protocols.md P-020, P-028, P-032, P-033. The point of these tests is that a declared
// policy set is validated with no migration executor in the world at all: an early slice can validate a plan before
// the code that would apply it exists (09 GC-007).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Planning.Ownership;
using NUnit.Framework;

namespace GameCore.Planning.Tests.Ownership
{
    /// <summary>Slot policy validation: complete declarations, migrations, permitted resets and transfers.</summary>
    [TestFixture]
    public sealed class SlotPolicyValidatorTests
    {
        private static readonly SchemaRef Facts = OwnershipFixtureIds.SchemaRefOf(1UL, 1U);
        private static readonly SchemaRef FactsV2 = OwnershipFixtureIds.SchemaRefOf(1UL, 2U);
        private static readonly SchemaRef OtherSchema = OwnershipFixtureIds.SchemaRefOf(2UL, 1U);

        private static readonly OwnerId QuestOwner = OwnershipFixtureIds.Owner(1UL);

        private static SlotAuthorityDeclaration Slot(
            SlotAuthorityOptions options,
            LastSupportPolicy lastSupport,
            FactoryKey transferPolicy,
            IReadOnlyList<FactoryKey>? migrationKeys)
            => new SlotAuthorityDeclaration(
                OwnershipFixtureIds.Slot(1UL),
                QuestOwner,
                Facts,
                OwnershipFixtureIds.Key(10UL),
                null,
                lastSupport,
                transferPolicy,
                migrationKeys,
                options);

        [Test]
        public void ACompleteDeclarationWithNoMigrationExecutorValidates()
        {
            SlotAuthorityDeclaration slot = Slot(
                SlotAuthorityOptions.Durable(),
                LastSupportPolicy.PreserveDormant,
                default(FactoryKey),
                null);

            SlotPolicyResult result = SlotPolicyValidator.ValidateDeclaration(slot);

            Assert.That(result.Succeeded, Is.True, result.Detail);
            Assert.That(result.Outcome, Is.EqualTo(SlotPolicyOutcome.Validated));
            Assert.That(result.Code, Is.EqualTo(DiagnosticCode.None));
        }

        [Test]
        public void ADeclarationWithoutAnOwnerRejectsBeforeAnyRequest()
        {
            var slot = new SlotAuthorityDeclaration(
                OwnershipFixtureIds.Slot(1UL),
                default(OwnerId),
                Facts,
                OwnershipFixtureIds.Key(10UL),
                null,
                LastSupportPolicy.PreserveDormant,
                default(FactoryKey),
                null,
                SlotAuthorityOptions.Durable());

            SlotPolicyResult result = SlotPolicyValidator.ValidateDeclaration(slot);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Code, Is.EqualTo(DiagnosticCode.MissingDependency));
        }

        [Test]
        public void TransferToWithoutATransferPolicyRejects()
        {
            SlotAuthorityDeclaration slot = Slot(
                SlotAuthorityOptions.Durable(),
                LastSupportPolicy.TransferTo,
                default(FactoryKey),
                null);

            SlotPolicyResult result = SlotPolicyValidator.ValidateDeclaration(slot);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Code, Is.EqualTo(DiagnosticCode.MissingDependency), result.Detail);
        }

        [Test]
        public void RemoveDerivedOnDurableStateRejects()
        {
            SlotAuthorityDeclaration slot = Slot(
                SlotAuthorityOptions.Durable(),
                LastSupportPolicy.RemoveDerived,
                default(FactoryKey),
                null);

            SlotPolicyResult result = SlotPolicyValidator.ValidateDeclaration(slot);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Code, Is.EqualTo(DiagnosticCode.Ineligible), result.Detail);
        }

        [Test]
        public void ReconfigurationPreservesRuntimeState()
        {
            SlotAuthorityDeclaration slot = Slot(
                SlotAuthorityOptions.Durable(),
                LastSupportPolicy.PreserveDormant,
                default(FactoryKey),
                null);

            SlotPolicyResult result = SlotPolicyValidator.Validate(
                slot,
                SlotPolicyRequest.ConfigurationUpdate(),
                null);

            Assert.That(result.Outcome, Is.EqualTo(SlotPolicyOutcome.PreserveRuntimeState));
        }

        [Test]
        public void AVersionChangeWithoutARegisteredMigrationIsMigrationRequired()
        {
            FactoryKey migration = OwnershipFixtureIds.Migration(1UL);
            SlotAuthorityDeclaration slot = Slot(
                SlotAuthorityOptions.Durable(),
                LastSupportPolicy.PreserveDormant,
                default(FactoryKey),
                new[] { migration });

            SlotPolicyResult declared = SlotPolicyValidator.Validate(
                slot,
                SlotPolicyRequest.VersionChange(FactsV2, migration),
                new SlotMigrationRegistry());

            Assert.That(declared.Succeeded, Is.False, "A declared migration with no executor cannot be applied (P-032).");
            Assert.That(declared.Code, Is.EqualTo(DiagnosticCode.MigrationRequired), declared.Detail);

            var withExecutor = new SlotMigrationRegistry();
            withExecutor.Register(migration, Facts, FactsV2);

            SlotPolicyResult applied = SlotPolicyValidator.Validate(
                slot,
                SlotPolicyRequest.VersionChange(FactsV2, migration),
                withExecutor);

            Assert.That(applied.Succeeded, Is.True, applied.Detail);
            Assert.That(applied.Outcome, Is.EqualTo(SlotPolicyOutcome.Migrate));
        }

        [Test]
        public void AVersionChangeWithAnUndeclaredMigrationKeyIsMigrationRequired()
        {
            SlotAuthorityDeclaration slot = Slot(
                SlotAuthorityOptions.Durable(),
                LastSupportPolicy.PreserveDormant,
                default(FactoryKey),
                new[] { OwnershipFixtureIds.Migration(1UL) });

            SlotPolicyResult result = SlotPolicyValidator.Validate(
                slot,
                SlotPolicyRequest.VersionChange(FactsV2, OwnershipFixtureIds.Migration(2UL)),
                new SlotMigrationRegistry());

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Code, Is.EqualTo(DiagnosticCode.MigrationRequired), result.Detail);
        }

        [Test]
        public void ASchemaIdentityChangeIsNotAMigration()
        {
            FactoryKey migration = OwnershipFixtureIds.Migration(1UL);
            SlotAuthorityDeclaration slot = Slot(
                SlotAuthorityOptions.Durable(),
                LastSupportPolicy.PreserveDormant,
                default(FactoryKey),
                new[] { migration });

            var registry = new SlotMigrationRegistry();
            registry.Register(migration, Facts, OtherSchema);

            SlotPolicyResult result = SlotPolicyValidator.Validate(
                slot,
                SlotPolicyRequest.VersionChange(OtherSchema, migration),
                registry);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Code, Is.EqualTo(DiagnosticCode.UnsupportedVersion), result.Detail);
        }

        [Test]
        public void AnUnpermittedResetRejectsAndAPermittedResetNeedsAReason()
        {
            SlotAuthorityDeclaration durable = Slot(
                SlotAuthorityOptions.Durable(),
                LastSupportPolicy.PreserveDormant,
                default(FactoryKey),
                null);

            SlotPolicyResult refused = SlotPolicyValidator.Validate(durable, SlotPolicyRequest.Reset("content repair"), null);
            Assert.That(refused.Succeeded, Is.False, "An unpermitted reset is never implicit (P-032).");
            Assert.That(refused.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));

            SlotAuthorityDeclaration resettable = Slot(
                SlotAuthorityOptions.Resettable("content repair", preserveDormantPermitted: true, disposableDerived: false),
                LastSupportPolicy.PreserveDormant,
                default(FactoryKey),
                null);

            SlotPolicyResult noReason = SlotPolicyValidator.Validate(resettable, SlotPolicyRequest.Reset(null), null);
            Assert.That(noReason.Succeeded, Is.False, "A reset requires an explicit reason (P-032).");
            Assert.That(noReason.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));

            SlotPolicyResult permitted = SlotPolicyValidator.Validate(resettable, SlotPolicyRequest.Reset("content repair"), null);
            Assert.That(permitted.Succeeded, Is.True, permitted.Detail);
            Assert.That(permitted.Outcome, Is.EqualTo(SlotPolicyOutcome.Reset));
        }

        [Test]
        public void AnOwnerTransferThatContradictsTheDeclaredPolicyRejects()
        {
            SlotAuthorityDeclaration dormantOnly = Slot(
                SlotAuthorityOptions.Dormant(),
                LastSupportPolicy.PreserveDormant,
                default(FactoryKey),
                null);

            SlotPolicyResult refused = SlotPolicyValidator.Validate(
                dormantOnly,
                SlotPolicyRequest.OwnerTransfer(OwnershipFixtureIds.Target(1UL)),
                null);

            Assert.That(refused.Succeeded, Is.False, "An owner transfer requires the declared TransferTo policy (P-032).");
            Assert.That(refused.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));

            FactoryKey transfer = OwnershipFixtureIds.Key(11UL);
            SlotAuthorityDeclaration transferable = Slot(
                SlotAuthorityOptions.Durable(),
                LastSupportPolicy.TransferTo,
                transfer,
                null);

            SlotPolicyResult accepted = SlotPolicyValidator.Validate(
                transferable,
                SlotPolicyRequest.OwnerTransfer(OwnershipFixtureIds.Target(1UL)),
                null);

            Assert.That(accepted.Succeeded, Is.True, accepted.Detail);
            Assert.That(accepted.Outcome, Is.EqualTo(SlotPolicyOutcome.Transfer));

            SlotPolicyResult unnamed = SlotPolicyValidator.Validate(
                transferable,
                SlotPolicyRequest.OwnerTransfer(default(TargetId)),
                null);

            Assert.That(unnamed.Succeeded, Is.False, "A transfer must name its destination for later validation (P-047).");
        }

        [Test]
        public void ALostSupportPolicyMustMatchTheDeclaration()
        {
            FactoryKey transfer = OwnershipFixtureIds.Key(11UL);
            SlotAuthorityDeclaration transferable = Slot(
                SlotAuthorityOptions.Durable(),
                LastSupportPolicy.TransferTo,
                transfer,
                null);

            SlotPolicyResult mismatched = SlotPolicyValidator.Validate(
                transferable,
                SlotPolicyRequest.LastSupportLoss(LastSupportPolicy.RemoveDerived, default(TargetId)),
                null);

            Assert.That(mismatched.Succeeded, Is.False, "The declared policy is the only legal one (P-032).");
            Assert.That(mismatched.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));

            SlotPolicyResult matched = SlotPolicyValidator.Validate(
                transferable,
                SlotPolicyRequest.LastSupportLoss(LastSupportPolicy.TransferTo, OwnershipFixtureIds.Target(2UL)),
                null);

            Assert.That(matched.Succeeded, Is.True, matched.Detail);
            Assert.That(matched.Outcome, Is.EqualTo(SlotPolicyOutcome.Transfer));
        }

        [Test]
        public void ADisposableDerivedSlotMayBeRemovedOnLastSupportLoss()
        {
            SlotAuthorityDeclaration derived = Slot(
                SlotAuthorityOptions.DerivedData(),
                LastSupportPolicy.RemoveDerived,
                default(FactoryKey),
                null);

            SlotPolicyResult result = SlotPolicyValidator.Validate(
                derived,
                SlotPolicyRequest.LastSupportLoss(LastSupportPolicy.RemoveDerived, default(TargetId)),
                null);

            Assert.That(result.Succeeded, Is.True, result.Detail);
            Assert.That(result.Outcome, Is.EqualTo(SlotPolicyOutcome.RemoveDerived));
        }

        [Test]
        public void ADeclarationThatClaimsTheWrongOwnerForKeyRejects()
        {
            SlotAuthorityDeclaration slot = Slot(
                SlotAuthorityOptions.Durable(),
                LastSupportPolicy.PreserveDormant,
                default(FactoryKey),
                null);

            var wrongKey = new StateSlotKey(OwnershipFixtureIds.Target(1UL), OwnershipFixtureIds.Owner(9UL), OwnershipFixtureIds.Slot(1UL));

            Assert.That(
                SlotAuthoritySet.TryFindFor(new[] { slot }, wrongKey, out SlotAuthorityDeclaration? found, out DiagnosticCode code, out string detail),
                Is.False);
            Assert.That(found, Is.Null);
            Assert.That(code, Is.EqualTo(DiagnosticCode.OwnershipConflict), detail);

            var rightKey = new StateSlotKey(OwnershipFixtureIds.Target(1UL), QuestOwner, OwnershipFixtureIds.Slot(1UL));
            Assert.That(
                SlotAuthoritySet.TryFindFor(new[] { slot }, rightKey, out SlotAuthorityDeclaration? resolved, out DiagnosticCode ok, out _),
                Is.True);
            Assert.That(resolved, Is.Not.Null);
            Assert.That(ok, Is.EqualTo(DiagnosticCode.None));
        }
    }
}

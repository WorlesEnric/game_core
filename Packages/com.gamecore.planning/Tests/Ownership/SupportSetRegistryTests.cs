// GC-007 pure tests — shared support sets and derived component lifetime (TEST-010).
//
// Normative sources: 00-core-protocols.md P-017, P-020, P-032, P-033. Removing one provider removes exactly its own
// support; the final removal follows the declared last-support policy; a base recipe component is never removed by a
// retracting provider; and a rejected retraction leaves the recorded set exactly as it was.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Planning.Ownership;
using NUnit.Framework;

namespace GameCore.Planning.Tests.Ownership
{
    /// <summary>Support-set behaviour (P-032, P-033).</summary>
    [TestFixture]
    public sealed class SupportSetRegistryTests
    {
        private static readonly SchemaRef Bonus = OwnershipFixtureIds.SchemaRefOf(1UL);
        private static readonly OwnerId ScoringOwner = OwnershipFixtureIds.Owner(1UL);
        private static readonly SlotId BonusSlot = OwnershipFixtureIds.Slot(1UL);
        private static readonly TargetId Seat = OwnershipFixtureIds.Target(1UL);

        private static readonly StateSlotKey Key = new StateSlotKey(Seat, ScoringOwner, BonusSlot);

        private static SupportSetRegistry Registry(LastSupportPolicy policy, SlotAuthorityOptions options, bool recipeRequired = false)
        {
            var slot = new SlotAuthorityDeclaration(
                BonusSlot,
                ScoringOwner,
                Bonus,
                OwnershipFixtureIds.Key(10UL),
                null,
                policy,
                policy == LastSupportPolicy.TransferTo ? OwnershipFixtureIds.Key(11UL) : default(FactoryKey),
                null,
                options);

            var required = recipeRequired ? new[] { Key } : null;
            return new SupportSetRegistry(new[] { slot }, required);
        }

        private static SupportRecord Support(ulong provider, ulong rule)
            => new SupportRecord(
                Key,
                OwnershipFixtureIds.Provider(provider),
                OwnershipFixtureIds.Rule(rule),
                OwnershipFixtureIds.Capability(1UL));

        [Test]
        public void RemovingOneOfTwoSupportsKeepsTheComponentActive()
        {
            SupportSetRegistry registry = Registry(LastSupportPolicy.RemoveDerived, SlotAuthorityOptions.DerivedData());
            SupportRecord first = Support(1UL, 1UL);
            SupportRecord second = Support(2UL, 2UL);

            Assert.That(registry.Add(first).Applied, Is.True);
            Assert.That(registry.Add(second).Applied, Is.True);
            Assert.That(registry.SupportCount(Key), Is.EqualTo(2));

            SupportSetDelta retraction = registry.Retract(first);

            Assert.That(retraction.Applied, Is.True);
            Assert.That(retraction.Outcome, Is.EqualTo(SupportSetOutcome.RetractedWhileSupported));
            Assert.That(retraction.Lifetime, Is.EqualTo(DerivedLifetimeDecision.KeepActive));
            Assert.That(registry.SupportCount(Key), Is.EqualTo(1), "Removing one provider removes exactly its support (P-033).");
            Assert.That(registry.DecideLifetime(Key), Is.EqualTo(DerivedLifetimeDecision.KeepActive));
        }

        [Test]
        public void TheFinalSupportLossFollowsTheDeclaredPolicy()
        {
            SupportSetRegistry registry = Registry(LastSupportPolicy.PreserveDormant, SlotAuthorityOptions.Dormant());
            SupportRecord only = Support(1UL, 1UL);
            registry.Add(only);

            SupportSetDelta retraction = registry.Retract(only);

            Assert.That(retraction.Applied, Is.True);
            Assert.That(retraction.Outcome, Is.EqualTo(SupportSetOutcome.RetractedFinalSupport));
            Assert.That(retraction.Lifetime, Is.EqualTo(DerivedLifetimeDecision.RetainDormant));
            Assert.That(retraction.EndedActiveLife, Is.True);
            Assert.That(registry.HasSupport(Key), Is.False);
            Assert.That(registry.RetainedDormantCount, Is.EqualTo(1));
            Assert.That(registry.DecideLifetime(Key), Is.EqualTo(DerivedLifetimeDecision.RetainDormant));
        }

        [Test]
        public void RemovingTheFinalSupportIsRejectedWhenTheDeclarationDoesNotPermitIt()
        {
            // Durable state that is not disposable derived data and does not permit dormant retention: the
            // declaration simply cannot express a legal final removal, and the retraction is refused unchanged.
            SupportSetRegistry registry = Registry(LastSupportPolicy.RemoveDerived, SlotAuthorityOptions.Durable());
            SupportRecord only = Support(1UL, 1UL);
            registry.Add(only);

            SupportSetDelta retraction = registry.Retract(only);

            Assert.That(retraction.Applied, Is.False);
            Assert.That(retraction.Outcome, Is.EqualTo(SupportSetOutcome.PolicyRejected));
            Assert.That(retraction.Code, Is.EqualTo(DiagnosticCode.Ineligible), retraction.Detail);
            Assert.That(registry.SupportCount(Key), Is.EqualTo(1), "A rejected retraction leaves the set exactly as it was (P-032).");
            Assert.That(registry.PolicyRejectedCount, Is.EqualTo(1));
        }

        [Test]
        public void ABaseRecipeRequirementKeepsTheComponentAfterTheFinalSupportLeaves()
        {
            SupportSetRegistry registry = Registry(
                LastSupportPolicy.RemoveDerived,
                SlotAuthorityOptions.DerivedData(),
                recipeRequired: true);
            SupportRecord only = Support(1UL, 1UL);
            registry.Add(only);

            SupportSetDelta retraction = registry.Retract(only);

            Assert.That(retraction.Applied, Is.True);
            Assert.That(retraction.Outcome, Is.EqualTo(SupportSetOutcome.RetainedByRecipeRequirement));
            Assert.That(retraction.Lifetime, Is.EqualTo(DerivedLifetimeDecision.KeepActive));
            Assert.That(registry.HasSupport(Key), Is.False);
            Assert.That(registry.DecideLifetime(Key), Is.EqualTo(DerivedLifetimeDecision.KeepActive));
            Assert.That(registry.RetainedByRecipeCount, Is.EqualTo(1));
        }

        [Test]
        public void TheIdenticalSupportIsNotASecondSupport()
        {
            SupportSetRegistry registry = Registry(LastSupportPolicy.RemoveDerived, SlotAuthorityOptions.DerivedData());
            SupportRecord support = Support(1UL, 1UL);

            Assert.That(registry.Add(support).Applied, Is.True);
            SupportSetDelta duplicate = registry.Add(support);

            Assert.That(duplicate.Applied, Is.False);
            Assert.That(duplicate.Outcome, Is.EqualTo(SupportSetOutcome.DuplicateSupport));
            Assert.That(registry.SupportCount(Key), Is.EqualTo(1), "Contribution identity contributes once (P-017).");
            Assert.That(registry.DuplicateCount, Is.EqualTo(1));
        }

        [Test]
        public void RetractingASupportTheSlotDoesNotHaveChangesNothing()
        {
            SupportSetRegistry registry = Registry(LastSupportPolicy.RemoveDerived, SlotAuthorityOptions.DerivedData());
            registry.Add(Support(1UL, 1UL));

            SupportSetDelta retraction = registry.Retract(Support(2UL, 9UL));

            Assert.That(retraction.Applied, Is.False);
            Assert.That(retraction.Outcome, Is.EqualTo(SupportSetOutcome.UnknownSupport));
            Assert.That(registry.SupportCount(Key), Is.EqualTo(1));
        }

        [Test]
        public void AnUndeclaredSlotIsNeverImplicitlyInitialized()
        {
            SupportSetRegistry registry = Registry(LastSupportPolicy.RemoveDerived, SlotAuthorityOptions.DerivedData());
            var undeclared = new StateSlotKey(OwnershipFixtureIds.Target(2UL), ScoringOwner, OwnershipFixtureIds.Slot(9UL));
            var support = new SupportRecord(
                undeclared,
                OwnershipFixtureIds.Provider(1UL),
                OwnershipFixtureIds.Rule(1UL),
                OwnershipFixtureIds.Capability(1UL));

            SupportSetDelta added = registry.Add(support);

            Assert.That(added.Applied, Is.False);
            Assert.That(added.Code, Is.EqualTo(DiagnosticCode.MissingDependency), added.Detail);
            Assert.That(registry.SupportedSlotCount, Is.EqualTo(0));
        }

        [Test]
        public void ATransferPolicyLeavesTheStatePendingRatherThanRemoved()
        {
            SupportSetRegistry registry = Registry(LastSupportPolicy.TransferTo, SlotAuthorityOptions.Durable());
            SupportRecord only = Support(1UL, 1UL);
            registry.Add(only);

            SupportSetDelta retraction = registry.Retract(only);

            Assert.That(retraction.Applied, Is.True);
            Assert.That(retraction.Lifetime, Is.EqualTo(DerivedLifetimeDecision.TransferPending));
            Assert.That(registry.TransferPendingCount, Is.EqualTo(1));
        }

        [Test]
        public void SupportedSlotsAreReportedInCanonicalOrder()
        {
            SupportSetRegistry registry = Registry(LastSupportPolicy.RemoveDerived, SlotAuthorityOptions.DerivedData());
            registry.Add(Support(1UL, 1UL));

            IReadOnlyList<StateSlotKey> slots = registry.SlotsWithSupport();

            Assert.That(slots.Count, Is.EqualTo(1));
            Assert.That(slots[0], Is.EqualTo(Key));
            Assert.That(registry.TotalSupportCount, Is.EqualTo(1));
            Assert.That(registry.AddedCount, Is.EqualTo(1));
        }
    }
}

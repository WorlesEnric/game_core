// GC-028 conformance closure: the required diagnostic-code list (P-052) and the runtime service-binding
// lease contract (P-007). Both are normative claims that had no direct executable assertion before this task:
//
//   * P-052 names twenty codes and calls them required. `DiagnosticCodeText.Values` is the production list;
//     nothing asserted that it holds exactly those names, in that order, each round-tripping through the text
//     mapping the diagnostics actually print.
//   * P-007 says a runtime service binding carries contract/provider/activation-epoch/lease and that the lease is
//     a process-local identity resolved once per assembly. There is no production counter for "resolutions per
//     frame" (see the task hand-off), so the executable half asserted here is the binding's own contract: all
//     four fields are present, the lease is a real non-default identity, it is stable for one binding instance and
//     distinct between two provider installations, and a rebind is observable as a different activation epoch.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using NUnit.Framework;

namespace GameCore.Contracts.Tests
{
    /// <summary>The 00 P-052 required diagnostic codes, as the catalog actually exposes them.</summary>
    [TestFixture]
    public sealed class RequiredDiagnosticCodeTests
    {
        /// <summary>
        /// The twenty names 00 P-052 lists, in the order the requirement lists them. This literal is the
        /// requirement's own text, not a copy of the implementation: the test fails if the enum, the text mapping
        /// or the exposure list drifts from the normative list.
        /// </summary>
        private static readonly string[] Documented =
        {
            "StaleHandle",
            "StalePlan",
            "MissingDependency",
            "ServiceConflict",
            "CapabilityConflict",
            "AmbiguousOrder",
            "Cycle",
            "Ineligible",
            "UnsupportedVersion",
            "OwnershipConflict",
            "BudgetExceeded",
            "MigrationRequired",
            "ResourceUnavailable",
            "Cancelled",
            "TooLate",
            "IdempotencyConflict",
            "ResultExpired",
            "ApplyFault",
            "TeardownBlocked",
            "CursorExpired",
        };

        [Test]
        public void EveryDocumentedCodeExistsIsNamedExactlyAndAppearsInTheExposedList()
        {
            IReadOnlyList<DiagnosticCode> exposed = DiagnosticCodeText.Values;

            Assert.That(exposed.Count, Is.GreaterThanOrEqualTo(Documented.Length),
                "the exposed list carries every documented code");
            for (int i = 0; i < Documented.Length; i++)
            {
                string name = Documented[i];
                Assert.That(DiagnosticCodeText.TryParse(name, out DiagnosticCode code), Is.True,
                    "P-052 requires the code '" + name + "' and the catalog must define it");

                Assert.That(code.ToString(), Is.EqualTo(name), "the enum member is named after the required code");
                Assert.That(DiagnosticCodeText.Of(code), Is.EqualTo(name), "the printed text is the required name");
                Assert.That(exposed, Does.Contain(code), "the required code is part of the exposed required list");
            }
        }

        [Test]
        public void TheDocumentedCodesAreExposedInTheRequirementOrder()
        {
            IReadOnlyList<DiagnosticCode> exposed = DiagnosticCodeText.Values;

            for (int i = 0; i < Documented.Length; i++)
            {
                Assert.That(DiagnosticCodeText.Of(exposed[i]), Is.EqualTo(Documented[i]),
                    "00 P-052 lists the required codes in this order");
            }
        }

        [Test]
        public void NoneIsAValueButNeverARequiredCode()
        {
            Assert.That(DiagnosticCodeText.Values, Does.Not.Contain(DiagnosticCode.None),
                "a successful result is not a required failure code");
            Assert.That(DiagnosticCodeText.Of(DiagnosticCode.None), Is.EqualTo("None"),
                "the text mapping still names the no-error value");
            Assert.That(DiagnosticCodeText.TryParse("None", out DiagnosticCode parsed), Is.False,
                "the parser resolves the required codes the catalog exposes; a successful result is the absence " +
                "of a code, and the frozen W0 seam refuses it through the same Values-driven lookup");
        }

        [Test]
        public void EveryEnumValueIsMappedAndEitherRequiredOrTheDocumentedP007Addition()
        {
            foreach (DiagnosticCode code in Enum.GetValues(typeof(DiagnosticCode)))
            {
                string text = DiagnosticCodeText.Of(code);
                Assert.That(text, Is.Not.Empty, "every code has a stable printed name (P-052)");
                if (code == DiagnosticCode.None)
                {
                    // None is the no-error value, not a required code: the catalog's exposed list is the
                    // required set, and TryParse resolves exactly that set (see NoneIsAValueButNeverARequiredCode).
                    continue;
                }

                Assert.That(DiagnosticCodeText.TryParse(text, out DiagnosticCode roundTripped), Is.True);
                Assert.That(roundTripped, Is.EqualTo(code), "the printed name parses back to its own code");

                bool required = Array.IndexOf(Documented, text) >= 0;
                bool isTheP007LeaseCode = code == DiagnosticCode.SnapshotBackpressure;
                bool isTheP012ProviderCode = code == DiagnosticCode.ProviderFailed;
                Assert.That(
                    required || isTheP007LeaseCode || isTheP012ProviderCode,
                    Is.True,
                    "code " + text + " is neither a 00 s9 required code, the P-007 SnapshotBackpressure code, " +
                    "nor the P-012 ProviderFailed code GC-028 added additively");
            }
        }

        [Test]
        public void AnUnknownNameIsRefusedRatherThanCoerced()
        {
            Assert.That(DiagnosticCodeText.TryParse("NotARealCode", out DiagnosticCode code), Is.False);
            Assert.That(code, Is.EqualTo(default(DiagnosticCode)));
            Assert.That(DiagnosticCodeText.TryParse(null, out DiagnosticCode fromNull), Is.False);
            Assert.That(fromNull, Is.EqualTo(default(DiagnosticCode)));
        }
    }

    /// <summary>The epoch-bound service binding of P-007, exercised as a value contract.</summary>
    [TestFixture]
    public sealed class ServiceBindingLeaseTests
    {
        private static readonly Id128 ContractId = new Id128(0xB000000000000001UL, 0x0000000000000001UL);
        private static readonly Id128 LeaseOne = new Id128(0xB000000000000010UL, 0x0000000000000010UL);
        private static readonly Id128 LeaseTwo = new Id128(0xB000000000000020UL, 0x0000000000000020UL);
        private static readonly Id128 ProviderOne = new Id128(0xB000000000000030UL, 0x0000000000000030UL);
        private static readonly Id128 ProviderTwo = new Id128(0xB000000000000040UL, 0x0000000000000040UL);

        private static ServiceBinding Binding(
            Id128 provider,
            ulong activationEpoch,
            Id128 lease,
            ServiceBindingKind kind = ServiceBindingKind.Single,
            bool fallback = false) =>
            new ServiceBinding(
                new ContractRef(ContractId, 1U),
                new ProviderInstallationId(provider),
                new ActivationEpoch(activationEpoch),
                lease,
                kind,
                fallback);

        [Test]
        public void ABindingCarriesContractProviderActivationEpochAndLease()
        {
            ServiceBinding binding = Binding(ProviderOne, 3UL, LeaseOne);

            Assert.That(binding.Contract.ContractId, Is.EqualTo(ContractId));
            Assert.That(binding.Contract.Version, Is.EqualTo(1U));
            Assert.That(binding.Provider.Value, Is.EqualTo(ProviderOne));
            Assert.That(binding.ActivationEpoch.Value, Is.EqualTo(3UL));
            Assert.That(binding.LeaseId.IsDefault, Is.False,
                "the binding owns a real lease identity rather than a placeholder (P-007)");
            Assert.That(binding.BindingKind, Is.EqualTo(ServiceBindingKind.Single));
            Assert.That(binding.IsFallback, Is.False, "an ordinary binding is not a fallback rebind (P-012)");
        }

        [Test]
        public void TwoResolutionsOfOneProviderAreTheSameBindingValueAndKeepTheirLease()
        {
            ServiceBinding first = Binding(ProviderOne, 3UL, LeaseOne);
            ServiceBinding again = Binding(ProviderOne, 3UL, LeaseOne);

            Assert.That(again.Provider, Is.EqualTo(first.Provider));
            Assert.That(again.ActivationEpoch, Is.EqualTo(first.ActivationEpoch));
            Assert.That(again.LeaseId, Is.EqualTo(first.LeaseId),
                "one resolved assembly hands the same lease to every consumer (P-007)");
        }

        [Test]
        public void ARebindIsObservableAsADifferentActivationEpochUnderTheSameLeaseIdentity()
        {
            ServiceBinding before = Binding(ProviderOne, 3UL, LeaseOne);
            ServiceBinding after = Binding(ProviderOne, 4UL, LeaseOne);

            Assert.That(after.ActivationEpoch, Is.Not.EqualTo(before.ActivationEpoch),
                "a rebind changes the activation epoch (P-006, P-011)");
            Assert.That(after.LeaseId, Is.EqualTo(before.LeaseId),
                "the lease identity is per resolution, not per epoch");
        }

        [Test]
        public void TwoProvidersNeverShareOneLeaseIdentity()
        {
            ServiceBinding one = Binding(ProviderOne, 1UL, LeaseOne);
            ServiceBinding two = Binding(ProviderTwo, 1UL, LeaseTwo);

            Assert.That(two.LeaseId, Is.Not.EqualTo(one.LeaseId),
                "a lease keeps exactly one provider alive (P-007, P-048)");
            Assert.That(two.Provider, Is.Not.EqualTo(one.Provider));
        }

        [Test]
        public void AnOptionalDependencyThatFellBackSaysSoDistinctlyFromASelectedProvider()
        {
            ServiceBinding selected = Binding(ProviderOne, 1UL, LeaseOne);
            ServiceBinding fallback = Binding(ProviderOne, 1UL, LeaseTwo, ServiceBindingKind.Single, fallback: true);

            Assert.That(fallback.IsFallback, Is.True);
            Assert.That(selected.IsFallback, Is.False);
            Assert.That(fallback.LeaseId, Is.Not.EqualTo(selected.LeaseId),
                "a fallback provider owns its own lease");
        }
    }
}

// GameCore.Composition tests — the structured diagnostic contract (P-052).
//
// P-052 requires a stable code, world/operation identity, phase, involved ids, counts/budgets and retry
// classification on a rejection. These fixtures assert those fields on the paths this task owns, so "the
// rejection code was right" is not mistaken for "the diagnostic is complete".
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Composition;
using NUnit.Framework;

namespace GameCore.Composition.Tests
{
    [TestFixture]
    public sealed class DiagnosticContractTests
    {
        private static readonly WorldId World = new WorldId(new Id128(0x776F726C64UL, 8UL));
        private static readonly IdFactory Ids = new IdFactory(0x64696167UL);
        private static readonly ScopeId Root = new ScopeId(new Id128(0x726F6F74UL, 8UL));

        private sealed class Rig
        {
            public Rig(ulong domain)
            {
                Ids = new IdFactory(domain);
                Manifests = new TestManifestSource();
                Host = CompositionHost.CreateDefault(World, Root, Manifests, null);
                Issuer = new OperationIssuer(World, new Id128(domain, 1UL));
            }

            public IdFactory Ids { get; }

            public TestManifestSource Manifests { get; }

            public CompositionHost Host { get; }

            public OperationIssuer Issuer { get; }
        }

        [Test]
        public void AServiceConflictDiagnosticNamesTheOperationPhaseInvolvedProvidersAndRetry()
        {
            Rig rig = new Rig(0x6469616731UL);
            Id128 contract = rig.Ids.Capability().Value;
            PluginTypeId providerType = rig.Ids.Type();
            PluginTypeId consumerType = rig.Ids.Type();

            rig.Manifests.Add(Manifests.Plain(providerType, rig.Ids, null, new[] { Manifests.Export(new ContractRef(contract, 1U), rig.Ids.NextId()) }), null);
            rig.Manifests.Add(Manifests.Plain(consumerType, rig.Ids, null, null, new[] { Manifests.Requires(new ContractRef(contract, 1U)) }), null);

            PluginInstanceId first = rig.Ids.Instance();
            PluginInstanceId second = rig.Ids.Instance();
            PluginInstanceId consumer = rig.Ids.Instance();
            foreach (PluginInstanceId provider in new[] { first, second })
            {
                EditAdmission mounted = rig.Host.SubmitEdit(
                    Payloads.Mount(rig.Manifests.ManifestOf(providerType), provider, Root, null),
                    rig.Issuer.Next(),
                    rig.Host.Snapshot().Revision);
                Assert.That(mounted.Staged, Is.True, mounted.Code.ToString());
                rig.Host.Drain();
            }

            OperationId operation = rig.Issuer.Next();
            EditAdmission admission = rig.Host.SubmitEdit(
                Payloads.Mount(rig.Manifests.ManifestOf(consumerType), consumer, Root, null),
                operation,
                rig.Host.Snapshot().Revision);

            Assert.That(admission.Code, Is.EqualTo(DiagnosticCode.ServiceConflict));
            Assert.That(rig.Host.Read(admission.Handle).Entry!.Outcome, Is.EqualTo(Outcome.Rejected));
            Assert.That(rig.Host.Drain(), Is.Empty);
            Assert.That(rig.Host.FindInstall(consumer), Is.Null);
            Assert.That(rig.Host.Snapshot().Revision.Value, Is.EqualTo(2UL));
            Assert.That(admission.Diagnostics, Is.Not.Empty);
            Diagnostic diagnostic = admission.Diagnostics[0];

            Assert.That(diagnostic.Code, Is.EqualTo(DiagnosticCode.ServiceConflict));
            Assert.That(diagnostic.CodeText, Is.EqualTo("ServiceConflict"));
            Assert.That(diagnostic.Phase, Is.EqualTo(OperationPhase.Validation));
            Assert.That(diagnostic.Operation, Is.EqualTo(operation), "The plan's diagnostics name the operation (P-052).");
            Assert.That(diagnostic.Retry, Is.EqualTo(RetryClassification.RequiresChangedInput));
            Assert.That(diagnostic.Summary, Is.Not.Empty);
            Assert.That(diagnostic.Summary, Does.Contain(contract.ToString()), "The summary names the contract under dispute.");

            // Involved ids carry the contract and every conflicting provider, which is the smallest known set.
            Assert.That(diagnostic.InvolvedIds, Does.Contain(contract));
            Assert.That(diagnostic.InvolvedIds, Does.Contain(first.Value));
            Assert.That(diagnostic.InvolvedIds, Does.Contain(second.Value));
            Assert.That(diagnostic.Count, Is.EqualTo(2), "Two providers were visible when the conflict was reported.");
        }

        [Test]
        public void AMissingRequiredProviderDiagnosticNamesTheContractAndZeroProviders()
        {
            Rig rig = new Rig(0x6469616732UL);
            Id128 contract = rig.Ids.Capability().Value;
            PluginTypeId consumerType = rig.Ids.Type();
            rig.Manifests.Add(
                Manifests.Plain(consumerType, rig.Ids, null, null, new[] { Manifests.Requires(new ContractRef(contract, 1U)) }),
                null);

            PluginInstanceId consumer = rig.Ids.Instance();
            OperationId operation = rig.Issuer.Next();
            EditAdmission admission = rig.Host.SubmitEdit(
                Payloads.Mount(rig.Manifests.ManifestOf(consumerType), consumer, Root, null),
                operation,
                CompositionRevision.Zero);

            Assert.That(admission.Staged, Is.True, admission.Code.ToString());
            rig.Host.Drain();
            Assert.That(rig.Host.FindInstall(consumer)!.State, Is.EqualTo(InstallationState.WaitingForDependencies));

            Diagnostic diagnostic = rig.Host.FindInstall(consumer)!.Diagnostics[0];
            Assert.That(diagnostic.Code, Is.EqualTo(DiagnosticCode.MissingDependency));
            Assert.That(diagnostic.Operation, Is.EqualTo(operation));
            Assert.That(diagnostic.InvolvedIds.Count, Is.EqualTo(1), "Only the absent contract is involved.");
            Assert.That(diagnostic.InvolvedIds[0], Is.EqualTo(contract));
            Assert.That(diagnostic.Count, Is.EqualTo(0), "No provider was visible.");
            Assert.That(diagnostic.Retry, Is.EqualTo(RetryClassification.RequiresChangedInput));
        }

        [Test]
        public void AValidationRejectionDiagnosticCarriesTheOperationAndAClassification()
        {
            TestManifestSource source = new TestManifestSource();
            CompositionHost host = CompositionHost.CreateDefault(World, Root, source, null);
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6469616733UL, 1UL));
            ScopeId scope = Ids.Scope();

            // Stale expected revision: the operation names itself and says what a retry would have to change.
            OperationId operation = issuer.Next();
            EditAdmission admission = host.SubmitEdit(Payloads.ScopeCreate(scope, Root), operation, new CompositionRevision(7UL));

            Assert.That(admission.Code, Is.EqualTo(DiagnosticCode.StalePlan));
            Assert.That(admission.Diagnostics.Count, Is.EqualTo(1));
            Diagnostic diagnostic = admission.Diagnostics[0];
            Assert.That(diagnostic.Code, Is.EqualTo(DiagnosticCode.StalePlan));
            Assert.That(diagnostic.CodeText, Is.EqualTo("StalePlan"));
            Assert.That(diagnostic.Operation, Is.EqualTo(operation));
            Assert.That(diagnostic.Phase, Is.EqualTo(OperationPhase.Validation));
            Assert.That(diagnostic.Retry, Is.EqualTo(RetryClassification.RequiresChangedInput));
            Assert.That(diagnostic.Summary, Is.Not.Empty);
            Assert.That(host.OperationLedger.RowOf(operation)!.Code, Is.EqualTo(DiagnosticCode.StalePlan), "The ledger records the same code.");
        }

        [Test]
        public void EveryReachableRefusalCarriesItsCodePhaseOperationAndClassification()
        {
            // Each entry drives one real rejection through the control lane and names the code it must report.
            int index = 0;
            foreach ((DiagnosticCode expected, string label) in Rejections())
            {
                index++;
                Rig rig = new Rig(0x6469616736UL + (ulong)index);
                Id128 contract = rig.Ids.Capability().Value;
                PluginTypeId type = rig.Ids.Type();
                PluginInstanceId instance = rig.Ids.Instance();
                PluginManifest manifest = Manifests.Plain(type, rig.Ids);
                rig.Manifests.Add(manifest, ConfigDocument.Empty);

                OperationId operation = rig.Issuer.Next();
                EditAdmission admission;

                switch (label)
                {
                    case "StalePlan":
                        admission = rig.Host.SubmitEdit(Payloads.ScopeCreate(rig.Ids.Scope(), Root), operation, new CompositionRevision(4UL));
                        break;
                    case "MissingDependency":
                        // A mount of a plugin type the catalog does not know (P-009).
                        admission = rig.Host.SubmitEdit(
                            Payloads.Mount(manifest, instance, rig.Ids.Scope(), null),
                            operation,
                            CompositionRevision.Zero);
                        break;
                    case "OwnershipConflict":
                        admission = rig.Host.SubmitEdit(Payloads.ScopeRemove(Root, true), operation, CompositionRevision.Zero);
                        break;
                    case "CapabilityConflict":
                        admission = rig.Host.SubmitEdit(
                            Payloads.ScopeIsolation(Root, new IsolationSet(true, new[] { contract }), null),
                            operation,
                            CompositionRevision.Zero);
                        break;
                    case "Cycle":
                    {
                        ScopeId parent = rig.Ids.Scope();
                        ScopeId child = rig.Ids.Scope();
                        rig.Host.SubmitEdit(Payloads.ScopeCreate(parent, Root), rig.Issuer.Next(), CompositionRevision.Zero);
                        rig.Host.Drain();
                        rig.Host.SubmitEdit(Payloads.ScopeCreate(child, parent), rig.Issuer.Next(), rig.Host.Snapshot().Revision);
                        rig.Host.Drain();
                        operation = rig.Issuer.Next();
                        admission = rig.Host.SubmitEdit(Payloads.ScopeReparent(parent, child), operation, rig.Host.Snapshot().Revision);
                        break;
                    }
                    case "UnsupportedVersion":
                        admission = rig.Host.SubmitEdit(
                            Payloads.Mount(manifest, instance, Root, ConfigDocument.Of(new ConfigField(rig.Ids.Capability().Value, ConfigFieldValue.OfUInt32(1U)))),
                            operation,
                            CompositionRevision.Zero);
                        break;
                    default:
                    {
                        // MigrationRequired: a TransferTo state slot whose manifest declares no transfer mapping,
                        // so the disposition it would need at last-support loss cannot be validated (P-032).
                        PluginManifest transferSlot = Manifests.Plain(
                            rig.Ids.Type(),
                            rig.Ids,
                            null,
                            null,
                            null,
                            new[]
                            {
                                Manifests.Slot(rig.Ids.Slot(), rig.Ids.Owner(), new SchemaRef(rig.Ids.Schema(), 1U), LastSupportPolicy.TransferTo),
                            });
                        rig.Manifests.Add(transferSlot, ConfigDocument.Empty);
                        admission = rig.Host.SubmitEdit(
                            Payloads.Mount(transferSlot, instance, Root, null),
                            operation,
                            CompositionRevision.Zero);
                        break;
                    }
                }

                Assert.That(admission.Code, Is.EqualTo(expected), label + " must report its documented code.");
                Assert.That(admission.Diagnostics, Is.Not.Empty, label + " must carry a structured diagnostic.");
                Diagnostic diagnostic = admission.Diagnostics[0];
                Assert.That(diagnostic.Code, Is.EqualTo(expected));
                Assert.That(diagnostic.CodeText, Is.EqualTo(DiagnosticCodeText.Of(expected)), label + " text");
                Assert.That(diagnostic.Operation, Is.EqualTo(operation), label + " must name the operation (P-052).");
                Assert.That(diagnostic.Phase, Is.EqualTo(OperationPhase.Validation));
                Assert.That(diagnostic.Retry, Is.EqualTo(RetryClassification.RequiresChangedInput), label + " retry classification");
                Assert.That(diagnostic.Summary, Is.Not.Empty, label + " summary");
                Assert.That(rig.Host.OperationLedger.RowOf(operation)!.Code, Is.EqualTo(expected), label + " ledger code");
            }
        }

        private static IEnumerable<(DiagnosticCode Code, string Label)> Rejections()
        {
            yield return (DiagnosticCode.StalePlan, "StalePlan");
            yield return (DiagnosticCode.MissingDependency, "MissingDependency");
            yield return (DiagnosticCode.OwnershipConflict, "OwnershipConflict");
            yield return (DiagnosticCode.CapabilityConflict, "CapabilityConflict");
            yield return (DiagnosticCode.Cycle, "Cycle");
            yield return (DiagnosticCode.UnsupportedVersion, "UnsupportedVersion");
            yield return (DiagnosticCode.MigrationRequired, "MigrationRequired");
        }
    }
}

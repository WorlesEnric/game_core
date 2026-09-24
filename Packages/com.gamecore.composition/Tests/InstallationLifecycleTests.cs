// GameCore.Composition tests — the installation lifecycle transition table and host-level lifecycle edges.
//
// 06 s1 fixes the exact legal edges of P-046. The table is asserted exhaustively over every state pair, so an
// extra edge (a shortcut into Active, a revived Disposed installation) or a missing edge fails here rather than
// being discovered by a later wave. The host-level fixtures then drive the edges a caller can actually reach.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Composition;
using NUnit.Framework;

namespace GameCore.Composition.Tests
{
    [TestFixture]
    public sealed class InstallationLifecycleTests
    {
        private static readonly WorldId World = new WorldId(new Id128(0x776F726C64UL, 7UL));
        private static readonly IdFactory Ids = new IdFactory(0x6C696665UL);
        private static readonly ScopeId Root = new ScopeId(new Id128(0x726F6F74UL, 7UL));

        /// <summary>
        /// The 17 legal edges of the 06 s1 diagram, listed as (from, to) pairs. A removal or suspension of an
        /// active installation therefore walks Active -> Quiescing -> ... rather than short-circuiting.
        /// </summary>
        private static readonly HashSet<string> LegalEdges = new HashSet<string>(StringComparer.Ordinal)
        {
            "Registered->WaitingForDependencies",
            "Registered->Preparing",
            "Registered->Retiring",
            "WaitingForDependencies->Preparing",
            "WaitingForDependencies->Retiring",
            "Preparing->Active",
            "Preparing->Failed",
            "Active->Quiescing",
            "Quiescing->Active",
            "Quiescing->Suspended",
            "Quiescing->WaitingForDependencies",
            "Quiescing->Retiring",
            "Suspended->Preparing",
            "Suspended->Retiring",
            "Failed->Preparing",
            "Failed->Retiring",
            "Retiring->Disposed",
        };

        [Test]
        public void EveryStatePairMatchesTheLifecycleDiagramExactly()
        {
            List<string> unexpected = new List<string>();
            List<string> missing = new List<string>();
            foreach (InstallationState from in EveryState())
            {
                foreach (InstallationState to in EveryState())
                {
                    string edge = from + "->" + to;
                    bool legal = InstallationStateMachine.IsAllowed(from, to);
                    bool expected = LegalEdges.Contains(edge);
                    if (legal && !expected)
                    {
                        unexpected.Add(edge);
                    }

                    if (!legal && expected)
                    {
                        missing.Add(edge);
                    }
                }
            }

            Assert.That(unexpected, Is.Empty, "No transition exists that the diagram does not show (P-046).");
            Assert.That(missing, Is.Empty, "Every diagram transition is implemented.");
        }

        [Test]
        public void NoStateIsItsOwnSuccessorAndDisposedIsTerminal()
        {
            foreach (InstallationState state in EveryState())
            {
                Assert.That(InstallationStateMachine.IsAllowed(state, state), Is.False, "A repeated lifecycle request is not an edge: " + state);
                Assert.That(
                    InstallationStateMachine.IsAllowed(state, InstallationState.Disposed),
                    Is.EqualTo(state == InstallationState.Retiring),
                    "Only a retiring installation reaches Disposed: " + state);
            }

            foreach (InstallationState state in EveryState())
            {
                foreach (InstallationState to in EveryState())
                {
                    if (InstallationStateMachine.IsAllowed(InstallationState.Disposed, to))
                    {
                        Assert.Fail("A disposed installation cannot be revived: Disposed->" + to);
                    }
                }
            }
        }

        [Test]
        public void RefusalIsAValueAndContributionAuthorityFollowsTheState()
        {
            LifecycleTransition refused = InstallationStateMachine.Request(InstallationState.Disposed, InstallationState.Active);
            Assert.That(refused.Allowed, Is.False);
            Assert.That(refused.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(refused.From, Is.EqualTo(InstallationState.Disposed));
            Assert.That(refused.To, Is.EqualTo(InstallationState.Active));

            // Which states expose bindings at all: only the four that resolution may bind (P-012, P-047). A
            // quiescing or suspended activation therefore contributes nothing, not even its old bindings.
            Assert.That(InstallationStateMachine.CanResolveActivation(InstallationState.Active), Is.True);
            Assert.That(InstallationStateMachine.CanResolveActivation(InstallationState.Registered), Is.True);
            Assert.That(InstallationStateMachine.CanResolveActivation(InstallationState.WaitingForDependencies), Is.True);
            Assert.That(InstallationStateMachine.CanResolveActivation(InstallationState.Preparing), Is.True);
            Assert.That(InstallationStateMachine.CanResolveActivation(InstallationState.Quiescing), Is.False, "A quiescing activation has no new authority (P-047).");
            Assert.That(InstallationStateMachine.CanResolveActivation(InstallationState.Suspended), Is.False);
            Assert.That(InstallationStateMachine.CanResolveActivation(InstallationState.Retiring), Is.False);
            Assert.That(InstallationStateMachine.CanResolveActivation(InstallationState.Disposed), Is.False);
            Assert.That(InstallationStateMachine.CanResolveActivation(InstallationState.Failed), Is.False);
        }

        private static IEnumerable<InstallationState> EveryState()
        {
            foreach (InstallationState state in Enum.GetValues(typeof(InstallationState)))
            {
                yield return state;
            }
        }

        [Test]
        public void HostRefusesAnIllegalLifecycleRequestAndKeepsTheInstallation()
        {
            TestManifestSource source = new TestManifestSource();
            CompositionHost host = CompositionHost.CreateDefault(World, Root, source, null);
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6C696665UL, 1UL));
            PluginTypeId type = Ids.Type();
            PluginInstanceId instance = Ids.Instance();
            source.Add(Manifests.Plain(type, Ids), null);

            host.SubmitEdit(Payloads.Mount(source.ManifestOf(type), instance, Root, null), issuer.Next(), CompositionRevision.Zero);
            host.Drain();
            Assert.That(host.FindInstall(instance)!.State, Is.EqualTo(InstallationState.Active));

            // Resume on an active installation is not an edge (Active has no Preparing successor), so it is
            // refused before publication and the published state is untouched.
            EditAdmission resume = host.SubmitEdit(Payloads.Resume(instance), issuer.Next(), host.Snapshot().Revision);
            Assert.That(resume.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(resume.Entry!.Outcome, Is.EqualTo(Outcome.Rejected));
            Assert.That(host.FindInstall(instance)!.State, Is.EqualTo(InstallationState.Active));
            Assert.That(host.Snapshot().Revision.Value, Is.EqualTo(1UL));

            // Suspend then suspend again: the second request is not an edge from Suspended.
            EditAdmission suspend = host.SubmitEdit(Payloads.Suspend(instance), issuer.Next(), host.Snapshot().Revision);
            Assert.That(suspend.Staged, Is.True, suspend.Code.ToString());
            host.Drain();
            Assert.That(host.FindInstall(instance)!.State, Is.EqualTo(InstallationState.Suspended));

            EditAdmission repeat = host.SubmitEdit(Payloads.Suspend(instance), issuer.Next(), host.Snapshot().Revision);
            Assert.That(repeat.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(host.FindInstall(instance)!.State, Is.EqualTo(InstallationState.Suspended));

            // An unmount of a suspended installation is a real edge and retracts the installation.
            EditAdmission unmount = host.SubmitEdit(Payloads.Unmount(instance), issuer.Next(), host.Snapshot().Revision);
            Assert.That(unmount.Staged, Is.True, unmount.Code.ToString());
            host.Drain();
            Assert.That(host.FindInstall(instance)!.State, Is.EqualTo(InstallationState.Disposed));
        }

        [Test]
        public void UnmountOfANeverActivatedInstallationIsAllowedAndRetiresIt()
        {
            TestManifestSource source = new TestManifestSource();
            CompositionHost host = CompositionHost.CreateDefault(World, Root, source, null);
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6C696665UL, 2UL));
            PluginTypeId type = Ids.Type();
            PluginInstanceId instance = Ids.Instance();
            Id128 missing = Ids.Capability().Value;

            // This installation never activates: its required provider does not exist (P-012).
            source.Add(
                Manifests.Plain(type, Ids, null, null, new[] { Manifests.Requires(new ContractRef(missing, 1U)) }),
                null);

            host.SubmitEdit(Payloads.Mount(source.ManifestOf(type), instance, Root, null), issuer.Next(), CompositionRevision.Zero);
            host.Drain();
            Assert.That(host.FindInstall(instance)!.State, Is.EqualTo(InstallationState.WaitingForDependencies));
            Assert.That(host.FindInstall(instance)!.Bindings, Is.Empty);
            Assert.That(host.FindInstall(instance)!.Diagnostics[0].CodeText, Is.EqualTo("MissingDependency"));

            // WaitingForDependencies -> Retiring -> Disposed is the lawful path for a waiting installation.
            EditAdmission unmount = host.SubmitEdit(Payloads.Unmount(instance), issuer.Next(), host.Snapshot().Revision);
            Assert.That(unmount.Staged, Is.True, unmount.Code.ToString());
            Assert.That(host.Drain().Count, Is.EqualTo(1));
            Assert.That(host.FindInstall(instance)!.State, Is.EqualTo(InstallationState.Disposed));
            Assert.That(host.FindInstall(instance)!.Bindings, Is.Empty);

            // A disposed installation cannot be unmounted again and cannot be reconfigured.
            EditAdmission again = host.SubmitEdit(Payloads.Unmount(instance), issuer.Next(), host.Snapshot().Revision);
            Assert.That(again.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(host.Snapshot().Revision.Value, Is.EqualTo(2UL));
        }
    }
}

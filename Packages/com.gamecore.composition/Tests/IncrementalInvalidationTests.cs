// GameCore.Composition tests — invalidation change sets, mode/exclusion edits and the publication validator
// (GC-013, P-013/P-014/P-016/P-023).
//
// GC-013's composition half has three claims to check: a proposal publishes the invalidation view of what it
// changed (so a derivation caller never has to re-read the whole definition), an isolation or exclusion edit is
// visible as a change rather than an empty delta, and a mode switch whose consequence cannot be honoured is
// rejected *as a proposal* so the old mode and the old assembly stay published (P-014).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Composition;
using NUnit.Framework;

namespace GameCore.Composition.Tests
{
    [TestFixture]
    public sealed class IncrementalInvalidationTests
    {
        private static readonly WorldId World = new WorldId(new Id128(0x776F726C64UL, 13UL));
        private static readonly IdFactory Ids = new IdFactory(0x696E7661UL);
        private static readonly ScopeId Root = new ScopeId(new Id128(0x726F6F74UL, 13UL));

        private static CompositionHost NewHost(ICompositionEditValidator? validator = null)
        {
            TestManifestSource source = new TestManifestSource();
            CompositionHostSettings settings = new CompositionHostSettings(
                new ControlLaneCapacitySettings(16, 16),
                new OperationExpirySettings(3, 0UL));
            return new CompositionHost(World, Root, settings, source, null, PropagationMode.Automatic, default(CompositionLaneSeed), validator);
        }

        private static ScopeId NewScope(CompositionHost host, OperationIssuer issuer, ScopeId parent)
        {
            ScopeId scope = Ids.Scope();
            EditAdmission admission = host.SubmitEdit(Payloads.ScopeCreate(scope, parent), issuer.Next(), host.Snapshot().Revision);
            Assert.That(admission.Staged, Is.True, admission.Code.ToString());
            host.Drain();
            return scope;
        }

        [Test]
        public void AModeSwitchPublishesAWholeWorldChangeSet()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6973737565UL, 131UL));

            EditAdmission admission = host.SubmitEdit(
                Payloads.SetMode(PropagationMode.Conservative), issuer.Next(), host.Snapshot().Revision);

            Assert.That(admission.Staged, Is.True, admission.Code.ToString());
            CompositionChangeSet changeSet = admission.Plan!.ChangeSet;
            Assert.That(changeSet.ModeChanged, Is.True);
            Assert.That(changeSet.WholeWorld, Is.True, "A mode switch can invalidate the world (P-014).");
            Assert.That(changeSet.BeforeMode, Is.EqualTo(PropagationMode.Automatic));
            Assert.That(changeSet.AfterMode, Is.EqualTo(PropagationMode.Conservative));
            Assert.That(changeSet.Reasons(), Does.Contain(CompositionChangeReasons.Mode));
            Assert.That(changeSet.IsEmpty, Is.False);
            Assert.That(changeSet.Describe(), Does.Contain("mode=Automatic->Conservative"));

            host.Drain();
            Assert.That(host.Mode, Is.EqualTo(PropagationMode.Conservative));
        }

        [Test]
        public void BothModeSwitchDirectionsPublishAndAreReported()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6973737565UL, 132UL));

            EditAdmission forward = host.SubmitEdit(
                Payloads.SetMode(PropagationMode.Conservative), issuer.Next(), host.Snapshot().Revision);
            host.Drain();
            Assert.That(forward.Plan!.ChangeSet.ModeChanged, Is.True);
            Assert.That(host.Mode, Is.EqualTo(PropagationMode.Conservative));

            EditAdmission backward = host.SubmitEdit(
                Payloads.SetMode(PropagationMode.Automatic), issuer.Next(), host.Snapshot().Revision);
            host.Drain();
            Assert.That(backward.Plan!.ChangeSet.ModeChanged, Is.True);
            Assert.That(backward.Plan.ChangeSet.BeforeMode, Is.EqualTo(PropagationMode.Conservative));
            Assert.That(backward.Plan.ChangeSet.AfterMode, Is.EqualTo(PropagationMode.Automatic));
            Assert.That(host.Mode, Is.EqualTo(PropagationMode.Automatic));
        }

        [Test]
        public void RepeatingTheCurrentModeIsNoChangeAndReportsNothing()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6973737565UL, 133UL));

            EditAdmission admission = host.SubmitEdit(
                Payloads.SetMode(PropagationMode.Automatic), issuer.Next(), host.Snapshot().Revision);

            Assert.That(admission.Entry!.Outcome, Is.EqualTo(Outcome.NoChange));
            Assert.That(admission.Plan!.ChangeSet.IsEmpty, Is.True, "NoChange names no change at all (P-006).");
            Assert.That(host.Snapshot().Revision.Value, Is.EqualTo(0UL));
        }

        [Test]
        public void AnIsolationEditIsVisibleAsAChangeRatherThanAnEmptyDelta()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6973737565UL, 134UL));
            ScopeId parent = NewScope(host, issuer, Root);
            ScopeId child = NewScope(host, issuer, parent);

            CapabilityId capability = Ids.Capability();
            EditAdmission admission = host.SubmitEdit(
                Payloads.ScopeIsolation(child, null, new IsolationSet(false, new[] { capability.Value })),
                issuer.Next(),
                host.Snapshot().Revision);

            Assert.That(admission.Staged, Is.True, admission.Code.ToString());
            CompositionChangeSet changeSet = admission.Plan!.ChangeSet;
            Assert.That(changeSet.ScopeFacts.Count, Is.EqualTo(1), "The isolation set is a scope fact (P-016).");
            Assert.That(changeSet.ScopeFacts[0].Scope.Equals(child), Is.True);
            Assert.That(changeSet.ScopeFacts[0].Reason, Is.EqualTo(ScopeFactChangeReason.Isolation));
            Assert.That(changeSet.Reasons(), Does.Contain(CompositionChangeReasons.ScopeFacts));
            Assert.That(changeSet.WholeWorld, Is.False, "A boundary edit is a local invalidation (P-016).");

            bool sawUpdate = false;
            for (int i = 0; i < admission.Plan.Delta!.Scopes.Count; i++)
            {
                if (admission.Plan.Delta.Scopes[i].Kind == CompositionEditKind.Update
                    && admission.Plan.Delta.Scopes[i].Scope.Equals(child))
                {
                    sawUpdate = true;
                }
            }

            Assert.That(sawUpdate, Is.True, "The delta names the edited scope rather than staying empty.");
            Assert.That(admission.Plan.IsNoChange, Is.False, "An isolation edit changes the definition fingerprint.");
        }

        [Test]
        public void AnExclusionEditAndAnImportEditAreBothReportedAsScopeFacts()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6973737565UL, 135UL));
            ScopeId scope = NewScope(host, issuer, Root);

            CapabilityId capability = Ids.Capability();
            // The fixture's fluent helpers cover isolation and grants; an exclusion set travels in the payload's
            // own exclusions field, so the edit is declared here exactly as the codec would carry it (P-016).
            EditAdmission excluded = host.SubmitEdit(
                new CompositionEditPayload(
                    CompositionEditSubject.ScopeIsolation,
                    scope,
                    default(ScopeId),
                    false,
                    null,
                    null,
                    new[] { new ExclusionRule(ExclusionTargetKind.Capability, capability.Value, scope, default(TargetId), true) },
                    null,
                    default(PluginTypeId),
                    default(PluginInstanceId),
                    DefinitionRevision.Zero,
                    ContentHash.Empty,
                    null,
                    0,
                    null,
                    PropagationMode.Automatic),
                issuer.Next(),
                host.Snapshot().Revision);
            host.Drain();

            bool sawExclusions = false;
            for (int i = 0; i < excluded.Plan!.ChangeSet.ScopeFacts.Count; i++)
            {
                if (excluded.Plan.ChangeSet.ScopeFacts[i].Reason == ScopeFactChangeReason.Exclusions)
                {
                    sawExclusions = true;
                }
            }

            Assert.That(sawExclusions, Is.True, "An exclusion edit is a scope fact (P-016).");

            PluginInstanceId provider = Ids.Instance();
            EditAdmission granted = host.SubmitEdit(
                Payloads.ScopeGrants(scope, new[] { new CapabilityImport(capability, provider) }),
                issuer.Next(),
                host.Snapshot().Revision);
            host.Drain();

            bool sawImports = false;
            for (int i = 0; i < granted.Plan!.ChangeSet.ScopeFacts.Count; i++)
            {
                if (granted.Plan.ChangeSet.ScopeFacts[i].Reason == ScopeFactChangeReason.Imports)
                {
                    sawImports = true;
                }
            }

            Assert.That(sawImports, Is.True, "A Conservative import edit is a scope fact (P-013).");
            Assert.That(granted.Plan.ChangeSet.Reasons(), Does.Contain(CompositionChangeReasons.ScopeFacts));
        }

        [Test]
        public void AScopeReparentReportsTheMoveAndItsNewParent()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6973737565UL, 136UL));
            ScopeId first = NewScope(host, issuer, Root);
            ScopeId second = NewScope(host, issuer, Root);
            ScopeId moved = NewScope(host, issuer, first);

            EditAdmission admission = host.SubmitEdit(
                Payloads.ScopeReparent(moved, second), issuer.Next(), host.Snapshot().Revision);
            host.Drain();

            Assert.That(admission.Plan!.ChangeSet.ScopeEdits.Count, Is.EqualTo(1));
            Assert.That(admission.Plan.ChangeSet.ScopeEdits[0].Kind, Is.EqualTo(CompositionEditKind.Reparent));
            Assert.That(admission.Plan.ChangeSet.ScopeEdits[0].OldParent.Equals(first), Is.True);
            Assert.That(admission.Plan.ChangeSet.ScopeEdits[0].NewParent.Equals(second), Is.True);
            Assert.That(admission.Plan.ChangeSet.Reasons(), Does.Contain(CompositionChangeReasons.ScopeReparented));
            Assert.That(admission.Plan.ChangeSet.WholeWorld, Is.False, "A move is a local invalidation (P-025).");
            Assert.That(host.FindScope(moved)!.Parent.Equals(second), Is.True);
        }

        [Test]
        public void AValidatorRefusalRejectsTheSwitchAndKeepsTheOldModeAndRevision()
        {
            RefusingValidator validator = new RefusingValidator();
            CompositionHost host = NewHost(validator);
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6973737565UL, 137UL));

            EditAdmission admission = host.SubmitEdit(
                Payloads.SetMode(PropagationMode.Conservative), issuer.Next(), host.Snapshot().Revision);

            Assert.That(admission.Rejected, Is.True);
            Assert.That(admission.Code, Is.EqualTo(DiagnosticCode.CapabilityConflict));
            Assert.That(admission.Plan, Is.Null, "A refused proposal is not staged.");
            Assert.That(admission.Diagnostics.Count, Is.GreaterThan(0));
            Assert.That(admission.Diagnostics[0].Code, Is.EqualTo(DiagnosticCode.CapabilityConflict));
            Assert.That(admission.Diagnostics[0].Summary, Does.Contain(RefusingValidator.Detail));

            Assert.That(validator.Invocations, Is.EqualTo(1), "The validator sees the planned proposal once.");
            Assert.That(host.Mode, Is.EqualTo(PropagationMode.Automatic), "The old mode stays published (P-014).");
            Assert.That(host.Snapshot().Revision.Value, Is.EqualTo(0UL));
            Assert.That(host.Snapshot().Epoch.Value, Is.EqualTo(0UL));

            host.Drain();
            Assert.That(host.Mode, Is.EqualTo(PropagationMode.Automatic));
        }

        [Test]
        public void AnAcceptingValidatorIsAskedAboutEveryProposalAndChangesNothing()
        {
            AcceptingValidator validator = new AcceptingValidator();
            CompositionHost host = NewHost(validator);
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6973737565UL, 138UL));

            ScopeId scope = Ids.Scope();
            EditAdmission created = host.SubmitEdit(Payloads.ScopeCreate(scope, Root), issuer.Next(), host.Snapshot().Revision);
            host.Drain();
            Assert.That(created.Staged, Is.True, created.Code.ToString());
            Assert.That(validator.Seen.Count, Is.EqualTo(1), "Every planned proposal is validated.");
            Assert.That(validator.Seen[0], Does.Contain(CompositionChangeReasons.ScopeAdded));

            EditAdmission switched = host.SubmitEdit(
                Payloads.SetMode(PropagationMode.Conservative), issuer.Next(), host.Snapshot().Revision);
            host.Drain();
            Assert.That(switched.Staged, Is.True, switched.Code.ToString());
            Assert.That(validator.Seen.Count, Is.EqualTo(2));
            Assert.That(validator.Seen[1], Does.Contain(CompositionChangeReasons.Mode));
            Assert.That(host.Mode, Is.EqualTo(PropagationMode.Conservative));
            Assert.That(host.Snapshot().Revision.Value, Is.EqualTo(2UL));
        }

        [Test]
        public void ARefusalAlsoWorksForANonModeEditSoTheSeamIsGeneral()
        {
            RefusingValidator validator = new RefusingValidator();
            CompositionHost host = NewHost(validator);
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6973737565UL, 139UL));
            ScopeId first = NewScope(host, issuer, Root);
            ScopeId second = NewScope(host, issuer, Root);
            ScopeId moved = NewScope(host, issuer, first);

            EditAdmission admission = host.SubmitEdit(
                Payloads.ScopeReparent(moved, second), issuer.Next(), host.Snapshot().Revision);

            Assert.That(admission.Rejected, Is.True);
            Assert.That(host.FindScope(moved)!.Parent.Equals(first), Is.True, "The refusal keeps the old membership.");
            Assert.That(host.Snapshot().Revision.Value, Is.EqualTo(3UL), "Revisions from the earlier scopes only.");
        }

        [Test]
        public void ALaneWithoutAValidatorBehavesExactlyAsBefore()
        {
            CompositionHost host = NewHost();
            Assert.That(host.Validator, Is.Null, "A pure composition lane has no derivation view.");

            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6973737565UL, 140UL));
            EditAdmission admission = host.SubmitEdit(
                Payloads.SetMode(PropagationMode.Conservative), issuer.Next(), host.Snapshot().Revision);
            host.Drain();

            Assert.That(admission.Staged, Is.True, admission.Code.ToString());
            Assert.That(host.Mode, Is.EqualTo(PropagationMode.Conservative));
        }

        private sealed class RefusingValidator : ICompositionEditValidator
        {
            public const string Detail = "the switch would expose an unresolved capability conflict";

            public int Invocations { get; private set; }

            public EditValidationResult Validate(
                CompositionState before,
                CompositionState after,
                CompositionChangeSet changeSet)
            {
                Invocations++;
                return EditValidationResult.Refuse(DiagnosticCode.CapabilityConflict, Detail);
            }
        }

        private sealed class AcceptingValidator : ICompositionEditValidator
        {
            /// <summary>The reason keys of every change set the validator was asked about, in call order.</summary>
            public List<List<string>> Seen { get; } = new List<List<string>>();

            public EditValidationResult Validate(
                CompositionState before,
                CompositionState after,
                CompositionChangeSet changeSet)
            {
                _ = before;
                _ = after;
                Seen.Add(new List<string>(changeSet.Reasons()));
                return EditValidationResult.Accept;
            }
        }
    }
}

// GameCore.Unity.Runtime.Tests — the recovery-from-initial-definitions suite (GC-017, TEST-016's last row).
//
// The gate sentence these cases implement, from `docs/game-core/00-core-protocols.md` P-049:
//
//   "Recovery creates a new `WorldId` from a verified checkpoint or initial catalog, validates/rebuilds composition
//    and recipes, restores state, then reopens admission; old callbacks/handles never become valid."
//
// and its P-031 half:
//
//   "If anything fails after the first live write ... the world enters `Faulted`, admission remains closed, no epoch
//    or new snapshot is published, and no simulation resumes. Its last committed immutable snapshot is inspectable
//    but its live ECS storage is unavailable except to controlled teardown/recovery."
//
// Every case drives the real modules through `RecoveryFixture`: a real owned command-driven world, the real
// planner, the real publisher over a real `TargetRegistry`, real ECS storage and the world's own fault latch. The
// source of every case is a world that reached `Faulted` by a real injected postwrite fault, and every assertion
// reads a value out of those modules — registry counts, lifecycles, fault counts, the published view and the
// destination's own storage. No case asserts a managed model of the recovery, and none of them asserts a value that
// would hold even if `InitialDefinitionRecovery` did nothing.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Unity.Fixtures;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Faults;
using GameCore.Unity.Runtime.Recovery;
using NUnit.Framework;

namespace GameCore.Unity.Runtime.Tests.Recovery
{
    [TestFixture]
    [Timeout(600000)]
    public sealed class InitialDefinitionRecoveryTests
    {
        [TearDown]
        public void TearDown() => UnityWorldRegistry.ResetAll();

        /// <summary>
        /// P-049 and P-031: one recovery from a faulted world's initial definitions produces one fresh running
        /// incarnation, and the source is never resumed, never repaired and never cleared of its fault.
        /// </summary>
        [Test]
        public void RecoveryFromInitialDefinitionsCreatesAFreshIncarnationAndNeverResumesTheSource()
        {
            RecoveryFixture fixture = RecoveryFixture.Create();
            fixture.FaultOnTheNextPublication();

            WorldId source = fixture.Source.World;
            int sourceFaults = fixture.Source.FaultCount;
            int sourceLiveWriteReaches = fixture.Source.Faults.ReachCountOf(FaultBoundary.FirstLiveWrite);
            int sourceRows = fixture.PublishedBindingRowCount;
            Assert.That(sourceRows, Is.EqualTo(2), "the source's last committed image is the contrast this case reads");
            Assert.That(UnityWorldRegistry.TryGet(source, out UnityWorldHost? beforeRecovery), Is.True);
            Assert.That(beforeRecovery!.Lifecycle, Is.EqualTo(WorldLifecycleState.Faulted));

            int registryBefore = UnityWorldRegistry.Count;
            WorldId destination = fixture.NextWorldId();
            RecoveryRequest request = RecoveryFixture.Request(source, destination);
            Assert.That(request.IsValid, Is.True, "the request is well formed: a fresh destination session (P-050)");

            RecoveryReport report = InitialDefinitionRecovery.Recover(request, RecoveryFixture.WorldRegistration());

            Assert.That(report.Outcome, Is.EqualTo(Outcome.Published), report.Describe());
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.None), report.Describe());
            Assert.That(report.Recovered, Is.True, report.Describe());
            Assert.That(report.DestinationExposed, Is.True);
            Assert.That(report.DestinationHost, Is.Not.Null, report.Describe());

            UnityWorldHost recovered = report.DestinationHost!;
            Assert.That(recovered.Lifecycle, Is.EqualTo(WorldLifecycleState.Running),
                "P-035: a created world is Running only after its initial validated assembly publication");
            Assert.That(recovered.CurrentEpoch, Is.EqualTo(AssemblyEpoch.First),
                "P-049: the recovered world restores its *initial* state, not the source's published epoch");
            Assert.That(recovered.CurrentStep, Is.EqualTo(LogicalStepId.Zero),
                "no gameplay step, clock or random stream is carried across a recovery");
            Assert.That(report.DestinationIsFreshIncarnation, Is.True, "P-004: the recovered world is a new incarnation");
            Assert.That(recovered.World.Session.Equals(source.Session), Is.False, "the destination session differs");
            Assert.That(destination.Session.Equals(source.Session), Is.False);

            Assert.That(report.RegistryCountBefore, Is.EqualTo(registryBefore));
            Assert.That(report.RegistryCountAfter, Is.EqualTo(registryBefore + 1));
            Assert.That(UnityWorldRegistry.Count, Is.EqualTo(registryBefore + 1), "exactly one world was added");

            // The source: same lifecycle, same fault count, and its latch is still armed with no new reach.
            Assert.That(report.SourceUnchanged, Is.True, report.Describe());
            Assert.That(report.SourceLifecycleBefore, Is.EqualTo(WorldLifecycleState.Faulted));
            Assert.That(report.SourceLifecycleAfter, Is.EqualTo(WorldLifecycleState.Faulted));
            Assert.That(report.SourceFaultCode, Is.EqualTo(DiagnosticCode.ApplyFault));
            Assert.That(report.SourceFaultCountBefore, Is.EqualTo(sourceFaults));
            Assert.That(report.SourceFaultCountAfter, Is.EqualTo(sourceFaults));
            Assert.That(fixture.Source.Lifecycle, Is.EqualTo(WorldLifecycleState.Faulted), "the source stays faulted");
            Assert.That(fixture.Source.FaultCount, Is.EqualTo(sourceFaults), "recovery adds no fault to the source");
            Assert.That(fixture.Source.Faults.IsArmed(FaultBoundary.FirstLiveWrite), Is.True,
                "recovery never clears the source's fault latch");
            Assert.That(fixture.Source.Faults.ReachCountOf(FaultBoundary.FirstLiveWrite), Is.EqualTo(sourceLiveWriteReaches));

            // The source never resumes: a host frame is still refused and no step advances (P-031).
            WorldPumpResult pump = fixture.PumpSource(9_000_000UL);
            Assert.That(pump.Pumped, Is.False, "P-031: a faulted world admits nothing, recovered or not");
            Assert.That(pump.Code, Is.EqualTo(DiagnosticCode.ApplyFault));
            Assert.That(fixture.Source.CurrentStep, Is.EqualTo(LogicalStepId.Zero), "no simulation resumes");
            Assert.That(fixture.Source.FaultCount, Is.EqualTo(sourceFaults), "the refused frame is not a new fault");

            // The real read-back: the destination publishes the initial assembly of the catalog and owns none of
            // the source's targets, while the source's own registry still owns both of them (P-049).
            AssemblyPublisher recoveredPublisher = RecoveryFixture.AttachPublisher(recovered);
            Assert.That(recoveredPublisher.Published.BindingRowCount, Is.EqualTo(0),
                "the recovered world starts from initial definitions: the source's " + sourceRows
                + " published row(s) are not carried over");
            Assert.That(recoveredPublisher.Published.Epoch, Is.EqualTo(AssemblyEpoch.First));
            Assert.That(recoveredPublisher.Registry.Count, Is.EqualTo(0));
            Assert.That(recoveredPublisher.Registry.IsLive(RecoveryFixtureKeys.Target(1UL)), Is.False);
            Assert.That(recoveredPublisher.Registry.IsLive(RecoveryFixtureKeys.Target(2UL)), Is.False);
            Assert.That(recoveredPublisher.Registry.World.Session.Equals(source.Session), Is.False);
            Assert.That(fixture.Registry.Count, Is.EqualTo(2), "the source's own registry was not touched");
            Assert.That(fixture.Registry.IsLive(RecoveryFixtureKeys.Target(1UL)), Is.True);
        }

        /// <summary>
        /// P-049: a live (`Running`) world is not a recovery source — recovering "over" it would be a silent second
        /// writer of one state domain — and the refusal leaves that world running.
        /// </summary>
        [Test]
        public void ARecoveryFromALiveWorldIsRefused()
        {
            RecoveryFixture fixture = RecoveryFixture.Create();
            WorldId source = fixture.Source.World;
            Assert.That(fixture.Source.Lifecycle, Is.EqualTo(WorldLifecycleState.Running));
            Assert.That(fixture.Source.FaultCount, Is.EqualTo(0));

            int registryBefore = UnityWorldRegistry.Count;
            WorldId destination = fixture.NextWorldId();

            RecoveryReport report = InitialDefinitionRecovery.Recover(
                RecoveryFixture.Request(source, destination),
                RecoveryFixture.WorldRegistration());

            Assert.That(report.Outcome, Is.EqualTo(Outcome.Rejected), report.Describe());
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.TooLate));
            Assert.That(report.SourceLifecycleBefore, Is.EqualTo(WorldLifecycleState.Running));
            Assert.That(report.SourceLifecycleAfter, Is.EqualTo(WorldLifecycleState.Running));
            Assert.That(report.SourceUnchanged, Is.True);
            Assert.That(report.DestinationExposed, Is.False);
            Assert.That(report.DestinationHost, Is.Null);
            Assert.That(report.Recovered, Is.False);
            Assert.That(report.DestinationIsFreshIncarnation, Is.False);
            Assert.That(report.RegistryCountBefore, Is.EqualTo(registryBefore));
            Assert.That(report.RegistryCountAfter, Is.EqualTo(registryBefore));
            Assert.That(UnityWorldRegistry.Count, Is.EqualTo(registryBefore), "a refusal creates nothing");
            Assert.That(UnityWorldRegistry.TryGet(destination, out _), Is.False);

            // The live world is untouched: an idle frame still dispatches its ingress and output peripherals.
            FixtureTrail trailBefore = ReadTrail(fixture.Source);
            WorldPumpResult pump = fixture.Source.PumpFrame(5_000_000UL);
            FixtureTrail trailAfter = ReadTrail(fixture.Source);
            Assert.That(pump.Pumped, Is.True, "the refused recovery did not stop the live world");
            Assert.That(pump.Reentrant, Is.False);
            Assert.That(trailAfter.IngressCount, Is.EqualTo(trailBefore.IngressCount + 1));
            Assert.That(trailAfter.OutputCount, Is.EqualTo(trailBefore.OutputCount + 1));
            Assert.That(fixture.Source.CurrentStep, Is.EqualTo(LogicalStepId.Zero), "an idle frame synthesizes no step");
            Assert.That(fixture.Source.Lifecycle, Is.EqualTo(WorldLifecycleState.Running));
            Assert.That(fixture.Source.FaultCount, Is.EqualTo(0));
        }

        /// <summary>
        /// P-005 and P-050: a source session nobody owns is a stale handle rather than a recovery source, and a
        /// request whose destination is not a fresh incarnation is malformed — both refused before anything is
        /// created, and the real source is left exactly as it was.
        /// </summary>
        [Test]
        public void ARecoveryWithAStaleSourceHandleIsRefused()
        {
            RecoveryFixture fixture = RecoveryFixture.Create();
            fixture.FaultOnTheNextPublication();

            int registryBefore = UnityWorldRegistry.Count;

            // A well-formed request naming a session no world owns (P-005: a dereference validates liveness).
            WorldId unownedSource = fixture.NextWorldId();
            WorldId destination = fixture.NextWorldId();
            RecoveryRequest staleRequest = RecoveryFixture.Request(unownedSource, destination);
            Assert.That(staleRequest.IsValid, Is.True, "the request is well formed; only its source session is dead");
            Assert.That(UnityWorldRegistry.TryGet(unownedSource, out _), Is.False);

            RecoveryReport stale = InitialDefinitionRecovery.Recover(staleRequest, RecoveryFixture.WorldRegistration());

            Assert.That(stale.Outcome, Is.EqualTo(Outcome.Rejected), stale.Describe());
            Assert.That(stale.Code, Is.EqualTo(DiagnosticCode.StaleHandle));
            Assert.That(stale.DestinationExposed, Is.False);
            Assert.That(stale.DestinationHost, Is.Null);
            Assert.That(stale.Recovered, Is.False);
            Assert.That(stale.RegistryCountBefore, Is.EqualTo(registryBefore));
            Assert.That(stale.RegistryCountAfter, Is.EqualTo(registryBefore));
            Assert.That(UnityWorldRegistry.TryGet(destination, out _), Is.False, "a stale source creates nothing");

            // The malformed request: the destination session is the source's, which P-050 forbids.
            WorldId source = fixture.Source.World;
            RecoveryRequest selfDestination = RecoveryFixture.Request(source, source);
            Assert.That(
                selfDestination.IsValid,
                Is.False,
                "P-050: a recovery reserves a fresh destination session, never the source's");

            RecoveryReport malformed = InitialDefinitionRecovery.Recover(selfDestination, RecoveryFixture.WorldRegistration());

            Assert.That(malformed.Outcome, Is.EqualTo(Outcome.Rejected), malformed.Describe());
            Assert.That(malformed.Code, Is.EqualTo(DiagnosticCode.UnsupportedVersion));
            Assert.That(
                malformed.SourceLifecycleBefore,
                Is.EqualTo(WorldLifecycleState.Created),
                "a malformed request is refused before the source is read at all");
            Assert.That(malformed.SourceFaultCountBefore, Is.EqualTo(0));
            Assert.That(malformed.DestinationExposed, Is.False);
            Assert.That(malformed.RegistryCountBefore, Is.EqualTo(registryBefore));
            Assert.That(malformed.RegistryCountAfter, Is.EqualTo(registryBefore));

            // The real source is untouched: still registered, still Faulted, still one fault.
            Assert.That(UnityWorldRegistry.TryGet(source, out UnityWorldHost? after), Is.True);
            Assert.That(after!.Lifecycle, Is.EqualTo(WorldLifecycleState.Faulted));
            Assert.That(after.FaultCount, Is.EqualTo(1));
            Assert.That(fixture.Source.Lifecycle, Is.EqualTo(WorldLifecycleState.Faulted));
        }

        /// <summary>
        /// P-049 and P-004: a recovered world is rebuilt from the *same* initial definitions, so a request naming
        /// another definition is refused and no world is created for it.
        /// </summary>
        [Test]
        public void ARecoveryFromADifferentDefinitionIsRefused()
        {
            RecoveryFixture fixture = RecoveryFixture.Create();
            fixture.FaultOnTheNextPublication();

            WorldId source = fixture.Source.World;
            WorldId destination = fixture.NextWorldId();
            int registryBefore = UnityWorldRegistry.Count;
            Assert.That(
                fixture.Source.Request.Definition,
                Is.EqualTo(FixtureRegistration.CommandWorldDefinition),
                "the source was created from the command-driven definition");

            var foreign = new RecoveryRequest(
                source,
                destination,
                FixtureRegistration.FixedStepWorldDefinition,
                TemporalModel.FixedStep,
                PropagationMode.Automatic,
                ContentHash.Empty,
                RecoveryFixtureKeys.Operation(destination, 2UL),
                FixtureRegistration.FixedStepConfiguration());
            Assert.That(foreign.IsValid, Is.True, "the request is well formed; only its definition differs");

            RecoveryReport report = InitialDefinitionRecovery.Recover(foreign, RecoveryFixture.WorldRegistration());

            Assert.That(report.Outcome, Is.EqualTo(Outcome.Rejected), report.Describe());
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.MissingDependency));
            Assert.That(report.SourceLifecycleBefore, Is.EqualTo(WorldLifecycleState.Faulted));
            Assert.That(report.SourceLifecycleAfter, Is.EqualTo(WorldLifecycleState.Faulted));
            Assert.That(report.SourceUnchanged, Is.True);
            Assert.That(report.DestinationExposed, Is.False);
            Assert.That(report.Recovered, Is.False);
            Assert.That(report.RegistryCountAfter, Is.EqualTo(registryBefore));
            Assert.That(UnityWorldRegistry.TryGet(destination, out _), Is.False, "nothing was created");
            Assert.That(UnityWorldRegistry.Count, Is.EqualTo(registryBefore));
        }

        /// <summary>
        /// P-050 and P-004: one session has one owner, so a recovery into a destination session that already has a
        /// live world is refused as an idempotency conflict, and the occupant is left running.
        /// </summary>
        [Test]
        public void ARecoveryIntoAnAlreadyOwnedDestinationSessionIsRefused()
        {
            RecoveryFixture fixture = RecoveryFixture.Create();
            fixture.FaultOnTheNextPublication();

            WorldId source = fixture.Source.World;
            WorldId destination = fixture.NextWorldId();

            bool created = UnityWorldRegistry.TryCreate(
                FixtureRegistration.CommandDrivenRequest(
                    destination,
                    RecoveryFixtureKeys.Operation(destination, 1UL),
                    ContentHash.Empty),
                RecoveryFixture.WorldRegistration(),
                out UnityWorldHost? occupant,
                out WorldCreateResult createResult);
            Assert.That(created, Is.True, createResult.Code + ": " + createResult.Detail);
            Assert.That(occupant, Is.Not.Null);
            int registryBefore = UnityWorldRegistry.Count;
            Assert.That(registryBefore, Is.EqualTo(2), "the source and the occupant");

            RecoveryReport report = InitialDefinitionRecovery.Recover(
                RecoveryFixture.Request(source, destination),
                RecoveryFixture.WorldRegistration());

            Assert.That(report.Outcome, Is.EqualTo(Outcome.Rejected), report.Describe());
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.IdempotencyConflict));
            Assert.That(report.DestinationExposed, Is.False, "the occupant is not exposed as the recovery's destination");
            Assert.That(report.DestinationHost, Is.Null);
            Assert.That(report.Recovered, Is.False);
            Assert.That(report.RegistryCountBefore, Is.EqualTo(registryBefore));
            Assert.That(report.RegistryCountAfter, Is.EqualTo(registryBefore));
            Assert.That(UnityWorldRegistry.Count, Is.EqualTo(registryBefore));
            Assert.That(report.SourceUnchanged, Is.True);
            Assert.That(report.SourceLifecycleAfter, Is.EqualTo(WorldLifecycleState.Faulted));

            Assert.That(occupant!.Lifecycle, Is.EqualTo(WorldLifecycleState.Running), "the occupant is left alone");
            Assert.That(occupant.CurrentEpoch, Is.EqualTo(AssemblyEpoch.First));
            Assert.That(occupant.World.Session.Equals(destination.Session), Is.True);
        }

        /// <summary>
        /// P-049 and P-035: this is TEST-016's checkpoint-restore row in its initial-definition form. The reference
        /// repair P-049 requires runs *before* any destination exists, so a repair that refuses leaves the registry
        /// exactly as it was and the incomplete destination never becomes the running published world.
        /// </summary>
        [Test]
        public void AFailedReferenceRepairNeverExposesARunningWorld()
        {
            RecoveryFixture fixture = RecoveryFixture.Create();
            fixture.FaultOnTheNextPublication();

            WorldId source = fixture.Source.World;
            int sourceFaults = fixture.Source.FaultCount;
            WorldId destination = fixture.NextWorldId();
            int registryBefore = UnityWorldRegistry.Count;

            var repair = new RefusingReferenceRepair();
            RecoveryRequest request = RecoveryFixture.Request(source, destination);

            RecoveryReport report = InitialDefinitionRecovery.Recover(
                request,
                RecoveryFixture.WorldRegistration(),
                repair);

            Assert.That(repair.Attempts, Is.EqualTo(1), "the repair is attempted exactly once");
            Assert.That(
                ReferenceEquals(repair.Observed, request),
                Is.True,
                "the repair ran before anything was created, over the caller's own request");
            Assert.That(repair.ObservedCode, Is.EqualTo(DiagnosticCode.UnsupportedVersion));
            Assert.That(report.RepairsAttempted, Is.EqualTo(1));
            Assert.That(report.Outcome, Is.EqualTo(Outcome.Rejected), report.Describe());
            Assert.That(
                report.Code,
                Is.EqualTo(DiagnosticCode.UnsupportedVersion),
                "the refusal carries the repair's own diagnostic code, not a substitute");
            Assert.That(report.DestinationExposed, Is.False, "the incomplete destination is never exposed");
            Assert.That(report.DestinationHost, Is.Null);
            Assert.That(report.Recovered, Is.False);
            Assert.That(report.RegistryCountBefore, Is.EqualTo(registryBefore));
            Assert.That(report.RegistryCountAfter, Is.EqualTo(registryBefore));
            Assert.That(UnityWorldRegistry.Count, Is.EqualTo(registryBefore), "a failed repair creates nothing");
            Assert.That(UnityWorldRegistry.TryGet(destination, out _), Is.False, "there is no destination world at all");
            Assert.That(report.SourceFaultCountBefore, Is.EqualTo(sourceFaults));
            Assert.That(report.SourceFaultCountAfter, Is.EqualTo(sourceFaults));
            Assert.That(report.SourceUnchanged, Is.True);
            Assert.That(report.SourceLifecycleAfter, Is.EqualTo(WorldLifecycleState.Faulted), "the source is untouched");
            Assert.That(fixture.Source.Lifecycle, Is.EqualTo(WorldLifecycleState.Faulted));
        }

        /// <summary>
        /// P-049: a repair that succeeds is what makes the destination's rebuilt references publishable, and the
        /// report names that it was attempted.
        /// </summary>
        [Test]
        public void ARepairThatSucceedsRecoversAndReportsItsAttempt()
        {
            RecoveryFixture fixture = RecoveryFixture.Create();
            fixture.FaultOnTheNextPublication();

            WorldId source = fixture.Source.World;
            WorldId destination = fixture.NextWorldId();
            int registryBefore = UnityWorldRegistry.Count;

            var repair = new AcceptingReferenceRepair();

            RecoveryReport report = InitialDefinitionRecovery.Recover(
                RecoveryFixture.Request(source, destination),
                RecoveryFixture.WorldRegistration(),
                repair);

            Assert.That(repair.Attempts, Is.EqualTo(1));
            Assert.That(report.RepairsAttempted, Is.EqualTo(1));
            Assert.That(report.Outcome, Is.EqualTo(Outcome.Published), report.Describe());
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(report.Recovered, Is.True, report.Describe());
            Assert.That(report.DestinationExposed, Is.True);
            Assert.That(report.DestinationHost!.Lifecycle, Is.EqualTo(WorldLifecycleState.Running));
            Assert.That(report.DestinationHost!.CurrentEpoch, Is.EqualTo(AssemblyEpoch.First));
            Assert.That(report.DestinationHost!.CurrentStep, Is.EqualTo(LogicalStepId.Zero));
            Assert.That(report.RegistryCountAfter, Is.EqualTo(registryBefore + 1));
            Assert.That(report.SourceUnchanged, Is.True);
            Assert.That(report.SourceLifecycleAfter, Is.EqualTo(WorldLifecycleState.Faulted));
            Assert.That(fixture.Source.FaultCount, Is.EqualTo(1), "the source keeps its one fault");
        }

        /// <summary>
        /// P-035 and P-002: creation is the host's authority, and a registration its own validation refuses cannot
        /// produce a world. The recovery reports that registration's code, exposes nothing and leaves the registry
        /// exactly as it was.
        /// </summary>
        [Test]
        public void ARecoveryWhoseRegistrationIsInvalidCreatesNothing()
        {
            RecoveryFixture fixture = RecoveryFixture.Create();
            fixture.FaultOnTheNextPublication();

            WorldId source = fixture.Source.World;
            WorldId destination = fixture.NextWorldId();
            int registryBefore = UnityWorldRegistry.Count;

            UnityWorldRegistration valid = RecoveryFixture.WorldRegistration();
            Assert.That(
                valid.TryValidate(out DiagnosticCode validCode, out string validDetail),
                Is.True,
                "the fixture's registration is the valid one: " + validCode + ": " + validDetail);

            UnityWorldRegistration invalid = DuplicateSystemRegistration();
            Assert.That(
                invalid.TryValidate(out DiagnosticCode registrationCode, out string registrationDetail),
                Is.False,
                "a duplicate system key must be refused by the registration's own validation");
            Assert.That(
                registrationCode,
                Is.EqualTo(DiagnosticCode.MissingDependency),
                registrationDetail);

            RecoveryReport report = InitialDefinitionRecovery.Recover(
                RecoveryFixture.Request(source, destination),
                invalid);

            Assert.That(report.Outcome, Is.EqualTo(Outcome.Rejected), report.Describe());
            Assert.That(
                report.Code,
                Is.EqualTo(registrationCode),
                "the failed creation reports the registration's own code");
            Assert.That(report.DestinationExposed, Is.False);
            Assert.That(report.DestinationHost, Is.Null);
            Assert.That(report.Recovered, Is.False);
            Assert.That(report.RegistryCountBefore, Is.EqualTo(registryBefore));
            Assert.That(report.RegistryCountAfter, Is.EqualTo(registryBefore));
            Assert.That(UnityWorldRegistry.Count, Is.EqualTo(registryBefore));
            Assert.That(UnityWorldRegistry.TryGet(destination, out _), Is.False, "no world was created");
            Assert.That(report.SourceUnchanged, Is.True);
            Assert.That(report.SourceLifecycleAfter, Is.EqualTo(WorldLifecycleState.Faulted));
            Assert.That(fixture.Source.Lifecycle, Is.EqualTo(WorldLifecycleState.Faulted));
        }

        /// <summary>
        /// P-005, P-004, P-031 and P-049: the recoverable window is owned-but-not-Running; disposal ends ownership,
        /// so a disposed source is a stale handle rather than a recovery source. A faulted world that is still owned
        /// recovers, and neither half ever resumes the source.
        /// </summary>
        [Test]
        public void AStoppedWorldIsRefusedWhileAFaultedOwnedWorldRecovers()
        {
            // (a) A faulted world that is still owned is the recovery source of P-031 and P-049.
            RecoveryFixture faulted = RecoveryFixture.Create();
            faulted.FaultOnTheNextPublication();

            WorldId faultedSource = faulted.Source.World;
            int faultedCount = faulted.Source.FaultCount;
            int registryBeforeRecovery = UnityWorldRegistry.Count;
            WorldId recoveredDestination = faulted.NextWorldId();

            RecoveryReport recovered = InitialDefinitionRecovery.Recover(
                RecoveryFixture.Request(faultedSource, recoveredDestination),
                RecoveryFixture.WorldRegistration());

            Assert.That(recovered.Outcome, Is.EqualTo(Outcome.Published), recovered.Describe());
            Assert.That(recovered.Recovered, Is.True);
            Assert.That(recovered.SourceUnchanged, Is.True);
            Assert.That(recovered.SourceLifecycleBefore, Is.EqualTo(WorldLifecycleState.Faulted));
            Assert.That(recovered.SourceLifecycleAfter, Is.EqualTo(WorldLifecycleState.Faulted));
            Assert.That(recovered.SourceFaultCountAfter, Is.EqualTo(faultedCount));
            Assert.That(recovered.RegistryCountAfter, Is.EqualTo(registryBeforeRecovery + 1));
            Assert.That(
                UnityWorldRegistry.TryGet(faultedSource, out UnityWorldHost? stillOwned),
                Is.True,
                "an owned, non-Running world is exactly what a recovery reads");
            Assert.That(stillOwned!.Lifecycle, Is.EqualTo(WorldLifecycleState.Faulted), "and it is never resumed");
            Assert.That(stillOwned.FaultCount, Is.EqualTo(faultedCount));

            // (b) A disposed source: `Stop` ended its ownership, so there is no live world to recover from.
            RecoveryFixture stopped = RecoveryFixture.Create();
            stopped.FaultOnTheNextPublication();

            WorldId disposedSource = stopped.Source.World;
            int registryBeforeStop = UnityWorldRegistry.Count;

            OperationResult stop = stopped.Source.Stop(
                RecoveryFixtureKeys.Operation(disposedSource, 42UL),
                "the source is stopped before the recovery attempt");
            Assert.That(stop.Outcome, Is.EqualTo(Outcome.Published), "a clean stop publishes: " + stop.Code);
            Assert.That(stop.Code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(stopped.Source.Lifecycle, Is.EqualTo(WorldLifecycleState.Disposed));

            int registryAfterStop = UnityWorldRegistry.Count;
            Assert.That(
                registryAfterStop,
                Is.EqualTo(registryBeforeStop - 1),
                "disposal ends ownership: Stop removes the world from the owned-world registry");
            Assert.That(UnityWorldRegistry.TryGet(disposedSource, out _), Is.False);

            WorldId refusedDestination = stopped.NextWorldId();
            RecoveryReport refused = InitialDefinitionRecovery.Recover(
                RecoveryFixture.Request(disposedSource, refusedDestination),
                RecoveryFixture.WorldRegistration());

            Assert.That(refused.Outcome, Is.EqualTo(Outcome.Rejected), refused.Describe());
            Assert.That(
                refused.Code,
                Is.EqualTo(DiagnosticCode.StaleHandle),
                "an unregistered session is a stale handle, and a disposed world has no fault record to read (P-005)");
            Assert.That(refused.DestinationExposed, Is.False);
            Assert.That(refused.DestinationHost, Is.Null);
            Assert.That(refused.Recovered, Is.False);
            Assert.That(refused.RegistryCountBefore, Is.EqualTo(registryAfterStop));
            Assert.That(refused.RegistryCountAfter, Is.EqualTo(registryAfterStop));
            Assert.That(UnityWorldRegistry.Count, Is.EqualTo(registryAfterStop));
            Assert.That(UnityWorldRegistry.TryGet(refusedDestination, out _), Is.False, "nothing was created");
            Assert.That(
                stopped.Source.CurrentStep,
                Is.EqualTo(LogicalStepId.Zero),
                "a stopped world is not resumed either");
            Assert.That(stopped.Source.Lifecycle, Is.EqualTo(WorldLifecycleState.Disposed));

            // The still-owned faulted source of (a) also never resumed across the whole case.
            Assert.That(faulted.Source.Lifecycle, Is.EqualTo(WorldLifecycleState.Faulted));
            Assert.That(faulted.Source.CurrentStep, Is.EqualTo(LogicalStepId.Zero));
        }

        /// <summary>
        /// The fixture's registration with one extra generated-style system registration for a key a dispatch table
        /// already names: the system count per key is a registration defect the validation refuses (P-039).
        /// </summary>
        private static UnityWorldRegistration DuplicateSystemRegistration()
        {
            UnityWorldRegistration valid = RecoveryFixture.WorldRegistration();
            var systems = new List<SystemRegistration>(valid.Systems);
            systems.Add(new ManagedSystemRegistration<FixtureIngressSystem>(
                FixtureKeys.IngressSystem,
                FixtureKeys.IngressStage,
                "FixtureIngressSystem"));

            return new UnityWorldRegistration(
                valid.WorldName,
                valid.Stages,
                systems,
                valid.IngressPlan,
                valid.StepPlan,
                valid.OutputPlan,
                valid.SeedWorldState);
        }

        private static FixtureTrail ReadTrail(UnityWorldHost host)
        {
            Assert.That(
                FixtureWorldState.TryReadTrail(host.EntityWorld.EntityManager, out FixtureTrail trail),
                Is.True,
                "the fixture world must carry its seeded trail entity");
            return trail;
        }

        /// <summary>A reference repair that refuses the destination, as TEST-016's last row requires.</summary>
        private sealed class RefusingReferenceRepair : IRecoveryRepair
        {
            public int Attempts { get; private set; }

            public RecoveryRequest? Observed { get; private set; }

            public DiagnosticCode ObservedCode { get; private set; }

            public bool TryRepair(RecoveryRequest request, out DiagnosticCode code, out string detail)
            {
                Attempts++;
                Observed = request;
                code = DiagnosticCode.UnsupportedVersion;
                ObservedCode = code;
                detail = "the test repair refuses to rebuild the destination's reference tables";
                return false;
            }
        }

        /// <summary>A reference repair that accepts the destination, i.e. the P-049 path that must publish.</summary>
        private sealed class AcceptingReferenceRepair : IRecoveryRepair
        {
            public int Attempts { get; private set; }

            public bool TryRepair(RecoveryRequest request, out DiagnosticCode code, out string detail)
            {
                Attempts++;
                _ = request;
                code = DiagnosticCode.None;
                detail = string.Empty;
                return true;
            }
        }
    }
}

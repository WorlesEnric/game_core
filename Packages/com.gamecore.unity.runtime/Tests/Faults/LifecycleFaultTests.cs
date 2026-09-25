// GameCore.Unity.Runtime.Tests — the lifecycle fault boundary suite (GC-017, TEST-016 rows 4 and 8).
//
// Both cases run the real composition lifecycle modules — the real `ResourceLedger`, `JobFenceRegistry`,
// `QuarantineRegistry`, `CallbackGate` and `TeardownSequencer` — over a small recording `ILifecycleWorldBinding`,
// which is the same fixture shape `GameCore.Composition.Tests/Lifecycle/TeardownAndQuarantineTests.cs` uses for
// the world-side half of a teardown (the composition package is Unity-free, so those cases hold no world and this
// suite holds none either).
//
// The two protocol sentences these cases exist for:
//
//   * P-048 — "already executing jobs MUST finish before storage or code-owned resources are released" and
//     "resources still reachable by unfinished work are quarantined and retained; elapsed timeout only reports
//     `TeardownBlocked`, never authorizes free". Case 4 drives exactly that: a job held in flight while unload
//     begins, a second pass that still refuses to free, and a release only once the job completes.
//   * P-048 — "dispose each lease at most once, attempt every independent cleanup, aggregate failures, retain
//     anything still reachable by unfinished work". Case 8 makes one disposer throw and asserts that every other
//     cleanup was still attempted, that the failure is recorded, and that the failed resource stays retained
//     instead of being reported as disposed.
//
// The GC-017 fault latches exist only in a compilation that defines `GAMECORE_FAULT_INJECTION`, so both cases
// begin by asserting the compilation switch; a false there means the symbol is missing rather than a boundary
// being unreachable. The assertion reads `FaultCompilation.IsCompiledIn`, which is the very property
// `UnityWorldHost.Faults.IsCompiledIn` returns — this suite drives the Unity-free lifecycle modules, so it has no
// world latch to read.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Composition;
using GameCore.Execution;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Faults;
using NUnit.Framework;

namespace GameCore.Unity.Runtime.Tests.Faults
{
    [TestFixture]
    public sealed class LifecycleFaultTests
    {
        /// <summary>Id domain of this suite's rig; distinct from every other suite's, so ids never collide.</summary>
        private const ulong Domain = 0x47433031374C4946UL;

        /// <summary>The one message that explains a false <c>IsCompiledIn</c> anywhere in this suite.</summary>
        private const string CompilationMessage =
            "the fault latches need GAMECORE_FAULT_INJECTION; it is declared by GameCore.Unity.Runtime.asmdef's"
            + " versionDefines entry on com.unity.test-framework, so a false here means the symbol is missing.";

        [TearDown]
        public void TearDown() => UnityWorldRegistry.ResetAll();

        /// <summary>
        /// Recording world binding of the rig: it answers the P-048 steps the sequencer asks a world for, and counts
        /// the calls, so a case can assert which step observed what. A faulted step boundary is a fact only a caller
        /// can script, which is why the binding is a fixture rather than the real Unity glue.
        /// </summary>
        private sealed class RecordingBinding : ILifecycleWorldBinding
        {
            public int CloseCalls { get; private set; }

            public int SettleCalls { get; private set; }

            public int FenceCalls { get; private set; }

            public int RetractCalls { get; private set; }

            /// <summary>Resources the world reports as still reachable by unfinished work (P-047).</summary>
            public IReadOnlyList<Id128> Fenced { get; set; } = Array.Empty<Id128>();

            /// <summary>What the world reports at the step boundary; the default is a clean committed boundary.</summary>
            public LifecycleStepSettlement Settlement { get; set; } = LifecycleStepSettlement.At(LogicalStepId.Zero);

            public LifecycleIngressClosure CloseIngress(PluginInstanceId instance, ActivationStamp stamp)
            {
                CloseCalls++;
                return new LifecycleIngressClosure(instance, stamp, 0, 0);
            }

            public bool ReopenIngress(PluginInstanceId instance, ActivationStamp stamp)
            {
                _ = instance;
                _ = stamp;
                return true;
            }

            public LifecycleStepSettlement SettleCurrentStep()
            {
                SettleCalls++;
                return Settlement;
            }

            public IReadOnlyList<Id128> FenceUsers(PluginInstanceId instance, ActivationStamp stamp)
            {
                _ = instance;
                _ = stamp;
                FenceCalls++;
                return Fenced;
            }

            public ContributionRetraction RetractContributions(
                PluginInstanceId instance,
                ActivationStamp stamp,
                OperationId operation)
            {
                _ = stamp;
                _ = operation;
                RetractCalls++;
                return new ContributionRetraction(
                    instance,
                    0,
                    0,
                    false,
                    0,
                    0,
                    "the caller publishes the assembly");
            }
        }

        /// <summary>
        /// The rig of both cases: the real lifecycle ledgers plus one recording world binding. Leases are created
        /// with an explicit disposer callback, so "one disposer throws" is a deterministic property of the fixture
        /// rather than a second implementation of the ledger.
        /// </summary>
        private sealed class Rig
        {
            private ulong nextLease;
            private ulong nextJob;

            public Rig(ulong worldOrdinal)
            {
                World = new WorldId(new Id128(Domain, worldOrdinal));
                Resources = new ResourceLedger();
                Jobs = new JobFenceRegistry();
                Quarantine = new QuarantineRegistry(maxEntries: 16, maxBytes: 0UL);
                Callbacks = new CallbackGate(World);
                Binding = new RecordingBinding();
                Teardown = new TeardownSequencer(Resources, Jobs, Quarantine, Callbacks, Binding);
                Instance = new PluginInstanceId(new Id128(Domain, 0x0100UL));
                Owner = new OwnerId(new Id128(Domain, 0x0110UL));
                Stamp = new ActivationStamp(InstallationGeneration.First, ActivationEpoch.First);
                Operation = new OperationId(World, new Id128(Domain, 0x0120UL), 1UL);
            }

            public WorldId World { get; }

            public ResourceLedger Resources { get; }

            public JobFenceRegistry Jobs { get; }

            public QuarantineRegistry Quarantine { get; }

            public CallbackGate Callbacks { get; }

            public RecordingBinding Binding { get; }

            public TeardownSequencer Teardown { get; }

            public PluginInstanceId Instance { get; }

            public OwnerId Owner { get; }

            public ActivationStamp Stamp { get; }

            public OperationId Operation { get; }

            /// <summary>Lease ids whose disposer throws, i.e. the injected disposer failure of row 8.</summary>
            public HashSet<Id128> ThrowingDisposals { get; } = new HashSet<Id128>();

            /// <summary>Lease ids in the order their disposer was attempted, which is the cleanup order evidence.</summary>
            public List<Id128> DisposalOrder { get; } = new List<Id128>();

            public ResourceKey ResourceKey(ulong ordinal) => new ResourceKey(new Id128(Domain, 0x0200UL + ordinal));

            public FactoryKey DisposerKey(ulong ordinal) => new FactoryKey(new Id128(Domain, 0x0300UL + ordinal), 1U);

            public StageId Stage(ulong ordinal) => new StageId(new Id128(Domain, 0x0400UL + ordinal));

            /// <summary>Registers the activation with the world's callback gate, as a publication would (P-047).</summary>
            public void Register()
            {
                Callbacks.RegisterActivation(Instance, Stamp.Generation, Stamp.ActivationEpoch);
            }

            public Id128 NextJobId()
            {
                nextJob++;
                return new Id128(Domain, 0x5000UL + nextJob);
            }

            /// <summary>
            /// Acquires one real lease into the ledger under this installation's activation stamp, with an explicit
            /// dependency edge, so the ledger's own retirement order is derived from the data (P-048).
            /// </summary>
            public ManagedResourceLease Acquire(ulong ordinal, Id128 dependsOn)
            {
                nextLease++;
                Id128 leaseId = new Id128(Domain, 0x6000UL + nextLease);
                var token = new AsyncWorkToken(
                    Operation,
                    Instance,
                    Stamp.Generation,
                    Stamp.ActivationEpoch,
                    (uint)ordinal);
                var lease = new ManagedResourceLease(
                    ResourceKey(ordinal),
                    leaseId,
                    token,
                    DisposerKey(ordinal),
                    null,
                    OnDispose);

                Assert.That(
                    Resources.Acquire(lease, WorldResourceKind.ManagedLease, Owner, Instance, (uint)ordinal, dependsOn),
                    Is.True);
                Assert.That(Resources.MarkReady(leaseId), Is.True, "the lease was registered and marked ready");
                return lease;
            }

            /// <summary>
            /// The real cleanup event of one lease. The order is recorded so a case can prove that a failure did
            /// not stop the remaining attempts, and an injected failure is exactly the row-8 disposer.
            /// </summary>
            private void OnDispose(Id128 leaseId)
            {
                DisposalOrder.Add(leaseId);
                if (ThrowingDisposals.Contains(leaseId))
                {
                    throw new InvalidOperationException(
                        "injected disposer failure: the release of " + leaseId.ToString() + " threw (TEST-016 row 8)");
                }
            }
        }

        private static bool HasId(IReadOnlyList<Id128> ids, Id128 candidate)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i].Equals(candidate))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// TEST-016 row 4. A job is still in flight when unload begins: the resource it may still reach is
        /// quarantined and NOT released, the teardown reports `TeardownBlocked` instead of a settled disposal, the
        /// failed and quarantined ids are reported, and a second pass while the job is unfinished still frees
        /// nothing. Completion of the job is the only event that allows the release.
        /// </summary>
        [Test]
        public void AJobHeldInFlightWhileUnloadBeginsIsFencedAndItsResourceQuarantined()
        {
            Assert.That(FaultCompilation.IsCompiledIn, Is.True, CompilationMessage);

            var rig = new Rig(0x4001UL);
            rig.Register();
            ManagedResourceLease lease = rig.Acquire(1UL, default(Id128));
            Id128 jobId = rig.NextJobId();
            Assert.That(
                rig.Jobs.Track(
                    jobId,
                    rig.Instance,
                    rig.Stage(1UL),
                    rig.DisposerKey(1UL),
                    AssemblyEpoch.First,
                    LogicalStepId.Zero,
                    new[] { lease.LeaseId }),
                Is.Not.Null);
            Assert.That(rig.Jobs.OutstandingCount, Is.EqualTo(1));
            Assert.That(rig.Resources.LiveLeaseCount, Is.EqualTo(1));

            TeardownReport report = rig.Teardown.Unload(rig.Instance, rig.Stamp, rig.Operation, true);

            // The fence step found the unfinished job and quarantined what it may still reach (P-047, P-048).
            Assert.That(report.BlockedByJobFence, Is.True);
            Assert.That(report.FencedResources.Count, Is.EqualTo(1));
            Assert.That(report.FencedResources[0].Equals(lease.LeaseId), Is.True, "the fenced resource is reported");
            Assert.That(report.OutstandingJobs, Is.EqualTo(1));
            Assert.That(report.Quarantined.Count, Is.EqualTo(1));
            Assert.That(report.Quarantined[0].Equals(lease.LeaseId), Is.True, "the quarantined id is reported");
            Assert.That(rig.Quarantine.Contains(lease.LeaseId), Is.True, "the resource is admitted to quarantine");
            Assert.That(rig.Quarantine.EntriesFor(rig.Instance).Count, Is.EqualTo(1));
            Assert.That(rig.Quarantine.EntriesFor(rig.Instance)[0].Reason, Does.Contain("P-047"));

            // Nothing was released: no disposer ran, the lease is not disposed and the report is not a settlement.
            Assert.That(report.Cleanup.Retired, Is.Empty, "a fenced resource is not released");
            Assert.That(report.Cleanup.Failed, Is.Empty, "no release was even attempted for it");
            Assert.That(rig.DisposalOrder, Is.Empty, "no disposer ran while the job is unfinished");
            Assert.That(lease.IsDisposed, Is.False);
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.TeardownBlocked));
            Assert.That(report.Blocked, Is.True);
            Assert.That(
                report.DisposeSettled,
                Is.False,
                "the report's settled-disposal flag is false, so a blocked teardown is never reported as disposed");
            Assert.That(rig.Resources.RetainedCountFor(rig.Instance), Is.EqualTo(1));
            Assert.That(rig.Resources.LiveLeaseCount, Is.EqualTo(1));
            Assert.That(rig.Jobs.IsOutstanding(jobId), Is.True);
            Assert.That(rig.Jobs.QuarantinedJobCount, Is.EqualTo(1), "the holding job is recorded as the reason");
            Assert.That(rig.Teardown.BlockedCount, Is.EqualTo(1));
            Assert.That(rig.Callbacks.LiveActivationCount, Is.EqualTo(0), "step 1 retired the activation (P-047)");

            // The essential rule: repeating the pass frees nothing, because elapsed time is not an authorisation.
            // Only the job's own completion ends the fence (P-048).
            TeardownReport second = rig.Teardown.Unload(rig.Instance, rig.Stamp, rig.Operation, true);
            Assert.That(second.Code, Is.EqualTo(DiagnosticCode.TeardownBlocked));
            Assert.That(second.DisposeSettled, Is.False);
            Assert.That(rig.DisposalOrder, Is.Empty, "a later pass still releases nothing");
            Assert.That(lease.IsDisposed, Is.False);
            Assert.That(rig.Teardown.PassCount, Is.EqualTo(2));
            Assert.That(rig.Teardown.BlockedCount, Is.EqualTo(2));

            // Each pass really walked the P-048 steps it reports: ingress closed once, the step boundary consulted
            // once, the users fenced once and the closure retracted once, per pass.
            Assert.That(rig.Binding.CloseCalls, Is.EqualTo(2), "one ingress closure per teardown pass");
            Assert.That(rig.Binding.SettleCalls, Is.EqualTo(2), "one step settlement per pass, and settleStep was true");
            Assert.That(rig.Binding.FenceCalls, Is.EqualTo(2), "one fence pass per attempt");
            Assert.That(rig.Binding.RetractCalls, Is.EqualTo(2), "one closure retraction per attempt");

            // Completion is the event that ends the fence; the explicit retry may then release exactly this lease.
            Assert.That(rig.Jobs.Complete(jobId), Is.True);
            Assert.That(rig.Jobs.IsOutstanding(jobId), Is.False);
            Assert.That(rig.Jobs.IsResourceFenced(lease.LeaseId), Is.False, "a completed job no longer fences anything");

            CleanupReport released = rig.Teardown.ReleaseQuarantineFor(rig.Instance);

            Assert.That(released.Retired.Count, Is.EqualTo(1));
            Assert.That(released.Retired[0].Equals(lease.LeaseId), Is.True);
            Assert.That(released.Failed, Is.Empty);
            Assert.That(released.Quarantined, Is.Empty);
            Assert.That(rig.DisposalOrder.Count, Is.EqualTo(1), "the disposer ran exactly once, after the job ended");
            Assert.That(rig.DisposalOrder[0].Equals(lease.LeaseId), Is.True);
            Assert.That(lease.IsDisposed, Is.True, "the buffered release happens once the user ended (P-048)");
            Assert.That(lease.DisposeCount, Is.EqualTo(1), "a lease is disposed at most once (P-048)");
            Assert.That(rig.Quarantine.Count, Is.EqualTo(0));
            Assert.That(rig.Resources.RetainedCountFor(rig.Instance), Is.EqualTo(0));
            Assert.That(rig.Resources.LiveLeaseCount, Is.EqualTo(0));
        }

        /// <summary>
        /// TEST-016 row 8. Five resources are acquired in one dependency chain and one disposer throws. Every one of
        /// the five is still attempted (the failure is not a stop condition), the failed id is reported in the
        /// cleanup's failed list, the resource stays retained and quarantined instead of being reported disposed,
        /// and the cleanup report distinguishes the retained case (`HasCleanupErrors` and `Blocked`). The same rule
        /// is then asserted over the world resource ledger, whose retirement order is derived from the dependency
        /// data, so the dependent resources are attempted even though the base resource is the one that fails.
        /// </summary>
        [Test]
        public void AThrowingDisposerIsRecordedAndOtherCleanupStillProceeds()
        {
            Assert.That(FaultCompilation.IsCompiledIn, Is.True, CompilationMessage);

            var rig = new Rig(0x4002UL);
            rig.Register();

            const int resourceCount = 5;
            var leases = new ManagedResourceLease[resourceCount];
            Id128 dependsOn = default(Id128);
            for (int i = 0; i < resourceCount; i++)
            {
                leases[i] = rig.Acquire((ulong)(i + 1), dependsOn);
                dependsOn = leases[i].LeaseId;
            }

            // The middle resource of the chain is the one whose disposer throws.
            const int failingIndex = 2;
            rig.ThrowingDisposals.Add(leases[failingIndex].LeaseId);

            TeardownReport report = rig.Teardown.Unload(rig.Instance, rig.Stamp, rig.Operation, false);

            // Every independent cleanup was attempted: each lease's real disposer ran exactly once.
            Assert.That(rig.DisposalOrder.Count, Is.EqualTo(resourceCount), "every resource's cleanup was attempted");
            int attempts = 0;
            for (int i = 0; i < resourceCount; i++)
            {
                attempts += leases[i].DisposeCount;
            }

            Assert.That(attempts, Is.EqualTo(resourceCount), "each disposer was attempted exactly once");

            // Retirement runs in reverse acquisition order, so the failing resource is the third attempt and the
            // two resources *after* it in that order were still attempted: one throwing disposer does not stop the
            // rest of the cleanup (P-048).
            Assert.That(rig.DisposalOrder[0].Equals(leases[4].LeaseId), Is.True);
            Assert.That(rig.DisposalOrder[1].Equals(leases[3].LeaseId), Is.True);
            Assert.That(rig.DisposalOrder[2].Equals(leases[failingIndex].LeaseId), Is.True, "the failing attempt");
            Assert.That(rig.DisposalOrder[3].Equals(leases[1].LeaseId), Is.True, "cleanup continued after the failure");
            Assert.That(rig.DisposalOrder[4].Equals(leases[0].LeaseId), Is.True);

            // The failure is recorded, aggregated and not disguised as a disposal.
            Assert.That(report.HasCleanupErrors, Is.True);
            Assert.That(report.Cleanup.HasCleanupErrors, Is.True);
            Assert.That(report.Cleanup.Failed.Count, Is.EqualTo(1));
            Assert.That(report.Cleanup.Failed[0].Equals(leases[failingIndex].LeaseId), Is.True);
            Assert.That(report.FailedReleases.Count, Is.EqualTo(1));
            Assert.That(report.FailedReleases[0].Equals(leases[failingIndex].LeaseId), Is.True);
            Assert.That(HasId(report.Quarantined, leases[failingIndex].LeaseId), Is.True);

            Assert.That(leases[failingIndex].IsDisposed, Is.False, "a failed release is never reported as a disposal");
            Assert.That(leases[0].IsDisposed, Is.True);
            Assert.That(leases[1].IsDisposed, Is.True);
            Assert.That(leases[3].IsDisposed, Is.True);
            Assert.That(leases[4].IsDisposed, Is.True);
            Assert.That(report.Cleanup.Retired.Count, Is.EqualTo(resourceCount - 1));

            // This pass ran with `settleStep` false (the caller is the publication boundary), so the world is asked
            // for the other three steps and never asked to settle a step it is inside of (P-030, P-048).
            Assert.That(rig.Binding.CloseCalls, Is.EqualTo(1));
            Assert.That(rig.Binding.SettleCalls, Is.EqualTo(0), "a publication-boundary teardown does not settle a step");
            Assert.That(rig.Binding.FenceCalls, Is.EqualTo(1));
            Assert.That(rig.Binding.RetractCalls, Is.EqualTo(1));
            Assert.That(
                report.Cleanup.Blocked,
                Is.True,
                "the cleanup report distinguishes the retained case from a clean one");

            // Ownership that cannot be proven safe stays retained and quarantined rather than silently freed.
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.TeardownBlocked));
            Assert.That(report.DisposeSettled, Is.False);
            Assert.That(rig.Quarantine.Contains(leases[failingIndex].LeaseId), Is.True);
            Assert.That(rig.Quarantine.EntriesFor(rig.Instance).Count, Is.EqualTo(1));
            Assert.That(rig.Quarantine.EntriesFor(rig.Instance)[0].Reason, Does.Contain("P-048"));
            Assert.That(rig.Resources.RetainedCountFor(rig.Instance), Is.EqualTo(1));
            Assert.That(rig.Resources.LiveLeaseCount, Is.EqualTo(1));
            Assert.That(rig.Resources.FailedReleaseCount, Is.EqualTo(1));
            Assert.That(rig.Resources.RetiredCount, Is.EqualTo(resourceCount - 1));

            // The same rule over the world resource ledger, whose retirement order comes from the dependency data:
            // two dependents of one base resource are both attempted before the failing base is quarantined.
            var worldLedger = new WorldResourceLedger(rig.World);
            Id128 baseId = worldLedger.Acquire(
                WorldResourceKind.ManagedLease,
                rig.ResourceKey(11UL),
                rig.Owner,
                rig.Instance,
                default(Id128),
                0UL);
            Id128 dependentOne = worldLedger.Acquire(
                WorldResourceKind.ManagedLease,
                rig.ResourceKey(12UL),
                rig.Owner,
                rig.Instance,
                baseId,
                0UL);
            Id128 dependentTwo = worldLedger.Acquire(
                WorldResourceKind.ManagedLease,
                rig.ResourceKey(13UL),
                rig.Owner,
                rig.Instance,
                baseId,
                0UL);

            RetirementOutcome outcome = worldLedger.RetireAll(baseId);

            Assert.That(outcome.RetiredCount, Is.EqualTo(2), "both dependents were retired");
            Assert.That(outcome.FailedCount, Is.EqualTo(1), "the failing resource is reported, not hidden");
            Assert.That(outcome.QuarantinedCount, Is.EqualTo(1));
            Assert.That(outcome.AllRetired, Is.False, "a retained resource is never a settled retirement");
            Assert.That(worldLedger.DisposalAttemptCount, Is.EqualTo(3), "every independent cleanup was attempted");
            Assert.That(worldLedger.RetainedResourceCount, Is.EqualTo(1));
            Assert.That(worldLedger.TryGetResource(baseId, out WorldResourceRecord baseRecord), Is.True);
            Assert.That(baseRecord.State, Is.EqualTo(ResourceRetirementState.Quarantined));
            Assert.That(baseRecord.IsRetained, Is.True);
            Assert.That(worldLedger.TryGetResource(dependentOne, out WorldResourceRecord one), Is.True);
            Assert.That(one.State, Is.EqualTo(ResourceRetirementState.Retired));
            Assert.That(worldLedger.TryGetResource(dependentTwo, out WorldResourceRecord two), Is.True);
            Assert.That(two.State, Is.EqualTo(ResourceRetirementState.Retired));
        }
    }
}

// GameCore.Composition tests — the GC-022 lifecycle stress rig.
//
// GC-022 asks for 1,000 mount/unmount cycles, 100 delayed completions, stalled jobs, throwing disposers and
// required-provider churn, with every acquisition traced to retirement or quarantine and every live counter back
// at baseline afterwards (09-implementation-guide.md#gc-022; TEST-015, TEST-016, TEST-023).
//
// This rig exists because those quantities are properties of the *lifecycle mechanism* — the installation state
// machine, the resource ledger, the tracked-job fence, the quarantine registry, the callback gate and the P-048
// teardown sequencer — and those live in `GameCore.Composition`, which is engine-free by construction
// (docs/game-core/01-architecture.md s1). So the 1,000 counted cycles run here, on the real production types, and
// the Unity-world suites of the same task prove the same mechanism through a real `UnityWorldHost`'s own
// ledgers (`GameCore.LifecycleStress.Tests`, and `-probeLifecycleStress` in the IL2CPP player).
//
// Three rules shape the rig:
//
//   * It drives the production types, not a model of them. A cycle performs exactly the identity, gate and
//     acquisition steps a mount publication performs (`ActivationLedger.Activate`,
//     `CallbackGate.RegisterActivation`, `ResourceLedger.Acquire` + `MarkReady`), and the unmount goes through the
//     production `InstallationLifecycleCoordinator.Unload`, which is the same P-048 path an O-07 removal takes.
//   * Every acquisition is a real `ManagedResourceLease` prepared by a real `IManagedResourceFactory`, acquired
//     under the installation's own `AsyncWorkToken`, so the ledger's activation-stamp filter — the thing that
//     makes a stale lease unretirable — is actually exercised.
//   * Nothing here reads a clock. P-048 says elapsed time never authorizes a release, so the rig has no time
//     source to accidentally depend on.
//
// The rig owns no assertion of its own: it reports what the ledgers say, and the tests assert it.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Composition;

namespace GameCore.Composition.Tests
{
    /// <summary>
    /// The five resource roles TEST-015 names for each plugin: a service lease, a system registration, an asset
    /// lease, a callback and a subscription. The role is what the trace reports; the kind is what the ledger
    /// records, and the two are deliberately not one-to-one:
    /// </summary>
    public enum LifecycleStressRole
    {
        /// <summary>A resolved service binding held for the activation's lifetime (kind `ManagedLease`).</summary>
        ServiceLease = 0,

        /// <summary>A subscription behind a closed gate until publication (kind `Subscription`).</summary>
        Subscription = 1,

        /// <summary>A registered system instance (kind `SystemRegistration`).</summary>
        SystemRegistration = 2,

        /// <summary>An asset lease keeping a native resource alive (kind `NativeContainer`).</summary>
        AssetLease = 3,

        /// <summary>A managed callback delivered through a gated lease (kind `ManagedLease`).</summary>
        Callback = 4,
    }

    /// <summary>One mount's activation edge together with the stamp its leases are acquired under.</summary>
    public readonly struct StressActivation
    {
        public readonly ActivationStamp Stamp;
        public readonly LifecycleTransition Transition;

        public StressActivation(ActivationStamp stamp, LifecycleTransition transition)
        {
            Stamp = stamp;
            Transition = transition;
        }
    }

    /// <summary>One acquisition of one cycle: what was acquired, under which identity, and where it ended.</summary>
    public readonly struct StressLease
    {
        public readonly LifecycleStressRole Role;
        public readonly Id128 LeaseId;
        public readonly ResourceKey Key;
        public readonly WorldResourceKind Kind;
        public readonly uint Ordinal;

        public StressLease(LifecycleStressRole role, Id128 leaseId, ResourceKey key, WorldResourceKind kind, uint ordinal)
        {
            Role = role;
            LeaseId = leaseId;
            Key = key;
            Kind = kind;
            Ordinal = ordinal;
        }

        public override string ToString() =>
            Role + "(" + LeaseId.ToString() + ", " + Kind + ", ordinal="
            + Ordinal.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>What one completed mount/unmount cycle observed, so a failure names the cycle it happened in.</summary>
    public sealed class LifecycleStressCycle
    {
        public LifecycleStressCycle(
            int index,
            PluginInstanceId instance,
            ActivationStamp stamp,
            LifecycleTransition activation,
            IReadOnlyList<StressLease> leases,
            TeardownReport teardown)
        {
            Index = index;
            Instance = instance;
            Stamp = stamp;
            Activation = activation;
            Leases = leases;
            Teardown = teardown;
        }

        public int Index { get; }

        public PluginInstanceId Instance { get; }

        public ActivationStamp Stamp { get; }

        /// <summary>The mount's activation edge; `Allowed` is false only when the cycle was refused (P-046).</summary>
        public LifecycleTransition Activation { get; }

        /// <summary>Every acquisition of this cycle, in acquisition order.</summary>
        public IReadOnlyList<StressLease> Leases { get; }

        /// <summary>The unmount's P-048 pass (O-07).</summary>
        public TeardownReport Teardown { get; }

        /// <summary>True when the whole cycle published, settled and retained nothing.</summary>
        public bool Settled =>
            Activation.Allowed &&
            Teardown.Code == DiagnosticCode.None &&
            Teardown.DisposeSettled &&
            Teardown.Quarantined.Count == 0 &&
            Teardown.FailedReleases.Count == 0 &&
            Teardown.Cleanup.Retired.Count == Leases.Count;

        public string Describe() =>
            "cycle=" + Index.ToString(CultureInfo.InvariantCulture)
            + " instance=" + Instance.ToString()
            + " activationAllowed=" + (Activation.Allowed ? "True" : "False")
            + " leases=" + Leases.Count.ToString(CultureInfo.InvariantCulture)
            + "\n" + Teardown.Describe();
    }

    /// <summary>
    /// A managed-resource factory whose leases are counted, whose resource keys are deterministic, and whose
    /// disposal can be made to throw for one named resource — the "one disposer throws" row of the 06 s5 failure
    /// matrix. It is a separate type from the composition suite's `TestResourceFactory` because the stress rig
    /// needs per-role keys and a scripted failure that survives being read back from the ledger.
    /// </summary>
    public sealed class StressResourceFactory : IManagedResourceFactory
    {
        /// <summary>Bytes a prepared lease claims; the composition ledger does not record bytes, so this is 0 there.</summary>
        public const ulong LeaseBytes = 0UL;

        private static readonly FrozenPayload EmptyConfig = new FrozenPayload(new byte[] { 7 });

        private readonly IdFactory ids;
        private readonly List<ManagedResourceLease> leases = new List<ManagedResourceLease>();

        public StressResourceFactory(IdFactory ids)
        {
            this.ids = ids ?? throw new ArgumentNullException(nameof(ids));
        }

        public int PrepareCount { get; private set; }

        /// <summary>Disposals that completed; a failed release is counted in `FailedDisposalCount` instead.</summary>
        public int DisposeCount { get; private set; }

        public int FailedDisposalCount { get; private set; }

        /// <summary>Resource keys whose disposer throws; the lease then stays retained (P-048).</summary>
        public HashSet<Id128> FailingDisposals { get; } = new HashSet<Id128>();

        /// <summary>Resource keys in disposal order, so reverse-acquisition-order teardown is observable.</summary>
        public List<Id128> DisposedOrder { get; } = new List<Id128>();

        /// <summary>Resource keys whose disposal threw, in the order they threw.</summary>
        public List<Id128> FailedDisposalOrder { get; } = new List<Id128>();

        public FactoryKey DisposerKey { get; set; } =
            new FactoryKey(new Id128(0x6C69667374726573UL, 1UL), 1U);

        public IReadOnlyList<ManagedResourceLease> Leases => leases;

        public IManagedResourceLease Prepare(ManagedResourceRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            PrepareCount++;
            ResourceKey key = request.Resource;
            Id128 leaseId = ids.NextId();
            var lease = new ManagedResourceLease(
                key,
                leaseId,
                request.Token,
                DisposerKey,
                new ManagedResourceGate(),
                disposed => OnDisposed(key, disposed));
            leases.Add(lease);
            return lease;
        }

        private void OnDisposed(ResourceKey key, Id128 leaseId)
        {
            _ = leaseId;
            DisposedOrder.Add(key.Value);
            if (FailingDisposals.Contains(key.Value))
            {
                FailedDisposalOrder.Add(key.Value);
                FailedDisposalCount++;
                throw new InvalidOperationException("Scripted disposal failure for resource " + key.ToString() + " (06 s5 one-disposer-throws).");
            }

            DisposeCount++;
        }
    }

    /// <summary>
    /// One composition lane's lifecycle mechanism under stress: the real activation ledger, resource ledger,
    /// tracked-job fence, quarantine registry, callback gate, P-048 teardown sequencer and lifecycle coordinator,
    /// with no live ECS storage behind them.
    /// </summary>
    public sealed class LifecycleStressRig
    {
        /// <summary>The five roles TEST-015 requires per plugin, in acquisition order.</summary>
        public static readonly LifecycleStressRole[] RolesInAcquisitionOrder =
        {
            LifecycleStressRole.ServiceLease,
            LifecycleStressRole.Subscription,
            LifecycleStressRole.SystemRegistration,
            LifecycleStressRole.AssetLease,
            LifecycleStressRole.Callback,
        };

        public LifecycleStressRig(ulong domain, int quarantineCapacity)
        {
            Ids = new IdFactory(domain);
            World = new WorldId(new Id128(domain, 0x4C535452455353UL));
            Factory = new StressResourceFactory(Ids);
            Callbacks = new CallbackGate(World);
            Coordinator = new InstallationLifecycleCoordinator(
                World,
                new ResourceLedger(),
                Callbacks,
                new LifecycleSettings(quarantineCapacity, 0UL),
                null);
            Issuer = new OperationIssuer(World, new Id128(domain, 1UL));
        }

        public IdFactory Ids { get; }

        public WorldId World { get; }

        public StressResourceFactory Factory { get; }

        public CallbackGate Callbacks { get; }

        public InstallationLifecycleCoordinator Coordinator { get; }

        public OperationIssuer Issuer { get; }

        public ResourceLedger Resources => Coordinator.Resources;

        public JobFenceRegistry Jobs => Coordinator.Jobs;

        public QuarantineRegistry Quarantine => Coordinator.Quarantine;

        public ActivationLedger Activations => Coordinator.Activations;

        public TeardownSequencer Teardown => Coordinator.Teardown;

        /// <summary>Ledger kind one role is tracked as; the mapping is asserted, never assumed by a caller.</summary>
        public static WorldResourceKind KindOf(LifecycleStressRole role)
        {
            switch (role)
            {
                case LifecycleStressRole.ServiceLease:
                case LifecycleStressRole.Callback:
                    return WorldResourceKind.ManagedLease;
                case LifecycleStressRole.Subscription:
                    return WorldResourceKind.Subscription;
                case LifecycleStressRole.SystemRegistration:
                    return WorldResourceKind.SystemRegistration;
                case LifecycleStressRole.AssetLease:
                    return WorldResourceKind.NativeContainer;
                default:
                    throw new ArgumentOutOfRangeException(nameof(role), "No ledger kind is declared for " + role + ".");
            }
        }

        /// <summary>
        /// One mount publication's identity and gate half: the activation edge plus the callback-gate
        /// registration, exactly as `InstallationLifecycleCoordinator.ApplyTransition` performs a mount
        /// (ActivationLedger.cs `Activate`; InstallationLifecycleCoordinator.cs:615-640).
        /// </summary>
        public StressActivation Activate(PluginInstanceId instance, ulong generation, ulong epoch, OperationId operation)
        {
            LifecycleTransition transition = Activations.Activate(
                instance,
                new InstallationGeneration(generation),
                new ActivationEpoch(epoch),
                operation);
            var stamp = new ActivationStamp(new InstallationGeneration(generation), new ActivationEpoch(epoch));
            if (transition.Allowed)
            {
                Callbacks.RegisterActivation(instance, stamp.Generation, stamp.ActivationEpoch);
            }

            return new StressActivation(stamp, transition);
        }

        public IReadOnlyList<StressLease> AcquireAll(
            PluginInstanceId instance,
            ActivationStamp stamp,
            OperationId operation,
            int ordinalBase)
        {
            var acquired = new List<StressLease>(RolesInAcquisitionOrder.Length);
            for (int i = 0; i < RolesInAcquisitionOrder.Length; i++)
            {
                acquired.Add(Acquire(
                    instance,
                    stamp,
                    operation,
                    RolesInAcquisitionOrder[i],
                    (uint)(ordinalBase + i)));
            }

            return acquired;
        }

        /// <summary>
        /// One gated acquisition: prepare a real lease, record it in the ledger under the installation's own
        /// activation stamp, then publish it. A staged lease that was never `MarkReady` is the P-029 case and is
        /// deliberately not what this helper builds.
        /// </summary>
        public StressLease Acquire(
            PluginInstanceId instance,
            ActivationStamp stamp,
            OperationId operation,
            LifecycleStressRole role,
            uint ordinal)
        {
            ResourceKey key = Ids.Resource();
            var token = new AsyncWorkToken(operation, instance, stamp.Generation, stamp.ActivationEpoch, ordinal);
            var lease = (ManagedResourceLease)Factory.Prepare(new ManagedResourceRequest(key, token, EmptyConfig));
            WorldResourceKind kind = KindOf(role);
            if (!Resources.Acquire(lease, kind, Ids.Owner(), instance, ordinal, default(Id128)))
            {
                throw new InvalidOperationException(
                    "The ledger refused acquisition " + ordinal.ToString(CultureInfo.InvariantCulture)
                    + " for " + instance.ToString() + ": one lease id identifies one acquisition.");
            }

            if (!Resources.MarkReady(lease.LeaseId))
            {
                throw new InvalidOperationException(
                    "The ledger refused to publish lease " + lease.LeaseId.ToString() + " acquired by " + instance.ToString() + ".");
            }

            return new StressLease(role, lease.LeaseId, key, kind, ordinal);
        }

        /// <summary>
        /// One complete mount/unmount cycle: activate a fresh installation identity, acquire the five roles, then
        /// unmount through the production coordinator so the P-048 order runs for real.
        /// </summary>
        public LifecycleStressCycle MountAndUnload(int index)
        {
            PluginInstanceId instance = Ids.Instance();
            OperationId mount = Issuer.Next();
            OperationId unmount = Issuer.Next();
            StressActivation activation = Activate(instance, (ulong)index + 1UL, 1UL, mount);
            IReadOnlyList<StressLease> leases = AcquireAll(instance, activation.Stamp, mount, 1);
            TeardownReport teardown = Coordinator.Unload(instance, unmount);
            return new LifecycleStressCycle(index, instance, activation.Stamp, activation.Transition, leases, teardown);
        }

        /// <summary>Every ledger record of one installation, in acquisition order, with its retirement state.</summary>
        public IReadOnlyList<WorldResourceRecord> RecordsOf(PluginInstanceId instance)
        {
            IReadOnlyList<WorldResourceRecord> all = Resources.Records();
            var mine = new List<WorldResourceRecord>();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Instance.Equals(instance))
                {
                    mine.Add(all[i]);
                }
            }

            return mine;
        }

        /// <summary>
        /// How many ledger records are still retained by the installation. Zero proves the acquisition was traced
        /// all the way to retirement or to a declared quarantine (P-048).
        /// </summary>
        public int RetainedRecordsOf(PluginInstanceId instance)
        {
            IReadOnlyList<WorldResourceRecord> mine = RecordsOf(instance);
            int retained = 0;
            for (int i = 0; i < mine.Count; i++)
            {
                if (mine[i].IsRetained)
                {
                    retained++;
                }
            }

            return retained;
        }

        /// <summary>A completion stamped by this world for one installation's activation (P-007, O-24).</summary>
        public AsyncWorkToken TokenFor(PluginInstanceId instance, ActivationStamp stamp, OperationId operation, uint ordinal) =>
            new AsyncWorkToken(operation, instance, stamp.Generation, stamp.ActivationEpoch, ordinal);

        /// <summary>Frozen configuration of every prepared lease in this rig; immutable, so it is shared safely.</summary>
        private static readonly FrozenPayload EmptyConfig = new FrozenPayload(new byte[] { 7 });
    }

}

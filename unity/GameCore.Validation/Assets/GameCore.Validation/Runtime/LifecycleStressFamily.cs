// GameCore.Validation.ProbeHost - the GC-022 Unity-world lifecycle-stress family contract.
//
// The Wave 6 task sentence, verbatim from `docs/game-core/09-implementation-guide.md` (GC-022):
//
//   "Run 1,000 mount/unmount cycles, 100 delayed completions, stalled jobs, throwing disposers and required-provider
//    churn. Exercise domain reload on/off plus scene reload settings, stop/recreate and headless cleanup. Trace every
//    acquisition to retirement or quarantine."
//
// One runner, two family adapters - the shape every earlier gate in this repository uses. The stress runner owns the
// scripted sequence, the cycle count and the observations; a family owns only what its genre declares, so both genres
// are driven through exactly the same sequence (P-001, P-002):
//
//   * `ILifecycleStressFamily` is the whole surface the runner needs from one genre. It is `IGc019Family` because the
//     runner builds a *real* world of that genre - the same world the GC-013/GC-018/GC-019 and gate runs build - and
//     an `IGc019Family` already declares the genre's compiled schedule, its live targets, its provider mounts and the
//     genre's own stage runtime (`AttachStageRuntime`, which attaches the genre's real module over the compiled
//     schedule). No new world-building surface is introduced for the stress.
//   * on top of that a family declares the four manifests the stress mounts beyond its catalog declarations (the
//     cyclic installation, the required-service pair and the installation whose staged lease a stalled job fences),
//     the stage and dispatch key a tracked job is recorded under, the plugin-instance identity mint the cycles use (a
//     fresh identity per cycle, P-004/P-005) and the two frozen digest literals of its runs.
//
// The shared runtime pieces below are the two places the runner cannot use the family's own fixtures:
//
//   * `LifecycleStressManifestSource` - the manifests the stress mounts are *not* members of the family's generated
//     catalog (this wave declares them, exactly as `NarrativeLifecycleScenario` declares its own service pair). The
//     source resolves the catalog first and its own additions second, and reports a miss instead of substituting one
//     (P-009).
//   * `LifecycleStressResourceFactory` and `ScriptedResourceLease` - the managed-resource half of a stress run. It
//     implements the declared seam `IManagedResourceFactory`/`IManagedResourceLease` with the same observable
//     sequence `ManagedResourceLease` has (a prepared lease sits behind a closed `ManagedResourceGate`; publication is
//     the only moment it opens; a lease is disposed at most once), and adds the two things a stress needs:
//     deterministic lease identities with a recorded disposal order, and a scripted failure that fails exactly one
//     disposal attempt (P-007, P-048). A release that threw leaves the lease retained - the composition ledger
//     quarantines it and independent cleanup continues with the other leases - and the explicit later release P-048
//     allows is the one that retires it, because this lease does not permanently refuse a retry.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Unity.Runtime.Integration;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// The four installations and the two dispatch identities one family's lifecycle-stress run needs beyond its
    /// catalog declarations. Every member is a real <see cref="PluginManifest"/> the real lane resolves and the real
    /// mount path installs; none of them declares a stage, a buffer, a state slot or a resource of its own, so none of
    /// them enters the family's compiled schedule (the same shape the narrative lifecycle scenario's service pair
    /// uses).
    /// </summary>
    public sealed class LifecycleStressDeclarations
    {
        private readonly List<PluginManifest> all;

        public LifecycleStressDeclarations(
            PluginManifest installation,
            PluginManifest serviceConsumer,
            PluginManifest serviceProvider,
            PluginManifest fenced,
            StageId stage,
            FactoryKey system)
        {
            Installation = installation ?? throw new ArgumentNullException(nameof(installation));
            ServiceConsumer = serviceConsumer ?? throw new ArgumentNullException(nameof(serviceConsumer));
            ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            Fenced = fenced ?? throw new ArgumentNullException(nameof(fenced));
            Stage = stage;
            System = system;

            all = new List<PluginManifest> { Installation, ServiceConsumer, ServiceProvider, Fenced };
        }

        /// <summary>The installation the counted mount/unmount cycles mount, once per cycle with a fresh identity.</summary>
        public PluginManifest Installation { get; }

        /// <summary>The required service's consumer: one capability of its own plus a required dependency (P-011).</summary>
        public PluginManifest ServiceConsumer { get; }

        /// <summary>The required service's provider: the export the consumer binds to, and whose removal makes it wait.</summary>
        public PluginManifest ServiceProvider { get; }

        /// <summary>The installation whose staged lease a tracked job keeps alive through its teardown (P-047).</summary>
        public PluginManifest Fenced { get; }

        /// <summary>The stage a tracked stress job is recorded under; the family's own first stage.</summary>
        public StageId Stage { get; }

        /// <summary>The dispatch key a tracked stress job is recorded under; the family's own first system.</summary>
        public FactoryKey System { get; }

        /// <summary>The four manifests in the order the source adds them; the run's own declaration set.</summary>
        public IReadOnlyList<PluginManifest> All => all;

        public override string ToString() =>
            "lifecycleStressDeclarations(installation=" + Installation.PluginTypeId.ToString()
            + ", consumer=" + ServiceConsumer.PluginTypeId.ToString()
            + ", provider=" + ServiceProvider.PluginTypeId.ToString()
            + ", fenced=" + Fenced.PluginTypeId.ToString() + ")";
    }

    /// <summary>
    /// One genre's declared facts for the GC-022 Unity-world lifecycle stress: everything the adapter gate already
    /// declares (so the stress drives the genre's own real world and its own real module), plus the lifecycle-stress
    /// declarations, the per-cycle instance identity and the mount/unmount payload builders.
    ///
    /// Every member is data or a payload: the runner owns the cycles, the completions, the fences and the
    /// observations, so both genres are driven through exactly the same sequence (P-001).
    /// </summary>
    public interface ILifecycleStressFamily : IGc019Family
    {
        /// <summary>The manifests and dispatch identities this run mounts beyond the family's catalog declarations.</summary>
        LifecycleStressDeclarations StressDeclarations { get; }

        /// <summary>
        /// Digest the generated-catalog run must report over its twelve named observations, all passing: the
        /// `NarrativeDigest.OfLines` value over `&lt;label&gt;/lifecycle-stress-...=pass` lines in the frozen order
        /// (P-008, P-028). It is computed from the frozen name table, never read from a run.
        /// </summary>
        string GeneratedCatalogDigest { get; }

        /// <summary>The fixture-catalog run's literal, computed the same way over `fixture:`-prefixed names.</summary>
        string FixtureCatalogDigest { get; }

        /// <summary>
        /// One fresh plugin-instance identity of this genre, from a small ordinal. A cycle never reuses another
        /// cycle's identity (P-004, P-005): a returned installation mounts under a new identity, and only the delayed
        /// completions step deliberately remounts an identity it already used, to reach a new generation.
        /// </summary>
        PluginInstanceId StressInstance(ulong ordinal);

        /// <summary>The genre's own O-03 mount payload for one manifest at one scope (P-020).</summary>
        CompositionEditPayload StressMount(PluginManifest manifest, PluginInstanceId instance, ScopeId scope);

        /// <summary>The genre's own O-07 unmount payload for one installation (P-046, P-048).</summary>
        CompositionEditPayload StressUnmount(PluginInstanceId instance);
    }

    /// <summary>
    /// The manifest source of one stress run: the family's catalog-backed declarations first, then this run's own four
    /// manifests. A miss is reported, never substituted (P-009). The added manifests declare the family's catalog
    /// configuration schema, so the defaults lookup stays the catalog's own answer.
    /// </summary>
    public sealed class LifecycleStressManifestSource : IPluginManifestSource
    {
        private readonly CatalogManifestSource catalog;
        private readonly Dictionary<Id128, PluginManifest> added = new Dictionary<Id128, PluginManifest>();
        private readonly List<PluginManifest> order = new List<PluginManifest>();

        public LifecycleStressManifestSource(CatalogManifestSource catalog)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        /// <summary>The manifests this run added, in insertion order (the run's own declaration set).</summary>
        public IReadOnlyList<PluginManifest> Added => order;

        public void Add(PluginManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            if (!added.ContainsKey(manifest.PluginTypeId.Value))
            {
                order.Add(manifest);
            }

            added[manifest.PluginTypeId.Value] = manifest;
        }

        public bool TryGetManifest(PluginTypeId pluginType, out PluginManifest? manifest)
        {
            if (added.TryGetValue(pluginType.Value, out PluginManifest? found))
            {
                manifest = found;
                return true;
            }

            return catalog.TryGetManifest(pluginType, out manifest);
        }

        public bool TryGetConfigDefaults(SchemaRef schema, out ConfigDocument? defaults) =>
            catalog.TryGetConfigDefaults(schema, out defaults);
    }

    /// <summary>
    /// The gated managed lease one stress run prepares: the declared `IManagedResourceLease` seam with the observable
    /// sequence `ManagedResourceLease` has - pending behind a closed <see cref="ManagedResourceGate"/> until
    /// publication opens it, disposed at most once, and closed irreversibly at disposal - plus one property the stress
    /// needs: a disposal attempt that throws leaves the lease retained and NOT disposed, so the explicit later release
    /// P-048 allows can run the disposal again and retire it exactly once.
    /// </summary>
    public sealed class ScriptedResourceLease : IManagedResourceLease
    {
        private readonly Action<Id128>? onDispose;
        private bool disposed;

        public ScriptedResourceLease(
            ResourceKey resource,
            Id128 leaseId,
            AsyncWorkToken token,
            FactoryKey disposer,
            IResourceGate? gate,
            Action<Id128>? onDispose)
        {
            Resource = resource;
            LeaseId = leaseId;
            Token = token;
            Disposer = disposer;
            Gate = gate ?? new ManagedResourceGate();
            this.onDispose = onDispose;
        }

        public ResourceKey Resource { get; }

        public Id128 LeaseId { get; }

        public AsyncWorkToken Token { get; }

        /// <summary>
        /// Pending while the lease is staged, `Ready` once publication opened its gate, `Retiring` after disposal:
        /// the same three states in the same order <see cref="ManagedResourceLease"/> reports (P-029, P-030).
        /// </summary>
        public ResourceReadiness Readiness =>
            disposed ? ResourceReadiness.Retiring
                : (Gate.IsOpen ? ResourceReadiness.Ready : ResourceReadiness.Pending);

        public FactoryKey Disposer { get; }

        /// <summary>Inert until the owning activation publishes; consumed callbacks re-check it (P-047).</summary>
        public IResourceGate Gate { get; }

        public bool IsDisposed => disposed;

        /// <summary>
        /// The one permitted disposal. A disposer that throws leaves the lease undisposed and retained, so a later
        /// explicit release is a second *attempt*, never a second disposal of a resource reported as released (P-048).
        /// </summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            Gate.Close();
            if (onDispose != null)
            {
                onDispose(LeaseId);
            }

            disposed = true;
        }

        public override string ToString() =>
            "scriptedLease(" + LeaseId.ToString() + ", " + Resource.ToString()
            + (disposed ? ", disposed" : ", retained") + ")";
    }

    /// <summary>
    /// The managed-resource factory of one stress run: deterministic lease identities, a counted preparation, a
    /// recorded disposal order and a scripted failure that fails exactly one disposal attempt (P-007, P-048).
    ///
    /// A release that throws is never reported as a disposal: the lease stays retained, the composition ledger
    /// quarantines it and independent cleanup continues with the other leases.
    /// </summary>
    public sealed class LifecycleStressResourceFactory : IManagedResourceFactory
    {
        /// <summary>Category salt of this factory's lease identities; distinct from the world and job categories.</summary>
        public const ulong LeaseIdSalt = 0x4C5354524C454153UL;

        private readonly List<ScriptedResourceLease> leases = new List<ScriptedResourceLease>();
        private ulong leaseSequence;

        /// <summary>Leases prepared by this factory.</summary>
        public int PrepareCount { get; private set; }

        /// <summary>Disposals that ran to completion.</summary>
        public int DisposeCount { get; private set; }

        /// <summary>Disposal attempts that threw; their leases stay retained until an explicit later release.</summary>
        public int FailedDisposalCount { get; private set; }

        /// <summary>Lease ids whose disposer throws once; an entry is removed when it fires, so the retry succeeds.</summary>
        public HashSet<Id128> FailingDisposals { get; } = new HashSet<Id128>();

        /// <summary>Successfully disposed lease ids in disposal order, so teardown order is observable (P-048).</summary>
        public List<Id128> DisposedOrder { get; } = new List<Id128>();

        /// <summary>Lease ids whose disposal attempt threw, in the order they threw.</summary>
        public List<Id128> FailedDisposalOrder { get; } = new List<Id128>();

        public FactoryKey DisposerKey { get; } = new FactoryKey(new Id128(LeaseIdSalt, 0UL), 1U);

        public IReadOnlyList<ScriptedResourceLease> Leases => leases;

        public IManagedResourceLease Prepare(ManagedResourceRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            PrepareCount++;
            leaseSequence++;
            var lease = new ScriptedResourceLease(
                request.Resource,
                new Id128(LeaseIdSalt, leaseSequence),
                request.Token,
                DisposerKey,
                new ManagedResourceGate(),
                OnDisposed);
            leases.Add(lease);
            return lease;
        }

        /// <summary>Resolves one prepared lease by identity; a miss is reported, never substituted (P-052).</summary>
        public bool TryGetLease(Id128 leaseId, out ScriptedResourceLease? lease)
        {
            for (int i = 0; i < leases.Count; i++)
            {
                if (leases[i].LeaseId.Equals(leaseId))
                {
                    lease = leases[i];
                    return true;
                }
            }

            lease = null;
            return false;
        }

        /// <summary>How many disposal attempts one lease recorded: one for a clean lease, two for a scripted retry.</summary>
        public int DisposalAttemptsOf(Id128 leaseId)
        {
            int count = 0;
            for (int i = 0; i < DisposedOrder.Count; i++)
            {
                if (DisposedOrder[i].Equals(leaseId))
                {
                    count++;
                }
            }

            for (int i = 0; i < FailedDisposalOrder.Count; i++)
            {
                if (FailedDisposalOrder[i].Equals(leaseId))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Every recorded disposal, joined for a detail line; `failed=` lists the attempts that threw.</summary>
        public string DescribeDisposals()
        {
            var text = new System.Text.StringBuilder();
            text.Append("disposed=");
            for (int i = 0; i < DisposedOrder.Count; i++)
            {
                if (i != 0)
                {
                    text.Append(',');
                }

                text.Append(DisposedOrder[i].ToString());
            }

            text.Append("; failed=");
            for (int i = 0; i < FailedDisposalOrder.Count; i++)
            {
                if (i != 0)
                {
                    text.Append(',');
                }

                text.Append(FailedDisposalOrder[i].ToString());
            }

            return text.ToString();
        }

        private void OnDisposed(Id128 leaseId)
        {
            if (FailingDisposals.Remove(leaseId))
            {
                FailedDisposalOrder.Add(leaseId);
                FailedDisposalCount++;
                throw new InvalidOperationException(
                    "Scripted disposal failure for lease " + leaseId.ToString() + " (06 s5: one disposer throws).");
            }

            DisposedOrder.Add(leaseId);
            DisposeCount++;
        }
    }
}

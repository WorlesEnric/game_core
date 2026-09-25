// GameCore.Composition — the observable service-closure delta of one publication (P-012, P-025, P-046).
//
// P-012 is a statement about *one plan*: "Loss of a required provider automatically makes affected consumers
// `WaitingForDependencies` and retracts their active contributions in the same plan; optional bindings rebind to
// their declared fallback. Consumers resume automatically when valid dependencies return." The applier already
// resolves that plan; this file is the part a caller can inspect afterwards, so "in the same publication" is
// checkable rather than inferred:
//
//   * which consumers started waiting, and the diagnostic that explains each wait;
//   * which waiting consumers resumed, and the provider that came back;
//   * which installations lost or gained a service binding, with the contract and provider named;
//   * which installations suspended, activated or retired in the same publication.
//
// The delta is a pure function of the plan's before/after states. It carries no resource, no lease and no live
// world reference, so it can be archived as evidence (P-026) without pinning storage.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Composition
{
    /// <summary>How one installation's lifecycle state changed in one publication.</summary>
    public readonly struct LifecycleEdge
    {
        public readonly PluginInstanceId Instance;
        public readonly InstallationState From;
        public readonly InstallationState To;

        public LifecycleEdge(PluginInstanceId instance, InstallationState from, InstallationState to)
        {
            Instance = instance;
            From = from;
            To = to;
        }

        public override string ToString() => Instance.ToString() + ": " + From + "->" + To;
    }

    /// <summary>One binding that appeared or disappeared for one consumer in this publication (P-007, P-012).</summary>
    public readonly struct BindingDelta
    {
        public readonly PluginInstanceId Consumer;
        public readonly ContractRef Contract;
        public readonly ProviderInstallationId Provider;
        public readonly bool Added;

        public BindingDelta(PluginInstanceId consumer, ContractRef contract, ProviderInstallationId provider, bool added)
        {
            Consumer = consumer;
            Contract = contract;
            Provider = provider;
            Added = added;
        }

        public override string ToString() =>
            Consumer.ToString() + ": " + (Added ? "+" : "-") + Contract.ContractId.ToString() + " from " + Provider.ToString();
    }

    /// <summary>
    /// The service-closure consequence of one planned composition change: waiting consumers, resumed consumers,
    /// lifecycle edges, and the binding set that changed. Read-only and self-describing.
    /// </summary>
    public sealed class ServiceClosureDelta
    {
        private ServiceClosureDelta(
            OperationId operation,
            IReadOnlyList<LifecycleEdge>? lifecycleEdges,
            IReadOnlyList<PluginInstanceId>? waitingConsumers,
            IReadOnlyList<PluginInstanceId>? resumedConsumers,
            IReadOnlyList<PluginInstanceId>? retractedConsumers,
            IReadOnlyList<PluginInstanceId>? retiredInstances,
            IReadOnlyList<BindingDelta>? bindings,
            IReadOnlyList<Diagnostic>? waitingDiagnostics,
            IReadOnlyList<Diagnostic>? closureDiagnostics)
        {
            Operation = operation;
            LifecycleEdges = ContractCollections.Freeze(lifecycleEdges);
            WaitingConsumers = ContractCollections.Freeze(waitingConsumers);
            ResumedConsumers = ContractCollections.Freeze(resumedConsumers);
            RetractedConsumers = ContractCollections.Freeze(retractedConsumers);
            RetiredInstances = ContractCollections.Freeze(retiredInstances);
            Bindings = ContractCollections.Freeze(bindings);
            WaitingDiagnostics = ContractCollections.Freeze(waitingDiagnostics);
            ClosureDiagnostics = ContractCollections.Freeze(closureDiagnostics);
        }

        public OperationId Operation { get; }

        /// <summary>Every installation whose lifecycle state changed in this one publication.</summary>
        public IReadOnlyList<LifecycleEdge> LifecycleEdges { get; }

        /// <summary>Consumers that became `WaitingForDependencies` because a required provider left (P-012).</summary>
        public IReadOnlyList<PluginInstanceId> WaitingConsumers { get; }

        /// <summary>Consumers that were waiting and became `Active` again because their dependency returned (P-012).</summary>
        public IReadOnlyList<PluginInstanceId> ResumedConsumers { get; }

        /// <summary>
        /// Consumers whose active contributions retract in this publication: waiting, suspended or retired. Their
        /// ingress closes and their derived behavior is retracted with the new assembly (P-046, P-047).
        /// </summary>
        public IReadOnlyList<PluginInstanceId> RetractedConsumers { get; }

        /// <summary>Installations this plan removes, already ordered consumers-before-providers (P-012, P-048).</summary>
        public IReadOnlyList<PluginInstanceId> RetiredInstances { get; }

        /// <summary>Bindings added or removed in this publication, in canonical order.</summary>
        public IReadOnlyList<BindingDelta> Bindings { get; }

        /// <summary>The reason each waiting consumer waits; empty when nothing is waiting (P-052).</summary>
        public IReadOnlyList<Diagnostic> WaitingDiagnostics { get; }

        /// <summary>Closure-level diagnostics: a cycle or conflict that rejected the proposal (P-012).</summary>
        public IReadOnlyList<Diagnostic> ClosureDiagnostics { get; }

        public bool HasWaits => WaitingConsumers.Count != 0;

        public bool HasResumes => ResumedConsumers.Count != 0;

        /// <summary>True when any observable closure fact changed; a no-change plan reports false.</summary>
        public bool Changed =>
            LifecycleEdges.Count != 0 || WaitingConsumers.Count != 0 || ResumedConsumers.Count != 0
            || RetiredInstances.Count != 0 || Bindings.Count != 0;

        /// <summary>Added bindings in canonical order.</summary>
        public IReadOnlyList<BindingDelta> AddedBindings() => Filter(true);

        /// <summary>Removed bindings in canonical order.</summary>
        public IReadOnlyList<BindingDelta> RemovedBindings() => Filter(false);

        /// <summary>True when this publication resumed exactly the consumer named (a dependent provider returned).</summary>
        public bool Resumed(PluginInstanceId instance) => Contains(ResumedConsumers, instance);

        /// <summary>True when this publication made exactly the consumer named wait (a required provider left).</summary>
        public bool Waits(PluginInstanceId instance) => Contains(WaitingConsumers, instance);

        /// <summary>
        /// Computes the delta of one plan from its before/after states. A rejected plan has no after-state change,
        /// so its delta is empty and `plan.Succeeded` remains the authority on whether anything happened.
        /// </summary>
        public static ServiceClosureDelta Compute(CompositionEditPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            List<LifecycleEdge> edges = new List<LifecycleEdge>();
            List<PluginInstanceId> waiting = new List<PluginInstanceId>();
            List<PluginInstanceId> resumed = new List<PluginInstanceId>();
            List<PluginInstanceId> retracted = new List<PluginInstanceId>();
            List<Diagnostic> waitingDiagnostics = new List<Diagnostic>();
            List<BindingDelta> bindings = new List<BindingDelta>();

            IReadOnlyList<InstallEntry> after = plan.After.Installs;
            for (int i = 0; i < after.Count; i++)
            {
                InstallEntry entry = after[i];
                InstallationState previous = InstallationState.Registered;
                IReadOnlyList<ServiceBinding> previousBindings = Array.Empty<ServiceBinding>();
                if (plan.Before.TryGetInstall(entry.Instance, out InstallEntry? before) && before != null)
                {
                    previous = before.State;
                    previousBindings = before.Bindings;
                }

                if (previous != entry.State)
                {
                    edges.Add(new LifecycleEdge(entry.Instance, previous, entry.State));
                }

                if (entry.State == InstallationState.WaitingForDependencies && previous != InstallationState.WaitingForDependencies)
                {
                    waiting.Add(entry.Instance);
                    AppendWaitingDiagnostics(waitingDiagnostics, entry);
                }
                else if (entry.State == InstallationState.Active && previous == InstallationState.WaitingForDependencies)
                {
                    // P-012: a waiting consumer resumes automatically when a valid dependency returns.
                    resumed.Add(entry.Instance);
                }

                if (Retracts(previous, entry.State))
                {
                    retracted.Add(entry.Instance);
                }

                DiffBindings(entry.Instance, previousBindings, entry.Bindings, bindings);
            }

            // A retired activation is not automatically a removal: the applier also lists an installation whose
            // *activation* was displaced (suspend, lost required provider, in-place replacement). Only an
            // installation whose after-state is Retiring or Disposed is removed, so only that one contributes a
            // disappearance edge and a removed binding here. The main loop above already reported the state change
            // and the binding change of a suspend or a lost provider, so without this filter a suspend would report
            // itself twice - which is itself a false statement about the closure.
            for (int i = 0; i < plan.RetiredInstances.Count; i++)
            {
                PluginInstanceId instance = plan.RetiredInstances[i];
                if (!plan.Before.TryGetInstall(instance, out InstallEntry? before) || before == null)
                {
                    continue;
                }

                if (!IsRemoved(plan, instance))
                {
                    continue;
                }

                // The main loop already reported the Active -> Retiring/Disposed edge of an install entry that is
                // present in the new assembly, so this only adds the edge for an identity that vanished entirely.
                if (!ContainsEdge(edges, new LifecycleEdge(instance, before.State, InstallationState.Disposed)))
                {
                    edges.Add(new LifecycleEdge(instance, before.State, InstallationState.Disposed));
                }

                DiffBindings(instance, before.Bindings, Array.Empty<ServiceBinding>(), bindings);
            }

            // The RetiredInstances list keeps the plan's own teardown order (consumers before providers), while the
            // edges and binding deltas above are canonical and de-duplicated.
            bindings.Sort(CompareBindingDeltas);
            return new ServiceClosureDelta(
                plan.Operation,
                edges,
                waiting,
                resumed,
                retracted,
                plan.RetiredInstances,
                bindings,
                waitingDiagnostics,
                plan.Services != null ? plan.Services.Diagnostics : null);
        }

        /// <summary>
        /// True when this publication *removes* the installation rather than merely displacing its activation: the
        /// after-state is Retiring or Disposed. This is the one place the difference is decided, so the delta and
        /// the coordinator agree by construction.
        /// </summary>
        private static bool IsRemoved(CompositionEditPlan plan, PluginInstanceId instance)
        {
            if (!plan.After.TryGetInstall(instance, out InstallEntry? after) || after == null)
            {
                // A vanished install entry is a removal too: nothing in the new assembly owns that identity.
                return true;
            }

            return after.State == InstallationState.Retiring || after.State == InstallationState.Disposed;
        }

        private static bool ContainsEdge(List<LifecycleEdge> edges, LifecycleEdge candidate)
        {
            for (int i = 0; i < edges.Count; i++)
            {
                if (edges[i].Instance.Equals(candidate.Instance) && edges[i].From == candidate.From && edges[i].To == candidate.To)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Canonical, stable rendering of the delta; the evidence form archived with a run (P-026).</summary>
        public string Describe()
        {
            List<string> lines = new List<string>();
            lines.Add("operation=" + Operation.ToString());
            lines.Add("edges=" + RenderEdges());
            lines.Add("waiting=" + RenderInstances(WaitingConsumers));
            lines.Add("resumed=" + RenderInstances(ResumedConsumers));
            lines.Add("retracted=" + RenderInstances(RetractedConsumers));
            lines.Add("retired=" + RenderInstances(RetiredInstances));
            lines.Add("bindings=" + RenderBindings());
            lines.Sort(StringComparer.Ordinal);
            return string.Join("\n", lines);
        }

        private static bool Retracts(InstallationState from, InstallationState to)
        {
            bool contributedBefore = from == InstallationState.Active;
            bool contributesNow = to == InstallationState.Active;
            return contributedBefore && !contributesNow;
        }

        private static void AppendWaitingDiagnostics(List<Diagnostic> into, InstallEntry entry)
        {
            for (int i = 0; i < entry.Diagnostics.Count; i++)
            {
                into.Add(entry.Diagnostics[i]);
            }
        }

        private static void DiffBindings(
            PluginInstanceId consumer,
            IReadOnlyList<ServiceBinding> before,
            IReadOnlyList<ServiceBinding> after,
            List<BindingDelta> into)
        {
            for (int i = 0; i < before.Count; i++)
            {
                if (!HasBinding(after, before[i]))
                {
                    into.Add(new BindingDelta(consumer, before[i].Contract, before[i].Provider, false));
                }
            }

            for (int i = 0; i < after.Count; i++)
            {
                if (!HasBinding(before, after[i]))
                {
                    into.Add(new BindingDelta(consumer, after[i].Contract, after[i].Provider, true));
                }
            }
        }

        private static bool HasBinding(IReadOnlyList<ServiceBinding> bindings, ServiceBinding candidate)
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].Contract.ContractId.Equals(candidate.Contract.ContractId) &&
                    bindings[i].Provider.Value.Equals(candidate.Provider.Value) &&
                    bindings[i].ActivationEpoch.Equals(candidate.ActivationEpoch) &&
                    bindings[i].IsFallback == candidate.IsFallback)
                {
                    return true;
                }
            }

            return false;
        }

        private static int CompareBindingDeltas(BindingDelta left, BindingDelta right)
        {
            int consumer = left.Consumer.Value.CompareTo(right.Consumer.Value);
            if (consumer != 0)
            {
                return consumer;
            }

            int contract = left.Contract.ContractId.CompareTo(right.Contract.ContractId);
            if (contract != 0)
            {
                return contract;
            }

            int provider = left.Provider.Value.CompareTo(right.Provider.Value);
            if (provider != 0)
            {
                return provider;
            }

            return left.Added == right.Added ? 0 : (left.Added ? 1 : -1);
        }

        private IReadOnlyList<BindingDelta> Filter(bool added)
        {
            List<BindingDelta> filtered = new List<BindingDelta>();
            for (int i = 0; i < Bindings.Count; i++)
            {
                if (Bindings[i].Added == added)
                {
                    filtered.Add(Bindings[i]);
                }
            }

            return filtered;
        }

        private static bool Contains(IReadOnlyList<PluginInstanceId> instances, PluginInstanceId candidate)
        {
            for (int i = 0; i < instances.Count; i++)
            {
                if (instances[i].Equals(candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private string RenderEdges()
        {
            List<string> rendered = new List<string>(LifecycleEdges.Count);
            for (int i = 0; i < LifecycleEdges.Count; i++)
            {
                rendered.Add(LifecycleEdges[i].ToString());
            }

            rendered.Sort(StringComparer.Ordinal);
            return "[" + string.Join(",", rendered) + "]";
        }

        private static string RenderInstances(IReadOnlyList<PluginInstanceId> instances)
        {
            List<string> rendered = new List<string>(instances.Count);
            for (int i = 0; i < instances.Count; i++)
            {
                rendered.Add(instances[i].ToString());
            }

            rendered.Sort(StringComparer.Ordinal);
            return "[" + string.Join(",", rendered) + "]";
        }

        private string RenderBindings()
        {
            List<string> rendered = new List<string>(Bindings.Count);
            for (int i = 0; i < Bindings.Count; i++)
            {
                rendered.Add(Bindings[i].ToString());
            }

            rendered.Sort(StringComparer.Ordinal);
            return "[" + string.Join(",", rendered) + "]";
        }

        public override string ToString() =>
            "closure(edges=" + LifecycleEdges.Count.ToString(CultureInfo.InvariantCulture)
            + ", waiting=" + WaitingConsumers.Count.ToString(CultureInfo.InvariantCulture)
            + ", resumed=" + ResumedConsumers.Count.ToString(CultureInfo.InvariantCulture)
            + ", bindings=" + Bindings.Count.ToString(CultureInfo.InvariantCulture) + ")";
    }
}

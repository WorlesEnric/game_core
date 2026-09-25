// GameCore.Unity.Runtime — the single switched publication reference (GC-008).
//
// Normative sources: 00 P-030 (at an end-of-step or idle boundary close step and command admission; drain old
// world jobs and adapter readers; revalidate; stage migrations; apply structural and state changes; install the
// new binding tables and execution graph; preinstall/validate the new ingress gates closed; construct the complete
// observation image and result record; then a nonthrowing serialized commit switches the published
// revision/epoch/mode/image and active gate table *together* and reopens admission; no observer or system sees a
// mixture of old and new assembly), P-045 (observers see immutable images at (epoch, step) publication only) and
// 04 s5 (one preconstructed immutable `PublishedWorldView` reference holding revision/epoch, snapshot,
// binding/schedule references and the active ingress-gate table; a serialized nonthrowing `Volatile.Write` of this
// reference exposes the new view; readers capture one reference for their check).
//
// Everything a reader needs to decide "what is the assembly right now" is one object: epoch, revision, binding
// table, derivation rules, compiled schedule, dispatch table, ingress gate table and the snapshot token. There is
// deliberately no way to read half of it, because there is no second field to read.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using GameCore.Contracts;
using GameCore.Planning;

namespace GameCore.Unity.Runtime
{
    /// <summary>What one installed ingress/dag gate looks like inside the published view (P-030, P-047).</summary>
    public readonly struct PublishedGate
    {
        public readonly FactoryKey SystemKey;
        public readonly AssemblyEpoch Epoch;

        /// <summary>True while the gate admits work; a closed gate discards instead of delivering.</summary>
        public readonly bool Open;

        public PublishedGate(FactoryKey systemKey, AssemblyEpoch epoch, bool open)
        {
            SystemKey = systemKey;
            Epoch = epoch;
            Open = open;
        }

        public override string ToString() => SystemKey.ToString() + (Open ? ":open" : ":closed");
    }

    /// <summary>
    /// Immutable published assembly of one world (04 s5). The epoch inside it is authoritative; the world's mirror
    /// counter is only ever moved after this reference has been switched.
    /// </summary>
    public sealed class PublishedWorldView
    {
        public static readonly PublishedWorldView Empty = new PublishedWorldView(
            AssemblyEpoch.Zero,
            CompositionRevision.Zero,
            default(SnapshotToken),
            null,
            TargetBindingTable.Empty,
            null,
            CompiledSchedule.Empty,
            null,
            0);

        public PublishedWorldView(
            AssemblyEpoch epoch,
            CompositionRevision revision,
            SnapshotToken token,
            OrderedDispatchTable? dispatchTable,
            TargetBindingTable bindings,
            IReadOnlyList<DerivedBindingRule>? rules,
            CompiledSchedule schedule,
            IReadOnlyList<PublishedGate>? gates,
            int ordinal)
        {
            Epoch = epoch;
            Revision = revision;
            Token = token;
            DispatchTable = dispatchTable;
            Bindings = bindings ?? TargetBindingTable.Empty;
            Rules = ContractCollections.Freeze(rules);
            Schedule = schedule ?? CompiledSchedule.Empty;
            Gates = ContractCollections.Freeze(gates);
            Ordinal = ordinal;
        }

        /// <summary>Published assembly epoch: the value every observer and gate must compare against (P-006).</summary>
        public AssemblyEpoch Epoch { get; }

        public CompositionRevision Revision { get; }

        /// <summary>Snapshot token of the published image; same logical step as the previous assembly (P-006).</summary>
        public SnapshotToken Token { get; }

        /// <summary>Epoch-bound ordered dispatch table; its entries are the executed order (P-040).</summary>
        public OrderedDispatchTable? DispatchTable { get; }

        /// <summary>Effective bindings of every target in this assembly (P-017).</summary>
        public TargetBindingTable Bindings { get; }

        /// <summary>Active derivation rules; a future spawn derives its assembly from exactly these (P-024).</summary>
        public IReadOnlyList<DerivedBindingRule> Rules { get; }

        public CompiledSchedule Schedule { get; }

        /// <summary>Active gate table installed with the view; gates are validated closed before the commit (P-030).</summary>
        public IReadOnlyList<PublishedGate> Gates { get; }

        /// <summary>
        /// Targets of this assembly in canonical order. They belong to the published view rather than only to its
        /// binding table because the view is what an observer captures in one read (P-030, 04 s5).
        /// </summary>
        public IReadOnlyList<TargetId> Targets => Bindings.Targets;

        /// <summary>Publication ordinal of this view, counting from the initial assembly; diagnostics only.</summary>
        public int Ordinal { get; }

        public int BoundTargetCount => Bindings.TargetCount;

        public int BindingRowCount => Bindings.Count;

        public bool HasBinding(TargetId target, CapabilityId capability, uint outputSlot) =>
            Bindings.TryGet(target, capability, outputSlot, out _);

        /// <summary>
        /// Effective rows a target of the given recipe in the given scope must receive at first visibility (P-024):
        /// every currently active derived rule that matches. A newly spawned target therefore appears fully
        /// assembled rather than progressively wired.
        /// </summary>
        public IReadOnlyList<DerivedBindingRule> RulesFor(DefinitionRef recipe, ScopeId scope)
        {
            var matching = new List<DerivedBindingRule>();
            for (int i = 0; i < Rules.Count; i++)
            {
                if (Rules[i].AppliesTo(recipe, scope))
                {
                    matching.Add(Rules[i]);
                }
            }

            return matching;
        }

        public override string ToString() =>
            "assembly#" + Ordinal.ToString(CultureInfo.InvariantCulture)
            + " epoch=" + Epoch.Value.ToString(CultureInfo.InvariantCulture)
            + " revision=" + Revision.Value.ToString(CultureInfo.InvariantCulture)
            + " targets=" + BoundTargetCount.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The one switched pointer of a world's published assembly. Writers construct a complete view and exchange the
    /// reference once; readers capture one reference and can never observe a mixture (P-030, 04 s5).
    /// </summary>
    public sealed class PublishedAssemblySlot
    {
        private PublishedWorldView current;

        /// <summary>
        /// Published views, counting the initial view constructed with this slot: a world that has published only
        /// its initial assembly reports 1, and the first <see cref="Switch"/> takes it to 2.
        /// </summary>
        private int switches = 1;

        public PublishedAssemblySlot(PublishedWorldView initial)
        {
            current = initial ?? throw new ArgumentNullException(nameof(initial));
        }

        /// <summary>The current view. One reference read, so every field read afterwards belongs to the same epoch.</summary>
        public PublishedWorldView Read() => Volatile.Read(ref current);

        /// <summary>Views published so far, the initial assembly included.</summary>
        public int SwitchCount => Volatile.Read(ref switches);

        /// <summary>
        /// The commit itself: a nonthrowing single-reference switch. The caller must have validated everything it
        /// needs beforehand, because throwing here would leave the world without a published image (P-030).
        /// </summary>
        public void Switch(PublishedWorldView view)
        {
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }

            Volatile.Write(ref current, view);
            Interlocked.Increment(ref switches);
        }
    }
}

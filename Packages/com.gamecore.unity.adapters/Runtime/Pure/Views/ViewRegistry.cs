// GameCore.Unity.Adapters — the target/view registry: stable target-to-view identity, independent of composition
// (GC-019).
//
// Normative sources: 00 P-001 ("A scope tree, installation dependency graph, execution graph, ECS entity graph,
// Transform hierarchy, and network topology are separate structures"), P-010 ("Scope membership and parent changes
// are composition operations, never a side effect of moving a Transform"), P-024 (despawn destroys only recipe-owned
// entities/resources; already committed events keep stable ids), P-034 (one authority per state domain; adapter
// mirrors are never separately authoritative) and 04 s6 ("Moving a plugin scope subtree updates composition
// membership and derived bindings; it does not reparent `LocalTransform` or GameObjects. An explicit presentation
// adapter may separately request a visual reparent").
//
// This file owns exactly one idea: a *view* is a presentation-side object addressed by the stable `TargetId` it
// presents, and nothing about it is authority. Consequently:
//   * destroying a view destroys nothing in gameplay and commits no event (P-003, P-024);
//   * a visual reparent changes the binder's parent pointer only, and the committed composition parent is read
//     separately, so the two can never be confused (P-010);
//   * a mirror a view holds (the values a binder last applied) is labelled presentation data: it has no write path
//     back into ECS, and the registry can prove that, because it performs no ECS write at all.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Unity.Adapters.Views
{
    /// <summary>
    /// Stable identity of one view: the target it presents plus a slot, so one target can carry several independent
    /// views (a body and an attached label) without either being able to address the other (P-004).
    /// </summary>
    public readonly struct ViewKey : IEquatable<ViewKey>, IComparable<ViewKey>
    {
        public readonly TargetId Target;

        /// <summary>View slot within the target; 0 is the target's primary view.</summary>
        public readonly uint Slot;

        public ViewKey(TargetId target, uint slot)
        {
            Target = target;
            Slot = slot;
        }

        public bool IsDefault => Target.IsDefault;

        public bool Equals(ViewKey other) => Target.Equals(other.Target) && Slot == other.Slot;

        public override bool Equals(object? obj) => obj is ViewKey other && Equals(other);

        public override int GetHashCode() => unchecked((Target.GetHashCode() * 397) ^ (int)Slot);

        /// <summary>Canonical order: target identity first, then slot; never registration order (P-008).</summary>
        public int CompareTo(ViewKey other)
        {
            int byTarget = Target.CompareTo(other.Target);
            return byTarget != 0 ? byTarget : Slot.CompareTo(other.Slot);
        }

        public static bool operator ==(ViewKey left, ViewKey right) => left.Equals(right);

        public static bool operator !=(ViewKey left, ViewKey right) => !left.Equals(right);

        public override string ToString() =>
            Target.ToString() + "#" + Slot.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Outcome of one view registration (a duplicate is refused, never silently replaced).</summary>
    public enum ViewCreateOutcome
    {
        Created = 0,

        /// <summary>A live view already occupies that key; the caller must destroy it first (P-017 ownership).</summary>
        AlreadyPresent = 1,

        /// <summary>The bind refused: no engine object was created (P-029-style value refusal).</summary>
        Refused = 2,

        /// <summary>The registry is at capacity; the request is refused explicitly (P-043).</summary>
        CapacityExceeded = 3,
    }

    /// <summary>Outcome of one view destruction (P-024: destroying a view destroys nothing in gameplay).</summary>
    public enum ViewDestroyOutcome
    {
        Destroyed = 0,

        /// <summary>No live view occupies that key; a repeated destroy is not a second destroy (P-050).</summary>
        AlreadyAbsent = 1,

        /// <summary>The bind refused to destroy the engine object (reported, not assumed).</summary>
        Refused = 2,
    }

    /// <summary>
    /// The engine half of presentation: it creates, parents, applies and destroys engine objects. Every method takes
    /// a binder-local handle that is never an identity (P-054), and no method may touch gameplay state.
    /// </summary>
    public interface IViewBinder
    {
        /// <summary>Creates one engine view. A refusal is a value, so the registry never records a phantom view.</summary>
        bool TryCreate(ViewKey key, out long handle, out string detail);

        /// <summary>Destroys one engine view. Gameplay state is untouched by contract (P-024).</summary>
        bool TryDestroy(long handle, out string detail);

        /// <summary>Sets the visual parent; this is presentation, never scope membership (P-010).</summary>
        bool TrySetVisualParent(long handle, long parentHandle, out string detail);

        /// <summary>Applies already-read presentation values to one engine view. No gameplay write is possible here.</summary>
        void Apply(long handle, PresentationApplyData data);

        /// <summary>Live engine views the binder owns; zero after teardown proves complete view cleanup (TEST-015).</summary>
        int LiveViewCount { get; }

        /// <summary>Applies the binder performed; presentation work is observable but never authoritative (04 s7).</summary>
        int ApplyCount { get; }
    }

    /// <summary>
    /// The presentation data of one apply: the committed token it came from plus the values read out of that image.
    /// It is deliberately plain data rather than an engine object graph, so the pure side can construct and check it.
    /// </summary>
    public sealed class PresentationApplyData
    {
        public PresentationApplyData(
            ViewKey key,
            SnapshotToken token,
            ScopeId compositionParent,
            IReadOnlyList<PresentationField>? fields)
        {
            Key = key;
            Token = token;
            CompositionParent = compositionParent;
            Fields = ContractCollections.Freeze(fields);
        }

        public ViewKey Key { get; }

        /// <summary>Committed image the values came from; the only thing a presentation may claim about provenance.</summary>
        public SnapshotToken Token { get; }

        /// <summary>Committed scope parent at that token; read from composition, never from the Transform (P-010).</summary>
        public ScopeId CompositionParent { get; }

        public IReadOnlyList<PresentationField> Fields { get; }

        public override string ToString() =>
            Key.ToString() + "@" + Token.ToString() + " fields=" + Fields.Count.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// One presentation value of one capability slot. It mirrors an already committed value; it is not a second
    /// authority and never a write path (P-034).
    /// </summary>
    public readonly struct PresentationField
    {
        public readonly CapabilityId Capability;
        public readonly uint OutputSlot;
        public readonly int Value;

        public PresentationField(CapabilityId capability, uint outputSlot, int value)
        {
            Capability = capability;
            OutputSlot = outputSlot;
            Value = value;
        }

        public override string ToString() =>
            Capability.ToString() + "#" + OutputSlot.ToString(CultureInfo.InvariantCulture)
            + "=" + Value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>One registered view and what presentation has done with it.</summary>
    public sealed class ViewRecord
    {
        internal ViewRecord(ViewKey key, long handle, SnapshotToken createdToken)
        {
            Key = key;
            Handle = handle;
            CreatedToken = createdToken;
            LastAppliedToken = createdToken;
        }

        public ViewKey Key { get; }

        /// <summary>Binder-local handle; opaque and never serialized (P-054).</summary>
        public long Handle { get; internal set; }

        /// <summary>Committed token current when the view was created; diagnostics only.</summary>
        public SnapshotToken CreatedToken { get; }

        /// <summary>Last committed token applied to this view; a stale apply is refused against it (P-045).</summary>
        public SnapshotToken LastAppliedToken { get; internal set; }

        /// <summary>Visual parent handle set by an explicit presentation request; never a scope parent (P-010).</summary>
        public long VisualParent { get; internal set; }

        /// <summary>Committed composition parent observed at the last apply, from the snapshot (P-010).</summary>
        public ScopeId CompositionParent { get; internal set; }

        public int ApplyCount { get; internal set; }

        /// <summary>Applies refused because the token was not newer than the last applied one (P-045).</summary>
        public int StaleApplyCount { get; internal set; }

        /// <summary>
        /// False until the view's first presentation. A view created from the currently committed image must be
        /// presentable at that image (the alternative would be a view that can never show what it was created for);
        /// every later apply must then be strictly newer (P-045).
        /// </summary>
        public bool HasApplied { get; internal set; }

        /// <summary>True once an explicit visual reparent moved this view (presentation only).</summary>
        public bool VisualParentChanged { get; internal set; }

        public bool IsLive { get; internal set; } = true;

        public override string ToString() =>
            Key.ToString() + "=handle" + Handle.ToString(CultureInfo.InvariantCulture)
            + "(applies=" + ApplyCount.ToString(CultureInfo.InvariantCulture)
            + ", visualParent=" + VisualParent.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// The stable target-to-view map of one world. It performs no gameplay write of any kind: its only side effects
    /// are on the binder, so "view destruction leaves gameplay state intact" is a property of the type rather than a
    /// promise about its callers (P-024, P-034).
    /// </summary>
    public sealed class ViewRegistry
    {
        private readonly Dictionary<Id128, List<ViewRecord>> byTarget = new Dictionary<Id128, List<ViewRecord>>();
        private readonly Dictionary<ViewKey, ViewRecord> byKey = new Dictionary<ViewKey, ViewRecord>();
        private readonly List<ViewKey> keys = new List<ViewKey>();
        private readonly Dictionary<long, ViewRecord> byHandle = new Dictionary<long, ViewRecord>();

        public ViewRegistry(WorldId world, uint capacity)
        {
            if (world.Session.IsDefault)
            {
                throw new ArgumentException("A view registry must name a live world session (P-004).", nameof(world));
            }

            if (capacity == 0U)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "A bounded view registry must have a positive capacity.");
            }

            World = world;
            Capacity = capacity;
        }

        public WorldId World { get; }

        public uint Capacity { get; }

        public int CreateCount { get; private set; }

        public int DestroyCount { get; private set; }

        /// <summary>Destructions whose target had already left the committed assembly; the view still went away.</summary>
        public int OrphanedDestroyCount { get; private set; }

        public int DuplicateCreateRefusalCount { get; private set; }

        public int CapacityRefusalCount { get; private set; }

        public int ApplyCount { get; private set; }

        public int StaleApplyRefusalCount { get; private set; }

        /// <summary>Applies for a key with no live view; nothing is created implicitly (P-024).</summary>
        public int MissingViewApplyCount { get; private set; }

        public int VisualReparentCount { get; private set; }

        /// <summary>Visual reparents that did not change the committed composition parent (P-010).</summary>
        public int VisualReparentsWithoutCompositionChange { get; private set; }

        /// <summary>Views refused because their target was not in the committed assembly at creation.</summary>
        public int UnassembledTargetRefusalCount { get; private set; }

        public int LiveViewCount
        {
            get
            {
                int live = 0;
                for (int i = 0; i < keys.Count; i++)
                {
                    if (byKey[keys[i]].IsLive)
                    {
                        live++;
                    }
                }

                return live;
            }
        }

        public int DistinctTargetCount => byTarget.Count;

        /// <summary>Live view keys in canonical order; the stable target-to-view map, enumerated deterministically.</summary>
        public IReadOnlyList<ViewKey> LiveKeys()
        {
            var live = new List<ViewKey>();
            for (int i = 0; i < keys.Count; i++)
            {
                if (byKey[keys[i]].IsLive)
                {
                    live.Add(keys[i]);
                }
            }

            return live;
        }

        public IReadOnlyList<ViewRecord> ViewsOf(TargetId target)
        {
            if (byTarget.TryGetValue(target.Value, out List<ViewRecord>? list) && list != null)
            {
                return list;
            }

            return Array.Empty<ViewRecord>();
        }

        public bool TryGet(ViewKey key, out ViewRecord? record)
        {
            if (byKey.TryGetValue(key, out ViewRecord? found) && found != null && found.IsLive)
            {
                record = found;
                return true;
            }

            record = null;
            return false;
        }

        public bool TryGetByHandle(long handle, out ViewRecord? record)
        {
            if (byHandle.TryGetValue(handle, out ViewRecord? found) && found != null && found.IsLive)
            {
                record = found;
                return true;
            }

            record = null;
            return false;
        }

        /// <summary>
        /// Registers one view for a target that is present in the committed assembly. A target the assembly does not
        /// carry is refused, so a view can never claim to present something that is not committed (P-024).
        /// </summary>
        public ViewCreateOutcome Create(
            ViewKey key,
            SnapshotToken token,
            bool targetIsInCommittedAssembly,
            IViewBinder? binder,
            out ViewRecord? record)
        {
            record = null;
            if (key.IsDefault)
            {
                return ViewCreateOutcome.Refused;
            }

            if (byKey.TryGetValue(key, out ViewRecord? existing) && existing != null && existing.IsLive)
            {
                DuplicateCreateRefusalCount++;
                return ViewCreateOutcome.AlreadyPresent;
            }

            if (!targetIsInCommittedAssembly)
            {
                UnassembledTargetRefusalCount++;
                return ViewCreateOutcome.Refused;
            }

            if ((uint)byKey.Count >= Capacity)
            {
                CapacityRefusalCount++;
                return ViewCreateOutcome.CapacityExceeded;
            }

            long handle = 0L;
            if (binder != null)
            {
                if (!binder.TryCreate(key, out handle, out string detail))
                {
                    _ = detail;
                    return ViewCreateOutcome.Refused;
                }
            }

            var created = new ViewRecord(key, handle, token);
            byKey[key] = created;
            keys.Add(key);
            keys.Sort(CompareKeys);
            if (byTarget.TryGetValue(key.Target.Value, out List<ViewRecord>? list) && list != null)
            {
                list.Add(created);
            }
            else
            {
                byTarget.Add(key.Target.Value, new List<ViewRecord> { created });
            }

            if (handle != 0L)
            {
                byHandle[handle] = created;
            }

            CreateCount++;
            record = created;
            return ViewCreateOutcome.Created;
        }

        /// <summary>
        /// Destroys one view. It removes presentation objects and bookkeeping only; it cannot move composition,
        /// cannot commit an event, and reports honestly whether the target was still in the committed assembly.
        /// </summary>
        public ViewDestroyOutcome Destroy(ViewKey key, bool targetIsLiveInComposition, IViewBinder? binder)
        {
            if (!byKey.TryGetValue(key, out ViewRecord? record) || record == null || !record.IsLive)
            {
                return ViewDestroyOutcome.AlreadyAbsent;
            }
            if (binder != null && record.Handle != 0L && !binder.TryDestroy(record.Handle, out string detail))
            {
                _ = detail;
                return ViewDestroyOutcome.Refused;
            }

            if (!targetIsLiveInComposition)
            {
                OrphanedDestroyCount++;
            }

            record.IsLive = false;
            if (record.Handle != 0L)
            {
                byHandle.Remove(record.Handle);
            }

            if (byTarget.TryGetValue(key.Target.Value, out List<ViewRecord>? list) && list != null)
            {
                list.Remove(record);
                if (list.Count == 0)
                {
                    byTarget.Remove(key.Target.Value);
                }
            }

            DestroyCount++;
            return ViewDestroyOutcome.Destroyed;
        }

        /// <summary>
        /// Applies committed presentation data to one live view. The view's first presentation is always accepted —
        /// a view created from the currently committed image must be able to present it — and every later apply must
        /// be strictly newer than the last one, so a stale image can never overwrite a newer one (P-045).
        /// </summary>
        public bool TryApply(ViewKey key, PresentationApplyData data, IViewBinder? binder)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (!byKey.TryGetValue(key, out ViewRecord? record) || record == null || !record.IsLive)
            {
                MissingViewApplyCount++;
                return false;
            }

            // A token from another world incarnation is refused first, so the first-apply rule below can never let a
            // foreign image in (P-004).
            if (!data.Token.World.Session.Equals(World.Session))
            {
                record.StaleApplyCount++;
                StaleApplyRefusalCount++;
                return false;
            }

            // The first presentation of a view is always applied: a view created from the currently committed image
            // would otherwise be unable to present that image at all, which would leave a freshly spawned target's
            // view one publication behind forever. From then on a stale image never overwrites a newer one (P-045).
            if (record.HasApplied && !IsNewer(data.Token, record.LastAppliedToken))
            {
                record.StaleApplyCount++;
                StaleApplyRefusalCount++;
                return false;
            }

            record.HasApplied = true;
            record.LastAppliedToken = data.Token;
            record.CompositionParent = data.CompositionParent;
            record.ApplyCount++;
            ApplyCount++;
            if (binder != null && record.Handle != 0L)
            {
                binder.Apply(record.Handle, data);
            }

            return true;
        }

        /// <summary>
        /// Requests a *visual* reparent. It moves the binder's parent pointer only and records the committed
        /// composition parent observed before and after, so "moving a Transform does not move composition" is
        /// observable data rather than an assumption (P-010).
        /// </summary>
        public bool TrySetVisualParent(
            ViewKey key,
            ViewKey parent,
            ScopeId committedCompositionParent,
            IViewBinder? binder)
        {
            if (!byKey.TryGetValue(key, out ViewRecord? record) || record == null || !record.IsLive)
            {
                return false;
            }

            if (!byKey.TryGetValue(parent, out ViewRecord? parentRecord) || parentRecord == null || !parentRecord.IsLive)
            {
                return false;
            }

            ScopeId before = record.CompositionParent;
            if (binder != null && record.Handle != 0L
                && !binder.TrySetVisualParent(record.Handle, parentRecord.Handle, out string detail))
            {
                _ = detail;
                return false;
            }

            record.VisualParent = parentRecord.Handle;
            record.VisualParentChanged = true;
            record.CompositionParent = committedCompositionParent;
            VisualReparentCount++;
            if (before.Equals(committedCompositionParent))
            {
                VisualReparentsWithoutCompositionChange++;
            }

            return true;
        }

        /// <summary>
        /// Destroys every view of a target that left the committed assembly, then reports how many went away. A
        /// despawned target's views are presentation garbage; gameplay already committed its own outcome (P-024).
        /// </summary>
        public int DestroyViewsOf(TargetId target, IViewBinder? binder)
        {
            IReadOnlyList<ViewRecord> views = ViewsOf(target);
            var keysToDestroy = new List<ViewKey>(views.Count);
            for (int i = 0; i < views.Count; i++)
            {
                keysToDestroy.Add(views[i].Key);
            }

            int destroyed = 0;
            for (int i = 0; i < keysToDestroy.Count; i++)
            {
                if (Destroy(keysToDestroy[i], targetIsLiveInComposition: false, binder: binder)
                    == ViewDestroyOutcome.Destroyed)
                {
                    destroyed++;
                }
            }

            return destroyed;
        }

        /// <summary>Tears every view down; returns how many engine views were destroyed (TEST-015 baseline check).</summary>
        public int DestroyAll(IViewBinder? binder)
        {
            IReadOnlyList<ViewKey> live = LiveKeys();
            int destroyed = 0;
            for (int i = 0; i < live.Count; i++)
            {
                if (Destroy(live[i], targetIsLiveInComposition: false, binder: binder) == ViewDestroyOutcome.Destroyed)
                {
                    destroyed++;
                }
            }

            byTarget.Clear();
            byKey.Clear();
            byHandle.Clear();
            keys.Clear();
            return destroyed;
        }

        /// <summary>
        /// Strictly-newer test used from a view's second presentation on: a later step wins, and at the same step a
        /// later assembly epoch wins, because a composition publication keeps the logical step and moves the epoch
        /// (P-006). A default <paramref name="current"/> never matches a live candidate, which is why the first
        /// presentation is decided by <see cref="ViewRecord.HasApplied"/> rather than by this test (P-045).
        /// </summary>
        private static bool IsNewer(SnapshotToken candidate, SnapshotToken current)
        {
            if (!candidate.World.Session.Equals(current.World.Session))
            {
                // Another world incarnation is never newer for this registry (P-004).
                return false;
            }

            int byStep = candidate.LogicalStepId.CompareTo(current.LogicalStepId);
            if (byStep != 0)
            {
                return byStep > 0;
            }

            return candidate.AssemblyEpoch.CompareTo(current.AssemblyEpoch) > 0;
        }

        private static int CompareKeys(ViewKey left, ViewKey right) => left.CompareTo(right);
    }
}

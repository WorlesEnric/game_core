// GameCore.Unity.Runtime — W2 integration seam: creating and reading the live targets of one owned world.
//
// A target exists as real ECS storage: its stable identity and published stamp, its derived binding buffer and its
// owner state slots (GC-008's published components). Composition needs to know the target exists (so it can derive
// for it) before the assembly that publishes those rows; the planner needs the live state slot values so a schema
// change migrates a copy instead of zero-initialising live state (P-029, P-032).
//
// This seeder is the only place that creates such an entity outside a spawn publication, and it stamps the target
// with the epoch/revision the world currently publishes, so a seeded target is never visible as belonging to an
// assembly that was never published (P-024, P-030). Reading live slots is deliberately a copy: the planner runs
// while the old assembly keeps simulating (P-027).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Planning;
using Unity.Entities;

namespace GameCore.Unity.Runtime.Integration
{
    /// <summary>
    /// Creates the declared targets of one world in real ECS storage, registers them with the world's target
    /// registry and the composition index, and copies their live state slots for planning.
    /// </summary>
    public sealed class LiveTargetSeeder
    {
        private readonly UnityWorldHost world;
        private readonly TargetRegistry registry;
        private readonly LiveTargetIndex index;

        public LiveTargetSeeder(UnityWorldHost world, TargetRegistry registry, LiveTargetIndex index)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.index = index ?? throw new ArgumentNullException(nameof(index));

            if (!registry.World.Session.Equals(world.World.Session))
            {
                throw new ArgumentException(
                    "The target registry belongs to another world incarnation than the host (P-004).",
                    nameof(registry));
            }
        }

        public LiveTargetIndex Index => index;

        public int SeededCount { get; private set; }

        /// <summary>
        /// Creates one target: its entity, its stable identity, its published stamp at the world's current
        /// assembly, an empty derived binding buffer and an empty state-slot buffer. The target is registered with
        /// the world's target registry and with the composition index in the same call, so the two views cannot
        /// disagree about which targets exist (P-010, P-015).
        /// </summary>
        public bool TrySeed(
            TargetId target,
            ScopeId scope,
            DefinitionRef recipe,
            out TargetHandle handle,
            out DiagnosticCode code,
            out string detail)
        {
            handle = default(TargetHandle);
            if (!index.TryRegister(target, scope, recipe, out code, out detail))
            {
                return false;
            }

            EntityManager entityManager = world.EntityWorld.EntityManager;
            Entity entity = entityManager.CreateEntity();
            if (!registry.TryAllocate(target, entity, out handle, out code))
            {
                // The registry refused the identity, so the entity it would have owned is destroyed again: a
                // refused seed leaves no storage behind (P-005).
                entityManager.DestroyEntity(entity);
                index.TryRetire(target);
                detail = "target " + target.ToString() + " was refused by the target registry (P-004, P-005).";
                return false;
            }

            entityManager.AddComponentData(entity, new TargetIdentity
            {
                Target = target,
                Generation = handle.Generation,
            });

            // The stamp names the assembly that is published right now: the target exists, and its rows (none yet)
            // belong to that visible epoch (P-030).
            entityManager.AddComponentData(entity, new AssemblyStamp
            {
                AssemblyEpoch = world.CurrentEpoch.Value,
                CompositionRevision = world.PublishedCompositionRevision.Value,
                Slot = handle.Slot,
                Published = 1,
            });

            entityManager.AddBuffer<CapabilityBinding>(entity);
            entityManager.AddBuffer<TargetSlotState>(entity);
            SeededCount++;
            detail = string.Empty;
            return true;
        }

        /// <summary>
        /// Seeds or replaces one live state slot of one target with an explicit schema version and value. This is
        /// real gameplay state, not derived data, so it lives in its own buffer with its own version (P-032).
        /// </summary>
        public bool TrySeedSlot(
            TargetId target,
            OwnerId owner,
            SlotId slot,
            uint schemaVersion,
            int value,
            out DiagnosticCode code,
            out string detail)
        {
            if (!registry.TryResolveTarget(target, out _, out Entity entity))
            {
                code = DiagnosticCode.StaleHandle;
                detail = "target " + target.ToString() + " is not registered in this world (P-005).";
                return false;
            }

            EntityManager entityManager = world.EntityWorld.EntityManager;
            DynamicBuffer<TargetSlotState> slots = entityManager.HasBuffer<TargetSlotState>(entity)
                ? entityManager.GetBuffer<TargetSlotState>(entity)
                : entityManager.AddBuffer<TargetSlotState>(entity);

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Slot.Equals(slot) && slots[i].Owner.Equals(owner))
                {
                    TargetSlotState replaced = slots[i];
                    replaced.SchemaVersion = schemaVersion;
                    replaced.Value = value;
                    replaced.Active = 1;
                    slots[i] = replaced;
                    code = DiagnosticCode.None;
                    detail = string.Empty;
                    return true;
                }
            }

            slots.Add(new TargetSlotState
            {
                Slot = slot,
                Owner = owner,
                SchemaVersion = schemaVersion,
                Value = value,
                Active = 1,
            });

            code = DiagnosticCode.None;
            detail = string.Empty;
            return true;
        }

        public bool TryGetEntity(TargetId target, out Entity entity) => registry.TryResolveTarget(target, out _, out entity);

        /// <summary>
        /// Copies the live state slots of the named targets, canonically ordered by (target, owner, slot). The copy
        /// is the planner's whole view of mutable state: migrations run on copies, so a failed migration leaves the
        /// live value exactly as it was (P-029).
        /// </summary>
        public IReadOnlyList<LiveSlotState> ReadLiveSlots(IReadOnlyList<TargetId>? targets)
        {
            var result = new List<LiveSlotState>();
            if (targets == null)
            {
                return result;
            }

            EntityManager entityManager = world.EntityWorld.EntityManager;
            for (int t = 0; t < targets.Count; t++)
            {
                if (!registry.TryResolveTarget(targets[t], out _, out Entity entity))
                {
                    continue;
                }

                if (!entityManager.HasBuffer<TargetSlotState>(entity))
                {
                    continue;
                }

                DynamicBuffer<TargetSlotState> slots = entityManager.GetBuffer<TargetSlotState>(entity);
                for (int s = 0; s < slots.Length; s++)
                {
                    TargetSlotState row = slots[s];
                    result.Add(new LiveSlotState(
                        new StateSlotKey(targets[t], row.Owner, row.Slot),
                        row.SchemaVersion,
                        row.Value));
                }
            }

            result.Sort(CompareLiveSlots);
            return result;
        }

        private static int CompareLiveSlots(LiveSlotState left, LiveSlotState right)
        {
            int target = left.Slot.Target.Value.CompareTo(right.Slot.Target.Value);
            if (target != 0)
            {
                return target;
            }

            int owner = left.Slot.Owner.Value.CompareTo(right.Slot.Owner.Value);
            return owner != 0 ? owner : left.Slot.Slot.Value.CompareTo(right.Slot.Slot.Value);
        }

        public override string ToString() => "seededTargets=" + SeededCount.ToString(CultureInfo.InvariantCulture);
    }
}

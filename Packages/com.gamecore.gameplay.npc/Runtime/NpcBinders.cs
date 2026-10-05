// GameCore.Gameplay.Npc - NPC presentation: NavMeshAgentBinder, NpcAnimatorBinder, NpcBubbleBinder (P1.3).
//
// All three read committed slots only (P-045) and run after the prefab view binder, which places every view at its
// world.pos. The NPC's pose is world.pos (P1.7a: the one pose every plugin writes); npc.pos is never read here. They
// are headless-safe: they act only through the prefab view binder's live views, which do not exist headless.
//
// All three are residency-aware (P1.7a, A7): they record every residency notification (including the replay the
// streamer sends when a binder is added), skip NPCs whose committed world.region is not Resident (never notified counts
// as not Resident), and drop per-NPC state cached for a region when it stops being Resident.
//
// NavMeshAgentBinder: each NPC view gets a NavMeshAgent that is never in charge of the transform (updatePosition and
// updateRotation off). Each frame the agent is steered toward the committed logical pose (world.pos) and the view is
// placed at the agent's simulated position, so the view walks around obstacles on the region's baked NavMesh; when the
// agent is off the NavMesh, or drifts further than SnapDistance from the logical pose, it is warped there (the logical
// pose always wins). Without a NavMesh the view is placed at the logical pose directly.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using UnityEngine;
using UnityEngine.AI;

namespace GameCore.Gameplay.Npc
{
    /// <summary>The committed region (world.region) of an NPC as an authoring id; empty when unknown.</summary>
    internal static class NpcResidency
    {
        public static string RegionOf(ICommittedSlotReader slots, PrefabViewBinder views, TargetId target) =>
            views.RegionIdOf(slots.TryRead(target, GameplaySlots.WorldOwner, GameplaySlots.Region, out int key) ? key : 0);
    }

    /// <summary>Moves NPC views along the NavMesh toward their committed logical pose (world.pos).</summary>
    public sealed class NavMeshAgentBinder : IPresentationBinder, IResidencyAware
    {
        private readonly PrefabViewBinder views;
        private readonly NpcWorldExtension extension;
        private readonly RegionResidencySet residency = new RegionResidencySet();
        private readonly Dictionary<TargetId, NavMeshAgent> agents = new Dictionary<TargetId, NavMeshAgent>();
        private readonly Dictionary<TargetId, string> agentRegions = new Dictionary<TargetId, string>();
        private readonly List<TargetId> evicted = new List<TargetId>();

        public NavMeshAgentBinder(PrefabViewBinder views, NpcWorldExtension extension)
        {
            this.views = views;
            this.extension = extension;
        }

        public string BinderName => "gameplay.npc-navmesh";

        public bool IsActive => views != null && views.IsActive;

        /// <summary>Distance (m) beyond which the agent is warped onto the logical pose.</summary>
        public float SnapDistance { get; set; } = 1.5f;

        public int Warps { get; private set; }

        public void OnResidencyChanged(string regionId, RegionResidency value)
        {
            if (residency.Set(regionId, value))
            {
                return;
            }

            evicted.Clear();
            foreach (KeyValuePair<TargetId, string> pair in agentRegions)
            {
                if (string.Equals(pair.Value, regionId, StringComparison.Ordinal))
                {
                    evicted.Add(pair.Key);
                }
            }

            for (int i = 0; i < evicted.Count; i++)
            {
                agents.Remove(evicted[i]);
                agentRegions.Remove(evicted[i]);
            }

            evicted.Clear();
        }

        public int Present(ICommittedSlotReader slots)
        {
            if (!IsActive)
            {
                return 0;
            }

            int touched = 0;
            IReadOnlyList<NpcRecord> records = extension.Records;
            for (int i = 0; i < records.Count; i++)
            {
                NpcRecord record = records[i];
                string regionId = NpcResidency.RegionOf(slots, views, record.Target);
                if (!residency.IsResident(regionId)
                    || !views.TryGetView(record.Target, out GameObject? view) || view == null
                    || !slots.TryRead(record.Target, GameplaySlots.WorldOwner, GameplaySlots.PosX, out int x)
                    || !slots.TryRead(record.Target, GameplaySlots.WorldOwner, GameplaySlots.PosZ, out int z))
                {
                    continue;
                }

                int yaw = slots.TryRead(record.Target, GameplaySlots.WorldOwner, GameplaySlots.Yaw, out int yawValue) ? yawValue : 0;
                float y = slots.TryRead(record.Target, GameplaySlots.WorldOwner, GameplaySlots.PosY, out int py)
                    ? (float)GameplayUnits.ToMetres(py)
                    : view.transform.localPosition.y;
                agentRegions[record.Target] = regionId;
                var local = new Vector3((float)GameplayUnits.ToMetres(x), y, (float)GameplayUnits.ToMetres(z));
                Transform? parent = view.transform.parent;
                Vector3 logical = parent != null ? parent.TransformPoint(local) : local;
                view.transform.position = Steer(record, view, logical);
                view.transform.localRotation = Quaternion.Euler(0f, (float)GameplayUnits.MilliradiansToDegrees(yaw), 0f);
                touched++;
            }

            return touched;
        }

        private Vector3 Steer(NpcRecord record, GameObject view, Vector3 logical)
        {
            if (!agents.TryGetValue(record.Target, out NavMeshAgent? agent) || agent == null)
            {
                if (!view.TryGetComponent(out agent))
                {
                    agent = view.AddComponent<NavMeshAgent>();
                }

                agent.updatePosition = false;
                agent.updateRotation = false;
                agent.speed = record.Definition != null ? record.Definition.Speed * 1.25f : 2.25f;
                agent.acceleration = 20f;
                agent.radius = 0.35f;
                agents[record.Target] = agent;
            }

            if (!agent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(logical, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                {
                    agent.Warp(hit.position);
                    Warps++;
                }
                else
                {
                    return logical;
                }
            }

            Vector3 simulated = agent.nextPosition;
            var flat = new Vector3(simulated.x - logical.x, 0f, simulated.z - logical.z);
            if (flat.magnitude > SnapDistance)
            {
                agent.Warp(logical);
                Warps++;
                return logical;
            }

            agent.SetDestination(logical);
            return new Vector3(simulated.x, simulated.y, simulated.z);
        }
    }

    /// <summary>Drives an NPC view's Animator (Speed, State) from committed slots, when the view has one.</summary>
    public sealed class NpcAnimatorBinder : IPresentationBinder, IResidencyAware
    {
        private static readonly int SpeedParameter = Animator.StringToHash("Speed");
        private static readonly int StateParameter = Animator.StringToHash("State");
        private readonly PrefabViewBinder views;
        private readonly NpcWorldExtension extension;
        private readonly RegionResidencySet residency = new RegionResidencySet();

        public NpcAnimatorBinder(PrefabViewBinder views, NpcWorldExtension extension)
        {
            this.views = views;
            this.extension = extension;
        }

        public string BinderName => "gameplay.npc-animator";

        public bool IsActive => views != null && views.IsActive;

        public void OnResidencyChanged(string regionId, RegionResidency value) => residency.Set(regionId, value);

        public int Present(ICommittedSlotReader slots)
        {
            if (!IsActive)
            {
                return 0;
            }

            int touched = 0;
            IReadOnlyList<NpcRecord> records = extension.Records;
            for (int i = 0; i < records.Count; i++)
            {
                NpcRecord record = records[i];
                if (!residency.IsResident(NpcResidency.RegionOf(slots, views, record.Target))
                    || !views.TryGetView(record.Target, out GameObject? view) || view == null)
                {
                    continue;
                }

                Animator? animator = view.GetComponentInChildren<Animator>();
                if (animator == null || animator.runtimeAnimatorController == null)
                {
                    continue;
                }

                int state = slots.TryRead(record.Target, NpcSlots.Owner, NpcSlots.State, out int value) ? value : 0;
                bool moving = state == NpcSlots.Patrol || state == NpcSlots.Approach;
                animator.SetFloat(SpeedParameter, moving && record.Definition != null ? record.Definition.Speed : 0f);
                animator.SetInteger(StateParameter, state);
                touched++;
            }

            return touched;
        }
    }

    /// <summary>Writes "Name" plus the state (e.g. "Maren - patrol") into each NPC view's bubble.</summary>
    public sealed class NpcBubbleBinder : IPresentationBinder, IResidencyAware
    {
        private readonly PrefabViewBinder views;
        private readonly NpcWorldExtension extension;
        private readonly RegionResidencySet residency = new RegionResidencySet();

        public NpcBubbleBinder(PrefabViewBinder views, NpcWorldExtension extension)
        {
            this.views = views;
            this.extension = extension;
        }

        public string BinderName => "gameplay.npc-bubble";

        public bool IsActive => views != null && views.IsActive;

        public void OnResidencyChanged(string regionId, RegionResidency value) => residency.Set(regionId, value);

        public static string TextFor(string displayName, int state) => displayName + "\n(" + NpcSlots.StateName(state) + ")";

        public int Present(ICommittedSlotReader slots)
        {
            if (!IsActive)
            {
                return 0;
            }

            int touched = 0;
            IReadOnlyList<NpcRecord> records = extension.Records;
            for (int i = 0; i < records.Count; i++)
            {
                NpcRecord record = records[i];
                if (!residency.IsResident(NpcResidency.RegionOf(slots, views, record.Target))
                    || !views.TryGetView(record.Target, out GameObject? view) || view == null)
                {
                    continue;
                }

                NpcBubble? bubble = view.GetComponentInChildren<NpcBubble>();
                if (bubble == null)
                {
                    continue;
                }

                int state = slots.TryRead(record.Target, NpcSlots.Owner, NpcSlots.State, out int value) ? value : 0;
                bubble.Show(TextFor(record.DisplayName, state));
                touched++;
            }

            return touched;
        }
    }
}

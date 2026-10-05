// GameCore.Gameplay.Player - PortalProbe: portal travel from the committed player pose (P1.3).
//
// RegionPortal's own trigger fires on a physics contact with an entity view, which does not exist headless and which a
// teleported view does not produce reliably. The probe instead tests the COMMITTED player position (feet, mm) against
// the trigger box of every RegionPortal in the loaded region scenes (pure box math in the portal's local frame,
// inflated by the player radius) before the pump, and asks the world to travel. GameplayWorld.RequestPortalTravel keeps
// the 30-frame de-duplication and the "portal connects the traveller's region" check, and the travel itself is the
// world plugin's committed world.travel; the player plugin adopts the arrival pose on the next step.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.World;
using UnityEngine;

namespace GameCore.Gameplay.Player
{
    /// <summary>Requests portal travel when the committed player pose stands in a portal's trigger box.</summary>
    public sealed class PortalProbe : IGameplayInputSource
    {
        private readonly List<RegionPortal> portals = new List<RegionPortal>();
        private readonly PlayerWorldExtension extension;
        private int lastScan = -1000;
        private int frames;

        public PortalProbe(PlayerWorldExtension extension, float inflateMetres)
        {
            this.extension = extension ?? throw new ArgumentNullException(nameof(extension));
            Inflate = Mathf.Max(0f, inflateMetres);
        }

        public bool Enabled { get; set; } = true;

        /// <summary>How far outside the box (metres) still counts: the player's radius.</summary>
        public float Inflate { get; }

        /// <summary>Frames between scans of the loaded scenes for RegionPortal components.</summary>
        public int RescanFrames { get; set; } = 30;

        public int Requested { get; private set; }

        public int PortalCount => portals.Count;

        public int Collect(GameplayWorld world)
        {
            frames++;
            if (!Enabled || extension.Module == null)
            {
                return 0;
            }

            if (frames - lastScan >= RescanFrames)
            {
                lastScan = frames;
                portals.Clear();
                portals.AddRange(UnityEngine.Object.FindObjectsByType<RegionPortal>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
                portals.Sort((l, r) => string.CompareOrdinal(l.Portal != null ? l.Portal.AuthoringId : string.Empty, r.Portal != null ? r.Portal.AuthoringId : string.Empty));
            }

            var player = extension.Player;
            // P1.7a (A4): world.pos is the authoritative pose.
            if (!world.Slots.TryRead(player, GameplaySlots.WorldOwner, GameplaySlots.PosX, out int x)
                || !world.Slots.TryRead(player, GameplaySlots.WorldOwner, GameplaySlots.PosZ, out int z))
            {
                return 0;
            }

            // A travel committed but not adopted by the player kernel yet: wait one step before probing again.
            if (world.Slots.ReadOrDefault(player, GameplaySlots.WorldOwner, GameplaySlots.Region, 0)
                != world.Slots.ReadOrDefault(player, PlayerSlots.Owner, PlayerSlots.RegionKey, 0))
            {
                return 0;
            }

            int y = world.Slots.ReadOrDefault(player, GameplaySlots.WorldOwner, GameplaySlots.PosY, 0);
            var feet = new Vector3((float)GameplayUnits.ToMetres(x), (float)GameplayUnits.ToMetres(y), (float)GameplayUnits.ToMetres(z));
            for (int i = 0; i < portals.Count; i++)
            {
                RegionPortal portal = portals[i];
                if (portal == null || portal.Portal == null || !Inside(portal, feet))
                {
                    continue;
                }

                if (world.RequestPortalTravel(player, portal.Portal.AuthoringId, portal.gameObject.scene.path))
                {
                    Requested++;
                    return 1;
                }
            }

            return 0;
        }

        /// <summary>True when <paramref name="point"/> (world space) is inside the portal's trigger box, inflated.</summary>
        public bool Inside(RegionPortal portal, Vector3 point)
        {
            Transform transform = portal.transform;
            Vector3 center = Vector3.zero;
            Vector3 size = new Vector3(3f, 3f, 1f);
            if (portal.TryGetComponent(out BoxCollider box))
            {
                center = box.center;
                size = box.size;
            }

            Vector3 local = transform.InverseTransformPoint(point) - center;
            Vector3 scale = transform.lossyScale;
            float ix = Inflate / Mathf.Max(0.0001f, Mathf.Abs(scale.x));
            float iz = Inflate / Mathf.Max(0.0001f, Mathf.Abs(scale.z));
            float iy = 0.5f / Mathf.Max(0.0001f, Mathf.Abs(scale.y));
            return Mathf.Abs(local.x) <= size.x * 0.5f + ix
                && Mathf.Abs(local.y) <= size.y * 0.5f + iy
                && Mathf.Abs(local.z) <= size.z * 0.5f + iz;
        }
    }
}

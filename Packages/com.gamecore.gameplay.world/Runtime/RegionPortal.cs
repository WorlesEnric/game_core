// GameCore.Gameplay.World - RegionPortal (P1.1).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using UnityEngine;

namespace GameCore.Gameplay.World
{
    /// <summary>
    /// One end of a portal inside a region scene: a trigger volume and the arrival pose of a traveller entering this
    /// region through it (the object's position and forward yaw). When the focus traveller's view enters the trigger,
    /// the portal asks the running world to travel to the portal's other region.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RegionPortal : MonoBehaviour
    {
        [AuthorRef(Category = "world.portal", Doc = "The portal connection this end belongs to.")]
        [SerializeField] private PortalDefinition? portal;

        [AuthorField(Unit = "m", Doc = "Distance in front of the portal a traveller arrives at.")]
        [SerializeField] private float arrivalDistance = 2.5f;

        public PortalDefinition? Portal => portal;

        public float ArrivalDistance => arrivalDistance;

        /// <summary>Arrival position (world space): in front of the portal along its forward axis.</summary>
        public Vector3 ArrivalPosition => transform.position + transform.forward * arrivalDistance;

        public float ArrivalYaw => transform.eulerAngles.y;

        public void Configure(PortalDefinition connection, float distance)
        {
            portal = connection;
            arrivalDistance = distance;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (portal == null || !Application.isPlaying)
            {
                return;
            }

            EntityViewTag? tag = other.GetComponentInParent<EntityViewTag>();
            if (tag == null)
            {
                return;
            }

            GameplayWorldBehaviour? host = FindAnyObjectByType<GameplayWorldBehaviour>();
            if (host != null && host.World != null)
            {
                host.World.RequestPortalTravel(tag.Target, portal.AuthoringId, gameObject.scene.path);
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.8f);
            Gizmos.DrawWireCube(transform.position, new Vector3(3f, 3f, 0.5f));
            Gizmos.DrawLine(transform.position, ArrivalPosition);
        }
    }
}

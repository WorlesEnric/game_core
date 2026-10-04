// GameCore.Gameplay.World - RegionBounds (P1.1).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using UnityEngine;

namespace GameCore.Gameplay.World
{
    /// <summary>The axis-aligned bounds of a region (local centre and size on the region object).</summary>
    [DisallowMultipleComponent]
    public sealed class RegionBounds : MonoBehaviour
    {
        [AuthorField(Unit = "m", Doc = "Bounds centre relative to the region object.")]
        [SerializeField] private Vector3 center = Vector3.zero;

        [AuthorField(Unit = "m", Doc = "Bounds size.")]
        [SerializeField] private Vector3 size = new Vector3(60f, 20f, 60f);

        public Vector3 Center => center;

        public Vector3 Size => size;

        public Bounds WorldBounds => new Bounds(transform.TransformPoint(center), size);

        public bool Contains(Vector3 point) => WorldBounds.Contains(point);

        public void Configure(Vector3 localCenter, Vector3 boundsSize)
        {
            center = localCenter;
            size = boundsSize;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
            Bounds bounds = WorldBounds;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}

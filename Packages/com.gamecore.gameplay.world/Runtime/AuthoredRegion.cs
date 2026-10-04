// GameCore.Gameplay.World - AuthoredRegion (P1.1).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using UnityEngine;

namespace GameCore.Gameplay.World
{
    /// <summary>The marker of a region scene: exactly one per scene.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RegionBounds))]
    public sealed class AuthoredRegion : MonoBehaviour, IAuthoredRegion
    {
        [AuthorRef(Category = "world.region", Doc = "The region this scene is.")]
        [SerializeField] private RegionDefinition? definition;

        [AuthorRef(Required = false, Doc = "Spawn point of the region (defaults to the region object).")]
        [SerializeField] private Transform? spawnPoint;

        public RegionDefinition? Definition => definition;

        /// <summary>The region's identity is its definition's authoring id.</summary>
        public string AuthoringId => definition != null ? definition.AuthoringId : string.Empty;

        public Transform SpawnPoint => spawnPoint != null ? spawnPoint : transform;

        public RegionBounds Bounds => GetComponent<RegionBounds>();

        public bool ContainsPoint(double x, double y, double z) => Bounds.Contains(new Vector3((float)x, (float)y, (float)z));

        public void Configure(RegionDefinition region, Transform? spawn)
        {
            definition = region;
            spawnPoint = spawn;
        }
    }
}

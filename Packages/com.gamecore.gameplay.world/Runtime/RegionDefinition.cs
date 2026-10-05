// GameCore.Gameplay.World - RegionDefinition (P1.1; completed per 05 row 1 by P1.7b: bounds, neighbours, spawn points,
// ambience).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using UnityEngine;

namespace GameCore.Gameplay.World
{
    /// <summary>A named arrival point of a region (portal arrivals, the world start, travel actions).</summary>
    [Serializable]
    public sealed class RegionSpawnPoint
    {
        [AuthorField(Doc = "Spawn point name, unique within the region (e.g. default, causeway, jetty).")]
        public string name = string.Empty;

        [AuthorField(Unit = "m", Doc = "Position (world space).")]
        public Vector3 position;

        [AuthorField(Unit = "deg", Min = 0, Max = 360, Doc = "Heading around +Y.")]
        public float yaw;

        public RegionSpawnPoint()
        {
        }

        public RegionSpawnPoint(string pointName, Vector3 at, float heading)
        {
            name = pointName ?? string.Empty;
            position = at;
            yaw = heading;
        }
    }

    /// <summary>One region of a world.</summary>
    [Authorable("world.region", DisplayName = "Region", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "A streamed region: one additive scene with its own composition scope, its bounds, neighbours, named spawn points and ambience.")]
    [CreateAssetMenu(menuName = "GameCore/Gameplay/Region Definition", fileName = "Region")]
    public sealed class RegionDefinition : ScriptableObject, IDefinitionAsset
    {
        /// <summary>The spawn point name used when none is named.</summary>
        public const string DefaultSpawnPoint = "default";

        [SerializeField] private string authoringId = string.Empty;

        [AuthorField(Doc = "Display name.")]
        [SerializeField] private string displayName = string.Empty;

        [AuthorField(Type = "artifact", Structural = true, Doc = "Project-relative path of the region scene (Assets/.../X.unity).")]
        [SerializeField] private string scenePath = string.Empty;

        [AuthorField(Unit = "m", Doc = "Centre of the region's bounds (world space).")]
        [SerializeField] private Vector3 boundsCenter;

        [AuthorField(Unit = "m", Doc = "Size of the region's bounds; zero means the scene's RegionBounds component decides.")]
        [SerializeField] private Vector3 boundsSize;

        [AuthorRef(Category = "world.region", Required = false, Doc = "Regions reachable from this one (preloaded with it when the world preloads neighbours).")]
        [SerializeField] private List<RegionDefinition> neighbours = new List<RegionDefinition>();

        [AuthorField(Doc = "Named arrival points (portal arrivals, the world start).")]
        [SerializeField] private List<RegionSpawnPoint> spawnPoints = new List<RegionSpawnPoint>();

        [AuthorRef(Category = "audio.ambience", Required = false, Doc = "The region's ambience (an AmbienceDefinition).")]
        [SerializeField] private ScriptableObject? ambience;

        [SerializeField] private string contentStamp = string.Empty;

        public string AuthoringId => authoringId;

        public string DefinitionName => string.IsNullOrEmpty(displayName) ? name : displayName;

        public string ContentStamp => contentStamp;

        public string DisplayName => DefinitionName;

        public string ScenePath => scenePath;

        /// <summary>The authored bounds; empty (size zero) when the scene's RegionBounds decides.</summary>
        public Bounds Bounds => new Bounds(boundsCenter, boundsSize);

        public bool HasBounds => boundsSize.x > 0f && boundsSize.y > 0f && boundsSize.z > 0f;

        public IReadOnlyList<RegionDefinition> Neighbours => neighbours;

        public IReadOnlyList<RegionSpawnPoint> SpawnPoints => spawnPoints;

        public ScriptableObject? Ambience => ambience;

        public bool EnsureAuthoringId()
        {
            string next = AuthoringIdField.Ensure(authoringId);
            bool changed = !string.Equals(next, authoringId, StringComparison.Ordinal);
            authoringId = next;
            return changed;
        }

        public void Configure(string regionName, string path)
        {
            displayName = regionName ?? string.Empty;
            scenePath = path ?? string.Empty;
        }

        public void SetBounds(Vector3 center, Vector3 size)
        {
            boundsCenter = center;
            boundsSize = size;
        }

        public void SetNeighbours(IEnumerable<RegionDefinition>? regions)
        {
            neighbours = regions != null ? new List<RegionDefinition>(regions) : new List<RegionDefinition>();
        }

        public void AddNeighbour(RegionDefinition region)
        {
            if (region != null && region != this && !neighbours.Contains(region))
            {
                neighbours.Add(region);
            }
        }

        /// <summary>Adds or moves a named spawn point.</summary>
        public void SetSpawnPoint(string pointName, Vector3 position, float yaw)
        {
            string key = string.IsNullOrEmpty(pointName) ? DefaultSpawnPoint : pointName;
            for (int i = 0; i < spawnPoints.Count; i++)
            {
                if (spawnPoints[i] != null && string.Equals(spawnPoints[i].name, key, StringComparison.Ordinal))
                {
                    spawnPoints[i].position = position;
                    spawnPoints[i].yaw = yaw;
                    return;
                }
            }

            spawnPoints.Add(new RegionSpawnPoint(key, position, yaw));
        }

        /// <summary>The named spawn point (empty name: <see cref="DefaultSpawnPoint"/>).</summary>
        public bool TryGetSpawnPoint(string pointName, out RegionSpawnPoint point)
        {
            string key = string.IsNullOrEmpty(pointName) ? DefaultSpawnPoint : pointName;
            for (int i = 0; i < spawnPoints.Count; i++)
            {
                if (spawnPoints[i] != null && string.Equals(spawnPoints[i].name, key, StringComparison.Ordinal))
                {
                    point = spawnPoints[i];
                    return true;
                }
            }

            point = null!;
            return false;
        }

        public void SetAmbience(ScriptableObject? value) => ambience = value;

        public void SetContentStamp(string stamp) => contentStamp = stamp ?? string.Empty;

        private void Reset() => EnsureAuthoringId();

        private void OnValidate()
        {
            if (!AuthoringIds.IsValid(authoringId))
            {
                EnsureAuthoringId();
            }
        }
    }
}

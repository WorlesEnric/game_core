// GameCore.Gameplay.World - authored world types (P1.1, Studio 03 s1/s4).
//
//   RegionDefinition  one region: its scene, display name and spawn point (the region's identity is this asset's id)
//   PortalDefinition  one undirected connection between two regions
//   WorldDefinition   the regions and portals of one world, its start region and streaming options
//   AuthoredRegion    the marker of a region scene: which RegionDefinition the scene is, with bounds and spawn point
//   RegionBounds      the region's axis-aligned bounds (entities must be inside)
//   RegionPortal      one end of a portal inside a region scene: a trigger volume and the arrival pose
//
// Every asset carries one serialized authoring id minted once (see GameCore.Gameplay.Entities.AuthoringIdField).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using UnityEngine;

namespace GameCore.Gameplay.World
{
    /// <summary>One region of a world.</summary>
    [Authorable("world.region", DisplayName = "Region", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "A streamed region: one additive scene with its own composition scope.")]
    [CreateAssetMenu(menuName = "GameCore/Gameplay/Region Definition", fileName = "Region")]
    public sealed class RegionDefinition : ScriptableObject, IDefinitionAsset
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorField(Doc = "Display name.")]
        [SerializeField] private string displayName = string.Empty;

        [AuthorField(Type = "artifact", Doc = "Project-relative path of the region scene (Assets/.../X.unity).")]
        [SerializeField] private string scenePath = string.Empty;

        [SerializeField] private string contentStamp = string.Empty;

        public string AuthoringId => authoringId;

        public string DefinitionName => string.IsNullOrEmpty(displayName) ? name : displayName;

        public string ContentStamp => contentStamp;

        public string DisplayName => DefinitionName;

        public string ScenePath => scenePath;

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

    /// <summary>One undirected portal connection between two regions.</summary>
    [Authorable("world.portal", DisplayName = "Portal", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "An undirected connection between two regions; each region scene holds one RegionPortal end of it.")]
    [CreateAssetMenu(menuName = "GameCore/Gameplay/Portal Definition", fileName = "Portal")]
    public sealed class PortalDefinition : ScriptableObject, IDefinitionAsset
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorRef(Category = "world.region", Doc = "First region.")]
        [SerializeField] private RegionDefinition? regionA;

        [AuthorRef(Category = "world.region", Doc = "Second region.")]
        [SerializeField] private RegionDefinition? regionB;

        [SerializeField] private string contentStamp = string.Empty;

        public string AuthoringId => authoringId;

        public string DefinitionName => name;

        public string ContentStamp => contentStamp;

        public RegionDefinition? RegionA => regionA;

        public RegionDefinition? RegionB => regionB;

        /// <summary>The region on the other side of <paramref name="from"/>, or null when the portal does not touch it.</summary>
        public RegionDefinition? Other(RegionDefinition from)
        {
            if (from == regionA)
            {
                return regionB;
            }

            return from == regionB ? regionA : null;
        }

        public bool EnsureAuthoringId()
        {
            string next = AuthoringIdField.Ensure(authoringId);
            bool changed = !string.Equals(next, authoringId, StringComparison.Ordinal);
            authoringId = next;
            return changed;
        }

        public void Connect(RegionDefinition a, RegionDefinition b)
        {
            regionA = a;
            regionB = b;
        }

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

    /// <summary>The regions and portals of one world.</summary>
    [Authorable("world.definition", DisplayName = "World", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "A world: its regions, portals, start region and streaming options.")]
    [CreateAssetMenu(menuName = "GameCore/Gameplay/World Definition", fileName = "World")]
    public sealed class WorldDefinition : ScriptableObject, IDefinitionAsset
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorRef(Category = "world.region", Doc = "Regions of the world.")]
        [SerializeField] private List<RegionDefinition> regions = new List<RegionDefinition>();

        [AuthorRef(Category = "world.portal", Required = false, Doc = "Portal connections between regions.")]
        [SerializeField] private List<PortalDefinition> portals = new List<PortalDefinition>();

        [AuthorRef(Category = "world.region", Doc = "The region the game starts in.")]
        [SerializeField] private RegionDefinition? startRegion;

        [AuthorField(Doc = "Keep the neighbours of the focus region resident as well.")]
        [SerializeField] private bool preloadNeighbours;

        [AuthorField(Doc = "Authoring id of the placed entity the streamer follows (the traveller).")]
        [SerializeField] private string focusEntityId = string.Empty;

        [SerializeField] private string contentStamp = string.Empty;

        public string AuthoringId => authoringId;

        public string DefinitionName => name;

        public string ContentStamp => contentStamp;

        public IReadOnlyList<RegionDefinition> Regions => regions;

        public IReadOnlyList<PortalDefinition> Portals => portals;

        public RegionDefinition? StartRegion => startRegion;

        public bool PreloadNeighbours => preloadNeighbours;

        public string FocusEntityId => focusEntityId;

        public bool EnsureAuthoringId()
        {
            string next = AuthoringIdField.Ensure(authoringId);
            bool changed = !string.Equals(next, authoringId, StringComparison.Ordinal);
            authoringId = next;
            return changed;
        }

        public void AddRegion(RegionDefinition region)
        {
            if (region != null && !regions.Contains(region))
            {
                regions.Add(region);
            }
        }

        public void AddPortal(PortalDefinition portal)
        {
            if (portal != null && !portals.Contains(portal))
            {
                portals.Add(portal);
            }
        }

        public void SetStartRegion(RegionDefinition? region) => startRegion = region;

        public void SetPreloadNeighbours(bool value) => preloadNeighbours = value;

        public void SetFocusEntity(string entityId) => focusEntityId = entityId ?? string.Empty;

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

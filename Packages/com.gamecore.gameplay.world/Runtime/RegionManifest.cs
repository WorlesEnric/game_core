// GameCore.Gameplay.World - the baked region manifest (P1.1).
//
// Written only by the bake (GameCore.Gameplay.Compile.Entry.Bake) and read at play time by WorldBuilder. It is the
// complete, integer-only description of the authored world: regions with their scenes and stable keys, portals with
// both arrival poses, definitions with their content stamps (whose first eight bytes are the recipe revision) and
// every placed entity with its region, definition, pose (mm, mrad), variant, scale and overrides. A running game never
// reads a region scene to learn what exists in it.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using UnityEngine;

namespace GameCore.Gameplay.World
{
    [Serializable]
    public sealed class ManifestRegion
    {
        public string authoringId = string.Empty;
        public string name = string.Empty;
        public string scenePath = string.Empty;
        public int key;
        public int spawnX;
        public int spawnY;
        public int spawnZ;
        public int spawnYaw;
    }

    [Serializable]
    public sealed class ManifestPortal
    {
        public string authoringId = string.Empty;
        public string name = string.Empty;
        public int key;
        public string regionA = string.Empty;
        public string regionB = string.Empty;
        public int arrivalAX;
        public int arrivalAY;
        public int arrivalAZ;
        public int arrivalAYaw;
        public int arrivalBX;
        public int arrivalBY;
        public int arrivalBZ;
        public int arrivalBYaw;
    }

    [Serializable]
    public sealed class ManifestDefinition
    {
        public string authoringId = string.Empty;
        public string name = string.Empty;
        public EntityDefinition? definition;
        public string contentStamp = string.Empty;
        public int variantCount = 1;

        /// <summary>The exact recipe revision: the first eight bytes of the content stamp.</summary>
        public ulong Revision => AuthoringIds.RevisionOfContentStamp(contentStamp);
    }

    [Serializable]
    public sealed class ManifestEntity
    {
        public string authoringId = string.Empty;
        public string name = string.Empty;
        public string regionId = string.Empty;
        public string definitionId = string.Empty;
        public int x;
        public int y;
        public int z;
        public int yaw;
        public int variant;
        public int scaleMilli = GameplayUnits.ScaleOne;
        public bool visible = true;
        public bool alive = true;
        public List<OverrideEntry> overrides = new List<OverrideEntry>();
    }

    /// <summary>The baked, integer-only world description WorldBuilder boots from.</summary>
    public sealed class RegionManifest : ScriptableObject
    {
        public const string Format = "gamecore.gameplay-manifest/1";

        [SerializeField] private string format = Format;
        [SerializeField] private string worldId = string.Empty;
        [SerializeField] private string worldName = string.Empty;
        [SerializeField] private string startRegionId = string.Empty;
        [SerializeField] private string focusEntityId = string.Empty;
        [SerializeField] private bool preloadNeighbours;
        [SerializeField] private string catalogFingerprint = string.Empty;
        [SerializeField] private string bakeReportHash = string.Empty;
        [SerializeField] private List<ManifestRegion> regions = new List<ManifestRegion>();
        [SerializeField] private List<ManifestPortal> portals = new List<ManifestPortal>();
        [SerializeField] private List<ManifestDefinition> definitions = new List<ManifestDefinition>();
        [SerializeField] private List<ManifestEntity> entities = new List<ManifestEntity>();

        public string FormatId => format;

        public string WorldId => worldId;

        public string WorldName => worldName;

        public string StartRegionId => startRegionId;

        public string FocusEntityId => focusEntityId;

        public bool PreloadNeighbours => preloadNeighbours;

        /// <summary>Lowercase hex fingerprint of the generated catalog this manifest was baked with.</summary>
        public string CatalogFingerprint => catalogFingerprint;

        /// <summary>SHA-256 of the bake report text: the identity of this bake.</summary>
        public string BakeReportHash => bakeReportHash;

        public IReadOnlyList<ManifestRegion> Regions => regions;

        public IReadOnlyList<ManifestPortal> Portals => portals;

        public IReadOnlyList<ManifestDefinition> Definitions => definitions;

        public IReadOnlyList<ManifestEntity> Entities => entities;

        public ManifestRegion? FindRegion(string authoringId)
        {
            for (int i = 0; i < regions.Count; i++)
            {
                if (string.Equals(regions[i].authoringId, authoringId, StringComparison.Ordinal))
                {
                    return regions[i];
                }
            }

            return null;
        }

        public ManifestRegion? FindRegionByKey(int key)
        {
            for (int i = 0; i < regions.Count; i++)
            {
                if (regions[i].key == key)
                {
                    return regions[i];
                }
            }

            return null;
        }

        public ManifestDefinition? FindDefinition(string authoringId)
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                if (string.Equals(definitions[i].authoringId, authoringId, StringComparison.Ordinal))
                {
                    return definitions[i];
                }
            }

            return null;
        }

        public ManifestEntity? FindEntity(string authoringId)
        {
            for (int i = 0; i < entities.Count; i++)
            {
                if (string.Equals(entities[i].authoringId, authoringId, StringComparison.Ordinal))
                {
                    return entities[i];
                }
            }

            return null;
        }

        /// <summary>Replaces the whole content (the bake's only write path).</summary>
        public void Assign(
            string world,
            string name,
            string startRegion,
            string focusEntity,
            bool preload,
            string fingerprint,
            string reportHash,
            List<ManifestRegion> regionList,
            List<ManifestPortal> portalList,
            List<ManifestDefinition> definitionList,
            List<ManifestEntity> entityList)
        {
            format = Format;
            worldId = world ?? string.Empty;
            worldName = name ?? string.Empty;
            startRegionId = startRegion ?? string.Empty;
            focusEntityId = focusEntity ?? string.Empty;
            preloadNeighbours = preload;
            catalogFingerprint = fingerprint ?? string.Empty;
            bakeReportHash = reportHash ?? string.Empty;
            regions = regionList ?? new List<ManifestRegion>();
            portals = portalList ?? new List<ManifestPortal>();
            definitions = definitionList ?? new List<ManifestDefinition>();
            entities = entityList ?? new List<ManifestEntity>();
        }
    }
}

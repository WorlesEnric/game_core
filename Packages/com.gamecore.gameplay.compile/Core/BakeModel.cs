// GameCore.Gameplay.Compile - the Unity-free bake model (P1.1).
//
// The editor half reads authored assets and region scenes into this model; everything after that (validation, the
// canonical order, the world bake report, the catalog description) is pure and runs under plain dotnet as well. All
// geometry is already integer (mm, mrad, milli) when it reaches the model, so the model never sees a float.
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.Gameplay.Compile
{
    /// <summary>One authored entity definition.</summary>
    public sealed class BakedDefinition
    {
        public string AuthoringId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        /// <summary>Lowercase hex SHA-256 of the definition's canonical authorable fields.</summary>
        public string ContentHash { get; set; } = string.Empty;

        /// <summary>
        /// Lowercase hex SHA-256 of the definition's structural fields only (P1.7a A8, SADR-012 studio); empty means the
        /// whole content is structural (the recipe then derives from <see cref="ContentHash"/>).
        /// </summary>
        public string StructuralHash { get; set; } = string.Empty;

        /// <summary>The hash the recipe revision and the catalog recipe implementation id derive from.</summary>
        public string RecipeHash => StructuralHash.Length == 64 ? StructuralHash : ContentHash;

        /// <summary>Number of variants (at least one: variant 0 is the base definition).</summary>
        public int VariantCount { get; set; } = 1;

        /// <summary>Authoring ids of the definition's variant definitions, in variant order (index 1 onward).</summary>
        public List<string> VariantIds { get; } = new List<string>();

        /// <summary>Names of the overridable fields the definition declares.</summary>
        public List<string> OverridableFields { get; } = new List<string>();
    }

    /// <summary>One authored region.</summary>
    public sealed class BakedRegion
    {
        public string AuthoringId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        /// <summary>Project-relative path of the region scene.</summary>
        public string ScenePath { get; set; } = string.Empty;

        public int SpawnX { get; set; }

        public int SpawnY { get; set; }

        public int SpawnZ { get; set; }

        public int SpawnYaw { get; set; }

        /// <summary>Bounds centre and half extents (mm).</summary>
        public int BoundsX { get; set; }

        public int BoundsY { get; set; }

        public int BoundsZ { get; set; }

        public int ExtentX { get; set; }

        public int ExtentY { get; set; }

        public int ExtentZ { get; set; }
    }

    /// <summary>One undirected portal connection between two regions.</summary>
    public sealed class BakedPortal
    {
        public string AuthoringId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string RegionA { get; set; } = string.Empty;

        public string RegionB { get; set; } = string.Empty;

        /// <summary>Arrival pose of a traveller entering <see cref="RegionA"/> through this portal.</summary>
        public BakedPose ArrivalA { get; set; } = new BakedPose();

        /// <summary>Arrival pose of a traveller entering <see cref="RegionB"/> through this portal.</summary>
        public BakedPose ArrivalB { get; set; } = new BakedPose();

        public bool HasEndA { get; set; }

        public bool HasEndB { get; set; }
    }

    /// <summary>An integer pose: position in mm, yaw in mrad.</summary>
    public sealed class BakedPose
    {
        public BakedPose()
        {
        }

        public BakedPose(int x, int y, int z, int yaw)
        {
            X = x;
            Y = y;
            Z = z;
            Yaw = yaw;
        }

        public int X { get; set; }

        public int Y { get; set; }

        public int Z { get; set; }

        public int Yaw { get; set; }
    }

    /// <summary>One authored, placed entity.</summary>
    public sealed class BakedEntity
    {
        public string AuthoringId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string RegionId { get; set; } = string.Empty;

        public string DefinitionId { get; set; } = string.Empty;

        public int X { get; set; }

        public int Y { get; set; }

        public int Z { get; set; }

        public int Yaw { get; set; }

        public int Variant { get; set; }

        public int ScaleMilli { get; set; } = 1000;

        public bool Visible { get; set; } = true;

        public bool Alive { get; set; } = true;

        /// <summary>Override field name to canonical value text.</summary>
        public SortedDictionary<string, string> Overrides { get; } = new SortedDictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>The whole authored world, as the bake read it.</summary>
    public sealed class BakedWorld
    {
        public string WorldId { get; set; } = string.Empty;

        public string WorldName { get; set; } = string.Empty;

        public string StartRegionId { get; set; } = string.Empty;

        /// <summary>Authoring id of the entity the region streamer follows (the debug traveller); may be empty.</summary>
        public string FocusEntityId { get; set; } = string.Empty;

        /// <summary>When true the streamer keeps the neighbours of the focus region resident as well.</summary>
        public bool PreloadNeighbours { get; set; }

        public List<BakedDefinition> Definitions { get; } = new List<BakedDefinition>();

        public List<BakedRegion> Regions { get; } = new List<BakedRegion>();

        public List<BakedPortal> Portals { get; } = new List<BakedPortal>();

        public List<BakedEntity> Entities { get; } = new List<BakedEntity>();

        /// <summary>Sorts every list into the canonical order: ordinal authoring id (entities: region, then id).</summary>
        public void Canonicalize()
        {
            Definitions.Sort((l, r) => string.CompareOrdinal(l.AuthoringId, r.AuthoringId));
            Regions.Sort((l, r) => string.CompareOrdinal(l.AuthoringId, r.AuthoringId));
            Portals.Sort((l, r) => string.CompareOrdinal(l.AuthoringId, r.AuthoringId));
            Entities.Sort((l, r) =>
            {
                int byRegion = string.CompareOrdinal(l.RegionId, r.RegionId);
                return byRegion != 0 ? byRegion : string.CompareOrdinal(l.AuthoringId, r.AuthoringId);
            });
        }

        public BakedDefinition? FindDefinition(string authoringId)
        {
            for (int i = 0; i < Definitions.Count; i++)
            {
                if (string.Equals(Definitions[i].AuthoringId, authoringId, StringComparison.Ordinal))
                {
                    return Definitions[i];
                }
            }

            return null;
        }

        public BakedRegion? FindRegion(string authoringId)
        {
            for (int i = 0; i < Regions.Count; i++)
            {
                if (string.Equals(Regions[i].AuthoringId, authoringId, StringComparison.Ordinal))
                {
                    return Regions[i];
                }
            }

            return null;
        }

        public BakedPortal? FindPortal(string authoringId)
        {
            for (int i = 0; i < Portals.Count; i++)
            {
                if (string.Equals(Portals[i].AuthoringId, authoringId, StringComparison.Ordinal))
                {
                    return Portals[i];
                }
            }

            return null;
        }
    }
}

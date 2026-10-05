// GameCore.Gameplay.Compile - the world bake report (gamecore.gameplay-bake/1).
//
// The report is the canonical, diffable text of one bake: every region, portal, definition and entity with the
// kernel identities derived from it (target ids, stable keys, definition revisions). It is written next to the
// RegionManifest asset and compared byte for byte by Entry.Verify, so "the bake is up to date" is a text comparison
// rather than a judgement about Unity YAML.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Gameplay.Contracts;

namespace GameCore.Gameplay.Compile
{
    /// <summary>Writes the canonical bake report of a world.</summary>
    public static class BakeReportWriter
    {
        public const string FormatId = "gamecore.gameplay-bake/1";

        /// <summary>Writes the report; the world is canonicalized first.</summary>
        public static string Write(BakedWorld world, string catalogFingerprint)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            world.Canonicalize();
            var json = new CanonicalJson();
            json.BeginObject();
            json.String("format", FormatId);
            json.String("worldId", world.WorldId);
            json.String("worldName", world.WorldName);
            json.String("startRegionId", world.StartRegionId);
            json.String("focusEntityId", world.FocusEntityId);
            json.Bool("preloadNeighbours", world.PreloadNeighbours);
            json.String("catalogFingerprint", catalogFingerprint ?? string.Empty);

            json.BeginArray("definitions");
            for (int i = 0; i < world.Definitions.Count; i++)
            {
                BakedDefinition definition = world.Definitions[i];
                json.BeginObject();
                json.String("id", definition.AuthoringId);
                json.String("name", definition.Name);
                json.String("contentHash", definition.ContentHash);
                json.String("structuralHash", definition.RecipeHash);
                json.Number("revision", definition.RecipeHash.Length >= 16 ? DefinitionHashing.RevisionOf(definition.RecipeHash) : 0UL);
                json.Number("variantCount", definition.VariantCount);
                Strings(json, "variants", definition.VariantIds);
                var fields = new List<string>(definition.OverridableFields);
                fields.Sort(StringComparer.Ordinal);
                Strings(json, "overridableFields", fields);
                json.EndObject();
            }

            json.EndArray();

            json.BeginArray("regions");
            for (int i = 0; i < world.Regions.Count; i++)
            {
                BakedRegion region = world.Regions[i];
                json.BeginObject();
                json.String("id", region.AuthoringId);
                json.String("name", region.Name);
                json.Number("key", KeyOf(region.AuthoringId));
                json.String("target", TargetHex(region.AuthoringId));
                json.String("scene", region.ScenePath);
                json.String("spawn", Pose(region.SpawnX, region.SpawnY, region.SpawnZ, region.SpawnYaw));
                json.String("bounds", Triple(region.BoundsX, region.BoundsY, region.BoundsZ) + " +- "
                    + Triple(region.ExtentX, region.ExtentY, region.ExtentZ));
                json.EndObject();
            }

            json.EndArray();

            json.BeginArray("portals");
            for (int i = 0; i < world.Portals.Count; i++)
            {
                BakedPortal portal = world.Portals[i];
                json.BeginObject();
                json.String("id", portal.AuthoringId);
                json.String("name", portal.Name);
                json.Number("key", KeyOf(portal.AuthoringId));
                json.String("regionA", portal.RegionA);
                json.String("regionB", portal.RegionB);
                json.String("arrivalA", Pose(portal.ArrivalA.X, portal.ArrivalA.Y, portal.ArrivalA.Z, portal.ArrivalA.Yaw));
                json.String("arrivalB", Pose(portal.ArrivalB.X, portal.ArrivalB.Y, portal.ArrivalB.Z, portal.ArrivalB.Yaw));
                json.EndObject();
            }

            json.EndArray();

            json.BeginArray("entities");
            for (int i = 0; i < world.Entities.Count; i++)
            {
                BakedEntity entity = world.Entities[i];
                json.BeginObject();
                json.String("id", entity.AuthoringId);
                json.String("name", entity.Name);
                json.String("target", TargetHex(entity.AuthoringId));
                json.String("region", entity.RegionId);
                json.String("definition", entity.DefinitionId);
                json.String("pose", Pose(entity.X, entity.Y, entity.Z, entity.Yaw));
                json.Number("variant", entity.Variant);
                json.Number("scaleMilli", entity.ScaleMilli);
                json.Bool("visible", entity.Visible);
                json.Bool("alive", entity.Alive);
                json.BeginArray("overrides");
                foreach (KeyValuePair<string, string> item in entity.Overrides)
                {
                    json.StringItem(item.Key + "=" + item.Value);
                }

                json.EndArray();
                json.EndObject();
            }

            json.EndArray();
            json.EndObject();
            return json.ToString();
        }

        private static void Strings(CanonicalJson json, string name, IReadOnlyList<string> values)
        {
            if (values.Count == 0)
            {
                json.EmptyArray(name);
                return;
            }

            json.BeginArray(name);
            for (int i = 0; i < values.Count; i++)
            {
                json.StringItem(values[i]);
            }

            json.EndArray();
        }

        private static long KeyOf(string id) => AuthoringIds.IsValid(id) ? AuthoringIds.StableKey(id) : 0L;

        private static string TargetHex(string id) =>
            AuthoringIds.IsValid(id) ? GameplayIds.Hex(AuthoringIds.TargetIdFor(id).Value) : string.Empty;

        private static string Triple(int x, int y, int z) =>
            x.ToString(CultureInfo.InvariantCulture) + "," + y.ToString(CultureInfo.InvariantCulture) + ","
            + z.ToString(CultureInfo.InvariantCulture);

        private static string Pose(int x, int y, int z, int yaw) =>
            Triple(x, y, z) + "@" + yaw.ToString(CultureInfo.InvariantCulture);
    }
}

// GameCore.Studio.Views - W-VIEW-04 model: a world definition's regions and portals from the index, the open region
// scenes' markers (spawn point), NPC schedules, and the World view's change sets.
//
// Edits name P1.1's world.* tools when the engine can bind them (ToolBinding). Today it cannot (see ToolBinding), so:
//   connect regions -> create world.portal {regionA, regionB} + set world.portals (two ops, the second depends on the
//                      first and names the new portal by a pre-minted authoring id)
//   set spawn point -> move the region's SpawnPoint object (when it exists), else world.setSpawnPoint
//   add portal      -> world.addPortal (no generic equivalent: the portal end is a non-authorable component); the
//                      engine refuses it until the tool binds its region parameter (PACKET.md, left open)
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameCore.Studio.Views
{
    public sealed class WorldRegion
    {
        public WorldRegion(string key, AuthoringRef reference, string name, string scenePath)
        {
            Key = key;
            Ref = reference;
            Name = name;
            ScenePath = scenePath;
        }

        public string Key { get; }

        public AuthoringRef Ref { get; }

        public string AuthoringId => Ref.AuthoringId ?? string.Empty;

        public string Name { get; }

        public string ScenePath { get; }

        public bool IsStart { get; set; }

        /// <summary>Resident / Unloaded (Edit Mode: scene open; Play Mode: the streamer).</summary>
        public string Residency { get; set; } = string.Empty;

        /// <summary>Authored objects the index holds for the region (its scene must be open to be indexed).</summary>
        public int EntityCount { get; set; }

        public bool SceneOpen { get; set; }

        /// <summary>The region marker's spawn point, when the scene is open.</summary>
        public Vector3? Spawn { get; set; }

        /// <summary>The marker component (open scenes only).</summary>
        public Component? Marker { get; set; }
    }

    public sealed class WorldPortal
    {
        public WorldPortal(string key, AuthoringRef reference, string name, string? regionA, string? regionB)
        {
            Key = key;
            Ref = reference;
            Name = name;
            RegionA = regionA;
            RegionB = regionB;
        }

        public string Key { get; }

        public AuthoringRef Ref { get; }

        public string Name { get; }

        public string? RegionA { get; }

        public string? RegionB { get; }
    }

    public sealed class SchedulePhase
    {
        public SchedulePhase(string name, float start, string behaviour)
        {
            Name = name;
            Start = start;
            Behaviour = behaviour;
        }

        public string Name { get; }

        public float Start { get; }

        public string Behaviour { get; }
    }

    public sealed class NpcSchedule
    {
        public NpcSchedule(string npc, float dayLength, IReadOnlyList<SchedulePhase> phases)
        {
            Npc = npc;
            DayLength = dayLength;
            Phases = phases;
        }

        public string Npc { get; }

        public float DayLength { get; }

        public IReadOnlyList<SchedulePhase> Phases { get; }
    }

    public sealed class WorldDocument
    {
        public const string Type = "world.definition";

        private WorldDocument(string key, AuthoringRef reference, string name, IReadOnlyList<WorldRegion> regions, IReadOnlyList<WorldPortal> portals, IReadOnlyList<NpcSchedule> schedules)
        {
            Key = key;
            Ref = reference;
            Name = name;
            Regions = regions;
            Portals = portals;
            Schedules = schedules;
        }

        public string Key { get; }

        public AuthoringRef Ref { get; }

        public string Name { get; }

        public IReadOnlyList<WorldRegion> Regions { get; }

        public IReadOnlyList<WorldPortal> Portals { get; }

        public IReadOnlyList<NpcSchedule> Schedules { get; }

        public WorldRegion? Region(string key)
        {
            foreach (WorldRegion region in Regions)
            {
                if (string.Equals(region.Key, key, StringComparison.Ordinal))
                {
                    return region;
                }
            }

            return null;
        }

        /// <summary>The portal connecting two regions (either direction), or null.</summary>
        public WorldPortal? Between(string a, string b)
        {
            foreach (WorldPortal portal in Portals)
            {
                if ((portal.RegionA == a && portal.RegionB == b) || (portal.RegionA == b && portal.RegionB == a))
                {
                    return portal;
                }
            }

            return null;
        }

        /// <summary>
        /// The world <paramref name="worldKey"/> (or the first world in the index): its regions and portals from the index
        /// refs, residency and spawn points from the open scenes, entity counts from the region contains edges.
        /// </summary>
        public static WorldDocument? Load(StudioViewContext context, IndexGraph graph, string? worldKey = null)
        {
            IndexNode? world = worldKey == null ? null : graph.Node(worldKey);
            if (world == null)
            {
                IReadOnlyList<IndexNode> worlds = graph.OfType(Type);
                world = worlds.Count > 0 ? worlds[0] : null;
            }

            if (world == null)
            {
                return null;
            }

            Dictionary<string, Component> markers = OpenMarkers(context.Runtime);
            AuthoringRef? start = IndexGraph.RefField(world, "startRegion");
            string? startKey = start == null ? null : Canonical(graph, start);
            List<WorldRegion> regions = new List<WorldRegion>();
            foreach (AuthoringRef reference in IndexGraph.RefsField(world, "regions"))
            {
                string key = Canonical(graph, reference);
                IndexNode? node = graph.Node(key);
                string name = node == null ? IndexGraph.LeafOf(reference) : (IndexGraph.StringField(node, "displayName") is string display && display.Length > 0 ? display : graph.NameOf(key));
                string scenePath = node == null ? string.Empty : IndexGraph.StringField(node, "scenePath") ?? string.Empty;
                WorldRegion region = new WorldRegion(key, node?.Ref ?? reference, name, scenePath)
                {
                    IsStart = key == startKey,
                    Residency = ViewSupport.Residency(context, graph, key),
                    EntityCount = CountContained(graph, key),
                };
                Scene scene = scenePath.Length == 0 ? default : SceneManager.GetSceneByPath(scenePath);
                region.SceneOpen = scene.IsValid() && scene.isLoaded;
                if (region.AuthoringId.Length > 0 && markers.TryGetValue(region.AuthoringId, out Component? marker))
                {
                    region.Marker = marker;
                    region.Spawn = SpawnOf(marker);
                }

                regions.Add(region);
            }

            List<WorldPortal> portals = new List<WorldPortal>();
            foreach (AuthoringRef reference in IndexGraph.RefsField(world, "portals"))
            {
                string key = Canonical(graph, reference);
                IndexNode? node = graph.Node(key);
                AuthoringRef? a = node == null ? null : IndexGraph.RefField(node, "regionA");
                AuthoringRef? b = node == null ? null : IndexGraph.RefField(node, "regionB");
                portals.Add(new WorldPortal(key, node?.Ref ?? reference, graph.NameOf(key), a == null ? null : Canonical(graph, a), b == null ? null : Canonical(graph, b)));
            }

            return new WorldDocument(world.Ref.IdentityKey, world.Ref, graph.NameOf(world.Ref.IdentityKey), regions, portals, Schedules(context.Runtime, graph));
        }

        private static int CountContained(IndexGraph graph, string regionKey)
        {
            int count = 0;
            Queue<string> queue = new Queue<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal) { regionKey };
            queue.Enqueue(regionKey);
            while (queue.Count > 0)
            {
                foreach (GraphLink link in graph.Outgoing(queue.Dequeue()))
                {
                    if (link.Kind == EdgeKind.Contains && seen.Add(link.ToKey))
                    {
                        count++;
                        queue.Enqueue(link.ToKey);
                    }
                }
            }

            return count;
        }

        /// <summary>Region markers (components implementing IAuthoredRegion) of the open scenes, by region authoring id.</summary>
        public static Dictionary<string, Component> OpenMarkers(StudioRuntime runtime)
        {
            Dictionary<string, Component> markers = new Dictionary<string, Component>(StringComparer.Ordinal);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (behaviour == null || !NestedReferenceContributor.ImplementsRegion(behaviour.GetType()))
                        {
                            continue;
                        }

                        PropertyInfo? id = behaviour.GetType().GetProperty("AuthoringId", BindingFlags.Public | BindingFlags.Instance);
                        if (id != null && id.GetValue(behaviour) is string authoringId && authoringId.Length > 0 && !markers.ContainsKey(authoringId))
                        {
                            markers.Add(authoringId, behaviour);
                        }
                    }
                }
            }

            return markers;
        }

        public static Vector3? SpawnOf(Component marker)
        {
            PropertyInfo? spawn = marker.GetType().GetProperty("SpawnPoint", BindingFlags.Public | BindingFlags.Instance);
            return spawn != null && spawn.GetValue(marker) is Transform transform && transform != null ? transform.position : (Vector3?)null;
        }

        private static IReadOnlyList<NpcSchedule> Schedules(StudioRuntime runtime, IndexGraph graph)
        {
            List<NpcSchedule> schedules = new List<NpcSchedule>();
            foreach (IndexNode npc in graph.OfType("npc.definition"))
            {
                AuthoringRef? scheduleRef = IndexGraph.RefField(npc, "schedule");
                if (scheduleRef == null)
                {
                    continue;
                }

                UnityEngine.Object? schedule = runtime.Resolver.Find(scheduleRef);
                if (schedule == null)
                {
                    continue;
                }

                float day = AuthoredData.Float(AuthoredData.Read(runtime, schedule, "dayLengthSeconds"), 0f);
                List<SchedulePhase> phases = new List<SchedulePhase>();
                if (AuthoredData.Read(runtime, schedule, "phases") is JArray rawPhases)
                {
                    foreach (JToken phase in rawPhases)
                    {
                        phases.Add(new SchedulePhase(AuthoredData.Text(phase["name"]), AuthoredData.Float(phase["startSeconds"]), AuthoredData.Display(phase["behaviour"], graph)));
                    }
                }

                string name = IndexGraph.StringField(npc, "displayName") is string display && display.Length > 0 ? display : graph.NameOf(npc.Ref.IdentityKey);
                schedules.Add(new NpcSchedule(name, day, phases));
            }

            return schedules;
        }

        private static string Canonical(IndexGraph graph, AuthoringRef reference)
        {
            string key = reference.IdentityKey;
            if (graph.Node(key) != null)
            {
                return key;
            }

            foreach (IndexNode node in graph.Nodes)
            {
                if (node.Ref.SameTarget(reference))
                {
                    return node.Ref.IdentityKey;
                }
            }

            return key;
        }
    }

    /// <summary>The World view's operations (see the file header).</summary>
    public static class WorldEdits
    {
        public const string ConnectRegionsTool = "world.connectRegions";
        public const string AddPortalTool = "world.addPortal";
        public const string SetSpawnPointTool = "world.setSpawnPoint";

        /// <summary>Operations connecting two regions with a new portal.</summary>
        public static IReadOnlyList<Operation> ConnectRegions(StudioRuntime runtime, WorldDocument world, WorldRegion a, WorldRegion b)
        {
            if (ToolBinding.IsBindable(ConnectRegionsTool))
            {
                return new[]
                {
                    ViewEdits.Op("op1", ConnectRegionsTool, world.Ref, new JObject
                    {
                        ["regionA"] = StudioJson.ToToken(SemanticIndexService.EdgeRef(a.Ref)),
                        ["regionB"] = StudioJson.ToToken(SemanticIndexService.EdgeRef(b.Ref)),
                    }),
                };
            }

            string id = AuthoringIdentity.NewAuthoringId();
            string folder = PortalFolder(runtime, world);
            JObject fields = new JObject
            {
                ["regionA"] = StudioJson.ToToken(SemanticIndexService.EdgeRef(a.Ref)),
                ["regionB"] = StudioJson.ToToken(SemanticIndexService.EdgeRef(b.Ref)),
            };
            Operation create = ViewEdits.Op("op1", BuiltInToolIdsExt.Create, null, new JObject
            {
                ["type"] = "world.portal",
                ["name"] = Safe(a.Name) + "_" + Safe(b.Name),
                ["path"] = folder,
                ["authoringId"] = id,
                ["fields"] = fields,
            });
            JArray portals = new JArray();
            foreach (WorldPortal portal in world.Portals)
            {
                portals.Add(StudioJson.ToToken(SemanticIndexService.EdgeRef(portal.Ref)));
            }

            portals.Add(StudioJson.ToToken(new AuthoringRef(AuthoringKind.Definition, authoringId: id)));
            Operation list = ViewEdits.Op("op2", BuiltInToolIdsExt.Set, world.Ref, new JObject { ["field"] = "portals", ["value"] = portals }, new[] { "op1" });
            return new[] { create, list };
        }

        /// <summary>world.addPortal on <paramref name="portal"/> (the tool's reflected target) at a position in the region scene.</summary>
        public static Operation AddPortal(WorldPortal portal, Vector3 position, float yaw = 0f, float arrivalDistance = 2.5f)
        {
            return ViewEdits.Op("op1", AddPortalTool, portal.Ref, new JObject
            {
                ["position"] = new JArray(position.x, position.y, position.z),
                ["yaw"] = yaw,
                ["arrivalDistance"] = arrivalDistance,
            });
        }

        /// <summary>Moves the region's spawn point (see the file header).</summary>
        public static Operation SetSpawnPoint(StudioRuntime runtime, WorldRegion region, Vector3 position, float yaw = 0f)
        {
            Transform? spawn = region.Marker == null ? null : region.Marker.transform.Find("SpawnPoint");
            if (!ToolBinding.IsBindable(SetSpawnPointTool) && spawn != null)
            {
                AuthoringRef? target = runtime.Resolver.BuildRef(spawn.gameObject, null, true);
                if (target != null)
                {
                    Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
                    return ViewEdits.Op("op1", BuiltInToolIdsExt.Move, target, new JObject
                    {
                        ["position"] = new JArray(position.x, position.y, position.z),
                        ["rotation"] = new JArray(rotation.x, rotation.y, rotation.z, rotation.w),
                    });
                }
            }

            AuthoringRef markerRef = region.Marker == null ? region.Ref : runtime.Resolver.BuildRef(region.Marker, null, true) ?? region.Ref;
            return ViewEdits.Op("op1", SetSpawnPointTool, markerRef, new JObject
            {
                ["position"] = new JArray(position.x, position.y, position.z),
                ["yaw"] = yaw,
            });
        }

        private static string PortalFolder(StudioRuntime runtime, WorldDocument world)
        {
            UnityEngine.Object? asset = runtime.Resolver.Find(world.Ref);
            string path = asset == null ? string.Empty : AssetDatabase.GetAssetPath(asset);
            if (path.Length == 0)
            {
                return "Assets";
            }

            string directory = Path.GetDirectoryName(path)!.Replace('\\', '/');
            return AssetDatabase.IsValidFolder(directory + "/Portals") ? directory + "/Portals" : directory;
        }

        private static string Safe(string name)
        {
            char[] chars = (name ?? string.Empty).ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]))
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Logic.Editor;
using GameCore.Gameplay.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameCore.Gameplay.Dialogue.Editor
{
    /// <summary>Uses the same world-owned narrative closure as the bake; never infers ownership from asset paths.</summary>
    public static class DialogueContentClosure
    {
        public static GameplayContentSet ResolveForActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            RegionDefinition? region = null;
            int markers = 0;
            if (scene.IsValid() && scene.isLoaded)
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                foreach (AuthoredRegion marker in root.GetComponentsInChildren<AuthoredRegion>(true))
                {
                    markers++;
                    region = marker.Definition;
                }
            }
            if (markers != 1 || region == null)
                throw new ArgumentException("npc_dialogue_world_ambiguous: the active scene must name exactly one authored region.");
            WorldDefinition? owner = null;
            foreach (string guid in AssetDatabase.FindAssets("t:WorldDefinition"))
            {
                WorldDefinition world = AssetDatabase.LoadAssetAtPath<WorldDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (world == null) continue;
                foreach (RegionDefinition member in world.Regions)
                {
                    if (member != region) continue;
                    if (owner != null && owner != world)
                        throw new ArgumentException("npc_dialogue_world_ambiguous: the active region belongs to more than one world.");
                    owner = world;
                    break;
                }
            }
            if (owner == null)
                throw new ArgumentException("npc_dialogue_world_ambiguous: the active region has no owning world.");
            List<GameplayContentSet> sets = NarrativeBake.FindContentSets(owner, out _);
            if (sets.Count != 1)
                throw new ArgumentException("npc_dialogue_content_set_missing: world '" + owner.name + "' must have exactly one GameplayContentSet; found " + sets.Count + ".");
            return sets[0];
        }

        public static bool Contains(GameplayContentSet set, ScriptableObject graph)
        {
            var seen = new HashSet<int>();
            var pending = new Queue<ScriptableObject>();
            foreach (ScriptableObject definition in set.Definitions)
                if (definition is INarrativeDefinition && seen.Add(definition.GetInstanceID())) pending.Enqueue(definition);
            while (pending.Count != 0)
            {
                ScriptableObject definition = pending.Dequeue();
                if (definition == graph) return true;
                foreach (ScriptableObject referenced in NarrativeBake.References(definition))
                    if (referenced is INarrativeDefinition && seen.Add(referenced.GetInstanceID())) pending.Enqueue(referenced);
            }
            return false;
        }

        public static void Enroll(GameplayContentSet set, DialogueGraphDefinition graph)
        {
            if (Contains(set, graph)) return;
            Undo.RecordObject(set, "Enroll dialogue graph");
            set.Add(graph);
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssetIfDirty(set);
        }
    }
}

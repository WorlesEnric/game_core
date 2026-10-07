#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
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
        /// <summary>Runs the ordinary read-only content-set bake validation with the source asset's path.</summary>
        public static IReadOnlyList<GameplayDiagnostic> ValidateProjectedSet(ScriptableObject definition, string originalPath)
        {
            if (!(definition is GameplayContentSet set))
                throw new ArgumentException("A projected GameplayContentSet is required.", nameof(definition));
            var diagnostics = new List<GameplayDiagnostic>();
            if (set.World == null)
            {
                diagnostics.Add(new GameplayDiagnostic(NarrativeDiagnosticCodes.ContentSetMissingWorld,
                    originalPath, set.name + " names no world"));
                return diagnostics;
            }
            NarrativeBake.Plan(set, originalPath, set.World.AuthoringId, diagnostics);
            return diagnostics;
        }

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

        public static bool Contains(GameplayContentSet set, ScriptableObject graph) => ContainsProposed(set, graph, null);

        /// <summary>Traverses final references without mutating unchanged members of the world's content closure.</summary>
        public static bool ContainsProposed(GameplayContentSet set, ScriptableObject graph,
            Func<UnityEngine.Object, UnityEngine.Object?>? projectReference)
        {
            ScriptableObject? proposedGraph = projectReference == null ? graph : projectReference(graph) as ScriptableObject;
            if (proposedGraph == null) return false;
            var seen = new HashSet<int>();
            var pending = new Queue<ScriptableObject>();
            foreach (ScriptableObject definition in set.Definitions) Enqueue(definition);
            while (pending.Count != 0)
            {
                ScriptableObject definition = pending.Dequeue();
                if (definition == proposedGraph) return true;
                foreach (ScriptableObject referenced in NarrativeBake.References(definition))
                    Enqueue(referenced);
            }
            return false;

            void Enqueue(ScriptableObject definition)
            {
                if (definition == null) return;
                ScriptableObject? proposed = projectReference == null ? definition : projectReference(definition) as ScriptableObject;
                if (proposed is INarrativeDefinition && seen.Add(proposed.GetInstanceID())) pending.Enqueue(proposed);
            }
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
